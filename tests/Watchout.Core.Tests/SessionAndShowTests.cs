using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Playback;
using Watchout.Core.Persistence;
using Watchout.Core.Scheduling;
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
    public void DeleteAssetRemovesImportedVideoAndItsCues()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 8_000, Fps = 60, Codec = "h264" };
        var media = MediaImport.FromProbe("/clips/wall.mp4", "/library/wall.mp4", probe, 1_000_000, true);
        session.ApplyImported(media);
        var cue = session.AddCueFromAsset(media.Id);
        Assert.NotNull(cue);
        Assert.Equal(SelectionKind.Cue, session.Selection.Kind);

        session.DeleteAsset(media.Id);
        Assert.Empty(session.Show!.Assets);
        Assert.DoesNotContain(session.Show.Timelines.SelectMany(t => t.Cues), c => c.AssetId == media.Id);
        Assert.Contains(session.Logs, l => l.Message.Contains("Deleted asset"));

        session.ApplyImported(media);
        session.Select(SelectionKind.Asset, media.Id);
        session.DeleteSelected();
        Assert.Empty(session.Show.Assets);
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
        Assert.Equal(0, PlaybackClock.WrapPlayhead(1000, 1000));
        Assert.Equal(0, PlaybackClock.WrapPlayhead(2000, 1000));
        Assert.Equal(500, PlaybackClock.WrapPlayhead(1500, 1000));
        Assert.True(PlaybackClock.WrapPlayhead(1000.0000001, 1000) < 1);

        tl.Loop = false;
        tl.Playhead = 0;
        session.Tick(2500);
        Assert.Equal(PlaybackState.Stop, tl.Playback);
        Assert.Equal(1000, tl.Playhead);
    }

    [Fact]
    public void LoopWrapsAtLastClipWhenTimelineIsADayLong()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 10_000, Fps = 60, Codec = "h264" };
        var media = MediaImport.FromProbe("/clips/wall.mp4", "/library/wall.mp4", probe, 1, true);
        session.ApplyImported(media);
        var cue = session.AddCueFromAsset(media.Id, start: 0)!;
        var tl = session.ActiveTimeline!;
        tl.Duration = LiveSources.LiveCueDurationMs;
        tl.Loop = true;
        tl.Playhead = 0;
        session.SetPlayback(tl.Id, PlaybackState.Play);
        session.Tick(12_500);
        Assert.Equal(PlaybackState.Play, tl.Playback);
        Assert.True(tl.Playhead < 10_000);
        Assert.Contains(PlaybackClock.VisibleMedia(session.Show!), e => e.Cue.Id == cue.Id);
    }

    [Fact]
    public void LoopWrapSnapsOntoAClipThatDoesNotStartAtZero()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 10_000, Fps = 60, Codec = "h264" };
        var media = MediaImport.FromProbe("/clips/later.mp4", "/library/later.mp4", probe, 1, true);
        session.ApplyImported(media);
        var cue = session.AddCueFromAsset(media.Id, start: 2_000)!;
        var tl = session.ActiveTimeline!;
        tl.Duration = 12_000;
        tl.Loop = true;
        tl.Playhead = 0;
        session.SetPlayback(tl.Id, PlaybackState.Play);
        session.Tick(12_500);
        Assert.Equal(PlaybackState.Play, tl.Playback);
        Assert.True(tl.Playhead >= 2_000);
        Assert.True(tl.Playhead < 12_000);
        Assert.Contains(PlaybackClock.VisibleMedia(session.Show!), e => e.Cue.Id == cue.Id);
        Assert.True(PlaybackClock.HasVisibleMediaCue(tl, tl.Playhead));
    }

    [Fact]
    public void PlayAndSpaceSnapBackWhenPlayheadIsPastTheClip()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 605_271, Fps = 60, Codec = "h264" };
        var media = MediaImport.FromProbe("/clips/4k.mp4", "/library/4k.mp4", probe, 1, true);
        session.ApplyImported(media);
        var cue = session.AddCueFromAsset(media.Id, start: 0)!;
        var tl = session.ActiveTimeline!;
        tl.Duration = LiveSources.LiveCueDurationMs;
        tl.Loop = true;
        tl.Playhead = 730_758;
        Assert.DoesNotContain(PlaybackClock.VisibleMedia(session.Show!), e => e.Cue.Id == cue.Id);

        session.SetPlayback(tl.Id, PlaybackState.Play);
        Assert.Equal(PlaybackState.Play, tl.Playback);
        Assert.True(tl.Playhead < 605_271);
        Assert.Contains(PlaybackClock.VisibleMedia(session.Show!), e => e.Cue.Id == cue.Id);

        tl.Playhead = 730_758;
        Assert.Equal(PlaybackState.Play, PlaybackClock.ToggleTarget(tl));
        session.TogglePlay();
        Assert.Equal(PlaybackState.Play, tl.Playback);
        Assert.True(tl.Playhead < 605_271);

        tl.Playhead = 730_758;
        session.Stop(tl.Id);
        Assert.Equal(PlaybackState.Stop, tl.Playback);
        Assert.Equal(0, tl.Playhead);
        Assert.Contains(PlaybackClock.VisibleMedia(session.Show!), e => e.Cue.Id == cue.Id);
    }

    [Fact]
    public void PuttingTheClipBackWhilePlayheadIsPastSnapsOntoIt()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 10_000, Fps = 60, Codec = "h264" };
        var media = MediaImport.FromProbe("/clips/wall.mp4", "/library/wall.mp4", probe, 1, true);
        session.ApplyImported(media);
        var first = session.AddCueFromAsset(media.Id, start: 0)!;
        var tl = session.ActiveTimeline!;
        tl.Duration = LiveSources.LiveCueDurationMs;
        tl.Loop = true;
        tl.Playhead = 3_600_000;
        session.SetPlayback(tl.Id, PlaybackState.Play);
        Assert.True(tl.Playhead < 10_000);

        session.DeleteSelected();
        Assert.DoesNotContain(tl.Cues, c => c.Id == first.Id);
        tl.Playhead = 3_600_000;
        Assert.False(PlaybackClock.HasVisibleMediaCue(tl, tl.Playhead));
        var again = session.AddCueFromAsset(media.Id, start: 0)!;
        Assert.Equal(0, tl.Playhead);
        Assert.Contains(PlaybackClock.VisibleMedia(session.Show!), e => e.Cue.Id == again.Id);
        Assert.Equal(0, PlaybackClock.LoopFileTime(3_600_000, 10_000));
        Assert.Equal(500, PlaybackClock.LoopFileTime(10_500, 10_000));
    }

    [Fact]
    public void OnceStopsAtLastClipWhenTimelineIsLonger()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 10_000, Fps = 60, Codec = "h264" };
        var media = MediaImport.FromProbe("/clips/once.mp4", "/library/once.mp4", probe, 1, true);
        session.ApplyImported(media);
        session.AddCueFromAsset(media.Id, start: 0);
        var tl = session.ActiveTimeline!;
        tl.Duration = LiveSources.LiveCueDurationMs;
        tl.Loop = false;
        tl.Playhead = 0;
        session.SetPlayback(tl.Id, PlaybackState.Play);
        session.Tick(12_000);
        Assert.Equal(PlaybackState.Stop, tl.Playback);
        Assert.Equal(10_000, tl.Playhead);
    }

    [Fact]
    public void LiveOnlyTimelineStillLoopsOnShowDuration()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.ApplyImported(LiveSources.NdiAsset("CAM 1", null));
        var ndi = session.Show!.Assets.Single(a => a.Kind == AssetKind.Ndi);
        session.AddCueFromAsset(ndi.Id);
        var tl = session.ActiveTimeline!;
        tl.Duration = 120_000;
        tl.Loop = true;
        tl.Playhead = 0;
        Assert.Equal(120_000, PlaybackClock.LoopSpan(tl));
        session.SetPlayback(tl.Id, PlaybackState.Play);
        session.Tick(150_000);
        Assert.Equal(PlaybackState.Play, tl.Playback);
        Assert.True(tl.Playhead < 120_000);
        Assert.Contains(PlaybackClock.VisibleMedia(session.Show), e => e.Cue.AssetId == ndi.Id);
    }

    [Fact]
    public void TickUsesClockNotFullUiRebuild()
    {
        var session = new ProducerSession();
        session.NewShow();
        var changes = 0;
        var clocks = 0;
        session.Changed += () => changes++;
        session.Clock += () => clocks++;
        session.Play();
        var afterPlay = changes;
        session.Tick(16);
        Assert.Equal(afterPlay, changes);
        Assert.Equal(1, clocks);
        Assert.True(session.ActiveTimeline!.Playhead > 0);
    }

    [Fact]
    public void PlayAndStopKeepTheWarmDecoder()
    {
        var session = new ProducerSession();
        session.NewShow();
        Assert.Equal(0, session.DecoderEpoch);
        session.Play();
        Assert.Equal(0, session.DecoderEpoch);
        session.Play();
        Assert.Equal(0, session.DecoderEpoch);
        session.Pause();
        Assert.Equal(0, session.DecoderEpoch);
        session.Stop();
        Assert.Equal(0, session.DecoderEpoch);
    }

    [Fact]
    public void LiveOutputTakesTheOnlyFileDecoder()
    {
        var session = new ProducerSession();
        session.NewShow();
        Assert.False(session.StageYieldsFileDecoder);
        session.LiveOutputs.Add("wall");
        Assert.True(session.StageYieldsFileDecoder);
        var epoch = session.DecoderEpoch;
        session.NoteLiveOutputsChanged();
        Assert.True(session.DecoderEpoch > epoch);
    }

    [Fact]
    public void LiveCueMoveUsesLayoutNotFullRebuild()
    {
        var session = new ProducerSession();
        session.OpenDemo();
        var cue = session.Show!.Timelines[0].Cues.First(c => c.Type == CueType.Media && c.AssetId is not null);
        var changes = 0;
        var layouts = 0;
        session.Changed += () => changes++;
        session.LayoutChanged += () => layouts++;

        session.LiveUpdateCue(cue.Id, c => c.Position = new Vec3 { X = 120, Y = 40, Z = c.Position.Z });
        Assert.Equal(0, changes);
        Assert.Equal(1, layouts);
        Assert.Equal(120, cue.Position.X);
        Assert.Equal(40, cue.Position.Y);

        session.LiveUpdateCue(cue.Id, c => c.Position = new Vec3 { X = 120, Y = 40, Z = c.Position.Z });
        Assert.Equal(1, layouts);

        session.LiveUpdateCue(cue.Id, c => c.Scale = new Vec2 { X = 200, Y = 100 });
        Assert.Equal(0, changes);
        Assert.Equal(2, layouts);
        Assert.Equal(200, cue.Scale.X);

        var display = session.Show.Displays[0];
        session.LiveUpdateDisplay(display.Id, d => d.X = 64);
        Assert.Equal(0, changes);
        Assert.Equal(3, layouts);
        Assert.Equal(64, display.X);

        session.SetCamera(10, 20, 0.4);
        Assert.Equal(0, changes);
        Assert.Equal(4, layouts);
        Assert.Equal(10, session.Camera.X);
    }

    [Fact]
    public void StageLayoutBusyHoldsUntilReleased()
    {
        var session = new ProducerSession();
        session.NewShow();
        var layouts = 0;
        session.LayoutChanged += () => layouts++;
        session.SetStageLayoutBusy(true);
        Assert.True(session.StageLayoutBusy);
        Assert.Equal(1, layouts);
        session.SetStageLayoutBusy(true);
        Assert.Equal(1, layouts);
        session.SetStageLayoutBusy(false);
        Assert.False(session.StageLayoutBusy);
        Assert.Equal(2, layouts);
    }

    [Fact]
    public void FrameDisplaysCentersTheWallInTheStageView()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.ReportStageView(800, 450);
        session.FrameDisplays();
        Assert.Equal(960, session.Camera.X);
        Assert.Equal(540, session.Camera.Y);
        Assert.InRange(session.Camera.Zoom, 0.25, 0.5);
        session.FrameDisplay(session.Show!.Displays[0].Id);
        Assert.Equal(960, session.Camera.X);
    }

    [Fact]
    public void FitTimelineToMediaUsesTheLongestClip()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 12_500, Fps = 30, Codec = "h264" };
        var media = MediaImport.FromProbe("/clips/long.mp4", "/library/long.mp4", probe, 1_000_000, true);
        session.ApplyImported(media);
        session.AddCueFromAsset(media.Id);
        Assert.True(session.FitTimelineToMedia());
        Assert.Equal(12_500, session.ActiveTimeline!.Duration);
        Assert.Equal(0, session.TimelineScroll);
        Assert.False(session.FitTimelineToMedia("missing"));
    }

    [Fact]
    public void AddLayerScrollsTheLastLaneIntoView()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.ReportTimelineView(800, 80);
        Assert.Equal(0, session.TimelineLayerScroll);
        session.AddLayer();
        var tl = session.ActiveTimeline!;
        Assert.Equal(11, tl.Layers.Count);
        var last = tl.Layers.Count - 1;
        var top = last * TimelineMath.LaneHeight;
        var visible = 80 - TimelineMath.RulerHeight;
        Assert.True(session.TimelineLayerScroll > 0);
        Assert.InRange(top, session.TimelineLayerScroll, session.TimelineLayerScroll + visible);
        Assert.InRange(top + TimelineMath.LaneHeight, session.TimelineLayerScroll, session.TimelineLayerScroll + visible + 0.001);
    }

    [Fact]
    public void AddCuePastTheEndExtendsDurationAndScrolls()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.ReportTimelineView(800, 200);
        session.SetTimelineZoom(0.1);
        session.UpdateTimeline(session.ActiveTimelineId!, t => t.Duration = 5_000);
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 10_000, Fps = 30, Codec = "h264" };
        var media = MediaImport.FromProbe("/clips/tail.mp4", "/library/tail.mp4", probe, 1_000_000, true);
        session.ApplyImported(media);
        var cue = session.AddCueFromAsset(media.Id, start: 5_000);
        Assert.NotNull(cue);
        Assert.Equal(5_000, cue!.Start);
        Assert.True(session.ActiveTimeline!.Duration >= 15_000);
        Assert.True(session.TimelineScroll > 0);
        var viewEnd = session.TimelineScroll + session.VisibleDurationMs();
        Assert.InRange(cue.Start, session.TimelineScroll - 1, viewEnd);
    }

    [Fact]
    public void PlaceAtEndStartsAfterTheLastClip()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.ReportTimelineView(800, 200);
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 8_000, Fps = 30, Codec = "h264" };
        var first = MediaImport.FromProbe("/clips/a.mp4", "/library/a.mp4", probe, 1_000_000, true);
        var second = MediaImport.FromProbe("/clips/b.mp4", "/library/b.mp4", probe, 1_000_000, true);
        session.ApplyImported(first);
        session.ApplyImported(second);
        session.AddCueFromAsset(first.Id, start: 0);
        var tail = session.AddCueAtEnd(second.Id);
        Assert.NotNull(tail);
        Assert.Equal(8_000, tail!.Start);
        Assert.True(session.FitTimelineToMedia());
        Assert.Equal(16_000, session.ActiveTimeline!.Duration);
        Assert.Equal(0, session.TimelineScroll);
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
    public void ConnectsFiveCaptureCardsOntoSeparateDisplaysAndLayers()
    {
        var session = new ProducerSession();
        session.NewShow();
        Assert.Single(session.Show!.Displays);

        var count = session.ConnectCaptures(Enumerable.Range(1, 5).Select(i => ($"card-{i}", $"Card {i}")));
        Assert.Equal(5, count);
        Assert.Equal(5, session.Show.Displays.Count);
        Assert.Equal(5, session.Show.Assets.Count(LiveSources.IsCapture));
        var cues = LiveSources.CaptureCues(session.Show);
        Assert.Equal(5, cues.Count);
        Assert.Equal(5, cues.Select(c => c.Position.X).Distinct().Count());
        Assert.Equal(5, cues.Select(c => c.LayerId).Distinct().Count());
        session.ConnectCaptures([("card-1", "Card 1")]);
        Assert.Equal(5, LiveSources.CaptureCues(session.Show).Count);
    }

    [Fact]
    public void ConnectAllPinsEachCardToAnExistingStageDisplayInOrder()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.AddDisplay();
        session.AddDisplay();
        Assert.Equal(3, session.Show!.Displays.Count);

        var count = session.ConnectCaptures([
            ("cam-a", "DeckLink 1"),
            ("cam-b", "DeckLink 2"),
            ("cam-c", "DeckLink 3"),
        ]);

        Assert.Equal(3, count);
        Assert.Equal(3, session.Show.Displays.Count);
        Assert.Equal(session.Show.Displays[0].Id, session.Show.CaptureDevices.First(d => d.Signal == "cam-a").DisplayId);
        Assert.Equal(session.Show.Displays[1].Id, session.Show.CaptureDevices.First(d => d.Signal == "cam-b").DisplayId);
        Assert.Equal(session.Show.Displays[2].Id, session.Show.CaptureDevices.First(d => d.Signal == "cam-c").DisplayId);
        Assert.Equal("cam-a", LiveSources.CaptureOnDisplay(session.Show, session.Show.Displays[0].Id));
        Assert.Equal("cam-b", LiveSources.CaptureOnDisplay(session.Show, session.Show.Displays[1].Id));
        Assert.Equal("cam-c", LiveSources.CaptureOnDisplay(session.Show, session.Show.Displays[2].Id));
        Assert.Equal("DeckLink 1", LiveSources.CaptureNameOnDisplay(session.Show, session.Show.Displays[0].Id));
    }

    [Fact]
    public void CaptureCardPinsToAChosenStageDisplay()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.AddDisplay();
        var left = session.Show!.Displays[0];
        var right = session.Show.Displays[1];
        session.ConnectCapture("elgato-2", "Elgato 2", displayId: right.Id);
        Assert.Equal(2, session.Show.Displays.Count);
        var cue = LiveSources.CaptureCue(session.Show, "elgato-2");
        Assert.NotNull(cue);
        Assert.Equal(right.X, cue!.Position.X);
        Assert.Equal(right.Id, session.Show.CaptureDevices.First(d => d.Signal == "elgato-2").DisplayId);

        session.AssignCaptureToDisplay("elgato-2", left.Id);
        cue = LiveSources.CaptureCue(session.Show, "elgato-2");
        Assert.Equal(left.X, cue!.Position.X);
        Assert.Equal(left.Id, LiveSources.CaptureDisplayKey(session.Show, "elgato-2"));
        Assert.Equal("elgato-2", LiveSources.CaptureOnDisplay(session.Show, left.Id));
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

    [Fact]
    public void DropAssetOnStageFitsTheTargetDisplayAndIsPlayableAtPlayhead()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.AddDisplay();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 8_000, Fps = 60, Codec = "h264" };
        var media = MediaImport.FromProbe("/clips/wall.mp4", "/library/wall.mp4", probe, 1_000_000, true);
        session.ApplyImported(media);
        var right = session.Show!.Displays[1];
        var cue = session.DropAssetOnStage(media.Id, right.Id, 0, 0);
        Assert.NotNull(cue);
        Assert.Equal(right.X, cue!.Position.X);
        Assert.Equal(right.Y, cue.Position.Y);
        Assert.Equal(session.ActiveTimeline!.Playhead, cue.Start);
        Assert.NotNull(Tweens.EvaluateCue(cue, session.ActiveTimeline.Playhead));
        session.SetPlayback(session.ActiveTimeline.Id, PlaybackState.Play);
        Assert.Equal(PlaybackState.Play, session.ActiveTimeline.Playback);
        session.Tick(500);
        Assert.True(session.ActiveTimeline.Playhead >= 500);

        var loose = session.DropAssetOnStage(media.Id, null, 400, 80);
        Assert.Equal(400, loose!.Position.X);
        Assert.Equal(80, loose.Position.Y);
    }

    [Fact]
    public void DeleteTimelineKeepsOneAndSelectsTheRest()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.AddTimeline();
        session.AddTimeline();
        Assert.Equal(3, session.Show!.Timelines.Count);
        var doomed = session.ActiveTimelineId;
        session.DeleteTimeline(doomed);
        Assert.Equal(2, session.Show.Timelines.Count);
        Assert.NotEqual(doomed, session.ActiveTimelineId);
        session.Select(SelectionKind.Timeline, session.Show.Timelines[0].Id);
        session.DeleteSelected();
        Assert.Single(session.Show.Timelines);
        session.DeleteTimeline();
        Assert.Single(session.Show.Timelines);
        Assert.Contains(session.Logs, l => l.Message.Contains("at least one timeline"));
    }

    [Fact]
    public void ToggleLoopAndLayerEdit()
    {
        var session = new ProducerSession();
        session.NewShow();
        var tl = session.ActiveTimeline!;
        Assert.True(tl.Loop);
        session.SetLoop(tl.Id, false);
        Assert.False(tl.Loop);
        session.ToggleLoop();
        Assert.True(tl.Loop);

        var before = tl.Layers.Count;
        session.AddLayer();
        Assert.Equal(before + 1, tl.Layers.Count);
        Assert.Equal(SelectionKind.Layer, session.Selection.Kind);
        var insertedAt = tl.Layers.Count;
        session.InsertLayer(session.Selection.Ids[0]);
        Assert.Equal(insertedAt + 1, tl.Layers.Count);
        session.DeleteLayer(session.Selection.Ids[0]);
        Assert.Equal(before + 1, tl.Layers.Count);
        session.Select(SelectionKind.Layer, tl.Layers[0].Id);
        session.UpdateLayer(tl.Layers[0].Id, l => l.Locked = true);
        Assert.True(tl.Layers[0].Locked);
    }

    [Fact]
    public void DisplayCanvasModeIgnoresCuesUntilToggledBack()
    {
        var session = new ProducerSession();
        session.NewShow();
        Assert.Equal(StageEditMode.Cues, session.StageEditMode);
        session.SetStageEditMode(StageEditMode.Displays);
        Assert.Equal(StageEditMode.Displays, session.StageEditMode);
        session.Select(SelectionKind.Display, session.Show!.Displays[0].Id);
        session.NudgeSelected(40, -10);
        Assert.Equal(40, session.Show.Displays[0].X);
        Assert.Equal(-10, session.Show.Displays[0].Y);
        session.SetStageEditMode(StageEditMode.Cues);
        Assert.Equal(StageEditMode.Cues, session.StageEditMode);
    }

    [Fact]
    public void SelectingACueLeavesDisplayCanvasAndEditsTheClip()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.SetStageEditMode(StageEditMode.Displays);
        session.Select(SelectionKind.Cue, "overlay");
        Assert.Equal(StageEditMode.Cues, session.StageEditMode);
        Assert.Equal(SelectionKind.Cue, session.Selection.Kind);
    }

    [Fact]
    public void AssignDisplayScreenAndCopyControllerSize()
    {
        var session = new ProducerSession();
        session.NewShow();
        var id = session.Show!.Displays[0].Id;
        session.AssignDisplayScreen(id, "mctrl4k");
        Assert.Equal("mctrl4k", session.Show.Displays[0].ScreenId);
        session.AssignDisplayScreen(id, "mctrl4k", new OutputScreen
        {
            Id = "mctrl4k",
            Label = "MCTRL4K",
            Width = 1920,
            Height = 1080,
            PhysicalWidth = 3840,
            PhysicalHeight = 1080,
        });
        Assert.Equal(3840, session.Show.Displays[0].Width);
        Assert.Equal(1080, session.Show.Displays[0].Height);
        session.CopyScreenSizeToDisplay(id, new OutputScreen
        {
            Id = "mctrl4k",
            Label = "MCTRL4K",
            Width = 3840,
            Height = 1080,
            PhysicalWidth = 3840,
            PhysicalHeight = 1080,
        });
        Assert.Equal(3840, session.Show.Displays[0].Width);
        Assert.Equal(1080, session.Show.Displays[0].Height);
        session.AssignDisplayScreen(id, "auto:2");
        Assert.Null(session.Show.Displays[0].ScreenId);
        Assert.Equal(2, session.Show.Displays[0].Channel);
    }

    [Fact]
    public void HiddenLayerCuesAreNotVisibleOnStage()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 10_000, Fps = 60, Codec = "h264" };
        var media = MediaImport.FromProbe("/shows/wall.mp4", "/library/wall.mp4", probe, 40_000_000, false);
        session.ApplyImported(media);
        var layer = session.ActiveTimeline!.Layers[0];
        var cue = session.AddCueFromAsset(media.Id, layer.Id, 0)!;
        session.ActiveTimeline.Playhead = 500;
        Assert.Contains(PlaybackClock.VisibleMedia(session.Show!), e => e.Cue.Id == cue.Id);
        session.ToggleLayerVisible(layer.Id);
        Assert.False(layer.Enabled);
        Assert.DoesNotContain(PlaybackClock.VisibleMedia(session.Show!), e => e.Cue.Id == cue.Id);
        session.ToggleLayerVisible(layer.Id);
        Assert.True(layer.Enabled);
        Assert.Contains(PlaybackClock.VisibleMedia(session.Show!), e => e.Cue.Id == cue.Id);
    }

    [Fact]
    public void LockedLayerBlocksCueEditsAndDrops()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 10_000, Fps = 60, Codec = "h264" };
        var media = MediaImport.FromProbe("/shows/wall.mp4", "/library/wall.mp4", probe, 40_000_000, false);
        session.ApplyImported(media);
        var layer = session.ActiveTimeline!.Layers[0];
        var cue = session.AddCueFromAsset(media.Id, layer.Id, 0)!;
        var start = cue.Start;
        var x = cue.Position.X;
        session.ToggleLayerLocked(layer.Id);
        Assert.True(layer.Locked);
        Assert.True(session.CueLayerLocked(cue.Id));
        session.UpdateCue(cue.Id, c => c.Start = 9000);
        Assert.Equal(start, cue.Start);
        session.LiveUpdateCue(cue.Id, c => c.Position = new Vec3 { X = 999, Y = c.Position.Y, Z = c.Position.Z });
        Assert.Equal(x, cue.Position.X);
        session.Select(SelectionKind.Cue, cue.Id);
        session.NudgeSelected(40, 10);
        Assert.Equal(x, cue.Position.X);
        session.DeleteSelected();
        Assert.Contains(session.ActiveTimeline.Cues, c => c.Id == cue.Id);
        Assert.Null(session.AddCueFromAsset(media.Id, layer.Id, 1000));
        session.ToggleLayerLocked(layer.Id);
        Assert.False(layer.Locked);
        session.UpdateCue(cue.Id, c => c.Start = 2000);
        Assert.Equal(2000, cue.Start);
    }

    [Fact]
    public void VisibleMediaDrawsLaterTimelineLayersInFront()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 10_000, Fps = 60, Codec = "h264" };
        var a = MediaImport.FromProbe("/a.mp4", "/library/a.mp4", probe, 1000, true);
        var b = MediaImport.FromProbe("/b.mp4", "/library/b.mp4", probe, 1000, true);
        session.ApplyImported(a);
        session.ApplyImported(b);
        var tl = session.ActiveTimeline!;
        var back = session.AddCueFromAsset(a.Id, tl.Layers[0].Id, 0)!;
        var front = session.AddCueFromAsset(b.Id, tl.Layers[1].Id, 0)!;
        tl.Playhead = 500;
        var ids = PlaybackClock.VisibleMedia(session.Show!).Select(e => e.Cue.Id).ToList();
        Assert.True(ids.IndexOf(back.Id) >= 0);
        Assert.True(ids.IndexOf(front.Id) >= 0);
        Assert.True(ids.IndexOf(back.Id) < ids.IndexOf(front.Id));
    }

    [Fact]
    public void VisibleCuesFollowNdiLayerSwap()
    {
        var session = new ProducerSession();
        session.NewShow();
        var tl = session.ActiveTimeline!;
        var video = ShowFactory.EmptyCue(new Cue { Name = "video", LayerId = tl.Layers[0].Id, Duration = 10_000 });
        var ndi1 = ShowFactory.EmptyCue(new Cue { Name = "ndi1", LayerId = tl.Layers[1].Id, Duration = 10_000, FreeRunning = true });
        var ndi2 = ShowFactory.EmptyCue(new Cue { Name = "ndi2", LayerId = tl.Layers[2].Id, Duration = 10_000, FreeRunning = true });
        tl.Cues.Add(video);
        tl.Cues.Add(ndi1);
        tl.Cues.Add(ndi2);
        tl.Playhead = 500;
        Assert.Equal(new[] { "video", "ndi1", "ndi2" }, PlaybackClock.VisibleCues(tl).Select(e => e.Cue.Name));

        ndi2.LayerId = tl.Layers[1].Id;
        ndi1.LayerId = tl.Layers[2].Id;
        Assert.Equal(new[] { "video", "ndi2", "ndi1" }, PlaybackClock.VisibleCues(tl).Select(e => e.Cue.Name));
    }
}
