using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// Guarded native Task Scheduler RPC for approved Push, Repair, Report and
/// Cleanup actions. Every execution recomputes the native AD/SYSVOL Preview
/// and exact confirmation hash. Registration and subsequent RPC failures are
/// ambiguous and must NEVER be treated as offline-eligible auto-retry.
/// </summary>
internal static class NativeImmediateApply
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex Host = new(
        @"\A[A-Za-z0-9][A-Za-z0-9.-]{1,253}\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private sealed record Result(string host, string status, int? exit,
        string note, string? reason = null, string? hresult = null);

    internal static JsonElement Execute(string mode, object request,
        DomainCredentials credentials)
    {
        if (mode is not ("Push" or "Repair" or "Report" or "Cleanup"))
            throw new ArgumentException("Unsupported native immediate action.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Task Scheduler RPC is required.");
        JsonElement args = JsonSerializer.SerializeToElement(request, Json);
        string purpose = Get(args, "purpose");
        if (purpose.Length == 0) purpose = "Push";
        if (!string.Equals(mode, purpose, StringComparison.Ordinal))
            throw new InvalidOperationException("Remote action differs from approved preview.");

        JsonElement Run()
        {
            // Native Preview independently validates AD domain, signed LDAP
            // OU/group membership, managed GPO, SYSVOL campaign and clock.
            var verified = NativeImmediateActions.Execute("Preview", request,
                new DomainCredentials(Get(args, "domain"), "", ""));
            var data = verified.GetProperty("data");
            string actualHash = Required(data, "hash");
            Guid approved = Guid.Parse(Required(data, "gpoId"));
            if (!Guid.TryParse(Get(args, "approveGuid"), out Guid confirmedGuid) ||
                confirmedGuid != approved ||
                !string.Equals(Get(args, "planHash"), actualHash, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Remote action confirmation mismatch; refresh Preview and exact target list.");
            string id = Required(data, "campaignId");
            if (!NativeSysvolStorage.ValidCampaignId(id) ||
                !string.Equals(Required(data, "purpose"), mode, StringComparison.Ordinal))
                throw new InvalidDataException("Managed campaign or purpose changed.");
            string domain = Get(args, "domain").Trim().ToLowerInvariant();
            var hosts = data.GetProperty("computers").EnumerateArray()
                .Select(x => x.GetString() ?? "").ToArray();
            if (hosts.Length is < 1 or > 25 ||
                hosts.Any(x => !Host.IsMatch(x)) ||
                hosts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != hosts.Length)
                throw new InvalidDataException("Native approved client list is invalid.");

            // Recheck the pinned client method on the selected DC.
            // Never attempt to execute an EXE as a PowerShell script.
            string dc = Get(args, "dc").Trim();
            NativeAdDiscovery.ValidateNames(domain, dc);
            string manifest = @"\\" + dc + @"\SYSVOL\" + domain +
                @"\scripts\LockScreenCampaigns\" + id + @"\campaign.json";
            using var doc = JsonDocument.Parse(File.ReadAllBytes(manifest),
                new JsonDocumentOptions { MaxDepth = 12 });
            if (doc.RootElement.GetProperty("campaignId").GetString() != id)
                throw new InvalidDataException("Remote campaign identity mismatch.");
            var clientType = NativeGpoDeploymentPlan.ParseClientType(
                doc.RootElement.TryGetProperty("clientType", out var typeField) &&
                typeField.ValueKind == JsonValueKind.String
                    ? typeField.GetString() ?? "" : "");
            string agentLeaf = clientType == CampaignClientType.Executable
                ? "LockScreen-Agent.exe" : "LockScreen-Agent.ps1";
            string script = mode is "Push" or "Repair"
                ? @"\\" + domain + @"\SYSVOL\" + domain +
                    @"\scripts\LockScreenCampaigns\" + id + @"\" + agentLeaf
                : @"C:\ProgramData\LockScreenCampaigns\" + id + @"\" + agentLeaf;
            string agentMode = mode == "Push" ? "Install" : mode;
            var answers = new List<Result>(hosts.Length);
            foreach (string host in hosts)
            {
                try { answers.Add(ScheduleOne(host, script, agentMode, id, clientType)); }
                catch (Exception ex)
                {
                    bool offline = IsKnownOffline(ex);
                    answers.Add(new Result(host, offline ? "offline" : "failed",
                        null, "Task Scheduler RPC: " + ex.GetType().Name +
                        (offline ? "; eligible for deferred retry" :
                         "; manual investigation required"),
                        offline ? "RPC endpoint temporarily unreachable" :
                            "RPC, credentials, task or campaign error",
                        "0x" + ((uint)ex.HResult).ToString("X8")));
                }
            }

            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                message = "Native remote task action finished. Inspect every host result and verify the client.",
                data = new
                {
                    results = answers,
                    successful = answers.Count(x => x.status == "success"),
                    pending = answers.Count(x => x.status == "pending"),
                    offline = answers.Count(x => x.status == "offline"),
                    failed = answers.Count(x => x.status == "failed"),
                    note = "One-time task state is recorded per host. Visible lock screen changes at the next lock/logon."
                }
            }, Json);
        }

        if (!credentials.IsAlternate) return Run();
        string username = credentials.UserName.Trim();
        string? userDomain = null;
        int slash = username.IndexOf('\\');
        if (slash > 0 && slash < username.Length - 1)
        {
            userDomain = username[..slash];
            username = username[(slash + 1)..];
        }
        else if (!username.Contains('@'))
            username += "@" + credentials.Domain.Trim();
        if (credentials.Password.Length == 0 ||
            !LogonUser(username, userDomain, credentials.Password, 9, 3,
                out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot impersonate alternate network account for Task Scheduler RPC.");
        using (token) return WindowsIdentity.RunImpersonated(token, Run);
    }

    private static Result ScheduleOne(string host, string agent, string agentMode,
        string id, CampaignClientType clientType)
    {
        if (!Host.IsMatch(host) || !NativeSysvolStorage.ValidCampaignId(id))
            throw new ArgumentException("Invalid approved remote client identity.");
        object? com = null, folder = null, registered = null, task = null;
        bool attempted = false;
        string taskName = "LSCM-Now-" + id;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service")
                ?? throw new COMException("Windows Task Scheduler COM is unavailable.");
            com = Activator.CreateInstance(type)
                ?? throw new COMException("Task Scheduler COM could not be created.");
            dynamic svc = com;
            svc.Connect(host);
            folder = svc.GetFolder("\\");
            dynamic root = folder;
            try
            {
                object old = root.GetTask(taskName);
                if (old is not null)
                {
                    Release(old);
                    throw new InvalidOperationException(
                        "A one-time campaign task already exists. Inspect target before retry.");
                }
            }
            catch (COMException e) when ((uint)e.HResult == 0x80070002u) { }

            task = svc.NewTask(0);
            dynamic definition = task;
            definition.RegistrationInfo.Description = "LSCM approved remote campaign " + agentMode;
            definition.Principal.UserId = "SYSTEM";
            definition.Principal.LogonType = 5;
            definition.Principal.RunLevel = 1;
            definition.Settings.Enabled = true;
            definition.Settings.Hidden = true;
            definition.Settings.AllowDemandStart = true;
            definition.Settings.StartWhenAvailable = true;
            definition.Settings.ExecutionTimeLimit = "PT10M";
            definition.Settings.DisallowStartIfOnBatteries = false;
            dynamic exec = definition.Actions.Create(0);
            if (clientType == CampaignClientType.Executable)
            {
                exec.Path = agent;
                exec.Arguments = "--client-agent " + agentMode;
            }
            else
            {
                exec.Path = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell",
                    "v1.0", "powershell.exe");
                exec.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass " +
                    "-WindowStyle Hidden -File \"" + agent + "\" -Mode " + agentMode;
            }
            Release(exec);

            // This is the irreversible boundary: a lost RPC response after
            // registration cannot establish whether task creation succeeded.
            attempted = true;
            registered = root.RegisterTaskDefinition(
                taskName, definition, 2, "SYSTEM", null, 5);
            dynamic runningTask = registered;
            runningTask.Run(null);
            DateTime deadline = DateTime.UtcNow.AddSeconds(80);
            bool seenStarted = false, running = true;
            do
            {
                Thread.Sleep(2000);
                object latest = root.GetTask(taskName);
                Release(registered);
                registered = latest;
                dynamic current = latest;
                try
                {
                    DateTime lastRun = (DateTime)current.LastRunTime;
                    if (lastRun > DateTime.Now.AddMinutes(-5))
                        seenStarted = true;
                }
                catch (Exception) { /* Non-started task: wait until deadline. */ }
                running = (int)current.State == 4;
            }
            while (DateTime.UtcNow < deadline && (running || !seenStarted));

            if (running || !seenStarted)
                return new Result(host, "pending", null,
                    "Remote task still running or not started. Task left for inspection.");
            int exitCode = (int)((dynamic)registered!).LastTaskResult;
            root.DeleteTask(taskName, 0);
            return new Result(host, exitCode == 0 ? "success" : "failed",
                exitCode, "Remote task completed; one-time task deleted. Local campaign tasks are preserved as designed.");
        }
        catch (Exception)
        {
            if (attempted)
                return new Result(host, "pending", null,
                    "Remote registration or execution result is uncertain; inspect target Task Scheduler and client health. No automatic retry.");
            throw;
        }
        finally
        {
            Release(registered);
            Release(task);
            Release(folder);
            Release(com);
        }
    }

    private static void Release(object? o)
    {
        if (o is not null && Marshal.IsComObject(o))
        {
            try { _ = Marshal.FinalReleaseComObject(o); }
            catch (Exception) { /* RPC cleanup cannot erase the operation result. */ }
        }
    }

    private static readonly HashSet<uint> OfflineCodes =
    [
        0x800706BAu, 0x800706BFu, 0x80070035u, 0x80070043u,
        0x800704CFu, 0x800705B4u, 0x80070079u, 0x80070040u,
        0x80072EE7u
    ];

    private static bool IsKnownOffline(Exception ex)
    {
        for (int i = 0; i < 5 && ex is not null; i++)
        {
            if (OfflineCodes.Contains(unchecked((uint)ex.HResult))) return true;
            ex = ex.InnerException!;
        }
        return false;
    }

    private static string Get(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) &&
        v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Required(JsonElement obj, string name) =>
        Get(obj, name) is { Length: > 0 } value ? value :
            throw new InvalidDataException("Missing approved action field: " + name);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string username, string? domain,
        string password, int logonType, int provider, out SafeAccessTokenHandle token);
}
