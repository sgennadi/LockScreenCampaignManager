using System.Globalization;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LockScreenCampaignManager;

/// <summary>
/// The native SYSTEM client of a campaign exported as Executable. This is an
/// independent headless entrypoint of the pinned portable EXE, not an
/// executable wrapper around the PowerShell script.
/// Read-only Status needs no elevation. Mutation requires LocalSystem.
/// </summary>
internal static partial class NativeClientAgent
{
    // Do not unlock EXE deployment until its Windows end-to-end compatibility
    // tests pass (including the GPO startup, scheduled transitions and cleanup).
    internal const bool IsReadyForProduction = false;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static readonly Regex Identifier = new(
        @"\A[a-z0-9][a-z0-9_-]{0,70}\z", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex ImageName = new(
        @"\A[0-9]{2}\.jpg\z", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex Sha = new(
        @"\A[0-9A-Fa-f]{64}\z", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private const string RegistryPath = @"SOFTWARE\Policies\Microsoft\Windows\Personalization";
    private const string MutexName = @"Global\LockScreenCampaign_Coordinator";

    private sealed record SourceRecord(string CampaignId, string SourceDirectory,
        string ManifestSha256, string AgentSha256);
    private sealed record OwnerRecord(string CampaignId, string ClaimedUtc);
    private sealed record RegistryValueRecord(bool Exists, string Kind,
        string? Text, long Number = 0, string[]? TextArray = null, string? Base64 = null);
    private sealed record BackupRecord(int Schema, string CampaignId,
        bool RegistryKeyExisted, RegistryValueRecord LockScreenImage,
        RegistryValueRecord NoChangingLockScreen, string Digest);

    private sealed class Context
    {
        internal required CampaignConfig Config { get; init; }
        internal required string ProcessExe { get; init; }
        internal required string Here { get; init; }
        internal required string Id { get; init; }
        internal required string Base { get; init; }
        internal required string Local { get; init; }
        internal required string ZoneId { get; init; }
        internal required TimeZoneInfo Zone { get; init; }
        internal required DateTime End { get; init; }
        internal required DateTime[] Starts { get; init; }
        internal required string LocalExe { get; init; }
        internal required string LocalManifest { get; init; }
        internal required string Images { get; init; }
        internal required string OwnerPath { get; init; }
        internal required string StatePath { get; init; }
        internal required string BackupPath { get; init; }
        internal required string CompletedPath { get; init; }
        internal required string ReceiptPath { get; init; }
        internal required string TaskName { get; init; }
        internal required string FeedbackTaskName { get; init; }
    }

    internal static int Run(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 2 ||
            args[0] != "--client-agent" ||
            args[1] is not ("Install" or "Apply" or "Repair" or
                "Cleanup" or "Status" or "Report"))
            return 2;

        string mode = args[1];
        try
        {
            Context ctx = Load();
            if (mode == "Status")
            {
                Console.WriteLine(JsonSerializer.Serialize(Status(ctx), Json));
                return 0;
            }

            if (WindowsIdentity.GetCurrent().User?.Value != "S-1-5-18")
                throw new UnauthorizedAccessException(
                    "The native campaign client changes Windows only as LocalSystem.");

            using var mutex = new Mutex(false, MutexName);
            bool acquired;
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(40)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
                throw new InvalidOperationException("Another campaign operation is running.");
            try
            {
                if (mode == "Install" || mode == "Repair") Install(ctx);
                else if (mode == "Apply") Apply(ctx);
                else if (mode == "Cleanup") Cleanup(ctx);
                else if (mode == "Report") Report(ctx);
                return 0;
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception ex)
        {
            if (mode == "Status")
                Console.Error.WriteLine("Native client Status unavailable: " + ex.GetType().Name);
            else WriteError(ex);
            return 1;
        }
    }

    private static Context Load()
    {
        string exe = Environment.ProcessPath ??
            throw new InvalidOperationException("Native executable path is unavailable.");
        if (!Path.GetFileName(exe).Equals("LockScreen-Agent.exe",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The executable must be a pinned client package.");
        string here = Path.GetDirectoryName(exe) ??
            throw new InvalidDataException("Client directory is missing.");
        string manifest = Path.Combine(here, "campaign.json");
        VerifyFileAttributes(exe, 250L * 1_048_576);
        VerifyFileAttributes(manifest, 1_048_576);
        var config = JsonSerializer.Deserialize<CampaignConfig>(
            File.ReadAllBytes(manifest), CampaignExporter.JsonOptions) ??
            throw new InvalidDataException("Campaign manifest is invalid.");
        if (config.ClientType != CampaignClientType.Executable ||
            config.SchemaVersion != 1 || !Identifier.IsMatch(config.CampaignId) ||
            !Sha.IsMatch(config.AgentSha256))
            throw new InvalidDataException("Native client manifest identity is invalid.");
        if (!FileSha(exe).Equals(config.AgentSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pinned client executable SHA-256 mismatch.");

        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId);
        if (config.Slides is null || config.Slides.Count is < 1 or > 30)
            throw new InvalidDataException("Campaign must contain 1-30 slides.");
        DateTime end = ParseLocal(config.EndLocal);
        if (zone.IsInvalidTime(end) || zone.IsAmbiguousTime(end))
            throw new InvalidDataException("Campaign end is invalid in its time zone.");
        var starts = new DateTime[config.Slides.Count];
        DateTime previous = DateTime.MinValue;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < starts.Length; i++)
        {
            CampaignSlide slide = config.Slides[i];
            starts[i] = ParseLocal(slide.AtLocal);
            if (!ImageName.IsMatch(slide.FileName) || !names.Add(slide.FileName) ||
                !Sha.IsMatch(slide.Sha256) ||
                starts[i] <= previous || starts[i] >= end ||
                zone.IsInvalidTime(starts[i]) || zone.IsAmbiguousTime(starts[i]))
                throw new InvalidDataException("Campaign image schedule or hash is invalid.");
            previous = starts[i];
        }

        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LockScreenCampaigns");
        string local = Path.Combine(baseDir, config.CampaignId);
        return new Context
        {
            Config = config, Id = config.CampaignId,
            ZoneId = config.TimeZoneId, Zone = zone, End = end, Starts = starts,
            ProcessExe = exe, Here = here, Base = baseDir, Local = local,
            LocalExe = Path.Combine(local, "LockScreen-Agent.exe"),
            LocalManifest = Path.Combine(local, "campaign.json"),
            Images = Path.Combine(local, "images"),
            OwnerPath = Path.Combine(baseDir, "active-owner.json"),
            StatePath = Path.Combine(local, "source-state.json"),
            BackupPath = Path.Combine(local, "registry-backup-native.json"),
            CompletedPath = Path.Combine(baseDir, "Completed", config.CampaignId + ".json"),
            ReceiptPath = Path.Combine(baseDir, "Completed",
                config.CampaignId + ".postcleanup.json"),
            TaskName = "LockScreen-LS-" + config.CampaignId,
            FeedbackTaskName = "LockScreen-LS-Feedback-" + config.CampaignId
        };
    }

    private static DateTime ParseLocal(string value) =>
        DateTime.ParseExact(value, CampaignSchedule.DateFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static DateTime CampaignNow(Context ctx) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ctx.Zone);

    private static int ExpectedIndex(Context ctx)
    {
        DateTime now = CampaignNow(ctx);
        if (now >= ctx.End) return -1;
        int index = -1;
        for (int i = 0; i < ctx.Starts.Length; i++)
        {
            if (now < ctx.Starts[i]) break;
            index = i;
        }
        if (index < 0) return -1;
        if (!ctx.Config.RandomizePerDevice) return index;

        // Stable per-machine permutation: the schedule remains unmodified.
        byte[] seed = SHA256.HashData(Encoding.UTF8.GetBytes(
            ctx.Id + ":" + Environment.MachineName.ToUpperInvariant()));
        int[] order = Enumerable.Range(0, ctx.Starts.Length).ToArray();
        for (int i = order.Length - 1; i >= 1; i--)
        {
            int j = (int)(BitConverter.ToUInt32(seed, (i * 4) % 28) % (uint)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order[index];
    }

    private static void VerifyFileAttributes(string path, long max)
    {
        FileAttributes flags = File.GetAttributes(path);
        if ((flags & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("Campaign file is a symlink or directory.");
        long size = new FileInfo(path).Length;
        if (size is < 1 || size > max)
            throw new InvalidDataException("Campaign file size is unsafe.");
    }

    private static string FileSha(string path)
    {
        VerifyFileAttributes(path, 250L * 1_048_576);
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool Verified(string path, string expected) =>
        File.Exists(path) && FileSha(path).Equals(expected,
            StringComparison.OrdinalIgnoreCase);

    private static T? ReadOptional<T>(string path, long max = 1_048_576)
        where T : class
    {
        if (!File.Exists(path)) return null;
        VerifyFileAttributes(path, max);
        return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Json);
    }

    private static void WriteNew(string path, object value)
    {
        string stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(stage, JsonSerializer.SerializeToUtf8Bytes(value, Json));
            File.Move(stage, path, overwrite: false);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }

    private static void VerifyAndCopy(string source, string destination,
        string expected, long max)
    {
        VerifyFileAttributes(source, max);
        if (!Verified(source, expected))
            throw new InvalidDataException("Trusted campaign source SHA-256 mismatch.");
        if (Verified(destination, expected)) return;
        string stage = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string backup = destination + "." + Guid.NewGuid().ToString("N") + ".old";
        try
        {
            File.Copy(source, stage, overwrite: false);
            if (!Verified(stage, expected))
                throw new IOException("Staged campaign file hash is invalid.");
            if (File.Exists(destination))
                File.Replace(stage, destination, backup, true);
            else File.Move(stage, destination);
            if (!Verified(destination, expected))
                throw new IOException("Installed campaign file hash is invalid.");
        }
        finally
        {
            if (File.Exists(stage)) File.Delete(stage);
            if (File.Exists(backup)) File.Delete(backup);
        }
    }

    private static void ProtectDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Campaign state directory must not be a reparse point.");
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        var principals = new[]
        {
            ("S-1-5-18", FileSystemRights.FullControl),
            ("S-1-5-32-544", FileSystemRights.FullControl),
            ("S-1-5-32-545", FileSystemRights.ReadAndExecute),
            ("S-1-15-2-1", FileSystemRights.ReadAndExecute),
            ("S-1-15-2-2", FileSystemRights.ReadAndExecute)
        };
        foreach (var principal in principals)
            acl.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(principal.Item1),
                principal.Item2,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(path), acl);
    }

    private static void EnsureNoOtherOwner(Context ctx)
    {
        OwnerRecord? owner = ReadOptional<OwnerRecord>(ctx.OwnerPath);
        if (owner is not null && owner.CampaignId != ctx.Id)
            throw new InvalidOperationException("Another campaign owns the lock screen.");
        using var key = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false);
        string current = Convert.ToString(key?.GetValue("LockScreenImage", "",
            RegistryValueOptions.DoNotExpandEnvironmentNames)) ?? "";
        if (current.StartsWith(ctx.Base + "\\", StringComparison.OrdinalIgnoreCase) &&
            !current.StartsWith(ctx.Images + "\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Another local campaign controls the lock screen.");
    }

    private static SourceRecord? GetState(Context ctx)
    {
        SourceRecord? state = ReadOptional<SourceRecord>(ctx.StatePath);
        if (state is null) return null;
        if (state.CampaignId != ctx.Id ||
            !Sha.IsMatch(state.ManifestSha256) || !Sha.IsMatch(state.AgentSha256) ||
            !Path.IsPathFullyQualified(state.SourceDirectory))
            throw new InvalidDataException("Local campaign source identity is invalid.");
        return state;
    }

    private static void Install(Context ctx)
    {
        VerifySupportedWindows();
        ProtectDirectory(ctx.Base);
        ProtectDirectory(ctx.Local);
        ProtectDirectory(ctx.Images);
        if (File.Exists(ctx.CompletedPath))
        {
            // A mapped EXE cannot delete itself on Windows. On the next
            // startup the GPO launcher reenters here to complete the
            // deferred-delete receipt, never to reinstall a finished ID.
            FinalizeCleanup(ctx);
            return;
        }
        EnsureNoOtherOwner(ctx);

        SourceRecord? prior = GetState(ctx);
        string source = ctx.Here.Equals(ctx.Local, StringComparison.OrdinalIgnoreCase)
            ? prior?.SourceDirectory ?? throw new InvalidDataException(
                "Local source identity missing. Run Startup installation from SYSVOL.")
            : ctx.Here;
        string sourceManifest = Path.Combine(source, "campaign.json");
        string sourceExe = Path.Combine(source, "LockScreen-Agent.exe");
        string manifestHash = FileSha(sourceManifest);
        string agentHash = FileSha(sourceExe);
        if (!agentHash.Equals(ctx.Config.AgentSha256, StringComparison.OrdinalIgnoreCase) ||
            (prior is not null &&
             (!prior.ManifestSha256.Equals(manifestHash, StringComparison.OrdinalIgnoreCase) ||
              !prior.AgentSha256.Equals(agentHash, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("Immutable campaign source changed.");
        foreach (CampaignSlide slide in ctx.Config.Slides)
            if (!Verified(Path.Combine(source, "images", slide.FileName), slide.Sha256))
                throw new InvalidDataException("Source image SHA-256 mismatch: " + slide.FileName);

        if (prior is null)
        {
            if (File.Exists(ctx.LocalManifest) &&
                !Verified(ctx.LocalManifest, manifestHash))
                throw new InvalidOperationException(
                    "Local campaign has different manifest and no source identity.");
            WriteNew(ctx.StatePath, new SourceRecord(ctx.Id, source,
                manifestHash, agentHash));
        }
        VerifyAndCopy(sourceManifest, ctx.LocalManifest, manifestHash, 1_048_576);
        VerifyAndCopy(sourceExe, ctx.LocalExe, agentHash, 250L * 1_048_576);
        foreach (CampaignSlide slide in ctx.Config.Slides)
            VerifyAndCopy(Path.Combine(source, "images", slide.FileName),
                Path.Combine(ctx.Images, slide.FileName),
                slide.Sha256, 30L * 1_048_576);
        RegisterCampaignTask(ctx);
        RegisterFeedbackTask(ctx);
        ReportSafe(ctx, "Pending");
        Apply(ctx);
    }

    private static void VerifySupportedWindows()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion") ??
            throw new InvalidOperationException("Windows version could not be read.");
        if (!int.TryParse(Convert.ToString(key.GetValue("CurrentBuildNumber")),
                out int build) || build < 14393)
            throw new PlatformNotSupportedException("Windows 10 1607 or later required.");
        string edition = Convert.ToString(key.GetValue("EditionID")) ?? "";
        if (!(edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) ||
              edition.Contains("Education", StringComparison.OrdinalIgnoreCase)))
            throw new PlatformNotSupportedException(
                "GPO lock-screen enforcement requires a supported Enterprise/Education edition.");
    }

    private static void Apply(Context ctx)
    {
        if (CampaignNow(ctx) >= ctx.End) { Cleanup(ctx); return; }
        int index = ExpectedIndex(ctx);
        if (index < 0) return;
        SourceRecord state = GetState(ctx) ??
            throw new InvalidDataException("Campaign has no trusted local state.");
        if (!Verified(ctx.LocalManifest, state.ManifestSha256) ||
            !Verified(ctx.LocalExe, state.AgentSha256))
            throw new InvalidDataException("Local campaign files changed; run Repair.");

        EnsureNoOtherOwner(ctx);
        CampaignSlide slide = ctx.Config.Slides[index];
        string selected = Path.Combine(ctx.Images, slide.FileName);
        if (!Verified(selected, slide.Sha256))
        {
            string source = Path.Combine(state.SourceDirectory, "images", slide.FileName);
            VerifyAndCopy(source, selected, slide.Sha256, 30L * 1_048_576);
        }

        using var key = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false);
        string current = Convert.ToString(key?.GetValue("LockScreenImage", "",
            RegistryValueOptions.DoNotExpandEnvironmentNames)) ?? "";
        BackupRecord? backup = ReadBackup(ctx);
        if (current.StartsWith(ctx.Images + "\\", StringComparison.OrdinalIgnoreCase) &&
            backup is null)
            throw new InvalidOperationException("Original registry backup is missing.");
        if (backup is not null &&
            current.Length > 0 &&
            !current.StartsWith(ctx.Images + "\\", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(current, backup.LockScreenImage.Text,
                StringComparison.Ordinal))
            throw new InvalidOperationException("Another GPO changed the lock screen.");
        if (backup is null) SaveBackup(ctx);
        if (!File.Exists(ctx.OwnerPath))
            WriteNew(ctx.OwnerPath, new OwnerRecord(ctx.Id,
                DateTimeOffset.UtcNow.ToString("O")));
        using var update = Registry.LocalMachine.CreateSubKey(RegistryPath,
            writable: true) ?? throw new IOException("Cannot open lock-screen policy.");
        update.SetValue("LockScreenImage", selected, RegistryValueKind.String);
        update.SetValue("NoChangingLockScreen", 1, RegistryValueKind.DWord);
        ReportSafe(ctx, "Active");
    }

    private static object Status(Context ctx)
    {
        // Status never performs repairs, writes receipts or infers missing
        // values when ACLs prevent reading the required evidence.
        var missing = new List<string>();
        var corrupt = new List<string>();
        string observationError = "";
        foreach (CampaignSlide slide in ctx.Config.Slides)
        {
            string path = Path.Combine(ctx.Images, slide.FileName);
            try
            {
                if (!File.Exists(path)) missing.Add(slide.FileName);
                else if (!Verified(path, slide.Sha256)) corrupt.Add(slide.FileName);
            }
            catch (Exception ex)
            {
                observationError = ex.GetType().Name;
                break;
            }
        }

        SourceRecord? state = null;
        string sourceStatus = "Missing";
        try
        {
            state = GetState(ctx);
            sourceStatus = state is null ? "Missing" : "Verified";
        }
        catch (Exception ex) { sourceStatus = "Unavailable: " + ex.GetType().Name; }

        bool manifestOk = state is not null && Verified(ctx.LocalManifest, state.ManifestSha256);
        bool agentOk = state is not null && Verified(ctx.LocalExe, state.AgentSha256);
        bool completed = File.Exists(ctx.CompletedPath);
        bool? taskPresent = null;
        string taskStatus = "Unavailable";
        try
        {
            taskPresent = TaskExists(ctx);
            taskStatus = taskPresent.Value ? "Present" : "Missing";
        }
        catch (Exception) { /* ACL/RPC failure cannot prove a task is absent. */ }

        string ownerStatus = "Unknown";
        try
        {
            var owner = ReadOptional<OwnerRecord>(ctx.OwnerPath);
            ownerStatus = owner is null ? "Unclaimed" :
                owner.CampaignId == ctx.Id ? "Owned" : "AnotherCampaign";
        }
        catch (Exception) { /* Do not interpret a corrupt owner as unclaimed. */ }

        string backupStatus = "Missing";
        try { if (ReadBackup(ctx) is not null) backupStatus = "Valid"; }
        catch (Exception) { backupStatus = "Invalid"; }

        int index = ExpectedIndex(ctx);
        string expected = index >= 0 ? ctx.Config.Slides[index].FileName : "";
        string currentImage = "";
        bool? registryMatches = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false);
            currentImage = Convert.ToString(key?.GetValue("LockScreenImage", "",
                RegistryValueOptions.DoNotExpandEnvironmentNames)) ?? "";
            if (index >= 0)
                registryMatches = currentImage.Equals(
                    Path.Combine(ctx.Images, expected), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { /* Unknown, not a confirmed absence. */ }

        string stage = completed ? "Completed" :
            index < 0 ? "Pending" : "Active";
        string health = sourceStatus.StartsWith("Unavailable", StringComparison.Ordinal) ||
            observationError.Length > 0 || backupStatus == "Invalid" ||
            ownerStatus == "Unknown" ? "Unknown" :
            completed ? "Completed" :
            missing.Count > 0 || corrupt.Count > 0 || !manifestOk || !agentOk ||
            taskPresent == false || (index >= 0 && registryMatches == false) ||
            (index >= 0 && (backupStatus != "Valid" || ownerStatus != "Owned")) ||
            ownerStatus == "AnotherCampaign" ? "RepairNeeded" :
            taskPresent is null || (index >= 0 && registryMatches is null)
                ? "Unknown" : "Healthy";

        return new
        {
            AgentVersion = "0.4.0-preview.64-native",
            CampaignId = ctx.Id, ClientType = "Executable",
            Health = health, Stage = stage,
            Installed = File.Exists(ctx.LocalExe),
            MissingImages = missing, CorruptImages = corrupt,
            ConfigHashOK = manifestOk, AgentHashOK = agentOk,
            SourceStatus = sourceStatus, TaskStatus = taskStatus,
            TaskPresent = taskPresent, BackupStatus = backupStatus,
            ActiveOwner = ownerStatus, ExpectedImage = expected,
            CurrentRegistryImage = currentImage,
            RegistryMatches = registryMatches,
            ObservationError = observationError,
            Completed = completed
        };
    }

    private static void WriteError(Exception ex)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LockScreenCampaigns");
            Directory.CreateDirectory(dir);
            string line = DateTimeOffset.UtcNow.ToString("O") + " Native EXE: " +
                ex.GetType().Name + " " + ex.Message + Environment.NewLine;
            File.AppendAllText(Path.Combine(dir, "native-agent-error.log"), line);
        }
        catch { }
    }
}
