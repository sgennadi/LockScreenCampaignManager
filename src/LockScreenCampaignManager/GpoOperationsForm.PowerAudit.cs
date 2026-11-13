using System.Text.Json;

namespace LockScreenCampaignManager;

internal sealed partial class GpoOperationsForm
{
    private async Task VerifyScreenSaverPowerAsync()
    {
        if (_busy) return;
        if (!CampaignTargetAssignments.NeedsScreenSaver(_project.EffectiveCampaignMode))
        {
            _status.Text = UiLocale.Display(
                "Select a Screen Saver or Both campaign before checking client power settings.");
            return;
        }
        string host = _computerName.Text.Trim();
        if (!NativeGpoConflictScan.SafeComputerName(host, _domain))
        {
            _status.Text = UiLocale.Display("Enter a domain computer FQDN, e.g. PC01.") + _domain;
            return;
        }
        int idleSeconds = _project.ScreenSaver.IdleSeconds;
        if (idleSeconds is < 60 or > 86400)
        {
            _status.Text = UiLocale.Display("Configure a valid Screen Saver idle timeout first.");
            return;
        }
        try
        {
            // The native reader validates the selected Computer OU plus this
            // precise FQDN on the selected DC before contacting remote WMI.
            var source = await ExecuteAsync(DomainOperation.ConflictScan, "ObservePower",
                new { domain = _domain, dc = _dc, ouDn = _ouDn, computer = host });
            EndpointPowerObservation observation = EndpointPowerObservation.Parse(
                source, _domain, _dc, _ouDn, host, DateTimeOffset.UtcNow);

            string gpoStatus = "Not verified: linked Computer GPO scan was unavailable.";
            IReadOnlyList<GroupConflictFinding> gpo = [];
            try
            {
                // Static linked-GPO candidates for this OU are NOT a substitute
                // for device RSoP. Never pass an unverified hostname into Scan.
                var scan = await ExecuteAsync(DomainOperation.ConflictScan, "Scan",
                    new { domain = _domain, dc = _dc, ouDn = _ouDn,
                        computer = "", policyScope = "Computer" });
                gpo = ReadPowerGpoCandidates(scan, _domain, _ouDn);
                gpoStatus = "Complete static linked-GPO candidate scan; not an effective RSoP.";
            }
            catch (Exception)
            {
                // Surface lack of evidence, never treat exceptions as zero GPOs.
                gpoStatus = "Linked-GPO candidate scan NOT VERIFIED; no clean result inferred.";
            }
            if (IsDisposed || Disposing) return;
            _conflicts.Text = "POWER AUDIT | PC " + observation.Computer +
                Environment.NewLine +
                "Configured Screen Saver idle: " + idleSeconds + " s (not applied User HKCU)" +
                Environment.NewLine +
                "WMI active plan: " + observation.PlanStatus +
                " | Remote HKLM: " + observation.RegistryStatus +
                " | Shorter observed timeouts: " + observation.PotentialEarly(idleSeconds) +
                Environment.NewLine + gpoStatus + Environment.NewLine +
                "No winning GPO, user application or physical display has been proven.";
            _status.Text = UiLocale.Display(
                "Power audit completed (read-only). Review endpoint readings and GPO candidates.");
            using var window = new ScreenSaverEndpointPowerDialog(
                observation, idleSeconds, gpo, gpoStatus);
            window.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Fail(ex, "Verify Screen Saver power (PC)");
        }
    }

    private static IReadOnlyList<GroupConflictFinding> ReadPowerGpoCandidates(
        JsonElement data, string domain, string ouDn)
    {
        static string Read(JsonElement item, string key, int max = 2048)
        {
            string value = item.GetProperty(key).GetString() ??
                throw new InvalidDataException("Incomplete GPO candidate record.");
            if (value.Length > max || value.Any(char.IsControl))
                throw new InvalidDataException("Invalid GPO candidate field: " + key);
            return value;
        }
        if (!Read(data,"domain").Equals(domain,StringComparison.OrdinalIgnoreCase) ||
            !Read(data,"targetOu").Equals(ouDn,StringComparison.OrdinalIgnoreCase) ||
            Read(data,"policyScope") != "Computer")
            throw new InvalidDataException("Computer power GPO candidate scope mismatch.");
        JsonElement rows = data.GetProperty("matches");
        int count = data.GetProperty("count").GetInt32();
        if (rows.ValueKind != JsonValueKind.Array || count is < 0 or > 500 ||
            count != rows.GetArrayLength())
            throw new InvalidDataException("Computer GPO candidate evidence is incomplete.");

        var output = new List<GroupConflictFinding>();
        foreach (JsonElement row in rows.EnumerateArray())
        {
            var finding = new GroupConflictFinding(
                "Verified device OU", "Computer",
                Read(row,"gpo",150), Read(row,"gpoId",40), Read(row,"risk",40),
                Read(row,"name",120), Read(row,"value",470),
                Read(row,"source",120), Read(row,"key",600),
                Read(row,"linkedAt",2048), Read(row,"applicability",550));
            if (ScreenSaverPowerConflictAnalysis.IsMachinePowerCandidate(finding))
                output.Add(finding);
        }
        return output;
    }
}
