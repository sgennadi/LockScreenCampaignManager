using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only view over already validated, authorized Device Dashboard timeline.
/// No queue mutation, RPC, AD queries or background collection.
/// </summary>
internal sealed class RepairHistoryDialog : Form
{
    private readonly IReadOnlyList<RepairHistoryEvent> _events;
    private readonly DataGridView _grid = new();
    private readonly ComboBox _source = new();
    private readonly TextBox _search = new();
    private readonly TextBox _details = new();
    private readonly Label _summary = new();
    private readonly List<RepairHistoryEvent> _visible = [];

    internal RepairHistoryDialog(IReadOnlyList<RepairHistoryEvent> events,
        string? selectedComputer = null)
    {
        _events = events.OrderByDescending(x => x.RecordedUtc).Take(500).ToArray();
        Text = "LockScreen Campaign Manager - Repair history";
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(850, 600);
        Size = new Size(1180, 790);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(16), BackColor = UiStyle.Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        Controls.Add(root);
        root.Controls.Add(new Label {
            Text = "Repair history (User + gMSA)", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 16f), ForeColor = UiStyle.Foreground
        }, 0, 0);
        var bar = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true
        };
        bar.Controls.Add(new Label {
            Text = "Source:", AutoSize = true, Margin = new Padding(2, 14, 6, 0)
        });
        _source.DropDownStyle = ComboBoxStyle.DropDownList;
        _source.Width = 118;
        _source.Items.AddRange(["All", "User", "gMSA"]);
        _source.SelectedIndex = 0;
        _source.SelectedIndexChanged += (_, _) => RefreshRows();
        bar.Controls.Add(_source);
        _search.PlaceholderText = "Filter computer or action...";
        _search.Width = 260;
        _search.Margin = new Padding(15, 7, 8, 0);
        _search.Text = selectedComputer ?? "";
        _search.TextChanged += (_, _) => RefreshRows();
        bar.Controls.Add(_search);
        root.Controls.Add(bar, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Time", FillWeight = 125 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Computer", FillWeight = 128 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Source", FillWeight = 58 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Action", FillWeight = 112 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "State", FillWeight = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "RPC #", FillWeight = 48 });
        _grid.SelectionChanged += (_, _) => ShowDetails();
        root.Controls.Add(_grid, 0, 2);
        _details.Dock = DockStyle.Fill;
        _details.ReadOnly = true;
        _details.Multiline = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.BackColor = UiStyle.Surface;
        _details.WordWrap = true;
        root.Controls.Add(_details, 0, 3);

        var footer = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false
        };
        actions.Controls.Add(MakeButton("Export visible CSV", ExportVisible, 170));
        actions.Controls.Add(MakeButton("Close", Close, 100));
        footer.Controls.Add(actions, 0, 0);
        _summary.Dock = DockStyle.Fill;
        _summary.AutoEllipsis = true;
        _summary.ForeColor = UiStyle.Muted;
        _summary.Text = "Read-only. Reports are agent/worker metadata, NOT proof of an image on screen.";
        footer.Controls.Add(_summary, 0, 1);
        root.Controls.Add(footer, 0, 4);
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "repair-history");
        RefreshRows();
    }

    private static Button MakeButton(string name, Action onClick, int width)
    {
        var button = new Button {
            Text = name, Width = width, Height = 38,
            Margin = new Padding(0, 4, 10, 4),
            BackColor = UiStyle.Surface, FlatStyle = FlatStyle.Flat
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private void RefreshRows()
    {
        string source = _source.SelectedItem as string ?? "All";
        string term = _search.Text.Trim();
        _visible.Clear();
        _visible.AddRange(_events.Where(e =>
            (source == "All" || e.Source == source) &&
            (term.Length == 0 ||
                e.ComputerFqdn.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                e.Action.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                e.State.Contains(term, StringComparison.OrdinalIgnoreCase))));
        _grid.Rows.Clear();
        foreach (var evt in _visible)
            _grid.Rows.Add(evt.RecordedUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                evt.ComputerFqdn, evt.Source, evt.Action, evt.State, evt.Attempts);
        if (_grid.Rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
            _grid.CurrentCell = _grid.Rows[0].Cells[0];
        }
        ShowDetails();
        _summary.Text = $"Showing {_visible.Count} of {_events.Count} recent events. " +
            "Journal is local/protected and may contain crash-related gaps; no automatic upload.";
    }

    private void ShowDetails()
    {
        if (_grid.SelectedRows.Count != 1 ||
            _grid.SelectedRows[0].Index >= _visible.Count)
        {
            _details.Text = "Select a recorded queue transition for more details.";
            return;
        }
        var e = _visible[_grid.SelectedRows[0].Index];
        _details.Text =
            $"Computer: {e.ComputerFqdn}\r\nSource: {e.Source}\r\n" +
            $"Recorded: {e.RecordedUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss}\r\n" +
            $"Action: {e.Action} | {e.PreviousState} -> {e.State}\r\n" +
            $"Recorded RPC attempts: {e.Attempts}\r\n" +
            $"Campaign: {e.CampaignId}\r\nGPO: {e.GpoId}\r\n" +
            $"Queue entry: {e.EntryId}\r\nReason: {e.Reason}\r\n" +
            "A removed tracking item does not prove that the endpoint has recovered.";
    }

    private static string Csv(string value)
    {
        string safe = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        // Excel may trim leading whitespace before interpreting a formula.
        string leadingTrimmed = safe.TrimStart();
        if (leadingTrimmed.Length > 0 && "=+-@".Contains(leadingTrimmed[0]))
            safe = "'" + safe;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }

    private void ExportVisible()
    {
        if (_visible.Count == 0) return;
        if (MessageBox.Show(this,
            "Export the visible Repair timeline to a local CSV? It contains " +
            "domain computer names, campaign IDs, GPO IDs and operational timestamps. " +
            "Review it before sharing outside your domain.",
            "Export private Repair history", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        using var save = new SaveFileDialog {
            Title = "Export Repair timeline (local CSV)",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            FileName = "LSCM-Repair-History-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv"
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var csv = new StringBuilder("RecordedUTC,Computer,Source,Action,PreviousState,State,Attempts,Campaign,GPO,QueueId,Reason\r\n");
            foreach (var e in _visible)
            {
                var cells = new[] {
                    e.RecordedUtc.ToUniversalTime().ToString("o"), e.ComputerFqdn,
                    e.Source, e.Action, e.PreviousState, e.State,
                    e.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    e.CampaignId, e.GpoId, e.EntryId, e.Reason
                };
                csv.AppendJoin(',', cells.Select(Csv)).Append("\r\n");
            }
            File.WriteAllText(save.FileName, csv.ToString(), new UTF8Encoding(true));
            _summary.Text = "Export saved locally: " + save.FileName;
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Export Repair history", ex); }
    }
}
