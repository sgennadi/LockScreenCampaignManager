using System.Globalization;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Aggregate-only, READ-ONLY estimate based on a selected DC's RootDSE UTC time
/// and replicated lastLogonTimestamp. This is NOT a live heartbeat, exact
/// logon time, GPO security filter, or list of eligible deployment targets.
/// </summary>
internal sealed class OuComputerActivityAccumulator
{
    private readonly DateTimeOffset? _dcUtc;
    private readonly Dictionary<string, int[]> _recentByGroup =
        new(StringComparer.OrdinalIgnoreCase);

    internal OuComputerActivityAccumulator(string dcCurrentTime)
    {
        if (DateTimeOffset.TryParseExact(dcCurrentTime,
                ["yyyyMMddHHmmss'.0Z'", "yyyyMMddHHmmss'Z'"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed) &&
            parsed.Year >= 2020 && parsed <= DateTimeOffset.UtcNow.AddDays(2))
            _dcUtc = parsed.ToUniversalTime();
    }

    internal int Enabled { get; private set; }
    internal int Disabled { get; private set; }
    internal int UnknownState { get; private set; }
    internal int UnknownTimestamp { get; private set; }
    private readonly int[] _recent = new int[4];

    internal void Record(string userAccountControl, string lastLogonTimestamp,
        IEnumerable<string> uniqueDirectGroupDns)
    {
        // No trusted DC clock -> no classification, not a false "zero active".
        if (_dcUtc is null) return;
        if (!int.TryParse(userAccountControl, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int flags) || flags < 0)
        {
            UnknownState++;
            return;
        }
        if ((flags & 0x2) != 0)
        {
            Disabled++;
            return;
        }
        Enabled++;
        if (!long.TryParse(lastLogonTimestamp, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long fileTime) || fileTime <= 0)
        {
            UnknownTimestamp++;
            return;
        }
        DateTimeOffset loginUtc;
        try { loginUtc = DateTimeOffset.FromFileTime(fileTime); }
        catch (ArgumentOutOfRangeException)
        {
            UnknownTimestamp++;
            return;
        }
        if (loginUtc.Year < 2000 || loginUtc > _dcUtc.Value.AddHours(24))
        {
            UnknownTimestamp++;
            return;
        }

        int[] days = [30, 90, 180, 365];
        bool[] isRecent = days.Select(day =>
            loginUtc >= _dcUtc.Value.AddDays(-day)).ToArray();
        for (int i = 0; i < isRecent.Length; i++)
            if (isRecent[i]) _recent[i]++;

        foreach (string groupDn in uniqueDirectGroupDns)
        {
            if (!_recentByGroup.TryGetValue(groupDn, out int[]? values))
                _recentByGroup[groupDn] = values = new int[4];
            for (int i = 0; i < isRecent.Length; i++)
                if (isRecent[i]) values[i]++;
        }
    }

    internal object? Export(IReadOnlyDictionary<string, int> directGroupCounts)
    {
        if (_dcUtc is null) return null;
        return new
        {
            basis = "replicatedLastLogonTimestamp",
            observedUtc = _dcUtc.Value.ToString("O", CultureInfo.InvariantCulture),
            enabled = Enabled,
            disabled = Disabled,
            unknownState = UnknownState,
            unknownTimestamp = UnknownTimestamp,
            last30 = _recent[0],
            last90 = _recent[1],
            last180 = _recent[2],
            last365 = _recent[3],
            groupRecentCounts = directGroupCounts.Keys
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(dn => {
                    var n = _recentByGroup.GetValueOrDefault(dn) ?? new int[4];
                    return new { dn, last30 = n[0], last90 = n[1],
                        last180 = n[2], last365 = n[3] };
                }).ToArray()
        };
    }
}

/// <summary>
/// Fail-closed validation of AD activity metadata. Counts contain no host or
/// user identities and cannot grant permission or change target OU/group.
/// </summary>
internal sealed class OuComputerActivityCoverage
{
    private readonly Dictionary<string, int[]> _groupRecent;
    private readonly int[] _recent;

