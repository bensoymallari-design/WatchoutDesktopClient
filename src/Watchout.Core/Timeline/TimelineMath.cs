using Watchout.Core.Models;

namespace Watchout.Core.Scheduling;

public static class TimelineMath
{
    public static double CueEnd(Cue cue) => cue.Start + Math.Max(0, cue.Duration);

    /// <summary>Day-long live NDI/capture cues. Finite H.264 clips (including 1 hour+ files) stay in ContentEnd.</summary>
    public const double LiveLengthMs = 24 * 60 * 60 * 1000;
    public const double LaneHeight = 28;
    public const double RulerHeight = 22;
    public const double HeaderWidth = 120;
    public const double LayerIconSize = 16;
    public const double LayerIconGap = 4;
    public const double MinZoom = 0.0002;
    public const double MaxZoom = 0.2;

    public enum LayerHeaderPart { Body, Lock, Eye }

    public static double LayerEyeLeft(double headerWidth = HeaderWidth) =>
        headerWidth - LayerIconGap - LayerIconSize;

    public static double LayerLockLeft(double headerWidth = HeaderWidth) =>
        LayerEyeLeft(headerWidth) - LayerIconGap - LayerIconSize;

    public static LayerHeaderPart HitLayerHeader(double x, double headerWidth = HeaderWidth)
    {
        if (x >= LayerEyeLeft(headerWidth)) return LayerHeaderPart.Eye;
        if (x >= LayerLockLeft(headerWidth)) return LayerHeaderPart.Lock;
        return LayerHeaderPart.Body;
    }

    public static Layer? FindLayer(IEnumerable<Layer> layers, string? layerId) =>
        layers.FirstOrDefault(l => l.Id == layerId);

    public static Layer? FindLayer(Show show, string? layerId) =>
        FindLayer(show.Timelines.SelectMany(t => t.Layers), layerId);

    public static bool LayerIsVisible(IEnumerable<Layer> layers, string? layerId) =>
        FindLayer(layers, layerId)?.Enabled != false;

    public static bool LayerIsLocked(IEnumerable<Layer> layers, string? layerId) =>
        FindLayer(layers, layerId)?.Locked == true;

    /// <summary>0 = back (first in the timeline list), higher = in front.</summary>
    public static int LayerStackIndex(IReadOnlyList<Layer> layers, string? layerId)
    {
        if (string.IsNullOrEmpty(layerId)) return 0;
        for (var i = 0; i < layers.Count; i++)
            if (layers[i].Id == layerId) return i;
        return 0;
    }

    public static double ClampZoom(double zoom) => Math.Clamp(zoom, MinZoom, MaxZoom);

    /// <summary>Visible portion of a cue bar after clipping to the time column.</summary>
    public static (double X, double W)? ClipCueBar(double x, double width, double left, double right)
    {
        var end = x + Math.Max(0, width);
        if (end <= left || x >= right) return null;
        var nx = Math.Max(x, left);
        var nw = Math.Min(end, right) - nx;
        return nw > 0 ? (nx, nw) : null;
    }

    public static double VisibleDurationMs(double viewWidthPx, double zoom, double headerPx = HeaderWidth) =>
        Math.Max(1, (Math.Max(headerPx + 1, viewWidthPx) - headerPx) / Math.Max(0.0001, zoom));

    public static double ClampScroll(double scroll, double durationMs, double visibleMs)
    {
        var max = Math.Max(0, durationMs - Math.Max(1, visibleMs));
        return Math.Clamp(scroll, 0, max);
    }

    public static double ScrollToShow(double startMs, double endMs, double scroll, double visibleMs)
    {
        visibleMs = Math.Max(1, visibleMs);
        if (endMs < startMs) endMs = startMs;
        if (endMs - startMs >= visibleMs) return Math.Max(0, startMs);
        if (startMs < scroll) return Math.Max(0, startMs);
        if (endMs > scroll + visibleMs) return Math.Max(0, endMs - visibleMs);
        return scroll;
    }

    public static double ClampLayerScroll(double scroll, int layerCount, double laneH, double visibleH)
    {
        var content = Math.Max(0, layerCount * laneH);
        var max = Math.Max(0, content - Math.Max(0, visibleH));
        return Math.Clamp(scroll, 0, max);
    }

    public static double LayerScrollToShow(int layerIndex, double laneH, double scroll, double visibleH)
    {
        if (visibleH <= 0) return Math.Max(0, scroll);
        var top = Math.Max(0, layerIndex) * laneH;
        var bottom = top + laneH;
        if (top < scroll) return top;
        if (bottom > scroll + visibleH) return Math.Max(0, bottom - visibleH);
        return scroll;
    }

    public static double ExtendDurationTo(double current, double needed) =>
        Math.Max(Math.Max(1000, current), needed);

    /// <summary>End of the last finite clip (markers and day-long live cues are ignored).</summary>
    public static double ContentEnd(IEnumerable<Cue> cues)
    {
        double end = 0;
        foreach (var cue in cues)
        {
            if (!IsFiniteMedia(cue)) continue;
            end = Math.Max(end, CueEnd(cue));
        }
        return end;
    }

