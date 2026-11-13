using System.Globalization;
using System.Net.NetworkInformation;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace LockScreenCampaignManager;

/// <summary>
/// Explicit, unsigned snapshot of the CURRENT process user's HKCU on THIS
/// Windows PC. No remote registry, commands, gpupdate, policy writes, agents
/// or files are touched until the operator chooses a LOCAL export path.
/// DNS suffix/user labels come from the local OS, NOT authenticated AD.
/// </summary>
internal sealed record EndpointUserSaverValue(
    string Source, string Setting, string Status, string Value);

internal sealed class EndpointUserReadinessEvidence
{
    internal const string Schema = "lscm-screen-saver-endpoint/v1";
    private const int MaxBytes = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly (string Source, string Path, string[] Settings)[] Sections =
    [
        ("PolicyHKCU", @"Software\Policies\Microsoft\Windows\Control Panel\Desktop",
            ["ScreenSaveActive", "ScreenSaveTimeOut", "SCRNSAVE.EXE", "ScreenSaverIsSecure"]),
        ("DesktopHKCU", @"Control Panel\Desktop",
            ["ScreenSaveActive", "ScreenSaveTimeOut", "SCRNSAVE.EXE", "ScreenSaverIsSecure"]),
        ("RestrictionHKCU", @"Software\Microsoft\Windows\CurrentVersion\Policies\System",
            ["NoDispScrSavPage"])
    ];

    internal string Domain { get; private init; } = "";
    internal string Computer { get; private init; } = "";
    internal string User { get; private init; } = "";
    internal string Session { get; private init; } = "";
    internal DateTimeOffset CapturedUtc { get; private init; }
    internal IReadOnlyList<EndpointUserSaverValue> Values { get; private init; } = [];
    internal string Sha256 { get; private init; } = "";
    internal string RawJson { get; private init; } = "";
    internal bool SignedByDomain => false;
    internal bool ProvesWinningGpo => false;
    internal bool ProvesScreenSaverDisplayed => false;
    internal bool IsFresh(DateTimeOffset now) =>
        CapturedUtc <= now.AddMinutes(2) &&
        now - CapturedUtc <= TimeSpan.FromMinutes(10);

