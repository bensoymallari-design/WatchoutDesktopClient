using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Watchout.Core.Gpu;
using Watchout.Core.Models;
using Watchout.Desktop.Gpu;
using Watchout.Desktop.Interop;
using Watchout.Desktop.Views;

namespace Watchout.Desktop.Output;

public sealed class OutputWindow : Window
{
    public string DisplayId { get; }
    readonly OutputScreen _screen;
    readonly StageSurface _surface;
    GpuOutputWall? _wall;

    public int PixelWidth => OutputViewMath.WallPixels(_screen.Width, _screen.Height).W;
    public int PixelHeight => OutputViewMath.WallPixels(_screen.Width, _screen.Height).H;
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
        var dip = OutputViewMath.ScreenToDip(screen.Width, screen.Height, screen.ScaleFactor);
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
        SourceInitialized += (_, _) => PlaceOnScreen();
        Loaded += (_, _) => PlaceOnScreen(refresh: true);
        DpiChanged += (_, _) => PlaceOnScreen(refresh: true);
        Closing += (_, _) =>
        {
            _wall?.Dispose();
            _wall = null;
            Hide();
        };
    }

    public void BindDisplay(Display display) => _surface.ViewDisplay = display;

    public void EnsureWall()
    {
        var owner = new WindowInteropHelper(this).Handle;
        if (owner == IntPtr.Zero) return;
        try
        {
            var (w, h) = OutputViewMath.WallPixels(_screen.Width, _screen.Height);
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

    public void PlaceOnScreen(bool refresh = false)
    {
        WindowState = WindowState.Normal;
        var hwnd = new WindowInteropHelper(this).Handle;
        var (w, h) = OutputViewMath.WallPixels(_screen.Width, _screen.Height);
        NativeWindow.Place(hwnd, _screen.Left, _screen.Top, w, h, topmost: false);
        EnsureWall();
        if (WallHwnd != IntPtr.Zero) NativeWindow.KeepTopmost(WallHwnd);
        if (!refresh) return;
        UpdateLayout();
        _surface.UpdateLayout();
        _surface.Refresh();
    }
}
