using System.Windows.Forms;

namespace LockScreenCampaignManager;

internal sealed partial class GpoOperationsForm
{
    // Called only by offline UI smoke. The form is never shown and all data
    // are synthetic; no AD, RPC or SMB read or remote task is performed.
    internal void TestAgentEvidenceSourceAndOverview()
    {
        ApplyAgentOverviewLanguage("English");
        if (!_agentScopeStatus.Text.Contains("AD OU: ?", StringComparison.Ordinal) ||
            !_agentReportsStatus.Text.Contains("SMB reports: ? / ?", StringComparison.Ordinal))
            throw new InvalidOperationException("Unverified agent inventory appeared as zero.");

        _agentInventoryVerified = true;
        _adOuComputerTotal = 0;
        UpdateAgentOverview(0);
        if (!_agentScopeStatus.Text.Contains("AD OU: 0", StringComparison.Ordinal) ||
            !_agentReportsStatus.Text.Contains("SMB reports: ? / 0", StringComparison.Ordinal))
            throw new InvalidOperationException("Verified empty AD scope conflated with unknown reports.");

        _adOuComputerTotal = 7;
        var now = DateTimeOffset.UtcNow;
        var history = new AgentTaskOutcome("Report", "success", now, "Synthetic submission");
        var evidence = AgentReportEvidence.Interpret("PILOT", "synthetic-campaign",
            new ClientFeedbackRecord("PILOT", "synthetic-campaign", now, "Active",
                "Healthy", "test.jpg", false, true, true, 3, 2,
                "0.4.0-preview.127", "PowerShell"), now, 15);
        // No AD last-seen value: the default activity view does not select this
        // row or schedule any CheckedListBox selection event.
        _agentInventory.Add(new PcRow("pilot.example.com", "PILOT$",
            RpcReachable: true, CheckedUtc: now, AgentReport: evidence,
            LastAction: history, MethodAgreement: "Reported match"));
        _agentShareAvailable = true;
        _agentValidReportCount = 1;
        _agentReportsCheckedUtc = now;
        _agentEvidenceNotice = "Validated client reports for selected campaign; client-supplied status is not authenticated proof.";
        var listRow = _agentInventory[0];
        _computers.Items.Add(listRow);
        _computers.SelectedIndex = 0;
        int checkEvents = 0;
        _computers.ItemCheck += (_, _) => checkEvents++;
        SetAgentProbeResult(3, 7, "10:15:02", 12);
        foreach (string language in new[] { "English", "Russian", "Hebrew", "English" })
        {
            UiStyle.Apply(this, new UiPreferences { Language = language, UiScalePercent = 150 });
            if (!_agentScopeStatus.Text.Contains("7", StringComparison.Ordinal) ||
                !_agentReportsStatus.Text.Contains("1 / 1", StringComparison.Ordinal) ||
                !_agentReportsStatus.Text.Contains(
                    UiLocale.Translate(_agentEvidenceNotice, language), StringComparison.Ordinal) ||
                (language != "English" && _agentReportsStatus.Text.StartsWith("SMB reports:", StringComparison.Ordinal)) ||
                !ReferenceEquals(_agentInventory[0].AgentReport, evidence))
                throw new InvalidOperationException("Agent overview localization changed evidence or counts: " + language);
            string detail = RenderSelectedAgentDetails(_agentInventory[0]);
            if (!detail.Contains("pilot.example.com", StringComparison.Ordinal) ||
                !detail.Contains("0.4.0-preview.127", StringComparison.Ordinal) ||
                !detail.Contains("PowerShell", StringComparison.Ordinal) ||
                !detail.Contains("Synthetic submission", StringComparison.Ordinal) ||
                !detail.Contains(FleetStatusPresentation.T("Reported issue", language), StringComparison.Ordinal) ||
                !detail.Contains(FleetStatusPresentation.T("Submission accepted", language), StringComparison.Ordinal) ||
                !detail.Contains(FleetStatusPresentation.ReportDetails(evidence, language), StringComparison.Ordinal) ||
                (language != "English" && detail.StartsWith("Computer:", StringComparison.Ordinal)))
                throw new InvalidOperationException("Agent detail localization changed evidence or missed status: " + language);
            if (evidence.Condition != "Reported issue" || evidence.Details.IndexOf("missing files 3", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Canonical agent evidence was changed.");
            string itemText = _computers.GetItemText(listRow);
            if (!ReferenceEquals(_computers.SelectedItem, listRow) ||
                _computers.CheckedItems.Count != 0 || checkEvents != 0 ||
                itemText != RenderAgentListItem(listRow) ||
                !itemText.Contains("pilot.example.com", StringComparison.Ordinal) ||
                !itemText.Contains(FleetStatusPresentation.T("Reported issue", language), StringComparison.Ordinal) ||
                !_agentSelectedDetails.Text.Contains(detail, StringComparison.Ordinal) ||
                !_agentProbeStatus.Text.Contains("3/7", StringComparison.Ordinal) ||
                !_agentProbeStatus.Text.Contains("10:15:02", StringComparison.Ordinal) ||
                !_agentProbeStatus.Text.Contains(FleetStatusPresentation.Format(
                    " | {0} deferred to next scan (oldest checked first)", language, 12), StringComparison.Ordinal) ||
                (language != "English" && _agentProbeStatus.Text.StartsWith("RPC TCP/135 reachable:", StringComparison.Ordinal)))
                throw new InvalidOperationException("Agent list selection, detail or RPC status localization changed evidence: " + language);
        }

        int previousRead = _agentEvidenceGeneration;
        var share = _clientHealth!.Controls.Find("ClientHealthSmbPath", true)
            .OfType<TextBox>().Single();
        string original = share.Text;
        share.Text = @"\\example.com\OtherReports$";
        share.Text = original;
        var row = _agentInventory.Single();
        if (previousRead == _agentEvidenceGeneration || _agentShareAvailable ||
            _agentReportsCheckedUtc.HasValue || row.AgentReport is not null ||
            row.MethodAgreement != "Not checked" ||
            !ReferenceEquals(row.LastAction, history) || row.RpcReachable != true ||
            !_agentReportsStatus.Text.Contains("SMB reports: ? / 1", StringComparison.Ordinal) ||
            !_agentReportsStatus.Text.Contains("SMB source changed.", StringComparison.Ordinal))
            throw new InvalidOperationException("Agent report source edit retained stale evidence or lost independent RPC/task history.");

        _agentInventory[0] = row with { AgentReport = evidence, MethodAgreement = "Reported match" };
        _agentShareAvailable = true;
        ResetAgentReports("Cannot read SMB Client Health share; report count is UNKNOWN, not zero.");
        UpdateAgentOverview(0);
        if (_agentInventory[0].AgentReport is not null || _agentShareAvailable ||
            !_agentReportsStatus.Text.Contains("UNKNOWN, not zero", StringComparison.Ordinal))
            throw new InvalidOperationException("Failed SMB read retained prior agent report.");

        InvalidateAgentEvidence();
        if (_agentInventory[0].LastAction is not null)
            throw new InvalidOperationException("Managed GPO change retained old task submission history.");
    }
}
