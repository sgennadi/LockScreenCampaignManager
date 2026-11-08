using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace LockScreenCampaignManager;

/// <summary>
/// Built-in Windows Task Scheduler (not RSAT, not an external service).
/// Uses TASK_LOGON_INTERACTIVE_TOKEN: only runs while the SAME Windows user is
/// logged on. Passwords and alternate credentials are never persisted.
/// </summary>
internal static class BackgroundRepairTask
{
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private static string Name => "LSCM-BackgroundRepair-" + BackgroundRepairIdentity.CurrentSid();

    internal static string CurrentExecutable()
    {
        string path = Environment.ProcessPath ?? "";
        if (!File.Exists(path) ||
            !Path.GetFileName(path).Equals("LockScreenCampaignManager.exe",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Background Repair requires the published LockScreenCampaignManager.exe. " +
                "Do not install a scheduled task from dotnet run.");
        return path;
    }

    private static dynamic OpenService()
    {
        Type type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)
            ?? throw new InvalidOperationException("Windows Task Scheduler COM is missing.");
        dynamic service = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Cannot create Task Scheduler COM service.");
        service.Connect();
        return service;
    }

    internal static void Install(string executable)
    {
        if (!string.Equals(Path.GetFullPath(executable), Path.GetFullPath(CurrentExecutable()),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Worker executable path does not match the running manager.");
        dynamic service = OpenService();
        dynamic folder = service.GetFolder("\\");
        dynamic task = service.NewTask(0);
        task.RegistrationInfo.Description =
            "Opt-in LockScreen Campaign Manager repair status and limited retry. " +
            "Runs only while the approving Windows account is logged on.";
        task.Principal.UserId = WindowsIdentity.GetCurrent().Name;
        task.Principal.LogonType = TaskLogonInteractiveToken;
        task.Principal.RunLevel = 1; // Highest available interactive user token.
        task.Settings.Enabled = true;
        task.Settings.Hidden = false; // Admin can audit and disable in Task Scheduler.
        task.Settings.StartWhenAvailable = true;
        task.Settings.AllowDemandStart = true;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.MultipleInstances = 2; // IgnoreNew: no concurrent scheduler instances.
        task.Settings.ExecutionTimeLimit = "PT12M";
        dynamic trigger = task.Triggers.Create(2); // TASK_TRIGGER_DAILY
        trigger.DaysInterval = 1;
        trigger.StartBoundary = DateTime.Now.AddMinutes(1)
            .ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        trigger.Repetition.Interval = "PT2M";
        trigger.Repetition.Duration = "P1D";
        trigger.Repetition.StopAtDurationEnd = false;
        dynamic action = task.Actions.Create(0); // TASK_ACTION_EXEC
        action.Path = executable;
        action.Arguments = "--background-repair-once";
        action.WorkingDirectory = Path.GetDirectoryName(executable) ?? "";
        // TASK_CREATE_OR_UPDATE: Windows will use the caller's current logon
        // token. No saved password and NO S4U/Run whether user is logged off.
        folder.RegisterTaskDefinition(Name, task, TaskCreateOrUpdate,
            WindowsIdentity.GetCurrent().Name, null, TaskLogonInteractiveToken);
    }

    internal static void Uninstall()
    {
        dynamic service = OpenService();
        dynamic folder = service.GetFolder("\\");
        try { folder.DeleteTask(Name, 0); }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002)) { }
    }

    internal static bool IsInstalled()
    {
        try
        {
            dynamic service = OpenService();
            dynamic folder = service.GetFolder("\\");
            return folder.GetTask(Name) is not null;
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002))
        {
            return false;
        }
    }
}
