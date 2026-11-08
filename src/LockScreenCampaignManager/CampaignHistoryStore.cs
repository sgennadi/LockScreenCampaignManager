using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Local, explicitly controlled history. Never sends data to GitHub, SYSVOL or SMB.
/// The operator can export a portable report and manually remove retained records.
/// </summary>
internal sealed class CampaignHistoryEntry
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CampaignId { get; set; } = "";
    public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Kind { get; set; } = "Event"; // Event | Snapshot | Final
    public string Summary { get; set; } = "";
    public string Domain { get; set; } = "";
    public string OuDn { get; set; } = "";
    public string GroupDn { get; set; } = "";
    public int Expected { get; set; }
    public int Healthy { get; set; }
    public int Ready { get; set; }
    public int Errors { get; set; }
    public int Stale { get; set; }
    public int NoReport { get; set; }
    public int Completed { get; set; }
    public int InvalidReports { get; set; }
    public List<ClientHealthRow> Rows { get; set; } = new();
    [JsonIgnore]
    public string SourceFile { get; set; } = ""; // process-only; never serialized
}

internal static class CampaignHistoryStore
{
    internal static string? TestRootOverride;
    internal static string Root => TestRootOverride ?? Path.Combine(AppLog.DirectoryPath, "History");
    private static readonly Regex CampaignIdPattern = new(
        @"^[a-z0-9][a-z0-9_-]{0,70}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private const long MaxReadBytes = 4 * 1024 * 1024;

    public static CampaignHistoryEntry SaveEvent(string campaignId, string kind, string summary)
    {
        if (!CampaignIdPattern.IsMatch(campaignId))
            throw new ArgumentException("Invalid campaign ID", nameof(campaignId));
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > 70)
            throw new ArgumentException("Invalid event name", nameof(kind));
        var e = new CampaignHistoryEntry { CampaignId = campaignId, Kind = "Event",
            Summary = kind + ": " + (summary.Length > 200 ? summary[..200] : summary) };
        Persist(e);
        return e;
    }

    public static void TryRecordEvent(string campaignId, string kind, string summary)
    {
        try { SaveEvent(campaignId, kind, summary); }
        catch (Exception ex) { AppLog.Write(ex, "Campaign history event"); }
    }

    public static CampaignHistoryEntry SaveHealth(ClientHealthSnapshot snap,
        string domain, string ouDn, string groupDn, bool final)
    {
        if (!CampaignIdPattern.IsMatch(snap.CampaignId))
            throw new InvalidDataException("Invalid CampaignID in feedback snapshot.");
        if (snap.Rows.Count > 5000) throw new InvalidDataException("Too many computers in snapshot.");
        var e = new CampaignHistoryEntry
        {
            CampaignId = snap.CampaignId, Kind = final ? "Final" : "Snapshot",
            Summary = final ? "Completion report (snapshot at export time)" : "Computer health snapshot",
            Domain = Limit(domain, 255), OuDn = Limit(ouDn, 1024), GroupDn = Limit(groupDn, 1024),
            OccurredUtc = snap.ObservedUtc,
            Expected = snap.Expected, Healthy = snap.Healthy, Ready = snap.Ready,
            Errors = snap.Errors, Stale = snap.Stale, NoReport = snap.NoReport,
            Completed = snap.Completed, InvalidReports = snap.InvalidReports,
            Rows = snap.Rows.ToList()
        };
        Persist(e);
        return e;
    }

    private static string Limit(string value, int max) => value.Length > max ? value[..max] : value;

    private static void Persist(CampaignHistoryEntry entry)
    {
        if (!CampaignIdPattern.IsMatch(entry.CampaignId))
            throw new InvalidDataException("Invalid CampaignID.");
        string directory = Path.Combine(Root, entry.CampaignId);
        Directory.CreateDirectory(directory);
        string name = entry.OccurredUtc.UtcDateTime.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture)
            + "-" + entry.Id + ".json";
        string target = Path.Combine(directory, name);
        string temporary = Path.Combine(directory, "." + entry.Id + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(entry, Serializer), new UTF8Encoding(false));
            File.Move(temporary, target);
            entry.SourceFile = target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static List<CampaignHistoryEntry> LoadRecent(int max = 300)
    {
        if (!Directory.Exists(Root)) return new();
        max = Math.Clamp(max, 1, 500);
        var paths = Directory.EnumerateFiles(Root, "*.json", SearchOption.AllDirectories)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Take(max + 1).ToList();
        if (paths.Count > max) paths.RemoveAt(paths.Count - 1);
        var entries = new List<CampaignHistoryEntry>();
        foreach (string path in paths)
        {
            try
            {
                if (new FileInfo(path).Length is < 2 or > MaxReadBytes) continue;
                var item = JsonSerializer.Deserialize<CampaignHistoryEntry>(File.ReadAllText(path), Serializer);
                if (item is null || item.SchemaVersion != 1 ||
                    !CampaignIdPattern.IsMatch(item.CampaignId) ||
                    item.Kind is not ("Event" or "Snapshot" or "Final") ||
                    item.Rows.Count > 5000 || item.Summary.Length > 1000)
                    continue;
                item.SourceFile = path;
                entries.Add(item);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                ArgumentException or NullReferenceException) { AppLog.Write(ex, "History read"); }
        }
        return entries.OrderByDescending(x => x.OccurredUtc).ToList();
    }

