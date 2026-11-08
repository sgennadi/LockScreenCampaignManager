using System.Runtime.InteropServices;

namespace LockScreenCampaignManager;

/// <summary>
/// Built-in Windows Group Policy Management COM interface (gpedit.dll).
/// This is native Windows COM, not RSAT or a PowerShell module. All callers
/// must revalidate AD/SYSVOL state, current target and explicit approval
/// before touching the GPO. Deleting a GPO is never a link-only operation.
/// </summary>
internal static class NativeGpoCom
{
    [ComImport, Guid("EA502722-A23D-11d1-A7D3-0000F87571E3")]
    private sealed class GroupPolicyObject { }

    [ComImport, Guid("EA502723-A23D-11d1-A7D3-0000F87571E3"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGroupPolicyObject
    {
        void New([MarshalAs(UnmanagedType.LPWStr)] string domain,
            [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags);
        void OpenDSGPO([MarshalAs(UnmanagedType.LPWStr)] string path, uint flags);
        void OpenLocalMachineGPO(uint flags);
        void OpenRemoteMachineGPO([MarshalAs(UnmanagedType.LPWStr)] string path, uint flags);
        void Save([MarshalAs(UnmanagedType.Bool)] bool machine,
            [MarshalAs(UnmanagedType.Bool)] bool add,
            ref Guid extension, ref Guid snapin);
        void Delete();
        void GetName([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name,
            int capacity);
    }

    internal static void DeleteManagedUnlinkedGpo(string verifiedDc, string gpoDn)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows GPO COM is required.");
        if (string.IsNullOrWhiteSpace(verifiedDc) ||
            !gpoDn.StartsWith("CN={", StringComparison.OrdinalIgnoreCase) ||
            !gpoDn.Contains(",CN=Policies,CN=System,DC=", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing GPO deletion for an invalid or unverified policy identity.");

        object com = new GroupPolicyObject();
        try
        {
            var policy = (IGroupPolicyObject)com;
            policy.OpenDSGPO("LDAP://" + verifiedDc + "/" + gpoDn, 0);
            policy.Delete();
        }
        finally
        {
            if (Marshal.IsComObject(com))
                _ = Marshal.ReleaseComObject(com);
        }
    }
}
