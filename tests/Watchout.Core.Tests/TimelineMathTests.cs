using Watchout.Core.Models;
using Watchout.Core.Scheduling;
using Xunit;

namespace Watchout.Core.Tests;

public class TimelineMathTests
{
    static Cue Cue(string id, string layer, double start, double duration, bool fadeIn = false, bool fadeOut = false, double fadeInDur = 500, double fadeOutDur = 500) => new()
    {
        Id = id,
        Type = CueType.Media,
        Name = id,
        LayerId = layer,
        Start = start,
        Duration = duration,
        Enabled = true,
        Color = "#3b82c4",
        Position = new Vec3(),
        Scale = new Vec2 { X = 100, Y = 100 },
        Rotation = new Vec3(),
        Opacity = 100,
        Volume = 100,
        FadeIn = fadeIn,
        FadeOut = fadeOut,
        FadeInDuration = fadeInDur,
        FadeOutDuration = fadeOutDur,
        FadeCurve = Easing.Linear,
        Tweens = [],
        Crop = new Crop(),
        Anchor = new Vec2 { X = 0.5, Y = 0.5 },
    };

    [Fact]
    public void SameLayerOverlapIsConflictUntilCrossfade()
    {
        var a = Cue("a", "l1", 0, 4000);
        var b = Cue("b", "l1", 3000, 4000);
        Assert.True(TimelineMath.CuesOverlap(a, b));
        Assert.Equal(1000, TimelineMath.OverlapMs(a, b));
        Assert.False(TimelineMath.IsAllowedOverlap(a, b));
        Assert.True(TimelineMath.CueHasConflict(a, [b]));

        var fadedA = Cue("a", "l1", 0, 4000, fadeOut: true);
        var fadedB = Cue("b", "l1", 3000, 4000, fadeIn: true);
        Assert.True(TimelineMath.IsAllowedOverlap(fadedA, fadedB));
        Assert.False(TimelineMath.CueHasConflict(fadedA, [fadedB]));
    }

    [Fact]
    public void DifferentLayersMayOverlap()
    {
        var a = Cue("a", "l1", 0, 4000);
        var b = Cue("b", "l2", 0, 4000);
        Assert.True(TimelineMath.IsAllowedOverlap(a, b));
        Assert.False(TimelineMath.CueHasConflict(a, [b]));
    }

    [Fact]
    public void FadeMultiplierRamps()
    {
        var a = Cue("a", "l1", 0, 2000, fadeIn: true, fadeOut: true, fadeInDur: 500, fadeOutDur: 500);
        Assert.Equal(0, TimelineMath.FadeMultiplier(a, 0));
        Assert.True(Math.Abs(TimelineMath.FadeMultiplier(a, 250) - 0.5) < 0.001);
        Assert.Equal(1, TimelineMath.FadeMultiplier(a, 1000));
        Assert.True(Math.Abs(TimelineMath.FadeMultiplier(a, 1750) - 0.5) < 0.001);
        Assert.Equal(0, TimelineMath.FadeMultiplier(a, 2000));
    }

    [Fact]
    public void CrossfadeUsesOverlapLength()
    {
        var a = Cue("a", "l1", 0, 4000, fadeOut: true, fadeOutDur: 2000);
        var b = Cue("b", "l1", 3000, 4000, fadeIn: true, fadeInDur: 2000);
        Assert.Equal(0.5, TimelineMath.FadeMultiplier(a, 3500, [b]));
        Assert.Equal(0.5, TimelineMath.FadeMultiplier(b, 500, [a]));
    }

    [Fact]
    public void SnapAndPairHelpers()
    {
        Assert.Equal(100, TimelineMath.SnapTime(98, [0d, 100, 200], 10));
        Assert.Equal(30, TimelineMath.CueEnd(Cue("a", "l", 10, 20)));
        var a = Cue("a", "l1", 0, 1000);
        var b = Cue("b", "l1", 2000, 1000);
        var pair = TimelineMath.FindCrossfadePair([a, b], ["a"]);
        Assert.Equal("a", pair?.A.Id);
        Assert.Equal("b", pair?.B.Id);
    }

    [Fact]
    public void DeletingTimelinesLeavesAtLeastOne()
    {
        var tls = new[] { new Timeline { Id = "a" }, new Timeline { Id = "b" }, new Timeline { Id = "c" } };
        Assert.Equal(["a", "c"], TimelineMath.RemoveTimelinesById(tls, ["b"], t => t.Id).Select(t => t.Id));
        Assert.Equal(["a", "b", "c"], TimelineMath.RemoveTimelinesById(tls, ["a", "b", "c"], t => t.Id).Select(t => t.Id));
        Assert.Equal(["only"], TimelineMath.RemoveTimelinesById([new Timeline { Id = "only" }], ["only"], t => t.Id).Select(t => t.Id));
    }

    [Fact]
    public void PurgingAssetRemovesCues()
    {
        var show = new Show
        {
            Assets = [new Asset { Id = "ndi" }, new Asset { Id = "clip" }],
            Timelines =
            [
                new Timeline
                {
                    Cues =
                    [
                        new Cue { AssetId = "ndi" },
                        new Cue { AssetId = "clip" },
                        new Cue { AssetId = null },
                    ],
                },
            ],
        };
        var next = TimelineMath.PurgeAssets(show, ["ndi"]);
        Assert.Equal(["clip"], next.Assets.Select(a => a.Id));
        Assert.Equal(["clip", null], next.Timelines[0].Cues.Select(c => c.AssetId));
    }

    [Fact]
    public void CueClickNeverSeeks()
    {
        Assert.False(TimelineMath.TimelineClickSeeksPlayhead("cue", true));
        Assert.False(TimelineMath.TimelineClickSeeksPlayhead("cue", false));
        Assert.True(TimelineMath.TimelineClickSeeksPlayhead("lane", true));
        Assert.False(TimelineMath.TimelineClickSeeksPlayhead("lane", false));
        Assert.True(TimelineMath.TimelineClickSeeksPlayhead("ruler", false));
    }
}
