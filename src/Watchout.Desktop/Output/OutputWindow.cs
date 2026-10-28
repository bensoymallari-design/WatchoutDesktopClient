using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Watchout.Core.Gpu;
using Watchout.Core.Models;
using Watchout.Core.Stage;
using Watchout.Desktop.Gpu;
using Watchout.Desktop.Interop;
using Watchout.Desktop.Views;

namespace Watchout.Desktop.Output;

public sealed class OutputWindow : Window
{
    public string DisplayId { get; }
    OutputScreen _screen;
    readonly StageSurface _surface;
    GpuOutputWall? _wall;
    bool _loggedOwnerFail;
    bool _loggedDropWall;
    bool _placing;

    public (int W, int H) DestSize
    {
        get
        {
            var client = NativeWindow.ClientSize(WallHwnd);
            return OutputViewMath.PresentDest(client.W, client.H, ScreenW, ScreenH);
        }
    }

    int ScreenW => ScreenAssign.ScreenWidth(_screen);
    int ScreenH => ScreenAssign.ScreenHeight(_screen);

    public int PixelWidth => DestSize.W;
    public int PixelHeight => DestSize.H;
    public IntPtr WallHwnd => _wall?.Hwnd ?? IntPtr.Zero;

    public OutputWindow(Display display, OutputScreen screen, bool playAudio)
    {
        DisplayId = display.Id;
        _screen = screen;
        Title = $"WatchMe · {display.Name}";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Normal;
        Background = Brushes.Black;
        Topmost = false;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Left = screen.Left;
        Top = screen.Top;
        var dip = OutputViewMath.ScreenToDip(ScreenAssign.ScreenWidth(screen), ScreenAssign.ScreenHeight(screen), screen.ScaleFactor);
        Width = Math.Max(64, dip.DipW);
        Height = Math.Max(64, dip.DipH);
        _surface = new StageSurface
        {
            Editing = false,
            ViewDisplay = display,
            PlayAudio = playAudio,
            UseLayoutRounding = true,
        };
        RenderOptions.SetBitmapScalingMode(_surface, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(_surface, EdgeMode.Aliased);
        Content = _surface;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (HwndSource.FromHwnd(hwnd) is { } source)
                source.AddHook(HoldOutputRect);
            PlaceOnScreen();
        };
        Loaded += (_, _) => PlaceOnScreen(refresh: true);
        Closing += (_, _) =>
        {
            _wall?.Dispose();
            _wall = null;
            Hide();
        };
    }

    public void BindDisplay(Display display) => _surface.ViewDisplay = display;

    public void DropWall()
    {
        if (_wall is null) return;
        _wall.Dispose();
        _wall = null;
        if (_loggedDropWall) return;
        _loggedDropWall = true;
        App.Session.Log("Output black — D3D11 compositor is off so the empty GPU wall HWND was covering Play. Showing the WPF window instead. Not RAM.", "warn");
    }

    public void EnsureWall()
    {
        var owner = new WindowInteropHelper(this).Handle;
        if (owner == IntPtr.Zero)
        {
            if (!_loggedOwnerFail)
            {
                _loggedOwnerFail = true;
                App.Session.Log("Output black — Output window handle is 0; wall HWND was not created.", "error");
            }
            return;
        }
        try
        {
            var (w, h) = OutputViewMath.WallPixels(ScreenW, ScreenH);
            if (_wall is null)
                _wall = GpuOutputWall.Create(owner, _screen.Left, _screen.Top, w, h);
            else
                _wall.Place(_screen.Left, _screen.Top, w, h);
        }
        catch (Exception ex)
        {
            App.Session.Log($"Output wall HWND failed — {ex.Message}", "error");
        }
    }

    /// <summary>
    /// A 150% laptop panel sends WM_DPICHANGED with a rect for that scale.
    /// HDMI, DisplayPort, and USB-C walls are a different scale. mpv and OBS
    /// ignore the suggestion and keep the OS monitor rectangle; WPF would
    /// otherwise shrink a 4K port to 2560.
    /// </summary>
    IntPtr HoldOutputRect(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmDpiChanged = 0x02E0;
        const int WmWindowPosChanging = 0x0046;
        const uint SwpNoMove = 0x0002;
        const uint SwpNoSize = 0x0001;
        if (msg == WmDpiChanged)
        {
            PlaceOnScreen();
            handled = true;
            return IntPtr.Zero;
        }
        if (msg != WmWindowPosChanging || lParam == IntPtr.Zero) return IntPtr.Zero;
        var pos = Marshal.PtrToStructure<WindowPos>(lParam);
        var (w, h) = OutputViewMath.WallPixels(ScreenW, ScreenH);
        if ((pos.Flags & SwpNoMove) == 0)
        {
            pos.X = _screen.Left;
            pos.Y = _screen.Top;
        }
        if ((pos.Flags & SwpNoSize) == 0 && w >= 64 && h >= 64)
        {
            pos.Cx = w;
            pos.Cy = h;
        }
        Marshal.StructureToPtr(pos, lParam, false);
        return IntPtr.Zero;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) =>
        PlaceOnScreen(refresh: true);

    public void PlaceOnScreen(bool refresh = false)
    {
        if (_placing) return;
        _placing = true;
        try
        {
        WindowState = WindowState.Normal;
        var hwnd = new WindowInteropHelper(this).Handle;
        var (w, h) = OutputViewMath.WallPixels(ScreenW, ScreenH);
        NativeWindow.Place(hwnd, _screen.Left, _screen.Top, w, h, topmost: false);
        if (GpuEngine.Available) EnsureWall();
        else DropWall();
        if (WallHwnd != IntPtr.Zero) NativeWindow.KeepTopmost(WallHwnd);
        if (!refresh) return;
        UpdateLayout();
        _surface.UpdateLayout();
        _surface.Refresh();
        }
        finally
        {
            _placing = false;
        }
    }

    public void ReviveAfterSinkChange()
    {
        var screens = Monitors.List();
        if (ViewDisplay is { } display)
        {
            var target = ScreenAssign.ResolveOutputScreen(display, screens, null, out _);
            if (target is not null) _screen = target;
        }
        PlaceOnScreen(refresh: true);
        _surface.SnapToPlayhead();
    }

    Display? ViewDisplay => _surface.ViewDisplay;

    [StructLayout(LayoutKind.Sequential)]
    struct WindowPos
    {
        public IntPtr Hwnd;
        public IntPtr HwndInsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
        public uint Flags;
    }
}
