"""Preview.115 unified AD-first GPO applicability (no gpresult XML required)."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"

def read(name: str) -> str:
    return (SRC / name).read_text(encoding="utf-8")

class UnifiedAdGpoContracts(unittest.TestCase):
    def test_existing_all_gpo_scanner_reused(self):
        source = read("NativeGpoDomainAudit.cs")
        scoped = read("NativeGpoDomainAudit.PilotScope.cs")
        router = read("DomainPowerShellRunner.cs")
        self.assertIn("internal static partial class NativeGpoDomainAudit", source)
        self.assertIn('if (mode == "PilotScope")', source)
        self.assertIn("ExecutePilotScope(request, credentials)", source)
        for x in ("ReadAllGpos(", "ReadTargetLinks(", "ParseRegistryPol(",
                  "ParseGppRegistry(", "ParseGppPowerOptions("):
            self.assertIn(x, source)
        self.assertIn('mode == "AllGpos"', router)
        self.assertIn("NativeGpoDomainAudit.Execute(", router)
        for x in ("ReadScopedLinks(", "ScopeTokensVerified(",
                  "NativeGpoConflictScan.ParseRegistryPol(",
                  "QualifyComputerRead(", "computerForUser!", "readOnly: true",
                  "computer GPO Read denied", "computer Read not verified",
                  "NativeGpoConflictScan.PolicyHalfHasConfiguredSettings("):
            self.assertIn(x, scoped)
        for forbidden in ("Process.Start(", "ModifyRequest(", "Registry.SetValue(",
                          "gpupdate.exe", "File.WriteAllText(", "File.Delete("):
            self.assertNotIn(forbidden, scoped)

    def test_ad_ancestry_nested_groups_security_and_loopback_are_conservative(self):
        model = read("NativeGpoDomainAudit.PilotScope.cs")
        for key in ("tokenGroups", "SearchPaged(", "SearchScope.Base",
                    "NativeAdDiscovery.Connect(", "ReferralChasingOptions.None",
                    "SessionOptions", "nTSecurityDescriptor",
                    "SecurityDescriptorFlagControl(SecurityMasks.Dacl)",
                    "RawSecurityDescriptor", "ObjectAce",
                    "ScopeApplyRight", "Read+Apply", "Deny",
                    "gPLink", "gPOptions", "ReadAncestry(",
                    "Classify(", "Excluded: block inheritance",
                    "Excluded: matching security deny",
                    "NativeGpoLifecycleList.ParseLinks",
                    "DecideLoopback(", "UserPolicyMode",
                    "Merge candidate", "Replace candidate",
                    "Computer OU (loopback)", "Conditional: WMI not evaluated",
                    "TryGpoFlags(", "QualifyLoopbackUserRow(",
                    "Conditional: loopback Replace candidate",
                    "Conditional: loopback unknown",
                    "Conditional: loopback not confirmed",
                    "Not configured", "Unknown",
                    "provesWinningGpo = false", "provesClientApplication = false"):
            self.assertIn(key, model, key)
        # No optimistic 'winning GPO' or direct endpoint policy modification
        self.assertNotIn('provesClientApplication = true', model)
        self.assertNotIn('provesWinningGpo = true', model)

    def test_one_readiness_screen_and_optional_xml(self):
        scope = read("DomainGpoScopeEvidence.cs")
        readiness = read("DomainGpoPilotReadiness.cs")
        ui = read("DomainGpoPilotReadinessDialog.cs")
        all_ui = read("DomainWideGpoReportDialog.cs")
        form = read("GpoOperationsForm.DomainAudit.cs")
        for key in ("DomainWideGpoReport report", "requestedUser",
                    "computerRead", "Excluded: computer GPO Read denied",
                    "Conditional: computer Read not verified",
                    "provesWinningGpo", "provesClientApplication",
                    "Fresh(DateTimeOffset now)", "GetArrayLength() > 750",
                    "NativeGpoConflictScan.Inside("):
            self.assertIn(key, scope, key)
        for key in ("AdScopeObserved", "AddAdScope(", "adScope is not null",
                    "Computer GPO Read", "link.ComputerRead",
                    "bool currentAd = adScope is not null",
                    "BlockerEvidencePriority(",
                    "Historical AD scope", "UserXmlMatchesScope(",
                    "boundUserXml", "User XML identity correlation",
                    "DomainGpoScopeEvidence? adScope = null",
                    "DomainPilotBlocker", "User OU",
                    "Computer OU (loopback)", "Not verified",
                    "not applied", "AD-only (no XML)"):
            self.assertIn(key, readiness, key)
        for key in ("PilotVerifyAdScope", "PilotAdUserSamOrUpn",
                    "VerifyAdScopeAsync()", "DomainGpoScopeEvidence",
                    "Analyze AD scope (no XML)...",
                    "_adScope = null", "DateTimeOffset.UtcNow, _endpointUser, _adScope"):
            self.assertIn(key, ui, key)
        self.assertIn("AllDcGpoUnifiedPilotButton", all_ui)
        self.assertIn("new DomainGpoPilotReadinessDialog(", all_ui)
        self.assertIn("new DomainGpoRsopDialog(", all_ui)
        self.assertIn('"PilotScope"', form)
        self.assertIn("DomainGpoScopeEvidence.Parse(", form)
        self.assertIn("_pilotAdScopeReader", read("DomainGpoRsopDialog.cs"))

    def test_locale_selftests_version_and_notes(self):
        local = read("UiLocale.Power115.cs")
        core = read("UiLocale.cs")
        for key in ("Power115Russian", "Power115Hebrew",
                    "Power115EnglishByRussian", "Power115EnglishByHebrew"):
            self.assertIn(key, local)
            self.assertIn(key, core)
        tests = read("SelfTests.cs")
        for key in ("TestUnifiedAdScopeApplicability();",
                    "DomainGpoScopeEvidence.Parse(", "AdScopeObserved",
                    "wrongIdentityRejected", "Expired", "AD-only (no XML)",
                    "defaultFlags", "bothDisabled", "computerOnly",
                    "incomplete", "combined",
                    "foreignUserXml", "matchedUserXml",
                    "unknownRead", "deniedReview", "QualifyComputerRead",
                    "missingTokenRejected", "ScopeTokensVerified"):
            if key == "Expired":
                self.assertIn("var expired =", tests)
            else:
                self.assertIn(key, tests)
        self.assertIn("UiLocale.Power115.cs", (ROOT / "tools" /
            "test_manager_features.py").read_text(encoding="utf-8"))
        self.assertIn("0.4.0-preview.119",
            read("LockScreenCampaignManager.csproj"))
        self.assertTrue((ROOT / "docs" / "RELEASE-PREVIEW119.md").is_file())
        self.assertTrue((ROOT / "docs" / "RELEASE-PREVIEW118.md").is_file())
        self.assertTrue((ROOT / "docs" / "RELEASE-PREVIEW117.md").is_file())
        self.assertTrue((ROOT / "docs" / "RELEASE-PREVIEW115.md").is_file())

if __name__ == "__main__":
    unittest.main(verbosity=2)
