namespace LockScreenCampaignManager;

/// <summary>
/// Read-only, conservative campaign closing assessment over ONE verified
/// AD OU + computer-security-group fleet snapshot. Client feedback is
/// self-reported, never independent proof that local GPO tasks are gone.
/// </summary>
internal sealed record ClosureAuditRow(
    string ComputerFqdn,
    string Category,
    string AgentState,
    string RepairState,
    string LastReport,
    string Explanation,
    string NextAction)
{
    internal bool RequiresAttention => Category != "Reported completion";
}

internal sealed class CampaignClosureAudit
{
    public required string CampaignId { get; init; }
    public required string GpoId { get; init; }
    public required DateTimeOffset ObservedUtc { get; init; }
    public required IReadOnlyList<ClosureAuditRow> Rows { get; init; }
    public int Total => Rows.Count;
    public int ReportedComplete => Rows.Count(x => x.Category == "Reported completion");
    public int NoReport => Rows.Count(x => x.Category == "No report");
    public int Outstanding => Rows.Count(x => x.Category == "Active or pending");
    public int NeedsReview => Rows.Count(x => x.Category == "Needs review");
    public int RepairTracked => Rows.Count(x => x.Category == "Repair unresolved");
    public int Unexpected => Rows.Count(x => x.Category == "Unclassified");
    public int WithoutCompletionReport => Total - ReportedComplete;

    // An agent-generated "Completed" report currently precedes deletion of
    // local files/tasks; it is not a post-cleanup attestation. A zero count
    // of outstanding endpoints must NEVER be equated to GPO or local cleanup.
    public bool FleetShutdownIndependentlyVerified => false;

    internal static CampaignClosureAudit Assess(DeviceDashboardSnapshot snapshot,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Rows.Count is < 1 or > 5000 ||
            snapshot.ClientSnapshot.Expected != snapshot.Rows.Count ||
            snapshot.ClientSnapshot.CampaignId != snapshot.CampaignId ||
            !Guid.TryParse(snapshot.GpoId, out _) ||
            snapshot.ObservedUtc > nowUtc.AddMinutes(5) ||
            snapshot.ObservedUtc < nowUtc.AddMinutes(-10))
            throw new InvalidDataException(
                "Load verified AD computers and refresh the selected campaign before auditing closure.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ClosureAuditRow>(snapshot.Rows.Count);
        foreach (var row in snapshot.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.ComputerFqdn) ||
                !names.Add(row.ComputerFqdn) ||
                !FeedbackPath.IsValidHost(row.ShortName))
                throw new InvalidDataException("Invalid or duplicate AD computer in closure scope.");
            string category;
            string explanation;
            string next;
            // A pending or NeedsAttention Repair is important even when a
            // client has sent a 'Completed' heartbeat from the same campaign.
            if (row.RepairEvidence.Count != 0 ||
                row.HasPendingRepair || row.RepairState != "None")
            {
                category = "Repair unresolved";
                explanation = "User/gMSA queue still tracks this computer. " +
                    "A task may have been submitted, and removal of a queue item " +
                    "does not prove endpoint cleanup.";
                next = "Inspect Repair history and remote task state; do not retry RPC blindly.";
            }
            else if (row.ClientHealth == "No report")
            {
                category = "No report";
                explanation = "No valid SMB report for this AD computer. " +
                    "Offline is unknown, not a failed or completed machine.";
                next = "Check connectivity, startup processing and feedback share.";
            }
            else if (row.ClientHealth == "Completed" && row.Stage == "Completed")
            {
                category = "Reported completion";
                explanation = "The agent reported Completed. In current agents " +
                    "that message is written before final local task/file deletion; " +
                    "it does NOT independently verify shutdown or GPO removal.";
                next = "Verify actual local task, files and registry restoration before final source cleanup.";
            }
            else if (row.NeedsAttention || row.ClientHealth is
                     "Needs attention" or "Error" or "Stale / offline")
            {
                category = "Needs review";
                explanation = "The last agent health report is incomplete, stale " +
                    "or indicates an error. Treat this endpoint as unconfirmed.";
                next = "Inspect Client Health details and conflicting lockscreen policies.";
            }
            else if (row.Stage is "Pending" or "Active" ||
                     row.ClientHealth is "Healthy" or "Ready / pending")
            {
                category = "Active or pending";
                explanation = "The agent has not reported completion; local " +
                    "scheduled tasks may continue even after GPO Disable/Unlink.";
                next = "Wait for scheduled end or explicitly Preview and approve Cleanup.";
            }
            else
            {
                category = "Unclassified";
                explanation = "The selected device has an unexpected combination " +
                    "of Client Health and agent stage. Do not infer completion.";
                next = "Refresh evidence and inspect this computer manually.";
            }
            rows.Add(new ClosureAuditRow(
                row.ComputerFqdn, category, row.Stage,
                row.RepairState, row.LastSeen, explanation, next));
        }

        return new CampaignClosureAudit
        {
            CampaignId = snapshot.CampaignId,
            GpoId = snapshot.GpoId,
            ObservedUtc = snapshot.ObservedUtc,
            Rows = rows.OrderBy(x => x.Category, StringComparer.Ordinal)
                .ThenBy(x => x.ComputerFqdn, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }
}