    /// <summary>Start of the earliest finite clip, or 0 when the timeline is live-only.</summary>
    public static double FirstFiniteMediaStart(IEnumerable<Cue> cues)
    {
        var start = double.PositiveInfinity;
        foreach (var cue in cues)
        {
            if (!cue.Enabled || !IsFiniteMedia(cue)) continue;
            start = Math.Min(start, cue.Start);
        }
        return double.IsInfinity(start) ? 0 : start;
    }

    public static bool IsFiniteMedia(Cue cue) =>
        cue.Type != CueType.Marker && !cue.FreeRunning && cue.Duration < LiveLengthMs;

    public static double FitDuration(double contentEndMs) =>
        Math.Max(1000, Math.Ceiling(contentEndMs));

    public static double FitZoom(double durationMs, double viewWidthPx, double headerPx = HeaderWidth) =>
        ClampZoom((viewWidthPx - headerPx) / Math.Max(1, durationMs));

    public static bool CuesOverlap(Cue a, Cue b) => a.Start < CueEnd(b) && b.Start < CueEnd(a);

    public static double OverlapMs(Cue a, Cue b) =>
        Math.Max(0, Math.Min(CueEnd(a), CueEnd(b)) - Math.Max(a.Start, b.Start));

    public enum CueBarPart { Start, Body, End }

    public enum TimelineGrab { None, Playhead, CueStart, CueEnd, CueBody }

    public const double CueEdgeMinPx = 16;
    public const double CueEdgeMaxPx = 72;
    public const double PlayheadHitPx = 10;
    public const double CueTrimHitPx = 10;

    /// <summary>
    /// Right-click the left third of a cue bar for start effects, the right third for end effects.
    /// </summary>
    public static CueBarPart HitCueBar(double localX, double widthPx)
    {
        widthPx = Math.Max(1, widthPx);
        localX = Math.Clamp(localX, 0, widthPx);
        if (widthPx < CueEdgeMinPx * 2)
            return localX < widthPx * 0.5 ? CueBarPart.Start : CueBarPart.End;
        var edge = Math.Clamp(widthPx * 0.28, CueEdgeMinPx, CueEdgeMaxPx);
        if (edge * 2 >= widthPx)
            return localX < widthPx * 0.5 ? CueBarPart.Start : CueBarPart.End;
        if (localX <= edge) return CueBarPart.Start;
        if (localX >= widthPx - edge) return CueBarPart.End;
        return CueBarPart.Body;
    }

    public static bool HitPlayhead(double x, double playheadX, double hitPx = PlayheadHitPx) =>
        Math.Abs(x - playheadX) <= Math.Max(1, hitPx);

    /// <summary>
    /// Trim handles are a ~10px strip at each end (and a few pixels outside the bar)
    /// so a SizeWE cursor is easy to hit. Thin bars split in half.
    /// </summary>
    public static CueBarPart? HitCueTrim(double localX, double widthPx, double hitPx = CueTrimHitPx)
    {
        widthPx = Math.Max(1, widthPx);
        hitPx = Math.Max(1, hitPx);
        if (localX < -hitPx || localX > widthPx + hitPx) return null;
        if (widthPx < hitPx * 2)
            return localX < widthPx * 0.5 ? CueBarPart.Start : CueBarPart.End;
        if (localX <= hitPx) return CueBarPart.Start;
        if (localX >= widthPx - hitPx) return CueBarPart.End;
        return CueBarPart.Body;
    }

    public static bool CueContainsTime(Cue cue, double msAt, double padMs = 0, double minWidthMs = 0)
    {
        var start = cue.Start - Math.Max(0, padMs);
        var end = Math.Max(CueEnd(cue) + Math.Max(0, padMs), cue.Start + Math.Max(0, minWidthMs));
        return msAt >= start && msAt <= end;
    }

    /// <summary>Cue start/end wins over the playhead so trim stays reachable; playhead wins over the clip body.</summary>
    public static TimelineGrab HoverGrab(CueBarPart? trim, bool onPlayhead)
    {
        if (trim is CueBarPart.Start) return TimelineGrab.CueStart;
        if (trim is CueBarPart.End) return TimelineGrab.CueEnd;
        if (onPlayhead) return TimelineGrab.Playhead;
        if (trim is CueBarPart.Body) return TimelineGrab.CueBody;
        return TimelineGrab.None;
    }

    public static bool UsesSizeWE(TimelineGrab grab) =>
        grab is TimelineGrab.Playhead or TimelineGrab.CueStart or TimelineGrab.CueEnd;

    public static bool UsesSizeAll(TimelineGrab grab) => grab == TimelineGrab.CueBody;

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

    public static double FadeMultiplier(Cue cue, double localTime, IReadOnlyList<Cue>? others = null) =>
        CueTransitions.Evaluate(cue, localTime, others).OpacityMul;

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
