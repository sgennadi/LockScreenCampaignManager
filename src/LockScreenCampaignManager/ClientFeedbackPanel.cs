using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>Client health view: reads untrusted SMB hints, archives snapshots and
/// asks the parent to perform a separately validated/confirmed remote Repair.
/// The panel itself never edits AD, GPOs or client files.</summary>
internal sealed class ClientFeedbackPanel : UserControl
{
    private readonly string _domain;
    private readonly string _dc;
    private readonly string _ouDn;
    private readonly string _groupDn;
    private readonly DomainCredentials _credentials;
    private readonly CampaignProject _project;
    private readonly Func<IReadOnlyList<string>, Task>? _requestRepair;
    private readonly ClientAlertEngine _alertEngine = new();
    private ClientAlertPreferences _alertPreferences = ClientAlertPreferences.Load();
    private readonly NotifyIcon _notify = new();
    private readonly TextBox _root = new();
    private readonly TextBox _campaignId = new();
    private readonly Label _totals = new();
    private readonly Label _coverage = new() { Name = "ClientHealthAdReportCoverage" };
    private readonly Label _message = new();
    // Display-only AD age filter, never a change to GPO, group or Repair targets.
    private readonly ComboBox _activityWindow = new() { Name = "ClientHealthAdActivityWindow" };
    private readonly DataGridView _grid = new();
    private readonly CheckBox _auto = new();
    private Button _repairButton = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private List<string> _expected = new();
    private ClientHealthAdInventory? _adInventory;
    private ClientHealthSnapshot? _snapshot;
    private bool _busy;

    public ClientFeedbackPanel(string domain, string dc, string ouDn, string groupDn,
        DomainCredentials credentials, CampaignProject project,
        Func<IReadOnlyList<string>, Task>? requestRepair = null)
    {
        _domain = domain;
        _dc = dc;
        _ouDn = ouDn;
        _groupDn = groupDn;
        _credentials = credentials;
        _project = project;
        _requestRepair = requestRepair;
        _notify.Icon = SystemIcons.Warning;
        _notify.Text = "LockScreen Campaign Manager";
        _notify.Visible = false;
        Dock = DockStyle.Fill;
        BackColor = UiStyle.Canvas;
        Build();
        _timer.Interval = 15_000;
        _timer.Tick += async (_, _) =>
        {
            if (_auto.Checked && Visible && !_busy) await RefreshAsync();
        };
        _timer.Start();
        Disposed += (_, _) => { _timer.Stop(); _timer.Dispose(); _notify.Visible = false; _notify.Dispose(); };
    }

    public TabPage AsTabPage()
    {
        var tab = new TabPage("Client Health / Live") { BackColor = UiStyle.Canvas };
        tab.Controls.Add(this);
        return tab;
    }

    public string SelectedCampaignId => _campaignId.Text.Trim();
    internal string FeedbackShareRoot => _root.Text.Trim();

    public void SetSelectedManagedCampaign(string? campaignId)
    {
        if (!string.IsNullOrWhiteSpace(campaignId) &&
            !string.Equals(_campaignId.Text, campaignId, StringComparison.Ordinal))
        {
            _campaignId.Text = campaignId;
            _snapshot = null;
            _alertEngine.Reset();
            _grid.Rows.Clear();
            _message.Text = UiLocale.Display("Campaign changed. Load its reports and refresh the AD computer list.");
        }
    }

    private static Button Action(string text, Action action, int width)
    {
        var b = new Button { Text = UiLocale.Display(text), Width = width, Height = 39,
            MinimumSize = new Size(width, 39), AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            Margin = new Padding(0, 5, 8, 6), BackColor = UiStyle.Surface,
            ForeColor = UiStyle.Foreground, FlatStyle = FlatStyle.Flat };
        b.Click += (_, _) => action();
        return b;
    }

