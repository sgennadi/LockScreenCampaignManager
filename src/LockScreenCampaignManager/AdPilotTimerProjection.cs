using System.Globalization;

namespace LockScreenCampaignManager;

internal sealed record AdPilotTimerCandidate(string Timer, DomainGpoMatch Row, AdScopedGpoLink Link);

/// <summary>Exact-principal AD/SYSVOL source ranking, not endpoint RSoP.</summary>
internal static class AdPilotTimerProjection
{
    private const string UserPolicy = @"software\policies\microsoft\windows\control panel\desktop";
    private const string UserDesktop = @"control panel\desktop";
    private const string Display = @"software\policies\microsoft\power\powersettings\3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e";
    private const string Sleep = @"software\policies\microsoft\power\powersettings\29f6c1db-86da-48c5-9fdb-f2b67b1f44da";
    private const string Lock = @"software\microsoft\windows\currentversion\policies\system";
    private const string Next = "Ranked AD/SYSVOL source only. Confirm the actual user, active power plan and endpoint policy; not applied RSoP.";

    internal static string? TimerName(DomainGpoMatch row)
    {
        string key = row.Key.TrimStart('\\').ToLowerInvariant();
        string name = row.Setting.ToLowerInvariant();
        if (row.Scope == "User")
        {
            if (key != UserPolicy && key != UserDesktop) return null;
            return name switch {
                "screensaveactive" => "ScreenSaveActive",
                "screensavetimeout" => "ScreenSaveTimeOut", _ => null
            };
        }
        if (row.Scope != "Computer") return null;
        if (key == Display || key == Sleep)
            return name switch {
                "acsettingindex" => key == Display ? "Display AC" : "Sleep AC",
                "dcsettingindex" => key == Display ? "Display DC" : "Sleep DC", _ => null
            };
        if (key == Lock && name == "inactivitytimeoutsecs") return "Machine inactivity lock";
        if (key == "gpp:globalpoweroptionsv2")
            return name switch {
                "displayoffac" => "Display AC", "displayoffdc" => "Display DC",
                "sleepafterac" => "Sleep AC", "sleepafterdc" => "Sleep DC", _ => null
            };
        if (key == "gpp:powerscheme")
            return name switch {
                "monitorac" => "Display AC", "monitordc" => "Display DC",
                "standbyac" => "Sleep AC", "standbydc" => "Sleep DC", _ => null
            };
        return null;
    }

    internal static AdPilotTimerCandidate? Rank(IEnumerable<AdPilotTimerCandidate> source,
        bool complete, string loopback)
    {
        if (!complete) return null;
        var rows = source.Where(x => !x.Link.State.StartsWith("Excluded:",
            StringComparison.Ordinal)).ToArray();
        if (rows.Length == 0 || rows.Any(x =>
            x.Link.State != "AD candidate" || x.Link.Security != "Read+Apply" ||
            x.Link.Wmi != "None" || x.Link.Order < 1 || x.Link.Depth < 0 ||
            x.Row.Scope != x.Link.Scope || x.Timer != rows[0].Timer ||
            !x.Row.GpoId.Equals(x.Link.GpoId, StringComparison.OrdinalIgnoreCase) ||
            x.Row.Source is not ("Registry.pol" or @"Microsoft\Windows NT\SecEdit\GptTmpl.inf") ||
            (x.Row.Scope == "User" && (loopback != "Not configured" ||
                x.Link.ComputerRead != "Read" ||
                !x.Row.Key.TrimStart('\\').Equals(UserPolicy, StringComparison.OrdinalIgnoreCase)))))
            return null;
        // Different CSEs and preferences cannot be assumed to share precedence.
        if (rows.Select(x => x.Row.Source).Distinct(StringComparer.Ordinal).Count() != 1)
            return null;
        var leaders = rows.Where(a => !rows.Any(b => GpoLinkPriority.Compare(
            b.Link.Enforced, b.Link.Depth, b.Link.LinkedAt, b.Link.Order,
            a.Link.Enforced, a.Link.Depth, a.Link.LinkedAt, a.Link.Order) < 0)).ToArray();
        return leaders.Length == 1 && TryValue(leaders[0], out _) ? leaders[0] : null;
    }

    private static bool TryValue(AdPilotTimerCandidate candidate, out uint value)
    {
        if (!uint.TryParse(candidate.Row.Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out value)) return false;
        return candidate.Timer switch {
            "ScreenSaveActive" => value <= 1,
            "ScreenSaveTimeOut" => value is >= 1 and <= 86400,
            _ => true
        };
    }

    internal static IReadOnlyList<DomainPilotFinding> Build(DomainWideGpoReport report,
        DomainGpoScopeEvidence? scope, int campaignIdle, DateTimeOffset now)
    {
        if (scope is null || !scope.Complete || !scope.Fresh(now) ||
            !report.RecognizedSourcesReadable || report.Issues.Count > 0 ||
            now - report.ObservedUtc > TimeSpan.FromMinutes(15) ||
            report.ObservedUtc > now.AddMinutes(2))
            return [];
        var lookup = report.Matches.ToLookup(x => x.GpoId + "|" + x.Scope,
            StringComparer.OrdinalIgnoreCase);
        var candidates = new List<AdPilotTimerCandidate>();
        foreach (var link in scope.Links)
        {
            if (link.State.StartsWith("Excluded:", StringComparison.Ordinal)) continue;
            foreach (var row in lookup[link.GpoId + "|" + link.Scope])
                if (TimerName(row) is string timer)
                    candidates.Add(new(timer, row, link));
        }
        var leaders = candidates.GroupBy(x => x.Timer, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => Rank(x, true, scope.Loopback),
                StringComparer.Ordinal);
        int reference = campaignIdle;
        bool projectedUser = false;
        if (leaders.GetValueOrDefault("ScreenSaveActive") is { } active &&
            TryValue(active, out uint enabled) && enabled == 1 &&
            leaders.GetValueOrDefault("ScreenSaveTimeOut") is { } idle &&
            TryValue(idle, out uint seconds))
        {
            reference = (int)seconds;
            projectedUser = true;
        }
        var findings = new List<DomainPilotFinding>();
        foreach (var (timer, leader) in leaders.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (leader is null)
            {
                findings.Add(new("AD timer priority", "Not verified",
                    timer + ": " + UiLocale.Display("Timer source precedence unresolved."), Next));
                continue;
            }
            _ = TryValue(leader, out uint value);
            string observed = timer + " = " + value.ToString(CultureInfo.InvariantCulture) +
                (timer == "ScreenSaveActive" ? "" : " s") + " | " + leader.Row.Gpo +
                " [" + leader.Row.GpoId + "] | " + leader.Row.Source;
            if (leader.Row.Scope == "Computer")
            {
                string target = projectedUser ? "ScreenSaveTimeOut (AD)" : "ScreenSaveTimeOut (campaign)";
                observed += value == 0 ? " | 0 = Never" :
                    " | " + value + " s " + (value < reference ? "<" :
                        value == reference ? "=" : ">") + " " + target + " " + reference + " s";
            }
            findings.Add(new("AD timer priority", "Source candidate", observed, Next));
        }
        return findings;
    }
}
