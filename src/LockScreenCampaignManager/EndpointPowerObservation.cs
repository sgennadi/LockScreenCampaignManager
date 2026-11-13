using System.Globalization;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Validated, untrusted and short-lived endpoint power readings. Actual active
/// plan / HKLM values are observations, never winning-GPO provenance, live
/// display-state attestation, or the logged-on user's HKCU Screen Saver.
/// </summary>
internal sealed record EndpointPowerReading(
    string Kind, string Supply, string Source, string Status, long? Seconds);

internal sealed record EndpointPowerAssessment(
    string Kind, string Supply, string Source, string Status,
    long? Seconds, string Interpretation);

internal sealed class EndpointPowerObservation
{
    internal string Domain { get; private init; } = "";
    internal string Dc { get; private init; } = "";
    internal string OuDn { get; private init; } = "";
    internal string Computer { get; private init; } = "";
    internal DateTimeOffset ObservedUtc { get; private init; }
    internal string PlanStatus { get; private init; } = "";
    internal string PlanName { get; private init; } = "";
    internal string PlanGuid { get; private init; } = "";
    internal string RegistryStatus { get; private init; } = "";
    internal IReadOnlyList<EndpointPowerReading> Readings { get; private init; } = [];
    internal IReadOnlyList<string> Notes { get; private init; } = [];

    internal static EndpointPowerObservation Parse(
        JsonElement data, string domain, string dc, string ouDn,
        string computer, DateTimeOffset now)
    {
        static string Field(JsonElement e, string key, int max = 2048)
        {
            string value = e.GetProperty(key).GetString() ??
                throw new InvalidDataException("Missing power evidence field: " + key);
            if (value.Length > max || value.Any(char.IsControl))
                throw new InvalidDataException("Unsafe power evidence text: " + key);
            return value;
        }
        if (data.ValueKind != JsonValueKind.Object ||
            data.GetProperty("adScopeVerified").ValueKind != JsonValueKind.True ||
            !Field(data,"domain").Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            !Field(data,"dc").Equals(dc, StringComparison.OrdinalIgnoreCase) ||
            !Field(data,"ouDn").Equals(ouDn, StringComparison.OrdinalIgnoreCase) ||
            !Field(data,"computer").Equals(computer, StringComparison.OrdinalIgnoreCase) ||
            !data.GetProperty("observedUtc").TryGetDateTimeOffset(out DateTimeOffset observed) ||
            observed.Year < 2020 ||
            observed > now.AddMinutes(2) ||
            now - observed > TimeSpan.FromMinutes(5))
            throw new InvalidDataException("AD endpoint power observation is not current and scoped.");

        string planStatus = Field(data, "activePlanStatus", 40);
        string registryStatus = Field(data, "policyRegistryStatus", 40);
        if (planStatus is not ("Available" or "Partial" or "Unavailable") ||
            registryStatus is not ("Readable" or "Partial" or "Unavailable"))
            throw new InvalidDataException("Unexpected endpoint power state.");
        string planGuid = Field(data, "activePlanGuid", 40);
        if ((planStatus == "Unavailable") != (planGuid.Length == 0) ||
            planGuid.Length > 0 && !Guid.TryParseExact(planGuid,"D",out _))
            throw new InvalidDataException("Invalid active power-plan identifier.");
        var readings = new List<EndpointPowerReading>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = data.GetProperty("values");
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 10)
            throw new InvalidDataException("Unbounded endpoint power values.");
        foreach (JsonElement value in values.EnumerateArray())
        {
            string kind = Field(value,"kind",30);
            string supply = Field(value,"supply",8);
            string source = Field(value,"source",30);
            string status = Field(value,"status",20);
            if (source is not ("ActivePlanWmi" or "PolicyRegistry") ||
                kind is not ("Display" or "Sleep" or "MachineLock") ||
                (kind == "MachineLock") != (supply == "Any") ||
                kind == "MachineLock" && source != "PolicyRegistry" ||
                kind != "MachineLock" && supply is not ("AC" or "DC") ||
                status is not ("Present" or "Absent" or "Unreadable" or "Unavailable") ||
                source == "ActivePlanWmi" && status is not ("Present" or "Unavailable") ||
                source == "PolicyRegistry" && status == "Unavailable")
                throw new InvalidDataException("Invalid endpoint power reading.");
            var raw = value.GetProperty("seconds");
            long? seconds = raw.ValueKind == JsonValueKind.Null ? null : raw.GetInt64();
            if ((status == "Present") != seconds.HasValue ||
                seconds is < 0 or > 4294967295)
                throw new InvalidDataException("Incomplete power timeout value.");
            if (!ids.Add(source+"|"+kind+"|"+supply))
                throw new InvalidDataException("Duplicate endpoint power reading.");
            readings.Add(new EndpointPowerReading(kind,supply,source,status,seconds));
        }
        if (planStatus != "Unavailable" &&
            readings.Count(v=>v.Source=="ActivePlanWmi") != 4)
            throw new InvalidDataException("Incomplete active-plan WMI evidence.");
        if (registryStatus != "Unavailable" &&
            readings.Count(v=>v.Source=="PolicyRegistry") != 5)
            throw new InvalidDataException("Incomplete policy registry evidence.");
        var noteData = data.GetProperty("notes");
        if (noteData.ValueKind != JsonValueKind.Array || noteData.GetArrayLength() > 5)
            throw new InvalidDataException("Unbounded endpoint evidence notes.");
        var notes = noteData.EnumerateArray().Select(v => v.GetString() ?? "")
            .Select(x=>x.Length > 550 ? x[..550] : x)
            .Where(x=>!x.Any(char.IsControl)).ToArray();
        return new EndpointPowerObservation
        {
            Domain=domain, Dc=dc, OuDn=ouDn, Computer=computer,
            ObservedUtc=observed.ToUniversalTime(),
            PlanStatus=planStatus, RegistryStatus=registryStatus,
            PlanName=Field(data,"activePlanName",100), PlanGuid=planGuid,
            Readings=readings, Notes=notes
        };
    }

    internal IReadOnlyList<EndpointPowerAssessment> Assess(int saverIdleSeconds)
    {
        if (saverIdleSeconds is < 60 or > 86400)
            throw new ArgumentOutOfRangeException(nameof(saverIdleSeconds));
        return Readings.Select(value => {
            string condition;
            if (value.Status != "Present" || value.Seconds is not long time)
                condition = value.Status == "Absent"
                    ? "Not configured here (other sources remain unknown)"
                    : "Not verified";
            else if (time == 0)
                condition = "Never (this setting only)";
            else if (time < saverIdleSeconds)
                condition = value.Kind == "MachineLock"
                    ? "Machine lock before configured saver — review"
                    : "Observed shorter timeout — potential blocker";
            else if (time == saverIdleSeconds)
                condition = "Same threshold — order not verified";
            else condition = "Later than configured saver (this setting only)";
            return new EndpointPowerAssessment(
                value.Kind, value.Supply, value.Source, value.Status,
                value.Seconds, condition);
        }).OrderBy(x=>x.Kind).ThenBy(x=>x.Supply).ThenBy(x=>x.Source).ToArray();
    }

    internal int PotentialEarly(int saverSeconds) =>
        Assess(saverSeconds).Count(x=>x.Interpretation.Contains("potential blocker",
            StringComparison.Ordinal));
    internal bool HasFullyVerifiedVisibility => false;
}
