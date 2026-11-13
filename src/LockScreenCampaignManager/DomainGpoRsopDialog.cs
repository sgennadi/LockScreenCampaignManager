using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Compare an in-memory all-DC GPO source snapshot with two independently
/// operator-imported, unsigned gpresult /x snapshots. Never queries endpoints,
/// never applies policies, and never infers an effective per-setting winner.
/// </summary>
internal sealed class DomainGpoRsopDialog : Form
{
    private readonly DomainWideGpoReport _report;
    private RsopEvidence? _computer;
    private UserRsopEvidence? _user;
    private readonly TextBox _computerName = new() { Name = "GpresultComputerFqdn" };
    private readonly TextBox _userName = new() { Name = "GpresultUserPrincipal" };
    private readonly TextBox _search = new() { Name = "GpresultComparisonSearch" };
    private readonly ComboBox _scope = new() { Name = "GpresultComparisonScope" };
    private readonly ComboBox _observation = new() { Name = "GpresultComparisonObservation" };
    private readonly DataGridView _grid = new() { Name = "GpresultComparisonGrid" };
    private readonly TextBox _details = new() { Name = "GpresultComparisonDetails" };
    private readonly Label _status = new() { Name = "GpresultEvidenceStatus" };
    private readonly Button _clearComputer;
    private readonly Button _clearUser;
    private DomainGpoRsopMatch[] _visible = [];

