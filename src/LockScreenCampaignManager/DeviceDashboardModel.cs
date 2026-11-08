using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// A per-computer read-only projection of the verified AD OU/group intersection,
/// untrusted Client Health hints, protected repair tracking and local history.
/// The projection never grants an identity permission to execute remote RPC.
/// </summary>
internal sealed record DeviceHistoryPoint(DateTimeOffset WhenUtc, string Condition,
    string Stage, string ExpectedImage, string AgentVersion);

/// <summary>Most recent durable queue metadata, NOT a full RPC attempt log.</summary>
internal sealed record DeviceRepairEvidence(
    string Source, string State, int Attempts,
    DateTimeOffset ApprovedUtc, DateTimeOffset NextAttemptUtc,
    DateTimeOffset? LastAttemptUtc, DateTimeOffset? SubmittedUtc,
    string Reason, bool IsInFlight);

internal sealed record DeviceDashboardRow(
    string ComputerFqdn,
    string ShortName,
    string ClientHealth,
    string Stage,
    string ExpectedImage,
    string LastSeen,
    string Integrity,
    string AgentVersion,
    string RepairState,
    string RepairExplanation,
    bool NeedsAttention,
    bool HasPendingRepair,
    IReadOnlyList<DeviceHistoryPoint> History,
    IReadOnlyList<DeviceRepairEvidence> RepairEvidence);

internal sealed class DeviceDashboardSnapshot
{
    public required string CampaignId { get; init; }
    public required string GpoId { get; init; }
    public required string FeedbackShare { get; init; }
    public required DateTimeOffset ObservedUtc { get; init; }
    public required ClientHealthSnapshot ClientSnapshot { get; init; }
    public required IReadOnlyList<DeviceDashboardRow> Rows { get; init; }
    public IReadOnlyList<RepairHistoryEvent> RepairTimeline { get; init; } = [];
    public int Problems => Rows.Count(x => x.NeedsAttention);
    public int ActiveRepair => Rows.Count(x => x.HasPendingRepair);
    public int NoReport => Rows.Count(x => x.ClientHealth == "No report");
    public int HealthClaims => Rows.Count(x => x.ClientHealth is "Healthy" or "Completed");
}

internal static class DeviceDashboardModel
{
    private static readonly Regex Fqdn = new(
        @"\A[A-Za-z0-9][A-Za-z0-9.-]{1,252}\z", RegexOptions.CultureInvariant);

