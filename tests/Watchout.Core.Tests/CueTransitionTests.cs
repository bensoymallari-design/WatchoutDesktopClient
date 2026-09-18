using Watchout.Core;
using Watchout.Core.Models;
using Watchout.Core.Playback;
using Watchout.Core.Persistence;
using Watchout.Core.Scheduling;
using Xunit;

namespace Watchout.Core.Tests;

public class CueTransitionTests
{
    static Cue Clip(
        bool fadeIn = false,
        bool fadeOut = false,
        TransitionFilter inn = TransitionFilter.None,
        TransitionFilter outt = TransitionFilter.None,
        double inDur = 500,
        double outDur = 500,
        double duration = 2000) => new()
    {
        Id = "c",
        Type = CueType.Media,
        Name = "Clip",
        LayerId = "l",
        Start = 0,
        Duration = duration,
        Enabled = true,
        Opacity = 100,
        Scale = new Vec2 { X = 100, Y = 100 },
        FadeIn = fadeIn,
        FadeOut = fadeOut,
        FadeInDuration = inDur,
        FadeOutDuration = outDur,
        FadeInFilter = inn,
        FadeOutFilter = outt,
        WipeCompletion = 100,
        WipeFeather = 8,
    };

    [Fact]
    public void LegacyFadeBoolResolvesAsFade()
    {
        var cue = Clip(fadeIn: true, fadeOut: true);
        Assert.Equal(TransitionFilter.Fade, CueTransitions.ResolvedIn(cue));
        Assert.Equal(TransitionFilter.Fade, CueTransitions.ResolvedOut(cue));
        Assert.Equal(0, TimelineMath.FadeMultiplier(cue, 0));
        Assert.Equal(1, TimelineMath.FadeMultiplier(cue, 1000));
    }

    [Fact]
    public void WipeInKeepsOpacityAndRampsWipe()
    {
        var cue = Clip(fadeIn: true, inn: TransitionFilter.Wipe, inDur: 500);
        var start = Tweens.EvaluateCue(cue, 0);
        Assert.NotNull(start);
        Assert.Equal(100, start!.Opacity);
        Assert.Equal(0, start.Wipe);
        var mid = Tweens.EvaluateCue(cue, 250)!;
        Assert.Equal(100, mid.Opacity);
        Assert.Equal(50, mid.Wipe, 3);
        var open = Tweens.EvaluateCue(cue, 800)!;
        Assert.Equal(100, open.Wipe);
        Assert.Equal(100, open.ScaleX);
    }

    [Fact]
    public void ScaleOutShrinksWithoutFading()
    {
        var cue = Clip(fadeOut: true, outt: TransitionFilter.Scale, outDur: 500, duration: 2000);
        var mid = Tweens.EvaluateCue(cue, 1750)!;
        Assert.Equal(100, mid.Opacity);
        Assert.Equal(50, mid.ScaleX, 3);
        Assert.Equal(50, mid.ScaleY, 3);
        var last = Tweens.EvaluateCue(cue, 1999)!;
        Assert.True(last.ScaleX < 5);
    }

    [Fact]
    public void DissolveUsesSineInOut()
    {
        var fade = Clip(fadeIn: true, inn: TransitionFilter.Fade, inDur: 1000);
        var dissolve = Clip(fadeIn: true, inn: TransitionFilter.Dissolve, inDur: 1000);
        Assert.Equal(0.25, TimelineMath.FadeMultiplier(fade, 250), 3);
        Assert.InRange(TimelineMath.FadeMultiplier(dissolve, 250), 0.12, 0.18);
    }

    [Fact]
    public void ApplyTogglesTheSameFilterOff()
    {
        var cue = Clip();
        CueTransitions.Apply(cue, "in", TransitionFilter.Wipe);
        Assert.True(cue.FadeIn);
        Assert.Equal(TransitionFilter.Wipe, cue.FadeInFilter);
        CueTransitions.Apply(cue, "in", TransitionFilter.Wipe, toggleSame: true);
        Assert.False(cue.FadeIn);
        Assert.Equal(TransitionFilter.None, cue.FadeInFilter);
        CueTransitions.Apply(cue, "out", TransitionFilter.Fade);
        CueTransitions.ApplyDuration(cue, "out", 2000);
        Assert.True(cue.FadeOut);
        Assert.Equal(2000, cue.FadeOutDuration);
    }

    [Fact]
    public void SessionStoresStartAndEndFilters()
    {
        var session = new ProducerSession();
        session.NewShow();
        var cue = session.AddPlaceholderCue();
        Assert.NotNull(cue);
        session.SetCueTransition("in", TransitionFilter.Wipe);
        session.SetCueTransitionDuration("in", 1000);
        session.SetCueTransition("out", TransitionFilter.Scale);
        var live = session.Show!.Timelines[0].Cues.First(c => c.Id == cue!.Id);
        Assert.True(live.FadeIn);
        Assert.Equal(TransitionFilter.Wipe, live.FadeInFilter);
        Assert.Equal(1000, live.FadeInDuration);
        Assert.True(live.FadeOut);
        Assert.Equal(TransitionFilter.Scale, live.FadeOutFilter);

        var json = ShowSerializer.SaveCue(live);
        Assert.Contains("wipe", json, StringComparison.OrdinalIgnoreCase);
        var round = ShowSerializer.LoadCue(json);
        Assert.Equal(TransitionFilter.Wipe, round.FadeInFilter);
        Assert.Equal(TransitionFilter.Scale, round.FadeOutFilter);
    }
}
