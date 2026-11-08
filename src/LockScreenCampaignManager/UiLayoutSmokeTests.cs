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
        ("AD first-step connect/read", () => new AdConnectionStartDialog(null),
            new[] { "Sign in & Read AD", "Next >", "Open campaign editor", "< Back",
                "Work offline (no AD)" }),
        ("Updater", () => new UniversalUpdaterWindow(),
            new[] { "1  Check for updates", "2  Download & verify", "3  Install & restart", "Backup status" }),
        ("gMSA readiness", () => new GmsaReadinessDialog(
            "example.com", "OU=Clients,DC=example,DC=com",
            "CN=Clients,OU=Groups,DC=example,DC=com"),
            new[] { "Run assessment", "Export safe JSON", "Close" }),
        ("Diagnostic trends", () => new DiagnosticsTrendsWindow(),
            new[] { "Analyze local reports", "Export aggregate JSON", "Close" }),
        ("Diagnostics", () => new DiagnosticsWindow(),
            new[] { "Refresh", "Error trends", "Close" }),
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
