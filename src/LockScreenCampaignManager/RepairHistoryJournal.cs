using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Persisted, per-queue operational timeline. Historical entries are separate
/// from queue.json, so confirmation/cancellation does not erase the evidence.
/// This is a protected operational journal, not a cryptographic attestation.
/// No credentials, passwords, raw RPC responses or client-supplied JSON enter it.
/// </summary>
internal sealed class RepairHistoryEvent
{
    public string EventId { get; set; } = "";
    public DateTimeOffset RecordedUtc { get; set; }
    public string Source { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntryId { get; set; } = "";
    public string Domain { get; set; } = "";
    public string DomainController { get; set; } = "";
    public string OuDn { get; set; } = "";
    public string GroupDn { get; set; } = "";
    public string CampaignId { get; set; } = "";
    public string GpoId { get; set; } = "";
    public string ComputerFqdn { get; set; } = "";
    public string PreviousState { get; set; } = "";
    public string State { get; set; } = "";
    public int Attempts { get; set; }
    public string Reason { get; set; } = "";
}

internal static class RepairHistoryJournal
{
    private const long MaximumMonthlyBytes = 16 * 1024 * 1024;
    private const int MaximumLineChars = 8192;
    private const int MaximumFilesForReview = 48;
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);
    private static readonly Regex FileName = new(
        @"\Arepair-history-\d{4}-\d{2}\.jsonl\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static bool Same(DeferredRepairItem a, DeferredRepairItem b) =>
        a.State == b.State && a.Attempts == b.Attempts &&
        a.Reason == b.Reason && a.SubmittedUtc == b.SubmittedUtc &&
        a.ApprovedUtc == b.ApprovedUtc;

    private static string RemovedAction(string disposition,
        DeferredRepairItem oldItem, IReadOnlyCollection<DeferredRepairItem> incoming)
    {
        if (disposition == "HealthyConfirmed") return "HealthyConfirmed";
        if (disposition == "Cancelled") return "Cancelled";
        if (incoming.Any(x =>
            x.Domain.Equals(oldItem.Domain, StringComparison.OrdinalIgnoreCase) &&
            x.ComputerFqdn.Equals(oldItem.ComputerFqdn, StringComparison.OrdinalIgnoreCase) &&
            x.CampaignId == oldItem.CampaignId &&
            Guid.TryParse(x.GpoId, out Guid next) &&
            Guid.TryParse(oldItem.GpoId, out Guid prior) && next == prior))
            return "Superseded";
        return "TrackingRemoved"; // NEVER infer a successful repair from disappearance.
    }

    private static RepairHistoryEvent Event(DeferredRepairItem item, string source,
        string action, string previousState, DateTimeOffset now) => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        RecordedUtc = now,
        Source = source,
        Action = action,
        EntryId = item.Id,
        Domain = item.Domain,
        DomainController = item.DomainController,
        OuDn = item.OuDn,
        GroupDn = item.GroupDn,
        CampaignId = item.CampaignId,
        GpoId = item.GpoId,
        ComputerFqdn = item.ComputerFqdn,
        PreviousState = previousState,
        State = item.State,
        Attempts = item.Attempts,
        Reason = UniversalDiagnosticsEmbedded.Clean(item.Reason, 180)
    };

    private static void AssertFile(string directory, string path)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("Repair history directory is missing.");
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Repair history directory is a reparse point.");
        if (File.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Repair history file is a reparse point.");
    }

