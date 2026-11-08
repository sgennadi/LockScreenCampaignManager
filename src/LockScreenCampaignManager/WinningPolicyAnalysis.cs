namespace LockScreenCampaignManager;

/// <summary>
/// Read-only static GPO link-priority comparison, NOT verified endpoint RSoP.
/// Policy preferences, scripts, CSEs and local edits may have different effects.
/// </summary>
internal sealed record WinningPolicySource(
    string Gpo, string GpoId, string LinkedAt, int LinkOrder, int ScopeDepth,
    bool Enforced, string Source, string Value, string Category,
    string ClientObservation, bool EligibleByLink);

internal sealed record WinningPolicyRow(
    string Key, string Name, string SuggestedGpo, string SuggestedGpoId,
    string SuggestedValue, string Assessment, string Explanation,
    IReadOnlyList<WinningPolicySource> Sources,
    string EndpointState = "Not checked",
    string EndpointValue = "",
    string EndpointComparison = "Not checked",
    string EndpointKind = "",
    string RsopCseStatus = "Not checked",
    string RsopCseGpoId = "",
    string RsopCseExplanation = "")
{
    internal bool HasConflict => Sources.Where(x => x.EligibleByLink)
        .Select(x => x.Value).Distinct(StringComparer.Ordinal).Skip(1).Any();
}

internal sealed class WinningPolicyAnalysis
{
    internal string Computer { get; private init; } = "";
    internal string Dc { get; private init; } = "";
    internal string ActualComputerContainer { get; private init; } = "";
    internal DateTimeOffset AdObservedUtc { get; private init; }
    internal DateTimeOffset? ClientObservedUtc { get; private init; }
    internal DateTimeOffset? EndpointObservedUtc { get; private init; }
    internal string EndpointStatus { get; private init; } = "Not checked";
    internal string EndpointOs { get; private init; } = "Unknown";
    internal string EndpointNote { get; private init; } = "";
    internal string RsopCseStatus { get; private init; } = "Not checked";
    internal string ImageHashStatus { get; private init; } = "Not checked";
    internal string ImageFileName { get; private init; } = "";
    internal string ImageExpectedSha256 { get; private init; } = "";
    internal string ImageActualSha256 { get; private init; } = "";
    internal string ImageNote { get; private init; } = "";
    internal bool IsVerifiedWinner => false;
    internal int CseReportedWinners => Rows.Count(x =>
        x.RsopCseStatus == "CSE reported winner (cached)");
    internal int EndpointValueMatches => Rows.Count(x =>
        x.EndpointComparison == "Matches static estimate (source unverified)");
    internal int EndpointValueDiffers => Rows.Count(x =>
        x.EndpointComparison == "Differs from static estimate (source unknown)");
    internal IReadOnlyList<WinningPolicyRow> Rows { get; private init; } = [];
    internal IReadOnlyList<string> Limitations { get; private init; } = [];
    internal int ConflictingSettings => Rows.Count(x => x.HasConflict);
    internal int PriorityLeaders => Rows.Count(x => x.SuggestedGpoId.Length > 0);

    private sealed record Item(EffectiveGpoPolicy Policy, EffectiveGpoSetting Setting);

    // The AD scanner only records the actual OU/domain chain; site membership
    // is never inferred. Same-container links obey GPMC Link Order 1 first.
    private static int ComparePolicies(EffectiveGpoPolicy a, EffectiveGpoPolicy b)
    {
        if (string.Equals(a.LinkedAt, b.LinkedAt,
            StringComparison.OrdinalIgnoreCase))
            return a.LinkOrder.CompareTo(b.LinkOrder);
        if (a.Enforced && b.Enforced)
            return a.ScopeDepth.CompareTo(b.ScopeDepth);
        if (a.Enforced != b.Enforced)
            return a.Enforced ? -1 : 1;
        return b.ScopeDepth.CompareTo(a.ScopeDepth);
    }

