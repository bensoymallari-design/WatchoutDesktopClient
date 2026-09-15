using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Watchout.Core.Stage;

namespace Watchout.Desktop.Interop;

/// <summary>
/// Layered blit for live NDI/capture. Output uses a top-level popup so the wall
/// sits above DXVA; Stage uses the same blit on the HwndHost child so WM_PAINT
/// erase cannot flash the PIP black.
/// </summary>
public sealed class LiveOverlayWindow : IDisposable
{
    internal const string ClassName = "WatchMeLiveBlit";

    const int WsPopup = unchecked((int)0x80000000);
    const int WsExLayered = 0x00080000;
    const int WsExNoActivate = 0x08000000;
    const int WsExToolwindow = 0x00000080;
    const int WsExTopmost = 0x00000008;
    const int WsExTransparent = 0x00000020;
    const int SwHide = 0;
    const int SwShowNoActivate = 4;
    const int UlwAlpha = 2;
    const uint SwpNosize = 0x0001;
    const uint SwpNozorder = 0x0004;
    const uint SwpNoActivate = 0x0010;
    const int WmPaint = 0x000F;
    const int WmEraseBkgnd = 0x0014;
    const int NullBrush = 5;
    const int ClassAlreadyExists = 1410;

    delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    static readonly WndProc Hook = OnClassProc;
    static bool _classReady;

    IntPtr _popup;
    IntPtr _hdc;
    IntPtr _dib;
    IntPtr _old;
    IntPtr _bits;
    int _w;
    int _h;
    int _x = int.MinValue;
    int _y = int.MinValue;
    bool _visible;

    public static void EnsureClass()
    {
        if (_classReady) return;
        var wc = new WndClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Hook),
            hInstance = GetModuleHandle(null),
            hbrBackground = GetStockObject(NullBrush),
            lpszClassName = ClassName,
        };
        var atom = RegisterClass(ref wc);
        _classReady = atom != 0 || Marshal.GetLastWin32Error() == ClassAlreadyExists;
    }

    static IntPtr OnClassProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmEraseBkgnd) return new IntPtr(1);
        if (msg == WmPaint)
        {
            ValidateRect(hwnd, IntPtr.Zero);
            return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Present(WriteableBitmap? bmp, int x, int y, int width, int height, byte alpha, bool bitsDirty)
    {
        EnsureWindow();
        if (_popup == IntPtr.Zero) return;
        width = LiveComposite.Stick(width, _w);
        height = LiveComposite.Stick(height, _h);
        x = LiveComposite.Stick(x, _x);
        y = LiveComposite.Stick(y, _y);
        if (!Push(_popup, bmp, width, height, alpha, bitsDirty, x, y, move: true)) return;
        _x = x;
        _y = y;
        if (!_visible)
        {
            ShowWindow(_popup, SwShowNoActivate);
            _visible = true;
        }
    }

    public void PresentChild(IntPtr hwnd, WriteableBitmap? bmp, int width, int height, byte alpha, bool bitsDirty)
    {
        if (hwnd == IntPtr.Zero) return;
        width = LiveComposite.Stick(width, _w);
        height = LiveComposite.Stick(height, _h);
        Push(hwnd, bmp, width, height, alpha, bitsDirty, 0, 0, move: false);
    }

    public void Hide()
    {
        if (_popup == IntPtr.Zero || !_visible) return;
        ShowWindow(_popup, SwHide);
        _visible = false;
        _x = int.MinValue;
        _y = int.MinValue;
    }

    bool Push(IntPtr hwnd, WriteableBitmap? bmp, int width, int height, byte alpha, bool bitsDirty, int x, int y, bool move)
    {
        if (bmp is null || width < 2 || height < 2 || alpha < 3) return false;
        EnsureBuffer(width, height);
        if (_hdc == IntPtr.Zero || _dib == IntPtr.Zero) return false;
        if (bitsDirty) Blit(bmp, width, height);
        if (!bitsDirty && !move) return true;
        if (!bitsDirty && move)
        {
            if (_visible && x == _x && y == _y) return true;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNosize | SwpNozorder | SwpNoActivate);
            return true;
        }

        var size = new NativeSize { Cx = width, Cy = height };
        var src = new NativePoint { X = 0, Y = 0 };
        var blend = new BlendFunction
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = alpha,
            AlphaFormat = 0,
        };
        if (move)
        {
            var dst = new NativePoint { X = x, Y = y };
            UpdateLayeredWindowMove(hwnd, IntPtr.Zero, ref dst, ref size, _hdc, ref src, 0, ref blend, UlwAlpha);
        }
        else
            UpdateLayeredWindowStay(hwnd, IntPtr.Zero, IntPtr.Zero, ref size, _hdc, ref src, 0, ref blend, UlwAlpha);
        return true;
    }

    void Blit(WriteableBitmap bmp, int width, int height)
    {
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
    }

    void EnsureWindow()
    {
        if (_popup != IntPtr.Zero) return;
        EnsureClass();
        _popup = CreateWindowEx(
            WsExLayered | WsExNoActivate | WsExToolwindow | WsExTopmost | WsExTransparent,
            ClassName,
            "",
            WsPopup,
            0, 0, 1, 1,
            IntPtr.Zero,
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
        if (_popup != IntPtr.Zero)
        {
            DestroyWindow(_popup);
            _popup = IntPtr.Zero;
        }
        DiscardBuffer();
        GC.SuppressFinalize(this);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern ushort RegisterClass(ref WndClass lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr DefWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool ValidateRect(IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow", SetLastError = true)]
    static extern bool UpdateLayeredWindowMove(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize,
        IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

    [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow", SetLastError = true)]
    static extern bool UpdateLayeredWindowStay(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref NativeSize psize,
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
    static extern IntPtr GetStockObject(int stock);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo info, int usage, out IntPtr bits, IntPtr section, int offset);

    [DllImport("gdi32.dll")]
    static extern int StretchDIBits(IntPtr hdc, int xDest, int yDest, int destWidth, int destHeight,
        int xSrc, int ySrc, int srcWidth, int srcHeight, IntPtr bits, ref BitmapInfo info, int usage, int rop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WndClass
    {
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }

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
