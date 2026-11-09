using System.Drawing;
using System.Drawing.Drawing2D;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

internal sealed class PhotoSaverImage
{
    public string Name { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

internal sealed class PhotoSaverManifest
{
    public string Schema { get; set; } = "lscm-photo-saver/v1";
    public int IntervalSeconds { get; set; } = 15;
    public string ExecutableSha256 { get; set; } = "";
    public List<PhotoSaverImage> Images { get; set; } = [];
}

/// <summary>
/// Real JPG screen saver package in the standard Windows .scr format:
/// a renamed self-contained Manager apphost + normalized, hash-verified JPGs.
/// It is deliberately a LOCAL pilot/export only. It does not install, edit
/// the user's screen saver settings, modify HKCU, or deploy a domain GPO.
/// </summary>
internal static class PhotoJpegSaver
{
    private static readonly Regex ImageName = new(
        @"^[0-9]{2,3}\.jpg$", RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Digest = new(
        @"^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly JsonSerializerOptions Json = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true, PropertyNameCaseInsensitive = true
    };

    internal static bool IsScrProcess
    {
        get
        {
            string path = Environment.ProcessPath ?? "";
            return Path.GetExtension(path).Equals(".scr",
                StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static string Export(CampaignProject project, string destinationParent)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.ScreenSaver.UseJpegSlideshow ||
            project.EffectiveCampaignMode == CampaignTargetMode.LockScreen)
            throw new CampaignValidationException("Choose JPG Screen Saver before exporting a slideshow.");
        CampaignSchedule.Validate(project, requireImages: true);
        string process = Environment.ProcessPath ?? "";
        if (!string.Equals(Path.GetFileName(process), "LockScreenCampaignManager.exe",
                StringComparison.OrdinalIgnoreCase) || !File.Exists(process))
            throw new InvalidOperationException("Use the verified portable manager EXE to export JPG Screen Saver.");
        string parent = Path.GetFullPath(destinationParent);
        if (parent.StartsWith(@"\\", StringComparison.Ordinal) ||
            parent.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            !Directory.Exists(parent) ||
            (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Export must use an existing normal local folder.");
        for (var directory = new DirectoryInfo(parent);
             directory is not null && directory.Parent is not null;
             directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked export folder is not allowed.");
        string basename = "JPG-ScreenSaver-" + CampaignSchedule.BuildId(project);
        string output = Path.Combine(parent, basename);
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException("Photo Screen Saver export folder already exists: " + basename);
        string staging = Path.Combine(parent, "." + basename + "-" +
            Guid.NewGuid().ToString("N") + ".tmp");
        Directory.CreateDirectory(staging);
        try
        {
            string imageFolder = Path.Combine(staging, "images");
            Directory.CreateDirectory(imageFolder);
            var list = new List<PhotoSaverImage>();
            if (project.ImagePaths.Count > 100)
                throw new CampaignValidationException("JPG Screen Saver supports up to 100 images.");
            for (int i = 0; i < project.ImagePaths.Count; ++i)
            {
                string name = (i + 1).ToString("D2") + ".jpg";
                string target = Path.Combine(imageFolder, name);
                CampaignExporter.WriteNormalizedJpeg(project.ImagePaths[i], target);
                list.Add(new PhotoSaverImage { Name = name, Sha256 = Sha(target) });
            }
            string executable = Path.Combine(staging, "PhotoScreenSaver.scr");
            var source = new FileInfo(process);
            if (source.Length is < 1024 or > 250L * 1024 * 1024 ||
                (source.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Portable manager binary is missing or unsafe.");
            File.Copy(process, executable);
            var config = new PhotoSaverManifest {
                IntervalSeconds = project.ScreenSaver.SlideSeconds,
                ExecutableSha256 = Sha(executable), Images = list
            };
            File.WriteAllText(Path.Combine(staging, "slideshow.json"),
                JsonSerializer.Serialize(config, Json) + Environment.NewLine,
                new UTF8Encoding(false));
            Verify(staging, checkExecutable: true);
            File.WriteAllText(Path.Combine(staging, "INSTALL-LOCAL-PILOT.txt"),
                "JPG Screen Saver local pilot package\r\n" +
                "The file PhotoScreenSaver.scr is a self-contained Windows screensaver.\r\n" +
                "Use it ONLY on an authorized local Windows 10/11 test computer.\r\n" +
                "Keep its slideshow.json and images folder together.\r\n" +
                "For testing, run: PhotoScreenSaver.scr /s\r\n" +
                "The /p preview and /c settings switches are supported.\r\n" +
                "The interval is configured in slideshow.json at export.\r\n" +
                "Do not set a User GPO SCRNSAVE.EXE to this package yet: the " +
                "domain delivery and trust checks have not been approved.\r\n",
                new UTF8Encoding(false));
            Directory.Move(staging, output);
            return output;
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (Exception ex) { AppLog.Write(ex, "Photo Saver export cleanup"); }
            throw;
        }
    }

    private static string Sha(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static (int IntervalSeconds, string[] Images) Verify(
        string folder, bool checkExecutable)
    {
        string full = Path.GetFullPath(folder);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked Photo Saver package root is unsafe.");
        string imageFolder = Path.Combine(full, "images");
        if (!Directory.Exists(imageFolder) ||
            (File.GetAttributes(imageFolder) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Photo Saver JPG directory missing or linked.");
        string configPath = Path.Combine(full, "slideshow.json");
        var configInfo = new FileInfo(configPath);
        if (!configInfo.Exists || configInfo.Length is < 32 or > 128 * 1024 ||
            (configInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Photo Saver manifest missing or unsafe.");
        var config = JsonSerializer.Deserialize<PhotoSaverManifest>(
            File.ReadAllText(configPath), Json) ??
            throw new InvalidDataException("Photo Saver manifest empty.");
        if (config.Schema != "lscm-photo-saver/v1" ||
            config.IntervalSeconds is < 5 or > 3600 ||
            config.Images is null || config.Images.Count is < 1 or > 100 ||
            !Digest.IsMatch(config.ExecutableSha256))
            throw new InvalidDataException("Invalid Photo Saver configuration.");
        if (checkExecutable)
        {
            string exe = Path.Combine(full, "PhotoScreenSaver.scr");
            var exeInfo = new FileInfo(exe);
            if (!exeInfo.Exists || exeInfo.Length is < 1024 or > 250L * 1024 * 1024 ||
                (exeInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
                !Sha(exe).Equals(config.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Photo Saver executable hash mismatch.");
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var verified = new List<string>();
        foreach (var image in config.Images)
        {
            if (!ImageName.IsMatch(image.Name) || !names.Add(image.Name) ||
                !Digest.IsMatch(image.Sha256))
                throw new InvalidDataException("Invalid or duplicate Photo Saver JPG name/hash.");
            string path = Path.Combine(imageFolder, image.Name);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is < 4 or > 20L * 1024 * 1024 ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                !Sha(path).Equals(image.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Photo Saver JPG missing or hash mismatch.");
            verified.Add(path);
        }
        if (Directory.EnumerateFiles(imageFolder).Count() != verified.Count)
            throw new InvalidDataException("Unexpected file in Photo Saver images directory.");
        return (config.IntervalSeconds, verified.ToArray());
    }

    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length > 0 &&
                args[0].Equals("--photo-saver-self-test", StringComparison.OrdinalIgnoreCase))
            {
                Verify(AppContext.BaseDirectory, checkExecutable: true);
                return 0;
            }
            string mode = args.Length == 0 ? "/c" : args[0].ToLowerInvariant();
            if (mode is "/c" or "-c")
            {
                MessageBox.Show(
                    "This JPG screen saver is managed by LockScreen Campaign Manager.\n" +
                    "Select photos and the interval in the campaign wizard.",
                    "JPG Screen Saver", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            if (mode is not ("/s" or "-s" or "/p" or "-p"))
                return 2;
            var (interval, files) = Verify(AppContext.BaseDirectory, checkExecutable: true);
            ApplicationConfiguration.Initialize();
            if (mode is "/p" or "-p")
            {
                if (args.Length < 2 || !long.TryParse(args[1], out long raw) || raw == 0)
                    return 2;
                using var preview = new PhotoSaverWindow(files, interval, new IntPtr(raw));
                Application.Run(preview);
            }
            else
            {
                using var full = new PhotoSaverWindow(files, interval, IntPtr.Zero);
                Application.Run(full);
            }
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "JPG Photo Screen Saver");
            return 1;
        }
    }
}

internal sealed class PhotoSaverWindow : Form
{
    private readonly string[] _images;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly bool _preview;
    private int _index;
    private Point? _firstMouse;
    private Image? _current;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out Rectangle rect);

    internal PhotoSaverWindow(string[] files, int seconds, IntPtr previewParent)
    {
        _images = files;
        _preview = previewParent != IntPtr.Zero;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        BackColor = Color.Black;
        StartPosition = FormStartPosition.Manual;
        if (_preview)
        {
            TopLevel = false;
            if (!GetClientRect(previewParent, out Rectangle rect))
                throw new InvalidOperationException("Cannot attach Windows photo preview.");
            Bounds = new Rectangle(0, 0, Math.Max(1, rect.Width), Math.Max(1, rect.Height));
            _ = Handle;
            if (SetParent(Handle, previewParent) == IntPtr.Zero)
                throw new InvalidOperationException("Windows screen saver preview parent rejected.");
        }
        else
        {
            TopMost = true;
            Bounds = SystemInformation.VirtualScreen;
            Cursor.Hide();
        }
        KeyDown += (_, _) => { if (!_preview) Close(); };
        MouseDown += (_, _) => { if (!_preview) Close(); };
        MouseMove += (_, e) =>
        {
            if (_preview) return;
            if (_firstMouse is null) { _firstMouse = e.Location; return; }
            if (Math.Abs(e.X - _firstMouse.Value.X) > 8 ||
                Math.Abs(e.Y - _firstMouse.Value.Y) > 8)
                Close();
        };
        LoadImage();
        _timer = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
        _timer.Tick += (_, _) => { _index = (_index + 1) % _images.Length; LoadImage(); };
        Shown += (_, _) => _timer.Start();
    }

    private void LoadImage()
    {
        _current?.Dispose();
        using var original = Image.FromFile(_images[_index]);
        _current = new Bitmap(original);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_current is null || ClientSize.Width < 1 || ClientSize.Height < 1)
            return;
        e.Graphics.Clear(Color.Black);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        double scale = Math.Min((double)ClientSize.Width / _current.Width,
            (double)ClientSize.Height / _current.Height);
        int w = Math.Max(1, (int)(_current.Width * scale));
        int h = Math.Max(1, (int)(_current.Height * scale));
        e.Graphics.DrawImage(_current,
            (ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Stop();
            _timer?.Dispose();
            _current?.Dispose();
            if (!_preview) Cursor.Show();
        }
        base.Dispose(disposing);
    }
}
