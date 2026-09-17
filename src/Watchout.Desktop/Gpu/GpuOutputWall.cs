using System.Runtime.InteropServices;
using Watchout.Desktop.Interop;

namespace Watchout.Desktop.Gpu;

/// <summary>
/// Top-level DXGI wall. WPF OutputWindow is a black overlay that used to cover
/// the nested HwndHost swap chain; Play decoded frames that never reached the TV.
/// This popup is not a WS_CHILD, so FlipDiscard is valid, and it stays above WPF.
/// </summary>
sealed class GpuOutputWall : IDisposable
{
    const int WsPopup = unchecked((int)0x80000000);
    const int WsVisible = 0x10000000;
    const int WsClipSiblings = 0x04000000;
    const int WsExTopmost = 0x00000008;
    const int WsExToolWindow = 0x00000080;
    const int WsExNoActivate = 0x08000000;
    const int WsExNoRedirectionBitmap = 0x00200000;

    public IntPtr Hwnd { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    GpuOutputWall(IntPtr hwnd, int w, int h)
    {
        Hwnd = hwnd;
        Width = w;
        Height = h;
    }

    public static GpuOutputWall Create(IntPtr owner, int left, int top, int width, int height)
    {
        width = Math.Max(64, width);
        height = Math.Max(64, height);
        var mon = NativeWindow.MonitorPixels(left, top);
        if (mon.W >= 64 && mon.H >= 64)
        {
            width = Math.Min(width, mon.W);
            height = Math.Min(height, mon.H);
        }
        var ex = WsExTopmost | WsExToolWindow | WsExNoActivate | WsExNoRedirectionBitmap;
        var style = WsPopup | WsVisible | WsClipSiblings;
        var hwnd = CreateWindowEx(ex, "Static", "WatchMe Wall", style, left, top, width, height, owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            ex &= ~WsExNoRedirectionBitmap;
            hwnd = CreateWindowEx(ex, "Static", "WatchMe Wall", style, left, top, width, height, owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the Output wall HWND");
        NativeWindow.Place(hwnd, left, top, width, height);
        var created = new GpuOutputWall(hwnd, width, height);
        created.Place(left, top, width, height);
        return created;
    }

    public void Place(int left, int top, int width, int height)
    {
        width = Math.Max(64, width);
        height = Math.Max(64, height);
        var mon = NativeWindow.MonitorPixels(left, top);
        if (mon.W >= 64 && mon.H >= 64)
        {
            width = Math.Min(width, mon.W);
            height = Math.Min(height, mon.H);
        }
        NativeWindow.Place(Hwnd, left, top, width, height);
        var client = NativeWindow.ClientSize(Hwnd);
        Width = client.W >= 64 ? client.W : width;
        Height = client.H >= 64 ? client.H : height;
    }

    public void Dispose()
    {
        var hwnd = Hwnd;
        Hwnd = IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;
        GpuEngine.DropOutput(hwnd);
        DestroyWindow(hwnd);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")]
    static extern bool DestroyWindow(IntPtr hwnd);
}
