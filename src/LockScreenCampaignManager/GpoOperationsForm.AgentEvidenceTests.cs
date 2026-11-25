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
        var evidence = new AgentReportEvidence("Reported healthy", "Active",
            "0.4.0-preview.127", "Healthy", now, true, "Synthetic evidence", "PowerShell");
        // No AD last-seen value: the default activity view does not select this
        // row or schedule any CheckedListBox selection event.
        _agentInventory.Add(new PcRow("pilot.example.com", "PILOT$",
            RpcReachable: true, CheckedUtc: now, AgentReport: evidence,
            LastAction: history, MethodAgreement: "Reported match"));
        _agentShareAvailable = true;
        _agentValidReportCount = 1;
        _agentReportsCheckedUtc = now;
        _agentEvidenceNotice = "Validated client reports for selected campaign; client-supplied status is not authenticated proof.";
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
