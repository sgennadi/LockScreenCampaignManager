using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

internal sealed partial class GpoOperationsForm
{
    private readonly System.Windows.Forms.Timer _deferredTimer = new();
    private readonly CheckBox _deferredAutoRetry = new()
    {
        Text = "Automatic retry when PCs return (while manager is open)",
        Checked = true,
        AutoSize = true,
        Margin = new Padding(7, 15, 7, 0)
    };
    private readonly CheckBox _backgroundRepair = new() { Text = "Background processing (Task Scheduler, while Windows user is signed in)", AutoSize = true, Margin = new Padding(8, 12, 4, 4) };
    private bool _backgroundChanging;
    private readonly DataGridView _deferredGrid = new();
    private readonly Label _deferredMessage = new();
    private bool _deferredWorking;

    private void InitializeDeferred()
    {
        var savedBackground = BackgroundRepairSettings.Load();
        _backgroundRepair.Checked = savedBackground.Enabled && !_credentials.IsAlternate;
        _backgroundRepair.Enabled = !_credentials.IsAlternate;
        _backgroundRepair.CheckedChanged += async (_, _) => await SetBackgroundRepairAsync();
        _deferredTimer.Interval = 60_000;
        _deferredTimer.Tick += async (_, _) =>
        {
            if (!_deferredAutoRetry.Checked || _busy || _deferredWorking ||
                IsDisposed || !Visible ||
                (!_credentials.IsAlternate && BackgroundRepairSettings.Load().Enabled)) return;
            await RetryDeferredAsync(force: false);
        };
        _deferredTimer.Start();
        RefreshDeferredRows();
    }

    private void CloseDeferred()
    {
        _deferredTimer.Stop();
        _deferredTimer.Dispose();
    }

    private TabPage BuildDeferredTab()
    {
        var tab = new TabPage("Deferred Repair") { BackColor = UiStyle.Canvas };
        var panel = Panel(5);
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        tab.Controls.Add(panel);

        var introduction = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = UiStyle.Muted,
            Text = "Only administrator-approved computers. " +
                   "Repeat RPC only after confirmed temporary unavailability. After execution, " +
                   "wait for a fresh Client Health report without re-running. " +
                   "Permission failures and existing remote tasks are not automatically retried. " +
                   "Passwords are not stored. Maximum 14 days / 20 attempts."
        };
        panel.Controls.Add(introduction, 0, 0);

