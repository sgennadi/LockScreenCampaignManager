namespace LockScreenCampaignManager;

/// <summary>
/// Operator-controlled correlation of THREE independent evidence sources:
/// DC-wide GPO files, unsigned gpresult XML, and a fresh read-only endpoint
/// power observation. None of the sources prove actual Screen Saver display,
/// current interactive HKCU, final setting precedence, or a causal blocker.
/// </summary>
internal sealed record DomainPilotFinding(
    string Area, string State, string Observation, string NextStep);

internal sealed class DomainGpoPilotReadiness
{
    internal string Computer { get; private init; } = "";
    internal string OuDn { get; private init; } = "";
    internal string Dc { get; private init; } = "";
    internal int SaverIdleSeconds { get; private init; }
    internal DateTimeOffset BuiltUtc { get; private init; }
    internal bool PowerObserved { get; private init; }
    internal bool ComputerXmlImported { get; private init; }
    internal bool UserXmlImported { get; private init; }
    internal IReadOnlyList<DomainPilotFinding> Findings { get; private init; } = [];
    internal int Concerns => Findings.Count(x => x.State == "Observed concern");
    internal int Potential => Findings.Count(x => x.State == "Source candidate");
    internal int NotVerified => Findings.Count(x => x.State == "Not verified");
    internal bool ProvesScreenSaverDisplayed => false;
    internal bool ProvesWinningGpo => false;
    internal bool PermitsAutomaticRemediation => false;

