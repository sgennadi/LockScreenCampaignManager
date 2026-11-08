using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Explicit read-only remote native-64 HKLM observation from the AD-scoped
/// computer. Registry equality is NOT a verified originating GPO or proof that
/// the image is displayed on a physical lock screen.
/// </summary>
internal sealed record EndpointLockScreenValue(
    string Key, string Name, string Status, string Kind, string Value);

internal sealed class EndpointLockScreenObservation
{
    internal string Computer { get; private init; } = "";
    internal string Domain { get; private init; } = "";
    internal string Dc { get; private init; } = "";
    internal string CampaignId { get; private init; } = "";
    internal string ManagedGpoId { get; private init; } = "";
    internal string ActualComputerContainer { get; private init; } = "";
    internal DateTimeOffset ObservedUtc { get; private init; }
    internal string RegistryStatus { get; private init; } = "Unavailable";
    internal string OsStatus { get; private init; } = "Unknown";
    internal string OsBuild { get; private init; } = "";
    internal string Note { get; private init; } = "";
    internal IReadOnlyList<EndpointLockScreenValue> Values { get; private init; } = [];
    internal bool IsVerifiedGpoSource => false;
    internal bool IsPhysicalLockScreenVerified => false;

    internal const string PersonalizationKey =
        @"SOFTWARE\Policies\Microsoft\Windows\Personalization";
    internal const string CloudKey =
        @"SOFTWARE\Policies\Microsoft\Windows\CloudContent";
    internal const string CspKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";

    private static readonly IReadOnlyDictionary<string, string[]> AllowedValues =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [PersonalizationKey] = ["LockScreenImage", "NoChangingLockScreen", "NoLockScreen"],
            [CloudKey] = ["RotatingLockScreenEnabled", "DisableWindowsSpotlightFeatures",
                "RotatingLockScreenOverlayEnabled", "ConfigureWindowsSpotlight",
                "DisableCloudOptimizedContent"],
            [CspKey] = ["LockScreenImagePath", "LockScreenImageUrl", "LockScreenImageStatus"]
        };

    internal static string NormalizeKey(string key)
    {
        string result = key.Replace('/', '\\').Trim().TrimStart('\\');
        foreach (string prefix in new[] { "HKEY_LOCAL_MACHINE\\", "HKLM\\" })
            if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                result = result[prefix.Length..];
        return result.ToUpperInvariant();
    }

    private static string Text(JsonElement element, string property, int max)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Endpoint snapshot is missing a required string.");
        string text = value.GetString() ?? "";
        if (text.Length > max || text.IndexOf('\0') >= 0)
            throw new InvalidDataException("Endpoint snapshot field is invalid or oversized.");
        return text;
    }

    internal static EndpointLockScreenObservation Parse(JsonElement data,
        EffectiveGpoReport report, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (data.ValueKind != JsonValueKind.Object ||
            !string.Equals(Text(data, "domain", 253), report.Domain,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Text(data, "dc", 253), report.Dc,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Text(data, "targetOu", 1024), report.OuDn,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Text(data, "groupDn", 1024), report.GroupDn,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Text(data, "computer", 253), report.Computer,
                StringComparison.OrdinalIgnoreCase) ||
            Text(data, "campaignId", 72) != report.CampaignId ||
            !Guid.TryParse(Text(data, "managedGpoId", 36), out var actualId) ||
            actualId != Guid.Parse(report.ManagedGpoId) ||
            !string.Equals(Text(data, "actualComputerContainer", 1024),
                report.ActualComputerContainer, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Endpoint observation scope does not match the selected AD computer/campaign.");

        var observed = data.GetProperty("observedUtc").GetDateTimeOffset();
        if (observed > now.AddMinutes(5) || observed < now.AddMinutes(-20))
            throw new InvalidDataException("Endpoint observation is stale or in the future.");
        string registryStatus = Text(data, "registryStatus", 20);
        string osStatus = Text(data, "osStatus", 20);
        string build = Text(data, "osBuild", 12);
        string note = Text(data, "note", 320);
        if (registryStatus is not ("Readable" or "Partial" or "Unavailable") ||
            osStatus is not ("Windows11" or "OtherWindows" or "Unknown") ||
            (build.Length > 0 && (!build.All(char.IsAsciiDigit) ||
                !int.TryParse(build, out _))) ||
            (osStatus == "Windows11" &&
                (build.Length == 0 || !int.TryParse(build, out int buildNumber) ||
                 buildNumber < 22000)))
            throw new InvalidDataException("Endpoint snapshot status and OS metadata are invalid.");

        var raw = data.GetProperty("values");
        if (raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() > 11)
            throw new InvalidDataException("Endpoint snapshot contains excessive registry values.");
        var values = new List<EndpointLockScreenValue>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in raw.EnumerateArray())
        {
            string key = Text(item, "key", 160);
            string name = Text(item, "name", 80);
            string status = Text(item, "status", 20);
            string kind = Text(item, "kind", 20);
            string value = Text(item, "value", 1024);
            if (!AllowedValues.TryGetValue(key, out var allowed) ||
                !allowed.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                !keys.Add(NormalizeKey(key) + "\\" + name) ||
                status is not ("Present" or "Absent" or "Unreadable" or "Unsupported" or "Oversized") ||
                ((status == "Present") != (kind is "String" or "ExpandString" or "DWord" or "QWord")) ||
                (status == "Present" && (kind == "DWord" || kind == "QWord") &&
                    !long.TryParse(value, out _) && !ulong.TryParse(value, out _)) ||
                (status != "Present" && value.Length != 0) ||
                ((status is "Absent" or "Unreadable" or "Oversized") && kind != "Unknown") ||
                (status == "Unsupported" && kind != "Other"))
                throw new InvalidDataException("Endpoint registry item is unknown, duplicate or contradictory.");
            values.Add(new EndpointLockScreenValue(key, name, status, kind, value));
        }
        if (registryStatus == "Unavailable")
        {
            if (values.Count != 0 || osStatus != "Unknown" || build.Length != 0)
                throw new InvalidDataException("Unavailable endpoint cannot report observed values.");
        }
        else
        {
            if (values.Count != AllowedValues.Sum(x => x.Value.Length) ||
                AllowedValues.Any(spec => spec.Value.Any(name =>
                    !keys.Contains(NormalizeKey(spec.Key) + "\\" + name))))
                throw new InvalidDataException("Remote registry inventory is incomplete.");
            if (registryStatus == "Readable" &&
                values.Any(v => v.Status is "Unreadable" or "Oversized"))
                throw new InvalidDataException("Unreadable values cannot be reported as a complete snapshot.");
        }
        return new EndpointLockScreenObservation
        {
            Computer = report.Computer, Domain = report.Domain,
            Dc = report.Dc, CampaignId = report.CampaignId,
            ManagedGpoId = report.ManagedGpoId,
            ActualComputerContainer = report.ActualComputerContainer,
            ObservedUtc = observed.ToUniversalTime(),
            RegistryStatus = registryStatus, OsStatus = osStatus,
            OsBuild = build, Note = note,
            Values = values
        };
    }

    internal EndpointLockScreenValue? Find(string key, string name) =>
        Values.SingleOrDefault(x =>
            NormalizeKey(x.Key) == NormalizeKey(key) &&
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
}
