using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Watchout.Core.Stage;
using Watchout.Desktop.Interop;
using Watchout.Desktop.Media;
using Watchout.Desktop.Output;

namespace Watchout.Desktop.Views;

/// <summary>
/// Live NDI/capture above DXVA. Stage uses a layered child HWND (no WM_PAINT erase).
/// Output uses a top-level layered popup because the fullscreen video overlay covers children.
/// </summary>
public sealed class CaptureLayer : HwndHost
{
    const int WmPaint = 0x000F;
    const int WmEraseBkgnd = 0x0014;
    const int WsChild = 0x40000000;
    const int WsVisible = 0x10000000;
    const int WsClipSiblings = 0x04000000;
    const int WsExLayered = 0x00080000;
    const int WsExNoActivate = 0x08000000;
    const int WsExTransparent = 0x00000020;
    const uint SwpNomove = 0x0002;
    const uint SwpNoActivate = 0x0010;
    const uint SwpHideWindow = 0x0080;
    static readonly IntPtr HwndBottom = new(1);

    string? _deviceId;
    string? _ndiName;
    bool _listening;
    IntPtr _hwnd;
    WriteableBitmap? _bmp;
    readonly Action _onFrame;
    LiveOverlayWindow? _overlay;
    bool _output;
    bool _wantOverlay;
    bool _syncing;
    bool _childHidden;
    int _childW;
    int _childH;
    double _childX = double.NaN;
    double _childY = double.NaN;
    long _lastBitsMs;
    double _overlayDipW;
    double _overlayDipH;

    public CaptureLayer()
    {
        _onFrame = OnFrame;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _output = Window.GetWindow(this) is OutputWindow;
        if (_output) _wantOverlay = true;
        Attach();
        Redraw();
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Detach();
        _overlay?.Dispose();
        _overlay = null;
        _wantOverlay = false;
        _childHidden = false;
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

    public void RaiseOverlay() => _overlay?.Raise();

    public void SetOverlayDipSize(double width, double height)
    {
        _overlayDipW = width;
        _overlayDipH = height;
    }

    public void SetOutputOverlay(bool want)
    {
        _output = Window.GetWindow(this) is OutputWindow;
        if (!_output)
        {
            _wantOverlay = false;
            return;
        }
        if (_wantOverlay == want)
        {
            SyncOverlay(forceBits: false);
            return;
        }
        _wantOverlay = want;
        if (!want) _overlay?.Hide();
        else SyncOverlay(forceBits: true);
    }

    public void SyncOverlay() => SyncOverlay(forceBits: false);

    void SyncOverlay(bool forceBits)
    {
        if (_syncing) return;
        _output = Window.GetWindow(this) is OutputWindow;
        if (!_output || !_wantOverlay || _bmp is null) return;
        var dipW = _overlayDipW > 1 ? _overlayDipW : ActualWidth;
        var dipH = _overlayDipH > 1 ? _overlayDipH : ActualHeight;
        if (dipW < 2 || dipH < 2) return;

        Point tl;
        Point br;
        try
        {
            tl = PointToScreen(new Point(0, 0));
            br = PointToScreen(new Point(dipW, dipH));
        }
        catch
        {
            return;
        }

        var x = (int)Math.Round(Math.Min(tl.X, br.X));
        var y = (int)Math.Round(Math.Min(tl.Y, br.Y));
        var w = (int)Math.Round(Math.Abs(br.X - tl.X));
        var h = (int)Math.Round(Math.Abs(br.Y - tl.Y));
        var alpha = (byte)Math.Clamp(Opacity * 255, 0, 255);
        var owner = Window.GetWindow(this) is { } win ? new WindowInteropHelper(win).Handle : IntPtr.Zero;
        _syncing = true;
        try
        {
            _overlay ??= new LiveOverlayWindow();
            _overlay.Present(_bmp, x, y, w, h, alpha, forceBits, owner);
        }
        finally
        {
            _syncing = false;
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        LiveOverlayWindow.EnsureClass();
        _hwnd = CreateWindowEx(
            WsExLayered | WsExNoActivate | WsExTransparent,
            LiveOverlayWindow.ClassName,
            "",
            WsChild | WsVisible | WsClipSiblings,
            0, 0,
            Math.Max(1, (int)Math.Ceiling(Math.Max(double.IsNaN(Width) ? 1 : Width, 1))),
            Math.Max(1, (int)Math.Ceiling(Math.Max(double.IsNaN(Height) ? 1 : Height, 1))),
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
        _childHidden = false;
    }

    protected override void OnWindowPositionChanged(Rect rc)
    {
        _output = Window.GetWindow(this) is OutputWindow;
        if (_output)
        {
            if (_hwnd != IntPtr.Zero && !_childHidden)
            {
                SetWindowPos(_hwnd, HwndBottom, 0, 0, 1, 1, SwpNoActivate | SwpHideWindow | SwpNomove);
                _childHidden = true;
            }
            return;
        }

        var w = LiveComposite.Stick(Math.Max(1, (int)Math.Round(rc.Width)), _childW);
        var h = LiveComposite.Stick(Math.Max(1, (int)Math.Round(rc.Height)), _childH);
        var sized = w != _childW || h != _childH;
        var moved = double.IsNaN(_childX) || double.IsNaN(_childY)
            || Math.Abs(rc.X - _childX) > 0.5 || Math.Abs(rc.Y - _childY) > 0.5;
        if (!sized && !moved) return;
        _childW = w;
        _childH = h;
        _childX = rc.X;
        _childY = rc.Y;
        base.OnWindowPositionChanged(new Rect(rc.X, rc.Y, w, h));
        if (sized) Redraw();
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmEraseBkgnd)
        {
            handled = true;
            return new IntPtr(1);
        }
        if (msg == WmPaint)
        {
            ValidateRect(hwnd, IntPtr.Zero);
            handled = true;
            return IntPtr.Zero;
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
        var now = Environment.TickCount64;
        var bits = now - _lastBitsMs >= 33;
        if (_output)
        {
            if (bits) _lastBitsMs = now;
            SyncOverlay(forceBits: bits);
            return;
        }
        if (!bits) return;
        _lastBitsMs = now;
        if (_hwnd == IntPtr.Zero || _bmp is null) return;
        var w = Math.Max(_childW, (int)Math.Round(ActualWidth));
        var h = Math.Max(_childH, (int)Math.Round(ActualHeight));
        if (w < 2 || h < 2) return;
        var alpha = (byte)Math.Clamp(Opacity * 255, 0, 255);
        _overlay ??= new LiveOverlayWindow();
        _overlay.PresentChild(_hwnd, _bmp, w, h, alpha, bitsDirty: true);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    static extern bool ValidateRect(IntPtr hwnd, IntPtr rect);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? lpModuleName);
}
