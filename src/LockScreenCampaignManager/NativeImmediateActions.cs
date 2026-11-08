using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only eligible computer listing and guarded immediate-action Preview.
/// The actual remote Push/Repair/Report/Cleanup still runs through the legacy
/// PowerShell Task Scheduler RPC engine and independently re-checks the
/// complete OU/group/GPO/SYSVOL scope and plan hash immediately before writing.
/// This module does not create scheduled tasks, modify GPOs or contact clients.
/// </summary>
internal static class NativeImmediateActions
{
    private const int MaxEligible = 2000;
    private const int MaxSelected = 25;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex HostRegex = new(
        @"\A[A-Za-z0-9][A-Za-z0-9.-]{1,253}\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly HashSet<string> Purposes = new(
        ["Push", "Cleanup", "Report", "Repair"], StringComparer.Ordinal);
    internal sealed record EligibleComputer(string name, string sam, string dn);

    internal static JsonElement Execute(string mode, object request,
        DomainCredentials credentials)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows signed/sealed LDAP is required.");
        if (mode is not ("List" or "Preview"))
            throw new ArgumentException("Only read-only List and Preview are native.");
        JsonElement args = JsonSerializer.SerializeToElement(request, Json);
        string domain = Text(args, "domain").Trim().ToLowerInvariant();
        string selectedDc = Text(args, "dc").Trim();
        string ouDn = Text(args, "ouDn").Trim();
        string groupDn = Text(args, "groupDn").Trim();
        NativeAdDiscovery.ValidateNames(domain, selectedDc);
        if (selectedDc.Length == 0)
            throw new ArgumentException("Select an explicit AD domain controller.");
        if (ouDn.Length is < 8 or > 2048 || groupDn.Length is < 8 or > 2048 ||
            ouDn.Any(char.IsControl) || groupDn.Any(char.IsControl))
            throw new ArgumentException("OU or security group DN is invalid.");

