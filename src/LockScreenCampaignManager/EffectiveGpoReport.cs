using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only AD / SYSVOL estimates from the selected DC, never Windows RSoP.
/// This model rejects substituted PCs, GPOs, malformed levels and optimistic
/// categories before a private report can be displayed or exported.
/// </summary>
internal sealed record EffectiveGpoSetting(
    string Source, string Key, string Name, string Value, string Risk, string Reason);

internal sealed record EffectiveGpoPolicy(
    string Name, string Id, string LinkedAt, int LinkOrder, int ScopeDepth, string Site, string Link,
    bool Enforced, bool BlockedBelow, bool MachinePartDisabled,
    string Security, string Wmi, string StaticRead, bool StartupScript,
    string Category, IReadOnlyList<EffectiveGpoSetting> Settings)
{
    internal int HighSettings => Settings.Count(x => x.Risk == "High");
}

internal sealed class EffectiveGpoReport
{
    internal string Domain { get; private init; } = "";
    internal string Dc { get; private init; } = "";
    internal string OuDn { get; private init; } = "";
    internal string GroupDn { get; private init; } = "";
    internal string Computer { get; private init; } = "";
    internal string ActualComputerContainer { get; private init; } = "";
    internal string CampaignId { get; private init; } = "";
    internal string ManagedGpoId { get; private init; } = "";
    internal DateTimeOffset AnalyzedUtc { get; private init; }
    internal string Membership { get; private init; } = "";
    internal string TokenStatus { get; private init; } = "";
    internal string SiteAssociation { get; private init; } = "";
    internal bool SiteInventoryComplete { get; private init; }
    internal IReadOnlyList<EffectiveGpoPolicy> Policies { get; private init; } = [];
    internal IReadOnlyList<string> Warnings { get; private init; } = [];
    internal int PotentialHigh => Policies.Count(p =>
        p.Category == "Potentially applicable" && p.HighSettings > 0);
    internal int NeedsVerification => Policies.Count(p =>
        p.Category is "Needs RSoP / verification" or "Site applicability unknown" or
            "Security deny candidate");
    internal int Excluded => Policies.Count(p => p.Category == "Excluded by link/config");
    internal bool IsConclusiveResultantSetOfPolicy => false;

    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "Excluded by link/config", "Security deny candidate",
        "Needs RSoP / verification", "Site applicability unknown",
        "Potentially applicable"
    };
    private static readonly HashSet<string> SecurityStates = new(StringComparer.Ordinal)
    {
        "Unknown token", "Matching deny ACE", "Read and Apply grants observed",
        "Read/Apply grants not established", "ACL unreadable"
    };
    private static readonly HashSet<string> LinkStates = new(StringComparer.Ordinal)
    {
        "Candidate", "Disabled link", "Computer configuration disabled",
        "Blocked inheritance"
    };

    private static string Value(JsonElement e, string field, int max)
    {
        string s = e.GetProperty(field).GetString() ??
            throw new InvalidDataException("Required GPO analysis field missing.");
        if (s.Length > max)
            throw new InvalidDataException("GPO analysis field exceeds size bound.");
        return s;
    }

    internal static EffectiveGpoReport Parse(JsonElement payload,
        string domain, string dc, string ouDn, string groupDn,
        string computer, string campaign, string managedGpo,
        DateTimeOffset now)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !Guid.TryParse(managedGpo, out var expectedGuid) ||
            expectedGuid == Guid.Empty ||
            !string.Equals(Value(payload, "domain", 253), domain,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Value(payload, "dc", 253), dc,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Value(payload, "targetOu", 1024), ouDn,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Value(payload, "groupDn", 1024), groupDn,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Value(payload, "computer", 253), computer,
                StringComparison.OrdinalIgnoreCase) ||
            Value(payload, "campaignId", 72) != campaign ||
            !Guid.TryParse(Value(payload, "managedGpoId", 36), out var actualGuid) ||
            actualGuid != expectedGuid)
            throw new InvalidDataException(
                "Effective GPO analysis response does not match selected AD computer/campaign.");

        string actualContainer = Value(payload, "actualComputerContainer", 1024);
        if (!actualContainer.Equals(ouDn, StringComparison.OrdinalIgnoreCase) &&
            !actualContainer.EndsWith("," + ouDn, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selected computer container does not belong to target OU.");
        var observed = payload.GetProperty("analyzedUtc").GetDateTimeOffset();
        if (observed > now.AddMinutes(5) || observed < now.AddMinutes(-20))
            throw new InvalidDataException("GPO analysis is stale or has invalid time.");
        string membership = Value(payload, "membership", 20);
        string token = Value(payload, "tokenStatus", 20);
        string site = Value(payload, "siteAssociation", 24);
        if (membership is not ("Nested/direct" or "Primary") ||
            token is not ("Read" or "Unknown" or "Unavailable") || site != "Unknown")
            throw new InvalidDataException(
                "AD computer membership or security token not established.");

        var raw = payload.GetProperty("policies");
        if (raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() > 305)
            throw new InvalidDataException("Invalid GPO candidate inventory.");
        var policies = new List<EffectiveGpoPolicy>();
        foreach (var e in raw.EnumerateArray())
        {
            string name = Value(e, "gpo", 256);
            string id = Value(e, "gpoId", 36);
            if (!Guid.TryParse(id, out var policyGuid) || policyGuid == Guid.Empty)
                throw new InvalidDataException("GPO candidate has an invalid GUID.");
            string linkedAt = Value(e, "linkedAt", 1024);
            string siteDn = Value(e, "site", 1024);
            string link = Value(e, "link", 50);
            int order = e.GetProperty("linkOrder").GetInt32();
            int depth = e.GetProperty("scopeDepth").GetInt32();
            if (order is < 1 or > 120 || (siteDn.Length == 0 && (depth < 0 || depth > 64)) ||
                (siteDn.Length != 0 && depth != -1) ||
                (siteDn.Length == 0 &&
                    !actualContainer.Equals(linkedAt, StringComparison.OrdinalIgnoreCase) &&
                    !actualContainer.EndsWith("," + linkedAt, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Invalid GPMC link order, OU depth or link ancestry.");
            string security = Value(e, "security", 70);
            string wmi = Value(e, "wmi", 45);
            string staticRead = Value(e, "staticRead", 40);
            string category = Value(e, "category", 64);
            bool machineOff = e.GetProperty("machinePartDisabled").GetBoolean();
            bool enforced = e.GetProperty("enforced").GetBoolean();
            bool blocked = e.GetProperty("blockedBelow").GetBoolean();
            bool dynamicScript = e.GetProperty("startupScript").GetBoolean();
            if (!Categories.Contains(category) || !SecurityStates.Contains(security) ||
                !LinkStates.Contains(link) ||
                wmi is not ("None" or "Present - not evaluated") ||
                staticRead is not ("Read" or "Unavailable" or "Partial or unreadable") ||
                !linkedAt.EndsWith("DC=" + domain.Replace(".", ",DC="),
                    StringComparison.OrdinalIgnoreCase) ||
                (siteDn.Length != 0 && siteDn != linkedAt) ||
                (siteDn.Length != 0 && category == "Potentially applicable") ||
                (siteDn.Length == 0 && category == "Site applicability unknown") ||
                (link != "Candidate" && category != "Excluded by link/config") ||
                (link == "Candidate" && category == "Excluded by link/config") ||
                (category == "Security deny candidate" &&
                    security != "Matching deny ACE") ||
                (machineOff && category == "Potentially applicable") ||
                (security != "Read and Apply grants observed" &&
                 category == "Potentially applicable") ||
                (staticRead != "Read" && category == "Potentially applicable") ||
                (wmi != "None" && category == "Potentially applicable") ||
                (dynamicScript && category == "Potentially applicable"))
                throw new InvalidDataException("Contradictory or out-of-scope GPO candidate data.");

            JsonElement settingsArray = e.GetProperty("settings");
            if (settingsArray.ValueKind != JsonValueKind.Array ||
                settingsArray.GetArrayLength() > 120)
                throw new InvalidDataException("Excessive GPO settings.");
            var settings = new List<EffectiveGpoSetting>();
            foreach (var item in settingsArray.EnumerateArray())
            {
                var src = Value(item, "source", 90);
                var risk = Value(item, "risk", 16);
                if (src is not ("Registry.pol" or @"Preferences\Registry\Registry.xml") ||
                    risk is not ("High" or "Warning" or "Info"))
                    throw new InvalidDataException("Unexpected GPO policy setting.");
                settings.Add(new EffectiveGpoSetting(
                    src, Value(item, "key", 4096), Value(item, "name", 128),
                    Value(item, "value", 520), risk, Value(item, "reason", 200)));
            }
            policies.Add(new EffectiveGpoPolicy(name, id, linkedAt,
                order, depth, siteDn, link, enforced, blocked, machineOff, security, wmi,
                staticRead, dynamicScript, category, settings));
        }
        int linked = payload.GetProperty("linkedCount").GetInt32();
        int sites = payload.GetProperty("siteCandidates").GetInt32();
        int potential = payload.GetProperty("potentialHigh").GetInt32();
        if (linked < 0 || sites < 0 || sites + linked != policies.Count ||
            sites != policies.Count(p => p.Site.Length != 0) ||
            potential != policies.Count(p =>
                p.Category == "Potentially applicable" && p.HighSettings > 0))
            throw new InvalidDataException("GPO scan counts contradict its details.");
        bool siteComplete = payload.GetProperty("siteInventoryComplete").GetBoolean();
        JsonElement notes = payload.GetProperty("warnings");
        if (notes.ValueKind != JsonValueKind.Array || notes.GetArrayLength() > 300)
            throw new InvalidDataException("Unbounded GPO warnings.");
        var warnings = notes.EnumerateArray()
            .Select(x => x.GetString() ?? "")
            .Where(x => x.Length is > 0 and <= 250).ToArray();
        if (!siteComplete && !warnings.Any(w => w.Contains(
                "Site GPO inventory incomplete", StringComparison.Ordinal)))
            throw new InvalidDataException("Incomplete site scan must be explicitly reported.");

        return new EffectiveGpoReport
        {
            Domain = domain, Dc = dc, OuDn = ouDn, GroupDn = groupDn,
            Computer = computer, ActualComputerContainer = actualContainer, CampaignId = campaign,
            ManagedGpoId = expectedGuid.ToString("D"),
            AnalyzedUtc = observed, Membership = membership,
            TokenStatus = token, SiteAssociation = site,
            SiteInventoryComplete = siteComplete, Policies = policies
                .OrderBy(x => x.Category, StringComparer.Ordinal)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings = warnings
        };
    }
}
