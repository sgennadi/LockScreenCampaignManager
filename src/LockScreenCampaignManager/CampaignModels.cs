using System.Globalization;
using System.Text.Json.Serialization;

namespace LockScreenCampaignManager;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CampaignScheduleMode
{
    Daily,
    Workdays,
    CustomDays,
    SpecificDates,
    EveryInterval
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CampaignClientType
{
    PowerShell = 0,
    Executable = 1
}

public sealed class CampaignProject
{
    public string Name { get; set; } = "Lock Screen Campaign";
    // In-memory only: a newly created campaign must explicitly select a type,
    // even if images were imported through Advanced. Legacy saved projects
    // without this flag still infer their old Lock Screen/user-group mode.
    [JsonIgnore] public bool RequiresCampaignTypeChoice { get; set; }
    // Local editing context only; never exported to the endpoint campaign.json.
    public string CompanyProfileId { get; set; } = string.Empty;
    // Existing projects remain PowerShell by default; chosen in Deploy.
    public CampaignClientType ClientType { get; set; } = CampaignClientType.PowerShell;
    // Empty list means legacy single-target Lock Screen deployment.
    // New projects can assign several computer/user groups independently.
    public List<CampaignGroupTarget> TargetAssignments { get; set; } = new();
    // For new campaigns, the type is chosen BEFORE files in the guided wizard.
    // Legacy projects without the field default to LockScreen.
    public CampaignTargetMode CampaignMode { get; set; } = CampaignTargetMode.LockScreen;
    // v0.4.0-preview.72 projects have group rows but no campaign type field.
    // Infer their mode rather than misclassifying a saved user-only campaign.
    public bool CampaignModeExplicit { get; set; } = false;
    [JsonIgnore]
    public CampaignTargetMode EffectiveCampaignMode
    {
        get
        {
            if (CampaignModeExplicit) return CampaignMode;
            bool lockScreen = TargetAssignments?.Any(x => x.UsesLockScreen) == true;
            bool screenSaver = TargetAssignments?.Any(x => x.UsesScreenSaver) == true;
            return lockScreen && screenSaver ? CampaignTargetMode.Both :
                screenSaver ? CampaignTargetMode.ScreenSaver : CampaignTargetMode.LockScreen;
        }
    }
    public CampaignScreenSaverSettings ScreenSaver { get; set; } = new();
    public DateTime StartLocal { get; set; } = DateTime.Today.AddDays(1).AddHours(14);
    public int LastImageHours { get; set; } = 24;
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public List<string> ImagePaths { get; set; } = new();
    // Legacy projects without these fields remain Daily and in fixed order.
    public CampaignScheduleMode ScheduleMode { get; set; } = CampaignScheduleMode.Daily;
    // Monday=1, Tuesday=2, ... Sunday=64; used only for CustomDays.
    public int WeekdayMask { get; set; } = 127;
    // Only used when ScheduleMode=EveryInterval. Default: every 02:30.
    // Range 1 minute to 7 days; integer minutes prevent fractional drift.
    public int IntervalMinutes { get; set; } = 150;
    // One local date/time per image slot when ScheduleMode=SpecificDates.
    public List<DateTime> SpecificDatesLocal { get; set; } = new();
    public bool RandomizePerDevice { get; set; } = false;
    public bool EnableFeedback { get; set; } = false;
    public string FeedbackShare { get; set; } = string.Empty;
    public int FeedbackIntervalMinutes { get; set; } = 15;
}

public sealed class CampaignConfig
{
    public int SchemaVersion { get; set; } = 1;
    public string CampaignId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public string EndLocal { get; set; } = string.Empty;
    public string AgentSha256 { get; set; } = string.Empty;
    // Keep schema-1 PS1 manifests byte-compatible by omitting the default.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CampaignClientType ClientType { get; set; } = CampaignClientType.PowerShell;
    // False for existing manifests. Stable per-device shuffle of which verified
    // image belongs to each scheduled slot; never changes scheduled times.
    public bool RandomizePerDevice { get; set; } = false;
    public List<CampaignSlide> Slides { get; set; } = new();
    public CampaignFeedback? Feedback { get; set; }
}

public sealed class CampaignFeedback
{
    public bool Enabled { get; set; } = false;
    public string ShareRoot { get; set; } = string.Empty;
    public int IntervalMinutes { get; set; } = 15;
}

public sealed class CampaignSlide
{
    public string AtLocal { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
/// Expected, actionable campaign configuration input. It is not an
/// application fault and must not produce a diagnostic error report.
/// </summary>
public sealed class CampaignValidationException : InvalidOperationException
{
    public CampaignValidationException(string message) : base(message) { }
}

public static class CampaignSchedule
{
    public const int MaxImages = 30;
    public const int MaxIntervalMinutes = 7 * 24 * 60;
    public const string DateFormat = "yyyy-MM-ddTHH:mm:ss";

    public static DateTime At(CampaignProject project, int imageIndex)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (imageIndex < 0 || imageIndex >= project.ImagePaths.Count)
            throw new ArgumentOutOfRangeException(nameof(imageIndex));
        var start = DateTime.SpecifyKind(project.StartLocal, DateTimeKind.Unspecified);
        if (project.ScheduleMode == CampaignScheduleMode.SpecificDates)
        {
            if (project.SpecificDatesLocal is null ||
                project.SpecificDatesLocal.Count != project.ImagePaths.Count)
                throw new InvalidOperationException("Specific dates require one date/time per image.");
            return DateTime.SpecifyKind(project.SpecificDatesLocal[imageIndex],
                DateTimeKind.Unspecified);
        }
        if (project.ScheduleMode == CampaignScheduleMode.Daily)
            return start.AddDays(imageIndex);
        if (project.ScheduleMode == CampaignScheduleMode.EveryInterval)
        {
            if (project.IntervalMinutes is < 1 or > MaxIntervalMinutes)
                throw new InvalidOperationException(
                    "Image change interval must be between 1 minute and 7 days.");
            // Elapsed minutes, not wall-clock arithmetic: crossing spring DST
            // skips the missing hour instead of moving the event early.
            // Fall-back ambiguous/repeated wall times are rejected by Validate
            // because the v1 manifest has no UTC offset per slide.
            var zone = TimeZoneInfo.FindSystemTimeZoneById(project.TimeZoneId);
            if (zone.IsInvalidTime(start) || zone.IsAmbiguousTime(start))
                throw new InvalidOperationException(
                    "The first interval time is invalid or ambiguous due to DST.");
            DateTime utcStart = TimeZoneInfo.ConvertTimeToUtc(start, zone);
            DateTime utcEvent = utcStart.AddMinutes(
                (long)imageIndex * project.IntervalMinutes);
            return DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeFromUtc(utcEvent, zone),
                DateTimeKind.Unspecified);
        }
        int mask = project.ScheduleMode switch
        {
            CampaignScheduleMode.Workdays => 31,
            CampaignScheduleMode.CustomDays => project.WeekdayMask,
            _ => throw new InvalidOperationException("Unsupported schedule mode.")
        };
        if (mask is < 1 or > 127)
            throw new InvalidOperationException("Select at least one weekday.");
        int found = -1;
        for (int offset = 0; offset < 366 * 2; offset++)
        {
            DateTime candidate = start.AddDays(offset);
            int weekday = ((int)candidate.DayOfWeek + 6) % 7; // Monday first
            if ((mask & (1 << weekday)) == 0) continue;
            if (++found == imageIndex) return candidate;
        }
        throw new InvalidOperationException("Selected weekdays exceed the supported schedule horizon.");
    }

