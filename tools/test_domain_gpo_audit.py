"""Preview.108: domain-wide selected-DC GPO source scan safety contracts."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"

def read(name):
    return (SRC / name).read_text(encoding="utf-8")

class AllDcGpoAuditContracts(unittest.TestCase):
    def test_all_gpcs_enumerated_paged_and_pinned_to_selected_dc(self):
        native = read("NativeGpoDomainAudit.cs")
        for text in (
            'mode != "AllGpos"', 'NativeAdDiscovery.ValidateNames(domain, dc)',
            'NativeAdDiscovery.Connect(dc, credentials)',
            'ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None',
            'NativeGpoLifecycleList.SameController(dc, actualDc)',
            '"(objectClass=groupPolicyContainer)", SearchScope.OneLevel',
            'new PageResultRequestControl(200)', 'returned.Cookie',
            'gpos.Count', 'TryLinkedGuid(', 'SearchScope.Base',
            'NativeGpoConflictScan.ParentDn(node)', 'NativeGpoConflictScan.Inside(',
            'NativeGpoConflictScan.EnsureDirectory(sysvol)', 'actualDc',
            'using (token) return WindowsIdentity.RunImpersonated(token, Run)',
            'GetProperty("screenSaverIdleSeconds")', 'observedUtc'):
            self.assertIn(text, native, text)
        self.assertIn('if (output.Count >= 2000)', native)
        self.assertIn('if (rows.Count >= 6000)', native)
        self.assertIn('if (issues.Count >= 300)', native)
        self.assertIn('!knownIds.Contains(x.Id)', native)
        self.assertNotIn('Directory.GetFiles(@"\\\\', native)
        for banned in ("Process.Start(", "PowerShell", "ModifyRequest(", "AddRequest(",
                       "DeleteRequest(", "Win32_Process", "SetValue(", "Activate(",
                       "File.WriteAllText(", "File.WriteAllBytes(", "gpupdate.exe"):
            self.assertNotIn(banned, native, banned)

    def test_reuses_native_policy_source_readers_and_distinguishes_applicability(self):
        native = read("NativeGpoDomainAudit.cs")
        conflict = read("NativeGpoConflictScan.cs")
        for parser in ("ParseRegistryPol(bytes, user)",
                       "ParseGppRegistry(bytes, user)",
                       "ParseGppPowerOptions(bytes)"):
            self.assertIn(parser, native, parser)
        self.assertIn("DomainSecurityTemplateBlockers.Parse(bytes)", native)
        inf = read("DomainSecurityTemplateBlockers.cs")
        self.assertIn('"Registry Values"', inf)
        self.assertIn("InactivityTimeoutSecs", inf)
        self.assertIn("DecoderFallbackException", inf)
        self.assertIn("MaxLines", inf)
        self.assertIn("RegistryCseAdvertised(cse)", native)
        self.assertIn("PolicyHalfHasConfiguredSettings(", native)
        self.assertIn("PolicyFolderExistsSafe(path)", native)
        self.assertIn("FileExistsSafe(file)", native)
        for marker in ('"Linked candidate"', '"Outside selected OUs"',
                       '"Inactive/filtered"', '"User OU not verified"',
                       '"Computer OU not verified"',
                       'hasWmi ? "WMI filter present; "', '"No GPO"'):
            if marker != '"No GPO"':
                self.assertIn(marker, native)
        self.assertIn('name == "userpolicymode"', native)
        self.assertIn('n == "userpolicymode"', conflict)
        self.assertIn('ScreenSaverPowerConflictAnalysis.Compare(', native)
        self.assertIn('No remote execution', native)

    def test_fresh_evidence_ui_localization_and_local_export(self):
        model = read("DomainWideGpoReport.cs")
        ui = read("DomainWideGpoReportDialog.cs")
        form = read("GpoOperationsForm.DomainAudit.cs")
        for text in ('TimeSpan.FromMinutes(5)', 'ObservedUtc',
                     'rows.Count(x => x.Risk == "High")',
                     'complete != (errors.Length == 0)',
                     'SetEquals(expected)', 'BuildCsv(', '"=+-@"'):
            self.assertIn(text, model)
        for text in ('AllDcGpoSearch', 'AllDcGpoScopeFilter',
                     'AllDcGpoRiskFilter', 'AllDcGpoLinkFilter',
                     'ExportCsv()', 'OverwritePrompt = true',
                     'FileAttributes.ReparsePoint', 'RightToLeft.No'):
            self.assertIn(text, ui)
        self.assertIn('DomainOperation.ConflictScan, "AllGpos"', form)
        self.assertIn('userOus', form)
        self.assertIn('computerOus', form)
        self.assertIn('GpoScanAllDcButton', read("GpoOperationsForm.cs"))
        self.assertIn('NativeGpoDomainAudit.Execute(', read("DomainPowerShellRunner.cs"))
        self.assertIn('TestDomainWideGpoAudit();', read("SelfTests.cs"))
        self.assertIn('CreateAllDcGpoSmoke', read("UiLayoutSmokeTests.cs"))
        for key in ('Power108Russian', 'Power108Hebrew',
                    'Power108EnglishByRussian', 'Power108EnglishByHebrew'):
            self.assertIn(key, read("UiLocale.Power108.cs"))
            self.assertIn(key, read("UiLocale.cs"))

if __name__ == "__main__":
    unittest.main(verbosity=2)
