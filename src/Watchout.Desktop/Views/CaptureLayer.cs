using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

/// <summary>
/// Live NDI/capture as a Win32 child so it can stack in front of DXVA MediaElement.
/// WPF Image/WriteableBitmap sits under the video overlay once H.264 starts playing.
/// </summary>
public sealed class CaptureLayer : HwndHost
{
    const int WmPaint = 0x000F;
    const int WmEraseBkgnd = 0x0014;
    const int WsChild = 0x40000000;
    const int WsVisible = 0x10000000;
    const int WsClipSiblings = 0x04000000;
    const int WsExNoActivate = 0x08000000;
    const int WsExTransparent = 0x00000020;

    string? _deviceId;
    string? _ndiName;
    bool _listening;
    IntPtr _hwnd;
    WriteableBitmap? _bmp;
    readonly Action _onFrame;

    public CaptureLayer()
    {
        _onFrame = OnFrame;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public string? DeviceId
    {
        get => _deviceId;
        set
        {
            if (_deviceId == value) return;
            Detach();
            _deviceId = value;
            _bmp = null;
            Redraw();
            if (IsLoaded) Attach();
        }
    }

    public string? NdiName
    {
        get => _ndiName;
        set
        {
            if (_ndiName == value) return;
            Detach();
            _ndiName = value;
            _bmp = null;
            Redraw();
            if (IsLoaded) Attach();
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowEx(
            WsExNoActivate | WsExTransparent,
            "Static",
            "",
            WsChild | WsVisible | WsClipSiblings,
            0, 0,
            Math.Max(1, (int)Math.Ceiling(Math.Max(Width, 1))),
            Math.Max(1, (int)Math.Ceiling(Math.Max(Height, 1))),
            hwndParent.Handle,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmPaint)
        {
            Paint(hwnd);
            handled = true;
            return IntPtr.Zero;
        }
        if (msg == WmEraseBkgnd)
        {
            handled = true;
            return new IntPtr(1);
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    void Attach()
    {
        if (_listening) return;
        if (!string.IsNullOrEmpty(_deviceId))
        {
            _bmp = CaptureHub.Retain(_deviceId, _onFrame);
            _listening = true;
            Redraw();
            return;
        }
        if (!string.IsNullOrEmpty(_ndiName))
        {
            _bmp = NdiHub.Retain(_ndiName, _onFrame);
            _listening = true;
            Redraw();
        }
    }

    void Detach()
    {
        if (!_listening) return;
        if (!string.IsNullOrEmpty(_deviceId)) CaptureHub.Release(_deviceId, _onFrame);
        if (!string.IsNullOrEmpty(_ndiName)) NdiHub.Release(_ndiName, _onFrame);
        _listening = false;
        _bmp = null;
        Redraw();
    }

    void OnFrame()
    {
        if (!string.IsNullOrEmpty(_deviceId))
        {
            _bmp = CaptureHub.Peek(_deviceId);
            Redraw();
            return;
        }
        if (!string.IsNullOrEmpty(_ndiName))
        {
            _bmp = NdiHub.Peek(_ndiName);
            Redraw();
        }
    }

    void Redraw()
    {
        if (_hwnd != IntPtr.Zero)
            InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    void Paint(IntPtr hwnd)
    {
        var ps = new PaintStruct();
        var hdc = BeginPaint(hwnd, out ps);
        try
        {
            GetClientRect(hwnd, out var rc);
            var dw = Math.Max(1, rc.Right - rc.Left);
            var dh = Math.Max(1, rc.Bottom - rc.Top);
            var brush = CreateSolidBrush(0x00181008);
            FillRect(hdc, ref rc, brush);
            DeleteObject(brush);
            var bmp = _bmp;
            if (bmp is null) return;
            bmp.Lock();
            try
            {
                var info = new BitmapInfo
                {
                    Size = Marshal.SizeOf<BitmapInfo>(),
                    Width = bmp.PixelWidth,
                    Height = -bmp.PixelHeight,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                };
                StretchDIBits(
                    hdc, 0, 0, dw, dh,
                    0, 0, bmp.PixelWidth, bmp.PixelHeight,
                    bmp.BackBuffer, ref info, 0, 0x00CC0020);
            }
            finally
            {
                bmp.Unlock();
            }
        }
        finally
        {
            EndPaint(hwnd, ref ps);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

    [DllImport("user32.dll")]
    static extern IntPtr BeginPaint(IntPtr hwnd, out PaintStruct lpPaint);

    [DllImport("user32.dll")]
    static extern bool EndPaint(IntPtr hwnd, ref PaintStruct lpPaint);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hwnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    static extern int FillRect(IntPtr hdc, ref NativeRect lprc, IntPtr hbr);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateSolidBrush(int color);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    static extern int StretchDIBits(IntPtr hdc, int xDest, int yDest, int destWidth, int destHeight,
        int xSrc, int ySrc, int srcWidth, int srcHeight, IntPtr bits, ref BitmapInfo info, int usage, int rop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PaintStruct
    {
        public IntPtr Hdc;
        public int Erase;
        public NativeRect RcPaint;
        public int Restore;
        public int IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }
}
