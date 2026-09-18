using Watchout.Core;
using Watchout.Core.Gpu;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Playback;
using Watchout.Core.Persistence;
using Watchout.Core.Stage;
using Xunit;

namespace Watchout.Core.Tests;

public class GpuLayerMathTests
{
    [Fact]
    public void MapRectScalesStageSpaceIntoPixels()
    {
        var mapped = GpuLayerMath.MapRect(new StageRect(100, 50, 1920, 1080), 0, 0, 0.5);
        Assert.Equal(50, mapped.X);
        Assert.Equal(25, mapped.Y);
        Assert.Equal(960, mapped.W);
        Assert.Equal(540, mapped.H);
    }

    [Fact]
    public void ColorMatrixIsIdentityAtDefaultLooks()
    {
        var draw = new GpuDraw(
            "c", GpuSourceKind.File, "file:x", 0, 1000, false, true,
            0, 0, 1920, 1080, 0, 0, 1, 1, 1, BlendMode.Normal,
            0, 0, 100, 0, 0, 0, 100, 0, 8, false, 0, 1, 0, 0.28f, 1);
        var m = GpuLayerMath.ColorMatrix(draw);
        Assert.InRange(m[0], 0.99f, 1.01f);
        Assert.InRange(m[5], 0.99f, 1.01f);
        Assert.InRange(m[10], 0.99f, 1.01f);
        Assert.InRange(m[12], -0.02f, 0.02f);
    }

    [Fact]
    public void EdgeBlendFadesDisplaySeams()
    {
        Assert.Equal(1, GpuLayerMath.EdgeBlend(0.5f, false, 128, 1920));
        Assert.True(GpuLayerMath.EdgeBlend(0, true, 128, 1920) < 0.05f);
        Assert.True(GpuLayerMath.EdgeBlend(0.5f, true, 128, 1920) > 0.9f);
    }

    [Fact]
    public void FileCueBecomesAGpuDrawWithCropAndBlend()
    {
        var asset = new Asset
        {
            Id = "a",
            Kind = AssetKind.Video,
            Width = 1920,
            Height = 1080,
            Duration = 10_000,
            OriginalPath = "/tmp/wall.mp4",
            Url = "/tmp/wall.mp4",
            Codec = "h264",
        };
        var cue = new Cue
        {
            Id = "c",
            Type = CueType.Media,
            AssetId = "a",
            Start = 0,
            Duration = 10_000,
            Enabled = true,
            Opacity = 80,
            Scale = new Vec2 { X = 50, Y = 50 },
            Position = new Vec3 { X = 10, Y = 20 },
            Blend = BlendMode.Add,
            Crop = new Crop { Left = 10, Right = 10, Top = 5, Bottom = 5 },
        };
        var ev = Tweens.EvaluateCue(cue, 1000);
        Assert.NotNull(ev);
        var draw = GpuLayerMath.FromCue(ev!, asset, 0, 0, 1, 1920, 1080, PlaybackState.Play, true);
        Assert.Equal(GpuSourceKind.File, draw.Kind);
        Assert.Equal(BlendMode.Add, draw.Blend);
        Assert.Equal(0.8f, draw.Opacity, 3);
        Assert.Equal(10, draw.X);
        Assert.Equal(20, draw.Y);
        Assert.Equal(960, draw.W);
        Assert.Equal(540, draw.H);
        Assert.InRange(draw.U0, 0.09f, 0.11f);
        Assert.True(draw.Playing);
        Assert.True(GpuLayerMath.UsesGpu(asset));
        Assert.Equal("Add", GpuLayerMath.BlendLabel(BlendMode.Add));
    }

    [Fact]
    public void EmptyPresentWhilePlayingKeepsTheLastFrame()
    {
        Assert.False(GpuSourceLifetime.ReleaseAfterIdle(0));
        Assert.False(GpuSourceLifetime.ReleaseAfterIdle(GpuSourceLifetime.HoldFrames - 1));
        Assert.True(GpuSourceLifetime.ReleaseAfterIdle(GpuSourceLifetime.HoldFrames));
        Assert.False(GpuSourceLifetime.KeepLastFrame(false, 0));
        Assert.False(GpuSourceLifetime.KeepLastFrame(true, 1));
        Assert.True(GpuSourceLifetime.KeepLastFrame(true, 0));
        Assert.True(GpuSourceLifetime.FreezeIdleWhilePlaying(true, 0));
        Assert.False(GpuSourceLifetime.FreezeIdleWhilePlaying(true, 1));
        Assert.False(GpuSourceLifetime.FreezeIdleWhilePlaying(false, 0));
        Assert.True(GpuSourceLifetime.OutputMustFlip(true, true));
        Assert.False(GpuSourceLifetime.OutputMustFlip(false, true));
        Assert.False(GpuSourceLifetime.ClearToBlack(0));
        Assert.True(GpuSourceLifetime.ClearToBlack(1));
    }

