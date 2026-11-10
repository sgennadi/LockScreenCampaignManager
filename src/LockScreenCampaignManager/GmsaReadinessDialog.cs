using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Elevated read-only production readiness UI. Never starts the worker, repairs
/// computers, changes GPOs or writes the protected queue. The exported report
/// is an intentionally small, anonymized summary (no hostnames, SID, UNC or DN).
/// </summary>
internal sealed class GmsaReadinessDialog : Form
{
    private readonly string _domain;
    private readonly string _ouDn;
    private readonly string _groupDn;
    private readonly DataGridView _checks = new();
    private readonly Label _summary = new();
    private readonly Label _notice = new();
    private readonly Button _run = new();
    private readonly Button _export = new();
    private readonly Button _close = new();
    private ReadinessReport? _report;
    private bool _running;

    internal GmsaReadinessDialog(string domain, string ouDn, string groupDn)
    {
        _domain = domain;
        _ouDn = ouDn;
        _groupDn = groupDn;
        Text = "gMSA Production Readiness — read-only";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(740, 560);
        Size = new Size(990, 700);

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(15),
            RowCount = 5, ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 67));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 67));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        Controls.Add(layout);

        layout.Controls.Add(new Label {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 14.5f),
            ForeColor = UiStyle.Foreground,
            Text = UiLocale.Display("Production Readiness\n" +
                "Verify local installation, task, heartbeat, gMSA identity, SMB and queue.")
        }, 0, 0);

        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = UiStyle.Foreground;
        _summary.Text = UiLocale.Display(
            "No assessment yet. Only a real domain pilot can prove endpoint operation.");
        layout.Controls.Add(_summary, 0, 1);

        _checks.Name = "GmsaReadinessGrid";
        _checks.Dock = DockStyle.Fill;
        _checks.ReadOnly = true;
        _checks.AllowUserToAddRows = false;
        _checks.AllowUserToDeleteRows = false;
        _checks.RowHeadersVisible = false;
        _checks.MultiSelect = false;
        _checks.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _checks.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _checks.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Check", FillWeight = 100
        });
        _checks.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Result", FillWeight = 58
        });
        _checks.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Meaning / next action", FillWeight = 232
        });
        layout.Controls.Add(_checks, 0, 2);

        _notice.Dock = DockStyle.Fill;
        _notice.ForeColor = UiStyle.Muted;
        _notice.Text = UiLocale.Display("Read-only checks never execute client RPC. " +
            "A successful assessment does NOT prove that a lock-screen image " +
            "is visible or that restart recovery works in the real AD.");
        layout.Controls.Add(_notice, 0, 3);

        var buttons = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false
        };
        Configure(_run, "Run assessment", 186);
        Configure(_export, "Export safe JSON", 177);
        Configure(_close, "Close", 110);
        _export.Enabled = false;
        buttons.Controls.AddRange(new Control[] { _run, _export, _close });
        layout.Controls.Add(buttons, 0, 4);

        _run.Click += async (_, _) => await InspectAsync();
        _export.Click += (_, _) => Export();
        _close.Click += (_, _) => Close();
        FormClosing += (_, e) => { if (_running) e.Cancel = true; };
        Shown += async (_, _) => await InspectAsync();
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "gmsa-readiness");
    }

    private static void Configure(Button button, string label, int width)
    {
        button.Text = UiLocale.Display(label);
        button.Width = width;
        button.Height = 39;
        button.MinimumSize = new Size(width, 39);
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowOnly;
        button.AutoEllipsis = false;
        button.Margin = new Padding(2, 4, 8, 6);
    }

    private async Task InspectAsync()
    {
        if (_running || IsDisposed) return;
        _running = true;
        _run.Enabled = _export.Enabled = _close.Enabled = false;
        UseWaitCursor = true;
        _summary.Text = UiLocale.Display(
            "Checking the local worker host (no Repair, no task launch)...");
        _report = null;
        try
        {
            var report = await Task.Run(() =>
                GmsaProductionReadiness.Inspect(_domain, _ouDn, _groupDn));
            if (IsDisposed) return;
            _report = report;
            _checks.SuspendLayout();
            try
            {
                _checks.Rows.Clear();
                foreach (var item in report.Checks)
                {
                    int index = _checks.Rows.Add(item.Name, item.Result, item.Detail);
                    if (item.Result == "Blocked")
                        _checks.Rows[index].DefaultCellStyle.ForeColor = Color.DarkRed;
                    else if (item.Result == "Warning")
                        _checks.Rows[index].DefaultCellStyle.ForeColor = Color.DarkGoldenrod;
                    else if (item.Result == "Pass")
                        _checks.Rows[index].DefaultCellStyle.ForeColor = Color.DarkGreen;
                }
                _checks.ClearSelection();
                _checks.CurrentCell = null;
            }
            finally { _checks.ResumeLayout(); }
            _summary.Text = report.Conclusion + Environment.NewLine +
                UiLocale.Display("Queue: ") + report.QueueEntries +
                " | " + UiLocale.Display("Unconfirmed RPC: ") + report.UnconfirmedRpc +
                " | " + UiLocale.Display("Operator review: ") + report.NeedsAttention;
            _notice.Text = UiLocale.Display("Assessment ") +
                report.CheckedUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") +
                UiLocale.Display(". Export omits account, computer, domain, OU, SID, IP and UNC.") +
                Environment.NewLine + report.EndpointAcceptance;
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _checks.Rows.Clear();
            _summary.Text = UiLocale.Display(
                "Readiness assessment unavailable. This is NOT a successful check.");
            _notice.Text = UiLocale.Display("Local administrator rights are required. ") +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 180);
            AppLog.Write(ex, "gMSA Production Readiness");
        }
        finally
        {
            _running = false;
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                _run.Enabled = _close.Enabled = true;
                _export.Enabled = _report is not null;
            }
        }
    }

    private void Export()
    {
        if (_running || _report is null) return;
        // The report contains only standardized check text and aggregate
        // counts. Do not serialize raw gMSA configuration or queue entries.
        using var dialog = new SaveFileDialog {
            Filter = "JSON (*.json)|*.json",
            Title = UiLocale.Display("Export privacy-safe gMSA readiness report"),
            FileName = "LSCM-gMSA-Readiness-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, _report.ToJson(), new UTF8Encoding(false));
            _notice.Text = UiLocale.Display(
                "Export saved locally after your approval. No report was sent to GitHub or elsewhere.");
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Export readiness JSON", ex); }
    }
}
