using System.Text.Json;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Comparison of separate named-DC observations. SYSVOL checks are read-only;
/// rows do not prove DFS Replication convergence, clients' RSoP, or future state.
/// </summary>
internal sealed record DcAuditImage(string FileName, string Sha256, long Bytes);

internal sealed record DcAuditNode(
    string Dc, string ReadStatus, string Reason, DateTimeOffset? DcUtc,
    DateTimeOffset? SampledUtc, int OffsetSeconds, string Rodc,
    string ManifestHash, string AgentHash, string GptIniHash,
    string PsScriptsHash, long AdVersion, long GptVersion,
    IReadOnlyList<DcAuditImage> Images);

internal sealed record DcConsistencyRow(
    DcAuditNode Node, string Assessment, string Explanation)
{
    internal bool IsMatching => Assessment == "Matching";
}

internal sealed class DcConsistencyReport
{
    public required string Domain { get; init; }
    public required string SelectedDc { get; init; }
    public required string CampaignId { get; init; }
    public required string GpoId { get; init; }
    public required DateTimeOffset ScannedUtc { get; init; }
    public required IReadOnlyList<DcConsistencyRow> Rows { get; init; }
    public required int DiscoveredCount { get; init; }
    public required bool ScopeComplete { get; init; }
    public required string Overall { get; init; }

    public int Matching => Rows.Count(x => x.Assessment == "Matching");
    public int Mismatch => Rows.Count(x => x.Assessment == "Mismatch");
    public int Unavailable => Rows.Count(x => x.Assessment == "Unavailable");
    public int Invalid => Rows.Count(x => x.Assessment == "Invalid");
    public int ClockWarning => Rows.Count(x => x.Assessment == "Clock drift");

