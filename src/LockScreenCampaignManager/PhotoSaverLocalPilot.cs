using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace LockScreenCampaignManager;

/// <summary>
/// Explicit, current-user-only Photo Screen Saver pilot installer.
/// This never creates AD/GPO objects, changes HKCU Policies, elevates, or runs
/// the .scr. Installing per user is NOT a substitute for protected production
/// computer deployment and signed publisher identity.
/// </summary>
internal static class PhotoSaverLocalPilot
{
    private const string Schema = "lscm-photo-saver-local-pilot/v1";
    private const string DesktopKey = @"Control Panel\Desktop";
    private const string PolicyKey = @"Software\Policies\Microsoft\Windows\Control Panel\Desktop";
    private static readonly string[] ScreenSaverNames =
        ["SCRNSAVE.EXE", "ScreenSaveActive", "ScreenSaveTimeOut", "ScreenSaverIsSecure"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed class PriorValue
    {
        public bool Exists { get; set; }
        public string Value { get; set; } = "";
    }

    private sealed class LocalPilotState
    {
        public string Schema { get; set; } = PhotoSaverLocalPilot.Schema;
        public string ScrPath { get; set; } = "";
        public Dictionary<string, PriorValue> Previous { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Expected { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    internal static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LockScreenCampaignManager", "PhotoSaverPilot");

    internal static string InstalledScr => Path.Combine(
        Root, "package", "PhotoScreenSaver.scr");

    internal static bool HasPilot => File.Exists(Path.Combine(Root, "state.json"));

    internal static string Install(string sourceFolder, int timeoutSeconds, bool requirePassword)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Local Photo Screen Saver requires Windows.");
        if (timeoutSeconds is < 60 or > 86400)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        ValidateLocalDirectory(sourceFolder);
        var verified = PhotoJpegSaver.Verify(sourceFolder, checkExecutable: true);
        if (verified.Images.Length == 0)
            throw new InvalidDataException("Pilot package has no verified JPG images.");
        ValidatePolicyNotManaged();

        string root = Root;
        string package = Path.Combine(root, "package");
        string statePath = Path.Combine(root, "state.json");
        if (HasPilot || Directory.Exists(package))
            throw new InvalidOperationException(
                "A local JPG Screen Saver pilot is already installed. Remove it before installing a new one.");
        ValidateExistingParents(root);
        Directory.CreateDirectory(root);
        string staging = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        bool installed = false;
        LocalPilotState? state = null;
        try
        {
            string images = Path.Combine(staging, "images");
            Directory.CreateDirectory(images);
            File.Copy(Path.Combine(sourceFolder, "PhotoScreenSaver.scr"),
                Path.Combine(staging, "PhotoScreenSaver.scr"));
            File.Copy(Path.Combine(sourceFolder, "slideshow.json"),
                Path.Combine(staging, "slideshow.json"));
            foreach (string path in verified.Images)
                File.Copy(path, Path.Combine(images, Path.GetFileName(path)));
            PhotoJpegSaver.Verify(staging, checkExecutable: true);

            using var desktop = Registry.CurrentUser.CreateSubKey(DesktopKey,
                writable: true) ?? throw new UnauthorizedAccessException("Cannot open user Desktop settings.");
            state = new LocalPilotState
            {
                ScrPath = InstalledScr,
                Previous = ReadPrevious(desktop),
                Expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SCRNSAVE.EXE"] = InstalledScr,
                    ["ScreenSaveActive"] = "1",
                    ["ScreenSaveTimeOut"] =
                        timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["ScreenSaverIsSecure"] = requirePassword ? "1" : "0"
                }
            };
            // Persist the rollback record BEFORE the first registry write.
            // Do not replace an old state file or a foreign installation.
            using (var stateFile = new FileStream(statePath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stateFile, new UTF8Encoding(false)))
                writer.Write(JsonSerializer.Serialize(state, Json));

            Directory.Move(staging, package);
            PhotoJpegSaver.Verify(package, checkExecutable: true);
            ValidatePolicyNotManaged(); // Policy may have arrived during file copy.
            foreach (string name in ScreenSaverNames)
                desktop.SetValue(name, state.Expected[name], RegistryValueKind.String);
            NotifyDesktop();
            installed = true;
            return InstalledScr;
        }
        finally
        {
            if (!installed)
            {
                if (state is not null)
                {
                    try
                    {
                        using var desktop = Registry.CurrentUser.CreateSubKey(DesktopKey, writable: true);
                        if (desktop is not null) Restore(desktop, state.Previous);
                        NotifyDesktop();
                    }
                    catch (Exception ex) { AppLog.Write(ex, "Photo Saver pilot install rollback"); }
                }
                try { if (File.Exists(statePath)) File.Delete(statePath); }
                catch (Exception ex) { AppLog.Write(ex, "Photo Saver pilot state rollback"); }
                try { if (Directory.Exists(package)) Directory.Delete(package, recursive: true); }
                catch (Exception ex) { AppLog.Write(ex, "Photo Saver pilot package rollback"); }
            }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception ex) { AppLog.Write(ex, "Photo Saver pilot staging cleanup"); }
        }
    }

    internal static bool Remove()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Local Photo Screen Saver requires Windows.");
        string root = Root;
        string statePath = Path.Combine(root, "state.json");
        if (!File.Exists(statePath)) return false;
        ValidateExistingParents(root);
        FileInfo stateInfo = new(statePath);
        if ((stateInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
            stateInfo.Length is < 50 or > 16384)
            throw new InvalidDataException("Local JPG pilot rollback file is unsafe.");
        LocalPilotState state = JsonSerializer.Deserialize<LocalPilotState>(
            File.ReadAllText(statePath), Json) ??
            throw new InvalidDataException("Local JPG pilot rollback file is empty.");
        if (state.Schema != Schema ||
            !string.Equals(state.ScrPath, InstalledScr, StringComparison.OrdinalIgnoreCase) ||
            state.Previous is null || state.Expected is null ||
            state.Previous.Count != ScreenSaverNames.Length ||
            state.Expected.Count != ScreenSaverNames.Length ||
            ScreenSaverNames.Any(name => !state.Previous.ContainsKey(name) ||
                !state.Expected.ContainsKey(name)) ||
            state.Previous.Any(pair => !ScreenSaverNames.Contains(pair.Key,
                StringComparer.OrdinalIgnoreCase) || pair.Value is null) ||
            state.Expected.Any(pair => !ScreenSaverNames.Contains(pair.Key,
                StringComparer.OrdinalIgnoreCase) || pair.Value is null) ||
            state.Expected["ScreenSaveActive"] != "1" ||
            state.Expected["ScreenSaverIsSecure"] is not ("0" or "1") ||
            !int.TryParse(state.Expected["ScreenSaveTimeOut"], out int timeout) ||
            timeout is < 60 or > 86400 ||
            !state.Expected["SCRNSAVE.EXE"].Equals(InstalledScr,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local JPG pilot rollback data is invalid.");

        ValidatePolicyNotManaged();
        using var desktop = Registry.CurrentUser.OpenSubKey(DesktopKey, writable: true) ??
            throw new InvalidOperationException("Current user Desktop settings are missing.");
        foreach (string name in ScreenSaverNames)
        {
            if (desktop.GetValueKind(name) != RegistryValueKind.String ||
                !string.Equals(desktop.GetValue(name, null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames) as string,
                    state.Expected[name], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "A current-user screen saver setting changed since pilot installation: " +
                    name + ". Automatic rollback is blocked to protect that change.");
        }

        Restore(desktop, state.Previous);
        NotifyDesktop();
        File.Delete(statePath);
        string package = Path.Combine(root, "package");
        if (Directory.Exists(package)) Directory.Delete(package, recursive: true);
        return true;
    }

    private static void ValidateLocalDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full) ||
            full.StartsWith(@"\\", StringComparison.Ordinal) ||
            full.StartsWith(@"\\?\", StringComparison.Ordinal))
            throw new InvalidDataException("The pilot package must be in a normal local folder.");
        ValidateExistingParents(full);
    }

    private static void ValidateExistingParents(string path)
    {
        for (DirectoryInfo? folder = new(Path.GetFullPath(path));
             folder is not null; folder = folder.Parent)
        {
            if (!folder.Exists) continue;
            if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked or redirected pilot directories are not supported.");
        }
    }

    private static void ValidatePolicyNotManaged()
    {
        foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var policy = hive.OpenSubKey(PolicyKey);
            if (policy is null) continue;
            if (ScreenSaverNames.Any(name =>
                policy.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    "Screen Saver settings are managed by Group Policy. Local pilot refuses to override them.");
        }
    }

    private static Dictionary<string, PriorValue> ReadPrevious(RegistryKey desktop)
    {
        var found = new Dictionary<string, PriorValue>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in ScreenSaverNames)
        {
            bool exists = desktop.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase);
            string value = "";
            if (exists)
            {
                if (desktop.GetValueKind(name) != RegistryValueKind.String)
                    throw new InvalidOperationException(
                        "Cannot back up non-string Screen Saver value: " + name);
                value = desktop.GetValue(name, "",
                    RegistryValueOptions.DoNotExpandEnvironmentNames) as string ??
                    throw new InvalidDataException("Invalid Screen Saver desktop registry value.");
            }
            found[name] = new PriorValue { Exists = exists, Value = value };
        }
        return found;
    }

    private static void Restore(RegistryKey desktop,
        IReadOnlyDictionary<string, PriorValue> previous)
    {
        foreach (var name in ScreenSaverNames)
        {
            var item = previous[name];
            if (item.Exists) desktop.SetValue(name, item.Value, RegistryValueKind.String);
            else desktop.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window,
        uint message, IntPtr wParam, string lParam, uint flags,
        uint timeout, out IntPtr result);

    private static void NotifyDesktop()
    {
        if (!OperatingSystem.IsWindows()) return;
        // Notification only; it does not write additional policy/registry values.
        // A Windows sign-out/in may still be required on some builds.
        _ = SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero,
            "Control Panel\\Desktop", 0x0002, 1000, out _);
    }
}
