using Watchout.Core.Models;
using Watchout.Core.Persistence;
using Watchout.Core.Scheduling;

namespace Watchout.Core.Playback;

public static class PlaybackClock
{
    public static void Tick(Models.Show show, double dtMs)
    {
        foreach (var tl in show.Timelines)
        {
            if (!tl.Enabled || tl.Playback != PlaybackState.Play) continue;
            tl.Playhead += dtMs * (tl.Rate <= 0 ? 1 : tl.Rate);
            if (tl.Playhead >= tl.Duration)
            {
                if (tl.Loop) tl.Playhead = WrapPlayhead(tl.Playhead, tl.Duration);
                else
                {
                    tl.Playhead = tl.Duration;
                    tl.Playback = PlaybackState.Stop;
                }
            }
        }
    }

    public static void SetPlayback(Models.Timeline timeline, PlaybackState state)
    {
        timeline.Playback = state;
        if (state == PlaybackState.Stop) timeline.Playhead = 0;
    }

    /// <summary>
    /// Keep a looping playhead inside [0, duration). <c>%</c> can return
    /// <paramref name="duration"/> itself after a long run, which hides the last
    /// clip (evaluate uses playhead &gt;= end) and leaves Stage and Output black.
    /// </summary>
    public static double WrapPlayhead(double playhead, double duration)
    {
        var span = Math.Max(1, duration);
        if (double.IsNaN(playhead) || double.IsInfinity(playhead) || playhead < 0) return 0;
        if (playhead < span) return playhead;
        var wrapped = playhead % span;
        if (wrapped <= 0 || wrapped >= span - 0.5) return 0;
        return wrapped;
    }

    public static IReadOnlyList<EvaluatedCue> VisibleCues(Models.Timeline timeline)
    {
        var others = timeline.Cues;
        var hidden = timeline.Layers.Where(l => !l.Enabled).Select(l => l.Id).ToHashSet();
        return timeline.Cues
            .Where(c => !hidden.Contains(c.LayerId))
            .Select(c => Tweens.EvaluateCue(c, timeline.Playhead, others))
            .OfType<EvaluatedCue>()
            .OrderBy(e => TimelineMath.LayerStackIndex(timeline.Layers, e.Cue.LayerId))
            .ThenBy(e => e.Z)
            .ToList();
    }

    public static IReadOnlyList<EvaluatedCue> VisibleMedia(Models.Show show)
    {
        var list = new List<EvaluatedCue>();
        foreach (var tl in show.Timelines.Where(t => t.Enabled))
        {
            foreach (var ev in VisibleCues(tl).Where(e => e.Cue.Type == CueType.Media && e.Opacity > 0.5))
            {
                var asset = show.Assets.FirstOrDefault(a => a.Id == ev.Cue.AssetId);
                if (asset is { Kind: AssetKind.Composition } && asset.Children.Count > 0)
                {
                    var kids = ExpandComposition(ev, asset);
                    if (kids.Count > 0) list.AddRange(kids);
                    else list.Add(ev);
                }
                else
                    list.Add(ev);
            }
        }
        return list;
    }

    public static string RootCueId(string cueId)
    {
        var slash = cueId.IndexOf('/');
        return slash < 0 ? cueId : cueId[..slash];
    }

    static IReadOnlyList<EvaluatedCue> ExpandComposition(EvaluatedCue parent, Asset asset)
    {
        var list = new List<EvaluatedCue>();
        foreach (var child in asset.Children)
        {
            var ev = Tweens.EvaluateCue(child, parent.LocalTime, asset.Children);
            if (ev is null || ev.Opacity <= 0.5) continue;
            var copy = ShowSerializer.LoadCue(ShowSerializer.SaveCue(child));
            copy.Id = $"{parent.Cue.Id}/{child.Id}";
            list.Add(new EvaluatedCue
            {
                Cue = copy,
                LocalTime = ev.LocalTime,
                Opacity = ev.Opacity * parent.Opacity / 100,
                X = parent.X + ev.X * (parent.ScaleX / 100),
                Y = parent.Y + ev.Y * (parent.ScaleY / 100),
                Z = parent.Z + ev.Z,
                ScaleX = ev.ScaleX * parent.ScaleX / 100,
                ScaleY = ev.ScaleY * parent.ScaleY / 100,
                RotX = ev.RotX + parent.RotX,
                RotY = ev.RotY + parent.RotY,
                RotZ = ev.RotZ + parent.RotZ,
                Volume = ev.Volume * parent.Volume / 100,
                Blur = ev.Blur,
                Brightness = ev.Brightness + parent.Brightness,
                Contrast = ev.Contrast + parent.Contrast,
                Saturation = ev.Saturation,
                Hue = ev.Hue + parent.Hue,
                Crop = ev.Crop,
                Wipe = ev.Wipe,
                WipeAngle = ev.WipeAngle,
                WipeFeather = ev.WipeFeather,
                Temperature = ev.Temperature + parent.Temperature,
                Exposure = ev.Exposure + parent.Exposure,
                Speed = ev.Speed,
            });
        }
        return list;
    }
}
