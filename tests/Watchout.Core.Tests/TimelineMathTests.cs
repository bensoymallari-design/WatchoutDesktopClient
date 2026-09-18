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
    public void ContentEndUsesTheLongestClipAndSkipsLiveDayCues()
    {
        var shortClip = Cue("a", "l1", 0, 8_000);
        var longClip = Cue("b", "l2", 2_000, 20_000);
        var live = Cue("live", "l3", 0, 24 * 60 * 60 * 1000);
        var marker = Cue("m", "l4", 50_000, 0);
        marker.Type = CueType.Marker;
        Assert.Equal(22_000, TimelineMath.ContentEnd([shortClip, longClip, live, marker]));
        Assert.Equal(22_000, TimelineMath.FitDuration(22_000));
        Assert.InRange(TimelineMath.FitZoom(22_000, 780, 120), 0.02, 0.04);
        var hour = Cue("hour", "l1", 0, 90 * 60 * 1000);
        Assert.Equal(90 * 60 * 1000, TimelineMath.ContentEnd([hour, live]));
        Assert.Equal(0, TimelineMath.FirstFiniteMediaStart([live, marker]));
        Assert.Equal(0, TimelineMath.FirstFiniteMediaStart([longClip, shortClip]));
        Assert.Equal(2_000, TimelineMath.FirstFiniteMediaStart([longClip]));
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
        Assert.False(TimelineMath.CanRemoveTimeline(1));
        Assert.True(TimelineMath.CanRemoveTimeline(2));
    }

    [Fact]
    public void RemovingLayerDropsItsCuesAndKeepsOne()
    {
        var layers = new List<Layer> { new() { Id = "a" }, new() { Id = "b" } };
        var cues = new List<Cue> { new() { Id = "c1", LayerId = "a" }, new() { Id = "c2", LayerId = "b" } };
        var next = TimelineMath.RemoveLayer(layers, cues, "a");
        Assert.NotNull(next);
        Assert.Equal(["b"], next.Value.Layers.Select(l => l.Id));
        Assert.Equal(["c2"], next.Value.Cues.Select(c => c.Id));
        Assert.Null(TimelineMath.RemoveLayer([new Layer { Id = "only" }], cues, "only"));
        Assert.Equal(1, TimelineMath.InsertLayerIndex(layers, "a"));
        Assert.Equal(2, TimelineMath.InsertLayerIndex(layers, null));
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
    public void TimelineScrollClampsAndReveals()
    {
        Assert.Equal(0, TimelineMath.ClampScroll(-10, 10_000, 5_000));
        Assert.Equal(5_000, TimelineMath.ClampScroll(9_000, 10_000, 5_000));
        Assert.Equal(2_000, TimelineMath.ClampScroll(2_000, 10_000, 5_000));
        Assert.Equal(0, TimelineMath.ClampScroll(100, 5_000, 8_000));
        Assert.Equal(1_000, TimelineMath.ScrollToShow(1_000, 2_000, 3_000, 5_000));
        Assert.Equal(1_000, TimelineMath.ScrollToShow(5_000, 6_000, 0, 5_000));
        Assert.Equal(0, TimelineMath.ScrollToShow(100, 200, 0, 5_000));
        Assert.Equal(4_000, TimelineMath.ScrollToShow(4_000, 20_000, 0, 5_000));
        Assert.Equal(15_000, TimelineMath.ExtendDurationTo(10_000, 15_000));
        Assert.Equal(10_000, TimelineMath.ExtendDurationTo(10_000, 4_000));
        Assert.Equal(680, TimelineMath.VisibleDurationMs(800, 1, 120), 3);
        Assert.Equal((120, 60), TimelineMath.ClipCueBar(80, 100, 120, 800));
        Assert.Equal((200, 100), TimelineMath.ClipCueBar(200, 100, 120, 800));
        Assert.Null(TimelineMath.ClipCueBar(0, 50, 120, 800));
        Assert.Equal((790, 10), TimelineMath.ClipCueBar(790, 50, 120, 800));
        Assert.True(TimelineMath.FitZoom(917_271, 1000, 120) < 0.002);
        Assert.InRange(TimelineMath.FitZoom(917_271, 1000, 120), 0.0008, 0.0012);
    }

    [Fact]
    public void LayerScrollRevealsTheLastLane()
    {
        Assert.Equal(0, TimelineMath.ClampLayerScroll(-10, 10, 28, 100));
        Assert.Equal(180, TimelineMath.ClampLayerScroll(500, 10, 28, 100));
        Assert.Equal(180, TimelineMath.LayerScrollToShow(9, 28, 0, 100));
        Assert.Equal(0, TimelineMath.LayerScrollToShow(0, 28, 0, 100));
        Assert.Equal(0, TimelineMath.LayerScrollToShow(0, 28, 40, 100));
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

    [Fact]
    public void CueBarHitSplitsStartAndEnd()
    {
        Assert.Equal(TimelineMath.CueBarPart.Start, TimelineMath.HitCueBar(4, 120));
        Assert.Equal(TimelineMath.CueBarPart.Body, TimelineMath.HitCueBar(60, 120));
        Assert.Equal(TimelineMath.CueBarPart.End, TimelineMath.HitCueBar(110, 120));
        Assert.Equal(TimelineMath.CueBarPart.Start, TimelineMath.HitCueBar(4, 20));
        Assert.Equal(TimelineMath.CueBarPart.End, TimelineMath.HitCueBar(16, 20));
    }

    [Fact]
    public void LayerHeaderHitsEyeAndLockIcons()
    {
        Assert.Equal(TimelineMath.LayerHeaderPart.Eye, TimelineMath.HitLayerHeader(TimelineMath.HeaderWidth - 2));
        Assert.Equal(TimelineMath.LayerHeaderPart.Lock, TimelineMath.HitLayerHeader(TimelineMath.LayerLockLeft() + 1));
        Assert.Equal(TimelineMath.LayerHeaderPart.Body, TimelineMath.HitLayerHeader(8));
        var layers = new List<Layer>
        {
            new() { Id = "a", Enabled = false, Locked = false },
            new() { Id = "b", Enabled = true, Locked = true },
        };
        Assert.False(TimelineMath.LayerIsVisible(layers, "a"));
        Assert.True(TimelineMath.LayerIsVisible(layers, "b"));
        Assert.True(TimelineMath.LayerIsLocked(layers, "b"));
        Assert.False(TimelineMath.LayerIsLocked(layers, "a"));
        Assert.Equal(0, TimelineMath.LayerStackIndex(layers, "a"));
        Assert.Equal(1, TimelineMath.LayerStackIndex(layers, "b"));
    }
}
