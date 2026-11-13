using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only presentation of TWO independent observations and linked GPO
/// candidates. No cached/remote policy is promoted to winning RSoP or proof
/// that a User Screen Saver was displayed in the active session.
/// </summary>
internal sealed class ScreenSaverEndpointPowerDialog : Form
{
    private readonly EndpointPowerObservation _observation;
    private readonly int _idle;
    private readonly IReadOnlyList<GroupConflictFinding> _candidates;
    private readonly string _scanStatus;
    private readonly DataGridView _readings = new() { Name = "EndpointPowerReadingsGrid" };
    private readonly DataGridView _policies = new() { Name = "EndpointPowerCandidateGrid" };
    private readonly TextBox _summary = new() { Name = "EndpointPowerAuditSummary" };

    internal ScreenSaverEndpointPowerDialog(
        EndpointPowerObservation observation, int configuredIdleSeconds,
        IReadOnlyList<GroupConflictFinding> candidates, string scanStatus)
    {
        _observation = observation;
        _idle = configuredIdleSeconds;
        _candidates = candidates;
        _scanStatus = scanStatus;

        Name = "ScreenSaverEndpointPowerDialog";
        Text = "LockScreen Campaign Manager - Screen Saver power audit";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(1030, 690);
        Size = new Size(1370, 880);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiStyle.Canvas;

        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        Controls.Add(root);

        var head = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        head.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        head.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        head.Controls.Add(new Label {
            Text = UiLocale.Display("Screen Saver — observed endpoint power and static GPO candidates"),
            Dock = DockStyle.Fill, AutoEllipsis = false,
            Font = new Font("Segoe UI Semibold", 15),
            ForeColor = UiStyle.Navy,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        head.Controls.Add(new Label {
            Name = "EndpointPowerScopeLabel", RightToLeft = RightToLeft.No,
            Text = "PC: " + observation.Computer + "  |  DC: " + observation.Dc +
                Environment.NewLine +
                UiLocale.Display("Configured User Screen Saver: ") + configuredIdleSeconds +
                " s  |  " + UiLocale.Display("Observed UTC: ") +
                observation.ObservedUtc.ToString("u"),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false, ForeColor = UiStyle.Foreground
        }, 0, 1);
        root.Controls.Add(head, 0, 0);

        var tabs = new TabControl { Name = "EndpointPowerAuditTabs", Dock = DockStyle.Fill };
        var readingsTab = new TabPage(UiLocale.Display("Observed endpoint power")) {
            BackColor = UiStyle.Surface
        };
        SetupGrid(_readings, [
            ("Source",110), ("Setting",100), ("Supply",65),
            ("State",90), ("Value (s)",85), ("Compared with Screen Saver",230)
        ]);
        readingsTab.Controls.Add(_readings);
        tabs.TabPages.Add(readingsTab);

        var gpoTab = new TabPage(UiLocale.Display("Linked GPO candidates")) {
            BackColor = UiStyle.Surface
        };
        SetupGrid(_policies, [
            ("GPO",150), ("Setting",125), ("Value",80),
            ("Risk",90), ("Source",90), ("Link",160), ("Note",220)
        ]);
        gpoTab.Controls.Add(_policies);
        tabs.TabPages.Add(gpoTab);
        root.Controls.Add(tabs, 0, 1);

        _summary.Dock = DockStyle.Fill;
        _summary.ReadOnly = true;
        _summary.Multiline = true;
        _summary.ScrollBars = ScrollBars.Vertical;
        _summary.WordWrap = true;
        _summary.BackColor = UiStyle.Surface;
        _summary.ForeColor = UiStyle.Foreground;
        root.Controls.Add(_summary, 0, 2);

        var footer = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, AutoScroll = true, Padding = new Padding(2)
        };
        var close = new Button {
            Text = UiLocale.Display("Close"), Width = 126, Height = 40,
            DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat,
            BackColor = UiStyle.Accent, ForeColor = Color.White
        };
        footer.Controls.Add(close);
        root.Controls.Add(footer, 0, 3);
        AcceptButton = close;
        CancelButton = close;
        Draw();
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "endpoint-power-audit");
    }

