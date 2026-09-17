using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Watchout.Core.Gpu;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Playback;
using Watchout.Core.Stage;
using Watchout.Desktop.Gpu;
using Watchout.Desktop.Media;
using Watchout.Desktop.Output;

namespace Watchout.Desktop.Views;

public sealed class StageSurface : Canvas
{
    readonly Dictionary<string, FrameworkElement> _layers = [];
    readonly Dictionary<string, MediaElement> _videos = [];
    readonly HashSet<string> _playing = [];
    readonly HashSet<string> _primed = [];
    readonly HashSet<string> _dead = [];
    readonly HashSet<string> _ended = [];
    readonly Dictionary<string, DateTime> _lastSeek = [];
    readonly Dictionary<string, double> _lastPos = [];
    readonly Dictionary<string, DateTime> _lastAdvance = [];
    readonly List<UIElement> _chrome = [];
    bool _clockQueued;
    bool _visualQueued;
    bool _chromeNeeded;
    Display? _viewDisplay;
    Point _dragStart;
    string? _dragCueId;
    Point _dragCueOrigin;
    string? _dragDisplayId;
    Point _dragDisplayOrigin;
    string? _resizeHandle;
    bool _resizeDisplay;
    StageRect _resizeStart;
    string? _hoverDisplayId;
    bool _panning;
    bool _panArmed;
    bool _dragArmed;
    bool _dropping;
    int _decoderEpoch = -1;
    Point _panStart;
    (double X, double Y, double Zoom) _panCam;
    GpuPresentLayer? _gpu;

    public bool Editing { get; set; }
    public Display? ViewDisplay { get => _viewDisplay; set => _viewDisplay = value; }
    public bool PlayAudio { get; set; }

    Show? SurfaceShow => Editing ? App.Session.Show : App.Session.PlaybackShow;

