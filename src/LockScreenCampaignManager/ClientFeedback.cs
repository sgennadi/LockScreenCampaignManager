using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// SMB status inbox. The share is configured by the administrator and must never be SYSVOL.
/// Files are *untrusted hints* supplied by clients, not authenticated remote attestation.
/// </summary>
public static class FeedbackPath
{
    private static readonly Regex RootPattern = new(
        @"^\\\\(?<server>[A-Za-z0-9][A-Za-z0-9.-]{0,252})\\(?<share>[A-Za-z0-9_$.-]{1,80})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CampaignPattern = new(
        @"^[a-z0-9][a-z0-9_-]{0,70}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HostPattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9-]{0,62}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ValidateRoot(string? value)
    {
        var root = (value ?? string.Empty).Trim().TrimEnd('\\');
        var match = RootPattern.Match(root);
        if (!match.Success || root.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Specify a UNC SMB share: \\\\server\\LockScreenReports$ (no subfolders or trailing slash).");
        var share = match.Groups["share"].Value;
        var forbidden = new[] { "SYSVOL", "NETLOGON", "IPC$", "ADMIN$", "C$", "D$", "E$" };
        if (forbidden.Contains(share, StringComparer.OrdinalIgnoreCase) ||
            Regex.IsMatch(share, @"^[A-Za-z]\$$", RegexOptions.CultureInvariant))
            throw new ArgumentException("SYSVOL, NETLOGON and administrative shares cannot be used for feedback.");
        return root;
    }

    public static string CampaignDirectory(string shareRoot, string campaignId)
    {
        if (!CampaignPattern.IsMatch(campaignId))
            throw new ArgumentException("Invalid CampaignID.");
        return Path.Combine(ValidateRoot(shareRoot), campaignId);
    }

    public static bool IsValidHost(string? name) =>
        name is not null && HostPattern.IsMatch(name);
}

public sealed record ClientFeedbackRecord(
    string Computer, string CampaignId, DateTimeOffset LastSeenUtc,
    string Stage, string Health, string ExpectedImage, bool ImageMatches,
    bool RegistryMatches, bool TaskPresent, int MissingFiles, int CorruptFiles,
    string AgentVersion);

public sealed record ClientHealthRow(
    string Computer, string Condition, string Stage, string Image,
    string LastSeen, string Files, string Registry, string Task,
    string Version);

public sealed class ClientHealthSnapshot
{
    public required IReadOnlyList<ClientHealthRow> Rows { get; init; }
    public required string CampaignId { get; init; }
    public required string SharePath { get; init; }
    public int Expected { get; init; }
    public int Healthy { get; init; }
    public int Ready { get; init; }
    public int Errors { get; init; }
    public int Stale { get; init; }
    public int NoReport { get; init; }
    public int Completed { get; init; }
    public int InvalidReports { get; init; }
    public DateTimeOffset ObservedUtc { get; init; }
}

public static class ClientFeedbackReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };
    private const int MaximumReports = 5000;
    private const int MaximumFileBytes = 16 * 1024;

    public static ClientHealthSnapshot Read(string share, string campaignId,
        IEnumerable<string> expectedComputers, DateTimeOffset nowUtc, int staleAfterMinutes = 45)
    {
        if (staleAfterMinutes is < 5 or > 180)
            throw new ArgumentOutOfRangeException(nameof(staleAfterMinutes));
        string directory = FeedbackPath.CampaignDirectory(share, campaignId);
        return ReadDirectory(directory, campaignId, expectedComputers, nowUtc, staleAfterMinutes);
    }

    /// <summary>
    /// Read ONE expected client report for confirmation of a submitted repair.
    /// SMB reports are untrusted telemetry, never authorization to execute RPC.
    /// Missing reports return null; inaccessible shares throw and leave work pending.
    /// </summary>
    public static ClientFeedbackRecord? ReadSingle(string share, string campaignId,
        string shortComputer, DateTimeOffset nowUtc)
    {
        string folder = FeedbackPath.CampaignDirectory(share, campaignId);
        return ReadSingleDirectory(folder, campaignId, shortComputer, nowUtc);
    }

