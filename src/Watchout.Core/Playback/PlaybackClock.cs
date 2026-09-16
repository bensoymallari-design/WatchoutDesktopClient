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
            var span = LoopSpan(tl);
            if (tl.Playhead >= span)
            {
                if (tl.Loop) tl.Playhead = SnapToPlayable(tl);
                else
                {
                    tl.Playhead = span;
                    tl.Playback = PlaybackState.Stop;
                }
            }
        }
    }

    public static void SetPlayback(Models.Timeline timeline, PlaybackState state)
    {
        if (state == PlaybackState.Stop) timeline.Playhead = 0;
        else if (state == PlaybackState.Play) timeline.Playhead = SnapToPlayable(timeline);
        timeline.Playback = state;
    }

    /// <summary>
    /// Space/Play while already running pauses — unless the clock has run off the
    /// last clip, in which case Play must snap back onto media instead of freezing
    /// on a black Stage and Output.
    /// </summary>
    public static PlaybackState ToggleTarget(Models.Timeline timeline)
    {
        if (timeline.Playback != PlaybackState.Play) return PlaybackState.Play;
        return timeline.Playhead >= LoopSpan(timeline) ? PlaybackState.Play : PlaybackState.Pause;
    }

    /// <summary>
    /// Loop and stop against the last finite clip, not a leftover 24 h timeline
    /// length from an NDI/capture cue. Day-long live cues stay on screen because
    /// they are free-running; H.264 hides the instant playhead &gt;= cue end.
    /// </summary>
    public static double LoopSpan(Models.Timeline timeline)
    {
        var duration = Math.Max(1, timeline.Duration);
        var content = TimelineMath.ContentEnd(timeline.Cues);
        if (content <= 0) return duration;
        return Math.Min(duration, content);
    }

    /// <summary>
    /// Play from empty time past the last clip jumps back onto media so Stage
    /// and Output light up again.
    /// </summary>
    public static double SnapToPlayable(Models.Timeline timeline)
    {
        var span = LoopSpan(timeline);
        var playhead = timeline.Playhead;
        if (double.IsNaN(playhead) || double.IsInfinity(playhead) || playhead < 0) playhead = 0;
        if (playhead >= span)
            playhead = timeline.Loop ? WrapPlayhead(playhead, span) : 0;
        if (!HasVisibleMediaCue(timeline, playhead))
            playhead = TimelineMath.FirstFiniteMediaStart(timeline.Cues);
        return playhead;
    }

    public static bool HasVisibleMediaCue(Models.Timeline timeline, double playhead)
    {
        var hidden = timeline.Layers.Where(l => !l.Enabled).Select(l => l.Id).ToHashSet();
        foreach (var cue in timeline.Cues)
        {
            if (cue.Type != CueType.Media || hidden.Contains(cue.LayerId)) continue;
            var ev = Tweens.EvaluateCue(cue, playhead, timeline.Cues);
            if (ev is not null && ev.Opacity > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Loop the H.264 file inside a longer cue. After ~1 hour the clock can still
    /// sit inside a leftover day-long bar while the decoder is already at EOF.
    /// </summary>
    public static double LoopFileTime(double localMs, double fileDurationMs)
    {
        if (fileDurationMs <= 1) return Math.Max(0, localMs);
        return WrapPlayhead(localMs, fileDurationMs);
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
            foreach (var ev in VisibleCues(tl).Where(e => e.Cue.Type == CueType.Media && e.Opacity > 0))
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
            if (ev is null || ev.Opacity <= 0) continue;
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
