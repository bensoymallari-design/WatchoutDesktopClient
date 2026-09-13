using Watchout.Core.Layout;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Playback;
using Watchout.Core.Persistence;
using Watchout.Core.Stage;
using Watchout.Core.Scheduling;

namespace Watchout.Core;

public sealed class ProducerSession
{
    public string View { get; private set; } = "welcome";
    public Models.Show? Show { get; private set; }
    public string? ShowPath { get; private set; }
    public Selection Selection { get; private set; } = new();
    public string? ActiveTimelineId { get; private set; }
    public (double X, double Y, double Zoom) Camera { get; private set; } = (2880, 540, 0.18);
    public List<LogEntry> Logs { get; } = [];
    public List<RecentShow> Recents { get; private set; } = [];
    public List<WindowLayout> Windows { get; private set; } = WindowLayouts.DefaultLayout();
    public bool Snap { get; set; } = true;
    public bool ClickJumpsToTime { get; set; } = true;
    public double TimelineZoom { get; set; } = 0.012;
    public double TimelineScroll { get; set; }
    public string? HoverCueId { get; set; }
    public HashSet<string> LiveOutputs { get; } = [];

    readonly List<string> _history = [];
    readonly List<string> _future = [];

    public event Action? Changed;

    public Timeline? ActiveTimeline =>
        Show is null ? null : Show.Timelines.FirstOrDefault(t => t.Id == ActiveTimelineId) ?? Show.Timelines.FirstOrDefault();

    public void Log(string message, string level = "info")
    {
        Logs.Insert(0, new LogEntry
        {
            Id = Ids.New("log"),
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Level = level,
            Message = message,
        });
        if (Logs.Count > 400) Logs.RemoveRange(400, Logs.Count - 400);
        Changed?.Invoke();
    }

    public void NewShow()
    {
        LoadShow(ShowFactory.EmptyShow(), null);
        Log("New show — H.264 plays through DXVA. Connect a capture card in Devices to bring Resolume onto the Stage.");
    }

    public void OpenDemo()
    {
        LoadShow(ShowFactory.MakeDemoShow(), null);
        Camera = (2880, 540, 0.18);
        Log("Opened LED wall demo. Import H.264 or connect a capture card (Resolume HDMI) in Devices.");
    }

    public void LoadShow(Models.Show show, string? path)
    {
        Show = show;
        ShowPath = path;
        View = "producer";
        ActiveTimelineId = show.Timelines.FirstOrDefault()?.Id;
        Selection = new Selection();
        _history.Clear();
        _future.Clear();
        FrameDisplays();
        if (!string.IsNullOrEmpty(path))
            Remember(path, show.Name, show.Id);
        Changed?.Invoke();
    }

    public void QuitToWelcome()
    {
        Show = null;
        ShowPath = null;
        View = "welcome";
        Changed?.Invoke();
    }

    public string SaveJson()
    {
        if (Show is null) throw new InvalidOperationException("No show");
        Show.ModifiedAt = DateTime.UtcNow.ToString("o");
        return ShowSerializer.Save(Show);
    }

    public void DidSave(string path)
    {
        ShowPath = path;
        if (Show is not null) Remember(path, Show.Name, Show.Id);
        Log($"Saved {path}");
        Changed?.Invoke();
    }

    public void SetRecents(IEnumerable<RecentShow> recents)
    {
        Recents = recents.ToList();
        Changed?.Invoke();
    }

    public void Select(SelectionKind kind, params string[] ids)
    {
        Selection = new Selection { Kind = kind, Ids = ids.ToList() };
        Changed?.Invoke();
    }

    public void ClearSelection() => Select(SelectionKind.None);

    public void SetActiveTimeline(string id)
    {
        ActiveTimelineId = id;
        Changed?.Invoke();
    }

