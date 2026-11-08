using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

internal sealed class BackgroundRepairStatus
{
    internal static string? TestRootOverride;
    private static string PathName => Path.Combine(
        TestRootOverride ?? DeferredRepairQueue.DirectoryPath, "background-status.json");
    public DateTimeOffset LastRunUtc { get; set; }
    public int Checked { get; set; }
    public int HealthyConfirmed { get; set; }
    public int RemoteAttempts { get; set; }
    public string Summary { get; set; } = "Not started";
    public static BackgroundRepairStatus Load()
    {
        try
        {
            if (!File.Exists(PathName) || new FileInfo(PathName).Length > 8192)
                return new();
            return JsonSerializer.Deserialize<BackgroundRepairStatus>(File.ReadAllText(PathName)) ?? new();
        }
        catch { return new(); }
    }
    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        string temp = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temp, PathName, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

/// <summary>
/// One-shot, silent worker invoked by an opt-in user-scoped Scheduled Task.
/// Never starts from client-provided SMB data. It can only retry a pre-approved
/// Queue item with explicit background permission and a matching Windows SID.
/// No credentials are stored; current Windows Kerberos/NTLM identity is used.
/// </summary>
internal static class BackgroundRepairWorker
{
    private const int MaxFeedbackReads = 20;

    internal static int RunOnce()
    {
        var settings = BackgroundRepairSettings.Load();
        if (!settings.Enabled || settings.AccountSid != BackgroundRepairIdentity.CurrentSid())
            return 0;

        using var mutex = new Mutex(false,
            @"Global\LSCM.BackgroundRepair.Worker." + BackgroundRepairIdentity.CurrentSid());
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return 0;

        try
        {
            var status = new BackgroundRepairStatus { LastRunUtc = DateTimeOffset.UtcNow };
            try
            {
                ProcessOneCycle(settings, status);
                if (status.Summary == "Not started")
                    status.Summary = $"OK. Checked {status.Checked}; healthy {status.HealthyConfirmed}; " +
                        $"RPC attempts {status.RemoteAttempts}.";
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "Background Repair worker");
                status.Summary = "Background cycle failed: " +
                    UniversalDiagnosticsEmbedded.Clean(ex.Message, 135);
                status.Save();
                return 1;
            }
            status.Save();
            return 0;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static void ProcessOneCycle(BackgroundRepairSettings settings,
        BackgroundRepairStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = DeferredRepairQueue.All()
            .Where(settings.Allows)
            .OrderBy(x => x.NextAttemptUtc)
            .ToArray();

        // Expire old approvals without any RPC.
        foreach (var item in pending.Where(x => x.State == "Queued" &&
            (x.ApprovedUtc < now.AddDays(-14) || x.Attempts >= 20)))
            DeferredRepairQueue.ExpireQueued(item.Id, now);

        // The feedback share is a separate protected SMB path, never SYSVOL.
        // If it is inaccessible, do not start NEW background RPC operations:
        // without feedback the manager could not reliably verify execution.
        string root = FeedbackPath.ValidateRoot(settings.FeedbackShareRoot);
        if (!Directory.Exists(root))
        {
            status.Summary = "Feedback share unavailable; no RPC attempted.";
            return;
        }

        foreach (var item in pending.Where(x => x.State is
                    "Queued" or "Attempting" or "AwaitingHealth" or "AwaitingTask")
                .Where(x => x.NextAttemptUtc <= now)
                .Take(MaxFeedbackReads))
        {
            if (!BackgroundRepairSettings.Load().Enabled) return; // Honor manual disable.
            status.Checked++;
            if (item.State is "Attempting" or "AwaitingHealth" or "AwaitingTask")
            {
                if (!item.SubmittedUtc.HasValue)
                {
                    DeferredRepairQueue.FlagForReview(item.Id,
                        "Missing remote submission timestamp; inspect task manually.", now);
                    continue;
                }
                if (now - item.SubmittedUtc.Value > TimeSpan.FromHours(2))
                {
                    DeferredRepairQueue.FlagForReview(item.Id,
                        "No healthy client feedback within two hours; manual review required.", now);
                    continue;
                }
            }
            try
            {
                var shortName = item.ComputerFqdn.Split('.')[0];
                var record = ClientFeedbackReader.ReadSingle(root, item.CampaignId,
                    shortName, DateTimeOffset.UtcNow);
                if (record is not null)
                {
                    bool confirmed = item.State == "Queued"
                        ? DeferredRepairQueue.CompleteHealthyOffline(
                            item.Id, record, DateTimeOffset.UtcNow)
                        : DeferredRepairQueue.CompleteVerified(
                            item.Id, record, DateTimeOffset.UtcNow);
                    if (confirmed)
                    {
                        status.HealthyConfirmed++;
                        CampaignHistoryStore.TryRecordEvent(item.CampaignId,
                            "Background Repair: client healthy",
                            "Fresh client SMB report confirmed healthy; no additional RPC.");
                        continue;
                    }
                    if (item.State is "Attempting" or "AwaitingHealth" or "AwaitingTask" &&
                        item.SubmittedUtc.HasValue &&
                        record.LastSeenUtc >= item.SubmittedUtc.Value &&
                        now - item.SubmittedUtc.Value > TimeSpan.FromMinutes(5))
                    {
                        DeferredRepairQueue.FlagForReview(item.Id,
                            "Fresh client report is unhealthy; manual review required.", now);
                        continue;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidDataException or ArgumentException or JsonException)
            {
                AppLog.Write(ex, "Background Repair client health");
                // No report is NOT approval for RPC; original task approval is.
            }
            if (item.State is "Attempting" or "AwaitingHealth" or "AwaitingTask")
                DeferredRepairQueue.ScheduleHealthPoll(item.Id, DateTimeOffset.UtcNow);
        }

        // At most ONE approved RPC retry per scheduled cycle, only for a queued
        // job that is still due and belongs to this Windows account.
        if (!BackgroundRepairSettings.Load().Enabled) return;
        var due = DeferredRepairQueue.All()
            .Where(settings.Allows).Where(x => x.Due(DateTimeOffset.UtcNow))
            .OrderBy(x => x.NextAttemptUtc).FirstOrDefault();
        if (due is null) return;
        if (!Directory.Exists(root)) return;
        // A reachable share root is not enough: do not start a repair if this
        // campaign's feedback folder is unavailable or access is denied.
        string campaignFolder = FeedbackPath.CampaignDirectory(root, due.CampaignId);
        if (!Directory.Exists(campaignFolder))
        {
            status.Summary = "Campaign feedback folder unavailable; no background RPC attempted.";
            return;
        }
        // No automatic access to alternate domain credentials. Windows token only.
        // Double-check settings and persist Attempting BEFORE any remote execution.
        var current = BackgroundRepairSettings.Load();
        if (!current.Enabled || !current.Allows(due) ||
            !string.Equals(current.FeedbackShareRoot, root, StringComparison.OrdinalIgnoreCase) ||
            !DeferredRepairQueue.BeginAttempt(due.Id, DateTimeOffset.UtcNow,
                requireBackgroundPermission: true))
            return;
        status.RemoteAttempts++;
        try
        {
            var credentials = new DomainCredentials(due.Domain, "", "");
            var outcome = DeferredRemoteRepair.Execute(due, credentials);
            DeferredRepairQueue.FinishAttempt(due.Id, outcome.Result, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            // Timeout after remote submission may be ambiguous. Never auto retry.
            AppLog.Write(ex, "Background Repair RPC");
            try { DeferredRepairQueue.FinishAttempt(due.Id, "failed", DateTimeOffset.UtcNow); }
            catch (Exception queueError) { AppLog.Write(queueError, "Background queue state"); }
        }
    }
}
