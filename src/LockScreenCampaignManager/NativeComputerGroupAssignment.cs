using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Narrowly scoped computer to security-group assignment using signed/sealed
/// native LDAP. Search/Preview never write. Apply changes one direct member
/// only after a five-minute preview, typed SAM approval, and fresh AD checks.
/// No RSAT, PowerShell, remote client RPC or GPO modifications.
/// </summary>
internal static class NativeComputerGroupAssignment
{
    private const int SearchLimit = 150;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> PrivilegedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Domain Admins", "Enterprise Admins", "Administrators",
        "Schema Admins", "Account Operators", "Server Operators",
        "Backup Operators", "Group Policy Creator Owners"
    };

    private sealed record Scope(
        string Domain, string Dc, string BaseDn, string OuDn, string GroupDn,
        Guid GroupGuid, long GroupUsn, string GroupSam, int GroupRid, bool Writable);

    private sealed record Computer(
        string Dn, string Sam, Guid Guid, string Name, int PrimaryGroupRid,
        IReadOnlyList<string> DirectMemberOf);

    private sealed record MembershipPlan(
        string domain, string dc, string ouDn, string groupDn,
        string groupSam, string groupGuid, string computerDn,
        string computerSam, string computerName, string computerGuid,
        int primaryGroupRid, string action, bool directMember,
        bool effectiveMember, bool writable, string previewUtc, string hash,
        string message);

    internal static JsonElement Execute(string mode, object request,
        DomainCredentials credentials)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native AD group membership needs Windows LDAP authentication.");
        if (mode is not ("Search" or "Preview" or "Apply"))
            throw new ArgumentException("Unsupported group membership operation.", nameof(mode));

        var json = JsonSerializer.SerializeToElement(request, Json);
        string domain = Text(json, "domain").Trim().ToLowerInvariant();
        string dc = Text(json, "dc").Trim();
        NativeAdDiscovery.ValidateNames(domain, dc);
        if (dc.Length == 0) throw new ArgumentException("Select an explicit domain controller.");
        string ouDn = Text(json, "ouDn").Trim();
        string groupDn = Text(json, "groupDn").Trim();
        using var ldap = NativeAdDiscovery.Connect(dc, credentials);
        ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        Scope scope = ReadScope(ldap, domain, dc, ouDn, groupDn);
        object data;
        string message;
        switch (mode)
        {
            case "Search":
                data = FindComputers(ldap, scope, Text(json, "search"));
                message = "Signed LDAP computer search completed; no group membership changed.";
                break;
            case "Preview":
                data = CreatePlan(ldap, scope, Text(json, "computerDn"),
                    Text(json, "action"), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                message = "Read-only native LDAP membership preview completed.";
                break;
            default:
                data = Apply(ldap, scope, json);
                message = "Approved single-computer membership change verified on the selected DC.";
                break;
        }
        return JsonSerializer.SerializeToElement(new { success = true, message, data }, Json);
    }

    private static string Text(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el) &&
        el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";

    internal static bool Inside(string dn, string parent) =>
        dn.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        dn.EndsWith("," + parent, StringComparison.OrdinalIgnoreCase);

    internal static int GroupRid(string sid)
    {
        if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) ||
            !int.TryParse(sid[(sid.LastIndexOf('-') + 1)..],
                NumberStyles.None, CultureInfo.InvariantCulture, out int rid) ||
            rid < 1000)
            throw new InvalidOperationException("Built-in/domain-default security groups cannot be modified.");
        return rid;
    }

    internal static string PlanDigest(IEnumerable<string> fields) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("|", fields).ToLowerInvariant())));

    internal static bool PreviewFresh(string value, DateTimeOffset now)
    {
        // The preview engine writes DateTime.UtcNow in the roundtrip "o"
        // format ending in Z. Parse it as UTC DateTime, not DateTimeOffset:
        // DateTimeOffset's "o" format expects a numeric offset.
        if (!DateTime.TryParseExact(value, "o", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed) ||
            parsed.Kind != DateTimeKind.Utc)
            return false;
        double minutes = (now.UtcDateTime - parsed).TotalMinutes;
        return minutes >= 0 && minutes <= 5;
    }

    private static byte[] Binary(SearchResultEntry entry, string name)
    {
        if (!entry.Attributes.Contains(name) || entry.Attributes[name] is not { Count: > 0 } attr ||
            attr[0] is not byte[] bytes)
            throw new InvalidOperationException("Required AD object identity is unavailable.");
        return bytes;
    }

    private static SearchResultEntry ReadOne(LdapConnection ldap,
        string dn, string filter, params string[] attrs)
    {
        var request = new SearchRequest(dn, filter, SearchScope.Base, attrs);
        var response = (SearchResponse)ldap.SendRequest(request);
        if (response.Entries.Count != 1)
            throw new InvalidOperationException("Target AD object is missing or has an unexpected object class.");
        return response.Entries[0];
    }

    private static string Required(SearchResultEntry entry, string attr)
    {
        string value = NativeAdDiscovery.Attr(entry, attr);
        if (value.Length == 0)
            throw new InvalidOperationException("Required AD property was not returned: " + attr);
        return value;
    }

    private static Scope ReadScope(LdapConnection ldap, string domain, string dc,
        string ouDn, string groupDn)
    {
        if (ouDn.Length > 2000 || groupDn.Length > 2000 ||
            ouDn.Any(char.IsControl) || groupDn.Any(char.IsControl))
            throw new InvalidDataException("AD target is too large or contains control characters.");
        var root = NativeAdDiscovery.ReadRootDse(ldap);
        string baseDn = Required(root, "defaultNamingContext");
        string actualDomain = NativeAdDiscovery.DomainFromDn(baseDn);
        string actualDc = Required(root, "dnsHostName");
        if (!actualDomain.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            !(actualDc.Equals(dc, StringComparison.OrdinalIgnoreCase) ||
              actualDc.Split('.')[0].Equals(dc, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Selected AD controller identity does not match the requested domain.");
        // Missing isRODC is not proof of a writable DC.
        bool writable = NativeAdDiscovery.Attr(root, "isRODC")
            .Equals("FALSE", StringComparison.OrdinalIgnoreCase);

        if (!ouDn.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
            !Inside(ouDn, baseDn) ||
            !groupDn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ||
            !Inside(groupDn, baseDn))
            throw new InvalidDataException("Computer OU and security group must belong to the selected AD domain.");

        _ = ReadOne(ldap, ouDn, "(objectClass=organizationalUnit)", "distinguishedName");
        var group = ReadOne(ldap, groupDn, "(objectClass=group)",
            "objectGUID", "objectSid", "groupType", "adminCount",
            "isCriticalSystemObject", "sAMAccountName", "uSNChanged");

        if (NativeAdDiscovery.Attr(group, "adminCount") == "1" ||
            NativeAdDiscovery.Attr(group, "isCriticalSystemObject")
                .Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Protected or critical AD security groups cannot be changed.");

        if (!long.TryParse(Required(group, "groupType"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long groupType) ||
            (groupType & 0x80000000L) == 0)
            throw new InvalidOperationException("Selected AD group is not security-enabled.");

        string groupSam = Required(group, "sAMAccountName");
        if (PrivilegedNames.Contains(groupSam))
            throw new InvalidOperationException("Privileged domain groups cannot be changed here.");

        var sid = new SecurityIdentifier(Binary(group, "objectSid"), 0);
        int rid = GroupRid(sid.Value);
        Guid guid = new Guid(Binary(group, "objectGUID"));
        if (guid == Guid.Empty ||
            !long.TryParse(Required(group, "uSNChanged"), NumberStyles.None,
                CultureInfo.InvariantCulture, out long groupUsn) || groupUsn <= 0)
            throw new InvalidOperationException("Cannot verify group GUID or change sequence.");

        return new Scope(domain, dc, baseDn, ouDn, groupDn,
            guid, groupUsn, groupSam, rid, writable);
    }

    private static Computer ReadComputer(LdapConnection ldap, Scope scope, string dn,
        SearchResultEntry? supplied = null)
    {
        if (dn.Length > 2000 || dn.Length == 0 || dn.Any(char.IsControl) ||
            !Inside(dn, scope.OuDn) || !Inside(dn, scope.BaseDn))
            throw new InvalidOperationException("Computer is outside the selected OU subtree.");
        var result = supplied ?? ReadOne(ldap, dn, "(objectClass=computer)",
            "distinguishedName", "objectGUID", "name",
            "sAMAccountName", "primaryGroupID", "memberOf");
        string actualDn = Required(result, "distinguishedName");
        string sam = Required(result, "sAMAccountName");
        if (!actualDn.Equals(dn, StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(sam, @"\A[A-Za-z0-9_.-]{1,60}\$\z"))
            throw new InvalidOperationException("Computer account identity is invalid or changed.");
        if (!int.TryParse(Required(result, "primaryGroupID"),
                NumberStyles.None, CultureInfo.InvariantCulture, out int primaryRid) ||
            primaryRid <= 0)
            throw new InvalidOperationException("Computer primary group is unverifiable.");
        var guid = new Guid(Binary(result, "objectGUID"));
        if (guid == Guid.Empty) throw new InvalidOperationException("Invalid computer GUID.");

        // An AD ranged memberOf result is not an authoritative full list.
        foreach (string attrName in result.Attributes.AttributeNames)
            if (attrName.StartsWith("memberOf;range=", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Incomplete AD membership range; refusing changes.");
        var memberships = new List<string>();
        if (result.Attributes.Contains("memberOf") &&
            result.Attributes["memberOf"] is { } attr)
            foreach (object value in attr)
                if (value is string member)
                    memberships.Add(member);
        return new Computer(actualDn, sam, guid, Required(result, "name"),
            primaryRid, memberships);
    }

    private static bool Direct(Computer computer, string groupDn) =>
        computer.DirectMemberOf.Any(x =>
            x.Equals(groupDn, StringComparison.OrdinalIgnoreCase));

    private static bool Effective(LdapConnection ldap, Computer computer, string groupDn)
    {
        if (Direct(computer, groupDn)) return true;
        string filter = "(&(objectCategory=computer)" +
            "(memberOf:1.2.840.113556.1.4.1941:=" +
            NativeAdDiscovery.EscapeFilter(groupDn) + "))";
        var response = (SearchResponse)ldap.SendRequest(new SearchRequest(
            computer.Dn, filter, SearchScope.Base, "distinguishedName"));
        return response.Entries.Count == 1;
    }

    private static object FindComputers(LdapConnection ldap, Scope scope, string search)
    {
        search = search.Trim();
        if (search.Length > 48 || search.Any(char.IsControl))
            throw new ArgumentException("Computer search is limited to 0-48 characters.");
        string filter = "(objectCategory=computer)";
        if (search.Length > 0)
        {
            string term = NativeAdDiscovery.EscapeFilter(search);
            filter = "(&(objectCategory=computer)(|(name=*" + term +
                "*)(sAMAccountName=*" + term + "*)))";
        }
        var request = new SearchRequest(scope.OuDn, filter, SearchScope.Subtree,
            "distinguishedName", "objectGUID", "name",
            "sAMAccountName", "primaryGroupID", "memberOf");
        request.Controls.Add(new PageResultRequestControl(SearchLimit));
        var response = (SearchResponse)ldap.SendRequest(request);
        var items = new List<(string Name, object Row)>();
        foreach (SearchResultEntry entry in response.Entries)
        {
            if (items.Count == SearchLimit) break;
            var pc = ReadComputer(ldap, scope, entry.DistinguishedName, entry);
            bool direct = Direct(pc, scope.GroupDn);
            bool primary = pc.PrimaryGroupRid == scope.GroupRid;
            bool effective = primary || direct || Effective(ldap, pc, scope.GroupDn);
            items.Add((pc.Name, new
            {
                name = pc.Name, sam = pc.Sam, dn = pc.Dn,
                guid = pc.Guid.ToString("D"),
                membership = primary ? "Primary" : direct ? "Direct" :
                    effective ? "Nested" : "None",
                direct, effective
            }));
        }
        // The caller displays a bounded list. Search never writes to AD.
        return new
        {
            domain = scope.Domain, ouDn = scope.OuDn,
            groupDn = scope.GroupDn, groupSam = scope.GroupSam,
            writable = scope.Writable, count = items.Count,
            computers = items.OrderBy(x => x.Name,
                StringComparer.OrdinalIgnoreCase).Select(x => x.Row).ToArray()
        };
    }

    private static MembershipPlan CreatePlan(LdapConnection ldap, Scope scope,
        string computerDn, string action, string previewUtc)
    {
        if (action is not ("Add" or "Remove"))
            throw new ArgumentException("Select Add or Remove.");
        var pc = ReadComputer(ldap, scope, computerDn);
        bool direct = Direct(pc, scope.GroupDn);
        if (pc.PrimaryGroupRid == scope.GroupRid)
            throw new InvalidOperationException("The computer primary group cannot be modified.");
        bool effective = direct || Effective(ldap, pc, scope.GroupDn);
        if (action == "Add" && effective)
            throw new InvalidOperationException("Computer already has direct or nested group membership.");
        if (action == "Remove" && !direct)
            throw new InvalidOperationException("Computer is not a direct group member.");

        string digest = PlanDigest(new[] {
            scope.Domain, scope.Dc.ToLowerInvariant(), scope.OuDn,
            scope.GroupDn, scope.GroupGuid.ToString("D"),
            scope.GroupUsn.ToString(CultureInfo.InvariantCulture),
            pc.Dn, pc.Guid.ToString("D"), pc.Sam,
            pc.PrimaryGroupRid.ToString(CultureInfo.InvariantCulture),
            action, direct.ToString(), effective.ToString(), previewUtc
        });
        return new MembershipPlan(scope.Domain, scope.Dc, scope.OuDn, scope.GroupDn,
            scope.GroupSam, scope.GroupGuid.ToString("D"), pc.Dn,
            pc.Sam, pc.Name, pc.Guid.ToString("D"), pc.PrimaryGroupRid,
            action, direct, effective, scope.Writable, previewUtc, digest,
            "Will " + action + " direct membership in the selected security group.");
    }

    private static object Apply(LdapConnection ldap, Scope scope, JsonElement request)
    {
        if (!scope.Writable)
            throw new InvalidOperationException("Cannot modify AD membership on a read-only or unverified DC.");
        string action = Text(request, "action");
        string computerDn = Text(request, "computerDn");
        string previewUtc = Text(request, "previewUtc");
        if (!PreviewFresh(previewUtc, DateTimeOffset.UtcNow))
            throw new InvalidOperationException("Membership preview expired or has an invalid UTC timestamp.");
        var plan = CreatePlan(ldap, scope, computerDn, action, previewUtc);
        string providedHash = Text(request, "planHash");
        string approveSam = Text(request, "approveSam");
        string approveGroupDn = Text(request, "approveGroupDn");
        if (plan.hash != providedHash || plan.computerSam != approveSam ||
            !scope.GroupDn.Equals(approveGroupDn, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Membership approval or AD objects changed after preview.");

        // Re-check the group and computer on the same explicitly selected DC.
        // Never change group membership when identities or USN are stale.
        var group = ReadOne(ldap, scope.GroupDn, "(objectClass=group)",
            "objectGUID", "uSNChanged");
        if (new Guid(Binary(group, "objectGUID")) != scope.GroupGuid ||
            !long.TryParse(Required(group, "uSNChanged"), NumberStyles.None,
                CultureInfo.InvariantCulture, out long newUsn) ||
            newUsn != scope.GroupUsn)
            throw new InvalidOperationException("Security group changed since the preview.");
        var computer = ReadComputer(ldap, scope, computerDn);
        if (computer.Guid.ToString("D") != plan.computerGuid ||
            computer.Sam != plan.computerSam ||
            !computer.Dn.Equals(plan.computerDn, StringComparison.OrdinalIgnoreCase) ||
            computer.PrimaryGroupRid != plan.primaryGroupRid)
            throw new InvalidOperationException("Computer changed since the preview.");

        // Exactly one LDAP member value is changed. This operation never
        // replaces an entire membership list or touches GPOs/computers.
        var mutation = new DirectoryAttributeModification
        {
            Name = "member",
            Operation = action == "Add"
                ? DirectoryAttributeOperation.Add : DirectoryAttributeOperation.Delete
        };
        mutation.Add(computer.Dn);
        ldap.SendRequest(new ModifyRequest(scope.GroupDn, mutation));

        var after = ReadComputer(ldap, scope, computerDn);
        bool isDirect = Direct(after, scope.GroupDn);
        if (isDirect != (action == "Add"))
            throw new InvalidOperationException(
                "AD write was attempted but direct membership could not be verified. Inspect the selected DC.");
        return new
        {
            computerSam = after.Sam, groupDn = scope.GroupDn,
            action, directMember = isDirect, verified = true, dc = scope.Dc
        };
    }
}
