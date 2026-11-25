using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

internal sealed partial class GpoOperationsForm
{
    // Run Now outcomes describe Task Scheduler submission, NOT proven agent
    // completion. They exist only in this Manager session and never grant
    // permission to launch an endpoint action.
    private sealed record AgentTaskOutcome(
        string Action, string Status, DateTimeOffset SubmittedUtc, string Detail)
    {
        internal string Display => Action + " / " + Status;
    }

    private int _adOuComputerTotal;
    private bool _agentInventoryVerified;
    private string _agentUiLanguage = "English";
    private int _agentValidReportCount;
    private int _agentInvalidReportCount;
    private DateTimeOffset? _agentReportsCheckedUtc;
    private string _agentEvidenceNotice = "Agent reports not checked. Choose a managed GPO and refresh feedback.";
    private bool _agentShareAvailable;

    private void InvalidateAgentEvidence()
    {
        ResetAgentReports("Select a managed GPO and refresh the reports.", resetTasks: true);
        if (!IsDisposed && !Disposing) DrawAgentInventory();
    }

    private void OnAgentFeedbackShareChanged()
    {
        ResetAgentReports("SMB source changed. Refresh Agent Reports for the selected GPO.");
        if (!IsDisposed && !Disposing) DrawAgentInventory();
    }

    // Do not present prior-source or failed-read reports as current.
    // RPC and local task submission history are independent of the SMB root.
    private void ResetAgentReports(string notice, bool resetTasks = false)
    {
        ++_agentEvidenceGeneration;
        _agentValidReportCount = 0;
        _agentInvalidReportCount = 0;
        _agentReportsCheckedUtc = null;
        _agentShareAvailable = false;
        _agentEvidenceNotice = notice;
        for (int i = 0; i < _agentInventory.Count; ++i)
        {
            var row = _agentInventory[i];
            _agentInventory[i] = row with {
                AgentReport = null, LastAction = resetTasks ? null : row.LastAction,
                MethodAgreement = "Not checked"
            };
        }
    }

    private async Task RefreshAgentEvidenceAsync()
    {
        if (_agentEvidenceLoading || !_agentInventoryVerified || _agentInventory.Count == 0 ||
            IsDisposed || Disposing || _agentTabControl is null)
            return;

        if (_policies.SelectedItem is not GpoRow policy ||
            !NativeSysvolStorage.ValidCampaignId(policy.CampaignId))
        {
            ResetAgentReports("Choose a managed GPO to read that campaign's SMB reports.");
            DrawAgentInventory();
            return;
        }

        string share = _clientHealth?.FeedbackShareRoot?.Trim() ?? "";
        try { share = FeedbackPath.ValidateRoot(share); }
        catch (ArgumentException)
        {
            ResetAgentReports("Client feedback share is missing or invalid. Configure SMB Client Health.");
            DrawAgentInventory();
            return;
        }

        var names = _agentInventory.Select(x => x.Name.Split('.')[0].ToUpperInvariant())
            .Where(FeedbackPath.IsValidHost).ToArray();
        // A short name cannot safely identify two endpoints. No ambiguous
        // SMB evidence may be attached to either AD computer.
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != _agentInventory.Count)
        {
            ResetAgentReports("Duplicate or invalid AD short computer names: cannot map SMB reports safely.");
            DrawAgentInventory();
            return;
        }

