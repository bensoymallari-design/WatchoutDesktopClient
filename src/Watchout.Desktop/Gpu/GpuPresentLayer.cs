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
    readonly GpuOutputHost? _host;
    WriteableBitmap? _bmp;
    (int W, int H) _loggedSize;
    bool _loggedTopLevel;

    public GpuPresentLayer(bool output)
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        _output = output;
        if (output)
        {
            _host = new GpuOutputHost();
            Children.Add(_host);
        }
        else
        {
            _image = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
            Children.Add(_image);
        }
    }

    public void Present(IReadOnlyList<GpuDraw> draws, Display? display, bool playAudio, bool keepLastFrame = false)
    {
        var w = Math.Max(2, (int)Math.Round(ActualWidth > 8 ? ActualWidth : Width));
        var h = Math.Max(2, (int)Math.Round(ActualHeight > 8 ? ActualHeight : Height));
        if (display is not null && w < 64)
        {
            w = Math.Max(64, (int)Math.Round(display.Width));
            h = Math.Max(64, (int)Math.Round(display.Height));
        }
        if (_output && _host is not null)
        {
            PresentOutputHost(draws, display, playAudio, keepLastFrame);
            return;
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
        var scale = 1.0;
        var src = PresentationSource.FromVisual(this)
            ?? (win is null ? null : PresentationSource.FromVisual(win));
        if (src?.CompositionTarget is { } ct)
            scale = ct.TransformToDevice.M11;
        var dipW = ActualWidth > 8 ? ActualWidth : Width;
        var dipH = ActualHeight > 8 ? ActualHeight : Height;
        var widget = OutputViewMath.DipToPixels(dipW, dipH, scale);
        var displayW = display is null ? 0 : (int)Math.Round(display.Width);
        var displayH = display is null ? 0 : (int)Math.Round(display.Height);
        var (w, h) = OutputViewMath.PresentSize(
            widget.W, widget.H, displayW, displayH,
            win?.PixelWidth ?? 0, win?.PixelHeight ?? 0);

        if (_host!.Handle == IntPtr.Zero)
        {
            UpdateLayout();
            _host.UpdateLayout();
        }
        if (_host.EnsureSize(w, h) && _loggedSize != (w, h))
        {
            _loggedSize = (w, h);
            App.Session.Log($"Output HWND resizing to {w}×{h}");
        }

        var hostHwnd = _host.Handle;
        var hostSize = NativeWindow.ClientSize(hostHwnd);
        nint hwnd;
        if (hostHwnd != IntPtr.Zero && !OutputViewMath.PreferTopLevelHwnd(hostSize.W, hostSize.H, w, h))
            hwnd = hostHwnd;
        else
        {
            hwnd = win is not null ? new WindowInteropHelper(win).Handle : hostHwnd;
            if (hwnd != IntPtr.Zero && hwnd != hostHwnd && !_loggedTopLevel)
            {
                _loggedTopLevel = true;
                App.Session.Log($"Output presenting on the wall window {w}×{h} (nested HWND was {hostSize.W}×{hostSize.H})");
            }
        }
        if (hwnd == IntPtr.Zero) return;
        GpuEngine.PresentOutput(hwnd, w, h, draws, display, playAudio, keepLastFrame);
    }
}

sealed class GpuOutputHost : HwndHost
{
    IntPtr _hwnd;
    int _w = 64;
    int _h = 64;

    public new IntPtr Handle => _hwnd != IntPtr.Zero ? _hwnd : base.Handle;

    public bool EnsureSize(int w, int h)
    {
        w = Math.Max(2, w);
        h = Math.Max(2, h);
        if (_hwnd == IntPtr.Zero) return false;
        if (_w == w && _h == h)
        {
            var client = NativeWindow.ClientSize(_hwnd);
            if (!OutputViewMath.HostNeedsResize(client.W, client.H, w, h))
                return false;
        }
        NativeWindow.Resize(_hwnd, w, h);
        _w = w;
        _h = h;
        return true;
    }

    protected override System.Runtime.InteropServices.HandleRef BuildWindowCore(System.Runtime.InteropServices.HandleRef hwndParent)
    {
        _hwnd = CreateWindowEx(0, "Static", "", 0x40000000 | 0x10000000, 0, 0, 64, 64, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return new System.Runtime.InteropServices.HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(System.Runtime.InteropServices.HandleRef hwnd)
    {
        GpuEngine.DropOutput(hwnd.Handle);
        DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_hwnd == IntPtr.Zero) return;
        var src = PresentationSource.FromVisual(this);
        var scale = src?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var pixels = OutputViewMath.DipToPixels(sizeInfo.NewSize.Width, sizeInfo.NewSize.Height, scale);
        EnsureSize(pixels.W, pixels.H);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool DestroyWindow(IntPtr hwnd);
}