    internal static ClientFeedbackRecord? ReadSingleDirectory(string directory,
        string campaignId, string shortComputer, DateTimeOffset nowUtc)
    {
        if (!FeedbackPath.IsValidHost(shortComputer) ||
            !Regex.IsMatch(campaignId, @"^[a-z0-9][a-z0-9_-]{0,70}$",
                RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid feedback report identity.");
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("SMB feedback campaign directory unavailable.");
        string host = shortComputer.ToUpperInvariant();
        string file = Path.Combine(directory, host + ".json");
        if (!File.Exists(file)) return null;
        var info = new FileInfo(file);
        if (info.Length is < 8 or > MaximumFileBytes)
            throw new InvalidDataException("Invalid client feedback report size.");
        ClientFeedbackRecordJson? record;
        try { record = JsonSerializer.Deserialize<ClientFeedbackRecordJson>(File.ReadAllText(file), JsonOptions); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid client feedback JSON.", ex); }
        if (record is null || record.SchemaVersion != 1 ||
            !string.Equals(record.CampaignId, campaignId, StringComparison.Ordinal) ||
            !string.Equals(record.ComputerName, host, StringComparison.OrdinalIgnoreCase) ||
            record.TimestampUtc.Year < 2020 ||
            record.TimestampUtc > nowUtc.AddMinutes(5) ||
            record.Stage is null || record.Health is null ||
            record.AgentVersion is null || record.ExpectedImage is null ||
            !new[] { "Pending", "Active", "Completed", "Error" }.Contains(record.Stage, StringComparer.Ordinal) ||
            !new[] { "Healthy", "RepairNeeded", "Error", "Completed", "Pending" }.Contains(record.Health, StringComparer.Ordinal) ||
            record.MissingFiles is < 0 or > 30 || record.CorruptFiles is < 0 or > 30 ||
            record.AgentVersion.Length > 50 || record.ExpectedImage.Length > 40)
            throw new InvalidDataException("Client feedback fields failed validation.");
        return new ClientFeedbackRecord(
            host, campaignId, record.TimestampUtc, record.Stage, record.Health,
            record.ExpectedImage, record.ImageMatches, record.RegistryMatches,
            record.TaskPresent, record.MissingFiles, record.CorruptFiles, record.AgentVersion);
    }

    // Internal entrypoint used by isolated tests; network-path validation stays in public Read.
    internal static ClientHealthSnapshot ReadDirectory(string directory, string campaignId,
        IEnumerable<string> expectedComputers, DateTimeOffset nowUtc, int staleAfterMinutes = 45)
    {
        // Missing share is a setup error, not proof that every machine is offline.
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("Feedback directory unavailable: " + directory);

        var reports = new Dictionary<string, ClientFeedbackRecord>(StringComparer.OrdinalIgnoreCase);
        int invalid = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .Take(MaximumReports + 1))
        {
            if (reports.Count + invalid >= MaximumReports)
                throw new InvalidDataException("Too many feedback reports in one campaign (maximum 5000).");
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (!FeedbackPath.IsValidHost(fileName)) { invalid++; continue; }
                var info = new FileInfo(file);
                if (info.Length is < 8 or > MaximumFileBytes) { invalid++; continue; }
                var record = JsonSerializer.Deserialize<ClientFeedbackRecordJson>(File.ReadAllText(file), JsonOptions);
                if (record is null || record.SchemaVersion != 1 ||
                    !string.Equals(record.CampaignId, campaignId, StringComparison.Ordinal) ||
                    !string.Equals(record.ComputerName, fileName, StringComparison.OrdinalIgnoreCase) ||
                    !FeedbackPath.IsValidHost(record.ComputerName) ||
                    record.TimestampUtc > nowUtc.AddMinutes(5) ||
                    record.TimestampUtc.Year < 2020 ||
                    record.Stage is null || record.Health is null ||
                    record.AgentVersion is null || record.ExpectedImage is null ||
                    !new[] { "Pending", "Active", "Completed", "Error" }.Contains(record.Stage, StringComparer.Ordinal) ||
                    !new[] { "Healthy", "RepairNeeded", "Error", "Completed", "Pending" }.Contains(record.Health, StringComparer.Ordinal) ||
                    record.MissingFiles is < 0 or > 30 || record.CorruptFiles is < 0 or > 30 ||
                    record.AgentVersion.Length > 50 || record.ExpectedImage.Length > 40)
                { invalid++; continue; }
                if (reports.TryGetValue(fileName, out var prior) && prior.LastSeenUtc >= record.TimestampUtc)
                    continue;
                reports[fileName] = new ClientFeedbackRecord(
                    fileName.ToUpperInvariant(), campaignId, record.TimestampUtc,
                    record.Stage, record.Health, record.ExpectedImage,
                    record.ImageMatches, record.RegistryMatches, record.TaskPresent,
                    record.MissingFiles, record.CorruptFiles, record.AgentVersion);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                ArgumentException or FormatException or NullReferenceException)
            {
                invalid++;
            }
        }
        var expected = expectedComputers.Where(FeedbackPath.IsValidHost)
            .Select(x => x.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        // If AD discovery was not performed, display reports but do not claim a fleet denominator.
        var names = expected.Count > 0 ? expected : reports.Keys.OrderBy(x => x).ToList();
        var rows = new List<ClientHealthRow>();
        int healthy = 0, ready = 0, stale = 0, missing = 0, errors = 0, completed = 0;
        foreach (var name in names)
        {
            if (!reports.TryGetValue(name, out var record))
            {
                missing++;
                rows.Add(new ClientHealthRow(name, "No report", "Unknown", "—", "—", "—", "—", "—", "—"));
                continue;
            }
            var age = nowUtc - record.LastSeenUtc;
            string condition;
            // Completed is terminal; a successful final report stays completed after its heartbeat ages.
            if (record.Stage == "Completed") { completed++; condition = "Completed"; }
            else if (age > TimeSpan.FromMinutes(staleAfterMinutes)) { stale++; condition = "Stale / offline"; }
            else if (record.Health != "Healthy" || record.MissingFiles != 0 || record.CorruptFiles != 0 ||
                     !record.ImageMatches || !record.TaskPresent ||
                     (record.Stage == "Active" && !record.RegistryMatches))
            { errors++; condition = "Needs attention"; }
            else if (record.Stage == "Pending") { ready++; condition = "Ready / pending"; }
            else if (record.Stage == "Active") { healthy++; condition = "Healthy"; }
            else { errors++; condition = "Error"; }
            rows.Add(new ClientHealthRow(name, condition, record.Stage,
                record.ExpectedImage, record.LastSeenUtc.LocalDateTime.ToString("dd.MM HH:mm:ss", CultureInfo.CurrentCulture),
                record.MissingFiles == 0 && record.CorruptFiles == 0 ? "OK" : $"Missing {record.MissingFiles}, Corrupt {record.CorruptFiles}",
                record.RegistryMatches ? "OK" : "Not verified", record.TaskPresent ? "Yes" : "No",
                record.AgentVersion));
        }
        return new ClientHealthSnapshot
        {
            Rows = rows, CampaignId = campaignId, SharePath = directory,
            Expected = expected.Count, Healthy = healthy, Ready = ready, Stale = stale,
            NoReport = missing, Errors = errors, Completed = completed, InvalidReports = invalid,
            ObservedUtc = nowUtc
        };
    }

    internal sealed class ClientFeedbackRecordJson
    {
        public ClientFeedbackRecordJson() { }
        public int SchemaVersion { get; set; }
        public string ComputerName { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public DateTimeOffset TimestampUtc { get; set; }
        public string Stage { get; set; } = "";
        public string Health { get; set; } = "";
        public string ExpectedImage { get; set; } = "";
        public bool ImageMatches { get; set; }
        public bool RegistryMatches { get; set; }
        public bool TaskPresent { get; set; }
        public int MissingFiles { get; set; }
        public int CorruptFiles { get; set; }
        public string AgentVersion { get; set; } = "";
    }
}
