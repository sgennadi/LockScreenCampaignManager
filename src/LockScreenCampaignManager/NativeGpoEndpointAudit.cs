using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// Native read-only Windows endpoint Registry64 / cached RSoP / SMB image hash
/// observation. Revalidates the chosen AD computer, OU, security group and
/// managed campaign through NativeGpoEffectiveAudit before contacting the
/// endpoint. No remote code, task, GP refresh or registry writes are allowed.
/// </summary>
internal static class NativeGpoEndpointAudit
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex Image = new(
        @"\A[0-9]{2}\.jpg\z", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex GuidInBraces = new(
        @"\{(?<id>[A-Fa-f0-9]{8}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{12})\}",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private sealed record Value(string key, string name, string status,
        string kind, string value);
    private sealed record RegistryObservation(string registryStatus,
        string osStatus, string osBuild, string observedUtc, Value[] values,
        string note);
    private sealed record RsopSetting(string key, string name, int precedence,
        string gpoId, bool deleted, int valueType);
    private sealed record RsopResult(string status, string observedUtc,
        RsopSetting[] settings, string note);
    private sealed record ImageResult(string status, string observedUtc,
        string fileName, string expectedSha256, string actualSha256,
        long bytes, string note);

    private const string Personalization =
        @"SOFTWARE\Policies\Microsoft\Windows\Personalization";
    private const string Cloud =
        @"SOFTWARE\Policies\Microsoft\Windows\CloudContent";
    private const string Csp =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";

    private static readonly (string Path, string[] Names)[] Allowed =
    [
        (Personalization, ["LockScreenImage", "NoChangingLockScreen", "NoLockScreen"]),
        (Cloud, ["RotatingLockScreenEnabled", "DisableWindowsSpotlightFeatures",
            "RotatingLockScreenOverlayEnabled", "ConfigureWindowsSpotlight",
            "DisableCloudOptimizedContent"]),
        (Csp, ["LockScreenImagePath", "LockScreenImageUrl", "LockScreenImageStatus"])
    ];

    internal static JsonElement Execute(string mode, object request,
        DomainCredentials credentials)
    {
        if (mode is not ("VerifyEndpoint" or "VerifyEvidence"))
            throw new ArgumentException("Only read-only endpoint verification is supported.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows native DCOM/SMB is required.");

        // Recompute the complete candidate audit under the same network
        // account. Its domain/DC/OU/computer/group/managed GPO checks must
        // succeed; a standalone hostname must NEVER authorize an RPC probe.
        JsonElement verified = NativeGpoEffectiveAudit.Execute("Effective",
            request, credentials).GetProperty("data");

        JsonElement Run()
        {
            string computer = Required(verified, "computer");
            string campaignId = Required(verified, "campaignId");
            string dc = Required(verified, "dc");
            string domain = Required(verified, "domain");

            RegistryObservation registry = ReadRegistry(computer);
            var response = new Dictionary<string, object?>
            {
                ["domain"] = domain,
                ["dc"] = dc,
                ["targetOu"] = Required(verified, "targetOu"),
                ["groupDn"] = Required(verified, "groupDn"),
                ["computer"] = computer,
                ["campaignId"] = campaignId,
                ["managedGpoId"] = Required(verified, "managedGpoId"),
                ["actualComputerContainer"] = Required(verified, "actualComputerContainer"),
                ["observedUtc"] = registry.observedUtc,
                ["registryStatus"] = registry.registryStatus,
                ["osStatus"] = registry.osStatus,
                ["osBuild"] = registry.osBuild,
                ["values"] = registry.values,
                ["note"] = registry.note
            };
            if (mode == "VerifyEvidence")
            {
                if (registry.osStatus != "Windows11")
                {
                    response["rsop"] = new RsopResult("Skipped",
                        DateTimeOffset.UtcNow.ToString("O"), [],
                        "Windows 11 status not established.");
                    response["image"] = NewImage("Skipped",
                        "Windows 11 status not established.");
                }
                else
                {
                    response["rsop"] = ReadRsop(computer);
                    response["image"] = ReadImage(dc, domain, computer,
                        campaignId, registry);
                }
            }

            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                message = "Native scoped read-only endpoint verification; cached RSoP and file hash do not prove physical display.",
                data = response
            }, Json);
        }

        // LOGON_NETCREDENTIALS_ONLY applies to remote native Registry, DCOM
        // and selected-DC/endpoint SMB as well as LDAP, never local writes.
        if (!credentials.IsAlternate) return Run();
        string user = credentials.UserName.Trim();
        string? domain = null;
        int slash = user.IndexOf('\\');
        if (slash > 0 && slash < user.Length - 1)
        {
            domain = user[..slash];
            user = user[(slash + 1)..];
        }
        else if (!user.Contains('@'))
            user += "@" + credentials.Domain.Trim();
        if (credentials.Password.Length == 0 ||
            !LogonUser(user, domain, credentials.Password, 9, 3,
                out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot use alternate network identity for read-only endpoint verification.");
        using (token) return WindowsIdentity.RunImpersonated(token, Run);
    }

    private static RegistryObservation ReadRegistry(string computer)
    {
        string observed = DateTimeOffset.UtcNow.ToString("O");
        RegistryKey? root;
        try
        {
            root = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine,
                computer, RegistryView.Registry64);
        }
        catch (Exception)
        {
            return new RegistryObservation("Unavailable", "Unknown", "",
                observed, [],
                "Remote Registry unavailable or access denied; missing values were not inferred.");
        }

        using (root)
        {
            bool partial = false;
            string os = "Unknown", build = "";
            try
            {
                using var key = root.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
                if (key is not null)
                {
                    string observedBuild = Convert.ToString(
                        key.GetValue("CurrentBuildNumber", "",
                            RegistryValueOptions.DoNotExpandEnvironmentNames),
                        CultureInfo.InvariantCulture) ?? "";
                    string type = Convert.ToString(
                        key.GetValue("InstallationType", "",
                            RegistryValueOptions.DoNotExpandEnvironmentNames),
                        CultureInfo.InvariantCulture) ?? "";
                    if (observedBuild.Length is >= 4 and <= 6 &&
                        observedBuild.All(char.IsAsciiDigit) &&
                        int.TryParse(observedBuild, out int buildNumber) &&
                        type.Length > 0)
                    {
                        build = observedBuild;
                        os = buildNumber >= 22000 && type == "Client"
                            ? "Windows11" : "OtherWindows";
                    }
                }
            }
            catch (Exception) { os = "Unknown"; build = ""; partial = true; }

            var records = new List<Value>();
            foreach (var spec in Allowed)
            {
                RegistryKey? subkey = null;
                string state = "Readable";
                HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
                try
                {
                    subkey = root.OpenSubKey(spec.Path, writable: false);
                    if (subkey is null) state = "MissingKey";
                    else names.UnionWith(subkey.GetValueNames());
                }
                catch (Exception) { partial = true; state = "Unreadable"; }

                try
                {
                    foreach (string name in spec.Names)
                    {
                        string kind = "Unknown";
                        string status = "Absent";
                        string value = "";
                        if (state == "Unreadable")
                            status = "Unreadable";
                        else if (state == "Readable" && names.Contains(name))
                        {
                            try
                            {
                                RegistryValueKind valueKind = subkey!.GetValueKind(name);
                                kind = valueKind.ToString();
                                if (valueKind is RegistryValueKind.String or
                                    RegistryValueKind.ExpandString or
                                    RegistryValueKind.DWord or RegistryValueKind.QWord)
                                {
                                    object? raw = subkey.GetValue(name, null,
                                        RegistryValueOptions.DoNotExpandEnvironmentNames);
                                    if (raw is null)
                                    {
                                        kind = "Unknown";
                                        status = "Unreadable";
                                    }
                                    else
                                    {
                                        value = Convert.ToString(raw,
                                            CultureInfo.InvariantCulture) ?? "";
                                        if (value.Length > 1024)
                                        {
                                            kind = "Unknown";
                                            status = "Oversized";
                                            value = "";
                                        }
                                        else status = "Present";
                                    }
                                }
                                else
                                {
                                    kind = "Other";
                                    status = "Unsupported";
                                }
                            }
                            catch (Exception)
                            {
                                kind = "Unknown";
                                status = "Unreadable";
                            }
                        }
                        if (status is "Unreadable" or "Oversized") partial = true;
                        records.Add(new Value(spec.Path, name, status, kind, value));
                    }
                }
                finally { subkey?.Dispose(); }
            }

            return new RegistryObservation(partial ? "Partial" : "Readable",
                os, build, DateTimeOffset.UtcNow.ToString("O"),
                records.ToArray(),
                "Read-only native Registry64 HKLM observation. No verified GPO origin or physical lock-screen claim.");
        }
    }

    private static RsopResult ReadRsop(string computer)
    {
        var observed = DateTimeOffset.UtcNow.ToString("O");
        object? locator = null, service = null, result = null;
        var settings = new List<RsopSetting>();
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator")
                ?? throw new COMException("WMI DCOM locator unavailable.");
            locator = Activator.CreateInstance(type)
                ?? throw new COMException("Cannot instantiate WMI locator.");
            dynamic l = locator;
            service = l.ConnectServer(computer, @"root\RSOP\Computer");
            dynamic wmi = service;
            const string filter =
                "valueName='LockScreenImage' OR valueName='NoLockScreen' OR " +
                "valueName='NoChangingLockScreen' OR " +
                "valueName='RotatingLockScreenEnabled' OR " +
                "valueName='DisableWindowsSpotlightFeatures' OR " +
                "valueName='RotatingLockScreenOverlayEnabled' OR " +
                "valueName='ConfigureWindowsSpotlight' OR " +
                "valueName='DisableCloudOptimizedContent'";
            result = wmi.ExecQuery(
                "SELECT * FROM RSOP_RegistryPolicySetting WHERE " + filter);
            if (result is not IEnumerable enumerable)
                throw new COMException("Remote WMI result cannot be enumerated.");
            int seen = 0;
            foreach (object item in enumerable)
            {
                if (++seen > 150) throw new InvalidDataException("RSoP record limit.");
                try
                {
                    dynamic record = item;
                    dynamic props = record.Properties_;
                    string name = Convert.ToString(props.Item("valueName").Value) ?? "";
                    string key = Convert.ToString(props.Item("registryKey").Value) ?? "";
                    key = EndpointLockScreenObservation.NormalizeKey(key);
                    bool personal = key == EndpointLockScreenObservation.NormalizeKey(Personalization);
                    bool cloud = key == EndpointLockScreenObservation.NormalizeKey(Cloud);
                    if ((!personal && !cloud) ||
                        !(personal ? Allowed[0].Names : Allowed[1].Names)
                            .Contains(name, StringComparer.OrdinalIgnoreCase))
                        continue;
                    int precedence = Convert.ToInt32(props.Item("precedence").Value,
                        CultureInfo.InvariantCulture);
                    int valueType = Convert.ToInt32(props.Item("valueType").Value,
                        CultureInfo.InvariantCulture);
                    bool deleted = Convert.ToBoolean(props.Item("deleted").Value,
                        CultureInfo.InvariantCulture);
                    if (precedence is < 1 or > 10000 || valueType is < 0 or > 255)
                        throw new InvalidDataException("Unsafe RSoP precedence/type.");
                    string rawId = Convert.ToString(props.Item("GPOID").Value) ?? "";
                    var idMatch = GuidInBraces.Match(rawId);
                    string gpo = idMatch.Success &&
                        Guid.TryParse(idMatch.Groups["id"].Value, out Guid guid)
                            ? guid.ToString("D") : "";
                    settings.Add(new RsopSetting(key, name, precedence, gpo,
                        deleted, valueType));
                    if (settings.Count > 96)
                        throw new InvalidDataException("Too many relevant RSoP records.");
                }
                finally { Release(item); }
            }
            return new RsopResult(settings.Count > 0 ? "Available" : "Empty",
                observed, settings.ToArray(),
                "Cached computer Registry Policy CSE precedence; query time is not policy refresh time.");
        }
        catch (Exception)
        {
            return new RsopResult("Unavailable", observed, [],
                "Remote WMI RSoP was unavailable/incomplete; no winning GPO was inferred.");
        }
        finally
        {
            Release(result);
            Release(service);
            Release(locator);
        }
    }

    private static ImageResult ReadImage(string dc, string domain,
        string computer, string campaignId, RegistryObservation observed)
    {
        if (observed.registryStatus == "Unavailable")
            return NewImage("RegistryUnavailable",
                "Cannot confirm a client image while remote Registry is unavailable.");

        Value? image = observed.values.SingleOrDefault(x =>
            x.key == Personalization && x.name == "LockScreenImage");
        if (image is null || image.status != "Present")
            return NewImage("NoObservedImagePath",
                "No managed LockScreenImage path was observed.");

        string prefix = @"C:\ProgramData\LockScreenCampaigns\" +
            campaignId + @"\images\";
        if (!image.value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return NewImage("OtherOrUnsupportedPath",
                "Registry path is outside the expected managed C: campaign images.");
        string fileName = image.value[prefix.Length..];
        if (!Image.IsMatch(fileName))
            return NewImage("UnsafeImagePath",
                "Unexpected image file name in observed registry path.");

        string expected = "";
        try
        {
            string manifest = Path.Combine(@"\\", dc, "SYSVOL", domain,
                "scripts", "LockScreenCampaigns", campaignId, "campaign.json");
            var info = new FileInfo(manifest);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                info.Length is < 10 or > 2_097_152)
                throw new InvalidDataException("Unsafe campaign manifest.");
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifest),
                new JsonDocumentOptions { MaxDepth = 12 });
            if (Get(document.RootElement, "campaignId") != campaignId)
                throw new InvalidDataException("Unexpected campaign manifest.");
            var slides = document.RootElement.GetProperty("slides").EnumerateArray()
                .Where(x => Get(x, "fileName") == fileName).ToArray();
            if (slides.Length != 1)
                throw new InvalidDataException("Image missing/ambiguous in manifest.");
            expected = Get(slides[0], "sha256").ToLowerInvariant();
            if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid expected JPEG hash.");
        }
        catch (Exception)
        {
            return NewImage("ManifestUnavailable",
                "Selected DC manifest was inaccessible or invalid.");
        }

        var path = @"\\" + computer + @"\C$";
        try
        {
            foreach (string segment in new[]
            {
                "ProgramData", "LockScreenCampaigns", campaignId, "images", fileName
            })
            {
                path = Path.Combine(path, segment);
                FileAttributes attr = File.GetAttributes(path);
                if ((attr & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse point on remote image path.");
            }
            var imageFile = new FileInfo(path);
            if ((imageFile.Attributes & FileAttributes.Directory) != 0 ||
                imageFile.Length is < 1 or > 33_554_432)
                throw new InvalidDataException("Unbounded remote campaign image.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.SequentialScan);
            string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return new ImageResult(actual == expected ? "HashMatches" : "HashDiffers",
                DateTimeOffset.UtcNow.ToString("O"),
                fileName, expected, actual, imageFile.Length,
                "Read-only SMB hash comparison; no proof of physical display.");
        }
        catch (Exception)
        {
            return new ImageResult("FileUnavailable",
                DateTimeOffset.UtcNow.ToString("O"),
                fileName, expected, "", 0,
                "Remote JPEG could not be read/verified; unsafe path or SMB error.");
        }
    }

    private static ImageResult NewImage(string status, string note) =>
        new(status, DateTimeOffset.UtcNow.ToString("O"), "", "", "", 0, note);

    private static string Get(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string Required(JsonElement element, string property) =>
        Get(element, property) is { Length: > 0 } value ? value :
            throw new InvalidDataException("Missing verified endpoint scope: " + property);

    private static void Release(object? item)
    {
        if (item is not null && Marshal.IsComObject(item))
        {
            try { _ = Marshal.FinalReleaseComObject(item); }
            catch (Exception) { /* Close is best-effort for read-only RPC. */ }
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string user, string? domain,
        string password, int logonType, int provider, out SafeAccessTokenHandle token);
}
