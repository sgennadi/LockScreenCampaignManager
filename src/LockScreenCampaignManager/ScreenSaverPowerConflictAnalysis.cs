using System.Globalization;
using System.Text;
using System.Xml;

namespace LockScreenCampaignManager;

/// <summary>
/// Compare a User Screen Saver idle interval against Computer-linked Power
/// policy and GPP candidates for an explicitly selected DEVICE OU. These
/// are NOT winning policies or current endpoint powercfg values.
/// </summary>
internal static class ScreenSaverPowerConflictAnalysis
{
    private const string VideoIdle =
        @"software\policies\microsoft\power\powersettings\3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e";
    private const string SleepIdle =
        @"software\policies\microsoft\power\powersettings\29f6c1db-86da-48c5-9fdb-f2b67b1f44da";
    private const string MachineIdle =
        @"software\microsoft\windows\currentversion\policies\system";

    internal static bool IsMachinePowerCandidate(GroupConflictFinding f)
    {
        string key = f.Key.TrimStart('\\').ToLowerInvariant();
        string name = f.Setting.ToLowerInvariant();
        return ((key == VideoIdle || key == SleepIdle) &&
                    name is ("acsettingindex" or "dcsettingindex")) ||
               (key == MachineIdle && name == "inactivitytimeoutsecs") ||
               (key == "gpp:globalpoweroptionsv2" &&
                    name is ("displayoffac" or "displayoffdc" or
                             "sleepafterac" or "sleepafterdc")) ||
               (key == "gpp:powerscheme" &&
                    name is ("monitorac" or "monitordc" or
                             "standbyac" or "standbydc"));
    }

    internal static IReadOnlyList<GroupConflictFinding> Compare(
        string target, int saverIdleSeconds, IEnumerable<GroupConflictFinding> deviceCandidates)
    {
        if (saverIdleSeconds is < 60 or > 86400)
            throw new ArgumentOutOfRangeException(nameof(saverIdleSeconds));
        var findings = new List<GroupConflictFinding>();
        foreach (GroupConflictFinding f in deviceCandidates)
        {
            // A disabled/unlinked candidate must never be promoted to a
            // misleading high-risk result.
            if (f.Risk == "Inactive/filtered" || !IsMachinePowerCandidate(f))
                continue;
            string key = f.Key.TrimStart('\\').ToLowerInvariant();
            string name = f.Setting.ToLowerInvariant();
            string kind = key == VideoIdle ||
                          key == "gpp:globalpoweroptionsv2" && name.StartsWith("displayoff") ||
                          key == "gpp:powerscheme" && name.StartsWith("monitor")
                ? "Display off" : key == MachineIdle ? "Machine inactivity lock" : "Sleep";
            string supply = name.EndsWith("ac") || name.StartsWith("ac") ? "AC" :
                name.EndsWith("dc") || name.StartsWith("dc") ? "DC" : "Any power";
            // Legacy GPP PowerScheme monitor/standby values are MINUTES.
            // Power.admx AC/DC indices and GPP Vista+ power options are seconds.
            bool minutes = key == "gpp:powerscheme";
            if (!TrySeconds(f.Value, minutes, out long seconds))
            {
                findings.Add(Candidate(f, target, "Warning", kind + " " + supply +
                    " has an unreadable timeout", f.Value +
                    " / saver " + saverIdleSeconds + " s"));
                continue;
            }
            if (seconds == 0) continue; // 0 = Never for power timeouts.
            if (seconds > saverIdleSeconds) continue;
            string risk = kind == "Machine inactivity lock" || seconds == saverIdleSeconds
                ? "Warning" : "High";
            string relation = seconds < saverIdleSeconds
                ? "BEFORE Screen Saver" : "AT Screen Saver threshold";
            string label = kind + " " + supply + " " + relation;
            findings.Add(Candidate(f, target, risk, label,
                seconds + " s vs Screen Saver " + saverIdleSeconds + " s"));
        }
        return findings;
    }

