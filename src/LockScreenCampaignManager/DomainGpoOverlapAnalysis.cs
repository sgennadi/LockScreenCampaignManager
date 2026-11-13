namespace LockScreenCampaignManager;

/// <summary>
/// Static same-setting inventory comparisons inspired by the identity/scope
/// grouping in GPOSettingsExplorer. This is not a winning GPO engine: even two
/// linked policies may apply to distinct OUs/users/devices after filtering.
/// </summary>
internal sealed record DomainGpoOverlap(
    string Scope, string Key, string Setting, string Kind,
    int GpoCount, int LinkedCandidates, string Gpos,
    string ConfiguredValues, string Applicability);

internal static class DomainGpoOverlapAnalysis
{
    internal static IReadOnlyList<DomainGpoOverlap> Analyze(
        IReadOnlyList<DomainGpoMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        if (matches.Count > 6000)
            throw new InvalidDataException("Unbounded domain GPO overlap input.");

        var findings = new List<DomainGpoOverlap>();
        foreach (var group in matches
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) &&
                        !string.IsNullOrWhiteSpace(x.Setting))
            .GroupBy(x => (Scope: x.Scope.ToUpperInvariant(),
                           Key: x.Key.Trim().TrimStart('\\').ToUpperInvariant(),
                           Name: x.Setting.Trim().ToUpperInvariant())))
        {
            var policies = group.GroupBy(x => x.GpoId,
                StringComparer.OrdinalIgnoreCase).ToArray();
            if (policies.Length < 2) continue;

            bool ambiguous = policies.Any(g => g.Select(x => x.Value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any());
            int variants = group.Select(x => x.Value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            string kind = ambiguous ? "Ambiguous source values" :
                variants == 1 ? "Same configured value" :
                "Different configured values";
            int active = policies.Count(g => g.Any(
                x => x.LinkStatus == "Linked candidate"));
            string applicability = active >= 2
                ? "Multiple GPOs are linked somewhere in selected OU ancestry. " +
                  "Their target users/devices may differ; security/WMI, loopback, " +
                  "site and actual winning RSoP are NOT verified."
                : "No two independently linked GPO candidates in selected OU " +
                  "scope. Inventory collision only; not an applied conflict.";

            // Keep the report scrollable/CSV-safe even when a domain has
            // hundreds of GPOs setting the same value.
            string gpos = string.Join("; ", policies.Take(16).Select(g =>
                g.First().Gpo + " {" + g.Key + "}"));
            if (policies.Length > 16) gpos += "; ... additional GPOs";
            string values = string.Join("; ", group.Select(x =>
                x.Gpo + "=" + x.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(24));
            if (values.Length > 2048) values = values[..2048] + " [truncated]";

            findings.Add(new DomainGpoOverlap(
                group.Key.Scope == "USER" ? "User" : "Computer",
                group.First().Key, group.First().Setting,
                kind, policies.Length, active, gpos, values, applicability));
        }
        return findings
            .OrderBy(x => x.Kind == "Different configured values" ? 0 :
                x.Kind == "Ambiguous source values" ? 1 : 2)
            .ThenByDescending(x => x.LinkedCandidates)
            .ThenBy(x => x.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Setting, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
