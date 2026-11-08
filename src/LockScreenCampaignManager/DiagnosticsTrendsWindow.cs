using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Opt-in offline analysis of explicit UniversalDiagnostics captures. All
/// files remain in the administrator's LocalAppData; no networking or upload.
/// </summary>
internal sealed class DiagnosticsTrendsWindow : Form
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _filter = new();
    private readonly TextBox _detail = new();
    private readonly Label _summary = new();
    private readonly Button _refresh = new();
    private readonly Button _export = new();
    private DiagnosticsTrendSnapshot? _snapshot;
    private bool _running;

    internal DiagnosticsTrendsWindow()
    {
        Text = "UniversalDiagnostics — Local Error Trends";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 520);
        Size = new Size(1040, 720);

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(14),
            ColumnCount = 1, RowCount = 5, AutoScroll = true
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        Controls.Add(layout);

        _summary.Dock = DockStyle.Fill;
        _summary.Text = "Only explicitly saved, locally verified diagnostic reports are analyzed. " +
            "No network requests or automatic GitHub uploads.";
        _summary.ForeColor = UiStyle.Foreground;
        layout.Controls.Add(_summary, 0, 0);

        var tools = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false
        };
        tools.Controls.Add(new Label {
            Text = "Filter:", AutoSize = true, Margin = new Padding(0, 12, 8, 0)
        });
        _filter.PlaceholderText = "Component, error type, code...";
        _filter.Width = 290;
        _filter.TextChanged += (_, _) => ShowRows();
        tools.Controls.Add(_filter);
        tools.Controls.Add(new Label {
            Text = "Sorted by frequency; all times local. Reports are never sent.",
            AutoSize = true, ForeColor = UiStyle.Muted,
            Margin = new Padding(16, 12, 0, 0)
        });
        layout.Controls.Add(tools, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Count", FillWeight = 42
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Component", FillWeight = 125
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Error type", FillWeight = 130
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Code", FillWeight = 75
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Level", FillWeight = 60
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "First seen", FillWeight = 115
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Last seen", FillWeight = 115
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Last version", FillWeight = 100
        });
        _grid.SelectionChanged += (_, _) => ShowDetails();
        layout.Controls.Add(_grid, 0, 2);

        _detail.Dock = DockStyle.Fill;
        _detail.Multiline = true;
        _detail.ReadOnly = true;
        _detail.WordWrap = true;
        _detail.ScrollBars = ScrollBars.Vertical;
        _detail.Text = "Select a fingerprint group to inspect aggregate counts.";
        layout.Controls.Add(_detail, 0, 3);

        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false
        };
        Configure(_refresh, "Analyze local reports", 206);
        Configure(_export, "Export aggregate JSON", 207);
        var close = new Button { Text = "Close", Width = 112, Height = 37,
            Margin = new Padding(0, 4, 8, 4) };
        actions.Controls.AddRange(new Control[] { _refresh, _export, close });
        layout.Controls.Add(actions, 0, 4);
        _export.Enabled = false;

        _refresh.Click += async (_, _) => await ReloadAsync();
        _export.Click += (_, _) => Export();
        close.Click += (_, _) => Close();
        Shown += async (_, _) => await ReloadAsync();
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "diagnostics-trends");
    }

    private static void Configure(Button b, string text, int width)
    {
        b.Text = text;
        b.Width = width;
        b.Height = 37;
        b.AutoEllipsis = true;
        b.Margin = new Padding(0, 4, 8, 4);
    }

    private async Task ReloadAsync()
    {
        if (_running || IsDisposed) return;
        _running = true;
        _refresh.Enabled = _export.Enabled = false;
        _summary.Text = "Validating saved reports and counting repeated fingerprints...";
        UseWaitCursor = true;
        try
        {
            var snap = await Task.Run(() => DiagnosticsTrendAnalyzer.Analyze(
                UniversalDiagnosticsEmbedded.GetReports(), DateTimeOffset.UtcNow));
            if (IsDisposed) return;
            _snapshot = snap;
            _summary.Text =
                $"Locally saved: {snap.ValidReports} validated reports | " +
                $"Rejected / invalid: {snap.InvalidReports} | " +
                $"Unique errors: {snap.Groups.Count}" + Environment.NewLine +
                "Offline, operator-initiated analysis. Export omits summaries, paths, hostnames and report IDs.";
            ShowRows();
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _snapshot = null;
            _grid.Rows.Clear();
            _detail.Text = "No validated local analysis could be produced.";
            _summary.Text = "Analysis failed: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 170);
            AppLog.Write(ex, "Local UniversalDiagnostics trends");
        }
        finally
        {
            _running = false;
            if (!IsDisposed)
            {
                _refresh.Enabled = true;
                _export.Enabled = _snapshot is not null;
                UseWaitCursor = false;
            }
        }
    }

    private void ShowRows()
    {
        if (IsDisposed || _grid.IsDisposed) return;
        string query = _filter.Text.Trim();
        IEnumerable<DiagnosticsTrend> view = _snapshot?.Groups ??
            Array.Empty<DiagnosticsTrend>();
        if (query.Length != 0)
            view = view.Where(x =>
                x.Component.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.ErrorType.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Code.Contains(query, StringComparison.OrdinalIgnoreCase));
        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            foreach (var trend in view)
            {
                int n = _grid.Rows.Add(trend.Count, trend.Component,
                    trend.ErrorType, trend.Code, trend.Level,
                    trend.FirstUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
                    trend.LastUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
                    trend.LastVersion);
                _grid.Rows[n].Tag = trend;
                if (trend.Level == "error")
                    _grid.Rows[n].DefaultCellStyle.ForeColor = Color.DarkRed;
            }
            _grid.ClearSelection();
            _grid.CurrentCell = null;
        }
        finally { _grid.ResumeLayout(); }
        _detail.Text = "Select one group. The exported file never includes raw error text.";
    }

    private void ShowDetails()
    {
        if (_grid.SelectedRows.Count != 1 ||
            _grid.SelectedRows[0].Tag is not DiagnosticsTrend t)
            return;
        _detail.Text = "Fingerprint: " + t.Fingerprint + Environment.NewLine +
            "Component: " + t.Component + " | Error type: " + t.ErrorType +
            " | Code: " + t.Code + Environment.NewLine +
            "Severity: " + t.Level + " | Occurrences: " + t.Count +
            Environment.NewLine + "First: " +
            t.FirstUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") +
            " | Last: " + t.LastUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") +
            Environment.NewLine +
            "No user paths, device names, tokens, or raw exception summaries are exported. " +
            "This is a grouping of approved local reports, not all crashes on clients.";
    }

    private void Export()
    {
        if (_running || _snapshot is null) return;
        using var dialog = new SaveFileDialog {
            Title = "Export sanitized local error trends",
            Filter = "JSON (*.json)|*.json",
            FileName = "LSCM-Diagnostics-Trends-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName,
                _snapshot.ExportSafeJson(), new UTF8Encoding(false));
            _summary.Text = "Sanitized trends exported locally after explicit confirmation. " +
                "Nothing was uploaded to GitHub.";
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Export local diagnostic trends", ex); }
    }
}
