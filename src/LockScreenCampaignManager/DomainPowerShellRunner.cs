using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

internal enum DomainOperation { Discovery, Deployment, Lifecycle, ConflictScan, Immediate, Assignment, Storage, DcConsistency }

internal sealed record DomainCredentials(string Domain, string UserName, string Password)
{
    public bool IsAlternate => !string.IsNullOrWhiteSpace(UserName);
}

internal static class DomainPowerShellRunner
{
    private const int LogonNetCredentialsOnly = 0x00000002;
    private const int CreateUnicodeEnvironment = 0x00000400;
    private const int CreateNoWindow = 0x08000000;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    internal static JsonElement Execute(DomainOperation operation, string mode, object request,
        DomainCredentials credentials, int timeoutMilliseconds = 180000)
    {
        // Read-only directory discovery runs entirely in C#, even for alternate
        // credentials. No temporary PS1 and no powershell.exe are launched.
        if (operation == DomainOperation.Discovery)
            return NativeAdDiscovery.Execute(mode, request, credentials);

        // Explicitly selected DC consistency audits also use native C# and
        // reuse the same authenticated LDAP library. SMB reads are read-only.
        if (operation == DomainOperation.DcConsistency)
            return NativeDcConsistency.Execute(mode, request, credentials);

        // Computer and Group Assignment is now native LDAP for Search,
        // Preview and Apply. Guarded writes never spawn powershell.exe.
        if (operation == DomainOperation.Assignment)
            return NativeComputerGroupAssignment.Execute(mode, request, credentials);

        // Only the basic inherited GPO/registry candidate Scan is native.
        // Effective/VerifyEndpoint/VerifyEvidence remain on the audited legacy
        // read-only engine until their source and scope contracts are ported.
        if (operation == DomainOperation.ConflictScan && mode == "Scan")
            return NativeGpoConflictScan.Execute(mode, request, credentials);

        // Read-only SYSVOL Scan and Cleanup Preview use native signed LDAP
        // and selected-DC SMB. The actual Cleanup remains on the historical
        // independently revalidating PS1 and requires typed admin approval.
        if (operation == DomainOperation.Storage && mode is ("Scan" or "Preview"))
            return NativeSysvolStorage.Execute(mode, request, credentials);

        // Read-only Lifecycle List and Preview use signed LDAP and
        // selected-DC SYSVOL without launching PowerShell. The legacy
        // Apply independently recomputes the exact same preview hash from
        // fresh AD/SYSVOL data before any write.
        if (operation == DomainOperation.Lifecycle && mode == "List")
            return NativeGpoLifecycleList.Execute(mode, request, credentials);
        if (operation == DomainOperation.Lifecycle && mode == "Preview")
            return NativeGpoLifecyclePreview.Execute(mode, request, credentials);

        // Eligible-host lookup, Preview, and post-Cleanup verification
        // run in native signed LDAP / read-only SMB, Scheduler RPC and HKLM.
        // Task creation, Push/Repair/Report/Cleanup remain in the audited
        // PowerShell module with independently rechecked approval hashes.
        if (operation == DomainOperation.Immediate &&
            mode is ("List" or "Preview" or "VerifyCleanup"))
            return NativeImmediateActions.Execute(mode, request, credentials);

        // Deployment preflight reads only: OU/group, GPO name availability,
        // SYSVOL, campaign schedules and image hashes. PS1 Deploy retains all
        // existing write safeguards and re-runs its own complete preflight.
        if (operation == DomainOperation.Deployment && mode == "Plan")
            return NativeGpoDeploymentPlan.Execute(mode, request, credentials);

        var root = Path.Combine(Path.GetTempPath(), "LSCM-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string asset = operation switch
            {
                DomainOperation.Deployment => "LockScreenCampaignManager.GPODeploy.ps1",
                DomainOperation.Lifecycle => "LockScreenCampaignManager.GpoLifecycle.ps1",
                DomainOperation.ConflictScan => "LockScreenCampaignManager.GpoConflictScan.ps1",
                DomainOperation.Immediate => "LockScreenCampaignManager.CampaignPushNow.ps1",
                DomainOperation.Storage => "LockScreenCampaignManager.SysvolStorage.ps1",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            string script = Path.Combine(root, operation switch
            {
                DomainOperation.Deployment => "GPO-Deploy.ps1",
                DomainOperation.Lifecycle => "Gpo-Lifecycle.ps1",
                DomainOperation.ConflictScan => "Gpo-ConflictScan.ps1",
                DomainOperation.Immediate => "Campaign-PushNow.ps1",
                DomainOperation.Storage => "Sysvol-Storage.ps1",
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            });
            using (var resource = typeof(DomainPowerShellRunner).Assembly.GetManifestResourceStream(asset)
                   ?? throw new InvalidOperationException("Embedded domain tool missing: " + asset))
            using (var dest = File.Create(script)) resource.CopyTo(dest);
            string reqPath = Path.Combine(root, "request.json");
            string resPath = Path.Combine(root, "response.json");
            File.WriteAllText(reqPath, JsonSerializer.Serialize(request, Json), new UTF8Encoding(false));
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"System32\WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(powershell)) throw new FileNotFoundException("Windows PowerShell 5.1 is missing.", powershell);
            var arguments = new[] {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-File", script, "-Mode", mode, "-RequestPath", reqPath, "-ResponsePath", resPath
            };
            int exit;
            if (credentials.IsAlternate)
                exit = RunWithNetworkCredentials(powershell, arguments, credentials, timeoutMilliseconds);
            else
                exit = RunNormally(powershell, arguments, timeoutMilliseconds);
            if (!File.Exists(resPath))
                throw new InvalidOperationException("Domain command did not return a result (exit " + exit + "). Check Windows PowerShell/AppLocker, LDAP, COM/GPO, SMB and permissions.");
            using var doc = JsonDocument.Parse(File.ReadAllText(resPath));
            var response = doc.RootElement.Clone();
            if (!response.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException(response.TryGetProperty("message", out var error)
                    ? error.GetString() ?? "Domain command failed" : "Domain command failed");
            if (exit != 0) throw new InvalidOperationException("Domain command unexpectedly failed with exit code " + exit);
            return response;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) { AppLog.Write(ex); }
        }
    }

    private static int RunNormally(string program, IEnumerable<string> args, int timeoutMilliseconds)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start Windows PowerShell.");
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("Active Directory operation timed out.");
        }
        return process.ExitCode;
    }

    private static int RunWithNetworkCredentials(string program, string[] args,
        DomainCredentials credentials, int timeoutMilliseconds)
    {
        ParseIdentity(credentials, out var user, out var domain);
        var commandLine = new StringBuilder(QuoteArgument(program));
        foreach (var arg in args) commandLine.Append(' ').Append(QuoteArgument(arg));
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        if (!CreateProcessWithLogonW(user, domain, credentials.Password,
                LogonNetCredentialsOnly, program, commandLine,
                CreateUnicodeEnvironment | CreateNoWindow, IntPtr.Zero,
                Path.GetDirectoryName(program), ref startup, out var proc))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Unable to start a network-authenticated domain session. Check Secondary Logon service and credentials.");
        try
        {
            uint wait = WaitForSingleObject(proc.ProcessHandle, (uint)timeoutMilliseconds);
            if (wait == WaitTimeout)
            {
                TerminateProcess(proc.ProcessHandle, 1);
                throw new TimeoutException("Active Directory operation timed out.");
            }
            if (wait != WaitObject0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot wait for PowerShell result.");
            if (!GetExitCodeProcess(proc.ProcessHandle, out uint code))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read PowerShell exit code.");
            return unchecked((int)code);
        }
        finally
        {
            if (proc.ProcessHandle != IntPtr.Zero) CloseHandle(proc.ProcessHandle);
            if (proc.ThreadHandle != IntPtr.Zero) CloseHandle(proc.ThreadHandle);
        }
    }

    private static void ParseIdentity(DomainCredentials credentials, out string user, out string? domain)
    {
        var input = credentials.UserName.Trim();
        int slash = input.IndexOf('\\');
        if (slash > 0 && slash < input.Length - 1)
        {
            domain = input[..slash];
            user = input[(slash + 1)..];
        }
        else if (input.Contains('@')) { user = input; domain = null; }
        else
        {
            if (string.IsNullOrWhiteSpace(credentials.Domain))
                throw new InvalidOperationException("Enter a DNS domain with an alternate account.");
            user = input + "@" + credentials.Domain.Trim();
            domain = null;
        }
        if (string.IsNullOrEmpty(credentials.Password))
            throw new InvalidOperationException("Enter the alternate account password; it is never saved.");
    }

    // Command line is built only from known flags and temporary paths. The password is
    // passed to CreateProcessWithLogonW, never to a command-line argument or a file.
    private static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char ch in value)
        {
            if (ch == '\\') { slashes++; continue; }
            if (ch == '"')
            {
                result.Append('\\', slashes * 2 + 1).Append('"');
                slashes = 0;
                continue;
            }
            result.Append('\\', slashes).Append(ch);
            slashes = 0;
        }
        result.Append('\\', slashes * 2).Append('"');
        return result.ToString();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithLogonW(string userName, string? domain, string password,
        int logonFlags, string applicationName, StringBuilder commandLine,
        int creationFlags, IntPtr environment, string? currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr handle, out uint code);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr handle, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
