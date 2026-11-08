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
        public ushort Priority;
        public ushort Weight;
        public ushort Port;
        public ushort Pad;
        public IntPtr Target;
    }

    [DllImport("dnsapi.dll", EntryPoint = "DnsQuery_W", CharSet = CharSet.Unicode)]
    private static extern int DnsQuery(string name, ushort type, uint options,
        IntPtr extra, out IntPtr results, IntPtr reserved);

    [DllImport("dnsapi.dll", EntryPoint = "DnsRecordListFree")]
    private static extern void DnsRecordListFree(IntPtr records, int freeType);

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
                return names.ToArray();
            }
            finally { if (records != IntPtr.Zero) DnsRecordListFree(records, 1); }
        }
        return names.Take(100).ToArray();
    }
}
