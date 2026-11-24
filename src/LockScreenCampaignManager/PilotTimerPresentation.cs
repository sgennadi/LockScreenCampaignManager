using System.Globalization;

namespace LockScreenCampaignManager;

/// <summary>Presentation-only structured timer data. The raw GPO name, GUID,
/// source file and numeric values are never translated or normalized.</summary>
internal sealed record PilotTimerPresentation(string Timer, uint? Value,
    string Gpo, string GpoId, string Source, int? ReferenceSeconds, bool ProjectedUser)
{
    internal DomainPilotFinding ToFinding(string next) =>
        new("AD timer priority", Value.HasValue ? "Source candidate" : "Not verified",
            Render("English"), next) { TimerPresentation = this };

    internal string Render(string language)
    {
        string T(string key) => UiLocale.Translate(key, language);
        string Number(long n) => n.ToString(CultureInfo.InvariantCulture);
        string caption = Timer switch {
            "ScreenSaveActive" => T("Screen Saver enabled flag") + " (ScreenSaveActive)",
            "ScreenSaveTimeOut" => T("Screen Saver idle timer") + " (ScreenSaveTimeOut)",
            "Display AC" => T("Display off (AC)"),
            "Display DC" => T("Display off (DC)"),
            "Sleep AC" => T("Sleep timer (AC)"),
            "Sleep DC" => T("Sleep timer (DC)"),
            "Machine inactivity lock" => T("Machine inactivity lock"),
            _ => Timer
        };
        if (Value is not uint value)
            return caption + ": " + T("Timer source precedence unresolved.");

        string result = caption + " = " + Number(value) +
            (Timer == "ScreenSaveActive" ? "" : " " + T("timer seconds"));
        // Separate the invariant evidence from localized prose for RTL readers.
        result += Environment.NewLine + "GPO: " + Gpo + " [" + GpoId + "] | " + Source;
        if (ReferenceSeconds is int reference)
        {
            string referenceText = (ProjectedUser ? T("AD Screen Saver timeout") :
                T("Campaign Screen Saver timeout")) + ": " + Number(reference) +
                " " + T("timer seconds");
            string relation = value == 0 ? T("0 = Never (this timer)") :
                value < reference ? T("Earlier than Screen Saver") :
                value == reference ? T("Equal to Screen Saver threshold") :
                T("Later than Screen Saver");
            result += Environment.NewLine + relation + " | " + referenceText;
        }
        return result;
    }
}

internal static class PilotFindingPresentation
{
    internal static string Observation(DomainPilotFinding finding, string language) =>
        finding.TimerPresentation?.Render(language) ??
        UiLocale.Translate(finding.Observation, language);

    internal static object[] Cells(DomainPilotFinding finding, string language) =>
        [
            UiLocale.Translate(finding.Area, language),
            UiLocale.Translate(finding.State, language),
            Observation(finding, language),
            UiLocale.Translate(finding.NextStep, language)
        ];

    internal static string Details(DomainPilotFinding finding, string language) =>
        UiLocale.Translate(finding.Area, language) + ": " +
        UiLocale.Translate(finding.State, language) + Environment.NewLine +
        Observation(finding, language) + Environment.NewLine +
        UiLocale.Translate("Next step", language) + ": " +
        UiLocale.Translate(finding.NextStep, language);
}