    /// <summary>
    /// Call under the SAME queue lock, AFTER the atomic queue.json replacement.
    /// If journaling fails the queue change is already authoritative: propagate
    /// the error, do not replay RPC. An interrupted write may cause a gap, which
    /// is why this journal is not represented as an atomic security audit.
    /// </summary>
    internal static void RecordQueueDiff(string directory, string source,
        IReadOnlyCollection<DeferredRepairItem> previous,
        IReadOnlyCollection<DeferredRepairItem> current,
        string disposition = "")
    {
        if (source is not ("User" or "gMSA") ||
            disposition is not ("" or "HealthyConfirmed" or "Cancelled"))
            throw new ArgumentException("Unrecognized repair journal source/disposition.");
        var oldById = previous.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var newById = current.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var events = new List<RepairHistoryEvent>();
        foreach (var item in current)
        {
            if (!oldById.TryGetValue(item.Id, out var former))
            {
                events.Add(Event(item, source, "Approved", "", now));
                continue;
            }
            if (Same(former, item)) continue; // Polling alone is not another RPC.
            string action = item.Attempts > former.Attempts && item.State == "Attempting"
                ? "RpcStarted" :
                former.State == "Attempting" && item.State == "AwaitingTask"
                    ? "RpcUncertain" :
                item.State == "NeedsAttention" ? "NeedsAttention" :
                item.State != former.State ? "StateChanged" : "DetailsUpdated";
            events.Add(Event(item, source, action, former.State, now));
        }
        foreach (var oldItem in previous.Where(x => !newById.ContainsKey(x.Id)))
            events.Add(Event(oldItem, source,
                RemovedAction(disposition, oldItem, current), oldItem.State, now));
        if (events.Count == 0) return;

        string file = Path.Combine(directory,
            "repair-history-" + now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture) +
            ".jsonl");
        AssertFile(directory, file);
        byte[] bytes = Encoding.UTF8.GetBytes(string.Concat(events.Select(e =>
            JsonSerializer.Serialize(e, Json) + "\n")));
        using var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.Read);
        if (stream.Length > MaximumMonthlyBytes - bytes.Length)
            throw new IOException("Monthly Repair history is full; manual archive required. Queue change is committed.");
        if (stream.Length > 0)
        {
            stream.Seek(-1, SeekOrigin.End);
            if (stream.ReadByte() != '\n')
                throw new InvalidDataException("Repair history ended with a partial line. Inspect it before any new RPC.");
        }
        stream.Seek(0, SeekOrigin.End);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static bool Valid(RepairHistoryEvent? e) =>
        e is not null &&
        e.EventId is not null && e.EntryId is not null &&
        e.Source is not null && e.Action is not null &&
        e.Domain is not null && e.DomainController is not null &&
        e.OuDn is not null && e.GroupDn is not null &&
        e.CampaignId is not null && e.GpoId is not null &&
        e.ComputerFqdn is not null && e.Reason is not null &&
        e.State is not null && e.PreviousState is not null &&
        e.EventId.Length == 32 && Guid.TryParseExact(e.EventId, "N", out _) &&
        e.RecordedUtc.Year >= 2025 &&
        e.RecordedUtc <= DateTimeOffset.UtcNow.AddMinutes(5) &&
        e.Source is "User" or "gMSA" &&
        e.Action is "Approved" or "RpcStarted" or "RpcUncertain" or
            "NeedsAttention" or "StateChanged" or "DetailsUpdated" or
            "HealthyConfirmed" or "Cancelled" or "Superseded" or "TrackingRemoved" &&
        e.EntryId.Length == 32 && Guid.TryParseExact(e.EntryId, "N", out _) &&
        e.Domain.Length is >= 3 and <= 253 && e.DomainController.Length <= 253 &&
        e.OuDn.Length is > 3 and <= 1024 &&
        e.GroupDn.Length is > 3 and <= 1024 &&
        e.CampaignId.Length is >= 1 and <= 72 &&
        Guid.TryParse(e.GpoId, out _) &&
        FeedbackPath.IsValidHost(e.ComputerFqdn.Split('.')[0]) &&
        e.ComputerFqdn.Length <= 253 &&
        e.Attempts is >= 0 and <= 30 &&
        e.PreviousState.Length <= 40 && e.State.Length <= 40 &&
        e.Reason.Length <= 200;

    /// <summary>
    /// GUI-only read. Validates complete files, even when a requested device
    /// has no entries, so corrupt/partial logs cannot be mistaken for empty.
    /// The last 120 monthly journals remain available; the display retains
    /// the latest maxEvents entries across those files without modifying them.
    /// </summary>
    internal static IReadOnlyList<RepairHistoryEvent> ReadScope(string directory,
        string source, string domain, string dc, string ouDn, string groupDn,
        string campaignId, string gpoId, int maxEvents = 400)
    {
        if (source is not ("User" or "gMSA") || maxEvents is < 1 or > 2000 ||
            !Guid.TryParse(gpoId, out var targetGpo))
            throw new ArgumentException("Invalid repair history query.");
        if (!Directory.Exists(directory)) return [];
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Repair journal directory is a reparse point.");
        var files = Directory.EnumerateFiles(directory, "repair-history-*.jsonl")
            .Where(x => FileName.IsMatch(Path.GetFileName(x)))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        if (files.Length > MaximumFilesForReview)
            throw new InvalidDataException("Repair journal contains too many monthly files for bounded inspection.");
        var latest = new Queue<RepairHistoryEvent>();
        foreach (string path in files)
        {
            AssertFile(directory, path);
            if (new FileInfo(path).Length > MaximumMonthlyBytes)
                throw new InvalidDataException("Repair history monthly file is oversized.");
            // Readers must share the file with the background writer; otherwise
            // opening the Dashboard could prevent the worker from committing a
            // durable transition. Reject truncated JSONL instead of guessing.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite);
            if (stream.Length == 0)
                throw new InvalidDataException("Repair history is unexpectedly empty.");
            stream.Seek(-1, SeekOrigin.End);
            if (stream.ReadByte() != '\n')
                throw new InvalidDataException("Repair history ends with an incomplete record.");
            stream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: false);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length is < 2 or > MaximumLineChars)
                    throw new InvalidDataException("Repair history line has invalid length.");
                RepairHistoryEvent? evt;
                try { evt = JsonSerializer.Deserialize<RepairHistoryEvent>(line, Json); }
                catch (JsonException ex)
                { throw new InvalidDataException("Malformed Repair history. Investigate incomplete writes.", ex); }
                if (!Valid(evt) || evt!.Source != source)
                    throw new InvalidDataException("Repair history entry failed validation.");
                if (!evt.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                    !evt.DomainController.Equals(dc, StringComparison.OrdinalIgnoreCase) ||
                    !evt.OuDn.Equals(ouDn, StringComparison.OrdinalIgnoreCase) ||
                    !evt.GroupDn.Equals(groupDn, StringComparison.OrdinalIgnoreCase) ||
                    evt.CampaignId != campaignId ||
                    !Guid.TryParse(evt.GpoId, out var currentGpo) || currentGpo != targetGpo)
                    continue;
                latest.Enqueue(evt);
                if (latest.Count > maxEvents) latest.Dequeue();
            }
        }
        return latest.OrderByDescending(x => x.RecordedUtc).ToArray();
    }
}
