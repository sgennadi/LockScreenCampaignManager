using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

internal static partial class NativeClientAgent
{
    private static void RegisterCampaignTask(Context ctx)
    {
        var triggers = new List<DateTime>(ctx.Starts) { ctx.End };
        WithTaskScheduler((scheduler, root) =>
        {
            dynamic definition = scheduler.NewTask(0);
            try
            {
                SetTaskPrincipalAndAction(definition, ctx.LocalExe,
                    "--client-agent Apply", "Scheduled lock-screen image changes");
                dynamic boot = definition.Triggers.Create(8); // TASK_TRIGGER_BOOT
                boot.Enabled = true;
                ReleaseCom(boot);
                foreach (DateTime at in triggers)
                {
                    DateTime local = TimeZoneInfo.ConvertTime(at,
                        ctx.Zone, TimeZoneInfo.Local);
                    dynamic item = definition.Triggers.Create(1); // TASK_TRIGGER_TIME
                    item.StartBoundary = local.ToString("yyyy-MM-ddTHH:mm:ss",
                        CultureInfo.InvariantCulture);
                    item.Enabled = true;
                    ReleaseCom(item);
                }
                // Daily recovery after an accidental image change, even if
                // a machine missed its one-shot trigger or is offline.
                dynamic daily = definition.Triggers.Create(2);
                daily.StartBoundary = DateTime.Today.AddHours(3).ToString(
                    "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                daily.DaysInterval = 1;
                daily.Enabled = true;
                ReleaseCom(daily);
                RegisterTask(root, ctx.TaskName, definition, ctx.LocalExe,
                    "--client-agent Apply");
            }
            finally { ReleaseCom(definition); }
        });
    }

    private static void RegisterFeedbackTask(Context ctx)
    {
        var feedback = ctx.Config.Feedback;
        if (feedback is not { Enabled: true }) return;
        ValidateFeedback(ctx);
        WithTaskScheduler((scheduler, root) =>
        {
            dynamic definition = scheduler.NewTask(0);
            try
            {
                SetTaskPrincipalAndAction(definition, ctx.LocalExe,
                    "--client-agent Report", "Lock-screen campaign health heartbeat");
                dynamic trigger = definition.Triggers.Create(1);
                trigger.StartBoundary = DateTime.Now.AddMinutes(2).ToString(
                    "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                trigger.Repetition.Interval = "PT" +
                    feedback.IntervalMinutes.ToString(CultureInfo.InvariantCulture) + "M";
                trigger.Repetition.Duration = "P45D";
                trigger.Enabled = true;
                ReleaseCom(trigger);
                RegisterTask(root, ctx.FeedbackTaskName, definition,
                    ctx.LocalExe, "--client-agent Report");
            }
            finally { ReleaseCom(definition); }
        });
    }

    private static void UnregisterCampaignTasks(Context ctx)
    {
        WithTaskScheduler((scheduler, root) =>
        {
            DeleteTaskIfOwned(root, ctx.TaskName, ctx.LocalExe,
                "--client-agent Apply");
            DeleteTaskIfOwned(root, ctx.FeedbackTaskName, ctx.LocalExe,
                "--client-agent Report");
        });
    }

    private static bool TaskExists(Context ctx)
    {
        bool found = false;
        WithTaskScheduler((scheduler, root) =>
        {
            object? task = GetTaskOrNull(root, ctx.TaskName);
            found = task is not null;
            ReleaseCom(task);
        });
        return found;
    }

    private static bool CampaignTasksGone(Context ctx)
    {
        bool gone = false;
        WithTaskScheduler((scheduler, root) =>
        {
            object? main = GetTaskOrNull(root, ctx.TaskName);
            object? feedback = GetTaskOrNull(root, ctx.FeedbackTaskName);
            try { gone = main is null && feedback is null; }
            finally
            {
                ReleaseCom(main);
                ReleaseCom(feedback);
            }
        });
        return gone;
    }

    private static void WithTaskScheduler(Action<dynamic, dynamic> act)
    {
        Type type = Type.GetTypeFromProgID("Schedule.Service") ??
            throw new InvalidOperationException("Windows Task Scheduler COM is unavailable.");
        object service = Activator.CreateInstance(type) ??
            throw new InvalidOperationException("Cannot instantiate Task Scheduler.");
        object? root = null;
        try
        {
            dynamic scheduler = service;
            scheduler.Connect();
            root = scheduler.GetFolder(@"\");
            // Tasks must be created and registered using the SAME connected
            // Task Scheduler service session, not an unrelated COM instance.
            act(scheduler, (dynamic)root);
        }
        finally { ReleaseCom(root); ReleaseCom(service); }
    }

    private static void SetTaskPrincipalAndAction(dynamic definition,
        string exe, string args, string description)
    {
        definition.RegistrationInfo.Description = description;
        definition.Principal.UserId = "SYSTEM";
        definition.Principal.LogonType = 5;  // TASK_LOGON_SERVICE_ACCOUNT
        definition.Principal.RunLevel = 1;   // TASK_RUNLEVEL_HIGHEST
        definition.Settings.Enabled = true;
        definition.Settings.Hidden = true;
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.AllowDemandStart = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.MultipleInstances = 2; // IgnoreNew
        definition.Settings.ExecutionTimeLimit = "PT10M";
        dynamic exec = definition.Actions.Create(0); // TASK_ACTION_EXEC
        exec.Path = exe;
        exec.Arguments = args;
        ReleaseCom(exec);
    }

    private static void RegisterTask(dynamic root, string name, dynamic definition,
        string requiredPath, string requiredArgs)
    {
        object? existing = GetTaskOrNull(root, name);
        if (existing is not null)
        {
            try { VerifyOwnedTask(existing, requiredPath, requiredArgs); }
            finally { ReleaseCom(existing); }
        }
        object registered = root.RegisterTaskDefinition(
            name, definition, 6, "SYSTEM", null, 5); // CREATE_OR_UPDATE
        try { VerifyOwnedTask(registered, requiredPath, requiredArgs); }
        finally { ReleaseCom(registered); }
    }

    private static object? GetTaskOrNull(dynamic root, string name)
    {
        try { return root.GetTask(name); }
        catch (COMException ex) when (unchecked((uint)ex.HResult) is
            0x80070002u or 0x8004130Fu)
        { return null; }
    }

    private static void DeleteTaskIfOwned(dynamic root, string name,
        string expectedPath, string expectedArgs)
    {
        object? task = GetTaskOrNull(root, name);
        if (task is null) return;
        try { VerifyOwnedTask(task, expectedPath, expectedArgs); }
        finally { ReleaseCom(task); }
        root.DeleteTask(name, 0);
    }

    private static void VerifyOwnedTask(object task, string path, string args)
    {
        dynamic registered = task;
        dynamic definition = registered.Definition;
        dynamic actions = definition.Actions;
        dynamic action = actions.Item(1);
        try
        {
            if (!string.Equals((string)action.Path, path,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals((string)action.Arguments, args,
                    StringComparison.Ordinal) ||
                !string.Equals((string)definition.Principal.UserId,
                    "SYSTEM", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "An existing scheduled task is not the managed native campaign task.");
        }
        finally
        {
            ReleaseCom(action);
            ReleaseCom(actions);
            ReleaseCom(definition);
        }
    }

    private static void ReleaseCom(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
            try { _ = Marshal.ReleaseComObject(instance); }
            catch (COMException) { }
    }
}
