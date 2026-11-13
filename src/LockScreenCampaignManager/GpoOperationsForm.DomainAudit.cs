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
            if (computerOus.Length == 0 && userOus.Length == 0)
            {
                _status.Text = UiLocale.Display(
                    "Choose a Computer or User OU before scanning all DC GPOs.");
                return;
            }
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
                " | Source matches: " + report.Count + Environment.NewLine +
                "Possible blockers: " + report.HighRisk + " high, " +
                report.Warnings + " warning" + Environment.NewLine +
                "Unverified/unreadable GPO halves or files: " +
                report.Issues.Count + Environment.NewLine +
                "Linked settings are candidates, NOT effective RSoP." +
                Environment.NewLine +
                "All GPOs were inventoried; only recognized Registry.pol, " +
                "Registry.xml and PowerOptions.xml settings were classified.";
            _status.Text = UiLocale.Display(
                "Domain GPO inventory finished (read-only). Review source and scope.");
            using var dialog = new DomainWideGpoReportDialog(report);
            dialog.ShowDialog(this);
        }
        catch (Exception ex) { Fail(ex, "Scan all DC GPOs"); }
    }
}