        string campaign = policy.CampaignId;
        int scopeGeneration = _agentInventoryGeneration;
        ResetAgentReports("Reading validated, campaign-matching SMB reports...");
        int requestGeneration = _agentEvidenceGeneration;
        _agentEvidenceLoading = true;
        if (_agentRefreshReportsButton is not null) _agentRefreshReportsButton.Enabled = false;
        DrawAgentInventory();
        try
        {
            var now = DateTimeOffset.UtcNow;
            int staleMinutes = Math.Clamp(_project.FeedbackIntervalMinutes * 3, 45, 180);
            ClientHealthSnapshot snapshot = await Task.Run(() =>
                ClientFeedbackReader.Read(share, campaign, names, now, staleMinutes));
            if (IsDisposed || Disposing ||
                scopeGeneration != _agentInventoryGeneration ||
                requestGeneration != _agentEvidenceGeneration ||
                _policies.SelectedItem is not GpoRow selected ||
                selected.Id != policy.Id)
                return;

            int reported = 0;
            for (int i = 0; i < _agentInventory.Count; ++i)
            {
                PcRow row = _agentInventory[i];
                string shortName = row.Name.Split('.')[0].ToUpperInvariant();
                snapshot.LatestReports.TryGetValue(shortName, out var report);
                if (report is not null) reported++;
                var interpretation = AgentReportEvidence.Interpret(
                    shortName, campaign, report, now, _project.FeedbackIntervalMinutes);
                _agentInventory[i] = row with {
                    AgentReport = interpretation,
                    MethodAgreement = report is null ? "No report" :
                        AgentReportEvidence.MethodAgreement(policy.ClientType, interpretation.ClientType)
                };
            }

            _agentValidReportCount = reported;
            _agentInvalidReportCount = snapshot.InvalidReports;
            _agentReportsCheckedUtc = now;
            _agentShareAvailable = true;
            _agentEvidenceNotice = "Validated client reports for selected campaign; client-supplied status is not authenticated proof.";
            DrawAgentInventory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException or System.Security.SecurityException)
        {
            if (IsDisposed || Disposing || scopeGeneration != _agentInventoryGeneration ||
                requestGeneration != _agentEvidenceGeneration) return;
            ResetAgentReports("Cannot read SMB Client Health share; report count is UNKNOWN, not zero.");
            AppLog.Write(ex, "Agent Management read client feedback");
            DrawAgentInventory();
        }
        finally
        {
            _agentEvidenceLoading = false;
            if (!IsDisposed && !Disposing && _agentRefreshReportsButton is not null)
                _agentRefreshReportsButton.Enabled = true;
        }
    }

    private void UpdateAgentTaskOutcomes(JsonElement result, string action)
    {
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("results", out JsonElement rows) ||
            rows.ValueKind != JsonValueKind.Array) return;

        DateTimeOffset utc = DateTimeOffset.UtcNow;
        var parsed = new Dictionary<string, AgentTaskOutcome>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in rows.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string host = ReadTaskField(item, "host", 253);
            string state = ReadTaskField(item, "status", 24);
            if (host.Length == 0 ||
                !new[] { "success", "pending", "offline", "failed" }.Contains(state,
                    StringComparer.OrdinalIgnoreCase)) continue;
            string description = ReadTaskField(item, "note", 160);
            parsed[host] = new AgentTaskOutcome(action, state, utc, description);
        }
        for (int i = 0; i < _agentInventory.Count; i++)
        {
            PcRow pc = _agentInventory[i];
            if (parsed.TryGetValue(pc.Name, out var status))
                _agentInventory[i] = pc with { LastAction = status };
        }
        DrawAgentInventory();
    }

    private static string ReadTaskField(JsonElement item, string name, int max)
    {
        if (!item.TryGetProperty(name, out JsonElement field) ||
            field.ValueKind != JsonValueKind.String) return "";
        string value = field.GetString() ?? "";
        if (value.Length > max || value.Any(char.IsControl)) return "";
        return value;
    }

    private void DisplaySelectedAgentDetails()
    {
        if (_agentSelectedDetails.IsDisposed) return;
        if (_computers.SelectedItem is not PcRow row)
        {
            _agentSelectedDetails.Text = "Select a verified computer. AD activity is approximate; SMB reports are untrusted.";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Computer: " + row.Name);
        sb.AppendLine("AD last activity (replicated, approximate): " +
            (row.AdLastSeenUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "Never / unknown") +
            " | Disabled: " + (row.AdDisabled?.ToString() ?? "Unknown"));
        sb.AppendLine("RPC TCP/135: " +
            (row.RpcReachable is null ? "NOT CHECKED" :
                row.RpcReachable.Value ? "Reachable" : "Not reachable") +
            " | Probe: " + (row.CheckedUtc?.ToLocalTime().ToString("dd.MM HH:mm:ss") ?? "—"));
        var evidence = row.AgentReport;
        sb.AppendLine("Client report: " + (evidence?.Condition ?? "Not checked") +
            " | Version: " + (evidence?.Version ?? "—") +
            " | Last: " + (evidence?.LastReportedUtc?.ToLocalTime().ToString("dd.MM HH:mm") ?? "—"));
        if (evidence is not null) sb.AppendLine(evidence.Details);
        sb.AppendLine("Managed GPO method: " +
            (_policies.SelectedItem is GpoRow managed ? managed.ClientType : "Unknown") +
            " | Client-reported type: " + (evidence?.ClientType ?? "Unknown") +
            " | Comparison: " + row.MethodAgreement);
        if (row.MethodAgreement == "Reported mismatch")
            sb.AppendLine("WARNING: Client-reported type differs from the verified managed GPO method. " +
                "Verify installed endpoint binary before any repair.");
        sb.AppendLine("Last remote task submission: " +
            (row.LastAction is { } task
                ? task.Display + " at " + task.SubmittedUtc.ToLocalTime().ToString("dd.MM HH:mm:ss")
                : "No recorded command in this Manager session"));
        if (row.LastAction is { } result)
            sb.AppendLine("Task result: " + result.Detail);
        _agentSelectedDetails.Text = sb.ToString();
    }

    private static bool MatchesAdActivity(PcRow row, string activity, DateTimeOffset utc)
    {
        if (activity == "All eligible") return true;
        if (activity == "Never / unknown") return row.AdLastSeenUtc is null;
        if (activity == "Disabled computers") return row.AdDisabled == true;
        if (activity == "Older than 90 days")
            return row.AdLastSeenUtc is { } stale && stale < utc.AddDays(-90);
        if (row.AdDisabled != false || row.AdLastSeenUtc is not { } seen)
            return false;
        int days = activity switch
        {
            "Active <= 30 days" => 30,
            "Active <= 60 days" => 60,
            "Active <= 90 days" => 90,
            "Active <= 180 days" => 180,
            "Active <= 365 days" => 365,
            _ => 90
        };
        return seen >= utc.AddDays(-days) && seen <= utc.AddDays(2);
    }

    // Called by UiLocale.Apply; refresh presentation only, not the domain or
    // the checked computer list. All stored statuses remain canonical English.
    internal void ApplyAgentOverviewLanguage(string language)
    {
        _agentUiLanguage = language;
        UpdateAgentOverview(_computers.Items.Count);
    }

    private void UpdateAgentOverview(int visible)
    {
        if (_agentReportsStatus.IsDisposed) return;
        int eligible = _agentInventory.Count;
        int disabled = _agentInventory.Count(pc => pc.AdDisabled == true);
        int inactive = _agentInventory.Count(pc =>
            pc.AdLastSeenUtc is null || pc.AdLastSeenUtc < DateTimeOffset.UtcNow.AddDays(-90));
        int fresh = _agentInventory.Count(pc => pc.AgentReport?.Fresh == true);
        int methodMismatch = _agentInventory.Count(pc => pc.MethodAgreement == "Reported mismatch");
        string T(string key) => UiLocale.Translate(key, _agentUiLanguage);
        string Count(bool known, int value) =>
            known ? value.ToString(CultureInfo.InvariantCulture) : "?";
        string scopeSummary = string.Format(CultureInfo.InvariantCulture, T(
            "AD OU: {0} | OU + security group: {1} | Visible after filters: {2} | Disabled: {3} | AD inactive / unknown >90d: {4}"),
            Count(_agentInventoryVerified, _adOuComputerTotal),
            Count(_agentInventoryVerified, eligible),
            Count(_agentInventoryVerified, visible),
            Count(_agentInventoryVerified, disabled),
            Count(_agentInventoryVerified, inactive));
        if (!_agentInventoryVerified)
            scopeSummary += Environment.NewLine + T("Agent AD inventory not verified.");
        UiStyle.ApplyStatus(_agentScopeStatus, scopeSummary,
            _agentInventoryVerified ? UiStatusKind.Neutral : UiStatusKind.Warning);
        _agentScopeStatus.AccessibleDescription = scopeSummary + Environment.NewLine +
            T("Agent AD activity is approximate, not live online status.");

        bool reportsKnown = _agentInventoryVerified && _agentShareAvailable &&
            _agentReportsCheckedUtc.HasValue;
        string checkedAt = reportsKnown
            ? _agentReportsCheckedUtc!.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : T("SMB check not verified");
        string feedbackSummary = string.Format(CultureInfo.InvariantCulture, T(
            "SMB reports: {0} / {1} eligible | Recent at check: {2} | Client type mismatch: {3} | No report: {4} | Invalid files: {5} | Checked: {6}"),
            Count(reportsKnown, _agentValidReportCount), Count(_agentInventoryVerified, eligible),
            Count(reportsKnown, fresh), Count(reportsKnown, methodMismatch),
            Count(reportsKnown, eligible - _agentValidReportCount),
            Count(reportsKnown, _agentInvalidReportCount), checkedAt);
        // A failure/retry reason must be visible, not only in accessibility metadata.
        feedbackSummary += Environment.NewLine + T(_agentEvidenceNotice);
        UiStyle.ApplyStatus(_agentReportsStatus, feedbackSummary,
            reportsKnown ? UiStatusKind.Success : UiStatusKind.Warning);
        _agentReportsStatus.AccessibleDescription = feedbackSummary + Environment.NewLine +
            T("Agent reports are untrusted diagnostic hints, not online proof.");
    }
}
