"""Offline acceptance contracts for independent endpoint cleanup verification.

All mutation in the runner is limited to a temporary LOCAL Scheduled Task
and private LOCAL evidence. The SYSTEM probe reads client state only.
"""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
PROBE = (ROOT / "tools/endpoint_closure_probe.ps1").read_text(encoding="utf-8")
RUNNER = (ROOT / "tools/run_endpoint_closure_local.ps1").read_text(encoding="utf-8")
WORKFLOW = (ROOT / ".github/workflows/offline-ad-win11-kit.yml").read_text(encoding="utf-8")

class EndpointClosureContracts(unittest.TestCase):
    def test_baseline_must_precede_deployment_and_be_immutable(self):
        for token in ("PREDEPLOYMENT_BASELINE_CAPTURED",
                      "BASELINE_ALREADY_EXISTS_NEVER_OVERWRITE",
                      "BASELINE_WAS_NOT_BEFORE_DEPLOYMENT",
                      "baselineBeforeDeployment",
                      "PRIOR_CAMPAIGN_COMPLETION_ID_NOT_REUSABLE",
                      "registryFingerprint", "Digest-Text",
                      "KeyExists", "NoChangingLockScreen", "LockScreenImage",
                      "Read-PolicyFingerprint", "Assert-LocalEvidencePath",
                      "Set-StrictMode -Version Latest"):
            self.assertIn(token, PROBE)
        self.assertIn("[IO.File]::Move($stage,$baselineFile)", PROBE)
        self.assertNotIn("WriteAllText($baselineFile", PROBE)

    def test_independent_postcleanup_conditions_fail_closed(self):
        for token in ("Read-TaskFootprint", "Get-ScheduledTask -TaskPath",
                      "campaignFolderAbsent", "mainTaskAbsent",
                      "feedbackTaskAbsent", "ownerRecordAbsent",
                      "No-ActiveOwner", "cleanupReceiptVerified",
                      "expectedGpoNotApplied", "gpoRsopStatus",
                      "Read-ExactComputerRsop", "Get-LscmComputerGpoEvidence",
                      "POSTCLEANUP_REQUIRES_REVIEW",
                      "POSTCLEANUP_LOCAL_STATE_MATCHES_BASELINE",
                      "registryKeyPresenceMatchesBaseline",
                      "EXPECTED_COMPUTER_GPO_STILL_APPLIED_OR_UNKNOWN",
                      "FINAL_AGENT_RECEIPT_MISSING_OR_INVALID"):
            self.assertIn(token, PROBE)
        self.assertIn("$rsop.status -ceq 'NotApplied'", PROBE)
        self.assertIn("XmlReaderSettings", PROBE)
        self.assertIn("DtdProcessing", PROBE)
        self.assertIn("Remove-Item -LiteralPath $temp", PROBE)
        self.assertNotIn("Invoke-Command", PROBE)
        self.assertNotIn("Start-Process", PROBE)
        self.assertNotIn("New-GPO", PROBE)
        self.assertNotIn("Set-GPRegistryValue", PROBE)
        self.assertNotIn("Set-ItemProperty", PROBE)
        self.assertNotIn("New-ItemProperty", PROBE)
        self.assertNotIn("Remove-ItemProperty", PROBE)
        self.assertNotIn("Set-Acl", PROBE)
        self.assertNotIn("Register-ScheduledTask", PROBE)
        self.assertNotIn("Unregister-ScheduledTask", PROBE)
        self.assertNotIn("Set-ADComputer", PROBE)
        self.assertNotIn("Copy-Item", PROBE)
        self.assertNotIn("Invoke-RestMethod", PROBE)
        self.assertNotIn("Invoke-WebRequest", PROBE)
        self.assertNotIn("Remove-Item -LiteralPath $campaignDir", PROBE)

    def test_runner_requires_local_consent_and_private_acl(self):
        for token in ("I_APPROVE_ENDPOINT_", "Read-Host",
                      "Mode", "Baseline", "VerifyCleanup", "ExpectedComputer",
                      "Assert-LocalAdmin", "Set-PrivateAcl",
                      "New-ScheduledTaskPrincipal -UserId 'SYSTEM'",
                      "Register-ScheduledTask", "Start-ScheduledTask",
                      "Unregister-ScheduledTask", "endpoint_closure_probe.ps1",
                      "smb_canary_rsop.ps1", "baseline.json", "report.json",
                      "deployedUtc", "gpoId", "ExpectedComputer",
                      "Registry same as predeployment"):
            self.assertIn(token, RUNNER)
        for forbidden in ("Invoke-Command", "New-PSSession",
                          "New-GPO", "New-GPLink", "Set-GPRegistryValue",
                          "net use", "Invoke-RestMethod", "Invoke-WebRequest",
                          "Register-ScheduledTask -CimSession"):
            self.assertNotIn(forbidden, RUNNER)

    def test_kit_and_ci_validate_real_ps51_and_publish_audit(self):
        for token in ("test_endpoint_closure.py", "endpoint_closure_probe.ps1",
                      "run_endpoint_closure_local.ps1", "smb_canary_rsop.ps1",
                      "ENDPOINT-CLOSURE-AUDIT.md", "ParseFile("):
            self.assertIn(token, WORKFLOW)
        self.assertIn("windows-latest", WORKFLOW)
        self.assertNotIn("self-hosted", WORKFLOW)

if __name__ == "__main__":
    unittest.main(verbosity=2)