    private static string NormalKey(string input)
    {
        string result = input.Replace('/', '\\').Trim().TrimStart('\\');
        foreach (string prefix in new[] { "HKEY_LOCAL_MACHINE\\", "HKLM\\" })
        {
            if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return result[prefix.Length..].TrimStart('\\').ToUpperInvariant();
        }
        return result.ToUpperInvariant();
    }

    internal static WinningPolicyAnalysis Build(EffectiveGpoReport report,
        RsopEvidence? rsop = null, EndpointLockScreenObservation? endpoint = null,
        EndpointSourceEvidence? sourceEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (rsop is not null &&
            (!string.Equals(rsop.Computer, report.Computer, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(rsop.Domain, report.Domain, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Imported RSoP belongs to a different AD computer.");

        if (endpoint is not null &&
            (!string.Equals(endpoint.Computer, report.Computer, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(endpoint.Domain, report.Domain, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(endpoint.Dc, report.Dc, StringComparison.OrdinalIgnoreCase) ||
             endpoint.CampaignId != report.CampaignId ||
             !string.Equals(endpoint.ManagedGpoId, report.ManagedGpoId, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(endpoint.ActualComputerContainer,
                 report.ActualComputerContainer, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Remote HKLM observation is scoped to a different AD computer or campaign.");

        if (sourceEvidence is not null &&
            (endpoint is null ||
             !string.Equals(sourceEvidence.Computer, report.Computer,
                 StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(sourceEvidence.Domain, report.Domain,
                 StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(sourceEvidence.Dc, report.Dc,
                 StringComparison.OrdinalIgnoreCase) ||
             sourceEvidence.CampaignId != report.CampaignId ||
             !string.Equals(sourceEvidence.ManagedGpoId, report.ManagedGpoId,
                 StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("RSoP/image evidence belongs to a different scoped endpoint.");
        var incompleteInventory = report.Policies.Any(p =>
            p.Link == "Candidate" &&
            (p.StaticRead != "Read" || p.StartupScript ||
             p.Wmi != "None" || p.Security != "Read and Apply grants observed"));
        var siteCandidates = report.Policies.Any(p => p.Site.Length != 0);
        var warnings = new List<string>
        {
            "Static AD/GPO priority is NOT the client's effective policy value.",
            "GPO startup scripts, including the campaign agent, can change the registry outside Registry.pol.",
            "Security filtering, WMI, GPP item-level targeting, other client extensions and local overrides are not fully evaluated."
        };
        if (siteCandidates || !report.SiteInventoryComplete)
            warnings.Add("The actual computer Site and its applied Site-linked GPOs are not verified.");
        if (incompleteInventory)
            warnings.Add("One or more candidate policies have unreadable or dynamic settings, or uncertain eligibility.");
        if (rsop is null)
            warnings.Add("No computer-scope gpresult XML imported.");
        else
        {
            if (rsop.IsHistorical)
                warnings.Add("The imported gpresult XML is historical (>24 hours).");
            if (rsop.Policies.Any(x => !report.Policies.Any(p =>
                string.Equals(p.Id, x.Id, StringComparison.OrdinalIgnoreCase))))
                warnings.Add("Imported client GPOs are missing from the selected DC's candidate inventory.");
            warnings.Add("Imported XML is unsigned GPO-level snapshot evidence, not a per-setting winning-policy attestation.");
        }

        if (endpoint is not null)
        {
            warnings.Add("Remote 64-bit HKLM is a point-in-time value observation, never the winning GPO source or physical display.");
            if (endpoint.RegistryStatus != "Readable")
                warnings.Add("Remote Registry status: " + endpoint.RegistryStatus + "; unavailable/partial is not evidence of absent settings.");
            if (endpoint.OsStatus != "Windows11")
                warnings.Add("Windows 11 status could not be confirmed; do not treat endpoint comparison as Win11 verification.");
        }
        if (sourceEvidence is not null)
        {
            warnings.Add("Computer Registry Policy CSE winner is cached RSoP, not proof of latest refresh or any script/MDM action.");
            warnings.Add("SMB JPEG hash verifies only a file matching the selected-DC campaign manifest, not physical lock screen display.");
        }
        var rows = new List<WinningPolicyRow>();
        var settings = report.Policies
            .SelectMany(p => p.Settings.Select(s => new Item(p, s)))
            .GroupBy(x => NormalKey(x.Setting.Key) + "\u001f" +
                x.Setting.Name.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in settings)
        {
            var entries = group.ToArray();
            var key = entries[0].Setting.Key;
            var name = entries[0].Setting.Name;
            var sources = entries.Select(x => new WinningPolicySource(
                x.Policy.Name, x.Policy.Id, x.Policy.LinkedAt,
                x.Policy.LinkOrder, x.Policy.ScopeDepth, x.Policy.Enforced,
                x.Setting.Source, x.Setting.Value, x.Policy.Category,
                rsop?.ObservationFor(x.Policy.Id) ?? "Not imported",
                x.Policy.Link == "Candidate" && x.Policy.Site.Length == 0))
                .OrderBy(x => x.Gpo, StringComparer.OrdinalIgnoreCase).ToArray();

            var eligible = entries.Where(x =>
                    x.Policy.Link == "Candidate" && x.Policy.Site.Length == 0)
                .OrderBy(x => x.Policy, Comparer<EffectiveGpoPolicy>.Create(ComparePolicies))
                .ToArray();
            string candidate = "", candidateId = "", candidateValue = "";
            string assessment, explanation;
            bool gppPresent = eligible.Any(x =>
                x.Setting.Source != "Registry.pol");
            bool ambiguous = eligible.Length > 1 &&
                ComparePolicies(eligible[0].Policy, eligible[1].Policy) == 0 &&
                eligible[0].Policy.Id != eligible[1].Policy.Id;
            if (eligible.Length == 0)
            {
                assessment = "No ranked OU/domain candidate";
                explanation = "All identified sources are excluded by link/config or Site applicability is unknown.";
            }
            else if (gppPresent || ambiguous)
            {
                assessment = "Order uncertain";
                explanation = gppPresent
                    ? "Registry Preferences actions and item-level targeting are not equivalent to Registry.pol precedence."
                    : "Two GPO links have indistinguishable precedence; no leader assigned.";
            }
            else
            {
                var leader = eligible[0];
                candidate = leader.Policy.Name;
                candidateId = leader.Policy.Id;
                candidateValue = leader.Setting.Value;
                bool conflictingValues = eligible.Select(x => x.Setting.Value)
                    .Distinct(StringComparer.Ordinal).Skip(1).Any();
                assessment = conflictingValues
                    ? "Static priority leader (unverified)"
                    : "Static setting source (unverified)";
                explanation = "Calculated from OU/domain depth, Enforced and GPMC link order. " +
                    "Not proof the setting was applied or is currently visible. " +
                    (leader.Policy.Category == "Potentially applicable" ?
                        "" : "Leading GPO eligibility itself requires client verification. ");
            }
            if (incompleteInventory || siteCandidates || !report.SiteInventoryComplete)
                explanation += " Candidate inventory/eligibility incomplete.";
            var comparison = CompareObserved(endpoint, key, name, candidateId, candidateValue);
            var cse = sourceEvidence?.Assessment(key, name);
            rows.Add(new WinningPolicyRow(key, name, candidate, candidateId,
                candidateValue, assessment, explanation, sources,
                comparison.State, comparison.Value, comparison.Comparison, comparison.Kind,
                cse?.Status ?? "Not checked", cse?.GpoId ?? "",
                cse?.Explanation ?? ""));
        }
        // A remote MDM/registry value can exist even with no matching static
        // GPO candidate. Show those explicit observations instead of hiding
        // competing MDM/local configuration behind an empty policy inventory.
        if (endpoint is { RegistryStatus: not "Unavailable" })
        {
            foreach (var value in endpoint.Values)
            {
                bool mandatory = (string.Equals(value.Key,
                        EndpointLockScreenObservation.PersonalizationKey,
                        StringComparison.OrdinalIgnoreCase) &&
                    value.Name is "LockScreenImage" or "NoLockScreen" or "NoChangingLockScreen") ||
                    (string.Equals(value.Key,
                        EndpointLockScreenObservation.CspKey,
                        StringComparison.OrdinalIgnoreCase) &&
                    value.Name is "LockScreenImagePath" or "LockScreenImageUrl" or "LockScreenImageStatus");
                if (value.Status != "Present" && !mandatory) continue;
                if (rows.Any(r =>
                    EndpointLockScreenObservation.NormalizeKey(r.Key) ==
                        EndpointLockScreenObservation.NormalizeKey(value.Key) &&
                    string.Equals(r.Name, value.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var observation = CompareObserved(endpoint, value.Key, value.Name, "", "");
                rows.Add(new WinningPolicyRow(value.Key, value.Name, "", "",
                    "", "No static GPO source", "Observed registry value without any identified static GPO source; " +
                    "MDM, local policy, script and other extensions remain possible.",
                    Array.Empty<WinningPolicySource>(), observation.State,
                    observation.Value, observation.Comparison, observation.Kind,
                    sourceEvidence?.Assessment(value.Key, value.Name).Status ?? "Not checked",
                    sourceEvidence?.Assessment(value.Key, value.Name).GpoId ?? "",
                    sourceEvidence?.Assessment(value.Key, value.Name).Explanation ?? ""));
            }
        }
        rows = rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase).ToList();
        return new WinningPolicyAnalysis
        {
            Computer = report.Computer, Dc = report.Dc,
            ActualComputerContainer = report.ActualComputerContainer,
            AdObservedUtc = report.AnalyzedUtc,
            ClientObservedUtc = rsop?.ReadUtc,
            EndpointObservedUtc = endpoint?.ObservedUtc,
            EndpointStatus = endpoint?.RegistryStatus ?? "Not checked",
            EndpointOs = endpoint?.OsStatus ?? "Unknown",
            EndpointNote = endpoint?.Note ?? "",
            RsopCseStatus = sourceEvidence?.RsopStatus ?? "Not checked",
            ImageHashStatus = sourceEvidence?.Image.Status ?? "Not checked",
            ImageFileName = sourceEvidence?.Image.FileName ?? "",
            ImageExpectedSha256 = sourceEvidence?.Image.ExpectedSha256 ?? "",
            ImageActualSha256 = sourceEvidence?.Image.ActualSha256 ?? "",
            ImageNote = sourceEvidence?.Image.Note ?? "",
            Rows = rows, Limitations = warnings
        };
    }

    private sealed record EndpointComparison(
        string State, string Value, string Comparison, string Kind);

    private static EndpointComparison CompareObserved(
        EndpointLockScreenObservation? endpoint,
        string registryKey, string registryName,
        string suggestedGpoId, string suggestedValue)
    {
        if (endpoint is null)
            return new("Not checked", "", "Not checked", "");
        if (endpoint.RegistryStatus == "Unavailable")
            return new("Unavailable", "", "Remote Registry unavailable", "");
        var observed = endpoint.Find(registryKey, registryName);
        if (observed is null)
            return new("Not inspected", "", "Not covered by remote snapshot", "");
        if (endpoint.OsStatus != "Windows11")
            return new(observed.Status, observed.Value,
                "Windows 11 not confirmed - no comparison", observed.Kind);
        if (observed.Status != "Present")
            return new(observed.Status, "", observed.Status switch
            {
                "Absent" => "Registry value absent (point-in-time)",
                _ => "Value could not be read reliably"
            }, observed.Kind);
        if (suggestedGpoId.Length == 0)
            return new("Present", observed.Value,
                "Observed without a ranked static GPO source", observed.Kind);
        // Windows registry strings/paths are compared without changing their
        // stored values. Equality is only a corroborating value match; it
        // NEVER authenticates a source GPO or a displayed image.
        bool same = string.Equals(observed.Value.Trim(),
            suggestedValue.Trim(), StringComparison.OrdinalIgnoreCase);
        return new("Present", observed.Value,
            same ? "Matches static estimate (source unverified)" :
                "Differs from static estimate (source unknown)", observed.Kind);
    }

}
