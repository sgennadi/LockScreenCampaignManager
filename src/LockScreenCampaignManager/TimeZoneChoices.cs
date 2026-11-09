using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Single Windows time-zone ID picker for the campaign editor, Schedule
/// dialog and guided Wizard. Keep actual IDs in CampaignProject.TimeZoneId:
/// endpoint scheduling consumes Windows time-zone IDs, not UI display text.
/// </summary>
internal static class TimeZoneChoices
{
    internal static IReadOnlyList<string> Ids() =>
        TimeZoneInfo.GetSystemTimeZones()
            .OrderBy(x => x.BaseUtcOffset)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static void Populate(ComboBox box, string currentId)
    {
        ArgumentNullException.ThrowIfNull(box);
        box.BeginUpdate();
        try
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.IntegralHeight = false;
            box.MaxDropDownItems = 16;
            box.Items.Clear();
            foreach (string id in Ids()) box.Items.Add(id);
            // Older imported projects may carry an IANA ID, also understood
            // by recent .NET Windows TimeZoneInfo. Never silently replace it.
            if (!string.IsNullOrWhiteSpace(currentId) &&
                !box.Items.Cast<string>().Any(x =>
                    x.Equals(currentId, StringComparison.OrdinalIgnoreCase)))
                box.Items.Add(currentId);
            box.SelectedItem = box.Items.Cast<string>()
                .FirstOrDefault(x => x.Equals(currentId,
                    StringComparison.OrdinalIgnoreCase)) ??
                box.Items.Cast<string>().FirstOrDefault(x =>
                    x.Equals(TimeZoneInfo.Local.Id,
                        StringComparison.OrdinalIgnoreCase));
            box.DropDownWidth = Math.Max(Math.Max(540, box.Width), box.DropDownWidth);
        }
        finally { box.EndUpdate(); }
    }

    internal static string SelectedId(ComboBox box) =>
        box.SelectedItem as string ?? "";

    // Display-only current offset comparison. Persist the original Windows
    // time-zone ID, never a formatted UTC offset: DST changes during a campaign.
    internal static string OffsetComparison(string timeZoneId, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return "Choose a Windows time zone to compare UTC offsets.";
        try
        {
            var selected = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var instant = nowUtc.ToUniversalTime();
            TimeSpan campaignOffset = selected.GetUtcOffset(instant);
            TimeSpan computerOffset = TimeZoneInfo.Local.GetUtcOffset(instant);
            TimeSpan difference = campaignOffset - computerOffset;
            string daylight = selected.IsDaylightSavingTime(instant) ? "Yes" : "No";
            return "Campaign: UTC" + OffsetText(campaignOffset) +
                "  |  This PC: UTC" + OffsetText(computerOffset) +
                "  |  Difference: " + DifferenceText(difference) +
                "  |  DST now: " + daylight;
        }
        catch (TimeZoneNotFoundException)
        {
            return "Time zone unavailable on this PC; verify the selected Windows ID.";
        }
        catch (InvalidTimeZoneException)
        {
            return "Time-zone rules are invalid on this PC; verify the selected ID.";
        }
    }

    internal static string OffsetText(TimeSpan offset)
    {
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();
        return sign + ((int)absolute.TotalHours).ToString("D2") +
            ":" + absolute.Minutes.ToString("D2");
    }

    private static string DifferenceText(TimeSpan difference)
    {
        if (difference == TimeSpan.Zero) return "0 h";
        string sign = difference < TimeSpan.Zero ? "-" : "+";
        var absolute = difference.Duration();
        return sign + ((int)absolute.TotalHours).ToString() +
            (absolute.Minutes == 0 ? " h" : " h " + absolute.Minutes + " min");
    }
}