    private void Build()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(13), BackColor = UiStyle.Canvas };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 164));
        Controls.Add(layout);
        var config = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
        _root.Name = "ClientHealthSmbPath";
        _root.RightToLeft = _campaignId.RightToLeft = RightToLeft.No;
        _root.Width = 430;
        _root.Height = 42;
        _root.Multiline = true;
        _root.WordWrap = false;
        _root.ScrollBars = ScrollBars.Horizontal;
        _root.AcceptsReturn = false;
        _root.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) e.SuppressKeyPress = true;
        };
        _root.Text = _project.FeedbackShare;
        _root.PlaceholderText = @"\\fileserver\LockScreenReports$";
        _campaignId.Width = 305;
        _campaignId.Text = CampaignSchedule.BuildId(_project);
        config.Controls.Add(new Label { Text = "SMB Share", AutoSize = true, Margin = new Padding(0, 17, 8, 0) });
        config.Controls.Add(_root);
        config.Controls.Add(Action("Browse SMB...", BrowseShare, 155));
        config.Controls.Add(Action("Copy path", CopyShare, 128));
        config.Controls.Add(new Label { Text = "Campaign ID", AutoSize = true, Margin = new Padding(8, 17, 8, 0) });
        config.Controls.Add(_campaignId);
        _auto.Text = "Auto refresh (15 sec)";
        _auto.AutoSize = true;
        _auto.Checked = true;
        _auto.Margin = new Padding(10, 14, 3, 0);
        config.Controls.Add(_auto);
        _activityWindow.DropDownStyle = ComboBoxStyle.DropDownList;
        _activityWindow.Width = 155;
        _activityWindow.DropDownWidth = 260;
        _activityWindow.Items.AddRange(
            ["Last 30 days", "Last 90 days", "Last 180 days", "Last 365 days"]);
        _activityWindow.SelectedIndex = 1;
        _activityWindow.SelectedIndexChanged += (_, _) => UpdateAdReportCoverage();
        new ToolTip().SetToolTip(_activityWindow, UiLocale.Display(
            "AD lastLogonTimestamp is approximate; activity window never filters deployment or Repair."));
        config.Controls.Add(_activityWindow);
        layout.Controls.Add(config, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        buttons.Controls.Add(Action("Load PCs from AD", () => _ = LoadExpectedAsync(), 225));
        buttons.Controls.Add(Action("Refresh", () => _ = RefreshAsync(), 118));
        buttons.Controls.Add(Action("Export CSV", ExportCsv, 138));
        buttons.Controls.Add(Action("Create folder", CreateReportFolder, 158));
        _repairButton = Action("Repair Selected", () => _ = RepairSelectedAsync(), 171);
        _repairButton.Enabled = _requestRepair is not null;
        buttons.Controls.Add(_repairButton);
        buttons.Controls.Add(Action("Alerts", ShowAlertSettings, 144));
        buttons.Controls.Add(Action("Save snapshot", SaveSnapshot, 172));
        buttons.Controls.Add(Action("Final report", ExportFinalReport, 166));
        buttons.Controls.Add(Action("History", OpenHistory, 115));
        layout.Controls.Add(buttons, 0, 1);
        _grid.Name = "ClientHealthGrid";
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ColumnHeadersHeight = Math.Max(_grid.ColumnHeadersHeight, 45);
        _grid.RowTemplate.Height = Math.Max(_grid.RowTemplate.Height, 40);
        _grid.ScrollBars = ScrollBars.Both;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Computer", FillWeight = 128 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Condition", FillWeight = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Stage", FillWeight = 75 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "JPG", FillWeight = 60 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Last seen", FillWeight = 102 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Files", FillWeight = 95 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Registry", FillWeight = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Task", FillWeight = 48 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Agent version", FillWeight = 104 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Client type", FillWeight = 92 });
        foreach (DataGridViewColumn column in _grid.Columns)
            column.MinimumWidth = Math.Max(94, (int)column.FillWeight);
        layout.Controls.Add(_grid, 0, 2);
        var foot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
            RowCount = 3, AutoScroll = true };
        foot.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        foot.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        foot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _totals.Name = "ClientHealthTotals";
        _totals.Dock = DockStyle.Fill;
        _totals.AutoEllipsis = false;
        _totals.Padding = new Padding(4, 7, 4, 7);
        _totals.Font = new Font("Segoe UI Semibold", 10f);
        _totals.Text = "Load computers from OU + Security Group to calculate fleet totals.";
        _coverage.Dock = DockStyle.Fill;
        _coverage.AutoEllipsis = false;
        _coverage.ForeColor = UiStyle.Navy;
        _coverage.Padding = new Padding(4, 4, 4, 4);
        _coverage.Text = UiLocale.Display("AD scope not loaded. Agent reports are untrusted diagnostics, not proof of online status.");
        _message.Name = "ClientHealthStatus";
        _message.Dock = DockStyle.Fill;
        _message.AutoEllipsis = false;
        _message.Padding = new Padding(4, 6, 4, 6);
        _message.ForeColor = UiStyle.Muted;
        _message.Text = "No data. Reports are diagnostic; Healthy does not prove the image is visible.";
        foot.Controls.Add(_totals, 0, 0);
        foot.Controls.Add(_coverage, 0, 1);
        foot.Controls.Add(_message, 0, 2);
        layout.Controls.Add(foot, 0, 3);
    }

    private void CopyShare()
    {
        if (!string.IsNullOrWhiteSpace(_root.Text))
            try { Clipboard.SetText(_root.Text.Trim()); }
            catch (Exception ex) { UiErrorDialog.Show(FindForm(), "Copy SMB share", ex); }
    }

    private void BrowseShare()
    {
        using var picker = new FolderBrowserDialog
        {
            Description = UiLocale.Display("Select the dedicated SMB share root, not a local directory."),
            UseDescriptionForTitle = true, ShowNewFolderButton = false
        };
        if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
        try { _root.Text = FeedbackPath.ValidateRoot(picker.SelectedPath); }
        catch (ArgumentException)
        {
            MessageBox.Show(FindForm(), UiLocale.Display("Select a UNC share root: ") +
                @"\\server\share$", UiLocale.Display("SMB share root required"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task LoadExpectedAsync()
    {
        if (_busy) return;
        _busy = true;
        _expected.Clear();
        _adInventory = null;
        _snapshot = null;
        _grid.Rows.Clear();
        _totals.Text = UiLocale.Display("Expected (AD): not loaded");
        UpdateAdReportCoverage();
        _message.Text = UiLocale.Display("Reading computer objects in OU and Security Group...");
        try
        {
            var result = await Task.Run(() => DomainPowerShellRunner.Execute(
                DomainOperation.Immediate, "Inventory",
                new { domain = _domain, dc = _dc, ouDn = _ouDn, groupDn = _groupDn },
                _credentials, 240_000));
            if (IsDisposed) return;
            var inventory = ClientHealthAdInventory.Parse(result.GetProperty("data"),
                _domain, DateTimeOffset.UtcNow);
            _adInventory = inventory;
            _expected = inventory.ExpectedHosts.OrderBy(x => x,
                StringComparer.OrdinalIgnoreCase).ToList();
            _message.Text = string.Format(UiLocale.Display(
                "AD inventory verified: OU {0} computer objects; scoped group {1}. Read-only; not effective GPO targeting."),
                inventory.OuTotal, inventory.GroupTotal);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _message.Text = UiLocale.Display(
                    "AD inventory not verified. Refresh AD before Repair or coverage comparisons.");
                UiErrorDialog.Show(FindForm(), "Load eligible computers", ex);
            }
        }
        finally { _busy = false; }
        if (!IsDisposed) await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            string root = FeedbackPath.ValidateRoot(_root.Text);
            string id = _campaignId.Text.Trim();
            string[] names = _expected.ToArray();
            bool verified = _adInventory is not null && _adInventory.IsFresh(DateTimeOffset.UtcNow);
            int maxAge = Math.Clamp(_project.FeedbackIntervalMinutes * 3, 45, 180);
            var snapshot = await Task.Run(() => ClientFeedbackReader.Read(root, id, names,
                DateTimeOffset.UtcNow, maxAge, expectedScopeVerified: verified));
            if (IsDisposed) return;
            _snapshot = snapshot;
            _grid.SuspendLayout();
            try
            {
                _grid.Rows.Clear();
                foreach (var row in snapshot.Rows)
                {
                    int idx = _grid.Rows.Add(row.Computer, row.Condition, row.Stage, row.Image,
                        row.LastSeen, row.Files, row.Registry, row.Task, row.Version, row.ClientType);
                    _grid.Rows[idx].Tag = row.Computer;
                    if (row.Condition is "Needs attention" or "Error")
                        _grid.Rows[idx].DefaultCellStyle.ForeColor = Color.DarkRed;
                    else if (row.Condition == "Healthy")
                        _grid.Rows[idx].DefaultCellStyle.ForeColor = Color.DarkGreen;
                }
                // Repair needs an explicit operator selection, not an auto-selected first row.
                _grid.ClearSelection();
                _grid.CurrentCell = null;
            }
            finally { _grid.ResumeLayout(); }
            _totals.Text = UiLocale.Display("Expected (AD): ") +
                (_adInventory is null ? UiLocale.Display("not loaded") : snapshot.Expected.ToString()) + " | " +
                UiLocale.Display("Healthy: ") + snapshot.Healthy + " | " +
                UiLocale.Display("Ready: ") + snapshot.Ready + " | " +
                UiLocale.Display("Error: ") + snapshot.Errors + " | " +
                UiLocale.Display("Stale: ") + snapshot.Stale + " | " +
                UiLocale.Display("No report: ") + snapshot.NoReport + " | " +
                UiLocale.Display("Completed: ") + snapshot.Completed;
            UpdateAdReportCoverage();
            _message.Text = UiLocale.Display("Updated ") +
                DateTime.Now.ToString("HH:mm:ss") +
                UiLocale.Display("; invalid/foreign JSON: ") + snapshot.InvalidReports +
                ". " + UiLocale.Display("Reports older than three intervals are marked stale.");
            string? alert = _alertEngine.Observe(snapshot, _alertPreferences, DateTimeOffset.UtcNow);
            if (!string.IsNullOrWhiteSpace(alert)) ShowLocalAlert(alert);
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _message.Text = UiLocale.Display("Could not read client feedback: ") +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 240);
        }
        finally { _busy = false; }
    }

    private void UpdateAdReportCoverage()
    {
        if (_adInventory is null)
        {
            _coverage.Text = UiLocale.Display(
                "AD scope not loaded. Agent reports are untrusted diagnostics, not proof of online status.");
            return;
        }
        int days = _activityWindow.SelectedIndex switch
        {
            0 => 30, 1 => 90, 2 => 180, 3 => 365, _ => 90
        };
        int maxAge = Math.Clamp(_project.FeedbackIntervalMinutes * 3, 45, 180);
        var report = _adInventory.Summarize(_snapshot, _campaignId.Text.Trim(),
            days, DateTimeOffset.UtcNow, maxAge);
        string ad = string.Format(UiLocale.Display(
            "AD OU: {0} | OU + group: {1} | Enabled: {2} | Disabled: {3} | State unknown: {4} | AD last-seen <= {5} days: {6} | No timestamp: {7}"),
            report.OuTotal, report.GroupTotal, report.Enabled, report.Disabled,
            report.UnknownAccountState, report.Days, report.RecentlySeenAd, report.UnknownLogon);
        string smb = report.ReportsVerified
            ? string.Format(UiLocale.Display(
                "Agent JSON received: {0}/{1} | Fresh: {2} | Stale reports: {3} | No report: {4}. Reports are untrusted hints, not online proof."),
                report.Reported, report.GroupTotal, report.FreshReports,
                report.StaleReports, report.NoReport)
            : UiLocale.Display(
                "Agent comparison not verified (reload AD, campaign and SMB reports). No zero coverage claimed.");
        _coverage.Text = ad + Environment.NewLine + smb;
    }

    private void ShowLocalAlert(string message)
    {
        if (IsDisposed) return;
        _notify.Visible = true;
        _notify.BalloonTipTitle = UiLocale.Display("LockScreen — increasing errors");
        _notify.BalloonTipText = message.Length > 230 ? message[..230] : message;
        _notify.BalloonTipIcon = ToolTipIcon.Warning;
        _notify.ShowBalloonTip(6000);
        _message.Text = UiLocale.Display("WARNING: ") + message;
    }

    private void ShowAlertSettings()
    {
        using var dialog = new ClientAlertSettingsDialog(_alertPreferences);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        _alertPreferences = dialog.Result;
        _alertEngine.Reset();
        _message.Text = UiLocale.Display(_alertPreferences.Enabled
            ? "Alerts enabled. The next snapshot establishes a baseline."
            : "Alerts disabled. No automatic endpoint changes.");
    }

    private async Task RepairSelectedAsync()
    {
        if (_busy || _requestRepair is null) return;
        if (_adInventory is null || !_adInventory.IsFresh(DateTimeOffset.UtcNow) ||
            _expected.Count == 0)
        {
            MessageBox.Show(FindForm(), UiLocale.Display("Click Load PCs from AD first. " +
                "Repair requires a verified OU + Security Group scope."),
                UiLocale.Display("Repair Selected"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string[] selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.Tag as string ?? "")
            .Where(FeedbackPath.IsValidHost)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selected.Length < 1 || selected.Length > 25)
        {
            MessageBox.Show(FindForm(), UiLocale.Display("Select 1–25 computer rows (Ctrl/Shift to select multiple)."),
                UiLocale.Display("Repair Selected"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var allowed = new HashSet<string>(_expected, StringComparer.OrdinalIgnoreCase);
        if (selected.Any(x => !allowed.Contains(x)))
        {
            MessageBox.Show(FindForm(), UiLocale.Display("A selected computer is outside the verified OU and group."),
                UiLocale.Display("Repair Selected"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_snapshot is null || !string.Equals(_snapshot.CampaignId, SelectedCampaignId, StringComparison.Ordinal))
            return;
        _busy = true;
        _repairButton.Enabled = false;
        try
        {
            await _requestRepair(selected);
        }
        catch (Exception ex) { UiErrorDialog.Show(FindForm(), "Repair Selected Computers", ex); }
        finally { _busy = false; _repairButton.Enabled = true; }
        if (!IsDisposed) await RefreshAsync();
    }

    private void SaveSnapshot()
    {
        if (_snapshot is null) { _message.Text = UiLocale.Display("Refresh SMB feedback first."); return; }
        try
        {
            var record = CampaignHistoryStore.SaveHealth(_snapshot, _domain, _ouDn, _groupDn, final: false);
            _message.Text = UiLocale.Display("Local snapshot saved: ") + record.OccurredUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
        }
        catch (Exception ex) { UiErrorDialog.Show(FindForm(), "Save Client Health snapshot", ex); }
    }

    private void ExportFinalReport()
    {
        if (_snapshot is null) { _message.Text = UiLocale.Display("Refresh SMB feedback first."); return; }
        if (_snapshot.Expected == 0)
        {
            MessageBox.Show(FindForm(), UiLocale.Display("Load the AD scope before exporting the final report, " +
                "otherwise the total number of target computers is unknown."), UiLocale.Display("Final report"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        int unresolved = _snapshot.Errors + _snapshot.Stale + _snapshot.NoReport + _snapshot.Ready + _snapshot.Healthy;
        if (unresolved > 0 && MessageBox.Show(FindForm(),
            UiLocale.Display("There are ") + unresolved +
            UiLocale.Display(" computers without a completion report. Save a final report snapshot as of now? This does NOT confirm full completion."),
            UiLocale.Display("Final report confirmation"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        using var dialog = new SaveFileDialog { Filter = "HTML (*.html)|*.html",
            Title = UiLocale.Display("Save final report"), FileName =
            $"LSCM-Final-{_snapshot.CampaignId}-{DateTime.Now:yyyyMMdd-HHmm}.html" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var record = CampaignHistoryStore.SaveHealth(_snapshot, _domain, _ouDn, _groupDn, final: true);
            File.WriteAllText(dialog.FileName, CampaignHistoryStore.HtmlReport(record), new UTF8Encoding(true));
            _message.Text = UiLocale.Display("Final snapshot saved to history and exported as HTML.");
        }
        catch (Exception ex) { UiErrorDialog.Show(FindForm(), "Final campaign report", ex); }
    }

    private void OpenHistory()
    {
        using var form = new CampaignHistoryForm();
        form.ShowDialog(FindForm());
    }

    private void CreateReportFolder()
    {
        try
        {
            string path = FeedbackPath.CampaignDirectory(_root.Text, _campaignId.Text.Trim());
            if (MessageBox.Show(FindForm(), UiLocale.Display("Create feedback folder?\n\n") + path +
                "\n\n" + UiLocale.Display("Configure the share ACL separately. The application does not change permissions."),
                UiLocale.Display("SMB Feedback"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Directory.CreateDirectory(path);
            _message.Text = UiLocale.Display("Folder ready. Verify Domain Computers ACLs and administrator access.");
        }
        catch (Exception ex) { UiErrorDialog.Show(FindForm(), "Create feedback folder", ex); }
    }

    private void ExportCsv()
    {
        if (_snapshot is null) return;
        using var dialog = new SaveFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", FileName =
            "LSCM-ClientHealth-" + _snapshot.CampaignId + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var builder = new StringBuilder("Computer,Condition,Stage,Image,LastSeen,Files,Registry,Task,AgentVersion,ClientType\r\n");
            foreach (var row in _snapshot.Rows)
                builder.AppendLine(string.Join(",", new[] { row.Computer, row.Condition, row.Stage,
                    row.Image, row.LastSeen, row.Files, row.Registry, row.Task, row.Version, row.ClientType }
                    .Select(CsvValue)));
            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            _message.Text = UiLocale.Display("Exported: ") + _snapshot.Rows.Count + UiLocale.Display(" rows.");
        }
        catch (Exception ex) { UiErrorDialog.Show(FindForm(), "Export Client Health", ex); }
    }
    private static string CsvValue(string value)
    {
        // Avoid CSV formula injection if an unexpected diagnostic string reaches Excel.
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
