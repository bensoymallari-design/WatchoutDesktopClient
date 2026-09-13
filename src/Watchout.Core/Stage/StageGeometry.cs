using Watchout.Core.Models;
using Watchout.Core.Playback;

namespace Watchout.Core.Stage;

public readonly record struct StageRect(double X, double Y, double W, double H);

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
        var pad = HandleHitPad(zoom);
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

    public static double SnapThreshold(double zoom) => Math.Max(6, 10 / Math.Max(0.05, zoom));

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
}
