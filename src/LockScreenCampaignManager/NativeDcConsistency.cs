using System.ComponentModel;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only verification of explicitly selected domain controllers using
/// signed LDAP, exact-DC UNC paths and SHA-256. No PS1, RSAT or writes.
/// Network-only impersonation keeps alternate SMB and LDAP identities aligned.
/// </summary>
internal static class NativeDcConsistency
{
    private const int MaxControllers = 12;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> InvalidReasons = new(StringComparer.Ordinal)
    {
        "InvalidFileTypeOrLength", "InvalidDirectoryOrReparsePoint",
        "InvalidDomainControllerUtc", "MissingDomainControllerRootDseFields",
        "DomainControllerIdentityMismatch", "SlowOrUnverifiableDcTimeSample",
        "UnexpectedCampaignLayout", "ManifestOrAgentIntegrityMismatch",
        "InvalidManifestImageCount", "UnexpectedImageCount",
        "InvalidImageManifestEntry", "ImageSha256Mismatch",
        "UnexpectedImageFile", "GptVersionMissing", "InvalidGptVersion",
        "GpoStartupTargetsOtherCampaign", "ManagedGpoAdMissing",
        "AdGptVersionMismatch", "CampaignChangedDuringScan"
    };

    private sealed class ImageRow
    {
        public string name { get; set; } = "";
        public string sha256 { get; set; } = "";
        public long bytes { get; set; }
    }
    private sealed class AuditRow(string name)
    {
        public string dc { get; } = name;
        public string status { get; set; } = "Unavailable";
        public string reason { get; set; } = "ReadDeniedOrUnavailable";
        public string dcUtc { get; set; } = "";
        public string sampledUtc { get; set; } = "";
        public int offsetSeconds { get; set; }
        public string rodc { get; set; } = "Unknown";
        public string manifestHash { get; set; } = "";
        public string agentHash { get; set; } = "";
        public string gptIniHash { get; set; } = "";
        public string psScriptsHash { get; set; } = "";
        public long adVersion { get; set; } = -1;
        public long gptVersion { get; set; } = -1;
        public List<ImageRow> images { get; set; } = [];
    }

