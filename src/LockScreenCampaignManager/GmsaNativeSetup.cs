using System.ComponentModel;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Native C# / Windows API gMSA provisioning. No PowerShell, cmd, RSAT, scripts,
/// or application-managed passwords are involved. AD gMSA and host authorization
/// must already exist; this installer installs it on the authorized local host.
/// </summary>
internal static class GmsaNativeSetup
{
    internal const string TaskName = "LSCM-Gmsa-UnattendedRepair";
    internal const string DiagnosticTaskName = "LSCM-Gmsa-ReadOnlyDiagnostic";
    internal static string BinaryDirectory => Path.Combine(GmsaRepairConfig.Root, "bin");
    internal static string InstalledBinary => Path.Combine(
        BinaryDirectory, "LockScreenCampaignManager.exe");

    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonPassword = 1;
    private const int TaskRunLevelLUA = 0;
    private const uint LinkToHostOnly = 1;
    private static readonly SecurityIdentifier LocalSystem =
        new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    internal sealed record Request(string Account, string Domain, string OuDn,
        string GroupDn, string ShareRoot);

    internal sealed record CheckResult(string Account, string Sid, bool Installed,
        string Message);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int NetIsServiceAccountCall(
        IntPtr serverName, [MarshalAs(UnmanagedType.LPWStr)] string accountName,
        [MarshalAs(UnmanagedType.Bool)] out bool isService);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int NetAddServiceAccountCall(
        IntPtr serverName, [MarshalAs(UnmanagedType.LPWStr)] string accountName,
        IntPtr password, uint flags);

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetGetJoinInformation(
        IntPtr server, out IntPtr name, out int status);

