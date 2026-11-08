using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// Conservative native per-computer GPO candidate audit. Verifies the exact
/// managed campaign, computer OU/group membership, nested AD SID token,
/// linked OU ancestors and site policies, DACL Apply/read ACEs, static
/// Registry.pol/GPP values and startup scripts. It never claims final RSoP.
/// </summary>
internal static class NativeGpoEffectiveAudit
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid ApplyPolicy = new(
        "edacfd8f-ffb3-11d1-b41d-00a0c968f939");
    private sealed record Level(string Dn, int Depth, bool Blocked,
        IReadOnlyList<NativeGpoLifecycleList.ParsedLink> Links);
    private sealed record Computer(string fqdn, string dn, string membership,
        string tokenStatus, IReadOnlySet<string> tokenSids);
    private sealed record Setting(string source, string key, string name,
        string value, string risk, string reason);
    private sealed record Details(string state, bool scripts, IReadOnlyList<Setting> settings);
    private sealed record Policy(string gpo, string gpoId, string linkedAt, string site,
        string link, int linkOrder, int scopeDepth, bool enforced, bool blockedBelow,
        bool machinePartDisabled, string security, string wmi,
        string staticRead, bool startupScript, string category,
        IReadOnlyList<Setting> settings, string note);

    internal static JsonElement Execute(string mode, object request,
        DomainCredentials credentials)
    {
        if (mode != "Effective")
            throw new ArgumentException("This native GPO assessment is read-only Effective.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows native LDAP and SYSVOL are required.");
        JsonElement args = JsonSerializer.SerializeToElement(request, Json);
        string domain = Get(args, "domain").Trim().ToLowerInvariant();
        string dcName = Get(args, "dc").Trim();
        string ouDn = Get(args, "ouDn").Trim();
        string groupDn = Get(args, "groupDn").Trim();
        string computer = Get(args, "computer").Trim();
        string campaignId = Get(args, "campaignId");
        string policyId = Get(args, "managedGpoId");
        NativeAdDiscovery.ValidateNames(domain, dcName);
        if (!NativeGpoConflictScan.SafeComputerName(computer, domain) ||
            !NativeSysvolStorage.ValidCampaignId(campaignId) ||
            !Guid.TryParse(policyId, out Guid managedGuid) || managedGuid == Guid.Empty ||
            !ouDn.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
            !groupDn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selected managed campaign, target computer or AD scope is invalid.");

        JsonElement Run()
        {
            using var ldap = NativeAdDiscovery.Connect(dcName, credentials);
            ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            var root = NativeAdDiscovery.ReadRootDse(ldap);
            string baseDn = Required(root, "defaultNamingContext");
            string actualDc = Required(root, "dnsHostName");
            string configDn = Required(root, "configurationNamingContext");
            if (!NativeAdDiscovery.DomainFromDn(baseDn).Equals(domain,
                    StringComparison.OrdinalIgnoreCase) ||
                !NativeGpoLifecycleList.SameController(dcName, actualDc) ||
                !NativeGpoConflictScan.Inside(ouDn, baseDn) ||
                !NativeGpoConflictScan.Inside(groupDn, baseDn))
                throw new InvalidOperationException("Selected AD controller/domain/OU/group do not match.");

            var machine = VerifyComputer(ldap, ouDn, groupDn, computer);
            var managed = NativeGpoLifecycleList.ReadManagedGpo(actualDc, domain, managedGuid)
                ?? throw new InvalidDataException("Selected GPO does not have the approved managed startup signature.");
            if (!string.Equals(managed.campaignId, campaignId, StringComparison.Ordinal))
                throw new InvalidDataException("GPO campaign ID or startup script changed.");
            string manifest = Path.Combine(@"\\", actualDc, "SYSVOL", domain,
                "scripts", "LockScreenCampaigns", campaignId, "campaign.json");
            var file = new FileInfo(manifest);
            if ((File.GetAttributes(manifest) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                file.Length is < 10 or > 1_048_576)
                throw new InvalidDataException("Managed campaign manifest is not a regular bounded file.");
            using (var doc = JsonDocument.Parse(File.ReadAllBytes(manifest),
                new JsonDocumentOptions { MaxDepth = 12 }))
            {
                if (doc.RootElement.GetProperty("schemaVersion").GetInt32() != 1 ||
                    Get(doc.RootElement, "campaignId") != campaignId)
                    throw new InvalidDataException("Managed campaign manifest could not be verified.");
            }

            string container = NativeGpoConflictScan.ParentDn(machine.dn) ??
                throw new InvalidDataException("Invalid AD computer distinguished name.");
            if (!NativeGpoConflictScan.Inside(container, ouDn))
                throw new InvalidOperationException("Computer is outside the selected OU.");
            var levels = ReadLevels(ldap, container, baseDn);
            var warnings = new List<string>();
            var policies = new List<Policy>();
            foreach (var level in levels)
            {
                for (int index = 0; index < level.Links.Count; index++)
                {
                    var link = level.Links[index];
                    if (link.Flags is < 0 or > 3)
                    {
                        warnings.Add("Invalid OU GPO link flags");
                        continue;
                    }
                    bool blockedBelow = levels.Any(x => x.Depth > level.Depth && x.Blocked);
                    try
                    {
                        policies.Add(Inspect(ldap, actualDc, domain, baseDn,
                            link, level.Dn, "", machine, level.Depth,
                            level.Links.Count - index, blockedBelow));
                    }
                    catch (Exception)
                    {
                        warnings.Add("An OU-linked GPO could not be inspected.");
                    }
                    if (policies.Count > 200)
                        throw new InvalidDataException("Too many OU GPO candidates.");
                }
            }

            int linkedCount = policies.Count;
            bool siteComplete = true;
            try
            {
                string sitesDn = "CN=Sites," + configDn;
                var sites = NativeGpoLifecycleList.SearchPaged(ldap, sitesDn,
                    "(objectClass=site)", 200, Stopwatch.StartNew(),
                    "gPLink", "distinguishedName");
                if (sites.Count >= 200) throw new InvalidDataException("Site inventory incomplete.");
                foreach (var site in sites)
                {
                    if (!NativeGpoConflictScan.Inside(site.DistinguishedName, sitesDn))
                        throw new InvalidDataException("Site DN outside configuration container.");
                    var links = NativeGpoLifecycleList.ParseLinks(
                        NativeAdDiscovery.Attr(site, "gPLink"));
                    for (int i = 0; i < links.Count; i++)
                    {
                        if (links[i].Flags is < 0 or > 3)
                            throw new InvalidDataException("Invalid site GPO link state.");
                        if ((links[i].Flags & 1) != 0) continue;
                        policies.Add(Inspect(ldap, actualDc, domain, baseDn,
                            links[i], site.DistinguishedName, site.DistinguishedName,
                            machine, -1, links.Count - i, false));
                        if (policies.Count - linkedCount > 100)
                            throw new InvalidDataException("Too many site GPO candidates.");
                    }
                }
            }
            catch (Exception)
            {
                siteComplete = false;
                warnings.Add("Site GPO inventory incomplete: applicability remains Unknown.");
                policies.RemoveRange(linkedCount, policies.Count - linkedCount);
            }
            int siteCandidates = policies.Count - linkedCount;
            int potentialHigh = policies.Count(p =>
                p.category == "Potentially applicable" &&
                p.settings.Any(x => x.risk == "High"));
            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                message = "Scoped native read-only GPO candidate assessment complete; this is not RSoP.",
                data = new
                {
                    domain, dc = actualDc, targetOu = ouDn, groupDn,
                    computer = machine.fqdn, campaignId,
                    managedGpoId = managedGuid.ToString("D"),
                    analyzedUtc = DateTimeOffset.UtcNow.ToString("O"),
                    membership = machine.membership, tokenStatus = machine.tokenStatus,
                    siteAssociation = "Unknown", actualComputerContainer = container,
                    siteInventoryComplete = siteComplete,
                    linkedCount, siteCandidates, potentialHigh,
                    policies, warnings,
                    scope = "Read-only candidate assessment, NOT RSoP. Site mapping, WMI filters, Group Policy CSE, GPP item-level targeting, local policies and replication may differ."
                }
            }, Json);
        }

        if (!credentials.IsAlternate) return Run();
        string user = credentials.UserName.Trim();
        string? userDomain = null;
        int slash = user.IndexOf('\\');
        if (slash > 0 && slash < user.Length - 1)
        {
            userDomain = user[..slash];
            user = user[(slash + 1)..];
        }
        else if (!user.Contains('@')) user += "@" + credentials.Domain.Trim();
        if (credentials.Password.Length == 0 ||
            !LogonUser(user, userDomain, credentials.Password, 9, 3,
                out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot obtain alternate network credentials for effective GPO audit.");
        using (token) return WindowsIdentity.RunImpersonated(token, Run);
    }

    private static Computer VerifyComputer(LdapConnection ldap, string ouDn,
        string groupDn, string fqdn)
    {
        _ = One(ldap, ouDn, "(objectClass=organizationalUnit)", "distinguishedName");
        var group = One(ldap, groupDn, "(objectClass=group)", "objectSid", "groupType");
        string groupType = NativeAdDiscovery.Attr(group, "groupType");
        if (!long.TryParse(groupType, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long securityType) ||
            (securityType & 0x80000000L) == 0 ||
            group.Attributes["objectSid"] is not { Count: > 0 } groupAttr ||
            groupAttr[0] is not byte[] groupBytes)
            throw new InvalidDataException("Selected security group identity is invalid.");
        var groupSid = new SecurityIdentifier(groupBytes, 0);
        int rid = int.Parse(groupSid.Value.Split('-')[^1], CultureInfo.InvariantCulture);
        string filter = "(&(objectCategory=computer)(dNSHostName=" +
            NativeAdDiscovery.EscapeFilter(fqdn) + "))";
        var matches = NativeGpoLifecycleList.SearchPaged(ldap, ouDn,
            filter, 2, Stopwatch.StartNew(),
            "distinguishedName", "dNSHostName", "primaryGroupID", "objectSid");
        if (matches.Count != 1 ||
            !NativeAdDiscovery.Attr(matches[0], "dNSHostName").Equals(
                fqdn, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("AD computer is missing or ambiguous in selected OU.");
        var computer = matches[0];
        string dn = computer.DistinguishedName;
        if (!NativeGpoConflictScan.Inside(dn, ouDn))
            throw new InvalidDataException("Computer identity left the selected OU.");
        bool primary = int.TryParse(NativeAdDiscovery.Attr(computer, "primaryGroupID"),
            out int primaryRid) && primaryRid == rid;
        var member = new SearchRequest(dn,
            "(&(objectCategory=computer)(memberOf:1.2.840.113556.1.4.1941:=" +
            NativeAdDiscovery.EscapeFilter(groupDn) + "))",
            SearchScope.Base, "distinguishedName");
        var membership = (SearchResponse)ldap.SendRequest(member);
        if (!primary && membership.Entries.Count != 1)
            throw new InvalidOperationException("Computer is not a member of the selected security group.");
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string tokenStatus = "Unknown";
        try
        {
            var tokenResult = (SearchResponse)ldap.SendRequest(
                new SearchRequest(dn, "(objectClass=computer)",
                    SearchScope.Base, "tokenGroups"));
            if (tokenResult.Entries.Count == 1 &&
                tokenResult.Entries[0].Attributes["tokenGroups"] is { Count: > 0 } tokenAttr)
            {
                foreach (object item in tokenAttr)
                {
                    if (item is not byte[] binary)
                        throw new InvalidDataException("Incomplete AD computer token.");
                    tokens.Add(new SecurityIdentifier(binary, 0).Value);
                }
                if (computer.Attributes["objectSid"] is { Count: > 0 } sid &&
                    sid[0] is byte[] computerSid)
                {
                    tokens.Add(new SecurityIdentifier(computerSid, 0).Value);
                    tokens.Add("S-1-1-0");
                    tokens.Add("S-1-5-11");
                    tokenStatus = "Read";
                }
            }
        }
        catch (Exception) { tokens.Clear(); tokenStatus = "Unavailable"; }
        return new Computer(fqdn, dn,
            primary ? "Primary" : "Nested/direct", tokenStatus, tokens);
    }

    private static IReadOnlyList<Level> ReadLevels(LdapConnection ldap,
        string computerParent, string baseDn)
    {
        var chain = new List<string>();
        string current = computerParent;
        for (int i = 0; i < 64; i++)
        {
            chain.Add(current);
            if (current.Equals(baseDn, StringComparison.OrdinalIgnoreCase)) break;
            current = NativeGpoConflictScan.ParentDn(current) ??
                throw new InvalidDataException("Cannot resolve OU ancestry.");
        }
        if (!chain[^1].Equals(baseDn, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("OU hierarchy could not be verified.");
        chain.Reverse();
        var levels = new List<Level>();
        for (int i = 0; i < chain.Count; i++)
        {
            var entry = One(ldap, chain[i], "(objectClass=*)", "gPLink", "gPOptions");
            string raw = NativeAdDiscovery.Attr(entry, "gPLink");
            if (raw.Length > 32000)
                throw new InvalidDataException("Too many OU GPO links.");
            var links = NativeGpoLifecycleList.ParseLinks(raw);
            if (links.Count > 120)
                throw new InvalidDataException("Too many linked policies at a single level.");
            int.TryParse(NativeAdDiscovery.Attr(entry, "gPOptions"), out int opts);
            levels.Add(new Level(chain[i], i, (opts & 1) != 0, links));
        }
        return levels;
    }

    private static Policy Inspect(LdapConnection ldap, string dc, string domain,
        string baseDn, NativeGpoLifecycleList.ParsedLink source,
        string linkedAt, string site, Computer computer, int depth,
        int order, bool blockedBelow)
    {
        if (!NativeGpoConflictScan.TryLinkedGuid(source.Dn, baseDn, out Guid id))
            throw new InvalidDataException("Linked policy is outside the verified AD domain.");
        var entry = One(ldap, source.Dn, "(objectClass=groupPolicyContainer)",
            "displayName", "flags", "gPCWQLFilter");
        string name = NativeAdDiscovery.Attr(entry, "displayName");
        if (name.Length == 0) name = id.ToString("D");
        if (!int.TryParse(NativeAdDiscovery.Attr(entry, "flags"), out int flags))
            flags = 0;
        string wmi = NativeAdDiscovery.Attr(entry, "gPCWQLFilter");
        bool enforced = (source.Flags & 2) != 0;
        bool machineOff = (flags & 2) != 0;
        string link = (source.Flags & 1) != 0 ? "Disabled link" :
            machineOff ? "Computer configuration disabled" :
            blockedBelow && !enforced ? "Blocked inheritance" : "Candidate";
        string security = CheckSecurity(ldap, source.Dn,
            computer.tokenSids, computer.tokenStatus);
        var details = ReadDetails(dc, domain, id);
        string category = link != "Candidate" ? "Excluded by link/config" :
            site.Length > 0 ? "Site applicability unknown" :
            security == "Matching deny ACE" ? "Security deny candidate" :
            security != "Read and Apply grants observed" ||
            wmi.Length > 0 || details.state != "Read" || details.scripts
                ? "Needs RSoP / verification" : "Potentially applicable";
        return new Policy(name, id.ToString("D"), linkedAt, site,
            link, order, depth, enforced, blockedBelow, machineOff,
            security, wmi.Length > 0 ? "Present - not evaluated" : "None",
            details.state, details.scripts, category, details.settings,
            "Estimated candidate only. Client RSoP, WMI result, GPP targeting and site membership unknown.");
    }

    private static string CheckSecurity(LdapConnection ldap, string dn,
        IReadOnlySet<string> principals, string tokenStatus)
    {
        if (tokenStatus != "Read" || principals.Count == 0) return "Unknown token";
        try
        {
            var req = new SearchRequest(dn, "(objectClass=groupPolicyContainer)",
                SearchScope.Base, "nTSecurityDescriptor");
            req.Controls.Add(new SecurityDescriptorFlagControl(SecurityMasks.Dacl));
            var response = (SearchResponse)ldap.SendRequest(req);
            if (response.Entries.Count != 1 ||
                response.Entries[0].Attributes["nTSecurityDescriptor"] is not { Count: > 0 } a ||
                a[0] is not byte[] bytes)
                return "ACL unreadable";
            var acl = new RawSecurityDescriptor(bytes, 0);
            bool readAllow = false, applyAllow = false, deny = false;
            if (acl.DiscretionaryAcl is null) return "ACL unreadable";
            foreach (GenericAce ace in acl.DiscretionaryAcl)
            {
                if ((ace.AceFlags & AceFlags.InheritOnly) != 0 ||
                    ace is not QualifiedAce q ||
                    !principals.Contains(q.SecurityIdentifier.Value))
                    continue;
                bool all = (q.AccessMask & 0x10000000) != 0;
                bool read = all || (q.AccessMask & 0x00020094) != 0 ||
                    (q.AccessMask & 0x10) != 0;
                bool apply = all || ((q.AccessMask & 0x100) != 0 &&
                    (q is not ObjectAce oa ||
                     (oa.ObjectAceFlags & ObjectAceFlags.ObjectAceTypePresent) == 0 ||
                     oa.ObjectAceType == ApplyPolicy));
                if (q.AceQualifier == AceQualifier.AccessDenied &&
                    (read || apply)) deny = true;
                if (q.AceQualifier == AceQualifier.AccessAllowed)
                {
                    if (read) readAllow = true;
                    if (apply) applyAllow = true;
                }
            }
            return deny ? "Matching deny ACE" :
                readAllow && applyAllow ? "Read and Apply grants observed" :
                "Read/Apply grants not established";
        }
        catch (Exception) { return "ACL unreadable"; }
    }

    private static Details ReadDetails(string dc, string domain, Guid id)
    {
        string machine = Path.Combine(@"\\", dc, "SYSVOL", domain,
            "Policies", "{" + id.ToString("D").ToUpperInvariant() + "}", "Machine");
        var settings = new List<Setting>();
        bool scripts = false;
        string state = "Read";
        try
        {
            FileAttributes folder = File.GetAttributes(machine);
            if ((folder & FileAttributes.Directory) == 0 ||
                (folder & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Unsafe SYSVOL machine policy directory.");
            using var scan = Directory.EnumerateFileSystemEntries(machine).GetEnumerator();
            _ = scan.MoveNext();
        }
        catch (Exception)
        {
            return new Details("Unavailable", false, settings);
        }
        foreach (string source in new[] { "Registry.pol", @"Preferences\Registry\Registry.xml" })
        {
            string path = Path.Combine(machine, source);
            try
            {
                FileAttributes flags;
                try { flags = File.GetAttributes(path); }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                if ((flags & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    throw new InvalidDataException("Unsafe policy registry path.");
                var info = new FileInfo(path);
                if (info.Length > 2 * 1_048_576)
                    throw new InvalidDataException("Oversized registry settings.");
                var bytes = File.ReadAllBytes(path);
                var found = source == "Registry.pol"
                    ? NativeGpoConflictScan.ParseRegistryPol(bytes)
                    : NativeGpoConflictScan.ParseGppRegistry(bytes);
                foreach (var hit in found)
                {
                    string value = hit.value.Length > 450
                        ? hit.value[..450] + " [truncated]" : hit.value;
                    settings.Add(new Setting(source, hit.key, hit.name,
                        value, hit.risk, hit.reason));
                    if (settings.Count > 120)
                        throw new InvalidDataException("Too many policy settings.");
                }
            }
            catch (Exception) { state = "Partial or unreadable"; }
        }
        foreach (string script in new[] { @"Scripts\psscripts.ini", @"Scripts\scripts.ini" })
        {
            try
            {
                FileAttributes flags = File.GetAttributes(Path.Combine(machine, script));
                if ((flags & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Unsafe startup script path.");
                scripts = true;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception) { state = "Partial or unreadable"; }
        }
        return new Details(state, scripts, settings);
    }

    private static SearchResultEntry One(LdapConnection ldap, string dn,
        string filter, params string[] attrs)
    {
        var result = (SearchResponse)ldap.SendRequest(
            new SearchRequest(dn, filter, SearchScope.Base, attrs));
        if (result.Entries.Count != 1)
            throw new InvalidDataException("Required AD object was not found.");
        return result.Entries[0];
    }

    private static string Required(SearchResultEntry entry, string name)
    {
        string value = NativeAdDiscovery.Attr(entry, name);
        return value.Length == 0 ? throw new InvalidDataException("Missing required AD field: " + name)
            : value;
    }

    private static string Get(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var item) &&
        item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string user, string? domain,
        string password, int logonType, int provider, out SafeAccessTokenHandle token);
}
