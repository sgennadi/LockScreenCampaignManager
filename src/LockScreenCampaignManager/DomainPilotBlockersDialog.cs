using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only operator review of candidate GPO source settings for ONE pilot.
/// These are NOT confirmed winning values or known causes on the device.
/// </summary>
internal sealed class DomainPilotBlockersDialog : Form
{
    private readonly IReadOnlyList<DomainPilotBlocker> _all;
    private DomainPilotBlocker[] _visible = [];
    private readonly TextBox _search = new() { Name = "PilotGpoBlockersSearch" };
    private readonly ComboBox _scope = new() { Name = "PilotGpoBlockersScope" };
    private readonly ComboBox _priority = new() { Name = "PilotGpoBlockersPriority" };
    private readonly DataGridView _grid = new() { Name = "PilotGpoBlockersGrid" };
    private readonly TextBox _details = new() { Name = "PilotGpoBlockersDetails" };
    private readonly Label _count = new() { Name = "PilotGpoBlockersCount" };

    internal DomainPilotBlockersDialog(IReadOnlyList<DomainPilotBlocker> rows)
    {
        _all = rows ?? throw new ArgumentNullException(nameof(rows));
        if (rows.Count > 6000)
            throw new InvalidDataException("Unbounded GPO pilot blocker review.");
        Name = "DomainPilotBlockersDialog";
        Text = "LockScreen Campaign Manager " + AppVersion.Current +
            " - " + UiLocale.Display("Review candidate GPO blockers");
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1050, 710);
        Size = new Size(1520, 875);
        BackColor = UiStyle.Canvas;

        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 91));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 179));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        Controls.Add(root);

        var header = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label {
            Dock = DockStyle.Fill, AutoEllipsis = false,
            ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 14),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = UiLocale.Display("Named GPO blocker candidates — read-only")
        }, 0, 0);
        header.Controls.Add(new Label {
            Dock = DockStyle.Fill, AutoEllipsis = false,
            ForeColor = UiStyle.Foreground,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = UiLocale.Display("A reported-applied GPO is NOT proof that its setting won or blocks the actual screen.")
        }, 0, 1);
        root.Controls.Add(header, 0, 0);

        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        _search.Dock = DockStyle.Fill;
        _search.RightToLeft = RightToLeft.No;
        _search.PlaceholderText = UiLocale.Display("Search GPO, GUID, key, setting or value...");
        _search.TextChanged += (_, _) => Draw();
        filters.Controls.Add(_search, 0, 0);
        Configure(_scope, ["All scopes", "Computer", "User"]);
        Configure(_priority, ["All priorities", "Review first (GPO reported applied)",
            "Unverified applicability", "Historical gpresult",
            "Historical GPO source", "Reported filtered"]);
        filters.Controls.Add(_scope, 1, 0);
        filters.Controls.Add(_priority, 2, 0);
        root.Controls.Add(filters, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.RightToLeft = RightToLeft.No;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.AutoGenerateColumns = false;
        foreach (var (label, width) in new (string, int)[] {
            ("Review priority", 260), ("Risk", 96), ("Scope", 90),
            ("GPO", 240), ("GPO GUID", 275), ("Setting", 170),
            ("Value", 185), ("Reported RSoP", 180), ("Source", 175),
            ("Registry key", 310)
        })
            _grid.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = UiLocale.Display(label),
                Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        _grid.SelectionChanged += (_, _) => ShowDetails();
        root.Controls.Add(_grid, 0, 2);

        _details.Dock = DockStyle.Fill;
        _details.ReadOnly = true;
        _details.Multiline = true;
        _details.ScrollBars = ScrollBars.Both;
        _details.WordWrap = true;
        _details.RightToLeft = RightToLeft.No;
        _details.Font = new Font("Consolas", 9.2f);
        _details.BackColor = UiStyle.Surface;
        root.Controls.Add(_details, 0, 3);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 370));
        _count.Dock = DockStyle.Fill;
        _count.TextAlign = ContentAlignment.MiddleLeft;
        _count.ForeColor = UiStyle.Muted;
        footer.Controls.Add(_count, 0, 0);
        var buttons = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            AutoScroll = true, WrapContents = false
        };
        var close = MakeButton("Close", 103);
        close.Click += (_, _) => Close();
        buttons.Controls.Add(close);
        var export = MakeButton("Export filtered CSV...", 225);
        export.Name = "PilotGpoBlockersExport";
        export.Click += (_, _) => Export();
        buttons.Controls.Add(export);
        footer.Controls.Add(buttons, 1, 0);
        root.Controls.Add(footer, 0, 4);

        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "named-gpo-pilot-blockers");
        Draw();
    }

    private sealed record Choice(string Key)
    {
        public override string ToString() => UiLocale.Display(Key);
    }

    private static Button MakeButton(string label, int width) => new() {
        Text = UiLocale.Display(label),
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly,
        MinimumSize = new Size(width, 36),
        FlatStyle = FlatStyle.Flat, BackColor = UiStyle.Surface,
        Margin = new Padding(4)
    };

    private void Configure(ComboBox control, string[] choices)
    {
        control.Dock = DockStyle.Fill;
        control.RightToLeft = RightToLeft.No;
        control.DropDownStyle = ComboBoxStyle.DropDownList;
        control.Items.AddRange(choices.Select(x => (object)new Choice(x)).ToArray());
        control.SelectedIndex = 0;
        control.SelectedIndexChanged += (_, _) => Draw();
    }

    private void Draw()
    {
        string query = _search.Text.Trim();
        string scope = (_scope.SelectedItem as Choice)?.Key ?? "All scopes";
        string priority = (_priority.SelectedItem as Choice)?.Key ?? "All priorities";
        _visible = _all.Where(row =>
                (scope == "All scopes" || row.Scope == scope) &&
                (priority == "All priorities" || row.ReviewState == priority) &&
                (query.Length == 0 || new[] {row.Gpo, row.GpoId, row.RegistryKey,
                    row.Setting, row.Value, row.Source, row.ReportedRsop}
                    .Any(x => x.Contains(query, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            foreach (var row in _visible)
            {
                int index = _grid.Rows.Add(UiLocale.Display(row.ReviewState),
                    row.Risk, row.Scope, row.Gpo, row.GpoId, row.Setting,
                    row.Value, UiLocale.Display(row.ReportedRsop),
                    row.Source, row.RegistryKey);
                _grid.Rows[index].Tag = row;
                if (row.ReviewState == "Review first (GPO reported applied)")
                    _grid.Rows[index].DefaultCellStyle.ForeColor = Color.Firebrick;
                else if (row.ReviewState is "Unverified applicability" or
                    "Historical gpresult" or "Historical GPO source")
                    _grid.Rows[index].DefaultCellStyle.BackColor =
                        Color.FromArgb(255, 244, 217);
            }
        }
        finally { _grid.ResumeLayout(); }
        _grid.ClearSelection();
        if (_grid.Rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
            _grid.CurrentCell = _grid.Rows[0].Cells[0];
        }
        ShowDetails();
        _count.Text = _visible.Length + "/" + _all.Count +
            UiLocale.Display(" named source candidates | NOT winning RSoP");
    }

    private void ShowDetails()
    {
        if (_grid.SelectedRows.Count == 0 ||
            _grid.SelectedRows[0].Tag is not DomainPilotBlocker item)
        {
            _details.Text = UiLocale.Display(
                "No GPO candidate selected. No matches is not proof of compatibility.");
            return;
        }
        _details.Text =
            "GPO: " + item.Gpo + " {" + item.GpoId + "}" +
            Environment.NewLine + "Scope: " + item.Scope +
            " | Review priority: " + item.ReviewState +
            Environment.NewLine + "Registry: " + item.RegistryKey +
            " / " + item.Setting + " = " + item.Value +
            Environment.NewLine + "Configured source: " + item.Source +
            " | Linked: " + item.LinkStatus +
            Environment.NewLine + "Source risk: " + item.Risk +
            " | Imported gpresult: " + item.ReportedRsop +
            Environment.NewLine + item.Interpretation +
            Environment.NewLine +
            UiLocale.Display("Check actual per-setting RSoP, interactive HKCU and screen behavior before modifying any GPO.");
    }

    private void Export()
    {
        if (MessageBox.Show(this, UiLocale.Display(
            "The file contains private domain GPO names, GUIDs, policy paths and values. " +
            "Save to a protected LOCAL folder only. Continue?"),
            UiLocale.Display("Private GPO report"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        using var picker = new SaveFileDialog {
            Title = UiLocale.Display("Export filtered named GPO candidates"),
            Filter = "CSV (*.csv)|*.csv",
            DefaultExt = "csv", AddExtension = true, OverwritePrompt = true,
            FileName = "Pilot-GPO-Blocker-Candidates.csv"
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            DomainPilotBlockerEvidence.ExportLocalCsv(picker.FileName, _visible);
        }
        catch (Exception ex)
        {
            UiErrorDialog.Show(this, "Export pilot GPO candidates", ex);
        }
    }
}
