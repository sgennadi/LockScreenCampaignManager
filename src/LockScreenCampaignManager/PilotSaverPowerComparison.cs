using System.Globalization;

namespace LockScreenCampaignManager;

/// <summary>
/// Conservative cross-check between the exact pilot user's HKCU idle timer and
/// selected-PC power readings. The active-plan and policy registry sources
/// can disagree; their values do not establish which source wins on Windows.
/// </summary>
internal static class PilotSaverPowerComparison
{
    internal static IReadOnlyList<DomainPilotFinding> Compare(
        EndpointUserReadinessEvidence? endpoint, DomainGpoScopeEvidence? scope,
        EndpointPowerObservation? power, DateTimeOffset now)
    {
        static DomainPilotFinding Unknown(string cause, string next) =>
            new("User saver vs power", "Not verified", cause, next);

        if (endpoint is null || scope is null ||
            !endpoint.IsFresh(now) || !scope.Fresh(now) || !scope.Complete ||
            scope.RequestedUser.Length == 0 ||
            !PilotUserSidCorrelation.Matches(scope.UserSid, endpoint.UserSid))
            return [Unknown(
                "No fresh exact AD user SID-matched HKCU timeout is available.",
                "Select the exact AD user, capture HKCU as that user and refresh AD scope.")];

        var policyActive = endpoint.Get("PolicyHKCU", "ScreenSaveActive");
        var desktopActive = endpoint.Get("DesktopHKCU", "ScreenSaveActive");
        var policyIdle = endpoint.Get("PolicyHKCU", "ScreenSaveTimeOut");
        var desktopIdle = endpoint.Get("DesktopHKCU", "ScreenSaveTimeOut");
        if ((policyActive.Status == "Present" && desktopActive.Status == "Present" &&
             policyActive.Value != desktopActive.Value) ||
            (policyIdle.Status == "Present" && desktopIdle.Status == "Present" &&
             policyIdle.Value != desktopIdle.Value))
            return [Unknown("PolicyHKCU and DesktopHKCU Screen Saver sources disagree.",
                "Resolve actual per-setting User RSoP before comparing with power.")];
        if (new[] { policyActive, desktopActive, policyIdle, desktopIdle }
            .Any(x => x.Status == "Unreadable"))
            return [Unknown("Some Screen Saver HKCU settings could not be read.",
                "Read both Screen Saver policy and desktop keys as the selected user.")];

        var active = policyActive.Status == "Present" ? policyActive : desktopActive;
        var idle = policyIdle.Status == "Present" ? policyIdle : desktopIdle;
        if (active.Status != "Present" || active.Value != "1" ||
            idle.Status != "Present" ||
            !int.TryParse(idle.Value, NumberStyles.None, CultureInfo.InvariantCulture,
                out int seconds) || seconds is < 60 or > 86400)
            return [Unknown("HKCU does not provide an enabled positive Screen Saver idle timer.",
                "Inspect ScreenSaveActive and ScreenSaveTimeOut for the exact user.")];
        if (power is null || power.ObservedUtc > now.AddMinutes(2) ||
            now - power.ObservedUtc > TimeSpan.FromMinutes(5))
            return [Unknown("Power readings are absent or older than five minutes.",
                "Run the approved read-only Verify power (PC) for the same PC/OU.")];
        return CompareTimerSources(seconds, power.Readings, true,
            power.PlanStatus, power.RegistryStatus);
    }

    internal static IReadOnlyList<DomainPilotFinding> CompareTimerSources(
        int saverSeconds, IReadOnlyList<EndpointPowerReading> readings,
        bool recent, string planStatus, string registryStatus)
    {
        var findings = new List<DomainPilotFinding>();
        bool comparedAny = false;
        if (saverSeconds is < 60 or > 86400 || !recent)
            return [new("User saver vs power", "Not verified",
                "Actual selected-user timeout or current power readings unavailable.",
                "Refresh the user's HKCU and selected-PC power evidence.")];

        foreach (var (kind, supply) in new [] {
            ("Display", "AC"), ("Display", "DC"),
            ("Sleep", "AC"), ("Sleep", "DC"), ("MachineLock", "Any")
        })
        {
            var wmi = readings.SingleOrDefault(x => x.Kind == kind &&
                x.Supply == supply && x.Source == "ActivePlanWmi" &&
                x.Status == "Present");
            var policy = readings.SingleOrDefault(x => x.Kind == kind &&
                x.Supply == supply && x.Source == "PolicyRegistry" &&
                x.Status == "Present");
            string area = kind == "MachineLock" ? "User saver vs machine lock" :
                kind == "Display" ? "User saver vs display" : "User saver vs sleep";
            if (kind == "MachineLock")
                wmi = null;
            // Never elevate a conflicting or partially verified power source
            // into an observed problem for the interactive user's session.
            if (wmi?.Seconds is long w && policy?.Seconds is long p && w != p)
            {
                findings.Add(new(area, "Not verified",
                    supply + ": active plan " + w + " s vs policy HKLM " + p +
                    " s; HKCU idle " + saverSeconds + " s (power source conflict).",
                    "Inspect per-setting powercfg and HKLM policy; source precedence is unknown."));
                continue;
            }
            var selected = wmi ?? policy;
            if (selected?.Seconds is not long timer)
                continue;
            bool complete = selected.Source == "ActivePlanWmi" ?
                planStatus == "Available" : registryStatus == "Readable";
            if (!complete)
            {
                findings.Add(new(area, "Not verified",
                    supply + ": " + selected.Source + " " + timer +
                    " s, source is not fully readable.",
                    "Repeat the authorized endpoint power query."));
                continue;
            }
            comparedAny = true;
            if (timer == 0) continue; // Never for this specific power source.
            if (timer < saverSeconds)
                findings.Add(new(area, "Observed concern",
                    supply + ": " + selected.Source + " timeout " + timer +
                    " s is " + (saverSeconds - timer) +
                    " s earlier than selected user's HKCU Screen Saver " +
                    saverSeconds + " s.",
                    "Potential earlier display/sleep/lock event. Check actual " +
                    "active AC/DC power, per-setting GPO and in-session behavior."));
            else if (timer == saverSeconds)
                findings.Add(new(area, "Source candidate",
                    supply + ": " + selected.Source + " timeout " + timer +
                    " s equals selected user's HKCU Screen Saver threshold.",
                    "Equal thresholds do not establish event order; test on Windows."));
        }
        if (findings.Count == 0)
            findings.Add(comparedAny
                ? new("User saver vs power", "Information",
                    "No earlier or equal timer in the readable supported power observations.",
                    "This does not verify display state, winning GPO or actual Screen Saver.")
                : new("User saver vs power", "Not verified",
                    "No complete current display, sleep or machine lock timer was available.",
                    "Refresh the read-only power observation for the selected PC."));
        return findings;
    }
}
