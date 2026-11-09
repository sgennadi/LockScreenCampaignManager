using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

// Immutable snapshots from the selected DC. Not effective RSoP.
internal sealed record GroupConflictFinding(
    string Target, string Scope, string Gpo, string GpoId, string Risk,
    string Setting, string Value, string Source, string Key,
    string LinkedAt, string Applicability);

internal sealed record GroupConflictTarget(
    string Target, string Scope, string OuDn, string GroupDn,
    int HighRisk, int Warnings, IReadOnlyList<GroupConflictFinding> Findings,
    IReadOnlyList<string> Notes);

/// <summary>
/// Full, searchable, read-only group GPO candidate report. Unlike the compact
/// deployment Preview this shows EVERY scanner match (up to 500 per target).
/// Export is strictly local, opt-in and CSV-formula-safe; never auto-publishes
/// domain paths, group names or policy settings to GitHub.
/// </summary>
internal sealed class GroupConflictReportDialog : Form
{
    private readonly IReadOnlyList<GroupConflictTarget> _targets;
    private readonly GroupConflictFinding[] _all;
    private readonly List<GroupConflictFinding> _visible = [];
    private readonly TextBox _search = new() { Name = "GroupConflictSearch" };
    private readonly ComboBox _risk = new() { Name = "GroupConflictRisk" };
    private readonly DataGridView _grid = new() { Name = "GroupConflictGrid" };
    private readonly TextBox _details = new() { Name = "GroupConflictDetails" };
    private readonly Label _status = new() { Name = "GroupConflictCount" };

    internal GroupConflictReportDialog(IReadOnlyList<GroupConflictTarget> targets)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        if (targets.Count is < 1 or > 40 ||
            targets.Sum(t => t.Findings.Count) > 20000)
            throw new InvalidDataException("Unexpected group GPO report size.");
        _all = targets.SelectMany(t => t.Findings).ToArray();
        Text = "LockScreen Campaign Manager " + AppVersion.Current +
            " - Full GPO conflict report (read-only)";
        Name = "GroupConflictReportDialog";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(990, 660);
        Size = new Size(1410, 880);

        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(14), BackColor = UiStyle.Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        Controls.Add(root);

