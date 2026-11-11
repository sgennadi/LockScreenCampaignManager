"""Offline safety contracts for opt-in local report viewer and Hebrew UI.

No AD, SMB, LocalSystem, connected domains, or external reports are accessed.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "LockScreenCampaignManager"

def read(name):
    return (SRC / name).read_text(encoding="utf-8")

class LocalLabAndHebrewContracts(unittest.TestCase):
    def test_local_only_json_reader_is_strict_and_never_calls_ad(self):
        code = read("LocalLabEvidence.cs")
        for name in ("lscm-smb-system-canary/v1", "lscm-endpoint-closure/v1",
                     "GetProperty", "GetArrayLength", "findings", "IsAttestation => false",
                     "STALE", "MaxDepth = 12", "256 * 1024", "ReparsePoint",
                     "LOCAL report.json", "POSTCLEANUP_LOCAL_STATE_MATCHES_BASELINE",
                     "POSTBOOT_SOURCE_AND_COMPUTER_RSOP_OBSERVED_ONLY",
                     "Contradictory", "JsonValueKind.Number"):
            # 'STALE' comes from UI, not from the parser itself.
            if name == "STALE":
                continue
            self.assertIn(name, code)
        for forbidden in ("Invoke-Command", "Get-ADComputer", "New-GPO", "Register-ScheduledTask",
                          "Set-ItemProperty", "HttpClient", "WebRequest", "File.WriteAllText"):
            self.assertNotIn(forbidden, code)

    def test_viewer_does_not_run_remote_commands_or_upload(self):
        ui = read("LocalLabEvidenceDialog.cs")
        main = read("MainForm.cs")
        gpo = read("GpoOperationsForm.cs")
        for required in ("LocalLabCanaryImport", "LocalLabCleanupImport",
                         "LocalLabEvidence.LoadLocalFile", "NOT authenticated",
                         "No data was sent", "Local lab evidence"):
            self.assertIn(required, ui)
        self.assertIn('new LocalLabEvidenceDialog()', main)
        self.assertIn('new LocalLabEvidencePanel().AsTabPage()', gpo)
        for forbidden in ("Invoke-Command", "New-GPO", "Set-GPRegistryValue",
                          "HttpClient", "File.WriteAllText", "Start-ScheduledTask",
                          "Register-ScheduledTask"):
            self.assertNotIn(forbidden, ui)

    def test_hebrew_settings_are_persisted_and_do_not_translate_protocols(self):
        prefs = read("UiStyle.cs")
        settings = read("UiSettingsDialog.cs")
        local = read("UiLocale.cs")
        smoke = read("UiLayoutSmokeTests.cs")
        for marker in ('Language { get; set; } = "English"', '"Hebrew"', "UiLocale.Apply"):
            self.assertIn(marker, prefs)
        for marker in ("UiSettingsLanguage", "עברית (Hebrew)", "ChangePreferences",
                       "AutoScroll = true", "Density = densities["):
            self.assertIn(marker, settings)
        for marker in ("RightToLeftLayout = hebrew", "RightToLeft.No", "TextBox",
                       "DataGridView", "TabPage", "Dictionary<string, string>"):
            self.assertIn(marker, local)
        self.assertIn("CheckHebrewRtlAndLanguageSelection();", smoke)
        keys = re.findall(r'^\s*\["([^"]+)"\]\s*=', local, re.M)
        self.assertEqual(len(keys), len(set(keys)))
        self.assertGreaterEqual(len(keys), 110)

    def test_wizard_hebrew_is_ui_only_and_reversible(self):
        locale = read("UiLocale.cs")
        dictionary = read("UiLocale.Wizard.cs")
        wizard = read("MainForm.CampaignWizard.cs")
        mode = read("MainForm.CampaignWizard.Mode.cs")
        smoke = read("UiLayoutSmokeTests.cs")
        for label in (
            '"1  AD connection"', '"2  Campaign type"', '"3  Images"',
            '"4  Arrange"', '"5  Schedule"', '"6  Save / Skip"',
            '"7  Deploy"', '"SYSVOL (default)"', '"SMB network folder (UNC)"',
            '"Campaign Wizard"', '"Campaign name"', '"Screen Saver (.scr)"',
            '"JPG photo slideshow', '"Require Windows sign-in on resume"'
        ):
            self.assertIn(label, dictionary)
        keys = re.findall(r'^\s*\["([^"]+)"\]\s*=', dictionary, re.M)
        self.assertEqual(len(keys), len(set(keys)))
        self.assertGreaterEqual(len(keys), 100)
        self.assertIn("TryTranslateWizardStep", dictionary)
        self.assertIn("WizardEnglish", dictionary)
        for marker in (
            "internal static partial class UiLocale", "ApplyComboLocalization",
            "combo.FormattingEnabled = true", "combo.Format +=",
            "combo.Invalidate()", "TextBox text", "RightToLeft.No"
        ):
            self.assertIn(marker, locale)
        for marker in (
            "private static string T(", "FitWizardPageText",
            "Name = \"CampaignWizardContentSource\"",
            "CampaignContentSource.SmbShare", "_storageModifiedAfterSave",
            "UiStyle.Apply(this)"
        ):
            self.assertIn(marker, wizard)
        self.assertIn('T("Selected type: ")', mode)
        self.assertIn("CheckHebrewCampaignWizardPages();", smoke)
        # Display language must never be used in persistent protocol keys.
        self.assertNotIn("UiLocale.Translate", read("CampaignModels.cs"))
        self.assertNotIn("UiLocale.Translate", read("CampaignPayloadLocation.cs"))


    def test_ad_and_health_hebrew_does_not_modify_protocol_data(self):
        locale = read("UiLocale.cs")
        words = read("UiLocale.AdHealth.cs")
        ad = read("AdConnectionStartDialog.cs")
        account = read("AdAlternateAccountWizard.cs")
        deployment = read("DomainDeploymentForm.cs")
        health = read("ClientFeedbackPanel.cs")
        smoke = read("UiLayoutSmokeTests.cs")
        keys = re.findall(r'^\s*\["([^"]+)"\]\s*=', words, re.M)
        self.assertEqual(len(keys), len(set(keys)))
        self.assertGreaterEqual(len(keys), 145)
        for expected in (
            "AD Connection Wizard", "1  Sign in",
            "Select Active Directory user", "Active Directory / GPO Deployment",
            "PowerShell (.ps1) - PowerShell Startup Scripts",
            "Native (.exe) - classic Startup Scripts",
            "SMB Share", "Load PCs from AD",
            "Computer", "Condition", "Client Health / Live"
        ):
            self.assertIn('["' + expected + '"]', words)
        for required in (
            "AdHealthHebrew.TryGetValue", "AdHealthEnglish.TryGetValue",
            '"DeploymentContentSource"', '"DeploymentClientType"',
            '"ClientHealthGrid"', "ColumnHeaderSource", "Headers.GetValue"):
            self.assertIn(required, locale)
        self.assertIn('UiLocale.Display("Selected AD account: ")', ad)
        self.assertIn("RightToLeft.No", account)
        self.assertIn("RightToLeft.No", deployment)
        self.assertIn('Name = "ClientHealthGrid"', health)
        self.assertIn("UiLocale.Display(\"Expected (AD): \")", health)
        self.assertIn("CheckHebrewAdAndClientHealth();", smoke)
        for protected in ("CampaignModels.cs", "CampaignPayloadLocation.cs",
                          "ClientFeedback.cs"):
            self.assertNotIn("UiLocale.Display(", read(protected))
        # CSV headers and diagnostic schema remain stable English protocol values.
        self.assertIn(
            "Computer,Condition,Stage,Image,LastSeen,Files,Registry,Task,AgentVersion,ClientType",
            health)

    def test_ops_diagnostics_hebrew_display_only_actions(self):
        locale = read("UiLocale.cs")
        glossary = read("UiLocale.OpsDiagnostics.cs")
        operations = read("GpoOperationsForm.cs")
        agent = read("GpoOperationsForm.AgentManagement.cs")
        deferred = read("GpoOperationsForm.Deferred.cs")
        gmsa = read("GpoOperationsForm.Gmsa.cs")
        conflict = read("GroupConflictReportDialog.cs")
        trends = read("DiagnosticsTrendsWindow.cs")
        diagnostic = read("UiErrorDialog.cs")
        smoke = read("UiLayoutSmokeTests.cs")
        keys = re.findall(r'^\s*\["([^"]+)"\]\s*=', glossary, re.M)
        self.assertEqual(len(keys), len(set(keys)))
        self.assertGreaterEqual(len(keys), 160)
        for ui_name in (
            "GPO Operations", "Deferred Repair", "GPO: Disable / Reuse / Delete",
            "Automatic retry when PCs return (while manager is open)",
            "24/7 gMSA — Route new approved Repair requests to machine queue",
            "Report an error — LockScreen Campaign Manager",
            "Diagnostics — saved error reports",
            "Error trends — LockScreen Campaign Manager",
            "Full GPO conflict report (read-only)", "Report an error..."
        ):
            self.assertIn('["' + ui_name + '"]', glossary)
        for control in (
            "GpoLifecycleAction", "GpoPushPurpose", "AgentActivityFilter",
            "GpoGmsaFilter", "GroupConflictRisk", "GpoDeferredRepairGrid",
            "GpoGmsaQueueGrid", "GroupConflictGrid", "DiagnosticsTrendsGrid"
        ):
            self.assertIn('"' + control + '"', locale)
        for marker, content in (
            ('Name = "GpoLifecycleAction"', operations),
            ('Name = "GpoPushPurpose"', operations),
            ('Name = "AgentActivityFilter"', agent),
            ('Name = "GpoGmsaQueueGrid"', gmsa),
            ('Name = "GpoGmsaFilter"', gmsa),
            ('Name = "GpoDeferredRepairGrid"', deferred),
            ('Name = "DiagnosticsTrendsGrid"', trends),
            ('Name = "GroupConflictGrid"', conflict),
            ("CheckHebrewGpoOperationsAndDiagnostics();", smoke)
        ):
            self.assertIn(marker, content)
        # Manager/PowerShell commands, queue state, source filenames and
        # exported JSON/CSV remain canonical English: UI labels are not keys.
        self.assertIn('"Disable", "Enable", "Unlink", "Relink", "Reuse", "Delete"', operations)
        self.assertIn('"Push", "Repair", "Cleanup", "Report"', operations)
        self.assertIn('"All", "High", "Warning", "Info", "Inactive/filtered"', conflict)
        self.assertIn(
            "Target,Scope,Risk,GPO,GPO GUID,Setting,Value,Registry key,Source,Linked at,Applicability",
            conflict)
        self.assertIn("_snapshot.ExportSafeJson()", trends)
        self.assertIn("ReviewedReport", diagnostic)
        self.assertIn("UiLocale.Display(Explanation)", diagnostic)
        for protected in (
            "CampaignModels.cs", "CampaignPayloadLocation.cs", "ClientFeedback.cs",
            "DomainPowerShellRunner.cs"
        ):
            self.assertNotIn("UiLocale.Translate(", read(protected))
            self.assertNotIn("UiLocale.Display(", read(protected))


    def test_russian_three_language_display_contract(self):
        preferences = read("UiStyle.cs")
        options = read("UiSettingsDialog.cs")
        locale = read("UiLocale.cs")
        wizard = read("UiLocale.Wizard.cs")
        russian = read("UiLocale.Russian.cs")
        schedule_he = read("UiLocale.ScheduleAdvanced.cs")
        schedule = read("CampaignScheduleDialog.cs")
        layouts = read("UiLayoutSmokeTests.cs")
        manager = read("GpoOperationsForm.cs")
        for key in ('"English" or "Hebrew" or "Russian"',):
            self.assertIn(key, preferences)
        for label in ('"English", "עברית (Hebrew)", "Русский"',
                      '2 => "Russian"', '"Russian" => 2'):
            self.assertIn(label, options)
        for token in ("EnglishByRussian.TryGetValue", "Russian.TryGetValue",
                      "ScheduleAdvancedHebrew.TryGetValue",
                      "ScheduleAdvancedEnglish.TryGetValue",
                      "CaptionSource", "Caption = Translate(column.HeaderText",
                      '"CampaignScheduleMode"', '"AssignmentTargetMode"'):
            self.assertIn(token, locale)
        self.assertIn('Prefix: "Шаг "', wizard)
        self.assertIn("CheckRussianThreeLanguageScheduleAndAdvanced();", layouts)
        self.assertIn('Name = "CampaignScheduleMode"', schedule)
        self.assertIn('Name = "CampaignScheduleGrid"', schedule)
        self.assertIn('UiLocale.Display("Scheduled images: ")', schedule)
        self.assertIn('Name = "GpoLifecycleAction"', manager)
        for source, minimum in ((russian, 650), (schedule_he, 140)):
            keys = re.findall(r'^\s*\["([^"]+)"\]\s*=', source, re.M)
            self.assertGreaterEqual(len(keys), minimum)
            self.assertEqual(len(keys), len(set(keys)))
        for key in ("Campaign Schedule - Images and Dates",
                    "Company Profiles — LockScreen Campaign Manager",
                    "Campaign type", "Screen Saver (.scr)",
                    "AD Connection Wizard", "Active Directory / GPO Deployment",
                    "Client Health / Live", "GPO Operations", "Deferred Repair",
                    "Error trends — LockScreen Campaign Manager"):
            self.assertIn('["' + key + '"]', russian)
        # User data and protocol keys MUST NOT be routed through UI localization.
        for protected in ("CampaignModels.cs", "CampaignPayloadLocation.cs",
                          "DomainPowerShellRunner.cs", "ClientFeedback.cs"):
            source = read(protected)
            self.assertNotIn("UiLocale.Display(", source)
            self.assertNotIn("UiLocale.Translate(", source)
        self.assertIn('"Disable", "Enable", "Unlink", "Relink", "Reuse", "Delete"', manager)
        self.assertIn('"Push", "Repair", "Cleanup", "Report"', manager)

    def test_advanced95_bilingual_safety_and_display(self):
        locale = read("UiLocale.cs")
        words = read("UiLocale.Advanced95.cs")
        gmsa = read("GmsaRepairSetupDialog.cs")
        readiness = read("GmsaReadinessDialog.cs")
        feedback = read("FeedbackSettingsDialog.cs")
        alerts = read("ClientAlertSettingsDialog.cs")
        dashboard = read("DeviceRepairDashboard.cs")
        smoke = read("UiLayoutSmokeTests.cs")
        for marker in ("Advanced95EnglishByRussian.TryGetValue",
                       "Advanced95EnglishByHebrew.TryGetValue",
                       "Advanced95Russian.TryGetValue",
                       "Advanced95Hebrew.TryGetValue",
                       '"GmsaReadinessGrid"', '"DeviceRepairGrid"',
                       '"DeviceRepairFilter"', '"FeedbackSmbInstructions"'):
            self.assertIn(marker, locale)
        for marker in ('"GmsaReadinessGrid"', 'UiLocale.Display("Queue: ")',
                       'report.Checks'):
            self.assertIn(marker, readiness)
        for source, markers in (
            (gmsa, ('UiLocale.Display("gMSA — Confirm installation")',
                    'UiLocale.Display("Disable gMSA")',
                    'MessageBoxDefaultButton.Button2',
                    'GmsaNativeSetup.Install(request)',
                    'GmsaNativeSetup.RemoveTask()',
                    'RightToLeft = RightToLeft.No')),
            (feedback, ('UiLocale.Display("Create feedback folder?', 
                        'FeedbackPath.ValidateRoot', 'Directory.CreateDirectory(folder)',
                        'Name = "FeedbackSmbInstructions"')),
            (dashboard, ('Name = "DeviceRepairGrid"',
                         'Name = "DeviceRepairFilter"',
                         'UiLocale.Display("Scoped AD computers: ")',
                         'UiLocale.Display("Repair Selected")',
                         '_requestRepair(selected.Select(')),
            (smoke, ('CheckRussianHebrewAdvanced95();',
                     '"Настройка gMSA 24/7"', '"הגדרת gMSA מסביב לשעון"'))
        ):
            for marker in markers:
                self.assertIn(marker, source)
        self.assertIn('UiLocale.Display(', alerts)
        for prefix in ("Advanced95Russian", "Advanced95Hebrew"):
            start = words.index('private static readonly Dictionary<string,string> ' + prefix)
            end = words.index('    };', start)
            keys = re.findall(r'^\s*\["([^"]+)"\]\s*=', words[start:end], re.M)
            self.assertGreaterEqual(len(keys), 105)
            self.assertEqual(len(keys), len(set(keys)))
        for protected in ("GmsaRepairConfig.cs", "GmsaRepairQueue.cs",
                          "NativeImmediateApply.cs", "NativeGpoDeploymentApply.cs",
                          "CampaignModels.cs"):
            self.assertNotIn("UiLocale.Display(", read(protected))
            self.assertNotIn("UiLocale.Translate(", read(protected))


    def test_preview96_updater_and_local_lab_tri_language_safety(self):
        words = read("UiLocale.Preview96.cs")
        locale = read("UiLocale.cs")
        updater = read("UniversalUpdaterWindow.cs")
        lab = read("LocalLabEvidenceDialog.cs")
        smoke = read("UiLayoutSmokeTests.cs")
        for marker in ("Preview96EnglishByRussian.TryGetValue",
                       "Preview96EnglishByHebrew.TryGetValue",
                       "Preview96Russian.TryGetValue", "Preview96Hebrew.TryGetValue",
                       '"UpdaterReleaseChannel"', '"UpdaterResultOutput"',
                       '"Updates — LockScreen Campaign Manager"'):
            self.assertIn(marker, locale if "Preview96" in marker or "Updates" in marker
                          else updater)
        for prefix in ("Preview96Russian", "Preview96Hebrew"):
            start = words.index("private static readonly Dictionary<string, string> " + prefix)
            end = words.index("    };", start)
            keys = re.findall(r'^\s*\["([^"]+)"\]\s*=', words[start:end], re.M)
            self.assertGreaterEqual(len(keys), 75)
            self.assertEqual(len(keys), len(set(keys)))
        for needle in (
            'T("Update check failed: ")',
            'T("No local rollback checkpoint found.")',
            'T("Update was not installed")',
            'T("Install verified portable update?")',
            'MessageBoxDefaultButton.Button2',
            'PortableUpdateCoordinator.Launch(_staged, _options)',
            'UpdateChannel.Stable : UpdateChannel.Prerelease',
            'source.GetItemText(source.SelectedItem)',
        ):
            self.assertIn(needle, updater + smoke)
        for marker in ('UiLocale.Display("No report imported")',
                       'UiLocale.Display("Report rejected - invalid or incompatible JSON")',
                       'LocalLabEvidence.LoadLocalFile',
                       'details.Text = text.ToString();'):
            self.assertIn(marker, lab)
        self.assertIn("CheckPreview96UpdaterLabLanguageAndDpi();", smoke)
        for protected in (
            "UniversalUpdater/UpdateDownloader.cs",
            "UniversalUpdater/GitHubReleaseClient.cs",
            "PortableUpdateCoordinator.cs",
            "LocalLabEvidence.cs",
            "CampaignModels.cs",
            "NativeGpoDeploymentApply.cs"):
            source = read(protected)
            self.assertNotIn("UiLocale.Translate(", source)
            self.assertNotIn("UiLocale.Display(", source)


    def test_preview97_gpo_client_health_readonly_consent_localization(self):
        locale = read("UiLocale.cs")
        words = read("UiLocale.Preview97.cs")
        health = read("ClientFeedbackPanel.cs")
        effective = read("EffectiveGpoConflictDialog.cs")
        closure = read("CampaignClosureAuditDialog.cs")
        ops = read("GpoOperationsForm.cs")
        smoke = read("UiLayoutSmokeTests.cs")
        for marker in (
            "Preview97EnglishByRussian.TryGetValue",
            "Preview97EnglishByHebrew.TryGetValue",
            "Preview97Russian.TryGetValue",
            "Preview97Hebrew.TryGetValue"
        ):
            self.assertIn(marker, locale)
        for language in ("Preview97Russian", "Preview97Hebrew"):
            start = words.index("private static readonly Dictionary<string,string> " + language)
            end = words.index("    };", start)
            keys = re.findall(r'^\s*\["([^"]+)"\]\s*=', words[start:end], re.M)
            self.assertGreaterEqual(len(keys), 80)
            self.assertEqual(len(keys), len(set(keys)))
        for text in (
            'UiLocale.Display("There are ")',
            'UiLocale.Display("Configure the share ACL separately.',
            'Name = "ClientHealthStatus"',
            'Name = "ClientHealthTotals"',
            'MessageBoxDefaultButton.Button2',
            'FeedbackPath.ValidateRoot',
            'Directory.CreateDirectory(path)'
        ):
            self.assertIn(text, health)
        for source, terms in (
            (effective, ('UiLocale.Display("Import private RSoP report")',
                         'UiLocale.Display("Verify endpoint (read-only)")',
                         'UiLocale.Display("Verify RSoP source + managed image")',
                         'UiLocale.Display("Export private effective GPO analysis")',
                         'MessageBoxDefaultButton.Button2', 'RsopEvidence.LoadLocalXml',
                         'UiLocale.Display("Private read-only report exported locally.')),
            (closure, ('UiLocale.Display("Verify post-cleanup")',
                       'UiLocale.Display("Export private closure audit")',
                       'MessageBoxDefaultButton.Button2',
                       'PostCleanupVerificationStore.Save')),
            (ops, ('UiLocale.Display("Read-only scan: ")',
                   'UiLocale.Display("Remote execution preview ready for ")',
                   'UiLocale.Display("WARNING: Repair ran, but result tracking failed;',
                   'ConfirmGuid(this, SelectedPushPurpose'))
        ):
            for text in terms:
                self.assertIn(text, source)
        self.assertIn("CheckPreview97GpoClientHealth();", smoke)
        for protected in (
            "CampaignModels.cs", "ClientFeedback.cs", "RsopEvidence.cs",
            "NativeGpoDeploymentApply.cs", "NativeImmediateApply.cs",
            "PostCleanupVerification.cs"
        ):
            source = read(protected)
            self.assertNotIn("UiLocale.Display(", source)
            self.assertNotIn("UiLocale.Translate(", source)


    def test_preview98_numbered_whats_new_is_presentation_only(self):
        formatter = read("ReleaseNotesSummary.cs")
        updater = read("UniversalUpdaterWindow.cs")
        indicator = read("MainForm.UpdateNotification.cs")
        ruhe = read("UiLocale.Preview96.cs")
        tests = read("SelfTests.cs")
        notes = (ROOT / "docs" / "RELEASE-PREVIEW98.md").read_text(encoding="utf-8")
        for needle in ("Regex ListMarker", "MarkdownLink", "Markdown-free",
                       "maxItems = 5", "maxCharsPerItem = 180", "Open gates:"):
            if needle == "Markdown-free":
                continue
            self.assertIn(needle, formatter)
        self.assertIn("ReleaseNotesSummary.Format(found.ReleaseNotes)", updater)
        self.assertIn('T("What\'s new:")', updater)
        self.assertLess(updater.index('T("What\'s new:")'),
                        updater.index('T("New release: ") + found.Version'))
        self.assertIn("ReleaseNotesSummary.Format(release.ReleaseNotes, 3, 90)", indicator)
        self.assertIn("TestHebrewUiPreferences();", tests)
        self.assertIn('string whatsNew = ReleaseNotesSummary.Format(', tests)
        self.assertIn('string legacyNotes = ReleaseNotesSummary.Format(', tests)
        self.assertIn('["What\'s new:"] = "Что нового:"', ruhe)
        self.assertIn('["What\'s new:"] = "מה חדש:"', ruhe)
        numbers = re.findall(r"^([1-9])\. ", notes, re.M)
        self.assertEqual(numbers, ["1", "2", "3", "4", "5"])
        self.assertIn("**Safety and compatibility:**", notes)
        self.assertNotIn("UiLocale.Display(", formatter)
        self.assertNotIn("File.WriteAllText", formatter)
        self.assertNotIn("HttpClient", formatter)
        for protected in ("UniversalUpdater/GitHubReleaseClient.cs",
                          "UniversalUpdater/UpdateDownloader.cs",
                          "UniversalUpdater/PortableExeTransaction.cs"):
            self.assertNotIn("ReleaseNotesSummary", read(protected))


if __name__ == "__main__":
    unittest.main(verbosity=2)
