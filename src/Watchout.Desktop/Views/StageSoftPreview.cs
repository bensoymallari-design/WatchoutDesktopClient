using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Watchout.Core.Gpu;
using Watchout.Desktop.Gpu;

namespace Watchout.Desktop.Views;

/// <summary>
/// Producer Stage picture when Output already owns the DXVA MediaElement.
/// Software RGB32, hardware transforms off, capped at Stage preview size so
/// Intel UHD does not run two 4K DXVA sessions (that blacks wall and cue).
/// </summary>
sealed class StageSoftPreview : Image, IDisposable
{
    MfGpuDecoder? _decoder;
    WriteableBitmap? _bmp;
    string? _url;
    bool _logged;

    public StageSoftPreview()
    {
        Stretch = Stretch.Fill;
        Focusable = false;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    public void Sync(string fileUrl, double mediaMs, bool playing, bool loop)
    {
        if (!string.Equals(_url, fileUrl, StringComparison.OrdinalIgnoreCase))
        {
            _decoder?.Dispose();
            _decoder = null;
            _url = fileUrl;
            _bmp = null;
            Source = null;
            try
            {
                _decoder = new MfGpuDecoder(
                    fileUrl,
                    audio: false,
                    gpu: null,
                    softwareOnly: true,
                    maxEdge: GpuResidentPath.StagePreviewMaxEdge);
            }
            catch (Exception ex)
            {
                App.Session.Log($"Stage software preview failed — {ex.Message}", "warn");
                return;
            }
            if (!_logged)
            {
                _logged = true;
                App.Session.Log("Stage software preview — Output keeps the only DXVA decode");
            }
        }
        _decoder?.Sync(mediaMs, playing, loop, 0, false);
        Blit();
    }

    void Blit()
    {
        if (_decoder is null) return;
        if (!_decoder.TryCopyFrame(out var pixels, out var w, out var h, out var stride, out var dirty))
            return;
        if (!dirty && Source is not null) return;
        if (w < 2 || h < 2 || stride < 8 || pixels.Length < stride) return;
        if (_bmp is null || _bmp.PixelWidth != w || _bmp.PixelHeight != h)
        {
            _bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
            Source = _bmp;
        }
        var copyH = Math.Min(h, pixels.Length / stride);
        if (copyH < 1) return;
        _bmp.WritePixels(new Int32Rect(0, 0, w, copyH), pixels, stride, 0);
    }

    public void Dispose()
    {
        _decoder?.Dispose();
        _decoder = null;
        _url = null;
        Source = null;
        _bmp = null;
    }
}