    public static DateTime End(CampaignProject project)
    {
        if (project.ImagePaths is null || project.ImagePaths.Count == 0)
            return project.StartLocal;
        return At(project, project.ImagePaths.Count - 1).AddHours(project.LastImageHours);
    }

    public static string Format(DateTime value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    // A human-readable GPO display name. Keep BuildId unchanged: it is the
    // immutable campaign/file identity used for deployment and cleanup.
    public static string DefaultGpoName(CampaignProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        string companySuffix = Guid.TryParse(project.CompanyProfileId, out Guid company)
            ? "-c" + company.ToString("N")[..8] : "";
        string product = project.EffectiveCampaignMode switch
        {
            CampaignTargetMode.ScreenSaver => "ScreenSaver",
            CampaignTargetMode.Both => "LockScreen+ScreenSaver",
            _ => "LockScreen"
        };
        return $"{product} - {project.StartLocal:yyyyMMdd-HHmm}{companySuffix}";
    }

    public static string BuildId(CampaignProject project)
    {
        string normalized = new string(project.Name.ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        normalized = string.Join("-", normalized.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 25) normalized = normalized[..25].TrimEnd('-');
        if (normalized.Length == 0) normalized = "campaign";
        // Company-bound campaigns receive a stable namespace suffix to avoid
        // collisions when separate companies use the same name/start date.
        // All previous unassigned campaign IDs remain byte-for-byte unchanged.
        string companySuffix = Guid.TryParse(project.CompanyProfileId, out Guid company)
            ? "-c" + company.ToString("N")[..8] : "";
        return $"{normalized}-{project.StartLocal:yyyyMMddHHmm}{companySuffix}";
    }

    public static void Validate(CampaignProject project, bool requireImages)
    {
        if (project.RequiresCampaignTypeChoice && !project.CampaignModeExplicit)
            throw new CampaignValidationException("Choose Lock Screen, Screen Saver or Both first.");
        if (string.IsNullOrWhiteSpace(project.Name))
            throw new CampaignValidationException("Enter a campaign name.");
        if (project.Name.Length > 100)
            throw new CampaignValidationException("Campaign name must not exceed 100 characters.");
        if (!string.IsNullOrEmpty(project.CompanyProfileId) &&
            !Guid.TryParse(project.CompanyProfileId, out _))
            throw new CampaignValidationException("Campaign company identity must be a GUID.");
        if (project.LastImageHours < 1 || project.LastImageHours > 72)
            throw new CampaignValidationException("Last image duration must be 1–72 hours.");
        if (project.ImagePaths is null || project.ImagePaths.Count > MaxImages)
            throw new CampaignValidationException($"Maximum {MaxImages} images.");
        if (!Enum.IsDefined(project.ScheduleMode))
            throw new CampaignValidationException("Unrecognized schedule mode.");
        if (project.ScheduleMode == CampaignScheduleMode.CustomDays &&
            project.WeekdayMask is < 1 or > 127)
            throw new CampaignValidationException("Choose at least one weekday.");
        if (project.ScheduleMode == CampaignScheduleMode.EveryInterval &&
            project.IntervalMinutes is < 1 or > MaxIntervalMinutes)
            throw new CampaignValidationException(
                "Image change interval must be between 00:01 and 168:00.");
        if (project.SpecificDatesLocal is null ||
            project.SpecificDatesLocal.Count > MaxImages)
            throw new CampaignValidationException("Invalid list of specific dates.");
        if (project.ScheduleMode == CampaignScheduleMode.SpecificDates &&
            project.SpecificDatesLocal.Count != project.ImagePaths.Count)
            throw new CampaignValidationException(
                "Specific dates require exactly one date/time per image.");
        if (project.ScheduleMode == CampaignScheduleMode.SpecificDates &&
            project.ImagePaths.Count > 0 &&
            DateTime.SpecifyKind(project.SpecificDatesLocal[0], DateTimeKind.Unspecified) !=
                DateTime.SpecifyKind(project.StartLocal, DateTimeKind.Unspecified))
            throw new CampaignValidationException(
                "The campaign start must equal the first scheduled image date/time.");
        DateTime previous = DateTime.MinValue;
        for (int i = 0; i < project.ImagePaths.Count; i++)
        {
            DateTime date = At(project, i);
            if (date <= previous || date < project.StartLocal)
                throw new CampaignValidationException(
                    "Scheduled dates must increase and cannot precede the campaign start.");
            if (date > project.StartLocal.AddYears(2))
                throw new CampaignValidationException("Campaign schedule exceeds two years.");
            previous = date;
        }
        CampaignTargetAssignments.Validate(project);
        if (!Enum.IsDefined(project.EffectiveCampaignMode))
            throw new CampaignValidationException("Choose Lock Screen, Screen Saver or Both.");
        var chosenMode = project.EffectiveCampaignMode;
        if (project.CampaignModeExplicit && project.TargetAssignments.Any(row =>
            (row.UsesLockScreen && chosenMode == CampaignTargetMode.ScreenSaver) ||
            (row.UsesScreenSaver && chosenMode == CampaignTargetMode.LockScreen)))
            throw new CampaignValidationException(
                "Group assignments include a different target type. Choose Both or edit group assignments.");
        bool needsImages = chosenMode is CampaignTargetMode.LockScreen or
            CampaignTargetMode.Both ||
            project.TargetAssignments.Any(x => x.UsesLockScreen);
        if (requireImages && needsImages && project.ImagePaths.Count == 0)
            throw new CampaignValidationException(
                project.TargetAssignments.Any(x => x.UsesLockScreen)
                    ? "Lock Screen assignments need at least one image."
                    : "Add at least one image.");
        if (project.EnableFeedback)
        {
            FeedbackPath.ValidateRoot(project.FeedbackShare);
            if (project.FeedbackIntervalMinutes is < 5 or > 60)
                throw new CampaignValidationException("Feedback interval must be 5–60 minutes.");
        }
        var zone = TimeZoneInfo.FindSystemTimeZoneById(project.TimeZoneId);
        foreach (var time in Enumerable.Range(0, project.ImagePaths.Count).Select(i => At(project, i))
                     .Append(End(project)))
        {
            if (zone.IsInvalidTime(time))
                throw new CampaignValidationException($"Invalid local time due to DST: {time:yyyy-MM-dd HH:mm}");
            if (zone.IsAmbiguousTime(time))
                throw new CampaignValidationException($"Ambiguous local time due to DST: {time:yyyy-MM-dd HH:mm}");
        }
        foreach (var path in project.ImagePaths)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Image not found", path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp"))
                throw new CampaignValidationException($"Unsupported image format: {path}");
        }
    }
}