        JsonElement Run()
        {
            using var ldap = NativeAdDiscovery.Connect(selectedDc, credentials);
            ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            var root = NativeAdDiscovery.ReadRootDse(ldap);
            string baseDn = Required(root, "defaultNamingContext");
            string dc = Required(root, "dnsHostName");
            string dns = NativeGpoLifecyclePreview.DomainFromDnPreserveCase(baseDn);
            if (!dns.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                !NativeGpoLifecycleList.SameController(selectedDc, dc))
                throw new InvalidOperationException(
                    "Selected AD domain and controller identity could not be verified.");
            if (!ouDn.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
                !groupDn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ||
                !NativeGpoConflictScan.Inside(ouDn, baseDn) ||
                !NativeGpoConflictScan.Inside(groupDn, baseDn))
                throw new InvalidOperationException(
                    "Computer OU and security group must belong to the selected domain.");

            _ = ReadOne(ldap, ouDn, "(objectClass=organizationalUnit)", "distinguishedName");
            var group = ReadOne(ldap, groupDn, "(objectClass=group)", "groupType");
            if (!long.TryParse(Required(group, "groupType"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out long groupType) ||
                (groupType & 0x80000000L) == 0)
                throw new InvalidOperationException("Selected AD object is not a security group.");

            var candidates = Eligible(ldap, ouDn, groupDn, dns);
            if (mode == "List")
                return JsonSerializer.SerializeToElement(new
                {
                    success = true,
                    message = "Eligible AD computers in the selected OU and nested security group. No changes made.",
                    data = candidates
                }, Json);

            string purpose = Text(args, "purpose");
            if (purpose.Length == 0) purpose = "Push";
            if (!Purposes.Contains(purpose))
                throw new ArgumentException("Unsupported immediate action purpose.");
            string gpoText = Text(args, "gpoId");
            if (!Guid.TryParse(gpoText, out Guid gpoId) || gpoId == Guid.Empty)
                throw new ArgumentException("Choose a valid managed GPO.");
            var selected = Selected(args, candidates);
            string gpoDn = NativeGpoLifecycleList.PolicyDn(gpoId, baseDn);
            var gpo = ReadOne(ldap, gpoDn, "(objectClass=groupPolicyContainer)", "flags");
            string flagText = NativeAdDiscovery.Attr(gpo, "flags");
            if (flagText.Length > 0 &&
                !int.TryParse(flagText, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out _))
                throw new InvalidDataException("Invalid GPO flags.");
            int flags = flagText.Length == 0 ? 0 :
                int.Parse(flagText, CultureInfo.InvariantCulture);
            bool allowDisabled = purpose is "Cleanup" or "Report";
            if (!allowDisabled && (flags & 2) != 0)
                throw new InvalidOperationException("Computer GPO configuration is disabled.");

            var ouEntry = ReadOne(ldap, ouDn, "(objectClass=organizationalUnit)", "gPLink");
            string rawLinks = NativeAdDiscovery.Attr(ouEntry, "gPLink");
            var linked = NativeGpoLifecycleList.ParseLinks(rawLinks)
                .Where(x => x.Dn.Equals(gpoDn, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (linked.Length > 1)
                throw new InvalidOperationException("Duplicate GPO links; refusing remote-action preview.");
            if (!allowDisabled && (linked.Length == 0 || (linked[0].Flags & 1) != 0))
                throw new InvalidOperationException("GPO must have an enabled link at the selected OU.");

            // Stronger-than-legacy exact managed signature, rather than trusting
            // any arbitrary startup script whose path resembles ours.
            var managed = NativeGpoLifecycleList.ReadManagedGpo(dc, dns, gpoId)
                ?? throw new InvalidOperationException("Selected GPO is not a managed campaign.");
            string manifest = @"\\" + dc + @"\SYSVOL\" + dns +
                @"\scripts\LockScreenCampaigns\" + managed.campaignId + @"\campaign.json";
            byte[] content = ReadManifest(manifest);
            using var doc = JsonDocument.Parse(content,
                new JsonDocumentOptions { MaxDepth = 12 });
            var config = doc.RootElement;
            if (!string.Equals(Text(config, "campaignId"), managed.campaignId,
                    StringComparison.Ordinal))
                throw new InvalidDataException("Managed campaign manifest ID changed.");
            string end = Text(config, "endLocal");
            string start = "";
            string zoneId = Text(config, "timeZoneId");
            if (config.TryGetProperty("slides", out JsonElement slides) &&
                slides.ValueKind == JsonValueKind.Array && slides.GetArrayLength() > 0)
                start = Text(slides[0], "atLocal");
            if (start.Length == 0 || end.Length == 0 || zoneId.Length == 0)
                throw new InvalidDataException("Campaign schedule is incomplete.");
            DateTime endLocal = DateTime.ParseExact(end, "yyyy-MM-ddTHH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            if (zone.IsAmbiguousTime(endLocal) || zone.IsInvalidTime(endLocal))
                throw new InvalidDataException("Managed campaign has an invalid end time.");
            if ((purpose is "Push" or "Repair") &&
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone) >= endLocal)
                throw new InvalidOperationException(
                    "Campaign has ended; reuse the GPO with a new campaign.");

            string manifestHash = Convert.ToHexString(SHA256.HashData(content));
            string digest = LegacyPlanHash(dns, dc, ouDn, groupDn, gpoId,
                managed.campaignId, purpose, manifestHash, selected);
            string note = purpose switch
            {
                "Push" => "Push installs immediately, but the image only appears when scheduled start has passed.",
                "Repair" => "Repair checks the trusted SYSVOL package and repairs files. Original registry backup/owner remain protected.",
                "Report" => "Report publishes a fresh client heartbeat; it does not change registry or GPO.",
                _ => "Cleanup restores original registry values and removes local campaign tasks."
            };
            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                message = "Native read-only remote action preview. No tasks created.",
                data = new
                {
                    computers = selected,
                    count = selected.Length,
                    campaignId = managed.campaignId,
                    purpose,
                    gpoId = gpoId.ToString("D"),
                    hash = digest,
                    start, end,
                    note = note + " Requires Task Scheduler RPC/firewall and local admin on each PC."
                }
            }, Json);
        }

        if (!credentials.IsAlternate) return Run();

        // Network-only credentials for both signed LDAP and selected-DC SMB.
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
                "Cannot use the supplied account for read-only LDAP/SYSVOL preview.");
        using (token)
            return WindowsIdentity.RunImpersonated(token, Run);
    }

    private static string Text(JsonElement args, string key) =>
        args.ValueKind == JsonValueKind.Object &&
        args.TryGetProperty(key, out JsonElement field) &&
        field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";

    private static string Required(SearchResultEntry result, string key)
    {
        string value = NativeAdDiscovery.Attr(result, key);
        if (value.Length == 0) throw new InvalidDataException("Required AD attribute is missing: " + key);
        return value;
    }

    private static SearchResultEntry ReadOne(LdapConnection ldap, string dn,
        string filter, params string[] attrs)
    {
        var response = (SearchResponse)ldap.SendRequest(
            new SearchRequest(dn, filter, SearchScope.Base, attrs));
        if (response.Entries.Count != 1)
            throw new InvalidDataException("Selected AD object cannot be uniquely verified.");
        return response.Entries[0];
    }

    private static EligibleComputer[] Eligible(LdapConnection ldap, string ouDn,
        string groupDn, string domain)
    {
        string filter = "(&(objectCategory=computer)(memberOf:1.2.840.113556.1.4.1941:=" +
            NativeAdDiscovery.EscapeFilter(groupDn) + "))";
        var entries = NativeGpoLifecycleList.SearchPaged(ldap,
            ouDn, filter, MaxEligible, Stopwatch.StartNew(),
            "sAMAccountName", "dNSHostName", "distinguishedName");
        var list = new List<EligibleComputer>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            string fqdn = NativeAdDiscovery.Attr(entry, "dNSHostName");
            string sam = NativeAdDiscovery.Attr(entry, "sAMAccountName");
            if (!ValidHost(fqdn, domain)) continue;
            if (!unique.Add(fqdn))
                throw new InvalidDataException("Duplicate AD computer DNS identities found.");
            list.Add(new EligibleComputer(fqdn, sam, entry.DistinguishedName));
        }
        return list.OrderBy(x => x.name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static bool ValidHost(string host, string domain) =>
        host.Length is >= 3 and <= 253 &&
        HostRegex.IsMatch(host) &&
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase) &&
        host.Split('.').All(label => label.Length is >= 1 and <= 63 &&
            !label.StartsWith('-') && !label.EndsWith('-'));

    private static string[] Selected(JsonElement args,
        IReadOnlyList<EligibleComputer> eligible)
    {
        if (!args.TryGetProperty("computers", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array ||
            nodes.GetArrayLength() is < 1 or > MaxSelected)
            throw new ArgumentException("Choose between 1 and 25 computers.");
        var allow = eligible.Select(x => x.name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selected = new List<string>();
        foreach (JsonElement node in nodes.EnumerateArray())
        {
            string host = node.ValueKind == JsonValueKind.String
                ? node.GetString()?.Trim() ?? "" : "";
            if (!allow.Contains(host) || !distinct.Add(host))
                throw new InvalidOperationException(
                    "Computer is not in the approved OU/group or appears twice.");
            selected.Add(host);
        }
        return selected.ToArray();
    }

    private static byte[] ReadManifest(string path)
    {
        var attrs = File.GetAttributes(path);
        if ((attrs & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("Campaign manifest cannot be a link or directory.");
        var info = new FileInfo(path);
        if (info.Length is < 10 or > 1_048_576)
            throw new InvalidDataException("Campaign manifest size is unexpected.");
        using var stream = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.Read);
        if (stream.Length != info.Length)
            throw new InvalidDataException("Campaign manifest changed during read.");
        byte[] bytes = new byte[checked((int)info.Length)];
        stream.ReadExactly(bytes);
        if (stream.Length != info.Length)
            throw new InvalidDataException("Campaign manifest changed during read.");
        return bytes;
    }

    internal static string LegacyPlanHash(string domain, string dc, string ouDn,
        string groupDn, Guid gpoId, string campaignId, string purpose,
        string manifestHash, IEnumerable<string> selected)
    {
        if (!Purposes.Contains(purpose) ||
            manifestHash.Length != 64 ||
            !manifestHash.All(Uri.IsHexDigit) ||
            !NativeSysvolStorage.ValidCampaignId(campaignId))
            throw new InvalidDataException("Invalid immediate action preview digest inputs.");
        string sorted = string.Join(",",
            selected.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase));
        string source = string.Join("|", domain, dc, ouDn, groupDn,
            gpoId.ToString("D"), campaignId, purpose,
            manifestHash.ToUpperInvariant(), sorted);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string username, string? domain,
        string password, int logonType, int provider, out SafeAccessTokenHandle token);
}
