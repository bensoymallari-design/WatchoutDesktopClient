using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Watchout.Core.Gpu;
using Watchout.Core.Models;
using Watchout.Desktop.Interop;
using Watchout.Desktop.Output;

namespace Watchout.Desktop.Gpu;

public sealed class GpuPresentLayer : Grid
{
    readonly bool _output;
    readonly Image? _image;
    WriteableBitmap? _bmp;
    bool _loggedWall;

    public GpuPresentLayer(bool output)
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        _output = output;
        if (!output)
        {
            _image = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
            Children.Add(_image);
        }
    }

    public void Present(IReadOnlyList<GpuDraw> draws, Display? display, bool playAudio, bool keepLastFrame = false)
    {
        if (_output)
        {
            PresentOutputHost(draws, display, playAudio, keepLastFrame);
            return;
        }
        var w = Math.Max(2, (int)Math.Round(ActualWidth > 8 ? ActualWidth : Width));
        var h = Math.Max(2, (int)Math.Round(ActualHeight > 8 ? ActualHeight : Height));
        if (display is not null && w < 64)
        {
            w = Math.Max(64, (int)Math.Round(display.Width));
            h = Math.Max(64, (int)Math.Round(display.Height));
        }
        if (_image is null) return;
        if (_bmp is null || _bmp.PixelWidth != w || _bmp.PixelHeight != h)
        {
            _bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            _image.Source = _bmp;
        }
        GpuEngine.PresentStage(_bmp, draws, playAudio, keepLastFrame);
    }

    void PresentOutputHost(IReadOnlyList<GpuDraw> draws, Display? display, bool playAudio, bool keepLastFrame)
    {
        var win = Window.GetWindow(this) as OutputWindow;
        win?.EnsureWall();
        var hwnd = win?.WallHwnd ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero && win is not null)
            hwnd = new WindowInteropHelper(win).Handle;
        if (hwnd == IntPtr.Zero) return;
        var client = NativeWindow.ClientSize(hwnd);
        var displayW = display is null ? 0 : (int)Math.Round(display.Width);
        var displayH = display is null ? 0 : (int)Math.Round(display.Height);
        var dest = win?.DestSize ?? OutputViewMath.PresentDest(client.W, client.H, 0, 0);
        var w = dest.W;
        var h = dest.H;
        if (w < 64 || h < 64)
            (w, h) = OutputViewMath.PresentSize(client.W, client.H, displayW, displayH, win?.PixelWidth ?? 0, win?.PixelHeight ?? 0);
        if (!_loggedWall)
        {
            _loggedWall = true;
            var cue = draws.Count > 0 ? $"{draws[0].W:0}×{draws[0].H:0}" : "none";
            App.Session.Log($"Output wall {w}×{h} · Stage display {displayW}×{displayH} · cue {cue} — 1:1 pixels");
        }
        NativeWindow.KeepTopmost(hwnd);
        GpuEngine.PresentOutput(hwnd, w, h, draws, display, playAudio, keepLastFrame);
    }
}
