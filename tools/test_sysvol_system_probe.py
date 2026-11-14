"""Preview.120 local SYSTEM SYSVOL pilot source/packaging regressions.
Runs offline. Never reaches DC, opens SMB or creates scheduled tasks.
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"

def get(path):
    return (ROOT / path).read_text(encoding="utf-8")

class SysvolSystemContracts(unittest.TestCase):
    def test_probe_readonly_requires_machine_context_and_exact_guid(self):
        probe = get("tools/sysvol_system_probe.ps1")
        for marker in ("S-1-5-18", "LOCAL_SYSTEM_REQUIRED",
                       "Get-WmiObject Win32_ComputerSystem",
                       "Guid]::TryParseExact", "Read-Source",
                       "GPT.INI", "User\\Registry.pol",
                       "Machine\\Registry.pol", "GPT_READ",
                       "GPT_NOT_READ", "FileMode]::CreateNew",
                       "lscm-sysvol-system/v1"):
            self.assertIn(marker, probe, marker)
        for blocked in ("gpupdate", "Invoke-Command", "Invoke-Expression",
                        "Start-Process", "Copy-Item", "New-ScheduledTask",
                        "Registry.SetValue", "Set-ItemProperty", "ModifyRequest",
                        "Get-FileHash", "schtasks.exe", "File.Delete"):
            self.assertNotIn(blocked, probe, blocked)
        # No SYSVOL file is executed or modified: one byte read only.
        self.assertIn("[IO.FileAccess]::Read", probe)
        self.assertIn("$stream.ReadByte()", probe)

    def test_harness_is_local_approved_and_cleans_task(self):
        harness = get("tools/run_sysvol_system_probe_local.ps1")
        for marker in ("I_APPROVE_SYSVOL_READ",
                       "WindowsBuiltInRole]::Administrator",
                       "New-ScheduledTaskPrincipal -UserId 'SYSTEM'",
                       "Register-ScheduledTask", "Start-ScheduledTask",
                       "Unregister-ScheduledTask", "SetAccessRuleProtection",
                       "sysvol_system_probe.ps1", "config.json",
                       "LSCM-SYSVOL-Canary", "Get-WmiObject Win32_OperatingSystem"):
            self.assertIn(marker, harness, marker)
        executable_lines = "\n".join(line for line in harness.splitlines()
                                     if not line.lstrip().startswith("#"))
        for blocked in ("-ComputerName", "Invoke-Command",
                        "gpupdate", "Set-ADObject", "New-GPO",
                        "Remove-GPO", "Set-GPPermission"):
            self.assertNotIn(blocked, executable_lines, blocked)

    def test_strict_unsigned_evidence_and_one_readiness(self):
        parser = get("src/LockScreenCampaignManager/DomainSysvolSystemEvidence.cs")
        readiness = get("src/LockScreenCampaignManager/DomainGpoPilotReadiness.cs")
        dialog = get("src/LockScreenCampaignManager/DomainGpoPilotReadinessDialog.cs")
        tests = get("src/LockScreenCampaignManager/SelfTests.cs")
        for marker in ("lscm-sysvol-system/v1", "MaxDepth = 4",
                       "RequiredKeys", "localSystem", "JsonValueKind.True",
                       "Guid.TryParseExact", "SafeComputerName",
                       "SameController", "ReportSha256", "Fresh(",
                       "FileAttributes.ReparsePoint", "new FileInfo(full)",
                       "ProvesWinningGpo => false",
                       "ProvesClientApplication => false"):
            self.assertIn(marker, parser, marker)
        for marker in ("SysvolSystemImported", "AddSysvolSystem(",
                       "SYSVOL / LocalSystem", "GPT.INI",
                       "Operator-imported UNSIGNED",
                       "Source candidate", "Not verified",
                       "DomainSysvolSystemEvidence? sysvolSystem = null"):
            self.assertIn(marker, readiness, marker)
        for marker in ("PilotImportSystemSysvol", "PilotClearSystemSysvol",
                       "ImportSysvolSnapshot()", "_sysvolSystem = null",
                       "DomainSysvolSystemEvidence.LoadLocalJson(",
                       "DateTimeOffset.UtcNow, _endpointUser, _adScope, _sysvolSystem"):
            self.assertIn(marker, dialog, marker)
        for marker in ("sysvolGood", "sysvolBlocked",
                       "sysvolStale", "sysvolRejected",
                       "DomainSysvolSystemEvidence.ParseJson("):
            self.assertIn(marker, tests, marker)

    def test_offline_kit_ci_locales_and_version(self):
        build = get(".github/workflows/build.yml")
        lab = get(".github/workflows/offline-ad-win11-kit.yml")
        for workflow in (build, lab):
            for marker in ("test_sysvol_system_probe.py",
                           "tools/sysvol_system_probe.ps1",
                           "tools/run_sysvol_system_probe_local.ps1"):
                self.assertIn(marker, workflow, marker)
        for marker in ("docs/SYSVOL-SYSTEM-PILOT.md",
                       "tools/sysvol_system_probe.ps1",
                       "tools/run_sysvol_system_probe_local.ps1"):
            self.assertIn(marker, lab, marker)
        locale = get("src/LockScreenCampaignManager/UiLocale.Power115.cs")
        for marker in ('["SYSVOL / LocalSystem"]',
                       '["Import SYSTEM SYSVOL JSON..."]',
                       '["Clear SYSVOL evidence"]',
                       '["Import private local SYSTEM SYSVOL report"]'):
            self.assertGreaterEqual(locale.count(marker), 2, marker)
        self.assertIn("0.4.0-preview.120", get(
            "src/LockScreenCampaignManager/LockScreenCampaignManager.csproj"))
        self.assertTrue((ROOT / "docs" / "SYSVOL-SYSTEM-PILOT.md").is_file())
        self.assertTrue((ROOT / "docs" / "RELEASE-PREVIEW120.md").is_file())

if __name__ == "__main__":
    unittest.main(verbosity=2)
