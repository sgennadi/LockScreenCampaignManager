using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only local analysis of explicit UniversalDiagnostics v1 captures.
/// No arbitrary paths are taken from JSON, no source content is executed,
/// no network calls and no automatic GitHub submission.
/// </summary>
internal sealed record DiagnosticsTrend(
    string Fingerprint, string Component, string ErrorType,
    string Code, string Level, string LastVersion, int Count,
    DateTimeOffset FirstUtc, DateTimeOffset LastUtc);

internal sealed class DiagnosticsTrendSnapshot
{
    public required DateTimeOffset GeneratedUtc { get; init; }
    public required IReadOnlyList<DiagnosticsTrend> Groups { get; init; }
    public int ValidReports { get; init; }
    public int InvalidReports { get; init; }

    // Deliberately omit summary text, raw file paths, report UUIDs and host
    // identities from the exported aggregate even if an old saved record
    // passed all stored-format checks.
    internal string ExportSafeJson() => JsonSerializer.Serialize(new {
        schema = "lscm-diagnostics-trends/v1",
        generated_utc = GeneratedUtc,
        project = UniversalDiagnosticsEmbedded.Project,
        valid_reports = ValidReports,
        rejected_reports = InvalidReports,
        groups = Groups.Select(g => new {
            fingerprint = g.Fingerprint, component = g.Component,
            error_type = g.ErrorType, code = g.Code, level = g.Level,
            last_version = g.LastVersion, count = g.Count,
            first_utc = g.FirstUtc, last_utc = g.LastUtc
        }).ToArray()
    }, new JsonSerializerOptions { WriteIndented = true });
}

internal static class DiagnosticsTrendAnalyzer
{
    private sealed record AcceptedRecord(
        string Fingerprint, string Component, string ErrorType, string Code,
        string Level, string Version, DateTimeOffset TimestampUtc);

    internal static DiagnosticsTrendSnapshot Analyze(
        IEnumerable<string> paths, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var all = paths.Take(251).ToArray();
        if (all.Length > 250)
            throw new InvalidDataException("Too many diagnostic reports requested.");
        var accepted = new List<AcceptedRecord>(all.Length);
        int rejected = 0;
        foreach (var path in all)
        {
            try
            {
                var result = ReadValidated(path, nowUtc);
                if (result is null) rejected++;
                else accepted.Add(result);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or
                UnauthorizedAccessException or JsonException or ArgumentException or
                InvalidOperationException or NotSupportedException or FormatException)
            {
                // An invalid/truncated file is NOT evidence of a real app error.
                // Never log raw untrusted contents or personally identifying paths.
                rejected++;
            }
        }
        var groups = accepted.GroupBy(x => x.Fingerprint, StringComparer.Ordinal)
            .Select(g =>
            {
                var latest = g.OrderByDescending(x => x.TimestampUtc).First();
                return new DiagnosticsTrend(
                    g.Key, latest.Component, latest.ErrorType,
                    latest.Code, latest.Level, latest.Version, g.Count(),
                    g.Min(x => x.TimestampUtc), g.Max(x => x.TimestampUtc));
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.LastUtc)
            .ThenBy(x => x.Fingerprint, StringComparer.Ordinal)
            .ToArray();
        return new DiagnosticsTrendSnapshot {
            GeneratedUtc = nowUtc, Groups = groups,
            ValidReports = accepted.Count, InvalidReports = rejected
        };
    }

    private static AcceptedRecord? ReadValidated(string path, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        var info = new FileInfo(path);
        if (info.Length is < 50 or > 65536 ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
            return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path),
            new JsonDocumentOptions { MaxDepth = 8 });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            root.EnumerateObject().Count() != 2)
            return null;
        if (!root.TryGetProperty("event", out var ev) ||
            ev.ValueKind != JsonValueKind.Object ||
            ev.EnumerateObject().Count() != 13 ||
            !root.TryGetProperty("delivery", out var delivery) ||
            delivery.ValueKind != JsonValueKind.Object ||
            delivery.EnumerateObject().Count() != 2)
            return null;
        string Read(JsonElement item, string key, int max, bool redact = true)
        {
            if (!item.TryGetProperty(key, out var field) ||
                field.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Invalid diagnostic metadata.");
            string value = field.GetString() ?? "";
            if (value.Length < 1 || value.Length > max ||
                redact && UniversalDiagnosticsEmbedded.Clean(value, max) != value)
                throw new InvalidDataException("Unredacted diagnostic metadata.");
            return value;
        }
        if (Read(delivery, "status", 30) != "pending" ||
            Read(ev, "schema", 60) != UniversalDiagnosticsEmbedded.Schema ||
            Read(ev, "project", 100) != UniversalDiagnosticsEmbedded.Project)
            return null;
        // An empty URL is the canonical pending delivery field.
        if (!delivery.TryGetProperty("url", out var url) ||
            url.ValueKind != JsonValueKind.String || url.GetString() != "")
            return null;
        // UUID is a structural identity, deliberately redacted by Clean()
        // when found inside free text. Validate its grammar separately.
        string uuid = Read(ev, "id", 40, redact: false);
        if (!Guid.TryParseExact(uuid, "D", out var parsedId) ||
            !string.Equals(Path.GetFileNameWithoutExtension(path),
                parsedId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            return null;
        string stamp = Read(ev, "timestamp_utc", 40);
        if (!DateTimeOffset.TryParse(stamp, out var time) ||
            time < new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero) ||
            time > now.AddMinutes(5))
            return null;

        string component = Read(ev, "component", 90);
        string errorType = Read(ev, "error_type", 90);
        string code = Read(ev, "code", 60);
        string level = Read(ev, "level", 10);
        string version = Read(ev, "version", 70);
        string platform = Read(ev, "platform", 20);
        string architecture = Read(ev, "architecture", 20);
        _ = Read(ev, "summary", 450); // Validate, never include in exported aggregate.
        string fingerprint = Read(ev, "fingerprint", 64);
        if (level is not ("error" or "warning" or "info") ||
            platform is not ("Windows" or "Other") ||
            architecture is not ("AMD64" or "x86" or "ARM64" or "Other") ||
            !UniversalUpdater.SemanticVersion.TryParse(version, out _) ||
            fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit))
            return null;
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("|", UniversalDiagnosticsEmbedded.Project.ToLowerInvariant(),
                component.ToLowerInvariant(), errorType.ToLowerInvariant(),
                code.ToLowerInvariant()))));
        if (!string.Equals(fingerprint, digest, StringComparison.Ordinal))
            return null;
        return new AcceptedRecord(fingerprint, component, errorType, code,
            level, version, time);
    }
}
