using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace LockScreenCampaignManager;

/// <summary>
/// Operator-approved retry queue. Stores NO credentials and launches nothing by itself.
/// Foreground manager or an explicitly opted-in per-user scheduled worker must
/// validate OU, GPO and security group before each remote attempt.
/// </summary>
internal sealed class DeferredRepairItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Domain { get; set; } = "";
    public string DomainController { get; set; } = "";
    public string OuDn { get; set; } = "";
    public string GroupDn { get; set; } = "";
    public string GpoId { get; set; } = "";
    public string CampaignId { get; set; } = "";
    public string ComputerFqdn { get; set; } = "";
    public DateTimeOffset ApprovedUtc { get; set; }
    public DateTimeOffset NextAttemptUtc { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? SubmittedUtc { get; set; } // set just BEFORE remote task invocation
    public int Attempts { get; set; }
    public string State { get; set; } = "Queued"; // Queued / Attempting / AwaitingHealth / AwaitingTask / NeedsAttention
    public string Reason { get; set; } = "RPC unavailable";
    public bool BackgroundAllowed { get; set; }
    public string ApprovalSid { get; set; } = "";

    public bool Due(DateTimeOffset utcNow) =>
        State == "Queued" && NextAttemptUtc <= utcNow &&
        ApprovedUtc > utcNow.AddDays(-14) && Attempts < 20;
}

internal static class DeferredRepairQueue
{
    internal static string? TestRootOverride;
    internal static string DirectoryPath => TestRootOverride ??
        Path.Combine(AppLog.DirectoryPath, "DeferredRepair");
    private static string QueuePath => Path.Combine(DirectoryPath, "queue.json");
    private static readonly object Gate = new();
    private static readonly TimeSpan MutexWait = TimeSpan.FromSeconds(15);

