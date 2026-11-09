using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Per-user company/AD workspace, NEVER an authorization boundary. Selecting
/// a workspace may prefill a domain and an OU but must not create a GPO,
/// assume that an AD group exists, or reuse another company's credentials.
/// These profiles contain no passwords, gMSA keys or client reports.
/// </summary>
internal sealed record CompanyProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("D");
    public string Name { get; init; } = "";
    public string Domain { get; init; } = "";
    public string PreferredDc { get; init; } = "";
    public string DefaultOuDn { get; init; } = "";
    public string DefaultGroupDn { get; init; } = "";
    public string FeedbackShare { get; init; } = "";
    public bool EnableFeedback { get; init; }
    public string TimeZoneId { get; init; } = TimeZoneInfo.Local.Id;
}

internal sealed class CompanyProfilesDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string ActiveProfileId { get; set; } = "";
    public List<CompanyProfile> Profiles { get; set; } = new();
}

internal static class CompanyProfilesStore
{
    private static readonly object Gate = new();
    internal static string? TestRootOverride { get; set; }
    internal static string FilePath => Path.Combine(
        TestRootOverride ?? AppLog.DirectoryPath, "company-profiles.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true, WriteIndented = true
    };
    private const int MaxProfiles = 40;
    private const int MaxFileSize = 65536;

    private static bool Host(string text, bool allowShort)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 253 ||
            text.Any(char.IsControl))
            return false;
        string[] labels = text.Split('.');
        if (!allowShort && labels.Length < 2) return false;
        return labels.All(label => label.Length is >= 1 and <= 63 &&
            char.IsAsciiLetterOrDigit(label[0]) &&
            char.IsAsciiLetterOrDigit(label[^1]) &&
            label.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'));
    }

    internal static CompanyProfile Validate(CompanyProfile input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Guid.TryParse(input.Id, out var id))
            throw new InvalidDataException("Company ID must be a GUID.");
        string name = input.Name.Trim();
        string domain = input.Domain.Trim().ToLowerInvariant();
        string dc = input.PreferredDc.Trim().ToLowerInvariant();
        string ou = input.DefaultOuDn.Trim();
        string group = input.DefaultGroupDn.Trim();
        string share = input.FeedbackShare.Trim();
        string zone = input.TimeZoneId.Trim();
        if (name.Length is < 2 or > 80 || name.Any(char.IsControl))
            throw new InvalidDataException("Company name must be 2–80 printable characters.");
        if (!Host(domain, allowShort: false))
            throw new InvalidDataException("Company domain must be a valid DNS name.");
        if (dc.Length > 0 && !Host(dc, allowShort: true))
            throw new InvalidDataException("Preferred domain controller name is invalid.");
        string suffix = "DC=" + string.Join(",DC=", domain.Split('.'));
        void CheckDn(string dn, string prefix, string title)
        {
            if (dn.Length == 0) return;
            if (dn.Length > 1024 || dn.Any(char.IsControl) ||
                !dn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !dn.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"{title} must be a complete distinguished name in {domain}.");
        }
        CheckDn(ou, "OU=", "Computer OU");
        CheckDn(group, "CN=", "Computer security group");
        if (share.Length > 0 || input.EnableFeedback)
            share = FeedbackPath.ValidateRoot(share);
        if (zone.Length == 0)
            throw new InvalidDataException("A Windows time zone is required.");
        _ = TimeZoneInfo.FindSystemTimeZoneById(zone);
        return input with
        {
            Id = id.ToString("D"), Name = name, Domain = domain,
            PreferredDc = dc, DefaultOuDn = ou, DefaultGroupDn = group,
            FeedbackShare = share, TimeZoneId = zone
        };
    }

    private static CompanyProfilesDocument Read()
    {
        string path = FilePath;
        if (!File.Exists(path)) return new CompanyProfilesDocument();
        var info = new FileInfo(path);
        if (info.Length is < 2 or > MaxFileSize ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Company profiles file is invalid or a filesystem link.");
        var data = JsonSerializer.Deserialize<CompanyProfilesDocument>(
            File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Company profile file is empty.");
        CheckDocument(data);
        return data;
    }

    private static void CheckDocument(CompanyProfilesDocument data)
    {
        if (data.SchemaVersion != 1 || data.Profiles is null ||
            data.Profiles.Count > MaxProfiles)
            throw new InvalidDataException("Unsupported or oversized company profile data.");
        data.Profiles = data.Profiles.Select(Validate).ToList();
        if (data.Profiles.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                data.Profiles.Count ||
            data.Profiles.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                data.Profiles.Count)
            throw new InvalidDataException("Duplicate company identity or name.");
        if (data.ActiveProfileId.Length > 0 &&
            !data.Profiles.Any(x => string.Equals(
                x.Id, data.ActiveProfileId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Selected company no longer exists.");
    }

    private static void Write(CompanyProfilesDocument data)
    {
        CheckDocument(data);
        string path = FilePath;
        string root = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(root);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Refusing to replace a linked company profile file.");
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(data, JsonOptions));
            if (bytes.Length > MaxFileSize)
                throw new InvalidDataException("Company profile data exceeds the storage size limit.");
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static IReadOnlyList<CompanyProfile> All()
    {
        lock (Gate) return Read().Profiles.OrderBy(x => x.Name,
            StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static CompanyProfile? Active()
    {
        lock (Gate)
        {
            var data = Read();
            return data.Profiles.FirstOrDefault(x =>
                string.Equals(x.Id, data.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static void Upsert(CompanyProfile profile)
    {
        profile = Validate(profile);
        lock (Gate)
        {
            var data = Read();
            int existing = data.Profiles.FindIndex(x =>
                string.Equals(x.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            if (data.Profiles.Any(x =>
                x.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase) &&
                !x.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("A company with the same name already exists.");
            if (existing < 0) data.Profiles.Add(profile);
            else data.Profiles[existing] = profile;
            Write(data);
        }
    }

    internal static void SetActive(string? id)
    {
        lock (Gate)
        {
            var data = Read();
            if (!string.IsNullOrEmpty(id) &&
                !data.Profiles.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Company profile does not exist.");
            data.ActiveProfileId = id ?? "";
            Write(data);
        }
    }

    internal static void Remove(string id)
    {
        lock (Gate)
        {
            var data = Read();
            if (id.Equals(data.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Switch to a different company before deleting the active profile.");
            int count = data.Profiles.RemoveAll(x =>
                x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (count != 1)
                throw new InvalidDataException("Company profile was not found.");
            Write(data);
        }
    }

    internal static CampaignProject NewCampaign(CompanyProfile? company)
    {
        string zone = company?.TimeZoneId ?? TimeZoneInfo.Local.Id;
        DateTime inCompanyZone = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(zone));
        return new CampaignProject {
            Name = "", // New projects are untitled until the operator chooses a campaign type.
            RequiresCampaignTypeChoice = true,
            CompanyProfileId = company?.Id ?? "",
            TimeZoneId = zone,
            EnableFeedback = company?.EnableFeedback ?? false,
            FeedbackShare = company?.FeedbackShare ?? "",
            FeedbackIntervalMinutes = 15,
            StartLocal = inCompanyZone.Date.AddDays(1).AddHours(14),
            LastImageHours = 24
        };
    }
}
