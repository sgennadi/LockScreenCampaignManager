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
    private readonly Func<string, string, string, Task<DomainGpoScopeEvidence>>? _readAdScope;
    private readonly TextBox _user = new() { Name = "PilotAdUserSamOrUpn" };
    private DomainGpoScopeEvidence? _adScope;
    private readonly Button _verifyAdScope;
    private readonly TextBox _computer = new() { Name = "PilotComputerFqdn" };
    private readonly ComboBox _ou = new() { Name = "PilotComputerOu" };
    private readonly DataGridView _findings = new() { Name = "PilotReadinessFindings" };
    private readonly TextBox _details = new() { Name = "PilotReadinessDetails" };
    private readonly Label _summary = new() { Name = "PilotReadinessSummary" };
    private readonly Button _verifyPower;
    private readonly Button _review;
    private readonly Button _reviewNamedGpos;
    private readonly Button _importEndpoint;
    private readonly Button _clearEndpoint;
    private readonly Button _captureEndpoint;
    private readonly Button _importSysvol;
    private readonly Button _clearSysvol;
    private DomainSysvolSystemEvidence? _sysvolSystem;
    private EndpointUserReadinessEvidence? _endpointUser;
    private EndpointPowerObservation? _observation;
    private bool _reading;
    private DomainGpoPilotReadiness? _assessment;
    private string _reviewLanguage = "English";

    internal DomainGpoPilotReadinessDialog(
        DomainWideGpoReport report, RsopEvidence? computerXml,
        UserRsopEvidence? userXml, string computerName, int saverIdleSeconds,
        Func<string, string, Task<EndpointPowerObservation>>? readPower,
        Func<string, string, string, Task<DomainGpoScopeEvidence>>? readAdScope = null)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _computerXml = computerXml;
        _userXml = userXml;
        _readPower = readPower;
        _readAdScope = readAdScope;
        _idle = saverIdleSeconds;
        Text = "LockScreen Campaign Manager " + AppVersion.Current +
            " - " + UiLocale.Display("Unified GPO readiness");
        Name = "DomainGpoPilotReadinessDialog";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(1080, 790);
        Size = new Size(1420, 960);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiStyle.Canvas;
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
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
            Text = UiLocale.Display("AD-first: OU, nested groups, ACL, WMI candidates and loopback. gpresult XML and HKCU are optional; no policy changes."),
            Dock = DockStyle.Fill, AutoEllipsis = false,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiStyle.Foreground
        }, 0, 1);
        root.Controls.Add(header, 0, 0);

        var entry = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3
        };
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 255));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++)
            entry.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
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
        entry.Controls.Add(new Label {
            Text = UiLocale.Display("Pilot AD user (SAM or UPN, optional)"),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 2);
        _user.Dock = DockStyle.Fill;
        _user.RightToLeft = RightToLeft.No;
        _user.PlaceholderText = "pilot.user@" + report.Domain;
        _user.TextChanged += (_, _) => InvalidateObservation();
        entry.Controls.Add(_user, 1, 2);
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
        // Use the available width rather than a fixed 1470 px table. Wrap
        // long translated observations; size visible rows to their contents.
        _findings.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _findings.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders;
        _findings.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _findings.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;
        _findings.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _findings.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        foreach (var (caption,weight,minWidth) in new (string,float,int)[] {
            ("Area", 16, 145), ("State", 13, 115),
            ("Observation", 41, 290), ("Next step", 30, 220)
        })
            _findings.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = UiLocale.Display(caption), FillWeight = weight,
                MinimumWidth = minWidth,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        _findings.SelectionChanged += (_, _) => Details();
        root.Controls.Add(_findings, 0, 2);

        _details.Dock = DockStyle.Fill;
        _details.ReadOnly = true;
        _details.Multiline = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.WordWrap = true;
        _details.RightToLeft = RightToLeft.No;
        _details.Font = new Font("Segoe UI", 9.5f);
        _details.BackColor = UiStyle.Surface;
        root.Controls.Add(_details, 0, 3);

        var footer = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4
        };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
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
        _verifyAdScope = Button("Analyze AD scope (no XML)...", 265);
        _verifyAdScope.Name = "PilotVerifyAdScope";
        _verifyAdScope.Enabled = _readAdScope is not null && _ou.Items.Count > 0;
        _verifyAdScope.Click += async (_, _) => await VerifyAdScopeAsync();
        actions.Controls.Add(_verifyAdScope);
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
        var endpointActions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, AutoScroll = true
        };
        _captureEndpoint = Button("Capture this PC's user HKCU...", 230);
        _captureEndpoint.Name = "PilotCaptureCurrentUser";
        _captureEndpoint.Click += (_, _) => {
            if (_reading) return;
            using var window = new EndpointUserProbeDialog();
            window.ShowDialog(this);
        };
        endpointActions.Controls.Add(_captureEndpoint);
        _importEndpoint = Button("Import user HKCU JSON...", 225);
        _importEndpoint.Name = "PilotImportUserHkcu";
        _importEndpoint.Click += (_, _) => ImportEndpointSnapshot();
        endpointActions.Controls.Add(_importEndpoint);
        _clearEndpoint = Button("Clear HKCU evidence", 180);
        _clearEndpoint.Name = "PilotClearUserHkcu";
        _clearEndpoint.Enabled = false;
        _clearEndpoint.Click += (_, _) => {
            _endpointUser = null; _clearEndpoint.Enabled = false; Review();
        };
        endpointActions.Controls.Add(_clearEndpoint);
        footer.Controls.Add(endpointActions, 0, 2);
        var sysvolActions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, AutoScroll = true
        };
        _importSysvol = Button("Import SYSTEM SYSVOL JSON...", 265);
        _importSysvol.Name = "PilotImportSystemSysvol";
        _importSysvol.Click += (_, _) => ImportSysvolSnapshot();
        sysvolActions.Controls.Add(_importSysvol);
        _clearSysvol = Button("Clear SYSVOL evidence", 195);
        _clearSysvol.Name = "PilotClearSystemSysvol";
        _clearSysvol.Enabled = false;
        _clearSysvol.Click += (_, _) => {
            _sysvolSystem = null; Review();
        };
        sysvolActions.Controls.Add(_clearSysvol);
        footer.Controls.Add(sysvolActions, 0, 3);
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
        _endpointUser = null;
        _adScope = null;
        _sysvolSystem = null;
        _clearSysvol.Enabled = false;
        _clearEndpoint.Enabled = false;
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
            _verifyAdScope.Enabled = false;
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
            _verifyAdScope.Enabled = false;
            _reviewNamedGpos.Enabled = false;
            return;
        }
        _verifyPower.Enabled = _readPower is not null;
        _verifyAdScope.Enabled = _readAdScope is not null;
        try
        {
            _assessment = DomainGpoPilotReadiness.Build(_report, computer,
                ou, _idle, _observation, _computerXml, _userXml,
                DateTimeOffset.UtcNow, _endpointUser, _adScope, _sysvolSystem);
        }
        catch (Exception ex)
        {
            _assessment = null;
            _findings.Rows.Clear();
            _summary.Text = UiLocale.Display("Not verified — evidence mismatch");
            _details.Text = ex.Message;
            _verifyPower.Enabled = false;
            _verifyAdScope.Enabled = false;
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
                    PilotFindingPresentation.Cells(finding, _reviewLanguage));
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
        _clearEndpoint.Enabled = _endpointUser is not null;
        _clearSysvol.Enabled = _sysvolSystem is not null;
        RefreshSummary();
    }

    private string ReviewText(string text) => UiLocale.Translate(text, _reviewLanguage);

    private void RefreshSummary()
    {
        if (_assessment is null || _reading) return;
        _summary.Text = _assessment.NamedBlockers.Count +
            ReviewText(" named GPO candidates | ") +
            _assessment.Concerns + ReviewText(" observed concerns | ") +
            _assessment.Potential + ReviewText(" source candidates | ") +
            _assessment.NotVerified + ReviewText(" not verified");
    }

    // Called by the shared locale pass. Re-render existing immutable evidence,
    // never repeat an AD/endpoint read or change the selected user/computer.
    internal void ApplyReviewLanguage(string language)
    {
        _reviewLanguage = language;
        if (_assessment is null || _reading) return;
        foreach (DataGridViewRow row in _findings.Rows)
            if (row.Tag is DomainPilotFinding finding)
                row.SetValues(PilotFindingPresentation.Cells(finding, language));
        RefreshSummary();
        Details();
    }

    private void Details()
    {
        if (_assessment is null) return;
        string warning = ReviewText(
            DomainGpoRsopCorrelation.EvidenceWarning(_computerXml, _userXml));
        if (_findings.SelectedRows.Count > 0 &&
            _findings.SelectedRows[0].Tag is DomainPilotFinding selected)
            _details.Text = PilotFindingPresentation.Details(selected, _reviewLanguage) +
                Environment.NewLine + Environment.NewLine +
                (_adScope is null
                    ? ReviewText("AD-only scope not scanned.")
                    : ReviewText("AD-only scan") + ": " + _adScope.ObservedUtc.ToString("u") +
                      " | " + ReviewText("AD loopback") + ": " + ReviewText(_adScope.Loopback) +
                      " | " + ReviewText("Source candidates only, not endpoint RSoP.")) +
                Environment.NewLine + warning;
        else
            _details.Text = warning;
    }


    private void ImportEndpointSnapshot()
    {
        if (_reading) return;
        string computer = _computer.Text.Trim();
        if (!NativeGpoConflictScan.SafeComputerName(computer, _report.Domain) ||
            !_report.ComputerOus.Contains(SelectedOu, StringComparer.OrdinalIgnoreCase))
        {
            Review();
            return;
        }
        using var picker = new OpenFileDialog
        {
            Title = UiLocale.Display("Import private local user HKCU evidence"),
            Filter = "JSON (*.json)|*.json", CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _endpointUser = EndpointUserReadinessEvidence.LoadLocalJson(
                picker.FileName, _report.Domain, computer, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _endpointUser = null;
            UiErrorDialog.Show(this, "Import pilot user HKCU evidence", ex);
        }
        Review();
    }


    private void ImportSysvolSnapshot()
    {
        if (_reading) return;
        string computer = _computer.Text.Trim();
        if (!NativeGpoConflictScan.SafeComputerName(computer, _report.Domain) ||
            !_report.ComputerOus.Contains(SelectedOu, StringComparer.OrdinalIgnoreCase))
        {
            Review();
            return;
        }
        using var picker = new OpenFileDialog {
            Title = UiLocale.Display("Import private local SYSTEM SYSVOL report"),
            Filter = "JSON (*.json)|*.json", CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _sysvolSystem = DomainSysvolSystemEvidence.LoadLocalJson(
                picker.FileName, _report.Domain, _report.Dc, computer,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _sysvolSystem = null;
            UiErrorDialog.Show(this, "Import SYSTEM SYSVOL evidence", ex);
        }
        Review();
    }

    private async Task VerifyAdScopeAsync()
    {
        if (_reading || _readAdScope is null) return;
        string computer = _computer.Text.Trim();
        string ou = SelectedOu;
        string user = _user.Text.Trim();
        if (!NativeGpoConflictScan.SafeComputerName(computer, _report.Domain) ||
            !_report.ComputerOus.Contains(ou, StringComparer.OrdinalIgnoreCase) ||
            user.Length > 256 || user.Any(char.IsControl) ||
            user.Contains('\\') || user.Contains('/') ||
            user.Contains('*') || user.Contains('(') || user.Contains(')'))
        {
            Review();
            return;
        }
        _reading = true;
        _verifyAdScope.Enabled = false;
        _verifyPower.Enabled = false;
        _review.Enabled = false;
        _reviewNamedGpos.Enabled = false;
        _importEndpoint.Enabled = false;
        _importSysvol.Enabled = false;
        _clearSysvol.Enabled = false;
        _captureEndpoint.Enabled = false;
        _clearEndpoint.Enabled = false;
        _computer.Enabled = false;
        _user.Enabled = false;
        _ou.Enabled = false;
        _summary.Text = UiLocale.Display("Reading selected DC OU/group/ACL and loopback (read-only)...");
        try
        {
            _adScope = await _readAdScope(computer, ou, user);
            if (!_adScope.Computer.Equals(computer, StringComparison.OrdinalIgnoreCase) ||
                !_adScope.ComputerOu.Equals(ou, StringComparison.OrdinalIgnoreCase) ||
                !_adScope.RequestedUser.Equals(user, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "AD scope report does not match the currently selected pilot.");
        }
        catch (Exception ex)
        {
            _adScope = null;
            UiErrorDialog.Show(this, "Analyze AD scope (read-only)", ex);
        }
        finally
        {
            _reading = false;
            _review.Enabled = true;
            _importEndpoint.Enabled = true;
            _importSysvol.Enabled = true;
            _captureEndpoint.Enabled = true;
            _computer.Enabled = true;
            _user.Enabled = true;
            _ou.Enabled = true;
            Review();
        }
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
        _verifyAdScope.Enabled = false;
        _review.Enabled = false;
        _reviewNamedGpos.Enabled = false;
        _importEndpoint.Enabled = false;
        _importSysvol.Enabled = false;
        _clearSysvol.Enabled = false;
        _clearEndpoint.Enabled = false;
        _captureEndpoint.Enabled = false;
        _computer.Enabled = false;
        _user.Enabled = false;
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
            _importEndpoint.Enabled = true;
            _importSysvol.Enabled = true;
            _captureEndpoint.Enabled = true;
            _computer.Enabled = true;
            _user.Enabled = true;
            _ou.Enabled = true;
            Review();
        }
    }
}
