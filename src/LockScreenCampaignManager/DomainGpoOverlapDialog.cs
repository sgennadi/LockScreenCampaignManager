using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Distinct-source comparison of a configured setting across several domain
/// GPOs. A shared key/value is NOT proof of effective-policy collision.
/// Read-only, no remote calls or automatic remediation.
/// </summary>
internal sealed class DomainGpoOverlapDialog : Form
{
    private readonly IReadOnlyList<DomainGpoOverlap> _all;
    private DomainGpoOverlap[] _visible = [];
    private readonly TextBox _search = new() { Name = "GpoOverlapSearch" };
    private readonly ComboBox _kind = new() { Name = "GpoOverlapKindFilter" };
    private readonly DataGridView _grid = new() { Name = "GpoOverlapGrid" };
    private readonly TextBox _details = new() { Name = "GpoOverlapDetails" };
    private readonly Label _summary = new() { Name = "GpoOverlapSummary" };

    internal DomainGpoOverlapDialog(IReadOnlyList<DomainGpoOverlap> items)
    {
        _all = items ?? throw new ArgumentNullException(nameof(items));
        if (_all.Count > 6000)
            throw new InvalidDataException("Unbounded GPO source overlap report.");
        Text = "LockScreen Campaign Manager " + AppVersion.Current +
            " - " + UiLocale.Display("Compare GPO settings");
        Name = "DomainGpoOverlapDialog";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(1050, 685);
        Size = new Size(1500, 870);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiStyle.Canvas;

        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 145));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        Controls.Add(root);

        var head = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        head.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        head.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        head.Controls.Add(new Label {
            Text = UiLocale.Display("Same configured GPO settings — source comparison"),
            Dock = DockStyle.Fill, AutoEllipsis = false,
            Font = new Font("Segoe UI Semibold", 14),
            ForeColor = UiStyle.Navy,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        head.Controls.Add(new Label {
            Text = UiLocale.Display("Linked candidates may target different devices and users. No winning GPO or effective conflict is proven."),
            Dock = DockStyle.Fill, AutoEllipsis = false,
            ForeColor = UiStyle.Foreground,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        root.Controls.Add(head, 0, 0);

        var filters = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        _search.Dock = DockStyle.Fill;
        _search.RightToLeft = RightToLeft.No;
        _search.PlaceholderText =
            UiLocale.Display("Search source GPOs, setting, key or values...");
        _search.TextChanged += (_, _) => Draw();
        _kind.Dock = DockStyle.Fill;
        _kind.RightToLeft = RightToLeft.No;
        _kind.DropDownStyle = ComboBoxStyle.DropDownList;
        _kind.Items.AddRange(new object[] {
            new Choice("All overlaps"),
            new Choice("Different configured values"),
            new Choice("Ambiguous source values"),
            new Choice("Same configured value")
        });
        _kind.SelectedIndex = 0;
        _kind.SelectedIndexChanged += (_, _) => Draw();
        filters.Controls.Add(_search, 0, 0);
        filters.Controls.Add(_kind, 1, 0);
        root.Controls.Add(filters, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.RightToLeft = RightToLeft.No;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        foreach (var (caption, width) in new (string, int)[] {
            ("Comparison", 205), ("Scope", 90),
            ("Setting", 175), ("GPOs", 315), ("Values", 340),
            ("Linked candidates", 155), ("Registry key", 290)
        })
            _grid.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = UiLocale.Display(caption), Width = width,
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

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 355));
        _summary.Dock = DockStyle.Fill;
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        _summary.ForeColor = UiStyle.Muted;
        bottom.Controls.Add(_summary, 0, 0);
        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, AutoScroll = true
        };
        var close = Action("Close", 102);
        close.Click += (_, _) => Close();
        actions.Controls.Add(close);
        var export = Action("Export filtered CSV...", 190);
        export.Name = "GpoOverlapExport";
        export.Click += (_, _) => ExportCsv();
        actions.Controls.Add(export);
        bottom.Controls.Add(actions, 1, 0);
        root.Controls.Add(bottom, 0, 4);

        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "gpo-overlap-report");
        Draw();
    }

    private sealed record Choice(string Key) {
        public override string ToString() => UiLocale.Display(Key);
    }

    private static Button Action(string label, int width) => new() {
        Text = UiLocale.Display(label),
        MinimumSize = new Size(width, 38),
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly,
        FlatStyle = FlatStyle.Flat,
        BackColor = UiStyle.Surface,
        Margin = new Padding(4)
    };

    private void Draw()
    {
        string query = _search.Text.Trim();
        string kind = (_kind.SelectedItem as Choice)?.Key ?? "All overlaps";
        _visible = _all.Where(x =>
            (kind == "All overlaps" || x.Kind == kind) &&
            (query.Length == 0 || new[] {
                x.Scope, x.Key, x.Setting, x.Kind,
                x.Gpos, x.ConfiguredValues, x.Applicability
            }.Any(v => v.Contains(query, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            foreach (var item in _visible)
            {
                int row = _grid.Rows.Add(
                    UiLocale.Display(item.Kind), item.Scope, item.Setting,
                    item.Gpos, item.ConfiguredValues, item.LinkedCandidates,
                    item.Key);
                _grid.Rows[row].Tag = item;
                if (item.Kind == "Different configured values" &&
                    item.LinkedCandidates >= 2)
                    _grid.Rows[row].DefaultCellStyle.ForeColor = Color.Firebrick;
                else if (item.Kind == "Ambiguous source values")
                    _grid.Rows[row].DefaultCellStyle.BackColor =
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
        _summary.Text = _visible.Length + "/" + _all.Count +
            UiLocale.Display(" same-setting comparisons | NOT effective RSoP");
    }

    private void ShowDetails()
    {
        if (_grid.SelectedRows.Count == 0 ||
            _grid.SelectedRows[0].Tag is not DomainGpoOverlap item)
        {
            _details.Text = UiLocale.Display(
                "No overlap row selected. Zero overlaps does not prove GPO compatibility.");
            return;
        }
        _details.Text = "Comparison: " + UiLocale.Display(item.Kind) +
            Environment.NewLine + "Scope: " + item.Scope +
            " | Setting: " + item.Setting +
            Environment.NewLine + "Registry key: " + item.Key +
            Environment.NewLine + "GPOs: " + item.GpoCount +
            " | Linked somewhere in selected OUs: " + item.LinkedCandidates +
            Environment.NewLine + "Sources: " + item.Gpos +
            Environment.NewLine + "Configured values: " + item.ConfiguredValues +
            Environment.NewLine + "Applicability: " + item.Applicability +
            Environment.NewLine + UiLocale.Display(
                "Review user and computer gpresult before changing a GPO. No automatic winner or repair.");
    }

    private void ExportCsv()
    {
        if (MessageBox.Show(this, UiLocale.Display(
            "This report contains private GPO names, domain paths and policy values. " +
            "Save to a protected LOCAL folder only; do not upload raw CSV. Continue?"),
            UiLocale.Display("Private GPO report"), MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var picker = new SaveFileDialog {
            Title = UiLocale.Display("Export filtered read-only GPO candidates"),
            Filter = "CSV (*.csv)|*.csv", DefaultExt = "csv",
            AddExtension = true, OverwritePrompt = true,
            FileName = "All-DC-GPO-Same-Setting-Comparison.csv"
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        string path = Path.GetFullPath(picker.FileName);
        if (!Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\?\", StringComparison.Ordinal))
            throw new InvalidDataException("Choose a regular local folder for GPO report.");
        for (DirectoryInfo? dir = new(Path.GetDirectoryName(path)!);
             dir is not null && dir.Parent is not null; dir = dir.Parent)
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("A linked directory cannot receive the report.");
        if (File.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Cannot overwrite a linked GPO report.");
        File.WriteAllText(path, DomainGpoOverlapAnalysis.BuildCsv(_visible),
            new UTF8Encoding(true));
    }
}
