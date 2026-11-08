using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Native Windows Task Scheduler RPC, with no PowerShell hosted in the manager.
/// The trusted LockScreen-Agent.ps1 still runs remotely as SYSTEM on the client.
/// A timeout after task registration is ambiguous and is NEVER auto-retried.
/// </summary>
internal static class NativeRemoteTaskScheduler
{
    internal sealed record Result(string host, string status, int? exit,
        string? reason, string? hresult, string note);

    private static readonly Regex HostName = new(
        @"\A[A-Za-z0-9][A-Za-z0-9.-]{1,253}\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex CampaignId = new(
        @"\A[a-z0-9][a-z0-9_-]{0,70}\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static Result Run(string host, string script, string mode, string id)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Task Scheduler COM requires Windows.");
        if (!HostName.IsMatch(host) || !CampaignId.IsMatch(id) ||
            mode is not ("Install" or "Repair" or "Report" or "Cleanup") ||
            script.Any(char.IsControl) ||
            !script.EndsWith(@"\LockScreen-Agent.ps1", StringComparison.OrdinalIgnoreCase) ||
            !(script.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) ||
              script.StartsWith(@"C:\ProgramData\LockScreenCampaigns\", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Untrusted remote task identity or script path.");

        object? service = null, folder = null, task = null, info = null,
            principal = null, settings = null, actions = null,
            action = null, registered = null;
        bool registrationAttempted = false;
        try
        {
            Type server = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true) ??
                throw new InvalidOperationException("Windows Task Scheduler COM is unavailable.");
            service = Activator.CreateInstance(server) ??
                throw new InvalidOperationException("Could not create Task Scheduler COM.");
            Call(service, "Connect", host);
            folder = Call(service, "GetFolder", @"\") ??
                throw new InvalidOperationException("Remote Task Scheduler root unavailable.");
            string taskName = "LSCM-Now-" + id;

            // A prior task can be running or completed but not yet collected.
            // Never replace it or assume it is safe to repeat an action.
            object? old = null;
            try
            {
                old = Call(folder, "GetTask", taskName);
                throw new InvalidOperationException(
                    "An earlier one-time LSCM task exists. Inspect it before retry.");
            }
            catch (Exception ex) when (TaskAbsent(ex)) { }
            finally { Release(old); }

            task = Call(service, "NewTask", 0) ??
                throw new InvalidOperationException("Cannot create remote task definition.");
            info = Property(task, "RegistrationInfo");
            Set(info, "Description", "LSCM approved remote campaign " + mode);
            principal = Property(task, "Principal");
            Set(principal, "UserId", "SYSTEM");
            Set(principal, "LogonType", 5); // TASK_LOGON_SERVICE_ACCOUNT
            Set(principal, "RunLevel", 1); // TASK_RUNLEVEL_HIGHEST
            settings = Property(task, "Settings");
            Set(settings, "Enabled", true);
            Set(settings, "Hidden", true);
            Set(settings, "AllowDemandStart", true);
            Set(settings, "StartWhenAvailable", true);
            Set(settings, "ExecutionTimeLimit", "PT10M");
            Set(settings, "DisallowStartIfOnBatteries", false);
            actions = Property(task, "Actions");
            action = Call(actions, "Create", 0) ??
                throw new InvalidOperationException("Cannot create Task Scheduler action.");
            Set(action, "Path",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"System32\WindowsPowerShell\v1.0\powershell.exe"));
            Set(action, "Arguments",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" +
                script + "\" -Mode " + mode);

            // After this point a transport failure can mean the task WAS
            // created or executed: return Pending rather than Offline.
            registrationAttempted = true;
            registered = Call(folder, "RegisterTaskDefinition", taskName, task, 2,
                "SYSTEM", null!, 5) ??
                throw new InvalidOperationException("Task registration produced no result.");
            _ = Call(registered, "Run", null!);
            var until = DateTime.UtcNow.AddSeconds(80);
            bool started = false, running = true;
            do
            {
                Thread.Sleep(2000);
                Release(registered);
                registered = Call(folder, "GetTask", taskName) ??
                    throw new InvalidOperationException("Registered remote task disappeared.");
                object? at = Property(registered, "LastRunTime");
                if (at is DateTime last && last > DateTime.Now.AddMinutes(-5))
                    started = true;
                running = Convert.ToInt32(Property(registered, "State"),
                    CultureInfo.InvariantCulture) == 4;
            } while (DateTime.UtcNow < until && (running || !started));

            if (running || !started)
                return new Result(host, "pending", null, null, null,
                    "Remote task still running or not started; left for inspection.");

            int exit = Convert.ToInt32(Property(registered, "LastTaskResult"),
                CultureInfo.InvariantCulture);
            Call(folder, "DeleteTask", taskName, 0);
            return new Result(host, exit == 0 ? "success" : "failed", exit,
                null, null,
                "Remote task completed and one-time task deleted. Client campaign tasks may remain.");
        }
        catch (Exception ex)
        {
            if (registrationAttempted)
                return new Result(host, "pending", null, null, null,
                    "Task registration or execution is uncertain. Inspect remote Scheduler; do not retry automatically.");

            int hr = Unwrap(ex).HResult;
            bool offline = IsOffline(hr);
            return new Result(host, offline ? "offline" : "failed", null,
                offline ? "RPC endpoint temporarily unreachable" :
                    "RPC, credentials, task or campaign error",
                "0x" + unchecked((uint)hr).ToString("X8", CultureInfo.InvariantCulture),
                "Task Scheduler RPC: " + Unwrap(ex).GetType().Name +
                (offline ? "; eligible for deferred retry" : "; investigate manually"));
        }
        finally
        {
            foreach (object? item in new[] { registered, action, actions,
                         settings, principal, info, task, folder, service })
                Release(item);
        }
    }

    private static bool TaskAbsent(Exception ex)
    {
        uint code = unchecked((uint)Unwrap(ex).HResult);
        return code is 0x80070002 or 0x8004130F;
    }

    private static bool IsOffline(int hr)
    {
        uint code = unchecked((uint)hr);
        return code is 0x800706BA or 0x800706BF or 0x80070035 or
            0x80070043 or 0x800704CF or 0x800705B4 or
            0x80070079 or 0x80070040 or 0x80072EE7;
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException tie && tie.InnerException is not null)
            ex = tie.InnerException;
        return ex;
    }

    private static object? Call(object target, string name, params object[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod,
            null, target, args, CultureInfo.InvariantCulture);

    private static object Property(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty,
            null, target, null, CultureInfo.InvariantCulture) ??
        throw new InvalidOperationException("Task Scheduler property missing: " + name);

    private static void Set(object target, string name, object value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty,
            null, target, new[] { value }, CultureInfo.InvariantCulture);

    private static void Release(object? obj)
    {
        try
        {
            if (obj is not null && Marshal.IsComObject(obj))
                _ = Marshal.FinalReleaseComObject(obj);
        }
        catch (Exception ex) { AppLog.Write(ex, "Task Scheduler COM release"); }
    }
}
