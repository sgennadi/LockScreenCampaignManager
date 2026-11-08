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
}
