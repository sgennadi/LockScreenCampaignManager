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
            new[] { "Open GPO Deployment Wizard...", "Finish offline", "Finish", "Back" }),
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
                "Run on selected" }),
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
        if (all.OfType<ClientFeedbackPanel>().Any() &&
            !all.OfType<TextBox>().Any(x =>
                x.Name == "ClientHealthSmbPath" && x.Multiline &&
                !x.WordWrap && x.ScrollBars == ScrollBars.Horizontal))
            throw new InvalidOperationException(
                $"Client Health SMB share cannot be scrolled at {profile}.");
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
                if (window.Name == "Campaign assignment matrix")
                {
                    var all = Descendants(form).ToArray();
                    var modes = all.OfType<ComboBox>()
                        .SingleOrDefault(x => x.Name == "AssignmentTargetMode");
                    var content = all.OfType<TableLayoutPanel>()
                        .SingleOrDefault(x => x.Name == "AssignmentScrollableFields");
                    var preview = all.OfType<TextBox>()
                        .SingleOrDefault(x => x.Name == "AssignmentReadOnlyPreview");
                    if (modes is null || modes.Items.Count != 3 ||
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
        Console.WriteLine($"PASS: {forms} offline WinForms dialog layouts and {buttons} button checks; " +
            "font profiles 100%, 125%, 150%, 175%, 187.5%, 200%; no dialog shown, no AD/gMSA/RPC or saved UI settings.");
        Console.WriteLine("NOTE: actual hardware DPI 200% and visual text-overflow inspection require an interactive VM.");
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        yield return parent;
        foreach (Control child in parent.Controls)
            foreach (Control descendant in Descendants(child))
                yield return descendant;
    }
}