    public static void Delete(CampaignHistoryEntry entry)
    {
        string full = Path.GetFullPath(entry.SourceFile);
        string basePath = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) ||
            !full.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("History path outside the local archive.");
        File.Delete(full);
    }

    private static string Escape(string? value) => WebUtility.HtmlEncode(value ?? "");

    public static string HtmlReport(CampaignHistoryEntry entry)
    {
        string ratio = entry.Expected > 0
            ? (100d * (entry.Healthy + entry.Completed) / entry.Expected)
                .ToString("0.0", CultureInfo.InvariantCulture) + "%"
            : "N/A (AD scope not loaded)";
        var html = new StringBuilder("<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\">\n");
        html.Append("<title>LockScreen campaign report</title><style>")
            .Append("body{font-family:Segoe UI,Arial,sans-serif;color:#263447;margin:30px}")
            .Append("h1{font-size:25px}h2{font-size:18px}.note{color:#566779}")
            .Append(".cards{display:flex;flex-wrap:wrap;gap:12px;margin:18px 0}.card{padding:12px 18px;background:#f1f5fa;border-radius:8px}")
            .Append(".card strong{display:block;font-size:22px}table{border-collapse:collapse;width:100%;font-size:12px}")
            .Append("th,td{border-bottom:1px solid #dce4ec;text-align:left;padding:8px}th{background:#edf2f8}")
            .Append("@media print{@page{size:A4 landscape;margin:10mm}body{margin:0}.card{break-inside:avoid}}")
            .Append("</style></head><body>");
        html.Append("<h1>LockScreen Campaign Manager — report</h1>")
            .Append("<p><b>Campaign ID:</b> ").Append(Escape(entry.CampaignId)).Append("</p>")
            .Append("<p><b>Type:</b> ").Append(Escape(entry.Kind)).Append(" · <b>Date:</b> ")
            .Append(Escape(entry.OccurredUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss zzz", CultureInfo.CurrentCulture)))
            .Append("</p><p><b>Domain:</b> ").Append(Escape(entry.Domain)).Append("<br><b>OU:</b> ")
            .Append(Escape(entry.OuDn)).Append("<br><b>Group:</b> ").Append(Escape(entry.GroupDn)).Append("</p>")
            .Append("<p>").Append(Escape(entry.Summary)).Append("</p>");
        html.Append("<div class=\"cards\">");
        foreach (var metric in new[] {
            ("Target PCs",entry.Expected),("Healthy",entry.Healthy),("Ready",entry.Ready),
            ("Errors",entry.Errors),("Stale",entry.Stale),("No report",entry.NoReport),
            ("Completed",entry.Completed),("Invalid reports",entry.InvalidReports)})
        {
            html.Append("<div class=\"card\"><strong>").Append(metric.Item2)
                .Append("</strong>").Append(Escape(metric.Item1)).Append("</div>");
        }
        html.Append("</div><p><b>Healthy or completed / expected:</b> ").Append(Escape(ratio)).Append("</p>");
        html.Append("<p class=\"note\">This summary is based on the latest client SMB reports. ")
            .Append("It does not prove that an image was visible. A missing report does not mean the computer failed. ")
            .Append("Final status reflects manual archiving, not proof of complete rollout.</p>");
        html.Append("<h2>Computers</h2><table><thead><tr>");
        foreach (string head in new[] { "Computer", "Status", "Stage", "Image", "Last Seen", "Files", "Registry", "Task", "Agent" })
            html.Append("<th>").Append(Escape(head)).Append("</th>");
        html.Append("</tr></thead><tbody>");
        foreach (var row in entry.Rows)
        {
            html.Append("<tr>");
            foreach (string value in new[] {row.Computer,row.Condition,row.Stage,row.Image,row.LastSeen,
                row.Files,row.Registry,row.Task,row.Version})
                html.Append("<td>").Append(Escape(value)).Append("</td>");
            html.Append("</tr>");
        }
        html.Append("</tbody></table></body></html>");
        return html.ToString();
    }

    private static string CsvCell(string? input)
    {
        string value = input ?? "";
        if (value.Length > 0 && "=+-@\t\r\n".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static string CsvReport(CampaignHistoryEntry entry)
    {
        var text = new StringBuilder("Computer,Condition,Stage,Image,LastSeen,Files,Registry,Task,Agent\r\n");
        foreach (var row in entry.Rows)
            text.AppendLine(string.Join(',', new[] { row.Computer,row.Condition,row.Stage,row.Image,
                row.LastSeen,row.Files,row.Registry,row.Task,row.Version }.Select(CsvCell)));
        return text.ToString();
    }
}