    internal static IReadOnlyList<string> ValidateAndNormalizeAdNames(
        IEnumerable<string> eligibleAdNames)
    {
        ArgumentNullException.ThrowIfNull(eligibleAdNames);
        var names = eligibleAdNames.ToArray();
        if (names.Length > 5000)
            throw new InvalidDataException("Dashboard scope exceeds the 5000-computer Client Health limit.");
        if (names.Any(x => string.IsNullOrWhiteSpace(x) || !Fqdn.IsMatch(x) ||
                x.Contains("..", StringComparison.Ordinal) ||
                !FeedbackPath.IsValidHost(x.Split('.')[0])))
            throw new InvalidDataException("AD returned an invalid device identity.");
        // Client reports are keyed by short machine name. If two scoped AD
        // objects share a short name, never silently pick a device for Repair.
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (names.Any(x => !unique.Add(x.Split('.')[0])))
            throw new InvalidDataException("Ambiguous computer short name in selected OU and group.");
        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static DeviceDashboardSnapshot Build(string campaignId, string gpoId,
        string share, IEnumerable<string> eligibleAdNames, ClientHealthSnapshot client,
        IEnumerable<DeferredRepairItem> perUserRepair,
        IEnumerable<DeferredRepairItem> gmsaRepair,
        IEnumerable<CampaignHistoryEntry> localHistory,
        string domain, string ouDn, string groupDn,
        IEnumerable<RepairHistoryEvent>? repairTimeline = null)
    {
        if (!Guid.TryParse(gpoId, out Guid selectedGpo) ||
            campaignId != client.CampaignId)
            throw new InvalidDataException("Selected GPO, campaign and feedback snapshot do not match.");
        string root = FeedbackPath.ValidateRoot(share);
        var eligible = ValidateAndNormalizeAdNames(eligibleAdNames);
        var rowsByName = client.Rows.ToDictionary(x => x.Computer, StringComparer.OrdinalIgnoreCase);
        var authorizedHosts = new HashSet<string>(eligible, StringComparer.OrdinalIgnoreCase);
        var verifiedTimeline = (repairTimeline ?? Array.Empty<RepairHistoryEvent>())
            .Where(x => x is not null && x.CampaignId == campaignId &&
                x.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase) &&
                x.OuDn.Equals(ouDn, StringComparison.OrdinalIgnoreCase) &&
                x.GroupDn.Equals(groupDn, StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(x.GpoId, out var historyGpo) && historyGpo == selectedGpo &&
                authorizedHosts.Contains(x.ComputerFqdn))
            .OrderByDescending(x => x.RecordedUtc).Take(500).ToArray();
        var repairs = perUserRepair.Select(x => (Entry: x, Source: "User"))
            .Concat(gmsaRepair.Select(x => (Entry: x, Source: "gMSA")))
            .Where(x => x.Entry.CampaignId == campaignId &&
                Guid.TryParse(x.Entry.GpoId, out Guid gpo) && gpo == selectedGpo)
            .ToArray();
        var history = localHistory.Where(x => x.CampaignId == campaignId &&
            x.Kind is "Snapshot" or "Final" &&
            string.Equals(x.Domain, domain, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.OuDn, ouDn, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.GroupDn, groupDn, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.OccurredUtc)
            .Take(100)
            .ToArray();

        var result = new List<DeviceDashboardRow>(eligible.Count);
        foreach (var fqdn in eligible)
        {
            string shortName = fqdn.Split('.')[0];
            if (!rowsByName.TryGetValue(shortName, out var health))
                health = new ClientHealthRow(shortName, "No report", "Unknown",
                    "—", "—", "—", "—", "—", "—");
            var pending = repairs.Where(x =>
                string.Equals(x.Entry.ComputerFqdn, fqdn, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            string repairState = pending.Length switch
            {
                0 => "None",
                1 => pending[0].Source + ": " + pending[0].Entry.State,
                _ => "Multiple Repair records"
            };
            string repairExplanation = pending.Length switch
            {
                0 => "No currently tracked Repair for this campaign.",
                1 => pending[0].Entry.Reason,
                _ => "More than one queue tracks this host. Investigate before issuing another RPC."
            };
            bool unresolved = pending.Any(x =>
                x.Entry.State is "Queued" or "Attempting" or "AwaitingTask" or "AwaitingHealth");
            bool attention = health.Condition is "Needs attention" or "Error" or "Stale / offline" ||
                pending.Length > 1 ||
                pending.Any(x => x.Entry.State == "NeedsAttention");
            // Missing telemetry is unknown rather than a confirmed device failure.
            string integrity = health.Files == "OK" && health.Registry == "OK" &&
                health.Task == "Yes" ? "Reported OK" :
                health.Condition == "No report" ? "Unknown" : "Needs verification";
            var evidence = pending.OrderByDescending(x => x.Entry.ApprovedUtc)
                .Select(x => new DeviceRepairEvidence(
                    x.Source, x.Entry.State, x.Entry.Attempts,
                    x.Entry.ApprovedUtc, x.Entry.NextAttemptUtc,
                    x.Entry.LastAttemptUtc, x.Entry.SubmittedUtc,
                    x.Entry.Reason,
                    x.Entry.State is "Attempting" or "AwaitingHealth" or "AwaitingTask"))
                .ToArray();
            var points = history.SelectMany(entry => entry.Rows
                    .Where(x => string.Equals(x.Computer, shortName, StringComparison.OrdinalIgnoreCase))
                    .Select(x => new DeviceHistoryPoint(entry.OccurredUtc,
                        x.Condition, x.Stage, x.Image, x.Version)))
                .OrderByDescending(x => x.WhenUtc).Take(8).ToArray();
            result.Add(new DeviceDashboardRow(fqdn, shortName,
                health.Condition, health.Stage, health.Image, health.LastSeen,
                integrity, health.Version, repairState, repairExplanation,
                attention, unresolved, points, evidence));
        }
        return new DeviceDashboardSnapshot {
            CampaignId = campaignId, GpoId = selectedGpo.ToString("D"),
            FeedbackShare = root, ClientSnapshot = client,
            ObservedUtc = client.ObservedUtc, Rows = result,
            RepairTimeline = verifiedTimeline
        };
    }
}