    [Fact]
    public void StageYieldsTheFilePreviewWhileOutputIsLive()
    {
        var clip = new Asset { Id = "a", Kind = AssetKind.Video, Url = "/tmp/wall.mp4", OriginalPath = "/tmp/wall.mp4" };
        Assert.False(GpuLayerMath.StageYieldsFilePreview(true, true, clip));
        Assert.False(GpuLayerMath.StageYieldsFilePreview(false, true, clip));
        Assert.False(GpuLayerMath.StageYieldsFilePreview(true, false, clip));
        Assert.False(GpuLayerMath.StageYieldsFilePreview(true, true, gpuOn: false, clip));
        Assert.True(GpuLayerMath.StageYieldsFilePreview(true, true, gpuOn: true, clip));
        Assert.False(GpuLayerMath.StageShowsPoster(true, true, gpuOn: false, clip));
        Assert.False(GpuLayerMath.StageShowsPoster(true, true, gpuOn: true, clip));
        Assert.False(GpuLayerMath.StageShowsPoster(true, false, gpuOn: false, clip));
        Assert.True(GpuLayerMath.DualFileDxvaKillsOutput(true, true, gpuOn: false));
        Assert.False(GpuLayerMath.DualFileDxvaKillsOutput(true, true, gpuOn: true));
        Assert.False(GpuLayerMath.DualFileDxvaKillsOutput(false, true, gpuOn: false));
        Assert.True(GpuLayerMath.StageUsesSoftPreview(true, true, gpuOn: false, clip));
        Assert.False(GpuLayerMath.StageUsesSoftPreview(true, true, gpuOn: true, clip));
        Assert.False(GpuLayerMath.StageUsesSoftPreview(true, false, gpuOn: false, clip));
        Assert.Equal(66, GpuLayerMath.SoftPreviewSleepMs(true));
        Assert.Equal(40, GpuLayerMath.SoftPreviewSleepMs(false));
        Assert.Equal("1", GpuLayerMath.SoftPreviewSeekArg(1000));
        Assert.Equal("22.5", GpuLayerMath.SoftPreviewSeekArg(22_500));
        Assert.Equal("0", GpuLayerMath.SoftPreviewSeekArg(-8));
        Assert.False(GpuLayerMath.SoftPreviewShouldGrab(-1, 22_203, 0, true, false));
        Assert.True(GpuLayerMath.SoftPreviewShouldGrab(-1, 22_203, 80, true, false));
        Assert.False(GpuLayerMath.SoftPreviewShouldGrab(22_000, 22_200, 100, true, true));
        Assert.True(GpuLayerMath.SoftPreviewShouldGrab(22_000, 22_400, 250, true, true));
        Assert.False(GpuLayerMath.SoftPreviewShouldGrab(7_409, 7_409, 200, false, true));
        Assert.True(GpuLayerMath.SoftPreviewShouldGrab(7_409, 8_000, 80, false, true));
        var ndi = new Asset { Id = "n", Kind = AssetKind.Ndi, Url = "ndi://cam" };
        Assert.False(GpuLayerMath.StageYieldsFilePreview(true, true, ndi));
        Assert.True(GpuLayerMath.StageDrawsSharedGpu(true, clip));
        Assert.True(GpuLayerMath.StageDrawsSharedGpu(true, ndi));
        Assert.False(GpuLayerMath.StageDrawsSharedGpu(false, clip));
        Assert.False(GpuLayerMath.StageDrawsSharedGpu(true, null));
        Assert.True(GpuLayerMath.ShowCueStageLabel(true));
        Assert.False(GpuLayerMath.ShowCueStageLabel(false));
        Assert.Equal("majdoul3", GpuLayerMath.CueStageLabel("majdoul3", "clip.mp4"));
        Assert.Equal("majdoul3", GpuLayerMath.CueStageLabel("Cue", "majdoul3"));
        Assert.Equal("wall", GpuLayerMath.CueStageLabel("  ", "wall"));
        Assert.Equal("", GpuLayerMath.CueStageLabel(null, null));
        Assert.Equal(28d, GpuLayerMath.CueLabelBarHeight(40));
        Assert.Equal(36d, GpuLayerMath.CueLabelBarHeight(1080));
        Assert.Equal(0d, GpuLayerMath.CueLabelBarHeight(10));
        var inset = GpuLayerMath.CueVideoInset(0, 0, 3840, 2160, editing: true, hwndLayer: true);
        Assert.Equal(36d, inset.Y);
        Assert.Equal(2160 - 36, inset.H);
        Assert.Equal((0d, 0d, 100d, 50d), GpuLayerMath.CueVideoInset(0, 0, 100, 50, editing: false, hwndLayer: true));
    }

