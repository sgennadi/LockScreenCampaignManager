using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

internal sealed class GmsaRepairStatus
{
    private static string StatusPath => Path.Combine(GmsaRepairConfig.StatusDirectory, "last-run.json");
    public DateTimeOffset LastRunUtc { get; set; }
    public int Checked { get; set; }
    public int HealthyConfirmed { get; set; }
    public int RpcAttempts { get; set; }
    public int RecoveredInterrupted { get; set; }
    public string ScopeFingerprint { get; set; } = "";

    internal bool BelongsTo(GmsaRepairConfig? config) =>
        config is not null && ScopeFingerprint.Length == 64 &&
        ScopeFingerprint.All(char.IsAsciiHexDigit) &&
        string.Equals(ScopeFingerprint, GmsaDiagnosticCheck.Fingerprint(config),
            StringComparison.OrdinalIgnoreCase);

    internal static GmsaRepairStatus LoadForScope(GmsaRepairConfig? config)
    {
        var status = Load();
        return status.BelongsTo(config) ? status : new GmsaRepairStatus {
            Summary = "No verified worker heartbeat for this gMSA scope."
        };
    }
    public string Summary { get; set; } = "Not started";
    internal static GmsaRepairStatus Load()
    {
        try
        {
            if (!File.Exists(StatusPath)) return new();
            var info = new FileInfo(StatusPath);
            if (info.Length is < 2 or > 8192 ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
                return new();
            return JsonSerializer.Deserialize<GmsaRepairStatus>(File.ReadAllText(StatusPath)) ?? new();
        }
        catch { return new(); }
    }

    /// <summary>
    /// Distinguishes a live scheduled process from an installed task.
    /// A heartbeat is evidence of an attempted worker cycle, NOT proof that
    /// any endpoint displayed the campaign image or that an RPC succeeded.
    /// </summary>
    internal static string Classify(GmsaRepairStatus status, DateTimeOffset now,
        bool configuredAndScheduled)
    {
        if (!configuredAndScheduled) return "Not enabled";
        if (status.LastRunUtc.Year < 2020 ||
            status.LastRunUtc > now.AddMinutes(5))
            return "Not verified (no valid worker heartbeat)";
        if (now - status.LastRunUtc > TimeSpan.FromMinutes(8))
            return "Overdue (no recent worker heartbeat)";
        if (status.RecoveredInterrupted > 0)
            return "Needs attention (interrupted RPC; awaiting client verification)";
        if (status.Summary.StartsWith("Cycle failed:", StringComparison.OrdinalIgnoreCase) ||
            status.Summary.StartsWith("SMB feedback root unavailable", StringComparison.OrdinalIgnoreCase) ||
            status.Summary.StartsWith("Campaign feedback folder unavailable", StringComparison.OrdinalIgnoreCase) ||
            status.Summary.StartsWith("Client feedback SMB unavailable", StringComparison.OrdinalIgnoreCase) ||
            status.Summary.StartsWith("Malformed client feedback", StringComparison.OrdinalIgnoreCase))
            return "Needs attention (worker ran, operation limited)";
        return "Heartbeat received (endpoint health not implied)";
    }

    internal void Save()
    {
        if (!Directory.Exists(GmsaRepairConfig.StatusDirectory))
            throw new DirectoryNotFoundException("Protected gMSA status folder is missing.");
        string temp = StatusPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temp, StatusPath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

/// <summary>
/// Password-free, unattended worker running ONLY as the configured AD gMSA.
/// No GUI; no local user token; no RSAT/WinRM; never reads admin LocalAppData.
/// A fresh, administrator-owned config and protected queue must be present.
/// </summary>
internal enum GmsaPreRpcDecision
{
    Proceed,
    ClientAlreadyHealthy,
    NeedsManualReview,
    PauseForTelemetry
}

internal static class GmsaRepairWorker
{
    /// <summary>
    /// A final, per-host feedback check happens immediately before attempting
    /// RPC, even if the host was not among the first 20 polled records.
    /// The supplied reader keeps this safety decision testable without AD/SMB.
    /// </summary>
    internal static GmsaPreRpcDecision CheckBeforeRpc(
        GmsaRepairConfig config, DeferredRepairItem due,
        GmsaRepairStatus status, Func<ClientFeedbackRecord?> readFeedback,
        DateTimeOffset now)
    {
        try
        {
            var record = readFeedback();
            if (record is not null &&
                GmsaRepairQueue.CompleteHealth(config, due.Id, record, now))
            {
                status.HealthyConfirmed++;
                CampaignHistoryStore.TryRecordEvent(due.CampaignId,
                    "gMSA Repair: client healthy",
                    "Verified a fresh matching report before remote RPC; no repair task needed.");
                return GmsaPreRpcDecision.ClientAlreadyHealthy;
            }
            return GmsaPreRpcDecision.Proceed;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or JsonException)
        {
            AppLog.Write(ex, "gMSA pre-RPC invalid client feedback");
            GmsaRepairQueue.FlagForReview(config, due.Id,
                "Client health report is invalid; manual inspection required before RPC.", now);
            status.Summary = "Malformed client feedback; affected Repair requires manual review.";
            return GmsaPreRpcDecision.NeedsManualReview;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex, "gMSA pre-RPC SMB feedback unavailable");
            status.Summary = "Client feedback SMB unavailable; no remote RPC attempted.";
            return GmsaPreRpcDecision.PauseForTelemetry;
        }
    }

    internal static int RunOnce()
    {
        try
        {
            var config = GmsaRepairConfig.Load(requireWorkerIdentity: true);
            using var mutex = new Mutex(false, @"Global\LSCM.GmsaRepair.Worker");
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return 0;
            try
            {
                var status = new GmsaRepairStatus {
                    LastRunUtc = DateTimeOffset.UtcNow,
                    ScopeFingerprint = GmsaDiagnosticCheck.Fingerprint(config)
                };
                try
                {
                    ProcessOneCycle(config, status);
                    if (status.Summary == "Not started")
                        status.Summary = $"OK. Checked: {status.Checked}; " +
                            $"healthy: {status.HealthyConfirmed}; RPC: {status.RpcAttempts}.";
                    status.Save();
                    return 0;
                }
                catch (Exception ex)
                {
                    AppLog.Write(ex, "gMSA unattended repair cycle");
                    status.Summary = "Cycle failed: " + UniversalDiagnosticsEmbedded.Clean(ex.Message, 150);
                    try { status.Save(); } catch (Exception saveEx) { AppLog.Write(saveEx, "gMSA status save"); }
                    return 1;
                }
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "gMSA unattended startup");
            return 1; // Fail closed, never fall back to administrator identity.
        }
    }

    private static void ProcessOneCycle(GmsaRepairConfig config, GmsaRepairStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        // Worker mutex is already held. This is a NEW process/cycle: any
        // persisted Attempting entry belongs to a previous incomplete run,
        // possibly stopped by server reboot. Only health polling may resume.
        status.RecoveredInterrupted =
            GmsaRepairQueue.RecoverInterruptedAttempts(config, now);
        if (status.RecoveredInterrupted > 0)
            status.Summary = "Recovered interrupted RPC: " + status.RecoveredInterrupted +
                "; awaiting Client Health, no automatic RPC repeat.";
        var requests = GmsaRepairQueue.All(config).OrderBy(x => x.NextAttemptUtc).ToArray();
        foreach (var old in requests.Where(x => x.State == "Queued" &&
                (x.ApprovedUtc <= now.AddDays(-14) || x.Attempts >= 20)))
            GmsaRepairQueue.ExpireQueued(config, old.Id, now);

        // Do not run a remote task without access to the dedicated client
        // feedback share. SYSVOL and administrative shares are rejected by
        // FeedbackPath.ValidateRoot.
        string share = FeedbackPath.ValidateRoot(config.FeedbackShare);
        if (!Directory.Exists(share))
        {
            status.Summary = "SMB feedback root unavailable. No remote RPC attempted.";
            return;
        }

        foreach (var item in requests.Where(x =>
                x.State is "Queued" or "Attempting" or "AwaitingHealth" or "AwaitingTask" &&
                x.NextAttemptUtc <= now).Take(20))
        {
            status.Checked++;
            if (item.State is "Attempting" or "AwaitingHealth" or "AwaitingTask")
            {
                if (!item.SubmittedUtc.HasValue)
                {
                    GmsaRepairQueue.FlagForReview(config, item.Id,
                        "Missing remote submission timestamp; inspect manually.", now);
                    continue;
                }
                if (now - item.SubmittedUtc.Value > TimeSpan.FromHours(2))
                {
                    GmsaRepairQueue.FlagForReview(config, item.Id,
                        "No fresh healthy report within two hours; manual investigation required.", now);
                    continue;
                }
            }

            try
            {
                var record = ClientFeedbackReader.ReadSingle(share, item.CampaignId,
                    item.ComputerFqdn.Split('.')[0], DateTimeOffset.UtcNow);
                if (record is not null)
                {
                    if (GmsaRepairQueue.CompleteHealth(config, item.Id, record, DateTimeOffset.UtcNow))
                    {
                        status.HealthyConfirmed++;
                        CampaignHistoryStore.TryRecordEvent(item.CampaignId,
                            "gMSA Repair: client healthy",
                            "Unattended worker verified a fresh matching SMB health report.");
                        continue;
                    }
                    if (item.State is "Attempting" or "AwaitingHealth" or "AwaitingTask" &&
                        item.SubmittedUtc.HasValue &&
                        record.LastSeenUtc >= item.SubmittedUtc.Value &&
                        now - item.SubmittedUtc.Value > TimeSpan.FromMinutes(5))
                    {
                        GmsaRepairQueue.FlagForReview(config, item.Id,
                            "Client report is fresh but unhealthy; no automatic RPC rerun.", now);
                        continue;
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or JsonException)
            {
                AppLog.Write(ex, "gMSA invalid client feedback");
                // A corrupt/foreign report must never be treated as "offline".
                // Hold this host for explicit operator review instead.
                GmsaRepairQueue.FlagForReview(config, item.Id,
                    "Client feedback is invalid; inspect before resubmitting Repair.", now);
                status.Summary = "Malformed client feedback; affected Repair requires manual review.";
                continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Write(ex, "gMSA client feedback unavailable");
                status.Summary = "Client feedback SMB unavailable; no remote RPC attempted.";
                // The share or an individual report could have lost access after
                // the earlier root probe. Do not execute any RPC in this cycle.
                return;
            }

            if (item.State is "Attempting" or "AwaitingHealth" or "AwaitingTask")
                GmsaRepairQueue.SchedulePoll(config, item.Id, DateTimeOffset.UtcNow);
        }

        // Never more than ONE remote repair per scheduled cycle.
        var due = GmsaRepairQueue.All(config)
            .Where(x => x.Due(DateTimeOffset.UtcNow))
            .OrderBy(x => x.NextAttemptUtc).FirstOrDefault();
        if (due is null) return;

        // The campaign folder must be accessible too. Do not treat an absent
        // feedback location as evidence the machine is offline.
        if (!Directory.Exists(FeedbackPath.CampaignDirectory(share, due.CampaignId)))
        {
            status.Summary = "Campaign feedback folder unavailable. No RPC attempted.";
            return;
        }
        var feedbackDecision = CheckBeforeRpc(config, due, status,
            () => ClientFeedbackReader.ReadSingle(share, due.CampaignId,
                due.ComputerFqdn.Split('.')[0], DateTimeOffset.UtcNow),
            DateTimeOffset.UtcNow);
        if (feedbackDecision != GmsaPreRpcDecision.Proceed)
            return;
        // Re-read protected admin settings after the SMB check: disabling the
        // worker between read and RPC must still block new remote execution.
        var fresh = GmsaRepairConfig.Load(requireWorkerIdentity: true);
        if (!fresh.ScopeMatches(due.Domain, due.OuDn, due.GroupDn) ||
            !GmsaRepairQueue.BeginAttempt(fresh, due.Id, DateTimeOffset.UtcNow))
            return;
        status.RpcAttempts++;
        try
        {
            var outcome = DeferredRemoteRepair.Execute(due,
                new DomainCredentials(due.Domain, "", ""));
            GmsaRepairQueue.FinishAttempt(fresh, due.Id, outcome.Result, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            // A timeout can occur AFTER Task Scheduler registered remote work.
            // Do not rerun if completion state could be unknown.
            AppLog.Write(ex, "gMSA RPC");
            try { GmsaRepairQueue.FinishAttempt(fresh, due.Id, "failed", DateTimeOffset.UtcNow); }
            catch (Exception queueEx) { AppLog.Write(queueEx, "gMSA RPC queue finalization"); }
        }
    }
}
