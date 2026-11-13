"""Preview.109: RDS idle/session and same-setting GPO source comparison contracts."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"

def read(name):
    return (SRC / name).read_text(encoding="utf-8")

class RdsAndOverlapContracts(unittest.TestCase):
    def test_rds_both_scopes_no_remote_actions_and_millisecond_comparison(self):
        rds = read("RdsSessionPolicyBlockers.cs")
        native = read("NativeGpoConflictScan.cs")
        audit = read("NativeGpoDomainAudit.cs")
        for term in ("maxidletime", "maxconnectiontime", "maxdisconnectiontime",
                     "fresetbroken", "MaxIdleTime", "TryMilliseconds",
                     "configuredSaverSeconds * 1000", "milliseconds == 0",
                     'return "Warning"', 'return "Info"'):
            self.assertIn(term, rds, term)
        self.assertIn('RdsSessionPolicyBlockers.Recognize(key, name, value)', native)
        self.assertLess(native.index("RdsSessionPolicyBlockers.Recognize(key, name, value)"),
                        native.index("if (userScope)"))
        self.assertIn("RdsSessionPolicyBlockers.AssessRisk(", audit)
        self.assertIn('"Linked candidate"', rds)
        for text in ("Process.Start", "CreateProcess", "ModifyRequest",
                     "File.WriteAllText", "Win32_Process", "Invoke-Command"):
            self.assertNotIn(text, rds)
        self.assertIn('TestRdsSessionAndGpoOverlaps();', read("SelfTests.cs"))

    def test_same_setting_overlap_preserves_evidence_and_stays_not_rsop(self):
        model = read("DomainGpoOverlapAnalysis.cs")
        dialog = read("DomainGpoOverlapDialog.cs")
        report = read("DomainWideGpoReport.cs")
        main = read("DomainWideGpoReportDialog.cs")
        for text in ("GroupBy(", "x.Scope.ToUpperInvariant()",
                     "x.Key.Trim()", "x.Setting.Trim()",
                     "Distinct(StringComparer.OrdinalIgnoreCase)",
                     "Ambiguous source values", "Same configured value",
                     "Different configured values", "Linked candidate",
                     "not an applied conflict", "BuildCsv(", '"=+-@"'):
            self.assertIn(text, model)
        self.assertIn('internal IReadOnlyList<DomainGpoOverlap> Overlaps', report)
        self.assertIn('DomainGpoOverlapAnalysis.Analyze(Matches)', report)
        self.assertIn('DomainGpoOverlapDialog(_report.Overlaps)', main)
        self.assertIn('Compare GPO settings...', main)
        for text in ("GpoOverlapSearch", "GpoOverlapKindFilter",
                     "GpoOverlapGrid", "ShowDetails()", "ExportCsv()",
                     "OverwritePrompt = true", "FileAttributes.ReparsePoint",
                     "RightToLeft.No"):
            self.assertIn(text, dialog)

    def test_localized_display_and_offline_smoke(self):
        for key in ("Power109Russian", "Power109Hebrew",
                    "Power109EnglishByRussian", "Power109EnglishByHebrew"):
            self.assertIn(key, read("UiLocale.Power109.cs"))
            self.assertIn(key, read("UiLocale.cs"))
        smoke = read("UiLayoutSmokeTests.cs")
        self.assertIn('CreateGpoOverlapSmoke', smoke)
        self.assertIn('"Compare GPO settings..."', smoke)
        self.assertIn('"Export filtered CSV..."', smoke)

if __name__ == "__main__":
    unittest.main(verbosity=2)
