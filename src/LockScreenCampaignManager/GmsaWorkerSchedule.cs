using System.Xml;

namespace LockScreenCampaignManager;

/// <summary>
/// Pure validator for the native Windows Task Scheduler trigger collection.
/// The worker needs both its daily 2-minute recurrence AND a delayed boot
/// trigger; additional or disabled triggers are not an authorized schedule.
/// This validator is testable without Task Scheduler, AD or a domain logon.
/// </summary>
internal readonly record struct GmsaWorkerTriggerSpec(
    int Type, bool Enabled, string Interval, string Duration, int DaysInterval, string Delay);

internal static class GmsaWorkerSchedule
{
    internal const int DailyTrigger = 2; // TASK_TRIGGER_DAILY
    internal const int BootTrigger = 8; // TASK_TRIGGER_BOOT
    internal const string Interval = "PT2M";
    internal const string Duration = "P1D";
    internal const string BootDelay = "PT3M";

    private static bool PeriodEquals(string? value, TimeSpan expected)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32)
            return false;
        try { return XmlConvert.ToTimeSpan(value) == expected; }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            return false;
        }
    }

    internal static bool Matches(IReadOnlyList<GmsaWorkerTriggerSpec>? triggers)
    {
        if (triggers is null || triggers.Count != 2)
            return false;

        bool daily = false, boot = false;
        foreach (var trigger in triggers)
        {
            if (!trigger.Enabled)
                return false;
            switch (trigger.Type)
            {
                case DailyTrigger:
                    if (daily || trigger.DaysInterval != 1 ||
                        !PeriodEquals(trigger.Interval, TimeSpan.FromMinutes(2)) ||
                        !PeriodEquals(trigger.Duration, TimeSpan.FromDays(1)))
                        return false;
                    daily = true;
                    break;
                case BootTrigger:
                    if (boot || !PeriodEquals(trigger.Delay, TimeSpan.FromMinutes(3)))
                        return false;
                    boot = true;
                    break;
                default:
                    return false;
            }
        }
        return daily && boot;
    }
}
