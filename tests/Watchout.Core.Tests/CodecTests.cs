using Watchout.Core.Media;
using Watchout.Core.Models;
using Xunit;

namespace Watchout.Core.Tests;

public class CodecTests
{
    [Fact]
    public void ClassifiesCommonExtensions()
    {
        Assert.Equal(AssetKind.Image, Codecs.MediaKind("wall.png"));
        Assert.Equal(AssetKind.Audio, Codecs.MediaKind("sting.wav"));
        Assert.Equal(AssetKind.Video, Codecs.MediaKind("show.mov"));
        Assert.Equal(AssetKind.Video, Codecs.MediaKind("loop.mp4"));
    }

    [Fact]
    public void H264Mp4PlaysNativelyThroughDxva()
    {
        Assert.True(Codecs.PlaysNatively("h264", "clip.mp4", "video/mp4"));
        Assert.True(Codecs.PlaysNatively("avc", "show.mov"));
        Assert.True(Codecs.PlaysNatively("hevc", "clip.mp4"));
        Assert.False(Codecs.NeedsH264Transcode("h264", "clip.mp4", "video/mp4"));
    }

    [Fact]
    public void HapDxvProresNeedH264NotWebm()
    {
        Assert.True(Codecs.NeedsH264Transcode("prores", "clip.mov"));
        Assert.True(Codecs.NeedsH264Transcode("hap", "clip.mov"));
        Assert.True(Codecs.NeedsH264Transcode("dxv", "clip.dxv"));
        Assert.False(Codecs.PlaysNatively("hap", "clip.mov"));
    }

    [Fact]
    public void WavDoesNotNeedAProxy()
    {
        Assert.True(Codecs.PlaysNatively("pcm_s16le", "hit.wav"));
        Assert.False(Codecs.NeedsH264Transcode("pcm_s16le", "hit.wav"));
    }

    [Fact]
    public void H264TranscodeKeepsPixelsAacAndNeverMentionsWebm()
    {
        var args = Codecs.H264TranscodeArgs("in.mov", "out.mp4", "libx264", 3840, 2160);
        Assert.DoesNotContain("-an", args);
        Assert.Contains("0:a:0?", args);
        Assert.Contains("aac", args);
        Assert.Contains("libx264", args);
        Assert.Contains("yuv420p", args);
        Assert.Contains("high", args);
        Assert.Contains("scale=3840:2160", args);
        Assert.DoesNotContain(args, a => a.Contains("webm", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(args, a => a.Contains("libvpx", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void LaptopPrepareDownscales4k()
    {
        var size = Codecs.ScaledProxySize(3840, 2160, 1920);
        Assert.Equal((1920, 1080), size);
        var args = Codecs.H264TranscodeArgs("in.mov", "in.mp4", "libx264", 3840, 2160, 1920);
        Assert.Contains("scale=1920:1080", args);
    }

    [Fact]
    public void SidecarMp4SitsNextToTheMaster()
    {
        Assert.Equal(Path.Combine("C:", "Shows", "clip.mp4"), Codecs.SiblingH264Path(Path.Combine("C:", "Shows", "clip.mov")));
        Assert.Equal("/shows/loop.mp4", Codecs.SiblingH264Path("/shows/loop.mp4"));
    }

    [Fact]
    public void HardwareEncoderPreference()
    {
        Assert.Equal("h264_nvenc", Codecs.PickH264Encoder(["libx264", "h264_nvenc", "h264_qsv"]));
        Assert.Equal("h264_amf", Codecs.PickH264Encoder(["libx264", "h264_amf"]));
        Assert.Equal("h264_qsv", Codecs.PickH264Encoder(["libx264", "h264_qsv"]));
        Assert.Equal("libx264", Codecs.PickH264Encoder(["libx264", "libvpx-vp9"]));
    }

    [Fact]
    public void CliIsFfmpegH264NotVp9()
    {
        var cmd = Codecs.H264Cli("show.mov", "show.mp4");
        Assert.Contains("libx264", cmd);
        Assert.Contains("aac", cmd);
        Assert.EndsWith("show.mp4", cmd);
        Assert.DoesNotContain("webm", cmd);
        Assert.DoesNotContain("libvpx", cmd);
    }

    [Fact]
    public void NativeH264DoesNotNeedRebuild()
    {
        Assert.False(Codecs.NeedsHqRebuild(new Asset
        {
            Kind = AssetKind.Video,
            Codec = "h264",
            OriginalPath = "clip.mp4",
            ProxyVersion = 3,
        }));
        Assert.True(Codecs.NeedsHqRebuild(new Asset
        {
            Kind = AssetKind.Video,
            Codec = "hap",
            OriginalPath = "clip.mov",
        }));
        Assert.False(Codecs.NeedsHqRebuild(new Asset
        {
            Kind = AssetKind.Audio,
            Codec = "pcm_s16le",
            OriginalPath = "hit.wav",
            ProxyVersion = 4,
        }));
        Assert.False(Codecs.NeedsHqRebuild(new Asset { Kind = AssetKind.Image, OriginalPath = "card.png" }));
        Assert.False(Codecs.NeedsHqRebuild(new Asset
        {
            Kind = AssetKind.Video,
            Codec = "hap",
            OriginalPath = "show.mov",
            Bytes = 100L * 1024 * 1024 * 1024,
            Width = 3840,
            Height = 2160,
        }));
    }

    [Fact]
    public void PlaybackPrefersOriginalH264OverLeftoverWebm()
    {
        var dir = Path.Combine(Path.GetTempPath(), "watchout-codec-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var mp4 = Path.Combine(dir, "clip.mp4");
        var webm = Path.Combine(dir, "clip.webm");
        File.WriteAllText(mp4, "x");
        File.WriteAllText(webm, "y");
        try
        {
            var asset = new Asset
            {
                Codec = "h264",
                OriginalPath = mp4,
                ProxyPath = webm,
                Url = new Uri(webm).AbsoluteUri,
            };
            Assert.Equal(mp4, Codecs.PlaybackPath(asset));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FileUrlKeepsSpacesInThePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "Ultimate 4K clip.mp4");
        var url = MediaImport.ToFileUrl(path);
        Assert.StartsWith("file:", url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4K", url);
        var back = Codecs.TryFileUrl(url);
        Assert.NotNull(back);
        Assert.Equal(Path.GetFullPath(path), Path.GetFullPath(back!));
    }
}
