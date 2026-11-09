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
        ("187.5% font simulation", new UiPreferences {
            Density = "Comfortable", UiScalePercent = 150, FontScalePercent = 125 })
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
        ("Campaign Wizard - images", () => CreateWizardPage(1),
            new[] { "Add image files...", "Choose image folder...", "Remove selected", "Next", "Back" }),
        ("Campaign Wizard - arrange", () => CreateWizardPage(2),
            new[] { "Move up", "Move down", "Sort by filename", "Auto match names", "Next", "Back" }),
        ("Campaign Wizard - schedule", () => CreateWizardPage(3),
            new[] { "Edit schedule...", "Next", "Back" }),
        ("Campaign Wizard - save", () => CreateWizardPage(4),
            new[] { "Validate campaign", "Save project...",
                "Skip saving & continue", "Save & Next", "Back" }),
        ("Campaign Wizard - deploy", () => CreateWizardPage(5),
            new[] { "Open GPO Deployment Wizard...", "Finish offline", "Finish", "Back" }),
        ("AD first-step connect/read", () => new AdConnectionStartDialog(null),
            new[] { "Sign in & Read AD", "Next >", "Open campaign editor", "< Back",
                "Work offline (no AD)" }),
        ("AD / GPO Deployment - dual client", () => new DomainDeploymentForm(
            new CampaignProject
            {
                Name = "Offline UI layout test",
                TimeZoneId = "UTC",
                StartLocal = new DateTime(2027, 1, 10, 8, 0, 0),
                ImagePaths = new List<string> { "test.jpg" }
            }), new[] { "Wizard", "Advanced", "Check GPO rights",
                "Preview / check conflicts", "Deploy GPO + files" }),
        ("Updater", () => new UniversalUpdaterWindow(),
            new[] { "1  Check", "2  Install", "Backup status" }),
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

    private static Form CreateEditorWithUpdateNotice()
    {
        var form = new MainForm();
        form.ShowSimulatedUpdateNoticeForLayoutTest();
        return form;
    }

    private static Form CreateWizardPage(int page)
    {
        // The editor stays invisible: no domain, GPO or SYSVOL access.
        // For image pages create one temporary PNG so the test exercises
        // actual decoding and the selection/preview binding, not just a
        // PictureBox that exists but renders nothing.
        string? fixture = null;
        if (page is 1 or 2)
        {
            fixture = Path.Combine(Path.GetTempPath(),
                "lscm-preview-smoke-" + Guid.NewGuid().ToString("N") + ".png");
            using var sample = new Bitmap(120, 68);
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
        if (page == 3)
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
        if (title.Parent is not TableLayoutPanel header ||
            header.Parent is not TableLayoutPanel outer ||
            header.RowStyles.Count < 4)
            throw new InvalidOperationException("Main editor header layout invalid.");
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
            int row = header.GetRow(label);
            if (header.RowStyles[row].Height < measured)
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
                CheckWizardFooter(form, profile.Name);
                CheckTimeZoneSelectors(form, profile.Name);
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
                }
                if (window.Name == "AD / GPO Deployment - dual client")
                {
                    var selector = Descendants(form).OfType<ComboBox>()
                        .SingleOrDefault(x => x.Name == "DeploymentClientType");
                    if (selector is null || selector.DropDownStyle != ComboBoxStyle.DropDownList ||
                        selector.Items.Count != 2 ||
                        selector.SelectedIndex != 0 ||
                        selector.Items[0]?.ToString()?.Contains(".ps1", StringComparison.Ordinal) != true ||
                        selector.Items[1]?.ToString()?.Contains(".exe", StringComparison.Ordinal) != true ||
                        selector.Width < 250 || selector.Height < selector.PreferredHeight)
                        throw new InvalidOperationException(
                            $"Deployment PS1/EXE client selector clipped or missing at {profile.Name}");
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
            "font profiles 100%, 125%, 150%, 187.5%; no dialog shown, no AD/gMSA/RPC or saved UI settings.");
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
