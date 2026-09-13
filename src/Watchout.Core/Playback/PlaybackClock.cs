using Watchout.Core.Models;

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
                if (tl.Loop) tl.Playhead %= Math.Max(1, tl.Duration);
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

    public static IReadOnlyList<EvaluatedCue> VisibleCues(Models.Timeline timeline)
    {
        var others = timeline.Cues;
        return timeline.Cues
            .Select(c => Tweens.EvaluateCue(c, timeline.Playhead, others))
            .OfType<EvaluatedCue>()
            .OrderBy(e => e.Z)
            .ThenBy(e => e.Cue.LayerId)
            .ToList();
    }

    public static IReadOnlyList<EvaluatedCue> VisibleMedia(Models.Show show)
    {
        var list = new List<EvaluatedCue>();
        foreach (var tl in show.Timelines.Where(t => t.Enabled))
            list.AddRange(VisibleCues(tl).Where(e => e.Cue.Type == CueType.Media && e.Opacity > 0.5));
        return list;
    }
}
