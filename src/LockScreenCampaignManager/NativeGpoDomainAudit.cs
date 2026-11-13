using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// Enumerate EVERY domain GPC on the explicitly selected DC, then inspect
/// known Lock Screen, User Screen Saver, power and GPP settings in that DC's
/// SYSVOL. Reuses the hardened native Registry.pol/GPP readers also used for
/// OU conflict scans. "Linked candidate" never means winning GPO/RSoP.
/// No remote execution, endpoint read, AD/GPO mutation or automatic repair.
/// </summary>
internal static class NativeGpoDomainAudit
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex GpLink = new(
        @"\[LDAP://(?<dn>[^;\]]+);(?<flags>[0-9]+)\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private sealed record Link(Guid Id, string Scope, string LinkedAt,
        string TargetOu, bool Active, bool Enforced);
    private sealed record Row(string Gpo, string GpoId, string Scope, string Source,
        string Key, string Setting, string Value, string Risk, string Reason,
        string LinkStatus, string LinkedAt, string Applicability);
    private sealed record Issue(string Gpo, string GpoId, string Scope, string Reason);

    internal static JsonElement Execute(string mode, object request, DomainCredentials credentials)
    {
        if (mode != "AllGpos")
            throw new ArgumentException("Only explicit read-only AllGpos is supported.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows signed LDAP and SYSVOL are required.");
        var args = JsonSerializer.SerializeToElement(request, Json);
        string domain = Required(args, "domain", 253).ToLowerInvariant();
        string dc = Required(args, "dc", 253);
        NativeAdDiscovery.ValidateNames(domain, dc);
        int idle = args.GetProperty("screenSaverIdleSeconds").GetInt32();
        if (idle is < 60 or > 86400)
            throw new InvalidDataException("Select a valid configured Screen Saver idle time.");
        string saverExe = Required(args, "screenSaverExe", 260);
        string modeName = Required(args, "campaignMode", 32);
        if (modeName is not ("LockScreen" or "ScreenSaver" or "Both"))
            throw new InvalidDataException("Unexpected campaign type.");
        string[] computerOus = Ous(args, "computerOus");
        string[] userOus = Ous(args, "userOus");
        if (computerOus.Length == 0 && userOus.Length == 0)
            throw new InvalidDataException("Select at least one Computer or User OU for relevance analysis.");

        JsonElement Run()
        {
            using var ldap = NativeAdDiscovery.Connect(dc, credentials);
            ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            var root = NativeAdDiscovery.ReadRootDse(ldap);
            string baseDn = NativeAdDiscovery.Attr(root, "defaultNamingContext");
            string actualDc = NativeAdDiscovery.Attr(root, "dnsHostName");
            if (!NativeAdDiscovery.DomainFromDn(baseDn).Equals(domain,
                    StringComparison.OrdinalIgnoreCase) ||
                !NativeGpoLifecycleList.SameController(dc, actualDc))
                throw new InvalidDataException("Selected DC and AD domain identity mismatch.");

            var links = new List<Link>();
            foreach (string ou in computerOus)
                ReadTargetLinks(ldap, baseDn, ou, "Computer", links);
            foreach (string ou in userOus)
                ReadTargetLinks(ldap, baseDn, ou, "User", links);

            string gpcContainer = "CN=Policies,CN=System," + baseDn;
            var container = (SearchResponse)ldap.SendRequest(new SearchRequest(
                gpcContainer, "(objectClass=*)", SearchScope.Base, "distinguishedName"));
            if (container.Entries.Count != 1)
                throw new InvalidDataException("GPO container could not be verified.");

            var gpos = ReadAllGpos(ldap, gpcContainer, baseDn);
            string sysvol = @"\\" + actualDc + @"\SYSVOL\" + domain + @"\Policies";
            NativeGpoConflictScan.EnsureDirectory(sysvol);
            var rows = new List<Row>();
            var issues = new List<Issue>();
            var knownIds = gpos.Select(x => x.Id).ToHashSet();
            foreach (var missing in links.Where(x => !knownIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Scope }).Distinct())
                AddIssue(issues, "Unresolved AD GPO link", missing.Id, missing.Scope,
                    "Selected OU ancestry links a missing or unreadable GPC object.");
            var notes = new List<string>();
            int inspectedHalves = 0, emptyHalves = 0;
            foreach (var (entry, id) in gpos)
            {
                string name = Safe(NativeAdDiscovery.Attr(entry, "displayName"), 150);
                if (name.Length == 0) name = id.ToString("D");
                int flags = IntAttr(entry, "flags");
                int version = IntAttr(entry, "versionNumber");
                bool hasWmi = NativeAdDiscovery.Attr(entry, "gPCWQLFilter").Length > 0;
                string gpoRoot = Path.Combine(sysvol, "{" + id.ToString("D").ToUpperInvariant() + "}");
                try { NativeGpoConflictScan.EnsureDirectory(gpoRoot); }
                catch (Exception ex) when (Expected(ex))
                {
                    AddIssue(issues, name, id, "Both",
                        "AD GPC exists but selected DC SYSVOL folder is unavailable/unsafe.");
                    continue;
                }
                foreach (bool user in new[] { false, true })
                {
                    string scope = user ? "User" : "Computer";
                    string half = user ? "User" : "Machine";
                    string cse = NativeAdDiscovery.Attr(entry, user
                        ? "gPCUserExtensionNames" : "gPCMachineExtensionNames");
                    bool halfDisabled = NativeGpoConflictScan.IsPolicyPartDisabled(flags, user);
                    string path = Path.Combine(gpoRoot, half);
                    try
                    {
                        if (!NativeGpoConflictScan.PolicyFolderExistsSafe(path))
                        {
                            if (!halfDisabled &&
                                NativeGpoConflictScan.PolicyHalfHasConfiguredSettings(
                                    version, cse, user))
                                AddIssue(issues, name, id, scope,
                                    "GPC advertises this policy half but SYSVOL half is missing.");
                            else emptyHalves++;
                            continue;
                        }
                        NativeGpoConflictScan.EnsureDirectory(path);
                        inspectedHalves++;
                        foreach (string source in new[] {
                            "Registry.pol", @"Preferences\Registry\Registry.xml",
                            @"Preferences\PowerOptions\PowerOptions.xml"
                        })
                        {
                            if (user && source.EndsWith("PowerOptions.xml",
                                    StringComparison.Ordinal)) continue;
                            string file = Path.Combine(path, source);
                            try
                            {
                                if (!NativeGpoConflictScan.FileExistsSafe(file))
                                {
                                    if (source == "Registry.pol" && !halfDisabled &&
                                        NativeGpoConflictScan.RegistryCseAdvertised(cse))
                                        AddIssue(issues, name, id, scope,
                                            "AD advertises Registry CSE but Registry.pol is missing.");
                                    continue;
                                }
                                if (source != "Registry.pol")
                                {
                                    string folder = Path.GetDirectoryName(file)!;
                                    NativeGpoConflictScan.EnsureDirectory(
                                        Path.GetDirectoryName(folder)!);
                                    NativeGpoConflictScan.EnsureDirectory(folder);
                                }
                                byte[] bytes = File.ReadAllBytes(file);
                                IReadOnlyList<NativeGpoConflictScan.Hit> hits =
                                    source == "Registry.pol"
                                        ? NativeGpoConflictScan.ParseRegistryPol(bytes, user)
                                        : source.EndsWith("PowerOptions.xml", StringComparison.Ordinal)
                                            ? ScreenSaverPowerConflictAnalysis.ParseGppPowerOptions(bytes)
                                            : NativeGpoConflictScan.ParseGppRegistry(bytes, user);
                                foreach (var hit in hits)
                                {
                                    if (rows.Count >= 6000)
                                        throw new InvalidDataException("All-GPO result cap exceeded.");
                                    var status = Resolve(id, scope, halfDisabled, links,
                                        user ? userOus.Length > 0 : computerOus.Length > 0);
                                    string risk = AssessRisk(hit, user, modeName, idle,
                                        saverExe, status.status);
                                    rows.Add(new Row(name, id.ToString("D"), scope, source,
                                        Safe(hit.key, 600), Safe(hit.name, 120),
                                        Safe(hit.value, 470), risk, Safe(hit.reason, 400),
                                        status.status, status.linkedAt,
                                        (hasWmi ? "WMI filter present; " : "") +
                                        "Static source only. Security filtering, site GPO, " +
                                        "loopback, GPP item-level targeting and winning RSoP unverified."));
                                }
                            }
                            catch (Exception ex) when (Expected(ex))
                            {
                                AddIssue(issues, name, id, scope,
                                    source + " not safely readable/parseable on selected DC.");
                            }
                        }
                        foreach (string script in new[] {
                            @"Scripts\scripts.ini", @"Scripts\psscripts.ini"
                        })
                        {
                            string file = Path.Combine(path, script);
                            if (NativeGpoConflictScan.FileExistsSafe(file))
                            {
                                if (notes.Count < 100)
                                    notes.Add(name + " / " + scope +
                                        ": script source present; content and effects not classified.");
                            }
                        }
                    }
                    catch (Exception ex) when (Expected(ex))
                    {
                        AddIssue(issues, name, id, scope,
                            "GPO policy-half folder or script metadata unreadable/unsafe.");
                    }
                }
            }
            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                message = "All domain GPOs enumerated on selected DC (read-only).",
                data = new {
                    domain, dc = actualDc, computerOus, userOus,
                    observedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    gpoCount = gpos.Count, inspectedHalves, emptyHalves,
                    issueCount = issues.Count, issues, notes,
                    matches = rows, count = rows.Count,
                    linkedCandidates = rows.Count(x => x.LinkStatus == "Linked candidate"),
                    highRisk = rows.Count(x => x.Risk == "High"),
                    warningsCount = rows.Count(x => x.Risk == "Warning"),
                    complete = issues.Count == 0,
                    scope = "All GPC objects in this domain on one verified DC. " +
                        "Recognized Registry.pol/GPP settings only. Linked candidates are " +
                        "NOT winning GPO or client RSoP. Site, loopback, WMI, GPP " +
                        "targeting, security filtering, MDM and real desktop unverified."
                }
            }, Json);
        }

        if (!credentials.IsAlternate) return Run();
        string userName = credentials.UserName.Trim();
        string? accountDomain = null;
        int slash = userName.IndexOf('\\');
        if (slash > 0 && slash < userName.Length - 1)
        {
            accountDomain = userName[..slash];
            userName = userName[(slash + 1)..];
        }
        else if (!userName.Contains('@')) userName += "@" + credentials.Domain.Trim();
        if (credentials.Password.Length == 0 ||
            !LogonUser(userName, accountDomain, credentials.Password, 9, 3,
                out SafeAccessTokenHandle token))
            throw new InvalidOperationException(
                "Could not establish alternate network credentials for all-GPO audit.");
        using (token) return WindowsIdentity.RunImpersonated(token, Run);
    }

    private static List<(SearchResultEntry Entry, Guid Id)> ReadAllGpos(
        LdapConnection ldap, string container, string baseDn)
    {
        var output = new List<(SearchResultEntry, Guid)>();
        var seen = new HashSet<Guid>();
        var request = new SearchRequest(container,
            "(objectClass=groupPolicyContainer)", SearchScope.OneLevel,
            "displayName", "flags", "versionNumber", "gPCWQLFilter",
            "gPCUserExtensionNames", "gPCMachineExtensionNames");
        var page = new PageResultRequestControl(200);
        request.Controls.Add(page);
        for (int number = 0; number < 12; number++)
        {
            var response = (SearchResponse)ldap.SendRequest(request);
            foreach (SearchResultEntry entry in response.Entries)
            {
                if (!NativeGpoConflictScan.TryLinkedGuid(
                        entry.DistinguishedName, baseDn, out Guid id) ||
                    !seen.Add(id))
                    throw new InvalidDataException("Invalid or duplicated domain GPC GUID.");
                if (output.Count >= 2000)
                    throw new InvalidDataException("All-GPO inventory exceeds 2,000 objects.");
                output.Add((entry, id));
            }
            var returned = response.Controls.OfType<PageResultResponseControl>()
                .SingleOrDefault();
            if (returned is null)
                throw new InvalidDataException("GPO LDAP paging was not verified.");
            if (returned.Cookie.Length == 0) return output;
            page.Cookie = returned.Cookie;
        }
        throw new InvalidDataException("Domain GPO paging did not finish; result incomplete.");
    }

    private static void ReadTargetLinks(LdapConnection ldap, string baseDn,
        string target, string scope, List<Link> output)
    {
        if (!target.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
            target.Any(char.IsControl) || target.Length > 2048 ||
            !NativeGpoConflictScan.Inside(target, baseDn))
            throw new InvalidDataException("Selected " + scope + " OU is invalid/outside domain.");
        string? node = target;
        bool blocked = false;
        int depth = 0;
        while (node is not null && NativeGpoConflictScan.Inside(node, baseDn))
        {
            if (++depth > 40)
                throw new InvalidDataException("OU ancestry exceeds safe depth.");
            var response = (SearchResponse)ldap.SendRequest(
                new SearchRequest(node,
                    node.Equals(target, StringComparison.OrdinalIgnoreCase)
                        ? "(objectClass=organizationalUnit)" : "(objectClass=*)",
                    SearchScope.Base, "gPLink", "gPOptions"));
            if (response.Entries.Count != 1)
                throw new InvalidDataException("Selected OU ancestry is not readable.");
            var entry = response.Entries[0];
            string raw = NativeAdDiscovery.Attr(entry, "gPLink");
            var matches = GpLink.Matches(raw);
            if (GpLink.Replace(raw, "").Trim().Length != 0)
                throw new InvalidDataException("Malformed gPLink in selected OU ancestry.");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (output.Count >= 1000)
                    throw new InvalidDataException("Target GPO links exceed safe cap.");
                if (!NativeGpoConflictScan.TryLinkedGuid(match.Groups["dn"].Value,
                        baseDn, out Guid id) ||
                    !int.TryParse(match.Groups["flags"].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out int flags) ||
                    (flags & ~3) != 0)
                    throw new InvalidDataException("Invalid GPO link in selected OU ancestry.");
                bool enforced = (flags & 2) != 0;
                output.Add(new Link(id, scope, node, target,
                    (flags & 1) == 0 && (!blocked || enforced), enforced));
            }
            string gpoOptions = NativeAdDiscovery.Attr(entry, "gPOptions");
            if (gpoOptions.Length > 0)
            {
                if (!int.TryParse(gpoOptions, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int flags) || (flags & ~1) != 0)
                    throw new InvalidDataException("Invalid OU inheritance policy options.");
                if ((flags & 1) != 0) blocked = true;
            }
            if (node.Equals(baseDn, StringComparison.OrdinalIgnoreCase)) break;
            node = NativeGpoConflictScan.ParentDn(node);
        }
        if (node is null || !node.Equals(baseDn, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("OU ancestry did not reach selected domain.");
    }

    private static (string status, string linkedAt) Resolve(
        Guid id, string scope, bool disabledHalf, IReadOnlyList<Link> links,
        bool hasTargetOu)
    {
        if (disabledHalf) return ("Inactive/filtered", "GPO policy half disabled");
        if (!hasTargetOu) return ("User OU not verified", "");
        var found = links.Where(x => x.Id == id && x.Scope == scope).ToArray();
        var active = found.Where(x => x.Active).ToArray();
        if (active.Length > 0)
            return ("Linked candidate", Safe(string.Join("; ", active.Select(x =>
                x.LinkedAt + (x.Enforced ? " [Enforced]" : "") +
                " -> " + x.TargetOu)), 2048));
        if (found.Length > 0)
            return ("Inactive/filtered", "Disabled link or blocked inheritance");
        return ("Outside selected OUs", "No linked ancestor path in selected OUs");
    }

    internal static string AssessRisk(NativeGpoConflictScan.Hit hit, bool user,
        string campaignMode, int idle, string saverExe, string linkStatus)
    {
        if (linkStatus == "Inactive/filtered") return "Inactive/filtered";
        if (linkStatus != "Linked candidate") return "Info";
        string key = hit.key.TrimStart('\\').ToLowerInvariant();
        string name = hit.name.ToLowerInvariant();
        bool includesSaver = campaignMode is "ScreenSaver" or "Both";
        bool includesLock = campaignMode is "LockScreen" or "Both";
        if (user)
        {
            if (!includesSaver) return "Info";
            if (name == "screensaveactive") return hit.value.Trim() switch {
                "0" => "High", "1" => "Info", _ => "Warning"
            };
            if (name == "screensavetimeout")
                return int.TryParse(hit.value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int seconds) && seconds == idle
                    ? "Info" : "Warning";
            if (name == "scrnsave.exe")
                return hit.value.Equals(saverExe, StringComparison.OrdinalIgnoreCase)
                    ? "Info" : "Warning";
            if (name == "screensaverissecure") return hit.value.Trim() == "0"
                ? "Warning" : "Info";
            return hit.risk;
        }
        if (name == "userpolicymode" &&
            key == @"software\policies\microsoft\windows\system")
        {
            if (!includesSaver) return "Info";
            return hit.value.Trim() switch {
                "2" => "High", "1" => "Warning", "0" => "Info", _ => "Warning"
            };
        }
        if (name == "nolockscreen" && includesLock)
            return hit.value.Trim() switch {
                "1" => "High", "0" => "Info", _ => "Warning"
            };
        var example = new GroupConflictFinding("Selected OUs", "Computer",
            "Static domain GPO", Guid.Empty.ToString(), hit.risk, hit.name,
            hit.value, "SYSVOL", hit.key, "", "Candidate");
        if (ScreenSaverPowerConflictAnalysis.IsMachinePowerCandidate(example))
        {
            if (!includesSaver) return "Info";
            var finding = ScreenSaverPowerConflictAnalysis.Compare(
                "Selected OUs", idle, [example]).FirstOrDefault();
            return finding?.Risk ?? "Info";
        }
        return includesLock ? hit.risk : "Info";
    }

    private static string[] Ous(JsonElement args, string field)
    {
        if (!args.TryGetProperty(field, out var array) ||
            array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() > 25)
            throw new InvalidDataException("Missing/unbounded selected OU scope.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(item.GetString()) ||
                item.GetString()!.Length > 2048)
                throw new InvalidDataException("Invalid selected OU scope.");
            names.Add(item.GetString()!.Trim());
        }
        return names.ToArray();
    }

    private static int IntAttr(SearchResultEntry entry, string key)
    {
        string raw = NativeAdDiscovery.Attr(entry, key);
        if (raw.Length == 0) return 0;
        if (int.TryParse(raw, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int value)) return value;
        throw new InvalidDataException("Invalid AD GPO integer attribute.");
    }

    private static void AddIssue(List<Issue> issues, string name, Guid id,
        string scope, string reason)
    {
        if (issues.Count >= 300)
            throw new InvalidDataException("All-GPO audit issue cap reached; incomplete.");
        issues.Add(new Issue(name, id.ToString("D"), scope, reason));
    }

    private static string Required(JsonElement args, string field, int max)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty(field, out var v) ||
            v.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(v.GetString()) ||
            v.GetString()!.Length > max ||
            v.GetString()!.Any(char.IsControl))
            throw new InvalidDataException("Invalid all-GPO audit argument: " + field);
        return v.GetString()!.Trim();
    }

    private static bool Expected(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
        InvalidDataException or XmlException or ArgumentException or
        System.Security.SecurityException;

    private static string Safe(string input, int max)
    {
        string printable = new(input.Where(c => !char.IsControl(c)).ToArray());
        return printable.Length <= max ? printable : printable[..max];
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string username, string? domain,
        string password, int logonType, int provider, out SafeAccessTokenHandle token);
}