    [Fact]
    public void GpuPathPrefersDxgiTexturesOverRgb32RamCopies()
    {
        Assert.Equal(GpuFrameSource.DxgiTexture, GpuResidentPath.Choose(true, true));
        Assert.Equal(GpuFrameSource.DxgiTexture, GpuResidentPath.Choose(true, false));
        Assert.Equal(GpuFrameSource.CpuPixels, GpuResidentPath.Choose(false, true));
        Assert.Equal(GpuFrameSource.None, GpuResidentPath.Choose(false, false));
        Assert.True(GpuResidentPath.SkipStageCpuReadback(true, true));
        Assert.False(GpuResidentPath.SkipStageCpuReadback(false, true));
        Assert.False(GpuResidentPath.SkipStageCpuReadback(true, false));
        Assert.False(GpuResidentPath.NeedCpuPixels(true));
        Assert.True(GpuResidentPath.NeedCpuPixels(false));
        var preview = GpuResidentPath.StagePreviewSize(3840, 2160);
        Assert.Equal(1280, preview.W);
        Assert.Equal(720, preview.H);
        Assert.Equal((960, 540), GpuResidentPath.StagePreviewSize(960, 540));
        Assert.Equal(1280, GpuResidentPath.StagePreviewMaxEdge);
        Assert.True(GpuResidentPath.ReuseBuffer(1920 * 1080 * 4, 1920 * 1080 * 4));
        Assert.False(GpuResidentPath.ReuseBuffer(16, 64));
        Assert.False(GpuResidentPath.ReuseBuffer(64, 0));
        Assert.Equal(128, GpuResidentPath.GrowBuffer(64, 100));
        Assert.Equal(64, GpuResidentPath.GrowBuffer(64, 32));
        Assert.False(GpuResidentPath.OpenProducedAFrame(false));
        Assert.True(GpuResidentPath.OpenProducedAFrame(true));
        Assert.True(GpuResidentPath.SoftPreviewOpenWithoutFrame(true, false));
        Assert.False(GpuResidentPath.SoftPreviewOpenWithoutFrame(true, true));
        Assert.False(GpuResidentPath.SoftPreviewOpenWithoutFrame(false, false));
        Assert.True(GpuResidentPath.StillOpening(false));
        Assert.False(GpuResidentPath.StillOpening(true));
        Assert.True(GpuResidentPath.StallWithoutPicture(true, false, 4000, 4000));
        Assert.False(GpuResidentPath.StallWithoutPicture(true, false, 100, 4000));
        Assert.False(GpuResidentPath.StallWithoutPicture(true, true, 5000, 4000));
        Assert.False(GpuResidentPath.StallWithoutPicture(false, false, 5000, 4000));
        Assert.Equal(80, GpuResidentPath.PrerollAttempts);
        Assert.Equal(2500, GpuResidentPath.PrerollBudgetMs);
    }

