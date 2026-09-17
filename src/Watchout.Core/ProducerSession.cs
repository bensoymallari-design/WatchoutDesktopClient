using Watchout.Core.Layout;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Network;
using Watchout.Core.Playback;
using Watchout.Core.Persistence;
using Watchout.Core.Stage;
using Watchout.Core.Scheduling;
using Watchout.Core.Gpu;

namespace Watchout.Core;

public sealed class ProducerSession
{
    public string View { get; private set; } = "welcome";
    public Models.Show? Show { get; private set; }
    public string? ShowPath { get; private set; }
    public Selection Selection { get; private set; } = new();
    public string? ActiveTimelineId { get; private set; }
    public (double X, double Y, double Zoom) Camera { get; private set; } = (960, 540, 0.4);
    public double StageViewWidth { get; private set; } = 960;
    public double StageViewHeight { get; private set; } = 540;
    public List<LogEntry> Logs { get; } = [];
    public List<RecentShow> Recents { get; private set; } = [];
    public List<WindowLayout> Windows { get; private set; } = WindowLayouts.DefaultLayout();
    public bool Snap { get; set; } = true;
    public bool ClickJumpsToTime { get; set; } = true;
    public StageEditMode StageEditMode { get; private set; } = StageEditMode.Cues;
    public double TimelineZoom { get; set; } = 0.012;
    public double TimelineScroll { get; set; }
    public double TimelineLayerScroll { get; set; }
    public double TimelineViewWidth { get; private set; } = 800;
    public double TimelineViewHeight { get; private set; } = 200;
    public string? HoverCueId { get; set; }
    public HashSet<string> LiveOutputs { get; } = [];
    public int DecoderEpoch { get; private set; }
    public bool StageLayoutBusy { get; private set; }

    readonly List<string> _history = [];
    readonly List<string> _future = [];
    bool _liveLayoutDirty;

    public event Action? Changed;
    public event Action? Clock;
    public event Action? TimelineViewChanged;
    public event Action? LayoutChanged;
    public event Action? PlaybackChanged;

    public bool PickingChroma { get; private set; }
    public bool BlindEdit { get; private set; }

    Models.Show? _playback;

