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

    internal static Guid CreateNewGpo(string selectedDc, string newPolicyName)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows GPO COM is required.");
        if (string.IsNullOrWhiteSpace(selectedDc) ||
            newPolicyName.Length is < 3 or > 120 || newPolicyName.Any(char.IsControl))
            throw new ArgumentException("Invalid new GPO identity.");

        object com = new GroupPolicyObject();
        try
        {
            var policy = (IGroupPolicyObject)com;
            policy.New("LDAP://" + selectedDc, newPolicyName, 0);
            var identifier = new System.Text.StringBuilder(512);
            policy.GetName(identifier, identifier.Capacity);
            if (!Guid.TryParse(identifier.ToString().Trim('{', '}'), out Guid gpoId) ||
                gpoId == Guid.Empty)
                throw new InvalidOperationException("GPO COM returned an invalid policy GUID.");
            return gpoId;
        }
        finally
        {
            if (Marshal.IsComObject(com)) _ = Marshal.ReleaseComObject(com);
        }
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
