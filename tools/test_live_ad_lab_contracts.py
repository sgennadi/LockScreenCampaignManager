"""Offline safety contracts for the manual real-AD workflow.

These tests do not connect to AD. Windows CI also parses the scripts in 5.1.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = (ROOT / ".github/workflows/live-ad-win11-lab.yml").read_text(encoding="utf-8")
CONTROLLER = (ROOT / "tools/lab_ad_integration.ps1").read_text(encoding="utf-8")
PROBE = (ROOT / "tools/lab_win11_probe.ps1").read_text(encoding="utf-8")
BUILD = (ROOT / ".github/workflows/build.yml").read_text(encoding="utf-8")

class LiveAdLabContracts(unittest.TestCase):
    def test_manual_protected_runner_only(self):
        self.assertIn("workflow_dispatch:", WORKFLOW)
        self.assertIn("environment: ad-lab", WORKFLOW)
        self.assertIn("runs-on: [self-hosted, Windows, X64, lscm-ad-lab]", WORKFLOW)
        self.assertIn("refs/heads/main", WORKFLOW)
        self.assertIn("I_APPROVE_AD_LAB_TEST", WORKFLOW)
        self.assertIn("concurrency:", WORKFLOW)
        self.assertNotRegex(WORKFLOW, r"(?m)^  (?:pull_request|push|schedule):")
        self.assertNotIn("windows-latest", WORKFLOW)
        for item in ("LSCM_LAB_DOMAIN","LSCM_LAB_DC","LSCM_LAB_OU_DN",
                     "LSCM_LAB_GROUP_DN","LSCM_LAB_CLIENTS"):
            self.assertIn(item, WORKFLOW)
        self.assertIn("retention-days: 7", WORKFLOW)

    def test_isolated_scope_and_real_windows11_evidence(self):
        for token in ("LSCM-CI","Assert-LabScope","SearchScope]::OneLevel",
                      "Get-FileHash","Schedule-Remote","GPO-Deploy.ps1",
                      "Gpo-Lifecycle.ps1","Invoke-Engine $deployEngine 'Plan'",
                      "Invoke-Engine $deployEngine 'Deploy'","Run-Probe",
                      "Await-Image","bootUtc","clientBootBefore",
                      "Gpo-Action $summary.gpoId 'Unlink'",
                      "Gpo-Action $summary.gpoId 'Delete'",
                      "summary.sourceDeleted","clientCleanupVerified"):
            self.assertIn(token, CONTROLLER)
        for token in ("Get-WmiObject","Windows 11","Enterprise|Education",
                      "Get-ScheduledTask","LockScreenImage",
                      "gpresult.exe","Get-FileHash","SHA256",
                      "w32tm.exe","bootUtc","observedUtc",
                      "$agent -Mode Cleanup","Completed"):
            self.assertIn(token, PROBE)
        self.assertNotIn("Invoke-Command", CONTROLLER)
        self.assertIn("Get-FileHash", CONTROLLER)

    def test_hosted_ci_parses_but_never_executes_live_test(self):
        self.assertIn("tools/test_live_ad_lab_contracts.py", BUILD)
        self.assertIn("tools/lab_ad_integration.ps1", BUILD)
        self.assertIn("tools/lab_win11_probe.ps1", BUILD)
        self.assertNotIn("tools/lab_ad_integration.ps1 -Mode Full", BUILD)
        self.assertIn("New-Item -ItemType Directory -Path $remote -Force", CONTROLLER)
        self.assertIn("Remove-LabSource $summary.gpoId", CONTROLLER)
        self.assertIn("Store-Report", CONTROLLER)

if __name__ == "__main__":
    unittest.main(verbosity=2)
