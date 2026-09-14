using Watchout.Core.Models;
using Watchout.Core.Stage;

namespace Watchout.Core.Media;

public static class LiveSources
{
    public const string CapturePrefix = "capture:";
    public const double LiveCueDurationMs = 24 * 60 * 60 * 1000;

    public static bool IsCapture(Asset? asset) =>
        asset is not null && (asset.Kind == AssetKind.Capture || IsCaptureUrl(asset.Url));

    public static bool IsNdi(Asset? asset) => NdiLive.IsNdi(asset);

    public static bool IsLive(Asset? asset) => IsCapture(asset) || IsNdi(asset);

    public static bool IsCaptureUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith(CapturePrefix, StringComparison.OrdinalIgnoreCase);

    public static string CaptureUrl(string deviceId) => CapturePrefix + deviceId;

    public static string? CaptureDeviceId(Asset? asset) => CaptureDeviceId(asset?.Url);

    public static string? CaptureDeviceId(string? url)
    {
        if (!IsCaptureUrl(url)) return null;
        return url![CapturePrefix.Length..];
    }

    public static ImportedMedia CaptureAsset(string deviceId, string name, int width = 1920, int height = 1080)
    {
        return new ImportedMedia
        {
            Id = Ids.New("asset"),
            Name = string.IsNullOrWhiteSpace(name) ? "Capture card" : name,
            Kind = AssetKind.Capture,
            Width = width > 0 ? width : 1920,
            Height = height > 0 ? height : 1080,
            Duration = LiveCueDurationMs,
            Fps = 60,
            Url = CaptureUrl(deviceId),
            Codec = "Capture card · live",
            Color = "#4ade80",
            Optimized = true,
            Notes = $"{name} · live HDMI/SDI capture · Resolume or any program output · Media Foundation",
            OriginalPath = CaptureUrl(deviceId),
        };
    }

    public static ImportedMedia NdiAsset(string sourceName, string? captureDeviceId, int width = 1920, int height = 1080)
    {
        var name = string.IsNullOrWhiteSpace(sourceName) ? "NDI Program" : NdiNames.FriendlyName(sourceName);
        var bound = !string.IsNullOrEmpty(captureDeviceId);
        return new ImportedMedia
        {
            Id = Ids.New("asset"),
            Name = name,
            Kind = AssetKind.Ndi,
            Width = width > 0 ? width : 1920,
            Height = height > 0 ? height : 1080,
            Duration = LiveCueDurationMs,
            Fps = 60,
            Url = bound ? CaptureUrl(captureDeviceId!) : NdiNames.NdiUrl(name),
            Codec = bound ? "NDI · Webcam Input" : "NDI",
            Color = "#4ade80",
            Optimized = true,
            Notes = bound
                ? $"{name} · NDI live via Webcam Input — drag onto a timeline layer"
                : $"{name} · NDI on the LAN. Drag onto a timeline layer. For picture, open NDI Webcam Input, pick this source, then Import NDI again.",
            OriginalPath = bound ? CaptureUrl(captureDeviceId!) : NdiNames.NdiUrl(name),
        };
    }

    public static IReadOnlyList<Cue> LiveCues(Show show) =>
        show.Timelines.SelectMany(t => t.Cues)
            .Where(c => c.AssetId is { } id && show.Assets.Any(a => a.Id == id && IsLive(a)))
            .ToList();

    public static IReadOnlyList<Cue> CaptureCues(Show show) =>
        show.Timelines.SelectMany(t => t.Cues)
            .Where(c => c.AssetId is { } id && show.Assets.Any(a => a.Id == id && IsCapture(a)))
            .ToList();

    public static string? NextCaptureLayerId(Show show) => NextLiveLayerId(show);

    public const string AutoDisplayKey = "auto";

    public static bool IsAutoDisplay(string? key) =>
        string.IsNullOrEmpty(key) || key == AutoDisplayKey;

    public static string DisplayChoiceLabel(Display display) =>
        $"{display.Name}  {display.Width:0}×{display.Height:0}";

    public static Cue? CaptureCue(Show show, string deviceId)
    {
        var asset = show.Assets.FirstOrDefault(a => CaptureDeviceId(a) == deviceId);
        if (asset is null) return null;
        return show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => c.AssetId == asset.Id);
    }

    public static string CaptureDisplayKey(Show show, string deviceId)
    {
        var rec = show.CaptureDevices.FirstOrDefault(d => d.Signal == deviceId);
        if (rec?.DisplayId is { Length: > 0 } id && show.Displays.Any(d => d.Id == id))
            return id;
        var cue = CaptureCue(show, deviceId);
        if (cue is null) return AutoDisplayKey;
        return StageGeometry.DisplayForCue(show.Displays, cue).Id;
    }

    public static string? CaptureOnDisplay(Show show, string displayId)
    {
        foreach (var rec in show.CaptureDevices.Where(d => d.Kind != "NDI"))
            if (CaptureDisplayKey(show, rec.Signal) == displayId)
                return rec.Signal;
        foreach (var asset in show.Assets.Where(IsCapture))
        {
            var id = CaptureDeviceId(asset);
            if (id is not null && CaptureDisplayKey(show, id) == displayId)
                return id;
        }
        return null;
    }

    public static void FitCueToDisplay(Cue cue, Asset asset, Display display)
    {
        var fit = StageGeometry.FitTransform(asset, display);
        cue.Position = fit.Position;
        cue.Scale = fit.Scale;
    }

    public static Display ResolveCaptureDisplay(Show show, string? displayId)
    {
        if (!string.IsNullOrEmpty(displayId) && displayId != AutoDisplayKey)
        {
            var pinned = show.Displays.FirstOrDefault(d => d.Id == displayId);
            if (pinned is not null) return pinned;
        }
        return EnsureDisplayForCapture(show);
    }

    public static string? NextLiveLayerId(Show show)
    {
        var tl = show.Timelines.FirstOrDefault();
        if (tl is null) return null;
        var used = LiveCues(show).Select(c => c.LayerId).ToHashSet();
        return tl.Layers.FirstOrDefault(l => l.Enabled && !l.Locked && !used.Contains(l.Id))?.Id
               ?? tl.Layers.FirstOrDefault(l => l.Enabled && !l.Locked)?.Id
               ?? tl.Layers.FirstOrDefault()?.Id;
    }

    public static Display EnsureDisplayForCapture(Show show) => EnsureDisplayForLive(show);

    public static Display EnsureDisplayForLive(Show show)
    {
        var occupied = new HashSet<string>();
        foreach (var cue in LiveCues(show))
        {
            var hit = StageGeometry.HitDisplay(show.Displays, (cue.Position.X + 16, cue.Position.Y + 16));
            if (hit is not null) occupied.Add(hit.Id);
        }
        var free = show.Displays.Where(d => d.Enabled).OrderBy(d => d.X).ThenBy(d => d.Channel)
            .FirstOrDefault(d => !occupied.Contains(d.Id));
        if (free is not null) return free;

        var last = show.Displays.OrderBy(d => d.X).LastOrDefault() ?? ShowFactory.EmptyDisplay();
        var created = ShowFactory.EmptyDisplay(new Display
        {
            Name = $"Display {show.Displays.Count + 1}",
            X = last.X + Math.Max(1, last.Width),
            Y = last.Y,
            Width = last.Width > 0 ? last.Width : 1920,
            Height = last.Height > 0 ? last.Height : 1080,
            Channel = show.Displays.Count + 1,
            NodeId = string.IsNullOrEmpty(last.NodeId) ? "local-runner" : last.NodeId,
        });
        show.Displays.Add(created);
        return created;
    }
}
