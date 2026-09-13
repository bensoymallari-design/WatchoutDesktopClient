using Watchout.Core.Models;

namespace Watchout.Core.Scheduling;

public static class TimelineMath
{
    public static double CueEnd(Cue cue) => cue.Start + Math.Max(0, cue.Duration);

    public static bool CuesOverlap(Cue a, Cue b) => a.Start < CueEnd(b) && b.Start < CueEnd(a);

    public static double OverlapMs(Cue a, Cue b) =>
        Math.Max(0, Math.Min(CueEnd(a), CueEnd(b)) - Math.Max(a.Start, b.Start));

    public static bool IsMedia(Cue cue) => cue.Type == CueType.Media;

    public static bool IsAllowedOverlap(Cue a, Cue b)
    {
        if (a.LayerId != b.LayerId) return true;
        if (!IsMedia(a) || !IsMedia(b)) return true;
        if (!CuesOverlap(a, b)) return true;
        var earlier = a.Start <= b.Start ? a : b;
        var later = earlier == a ? b : a;
        return earlier.FadeOut && later.FadeIn;
    }

    public static bool CueHasConflict(Cue cue, IEnumerable<Cue> others)
    {
        if (!IsMedia(cue) || !cue.Enabled) return false;
        return others.Any(other => other.Id != cue.Id && other.Enabled && !IsAllowedOverlap(cue, other));
    }

    public static (double FadeIn, double FadeOut) FadeDurations(Cue cue, IReadOnlyList<Cue>? others = null)
    {
        var fadeIn = cue.FadeIn ? Math.Max(0, cue.FadeInDuration) : 0;
        var fadeOut = cue.FadeOut ? Math.Max(0, cue.FadeOutDuration) : 0;
        if (others is null) return (fadeIn, fadeOut);
        foreach (var other in others)
        {
            if (other.Id == cue.Id || !other.Enabled || !IsMedia(other) || other.LayerId != cue.LayerId) continue;
            if (!CuesOverlap(cue, other) || !IsAllowedOverlap(cue, other)) continue;
            var ov = OverlapMs(cue, other);
            if (ov <= 0) continue;
            if (cue.Start <= other.Start) fadeOut = ov;
            else fadeIn = ov;
        }
        return (fadeIn, fadeOut);
    }

    public static double FadeMultiplier(Cue cue, double localTime, IReadOnlyList<Cue>? others = null)
    {
        var m = 1.0;
        var curve = cue.FadeCurve;
        var (fadeIn, fadeOut) = FadeDurations(cue, others);
        if (fadeIn > 0)
            m *= EasingCurves.Ease(curve, Math.Min(1, Math.Max(0, localTime / fadeIn)));
        if (fadeOut > 0)
        {
            var remain = cue.Duration - localTime;
            m *= EasingCurves.Ease(curve, Math.Min(1, Math.Max(0, remain / fadeOut)));
        }
        return m;
    }

    public static double SnapTime(double value, IEnumerable<double> anchors, double threshold)
    {
        var best = value;
        var bestDist = threshold;
        foreach (var anchor in anchors)
        {
            var dist = Math.Abs(value - anchor);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = anchor;
            }
        }
        return Math.Max(0, best);
    }

    public static List<double> TimelineAnchors(IEnumerable<Cue> cues, IEnumerable<string> excludeIds, double playhead)
    {
        var skip = excludeIds.ToHashSet();
        var anchors = new List<double> { 0, playhead };
        foreach (var cue in cues)
        {
            if (skip.Contains(cue.Id)) continue;
            anchors.Add(cue.Start);
            anchors.Add(CueEnd(cue));
        }
        return anchors;
    }

    public static (Cue A, Cue B)? FindCrossfadePair(IReadOnlyList<Cue> cues, IReadOnlyList<string> selectedIds)
    {
        var media = cues.Where(c => IsMedia(c) && c.Enabled).ToList();
        var selected = media.Where(c => selectedIds.Contains(c.Id)).OrderBy(c => c.Start).ToList();
        if (selected.Count >= 2) return (selected[0], selected[1]);
        if (selected.Count == 1)
        {
            var a = selected[0];
            var same = media.Where(c => c.LayerId == a.LayerId && c.Id != a.Id).ToList();
            var next = same.Where(c => c.Start >= a.Start).OrderBy(c => c.Start).FirstOrDefault();
            var prev = same.Where(c => c.Start < a.Start).OrderByDescending(c => c.Start).FirstOrDefault();
            var other = next ?? prev;
            if (other is null) return null;
            return a.Start <= other.Start ? (a, other) : (other, a);
        }
        return null;
    }

    public static List<T> RemoveTimelines<T>(IReadOnlyList<T> timelines, IEnumerable<string> ids) where T : class
    {
        var drop = ids.ToHashSet();
        var idOf = (T t) => t.GetType().GetProperty("Id")?.GetValue(t)?.ToString() ?? "";
        var next = timelines.Where(t => !drop.Contains(idOf(t))).ToList();
        return next.Count > 0 ? next : timelines.ToList();
    }

    public static List<TTimeline> RemoveTimelinesById<TTimeline>(IReadOnlyList<TTimeline> timelines, IEnumerable<string> ids, Func<TTimeline, string> id)
    {
        var drop = ids.ToHashSet();
        var next = timelines.Where(t => !drop.Contains(id(t))).ToList();
        return next.Count > 0 ? next : timelines.ToList();
    }

    public static bool CanRemoveTimeline(int count) => count > 1;

    public static bool CanRemoveLayer(int count) => count > 1;

    public static (List<Layer> Layers, List<Cue> Cues)? RemoveLayer(IReadOnlyList<Layer> layers, IReadOnlyList<Cue> cues, string id)
    {
        if (!CanRemoveLayer(layers.Count) || layers.All(l => l.Id != id)) return null;
        return (layers.Where(l => l.Id != id).ToList(), cues.Where(c => c.LayerId != id).ToList());
    }

    public static int InsertLayerIndex(IReadOnlyList<Layer> layers, string? afterId)
    {
        if (afterId is null) return layers.Count;
        var idx = -1;
        for (var i = 0; i < layers.Count; i++)
            if (layers[i].Id == afterId) idx = i;
        return idx >= 0 ? idx + 1 : layers.Count;
    }

    public static Show PurgeAssets(Show show, IEnumerable<string> ids)
    {
        var drop = ids.ToHashSet();
        show.Assets = show.Assets.Where(a => !drop.Contains(a.Id)).ToList();
        foreach (var tl in show.Timelines)
            tl.Cues = tl.Cues.Where(c => c.AssetId is null || !drop.Contains(c.AssetId)).ToList();
        return show;
    }

    public static bool TimelineClickSeeksPlayhead(string target, bool clickJumpsToTime)
    {
        if (target == "ruler") return true;
        if (target == "lane") return clickJumpsToTime;
        return false;
    }
}
