using System.Collections;
using System.ComponentModel;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// Explicit read-only power observation for one AD-verified computer in a
/// selected OU. WMI and HKLM policy values are independent observations, not
/// proof of a winning GPO or of a visible User Screen Saver.
/// No remote command, task, gpupdate, service control, or power-plan change.
/// </summary>
internal static class NativeEndpointPowerObservation
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string VideoIdle = "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e";
    private const string SleepIdle = "29f6c1db-86da-48c5-9fdb-f2b67b1f44da";
    private const string PolicyRoot = @"SOFTWARE\Policies\Microsoft\Power\PowerSettings\";
    private const string MachineLockKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private sealed record PowerValue(string kind, string supply,
        string source, string status, long? seconds);
    private sealed record PowerRead(string status, PowerValue[] values, string note);
    private sealed record PlanRead(string status, string guid, string name,
        PowerValue[] values, string note);

    internal static JsonElement Execute(string mode, object request, DomainCredentials credentials)
    {
        if (mode != "Observe")
            throw new ArgumentException("Power observations support read-only Observe only.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows LDAP, WMI and Remote Registry are required.");
        JsonElement args = JsonSerializer.SerializeToElement(request, Json);
        string domain = Required(args, "domain").ToLowerInvariant();
        string dc = Required(args, "dc");
        string ouDn = Required(args, "ouDn");
        string computer = Required(args, "computer").ToLowerInvariant();

        NativeAdDiscovery.ValidateNames(domain, dc);
        if (dc.Length == 0 || !ouDn.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
            ouDn.Length > 2048 || ouDn.Any(char.IsControl) ||
            !NativeGpoConflictScan.SafeComputerName(computer, domain))
            throw new ArgumentException("Select a Computer OU and a valid domain computer FQDN.");

        JsonElement Run()
        {
            // All network reads, including LDAP, share the same Windows
            // identity. No remote WMI/Registry access before LDAP validation.
            using (var ldap = NativeAdDiscovery.Connect(dc, credentials))
            {
                ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
                SearchResultEntry root = NativeAdDiscovery.ReadRootDse(ldap);
                string baseDn = NativeAdDiscovery.Attr(root, "defaultNamingContext");
                string actualDc = NativeAdDiscovery.Attr(root, "dnsHostName");
                if (NativeAdDiscovery.DomainFromDn(baseDn) != domain ||
                    !NativeGpoLifecycleList.SameController(dc, actualDc) ||
                    !NativeGpoConflictScan.Inside(ouDn, baseDn))
                    throw new InvalidDataException("Selected AD domain/DC/OU could not be verified.");
                var ou = (SearchResponse)ldap.SendRequest(new SearchRequest(
                    ouDn, "(objectClass=organizationalUnit)", SearchScope.Base, "distinguishedName"));
                if (ou.Entries.Count != 1)
                    throw new InvalidDataException("Selected Computer OU is unavailable.");
                var search = new SearchRequest(ouDn,
                    "(&(objectCategory=computer)(dNSHostName=" +
                    NativeAdDiscovery.EscapeFilter(computer) + "))",
                    SearchScope.Subtree, "dNSHostName", "userAccountControl");
                search.SizeLimit = 2;  // duplicate computer identities fail closed
                var response = (SearchResponse)ldap.SendRequest(search);
                if (response.Entries.Count != 1 ||
                    !response.Entries[0].DistinguishedName.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ||
                    !NativeGpoConflictScan.Inside(response.Entries[0].DistinguishedName, ouDn) ||
                    !string.Equals(NativeAdDiscovery.Attr(response.Entries[0], "dNSHostName"),
                        computer, StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(NativeAdDiscovery.Attr(response.Entries[0], "userAccountControl"),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out int flags) ||
                    (flags & 2) != 0)
                    throw new InvalidDataException("Computer is missing, disabled or outside selected Computer OU.");
            }

            var plan = ReadActivePlan(computer);
            var registry = ReadPolicies(computer);
            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                message = "Read-only scoped endpoint power observation. No GPO changes.",
                data = new
                {
                    domain, dc, ouDn, computer, adScopeVerified = true,
                    observedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    activePlanStatus = plan.status, activePlanGuid = plan.guid,
                    activePlanName = plan.name, policyRegistryStatus = registry.status,
                    values = plan.values.Concat(registry.values).ToArray(),
                    notes = new[]
                    {
                        plan.note, registry.note,
                        "Active-plan settings can be overridden by GPO, device state or MDM. " +
                        "Remote HKLM values do not identify the winning GPO. User HKCU and physical display are NOT verified."
                    }
                }
            }, Json);
        }

        if (!credentials.IsAlternate) return Run();
        string user = credentials.UserName.Trim();
        string? netbios = null;
        int slash = user.IndexOf('\\');
        if (slash > 0 && slash < user.Length - 1)
        {
            netbios = user[..slash];
            user = user[(slash + 1)..];
        }
        else if (!user.Contains('@')) user += "@" + credentials.Domain.Trim();
        if (credentials.Password.Length == 0 ||
            !LogonUser(user, netbios, credentials.Password, 9, 3,
                out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not establish read-only network identity for endpoint observation.");
        using (token) return WindowsIdentity.RunImpersonated(token, Run);
    }

    private static PlanRead ReadActivePlan(string computer)
    {
        object? locator = null, service = null, plans = null, indices = null;
        var results = new List<PowerValue>();
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator")
                ?? throw new COMException("WMI locator unavailable.");
            locator = Activator.CreateInstance(type)
                ?? throw new COMException("Cannot instantiate WMI locator.");
            dynamic l = locator;
            service = l.ConnectServer(computer, @"root\cimv2\power");
            dynamic wmi = service;
            plans = wmi.ExecQuery("SELECT * FROM Win32_PowerPlan");
            if (plans is not IEnumerable available)
                throw new COMException("WMI power plans cannot be enumerated.");

            string activeGuid = "", activeName = "";
            object? activePlan = null;
            int planCount = 0;
            try
            {
                foreach (object item in available)
                {
                    bool active = false;
                    try
                    {
                        if (++planCount > 40)
                            throw new InvalidDataException("Too many power plans.");
                        dynamic p = item;
                        active = Convert.ToBoolean(p.Properties_.Item("IsActive").Value,
                            CultureInfo.InvariantCulture);
                        if (!active) continue;
                        string instance = Convert.ToString(p.Properties_.Item("InstanceID").Value,
                            CultureInfo.InvariantCulture) ?? "";
                        if (!TryPlanId(instance, out string guid) || activePlan is not null)
                            throw new InvalidDataException("Active power plan is ambiguous.");
                        activeGuid = guid;
                        activeName = SafeLabel(Convert.ToString(
                            p.Properties_.Item("ElementName").Value,
                            CultureInfo.InvariantCulture) ?? "", 90);
                        activePlan = item; // keep COM plan object alive for GetRelated
                    }
                    finally
                    {
                        if (!active || !ReferenceEquals(item, activePlan))
                            Release(item);
                    }
                }
                if (activePlan is null)
                    throw new InvalidDataException("WMI did not return one active plan.");

                // Microsoft documents the association from a power PLAN to
                // DataIndex and from each index to Win32_PowerSetting.
                // DataIndex's final GUID is NOT the PowerSetting GUID.
                dynamic selected = activePlan;
                indices = selected.GetRelated("Win32_PowerSettingDataIndex");
                if (indices is not IEnumerable dataIndices)
                    throw new COMException("Active plan setting association unavailable.");
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int indexCount = 0;
                foreach (object index in dataIndices)
                {
                    object? associated = null;
                    try
                    {
                        if (++indexCount > 2048)
                            throw new InvalidDataException("Too many related power indices.");
                        dynamic row = index;
                        string instance = Convert.ToString(row.Properties_.Item("InstanceID").Value,
                            CultureInfo.InvariantCulture) ?? "";
                        if (!TryIndexSupply(instance, activeGuid, out string supply)) continue;
                        associated = row.GetRelated("Win32_PowerSetting");
                        if (associated is not IEnumerable settings)
                            throw new COMException("Power setting association unavailable.");
                        int relatedCount = 0;
                        foreach (object setting in settings)
                        {
                            try
                            {
                                if (++relatedCount > 1)
                                    throw new InvalidDataException("Ambiguous power setting relation.");
                                dynamic metadata = setting;
                                string id = Convert.ToString(
                                    metadata.Properties_.Item("InstanceID").Value,
                                    CultureInfo.InvariantCulture) ?? "";
                                if (!TrySettingKind(id, out string kind)) continue;
                                if (!found.Add(kind + "/" + supply))
                                    throw new InvalidDataException("Duplicate active plan timer.");
                                long seconds = Convert.ToUInt32(
                                    row.Properties_.Item("SettingIndexValue").Value,
                                    CultureInfo.InvariantCulture);
                                results.Add(new PowerValue(kind, supply,
                                    "ActivePlanWmi", "Present", seconds));
                            }
                            finally { Release(setting); }
                        }
                    }
                    finally { Release(associated); Release(index); }
                }
                foreach (string kind in new[] { "Display", "Sleep" })
                    foreach (string supply in new[] { "AC", "DC" })
                        if (!found.Contains(kind + "/" + supply))
                            results.Add(new PowerValue(kind, supply,
                                "ActivePlanWmi", "Unavailable", null));
                string status = results.All(v => v.status == "Present")
                    ? "Available" : "Partial";
                return new PlanRead(status, activeGuid, activeName, results.ToArray(),
                    "Read-only active-plan WMI data. Windows may override it with GPO or MDM.");
            }
            finally { Release(activePlan); }
        }
        catch (Exception)
        {
            return new PlanRead("Unavailable", "", "", [],
                "Remote WMI active plan could not be read completely. Not a safe/zero result.");
        }
        finally { Release(indices); Release(plans); Release(service); Release(locator); }
    }

    internal static bool TryPlanId(string value, out string guid)
    {
        guid = "";
        const string prefix = "Microsoft:PowerPlan\\";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(value[prefix.Length..], "B", out Guid plan) ||
            plan == Guid.Empty) return false;
        guid = plan.ToString("D");
        return true;
    }

    internal static bool TryIndexSupply(string value, string activePlan,
        out string supply)
    {
        supply = "";
        string[] parts = value.Split('\\');
        if (parts.Length != 4 ||
            !parts[0].Equals("Microsoft:PowerSettingDataIndex", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(parts[1], "B", out Guid plan) ||
            !Guid.TryParse(activePlan, out Guid expected) || plan != expected ||
            !Guid.TryParseExact(parts[3], "B", out Guid dataIndex) || dataIndex == Guid.Empty ||
            !(parts[2].Equals("AC", StringComparison.OrdinalIgnoreCase) ||
              parts[2].Equals("DC", StringComparison.OrdinalIgnoreCase))) return false;
        supply = parts[2].ToUpperInvariant();
        return true;
    }

    internal static bool TrySettingKind(string value, out string kind)
    {
        kind = "";
        const string prefix = "Microsoft:PowerSetting\\";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(value[prefix.Length..], "B", out Guid id))
            return false;
        kind = id.ToString("D") switch {
            VideoIdle => "Display",
            SleepIdle => "Sleep",
            _ => ""
        };
        return kind.Length > 0;
    }

    private static PowerRead ReadPolicies(string computer)
    {
        var values = new List<PowerValue>();
        RegistryKey root;
        try
        {
            root = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine,
                computer, RegistryView.Registry64);
        }
        catch (Exception)
        {
            return new PowerRead("Unavailable", [],
                "Remote HKLM policy Registry unavailable. No absence was inferred.");
        }
        bool partial = false;
        using (root)
        {
            foreach (var (kind, guid) in new[] {
                (Kind:"Display", Guid:VideoIdle), (Kind:"Sleep", Guid:SleepIdle)
            })
            {
                foreach (string supply in new[] { "AC", "DC" })
                {
                    var v = Dword(root, PolicyRoot + guid, supply + "SettingIndex");
                    if (v.Status == "Unreadable") partial = true;
                    values.Add(new PowerValue(kind, supply, "PolicyRegistry",
                        v.Status, v.Seconds));
                }
            }
            var lockSeconds = Dword(root, MachineLockKey, "InactivityTimeoutSecs");
            if (lockSeconds.Status == "Unreadable") partial = true;
            values.Add(new PowerValue("MachineLock", "Any", "PolicyRegistry",
                lockSeconds.Status, lockSeconds.Seconds));
        }
        return new PowerRead(partial ? "Partial" : "Readable", values.ToArray(),
            "Read-only policy registry snapshot. Source GPO/effective user state unverified.");
    }

    private static (string Status, long? Seconds) Dword(
        RegistryKey root, string path, string name)
    {
        try
        {
            using var key = root.OpenSubKey(path, writable: false);
            if (key is null || !key.GetValueNames().Contains(name,
                    StringComparer.OrdinalIgnoreCase)) return ("Absent", null);
            if (key.GetValueKind(name) != RegistryValueKind.DWord)
                return ("Unreadable", null);
            object? raw = key.GetValue(name, null,
                RegistryValueOptions.DoNotExpandEnvironmentNames);
            return raw is int value
                ? ("Present", unchecked((uint)value))
                : ("Unreadable", null);
        }
        catch (Exception)
        {
            return ("Unreadable", null);
        }
    }

    private static string Required(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var field) &&
        field.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(field.GetString())
            ? field.GetString()! : throw new InvalidDataException(
                "Missing/invalid endpoint power observation request field.");

    private static string SafeLabel(string text, int max) =>
        new string(text.Take(max).Where(c => !char.IsControl(c)).ToArray());

    private static void Release(object? item)
    {
        if (item is not null && Marshal.IsComObject(item))
            Marshal.FinalReleaseComObject(item);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string user, string? domain,
        string password, int logonType, int provider, out SafeAccessTokenHandle token);
}
