using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using Watchout.Core.Media;

namespace Watchout.Desktop.Media;

public sealed record CaptureDeviceInfo(string Id, string Name, string Kind);

/// <summary>
/// Shared live HDMI/SDI capture (Elgato, Blackmagic, Magewell, USB capture, NDI Webcam Input).
/// One session per device so Stage and Runner outputs see the same Resolume feed.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class CaptureHub
{
    static readonly Dictionary<string, Session> Sessions = [];
    static readonly object Gate = new();
    public static IReadOnlyList<CaptureDeviceInfo> Devices { get; private set; } = [];
    public static int Generation { get; private set; }
    public static int LiveCount { get { lock (Gate) return Sessions.Count; } }
    public static event Action? Changed;

    public static async Task RefreshAsync()
    {
        IReadOnlyList<CaptureDeviceInfo> list = [];
        string? error = null;
        try
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            var found = new List<CaptureDeviceInfo>();
            foreach (var group in groups)
            {
                if (!group.SourceInfos.Any(i => i.SourceKind == MediaFrameSourceKind.Color)) continue;
                found.Add(new CaptureDeviceInfo(
                    group.Id,
                    string.IsNullOrWhiteSpace(group.DisplayName) ? "Capture device" : group.DisplayName,
                    Classify(group.DisplayName)));
            }
            list = found;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        void Apply()
        {
            Devices = list;
            Generation++;
            if (error is not null) App.Session.Log($"Could not list capture cards: {error}", "error");
            Changed?.Invoke();
        }

        var dispatcher = App.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.Invoke(Apply);
        else Apply();
    }

    public static WriteableBitmap? Retain(string deviceId, Action onFrame)
    {
        lock (Gate)
        {
            if (!Sessions.TryGetValue(deviceId, out var session))
            {
                session = new Session(deviceId);
                Sessions[deviceId] = session;
                _ = session.StartAsync();
            }
            session.Add(onFrame);
            return session.Bitmap;
        }
    }

    public static WriteableBitmap? Peek(string deviceId)
    {
        lock (Gate) return Sessions.TryGetValue(deviceId, out var session) ? session.Bitmap : null;
    }

    public static void Release(string deviceId, Action onFrame)
    {
        Session? dead = null;
        lock (Gate)
        {
            if (!Sessions.TryGetValue(deviceId, out var session)) return;
            session.Remove(onFrame);
            if (session.Listeners == 0)
            {
                Sessions.Remove(deviceId);
                dead = session;
            }
        }
        dead?.Dispose();
    }

    public static void Shutdown()
    {
        Session[] all;
        lock (Gate)
        {
            all = Sessions.Values.ToArray();
            Sessions.Clear();
        }
        foreach (var s in all) s.Dispose();
    }

    static string Classify(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("ndi"))
            return "NDI";
        if (n.Contains("elgato") || n.Contains("blackmagic") || n.Contains("decklink") || n.Contains("intensity")
            || n.Contains("magewell") || n.Contains("cam link") || n.Contains("capture") || n.Contains("hdmi")
            || n.Contains("sdi"))
            return "Capture card";
        return "Camera / video device";
    }

    sealed class Session : IDisposable
    {
        readonly string _id;
        readonly List<Action> _listeners = [];
        readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
        MediaCapture? _capture;
        MediaFrameReader? _reader;
        byte[]? _scratch;
        int _width;
        int _height;
        bool _logged;
        int _uiBusy;

        public WriteableBitmap? Bitmap { get; private set; }
        public int Listeners { get { lock (_listeners) return _listeners.Count; } }

        public Session(string id) => _id = id;

        public void Add(Action onFrame)
        {
            lock (_listeners) _listeners.Add(onFrame);
        }

        public void Remove(Action onFrame)
        {
            lock (_listeners) _listeners.Remove(onFrame);
        }

        public async Task StartAsync()
        {
            try
            {
                var groups = await MediaFrameSourceGroup.FindAllAsync();
                var group = groups.FirstOrDefault(g => g.Id == _id) ?? groups.FirstOrDefault(g => g.DisplayName == _id);
                if (group is null)
                {
                    App.Current.Dispatcher.Invoke(() => App.Session.Log($"Capture device not found: {_id}", "error"));
                    return;
                }

                var sourceInfo = group.SourceInfos.FirstOrDefault(i => i.SourceKind == MediaFrameSourceKind.Color)
                                 ?? group.SourceInfos.FirstOrDefault();
                if (sourceInfo is null) return;

                _capture = new MediaCapture();
                var settings = new MediaCaptureInitializationSettings
                {
                    SourceGroup = group,
                    SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                };
                try
                {
                    await _capture.InitializeAsync(settings);
                }
                catch
                {
                    settings.SharingMode = MediaCaptureSharingMode.SharedReadOnly;
                    await _capture.InitializeAsync(settings);
                }

                if (!_capture.FrameSources.TryGetValue(sourceInfo.Id, out var source))
                    source = _capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
                if (source is null) return;

                await TryPreferHdAsync(source);

                _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
                _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
                _reader.FrameArrived += OnFrame;
                var status = await _reader.StartAsync();
                App.Current.Dispatcher.Invoke(() =>
                    App.Session.Log(status == MediaFrameReaderStartStatus.Success
                        ? $"Capture live: {group.DisplayName} — feed Resolume (or any HDMI/SDI) into this card"
                        : $"Capture card opened but frames failed ({status}). Close other apps that have locked the same input.",
                        status == MediaFrameReaderStartStatus.Success ? "info" : "warn"));
            }
            catch (Exception ex)
            {
                App.Current.Dispatcher.Invoke(() => App.Session.Log($"Capture card failed: {ex.Message}", "error"));
            }
        }

        static async Task TryPreferHdAsync(MediaFrameSource source)
        {
            try
            {
                var listed = source.SupportedFormats
                    .Select(f => (
                        Format: f,
                        W: (int)f.VideoFormat.Width,
                        H: (int)f.VideoFormat.Height,
                        Fps: f.FrameRate.Numerator / (double)Math.Max(1, f.FrameRate.Denominator)))
                    .ToList();
                var pick = LivePicture.PickCaptureFormat(listed.Select(f => (f.W, f.H, f.Fps)));
                if (pick is null) return;
                var match = listed
                    .Where(f => f.W == pick.Value.W && f.H == pick.Value.H)
                    .OrderBy(f => Math.Abs(f.Fps - pick.Value.Fps))
                    .Select(f => f.Format)
                    .FirstOrDefault();
                if (match is not null) await source.SetFormatAsync(match);
            }
            catch
            {
                /* use default format */
            }
        }

        void OnFrame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            if (Interlocked.CompareExchange(ref _uiBusy, 1, 0) != 0) return;
            try
            {
                using var frame = sender.TryAcquireLatestFrame();
                var raw = frame?.VideoMediaFrame?.SoftwareBitmap;
                if (raw is null)
                {
                    Interlocked.Exchange(ref _uiBusy, 0);
                    return;
                }
                SoftwareBitmap bgra = raw;
                var converted = false;
                if (raw.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
                {
                    bgra = SoftwareBitmap.Convert(raw, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    converted = true;
                }
                var w = bgra.PixelWidth;
                var h = bgra.PixelHeight;
                var needed = w * h * 4;
                if (_scratch is null || _scratch.Length != needed) _scratch = new byte[needed];
                var buffer = new Windows.Storage.Streams.Buffer((uint)needed);
                bgra.CopyToBuffer(buffer);
                using var reader = DataReader.FromBuffer(buffer);
                reader.ReadBytes(_scratch);
                if (converted) bgra.Dispose();

                _ui.BeginInvoke(() =>
                {
                    try
                    {
                        if (Bitmap is null || _width != w || _height != h)
                        {
                            Bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                            _width = w;
                            _height = h;
                            if (!_logged)
                            {
                                _logged = true;
                                App.Session.Log($"Capture signal {w}×{h} — live on Stage and Output");
                            }
                            App.Session.NoteLiveFrameSize(null, _id, w, h);
                        }
                        Bitmap.WritePixels(new Int32Rect(0, 0, w, h), _scratch, w * 4, 0);
                        Action[] listeners;
                        lock (_listeners) listeners = _listeners.ToArray();
                        foreach (var fn in listeners) fn();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _uiBusy, 0);
                    }
                }, DispatcherPriority.Render);
            }
            catch
            {
                Interlocked.Exchange(ref _uiBusy, 0);
            }
        }

        public void Dispose()
        {
            try { if (_reader is not null) _reader.FrameArrived -= OnFrame; } catch { /* ignore */ }
            try { _reader?.StopAsync().AsTask().GetAwaiter().GetResult(); } catch { /* ignore */ }
            _reader?.Dispose();
            try { _capture?.Dispose(); } catch { /* ignore */ }
        }
    }
}