    private static GroupConflictFinding Candidate(GroupConflictFinding source,
        string target, string risk, string label, string value) =>
        new(target, "Computer", source.Gpo, source.GpoId,
            risk, label, value, source.Source, source.Key, source.LinkedAt,
            source.Applicability +
            " | PRELIMINARY: Computer OU GPO vs configured User Screen Saver. " +
            "Neither winning policy, security/WMI filtering, active power plan, " +
            "GPP item-level targeting nor actual device powercfg is verified.");

    private static bool TrySeconds(string text, bool minutes, out long seconds)
    {
        seconds = 0;
        string value = text.Trim();
        bool hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        uint parsed;
        if (hex)
        {
            if (!uint.TryParse(value[2..], NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out parsed))
                return false;
        }
        else if (!uint.TryParse(value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out parsed))
            return false;
        seconds = minutes ? (long)parsed * 60L : parsed;
        return true;
    }

    /// <summary>
    /// Read GPP PowerOptions.xml from a selected DC's Machine half.
    /// GlobalPowerOptionsV2 displayOffAC/DC use seconds. Legacy PowerScheme
    /// monitorAc/monitorDc use minutes (MS-GPPREF). Only candidate settings
    /// are returned; the current default plan and item-level filters are NOT
    /// resolved, so this is never a verdict about actual applied policy.
    /// </summary>
    internal static IReadOnlyList<NativeGpoConflictScan.Hit> ParseGppPowerOptions(byte[] bytes)
    {
        if (bytes is null || bytes.Length is < 1 or > 2097152)
            throw new InvalidDataException("GPP PowerOptions.xml exceeds size limit.");
        using var stream = new MemoryStream(bytes, false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2097152,
            MaxCharactersFromEntities = 0
        });
        var xml = new XmlDocument { XmlResolver = null };
        xml.Load(reader);
        if (xml.DocumentElement?.LocalName != "PowerOptions")
            throw new InvalidDataException("Unexpected GPP PowerOptions root.");
        if (Disabled(xml.DocumentElement)) return [];

        var result = new List<NativeGpoConflictScan.Hit>();
        foreach (XmlNode node in xml.DocumentElement.ChildNodes)
        {
            if (node is not XmlElement parent || Disabled(parent)) continue;
            bool modern = parent.LocalName == "GlobalPowerOptionsV2";
            bool legacy = parent.LocalName == "PowerScheme";
            if (!modern && !legacy) continue;
            foreach (XmlNode n in parent.ChildNodes)
            {
                if (n is not XmlElement props || props.LocalName != "Properties" ||
                    Disabled(props) ||
                    props.GetAttribute("action").Equals("D", StringComparison.OrdinalIgnoreCase))
                    continue;
                string[] names = modern
                    ? ["displayOffAC", "displayOffDC", "sleepAfterAC", "sleepAfterDC"]
                    : ["monitorAc", "monitorDc", "standbyAc", "standbyDc"];
                foreach (string name in names)
                {
                    if (!props.HasAttribute(name)) continue;
                    string value = props.GetAttribute(name).Trim();
                    if (!uint.TryParse(value, NumberStyles.None,
                            CultureInfo.InvariantCulture, out uint number))
                        throw new InvalidDataException("GPP PowerOptions numeric timer is invalid.");
                    // Old PowerScheme timers are minutes. The high-risk
                    // comparison handles conversion and never assumes a
                    // legacy XP plan actually applies to Windows 10/11.
                    string key = modern ? "GPP:GlobalPowerOptionsV2" : "GPP:PowerScheme";
                    string label = name.StartsWith("displayOff", StringComparison.OrdinalIgnoreCase) ||
                                   name.StartsWith("monitor", StringComparison.OrdinalIgnoreCase)
                        ? "display-off" : "sleep";
                    result.Add(new NativeGpoConflictScan.Hit(key, name,
                        number.ToString(CultureInfo.InvariantCulture), "Warning",
                        "GPP PowerOptions " + label + " candidate; " +
                        (modern ? "seconds" : "legacy scheme minutes") +
                        ". Actual active plan and item-level targeting are unverified."));
                    if (result.Count > 120)
                        throw new InvalidDataException("Too many GPP PowerOptions settings.");
                }
            }
        }
        return result;
    }

    private static bool Disabled(XmlElement element) =>
        element.GetAttribute("disabled") is "1" or "true" or "True";
}
