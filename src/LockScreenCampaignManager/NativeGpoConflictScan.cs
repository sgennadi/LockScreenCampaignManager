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
    private sealed record Match(
        string gpo, string gpoId, string linkedAt, bool enforced, bool disabled,
        bool inheritanceBlocked, bool computerPartDisabled, bool wmiFilterPresent,
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
            if (!Inside(ouDn, baseDn) || (ouDn.Length > 0 && ouDn.Any(char.IsControl)))
                throw new InvalidDataException("Selected OU is outside the verified AD domain.");

            var levels = ReadLevels(ldap, baseDn, ouDn);
            var found = new List<Match>();
            var notes = new List<string>();
            foreach (var level in levels)
            {
                foreach (var link in level.Links)
                {
                    bool enabled = (link.Flags & 1) == 0;
                    bool enforced = (link.Flags & 2) != 0;
                    bool blockedBelow = levels.Any(x => x.Depth > level.Depth && x.Blocked);
                    bool inScope = enabled && (enforced || !blockedBelow);
                    if (!TryLinkedGuid(link.Dn, baseDn, out Guid id))
                    {
                        notes.Add("Invalid GPO link in the selected OU hierarchy.");
                        continue;
                    }
                    var entry = ReadObject(ldap, link.Dn,
                        "(objectClass=groupPolicyContainer)", "displayName", "flags", "gPCWQLFilter");
                    string gpoName = NativeAdDiscovery.Attr(entry, "displayName");
                    if (gpoName.Length == 0) gpoName = id.ToString("D");
                    int flags = IntAttribute(entry, "flags");
                    string wmi = NativeAdDiscovery.Attr(entry, "gPCWQLFilter");
                    if ((flags & 2) != 0) inScope = false;

                    // The SYSVOL path is pinned to the same verified DC used
                    // for LDAP. A missing/unreadable Machine directory must
                    // block the pre-deployment audit, never count as no conflicts.
                    string machine = Path.Combine(@"\\", actualDc, "SYSVOL", domain,
                        "Policies", "{" + id.ToString("D").ToUpperInvariant() + "}", "Machine");
                    EnsureDirectory(machine);
                    foreach (string startup in new[]
                    {
                        @"Scripts\psscripts.ini", @"Scripts\scripts.ini"
                    })
                    {
                        string path = Path.Combine(machine, startup);
                        if (FileExistsSafe(path) && inScope)
                            notes.Add("Startup script in " + SafeName(gpoName) +
                                " requires manual GPMC/gpresult verification.");
                    }
                    foreach (string source in new[]
                    {
                        "Registry.pol", @"Preferences\Registry\Registry.xml"
                    })
                    {
                        string file = Path.Combine(machine, source);
                        if (!FileExistsSafe(file)) continue;
                        IReadOnlyList<Hit> hits;
                        try
                        {
                            byte[] bytes = File.ReadAllBytes(file);
                            hits = source == "Registry.pol"
                                ? ParseRegistryPol(bytes) : ParseGppRegistry(bytes);
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
                                wmi.Length > 0, source, hit.key, hit.name,
                                Truncate(hit.value, 450),
                                inScope ? hit.risk : "Inactive/filtered", hit.reason,
                                "Estimated: security filtering, WMI, site/local policies and RSoP not fully evaluated."));
                        }
                    }
                }
            }
            object? remoteRegistry = null;
            if (computer.Length > 0)
            {
                remoteRegistry = ReadRemoteRegistry(computer);
                // If remote registry cannot be read, a warning remains visible.
                // This is never proof that a policy is absent on the client.
            }
            int high = found.Count(x => x.risk == "High");
            int warningCount = found.Count(x => x.risk == "Warning");
            var result = new
            {
                domain, dc = actualDc, targetOu = ouDn,
                scannedLevels = levels.Select(x => new ScanLevel(x.Dn, x.Blocked, x.Depth)),
                matches = found, count = found.Count, highRisk = high,
                warningsCount = warningCount, notes, remoteRegistry,
                scope = "Read-only candidate audit. Not a definitive Resultant Set of Policy (RSoP). " +
                    "Site-level, local, WMI, item-level targeting and security permissions require verification."
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
                Match m = matches[j];
                if (!int.TryParse(m.Groups["flags"].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out int flags))
                    throw new InvalidDataException("Invalid GPO link state.");
                parsed.Add(new GpoLink(m.Groups["dn"].Value, flags, matches.Count - j));
            }
            if (parsed.Count > 0 && matches.Count != parsed.Count)
                throw new InvalidDataException("Invalid linked GPO inventory.");
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

    private static void EnsureDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Selected DC SYSVOL Machine folder missing or unsafe.");
        // Fail closed if directory listing cannot be read.
        _ = directory.EnumerateFileSystemInfos().Take(1).ToArray();
    }

    private static bool FileExistsSafe(string path)
    {
        if (!File.Exists(path)) return false;
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
            info.Length is < 1 or > 2097152)
            throw new InvalidDataException("Linked GPO registry file is unsafe or too large.");
        return true;
    }

    internal static Hit? TargetValue(string key, string name, string value)
    {
        string k = key.TrimStart('\\').ToLowerInvariant();
        string n = name.ToLowerInvariant();
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

    internal static IReadOnlyList<Hit> ParseRegistryPol(byte[] contents)
    {
        if (contents is null || contents.Length is < 8 or > 2097152)
            throw new InvalidDataException("Invalid Registry.pol length.");
        using var stream = new MemoryStream(contents, false);
        using var reader = new BinaryReader(stream, Encoding.Unicode);
        if (reader.ReadUInt32() != 0x67655250)
            throw new InvalidDataException("Invalid Registry.pol signature.");
        uint version = reader.ReadUInt32();
        if (version is < 1 or > 10)
            throw new InvalidDataException("Unsupported Registry.pol version.");
        var hits = new List<Hit>();
        while (stream.Position < stream.Length)
        {
            ushort marker = ReadWord(reader);
            if (marker == 0) continue;
            if (marker != '[') throw new InvalidDataException("Malformed Registry.pol entry.");
            string key = ReadString(reader);
            string name = ReadString(reader);
            uint type = reader.ReadUInt32();
            Expect(reader, ';');
            uint len = reader.ReadUInt32();
            if (len > 65535) throw new InvalidDataException("Registry.pol value limit exceeded.");
            Expect(reader, ';');
            byte[] bytes = reader.ReadBytes((int)len);
            if (bytes.Length != len) throw new InvalidDataException("Truncated Registry.pol value.");
            Expect(reader, ']');
            string display = type switch
            {
                1 or 2 or 7 => Encoding.Unicode.GetString(bytes).TrimEnd('\0'),
                4 when bytes.Length >= 4 => BitConverter.ToUInt32(bytes, 0).ToString(CultureInfo.InvariantCulture),
                4 => "[invalid DWORD]",
                _ => "[binary or unsupported]"
            };
            var hit = TargetValue(key, name, display);
            if (hit is not null) hits.Add(hit);
            if (hits.Count > 120)
                throw new InvalidDataException("Too many relevant Registry.pol settings.");
        }
        return hits;
    }

    private static string ReadString(BinaryReader reader)
    {
        var text = new StringBuilder();
        for (int i = 0; i < 4097; i++)
        {
            ushort value = ReadWord(reader);
            if (value == ';') return text.ToString();
            if (value != 0) text.Append((char)value);
        }
        throw new InvalidDataException("Registry.pol string too long.");
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

    internal static IReadOnlyList<Hit> ParseGppRegistry(byte[] contents)
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
                if (hive is not ("HKEY_LOCAL_MACHINE" or "HKLM")) continue;
                var hit = TargetValue(p.GetAttribute("key"),
                    p.GetAttribute("name"), p.GetAttribute("value"));
                if (hit is not null) hits.Add(hit);
                if (hits.Count > 120)
                    throw new InvalidDataException("Too many relevant GPP registry settings.");
            }
        }
        return hits;
    }

    private static object ReadRemoteRegistry(string computer)
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
            return new
            {
                reachable = true, values,
                note = "Read-only remote HKLM. GPO source still requires gpresult."
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
            return new
            {
                reachable = false, values,
                note = "Remote Registry unavailable/denied; this is NOT proof of no conflict. " +
                    "Check RemoteRegistry/firewall/RPC and run gpresult locally."
            };
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
