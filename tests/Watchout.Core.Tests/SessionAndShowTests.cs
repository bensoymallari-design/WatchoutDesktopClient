using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Playback;
using Watchout.Core.Persistence;
using Xunit;

namespace Watchout.Core.Tests;

public class SessionAndShowTests
{
    [Fact]
    public void DemoShowHasThreeDisplaysAndMediaCues()
    {
        var show = ShowFactory.MakeDemoShow();
        Assert.Equal(3, show.Displays.Count);
        Assert.Equal(5760, show.Displays.Sum(d => d.Width));
        Assert.Contains(show.Assets, a => a.Kind == AssetKind.Procedural);
        Assert.True(show.Timelines[0].Cues.Count > 5);
        var json = ShowSerializer.Save(show);
        Assert.Contains("\"gpu\"", json.ToLowerInvariant());
        var round = ShowSerializer.Load(json);
        Assert.Equal(show.Name, round.Name);
        Assert.Equal(3, round.Displays.Count);
        Assert.Equal(OutputType.GPU, round.Displays[0].OutputType);
    }

    [Fact]
    public void NewShowImportH264DoesNotCreateWebm()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 10_000, Fps = 60, Codec = "h264", HasAudio = true };
        var media = MediaImport.FromProbe("/shows/wall.mp4", "/library/wall.mp4", probe, 40_000_000, false);
        Assert.True(media.Optimized);
        Assert.Contains("DXVA", media.Notes);
        Assert.DoesNotContain("WebM", media.Notes.Replace("no WebM", ""));
        Assert.False(Codecs.NeedsH264Transcode(media.Codec, media.OriginalPath));
        session.ApplyImported(media);
        Assert.Single(session.Show!.Assets);
        var cue = session.AddCueFromAsset(media.Id);
        Assert.NotNull(cue);
        Assert.Equal(media.Id, cue!.AssetId);
    }

    [Fact]
    public void PlaybackClockLoopsAndStops()
    {
        var session = new ProducerSession();
        session.OpenDemo();
        var tl = session.ActiveTimeline!;
        tl.Duration = 1000;
        tl.Loop = true;
        session.SetPlayback(tl.Id, PlaybackState.Play);
        session.Tick(2500);
        Assert.Equal(PlaybackState.Play, tl.Playback);
        Assert.True(tl.Playhead < 1000);

        tl.Loop = false;
        tl.Playhead = 0;
        session.Tick(2500);
        Assert.Equal(PlaybackState.Stop, tl.Playback);
        Assert.Equal(1000, tl.Playhead);
    }

    [Fact]
    public void EvaluateCueAppliesFadeAndTween()
    {
        var cue = ShowFactory.EmptyCue(new Cue
        {
            LayerId = "l",
            Start = 0,
            Duration = 2000,
            FadeIn = true,
            FadeInDuration = 1000,
            Opacity = 100,
            Tweens = [Tweens.MakeTween(TweenType.PositionX, (0, 0, Easing.Linear), (2000, 200, Easing.Linear))],
        });
        var ev = Tweens.EvaluateCue(cue, 1000);
        Assert.NotNull(ev);
        Assert.Equal(100, ev!.X);
        Assert.True(ev.Opacity > 99);
        Assert.Null(Tweens.EvaluateCue(cue, 2500));
    }

    [Fact]
    public void ElectronStyleJsonRoundTripUsesCamelCaseCueType()
    {
        var show = ShowFactory.EmptyShow();
        show.Timelines[0].Cues.Add(ShowFactory.EmptyCue(new Cue { Name = "Clip", LayerId = show.Timelines[0].Layers[0].Id, Start = 0, Duration = 1000 }));
        var json = ShowSerializer.Save(show);
        Assert.Contains("\"type\": \"media\"", json);
        Assert.Contains("\"playback\": \"stop\"", json);
        Assert.DoesNotContain("\"type\": \"Media\"", json);
    }

    [Fact]
    public void CaptureUrlRoundTripAndConnectCaptureAddsLiveCue()
    {
        Assert.True(LiveSources.IsCaptureUrl("capture:elgato-1"));
        Assert.Equal("elgato-1", LiveSources.CaptureDeviceId("capture:elgato-1"));
        Assert.Equal("capture:elgato-1", LiveSources.CaptureUrl("elgato-1"));

        var session = new ProducerSession();
        session.NewShow();
        var asset = session.ConnectCapture("elgato-1", "Elgato 4K");
        Assert.Equal(AssetKind.Capture, asset.Kind);
        Assert.Equal("capture:elgato-1", asset.Url);
        Assert.True(LiveSources.IsCapture(asset));
        Assert.Contains(session.Show!.CaptureDevices, d => d.Signal == "elgato-1");
        var cue = session.Show.Timelines.SelectMany(t => t.Cues).First(c => c.AssetId == asset.Id);
        Assert.True(cue.FreeRunning);
        Assert.True(cue.Duration >= LiveSources.LiveCueDurationMs);
        Assert.NotNull(Tweens.EvaluateCue(cue, 0));
        Assert.NotNull(Tweens.EvaluateCue(cue, cue.Duration + 5_000));
    }

    [Fact]
    public void FreeRunningCueStaysVisibleOutsideWindow()
    {
        var cue = ShowFactory.EmptyCue(new Cue
        {
            LayerId = "l",
            Start = 1_000,
            Duration = 500,
            FreeRunning = true,
            Opacity = 100,
        });
        Assert.NotNull(Tweens.EvaluateCue(cue, 0));
        Assert.NotNull(Tweens.EvaluateCue(cue, 10_000));
    }
}
