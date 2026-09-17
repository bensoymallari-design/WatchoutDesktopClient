using Watchout.Core.Models;
using Watchout.Core.Playback;

namespace Watchout.Core.Stage;

public readonly record struct StageRect(double X, double Y, double W, double H);

public readonly record struct StageHit(StageHitKind Kind, string? Id = null, string? Handle = null);

public static class StageGeometry
{
    public static StageRect DisplayRect(Display d) => new(d.X, d.Y, d.Width, d.Height);

    public static StageRect CueRect(EvaluatedCue ev, Asset? asset)
    {
        var aw = asset?.Width > 0 ? asset.Width : 1920;
        var ah = asset?.Height > 0 ? asset.Height : 1080;
        return new StageRect(ev.X, ev.Y, aw * (ev.ScaleX / 100), ah * (ev.ScaleY / 100));
    }

    public static StageRect CueRect(Cue cue, Asset? asset)
    {
        var aw = asset?.Width > 0 ? asset.Width : 1920;
        var ah = asset?.Height > 0 ? asset.Height : 1080;
        return new StageRect(cue.Position.X, cue.Position.Y, aw * (cue.Scale.X / 100), ah * (cue.Scale.Y / 100));
    }

    public static bool PointInRect((double X, double Y) pt, StageRect r) =>
        pt.X >= r.X && pt.X <= r.X + r.W && pt.Y >= r.Y && pt.Y <= r.Y + r.H;

    public static Display? HitDisplay(IReadOnlyList<Display> displays, (double X, double Y) pt)
    {
        for (var i = displays.Count - 1; i >= 0; i--)
        {
            var d = displays[i];
            if (d.Enabled && PointInRect(pt, DisplayRect(d))) return d;
        }
        return null;
    }

