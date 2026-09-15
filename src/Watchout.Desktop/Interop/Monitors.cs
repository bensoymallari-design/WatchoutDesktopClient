using System.Runtime.InteropServices;
using Watchout.Core.Models;

namespace Watchout.Desktop.Interop;

public static class Monitors
{
    public static List<OutputScreen> List()
    {
        var edid = DisplayConfig.List();
        var screens = new List<OutputScreen>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(hMonitor, ref info)) return true;
            var r = info.rcMonitor;
            var width = Math.Max(1, r.Right - r.Left);
            var height = Math.Max(1, r.Bottom - r.Top);
            var primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
            var device = info.szDevice ?? "";
            var key = DisplayConfig.DeviceKey(device);
            var match = edid.FirstOrDefault(e => string.Equals(e.DeviceKey, key, StringComparison.OrdinalIgnoreCase));
            var current = CurrentMode(device);
            var currentW = current.W > 0 ? current.W : width;
            var currentH = current.H > 0 ? current.H : height;
            var physicalW = match.PreferredWidth > 0 ? match.PreferredWidth : currentW;
            var physicalH = match.PreferredHeight > 0 ? match.PreferredHeight : currentH;
            var label = !string.IsNullOrWhiteSpace(match.FriendlyName)
                ? match.FriendlyName
                : string.IsNullOrWhiteSpace(key)
                    ? (primary ? "Primary" : $"Monitor {screens.Count + 1}")
                    : key;
            screens.Add(new OutputScreen
            {
                Id = string.IsNullOrWhiteSpace(key) ? $"{r.Left},{r.Top},{width}x{height}" : key,
                Label = label,
                Left = r.Left,
                Top = r.Top,
                Width = width,
                Height = height,
                PhysicalWidth = physicalW,
                PhysicalHeight = physicalH,
                IsPrimary = primary,
                ScaleFactor = DpiScale(hMonitor),
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

    static (int W, int H) CurrentMode(string device)
    {
        if (string.IsNullOrWhiteSpace(device)) return (0, 0);
        var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(device, EnumCurrentSettings, ref mode)) return (0, 0);
        return (mode.dmPelsWidth, mode.dmPelsHeight);
    }

    static double DpiScale(IntPtr hMonitor)
    {
        try
        {
            if (GetDpiForMonitor(hMonitor, 0, out var dx, out _) == 0 && dx > 0)
                return dx / 96.0;
        }
        catch
        {
            // older Windows without shcore
        }
        return 1;
    }

    const int MONITORINFOF_PRIMARY = 1;
    const int CCHDEVICENAME = 32;
    const int EnumCurrentSettings = -1;

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}
