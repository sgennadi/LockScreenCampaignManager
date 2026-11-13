"""Preview.114 local Windows HKCU Screen Saver observation: strict, opt-in, private."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"

def read(name: str) -> str:
    return (SRC / name).read_text(encoding="utf-8")

class EndpointUserReadinessContracts(unittest.TestCase):
    def test_local_hkcu_only_no_remote_mutation(self):
        model = read("EndpointUserReadinessEvidence.cs")
        for marker in (
            'Schema = "lscm-screen-saver-endpoint/v1"',
            "Registry.CurrentUser.OpenSubKey(", "writable: false",
            "RegistryValueOptions.DoNotExpandEnvironmentNames",
            '"PolicyHKCU"', '"DesktopHKCU"', '"RestrictionHKCU"',
            '"ScreenSaveActive"', '"ScreenSaveTimeOut"', '"SCRNSAVE.EXE"',
            '"ScreenSaverIsSecure"', '"NoDispScrSavPage"',
            "WindowsIdentity.GetCurrent().Name",
            "CapturedUtc", "IsFresh(", "TimeSpan.FromMinutes(10)",
            "CheckExactKeys(", "GetArrayLength() != expected",
            "StrictUtf8.GetByteCount(json) > MaxBytes",
            "SHA256.HashData(", "SignedByDomain => false",
            "ProvesWinningGpo => false", "ProvesScreenSaverDisplayed => false"
        ):
            self.assertIn(marker, model, marker)
        for forbidden in (
            "Registry.LocalMachine", "Registry.Users", "Registry.SetValue(",
            "SetValue(", "DeleteValue(", "Process.Start(", "HttpClient(",
            "Win32_Process", "ModifyRequest(", "gpupdate", "Invoke-Command",
            "ScheduledTask", "ComputerManagement"
        ):
            self.assertNotIn(forbidden, model, forbidden)

    def test_local_file_privacy_and_exact_identity(self):
        model = read("EndpointUserReadinessEvidence.cs")
        for marker in (
            "EnsureLocalPath(path, mustExist: false)",
            "EnsureLocalPath(path, mustExist: true)",
            "FileMode.CreateNew", "FileOptions.WriteThrough",
            "FileAttributes.ReparsePoint", 'path.StartsWith(@"\\",
            "Path.IsPathFullyQualified(path)",
            'full.EndsWith(".json"', "File.ReadAllText(full, StrictUtf8)",
            "!evidence.Domain.Equals(expectedDomain",
            "!evidence.Computer.Equals(expectedComputer",
            "Unexpected or repeated endpoint JSON property"
        ):
            self.assertIn(marker, model, marker)

    def test_manual_ui_gates_and_scope_invalidation(self):
        ui = read("EndpointUserProbeDialog.cs")
        pilot = read("DomainGpoPilotReadinessDialog.cs")
        readiness = read("DomainGpoPilotReadiness.cs")
        for marker in ("EndpointUserProbeCapture", "EndpointUserProbeSave",
                       "EndpointUserReadinessEvidence.CaptureLocal()",
                       "MessageBoxButtons.YesNo", "MessageBoxDefaultButton.Button2",
                       "SaveFileDialog", "_snapshot.SaveLocalJson",
                       "_save.Enabled = false", "UiStyle.Apply(this)"):
            self.assertIn(marker, ui, marker)
        for marker in ("PilotImportUserHkcu", "PilotClearUserHkcu",
                       "PilotCaptureCurrentUser", "ImportEndpointSnapshot()",
                       "EndpointUserReadinessEvidence.LoadLocalJson(",
                       "_endpointUser = null;", "DateTimeOffset.UtcNow, _endpointUser",
                       "new EndpointUserProbeDialog()", "_reading"):
            self.assertIn(marker, pilot, marker)
        for marker in ("EndpointUserImported", "AddEndpointUser(",
                       "endpointUser.IsFresh(now)", "ProvesScreenSaverDisplayed => false",
                       "ProvesWinningGpo => false",
                       "PermitsAutomaticRemediation => false",
                       'Source candidate', 'Not verified', 'Observed concern',
                       'Session', 'NoDispScrSavPage'):
            if marker == "endpointUser.IsFresh(now)":
                self.assertIn("endpoint.IsFresh(now)", readiness)
            else:
                self.assertIn(marker, readiness)
        self.assertNotIn("Process.Start(", pilot)
        self.assertNotIn("ModifyRequest(", pilot)

    def test_standalone_mode_tests_localization_and_release(self):
        self.assertIn('args.Contains("--endpoint-user-probe"', read("Program.cs"))
        self.assertIn('"Endpoint user Screen Saver probe...", OpenEndpointUserProbe',
                      read("MainForm.cs"))
        smoke = read("UiLayoutSmokeTests.cs")
        self.assertIn("EndpointUserProbeDialog()", smoke)
        self.assertIn('"Import user HKCU JSON..."', smoke)
        self.assertIn('"Capture this Windows user"', smoke)
        for marker in ("Power114Russian", "Power114Hebrew",
                       "Power114EnglishByRussian", "Power114EnglishByHebrew"):
            self.assertIn(marker, read("UiLocale.Power114.cs"))
            self.assertIn(marker, read("UiLocale.cs"))
        for marker in ("TestEndpointUserReadinessEvidence();",
                       "EndpointUserReadinessEvidence.ParseJson(",
                       "EndpointUserReadinessEvidence.LoadLocalJson(",
                       "!result.SignedByDomain", "IsFresh(now)",
                       "PermitsAutomaticRemediation"):
            self.assertIn(marker, read("SelfTests.cs"))
        self.assertIn("0.4.0-preview.114", read("LockScreenCampaignManager.csproj"))
        self.assertTrue((ROOT / "docs" / "RELEASE-PREVIEW114.md").is_file())

if __name__ == "__main__":
    unittest.main(verbosity=2)
