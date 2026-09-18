using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Persistence;
using Xunit;

namespace Watchout.Core.Tests;

public class WatchMeStackTests
{
    const string SampleSdp = """
        v=0
        o=- 1 1 IN IP4 192.168.10.20
        s=Cam1
        c=IN IP4 239.1.1.1
        m=video 5004 RTP/AVP 96
        a=rtpmap:96 raw/90000
        a=fmtp:96 sampling=YCbCr-4:2:2; width=1920; height=1080; exactframerate=50; depth=10
        m=audio 5006 RTP/AVP 97
        a=rtpmap:97 L24/48000/16
        """;

    [Fact]
    public void SdpParses2110VideoAndAudio()
    {
        var streams = Sdp.Parse(SampleSdp);
        Assert.Equal(2, streams.Count);
        var video = streams.First(s => s.Kind == "video");
        Assert.Equal(1920, video.Width);
        Assert.Equal(1080, video.Height);
        Assert.Equal(50, video.Fps);
        Assert.Equal("239.1.1.1", video.Destination);
        Assert.Equal(5004, video.Port);
        Assert.Equal(16, streams.First(s => s.Kind == "audio").Channels);
    }

    [Fact]
    public void NmosParsesQuerySenders()
    {
        var json = """
            { "data": [
              { "id": "s1", "label": "Wall left", "transport": "urn:x-nmos:transport:rtp", "manifest_href": "http://nmos/sdp" }
            ] }
            """;
        var senders = Nmos.ParseSenders(json);
        var sender = Assert.Single(senders);
        Assert.Equal("Wall left", sender.Label);
        Assert.Contains("senders", Nmos.QuerySendersUrl("http://nmos.local"));
    }

    [Fact]
    public void ImportSt2110AndNmosCreatesLiveAssets()
    {
        var session = new ProducerSession();
        session.NewShow();
        var asset = session.ImportSt2110("Cam 1", SampleSdp);
        Assert.Equal(AssetKind.St2110, asset.Kind);
        Assert.True(LiveSources.IsSt2110(asset));
        Assert.Equal(1920, asset.Width);
        Assert.Contains(session.Show!.CaptureDevices, d => d.Kind == "ST2110");

        var n = session.ImportNmosSenders([new NmosSender { Id = "a", Label = "NMOS cam", ManifestHref = "http://x" }]);
        Assert.Equal(1, n);
        Assert.Equal(2, session.Show.Assets.Count(a => a.Kind == AssetKind.St2110));
    }

    [Fact]
    public void NotchLcAndHapNeedH264AndHapEncodeArgs()
    {
        Assert.True(Codecs.NeedsH264Transcode("notchlc", "clip.mov"));
        Assert.False(Codecs.PlaysNatively("notchlc", "clip.mov"));
        var hap = Codecs.HapEncodeArgs("in.mov", "out.mov");
        Assert.Contains("hap", hap);
        Assert.Contains("hap_q", hap);
        Assert.Contains("aac", hap);
        Assert.DoesNotContain("-an", hap);
        Assert.DoesNotContain(hap, a => a.Contains("webm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Broadcast10BitEncodeUsesHigh10()
    {
        var args = Codecs.H264TranscodeArgs("in.mov", "out.mp4", "libx264", 1920, 1080, null, true, OptimizePreset.Broadcast, 10, 2);
        Assert.Contains("high10", args);
        Assert.Contains("yuv420p10le", args);
        Assert.Contains("slow", args);
        Assert.Contains("16", args);
    }

    [Fact]
    public void WideWavNeedsStereoDownmix()
    {
        var channels = (ushort)65535;
        var bytes = new byte[44];
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(bytes, 12);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), channels);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 48000);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), 48000 * 2 * channels);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 2);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
        Assert.True(WavHeader.TryRead(bytes, out var info));
        Assert.Equal(65535, info.Channels);
        Assert.True(WavHeader.NeedsStereoDownmix(info.Channels));
        var probe = new MediaProbe { Codec = "pcm_s16le", Channels = 65535, DurationMs = 1000 };
        var media = MediaImport.FromProbe("wide.wav", Path.Combine(Path.GetTempPath(), "wide.wav"), probe, 1000, true);
        Assert.Equal(AssetKind.Audio, media.Kind);
        Assert.Contains("65535", media.Notes);
        Assert.False(media.Optimized);
    }

    [Fact]
    public void LtcPacksAndChasesPlayhead()
    {
        var stamp = new LtcStamp(0, 0, 5, 10, 30);
        var round = Ltc.Unpack(Ltc.Pack(stamp), 30);
        Assert.Equal(0, round.Hours);
        Assert.Equal(0, round.Minutes);
        Assert.Equal(5, round.Seconds);
        Assert.Equal(10, round.Frames);
        var wav = Ltc.Wav(stamp, 3);
        Assert.True(WavHeader.TryRead(wav, out var info));
        Assert.Equal(1, info.Channels);
        var session = new ProducerSession();
        session.NewShow();
        session.ChaseLtc(stamp);
        Assert.Equal(stamp.Milliseconds, session.ActiveTimeline!.Playhead, 3);
    }

    [Fact]
    public void AccessControlAndNodeHealth()
    {
        var settings = new AppSettings { AccessEnabled = true, AccessPin = "1234", AccessRole = AccessRole.Operator };
        Assert.True(AccessControl.IsLocked(settings));
        Assert.False(AccessControl.Unlock(settings, "0000"));
        Assert.True(AccessControl.Unlock(settings, "1234"));
        Assert.True(AccessControl.CanEditShow(AccessRole.Operator));
        Assert.False(AccessControl.CanEditShow(AccessRole.Viewer));
        var node = new ShowNode { Address = "10.0.0.8", ControlPort = 8090, Kind = NodeKind.Watchpax };
        Assert.Equal("http://10.0.0.8:8090/watchme/health", NodeControl.HealthUrl(node));
        Assert.True(NodeControl.ParseHealth("{\"status\":\"ok\"}"));
    }

    [Fact]
    public void HdrAndOptimizePrefsRoundTrip()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.SetHdrPipeline(true);
        session.SetOptimizePreset(OptimizePreset.Broadcast);
        Assert.True(session.Show!.Prefs.HdrPipeline);
        Assert.Equal(10, session.Show.Prefs.BitDepth);
        Assert.Equal(OptimizePreset.Broadcast, session.Show.Prefs.OptimizePreset);
        var round = ShowSerializer.Load(ShowSerializer.Save(session.Show));
        Assert.True(round.Prefs.HdrPipeline);
        Assert.Equal(NodeKind.Watchpax, round.Nodes.First(n => n.Services.Runner).Kind);
    }
}
