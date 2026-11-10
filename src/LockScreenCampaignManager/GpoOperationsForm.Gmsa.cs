using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

internal sealed partial class GpoOperationsForm
{
    private readonly CheckBox _gmsaForNewRepair = new()
    {
        Text = "24/7 gMSA — Route new approved Repair requests to machine queue",
        AutoSize = true, Margin = new Padding(9, 12, 10, 8)
    };
    private readonly DataGridView _gmsaGrid = new();
    private readonly Label _gmsaInfo = new();
    private Button? _gmsaCancelSelected;
    private bool _gmsaQueueEditable;
    private bool _gmsaLoading;
    private bool _gmsaRefreshAgain;
    private readonly ComboBox _gmsaFilter = new();
    private readonly CheckBox _gmsaAutoRefresh = new() {
        Text = "Auto-refresh (30 s)", Checked = true, AutoSize = true,
        Margin = new Padding(8, 14, 4, 0)
    };
    private readonly System.Windows.Forms.Timer _gmsaTimer = new() { Interval = 30_000 };
    private TabPage? _gmsaTab;
    private IReadOnlyList<GmsaQueueAuditRow> _gmsaRows = Array.Empty<GmsaQueueAuditRow>();

    private sealed record GmsaUiSnapshot(
        GmsaRepairConfig? Active,
        GmsaRepairConfig? Disabled,
        bool Registered,
        bool IsAdministrator,
        IReadOnlyList<GmsaQueueAuditRow> Rows,
        string Counts,
        GmsaRepairStatus LastStatus);

