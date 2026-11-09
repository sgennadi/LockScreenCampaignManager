using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// Native, read-only enumeration of GPOs managed by LockScreen Campaign Manager.
/// Reads AD through signed/sealed LDAP and verifies startup scripts and campaign
/// manifests from the selected DC's SYSVOL. No GPO writes, PowerShell or RSAT.
/// Preview/Apply continue using the existing shared plan-hash engine.
/// </summary>
internal static class NativeGpoLifecycleList
{
    private const int MaxGpos = 2500;
    private const int MaxLinks = 12000;
    private const int MaxContainers = 10000;
    private static readonly TimeSpan AuditTimeout = TimeSpan.FromMinutes(4);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Regex LinkPattern = new(
        @"\[LDAP://(?<dn>[^;\]]+);(?<flags>\d+)\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));
    private static readonly Regex ScriptPattern = new(
        @"(?im)^0CmdLine=(?<script>\\\\[^\r\n]+\\scripts\\LockScreenCampaigns\\(?<id>[a-z0-9_-]+)\\LockScreen-Agent\.ps1)\s*$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex ParametersPattern = new(
        @"(?im)^0Parameters=-Mode Install\s*$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex ExtraScriptPattern = new(
        @"(?im)^1CmdLine=", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private sealed record LinkInfo(
        string ou, int flags, bool enabled, bool enforced,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? type);
    internal sealed record ManagedInfo(string campaignId, string end, string timeZone);
    private sealed record PolicyResult(
        string id, string name, string campaignId, string end,
        string timeZone, LinkInfo[] links, int linkCount, int gpoFlags);

    internal static JsonElement Execute(string mode, object request, DomainCredentials credentials)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("GPO inventory requires Windows LDAP and SMB.");
        if (mode != "List")
            throw new ArgumentException("Only read-only List is supported by the native engine.");

        JsonElement args = JsonSerializer.SerializeToElement(request, Json);
        string domain = Get(args, "domain").Trim().ToLowerInvariant();
        string requestedDc = Get(args, "dc").Trim();
        NativeAdDiscovery.ValidateNames(domain, requestedDc);
        if (requestedDc.Length == 0)
            throw new ArgumentException("Choose a domain controller before listing managed GPOs.");

        JsonElement Run()
        {
            using var ldap = NativeAdDiscovery.Connect(requestedDc, credentials);
            ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            var root = NativeAdDiscovery.ReadRootDse(ldap);
            string baseDn = Required(root, "defaultNamingContext");
            string actualDc = Required(root, "dnsHostName");
            string configDn = Required(root, "configurationNamingContext");
            if (!NativeAdDiscovery.DomainFromDn(baseDn).Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                !SameController(requestedDc, actualDc))
                throw new InvalidOperationException(
                    "Connected controller identity does not match the requested AD domain/DC.");
            // Preserve the legacy lifecycle requirement. Unknown RODC state
            // is not evidence of a writable controller.
            if (NativeAdDiscovery.ReadDcWritable(ldap, root) != true)
                throw new InvalidOperationException("A writable AD controller must be verified.");

            var timer = Stopwatch.StartNew();
            var linksByGpo = new Dictionary<string, List<LinkInfo>>(StringComparer.OrdinalIgnoreCase);
            CollectLinks(ldap, actualDc, baseDn,
                "(|(objectClass=organizationalUnit)(objectClass=domainDNS))",
                "Domain", linksByGpo, timer);
            CollectLinks(ldap, actualDc, "CN=Sites," + configDn,
                "(objectClass=site)", "Site", linksByGpo, timer);

            string policiesDn = "CN=Policies,CN=System," + baseDn;
            var managed = new List<PolicyResult>();
            foreach (var entry in SearchPaged(ldap, policiesDn,
                         "(objectClass=groupPolicyContainer)", MaxGpos,
                         timer, "name", "displayName", "flags"))
            {
                string rawGuid = Required(entry, "name");
                if (!Guid.TryParseExact(rawGuid, "B", out Guid id) || id == Guid.Empty)
                    throw new InvalidDataException("Directory returned a malformed GPO identity.");

                ManagedInfo? info = ReadManagedGpo(actualDc, domain, id);
                if (info is null) continue;

                string key = PolicyDn(id, baseDn);
                LinkInfo[] links = linksByGpo.TryGetValue(key, out var stored)
                    ? stored.ToArray() : [];
                string display = NativeAdDiscovery.Attr(entry, "displayName");
                string rawFlags = NativeAdDiscovery.Attr(entry, "flags");
                if (rawFlags.Length != 0 &&
                    !int.TryParse(rawFlags, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out _))
                    throw new InvalidDataException("Directory returned invalid GPO flags.");
                int flags = rawFlags.Length == 0 ? 0 :
                    int.Parse(rawFlags, CultureInfo.InvariantCulture);

                managed.Add(new PolicyResult(id.ToString("D"), display,
                    info.campaignId, info.end, info.timeZone,
                    links, links.Length, flags));
            }

            // GPOs not matched to the exact embedded startup-script signature
            // are deliberately not displayed as application-owned policies.
            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                message = "Native read-only list of verified managed GPOs.",
                data = managed.OrderBy(x => x.name,
                    StringComparer.OrdinalIgnoreCase).ToArray()
            }, Json);
        }