    internal static DomainGpoPilotReadiness Build(
        DomainWideGpoReport report, string computer, string selectedComputerOu,
        int saverIdleSeconds, EndpointPowerObservation? power,
        RsopEvidence? computerXml, UserRsopEvidence? userXml,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (saverIdleSeconds is < 60 or > 86400 ||
            !NativeGpoConflictScan.SafeComputerName(computer, report.Domain))
            throw new InvalidDataException(
                "Pilot requires a valid domain computer FQDN and Screen Saver idle time.");
        if (selectedComputerOu.Length > 0 &&
            !report.ComputerOus.Contains(selectedComputerOu,
                StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Selected pilot Computer OU is not part of the scanned domain GPO scope.");
        if (report.ComputerOus.Count > 0 && selectedComputerOu.Length == 0)
            throw new InvalidDataException("Select a scanned Computer OU for the pilot.");

        if (computerXml is not null &&
            (!computerXml.Computer.Equals(computer, StringComparison.OrdinalIgnoreCase) ||
             !computerXml.Domain.Equals(report.Domain, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                "Computer gpresult XML belongs to a different domain or computer.");
        if (userXml is not null &&
            !userXml.Domain.Equals(report.Domain, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("User gpresult XML belongs to another domain.");
        if (power is not null &&
            (!power.Domain.Equals(report.Domain, StringComparison.OrdinalIgnoreCase) ||
             !NativeGpoLifecycleList.SameController(power.Dc, report.Dc) ||
             !power.Computer.Equals(computer, StringComparison.OrdinalIgnoreCase) ||
             !power.OuDn.Equals(selectedComputerOu, StringComparison.OrdinalIgnoreCase) ||
             power.ObservedUtc > now.AddMinutes(2)))
            throw new InvalidDataException(
                "Endpoint power reading does not match the selected DC/OU/computer.");

        var findings = new List<DomainPilotFinding>();
        static void Add(List<DomainPilotFinding> findings,
            string area, string state, string observed, string next) =>
            findings.Add(new DomainPilotFinding(area, state, observed, next));

        if (now - report.ObservedUtc > TimeSpan.FromMinutes(15))
            Add(findings, "DC inventory", "Not verified",
                "The selected-DC GPO source scan is over 15 minutes old.",
                "Re-run Scan all DC GPOs before interpreting current links or source versions.");
        else
            Add(findings, "DC inventory", "Information",
                report.GpoCount + " domain GPO objects inventoried on " + report.Dc +
                "; only supported blocker settings were interpreted.",
                "Do not treat this inventory as all CSE settings or a winning policy.");

        if (report.Issues.Count > 0 || !report.RecognizedSourcesReadable)
            Add(findings, "DC source integrity", "Not verified",
                report.Issues.Count + " source read issues. Some policy files or " +
                "GPC/SYSVOL state are not verified.",
                "Review the all-domain scan issues and DC replication before ruling out a GPO.");
        else
            Add(findings, "DC source integrity", "Information",
                "Known scanner sources were read without reported errors; " +
                "unsupported extensions remain unknown.",
                "Review scripts, CSEs, GPP targeting, ACL/WMI and site links separately.");

        AddXml(findings, "Computer", computerXml?.ReadUtc,
            computerXml?.IsHistorical, computerXml?.Sha256, computerXml?.UnidentifiedEntries);
        AddXml(findings, "User", userXml?.ReadUtc,
            userXml?.IsHistorical, userXml?.Sha256, userXml?.UnidentifiedEntries);

        // A GPO being "Reported applied" only raises the priority for review.
        // This is NOT a per-setting winner and is never a confirmed policy block.
        var correlated = DomainGpoRsopCorrelation.Compare(report, computerXml, userXml);
        foreach (string scope in new[] { "Computer", "User" })
        {
            var blockers = correlated.Where(x => x.Scope == scope &&
                x.LinkStatus == "Linked candidate" &&
                x.SourceRisk is ("High" or "Warning")).ToArray();
            int reportedApplied = blockers.Count(x => x.ReportedRsop == "Reported applied");
            int reportedFiltered = blockers.Count(x => x.ReportedRsop == "Reported filtered");
            int unknown = blockers.Length - reportedApplied - reportedFiltered;
            if (reportedApplied > 0)
                Add(findings, scope + " GPO source", "Source candidate",
                    reportedApplied + " candidate blocker setting(s) belong to GPOs " +
                    "reported applied in unsigned " + scope + " gpresult XML.",
                    "Inspect per-setting RSoP and the actual client state. " +
                    "The setting may be overridden, filtered or from an older GPO version.");
            if (unknown > 0)
                Add(findings, scope + " GPO source", "Not verified",
                    unknown + " linked candidate blocker setting(s) lack conclusive " +
                    scope + " gpresult GPO flags.",
                    "Collect a fresh full " + scope + " gpresult XML in the correct context.");
            if (reportedFiltered > 0)
                Add(findings, scope + " GPO source", "Information",
                    reportedFiltered + " linked candidate blocker setting(s) are " +
                    "reported filtered in this imported XML.",
                    "Historical filtering does not prove they are excluded for all clients.");
            if (blockers.Length == 0)
                Add(findings, scope + " GPO source", "Information",
                    "No recognized linked high/warning sources in the selected-DC inventory.",
                    "Unsupported CSEs and policies outside selected OUs remain unverified.");
        }

        if (power is null)
            Add(findings, "Endpoint power", "Not verified",
                "No read-only active power-plan/HKLM observation was made on the pilot PC.",
                "With authorization, select the verified Computer OU and run Verify power (PC).");
        else
        {
            if (now - power.ObservedUtc > TimeSpan.FromMinutes(5))
                Add(findings, "Endpoint power", "Not verified",
                    "Power observation is more than five minutes old.",
                    "Repeat the read-only power observation on the same approved PC.");
            if (power.PlanStatus != "Available" || power.RegistryStatus != "Readable")
                Add(findings, "Endpoint power", "Not verified",
                    "Active-plan WMI: " + power.PlanStatus +
                    "; policy HKLM: " + power.RegistryStatus + ".",
                    "Incomplete or unavailable sources are not proof of normal power behavior.");
            var disputes = power.SourceDisagreements();
            foreach (var mismatch in disputes)
                Add(findings, "Endpoint power", "Observed concern",
                    mismatch.Kind + " " + mismatch.Supply +
                    ": active plan " + mismatch.Wmi + " s vs policy HKLM " +
                    mismatch.Policy + " s (source values disagree).",
                    "Inspect effective powercfg on the actual PC; source precedence is unknown.");

            foreach (var value in power.Assess(saverIdleSeconds))
            {
                if (value.Status != "Present")
                {
                    if (value.Status is "Unreadable" or "Unavailable")
                        Add(findings, "Endpoint power", "Not verified",
                            value.Kind + " " + value.Supply + " from " +
                            value.Source + " could not be read.",
                            "Validate the selected DC, endpoint rights and power query.");
                    continue;
                }
                if (value.Seconds is not long seconds || seconds == 0)
                    continue; // zero means Never for this individual timer
                string area = value.Kind == "Display" ? "Display off" :
                    value.Kind == "Sleep" ? "Sleep" : "Machine inactivity lock";
                if (seconds < saverIdleSeconds)
                    Add(findings, area, "Observed concern",
                        value.Source + " / " + value.Supply + " = " + seconds +
                        " s, earlier than configured Screen Saver " +
                        saverIdleSeconds + " s.",
                        "Potential earlier power/lock event; verify active power plan, " +
                        "user policy and actual idle behavior. This is not screen-state proof.");
                else if (seconds == saverIdleSeconds)
                    Add(findings, area, "Source candidate",
                        value.Source + " / " + value.Supply +
                        " matches configured Screen Saver threshold (" + seconds + " s).",
                        "Same threshold: order of execution and actual user Screen Saver are unknown.");
            }
        }

        // No general readiness outcome can bypass this mandatory manual gate.
        Add(findings, "Interactive User", "Not verified",
            "User HKCU Screen Saver settings and logon/RDS session are not " +
            "read directly by the selected-DC machine audit.",
            "Confirm the actual signed-in user's registry/RSoP and test Screen Saver in-session.");
        Add(findings, "Visual acceptance", "Not verified",
            "No pixel-level verification of Lock Screen, Screen Saver, resume " +
            "password, wake or multi-monitor display exists in these sources.",
            "Test manually on Windows 10 and Windows 11 in an isolated pilot.");
        return new DomainGpoPilotReadiness
        {
            Computer = computer, OuDn = selectedComputerOu, Dc = report.Dc,
            SaverIdleSeconds = saverIdleSeconds, BuiltUtc = now.ToUniversalTime(),
            PowerObserved = power is not null,
            ComputerXmlImported = computerXml is not null,
            UserXmlImported = userXml is not null, Findings = findings
        };
    }

    private static void AddXml(List<DomainPilotFinding> findings, string scope,
        DateTimeOffset? read, bool? historical, string? digest, int? unresolved)
    {
        if (read is null)
        {
            findings.Add(new DomainPilotFinding(scope + " gpresult", "Not verified",
                "No " + scope + " RSoP XML was imported.",
                "Import a private local gpresult /scope " +
                scope.ToLowerInvariant() + " /x XML from the intended pilot context."));
            return;
        }
        findings.Add(new DomainPilotFinding(scope + " gpresult",
            historical == true ? "Not verified" : "Information",
            "Unsigned manual XML: " + read.Value.ToString("u") +
            "; SHA-256 " + (digest ?? "unavailable") +
            "; unidentified GPO entries: " + (unresolved ?? 0) +
            (historical == true ? " [HISTORICAL]" : ""),
            "Confirm the report's real endpoint/user identity and time. " +
            "A checksum does NOT authenticate origin or current policy."));
    }
}
