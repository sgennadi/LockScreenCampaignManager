using System.Text.Json;

namespace LockScreenCampaignManager;

internal sealed partial class GpoOperationsForm
{
    private async Task ScanAllDomainGposAsync()
    {
        if (_busy) return;
        try
        {
            // Independent selected User and Computer OU targets. A Computer OU
            // is never silently promoted to a User Screen Saver assignment.
            var computerOus = _project.TargetAssignments
                .Select(x => x.ComputerOuDn)
                .Append(_ouDn)
                .Where(x => x.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var userOus = _project.TargetAssignments
                .Where(x => x.UsesScreenSaver)
                .Select(x => x.UserOuDn)
                .Where(x => x.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string mode = _project.EffectiveCampaignMode.ToString();
            var data = await ExecuteAsync(DomainOperation.ConflictScan, "AllGpos",
                new {
                    domain = _domain, dc = _dc, computerOus, userOus,
                    campaignMode = mode,
                    screenSaverIdleSeconds = _project.ScreenSaver.IdleSeconds is >= 60 and <= 86400
                        ? _project.ScreenSaver.IdleSeconds : 600,
                    screenSaverExe = string.IsNullOrWhiteSpace(_project.ScreenSaver.Executable)
                        ? "scrnsave.scr" : _project.ScreenSaver.Executable
                });
            var report = DomainWideGpoReport.Parse(
                data, _domain, _dc, computerOus, userOus);
            _conflicts.Text =
                "ALL DC GPOs (READ ONLY) | " + report.Dc + Environment.NewLine +
                "GPO objects inspected: " + report.GpoCount +
                " | Source matches: " + report.Count +
                " | Same-setting overlaps: " + report.Overlaps.Count + Environment.NewLine +
                "Possible blockers: " + report.HighRisk + " high, " +
                report.Warnings + " warning" + Environment.NewLine +
                "Unverified/unreadable GPO halves or files: " +
                report.Issues.Count + Environment.NewLine +
                "Linked settings are candidates, NOT effective RSoP." +
                Environment.NewLine +
                "All GPO objects were inventoried; recognized Registry.pol, " +
                "Registry.xml, PowerOptions.xml and GptTmpl.inf settings classified. " +
                "RDS session limits are remote-session-only candidates.";
            _status.Text = UiLocale.Display(
                "Domain GPO inventory finished (read-only). Review source and scope.");
            int pilotIdle = CampaignTargetAssignments.NeedsScreenSaver(
                    _project.EffectiveCampaignMode) &&
                _project.ScreenSaver.IdleSeconds is >= 60 and <= 86400
                    ? _project.ScreenSaver.IdleSeconds : 0;
            // Optional read-only on-demand power source; no machine is ever
            // contacted while scanning domain GPOs or importing gpresult XML.
            using var dialog = new DomainWideGpoReportDialog(report, pilotIdle,
                async (computer, computerOu) => {
                    if (!NativeGpoConflictScan.SafeComputerName(computer, report.Domain) ||
                        !report.ComputerOus.Contains(computerOu,
                            StringComparer.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            "Pilot computer/OU was not included in the selected-DC scan.");
                    var observation = await ExecuteAsync(DomainOperation.ConflictScan,
                        "ObservePower", new {
                            domain = report.Domain, dc = report.Dc,
                            ouDn = computerOu, computer
                        });
                    return EndpointPowerObservation.Parse(observation,
                        report.Domain, report.Dc, computerOu, computer,
                        DateTimeOffset.UtcNow);
                });
            dialog.ShowDialog(this);
        }
        catch (Exception ex) { Fail(ex, "Scan all DC GPOs"); }
    }
}
