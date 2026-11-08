using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows.Forms;
using UniversalUpdater;

namespace LockScreenCampaignManager;

/// <summary>
/// Operator-approved portable EXE replacement. The helper is a copy of the
/// currently running, already trusted manager, not the downloaded executable.
/// It waits for the main process to exit, replaces the EXE, runs its offline
/// self-test, restores the previous EXE on failure, and restarts on success.
/// It never elevates, modifies GPOs or touches the separately installed gMSA worker.
/// </summary>
internal static class PortableUpdateCoordinator
{
    internal const string HelperSwitch = "--complete-portable-update";

    internal static void Launch(StagedPackage package, UpdateOptions options)
    {
        options.Validate();
        if (package.ExtractedExecutablePath is null ||
            package.ExtractedExecutableSha256 is null ||
            !package.Release.Repository.Equals(options.Repository, StringComparison.Ordinal) ||
            package.Release.Rule.Architecture != options.Architecture ||
            package.Release.Version <= SemanticVersion.Parse(options.InstalledVersion))
            throw new InvalidDataException("A newer verified package must be downloaded first.");

        string target = Path.GetFullPath(Application.ExecutablePath);
        string source = Path.GetFullPath(package.ExtractedExecutablePath);
        string archive = Path.GetFullPath(package.ArchivePath);
        string stagingRoot = Path.GetFullPath(UpdateStateStore.GetStagingRoot(options));
        VerifyPaths(target, source, archive, stagingRoot);
        VerifyDigest(source, package.ExtractedExecutableSha256);
        VerifyDigest(archive, package.Release.Sha256);
        PeSafety.ValidateArchitecture(source, options.Architecture);

        // The helper deliberately executes the installed version, not the
        // new EXE, until integrity and post-replacement health checks pass.
        string helper = Path.Combine(Path.GetDirectoryName(source)!,
            "current-manager-updater-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(target, helper, overwrite: false);
        try
        {
            var start = new ProcessStartInfo(helper) {
                UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(helper)!,
                CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            foreach (string value in new[] { HelperSwitch, target, source,
                         package.ExtractedExecutableSha256, archive, package.Release.Sha256,
                         Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) })
                start.ArgumentList.Add(value);
            using var process = Process.Start(start) ??
                throw new InvalidOperationException("Could not start the portable update helper.");
        }
        catch
        {
            try { File.Delete(helper); } catch (IOException) { }
            throw;
        }
    }

    internal static int RunHelper(string[] args)
    {
        if (args.Length != 7 || args[0] != HelperSwitch) return 2;
        try
        {
            string target = Path.GetFullPath(args[1]);
            string source = Path.GetFullPath(args[2]);
            string stageSha = args[3];
            string archive = Path.GetFullPath(args[4]);
            string archiveSha = args[5];
            if (!int.TryParse(args[6], out int parentPid) || parentPid <= 0)
                throw new InvalidDataException("Invalid parent process ID.");
            var options = UniversalUpdaterWindow.PinnedOptions(UpdateChannel.Prerelease);
            string root = Path.GetFullPath(UpdateStateStore.GetStagingRoot(options));
            VerifyPaths(target, source, archive, root);
            // Another user, SYSTEM, or a scheduled service cannot be updated
            // through an elevated helper; this remains the interactive user.
            WaitForExit(parentPid, TimeSpan.FromSeconds(75));
            VerifyDigest(source, stageSha);
            VerifyDigest(archive, archiveSha);
            PeSafety.ValidateArchitecture(source, options.Architecture);
            using var updateLock = new FileStream(
                Path.Combine(Path.GetDirectoryName(target)!, ".lockscreen-update.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            string backup = target + ".bak";
            string earlierBackup = backup + ".previous";
            string next = target + ".incoming";
            if (File.Exists(next)) throw new IOException("A previous update is incomplete.");
            if (File.Exists(earlierBackup)) throw new IOException("A prior rollback checkpoint requires attention.");
            bool previousMoved = false;
            if (File.Exists(backup))
            {
                RejectLink(backup);
                File.Move(backup, earlierBackup);
                previousMoved = true;
            }
            bool replaced = false;
            try
            {
                File.Copy(source, next, overwrite: false);
                VerifyDigest(next, stageSha);
                File.Replace(next, target, backup, ignoreMetadataErrors: false);
                replaced = true;
                if (!SelfTest(target, TimeSpan.FromSeconds(90)))
                    throw new InvalidOperationException("Updated EXE failed the offline self-test.");
                VerifyDigest(target, stageSha);
                if (previousMoved) File.Delete(earlierBackup);
                StartManager(target);
                return 0;
            }
            catch
            {
                if (replaced && File.Exists(backup))
                {
                    string restore = target + ".restore";
                    File.Copy(backup, restore, overwrite: false);
                    File.Replace(restore, target, null, ignoreMetadataErrors: false);
                    if (previousMoved)
                    {
                        File.Delete(backup);
                        File.Move(earlierBackup, backup);
                    }
                    StartManager(target);
                }
                else if (previousMoved && File.Exists(earlierBackup))
                    File.Move(earlierBackup, backup);
                throw;
            }
            finally
            {
                if (File.Exists(next)) File.Delete(next);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Portable update helper");
            return 1;
        }
    }

    private static void WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                throw new TimeoutException("Manager did not exit. Update was not applied.");
        }
        catch (ArgumentException) { /* Main process already exited. */ }
    }

    private static bool SelfTest(string file, TimeSpan timeout)
    {
        using var process = Process.Start(new ProcessStartInfo(file) {
            ArgumentList = { "--self-test" }, UseShellExecute = false,
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new IOException("Could not run updated EXE self-test.");
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            return false;
        }
        return process.ExitCode == 0;
    }

    private static void StartManager(string file) =>
        Process.Start(new ProcessStartInfo(file) {
            WorkingDirectory = Path.GetDirectoryName(file)!,
            UseShellExecute = true
        });

    private static void VerifyPaths(string target, string source,
        string archive, string stagingRoot)
    {
        string profile = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(profile, StringComparison.OrdinalIgnoreCase) ||
            !target.EndsWith(Path.DirectorySeparatorChar + "LockScreenCampaignManager.exe",
                StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "In-app updates support only a portable EXE inside your user profile. " +
                "Administrator-owned and gMSA installations must be updated separately.");
        string stage = Path.GetDirectoryName(source)!;
        if (!string.Equals(Path.GetDirectoryName(stage), stagingRoot,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(archive), stage,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(source), "LockScreenCampaignManager.exe",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update source is outside verified user staging.");
        foreach (string p in new[] { target, source, archive, stage, stagingRoot,
            Path.GetDirectoryName(target)!, profile.TrimEnd(Path.DirectorySeparatorChar) })
            RejectLink(p);
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Portable update paths must not be junctions or links.");
    }

    private static void VerifyDigest(string file, string expected)
    {
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid SHA-256 digest.");
        using var stream = File.OpenRead(file);
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update file changed after verification.");
    }
}
