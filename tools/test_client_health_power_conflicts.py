"""Preview.106: offline-only AD Client Health and Screen Saver GPO power contracts."""
from pathlib import Path
import re
import unittest

SRC = Path(__file__).resolve().parents[1] / "src" / "LockScreenCampaignManager"

def read(name):
    return (SRC / name).read_text(encoding="utf-8")

class ScopedHealthAndPowerContracts(unittest.TestCase):
    def test_ad_inventory_verified_and_isolated(self):
        native = read("NativeImmediateActions.cs")
        model = read("ClientHealthAdInventory.cs")
        panel = read("ClientFeedbackPanel.cs")
        feedback = read("ClientFeedback.cs")
        self.assertIn('"Inventory"', panel)
        self.assertIn("DomainOperation.Immediate", panel)
        self.assertIn('dcCurrentTime = NativeAdDiscovery.Attr(root, "currentTime")', native)
        self.assertIn('ClientHealthAdInventory.Parse(result.GetProperty("data")', panel)
        self.assertIn("expectedScopeVerified: verified", panel)
        self.assertIn("expectedScopeVerified || expected.Count > 0", feedback)
        self.assertIn("IsFresh(DateTimeOffset.UtcNow)", panel)
        self.assertIn("OuTotal", model)
        self.assertIn("UnknownLogon", model)
        self.assertIn("ReportsVerified", model)
        self.assertIn("StaleReports", model)
        self.assertIn("LatestReports.TryGetValue(name", model)
        self.assertIn("feedback.Expected == _members.Count", model)
        self.assertIn('string.Equals(feedback.CampaignId, campaignId, StringComparison.Ordinal)', model)
        self.assertIn('"replicatedLastLogonTimestamp"', read("OuComputerActivityCoverage.cs"))
        for forbidden in ("ModifyRequest", "AddRequest", "DeleteRequest",
                          "WriteAllText", "CreateDirectory", "File.Delete", "HttpClient"):
            self.assertNotIn(forbidden, model)
        self.assertIn("_adInventory.IsFresh(DateTimeOffset.UtcNow)", panel)
        self.assertIn("TestClientHealthAdCoverage();", read("SelfTests.cs"))

    def test_power_gpo_discovery_all_sources_and_scopes(self):
        scan = read("NativeGpoConflictScan.cs")
        compare = read("ScreenSaverPowerConflictAnalysis.cs")
        matrix = read("DomainDeploymentForm.AssignmentDeployment.cs")
        assignments = read("CampaignAssignmentsDialog.cs")
        self.assertIn('3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e', scan.lower())
        self.assertIn('29f6c1db-86da-48c5-9fdb-f2b67b1f44da', scan.lower())
        self.assertIn('"inactivitytimeoutsecs"', scan.lower())
        self.assertIn('@"Preferences\\PowerOptions\\PowerOptions.xml"', scan)
        self.assertIn("ParseGppPowerOptions(bytes)", scan)
        self.assertIn("XmlResolver = null", compare)
        self.assertIn("DtdProcessing = DtdProcessing.Prohibit", compare)
        self.assertIn("displayOffAC", compare)
        self.assertIn("monitorAc", compare)
        self.assertIn("seconds = minutes ? (long)parsed * 60L : parsed", compare)
        self.assertIn("if (seconds == 0) continue", compare)
        self.assertIn('f.Risk == "Inactive/filtered"', compare)
        self.assertIn("ScreenSaverPowerConflictAnalysis.Compare(", matrix)
        self.assertIn("ScreenSaverPowerConflictAnalysis.IsMachinePowerCandidate", matrix)
        self.assertIn('row.ComputerOuDn, "Computer", powerTarget', matrix)
        self.assertIn('row.UserOuDn, "User", userTarget', matrix)
        self.assertIn("GroupConflictFinding(", matrix)
        self.assertIn('powerWarnings = 1;', matrix)
        self.assertIn('audit.HighRisk + powerHigh', matrix)
        self.assertIn("_computerOu.Enabled = hasComputer || hasUser;", assignments)
        self.assertIn("_computerGroup.Enabled = hasComputer;", assignments)
        self.assertIn("TestScreenSaverPowerConflictAnalysis();", read("SelfTests.cs"))
        for forbidden in ("ModifyRequest", "DeleteRequest", "Set-AD", "File.WriteAllText",
                          "File.WriteAllBytes", "Process.Start", "powercfg.exe"):
            self.assertNotIn(forbidden, compare)
        self.assertIn("NOT RSoP", matrix)
        self.assertIn("it does NOT assign a computer policy", assignments)
        
    def test_trilingual_ui_and_dpi(self):
        locale = read("UiLocale.Coverage106.cs")
        ui = read("UiLocale.cs")
        self.assertIn('Coverage106Russian', locale)
        self.assertIn('Coverage106Hebrew', locale)
        self.assertIn('Coverage106EnglishByRussian', ui)
        self.assertIn('Coverage106EnglishByHebrew', ui)
        self.assertIn('"ClientHealthAdActivityWindow"', ui)
        self.assertIn('"ClientHealthAdReportCoverage"', read("ClientFeedbackPanel.cs"))
        self.assertIn('"ClientHealthAdReportCoverage"', read("UiLayoutSmokeTests.cs"))
        self.assertIn("UiLocale.Coverage106.cs", (SRC.parents[1] / "tools" / "test_manager_features.py").read_text(encoding="utf-8"))

if __name__ == "__main__":
    unittest.main(verbosity=2)
