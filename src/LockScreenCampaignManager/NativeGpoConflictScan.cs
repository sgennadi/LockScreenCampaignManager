using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only, domain-scoped GPO/Registry.pol/GPP conflict discovery.
/// Scope is limited to the explicitly selected OU and its ancestors.
/// Does not calculate final RSoP, alter GPOs or run any remote code.
/// All LDAP reads are signed/sealed; alternate credentials also apply to SMB.
/// </summary>
internal static class NativeGpoConflictScan
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex Links = new(
        @"\[LDAP://(?<dn>[^;\]]+);(?<flags>[0-9]+)\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    internal sealed record Hit(string key, string name, string value, string risk, string reason);
    private sealed record GpoLink(string Dn, int Flags, int Order);
    private sealed record Level(string Dn, bool Blocked, int Depth, IReadOnlyList<GpoLink> Links);
    private sealed record ScanLevel(string dn, bool blocked, int depth);
    private sealed record RemoteRegistryResult(bool reachable, IReadOnlyList<Hit> values, string note);
    private sealed record Match(
        string gpo, string gpoId, string linkedAt, bool enforced, bool disabled,
        bool inheritanceBlocked, bool computerPartDisabled, bool userPartDisabled,
        bool wmiFilterPresent,
        string source, string key, string name, string value, string risk, string reason,
        string applicability);

    internal static JsonElement Execute(string mode, object request, DomainCredentials credentials)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows LDAP and SYSVOL access is required.");
        if (mode != "Scan")
            throw new ArgumentOutOfRangeException(nameof(mode), "Only simple read-only Scan is native.");
        var args = JsonSerializer.SerializeToElement(request, Json);
        string domain = Get(args, "domain").Trim().ToLowerInvariant();
        string dc = Get(args, "dc").Trim();
        NativeAdDiscovery.ValidateNames(domain, dc);
        if (dc.Length == 0) throw new ArgumentException("Choose an explicit domain controller.");
        string ouDn = Get(args, "ouDn").Trim();
        string computer = Get(args, "computer").Trim();
        bool userScope = IsUserPolicyScope(Get(args, "policyScope"));
        if (userScope && computer.Length > 0)
            throw new ArgumentException(
                "User Configuration requires a user gpresult/RSoP; remote computer HKLM is not user evidence.");
        if (computer.Length > 0 && !SafeComputerName(computer, domain))
            throw new ArgumentException("Computer must be a domain FQDN; no IP addresses or paths.");

        JsonElement Run()
        {
            using var ldap = NativeAdDiscovery.Connect(dc, credentials);
            ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            var root = NativeAdDiscovery.ReadRootDse(ldap);
            string baseDn = NativeAdDiscovery.Attr(root, "defaultNamingContext");
            string actualDc = NativeAdDiscovery.Attr(root, "dnsHostName");
            if (!string.Equals(NativeAdDiscovery.DomainFromDn(baseDn), domain,
                    StringComparison.OrdinalIgnoreCase) ||
                (!string.Equals(actualDc, dc, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(actualDc.Split('.')[0], dc, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("AD domain/DC identity mismatch.");
            if (!Inside(ouDn, baseDn) ||
                (!ouDn.Equals(baseDn, StringComparison.OrdinalIgnoreCase) &&
                 !ouDn.StartsWith("OU=", StringComparison.OrdinalIgnoreCase)) ||
                (ouDn.Length > 0 && ouDn.Any(char.IsControl)))
                throw new InvalidDataException("Selected OU is outside the verified AD domain.");

            var levels = ReadLevels(ldap, baseDn, ouDn);
            var found = new List<Match>();
            var notes = new List<string>();
            int skippedEmptyHalves = 0;
            int skippedInactiveMissingHalves = 0;
            foreach (var level in levels)
            {
                foreach (var link in level.Links)
                {
                    bool enabled = (link.Flags & 1) == 0;
                    bool enforced = (link.Flags & 2) != 0;
                    bool blockedBelow = levels.Any(x => x.Depth > level.Depth && x.Blocked);
                    bool inScope = enabled && (enforced || !blockedBelow);
                    if (!TryLinkedGuid(link.Dn, baseDn, out Guid id))
                        throw new InvalidDataException(
                            "A linked GPO is invalid or outside the selected domain; scan incomplete.");
                    var entry = ReadObject(ldap, link.Dn,
                        "(objectClass=groupPolicyContainer)", "displayName", "flags",
                        "gPCWQLFilter", "versionNumber", "gPCUserExtensionNames",
                        "gPCMachineExtensionNames");
                    string gpoName = NativeAdDiscovery.Attr(entry, "displayName");
                    if (gpoName.Length == 0) gpoName = id.ToString("D");
                    int flags = IntAttribute(entry, "flags");
                    int versionNumber = IntAttribute(entry, "versionNumber");
                    string extensionNames = NativeAdDiscovery.Attr(entry,
                        userScope ? "gPCUserExtensionNames" : "gPCMachineExtensionNames");
                    string wmi = NativeAdDiscovery.Attr(entry, "gPCWQLFilter");
                    bool partDisabled = IsPolicyPartDisabled(flags, userScope);
                    if (partDisabled) inScope = false;
                    if (wmi.Length > 0 && inScope)
                        notes.Add("WMI filter in " + SafeName(gpoName) +
                            " cannot be evaluated without client-side RSoP.");

                    // The SYSVOL path is pinned to the same verified DC used
                    // for LDAP. Real GPOs can have no User/Machine directory
                    // when that policy half has no configured settings at all.
                    // Check AD version + CSEs before treating absence as benign.
                    string policyFolder = userScope ? "User" : "Machine";
                    string gpoRoot = Path.Combine(@"\\", actualDc, "SYSVOL", domain,
                        "Policies", "{" + id.ToString("D").ToUpperInvariant() + "}");
                    EnsureDirectory(gpoRoot);
                    string policyRoot = Path.Combine(gpoRoot, policyFolder);
                    if (!PolicyFolderExistsSafe(policyRoot))
                    {
                        if (!inScope)
                        {
                            skippedInactiveMissingHalves++;
                            continue; // Disabled/non-applicable links cannot override this scope.
                        }
                        if (PolicyHalfHasConfiguredSettings(versionNumber,
                                extensionNames, userScope))
                            throw new InvalidDataException("Linked GPO " +
                                SafeName(gpoName) + " advertises " + policyFolder +
                                " Configuration in AD, but its SYSVOL folder is missing. " +
                                "Check AD/SYSVOL consistency on the selected DC.");
                        skippedEmptyHalves++;
                        continue; // Legitimately empty policy half.
                    }
                    EnsureDirectory(policyRoot);
                    foreach (string startup in new[]
                    {
                        @"Scripts\psscripts.ini", @"Scripts\scripts.ini"
                    })
                    {
                        string path = Path.Combine(policyRoot, startup);
                        if (FileExistsSafe(path) && inScope)
                            notes.Add((userScope ? "User logon/logoff" : "Computer startup") +
                                " script in " + SafeName(gpoName) +
                                " requires manual GPMC/gpresult verification.");
                    }
                    foreach (string source in new[]
                    {
                        "Registry.pol", @"Preferences\Registry\Registry.xml",
                        @"Preferences\PowerOptions\PowerOptions.xml"
                    })
                    {
                        string file = Path.Combine(policyRoot, source);
                        if (!FileExistsSafe(file))
                        {
                            if (inScope && source == "Registry.pol" &&
                                RegistryCseAdvertised(extensionNames))
                                throw new InvalidDataException("Linked GPO " +
                                    SafeName(gpoName) + " advertises Registry Policy CSE in AD " +
                                    "but Registry.pol is missing from " + policyFolder +
                                    " on the selected DC.");
                            continue;
                        }
                        if (source != "Registry.pol")
                        {
                            // Registry.xml itself is not a link, but ancestor
                            // folders could be a junction outside the GPO.
                            string registryFolder = Path.GetDirectoryName(file)!;
                            EnsureDirectory(Path.GetDirectoryName(registryFolder)!);
                            EnsureDirectory(registryFolder);
                        }
                        IReadOnlyList<Hit> hits;
                        try
                        {
                            byte[] bytes = File.ReadAllBytes(file);
                            hits = source == "Registry.pol"
                                ? ParseRegistryPol(bytes, userScope)
                                : source.EndsWith("PowerOptions.xml", StringComparison.Ordinal)
                                    ? userScope ? [] : ScreenSaverPowerConflictAnalysis.ParseGppPowerOptions(bytes)
                                    : ParseGppRegistry(bytes, userScope);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                            or InvalidDataException or XmlException or ArgumentException)
                        {
                            // Unknown evidence cannot be reported as a clean scan.
                            throw new InvalidDataException(
                                "Linked GPO registry settings cannot be fully inspected.", ex);
                        }
                        foreach (var hit in hits)
                        {
                            if (found.Count >= 500)
                                throw new InvalidDataException("GPO conflict scan result limit exceeded.");
                            found.Add(new Match(gpoName, id.ToString("D"), level.Dn,
                                enforced, !enabled, blockedBelow, (flags & 2) != 0,
                                (flags & 1) != 0, wmi.Length > 0, source, hit.key, hit.name,
                                Truncate(hit.value, 450),
                                inScope ? hit.risk : "Inactive/filtered", hit.reason,
                                "Estimated: security filtering, WMI, site/local policies and RSoP not fully evaluated."));
                        }
                    }
                }
            }
            if (skippedEmptyHalves > 0)
                notes.Add(skippedEmptyHalves + " GPO " +
                    (userScope ? "User" : "Computer") +
                    " policy halves had no configured AD settings and no SYSVOL folder; " +
                    "skipped as empty, not as conflicts.");
            if (skippedInactiveMissingHalves > 0)
                notes.Add(skippedInactiveMissingHalves +
                    " inactive/non-applicable linked policy halves had no SYSVOL folder; " +
                    "they cannot override this target scope.");
            object? remoteRegistry = null;
            if (computer.Length > 0)
            {
                var remote = ReadRemoteRegistry(computer);
                remoteRegistry = remote;
                if (!remote.reachable) notes.Add(remote.note);
            }
            int high = found.Count(x => x.risk == "High");
            int warningCount = found.Count(x => x.risk == "Warning");
            var result = new
            {
                domain, dc = actualDc, targetOu = ouDn,
                policyScope = userScope ? "User" : "Computer",
                scannedLevels = levels.Select(x => new ScanLevel(x.Dn, x.Blocked, x.Depth)),
                matches = found, count = found.Count, highRisk = high,
                warningsCount = warningCount, notes, remoteRegistry,
                scope = "Read-only " + (userScope ? "User" : "Computer") +
                    " Configuration static OU-link candidates, NOT Resultant Set of Policy (RSoP). " +
                    "Site/local policies, WMI, security filtering, GPP item-level targeting and " +
                    "scripts may change effective settings. Zero hits does not prove no conflict."
            };
            return JsonSerializer.SerializeToElement(new
            {
                success = true, message = "Native read-only GPO conflict scan complete.", data = result
            }, Json);
        }

        if (!credentials.IsAlternate) return Run();
        // LOGON_NETCREDENTIALS_ONLY ensures network SMB and remote Registry
        // use the same specified account as signed/sealed LDAP.
        string username = credentials.UserName.Trim();
        string? accountDomain = null;
        int slash = username.IndexOf('\\');
        if (slash > 0 && slash < username.Length - 1)
        {
            accountDomain = username[..slash];
            username = username[(slash + 1)..];
        }
        else if (!username.Contains('@'))
            username += "@" + credentials.Domain.Trim();
        if (credentials.Password.Length == 0 ||
            !LogonUser(username, accountDomain, credentials.Password,
                9, 3, out var token))
            throw new InvalidOperationException("Could not establish alternate network credentials for read-only GPO scan.");
        using (token)
            return WindowsIdentity.RunImpersonated(token, Run);
    }

    // Default Computer preserves the existing single-target scan API.
    // A typo must not silently select the wrong Registry.pol policy half.
    internal static bool IsUserPolicyScope(string? scope)
    {
        if (string.IsNullOrEmpty(scope) ||
            scope.Equals("Computer", StringComparison.OrdinalIgnoreCase))
            return false;
        if (scope.Equals("User", StringComparison.OrdinalIgnoreCase))
            return true;
        throw new ArgumentException("Policy scope must be User or Computer.", nameof(scope));
    }

    internal static bool IsPolicyPartDisabled(int gpoFlags, bool userScope) =>
        (gpoFlags & (userScope ? 1 : 2)) != 0;

    // GPO versionNumber = high 16 bits User, low 16 bits Computer.
    // AD may represent the 32-bit value as signed; preserve the raw bit layout.
    internal static bool PolicyHalfHasConfiguredSettings(
        int versionNumber, string? extensionNames, bool userScope)
    {
        uint version = unchecked((uint)versionNumber);
        uint halfVersion = userScope ? version >> 16 : version & 0xFFFF;
        return halfVersion != 0 || !string.IsNullOrWhiteSpace(extensionNames);
    }

    // MS-GPREG Registry Policy Client Side Extension, same for User/Computer.
    internal static bool RegistryCseAdvertised(string? extensionNames) =>
        (extensionNames ?? "").Contains(
            "{35378EAC-683F-11D2-A89A-00C04FBBCFA2}",
            StringComparison.OrdinalIgnoreCase);

    private static string Get(JsonElement args, string key) =>
        args.ValueKind == JsonValueKind.Object &&
        args.TryGetProperty(key, out var item) &&
        item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";

    internal static bool Inside(string dn, string parent) =>
        dn.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        dn.EndsWith("," + parent, StringComparison.OrdinalIgnoreCase);

    internal static bool SafeComputerName(string host, string domain) =>
        NativeDcConsistency.ValidController(host, domain) &&
        !IPAddress.TryParse(host, out _);

    internal static string? ParentDn(string dn)
    {
        for (int i = 0; i < dn.Length; i++)
        {
            if (dn[i] == '\\')
            {
                i++; // RFC4514 DN escaping, including \, and \HH sequences
                continue;
            }
            if (dn[i] == ',') return dn[(i + 1)..];
        }
        return null;
    }

    private static List<Level> ReadLevels(LdapConnection ldap, string baseDn, string ouDn)
    {
        var chain = new List<string>();
        string current = ouDn;
        for (int i = 0; i < 64; i++)
        {
            chain.Add(current);
            if (current.Equals(baseDn, StringComparison.OrdinalIgnoreCase)) break;
            current = ParentDn(current) ??
                throw new InvalidDataException("Cannot determine OU ancestry.");
            if (!Inside(current, baseDn))
                throw new InvalidDataException("OU ancestry left the verified domain.");
        }
        if (!chain[^1].Equals(baseDn, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("OU ancestry exceeds safety limit.");
        chain.Reverse();

        var levels = new List<Level>(chain.Count);
        for (int i = 0; i < chain.Count; i++)
        {
            string dn = chain[i];
            var entry = ReadObject(ldap, dn, "(objectClass=*)", "gPLink", "gPOptions");
            string links = NativeAdDiscovery.Attr(entry, "gPLink");
            var matches = Links.Matches(links);
            var parsed = new List<GpoLink>();
            for (int j = 0; j < matches.Count; j++)
            {
                System.Text.RegularExpressions.Match m = matches[j];
                if (!int.TryParse(m.Groups["flags"].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out int flags))
                    throw new InvalidDataException("Invalid GPO link state.");
                parsed.Add(new GpoLink(m.Groups["dn"].Value, flags, matches.Count - j));
            }
            string leftovers = Links.Replace(links, "");
            if (!string.IsNullOrWhiteSpace(leftovers))
                throw new InvalidDataException("Unrecognized or malformed linked GPO inventory.");
            int flagsOption = IntAttribute(entry, "gPOptions");
            levels.Add(new Level(dn, (flagsOption & 1) != 0, i, parsed));
        }
        return levels;
    }

    private static SearchResultEntry ReadObject(LdapConnection ldap, string dn,
        string filter, params string[] attributes)
    {
        var response = (SearchResponse)ldap.SendRequest(
            new SearchRequest(dn, filter, SearchScope.Base, attributes));
        if (response.Entries.Count != 1)
            throw new InvalidDataException("Linked AD object could not be uniquely verified.");
        return response.Entries[0];
    }

    private static int IntAttribute(SearchResultEntry entry, string key)
    {
        string raw = NativeAdDiscovery.Attr(entry, key);
        return raw.Length == 0 ? 0 :
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value : throw new InvalidDataException("Invalid GPO option attribute.");
    }

    internal static bool TryLinkedGuid(string dn, string baseDn, out Guid id)
    {
        id = Guid.Empty;
        string container = "CN=Policies,CN=System," + baseDn;
        if (!Inside(dn, container)) return false;
        var match = Regex.Match(dn, @"\ACN=\{(?<id>[0-9a-fA-F-]{36})\},CN=Policies,",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success && Guid.TryParseExact(match.Groups["id"].Value, "D", out id)
            && id != Guid.Empty;
    }

    // Only an actual not-found error is benign for an unconfigured half.
    // Access denied, broken network paths and reparse points are not.
    internal static bool PolicyFolderExistsSafe(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Selected DC GPO policy folder is unsafe.");
        return true;
    }

    internal static void EnsureDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Selected DC SYSVOL policy folder missing or unsafe.");
        // Fail closed if directory listing cannot be read.
        _ = directory.EnumerateFileSystemInfos().Take(1).ToArray();
    }

    internal static bool FileExistsSafe(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        // Access denied / network failure is not a legitimate "missing file".
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Linked GPO registry file is unsafe.");
        var info = new FileInfo(path);
        if (info.Length is < 1 or > 2097152)
            throw new InvalidDataException("Linked GPO registry file is empty or too large.");
        return true;
    }

    internal static Hit? TargetValue(string key, string name, string value,
        bool userScope = false)
    {
        string k = key.TrimStart('\\').ToLowerInvariant();
        string n = name.ToLowerInvariant();
        if (userScope)
        {
            // User Group Policy writes these REG_SZ values in HKCU.
            // GPP can also update the legacy HKCU\Control Panel\Desktop path.
            bool screenSaverKey = k is
                @"software\policies\microsoft\windows\control panel\desktop" or
                @"control panel\desktop";
            if (screenSaverKey && n is
                ("screensaveactive" or "scrnsave.exe" or
                 "screensavetimeout" or "screensaverissecure"))
                return new Hit(key, name, value, "High",
                    "Potential User Configuration Screen Saver overlap; check user gpresult/RSoP.");
            if (n == "nodispscrsavpage" && k ==
                @"software\microsoft\windows\currentversion\policies\system")
                return new Hit(key, name, value, "Warning",
                    "Screen Saver UI restriction (HKCU Policies\\System); " +
                    "does not itself prevent a screen saver from running.");
            return null;
        }
        // Loopback Replace can suppress the intended User OU Screen Saver policy
        // when a different Computer OU wins. Never claim applicability without
        // device/user RSoP, but surface Merge and Replace as domain candidates.
        if (k == @"software\policies\microsoft\windows\system" &&
            n == "userpolicymode")
            return new Hit(key, name, value, "Warning",
                "User Group Policy loopback processing. Replace may bypass User OU " +
                "Screen Saver GPO; verify Computer + User gpresult and security filters.");
        // Microsoft Power.admx: VIDEOIDLE under Computer Configuration. AC
        // and DC are seconds; 0 means Never, NOT an immediate screen-off.
        // A linked GPO here is only a candidate, never proof that the client
        // received the setting. The campaign idle comparison is separate.
        if (k == @"software\policies\microsoft\power\powersettings\3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e"
            && n is ("acsettingindex" or "dcsettingindex"))
            return new Hit(key, name, value, "Warning",
                "Computer display-off policy (" + (n.StartsWith("ac") ? "AC / plugged in" : "DC / battery") +
                "); compare with User Screen Saver idle timeout and confirm effective powercfg/RSoP.");
        if (k == @"software\policies\microsoft\power\powersettings\29f6c1db-86da-48c5-9fdb-f2b67b1f44da"
            && n is ("acsettingindex" or "dcsettingindex"))
            return new Hit(key, name, value, "Warning",
                "Computer sleep timeout (" + (n.StartsWith("ac") ? "AC" : "DC") +
                "); may occur before User Screen Saver. Confirm active power plan.");
        if (k == @"software\microsoft\windows\currentversion\policies\system" &&
            n == "inactivitytimeoutsecs")
            return new Hit(key, name, value, "Warning",
                "Machine inactivity lock timeout may change Screen Saver visibility; not proof of disabling .scr.");
        if ((n == "lockscreenimage" || n == "nochanginglockscreen" || n == "nolockscreen")
            && k.EndsWith(@"\policies\microsoft\windows\personalization", StringComparison.Ordinal))
        {
            return new Hit(key, name, value, n == "nochanginglockscreen" ? "Info" : "High",
                "Potential managed Lock Screen policy overlap");
        }
        if ((n == "rotatinglockscreenenabled" || n == "disablewindowsspotlightfeatures" ||
             n == "rotatinglockscreenoverlayenabled" || n == "configurewindowsspotlight" ||
             n == "disablecloudoptimizedcontent") &&
             (k.EndsWith(@"\cloudcontent", StringComparison.Ordinal) ||
              k.EndsWith(@"\personalization", StringComparison.Ordinal)))
        {
            return new Hit(key, name, value, "Warning",
                "Windows Spotlight / rotating Lock Screen setting");
        }
        return null;
    }

    internal static IReadOnlyList<Hit> ParseRegistryPol(byte[] contents,
        bool userScope = false)
    {
        if (contents is null || contents.Length is < 8 or > 2097152)
            throw new InvalidDataException("Invalid Registry.pol length.");
        using var stream = new MemoryStream(contents, false);
        using var reader = new BinaryReader(stream, Encoding.Unicode);
        if (reader.ReadUInt32() != 0x67655250)
            throw new InvalidDataException("Invalid Registry.pol signature.");
        uint version = ReadDword(reader);
        if (version is < 1 or > 10)
            throw new InvalidDataException("Unsupported Registry.pol version.");
        var hits = new List<Hit>();
        while (stream.Position < stream.Length)
        {
            ushort marker = ReadWord(reader);
            if (marker == 0) continue;
            if (marker != '[') throw new InvalidDataException("Malformed Registry.pol entry.");
            // MS-GPREG requires UTF-16LE NUL-terminated key AND value
            // identifiers, followed by a separate UTF-16 semicolon.
            // A delimiter alone is not a valid identifier terminator.
            string key = ReadNullTerminatedIdentifier(reader, key: true);
            string name = ReadNullTerminatedIdentifier(reader, key: false);
            uint type = ReadDword(reader);
            Expect(reader, ';');
            uint len = ReadDword(reader);
            if (len > 65535) throw new InvalidDataException("Registry.pol value limit exceeded.");
            Expect(reader, ';');
            byte[] bytes = reader.ReadBytes((int)len);
            if (bytes.Length != len) throw new InvalidDataException("Truncated Registry.pol value.");
            Expect(reader, ']');
            string display = DisplayRegistryPolicyValue(type, bytes);
            var hit = TargetValue(key, name, display, userScope);
            if (hit is not null) hits.Add(hit);
            if (hits.Count > 120)
                throw new InvalidDataException("Too many relevant Registry.pol settings.");
        }
        return hits;
    }

    private static string ReadNullTerminatedIdentifier(BinaryReader reader, bool key)
    {
        var text = new StringBuilder();
        int maxChars = key ? 4096 : 259;
        for (int i = 0; i <= maxChars; i++)
        {
            ushort word = ReadWord(reader);
            if (word == 0)
            {
                if (key && text.Length == 0)
                    throw new InvalidDataException("Registry.pol key identifier cannot be empty.");
                Expect(reader, ';');
                // A malformed UTF-16 name can conceal a relevant policy key
                // from the static scanner. Validate rather than replace.
                string value = text.ToString();
                try
                {
                    _ = new UnicodeEncoding(false, false, true).GetByteCount(value);
                }
                catch (EncoderFallbackException ex)
                {
                    throw new InvalidDataException("Registry.pol identifier contains invalid UTF-16.", ex);
                }
                return value;
            }
            // Semicolons/brackets may occur inside a registry identifier.
            // Only the UTF-16 NUL plus following semicolon ends the field.
            if (word < 0x20)
                throw new InvalidDataException(
                    "Registry.pol identifier contains invalid control characters.");
            text.Append((char)word);
        }
        throw new InvalidDataException("Registry.pol identifier too long.");
    }

    private static uint ReadDword(BinaryReader reader)
    {
        if (reader.BaseStream.Position + 4 > reader.BaseStream.Length)
            throw new InvalidDataException("Truncated Registry.pol DWORD.");
        return reader.ReadUInt32();
    }

    private static string DisplayRegistryPolicyValue(uint type, byte[] bytes)
    {
        // Keep unknown/binary policy types as overlapping candidates rather
        // than claiming they set a valid string, or silently omitting them.
        if (type is 1 or 2 or 7)
        {
            if (bytes.Length < 2 || bytes.Length % 2 != 0 ||
                bytes[^1] != 0 || bytes[^2] != 0 ||
                (type == 7 && (bytes.Length < 4 || bytes[^3] != 0 || bytes[^4] != 0)))
                throw new InvalidDataException("Malformed UTF-16 Registry.pol value.");
            try
            {
                return new UnicodeEncoding(false, false, true).GetString(bytes)
                    .TrimEnd('\0');
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("Invalid UTF-16 Registry.pol value.", ex);
            }
        }
        if (type == 4 && bytes.Length != 4)
            throw new InvalidDataException("Malformed Registry.pol DWORD value.");
        if (type == 5 && bytes.Length != 4)
            throw new InvalidDataException("Malformed Registry.pol big-endian DWORD value.");
        if (type == 11 && bytes.Length != 8)
            throw new InvalidDataException("Malformed Registry.pol QWORD value.");
        return type switch
        {
            4 => BitConverter.ToUInt32(bytes, 0).ToString(CultureInfo.InvariantCulture),
            5 => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes)
                .ToString(CultureInfo.InvariantCulture),
            11 => BitConverter.ToUInt64(bytes, 0).ToString(CultureInfo.InvariantCulture),
            _ => "[binary or unsupported]"
        };
    }

    private static ushort ReadWord(BinaryReader reader)
    {
        if (reader.BaseStream.Position + 2 > reader.BaseStream.Length)
            throw new InvalidDataException("Truncated Registry.pol data.");
        return reader.ReadUInt16();
    }

    private static void Expect(BinaryReader reader, char expected)
    {
        if (ReadWord(reader) != expected)
            throw new InvalidDataException("Malformed Registry.pol separator.");
    }

    internal static IReadOnlyList<Hit> ParseGppRegistry(byte[] contents,
        bool userScope = false)
    {
        if (contents is null || contents.Length is < 1 or > 2097152)
            throw new InvalidDataException("GPP Registry.xml exceeds size limit.");
        using var stream = new MemoryStream(contents, false);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2097152,
            MaxCharactersFromEntities = 0
        };
        using var reader = XmlReader.Create(stream, settings);
        var xml = new XmlDocument { XmlResolver = null };
        xml.Load(reader);
        var hits = new List<Hit>();
        var entries = xml.SelectNodes("//Registry");
        if (entries is null) return hits;
        foreach (XmlNode entry in entries)
        {
            if (entry is not XmlElement element ||
                element.GetAttribute("disabled") == "1")
                continue;
            foreach (XmlNode child in element.SelectNodes("./Properties")!)
            {
                if (child is not XmlElement p) continue;
                string hive = p.GetAttribute("hive");
                bool correctHive = userScope
                    ? hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase) ||
                      hive.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)
                    : hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) ||
                      hive.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase);
                if (!correctHive) continue;
                var hit = TargetValue(p.GetAttribute("key"),
                    p.GetAttribute("name"), p.GetAttribute("value"), userScope);
                if (hit is not null) hits.Add(hit);
                if (hits.Count > 120)
                    throw new InvalidDataException("Too many relevant GPP registry settings.");
            }
        }
        return hits;
    }

    private static RemoteRegistryResult ReadRemoteRegistry(string computer)
    {
        var values = new List<Hit>();
        try
        {
            using var registry = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine,
                computer, RegistryView.Registry64);
            foreach (string path in new[]
            {
                @"SOFTWARE\Policies\Microsoft\Windows\Personalization",
                @"SOFTWARE\Policies\Microsoft\Windows\CloudContent"
            })
            {
                using var key = registry.OpenSubKey(path);
                if (key is null) continue;
                foreach (string name in key.GetValueNames())
                {
                    object? raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    string value = raw is string[] list ? string.Join(";", list) :
                        Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
                    var hit = TargetValue(path, name, Truncate(value, 450));
                    if (hit is not null) values.Add(hit);
                }
            }
            return new RemoteRegistryResult(true, values,
                "Read-only remote HKLM. GPO source still requires gpresult.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
            return new RemoteRegistryResult(false, values,
                "Remote Registry unavailable/denied; this is NOT proof of no conflict. " +
                    "Check RemoteRegistry/firewall/RPC and run gpresult locally.");
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + " [truncated]";

    private static string SafeName(string value)
    {
        string shortName = Truncate(value, 100);
        return new string(shortName.Where(x => !char.IsControl(x)).ToArray());
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string username, string? domain,
        string password, int logonType, int provider, out SafeAccessTokenHandle token);
}