    internal static JsonElement Execute(string mode, object request, DomainCredentials credentials)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DC consistency requires Windows authentication.");
        if (mode != "Scan")
            throw new ArgumentException("Only read-only multi-DC scans are supported.");
        JsonElement args = JsonSerializer.SerializeToElement(request, Json);
        string domain = Get(args, "domain").Trim().ToLowerInvariant();
        string baseline = Get(args, "dc").Trim().ToLowerInvariant();
        string campaignId = Get(args, "campaignId");
        NativeAdDiscovery.ValidateNames(domain, baseline);
        if (!ValidController(baseline, domain) ||
            !Regex.IsMatch(campaignId, @"\A[a-z0-9][a-z0-9_-]{0,70}\z"))
            throw new InvalidDataException("Invalid selected DC or campaign identifier.");
        if (!Guid.TryParse(Get(args, "gpoId"), out var gpo) || gpo == Guid.Empty)
            throw new InvalidDataException("Choose a valid managed GPO.");
        if (!args.TryGetProperty("controllers", out var controllers) ||
            controllers.ValueKind != JsonValueKind.Array ||
            controllers.GetArrayLength() is < 1 or > MaxControllers)
            throw new InvalidDataException("Select 1–12 discovered domain controllers.");
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in controllers.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Invalid controller selection.");
            string dc = (entry.GetString() ?? "").Trim().ToLowerInvariant();
            if (!ValidController(dc, domain) || !hosts.Add(dc))
                throw new InvalidDataException("Duplicate or out-of-domain controller.");
        }
        if (!hosts.Contains(baseline))
            throw new InvalidDataException("Selected baseline DC must be in the audit scope.");

        JsonElement Run()
        {
            var rows = hosts.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(dc => ScanOne(dc, domain, campaignId, gpo, credentials))
                .ToArray();
            return JsonSerializer.SerializeToElement(new
            {
                success = true, message = "Native read-only DC consistency scan completed.",
                data = new
                {
                    domain, selectedDc = baseline, campaignId,
                    gpoId = gpo.ToString("D"),
                    scannedUtc = DateTimeOffset.UtcNow.ToString("O"),
                    rows
                }
            }, Json);
        }
        if (!credentials.IsAlternate) return Run();

        // This is Windows LOGON_NETCREDENTIALS_ONLY: no elevation or local
        // account switch. It is required for SMB to use alternate network
        // credentials, matching the LDAP identity used for each DC.
        string userName = credentials.UserName.Trim();
        string? userDomain = null;
        int slash = userName.IndexOf('\\');
        if (slash > 0 && slash < userName.Length - 1)
        {
            userDomain = userName[..slash];
            userName = userName[(slash + 1)..];
        }
        else if (!userName.Contains('@'))
            userName += "@" + credentials.Domain.Trim();
        if (credentials.Password.Length == 0 ||
            !LogonUser(userName, userDomain, credentials.Password,
                9, 3, out var identity))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot establish network credentials for selected DC audit.");
        using (identity)
            return WindowsIdentity.RunImpersonated(identity, Run);
    }

    internal static bool ValidController(string host, string domain)
    {
        if (!host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(host, @"\A(?=.{3,253}\z)[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+\z"))
            return false;
        return host.Split('.').All(label => label.Length is >= 1 and <= 63 &&
            label[0] != '-' && label[^1] != '-');
    }

    internal static DateTime ParseDcTime(string value)
    {
        var match = Regex.Match(value, @"\A(?<stamp>\d{14})(?:\.\d{1,7})?Z\z");
        if (!match.Success ||
            !DateTime.TryParseExact(match.Groups["stamp"].Value,
                "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
            throw new InvalidDataException("InvalidDomainControllerUtc");
        return timestamp;
    }

    private static string Get(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static void RegularDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists) throw new DirectoryNotFoundException();
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("InvalidDirectoryOrReparsePoint");
    }

    private static (string Hash, long Length) DigestFile(string path, long limit)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException();
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
            info.Length is < 1 || info.Length > limit)
            throw new InvalidDataException("InvalidFileTypeOrLength");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        string digest = Convert.ToHexStringLower(SHA256.HashData(stream));
        return (digest, info.Length);
    }

    private static long ReadGpoVersion(LdapConnection ldap, string baseDn, Guid id)
    {
        string guid = "{" + id.ToString("D").ToUpperInvariant() + "}";
        string gpoDn = "CN=" + guid + ",CN=Policies,CN=System," + baseDn;
        var response = (SearchResponse)ldap.SendRequest(new SearchRequest(
            gpoDn, "(objectClass=groupPolicyContainer)", SearchScope.Base,
            "versionNumber"));
        if (response.Entries.Count != 1 ||
            !long.TryParse(NativeAdDiscovery.Attr(response.Entries[0], "versionNumber"),
                NumberStyles.None, CultureInfo.InvariantCulture, out long version))
            throw new InvalidDataException("ManagedGpoAdMissing");
        return version;
    }

    private static AuditRow ScanOne(string dc, string domain, string campaignId,
        Guid gpo, DomainCredentials credentials)
    {
        var row = new AuditRow(dc);
        try
        {
            using var ldap = NativeAdDiscovery.Connect(dc, credentials);
            DateTime begin = DateTime.UtcNow;
            var root = NativeAdDiscovery.ReadRootDse(ldap);
            DateTime end = DateTime.UtcNow;
            string baseDn = NativeAdDiscovery.Attr(root, "defaultNamingContext");
            string host = NativeAdDiscovery.Attr(root, "dnsHostName");
            string dcClock = NativeAdDiscovery.Attr(root, "currentTime");
            if (baseDn.Length == 0 || host.Length == 0 || dcClock.Length == 0)
                throw new InvalidDataException("MissingDomainControllerRootDseFields");
            if (!string.Equals(NativeAdDiscovery.DomainFromDn(baseDn), domain,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(host, dc, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DomainControllerIdentityMismatch");
            DateTime utc = ParseDcTime(dcClock);
            DateTime sampled = begin.AddTicks((end - begin).Ticks / 2);
            if ((end - begin).TotalSeconds > 30)
                throw new InvalidDataException("SlowOrUnverifiableDcTimeSample");
            row.dcUtc = utc.ToString("O");
            row.sampledUtc = sampled.ToString("O");
            row.offsetSeconds = (int)Math.Round((utc - sampled).TotalSeconds);
            string rodcFlag = NativeAdDiscovery.Attr(root, "isRODC");
            row.rodc = rodcFlag.Length == 0 ? "Unknown" :
                rodcFlag.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No";

            string campaignRoot = Path.Combine(@"\\", dc, "SYSVOL", domain,
                "scripts", "LockScreenCampaigns", campaignId);
            string imageRoot = Path.Combine(campaignRoot, "images");
            RegularDirectory(campaignRoot);
            RegularDirectory(imageRoot);
            string[] entries = Directory.GetFileSystemEntries(campaignRoot)
                .Select(Path.GetFileName).ToArray()!;
            if (entries.Length != 3 ||
                entries.Any(x => x is not ("campaign.json" or "LockScreen-Agent.ps1" or "images")))
                throw new InvalidDataException("UnexpectedCampaignLayout");
            string manifest = Path.Combine(campaignRoot, "campaign.json");
            string agent = Path.Combine(campaignRoot, "LockScreen-Agent.ps1");
            var manifestInfo = DigestFile(manifest, 1024 * 1024);
            var agentInfo = DigestFile(agent, 5 * 1024 * 1024);
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest, Encoding.UTF8),
                new JsonDocumentOptions { MaxDepth = 12 });
            var cfg = doc.RootElement;
            if (cfg.GetProperty("schemaVersion").GetInt32() != 1 ||
                cfg.GetProperty("campaignId").GetString() != campaignId ||
                !EqualsHash(Get(cfg, "agentSha256"), agentInfo.Hash))
                throw new InvalidDataException("ManifestOrAgentIntegrityMismatch");
            JsonElement slides = cfg.GetProperty("slides");
            if (slides.ValueKind != JsonValueKind.Array ||
                slides.GetArrayLength() is < 1 or > 30)
                throw new InvalidDataException("InvalidManifestImageCount");
            string[] imageFiles = Directory.GetFileSystemEntries(imageRoot)
                .Select(Path.GetFileName).ToArray()!;
            if (imageFiles.Length != slides.GetArrayLength())
                throw new InvalidDataException("UnexpectedImageCount");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var images = new List<ImageRow>();
            foreach (var slide in slides.EnumerateArray())
            {
                string filename = Get(slide, "fileName");
                string hash = Get(slide, "sha256");
                if (!Regex.IsMatch(filename, @"\A[0-9]{2}\.jpg\z") ||
                    !seen.Add(filename) ||
                    !Regex.IsMatch(hash, @"\A[a-fA-F0-9]{64}\z"))
                    throw new InvalidDataException("InvalidImageManifestEntry");
                var actual = DigestFile(Path.Combine(imageRoot, filename), 30 * 1024 * 1024);
                if (!EqualsHash(hash, actual.Hash))
                    throw new InvalidDataException("ImageSha256Mismatch");
                images.Add(new ImageRow { name = filename,
                    sha256 = actual.Hash, bytes = actual.Length });
            }
            if (imageFiles.Any(f => !seen.Contains(f ?? "")))
                throw new InvalidDataException("UnexpectedImageFile");

            string gptRoot = Path.Combine(@"\\", dc, "SYSVOL", domain, "Policies",
                "{" + gpo.ToString("D").ToUpperInvariant() + "}");
            string machineRoot = Path.Combine(gptRoot, "Machine");
            string scriptRoot = Path.Combine(machineRoot, "Scripts");
            RegularDirectory(gptRoot);
            RegularDirectory(machineRoot);
            RegularDirectory(scriptRoot);
            string gptIni = Path.Combine(gptRoot, "gpt.ini");
            string startup = Path.Combine(scriptRoot, "psscripts.ini");
            var gptDigest = DigestFile(gptIni, 1024 * 1024);
            var startupDigest = DigestFile(startup, 1024 * 1024);
            string gptText = File.ReadAllText(gptIni);
            string startupText = File.ReadAllText(startup, Encoding.Unicode);
            var versionMatch = Regex.Match(gptText, @"(?im)^\s*Version=(\d+)\s*$");
            if (!versionMatch.Success)
                throw new InvalidDataException("GptVersionMissing");
            if (!long.TryParse(versionMatch.Groups[1].Value,
                NumberStyles.None, CultureInfo.InvariantCulture, out long gptVersion))
                throw new InvalidDataException("InvalidGptVersion");
            var campaignPattern = Regex.Escape(campaignId);
            if (!Regex.IsMatch(startupText,
                    @"(?im)^0CmdLine=\\\\[^\r\n]+\\scripts\\LockScreenCampaigns\\" +
                    campaignPattern + @"\\LockScreen-Agent\.ps1\s*$") ||
                !Regex.IsMatch(startupText, @"(?im)^0Parameters=-Mode Install\s*$"))
                throw new InvalidDataException("GpoStartupTargetsOtherCampaign");
            long adVersion = ReadGpoVersion(ldap, baseDn, gpo);
            if (adVersion != gptVersion)
                throw new InvalidDataException("AdGptVersionMismatch");
            if (DigestFile(manifest, 1024 * 1024).Hash != manifestInfo.Hash)
                throw new InvalidDataException("CampaignChangedDuringScan");

            row.manifestHash = manifestInfo.Hash;
            row.agentHash = agentInfo.Hash;
            row.gptIniHash = gptDigest.Hash;
            row.psScriptsHash = startupDigest.Hash;
            row.adVersion = adVersion;
            row.gptVersion = gptVersion;
            row.images = images.OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase).ToList();
            row.status = "Readable";
            row.reason = "OK";
        }
        catch (Exception ex)
        {
            // UI and JSON must never contain private shares, LDAP DNs,
            // exception messages or passwords. Partial evidence is not trusted.
            row.status = ex is InvalidDataException &&
                InvalidReasons.Contains(ex.Message) ? "Invalid" : "Unavailable";
            row.reason = row.status == "Invalid" ? ex.Message : "ReadDeniedOrUnavailable";
            row.manifestHash = row.agentHash = row.gptIniHash = row.psScriptsHash = "";
            row.adVersion = row.gptVersion = -1;
            row.images.Clear();
        }
        return row;
    }

    private static bool EqualsHash(string expected, string actual) =>
        Regex.IsMatch(expected, @"\A[a-fA-F0-9]{64}\z") &&
        string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string username, string? domain,
        string password, int logonType, int provider, out SafeAccessTokenHandle token);
}
