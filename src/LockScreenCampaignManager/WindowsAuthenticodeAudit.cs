using System.Runtime.InteropServices;

namespace LockScreenCampaignManager;

/// <summary>
/// Native Windows Authenticode chain and file-digest verification. WinVerifyTrust
/// checks the signed PE, unlike merely reading an embedded certificate.
/// A valid signature proves a trusted signer, NOT that the signer is the
/// administrator's intended publisher; automatic installation remains disabled.
/// </summary>
internal static class WindowsAuthenticodeAudit
{
    private static readonly Guid GenericVerifyV2 = new(
        "00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallback;
        public IntPtr SipClient;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", EntryPoint = "WinVerifyTrust", ExactSpelling = true,
        CallingConvention = CallingConvention.Winapi)]
    private static extern int WinVerifyTrust(
        IntPtr owner, ref Guid action, ref WinTrustData data);

    internal sealed record Result(bool Trusted, string Code, string Explanation);

    internal static Result Inspect(string executablePath, bool cacheOnly = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Authenticode is Windows-only.");
        if (string.IsNullOrWhiteSpace(executablePath) ||
            !Path.IsPathFullyQualified(executablePath) || !File.Exists(executablePath))
            throw new FileNotFoundException("Staged Windows executable not found.");
        if ((File.GetAttributes(executablePath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Signature inspection does not follow linked EXE files.");

        IntPtr widePath = IntPtr.Zero;
        IntPtr fileInfoPointer = IntPtr.Zero;
        try
        {
            widePath = Marshal.StringToHGlobalUni(Path.GetFullPath(executablePath));
            var fileInfo = new WinTrustFileInfo {
                StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = widePath
            };
            fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var data = new WinTrustData {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2,              // WTD_UI_NONE
                RevocationChecks = 1,      // WTD_REVOKE_WHOLECHAIN
                UnionChoice = 1,           // WTD_CHOICE_FILE
                FileInfo = fileInfoPointer,
                StateAction = 0,           // WTD_STATEACTION_IGNORE
                ProviderFlags = 0x00000080u | (cacheOnly ? 0x00001000u : 0u) // WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT | WTD_CACHE_ONLY_URL_RETRIEVAL
            };
            Guid action = GenericVerifyV2;
            int code = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            bool trusted = code == 0;
            return new Result(trusted, "0x" + unchecked((uint)code).ToString("X8"),
                trusted
                    ? (cacheOnly ? "Windows verified the PE using cached revocation evidence only. " : "Windows verified the PE signature and trust chain. ") +
                      "Administrator publisher pinning is still required before unattended installation."
                    : "Windows could not verify a trusted Authenticode signature. " +
                      "Do not treat the GitHub SHA-256 digest as publisher authenticity.");
        }
        finally
        {
            if (fileInfoPointer != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPointer);
            if (widePath != IntPtr.Zero) Marshal.FreeHGlobal(widePath);
        }
    }
}
