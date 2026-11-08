using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace LockScreenCampaignManager;

/// <summary>
/// Dependency-free visual assets for forms and actions. Icons use vector
/// primitives at device DPI and the same navy/green palette across the GUI.
/// </summary>
internal static class UiGlyphs
{
    private static readonly Lazy<Icon> ProgramIcon = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("LockScreenCampaignManager.AppIcon")
            ?? throw new InvalidOperationException("Embedded application icon is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    });

    internal static Icon AppIcon => ProgramIcon.Value;
    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.Ordinal);

    private static string Kind(string label)
    {
        string value = label.ToLowerInvariant();
        if (value.Contains("ad") || value.Contains("domain") ||
            value.Contains("connect") || value.Contains("dc")) return "network";
        if (value.Contains("folder")) return "folder";
        if (value.Contains("file") || value.Contains("image") ||
            value.Contains("preview")) return "image";
        if (value.Contains("save") || value.Contains("export")) return "save";
        if (value.Contains("company") || value.Contains("companies") ||
            value.Contains("group")) return "company";
        if (value.Contains("schedule") || value.Contains("date") ||
            value.Contains("time")) return "calendar";
        if (value.Contains("deploy") || value.Contains("gpo") ||
            value.Contains("apply")) return "shield";
        if (value.Contains("diagnos") || value.Contains("debug") ||
            value.Contains("log")) return "pulse";
        if (value.Contains("update") || value.Contains("refresh") ||
            value.Contains("read")) return "refresh";
        if (value.Contains("delete") || value.Contains("remove") ||
            value.Contains("cancel")) return "close";
        if (value.Contains("new") || value.Contains("add")) return "plus";
        if (value.Contains("settings") || value.Contains("configure")) return "settings";
        if (value.Contains("next") || value.Contains("continue") ||
            value.Contains("finish")) return "check";
        if (value.Contains("back") || value.Contains("return")) return "back";
        if (value.Contains("help") || value.Contains("about")) return "help";
        return "grid";
    }

    internal static Bitmap ForButton(string label)
    {
        string kind = Kind(label);
        if (Cache.TryGetValue(kind, out var bitmap)) return bitmap;
        bitmap = new Bitmap(24, 24, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        Color ink = kind is "plus" or "check" ? Color.FromArgb(10, 134, 76)
            : Color.FromArgb(13, 91, 171);
        using var p = new Pen(ink, 2.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round,
            LineJoin = LineJoin.Round };
        switch (kind)
        {
            case "folder":
                g.DrawLines(p, [new Point(3, 9), new Point(3, 20),
                    new Point(21, 20), new Point(21, 8), new Point(12, 8),
                    new Point(10, 5), new Point(3, 5)]);
                break;
            case "image":
                g.DrawRoundedRectangle(p, new Rectangle(2, 3, 20, 18), new Size(3, 3));
                g.DrawEllipse(p, 15, 6, 3, 3);
                g.DrawLines(p, [new Point(4, 17), new Point(10, 10),
                    new Point(14, 14), new Point(17, 12), new Point(21, 17)]);
                break;
            case "network":
                g.DrawLine(p, 12, 7, 12, 13);
                g.DrawLine(p, 5, 14, 19, 14);
                g.DrawLine(p, 5, 14, 5, 17);
                g.DrawLine(p, 19, 14, 19, 17);
                g.DrawRoundedRectangle(p, new Rectangle(8, 2, 8, 6), new Size(2, 2));
                g.DrawRoundedRectangle(p, new Rectangle(1, 17, 8, 6), new Size(2, 2));
                g.DrawRoundedRectangle(p, new Rectangle(15, 17, 8, 6), new Size(2, 2));
                break;
            case "company":
                g.DrawRectangle(p, 5, 4, 14, 18);
                g.DrawLine(p, 8, 9, 16, 9);
                g.DrawLine(p, 8, 13, 16, 13);
                g.DrawLine(p, 8, 17, 16, 17);
                break;
            case "calendar":
                g.DrawRoundedRectangle(p, new Rectangle(3, 5, 18, 16), new Size(2, 2));
                g.DrawLine(p, 4, 11, 20, 11);
                g.DrawLine(p, 8, 3, 8, 7);
                g.DrawLine(p, 16, 3, 16, 7);
                break;
            case "shield":
                g.DrawLines(p, [new Point(12, 2), new Point(21, 6),
                    new Point(20, 15), new Point(12, 22), new Point(4, 15),
                    new Point(3, 6), new Point(12, 2)]);
                g.DrawLines(p, [new Point(8, 12), new Point(11, 15), new Point(16, 9)]);
                break;
            case "pulse":
                g.DrawLines(p, [new Point(2, 13), new Point(7, 13),
                    new Point(10, 6), new Point(13, 19),
                    new Point(16, 11), new Point(19, 13), new Point(22, 13)]);
                break;
            case "refresh":
                g.DrawArc(p, 4, 4, 16, 16, 40, 255);
                g.DrawLines(p, [new Point(18, 4), new Point(20, 9), new Point(15, 9)]);
                break;
            case "save":
                g.DrawRoundedRectangle(p, new Rectangle(4, 3, 16, 19), new Size(2, 2));
                g.DrawRectangle(p, 8, 4, 8, 7);
                g.DrawRectangle(p, 8, 15, 9, 6);
                break;
            case "plus":
                g.DrawEllipse(p, 2, 2, 20, 20);
                g.DrawLine(p, 12, 7, 12, 17);
                g.DrawLine(p, 7, 12, 17, 12);
                break;
            case "close":
                g.DrawLine(p, 6, 6, 18, 18);
                g.DrawLine(p, 18, 6, 6, 18);
                break;
            case "back":
                g.DrawLines(p, [new Point(13, 5), new Point(6, 12), new Point(13, 19)]);
                g.DrawLine(p, 6, 12, 21, 12);
                break;
            case "check":
                g.DrawEllipse(p, 2, 2, 20, 20);
                g.DrawLines(p, [new Point(6, 12), new Point(10, 16), new Point(18, 8)]);
                break;
            default:
                g.DrawRoundedRectangle(p, new Rectangle(3, 3, 18, 18), new Size(4, 4));
                g.DrawLine(p, 8, 8, 16, 8);
                g.DrawLine(p, 8, 12, 16, 12);
                g.DrawLine(p, 8, 16, 16, 16);
                break;
        }
        Cache.Add(kind, bitmap);
        return bitmap;
    }
}