        if (!credentials.IsAlternate) return Run();

        // Both LDAP and SMB must use the explicitly provided network identity.
        // LOGON_NETCREDENTIALS_ONLY avoids elevating or changing local rights.
        string user = credentials.UserName.Trim();
        string? userDomain = null;
        int slash = user.IndexOf('\\');
        if (slash > 0 && slash < user.Length - 1)
        {
            userDomain = user[..slash];
            user = user[(slash + 1)..];
        }
        else if (!user.Contains('@'))
            user += "@" + credentials.Domain.Trim();
        if (credentials.Password.Length == 0 ||
            !LogonUser(user, userDomain, credentials.Password, 9, 3,
                out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not establish alternate network identity for managed GPO inventory.");
        using (token)
            return WindowsIdentity.RunImpersonated(token, Run);
    }

    private static string Get(JsonElement source, string key) =>
        source.ValueKind == JsonValueKind.Object &&
        source.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    internal static bool SameController(string requested, string actual) =>
        actual.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
        (requested.IndexOf('.') < 0 &&
         actual.Split('.')[0].Equals(requested, StringComparison.OrdinalIgnoreCase));

    internal static string PolicyDn(Guid id, string baseDn) =>
        "CN={" + id.ToString("D").ToUpperInvariant() +
        "},CN=Policies,CN=System," + baseDn;

    private static string Required(SearchResultEntry entry, string attr)
    {
        string value = NativeAdDiscovery.Attr(entry, attr);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("The selected DC omitted required AD information.");
        return value;
    }

    internal static List<SearchResultEntry> SearchPaged(
        LdapConnection ldap, string rootDn, string filter, int max,
        Stopwatch timer, params string[] attributes)
    {
        var all = new List<SearchResultEntry>();
        byte[] cookie = [];
        do
        {
            if (timer.Elapsed > AuditTimeout)
                throw new TimeoutException("Managed GPO listing exceeded its time limit.");
            var request = new SearchRequest(rootDn, filter, SearchScope.Subtree, attributes);
            request.Controls.Add(new PageResultRequestControl(250) { Cookie = cookie });
            var response = (SearchResponse)ldap.SendRequest(request);
            foreach (SearchResultEntry result in response.Entries)
            {
                if (all.Count >= max)
                    throw new InvalidDataException("Directory inventory exceeds bounded audit limit.");
                all.Add(result);
            }
            cookie = [];
            foreach (DirectoryControl control in response.Controls)
                if (control is PageResultResponseControl page)
                    cookie = page.Cookie ?? [];
        }
        while (cookie.Length > 0);
        return all;
    }

    private static void CollectLinks(
        LdapConnection ldap, string selectedDc, string dn, string filter, string kind,
        Dictionary<string, List<LinkInfo>> result, Stopwatch timer)
    {
        int counted = result.Values.Sum(x => x.Count);
        foreach (var entry in SearchPaged(ldap, dn, filter, MaxContainers,
                     timer, "gPLink"))
        {
            string raw = NativeAdDiscovery.Attr(entry, "gPLink");
            if (raw.Length == 0) continue;
            var found = ParseLinks(raw);
            foreach (var item in found)
            {
                if (++counted > MaxLinks)
                    throw new InvalidDataException("Linked GPO count exceeds safe listing limit.");
                if (!result.TryGetValue(item.Dn, out var list))
                {
                    list = [];
                    result.Add(item.Dn, list);
                }
                string place = kind == "Site"
                    ? "LDAP://" + selectedDc + "/" + entry.DistinguishedName
                    : entry.DistinguishedName;
                list.Add(new LinkInfo(place, item.Flags,
                    (item.Flags & 1) == 0, (item.Flags & 2) != 0,
                    kind == "Site" ? "Site" : null));
            }
        }
    }

    internal sealed record ParsedLink(string Dn, int Flags);

    internal static IReadOnlyList<ParsedLink> ParseLinks(string raw)
    {
        if (raw.Length > 1_000_000)
            throw new InvalidDataException("GPO link value exceeds read-only audit bounds.");
        var matches = LinkPattern.Matches(raw);
        var parsed = new List<ParsedLink>(matches.Count);
        foreach (System.Text.RegularExpressions.Match link in matches)
        {
            string dn = link.Groups["dn"].Value;
            if (!int.TryParse(link.Groups["flags"].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int flags) ||
                flags < 0 || flags > 3 ||
                !dn.StartsWith("CN={", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Malformed linked GPO attribute.");
            parsed.Add(new ParsedLink(dn, flags));
        }
        if (!string.IsNullOrWhiteSpace(LinkPattern.Replace(raw, "")))
            throw new InvalidDataException("Incomplete linked GPO attribute; listing refused.");
        return parsed;
    }

    internal static bool IsManagedStartup(string startup, string domain,
        out string campaignId)
    {
        campaignId = "";
        var commands = ScriptPattern.Matches(startup);
        if (commands.Count != 1 ||
            !ParametersPattern.IsMatch(startup) ||
            ExtraScriptPattern.IsMatch(startup))
            return false;
        string id = commands[0].Groups["id"].Value;
        if (!NativeSysvolStorage.ValidCampaignId(id)) return false;
        string path = commands[0].Groups["script"].Value;
        string expected = @"\\" + domain + @"\SYSVOL\" + domain +
            @"\scripts\LockScreenCampaigns\" + id + @"\LockScreen-Agent.ps1";
        if (!path.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return false;
        campaignId = id;
        return true;
    }

    private static bool ExistsStrict(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        // Network failure, ACL denial, malformed paths and IO errors
        // propagate rather than erasing a managed-policy audit result.
    }

    internal static ManagedInfo? ReadManagedGpo(string dc, string domain, Guid id)
    {
        string policyDir = @"\\" + dc + @"\SYSVOL\" + domain + @"\Policies\{" +
            id.ToString("D").ToUpperInvariant() + "}";
        string scripts = Path.Combine(policyDir, "Machine", "Scripts");
        string psIni = Path.Combine(scripts, "psscripts.ini");
        string classicIni = Path.Combine(scripts, "scripts.ini");
        // File.Exists silently returns false for access denied, broken SMB
        // and several IO failures. Those must NEVER become evidence that a
        // selected GPO lacks a startup method.
        bool hasPowerShell = ExistsStrict(psIni);
        bool hasClassic = ExistsStrict(classicIni);
        if (!hasPowerShell && !hasClassic) return null;
        if (hasPowerShell && hasClassic)
            return null; // Mixed startup methods are not a managed signature.
        string ini = hasPowerShell ? psIni : classicIni;
        bool executable = hasClassic;
        FileAttributes attr = File.GetAttributes(ini);
        if ((attr & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("A GPO script file is unsafe.");
        if (new FileInfo(ini).Length > 1_048_576)
            throw new InvalidDataException("GPO startup list exceeds audit limits.");
        string startup = File.ReadAllText(ini, Encoding.Unicode);
        string campaignId;
        if (!executable)
        {
            if (!IsManagedStartup(startup, domain, out campaignId))
                return null;
        }
        else
        {
            // scripts.ini names the executable directly with a separate,
            // fixed --client-agent Install argument, as specified by
            // Microsoft MS-GPSCR. No intermediate CMD is accepted.
            var pathMatch = Regex.Match(startup,
                @"(?im)^0CmdLine=(?<file>\\\\[^\r\n]+\\LockScreenCampaigns\\(?<id>[a-z0-9_-]+)\\LockScreen-Agent\.exe)\s*$",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!pathMatch.Success ||
                !Regex.IsMatch(startup, @"(?im)^0Parameters=--client-agent Install\s*$"))
                return null;
            campaignId = pathMatch.Groups["id"].Value;
            if (!NativeSysvolStorage.ValidCampaignId(campaignId))
                return null;
            string exe = @"\\" + domain + @"\SYSVOL\" + domain +
                @"\scripts\LockScreenCampaigns\" + campaignId +
                @"\LockScreen-Agent.exe";
            string expectedIni = "[Startup]\r\n0CmdLine=" + exe +
                "\r\n0Parameters=--client-agent Install\r\n";
            if (!string.Equals(startup, expectedIni, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        string root = @"\\" + dc + @"\SYSVOL\" + domain +
            @"\scripts\LockScreenCampaigns\" + campaignId;
        string manifestPath = Path.Combine(root, "campaign.json");
        if ((File.GetAttributes(manifestPath) &
             (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Managed GPO manifest is unsafe.");
        if (new FileInfo(manifestPath).Length is < 10 or > 1_048_576)
            throw new InvalidDataException("Managed GPO manifest size is invalid.");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(manifestPath),
            new JsonDocumentOptions { MaxDepth = 12 });
        JsonElement manifest = doc.RootElement;
        if (manifest.ValueKind != JsonValueKind.Object ||
            manifest.GetProperty("schemaVersion").GetInt32() != 1 ||
            !string.Equals(manifest.GetProperty("campaignId").GetString(),
                campaignId, StringComparison.Ordinal))
            throw new InvalidDataException(
                "Managed GPO campaign identity has diverged from SYSVOL.");
        CampaignClientType kind = NativeGpoDeploymentPlan.ParseClientType(
            manifest.TryGetProperty("clientType", out var modeField) &&
            modeField.ValueKind == JsonValueKind.String ? modeField.GetString() ?? "" : "");
        if ((kind == CampaignClientType.Executable) != executable)
            throw new InvalidDataException("GPO startup method differs from campaign manifest.");
        string end = manifest.GetProperty("endLocal").GetString() ?? "";
        string zone = manifest.GetProperty("timeZoneId").GetString() ?? "";
        if (end.Length == 0 || zone.Length == 0)
            throw new InvalidDataException("Managed GPO campaign schedule is incomplete.");
        return new ManagedInfo(campaignId, end, zone);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string username, string? domain,
        string password, int logonType, int provider, out SafeAccessTokenHandle token);
}
