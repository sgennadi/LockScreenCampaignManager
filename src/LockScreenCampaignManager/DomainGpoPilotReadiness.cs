namespace LockScreenCampaignManager;

/// <summary>
/// Operator-controlled correlation of DC-wide GPO source inventory, AD-only
/// principal scope, optional unsigned gpresult XML and endpoint observations.
/// None of the sources prove actual Screen Saver display,
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
    internal bool EndpointUserImported { get; private init; }
    internal bool AdScopeObserved { get; private init; }
    internal IReadOnlyList<DomainPilotFinding> Findings { get; private init; } = [];
    internal IReadOnlyList<DomainPilotBlocker> NamedBlockers { get; private init; } = [];
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
        DateTimeOffset now, EndpointUserReadinessEvidence? endpointUser = null,
        DomainGpoScopeEvidence? adScope = null)
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
        if (endpointUser is not null &&
            (!endpointUser.Computer.Equals(computer, StringComparison.OrdinalIgnoreCase) ||
             !endpointUser.Domain.Equals(report.Domain, StringComparison.OrdinalIgnoreCase) ||
             endpointUser.CapturedUtc > now.AddMinutes(2)))
            throw new InvalidDataException(
                "Local User HKCU snapshot belongs to another computer/domain or is future-dated.");
        if (adScope is not null &&
            (!adScope.Computer.Equals(computer, StringComparison.OrdinalIgnoreCase) ||
             !adScope.Domain.Equals(report.Domain, StringComparison.OrdinalIgnoreCase) ||
             !NativeGpoLifecycleList.SameController(adScope.Dc, report.Dc) ||
             !adScope.ComputerOu.Equals(selectedComputerOu, StringComparison.OrdinalIgnoreCase) ||
             adScope.ObservedUtc > now.AddMinutes(2)))
            throw new InvalidDataException(
                "AD-only scope reading belongs to another pilot PC/DC/OU or is future-dated.");
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

        // An incomplete/stale DC projection does not substitute for missing
        // endpoint evidence. User policy needs an explicitly resolved AD user.
        bool currentAd = adScope is not null && adScope.Fresh(now) &&
            adScope.Complete && report.RecognizedSourcesReadable &&
            now - report.ObservedUtc <= TimeSpan.FromMinutes(15);
        AddXml(findings, "Computer", computerXml?.ReadUtc,
            computerXml?.IsHistorical, computerXml?.Sha256, computerXml?.UnidentifiedEntries,
            currentAd);
        AddXml(findings, "User", userXml?.ReadUtc,
            userXml?.IsHistorical, userXml?.Sha256, userXml?.UnidentifiedEntries,
            currentAd && adScope?.User.Length > 0);
        AddEndpointUser(findings, endpointUser, userXml, saverIdleSeconds, now);
        var adCandidates = AddAdScope(findings, report, adScope, now,
            saverIdleSeconds);

        // Imported UserResults must describe the same explicit AD user before
        // they can elevate a candidate GPO for that AD scope. Domain/RSoP XML
        // strings are unsigned, so equality is only a conservative correlation.
        bool userXmlCorrelated = userXml is not null &&
            (adScope is null || UserXmlMatchesScope(userXml, adScope));
        if (userXml is not null && adScope is not null && !userXmlCorrelated)
            Add(findings, "User XML identity correlation", "Not verified",
                "Imported User gpresult principal '" + userXml.UserPrincipal +
                "' is not linked to selected AD scope user '" +
                (adScope.User.Length == 0 ? "[no user selected]" : adScope.User) +
                "'. User XML will not elevate GPO review priority.",
                "Select the same exact AD user/SAM/UPN or recapture User gpresult " +
                "for the selected pilot. No match proves endpoint authenticity.");
        var boundUserXml = userXmlCorrelated ? userXml : null;

        // A GPO being "Reported applied" only raises the priority for review.
        // This is NOT a per-setting winner and is never a confirmed policy block.
        var correlated = DomainGpoRsopCorrelation.Compare(
            report, computerXml, boundUserXml);
        var xmlBlockers = DomainPilotBlockerEvidence.Analyze(
            correlated, report.ObservedUtc, now, computerXml, boundUserXml);
        var namedBlockers = xmlBlockers.Concat(adCandidates)
            .GroupBy(x => x.Scope + "|" + x.GpoId + "|" +
                x.RegistryKey + "|" + x.Setting + "|" + x.Value,
                StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderBy(y => BlockerEvidencePriority(y.ReviewState)).First())
            .Take(6000).ToArray();
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
                    adScope is null
                        ? "Run Analyze AD scope (no XML) to check target OU, nested " +
                          "security groups, ACL and loopback."
                        : "AD source applicability was independently analyzed. " +
                          "Optional gpresult adds endpoint-session evidence.");
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
            bool recent = power.ObservedUtc <= now.AddMinutes(2) &&
                now - power.ObservedUtc <= TimeSpan.FromMinutes(5);
            if (!recent)
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
                Add(findings, "Endpoint power",
                    recent && power.PlanStatus == "Available" &&
                        power.RegistryStatus == "Readable"
                        ? "Observed concern" : "Not verified",
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
                bool sourceComplete = value.Source == "ActivePlanWmi"
                    ? power.PlanStatus == "Available" : power.RegistryStatus == "Readable";
                if (seconds < saverIdleSeconds)
                    Add(findings, area,
                        recent && sourceComplete ? "Observed concern" : "Not verified",
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
            UserXmlImported = userXml is not null,
            EndpointUserImported = endpointUser is not null,
            AdScopeObserved = adScope is not null, Findings = findings,
            NamedBlockers = namedBlockers
        };
    }


    private static void AddEndpointUser(List<DomainPilotFinding> findings,
        EndpointUserReadinessEvidence? endpoint, UserRsopEvidence? userXml,
        int idleSeconds, DateTimeOffset now)
    {
        static void Add(List<DomainPilotFinding> target, string area, string state,
            string observed, string next) =>
            target.Add(new DomainPilotFinding(area, state, observed, next));

        if (endpoint is null)
        {
            Add(findings, "User HKCU snapshot", "Not verified",
                "No snapshot of the actual signed-in user's Screen Saver registry settings.",
                "Run the standalone endpoint probe as the pilot Windows user, " +
                "then explicitly import the private local JSON on the manager.");
            return;
        }
        bool fresh = endpoint.IsFresh(now);
        Add(findings, "User HKCU snapshot",
            fresh ? "Information" : "Not verified",
            "Unsigned " + endpoint.Session + " HKCU snapshot for " +
            endpoint.User + " captured " + endpoint.CapturedUtc.ToString("u") +
            "; SHA-256 " + endpoint.Sha256 +
            (fresh ? "." : " [STALE: older than ten minutes]."),
            "The JSON is operator-supplied. Computer/domain strings and SHA-256 " +
            "do not authenticate the endpoint, user or winning Group Policy.");
        if (userXml is not null &&
            !userXml.UserPrincipal.Equals(endpoint.User,
                StringComparison.OrdinalIgnoreCase))
            Add(findings, "User identity correlation", "Not verified",
                "Imported User gpresult principal differs from the Windows HKCU " +
                "snapshot principal (possibly UPN vs DOMAIN\\user).",
                "Resolve the user's SID and gpresult context on the pilot PC. " +
                "Matching computer DNS names do not link a User XML to this HKCU.");

        foreach (string setting in new[] {
            "ScreenSaveActive", "ScreenSaveTimeOut", "SCRNSAVE.EXE", "ScreenSaverIsSecure"
        })
        {
            var policy = endpoint.Get("PolicyHKCU", setting);
            var desktop = endpoint.Get("DesktopHKCU", setting);
            // Inspect both. A registry policy candidate is not itself proof
            // of winning RSoP, especially without live RSoP/session evidence.
            if (policy.Status == "Unreadable" ||
                desktop.Status == "Unreadable")
            {
                Add(findings, "User HKCU " + setting, "Not verified",
                    "HKCU Screen Saver policy/desktop value could not be read.",
                    "Run the probe under the actual interactive user, not a different admin.");
                continue;
            }
            if (policy.Status == "Present" && desktop.Status == "Present" &&
                !policy.Value.Equals(desktop.Value, StringComparison.OrdinalIgnoreCase))
                Add(findings, "User HKCU " + setting, "Source candidate",
                    "PolicyHKCU = '" + policy.Value + "'; DesktopHKCU = '" +
                    desktop.Value + "' (different sources).",
                    "Compare the actual User RSoP and behavior. Do not infer which value wins.");

            var chosen = policy.Status == "Present" ? policy : desktop;
            if (chosen.Status != "Present")
            {
                Add(findings, "User HKCU " + setting, "Not verified",
                    "Neither PolicyHKCU nor DesktopHKCU supplies this setting.",
                    "Inspect User GPO settings and the actual Windows Screen Saver behavior.");
                continue;
            }

            string source = chosen.Source + " = '" + chosen.Value + "'. ";
            bool concern = fresh;
            switch (setting)
            {
                case "ScreenSaveActive":
                    if (chosen.Value == "0")
                        Add(findings, "User Screen Saver active",
                            concern ? "Observed concern" : "Not verified",
                            source + "Screen Saver appears disabled in this snapshot.",
                            "Check actual User RSoP, current HKCU and in-session idle behavior.");
                    else if (chosen.Value != "1")
                        Add(findings, "User Screen Saver active", "Not verified",
                            source + "Value is not a recognized on/off setting.",
                            "Confirm the user's effective Screen Saver policy.");
                    break;
                case "ScreenSaveTimeOut":
                    if (!long.TryParse(chosen.Value, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out long seconds) ||
                        seconds < 0 || seconds > 86400)
                    {
                        Add(findings, "User Screen Saver idle", "Not verified",
                            source + "Timeout is not a valid nonnegative number of seconds.",
                            "Confirm the policy timeout on the actual client.");
                        break;
                    }
                    if (seconds == 0)
                        Add(findings, "User Screen Saver idle",
                            concern ? "Observed concern" : "Not verified",
                            source + "Timeout is zero; automatic activation may be disabled.",
                            "Verify active/timeout/secure settings and real idle behavior.");
                    else if (seconds > idleSeconds)
                        Add(findings, "User Screen Saver idle",
                            concern ? "Observed concern" : "Not verified",
                            source + "Timeout exceeds campaign target " + idleSeconds + " s.",
                            "Review the winning User GPO and adjust only after authorization.");
                    else if (seconds < idleSeconds)
                        Add(findings, "User Screen Saver idle", "Source candidate",
                            source + "Starts earlier than the proposed " + idleSeconds + " s.",
                            "Confirm the user experience and campaign schedule on the pilot.");
                    break;
                case "SCRNSAVE.EXE":
                    if (string.IsNullOrWhiteSpace(chosen.Value))
                        Add(findings, "User Screen Saver file", "Not verified",
                            source + "No executable selected.",
                            "Check the user's Screen Saver GPO and chosen Windows .scr.");
                    else
                        Add(findings, "User Screen Saver file", "Information",
                            source + "Executable path was read, NOT executed or verified.",
                            "Verify existence, signature, switches and real screen display locally.");
                    break;
                case "ScreenSaverIsSecure":
                    if (chosen.Value == "0")
                        Add(findings, "User Screen Saver secure", "Source candidate",
                            source + "Resume password may not be required.",
                            "Check the campaign's security requirement and actual lock behavior.");
                    else if (chosen.Value != "1")
                        Add(findings, "User Screen Saver secure", "Not verified",
                            source + "Unexpected secure-on-resume value.",
                            "Inspect the current User policy and lock behavior.");
                    break;
            }
        }
        var restriction = endpoint.Get("RestrictionHKCU", "NoDispScrSavPage");
        if (restriction.Status == "Unreadable")
            Add(findings, "Screen Saver UI restriction", "Not verified",
                "Could not read NoDispScrSavPage HKCU value.",
                "Read the restriction in the actual user context.");
        else if (restriction.Status == "Present" && restriction.Value != "0")
            Add(findings, "Screen Saver UI restriction", "Source candidate",
                "NoDispScrSavPage = " + restriction.Value +
                " (hides settings UI; not proof that the Screen Saver is disabled).",
                "Inspect User GPO restrictions before troubleshooting the UI.");
    }


    /// <summary>
    /// Add the DC's per-object AD applicability projection to the SAME
    /// readiness report. No gpresult XML is needed. The projection is useful
    /// for candidate/exclusion triage; it never reports a winning policy.
    /// </summary>
    private static IReadOnlyList<DomainPilotBlocker> AddAdScope(
        List<DomainPilotFinding> findings, DomainWideGpoReport report,
        DomainGpoScopeEvidence? scope, DateTimeOffset now, int idleSeconds)
    {
        if (scope is null)
        {
            findings.Add(new DomainPilotFinding("AD applicability", "Not verified",
                "No exact computer/user ACL+OU+loopback scan was requested.",
                "Use Analyze AD scope (no XML) to verify selected DC, computer, " +
                "nested security groups and optional User SAM/UPN."));
            return [];
        }
        bool fresh = scope.Fresh(now) &&
            now - report.ObservedUtc <= TimeSpan.FromMinutes(15);
        bool complete = scope.Complete && fresh &&
            report.RecognizedSourcesReadable && report.Issues.Count == 0;
        var links = scope.Links;
        int candidate = links.Count(x => x.State == "AD candidate");
        int excluded = links.Count(x => x.State.StartsWith("Excluded:",
            StringComparison.Ordinal));
        int uncertain = links.Count - candidate - excluded;
        findings.Add(new DomainPilotFinding("AD applicability",
            complete ? "Information" : "Not verified",
            scope.Computer + " / " +
            (scope.User.Length > 0 ? scope.User : "[no user selected]") +
            " | DC " + scope.Dc + " | " +
            candidate + " ACL+OU source candidates (not proven applied), " +
            excluded + " excluded, " +
            uncertain + " conditional/unknown | tokenGroups Computer=" +
            scope.ComputerToken + ", User=" + scope.UserToken + ".",
            "Domain object/ACL readings are not client policy application. " +
            "Missing sites, WMI and GPP targeting can change actual results."));
        if (!complete)
            findings.Add(new DomainPilotFinding("AD scope freshness",
                "Not verified", "AD applicability is stale, incomplete or source files were unreadable.",
                "Refresh all DC GPOs and repeat the explicit AD-only pilot scan."));
        findings.Add(new DomainPilotFinding("AD loopback", !complete ||
            scope.User.Length == 0 || scope.Loopback == "Unknown"
                ? "Not verified" : "Information",
            "Computer-side UserPolicyMode: " + scope.Loopback +
            ". User Configuration candidates: " +
            links.Count(x => x.Scope == "User" && x.Origin == "User OU") +
            " from User OU, " +
            links.Count(x => x.Origin == "Computer OU (loopback)") +
            " from Computer OU.",
            "Merge/Replace candidates are NOT proven winners. Site, WMI, " +
            "CSE processing and computer Read permission remain unverified."));
        if (scope.User.Length == 0)
            findings.Add(new DomainPilotFinding("User scope", "Not verified",
                "No exact AD user specified; User OU, nested group security " +
                "filters and loopback User-half applicability cannot be computed.",
                "Enter test-user SAM/UPN and rerun Analyze AD scope (no XML)."));
        else
        {
            var userLinks = links.Where(x => x.Scope == "User").ToArray();
            int readable = userLinks.Count(x => x.ComputerRead == "Read");
            int deniedRead = userLinks.Count(x =>
                x.ComputerRead == "Deny");
            int unknownRead = userLinks.Count(x =>
                x.ComputerRead == "Unknown");
            int notAssessed = userLinks.Count(x =>
                x.ComputerRead == "Not assessed");
            findings.Add(new DomainPilotFinding("Computer GPO Read",
                complete && deniedRead > 0 ? "Source candidate" :
                    !complete || unknownRead > 0 ? "Not verified" :
                    "Information",
                readable + " User GPO links grant computer GPC Read; " +
                deniedRead + " explicitly deny; " +
                unknownRead + " unresolved; " +
                notAssessed + " not assessed (already excluded by another rule).",
                "Read permission on the GPO container is checked against the " +
                "selected computer token; SYSVOL/file access, the actual client " +
                "token and User GPO application remain unverified."));
        }

        var mapped = report.Matches.ToLookup(
            r => r.GpoId + "|" + r.Scope, StringComparer.OrdinalIgnoreCase);
        var blockers = new List<DomainPilotBlocker>();
        int details = 0;
        foreach (var link in links)
        {
            // Unknown scope and GPP filters can never become "applied".
            bool active = link.State == "AD candidate";
            bool unsure = !active && !link.State.StartsWith("Excluded:",
                StringComparison.Ordinal);
            string group = link.Scope + " / " + link.Origin;
            var matches = mapped[link.GpoId + "|" + link.Scope].ToArray();
            if (matches.Length == 0)
            {
                if (details++ < 75)
                    findings.Add(new DomainPilotFinding(group,
                        link.State.StartsWith("Excluded:", StringComparison.Ordinal)
                            ? "Information" : "Not verified",
                        link.Gpo + " [" + link.GpoId + "] " + link.State +
                        "; no recognized source settings from this GPO.",
                        "Review CSE settings/SYSVOL sources if this policy is relevant."));
                continue;
            }
            foreach (var row in matches)
            {
                bool possible = IsAdBlockerCandidate(row, idleSeconds);
                if (!possible) continue;
                string state = active && complete ? "Source candidate" :
                    unsure || !complete ? "Not verified" :
                    "Information";
                string description = link.Gpo + " [" + link.GpoId + "] / " +
                    row.Setting + " = " + row.Value + " / " +
                    link.State + "; link " + link.LinkedAt +
                    (link.Enforced ? " [Enforced]" : "") +
                    "; ACL " + link.Security + "; WMI " + link.Wmi +
                    (link.Scope == "User" ? "; computer GPC Read " +
                        link.ComputerRead : "") + ".";
                if (details++ < 75)
                    findings.Add(new DomainPilotFinding(group, state,
                        description, "DC-only candidate, never a per-setting winner. " +
                        "Confirm target GPO link order, WMI/GPP and actual user session."));
                if ((active || unsure) && blockers.Count < 250)
                    blockers.Add(new DomainPilotBlocker(row.Scope, link.Gpo,
                        link.GpoId, row.Key, row.Setting, row.Value,
                        row.Risk is ("High" or "Warning") ? row.Risk : "Warning",
                        "AD-only (no XML)", !fresh
                            ? "Historical AD scope" :
                            active && complete
                                ? "AD candidate (not applied)" :
                                "AD conditional/unverified",
                        row.Source, link.State,
                        "OU " + link.LinkedAt + " | " + link.Origin +
                        " | ACL " + link.Security + " | WMI " + link.Wmi +
                        (link.Scope == "User" ? " | computer GPC Read " +
                            link.ComputerRead : "") +
                        ". Not proven applied or winning."));
            }
        }
        if (details > 75)
            findings.Add(new DomainPilotFinding("AD source details", "Information",
                (details - 75) + " additional matching GPO records omitted from grid.",
                "Use the named blocker grid and source scan for details."));
        return blockers;
    }

    // UPNs must match the verified AD lookup request. An explicitly chosen
    // SAM may match a DOMAIN\\sam gpresult name, but the XML is still unsigned.
    // No selected AD user means that imported user XML cannot be scoped.
    internal static bool UserXmlMatchesScope(
        UserRsopEvidence imported, DomainGpoScopeEvidence scope)
    {
        if (scope.User.Length == 0) return false;
        string principal = imported.UserPrincipal;
        if (scope.RequestedUser.Contains('@'))
            return principal.Equals(scope.RequestedUser,
                StringComparison.OrdinalIgnoreCase);
        int slash = principal.IndexOf('\\');
        return slash > 0 && slash == principal.LastIndexOf('\\') &&
            principal[(slash + 1)..].Equals(scope.User,
                StringComparison.OrdinalIgnoreCase);
    }

    // Preserve strong current user/computer RSoP evidence before AD-only
    // guesses when the same GPO/setting is seen in both sources.
    private static int BlockerEvidencePriority(string state) => state switch {
        "Review first (GPO reported applied)" => 0,
        "Reported filtered" => 1,
        "AD candidate (not applied)" => 2,
        "AD conditional/unverified" => 3,
        "Unverified applicability" => 4,
        "Historical gpresult" => 5,
        "Historical GPO source" => 6,
        "Historical AD scope" => 7,
        _ => 8
    };

    private static bool IsAdBlockerCandidate(DomainGpoMatch row, int idle)
    {
        string name = row.Setting.ToLowerInvariant();
        if (name == "screensaveactive") return row.Value.Trim() != "1";
        if (name == "screensavetimeout")
            return !long.TryParse(row.Value, out long value) ||
                value != idle;
        if (name is "scrnsave.exe" or "screensaverissecure" or
            "userpolicymode" or "nolockscreen") return true;
        return row.Risk is ("High" or "Warning");
    }

    private static void AddXml(List<DomainPilotFinding> findings, string scope,
        DateTimeOffset? read, bool? historical, string? digest, int? unresolved,
        bool adScopeAvailable = false)
    {
        if (read is null)
        {
            findings.Add(new DomainPilotFinding(scope + " gpresult",
                adScopeAvailable ? "Information" : "Not verified",
                "No " + scope + " RSoP XML was imported (OPTIONAL).",
                adScopeAvailable
                    ? "AD-only OU/group/ACL projection is available; import XML only if " +
                      "you need extra endpoint-session evidence."
                    : "Analyze AD scope (no XML) first, or optionally import " +
                      "gpresult /scope " + scope.ToLowerInvariant() + " /x XML."));
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
