"""Preview.105 aggregate AD computer activity evidence (offline-only contracts)."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src" / "LockScreenCampaignManager"

def src(name):
    return (APP / name).read_text(encoding="utf-8")

class ComputerActivityContracts(unittest.TestCase):
    def test_only_computer_ou_ldap_queries_extra_attributes(self):
        code = src("NativeAdDiscovery.cs")
        scoped = code.split("private static object DirectGroupsForOu(", 1)[1].split(
            "private static object[] Groups(", 1)[0]
        self.assertIn('principalKind == "Computer"', scoped)
        self.assertIn('["memberOf", "userAccountControl", "lastLogonTimestamp"]', scoped)
        self.assertIn('OuComputerActivityAccumulator? activity = null;', scoped)
        self.assertIn('Attr(ReadRootDse(ldap), "currentTime")', scoped)
        self.assertIn('activity?.Record(', scoped)
        self.assertIn('activity = activity?.Export(directGroupCounts)', scoped)
        self.assertIn('SearchScope.Base', scoped)
        self.assertIn('if (principals.Count >= QueryLimit)', scoped)
        for forbidden in ("ModifyRequest", "AddRequest", "DeleteRequest",
                          "WriteAllText", "Set-AD", "Set-GP", '"tokenGroups"'):
            self.assertNotIn(forbidden, scoped)

    def test_activity_accounting_no_false_online_claims(self):
        code = src("OuComputerActivityCoverage.cs")
        for token in (
            "OuComputerActivityAccumulator", "OuComputerActivityCoverage",
            '"replicatedLastLogonTimestamp"',
            '"yyyyMMddHHmmss\'.0Z\'"',
            "DateTimeOffset.FromFileTime(fileTime)",
            "(flags & 0x2) != 0", "Disabled++;",
            "UnknownState++;", "UnknownTimestamp++;",
            "int[] days = [30, 90, 180, 365];",
            "validRecent",  # lower case token not needed: see below
        )[:-1]:
            self.assertIn(token, code, token)
        self.assertIn("private static bool ValidRecent", code)
        self.assertIn("values[0] <= values[1]", code)
        self.assertIn("values[2] <= values[3]", code)
        self.assertIn("!byGroup.TryAdd(dn, values)", code)
        self.assertIn("byGroup.Count != directGroupCounts.Count", code)
        self.assertIn("no valid", src("UiLocale.Activity105.cs"))
        for forbidden in ("sAMAccountName", "userPrincipalName", "dNSHostName",
                          "DirectoryEntry", "ModifyRequest", "File.WriteAllText",
                          "LdapConnection", "HttpClient"):
            self.assertNotIn(forbidden, code)

    def test_fail_closed_parser_and_diagnostics_only_ui(self):
        coverage = src("OuGroupDirectCoverage.cs")
        for token in (
            "internal OuComputerActivityCoverage? Activity", 
            'expectedKind != "Computer"',
            "OuComputerActivityCoverage.Parse(observed, principals, counts)"
        ):
            self.assertIn(token, coverage)
        deployment = src("DomainDeploymentForm.cs")
        assignment = src("CampaignAssignmentsDialog.cs")
        for text, control in ((deployment, "DeploymentActivityWindow"),
                              (assignment, "AssignmentActivityWindow")):
            self.assertIn(control, text)
            for days in (30, 90, 180, 365):
                self.assertIn(f'"Last {days} days"', text)
            self.assertIn("Does not change GPO targeting.", text)
            self.assertIn("activity.Recent(days)", text)
            self.assertIn("activity.GroupRecent(", text)
            self.assertNotIn("WriteLastLogon", text)
        self.assertIn("if (userScope)", assignment)
        self.assertIn("TestOuComputerActivityCoverage();", src("SelfTests.cs"))
        smoke = src("UiLayoutSmokeTests.cs")
        for name in ("DeploymentActivityWindow", "AssignmentActivityWindow"):
            self.assertIn(name, smoke)

    def test_trilingual_safe_values(self):
        text = src("UiLocale.Activity105.cs")
        dictionary_lines = re.findall(r'^\s*\["([^"]+)"\]\s*=', text, re.M)
        self.assertEqual(len(dictionary_lines), 16)
        self.assertEqual(len(set(dictionary_lines)), 8)
        for key in ("Last 30 days", "Last 90 days", "Last 180 days",
                    "Last 365 days"):
            self.assertEqual(dictionary_lines.count(key), 2)
        ui = src("UiLocale.cs")
        for key in ("Activity105EnglishByRussian", "Activity105EnglishByHebrew",
                    "Activity105Russian", "Activity105Hebrew",
                    '"DeploymentActivityWindow"', '"AssignmentActivityWindow"'):
            self.assertIn(key, ui)

if __name__ == "__main__":
    unittest.main(verbosity=2)
