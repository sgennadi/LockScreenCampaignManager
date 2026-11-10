using System.Text.Json;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only interpretation of untrusted local-only SYSTEM canary and endpoint
/// cleanup reports. No computer identity is carried by these report schemas;
/// importing them never authenticates a host, GPO, or successful display.
/// </summary>
internal sealed record LocalLabCheck(string Label, bool? Passed, string Description);

internal sealed record LocalLabEvidence(
    string Kind, string Result, DateTimeOffset ObservedUtc, bool Stale,
    int OsBuild, IReadOnlyList<LocalLabCheck> Checks, IReadOnlyList<string> Findings)
{
    internal bool IsAttestation => false;

    internal static LocalLabEvidence LoadLocalFile(string path, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Select an existing LOCAL report.json file.");
        string full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("Reports must be imported from a local disk.");
        for (DirectoryInfo? d = Directory.GetParent(full); d is not null; d = d.Parent)
            if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked/junction report folders are not permitted.");
        var info = new FileInfo(full);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
            info.Length < 10 || info.Length > 256 * 1024)
            throw new InvalidDataException("Invalid, linked, or oversized local evidence report.");
        using var reader = new FileStream(full, FileMode.Open, FileAccess.Read,
            FileShare.Read, 32 * 1024, FileOptions.SequentialScan);
        using var doc = JsonDocument.Parse(reader, new JsonDocumentOptions {
            MaxDepth = 12, AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        return Parse(doc.RootElement, now);
    }

    internal static LocalLabEvidence Parse(JsonElement data, DateTimeOffset now)
    {
        if (data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Evidence must be one JSON object.");
        string schema = Str(data, "schema", 80);
        bool canary = schema == "lscm-smb-system-canary/v1";
        bool closure = schema == "lscm-endpoint-closure/v1";
        if (!canary && !closure)
            throw new InvalidDataException("Not a supported local SYSTEM evidence schema.");
        string result = Str(data, "result", 100);
        string[] valid = canary
            ? ["INCOMPLETE", "FAIL_CLOSED",
                "SOURCE_VERIFIED_AS_LOCAL_SYSTEM_ONLY",
                "POSTBOOT_SOURCE_AND_COMPUTER_RSOP_OBSERVED_ONLY"]
            : ["FAIL_CLOSED", "PREDEPLOYMENT_BASELINE_CAPTURED",
                "POSTCLEANUP_REQUIRES_REVIEW",
                "POSTCLEANUP_LOCAL_STATE_MATCHES_BASELINE"];
        if (!valid.Contains(result, StringComparer.Ordinal))
            throw new InvalidDataException("Unknown or incompatible result code.");
        string rawTime = Str(data, canary ? "utc" : "observedUtc", 64);
        if (!DateTimeOffset.TryParseExact(rawTime, "o",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var time) ||
            time > now.AddMinutes(5) || time.Year < 2024)
            throw new InvalidDataException("Invalid or future local evidence timestamp.");
        bool stale = now - time > TimeSpan.FromHours(24);
        int build = Int(data, "osBuild", 0, 999999);
        var checks = new List<LocalLabCheck>();
        bool Check(string key, string title, string explanation)
        {
            bool value = Bool(data, key);
            checks.Add(new(title, value, explanation));
            return value;
        }
        string kind;
        if (canary)
        {
            kind = "SMB LocalSystem canary";
            bool system = Check("localSystem", "LocalSystem process",
                "Local SYSTEM is claimed by the report, not remotely authenticated.");
            bool client = Check("windowsClient", "Windows client", "Windows client OS reported.");
            bool computer = Check("approvedComputer", "Expected computer",
                "Local test-runner identity check reported.");
            bool domain = Check("approvedDomain", "Expected AD domain",
                "Local test-runner domain check reported.");
            bool source = Check("smbSourceReadable", "Read SMB source",
                "Observed at the time of the local test, not necessarily during boot.");
            bool manifest = Check("manifestSha256Verified", "Manifest SHA-256",
                "Local report claims pinned manifest matched.");
            bool agent = Check("agentSha256Verified", "Agent SHA-256",
                "Local report claims pinned PS1/EXE matched.");
            bool images = Check("allImagesVerified", "Images SHA-256",
                "All manifest-listed image hashes were checked.");
            bool cache = Check("localCacheHashVerified", "Local cache SHA-256",
                "Local package content matches or cache was unavailable.");
            bool reboot = Check("rebootAfterDeployment", "Reboot after deployment",
                "Boot timestamp follows the deployment receipt.");
            bool gp = Check("actualGpoAppliedVerified", "Exact Computer GPO in RSoP",
                "Exact GUID in Computer RSoP; not proof it set the winning value.");
            string gpStatus = Str(data, "gpoRsopStatus", 64);
            if (gpStatus is not ("NotCollected" or "UnavailableOrUnparseable" or
                "RsopUnavailable" or "NotApplied" or "GpoFilteredOrDisabled" or
                "IncompleteGpoEvidence" or "AmbiguousGpoIdentity" or
                "AppliedComputerGpo"))
                throw new InvalidDataException("Unknown Computer GPO evidence status.");
            bool gpr = Bool(data, "computerGpresultAvailable");
            bool cachePresent = Bool(data, "localCachePresent");
            int imagesCount = Int(data, "imageCount", 0, 10000);
            if (gp != (gpStatus == "AppliedComputerGpo") ||
                (gp && !gpr) || (images && imagesCount < 1) ||
                (cache && !cachePresent))
                throw new InvalidDataException("Contradictory SMB canary checks.");
            bool basePass = system && client && computer && domain &&
                source && manifest && agent && images;
            if ((result == "SOURCE_VERIFIED_AS_LOCAL_SYSTEM_ONLY" && !basePass) ||
                (result == "POSTBOOT_SOURCE_AND_COMPUTER_RSOP_OBSERVED_ONLY" &&
                 !(basePass && cache && reboot && gp)))
                throw new InvalidDataException("Success result conflicts with missing SMB evidence.");
            _ = Bool(data, "realLockScreenDisplayVerified");
            _ = Bool(data, "registryRestorationVerified");
        }
        else
        {
            kind = "Endpoint cleanup audit";
            bool system = Check("localSystem", "LocalSystem process",
                "Local SYSTEM is claimed by the report; not an authenticated remote identity.");
            bool authorized = Check("authorizedTestComputer", "Test computer and domain",
                "Reported locally verified against the approved test endpoint.");
            bool baseline = Check("baselineBeforeDeployment", "Baseline before deployment",
                "Baseline capture UTC precedes deployed UTC.");
            bool registry = Check("registryValuesMatchBaseline", "Registry matches baseline",
                "Fingerprint of two Lock Screen policy values matches original.");
            bool key = Check("registryKeyPresenceMatchesBaseline", "Registry key restored",
                "Original personalization key presence matches.");
            bool files = Check("campaignFolderAbsent", "Campaign directory removed",
                "Campaign-specific ProgramData folder is absent.");
            bool task = Check("mainTaskAbsent", "Scheduled task removed",
                "Main campaign scheduled task absent.");
            bool feedback = Check("feedbackTaskAbsent", "Feedback task removed",
                "Campaign feedback task absent.");
            bool owner = Check("ownerRecordAbsent", "Owner record released",
                "No active owner record.");
            bool receipt = Check("cleanupReceiptVerified", "Final agent receipt",
                "Final postcleanup receipt claims all agent cleanup stages finished.");
            bool gp = Check("expectedGpoNotApplied", "Exact Computer GPO no longer applied",
                "Local Computer RSoP does not list the exact GPO GUID.");
            bool available = Bool(data, "computerGpresultAvailable");
            string status = Str(data, "gpoRsopStatus", 64);
            if (status is not ("NotCollected" or "RsopUnavailable" or
                "NotApplied" or "AppliedComputerGpo" or "AmbiguousGpoIdentity" or
                "IncompleteGpoEvidence" or "GpoFilteredOrDisabled"))
                throw new InvalidDataException("Unknown cleanup RSoP evidence status.");
            if (gp != (status == "NotApplied") || (gp && !available))
                throw new InvalidDataException("Contradictory cleanup GPO/RSoP result.");
            if (result == "POSTCLEANUP_LOCAL_STATE_MATCHES_BASELINE" &&
                !(system && authorized && baseline && registry && key && files &&
                  task && feedback && owner && receipt && gp))
                throw new InvalidDataException("Cleanup success contradicts the reported checks.");
            if (result == "PREDEPLOYMENT_BASELINE_CAPTURED" &&
                (!system || !authorized || !files || !task || !feedback || !owner))
                throw new InvalidDataException("Baseline success contradicts the reported checks.");
            _ = Bool(data, "lockScreenDisplayVisuallyVerified");
        }
        JsonElement list = data.GetProperty("findings");
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 64)
            throw new InvalidDataException("Invalid evidence finding list.");
        var findings = new List<string>();
        foreach (var f in list.EnumerateArray())
        {
            if (f.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Invalid evidence finding.");
            string code = f.GetString() ?? "";
            if (!Regex.IsMatch(code, @"\A[A-Z0-9_]{3,80}\z",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                throw new InvalidDataException("Invalid or private finding text.");
            findings.Add(code);
        }
        return new LocalLabEvidence(kind, result, time.ToUniversalTime(),
            stale, build, checks, findings);
    }

    private static string Str(JsonElement root, string name, int max)
    {
        if (!root.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Missing required evidence field: " + name);
        string s = value.GetString() ?? "";
        if (s.Length < 1 || s.Length > max || s.Contains('\0'))
            throw new InvalidDataException("Invalid evidence field: " + name);
        return s;
    }
    private static bool Bool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Missing boolean evidence: " + name);
        return value.GetBoolean();
    }
    private static int Int(JsonElement root, string name, int min, int max)
    {
        if (!root.TryGetProperty(name, out var value) ||
            !value.TryGetInt32(out int n) || n < min || n > max)
            throw new InvalidDataException("Invalid evidence number: " + name);
        return n;
    }
}
