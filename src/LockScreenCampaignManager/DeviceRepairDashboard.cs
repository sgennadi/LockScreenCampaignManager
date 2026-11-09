using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only consolidation of verified AD computers, Client Health SMB hints,
/// protected Repair queue states and opt-in local campaign history. The ONLY
/// write action delegates to the existing Preview + full GUID confirmation
/// pipeline in GpoOperationsForm. No new RPC endpoint, privilege, or approval.
/// </summary>
internal sealed class DeviceRepairDashboard : UserControl
{
    private readonly string _domain;
    private readonly string _dc;
    private readonly string _ouDn;
    private readonly string _groupDn;
    private readonly DomainCredentials _credentials;
    private readonly CampaignProject _project;
    private readonly Func<(string CampaignId, string GpoId)> _selectedPolicy;
    private readonly Func<string> _currentShare;
    private readonly Func<IReadOnlyList<string>, Task> _requestRepair;
    private readonly Action _openClientHealth;

    private readonly Label _headline = new();
    private readonly Label _summary = new();
    private readonly Label _status = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _details = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _filter = new();
    private readonly CheckBox _auto = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private Button _refresh = new();
    private Button _load = new();
    private Button _repair = new();
    private Button _save = new();
    private Button _closure = new();
    private TabPage? _tab;

    private IReadOnlyList<string> _verified = Array.Empty<string>();
    private DeviceDashboardSnapshot? _snapshot;
    private bool _busy;
    private bool _drawing;
    private int _detailRequest;
    private string _verifiedCampaign = "";
    private string _verifiedGpo = "";

    internal DeviceRepairDashboard(string domain, string dc, string ouDn, string groupDn,
        DomainCredentials credentials, CampaignProject project,
        Func<(string CampaignId, string GpoId)> selectedPolicy,
        Func<string> currentShare,
        Func<IReadOnlyList<string>, Task> requestRepair,
        Action openClientHealth)
    {
        _domain = domain;
        _dc = dc;
        _ouDn = ouDn;
        _groupDn = groupDn;
        _credentials = credentials;
        _project = project;
        _selectedPolicy = selectedPolicy;
        _currentShare = currentShare;
        _requestRepair = requestRepair;
        _openClientHealth = openClientHealth;
        Dock = DockStyle.Fill;
        BackColor = UiStyle.Canvas;
        Build();
        _timer.Interval = 60_000;
        _timer.Tick += (_, _) => {
            if (_auto.Checked && _tab?.Parent is TabControl tabs &&
                tabs.SelectedTab == _tab && !_busy && HasValidSnapshot())
                _ = RefreshReportsAsync();
        };
        Disposed += (_, _) => { _timer.Stop(); _timer.Dispose(); };
    }

    internal TabPage AsTabPage()
    {
        _tab = new TabPage("Device & Repair Dashboard") { BackColor = UiStyle.Canvas };
        _tab.Controls.Add(this);
        _tab.Enter += (_, _) => UpdateTimer();
        _tab.Leave += (_, _) => _timer.Stop();
        return _tab;
    }

    internal void SetManagedCampaign(string? campaignId, string? gpoId)
    {
        if (_snapshot is not null &&
            string.Equals(_snapshot.CampaignId, campaignId, StringComparison.Ordinal) &&
            string.Equals(_snapshot.GpoId, gpoId, StringComparison.OrdinalIgnoreCase))
            return;
        _verified = Array.Empty<string>();
        _verifiedCampaign = _verifiedGpo = "";
        _snapshot = null;
        _detailRequest++;
        _grid.Rows.Clear();
        _details.Text = "Load verified computers before reviewing per-device reports.";
        _headline.Text = string.IsNullOrWhiteSpace(campaignId)
            ? "Choose a managed GPO in the GPO tab."
            : "Selected campaign: " + campaignId + " | Read-only until AD scope is verified.";
        _summary.Text = "No current device data. Health reports are not proof of on-screen display.";
        _status.Text = "Click Load verified AD devices, then Refresh reports.";
        SetButtons();
    }

