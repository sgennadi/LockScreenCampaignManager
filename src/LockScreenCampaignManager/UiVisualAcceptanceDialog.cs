using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Optional local, operator-driven visual acceptance. Unlike the hosted
/// simulated UI smoke, this reads the actual visible WinForms window DPI.
/// No network, GPO, LDAP, screenshot or telemetry functionality exists here.
/// An accepted report is UNSIGNED manual observation, not an attestation.
/// </summary>
internal sealed class UiVisualAcceptanceDialog : Form
{
    private const string AcceptedResult = "LOCAL_OPERATOR_OBSERVATIONS_RECORDED_NOT_ATTESTED";
    private const int RemoteSessionMetric = 0x1000;
    private readonly Label _display = new() { Name = "VisualAcceptanceDisplayStatus" };
    private readonly Label _smoke = new() { Name = "VisualAcceptanceSmokeStatus" };
    private readonly Label _result = new() { Name = "VisualAcceptanceResult" };
    private readonly Button _refresh = new() { Name = "VisualAcceptanceRefresh" };
    private readonly Button _runSmoke = new() { Name = "VisualAcceptanceRunSmoke" };
    private readonly Button _save = new() { Name = "VisualAcceptanceSave" };
    private readonly List<CheckBox> _operatorChecks = new();
    private bool _smokePassed;
    private bool _smokeRunning;
    private int _lastDpi;
    private bool _localConsole;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    internal static int ScalePercent(int dpi) =>
        dpi is >= 96 and <= 768 ? (int)Math.Round(dpi * 100.0 / 96.0) : 0;

    internal static bool Meets200PercentLocalConsole(int dpi, bool localConsole) =>
        dpi == 192 && localConsole;

    private static bool IsLocalConsole()
    {
        try
        {
            uint active = WTSGetActiveConsoleSessionId();
            return active != uint.MaxValue &&
                (uint)Process.GetCurrentProcess().SessionId == active &&
                !SystemInformation.TerminalServerSession &&
                GetSystemMetrics(RemoteSessionMetric) == 0;
        }
        catch { return false; }
    }

    internal UiVisualAcceptanceDialog()
    {
        Name = "UiVisualAcceptanceDialog";
        Text = "Visual DPI / language acceptance";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = UiStyle.Canvas;
        MinimumSize = new Size(650, 440);
        Size = new Size(880, 670);
        Build();
        UiStyle.Apply(this);

        // No handle is forced in the constructor or hosted CI layout tests.
        // On a physical monitor, GetDpiForWindow is re-read whenever the
        // window moves, changes DPI, or the operator requests a refresh.
        Shown += (_, _) => RefreshDisplay();
        DpiChanged += (_, _) => BeginInvoke(new Action(RefreshDisplay));
        LocationChanged += (_, _) =>
        {
            if (Visible && IsHandleCreated) RefreshDisplay();
        };
    }

    private void Build()
    {
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        Controls.Add(root);

        var viewport = new Panel { Name = "VisualAcceptanceScroll",
            Dock = DockStyle.Fill, AutoScroll = true };
        root.Controls.Add(viewport, 0, 0);
        var content = new TableLayoutPanel {
            Name = "VisualAcceptanceFields",
            Dock = DockStyle.Top, ColumnCount = 1,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 15)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        viewport.Controls.Add(content);

        void Add(Control control)
        {
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(control, 0, content.RowCount++);
        }

        Add(new Label {
            Name = "VisualAcceptanceTitle",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(780, 0),
            ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 13),
            Text = "200% physical display check"
        });
        Add(new Label {
            Name = "VisualAcceptancePrivacy",
            AutoSize = true, Dock = DockStyle.Top,
            MaximumSize = new Size(780, 0),
            ForeColor = UiStyle.Muted,
            Text = "Operator review only. No screenshots, network, AD or GPO changes."
        });
        _display.Dock = DockStyle.Top;
        _display.AutoSize = true;
        _display.MaximumSize = new Size(780, 0);
        _display.ForeColor = UiStyle.Navy;
        _display.Text = "Display DPI: not checked.";
        Add(_display);

        _smoke.Dock = DockStyle.Top;
        _smoke.AutoSize = true;
        _smoke.Text = "Offline UI smoke: not run.";
        _smoke.ForeColor = UiStyle.Navy;
        Add(_smoke);

        Add(new Label {
            Name = "VisualAcceptanceInstructions",
            AutoSize = true, Dock = DockStyle.Top,
            MaximumSize = new Size(780, 0),
            ForeColor = UiStyle.Navy,
            Text = "Keep this window open beside the Manager. Check each item only after inspecting the real 200% monitor."
        });

