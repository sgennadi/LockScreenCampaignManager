using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only, operator-initiated readiness assessment. This is not a claim
/// of endpoint visual attestation or an automated domain acceptance test.
/// No AD, GPO, Task Scheduler, SMB or repair queue writes are performed.
/// </summary>
internal sealed record ReadinessCheck(string Name, string Result, string Detail);
internal sealed record ReadinessEvidence(
    bool ConfigurationPresent,
    bool ScopeMatches,
    bool WorkerEnabled,
    bool SchedulerVerified,
    bool AccountVerified,
    bool HeartbeatFresh,
    bool DiagnosticFresh,
    bool DiagnosticPassed,
    bool FeedbackReadableByAdmin,
    bool QueueReadable,
    int TotalQueued,
    int UnconfirmedRpc,
    int RequiresAttention);

internal sealed class ReadinessReport
{
    public string Schema { get; init; } = "lscm-readiness/v1";
    public string ManagerVersion { get; init; } = AppVersion.Current;
    public DateTimeOffset CheckedUtc { get; init; }
    public string ScopeFingerprint { get; init; } = "";
    public string Conclusion { get; init; } = "";
    public List<ReadinessCheck> Checks { get; init; } = new();
    public int QueueEntries { get; init; }
    public int UnconfirmedRpc { get; init; }
    public int NeedsAttention { get; init; }
    public string EndpointAcceptance { get; init; } =
        "Not tested: verify actual Lock Screen on test PCs and reboot/Client Health.";
    internal string ToJson() =>
        JsonSerializer.Serialize(this, new JsonSerializerOptions {
            WriteIndented = true
        });

    internal string ToDisplay()
    {
        var text = new StringBuilder();
        text.AppendLine("LockScreen Campaign Manager — Production Readiness");
        text.AppendLine("Checked (local time): " +
            CheckedUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        text.AppendLine("Manager: " + ManagerVersion);
        text.AppendLine("Conclusion: " + Conclusion);
        text.AppendLine("Scope fingerprint: " +
            (ScopeFingerprint.Length == 64 ? ScopeFingerprint[..16] + "..." : "not verified"));
        text.AppendLine("Queue: " + QueueEntries + "; uncertain RPC: " +
            UnconfirmedRpc + "; attention: " + NeedsAttention);
        text.AppendLine();
        foreach (var check in Checks)
            text.AppendLine("[" + check.Result.ToUpperInvariant() +
                "] " + check.Name + " — " + check.Detail);
        text.AppendLine();
        text.AppendLine("Endpoint acceptance: " + EndpointAcceptance);
        text.AppendLine("No changes to GPO, remote PCs, passwords, queues or Task Scheduler.");
        return text.ToString();
    }
}

internal static class GmsaProductionReadiness
{
    // Stable, deliberately unambiguous outcomes. A passing local assessment
    // is ONLY "Ready for pilot", never "production verified".
    internal static ReadinessReport Evaluate(ReadinessEvidence e,
        DateTimeOffset now, string fingerprint)
    {
        var checks = new List<ReadinessCheck>();
        void Add(string name, bool passed, string ok, string failure, bool warning = false) =>
            checks.Add(new ReadinessCheck(name,
                passed ? "Pass" : warning ? "Warning" : "Blocked",
                passed ? ok : failure));

        Add("Protected installation", e.ConfigurationPresent,
            "Protected configuration was read.", "Configuration missing or invalid.");
        Add("AD scope identity", e.ScopeMatches,
            "Selected domain, OU and security group match the installation.",
            "Installed scope differs from the selected management scope.");
        Add("Worker enabled", e.WorkerEnabled,
            "Worker is enabled.", "Worker is disabled.");
        Add("Task Scheduler", e.SchedulerVerified,
            "Native gMSA task identity and boot/interval triggers verified.",
            "Native task missing, disabled or incorrectly registered.");
        Add("Local gMSA account", e.AccountVerified,
            "gMSA account is installed and SID matched by native Netlogon.",
            "Check local gMSA installation and AD password retrieval permissions.");
        Add("Worker heartbeat", e.HeartbeatFresh,
            "Recent scoped worker heartbeat received.",
            "Heartbeat absent, outdated or from a previous installation.");
        Add("Read-only gMSA test", e.DiagnosticFresh && e.DiagnosticPassed,
            "Fresh read-only diagnostic completed under the gMSA identity.",
            "Run gMSA test to verify identity, SYSVOL and SMB access.");
        Add("Feedback SMB", e.FeedbackReadableByAdmin,
            "Administrator can enumerate the configured Client Health share.",
            "Feedback share cannot be enumerated from this host.");
        Add("Protected Repair queue", e.QueueReadable,
            "Protected machine-wide queue read and validated.",
            "Queue could not be verified safely.");
        Add("Unconfirmed RPC outcomes", e.UnconfirmedRpc == 0,
            "No unresolved Attempting or AwaitingTask entries.",
            "Remote tasks may already exist; inspect clients without repeating RPC.",
            warning: true);
        Add("Manual review", e.RequiresAttention == 0,
            "No current queue entries require administrator review.",
            "Some queue entries require administrator investigation.", warning: true);

        string conclusion = checks.Any(x => x.Result == "Blocked")
            ? "NOT READY — fix blocking checks before pilot"
            : checks.Any(x => x.Result == "Warning")
                ? "PILOT NEEDS REVIEW — investigate queue warnings"
                : "READY FOR PILOT — live endpoint acceptance still required";
        return new ReadinessReport {
            CheckedUtc = now,
            ScopeFingerprint = fingerprint.Length == 64 &&
                fingerprint.All(Uri.IsHexDigit) ? fingerprint : "",
            Conclusion = conclusion, Checks = checks,
            QueueEntries = e.TotalQueued,
            UnconfirmedRpc = e.UnconfirmedRpc,
            NeedsAttention = e.RequiresAttention
        };
    }

