using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Playback;
using Watchout.Core.Stage;
using Xunit;

namespace Watchout.Core.Tests;

public class CueLooksTests
{
    [Fact]
    public void SpeedScalesLocalMediaTime()
    {
        Assert.Equal(2000, CueLooks.MediaTime(1000, 200));
        Assert.Equal(500, CueLooks.MediaTime(1000, 50));
        Assert.Equal(1, CueLooks.SpeedRatio(100));
        var cue = new Cue { Start = 0, Duration = 10_000, Speed = 200, Enabled = true, Opacity = 100, Scale = new Vec2 { X = 100, Y = 100 } };
        var ev = Tweens.EvaluateCue(cue, 1000);
        Assert.NotNull(ev);
        Assert.Equal(2000, ev!.LocalTime);
    }

    [Fact]
    public void WipeStartsHiddenAndEndsVisible()
    {
        var hidden = CueLooks.WipeGradient(0, 0, 0);
        Assert.Equal(0, hidden.Soft0);
        Assert.Equal(0, hidden.Soft1);
        var open = CueLooks.WipeGradient(100, 0, 0);
        Assert.Equal(1, open.Soft0);
        Assert.Equal(1, open.Soft1);
        var mid = CueLooks.WipeGradient(50, 90, 20);
        Assert.True(mid.Soft0 < mid.Soft1);
        Assert.True(mid.Y2 > mid.Y1);
    }

    [Fact]
    public void ReplaceMediaKeepsOldBoxOrFits()
    {
        var prev = new Asset { Width = 1920, Height = 1080 };
        var next = new Asset { Width = 3840, Height = 2160 };
        var pos = new Vec3 { X = 100, Y = 40 };
        var scale = new Vec2 { X = 100, Y = 100 };

        var keep = CueLooks.ReplaceMedia(MediaReplaceMode.KeepOldSize, prev, next, pos, scale);
        Assert.Equal(100, keep.Position.X);
        Assert.Equal(50, keep.Scale.X);
        Assert.Equal(50, keep.Scale.Y);

        var fresh = CueLooks.ReplaceMedia(MediaReplaceMode.NewSize, prev, next, pos, scale);
        Assert.Equal(100, fresh.Scale.X);

        var fit = CueLooks.ReplaceMedia(MediaReplaceMode.FitProportionally, prev, next, pos, scale);
        Assert.Equal(50, fit.Scale.X);
        Assert.Equal(100, fit.Position.X);
        Assert.Equal(40, fit.Position.Y);
    }

    [Fact]
    public void PlaceholderAndReplaceRoundTripInSession()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1280, Height = 720, DurationMs = 8000, Fps = 30, Codec = "h264" };
        var media = MediaImport.FromProbe("/clips/a.mp4", "/library/a.mp4", probe, 1, true);
        session.ApplyImported(media);
        var cue = session.AddPlaceholderCue();
        Assert.NotNull(cue);
        Assert.Null(cue!.AssetId);
        session.Show!.Prefs.MediaReplaceMode = MediaReplaceMode.KeepOldSize;
        session.ReplaceCueMedia(cue.Id, media.Id);
        var live = session.Show.Timelines[0].Cues.First(c => c.Id == cue.Id);
        Assert.Equal(media.Id, live.AssetId);
        Assert.Equal(media.Name, live.Name);
    }

    [Fact]
    public void HexRoundTrip()
    {
        Assert.True(CueLooks.TryParseHex("#00FF00", out var r, out var g, out var b));
        Assert.Equal(0, r);
        Assert.Equal(255, g);
        Assert.Equal(0, b);
        Assert.Equal("#0A0B0C", CueLooks.ColorToHex(10, 11, 12));
    }
}
