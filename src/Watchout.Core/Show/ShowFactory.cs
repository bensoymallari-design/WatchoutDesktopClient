using Watchout.Core.Models;
using Watchout.Core.Playback;

namespace Watchout.Core;

public static class ShowFactory
{
    public const string Version = Brand.Version;

    public static string DefaultCueColor(CueType type) => type switch
    {
        CueType.Media => "#3b82c4",
        CueType.Control => "#c084fc",
        CueType.Marker => "#fbbf24",
        CueType.Output => "#34d399",
        CueType.Variable => "#fb7185",
        CueType.Artnet => "#f97316",
        _ => "#3b82c4",
    };

    public static Layer EmptyLayer(string name, int index) => new()
    {
        Id = Ids.New("layer"),
        Name = string.IsNullOrEmpty(name) ? $"Layer {index}" : name,
        Enabled = true,
        Locked = false,
        Expanded = true,
    };

    public static Timeline EmptyTimeline(string name = "Main Timeline")
    {
        var layers = Enumerable.Range(1, 10).Select(i => EmptyLayer($"Layer {i}", i)).ToList();
        return new Timeline
        {
            Id = Ids.New("tl"),
            Name = name,
            Duration = 120_000,
            Playback = PlaybackState.Stop,
            Playhead = 0,
            Loop = true,
            Enabled = true,
            Rate = 1,
            Layers = layers,
            Cues = [],
        };
    }

    public static Display EmptyDisplay(Display? partial = null)
    {
        var d = partial ?? new Display();
        return new Display
        {
            Id = string.IsNullOrEmpty(d.Id) ? Ids.New("disp") : d.Id,
            Name = string.IsNullOrEmpty(d.Name) ? "Display 1" : d.Name,
            X = d.X,
            Y = d.Y,
            Z = d.Z,
            Width = d.Width > 0 ? d.Width : 1920,
            Height = d.Height > 0 ? d.Height : 1080,
            Rotation = d.Rotation,
            OutputType = d.OutputType,
            Channel = d.Channel > 0 ? d.Channel : 1,
            NodeId = string.IsNullOrEmpty(d.NodeId) ? "local-runner" : d.NodeId,
            Enabled = d.Enabled || partial is null,
            Blend = d.Blend,
            BlendWidth = d.BlendWidth > 0 ? d.BlendWidth : 128,
            Virtual = d.Virtual,
            ScreenId = d.ScreenId,
            MaskEnabled = d.MaskEnabled,
            MaskInvert = d.MaskInvert,
            MaskUrl = d.MaskUrl,
            Role = d.Role,
            KeyChannel = d.KeyChannel > 0 ? d.KeyChannel : 1,
            ColorSpace = d.ColorSpace,
        };
    }

    public static Asset EmptyAsset(Asset partial)
    {
        return new Asset
        {
            Id = string.IsNullOrEmpty(partial.Id) ? Ids.New("asset") : partial.Id,
            Name = partial.Name,
            Kind = partial.Kind,
            FolderId = partial.FolderId,
            Width = partial.Width > 0 ? partial.Width : 1920,
            Height = partial.Height > 0 ? partial.Height : 1080,
            Duration = partial.Duration > 0 ? partial.Duration : 10_000,
            Fps = partial.Fps > 0 ? partial.Fps : 60,
            Url = partial.Url,
            Codec = string.IsNullOrEmpty(partial.Codec) ? "H.264" : partial.Codec,
            Color = string.IsNullOrEmpty(partial.Color) ? "#3b82c4" : partial.Color,
            Optimized = true,
            Notes = partial.Notes,
            OriginalPath = partial.OriginalPath,
            ProxyPath = partial.ProxyPath,
            ProxyVersion = partial.ProxyVersion,
            Bytes = partial.Bytes,
            Linked = partial.Linked,
            PosterUrl = partial.PosterUrl,
            Dynamic = partial.Dynamic,
            ActiveRevisionId = partial.ActiveRevisionId,
            Revisions = partial.Revisions ?? [],
            Children = partial.Children ?? [],
        };
    }