    [Fact]
    public void OutputPictureCauseNamesBlackAndStuck()
    {
        var play = new OutputPictureHint(true, 1, 1, false, GpuSourceKind.File, false, true, false, false, false, false, null);
        Assert.Equal(OutputPictureKind.Picture, OutputPictureCause.Classify(play));
        var opening = play with { ReadyTextures = 0, DecoderOpening = true, DecoderReady = false };
        Assert.Equal(OutputPictureKind.Opening, OutputPictureCause.Classify(opening));
        Assert.False(OutputPictureCause.ShouldLog(OutputPictureKind.Opening, false, 100));
        Assert.True(OutputPictureCause.ShouldLog(OutputPictureKind.Opening, false, OutputPictureCause.QuietMs));
        Assert.False(OutputPictureCause.ShouldLog(OutputPictureKind.Opening, true, 5000));
        var dead = play with { ReadyTextures = 0, DecoderDead = true, DecodeError = "no picture" };
        Assert.Equal(OutputPictureKind.DecodeFailed, OutputPictureCause.Classify(dead));
        Assert.True(OutputPictureCause.ShouldLog(OutputPictureKind.DecodeFailed, false, 0));
        Assert.Contains("could not decode this MP4", OutputPictureCause.Message(OutputPictureKind.DecodeFailed, "file:wall.mp4", "no picture"));
        var stalled = play with { DecoderStalled = true };
        Assert.Equal(OutputPictureKind.Stalled, OutputPictureCause.Classify(stalled));
        Assert.Contains("stuck", OutputPictureCause.Message(OutputPictureKind.Stalled, "file:wall.mp4", null));
        var missing = play with { ReadyTextures = 0, FileMissing = true };
        Assert.Equal(OutputPictureKind.MissingFile, OutputPictureCause.Classify(missing));
        var noCue = play with { DrawCount = 0, ReadyTextures = 0 };
        Assert.Equal(OutputPictureKind.NoCue, OutputPictureCause.Classify(noCue));
        var hold = noCue with { KeepLastFrame = true };
        Assert.Equal(OutputPictureKind.HoldingLastFrame, OutputPictureCause.Classify(hold));
        var ndi = play with { Kind = GpuSourceKind.Ndi, ReadyTextures = 0, LiveHasPixels = false };
        Assert.Equal(OutputPictureKind.LiveEmpty, OutputPictureCause.Classify(ndi));
        Assert.Contains("NDI/capture", OutputPictureCause.Message(OutputPictureKind.LiveEmpty, "ndi:cam", null));
        Assert.Equal(OutputPictureKind.Idle, OutputPictureCause.Classify(play with { Playing = false, ReadyTextures = 0 }));
        Assert.Equal("error", OutputPictureCause.Level(OutputPictureKind.DecodeFailed));
        Assert.Equal("warn", OutputPictureCause.Level(OutputPictureKind.Stalled));
        Assert.Equal(OutputPictureKind.GpuOff, OutputPictureCause.WhenPresentSkipped(true, 1, gpuOn: false, hwndOk: true));
        Assert.Equal(OutputPictureKind.NoHwnd, OutputPictureCause.WhenPresentSkipped(true, 1, gpuOn: true, hwndOk: false));
        Assert.Equal(OutputPictureKind.Idle, OutputPictureCause.WhenPresentSkipped(true, 1, gpuOn: true, hwndOk: true));
        Assert.Equal(OutputPictureKind.Idle, OutputPictureCause.WhenPresentSkipped(false, 1, gpuOn: false, hwndOk: false));
        Assert.False(OutputPictureCause.ShouldLog(OutputPictureKind.GpuOff, false, 100));
        Assert.True(OutputPictureCause.ShouldLog(OutputPictureKind.GpuOff, false, OutputPictureCause.QuietMs));
        Assert.False(OutputPictureCause.ShouldLog(OutputPictureKind.NoHwnd, false, 100));
        Assert.True(OutputPictureCause.ShouldLog(OutputPictureKind.NoHwnd, false, OutputPictureCause.QuietMs));
        Assert.Contains("compositor is off", OutputPictureCause.Message(OutputPictureKind.GpuOff, "", "E_INVALIDARG"));
        Assert.Contains("E_INVALIDARG", OutputPictureCause.Message(OutputPictureKind.GpuOff, "", "E_INVALIDARG"));
        Assert.Contains("no wall HWND", OutputPictureCause.Message(OutputPictureKind.NoHwnd, "", null));
    }

