using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only inventory of all domain GPO source candidates from the selected
/// DC. The linked state is preliminary; no row claims winning RSoP or proof
/// that a user/device receives the policy.
/// </summary>
internal sealed class DomainWideGpoReportDialog : Form
{
    private readonly DomainWideGpoReport _report;
    private readonly DataGridView _grid = new() { Name = "AllDcGpoFindingsGrid" };
    private readonly TextBox _details = new() { Name = "AllDcGpoDetails" };
    private readonly TextBox _search = new() { Name = "AllDcGpoSearch" };
    private readonly ComboBox _risk = new() { Name = "AllDcGpoRiskFilter" };
    private readonly ComboBox _scope = new() { Name = "AllDcGpoScopeFilter" };
    private readonly ComboBox _link = new() { Name = "AllDcGpoLinkFilter" };
    private readonly Label _count = new() { Name = "AllDcGpoCount" };
    private DomainGpoMatch[] _visible = [];

    internal DomainWideGpoReportDialog(DomainWideGpoReport report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        Text = "LockScreen Campaign Manager " + AppVersion.Current +
            " - All DC GPO conflict audit";
        Name = "DomainWideGpoReportDialog";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(1050, 700);
        Size = new Size(1510, 910);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiStyle.Canvas;

        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var headline = new Label {
            Text = UiLocale.Display("All domain GPOs on selected DC — read-only"),
            Dock = DockStyle.Fill, AutoEllipsis = false,
            ForeColor = UiStyle.Navy, Font = new Font("Segoe UI Semibold", 14),
            TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(headline, 0, 0);
        header.Controls.Add(new Label {
            Name = "AllDcGpoScopeLabel", RightToLeft = RightToLeft.No,
            Dock = DockStyle.Fill, AutoEllipsis = false,
            ForeColor = UiStyle.Foreground, TextAlign = ContentAlignment.MiddleLeft,
            Text = "DC: " + report.Dc + "  |  GPOs: " + report.GpoCount +
                "  |  Known-setting hits: " + report.Count +
                "  |  Linked candidates: " + report.LinkedCandidates +
                "  |  High: " + report.HighRisk +
                "  |  Warnings: " + report.Warnings +
                "  |  Read failures: " + report.Issues.Count +
                Environment.NewLine +
                UiLocale.Display("Static GPO source evidence; not winning RSoP, GPP/ACL/WMI applicability or endpoint behavior.")
        }, 0, 1);
        root.Controls.Add(header, 0, 0);

        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        _search.Dock = DockStyle.Fill;
        _search.PlaceholderText = UiLocale.Display("Search GPO, setting, registry key, value, link...");
        _search.RightToLeft = RightToLeft.No;
        _search.TextChanged += (_, _) => Draw();
        filters.Controls.Add(_search, 0, 0);
        Configure(_scope, ["All scopes", "Computer", "User"]);
        Configure(_risk, ["All risks", "High", "Warning", "Info", "Inactive/filtered"]);
        Configure(_link, ["All links", "Linked candidate", "Outside selected OUs",
            "Inactive/filtered", "User OU not verified"]);
        filters.Controls.Add(_scope, 1, 0);
        filters.Controls.Add(_risk, 2, 0);
        filters.Controls.Add(_link, 3, 0);
        root.Controls.Add(filters, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.RightToLeft = RightToLeft.No;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var (name,width) in new (string,int)[] {
            ("Risk", 105), ("Link status", 165), ("Scope", 88), ("GPO", 220),
            ("Setting", 160), ("Value", 175), ("Source", 190),
            ("Linked at", 285), ("Key", 260)
        })
            _grid.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = UiLocale.Display(name), Width = width,
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
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
        _count.Dock = DockStyle.Fill;
        _count.TextAlign = ContentAlignment.MiddleLeft;
        _count.ForeColor = UiStyle.Muted;
        footer.Controls.Add(_count, 0, 0);
        var buttons = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, AutoScroll = true
        };
        var close = MakeButton("Close", 110);
        close.Click += (_, _) => Close();
        buttons.Controls.Add(close);
        var export = MakeButton("Export filtered CSV...", 200);
        export.Name = "AllDcGpoExport";
        export.Click += (_, _) => ExportCsv();
        buttons.Controls.Add(export);
        footer.Controls.Add(buttons, 1, 0);
        root.Controls.Add(footer, 0, 4);
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "all-dc-gpo-audit");
        Draw();
    }

    private static Button MakeButton(string caption, int width) => new() {
        Text = UiLocale.Display(caption), MinimumSize = new Size(width, 38),
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly,
        FlatStyle = FlatStyle.Flat, BackColor = UiStyle.Surface,
        Margin = new Padding(4)
    };

    private void Configure(ComboBox box, IEnumerable<string> items)
    {
        box.Dock = DockStyle.Fill;
        box.RightToLeft = RightToLeft.No;
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Items.AddRange(items.ToArray());
        box.SelectedIndex = 0;
        box.SelectedIndexChanged += (_, _) => Draw();
    }

    private void Draw()
    {
        string query = _search.Text.Trim();
        string scope = _scope.SelectedItem as string ?? "All scopes";
        string risk = _risk.SelectedItem as string ?? "All risks";
        string link = _link.SelectedItem as string ?? "All links";
        _visible = _report.Matches
            .Where(x => (scope == "All scopes" || x.Scope == scope) &&
                (risk == "All risks" || x.Risk == risk) &&
                (link == "All links" || x.LinkStatus == link) &&
                (query.Length == 0 || new[] {
                    x.Gpo, x.GpoId, x.Scope, x.Risk, x.LinkStatus,
                    x.Setting, x.Value, x.Key, x.Source, x.LinkedAt, x.Reason
                }.Any(v => v.Contains(query, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(x => x.Risk == "High" ? 0 :
                x.Risk == "Warning" ? 1 :
                x.LinkStatus == "Linked candidate" ? 2 : 3)
            .ThenBy(x => x.Gpo, StringComparer.OrdinalIgnoreCase).ToArray();
        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            foreach (var item in _visible)
            {
                int i = _grid.Rows.Add(item.Risk, item.LinkStatus,
                    item.Scope, item.Gpo, item.Setting, item.Value,
                    item.Source, item.LinkedAt, item.Key);
                _grid.Rows[i].Tag = item;
                if (item.Risk == "High")
                    _grid.Rows[i].DefaultCellStyle.ForeColor = Color.Firebrick;
                else if (item.Risk == "Warning")
                    _grid.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 244, 217);
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
        _count.Text = _visible.Length + "/" + _report.Count +
            UiLocale.Display(" recognized settings | ") +
            _report.GpoCount + UiLocale.Display(" GPOs | ") +
            _report.Issues.Count + UiLocale.Display(" source issues | NOT RSoP");
    }

    private void ShowDetails()
    {
        string issues = _report.Issues.Count == 0
            ? UiLocale.Display("Known-source read errors: none (unrecognized CSEs remain unverified).")
            : UiLocale.Display("Unverified GPO source reads: ") +
                _report.Issues.Count + Environment.NewLine +
                string.Join(Environment.NewLine, _report.Issues.Take(35).Select(x =>
                    x.Gpo + " [" + x.Scope + "] " + x.Reason));
        string limits = UiLocale.Display(
            "No complete GPO settings index, winning RSoP, security/WMI/GPP item-level filtering, client HKCU, MDM, site or loopback verification is claimed.");
        if (_grid.SelectedRows.Count == 0 ||
            _grid.SelectedRows[0].Tag is not DomainGpoMatch item)
        {
            _details.Text = issues + Environment.NewLine + limits +
                Environment.NewLine +
                string.Join(Environment.NewLine, _report.Notes.Take(20));
            return;
        }
        _details.Text =
            "GPO: " + item.Gpo + " {" + item.GpoId + "}" + Environment.NewLine +
            "Scope: " + item.Scope + " | Link status: " + item.LinkStatus +
            Environment.NewLine + "Linked: " + item.LinkedAt +
            Environment.NewLine + "Registry: " + item.Key + " / " + item.Setting +
            Environment.NewLine + "Value: " + item.Value +
            Environment.NewLine + "Source: " + item.Source +
            Environment.NewLine + "Risk: " + item.Risk + " | " + item.Reason +
            Environment.NewLine + "Limitations: " + item.Applicability +
            Environment.NewLine + issues + Environment.NewLine + limits;
    }

    private void ExportCsv()
    {
        if (MessageBox.Show(this, UiLocale.Display(
            "This report contains private GPO names, domain paths and policy values. " +
            "Save to a protected LOCAL folder only; do not upload raw CSV. Continue?"),
            UiLocale.Display("Private GPO report"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var picker = new SaveFileDialog {
            Title = UiLocale.Display("Export filtered read-only GPO candidates"),
            Filter = "CSV (*.csv)|*.csv", DefaultExt = "csv",
            AddExtension = true, OverwritePrompt = true,
            FileName = "All-DC-GPO-Conflict-Audit.csv"
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        string full = Path.GetFullPath(picker.FileName);
        if (!Path.IsPathFullyQualified(full) ||
            full.StartsWith(@"\\", StringComparison.Ordinal) ||
            full.StartsWith(@"\\?\", StringComparison.Ordinal))
            throw new InvalidDataException("Choose a regular local folder for GPO report.");
        for (DirectoryInfo? dir = new(Path.GetDirectoryName(full)!);
            dir is not null && dir.Parent is not null; dir = dir.Parent)
        {
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("A linked directory cannot receive the report.");
        }
        if (File.Exists(full) &&
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Cannot overwrite a linked GPO report.");
        File.WriteAllText(full, DomainWideGpoReport.BuildCsv(_visible),
            new UTF8Encoding(true));
    }
}