    public static Cue EmptyCue(Cue partial)
    {
        var type = partial.Type;
        return new Cue
        {
            Id = string.IsNullOrEmpty(partial.Id) ? Ids.New("cue") : partial.Id,
            Type = type,
            Name = string.IsNullOrEmpty(partial.Name) ? "Cue" : partial.Name,
            LayerId = partial.LayerId,
            Start = partial.Start,
            Duration = partial.Duration > 0 ? partial.Duration : (type == CueType.Marker ? 0 : 5000),
            AssetId = partial.AssetId,
            Enabled = true,
            Color = string.IsNullOrEmpty(partial.Color) ? DefaultCueColor(type) : partial.Color,
            Position = partial.Position ?? new Vec3(),
            Scale = partial.Scale ?? new Vec2 { X = 100, Y = 100 },
            Rotation = partial.Rotation ?? new Vec3(),
            Opacity = partial.Opacity != 0 ? partial.Opacity : 100,
            Volume = partial.Volume > 0 ? partial.Volume : 100,
            Muted = partial.Muted ?? false,
            Blur = partial.Blur > 0 ? partial.Blur : 0.5,
            Brightness = partial.Brightness,
            Contrast = partial.Contrast,
            Saturation = partial.Saturation > 0 ? partial.Saturation : 100,
            Hue = partial.Hue,
            Crop = partial.Crop ?? new Crop(),
            Anchor = partial.Anchor ?? new Vec2 { X = 0.5, Y = 0.5 },
            FreeRunning = partial.FreeRunning,
            FadeIn = partial.FadeIn,
            FadeOut = partial.FadeOut,
            FadeInDuration = partial.FadeInDuration > 0 ? partial.FadeInDuration : 500,
            FadeOutDuration = partial.FadeOutDuration > 0 ? partial.FadeOutDuration : 500,
            FadeCurve = partial.FadeCurve,
            Tweens = partial.Tweens ?? [],
            Speed = partial.Speed > 0 ? partial.Speed : 100,
            WipeCompletion = partial.WipeCompletion,
            WipeAngle = partial.WipeAngle,
            WipeFeather = partial.WipeFeather,
            Temperature = partial.Temperature,
            Exposure = partial.Exposure,
            ChromaKeyEnabled = partial.ChromaKeyEnabled,
            ChromaKeyColor = string.IsNullOrEmpty(partial.ChromaKeyColor) ? "#00FF00" : partial.ChromaKeyColor,
            ChromaKeyTolerance = partial.ChromaKeyTolerance > 0 ? partial.ChromaKeyTolerance : 28,
            Control = partial.Control,
            Output = partial.Output,
            Variable = partial.Variable,
            Artnet = partial.Artnet,
        };
    }

    public static ShowNode LocalNode(ShowNode? partial = null)
    {
        var n = partial ?? new ShowNode();
        return new ShowNode
        {
            Id = string.IsNullOrEmpty(n.Id) ? "local-producer" : n.Id,
            Name = string.IsNullOrEmpty(n.Name) ? "localhost" : n.Name,
            Address = string.IsNullOrEmpty(n.Address) ? "127.0.0.1" : n.Address,
            Online = true,
            Services = n.Services.Producer || n.Services.Director || n.Services.Runner || n.Services.AssetManager
                ? n.Services
                : new NodeService { Producer = true, Director = true, Runner = true, AssetManager = true },
            Gpu = string.IsNullOrEmpty(n.Gpu) ? "Desktop GPU" : n.Gpu,
            Cpu = n.Cpu,
            GpuLoad = n.GpuLoad,
            Ram = n.Ram,
            Disk = n.Disk,
            Version = Version,
            MacAddress = n.MacAddress ?? "",
        };
    }

    public static Show EmptyShow(string name = "Untitled Show")
    {
        var now = DateTime.UtcNow.ToString("o");
        var timeline = EmptyTimeline();
        return new Show
        {
            Id = Ids.New("show"),
            Name = name,
            CreatedAt = now,
            ModifiedAt = now,
            Director = "localhost",
            AssetManager = "localhost",
            Prefs = new ShowPrefs { ColorSpace = ColorSpaceTag.Rec709 },
            Assets = [],
            Displays = [EmptyDisplay(new Display { Name = "Display 1", Width = 1920, Height = 1080, NodeId = "local-runner" })],
            Timelines = [timeline],
            Nodes =
            [
                LocalNode(new ShowNode
                {
                    Id = "local-producer",
                    Name = "Producer",
                    Services = new NodeService { Producer = true, Director = true, Runner = false, AssetManager = true },
                }),
                LocalNode(new ShowNode
                {
                    Id = "local-runner",
                    Name = "Runner-01",
                    Address = "127.0.0.1",
                    Services = new NodeService { Producer = false, Director = false, Runner = true, AssetManager = false },
                    Gpu = "DXVA / Media Foundation",
                    Cpu = 12,
                    GpuLoad = 8,
                }),
            ],
            AudioDevices = [new AudioDevice { Id = Ids.New("aud"), Name = "WASAPI Default", NodeId = "local-runner", Channels = 2, Driver = "WASAPI" }],
            CaptureDevices = [new CaptureDevice { Id = Ids.New("cap"), Name = "HDMI Capture", NodeId = "local-runner", Kind = "HDMI", Signal = "WatchMe-CAPTURE" }],
            Variables =
            [
                new ShowVariable { Id = Ids.New("var"), Name = "intensity", Value = 1, Min = 0, Max = 1, Protocol = "osc", Address = "/watchin/intensity" },
                new ShowVariable { Id = Ids.New("var"), Name = "show_mode", Value = 0, Min = 0, Max = 4, Protocol = "none", Address = "" },
            ],
            CueSets = [new CueSet { Id = Ids.New("set"), Name = "Default", CueIds = [], Enabled = true }],
        };
    }

