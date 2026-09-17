using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    public int PixelWidth => Math.Max(64, _screen.Width);
    public int PixelHeight => Math.Max(64, _screen.Height);
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
        Width = Math.Max(64, screen.Width);
        Height = Math.Max(64, screen.Height);
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
            if (_wall is null)
                _wall = GpuOutputWall.Create(owner, _screen.Left, _screen.Top, PixelWidth, PixelHeight);
            else
                _wall.Place(_screen.Left, _screen.Top, PixelWidth, PixelHeight);
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
        NativeWindow.Place(hwnd, _screen.Left, _screen.Top, PixelWidth, PixelHeight, topmost: false);
        SyncDipSize();
        EnsureWall();
        if (WallHwnd != IntPtr.Zero) NativeWindow.KeepTopmost(WallHwnd);
        if (!refresh) return;
        UpdateLayout();
        _surface.UpdateLayout();
        _surface.Refresh();
    }

    void SyncDipSize()
    {
        var src = PresentationSource.FromVisual(this);
        var scale = src?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        if (scale < 0.1) scale = 1;
        Width = PixelWidth / scale;
        Height = PixelHeight / scale;
    }
}
