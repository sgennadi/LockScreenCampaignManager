using System.Globalization;

namespace LockScreenCampaignManager;

/// <summary>
/// Windows Remote Desktop Session Host limits stored by computer/user GPO
/// in HKLM/HKCU Software\Policies\Microsoft\Windows NT\Terminal Services.
/// Duration values are MILLISECONDS. RDS limits do not apply to the physical
/// console and cannot prove that the policy actually won in client RSoP.
/// </summary>
internal static class RdsSessionPolicyBlockers
{
    internal const string PolicyKey =
        @"software\policies\microsoft\windows nt\terminal services";

    internal static NativeGpoConflictScan.Hit? Recognize(
        string key, string name, string value)
    {
        if (!key.TrimStart('\\').Equals(PolicyKey,
                StringComparison.OrdinalIgnoreCase))
            return null;
        string setting = name.ToLowerInvariant();
        string description = setting switch
        {
            "maxidletime" =>
                "RDS active-but-idle session time limit (milliseconds); " +
                "remote session may disconnect before Screen Saver. " +
                "Does NOT apply to local console sessions.",
            "maxconnectiontime" =>
                "RDS total active session lifetime limit (milliseconds); " +
                "not equivalent to idle time and cannot be compared directly. " +
                "Does NOT apply to local console sessions.",
            "maxdisconnectiontime" =>
                "RDS disconnected session time limit (milliseconds). " +
                "It acts AFTER disconnect, not before Screen Saver on the console.",
            "fresetbroken" =>
                "RDS end-session-at-time-limit policy. Only changes what happens " +
                "after a configured time limit; not a stand-alone idle timer.",
            _ => ""
        };
        return description.Length == 0 ? null :
            new NativeGpoConflictScan.Hit(key, name, value,
                "Warning", description + " Actual RDS host/policy RSoP unverified.");
    }

    internal static string AssessRisk(
        string name, string value, int configuredSaverSeconds,
        bool includesScreenSaver, string linkStatus)
    {
        if (linkStatus == "Inactive/filtered") return "Inactive/filtered";
        if (linkStatus != "Linked candidate" || !includesScreenSaver) return "Info";
        if (name.Equals("fResetBroken", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryMilliseconds(value, out uint enabled)) return "Warning";
            return enabled == 0 ? "Info" : "Warning";
        }
        if (!TryMilliseconds(value, out uint milliseconds))
            return "Warning"; // unknown typed / malformed value is not safe
        if (milliseconds == 0) return "Info"; // unlimited, this source only
        if (name.Equals("MaxIdleTime", StringComparison.OrdinalIgnoreCase))
        {
            // RDS idle is measured in ms, configured Screen Saver in seconds.
            // Warn at equal threshold, because order / warning/disconnect
            // timing is not established. RDS ONLY, not a physical PC screen.
            return milliseconds <= (long)configuredSaverSeconds * 1000
                ? "Warning" : "Info";
        }
        if (name.Equals("MaxConnectionTime", StringComparison.OrdinalIgnoreCase))
            return "Warning"; // relative to session age, not idle
        return "Info"; // max disconnected time acts after disconnect
    }

    internal static bool TryMilliseconds(string raw, out uint result)
    {
        string text = raw.Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(text[2..], NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out result)
            : uint.TryParse(text, NumberStyles.None,
                CultureInfo.InvariantCulture, out result);
    }
}
