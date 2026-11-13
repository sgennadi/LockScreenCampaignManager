"""Preview.107: offline read-only endpoint power observation safety contracts."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"

def load(name):
    return (SRC / name).read_text(encoding="utf-8")

class EndpointPowerAuditContracts(unittest.TestCase):
    def test_remote_reads_are_preceded_by_scoped_ldap_and_same_identity(self):
        native = load("NativeEndpointPowerObservation.cs")
        self.assertIn('mode != "Observe"', native)
        self.assertIn('NativeAdDiscovery.ValidateNames(domain, dc)', native)
        self.assertIn('NativeGpoConflictScan.SafeComputerName(computer, domain)', native)
        self.assertIn('using (var ldap = NativeAdDiscovery.Connect(dc, credentials))', native)
        self.assertIn('ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;', native)
        self.assertIn('NativeGpoLifecycleList.SameController(dc, actualDc)', native)
        self.assertIn('SearchScope.Base', native)
        self.assertIn('SearchScope.Subtree', native)
        self.assertIn('search.SizeLimit = 2;', native)
        self.assertIn('(flags & 2) != 0', native)
        self.assertIn('NativeGpoConflictScan.Inside(response.Entries[0].DistinguishedName, ouDn)', native)
        self.assertIn('NativeAdDiscovery.EscapeFilter(computer)', native)
        self.assertLess(native.index('using (var ldap = NativeAdDiscovery.Connect(dc, credentials))'),
                        native.index('var plan = ReadActivePlan(computer)'))
        self.assertIn('WindowsIdentity.RunImpersonated(token, Run)', native)
        self.assertIn('LogonUser(user, netbios, credentials.Password, 9, 3', native)
        forbidden = ('ModifyRequest(', 'AddRequest(', 'DeleteRequest(', 'CreateProcess',
                     'Process.Start', 'Win32_Process', 'Win32_ScheduledJob',
                     'Invoke-Command', 'WinRM', 'RunAs', 'gpupdate.exe', 'powercfg.exe',
                     'SetValue(', 'Activate(', 'File.WriteAllText(')
        for term in forbidden:
            self.assertNotIn(term, native, term)

    def test_wmi_guids_are_validated_through_associations(self):
        native = load("NativeEndpointPowerObservation.cs")
        self.assertIn('@"root\cimv2\power"', native)
        self.assertIn('Win32_PowerPlan', native)
        self.assertIn('Win32_PowerSettingDataIndex', native)
        self.assertIn('row.GetRelated("Win32_PowerSetting")', native)
        self.assertIn('TrySettingKind(id, out string kind)', native)
        self.assertIn('TryIndexSupply(instance, activeGuid, out string supply)', native)
        self.assertIn('SettingIndexValue', native)
        self.assertIn('PolicyRoot + guid', native)
        self.assertIn('InactivityTimeoutSecs', native)
        self.assertIn('writable: false', native)
        self.assertIn('RegistryView.Registry64', native)
        self.assertIn('Display', native)
        self.assertIn('Sleep', native)
        self.assertIn('MachineLock', native)
        self.assertIn('if (activePlan is null)', native)
        self.assertIn('return new PlanRead("Unavailable"', native)

    def test_models_never_claim_effective_winner_or_live_display(self):
        parser = load("EndpointPowerObservation.cs")
        form = load("GpoOperationsForm.PowerAudit.cs")
        dialog = load("ScreenSaverEndpointPowerDialog.cs")
        router = load("DomainPowerShellRunner.cs")
        self.assertIn('mode == "ObservePower"', router)
        self.assertIn('NativeEndpointPowerObservation.Execute("Observe"', router)
        for text in ('adScopeVerified', 'observed > now.AddMinutes(2)',
                     'TimeSpan.FromMinutes(5)', 'Readings=readings',
                     'PotentialEarly', 'HasFullyVerifiedVisibility => false',
                     'Duplicate endpoint power reading', 'Never (this setting only)',
                     'potential blocker'):
            self.assertIn(text, parser)
        self.assertIn('IsFresh', load("ClientHealthAdInventory.cs"))
        self.assertIn('ReadPowerGpoCandidates(scan, _domain, _ouDn)', form)
        self.assertIn('computer = "", policyScope = "Computer"', form)
        self.assertIn('Linked-GPO candidate scan NOT VERIFIED', form)
        self.assertIn('Source', dialog)
        self.assertIn('Configured User Screen Saver is not verified', dialog)
        self.assertIn('No winning GPO, active display state', dialog)
        self.assertIn('GpoVerifyPowerButton', load("GpoOperationsForm.cs"))
        self.assertIn('TestEndpointPowerObservation();', load("SelfTests.cs"))
        self.assertIn('CreateEndpointPowerSmoke', load("UiLayoutSmokeTests.cs"))

    def test_display_translations_are_dedicated(self):
        values = load("UiLocale.Power107.cs")
        locale = load("UiLocale.cs")
        for key in ("Power107Russian", "Power107Hebrew",
                    "Power107EnglishByRussian", "Power107EnglishByHebrew"):
            self.assertIn(key, values)
            self.assertIn(key, locale)
        for tag in ('"EndpointPowerReadingsGrid"', '"EndpointPowerCandidateGrid"'):
            self.assertIn(tag, locale)
        self.assertIn('Screen Saver power audit', locale)
        self.assertIn('Не проверено', values)
        self.assertIn('לא אומת', values)

if __name__ == "__main__":
    unittest.main(verbosity=2)
