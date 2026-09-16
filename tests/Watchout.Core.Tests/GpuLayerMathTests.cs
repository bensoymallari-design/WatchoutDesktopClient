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
