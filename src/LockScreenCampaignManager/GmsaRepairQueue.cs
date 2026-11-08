using System.Diagnostics;
using System.Security.AccessControl;
using System.Threading;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Separate, machine-wide, ACL-protected queue. The gMSA does NOT have access
/// to the approving administrator's LocalAppData user queue. All mutations
/// are serialized across processes and recorded atomically on the same volume.
/// </summary>
internal static class GmsaRepairQueue
{
    private const int MaxEntries = 500;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string PathName => Path.Combine(GmsaRepairConfig.QueueDirectory, "queue.json");

    // A file lock within the ACL-protected queue directory is accessible by
    // BOTH local Administrators and the gMSA. Named kernel objects can be
    // inaccessible when created with a different account's default DACL.
    private static IDisposable Lock()
    {
        var lockPath = Path.Combine(GmsaRepairConfig.QueueDirectory, "queue.lock");
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                if (File.Exists(lockPath) &&
                    (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("gMSA queue lock is a reparse point.");
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, bufferSize: 4096);
            }
            catch (IOException) when (watch.Elapsed < TimeSpan.FromSeconds(15))
            {
                Thread.Sleep(100);
            }
        }
    }

    private static void CheckDirectory(GmsaRepairConfig config)
    {
        string path = GmsaRepairConfig.QueueDirectory;
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException("The protected gMSA queue directory is missing.");
        if (GmsaRepairConfig.TestRootOverride is not null) return;
        var info = new DirectoryInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("gMSA queue is a junction or reparse point.");
        var acl = info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (!acl.AreAccessRulesProtected)
            throw new UnauthorizedAccessException("gMSA queue must have a protected ACL.");
        var owner = acl.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner?.Value is not ("S-1-5-18" or "S-1-5-32-544"))
            throw new UnauthorizedAccessException("The gMSA queue directory owner is untrusted.");
        var write = FileSystemRights.WriteData | FileSystemRights.AppendData |
                    FileSystemRights.Delete | FileSystemRights.ChangePermissions |
                    FileSystemRights.TakeOwnership | FileSystemRights.WriteAttributes;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & write) == 0)
                continue;
            string sid = ((SecurityIdentifier)rule.IdentityReference).Value;
            if (sid is not ("S-1-5-18" or "S-1-5-32-544") &&
                !string.Equals(sid, config.GmsaSid, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("gMSA queue has an unapproved writer.");
        }
    }

    private static List<DeferredRepairItem> Read(GmsaRepairConfig config, bool inspection = false)
    {
        CheckDirectory(config);
        if (!File.Exists(PathName)) return new();
        var info = new FileInfo(PathName);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
            info.Length is < 2 or > 1_000_000)
            throw new InvalidDataException("Invalid gMSA queue file.");
        var items = JsonSerializer.Deserialize<List<DeferredRepairItem>>(
            File.ReadAllText(PathName, Encoding.UTF8), Json)
            ?? throw new InvalidDataException("Invalid gMSA queue JSON.");
        if (items.Count > MaxEntries || items.Any(x => x is null ||
                !DeferredRepairQueue.IsValidForStorage(x) ||
                !(inspection ? config.MatchesForInspection(x) : config.Matches(x))) ||
            items.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            throw new InvalidDataException("gMSA queue contains invalid or out-of-scope entries.");
        return items;
    }

    private static void Write(GmsaRepairConfig config, List<DeferredRepairItem> items, string disposition = "")
    {
        if (items.Count > MaxEntries || items.Any(x => !DeferredRepairQueue.IsValidForStorage(x) ||
            !config.Matches(x)))
            throw new InvalidDataException("Cannot write unauthorized gMSA repair entries.");
        CheckDirectory(config);
        var previous = Read(config); // capture authorized queue state before atomic replace.
        string temp = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(items, Json), new UTF8Encoding(false));
            File.Move(temp, PathName, overwrite: true);
            RepairHistoryJournal.RecordQueueDiff(GmsaRepairConfig.QueueDirectory,
                "gMSA", previous, items, disposition);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static IReadOnlyList<DeferredRepairItem> All(GmsaRepairConfig config)
    {
        config.Validate(); // Disabled installations cannot use the operational queue API.
        lock (Gate) { using var lease = Lock(); return Read(config).ToArray(); }
    }

    /// <summary>
    /// A privileged, strictly read-only snapshot for the disabled gMSA GUI.
    /// The original approvals, queue states and timestamps are never changed;
    /// this method cannot start Repair, invoke a worker or re-enable a task.
    /// </summary>
    internal static IReadOnlyList<DeferredRepairItem> InspectReadOnly(GmsaRepairConfig config)
    {
        if (!IsAdministrator() && GmsaRepairConfig.TestRootOverride is null)
            throw new UnauthorizedAccessException(
                "Inspecting the protected gMSA queue requires a local administrator.");
        config.ValidateForInspection();
        lock (Gate) { using var lease = Lock(); return Read(config, inspection: true).ToArray(); }
    }

    internal static bool IsAdministrator() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

    private static void RequireAdmin(GmsaRepairConfig config)
    {
        if (!IsAdministrator() && GmsaRepairConfig.TestRootOverride is null)
            throw new UnauthorizedAccessException(
                "Configuring gMSA Repair requires an elevated local administrator on the worker host.");
        config.Validate();
    }

    /// <summary>
    /// Must be called AFTER explicit Preview and full GUID confirmation but
    /// BEFORE initial remote RPC. Atomically revoke earlier queued jobs so a
    /// scheduled gMSA worker cannot collide with a new foreground operation.
    /// </summary>
    internal static void ReserveApproved(GmsaRepairConfig config,
        string domain, string gpoId, string campaignId,
        IReadOnlyCollection<string> hosts, string ouDn, string groupDn)
    {
        RequireAdmin(config);
        if (!config.ScopeMatches(domain, ouDn, groupDn) ||
            !Guid.TryParse(gpoId, out Guid targetGuid) ||
            hosts.Count is < 1 or > 25)
            throw new UnauthorizedAccessException("New Repair does not match the configured gMSA scope.");
        var selected = new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase);
        if (selected.Count != hosts.Count || selected.Any(x => !FeedbackPath.IsValidHost(x.Split('.')[0])))
            throw new InvalidDataException("gMSA reservation has duplicate or invalid computers.");
        lock (Gate)
        {
            using var lease = Lock();
            var entries = Read(config);
            var matching = entries.Where(x =>
                x.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase) &&
                Guid.Parse(x.GpoId) == targetGuid && x.CampaignId == campaignId &&
                selected.Contains(x.ComputerFqdn)).ToArray();
            if (matching.Any(x => x.State is "Attempting" or "AwaitingHealth" or "AwaitingTask"))
                throw new InvalidOperationException(
                    "gMSA remote task is still running or unconfirmed; investigate before a new Repair.");
            foreach (var item in matching.Where(x => x.State == "Queued"))
            {
                item.State = "NeedsAttention";
                item.Reason = "Superseded by a newly approved administrator Repair.";
            }
            if (matching.Any(x => x.State == "NeedsAttention")) Write(config, entries);
        }
    }

    internal static (int Offline, int AwaitingHealth, int AwaitingTask, int NeedsAttention)
        EnqueueApprovedResults(GmsaRepairConfig config, string dc, string gpoId,
            string campaignId, IReadOnlyDictionary<string, string> statuses,
            DateTimeOffset submittedUtc, DateTimeOffset now)
    {
        RequireAdmin(config);
        if (!Guid.TryParse(gpoId, out var targetGuid) ||
            statuses.Count is < 1 or > 25 ||
            submittedUtc > now.AddMinutes(5) ||
            submittedUtc < now.AddHours(-1))
            throw new ArgumentException("Invalid gMSA initial repair results.");
        string approvingSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new UnauthorizedAccessException("Cannot determine approval SID.");
        int offline = 0, healthy = 0, pending = 0, errors = 0;
        lock (Gate)
        {
            using var lease = Lock();
            var items = Read(config);
            foreach (var (host, status) in statuses)
            {
                if (status is not ("offline" or "success" or "pending" or "failed") ||
                    !DeferredRepairQueue.IsValidForStorage(new DeferredRepairItem
                    {
                        Domain = config.Domain,
                        DomainController = dc,
                        OuDn = config.OuDn,
                        GroupDn = config.GroupDn,
                        GpoId = gpoId,
                        CampaignId = campaignId,
                        ComputerFqdn = host,
                        ApprovedUtc = submittedUtc,
                        BackgroundAllowed = true,
                        ApprovalSid = approvingSid,
                        NextAttemptUtc = now
                    }))
                    throw new InvalidDataException("Invalid host or remote status from approved Repair.");
                string state = status switch
                {
                    "offline" => "Queued",
                    "success" => "AwaitingHealth",
                    "pending" => "AwaitingTask",
                    _ => "NeedsAttention"
                };
                var item = new DeferredRepairItem
                {
                    Domain = config.Domain, DomainController = dc,
                    OuDn = config.OuDn, GroupDn = config.GroupDn,
                    GpoId = targetGuid.ToString("D"), CampaignId = campaignId,
                    ComputerFqdn = host,
                    ApprovedUtc = submittedUtc, NextAttemptUtc = now.AddMinutes(1),
                    SubmittedUtc = state is "AwaitingHealth" or "AwaitingTask" ? submittedUtc : null,
                    BackgroundAllowed = true, ApprovalSid = approvingSid, State = state,
                    Reason = state switch
                    {
                        "Queued" => "Approved remote Repair: RPC was offline before task registration.",
                        "AwaitingHealth" => "Remote task exited, awaiting healthy SMB feedback.",
                        "AwaitingTask" => "Remote RPC outcome unknown; never repeat automatically.",
                        _ => "Initial remote Repair failed; administrator review required."
                    }
                };
                if (!config.Matches(item)) throw new InvalidDataException("gMSA approval is out of scope.");
                items.RemoveAll(x => x.Domain.Equals(config.Domain, StringComparison.OrdinalIgnoreCase) &&
                    x.CampaignId == campaignId &&
                    Guid.Parse(x.GpoId) == targetGuid &&
                    x.ComputerFqdn.Equals(host, StringComparison.OrdinalIgnoreCase));
                items.Add(item);
                switch (state)
                {
                    case "Queued": offline++; break;
                    case "AwaitingHealth": healthy++; break;
                    case "AwaitingTask": pending++; break;
                    default: errors++; break;
                }
            }
            Write(config, items);
        }
        return (offline, healthy, pending, errors);
    }

    internal static bool ExpireQueued(GmsaRepairConfig config, string id, DateTimeOffset now)
    {
        lock (Gate)
        {
            using var lease = Lock();
            var items = Read(config);
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null || item.State != "Queued" ||
                item.Attempts < 20 && item.ApprovedUtc > now.AddDays(-14)) return false;
            item.State = "NeedsAttention";
            item.Reason = "Approved window or retry limit expired.";
            Write(config, items);
            return true;
        }
    }

    /// <summary>
    /// Call only while holding the single gMSA worker-process mutex, at the
    /// beginning of a NEW worker cycle. If a prior worker crashed or Windows
    /// rebooted during RPC, the remote task may already have been registered.
    /// Convert that in-flight state to health-only monitoring; NEVER retry RPC.
    /// Queued and already-submitted states retain their original approval.
    /// </summary>
    internal static int RecoverInterruptedAttempts(GmsaRepairConfig config, DateTimeOffset now)
    {
        lock (Gate)
        {
            using var lease = Lock();
            var items = Read(config);
            int recovered = 0;
            foreach (var item in items.Where(x => x.State == "Attempting"))
            {
                if (!config.Matches(item) || !item.SubmittedUtc.HasValue)
                    throw new InvalidDataException(
                        "Interrupted repair is missing an approved scope or submission timestamp.");
                item.State = "AwaitingTask";
                item.NextAttemptUtc = now;
                item.Reason =
                    "Previous worker stopped during RPC; remote outcome unknown. " +
                    "Wait for Client Health; never repeat without operator review.";
                recovered++;
            }
            if (recovered > 0)
                Write(config, items);
            return recovered;
        }
    }

    internal static bool BeginAttempt(GmsaRepairConfig config, string id, DateTimeOffset now)
    {
        lock (Gate)
        {
            using var lease = Lock();
            var items = Read(config);
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null || !item.Due(now) || !config.Matches(item)) return false;
            item.State = "Attempting";
            item.Attempts++;
            item.LastAttemptUtc = now;
            item.SubmittedUtc = now;
            item.NextAttemptUtc = now.AddMinutes(1);
            item.Reason = "gMSA RPC started; unknown outcomes never auto-retry.";
            Write(config, items); // PERSIST BEFORE NETWORK EXECUTION
            return true;
        }
    }

    internal static void FinishAttempt(GmsaRepairConfig config, string id, string result, DateTimeOffset now)
    {
        if (result is not ("success" or "offline" or "pending" or "failed"))
            throw new ArgumentException("Invalid gMSA remote result.");
        lock (Gate)
        {
            using var lease = Lock();
            var items = Read(config);
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null || item.State != "Attempting" || !item.SubmittedUtc.HasValue)
                return;
            switch (result)
            {
                case "offline":
                    item.SubmittedUtc = null;
                    item.State = "Queued";
                    item.NextAttemptUtc = now.AddMinutes(
                        Math.Min(30, 2 * Math.Pow(2, Math.Min(item.Attempts, 4))));
                    item.Reason = "RPC unavailable before task registration; scheduled safe retry.";
                    if (item.Attempts >= 20 || item.ApprovedUtc <= now.AddDays(-14))
                    {
                        item.State = "NeedsAttention";
                        item.Reason = "Approval expired or RPC limit reached.";
                    }
                    break;
                case "success":
                    item.State = "AwaitingHealth";
                    item.NextAttemptUtc = now.AddMinutes(1);
                    item.Reason = "Remote task exited with 0; client health confirmation pending.";
                    break;
                case "pending":
                    item.State = "AwaitingTask";
                    item.NextAttemptUtc = now.AddMinutes(1);
                    item.Reason = "Remote task outcome uncertain; no RPC repeat.";
                    break;
                default:
                    item.State = "NeedsAttention";
                    item.Reason = "Task failed or AD eligibility changed; manual review required.";
                    break;
            }
            Write(config, items);
        }
    }

    internal static bool CompleteHealth(GmsaRepairConfig config, string id,
        ClientFeedbackRecord record, DateTimeOffset now)
    {
        lock (Gate)
        {
            using var lease = Lock();
            var items = Read(config);
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null || item.State is not
                ("Queued" or "Attempting" or "AwaitingHealth" or "AwaitingTask"))
                return false;
            DateTimeOffset from = item.State == "Queued"
                ? item.ApprovedUtc : item.SubmittedUtc ?? DateTimeOffset.MaxValue;
            if (item.ApprovedUtc <= now.AddDays(-14) ||
                !DeferredRepairQueue.MatchesHealthy(item, record, from, now))
                return false;
            items.Remove(item);
            Write(config, items, "HealthyConfirmed");
            return true;
        }
    }

    internal static void SchedulePoll(GmsaRepairConfig config, string id, DateTimeOffset now)
    {
        lock (Gate)
        {
            using var lease = Lock();
            var items = Read(config);
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null || item.State is not ("Attempting" or "AwaitingHealth" or "AwaitingTask"))
                return;
            item.NextAttemptUtc = now.AddMinutes(1);
            Write(config, items);
        }
    }

    internal static void FlagForReview(GmsaRepairConfig config, string id, string reason, DateTimeOffset now)
    {
        lock (Gate)
        {
            using var lease = Lock();
            var items = Read(config);
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null || item.State is "NeedsAttention") return;
            item.State = "NeedsAttention";
            item.Reason = UniversalDiagnosticsEmbedded.Clean(reason, 180);
            item.NextAttemptUtc = now;
            Write(config, items);
        }
    }

    internal static void Cancel(GmsaRepairConfig config, string id)
    {
        RequireAdmin(config);
        lock (Gate)
        {
            using var lease = Lock();
            var items = Read(config);
            var item = items.SingleOrDefault(x => x.Id == id);
            if (item is null) return;
            // Cancelling tracking never kills a task already running on a PC.
            items.Remove(item);
            Write(config, items, "Cancelled");
        }
    }
}
