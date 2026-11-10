"""Offline-only contracts: the SYSTEM SMB canary cannot write AD or source files.

CI parses the two scripts with the actual Windows PowerShell 5.1 parser.
This module checks the privacy and evidence boundaries without touching a domain.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
RUNNER = (ROOT / "tools/run_smb_canary_local.ps1").read_text(encoding="utf-8")
PROBE = (ROOT / "tools/smb_system_canary_probe.ps1").read_text(encoding="utf-8")
APPLY = (ROOT / "src/LockScreenCampaignManager/NativeGpoDeploymentApply.cs").read_text(encoding="utf-8")
PLAN = (ROOT / "src/LockScreenCampaignManager/NativeGpoDeploymentPlan.cs").read_text(encoding="utf-8")
WORKFLOW = (ROOT / ".github/workflows/offline-ad-win11-kit.yml").read_text(encoding="utf-8")

class SmbSystemCanaryContracts(unittest.TestCase):
    def test_runner_only_schedules_local_system_with_explicit_consent(self):
        for part in ("I_APPROVE_SMB_CANARY", "Read-Host", "Assert-LocalAdmin",
                     "ExpectedComputer", "New-ScheduledTaskAction",
                     "New-ScheduledTaskPrincipal -UserId 'SYSTEM'",
                     "Register-ScheduledTask", "Start-ScheduledTask",
                     "Stop-ScheduledTask", "Unregister-ScheduledTask",
                     "smb_system_canary_probe.ps1", "Set-PrivateAcl",
                     "contentSource", "manifestSha256", "agentSha256",
                     "packageStoragePath", "Local cache verified"):
            self.assertIn(part, RUNNER)
        for forbidden in ("Invoke-Command", "New-PSSession", "Enter-PSSession",
                          "New-GPO", "Set-GPRegistryValue", "Set-ADComputer",
                          "Invoke-WebRequest", "Invoke-RestMethod",
                          "GITHUB_TOKEN", "ConvertTo-SecureString",
                          "schtasks /s", "Register-ScheduledTask -CimSession"):
            self.assertNotIn(forbidden, RUNNER)

    def test_probe_is_read_only_and_does_not_claim_winning_policy(self):
        for part in ("S-1-5-18", "Win32_ComputerSystem", "Win32_OperatingSystem",
                     "schemaVersion", "Get-FileHash", "Verify-Images",
                     "manifestSha256Verified", "agentSha256Verified",
                     "localCacheHashVerified", "actualGpoAppliedVerified=$false",
                     "realLockScreenDisplayVerified=$false",
                     "registryRestorationVerified=$false",
                     "SOURCE_VERIFIED_AS_LOCAL_SYSTEM_ONLY",
                     "Get-WmiObject", "ConvertTo-Json"):
            self.assertIn(part, PROBE)
        for forbidden in ("Set-ItemProperty", "New-ItemProperty",
                          "Remove-ItemProperty", "Set-Acl", "Copy-Item",
                          "Remove-Item", "Start-Process", "Invoke-Expression",
                          "Invoke-Command", "New-GPO", "Set-GPRegistryValue",
                          "Register-ScheduledTask", "net use",
                          "Invoke-WebRequest", "Invoke-RestMethod"):
            self.assertNotIn(forbidden, PROBE)
        self.assertNotIn("Exception.ToString()", PROBE)
        self.assertNotIn("error=$_.Exception.Message", PROBE)
        self.assertNotIn("sourcePath=$", PROBE)

    def test_plan_apply_pin_source_hash_and_reverify(self):
        self.assertIn("VerifyImmutablePackageForTest", PLAN)
        self.assertIn("manifestSha256 = package.ManifestSha256", PLAN)
        self.assertIn("agentSha256 = package.AgentSha256", PLAN)
        self.assertIn("package.AgentHashPinned", PLAN)
        self.assertIn("NativeGpoDeploymentPlan.VerifyImmutablePackageForTest(targetPath)", APPLY)
        self.assertIn('Required(plan, "manifestSha256")', APPLY)
        self.assertIn('Required(plan, "agentSha256")', APPLY)
        self.assertIn("manifestSha256 = expectedManifestHash", APPLY)
        self.assertIn("agentSha256 = expectedAgentHash", APPLY)
        self.assertIn("RegisterStartup(ldap, gpoDn", APPLY)
        assert APPLY.index("SMB source changed before GPO startup registration.") < APPLY.index("RegisterStartup(ldap, gpoDn", APPLY.index("SMB source changed before GPO startup registration."))

    def test_offline_kit_contains_both_scripts_and_real_parser(self):
        self.assertIn("tools/test_smb_system_canary.py", WORKFLOW)
        self.assertIn("smb_system_canary_probe.ps1", WORKFLOW)
        self.assertIn("run_smb_canary_local.ps1", WORKFLOW)
        self.assertIn("SMB-SYSTEM-CANARY.md", WORKFLOW)
        self.assertIn("ParseFile(", WORKFLOW)
        self.assertIn("runs-on: windows-latest", WORKFLOW)
        self.assertNotIn("self-hosted", WORKFLOW)

if __name__ == "__main__":
    unittest.main(verbosity=2)
