"""Preview.103: EN/RU/HE OU and GPO assignment UI strings.

Entirely offline. Source-only checks; does not query LDAP or apply policies.
"""
import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src" / "LockScreenCampaignManager"


def read(name):
    return (APP / name).read_text(encoding="utf-8")


def locale_entries(name):
    text = read("UiLocale.Targeting103.cs")
    anchor = f"Dictionary<string, string> {name} = new(StringComparer.Ordinal)"
    assert anchor in text, f"Missing dictionary {name}"
    body = text.split(anchor, 1)[1].split("};", 1)[0]
    matches = re.findall(
        r'^\s*\[("(?:\\.|[^"\\])*")\]\s*=\s*("(?:\\.|[^"\\])*"),',
        body, re.M)
    entries = [(json.loads(key), json.loads(value)) for key, value in matches]
    return entries


class Targeting103LocalisationTests(unittest.TestCase):
    def test_dictionaries_reversible_and_placeholders_complete(self):
        ru = locale_entries("Targeting103Russian")
        he = locale_entries("Targeting103Hebrew")
        for entries in (ru, he):
            self.assertGreaterEqual(len(entries), 55)
            keys = [key for key, _ in entries]
            values = [v for _, v in entries]
            self.assertEqual(len(set(keys)), len(keys), "duplicate source keys")
            self.assertEqual(len(set(values)), len(values), "reverse lookup collisions")
            for source, translated in entries:
                src_args = sorted(re.findall(r"\{(\d+)\}", source))
                tr_args = sorted(re.findall(r"\{(\d+)\}", translated))
                self.assertEqual(src_args, tr_args, f"Format placeholders: {source}")
                self.assertNotEqual(source, translated, source)
        self.assertEqual(set(dict(ru)), set(dict(he)))

    def test_display_only_combo_keeps_technical_enumerations(self):
        locale = read("UiLocale.cs")
        deploy = read("DomainDeploymentForm.cs")
        for needle in (
            "Targeting103EnglishByRussian.TryGetValue",
            "Targeting103EnglishByHebrew.TryGetValue",
            "Targeting103Russian.TryGetValue",
            "Targeting103Hebrew.TryGetValue",
            '"DeploymentGroupListMode"',
        ):
            self.assertIn(needle, locale)
        self.assertIn('_groupListMode.Items.Add("OU member groups (computers)")', deploy)
        self.assertIn('_groupListMode.Items.Add("All domain groups (group first)")', deploy)
        self.assertIn("UiLocale.Display(_project.EffectiveCampaignMode switch", deploy)
        self.assertIn("RightToLeft", deploy)
        self.assertNotIn('row.Dn = UiLocale', deploy)
        self.assertNotIn('group.Dn = UiLocale', deploy)

    def test_help_and_status_are_translated_without_touching_ad(self):
        dialog = read("CampaignAssignmentsDialog.cs")
        deploy = read("DomainDeploymentForm.cs")
        smoke = read("UiLayoutSmokeTests.cs")
        for token in (
            '"Filter computer groups by selected OU"',
            '"Filter user groups by selected OU"',
            "UiLocale.Display(",
            "UiLocale.Display(userScope ? \"User\" : \"Computer\")",
            "SizeType.AutoSize) // Long translated warning",
            "AssignmentModeHelp",
        ):
            self.assertIn(token, dialog)
        self.assertIn("UiLocale.Display(_project.EffectiveCampaignMode switch", deploy)
        self.assertIn("UiLocale.Display(\"OU membership filter unavailable or incomplete.", deploy)
        self.assertIn("fields.RowStyles[14].SizeType != SizeType.AutoSize", smoke)
        self.assertIn("StringComparison.OrdinalIgnoreCase", smoke)
        for forbidden in ("Set-GPLink", "New-GPO", "WriteAllBytes",
                          "SessionOptions.Signing", "LdapConnection"):
            self.assertNotIn(forbidden, read("UiLocale.Targeting103.cs"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