    internal DomainGpoRsopDialog(DomainWideGpoReport report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        Text = "LockScreen Campaign Manager " + AppVersion.Current +
            " - " + UiLocale.Display("Compare gpresult XML");
        Name = "DomainGpoRsopDialog";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1110, 740);
        Size = new Size(1570, 920);
        Font = new Font("Segoe UI", 9.5f);
        BackColor = UiStyle.Canvas;
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(14),
            ColumnCount = 1, RowCount = 6
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 93));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 185));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label {
            Text = UiLocale.Display("GPO source vs imported Computer / User gpresult"),
            Dock = DockStyle.Fill, AutoEllipsis = false,
            ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 14),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        header.Controls.Add(new Label {
            Text = UiLocale.Display(
                "Read-only source comparison by exact GPO GUID and policy scope. " +
                "Typed identities and imported XML are NOT AD-verified or signed."),
            Dock = DockStyle.Fill, AutoEllipsis = false,
            ForeColor = UiStyle.Foreground,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        root.Controls.Add(header, 0, 0);

        var identity = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 3
        };
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        identity.Controls.Add(MakeLabel("Computer FQDN (typed):"), 0, 0);
        identity.Controls.Add(MakeLabel("User identity (exact):"), 0, 1);
        _computerName.Dock = DockStyle.Fill;
        _computerName.PlaceholderText = "pc01." + report.Domain;
        _computerName.RightToLeft = RightToLeft.No;
        _computerName.TextChanged += (_, _) => {
            _computer = null; _clearComputer.Enabled = false; Draw();
        };
        identity.Controls.Add(_computerName, 1, 0);
        _userName.Dock = DockStyle.Fill;
        _userName.PlaceholderText = "DOMAIN\\user  /  user@" + report.Domain;
        _userName.RightToLeft = RightToLeft.No;
        _userName.TextChanged += (_, _) => {
            _user = null; _clearUser.Enabled = false; Draw();
        };
        identity.Controls.Add(_userName, 1, 1);

        var pcButtons = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = true
        };
        var importPc = Action("Import Computer XML...", 205);
        importPc.Name = "GpresultImportComputer";
        importPc.Click += (_, _) => ImportComputer();
        pcButtons.Controls.Add(importPc);
        _clearComputer = Action("Clear Computer", 143);
        _clearComputer.Name = "GpresultClearComputer";
        _clearComputer.Enabled = false;
        _clearComputer.Click += (_, _) => {
            _computer = null; _clearComputer.Enabled = false; Draw();
        };
        pcButtons.Controls.Add(_clearComputer);
        identity.Controls.Add(pcButtons, 2, 0);

        var userButtons = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = true
        };
        var importUser = Action("Import User XML...", 205);
        importUser.Name = "GpresultImportUser";
        importUser.Click += (_, _) => ImportUser();
        userButtons.Controls.Add(importUser);
        _clearUser = Action("Clear User", 143);
        _clearUser.Name = "GpresultClearUser";
        _clearUser.Enabled = false;
        _clearUser.Click += (_, _) => {
            _user = null; _clearUser.Enabled = false; Draw();
        };
        userButtons.Controls.Add(_clearUser);
        identity.Controls.Add(userButtons, 2, 1);
        root.Controls.Add(identity, 0, 1);

        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 225));
        _search.Dock = DockStyle.Fill;
        _search.PlaceholderText = UiLocale.Display(
            "Search GPO, setting, registry key or XML observation...");
        _search.TextChanged += (_, _) => Draw();
        _search.RightToLeft = RightToLeft.No;
        filters.Controls.Add(_search, 0, 0);
        SetupCombo(_scope, ["All scopes", "Computer", "User"]);
        SetupCombo(_observation, ["All RSoP states", "Reported applied",
            "Reported filtered", "Insufficient flags", "Not listed (unknown)",
            "Not imported"]);
        filters.Controls.Add(_scope, 1, 0);
        filters.Controls.Add(_observation, 2, 0);
        root.Controls.Add(filters, 0, 2);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.RightToLeft = RightToLeft.No;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        foreach (var (caption,width) in new (string,int)[] {
            ("Scope", 94), ("Source risk", 110),
            ("Reported RSoP", 180), ("GPO", 245),
            ("Setting", 195), ("Value", 210), ("Link status", 180),
            ("Registry key", 340), ("Source", 190)
        })
            _grid.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = UiLocale.Display(caption), Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        _grid.SelectionChanged += (_, _) => ShowDetails();
        root.Controls.Add(_grid, 0, 3);

        _details.Dock = DockStyle.Fill;
        _details.Multiline = true;
        _details.ScrollBars = ScrollBars.Both;
        _details.ReadOnly = true;
        _details.RightToLeft = RightToLeft.No;
        _details.Font = new Font("Consolas", 9.2f);
        _details.BackColor = UiStyle.Surface;
        root.Controls.Add(_details, 0, 4);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        _status.Dock = DockStyle.Fill;
        _status.AutoEllipsis = false;
        _status.ForeColor = UiStyle.Muted;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        footer.Controls.Add(_status, 0, 0);
        var close = Action("Close", 118);
        close.Name = "GpresultComparisonClose";
        close.Click += (_, _) => Close();
        footer.Controls.Add(close, 1, 0);
        root.Controls.Add(footer, 0, 5);
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "all-gpo-rsop-correlation");
        Draw();
    }

    private sealed record Choice(string Key) {
        public override string ToString() => UiLocale.Display(Key);
    }

    private static Label MakeLabel(string label) => new() {
        Text = UiLocale.Display(label),
        Dock = DockStyle.Fill, AutoEllipsis = false,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private static Button Action(string caption, int width) => new() {
        Text = UiLocale.Display(caption), MinimumSize = new Size(width, 36),
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly,
        FlatStyle = FlatStyle.Flat,
        BackColor = UiStyle.Surface, Margin = new Padding(4)
    };
    private void SetupCombo(ComboBox box, IEnumerable<string> options)
    {
        box.Dock = DockStyle.Fill;
        box.RightToLeft = RightToLeft.No;
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Items.AddRange(options.Select(x => (object)new Choice(x)).ToArray());
        box.SelectedIndex = 0;
        box.SelectedIndexChanged += (_, _) => Draw();
    }

    private string SelectXml()
    {
        using var chooser = new OpenFileDialog {
            Title = UiLocale.Display("Select local gpresult /x XML"),
            Filter = "gpresult XML (*.xml)|*.xml",
            CheckFileExists = true, CheckPathExists = true,
            Multiselect = false, RestoreDirectory = true
        };
        return chooser.ShowDialog(this) == DialogResult.OK ? chooser.FileName : "";
    }

    private bool ConfirmPrivateImport()
    {
        return MessageBox.Show(this, UiLocale.Display(
            "The selected gpresult XML may contain private usernames, group " +
            "memberships and internal policy paths. Only GPO GUIDs/flags and " +
            "report metadata are retained in memory, not uploaded or saved. " +
            "An unsigned file cannot prove the actual client identity. Continue?"),
            UiLocale.Display("Import private RSoP report"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private bool ConfirmHistorical(DateTimeOffset time)
    {
        return MessageBox.Show(this,
            UiLocale.Display("The imported snapshot is over 24 hours old: ") +
            time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") +
            Environment.NewLine +
            UiLocale.Display("Use this snapshot as historical evidence only?"),
            UiLocale.Display("Historical RSoP"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void ImportComputer()
    {
        string computer = _computerName.Text.Trim();
        if (computer.Length > 253 ||
            !computer.EndsWith("." + _report.Domain, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, UiLocale.Display(
                "Enter the full Computer FQDN from the gpresult XML first."),
                UiLocale.Display("Computer identity required"));
            return;
        }
        string path = SelectXml();
        if (path.Length == 0 || !ConfirmPrivateImport()) return;
        try
        {
            var incoming = RsopEvidence.LoadLocalXml(path, computer, _report.Domain);
            if (incoming.IsHistorical && !ConfirmHistorical(incoming.ReadUtc)) return;
            _computer = incoming;
            _clearComputer.Enabled = true;
            Draw();
        }
        catch (Exception ex)
        {
            UiErrorDialog.Show(this, "Import Computer gpresult XML", ex);
        }
    }

    private void ImportUser()
    {
        string identity = _userName.Text.Trim();
        if (identity.Length == 0)
        {
            MessageBox.Show(this, UiLocale.Display(
                "Enter the exact UserResults identity from gpresult first."),
                UiLocale.Display("User identity required"));
            return;
        }
        string path = SelectXml();
        if (path.Length == 0 || !ConfirmPrivateImport()) return;
        try
        {
            var incoming = UserRsopEvidence.LoadLocalXml(path, identity, _report.Domain);
            if (incoming.IsHistorical && !ConfirmHistorical(incoming.ReadUtc)) return;
            _user = incoming;
            _clearUser.Enabled = true;
            Draw();
        }
        catch (Exception ex)
        {
            UiErrorDialog.Show(this, "Import User gpresult XML", ex);
        }
    }

    private void Draw()
    {
        string text = _search.Text.Trim();
        string scope = (_scope.SelectedItem as Choice)?.Key ?? "All scopes";
        string observation = (_observation.SelectedItem as Choice)?.Key ?? "All RSoP states";
        _visible = DomainGpoRsopCorrelation.Compare(_report, _computer, _user)
            .Where(x =>
                (scope == "All scopes" || x.Scope == scope) &&
                (observation == "All RSoP states" || x.ReportedRsop == observation) &&
                (text.Length == 0 || new[] {
                    x.Scope, x.Gpo, x.GpoId, x.Setting, x.RegistryKey,
                    x.Value, x.SourceRisk, x.ReportedRsop, x.LinkStatus, x.Source
                }.Any(v => v.Contains(text, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(x => x.SourceRisk == "High" ? 0 :
                x.SourceRisk == "Warning" ? 1 : 2)
            .ThenBy(x => x.Gpo, StringComparer.OrdinalIgnoreCase).ToArray();
        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            foreach (var row in _visible)
            {
                int index = _grid.Rows.Add(row.Scope, row.SourceRisk,
                    UiLocale.Display(row.ReportedRsop), row.Gpo, row.Setting,
                    row.Value, UiLocale.Display(row.LinkStatus),
                    row.RegistryKey, row.Source);
                _grid.Rows[index].Tag = row;
                if (row.ReportedRsop == "Reported applied" &&
                    row.SourceRisk == "High")
                    _grid.Rows[index].DefaultCellStyle.ForeColor = Color.DarkRed;
                else if (row.ReportedRsop == "Insufficient flags" ||
                         row.ReportedRsop == "Not listed (unknown)")
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
        _status.Text = _visible.Length + "/" + _report.Count +
            UiLocale.Display(" source rows | Computer: ") +
            UiLocale.Display(_computer is null ? "Not imported" : "XML loaded") +
            UiLocale.Display(" | User: ") +
            UiLocale.Display(_user is null ? "Not imported" : "XML loaded") +
            UiLocale.Display(" | NOT winning RSoP");
    }

    private void ShowDetails()
    {
        if (_grid.SelectedRows.Count == 0 ||
            _grid.SelectedRows[0].Tag is not DomainGpoRsopMatch row)
        {
            _details.Text = DomainGpoRsopCorrelation.EvidenceWarning(_computer, _user);
            return;
        }
        string detail = row.Scope == "Computer"
            ? _computer?.DetailFor(row.GpoId) ?? "Computer RSoP not imported."
            : _user?.DetailFor(row.GpoId) ?? "User RSoP not imported.";
        _details.Text = "GPO: " + row.Gpo + " {" + row.GpoId + "}" +
            Environment.NewLine + "Scope: " + row.Scope +
            " | DC source: " + _report.Dc +
            " | Scanned: " + _report.ObservedUtc.ToString("u") +
            Environment.NewLine + "Registry: " + row.RegistryKey +
            " / " + row.Setting + " = " + row.Value +
            Environment.NewLine + "GPO source risk: " + row.SourceRisk +
            " | Link: " + row.LinkStatus +
            Environment.NewLine + "Imported XML: " + row.ReportedRsop +
            " | " + detail +
            Environment.NewLine + row.Interpretation +
            Environment.NewLine + DomainGpoRsopCorrelation.EvidenceWarning(_computer, _user);
    }
}
