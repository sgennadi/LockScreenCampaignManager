"""Preview.124 wizard Review & Save source contracts (offline, read-only).

Native WinForms DPI/UI smoke tests in the Windows build verify controls,
checkbox behaviour and navigation without opening a SaveFileDialog.
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"


def read(name):
    return (SRC / name).read_text(encoding="utf-8")


class WizardReviewTests(unittest.TestCase):
    def test_one_primary_next_and_optional_local_save(self):
        code = read("MainForm.CampaignWizard.cs")
        review = code.split("private void RenderSave(", 1)[1].split(
            "private void AddStorageSelector(", 1
        )[0]
        self.assertIn('Name = "WizardSkipLocalProjectSave"', review)
        self.assertIn('Text = T("Skip saving a local project (.json)")', review)
        self.assertIn("Checked = _skipProjectSave", review)
        self.assertIn("_skipProjectSave = skip.Checked", review)
        self.assertIn('Name = "WizardSaveReviewLayout"', review)
        self.assertIn('Name = "WizardCampaignNameField"', review)
        self.assertIn('Name = "CampaignWizardSaveStorageOptions"', review)
        self.assertIn("stack.Controls.Add(group, 0, 0)", review)
        self.assertIn("stack.Controls.Add(storage, 0, 1)", review)
        self.assertNotIn('Action(actions, "Validate campaign"', review)
        self.assertNotIn('Action(actions, "Save project..."', review)
        self.assertNotIn('Action(actions, "Skip saving & continue"', review)
        self.assertIn('page.RowStyles[2].Height = 0', code)
        self.assertIn("actions.Controls.Count == 0", code)
        self.assertIn("_next.Click += (_, _) => NextStep()", code)
        self.assertIn('ShouldSaveCampaignProject(_saved, _skipProjectSave)', code)
        self.assertIn('if (!_owner.TrySaveProject()) return;', code)
        self.assertIn('CampaignSchedule.Validate(_owner._project, requireImages: true)', code)
        self.assertIn('private bool ConfirmSavedProjectIsOutdated()', code)

    def test_wizard_smoke_checks_checkbox_and_no_prompt(self):
        code = read("UiLayoutSmokeTests.cs")
        self.assertIn("WizardSkipLocalProjectSave", code)
        self.assertIn("skip.Checked = true", code)
        self.assertIn("skip.Checked = false", code)
        self.assertIn("ShouldSaveCampaignProject(saved: false, skipSaving: skip.Checked)", code)
        self.assertIn('"CampaignWizardContinueAction"', code)
        self.assertIn('"Campaign Wizard - review and save"', code)
        self.assertIn('"Campaign Wizard - Screen Saver-only review"', code)
        self.assertNotIn('"Save & Next", "Back"', code)

    def test_ru_he_and_numbered_release(self):
        en = read("MainForm.CampaignWizard.cs")
        ru = read("UiLocale.Russian.cs")
        he = read("UiLocale.Wizard.cs")
        keys = (
            "6  Review & save", "Review & save", "Review and continue",
            "Skip saving a local project (.json)", "Local .json save optional",
            "Local .json save skipped",
            "Next validates the campaign and offers to save a .json file.",
            "Next validates and continues without a local .json file.",
        )
        for key in keys:
            self.assertIn(key, en, key)
            self.assertIn('["' + key + '"]', ru, key)
            self.assertIn('["' + key + '"]', he, key)
        self.assertIn("0.4.0-preview.124", read("LockScreenCampaignManager.csproj"))
        self.assertTrue((ROOT / "docs" / "RELEASE-PREVIEW124.md").is_file())


if __name__ == "__main__":
    unittest.main(verbosity=2)
