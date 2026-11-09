using System.ComponentModel;
using System.DirectoryServices.Protocols;
using System.Diagnostics;
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
/// Separate USER-scoped GPO for an explicitly chosen user OU + security group.
/// Does not enable loopback, does not modify computer GPOs, and never starts a
/// screen saver on a remote host. Registry.pol is authored per MS-GPREG.
/// </summary>
internal static class NativeUserScreenSaverGpo
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex ZeroVersion = new(
        @"(?m)^Version=0\s*$", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private const string UserExtension =
        "[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{0F6B957E-509E-11D1-A7CC-0000F87571E3}]";
    private const string PolicyKey =
        @"Software\Policies\Microsoft\Windows\Control Panel\Desktop";

    internal static JsonElement Execute(string mode, object request,
        DomainCredentials credentials)
    {
        if (mode is not ("Plan" or "Deploy"))
            throw new ArgumentException("Unsupported Screen Saver GPO operation.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows LDAP/GPO COM/SMB required.");
        var args = JsonSerializer.SerializeToElement(request, Json);
        string domain = Required(args, "domain").Trim().ToLowerInvariant();
        string dc = Required(args, "dc").Trim();
        string userOu = Required(args, "userOuDn").Trim();
        string userGroup = Required(args, "userGroupDn").Trim();
        string name = Required(args, "gpoName").Trim();
        string saver = Required(args, "screenSaverExe").Trim();
        int seconds = Int(args, "idleSeconds");
        bool secure = Boolean(args, "passwordOnResume");
        NativeAdDiscovery.ValidateNames(domain, dc);
        if (dc.Length == 0 || name.Length is < 3 or > 120 ||
            name.Any(char.IsControl) || userOu.Length is < 8 or > 2048 ||
            userGroup.Length is < 8 or > 2048 ||
            !userOu.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
            !userGroup.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ||
            userOu.Any(char.IsControl) || userGroup.Any(char.IsControl))
            throw new ArgumentException("Invalid Screen Saver GPO name or user OU/group.");
        if (seconds is < 60 or > 86400 ||
            !CampaignTargetAssignments.BuiltInScreenSavers.Contains(saver,
                StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid built-in Screen Saver or idle time.");

        JsonElement Run()
        {
            using var ldap = NativeAdDiscovery.Connect(dc,
                new DomainCredentials(domain, "", ""));
            ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            var root = NativeAdDiscovery.ReadRootDse(ldap);
            string baseDn = Required(root, "defaultNamingContext");
            string actualDc = Required(root, "dnsHostName");
            if (!NativeAdDiscovery.DomainFromDn(baseDn).Equals(domain,
                    StringComparison.OrdinalIgnoreCase) ||
                !NativeGpoLifecycleList.SameController(dc, actualDc))
                throw new InvalidOperationException(
                    "Screen Saver DC does not match the selected domain.");
            if (NativeAdDiscovery.ReadDcWritable(ldap, root) != true)
                throw new InvalidOperationException("A verified writable DC is required.");
            DateTime dcUtc = NativeDcConsistency.ParseDcTime(Required(root, "currentTime"));
            if (Math.Abs((DateTime.UtcNow - dcUtc).TotalSeconds) > 300)
                throw new InvalidOperationException(
                    "Screen Saver deployment blocked: clock skew exceeds 5 minutes.");
            if (!NativeGpoConflictScan.Inside(userOu, baseDn) ||
                !NativeGpoConflictScan.Inside(userGroup, baseDn))
                throw new InvalidOperationException(
                    "Screen Saver OU/group must belong to the selected domain.");

            _ = One(ldap, userOu, "(objectClass=organizationalUnit)", "distinguishedName");
            var group = One(ldap, userGroup, "(objectClass=group)",
                "groupType", "objectSid", "sAMAccountName", "member");
            if (!long.TryParse(Required(group, "groupType"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var groupType) ||
                (groupType & 0x80000000L) == 0)
                throw new InvalidOperationException(
                    "Screen Saver user group must be a security-enabled AD group.");
            if (group.Attributes["objectSid"] is not { Count: > 0 } sid ||
                sid[0] is not byte[] binarySid || binarySid.Length < 8)
                throw new InvalidDataException("Screen Saver group SID unavailable.");
            var selectedSid = new SecurityIdentifier(binarySid, 0);
            string groupSam = Required(group, "sAMAccountName");

            // Never overwrite or edit a different GPO with the chosen name.
            var existing = NativeGpoLifecycleList.SearchPaged(ldap,
                "CN=Policies,CN=System," + baseDn,
                "(objectClass=groupPolicyContainer)", 2500,
                Stopwatch.StartNew(), "displayName");
            if (existing.Any(x => NativeAdDiscovery.Attr(x, "displayName")
                .Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Screen Saver GPO name already exists.");

            string memberHash = Hash(string.Join("|",
                group.Attributes["member"] is { Count: > 0 } members
                    ? members.Cast<object>()
                        .Select(x => Convert.ToString(x, CultureInfo.InvariantCulture) ?? "")
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    : Array.Empty<string>()));
            int memberCount = group.Attributes["member"]?.Count ?? 0;
            string proof = Hash(string.Join("|", domain, actualDc.ToLowerInvariant(),
                userOu.ToLowerInvariant(), userGroup.ToLowerInvariant(),
                selectedSid.Value, memberHash, name.ToLowerInvariant(),
                saver.ToLowerInvariant(), seconds, secure));
            var preview = new
            {
                domain, dc = actualDc, userOuDn = userOu, userGroupDn = userGroup,
                groupSam, directGroupMembers = memberCount, gpoName = name,
                targetMode = "ScreenSaver", scope = "User Configuration (HKCU)",
                groupScope = "User security group; computer accounts do not receive this policy",
                screenSaverExe = saver, idleSeconds = seconds,
                passwordOnResume = secure, planHash = proof,
                dcUtc = dcUtc.ToString("O"), gpoMethod = "UserRegistryPolicy",
                registryKey = PolicyKey, rsatRequired = false
            };
            if (mode == "Plan")
                return Reply(true,
                    "Read-only user OU/group Screen Saver preflight completed.", preview);

            if (Required(args, "approvedPlanHash") != proof)
                throw new InvalidOperationException(
                    "Screen Saver group, settings or DC changed since Preview. Repeat Preview.");

            Guid? created = null;
            bool linked = false;
            string gpoSysvol = "";
            try
            {
                created = NativeGpoCom.CreateNewGpo(actualDc, name);
                string gpoDn = NativeGpoLifecycleList.PolicyDn(created.Value, baseDn);
                var newPolicy = One(ldap, gpoDn,
                    "(objectClass=groupPolicyContainer)",
                    "versionNumber", "gPCUserExtensionNames", "flags");
                if (Required(newPolicy, "versionNumber") != "0" ||
                    NativeAdDiscovery.Attr(newPolicy, "gPCUserExtensionNames").Length > 0)
                    throw new InvalidDataException("New user GPO is not empty.");
                NativeGpoDeploymentApply.RestrictApplyToGroup(ldap, gpoDn, selectedSid);

                gpoSysvol = @"\\" + actualDc + @"\SYSVOL\" + domain +
                    @"\Policies\{" + created.Value.ToString("D").ToUpperInvariant() + "}";
                string gpt = Path.Combine(gpoSysvol, "gpt.ini");
                string user = Path.Combine(gpoSysvol, "User");
                string pol = Path.Combine(user, "Registry.pol");
                if (!File.Exists(gpt) ||
                    !ZeroVersion.IsMatch(File.ReadAllText(gpt, Encoding.ASCII)) ||
                    File.Exists(pol))
                    throw new InvalidDataException("New GPO GPT state is not empty.");
                Directory.CreateDirectory(user);
                byte[] registry = BuildRegistryPol(saver, seconds, secure);
                VerifyScreenSaverRegistryPol(registry, saver, seconds, secure);
                File.WriteAllBytes(pol, registry);
                byte[] actualPolicyBytes = File.ReadAllBytes(pol);
                if (!SHA256.HashData(actualPolicyBytes)
                    .SequenceEqual(SHA256.HashData(registry)))
                    throw new IOException("Screen Saver Registry.pol read-back mismatch.");
                // A matching hash is not enough: the MS-GPREG file must also
                // contain four structurally valid UTF-16LE user policy records.
                VerifyScreenSaverRegistryPol(actualPolicyBytes, saver, seconds, secure);

                string gptText = File.ReadAllText(gpt, Encoding.ASCII);
                File.WriteAllText(gpt, ZeroVersion.Replace(gptText,
                    "Version=65536", 1), Encoding.ASCII);
                var edit = new ModifyRequest(gpoDn);
                foreach (var (attribute, value) in new[] {
                    ("versionNumber", "65536"),
                    ("gPCUserExtensionNames", UserExtension),
                    ("flags", "2")
                })
                {
                    var set = new DirectoryAttributeModification {
                        Name = attribute,
                        Operation = DirectoryAttributeOperation.Replace
                    };
                    set.Add(value);
                    edit.Modifications.Add(set);
                }
                ldap.SendRequest(edit);
                var verified = One(ldap, gpoDn,
                    "(objectClass=groupPolicyContainer)",
                    "versionNumber", "gPCUserExtensionNames", "flags");
                if (Required(verified, "versionNumber") != "65536" ||
                    Required(verified, "gPCUserExtensionNames") != UserExtension ||
                    Required(verified, "flags") != "2" ||
                    !File.Exists(pol) ||
                    !File.ReadAllText(gpt, Encoding.ASCII).Contains(
                        "Version=65536", StringComparison.Ordinal))
                    throw new IOException("User policy AD/SYSVOL version mismatch.");

                // One GPO per explicitly selected user group/OU; do not link
                // to domain or computer OU and do not enable loopback.
                NativeGpoDeploymentApply.LinkNewGpo(ldap, userOu, gpoDn);
                linked = true;
                return Reply(true,
                    "Screen Saver user GPO created with verified HKCU policy and filtered OU link.",
                    new {
                        domain, dc = actualDc, userOuDn = userOu, userGroupDn = userGroup,
                        groupSam, gpoName = name, gpoId = created.Value.ToString("D"),
                        targetMode = "ScreenSaver", directGroupMembers = memberCount,
                        screenSaverExe = saver, idleSeconds = seconds,
                        passwordOnResume = secure, registryKey = PolicyKey,
                        gpoMethod = "UserRegistryPolicy", sysvolOnDc = gpoSysvol,
                        linked, policyVersion = 65536
                    });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Incomplete Screen Saver user GPO deployment: " + ex.Message +
                    ". New GPO: " + (created?.ToString("D") ?? "(not created)") +
                    "; linked: " + linked + "; SYSVOL: " + gpoSysvol +
                    ". Inspect AD/SYSVOL manually before retrying.", ex);
            }
        }

        if (!credentials.IsAlternate) return Run();
        string login = credentials.UserName.Trim();
        string? accountDomain = null;
        int slash = login.IndexOf('\\');
        if (slash > 0 && slash < login.Length - 1)
        {
            accountDomain = login[..slash];
            login = login[(slash + 1)..];
        }
        else if (!login.Contains('@'))
            login += "@" + credentials.Domain.Trim();
        if (credentials.Password.Length == 0 ||
            !LogonUser(login, accountDomain, credentials.Password, 9, 3,
                out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot establish alternate network identity for Screen Saver GPO.");
        using (token) return WindowsIdentity.RunImpersonated(token, Run);
    }

    /// <summary>
    /// MS-GPREG Header = PReg + DWORD version; body fields are UTF-16LE,
    /// followed by little-endian DWORD type/size and exact binary value bytes.
    /// Tested without AD, clients, PowerShell or Registry writes.
    /// </summary>
    internal static byte[] BuildRegistryPol(string saver, int seconds, bool secure)
    {
        if (!CampaignTargetAssignments.BuiltInScreenSavers.Contains(saver,
                StringComparer.OrdinalIgnoreCase) || seconds is < 60 or > 86400)
            throw new ArgumentException("Invalid user Screen Saver policy.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("PReg"));
        writer.Write((uint)1);
        void Text(string value) => writer.Write(Encoding.Unicode.GetBytes(value));
        void Value(string key, string field, string value)
        {
            byte[] data = Encoding.Unicode.GetBytes(value + "\0");
            // Both UTF-16LE identifiers MUST include a U+0000 terminator
            // BEFORE their semicolon delimiter (MS-GPREG 2.2.1).
            // A plain "[key;value;..." passes string scans but Windows may
            // silently ignore the malformed registry policy.
            Text("[" + key + "\0;" + field + "\0;");
            writer.Write((uint)1); // REG_SZ
            Text(";");
            writer.Write((uint)data.Length);
            Text(";");
            writer.Write(data);
            Text("]");
        }

        Value(PolicyKey, "ScreenSaveActive", "1");
        Value(PolicyKey, "SCRNSAVE.EXE", saver);
        Value(PolicyKey, "ScreenSaveTimeOut", seconds.ToString(CultureInfo.InvariantCulture));
        Value(PolicyKey, "ScreenSaverIsSecure", secure ? "1" : "0");
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Strict independent binary parse of a user-scoped MS-GPREG v1 policy.
    /// This validates the actual bytes that the Windows Registry CSE reads;
    /// a case-insensitive substring search is NOT a valid integrity check.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ParseUserRegistryPol(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 10 or > 65535 ||
            !bytes.AsSpan(0, 4).SequenceEqual("PReg"u8) ||
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(4, 4)) != 1)
            throw new InvalidDataException("Invalid Registry.pol signature or version.");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int offset = 8;
        void RequireUtf16(char expected)
        {
            if (offset + 2 > bytes.Length ||
                System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                    bytes.AsSpan(offset, 2)) != expected)
                throw new InvalidDataException("Malformed Registry.pol delimiter.");
            offset += 2;
        }

        string ReadNullTerminatedUnicode()
        {
            int start = offset;
            while (offset + 2 <= bytes.Length)
            {
                ushort ch = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                    bytes.AsSpan(offset, 2));
                offset += 2;
                if (ch == 0)
                {
                    if (offset - start <= 2)
                        throw new InvalidDataException("Empty Registry.pol identifier.");
                    return Encoding.Unicode.GetString(bytes, start, offset - start - 2);
                }
                if (offset - start > 8192)
                    throw new InvalidDataException("Registry.pol identifier too long.");
            }
            throw new InvalidDataException("Registry.pol identifier lacks UTF-16 NUL.");
        }

        uint ReadUInt32()
        {
            if (offset + 4 > bytes.Length)
                throw new InvalidDataException("Truncated Registry.pol DWORD.");
            uint value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(offset, 4));
            offset += 4;
            return value;
        }

        while (offset < bytes.Length)
        {
            RequireUtf16('[');
            string key = ReadNullTerminatedUnicode();
            RequireUtf16(';');
            string name = ReadNullTerminatedUnicode();
            RequireUtf16(';');
            uint kind = ReadUInt32();
            RequireUtf16(';');
            uint dataSize = ReadUInt32();
            RequireUtf16(';');
            if (kind != 1 || dataSize is < 2 or > 65534 ||
                (dataSize % 2) != 0 || dataSize > bytes.Length - offset - 2)
                throw new InvalidDataException("Registry.pol REG_SZ kind/size is invalid.");
            string data = Encoding.Unicode.GetString(bytes, offset, (int)dataSize);
            offset += (int)dataSize;
            RequireUtf16(']');
            if (data.Length == 0 || data[^1] != '\0' ||
                data.AsSpan(0, data.Length - 1).Contains('\0') ||
                !key.Equals(PolicyKey, StringComparison.Ordinal))
                throw new InvalidDataException("Registry.pol key or REG_SZ terminator is invalid.");
            if (!values.TryAdd(name, data[..^1]))
                throw new InvalidDataException("Duplicate Registry.pol policy value.");
        }

        if (values.Count != 4 ||
            !values.ContainsKey("ScreenSaveActive") ||
            !values.ContainsKey("SCRNSAVE.EXE") ||
            !values.ContainsKey("ScreenSaveTimeOut") ||
            !values.ContainsKey("ScreenSaverIsSecure"))
            throw new InvalidDataException("Registry.pol must contain exactly four Screen Saver values.");
        return values;
    }

    internal static void VerifyScreenSaverRegistryPol(
        byte[] bytes, string saver, int seconds, bool secure)
    {
        var values = ParseUserRegistryPol(bytes);
        if (values["ScreenSaveActive"] != "1" ||
            !values["SCRNSAVE.EXE"].Equals(saver, StringComparison.OrdinalIgnoreCase) ||
            values["ScreenSaveTimeOut"] != seconds.ToString(CultureInfo.InvariantCulture) ||
            values["ScreenSaverIsSecure"] != (secure ? "1" : "0"))
            throw new InvalidDataException(
                "Registry.pol does not match the approved Screen Saver policy.");
    }

    private static JsonElement Reply(bool success, string message, object data) =>
        JsonSerializer.SerializeToElement(new { success, message, data }, Json);

    private static SearchResultEntry One(LdapConnection ldap,
        string dn, string filter, params string[] attributes)
    {
        var response = (SearchResponse)ldap.SendRequest(
            new SearchRequest(dn, filter, SearchScope.Base, attributes));
        if (response.Entries.Count != 1)
            throw new InvalidOperationException(
                "Selected Screen Saver OU/group could not be uniquely verified.");
        return response.Entries[0];
    }

    private static string Required(SearchResultEntry obj, string attribute)
    {
        string value = NativeAdDiscovery.Attr(obj, attribute);
        if (value.Length == 0)
            throw new InvalidDataException("Missing AD attribute " + attribute);
        return value;
    }

    private static string Required(JsonElement args, string key)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException("Missing Screen Saver parameter " + key);
        return value.GetString()!;
    }

    private static int Int(JsonElement args, string key) =>
        args.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int i)
            ? i : throw new ArgumentException("Invalid integer Screen Saver parameter " + key);

    private static bool Boolean(JsonElement args, string key) =>
        args.TryGetProperty(key, out var value) && value.ValueKind is
            JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new ArgumentException("Invalid boolean Screen Saver parameter " + key);

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string username, string? domain,
        string password, int logonType, int provider,
        out SafeAccessTokenHandle token);
}
