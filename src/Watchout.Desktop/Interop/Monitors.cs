using System.Runtime.InteropServices;
using Watchout.Core.Models;

namespace Watchout.Desktop.Interop;

public static class Monitors
{
    public static List<OutputScreen> List()
    {
        var screens = new List<OutputScreen>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(hMonitor, ref info)) return true;
            var r = info.rcMonitor;
            var width = r.Right - r.Left;
            var height = r.Bottom - r.Top;
            var primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
            screens.Add(new OutputScreen
            {
                Id = $"{r.Left},{r.Top},{width}x{height}",
                Label = string.IsNullOrWhiteSpace(info.szDevice)
                    ? (primary ? "Primary" : $"Monitor {screens.Count + 1}")
                    : info.szDevice.Replace(@"\\.\", ""),
                Left = r.Left,
                Top = r.Top,
                Width = width,
                Height = height,
                PhysicalWidth = width,
                PhysicalHeight = height,
                IsPrimary = primary,
                ScaleFactor = 1,
            });
            return true;
        }, IntPtr.Zero);

        if (screens.Count == 0)
        {
            screens.Add(new OutputScreen
            {
                Id = "primary",
                Label = "Primary",
                Width = 1920,
                Height = 1080,
                PhysicalWidth = 1920,
                PhysicalHeight = 1080,
                IsPrimary = true,
            });
        }
        return screens;
    }

    const int MONITORINFOF_PRIMARY = 1;
    const int CCHDEVICENAME = 32;

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string szDevice;
    }
}