    private void Build()
    {
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
            Padding = new Padding(12), BackColor = UiStyle.Canvas, AutoScroll = true
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        Controls.Add(layout);

        var title = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        title.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        title.Controls.Add(new Label {
            Text = "Device & Repair Dashboard", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 16f),
            ForeColor = UiStyle.Foreground
        }, 0, 0);
        _headline.Dock = DockStyle.Fill;
        _headline.ForeColor = UiStyle.Muted;
        _headline.AutoEllipsis = true;
        _headline.Text = "Choose a managed GPO and load the authorized AD devices.";
        title.Controls.Add(_headline, 0, 1);
        layout.Controls.Add(title, 0, 0);

        var commands = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true
        };
        _load = Button("Load verified AD devices", 211, () => _ = LoadVerifiedAsync());
        _refresh = Button("Refresh reports", 145, () => _ = RefreshReportsAsync());
        _repair = Button("Repair selected", 157, () => _ = RepairSelectedAsync());
        _save = Button("Save snapshot", 149, SaveSnapshot);
        _closure = Button("Closure audit", 155, () => _ = OpenClosureAuditAsync());
        commands.Controls.AddRange(new Control[] {
            _load, _refresh, _repair, _save, _closure,
            Button("Client Health", 142, _openClientHealth),
            Button("History", 110, OpenHistory),
            Button("Repair history", 148, OpenRepairHistory)
        });
        commands.Controls.Add(new Label {
            Text = "Filter:", AutoSize = true, Margin = new Padding(8, 15, 7, 0)
        });
        _filter.DropDownStyle = ComboBoxStyle.DropDownList;
        _filter.Width = 150;
        _filter.Items.AddRange(new object[] {
            "All devices", "Needs attention", "Repair pending",
            "No report", "Healthy / completed"
        });
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => DrawRows();
        commands.Controls.Add(_filter);
        _search.PlaceholderText = "Search computer...";
        _search.Width = 175;
        _search.Margin = new Padding(4, 7, 8, 0);
        _search.TextChanged += (_, _) => DrawRows();
        commands.Controls.Add(_search);
        _auto.Text = "Auto-refresh (60 s)";
        _auto.AutoSize = true;
        _auto.Checked = false; // Large fleets: poll only with explicit opt-in.
        _auto.Margin = new Padding(10, 14, 0, 0);
        _auto.CheckedChanged += (_, _) => UpdateTimer();
        commands.Controls.Add(_auto);
        layout.Controls.Add(commands, 0, 1);

        _summary.Dock = DockStyle.Fill;
        _summary.AutoEllipsis = false;
        _summary.Padding = new Padding(5, 8, 5, 10);
        _summary.ForeColor = UiStyle.Foreground;
        _summary.Text = "Load verified AD devices. This view never sends RPC automatically.";
        layout.Controls.Add(_summary, 0, 2);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ColumnHeadersHeight = Math.Max(_grid.ColumnHeadersHeight, 45);
        _grid.RowTemplate.Height = Math.Max(_grid.RowTemplate.Height, 39);
        _grid.ScrollBars = ScrollBars.Both;
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Computer", FillWeight = 140
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Client Health", FillWeight = 130
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Stage", FillWeight = 77
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Expected image", FillWeight = 98
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Last report", FillWeight = 116
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Integrity", FillWeight = 109
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Agent", FillWeight = 92
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Repair queue", FillWeight = 152
        });
        foreach (DataGridViewColumn column in _grid.Columns)
            column.MinimumWidth = Math.Max(100, (int)column.FillWeight);
        _grid.SelectionChanged += (_, _) => {
            SetButtons();
            if (!_drawing) _ = LoadSelectedDetailsAsync();
        };
        layout.Controls.Add(_grid, 0, 3);

        _details.Dock = DockStyle.Fill;
        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.WordWrap = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.BackColor = UiStyle.Surface;
        _details.Text = "Select a verified computer to view fresh agent evidence, " +
            "pending Repair status and locally archived Client Health snapshots.";
        layout.Controls.Add(_details, 0, 4);

        _status.Dock = DockStyle.Fill;
        _status.AutoEllipsis = false;
        _status.Padding = new Padding(5, 9, 5, 12);
        _status.ForeColor = UiStyle.Muted;
        _status.Text = "No automatic Repair. A manual Repair always uses Preview and full GPO GUID confirmation.";
        layout.Controls.Add(_status, 0, 5);
        SetButtons();
    }

    private static Button Button(string text, int width, Action action)
    {
        var button = new Button {
            Text = text, Width = width, Height = 38,
            Margin = new Padding(0, 4, 8, 5),
            BackColor = UiStyle.Surface, ForeColor = UiStyle.Foreground,
            FlatStyle = FlatStyle.Flat
        };
        button.Click += (_, _) => action();
        return button;
    }

    private (string CampaignId, string GpoId)? CurrentSelection()
    {
        var pair = _selectedPolicy();
        if (string.IsNullOrWhiteSpace(pair.CampaignId) ||
            !Guid.TryParse(pair.GpoId, out Guid parsed))
            return null;
        return (pair.CampaignId, parsed.ToString("D"));
    }

    private bool HasValidSnapshot()
    {
        var current = CurrentSelection();
        return current.HasValue && _snapshot is not null &&
            string.Equals(current.Value.CampaignId, _snapshot.CampaignId,
                StringComparison.Ordinal) &&
            string.Equals(current.Value.GpoId, _snapshot.GpoId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_currentShare().Trim().TrimEnd('\\'), _snapshot.FeedbackShare,
                StringComparison.OrdinalIgnoreCase);
    }

    private bool HasVerifiedScope()
    {
        var current = CurrentSelection();
        return current.HasValue && _verified.Count > 0 &&
            string.Equals(_verifiedCampaign, current.Value.CampaignId,
                StringComparison.Ordinal) &&
            string.Equals(_verifiedGpo, current.Value.GpoId,
                StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateTimer()
    {
        if (IsDisposed || !_auto.Checked ||
            _tab?.Parent is not TabControl tabs || tabs.SelectedTab != _tab)
            _timer.Stop();
        else
            _timer.Start();
    }

    private void SetButtons()
    {
        if (IsDisposed) return;
        _load.Enabled = !_busy && CurrentSelection().HasValue;
        _refresh.Enabled = !_busy && HasVerifiedScope();
        _save.Enabled = !_busy && HasValidSnapshot();
        _closure.Enabled = !_busy && HasValidSnapshot();
        _repair.Enabled = !_busy && HasValidSnapshot() &&
            _grid.SelectedRows.Count is >= 1 and <= 25;
    }

    private async Task LoadVerifiedAsync()
    {
        if (_busy) return;
        var policy = CurrentSelection();
        if (policy is null)
        {
            _status.Text = "Choose a managed GPO first.";
            return;
        }
        try
        {
            _ = FeedbackPath.ValidateRoot(_currentShare());
        }
        catch (Exception ex)
        {
            _status.Text = "Configure an SMB Client Health share first: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 140);
            return;
        }
        // Discard any previous device state before starting new AD discovery;
        // stale rows must never look like a verified current scope.
        _snapshot = null;
        _grid.Rows.Clear();
        _summary.Text = "Verifying the selected AD scope...";
        _details.Text = "No current verified device snapshot.";
        _busy = true;
        SetButtons();
        _status.Text = "Discovering the selected OU and Security Group through the selected DC...";
        try
        {
            var names = await Task.Run(() => {
                var result = DomainPowerShellRunner.Execute(
                    DomainOperation.Immediate, "List",
                    new { domain = _domain, dc = _dc, ouDn = _ouDn, groupDn = _groupDn },
                    _credentials, 240_000);
                var data = result.GetProperty("data");
                if (data.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("AD discovery did not return an array.");
                return DeviceDashboardModel.ValidateAndNormalizeAdNames(
                    data.EnumerateArray().Select(x => x.GetProperty("name").GetString() ?? ""));
            });
            if (IsDisposed) return;
            if (CurrentSelection() != policy)
                throw new InvalidOperationException("Selected managed GPO changed during AD discovery.");
            _verified = names;
            _verifiedCampaign = policy.Value.CampaignId;
            _verifiedGpo = policy.Value.GpoId;
            _snapshot = null; // Do not combine new AD scope with old SMB data.
            if (names.Count == 0)
            {
                _headline.Text = "Campaign: " + policy.Value.CampaignId +
                    " | Verified AD scope: 0 eligible computers.";
                _summary.Text = "No target computers are present in this OU and Security Group.";
                _details.Text = "No eligible AD computer. No Client Health claim or Repair is inferred.";
                _status.Text = "The verified AD scope is empty; there is nothing to repair.";
                return;
            }
            _status.Text = $"Verified {names.Count} scoped AD computers. Loading Client Health...";
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _verified = Array.Empty<string>();
                _snapshot = null;
                _status.Text = "AD verification failed: " +
                    UniversalDiagnosticsEmbedded.Clean(ex.Message, 180);
                AppLog.Write(ex, "Device Dashboard AD verification");
            }
        }
        finally
        {
            _busy = false;
            if (!IsDisposed) SetButtons();
        }
        if (!IsDisposed && HasVerifiedScope())
            await RefreshReportsAsync();
    }

    private static (IReadOnlyList<DeferredRepairItem> Queue,
        IReadOnlyList<RepairHistoryEvent> Timeline) ReadScopedGmsaRepairState(
        string domain, string dc, string ouDn, string groupDn,
        string campaignId, string gpoId)
    {
        // A missing/invalid protected queue is not proof that no repair exists.
        // Do not read potentially privileged gMSA history without the same
        // verified admin/config scope as the operational queue.
        if (!File.Exists(GmsaRepairConfig.ConfigPath))
        {
            if (File.Exists(Path.Combine(GmsaRepairConfig.QueueDirectory, "queue.json")) ||
                (Directory.Exists(GmsaRepairConfig.QueueDirectory) &&
                Directory.EnumerateFiles(GmsaRepairConfig.QueueDirectory,
                    "repair-history-*.jsonl").Any()))
                throw new InvalidDataException(
                    "Protected gMSA Repair data exists without trusted configuration.");
            return ([], []);
        }
        var config = GmsaRepairConfig.TryReadForInspection() ??
            throw new InvalidDataException("Protected gMSA configuration cannot be audited.");
        if (!config.ScopeMatchesForInspection(domain, ouDn, groupDn))
            return ([], []);
        var records = GmsaRepairQueue.InspectReadOnly(config);
        var history = RepairHistoryJournal.ReadScope(GmsaRepairConfig.QueueDirectory,
            "gMSA", domain, dc, ouDn, groupDn, campaignId, gpoId, 250);
        return (records, history);
    }

    private async Task RefreshReportsAsync()
    {
        if (_busy || !HasVerifiedScope()) return;
        var policy = CurrentSelection();
        if (policy is null) return;
        string root;
        try { root = FeedbackPath.ValidateRoot(_currentShare()); }
        catch (Exception ex)
        {
            _snapshot = null;
            _status.Text = "SMB configuration is invalid: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 150);
            SetButtons();
            return;
        }
        string[] names = _verified.ToArray();
        int staleMinutes = Math.Clamp(_project.FeedbackIntervalMinutes * 3, 45, 180);
        _busy = true;
        SetButtons();
        _status.Text = "Reading SMB Client Health, both Repair queues and local history...";
        try
        {
            var snapshot = await Task.Run(() => {
                var now = DateTimeOffset.UtcNow;
                var client = ClientFeedbackReader.Read(root, policy.Value.CampaignId,
                    names.Select(x => x.Split('.')[0]), now, staleMinutes);
                var perUser = DeferredRepairQueue.ForScope(_domain, _dc, _ouDn, _groupDn);
                var localTimeline = RepairHistoryJournal.ReadScope(DeferredRepairQueue.DirectoryPath,
                    "User", _domain, _dc, _ouDn, _groupDn,
                    policy.Value.CampaignId, policy.Value.GpoId, 250);
                var gmsa = ReadScopedGmsaRepairState(_domain, _dc, _ouDn, _groupDn,
                    policy.Value.CampaignId, policy.Value.GpoId);
                var history = CampaignHistoryStore.LoadRecent(300);
                return DeviceDashboardModel.Build(policy.Value.CampaignId, policy.Value.GpoId,
                    root, names, client, perUser, gmsa.Queue, history, _domain, _ouDn,
                    _groupDn, localTimeline.Concat(gmsa.Timeline));
            });
            if (IsDisposed) return;
            if (CurrentSelection() != policy ||
                !string.Equals(_currentShare().Trim().TrimEnd('\\'), root,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Campaign or feedback share changed during refresh.");
            _snapshot = snapshot;
            _summary.Text =
                $"Scoped AD computers: {snapshot.Rows.Count}  |  " +
                $"Client Health claims: {snapshot.HealthClaims}  |  " +
                $"Attention: {snapshot.Problems}  |  No report: {snapshot.NoReport}  |  " +
                $"Pending Repair: {snapshot.ActiveRepair}  |  " +
                $"Repair events: {snapshot.RepairTimeline.Count}  |  " +
                $"Invalid reports: {snapshot.ClientSnapshot.InvalidReports}";
            _headline.Text = "Campaign: " + snapshot.CampaignId +
                " | Verified OU + Security Group | Refreshed " +
                snapshot.ObservedUtc.LocalDateTime.ToString("HH:mm:ss");
            DrawRows();
            _status.Text = "Read-only snapshot. Expected image is an agent claim, " +
                "not proof of the image displayed on screen.";
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _snapshot = null;
                _grid.Rows.Clear();
                _details.Text = "Data sources could not be safely combined.";
                _summary.Text = "Snapshot unavailable. Missing telemetry is not a failed endpoint.";
                _status.Text = "Dashboard refresh failed: " +
                    UniversalDiagnosticsEmbedded.Clean(ex.Message, 170);
                AppLog.Write(ex, "Device & Repair Dashboard refresh");
            }
        }
        finally
        {
            _busy = false;
            if (!IsDisposed) SetButtons();
        }
    }

    private void DrawRows()
    {
        if (IsDisposed || _grid.IsDisposed) return;
        string filter = _filter.SelectedItem as string ?? "All devices";
        string query = _search.Text.Trim();
        string[] selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(x => x.Tag as string ?? "").ToArray();
        IEnumerable<DeviceDashboardRow> rows = _snapshot?.Rows ??
            Array.Empty<DeviceDashboardRow>();
        if (!string.IsNullOrWhiteSpace(query))
            rows = rows.Where(x => x.ComputerFqdn.Contains(query,
                StringComparison.OrdinalIgnoreCase));
        rows = filter switch {
            "Needs attention" => rows.Where(x => x.NeedsAttention),
            "Repair pending" => rows.Where(x => x.HasPendingRepair),
            "No report" => rows.Where(x => x.ClientHealth == "No report"),
            "Healthy / completed" => rows.Where(x =>
                x.ClientHealth is "Healthy" or "Completed"),
            _ => rows
        };
        _drawing = true;
        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            foreach (var row in rows.OrderByDescending(x => x.NeedsAttention)
                         .ThenBy(x => x.ComputerFqdn, StringComparer.OrdinalIgnoreCase))
            {
                int i = _grid.Rows.Add(row.ComputerFqdn, row.ClientHealth,
                    row.Stage, row.ExpectedImage, row.LastSeen,
                    row.Integrity, row.AgentVersion, row.RepairState);
                var gridRow = _grid.Rows[i];
                gridRow.Tag = row.ComputerFqdn;
                gridRow.Cells[3].ToolTipText =
                    "Expected image filename reported by the agent; not independently verified on screen.";
                gridRow.Cells[7].ToolTipText = row.RepairExplanation;
                if (row.NeedsAttention)
                    gridRow.DefaultCellStyle.ForeColor = Color.DarkRed;
                else if (row.HasPendingRepair)
                    gridRow.DefaultCellStyle.ForeColor = Color.DarkGoldenrod;
                else if (row.ClientHealth == "Healthy")
                    gridRow.DefaultCellStyle.ForeColor = Color.DarkGreen;
            }
            _grid.ClearSelection();
            _grid.CurrentCell = null;
            foreach (DataGridViewRow row in _grid.Rows)
                if (selected.Contains(row.Tag as string, StringComparer.OrdinalIgnoreCase))
                    row.Selected = true;
        }
        finally
        {
            _grid.ResumeLayout();
            _drawing = false;
        }
        SetButtons();
        _ = LoadSelectedDetailsAsync();
    }

    private async Task LoadSelectedDetailsAsync()
    {
        if (!HasValidSnapshot())
        {
            _details.Text = "Load a fresh snapshot and select a verified computer.";
            return;
        }
        if (_grid.SelectedRows.Count != 1)
        {
            _details.Text = _grid.SelectedRows.Count > 1
                ? "Multiple devices selected. Repair requires existing Preview + full GPO GUID approval."
                : "Select one device for details. Client Health is untrusted agent telemetry.";
            return;
        }
        string fqdn = _grid.SelectedRows[0].Tag as string ?? "";
        var row = _snapshot!.Rows.FirstOrDefault(x =>
            string.Equals(x.ComputerFqdn, fqdn, StringComparison.OrdinalIgnoreCase));
        if (row is null) return;
        int request = ++_detailRequest;
        var snapshot = _snapshot;
        _details.Text = "Reading fresh report for " + row.ComputerFqdn + "...";
        try
        {
            var record = await Task.Run(() => ClientFeedbackReader.ReadSingle(
                snapshot.FeedbackShare, snapshot.CampaignId, row.ShortName,
                DateTimeOffset.UtcNow));
            if (IsDisposed || request != _detailRequest ||
                _snapshot != snapshot || !HasValidSnapshot())
                return;
            var builder = new StringBuilder();
            builder.AppendLine("Computer: " + row.ComputerFqdn);
            builder.AppendLine("Client condition: " + row.ClientHealth +
                " | Agent stage: " + row.Stage);
            builder.AppendLine("Repair: " + row.RepairState +
                " | " + row.RepairExplanation);
            builder.AppendLine("Tracked Repair records: " + row.RepairEvidence.Count +
                " (most recent durable queue metadata, not a complete attempt log)");
            foreach (var repair in row.RepairEvidence)
            {
                string Date(DateTimeOffset value) =>
                    value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                string Optional(DateTimeOffset? value) =>
                    value.HasValue ? Date(value.Value) : "not recorded";
                builder.AppendLine("  Source: " + repair.Source + " | State: " + repair.State +
                    " | RPC attempts: " + repair.Attempts + " / 20");
                builder.AppendLine("  Approved: " + Date(repair.ApprovedUtc) +
                    " | Expires: " + Date(repair.ApprovedUtc.AddDays(14)));
                builder.AppendLine("  Last attempt: " + Optional(repair.LastAttemptUtc) +
                    " | Submitted: " + Optional(repair.SubmittedUtc) +
                    " | Next check: " + Date(repair.NextAttemptUtc));
                builder.AppendLine("  Reason: " + repair.Reason);
                if (repair.IsInFlight)
                    builder.AppendLine("  WARNING: Remote task may already exist. " +
                        "No automatic second RPC until an administrator investigates.");
            }
            if (record is null)
                builder.AppendLine("Fresh SMB report: none. Device state is unknown.");
            else
            {
                builder.AppendLine("Last reported (local): " +
                    record.LastSeenUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                builder.AppendLine("Agent: " + record.AgentVersion +
                    " | Expected image: " + (record.ExpectedImage.Length > 0
                        ? record.ExpectedImage : "(none)"));
                builder.AppendLine("Image files pass agent checks: " +
                    (record.ImageMatches ? "Yes (agent claim)" : "No") +
                    " | Missing: " + record.MissingFiles +
                    " | Corrupt: " + record.CorruptFiles);
                builder.AppendLine("Registry matches expected policy: " +
                    (record.RegistryMatches ? "Yes (agent claim)" : "No") +
                    " | Scheduled task: " + (record.TaskPresent ? "Reported present" : "Not reported"));
            }
            var timeline = snapshot.RepairTimeline.Where(x =>
                x.ComputerFqdn.Equals(row.ComputerFqdn, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.RecordedUtc).Take(30).ToArray();
            builder.AppendLine("Durable Repair timeline: " + timeline.Length +
                " recent events (User/gMSA queues; read-only, may contain gaps)");
            foreach (var entry in timeline)
                builder.AppendLine("  " + entry.RecordedUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") +
                    " | " + entry.Source + " / " + entry.Action +
                    " | " + entry.PreviousState + " -> " + entry.State +
                    " | RPC attempts: " + entry.Attempts +
                    " | " + entry.Reason);
            builder.AppendLine("Archived snapshots for this computer: " + row.History.Count);
            foreach (var past in row.History)
                builder.AppendLine("  " + past.WhenUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm") +
                    " | " + past.Condition + " | " + past.Stage +
                    " | expected: " + past.ExpectedImage +
                    " | agent: " + past.AgentVersion);
            builder.AppendLine("No independent screen capture or on-screen image attestation is available.");
            _details.Text = builder.ToString();
        }
        catch (Exception ex)
        {
            if (IsDisposed || request != _detailRequest) return;
            _details.Text = row.ComputerFqdn + Environment.NewLine +
                "Fresh client report unavailable. Do not treat this as a failed machine." +
                Environment.NewLine +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 170);
        }
    }

    private async Task RepairSelectedAsync()
    {
        if (_busy || !HasValidSnapshot()) return;
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(x => x.Tag as string ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selected.Length is < 1 or > 25)
        {
            MessageBox.Show(FindForm(), "Select 1–25 verified computers.", "Repair Selected",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var verified = new HashSet<string>(_verified, StringComparer.OrdinalIgnoreCase);
        if (selected.Any(x => !verified.Contains(x)))
        {
            MessageBox.Show(FindForm(), "Selected host is outside the last verified AD scope.",
                "Repair Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_snapshot!.Rows.Where(x => selected.Contains(x.ComputerFqdn,
                StringComparer.OrdinalIgnoreCase)).Any(x => x.HasPendingRepair))
        {
            MessageBox.Show(FindForm(),
                "One or more selected PCs already have a pending Repair. " +
                "Inspect the protected queues and Client Health before new RPC.",
                "Repair already tracked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(FindForm(),
            "Open the EXISTING Repair workflow for these AD-verified computers?" +
            Environment.NewLine + Environment.NewLine +
            "This dashboard does not start an RPC. The existing workflow will " +
            "revalidate AD, GPO and both Repair queues and require Preview plus " +
            "typing the complete GPO GUID before any remote task." +
            Environment.NewLine + "Continue?",
            "Repair Selected", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        _busy = true;
        SetButtons();
        try
        {
            await _requestRepair(selected.Select(x => x.Split('.')[0]).ToArray());
        }
        catch (Exception ex) { if (!IsDisposed) UiErrorDialog.Show(FindForm(), "Device Repair", ex); }
        finally
        {
            _busy = false;
            if (!IsDisposed) SetButtons();
        }
        if (!IsDisposed) await RefreshReportsAsync();
    }

    private void SaveSnapshot()
    {
        if (!HasValidSnapshot()) return;
        try
        {
            var record = CampaignHistoryStore.SaveHealth(
                _snapshot!.ClientSnapshot, _domain, _ouDn, _groupDn, final: false);
            _status.Text = "Saved local Client Health snapshot: " +
                record.OccurredUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (Exception ex) { UiErrorDialog.Show(FindForm(), "Save Device Dashboard snapshot", ex); }
    }

    private async Task OpenClosureAuditAsync()
    {
        if (_busy || !HasValidSnapshot() || _snapshot is null)
        {
            _status.Text = "Load verified AD devices and refresh Client Health first.";
            return;
        }
        var snapshot = _snapshot;
        CampaignClosureAudit audit;
        try { audit = CampaignClosureAudit.Assess(snapshot, DateTimeOffset.UtcNow); }
        catch (Exception ex)
        {
            if (!IsDisposed)
                _status.Text = "Closure audit needs a fresh verified AD snapshot: " +
                    UniversalDiagnosticsEmbedded.Clean(ex.Message, 160);
            return;
        }

        _busy = true;
        SetButtons();
        _status.Text = "Reading the selected DC's managed GPO links (read-only)...";
        CampaignGpoLinkEvidence evidence;
        try
        {
            evidence = await Task.Run(() =>
            {
                var response = DomainPowerShellRunner.Execute(
                    DomainOperation.Lifecycle, "List",
                    new { domain = _domain, dc = _dc }, _credentials, 240_000);
                return CampaignGpoLinkEvidence.FromManagedList(
                    response.GetProperty("data"), _dc, _ouDn,
                    audit.CampaignId, audit.GpoId, DateTimeOffset.UtcNow);
            });
        }
        catch (Exception ex)
        {
            // Preserve a useful client-only audit, but explicitly mark DC
            // policy evidence unavailable; never substitute a stale GPO list.
            AppLog.Write(ex, "Campaign Closure selected-DC read");
            evidence = CampaignGpoLinkEvidence.Unavailable(
                _dc, ex.Message);
        }
        finally
        {
            _busy = false;
            if (!IsDisposed) SetButtons();
        }
        if (IsDisposed || !ReferenceEquals(_snapshot, snapshot) || !HasValidSnapshot())
            return;
        _status.Text = evidence.Available
            ? "Closure audit opened. GPO links and agent claims are separate evidence."
            : "Closure audit opened with UNKNOWN DC policy state.";
        try
        {
            var scope = new PostCleanupScope(_domain, _dc, _ouDn, _groupDn,
                audit.CampaignId, audit.GpoId);
            var eligible = snapshot.Rows.Select(x => x.ComputerFqdn).ToArray();
            var prior = PostCleanupVerificationStore.Load(scope, eligible);
            async Task<IReadOnlyList<PostCleanupObservation>> ScanAsync(IReadOnlyList<string> selected)
            {
                if (IsDisposed || !ReferenceEquals(_snapshot, snapshot) ||
                    !HasValidSnapshot() || DateTimeOffset.UtcNow -
                    snapshot.ObservedUtc > TimeSpan.FromMinutes(10))
                    throw new InvalidOperationException(
                        "Selected campaign or AD scope changed; refresh the dashboard.");
                // Pure remote reads. No Preview/Apply, no scheduler registrations,
                // no server-side retry loop and no automatic collection.
                var result = await Task.Run(() => DomainPowerShellRunner.Execute(
                    DomainOperation.Immediate, "VerifyCleanup", new {
                        domain = _domain, dc = _dc, ouDn = _ouDn, groupDn = _groupDn,
                        gpoId = audit.GpoId, purpose = "VerifyCleanup",
                        computers = selected.ToArray()
                    }, _credentials, 15 * 60 * 1000));
                return PostCleanupObservation.ParseBatch(
                    result.GetProperty("data"), audit.CampaignId, audit.GpoId,
                    selected, DateTimeOffset.UtcNow);
            }
            using var window = new CampaignClosureAuditDialog(audit, evidence,
                scope, eligible, prior, ScanAsync);
            window.ShowDialog(FindForm());
        }
        catch (Exception ex)
        {
            if (!IsDisposed) UiErrorDialog.Show(FindForm(),
                "Campaign Closure post-cleanup history", ex);
        }
    }

    private void OpenRepairHistory()
    {
        if (!HasValidSnapshot() || _snapshot is null)
        {
            MessageBox.Show(FindForm(), "Load verified AD devices and refresh reports first.",
                "Repair history", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string? selected = _grid.SelectedRows.Count == 1
            ? _grid.SelectedRows[0].Tag as string : null;
        using var window = new RepairHistoryDialog(_snapshot.RepairTimeline, selected);
        window.ShowDialog(FindForm());
    }

    private void OpenHistory()
    {
        using var history = new CampaignHistoryForm();
        history.ShowDialog(FindForm());
    }
}
