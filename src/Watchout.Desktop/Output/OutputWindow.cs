using System.Windows;
using System.Windows.Input;
using Watchout.Core.Models;
using Watchout.Desktop.Views;

namespace Watchout.Desktop.Output;

public sealed class OutputWindow : Window
{
    public string DisplayId { get; }
    readonly StageSurface _surface;

    public OutputWindow(Display display, OutputScreen screen, bool playAudio)
    {
        DisplayId = display.Id;
        Title = $"WATCHOUT · {display.Name}";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = System.Windows.Media.Brushes.Black;
        Topmost = true;
        ShowInTaskbar = false;
        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = screen.Height;
        _surface = new StageSurface
        {
            Editing = false,
            ViewDisplay = display,
            PlayAudio = playAudio,
        };
        Content = _surface;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        Loaded += (_, _) =>
        {
            WindowState = WindowState.Maximized;
            _surface.Refresh();
        };
    }

    public void BindDisplay(Display display) => _surface.ViewDisplay = display;
}
