namespace LockScreenCampaignManager;

/// <summary>Relative priority of domain/OU links, not proof of application.
/// Enforced links outrank ordinary links; among enforced links, ancestors
/// outrank descendants. Within the same container/class, link order 1 wins.
/// Different containers at equal depth cannot be ranked by link order.</summary>
internal static class GpoLinkPriority
{
    internal static int Compare(bool enforcedA, int depthA, string linkedAtA, int orderA,
        bool enforcedB, int depthB, string linkedAtB, int orderB)
    {
        if (enforcedA != enforcedB) return enforcedA ? -1 : 1;
        if (depthA != depthB)
            return enforcedA ? depthA.CompareTo(depthB) : depthB.CompareTo(depthA);
        if (string.Equals(linkedAtA, linkedAtB, StringComparison.OrdinalIgnoreCase))
            return orderA.CompareTo(orderB);
        return 0;
    }
}

internal sealed record LoopbackLinkCandidate(string LinkedAt, int Depth, int Order,
    bool Enforced, string State, string Value);

internal static class LoopbackPolicyProjection
{
    private const string Key = @"Software\Policies\Microsoft\Windows\System";
    private const string Setting = "UserPolicyMode";

    // Reuse the bounded Registry.pol reader, including its UTF-16 and type
    // validation. Deletion/soft-write instructions or non-DWORD modes are
    // deliberately unresolved rather than interpreted as ordinary settings.
    internal static string ReadRegistryPol(byte[] contents, out bool reliable)
    {
        string value = "";
        bool valid = true;
        int assignments = 0;
        _ = NativeGpoConflictScan.ParseRegistryPol(contents, false,
            (rawKey, name, type, display) =>
            {
                string key = rawKey.TrimStart('\\').TrimEnd('\\');
                bool target = key.Equals(Key, StringComparison.OrdinalIgnoreCase);
                bool ancestor = Key.StartsWith(key + "\\", StringComparison.OrdinalIgnoreCase);
                if (target && name.Equals(Setting, StringComparison.OrdinalIgnoreCase))
                {
                    assignments++;
                    if (type != 4 || display is not ("0" or "1" or "2") ||
                        assignments != 1) valid = false;
                    else value = display;
                }
                else if (target && (name.Equals("**Del." + Setting, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("**soft." + Setting, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("**DelVals.", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("**DeleteValues", StringComparison.OrdinalIgnoreCase)))
                    valid = false;
                else if (ancestor && name.Equals("**DeleteKeys", StringComparison.OrdinalIgnoreCase))
                    valid = false;
            });
        reliable = valid;
        return valid ? value : "";
    }

    internal static string Decide(IEnumerable<LoopbackLinkCandidate> machine, bool complete)
    {
        if (!complete) return "Unknown";
        var rows = machine.Where(x => x.Value.Length > 0 &&
            !x.State.StartsWith("Excluded:", StringComparison.Ordinal)).ToArray();
        if (rows.Any(x => x.State != "AD candidate" ||
            x.Value is not ("0" or "1" or "2") || x.Depth < 0 || x.Order < 1))
            return "Unknown";
        if (rows.Length == 0) return "Not configured";
        // Every non-excluded setting must have a verified AD scope. A
        // conditional WMI/ACL or incomplete scan must not establish a mode.
        // Use pairwise dominance, not a sort with an incomparable tie, so
        // unrelated same-depth containers cannot hide a possible leader.
        var leaders = rows.Where(a => !rows.Any(b =>
            GpoLinkPriority.Compare(b.Enforced, b.Depth, b.LinkedAt, b.Order,
                a.Enforced, a.Depth, a.LinkedAt, a.Order) < 0)).ToArray();
        if (leaders.Length == 0) return "Unknown";
        string[] modes = leaders.Select(x => x.Value).Distinct(StringComparer.Ordinal).ToArray();
        if (modes.Length != 1) return "Unknown";
        return modes[0] switch { "0" => "Not configured", "1" => "Merge candidate",
            "2" => "Replace candidate", _ => "Unknown" };
    }
}
