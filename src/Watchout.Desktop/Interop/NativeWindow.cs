using System.Runtime.InteropServices;

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
    /// </summary>
    public static (int W, int H) MonitorPixels(int x, int y)
    {
        var mon = MonitorFromPoint(new POINT { X = x, Y = y }, MonitorDefaultToNearest);
        if (mon == IntPtr.Zero) return (0, 0);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref info)) return (0, 0);
        var r = info.rcMonitor;
        return (Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
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

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(POINT pt, int flags);

    [DllImport("user32.dll")]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

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

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}
