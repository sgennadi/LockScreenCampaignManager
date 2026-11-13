"""Preview.112 named source GPO candidates: scope, no false verdict, privacy."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"

def get(name: str) -> str:
    return (SRC / name).read_text(encoding="utf-8")

class NamedPilotGpoBlockerContracts(unittest.TestCase):
    def test_exact_guid_scoped_source_and_not_winning_gpo(self):
        model = get("DomainPilotBlockerEvidence.cs")
        readiness = get("DomainGpoPilotReadiness.cs")
        for marker in (
            "DomainGpoRsopMatch", 'row.LinkStatus == "Linked candidate"',
            'row.SourceRisk is ("High" or "Warning")',
            "row.GpoId", "row.Setting", "row.RegistryKey", "row.Value",
            "row.ReportedRsop", "row.Source", "row.Interpretation",
            "No confirmed blocking GPO or winning setting.",
            "Historical GPO source", "Historical gpresult",
            "Unverified applicability", "Reported filtered",
            "Review first (GPO reported applied)",
            "TimeSpan.FromMinutes(15)", "TimeSpan.FromHours(24)"
        ):
            self.assertIn(marker, model, marker)
        self.assertIn("NamedBlockers", readiness)
        self.assertIn("DomainPilotBlockerEvidence.Analyze(", readiness)
        self.assertIn("DomainGpoRsopCorrelation.Compare(report, computerXml, userXml)", readiness)
        self.assertIn("PermitsAutomaticRemediation => false", readiness)
        for forbidden in ("Process.Start(", "ModifyRequest(", "SetValue(",
                          "gpupdate.exe", "Win32_Process", "PowerShell"):
            self.assertNotIn(forbidden, model, forbidden)
            self.assertNotIn(forbidden, get("DomainPilotBlockersDialog.cs"), forbidden)

    def test_bounded_safe_local_csv(self):
        model = get("DomainPilotBlockerEvidence.cs")
        dialog = get("DomainPilotBlockersDialog.cs")
        for marker in (
            "if (++count > 6000)", "if (blockers.Count > 6000)",
            "StartsWith(@", "FileAttributes.ReparsePoint",
            "BuildCsv(blockers)", "File.WriteAllText(full",
            '"=+-@"', "UTF8Encoding("
        ):
            self.assertIn(marker, model, marker)
        for marker in (
            "PilotGpoBlockersGrid", "PilotGpoBlockersSearch",
            "PilotGpoBlockersScope", "PilotGpoBlockersPriority",
            "PilotGpoBlockersExport", "MessageBoxButtons.YesNo",
            "MessageBoxDefaultButton.Button2", "SaveFileDialog",
            "DomainPilotBlockerEvidence.ExportLocalCsv",
            "NOT proof that its setting won", "ReviewState"
        ):
            self.assertIn(marker, dialog, marker)

    def test_operator_gate_and_high_dpi(self):
        dlg = get("DomainGpoPilotReadinessDialog.cs")
        smoke = get("UiLayoutSmokeTests.cs")
        for marker in ("PilotReviewNamedGpoBlockers", "DomainPilotBlockersDialog(",
                       "NamedBlockers.Count > 0",
                       "_reviewNamedGpos.Enabled = false", "if (_reading"):
            self.assertIn(marker, dlg, marker)
        self.assertIn("CreateNamedGpoBlockersSmoke", smoke)
        self.assertIn("Review GPO blockers...", smoke)
        self.assertIn('"Export filtered CSV..."', smoke)
        for marker in ("Power112Russian", "Power112Hebrew",
                       "Power112EnglishByRussian", "Power112EnglishByHebrew"):
            self.assertIn(marker, get("UiLocale.Power112.cs"))
            self.assertIn(marker, get("UiLocale.cs"))
        self.assertIn("DomainPilotBlockerEvidence.BuildCsv", get("SelfTests.cs"))
        self.assertIn("0.4.0-preview.114", get("LockScreenCampaignManager.csproj"))
        self.assertIn("NOT winning", (ROOT / "docs" / "RELEASE-PREVIEW112.md")
                      .read_text(encoding="utf-8"))

if __name__ == "__main__":
    unittest.main(verbosity=2)
