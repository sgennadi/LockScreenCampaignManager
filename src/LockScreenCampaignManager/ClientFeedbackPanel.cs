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
    private readonly Label _message = new();
    private readonly DataGridView _grid = new();
    private readonly CheckBox _auto = new();
    private Button _repairButton = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private List<string> _expected = new();
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
            _message.Text = "Campaign changed. Load its reports and refresh the AD computer list.";
        }
    }

    private static Button Action(string text, Action action, int width)
    {
        var b = new Button { Text = text, Width = width, Height = 39,
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        Controls.Add(layout);
        var config = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
        _root.Name = "ClientHealthSmbPath";
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
        var foot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        foot.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        foot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _totals.Dock = DockStyle.Fill;
        _totals.AutoEllipsis = false;
        _totals.Padding = new Padding(4, 7, 4, 7);
        _totals.Font = new Font("Segoe UI Semibold", 10f);
        _totals.Text = "Load computers from OU + Security Group to calculate fleet totals.";
        _message.Dock = DockStyle.Fill;
        _message.AutoEllipsis = false;
        _message.Padding = new Padding(4, 6, 4, 6);
        _message.ForeColor = UiStyle.Muted;
        _message.Text = "No data. Reports are diagnostic; Healthy does not prove the image is visible.";
        foot.Controls.Add(_totals, 0, 0);
        foot.Controls.Add(_message, 0, 1);
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
            Description = "Select the dedicated SMB share root, not a local directory.",
            UseDescriptionForTitle = true, ShowNewFolderButton = false
        };
        if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
        try { _root.Text = FeedbackPath.ValidateRoot(picker.SelectedPath); }
        catch (ArgumentException)
        {
            MessageBox.Show(FindForm(), "Select a UNC share root: \\\\server\\share$",
                "SMB share root required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task LoadExpectedAsync()
    {
        if (_busy) return;
        _busy = true;
        _message.Text = "Reading computer objects in OU and Security Group...";
        try
        {
            var result = await Task.Run(() => DomainPowerShellRunner.Execute(
                DomainOperation.Immediate, "List",
                new { domain = _domain, dc = _dc, ouDn = _ouDn, groupDn = _groupDn },
                _credentials, 240_000));
            if (IsDisposed) return;
            var data = result.GetProperty("data");
            var hosts = data.EnumerateArray().Select(x => (x.GetProperty("name").GetString() ?? "").Split('.')[0])
                .Where(FeedbackPath.IsValidHost).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _expected = hosts;
            _message.Text = $"Found {hosts.Count} computers in the OU + Security Group intersection.";
        }
        catch (Exception ex) { if (!IsDisposed) UiErrorDialog.Show(FindForm(), "Load eligible computers", ex); }
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
            int maxAge = Math.Clamp(_project.FeedbackIntervalMinutes * 3, 45, 180);
            var snapshot = await Task.Run(() => ClientFeedbackReader.Read(root, id, names,
                DateTimeOffset.UtcNow, maxAge));
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
            _totals.Text = $"Expected (AD): {(snapshot.Expected == 0 ? "not loaded" : snapshot.Expected)} | " +
                           $"Healthy: {snapshot.Healthy} | Ready: {snapshot.Ready} | " +
                           $"Error: {snapshot.Errors} | Stale: {snapshot.Stale} | " +
                           $"No report: {snapshot.NoReport} | Completed: {snapshot.Completed}";
            _message.Text = $"Updated {DateTime.Now:HH:mm:ss}; invalid/foreign JSON: {snapshot.InvalidReports}. " +
                            "Reports older than three intervals are marked stale.";
            string? alert = _alertEngine.Observe(snapshot, _alertPreferences, DateTimeOffset.UtcNow);
            if (!string.IsNullOrWhiteSpace(alert)) ShowLocalAlert(alert);
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _message.Text = "Could not read client feedback: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 240);
        }
        finally { _busy = false; }
    }

    private void ShowLocalAlert(string message)
    {
        if (IsDisposed) return;
        _notify.Visible = true;
        _notify.BalloonTipTitle = "LockScreen — increasing errors";
        _notify.BalloonTipText = message.Length > 230 ? message[..230] : message;
        _notify.BalloonTipIcon = ToolTipIcon.Warning;
        _notify.ShowBalloonTip(6000);
        _message.Text = "WARNING: " + message;
    }

    private void ShowAlertSettings()
    {
        using var dialog = new ClientAlertSettingsDialog(_alertPreferences);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        _alertPreferences = dialog.Result;
        _alertEngine.Reset();
        _message.Text = _alertPreferences.Enabled
            ? "Alerts enabled. The next snapshot establishes a baseline."
            : "Alerts disabled. No automatic endpoint changes.";
    }

    private async Task RepairSelectedAsync()
    {
        if (_busy || _requestRepair is null) return;
        if (_expected.Count == 0)
        {
            MessageBox.Show(FindForm(), "Click Load PCs from AD first. " +
                "Repair requires a verified OU + Security Group scope.",
                "Repair Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string[] selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.Tag as string ?? "")
            .Where(FeedbackPath.IsValidHost)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selected.Length < 1 || selected.Length > 25)
        {
            MessageBox.Show(FindForm(), "Select 1–25 computer rows (Ctrl/Shift to select multiple).",
                "Repair Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var allowed = new HashSet<string>(_expected, StringComparer.OrdinalIgnoreCase);
        if (selected.Any(x => !allowed.Contains(x)))
        {
            MessageBox.Show(FindForm(), "A selected computer is outside the verified OU and group.",
                "Repair Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        if (_snapshot is null) { _message.Text = "Refresh SMB feedback first."; return; }
        try
        {
            var record = CampaignHistoryStore.SaveHealth(_snapshot, _domain, _ouDn, _groupDn, final: false);
            _message.Text = "Local snapshot saved: " + record.OccurredUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
        }
        catch (Exception ex) { UiErrorDialog.Show(FindForm(), "Save Client Health snapshot", ex); }
    }

    private void ExportFinalReport()
    {
        if (_snapshot is null) { _message.Text = "Refresh SMB feedback first."; return; }
        if (_snapshot.Expected == 0)
        {
            MessageBox.Show(FindForm(), "Load the AD scope before exporting the final report, " +
                "otherwise the total number of target computers is unknown.", "Final report",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        int unresolved = _snapshot.Errors + _snapshot.Stale + _snapshot.NoReport + _snapshot.Ready + _snapshot.Healthy;
        if (unresolved > 0 && MessageBox.Show(FindForm(),
            $"There are {unresolved} computers without a completion report. " +
            "Save a final report snapshot as of now? This does NOT confirm full completion.",
            "Final report confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        using var dialog = new SaveFileDialog { Filter = "HTML (*.html)|*.html",
            Title = "Save final report", FileName =
            $"LSCM-Final-{_snapshot.CampaignId}-{DateTime.Now:yyyyMMdd-HHmm}.html" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var record = CampaignHistoryStore.SaveHealth(_snapshot, _domain, _ouDn, _groupDn, final: true);
            File.WriteAllText(dialog.FileName, CampaignHistoryStore.HtmlReport(record), new UTF8Encoding(true));
            _message.Text = "Final snapshot saved to history and exported as HTML.";
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
            if (MessageBox.Show(FindForm(), "Create feedback folder?\n" + path +
                "\n\nConfigure the share ACL separately. The application does not change permissions.",
                "SMB Feedback", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Directory.CreateDirectory(path);
            _message.Text = "Folder ready. Verify Domain Computers ACLs and administrator access.";
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
            _message.Text = "Exported: " + _snapshot.Rows.Count + " rows.";
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