    public void SetPlayhead(string timelineId, double ms)
    {
        var tl = Show?.Timelines.FirstOrDefault(t => t.Id == timelineId);
        if (tl is null) return;
        tl.Playhead = Math.Max(0, Math.Min(tl.Duration, ms));
        Changed?.Invoke();
    }

    public void SetPlayback(string? timelineId, PlaybackState state)
    {
        if (Show is null) return;
        IEnumerable<Timeline> targets = timelineId is null
            ? Show.Timelines
            : Show.Timelines.Where(t => t.Id == timelineId);
        foreach (var tl in targets)
            PlaybackClock.SetPlayback(tl, state);
        Log(state switch
        {
            PlaybackState.Play => "Play — DXVA H.264 outputs follow this clock",
            PlaybackState.Pause => "Pause",
            _ => "Stop",
        });
        Changed?.Invoke();
    }

    public void TogglePlay()
    {
        var tl = ActiveTimeline;
        if (tl is null) return;
        SetPlayback(tl.Id, tl.Playback == PlaybackState.Play ? PlaybackState.Pause : PlaybackState.Play);
    }

    public void Tick(double dtMs)
    {
        if (Show is null) return;
        if (Show.Timelines.All(t => t.Playback != PlaybackState.Play)) return;
        PlaybackClock.Tick(Show, dtMs);
        Changed?.Invoke();
    }

    public void SetCamera(double? x = null, double? y = null, double? zoom = null)
    {
        Camera = (x ?? Camera.X, y ?? Camera.Y, zoom ?? Camera.Zoom);
        Changed?.Invoke();
    }

    public void FrameDisplays()
    {
        if (Show is null) return;
        var wall = StageGeometry.WallRect(Show.Displays);
        if (wall is not { } w) return;
        Camera = (w.X + w.W / 2, w.Y + w.H / 2, 0.18);
        Changed?.Invoke();
    }

    public void ApplyImported(ImportedMedia media)
    {
        Mutate(show =>
        {
            var existing = show.Assets.FirstOrDefault(a => a.Id == media.Id);
            var asset = new Asset
            {
                Id = media.Id,
                Name = media.Name,
                Kind = media.Kind,
                Width = media.Width,
                Height = media.Height,
                Duration = media.Duration,
                Fps = media.Fps,
                Url = media.Url,
                Codec = media.Codec,
                Color = media.Color,
                Optimized = media.Optimized,
                Notes = media.Notes,
                OriginalPath = media.OriginalPath,
                ProxyPath = media.ProxyPath,
                ProxyVersion = media.ProxyVersion,
                Bytes = media.Bytes,
                Linked = media.Linked,
                PosterUrl = media.PosterUrl,
            };
            if (existing is null) show.Assets.Add(asset);
            else
            {
                var i = show.Assets.IndexOf(existing);
                show.Assets[i] = asset;
            }
        }, record: false);
        Log(media.Notes);
    }

    public Cue? AddCueFromAsset(string assetId, string? layerId = null, double? start = null, string? displayId = null)
    {
        Cue? created = null;
        Mutate(show =>
        {
            var tl = ActiveTimelineOf(show);
            if (tl is null) return;
            var asset = show.Assets.FirstOrDefault(a => a.Id == assetId);
            if (asset is null) return;
            var layer = layerId is not null
                ? tl.Layers.FirstOrDefault(l => l.Id == layerId)
                : tl.Layers.FirstOrDefault(l => l.Enabled && !l.Locked) ?? tl.Layers.FirstOrDefault();
            if (layer is null) return;
            var display = displayId is not null
                ? show.Displays.FirstOrDefault(d => d.Id == displayId)
                : show.Displays.FirstOrDefault(d => d.Enabled);
            var cue = ShowFactory.EmptyCue(new Cue
            {
                Name = asset.Name,
                LayerId = layer.Id,
                Start = LiveSources.IsCapture(asset) ? 0 : start ?? tl.Playhead,
                Duration = LiveSources.IsCapture(asset) ? Math.Max(tl.Duration, LiveSources.LiveCueDurationMs) : (asset.Duration > 0 ? asset.Duration : show.Prefs.ImageDuration),
                AssetId = asset.Id,
                Color = asset.Color,
                Type = CueType.Media,
                FreeRunning = LiveSources.IsCapture(asset) || asset.Kind == AssetKind.Ndi,
            });
            if (display is not null)
            {
                var fit = StageGeometry.FitTransform(asset, display);
                cue.Position = fit.Position;
                cue.Scale = fit.Scale;
            }
            tl.Cues.Add(cue);
            created = cue;
            Selection = new Selection { Kind = SelectionKind.Cue, Ids = [cue.Id] };
        });
        return created;
    }

