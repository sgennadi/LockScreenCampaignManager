using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Shared, per-user geometry persistence for resizable WinForms dialogs.
/// Respects monitor removal and HiDPI changes; never stores AD details,
/// credentials, or any content from the window being positioned.
/// </summary>
internal sealed class UiSavedWindow
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Dpi { get; set; } = 96;
    public bool Maximized { get; set; }
}

internal static class UiWindowPlacement
{
    private static readonly object Gate = new();
    private static readonly Regex KeyPattern = new(
        @"^[a-z][a-z0-9-]{2,49}$", RegexOptions.CultureInvariant);
    private static string FilePath => Path.Combine(
        AppLog.DirectoryPath, "ui-window-placement.json");

    // This pure method can be tested without opening a GUI or querying a
    // physical display; always keep at least part of the title bar visible.
    internal static Rectangle ClampToScreens(Rectangle desired, Size minimum,
        IReadOnlyList<Rectangle> workingAreas)
    {
        if (workingAreas.Count == 0)
            throw new ArgumentException("At least one display working area is required.");
        var screens = workingAreas.Where(x => x.Width > 0 && x.Height > 0)
            .ToArray();
        if (screens.Length == 0)
            throw new ArgumentException("Display working area has invalid dimensions.");
        Rectangle area = screens.OrderByDescending(x =>
        {
            var overlap = Rectangle.Intersect(x, desired);
            return (long)Math.Max(0, overlap.Width) * Math.Max(0, overlap.Height);
        }).First();
        // Windows DPI switching may restore a size larger than the new monitor.
        int minWidth = Math.Clamp(minimum.Width, 1, area.Width);
        int minHeight = Math.Clamp(minimum.Height, 1, area.Height);
        int width = Math.Clamp(desired.Width, minWidth, area.Width);
        int height = Math.Clamp(desired.Height, minHeight, area.Height);
        int x = Math.Clamp(desired.X, area.Left, area.Right - width);
        int y = Math.Clamp(desired.Y, area.Top, area.Bottom - height);
        return new Rectangle(x, y, width, height);
    }

    private static Dictionary<string, UiSavedWindow> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new(StringComparer.Ordinal);
            var info = new FileInfo(FilePath);
            if (info.Length is < 2 or > 32768 ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
                return new(StringComparer.Ordinal);
            var values = JsonSerializer.Deserialize<Dictionary<string, UiSavedWindow>>(
                File.ReadAllText(FilePath)) ?? new();
            if (values.Count > 40)
                return new(StringComparer.Ordinal);
            return new Dictionary<string, UiSavedWindow>(values, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or
            UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Write(ex, "Read UI window positions");
            return new(StringComparer.Ordinal);
        }
    }

    internal static void Attach(Form form, string key)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!KeyPattern.IsMatch(key))
            throw new ArgumentException("Window placement key is invalid.");

        form.Shown += (_, _) =>
        {
            try
            {
                UiSavedWindow? saved;
                lock (Gate) _ = Load().TryGetValue(key, out saved);
                if (saved is null ||
                    saved.Width is < 300 or > 10000 ||
                    saved.Height is < 240 or > 10000 ||
                    saved.Dpi is < 96 or > 384)
                    return;
                double factor = Math.Clamp(
                    form.DeviceDpi / (double)saved.Dpi, 0.5, 2.5);
                int width = (int)Math.Round(saved.Width * factor);
                int height = (int)Math.Round(saved.Height * factor);
                var target = ClampToScreens(
                    new Rectangle(saved.X, saved.Y, width, height),
                    form.MinimumSize,
                    Screen.AllScreens.Select(screen => screen.WorkingArea).ToArray());
                form.StartPosition = FormStartPosition.Manual;
                form.Bounds = target;
                if (saved.Maximized && form.MaximizeBox)
                    form.WindowState = FormWindowState.Maximized;
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "Restore UI window position");
            }
        };
        form.FormClosed += (_, _) =>
        {
            try
            {
                Rectangle bounds = form.WindowState == FormWindowState.Normal
                    ? form.Bounds : form.RestoreBounds;
                if (bounds.Width < 300 || bounds.Height < 240)
                    return;
                var saved = new UiSavedWindow {
                    X = bounds.X, Y = bounds.Y,
                    Width = bounds.Width, Height = bounds.Height,
                    Dpi = Math.Clamp(form.DeviceDpi, 96, 384),
                    Maximized = form.WindowState == FormWindowState.Maximized
                };
                lock (Gate)
                {
                    var all = Load();
                    all[key] = saved;
                    Directory.CreateDirectory(AppLog.DirectoryPath);
                    var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllText(temp, JsonSerializer.Serialize(all,
                            new JsonSerializerOptions { WriteIndented = true }));
                        File.Move(temp, FilePath, overwrite: true);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
            }
            catch (Exception ex) { AppLog.Write(ex, "Save UI window position"); }
        };
    }
}
