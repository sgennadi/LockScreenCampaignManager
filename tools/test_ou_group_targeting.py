"""Offline contracts: OU member-based group filtering and readable deployment UI.

No AD/SMB/GPO request is executed. Actual LDAP membership still needs a real
Windows domain endpoint and a read-only operator verification.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src" / "LockScreenCampaignManager"

def source(filename):
    return (APP / filename).read_text(encoding="utf-8")

class OuMembershipAndDeployUiTests(unittest.TestCase):
    def test_native_ldap_filter_is_read_only_and_validates_ou(self):
        discovery = source("NativeAdDiscovery.cs")
        for token in (
            '"ComputerGroupsForOu" =>', '"UserGroupsForOu" =>',
            'SearchScope.Base', 'objectCategory=organizationalUnit',
            'objectCategory=computer', 'objectCategory=person',
            'principal.Attributes["memberOf"]',
            'directMembershipOnly = true',
            'nestedGroupsExpanded = false',
            'primaryGroupsIncluded = false',
            'principals.Count >= QueryLimit',
            'EndsWith("," + baseDn, StringComparison.OrdinalIgnoreCase)',
        ):
            self.assertIn(token, discovery, token)
        body = discovery.split("private static object DirectGroupsForOu(", 1)[1].split(
            "private static object[] Groups(", 1)[0]
        for forbidden in ("new SearchRequest(dn, ", "ModifyRequest",
                          "AddRequest", "DeleteRequest", "Set-ADGroup",
                          "Register-ScheduledTask", "WriteAllText"):
            self.assertNotIn(forbidden, body)

    def test_computer_group_picker_does_not_filter_by_group_object_ou(self):
        deploy = source("DomainDeploymentForm.cs")
        for token in (
            'Name = "DeploymentGroupListMode"',
            'Groups with computers in selected OU (direct)',
            'All domain security groups (choose group first)',
            '"ComputerGroupsForOu"',
            "directGroupDns",
            "_directComputerGroupDns.Contains(row.Dn)",
            "await Task.Delay(300)",
            "_membershipRequestVersion",
            "Select the OU to LINK the GPO",
            'DistinctBy(row => row.Dn, StringComparer.OrdinalIgnoreCase)',
        ):
            self.assertIn(token, deploy, token)
        self.assertIn("User OU / groups...", deploy)
        self.assertIn("Computer + User groups...", deploy)
        self.assertIn("RefreshComputerGroupMembershipAsync", deploy)
        self.assertNotIn("row.Dn.StartsWith(selectedOu)", deploy)

    def test_screensaver_uses_user_filter_not_computer_policy(self):
        matrix = source("CampaignAssignmentsDialog.cs")
        form = source("DomainDeploymentForm.cs")
        for token in (
            'AssignmentFilterComputerGroupsByOu',
            'AssignmentFilterUserGroupsByOu',
            'Only groups with direct COMPUTER members',
            'Only groups with direct USER members',
            'await RefreshScopeGroupChoicesAsync(true)',
            'await RefreshScopeGroupChoicesAsync(false)',
            'filter.Checked = false',
            'SelectedDn(ou)',
            'Nested groups / primary groups are not expanded',
        ):
            self.assertIn(token, matrix, token)
        self.assertIn('"UserGroupsForOu" : "ComputerGroupsForOu"', form)
        self.assertIn('FindScopedMemberGroupsAsync', form)
        self.assertIn('No loopback/Replace is configured automatically.', matrix)
        self.assertIn('Screen Saver is a USER policy', matrix)

    def test_group_first_preserves_exact_dn_across_ou_changes(self):
        matrix = source("CampaignAssignmentsDialog.cs")
        deploy = source("DomainDeploymentForm.cs")
        tests = source("SelfTests.cs")
        for value in (
            "MustShowAllToKeepGroup(chosenDn, allowed)",
            'ReplaceGroupChoices(groups, null, chosenDn)',
            'ReplaceGroupChoices(_computerGroup, null, current.ComputerGroupDn)',
            'ReplaceGroupChoices(_userGroup, null, current.UserGroupDn)',
            'existing selection retained',
            'selected OU\'s DIRECT membership list',
            'await Task.Delay(250)',
        ):
            self.assertIn(value, matrix, value)
        # Never empty the list before the LDAP lookup has completed.
        self.assertNotIn(
            'ReplaceGroupChoices(groups, new HashSet<string>(StringComparer.OrdinalIgnoreCase));',
            matrix,
        )
        self.assertIn("if (IsOuGroupFilterActive && _groups.SelectedItem is DirectoryRow chosen)", deploy)
        self.assertIn('_groupListMode.SelectedIndex = 1; // Cancels older async lookups.', deploy)
        self.assertIn("if (!string.IsNullOrWhiteSpace(_company.DefaultGroupDn))", deploy)
        self.assertIn("MustShowAllToKeepGroup(", tests)
        self.assertIn("group-first selection", tests)

    def test_preview103_target_labels_translated_without_changing_ad_values(self):
        ui = source("UiLocale.cs")
        target = source("UiLocale.Targeting103.cs")
        deploy = source("DomainDeploymentForm.cs")
        matrix = source("CampaignAssignmentsDialog.cs")
        smoke = source("UiLayoutSmokeTests.cs")
        import re
        for prefix in ("Targeting103Russian", "Targeting103Hebrew"):
            self.assertIn(prefix + " = new(StringComparer.Ordinal)", target)
            body = target.split(prefix + " = new(StringComparer.Ordinal)", 1)[1].split("};", 1)[0]
            keys = re.findall(r'^\\s*\\["([^"]+)"\\]\\s*=', body, re.M)
            self.assertGreaterEqual(len(keys), 55)
            self.assertEqual(len(keys), len(set(keys)))
            for key in (
                "OU member groups (computers)",
                "All domain groups (group first)",
                "User OU / groups...",
                "Computer + User groups...",
                "Filter user groups by selected OU",
                "Filter computer groups by selected OU",
                "Group chosen. Select the OU to LINK the GPO; a security group is not a GPO link.",
            ):
                self.assertIn(key, keys, (prefix, key))
        for token in (
            "Targeting103EnglishByRussian.TryGetValue",
            "Targeting103EnglishByHebrew.TryGetValue",
            "Targeting103Russian.TryGetValue",
            "Targeting103Hebrew.TryGetValue",
            '"DeploymentGroupListMode"',
        ):
            self.assertIn(token, ui)
        self.assertIn('UiLocale.Display(_project.EffectiveCampaignMode switch', deploy)
        self.assertIn('UiLocale.Display("Group chosen. Select the OU', deploy)
        self.assertIn('"Filter computer groups by selected OU"', matrix)
        self.assertIn('"Filter user groups by selected OU"', matrix)
        self.assertIn('SizeType.AutoSize) // Long translated warning', matrix)
        self.assertIn('fields.RowStyles[14].SizeType != SizeType.AutoSize', smoke)
        self.assertNotIn('selectedOu.Dn + translated', deploy)
        self.assertNotIn('row.Dn = UiLocale', deploy)

    def test_browse_has_independent_full_width_layout(self):
        deploy = source("DomainDeploymentForm.cs")
        smoke = source("UiLayoutSmokeTests.cs")
        self.assertIn('Name = "DeploymentContentFolderRow"', deploy)
        self.assertIn('folderRow.Controls.Add(_contentShareBrowse, 1, 0)', deploy)
        self.assertIn('folderRow.Controls.Add(_contentShareBox, 0, 0)', deploy)
        self.assertIn('new ColumnStyle(SizeType.Absolute, 166)', deploy)
        self.assertNotIn('storage.Controls.Add(_contentShareBrowse, 3, 0)', deploy)
        self.assertIn('folderRow.GetControlFromPosition(1, 0) != browse', smoke)
        self.assertIn('AssignmentFilterUserGroupsByOu', smoke)
        self.assertIn('DeploymentGroupListMode', smoke)

if __name__ == "__main__":
    unittest.main(verbosity=2)
