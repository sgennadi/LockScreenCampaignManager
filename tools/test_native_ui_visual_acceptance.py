"""Offline contracts for the operator-driven physical DPI acceptance UI.

No domain, network, report export, native UI or external process is run here.
The actual Windows monitor remains an explicitly unverified manual gate.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"

def read(name):
    return (SRC / name).read_text(encoding="utf-8")

class NativeVisualAcceptanceContracts(unittest.TestCase):
    def test_accessible_and_not_automatic(self):
        form = read("MainForm.cs")
        dialog = read("UiVisualAcceptanceDialog.cs")
        smoke = read("UiLayoutSmokeTests.cs")
        for token in ('"Visual DPI / language check...", OpenVisualDpiCheck',
                      "using var form = new UiVisualAcceptanceDialog();",
                      "form.ShowDialog(this);"):
            self.assertIn(token, form)
        for token in ('Name = "UiVisualAcceptanceDialog"',
                      'Name = "VisualAcceptanceScroll"',
                      'Name = "VisualAcceptanceDisplayStatus"',
                      'Name = "VisualAcceptanceSave"',
                      "AutoScroll = true",
                      "UiVisualAcceptanceDialog()",
                      'new CheckBox {',
                      'box.CheckedChanged +=',
                      'RefreshDisplay();',
                      '_operatorChecks.All(x => x.Checked)',
                      'Meets200PercentLocalConsole(_lastDpi, _localConsole)',
                      'if (!_save.Enabled) return;'):
            self.assertIn(token, dialog)
        self.assertEqual(dialog.count('_operatorChecks.Add(box)'), 1)
        self.assertIn('("Visual DPI / language acceptance", () => new UiVisualAcceptanceDialog()', smoke)
        self.assertIn('checks.Length != 6', smoke)
        self.assertIn('save is null || save.Enabled', smoke)

    def test_actual_win32_dpi_and_remote_rejection(self):
        dialog = read("UiVisualAcceptanceDialog.cs")
        for token in (
            'GetDpiForWindow(IntPtr hwnd)',
            'WTSGetActiveConsoleSessionId()',
            'GetSystemMetrics(RemoteSessionMetric) == 0',
            'Process.GetCurrentProcess().SessionId == active',
            '!SystemInformation.TerminalServerSession',
            'GetDpiForWindow(Handle)',
            'dpi == 192 && localConsole',
            'DpiChanged +=',
            'LocationChanged +=',
            'ScalePercent(_lastDpi)',
        ):
            self.assertIn(token, dialog)
        self.assertNotIn('SetProcessDpiAwareness', dialog)
        self.assertNotIn('SetDisplayConfig', dialog)
        self.assertNotIn('ChangeDisplaySettings', dialog)

    def test_manual_observations_and_private_unsigned_report(self):
        dialog = read("UiVisualAcceptanceDialog.cs")
        for token in (
            'const string AcceptedResult = "LOCAL_OPERATOR_OBSERVATIONS_RECORDED_NOT_ATTESTED"',
            'Process.Start(start)',
            'start.ArgumentList.Add("--ui-smoke-test")',
            '_smokePassed = process.ExitCode == 0',
            'TimeSpan.FromMinutes(5)',
            'new CancellationTokenSource',
            'new { id = "visual-" + (i + 1), confirmed = check.Checked }',
            'FileAttributes.ReparsePoint',
            'Path.Combine(AppLog.DirectoryPath, "VisualAcceptance")',
            'attested = false',
            'screensCaptured = false',
            'networkTransmitted = false',
            'domainActionsPerformed = false'
        ):
            self.assertIn(token, dialog)
        for forbidden in ("HttpClient", "HttpWebRequest", "DirectoryEntry",
                          "LdapConnection", "PowerShell", "Set-GP", "New-GPO",
                          "Get-AD", "Screenshot", "CopyFromScreen", "Clipboard.Set",
                          "WriteAllBytes", "ProcessStartInfo { FileName = \"powershell"):
            self.assertNotIn(forbidden, dialog)
        self.assertNotIn("Report saved to GitHub", dialog)

    def test_localization_is_present_and_reversible(self):
        locale = read("UiLocale.cs")
        dicts = read("UiLocale.Visual102.cs")
        for lang in ("Russian", "Hebrew"):
            self.assertIn("Visual102" + lang, dicts)
            self.assertIn("Visual102" + lang + ".TryGetValue", locale)
        self.assertIn("Visual102EnglishByRussian.TryGetValue", locale)
        self.assertIn("Visual102EnglishByHebrew.TryGetValue", locale)
        self.assertIn('"Visual DPI / language acceptance"', locale)
        keys = re.findall(r'^\s*\["([^"]+)"\]\s*=', dicts, re.M)
        self.assertGreaterEqual(len(keys), 40)
        self.assertEqual(len(keys) // 2, len(set(keys)))

if __name__ == "__main__":
    unittest.main(verbosity=2)
