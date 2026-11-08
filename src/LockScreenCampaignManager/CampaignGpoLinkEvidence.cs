using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only, selected-DC GPO link observation. This is AD state, NOT a
/// calculation of RSoP or proof that a machine has processed policy.
/// </summary>
internal sealed record CampaignGpoLinkEvidence(
    DateTimeOffset ObservedUtc,
    string DomainController,
    string DirectOuLink,
    string MachineConfiguration,
    int OtherLinks,
    bool Available,
    string Caution)
{
    internal static CampaignGpoLinkEvidence Unavailable(string dc, string explanation) =>
        new(DateTimeOffset.UtcNow, dc, "Unknown", "Unknown", 0, false,
            UniversalDiagnosticsEmbedded.Clean(explanation, 180));

    internal static CampaignGpoLinkEvidence FromManagedList(
        JsonElement rows, string dc, string ouDn, string campaignId, string gpoId,
        DateTimeOffset observedUtc)
    {
        if (!Guid.TryParse(gpoId, out var selectedGpo) ||
            string.IsNullOrWhiteSpace(campaignId) ||
            string.IsNullOrWhiteSpace(ouDn) ||
            rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Invalid selected GPO or lifecycle list.");

        var policies = rows.EnumerateArray().Where(x =>
            x.ValueKind == JsonValueKind.Object &&
            x.TryGetProperty("id", out var id) &&
            Guid.TryParse(id.GetString(), out var parsed) &&
            parsed == selectedGpo).ToArray();
        if (policies.Length != 1)
            throw new InvalidDataException(
                "Selected managed GPO is missing or duplicated in fresh DC list.");

        var policy = policies[0];
        if (policy.GetProperty("campaignId").GetString() != campaignId)
            throw new InvalidDataException(
                "Managed campaign has changed on the selected DC.");
        int flags = policy.GetProperty("gpoFlags").GetInt32();
        if (flags is < 0 or > 3)
            throw new InvalidDataException("Unexpected GPO configuration flags.");
        var links = policy.GetProperty("links");
        if (links.ValueKind != JsonValueKind.Array || links.GetArrayLength() > 1000)
            throw new InvalidDataException("GPO link listing is incomplete or invalid.");
        var ownLinks = new List<bool>();
        int other = 0;
        foreach (var link in links.EnumerateArray())
        {
            string target = link.GetProperty("ou").GetString() ??
                throw new InvalidDataException("GPO link has no target.");
            bool enabled = link.GetProperty("enabled").GetBoolean();
            if (target.Equals(ouDn, StringComparison.OrdinalIgnoreCase))
                ownLinks.Add(enabled);
            else
                other++;
        }

        if (ownLinks.Count > 1)
            throw new InvalidDataException(
                "Duplicate links to the selected OU; investigate the AD object.");
        string direct = ownLinks.Count == 0 ? "No direct link on selected OU" :
            ownLinks[0] ? "Direct OU link enabled" : "Direct OU link disabled";
        string machine = (flags & 2) != 0
            ? "GPO computer configuration disabled"
            : "GPO computer configuration enabled";
        return new CampaignGpoLinkEvidence(
            observedUtc, dc, direct, machine, other, true,
            "This describes only the selected DC's GPO configuration. " +
            "Parent OU/site links, WMI filtering, security filtering, replication " +
            "and endpoint RSoP may differ. Disabling or unlinking a GPO does not " +
            "remove local scheduled tasks or restore HKLM.");
    }
}
