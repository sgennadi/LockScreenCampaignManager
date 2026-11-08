using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// A scoped, unsigned and potentially stale remote Registry Policy CSE snapshot
/// plus an independent SMB file SHA-256 comparison. No claim of a physically
/// displayed image or proof that an arbitrary script/MDM value came from a GPO.
/// </summary>
internal sealed record EndpointRsopSetting(
    string Key, string Name, int Precedence, string GpoId,
    bool Deleted, int ValueType);

internal sealed record EndpointRsopAssessment(
    string Status, string GpoId, int Precedence, string Explanation);

internal sealed record EndpointImageIntegrity(
    string Status, string FileName, string ExpectedSha256, string ActualSha256,
    long Bytes, DateTimeOffset ObservedUtc, string Note)
{
    internal bool FileHashMatches => Status == "HashMatches";
    internal bool ImageDisplayedVerified => false;
}

internal sealed class EndpointSourceEvidence
{
    internal string Computer { get; private init; } = "";
    internal string Domain { get; private init; } = "";
    internal string Dc { get; private init; } = "";
    internal string CampaignId { get; private init; } = "";
    internal string ManagedGpoId { get; private init; } = "";
    internal string RsopStatus { get; private init; } = "Not checked";
    internal DateTimeOffset RsopObservedUtc { get; private init; }
    internal string RsopNote { get; private init; } = "";
    internal IReadOnlyList<EndpointRsopSetting> RsopSettings { get; private init; } = [];
    internal EndpointImageIntegrity Image { get; private init; } =
        new("Not checked", "", "", "", 0, default, "");
    internal bool CurrentRSoPProven => false; // Remote WMI is a cached snapshot.
    internal bool PhysicalImageDisplayedProven => false;
    internal bool IsSignedEvidence => false;

