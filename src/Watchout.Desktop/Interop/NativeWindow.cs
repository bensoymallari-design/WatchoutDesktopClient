using System.Runtime.InteropServices;

namespace Watchout.Desktop.Interop;

public static class NativeWindow
{
    static readonly IntPtr HwndTopmost = new(-1);
    const uint SwpShowWindow = 0x0040;
    const uint SwpNoActivate = 0x0010;
    const uint SwpNoMove = 0x0002;
    const uint SwpNoZOrder = 0x0004;
    const int GwlStyle = -16;
    const int WsChild = 0x40000000;

    public static void Place(IntPtr hwnd, int left, int top, int width, int height)
    {
        if (hwnd == IntPtr.Zero || width <= 0 || height <= 0) return;
        SetWindowPos(hwnd, HwndTopmost, left, top, width, height, SwpShowWindow | SwpNoActivate);
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

    public static bool IsChild(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        return (GetWindowLong(hwnd, GwlStyle) & WsChild) != 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
}
