using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Watchout.Core.Gpu;
using Watchout.Core.Models;

namespace Watchout.Desktop.Gpu;

public sealed class GpuPresentLayer : Grid
{
    readonly bool _output;
    readonly Image? _image;
    readonly GpuOutputHost? _host;
    WriteableBitmap? _bmp;

    public GpuPresentLayer(bool output)
    {
        IsHitTestVisible = false;
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
            var hwnd = _host.Handle;
            if (hwnd != IntPtr.Zero)
                GpuEngine.PresentOutput(hwnd, w, h, draws, display, playAudio, keepLastFrame);
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
}

sealed class GpuOutputHost : HwndHost
{
    IntPtr _hwnd;

    public new IntPtr Handle => _hwnd != IntPtr.Zero ? _hwnd : base.Handle;

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
        var w = Math.Max(2, (int)Math.Round(sizeInfo.NewSize.Width));
        var h = Math.Max(2, (int)Math.Round(sizeInfo.NewSize.Height));
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, w, h, 0x0002 | 0x0004 | 0x0010);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool DestroyWindow(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