    public static Show MakeDemoShow()
    {
        var show = EmptyShow("WatchMe Demo — LED Wall");
        show.Displays =
        [
            EmptyDisplay(new Display { Name = "LED Left", X = 0, Y = 0, Width = 1920, Height = 1080, Channel = 1, NodeId = "local-runner" }),
            EmptyDisplay(new Display { Name = "LED Center", X = 1920, Y = 0, Width = 1920, Height = 1080, Channel = 2, NodeId = "local-runner" }),
            EmptyDisplay(new Display { Name = "LED Right", X = 3840, Y = 0, Width = 1920, Height = 1080, Channel = 3, NodeId = "local-runner" }),
        ];

        var aurora = EmptyAsset(new Asset { Name = "Aurora Wash", Kind = AssetKind.Procedural, Codec = "Procedural", Duration = 30_000, Color = "#22d3ee", Url = "procedural:aurora", Notes = "Live generated aurora wash across the wall" });
        var bars = EmptyAsset(new Asset { Name = "Color Bars HDR", Kind = AssetKind.Image, Codec = "PNG", Duration = 8000, Color = "#fbbf24", Url = "watchme:demo/bars" });
        var title = EmptyAsset(new Asset { Name = "Show Title Card", Kind = AssetKind.Image, Codec = "PNG", Duration = 6000, Color = "#fb7185", Url = "watchme:demo/title" });
        var grid = EmptyAsset(new Asset { Name = "Pixel Grid", Kind = AssetKind.Image, Codec = "PNG", Duration = 10_000, Color = "#64748b", Url = "watchme:demo/grid" });
        var ndi = EmptyAsset(new Asset { Name = "NDI Program", Kind = AssetKind.Ndi, Codec = "NDI HX3", Duration = 60_000, Color = "#4ade80", Url = "procedural:ndi", Notes = "Placeholder until a capture device is connected" });
        var sting = EmptyAsset(new Asset { Name = "Impact Sting", Kind = AssetKind.Audio, Codec = "WAV 48k", Duration = 2500, Width = 0, Height = 0, Color = "#38bdf8", Url = "" });
        show.Assets = [aurora, bars, title, grid, ndi, sting];

        var main = show.Timelines[0];
        main.Duration = 45_000;
        main.Name = "Main Timeline";
        var bg = EmptyTimeline("Background Loop");
        bg.Duration = 30_000;
        bg.Loop = true;
        var control = EmptyTimeline("Show Control");
        control.Duration = 45_000;

        var l1 = main.Layers[0]; l1.Name = "Titles";
        var l2 = main.Layers[1]; l2.Name = "Full wall";
        var l3 = main.Layers[2]; l3.Name = "Overlays";
        var l4 = main.Layers[3]; l4.Name = "Live";

        Cue CueAt(string name, string layerId, double start, double duration, Asset asset, Action<Cue>? tweak = null)
        {
            var cue = EmptyCue(new Cue
            {
                Name = name,
                LayerId = layerId,
                Start = start,
                Duration = duration,
                AssetId = asset.Id,
                Color = asset.Color,
            });
            tweak?.Invoke(cue);
            return cue;
        }

        main.Cues =
        [
            CueAt("Title Card", l1.Id, 500, 7000, title, c =>
            {
                c.Position = new Vec3 { X = 1920 };
                c.FadeIn = true; c.FadeOut = true;
                c.FadeInDuration = 800; c.FadeOutDuration = 1000;
                c.Tweens =
                [
                    Tweens.MakeTween(TweenType.ScaleX, (0, 92, Easing.CubicOut), (1800, 100, Easing.CubicOut)),
                    Tweens.MakeTween(TweenType.ScaleY, (0, 92, Easing.CubicOut), (1800, 100, Easing.CubicOut)),
                ];
            }),
            CueAt("Color Bars", l1.Id, 6500, 8000, bars, c =>
            {
                c.Position = new Vec3 { X = 1920 };
                c.FadeIn = true; c.FadeOut = true;
                c.FadeInDuration = 1000; c.FadeOutDuration = 800;
            }),
            CueAt("Aurora Full Wall", l2.Id, 12_000, 18_000, aurora, c =>
            {
                c.Scale = new Vec2 { X = 300, Y = 100 };
                c.FadeIn = true; c.FadeOut = true;
                c.FadeInDuration = 800; c.FadeOutDuration = 800;
                c.Tweens = [Tweens.MakeTween(TweenType.PositionX, (0, -400, Easing.SineInOut), (18_000, 400, Easing.SineInOut))];
            }),
            CueAt("NDI Live", l4.Id, 0, 45_000, ndi, c =>
            {
                c.Position = new Vec3 { X = 3840 };
                c.FreeRunning = true;
            }),
            CueAt("End Grid", l1.Id, 13_700, 9000, grid, c =>
            {
                c.Position = new Vec3 { X = 1920 };
                c.FadeIn = true; c.FadeOut = true;
                c.FadeInDuration = 800; c.FadeOutDuration = 800;
            }),
            CueAt("Overlap A", l3.Id, 24_000, 5000, bars, c => c.Position = new Vec3 { X = 1920 }),
            CueAt("Overlap B", l3.Id, 27_000, 5000, grid, c => c.Position = new Vec3 { X = 1920 }),
            EmptyCue(new Cue { Type = CueType.Marker, Name = "Top of Show", LayerId = l4.Id, Start = 0, Duration = 0, Color = "#fbbf24" }),
            EmptyCue(new Cue { Type = CueType.Marker, Name = "Look B", LayerId = l4.Id, Start = 12_000, Duration = 0, Color = "#fbbf24" }),
            EmptyCue(new Cue
            {
                Type = CueType.Control, Name = "Play Background", LayerId = l4.Id, Start = 12_000, Duration = 200, Color = "#c084fc",
                Control = new CueControl { State = ControlState.Play, Target = ControlTarget.List, TimelineIds = [bg.Id], JumpMode = JumpMode.Time, JumpTime = 0 },
            }),
            EmptyCue(new Cue
            {
                Type = CueType.Output, Name = "HTTP GO", LayerId = l4.Id, Start = 6500, Duration = 100, Color = "#34d399",
                Output = new CueOutput { Protocol = "http", Address = "http://127.0.0.1:3012/go", Message = "{\"cue\":\"look-a\"}" },
            }),
        ];

        bg.Cues =
        [
            CueAt("Loop Wash", bg.Layers[2].Id, 0, 30_000, aurora, c =>
            {
                c.Opacity = 35;
                c.Scale = new Vec2 { X = 300, Y = 100 };
            }),
        ];

        control.Cues =
        [
            EmptyCue(new Cue
            {
                Type = CueType.Control, Name = "Start Main", LayerId = control.Layers[0].Id, Start = 0, Duration = 100, Color = "#c084fc",
                Control = new CueControl { State = ControlState.Play, Target = ControlTarget.List, TimelineIds = [main.Id], JumpMode = JumpMode.Time, JumpTime = 0 },
            }),
            EmptyCue(new Cue
            {
                Type = CueType.Artnet, Name = "House Lights Down", LayerId = control.Layers[1].Id, Start = 400, Duration = 4000, Color = "#f97316",
                Artnet = new CueArtnet { Universe = 1, StartChannel = 1, Values = [0, 0, 0] },
                Tweens = [Tweens.MakeTween(TweenType.Opacity, (0, 100, Easing.SineInOut), (4000, 0, Easing.SineInOut))],
            }),
        ];

        show.Timelines = [main, bg, control];
        show.CueSets =
        [
            new CueSet { Id = Ids.New("set"), Name = "Default", CueIds = main.Cues.Select(c => c.Id).ToList(), Enabled = true },
            new CueSet { Id = Ids.New("set"), Name = "Openers only", CueIds = main.Cues.Where(c => c.Start < 10_000).Select(c => c.Id).ToList(), Enabled = true },
        ];
        return show;
    }
}
