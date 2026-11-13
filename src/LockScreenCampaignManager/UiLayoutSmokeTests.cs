using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// CI-only, in-process WinForms layout smoke. No modal windows are shown,
/// no domain queries are performed, and persisted UI preferences are untouched.
/// Font scaling is simulated; a real per-monitor 200% DPI acceptance test is
/// still required on a desktop/VM with an interactive display.
/// </summary>
internal static class UiLayoutSmokeTests
{
    private static Form CreateEndpointPowerSmoke()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var json = System.Text.Json.JsonSerializer.SerializeToElement(new {
            domain = "example.com", dc = "dc01.example.com",
            ouDn = "OU=Computers,DC=example,DC=com",
            computer = "pc01.example.com", adScopeVerified = true,
            observedUtc = now, activePlanStatus = "Unavailable",
            activePlanGuid = "", activePlanName = "",
            policyRegistryStatus = "Unavailable", values = Array.Empty<object>(),
            notes = new[] { "UI-only fixture; no network or endpoints contacted" }
        });
        var observation = EndpointPowerObservation.Parse(
            json, "example.com", "dc01.example.com",
            "OU=Computers,DC=example,DC=com", "pc01.example.com", now);
        return new ScreenSaverEndpointPowerDialog(observation, 600, [],
            "Not verified: offline simulated UI.");
    }

    private static readonly (string Name, UiPreferences Settings)[] Profiles =
    {
        ("100%", new UiPreferences { Density = "Auto", UiScalePercent = 100, FontScalePercent = 100 }),
        ("125%", new UiPreferences { Density = "Auto", UiScalePercent = 125, FontScalePercent = 100 }),
        ("150%", new UiPreferences { Density = "Comfortable", UiScalePercent = 150, FontScalePercent = 100 }),
        ("175%", new UiPreferences { Density = "Comfortable", UiScalePercent = 175, FontScalePercent = 100 }),
        ("187.5% font simulation", new UiPreferences {
            Density = "Comfortable", UiScalePercent = 150, FontScalePercent = 125 }),
        ("200%", new UiPreferences { Density = "Comfortable", UiScalePercent = 200, FontScalePercent = 100 })
    };

    private static readonly (string Name, Func<Form> Factory, string[] ExpectedButtons)[] Windows =
    {
        ("Campaign editor - offline Wizard", () => new MainForm(),
            new[] { "Wizard", "Advanced", "Skip AD", "Back", "1  Add files...",
                "Add folder...", "2  Arrange / edit  ▾", "3  Schedule...", "Project  ▾", "Help / tools  ▾" }),
        ("Campaign editor - update available", () => CreateEditorWithUpdateNotice(),
            new[] { "Wizard", "Update available: 0.4.0-preview.53" }),
        ("Campaign Wizard - AD connection", () => CreateWizardPage(0),
            new[] { "AD Connection Wizard...", "Continue offline", "Continue", "Back", "Close Wizard" }),
        ("Campaign Wizard - campaign type", () => CreateWizardPage(1),
            new[] { "Lock Screen / Screen Saver / Both", "Next", "Back" }),
        ("Campaign Wizard - Screen Saver-only save", () => CreateWizardPage(-1),
            new[] { "Validate campaign", "Save project...", "Back" }),
        ("Campaign Wizard - JPG pilot install", () => CreatePhotoWizardSave(),
            new[] { "Export JPG .scr pilot...", "Export GPO lab kit...",
                "Install local JPG pilot...", "Validate campaign", "Back" }),
        ("Campaign Wizard - images", () => CreateWizardPage(2),
            new[] { "Add image files...", "Choose image folder...", "Remove selected", "Next", "Back" }),
        ("Campaign Wizard - arrange", () => CreateWizardPage(3),
            new[] { "Move up", "Move down", "Sort by filename", "Auto match names", "Next", "Back" }),
        ("Campaign Wizard - schedule", () => CreateWizardPage(4),
            new[] { "Edit schedule...", "Next", "Back" }),
        ("Campaign Wizard - save", () => CreateWizardPage(5),
            new[] { "Validate campaign", "Save project...",
                "Skip saving & continue", "Save & Next", "Back" }),
        ("Campaign Wizard - deploy", () => CreateWizardPage(6),
            new[] { "Open GPO Deployment Wizard...", "Save project changes...", "Finish offline", "Finish", "Back" }),
        ("Agent Management - read-only desktop layout", () =>
            new GpoOperationsForm("example.com", "dc01.example.com",
                "OU=Computers,DC=example,DC=com",
                "CN=Campaign-Clients,OU=Groups,DC=example,DC=com",
                new DomainCredentials("example.com", "", ""),
                new CampaignProject {
                    Name = "layout-only", TimeZoneId = "UTC",
                    StartLocal = new DateTime(2026, 11, 9, 14, 0, 0)
                }),
            new[] { "Computers from OU + group", "Check RPC reachability",
                "Select reachable (max 25)", "Open Client Health", "Preview",
                "Run on selected", "Verify Screen Saver power (PC)" }),
        ("AD / GPO deployment - offline Wizard", () => new DomainDeploymentForm(
            new CampaignProject {
                Name = "layout-only", TimeZoneId = "UTC",
                StartLocal = new DateTime(2026, 11, 9, 14, 0, 0)
            }),
            new[] { "Wizard", "Advanced", "Next >", "Check GPO rights",
                "Preview / check conflicts", "Deploy GPO + files", "Close" }),
        ("Full group GPO conflict report - offline", () =>
            new GroupConflictReportDialog([
                new GroupConflictTarget("Pilot users", "User",
                    "OU=Users,DC=example,DC=com",
                    "CN=Pilot,OU=Groups,DC=example,DC=com",
                    1, 0, [
                        new GroupConflictFinding("Pilot users", "User", "Existing GPO",
                            Guid.Empty.ToString("D"), "High", "ScreenSaveTimeOut", "900",
                            "Registry.pol", "Control Panel\\Desktop",
                            "OU=Users,DC=example,DC=com", "Unknown without user gpresult")
                    ], ["WMI and security filter results require real client RSoP"])
            ]), new[] { "Export filtered CSV...", "Close" }),
        ("AD user picker - typeahead page", () => new AdAlternateAccountWizard(
            "example.com", "dc01.example.com", "", "", false, false, true),
            new[] { "Next >", "Use typed login instead", "Cancel", "< Back" }),
        ("AD user picker - password page", () => CreateAdAccountPasswordWizard(),
            new[] { "Use this account", "< Back", "Cancel", "Forget saved login" }),
        ("AD first-step connect/read", () => new AdConnectionStartDialog(null),
            new[] { "Sign in & Read AD", "Next >", "Open campaign editor", "< Back",
                "Work offline (no AD)" }),
        ("AD first-step advanced options", () => {
            var wizard = new AdConnectionStartDialog(null);
            wizard.ShowAdvancedOptionsForSmokeTest();
            return wizard;
        }, new[] { "Sign in & Read AD", "Next >", "Open campaign editor",
            "< Back", "Work offline (no AD)" }),
        ("Updater", () => new UniversalUpdaterWindow(),
            new[] { "1  Check", "2  Install", "Backup status" }),
        ("Local lab JSON evidence viewer", () => new LocalLabEvidenceDialog(),
            new[] { "Import SMB canary JSON...", "Import cleanup JSON..." }),
        ("Screen Saver endpoint power - no-network simulated layout",
            CreateEndpointPowerSmoke, new[] { "Close" }),
        ("Interface settings", () => new UiSettingsDialog(),
            new[] { "Apply" }),
        ("Visual DPI / language acceptance", () => new UiVisualAcceptanceDialog(),
            new[] { "Refresh display", "Run offline UI smoke", "Save local report", "Close" }),
        ("SMB feedback settings", () => new FeedbackSettingsDialog(
            new CampaignProject { Name = "layout-only", TimeZoneId = "UTC",
                FeedbackShare = @"\\example.com\LockScreenReports$",
                EnableFeedback = true }),
            new[] { "Save", "Cancel", "Browse SMB...",
                "Copy share path", "Create campaign folder" }),
        ("Client Health SMB stand-alone", () => {
            var form = new Form { Size = new Size(1250, 820),
                MinimumSize = new Size(920, 670) };
            var panel = new ClientFeedbackPanel("example.com", "dc01.example.com",
                "OU=Computers,DC=example,DC=com",
                "CN=Campaign-Clients,DC=example,DC=com",
                new DomainCredentials("example.com", "", ""),
                new CampaignProject { TimeZoneId = "UTC",
                    FeedbackShare = @"\\example.com\LockScreenReports$" });
            form.Controls.Add(panel);
            return form;
        }, new[] { "Browse SMB...", "Copy path", "Load PCs from AD",
            "Refresh", "Export CSV", "Repair Selected" }),
        ("Campaign assignment matrix", () =>
            new CampaignAssignmentsDialog(
                new CampaignProject { Name = "matrix-ci", TimeZoneId = "UTC",
                    TargetAssignments = [
                        new CampaignGroupTarget {
                            Name = "Lab computers + pilot users",
                            Mode = CampaignTargetMode.Both,
                            ComputerOuDn = "OU=Devices,DC=example,DC=com",
                            ComputerGroupDn = "CN=PCs,OU=Groups,DC=example,DC=com",
                            UserOuDn = "OU=Users,DC=example,DC=com",
                            UserGroupDn = "CN=Pilots,OU=Groups,DC=example,DC=com"
                        }
                    ] },
                [
                    new CampaignAssignmentsDialog.AdChoice("Devices",
                        "OU=Devices,DC=example,DC=com"),
                    new CampaignAssignmentsDialog.AdChoice("Users",
                        "OU=Users,DC=example,DC=com")
                ],
                [
                    new CampaignAssignmentsDialog.AdChoice("PCs",
                        "CN=PCs,OU=Groups,DC=example,DC=com"),
                    new CampaignAssignmentsDialog.AdChoice("Pilots",
                        "CN=Pilots,OU=Groups,DC=example,DC=com")
                ],
                "OU=Devices,DC=example,DC=com",
                "CN=PCs,OU=Groups,DC=example,DC=com"
            ), new[] { "Add group...", "Remove", "Save assignments", "Cancel" }),
        ("gMSA setup", () => new GmsaRepairSetupDialog(
            "example.com", "OU=Clients,DC=example,DC=com",
            "CN=Clients,OU=Groups,DC=example,DC=com", @"\\files.example.com\ClientHealth$"),
            new[] { "Check", "Run gMSA test", "Install / Repair",
                "Disable", "Remove task", "Readiness", "Close" }),
        ("Client Health alert settings", () => new ClientAlertSettingsDialog(
            new ClientAlertPreferences()), new[] { "Save", "Cancel" }),
        ("gMSA readiness", () => new GmsaReadinessDialog(
            "example.com", "OU=Clients,DC=example,DC=com",
            "CN=Clients,OU=Groups,DC=example,DC=com"),
            new[] { "Run assessment", "Export safe JSON", "Close" }),
        ("Diagnostic trends", () => new DiagnosticsTrendsWindow(),
            new[] { "Analyze local reports", "Export aggregate JSON", "Close" }),
        ("Diagnostics", () => new DiagnosticsWindow(),
            new[] { "Refresh", "Error trends", "Report an error...", "Close" }),
        ("Repair history", () => new RepairHistoryDialog(Array.Empty<RepairHistoryEvent>()),
            new[] { "Export visible CSV", "Close" }),
        ("Campaign closure audit", () => new CampaignClosureAuditDialog(
            new CampaignClosureAudit {
                CampaignId = "layout-only", GpoId = Guid.Empty.ToString("D"),
                ObservedUtc = DateTimeOffset.UtcNow, Rows = Array.Empty<ClosureAuditRow>()
            },
            CampaignGpoLinkEvidence.Unavailable("dc01.example.com",
                "CI layout only; no domain requested")),
            new[] { "Verify selected (read-only)", "Export visible CSV", "Close" }),
        ("Company profiles", () => new CompanyProfilesDialog(),
            new[] { "New company", "Select company", "Unassigned", "Edit", "Close" }),
        ("Company profile editor", () => new CompanyProfileEditDialog(),
            new[] { "Save company", "Cancel" }),
        ("Campaign schedule", () => new CampaignScheduleDialog(new CampaignProject {
            TimeZoneId = "UTC",
            StartLocal = new DateTime(2026, 11, 8, 14, 0, 0),
            ScheduleMode = CampaignScheduleMode.EveryInterval,
            IntervalMinutes = 150,
            ImagePaths = new List<string> { "sample1.jpg", "sample2.jpg" }
        }), new[] { "Apply schedule", "Cancel", "Set selected date/time",
            "Repeat selected image", "Import image folder..." }),
        ("Image folder match preview", () => new ImageFolderImportDialog(new CampaignProject {
            TimeZoneId = "UTC", StartLocal = new DateTime(2026, 11, 9, 8, 35, 0)
        }), new[] { "Choose folder...", "Apply image mapping", "Cancel" }),
        ("Effective GPO candidates", () => {
            string gpo = Guid.NewGuid().ToString("D");
            var utc = DateTimeOffset.UtcNow;
            const string domain = "example.com";
            const string dc = "dc01.example.com";
            const string ou = "OU=Clients,DC=example,DC=com";
            const string group = "CN=Computers,OU=Groups,DC=example,DC=com";
            const string computer = "PC022.example.com";
            const string campaign = "effective-ci";
            using var json = System.Text.Json.JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(new {
                    domain, dc, targetOu = ou, groupDn = group, computer,
                    campaignId = campaign, managedGpoId = gpo, analyzedUtc = utc,
                    actualComputerContainer = ou,
                    membership = "Nested/direct", tokenStatus = "Unknown",
                    siteAssociation = "Unknown", siteInventoryComplete = false,
                    linkedCount = 0, siteCandidates = 0, potentialHigh = 0,
                    policies = Array.Empty<object>(),
                    warnings = new[] {
                        "Site GPO inventory incomplete: applicability remains Unknown."
                    }
                }));
            return new EffectiveGpoConflictDialog(EffectiveGpoReport.Parse(
                json.RootElement, domain, dc, ou, group, computer, campaign,
                gpo, utc));
        }, new[] { "Import gpresult XML", "Clear imported RSoP",
            "Verify endpoint HKLM", "Verify RSoP + image", "Clear HKLM snapshot",
            "Winning policy analysis", "RSoP-only GPOs", "Export visible CSV", "Close" }),
        ("Winning Policy Analysis", () => {
            const string domain = "example.com", dc = "dc01.example.com";
            const string ou = "OU=Clients,DC=example,DC=com";
            const string group = "CN=Computers,OU=Groups,DC=example,DC=com";
            const string computer = "PC022.example.com";
            string gpo = Guid.NewGuid().ToString("D");
            var utc = DateTimeOffset.UtcNow;
            using var json = System.Text.Json.JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(new {
                    domain, dc, targetOu = ou, groupDn = group, computer,
                    campaignId = "winning-ci", managedGpoId = gpo,
                    actualComputerContainer = ou, analyzedUtc = utc,
                    membership = "Nested/direct", tokenStatus = "Unknown",
                    siteAssociation = "Unknown", siteInventoryComplete = true,
                    linkedCount = 0, siteCandidates = 0, potentialHigh = 0,
                    policies = Array.Empty<object>(), warnings = Array.Empty<string>()
                }));
            var report = EffectiveGpoReport.Parse(json.RootElement,
                domain, dc, ou, group, computer, "winning-ci", gpo, utc);
            return new WinningPolicyDialog(WinningPolicyAnalysis.Build(report));
        }, new[] { "Export visible CSV", "Close" }),
        ("DC consistency", () => {
            var form = new Form { MinimumSize = new Size(920, 670), Size = new Size(1250, 820) };
            form.Controls.Add(new DcConsistencyPanel("example.com", "dc01.example.com",
                new DomainCredentials("example.com", "", ""), () => ("", "")));
            return form;
        }, new[] { "Discover DCs", "Compare selected DCs", "Export safe CSV" }),
        ("Smart SYSVOL Storage", () => {
            var form = new Form { MinimumSize = new Size(920, 670), Size = new Size(1250, 820) };
            form.Controls.Add(new SysvolStoragePanel("example.com", "dc01.example.com",
                new CampaignProject { TimeZoneId = "UTC" },
                new DomainCredentials("example.com", "", "")));
            return form;
        }, new[] { "Scan SYSVOL", "Compare current campaign", "Preview cleanup",
            "Delete source folder" }),
        ("Computer group assignment", () => {
            var form = new Form { MinimumSize = new Size(920, 670), Size = new Size(1250, 820) };
            var panel = new ComputerGroupAssignmentPanel(
                "example.com", "dc01.example.com",
                "OU=Clients,DC=example,DC=com",
                "CN=LockScreen,OU=Groups,DC=example,DC=com",
                new DomainCredentials("example.com", "", ""));
            form.Controls.Add(panel);
            return form;
        }, new[] { "Find computers", "Preview membership change", "Apply group change" })
    };

    private static Form CreateAdAccountPasswordWizard()
    {
        var form = new AdAlternateAccountWizard("example.com", "dc01.example.com",
            "", "", false, false, allowSaving: true, forgetSavedLogin: () => { });
        form.ShowPasswordPageForSmokeTest();
        return form;
    }

    private static Form CreateEditorWithUpdateNotice()
    {
        var form = new MainForm();
        form.ShowSimulatedUpdateNoticeForLayoutTest();
        return form;
    }

    private static Form CreatePhotoWizardSave()
    {
        var editor = new MainForm();
        try
        {
            // Toggle JPG mode through the real wizard controls, not internal
            // project shortcuts, and never install or change any Windows setting.
            using (var selection = editor.CreateCampaignWizardForSmokeTest(1))
            {
                var type = Descendants(selection).OfType<ComboBox>()
                    .Single(x => x.Name == "WizardCampaignType");
                var photo = Descendants(selection).OfType<CheckBox>()
                    .Single(x => x.Name == "WizardScreenSaverJpegMode");
                type.SelectedItem = CampaignTargetMode.ScreenSaver;
                photo.Checked = true;
            }
            var wizard = editor.CreateCampaignWizardForSmokeTest(5);
            wizard.Disposed += (_, _) => editor.Dispose();
            return wizard;
        }
        catch
        {
            editor.Dispose();
            throw;
        }
    }

    private static Form CreateWizardPage(int page)
    {
        // The editor stays invisible: no domain, GPO or SYSVOL access.
        // For image pages create one temporary PNG so the test exercises
        // actual decoding and the selection/preview binding, not just a
        // PictureBox that exists but renders nothing.
        string? fixture = null;
        if (page is 2 or 3)
        {
            fixture = Path.Combine(Path.GetTempPath(),
                "lscm-preview-smoke-" + Guid.NewGuid().ToString("N") + ".png");
            using var sample = new Bitmap(120, 68);
            using (var brush = Graphics.FromImage(sample))
                brush.Clear(Color.FromArgb(23, 201, 143));
            sample.Save(fixture, System.Drawing.Imaging.ImageFormat.Png);
        }
        var editor = new MainForm();
        Form wizard;
        try { wizard = editor.CreateCampaignWizardForSmokeTest(page, fixture); }
        catch
        {
            editor.Dispose();
            if (fixture is not null) File.Delete(fixture);
            throw;
        }
        wizard.Disposed += (_, _) =>
        {
            editor.Dispose();
            if (fixture is not null)
            {
                try { File.Delete(fixture); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        };
        if (page is 5 or 6)
        {
            var source = Descendants(wizard).OfType<ComboBox>()
                .Single(x => x.Name == "CampaignWizardContentSource");
            var path = Descendants(wizard).OfType<TextBox>()
                .Single(x => x.Name == "CampaignWizardContentShareRoot");
            if (source.SelectedIndex != 0 ||
                editor.CampaignSourceForSmokeTest != CampaignContentSource.Sysvol)
                throw new InvalidOperationException(
                    "Wizard must show SYSVOL by default for existing campaigns.");
            source.SelectedIndex = 1;
            path.Text = @"\\files.example.com\Campaigns";
            if (editor.CampaignSourceForSmokeTest != CampaignContentSource.SmbShare ||
                editor.CampaignShareRootForSmokeTest != path.Text)
                throw new InvalidOperationException(
                    "Wizard SMB folder selection did not persist in the campaign editor.");
            source.SelectedIndex = 0;
            if (editor.CampaignSourceForSmokeTest != CampaignContentSource.Sysvol)
                throw new InvalidOperationException(
                    "Switching back to SYSVOL did not persist in the campaign project.");
        }
        if (page == 4)
        {
            var picker = Descendants(wizard).OfType<ComboBox>()
                .Single(x => x.Name == "WizardTimeZoneSelect");
            string initial = editor.CampaignTimeZoneForSmokeTest;
            string alternative = TimeZoneChoices.Ids()
                .First(x => !x.Equals(initial, StringComparison.OrdinalIgnoreCase));
            picker.SelectedItem = alternative;
            var mainPicker = Descendants(editor).OfType<ComboBox>()
                .Single(x => x.Name == "MainTimeZoneSelector");
            if (editor.CampaignTimeZoneForSmokeTest != alternative ||
                mainPicker.Text != alternative ||
                TimeZoneChoices.SelectedId(picker) != alternative)
                throw new InvalidOperationException(
                    "Campaign Wizard time-zone selection did not persist in the local project and main editor.");
        }
        return wizard;
    }

    private static void CheckCampaignNameLayout(Form form, string profile)
    {
        var labels = Descendants(form).OfType<Label>()
            .Where(x => x.Name is "CampaignNameLabel" or "WizardCampaignNameLabel")
            .ToArray();
        foreach (Label title in labels)
        {
            if (title.Parent is not TableLayoutPanel box ||
                box.RowStyles.Count < 2)
                throw new InvalidOperationException(
                    $"Campaign name label has no two-row layout at {profile}");
            int row = box.GetRow(title);
            if (row != 0)
                throw new InvalidOperationException(
                    $"Campaign name label misplaced in {form.Text} at {profile}");
            int measured = TextRenderer.MeasureText("Agpqy", title.Font,
                new Size(4096, 4096),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height +
                title.Padding.Vertical + title.Margin.Vertical + 7;
            if (box.RowStyles[row].Height < measured)
                throw new InvalidOperationException(
                    $"Campaign name descenders clipped in {form.Text} at {profile}");
            var field = box.GetControlFromPosition(0, 1);
            if (field is not TextBox input)
                throw new InvalidOperationException(
                    $"Campaign name input missing in {form.Text} at {profile}");
            if (box.RowStyles[1].Height <
                    Math.Max(input.Height, input.PreferredSize.Height) + input.Margin.Vertical)
                throw new InvalidOperationException(
                    $"Campaign name input row clipped in {form.Text} at {profile}");
            if (box.Height < box.RowStyles[0].Height + box.RowStyles[1].Height +
                    box.Padding.Vertical)
                throw new InvalidOperationException(
                    $"Campaign name text/input overlap in {form.Text} at {profile}");

            // The main editor's former 98px row cropped the whole label
            // group even after the label itself was made taller.
            if (title.Name == "CampaignNameLabel" &&
                box.Parent is FlowLayoutPanel flow &&
                flow.Parent?.Parent is TableLayoutPanel editor)
            {
                int contentBottom = flow.Controls.Cast<Control>()
                    .Select(x => x.Bottom + x.Margin.Bottom)
                    .DefaultIfEmpty(box.Height).Max();
                int requiredRow = contentBottom + flow.Padding.Bottom +
                    flow.Parent.Padding.Vertical + flow.Parent.Margin.Vertical;
                if (editor.RowStyles[1].Height + 3 < requiredRow)
                    throw new InvalidOperationException(
                        $"Campaign name settings strip still clipped at {profile}: " +
                        $"{editor.RowStyles[1].Height:N0}px available, {requiredRow}px required");
            }
        }
        if ((form is MainForm || form.Text.Contains("Campaign Wizard",
                StringComparison.OrdinalIgnoreCase)) && labels.Length != 1)
            throw new InvalidOperationException(
                $"Missing Campaign name label in {form.Text} at {profile}");
    }

    private static void CheckMainEditorHeader(Form form, string profile)
    {
        Label[] labels = Descendants(form).OfType<Label>().ToArray();
        Label? title = labels.FirstOrDefault(x => x.Name == "MainHeaderTitle");
        if (title is null) return;
        Label steps = labels.FirstOrDefault(x => x.Name == "MainHeaderSteps") ??
            throw new InvalidOperationException("Missing main editor step label.");
        Label company = labels.FirstOrDefault(x => x.Name == "MainHeaderCompany") ??
            throw new InvalidOperationException("Missing main editor company label.");
        if (title.Parent is not TableLayoutPanel identity ||
            identity.Parent is not TableLayoutPanel header ||
            header.Parent is not TableLayoutPanel outer ||
            header.RowStyles.Count < 4)
            throw new InvalidOperationException("Main editor title and version header layout invalid.");
        Label? visibleVersion = labels.FirstOrDefault(x => x.Name == "MainHeaderVersion");
        if (visibleVersion is null || visibleVersion.Parent != identity ||
            identity.GetColumn(visibleVersion) != 1 ||
            identity.GetColumn(title) != 0)
            throw new InvalidOperationException("Main title/version alignment is invalid.");
        Control actions = header.GetControlFromPosition(0, 3) ??
            throw new InvalidOperationException("AD controls missing from main header.");
        UiStyle.FitEditorHeader(outer, header, title, steps, company, actions, 190);
        int expected = header.RowStyles.Cast<RowStyle>().Take(4)
            .Sum(x => (int)Math.Ceiling(x.Height)) +
            header.Padding.Vertical + header.Margin.Vertical;
        if (outer.RowStyles[0].Height < expected)
            throw new InvalidOperationException(
                $"Main editor header overlaps AD controls at {profile}.");
        foreach (Label label in new[] { title, steps, company })
        {
            int measured = TextRenderer.MeasureText("Agpqy", label.Font).Height +
                label.Padding.Vertical + label.Margin.Vertical + 5;
            int row = label == title ? 0 : header.GetRow(label);
            if (row < 0 || header.RowStyles[row].Height < measured)
                throw new InvalidOperationException(
                    $"Main editor header clipped {label.Name} at {profile}.");
        }
    }

    private static void CheckWizardFooter(Form form, string profile)
    {
        var footer = Descendants(form).OfType<TableLayoutPanel>()
            .FirstOrDefault(x => x.Name == "CampaignWizardFooter");
        if (footer is null) return;
        if (footer.Parent != form || footer.Dock != DockStyle.Bottom)
            throw new InvalidOperationException(
                $"Campaign Wizard footer is not pinned outside scrolling pages at {profile}.");
        footer.PerformLayout();
        foreach (string caption in new[] { "Back", "Close Wizard" })
        {
            var button = footer.Controls.OfType<Button>()
                .FirstOrDefault(x => x.Text == caption);
            if (button is null || button.Dock != DockStyle.Fill ||
                button.Height < 30 || button.Width < 45)
                throw new InvalidOperationException(
                    $"Wizard bottom navigation missing or cropped: {caption} at {profile}.");
        }
        var next = footer.Controls.OfType<Button>()
            .FirstOrDefault(x => x.Text is "Next" or "Continue" or
                "Save & Next" or "Continue (no save)" or "Finish" or "Deploy GPO...");
        if (next is null || next.Dock != DockStyle.Fill ||
            next.Height < 30 || next.Width < 75)
            throw new InvalidOperationException(
                $"Wizard Next/Finish action is cropped at {profile}.");

        // Catch horizontally clipped Continue / Continue (no save), not only
        // the presence of a nonzero button bounding box.
        int captionWidth = TextRenderer.MeasureText(next.Text, next.Font).Width + 12;
        int captionHeight = TextRenderer.MeasureText("Agpqy", next.Font).Height + 7;
        if (next.Name != "CampaignWizardContinueAction" ||
            next.Image is not null || next.AutoEllipsis ||
            next.Width < captionWidth || next.Height < captionHeight)
            throw new InvalidOperationException(
                $"Campaign Wizard Continue text clipped at {profile}: " +
                $"{next.Text}, {next.Width}x{next.Height}, required " +
                $"{captionWidth}x{captionHeight}.");
    }

    private static void CheckUpdaterHeader(Form form, string profile)
    {
        if (form is not UniversalUpdaterWindow) return;
        var all = Descendants(form).ToArray();
        var header = all.OfType<TableLayoutPanel>()
            .FirstOrDefault(x => x.Name == "UpdaterHeader") ??
            throw new InvalidOperationException("Software updates header missing.");
        var title = all.OfType<Label>()
            .FirstOrDefault(x => x.Name == "UpdaterHeaderTitle") ??
            throw new InvalidOperationException("Software updates title missing.");
        var subtitle = all.OfType<Label>()
            .FirstOrDefault(x => x.Name == "UpdaterHeaderSubtitle") ??
            throw new InvalidOperationException("Software updates subtitle missing.");
        if (header.Parent is not TableLayoutPanel outer || title.Parent != header ||
            subtitle.Parent != header || header.RowStyles.Count != 2 ||
            title.AutoEllipsis || subtitle.AutoEllipsis)
            throw new InvalidOperationException(
                $"Updater banner text hierarchy invalid at {profile}.");
        UiStyle.FitWizardHeader(outer, header, title, subtitle, 116);
        int titleHeight = TextRenderer.MeasureText("Agpqy", title.Font).Height +
            title.Padding.Vertical + title.Margin.Vertical + 8;
        int subtitleHeight = TextRenderer.MeasureText("Agpqy", subtitle.Font).Height +
            subtitle.Padding.Vertical + subtitle.Margin.Vertical + 8;
        if (header.RowStyles[0].Height < titleHeight ||
            header.RowStyles[1].Height < subtitleHeight ||
            outer.RowStyles[0].Height <
                header.RowStyles[0].Height + header.RowStyles[1].Height +
                header.Padding.Vertical + header.Margin.Vertical)
            throw new InvalidOperationException(
                $"Software updates subtitle clipped at {profile}.");
    }

    private static void CheckUpdaterFieldHeights(Form form, string profile)
    {
        if (form is not UniversalUpdaterWindow) return;
        var all = Descendants(form).ToArray();
        var source = all.OfType<ComboBox>()
            .SingleOrDefault(x => x.Name == "UpdaterReleaseChannel");
        var config = all.OfType<TableLayoutPanel>()
            .SingleOrDefault(x => x.Name == "UpdaterConfiguration");
        if (source is null || config is null ||
            source.Parent is not TableLayoutPanel sourceField ||
            sourceField.Parent != config ||
            source.Items.Count != 2 ||
            source.SelectedIndex != 0 ||
            source.SelectedItem?.ToString() != "Preview + stable" ||
            source.DropDownStyle != ComboBoxStyle.DropDownList)
            throw new InvalidOperationException(
                $"Updater release channel missing at {profile}.");
        var label = sourceField.GetControlFromPosition(0, 0) as Label;
        if (label is null ||
            UiLocale.Translate(label.Text, "English") != "Release channel" ||
            label.AutoEllipsis)
            throw new InvalidOperationException(
                $"Updater release channel label missing/clipped at {profile}.");
        int labelHeight = TextRenderer.MeasureText("Agpqy", label.Font,
            new Size(4096, 4096),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height +
            label.Padding.Vertical + label.Margin.Vertical + 9;
        int editorHeight = source.PreferredHeight + source.Margin.Vertical + 8;
        int optionTextWidth = TextRenderer.MeasureText(
            source.GetItemText(source.SelectedItem), source.Font).Width + 24;
        if (sourceField.RowStyles[0].Height < labelHeight ||
            sourceField.RowStyles[1].Height < editorHeight ||
            source.Height < source.PreferredHeight ||
            source.Width < optionTextWidth ||
            sourceField.GetRow(source) != 1)
            throw new InvalidOperationException(
                $"Updater channel is clipped at {profile}: " +
                $"label={sourceField.RowStyles[0].Height}, " +
                $"editor={sourceField.RowStyles[1].Height}, " +
                $"native={source.Height}/{source.PreferredHeight}, " +
                $"width={source.Width}/{optionTextWidth}.");
        int total = config.RowStyles.Cast<RowStyle>()
            .Sum(x => (int)Math.Ceiling(x.Height)) +
            config.Padding.Vertical + config.Margin.Vertical;
        if (config.Parent is not TableLayoutPanel outer ||
            outer.RowStyles[1].Height < total)
            throw new InvalidOperationException(
                $"Updater configuration panel is clipped at {profile}.");
    }

    private static void CheckMainWizardStorage(Form form, string profile)
    {
        if (!form.Text.Contains("Campaign Wizard", StringComparison.Ordinal))
            return;
        var all = Descendants(form).ToArray();
        bool isDeploy = all.OfType<Label>()
            .Any(x => x.Text == "Deploy or finish offline");
        bool isSave = all.OfType<Label>()
            .Any(x => x.Text == "Save or skip saving");
        if (!isDeploy && !isSave) return;
        var source = all.OfType<ComboBox>()
            .SingleOrDefault(x => x.Name == "CampaignWizardContentSource");
        var folder = all.OfType<TextBox>()
            .SingleOrDefault(x => x.Name == "CampaignWizardContentShareRoot");
        var browse = all.OfType<Button>()
            .SingleOrDefault(x => x.Name == "CampaignWizardContentShareBrowse");
        var field = all.OfType<TableLayoutPanel>()
            .SingleOrDefault(x => x.Name == (isSave
                ? "CampaignWizardSaveStorageOptions" : "CampaignWizardStorageOptions"));
        var row = all.OfType<TableLayoutPanel>()
            .SingleOrDefault(x => x.Name == "CampaignWizardNetworkStorageRow");
        if (source is null || folder is null || browse is null ||
            field is null || row is null ||
            source.Items.Count != 2 || source.SelectedIndex != 0 ||
            source.DropDownStyle != ComboBoxStyle.DropDownList ||
            source.Height < source.PreferredHeight ||
            source.Width < TextRenderer.MeasureText(
                "SMB network folder (UNC)", source.Font).Width + 22 ||
            field.RowStyles.Cast<RowStyle>().Any(x => x.SizeType != SizeType.AutoSize))
            throw new InvalidOperationException(
                $"Main Wizard { (isSave ? "Save" : "Deploy") } SYSVOL/SMB picker clipped or missing at {profile}.");
        source.SelectedIndex = 1;
        folder.Text = @"\\files.example.com\Campaigns";
        form.PerformLayout();
        // WinForms Visible is inherited from the never-shown test form;
        // assert the selected editor is enabled and has physical dimensions.
        if (!folder.Enabled || !browse.Enabled || row.Height < 20 ||
            folder.Width < 160 ||
            browse.Height < TextRenderer.MeasureText("Browse...", browse.Font).Height + 6)
            throw new InvalidOperationException(
                $"Main Wizard network UNC input or Browse button clipped at {profile}: " +
                $"row={row.Width}x{row.Height} visible={row.Visible}, " +
                $"folder={folder.Width}x{folder.Height} enabled={folder.Enabled}, " +
                $"browse={browse.Width}x{browse.Height} enabled={browse.Enabled}, " +
                $"browse-text={TextRenderer.MeasureText("Browse...", browse.Font).Height + 6}.");
        source.SelectedIndex = 0;
    }

    private static void CheckAdAdvancedOptions(Form form, string profile)
    {
        if (form is not AdConnectionStartDialog) return;
        var all = Descendants(form).ToArray();
        var options = all.OfType<CheckBox>()
            .FirstOrDefault(x => x.Name == "AdAdvancedConnectionOptions");
        var panel = all.OfType<TableLayoutPanel>()
            .FirstOrDefault(x => x.Name == "AdAdvancedConnectionPanel");
        var content = all.OfType<TableLayoutPanel>()
            .FirstOrDefault(x => x.Name == "AdConnectionScrollableContent");
        if (options is null || panel is null || content is null ||
            content.Parent is not Panel scrolling || !scrolling.AutoScroll ||
            options.Parent != panel || options.Dock != DockStyle.Fill ||
            options.AutoEllipsis || options.AutoSize)
            throw new InvalidOperationException(
                $"Advanced connection options are not safely laid out at {profile}.");
        if (!options.Checked) return;
        int required = TextRenderer.MeasureText("Agpqy", options.Font).Height +
            options.Padding.Vertical + options.Margin.Vertical + 10;
        int rowsTotal = content.RowStyles.Cast<RowStyle>()
            .Sum(row => (int)Math.Ceiling(row.Height));
        if (panel.RowStyles[0].Height < required ||
            content.RowStyles[2].Height < panel.RowStyles[0].Height + 100 ||
            content.Height < rowsTotal)
            throw new InvalidOperationException(
                $"Advanced connection options lower line clipped at {profile}: " +
                $"checkbox={options.Height}, header={panel.RowStyles[0].Height}, " +
                $"content={content.Height}, needed={rowsTotal}.");
    }

    private static void CheckDpiTableRows(Form form, string profile)
    {
        // Audit *every* absolute cell, including secondary tabs, at all six
        // font profiles. No screenshots/AD access are needed for this check.
        var all = Descendants(form).ToArray();
        foreach (Control control in all)
        {
            if (control.Parent is not TableLayoutPanel table ||
                control.Dock != DockStyle.Fill) continue;
            int row = table.GetRow(control);
            if (row < 0 || row >= table.RowStyles.Count ||
                table.RowStyles[row].SizeType != SizeType.Absolute) continue;
            int measured = TextRenderer.MeasureText("Agpqy", control.Font,
                new Size(4096, 4096),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height;
            int required = control switch
            {
                Label label when !label.AutoSize =>
                    measured + control.Padding.Vertical + control.Margin.Vertical + 9,
                CheckBox or RadioButton =>
                    measured + control.Padding.Vertical + control.Margin.Vertical + 13,
                Button => measured + control.Margin.Vertical + 17,
                ComboBox box => box.PreferredHeight + box.Margin.Vertical + 7,
                _ => 0
            };
            if (required > 0 && table.RowStyles[row].Height + 0.25f < required)
                throw new InvalidOperationException(
                    $"DPI text row clipped: {form.Text} / {profile} / " +
                    $"{control.Name} ({control.Text}): row {table.RowStyles[row].Height}, " +
                    $"glyphs need {required}.");
        }
    }

    private static void CheckTimeZoneDifference(Form form, string profile)
    {
        string compared = TimeZoneChoices.OffsetComparison("UTC",
            new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero));
        if (!compared.Contains("Campaign: UTC+00:00", StringComparison.Ordinal) ||
            !compared.Contains("This PC: UTC", StringComparison.Ordinal) ||
            !compared.Contains("Difference:", StringComparison.Ordinal) ||
            !compared.Contains("DST now: No", StringComparison.Ordinal) ||
            TimeZoneChoices.OffsetText(TimeSpan.FromMinutes(-210)) != "-03:30")
            throw new InvalidOperationException("Time-zone UTC/difference/DST formatter is incorrect.");
        string requiredName = form.Text.Contains("Campaign Wizard", StringComparison.Ordinal)
            ? "WizardTimeZoneDifference" : form is CampaignScheduleDialog
                ? "ScheduleTimeZoneDifference" : "";
        if (requiredName.Length == 0) return;
        // The Schedule pane exists only on the fourth Wizard step.
        var label = Descendants(form).OfType<Label>()
            .FirstOrDefault(x => x.Name == requiredName);
        if (label is null &&
            !(form.Text.Contains("Campaign Wizard", StringComparison.Ordinal) &&
              Descendants(form).OfType<ComboBox>()
                .All(x => x.Name != "WizardTimeZoneSelect")))
            throw new InvalidOperationException(
                $"Missing {requiredName} at {profile}.");
        if (label is not null && (!label.Text.Contains("Difference:", StringComparison.Ordinal) ||
            !label.Text.Contains("DST now:", StringComparison.Ordinal) ||
            label.AutoEllipsis))
            throw new InvalidOperationException(
                $"Time-zone difference/DST clipped at {profile}.");
    }

    private static void CheckScrollableUncPaths(Form form, string profile)
    {
        var all = Descendants(form).ToArray();
        if (form is FeedbackSettingsDialog &&
            !all.OfType<TextBox>().Any(x =>
                x.Name == "FeedbackShareScrollable" && x.Multiline &&
                !x.WordWrap && x.ScrollBars == ScrollBars.Horizontal))
            throw new InvalidOperationException(
                $"SMB feedback share is not horizontally scrollable at {profile}.");
        if (all.OfType<ClientFeedbackPanel>().Any())
        {
            if (!all.OfType<TextBox>().Any(x =>
                    x.Name == "ClientHealthSmbPath" && x.Multiline &&
                    !x.WordWrap && x.ScrollBars == ScrollBars.Horizontal))
                throw new InvalidOperationException(
                    $"Client Health SMB share cannot be scrolled at {profile}.");
            var activity = all.OfType<ComboBox>()
                .SingleOrDefault(x => x.Name == "ClientHealthAdActivityWindow");
            var coverage = all.OfType<Label>()
                .SingleOrDefault(x => x.Name == "ClientHealthAdReportCoverage");
            if (activity is null || activity.Items.Count != 4 ||
                activity.SelectedIndex != 1 ||
                activity.DropDownStyle != ComboBoxStyle.DropDownList ||
                coverage is null || coverage.AutoEllipsis ||
                coverage.Text.Length < 30)
                throw new InvalidOperationException(
                    $"Client Health read-only AD/SMB coverage must start unverified " +
                    $"with a visible activity window at {profile}.");
        }
        if (form is GpoOperationsForm &&
            !all.OfType<TextBox>().Any(x =>
                x.Name == "GpoScopePathViewer" && x.ReadOnly &&
                x.Multiline && !x.WordWrap &&
                x.ScrollBars == ScrollBars.Horizontal))
            throw new InvalidOperationException(
                $"GPO OU/LDAP DN path cannot be scrolled at {profile}.");
        if (all.OfType<TextBox>().FirstOrDefault(x =>
                x.Name == "WizardFeedbackConfiguration") is { } configuration &&
            (!configuration.ReadOnly || !configuration.Multiline ||
             configuration.ScrollBars != ScrollBars.Both))
            throw new InvalidOperationException(
                $"Wizard Client Health SMB status/path cannot be scrolled at {profile}.");
    }

    private static void CheckTimeZoneSelectors(Form form, string profile)
    {
        var all = Descendants(form).OfType<ComboBox>().ToArray();
        var main = all.FirstOrDefault(x => x.Name == "MainTimeZoneSelector");
        if (form is MainForm && (main is null || main.Width < 320 ||
            main.DropDownWidth < 500))
            throw new InvalidOperationException(
                $"Main Windows time-zone field or menu is clipped at {profile}.");
        var picker = all.FirstOrDefault(x => x.Name is
            "WizardTimeZoneSelect" or "ScheduleTimeZoneSelect");
        if (picker is null) return;
        if (picker.Dock != DockStyle.Fill ||
            picker.DropDownStyle != ComboBoxStyle.DropDownList ||
            picker.Items.Count < 5 ||
            string.IsNullOrWhiteSpace(TimeZoneChoices.SelectedId(picker)))
            throw new InvalidOperationException(
                $"Time-zone picker is empty or not usable at {profile}.");
        if (picker.Name == "ScheduleTimeZoneSelect" &&
            TimeZoneChoices.SelectedId(picker) != "UTC")
            throw new InvalidOperationException(
                "Schedule editor failed to preserve the selected UTC time zone.");
        if (picker.DropDownWidth < 420)
            throw new InvalidOperationException(
                $"Time-zone selection drop-down is too narrow at {profile}.");
    }

    private static void CheckWizardHeader(Form form, string profile)
    {
        Label[] labels = Descendants(form).OfType<Label>().ToArray();
        Label? title = labels.FirstOrDefault(x =>
            x.Name is "AdWizardHeaderTitle" or "CampaignWizardHeaderTitle");
        if (title is null) return;
        string subtitleName = title.Name == "AdWizardHeaderTitle"
            ? "AdWizardHeaderSubtitle" : "CampaignWizardHeaderSubtitle";
        Label subtitle = labels.FirstOrDefault(x => x.Name == subtitleName) ??
            throw new InvalidOperationException(
                $"Missing wizard subtitle: {form.Text} at {profile}");
        if (title.Parent is not TableLayoutPanel header ||
            header.Parent is not TableLayoutPanel outer ||
            header.RowStyles.Count < 2 || outer.RowStyles.Count < 1)
            throw new InvalidOperationException(
                $"Invalid wizard header layout: {form.Text} at {profile}");

        // Simulate the real DPI-driven FontChanged adjustment without showing
        // modal windows or querying AD; verify both inner AND outer row fit.
        UiStyle.FitWizardHeader(outer, header, title, subtitle, 110);
        int requiredTitle = TextRenderer.MeasureText("Agpqy", title.Font).Height +
            title.Padding.Vertical + title.Margin.Vertical + 8;
        int requiredSubtitle = TextRenderer.MeasureText("Agpqy", subtitle.Font).Height +
            subtitle.Padding.Vertical + subtitle.Margin.Vertical + 8;
        float outerHeight = outer.RowStyles[0].Height;
        float titleHeight = header.RowStyles[0].Height;
        float subtitleHeight = header.RowStyles[1].Height;
        if (titleHeight < requiredTitle || subtitleHeight < requiredSubtitle ||
            outerHeight < titleHeight + subtitleHeight + header.Padding.Vertical +
                header.Margin.Vertical)
            throw new InvalidOperationException(
                $"Wizard header descenders or subtitle clipped: {form.Text} at {profile}");
    }

    internal static void Run()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WinForms layout smoke requires Windows.");
        int forms = 0;
        int buttons = 0;
        foreach (var profile in Profiles)
        {
            foreach (var window in Windows)
            {
                // None of the factories call Show/ShowDialog. In particular,
                // Shown-triggered readiness/network operations never run.
                using var form = window.Factory();
                if (form.Visible)
                    throw new InvalidOperationException(
                        $"Unexpected visible UI: {window.Name} ({profile.Name})");
                UiStyle.Apply(form, new UiPreferences {
                    Density = profile.Settings.Density,
                    UiScalePercent = profile.Settings.UiScalePercent,
                    FontScalePercent = profile.Settings.FontScalePercent
                });
                form.PerformLayout();
                CheckMainEditorHeader(form, profile.Name);
                CheckWizardHeader(form, profile.Name);
                // Every operator-facing primary window must show the SAME
                // version as the package. This catches stale hard-coded tags.
                string? versionLabel = window.Name switch
                {
                    "Campaign editor - offline Wizard" or
                    "Campaign editor - update available" => "MainHeaderVersion",
                    "Campaign Wizard - AD connection" or "Campaign Wizard - campaign type" or
                    "Campaign Wizard - Screen Saver-only save" or
                    "Campaign Wizard - images" or "Campaign Wizard - arrange" or
                    "Campaign Wizard - schedule" or
                    "Campaign Wizard - save" or "Campaign Wizard - deploy" => "CampaignWizardVersion",
                    "Agent Management - read-only desktop layout" => "AgentManagementVersion",
                    "AD / GPO deployment - offline Wizard" => "DeploymentVersion",
                    "AD first-step connect/read" => "AdWizardVersion",
                    _ => null
                };
                if (versionLabel is not null)
                {
                    Label? label = Descendants(form).OfType<Label>()
                        .FirstOrDefault(x => x.Name == versionLabel);
                    if (label is null || label.Text != "Version " + AppVersion.Current ||
                        label.Width < 130 || label.Height < 18)
                        throw new InvalidOperationException(
                            "Missing/clipped live version " + versionLabel + " at " + profile.Name);
                }
                CheckWizardFooter(form, profile.Name);
                CheckUpdaterHeader(form, profile.Name);
                CheckUpdaterFieldHeights(form, profile.Name);
                CheckMainWizardStorage(form, profile.Name);
                CheckAdAdvancedOptions(form, profile.Name);
                CheckDpiTableRows(form, profile.Name);
                CheckTimeZoneDifference(form, profile.Name);
                CheckScrollableUncPaths(form, profile.Name);
                if (window.Name == "Full group GPO conflict report - offline")
                {
                    var all = Descendants(form).ToArray();
                    var grid = all.OfType<DataGridView>()
                        .SingleOrDefault(x => x.Name == "GroupConflictGrid");
                    var search = all.OfType<TextBox>()
                        .SingleOrDefault(x => x.Name == "GroupConflictSearch");
                    var filter = all.OfType<ComboBox>()
                        .SingleOrDefault(x => x.Name == "GroupConflictRisk");
                    var export = all.OfType<Button>()
                        .SingleOrDefault(x => x.Name == "GroupConflictExportCsv");
                    if (grid is null || grid.Rows.Count != 1 ||
                        search is null || filter is null || filter.Items.Count != 5 ||
                        export is null || export.Width < 180)
                        throw new InvalidOperationException(
                            "Full read-only group GPO report grid/filter/export missing at " +
                            profile.Name);
                }
                if (window.Name == "Visual DPI / language acceptance")
                {
                    var all = Descendants(form).ToArray();
                    var viewport = all.OfType<Panel>()
                        .SingleOrDefault(x => x.Name == "VisualAcceptanceScroll");
                    var checks = all.OfType<CheckBox>()
                        .Where(x => x.Name.StartsWith("VisualAcceptanceCheck",
                            StringComparison.Ordinal)).ToArray();
                    var save = all.OfType<Button>()
                        .SingleOrDefault(x => x.Name == "VisualAcceptanceSave");
                    var smoke = all.OfType<Button>()
                        .SingleOrDefault(x => x.Name == "VisualAcceptanceRunSmoke");
                    var output = all.OfType<Label>()
                        .SingleOrDefault(x => x.Name == "VisualAcceptanceDisplayStatus");
                    if (viewport?.AutoScroll != true || checks.Length != 6 ||
                        checks.Any(x => x.Checked || x.Height < 45) ||
                        save is null || save.Enabled ||
                        smoke is null || !smoke.Enabled || output is null ||
                        !output.Text.Contains("not checked",
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            "Visual DPI review must start offline, unapproved and scrollable at " +
                            profile.Name);
                }
                if (window.Name == "Campaign assignment matrix")
                {
                    var all = Descendants(form).ToArray();
                    var modes = all.OfType<ComboBox>()
                        .SingleOrDefault(x => x.Name == "AssignmentTargetMode");
                    var content = all.OfType<TableLayoutPanel>()
                        .SingleOrDefault(x => x.Name == "AssignmentScrollableFields");
                    var preview = all.OfType<TextBox>()
                        .SingleOrDefault(x => x.Name == "AssignmentReadOnlyPreview");
                    var computerGroup = all.OfType<ComboBox>()
                        .SingleOrDefault(x => x.Name == "AssignmentComputerGroup");
                    var userGroup = all.OfType<ComboBox>()
                        .SingleOrDefault(x => x.Name == "AssignmentUserGroup");
                    var activityWindow = all.OfType<ComboBox>()
                        .SingleOrDefault(x => x.Name == "AssignmentActivityWindow");
                    if (activityWindow is null || activityWindow.Items.Count != 4 ||
                        activityWindow.SelectedIndex != 1 ||
                        activityWindow.DropDownStyle != ComboBoxStyle.DropDownList)
                        throw new InvalidOperationException(
                            "Computer assignment activity window must be view-only at " + profile.Name);
                    var computerCoverage = all.OfType<Label>()
                        .SingleOrDefault(x => x.Name == "AssignmentComputerCoverage");
                    var userCoverage = all.OfType<Label>()
                        .SingleOrDefault(x => x.Name == "AssignmentUserCoverage");
                    if (computerCoverage?.AutoSize != true ||
                        userCoverage?.AutoSize != true ||
                        !computerCoverage.Text.Contains("not verified",
                            StringComparison.OrdinalIgnoreCase) ||
                        !userCoverage.Text.Contains("not verified",
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            "Independent User/Computer OU counts must start UNVERIFIED without LDAP: " +
                            profile.Name);
                    // The offline matrix factory has two distinct selected
                    // groups. Rebuilding filtered options must retain their
                    // exact original distinguished names across User/Computer.
                    if (computerGroup?.SelectedItem is not CampaignAssignmentsDialog.AdChoice c ||
                        userGroup?.SelectedItem is not CampaignAssignmentsDialog.AdChoice u ||
                        c.Dn != "CN=PCs,OU=Groups,DC=example,DC=com" ||
                        u.Dn != "CN=Pilots,OU=Groups,DC=example,DC=com")
                        throw new InvalidOperationException(
                            "User/Computer group assignments lost during offline matrix initialization: " +
                            profile.Name);
                    var computerMemberFilter = all.OfType<CheckBox>()
                        .SingleOrDefault(x => x.Name == "AssignmentFilterComputerGroupsByOu");
                    var userMemberFilter = all.OfType<CheckBox>()
                        .SingleOrDefault(x => x.Name == "AssignmentFilterUserGroupsByOu");
                    var help = all.OfType<Label>()
                        .SingleOrDefault(x => x.Name == "AssignmentModeHelp");
                    var fields = all.OfType<TableLayoutPanel>()
                        .SingleOrDefault(x => x.Name == "AssignmentScrollableFields");
                    if (fields is null || help?.AutoSize != true ||
                        fields.RowStyles.Count < 16 ||
                        fields.RowStyles[14].SizeType != SizeType.AutoSize ||
                        fields.RowStyles[15].SizeType != SizeType.AutoSize)
                        throw new InvalidOperationException(
                            "Group assignment warnings must auto-wrap at high DPI: " + profile.Name);
                    if (modes is null || modes.Items.Count != 3 ||
                        computerMemberFilter is null || userMemberFilter is null ||
                        !computerMemberFilter.Text.Contains("computer", StringComparison.OrdinalIgnoreCase) ||
                        !userMemberFilter.Text.Contains("user", StringComparison.OrdinalIgnoreCase) ||
                        !modes.Items.Contains(CampaignTargetMode.Both) ||
                        !modes.Items.Contains(CampaignTargetMode.LockScreen) ||
                        !modes.Items.Contains(CampaignTargetMode.ScreenSaver) ||
                        content?.Parent is not Panel scroller || !scroller.AutoScroll ||
                        preview is null ||
                        !all.OfType<CheckBox>().Any(x => x.Name == "AssignmentScreenSaverJpegMode") ||
                        !all.OfType<NumericUpDown>().Any(x => x.Name == "AssignmentScreenSaverSlideSeconds") ||
                        !preview.Text.Contains("Both",
                            StringComparison.Ordinal) ||
                        !preview.Text.Contains("Computer group:",
                            StringComparison.Ordinal) ||
                        !preview.Text.Contains("User group:",
                            StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "Campaign Lock Screen / Screen Saver / Both group targeting is unavailable at " +
                            profile.Name);
                }
                if (window.Name == "Campaign Wizard - campaign type")
                {
                    var all = Descendants(form).ToArray();
                    var mode = all.OfType<ComboBox>()
                        .SingleOrDefault(x => x.Name == "WizardCampaignType");
                    var saver = all.OfType<ComboBox>()
                        .SingleOrDefault(x => x.Name == "WizardScreenSaverExecutable");
                    var timeout = all.OfType<NumericUpDown>()
                        .SingleOrDefault(x => x.Name == "WizardScreenSaverIdleSeconds");
                    var password = all.OfType<CheckBox>()
                        .SingleOrDefault(x => x.Name == "WizardScreenSaverRequirePassword");
                    var photo = all.OfType<CheckBox>()
                        .SingleOrDefault(x => x.Name == "WizardScreenSaverJpegMode");
                    var slideshow = all.OfType<NumericUpDown>()
                        .SingleOrDefault(x => x.Name == "WizardScreenSaverSlideSeconds");
                    var next = all.OfType<Button>()
                        .FirstOrDefault(x => x.Name == "CampaignWizardContinueAction");
                    bool hasSkipped = all.OfType<Label>().Any(x =>
                        x.Text.Contains("(skip)", StringComparison.Ordinal));
                    if (mode is null || mode.SelectedIndex >= 0 ||
                        next is null || next.Enabled || hasSkipped ||
                        mode.Items.Count != 3 ||
                        !mode.Items.Contains(CampaignTargetMode.ScreenSaver) ||
                        !mode.Items.Contains(CampaignTargetMode.Both) ||
                        saver is null || timeout is null || password is null ||
                        photo is null || slideshow is null)
                        throw new InvalidOperationException(
                            "Campaign type must be selected before images at " + profile.Name);
                    mode.SelectedItem = CampaignTargetMode.ScreenSaver;
                    if (!next.Enabled ||
                        !all.OfType<Label>().Any(x => x.Text.StartsWith("3  Images (skip)", StringComparison.Ordinal)) ||
                        !all.OfType<Label>().Any(x => x.Text.StartsWith("4  Arrange (skip)", StringComparison.Ordinal)) ||
                        !all.OfType<Label>().Any(x => x.Text.StartsWith("5  Schedule (skip)", StringComparison.Ordinal)))
                        throw new InvalidOperationException(
                            "Screen Saver-only must immediately skip three image steps at " + profile.Name);
                    photo.Checked = true;
                    if (!slideshow.Enabled || saver.Enabled ||
                        all.OfType<Label>().Any(x => x.Text.Contains("(skip)", StringComparison.Ordinal)))
                        throw new InvalidOperationException(
                            "JPG Screen Saver requires image/arrange/schedule steps at " + profile.Name);
                    photo.Checked = false;
                    if (!all.OfType<Label>().Any(x =>
                        x.Text.StartsWith("3  Images (skip)", StringComparison.Ordinal)))
                        throw new InvalidOperationException(
                            "Returning to built-in Screen Saver must restore skip labels at " + profile.Name);
                    mode.SelectedItem = CampaignTargetMode.Both;
                    if (all.OfType<Label>().Any(x => x.Text.Contains("(skip)", StringComparison.Ordinal)))
                        throw new InvalidOperationException(
                            "Both mode must not skip image steps at " + profile.Name);
                    mode.SelectedItem = CampaignTargetMode.LockScreen;
                    if (all.OfType<Label>().Any(x => x.Text.Contains("(skip)", StringComparison.Ordinal)))
                        throw new InvalidOperationException(
                            "Lock Screen mode must never skip image stages at " + profile.Name);
                    mode.SelectedItem = CampaignTargetMode.ScreenSaver;
                    if (!saver.Enabled || !timeout.Enabled || !password.Enabled)
                        throw new InvalidOperationException(
                            "Screen Saver controls not enabled before images at " + profile.Name);
                }
                CheckTimeZoneSelectors(form, profile.Name);
                if (form is GpoOperationsForm)
                {
                    var lifecycle = Descendants(form).OfType<Button>()
                        .SingleOrDefault(x => x.Name == "GpoLifecyclePreviewButton");
                    if (lifecycle is null || lifecycle.Enabled)
                        throw new InvalidOperationException(
                            "Bug #11 regression: missing GPO selection must disable lifecycle Preview.");
                }
                if (window.Name is "Campaign Wizard - images" or "Campaign Wizard - arrange")
                {
                    var imageList = Descendants(form).OfType<ListBox>()
                        .SingleOrDefault(x => x.Name == "CampaignWizardImageList");
                    var imagePreview = Descendants(form).OfType<PictureBox>()
                        .SingleOrDefault(x => x.Name == "CampaignWizardImagePreview");
                    var imagePath = Descendants(form).OfType<Label>()
                        .SingleOrDefault(x => x.Name == "CampaignWizardImagePath");
                    var frame = Descendants(form).OfType<Panel>()
                        .SingleOrDefault(x => x.Name == "CampaignWizardPreviewFrame");
                    var placeholder = Descendants(form).OfType<Label>()
                        .SingleOrDefault(x => x.Name == "CampaignWizardPreviewPlaceholder");
                    if (imageList is null || imagePreview is null || imagePath is null ||
                        frame is null || placeholder is null ||
                        imagePreview.FindForm() != form ||
                        imagePreview.Parent != frame ||
                        imagePreview.SizeMode != PictureBoxSizeMode.Zoom ||
                        imagePreview.Dock != DockStyle.Fill ||
                        imageList.Items.Count != 1 || imageList.SelectedIndex != 0 ||
                        imagePreview.Image is null ||
                        !imagePath.Text.Contains("120 x 68", StringComparison.Ordinal) ||
                        placeholder.Visible)
                        throw new InvalidOperationException(
                            $"Wizard must show the selected PNG inside its own image/arrange page at {profile.Name}.");
                    // The old regression only verified control existence.
                    // Force a minimum-sized window and verify that DPI layout
                    // has not collapsed the actual preview to a zero-height box.
                    form.Size = new Size(950, 690);
                    form.PerformLayout();
                    foreach (Control pane in Descendants(form).Where(x =>
                        x is TableLayoutPanel or Panel))
                        pane.PerformLayout();
                    if (imagePreview.Width < 180 || imagePreview.Height < 200 ||
                        frame.Width < 180 || frame.Height < 200)
                        throw new InvalidOperationException(
                            $"Wizard preview is visually collapsed at {profile.Name}: " +
                            $"{imagePreview.Width} x {imagePreview.Height}.");
                    // A non-null bitmap alone does not prove that it is painted.
                    imagePreview.CreateControl();
                    using var painted = new Bitmap(imagePreview.Width, imagePreview.Height);
                    imagePreview.DrawToBitmap(painted,
                        new Rectangle(0, 0, painted.Width, painted.Height));
                    Color center = painted.GetPixel(painted.Width / 2, painted.Height / 2);
                    if (Math.Abs(center.R - 23) > 10 ||
                        Math.Abs(center.G - 201) > 10 ||
                        Math.Abs(center.B - 143) > 10)
                        throw new InvalidOperationException(
                            $"Wizard preview did not paint the selected image at {profile.Name}: " +
                            $"center={center.R},{center.G},{center.B}.");
                }
                if (window.Name == "AD / GPO deployment - offline Wizard")
                {
                    var selector = Descendants(form).OfType<ComboBox>()
                        .SingleOrDefault(x => x.Name == "DeploymentClientType");
                    var content = Descendants(form).OfType<ComboBox>()
                        .SingleOrDefault(x => x.Name == "DeploymentContentSource");
                    var unc = Descendants(form).OfType<TextBox>()
                        .SingleOrDefault(x => x.Name == "DeploymentContentShareRoot");
                    var browse = Descendants(form).OfType<Button>()
                        .SingleOrDefault(x => x.Name == "DeploymentContentShareBrowse");
                    var groupListMode = Descendants(form).OfType<ComboBox>()
                        .SingleOrDefault(x => x.Name == "DeploymentGroupListMode");
                    var activityWindow = Descendants(form).OfType<ComboBox>()
                        .SingleOrDefault(x => x.Name == "DeploymentActivityWindow");
                    if (activityWindow is null || activityWindow.Items.Count != 4 ||
                        activityWindow.SelectedIndex != 1 ||
                        activityWindow.DropDownStyle != ComboBoxStyle.DropDownList)
                        throw new InvalidOperationException(
                            "Deployment activity window must be view-only at " + profile.Name);
                    var coverage = Descendants(form).OfType<Label>()
                        .SingleOrDefault(x => x.Name == "DeploymentOuGroupCoverage");
                    if (coverage?.AutoSize != true ||
                        !coverage.Text.Contains("not verified",
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            "AD OU/group coverage must stay UNVERIFIED in offline wizard: " + profile.Name);
                    if (groupListMode is null || groupListMode.Items.Count != 2 ||
                        groupListMode.SelectedIndex != 0 ||
                        groupListMode.DropDownStyle != ComboBoxStyle.DropDownList)
                        throw new InvalidOperationException(
                            $"OU direct computer membership / All groups selector missing at {profile.Name}.");
                    if (content is null || unc is null || browse is null ||
                        content.Items.Count != 2 || content.SelectedIndex != 0 ||
                        content.Width < 220 || content.Height < content.PreferredHeight ||
                        unc.Width < 190 ||
                        !content.Items[0]!.ToString()!.Contains("SYSVOL", StringComparison.Ordinal) ||
                        !content.Items[1]!.ToString()!.Contains("SMB", StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Deployment SYSVOL/SMB selector or UNC editor missing/clipped at {profile.Name}.");
                    // The Browse button must be in its own, visible two-column
                    // folder row, not squeezed past a four-column fixed grid.
                    if (browse.Parent is not TableLayoutPanel folderRow ||
                        folderRow.Name != "DeploymentContentFolderRow" ||
                        folderRow.ColumnCount != 2 ||
                        folderRow.GetControlFromPosition(1, 0) != browse ||
                        folderRow.GetControlFromPosition(0, 0) != unc ||
                        browse.Width < 140 || browse.Height < 25 ||
                        folderRow.Width < browse.Width + unc.Width)
                        throw new InvalidOperationException(
                            $"Deployment UNC Browse row missing or clipped at {profile.Name}.");
                    if (selector is null ||
                        selector.DropDownStyle != ComboBoxStyle.DropDownList ||
                        selector.Items.Count != 2 || selector.SelectedIndex != 0 ||
                        selector.Items[0]?.ToString()?.Contains(".ps1", StringComparison.Ordinal) != true ||
                        selector.Items[1]?.ToString()?.Contains(".exe", StringComparison.Ordinal) != true ||
                        selector.Width < 250 || selector.Height < selector.PreferredHeight)
                        throw new InvalidOperationException(
                            $"Deployment client selector PS1/EXE missing or clipped at {profile.Name}: " +
                            $"found={selector is not null}; width={selector?.Width}; " +
                            $"height={selector?.Height}; preferred={selector?.PreferredHeight}; " +
                            $"items={selector?.Items.Count}; index={selector?.SelectedIndex}; " +
                            $"style={selector?.DropDownStyle}.");
                }
                if (form is MainForm || form.Text.Contains("Campaign Wizard",
                        StringComparison.OrdinalIgnoreCase) && window.Name.EndsWith("save"))
                    CheckCampaignNameLayout(form, profile.Name);
                var controls = Descendants(form).ToArray();
                var actual = controls.OfType<Button>().ToArray();
                foreach (string label in window.ExpectedButtons)
                {
                    if (!actual.Any(x => x.Text == label))
                        throw new InvalidOperationException(
                            $"Missing {window.Name} action '{label}' at {profile.Name}.");
                }
                foreach (Button button in actual)
                {
                    if (button.Width <= 0 || button.Height <= 0)
                        throw new InvalidOperationException(
                            $"Invalid button dimensions: {window.Name}/{button.Text}");
                    // Fixed/docked buttons may be intentionally ellipsized.
                    // Non-docked buttons must keep the styled min text width.
                    if (button.Dock == DockStyle.None)
                    {
                        int needed = TextRenderer.MeasureText(button.Text, button.Font).Width + 16;
                        if (button.Width < needed && !button.AutoEllipsis)
                            throw new InvalidOperationException(
                                $"Clipped button without ellipsis: {window.Name}/{button.Text} at {profile.Name}");
                    }
                }
                var area = new Rectangle(0, 0, 1440, 900);
                var restored = UiWindowPlacement.ClampToScreens(
                    new Rectangle(-25000, -25000, form.Width, form.Height),
                    form.MinimumSize, new[] { area });
                if (!area.Contains(restored) || restored.Width < 1 || restored.Height < 1)
                    throw new InvalidOperationException(
                        $"Off-monitor restored bounds: {window.Name} at {profile.Name}");
                if (form.IsDisposed)
                    throw new InvalidOperationException("Unexpected form disposal during layout.");
                forms++;
                buttons += actual.Length;
            }
        }
        CheckHebrewRtlAndLanguageSelection();
        CheckHebrewCampaignWizardPages();
        CheckHebrewAdAndClientHealth();
        CheckHebrewGpoOperationsAndDiagnostics();
        CheckRussianThreeLanguageScheduleAndAdvanced();
        CheckRussianHebrewAdvanced95();
        CheckPreview96UpdaterLabLanguageAndDpi();
        CheckPreview97GpoClientHealth();
        CheckRepairResultTranslations();
        foreach (string language in new[] { "Russian", "Hebrew" })
        {
            const string caption = "Visual DPI / language acceptance";
            string translated = UiLocale.Translate(caption, language);
            if (translated == caption ||
                UiLocale.Translate(translated, "English") != caption ||
                UiLocale.Translate(translated, language) != translated)
                throw new InvalidOperationException(
                    "Preview.102 visual DPI caption must translate and reverse: " + language);
        }
        Console.WriteLine($"PASS: {forms} offline WinForms dialog layouts and {buttons} button checks; " +
            "font profiles 100%, 125%, 150%, 175%, 187.5%, 200%; no dialog shown, no AD/gMSA/RPC or saved UI settings.");
        Console.WriteLine("NOTE: actual hardware DPI 200% and visual text-overflow inspection require an interactive VM.");
    }

    private static void CheckHebrewRtlAndLanguageSelection()
    {
        // Settings are previewed without writing ui-settings.json or changing
        // global preferences; Windows does not need an AD session for this.
        foreach (var profile in Profiles)
        {
            var hebrew = new UiPreferences {
                Language = "Hebrew", Density = profile.Settings.Density,
                UiScalePercent = profile.Settings.UiScalePercent,
                FontScalePercent = profile.Settings.FontScalePercent
            };
            using var settings = new UiSettingsDialog();
            using var viewer = new LocalLabEvidenceDialog();
            using var editor = new MainForm();
            foreach (Form form in new Form[] { settings, viewer, editor })
            {
                UiStyle.Apply(form, hebrew);
                form.PerformLayout();
                if (form.RightToLeft != RightToLeft.Yes || !form.RightToLeftLayout)
                    throw new InvalidOperationException(
                        $"Hebrew RTL form layout was not enabled: {form.Name} / {profile.Name}");
            }
            if (!Descendants(settings).OfType<ComboBox>().Any(x =>
                    x.Name == "UiSettingsLanguage" && x.SelectedIndex >= 0) ||
                !Descendants(settings).OfType<Label>().Any(x => x.Text == "שפת ממשק") ||
                !Descendants(settings).OfType<Button>().Any(x => x.Text == "החל") ||
                !Descendants(viewer).OfType<Button>().Any(x =>
                    x.Name == "LocalLabCanaryImport" && x.Text.Contains("ייבא", StringComparison.Ordinal)) ||
                !Descendants(editor).OfType<Button>().Any(x => x.Text == "אשף"))
                throw new InvalidOperationException(
                    $"Hebrew language or main/window translations missing: {profile.Name}");
            if (Descendants(viewer).OfType<TextBox>().Where(x => x.ReadOnly)
                .Any(x => x.RightToLeft != RightToLeft.No))
                throw new InvalidOperationException(
                    $"Hebrew RTL would reverse technical JSON report text: {profile.Name}");
            UiStyle.Apply(settings, new UiPreferences { Language = "English" });
            if (settings.RightToLeft != RightToLeft.No || settings.RightToLeftLayout ||
                !Descendants(settings).OfType<Label>().Any(x => x.Text == "Language"))
                throw new InvalidOperationException(
                    $"Switching Hebrew back to English did not restore LTR: {profile.Name}");
        }
    }

    private static void CheckHebrewCampaignWizardPages()
    {
        // Exercise all seven wizard pages without ShowDialog, LDAP, GPO, SMB,
        // local file export or saved UI preference changes.
        var hebrew = new UiPreferences { Language = "Hebrew" };
        var english = new UiPreferences { Language = "English" };
        using var editor = new MainForm();
        CampaignContentSource originalSource = editor.CampaignSourceForSmokeTest;
        string originalShare = editor.CampaignShareRootForSmokeTest;

        for (int page = 0; page < 7; page++)
        {
            using Form wizard = editor.CreateCampaignWizardForSmokeTest(page);
            UiStyle.Apply(wizard, hebrew);
            var children = Descendants(wizard).ToArray();
            var title = children.OfType<Label>()
                .Single(x => x.Name == "CampaignWizardHeaderTitle");
            var subtitle = children.OfType<Label>()
                .Single(x => x.Name == "CampaignWizardHeaderSubtitle");
            if (title.Text != "אשף הקמפיינים" ||
                !subtitle.Text.StartsWith($"שלב {page + 1} מתוך 7 — ", StringComparison.Ordinal) ||
                !children.OfType<Label>().Any(x => x.Text == "1  חיבור ל-AD") ||
                !wizard.RightToLeftLayout)
                throw new InvalidOperationException(
                    $"Hebrew wizard page/step text missing: page {page + 1}.");

            if (page == 1)
            {
                ComboBox type = children.OfType<ComboBox>()
                    .Single(x => x.Name == "WizardCampaignType");
                if (!type.FormattingEnabled || type.SelectedIndex != -1 ||
                    !type.Items.Contains(CampaignTargetMode.Both) ||
                    UiLocale.Translate(type.Items[0]!.ToString()!, "Hebrew") == type.Items[0]!.ToString())
                    throw new InvalidOperationException(
                        "Hebrew campaign-type display must preserve underlying enum and initial empty selection.");
            }
            if (page is 5 or 6)
            {
                ComboBox source = children.OfType<ComboBox>()
                    .Single(x => x.Name == "CampaignWizardContentSource");
                TextBox unc = children.OfType<TextBox>()
                    .Single(x => x.Name == "CampaignWizardContentShareRoot");
                if (!source.FormattingEnabled || source.SelectedIndex != 0 ||
                    source.Items[0]?.ToString() != "SYSVOL (default)" ||
                    !UiLocale.Translate("SMB network folder (UNC)", "Hebrew").Contains("SMB", StringComparison.Ordinal) ||
                    unc.RightToLeft != RightToLeft.No)
                    throw new InvalidOperationException(
                        "Hebrew display must preserve SYSVOL/SMB keys and left-to-right UNC entry.");
            }

            UiStyle.Apply(wizard, english);
            if (title.Text != "Campaign Wizard" ||
                !subtitle.Text.StartsWith($"Step {page + 1} of 7  -  ", StringComparison.Ordinal) ||
                wizard.RightToLeftLayout)
                throw new InvalidOperationException(
                    $"Reversing Hebrew wizard layout or captions failed: page {page + 1}.");
        }
        if (editor.CampaignSourceForSmokeTest != originalSource ||
            editor.CampaignShareRootForSmokeTest != originalShare)
            throw new InvalidOperationException(
                "Language-only wizard checks unexpectedly changed campaign storage settings.");
    }

    private static void CheckHebrewAdAndClientHealth()
    {
        // Pure UI-only acceptance: no Shown event, directory queries,
        // SMB reads, user passwords, deployment, exports or saved settings.
        foreach (var profile in Profiles)
        {
            var he = new UiPreferences {
                Language = "Hebrew", Density = profile.Settings.Density,
                UiScalePercent = profile.Settings.UiScalePercent,
                FontScalePercent = profile.Settings.FontScalePercent
            };
            var en = new UiPreferences {
                Language = "English", Density = profile.Settings.Density,
                UiScalePercent = profile.Settings.UiScalePercent,
                FontScalePercent = profile.Settings.FontScalePercent
            };

            using var ad = new AdConnectionStartDialog();
            using var account = new AdAlternateAccountWizard(
                "example.com", "dc01.example.com", "", "", false, false, true);
            account.ShowPasswordPageForSmokeTest();
            var campaign = new CampaignProject {
                Name = "ui-test", TimeZoneId = "UTC",
                StartLocal = new DateTime(2026, 11, 10, 12, 0, 0),
                ContentSource = CampaignContentSource.Sysvol
            };
            using var deploy = new DomainDeploymentForm(campaign);
            using var feedback = new FeedbackSettingsDialog(campaign);
            using var healthWindow = new Form { Width = 1220, Height = 820 };
            var health = new ClientFeedbackPanel("example.com", "dc01.example.com",
                "OU=Computers,DC=example,DC=com",
                "CN=TestPCs,DC=example,DC=com",
                new DomainCredentials("example.com", "", ""), campaign);
            healthWindow.Controls.Add(health);

            ComboBox content = Descendants(deploy).OfType<ComboBox>()
                .Single(x => x.Name == "DeploymentContentSource");
            ComboBox agent = Descendants(deploy).OfType<ComboBox>()
                .Single(x => x.Name == "DeploymentClientType");
            var grid = Descendants(healthWindow).OfType<DataGridView>()
                .Single(x => x.Name == "ClientHealthGrid");

            string[] sourceValues = content.Items.Cast<object>()
                .Select(x => x.ToString() ?? "").ToArray();
            string[] agentValues = agent.Items.Cast<object>()
                .Select(x => x.ToString() ?? "").ToArray();
            string[] originalHeaders = grid.Columns.Cast<DataGridViewColumn>()
                .Select(x => x.HeaderText).ToArray();
            int sourceIndex = content.SelectedIndex, agentIndex = agent.SelectedIndex;
            string share = campaign.FeedbackShare;
            foreach (Form form in new Form[] { ad, account, deploy, feedback, healthWindow })
            {
                UiStyle.Apply(form, he);
                form.PerformLayout();
                if (!form.RightToLeftLayout || form.RightToLeft != RightToLeft.Yes)
                    throw new InvalidOperationException(
                        "AD/Client Health Hebrew RTL missing: " + form.Name + " / " + profile.Name);
            }
            if (!Descendants(ad).OfType<Label>()
                    .Any(x => x.Text == "אשף חיבור ל-AD") ||
                !Descendants(account).OfType<Label>()
                    .Any(x => x.Text == "הזן סיסמת חשבון") ||
                !Descendants(deploy).OfType<Label>()
                    .Any(x => x.Text == "פריסת Active Directory / GPO") ||
                !Descendants(healthWindow).OfType<Button>()
                    .Any(x => x.Text == "טען מחשבים מ-AD") ||
                grid.Columns[0].HeaderText != "מחשב" ||
                grid.Columns[1].HeaderText != "מצב" ||
                !agent.FormattingEnabled || !content.FormattingEnabled ||
                content.SelectedIndex != sourceIndex || agent.SelectedIndex != agentIndex ||
                content.Items.Cast<object>().Select(x => x.ToString()).Where(x => x is not null)
                    .SequenceEqual(sourceValues) == false ||
                agent.Items.Cast<object>().Select(x => x.ToString()).Where(x => x is not null)
                    .SequenceEqual(agentValues) == false)
                throw new InvalidOperationException(
                    "AD/Client Health visible Hebrew or immutable selection values failed: " + profile.Name);

            foreach (Form form in new Form[] { ad, account, deploy, feedback, healthWindow })
            {
                UiStyle.Apply(form, en);
                if (form.RightToLeftLayout || form.RightToLeft != RightToLeft.No)
                    throw new InvalidOperationException(
                        "AD/Client Health could not return to English: " + profile.Name);
            }
            if (grid.Columns.Cast<DataGridViewColumn>()
                    .Select(x => x.HeaderText).SequenceEqual(originalHeaders) == false ||
                campaign.ContentSource != CampaignContentSource.Sysvol ||
                campaign.ClientType != CampaignClientType.PowerShell ||
                campaign.FeedbackShare != share)
                throw new InvalidOperationException(
                    "Language switch changed client report columns or campaign settings: " + profile.Name);
        }
    }

    private static void CheckHebrewGpoOperationsAndDiagnostics()
    {
        // Do not Show/ShowDialog any form. These are display-only checks
        // against synthetic AD values, not endpoint or Group Policy tests.
        foreach (var profile in Profiles)
        {
            var hebrew = new UiPreferences {
                Language = "Hebrew", Density = profile.Settings.Density,
                UiScalePercent = profile.Settings.UiScalePercent,
                FontScalePercent = profile.Settings.FontScalePercent
            };
            var english = new UiPreferences {
                Language = "English", Density = profile.Settings.Density,
                UiScalePercent = profile.Settings.UiScalePercent,
                FontScalePercent = profile.Settings.FontScalePercent
            };
            var campaign = new CampaignProject {
                Name = "rtl-ci", TimeZoneId = "UTC",
                StartLocal = new DateTime(2026, 11, 10, 12, 0, 0),
                ClientType = CampaignClientType.PowerShell
            };
            using var operations = new GpoOperationsForm(
                "example.com", "dc01.example.com",
                "OU=Computers,DC=example,DC=com",
                "CN=TestPCs,DC=example,DC=com",
                new DomainCredentials("example.com", "", ""), campaign);
            using var report = new GroupConflictReportDialog([
                new GroupConflictTarget("RTL pilot", "Computer",
                    "OU=Computers,DC=example,DC=com",
                    "CN=TestPCs,DC=example,DC=com", 1, 0, [
                        new GroupConflictFinding("RTL pilot", "Computer", "Test GPO",
                            Guid.Empty.ToString("D"), "High", "NoLockScreen",
                            "1", "Registry.pol", "Software\\Policies",
                            "OU=Computers,DC=example,DC=com",
                            "Unknown until gpresult")
                    ], ["Read-only UI fixture"])
            ]);
            using var trends = new DiagnosticsTrendsWindow();
            var lifecycle = Descendants(operations).OfType<ComboBox>()
                .Single(x => x.Name == "GpoLifecycleAction");
            var push = Descendants(operations).OfType<ComboBox>()
                .Single(x => x.Name == "GpoPushPurpose");
            var activity = Descendants(operations).OfType<ComboBox>()
                .Single(x => x.Name == "AgentActivityFilter");
            var gmsaFilter = Descendants(operations).OfType<ComboBox>()
                .Single(x => x.Name == "GpoGmsaFilter");
            var risk = Descendants(report).OfType<ComboBox>()
                .Single(x => x.Name == "GroupConflictRisk");
            var boxes = new[] { lifecycle, push, activity, gmsaFilter, risk };
            string[][] values = boxes.Select(box => box.Items.Cast<object>()
                .Select(x => x.ToString() ?? "").ToArray()).ToArray();
            int[] indices = boxes.Select(box => box.SelectedIndex).ToArray();
            var grids = new[] {
                Descendants(operations).OfType<DataGridView>()
                    .Single(x => x.Name == "GpoDeferredRepairGrid"),
                Descendants(operations).OfType<DataGridView>()
                    .Single(x => x.Name == "GpoGmsaQueueGrid"),
                Descendants(report).OfType<DataGridView>()
                    .Single(x => x.Name == "GroupConflictGrid"),
                Descendants(trends).OfType<DataGridView>()
                    .Single(x => x.Name == "DiagnosticsTrendsGrid")
            };
            string[][] originalHeaders = grids.Select(grid =>
                grid.Columns.Cast<DataGridViewColumn>()
                    .Select(column => column.HeaderText).ToArray()).ToArray();

            foreach (var form in new Form[] { operations, report, trends })
            {
                UiStyle.Apply(form, hebrew);
                form.PerformLayout();
                if (form.RightToLeft != RightToLeft.Yes || !form.RightToLeftLayout)
                    throw new InvalidOperationException(
                        "Preview.93 Hebrew RTL failed: " + profile.Name);
            }
            var tabs = Descendants(operations).OfType<TabPage>()
                .Select(x => x.Text).ToArray();
            if (!operations.Text.Contains("פעולות GPO", StringComparison.Ordinal) ||
                !tabs.Contains("תיקון מושהה") ||
                !tabs.Contains("gMSA 24/7") ||
                !Descendants(operations).OfType<CheckBox>()
                    .Any(x => x.Text.StartsWith("gMSA מסביב לשעון", StringComparison.Ordinal)) ||
                !report.Text.Contains("דוח התנגשויות GPO מלא", StringComparison.Ordinal) ||
                !trends.Text.StartsWith("מגמות שגיאות", StringComparison.Ordinal) ||
                grids[0].Columns[0].HeaderText != "מחשב" ||
                grids[0].Columns[1].HeaderText != "קמפיין" ||
                grids[1].Columns[2].HeaderText != "סטטוס" ||
                grids[2].Columns[0].HeaderText != "יעד" ||
                grids[3].Columns[0].HeaderText != "כמות")
                throw new InvalidOperationException(
                    "Preview.93 Hebrew GPO/diagnostics window text missing: " + profile.Name);

            foreach (var box in boxes)
                if (!box.FormattingEnabled)
                    throw new InvalidOperationException(
                        "Operator enum/filter needs display-only ComboBox formatting.");
            for (int i = 0; i < boxes.Length; i++)
                if (boxes[i].SelectedIndex != indices[i] ||
                    !boxes[i].Items.Cast<object>().Select(x => x.ToString() ?? "")
                        .SequenceEqual(values[i]))
                    throw new InvalidOperationException(
                        "Hebrew localization modified GPO operation or risk key.");

            foreach (var form in new Form[] { operations, report, trends })
            {
                UiStyle.Apply(form, english);
                if (form.RightToLeft != RightToLeft.No || form.RightToLeftLayout)
                    throw new InvalidOperationException(
                        "Preview.93 English reversal failed: " + profile.Name);
            }
            for (int i = 0; i < grids.Length; i++)
                if (!grids[i].Columns.Cast<DataGridViewColumn>()
                    .Select(column => column.HeaderText).SequenceEqual(originalHeaders[i]))
                    throw new InvalidOperationException(
                        "English reversal damaged report header: " + profile.Name);
            if (risk.SelectedItem?.ToString() != "All" ||
                lifecycle.SelectedItem?.ToString() != "Disable" ||
                push.SelectedItem?.ToString() != "Push" ||
                campaign.ClientType != CampaignClientType.PowerShell)
                throw new InvalidOperationException(
                    "Display-only localization changed GPO action or client type.");
        }
    }

    private static void CheckRussianThreeLanguageScheduleAndAdvanced()
    {
        // No Show(), no saved settings, no AD/SMB/GPO writes. All UI fixtures
        // use synthetic domains and local-only campaign values.
        if (new UiPreferences { Language = "Russian" }.Validate().Language != "Russian")
            throw new InvalidOperationException("Russian must remain a persisted UI option.");
        if (new UiPreferences { Language = "Unknown" }.Validate().Language != "English")
            throw new InvalidOperationException("Unknown language must fail back to English.");

        const string englishStep = "Step 4 of 7  -  Schedule";
        const string russianStep = "Шаг 4 из 7 — Расписание";
        const string hebrewStep = "שלב 4 מתוך 7 — תזמון";
        if (UiLocale.Translate(englishStep, "Russian") != russianStep ||
            UiLocale.Translate(russianStep, "Hebrew") != hebrewStep ||
            UiLocale.Translate(hebrewStep, "English") != englishStep ||
            UiLocale.Translate(UiLocale.Translate("Schedule:", "Hebrew"), "Russian") !=
                "Расписание:" ||
            UiLocale.Translate(UiLocale.Translate("Apply", "Russian"), "English") !=
                "Apply")
            throw new InvalidOperationException("Three-language captions are not reversible.");

        foreach (var profile in Profiles)
        {
            UiPreferences Style(string language) => new() {
                Language = language, Density = profile.Settings.Density,
                UiScalePercent = profile.Settings.UiScalePercent,
                FontScalePercent = profile.Settings.FontScalePercent
            };
            using var settings = new UiSettingsDialog();
            using var schedule = Windows.Single(x => x.Name == "Campaign schedule").Factory();
            using var company = Windows.Single(x => x.Name == "Company profile editor").Factory();
            using var assignments = Windows.Single(x => x.Name == "Campaign assignment matrix").Factory();
            using var operations = Windows.Single(
                x => x.Name == "Agent Management - read-only desktop layout").Factory();

            var allForms = new Form[] { settings, schedule, company, assignments, operations };
            var options = Descendants(settings).OfType<ComboBox>()
                .Single(x => x.Name == "UiSettingsLanguage");
            if (options.Items.Count != 3 ||
                options.Items[0]?.ToString() != "English" ||
                options.Items[1]?.ToString() != "עברית (Hebrew)" ||
                options.Items[2]?.ToString() != "Русский")
                throw new InvalidOperationException(
                    "English / Hebrew / Russian language picker is incomplete.");

            ComboBox mode = Descendants(schedule).OfType<ComboBox>()
                .Single(x => x.Name == "CampaignScheduleMode");
            ComboBox target = Descendants(assignments).OfType<ComboBox>()
                .Single(x => x.Name == "AssignmentTargetMode");
            ComboBox lifecycle = Descendants(operations).OfType<ComboBox>()
                .Single(x => x.Name == "GpoLifecycleAction");
            ComboBox remote = Descendants(operations).OfType<ComboBox>()
                .Single(x => x.Name == "GpoPushPurpose");
            var pickers = new[] { mode, target, lifecycle, remote };
            var keys = pickers.Select(x => x.Items.Cast<object>()
                .Select(item => item.ToString() ?? "").ToArray()).ToArray();
            var selectedIndices = pickers.Select(x => x.SelectedIndex).ToArray();
            var grid = Descendants(schedule).OfType<DataGridView>()
                .Single(x => x.Name == "CampaignScheduleGrid");
            string[] originalColumns = grid.Columns.Cast<DataGridViewColumn>()
                .Select(x => x.HeaderText).ToArray();

            foreach (Form form in allForms)
            {
                UiStyle.Apply(form, Style("Russian"));
                form.PerformLayout();
                if (form.RightToLeft != RightToLeft.No || form.RightToLeftLayout)
                    throw new InvalidOperationException(
                        "Russian must use LTR: " + form.Name + " / " + profile.Name);
            }
            if (!Descendants(settings).OfType<Label>().Any(x => x.Text == "Язык") ||
                !Descendants(schedule).OfType<Label>().Any(x => x.Text == "Расписание:") ||
                !Descendants(company).OfType<Label>().Any(x => x.Text == "Название компании") ||
                !Descendants(assignments).OfType<CheckBox>().Any(x =>
                    x.Text.StartsWith("Использовать слайд-шоу JPG", StringComparison.Ordinal)) ||
                schedule.Text != "Расписание кампании — изображения и даты" ||
                company.Text != "Новый профиль компании" ||
                grid.Columns[1].HeaderText != "Дата / время смены" ||
                !mode.FormattingEnabled || !target.FormattingEnabled ||
                !lifecycle.FormattingEnabled || !remote.FormattingEnabled)
                throw new InvalidOperationException(
                    "Russian schedule/advanced translation missing: " + profile.Name);

            foreach (Form form in allForms)
                UiStyle.Apply(form, Style("Hebrew"));
            if (!schedule.RightToLeftLayout ||
                schedule.Text != "תזמון הקמפיין — תמונות ותאריכים" ||
                grid.Columns[1].HeaderText != "תאריך / שעת החלפה")
                throw new InvalidOperationException(
                    "Hebrew translation after Russian failed: " + profile.Name);
            foreach (Form form in allForms)
                UiStyle.Apply(form, Style("English"));
            if (schedule.RightToLeftLayout ||
                schedule.Text != "Campaign Schedule - Images and Dates" ||
                company.Text != "New company profile" ||
                grid.Columns.Cast<DataGridViewColumn>()
                    .Select(x => x.HeaderText).SequenceEqual(originalColumns) == false)
                throw new InvalidOperationException(
                    "English translation after Russian/Hebrew failed: " + profile.Name);

            for (int i = 0; i < pickers.Length; i++)
            {
                if (pickers[i].SelectedIndex != selectedIndices[i] ||
                    !pickers[i].Items.Cast<object>()
                        .Select(item => item.ToString() ?? "").SequenceEqual(keys[i]))
                    throw new InvalidOperationException(
                        "Locale switch changed schedule, group or GPO operation values.");
            }
            var zone = Descendants(schedule).OfType<ComboBox>()
                .Single(x => x.Name == "ScheduleTimeZoneSelect");
            var difference = Descendants(schedule).OfType<Label>()
                .Single(x => x.Name == "ScheduleTimeZoneDifference");
            if (zone.RightToLeft != RightToLeft.No ||
                difference.RightToLeft != RightToLeft.No)
                throw new InvalidOperationException(
                    "Language switch reversed a technical Windows timezone ID.");
        }
    }

    private static void CheckRussianHebrewAdvanced95()
    {
        // CI-only synthetic data: never Show() a dialog; Shown would start a
        // readiness probe. No AD, RPC, SMB write, gMSA installation or JSON export.
        foreach (var profile in Profiles)
        {
            UiPreferences Style(string lang) => new() {
                Language = lang, Density = profile.Settings.Density,
                UiScalePercent = profile.Settings.UiScalePercent,
                FontScalePercent = profile.Settings.FontScalePercent
            };
            const string domain = "example.com";
            const string ou = "OU=Clients,DC=example,DC=com";
            const string group = "CN=Clients,OU=Groups,DC=example,DC=com";
            const string share = @"\\files.example.com\ClientHealth$";

            using var setup = new GmsaRepairSetupDialog(domain, ou, group, share);
            using var readiness = new GmsaReadinessDialog(domain, ou, group);
            using var feedback = new FeedbackSettingsDialog(new CampaignProject {
                Name = "advanced-rtl-ci", TimeZoneId = "UTC",
                EnableFeedback = true, FeedbackShare = share
            });
            using var alerts = new ClientAlertSettingsDialog(new ClientAlertPreferences());
            using var operations = new GpoOperationsForm(
                domain, "dc01.example.com", ou, group,
                new DomainCredentials(domain, "", ""),
                new CampaignProject { Name = "advanced-ci", TimeZoneId = "UTC" });

            Form[] forms = [setup, readiness, feedback, alerts, operations];
            var checkGrid = Descendants(readiness).OfType<DataGridView>()
                .Single(x => x.Name == "GmsaReadinessGrid");
            var deviceGrid = Descendants(operations).OfType<DataGridView>()
                .Single(x => x.Name == "DeviceRepairGrid");
            var deviceFilter = Descendants(operations).OfType<ComboBox>()
                .Single(x => x.Name == "DeviceRepairFilter");
            var inputShare = Descendants(feedback).OfType<TextBox>()
                .Single(x => x.Name == "FeedbackShareScrollable");
            var help = Descendants(feedback).OfType<TextBox>()
                .Single(x => x.Name == "FeedbackSmbInstructions");
            string[] checkColumns = checkGrid.Columns.Cast<DataGridViewColumn>()
                .Select(x => x.HeaderText).ToArray();
            string[] deviceColumns = deviceGrid.Columns.Cast<DataGridViewColumn>()
                .Select(x => x.HeaderText).ToArray();
            string[] filterKeys = deviceFilter.Items.Cast<object>()
                .Select(x => x.ToString() ?? "").ToArray();
            int filterIndex = deviceFilter.SelectedIndex;

            foreach (var form in forms)
            {
                UiStyle.Apply(form, Style("Russian"));
                form.PerformLayout();
                if (form.RightToLeft != RightToLeft.No || form.RightToLeftLayout)
                    throw new InvalidOperationException(
                        "Advanced95 Russian must remain LTR: " + form.Text +
                        " / " + profile.Name);
            }
            if (setup.Text != "Настройка gMSA 24/7" ||
                readiness.Text != "Готовность gMSA — только чтение" ||
                checkGrid.Columns[1].HeaderText != "Результат" ||
                deviceGrid.Columns[3].HeaderText != "Ожидаемое изображение" ||
                !deviceFilter.FormattingEnabled ||
                !Descendants(setup).OfType<Button>()
                    .Any(x => x.Text == "Установить / исправить") ||
                !Descendants(alerts).OfType<Label>()
                    .Any(x => x.Text == "Рост числа проблем на") ||
                !help.Text.StartsWith("Принцип работы:", StringComparison.Ordinal) ||
                inputShare.Text != share || deviceFilter.SelectedIndex != filterIndex)
                throw new InvalidOperationException(
                    "Advanced95 Russian setup/readiness/dashboard captions missing: " +
                    profile.Name);

            foreach (var form in forms)
                UiStyle.Apply(form, Style("Hebrew"));
            if (setup.Text != "הגדרת gMSA מסביב לשעון" ||
                readiness.Text != "מוכנות gMSA לייצור — קריאה בלבד" ||
                !setup.RightToLeftLayout || !readiness.RightToLeftLayout ||
                checkGrid.Columns[1].HeaderText != "תוצאה" ||
                deviceGrid.Columns[3].HeaderText != "תמונה צפויה" ||
                !help.Text.StartsWith("כיצד זה עובד:", StringComparison.Ordinal) ||
                help.RightToLeft != RightToLeft.Yes)
                throw new InvalidOperationException(
                    "Advanced95 Hebrew RTL/labels missing: " + profile.Name);

            foreach (var form in forms)
                UiStyle.Apply(form, Style("English"));
            if (setup.Text != "gMSA 24/7 — Setup" ||
                readiness.Text != "gMSA Production Readiness — read-only" ||
                setup.RightToLeftLayout || readiness.RightToLeftLayout ||
                checkGrid.Columns.Cast<DataGridViewColumn>()
                    .Select(x => x.HeaderText).SequenceEqual(checkColumns) == false ||
                deviceGrid.Columns.Cast<DataGridViewColumn>()
                    .Select(x => x.HeaderText).SequenceEqual(deviceColumns) == false ||
                !help.Text.StartsWith("How it works:", StringComparison.Ordinal) ||
                help.RightToLeft != RightToLeft.No ||
                inputShare.Text != share ||
                deviceFilter.SelectedIndex != filterIndex ||
                !deviceFilter.Items.Cast<object>()
                    .Select(x => x.ToString() ?? "").SequenceEqual(filterKeys))
                throw new InvalidOperationException(
                    "Advanced95 reverse switch damaged UI or technical values: " +
                    profile.Name);
        }
    }

    private static void CheckPreview96UpdaterLabLanguageAndDpi()
    {
        // Offline fixtures: do not show either Form or invoke Check/Install/Import.
        // The updater never opens GitHub, replaces an EXE or writes an AD/GPO.
        const string intro = "Check for updates to compare your installed version with GitHub Releases.\n\n" +
            "1. Check for updates (Stable or Preview).\n" +
            "2. Install: automatically download and verify, then request approval to restart.\n\n" +
            "Updating the portable EXE never modifies GPO or gMSA installations.";
        if (!UiLocale.Translate(intro, "Russian").StartsWith(
                "Проверьте обновления", StringComparison.Ordinal) ||
            !UiLocale.Translate(intro, "Hebrew").StartsWith(
                "בדוק עדכונים", StringComparison.Ordinal) ||
            UiLocale.Translate(UiLocale.Translate(intro, "Russian"), "English") != intro ||
            UiLocale.Translate(UiLocale.Translate(intro, "Hebrew"), "English") != intro)
            throw new InvalidOperationException("Updater operator help is not reversible.");

        const string warning = "GitHub release hashes do NOT prove publisher identity. " +
            "These preview builds are unsigned. Continue only if you trust the official project release.";
        if (UiLocale.Translate(warning, "Russian") == warning ||
            UiLocale.Translate(warning, "Hebrew") == warning ||
            UiLocale.Translate(UiLocale.Translate(warning, "Hebrew"), "English") != warning)
            throw new InvalidOperationException(
                "Unsigned updater warning must remain fully translated and reversible.");

        foreach (var profile in Profiles)
        {
            UiPreferences Style(string language) => new() {
                Language = language, Density = profile.Settings.Density,
                UiScalePercent = profile.Settings.UiScalePercent,
                FontScalePercent = profile.Settings.FontScalePercent
            };
            using var updater = new UniversalUpdaterWindow();
            using var lab = new LocalLabEvidenceDialog();
            Form[] forms = [updater, lab];
            var channel = Descendants(updater).OfType<ComboBox>()
                .Single(x => x.Name == "UpdaterReleaseChannel");
            int originalIndex = channel.SelectedIndex;
            string[] items = channel.Items.Cast<object>()
                .Select(x => x.ToString() ?? "").ToArray();
            var version = Descendants(updater).OfType<TextBox>()
                .Single(x => x.Name == "UpdaterInstalledVersion");
            var repository = Descendants(updater).OfType<TextBox>()
                .Single(x => x.Name == "UpdaterRepositoryValue");
            var output = Descendants(updater).OfType<TextBox>()
                .Single(x => x.Name == "UpdaterResultOutput");
            string originalVersion = version.Text;
            string originalRepository = repository.Text;
            var labStatus = Descendants(lab).OfType<Label>()
                .Single(x => x.Name == "LocalLabCanaryImportStatus");

            foreach (Form f in forms) UiStyle.Apply(f, Style("Russian"));
            updater.PerformLayout();
            CheckUpdaterHeader(updater, profile.Name + "/ru");
            CheckUpdaterFieldHeights(updater, profile.Name + "/ru");
            if (updater.RightToLeftLayout || lab.RightToLeftLayout ||
                updater.Text != "Обновления — LockScreen Campaign Manager" ||
                lab.Text != "LockScreen Campaign Manager — локальные испытания" ||
                labStatus.Text != "Отчёт не импортирован" ||
                !channel.FormattingEnabled ||
                !Descendants(updater).OfType<Label>().Any(x =>
                    x.Name == "UpdaterHeaderSubtitle" &&
                    x.Text.StartsWith("1  Проверить обновления", StringComparison.Ordinal)) ||
                !Descendants(lab).OfType<Button>().Any(x =>
                    x.Name == "LocalLabCanaryImport" && x.Text.Contains("Импортировать", StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    "Preview.96 Russian Updater/Lab captions missing: " + profile.Name);

            foreach (Form f in forms) UiStyle.Apply(f, Style("Hebrew"));
            updater.PerformLayout();
            CheckUpdaterHeader(updater, profile.Name + "/he");
            CheckUpdaterFieldHeights(updater, profile.Name + "/he");
            if (!updater.RightToLeftLayout || !lab.RightToLeftLayout ||
                updater.Text != "עדכונים — LockScreen Campaign Manager" ||
                lab.Text != "LockScreen Campaign Manager — ממצאי בדיקות מקומיות" ||
                labStatus.Text != "לא יובא דוח" ||
                !Descendants(updater).OfType<Label>().Any(x =>
                    x.Name == "UpdaterHeaderSubtitle" &&
                    x.Text.StartsWith("1  בדוק עדכונים", StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    "Preview.96 Hebrew Updater/Lab captions missing: " + profile.Name);

            foreach (Form f in forms) UiStyle.Apply(f, Style("English"));
            if (updater.RightToLeftLayout || lab.RightToLeftLayout ||
                updater.Text != "Updates — LockScreen Campaign Manager" ||
                lab.Text != "LockScreen Campaign Manager - Local lab evidence" ||
                labStatus.Text != "No report imported" ||
                !Descendants(updater).OfType<Label>().Any(x =>
                    x.Name == "UpdaterHeaderSubtitle" &&
                    x.Text == "1  Check for updates     2  Install update"))
                throw new InvalidOperationException(
                    "Preview.96 English restoration failed: " + profile.Name);
            if (channel.SelectedIndex != originalIndex ||
                !channel.Items.Cast<object>().Select(x => x.ToString() ?? "")
                    .SequenceEqual(items) ||
                !string.Equals(version.Text, originalVersion, StringComparison.Ordinal) ||
                !string.Equals(repository.Text, originalRepository, StringComparison.Ordinal) ||
                output.RightToLeft != RightToLeft.No ||
                version.RightToLeft != RightToLeft.No ||
                repository.RightToLeft != RightToLeft.No)
                throw new InvalidOperationException(
                    "Preview.96 translation mutated release selection, architecture or raw evidence.");
        }
    }

    private static void CheckRepairResultTranslations()
    {
        string[] templates =
        {
            "Repair attempted: {0} task success, {1} pending, {2} failed. Offline queued: {3}; health confirmation: {4}; uncertain tasks: {5}; failed: {6}. Check Client Health.",
            "WARNING: Remote Repair may have executed but queue could not be saved; inspect clients before retry.",
            "Repair request sent to {0} computers.\nSuccessful tasks: {1}\nPending: {2}\nFailures: {3}\n",
            "Deferred offline: {0}\nAwaiting client verification: {1}\nRequire manual review: {2}\n\n",
            "WARNING: result queue was not saved; inspect the PCs manually.\n\n",
            "Check fresh Client Health reports and actual Lock Screen application.",
            "Repair result",
            "Select a managed GPO in the first tab before previewing a remote task.",
            "Choose a managed GPO in the list first. Use Refresh GPOs to load policies managed by LockScreen Campaign Manager.",
        };
        foreach (string template in templates)
        foreach (string language in new[] { "Russian", "Hebrew" })
        {
            string translated = UiLocale.Translate(template, language);
            if (translated == template || UiLocale.Translate(translated, "English") != template)
                throw new InvalidOperationException("Repair result translation must round-trip: " + language);
            object[] counts = { 103, 207, 311, 419, 523, 631, 743 };
            string formatted = string.Format(translated, counts);
            for (int index = 0; index < counts.Length; index++)
            {
                string placeholder = "{" + index + "}";
                if (template.Contains(placeholder) != translated.Contains(placeholder) ||
                    (template.Contains(placeholder) && !formatted.Contains(counts[index].ToString()!)))
                    throw new InvalidOperationException("Repair result translation lost a counter: " + language);
            }
        }
    }

    private static void CheckPreview97GpoClientHealth()
    {
        // This is UI-only. Do not Show forms, click RPC/SMB/AD, confirm a GPO,
        // write a report or read a production machine. Translations are display-only.
        const string gpresultWarning =
            "Read the selected local gpresult XML? It can contain private computer, " +
            "domain, account, GPO and security settings. Only the current window will " +
            "receive parsed GPO IDs and flags, and the original file will NOT be copied " +
            "or uploaded. A user-supplied XML is NOT cryptographically authenticated.";
        const string closingWarning =
            "Export the visible closure assessment to a local CSV? It includes " +
            "computer FQDNs, the selected campaign and GPO IDs, and operational " +
            "information. Review it before sharing. A report is not proof of " +
            "endpoint cleanup.";
        foreach (var warning in new[] { gpresultWarning, closingWarning })
        {
            string ru = UiLocale.Translate(warning, "Russian");
            string he = UiLocale.Translate(warning, "Hebrew");
            if (ru == warning || he == warning ||
                UiLocale.Translate(ru, "English") != warning ||
                UiLocale.Translate(he, "English") != warning ||
                UiLocale.Translate(ru, "Hebrew") != he)
                throw new InvalidOperationException(
                    "Preview.97 safety consent must translate and reverse without loss.");
        }
        foreach (var profile in Profiles)
        {
            UiPreferences Style(string lang) => new() {
                Language = lang, Density = profile.Settings.Density,
                UiScalePercent = profile.Settings.UiScalePercent,
                FontScalePercent = profile.Settings.FontScalePercent
            };
            const string share = @"\\example.com\LockScreenReports$";
            using var host = new Form { Size = new Size(1250, 820),
                MinimumSize = new Size(920, 670) };
            var project = new CampaignProject {
                Name = "safe-rtl-ci", TimeZoneId = "UTC",
                FeedbackShare = share
            };
            var panel = new ClientFeedbackPanel(
                "example.com", "dc01.example.com",
                "OU=Computers,DC=example,DC=com",
                "CN=TestPCs,DC=example,DC=com",
                new DomainCredentials("example.com", "", ""), project);
            host.Controls.Add(panel);
            var health = Descendants(host).OfType<DataGridView>()
                .Single(x => x.Name == "ClientHealthGrid");
            var shareText = Descendants(host).OfType<TextBox>()
                .Single(x => x.Name == "ClientHealthSmbPath");
            var status = Descendants(host).OfType<Label>()
                .Single(x => x.Name == "ClientHealthStatus");
            var totals = Descendants(host).OfType<Label>()
                .Single(x => x.Name == "ClientHealthTotals");
            var originalColumns = health.Columns.Cast<DataGridViewColumn>()
                .Select(x => x.HeaderText).ToArray();
            var initialStatus = status.Text;
            var initialTotals = totals.Text;

            foreach (var lang in new[] { "Russian", "Hebrew", "English" })
            {
                UiStyle.Apply(host, Style(lang));
                host.PerformLayout();
                bool rtl = lang == "Hebrew";
                if (host.RightToLeftLayout != rtl ||
                    shareText.RightToLeft != RightToLeft.No ||
                    shareText.Text != share ||
                    health.Columns[1].HeaderText !=
                        UiLocale.Translate("Condition", lang) ||
                    status.Text != UiLocale.Translate(initialStatus, lang) ||
                    totals.Text != UiLocale.Translate(initialTotals, lang))
                    throw new InvalidOperationException(
                        "Preview.97 Client Health localized labels or LTR share damaged: " +
                        lang + "/" + profile.Name);
                if (health.Columns[1].HeaderText == "Condition" && lang != "English")
                    throw new InvalidOperationException(
                        "Preview.97 Client Health condition column untranslated.");
            }
            if (!health.Columns.Cast<DataGridViewColumn>()
                    .Select(x => x.HeaderText).SequenceEqual(originalColumns) ||
                status.Text != initialStatus ||
                totals.Text != initialTotals ||
                shareText.Text != share)
                throw new InvalidOperationException(
                    "Preview.97 language switching changed Client Health evidence.");
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        yield return parent;
        foreach (Control child in parent.Controls)
            foreach (Control descendant in Descendants(child))
                yield return descendant;
    }
}