    private static string String(JsonElement e, string name, int max)
    {
        if (!e.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Missing RSoP or image evidence field: " + name);
        string raw = value.GetString() ?? "";
        if (raw.Length > max || raw.IndexOf('\0') >= 0)
            throw new InvalidDataException("Invalid RSoP or image evidence field length.");
        return raw;
    }

    private static DateTimeOffset ReadTime(JsonElement e, DateTimeOffset now)
    {
        var value = e.GetProperty("observedUtc").GetDateTimeOffset().ToUniversalTime();
        if (value > now.AddMinutes(5) || value < now.AddMinutes(-20))
            throw new InvalidDataException("RSoP/image observation timestamp is stale or in future.");
        return value;
    }

    internal static EndpointSourceEvidence Parse(JsonElement data, EffectiveGpoReport report,
        EndpointLockScreenObservation endpoint, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (data.ValueKind != JsonValueKind.Object ||
            !string.Equals(endpoint.Computer, report.Computer, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(endpoint.Domain, report.Domain, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(endpoint.Dc, report.Dc, StringComparison.OrdinalIgnoreCase) ||
            endpoint.CampaignId != report.CampaignId ||
            !string.Equals(endpoint.ManagedGpoId, report.ManagedGpoId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Endpoint scope mismatch for RSoP/image evidence.");
        var rawRsop = data.GetProperty("rsop");
        var rawImage = data.GetProperty("image");
        string status = String(rawRsop, "status", 20);
        if (status is not ("Available" or "Empty" or "Unavailable" or "Skipped"))
            throw new InvalidDataException("Unexpected remote RSoP status.");
        var rsopTime = ReadTime(rawRsop, now);
        string rsopNote = String(rawRsop, "note", 320);
        var settings = rawRsop.GetProperty("settings");
        if (settings.ValueKind != JsonValueKind.Array || settings.GetArrayLength() > 96)
            throw new InvalidDataException("RSoP settings exceeded allowed limit.");
        var policies = new List<EndpointRsopSetting>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in settings.EnumerateArray())
        {
            string key = EndpointLockScreenObservation.NormalizeKey(String(item, "key", 160));
            string name = String(item, "name", 80);
            string gpo = String(item, "gpoId", 36);
            if (gpo.Length > 0 && (!Guid.TryParse(gpo, out var parsed) ||
                parsed == Guid.Empty || parsed.ToString("D") != gpo.ToLowerInvariant()))
                throw new InvalidDataException("RSoP GPO identity must be canonical GUID.");
            int precedence = item.GetProperty("precedence").GetInt32();
            int valueType = item.GetProperty("valueType").GetInt32();
            bool deleted = item.GetProperty("deleted").GetBoolean();
            if (precedence is < 1 or > 10000 || valueType is < 0 or > 255 ||
                !unique.Add(key + "\\" + name + "\\" + precedence + "\\" + gpo))
                throw new InvalidDataException("Ambiguous or duplicate RSoP registry policy record.");
            bool knownPersonalization = key == EndpointLockScreenObservation.NormalizeKey(
                EndpointLockScreenObservation.PersonalizationKey) &&
                name is "LockScreenImage" or "NoLockScreen" or "NoChangingLockScreen";
            bool knownCloud = key == EndpointLockScreenObservation.NormalizeKey(
                EndpointLockScreenObservation.CloudKey) &&
                name is "RotatingLockScreenEnabled" or "DisableWindowsSpotlightFeatures" or
                    "RotatingLockScreenOverlayEnabled" or "ConfigureWindowsSpotlight" or
                    "DisableCloudOptimizedContent";
            if (!knownPersonalization && !knownCloud)
                throw new InvalidDataException("RSoP record refers to an unapproved registry setting.");
            policies.Add(new EndpointRsopSetting(key, name, precedence, gpo, deleted, valueType));
        }
        if ((status != "Available" && policies.Count != 0) ||
            (status == "Available" && policies.Count == 0) ||
            (endpoint.OsStatus != "Windows11" && status != "Skipped"))
            throw new InvalidDataException("Contradictory RSoP status/Windows platform.");
        string imageStatus = String(rawImage, "status", 32);
        if (imageStatus is not ("HashMatches" or "HashDiffers" or "Unknown" or
                "RegistryUnavailable" or "NoObservedImagePath" or "OtherOrUnsupportedPath" or
                "UnsafeImagePath" or "ManifestUnavailable" or "FileUnavailable" or "Skipped"))
            throw new InvalidDataException("Unknown image integrity result.");
        string fileName = String(rawImage, "fileName", 12);
        string expected = String(rawImage, "expectedSha256", 64);
        string actual = String(rawImage, "actualSha256", 64);
        string imageNote = String(rawImage, "note", 320);
        var imageTime = ReadTime(rawImage, now);
        long bytes = rawImage.GetProperty("bytes").GetInt64();
        if (bytes is < 0 or > 33554432 ||
            (fileName.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(
                fileName, @"\A[0-9]{2}\.jpg\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) ||
            (expected.Length > 0 && (expected.Length != 64 || !expected.All(Uri.IsHexDigit))) ||
            (actual.Length > 0 && (actual.Length != 64 || !actual.All(Uri.IsHexDigit))) ||
            (imageStatus is "HashMatches" or "HashDiffers" &&
                (fileName.Length == 0 || expected.Length != 64 || actual.Length != 64 ||
                 bytes == 0 || (imageStatus == "HashMatches") !=
                 string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))) ||
            (imageStatus != "HashMatches" && imageStatus != "HashDiffers" &&
                actual.Length != 0) ||
            (endpoint.OsStatus != "Windows11" && imageStatus != "Skipped"))
            throw new InvalidDataException("Contradictory image status or hash evidence.");

        return new EndpointSourceEvidence
        {
            Computer = report.Computer, Domain = report.Domain,
            Dc = report.Dc, CampaignId = report.CampaignId,
            ManagedGpoId = report.ManagedGpoId,
            RsopStatus = status, RsopObservedUtc = rsopTime,
            RsopNote = rsopNote, RsopSettings = policies,
            Image = new EndpointImageIntegrity(imageStatus, fileName, expected,
                actual, bytes, imageTime, imageNote)
        };
    }

    internal EndpointRsopAssessment Assessment(string key, string name)
    {
        if (RsopStatus != "Available")
            return new(RsopStatus, "", 0,
                "Computer Registry Policy CSE is " + RsopStatus +
                "; this is NOT proof the setting is absent or not applied.");
        var matches = RsopSettings.Where(x =>
            x.Key == EndpointLockScreenObservation.NormalizeKey(key) &&
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
            return new("Not reported", "", 0,
                "This setting was not reported in the queried Registry Policy CSE snapshot.");
        var winners = matches.Where(x => x.Precedence == 1).ToArray();
        if (winners.Length != 1)
            return new("Ambiguous", "", 0,
                "CSE reported zero or multiple precedence=1 rows; no source is assigned.");
        var winning = winners[0];
        if (winning.Deleted)
            return new("Deleted setting", "", 1,
                "Registry Policy CSE reports a delete action, not a displayed image.");
        if (winning.GpoId.Length == 0)
            return new("Unknown GPO ID", "", 1,
                "Precedence=1 is reported but the source GUID could not be safely identified.");
        return new("CSE reported winner (cached)", winning.GpoId, 1,
            "Precedence=1 identifies a Registry Policy CSE winner in the cached client RSoP; " +
            "not proof of current HKLM state or visible lock screen.");
    }
}

internal sealed record EndpointEvidenceBundle(
    EndpointLockScreenObservation Registry,
    EndpointSourceEvidence Source);
