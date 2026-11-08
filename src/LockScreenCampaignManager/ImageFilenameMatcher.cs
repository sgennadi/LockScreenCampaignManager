using System.Globalization;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Offline, advisory name-to-schedule inference. No file is modified or assigned
/// without the operator accepting the preview in the import dialog.
/// </summary>
internal static class ImageFilenameMatcher
{
    internal sealed record Hint(string FullPath, DateOnly? Date, DayOfWeek? Weekday,
        int? Ordinal, bool Conflicting, string Evidence);
    internal sealed record Slot(int Number, DateTime LocalTime, string? SuggestedPath,
        string Reason, bool Strong);
    internal sealed record Proposal(IReadOnlyList<string> Files, IReadOnlyList<Slot> Slots,
        CampaignProject Schedule, int ConfidentCount, int ReviewCount);

    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp"];
    internal static bool IsSupportedImage(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DayOfWeek> DayTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["mon"]=DayOfWeek.Monday, ["monday"]=DayOfWeek.Monday,
            ["пн"]=DayOfWeek.Monday, ["понедельник"]=DayOfWeek.Monday,
            ["tue"]=DayOfWeek.Tuesday, ["tues"]=DayOfWeek.Tuesday,
            ["tuesday"]=DayOfWeek.Tuesday, ["вт"]=DayOfWeek.Tuesday,
            ["вторник"]=DayOfWeek.Tuesday,
            ["wed"]=DayOfWeek.Wednesday, ["wednesday"]=DayOfWeek.Wednesday,
            ["ср"]=DayOfWeek.Wednesday, ["среда"]=DayOfWeek.Wednesday,
            ["thu"]=DayOfWeek.Thursday, ["thurs"]=DayOfWeek.Thursday,
            ["thursday"]=DayOfWeek.Thursday, ["чт"]=DayOfWeek.Thursday,
            ["четверг"]=DayOfWeek.Thursday,
            ["fri"]=DayOfWeek.Friday, ["friday"]=DayOfWeek.Friday,
            ["пт"]=DayOfWeek.Friday, ["пятница"]=DayOfWeek.Friday,
            ["sat"]=DayOfWeek.Saturday, ["saturday"]=DayOfWeek.Saturday,
            ["сб"]=DayOfWeek.Saturday, ["суббота"]=DayOfWeek.Saturday,
            ["sun"]=DayOfWeek.Sunday, ["sunday"]=DayOfWeek.Sunday,
            ["вс"]=DayOfWeek.Sunday, ["воскресенье"]=DayOfWeek.Sunday,
            // Hebrew full-day names with the initial יום prefix removed.
            ["ראשון"]=DayOfWeek.Sunday, ["שני"]=DayOfWeek.Monday,
            ["שלישי"]=DayOfWeek.Tuesday, ["רביעי"]=DayOfWeek.Wednesday,
            ["חמישי"]=DayOfWeek.Thursday, ["שישי"]=DayOfWeek.Friday,
            ["שבת"]=DayOfWeek.Saturday
        };

    internal static IReadOnlyList<string> ScanFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new DirectoryNotFoundException("Select an existing image folder.");
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Importing from a linked folder is not supported.");
        var paths = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Extensions.Contains(Path.GetExtension(path),
                StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Take(CampaignSchedule.MaxImages + 1).ToArray();
        if (paths.Length == 0)
            throw new InvalidDataException("The folder contains no JPG, PNG or BMP images.");
        if (paths.Length > CampaignSchedule.MaxImages)
            throw new InvalidDataException("The folder contains more than 30 images. Use a smaller folder.");
        foreach (string file in paths)
        {
            var info = new FileInfo(file);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked image files are not accepted.");
            if (info.Length == 0)
                throw new InvalidDataException("An image is empty: " + info.Name);
        }
        return paths;
    }

    private static readonly Regex ExplicitDate = new(
        @"(?<!\d)(?:\d{4}[-_.]\d{2}[-_.]\d{2}|\d{2}[-_.]\d{2}[-_.]\d{4}|\d{8})(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy_MM_dd", "yyyy.MM.dd",
        "dd.MM.yyyy", "dd-MM-yyyy", "dd_MM_yyyy", "yyyyMMdd"
    ];

    private static (DateOnly? Date, bool Conflicting, string WithoutDates)
        ParseDates(string name)
    {
        // No guessing ambiguous US-format dates. A malformed date token, or
        // multiple date tokens, must go to operator review instead of being
        // mistaken for a slide number.
        var matches = ExplicitDate.Matches(name);
        if (matches.Count == 0) return (null, false, name);
        var withoutDates = ExplicitDate.Replace(name, " ");
        if (matches.Count != 1 ||
            !DateOnly.TryParseExact(matches[0].Value, DateFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return (null, true, withoutDates);
        return (parsed, false, withoutDates);
    }

    internal static Hint Parse(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var parsedDate = ParseDates(name);
        DateOnly? date = parsedDate.Date;
        // Separate letter runs and digits: Monday01.jpg and Tuesday_02.jpg
        // must both recognize weekday + ordinal hints.
        var tokens = Regex.Matches(name.ToLowerInvariant(), @"[\p{L}]+")
            .Select(match => match.Value).ToArray();
        var weekdays = tokens.Where(DayTokens.ContainsKey)
            .Select(token => DayTokens[token]).Distinct().ToArray();
        // Date digits are NOT slide numbers. Any standalone ordinal alongside
        // a date, however, must also agree with the scheduled slot.
        var numbers = Regex.Matches(parsedDate.WithoutDates,
                @"(?<!\d)(?:0?[1-9]|[12]\d|30)(?!\d)")
            .Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture))
            .Distinct().ToArray();
        int? ordinal = numbers.Length == 1 ? numbers[0] : null;
        bool conflict = parsedDate.Conflicting || weekdays.Length > 1 ||
            (date is not null && weekdays.Length == 1 &&
             date.Value.DayOfWeek != weekdays[0]) ||
            numbers.Length > 1;
        string evidence = conflict ? "Conflicting filename clues; review manually"
            : date is not null ? "Date in filename"
            : weekdays.Length == 1 && ordinal.HasValue ? "Weekday and image number"
            : weekdays.Length == 1 ? "Weekday in filename"
            : ordinal.HasValue ? "Image number in filename"
            : "No recognized day or number; alphabetical suggestion";
        return new Hint(path, date,
            weekdays.Length == 1 ? weekdays[0] : null,
            ordinal, conflict, evidence);
    }