    public static Display? NearestDisplay(IReadOnlyList<Display> displays, (double X, double Y) pt, double maxDist = double.PositiveInfinity)
    {
        var inside = HitDisplay(displays, pt);
        if (inside is not null) return inside;
        Display? best = null;
        var bestDist = maxDist;
        foreach (var d in displays.Where(x => x.Enabled))
        {
            var r = DisplayRect(d);
            var cx = Math.Clamp(pt.X, r.X, r.X + r.W);
            var cy = Math.Clamp(pt.Y, r.Y, r.Y + r.H);
            var dx = pt.X - cx;
            var dy = pt.Y - cy;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist <= bestDist)
            {
                bestDist = dist;
                best = d;
            }
        }
        return best;
    }

    public static Display? DropTarget(IReadOnlyList<Display> displays, (double X, double Y) pt, bool snap, double snapDist)
    {
        var hit = HitDisplay(displays, pt);
        if (hit is not null) return hit;
        return snap ? NearestDisplay(displays, pt, snapDist) : null;
    }

    public static EvaluatedCue? HitCue(IReadOnlyList<EvaluatedCue> cues, IReadOnlyList<Asset> assets, (double X, double Y) pt)
    {
        var byId = assets.ToDictionary(a => a.Id);
        for (var i = cues.Count - 1; i >= 0; i--)
        {
            var ev = cues[i];
            if (ev.Cue.Type != CueType.Media) continue;
            Asset? asset = ev.Cue.AssetId is { } id && byId.TryGetValue(id, out var a) ? a : null;
            if (PointInRect(pt, CueRect(ev, asset))) return ev;
        }
        return null;
    }

    public static StageRect? WallRect(IEnumerable<Display> displays)
    {
        var live = displays.Where(d => d.Enabled).ToList();
        if (live.Count == 0) return null;
        var x = live.Min(d => d.X);
        var y = live.Min(d => d.Y);
        var right = live.Max(d => d.X + d.Width);
        var bottom = live.Max(d => d.Y + d.Height);
        return new StageRect(x, y, right - x, bottom - y);
    }

    /// <summary>Camera center + zoom so <paramref name="world"/> fills the Stage view with padding.</summary>
    public static (double X, double Y, double Zoom) FitCamera(StageRect world, double viewW, double viewH, double pad = 48)
    {
        viewW = Math.Max(64, viewW);
        viewH = Math.Max(64, viewH);
        var innerW = Math.Max(32, viewW - pad * 2);
        var innerH = Math.Max(32, viewH - pad * 2);
        var zoom = Math.Min(innerW / Math.Max(1, world.W), innerH / Math.Max(1, world.H));
        zoom = Math.Clamp(zoom, 0.03, 2);
        return (world.X + world.W / 2, world.Y + world.H / 2, zoom);
    }

    public static (double X, double Y, double Width, double Height)? WallAsBox(IEnumerable<Display> displays)
    {
        var wall = WallRect(displays);
        return wall is { } w ? (w.X, w.Y, w.W, w.H) : null;
    }

    public static (Vec3 Position, Vec2 Scale) FitTransform(Asset asset, Display display, string mode = "cover") =>
        FitTransform(asset.Width, asset.Height, display.X, display.Y, display.Width, display.Height, mode);

    public static (Vec3 Position, Vec2 Scale) FitTransform(
        double assetW, double assetH, double dx, double dy, double dw, double dh, string mode = "cover")
    {
        var aw = Math.Max(1, assetW > 0 ? assetW : 1920);
        var ah = Math.Max(1, assetH > 0 ? assetH : 1080);
        if (mode == "contain")
        {
            var factor = Math.Min(dw / aw, dh / ah);
            var w = aw * factor;
            var h = ah * factor;
            return (
                new Vec3 { X = Math.Round(dx + (dw - w) / 2), Y = Math.Round(dy + (dh - h) / 2), Z = 0 },
                new Vec2 { X = RoundHundredths(w / aw * 100), Y = RoundHundredths(h / ah * 100) });
        }
        return (
            new Vec3 { X = Math.Round(dx), Y = Math.Round(dy), Z = 0 },
            new Vec2 { X = RoundHundredths(dw / aw * 100), Y = RoundHundredths(dh / ah * 100) });
    }

    static double RoundHundredths(double n) => Math.Round(n * 100) / 100;

    public static double HandleHitPad(double zoom) => Math.Max(14, 18 / Math.Max(0.04, zoom));

    public static string? HitResizeHandle(StageRect rect, (double X, double Y) pt, double zoom)
    {
        var pad = Math.Min(HandleHitPad(zoom), Math.Max(8, Math.Min(rect.W, rect.H) / 4));
        var (x, y, w, h) = (rect.X, rect.Y, rect.W, rect.H);
        var nearL = Math.Abs(pt.X - x) <= pad;
        var nearR = Math.Abs(pt.X - (x + w)) <= pad;
        var nearT = Math.Abs(pt.Y - y) <= pad;
        var nearB = Math.Abs(pt.Y - (y + h)) <= pad;
        var inX = pt.X >= x - pad && pt.X <= x + w + pad;
        var inY = pt.Y >= y - pad && pt.Y <= y + h + pad;
        if (!inX || !inY) return null;
        if (nearT && nearL) return "nw";
        if (nearT && nearR) return "ne";
        if (nearB && nearL) return "sw";
        if (nearB && nearR) return "se";
        if (nearT) return "n";
        if (nearB) return "s";
        if (nearL) return "w";
        if (nearR) return "e";
        return null;
    }

    public static StageRect ResizeRect(StageRect start, string handle, double dx, double dy, bool keepAspect = false)
    {
        var x = start.X;
        var y = start.Y;
        var w = start.W;
        var h = start.H;
        if (handle.Contains('e')) w = start.W + dx;
        if (handle.Contains('s')) h = start.H + dy;
        if (handle.Contains('w'))
        {
            x = start.X + dx;
            w = start.W - dx;
        }
        if (handle.Contains('n'))
        {
            y = start.Y + dy;
            h = start.H - dy;
        }
        if (keepAspect && start.H > 0)
        {
            var aspect = start.W / start.H;
            var corner = handle.Length == 2;
            if (corner)
            {
                if (Math.Abs(dx) * start.H >= Math.Abs(dy) * start.W) h = w / aspect;
                else w = h * aspect;
                if (handle.Contains('w')) x = start.X + start.W - w;
                if (handle.Contains('n')) y = start.Y + start.H - h;
            }
            else if (handle is "e" or "w")
            {
                h = w / aspect;
                y = start.Y + (start.H - h) / 2;
                if (handle == "w") x = start.X + start.W - w;
            }
            else
            {
                w = h * aspect;
                x = start.X + (start.W - w) / 2;
                if (handle == "n") y = start.Y + start.H - h;
            }
        }
        const double min = 32;
        if (w < min)
        {
            if (handle.Contains('w')) x = start.X + start.W - min;
            w = min;
        }
        if (h < min)
        {
            if (handle.Contains('n')) y = start.Y + start.H - min;
            h = min;
        }
        return new StageRect(x, y, w, h);
    }

    public static StageRect SnapResizeRect(StageRect rect, string handle, IReadOnlyList<double> guidesX, IReadOnlyList<double> guidesY, double threshold)
    {
        var x = rect.X;
        var y = rect.Y;
        var w = rect.W;
        var h = rect.H;
        if (handle.Contains('w'))
        {
            var next = SnapValue(x, guidesX, threshold);
            w += x - next;
            x = next;
        }
        if (handle.Contains('e')) w = SnapValue(x + w, guidesX, threshold) - x;
        if (handle.Contains('n'))
        {
            var next = SnapValue(y, guidesY, threshold);
            h += y - next;
            y = next;
        }
        if (handle.Contains('s')) h = SnapValue(y + h, guidesY, threshold) - y;
        const double min = 32;
        if (w < min)
        {
            if (handle.Contains('w')) x -= min - w;
            w = min;
        }
        if (h < min)
        {
            if (handle.Contains('n')) y -= min - h;
            h = min;
        }
        return new StageRect(x, y, w, h);
    }

    public static (Vec3 Position, Vec2 Scale) RectToCueTransform(StageRect rect, Asset asset)
    {
        var aw = Math.Max(1, asset.Width > 0 ? asset.Width : 1920);
        var ah = Math.Max(1, asset.Height > 0 ? asset.Height : 1080);
        return (
            new Vec3 { X = Math.Round(rect.X), Y = Math.Round(rect.Y), Z = 0 },
            new Vec2 { X = RoundHundredths(rect.W / aw * 100), Y = RoundHundredths(rect.H / ah * 100) });
    }

    /// <summary>
    /// On-Stage pixel size of a cue (asset native × scale %). Watchout-style Width/Height.
    /// </summary>
    public static (double W, double H) CuePixelSize(Asset? asset, Vec2 scale)
    {
        var r = CueRect(new Cue { Scale = scale }, asset);
        return (Math.Round(r.W), Math.Round(r.H));
    }

    public static Vec2 ScaleFromPixelSize(Asset? asset, double pixelW, double pixelH) =>
        RectToCueTransform(new StageRect(0, 0, Math.Max(1, pixelW), Math.Max(1, pixelH)), asset ?? new Asset()).Scale;

    public static (double Value, double Dist)? NearestGuide(double value, IEnumerable<double> guides, double threshold)
    {
        double? best = null;
        var dist = threshold;
        foreach (var g in guides)
        {
            var d = Math.Abs(value - g);
            if (d <= dist)
            {
                dist = d;
                best = g;
            }
        }
        return best is { } b ? (b, dist) : null;
    }

    public static double SnapValue(double value, IEnumerable<double> guides, double threshold) =>
        NearestGuide(value, guides, threshold)?.Value ?? value;

    public static StageRect SnapRect(StageRect rect, IReadOnlyList<double> guidesX, IReadOnlyList<double> guidesY, double threshold)
    {
        var x = SnapAxis(rect.X, rect.W, guidesX, threshold);
        var y = SnapAxis(rect.Y, rect.H, guidesY, threshold);
        return rect with { X = Math.Round(x), Y = Math.Round(y) };
    }

    static double SnapAxis(double pos, double size, IReadOnlyList<double> guides, double threshold)
    {
        var start = NearestGuide(pos, guides, threshold);
        var end = NearestGuide(pos + size, guides, threshold);
        if (start is { } s && end is { } e) return s.Dist <= e.Dist ? s.Value : e.Value - size;
        if (start is { } s2) return s2.Value;
        if (end is { } e2) return e2.Value - size;
        return pos;
    }

    public static (List<double> X, List<double> Y) DisplayGuides(IEnumerable<Display> displays)
    {
        var x = new List<double>();
        var y = new List<double>();
        foreach (var d in displays.Where(d => d.Enabled))
        {
            x.Add(d.X);
            x.Add(d.X + d.Width);
            y.Add(d.Y);
            y.Add(d.Y + d.Height);
        }
        return (x, y);
    }

    public static (List<double> X, List<double> Y) DisplayMoveGuides(IEnumerable<Display> displays, string? skipId = null)
    {
        var x = new List<double> { 0 };
        var y = new List<double> { 0 };
        foreach (var d in displays.Where(d => d.Enabled && d.Id != skipId))
        {
            x.Add(d.X);
            x.Add(d.X + d.Width);
            x.Add(d.X + d.Width / 2);
            y.Add(d.Y);
            y.Add(d.Y + d.Height);
            y.Add(d.Y + d.Height / 2);
        }
        return (x, y);
    }

    public static double SnapThreshold(double zoom) => Math.Max(32, 48 / Math.Max(0.04, zoom));

    /// <summary>
    /// ~12 screen pixels in stage space. <see cref="SnapThreshold"/> is the drop
    /// magnet (200+ px at overview zoom) and glued Fit-to-display cues to the
    /// wall when used for drag/resize.
    /// </summary>
    public static double EditSnapThreshold(double zoom) =>
        Math.Max(8, 12 / Math.Max(0.04, zoom));

    public static Display DisplayForCue(IReadOnlyList<Display> displays, Cue cue)
    {
        return displays.FirstOrDefault(d =>
                   d.Enabled &&
                   cue.Position.X >= d.X &&
                   cue.Position.X < d.X + d.Width &&
                   cue.Position.Y >= d.Y &&
                   cue.Position.Y < d.Y + d.Height)
               ?? displays.FirstOrDefault(d => d.Enabled)
               ?? displays[0];
    }

    public static double DisplayChromeHeight(double zoom) => Math.Max(24, 24 / Math.Max(0.04, zoom));

    public static bool PointInDisplayChrome(Display d, (double X, double Y) pt, double zoom)
    {
        if (!d.Enabled) return false;
        var h = Math.Min(DisplayChromeHeight(zoom), d.Height);
        return pt.X >= d.X && pt.X <= d.X + d.Width && pt.Y >= d.Y && pt.Y <= d.Y + h;
    }

    public static List<(Cue Cue, StageRect Rect)> CueRects(IEnumerable<Cue> cues, IReadOnlyList<Asset> assets)
    {
        var byId = assets.ToDictionary(a => a.Id);
        return cues.Select(c =>
        {
            Asset? asset = c.AssetId is { } id && byId.TryGetValue(id, out var a) ? a : null;
            return (c, CueRect(c, asset));
        }).ToList();
    }

    public static List<(Cue Cue, StageRect Rect)> CueRects(IEnumerable<EvaluatedCue> cues, IReadOnlyList<Asset> assets)
    {
        var byId = assets.ToDictionary(a => a.Id);
        return cues.Select(ev =>
        {
            Asset? asset = ev.Cue.AssetId is { } id && byId.TryGetValue(id, out var a) ? a : null;
            return (ev.Cue, CueRect(ev, asset));
        }).ToList();
    }

    /// <summary>
    /// Stage edit hits VisibleMedia plus the current selection, so a cue picked
    /// on the timeline still has move/resize handles when the playhead is elsewhere.
    /// </summary>
    public static List<(Cue Cue, StageRect Rect)> EditCueRects(
        IReadOnlyList<EvaluatedCue> visible,
        IReadOnlyList<Asset> assets,
        IEnumerable<Cue> timelineCues,
        Selection selection)
    {
        var rects = CueRects(visible, assets);
        if (selection.Kind != SelectionKind.Cue) return rects;
        var have = rects.Select(r => r.Cue.Id).ToHashSet();
        foreach (var id in selection.Ids)
        {
            if (!have.Add(id)) continue;
            var cue = timelineCues.FirstOrDefault(c => c.Id == id);
            if (cue is null || cue.Type != CueType.Media) continue;
            var asset = assets.FirstOrDefault(a => a.Id == cue.AssetId);
            rects.Add((cue, CueRect(cue, asset)));
        }
        return rects;
    }

    public static StageHit HitEditTarget(
        StageEditMode mode,
        IReadOnlyList<Display> displays,
        IReadOnlyList<(Cue Cue, StageRect Rect)> cueRects,
        Selection selection,
        (double X, double Y) pt,
        double zoom,
        bool preferDisplay = false)
    {
        var editDisplays = mode == StageEditMode.Displays || preferDisplay;
        Display? selectedDisplay = selection.Kind == SelectionKind.Display
            ? displays.FirstOrDefault(d => selection.Ids.Contains(d.Id) && d.Enabled)
            : null;
        if (selectedDisplay is not null)
        {
            var handle = HitResizeHandle(DisplayRect(selectedDisplay), pt, zoom);
            if (handle is not null)
                return new StageHit(StageHitKind.DisplayHandle, selectedDisplay.Id, handle);
        }

        // Orange handles stay live for the selected cue even in Edit displays —
        // otherwise an overlay (NDI on layer 2) can only be resized, never moved.
        // Alt still prefers the display under the clip.
        if (!preferDisplay)
        {
            var selectedCue = HitSelectedCue(cueRects, selection, pt, zoom);
            if (selectedCue.Kind != StageHitKind.None)
                return selectedCue;
        }

        if (editDisplays)
        {
            var display = HitDisplay(displays, pt);
            return display is null ? default : new StageHit(StageHitKind.Display, display.Id);
        }

        for (var i = displays.Count - 1; i >= 0; i--)
        {
            if (PointInDisplayChrome(displays[i], pt, zoom))
                return new StageHit(StageHitKind.Display, displays[i].Id);
        }

        for (var i = cueRects.Count - 1; i >= 0; i--)
        {
            var (cue, rect) = cueRects[i];
            if (cue.Type != CueType.Media) continue;
            if (PointInRect(pt, rect))
                return new StageHit(StageHitKind.Cue, cue.Id);
        }

        var hitDisplay = HitDisplay(displays, pt);
        return hitDisplay is null ? default : new StageHit(StageHitKind.Display, hitDisplay.Id);
    }

    static StageHit HitSelectedCue(
        IReadOnlyList<(Cue Cue, StageRect Rect)> cueRects,
        Selection selection,
        (double X, double Y) pt,
        double zoom)
    {
        if (selection.Kind != SelectionKind.Cue) return default;
        var selected = cueRects.LastOrDefault(c => selection.Ids.Contains(c.Cue.Id));
        if (selected.Cue is null) return default;
        var handle = HitResizeHandle(selected.Rect, pt, zoom);
        if (handle is not null)
            return new StageHit(StageHitKind.CueHandle, selected.Cue.Id, handle);
        if (PointInRect(pt, selected.Rect))
            return new StageHit(StageHitKind.Cue, selected.Cue.Id);
        return default;
    }
}
