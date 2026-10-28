using System.Runtime.InteropServices;
using Watchout.Core.Models;
using Watchout.Core.Stage;

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
            var match = MatchEdid(edid, key, r.Left, r.Top);
            var current = CurrentMode(device);
            var windows = ScreenAssign.PickWindowsMode(
                current.W, current.H, width, height, match.CurrentWidth, match.CurrentHeight);
            var physicalW = match.PreferredWidth > 0 ? match.PreferredWidth : windows.W;
            var physicalH = match.PreferredHeight > 0 ? match.PreferredHeight : windows.H;
            var connector = match.Connector ?? "";
            var confirmed = (current.W >= ScreenAssign.MinModePx && current.H >= ScreenAssign.MinModePx)
                || (match.CurrentWidth >= ScreenAssign.MinModePx && match.CurrentHeight >= ScreenAssign.MinModePx);
            screens.Add(new OutputScreen
            {
                Id = string.IsNullOrWhiteSpace(key) ? $"{r.Left},{r.Top},{windows.W}x{windows.H}" : key,
                Label = BuildLabel(match.FriendlyName, connector, key, primary, screens.Count + 1),
                Left = r.Left,
                Top = r.Top,
                Width = windows.W,
                Height = windows.H,
                PhysicalWidth = physicalW,
                PhysicalHeight = physicalH,
                IsPrimary = primary,
                ScaleFactor = DpiScale(hMonitor),
                Connector = connector,
                IsInternal = match.IsInternal,
                ModeConfirmed = confirmed,
            });
            return true;
        }, IntPtr.Zero);

        screens.Sort(static (a, b) =>
        {
            var c = a.Left.CompareTo(b.Left);
            return c != 0 ? c : a.Top.CompareTo(b.Top);
        });

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

    /// <summary>
    /// GDI name first. USB-C docks and hybrid GPUs sometimes leave the source
    /// name blank; the desktop position still identifies the port.
    /// </summary>
    static DisplayConfig.EdidInfo MatchEdid(List<DisplayConfig.EdidInfo> edid, string key, int left, int top)
    {
        if (!string.IsNullOrEmpty(key))
        {
            var byName = edid.FirstOrDefault(e => string.Equals(e.DeviceKey, key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(byName.DeviceKey))
                return byName;
        }
        var byPos = edid.Where(e => e.Left == left && e.Top == top).ToList();
        if (byPos.Count == 0) return default;
        foreach (var entry in byPos)
        {
            if (!string.IsNullOrEmpty(entry.FriendlyName) || !string.IsNullOrEmpty(entry.Connector))
                return entry;
        }
        return byPos[0];
    }

    static string BuildLabel(string? friendly, string connector, string key, bool primary, int index)
    {
        var name = !string.IsNullOrWhiteSpace(friendly)
            ? friendly.Trim()
            : string.IsNullOrWhiteSpace(key)
                ? (primary ? "Primary" : $"Monitor {index}")
                : key;
        if (string.IsNullOrEmpty(connector) || connector == "Internal") return name;
        if (name.Contains(connector, StringComparison.OrdinalIgnoreCase)) return name;
        return $"{name} · {connector}";
    }

    public static (int W, int H) WindowsPixels(IntPtr hMonitor)
    {
        if (hMonitor == IntPtr.Zero) return (0, 0);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(hMonitor, ref info)) return (0, 0);
        var r = info.rcMonitor;
        var width = Math.Max(0, r.Right - r.Left);
        var height = Math.Max(0, r.Bottom - r.Top);
        var device = info.szDevice ?? "";
        var current = CurrentMode(device);
        var key = DisplayConfig.DeviceKey(device);
        var match = MatchEdid(DisplayConfig.List(), key, r.Left, r.Top);
        return ScreenAssign.PickWindowsMode(current.W, current.H, width, height, match.CurrentWidth, match.CurrentHeight);
    }

    static (int W, int H) CurrentMode(string device)
    {
        if (string.IsNullOrWhiteSpace(device)) return (0, 0);
        var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        if (EnumDisplaySettings(device, EnumCurrentSettings, ref mode)
            && mode.dmPelsWidth > 0 && mode.dmPelsHeight > 0)
            return (mode.dmPelsWidth, mode.dmPelsHeight);
        if (EnumDisplaySettingsEx(device, EnumCurrentSettings, ref mode, EdsRawMode)
            && mode.dmPelsWidth > 0 && mode.dmPelsHeight > 0)
            return (mode.dmPelsWidth, mode.dmPelsHeight);
        return (0, 0);
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
    const int EdsRawMode = 2;

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, int dwFlags);

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
