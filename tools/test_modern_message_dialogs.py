"""Preview.113 modern dialogs and custom SCR/EXE pilot source contracts."""
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/"src"/"LockScreenCampaignManager"
def read(name): return (SRC/name).read_text(encoding="utf-8")
class ModernTests(unittest.TestCase):
    def test_dialog_safe_update_default(self):
        d=read("ModernMessageDialog.cs")
        for t in ("internal static class MessageBox", "SafeDismissal",
            "MessageBoxDefaultButton.Button2", "DialogResult.No", "Keys.Escape",
            "System.Windows.Forms.MessageBox.Show(", "UiStyle.Apply(this)",
            "AutoScaleMode.Dpi", "ScrollBars = ScrollBars.Vertical"):
            self.assertIn(t,d)
        u=read("UniversalUpdaterWindow.cs")
        self.assertIn("if (answer != DialogResult.Yes) return;",u)
        self.assertIn("MessageBoxDefaultButton.Button2",u)
    def test_custom_pilot_gpo_fail_closed(self):
        x=read("CustomSaverPilotFile.cs")
        for t in ("SHA256.HashData(stream)", "ReadExactly(dos)", "ReadExactly(pe)",
            "WindowsAuthenticodeAudit.Inspect(full, cacheOnly: true)", "FileAttributes.ReparsePoint",
            "ValidateStored(", "160L * 1024 * 1024"):
            self.assertIn(t,x)
        for t in ("Process.Start(", "ModifyRequest(", "File.Copy(", "File.WriteAllText("):
            self.assertNotIn(t,x)
        self.assertIn("InspectPeOnlyForSelfTest(Application.ExecutablePath)", read("SelfTests.cs"))
        sign=read("WindowsAuthenticodeAudit.cs")
        self.assertIn("WTD_CACHE_ONLY_URL_RETRIEVAL", sign)
        self.assertIn("0x00001000u", sign)
        self.assertIn("bool cacheOnly = false", sign)
        wizard=read("MainForm.CampaignWizard.Mode.cs")
        self.assertIn("WizardScreenSaverBrowseCustom",wizard)
        self.assertIn("CustomSaverPilotFile.Inspect(picker.FileName)",wizard)
        self.assertIn("CustomExecutableSha256",wizard)
        self.assertIn("Custom Screen Saver pilot only",read("MainForm.CampaignWizard.cs"))
        self.assertIn("_project.ScreenSaver.CustomExecutablePath.Length > 0",
                      read("DomainDeploymentForm.AssignmentDeployment.cs"))
        self.assertIn("TestCustomSaverPilotSelection();",read("SelfTests.cs"))
    def test_ru_he_version(self):
        ui=read("UiLocale.Power113.cs")
        for t in ("Power113Russian", "Power113Hebrew",
                  "Browse .scr / .exe...", "Power113EnglishByRussian"):
            self.assertIn(t,ui)
        self.assertIn("0.4.0-preview.113",read("LockScreenCampaignManager.csproj"))
        self.assertTrue((ROOT/"docs"/"RELEASE-PREVIEW113.md").is_file())
if __name__=="__main__": unittest.main(verbosity=2)
