"""Preview.111: read-only domain pilot readiness and optional scoped power read."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"

def get(name):
    return (SRC / name).read_text(encoding="utf-8")

class PilotReadinessContracts(unittest.TestCase):
    def test_no_false_clean_state_or_auto_remediation(self):
        model = get("DomainGpoPilotReadiness.cs")
        for marker in (
            "Source candidate", "Observed concern", "Not verified",
            "ProvesScreenSaverDisplayed => false",
            "ProvesWinningGpo => false",
            "PermitsAutomaticRemediation => false",
            "DomainGpoRsopCorrelation.Compare(report, computerXml, userXml)",
            "report.RecognizedSourcesReadable",
            "report.Issues.Count",
            "AddXml(findings, \"Computer\"", "AddXml(findings, \"User\"",
            "User HKCU Screen Saver settings",
            "Visual acceptance",
            "now - power.ObservedUtc > TimeSpan.FromMinutes(5)",
            "power.SourceDisagreements()",
            "power.Assess(saverIdleSeconds)",
            "recent && sourceComplete ? \"Observed concern\" : \"Not verified\""
        ):
            if marker == "now - power.ObservedUtc > TimeSpan.FromMinutes(5)":
                self.assertIn("now - power.ObservedUtc", model)
                self.assertIn("TimeSpan.FromMinutes(5)", model)
            else:
                self.assertIn(marker, model, marker)
        self.assertIn("!power.Computer.Equals(computer", model)
        self.assertIn("!power.OuDn.Equals(selectedComputerOu", model)
        self.assertIn("report.ComputerOus.Contains(selectedComputerOu", model)
        self.assertIn("SameController(power.Dc, report.Dc)", model)
        self.assertIn("TestDomainPilotReadiness();", get("SelfTests.cs"))

    def test_opt_in_remote_wmi_and_hklm_requires_confirm_and_new_scope_validation(self):
        dialog = get("DomainGpoPilotReadinessDialog.cs")
        forms = get("GpoOperationsForm.DomainAudit.cs")
        for marker in (
            "PilotVerifyPower", "PilotRefreshReview",
            "PilotComputerFqdn", "PilotComputerOu",
            "PilotReadinessFindings", "PilotReadinessDetails",
            "MessageBoxButtons.YesNo", "MessageBoxDefaultButton.Button2",
            "NativeGpoConflictScan.SafeComputerName",
            "_report.ComputerOus.Contains(ou",
            "if (_reading || _readPower is null) return",
            "_observation = null;", "Review();",
            "await _readPower(computer, ou)",
            "reading.OuDn.Equals(ou",
            "SameController(reading.Dc, _report.Dc)",
            "Read-only endpoint query still running",
            "DateTimeOffset.UtcNow"
        ):
            if marker == "DateTimeOffset.UtcNow":
                self.assertIn(marker, dialog)
            else:
                self.assertIn(marker, dialog, marker)
        self.assertLess(dialog.index("MessageBoxButtons.YesNo"),
                        dialog.index("await _readPower(computer, ou)"))
        self.assertIn('ExecuteAsync(DomainOperation.ConflictScan,', forms)
        self.assertIn('"ObservePower"', forms)
        self.assertIn("EndpointPowerObservation.Parse(observation,", forms)
        self.assertIn("report.ComputerOus.Contains(computerOu,", forms)
        for marker in ("Process.Start(", "ModifyRequest(", "SetValue(",
                       "File.WriteAllText(", "gpupdate.exe", "CreateProcess("):
            self.assertNotIn(marker, dialog, marker)
            self.assertNotIn(marker, model := get("DomainGpoPilotReadiness.cs"), marker)

    def test_ui_smoke_localization_and_preview_release_gates(self):
        rsop = get("DomainGpoRsopDialog.cs")
        wide = get("DomainWideGpoReportDialog.cs")
        smoke = get("UiLayoutSmokeTests.cs")
        for marker in ("GpresultPilotReadiness",
                       "DomainGpoPilotReadinessDialog(",
                       "_pilotPowerReader", "_saverIdleSeconds"):
            self.assertIn(marker, rsop, marker)
        self.assertIn("new DomainGpoRsopDialog(", wide)
        self.assertIn("CreateGpoPilotReadinessSmoke()", smoke)
        self.assertIn('"Pilot readiness..."', smoke)
        self.assertIn('"Verify power (PC)..."', smoke)
        for marker in ("Power111Russian", "Power111Hebrew",
                       "Power111EnglishByRussian", "Power111EnglishByHebrew"):
            self.assertIn(marker, get("UiLocale.Power111.cs"))
            self.assertIn(marker, get("UiLocale.cs"))
        self.assertIn("0.4.0-preview.113", get("LockScreenCampaignManager.csproj"))
        self.assertIn("NOT", (ROOT / "docs" / "RELEASE-PREVIEW111.md")
                      .read_text(encoding="utf-8"))

if __name__ == "__main__":
    unittest.main(verbosity=2)