        var head = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        head.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        head.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        head.Controls.Add(new Label {
            Text = "Complete static GPO candidates - NOT effective RSoP",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 12),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
        }, 0, 0);
        var filters = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        _risk.Dock = DockStyle.Fill;
        _risk.DropDownStyle = ComboBoxStyle.DropDownList;
        _risk.Items.AddRange(new object[] {
            "All", "High", "Warning", "Info", "Inactive/filtered"
        });
        _risk.SelectedIndex = 0;
        _risk.SelectedIndexChanged += (_, _) => Draw();
        _search.Dock = DockStyle.Fill;
        _search.PlaceholderText = "Search group, OU, policy, setting, value...";
        _search.TextChanged += (_, _) => Draw();
        filters.Controls.Add(_risk, 0, 0);
        filters.Controls.Add(_search, 1, 0);
        filters.Controls.Add(new Label {
            Text = "Read-only | all targets | local export",
            Dock = DockStyle.Fill, AutoEllipsis = true,
            ForeColor = UiStyle.Muted, TextAlign = ContentAlignment.MiddleRight
        }, 2, 0);
        head.Controls.Add(filters, 0, 1);
        root.Controls.Add(head, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Target", Width = 165 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Scope", Width = 92 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Risk", Width = 114 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "GPO", Width = 220 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Setting", Width = 180 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", Width = 210 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Source", Width = 135 });
        _grid.SelectionChanged += (_, _) => ShowDetails();
        root.Controls.Add(_grid, 0, 1);

        _details.Dock = DockStyle.Fill;
        _details.ReadOnly = true;
        _details.Multiline = true;
        _details.ScrollBars = ScrollBars.Both;
        _details.WordWrap = false;
        _details.Font = new Font("Consolas", 9.5f);
        root.Controls.Add(_details, 0, 2);

        var footer = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 325));
        _status.Dock = DockStyle.Fill;
        _status.AutoEllipsis = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = UiStyle.Muted;
        footer.Controls.Add(_status, 0, 0);
        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var close = MakeButton("Close", 100);
        close.Click += (_, _) => Close();
        actions.Controls.Add(close);
        var export = MakeButton("Export filtered CSV...", 199);
        export.Name = "GroupConflictExportCsv";
        export.Click += (_, _) => ExportCsv();
        actions.Controls.Add(export);
        footer.Controls.Add(actions, 1, 0);
        root.Controls.Add(footer, 0, 3);

        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "group-conflict-report");
        Draw();
    }

    private static Button MakeButton(string label, int width) => new() {
        Text = label, Width = width, Height = 40,
        FlatStyle = FlatStyle.Flat, BackColor = UiStyle.Surface,
        Margin = new Padding(4, 4, 0, 4)
    };

    private void Draw()
    {
        string query = _search.Text.Trim();
        string selectedRisk = _risk.SelectedItem as string ?? "All";
        _visible.Clear();
        _visible.AddRange(_all.Where(x =>
            (selectedRisk == "All" || x.Risk == selectedRisk) &&
            (query.Length == 0 || new[] {
                x.Target, x.Scope, x.Gpo, x.GpoId, x.Risk,
                x.Setting, x.Value, x.Source, x.Key, x.LinkedAt,
                x.Applicability
            }.Any(part => part.Contains(query, StringComparison.OrdinalIgnoreCase)))));
        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            foreach (var x in _visible)
            {
                int row = _grid.Rows.Add(x.Target, x.Scope, x.Risk,
                    x.Gpo, x.Setting, x.Value, x.Source);
                _grid.Rows[row].Tag = x;
                if (x.Risk == "High")
                    _grid.Rows[row].DefaultCellStyle.ForeColor = Color.DarkRed;
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
        _status.Text = _visible.Count + "/" + _all.Length +
            " findings | " + _targets.Count + " GPO targets | " +
            _targets.Sum(x => x.HighRisk) + " high-risk | " +
            _targets.Sum(x => x.Warnings) + " warnings | NOT RSoP";
    }

    private void ShowDetails()
    {
        if (_grid.SelectedRows.Count == 0 ||
            _grid.SelectedRows[0].Tag is not GroupConflictFinding entry)
        {
            _details.Text = "No row selected. Select a finding, or clear filters.\r\n" +
                "No static hits never proves that the GPO wins or has no conflict.\r\n" +
                string.Join("\r\n", _targets.Select(t =>
                    t.Target + " | " + t.Scope + " | " +
                    t.HighRisk + " high / " + t.Warnings + " warning\r\n" +
                    "OU: " + t.OuDn + "\r\nGroup: " + t.GroupDn + "\r\n" +
                    string.Join("\r\n", t.Notes.Select(n => "COVERAGE: " + n)))));
            return;
        }
        var target = _targets.First(t =>
            t.Target == entry.Target && t.Scope == entry.Scope);
        _details.Text =
            "Target: " + entry.Target + " | " + entry.Scope + "\r\n" +
            "OU: " + target.OuDn + "\r\n" +
            "Group: " + target.GroupDn + "\r\n" +
            "GPO: " + entry.Gpo + " {" + entry.GpoId + "}\r\n" +
            "Risk: " + entry.Risk + " | Setting: " + entry.Setting + "\r\n" +
            "Value: " + entry.Value + "\r\nRegistry key: " + entry.Key +
            "\r\nSource: " + entry.Source + "\r\nLinked at: " + entry.LinkedAt +
            "\r\nApplicability: " + entry.Applicability + "\r\n" +
            string.Join("\r\n", target.Notes.Select(x => "COVERAGE: " + x)) +
            "\r\nNot effective client Resultant Set of Policy.";
    }

    private void ExportCsv()
    {
        if (MessageBox.Show(this,
            "This report may contain private domain, OU, group and GPO values. " +
            "Export only to a protected LOCAL folder. Never post raw CSV to a public issue.\r\n\r\nContinue?",
            "Review private GPO report export", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var choose = new SaveFileDialog {
            Title = "Export filtered read-only GPO candidates",
            Filter = "CSV (*.csv)|*.csv",
            DefaultExt = "csv",
            AddExtension = true,
            FileName = "GPO-Conflict-Candidates.csv",
            OverwritePrompt = true
        };
        if (choose.ShowDialog(this) != DialogResult.OK) return;
        string full = Path.GetFullPath(choose.FileName);
        if (!Path.IsPathFullyQualified(full) ||
            full.StartsWith(@"\\", StringComparison.Ordinal) ||
            full.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "Choose a regular local folder, not a UNC/device path.");
            return;
        }
        for (var folder = new DirectoryInfo(Path.GetDirectoryName(full)!);
             folder is not null && folder.Parent is not null; folder = folder.Parent)
            if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked folder is not accepted for GPO CSV export.");
        if (File.Exists(full) &&
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Cannot overwrite a linked report file.");
        File.WriteAllText(full, BuildCsv(_visible), new UTF8Encoding(true));
    }

    internal static string BuildCsv(IEnumerable<GroupConflictFinding> findings)
    {
        static string Esc(string? value)
        {
            string s = (value ?? "").Replace("\r", " ").Replace("\n", " ");
            // Excel formula injection protection applies even after whitespace.
            if (s.TrimStart().Length > 0 &&
                "=+-@\t".Contains(s.TrimStart()[0]))
                s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var text = new StringBuilder(
            "Target,Scope,Risk,GPO,GPO GUID,Setting,Value,Registry key,Source,Linked at,Applicability\r\n");
        foreach (var f in findings)
            text.AppendLine(string.Join(",", new[] {
                f.Target, f.Scope, f.Risk, f.Gpo, f.GpoId, f.Setting,
                f.Value, f.Key, f.Source, f.LinkedAt, f.Applicability
            }.Select(Esc)));
        return text.ToString();
    }
}
