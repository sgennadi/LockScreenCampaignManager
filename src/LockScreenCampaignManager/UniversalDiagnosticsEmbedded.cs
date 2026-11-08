using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Embedded C# port of sgennadi/UniversalDiagnocticsModule contract v1.
/// No network, GitHub token, background upload or automatic report capture.
/// </summary>
internal static class UniversalDiagnosticsEmbedded
{
    public const string Schema = "universal-diagnostics/v1";
    public const string Project = "sgennadi/LockScreenCampaignManager";
    internal sealed record Receipt(string Id, string Path);
    private static readonly (string Pattern, string Replacement)[] Patterns = {
        (@"(?i)\b\d{6}(?:-\d{6}){7}\b", "[BITLOCKER-KEY]"),
        (@"(?i)\b(?:github_pat_[A-Za-z0-9_]{15,}|gh[pousr]_[A-Za-z0-9_]{15,})\b", "[TOKEN]"),
        ("(?i)https?://[^\\s<>\"']+", "[URL]"),
        ("(?i)\\\\\\\\[^\\s<>\"']+", "[NETWORK-PATH]"),
        ("(?i)(?<![A-Za-z0-9])[A-Z]:\\\\[^\\s\"']+", "[WINDOWS-PATH]"),
        (@"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", "[EMAIL]"),
        (@"(?i)\bS-1-\d+(?:-\d+){2,}\b", "[SID]"),
        (@"(?i)\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b", "[GUID]"),
        (@"(?i)\b(?:\d{1,3}\.){3}\d{1,3}\b", "[IP]"),
        (@"(?i)\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b", "[MAC]"),
        (@"(?i)\b[A-Za-z0-9_.-]{2,70}\\[A-Za-z0-9_.-]{2,70}\b", "[DOMAIN-ACCOUNT]"),
        (@"(?i)\b(?:CN|OU|DC)=[^\s,;]+(?:,(?:CN|OU|DC)=[^\s,;]+)*", "[LDAP-DN]"),
        (@"(?i)\b(?:[A-Z0-9-]+\.)+[A-Z]{2,}\b", "[DNS-NAME]")
    };
    public static string Clean(string? input, int limit = 450)
    {
        string text = (input ?? "").Replace('\0', ' ');
        if (text.Length > limit * 3) text = text[..(limit * 3)];
        text = Regex.Replace(text, @"(?im)^.*\b(?:password|passwd|passphrase|pwd|recovery[\s_-]*key|bitlocker|laps|access[\s_-]*token|refresh[\s_-]*token|authorization|bearer|secret|api[\s_-]*key|private[\s_-]*key|github_pat_|gh[pousr]_)\b.*$", "[REDACTED: confidential line]");
        foreach (var (pattern, substitute) in Patterns)
            text = Regex.Replace(text, pattern, substitute, RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"[\r\n\t]+", " ").Trim();
        return string.IsNullOrEmpty(text) ? "unspecified" : text[..Math.Min(text.Length, limit)];
    }
    private static string Digest(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    public static string QueueDirectory()
    {
        string root = Environment.GetEnvironmentVariable("UDX_STATE_DIR") ?? "";
        if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UniversalDiagnostics");
        return Path.Combine(Path.GetFullPath(root), Digest(Project.ToLowerInvariant())[..16]);
    }
    public static string MakePayload(string component, Exception ex) => JsonSerializer.Serialize(new
    {
        project = Project,
        component = Clean(component, 90), version = AppVersion.Current,
        error_type = ex.GetType().Name, code = $"0x{ex.HResult:X8}",
        summary = Clean(ex.Message), level = "error"
    });
    private static object Record(string payload)
    {
        if (Encoding.UTF8.GetByteCount(payload) > 65536) throw new InvalidDataException("Payload too large.");
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        string Field(string key, string fallback, int limit) => root.TryGetProperty(key, out var x)
            && x.ValueKind == JsonValueKind.String ? Clean(x.GetString(), limit) : Clean(fallback, limit);
        string component = Field("component", "Application", 90);
        string errorType = Field("error_type", "Error", 90);
        string code = Field("code", "Unknown", 60);
        return new
        {
            @event = new
            {
                schema = Schema, id = Guid.NewGuid().ToString("D"),
                timestamp_utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                project = Project, component, error_type = errorType, code,
                level = Field("level", "error", 10),
                version = Field("version", AppVersion.Current, 70),
                summary = Field("summary", "No description", 450),
                platform = OperatingSystem.IsWindows() ? "Windows" : "Other",
                architecture = RuntimeInformation.OSArchitecture switch {
                    Architecture.X64 => "AMD64", Architecture.X86 => "x86", Architecture.Arm64 => "ARM64", _ => "Other"
                },
                fingerprint = Digest(string.Join("|", Project.ToLowerInvariant(), component.ToLowerInvariant(), errorType.ToLowerInvariant(), code.ToLowerInvariant()))
            },
            delivery = new { status = "pending", url = "" }
        };
    }
    // The approved preview is persisted unchanged: report ID and timestamp
    // cannot silently drift between operator review and local capture.
    public static string Preview(string payload) =>
        JsonSerializer.Serialize(Record(payload), new JsonSerializerOptions { WriteIndented = true });

    public static Receipt Capture(string payload) => CaptureReviewed(Preview(payload));

    public static Receipt CaptureReviewed(string reviewedPreview)
    {
        if (string.IsNullOrWhiteSpace(reviewedPreview) ||
            Encoding.UTF8.GetByteCount(reviewedPreview) > 65536)
            throw new InvalidDataException("Reviewed diagnostic report is invalid or too large.");
        using var json = JsonDocument.Parse(reviewedPreview, new JsonDocumentOptions { MaxDepth = 8 });
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2)
            throw new InvalidDataException("Unrecognized reviewed diagnostic record.");
        var ev = root.GetProperty("event");
        var delivery = root.GetProperty("delivery");
        if (ev.ValueKind != JsonValueKind.Object ||
            ev.EnumerateObject().Count() != 13 ||
            delivery.ValueKind != JsonValueKind.Object ||
            delivery.EnumerateObject().Count() != 2 ||
            delivery.GetProperty("status").GetString() != "pending" ||
            delivery.GetProperty("url").GetString() != "")
            throw new InvalidDataException("Diagnostic report must have a pending delivery state.");
        string Get(string field) => ev.GetProperty(field).GetString() ??
            throw new InvalidDataException("Diagnostic record contains an invalid field.");
        if (Get("schema") != Schema || Get("project") != Project ||
            !Guid.TryParseExact(Get("id"), "D", out var id) ||
            !DateTimeOffset.TryParse(Get("timestamp_utc"), out var created) ||
            created > DateTimeOffset.UtcNow.AddMinutes(5) ||
            created < DateTimeOffset.UtcNow.AddMinutes(-30) ||
            Get("platform") != (OperatingSystem.IsWindows() ? "Windows" : "Other"))
            throw new InvalidDataException("Diagnostic record has an untrusted scope or timestamp.");
        if (Get("architecture") != (RuntimeInformation.OSArchitecture switch {
                Architecture.X64 => "AMD64", Architecture.X86 => "x86",
                Architecture.Arm64 => "ARM64", _ => "Other"
            }))
            throw new InvalidDataException("Diagnostic architecture changed after preview.");
        foreach (var (field, maximum) in new[] {
            ("component", 90), ("error_type", 90), ("code", 60),
            ("level", 10), ("version", 70), ("summary", 450)
        })
        {
            string value = Get(field);
            if (value != Clean(value, maximum))
                throw new InvalidDataException("Reviewed diagnostic includes unredacted metadata.");
        }
        if (Get("version") != AppVersion.Current ||
            Get("level") is not ("error" or "warning" or "info"))
            throw new InvalidDataException("Diagnostic version or severity is unexpected.");
        string expected = Digest(string.Join("|", Project.ToLowerInvariant(),
            Get("component").ToLowerInvariant(), Get("error_type").ToLowerInvariant(),
            Get("code").ToLowerInvariant()));
        if (!string.Equals(Get("fingerprint"), expected, StringComparison.Ordinal))
            throw new InvalidDataException("Diagnostic fingerprint was modified.");
        byte[] bytes = Encoding.UTF8.GetBytes(reviewedPreview);
        string folder = QueueDirectory();
        Directory.CreateDirectory(folder);
        string temp = Path.Combine(folder, ".udx-" + Guid.NewGuid().ToString("N") + ".tmp");
        string destination = Path.Combine(folder, id.ToString("D") + ".json");
        try
        {
            using (var writer = new FileStream(temp, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            {
                writer.Write(bytes);
                writer.Flush(flushToDisk: true);
            }
            File.Move(temp, destination);
            return new Receipt(id.ToString("D"), destination);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static IReadOnlyList<string> GetReports() => Directory.Exists(QueueDirectory())
        ? Directory.EnumerateFiles(QueueDirectory(), "*.json").OrderByDescending(File.GetCreationTimeUtc).Take(250).ToArray()
        : Array.Empty<string>();
}