    internal static Proposal Suggest(CampaignProject source, IReadOnlyList<string> paths)
    {
        if (paths.Count is < 1 or > CampaignSchedule.MaxImages ||
            paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count)
            throw new InvalidDataException("Select 1–30 distinct images.");
        var hints = paths.Select(Parse).ToArray();
        var schedule = new CampaignProject {
            Name = source.Name, CompanyProfileId = source.CompanyProfileId,
            TimeZoneId = source.TimeZoneId, StartLocal = source.StartLocal,
            LastImageHours = source.LastImageHours,
            ImagePaths = paths.ToList(), ScheduleMode = source.ScheduleMode,
            WeekdayMask = source.WeekdayMask, IntervalMinutes = source.IntervalMinutes,
            RandomizePerDevice = source.RandomizePerDevice,
            EnableFeedback = source.EnableFeedback, FeedbackShare = source.FeedbackShare,
            FeedbackIntervalMinutes = source.FeedbackIntervalMinutes,
            SpecificDatesLocal = new List<DateTime>(source.SpecificDatesLocal)
        };
        if (schedule.ScheduleMode == CampaignScheduleMode.SpecificDates &&
            schedule.SpecificDatesLocal.Count != paths.Count)
        {
            // In a fresh, empty SpecificDates campaign, filenames can propose
            // the actual dates. No silent date override of an existing schedule.
            var dates = hints.Select(h => h.Date).ToArray();
            if (hints.All(h => !h.Conflicting) && dates.All(d => d.HasValue) &&
                dates.Distinct().Count() == paths.Count)
            {
                DateTime clock = source.StartLocal;
                schedule.SpecificDatesLocal = dates.Select(d =>
                    d!.Value.ToDateTime(TimeOnly.FromDateTime(clock)))
                    .OrderBy(d => d).ToList();
            }
            else
            {
                schedule.SpecificDatesLocal = Enumerable.Range(0, paths.Count)
                    .Select(i => source.StartLocal.AddDays(i)).ToList();
            }
            schedule.StartLocal = schedule.SpecificDatesLocal[0];
        }
        // Precompute every slot before suggesting a strong match. In a
        // minute/hour interval or a multi-week campaign a day-only hint may
        // fit several slots; greedily picking the first would be misleading.
        var times = Enumerable.Range(0, paths.Count)
            .Select(slot => CampaignSchedule.At(schedule, slot)).ToArray();
        var assigned = new HashSet<int>();
        var slots = new List<Slot>(paths.Count);
        for (int slot = 0; slot < paths.Count; slot++)
        {
            DateTime when = times[slot];
            var candidates = hints.Select((hint, idx) => (hint, idx,
                    score: Score(hint, when, slot + 1)))
                .Where(x => x.score > 0 && !assigned.Contains(x.idx))
                .ToArray();
            // A shared weekday/date clue must not overshadow a less-specific
            // but uniquely numbered file for this slot.
            var scored = candidates.Where(x => x.score < 70 ||
                    times.Select((time, index) => Score(x.hint, time, index + 1))
                        .Count(score => score >= x.score) == 1)
                .OrderByDescending(x => x.score).ThenBy(x => x.idx)
                .ToArray();
            if (scored.Length == 0)
            {
                slots.Add(new Slot(slot + 1, when, null,
                    candidates.Length > 0
                        ? "Filename fits multiple scheduled slots; choose the image manually"
                        : "No matching filename; choose an image manually", false));
                continue;
            }
            if (scored.Length > 1 && scored[0].score == scored[1].score)
            {
                slots.Add(new Slot(slot + 1, when, null,
                    "Ambiguous filenames; choose an image manually", false));
                continue;
            }
            var best = scored[0];
            bool strong = best.score >= 70;
            assigned.Add(best.idx);
            slots.Add(new Slot(slot + 1, when, best.hint.FullPath,
                strong ? best.hint.Evidence + " — matches schedule"
                    : "Alphabetical fallback; confirm this choice", strong));
        }
        return new Proposal(paths.ToArray(), slots, schedule,
            slots.Count(s => s.Strong),
            slots.Count(s => !s.Strong || s.SuggestedPath is null));
    }

    private static int Score(Hint file, DateTime time, int number)
    {
        if (file.Conflicting) return 0;
        if (file.Date.HasValue && file.Date.Value != DateOnly.FromDateTime(time))
            return 0;
        if (file.Weekday.HasValue && file.Weekday.Value != time.DayOfWeek)
            return 0;
        if (file.Ordinal.HasValue && file.Ordinal.Value != number)
            return 0;
        if (file.Date.HasValue) return 110;
        if (file.Weekday.HasValue && file.Ordinal.HasValue) return 100;
        if (file.Weekday.HasValue) return 90;
        if (file.Ordinal.HasValue) return 80;
        return 10;
    }
}
