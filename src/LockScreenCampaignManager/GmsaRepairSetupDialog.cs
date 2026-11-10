using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Fully native, DPI-aware installation and lifecycle dialog for gMSA Repair.
/// No .ps1 file is generated, launched or required by the application.
/// The AD gMSA must already exist and this host must be allowed to use it.
/// </summary>
internal sealed class GmsaRepairSetupDialog : Form
{
    private readonly string _domain;
    private readonly string _ouDn;
    private readonly string _groupDn;
    private readonly TextBox _account = new();
    private readonly TextBox _share = new();
    private readonly TextBox _output = new();
    private readonly Button _check = new();
    private readonly Button _install = new();
    private readonly Button _diagnose = new();
    private readonly Button _disable = new();
    private readonly Button _remove = new();
    private readonly Button _readiness = new();
    private readonly Button _close = new();
    private bool _working;
    // A single authoritative button list prevents a disabled diagnostic button
    // from remaining stuck after Check, Install, Disable or an exception.
    private Button[] OperationButtons =>
        new[] { _check, _diagnose, _install, _disable, _remove, _readiness, _close };

    internal GmsaRepairSetupDialog(string domain, string ouDn, string groupDn,
        string? feedbackShare)
    {
        _domain = domain;
        _ouDn = ouDn;
        _groupDn = groupDn;
        Text = "gMSA 24/7 — Setup";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(740, 620);
        Size = new Size(840, 700);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;

        // Disabled installations must remain inspectable and reusable. Never
        // silently substitute a default account after the admin clicks Disable.
        // Do not offer details from an installation for a different AD scope.
        var stored = GmsaRepairConfig.TryReadForInspection();
        bool sameScope = stored is not null &&
            string.Equals(stored.Domain, domain, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(stored.OuDn, ouDn, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(stored.GroupDn, groupDn, StringComparison.OrdinalIgnoreCase);
        _account.Text = sameScope ? stored!.GmsaAccount :
            domain.Split('.')[0].ToUpperInvariant() + @"\gmsaLSCM$";
        _share.Text = sameScope ? stored!.FeedbackShare : feedbackShare ?? "";

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9,
            Padding = new Padding(16), AutoScroll = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // Allow two rows of actions at enlarged Segoe UI sizes.
        // Scroll remains enabled for smaller windows / large system DPI.
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        Controls.Add(layout);

        var intro = new Label
        {
            Text = "Native setup without PowerShell or RSAT. The gMSA must already exist in AD " +
                "and this server must be authorized to retrieve its managed password. " +
                "Installation requires a local administrator. " +
                "After setup, processing continues after the administrator logs off Windows.",
            Dock = DockStyle.Fill, AutoSize = false
        };
        layout.Controls.Add(intro, 0, 0);
        layout.SetColumnSpan(intro, 2);

        AddInput(layout, 1, "gMSA account", _account, false);
        AddInput(layout, 2, "Domain", new TextBox { Text = _domain }, true);
        AddInput(layout, 3, "Computer OU", new TextBox { Text = _ouDn }, true);
        AddInput(layout, 4, "Computer group", new TextBox { Text = _groupDn }, true);
        AddInput(layout, 5, "SMB Client Health", _share, false);

        _account.RightToLeft = RightToLeft.No; // gMSA DOMAIN\account$ identifier
        _share.RightToLeft = RightToLeft.No;   // UNC source remains LTR
        _output.RightToLeft = RightToLeft.No; // raw diagnostic text remains LTR
        _output.Multiline = true;
        _output.ReadOnly = true;
        _output.ScrollBars = ScrollBars.Vertical;
        _output.Dock = DockStyle.Fill;
        _output.WordWrap = true;
        _output.Text = UiLocale.Display(
            "Click Check to verify the domain, administrator privileges, SID, " +
            "and local Netlogon gMSA status. This check does not run Repair.");
        layout.Controls.Add(_output, 0, 6);
        layout.SetColumnSpan(_output, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, AutoScroll = true
        };
        ConfigureButton(_check, "Check", 114);
        ConfigureButton(_diagnose, "Run gMSA test", 165);
        ConfigureButton(_install, "Install / Repair", 176);
        ConfigureButton(_disable, "Disable", 123);
        ConfigureButton(_remove, "Remove task", 154);
        ConfigureButton(_readiness, "Readiness", 144);
        ConfigureButton(_close, "Close", 107);
        actions.Controls.AddRange(new Control[] { _check, _diagnose, _install });
        layout.Controls.Add(actions, 0, 7);
        layout.SetColumnSpan(actions, 2);
        var lifecycle = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, AutoScroll = true
        };
        lifecycle.Controls.AddRange(new Control[] { _disable, _remove, _readiness, _close });
        layout.Controls.Add(lifecycle, 0, 8);
        layout.SetColumnSpan(lifecycle, 2);

        _check.Click += async (_, _) =>
        {
            var request = Input();
            await RunAsync(() => GmsaNativeSetup.Inspect(request), "Check");
        };
        _diagnose.Click += async (_, _) =>
        {
            var request = Input();
            await RunAsync(() => GmsaNativeSetup.RunDiagnostic(request), "gMSA read-only test");
        };
        _install.Click += async (_, _) =>
        {
            if (MessageBox.Show(this,
                UiLocale.Display(
                    "Install or repair the gMSA worker on THIS server?\n\n" +
                    "The EXE will be copied to protected ProgramData with restricted NTFS access. " +
                    "Task Scheduler will register this gMSA to run every two minutes.\n\n" +
                    "The manager does not create the AD gMSA, store a password, or run " +
                    "Repair before explicit Preview and GUID confirmation.\n\nContinue?"),
                UiLocale.Display("gMSA — Confirm installation"), MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            var request = Input();
            await RunAsync(() =>
            {
                var config = GmsaNativeSetup.Install(request);
                return UiLocale.Display("Installation completed. Account: ") + config.GmsaAccount +
                    "\nEXE: " + config.ExecutablePath +
                    "\n" + UiLocale.Display("Task Scheduler: ") + GmsaNativeSetup.TaskName +
                    "\n" + UiLocale.Display("Queue: ") + GmsaRepairConfig.QueueDirectory +
                    "\n" + UiLocale.Display("Verify the first run and AD/SMB permissions in a test OU.");
            }, "Installation");
        };
        _disable.Click += async (_, _) =>
        {
            if (MessageBox.Show(this, UiLocale.Display("Disable automatic gMSA processing?\n" +
                "The queue is retained. Tasks running on endpoints are not canceled."),
                UiLocale.Display("Disable gMSA"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await RunAsync(() => { GmsaNativeSetup.Disable(); return UiLocale.Display(
                "Background gMSA disabled. Queue retained."); },
                "Disabling");
        };
        _remove.Click += async (_, _) =>
        {
            if (MessageBox.Show(this,
                UiLocale.Display("Disable gMSA and REMOVE the Task Scheduler task?\n" +
                "Queue files are retained for audit. Already submitted remote tasks are unaffected."),
                UiLocale.Display("Remove gMSA task"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await RunAsync(() => { GmsaNativeSetup.RemoveTask(); return UiLocale.Display(
                "Task removed; configuration disabled."); },
                "Removal");
        };
        _readiness.Click += (_, _) =>
        {
            if (_working) return;
            using var dialog = new GmsaReadinessDialog(_domain, _ouDn, _groupDn);
            dialog.ShowDialog(this);
        };
        _close.Click += (_, _) => Close();
        FormClosing += (_, e) => { if (_working) e.Cancel = true; };
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "gmsa-setup");
    }

    private static void AddInput(TableLayoutPanel layout, int row, string name,
        TextBox input, bool readOnly)
    {
        layout.Controls.Add(new Label
        {
            Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, row);
        input.Dock = DockStyle.Fill;
        input.RightToLeft = RightToLeft.No; // technical AD / SID / gMSA / UNC data
        input.Margin = new Padding(4, 6, 4, 6);
        input.ReadOnly = readOnly;
        input.TabStop = !readOnly;
        layout.Controls.Add(input, 1, row);
    }

    private static void ConfigureButton(Button button, string caption, int width)
    {
        button.Text = UiLocale.Display(caption);
        button.Width = width;
        button.Height = 40;
        button.MinimumSize = new Size(width, 40);
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowOnly;
        button.AutoEllipsis = false;
        button.Margin = new Padding(2, 3, 5, 3);
    }

    private GmsaNativeSetup.Request Input() =>
        new(_account.Text.Trim(), _domain, _ouDn, _groupDn, _share.Text.Trim());

    private async Task RunAsync(Func<string> action, string operation)
    {
        if (_working) return;
        _working = true;
        foreach (var button in OperationButtons)
            button.Enabled = false;
        _output.Text = UiLocale.Display(operation) + UiLocale.Display(": checking…");
        UseWaitCursor = true;
        try
        {
            // Netlogon and ACL operations can block on DC/SMB. Keep the GUI
            // responsive without passing any Windows handles to the worker.
            string result = await Task.Run(action);
            _output.Text = result;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Native gMSA GUI: " + operation);
            _output.Text = UiLocale.Display(operation) + UiLocale.Display(" — error:\r\n") +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 1100) +
                "\r\n" + UiLocale.Display("Verify local administrator membership, AD connectivity, and the gMSA permissions on this server.");
        }
        finally
        {
            UseWaitCursor = false;
            _working = false;
            foreach (var button in OperationButtons)
                button.Enabled = true;
        }
    }
}
