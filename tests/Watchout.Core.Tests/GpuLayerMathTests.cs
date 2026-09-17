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
        Assert.True(GpuLayerMath.StageYieldsFilePreview(true, true, clip));
        Assert.False(GpuLayerMath.StageYieldsFilePreview(false, true, clip));
        Assert.False(GpuLayerMath.StageYieldsFilePreview(true, false, clip));
        var ndi = new Asset { Id = "n", Kind = AssetKind.Ndi, Url = "ndi://cam" };
        Assert.False(GpuLayerMath.StageYieldsFilePreview(true, true, ndi));
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
        var x20 = OutputViewMath.PresentDest(516, 430, 516, 430);
        Assert.Equal(516, x20.W);
        Assert.Equal(430, x20.H);
        var x20OnEdid = OutputViewMath.PresentDest(1920, 1080, 516, 430);
        Assert.Equal(516, x20OnEdid.W);
        Assert.Equal(430, x20OnEdid.H);
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
        Assert.Equal(1, fill.ScaleX, 3);
        Assert.Equal(1, fill.ScaleY, 3);
        var filled = GpuLayerMath.MapRect(new StageRect(0, 0, 1920, 1080), fill.OriginX, fill.OriginY, fill.ScaleX, fill.ScaleY);
        Assert.Equal(1920, filled.W);
        Assert.Equal(1080, filled.H);
        var dipWall = OutputViewMath.OutputViewport(0, 0, 3840, 2160, 2560, 1440);
        Assert.Equal(1, dipWall.ScaleX, 3);
        var oneToOne = OutputViewMath.OutputViewport(0, 0, 1920, 1080, 1920, 1080);
        Assert.Equal(1, oneToOne.ScaleX, 3);
        Assert.Equal(1, oneToOne.ScaleY, 3);
        var fittedOnSmallerWall = OutputViewMath.OutputViewport(0, 0, 3840, 2160, 1920, 1080);
        var whole = GpuLayerMath.MapRect(new StageRect(0, 0, 3840, 2160), fittedOnSmallerWall.OriginX, fittedOnSmallerWall.OriginY, fittedOnSmallerWall.ScaleX, fittedOnSmallerWall.ScaleY);
        Assert.Equal(3840, whole.W);
        Assert.Equal(2160, whole.H);
        var stageOnX20 = OutputViewMath.OutputViewport(0, 0, 516, 430, 516, 430);
        Assert.Equal(1, stageOnX20.ScaleX, 3);
        var x20Mapped = GpuLayerMath.MapRect(new StageRect(0, 0, 516, 430), stageOnX20.OriginX, stageOnX20.OriginY, stageOnX20.ScaleX, stageOnX20.ScaleY);
        Assert.Equal(516, (int)Math.Round(x20Mapped.W));
        Assert.Equal(430, (int)Math.Round(x20Mapped.H));
        var tv = OutputViewMath.OutputViewport(0, 0, 1920, 1080, 1920, 1080);
        Assert.Equal(1, tv.ScaleX, 3);
        var almost = OutputViewMath.OutputViewport(0, 0, 3840, 2160, 3730, 2098);
        Assert.Equal(1, almost.ScaleX, 3);
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