    private static readonly Regex DomainPattern = new(
        @"\A(?=.{3,253}\z)[a-z0-9-]+(?:\.[a-z0-9-]+)+\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CampaignPattern = new(
        @"\A[a-z0-9][a-z0-9_-]{0,70}\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new(
        @"\A[a-f0-9]{64}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> AllowedReasons = new(StringComparer.Ordinal)
    {
        "OK", "ReadDeniedOrUnavailable", "InvalidFileTypeOrLength",
        "InvalidDirectoryOrReparsePoint", "InvalidDomainControllerUtc",
        "MissingDomainControllerRootDseFields", "DomainControllerIdentityMismatch",
        "SlowOrUnverifiableDcTimeSample", "UnexpectedCampaignLayout",
        "ManifestOrAgentIntegrityMismatch", "InvalidManifestImageCount",
        "UnexpectedImageCount", "InvalidImageManifestEntry", "ImageSha256Mismatch",
        "UnexpectedImageFile", "GptVersionMissing", "InvalidGptVersion",
        "GpoStartupTargetsOtherCampaign", "ManagedGpoAdMissing",
        "AdGptVersionMismatch", "CampaignChangedDuringScan"
    };

    private static string RequiredString(JsonElement element, string name, int max)
    {
        string value = element.GetProperty(name).GetString() ??
            throw new InvalidDataException("Empty multi-DC audit field.");
        if (value.Length > max)
            throw new InvalidDataException("Oversized multi-DC audit field.");
        return value;
    }

    private static DcAuditNode ParseNode(JsonElement e, string domain,
        DateTimeOffset now)
    {
        string dc = RequiredString(e, "dc", 253);
        if (!DomainPattern.IsMatch(domain) ||
            !dc.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase) ||
            !DomainPattern.IsMatch(dc.ToLowerInvariant()))
            throw new InvalidDataException("Multi-DC report contains an out-of-domain controller.");
        string state = RequiredString(e, "status", 24);
        string reason = RequiredString(e, "reason", 65);
        if (state is not ("Readable" or "Invalid" or "Unavailable") ||
            !AllowedReasons.Contains(reason) ||
            (state == "Readable" && reason != "OK") ||
            (state != "Readable" && reason == "OK"))
            throw new InvalidDataException("Multi-DC report contains invalid read status.");
        string rodc = RequiredString(e, "rodc", 7);
        if (rodc is not ("Yes" or "No" or "Unknown"))
            throw new InvalidDataException("Unrecognized RODC state.");
        int offset = e.GetProperty("offsetSeconds").GetInt32();
        if (Math.Abs((long)offset) > 86400)
            throw new InvalidDataException("Unreasonable DC clock offset.");
        DateTimeOffset? dcUtc = null;
        DateTimeOffset? sampled = null;
        string dcText = RequiredString(e, "dcUtc", 48);
        string localText = RequiredString(e, "sampledUtc", 48);
        if (dcText.Length != 0 || localText.Length != 0)
        {
            if (dcText.Length == 0 || localText.Length == 0 ||
                !DateTimeOffset.TryParse(dcText, out var d) ||
                !DateTimeOffset.TryParse(localText, out var s) ||
                d.Offset != TimeSpan.Zero || s.Offset != TimeSpan.Zero ||
                d > now.AddMinutes(10) || s > now.AddMinutes(10) ||
                d < now.AddHours(-4) || s < now.AddHours(-4))
                throw new InvalidDataException("Invalid DC clock sampling timestamps.");
            dcUtc = d;
            sampled = s;
        }
        string manifest = RequiredString(e, "manifestHash", 64);
        string agent = RequiredString(e, "agentHash", 64);
        string gpt = RequiredString(e, "gptIniHash", 64);
        string startup = RequiredString(e, "psScriptsHash", 64);
        long adVersion = e.GetProperty("adVersion").GetInt64();
        long gptVersion = e.GetProperty("gptVersion").GetInt64();
        JsonElement items = e.GetProperty("images");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 30)
            throw new InvalidDataException("Invalid multi-DC image inventory.");
        var images = new List<DcAuditImage>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.EnumerateArray())
        {
            string name = RequiredString(item, "name", 6);
            string hash = RequiredString(item, "sha256", 64);
            long bytes = item.GetProperty("bytes").GetInt64();
            if (!Regex.IsMatch(name, @"\A[0-9]{2}\.jpg\z") ||
                !names.Add(name) || !Sha256Pattern.IsMatch(hash) ||
                bytes is < 1 or > 31457280)
                throw new InvalidDataException("Invalid multi-DC image entry.");
            images.Add(new DcAuditImage(name, hash, bytes));
        }
        if (state == "Readable" &&
            (dcUtc is null || images.Count is < 1 or > 30 ||
             !Sha256Pattern.IsMatch(manifest) ||
             !Sha256Pattern.IsMatch(agent) ||
             !Sha256Pattern.IsMatch(gpt) ||
             !Sha256Pattern.IsMatch(startup) ||
             adVersion < 0 || gptVersion < 0 || adVersion != gptVersion))
            throw new InvalidDataException("Readable DC lacks verified evidence.");
        if (state != "Readable" &&
            (manifest.Length != 0 || agent.Length != 0 || gpt.Length != 0 ||
             startup.Length != 0 || images.Count != 0 ||
             adVersion != -1 || gptVersion != -1))
            throw new InvalidDataException("Partial DC evidence must never be presented as verified.");

        return new DcAuditNode(dc, state, reason, dcUtc, sampled, offset,
            rodc, manifest, agent, gpt, startup, adVersion, gptVersion, images);
    }

    private static bool ImageSetEquals(DcAuditNode left, DcAuditNode right)
    {
        if (left.Images.Count != right.Images.Count) return false;
        var rightByName = right.Images.ToDictionary(x => x.FileName,
            StringComparer.OrdinalIgnoreCase);
        return left.Images.All(x => rightByName.TryGetValue(x.FileName, out var y) &&
            x.Sha256 == y.Sha256 && x.Bytes == y.Bytes);
    }

    private static bool FilesAndGpoEqual(DcAuditNode a, DcAuditNode b) =>
        a.ManifestHash == b.ManifestHash &&
        a.AgentHash == b.AgentHash &&
        a.GptIniHash == b.GptIniHash &&
        a.PsScriptsHash == b.PsScriptsHash &&
        a.AdVersion == b.AdVersion &&
        a.GptVersion == b.GptVersion &&
        ImageSetEquals(a, b);

    internal static DcConsistencyReport Parse(JsonElement data,
        string expectedDomain, string selectedDc, string campaignId,
        string gpoId, IReadOnlyCollection<string> discovered,
        IReadOnlyCollection<string> requested, DateTimeOffset now)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !Guid.TryParse(gpoId, out var target) || target == Guid.Empty ||
            !CampaignPattern.IsMatch(campaignId) ||
            !DomainPattern.IsMatch(expectedDomain.ToLowerInvariant()) ||
            data.GetProperty("domain").GetString() != expectedDomain.ToLowerInvariant() ||
            !string.Equals(data.GetProperty("selectedDc").GetString(), selectedDc,
                StringComparison.OrdinalIgnoreCase) ||
            data.GetProperty("campaignId").GetString() != campaignId ||
            !Guid.TryParse(data.GetProperty("gpoId").GetString(), out var got) ||
            got != target)
            throw new InvalidDataException("Multi-DC response has wrong campaign/domain/GPO.");
        DateTimeOffset scanned = data.GetProperty("scannedUtc").GetDateTimeOffset();
        if (scanned > now.AddMinutes(5) || scanned < now.AddMinutes(-30))
            throw new InvalidDataException("Multi-DC scan is stale or from the future.");
        var all = new HashSet<string>(discovered, StringComparer.OrdinalIgnoreCase);
        var expected = new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase);
        if (all.Count is < 1 or > 5000 || expected.Count is < 1 or > 12 ||
            expected.Count != requested.Count ||
            !expected.IsSubsetOf(all) || !expected.Contains(selectedDc) ||
            !all.Contains(selectedDc))
            throw new InvalidDataException("Multi-DC scan target list is not the discovered AD scope.");
        JsonElement payload = data.GetProperty("rows");
        if (payload.ValueKind != JsonValueKind.Array ||
            payload.GetArrayLength() != expected.Count)
            throw new InvalidDataException("Multi-DC scan omitted requested controllers.");
        var nodes = payload.EnumerateArray()
            .Select(e => ParseNode(e, expectedDomain.ToLowerInvariant(), now))
            .ToArray();
        var returned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (nodes.Any(n => !expected.Contains(n.Dc) || !returned.Add(n.Dc)))
            throw new InvalidDataException("Multi-DC scan returned an unexpected or duplicate controller.");
        var reference = nodes.Single(x =>
            x.Dc.Equals(selectedDc, StringComparison.OrdinalIgnoreCase));
        bool baselineUsable = reference.ReadStatus == "Readable";
        var rows = new List<DcConsistencyRow>(nodes.Length);
        foreach (var node in nodes)
        {
            string status;
            string detail;
            if (node.ReadStatus == "Unavailable")
            {
                status = "Unavailable";
                detail = "Selected domain controller cannot be fully read. No success inferred.";
            }
            else if (node.ReadStatus == "Invalid")
            {
                status = "Invalid";
                detail = node.Reason + "; investigate AD/SYSVOL without overwriting content.";
            }
            else if (!baselineUsable)
            {
                status = "Unverified";
                detail = "Selected baseline DC is unavailable or invalid; comparisons are blocked.";
            }
            else if (Math.Abs((long)node.OffsetSeconds) > 300 ||
                Math.Abs((long)reference.OffsetSeconds) > 300 ||
                Math.Abs((long)node.OffsetSeconds - reference.OffsetSeconds) > 120)
            {
                status = "Clock drift";
                detail = "Controller UTC clocks differ materially; review Windows Time and PDC source.";
            }
            else if (!FilesAndGpoEqual(node, reference))
            {
                status = "Mismatch";
                detail = "Campaign SHA-256/images or selected GPO AD/GPT versions differ from baseline.";
            }
            else
            {
                status = "Matching";
                detail = "Read-only observation matches baseline; not a replication convergence guarantee.";
            }
            rows.Add(new DcConsistencyRow(node, status, detail));
        }

        bool allDiscovered = expected.Count == all.Count;
        bool allGood = rows.All(x => x.IsMatching);
        string overall = !baselineUsable ? "Unverified" :
            rows.Any(x => x.Assessment == "Mismatch") ? "Mismatch" :
            rows.Any(x => x.Assessment == "Invalid") ? "Invalid" :
            rows.Any(x => x.Assessment == "Clock drift") ? "Clock drift" :
            rows.Any(x => x.Assessment == "Unavailable" || x.Assessment == "Unverified")
                ? "Incomplete" :
            !allDiscovered ? "Partial scope" :
            expected.Count == 1 ? "Single DC only" :
            allGood ? "Matching at scan time" : "Incomplete";

        return new DcConsistencyReport
        {
            Domain = expectedDomain.ToLowerInvariant(),
            SelectedDc = selectedDc,
            CampaignId = campaignId,
            GpoId = gpoId,
            ScannedUtc = scanned,
            DiscoveredCount = all.Count,
            ScopeComplete = allDiscovered,
            Rows = rows.OrderBy(x => x.Node.Dc, StringComparer.OrdinalIgnoreCase).ToArray(),
            Overall = overall
        };
    }
}
