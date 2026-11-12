"""Preview.104 read-only OU/group direct coverage contracts.

Run without AD, network, GPO or Windows. Hosted CI does not claim actual
membership, policy effectiveness or inactive-device filtering.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src" / "LockScreenCampaignManager"

def source(filename):
    return (APP / filename).read_text(encoding="utf-8")

class DirectCoverageContracts(unittest.TestCase):
    def test_ldap_counts_each_principal_once_per_direct_group(self):
        native = source("NativeAdDiscovery.cs")
        between = native.split("private static object DirectGroupsForOu(", 1)[1].split(
            "private static object[] Groups(", 1)[0]
        for token in (
            "SearchScope.Base",
            "(objectClass=organizationalUnit)",
            'Search(ldap, ouDn, principalFilter, QueryLimit, "memberOf")',
            "principals.Count >= QueryLimit",
            'var directGroupCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)',
            "var uniqueMemberships = new HashSet<string>(StringComparer.OrdinalIgnoreCase)",
            "uniqueMemberships.Add(dn)",
            "directGroupCounts.GetValueOrDefault(dn) + 1",
            "principalCount = principals.Count",
            "directGroupCounts = directGroupCounts.OrderBy",
            "directMemberCount = pair.Value",
            "directMembershipOnly = true",
            "nestedGroupsExpanded = false",
            "primaryGroupsIncluded = false",
            "readOnly = true",
        ):
            self.assertIn(token, between, token)
        for forbidden in ("ModifyRequest", "AddRequest", "DeleteRequest",
                          "Set-AD", "Set-GP", "WriteAllText", '"tokenGroups"',
                          "sAMAccountName", "userPrincipalName"):
            self.assertNotIn(forbidden, between, forbidden)

    def test_coverage_parser_is_scoped_and_fail_closed(self):
        code = source("OuGroupDirectCoverage.cs")
        for token in (
            "expectedOuDn",
            "expectedKind",
            'expectedKind is not ("Computer" or "User")',
            'StringComparison.OrdinalIgnoreCase',
            'data.GetProperty("readOnly").ValueKind != JsonValueKind.True',
            'data.GetProperty("directMembershipOnly").ValueKind != JsonValueKind.True',
            'data.GetProperty("nestedGroupsExpanded").ValueKind != JsonValueKind.False',
            'data.GetProperty("primaryGroupsIncluded").ValueKind != JsonValueKind.False',
            "principals >= 5000",
            "TryAdd(dn, matched)",
            "returnedDns.SetEquals(counts.Keys)",
            "DirectMatchesFor(string? groupDn)",
        ):
            self.assertIn(token, code, token)
        tests = source("SelfTests.cs")
        for token in (
            "TestOuGroupDirectCoverage();",
            "snapshot.DirectMatchesFor(groupA.ToUpperInvariant()) == 3",
            "snapshot.DirectMatchesFor(groupC) == 0",
            "snapshot.PrincipalKind == \"User\"",
            "Evidence(computerOu, \"Computer\", 5000",
            "Evidence(computerOu, \"Computer\", 4, [(groupA, 5)]",
            "Evidence(computerOu, \"Computer\", 4, [(groupA, 1), (groupA, 1)]",
        ):
            self.assertIn(token, tests, token)

    def test_both_target_scopes_show_counts_without_implying_policy_success(self):
        deployment = source("DomainDeploymentForm.cs")
        dialog = source("CampaignAssignmentsDialog.cs")
        smoke = source("UiLayoutSmokeTests.cs")
        for token in (
            'Name = "DeploymentOuGroupCoverage"',
            "_computerCoverage = null;",
            "OuGroupDirectCoverage.Parse(",
            "UpdateCoverageStatus();",
            "selected group: {1} direct matches.",
            "not effective GPO coverage.",
        ):
            self.assertIn(token, deployment, token)
        for token in (
            'Name = "AssignmentComputerCoverage"',
            'Name = "AssignmentUserCoverage"',
            'Name = "AssignmentScopeCoverageRows"',
            "UpdateCoverageLabel(false)",
            "UpdateCoverageLabel(true)",
            "OuGroupDirectCoverage coverage =",
            "await _membershipLookup(selectedOuDn, userScope)",
            "var allowed = coverage.DirectGroupDns.ToHashSet(",
            "Inactive accounts included. Not effective GPO application.",
            "filter.Checked = false",
        ):
            self.assertIn(token, dialog, token)
        self.assertIn("not verified", deployment)
        self.assertIn("not verified", dialog)
        self.assertIn("AssignmentComputerCoverage", smoke)
        self.assertIn("AssignmentUserCoverage", smoke)
        self.assertIn("DeploymentOuGroupCoverage", smoke)
        self.assertNotIn("new GpoLink", source("OuGroupDirectCoverage.cs"))

    def test_locale_and_privacy(self):
        locale = source("UiLocale.Coverage104.cs")
        ui = source("UiLocale.cs")
        for key in ("Coverage104Russian", "Coverage104Hebrew",
                    "Coverage104EnglishByRussian", "Coverage104EnglishByHebrew"):
            self.assertIn(key, locale)
            self.assertIn(key + ".TryGetValue", ui)
        ru = locale.split("Coverage104Russian =", 1)[1].split(
            "Coverage104Hebrew =", 1)[0]
        he = locale.split("Coverage104Hebrew =", 1)[1].split(
            "Coverage104EnglishByRussian =", 1)[0]
        pairs = []
        for d in (ru, he):
            keys = re.findall(r'^\s*\["([^"]+)"\]\s*=', d, re.M)
            vals = re.findall(r'^\s*\["[^"]+"\]\s*=\s*"([^"]+)"', d, re.M)
            self.assertGreaterEqual(len(keys), 8)
            self.assertEqual(len(keys), len(set(keys)))
            self.assertEqual(len(vals), len(set(vals)), "Reverse map ambiguous")
            pairs.append(keys)
        self.assertEqual(set(pairs[0]), set(pairs[1]))
        for forbidden in ("LdapConnection", "DomainCredentials", "ModifyRequest", "New-GPO",
                          "HttpClient", "HttpWebRequest", "File.WriteAllText"):
            self.assertNotIn(forbidden, locale)

if __name__ == "__main__":
    unittest.main(verbosity=2)
