"""Preview.110: dual-scope gpresult XML source binding and safety checks."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"

def source(name):
    return (SRC / name).read_text(encoding="utf-8")

class DomainGpresultContracts(unittest.TestCase):
    def test_reuse_strict_local_file_reader_and_safe_user_scope(self):
        computer = source("RsopEvidence.cs")
        user = source("UserRsopEvidence.cs")
        for marker in (
            "ReadLocalXmlDocument(path)", "FileShare.Read",
            "SHA256.HashData(stream)", "FileAttributes.ReparsePoint",
            'full.StartsWith(@"\\\\")', "DtdProcessing = DtdProcessing.Prohibit",
            "XmlResolver = null", "MaxBytes", "Path.GetFullPath(path)",
        ):
            self.assertIn(marker, computer, marker)
        for marker in (
            "RsopEvidence.ReadLocalXmlDocument(path)",
            "RsopEvidence.SafeXml", 'x.Name.LocalName == "UserResults"',
            'root.Name.NamespaceName != Namespace', 'LoggedData',
            "principal.Equals(expectedPrincipal", "reportedDomain.Equals(",
            "seen.Add(id)", "RsopEvidence.GuidFromGpo(item)",
            "RsopEvidence.Bool(item", '"Reported applied"',
            '"Reported filtered"', '"Insufficient flags"',
            "IsSignedEndpointAttestation => false",
            "ProvesActualSettingPrecedence => false",
        ):
            self.assertIn(marker, user, marker)
        for forbidden in ("Process.Start(", "ModifyRequest(", "SetValue(",
                          "File.WriteAllBytes(", "File.WriteAllText(",
                          "PowerShell", "Win32_Process", "gpupdate.exe"):
            self.assertNotIn(forbidden, user, forbidden)

    def test_exact_guid_and_scope_never_become_winning_policy(self):
        model = source("DomainGpoRsopCorrelation.cs")
        for marker in (
            "DomainWideGpoReport report, RsopEvidence? computer, UserRsopEvidence? user",
            'x.Scope == "Computer"', "ObservationFor(x.GpoId)",
            '"Reported applied"', '"Reported filtered"',
            '"Not listed (unknown)"', '"Not imported"',
            "Per-setting precedence, source version and live behavior NOT verified.",
            "Neither imported file is signed or remotely collected.",
            "User/Computer evidence may describe different machines or sessions.",
            "x.LinkStatus",
        ):
            self.assertIn(marker, model, marker)
        self.assertIn("TestDomainRsopCorrelation();", source("SelfTests.cs"))
        self.assertIn("TestDomainRsopCorrelation()", source("SelfTests.cs"))

    def test_ui_has_two_independent_imports_and_fails_closed(self):
        dlg = source("DomainGpoRsopDialog.cs")
        for marker in (
            "GpresultImportComputer", "GpresultImportUser",
            "GpresultClearComputer", "GpresultClearUser",
            "GpresultComparisonGrid", "GpresultComparisonScope",
            "GpresultComparisonObservation", "ConfirmPrivateImport()",
            "UserRsopEvidence.LoadLocalXml(", "RsopEvidence.LoadLocalXml(",
            "incoming.IsHistorical", "ConfirmHistorical(incoming.ReadUtc)",
            "domain", "Typed identities and imported XML",
        ):
            if marker == "domain":
                self.assertIn("_report.Domain", dlg)
            elif marker == "Typed identities and imported XML":
                self.assertIn("Typed identities and imported XML", dlg)
            else:
                self.assertIn(marker, dlg, marker)
        self.assertIn("_computer = null", dlg)
        self.assertIn("_user = null", dlg)
        self.assertIn("new DomainGpoRsopDialog(_report)", source("DomainWideGpoReportDialog.cs"))
        self.assertIn("AllDcGpoRsopButton", source("DomainWideGpoReportDialog.cs"))
        self.assertIn("CreateGpoRsopSmoke()", source("UiLayoutSmokeTests.cs"))
        self.assertIn('"Compare gpresult XML..."', source("UiLayoutSmokeTests.cs"))

    def test_russian_hebrew_and_readonly_release_contract(self):
        locale = source("UiLocale.Power110.cs")
        host = source("UiLocale.cs")
        for key in ("Power110Russian", "Power110Hebrew",
                    "Power110EnglishByRussian", "Power110EnglishByHebrew"):
            self.assertIn(key, locale)
            self.assertIn(key, host)
        self.assertIn("0.4.0-preview.110",
                      source("LockScreenCampaignManager.csproj"))
        release = (ROOT / "docs" / "RELEASE-PREVIEW110.md").read_text(encoding="utf-8")
        self.assertIn("User", release)
        self.assertIn("Computer", release)
        self.assertIn("not", release.lower())

if __name__ == "__main__":
    unittest.main(verbosity=2)
