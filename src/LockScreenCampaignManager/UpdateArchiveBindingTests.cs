using System.IO.Compression;
using System.Security.Cryptography;
using UniversalUpdater;

namespace LockScreenCampaignManager;

/// <summary>Offline synthetic inputs only: no executable is launched or installed.</summary>
internal static class UpdateArchiveBindingTests
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "lscm-update-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string archive = Path.Combine(root, "update.zip");
            string exe = Path.Combine(root, "manager.exe");
            var rule = new UpdateAssetRule("win-x64", "update.zip", PackageKind.SingleExeZip, "manager.exe");
            byte[] original = [1, 2, 3, 4];
            byte[] changed = [4, 3, 2, 1];
            string originalHash = Convert.ToHexString(SHA256.HashData(original));
            string changedHash = Convert.ToHexString(SHA256.HashData(changed));
            WriteArchive(archive, original, extraEntry: false);
            string archiveHash = Digest(archive);
            File.WriteAllBytes(exe, original);
            SingleExeArchiveBinding.Verify(archive, archiveHash, exe, originalHash, rule, 4096, 4096);

            File.WriteAllBytes(exe, changed);
            // Even a newly calculated EXE digest must not validate a substituted EXE.
            Reject(() => SingleExeArchiveBinding.Verify(archive, archiveHash, exe, changedHash, rule, 4096, 4096));
            Reject(() => SingleExeArchiveBinding.Verify(archive, archiveHash, exe, originalHash, rule, 4096, 4096));
            File.WriteAllBytes(exe, original);
            Reject(() => SingleExeArchiveBinding.Verify(archive, new string('0', 64), exe, originalHash, rule, 4096, 4096));
            Reject(() => SingleExeArchiveBinding.Verify(archive, archiveHash, exe, originalHash, rule, 1, 4096));
            Reject(() => SingleExeArchiveBinding.Verify(archive, archiveHash, exe, originalHash, rule, 4096, 1));

            File.Delete(archive);
            WriteArchive(archive, original, extraEntry: true);
            Reject(() => SingleExeArchiveBinding.Verify(archive, Digest(archive), exe, originalHash, rule, 4096, 4096));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string Digest(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteArchive(string path, byte[] bytes, bool extraEntry)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var stream = zip.CreateEntry("manager.exe").Open())
            stream.Write(bytes);
        if (extraEntry)
        {
            using var stream = zip.CreateEntry("other.exe").Open();
            stream.WriteByte(1);
        }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Unsafe update package was accepted.");
    }
}
