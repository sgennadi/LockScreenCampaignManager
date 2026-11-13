using System.Globalization;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// One explicitly selected DC/OU/nested-group Inventory result. These are
/// directory hints for diagnostics, not an effective GPO authorization token.
/// An empty verified group is different from "AD was not loaded".
/// </summary>
internal sealed class ClientHealthAdInventory
{
    internal sealed record Computer(string Host, DateTimeOffset? AdLastSeenUtc, bool? Disabled);

    private readonly Dictionary<string, Computer> _members;
    private ClientHealthAdInventory(int ouTotal, Dictionary<string, Computer> members,
        DateTimeOffset observedUtc, DateTimeOffset dcUtc)
    {
        OuTotal = ouTotal;
        _members = members;
        ObservedUtc = observedUtc;
        DcUtc = dcUtc;
    }

    internal int OuTotal { get; }
    internal int GroupTotal => _members.Count;
    internal DateTimeOffset ObservedUtc { get; }
    internal DateTimeOffset DcUtc { get; }
    internal IReadOnlyCollection<string> ExpectedHosts => _members.Keys;

    internal bool IsFresh(DateTimeOffset now) =>
        now >= ObservedUtc.AddMinutes(-2) && now - ObservedUtc <= TimeSpan.FromMinutes(20);

    internal static ClientHealthAdInventory Parse(JsonElement data,
        string expectedDomain, DateTimeOffset observedUtc)
    {
        if (observedUtc.Year < 2020 ||
            observedUtc > DateTimeOffset.UtcNow.AddMinutes(5) ||
            !data.TryGetProperty("activitySource", out var source) ||
            source.GetString() != "lastLogonTimestamp (replicated; approximate, not online presence)" ||
            !data.TryGetProperty("dcCurrentTime", out var clock) ||
            !TryParseDcClock(clock.GetString(), out DateTimeOffset dcUtc) ||
            dcUtc > observedUtc.AddHours(24) || dcUtc < observedUtc.AddHours(-24))
            throw new InvalidDataException("AD inventory source or DC clock was not verified.");

        int ouTotal = data.GetProperty("ouTotal").GetInt32();
        int groupTotal = data.GetProperty("groupEligible").GetInt32();
        var items = data.GetProperty("computers");
        if (items.ValueKind != JsonValueKind.Array ||
            ouTotal is < 0 or > 5000 || groupTotal is < 0 or > 2000 ||
            groupTotal > ouTotal || items.GetArrayLength() != groupTotal)
            throw new InvalidDataException("Incomplete OU/group inventory counts.");

        var members = new Dictionary<string, Computer>(StringComparer.OrdinalIgnoreCase);
        string suffix = "." + expectedDomain.Trim().ToLowerInvariant();
        if (suffix.Length < 5 || suffix.Length > 254)
            throw new InvalidDataException("Invalid domain for AD inventory.");
        foreach (JsonElement item in items.EnumerateArray())
        {
            string? fqdn = item.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(fqdn) ||
                !NativeImmediateActions.ValidHost(fqdn, expectedDomain) ||
                !fqdn.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A computer outside the selected domain was returned.");

            string shortName = fqdn[..^suffix.Length];
            if (!FeedbackPath.IsValidHost(shortName) || shortName.Length > 63)
                throw new InvalidDataException("Invalid short computer name.");
            var disabled = item.GetProperty("adDisabled");
            if (disabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
                throw new InvalidDataException("Invalid AD account state.");
            bool? isDisabled = disabled.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
            var seen = item.GetProperty("adLastSeenUtc");
            DateTimeOffset? lastSeen = null;
            if (seen.ValueKind == JsonValueKind.String)
            {
                if (!seen.TryGetDateTimeOffset(out DateTimeOffset parsed) ||
                    parsed.Year < 2000 || parsed > dcUtc.AddDays(2))
                    throw new InvalidDataException("Invalid replicated logon timestamp.");
                lastSeen = parsed.ToUniversalTime();
            }
            else if (seen.ValueKind != JsonValueKind.Null)
                throw new InvalidDataException("Invalid AD last-seen value.");

            if (!members.TryAdd(shortName, new Computer(
                    shortName.ToUpperInvariant(), lastSeen, isDisabled)))
                throw new InvalidDataException("Duplicate AD computer identity in scope.");
        }

        return new ClientHealthAdInventory(ouTotal, members, observedUtc, dcUtc);
    }

    private static bool TryParseDcClock(string? raw, out DateTimeOffset time) =>
        DateTimeOffset.TryParseExact(raw,
            ["yyyyMMddHHmmss'.0Z'", "yyyyMMddHHmmss'Z'"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time) &&
        time.Year >= 2020 && time <= DateTimeOffset.UtcNow.AddDays(2);

    internal ClientHealthCoverageSummary Summarize(ClientHealthSnapshot? feedback,
        string campaignId, int days, DateTimeOffset now, int staleAfterMinutes)
    {
        if (days is not (30 or 90 or 180 or 365) ||
            staleAfterMinutes is < 5 or > 180)
            throw new ArgumentOutOfRangeException(nameof(days));

        int enabled = 0, disabled = 0, unknown = 0, unknownTimestamp = 0, recent = 0;
        foreach (var pc in _members.Values)
        {
            if (pc.Disabled == true) { disabled++; continue; }
            if (pc.Disabled is null) { unknown++; continue; }
            enabled++;
            if (pc.AdLastSeenUtc is not DateTimeOffset time)
                unknownTimestamp++;
            else if (time >= DcUtc.AddDays(-days)) recent++;
        }

        bool reportsVerified = IsFresh(now) && feedback is not null &&
            string.Equals(feedback.CampaignId, campaignId, StringComparison.Ordinal) &&
            now >= feedback.ObservedUtc.AddMinutes(-2) &&
            now - feedback.ObservedUtc <= TimeSpan.FromMinutes(5) &&
            feedback.Expected == _members.Count;

        int reports = 0, fresh = 0;
        if (reportsVerified)
        {
            foreach (var name in _members.Keys)
            {
                if (!feedback!.LatestReports.TryGetValue(name, out var record)) continue;
                if (!string.Equals(record.CampaignId, campaignId, StringComparison.Ordinal))
                    continue;
                reports++;
                // Completed may be terminal in the campaign, but it is not a
                // current heartbeat just because the last stage is Completed.
                if (record.LastSeenUtc <= now.AddMinutes(5) &&
                    now - record.LastSeenUtc <= TimeSpan.FromMinutes(staleAfterMinutes))
                    fresh++;
            }
        }

        return new ClientHealthCoverageSummary(OuTotal, _members.Count, enabled,
            disabled, unknown, unknownTimestamp, recent, days,
            reportsVerified, reports, fresh, reportsVerified ? _members.Count - reports : 0,
            IsFresh(now));
    }
}

/// <summary>
/// Read-only intersection of a VERIFIED AD inventory with matching campaign
/// SMB report identities. Reports are untrusted diagnostic hints, not proof of
/// online status; AD lastLogonTimestamp can lag replication by many days.
/// </summary>
internal sealed record ClientHealthCoverageSummary(
    int OuTotal, int GroupTotal, int Enabled, int Disabled,
    int UnknownAccountState, int UnknownLogon, int RecentlySeenAd, int Days,
    bool ReportsVerified, int Reported, int FreshReports, int NoReport,
    bool AdScopeFresh)
{
    internal int StaleReports => ReportsVerified ? Reported - FreshReports : 0;
}
