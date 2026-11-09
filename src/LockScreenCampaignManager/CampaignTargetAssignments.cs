using System.Text.Json.Serialization;

namespace LockScreenCampaignManager;

/// <summary>
/// A campaign can target computer lock screens, user screen savers or both.
/// These are distinct GPO scopes: HKLM computer policy cannot silently be
/// substituted for HKCU screen-saver policy. A Both row has two target groups.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CampaignTargetMode
{
    LockScreen = 0,
    ScreenSaver = 1,
    Both = 2
}

public sealed class CampaignGroupTarget
{
    public string Name { get; set; } = "";
    public CampaignTargetMode Mode { get; set; } = CampaignTargetMode.LockScreen;
    public string ComputerOuDn { get; set; } = "";
    public string ComputerGroupDn { get; set; } = "";
    public string UserOuDn { get; set; } = "";
    public string UserGroupDn { get; set; } = "";

    [JsonIgnore] public bool UsesLockScreen =>
        Mode is CampaignTargetMode.LockScreen or CampaignTargetMode.Both;
    [JsonIgnore] public bool UsesScreenSaver =>
        Mode is CampaignTargetMode.ScreenSaver or CampaignTargetMode.Both;

    public CampaignGroupTarget Clone() => new()
    {
        Name = Name, Mode = Mode, ComputerOuDn = ComputerOuDn,
        ComputerGroupDn = ComputerGroupDn, UserOuDn = UserOuDn,
        UserGroupDn = UserGroupDn
    };

    public override string ToString() => string.IsNullOrWhiteSpace(Name)
        ? Mode.ToString() : Name + " — " + Mode;
}

public sealed class CampaignScreenSaverSettings
{
    // Windows built-in .scr only. Image campaign slides are NOT executables.
    // A custom slideshow saver requires a separately signed .scr deployment.
    public string Executable { get; set; } = "scrnsave.scr";
    public int IdleSeconds { get; set; } = 600;
    public bool PasswordOnResume { get; set; } = true;
}

public static class CampaignTargetAssignments
{
    public static bool NeedsLockScreen(CampaignTargetMode mode) =>
        mode is CampaignTargetMode.LockScreen or CampaignTargetMode.Both;
    public static bool NeedsScreenSaver(CampaignTargetMode mode) =>
        mode is CampaignTargetMode.ScreenSaver or CampaignTargetMode.Both;

    public static readonly string[] BuiltInScreenSavers =
        ["scrnsave.scr", "Mystify.scr", "Ribbons.scr", "Bubbles.scr"];

    public static void Validate(CampaignProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var rows = project.TargetAssignments ??
            throw new CampaignValidationException("Target assignment list is unavailable.");
        if (rows.Count > 20)
            throw new CampaignValidationException(
                "A campaign supports at most 20 group assignments per deployment batch.");
        var seenComputer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenUser = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CampaignGroupTarget? row in rows)
        {
            if (row is null || !Enum.IsDefined(row.Mode))
                throw new CampaignValidationException("Unrecognized campaign target mode.");
            if (row.Name.Length > 100 || row.Name.Any(char.IsControl))
                throw new CampaignValidationException("Group assignment label is invalid.");
            if (row.UsesLockScreen)
            {
                RequireDn(row.ComputerOuDn, "OU=", "Computer OU");
                RequireDn(row.ComputerGroupDn, "CN=", "Computer security group");
                if (!seenComputer.Add(row.ComputerOuDn + "|" + row.ComputerGroupDn))
                    throw new CampaignValidationException(
                        "Duplicate Lock Screen target group. Merge the assignments.");
            }
            if (row.UsesScreenSaver)
            {
                RequireDn(row.UserOuDn, "OU=", "User OU");
                RequireDn(row.UserGroupDn, "CN=", "User security group");
                if (!seenUser.Add(row.UserOuDn + "|" + row.UserGroupDn))
                    throw new CampaignValidationException(
                        "Duplicate Screen Saver target group. Merge the assignments.");
            }
        }
        var saver = project.ScreenSaver ??
            throw new CampaignValidationException("Screen Saver settings are unavailable.");
        if (!BuiltInScreenSavers.Contains(saver.Executable,
                StringComparer.OrdinalIgnoreCase))
            throw new CampaignValidationException(
                "Choose a supported built-in Windows .scr; custom programs are not accepted.");
        if (saver.IdleSeconds is < 60 or > 86400)
            throw new CampaignValidationException(
                "Screen Saver idle timeout must be between 60 and 86400 seconds.");
    }

    private static void RequireDn(string? dn, string prefix, string label)
    {
        if (string.IsNullOrWhiteSpace(dn) || dn.Length > 2048 ||
            dn.Any(char.IsControl) ||
            !dn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !dn.Contains(",DC=", StringComparison.OrdinalIgnoreCase))
            throw new CampaignValidationException(
                "Select a complete Active Directory distinguished name for " + label + ".");
    }
}
