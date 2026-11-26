"""Preview.122: local-only Screen Saver binary and actual HKCU/power contracts.
Source checks run offline; the native Windows self-tests exercise logic.
No DC, SMB, process launch, registry writes or network calls are made here.
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"


def code(name):
    return (SRC / name).read_text(encoding="utf-8")


class LocalSaverAndPowerContracts(unittest.TestCase):
    def test_local_scr_file_read_without_process_or_network(self):
        model = code("EndpointScrFileEvidence.cs")
        for marker in (
            "EndpointScrFileEvidence(", "Capture(", "Parse(",
            '"Readable"', '"Denied"', '"Missing"', '"Linked"',
            '"NonLocal"', '"NotSelected"', '"Unsupported"',
            "Environment.ExpandEnvironmentVariables(", "Path.IsPathFullyQualified(",
            "new DriveInfo(root).DriveType != DriveType.Fixed",
            "FileAttributes.ReparsePoint", "FileAccess.Read",
            "FileMode.Open", "stream.ReadByte()",
            'extension.Equals(".scr"', 'extension.Equals(".exe"',
            "Path.Combine(Environment.SystemDirectory, value)",
            "valueSha256", "HashValue(selected.Value)",
            "Unexpected SCR observation fields.",
        ):
            self.assertIn(marker, model, marker)
        for forbidden in (
            "Process.Start(", "ProcessStartInfo(", "LoadLibrary(", "ShellExecute(",
            "Start-Process", "Registry.SetValue(", "ModifyRequest(",
            "HttpClient(", "FileAccess.Write", "FileMode.Create",
            "DownloadString(", "WebRequest(", "File.Copy(", "File.Move("
        ):
            self.assertNotIn(forbidden, model, forbidden)

    def test_new_hkcu_snapshot_and_exact_backwards_compatible_v1(self):
        evidence = code("EndpointUserReadinessEvidence.cs")
        for marker in (
            "EndpointScrFileEvidence? ScrFile", "EndpointScrFileEvidence.Capture(",
            '"scrFile"', "valueSha256 = scrFile.ValueSha256",
            "EndpointScrFileEvidence.Parse(scrProperty",
            "bool hasScrFile", "bool hasUserSid",
            "if (hasScrFile && !hasUserSid)",
            "CheckExactKeys(root", "UserSid = userSid",
            "StrictUtf8.GetByteCount(json) > MaxBytes",
            "FileMode.CreateNew", "SignedByDomain => false",
            "ProvesScreenSaverDisplayed => false",
        ):
            self.assertIn(marker, evidence, marker)
        readiness = code("DomainGpoPilotReadiness.cs")
        for marker in (
            "PilotSaverPowerComparison.Compare(", "User Screen Saver file access",
            'scr.Status switch', '"Missing" or "Denied" => "Observed concern"',
            "endpoint.ScrFile", "PilotUserSidCorrelation.Matches(",
        ):
            self.assertIn(marker, readiness, marker)

    def test_power_compares_real_user_hkcu_time_not_campaign_target(self):
        power = code("PilotSaverPowerComparison.cs")
        for marker in (
            "PilotUserSidCorrelation.Matches(scope.UserSid, endpoint.UserSid)",
            "endpoint.IsFresh(now)", "scope.Fresh(now)", "scope.Complete",
            'endpoint.Get("PolicyHKCU", "ScreenSaveTimeOut")',
            "CompareTimerSources(seconds, power.Readings",
            '("Display", "AC")', '("Display", "DC")',
            '("Sleep", "AC")', '("Sleep", "DC")',
            '("MachineLock", "Any")',
            '"Source candidate"', '"Observed concern"', '"Not verified"',
            'wmi?.Seconds is long w && policy?.Seconds is long p && w != p',
            "saverSeconds - timer",
            "Power source conflict",
        ):
            if marker == "Power source conflict":
                self.assertIn("power source conflict", power, marker)
            else:
                self.assertIn(marker, power, marker)
        self.assertNotIn("Process.Start(", power)
        self.assertNotIn("SetValue(", power)
        self.assertNotIn("gpupdate.exe", power)

    def test_native_legacy_wrong_user_conflict_and_fake_status(self):
        tests = code("SelfTests.cs")
        for marker in (
            "WithScr(WithSid(valid, pilotSid)", "WithScr(valid,",
            "EndpointScrFileEvidence.Capture(",
            "timerConflict", "timerEarly", "timerEqual", "timerUnknown",
            "User Screen Saver file access", "missingFileForOtherUser",
            "Unsupported", "NonLocal", "360 s earlier",
        ):
            self.assertIn(marker, tests, marker)

    def test_localized_release_and_ci_gate(self):
        locale = code("UiLocale.Power114.cs")
        for key in (
            "User Screen Saver file access",
            "User saver vs power", "User saver vs display",
            "User saver vs sleep", "User saver vs machine lock"
        ):
            self.assertEqual(locale.count(f'["{key}"]'), 2, key)
        csproj = code("LockScreenCampaignManager.csproj")
        self.assertIn("0.4.0-preview.128", csproj)
        self.assertTrue((ROOT / "docs" / "RELEASE-PREVIEW122.md").is_file())
        self.assertTrue((ROOT / "docs" / "SCR-READ-ONLY-PILOT.md").is_file())
        for workflow in (".github/workflows/build.yml",
                         ".github/workflows/offline-ad-win11-kit.yml"):
            text = (ROOT / workflow).read_text(encoding="utf-8")
            self.assertIn("python tools/test_endpoint_scr_and_timing.py", text)


if __name__ == "__main__":
    unittest.main(verbosity=2)