    private TabPage BuildGmsaTab()
    {
        var tab = new TabPage("gMSA 24/7") { BackColor = UiStyle.Canvas };
        _gmsaTab = tab;
        var layout = Panel(5);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 137));
        tab.Controls.Add(layout);
        layout.Controls.Add(Note(
            "Unattended Repair via gMSA and Task Scheduler on a dedicated always-on Windows host. " +
            "Install and manage directly from this host's graphical interface. " +
            "Existing per-user requests are never migrated automatically."), 0, 0);
        layout.Controls.Add(_gmsaForNewRepair, 0, 1);
        _gmsaGrid.Name = "GpoGmsaQueueGrid";
        _gmsaGrid.Dock = DockStyle.Fill;
        _gmsaGrid.ReadOnly = true;
        _gmsaGrid.AllowUserToAddRows = false;
        _gmsaGrid.AllowUserToDeleteRows = false;
        _gmsaGrid.MultiSelect = false;
        _gmsaGrid.RowHeadersVisible = false;
        _gmsaGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gmsaGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _gmsaGrid.SelectionChanged += (_, _) => {
            if (_gmsaCancelSelected is not null)
                _gmsaCancelSelected.Enabled =
                    _gmsaQueueEditable && _gmsaGrid.SelectedRows.Count > 0;
        };
        _gmsaGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Computer", FillWeight = 145 });
        _gmsaGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Campaign", FillWeight = 135 });
        _gmsaGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status", FillWeight = 102 });
        _gmsaGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Health", FillWeight = 138 });
        _gmsaGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Attempts", FillWeight = 64 });
        _gmsaGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Next check", FillWeight = 106 });
        _gmsaGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Reason", FillWeight = 160 });
        layout.Controls.Add(_gmsaGrid, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        buttons.Controls.Add(ActionButton("Configure gMSA", OpenGmsaSetupDialog, 152));
        buttons.Controls.Add(ActionButton("Refresh gMSA", RefreshGmsaRows, 132));
        buttons.Controls.Add(ActionButton("Production Readiness", OpenProductionReadiness, 188));
        _gmsaCancelSelected = ActionButton("Cancel selected", CancelSelectedGmsa, 158);
        _gmsaCancelSelected.Enabled = false;
        buttons.Controls.Add(_gmsaCancelSelected);
        buttons.Controls.Add(new Label {
            Text = "Filter:", AutoSize = true, Margin = new Padding(8, 18, 5, 0)
        });
        _gmsaFilter.Name = "GpoGmsaFilter";
        _gmsaFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _gmsaFilter.Width = 144;
        _gmsaFilter.Margin = new Padding(0, 10, 6, 0);
        _gmsaFilter.Items.AddRange(new object[] {
            "All", "Needs review", "Monitoring",
            "Queued", "AwaitingTask", "AwaitingHealth", "Attempting"
        });
        _gmsaFilter.SelectedIndex = 0;
        _gmsaFilter.SelectedIndexChanged += (_, _) => ShowGmsaFilteredRows();
        buttons.Controls.Add(_gmsaFilter);
        buttons.Controls.Add(_gmsaAutoRefresh);
        layout.Controls.Add(buttons, 0, 3);
        _gmsaInfo.Dock = DockStyle.Fill;
        _gmsaInfo.ForeColor = UiStyle.Muted;
        _gmsaInfo.AutoEllipsis = false;
        _gmsaInfo.Text = UiLocale.Display("Mode is off. Setup requires local administrator rights on this Windows host.");
        layout.Controls.Add(_gmsaInfo, 0, 4);
        _gmsaTimer.Tick += (_, _) => {
            if (!_busy && IsGmsaTabSelected() && _gmsaAutoRefresh.Checked)
                RefreshGmsaRows();
        };
        _gmsaAutoRefresh.CheckedChanged += (_, _) => UpdateGmsaTimer();
        tab.Enter += (_, _) => {
            UpdateGmsaTimer();
            RefreshGmsaRows();
        };
        tab.Leave += (_, _) => _gmsaTimer.Stop();
        FormClosed += (_, _) => {
            _gmsaTimer.Stop();
            _gmsaTimer.Dispose();
        };
        return tab;
    }

    private void OpenProductionReadiness()
    {
        if (_busy) return;
        using var dialog = new GmsaReadinessDialog(_domain, _ouDn, _groupDn);
        dialog.ShowDialog(this);
        RefreshGmsaRows();
    }

    private void OpenGmsaSetupDialog()
    {
        if (_busy) return;
        using var setup = new GmsaRepairSetupDialog(_domain, _ouDn, _groupDn,
            _clientHealth?.FeedbackShareRoot);
        setup.ShowDialog(this);
        RefreshGmsaRows();
    }

    private GmsaRepairConfig? CurrentGmsaConfig() =>
        GmsaRepairConfig.TryLoad() is { } c &&
        c.ScopeMatches(_domain, _ouDn, _groupDn) ? c : null;

    private bool IsGmsaTabSelected() =>
        _gmsaTab?.Parent is TabControl control && control.SelectedTab == _gmsaTab;

    private void UpdateGmsaTimer()
    {
        if (IsDisposed || !_gmsaAutoRefresh.Checked || !IsGmsaTabSelected())
            _gmsaTimer.Stop();
        else
            _gmsaTimer.Start();
    }

    // Public UI actions use one nonblocking load path. The local protected
    // queue lock can be held by Task Scheduler for up to 15 seconds.
    // Never block the WinForms UI thread while waiting for that lock.
    private void RefreshGmsaRows() => _ = RefreshGmsaRowsAsync();

    private async Task RefreshGmsaRowsAsync()
    {
        if (IsDisposed || _gmsaGrid.IsDisposed) return;
        if (_gmsaLoading)
        {
            _gmsaRefreshAgain = true;
            return;
        }
        _gmsaLoading = true;
        _gmsaQueueEditable = false;
        if (_gmsaCancelSelected is not null) _gmsaCancelSelected.Enabled = false;
        try
        {
            var snapshot = await Task.Run(ReadGmsaSnapshot);
            if (IsDisposed || _gmsaGrid.IsDisposed) return;
            ApplyGmsaSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            if (IsDisposed || _gmsaGrid.IsDisposed) return;
            _gmsaQueueEditable = false;
            _gmsaRows = Array.Empty<GmsaQueueAuditRow>();
            _gmsaGrid.Rows.Clear();
            _gmsaForNewRepair.Checked = false;
            _gmsaForNewRepair.Enabled = false;
            if (_gmsaCancelSelected is not null) _gmsaCancelSelected.Enabled = false;
            _gmsaInfo.Text = "gMSA queue unavailable: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 180);
            AppLog.Write(ex, "gMSA read-only UI refresh");
        }
        finally
        {
            _gmsaLoading = false;
            if (_gmsaRefreshAgain && !IsDisposed)
            {
                _gmsaRefreshAgain = false;
                RefreshGmsaRows();
            }
        }
    }

    // This method runs off the UI thread. It reads an immutable snapshot for
    // presentation only; it never invokes Repair, touches GPOs or writes queue.
    private GmsaUiSnapshot ReadGmsaSnapshot()
    {
        var config = CurrentGmsaConfig();
        var installed = GmsaRepairConfig.TryReadForInspection();
        var disabled = config is null && installed is { Enabled: false } &&
            installed.ScopeMatchesForInspection(_domain, _ouDn, _groupDn)
            ? installed : null;
        bool admin = GmsaRepairQueue.IsAdministrator();
        bool registered = config is not null &&
            GmsaRepairConfig.IsScheduledTaskReady(config);
        var entries = config is not null ? GmsaRepairQueue.All(config)
            : disabled is not null ? GmsaRepairQueue.InspectReadOnly(disabled)
            : Array.Empty<DeferredRepairItem>();
        var rows = GmsaQueueAudit.Analyze(entries, DateTimeOffset.UtcNow, registered);
        return new GmsaUiSnapshot(config, disabled, registered, admin,
            rows, GmsaQueueAudit.FormatCounts(entries),
            GmsaRepairStatus.LoadForScope(config));
    }

    private void ApplyGmsaSnapshot(GmsaUiSnapshot snapshot)
    {
        bool usable = snapshot.Active is not null && snapshot.Registered &&
            snapshot.IsAdministrator;
        _gmsaForNewRepair.Enabled = usable;
        if (!usable) _gmsaForNewRepair.Checked = false;
        _gmsaRows = snapshot.Rows;
        _gmsaQueueEditable = snapshot.Active is not null && snapshot.IsAdministrator;
        ShowGmsaFilteredRows();

        string attention = GmsaQueueAudit.FormatAttention(snapshot.Rows);
        if (snapshot.Disabled is not null)
        {
            _gmsaInfo.Text =
                $"gMSA: {snapshot.Disabled.GmsaAccount} | DISABLED — queue inspection only." +
                Environment.NewLine + snapshot.Counts + Environment.NewLine + attention +
                Environment.NewLine +
                "No RPC, Cancel or new Repair while disabled. Re-enable via Configure gMSA.";
        }
        else if (snapshot.Active is null)
        {
            _gmsaInfo.Text = "gMSA is not configured or the AD scope does not match. " +
                "Open Configure gMSA to check or install without PowerShell.";
        }
        else
        {
            var last = snapshot.LastStatus;
            string heartbeat = GmsaRepairStatus.Classify(
                last, DateTimeOffset.UtcNow, snapshot.Registered);
            _gmsaInfo.Text =
                $"gMSA: {snapshot.Active.GmsaAccount} | " +
                $"Task: {(snapshot.Registered ? "registered" : "not verified")} | " +
                "Last run: " +
                (last.LastRunUtc.Year > 2020
                    ? last.LastRunUtc.LocalDateTime.ToString("dd.MM HH:mm:ss") : "not yet") +
                Environment.NewLine + snapshot.Counts + Environment.NewLine + attention +
                Environment.NewLine + "Worker: " + heartbeat +
                (usable ? "" : " | Administrator and valid task required.");
        }
    }

    private void ShowGmsaFilteredRows()
    {
        if (IsDisposed || _gmsaGrid.IsDisposed) return;
        string filter = _gmsaFilter.SelectedItem as string ?? "All";
        string? selectedId = _gmsaGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(x => x.Tag as string).FirstOrDefault();
        IEnumerable<GmsaQueueAuditRow> view = filter switch
        {
            "Needs review" => _gmsaRows.Where(x =>
                x.Priority == GmsaQueuePriority.Attention),
            "Monitoring" => _gmsaRows.Where(x =>
                x.Priority == GmsaQueuePriority.Monitor),
            "All" => _gmsaRows,
            _ => _gmsaRows.Where(x => x.State == filter)
        };
        _gmsaGrid.SuspendLayout();
        try
        {
            _gmsaGrid.Rows.Clear();
            foreach (var row in view)
            {
                int i = _gmsaGrid.Rows.Add(row.ComputerFqdn, row.CampaignId,
                    row.State, row.Health, row.Attempts.ToString(),
                    row.NextAttemptUtc.LocalDateTime.ToString("dd.MM HH:mm"),
                    row.Reason);
                var gridRow = _gmsaGrid.Rows[i];
                gridRow.Tag = row.Id;
                gridRow.Cells[3].ToolTipText = row.Guidance;
                gridRow.Cells[6].ToolTipText = row.Reason;
                if (row.Priority == GmsaQueuePriority.Attention)
                    gridRow.DefaultCellStyle.ForeColor = Color.DarkRed;
                else if (row.Priority == GmsaQueuePriority.Monitor)
                    gridRow.DefaultCellStyle.ForeColor = Color.DarkGoldenrod;
            }
            _gmsaGrid.ClearSelection();
            _gmsaGrid.CurrentCell = null;
            if (selectedId is not null)
            {
                foreach (DataGridViewRow row in _gmsaGrid.Rows)
                {
                    if (!string.Equals(row.Tag as string, selectedId, StringComparison.Ordinal))
                        continue;
                    row.Selected = true;
                    _gmsaGrid.CurrentCell = row.Cells[0];
                    break;
                }
            }
        }
        finally { _gmsaGrid.ResumeLayout(); }
        if (_gmsaCancelSelected is not null)
            _gmsaCancelSelected.Enabled =
                _gmsaQueueEditable && _gmsaGrid.SelectedRows.Count > 0;
    }

    private void CancelSelectedGmsa()
    {
        if (_busy || !_gmsaQueueEditable) return;
        string? id = _gmsaGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(x => x.Tag as string).FirstOrDefault();
        var config = CurrentGmsaConfig();
        if (config is null || string.IsNullOrWhiteSpace(id)) return;
        if (MessageBox.Show(this, "Cancel this gMSA request?\n" +
                "This will NOT cancel a task already running on a remote PC.",
                "gMSA Repair", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            GmsaRepairQueue.Cancel(config, id);
            RefreshGmsaRows();
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Cancel gMSA Repair", ex); }
    }

    /// <summary>
    /// Fail closed before any new foreground RPC if the protected gMSA queue
    /// contains an unresolved task. Disabled workers are still auditable; an
    /// offline Queued request cannot be silently duplicated while disabled.
    /// Existing active Queued requests are superseded separately by the
    /// original administrator-approved ReserveApproved() transaction.
    /// </summary>
    private void EnsureNoPendingGmsaRepair(GpoRow policy,
        IEnumerable<string> selectedFqdn)
    {
        bool configExists = File.Exists(GmsaRepairConfig.ConfigPath);
        bool orphanQueue = File.Exists(Path.Combine(GmsaRepairConfig.QueueDirectory, "queue.json"));
        if (!configExists)
        {
            if (orphanQueue)
                throw new InvalidDataException(
                    "Protected gMSA queue exists without trusted configuration. " +
                    "Do not repeat remote Repair until an administrator investigates.");
            return;
        }
        var installed = GmsaRepairConfig.TryReadForInspection() ??
            throw new InvalidDataException(
                "Protected gMSA settings cannot be verified. Repair is blocked " +
                "until a local administrator checks the worker installation.");
        if (!installed.ScopeMatchesForInspection(_domain, _ouDn, _groupDn))
            return;
        // Read-only inspection also works after Disable or Remove task.
        // Access failures must NOT be interpreted as an empty queue.
        var items = GmsaRepairQueue.InspectReadOnly(installed);
        var blocked = GmsaRepairConflictGuard.BlockedHosts(
            items, policy.CampaignId, policy.Id, selectedFqdn,
            installed.Enabled && GmsaRepairConfig.IsScheduledTaskReady(installed));
        if (blocked.Count > 0)
            throw new InvalidOperationException(
                "Previous gMSA Repair is still queued or has an uncertain RPC result " +
                "for: " + string.Join(", ", blocked) + ". " +
                "Inspect gMSA 24/7 / Client Health before issuing a NEW Repair. " +
                "Disabling gMSA never cancels a previously submitted client task.");
    }

    /// <summary>Called only AFTER the ordinary exact-GUID operator confirmation.</summary>
    private void ReserveGmsaBeforeManualRepair(GpoRow selected, IReadOnlyCollection<string> hosts,
        bool routeToGmsa)
    {
        var config = CurrentGmsaConfig();
        if (routeToGmsa && config is null)
            throw new InvalidOperationException("gMSA routing was selected, but the configuration or AD scope is unavailable.");
        if (config is null) return;

        var allowed = new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase);
        bool existing = GmsaRepairQueue.All(config).Any(x =>
            x.CampaignId == selected.CampaignId &&
            x.GpoId.Equals(selected.Id, StringComparison.OrdinalIgnoreCase) &&
            allowed.Contains(x.ComputerFqdn));
        if (existing || routeToGmsa)
        {
            if (routeToGmsa &&
                (!GmsaRepairQueue.IsAdministrator() || !GmsaRepairConfig.IsScheduledTaskReady(config)))
                throw new UnauthorizedAccessException("gMSA routing requires a local administrator and a valid scheduled task.");
            GmsaRepairQueue.ReserveApproved(config, _domain, selected.Id,
                selected.CampaignId, hosts, _ouDn, _groupDn);
        }
    }

    private bool TrackGmsaInsteadOfUser(bool approvedGmsaRoute)
    {
        if (!approvedGmsaRoute) return false;
        var config = CurrentGmsaConfig();
        if (config is null || !GmsaRepairConfig.IsScheduledTaskReady(config))
            throw new InvalidOperationException("gMSA became unavailable. Inspect RPC results manually.");
        return true;
    }

    private (int Offline, int AwaitingHealth, int AwaitingTask, int NeedsAttention)
        SaveGmsaApprovedResults(GpoRow policy, IReadOnlyDictionary<string, string> statuses,
            DateTimeOffset submittedUtc)
    {
        var config = CurrentGmsaConfig()
            ?? throw new InvalidOperationException("Protected gMSA configuration is not available.");
        var tracked = GmsaRepairQueue.EnqueueApprovedResults(config, _dc, policy.Id,
            policy.CampaignId, statuses, submittedUtc, DateTimeOffset.UtcNow);
        RefreshGmsaRows();
        CampaignHistoryStore.TryRecordEvent(policy.CampaignId, "gMSA repair routing",
            $"gMSA queued: {tracked.Offline}; awaiting client health: {tracked.AwaitingHealth}; " +
            $"uncertain RPC: {tracked.AwaitingTask}; needs review: {tracked.NeedsAttention}.");
        return tracked;
    }
}
