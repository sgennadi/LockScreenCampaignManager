using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

internal sealed class CampaignHistoryForm : Form
{
    private readonly DataGridView _table = new();
    private readonly TextBox _details = new();
    private readonly Label _status = new();
    private List<CampaignHistoryEntry> _entries = new();
    private bool _busy;

    public CampaignHistoryForm()
    {
        Text = "LockScreen Campaign Manager — Campaign history";
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(860, 590);
        Size = new Size(1200, 800);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(15), BackColor = UiStyle.Canvas };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "Campaign history and report archive", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 16f), ForeColor = UiStyle.Foreground }, 0, 0);
        _table.Dock = DockStyle.Fill;
        _table.ReadOnly = true;
        _table.AllowUserToAddRows = false;
        _table.AllowUserToDeleteRows = false;
        _table.RowHeadersVisible = false;
        _table.MultiSelect = false;
        _table.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _table.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Date", FillWeight = 92 });
        _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Campaign", FillWeight = 145 });
        _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", FillWeight = 65 });
        _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Description", FillWeight = 215 });
        _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Healthy / completed", FillWeight = 87 });
        _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Errors / no report", FillWeight = 100 });
        _table.SelectionChanged += (_, _) => UpdateSelection();
        root.Controls.Add(_table, 0, 1);
        _details.Dock = DockStyle.Fill;
        _details.Multiline = true;
        _details.ScrollBars = ScrollBars.Both;
        _details.ReadOnly = true;
        _details.WordWrap = true;
        _details.BackColor = UiStyle.Surface;
        root.Controls.Add(_details, 0, 2);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        actions.Controls.Add(Button("Refresh", () => _ = ReloadAsync(), 115));
        actions.Controls.Add(Button("Export HTML", () => Export("HTML"), 147));
        actions.Controls.Add(Button("Export CSV", () => Export("CSV"), 130));
        actions.Controls.Add(Button("Delete entry", DeleteEntry, 155));
        actions.Controls.Add(Button("Open folder", OpenFolder, 153));
        actions.Controls.Add(Button("Close", Close, 105));
        footer.Controls.Add(actions, 0, 0);
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = UiStyle.Muted;
        _status.Text = "History is stored in the administrator profile, not SYSVOL or GitHub.";
        footer.Controls.Add(_status, 0, 1);
        root.Controls.Add(footer, 0, 3);
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "campaign-history");
        Shown += async (_, _) => await ReloadAsync();
    }

    private static Button Button(string title, Action action, int width)
    {
        var button = new Button { Text = title, Width = width, Height = 38,
            Margin = new Padding(0, 4, 9, 6), BackColor = UiStyle.Surface,
            FlatStyle = FlatStyle.Flat };
        button.Click += (_, _) => action();
        return button;
    }

    private CampaignHistoryEntry? Selected => _table.SelectedRows.Count == 0 ? null :
        _table.SelectedRows[0].Index is int n && n >= 0 && n < _entries.Count ? _entries[n] : null;

    private void UpdateSelection()
    {
        var item = Selected;
        _details.Text = item is null ? "Select a record." :
            $"{item.OccurredUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}\r\n" +
            $"Campaign: {item.CampaignId}\r\nType: {item.Kind}\r\n{item.Summary}\r\n" +
            (item.Kind == "Event" ? "" :
             $"\r\nDomain: {item.Domain}\r\nOU: {item.OuDn}\r\nGroup: {item.GroupDn}\r\n" +
             $"Total: {item.Expected}; Healthy: {item.Healthy}; Completed: {item.Completed}; " +
             $"Errors: {item.Errors}; No report: {item.NoReport}; Stale: {item.Stale}; " +
             $"Invalid reports: {item.InvalidReports}\r\n" +
             "Status comes from client reports, not visual screen inspection.");
    }

    private async Task ReloadAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var records = await Task.Run(() => CampaignHistoryStore.LoadRecent());
            if (IsDisposed) return;
            _entries = records;
            _table.Rows.Clear();
            foreach (var item in records)
                _table.Rows.Add(item.OccurredUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                    item.CampaignId, item.Kind, item.Summary,
                    item.Kind == "Event" ? "—" : $"{item.Healthy}/{item.Completed}",
                    item.Kind == "Event" ? "—" : $"{item.Errors}/{item.NoReport}");
            if (_table.Rows.Count > 0)
            {
                _table.Rows[0].Selected = true;
                _table.CurrentCell = _table.Rows[0].Cells[0];
            }
            _status.Text = $"Loaded {records.Count} records; showing the latest 300. " +
                "A snapshot does not prove campaign completion everywhere.";
        }
        catch (Exception ex) { if (!IsDisposed) UiErrorDialog.Show(this, "Load campaign history", ex); }
        finally { _busy = false; }
    }

    private void Export(string type)
    {
        var item = Selected;
        if (item is null) return;
        if (item.Kind == "Event")
        {
            MessageBox.Show(this, "Select Snapshot or Final for report export. Events contain only short summaries.",
                "History", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string extension = type == "HTML" ? "html" : "csv";
        using var dialog = new SaveFileDialog {
            Title = "Save report", Filter = type == "HTML" ? "HTML (*.html)|*.html" : "CSV UTF-8 (*.csv)|*.csv",
            FileName = $"LSCM-{item.CampaignId}-{item.Kind}-{item.OccurredUtc:yyyyMMdd-HHmm}.{extension}"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string report = type == "HTML" ? CampaignHistoryStore.HtmlReport(item) : CampaignHistoryStore.CsvReport(item);
            File.WriteAllText(dialog.FileName, report, new UTF8Encoding(true));
            _status.Text = "Report saved: " + dialog.FileName;
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Export archived campaign report", ex); }
    }

    private void DeleteEntry()
    {
        var item = Selected;
        if (item is null) return;
        if (MessageBox.Show(this,
            $"Delete only the local entry for {item.OccurredUtc.ToLocalTime():dd.MM.yyyy HH:mm}?\n" +
            "GPO, SYSVOL, client data and other entries remain unchanged.",
            "Delete history entry", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try { CampaignHistoryStore.Delete(item); _ = ReloadAsync(); }
        catch (Exception ex) { UiErrorDialog.Show(this, "Delete local campaign archive", ex); }
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(CampaignHistoryStore.Root);
            Process.Start(new ProcessStartInfo { FileName = CampaignHistoryStore.Root, UseShellExecute = true });
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Open history folder", ex); }
    }
}