    [Fact]
    public void OutputPresentsAtTheScreenSizeNotThe64HostStub()
    {
        var nested = OutputViewMath.PresentSize(64, 64, 3840, 2160, 3840, 2160);
        Assert.Equal(3840, nested.W);
        Assert.Equal(2160, nested.H);
        var dip = OutputViewMath.DipToPixels(2560, 1440, 1.5);
        Assert.Equal(3840, dip.W);
        Assert.Equal(2160, dip.H);
        Assert.True(OutputViewMath.HostNeedsResize(64, 64, 3840, 2160));
        Assert.False(OutputViewMath.HostNeedsResize(3840, 2160, 3840, 2160));
        Assert.False(OutputViewMath.FlipModelAllowed(hwndIsChild: true));
        Assert.True(OutputViewMath.FlipModelAllowed(hwndIsChild: false));
        Assert.True(OutputViewMath.PreferTopLevelHwnd(64, 64, 3840, 2160));
        Assert.False(OutputViewMath.PreferTopLevelHwnd(3840, 2160, 3840, 2160));
        Assert.True(OutputViewMath.RetryOpenWithoutHardwareTransforms(true));
        Assert.False(OutputViewMath.RetryOpenWithoutHardwareTransforms(false));
        Assert.True(OutputViewMath.TearGpuOnPresentError(unchecked((int)0x887A0005)));
        Assert.False(OutputViewMath.TearGpuOnPresentError(unchecked((int)0x887A0001)));
        var widget = OutputViewMath.PresentSize(1920, 1080, 0, 0, 0, 0);
        Assert.Equal(1920, widget.W);
        Assert.Equal(1080, widget.H);
        var wall = OutputViewMath.WallPixels(3840, 2160);
        Assert.Equal(3840, wall.W);
        Assert.Equal(2160, wall.H);
        // A 2560 client (wrong-monitor DPI) must not shrink the 4K wall.
        var keepScreen = OutputViewMath.PresentSize(2560, 1440, 3840, 2160, 3840, 2160);
        Assert.Equal(3840, keepScreen.W);
        Assert.Equal(2160, keepScreen.H);
        var stub = OutputViewMath.PresentDest(64, 64, 3840, 2160);
        Assert.Equal(3840, stub.W);
        var hwnd = OutputViewMath.PresentDest(1920, 1080, 3840, 2160);
        Assert.Equal(1920, hwnd.W);
        Assert.Equal(1080, hwnd.H);
        var dpiClient = OutputViewMath.PresentDest(2560, 1440, 3840, 2160);
        Assert.Equal(2560, dpiClient.W);
        Assert.Equal(1440, dpiClient.H);
        var actualSwap = OutputViewMath.SwapPixels(3840, 2160, 2560, 1440);
        Assert.Equal(2560, actualSwap.W);
        Assert.Equal(1440, actualSwap.H);
        var hdmiDip = OutputViewMath.ScreenToDip(3840, 2160, 1);
        Assert.Equal(3840, hdmiDip.DipW);
        Assert.Equal(2160, hdmiDip.DipH);
        var laptopDip = OutputViewMath.ScreenToDip(3840, 2160, 1.5);
        Assert.Equal(2560, laptopDip.DipW);
        Assert.Equal(1440, laptopDip.DipH);
        var vp = OutputViewMath.OutputViewport(1920, 0, 3840, 2160, 3840, 2160);
        Assert.Equal(1920, vp.OriginX);
        Assert.Equal(1, vp.ScaleX, 3);
        Assert.Equal(1, vp.ScaleY, 3);
        var mapped = GpuLayerMath.MapRect(new StageRect(1920, 0, 3840, 2160), vp.OriginX, vp.OriginY, vp.ScaleX, vp.ScaleY);
        Assert.Equal(0, mapped.X);
        Assert.Equal(0, mapped.Y);
        Assert.Equal(3840, mapped.W);
        Assert.Equal(2160, mapped.H);
        var fill = OutputViewMath.OutputViewport(0, 0, 1920, 1080, 3840, 2160);
        Assert.Equal(2, fill.ScaleX, 3);
        Assert.Equal(2, fill.ScaleY, 3);
        var filled = GpuLayerMath.MapRect(new StageRect(0, 0, 1920, 1080), fill.OriginX, fill.OriginY, fill.ScaleX, fill.ScaleY);
        Assert.Equal(3840, filled.W);
        Assert.Equal(2160, filled.H);
        var dipWall = OutputViewMath.OutputViewport(0, 0, 3840, 2160, 2560, 1440);
        Assert.InRange(dipWall.ScaleX, 0.66, 0.68);
        var oneToOne = OutputViewMath.OutputViewport(0, 0, 1920, 1080, 1920, 1080);
        Assert.Equal(1, oneToOne.ScaleX, 3);
        Assert.Equal(1, oneToOne.ScaleY, 3);
        var fittedOnSmallerWall = OutputViewMath.OutputViewport(0, 0, 3840, 2160, 1920, 1080);
        var whole = GpuLayerMath.MapRect(new StageRect(0, 0, 3840, 2160), fittedOnSmallerWall.OriginX, fittedOnSmallerWall.OriginY, fittedOnSmallerWall.ScaleX, fittedOnSmallerWall.ScaleY);
        Assert.Equal(1920, whole.W);
        Assert.Equal(1080, whole.H);
        var almost = OutputViewMath.OutputViewport(0, 0, 3840, 2160, 3730, 2098);
        Assert.True(almost.ScaleX < 0.99);
        var almostMapped = GpuLayerMath.MapRect(new StageRect(0, 0, 3840, 2160), almost.OriginX, almost.OriginY, almost.ScaleX, almost.ScaleY);
        Assert.True(almostMapped.W <= 3730 + 1);
        // 1700×720 cue stays put; 3840 Stage cue must not overflow a smaller HWND (that zooms).
        var small = GpuLayerMath.FitDrawToDest(0, 0, 1700, 720, 3840, 2160);
        Assert.Equal(1700, small.W);
        Assert.Equal(720, small.H);
        var stageFit = GpuLayerMath.FitDrawToDest(0, 0, 3840, 2160, 2560, 1440);
        Assert.Equal(2560, (int)Math.Round(stageFit.W));
        Assert.Equal(1440, (int)Math.Round(stageFit.H));
        Assert.Equal(0, (int)Math.Round(stageFit.X));
        Assert.Equal(0, (int)Math.Round(stageFit.Y));
        var dragged = GpuLayerMath.FitDrawToDest(480, 120, 3840, 2160, 2560, 1440);
        Assert.Equal(2560, (int)Math.Round(dragged.W));
        Assert.Equal(1440, (int)Math.Round(dragged.H));
        Assert.True(dragged.X > stageFit.X + 1);
        Assert.True(dragged.Y > stageFit.Y + 1);
        Assert.True(OutputViewMath.PulseClock(true, false));
        Assert.True(OutputViewMath.PulseClock(false, true));
        Assert.False(OutputViewMath.PulseClock(false, false));
        Assert.True(OutputViewMath.D3D11CreateNeedsFeatureLevels(4));
        Assert.False(OutputViewMath.D3D11CreateNeedsFeatureLevels(0));
        Assert.Equal(112, OutputViewMath.ConstantBufferBytes(112));
        Assert.Equal(16, OutputViewMath.ConstantBufferBytes(1));
        Assert.Equal(16, OutputViewMath.ConstantBufferBytes(0));
        Assert.True(OutputViewMath.SurviveUnhandled(unchecked((int)0x887A0005)));
        Assert.True(OutputViewMath.SurviveUnhandled(unchecked((int)0x887A0006)));
        Assert.True(OutputViewMath.SurviveUnhandled(unchecked((int)0x8007000E)));
        Assert.True(OutputViewMath.SurviveUnhandled(unchecked((int)0x80070057)));
        Assert.True(OutputViewMath.SurviveUnhandled(unchecked((int)0x80004005)));
        Assert.True(OutputViewMath.SurviveException(unchecked((int)0x887A0005), "COMException", "DXGI"));
        Assert.True(OutputViewMath.SurviveException(0, "OutOfMemoryException", "alloc"));
        Assert.True(OutputViewMath.SurviveException(0, "Exception", "D3D11 compositor"));
        Assert.False(OutputViewMath.SurviveUnhandled(0));
        Assert.False(OutputViewMath.SurviveException(0, "NullReferenceException", "object"));
        Assert.True(OutputViewMath.HoldSoftwareOutput(true, true));
        Assert.False(OutputViewMath.HoldSoftwareOutput(false, true));
        Assert.False(OutputViewMath.HoldSoftwareOutput(true, false));
        Assert.False(OutputViewMath.RetryCompositor(true, true));
        Assert.True(OutputViewMath.RetryCompositor(false, true));
    }

    [Fact]
    public void BlendModeRoundTripsInShowJson()
    {
        var session = new ProducerSession();
        session.OpenDemo();
        var cue = session.Show!.Timelines[0].Cues.First(c => c.Type == CueType.Media);
        session.UpdateCue(cue.Id, c => c.Blend = BlendMode.Screen);
        var round = ShowSerializer.Load(ShowSerializer.Save(session.Show));
        var again = round.Timelines[0].Cues.First(c => c.Id == cue.Id);
        Assert.Equal(BlendMode.Screen, again.Blend);
    }
}