    [DllImport("Netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    private static T Netlogon<T>(string entryPoint) where T : Delegate
    {
        // Microsoft documents these managed-service-account exports in
        // Logoncli.dll; DllImport("Netapi32.dll") alone is not reliable.
        string trustedDll = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "logoncli.dll");
        // Never permit searching the application folder, current directory,
        // or PATH for a privileged Windows Netlogon DLL.
        IntPtr module = NativeLibrary.Load(trustedDll);
        IntPtr address = NativeLibrary.GetExport(module, entryPoint);
        // Keep the loaded system DLL resident while the delegate is in use.
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static string SamAccount(string account) =>
        account.Split('\\', StringSplitOptions.RemoveEmptyEntries).Last();

    private static bool AccountInstalled(string account)
    {
        var call = Netlogon<NetIsServiceAccountCall>("NetIsServiceAccount");
        string sam = SamAccount(account);
        int code = call(IntPtr.Zero, sam, out bool isService);
        if (code != 0)
            throw new InvalidOperationException("NetIsServiceAccount failed with NTSTATUS 0x" +
                unchecked((uint)code).ToString("X8", CultureInfo.InvariantCulture) +
                ". Confirm that Netlogon is running and the gMSA is authorized on this host.");
        return isService;
    }

    private static void EnsureAccountInstalled(string account)
    {
        if (AccountInstalled(account)) return;
        var call = Netlogon<NetAddServiceAccountCall>("NetAddServiceAccount");
        // Host is already authorized in AD. This does NOT create an AD gMSA.
        // LINK_TO_HOST_ONLY never creates a standalone MSA; the flag is
        // ignored for an existing gMSA, as documented by Microsoft.
        int status = call(IntPtr.Zero, SamAccount(account), IntPtr.Zero, LinkToHostOnly);
        if (status != 0)
            throw new InvalidOperationException(
                "Windows could not prepare the gMSA on this server. " +
                "Verify this host is allowed to retrieve the managed password from AD. " +
                "NTSTATUS: 0x" + unchecked((uint)status).ToString("X8",
                    CultureInfo.InvariantCulture));
        if (!AccountInstalled(account))
            throw new InvalidOperationException(
                "gMSA did not appear in local Netlogon after installation.");
    }

    private static string JoinedDomain()
    {
        IntPtr domainPtr = IntPtr.Zero;
        int code = NetGetJoinInformation(IntPtr.Zero, out domainPtr, out int status);
        try
        {
            if (code != 0 || status != 3) // NetSetupDomainName
                throw new InvalidOperationException(
                    "This Windows host is not joined to Active Directory. NetGetJoinInformation: " + code);
            return Marshal.PtrToStringUni(domainPtr) ?? "";
        }
        finally { if (domainPtr != IntPtr.Zero) NetApiBufferFree(domainPtr); }
    }

    private static void EnsureAdministrator()
    {
        if (!GmsaRepairQueue.IsAdministrator())
            throw new UnauthorizedAccessException(
                "gMSA installation or removal requires the manager to run as " +
                "a local administrator (Run as administrator).");
    }

    internal static CheckResult Check(Request request)
    {
        EnsureAdministrator();
        string account = request.Account.Trim();
        if (!account.EndsWith('$') || account.Count(x => x == '\\') != 1)
            throw new ArgumentException("Account must have the form DOMAIN\\gmsaLSCM$.");
        // Validation is shared with the unattended worker and rejects broader
        // DNs, SYSVOL/NETLOGON, unsafe EXE paths and malformed account names.
        var sample = new GmsaRepairConfig
        {
            Enabled = true, GmsaAccount = account,
            Domain = request.Domain.Trim().ToLowerInvariant(),
            OuDn = request.OuDn.Trim(), GroupDn = request.GroupDn.Trim(),
            FeedbackShare = FeedbackPath.ValidateRoot(request.ShareRoot),
            ExecutablePath = InstalledBinary, InstalledUtc = DateTimeOffset.UtcNow,
            GmsaSid = "S-1-5-18" // replaced with the resolved account below
        };
        var resolved = (SecurityIdentifier)new NTAccount(account).Translate(
            typeof(SecurityIdentifier));
        if (resolved.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
            resolved.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid))
            throw new UnauthorizedAccessException("A built-in system account is not a gMSA.");
        sample.GmsaSid = resolved.Value;
        sample.Validate();
        string joined = JoinedDomain(); // Windows returns the NetBIOS name here.
        // NetGetJoinInformation returns a NetBIOS domain (e.g. DOMAIN), not
        // necessarily the DNS domain (e.g. ad.example.test). Compare the DNS suffix
        // from Windows as well; the Netlogon account SID is checked separately.
        string dnsDomain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
        bool sameDomain =
            string.Equals(joined, sample.Domain, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dnsDomain, sample.Domain, StringComparison.OrdinalIgnoreCase);
        if (!sameDomain)
            throw new InvalidOperationException(
                $"Server domain: {joined} (DNS: {dnsDomain}); selected: {sample.Domain}. " +
                "Join this host to the selected AD DNS domain before installing gMSA.");
        string published = Environment.ProcessPath ?? "";
        if (!File.Exists(published) ||
            !Path.GetFileName(published).Equals(
                "LockScreenCampaignManager.exe", StringComparison.OrdinalIgnoreCase) ||
            published.StartsWith(@"\\", StringComparison.Ordinal) ||
            (File.GetAttributes(published) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException(
                "Launch the published LockScreenCampaignManager.exe from a local disk to install.");
        bool installed = AccountInstalled(account);
        return new CheckResult(account, resolved.Value, installed,
            installed
                ? "gMSA is installed in local Netlogon. Task registration is available."
                : "gMSA exists in AD but is not yet installed on this Windows host. " +
                  "The native installer will prepare it through the Netlogon API.");
    }

    private static void EnsureDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            if ((new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Refusing reparse-point/junction directory: " + path);
        }
        else if (File.Exists(path))
            throw new UnauthorizedAccessException("An existing file occupies the gMSA path: " + path);
        else
            Directory.CreateDirectory(path);
    }

    private static DirectorySecurity DirectoryAcl(SecurityIdentifier gmsa,
        FileSystemRights gmsaRights)
    {
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(Administrators);
        const InheritanceFlags inheritance =
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { LocalSystem, Administrators })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                inheritance, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(gmsa, gmsaRights,
            inheritance, PropagationFlags.None, AccessControlType.Allow));
        return acl;
    }

    private static FileSecurity FileAcl(SecurityIdentifier gmsa)
    {
        var acl = new FileSecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(Administrators);
        foreach (var sid in new[] { LocalSystem, Administrators })
            acl.AddAccessRule(new FileSystemAccessRule(sid,
                FileSystemRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(gmsa,
            FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        return acl;
    }

    private static void ProtectDirectory(string path, SecurityIdentifier sid,
        FileSystemRights permission)
    {
        EnsureDirectory(path);
        new DirectoryInfo(path).SetAccessControl(DirectoryAcl(sid, permission));
    }

    private static void ProtectFile(string path, SecurityIdentifier sid)
    {
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("gMSA EXE/config is a reparse point.");
        info.SetAccessControl(FileAcl(sid));
    }

    private static void SecurePaths(SecurityIdentifier sid)
    {
        string parent = Path.GetDirectoryName(GmsaRepairConfig.Root)
            ?? throw new InvalidDataException("Invalid ProgramData worker root");
        ProtectDirectory(parent, sid, FileSystemRights.ReadAndExecute);
        ProtectDirectory(GmsaRepairConfig.Root, sid, FileSystemRights.ReadAndExecute);
        ProtectDirectory(BinaryDirectory, sid, FileSystemRights.ReadAndExecute);
        ProtectDirectory(GmsaRepairConfig.QueueDirectory, sid, FileSystemRights.Modify);
        ProtectDirectory(GmsaRepairConfig.StatusDirectory, sid, FileSystemRights.Modify);
    }

    private static void WriteConfig(GmsaRepairConfig config, SecurityIdentifier sid)
    {
        string path = GmsaRepairConfig.ConfigPath;
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(config,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
                new UTF8Encoding(false));
            ProtectFile(temp, sid);
            File.Move(temp, path, overwrite: true);
            ProtectFile(path, sid);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static GmsaRepairConfig? ExistingConfig()
    {
        if (!File.Exists(GmsaRepairConfig.ConfigPath)) return null;
        // Do not trust an existing config before checking owner, inheritance,
        // writable principals and the entire protected path.
        GmsaRepairConfig.VerifyProtectedPath(Path.GetDirectoryName(
            GmsaRepairConfig.Root)!, directory: true);
        GmsaRepairConfig.VerifyProtectedPath(GmsaRepairConfig.Root, directory: true);
        GmsaRepairConfig.VerifyProtectedPath(GmsaRepairConfig.ConfigPath, directory: false);
        var value = JsonSerializer.Deserialize<GmsaRepairConfig>(
            File.ReadAllText(GmsaRepairConfig.ConfigPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Existing gMSA configuration is invalid.");
        return value;
    }

    private static dynamic SchedulerFolder()
    {
        Type type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)
            ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Cannot connect to Windows Task Scheduler.");
        service.Connect();
        return service.GetFolder("\\");
    }

    private static dynamic? TaskOrNull(dynamic folder, string name = TaskName)
    {
        // Enumerate including hidden tasks. A missing GetTask() may return
        // different HRESULTs across Windows builds. Never accidentally treat
        // access denied or an unknown Task Scheduler failure as "no task".
        // Enumeration also detects a tampered, hidden task with the same name.
        dynamic tasks = folder.GetTasks(1); // TASK_ENUM_HIDDEN
        int count = (int)tasks.Count;
        for (int i = 1; i <= count; i++)
        {
            dynamic task = tasks.Item(i); // COM collections are 1-based.
            if (string.Equals((string)task.Name, name,
                    StringComparison.OrdinalIgnoreCase))
                return task;
        }
        return null;
    }

    private static bool SameTask(dynamic task, GmsaRepairConfig config,
        string argument = "--gmsa-repair-once")
    {
        dynamic definition = task.Definition;
        string userId = (string)definition.Principal.UserId;
        return definition.Principal.LogonType == TaskLogonPassword &&
            (userId.Equals(config.GmsaAccount, StringComparison.OrdinalIgnoreCase) ||
             userId.Equals(config.GmsaSid, StringComparison.OrdinalIgnoreCase)) &&
            definition.Actions.Count == 1 &&
            string.Equals((string)definition.Actions.Item(1).Path,
                config.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals((string)definition.Actions.Item(1).Arguments,
                argument, StringComparison.Ordinal) &&
            (argument != "--gmsa-diagnose-once" ||
             (definition.Triggers.Count == 0 && definition.Settings.AllowDemandStart));
    }

    private static bool IsDiagnosticTaskReady(GmsaRepairConfig config)
    {
        try
        {
            dynamic? task = TaskOrNull(SchedulerFolder(), DiagnosticTaskName);
            return task is not null && task.Enabled &&
                SameTask(task, config, "--gmsa-diagnose-once");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Inspect gMSA diagnostic task");
            return false;
        }
    }

    private static void RegisterDiagnosticTask(GmsaRepairConfig config)
    {
        dynamic folder = SchedulerFolder();
        dynamic? oldTask = TaskOrNull(folder, DiagnosticTaskName);
        if (oldTask is not null &&
            !SameTask(oldTask, config, "--gmsa-diagnose-once"))
            throw new InvalidOperationException(
                "An unknown task already uses the gMSA diagnostic task name.");

        Type type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic scheduler = Activator.CreateInstance(type)!;
        scheduler.Connect();
        dynamic definition = scheduler.NewTask(0);
        definition.RegistrationInfo.Description =
            "LockScreen Campaign Manager read-only gMSA diagnostics (no Repair, on demand only).";
        definition.Principal.UserId = config.GmsaAccount;
        definition.Principal.LogonType = TaskLogonPassword;
        definition.Principal.RunLevel = TaskRunLevelLUA;
        definition.Settings.Enabled = true;
        definition.Settings.Hidden = false;
        definition.Settings.AllowDemandStart = true;
        definition.Settings.StartWhenAvailable = false;
        definition.Settings.MultipleInstances = 2; // IgnoreNew.
        definition.Settings.ExecutionTimeLimit = "PT3M";
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        // Zero triggers: can ONLY be invoked explicitly from an authorized GUI.
        dynamic action = definition.Actions.Create(0);
        action.Path = config.ExecutablePath;
        action.Arguments = "--gmsa-diagnose-once";
        action.WorkingDirectory = BinaryDirectory;
        folder.RegisterTaskDefinition(DiagnosticTaskName, definition,
            TaskCreateOrUpdate, config.GmsaAccount, null, TaskLogonPassword);
        if (!IsDiagnosticTaskReady(config))
            throw new InvalidOperationException(
                "Diagnostic task registration did not pass principal/action verification.");
    }

    private static void RegisterTask(GmsaRepairConfig config)
    {
        dynamic folder = SchedulerFolder();
        dynamic? existing = TaskOrNull(folder);
        if (existing is not null && !SameTask(existing, config))
            throw new InvalidOperationException(
                "An existing LSCM-gMSA Task Scheduler task uses a different " +
                "account or executable. The unknown task will not be overwritten.");
        // Use Task Scheduler COM directly; no scripting runtime required.
        Type type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic scheduler = Activator.CreateInstance(type)!;
        scheduler.Connect();
        dynamic task = scheduler.NewTask(0);
        task.RegistrationInfo.Description =
            "LockScreen Campaign Manager: approved Repair every 2 minutes and after boot delay.";
        task.Principal.UserId = config.GmsaAccount;
        task.Principal.LogonType = TaskLogonPassword;
        task.Principal.RunLevel = TaskRunLevelLUA;
        task.Settings.Enabled = true;
        task.Settings.Hidden = false;
        task.Settings.StartWhenAvailable = true;
        task.Settings.MultipleInstances = 2; // IgnoreNew
        task.Settings.ExecutionTimeLimit = "PT12M";
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        dynamic daily = task.Triggers.Create(GmsaWorkerSchedule.DailyTrigger);
        daily.Enabled = true;
        daily.StartBoundary = DateTime.Now.AddMinutes(2)
            .ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        daily.DaysInterval = 1;
        daily.Repetition.Interval = GmsaWorkerSchedule.Interval;
        daily.Repetition.Duration = GmsaWorkerSchedule.Duration;
        daily.Repetition.StopAtDurationEnd = false;
        // A boot trigger starts a recovery cycle after Windows has had time
        // to initialize network services. The daily trigger remains active
        // if the domain controller is still unreachable after the boot delay.
        dynamic boot = task.Triggers.Create(GmsaWorkerSchedule.BootTrigger);
        boot.Enabled = true;
        boot.Delay = GmsaWorkerSchedule.BootDelay;
        dynamic action = task.Actions.Create(0);
        action.Path = config.ExecutablePath;
        action.Arguments = "--gmsa-repair-once";
        action.WorkingDirectory = BinaryDirectory;
        // gMSA is a special password-managed Windows account. NULL is sent
        // as the VARIANT password; there is no app-owned secret to collect.
        folder.RegisterTaskDefinition(TaskName, task, TaskCreateOrUpdate,
            config.GmsaAccount, null, TaskLogonPassword);
        if (!GmsaRepairConfig.IsScheduledTaskReady(config))
            throw new InvalidOperationException("gMSA Scheduled Task did not verify after registration.");
    }

    internal static GmsaRepairConfig Install(Request request)
    {
        var check = Check(request);
        string source = Environment.ProcessPath!;
        var sid = new SecurityIdentifier(check.Sid);
        EnsureAccountInstalled(check.Account);
        var config = new GmsaRepairConfig
        {
            Enabled = true, SchemaVersion = 1, GmsaAccount = check.Account,
            GmsaSid = check.Sid, Domain = request.Domain.Trim().ToLowerInvariant(),
            OuDn = request.OuDn.Trim(), GroupDn = request.GroupDn.Trim(),
            FeedbackShare = FeedbackPath.ValidateRoot(request.ShareRoot),
            ExecutablePath = InstalledBinary,
            InstalledUtc = DateTimeOffset.UtcNow
        };
        config.Validate();
        // DO NOT reauthorize older RPC requests by changing InstalledUtc.
        var previous = ExistingConfig();
        if (previous is not null)
        {
            if (!string.Equals(previous.GmsaSid, config.GmsaSid, StringComparison.Ordinal) ||
                !string.Equals(previous.Domain, config.Domain, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previous.OuDn, config.OuDn, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previous.GroupDn, config.GroupDn, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previous.ExecutablePath, config.ExecutablePath,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The existing gMSA installation uses a different SID, domain, OU, or group. " +
                    "Review the existing queue before reinstalling.");
            config.InstalledUtc = previous.InstalledUtc;
            if (config.InstalledUtc.Year < 2025)
                throw new InvalidDataException("Saved gMSA approval date is invalid.");
        }
        else if (File.Exists(Path.Combine(GmsaRepairConfig.QueueDirectory, "queue.json")))
            throw new InvalidOperationException(
                "An old queue exists without trusted configuration. Installation stopped.");

        SecurePaths(sid);
        if ((new FileInfo(source).Attributes & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Source executable must not be a link.");
        if (!Path.GetFullPath(source).Equals(InstalledBinary,
                StringComparison.OrdinalIgnoreCase))
        {
            var tempExe = InstalledBinary + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, tempExe, overwrite: false);
                ProtectFile(tempExe, sid);
                File.Move(tempExe, InstalledBinary, overwrite: true);
            }
            finally { if (File.Exists(tempExe)) File.Delete(tempExe); }
        }
        ProtectFile(InstalledBinary, sid);
        // Install is fail-closed: on any registration error the config is
        // disabled, even when Task Scheduler accepted an ambiguous request.
        config.Enabled = false;
        WriteConfig(config, sid);
        try
        {
            config.Enabled = true;
            RegisterTask(config);
            RegisterDiagnosticTask(config);
            WriteConfig(config, sid);
            return GmsaRepairConfig.Load();
        }
        catch
        {
            config.Enabled = false;
            WriteConfig(config, sid);
            try
            {
                dynamic? task = TaskOrNull(SchedulerFolder());
                if (task is not null && SameTask(task, config)) task!.Enabled = false;
                dynamic? diagnostic = TaskOrNull(SchedulerFolder(), DiagnosticTaskName);
                if (diagnostic is not null &&
                    SameTask(diagnostic, config, "--gmsa-diagnose-once"))
                    diagnostic!.Enabled = false;
            }
            catch (Exception ex) { AppLog.Write(ex, "Disable failed gMSA task"); }
            throw;
        }
    }

    internal static void Disable()
    {
        EnsureAdministrator();
        var config = ExistingConfig() ?? throw new InvalidOperationException(
            "gMSA is not installed on this server.");
        var sid = new SecurityIdentifier(config.GmsaSid);
        config.Enabled = false;
        WriteConfig(config, sid); // revoke permission BEFORE disabling the task
        dynamic folder = SchedulerFolder();
        dynamic? task = TaskOrNull(folder);
        if (task is not null)
        {
            if (!SameTask(task, config))
                throw new InvalidOperationException(
                    "An unknown task has this name. Only local authorization was disabled.");
            task.Enabled = false;
            try { task.Stop(0); } catch (COMException) { }
        }
        dynamic? diagnosticTask = TaskOrNull(folder, DiagnosticTaskName);
        if (diagnosticTask is not null)
        {
            if (!SameTask(diagnosticTask, config, "--gmsa-diagnose-once"))
                throw new InvalidOperationException(
                    "Unknown diagnostic task detected; only main worker was disabled.");
            diagnosticTask.Enabled = false;
            try { diagnosticTask.Stop(0); } catch (COMException) { }
        }
        // Keep the protected queue and original install time for auditing.
    }

    internal static void RemoveTask()
    {
        Disable();
        dynamic folder = SchedulerFolder();
        var config = ExistingConfig() ?? throw new InvalidOperationException(
            "Missing gMSA configuration during task removal.");
        dynamic? diagnostic = TaskOrNull(folder, DiagnosticTaskName);
        if (diagnostic is not null)
        {
            if (!SameTask(diagnostic, config, "--gmsa-diagnose-once"))
                throw new InvalidOperationException(
                    "Diagnostic task changed unexpectedly; refusing to delete it.");
            folder.DeleteTask(DiagnosticTaskName, 0);
        }
        dynamic? task = TaskOrNull(folder);
        if (task is not null)
        {
            if (!SameTask(task, config))
                throw new InvalidOperationException(
                    "Repair task changed unexpectedly; refusing to delete it.");
            folder.DeleteTask(TaskName, 0);
        }
    }

    internal static string RunDiagnostic(Request request)
    {
        EnsureAdministrator();
        var checkedAccount = Check(request);
        var config = GmsaRepairConfig.Load();
        if (!string.Equals(config.GmsaSid, checkedAccount.Sid, StringComparison.Ordinal) ||
            !config.ScopeMatches(request.Domain, request.OuDn, request.GroupDn) ||
            !string.Equals(config.FeedbackShare, FeedbackPath.ValidateRoot(request.ShareRoot),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The requested diagnostic account or AD scope does not match the installed gMSA.");
        if (!GmsaRepairConfig.IsScheduledTaskReady(config) ||
            !IsDiagnosticTaskReady(config))
            throw new InvalidOperationException(
                "gMSA Task Scheduler registration is not ready. Use Install / Repair first.");

        dynamic task = TaskOrNull(SchedulerFolder(), DiagnosticTaskName)
            ?? throw new InvalidOperationException("gMSA diagnostic task is missing.");
        if (!SameTask(task, config, "--gmsa-diagnose-once"))
            throw new UnauthorizedAccessException("gMSA diagnostic task identity changed.");
        DateTimeOffset started = DateTimeOffset.UtcNow;
        // Run() only starts a separate read-only diagnostic process using the
        // configured gMSA. It cannot execute Remote Repair or modify the queue.
        task.Run(null);
        for (int i = 0; i < 32; i++)
        {
            System.Threading.Thread.Sleep(300); // Run from Task.Run, not the UI thread.
            var report = GmsaDiagnosticReport.Load();
            if (GmsaDiagnosticCheck.BelongsTo(report, config) &&
                report!.StartedUtc >= started &&
                report.CompletedUtc >= report.StartedUtc)
                return report.Format();
        }
        return "Diagnostic task was requested. No fresh report received yet. " +
            "Use Check to inspect Task Scheduler and the diagnostic report. " +
            "A queued task alone does not verify a successful gMSA logon.";
    }

    internal static string Inspect(Request request)
    {
        var check = Check(request);
        var config = GmsaRepairConfig.TryReadForInspection();
        var status = GmsaRepairStatus.LoadForScope(config);
        var result = new StringBuilder(check.Message);
        result.AppendLine();
        result.AppendLine("Configuration: " + (config is null ? "not installed / invalid" :
            config.Enabled ? "enabled" : "disabled"));
        if (config is not null)
        {
            bool correctlyRegistered = GmsaRepairConfig.IsScheduledTaskReady(config);
            result.AppendLine("Task verification: " +
                (correctlyRegistered ? "registered: 2-minute cycle + delayed startup" :
                    "not ready / disabled / missing boot trigger / incorrect identity"));
            try
            {
                dynamic folder = SchedulerFolder();
                dynamic? task = TaskOrNull(folder);
                if (task is not null)
                {
                    result.AppendLine("Task Scheduler state: " + (int)task.State);
                    result.AppendLine("Last Task Scheduler result: 0x" +
                        unchecked((uint)(int)task.LastTaskResult).ToString("X8",
                            CultureInfo.InvariantCulture));
                    DateTime last = (DateTime)task.LastRunTime;
                    DateTime next = (DateTime)task.NextRunTime;
                    if (last.Year > 2000)
                        result.AppendLine("Last task start: " + last.ToString("G"));
                    if (next.Year > 2000)
                        result.AppendLine("Next scheduled start: " + next.ToString("G"));
                }
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "Inspect gMSA Task Scheduler result");
                result.AppendLine("Scheduler diagnostics could not be read.");
            }
        }
        result.AppendLine("Last worker heartbeat: " +
            (status.LastRunUtc.Year > 2020
                ? status.LastRunUtc.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss")
                : "never observed"));
        result.AppendLine("Worker health: " + GmsaRepairStatus.Classify(
            status, DateTimeOffset.UtcNow,
            config is not null && config.Enabled &&
            GmsaRepairConfig.IsScheduledTaskReady(config)));
        result.AppendLine("Worker result: " + status.Summary);
        result.AppendLine("Diagnostic task: " +
            (config is not null && IsDiagnosticTaskReady(config) ? "registered (on demand)" :
                "not registered / disabled"));
        var diagnostic = GmsaDiagnosticReport.Load();
        if (config is not null && diagnostic is not null)
        {
            if (!GmsaDiagnosticCheck.BelongsTo(diagnostic, config))
                result.AppendLine("Previous diagnostic belongs to another gMSA scope; rerun test.");
            else if (diagnostic.StartedUtc < config.InstalledUtc ||
                     diagnostic.CompletedUtc < DateTimeOffset.UtcNow.AddHours(-1))
                result.AppendLine("Previous diagnostic is outdated; rerun gMSA test.");
            else
            {
                result.AppendLine();
                result.AppendLine(diagnostic.Format());
            }
        }
        result.AppendLine("Task registered is not proof of successful gMSA sign-in or Repair.");
        return result.ToString();
    }
}
