"""Read-only CI contracts: explicit update check and persisted Wizard skip-save.

No network access, per-user writes, or Windows dialogs are invoked here.
Native WinForms tests separately validate the actual control and DPI layout.
"""
import unittest
from pathlib import Path

SRC = Path(__file__).resolve().parents[1] / "src" / "LockScreenCampaignManager"

def read(name):
    return (SRC / name).read_text(encoding="utf-8")

class OneClickAndPreferenceTests(unittest.TestCase):
    def test_explicit_menu_and_indicator_both_auto_check(self):
        manager = read("MainForm.cs")
        notice = read("MainForm.UpdateNotification.cs")
        updater = read("UniversalUpdaterWindow.cs")
        menu = manager.split("private void OpenUniversalUpdater()", 1)[1].split(
            "private void OpenVisualDpiCheck()", 1)[0]
        self.assertIn("new UniversalUpdaterWindow(checkOnOpen: true)", menu)
        self.assertIn("new UniversalUpdaterWindow(checkOnOpen: true)", notice)
        self.assertIn("if (checkOnOpen)", updater)
        self.assertIn("Shown += async (_, _) => await CheckAsync();", updater)
        self.assertIn('T("Check again")', updater)
        # Check-on-open can only call CheckAsync, never stage or apply directly.
        automatic = updater.split("if (checkOnOpen)", 1)[1].split(
            "private static TableLayoutPanel Field(", 1)[0]
        self.assertNotIn("InstallSelectedAsync(", automatic)
        self.assertNotIn("DownloadAsync(", automatic)
        self.assertNotIn("InstallAndRestart(", automatic)
        self.assertIn("if (_operation is not null) return;", updater)

    def test_save_checkbox_persists_only_as_user_preference(self):
        style = read("UiStyle.cs")
        wizard = read("MainForm.CampaignWizard.cs")
        settings = read("UiSettingsDialog.cs")
        self.assertIn("public bool WizardSkipLocalProjectSave { get; set; }", style)
        self.assertIn('Path.Combine(AppLog.DirectoryPath, "ui-settings.json")', style)
        self.assertIn("File.Move(temp, SettingsFile, true)", style)
        self.assertIn("public static void RememberWizardSkipLocalProjectSave(bool skip)", style)
        self.assertIn("Preferences.WizardSkipLocalProjectSave = skip;", style)
        self.assertIn("Preferences.Save();", style)
        self.assertIn("UiStyle.Preferences.WizardSkipLocalProjectSave;", wizard)
        self.assertIn("UiStyle.RememberWizardSkipLocalProjectSave(_skipProjectSave);", wizard)
        self.assertIn("persistWizardSaveOption: false", wizard)
        self.assertIn("WizardSkipLocalProjectSave = UiStyle.Preferences.WizardSkipLocalProjectSave", settings)
        self.assertIn("Could not remember the local save choice for next time.", wizard)
        self.assertNotIn("WizardSkipLocalProjectSave", read("CampaignModels.cs"))

    def test_ui_translations_and_native_legacy_roundtrip(self):
        ru = read("UiLocale.Russian.cs")
        he = read("UiLocale.cs") + read("UiLocale.Wizard.cs")
        native = read("SelfTests.cs")
        for key in ("Check again",):
            self.assertIn('["' + key + '"]', ru)
            self.assertIn('["' + key + '"]', he)
        self.assertIn("saved.WizardSkipLocalProjectSave", native)
        self.assertIn("legacyPreferences.WizardSkipLocalProjectSave", native)

if __name__ == "__main__":
    unittest.main(verbosity=2)
