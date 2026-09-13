using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Playback;
using Watchout.Core.Stage;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

public sealed class StageSurface : Canvas
{
    readonly Dictionary<string, FrameworkElement> _layers = [];
    readonly Dictionary<string, MediaElement> _videos = [];
    readonly List<UIElement> _chrome = [];
    Display? _viewDisplay;
    Point _dragStart;
    string? _dragCueId;
    Point _dragCueOrigin;
    bool _panning;
    Point _panStart;
    (double X, double Y, double Zoom) _panCam;

    public bool Editing { get; set; }
    public Display? ViewDisplay { get => _viewDisplay; set => _viewDisplay = value; }
    public bool PlayAudio { get; set; }

    public StageSurface()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Focusable = true;
        Background = new SolidColorBrush(Color.FromRgb(17, 17, 17));
        App.Session.Changed += () => Dispatcher.BeginInvoke(Refresh, DispatcherPriority.Render);
        SizeChanged += (_, _) => Refresh();
        MouseWheel += OnWheel;
        MouseLeftButtonDown += OnLeftDown;
        MouseLeftButtonUp += (_, _) => { _dragCueId = null; ReleaseMouseCapture(); };
        MouseMove += OnMove;
        MouseRightButtonDown += (_, e) =>
        {
            _panning = true;
            _panStart = e.GetPosition(this);
            _panCam = App.Session.Camera;
            CaptureMouse();
        };
        MouseRightButtonUp += (_, _) => { _panning = false; ReleaseMouseCapture(); };
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) =>
        {
            foreach (var v in _videos.Values) { try { v.Stop(); v.Close(); } catch { /* ignore */ } }
            _videos.Clear();
        };
    }

    public void Refresh()
    {
        var session = App.Session;
        var show = session.Show;
        if (show is null)
        {
            Children.Clear();
            _layers.Clear();
            return;
        }

        var (originX, originY, scale) = Viewport(show);
        foreach (var chrome in _chrome) Children.Remove(chrome);
        _chrome.Clear();

        if (Editing)
        {
            foreach (var display in show.Displays.Where(d => d.Enabled))
            {
                var r = Map(display.X, display.Y, display.Width, display.Height, originX, originY, scale);
                var selected = session.Selection.Kind == SelectionKind.Display && session.Selection.Ids.Contains(display.Id);
                var border = new Border
                {
                    Width = Math.Max(2, r.Width),
                    Height = Math.Max(2, r.Height),
                    BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(245, 166, 35) : Color.FromRgb(80, 80, 80)),
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    Background = new SolidColorBrush(Color.FromArgb(28, 48, 48, 48)),
                    IsHitTestVisible = false,
                };
                SetLeft(border, r.X);
                SetTop(border, r.Y);
                SetZIndex(border, 0);
                Children.Add(border);
                _chrome.Add(border);
                var label = new TextBlock
                {
                    Text = $"{display.Name}  {display.Width:0}×{display.Height:0}",
                    Foreground = new SolidColorBrush(Color.FromRgb(245, 166, 35)),
                    FontSize = 11,
                    IsHitTestVisible = false,
                };
                SetLeft(label, r.X + 6);
                SetTop(label, r.Y + 4);
                Children.Add(label);
                _chrome.Add(label);
            }
        }

        var live = PlaybackClock.VisibleMedia(show);
        var liveIds = live.Select(e => e.Cue.Id).ToHashSet();
        foreach (var stale in _layers.Keys.Where(id => !liveIds.Contains(id)).ToList())
        {
            Children.Remove(_layers[stale]);
            _layers.Remove(stale);
            if (_videos.Remove(stale, out var dead))
            {
                try { dead.Stop(); dead.Close(); } catch { /* ignore */ }
            }
        }

        foreach (var ev in live)
        {
            var asset = show.Assets.FirstOrDefault(a => a.Id == ev.Cue.AssetId);
            var rect = StageGeometry.CueRect(ev, asset);
            var mapped = Map(rect.X, rect.Y, rect.W, rect.H, originX, originY, scale);
            if (!_layers.TryGetValue(ev.Cue.Id, out var el))
            {
                el = BuildLayer(ev, asset, mapped, show);
                if (el is null) continue;
                _layers[ev.Cue.Id] = el;
                Children.Add(el);
            }
            else if (el is ProceduralLayer proc)
            {
                proc.Kind = asset?.Url ?? proc.Kind;
                proc.LocalTime = ev.LocalTime;
                proc.InvalidateVisual();
            }
            else if (el is CaptureLayer capture)
                capture.DeviceId = LiveSources.CaptureDeviceId(asset);
            else if (el is MediaElement video)
            {
                var tl = show.Timelines.FirstOrDefault(t => t.Cues.Any(c => c.Id == ev.Cue.Id));
                video.IsMuted = !PlayAudio || ev.Volume <= 0;
                video.Volume = Math.Clamp(ev.Volume / 100.0, 0, 1);
                SyncVideo(video, ev, tl?.Playback ?? PlaybackState.Stop);
            }

            el.Opacity = Math.Clamp(ev.Opacity / 100.0, 0, 1);
            SetLeft(el, mapped.X);
            SetTop(el, mapped.Y);
            el.Width = Math.Max(1, mapped.Width);
            el.Height = Math.Max(1, mapped.Height);
            SetZIndex(el, 10);

            if (Editing && session.Selection.Kind == SelectionKind.Cue && session.Selection.Ids.Contains(ev.Cue.Id))
            {
                var outline = new Rectangle
                {
                    Width = el.Width,
                    Height = el.Height,
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
            }
        }
    }

    FrameworkElement? BuildLayer(EvaluatedCue ev, Asset? asset, Rect mapped, Show show)
    {
        if (asset is null) return Placeholder(mapped, ev.Cue.Name, ev.Cue.Color);
        if (LiveSources.IsCapture(asset))
            return new CaptureLayer { DeviceId = LiveSources.CaptureDeviceId(asset), Width = mapped.Width, Height = mapped.Height };
        if (asset.Url.StartsWith("procedural:", StringComparison.Ordinal))
            return new ProceduralLayer { Kind = asset.Url, LocalTime = ev.LocalTime, Width = mapped.Width, Height = mapped.Height };
        if (asset.Kind is AssetKind.Image or AssetKind.Ndi
            || asset.Url.StartsWith("watchout:", StringComparison.OrdinalIgnoreCase)
            || asset.Url.StartsWith("watchme:", StringComparison.OrdinalIgnoreCase)
            || asset.Url.StartsWith("data:", StringComparison.Ordinal))
        {
            var still = DemoArt.ForUrl(asset.Url, Math.Max(8, (int)asset.Width), Math.Max(8, (int)asset.Height)) ?? MediaLibrary.LoadStill(asset);
            if (still is null) return Placeholder(mapped, asset.Name, asset.Color);
            return new Image { Source = still, Stretch = Stretch.Fill, Width = mapped.Width, Height = mapped.Height };
        }
        if (asset.Kind == AssetKind.Audio) return null;

        var path = Codecs.PlaybackPath(asset);
        var file = Codecs.TryFileUrl(path) ?? (System.IO.File.Exists(path) ? path : null);
        if (file is null) return Placeholder(mapped, asset.Name, asset.Color);

        var video = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = Stretch.Fill,
            ScrubbingEnabled = true,
            IsMuted = !PlayAudio || ev.Volume <= 0,
            Width = mapped.Width,
            Height = mapped.Height,
        };
        video.MediaFailed += (_, e) => App.Session.Log($"Media Foundation could not play {asset.Name}: {e.ErrorException.Message}", "error");
        video.MediaOpened += (_, _) => App.Session.Log($"{asset.Name} · Media Foundation / DXVA opened {System.IO.Path.GetFileName(file)}");
        try { video.Source = new Uri(file); }
        catch { return Placeholder(mapped, asset.Name, asset.Color); }
        _videos[ev.Cue.Id] = video;
        var tl = show.Timelines.FirstOrDefault(t => t.Cues.Any(c => c.Id == ev.Cue.Id));
        SyncVideo(video, ev, tl?.Playback ?? PlaybackState.Stop);
        return video;
    }

    static void SyncVideo(MediaElement video, EvaluatedCue ev, PlaybackState playback)
    {
        var target = TimeSpan.FromMilliseconds(Math.Max(0, ev.LocalTime));
        try
        {
            if (playback == PlaybackState.Play)
            {
                var drift = Math.Abs((video.Position - target).TotalMilliseconds);
                if (drift > 120) video.Position = target;
                video.Play();
            }
            else
            {
                video.Pause();
                video.Position = target;
            }
        }
        catch { /* decoder not ready */ }
    }

    static FrameworkElement Placeholder(Rect mapped, string name, string color)
    {
        var grid = new Grid { Width = mapped.Width, Height = mapped.Height, Background = BrushFrom(color) };
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

    (double OriginX, double OriginY, double Scale) Viewport(Show show)
    {
        if (ViewDisplay is { } d)
        {
            var scale = Math.Min(ActualWidth / Math.Max(1, d.Width), ActualHeight / Math.Max(1, d.Height));
            return (d.X, d.Y, scale > 0 ? scale : 1);
        }
        var cam = App.Session.Camera;
        var zoom = cam.Zoom <= 0 ? 0.18 : cam.Zoom;
        return (cam.X - ActualWidth / 2 / zoom, cam.Y - ActualHeight / 2 / zoom, zoom);
    }

    static Rect Map(double x, double y, double w, double h, double ox, double oy, double scale) =>
        new((x - ox) * scale, (y - oy) * scale, w * scale, h * scale);

    static Brush BrushFrom(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!); }
        catch { return new SolidColorBrush(Color.FromRgb(59, 130, 196)); }
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Editing) return;
        var cam = App.Session.Camera;
        App.Session.SetCamera(zoom: Math.Clamp(cam.Zoom * (e.Delta > 0 ? 1.12 : 0.9), 0.03, 2));
    }

    void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (!Editing) return;
        Focus();
        var show = App.Session.Show;
        if (show is null) return;
        var stage = ScreenToStage(e.GetPosition(this), show);
        var media = PlaybackClock.VisibleMedia(show).ToList();
        var hitCue = StageGeometry.HitCue(media, show.Assets, stage);
        if (hitCue is not null)
        {
            App.Session.Select(SelectionKind.Cue, hitCue.Cue.Id);
            _dragCueId = hitCue.Cue.Id;
            _dragStart = e.GetPosition(this);
            _dragCueOrigin = new Point(hitCue.Cue.Position.X, hitCue.Cue.Position.Y);
            CaptureMouse();
            return;
        }
        var hitDisp = StageGeometry.HitDisplay(show.Displays, stage);
        if (hitDisp is not null) App.Session.Select(SelectionKind.Display, hitDisp.Id);
        else App.Session.ClearSelection();
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        var show = App.Session.Show;
        if (show is null) return;
        if (_panning && Editing)
        {
            var p = e.GetPosition(this);
            var zoom = Math.Max(0.03, _panCam.Zoom);
            App.Session.SetCamera(_panCam.X - (p.X - _panStart.X) / zoom, _panCam.Y - (p.Y - _panStart.Y) / zoom, zoom);
            return;
        }
        if (_dragCueId is null) return;
        var now = e.GetPosition(this);
        var (_, _, scale) = Viewport(show);
        var x = _dragCueOrigin.X + (now.X - _dragStart.X) / scale;
        var y = _dragCueOrigin.Y + (now.Y - _dragStart.Y) / scale;
        if (App.Session.Snap)
        {
            var guides = StageGeometry.DisplayGuides(show.Displays);
            var cue = show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => c.Id == _dragCueId);
            var asset = cue?.AssetId is { } aid ? show.Assets.FirstOrDefault(a => a.Id == aid) : null;
            if (cue is not null)
            {
                var rect = StageGeometry.CueRect(cue, asset);
                var snapped = StageGeometry.SnapRect(new StageRect(x, y, rect.W, rect.H), guides.X, guides.Y, StageGeometry.SnapThreshold(scale));
                x = snapped.X;
                y = snapped.Y;
            }
        }
        App.Session.UpdateCue(_dragCueId, c => c.Position = new Vec3 { X = Math.Round(x), Y = Math.Round(y), Z = c.Position.Z }, record: false);
    }

    (double X, double Y) ScreenToStage(Point p, Show show)
    {
        var (ox, oy, scale) = Viewport(show);
        return (ox + p.X / scale, oy + p.Y / scale);
    }
}
