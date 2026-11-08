using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Administrator-owned, machine-wide configuration for the dedicated gMSA
/// scheduler task. The worker never accepts an account/domain/scope/path from
/// command-line arguments or from an untrusted client health report.
/// </summary>
internal sealed class GmsaRepairConfig
{
    internal static string? TestRootOverride;
    internal static string Root => TestRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LockScreenCampaignManager", "GmsaWorker");
    internal static string ConfigPath => Path.Combine(Root, "config.json");
    internal static string QueueDirectory => Path.Combine(Root, "queue");
    internal static string StatusDirectory => Path.Combine(Root, "status");

    public int SchemaVersion { get; set; } = 1;
    public bool Enabled { get; set; }
    public string GmsaAccount { get; set; } = "";
    public string GmsaSid { get; set; } = "";
    public string Domain { get; set; } = "";
    public string OuDn { get; set; } = "";
    public string GroupDn { get; set; } = "";
    public string FeedbackShare { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public DateTimeOffset InstalledUtc { get; set; }

    private static readonly Regex GmsaAccountPattern = new(
        @"\A[A-Za-z0-9_.-]{1,63}\\[A-Za-z0-9_-]{1,19}\$\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex DomainPattern = new(
        @"^(?=.{3,253}$)[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$",
        RegexOptions.CultureInvariant);

    // Never conflate read-only auditing of a disabled installation with
    // authorization to launch RPC. Active operations MUST use ScopeMatches().
    internal bool ScopeMatchesForInspection(string domain, string ouDn, string groupDn) =>
        string.Equals(Domain, domain, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(OuDn, ouDn, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(GroupDn, groupDn, StringComparison.OrdinalIgnoreCase);

    internal bool ScopeMatches(string domain, string ouDn, string groupDn) =>
        Enabled && ScopeMatchesForInspection(domain, ouDn, groupDn);

    internal bool MatchesForInspection(DeferredRepairItem item) =>
        ScopeMatchesForInspection(item.Domain, item.OuDn, item.GroupDn) &&
        item.BackgroundAllowed &&
        !string.IsNullOrWhiteSpace(item.ApprovalSid) &&
        item.ApprovedUtc >= InstalledUtc.AddMinutes(-5) &&
        item.ApprovedUtc <= DateTimeOffset.UtcNow.AddMinutes(5) &&
        DeferredRepairQueue.IsValidForStorage(item);

    internal bool Matches(DeferredRepairItem item) =>
        Enabled && MatchesForInspection(item);

    internal void Validate() => ValidateCore(requireEnabled: true);

    // Only the elevated read-only queue inspector may accept Enabled=false.
    // No temporary Enable=true mutation of the live config is necessary.
    internal void ValidateForInspection() => ValidateCore(requireEnabled: false);

    private void ValidateCore(bool requireEnabled)
    {
        if (SchemaVersion != 1 || (requireEnabled && !Enabled) ||
            !GmsaAccountPattern.IsMatch(GmsaAccount) ||
            !DomainPattern.IsMatch(Domain) ||
            OuDn.Length is < 8 or > 1024 || GroupDn.Length is < 8 or > 1024 ||
            !OuDn.EndsWith(",DC=" + Domain.Replace(".", ",DC="),
                StringComparison.OrdinalIgnoreCase) ||
            !GroupDn.EndsWith(",DC=" + Domain.Replace(".", ",DC="),
                StringComparison.OrdinalIgnoreCase) ||
            InstalledUtc.Year < 2025 || InstalledUtc > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new InvalidDataException("Invalid gMSA configuration or approved AD scope.");
        try
        {
            _ = new SecurityIdentifier(GmsaSid);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("Invalid gMSA account SID.", ex);
        }
        if (!string.Equals(FeedbackShare, FeedbackPath.ValidateRoot(FeedbackShare),
                StringComparison.OrdinalIgnoreCase) ||
            !Path.IsPathFullyQualified(ExecutablePath) ||
            !Path.GetFileName(ExecutablePath).Equals("LockScreenCampaignManager.exe",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid gMSA feedback share or executable path.");
    }

    // Avoid accepting a path shadowed by a junction or an inherited writable ACL.
    internal static void VerifyProtectedPath(string path, bool directory)
    {
        if (TestRootOverride is not null) return;
        FileSystemSecurity security;
        if (directory)
        {
            var info = new DirectoryInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("gMSA root is a reparse point.");
            security = info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        }
        else
        {
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("gMSA configuration is a reparse point.");
            security = info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        }
        if (!security.AreAccessRulesProtected)
            throw new UnauthorizedAccessException("gMSA configuration ACL inheritance is not disabled.");
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner?.Value is not ("S-1-5-18" or "S-1-5-32-544"))
            throw new UnauthorizedAccessException(
                "gMSA configuration or EXE owner is not SYSTEM/Administrators.");
        var write = FileSystemRights.WriteData | FileSystemRights.AppendData |
            FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership |
            FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(
                     includeExplicit: true, includeInherited: true,
                     targetType: typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow ||
                (rule.FileSystemRights & write) == 0) continue;
            string sid = ((SecurityIdentifier)rule.IdentityReference).Value;
            if (sid != "S-1-5-18" && sid != "S-1-5-32-544")
                throw new UnauthorizedAccessException(
                    "gMSA configuration is writable by an identity outside SYSTEM/Administrators.");
        }
    }

    internal static GmsaRepairConfig Load(bool requireWorkerIdentity = false)
    {
        if (!File.Exists(ConfigPath) || new FileInfo(ConfigPath).Length is < 50 or > 16384)
            throw new FileNotFoundException("Machine-wide gMSA worker has not been configured.",
                ConfigPath);
        // The parent directory must also be protected; otherwise an
        // unprivileged parent owner can swap the entire worker folder.
        VerifyProtectedPath(Path.GetDirectoryName(Root)
            ?? throw new InvalidDataException("Invalid protected worker root."), directory: true);
        VerifyProtectedPath(Root, directory: true);
        VerifyProtectedPath(ConfigPath, directory: false);
        var config = JsonSerializer.Deserialize<GmsaRepairConfig>(File.ReadAllText(ConfigPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Malformed gMSA configuration.");
        config.Validate();
        // Enforce EXE and bin-folder integrity even if an administrator
        // mistakenly changes the installed file permissions after setup.
        VerifyProtectedPath(Path.GetDirectoryName(config.ExecutablePath)
            ?? throw new InvalidDataException("Invalid gMSA binary directory."), directory: true);
        VerifyProtectedPath(config.ExecutablePath, directory: false);
        if (requireWorkerIdentity)
        {
            string actualSid = WindowsIdentity.GetCurrent().User?.Value ?? "";
            if (!string.Equals(actualSid, config.GmsaSid, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Worker is not running as the configured gMSA.");
            if (!string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""),
                    Path.GetFullPath(config.ExecutablePath), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Unexpected worker executable path.");
        }
        return config;
    }

    internal static GmsaRepairConfig? TryLoad()
    {
        try { return Load(); }
        catch { return null; }
    }

    /// <summary>
    /// Allows an elevated operator to inspect a safely disabled installation
    /// without re-enabling the unattended worker or weakening Load().
    /// </summary>
    internal static GmsaRepairConfig? TryReadForInspection()
    {
        try
        {
            if (!File.Exists(ConfigPath) || new FileInfo(ConfigPath).Length is < 50 or > 16384)
                return null;
            VerifyProtectedPath(Path.GetDirectoryName(Root)
                ?? throw new InvalidDataException("Missing protected ProgramData parent."), true);
            VerifyProtectedPath(Root, true);
            VerifyProtectedPath(ConfigPath, false);
            var config = JsonSerializer.Deserialize<GmsaRepairConfig>(
                File.ReadAllText(ConfigPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("Malformed installed gMSA settings.");
            config.ValidateForInspection(); // Never temporarily enable a disabled configuration.
            VerifyProtectedPath(Path.GetDirectoryName(config.ExecutablePath)
                ?? throw new InvalidDataException("Invalid protected binary directory."), true);
            VerifyProtectedPath(config.ExecutablePath, false);
            return config;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Inspect disabled gMSA configuration");
            return null;
        }
    }

    // Convert Task Scheduler COM trigger collection to plain data before
    // calling the strict, testable schedule validator. Extra triggers or a
    // missing boot trigger fail validation rather than widening execution.
    private static IReadOnlyList<GmsaWorkerTriggerSpec> ReadWorkerTriggers(dynamic triggers)
    {
        var items = new List<GmsaWorkerTriggerSpec>();
        int count = (int)triggers.Count;
        if (count != 2) return items;
        for (int i = 1; i <= count; i++)
        {
            dynamic trigger = triggers.Item(i);
            int type = (int)trigger.Type;
            bool enabled = (bool)trigger.Enabled;
            if (type == GmsaWorkerSchedule.DailyTrigger)
                items.Add(new GmsaWorkerTriggerSpec(
                    type, enabled, (string)trigger.Repetition.Interval,
                    (string)trigger.Repetition.Duration, (int)trigger.DaysInterval, ""));
            else if (type == GmsaWorkerSchedule.BootTrigger)
                items.Add(new GmsaWorkerTriggerSpec(
                    type, enabled, "", "", 0, (string)trigger.Delay));
            else
                items.Add(new GmsaWorkerTriggerSpec(type, enabled, "", "", 0, ""));
        }
        return items;
    }

    internal static bool IsScheduledTaskReady(GmsaRepairConfig config)
    {
        try
        {
            var server = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)
                ?? throw new InvalidOperationException("Task Scheduler COM unavailable.");
            dynamic service = Activator.CreateInstance(server)
                ?? throw new InvalidOperationException("Task Scheduler instance unavailable.");
            service.Connect();
            dynamic task = service.GetFolder("\\").GetTask("LSCM-Gmsa-UnattendedRepair");
            if (task is null) return false;
            dynamic definition = task.Definition;
            string principal = (string)definition.Principal.UserId;
            return task.Enabled &&
                definition.Principal.LogonType == 1 && // Password logon, managed by AD
                (string.Equals(principal, config.GmsaAccount, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(principal, config.GmsaSid, StringComparison.OrdinalIgnoreCase)) &&
                definition.Actions.Count == 1 &&
                string.Equals((string)definition.Actions.Item(1).Path,
                    config.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string)definition.Actions.Item(1).Arguments,
                    "--gmsa-repair-once", StringComparison.Ordinal) &&
                definition.Settings.MultipleInstances == 2 && // IgnoreNew
                definition.Settings.StartWhenAvailable &&
                GmsaWorkerSchedule.Matches(ReadWorkerTriggers(definition.Triggers));
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Inspect gMSA Task Scheduler registration");
            return false;
        }
    }


}
