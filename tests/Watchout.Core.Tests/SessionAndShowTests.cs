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

        tl.Loop = false;
        tl.Playhead = 0;
        session.Tick(2500);
        Assert.Equal(PlaybackState.Stop, tl.Playback);
        Assert.Equal(1000, tl.Playhead);
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
}