        string[] prompts = [
            "EN: Wizard, AD Deployment, Browse and Next buttons are fully visible.",
            "RU: Russian labels and Client Health rows are not clipped.",
            "HE: Hebrew RTL Wizard, Updater and buttons are not clipped.",
            "Mixed technical text (UNC, GUID, SHA-256, versions) remains LTR.",
            "Switching English / Russian / Hebrew / English preserves layout.",
            "I inspected the Manager on the 200% monitor, not an RDP window."
        ];
        for (int i = 0; i < prompts.Length; i++)
        {
            var box = new CheckBox {
                Name = "VisualAcceptanceCheck" + (i + 1),
                Text = prompts[i],
                AutoSize = false,
                Height = 64,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 5, 0, 5),
                ForeColor = UiStyle.Navy
            };
            box.CheckedChanged += (_, _) => UpdateActionState();
            _operatorChecks.Add(box);
            Add(box);
        }

        var footer = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        root.Controls.Add(footer, 0, 1);
        var commands = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, AutoScroll = true
        };
        footer.Controls.Add(commands, 0, 0);
        static Button MakeButton(Button button, string text, int width)
        {
            button.Text = text;
            button.Width = width;
            button.Height = 37;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowOnly;
            button.MinimumSize = new Size(width, 37);
            button.Margin = new Padding(4);
            return button;
        }
        commands.Controls.Add(MakeButton(_refresh, "Refresh display", 150));
        commands.Controls.Add(MakeButton(_runSmoke, "Run offline UI smoke", 185));
        commands.Controls.Add(MakeButton(_save, "Save local report", 168));
        var close = MakeButton(new Button { Name = "VisualAcceptanceClose" },
            "Close", 90);
        commands.Controls.Add(close);
        _refresh.Click += (_, _) => RefreshDisplay();
        _runSmoke.Click += async (_, _) => await RunSmokeAsync();
        _save.Click += (_, _) => SaveLocalReport();
        close.Click += (_, _) => Close();
        _result.Name = "VisualAcceptancePrivacyResult";
        _result.Dock = DockStyle.Fill;
        _result.Text = "No data sent.";
        _result.ForeColor = UiStyle.Muted;
        _result.AutoEllipsis = false;
        footer.Controls.Add(_result, 0, 1);
        UpdateActionState();
    }

    private void RefreshDisplay()
    {
        if (!IsHandleCreated || IsDisposed) return;
        try
        {
            _lastDpi = checked((int)GetDpiForWindow(Handle));
            _localConsole = IsLocalConsole();
        }
        catch
        {
            _lastDpi = 0;
            _localConsole = false;
        }

        if (Meets200PercentLocalConsole(_lastDpi, _localConsole))
        {
            _display.Text = UiLocale.Display(
                "Real 200% DPI detected on this window in the local console.") +
                "  " + _lastDpi + " DPI";
        }
        else
        {
            _display.Text = UiLocale.Display(
                "Not accepted: move this window onto a real 200% local-console monitor.") +
                "  " + _lastDpi + " DPI / " + ScalePercent(_lastDpi) + "%";
        }
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        _save.Enabled = !_smokeRunning && _smokePassed &&
            _operatorChecks.All(x => x.Checked) &&
            Meets200PercentLocalConsole(_lastDpi, _localConsole);
        _runSmoke.Enabled = !_smokeRunning;
    }

    private async Task RunSmokeAsync()
    {
        if (_smokeRunning) return;
        _smokeRunning = true;
        _smokePassed = false;
        _smoke.Text = UiLocale.Display("Offline UI smoke: running.");
        UpdateActionState();
        Process? process = null;
        try
        {
            string path = Environment.ProcessPath ??
                throw new InvalidOperationException("Current executable path unavailable.");
            var start = new ProcessStartInfo {
                FileName = path, UseShellExecute = false, CreateNoWindow = true
            };
            start.ArgumentList.Add("--ui-smoke-test");
            process = Process.Start(start) ??
                throw new InvalidOperationException("Offline UI smoke did not launch.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await process.WaitForExitAsync(timeout.Token);
            _smokePassed = process.ExitCode == 0;
        }
        catch
        {
            // A hosted or interrupted subprocess is never counted as a pass.
            _smokePassed = false;
            if (process is { HasExited: false })
            {
                try { process.Kill(entireProcessTree: true); }
                catch { /* The subprocess may already have exited. */ }
            }
        }
        finally
        {
            process?.Dispose();
            if (IsDisposed) return;
            _smoke.Text = UiLocale.Display(_smokePassed
                ? "Offline UI smoke: PASS (simulated layout only)."
                : "Offline UI smoke: FAILED or timed out.");
            _smokeRunning = false;
            RefreshDisplay();
        }
    }

    private void SaveLocalReport()
    {
        // Refresh and revalidate the ACTIVE monitor at the moment of writing:
        // a prior 200% reading must not authorize an off-monitor result.
        RefreshDisplay();
        if (!_save.Enabled) return;

        try
        {
            string folder = Path.Combine(AppLog.DirectoryPath, "VisualAcceptance");
            Directory.CreateDirectory(folder);
            var info = new DirectoryInfo(folder);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked report folder is not accepted.");

            var report = new {
                schema = "lscm-visual-acceptance/v1",
                observedUtc = DateTimeOffset.UtcNow.ToString("O"),
                version = AppVersion.Current,
                osBuild = Environment.OSVersion.Version.Build,
                windowDpi = _lastDpi,
                windowScalePercent = ScalePercent(_lastDpi),
                localConsole = _localConsole,
                offlineSimulatedSmokePassed = _smokePassed,
                operatorChecks = _operatorChecks.Select((check, i) =>
                    new { id = "visual-" + (i + 1), confirmed = check.Checked }).ToArray(),
                result = AcceptedResult,
                attested = false,
                screensCaptured = false,
                networkTransmitted = false,
                domainActionsPerformed = false
            };
            string path = Path.Combine(folder,
                "visual-" + Guid.NewGuid().ToString("N") + ".json");
            string payload = JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, payload, new UTF8Encoding(false));
            _result.Text = UiLocale.Display(
                "Saved locally. Unsigned operator evidence; no data sent.");
            MessageBox.Show(this, UiLocale.Display(
                "Report saved locally. This is not proof of actual GPO application."),
                UiLocale.Display("Visual DPI / language acceptance"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch
        {
            // Do not expose file contents, user names, or full local paths.
            _result.Text = UiLocale.Display(
                "Report not saved. Check local folder permissions.");
        }
    }
}