    public Models.Show? PlaybackShow => BlindEdit ? _playback ?? Show : Show;

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
        Log("Opened LED wall demo. Import H.264 or connect a capture card (Resolume HDMI) in Devices.");
    }

    public void LoadShow(Models.Show show, string? path)
    {
        Show = show;
        ShowPath = path;
        View = "producer";
        ActiveTimelineId = show.Timelines.FirstOrDefault()?.Id;
        Selection = new Selection();
        TimelineZoom = 0.012;
        TimelineScroll = 0;
        TimelineLayerScroll = 0;
        BlindEdit = false;
        _playback = null;
        _history.Clear();
        _future.Clear();
        FrameDisplays();
        if (!string.IsNullOrEmpty(path))
            Remember(path, show.Name, show.Id);
        Changed?.Invoke();
        if (show.Prefs.AutoStart) Play();
    }

    public void QuitToWelcome()
    {
        Show = null;
        ShowPath = null;
        View = "welcome";
        BlindEdit = false;
        _playback = null;
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
        var list = ids.ToList();
        if (Selection.Kind == kind && Selection.Ids.Count == list.Count && Selection.Ids.SequenceEqual(list))
        {
            if (kind == SelectionKind.Cue && StageEditMode != StageEditMode.Cues)
            {
                StageEditMode = StageEditMode.Cues;
                Changed?.Invoke();
            }
            return;
        }
        Selection = new Selection { Kind = kind, Ids = list };
        if (kind == SelectionKind.Cue)
            StageEditMode = StageEditMode.Cues;
        Changed?.Invoke();
    }

    public void ClearSelection() => Select(SelectionKind.None);

    public void SetActiveTimeline(string id)
    {
        ActiveTimelineId = id;
        ClampTimelineView();
        Changed?.Invoke();
    }

    public void ReportTimelineView(double width, double height)
    {
        var w = TimelineViewWidth;
        var h = TimelineViewHeight;
        if (width > 1) TimelineViewWidth = width;
        if (height > 1) TimelineViewHeight = height;
        ClampTimelineView();
        if (Math.Abs(w - TimelineViewWidth) > 0.5 || Math.Abs(h - TimelineViewHeight) > 0.5)
            TimelineViewChanged?.Invoke();
    }

    public double VisibleDurationMs() =>
        TimelineMath.VisibleDurationMs(TimelineViewWidth, TimelineZoom);

    public void SetTimelineScroll(double scroll)
    {
        var next = ActiveTimeline is null
            ? 0
            : TimelineMath.ClampScroll(scroll, ActiveTimeline.Duration, VisibleDurationMs());
        if (Math.Abs(next - TimelineScroll) < 0.01) return;
        TimelineScroll = next;
        TimelineViewChanged?.Invoke();
    }

    public void SetTimelineLayerScroll(double scroll)
    {
        var lanesH = Math.Max(0, TimelineViewHeight - TimelineMath.RulerHeight);
        var next = ActiveTimeline is null
            ? 0
            : TimelineMath.ClampLayerScroll(scroll, ActiveTimeline.Layers.Count, TimelineMath.LaneHeight, lanesH);
        if (Math.Abs(next - TimelineLayerScroll) < 0.01) return;
        TimelineLayerScroll = next;
        TimelineViewChanged?.Invoke();
    }

    public void SetTimelineZoom(double zoom, double? keepMs = null, double? keepX = null)
    {
        TimelineZoom = TimelineMath.ClampZoom(zoom);
        if (keepMs is double ms && keepX is double x && x > TimelineMath.HeaderWidth)
            TimelineScroll = ms - (x - TimelineMath.HeaderWidth) / TimelineZoom;
        ClampTimelineView();
        TimelineViewChanged?.Invoke();
    }

    public void RevealTime(double startMs, double endMs)
    {
        ApplyRevealTime(startMs, endMs);
        TimelineViewChanged?.Invoke();
    }

    public void RevealLayer(int index)
    {
        ApplyRevealLayer(index);
        TimelineViewChanged?.Invoke();
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
        PlaybackChanged?.Invoke();
    }

    /// <summary>
    /// Stage drag/resize. Output keeps its last DXVA rectangle until this
    /// returns to false so the wall does not hitch behind the mouse.
    /// </summary>
    public void SetStageLayoutBusy(bool busy)
    {
        if (StageLayoutBusy == busy) return;
        StageLayoutBusy = busy;
        LayoutChanged?.Invoke();
    }

    public void TogglePlay()
    {
        var tl = ActiveTimeline;
        if (tl is null) return;
        SetPlayback(tl.Id, PlaybackClock.ToggleTarget(tl));
    }

    public void Play(string? timelineId = null) => SetPlayback(timelineId ?? ActiveTimelineId, PlaybackState.Play);

    public void Pause(string? timelineId = null) => SetPlayback(timelineId ?? ActiveTimelineId, PlaybackState.Pause);

    public void Stop(string? timelineId = null) => SetPlayback(timelineId, PlaybackState.Stop);

    /// <summary>
    /// One DXVA decode of a 4K file. Stage plus Output both decoding the same
    /// clip is what went black after a long run.
    /// </summary>
    public bool StageYieldsFileDecoder => LiveOutputs.Count > 0;

    public void NoteLiveOutputsChanged()
    {
        DecoderEpoch++;
        Changed?.Invoke();
    }

    public void SetStageEditMode(StageEditMode mode)
    {
        if (StageEditMode == mode) return;
        StageEditMode = mode;
        if (mode == StageEditMode.Displays)
            Log("Display canvas — click a display to move and resize it. Amber handles edit Width×Height.");
        else
            Log("Cue canvas — double-click a display (or hold Alt) to edit the display instead.");
        Changed?.Invoke();
    }

    public void SetSnap(bool snap)
    {
        Snap = snap;
        Changed?.Invoke();
    }

    public void SetClickJumpsToTime(bool value)
    {
        ClickJumpsToTime = value;
        Changed?.Invoke();
    }

    public void Tick(double dtMs)
    {
        if (Show is null) return;
        var livePlaying = Show.Timelines.Any(t => t.Playback == PlaybackState.Play);
        var wallPlaying = BlindEdit && _playback is not null && _playback.Timelines.Any(t => t.Playback == PlaybackState.Play);
        if (!OutputViewMath.PulseClock(livePlaying || wallPlaying, LiveOutputs.Count > 0)) return;
        if (livePlaying) PlaybackClock.Tick(Show, dtMs);
        if (wallPlaying) PlaybackClock.Tick(_playback!, dtMs);
        Clock?.Invoke();
    }

    public void SetBlindEdit(bool on)
    {
        if (Show is null) return;
        if (on == BlindEdit) return;
        if (on)
        {
            _playback = ShowSerializer.Clone(Show);
            BlindEdit = true;
            Log("Blind edit — Output holds that snapshot. Producer Stage is free to change. Take to Output when the wall should match.");
        }
        else
        {
            BlindEdit = false;
            _playback = null;
            Log("Live to Output — Stage edits go to the wall again.");
        }
        PlaybackChanged?.Invoke();
        Changed?.Invoke();
    }

    public void TakeToOutput()
    {
        if (Show is null) return;
        if (!BlindEdit)
        {
            Log("Take is for Blind edit — Output already follows Producer.");
            return;
        }
        _playback = ShowSerializer.Clone(Show);
        Log("Take to Output — wall now matches Producer.");
        PlaybackChanged?.Invoke();
        Changed?.Invoke();
    }

    public ImportReport ImportWatchout6(string path)
    {
        var report = Watchout6Importer.ImportFile(path);
        if (report.Show is not null)
            LoadShow(report.Show, report.NativeWatchMe ? path : null);
        Log(report.Message, report.Ok ? "info" : "warn");
        foreach (var note in report.Notes)
            Log(note);
        return report;
    }

    public void ReportStageView(double width, double height)
    {
        if (width > 1) StageViewWidth = width;
        if (height > 1) StageViewHeight = height;
    }

    public void SetCamera(double? x = null, double? y = null, double? zoom = null)
    {
        var next = (x ?? Camera.X, y ?? Camera.Y, zoom ?? Camera.Zoom);
        if (Math.Abs(next.Item1 - Camera.X) < 0.01
            && Math.Abs(next.Item2 - Camera.Y) < 0.01
            && Math.Abs(next.Item3 - Camera.Zoom) < 0.0001)
            return;
        Camera = next;
        LayoutChanged?.Invoke();
    }

    public void FrameDisplays()
    {
        if (Show is null) return;
        var wall = StageGeometry.WallRect(Show.Displays);
        if (wall is not { } w) return;
        Camera = StageGeometry.FitCamera(w, StageViewWidth, StageViewHeight);
        Changed?.Invoke();
    }

    public void FrameDisplay(string? displayId = null)
    {
        if (Show is null) return;
        var display = displayId is not null
            ? Show.Displays.FirstOrDefault(d => d.Id == displayId)
            : Selection.Kind == SelectionKind.Display
                ? Show.Displays.FirstOrDefault(d => Selection.Ids.Contains(d.Id))
                : null;
        display ??= Show.Displays.FirstOrDefault(d => d.Enabled) ?? Show.Displays.FirstOrDefault();
        if (display is null)
        {
            FrameDisplays();
            return;
        }
        Camera = StageGeometry.FitCamera(StageGeometry.DisplayRect(display), StageViewWidth, StageViewHeight, 36);
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
                Channels = media.Channels,
                BitDepth = media.BitDepth,
                ColorSpace = media.ColorSpace,
            };
            if (existing is null) show.Assets.Add(asset);
            else
            {
                asset.Revisions = existing.Revisions ?? [];
                asset.Children = existing.Children ?? [];
                asset.Dynamic = existing.Dynamic;
                asset.ActiveRevisionId = existing.ActiveRevisionId;
                var i = show.Assets.IndexOf(existing);
                show.Assets[i] = asset;
            }
        }, record: false);
        Log(media.Notes);
    }

    public Cue? AddCueFromAsset(string assetId, string? layerId = null, double? start = null, string? displayId = null)
    {
        if (RefuseLockedLayer(ActiveTimeline, layerId)) return null;
        Cue? created = null;
        Mutate(show =>
        {
            var tl = ActiveTimelineOf(show);
            if (tl is null) return;
            var asset = show.Assets.FirstOrDefault(a => a.Id == assetId);
            if (asset is null) return;
            var layer = PickEditableLayer(tl, layerId);
            if (layer is null) return;
            var display = displayId is not null
                ? show.Displays.FirstOrDefault(d => d.Id == displayId)
                : show.Displays.FirstOrDefault(d => d.Enabled);
            var live = LiveSources.IsLive(asset);
            var cue = ShowFactory.EmptyCue(new Cue
            {
                Name = asset.Name,
                LayerId = layer.Id,
                Start = live ? 0 : start ?? tl.Playhead,
                Duration = live ? Math.Max(tl.Duration, LiveSources.LiveCueDurationMs) : (asset.Duration > 0 ? asset.Duration : show.Prefs.ImageDuration),
                AssetId = asset.Id,
                Color = asset.Color,
                Type = CueType.Media,
                FreeRunning = live,
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
            if (!live)
            {
                var end = TimelineMath.CueEnd(cue);
                if (end > tl.Duration)
                    tl.Duration = TimelineMath.ExtendDurationTo(tl.Duration, end);
                ApplyRevealTime(cue.Start, end);
                if (tl.Playback == PlaybackState.Play && !PlaybackClock.HasVisibleMediaCue(tl, tl.Playhead))
                    tl.Playhead = cue.Start;
            }
            var layerIndex = tl.Layers.FindIndex(l => l.Id == layer.Id);
            if (layerIndex >= 0) ApplyRevealLayer(layerIndex);
        });
        return created;
    }

    public Cue? AddCueAtEnd(string assetId, string? layerId = null)
    {
        var tl = ActiveTimeline;
        if (tl is null) return null;
        var cue = AddCueFromAsset(assetId, layerId, TimelineMath.ContentEnd(tl.Cues));
        if (cue is not null)
            Log($"Placed {cue.Name} at the end — Fit to media to frame the whole timeline");
        return cue;
    }

    public Cue? DropAssetOnStage(string assetId, string? displayId, double x, double y)
    {
        var cue = AddCueFromAsset(assetId, displayId: displayId);
        if (cue is null) return null;
        if (displayId is null)
        {
            UpdateCue(cue.Id, c =>
            {
                c.Position = new Vec3 { X = Math.Round(x), Y = Math.Round(y), Z = c.Position.Z };
            }, record: false);
        }
        Log(displayId is null
            ? $"Placed {cue.Name} on Stage — press Space to play"
            : $"Placed {cue.Name} on the display — press Space to play");
        return cue;
    }

    public Cue? AddPlaceholderCue(string? layerId = null, double? start = null)
    {
        if (RefuseLockedLayer(ActiveTimeline, layerId)) return null;
        Cue? created = null;
        Mutate(show =>
        {
            var tl = ActiveTimelineOf(show);
            if (tl is null) return;
            var layer = PickEditableLayer(tl, layerId);
            if (layer is null) return;
            var display = show.Displays.FirstOrDefault(d => d.Enabled) ?? show.Displays.FirstOrDefault();
            var cue = ShowFactory.EmptyCue(new Cue
            {
                Name = "Placeholder",
                Type = CueType.Media,
                LayerId = layer.Id,
                Start = start ?? tl.Playhead,
                Duration = show.Prefs.ImageDuration,
                Color = "#78716C",
            });
            if (display is not null)
            {
                cue.Position = new Vec3 { X = display.X, Y = display.Y };
                cue.Scale = new Vec2
                {
                    X = display.Width / 19.20,
                    Y = display.Height / 10.80,
                };
            }
            tl.Cues.Add(cue);
            created = cue;
            Selection = new Selection { Kind = SelectionKind.Cue, Ids = [cue.Id] };
        });
        if (created is not null)
            Log("Placeholder cue — assign a clip, capture, or NDI in Properties when the content is ready");
        return created;
    }

    public void ReplaceCueMedia(string cueId, string? assetId)
    {
        Mutate(show =>
        {
            var cue = show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => c.Id == cueId);
            if (cue is null) return;
            if (string.IsNullOrEmpty(assetId))
            {
                cue.AssetId = null;
                cue.Name = "Placeholder";
                cue.Color = "#78716C";
                return;
            }
            var next = show.Assets.FirstOrDefault(a => a.Id == assetId);
            if (next is null) return;
            var previous = cue.AssetId is { } oldId ? show.Assets.FirstOrDefault(a => a.Id == oldId) : null;
            var fit = CueLooks.ReplaceMedia(show.Prefs.MediaReplaceMode, previous, next, cue.Position, cue.Scale);
            cue.AssetId = next.Id;
            cue.Name = next.Name;
            cue.Color = next.Color;
            cue.Position = fit.Position;
            cue.Scale = fit.Scale;
            if (!LiveSources.IsLive(next) && next.Duration > 0)
                cue.Duration = next.Duration;
        });
        Log("Replaced cue media");
    }

    public void SetMediaReplaceMode(MediaReplaceMode mode) =>
        Mutate(show => show.Prefs.MediaReplaceMode = mode);

    public void SetShowAutoStart(bool value) =>
        Mutate(show => show.Prefs.AutoStart = value);

    public void BeginPickChroma()
    {
        if (Selection.Kind != SelectionKind.Cue)
        {
            Log("Select a cue first, then pick the key color on Stage", "warn");
            return;
        }
        PickingChroma = true;
        Log("Click the Stage (or Output) to pick the chroma-key color");
        Changed?.Invoke();
    }

    public void ApplyPickedChroma(string hex)
    {
        PickingChroma = false;
        var id = Selection.Kind == SelectionKind.Cue ? Selection.Ids.FirstOrDefault() : null;
        if (id is null)
        {
            Changed?.Invoke();
            return;
        }
        UpdateCue(id, c =>
        {
            c.ChromaKeyEnabled = true;
            c.ChromaKeyColor = hex;
        });
        Log($"Chroma key {hex}");
    }

    public void CancelPickChroma()
    {
        if (!PickingChroma) return;
        PickingChroma = false;
        Changed?.Invoke();
    }

    public void FitSelectedToDisplay(string mode = "contain")
    {
        if (Show is null || !SelectedCues(Show).Any())
        {
            Log("Select a cue, then Fit cue to fill that display", "warn");
            return;
        }
        string? name = null;
        string? size = null;
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
                name = cue.Name;
                size = $"{display.Name} {display.Width:0}×{display.Height:0}";
            }
        });
        if (name is not null) Log($"Fitted {name} to {size}");
    }

    public void SetCuePixelSize(string id, double? width = null, double? height = null)
    {
        if (CueLayerLocked(id))
        {
            Log("Layer is locked", "warn");
            return;
        }
        Mutate(show =>
        {
            var cue = show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => c.Id == id);
            if (cue is null) return;
            var asset = show.Assets.FirstOrDefault(a => a.Id == cue.AssetId);
            var size = StageGeometry.CuePixelSize(asset, cue.Scale);
            cue.Scale = StageGeometry.ScaleFromPixelSize(asset, width ?? size.W, height ?? size.H);
        });
    }

    public void FitSelectedToWall(string mode = "contain")
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

    public void UpdateCue(string id, Action<Cue> patch, bool record = true)
    {
        if (CueLayerLocked(id))
        {
            Log("Layer is locked", "warn");
            return;
        }
        Mutate(show =>
        {
            var cue = show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => c.Id == id);
            if (cue is not null) patch(cue);
        }, record);
    }

    /// <summary>
    /// Stage drag/resize: patch position/scale without rebuilding Devices, Timeline, or seeking video.
    /// </summary>
    public void LiveUpdateCue(string id, Action<Cue> patch)
    {
        if (Show is null || CueLayerLocked(id)) return;
        var cue = Show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => c.Id == id);
        if (cue is null) return;
        var x = cue.Position.X;
        var y = cue.Position.Y;
        var z = cue.Position.Z;
        var sx = cue.Scale.X;
        var sy = cue.Scale.Y;
        patch(cue);
        if (x == cue.Position.X && y == cue.Position.Y && z == cue.Position.Z
            && sx == cue.Scale.X && sy == cue.Scale.Y)
            return;
        Show.ModifiedAt = DateTime.UtcNow.ToString("o");
        _liveLayoutDirty = true;
        LayoutChanged?.Invoke();
    }

    /// <summary>
    /// Mouse-up after Stage drag/resize. Properties and Devices read X/Y/Width
    /// from Changed; live drag only fires LayoutChanged so Output does not hitch.
    /// </summary>
    public void CommitLiveLayout()
    {
        if (!_liveLayoutDirty) return;
        _liveLayoutDirty = false;
        Changed?.Invoke();
    }

    public void MoveCues(IEnumerable<string> ids, double dStart, string? layerId = null)
    {
        if (layerId is not null && RefuseLockedLayer(ActiveTimeline, layerId)) return;
        var set = ids.ToHashSet();
        Mutate(show =>
        {
            foreach (var cue in show.Timelines.SelectMany(t => t.Cues).Where(c => set.Contains(c.Id)))
            {
                if (CueOnLockedLayer(show, cue)) continue;
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
        if (Selection.Kind == SelectionKind.Timeline)
        {
            var id = Selection.Ids.FirstOrDefault();
            if (id is not null) DeleteTimeline(id);
            return;
        }
        if (Selection.Kind == SelectionKind.Layer)
        {
            var id = Selection.Ids.FirstOrDefault();
            if (id is not null) DeleteLayer(id);
            return;
        }
        if (Selection.Kind == SelectionKind.Display && Show is not null && Show.Displays.Count <= Selection.Ids.Count)
        {
            Log("Keep at least one display", "warn");
            return;
        }
        Mutate(show =>
        {
            if (Selection.Kind == SelectionKind.Cue)
            {
                var drop = Selection.Ids.Where(id => !CueLayerLocked(id)).ToHashSet();
                if (drop.Count == 0)
                {
                    Log("Layer is locked", "warn");
                    return;
                }
                foreach (var tl in show.Timelines)
                    tl.Cues = tl.Cues.Where(c => !drop.Contains(c.Id)).ToList();
            }
            else if (Selection.Kind == SelectionKind.Asset)
            {
                TimelineMath.PurgeAssets(show, Selection.Ids);
                Log(Selection.Ids.Count == 1 ? "Deleted asset" : $"Deleted {Selection.Ids.Count} assets");
            }
            else if (Selection.Kind == SelectionKind.Display)
            {
                var drop = Selection.Ids.ToHashSet();
                show.Displays = show.Displays.Where(d => !drop.Contains(d.Id)).ToList();
            }
            Selection = new Selection();
        });
    }

    public void NudgeSelected(double dx, double dy)
    {
        if (dx == 0 && dy == 0) return;
        if (Selection.Kind == SelectionKind.Cue)
        {
            Mutate(show =>
            {
                foreach (var cue in SelectedCues(show))
                {
                    if (CueOnLockedLayer(show, cue)) continue;
                    cue.Position.X += dx;
                    cue.Position.Y += dy;
                }
            });
        }
        else if (Selection.Kind == SelectionKind.Display)
        {
            Mutate(show =>
            {
                foreach (var display in show.Displays.Where(d => Selection.Ids.Contains(d.Id)))
                {
                    display.X += dx;
                    display.Y += dy;
                }
            });
        }
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
                    if (CueOnLockedLayer(show, cue)) continue;
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

    public void GroupSelectedCues()
    {
        if (Show is null) return;
        var tl = ActiveTimeline;
        if (tl is null) return;
        var cues = tl.Cues.Where(c => Selection.Kind == SelectionKind.Cue && Selection.Ids.Contains(c.Id)).ToList();
        if (cues.Count < 2)
        {
            Log("Select two or more cues on the same timeline, then Group", "warn");
            return;
        }

        Mutate(show =>
        {
            var timeline = ActiveTimelineOf(show);
            if (timeline is null) return;
            var selected = timeline.Cues.Where(c => Selection.Ids.Contains(c.Id)).ToList();
            if (selected.Count < 2) return;
            var start = selected.Min(c => c.Start);
            var end = selected.Max(c => c.Start + c.Duration);
            var originX = selected.Min(c => c.Position.X);
            var originY = selected.Min(c => c.Position.Y);
            var children = new List<Cue>();
            double maxR = 0, maxB = 0;
            foreach (var cue in selected)
            {
                var child = ShowSerializer.LoadCue(ShowSerializer.SaveCue(cue));
                child.Start -= start;
                child.Position = new Vec3 { X = cue.Position.X - originX, Y = cue.Position.Y - originY, Z = cue.Position.Z };
                children.Add(child);
                var asset = show.Assets.FirstOrDefault(a => a.Id == cue.AssetId);
                var rect = StageGeometry.CueRect(cue, asset);
                maxR = Math.Max(maxR, rect.X + rect.W - originX);
                maxB = Math.Max(maxB, rect.Y + rect.H - originY);
            }

            var group = ShowFactory.EmptyAsset(new Asset
            {
                Name = "Group",
                Kind = AssetKind.Composition,
                Codec = "Group",
                Width = Math.Max(16, maxR),
                Height = Math.Max(16, maxB),
                Duration = Math.Max(1, end - start),
                Color = "#a78bfa",
                Notes = $"{children.Count} cues grouped — Ungroup to explode them back onto the timeline",
                Children = children,
            });
            show.Assets.Add(group);
            foreach (var cue in selected)
                timeline.Cues.Remove(cue);
            var parent = ShowFactory.EmptyCue(new Cue
            {
                Name = "Group",
                Type = CueType.Media,
                LayerId = selected[0].LayerId,
                Start = start,
                Duration = Math.Max(1, end - start),
                AssetId = group.Id,
                Color = "#a78bfa",
                Position = new Vec3 { X = originX, Y = originY },
            });
            timeline.Cues.Add(parent);
            Selection = new Selection { Kind = SelectionKind.Cue, Ids = [parent.Id] };
        });
        Log("Grouped selected cues into a composition. Ungroup restores the children.");
    }

    public void UngroupSelected()
    {
        if (Show is null) return;
        var exploded = false;
        Mutate(show =>
        {
            var timeline = ActiveTimelineOf(show);
            if (timeline is null) return;
            var parent = timeline.Cues.FirstOrDefault(c => Selection.Kind == SelectionKind.Cue && Selection.Ids.Contains(c.Id));
            if (parent is null) return;
            var asset = show.Assets.FirstOrDefault(a => a.Id == parent.AssetId);
            if (asset is not { Kind: AssetKind.Composition } || asset.Children.Count == 0)
                return;
            var ids = new List<string>();
            foreach (var child in asset.Children)
            {
                var copy = ShowSerializer.CloneCue(child);
                copy.Start += parent.Start;
                copy.Position = new Vec3
                {
                    X = parent.Position.X + child.Position.X * (parent.Scale.X / 100),
                    Y = parent.Position.Y + child.Position.Y * (parent.Scale.Y / 100),
                    Z = parent.Position.Z + child.Position.Z,
                };
                if (string.IsNullOrEmpty(copy.LayerId) || timeline.Layers.All(l => l.Id != copy.LayerId))
                    copy.LayerId = parent.LayerId;
                timeline.Cues.Add(copy);
                ids.Add(copy.Id);
            }
            timeline.Cues.Remove(parent);
            if (show.Timelines.SelectMany(t => t.Cues).All(c => c.AssetId != asset.Id))
                show.Assets.Remove(asset);
            Selection = new Selection { Kind = SelectionKind.Cue, Ids = ids };
            exploded = true;
        });
        if (exploded) Log("Ungrouped composition — children are cues on the timeline again.");
        else Log("Select a grouped composition cue, then Ungroup", "warn");
    }

    public bool WakeNode(string? nodeId = null)
    {
        if (Show is null) return false;
        var node = nodeId is not null
            ? Show.Nodes.FirstOrDefault(n => n.Id == nodeId)
            : Selection.Kind == SelectionKind.Display
                ? Show.Nodes.FirstOrDefault(n => n.Id == Show.Displays.FirstOrDefault(d => Selection.Ids.Contains(d.Id))?.NodeId)
                : Show.Nodes.FirstOrDefault(n => n.Services.Runner);
        node ??= Show.Nodes.FirstOrDefault();
        if (node is null)
        {
            Log("No display node to wake", "warn");
            return false;
        }
        if (!WakeOnLan.Send(node.MacAddress))
        {
            Log($"Wake on LAN needs a MAC on {node.Name} — set it in Properties", "warn");
            return false;
        }
        Log($"Sent Wake-on-LAN magic packet to {node.Name} ({node.MacAddress})");
        return true;
    }

    public void UpdateNode(string id, Action<ShowNode> patch) =>
        Mutate(show =>
        {
            var n = show.Nodes.FirstOrDefault(x => x.Id == id);
            if (n is not null) patch(n);
        });

    public void SetColorSpace(ColorSpaceTag space) =>
        Mutate(show =>
        {
            show.Prefs.ColorSpace = space;
        });

    public void SetHdrPipeline(bool hdr, int bitDepth = 10) =>
        Mutate(show =>
        {
            show.Prefs.HdrPipeline = hdr;
            show.Prefs.BitDepth = hdr ? Math.Max(10, bitDepth) : 8;
            if (hdr) show.Prefs.ColorSpace = ColorSpaceTag.Pq;
        });

    public void SetOptimizePreset(OptimizePreset preset) =>
        Mutate(show => show.Prefs.OptimizePreset = preset);

    public void SetNmosRegistry(string url) =>
        Mutate(show => show.Prefs.NmosRegistry = url ?? "");

    public void SetLtc(bool enabled, double fps = 30) =>
        Mutate(show =>
        {
            show.Prefs.LtcEnabled = enabled;
            show.Prefs.LtcFps = fps > 0 ? fps : 30;
        });

    public Asset ImportSt2110(string name, string sdp, string? nmosId = null, bool placeOnLayer = false)
    {
        if (Show is null) NewShow();
        var media = LiveSources.St2110Asset(name, sdp, nmosId);
        ApplyImported(media);
        Mutate(show =>
        {
            show.CaptureDevices = show.CaptureDevices
                .Where(d => d.Signal != media.Url)
                .Append(new CaptureDevice
                {
                    Id = Ids.New("cap"),
                    Name = media.Name,
                    NodeId = show.Nodes.FirstOrDefault(n => n.Services.Runner)?.Id ?? "local-runner",
                    Kind = "ST2110",
                    Signal = media.Url,
                })
                .ToList();
        }, record: false);
        if (placeOnLayer)
        {
            var hasCue = Show!.Timelines.SelectMany(t => t.Cues).Any(c => c.AssetId == media.Id);
            if (!hasCue)
            {
                string? layerId = null;
                Mutate(show => layerId = LiveSources.NextLiveLayerId(show), record: false);
                AddCueFromAsset(media.Id, layerId, 0);
            }
        }
        else
            Select(SelectionKind.Asset, media.Id);
        return Show!.Assets.First(a => a.Id == media.Id);
    }

    public int ImportNmosSenders(IEnumerable<NmosSender> senders, IReadOnlyDictionary<string, string>? sdpByHref = null)
    {
        var n = 0;
        foreach (var sender in senders)
        {
            var sdp = "";
            if (sdpByHref is not null && !string.IsNullOrEmpty(sender.ManifestHref) && sdpByHref.TryGetValue(sender.ManifestHref, out var body))
                sdp = body;
            if (string.IsNullOrWhiteSpace(sdp))
                sdp = $"v=0\nm=video 5004 RTP/AVP 96\na=rtpmap:96 raw/90000\na=fmtp:96 width=1920; height=1080; exactframerate=60\n";
            ImportSt2110(sender.Label, sdp, sender.Id);
            n++;
        }
        if (n > 0) Log($"Imported {n} NMOS sender(s) as ST 2110 assets");
        else Log("NMOS registry returned no senders", "warn");
        return n;
    }

    public void ChaseLtc(LtcStamp stamp)
    {
        var tl = ActiveTimeline;
        if (tl is null) return;
        SetPlayhead(tl.Id, stamp.Milliseconds);
        Log($"LTC chase {stamp.Hours:00}:{stamp.Minutes:00}:{stamp.Seconds:00}:{stamp.Frames:00}");
    }

    public byte[] ExportLtcWav(double durationMs, int sampleRate = 48000)
    {
        var tl = ActiveTimeline;
        var fps = Show?.Prefs.LtcFps > 0 ? Show.Prefs.LtcFps : 30;
        var start = LtcStamp.FromMilliseconds(tl?.Playhead ?? 0, fps);
        var frames = Math.Max(1, (int)Math.Ceiling((durationMs <= 0 ? 1000 : durationMs) / (1000d / fps)));
        Log($"Exported {frames} frames of LTC at {fps:0} fps");
        return Ltc.Wav(start, frames, sampleRate, fps);
    }

    public void MarkNode(string id, bool online, NodeKind? kind = null) =>
        Mutate(show =>
        {
            var node = show.Nodes.FirstOrDefault(n => n.Id == id);
            if (node is null) return;
            node.Online = online;
            if (kind is { } k) node.Kind = k;
        }, record: false);

    public string NodeShowPayload() => Show is null ? "{}" : ShowSerializer.Save(Show);

    public void PushAssetRevision(string assetId, string url, string? proxyPath, string notes)
    {
        Mutate(show =>
        {
            var asset = show.Assets.FirstOrDefault(a => a.Id == assetId);
            if (asset is null) return;
            asset.Revisions ??= [];
            if (asset.Revisions.Count == 0 && !string.IsNullOrEmpty(asset.Url))
            {
                asset.Revisions.Add(new AssetRevision
                {
                    Id = Ids.New("rev"),
                    Url = asset.Url,
                    ProxyPath = asset.ProxyPath,
                    OriginalPath = asset.OriginalPath,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    Notes = "Original",
                });
            }
            var rev = new AssetRevision
            {
                Id = Ids.New("rev"),
                Url = url,
                ProxyPath = proxyPath,
                OriginalPath = asset.OriginalPath,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                Notes = notes,
            };
            asset.Revisions.Add(rev);
            ApplyRevision(asset, rev);
        });
        Log(notes);
    }

    public void ActivateRevision(string assetId, string revisionId)
    {
        Mutate(show =>
        {
            var asset = show.Assets.FirstOrDefault(a => a.Id == assetId);
            var rev = asset?.Revisions.FirstOrDefault(r => r.Id == revisionId);
            if (asset is null || rev is null) return;
            ApplyRevision(asset, rev);
        });
        Log("Switched asset revision — cues keep the same slot");
    }

    static void ApplyRevision(Asset asset, AssetRevision rev)
    {
        asset.ActiveRevisionId = rev.Id;
        asset.Url = rev.Url;
        if (!string.IsNullOrEmpty(rev.ProxyPath)) asset.ProxyPath = rev.ProxyPath;
        asset.Dynamic = true;
        asset.Notes = rev.Notes;
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

    public void LiveUpdateDisplay(string id, Action<Display> patch)
    {
        if (Show is null) return;
        var display = Show.Displays.FirstOrDefault(d => d.Id == id);
        if (display is null) return;
        var x = display.X;
        var y = display.Y;
        var w = display.Width;
        var h = display.Height;
        patch(display);
        if (x == display.X && y == display.Y && w == display.Width && h == display.Height)
            return;
        Show.ModifiedAt = DateTime.UtcNow.ToString("o");
        _liveLayoutDirty = true;
        LayoutChanged?.Invoke();
    }

    public void MapScreens(IReadOnlyList<OutputScreen> screens, bool includePrimary = false)
    {
        Mutate(show => show.Displays = ScreenAssign.LayoutDisplaysOnScreens(show.Displays, screens, includePrimary));
        FrameDisplays();
        Log(includePrimary
            ? "Copied every monitor, including the Producer laptop, onto the Stage."
            : "Assigned extra screens (LED processor, Colorlight, NovaStar, TV, projector) onto the Stage and copied their size and layout.");
        WarnWindowsModeMismatch(screens);
    }

    public void AssignDisplayScreen(string displayId, string? key, OutputScreen? screen = null)
    {
        string? name = null;
        var copied = false;
        Mutate(show =>
        {
            var d = show.Displays.FirstOrDefault(x => x.Id == displayId);
            if (d is null) return;
            ScreenAssign.ApplyAssignment(d, key);
            name = d.Name;
            if (screen is not null && !ScreenAssign.IsAutoKey(key))
            {
                ScreenAssign.CopyScreenSize(d, screen);
                copied = true;
            }
        });
        if (copied) FrameDisplays();
        if (name is null) return;
        if (ScreenAssign.IsAutoKey(key))
        {
            Log($"{name} uses {ScreenAssign.AutoChoiceLabel(Show?.Displays.FirstOrDefault(d => d.Id == displayId)?.Channel ?? 1)}");
            return;
        }
        if (screen is not null && copied)
        {
            var w = ScreenAssign.ScreenWidth(screen);
            var h = ScreenAssign.ScreenHeight(screen);
            Log($"{name} is pinned to {screen.Label} at {w}×{h} — Output sends it there");
            WarnWindowsModeMismatch([screen]);
        }
        else
            Log($"{name} is pinned to a specific screen — Output sends it there");
    }

    public void CopyScreenSizeToDisplay(string? displayId, OutputScreen screen)
    {
        displayId ??= Selection.Kind == SelectionKind.Display ? Selection.Ids.FirstOrDefault() : null;
        displayId ??= Show?.Displays.FirstOrDefault()?.Id;
        if (displayId is null)
        {
            Log("Select a Display on Stage first, then Use size to copy that controller onto it", "warn");
            return;
        }
        string? name = null;
        var w = ScreenAssign.ScreenWidth(screen);
        var h = ScreenAssign.ScreenHeight(screen);
        Mutate(show =>
        {
            var d = show.Displays.FirstOrDefault(x => x.Id == displayId);
            if (d is null) return;
            ScreenAssign.CopyScreenSize(d, screen);
            d.ScreenId = screen.Id;
            name = d.Name;
        });
        FrameDisplays();
        if (name is not null)
            Log($"Copied {screen.Label} {w}×{h} onto {name}");
        WarnWindowsModeMismatch([screen]);
    }

    void WarnWindowsModeMismatch(IEnumerable<OutputScreen> screens)
    {
        foreach (var screen in screens.Where(ScreenAssign.WindowsModeDiffersFromController))
        {
            Log($"Stage on {screen.Label} is {ScreenAssign.ScreenWidth(screen)}×{ScreenAssign.ScreenHeight(screen)} (Windows mode). Controller EDID is {screen.PhysicalWidth}×{screen.PhysicalHeight}.", "info");
        }
    }

    public AudioDevice ActiveAudioDevice =>
        Show?.AudioDevices.FirstOrDefault() ?? AudioAssign.DefaultSpeaker();

    public void SetAudioOutput(AudioDevice device)
    {
        Mutate(show =>
        {
            show.AudioDevices =
            [
                new AudioDevice
                {
                    Id = string.IsNullOrEmpty(device.Id) ? AudioAssign.DefaultId : device.Id,
                    Name = string.IsNullOrEmpty(device.Name) ? "Windows default speaker" : device.Name,
                    NodeId = "local-runner",
                    Channels = device.Channels > 0 ? device.Channels : 2,
                    Driver = string.IsNullOrEmpty(device.Driver) ? "WASAPI" : device.Driver,
                },
            ];
        });
        Log($"Audio → {ActiveAudioDevice.Name} · {AudioAssign.StatusLine(ActiveAudioDevice)}");
    }

    public void AddTimeline()
    {
        Mutate(show =>
        {
            var tl = ShowFactory.EmptyTimeline($"Timeline {show.Timelines.Count + 1}");
            show.Timelines.Add(tl);
            ActiveTimelineId = tl.Id;
            Selection = new Selection { Kind = SelectionKind.Timeline, Ids = [tl.Id] };
        });
    }

    public void DeleteTimeline(string? id = null)
    {
        id ??= Selection.Kind == SelectionKind.Timeline ? Selection.Ids.FirstOrDefault() : ActiveTimelineId;
        if (Show is null || id is null) return;
        if (!TimelineMath.CanRemoveTimeline(Show.Timelines.Count))
        {
            Log("Keep at least one timeline", "warn");
            return;
        }
        if (Show.Timelines.All(t => t.Id != id)) return;
        Mutate(show =>
        {
            show.Timelines = TimelineMath.RemoveTimelinesById(show.Timelines, [id], t => t.Id);
            if (ActiveTimelineId == id)
                ActiveTimelineId = show.Timelines[0].Id;
            Selection = new Selection();
        });
        Log("Deleted timeline");
    }

    public void UpdateTimeline(string id, Action<Timeline> patch) =>
        Mutate(show =>
        {
            var tl = show.Timelines.FirstOrDefault(t => t.Id == id);
            if (tl is not null) patch(tl);
        });

    public void SetLoop(string? id, bool loop)
    {
        id ??= ActiveTimelineId;
        if (id is null) return;
        UpdateTimeline(id, t => t.Loop = loop);
        Log(loop ? "Loop on — play repeats this timeline" : "Loop off — play stops at the end");
    }

    public void ToggleLoop(string? id = null)
    {
        var tl = id is null ? ActiveTimeline : Show?.Timelines.FirstOrDefault(t => t.Id == id);
        if (tl is null) return;
        SetLoop(tl.Id, !tl.Loop);
    }

    public bool FitTimelineToMedia(string? timelineId = null)
    {
        var tl = timelineId is null ? ActiveTimeline : Show?.Timelines.FirstOrDefault(t => t.Id == timelineId);
        if (tl is null) return false;
        var end = TimelineMath.ContentEnd(tl.Cues);
        if (end <= 0)
        {
            Log("No clips on this timeline to fit — drop media on a layer first", "warn");
            return false;
        }
        var duration = TimelineMath.FitDuration(end);
        UpdateTimeline(tl.Id, t =>
        {
            t.Duration = duration;
            if (t.Playhead >= duration) t.Playhead = 0;
        });
        TimelineScroll = 0;
        ClampTimelineView();
        Log($"Fit to media — timeline length is {TimeFormat.FormatPlayTime(duration)}");
        return true;
    }

    public void AddLayer()
    {
        Mutate(show =>
        {
            var tl = ActiveTimelineOf(show);
            if (tl is null) return;
            var layer = ShowFactory.EmptyLayer($"Layer {tl.Layers.Count + 1}", tl.Layers.Count + 1);
            tl.Layers.Add(layer);
            Selection = new Selection { Kind = SelectionKind.Layer, Ids = [layer.Id] };
            ApplyRevealLayer(tl.Layers.Count - 1);
        });
    }

    public void InsertLayer(string? afterId = null)
    {
        Mutate(show =>
        {
            var tl = ActiveTimelineOf(show);
            if (tl is null) return;
            afterId ??= Selection.Kind == SelectionKind.Layer ? Selection.Ids.FirstOrDefault() : null;
            var at = TimelineMath.InsertLayerIndex(tl.Layers, afterId);
            var layer = ShowFactory.EmptyLayer($"Layer {tl.Layers.Count + 1}", at + 1);
            tl.Layers.Insert(at, layer);
            Selection = new Selection { Kind = SelectionKind.Layer, Ids = [layer.Id] };
            ApplyRevealLayer(at);
        });
    }

    public void DeleteLayer(string? id = null)
    {
        id ??= Selection.Kind == SelectionKind.Layer ? Selection.Ids.FirstOrDefault() : ActiveTimeline?.Layers.LastOrDefault()?.Id;
        if (id is null) return;
        var tl = ActiveTimeline;
        if (tl is null) return;
        if (!TimelineMath.CanRemoveLayer(tl.Layers.Count))
        {
            Log("Keep at least one layer", "warn");
            return;
        }
        if (tl.Layers.All(l => l.Id != id)) return;
        Mutate(show =>
        {
            var live = ActiveTimelineOf(show);
            if (live is null) return;
            var next = TimelineMath.RemoveLayer(live.Layers, live.Cues, id);
            if (next is null) return;
            live.Layers = next.Value.Layers;
            live.Cues = next.Value.Cues;
            if (Selection.Kind == SelectionKind.Layer && Selection.Ids.Contains(id))
                Selection = new Selection();
            ClampTimelineView();
        });
        Log("Deleted layer");
    }

    public void UpdateLayer(string id, Action<Layer> patch) =>
        Mutate(show =>
        {
            var layer = show.Timelines.SelectMany(t => t.Layers).FirstOrDefault(l => l.Id == id);
            if (layer is not null) patch(layer);
        });

    public void ToggleLayerVisible(string id) =>
        UpdateLayer(id, l => l.Enabled = !l.Enabled);

    public void ToggleLayerLocked(string id) =>
        UpdateLayer(id, l => l.Locked = !l.Locked);

    public bool CueLayerLocked(string cueId)
    {
        if (Show is null) return false;
        foreach (var tl in Show.Timelines)
        {
            var cue = tl.Cues.FirstOrDefault(c => c.Id == cueId);
            if (cue is null) continue;
            return TimelineMath.LayerIsLocked(tl.Layers, cue.LayerId);
        }
        return false;
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

    public Asset ConnectCapture(string deviceId, string name, int width = 1920, int height = 1080, bool announce = true, string? displayId = null)
    {
        if (Show is null) NewShow();
        var existing = Show!.Assets.FirstOrDefault(a => LiveSources.CaptureDeviceId(a) == deviceId);
        var media = LiveSources.CaptureAsset(deviceId, name, width, height);
        if (existing is not null) media.Id = existing.Id;
        ApplyImported(media);
        Mutate(show =>
        {
            var prev = show.CaptureDevices.FirstOrDefault(d => d.Signal == deviceId);
            show.CaptureDevices = show.CaptureDevices
                .Where(d => d.Signal != deviceId)
                .Append(new CaptureDevice
                {
                    Id = prev?.Id ?? Ids.New("cap"),
                    Name = name,
                    NodeId = "local-runner",
                    Kind = "HDMI",
                    Signal = deviceId,
                    DisplayId = LiveSources.IsAutoDisplay(displayId) ? prev?.DisplayId : displayId,
                })
                .ToList();
        }, record: false);
        var hasCue = Show.Timelines.SelectMany(t => t.Cues).Any(c => c.AssetId == media.Id);
        var displaysBefore = Show.Displays.Count;
        if (!hasCue)
        {
            string? targetId = null;
            string? layerId = null;
            Mutate(show =>
            {
                var target = LiveSources.ResolveCaptureDisplay(show, displayId ?? show.CaptureDevices.FirstOrDefault(d => d.Signal == deviceId)?.DisplayId);
                targetId = target.Id;
                layerId = LiveSources.NextCaptureLayerId(show);
                var rec = show.CaptureDevices.FirstOrDefault(d => d.Signal == deviceId);
                if (rec is not null) rec.DisplayId = target.Id;
            }, record: false);
            AddCueFromAsset(media.Id, layerId, 0, targetId);
        }
        else if (!LiveSources.IsAutoDisplay(displayId))
            AssignCaptureToDisplay(deviceId, displayId, name);
        if (announce)
        {
            var dest = Show.Displays.FirstOrDefault(d => d.Id == (displayId ?? Show.CaptureDevices.FirstOrDefault(c => c.Signal == deviceId)?.DisplayId));
            Log(dest is null
                ? $"Live capture connected: {name} — play Resolume (or any HDMI/SDI source) into this card"
                : $"Live capture {name} → {dest.Name} on Stage");
            if (Show.Displays.Count > displaysBefore) FrameDisplays();
        }
        return Show.Assets.First(a => a.Id == media.Id);
    }

    public void AssignCaptureToDisplay(string deviceId, string? displayKey, string? name = null)
    {
        if (Show is null) return;
        name ??= Show.CaptureDevices.FirstOrDefault(d => d.Signal == deviceId)?.Name
                 ?? Show.Assets.FirstOrDefault(a => LiveSources.CaptureDeviceId(a) == deviceId)?.Name
                 ?? deviceId;
        if (LiveSources.IsAutoDisplay(displayKey))
        {
            Mutate(show =>
            {
                var rec = show.CaptureDevices.FirstOrDefault(d => d.Signal == deviceId);
                if (rec is not null) rec.DisplayId = null;
            }, record: false);
            return;
        }
        var display = Show.Displays.FirstOrDefault(d => d.Id == displayKey);
        if (display is null)
        {
            Log("Pick a Stage display for that capture card", "warn");
            return;
        }
        if (Show.Assets.All(a => LiveSources.CaptureDeviceId(a) != deviceId))
        {
            ConnectCapture(deviceId, name, displayId: display.Id);
            return;
        }
        var asset = Show.Assets.First(a => LiveSources.CaptureDeviceId(a) == deviceId);
        var cue = LiveSources.CaptureCue(Show, deviceId);
        if (cue is null)
        {
            var layerId = LiveSources.NextCaptureLayerId(Show);
            AddCueFromAsset(asset.Id, layerId, 0, display.Id);
        }
        else
        {
            UpdateCue(cue.Id, c => LiveSources.FitCueToDisplay(c, asset, display));
        }
        Mutate(show =>
        {
            var rec = show.CaptureDevices.FirstOrDefault(d => d.Signal == deviceId);
            if (rec is not null) rec.DisplayId = display.Id;
            else
                show.CaptureDevices.Add(new CaptureDevice
                {
                    Id = Ids.New("cap"),
                    Name = name,
                    NodeId = "local-runner",
                    Kind = "HDMI",
                    Signal = deviceId,
                    DisplayId = display.Id,
                });
        }, record: false);
        Log($"{name} → {display.Name} on Stage");
    }

    public Asset ImportNdi(string sourceName, string? captureDeviceId = null, bool placeOnLayer = false, bool announce = true)
    {
        if (Show is null) NewShow();
        var name = NdiNames.FriendlyName(sourceName);
        var existing = Show!.Assets.FirstOrDefault(a =>
                           captureDeviceId is not null && LiveSources.CaptureDeviceId(a) == captureDeviceId)
                       ?? Show.Assets.FirstOrDefault(a => a.Kind == AssetKind.Ndi && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                       ?? Show.Assets.FirstOrDefault(a => a.Kind == AssetKind.Ndi && a.Url.StartsWith("procedural:", StringComparison.OrdinalIgnoreCase));
        var media = LiveSources.NdiAsset(name, captureDeviceId);
        if (existing is not null) media.Id = existing.Id;
        ApplyImported(media);
        if (captureDeviceId is not null)
        {
            Mutate(show =>
            {
                show.CaptureDevices = show.CaptureDevices
                    .Where(d => d.Signal != captureDeviceId)
                    .Append(new CaptureDevice
                    {
                        Id = Ids.New("cap"),
                        Name = name,
                        NodeId = "local-runner",
                        Kind = "NDI",
                        Signal = captureDeviceId,
                    })
                    .ToList();
            }, record: false);
        }
        if (placeOnLayer)
        {
            var hasCue = Show.Timelines.SelectMany(t => t.Cues).Any(c => c.AssetId == media.Id);
            if (!hasCue)
            {
                string? layerId = null;
                Mutate(show => layerId = LiveSources.NextLiveLayerId(show), record: false);
                AddCueFromAsset(media.Id, layerId, 0);
            }
            if (announce)
                Log(captureDeviceId is null
                    ? $"NDI {name} is live on a timeline layer"
                    : $"NDI {name} is on a timeline layer. Press Space if you need the clock running.");
        }
        else
        {
            Select(SelectionKind.Asset, media.Id);
            if (announce)
                Log($"NDI {name} is in Assets. Drag it onto a timeline layer like a video.");
        }
        return Show.Assets.First(a => a.Id == media.Id);
    }

    public Asset ConnectNdi(string sourceName, string? captureDeviceId = null, bool announce = true) =>
        ImportNdi(sourceName, captureDeviceId, placeOnLayer: true, announce);

    public int ConnectCaptures(IEnumerable<(string Id, string Name)> devices)
    {
        var list = devices.ToList();
        if (list.Count == 0) return 0;
        if (Show is null) NewShow();
        var existing = Show!.Displays.Select(d => d.Id).ToList();
        var i = 0;
        foreach (var device in list)
        {
            var displayId = i < existing.Count ? existing[i] : null;
            ConnectCapture(device.Id, device.Name, announce: false, displayId: displayId);
            i++;
        }
        FrameDisplays();
        if (list.Count == 1)
            Log($"Live capture connected: {list[0].Name} — play Resolume (or any HDMI/SDI source) into this card");
        else
            Log($"Connected {list.Count} capture cards across {Show.Displays.Count} display(s). Card 1 → Display 1, card 2 → Display 2, … — remap any card in Devices or click a Stage display.");
        return list.Count;
    }

    public void NoteLiveFrameSize(string? ndiName, string? deviceId, int width, int height)
    {
        if (Show is null || width < 2 || height < 2) return;
        if (!Show.Assets.Any(a => LiveFrameMatches(a, ndiName, deviceId)
                                 && ((int)a.Width != width || (int)a.Height != height)))
            return;
        Mutate(show =>
        {
            foreach (var asset in show.Assets.Where(a => LiveFrameMatches(a, ndiName, deviceId)))
            {
                if ((int)asset.Width == width && (int)asset.Height == height) continue;
                var oldW = asset.Width > 0 ? asset.Width : 1920;
                var oldH = asset.Height > 0 ? asset.Height : 1080;
                foreach (var cue in show.Timelines.SelectMany(t => t.Cues).Where(c => c.AssetId == asset.Id))
                {
                    var keep = LivePicture.KeepCueScale(oldW, oldH, cue.Scale.X, cue.Scale.Y, width, height);
                    cue.Scale = new Vec2 { X = keep.ScaleX, Y = keep.ScaleY };
                }
                asset.Width = width;
                asset.Height = height;
            }
        }, record: false);
    }

    static bool LiveFrameMatches(Asset asset, string? ndiName, string? deviceId)
    {
        if (!string.IsNullOrEmpty(deviceId) && LiveSources.CaptureDeviceId(asset) == deviceId)
            return true;
        if (string.IsNullOrEmpty(ndiName)) return false;
        var source = LiveSources.NdiSourceName(asset);
        if (source is not null && string.Equals(source, ndiName, StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(asset.Name, NdiNames.FriendlyName(ndiName), StringComparison.OrdinalIgnoreCase);
    }

    public void UpdateAsset(string id, Action<Asset> patch) =>
        Mutate(show =>
        {
            var a = show.Assets.FirstOrDefault(x => x.Id == id);
            if (a is not null) patch(a);
        }, record: false);

    public void DeleteAsset(string? id = null)
    {
        id ??= Selection.Kind == SelectionKind.Asset ? Selection.Ids.FirstOrDefault() : null;
        if (Show is null || id is null) return;
        if (Show.Assets.All(a => a.Id != id))
        {
            Log("Select an imported clip in Assets, then Delete", "warn");
            return;
        }
        Mutate(show =>
        {
            TimelineMath.PurgeAssets(show, [id]);
            Selection = new Selection();
        });
        Log("Deleted asset — it is gone from Assets and from the Timeline");
    }

    static Layer? PickEditableLayer(Models.Timeline tl, string? layerId) =>
        layerId is not null
            ? tl.Layers.FirstOrDefault(l => l.Id == layerId && !l.Locked)
            : tl.Layers.FirstOrDefault(l => l.Enabled && !l.Locked);

    bool RefuseLockedLayer(Models.Timeline? tl, string? layerId)
    {
        if (tl is null) return false;
        var layer = layerId is not null
            ? tl.Layers.FirstOrDefault(l => l.Id == layerId)
            : tl.Layers.FirstOrDefault(l => l.Enabled && !l.Locked) ?? tl.Layers.FirstOrDefault();
        if (layer is not { Locked: true }) return false;
        Log($"Layer \"{layer.Name}\" is locked", "warn");
        return true;
    }

    static bool CueOnLockedLayer(Models.Show show, Cue cue) =>
        TimelineMath.LayerIsLocked(show.Timelines.SelectMany(t => t.Layers), cue.LayerId);

    Timeline? ActiveTimelineOf(Models.Show show) =>
        show.Timelines.FirstOrDefault(t => t.Id == ActiveTimelineId) ?? show.Timelines.FirstOrDefault();

    IEnumerable<Cue> SelectedCues(Models.Show show) =>
        show.Timelines.SelectMany(t => t.Cues).Where(c => Selection.Kind == SelectionKind.Cue && Selection.Ids.Contains(c.Id));

    void ApplyRevealTime(double startMs, double endMs) =>
        TimelineScroll = TimelineMath.ScrollToShow(startMs, endMs, TimelineScroll, VisibleDurationMs());

    void ApplyRevealLayer(int index)
    {
        var lanesH = Math.Max(0, TimelineViewHeight - TimelineMath.RulerHeight);
        TimelineLayerScroll = TimelineMath.LayerScrollToShow(index, TimelineMath.LaneHeight, TimelineLayerScroll, lanesH);
    }

    void ClampTimelineView()
    {
        var tl = ActiveTimeline;
        if (tl is null)
        {
            TimelineScroll = 0;
            TimelineLayerScroll = 0;
            return;
        }
        TimelineScroll = TimelineMath.ClampScroll(TimelineScroll, tl.Duration, VisibleDurationMs());
        var lanesH = Math.Max(0, TimelineViewHeight - TimelineMath.RulerHeight);
        TimelineLayerScroll = TimelineMath.ClampLayerScroll(TimelineLayerScroll, tl.Layers.Count, TimelineMath.LaneHeight, lanesH);
    }

    void Mutate(Action<Models.Show> mutator, bool record = true)
    {
        if (Show is null) return;
        if (record) _history.Add(ShowSerializer.Save(Show));
        if (_history.Count > 50) _history.RemoveAt(0);
        if (record) _future.Clear();
        mutator(Show);
        Show.ModifiedAt = DateTime.UtcNow.ToString("o");
        _liveLayoutDirty = false;
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
