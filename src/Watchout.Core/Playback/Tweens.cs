using Watchout.Core.Models;
using Watchout.Core.Scheduling;
using Watchout.Core.Stage;

namespace Watchout.Core.Playback;

public static class Tweens
{
    public static double? EvalTween(Tween tween, double localTime)
    {
        if (!tween.Enabled || tween.Points.Count == 0) return null;
        var pts = tween.Points.OrderBy(p => p.Time).ToList();
        if (localTime <= pts[0].Time) return pts[0].Value;
        var last = pts[^1];
        if (localTime >= last.Time) return last.Value;
        for (var i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            if (localTime <= b.Time)
            {
                var span = Math.Max(1, b.Time - a.Time);
                var t = EasingCurves.Ease(b.Easing, (localTime - a.Time) / span);
                return a.Value + (b.Value - a.Value) * t;
            }
        }
        return last.Value;
    }

    public static EvaluatedCue? EvaluateCue(Cue cue, double playhead, IReadOnlyList<Cue>? others = null)
    {
        if (!cue.Enabled) return null;
        if (!cue.FreeRunning && (playhead < cue.Start || playhead >= cue.Start + cue.Duration)) return null;
        var raw = cue.FreeRunning ? playhead : playhead - cue.Start;
        var local = CueLooks.MediaTime(raw, cue.Speed);
        var map = cue.Tweens.GroupBy(t => t.Type).ToDictionary(g => g.Key, g => g.First());
        var opacity = Pick(map, TweenType.Opacity, local, cue.Opacity) * TimelineMath.FadeMultiplier(cue, local, others);
        return new EvaluatedCue
        {
            Cue = cue,
            LocalTime = local,
            Opacity = opacity,
            X = Pick(map, TweenType.PositionX, local, cue.Position.X),
            Y = Pick(map, TweenType.PositionY, local, cue.Position.Y),
            Z = Pick(map, TweenType.PositionZ, local, cue.Position.Z),
            ScaleX = Pick(map, TweenType.ScaleX, local, cue.Scale.X),
            ScaleY = Pick(map, TweenType.ScaleY, local, cue.Scale.Y),
            RotX = Pick(map, TweenType.RotationX, local, cue.Rotation.X),
            RotY = Pick(map, TweenType.RotationY, local, cue.Rotation.Y),
            RotZ = Pick(map, TweenType.RotationZ, local, cue.Rotation.Z),
            Volume = cue.Muted == true ? 0 : Pick(map, TweenType.Volume, local, cue.Volume),
            Blur = Pick(map, TweenType.Blur, local, cue.Blur),
            Brightness = Pick(map, TweenType.Brightness, local, cue.Brightness),
            Contrast = Pick(map, TweenType.Contrast, local, cue.Contrast),
            Saturation = Pick(map, TweenType.Saturation, local, cue.Saturation),
            Hue = Pick(map, TweenType.Hue, local, cue.Hue),
            Crop = new Crop
            {
                Top = Pick(map, TweenType.CropTop, local, cue.Crop.Top),
                Bottom = Pick(map, TweenType.CropBottom, local, cue.Crop.Bottom),
                Left = Pick(map, TweenType.CropLeft, local, cue.Crop.Left),
                Right = Pick(map, TweenType.CropRight, local, cue.Crop.Right),
            },
            Wipe = Pick(map, TweenType.WipeCompletion, local, cue.WipeCompletion),
            WipeAngle = cue.WipeAngle,
            WipeFeather = cue.WipeFeather,
            Temperature = cue.Temperature,
            Exposure = cue.Exposure,
            Speed = cue.Speed <= 0 ? 100 : cue.Speed,
        };
    }

    static double Pick(Dictionary<TweenType, Tween> map, TweenType type, double local, double fallback)
    {
        if (!map.TryGetValue(type, out var tw)) return fallback;
        return EvalTween(tw, local) ?? fallback;
    }

    public static Tween MakeTween(TweenType type, IEnumerable<(double Time, double Value, Easing Easing)> points)
    {
        return new Tween
        {
            Id = Ids.New("tw"),
            Type = type,
            Enabled = true,
            Visible = true,
            Points = points.Select(p => new TweenPoint
            {
                Id = Ids.New("tp"),
                Time = p.Time,
                Value = p.Value,
                Easing = p.Easing,
            }).ToList(),
        };
    }

    public static Tween MakeTween(TweenType type, params (double Time, double Value, Easing Easing)[] points) =>
        MakeTween(type, (IEnumerable<(double, double, Easing)>)points);
}

public sealed class EvaluatedCue
{
    public required Cue Cue { get; init; }
    public double LocalTime { get; init; }
    public double Opacity { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double ScaleX { get; init; }
    public double ScaleY { get; init; }
    public double RotX { get; init; }
    public double RotY { get; init; }
    public double RotZ { get; init; }
    public double Volume { get; init; }
    public double Blur { get; init; }
    public double Brightness { get; init; }
    public double Contrast { get; init; }
    public double Saturation { get; init; }
    public double Hue { get; init; }
    public Crop Crop { get; init; } = new();
    public double Wipe { get; init; }
    public double WipeAngle { get; init; }
    public double WipeFeather { get; init; }
    public double Temperature { get; init; }
    public double Exposure { get; init; }
    public double Speed { get; init; } = 100;
}
