using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LockScreenCampaignManager;

internal static partial class NativeClientAgent
{
    private static RegistryValueRecord GetRegistryValue(RegistryKey? key,
        string name)
    {
        if (key is null || !key.GetValueNames().Contains(name,
                StringComparer.OrdinalIgnoreCase))
            return new RegistryValueRecord(false, "String", null);
        RegistryValueKind kind = key.GetValueKind(name);
        object? raw = key.GetValue(name, null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);
        return kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString =>
                new RegistryValueRecord(true, kind.ToString(),
                    Convert.ToString(raw, CultureInfo.InvariantCulture)),
            RegistryValueKind.DWord or RegistryValueKind.QWord =>
                new RegistryValueRecord(true, kind.ToString(), null,
                    Convert.ToInt64(raw, CultureInfo.InvariantCulture)),
            RegistryValueKind.MultiString =>
                new RegistryValueRecord(true, "MultiString", null, 0,
                    (string[]?)raw ?? []),
            RegistryValueKind.Binary =>
                new RegistryValueRecord(true, "Binary", null, 0, null,
                    Convert.ToBase64String((byte[]?)raw ?? [])),
            _ => throw new InvalidOperationException(
                "Unsupported registry type for safe original-value backup.")
        };
    }

    private static void RestoreRegistryValue(RegistryKey key, string name,
        RegistryValueRecord state)
    {
        if (!state.Exists)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
            return;
        }
        if (!Enum.TryParse(state.Kind, out RegistryValueKind kind) ||
            kind is RegistryValueKind.Unknown or RegistryValueKind.None)
            throw new InvalidDataException("Original registry backup type is invalid.");
        object value = kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString =>
                state.Text ?? "",
            RegistryValueKind.DWord => checked((int)state.Number),
            RegistryValueKind.QWord => state.Number,
            RegistryValueKind.MultiString => state.TextArray ?? [],
            RegistryValueKind.Binary => Convert.FromBase64String(state.Base64 ?? ""),
            _ => throw new InvalidDataException("Unsupported registry backup type.")
        };
        key.SetValue(name, value, kind);
    }

    private static string BackupDigest(BackupRecord backup)
    {
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            backup.Schema, backup.CampaignId, backup.RegistryKeyExisted,
            backup.LockScreenImage, backup.NoChangingLockScreen
        }, Json);
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }

    private static BackupRecord? ReadBackup(Context ctx)
    {
        var backup = ReadOptional<BackupRecord>(ctx.BackupPath);
        if (backup is null) return null;
        if (backup.Schema != 2 || backup.CampaignId != ctx.Id ||
            backup.LockScreenImage is null || backup.NoChangingLockScreen is null ||
            !Sha.IsMatch(backup.Digest) ||
            !BackupDigest(backup).Equals(backup.Digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original lock-screen registry backup is invalid.");
        return backup;
    }

    private static void SaveBackup(Context ctx)
    {
        if (ReadBackup(ctx) is not null) return;
        using var hive = OpenNativeHive();
        using var key = hive.OpenSubKey(RegistryPath, writable: false);
        var image = GetRegistryValue(key, "LockScreenImage");
        var flag = GetRegistryValue(key, "NoChangingLockScreen");
        var draft = new BackupRecord(2, ctx.Id, key is not null, image, flag, "");
        WriteNew(ctx.BackupPath, draft with { Digest = BackupDigest(draft) });
        if (ReadBackup(ctx) is null)
            throw new IOException("Registry original-value backup did not persist.");
    }

    private static void Cleanup(Context ctx)
    {
        if (!Directory.Exists(ctx.Local))
        {
            FinalizeCleanup(ctx);
            return;
        }
        BackupRecord? backup = ReadBackup(ctx);
        EnsureNoOtherOwner(ctx);
        using var hive = OpenNativeHive();
        using var current = hive.OpenSubKey(RegistryPath, writable: false);
        string currentImage = Convert.ToString(current?.GetValue("LockScreenImage", "",
            RegistryValueOptions.DoNotExpandEnvironmentNames)) ?? "";
        bool ownsImage = currentImage.StartsWith(ctx.Images + "\\",
            StringComparison.OrdinalIgnoreCase);
        OwnerRecord? owner = ReadOptional<OwnerRecord>(ctx.OwnerPath);
        if (owner is not null && owner.CampaignId != ctx.Id)
            throw new InvalidOperationException("A different campaign owns the lock screen.");
        if (ownsImage && backup is null)
            throw new InvalidOperationException("Cannot restore without original registry backup.");

        if (ownsImage && backup is not null)
        {
            using var key = hive.CreateSubKey(RegistryPath,
                writable: true) ?? throw new IOException("Registry policy unavailable.");
            RestoreRegistryValue(key, "LockScreenImage", backup.LockScreenImage);
            RegistryValueRecord currentFlag = GetRegistryValue(key,
                "NoChangingLockScreen");
            if (currentFlag.Exists && currentFlag.Kind == "DWord" &&
                currentFlag.Number == 1)
                RestoreRegistryValue(key, "NoChangingLockScreen",
                    backup.NoChangingLockScreen);
        }
        if (owner is not null && owner.CampaignId == ctx.Id)
            File.Delete(ctx.OwnerPath);

        ProtectDirectory(Path.GetDirectoryName(ctx.CompletedPath)!);
        if (!File.Exists(ctx.CompletedPath))
            WriteNew(ctx.CompletedPath, new
            {
                CampaignId = ctx.Id,
                CompletedUtc = DateTimeOffset.UtcNow.ToString("O"),
                RestoredOwnRegistry = ownsImage,
                Status = "PendingNativeExeRemoval"
            });
        ReportSafe(ctx, "Completed");
        UnregisterCampaignTasks(ctx);
        if (!CampaignTasksGone(ctx))
            throw new IOException("Native client scheduled tasks were not removed.");

        // Windows keeps an executable mapped until the final process exits.
        // Unlike PS1, removing the running EXE synchronously cannot work.
        // Record the pending-delete operation, NEVER falsely attest that
        // the executable and directory have already disappeared.
        foreach (string file in Directory.EnumerateFiles(ctx.Images))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Unexpected image directory reparse point.");
            File.Delete(file);
        }
        if (!MoveFileEx(ctx.LocalExe, null, 4)) // MOVEFILE_DELAY_UNTIL_REBOOT
            throw new IOException("Cannot schedule removal of native agent executable.");
    }

    private static object ReceiptValue(RegistryValueRecord state) => new
    {
        state.Exists,
        state.Kind,
        Value = state.Kind switch
        {
            "DWord" or "QWord" => (object)state.Number,
            "Binary" => Convert.FromBase64String(state.Base64 ?? "")
                .Select(value => (int)value).ToArray(),
            "MultiString" => state.TextArray ?? [],
            _ => state.Text
        }
    };

    private static bool SameRegistryValue(RegistryValueRecord a,
        RegistryValueRecord b) =>
        a.Exists == b.Exists && a.Kind == b.Kind &&
        a.Text == b.Text && a.Number == b.Number &&
        a.Base64 == b.Base64 &&
        (a.TextArray ?? []).SequenceEqual(b.TextArray ?? []);

    private static void FinalizeCleanup(Context ctx)
    {
        if (!File.Exists(ctx.CompletedPath) || File.Exists(ctx.LocalExe))
            return; // Not yet deleted by Windows after reboot.
        // A completed marker is not itself evidence that old registry
        // values were restored. Never fabricate a valid receipt from it.
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(ctx.CompletedPath));
        var marker = document.RootElement;
        if (!marker.TryGetProperty("CampaignId", out var idField) ||
            idField.GetString() != ctx.Id ||
            !marker.TryGetProperty("RestoredOwnRegistry", out var restoredField))
            throw new InvalidDataException("Native cleanup marker is invalid.");
        bool restored = restoredField.ValueKind == JsonValueKind.True;
        BackupRecord? backup = Directory.Exists(ctx.Local) ? ReadBackup(ctx) : null;
        if (restored && backup is null)
            throw new InvalidDataException("Original backup is missing; final receipt denied.");

        OwnerRecord? owner = ReadOptional<OwnerRecord>(ctx.OwnerPath);
        if (owner is not null && owner.CampaignId == ctx.Id)
            throw new InvalidOperationException("Cleanup owner was not released.");
        if (!CampaignTasksGone(ctx))
            throw new InvalidOperationException(
                "Scheduled tasks still exist; native cleanup receipt cannot be issued.");

        if (restored && backup is not null)
        {
            using var hive = OpenNativeHive();
            using var registry = hive.OpenSubKey(
                RegistryPath, writable: false);
            if (!SameRegistryValue(GetRegistryValue(registry, "LockScreenImage"),
                    backup.LockScreenImage) ||
                !SameRegistryValue(GetRegistryValue(registry, "NoChangingLockScreen"),
                    backup.NoChangingLockScreen))
                throw new InvalidOperationException(
                    "Original registry values were overwritten after cleanup.");
        }

        if (Directory.Exists(ctx.Local))
        {
            if ((File.GetAttributes(ctx.Local) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Cannot finalize cleanup through a reparse point.");
            // Restrict deletion to the known, owned local footprint. A
            // foreign file or link is not a reason to recurse dangerously.
            foreach (string entry in Directory.GetFileSystemEntries(ctx.Local))
            {
                string name = Path.GetFileName(entry);
                if (name is not ("source-state.json" or "campaign.json" or
                    "registry-backup-native.json" or "images"))
                    throw new IOException("Unexpected local campaign entry; manual review required.");
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Local campaign contains an unexpected reparse point.");
                if (name == "images" && Directory.GetFileSystemEntries(entry).Length != 0)
                    throw new IOException("Local campaign image directory was not cleared.");
            }
            Directory.Delete(ctx.Local, recursive: true);
        }
        // Record no receipt if a different GPO replaced our image. That is
        // a NeedsReview outcome rather than a falsely verified restoration.
        if (!restored || backup is null) return;
        if (!File.Exists(ctx.ReceiptPath))
            WriteNew(ctx.ReceiptPath, new
            {
                schemaVersion = 1, campaignId = ctx.Id,
                observedUtc = DateTimeOffset.UtcNow.ToString("O"),
                cleanupStage = "AfterFileAndTaskRemoval",
                localRootRemoved = true, scheduledTaskRemoved = true,
                feedbackTaskRemoved = true, ownerReleased = true,
                restoredOwnRegistry = true,
                expectedLockScreenImage = ReceiptValue(backup.LockScreenImage),
                expectedNoChangingLockScreen = ReceiptValue(backup.NoChangingLockScreen)
            });
    }

    private static readonly Regex FeedbackRoot = new(
        @"\A\\\\[A-Za-z0-9][A-Za-z0-9.-]{0,252}\\[A-Za-z0-9_$.-]{1,80}\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static void ValidateFeedback(Context ctx)
    {
        CampaignFeedback? settings = ctx.Config.Feedback;
        if (settings is not { Enabled: true }) return;
        if (!FeedbackRoot.IsMatch(settings.ShareRoot) ||
            settings.ShareRoot.Contains("..", StringComparison.Ordinal) ||
            settings.IntervalMinutes is < 5 or > 60)
            throw new InvalidDataException("Invalid feedback SMB destination.");
        string share = settings.ShareRoot.Split('\\')[^1];
        if (share.Equals("SYSVOL", StringComparison.OrdinalIgnoreCase) ||
            share.Equals("NETLOGON", StringComparison.OrdinalIgnoreCase) ||
            share.Equals("ADMIN$", StringComparison.OrdinalIgnoreCase) ||
            share.Equals("IPC$", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(share, @"\A[A-Za-z]\$\z"))
            throw new InvalidDataException("Feedback may not use an administrative share.");
    }

    private static void Report(Context ctx)
    {
        CampaignFeedback? settings = ctx.Config.Feedback;
        if (settings is not { Enabled: true })
            throw new InvalidOperationException("Client feedback is disabled.");
        ValidateFeedback(ctx);
        string machine = Environment.MachineName.ToUpperInvariant();
        if (!Regex.IsMatch(machine, @"\A[A-Z0-9][A-Z0-9-]{0,62}\z"))
            throw new InvalidDataException("Unsafe computer name.");
        string folder = Path.Combine(settings.ShareRoot, ctx.Id);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException("Feedback SMB folder unavailable.");
        WriteFeedback(ctx, folder, machine, null);
    }

    private static void ReportSafe(Context ctx, string stage)
    {
        if (ctx.Config.Feedback is not { Enabled: true }) return;
        try
        {
            ValidateFeedback(ctx);
            string machine = Environment.MachineName.ToUpperInvariant();
            string folder = Path.Combine(ctx.Config.Feedback.ShareRoot, ctx.Id);
            if (Directory.Exists(folder) &&
                Regex.IsMatch(machine, @"\A[A-Z0-9][A-Z0-9-]{0,62}\z"))
                WriteFeedback(ctx, folder, machine, stage);
        }
        catch (Exception ex) { WriteError(ex); }
    }

    private static void WriteFeedback(Context ctx, string folder, string machine,
        string? stageOverride)
    {
        DateTime now = CampaignNow(ctx);
        int current = ExpectedIndex(ctx);
        string stage = stageOverride ??
            (now >= ctx.End ? "Completed" :
             current < 0 ? "Pending" : "Active");
        int missing = 0, corrupt = 0;
        foreach (CampaignSlide slide in ctx.Config.Slides)
        {
            string path = Path.Combine(ctx.Images, slide.FileName);
            if (!File.Exists(path)) missing++;
            else if (!Verified(path, slide.Sha256)) corrupt++;
        }
        SourceRecord? state = GetState(ctx);
        bool configOk = state is not null &&
            Verified(ctx.LocalManifest, state.ManifestSha256);
        bool agentOk = state is not null &&
            Verified(ctx.LocalExe, state.AgentSha256);
        using var hive = OpenNativeHive();
        using var registry = hive.OpenSubKey(RegistryPath,
            writable: false);
        bool registryOk = current >= 0 &&
            string.Equals(Convert.ToString(registry?.GetValue("LockScreenImage", "",
                RegistryValueOptions.DoNotExpandEnvironmentNames)),
                Path.Combine(ctx.Images, ctx.Config.Slides[current].FileName),
                StringComparison.OrdinalIgnoreCase);
        bool task = TaskExists(ctx);
        string health = stage == "Completed" ? "Completed" :
            missing + corrupt > 0 || !configOk || !agentOk ||
            !task || (stage == "Active" && !registryOk)
                ? "RepairNeeded" : "Healthy";
        var record = new
        {
            schemaVersion = 1, campaignId = ctx.Id, computerName = machine,
            timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            agentVersion = "0.4.0-preview.70-native", clientType = "Executable",
            stage, health,
            expectedImage = current >= 0 ? ctx.Config.Slides[current].FileName : "",
            imageMatches = missing + corrupt == 0,
            registryMatches = registryOk,
            taskPresent = task, missingFiles = missing, corruptFiles = corrupt
        };
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(record, Json);
        if (payload.Length > 8192)
            throw new InvalidDataException("Feedback payload exceeds 8 KiB.");
        string dest = Path.Combine(folder, machine + ".json");
        string temporary = Path.Combine(folder,
            "." + machine + "." + Guid.NewGuid().ToString("N") + ".tmp");
        string old = Path.Combine(folder,
            "." + machine + "." + Guid.NewGuid().ToString("N") + ".old");
        try
        {
            File.WriteAllBytes(temporary, payload);
            if (File.Exists(dest)) File.Replace(temporary, dest, old, true);
            else File.Move(temporary, dest);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(old)) File.Delete(old);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string from, string? to, int flags);
}
