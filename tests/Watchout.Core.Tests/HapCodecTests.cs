using Snappier;
using Watchout.Core.Media;
using Xunit;

namespace Watchout.Core.Tests;

public class HapCodecTests
{
    [Fact]
    public void HapIsAGpuTextureNotAnH264Transcode()
    {
        Assert.True(HapCodec.IsHap("hap", "clip.mov"));
        Assert.True(HapCodec.IsHap("hap_q", @"C:\media\id.hap.mov"));
        Assert.False(HapCodec.IsHap("h264", "clip.mp4"));
        Assert.False(HapCodec.IsHap("h264", "Ultimate 4K Dolby Vision.mp4"));
        Assert.False(Codecs.NeedsH264Transcode("hap", "clip.mov"));
        Assert.True(HapCodec.NeedsHapEncode("h264", "clip.mp4"));
        Assert.False(HapCodec.NeedsHapEncode("hap", "clip.hap.mov"));
        Assert.False(MediaPolicy.ShouldBuildHap(200L * 1024 * 1024, 3840, 2160));
        Assert.False(MediaPolicy.ShouldBuildFullProxy(200L * 1024 * 1024, 3840, 2160));
    }

    [Fact]
    public void UncompressedDxt1RoundTrips()
    {
        var dxt = new byte[HapCodec.DxtSize(8, 8, HapTextureKind.Dxt1)];
        dxt[0] = 0xAB;
        var packet = HapCodec.WrapUncompressed(HapTextureKind.Dxt1, dxt);
        var frame = HapCodec.Decode(packet, 8, 8);
        Assert.Equal(HapTextureKind.Dxt1, frame.Kind);
        Assert.Equal(dxt, frame.Blocks);
        Assert.False(frame.YCoCg);
    }

    [Fact]
    public void SnappyHapQDecodesToDxtBlocks()
    {
        var dxt = new byte[HapCodec.DxtSize(8, 8, HapTextureKind.YCoCgDxt5)];
        dxt[3] = 0xCD;
        var compressed = Snappy.CompressToArray(dxt);
        var packet = new byte[4 + compressed.Length];
        packet[0] = (byte)(compressed.Length & 0xFF);
        packet[1] = (byte)((compressed.Length >> 8) & 0xFF);
        packet[2] = 0;
        packet[3] = HapCodec.CompSnappy | HapCodec.FmtYCoCg;
        compressed.CopyTo(packet.AsSpan(4));
        var frame = HapCodec.Decode(packet, 8, 8);
        Assert.True(frame.YCoCg);
        Assert.Equal(dxt, frame.Blocks);
    }

    [Fact]
    public void PlaybackPrefersNativeH264OverLeftoverHapProxy()
    {
        var dir = Path.Combine(Path.GetTempPath(), "watchout-hap-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var mp4 = Path.Combine(dir, "Ultimate 4K Dolby Vision.mp4");
        var hap = Path.Combine(dir, "clip.hap.mov");
        File.WriteAllText(mp4, "x");
        File.WriteAllText(hap, "y");
        try
        {
            var asset = new Watchout.Core.Models.Asset
            {
                Codec = "h264",
                OriginalPath = mp4,
                ProxyPath = hap,
                Url = new Uri(mp4).AbsoluteUri,
            };
            Assert.Equal(mp4, Codecs.PlaybackPath(asset));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void PlaybackUsesHapWhenTheOriginalFileIsHap()
    {
        var dir = Path.Combine(Path.GetTempPath(), "watchout-hap-orig-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var hap = Path.Combine(dir, "clip.hap.mov");
        File.WriteAllText(hap, "y");
        try
        {
            var asset = new Watchout.Core.Models.Asset
            {
                Codec = "hap_q",
                OriginalPath = hap,
                Url = new Uri(hap).AbsoluteUri,
            };
            Assert.Equal(hap, Codecs.PlaybackPath(asset));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
