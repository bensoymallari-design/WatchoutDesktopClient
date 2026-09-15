using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Watchout.Desktop.Interop;

/// <summary>
/// Top-level layered popup that can sit above a DXVA MediaElement overlay.
/// Child HWNDs inside the Output window stay under that plane on a fullscreen wall.
/// </summary>
public sealed class LiveOverlayWindow : IDisposable
{
    const int WsPopup = unchecked((int)0x80000000);
    const int WsExLayered = 0x00080000;
    const int WsExNoActivate = 0x08000000;
    const int WsExToolwindow = 0x00000080;
    const int WsExTopmost = 0x00000008;
    const int WsExTransparent = 0x00000020;
    const int SwHide = 0;
    const int SwShowNoActivate = 4;
    const int UlwAlpha = 2;
    const uint SwpNoActivate = 0x0010;
    const uint SwpShowWindow = 0x0040;
    static readonly IntPtr HwndTopmost = new(-1);

    IntPtr _hwnd;
    IntPtr _hdc;
    IntPtr _dib;
    IntPtr _old;
    IntPtr _bits;
    int _w;
    int _h;
    bool _visible;

    public void Present(WriteableBitmap? bmp, int x, int y, int width, int height, byte alpha, IntPtr owner)
    {
        if (bmp is null || width < 2 || height < 2 || alpha < 3)
        {
            Hide();
            return;
        }

        EnsureWindow(owner);
        if (_hwnd == IntPtr.Zero) return;
        EnsureBuffer(width, height);
        if (_hdc == IntPtr.Zero || _dib == IntPtr.Zero) return;

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
                _hdc, 0, 0, width, height,
                0, 0, bmp.PixelWidth, bmp.PixelHeight,
                bmp.BackBuffer, ref info, 0, 0x00CC0020);
        }
        finally
        {
            bmp.Unlock();
        }

        var dst = new NativePoint { X = x, Y = y };
        var size = new NativeSize { Cx = width, Cy = height };
        var src = new NativePoint { X = 0, Y = 0 };
        var blend = new BlendFunction
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = alpha,
            AlphaFormat = 0,
        };
        UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref dst, ref size, _hdc, ref src, 0, ref blend, UlwAlpha);
        SetWindowPos(_hwnd, HwndTopmost, x, y, width, height, SwpNoActivate | SwpShowWindow);
        if (!_visible)
        {
            ShowWindow(_hwnd, SwShowNoActivate);
            _visible = true;
        }
    }

    public void Hide()
    {
        if (_hwnd == IntPtr.Zero || !_visible) return;
        ShowWindow(_hwnd, SwHide);
        _visible = false;
    }

    void EnsureWindow(IntPtr owner)
    {
        if (_hwnd != IntPtr.Zero) return;
        _hwnd = CreateWindowEx(
            WsExLayered | WsExNoActivate | WsExToolwindow | WsExTopmost | WsExTransparent,
            "Static",
            "",
            WsPopup,
            0, 0, 1, 1,
            owner,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);
    }

    void EnsureBuffer(int width, int height)
    {
        if (_dib != IntPtr.Zero && _w == width && _h == height) return;
        DiscardBuffer();
        _w = width;
        _h = height;
        var info = new BitmapInfo
        {
            Size = Marshal.SizeOf<BitmapInfo>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
            Compression = 0,
        };
        _hdc = CreateCompatibleDC(IntPtr.Zero);
        _dib = CreateDIBSection(IntPtr.Zero, ref info, 0, out _bits, IntPtr.Zero, 0);
        if (_hdc != IntPtr.Zero && _dib != IntPtr.Zero)
            _old = SelectObject(_hdc, _dib);
    }

    void DiscardBuffer()
    {
        if (_hdc != IntPtr.Zero && _old != IntPtr.Zero)
            SelectObject(_hdc, _old);
        _old = IntPtr.Zero;
        if (_dib != IntPtr.Zero) DeleteObject(_dib);
        _dib = IntPtr.Zero;
        _bits = IntPtr.Zero;
        if (_hdc != IntPtr.Zero) DeleteDC(_hdc);
        _hdc = IntPtr.Zero;
        _w = 0;
        _h = 0;
    }

    public void Dispose()
    {
        Hide();
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        DiscardBuffer();
        GC.SuppressFinalize(this);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize,
        IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr ho);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo info, int usage, out IntPtr bits, IntPtr section, int offset);

    [DllImport("gdi32.dll")]
    static extern int StretchDIBits(IntPtr hdc, int xDest, int yDest, int destWidth, int destHeight,
        int xSrc, int ySrc, int srcWidth, int srcHeight, IntPtr bits, ref BitmapInfo info, int usage, int rop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeSize
    {
        public int Cx, Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BlendFunction
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
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