    private static void SetupGrid(DataGridView grid,
        IReadOnlyList<(string caption, int weight)> columns)
    {
        grid.Dock = DockStyle.Fill;
        grid.RightToLeft = RightToLeft.No; // GUID/FQDN/AC/DC values remain invariant
        grid.ReadOnly = true;
        grid.MultiSelect = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (caption, weight) in columns)
            grid.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = UiLocale.Display(caption), FillWeight = weight
            });
    }

    private void Draw()
    {
        var disagreements = _observation.SourceDisagreements();
        foreach (EndpointPowerAssessment value in _observation.Assess(_idle))
        {
            int row = _readings.Rows.Add(
                value.Source, value.Kind, value.Supply, value.Status,
                value.Seconds is long seconds ? seconds == 0 ? "0 (Never)" :
                    seconds.ToString() : "—", UiLocale.Display(value.Interpretation));
            if (disagreements.Any(d => d.Kind == value.Kind && d.Supply == value.Supply))
                _readings.Rows[row].DefaultCellStyle.BackColor = Color.FromArgb(255, 244, 217);
            if (value.Interpretation.Contains("potential blocker", StringComparison.Ordinal))
                _readings.Rows[row].DefaultCellStyle.ForeColor = Color.Firebrick;
        }
        if (_readings.Rows.Count == 0)
            _readings.Rows.Add("Not verified", "—", "—", "Unavailable",
                "—", "Remote WMI/Registry evidence unavailable");

        var rawCount = _candidates.Count;
        int high = 0, warning = 0;
        foreach (var candidate in _candidates)
        {
            var comparison = ScreenSaverPowerConflictAnalysis.Compare(
                _observation.Computer, _idle, [candidate]);
            var finding = comparison.FirstOrDefault();
            string assessment = candidate.Risk == "Inactive/filtered" ?
                "Excluded/inactive candidate" : finding is null ?
                "No earlier positive timeout established" :
                finding.Risk + ": " + finding.Setting;
            if (finding?.Risk == "High") high++;
            if (finding?.Risk == "Warning") warning++;
            int row = _policies.Rows.Add(candidate.Gpo, candidate.Setting,
                candidate.Value, candidate.Risk, candidate.Source,
                candidate.LinkedAt, assessment);
            if (finding?.Risk == "High")
                _policies.Rows[row].DefaultCellStyle.ForeColor = Color.Firebrick;
        }
        if (_policies.Rows.Count == 0)
            _policies.Rows.Add("—", "—", "—", "—", "—", "—",
                _scanStatus.StartsWith("Complete", StringComparison.Ordinal)
                    ? "No power GPO candidates in this scoped static scan"
                    : "GPO candidate scan not verified");

        _summary.Text =
            UiLocale.Display("Configured User Screen Saver is not verified as applied HKCU policy.") +
            Environment.NewLine +
            UiLocale.Display("Active plan: ") + _observation.PlanStatus + " | " +
            _observation.PlanName + " | " + _observation.PlanGuid +
            Environment.NewLine +
            UiLocale.Display("Client HKLM power policies: ") + _observation.RegistryStatus +
            " | " + UiLocale.Display("Observed short timeouts: ") +
            _observation.PotentialEarly(_idle) +
            Environment.NewLine +
            UiLocale.Display("Source disagreements (WMI vs HKLM): ") + disagreements.Count +
            (disagreements.Count == 0 ? "" : " | " + string.Join("; ",
                disagreements.Select(d => d.Kind + "/" + d.Supply +
                    " WMI " + d.Wmi + "s / HKLM " + d.Policy + "s"))) +
            Environment.NewLine +
            UiLocale.Display("Static linked-GPO candidates: ") + rawCount +
            " (" + high + " high / " + warning + " warning). " + _scanStatus +
            Environment.NewLine +
            UiLocale.Display("No winning GPO, active display state or actual User Screen Saver is proven. Verify gpresult /scope user and computer, powercfg /query, MDM and real desktop behavior.");
    }
}