    public StageSurface()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Focusable = true;
        Background = new SolidColorBrush(Color.FromRgb(17, 17, 17));
        AllowDrop = true;
        PreviewDragOver += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => { if (_hoverDisplayId is not null) { _hoverDisplayId = null; Refresh(); } };
        PreviewDrop += OnDrop;
        Drop += OnDrop;
        App.Session.Changed += QueueRefresh;
        App.Session.LayoutChanged += QueueLayout;
        App.Session.Clock += QueuePlaybackTick;
        App.Session.PlaybackChanged += QueueTake;
        SizeChanged += (_, _) =>
        {
            SyncViewSize();
            Refresh();
        };
        MouseWheel += OnWheel;
        PreviewMouseLeftButtonDown += OnLeftDown;
        PreviewMouseLeftButtonUp += OnLeftUp;
        MouseDown += OnAnyDown;
        MouseUp += OnAnyUp;
        PreviewMouseMove += OnMove;
        MouseRightButtonDown += (_, e) =>
        {
            _panning = true;
            _panStart = e.GetPosition(this);
            _panCam = App.Session.Camera;
            CaptureMouse();
        };
        MouseRightButtonUp += (_, _) => { _panning = false; ReleaseMouseCapture(); };
        Loaded += (_, _) =>
        {
            SyncViewSize();
            if (Editing) App.Session.FrameDisplays();
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            DropAllMedia();
            _layers.Clear();
            _chrome.Clear();
            if (_gpu is not null)
            {
                Children.Remove(_gpu);
                _gpu = null;
            }
        };
    }

    void QueueRefresh()
    {
        if (!Editing && App.Session.BlindEdit) return;
        _chromeNeeded = true;
        QueueVisual(syncMedia: true);
    }

    void QueueLayout()
    {
        if (!Editing && App.Session.BlindEdit) return;
        if (Editing) _chromeNeeded = true;
        QueueVisual(syncMedia: false);
    }

    void QueueTake()
    {
        _chromeNeeded = true;
        QueueVisual(syncMedia: true);
    }

    void QueuePlaybackTick() => QueueVisual(syncMedia: true);

    void QueueVisual(bool syncMedia)
    {
        if (syncMedia) _clockQueued = true;
        if (_visualQueued) return;
        _visualQueued = true;
        var priority = App.Session.StageLayoutBusy ? DispatcherPriority.Input : DispatcherPriority.Render;
        Dispatcher.BeginInvoke(() =>
        {
            _visualQueued = false;
            var sync = _clockQueued;
            _clockQueued = false;
            if (_chromeNeeded)
            {
                _chromeNeeded = false;
                DrawChrome();
            }
            TickPlayback(sync);
        }, priority);
    }

    public void Refresh()
    {
        DrawChrome();
        TickPlayback(syncMedia: true);
        ApplyOutputMask();
    }

    void DrawChrome()
    {
        var session = App.Session;
        var show = SurfaceShow;
        if (show is null)
        {
            DropAllMedia();
            Children.Clear();
            _layers.Clear();
            _chrome.Clear();
            return;
        }

        var (originX, originY, scale) = Viewport(show);
        foreach (var chrome in _chrome) Children.Remove(chrome);
        _chrome.Clear();

        if (!Editing) return;

        foreach (var display in show.Displays.Where(d => d.Enabled))
        {
            var r = Map(display.X, display.Y, display.Width, display.Height, originX, originY, scale);
            var selected = session.Selection.Kind == SelectionKind.Display && session.Selection.Ids.Contains(display.Id);
            var hover = display.Id == _hoverDisplayId;
            var outputLive = session.LiveOutputs.Contains(display.Id) || session.StageYieldsFileDecoder;
            var border = new Border
            {
                Width = Math.Max(2, r.Width),
                Height = Math.Max(2, r.Height),
                BorderBrush = new SolidColorBrush(hover ? Color.FromRgb(74, 222, 128) : selected ? Color.FromRgb(245, 166, 35) : Color.FromRgb(80, 80, 80)),
                BorderThickness = new Thickness(hover || selected ? 3 : 1),
                Background = new SolidColorBrush(hover ? Color.FromArgb(50, 74, 222, 128) : Color.FromArgb(40, 20, 20, 20)),
                IsHitTestVisible = false,
            };
            SetLeft(border, r.X);
            SetTop(border, r.Y);
            SetZIndex(border, 0);
            Children.Add(border);
            _chrome.Add(border);

            var cap = LiveSources.CaptureNameOnDisplay(show, display.Id);
            var key = display.Role == DisplayRole.Key ? $"KEY {Math.Max(1, display.KeyChannel)}" : "";
            var hud = new StackPanel
            {
                Width = Math.Max(2, r.Width),
                IsHitTestVisible = false,
            };
            hud.Children.Add(new TextBlock
            {
                Text = "STAGE",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 245, 166, 35)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            });
            hud.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(display.Name) ? "Display" : display.Name,
                FontSize = Math.Clamp(r.Height * 0.09, 18, 42),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(235, 245, 166, 35)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
            var sub = cap
                ?? (outputLive
                    ? $"{display.Width:0}×{display.Height:0}  ·  Output live — picture is on the wall"
                    : string.IsNullOrEmpty(key)
                        ? $"{display.Width:0}×{display.Height:0}"
                        : $"{display.Width:0}×{display.Height:0}  ·  {key}");
            hud.Children.Add(new TextBlock
            {
                Text = sub,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 175, 168)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(8, 8, 8, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            var hudH = Math.Min(120, Math.Max(64, r.Height * 0.28));
            SetLeft(hud, r.X);
            SetTop(hud, r.Y + Math.Max(0, (r.Height - hudH) / 2));
            SetZIndex(hud, 200);
            Children.Add(hud);
            _chrome.Add(hud);
            if (selected)
                DrawHandles(r, 30);
        }

        if (session.Selection.Kind != SelectionKind.Cue) return;
        var live = PlaybackClock.VisibleMedia(show);
        var selectedRects = StageGeometry.EditCueRects(
            live, show.Assets, show.Timelines.SelectMany(t => t.Cues), session.Selection);
        foreach (var item in selectedRects.Where(r => session.Selection.Ids.Contains(r.Cue.Id)))
        {
            var mapped = Map(item.Rect.X, item.Rect.Y, item.Rect.W, item.Rect.H, originX, originY, scale);
            var outline = new Rectangle
            {
                Width = Math.Max(1, mapped.Width),
                Height = Math.Max(1, mapped.Height),
                Stroke = new SolidColorBrush(Color.FromRgb(245, 166, 35)),
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            SetLeft(outline, mapped.X);
            SetTop(outline, mapped.Y);
            Children.Add(outline);
            _chrome.Add(outline);
            SetZIndex(outline, 20);
            DrawHandles(mapped, 21);
        }
    }

    void TickPlayback(bool syncMedia = true)
    {
        var session = App.Session;
        var show = SurfaceShow;
        if (show is null) return;

        if (session.DecoderEpoch != _decoderEpoch)
        {
            DropAllMedia();
            _decoderEpoch = session.DecoderEpoch;
        }

        if (!Editing && Window.GetWindow(this) is OutputWindow wall)
            wall.EnsureWall();
        var (originX, originY, scaleX, scaleY) = OutputViewport(show);
        var live = PlaybackClock.VisibleMedia(show);
        var liveIds = live.Select(e => e.Cue.Id).ToHashSet();
        var gpuDraws = new List<Watchout.Core.Gpu.GpuDraw>();
        var gpuOn = GpuEngine.Available || GpuEngine.TryStart();
        foreach (var stale in _layers.Keys.Where(id => !liveIds.Contains(id)).ToList())
            DropMedia(stale);

        var z = 0;
        foreach (var ev in live)
        {
            var asset = show.Assets.FirstOrDefault(a => a.Id == ev.Cue.AssetId);
            var rect = StageGeometry.CueRect(ev, asset);
            var mapped = Map(rect.X, rect.Y, rect.W, rect.H, originX, originY, scaleX, scaleY);
            // Output already owns the H.264 decoder. Leave Stage as a labeled
            // canvas so a second 4K DXVA cannot freeze the PC — but keep a
            // preview box so move/resize still has something to grab.
            if (gpuOn && asset is not null && GpuLayerMath.UsesGpu(asset)
                && !GpuLayerMath.StageYieldsFilePreview(Editing, session.StageYieldsFileDecoder, asset))
            {
                if (_layers.ContainsKey(ev.Cue.Id)) DropMedia(ev.Cue.Id);
                var tl = show.Timelines.FirstOrDefault(t => t.Cues.Any(c => c.Id == PlaybackClock.RootCueId(ev.Cue.Id)));
                gpuDraws.Add(GpuLayerMath.FromCue(
                    ev, asset, originX, originY, scaleX, PixelWidth(), PixelHeight(),
                    tl?.Playback ?? PlaybackState.Stop, tl?.Loop == true, scaleY));
                z++;
                continue;
            }
            if (!_layers.TryGetValue(ev.Cue.Id, out var el) || !LayerFits(el, asset) || ChromaChanged(el, ev.Cue) || _dead.Contains(ev.Cue.Id))
            {
                if (el is not null) DropMedia(ev.Cue.Id);
                el = BuildLayer(ev, asset, mapped, show);
                if (el is null) continue;
                _layers[ev.Cue.Id] = el;
                Children.Add(el);
            }
            var media = el is CueLookHost host ? host.Media : el;
            if (syncMedia && media is ProceduralLayer proc)
            {
                proc.Kind = asset?.Url ?? proc.Kind;
                proc.LocalTime = ev.LocalTime;
                proc.InvalidateVisual();
            }
            else if (media is CaptureLayer capture)
            {
                capture.DeviceId = LiveSources.CaptureDeviceId(asset);
                capture.NdiName = LiveSources.IsCapture(asset) ? null : LiveSources.NdiSourceName(asset);
            }
            else if (syncMedia && media is MediaElement video)
            {
                var tl = show.Timelines.FirstOrDefault(t => t.Cues.Any(c => c.Id == PlaybackClock.RootCueId(ev.Cue.Id)));
                var muted = !PlayAudio || ev.Volume <= 0;
                var vol = Math.Clamp(ev.Volume / 100.0, 0, 1);
                if (video.IsMuted != muted) video.IsMuted = muted;
                if (Math.Abs(video.Volume - vol) > 0.01) video.Volume = vol;
                var speed = CueLooks.SpeedRatio(ev.Speed);
                if (Math.Abs(video.SpeedRatio - speed) > 0.01)
                {
                    try { video.SpeedRatio = speed; } catch { /* decoder not ready */ }
                }
                SyncVideo(ev.Cue.Id, video, ev, tl?.Playback ?? PlaybackState.Stop, tl?.Loop == true, asset?.Duration ?? 0);
            }

            ApplyLooks(el, ev);
            PlaceLayer(el, mapped);
            var zIndex = 100 + z;
            if (GetZIndex(el) != zIndex) SetZIndex(el, zIndex);
            z++;
        }
        var videoZs = VideoZs().ToArray();
        var captures = _layers.Values.OfType<CaptureLayer>().OrderBy(feed => GetZIndex(feed)).ToList();
        foreach (var el in captures)
        {
            var inFront = LiveComposite.ScreenOverlayOnOutput(GetZIndex(el), videoZs);
            el.SetOutputOverlay(inFront);
            if (!inFront) el.SendBehind();
        }
        var stack = captures.Where(el => LiveComposite.ScreenOverlayOnOutput(GetZIndex(el), videoZs)).ToList();
        var restacking = false;
        void Restack()
        {
            if (restacking) return;
            restacking = true;
            try
            {
                foreach (var el in stack)
                    el.RaiseOverlay();
            }
            finally
            {
                restacking = false;
            }
        }
        foreach (var el in captures)
            el.OnRestack(null);
        foreach (var el in stack)
            el.OnRestack(stack.Count > 1 ? Restack : null);
        Restack();
        var playing = show.Timelines.Any(t => t.Enabled && t.Playback == PlaybackState.Play);
        PresentGpu(gpuDraws, gpuOn, GpuSourceLifetime.KeepLastFrame(playing, gpuDraws.Count));
    }

    void PresentGpu(List<Watchout.Core.Gpu.GpuDraw> draws, bool gpuOn, bool keepLastFrame)
    {
        if (!gpuOn || (Editing && draws.Count == 0))
        {
            if (_gpu is not null)
            {
                Children.Remove(_gpu);
                _gpu = null;
            }
            return;
        }
        if (_gpu is null)
        {
            _gpu = new GpuPresentLayer(output: !Editing);
            Children.Insert(0, _gpu);
            SetZIndex(_gpu, 1);
        }
        if (LayoutDiffers(_gpu.Width, ActualWidth) || _gpu.Width < 8)
            _gpu.Width = Math.Max(2, ActualWidth > 8 ? ActualWidth : ViewDisplay?.Width ?? 2);
        if (LayoutDiffers(_gpu.Height, ActualHeight) || _gpu.Height < 8)
            _gpu.Height = Math.Max(2, ActualHeight > 8 ? ActualHeight : ViewDisplay?.Height ?? 2);
        SetLeft(_gpu, 0);
        SetTop(_gpu, 0);
        if (!Editing) _gpu.UpdateLayout();
        _gpu.Present(draws, ViewDisplay, PlayAudio, keepLastFrame);
    }

    void DropAllMedia()
    {
        foreach (var id in _videos.Keys.ToList())
            DropMedia(id);
    }

    void DropMedia(string id)
    {
        if (_layers.Remove(id, out var el))
            Children.Remove(el);
        _playing.Remove(id);
        _primed.Remove(id);
        _lastSeek.Remove(id);
        _lastPos.Remove(id);
        _lastAdvance.Remove(id);
        _dead.Remove(id);
        _ended.Remove(id);
        if (_videos.Remove(id, out var dead))
        {
            try { dead.Stop(); dead.Close(); } catch { /* ignore */ }
        }
    }

    IEnumerable<int> VideoZs()
    {
        foreach (var el in _layers.Values)
            if (el is MediaElement)
                yield return GetZIndex(el);
    }

    void DrawHandles(Rect mapped, int z)
    {
        foreach (var handle in new[] { "nw", "n", "ne", "e", "se", "s", "sw", "w" })
        {
            var (hx, hy) = HandlePoint(mapped, handle);
            var knob = new Rectangle
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(245, 166, 35)),
                IsHitTestVisible = false,
            };
            SetLeft(knob, hx - 4);
            SetTop(knob, hy - 4);
            Children.Add(knob);
            _chrome.Add(knob);
            SetZIndex(knob, z);
        }
    }

    FrameworkElement? BuildLayer(EvaluatedCue ev, Asset? asset, Rect mapped, Show show)
    {
        var inner = BuildMedia(ev, asset, mapped, show);
        if (inner is null) return null;
        // Media Foundation / DXVA and live NDI/capture are HWND interop. Keep them
        // direct Canvas children so ZIndex can stack NDI in front of playing H.264.
        if (inner is MediaElement or CaptureLayer) return inner;
        return WrapLooks(inner, ev);
    }

    FrameworkElement? BuildMedia(EvaluatedCue ev, Asset? asset, Rect mapped, Show show)
    {
        var native = LayerNativeSize(asset, mapped);
        if (asset is null) return Placeholder(native, ev.Cue.Name, ev.Cue.Color);
        if (LiveSources.IsCapture(asset))
            return new CaptureLayer { DeviceId = LiveSources.CaptureDeviceId(asset), Width = native.W, Height = native.H, IsHitTestVisible = false };
        if (LiveSources.NdiSourceName(asset) is { Length: > 0 } ndiName)
            return new CaptureLayer { NdiName = ndiName, Width = native.W, Height = native.H, IsHitTestVisible = false };
        if (asset.Url.StartsWith("procedural:", StringComparison.Ordinal))
            return new ProceduralLayer { Kind = asset.Url, LocalTime = ev.LocalTime, Width = native.W, Height = native.H, IsHitTestVisible = false };
        if (LiveSources.IsNdi(asset))
            return Placeholder(native, $"{asset.Name}\nNDI · no source name on this clip", asset.Color);
        if (LiveSources.IsSt2110(asset))
            return Placeholder(native, $"{asset.Name}\nST 2110 · SDP on this clip", asset.Color);
        if (asset.Kind is AssetKind.Image
            || asset.Url.StartsWith("watchout:", StringComparison.OrdinalIgnoreCase)
            || asset.Url.StartsWith("watchme:", StringComparison.OrdinalIgnoreCase)
            || asset.Url.StartsWith("data:", StringComparison.Ordinal))
        {
            var still = DemoArt.ForUrl(asset.Url, Math.Max(8, (int)asset.Width), Math.Max(8, (int)asset.Height)) ?? MediaLibrary.LoadStill(asset);
            if (still is null) return Placeholder(native, asset.Name, asset.Color);
            if (ev.Cue.ChromaKeyEnabled && still is BitmapSource bmp)
                still = MediaLibrary.ChromaKey(bmp, ev.Cue.ChromaKeyColor, ev.Cue.ChromaKeyTolerance) ?? still;
            return new Image { Source = still, Stretch = Stretch.Fill, Width = native.W, Height = native.H, IsHitTestVisible = false };
        }
        if (asset.Kind == AssetKind.Audio) return null;
        if (asset.Kind == AssetKind.Composition)
            return Placeholder(native, asset.Name, asset.Color);

        if (Editing && App.Session.StageYieldsFileDecoder)
            return new StagePreviewBox { Width = native.W, Height = native.H, CueName = asset.Name };

        var path = Codecs.PlaybackPath(asset);
        var file = Codecs.TryFileUrl(path) ?? (System.IO.File.Exists(path) ? path : null);
        if (file is null) return Placeholder(native, asset.Name, asset.Color);

        var video = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = Stretch.Fill,
            ScrubbingEnabled = false,
            Focusable = false,
            IsHitTestVisible = false,
            IsMuted = !PlayAudio || ev.Volume <= 0,
            Width = native.W,
            Height = native.H,
        };
        video.MediaFailed += (_, e) =>
        {
            App.Session.Log($"Media Foundation could not play {asset.Name} ({file}): {e.ErrorException.Message}", "error");
            _dead.Add(ev.Cue.Id);
            _playing.Remove(ev.Cue.Id);
        };
        video.MediaEnded += (_, _) =>
        {
            _playing.Remove(ev.Cue.Id);
            _primed.Remove(ev.Cue.Id);
            _ended.Add(ev.Cue.Id);
            var tlNow = show.Timelines.FirstOrDefault(t => t.Cues.Any(c => c.Id == PlaybackClock.RootCueId(ev.Cue.Id)));
            if (tlNow?.Playback != PlaybackState.Play || tlNow.Loop != true) return;
            try
            {
                video.Position = TimeSpan.Zero;
                video.Play();
                _playing.Add(ev.Cue.Id);
                _ended.Remove(ev.Cue.Id);
                _lastSeek[ev.Cue.Id] = DateTime.UtcNow;
                _lastPos[ev.Cue.Id] = 0;
                _lastAdvance[ev.Cue.Id] = DateTime.UtcNow;
            }
            catch
            {
                _dead.Add(ev.Cue.Id);
            }
        };
        video.MediaOpened += (_, _) =>
        {
            App.Session.Log($"{asset.Name} · Media Foundation / DXVA opened {System.IO.Path.GetFileName(file)}");
            var tlNow = show.Timelines.FirstOrDefault(t => t.Cues.Any(c => c.Id == PlaybackClock.RootCueId(ev.Cue.Id)));
            SyncVideo(ev.Cue.Id, video, ev, tlNow?.Playback ?? PlaybackState.Stop, tlNow?.Loop == true, asset.Duration);
        };
        try { video.Source = MediaLibrary.LocalUri(file); }
        catch { return Placeholder(native, asset.Name, asset.Color); }
        _videos[ev.Cue.Id] = video;
        var tl = show.Timelines.FirstOrDefault(t => t.Cues.Any(c => c.Id == PlaybackClock.RootCueId(ev.Cue.Id)));
        SyncVideo(ev.Cue.Id, video, ev, tl?.Playback ?? PlaybackState.Stop, tl?.Loop == true, asset.Duration);
        return video;
    }

    static (double W, double H) LayerNativeSize(Asset? asset, Rect mapped)
    {
        var w = asset?.Width > 0 ? asset.Width : Math.Max(1, mapped.Width);
        var h = asset?.Height > 0 ? asset.Height : Math.Max(1, mapped.Height);
        return (w, h);
    }

    bool LayerFits(FrameworkElement el, Asset? asset)
    {
        if (el is CueLookHost host) return LayerFits(host.Media, asset);
        if (LiveSources.IsCapture(asset) || LiveSources.NdiSourceName(asset) is { Length: > 0 }) return el is CaptureLayer;
        if (asset?.Url.StartsWith("procedural:", StringComparison.Ordinal) == true) return el is ProceduralLayer;
        if (LiveSources.IsNdi(asset)) return el is not CaptureLayer && el is not MediaElement;
        if (Editing && App.Session.StageYieldsFileDecoder)
            return el is StagePreviewBox;
        if (asset is { Kind: AssetKind.Video })
            return el is MediaElement;
        return el is not CaptureLayer;
    }

    void SyncVideo(string cueId, MediaElement video, EvaluatedCue ev, PlaybackState playback, bool loop, double fileMs)
    {
        if (video.NaturalDuration.HasTimeSpan)
            fileMs = video.NaturalDuration.TimeSpan.TotalMilliseconds;
        var local = loop && fileMs > 1
            ? PlaybackClock.LoopFileTime(ev.LocalTime, fileMs)
            : ev.LocalTime;
        var target = TimeSpan.FromMilliseconds(Math.Max(0, local));
        try
        {
            var now = DateTime.UtcNow;
            var pos = video.Position.TotalMilliseconds;
            var drift = Math.Abs((video.Position - target).TotalMilliseconds);
            var sinceSeek = _lastSeek.TryGetValue(cueId, out var at)
                ? (now - at).TotalMilliseconds
                : double.PositiveInfinity;
            if (playback == PlaybackState.Play)
            {
                _lastSeek.TryAdd(cueId, now);
                if (!_lastAdvance.ContainsKey(cueId) || Math.Abs(pos - _lastPos.GetValueOrDefault(cueId)) >= 5)
                    _lastAdvance[cueId] = now;
                _lastPos[cueId] = pos;
                sinceSeek = (now - _lastSeek[cueId]).TotalMilliseconds;
                var sinceAdvance = (now - _lastAdvance[cueId]).TotalMilliseconds;
                if (VideoSync.DecoderStalled(true, sinceAdvance, sinceSeek))
                {
                    App.Session.Log($"{ev.Cue.Name} · DXVA stalled — restarting the decoder", "warn");
                    _dead.Add(cueId);
                    _playing.Remove(cueId);
                    return;
                }
                if (_playing.Add(cueId))
                {
                    if (_ended.Remove(cueId)
                        || VideoSync.SeekOnPlayStart(drift)
                        || VideoSync.RestartAfterWrap(video.Position.TotalMilliseconds, target.TotalMilliseconds))
                    {
                        video.Position = target;
                        _lastSeek[cueId] = now;
                        _lastAdvance[cueId] = now;
                    }
                    video.Play();
                    _primed.Add(cueId);
                    _lastAdvance.TryAdd(cueId, now);
                    return;
                }
                if (VideoSync.RestartAfterWrap(video.Position.TotalMilliseconds, target.TotalMilliseconds)
                    || VideoSync.ReseekWhilePlaying(drift, sinceSeek))
                {
                    video.Position = target;
                    _lastSeek[cueId] = now;
                    _lastAdvance[cueId] = now;
                    video.Play();
                }
                return;
            }
            if (_playing.Remove(cueId))
            {
                video.Pause();
                _lastSeek.Remove(cueId);
                _lastPos.Remove(cueId);
                _lastAdvance.Remove(cueId);
                _ended.Remove(cueId);
            }
            if (_primed.Add(cueId))
            {
                video.Play();
                video.Pause();
            }
            if (VideoSync.SeekWhileIdle(drift))
                video.Position = target;
        }
        catch { /* decoder not ready */ }
    }

    static FrameworkElement Placeholder((double W, double H) native, string name, string color)
    {
        var grid = new Grid { Width = Math.Max(1, native.W), Height = Math.Max(1, native.H), Background = BrushFrom(color), IsHitTestVisible = false };
        grid.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 16,
        });
        return grid;
    }

    (double OriginX, double OriginY, double ScaleX, double ScaleY) OutputViewport(Show show)
    {
        if (!Editing && ViewDisplay is { } display)
        {
            var destW = PixelWidth();
            var destH = PixelHeight();
            return Watchout.Core.Gpu.OutputViewMath.OutputViewport(
                display.X, display.Y, display.Width, display.Height, destW, destH);
        }
        var v = Viewport(show);
        return (v.OriginX, v.OriginY, v.Scale, v.Scale);
    }

    int PixelWidth()
    {
        if (Window.GetWindow(this) is OutputWindow win) return win.PixelWidth;
        return Math.Max(2, (int)Math.Round(ActualWidth > 8 ? ActualWidth : ViewDisplay?.Width ?? 2));
    }

    int PixelHeight()
    {
        if (Window.GetWindow(this) is OutputWindow win) return win.PixelHeight;
        return Math.Max(2, (int)Math.Round(ActualHeight > 8 ? ActualHeight : ViewDisplay?.Height ?? 2));
    }

    (double OriginX, double OriginY, double Scale) Viewport(Show show)
    {
        if (ViewDisplay is { } d)
        {
            var vw = ActualWidth;
            var vh = ActualHeight;
            var scale = vw < 2 || vh < 2
                ? 1
                : Math.Min(vw / Math.Max(1, d.Width), vh / Math.Max(1, d.Height));
            if (Math.Abs(scale - 1) < 0.03) scale = 1;
            return (d.X, d.Y, scale > 0 ? scale : 1);
        }
        var cam = App.Session.Camera;
        var zoom = cam.Zoom <= 0 ? 0.18 : cam.Zoom;
        return (cam.X - ActualWidth / 2 / zoom, cam.Y - ActualHeight / 2 / zoom, zoom);
    }

    static Rect Map(double x, double y, double w, double h, double ox, double oy, double scaleX, double scaleY = 0)
    {
        var sy = scaleY == 0 ? scaleX : scaleY;
        return new((x - ox) * scaleX, (y - oy) * sy, w * scaleX, h * sy);
    }

    static Brush BrushFrom(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!); }
        catch { return new SolidColorBrush(Color.FromRgb(59, 130, 196)); }
    }

    static (double X, double Y) HandlePoint(Rect r, string handle) => handle switch
    {
        "nw" => (r.X, r.Y),
        "n" => (r.X + r.Width / 2, r.Y),
        "ne" => (r.X + r.Width, r.Y),
        "e" => (r.X + r.Width, r.Y + r.Height / 2),
        "se" => (r.X + r.Width, r.Y + r.Height),
        "s" => (r.X + r.Width / 2, r.Y + r.Height),
        "sw" => (r.X, r.Y + r.Height),
        _ => (r.X, r.Y + r.Height / 2),
    };

    void OnDragOver(object sender, DragEventArgs e)
    {
        if (!Editing || !StudioDrag.IsMediaDrag(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        var show = App.Session.Show;
        if (show is null) return;
        var stage = ScreenToStage(e.GetPosition(this), show);
        var hit = StageGeometry.DropTarget(show.Displays, stage, App.Session.Snap, DropSnapDistance(show))?.Id;
        if (hit == _hoverDisplayId) return;
        _hoverDisplayId = hit;
        Refresh();
    }

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (!Editing || _dropping) return;
        _dropping = true;
        try
        {
            e.Handled = true;
            _hoverDisplayId = null;
            var show = App.Session.Show;
            if (show is null) return;
            var stage = ScreenToStage(e.GetPosition(this), show);
            var display = StageGeometry.DropTarget(show.Displays, stage, App.Session.Snap, DropSnapDistance(show));
            if (StudioDrag.TryAssetId(e.Data, out var assetId))
            {
                App.Session.DropAssetOnStage(assetId, display?.Id, stage.X, stage.Y);
                StudioDrag.AssetId = null;
                return;
            }
            var files = StudioDrag.Files(e.Data);
            if (files.Length == 0) return;
            var ids = await MediaLibrary.ImportFilesAsync(files, App.Session);
            foreach (var id in ids)
                App.Session.DropAssetOnStage(id, display?.Id, stage.X, stage.Y);
        }
        finally
        {
            _dropping = false;
            Refresh();
        }
    }

    double DropSnapDistance(Show show)
    {
        var magnet = StageGeometry.SnapThreshold(Viewport(show).Scale) * 6;
        var smallest = show.Displays.Where(d => d.Enabled).Select(d => Math.Min(d.Width, d.Height)).DefaultIfEmpty(1920).Min();
        return Math.Max(magnet, smallest * 0.35);
    }

    static bool LayoutDiffers(double current, double next) =>
        double.IsNaN(current) || double.IsNaN(next) || Math.Abs(current - next) > 0.5;

    void PlaceLayer(FrameworkElement el, Rect mapped)
    {
        var w = Math.Max(1, mapped.Width);
        var h = Math.Max(1, mapped.Height);
        if (el is MediaElement && VideoSync.HoldOutputVideoLayout(!Editing, App.Session.StageLayoutBusy))
            return;
        if (el is MediaElement)
        {
            if (VideoSync.VideoLayoutChanged(GetLeft(el), mapped.X)) SetLeft(el, mapped.X);
            if (VideoSync.VideoLayoutChanged(GetTop(el), mapped.Y)) SetTop(el, mapped.Y);
            if (VideoSync.VideoLayoutChanged(el.Width, w)) el.Width = w;
            if (VideoSync.VideoLayoutChanged(el.Height, h)) el.Height = h;
            return;
        }
        if (LayoutDiffers(GetLeft(el), mapped.X)) SetLeft(el, mapped.X);
        if (LayoutDiffers(GetTop(el), mapped.Y)) SetTop(el, mapped.Y);
        if (el is CaptureLayer live && ViewDisplay is not null)
        {
            live.SetOverlayDipSize(w, h);
            if (LayoutDiffers(el.Width, 2)) el.Width = 2;
            if (LayoutDiffers(el.Height, 2)) el.Height = 2;
            return;
        }
        if (el is not MediaElement and not CaptureLayer)
            el.RenderTransform = Transform.Identity;
        if (LayoutDiffers(el.Width, w)) el.Width = w;
        if (LayoutDiffers(el.Height, h)) el.Height = h;
        if (el is CueLookHost host)
        {
            if (LayoutDiffers(host.Media.Width, w)) host.Media.Width = w;
            if (LayoutDiffers(host.Media.Height, h)) host.Media.Height = h;
        }
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Editing) return;
        ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.12 : 0.9);
        e.Handled = true;
    }

    public void SyncViewSize()
    {
        if (!Editing) return;
        App.Session.ReportStageView(ActualWidth, ActualHeight);
    }

    public void FrameWall()
    {
        SyncViewSize();
        App.Session.FrameDisplays();
    }

    public void FrameSelectedDisplay()
    {
        SyncViewSize();
        App.Session.FrameDisplay();
    }

    public void ZoomBy(double factor) =>
        ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), factor);

    void ZoomAt(Point screen, double factor)
    {
        var show = App.Session.Show;
        if (show is null) return;
        SyncViewSize();
        var cam = App.Session.Camera;
        var oldZ = cam.Zoom <= 0 ? 0.18 : cam.Zoom;
        var next = Math.Clamp(oldZ * factor, 0.03, 2);
        var stage = ScreenToStage(screen, show);
        var x = stage.X - (screen.X - ActualWidth / 2) / next;
        var y = stage.Y - (screen.Y - ActualHeight / 2) / next;
        App.Session.SetCamera(x, y, next);
    }

    void OnAnyDown(object sender, MouseButtonEventArgs e)
    {
        if (!Editing || e.ChangedButton != MouseButton.Middle) return;
        _panning = true;
        _panArmed = false;
        _panStart = e.GetPosition(this);
        _panCam = App.Session.Camera;
        CaptureMouse();
        e.Handled = true;
    }

    void OnAnyUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        _panning = false;
        ReleaseMouseCapture();
    }

    void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (_panArmed && !_panning) App.Session.ClearSelection();
        _dragCueId = null;
        _dragDisplayId = null;
        _resizeHandle = null;
        _resizeDisplay = false;
        _dragArmed = false;
        _panArmed = false;
        _panning = false;
        App.Session.SetStageLayoutBusy(false);
        ReleaseMouseCapture();
    }

    void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (App.Session.PickingChroma)
        {
            PickChromaAt(e.GetPosition(this));
            e.Handled = true;
            return;
        }
        if (!Editing) return;
        Focus();
        var show = App.Session.Show;
        if (show is null) return;
        _panning = false;
        _panArmed = false;
        var stage = ScreenToStage(e.GetPosition(this), show);
        if (e.ClickCount >= 2)
        {
            var canvas = HitAt(show, stage, preferDisplay: true);
            if (canvas.Kind is StageHitKind.Display or StageHitKind.DisplayHandle)
            {
                App.Session.SetStageEditMode(StageEditMode.Displays);
                App.Session.Select(SelectionKind.Display, canvas.Id!);
                return;
            }
        }
        var hit = HitAt(show, stage, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
        _dragArmed = false;
        switch (hit.Kind)
        {
            case StageHitKind.DisplayHandle:
                App.Session.Select(SelectionKind.Display, hit.Id!);
                _resizeHandle = hit.Handle;
                _resizeDisplay = true;
                _resizeStart = StageGeometry.DisplayRect(show.Displays.First(d => d.Id == hit.Id));
                _dragStart = e.GetPosition(this);
                App.Session.SetStageLayoutBusy(true);
                CaptureMouse();
                e.Handled = true;
                return;
            case StageHitKind.CueHandle:
                App.Session.Select(SelectionKind.Cue, hit.Id!);
                if (App.Session.CueLayerLocked(hit.Id!)) return;
                _resizeHandle = hit.Handle;
                _resizeDisplay = false;
                var cueForResize = show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => c.Id == hit.Id);
                var assetForResize = show.Assets.FirstOrDefault(a => a.Id == cueForResize?.AssetId);
                _resizeStart = cueForResize is null ? default : StageGeometry.CueRect(cueForResize, assetForResize);
                _dragStart = e.GetPosition(this);
                App.Session.SetStageLayoutBusy(true);
                CaptureMouse();
                e.Handled = true;
                return;
            case StageHitKind.Cue:
                App.Session.Select(SelectionKind.Cue, hit.Id!);
                if (App.Session.CueLayerLocked(hit.Id!)) return;
                var cue = show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => c.Id == hit.Id);
                _dragCueId = hit.Id;
                _dragStart = e.GetPosition(this);
                _dragCueOrigin = new Point(cue?.Position.X ?? 0, cue?.Position.Y ?? 0);
                App.Session.SetStageLayoutBusy(true);
                CaptureMouse();
                e.Handled = true;
                return;
            case StageHitKind.Display:
                App.Session.Select(SelectionKind.Display, hit.Id!);
                var display = show.Displays.First(d => d.Id == hit.Id);
                _dragDisplayId = hit.Id;
                _dragStart = e.GetPosition(this);
                _dragDisplayOrigin = new Point(display.X, display.Y);
                _dragArmed = true;
                App.Session.SetStageLayoutBusy(true);
                CaptureMouse();
                e.Handled = true;
                return;
            default:
                _panArmed = true;
                _panning = false;
                _panStart = e.GetPosition(this);
                _panCam = App.Session.Camera;
                CaptureMouse();
                break;
        }
    }

    StageHit HitAt(Show show, (double X, double Y) stage, bool preferDisplay)
    {
        var live = PlaybackClock.VisibleMedia(show);
        var rects = StageGeometry.EditCueRects(live, show.Assets, show.Timelines.SelectMany(t => t.Cues), App.Session.Selection);
        return StageGeometry.HitEditTarget(App.Session.StageEditMode, show.Displays, rects, App.Session.Selection, stage, Viewport(show).Scale, preferDisplay);
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        var show = App.Session.Show;
        if (show is null) return;
        var (_, _, scale) = Viewport(show);
        if ((_panning || _panArmed) && Editing)
        {
            var p = e.GetPosition(this);
            if (_panArmed && !_panning)
            {
                if (Math.Abs(p.X - _panStart.X) < 4 && Math.Abs(p.Y - _panStart.Y) < 4) return;
                _panning = true;
            }
            var zoom = Math.Max(0.03, _panCam.Zoom);
            App.Session.SetCamera(_panCam.X - (p.X - _panStart.X) / zoom, _panCam.Y - (p.Y - _panStart.Y) / zoom, zoom);
            Cursor = Cursors.SizeAll;
            return;
        }
        if (_resizeHandle is not null && _dragStart != default)
        {
            var now = e.GetPosition(this);
            var dx = (now.X - _dragStart.X) / scale;
            var dy = (now.Y - _dragStart.Y) / scale;
            var next = StageGeometry.ResizeRect(_resizeStart, _resizeHandle, dx, dy, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            if (App.Session.Snap)
            {
                var guides = _resizeDisplay
                    ? StageGeometry.DisplayMoveGuides(show.Displays, App.Session.Selection.Ids.FirstOrDefault())
                    : StageGeometry.DisplayGuides(show.Displays);
                next = StageGeometry.SnapResizeRect(next, _resizeHandle, guides.X, guides.Y, StageGeometry.EditSnapThreshold(scale));
            }
            if (_resizeDisplay && App.Session.Selection.Ids.FirstOrDefault() is { } displayId)
            {
                App.Session.LiveUpdateDisplay(displayId, d =>
                {
                    d.X = Math.Round(next.X);
                    d.Y = Math.Round(next.Y);
                    d.Width = Math.Round(next.W);
                    d.Height = Math.Round(next.H);
                });
            }
            else
            {
                var cue = show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => App.Session.Selection.Ids.Contains(c.Id));
                var asset = cue?.AssetId is { } aid ? show.Assets.FirstOrDefault(a => a.Id == aid) : null;
                if (cue is not null && asset is not null)
                {
                    var fit = StageGeometry.RectToCueTransform(next, asset);
                    App.Session.LiveUpdateCue(cue.Id, c =>
                    {
                        c.Position = fit.Position;
                        c.Scale = fit.Scale;
                    });
                    PlaceDraggedCue(show, cue.Id);
                }
            }
            return;
        }
        if (_dragDisplayId is not null)
        {
            var now = e.GetPosition(this);
            if (_dragArmed && Math.Abs(now.X - _dragStart.X) < 4 && Math.Abs(now.Y - _dragStart.Y) < 4) return;
            _dragArmed = false;
            var x = _dragDisplayOrigin.X + (now.X - _dragStart.X) / scale;
            var y = _dragDisplayOrigin.Y + (now.Y - _dragStart.Y) / scale;
            if (App.Session.Snap)
            {
                var display = show.Displays.FirstOrDefault(d => d.Id == _dragDisplayId);
                var guides = StageGeometry.DisplayMoveGuides(show.Displays, _dragDisplayId);
                var snapped = StageGeometry.SnapRect(new StageRect(x, y, display?.Width ?? 1920, display?.Height ?? 1080), guides.X, guides.Y, StageGeometry.EditSnapThreshold(scale));
                x = snapped.X;
                y = snapped.Y;
            }
            App.Session.LiveUpdateDisplay(_dragDisplayId, d =>
            {
                d.X = Math.Round(x);
                d.Y = Math.Round(y);
            });
            return;
        }
        if (_dragCueId is null)
        {
            if (App.Session.PickingChroma)
            {
                Cursor = Cursors.Cross;
                return;
            }
            if (Editing && e.LeftButton != MouseButtonState.Pressed)
            {
                var stage = ScreenToStage(e.GetPosition(this), show);
                var hit = HitAt(show, stage, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
                Cursor = hit.Kind switch
                {
                    StageHitKind.DisplayHandle or StageHitKind.CueHandle => HandleCursor(hit.Handle),
                    StageHitKind.Cue => Cursors.SizeAll,
                    StageHitKind.Display => Cursors.Hand,
                    _ => Cursors.SizeAll,
                };
            }
            return;
        }
        var pos = e.GetPosition(this);
        var cx = _dragCueOrigin.X + (pos.X - _dragStart.X) / scale;
        var cy = _dragCueOrigin.Y + (pos.Y - _dragStart.Y) / scale;
        if (App.Session.Snap)
        {
            var guides = StageGeometry.DisplayGuides(show.Displays);
            var cue = show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => c.Id == _dragCueId);
            var asset = cue?.AssetId is { } aid ? show.Assets.FirstOrDefault(a => a.Id == aid) : null;
            if (cue is not null)
            {
                var rect = StageGeometry.CueRect(cue, asset);
                var snapped = StageGeometry.SnapRect(new StageRect(cx, cy, rect.W, rect.H), guides.X, guides.Y, StageGeometry.EditSnapThreshold(scale));
                cx = snapped.X;
                cy = snapped.Y;
            }
        }
        App.Session.LiveUpdateCue(_dragCueId, c => c.Position = new Vec3 { X = Math.Round(cx), Y = Math.Round(cy), Z = c.Position.Z });
        PlaceDraggedCue(show, _dragCueId);
    }

    void PlaceDraggedCue(Show show, string cueId)
    {
        if (!_layers.TryGetValue(cueId, out var el)) return;
        var cue = show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => c.Id == cueId);
        if (cue is null) return;
        var asset = cue.AssetId is { } aid ? show.Assets.FirstOrDefault(a => a.Id == aid) : null;
        var rect = StageGeometry.CueRect(cue, asset);
        var (ox, oy, scale) = Viewport(show);
        PlaceLayer(el, Map(rect.X, rect.Y, rect.W, rect.H, ox, oy, scale));
    }

    static Cursor HandleCursor(string? handle) => handle switch
    {
        "n" or "s" => Cursors.SizeNS,
        "e" or "w" => Cursors.SizeWE,
        "ne" or "sw" => Cursors.SizeNESW,
        "nw" or "se" => Cursors.SizeNWSE,
        _ => Cursors.SizeAll,
    };

    static string ChromaStamp(Cue cue) =>
        cue.ChromaKeyEnabled ? $"{cue.ChromaKeyColor}|{cue.ChromaKeyTolerance:0}" : "";

    static bool ChromaChanged(FrameworkElement el, Cue cue) =>
        el is CueLookHost host && host.ChromaStamp != ChromaStamp(cue);

    static FrameworkElement WrapLooks(FrameworkElement inner, EvaluatedCue ev)
    {
        inner.IsHitTestVisible = false;
        var host = new CueLookHost(inner, ChromaStamp(ev.Cue))
        {
            Width = inner.Width,
            Height = inner.Height,
            IsHitTestVisible = false,
        };
        return host;
    }

    static void ApplyLooks(FrameworkElement el, EvaluatedCue ev)
    {
        var opacity = Math.Clamp(ev.Opacity / 100.0, 0, 1);
        if (Math.Abs(el.Opacity - opacity) > 0.005) el.Opacity = opacity;
        var video = el is CueLookHost hostMedia ? hostMedia.Media as MediaElement : el as MediaElement;
        if (video is not null || el is CaptureLayer)
        {
            el.Clip = null;
            el.OpacityMask = null;
            return;
        }
        var w = Math.Max(1, el.Width > 1 && !double.IsNaN(el.Width) ? el.Width : 1920);
        var h = Math.Max(1, el.Height > 1 && !double.IsNaN(el.Height) ? el.Height : 1080);
        var crop = ev.Crop;
        var cropped = crop.Top > 0.05 || crop.Bottom > 0.05 || crop.Left > 0.05 || crop.Right > 0.05;
        if (cropped)
        {
            var box = CueLooks.CropBox(w, h, crop);
            el.Clip = new RectangleGeometry(new Rect(box.X, box.Y, box.W, box.H));
        }
        else el.Clip = null;

        if (ev.Wipe < 99.4)
        {
            var g = CueLooks.WipeGradient(ev.Wipe, ev.WipeAngle, ev.WipeFeather);
            el.OpacityMask = new LinearGradientBrush(
                [
                    new GradientStop(Colors.White, 0),
                    new GradientStop(Colors.White, g.Soft0),
                    new GradientStop(Colors.Transparent, g.Soft1),
                    new GradientStop(Colors.Transparent, 1),
                ],
                new Point(g.X1, g.Y1),
                new Point(g.X2, g.Y2));
        }
        else el.OpacityMask = null;

        if (el is CueLookHost host) host.PaintOverlays(ev);
    }

    void ApplyOutputMask()
    {
        if (ViewDisplay is not { MaskEnabled: true } display || string.IsNullOrEmpty(display.MaskUrl))
        {
            OpacityMask = null;
            return;
        }
        var file = Codecs.TryFileUrl(display.MaskUrl) ?? display.MaskUrl;
        if (!File.Exists(file))
        {
            OpacityMask = null;
            return;
        }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = MediaLibrary.LocalUri(file);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            OpacityMask = new ImageBrush(bmp)
            {
                Stretch = Stretch.Fill,
                Opacity = display.MaskInvert ? 1 : 1,
            };
        }
        catch
        {
            OpacityMask = null;
        }
    }

    void PickChromaAt(Point p)
    {
        try
        {
            var w = Math.Max(1, (int)ActualWidth);
            var h = Math.Max(1, (int)ActualHeight);
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(this);
            var x = Math.Clamp((int)p.X, 0, w - 1);
            var y = Math.Clamp((int)p.Y, 0, h - 1);
            var px = new byte[4];
            bmp.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
            App.Session.ApplyPickedChroma(CueLooks.ColorToHex(px[2], px[1], px[0]));
        }
        catch
        {
            App.Session.CancelPickChroma();
        }
    }

    (double X, double Y) ScreenToStage(Point p, Show show)
    {
        var (ox, oy, scale) = Viewport(show);
        return (ox + p.X / scale, oy + p.Y / scale);
    }
}

