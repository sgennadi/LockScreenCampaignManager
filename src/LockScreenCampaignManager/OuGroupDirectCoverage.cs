using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Verified, capped read-only LDAP coverage of an OU subtree. Counts are for
/// DIRECT memberOf edges only, not effective security tokens or GPO RSoP.
/// Never serialize principal names or infer that zero is a policy denial.
/// </summary>
internal sealed class OuGroupDirectCoverage
{
    private readonly Dictionary<string, int> _counts;

    private OuGroupDirectCoverage(string ouDn, string kind, int principals,
        Dictionary<string, int> counts, OuComputerActivityCoverage? activity)
    {
        OuDn = ouDn;
        PrincipalKind = kind;
        PrincipalCount = principals;
        _counts = counts;
        Activity = activity;
    }

    internal string OuDn { get; }
    internal string PrincipalKind { get; }
    internal int PrincipalCount { get; }
    internal OuComputerActivityCoverage? Activity { get; }
    internal IReadOnlyCollection<string> DirectGroupDns => _counts.Keys;

    internal int DirectMatchesFor(string? groupDn) =>
        !string.IsNullOrWhiteSpace(groupDn) && _counts.TryGetValue(groupDn, out int count)
            ? count : 0;

    internal static OuGroupDirectCoverage Parse(JsonElement data,
        string expectedOuDn, string expectedKind)
    {
        if (expectedKind is not ("Computer" or "User") ||
            string.IsNullOrWhiteSpace(expectedOuDn) ||
            !string.Equals(data.GetProperty("ouDn").GetString(), expectedOuDn,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(data.GetProperty("principalKind").GetString(),
                expectedKind, StringComparison.Ordinal) ||
            data.GetProperty("readOnly").ValueKind != JsonValueKind.True ||
            data.GetProperty("directMembershipOnly").ValueKind != JsonValueKind.True ||
            data.GetProperty("nestedGroupsExpanded").ValueKind != JsonValueKind.False ||
            data.GetProperty("primaryGroupsIncluded").ValueKind != JsonValueKind.False)
            throw new InvalidDataException("The OU direct-membership scope is not verified.");

        int principals = data.GetProperty("principalCount").GetInt32();
        int namedTotal = data.GetProperty(expectedKind == "User"
            ? "userCount" : "computerCount").GetInt32();
        if (principals < 0 || principals >= 5000 || principals != namedTotal)
            throw new InvalidDataException("Incomplete or inconsistent OU membership totals.");

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement row in data.GetProperty("directGroupCounts").EnumerateArray())
        {
            string? dn = row.GetProperty("dn").GetString();
            int matched = row.GetProperty("directMemberCount").GetInt32();
            if (string.IsNullOrWhiteSpace(dn) || dn.Length > 2048 ||
                !dn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ||
                dn.Any(char.IsControl) || matched < 1 || matched > principals ||
                !counts.TryAdd(dn, matched))
                throw new InvalidDataException("Untrusted or repeated group count in LDAP response.");
        }

        var returnedDns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in data.GetProperty("directGroupDns").EnumerateArray())
        {
            string? dn = item.GetString();
            if (string.IsNullOrWhiteSpace(dn) || !returnedDns.Add(dn))
                throw new InvalidDataException("LDAP direct group list contains duplicate entries.");
        }
        if (!returnedDns.SetEquals(counts.Keys))
            throw new InvalidDataException("LDAP group counts and membership list do not agree.");

        // Optional AD logon activity is never inferred for User scope. A
        // missing/invalid DC clock remains "not verified"; it is not zero.
        OuComputerActivityCoverage? activity = null;
        if (data.TryGetProperty("activity", out JsonElement observed) &&
            observed.ValueKind != JsonValueKind.Null)
        {
            if (expectedKind != "Computer")
                throw new InvalidDataException("User OU must not return computer activity.");
            activity = OuComputerActivityCoverage.Parse(observed, principals, counts);
        }

        return new OuGroupDirectCoverage(expectedOuDn, expectedKind, principals, counts, activity);
    }
}
