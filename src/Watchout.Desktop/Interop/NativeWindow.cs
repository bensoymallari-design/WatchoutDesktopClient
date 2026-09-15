using System.Runtime.InteropServices;

namespace Watchout.Desktop.Interop;

public static class NativeWindow
{
    static readonly IntPtr HwndTopmost = new(-1);
    const uint SwpShowWindow = 0x0040;

    public static void Place(IntPtr hwnd, int left, int top, int width, int height)
    {
        if (hwnd == IntPtr.Zero || width <= 0 || height <= 0) return;
        SetWindowPos(hwnd, HwndTopmost, left, top, width, height, SwpShowWindow);
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
