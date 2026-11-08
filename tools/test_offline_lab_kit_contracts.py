"""Offline checks of the runner-free Live AD lab packaging and safety contracts.

This test never contacts a DC. Run on GitHub-hosted Windows or a developer PC.
"""
from pathlib import Path
import json
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = (ROOT / ".github/workflows/offline-ad-win11-kit.yml").read_text(encoding="utf-8")
LAUNCHER = (ROOT / "tools/run_lab_local.ps1").read_text(encoding="utf-8")
LIVE = (ROOT / "tools/lab_ad_integration.ps1").read_text(encoding="utf-8")
README = (ROOT / "docs/OFFLINE-AD-WIN11-LAB.md").read_text(encoding="utf-8")
SAMPLE = json.loads((ROOT / "tools/lab.config.sample.json").read_text(encoding="utf-8"))

class OfflineLabKitContracts(unittest.TestCase):
    def test_github_only_packages_and_does_not_access_domain(self):
        self.assertIn("runs-on: windows-latest", WORKFLOW)
        self.assertIn("permissions:\n  contents: read", WORKFLOW)
        self.assertIn("pull_request:", WORKFLOW)
        self.assertIn("workflow_dispatch:", WORKFLOW)
        self.assertIn("actions/upload-artifact@v7", WORKFLOW)
        self.assertIn("github.ref == 'refs/heads/main'", WORKFLOW)
        # Short retention protects preview CI from exhausting Actions storage.
        self.assertIn("retention-days: 2", WORKFLOW)
        self.assertIn("continue-on-error: true", WORKFLOW)
        self.assertIn("Report offline-kit artifact upload problem", WORKFLOW)
        self.assertNotIn("self-hosted", WORKFLOW)
        self.assertNotIn("tools/lab_ad_integration.ps1 -Mode Full", WORKFLOW)
        self.assertNotIn("tools/run_lab_local.ps1 -Mode Full", WORKFLOW)
        self.assertIn("Compress-Archive", WORKFLOW)
        self.assertIn("Expand-Archive", WORKFLOW)
        for file in ["run_lab_local.ps1", "lab_ad_integration.ps1",
                     "lab_win11_probe.ps1", "GPO-Deploy.ps1",
                     "Gpo-Lifecycle.ps1", "Gpo-ConflictScan.ps1",
                     "LockScreen-Agent.ps1"]:
            self.assertIn(file, WORKFLOW)
        self.assertIn("src/LockScreenCampaignManager/Assets", WORKFLOW)

    def test_launcher_has_no_credentials_or_implicit_mutation(self):
        self.assertIn("ValidateSet('Preflight','Full')", LAUNCHER)
        self.assertIn("I_APPROVE_AD_LAB_TEST", LAUNCHER)
        self.assertIn("Run only from a domain-joined Windows workstation", LAUNCHER)
        self.assertIn("Get-WmiObject", LAUNCHER)
        self.assertIn("lab.config.json", LAUNCHER)
        self.assertIn("reports", LAUNCHER)
        self.assertIn("Read-Host", LAUNCHER)
        self.assertIn("last-successful-preflight.json", LAUNCHER)
        self.assertIn("verifiedUtc.AddMinutes(60)", LAUNCHER)
        self.assertIn("scopeHash", LAUNCHER)
        self.assertIn("Remove-Item -LiteralPath $gatePath", LAUNCHER)
        self.assertIn("if ($Mode -eq 'Full')", LAUNCHER)
        self.assertIn("LSCM-CI", LAUNCHER)
        self.assertIn("lab_ad_integration.ps1", LAUNCHER)
        self.assertIn("Client", LIVE)
        self.assertIn("Assert-LabScope", LIVE)
        for forbidden in ["-Password ", "ConvertTo-SecureString", "GITHUB_TOKEN",
                          "Invoke-WebRequest", "Invoke-RestMethod"]:
            self.assertNotIn(forbidden, LAUNCHER)

    def test_template_and_docs_are_nonproduction(self):
        self.assertEqual(SAMPLE["domain"], "example.com")
        self.assertEqual(SAMPLE["dc"], "dc01.example.com")
        self.assertTrue(SAMPLE["ouDn"].startswith("OU=LSCM-CI,"))
        self.assertTrue(SAMPLE["groupDn"].startswith("CN=LSCM-CI-"))
        self.assertEqual(SAMPLE["clients"], ["WIN11CI01"])
        self.assertIn("No domain-joined runner", README)
        self.assertIn("Preflight", README)
        self.assertIn("Full", README)
        self.assertIn("live-ad-lab.json", README)
        self.assertIn("no automatic upload", README.lower())
        for cmd in ["Run-Preflight.cmd", "Run-Full-Lab.cmd"]:
            data = (ROOT / "tools" / cmd).read_text(encoding="utf-8")
            self.assertIn("run_lab_local.ps1", data)

if __name__ == "__main__":
    unittest.main(verbosity=2)
