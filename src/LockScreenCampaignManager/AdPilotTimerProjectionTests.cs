namespace LockScreenCampaignManager;

internal static class AdPilotTimerProjectionTests
{
    private const string Power = @"Software\Policies\Microsoft\Power\PowerSettings\3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e";
    private const string User = @"Software\Policies\Microsoft\Windows\Control Panel\Desktop";
    private const string Parent = "OU=Clients,DC=example,DC=com";
    private const string Child = "OU=Pilot," + Parent;
    internal static void Run()
    {
        var parent = Item("Parent", Parent, 1, 1, false, "900");
        var child = Item("Child", Child, 2, 1, false, "120");
        Expect(child, parent, child);
        var enforced = parent with { Link = parent.Link with { Enforced = true } };
        Expect(enforced, enforced, child);
        Expect(null, parent, parent with { Row = parent.Row with { Value = "120" } });
        Expect(null, parent, child with { Link = child.Link with {
            State = "Conditional: WMI not evaluated", Wmi = "Unverified" } });
        Expect(parent, parent, child with { Link = child.Link with { State = "Excluded: link disabled" } });
        Expect(null, parent, child with { Row = child.Row with {
            Source = @"Preferences\PowerOptions\PowerOptions.xml" } });
        Expect(null, child with { Row = child.Row with { Value = "[binary or unsupported]" } });
        if (AdPilotTimerProjection.Rank([parent], false, "Not configured") is not null)
            throw new InvalidOperationException("Incomplete AD scope ranked a timer.");
        var user = Item("User", Parent, 1, 1, false, "300", User, "ScreenSaveTimeOut", "User");
        Expect(user, user);
        if (AdPilotTimerProjection.Rank([user], true, "Replace candidate") is not null)
            throw new InvalidOperationException("Unconfirmed loopback ranked a user timer.");
        if (AdPilotTimerProjection.TimerName(child.Row) != "Display AC" ||
            DomainGpoPilotReadiness.AdBlockerRisk(child.Row, 600) != "High" ||
            DomainGpoPilotReadiness.AdBlockerRisk(parent.Row, 600) != "Info")
            throw new InvalidOperationException("Exact AD timer risk must not depend on coarse OU inventory.");
        var legacy = child.Row with { Key = "GPP:PowerScheme", Setting = "monitorAc", Value = "2",
            Source = @"Preferences\PowerOptions\PowerOptions.xml" };
        if (AdPilotTimerProjection.TimerName(legacy) != "Display AC" ||
            DomainGpoPilotReadiness.AdBlockerRisk(legacy, 600) != "High" ||
            DomainGpoPilotReadiness.AdBlockerRisk(legacy with { Value = "0" }, 600) != "Info")
            throw new InvalidOperationException("Legacy GPP minutes or zero semantics changed.");
    }

    private static AdPilotTimerCandidate Item(string name, string ou, int depth, int order,
        bool enforced, string value, string key = Power, string setting = "ACSettingIndex",
        string scope = "Computer")
    {
        string id = Guid.NewGuid().ToString("D");
        var row = new DomainGpoMatch(name, id, scope, "Registry.pol", key, setting, value,
            "Info", "Synthetic source", "Outside selected OUs", ou, "Source only");
        var link = new AdScopedGpoLink(name, id, scope,
            scope == "User" ? "User OU" : "Computer OU", ou, depth, order, enforced,
            "AD candidate", "Read+Apply", "None", "", scope == "User" ? "Read" : "N/A");
        return new(AdPilotTimerProjection.TimerName(row)!, row, link);
    }

    private static void Expect(AdPilotTimerCandidate? expected, params AdPilotTimerCandidate[] rows)
    {
        if (AdPilotTimerProjection.Rank(rows, true, "Not configured") != expected)
            throw new InvalidOperationException("Unexpected AD timer precedence.");
    }
}