    /// <summary>
    /// Executes on Task.Run from an elevated GUI. Fail closed on invalid
    /// protected configuration/queue; never treat unreadable data as empty.
    /// This is not the credential-bearing scheduled gMSA diagnostic itself.
    /// </summary>
    internal static ReadinessReport Inspect(string domain, string ou, string group)
    {
        if (!GmsaRepairQueue.IsAdministrator() &&
            GmsaRepairConfig.TestRootOverride is null)
            throw new UnauthorizedAccessException("Local administrator elevation is required.");
        var config = GmsaRepairConfig.TryReadForInspection();
        if (config is null)
            return Evaluate(new ReadinessEvidence(false, false, false, false,
                false, false, false, false, false, false, 0, 0, 0),
                DateTimeOffset.UtcNow, "");
        bool scope = config.ScopeMatchesForInspection(domain, ou, group);
        string fingerprint = GmsaDiagnosticCheck.Fingerprint(config);
        if (!scope)
            return Evaluate(new ReadinessEvidence(true, false, config.Enabled,
                false, false, false, false, false, false, false, 0, 0, 0),
                DateTimeOffset.UtcNow, fingerprint);
        bool scheduler = config.Enabled &&
            GmsaRepairConfig.IsScheduledTaskReady(config);

        bool account = false;
        try
        {
            var check = GmsaNativeSetup.Check(new GmsaNativeSetup.Request(
                config.GmsaAccount, config.Domain, config.OuDn,
                config.GroupDn, config.FeedbackShare));
            account = check.Installed &&
                string.Equals(check.Sid, config.GmsaSid, StringComparison.Ordinal);
        }
        catch (Exception ex) { AppLog.Write(ex, "Readiness native gMSA account"); }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var status = GmsaRepairStatus.LoadForScope(config);
        bool freshHeartbeat = status.BelongsTo(config) &&
            status.LastRunUtc >= config.InstalledUtc &&
            status.LastRunUtc <= now.AddMinutes(5) &&
            now - status.LastRunUtc <= TimeSpan.FromMinutes(8) &&
            GmsaRepairStatus.Classify(status, now, scheduler)
                .StartsWith("Heartbeat received", StringComparison.Ordinal);

        var diagnostic = GmsaDiagnosticReport.Load();
        bool diagnosticFresh = GmsaDiagnosticCheck.BelongsTo(diagnostic, config) &&
            diagnostic!.CompletedUtc >= config.InstalledUtc &&
            diagnostic.CompletedUtc <= now.AddMinutes(5) &&
            now - diagnostic.CompletedUtc <= TimeSpan.FromHours(1);
        bool diagnosticPassed = diagnosticFresh && diagnostic!.Passed;
        bool feedback = false;
        try
        {
            string share = FeedbackPath.ValidateRoot(config.FeedbackShare);
            if (Directory.Exists(share))
            {
                using var entries = Directory.EnumerateFileSystemEntries(share)
                    .Take(1).GetEnumerator();
                _ = entries.MoveNext();
                feedback = true;
            }
        }
        catch (Exception ex) { AppLog.Write(ex, "Readiness SMB access"); }

        bool queueReadable = false;
        int total = 0, uncertain = 0, attention = 0;
        try
        {
            var entries = GmsaRepairQueue.InspectReadOnly(config);
            queueReadable = true;
            total = entries.Count;
            uncertain = entries.Count(x => x.State is "Attempting" or "AwaitingTask");
            var audited = GmsaQueueAudit.Analyze(entries, now, scheduler);
            attention = audited.Count(x => x.Priority == GmsaQueuePriority.Attention);
        }
        catch (Exception ex) { AppLog.Write(ex, "Readiness gMSA queue"); }

        return Evaluate(new ReadinessEvidence(true, true, config.Enabled,
            scheduler, account, freshHeartbeat, diagnosticFresh,
            diagnosticPassed, feedback, queueReadable, total, uncertain,
            attention), now, fingerprint);
    }
}