sealed class StagePreviewBox : Border
{
    readonly TextBlock _label = new()
    {
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(245, 166, 35)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        IsHitTestVisible = false,
    };

    public string CueName { get => _label.Text; set => _label.Text = value; }

    public StagePreviewBox()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        Background = new SolidColorBrush(Color.FromArgb(50, 245, 166, 35));
        BorderBrush = new SolidColorBrush(Color.FromRgb(245, 166, 35));
        BorderThickness = new Thickness(1);
        Child = _label;
    }
}

sealed class CueLookHost : Grid
{
    public FrameworkElement Media { get; }
    public string ChromaStamp { get; }
    readonly Border _temperature = new() { IsHitTestVisible = false };
    readonly Border _exposure = new() { IsHitTestVisible = false };

    public CueLookHost(FrameworkElement media, string chromaStamp)
    {
        Media = media;
        ChromaStamp = chromaStamp;
        Children.Add(media);
        Children.Add(_temperature);
        Children.Add(_exposure);
    }

    public void PaintOverlays(EvaluatedCue ev)
    {
        Paint(_temperature, CueLooks.TemperatureOverlay(ev.Temperature));
        Paint(_exposure, CueLooks.ExposureOverlay(ev.Exposure));
    }

    static void Paint(Border border, (double R, double G, double B, double A) tone)
    {
        if (tone.A < 0.01)
        {
            border.Background = null;
            return;
        }
        border.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp(tone.A * 255, 0, 255),
            (byte)Math.Clamp(tone.R * 255, 0, 255),
            (byte)Math.Clamp(tone.G * 255, 0, 255),
            (byte)Math.Clamp(tone.B * 255, 0, 255)));
    }
}