    private sealed class Lease(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            try { mutex.ReleaseMutex(); }
            finally { mutex.Dispose(); }
        }
    }

    private static IDisposable AcquireCrossProcess()
    {
        // Prevent lost updates when GUI and Task Scheduler use the same queue.
        string sid = BackgroundRepairIdentity.CurrentSid();
        var mutex = new Mutex(false, @"Global\LSCM.DeferredRepair.Queue." + sid);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(MutexWait); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Deferred Repair queue is busy; retry later.");
            return new Lease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Regex Fqdn = new(
        @"^[A-Za-z0-9][A-Za-z0-9.-]{1,253}$", RegexOptions.CultureInvariant);
    private const int MaxEntries = 500;

    internal static bool IsValidForStorage(DeferredRepairItem x) => Valid(x);

    private static bool Valid(DeferredRepairItem x) =>
        x.Id.Length == 32 && Guid.TryParseExact(x.Id, "N", out _) &&
        x.Domain.Length is >= 3 and <= 253 &&
        Fqdn.IsMatch(x.Domain) &&
        (x.DomainController.Length == 0 || Fqdn.IsMatch(x.DomainController)) &&
        x.OuDn.Length is > 3 and <= 1024 &&
        x.GroupDn.Length is > 3 and <= 1024 &&
        x.ComputerFqdn.Length <= 253 && Fqdn.IsMatch(x.ComputerFqdn) &&
        FeedbackPath.IsValidHost(x.ComputerFqdn.Split('.')[0]) &&
        Guid.TryParse(x.GpoId, out _) &&
        Regex.IsMatch(x.CampaignId, @"^[a-z0-9][a-z0-9_-]{0,70}$") &&
        x.ApprovedUtc.Year >= 2025 && x.ApprovedUtc <= DateTimeOffset.UtcNow.AddMinutes(5) &&
        x.Attempts is >= 0 and <= 30 &&
        x.State is "Queued" or "Attempting" or "AwaitingHealth" or "NeedsAttention" or "AwaitingTask" &&
        (x.State is not ("Attempting" or "AwaitingHealth" or "AwaitingTask") || x.SubmittedUtc.HasValue || x.State == "AwaitingTask") &&
        (!x.SubmittedUtc.HasValue || x.SubmittedUtc.Value <= DateTimeOffset.UtcNow.AddMinutes(5)) &&
        x.Reason.Length <= 200 &&
        (!x.BackgroundAllowed ||
            (x.ApprovalSid.Length is >= 10 and <= 200 &&
             Regex.IsMatch(x.ApprovalSid, @"^S-1-\d+(-\d+)+$", RegexOptions.CultureInvariant)));

    private static List<DeferredRepairItem> Read()
    {
        if (!File.Exists(QueuePath)) return new();
        var info = new FileInfo(QueuePath);
        if (info.Length is < 2 or > 1_000_000)
            throw new InvalidDataException("Deferred repair queue has an invalid size.");
        var items = JsonSerializer.Deserialize<List<DeferredRepairItem>>(
            File.ReadAllText(QueuePath, Encoding.UTF8), Json)
            ?? throw new InvalidDataException("Deferred queue is empty.");
        if (items.Count > MaxEntries || items.Any(x => x is null || !Valid(x)) ||
            items.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new InvalidDataException("Deferred repair queue contains invalid entries.");
        return items;
    }

    private static void Write(List<DeferredRepairItem> items, string disposition = "")
    {
        if (items.Count > MaxEntries || items.Any(x => !Valid(x)))
            throw new InvalidDataException("Invalid deferred queue state.");
        Directory.CreateDirectory(DirectoryPath);
        var previous = Read(); // immutable snapshot from disk under cross-process queue lock.
        string temp = QueuePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(items, Json), new UTF8Encoding(false));
            File.Move(temp, QueuePath, overwrite: true);
            RepairHistoryJournal.RecordQueueDiff(DirectoryPath, "User", previous, items, disposition);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static IReadOnlyList<DeferredRepairItem> All()
    {
        lock (Gate) { using var lease = AcquireCrossProcess(); return Read().ToArray(); }
    }

    internal static IReadOnlyList<DeferredRepairItem> ForScope(
        string domain, string dc, string ouDn, string groupDn)
    {
        lock (Gate)
        {
            using var lease = AcquireCrossProcess();
            return Read().Where(x =>
                x.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase) &&
                x.DomainController.Equals(dc, StringComparison.OrdinalIgnoreCase) &&
                x.OuDn.Equals(ouDn, StringComparison.OrdinalIgnoreCase) &&
                x.GroupDn.Equals(groupDn, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }

    /// <summary>
    /// Old requests and those created under alternate in-memory credentials
    /// remain foreground-only. Enabling background later NEVER upgrades old
    /// jobs without fresh operator approval.
    /// </summary>
    internal static int BackgroundCount() => All().Count(x => x.BackgroundAllowed);

    /// <summary>Expire an unused approval without pretending an RPC attempt happened.</summary>
    internal static bool ExpireQueued(string id, DateTimeOffset now)
    {
        lock (Gate)
        {
            using var lease = AcquireCrossProcess();
            var items = Read();
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null || item.State != "Queued" ||
                item.Attempts < 20 && item.ApprovedUtc >= now.AddDays(-14)) return false;
            item.State = "NeedsAttention";
            item.Reason = "Approval expired or RPC retry limit reached; manual reapproval required.";
            Write(items);
            return true;
        }
    }

    // Called ONLY after the user approved an immutable Repair Preview and typed GPO GUID.
    internal static int EnqueueApproved(string domain, string dc, string ouDn, string groupDn,
        string gpoId, string campaignId, IEnumerable<string> verifiedOfflineHosts, DateTimeOffset now)
    {
        if (!Guid.TryParse(gpoId, out var guid)) throw new ArgumentException("Invalid approved GPO GUID");
        var hosts = verifiedOfflineHosts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (hosts.Length > 25) throw new ArgumentOutOfRangeException(nameof(verifiedOfflineHosts));
        lock (Gate)
        {
            using var lease = AcquireCrossProcess();
            var items = Read();
            int added = 0;
            foreach (string host in hosts)
            {
                if (items.Any(x => x.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase) &&
                    x.CampaignId == campaignId && Guid.Parse(x.GpoId) == guid &&
                    x.ComputerFqdn.Equals(host, StringComparison.OrdinalIgnoreCase) &&
                    x.State != "NeedsAttention"))
                    continue;

                var item = new DeferredRepairItem {
                    Domain = domain, DomainController = dc, OuDn = ouDn, GroupDn = groupDn,
                    GpoId = guid.ToString("D"), CampaignId = campaignId,
                    ComputerFqdn = host, ApprovedUtc = now,
                    NextAttemptUtc = now.AddMinutes(1)
                };
                if (!Valid(item)) throw new ArgumentException("Deferred target/scope failed validation");
                items.Add(item);
                added++;
            }
            if (added > 0) Write(items);
            return added;
        }
    }

    /// <summary>
    /// Records outcomes of the FIRST operator-approved Repair, not just offline hosts.
    /// "success" means the remote task exited, NOT that client health is verified.
    /// "pending" means task execution is ambiguous and must NEVER be repeated.
    /// Only recognized offline responses are eligible for automatic RPC retry.
    /// No credentials or session tokens are persisted.
    /// </summary>
    internal static (int Offline, int AwaitingHealth, int AwaitingTask, int NeedsAttention) EnqueueInitialApprovedResults(
        string domain, string dc, string ouDn, string groupDn,
        string gpoId, string campaignId, IReadOnlyDictionary<string, string> results,
        DateTimeOffset submittedUtc, DateTimeOffset now, bool backgroundAllowed = false)
    {
        if (!Guid.TryParse(gpoId, out var guid))
            throw new ArgumentException("Invalid approved GPO GUID.");
        if (results is null || results.Count is < 1 or > 25)
            throw new ArgumentException("Expected 1-25 approved initial Repair results.");
        if (submittedUtc > now.AddMinutes(5) || submittedUtc < now.AddHours(-1))
            throw new ArgumentException("Remote operation timestamp is outside the allowed window.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            if (!seen.Add(result.Key) ||
                result.Value is not ("offline" or "success" or "pending" or "failed"))
                throw new ArgumentException("Duplicate host or unsupported remote result.");
        }

        int offline = 0, awaitingHealth = 0, awaitingTask = 0, needsAttention = 0;
        lock (Gate)
        {
            using var lease = AcquireCrossProcess();
            var items = Read();
            foreach (var result in results)
            {
                // Failed/permission-denied jobs are visible to administrators, but NEVER retried automatically.
                // A new explicitly approved Repair supersedes all previous local
                // tracking for the SAME host + campaign + GPO. Keep the latest
                // outcome: otherwise an older Queued entry could retry RPC after
                // a new action has succeeded, or an old Pending might disappear.
                string state = result.Value switch
                {
                    "offline" => "Queued",
                    "success" => "AwaitingHealth",
                    "pending" => "AwaitingTask",
                    "failed" => "NeedsAttention",
                    _ => throw new InvalidOperationException("Unexpected repair result.")
                };
                var item = new DeferredRepairItem
                {
                    Domain = domain, DomainController = dc, OuDn = ouDn, GroupDn = groupDn,
                    GpoId = guid.ToString("D"), CampaignId = campaignId,
                    ComputerFqdn = result.Key, ApprovedUtc = submittedUtc,
                    BackgroundAllowed = backgroundAllowed,
                    ApprovalSid = backgroundAllowed ? BackgroundRepairIdentity.CurrentSid() : "",
                    SubmittedUtc = state is "Queued" or "NeedsAttention" ? null : submittedUtc,
                    NextAttemptUtc = now.AddMinutes(state == "Queued" ? 1 : 2),
                    State = state,
                    Reason = state switch
                    {
                        "Queued" => "Initial RPC unavailable; retry when reachable.",
                        "AwaitingHealth" => "Remote task exited; awaiting fresh healthy client report.",
                        "AwaitingTask" => "Remote task status uncertain; do not repeat RPC.",
                        _ => "Initial remote repair failed; inspect error details. No automatic retry."
                    }
                };
                if (!Valid(item))
                    throw new ArgumentException("Initial Repair target/scope failed validation.");
                // New approval replaces earlier tracking, including old Queued
                // entries, so stale RPC retries cannot survive a successful repair.
                items.RemoveAll(x => x.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase) &&
                    x.CampaignId == campaignId && Guid.Parse(x.GpoId) == guid &&
                    x.ComputerFqdn.Equals(result.Key, StringComparison.OrdinalIgnoreCase));
                items.Add(item);
                if (state == "Queued") offline++;
                else if (state == "AwaitingHealth") awaitingHealth++;
                else if (state == "AwaitingTask") awaitingTask++;
                else needsAttention++;
            }
            if (offline + awaitingHealth + awaitingTask + needsAttention > 0) Write(items);
        }
        return (offline, awaitingHealth, awaitingTask, needsAttention);
    }

    /// <summary>
    /// An administrator has confirmed a NEW Repair for these exact computers.
    /// Atomically suspend old queued background retries before initial RPC so a
    /// scheduled worker cannot race with the fresh operator-approved attempt.
    /// Never overwrite an in-flight or ambiguous task.
    /// </summary>
    internal static void SuspendBackgroundForApprovedRepair(string domain, string gpoId,
        string campaignId, IEnumerable<string> hosts)
    {
        var selected = new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase);
        if (selected.Count is < 1 or > 25 || !Guid.TryParse(gpoId, out var guid))
            throw new ArgumentException("Invalid new Repair targets.");
        lock (Gate)
        {
            using var lease = AcquireCrossProcess();
            var items = Read();
            var matching = items.Where(x =>
                x.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase) &&
                Guid.Parse(x.GpoId) == guid && x.CampaignId == campaignId &&
                selected.Contains(x.ComputerFqdn)).ToArray();
            if (matching.Any(x => x.State is "Attempting" or "AwaitingHealth" or "AwaitingTask"))
                throw new InvalidOperationException(
                    "Previous Repair is still running or unconfirmed; inspect Deferred Repair before retry.");
            bool changed = false;
            foreach (var item in matching.Where(x => x.State == "Queued"))
            {
                // Revoking just BackgroundAllowed is not enough: the GUI timer
                // could still repeat this old request if user background mode
                // is disabled later. Supersede the old request completely.
                item.BackgroundAllowed = false;
                item.ApprovalSid = "";
                item.State = "NeedsAttention";
                item.Reason = "Superseded by an explicitly approved new Repair.";
                changed = true;
            }
            if (changed) Write(items);
        }
    }

    /// <summary>
    /// Persist a fail-closed in-flight marker BEFORE invoking remote RPC.
    /// If the manager exits between sending RPC and receiving its response,
    /// reopening it will not automatically run the task a second time.
    /// </summary>
    internal static bool BeginAttempt(string id, DateTimeOffset now,
        bool requireBackgroundPermission = false)
    {
        lock (Gate)
        {
            using var lease = AcquireCrossProcess();
            var items = Read();
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null || item.State != "Queued" ||
                item.ApprovedUtc < now.AddDays(-14) || item.Attempts >= 20 ||
                requireBackgroundPermission &&
                (!item.BackgroundAllowed ||
                 item.ApprovalSid != BackgroundRepairIdentity.CurrentSid()))
                return false;
            item.State = "Attempting";
            item.Attempts++;
            item.LastAttemptUtc = now;
            item.SubmittedUtc = now;
            item.NextAttemptUtc = now.AddMinutes(1);
            item.Reason = "Remote attempt started. If interrupted, do not retry automatically.";
            Write(items);
            return true;
        }
    }

    /// <summary>
    /// Only an explicit "offline before task registration" result can safely
    /// return a persisted in-flight job to the retry queue.
    /// </summary>
    internal static void FinishAttempt(string id, string result, DateTimeOffset now)
    {
        if (result is not ("offline" or "success" or "pending" or "failed"))
            throw new ArgumentException("Unsupported remote retry outcome.");
        lock (Gate)
        {
            using var lease = AcquireCrossProcess();
            var items = Read();
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null || item.State != "Attempting" || !item.SubmittedUtc.HasValue)
                return;
            switch (result)
            {
                case "offline":
                    item.State = "Queued";
                    item.SubmittedUtc = null;
                    item.Reason = "RPC unavailable before task registration; retry scheduled.";
                    item.NextAttemptUtc = now.AddMinutes(
                        Math.Min(30, 2 * Math.Pow(2, Math.Min(item.Attempts, 4))));
                    if (item.Attempts >= 20 || item.ApprovedUtc < now.AddDays(-14))
                    {
                        item.State = "NeedsAttention";
                        item.Reason = "Approval expired or retry limit reached; manual review required.";
                    }
                    break;
                case "success":
                    item.State = "AwaitingHealth";
                    item.Reason = "Remote task exited; awaiting fresh healthy client status.";
                    item.NextAttemptUtc = now.AddMinutes(1);
                    break;
                case "pending":
                    item.State = "AwaitingTask";
                    item.Reason = "Remote task outcome uncertain; no automatic RPC retry.";
                    item.NextAttemptUtc = now.AddMinutes(1);
                    break;
                default:
                    item.State = "NeedsAttention";
                    item.Reason = "Remote task failed or eligibility changed; manual review required.";
                    break;
            }
            Write(items);
        }
    }

    internal static void Update(string id, string state, string reason, DateTimeOffset now,
        DateTimeOffset? submittedUtc = null)
    {
        if (state is not ("Queued" or "AwaitingHealth" or "NeedsAttention" or "AwaitingTask"))
            throw new ArgumentException("Invalid deferred result. Completion requires verified feedback.");
        lock (Gate)
        {
            using var lease = AcquireCrossProcess();
            var items = Read();
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null) return;
            if (item.State != "Queued")
                throw new InvalidOperationException("Only queued jobs can transition after an RPC attempt.");
            item.Attempts++;
            item.State = state;
            item.LastAttemptUtc = now;
            item.Reason = UniversalDiagnosticsEmbedded.Clean(reason, 180);
            if (state is "AwaitingHealth" or "AwaitingTask" && submittedUtc.HasValue)
            {
                item.SubmittedUtc = submittedUtc;
            }
            if (state == "AwaitingHealth")
            {
                if (!submittedUtc.HasValue || submittedUtc.Value > now.AddMinutes(5) ||
                    submittedUtc.Value < now.AddMinutes(-30))
                    throw new ArgumentException("Missing or invalid remote submission timestamp.");
                item.SubmittedUtc = submittedUtc;
                item.NextAttemptUtc = now.AddMinutes(2); // next feedback check
            }
            else if (state == "Queued")
                item.NextAttemptUtc = now.AddMinutes(
                    Math.Min(30, 2 * Math.Pow(2, Math.Min(item.Attempts, 4))));
            // The last approved retry may have submitted an actual remote task.
            // Preserve the health-confirmation state even when the RPC limit is
            // reached; only a still-queued request becomes terminal.
            if (item.State == "Queued" &&
                (item.Attempts >= 20 || item.ApprovedUtc < now.AddDays(-14)))
            {
                item.State = "NeedsAttention";
                item.Reason = "Retry limit/approval window reached; renew manual approval.";
            }
            Write(items);
        }
    }

    // Client health is an untrusted operational hint, not physical screen attestation.
    internal static bool MatchesHealthy(DeferredRepairItem item, ClientFeedbackRecord record,
        DateTimeOffset firstAcceptableUtc, DateTimeOffset now)
    {
        var shortName = item.ComputerFqdn.Split('.')[0];
        return record.Computer.Equals(shortName, StringComparison.OrdinalIgnoreCase) &&
            record.CampaignId.Equals(item.CampaignId, StringComparison.Ordinal) &&
            record.LastSeenUtc >= firstAcceptableUtc &&
            record.LastSeenUtc <= now.AddMinutes(5) &&
            record.Health == "Healthy" &&
            record.Stage is "Active" or "Pending" &&
            record.ImageMatches && record.TaskPresent &&
            record.MissingFiles == 0 && record.CorruptFiles == 0 &&
            (record.Stage != "Active" || record.RegistryMatches);
    }

    /// <summary>
    /// A machine may have repaired itself on startup after the operator approved
    /// Repair Selected but before the manager could retry RPC. Fresh healthy
    /// feedback can satisfy the request without unnecessary remote execution.
    /// No client-supplied report can authorize a new RPC command.
    /// </summary>
    internal static bool CompleteHealthyOffline(string id, ClientFeedbackRecord record, DateTimeOffset now)
    {
        lock (Gate)
        {
            using var lease = AcquireCrossProcess();
            var items = Read();
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null || item.State != "Queued" ||
                item.ApprovedUtc < now.AddDays(-14) ||
                !MatchesHealthy(item, record, item.ApprovedUtc, now))
                return false;
            items.Remove(item);
            Write(items, "HealthyConfirmed");
            return true;
        }
    }

    // Remote task exit 0 alone is NOT completion; client feedback must be newer.
    internal static bool CompleteVerified(string id, ClientFeedbackRecord record, DateTimeOffset now)
    {
        lock (Gate)
        {
            using var lease = AcquireCrossProcess();
            var items = Read();
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null || item.State is not ("Attempting" or "AwaitingHealth" or "AwaitingTask") ||
                !item.SubmittedUtc.HasValue ||
                !MatchesHealthy(item, record, item.SubmittedUtc.Value, now))
                return false;
            items.Remove(item);
            Write(items, "HealthyConfirmed");
            return true;
        }
    }

    /// <summary>Schedule another SMB read, not a remote Repair attempt.</summary>
    internal static void ScheduleHealthPoll(string id, DateTimeOffset now)
    {
        lock (Gate)
        {
            using var lease = AcquireCrossProcess();
            var items = Read();
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null || item.State is not ("Attempting" or "AwaitingHealth" or "AwaitingTask")) return;
            item.NextAttemptUtc = now.AddMinutes(1);
            Write(items);
        }
    }

    internal static void FlagForReview(string id, string reason, DateTimeOffset now)
    {
        lock (Gate)
        {
            using var lease = AcquireCrossProcess();
            var items = Read();
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null || item.State is not ("Attempting" or "AwaitingHealth" or "AwaitingTask")) return;
            item.State = "NeedsAttention";
            item.Reason = UniversalDiagnosticsEmbedded.Clean(reason, 180);
            item.LastAttemptUtc = now;
            Write(items);
        }
    }


    internal static bool Cancel(string id)
    {
        lock (Gate)
        {
            using var lease = AcquireCrossProcess();
            var items = Read();
            int removed = items.RemoveAll(x => x.Id == id);
            if (removed > 0) Write(items, "Cancelled");
            return removed > 0;
        }
    }
}
