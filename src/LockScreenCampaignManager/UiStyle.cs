using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

internal sealed class UiPreferences
{
    public string Density { get; set; } = "Auto";
    public int UiScalePercent { get; set; } = 100;
    public int FontScalePercent { get; set; } = 100;
    private static readonly string SettingsFile = Path.Combine(AppLog.DirectoryPath, "ui-settings.json");
    public UiPreferences Validate()
    {
        if (Density is not ("Auto" or "Compact" or "Comfortable")) Density = "Auto";
        UiScalePercent = Math.Clamp(UiScalePercent, 100, 150);
        FontScalePercent = Math.Clamp(FontScalePercent, 90, 125);
        return this;
    }
    public static UiPreferences Load()
    {
        try { return File.Exists(SettingsFile)
            ? (JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(SettingsFile)) ?? new()).Validate()
            : new(); }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(AppLog.DirectoryPath);
        string temp = SettingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(Validate(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, SettingsFile, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

internal enum UiStatusKind { Neutral, Busy, Success, Warning, Error }

internal static class UiStyle
{
    public static UiPreferences Preferences { get; private set; } = UiPreferences.Load();
    // BitKeyBridge / NI-inspired visual tokens, with Windows High Contrast fallback.
    public static Color Navy => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(11,46,90);
    public static Color Green => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(10,134,76);
    public static Color SoftBlue => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(226,239,255);
    public static Color SoftGreen => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(225,248,234);
    public static Color Canvas => SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(244,247,253);
    public static Color Surface => SystemColors.Window;
    public static Color Accent => SystemInformation.HighContrast ? SystemColors.HotTrack : Color.FromArgb(14,106,230);
    public static Color Border => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(211,225,243);
    public static Color Muted => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(94,112,142);
    public static Color Foreground => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(20,39,68);
    public static void ApplyStatus(Label label, string value, UiStatusKind status)
    {
        label.Text = value;
        label.AutoEllipsis = false;
        label.TabStop = false;
        label.AccessibleName = "Active Directory connection status";
        label.AccessibleDescription = value;
        label.Padding = new Padding(9, 6, 9, 6);
        if (SystemInformation.HighContrast)
        {
            label.BackColor = SystemColors.Window;
            label.ForeColor = SystemColors.WindowText;
            return;
        }
        label.BackColor = status switch
        {
            UiStatusKind.Success => SoftGreen,
            UiStatusKind.Warning => Color.FromArgb(255,243,216),
            UiStatusKind.Error => Color.FromArgb(255,225,226),
            _ => SoftBlue
        };
        label.ForeColor = status switch
        {
            UiStatusKind.Success => Green,
            UiStatusKind.Warning => Color.FromArgb(152,88,14),
            UiStatusKind.Error => Color.FromArgb(167,43,43),
            _ => Navy
        };
    }
    private sealed record FontBaseline(string Family, float Size, FontStyle Style);
    private static readonly ConditionalWeakTable<Control, FontBaseline> Fonts = new();
    private static readonly ConditionalWeakTable<Button, object> Rounded = new();
    private static readonly Dictionary<string, Font> FontCache = new();
    public static void ChangePreferences(UiPreferences value)
    {
        Preferences = value.Validate();
        Preferences.Save();
        foreach (var form in Application.OpenForms.Cast<Form>().ToArray()) Apply(form);
    }
    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (Control item in Descendants(child)) yield return item;
    }
    // Override is used for non-persistent CI layout simulation only; it never
    // writes to the user's settings or changes the global UI preferences.
    public static void Apply(Form form, UiPreferences? previewPreferences = null)
    {
        if (form.IsDisposed) return;
        form.Icon = UiGlyphs.AppIcon;
        var effectivePreferences = previewPreferences is null
            ? Preferences : previewPreferences.Validate();
        var all = Descendants(form).ToArray();
        foreach (var c in all) _ = Fonts.GetValue(c, x =>
            new FontBaseline(x.Font.FontFamily.Name, x.Font.SizeInPoints, x.Font.Style));
        float factor = (effectivePreferences.UiScalePercent / 100f) *
            (effectivePreferences.FontScalePercent / 100f);
        form.SuspendLayout();
        try
        {
            foreach (var c in all)
            {
                var baseline = Fonts.GetValue(c, x => new FontBaseline(x.Font.FontFamily.Name, x.Font.SizeInPoints, x.Font.Style));
                float size = Math.Clamp(baseline.Size * factor, 8f, 36f);
                string key = $"{baseline.Family}|{baseline.Style}|{size:F2}";
                if (!FontCache.TryGetValue(key, out Font? font))
                {
                    font = new Font(baseline.Family, size, baseline.Style, GraphicsUnit.Point);
                    FontCache[key] = font;
                }
                if (!ReferenceEquals(c.Font, font)) c.Font = font;
                if (c is Button b) Decorate(b, effectivePreferences);
                else if (c is DataGridView grid)
                {
                    grid.EnableHeadersVisualStyles = false;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = SoftBlue;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = Navy;
                    grid.AlternatingRowsDefaultCellStyle.BackColor = SystemInformation.HighContrast
                        ? SystemColors.Window : Color.FromArgb(249,251,255);
                    grid.DefaultCellStyle.SelectionBackColor = SystemInformation.HighContrast
                        ? SystemColors.Highlight : Color.FromArgb(210,229,254);
                    grid.DefaultCellStyle.SelectionForeColor = SystemInformation.HighContrast
                        ? SystemColors.HighlightText : Foreground;
                    int text = TextRenderer.MeasureText("Ag", grid.Font).Height;
                    grid.ColumnHeadersHeight = Math.Max(grid.ColumnHeadersHeight, text + 18);
                    grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height, text + 19);
                }
                else if (c is FlowLayoutPanel flow) flow.AutoScroll = true;
            }
            form.BackColor = Canvas;
        }
        finally { form.ResumeLayout(true); }
    }
    private static void Decorate(Button b, UiPreferences preferences)
    {
        bool primary = Equals(b.Tag, "PrimaryAction") || b.ForeColor == Color.White ||
            b.BackColor == Color.FromArgb(24,102,190) || b.BackColor == Accent;
        if (b.Image is null && !Equals(b.Tag, "NoIcon") &&
            !string.IsNullOrWhiteSpace(b.Text) &&
            b.Text is not "↑" and not "↓")
        {
            b.Image = UiGlyphs.ForButton(b.Text);
            b.ImageAlign = ContentAlignment.MiddleLeft;
            b.TextImageRelation = TextImageRelation.ImageBeforeText;
        }
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = primary ? Accent : Surface;
        b.ForeColor = primary ? Color.White : Foreground;
        b.FlatAppearance.BorderColor = primary ? Accent : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(12,70,180) : SoftBlue;
        int needed = TextRenderer.MeasureText(b.Text, b.Font).Width +
            (b.Image is null ? 24 : 52);
        if (b.Dock is not (DockStyle.Top or DockStyle.Bottom or DockStyle.Fill))
            b.MinimumSize = new Size(Math.Max(needed, b.MinimumSize.Width), 0);
        b.Height = Math.Max(b.Height, TextRenderer.MeasureText("Ag", b.Font).Height +
            (preferences.Density == "Compact" ? 11 : 17));
        if (Rounded.TryGetValue(b, out _)) { SetCorners(b); return; }
        Rounded.Add(b, new object());
        b.SizeChanged += (_, _) => SetCorners(b);
        b.DpiChangedAfterParent += (_, _) => SetCorners(b);
        SetCorners(b);
    }
    private static void SetCorners(Button b)
    {
        if (b.IsDisposed || b.Width < 4 || b.Height < 4) return;
        int radius = Math.Clamp((int)Math.Round(9.0 * b.DeviceDpi / 96), 4, b.Height / 2);
        int d = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(b.Width - d - 1, 0, d, d, 270, 90);
        path.AddArc(b.Width - d - 1, b.Height - d - 1, d, d, 0, 90);
        path.AddArc(0, b.Height - d - 1, d, d, 90, 90);
        path.CloseFigure();
        var previous = b.Region;
        b.Region = new Region(path);
        previous?.Dispose();
    }
}
