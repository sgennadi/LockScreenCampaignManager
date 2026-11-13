using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Operator-approved pilot checklist. Never installs agents, edits GPOs or
/// fetches power readings automatically. The optional remote reader is a
/// caller-provided, explicitly scoped read-only WMI/HKLM operation.
/// </summary>
internal sealed class DomainGpoPilotReadinessDialog : Form
{
    private readonly DomainWideGpoReport _report;
    private readonly RsopEvidence? _computerXml;
    private readonly UserRsopEvidence? _userXml;
    private readonly Func<string, string, Task<EndpointPowerObservation>>? _readPower;
    private readonly int _idle;
    private readonly TextBox _computer = new() { Name = "PilotComputerFqdn" };
    private readonly ComboBox _ou = new() { Name = "PilotComputerOu" };
    private readonly DataGridView _findings = new() { Name = "PilotReadinessFindings" };
    private readonly TextBox _details = new() { Name = "PilotReadinessDetails" };
    private readonly Label _summary = new() { Name = "PilotReadinessSummary" };
    private readonly Button _verifyPower;
    private readonly Button _review;
    private readonly Button _reviewNamedGpos;
    private EndpointPowerObservation? _observation;
    private bool _reading;
    private DomainGpoPilotReadiness? _assessment;

    internal DomainGpoPilotReadinessDialog(
        DomainWideGpoReport report, RsopEvidence? computerXml,
        UserRsopEvidence? userXml, string computerName, int saverIdleSeconds,
        Func<string, string, Task<EndpointPowerObservation>>? readPower)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _computerXml = computerXml;
        _userXml = userXml;
        _readPower = readPower;
        _idle = saverIdleSeconds;
        Text = "LockScreen Campaign Manager " + AppVersion.Current +
            " - " + UiLocale.Display("Pilot readiness review");
        Name = "DomainGpoPilotReadinessDialog";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(1010, 710);
        Size = new Size(1390, 900);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiStyle.Canvas;
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label {
            Text = UiLocale.Display("Screen Saver pilot readiness — read-only"),
            Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 14),
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiStyle.Navy
        }, 0, 0);
        header.Controls.Add(new Label {
            Text = UiLocale.Display("Correlates independent DC GPO, unsigned User/Computer gpresult XML and optional endpoint power. No automatic verdict or changes."),
            Dock = DockStyle.Fill, AutoEllipsis = false,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiStyle.Foreground
        }, 0, 1);
        root.Controls.Add(header, 0, 0);

        var entry = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2
        };
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        entry.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        entry.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        entry.Controls.Add(new Label {
            Text = UiLocale.Display("Pilot computer (FQDN)"),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _computer.Dock = DockStyle.Fill;
        _computer.RightToLeft = RightToLeft.No;
        _computer.Text = computerName.Trim();
        _computer.PlaceholderText = "pilot-pc." + report.Domain;
        _computer.TextChanged += (_, _) => InvalidateObservation();
        entry.Controls.Add(_computer, 1, 0);
        entry.Controls.Add(new Label {
            Text = UiLocale.Display("Verified target Computer OU"),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        _ou.Dock = DockStyle.Fill;
        _ou.DropDownStyle = ComboBoxStyle.DropDownList;
        _ou.RightToLeft = RightToLeft.No;
        foreach (string candidate in report.ComputerOus)
            _ou.Items.Add(candidate);
        if (_ou.Items.Count > 0) _ou.SelectedIndex = 0;
        _ou.SelectedIndexChanged += (_, _) => InvalidateObservation();
        entry.Controls.Add(_ou, 1, 1);
        root.Controls.Add(entry, 0, 1);

        _findings.Dock = DockStyle.Fill;
        _findings.Name = "PilotReadinessFindings";
        _findings.ReadOnly = true;
        _findings.RightToLeft = RightToLeft.No;
        _findings.MultiSelect = false;
        _findings.RowHeadersVisible = false;
        _findings.AutoGenerateColumns = false;
        _findings.AllowUserToAddRows = false;
        _findings.AllowUserToDeleteRows = false;
        _findings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _findings.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        foreach (var (caption,width) in new (string,int)[] {
            ("Area", 205), ("State", 140),
            ("Observation", 595), ("Next step", 530)
        })
            _findings.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = UiLocale.Display(caption), Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        _findings.SelectionChanged += (_, _) => Details();
        root.Controls.Add(_findings, 0, 2);

        _details.Dock = DockStyle.Fill;
        _details.ReadOnly = true;
        _details.Multiline = true;
        _details.ScrollBars = ScrollBars.Both;
        _details.WordWrap = true;
        _details.RightToLeft = RightToLeft.No;
        _details.Font = new Font("Consolas", 9.2f);
        _details.BackColor = UiStyle.Surface;
        root.Controls.Add(_details, 0, 3);

        var footer = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _summary.Dock = DockStyle.Fill;
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        _summary.ForeColor = UiStyle.Muted;
        footer.Controls.Add(_summary, 0, 0);
        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, AutoScroll = true
        };
        var close = Button("Close", 95);
        close.Click += (_, _) => Close();
        actions.Controls.Add(close);
        _verifyPower = Button("Verify power (PC)...", 185);
        _verifyPower.Name = "PilotVerifyPower";
        _verifyPower.Enabled = _readPower is not null && _ou.Items.Count > 0;
        _verifyPower.Click += async (_, _) => await VerifyPowerAsync();
        actions.Controls.Add(_verifyPower);
        _review = Button("Refresh review", 160);
        _review.Name = "PilotRefreshReview";
        _review.Click += (_, _) => Review();
        actions.Controls.Add(_review);
        _reviewNamedGpos = Button("Review GPO blockers...", 225);
        _reviewNamedGpos.Name = "PilotReviewNamedGpoBlockers";
        _reviewNamedGpos.Enabled = false;
        _reviewNamedGpos.Click += (_, _) => {
            if (_reading || _assessment is null ||
                _assessment.NamedBlockers.Count == 0) return;
            using var window = new DomainPilotBlockersDialog(_assessment.NamedBlockers);
            window.ShowDialog(this);
        };
        actions.Controls.Add(_reviewNamedGpos);
        footer.Controls.Add(actions, 0, 1);
        root.Controls.Add(footer, 0, 4);

        FormClosing += (_, e) => {
            if (!_reading) return;
            e.Cancel = true;
            MessageBox.Show(this, UiLocale.Display(
                "Read-only endpoint query still running; review its outcome before closing."),
                UiLocale.Display("Pilot readiness review"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "pilot-readiness");
        Review();
    }

    private static Button Button(string label, int width) => new() {
        Text = UiLocale.Display(label),
        MinimumSize = new Size(width, 36),
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly,
        FlatStyle = FlatStyle.Flat, BackColor = UiStyle.Surface, Margin = new Padding(4)
    };

    private string SelectedOu => _ou.SelectedItem as string ?? "";

    private void InvalidateObservation()
    {
        // A fresh computer/OU selection invalidates only its dependent WMI/HKLM
        // observation; the two separately imported gpresult XMLs remain as
        // clearly labeled historical/unsigned evidence.
        _observation = null;
        Review();
    }

    private void Review()
    {
        if (_reading) return;
        string computer = _computer.Text.Trim();
        string ou = SelectedOu;
        if (!NativeGpoConflictScan.SafeComputerName(computer, _report.Domain) ||
            _idle is < 60 or > 86400)
        {
            _assessment = null;
            _findings.Rows.Clear();
            _details.Text = UiLocale.Display(
                "Enter a full domain computer FQDN and a configured Screen Saver idle time.");
            _summary.Text = UiLocale.Display("Not verified — invalid pilot input");
            _verifyPower.Enabled = false;
            _reviewNamedGpos.Enabled = false;
            return;
        }
        if (_report.ComputerOus.Count == 0 || ou.Length == 0)
        {
            _assessment = null;
            _findings.Rows.Clear();
            _details.Text = UiLocale.Display(
                "No scanned Computer OU. Select a Computer OU in the campaign and rescan DC GPOs.");
            _summary.Text = UiLocale.Display("Not verified — Computer OU missing");
            _verifyPower.Enabled = false;
            _reviewNamedGpos.Enabled = false;
            return;
        }
        _verifyPower.Enabled = _readPower is not null;
        try
        {
            _assessment = DomainGpoPilotReadiness.Build(_report, computer,
                ou, _idle, _observation, _computerXml, _userXml,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _assessment = null;
            _findings.Rows.Clear();
            _summary.Text = UiLocale.Display("Not verified — evidence mismatch");
            _details.Text = ex.Message;
            _verifyPower.Enabled = false;
            _reviewNamedGpos.Enabled = false;
            return;
        }
        _findings.SuspendLayout();
        try
        {
            _findings.Rows.Clear();
            foreach (DomainPilotFinding finding in _assessment.Findings)
            {
                int i = _findings.Rows.Add(
                    UiLocale.Display(finding.Area), UiLocale.Display(finding.State),
                    UiLocale.Display(finding.Observation),
                    UiLocale.Display(finding.NextStep));
                _findings.Rows[i].Tag = finding;
                if (finding.State == "Observed concern")
                    _findings.Rows[i].DefaultCellStyle.ForeColor = Color.Firebrick;
                if (finding.State == "Not verified")
                    _findings.Rows[i].DefaultCellStyle.BackColor =
                        Color.FromArgb(255, 244, 217);
            }
        }
        finally { _findings.ResumeLayout(); }
        _findings.ClearSelection();
        if (_findings.Rows.Count > 0)
        {
            _findings.Rows[0].Selected = true;
            _findings.CurrentCell = _findings.Rows[0].Cells[0];
        }
        Details();
        _reviewNamedGpos.Enabled = _assessment.NamedBlockers.Count > 0;
        _summary.Text = _assessment.NamedBlockers.Count +
            UiLocale.Display(" named GPO candidates | ") +
            _assessment.Concerns +
            UiLocale.Display(" observed concerns | ") +
            _assessment.Potential + UiLocale.Display(" source candidates | ") +
            _assessment.NotVerified + UiLocale.Display(" not verified");
    }

    private void Details()
    {
        if (_assessment is null) return;
        if (_findings.SelectedRows.Count > 0 &&
            _findings.SelectedRows[0].Tag is DomainPilotFinding selected)
            _details.Text = selected.Area + ": " + selected.State +
                Environment.NewLine + selected.Observation +
                Environment.NewLine + "Next: " + selected.NextStep +
                Environment.NewLine + Environment.NewLine +
                DomainGpoRsopCorrelation.EvidenceWarning(_computerXml, _userXml);
        else
            _details.Text = DomainGpoRsopCorrelation.EvidenceWarning(
                _computerXml, _userXml);
    }

    private async Task VerifyPowerAsync()
    {
        if (_reading || _readPower is null) return;
        string computer = _computer.Text.Trim();
        string ou = SelectedOu;
        if (!NativeGpoConflictScan.SafeComputerName(computer, _report.Domain) ||
            !_report.ComputerOus.Contains(ou, StringComparer.OrdinalIgnoreCase))
        {
            Review();
            return;
        }
        if (MessageBox.Show(this, UiLocale.Display(
            "Read power settings from this approved domain computer using remote " +
            "WMI and HKLM? The selected DC must reverify the exact Computer OU/FQDN. " +
            "No changes, gpupdate, remote tasks or scripts are performed."),
            UiLocale.Display("Verify endpoint power — read-only"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        _reading = true;
        _verifyPower.Enabled = false;
        _review.Enabled = false;
        _reviewNamedGpos.Enabled = false;
        _computer.Enabled = false;
        _ou.Enabled = false;
        _summary.Text = UiLocale.Display("Reading endpoint power (read-only)...");
        try
        {
            var reading = await _readPower(computer, ou);
            if (!reading.Computer.Equals(computer, StringComparison.OrdinalIgnoreCase) ||
                !reading.OuDn.Equals(ou, StringComparison.OrdinalIgnoreCase) ||
                !reading.Domain.Equals(_report.Domain, StringComparison.OrdinalIgnoreCase) ||
                !NativeGpoLifecycleList.SameController(reading.Dc, _report.Dc))
                throw new InvalidDataException(
                    "Remote power observation did not match the pilot computer/OU/DC.");
            _observation = reading;
        }
        catch (Exception ex)
        {
            _observation = null;
            UiErrorDialog.Show(this, "Verify pilot power (read-only)", ex);
        }
        finally
        {
            _reading = false;
            _review.Enabled = true;
            _computer.Enabled = true;
            _ou.Enabled = true;
            Review();
        }
    }
}
