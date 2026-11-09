using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Display-only interpretation of untrusted client-supplied SMB feedback.
/// Recent reports are NOT authentication, device online proof or permission
/// to run remote tasks. Every action continues to verify AD/GPO scope itself.
/// </summary>
internal sealed record AgentReportEvidence(
    string Condition, string Stage, string Version, string Health,
    DateTimeOffset? LastReportedUtc, bool Fresh, string Details)
{
    private static readonly Regex SafeVersion = new(
        @"\A[A-Za-z0-9][A-Za-z0-9.+_ -]{0,49}\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static AgentReportEvidence Missing() =>
        new("No report", "Unknown", "—", "Unknown", null, false,
            "No validated, campaign-matching SMB status received for this AD computer.");

    internal static AgentReportEvidence Interpret(string computerShortName,
        string campaignId, ClientFeedbackRecord? report,
        DateTimeOffset observedUtc, int reportIntervalMinutes)
    {
        if (reportIntervalMinutes is < 5 or > 60)
            throw new ArgumentOutOfRangeException(nameof(reportIntervalMinutes));
        if (report is null) return Missing();
        if (!FeedbackPath.IsValidHost(computerShortName) ||
            !string.Equals(report.Computer, computerShortName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(report.CampaignId, campaignId, StringComparison.Ordinal) ||
            report.LastSeenUtc.Year < 2020 || report.LastSeenUtc > observedUtc.AddMinutes(5))
            return new("Invalid report", "Unknown", "—", "Unverified", null, false,
                "SMB report identity, campaign or timestamp does not match the verified target.");

        int staleMinutes = Math.Clamp(reportIntervalMinutes * 3, 45, 180);
        bool recent = observedUtc - report.LastSeenUtc <= TimeSpan.FromMinutes(staleMinutes);
        string condition;
        if (report.Stage == "Completed") condition = "Reported completed";
        else if (!recent) condition = "Stale report";
        else if (report.Stage == "Error" || report.Health == "Error")
            condition = "Reported error";
        else if (report.Health == "RepairNeeded" || report.MissingFiles > 0 ||
                 report.CorruptFiles > 0 || !report.ImageMatches ||
                 !report.TaskPresent ||
                 (report.Stage == "Active" && !report.RegistryMatches))
            condition = "Reported issue";
        else if (report.Stage == "Pending") condition = "Reported pending";
        else if (report.Stage == "Active" && report.Health == "Healthy")
            condition = "Reported healthy";
        else condition = "Unverified report";

        string version = SafeVersion.IsMatch(report.AgentVersion) ? report.AgentVersion : "—";
        string details = "Client-reported: " + report.Stage + " / " + report.Health +
            " | image " + (report.ImageMatches ? "match" : "not verified") +
            " | registry " + (report.RegistryMatches ? "match" : "not verified") +
            " | task " + (report.TaskPresent ? "present" : "missing") +
            " | missing files " + report.MissingFiles +
            " | corrupt files " + report.CorruptFiles +
            " | timestamp supplied by endpoint; not authenticated proof.";
        return new AgentReportEvidence(condition, report.Stage, version,
            report.Health, report.LastSeenUtc, recent && report.Stage != "Completed", details);
    }
}
