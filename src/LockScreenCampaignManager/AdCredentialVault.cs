using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace LockScreenCampaignManager;

/// <summary>
/// Per-Windows-user credential persistence. Windows Credential Manager protects
/// generic credential blobs with its OS-managed DPAPI protection. Nothing is
/// stored in the project, company profile, logs, or source-controlled files.
/// Other processes running as this Windows user can access user credentials;
/// this is convenience storage, not isolation from compromised user sessions.
/// </summary>
internal static class AdCredentialVault
{
    private const uint TypeGeneric = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaxPasswordBytes = 5120;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);

    internal sealed record Entry(string Username, string Password)
    {
        internal bool HasPassword => Password.Length > 0;
    }

    private static string Target(string domain)
    {
        domain = domain.Trim().TrimEnd('.').ToLowerInvariant();
        if (domain.Length is < 3 or > 253 ||
            !System.Text.RegularExpressions.Regex.IsMatch(domain,
                @"\A[a-z0-9-]+(?:\.[a-z0-9-]+)+\z"))
            throw new ArgumentException("Enter a valid AD DNS domain first.", nameof(domain));
        return "LockScreenCampaignManager/AD/" + domain;
    }

    internal static Entry? Read(string domain)
    {
        if (!CredRead(Target(domain), TypeGeneric, 0, out IntPtr ptr))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return null;
            throw new Win32Exception(error, "Windows Credential Manager read failed.");
        }
        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            if (cred.CredentialBlobSize > MaxPasswordBytes)
                throw new InvalidDataException("Saved credential exceeds supported length.");
            string password = "";
            if (cred.CredentialBlobSize != 0)
            {
                if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize % 2 != 0)
                    throw new InvalidDataException("Saved credential has an invalid encrypted payload.");
                password = Marshal.PtrToStringUni(cred.CredentialBlob,
                    checked((int)cred.CredentialBlobSize / 2)) ?? "";
            }
            return new Entry(cred.UserName ?? "", password);
        }
        finally { CredFree(ptr); }
    }

    internal static void Save(string domain, string username, string password,
        bool rememberUsername, bool savePassword)
    {
        string target = Target(domain);
        if (!rememberUsername && !savePassword)
        {
            Delete(domain);
            return;
        }
        if (savePassword && !rememberUsername)
            throw new ArgumentException("Saving a password requires remembering its username.");
        username = username.Trim();
        if (username.Length == 0 || username.Length > 512 || username.Any(char.IsControl))
            throw new ArgumentException("Enter a valid AD username.");
        byte[] bytes = savePassword ? Encoding.Unicode.GetBytes(password) : Array.Empty<byte>();
        if (bytes.Length > MaxPasswordBytes || (savePassword && bytes.Length == 0))
            throw new ArgumentException("Invalid credential password length.");
        IntPtr blob = IntPtr.Zero;
        try
        {
            if (bytes.Length > 0)
            {
                blob = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, blob, bytes.Length);
            }
            var cred = new Credential {
                Type = TypeGeneric, TargetName = target,
                UserName = username, CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob, Persist = PersistLocalMachine,
                Comment = "LockScreen Campaign Manager - user opted in"
            };
            if (!CredWrite(ref cred, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Unable to save the AD credential in Windows Credential Manager.");
        }
        finally
        {
            if (blob != IntPtr.Zero)
            {
                for (int i = 0; i < bytes.Length; i++) Marshal.WriteByte(blob, i, 0);
                Marshal.FreeCoTaskMem(blob);
            }
            if (bytes.Length > 0) Array.Clear(bytes);
        }
    }

    internal static void Delete(string domain)
    {
        if (!CredDelete(Target(domain), TypeGeneric, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
                throw new Win32Exception(error, "Unable to remove the saved AD credential.");
        }
    }
}
