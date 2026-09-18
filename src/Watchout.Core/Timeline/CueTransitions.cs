using Watchout.Core.Models;

namespace Watchout.Core.Scheduling;

public readonly record struct TransitionState(
    double InAmount,
    double OutAmount,
    TransitionFilter InFilter,
    TransitionFilter OutFilter)
{
    public double OpacityMul =>
        (CueTransitions.UsesOpacity(InFilter) ? InAmount : 1) *
        (CueTransitions.UsesOpacity(OutFilter) ? OutAmount : 1);

    public double ScaleMul =>
        (CueTransitions.UsesScale(InFilter) ? InAmount : 1) *
        (CueTransitions.UsesScale(OutFilter) ? OutAmount : 1);

    public double WipeMul =>
        (CueTransitions.UsesWipe(InFilter) ? InAmount : 1) *
        (CueTransitions.UsesWipe(OutFilter) ? OutAmount : 1);
}

/// <summary>
/// Start-of-cue and end-of-cue transition filters. Legacy shows that only
/// set <see cref="Cue.FadeIn"/> / <see cref="Cue.FadeOut"/> still resolve as Fade.
/// </summary>
public static class CueTransitions
{
    public static readonly (TransitionFilter Filter, string Name)[] Filters =
    [
        (TransitionFilter.None, "None"),
        (TransitionFilter.Fade, "Fade"),
        (TransitionFilter.Dissolve, "Dissolve"),
        (TransitionFilter.Wipe, "Wipe"),
        (TransitionFilter.Scale, "Scale"),
    ];

    public static bool UsesOpacity(TransitionFilter filter) =>
        filter is TransitionFilter.Fade or TransitionFilter.Dissolve;

    public static bool UsesWipe(TransitionFilter filter) => filter == TransitionFilter.Wipe;

    public static bool UsesScale(TransitionFilter filter) => filter == TransitionFilter.Scale;

    public static string Label(TransitionFilter filter) =>
        Filters.FirstOrDefault(f => f.Filter == filter).Name ?? "None";

    public static string Mark(TransitionFilter filter) => filter switch
    {
        TransitionFilter.Fade => "F",
        TransitionFilter.Dissolve => "D",
        TransitionFilter.Wipe => "W",
        TransitionFilter.Scale => "S",
        _ => "",
    };

    /// <summary>Old shows stored only the bool; treat that as a linear fade.</summary>
    public static TransitionFilter ResolvedIn(Cue cue)
    {
        if (!cue.FadeIn) return TransitionFilter.None;
        return cue.FadeInFilter == TransitionFilter.None ? TransitionFilter.Fade : cue.FadeInFilter;
    }

    public static TransitionFilter ResolvedOut(Cue cue)
    {
        if (!cue.FadeOut) return TransitionFilter.None;
        return cue.FadeOutFilter == TransitionFilter.None ? TransitionFilter.Fade : cue.FadeOutFilter;
    }

    public static void Apply(Cue cue, string which, TransitionFilter filter, bool toggleSame = false)
    {
        var start = which != "out";
        var current = start ? ResolvedIn(cue) : ResolvedOut(cue);
        var next = toggleSame && current == filter ? TransitionFilter.None : filter;
        if (start)
        {
            cue.FadeInFilter = next;
            cue.FadeIn = next != TransitionFilter.None;
        }
        else
        {
            cue.FadeOutFilter = next;
            cue.FadeOut = next != TransitionFilter.None;
        }
    }

    public static void ApplyDuration(Cue cue, string which, double ms)
    {
        ms = Math.Max(0, ms);
        if (which == "out") cue.FadeOutDuration = ms;
        else cue.FadeInDuration = ms;
    }

    public static TransitionState Evaluate(Cue cue, double localTime, IReadOnlyList<Cue>? others = null)
    {
        var inn = ResolvedIn(cue);
        var outt = ResolvedOut(cue);
        var (fadeIn, fadeOut) = TimelineMath.FadeDurations(cue, others);
        var inAmt = Amount(localTime, fadeIn, Curve(inn, cue.FadeCurve));
        var outAmt = Amount(cue.Duration - localTime, fadeOut, Curve(outt, cue.FadeCurve));
        return new TransitionState(inAmt, outAmt, inn, outt);
    }

    static Easing Curve(TransitionFilter filter, Easing fadeCurve) =>
        filter == TransitionFilter.Dissolve ? Easing.SineInOut : fadeCurve;

    static double Amount(double t, double duration, Easing curve)
    {
        if (duration <= 0) return 1;
        return EasingCurves.Ease(curve, Math.Min(1, Math.Max(0, t / duration)));
    }
}