    public void FitSelectedToDisplay(string mode = "cover")
    {
        Mutate(show =>
        {
            foreach (var cue in SelectedCues(show))
            {
                var asset = show.Assets.FirstOrDefault(a => a.Id == cue.AssetId);
                var display = StageGeometry.DisplayForCue(show.Displays, cue);
                if (asset is null || display is null) continue;
                var fit = StageGeometry.FitTransform(asset, display, mode);
                cue.Position = fit.Position;
                cue.Scale = fit.Scale;
            }
        });
    }

    public void FitSelectedToWall(string mode = "cover")
    {
        Mutate(show =>
        {
            var wall = StageGeometry.WallAsBox(show.Displays);
            if (wall is null) return;
            var (x, y, w, h) = wall.Value;
            foreach (var cue in SelectedCues(show))
            {
                var asset = show.Assets.FirstOrDefault(a => a.Id == cue.AssetId);
                if (asset is null) continue;
                var fit = StageGeometry.FitTransform(asset.Width, asset.Height, x, y, w, h, mode);
                cue.Position = fit.Position;
                cue.Scale = fit.Scale;
            }
        });
    }

    public void UpdateCue(string id, Action<Cue> patch, bool record = true) =>
        Mutate(show =>
        {
            var cue = show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => c.Id == id);
            if (cue is not null) patch(cue);
        }, record);

    public void MoveCues(IEnumerable<string> ids, double dStart, string? layerId = null)
    {
        var set = ids.ToHashSet();
        Mutate(show =>
        {
            foreach (var cue in show.Timelines.SelectMany(t => t.Cues).Where(c => set.Contains(c.Id)))
            {
                cue.Start = Math.Max(0, cue.Start + dStart);
                if (layerId is not null) cue.LayerId = layerId;
            }
        });
    }

    public void ResizeCue(string id, double start, double duration) =>
        UpdateCue(id, c =>
        {
            c.Start = Math.Max(0, start);
            c.Duration = Math.Max(0, duration);
        });

    public void DeleteSelected()
    {
        Mutate(show =>
        {
            if (Selection.Kind == SelectionKind.Cue)
            {
                var drop = Selection.Ids.ToHashSet();
                foreach (var tl in show.Timelines)
                    tl.Cues = tl.Cues.Where(c => !drop.Contains(c.Id)).ToList();
            }
            else if (Selection.Kind == SelectionKind.Asset)
                TimelineMath.PurgeAssets(show, Selection.Ids);
            else if (Selection.Kind == SelectionKind.Display)
            {
                var drop = Selection.Ids.ToHashSet();
                show.Displays = show.Displays.Where(d => !drop.Contains(d.Id)).ToList();
            }
            else if (Selection.Kind == SelectionKind.Timeline)
            {
                show.Timelines = TimelineMath.RemoveTimelinesById(show.Timelines, Selection.Ids, t => t.Id);
                ActiveTimelineId = show.Timelines.First().Id;
            }
            Selection = new Selection();
        });
    }

    public void DuplicateSelected()
    {
        Mutate(show =>
        {
            if (Selection.Kind != SelectionKind.Cue) return;
            var copies = new List<string>();
            foreach (var tl in show.Timelines)
            {
                foreach (var cue in tl.Cues.Where(c => Selection.Ids.Contains(c.Id)).ToList())
                {
                    var json = ShowSerializer.SaveCue(cue);
                    var copy = ShowSerializer.LoadCue(json);
                    copy.Id = Ids.New("cue");
                    copy.Start += 1000;
                    copy.Name += " copy";
                    tl.Cues.Add(copy);
                    copies.Add(copy.Id);
                }
            }
            Selection = new Selection { Kind = SelectionKind.Cue, Ids = copies };
        });
    }

    public void ToggleFade(string which)
    {
        Mutate(show =>
        {
            foreach (var cue in SelectedCues(show))
            {
                if (which == "in") cue.FadeIn = !cue.FadeIn;
                else cue.FadeOut = !cue.FadeOut;
            }
        });
    }

    public void ApplyCrossfade()
    {
        Mutate(show =>
        {
            var tl = ActiveTimelineOf(show);
            if (tl is null) return;
            var pair = TimelineMath.FindCrossfadePair(tl.Cues, Selection.Ids);
            if (pair is null) return;
            pair.Value.A.FadeOut = true;
            pair.Value.B.FadeIn = true;
        });
    }

    public void AddDisplay(Display? partial = null)
    {
        Mutate(show =>
        {
            var d = ShowFactory.EmptyDisplay(partial);
            if (show.Displays.Count > 0)
            {
                var last = show.Displays[^1];
                d.X = last.X + last.Width;
                d.Channel = show.Displays.Count + 1;
                d.Name = $"Display {d.Channel}";
            }
            show.Displays.Add(d);
            Selection = new Selection { Kind = SelectionKind.Display, Ids = [d.Id] };
        });
    }

    public void AddDisplayGrid(int cols, int rows, double w, double h, double gap = 0)
    {
        Mutate(show =>
        {
            show.Displays.Clear();
            for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                show.Displays.Add(ShowFactory.EmptyDisplay(new Display
                {
                    Name = $"Display {r * cols + c + 1}",
                    X = c * (w + gap),
                    Y = r * (h + gap),
                    Width = w,
                    Height = h,
                    Channel = r * cols + c + 1,
                }));
            }
        });
        FrameDisplays();
    }

    public void UpdateDisplay(string id, Action<Display> patch, bool record = true) =>
        Mutate(show =>
        {
            var d = show.Displays.FirstOrDefault(x => x.Id == id);
            if (d is not null) patch(d);
        }, record);

    public void MapScreens(IReadOnlyList<OutputScreen> screens, bool includePrimary = false)
    {
        Mutate(show => show.Displays = ScreenAssign.LayoutDisplaysOnScreens(show.Displays, screens, includePrimary));
        FrameDisplays();
        Log(includePrimary
            ? "Mapped every monitor, including the Producer laptop, onto the Stage."
            : "Mapped HDMI / extra monitors onto the Stage. Laptop stays the Producer.");
    }

    public void AddTimeline()
    {
        Mutate(show =>
        {
            var tl = ShowFactory.EmptyTimeline($"Timeline {show.Timelines.Count + 1}");
            show.Timelines.Add(tl);
            ActiveTimelineId = tl.Id;
        });
    }

    public void UpdateTimeline(string id, Action<Timeline> patch) =>
        Mutate(show =>
        {
            var tl = show.Timelines.FirstOrDefault(t => t.Id == id);
            if (tl is not null) patch(tl);
        });

    public void AddLayer()
    {
        Mutate(show =>
        {
            var tl = ActiveTimelineOf(show);
            tl?.Layers.Add(ShowFactory.EmptyLayer($"Layer {tl.Layers.Count + 1}", tl.Layers.Count + 1));
        });
    }

    public void SetShowName(string name)
    {
        if (Show is null) return;
        Show.Name = name;
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (Show is null || _history.Count == 0) return;
        _future.Add(ShowSerializer.Save(Show));
        Show = ShowSerializer.Load(_history[^1]);
        _history.RemoveAt(_history.Count - 1);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_future.Count == 0) return;
        if (Show is not null) _history.Add(ShowSerializer.Save(Show));
        Show = ShowSerializer.Load(_future[^1]);
        _future.RemoveAt(_future.Count - 1);
        Changed?.Invoke();
    }

    public Asset ConnectCapture(string deviceId, string name, int width = 1920, int height = 1080, bool announce = true)
    {
        if (Show is null) NewShow();
        var existing = Show!.Assets.FirstOrDefault(a => LiveSources.CaptureDeviceId(a) == deviceId);
        var media = LiveSources.CaptureAsset(deviceId, name, width, height);
        if (existing is not null) media.Id = existing.Id;
        ApplyImported(media);
        Mutate(show =>
        {
            show.CaptureDevices = show.CaptureDevices
                .Where(d => d.Signal != deviceId)
                .Append(new CaptureDevice
                {
                    Id = Ids.New("cap"),
                    Name = name,
                    NodeId = "local-runner",
                    Kind = "HDMI",
                    Signal = deviceId,
                })
                .ToList();
        }, record: false);
        var hasCue = Show.Timelines.SelectMany(t => t.Cues).Any(c => c.AssetId == media.Id);
        var displaysBefore = Show.Displays.Count;
        if (!hasCue)
        {
            string? displayId = null;
            string? layerId = null;
            Mutate(show =>
            {
                displayId = LiveSources.EnsureDisplayForCapture(show).Id;
                layerId = LiveSources.NextCaptureLayerId(show);
            }, record: false);
            AddCueFromAsset(media.Id, layerId, 0, displayId);
        }
        if (announce)
        {
            Log($"Live capture connected: {name} — play Resolume (or any HDMI/SDI source) into this card");
            if (Show.Displays.Count > displaysBefore) FrameDisplays();
        }
        return Show.Assets.First(a => a.Id == media.Id);
    }

    public int ConnectCaptures(IEnumerable<(string Id, string Name)> devices)
    {
        var list = devices.ToList();
        foreach (var device in list)
            ConnectCapture(device.Id, device.Name, announce: false);
        if (list.Count > 0) FrameDisplays();
        if (list.Count == 1)
            Log($"Live capture connected: {list[0].Name} — play Resolume (or any HDMI/SDI source) into this card");
        else if (list.Count > 1)
            Log($"Connected {list.Count} capture cards across {Show!.Displays.Count} display(s). Each card is its own live layer — map extra HDMI outputs in Devices.");
        return list.Count;
    }

    public void UpdateAsset(string id, Action<Asset> patch) =>
        Mutate(show =>
        {
            var a = show.Assets.FirstOrDefault(x => x.Id == id);
            if (a is not null) patch(a);
        }, record: false);

    Timeline? ActiveTimelineOf(Models.Show show) =>
        show.Timelines.FirstOrDefault(t => t.Id == ActiveTimelineId) ?? show.Timelines.FirstOrDefault();

    IEnumerable<Cue> SelectedCues(Models.Show show) =>
        show.Timelines.SelectMany(t => t.Cues).Where(c => Selection.Kind == SelectionKind.Cue && Selection.Ids.Contains(c.Id));

    void Mutate(Action<Models.Show> mutator, bool record = true)
    {
        if (Show is null) return;
        if (record) _history.Add(ShowSerializer.Save(Show));
        if (_history.Count > 50) _history.RemoveAt(0);
        if (record) _future.Clear();
        mutator(Show);
        Show.ModifiedAt = DateTime.UtcNow.ToString("o");
        Changed?.Invoke();
    }

    void Remember(string path, string name, string id)
    {
        Recents = new[]
        {
            new RecentShow { Id = id, Name = name, Path = path, SavedAt = DateTime.UtcNow.ToString("o") },
        }.Concat(Recents.Where(r => r.Path != path)).Take(10).ToList();
    }
}
