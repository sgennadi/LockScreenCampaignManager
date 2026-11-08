using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>Windows DNS SRV lookup for DC discovery; no RSAT, PowerShell or credentials.</summary>
internal static class DcDnsDiscovery
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SrvRecord
    {
        public IntPtr Next;
        public IntPtr Name;
        public ushort Type;
        public ushort DataLength;
        public uint Flags;
        public uint Ttl;
        public uint Reserved;
        // DNS_SRV_DATA starts with PWSTR pNameTarget (pointer aligned).
        // Priority/Weight/Port come AFTER the pointer, not before it.
        public IntPtr Target;
        public ushort Priority;
        public ushort Weight;
        public ushort Port;
        public ushort Pad;
    }

    [DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode)]
    private static extern int DnsQuery(string name, ushort type, uint options,
        IntPtr extra, out IntPtr results, IntPtr reserved);

    [DllImport("dnsapi.dll", EntryPoint = "DnsRecordListFree")]
    private static extern void DnsRecordListFree(IntPtr records, int freeType);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DomainControllerInfo
    {
        public IntPtr DomainControllerName;
        public IntPtr DomainControllerAddress;
        public uint DomainControllerAddressType;
        public Guid DomainGuid;
        public IntPtr DomainName;
        public IntPtr DnsForestName;
        public uint Flags;
        public IntPtr DcSiteName;
        public IntPtr ClientSiteName;
    }

    [DllImport("netapi32.dll", EntryPoint = "DsGetDcNameW", CharSet = CharSet.Unicode)]
    private static extern int DsGetDcName(string? computerName, string domainName,
        IntPtr domainGuid, string? siteName, uint flags, out IntPtr dcInfo);

    [DllImport("netapi32.dll", EntryPoint = "NetApiBufferFree")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    // DC locator (Netlogon) complements DNS SRV and works without RSAT.
    private static string? LocateDc(string domain)
    {
        const uint DS_DIRECTORY_SERVICE_REQUIRED = 0x00000010;
        const uint DS_RETURN_DNS_NAME = 0x40000000;
        IntPtr info = IntPtr.Zero;
        try
        {
            if (DsGetDcName(null, domain, IntPtr.Zero, null,
                DS_DIRECTORY_SERVICE_REQUIRED | DS_RETURN_DNS_NAME, out info) != 0 ||
                info == IntPtr.Zero) return null;
            var result = Marshal.PtrToStructure<DomainControllerInfo>(info);
            string name = (Marshal.PtrToStringUni(result.DomainControllerName) ?? "")
                .Trim().TrimStart('\\').TrimEnd('.');
            return name.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase) &&
                name.Length <= 255 && !name.Any(char.IsControl) ? name : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or
            EntryPointNotFoundException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
        finally { if (info != IntPtr.Zero) NetApiBufferFree(info); }
    }

    internal static string LocalDnsDomain() =>
        IPGlobalProperties.GetIPGlobalProperties().DomainName.Trim().TrimEnd('.');

    internal static IReadOnlyList<string> FindControllers(string domain)
    {
        domain = domain.Trim().TrimEnd('.');
        if (domain.Length is < 3 or > 250 ||
            !Regex.IsMatch(domain, @"\A(?=.{3,250}\z)[a-zA-Z0-9-]+(?:\.[a-zA-Z0-9-]+)+\z"))
            return Array.Empty<string>();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prefix in new[] { "_ldap._tcp.dc._msdcs.", "_ldap._tcp." })
        {
            IntPtr records = IntPtr.Zero;
            try
            {
                if (DnsQuery(prefix + domain, 33, 0, IntPtr.Zero,
                    out records, IntPtr.Zero) != 0) continue;
                IntPtr cursor = records;
                for (int i = 0; i < 128 && cursor != IntPtr.Zero; i++)
                {
                    var srv = Marshal.PtrToStructure<SrvRecord>(cursor);
                    if (srv.Type == 33 && srv.Port == 389 && srv.Target != IntPtr.Zero)
                    {
                        string host = (Marshal.PtrToStringUni(srv.Target) ?? "")
                            .Trim().TrimEnd('.');
                        if (host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase) &&
                            host.Length <= 255 && !host.Any(char.IsControl))
                            names.Add(host);
                    }
                    cursor = srv.Next;
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or
                EntryPointNotFoundException or System.ComponentModel.Win32Exception)
            {
                break;
            }
            finally { if (records != IntPtr.Zero) DnsRecordListFree(records, 1); }
        }
        // When a DNS server refuses SRV enumeration, let Windows Netlogon
        // locate at least one controller. Never synthesize an unverified DC.
        if (names.Count == 0)
        {
            var located = LocateDc(domain);
            if (!string.IsNullOrEmpty(located)) names.Add(located);
        }
        return names.Take(100).ToArray();
    }
}
