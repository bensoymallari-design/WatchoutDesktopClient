using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Watchout.Core.Models;
using Watchout.Desktop.Interop;
using Watchout.Desktop.Views;

namespace Watchout.Desktop.Output;

public sealed class OutputWindow : Window
{
    public string DisplayId { get; }
    readonly OutputScreen _screen;
    readonly StageSurface _surface;

    public OutputWindow(Display display, OutputScreen screen, bool playAudio)
    {
        DisplayId = display.Id;
        _screen = screen;
        Title = $"WatchMe · {display.Name}";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Normal;
        Background = Brushes.Black;
        Topmost = true;
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
        Loaded += (_, _) =>
        {
            PlaceOnScreen();
            _surface.Refresh();
        };
        DpiChanged += (_, _) => PlaceOnScreen();
    }

    public void BindDisplay(Display display) => _surface.ViewDisplay = display;

    public void PlaceOnScreen()
    {
        WindowState = WindowState.Normal;
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeWindow.Place(hwnd, _screen.Left, _screen.Top, _screen.Width, _screen.Height);
        UpdateLayout();
        _surface.UpdateLayout();
        _surface.Refresh();
    }
}
