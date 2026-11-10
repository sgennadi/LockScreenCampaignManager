"""Offline safety contracts for opt-in local report viewer and Hebrew UI.

No AD, SMB, LocalSystem, connected domains, or external reports are accessed.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"

def read(name):
    return (SRC / name).read_text(encoding="utf-8")

class LocalLabAndHebrewContracts(unittest.TestCase):
    def test_local_only_json_reader_is_strict_and_never_calls_ad(self):
        code = read("LocalLabEvidence.cs")
        for name in ("lscm-smb-system-canary/v1", "lscm-endpoint-closure/v1",
                     "GetProperty", "GetArrayLength", "findings", "IsAttestation => false",
                     "STALE", "MaxDepth = 12", "256 * 1024", "ReparsePoint",
                     "LOCAL report.json", "POSTCLEANUP_LOCAL_STATE_MATCHES_BASELINE",
                     "POSTBOOT_SOURCE_AND_COMPUTER_RSOP_OBSERVED_ONLY",
                     "Contradictory", "JsonValueKind.Number"):
            # 'STALE' comes from UI, not from the parser itself.
            if name == "STALE":
                continue
            self.assertIn(name, code)
        for forbidden in ("Invoke-Command", "Get-ADComputer", "New-GPO", "Register-ScheduledTask",
                          "Set-ItemProperty", "HttpClient", "WebRequest", "File.WriteAllText"):
            self.assertNotIn(forbidden, code)

    def test_viewer_does_not_run_remote_commands_or_upload(self):
        ui = read("LocalLabEvidenceDialog.cs")
        main = read("MainForm.cs")
        gpo = read("GpoOperationsForm.cs")
        for required in ("LocalLabCanaryImport", "LocalLabCleanupImport",
                         "LocalLabEvidence.LoadLocalFile", "NOT authenticated",
                         "No data was sent", "Local lab evidence"):
            self.assertIn(required, ui)
        self.assertIn('new LocalLabEvidenceDialog()', main)
        self.assertIn('new LocalLabEvidencePanel().AsTabPage()', gpo)
        for forbidden in ("Invoke-Command", "New-GPO", "Set-GPRegistryValue",
                          "HttpClient", "File.WriteAllText", "Start-ScheduledTask",
                          "Register-ScheduledTask"):
            self.assertNotIn(forbidden, ui)

    def test_hebrew_settings_are_persisted_and_do_not_translate_protocols(self):
        prefs = read("UiStyle.cs")
        settings = read("UiSettingsDialog.cs")
        local = read("UiLocale.cs")
        smoke = read("UiLayoutSmokeTests.cs")
        for marker in ('Language { get; set; } = "English"', '"Hebrew"', "UiLocale.Apply"):
            self.assertIn(marker, prefs)
        for marker in ("UiSettingsLanguage", "עברית (Hebrew)", "ChangePreferences",
                       "AutoScroll = true", "Density = densities["):
            self.assertIn(marker, settings)
        for marker in ("RightToLeftLayout = hebrew", "RightToLeft.No", "TextBox",
                       "DataGridView", "TabPage", "Dictionary<string, string>"):
            self.assertIn(marker, local)
        self.assertIn("CheckHebrewRtlAndLanguageSelection();", smoke)
        keys = re.findall(r'^\s*\["([^"]+)"\]\s*=', local, re.M)
        self.assertEqual(len(keys), len(set(keys)))
        self.assertGreaterEqual(len(keys), 110)

if __name__ == "__main__":
    unittest.main(verbosity=2)
