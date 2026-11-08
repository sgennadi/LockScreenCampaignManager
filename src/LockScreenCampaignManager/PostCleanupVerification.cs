using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// An observation from remote SMB, Task Scheduler and Remote Registry reads.
/// "Verified" means only that the live footprint matched an agent-generated
/// post-cleanup receipt at the timestamp. It is not signed attestation or RSoP.
/// </summary>
internal sealed record PostCleanupObservation(
    string Computer, string CampaignId, DateTimeOffset ObservedUtc,
    string Status, string Reason, bool ReceiptValid, bool TasksGone,
    bool LocalFolderGone, bool OwnerReleased, bool RegistryRestored)
{
    internal static readonly string[] AllowedStatuses =
        ["Verified", "Pending", "Unknown", "NeedsReview"];
    internal static readonly string[] AllowedReasons =
    [
        "RemoteFootprintMatchesPostCleanupReceipt",
        "LocalCampaignFolderStillPresent",
        "CampaignOwnerStillPresent", "NoPostCleanupReceipt",
        "CampaignTaskStillPresent", "AnotherCampaignOwnsRegistry",
        "IncompleteOrUnrestoredReceipt", "InvalidReceiptTime",
        "RegistryDiffersFromReceipt", "ClientOfflineOrUnreachable",
        "ReadDeniedOrInvalidEvidence"
    ];
    internal bool IsObservedVerified =>
        Status == "Verified" && Reason == "RemoteFootprintMatchesPostCleanupReceipt" &&
        ReceiptValid && TasksGone && LocalFolderGone &&
        OwnerReleased && RegistryRestored;
    internal string DisplayState => Status == "Verified"
        ? "Observed clean (not a signed attestation)" : Status;

    internal static IReadOnlyList<PostCleanupObservation> ParseBatch(
        JsonElement data, string campaignId, string gpoId,
        IEnumerable<string> expectedComputers, DateTimeOffset nowUtc)
    {
        if (!Guid.TryParse(gpoId, out _) || data.ValueKind != JsonValueKind.Object ||
            data.GetProperty("campaignId").GetString() != campaignId ||
            !Guid.TryParse(data.GetProperty("gpoId").GetString(), out var returned) ||
            returned != Guid.Parse(gpoId))
            throw new InvalidDataException("Cleanup evidence campaign/GPO identity mismatch.");
        string[] expected = DeviceDashboardModel.ValidateAndNormalizeAdNames(expectedComputers).ToArray();
        if (expected.Length is < 1 or > 25) throw new InvalidDataException("Choose up to 25 scoped clients.");
        var allow = new HashSet<string>(expected, StringComparer.OrdinalIgnoreCase);
        var observed = new List<PostCleanupObservation>();
        var returnedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = data.GetProperty("results");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != expected.Length)
            throw new InvalidDataException("Cleanup evidence must include every requested AD computer.");
        foreach (JsonElement e in items.EnumerateArray())
        {
            string host = e.GetProperty("computer").GetString() ?? "";
            string id = e.GetProperty("campaignId").GetString() ?? "";
            string status = e.GetProperty("status").GetString() ?? "";
            string reason = e.GetProperty("reason").GetString() ?? "";
            DateTimeOffset time = e.GetProperty("observedUtc").GetDateTimeOffset();
            if (!allow.Contains(host) || !returnedHosts.Add(host) || id != campaignId ||
                !AllowedStatuses.Contains(status, StringComparer.Ordinal) ||
                !AllowedReasons.Contains(reason, StringComparer.Ordinal) ||
                time < nowUtc.AddMinutes(-30) || time > nowUtc.AddMinutes(5))
                throw new InvalidDataException("Remote cleanup evidence failed identity/status/time validation.");

            var row = new PostCleanupObservation(host, id, time, status, reason,
                e.GetProperty("receiptValid").GetBoolean(),
                e.GetProperty("tasksGone").GetBoolean(),
                e.GetProperty("localFolderGone").GetBoolean(),
                e.GetProperty("ownerReleased").GetBoolean(),
                e.GetProperty("registryRestored").GetBoolean());
            if (status == "Verified" && !row.IsObservedVerified)
                throw new InvalidDataException("Endpoint claimed verified without all independent checks.");
            if (status != "Verified" && row.IsObservedVerified)
                throw new InvalidDataException("Contradictory cleanup evidence status.");
            observed.Add(row);
        }
        return observed.OrderBy(x => x.Computer, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

/// <summary>
/// Local durable last-observation cache, not a queue of remote RPC actions.
/// Offline/Unknown devices remain for later EXPLICIT manual reinspection.
/// </summary>
internal sealed record PostCleanupScope(
    string Domain, string Dc, string OuDn, string GroupDn, string CampaignId, string GpoId)
{
    internal string NormalizedKey =>
        string.Join("|", Domain.ToLowerInvariant(), Dc.ToLowerInvariant(),
            OuDn.ToLowerInvariant(), GroupDn.ToLowerInvariant(),
            CampaignId, Guid.Parse(GpoId).ToString("D"));

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Domain) || string.IsNullOrWhiteSpace(Dc) ||
            string.IsNullOrWhiteSpace(OuDn) || string.IsNullOrWhiteSpace(GroupDn) ||
            !Guid.TryParse(GpoId, out _) ||
            CampaignId.Length is < 1 or > 72 ||
            !System.Text.RegularExpressions.Regex.IsMatch(CampaignId,
                @"\A[a-z0-9][a-z0-9_-]{0,70}\z"))
            throw new ArgumentException("Invalid post-cleanup verification scope.");
    }
}

