using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Exports a self-contained, READ-ONLY pilot kit for MANUAL GPMC staging.
/// Never creates/links GPOs, sets HKCU/HKLM, copies to SYSVOL, or deploys.
/// Startup only copies files on explicitly selected LAB computers under SYSTEM.
/// </summary>
internal static class PhotoSaverGpoPilotKit
{
    private const string InstallResource =
        "LockScreenCampaignManager.PhotoSaverStartupInstall.ps1";
    private static readonly Regex SafeId = new(
        @"^[a-z0-9-]{5,75}$", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex HexDigest = new(
        @"^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly string[] Tokens =
    [
        "__LSCM_CAMPAIGN_ID__", "__LSCM_SOURCE_FOLDER__",
        "__LSCM_MANIFEST_SHA256__", "__LSCM_SCR_SHA256__",
        "__LSCM_SLIDE_SECONDS__"
    ];

    internal static string RenderStartupScript(string template, string id,
        string sourceFolder, string manifestSha256, string scrSha256, int seconds)
    {
        if (!SafeId.IsMatch(id) ||
            !sourceFolder.Equals("JPG-ScreenSaver-" + id, StringComparison.Ordinal) ||
            !HexDigest.IsMatch(manifestSha256) ||
            !HexDigest.IsMatch(scrSha256) ||
            seconds is < 5 or > 3600)
            throw new InvalidDataException("Invalid JPG GPO pilot script identity.");
        string[] values =
        [
            id, sourceFolder, manifestSha256.ToLowerInvariant(),
            scrSha256.ToLowerInvariant(), seconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        ];
        for (int i = 0; i < Tokens.Length; i++)
        {
            string marker = Tokens[i];
            if (template.Split(marker, StringSplitOptions.None).Length != 2)
                throw new InvalidDataException(
                    "JPG GPO startup template has missing/duplicate marker.");
            template = template.Replace(marker, values[i], StringComparison.Ordinal);
        }
        if (Tokens.Any(template.Contains))
            throw new InvalidDataException("Unexpanded JPG GPO pilot template marker.");
        return template;
    }

    internal static string LoadTemplate()
    {
        using var resource = typeof(PhotoSaverGpoPilotKit).Assembly
            .GetManifestResourceStream(InstallResource) ??
            throw new InvalidDataException(
                "Embedded JPG GPO startup script is missing.");
        using var reader = new StreamReader(resource, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        string content = reader.ReadToEnd();
        if (content.Length is < 500 or > 100_000)
            throw new InvalidDataException("Invalid JPG GPO startup resource length.");
        return content;
    }

    private static string Hash(string path)
    {
        using var read = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(read)).ToLowerInvariant();
    }

    private static void AssertSafeLocalDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal) ||
            full.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            !Directory.Exists(full))
            throw new InvalidDataException("JPG GPO kit requires an existing local folder.");
        for (DirectoryInfo? dir = new(full); dir is not null; dir = dir.Parent)
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "JPG GPO kit refuses linked, junction or redirected directories.");
    }

    internal static string Export(CampaignProject project,
        string photoPackage, string destinationParent)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.EffectiveCampaignMode == CampaignTargetMode.LockScreen ||
            !project.ScreenSaver.UseJpegSlideshow)
            throw new CampaignValidationException(
                "Choose JPG Screen Saver or Both before exporting a GPO lab kit.");
        string id = CampaignSchedule.BuildId(project);
        if (!SafeId.IsMatch(id))
            throw new CampaignValidationException("Invalid JPG campaign ID.");
        string photoName = "JPG-ScreenSaver-" + id;
        AssertSafeLocalDirectory(photoPackage);
        AssertSafeLocalDirectory(destinationParent);
        if (!Path.GetFileName(Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(photoPackage))).Equals(photoName,
                    StringComparison.Ordinal))
            throw new InvalidDataException(
                "Select the JPG package exported for this exact campaign.");
        var (interval, images) = PhotoJpegSaver.Verify(photoPackage,
            checkExecutable: true);
        if (interval != project.ScreenSaver.SlideSeconds ||
            images.Length != project.ImagePaths.Count)
            throw new InvalidDataException(
                "JPG package no longer matches the campaign image count or slide interval.");
        // This is staged into a manually configured TEST GPO's SYSVOL.
        // Refuse an unbounded payload that could saturate replication.
        const long maxPilotBytes = 256L * 1024 * 1024;
        long packageBytes = new FileInfo(Path.Combine(photoPackage,
            "PhotoScreenSaver.scr")).Length +
            new FileInfo(Path.Combine(photoPackage, "slideshow.json")).Length +
            images.Sum(image => new FileInfo(image).Length);
        if (packageBytes > maxPilotBytes)
            throw new CampaignValidationException(
                "JPG GPO LAB package exceeds 256 MiB. Reduce the pilot photos " +
                "or use a separately approved content distribution system; " +
                "do not copy large image campaigns to SYSVOL.");

        string manifestHash = Hash(Path.Combine(photoPackage, "slideshow.json"));
        string scrHash = Hash(Path.Combine(photoPackage, "PhotoScreenSaver.scr"));
        string script = RenderStartupScript(LoadTemplate(), id, photoName,
            manifestHash, scrHash, interval);

        string outputName = "JPG-GPO-Lab-Kit-" + id;
        string output = Path.Combine(destinationParent, outputName);
        if (File.Exists(output) || Directory.Exists(output))
            throw new IOException("JPG GPO lab kit already exists; never overwrite: " + output);
        string stage = Path.Combine(destinationParent, "." + outputName +
            "-" + Guid.NewGuid().ToString("N") + ".tmp");
        Directory.CreateDirectory(stage);
        try
        {
            string copy = Path.Combine(stage, photoName);
            Directory.CreateDirectory(copy);
            Directory.CreateDirectory(Path.Combine(copy, "images"));
            File.Copy(Path.Combine(photoPackage, "PhotoScreenSaver.scr"),
                Path.Combine(copy, "PhotoScreenSaver.scr"));
            File.Copy(Path.Combine(photoPackage, "slideshow.json"),
                Path.Combine(copy, "slideshow.json"));
            foreach (string file in images)
                File.Copy(file, Path.Combine(copy, "images",
                    Path.GetFileName(file)));
            var checkedCopy = PhotoJpegSaver.Verify(copy, true);
            if (checkedCopy.IntervalSeconds != interval ||
                checkedCopy.Images.Length != images.Length ||
                Hash(Path.Combine(copy, "slideshow.json")) != manifestHash ||
                Hash(Path.Combine(copy, "PhotoScreenSaver.scr")) != scrHash)
                throw new InvalidDataException(
                    "GPO kit copy SHA-256 verification failed.");
            File.WriteAllText(Path.Combine(stage, "PhotoSaver-StartupInstall.ps1"),
                script, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(stage, "LAB-GPO-INSTRUCTIONS.txt"),
                Instructions(id, photoName, manifestHash, scrHash),
                new UTF8Encoding(false));
            Directory.Move(stage, output);
            return output;
        }
        catch
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            catch (Exception ex) { AppLog.Write(ex, "JPG GPO kit cleanup"); }
            throw;
        }
    }

    private static string Instructions(string id, string folder, string manifest,
        string executable) => string.Join("\r\n",
    [
        "JPG SCREEN SAVER - MANUAL COMPUTER GPO LAB KIT ONLY",
        "==================================================",
        "Campaign ID: " + id,
        "This kit makes NO GPO / AD / SYSVOL changes by itself.",
        "It contains a PowerShell 5.1 Startup installer that copies verified",
        "images and the portable SCR into a protected LOCAL computer directory.",
        "",
        "1) Test PhotoScreenSaver.scr /s and /p in a local Windows 10/11 pilot first.",
        "2) In GPMC manually create a dedicated TEST Computer Startup GPO linked",
        "   ONLY to a small pilot computer OU with a dedicated COMPUTER group.",
        "3) Stage PhotoSaver-StartupInstall.ps1 and its sibling folder " + folder,
        "   together in that GPO's Computer Startup script directory, and",
        "   reference PhotoSaver-StartupInstall.ps1 with parameter -Mode Install.",
        "   The script MUST run under LocalSystem at Windows startup.",
        "4) Reboot one test PC; inspect output and run the script with -Mode Verify",
        "   from the SAME staging directory. A successful check does not prove",
        "   Windows screensaver execution, sign-in on resume, or GPO RSoP.",
        "5) Confirm local file and image hashes, protected ACL and real /s /p.",
        "6) User Screen Saver GPO activation is NOT included in this kit.",
        "   Never target a user group until every eligible computer has the",
        "   matching protected local SCR and effective user GPO is validated.",
        "",
        @"Local target: C:\ProgramData\LSCM-PhotoSaver\" + id,
        @"Proposed SCRNSAVE.EXE (NOT APPLIED): C:\ProgramData\LSCM-PhotoSaver\" +
            id + @"\PhotoScreenSaver.scr",
        "Never run this SCR directly from SYSVOL; do not use a local-only",
        "user-profile path for a domain User GPO.",
        "No loopback policies are enabled. Built-in Windows saver GPOs unchanged.",
        "Exact package manifest SHA256: " + manifest,
        "Exact portable SCR SHA256: " + executable,
        "REMOVAL / ROLLBACK: do not blindly delete the computer package while",
        "a User GPO or user profile may reference its path. Remove/revert user",
        "policy first, then revoke Computer GPO scope and inspect endpoints.",
        "LAB ONLY - Test AppLocker/WDAC, multi-monitor and power/lock behavior.",
        ""
    ]);
}
