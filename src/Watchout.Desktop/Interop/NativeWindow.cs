using System.Runtime.InteropServices;
using Watchout.Core.Stage;

namespace Watchout.Desktop.Interop;

public static class NativeWindow
{
    static readonly IntPtr HwndTopmost = new(-1);
    static readonly IntPtr HwndNoTopmost = new(-2);
    const uint SwpShowWindow = 0x0040;
    const uint SwpNoActivate = 0x0010;
    const uint SwpNoMove = 0x0002;
    const uint SwpNoZOrder = 0x0004;
    const int GwlStyle = -16;
    const int WsChild = 0x40000000;

    public static void Place(IntPtr hwnd, int left, int top, int width, int height, bool topmost = true)
    {
        if (hwnd == IntPtr.Zero || width <= 0 || height <= 0) return;
        SetWindowPos(hwnd, topmost ? HwndTopmost : HwndNoTopmost, left, top, width, height, SwpShowWindow | SwpNoActivate);
    }

    public static void Resize(IntPtr hwnd, int width, int height)
    {
        if (hwnd == IntPtr.Zero || width <= 0 || height <= 0) return;
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, SwpNoMove | SwpNoZOrder | SwpNoActivate);
    }

    public static (int W, int H) ClientSize(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return (0, 0);
        if (!GetClientRect(hwnd, out var r)) return (0, 0);
        return (Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
    }

    /// <summary>
    /// OS pixels of the monitor that contains this point. A 3840 wall HWND on a
    /// 2560 mode must not stay 3840 — DXGI then crops (Fit cue looks zoomed).
    /// Colorlight X20 NVIDIA custom (6720×1344) can be larger than the EDID
    /// rectangle — use that live mode so the wall HWND is not clamped to 1920.
    /// </summary>
    public static (int W, int H) MonitorPixels(int x, int y)
    {
        var mon = MonitorFromPoint(new POINT { X = x, Y = y }, MonitorDefaultToNearest);
        if (mon == IntPtr.Zero) return (0, 0);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfoEx(mon, ref info)) return (0, 0);
        var r = info.rcMonitor;
        var rectW = Math.Max(0, r.Right - r.Left);
        var rectH = Math.Max(0, r.Bottom - r.Top);
        var current = CurrentMode(info.szDevice);
        return ScreenAssign.WindowsModePixels(rectW, rectH, current.W, current.H);
    }

    static (int W, int H) CurrentMode(string device)
    {
        if (string.IsNullOrWhiteSpace(device)) return (0, 0);
        var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(device, EnumCurrentSettings, ref mode)) return (0, 0);
        return (mode.dmPelsWidth, mode.dmPelsHeight);
    }

    public static bool IsChild(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        return (GetWindowLong(hwnd, GwlStyle) & WsChild) != 0;
    }

    public static void KeepTopmost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow | SwpNoActivate);
    }

    const uint SwpNoSize = 0x0001;
    const int MonitorDefaultToNearest = 2;
    const int CCHDEVICENAME = 32;
    const int EnumCurrentSettings = -1;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(POINT pt, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "GetMonitorInfo")]
    static extern bool GetMonitorInfoEx(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X, Y;
    }

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
