using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// Native, strictly read-only GPO Deployment Plan. No RSAT, PowerShell,
/// directory mutation, SYSVOL writes or client RPC. The legacy GPO Deploy
/// engine repeats ALL package, OU, group, duplicate-name and SYSVOL checks
/// before creating a new policy. A Plan is not an authorization token.
/// </summary>
internal static class NativeGpoDeploymentPlan
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex ImageName = new(@"^[0-9]{2}\.jpg$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Digest = new(@"^[0-9a-fA-F]{64}$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private sealed record VerifiedPackage(string Id, int Count, long Bytes,
        CampaignClientType ClientType);
    internal static JsonElement Execute(string mode, object request, DomainCredentials credentials)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native AD/SYSVOL preflight needs Windows.");
        if (mode != "Plan")
            throw new ArgumentException("Only read-only GPO Deployment Plan is native.");
        var args = JsonSerializer.SerializeToElement(request, Json);
        string domain = String(args, "domain").Trim().ToLowerInvariant();
        string dc = String(args, "dc").Trim();
        string ouDn = String(args, "ouDn").Trim();
        string groupDn = String(args, "groupDn").Trim();
        string gpoName = String(args, "gpoName").Trim();
        string localPackage = String(args, "packagePath").Trim();
        CampaignClientType requestedClient = ParseClientType(String(args, "clientType"));
        CampaignContentSource contentSource = CampaignPayloadLocation.Parse(
            String(args, "contentSource"));
        string smbRoot = contentSource == CampaignContentSource.SmbShare
            ? CampaignPayloadLocation.ValidateRoot(String(args, "contentShareRoot"))
            : string.Empty;
        NativeAdDiscovery.ValidateNames(domain, dc);
        if (dc.Length == 0)
            throw new ArgumentException("Select an explicit writable domain controller.");
        if (gpoName.Length is < 3 or > 120 || gpoName.Any(char.IsControl))
            throw new ArgumentException("GPO name must contain 3-120 printable characters.");
        if (ouDn.Length is < 8 or > 2048 || groupDn.Length is < 8 or > 2048 ||
            ouDn.Any(char.IsControl) || groupDn.Any(char.IsControl))
            throw new ArgumentException("Selected OU/group is invalid.");
        if (localPackage.Length is < 3 or > 2048 ||
            !Path.IsPathFullyQualified(localPackage))
            throw new ArgumentException("Choose an exported local campaign package folder.");

        JsonElement Run()
        {
            using var ldap = NativeAdDiscovery.Connect(dc, credentials);
            ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            var root = NativeAdDiscovery.ReadRootDse(ldap);
            string baseDn = Required(root, "defaultNamingContext");
            string actualDc = Required(root, "dnsHostName");
            string actualDomain = NativeGpoLifecyclePreview.DomainFromDnPreserveCase(baseDn);
            if (!actualDomain.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                !NativeGpoLifecycleList.SameController(dc, actualDc))
                throw new InvalidOperationException(
                    "Selected AD domain and domain controller cannot be verified.");
            if (NativeAdDiscovery.ReadDcWritable(ldap, root) != true)
                throw new InvalidOperationException("A writable domain controller is required.");
            DateTime dcUtc = NativeDcConsistency.ParseDcTime(Required(root, "currentTime"));
            int skew = (int)Math.Round((DateTime.UtcNow - dcUtc).TotalSeconds);
            if (Math.Abs((long)skew) > 300)
                throw new InvalidOperationException("Windows clock differs from selected DC by more than 5 minutes.");

            if (!ouDn.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
                !groupDn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ||
                !NativeGpoConflictScan.Inside(ouDn, baseDn) ||
                !NativeGpoConflictScan.Inside(groupDn, baseDn))
                throw new InvalidOperationException(
                    "Selected OU/group must belong to the selected AD domain.");
            _ = ReadOne(ldap, ouDn, "(objectClass=organizationalUnit)", "distinguishedName");
            var group = ReadOne(ldap, groupDn, "(objectClass=group)",
                "groupType", "objectSid", "sAMAccountName", "member");
            if (!long.TryParse(Required(group, "groupType"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out long groupType) ||
                (groupType & 0x80000000L) == 0)
                throw new InvalidOperationException("Selected AD group is not security-enabled.");
            if (group.Attributes["objectSid"] is not { Count: > 0 } sid ||
                sid[0] is not byte[] sidBytes || sidBytes.Length < 8)
                throw new InvalidDataException("Security group SID is unavailable.");
            string groupSam = Required(group, "sAMAccountName");
            int memberCount = CountDirectMembers(group);

            VerifiedPackage package = ValidatePackage(localPackage);
            if (package.ClientType != requestedClient)
                throw new InvalidDataException("Selected client does not match the immutable campaign package.");
            string dfsSource = @"\\" + domain + @"\SYSVOL\" + domain +
                @"\scripts\LockScreenCampaigns\" + package.Id;
            string dcCampaign = @"\\" + actualDc + @"\SYSVOL\" + domain +
                @"\scripts\LockScreenCampaigns\" + package.Id;
            if (contentSource == CampaignContentSource.SmbShare)
            {
                // No SYSVOL payload in SMB mode: the GPO GPT stays in SYSVOL.
                // Inspect the selected dedicated SMB root without creating files.
                CampaignPayloadLocation.CheckShareReadOnly(smbRoot);
                dfsSource = CampaignPayloadLocation.TargetPath(smbRoot, package.Id);
                dcCampaign = dfsSource;
            }
            else
            {
                string scriptsRoot = @"\\" + actualDc + @"\SYSVOL\" + domain + @"\scripts";
                VerifyDirectory(scriptsRoot);
            }
            string dfsScript = Path.Combine(dfsSource,
                requestedClient == CampaignClientType.Executable
                    ? "LockScreen-Agent.exe" : "LockScreen-Agent.ps1");
            if (dfsScript.Length >= 260)
                throw new InvalidOperationException("Startup script UNC path exceeds policy limits.");
            if (ExistsStrict(dcCampaign))
                throw new InvalidOperationException("Campaign payload already exists; never overwrite it.");

            var existing = NativeGpoLifecycleList.SearchPaged(ldap,
                "CN=Policies,CN=System," + baseDn,
                "(objectClass=groupPolicyContainer)", 2500,
                Stopwatch.StartNew(), "displayName");
            if (existing.Any(g => NativeAdDiscovery.Attr(g, "displayName")
                    .Equals(gpoName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A GPO already exists with this display name.");

            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                message = "Native read-only preflight complete. No AD or SYSVOL changes made.",
                data = new
                {
                    domain, dc = actualDc, ou = ouDn, group = groupDn,
                    groupSam, directGroupMembers = memberCount,
                    gpoName, campaignId = package.Id,
                    imageCount = package.Count, imageBytes = package.Bytes,
                    // sysvolOnDc is retained for the original SYSVOL path only.
                    sysvolOnDc = contentSource == CampaignContentSource.Sysvol ? dcCampaign : "",
                    packageStoragePath = dcCampaign,
                    contentSource = contentSource.ToString(),
                    contentShareRoot = smbRoot,
                    clientScriptPath = dfsScript,
                    clientType = requestedClient.ToString(),
                    gpoMethod = requestedClient == CampaignClientType.Executable
                        ? "ClassicStartupExe" : "PowerShellStartup",
                    operation = "Plan",
                    rsatRequired = false, nativeEngine = "Windows ADSI + gpedit.dll COM",
                    dcUtc = dcUtc.ToString("o", CultureInfo.InvariantCulture),
                    clockSkewSeconds = skew
                }
            }, Json);
        }

        if (!credentials.IsAlternate) return Run();
        // Explicit alternate credentials are applied consistently to both
        // signed/sealed LDAP and selected-DC SMB reads, without local elevation.
        string user = credentials.UserName.Trim();
        string? accountDomain = null;
        int slash = user.IndexOf('\\');
        if (slash > 0 && slash < user.Length - 1)
        {
            accountDomain = user[..slash];
            user = user[(slash + 1)..];
        }
        else if (!user.Contains('@'))
            user += "@" + credentials.Domain.Trim();
        if (credentials.Password.Length == 0 ||
            !LogonUser(user, accountDomain, credentials.Password, 9, 3,
                out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot establish alternate network identity for read-only GPO preflight.");
        using (token)
            return WindowsIdentity.RunImpersonated(token, Run);
    }

    internal static CampaignClientType ParseClientType(string value) => value switch
    {
        "" or "PowerShell" => CampaignClientType.PowerShell,
        "Executable" => CampaignClientType.Executable,
        _ => throw new InvalidDataException("Unsupported clientType in deployment package.")
    };

    private static string String(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object &&
        json.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string Required(SearchResultEntry entry, string attr)
    {
        string text = NativeAdDiscovery.Attr(entry, attr);
        return text.Length > 0 ? text : throw new InvalidDataException(
            "Selected DC did not return required AD attribute: " + attr);
    }

    private static SearchResultEntry ReadOne(LdapConnection ldap, string dn,
        string filter, params string[] properties)
    {
        var response = (SearchResponse)ldap.SendRequest(
            new SearchRequest(dn, filter, SearchScope.Base, properties));
        if (response.Entries.Count != 1)
            throw new InvalidOperationException("Selected AD object could not be uniquely verified.");
        return response.Entries[0];
    }

    internal static int CountDirectMembers(SearchResultEntry entry)
    {
        foreach (string name in entry.Attributes.AttributeNames)
            if (name.StartsWith("member;range=", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "AD returned a ranged group member list; cannot claim exact member count.");
        return entry.Attributes.Contains("member") ?
            entry.Attributes["member"]?.Count ?? 0 : 0;
    }

    private static void VerifyDirectory(string directory)
    {
        var flags = File.GetAttributes(directory);
        if ((flags & FileAttributes.Directory) == 0 ||
            (flags & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Deployment folder is missing or unsafe.");
        using var entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
        _ = entries.MoveNext();
    }

    private static bool ExistsStrict(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        // Access denied, invalid paths and SMB errors propagate; none of them
        // can count as proof that a name is available in SYSVOL.
    }

    private static (string Hash, long Bytes) VerifyFile(string path, long max)
    {
        var flags = File.GetAttributes(path);
        if ((flags & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("Campaign file is a directory or link.");
        long bytes = new FileInfo(path).Length;
        if (bytes < 1 || bytes > max)
            throw new InvalidDataException("Campaign file size is unsafe.");
        using var stream = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.Read);
        string hash = Convert.ToHexString(SHA256.HashData(stream));
        if (stream.Length != bytes)
            throw new InvalidDataException("Campaign file changed during verification.");
        return (hash, bytes);
    }

    private static VerifiedPackage ValidatePackage(string packagePath)
    {
        VerifyDirectory(packagePath);
        VerifyDirectory(Path.Combine(packagePath, "images"));
        string jsonPath = Path.Combine(packagePath, "campaign.json");
        _ = VerifyFile(jsonPath, 1_048_576);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(jsonPath),
            new JsonDocumentOptions { MaxDepth = 12 });
        var cfg = doc.RootElement;
        CampaignClientType clientType = ParseClientType(String(cfg, "clientType"));
        string agent = Path.Combine(packagePath, clientType == CampaignClientType.Executable
            ? "LockScreen-Agent.exe" : "LockScreen-Agent.ps1");
        var agentInfo = VerifyFile(agent, clientType == CampaignClientType.Executable
            ? 250L * 1_048_576 : 5 * 1_048_576);
        string id = String(cfg, "campaignId");
        if (!NativeSysvolStorage.ValidCampaignId(id) ||
            cfg.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Package campaign ID or schema is invalid.");
        if (cfg.TryGetProperty("agentSha256", out var expected) &&
            (expected.ValueKind != JsonValueKind.String ||
             !Digest.IsMatch(expected.GetString() ?? "") ||
             !agentInfo.Hash.Equals(expected.GetString(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("SYSTEM agent SHA-256 differs from campaign manifest.");
        if (cfg.TryGetProperty("randomizePerDevice", out var randomized) &&
            randomized.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Campaign shuffle flag is invalid.");
        string zoneName = String(cfg, "timeZoneId");
        if (zoneName.Length == 0)
            throw new InvalidDataException("Campaign timezone is missing.");
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(zoneName);
        DateTime end = ParseLocal(String(cfg, "endLocal"));
        if (zone.IsInvalidTime(end) || zone.IsAmbiguousTime(end))
            throw new InvalidDataException("Campaign end time is ambiguous.");
        if (!cfg.TryGetProperty("slides", out var slides) ||
            slides.ValueKind != JsonValueKind.Array ||
            slides.GetArrayLength() is < 1 or > 30)
            throw new InvalidDataException("Campaign requires 1-30 valid images.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DateTime last = DateTime.MinValue;
        long imageBytes = 0;
        foreach (JsonElement slide in slides.EnumerateArray())
        {
            string leaf = String(slide, "fileName");
            if (!ImageName.IsMatch(leaf) || !seen.Add(leaf))
                throw new InvalidDataException("Unsafe or repeated image filename.");
            DateTime at = ParseLocal(String(slide, "atLocal"));
            if (at <= last || at >= end ||
                zone.IsInvalidTime(at) || zone.IsAmbiguousTime(at))
                throw new InvalidDataException("Campaign dates overlap or exceed end.");
            last = at;
            string digest = String(slide, "sha256");
            if (!Digest.IsMatch(digest))
                throw new InvalidDataException("Campaign image hash is invalid.");
            var file = VerifyFile(Path.Combine(packagePath, "images", leaf),
                30 * 1_048_576);
            if (!file.Hash.Equals(digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Campaign image SHA-256 differs from manifest.");
            imageBytes = checked(imageBytes + file.Bytes);
        }
        return new VerifiedPackage(id, slides.GetArrayLength(), imageBytes, clientType);
    }

    // Offline CI can verify the same package validation without LDAP,
    // SYSVOL access or allowing any GPO mutation.
    internal static (string Id, int Count, long Bytes) VerifyLocalPackageForTest(string path)
    {
        var found = ValidatePackage(path);
        return (found.Id, found.Count, found.Bytes);
    }

    internal static DateTime ParseLocal(string text) =>
        DateTime.ParseExact(text, "yyyy-MM-ddTHH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string username, string? domain,
        string password, int logonType, int provider, out SafeAccessTokenHandle token);
}