    internal EndpointUserSaverValue Get(string source, string setting) =>
        Values.Single(x => x.Source == source &&
            x.Setting.Equals(setting, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads HKCU only, under the identity actually running this process.
    /// Run as the pilot user, not elevated under another administrator account.
    /// </summary>
    internal static EndpointUserReadinessEvidence CaptureLocal()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Endpoint snapshot requires Windows.");
        string suffix = IPGlobalProperties.GetIPGlobalProperties().DomainName.Trim().Trim('.');
        string fqdn = Environment.MachineName + "." + suffix;
        if (suffix.Length == 0 ||
            !NativeGpoConflictScan.SafeComputerName(fqdn, suffix))
            throw new InvalidDataException(
                "Local DNS domain suffix cannot identify this PC; no endpoint evidence exported.");
        string user = WindowsIdentity.GetCurrent().Name;
        if (string.IsNullOrWhiteSpace(user) || user.Length > 256 ||
            user.Any(char.IsControl))
            throw new InvalidDataException("Windows identity is unavailable.");

        var values = new List<EndpointUserSaverValue>();
        foreach (var (source, path, settings) in Sections)
        {
            RegistryKey? key = null;
            bool inaccessible = false;
            try { key = Registry.CurrentUser.OpenSubKey(path, writable: false); }
            catch (Exception e) when (e is UnauthorizedAccessException or
                IOException or SecurityException)
            { inaccessible = true; }
            using (key)
            {
                foreach (string setting in settings)
                {
                    if (inaccessible)
                    {
                        values.Add(new(source, setting, "Unreadable", ""));
                        continue;
                    }
                    try
                    {
                        object? raw = key?.GetValue(setting, null,
                            RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (raw is null)
                            values.Add(new(source, setting, "Missing", ""));
                        else if (raw is string text && text.Length <= 512 &&
                            !text.Any(char.IsControl))
                            values.Add(new(source, setting, "Present", text));
                        else if (raw is int or long)
                            values.Add(new(source, setting, "Present",
                                Convert.ToString(raw, CultureInfo.InvariantCulture) ?? ""));
                        else
                            values.Add(new(source, setting, "Unreadable", ""));
                    }
                    catch (Exception e) when (e is UnauthorizedAccessException or
                        IOException or SecurityException or ArgumentException)
                    { values.Add(new(source, setting, "Unreadable", "")); }
                }
            }
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string json = JsonSerializer.Serialize(new
        {
            schema = Schema, capturedUtc = now.ToString("O", CultureInfo.InvariantCulture),
            domain = suffix.ToLowerInvariant(), computer = fqdn.ToLowerInvariant(),
            user, session = System.Windows.Forms.SystemInformation.TerminalServerSession
                ? "RemoteDesktop" : "NonRemoteUnverified",
            values = values.Select(x => new
            {
                source = x.Source, setting = x.Setting, status = x.Status, value = x.Value
            }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true });
        return ParseJson(json, now);
    }

    /// <summary>
    /// STRICT parser for operator-supplied, unsigned local JSON. Rejects
    /// unexpected scopes, duplicate/missing keys, large inputs, future dates,
    /// malformed statuses and executable/command-like fields.
    /// </summary>
    internal static EndpointUserReadinessEvidence ParseJson(string json, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(json) ||
            StrictUtf8.GetByteCount(json) > MaxBytes)
            throw new InvalidDataException("Endpoint JSON is missing or exceeds 64 KiB.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            MaxDepth = 8, AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Endpoint evidence must be a JSON object.");
        CheckExactKeys(root, ["schema", "capturedUtc", "domain", "computer",
            "user", "session", "values"]);
        if (Field(root, "schema", 80) != Schema)
            throw new InvalidDataException("Unsupported endpoint snapshot schema.");

        string domain = Field(root, "domain", 253);
        string computer = Field(root, "computer", 253);
        string user = Field(root, "user", 256);
        string session = Field(root, "session", 30);
        if (!NativeGpoConflictScan.SafeComputerName(computer, domain) ||
            string.IsNullOrWhiteSpace(user) ||
            session is not ("RemoteDesktop" or "NonRemoteUnverified"))
            throw new InvalidDataException("Invalid endpoint identity/session.");
        string timestamp = Field(root, "capturedUtc", 50);
        if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset captured) ||
            captured.Offset != TimeSpan.Zero || captured.Year < 2020 ||
            captured > now.AddMinutes(2))
            throw new InvalidDataException("Invalid/future UTC endpoint snapshot time.");

        JsonElement list = root.GetProperty("values");
        int expected = Sections.Sum(s => s.Settings.Length);
        if (list.ValueKind != JsonValueKind.Array ||
            list.GetArrayLength() != expected)
            throw new InvalidDataException("Endpoint snapshot must contain all fixed HKCU readings.");
        var values = new List<EndpointUserSaverValue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid endpoint registry entry.");
            CheckExactKeys(item, ["source", "setting", "status", "value"]);
            string source = Field(item, "source", 30);
            string setting = Field(item, "setting", 50);
            string status = Field(item, "status", 20);
            string value = Field(item, "value", 512);
            if (!Sections.Any(s => s.Source == source &&
                    s.Settings.Contains(setting, StringComparer.OrdinalIgnoreCase)) ||
                !seen.Add(source + "|" + setting) ||
                status is not ("Present" or "Missing" or "Unreadable") ||
                (status != "Present" && value.Length > 0))
                throw new InvalidDataException("Unknown/duplicate/invalid HKCU reading.");
            values.Add(new(source, setting, status, value));
        }
        return new EndpointUserReadinessEvidence
        {
            Domain = domain, Computer = computer, User = user,
            Session = session, CapturedUtc = captured.ToUniversalTime(),
            Values = values, RawJson = json,
            Sha256 = Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(json)))
                .ToLowerInvariant()
        };
    }

    private static void CheckExactKeys(JsonElement data, string[] expected)
    {
        string[] actual = data.EnumerateObject().Select(x => x.Name).ToArray();
        if (actual.Length != expected.Length ||
            actual.Distinct(StringComparer.Ordinal).Count() != actual.Length ||
            actual.Except(expected, StringComparer.Ordinal).Any() ||
            expected.Except(actual, StringComparer.Ordinal).Any())
            throw new InvalidDataException("Unexpected or repeated endpoint JSON property.");
    }

    private static string Field(JsonElement obj, string key, int max)
    {
        JsonElement value = obj.GetProperty(key);
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Invalid endpoint text type: " + key);
        string result = value.GetString() ?? "";
        if (result.Length > max || result.Any(char.IsControl))
            throw new InvalidDataException("Unsafe endpoint text: " + key);
        return result;
    }

    internal static EndpointUserReadinessEvidence LoadLocalJson(string path,
        string expectedDomain, string expectedComputer, DateTimeOffset now)
    {
        string full = EnsureLocalPath(path, mustExist: true);
        if (new FileInfo(full).Length > MaxBytes)
            throw new InvalidDataException("Endpoint JSON exceeds 64 KiB.");
        var evidence = ParseJson(File.ReadAllText(full, StrictUtf8), now);
        if (!evidence.Domain.Equals(expectedDomain, StringComparison.OrdinalIgnoreCase) ||
            !evidence.Computer.Equals(expectedComputer, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Endpoint snapshot DNS suffix/computer differs from the selected pilot. " +
                "Do not reuse a snapshot from another device.");
        return evidence;
    }

    /// <summary>
    /// A new local file only: never overwrite, traverse a reparse directory,
    /// write to UNC, perform network I/O or upload a domain/user identity.
    /// </summary>
    internal void SaveLocalJson(string path)
    {
        string full = EnsureLocalPath(path, mustExist: false);
        using var stream = new FileStream(full, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream, StrictUtf8);
        writer.Write(RawJson);
    }

    private static string EnsureLocalPath(string path, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 ||
            path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Only an explicit local absolute JSON path is permitted.");
        string full = Path.GetFullPath(path);
        if (!full.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(@"\\", StringComparison.Ordinal) ||
            full.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException("Choose a local .json file, not a network share.");
        DirectoryInfo? parent = new DirectoryInfo(
            Path.GetDirectoryName(full) ??
            throw new InvalidDataException("Missing endpoint export folder."));
        if (!parent.Exists)
            throw new DirectoryNotFoundException(parent.FullName);
        for (DirectoryInfo? current = parent; current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse/link folders cannot store endpoint evidence.");
        if (mustExist && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Do not import a linked endpoint evidence file.");
        return full;
    }
}
