using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// Native SYSVOL campaign inventory, preview and explicitly approved Cleanup.
/// Scan/Preview are read-only. Cleanup recomputes AD/SYSVOL evidence before
/// deleting only one fully verified expired, unreferenced campaign directory.
/// </summary>
internal static class NativeSysvolStorage
{
    // Windows LOGON_NETCREDENTIALS_ONLY (9): use alternate credentials
    // exclusively for outbound LDAP/SMB without changing local privileges.
    private const int LogonNetCredentialsOnly = 9;
    private const int LogonProviderWinNt50 = 3;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex CampaignIdPattern = new(
        @"\A[a-z0-9][a-z0-9_-]{0,70}\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ImageNamePattern = new(
        @"\A[0-9]{2}\.jpg\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex HashPattern = new(
        @"\A[0-9a-fA-F]{64}\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex CampaignReferencePattern = new(
        @"(?i)(?:\\|/)LockScreenCampaigns(?:\\|/)([a-z0-9_-]+)(?:\\|/|$)",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly TimeSpan AuditTimeout = TimeSpan.FromMinutes(4);

    internal sealed record Slide(string hash, long bytes, string file);
    internal sealed record Campaign(string id, string path, bool valid, string endUtc,
        int imageCount, long imageBytes, IReadOnlyList<Slide> slides,
        string fingerprint, string reason);
    internal sealed record Gpo(string id, string name, IReadOnlySet<string> campaignIds);
    private sealed record Scope(string domain, string dc, string baseDn, bool writable,
        DateTime dcUtc, int skewSeconds, string root, string policies);
    private sealed record Row(string campaignId, bool valid, string endUtc,
        int imageCount, long imageBytes, int duplicateImages, long duplicateBytes,
        string[] gpoReferences, int gpoCount, bool cleanupEligible, string reason);
    private sealed record Comparison(int imageCount, int duplicateImages,
        long duplicateBytes, long totalBytes, string note);
    private sealed record CleanupPlan(string campaignId, string path,
        string previewUtc, string planHash, int imageCount,
        long imageBytes, string note);

    internal static bool ValidCampaignId(string value) => CampaignIdPattern.IsMatch(value);

    internal static bool Eligible(bool valid, bool audited, DateTime endUtc,
        DateTime dcUtc, int gpoCount) =>
        valid && audited && gpoCount == 0 && endUtc.AddDays(30) <= dcUtc;

    /// <summary>
    /// Independently re-run by Cleanup: these checks provide a read-only
    /// preview, never a standalone authorization to delete SYSVOL files.
    /// </summary>
    internal static void ValidatePreviewConditions(bool writable, int skewSeconds,
        bool stagingPresent, DateTime endUtc, DateTime dcUtc, int gpoReferences)
    {
        if (!writable)
            throw new InvalidOperationException("Writable DC could not be verified.");
        if (Math.Abs((long)skewSeconds) > 300)
            throw new InvalidOperationException(
                "Computer clock differs from selected DC by over five minutes.");
        if (stagingPresent)
            throw new InvalidOperationException(
                "Concurrent or incomplete SYSVOL deployment staging is present.");
        if (endUtc.Kind != DateTimeKind.Utc || dcUtc.Kind != DateTimeKind.Utc ||
            endUtc.AddDays(30) > dcUtc)
            throw new InvalidOperationException(
                "Campaign has not passed its verified 30-day grace period.");
        if (gpoReferences != 0)
            throw new InvalidOperationException(
                "At least one GPO still references this campaign.");
    }

    internal static string CleanupPreviewHash(string domain, string selectedDc,
        string id, string fingerprint, string timestamp, int gpoCount) =>
        Sha(domain + "|" + selectedDc + "|" + id + "|" + fingerprint +
            "|" + timestamp + "|" + gpoCount.ToString(CultureInfo.InvariantCulture));

    internal static (int Count, long Bytes) DuplicateSlides(
        IReadOnlyList<Slide> slides, HashSet<string> known)
    {
        int count = 0;
        long bytes = 0;
        foreach (Slide slide in slides)
            if (known.Contains(slide.hash))
            {
                count++;
                bytes = checked(bytes + slide.bytes);
            }
        return (count, bytes);
    }

    internal static JsonElement Execute(string mode, object request,
        DomainCredentials credentials)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("SYSVOL requires Windows LDAP and SMB access.");
        if (mode is not ("Scan" or "Preview" or "Cleanup"))
            throw new ArgumentOutOfRangeException(nameof(mode),
                "Unsupported native SYSVOL operation.");
        var args = JsonSerializer.SerializeToElement(request, Json);
        string domain = Get(args, "domain").Trim().ToLowerInvariant();
        string dc = Get(args, "dc").Trim();
        NativeAdDiscovery.ValidateNames(domain, dc);
        if (dc.Length == 0) throw new ArgumentException("Select an explicit domain controller.");
        string packagePath = Get(args, "packagePath");

        JsonElement Run()
        {
            using LdapConnection ldap = NativeAdDiscovery.Connect(dc, credentials);
            ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            Scope scope = ReadScope(ldap, domain, dc);
            var timer = Stopwatch.StartNew();
            Campaign[] campaigns = ReadInventory(scope, timer);
            if (mode is "Preview" or "Cleanup")
            {
                // Preview and Cleanup fail closed on ANY incomplete inventory
                // or GPO reference audit. Cleanup refreshes source evidence
                // independently of the previously displayed Preview.
                VerifyDirectory(scope.root);
                bool staging = Directory.EnumerateDirectories(scope.root)
                    .Select(Path.GetFileName).Any(name => name is not null &&
                        (name.StartsWith(".staging-", StringComparison.OrdinalIgnoreCase) ||
                         name.StartsWith(".lscm-stage-", StringComparison.OrdinalIgnoreCase)));
                Gpo[] previewPolicies = ReadGpoReferences(ldap, scope, timer);
                string id = Get(args, "campaignId");
                if (!ValidCampaignId(id))
                    throw new InvalidDataException("Invalid campaign identifier for cleanup preview.");
                Campaign[] matching = campaigns.Where(x =>
                    x.id.Equals(id, StringComparison.Ordinal)).ToArray();
                if (matching.Length != 1 || !matching[0].valid)
                    throw new InvalidDataException("Campaign folder missing or not fully verified.");
                Campaign folder = matching[0];
                if (!string.Equals(Path.GetDirectoryName(folder.path), scope.root,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetFileName(folder.path), id, StringComparison.Ordinal))
                    throw new InvalidDataException("Campaign is outside the verified SYSVOL folder.");
                // The original inventory may have been read several seconds ago.
                // Re-read the exact campaign before authorizing a preview.
                Campaign verified = ReadVerifiedFolder(folder.path, id);
                if (!string.Equals(verified.fingerprint, folder.fingerprint,
                        StringComparison.Ordinal))
                    throw new InvalidDataException("SYSVOL campaign changed while auditing GPOs.");
                if (!DateTime.TryParse(verified.endUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out DateTime endUtc))
                    throw new InvalidDataException("Campaign UTC end time is invalid.");
                int references = previewPolicies.Count(g =>
                    g.campaignIds.Contains(id));
                ValidatePreviewConditions(scope.writable, scope.skewSeconds,
                    staging, endUtc, scope.dcUtc, references);
                string timestamp = mode == "Cleanup" ? Get(args, "previewUtc")
                    : scope.dcUtc.ToString("o", CultureInfo.InvariantCulture);
                if (mode == "Cleanup")
                {
                    if (!DateTimeOffset.TryParse(timestamp,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out var approvedUtc) ||
                        approvedUtc.UtcDateTime > scope.dcUtc.AddSeconds(3) ||
                        approvedUtc.UtcDateTime.AddMinutes(5) < scope.dcUtc)
                        throw new InvalidOperationException(
                            "SYSVOL cleanup preview expired or is invalid. Preview again.");
                }
                // IMPORTANT: This hash precisely matches legacy Guard-Plan.
                // Use the selected DC spelling (not LDAP's canonical DC FQDN),
                // because Cleanup recomputes the hash from the request's DC.
                string planHash = CleanupPreviewHash(scope.domain,
                    dc.ToLowerInvariant(), id, verified.fingerprint,
                    timestamp, previewPolicies.Length);
                var plan = new CleanupPlan(id, verified.path, timestamp,
                    planHash, verified.imageCount, verified.imageBytes,
                    "No GPO file reference found on selected DC. Check offline PCs, " +
                    "custom references and DFSR replication before approving deletion.");
                if (mode == "Cleanup")
                {
                    if (!string.Equals(Get(args, "approveId"), id,
                            StringComparison.Ordinal) ||
                        !string.Equals(Get(args, "planHash"), planHash,
                            StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "SYSVOL cleanup approval or source fingerprint mismatch.");
                    // One last fresh targeted directory and GPO reference
                    // verification before the destructive filesystem call.
                    Campaign now = ReadVerifiedFolder(verified.path, id);
                    if (!string.Equals(now.fingerprint, verified.fingerprint,
                            StringComparison.Ordinal) ||
                        ReadGpoReferences(ldap, scope, Stopwatch.StartNew()).Any(g =>
                            g.campaignIds.Contains(id)))
                        throw new InvalidOperationException(
                            "SYSVOL source or GPO references changed. Refresh Preview.");
                    VerifyDirectory(now.path);
                    Directory.Delete(now.path, recursive: true);
                    if (Directory.Exists(now.path))
                        throw new IOException("SYSVOL deletion could not be verified.");
                    return JsonSerializer.SerializeToElement(new
                    {
                        success = true,
                        message = "One unreferenced expired campaign folder deleted after explicit approval.",
                        data = new
                        {
                            campaignId = id, path = now.path, deleted = true,
                            note = "GPOs and client computers were not modified."
                        }
                    }, Json);
                }
                return JsonSerializer.SerializeToElement(new
                {
                    success = true,
                    message = "Native read-only SYSVOL cleanup preview complete; no files changed.",
                    data = plan
                }, Json);
            }
            bool gpoAuditComplete = true;
            Gpo[] policies;
            try
            {
                policies = ReadGpoReferences(ldap, scope, timer);
            }
            catch (Exception ex) when (ex is DirectoryException or IOException
                or InvalidDataException or UnauthorizedAccessException
                or System.Security.SecurityException or System.ComponentModel.Win32Exception
                or TimeoutException)
            {
                // A partial GPO scan is not evidence that a campaign is unused.
                policies = [];
                gpoAuditComplete = false;
            }
            var duplicateFirst = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Campaign folder in campaigns.Where(x => x.valid))
                foreach (Slide slide in folder.slides)
                    duplicateFirst.TryAdd(slide.hash, folder.id);

            Row[] rows = campaigns.Select(folder =>
            {
                int duplicates = 0;
                long duplicateBytes = 0;
                if (folder.valid)
                    foreach (Slide slide in folder.slides)
                        if (!duplicateFirst[slide.hash].Equals(folder.id, StringComparison.Ordinal))
                        {
                            duplicates++;
                            duplicateBytes = checked(duplicateBytes + slide.bytes);
                        }
                var gpoHits = policies.Where(g =>
                    g.campaignIds.Contains(folder.id)).ToArray();
                bool past = folder.valid && DateTime.TryParse(
                    folder.endUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTime until) &&
                    until.ToUniversalTime().AddDays(30) <= scope.dcUtc;
                bool eligible = folder.valid && gpoAuditComplete && past && gpoHits.Length == 0;
                string reason = !gpoAuditComplete
                    ? "GPO reference audit incomplete; cleanup disabled"
                    : !folder.valid
                    ? "Invalid files: manual inspection required"
                    : !past
                    ? "Campaign is active or within 30-day grace period"
                    : gpoHits.Length > 0
                    ? "GPO still references this campaign"
                    : "Review eligible campaign before cleanup";
                return new Row(folder.id, folder.valid, folder.endUtc,
                    folder.imageCount, folder.imageBytes, duplicates, duplicateBytes,
                    gpoHits.Select(x => x.name).ToArray(), gpoHits.Length,
                    eligible, reason);
            }).ToArray();

            object? comparison = null;
            if (!string.IsNullOrWhiteSpace(packagePath))
            {
                // A local staging package may contain additional metadata files.
                Campaign incoming = ReadVerifiedFolder(packagePath,
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(packagePath)), false);
                var hashes = duplicateFirst.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var duplicates = DuplicateSlides(incoming.slides, hashes);
                comparison = new Comparison(incoming.imageCount, duplicates.Count,
                    duplicates.Bytes, incoming.imageBytes,
                    "Identical JPEGs are independently copied; SYSVOL has no shared-file links.");
            }
            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                message = gpoAuditComplete
                    ? "Read-only native SYSVOL and domain GPO inventory complete."
                    : "SYSVOL inventory complete; GPO reference audit incomplete, cleanup disabled.",
                data = new
                {
                    domain = scope.domain, dc = scope.dc, root = scope.root,
                    writable = scope.writable, gpoAudited = policies.Length,
                    gpoAuditComplete, dcUtc = scope.dcUtc.ToString("o"),
                    clockSkewSeconds = scope.skewSeconds,
                    campaigns = rows, comparison
                }
            }, Json);
        }

        if (!credentials.IsAlternate) return Run();

        // Network-only impersonation ensures explicit alternate credentials
        // are also applied to SMB reads; LDAP uses the same supplied identity.
        string name = credentials.UserName.Trim();
        string? accountDomain = null;
        int slash = name.IndexOf('\\');
        if (slash > 0 && slash < name.Length - 1)
        {
            accountDomain = name[..slash];
            name = name[(slash + 1)..];
        }
        else if (!name.Contains('@'))
            name += "@" + credentials.Domain.Trim();
        if (credentials.Password.Length == 0 ||
            !LogonUser(name, accountDomain, credentials.Password,
                LogonNetCredentialsOnly, LogonProviderWinNt50,
                out SafeAccessTokenHandle token))
            throw new InvalidOperationException(
                "Could not establish alternate network credentials for SYSVOL inspection.");
        using (token)
            return WindowsIdentity.RunImpersonated(token, Run);
    }

    private static string Get(JsonElement args, string key) =>
        args.ValueKind == JsonValueKind.Object &&
        args.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static Scope ReadScope(LdapConnection ldap, string domain, string requestedDc)
    {
        var root = NativeAdDiscovery.ReadRootDse(ldap);
        string baseDn = NativeAdDiscovery.Attr(root, "defaultNamingContext");
        string actualDc = NativeAdDiscovery.Attr(root, "dnsHostName");
        string timeText = NativeAdDiscovery.Attr(root, "currentTime");
        if (!NativeAdDiscovery.DomainFromDn(baseDn).Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            actualDc.Length == 0 || timeText.Length == 0 ||
            (!actualDc.Equals(requestedDc, StringComparison.OrdinalIgnoreCase) &&
             !actualDc.Split('.')[0].Equals(requestedDc, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Selected DC identity or clock cannot be verified.");
        DateTime now = NativeDcConsistency.ParseDcTime(timeText);
        int skew = (int)Math.Round((DateTime.UtcNow - now).TotalSeconds);
        bool writable = NativeAdDiscovery.ReadDcWritable(ldap, root) == true;
        string sysvol = @"\\" + actualDc + @"\SYSVOL\" + domain;
        return new Scope(domain, actualDc, baseDn, writable, now, skew,
            Path.Combine(sysvol, "scripts", "LockScreenCampaigns"),
            Path.Combine(sysvol, "Policies"));
    }

    private static void VerifyDirectory(string path)
    {
        var flags = File.GetAttributes(path);
        if ((flags & FileAttributes.Directory) == 0 ||
            (flags & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("SYSVOL folder is not a regular directory.");
        // Read access failures are not mistaken for empty directories.
        using var iterator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
        _ = iterator.MoveNext();
    }

    private static bool RegularFile(string path)
    {
        FileAttributes flags;
        try { flags = File.GetAttributes(path); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        if ((flags & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("SYSVOL file is a reparse point or directory.");
        return true;
    }

    private static (string Hash, long Length) FileHash(string path, long min, long max)
    {
        if (!RegularFile(path)) throw new FileNotFoundException("Required campaign file is missing.");
        var info = new FileInfo(path);
        if (info.Length < min || info.Length > max)
            throw new InvalidDataException("SYSVOL file violates size limits.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        string hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        return (hash, info.Length);
    }

    private static Campaign ReadVerifiedFolder(string folder, string id, bool exactLayout = true)
    {
        if (!ValidCampaignId(id))
            throw new InvalidDataException("Unsafe campaign identifier.");
        VerifyDirectory(folder);
        string manifest = Path.Combine(folder, "campaign.json");
        string images = Path.Combine(folder, "images");
        VerifyDirectory(images);
        var info = FileHash(manifest, 10, 1_048_576);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(manifest),
            new JsonDocumentOptions { MaxDepth = 12 });
        var cfg = doc.RootElement;
        CampaignClientType clientType = NativeGpoDeploymentPlan.ParseClientType(
            cfg.TryGetProperty("clientType", out var declared) &&
            declared.ValueKind == JsonValueKind.String
                ? declared.GetString() ?? "" : "");
        string agentName = clientType == CampaignClientType.Executable
            ? "LockScreen-Agent.exe" : "LockScreen-Agent.ps1";
        string agent = Path.Combine(folder, agentName);
        var agentInfo = FileHash(agent, 1, clientType == CampaignClientType.Executable
            ? 250L * 1_048_576 : 5 * 1_048_576);
        if (cfg.GetProperty("campaignId").GetString() != id ||
            cfg.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Campaign ID or schema mismatch.");
        string tzId = cfg.GetProperty("timeZoneId").GetString() ?? "";
        TimeZoneInfo timezone = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        DateTime endLocal = DateTime.ParseExact(
            cfg.GetProperty("endLocal").GetString() ?? "",
            "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None);
        if (timezone.IsAmbiguousTime(endLocal) || timezone.IsInvalidTime(endLocal))
            throw new InvalidDataException("Campaign end time is ambiguous or invalid.");
        DateTime endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, timezone);
        if (cfg.TryGetProperty("agentSha256", out var hashElement) &&
            (!HashPattern.IsMatch(hashElement.GetString() ?? "") ||
             !agentInfo.Hash.Equals(hashElement.GetString(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Campaign agent SHA-256 mismatch.");
        var slides = cfg.GetProperty("slides");
        if (slides.ValueKind != JsonValueKind.Array ||
            slides.GetArrayLength() is < 1 or > 30)
            throw new InvalidDataException("Campaign must contain 1-30 slides.");
        string[] entries = Directory.GetFileSystemEntries(folder)
            .Select(Path.GetFileName).ToArray()!;
        if (exactLayout && (entries.Length != 3 ||
            entries.Any(x => x != "campaign.json" && x != agentName && x != "images")))
            throw new InvalidDataException("Unexpected files in campaign folder.");
        string[] imageFiles = Directory.GetFileSystemEntries(images)
            .Select(Path.GetFileName).ToArray()!;
        if (imageFiles.Length != slides.GetArrayLength())
            throw new InvalidDataException("Manifest and SYSVOL image counts differ.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new List<Slide>();
        long bytes = 0;
        foreach (JsonElement slide in slides.EnumerateArray())
        {
            string leaf = slide.GetProperty("fileName").GetString() ?? "";
            string expectedHash = slide.GetProperty("sha256").GetString() ?? "";
            if (!ImageNamePattern.IsMatch(leaf) || !HashPattern.IsMatch(expectedHash) ||
                !seen.Add(leaf))
                throw new InvalidDataException("Unsafe or duplicate image entry.");
            var image = FileHash(Path.Combine(images, leaf), 1, 30 * 1_048_576);
            if (!image.Hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Campaign image SHA-256 mismatch.");
            bytes = checked(bytes + image.Length);
            hashes.Add(new Slide(image.Hash, image.Length, leaf));
        }
        if (imageFiles.Any(x => !seen.Contains(x ?? "")))
            throw new InvalidDataException("Unexpected SYSVOL image.");
        string fingerprint = Sha(id + "|" + info.Hash + "|" + agentInfo.Hash + "|" +
            string.Join("|", hashes.Select(x =>
                x.file + ":" + x.hash + ":" +
                x.bytes.ToString(CultureInfo.InvariantCulture))));
        return new Campaign(id, folder, true, endUtc.ToString("o"),
            hashes.Count, bytes, hashes, fingerprint, "");
    }

    private static Campaign[] ReadInventory(Scope scope, Stopwatch timer)
    {
        string parent = Path.GetDirectoryName(scope.root) ??
            throw new InvalidDataException("SYSVOL root is invalid.");
        VerifyDirectory(parent);
        try { VerifyDirectory(scope.root); }
        catch (DirectoryNotFoundException) { return []; }
        catch (FileNotFoundException) { return []; }
        var dirs = Directory.EnumerateDirectories(scope.root).ToArray();
        if (dirs.Length > 500)
            throw new InvalidDataException("SYSVOL campaign directory count exceeds 500.");
        var list = new List<Campaign>();
        foreach (string directory in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (timer.Elapsed > AuditTimeout)
                throw new TimeoutException("SYSVOL campaign inventory timed out.");
            string id = Path.GetFileName(directory);
            if (id.StartsWith('.') || !ValidCampaignId(id)) continue;
            try { list.Add(ReadVerifiedFolder(directory, id)); }
            catch (Exception ex) when (ex is InvalidDataException or IOException
                or UnauthorizedAccessException or JsonException or ArgumentException
                or TimeZoneNotFoundException or InvalidTimeZoneException
                or FormatException or OverflowException)
            {
                // Invalid content is visible to the operator but is never
                // eligible for cleanup. Detailed paths are not exposed here.
                list.Add(new Campaign(id, directory, false, "", 0, 0,
                    [], "", "Invalid or unverified campaign files"));
            }
        }
        return list.ToArray();
    }

    private static Gpo[] ReadGpoReferences(LdapConnection ldap, Scope scope,
        Stopwatch timer)
    {
        string policiesDn = "CN=Policies,CN=System," + scope.baseDn;
        var policies = new List<Gpo>();
        byte[] cookie = [];
        do
        {
            if (timer.Elapsed > AuditTimeout)
                throw new TimeoutException("Full GPO inventory timed out.");
            var request = new SearchRequest(policiesDn, "(objectClass=groupPolicyContainer)",
                SearchScope.Subtree, "name", "displayName");
            request.Controls.Add(new PageResultRequestControl(250) { Cookie = cookie });
            var response = (SearchResponse)ldap.SendRequest(request);
            foreach (SearchResultEntry entry in response.Entries)
            {
                if (policies.Count >= 2500)
                    throw new InvalidDataException("Too many domain GPOs for reference audit.");
                string id = NativeAdDiscovery.Attr(entry, "name");
                if (!Regex.IsMatch(id, @"\A\{[0-9a-fA-F-]{36}\}\z") ||
                    !Guid.TryParse(id, out _))
                    throw new InvalidDataException("Invalid domain GPO GUID.");
                string folder = Path.Combine(scope.policies, id.ToUpperInvariant());
                VerifyDirectory(folder);
                var references = ScanPolicyFolder(folder, timer);
                policies.Add(new Gpo(id,
                    NativeAdDiscovery.Attr(entry, "displayName"),
                    references));
            }
            cookie = [];
            foreach (DirectoryControl control in response.Controls)
                if (control is PageResultResponseControl page)
                    cookie = page.Cookie ?? [];
        }
        while (cookie.Length > 0);
        return policies.ToArray();
    }

    private static HashSet<string> ScanPolicyFolder(string folder, Stopwatch timer)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(folder);
        int dirs = 0, files = 0;
        long totalBytes = 0;
        while (stack.Count > 0)
        {
            if (timer.Elapsed > AuditTimeout)
                throw new TimeoutException("GPO reference audit exceeded time limit.");
            string directory = stack.Pop();
            VerifyDirectory(directory);
            if (++dirs > 5000)
                throw new InvalidDataException("GPO directory tree exceeds audit bounds.");
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes flags = File.GetAttributes(path);
                if ((flags & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("GPO contains a reparse point.");
                if ((flags & FileAttributes.Directory) != 0)
                {
                    stack.Push(path);
                    continue;
                }
                if (++files > 5000)
                    throw new InvalidDataException("GPO file count exceeds audit bounds.");
                var info = new FileInfo(path);
                if (info.Length > 2_097_152)
                    throw new InvalidDataException("GPO policy file exceeds scan limit.");
                totalBytes = checked(totalBytes + info.Length);
                if (totalBytes > 100 * 1_048_576)
                    throw new InvalidDataException("GPO content volume exceeds scan limit.");
                if (info.Length == 0) continue;
                byte[] raw = File.ReadAllBytes(path);
                foreach (string campaignId in ExtractReferences(raw))
                    references.Add(campaignId);
            }
        }
        return references;
    }

    internal static IReadOnlySet<string> ExtractReferences(byte[] raw)
    {
        if (raw.Length > 2_097_152)
            throw new InvalidDataException("GPO policy file exceeds scan limit.");
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Encoding codec in new[]
        {
            Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF8
        })
        {
            string body = codec.GetString(raw);
            foreach (System.Text.RegularExpressions.Match match
                in CampaignReferencePattern.Matches(body))
                if (ValidCampaignId(match.Groups[1].Value))
                    references.Add(match.Groups[1].Value);
        }
        return references;
    }

    private static string Sha(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string username, string? domain,
        string password, int logonType, int provider, out SafeAccessTokenHandle token);
}
