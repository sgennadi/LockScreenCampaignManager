namespace LockScreenCampaignManager;

/// <summary>
/// Pure, read-only audit of machine-wide gMSA approvals. An audit never reads
/// SMB reports, changes the protected JSON queue, schedules tasks or calls RPC.
/// Severity describes operator attention, not actual endpoint health.
/// </summary>
internal enum GmsaQueuePriority { Normal, Monitor, Attention }

internal sealed record GmsaQueueAuditRow(
    string Id,
    string ComputerFqdn,
    string CampaignId,
    string State,
    int Attempts,
    DateTimeOffset ApprovedUtc,
    DateTimeOffset NextAttemptUtc,
    string Reason,
    string Health,
    string Guidance,
    GmsaQueuePriority Priority);

internal static class GmsaQueueAudit
{
    // A scheduled worker has a 12-minute execution timeout. Do not label an
    // active attempt "stalled" before that window and a little clock slack.
    internal static readonly TimeSpan AttemptWarning = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan FeedbackWarning = TimeSpan.FromHours(2);
    internal static readonly TimeSpan RetryDelayWarning = TimeSpan.FromMinutes(10);

    internal static IReadOnlyList<GmsaQueueAuditRow> Analyze(
        IReadOnlyCollection<DeferredRepairItem> entries,
        DateTimeOffset now, bool workerReady)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Select(x => AnalyzeOne(x, now, workerReady))
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.NextAttemptUtc)
            .ThenBy(x => x.ComputerFqdn, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static GmsaQueueAuditRow AnalyzeOne(
        DeferredRepairItem item, DateTimeOffset now, bool workerReady)
    {
        string health;
        string guidance;
        GmsaQueuePriority priority;
        switch (item.State)
        {
            case "NeedsAttention":
                health = "Review required";
                guidance = "An administrator must investigate before a new Repair.";
                priority = GmsaQueuePriority.Attention;
                break;
            case "Queued":
                if (item.ApprovedUtc <= now.AddDays(-14))
                {
                    health = "Approval expired";
                    guidance = "The original 14-day approval ended; worker will not retry.";
                    priority = GmsaQueuePriority.Attention;
                }
                else if (item.Attempts >= 20)
                {
                    health = "Retry limit reached";
                    guidance = "20 attempts used; automatic RPC is prohibited.";
                    priority = GmsaQueuePriority.Attention;
                }
                else if (!workerReady)
                {
                    health = "Paused";
                    guidance = "Task is not verified or gMSA is disabled; no RPC permitted.";
                    priority = GmsaQueuePriority.Monitor;
                }
                else if (now - item.NextAttemptUtc > RetryDelayWarning)
                {
                    health = "Retry delayed";
                    guidance = "Scheduled retry is overdue; check the worker heartbeat and SMB feedback.";
                    priority = GmsaQueuePriority.Monitor;
                }
                else
                {
                    health = item.NextAttemptUtc <= now ? "Due for retry" : "Scheduled retry";
                    guidance = "Worker may retry only within the existing approval and attempt limit.";
                    priority = GmsaQueuePriority.Normal;
                }
                break;
            case "Attempting":
                // The remote task may already have been registered. Never
                // interpret this as permission to run RPC a second time.
                if (!item.SubmittedUtc.HasValue)
                {
                    health = "RPC timestamp missing";
                    guidance = "Unconfirmed remote outcome; administrator review, no automatic RPC.";
                    priority = GmsaQueuePriority.Attention;
                }
                else if (!workerReady || now - item.SubmittedUtc.Value > AttemptWarning)
                {
                    health = "RPC unconfirmed";
                    guidance = "Worker may have stopped; only the next worker cycle may recover to AwaitingTask.";
                    priority = GmsaQueuePriority.Attention;
                }
                else
                {
                    health = "RPC in progress";
                    guidance = "Registration is in progress or unresolved; never submit another RPC.";
                    priority = GmsaQueuePriority.Monitor;
                }
                break;
            case "AwaitingTask":
            case "AwaitingHealth":
                if (!item.SubmittedUtc.HasValue)
                {
                    health = "Submission time missing";
                    guidance = "Cannot establish remote outcome; manual review, no RPC retry.";
                    priority = GmsaQueuePriority.Attention;
                }
                else if (now - item.SubmittedUtc.Value > FeedbackWarning)
                {
                    health = "Health confirmation overdue";
                    guidance = "Two-hour feedback window elapsed; manual review, no RPC repeat.";
                    priority = GmsaQueuePriority.Attention;
                }
                else
                {
                    health = item.State == "AwaitingTask" ?
                        "RPC result unknown" : "Waiting for Client Health";
                    guidance = item.State == "AwaitingTask" ?
                        "Remote task may exist; health polling only, never automatically repeat RPC." :
                        "Remote task reported success; wait for fresh healthy SMB feedback.";
                    priority = GmsaQueuePriority.Monitor;
                }
                break;
            default:
                // Malformed data should normally fail queue validation before
                // reaching the audit, but an unknown state must never look healthy.
                health = "Unrecognized queue state";
                guidance = "Read-only inspection only; investigate protected queue validation.";
                priority = GmsaQueuePriority.Attention;
                break;
        }

        return new GmsaQueueAuditRow(
            item.Id, item.ComputerFqdn, item.CampaignId, item.State, item.Attempts,
            item.ApprovedUtc, item.NextAttemptUtc, item.Reason,
            health, guidance, priority);
    }

    internal static string FormatCounts(IReadOnlyCollection<DeferredRepairItem> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        int Count(string state) => entries.Count(x => x.State == state);
        return $"Entries: {entries.Count} | " +
            $"Queued: {Count("Queued")} | " +
            $"Awaiting health: {Count("AwaitingHealth")} | " +
            $"Awaiting task: {Count("AwaitingTask")} | " +
            $"Attempting: {Count("Attempting")} | " +
            $"Needs attention: {Count("NeedsAttention")}";
    }

    internal static string FormatAttention(IReadOnlyCollection<GmsaQueueAuditRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return "Operator review: " +
            rows.Count(x => x.Priority == GmsaQueuePriority.Attention) +
            " | Monitoring: " +
            rows.Count(x => x.Priority == GmsaQueuePriority.Monitor) +
            " | Normal: " +
            rows.Count(x => x.Priority == GmsaQueuePriority.Normal);
    }
}
