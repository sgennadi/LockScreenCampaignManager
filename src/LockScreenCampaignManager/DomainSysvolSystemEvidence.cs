using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Strict operator-imported, UNSIGNED local SYSTEM SYSVOL read observation.
/// Does not prove actual client GPO application, RSoP or winning policy.
/// </summary>
internal sealed class DomainSysvolSystemEvidence
{
    internal const string Schema = "lscm-sysvol-system/v1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] FileStates =
        ["Read", "Denied", "Missing", "Unavailable", "Unsafe"];
    private static readonly string[] RequiredKeys =
    [
        "schema", "observedUtc", "domain", "dc", "computer", "gpoId",
        "localSystem", "gptIni", "userRegistryPol", "machineRegistryPol", "result"
    ];

    internal string Domain { get; private init; } = "";
    internal string Dc { get; private init; } = "";
    internal string Computer { get; private init; } = "";
    internal string GpoId { get; private init; } = "";
    internal DateTimeOffset ObservedUtc { get; private init; }
    internal string GptIni { get; private init; } = "";
    internal string UserRegistryPol { get; private init; } = "";
    internal string MachineRegistryPol { get; private init; } = "";
    internal string ReportSha256 { get; private init; } = "";
    internal bool SignedByDomain => false;
    internal bool ProvesWinningGpo => false;
    internal bool ProvesClientApplication => false;
    internal bool Fresh(DateTimeOffset now) =>
        ObservedUtc <= now.AddMinutes(2) &&
        now - ObservedUtc <= TimeSpan.FromMinutes(10);

    internal static DomainSysvolSystemEvidence ParseJson(
        string json, string expectedDomain, string expectedDc,
        string expectedComputer, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(json) ||
            StrictUtf8.GetByteCount(json) > 16 * 1024)
            throw new InvalidDataException("SYSVOL SYSTEM report exceeds 16 KiB.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            MaxDepth = 4, AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid SYSVOL SYSTEM JSON object.");
        var keys = root.EnumerateObject().Select(x => x.Name).ToArray();
        if (keys.Length != RequiredKeys.Length ||
            keys.Distinct(StringComparer.Ordinal).Count() != keys.Length ||
            RequiredKeys.Except(keys, StringComparer.Ordinal).Any())
            throw new InvalidDataException("Unknown, missing or repeated SYSVOL fields.");
        string field(string name, int max)
        {
            JsonElement element = root.GetProperty(name);
            if (element.ValueKind != JsonValueKind.String ||
                element.GetString() is not string value || value.Length > max ||
                value.Any(char.IsControl))
                throw new InvalidDataException("Invalid SYSVOL field: " + name);
            return value;
        }
        if (field("schema", 60) != Schema)
            throw new InvalidDataException("Unsupported SYSVOL report schema.");
        string domain = field("domain", 253);
        string dc = field("dc", 253);
        string computer = field("computer", 253);
        string gpo = field("gpoId", 40);
        if (!domain.Equals(expectedDomain, StringComparison.OrdinalIgnoreCase) ||
            !NativeGpoLifecycleList.SameController(dc, expectedDc) ||
            !computer.Equals(expectedComputer, StringComparison.OrdinalIgnoreCase) ||
            !NativeGpoConflictScan.SafeComputerName(computer, expectedDomain) ||
            !Guid.TryParseExact(gpo, "D", out Guid guid) || guid == Guid.Empty)
            throw new InvalidDataException(
                "SYSVOL report domain/DC/computer/GPO is not a verified pilot match.");
        if (!root.TryGetProperty("localSystem", out var system) ||
            system.ValueKind != JsonValueKind.True)
            throw new InvalidDataException("Report must declare local SYSTEM context.");
        string gpt = field("gptIni", 20);
        string user = field("userRegistryPol", 20);
        string machine = field("machineRegistryPol", 20);
        string result = field("result", 30);
        if (!FileStates.Contains(gpt) || !FileStates.Contains(user) ||
            !FileStates.Contains(machine) ||
            result != (gpt == "Read" ? "GPT_READ" : "GPT_NOT_READ"))
            throw new InvalidDataException("Inconsistent SYSVOL file/read states.");
        string stamp = field("observedUtc", 55);
        if (!DateTimeOffset.TryParse(stamp,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var observed) ||
            observed.Offset != TimeSpan.Zero || observed.Year < 2020 ||
            observed > now.AddMinutes(2))
            throw new InvalidDataException("Invalid/future SYSVOL observation UTC.");
        return new DomainSysvolSystemEvidence
        {
            Domain = domain, Dc = dc, Computer = computer,
            GpoId = guid.ToString("D"), ObservedUtc = observed.ToUniversalTime(),
            GptIni = gpt, UserRegistryPol = user,
            MachineRegistryPol = machine,
            ReportSha256 = Convert.ToHexString(
                SHA256.HashData(StrictUtf8.GetBytes(json))).ToLowerInvariant()
        };
    }

    internal static DomainSysvolSystemEvidence LoadLocalJson(
        string path, string domain, string dc, string computer, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 ||
            !Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException("Choose an absolute local JSON file.");
        string full = Path.GetFullPath(path);
        if (!full.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("Network and non-JSON evidence is refused.");
        var parent = new DirectoryInfo(Path.GetDirectoryName(full) ??
            throw new InvalidDataException("Missing local evidence directory."));
        if (!parent.Exists)
            throw new DirectoryNotFoundException(parent.FullName);
        for (DirectoryInfo? dir = parent; dir is not null; dir = dir.Parent)
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked evidence folders are refused.");
        var item = new FileInfo(full);
        if (!item.Exists || item.Length is < 20 or > 16384 ||
            (item.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Invalid or linked local SYSVOL report.");
        return ParseJson(File.ReadAllText(full, StrictUtf8), domain, dc, computer, now);
    }
}