    private OuComputerActivityCoverage(int enabled, int disabled, int unknownState,
        int unknownTimestamp, int[] recent, Dictionary<string, int[]> groupRecent)
    {
        Enabled = enabled;
        Disabled = disabled;
        UnknownState = unknownState;
        UnknownTimestamp = unknownTimestamp;
        _recent = recent;
        _groupRecent = groupRecent;
    }

    internal int Enabled { get; }
    internal int Disabled { get; }
    internal int UnknownState { get; }
    internal int UnknownTimestamp { get; }
    internal int Recent(int days) => _recent[Index(days)];

    internal int GroupRecent(string? groupDn, int days) =>
        !string.IsNullOrWhiteSpace(groupDn) &&
        _groupRecent.TryGetValue(groupDn, out int[]? values)
            ? values[Index(days)] : 0;

    private static int Index(int days) => days switch
    {
        30 => 0, 90 => 1, 180 => 2, 365 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(days))
    };

    internal static OuComputerActivityCoverage Parse(JsonElement data,
        int principals, IReadOnlyDictionary<string, int> directGroupCounts)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            data.GetProperty("basis").GetString() != "replicatedLastLogonTimestamp" ||
            !DateTimeOffset.TryParse(data.GetProperty("observedUtc").GetString(),
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                out DateTimeOffset observed) ||
            observed.Year < 2020 || observed > DateTimeOffset.UtcNow.AddDays(2))
            throw new InvalidDataException("AD computer activity observation is not verified.");

        int enabled = data.GetProperty("enabled").GetInt32();
        int disabled = data.GetProperty("disabled").GetInt32();
        int unknownState = data.GetProperty("unknownState").GetInt32();
        int unknownTimestamp = data.GetProperty("unknownTimestamp").GetInt32();
        int[] recent =
        [
            data.GetProperty("last30").GetInt32(),
            data.GetProperty("last90").GetInt32(),
            data.GetProperty("last180").GetInt32(),
            data.GetProperty("last365").GetInt32()
        ];
        if (enabled < 0 || disabled < 0 || unknownState < 0 ||
            unknownTimestamp < 0 || unknownTimestamp > enabled ||
            (long)enabled + disabled + unknownState != principals ||
            !ValidRecent(recent, enabled - unknownTimestamp))
            throw new InvalidDataException("AD computer activity totals are inconsistent.");

        var byGroup = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement row in data.GetProperty("groupRecentCounts").EnumerateArray())
        {
            string? dn = row.GetProperty("dn").GetString();
            if (string.IsNullOrWhiteSpace(dn) ||
                !directGroupCounts.TryGetValue(dn, out int groupTotal))
                throw new InvalidDataException("Unrecognized activity group DN.");
            int[] values =
            [
                row.GetProperty("last30").GetInt32(),
                row.GetProperty("last90").GetInt32(),
                row.GetProperty("last180").GetInt32(),
                row.GetProperty("last365").GetInt32()
            ];
            if (!ValidRecent(values, groupTotal) ||
                values.Where((value, index) => value > recent[index]).Any() ||
                !byGroup.TryAdd(dn, values))
                throw new InvalidDataException("Duplicate or inconsistent group activity counts.");
        }
        if (byGroup.Count != directGroupCounts.Count)
            throw new InvalidDataException("Incomplete per-group AD activity counts.");

        return new OuComputerActivityCoverage(enabled, disabled,
            unknownState, unknownTimestamp, recent, byGroup);
    }

    private static bool ValidRecent(int[] values, int upperBound) =>
        values.Length == 4 && values[0] >= 0 &&
        values[0] <= values[1] && values[1] <= values[2] &&
        values[2] <= values[3] && values[3] <= upperBound;
}