internal static class PostCleanupVerificationStore
{
    private static readonly object Gate = new();
    private const int MaximumBytes = 1_600_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Isolated temp storage is used only by in-process CI SelfTests.
    internal static string? TestRootOverride { get; set; }
    private sealed class Saved
    {
        public Saved() { }
        public string Scope { get; set; } = "";
        public List<PostCleanupObservation> Observations { get; set; } = [];
    }

    internal static string Root => TestRootOverride ??
        Path.Combine(AppLog.DirectoryPath, "PostCleanupVerification");
    private static string PathFor(PostCleanupScope scope)
    {
        scope.Validate();
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(scope.NormalizedKey))).ToLowerInvariant();
        return Path.Combine(Root, hash + ".json");
    }

    private static void AssertSafePath(string file)
    {
        if (Directory.Exists(Root) &&
            (File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Post-cleanup store directory is a reparse point.");
        if (File.Exists(file) &&
            (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Post-cleanup store file is a reparse point.");
    }

    internal static IReadOnlyList<PostCleanupObservation> Load(
        PostCleanupScope scope, IReadOnlyCollection<string> verifiedAdComputers)
    {
        lock (Gate)
        {
            string file = PathFor(scope);
            AssertSafePath(file);
            if (!File.Exists(file)) return [];
            var size = new FileInfo(file).Length;
            if (size is < 8 or > MaximumBytes)
                throw new InvalidDataException("Post-cleanup store has unexpected size.");
            Saved? saved;
            try { saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(file), Json); }
            catch (JsonException ex)
            { throw new InvalidDataException("Post-cleanup cache is malformed.", ex); }
            if (saved is null || saved.Scope != scope.NormalizedKey ||
                saved.Observations is null || saved.Observations.Count > 5000)
                throw new InvalidDataException("Post-cleanup cache scope or format is invalid.");
            var allow = new HashSet<string>(
                DeviceDashboardModel.ValidateAndNormalizeAdNames(verifiedAdComputers),
                StringComparer.OrdinalIgnoreCase);
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in saved.Observations)
            {
                if (item is null || item.CampaignId != scope.CampaignId ||
                    string.IsNullOrWhiteSpace(item.Computer) ||
                    !FeedbackPath.IsValidHost(item.Computer.Split('.')[0]) ||
                    !unique.Add(item.Computer) ||
                    !PostCleanupObservation.AllowedStatuses.Contains(item.Status) ||
                    !PostCleanupObservation.AllowedReasons.Contains(item.Reason) ||
                    (item.Status == "Verified" && !item.IsObservedVerified) ||
                    item.ObservedUtc.Year < 2025 ||
                    item.ObservedUtc > DateTimeOffset.UtcNow.AddMinutes(5))
                    throw new InvalidDataException("Persisted post-cleanup evidence is invalid.");
            }
            // Users removed from the verified OU+group are NOT shown as
            // still-eligible devices. Never expand authorization from cache.
            return saved.Observations.Where(x => allow.Contains(x.Computer))
                .OrderBy(x => x.Computer, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    internal static void Save(PostCleanupScope scope,
        IReadOnlyCollection<string> verifiedAdComputers,
        IReadOnlyList<PostCleanupObservation> updates)
    {
        lock (Gate)
        {
            var eligible = DeviceDashboardModel.ValidateAndNormalizeAdNames(verifiedAdComputers);
            var allow = new HashSet<string>(eligible, StringComparer.OrdinalIgnoreCase);
            if (updates.Count is < 1 or > 25 ||
                updates.Any(x => !allow.Contains(x.Computer) ||
                    x.CampaignId != scope.CampaignId))
                throw new InvalidDataException("Post-cleanup update is outside verified AD scope.");
            var merged = Load(scope, eligible).ToDictionary(
                x => x.Computer, StringComparer.OrdinalIgnoreCase);
            foreach (var item in updates) merged[item.Computer] = item;
            if (merged.Count > 5000)
                throw new InvalidDataException("Too many post-cleanup audit records.");
            Directory.CreateDirectory(Root);
            string file = PathFor(scope);
            AssertSafePath(file);
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Saved
            {
                Scope = scope.NormalizedKey,
                Observations = merged.Values.OrderBy(x => x.Computer,
                    StringComparer.OrdinalIgnoreCase).ToList()
            }, Json));
            if (bytes.Length > MaximumBytes)
                throw new InvalidDataException("Post-cleanup audit cache exceeds size cap.");
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, file, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