        _deferredGrid.Name = "GpoDeferredRepairGrid";
        _deferredGrid.Dock = DockStyle.Fill;
        _deferredGrid.AllowUserToAddRows = false;
        _deferredGrid.AllowUserToDeleteRows = false;
        _deferredGrid.ReadOnly = true;
        _deferredGrid.MultiSelect = false;
        _deferredGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _deferredGrid.RowHeadersVisible = false;
        _deferredGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _deferredGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Computer", FillWeight = 148 });
        _deferredGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Campaign", FillWeight = 125 });
        _deferredGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status", FillWeight = 98 });
        _deferredGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Attempts", FillWeight = 62 });
        _deferredGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Next check", FillWeight = 108 });
        _deferredGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Reason", FillWeight = 157 });
        panel.Controls.Add(_deferredGrid, 0, 1);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        toolbar.Controls.Add(ActionButton("Refresh queue", RefreshDeferredRows, 155));
        toolbar.Controls.Add(ActionButton("Check selected now", () => _ = RetryDeferredAsync(force: true), 239));
        toolbar.Controls.Add(ActionButton("Cancel selected", CancelDeferredSelected, 188));
        toolbar.Controls.Add(_deferredAutoRetry);
        panel.Controls.Add(toolbar, 0, 2);
        var backgroundRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        backgroundRow.Controls.Add(_backgroundRepair);
        panel.Controls.Add(backgroundRow, 0, 3);
        _deferredMessage.Dock = DockStyle.Fill;
        _deferredMessage.ForeColor = UiStyle.Muted;
        _deferredMessage.Text = "Without background mode, RPC runs only while GPO Operations is open. " +
            "Background mode requires separate approval and stores no password.";
        panel.Controls.Add(_deferredMessage, 0, 4);
        return tab;
    }

    private void RefreshDeferredRows()
    {
        if (_deferredGrid.IsDisposed) return;
        try
        {
            var entries = DeferredRepairQueue.ForScope(_domain, _dc, _ouDn, _groupDn);
            _deferredGrid.SuspendLayout();
            try
            {
                _deferredGrid.Rows.Clear();
                foreach (var entry in entries.OrderBy(x => x.NextAttemptUtc))
                {
                    int row = _deferredGrid.Rows.Add(entry.ComputerFqdn,
                        entry.CampaignId, entry.State, entry.Attempts.ToString(),
                        entry.State is "Queued" or "Attempting" or "AwaitingHealth" or "AwaitingTask"
                            ? entry.NextAttemptUtc.LocalDateTime.ToString("dd.MM HH:mm:ss")
                            : "—", entry.Reason);
                    _deferredGrid.Rows[row].Tag = entry.Id;
                    if (entry.State == "NeedsAttention")
                        _deferredGrid.Rows[row].DefaultCellStyle.ForeColor = Color.DarkRed;
                }
                _deferredGrid.ClearSelection();
                _deferredGrid.CurrentCell = null;
            }
            finally { _deferredGrid.ResumeLayout(); }
            var bg = BackgroundRepairSettings.Load();
            var last = BackgroundRepairStatus.Load();
            string taskState = "Off";
            if (bg.Enabled && !_credentials.IsAlternate)
            {
                try
                {
                    taskState = BackgroundRepairTask.IsInstalled() ? "Registered" : "MISSING - toggle Off/On to reinstall";
                    if (!string.Equals(bg.ExecutablePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                        taskState += " (EXE path changed)";
                }
                catch (Exception taskEx)
                {
                    AppLog.Write(taskEx, "Check background scheduler task");
                    taskState = "Scheduler unavailable";
                }
            }
            _deferredMessage.Text = $"Tracked: {entries.Count} | " +
                $"RPC when online: {entries.Count(x => x.State == "Queued")} | " +
                $"Awaiting Client Health: {entries.Count(x => x.State is "Attempting" or "AwaitingHealth" or "AwaitingTask")} | " +
                $"Manual review: {entries.Count(x => x.State == "NeedsAttention")}." +
                Environment.NewLine +
                $"Background: {taskState} | last cycle: " +
                $"{(last.LastRunUtc.Year > 2020 ? last.LastRunUtc.LocalDateTime.ToString("dd.MM HH:mm") : "none")}. " +
                last.Summary;
        }
        catch (Exception ex)
        {
            _deferredMessage.Text = "Queue unavailable: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message);
            AppLog.Write(ex, "Deferred repair queue read");
        }
    }

    private void CancelDeferredSelected()
    {
        if (_busy || _deferredWorking) return;
        string? id = _deferredGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.Tag as string).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id)) return;
        if (MessageBox.Show(this,
                "Remove the selected deferred request?\n" +
                "This does NOT cancel already running remote tasks or change the GPO.",
                "Cancel Deferred Repair", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        try { DeferredRepairQueue.Cancel(id); RefreshDeferredRows(); }
        catch (Exception ex) { UiErrorDialog.Show(this, "Cancel deferred repair", ex); }
    }

    /// <summary>
    /// Do not issue a second Repair while a prior submitted task for this
    /// machine is still awaiting health confirmation or has unknown outcome.
    /// The administrator can inspect/cancel that queue record explicitly first.
    /// </summary>
    private void EnsureNoInFlightRepair(GpoRow policy, IEnumerable<string> fqdnHosts)
    {
        var requested = new HashSet<string>(fqdnHosts, StringComparer.OrdinalIgnoreCase);
        var blocked = DeferredRepairQueue.ForScope(_domain, _dc, _ouDn, _groupDn)
            .Where(x => x.GpoId.Equals(policy.Id, StringComparison.OrdinalIgnoreCase) &&
                x.CampaignId == policy.CampaignId &&
                requested.Contains(x.ComputerFqdn) &&
                x.State is "Attempting" or "AwaitingHealth" or "AwaitingTask")
            .Select(x => x.ComputerFqdn)
            .Take(5).ToArray();
        if (blocked.Length != 0)
            throw new InvalidOperationException(
                "Cannot resend Repair: the previous remote task " +
                "still awaits confirmation or has an unknown outcome. " +
                "Check Client Health / Deferred Repair for: " +
                string.Join(", ", blocked) +
                ". A new command requires manual review and removal of the old queue record.");
        // A disabled gMSA can still have a remote task awaiting health.
        // Read its protected queue BEFORE any new manual RPC.
        EnsureNoPendingGmsaRepair(policy, requested);
    }

    // Track every initial Repair result after the operator reviewed Preview and
    // typed the exact GPO GUID. A successful task still requires health confirmation.
    private (int Offline, int AwaitingHealth, int AwaitingTask, int NeedsAttention, bool Tracked) TrackApprovedRepairResults(
        JsonElement remoteData, GpoRow approvedGpo, IEnumerable<string> approvedHosts,
        DateTimeOffset submittedUtc, bool approvedGmsaRoute)
    {
        try
        {
            var allowed = new HashSet<string>(approvedHosts, StringComparer.OrdinalIgnoreCase);
            if (allowed.Count is < 1 or > 25 ||
                !remoteData.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Remote Repair did not return an eligible result list.");

            var statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var result in results.EnumerateArray())
            {
                string host = result.GetProperty("host").GetString() ?? "";
                string status = result.GetProperty("status").GetString() ?? "";
                if (!allowed.Contains(host) || !statuses.TryAdd(host, status))
                    throw new InvalidDataException("Remote Repair returned an unapproved or duplicate computer.");
            }
            if (statuses.Count != allowed.Count)
                throw new InvalidDataException("Remote Repair omitted one or more approved computers.");

            if (TrackGmsaInsteadOfUser(approvedGmsaRoute))
            {
                var unattended = SaveGmsaApprovedResults(approvedGpo, statuses, submittedUtc);
                return (unattended.Offline, unattended.AwaitingHealth,
                    unattended.AwaitingTask, unattended.NeedsAttention, true);
            }
            var tracked = DeferredRepairQueue.EnqueueInitialApprovedResults(
                _domain, _dc, _ouDn, _groupDn, approvedGpo.Id, approvedGpo.CampaignId,
                statuses, submittedUtc, DateTimeOffset.UtcNow,
                backgroundAllowed: ShouldBackgroundTrackCurrentResults());
            RefreshDeferredRows();
            CampaignHistoryStore.TryRecordEvent(approvedGpo.CampaignId, "Repair result tracking",
                $"Offline queued: {tracked.Offline}; awaiting healthy report: {tracked.AwaitingHealth}; " +
                $"remote task pending: {tracked.AwaitingTask}; failed/manual review: {tracked.NeedsAttention}. No credentials stored.");
            return (tracked.Offline, tracked.AwaitingHealth, tracked.AwaitingTask, tracked.NeedsAttention, true);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Repair result tracking");
            MessageBox.Show(this,
                "Could not safely save Repair results to the local queue.\n" +
                "RPC commands MAY already have executed. Do not resend until you " +
                "inspect the PCs and their Client Health reports.\n\n" +
                UniversalDiagnosticsEmbedded.Clean(ex.Message),
                "Repair — manual review required", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return (0, 0, 0, 0, false);
        }
    }

    private bool ShouldBackgroundTrackCurrentResults()
    {
        if (_credentials.IsAlternate) return false; // No stored alternate domain password.
        var settings = BackgroundRepairSettings.Load();
        if (!settings.Enabled) return false;
        try
        {
            return string.Equals(FeedbackPath.ValidateRoot(_clientHealth?.FeedbackShareRoot),
                settings.FeedbackShareRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }

    private async Task SetBackgroundRepairAsync()
    {
        if (_backgroundChanging || IsDisposed || _busy) return;
        _backgroundChanging = true;
        _busy = true;
        _backgroundRepair.Enabled = false;
        bool enabled = _backgroundRepair.Checked;
        try
        {
            var settings = BackgroundRepairSettings.Load();
            if (enabled)
            {
                if (_credentials.IsAlternate)
                    throw new InvalidOperationException(
                        "Background RPC cannot use alternate credentials without a stored password. " +
                        "Connect using the current Windows domain account.");
                string root = FeedbackPath.ValidateRoot(_clientHealth?.FeedbackShareRoot);
                bool reachable = await Task.Run(() => Directory.Exists(root));
                if (!reachable)
                    throw new DirectoryNotFoundException(
                        "Configure an accessible Client Health SMB share first. " +
                        "Without reports, automatic background processing will not start.");
                string executable = BackgroundRepairTask.CurrentExecutable();
                if (MessageBox.Show(this,
                    "Install a Windows Task Scheduler task for this Windows user?\n\n" +
                    "It will run one hidden cycle every 2 minutes, even after the manager closes, " +
                    "but ONLY while this user remains signed in to Windows.\n\n" +
                    "New Repair tasks require separate Preview and GUID confirmation. " +
                    "Old jobs are not authorized automatically. Passwords are not stored.\n\n" +
                    "At most one RPC retry per cycle; changed OU, group, or GPO blocks execution.",
                    "Background Deferred Repair", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    _backgroundRepair.Checked = false;
                    return;
                }
                await Task.Run(() => BackgroundRepairTask.Install(executable));
                settings.Enabled = true;
                settings.AccountSid = BackgroundRepairIdentity.CurrentSid();
                settings.FeedbackShareRoot = root;
                settings.ExecutablePath = executable;
                settings.EnabledUtc = DateTimeOffset.UtcNow;
                try { settings.Save(); }
                catch
                {
                    try { await Task.Run(BackgroundRepairTask.Uninstall); }
                    catch (Exception cleanupEx) { AppLog.Write(cleanupEx, "Remove inactive background task"); }
                    throw;
                }
            }
            else
            {
                // Persist fail-closed state before uninstalling the Scheduler task.
                settings.AccountSid = BackgroundRepairIdentity.CurrentSid();
                settings.Enabled = false;
                settings.Save();
                await Task.Run(BackgroundRepairTask.Uninstall);
            }
            _status.Text = enabled
                ? "Background Repair enabled for current Windows user; checks every 2 minutes."
                : "Background Repair disabled; previously approved tasks remain queued.";
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Toggle background Repair");
            UiErrorDialog.Show(this, "Background Repair", ex);
            bool actual = BackgroundRepairSettings.Load().Enabled && !_credentials.IsAlternate;
            _backgroundRepair.Checked = actual;
        }
        finally
        {
            _backgroundRepair.Enabled = !_credentials.IsAlternate;
            _backgroundChanging = false;
            _busy = false;
            if (!IsDisposed) RefreshDeferredRows();
        }
    }

    private async Task RetryDeferredAsync(bool force)
    {
        if (_busy || _deferredWorking || IsDisposed) return;
        _deferredWorking = true;
        _busy = true; // Prevent other AD operations during asynchronous SMB checks and remote RPC.
        UseWaitCursor = true;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var entries = DeferredRepairQueue.ForScope(_domain, _dc, _ouDn, _groupDn);
            // Expired approval is terminal, never silently renew an old permission.
            foreach (var expired in entries.Where(x => x.State == "Queued" &&
                       (x.ApprovedUtc < now.AddDays(-14) || x.Attempts >= 20)))
                DeferredRepairQueue.Update(expired.Id, "NeedsAttention",
                    "Operator approval expired or retry limit reached", now);

            string? selectedId = force
                ? _deferredGrid.SelectedRows.Cast<DataGridViewRow>()
                    .Select(r => r.Tag as string).FirstOrDefault()
                : null;
            if (force && string.IsNullOrWhiteSpace(selectedId))
            {
                _status.Text = UiLocale.Display("Select a Deferred Repair record to inspect.");
                return;
            }

            // Check locally self-healed clients BEFORE any additional remote RPC.
            await CheckHealthConfirmationsAsync(DateTimeOffset.UtcNow, selectedId);
            await CheckOfflineHealthAsync(DateTimeOffset.UtcNow, selectedId);

            var due = DeferredRepairQueue.ForScope(_domain, _dc, _ouDn, _groupDn)
                .Where(x => x.State == "Queued" && x.Attempts < 20 &&
                    x.ApprovedUtc > now.AddDays(-14) &&
                    (force || x.NextAttemptUtc <= now))
                .OrderBy(x => x.NextAttemptUtc).ToArray();
            var item = force
                ? due.FirstOrDefault(x => x.Id == selectedId)
                : due.FirstOrDefault();
            if (item is null)
            {
                if (force)
                    _status.Text = "Report checked. Queued may initiate RPC; AwaitingHealth/AwaitingTask " +
                        "only check reports; Attempting is not retried after interruption; " +
                        "NeedsAttention requires fresh approval.";
                return;
            }
            _deferredMessage.Text = "Checking connectivity and AD scope: " + item.ComputerFqdn;

            // Durable marker is written before the remote call. After a crash,
            // an Attempting job is only inspected, never automatically re-run.
            if (!DeferredRepairQueue.BeginAttempt(item.Id, DateTimeOffset.UtcNow))
                return;
            try
            {
                var outcome = await Task.Run(() => ExecuteDeferredOperation(item));
                var result = outcome.Result switch
                {
                    "offline" => "offline",
                    "success" => "success",
                    "pending" => "pending",
                    _ => "failed"
                };
                DeferredRepairQueue.FinishAttempt(item.Id, result, DateTimeOffset.UtcNow);
                if (result == "success")
                    CampaignHistoryStore.TryRecordEvent(item.CampaignId, "Deferred Repair task executed",
                        "Remote task exited with code 0; client health has NOT yet been confirmed.");
            }
            catch (Exception ex)
            {
                // Fail closed: interrupted RPC may have created the remote task.
                // If completion of the in-flight marker also fails, Attempting
                // remains durable and will NEVER schedule another RPC.
                try { DeferredRepairQueue.FinishAttempt(item.Id, "failed", DateTimeOffset.UtcNow); }
                catch (Exception queueEx) { AppLog.Write(queueEx, "Deferred crash-safe queue"); }
                AppLog.Write(ex, "Deferred Repair");
            }
        }
        catch (Exception ex)
        {
            _deferredMessage.Text = "Could not inspect queue: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message);
            AppLog.Write(ex, "Deferred repair processing");
        }
        finally
        {
            _busy = false;
            _deferredWorking = false;
            UseWaitCursor = false;
            if (!IsDisposed) RefreshDeferredRows();
        }
    }

    private async Task CheckHealthConfirmationsAsync(DateTimeOffset now, string? selectedId)
    {
        var awaiting = DeferredRepairQueue.ForScope(_domain, _dc, _ouDn, _groupDn)
            .Where(x => x.State is "Attempting" or "AwaitingHealth" or "AwaitingTask")
            .Where(x => selectedId is not null ? x.Id == selectedId : x.NextAttemptUtc <= now)
            .OrderBy(x => x.NextAttemptUtc)
            .Take(10).ToArray(); // Fair, bounded SMB checks per 60-second tick.
        if (awaiting.Length == 0) return;
        string share = _clientHealth?.FeedbackShareRoot ?? "";
        bool shareConfigured;
        try { _ = FeedbackPath.ValidateRoot(share); shareConfigured = true; }
        catch (ArgumentException) { shareConfigured = false; }

        foreach (var item in awaiting)
        {
            if (!item.SubmittedUtc.HasValue)
            {
                DeferredRepairQueue.FlagForReview(item.Id,
                    "Remote task was pending before health-confirmation support; inspect manually.", now);
                continue;
            }
            if (now - item.SubmittedUtc.Value > TimeSpan.FromHours(2))
            {
                DeferredRepairQueue.FlagForReview(item.Id,
                    "No confirmed healthy report within 2 hours after remote task.", now);
                CampaignHistoryStore.TryRecordEvent(item.CampaignId,
                    "Deferred Repair health timeout", "No fresh healthy report; needs manual verification.");
                continue;
            }
            if (!shareConfigured)
            {
                _deferredMessage.Text = "SMB share is not configured in Client Health; deferred repair awaits confirmation.";
                continue;
            }
            try
            {
                string shortName = item.ComputerFqdn.Split('.')[0];
                var record = await Task.Run(() => ClientFeedbackReader.ReadSingle(
                    share, item.CampaignId, shortName, DateTimeOffset.UtcNow));
                if (record is null || record.LastSeenUtc < item.SubmittedUtc.Value)
                    continue; // Older feedback cannot prove execution.
                if (DeferredRepairQueue.CompleteVerified(item.Id, record, DateTimeOffset.UtcNow))
                {
                    CampaignHistoryStore.TryRecordEvent(item.CampaignId,
                        "Deferred Repair: healthy report received",
                        "New client report claims correct JPG, registry and scheduled task; verify on endpoint if needed.");
                }
                else if (now - item.SubmittedUtc.Value > TimeSpan.FromMinutes(5))
                {
                    // Fresh but unhealthy report: no duplicate remote task.
                    DeferredRepairQueue.FlagForReview(item.Id,
                        "Fresh client report is not healthy; inspect agent log and original backup.", now);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidDataException or JsonException or ArgumentException)
            {
                // Reporting share unavailable/invalid: wait for next poll, never repeat RPC.
                AppLog.Write(ex, "Deferred client SMB confirmation");
                _status.Text = UiLocale.Display("Client report is unavailable or invalid. RPC is not retried.");
            }
            finally
            {
                // Move to the back of the polling queue; avoid starving other clients
                // when there are more than ten awaiting reports.
                DeferredRepairQueue.ScheduleHealthPoll(item.Id, DateTimeOffset.UtcNow);
            }
        }
    }

    private async Task CheckOfflineHealthAsync(DateTimeOffset now, string? selectedId)
    {
        string share = _clientHealth?.FeedbackShareRoot ?? "";
        try { share = FeedbackPath.ValidateRoot(share); }
        catch (ArgumentException) { return; } // No telemetry share: preserve ordinary approved RPC retry.

        var offline = DeferredRepairQueue.ForScope(_domain, _dc, _ouDn, _groupDn)
            .Where(x => x.State == "Queued" && x.ApprovedUtc >= now.AddDays(-14))
            .Where(x => selectedId is not null ? x.Id == selectedId : x.NextAttemptUtc <= now)
            .OrderBy(x => x.NextAttemptUtc)
            .Take(10).ToArray();
        foreach (var item in offline)
        {
            try
            {
                var record = await Task.Run(() => ClientFeedbackReader.ReadSingle(
                    share, item.CampaignId, item.ComputerFqdn.Split('.')[0], DateTimeOffset.UtcNow));
                if (record is not null &&
                    DeferredRepairQueue.CompleteHealthyOffline(item.Id, record, DateTimeOffset.UtcNow))
                {
                    CampaignHistoryStore.TryRecordEvent(item.CampaignId,
                        "Deferred Repair: self-healed before RPC",
                        "Client agent reported healthy after approval; queued RPC was skipped.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Write(ex, "Deferred offline SMB availability");
                break; // Avoid repeatedly waiting for an unavailable share.
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException)
            {
                AppLog.Write(ex, "Deferred offline client report");
                // Invalid client telemetry does not authorize, postpone or repeat RPC.
            }
        }
    }

    private (string Result, string Message) ExecuteDeferredOperation(DeferredRepairItem item) =>
        DeferredRemoteRepair.Execute(item, _credentials);
}
