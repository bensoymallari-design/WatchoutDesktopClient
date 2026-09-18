using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Watchout.Core.Gpu;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

/// <summary>
/// Producer Stage picture when Output already owns the DXVA MediaElement.
/// ffmpeg grabs a JPEG at the playhead so Stage never opens a second MF
/// reader (that stayed black on this clip, and dual DXVA killed the wall).
/// </summary>
sealed class StageSoftPreview : Image, IDisposable
{
    string? _path;
    double _shownMs = -1;
    DateTime _grabUtc = DateTime.MinValue;
    int _gen;
    bool _busy;
    bool _logged;
    bool _loggedFail;

    public StageSoftPreview()
    {
        Stretch = Stretch.Fill;
        Focusable = false;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    public void Sync(string filePath, double mediaMs, bool playing, bool loop)
    {
        _ = loop;
        if (!string.Equals(_path, filePath, StringComparison.OrdinalIgnoreCase))
        {
            _gen++;
            _path = filePath;
            _shownMs = -1;
            Source = null;
            _busy = false;
            _loggedFail = false;
        }
        var since = (_grabUtc == DateTime.MinValue)
            ? double.PositiveInfinity
            : (DateTime.UtcNow - _grabUtc).TotalMilliseconds;
        if (_busy || !GpuLayerMath.SoftPreviewShouldGrab(_shownMs, mediaMs, since, playing, Source is not null))
            return;
        _busy = true;
        _grabUtc = DateTime.UtcNow;
        var gen = _gen;
        _ = GrabAsync(gen, filePath, mediaMs);
    }

    async Task GrabAsync(int gen, string path, double timeMs)
    {
        try
        {
            if (!_logged)
            {
                _logged = true;
                Ui(() => App.Session.Log("Stage software preview — ffmpeg frame at the playhead; Output keeps DXVA"));
            }
            if (!FfmpegTools.Available)
            {
                Fail("ffmpeg is not available");
                return;
            }
            var dest = Path.Combine(Path.GetTempPath(), "WatchMe", $"stage-preview-{Environment.ProcessId}-{gen}.jpg");
            var file = await FfmpegTools.ExtractFrameAsync(path, timeMs, dest);
            if (file is null)
            {
                Fail("ffmpeg could not grab a frame at the playhead");
                return;
            }
            var bmp = LoadJpeg(file);
            try { File.Delete(file); } catch { /* temp */ }
            if (bmp is null)
            {
                Fail("Stage could not load the preview JPEG");
                return;
            }
            Ui(() =>
            {
                if (gen != _gen) return;
                Source = bmp;
                _shownMs = timeMs;
            });
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        finally
        {
            Ui(() => { if (gen == _gen) _busy = false; });
        }
    }

    void Fail(string detail)
    {
        if (_loggedFail) return;
        _loggedFail = true;
        Ui(() => App.Session.Log($"Stage software preview failed — {detail}", "warn"));
    }

    void Ui(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }

    static BitmapImage? LoadJpeg(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _gen++;
        _path = null;
        Source = null;
        _busy = false;
    }
}
