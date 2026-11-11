using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Windows-only, read-only display scaling evidence for the local operator lab.
// No window/screenshot/identity capture and no changes to DPI or display settings.
public static class LscmDisplayMonitorInterop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public sealed class ScaleReading
    {
        public int Index { get; set; }
        public int ScalePercent { get; set; }
    }

    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc,
        ref Rect area, IntPtr userData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr region,
        MonitorCallback callback, IntPtr userData);

    // This API returns the selected monitor's actual Windows scale factor
    // without depending on PowerShell.exe's process DPI awareness mode.
    [DllImport("shcore.dll")]
    private static extern int GetScaleFactorForMonitor(IntPtr monitor,
        out int scalePercent);

    public static ScaleReading[] Read()
    {
        var readings = new List<ScaleReading>();
        bool failed = false;
        MonitorCallback callback = delegate(IntPtr monitor, IntPtr dc,
            ref Rect area, IntPtr userData)
        {
            int scalePercent;
            if (GetScaleFactorForMonitor(monitor, out scalePercent) != 0 ||
                scalePercent < 100 || scalePercent > 500)
            {
                failed = true;
                return false;
            }

            readings.Add(new ScaleReading {
                Index = readings.Count + 1,
                ScalePercent = scalePercent
            });
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback,
                IntPtr.Zero) || failed || readings.Count == 0)
        {
            throw new InvalidOperationException("DISPLAY_SCALE_NOT_VERIFIED");
        }
        GC.KeepAlive(callback);
        return readings.ToArray();
    }
}
