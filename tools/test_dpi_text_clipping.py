"""Preview.116 WinForms high-DPI clipping regressions, offline and read-only."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"

def read(name: str) -> str:
    return (SRC / name).read_text(encoding="utf-8")

class DpiTextClippingContracts(unittest.TestCase):
    def test_screen_saver_browse_has_full_width_auto_row(self):
        source = read("MainForm.CampaignWizard.Mode.cs")
        for marker in (
            'Name = "WizardScreenSaverPickerLayout"',
            'ColumnCount = 2, RowCount = 2',
            'saverRow.RowStyles.Add(new RowStyle(SizeType.AutoSize))',
            'saverRow.Controls.Add(customBrowse, 1, 1)',
            'Name = "WizardScreenSaverBrowseCustom"',
            'AutoSizeMode = AutoSizeMode.GrowAndShrink',
            'AutoEllipsis = false',
            'Name = "WizardScreenSaverPilotNotice"',
            'details.RowStyles.Add(i is 3 or 5 or 6 or 8 or 9',
            'note.MaximumSize = new Size(textWidth, 0)',
            'customInfo.MaximumSize = new Size(textWidth, 0)',
        ):
            self.assertIn(marker, source, marker)
        self.assertNotIn(
            'saverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155))',
            source)

    def test_assignment_field_labels_wrap_and_scroll(self):
        source = read("CampaignAssignmentsDialog.cs")
        for marker in (
            'MinimumSize = new Size(0, height)',
            'AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink',
            'table.RowStyles.Add(new RowStyle(SizeType.AutoSize))',
            'caption.MaximumSize = new Size(width, 0)',
            'fields.RowStyles.Add(i switch',
            '14', '15', 'RowStyle(SizeType.AutoSize)',
        ):
            self.assertIn(marker, source, marker)
        self.assertNotIn(
            'table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34))',
            source)

    def test_modern_dialog_header_body_footer_remain_readable(self):
        source = read("ModernMessageDialog.cs")
        for marker in (
            'void FitTextChrome()', 'TextFormatFlags.WordBreak',
            'headerText.RowStyles[0].Height = headingHeight',
            'headerText.RowStyles[1].Height = hintHeight',
            'outer.RowStyles[1].Height = Math.Max(112, requiredHeader)',
            'outer.RowStyles[3].Height = Math.Max(76',
            'int bodyMinimum = Math.Max(105',
            'body.SelectionStart = 0',
            'body.ScrollToCaret()', 'TabStop = false',
            'DefaultAction = values[index].Result',
            'DialogResult = SafeDismissal',
            'SafeDismissal = values.Any',
            'Math.Min(450, Math.Max(300, display.Height - 90))'
        ):
            self.assertIn(marker, source, marker)

    def test_deployment_version_never_fixed_220_only(self):
        source = read("DomainDeploymentForm.cs")
        for marker in (
            'Name = "DeploymentHeaderTitle"',
            'void FitDeploymentHeader()', 'TextRenderer.MeasureText(',
            'pageHeader.ColumnStyles[1].Width = Math.Max(220, needed)',
            'layout.RowStyles[0].Height = Math.Max(42,',
            'deploymentVersion.FontChanged +=',
            'pageHeader.SizeChanged +=',
            'Name = "DeploymentVersion"'):
            self.assertIn(marker, source, marker)
        self.assertIn('AutoEllipsis = false', source)

    def test_native_smoke_checks_real_controls_and_languages(self):
        source = read("UiTextClippingSmoke.cs")
        smoke = read("UiLayoutSmokeTests.cs")
        for marker in (
            'WizardScreenSaverBrowseCustom',
            'WizardScreenSaverPickerLayout',
            'WizardScreenSaverPilotNotice',
            'AssignmentScrollableFields',
            'DeploymentVersion', 'DeploymentHeaderTitle',
            'ModernDialogHeaderTitle', 'ModernDialogHeaderHint',
            'ModernDialogBody', 'ModernDialogActionButtons',
            'TextRenderer.MeasureText(', 'Check(Form form, string profile)'
        ):
            self.assertIn(marker, source, marker)
        for marker in (
            'UiTextClippingSmoke.Check(form, profile.Name)',
            'CheckThreeLanguageClipping();',
            '"English", "Russian", "Hebrew"',
            'new[] { 100, 150, 200 }',
            '"Group target validation warning — offline simulated layout"',
            '"Check target assignments"'
        ):
            self.assertIn(marker, smoke, marker)
        self.assertIn("0.4.0-preview.121", read("LockScreenCampaignManager.csproj"))
        self.assertTrue((ROOT / "docs" / "RELEASE-PREVIEW116.md").is_file())

if __name__ == "__main__":
    unittest.main(verbosity=2)
