using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Network;
using Watchout.Core.Playback;
using Watchout.Core.Persistence;
using Xunit;

namespace Watchout.Core.Tests;

public class Watchout78Tests
{
    [Fact]
    public void BlindEditHoldsOutputSnapshotUntilTake()
    {
        var session = new ProducerSession();
        session.NewShow();
        var live = session.Show!.Displays[0];
        var originX = live.X;
        session.SetBlindEdit(true);
        Assert.True(session.BlindEdit);
        Assert.NotSame(session.Show, session.PlaybackShow);

        session.UpdateDisplay(live.Id, d => d.X = 480);
        Assert.Equal(480, session.Show.Displays[0].X);
        Assert.Equal(originX, session.PlaybackShow!.Displays[0].X);

        session.TakeToOutput();
        Assert.Equal(480, session.PlaybackShow.Displays[0].X);

        session.SetBlindEdit(false);
        Assert.False(session.BlindEdit);
        Assert.Same(session.Show, session.PlaybackShow);
    }

    [Fact]
    public void BlindEditKeepsOutputClockWhenProducerPauses()
    {
        var session = new ProducerSession();
        session.NewShow();
        var tl = session.ActiveTimeline!;
        tl.Duration = 10_000;
        session.Play();
        session.SetBlindEdit(true);
        Assert.Equal(PlaybackState.Play, session.PlaybackShow!.Timelines[0].Playback);
        session.Pause();
        Assert.Equal(PlaybackState.Pause, session.Show!.Timelines[0].Playback);
        Assert.Equal(PlaybackState.Play, session.PlaybackShow.Timelines[0].Playback);
        var wall = session.PlaybackShow.Timelines[0].Playhead;
        var producer = session.Show.Timelines[0].Playhead;
        session.Tick(250);
        Assert.Equal(wall + 250, session.PlaybackShow.Timelines[0].Playhead, 3);
        Assert.Equal(producer, session.Show.Timelines[0].Playhead);
    }

    [Fact]
    public void GroupAndUngroupComposition()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 4000, Fps = 60, Codec = "h264" };
        var a = MediaImport.FromProbe("/a.mp4", "/library/a.mp4", probe, 1000, true);
        var b = MediaImport.FromProbe("/b.mp4", "/library/b.mp4", probe, 1000, true);
        session.ApplyImported(a);
        session.ApplyImported(b);
        var c1 = session.AddCueFromAsset(a.Id)!;
        var c2 = session.AddCueFromAsset(b.Id)!;
        session.UpdateCue(c2.Id, c => { c.Start = 1000; c.Position.X = 400; });
        session.Select(SelectionKind.Cue, c1.Id, c2.Id);
        session.GroupSelectedCues();
        var group = session.Show!.Assets.First(x => x.Kind == AssetKind.Composition);
        Assert.Equal(2, group.Children.Count);
        Assert.Single(session.ActiveTimeline!.Cues);
        session.ActiveTimeline.Playhead = 500;
        var visible = PlaybackClock.VisibleMedia(session.Show);
        Assert.Contains(visible, e => e.Cue.AssetId == a.Id);

        session.Select(SelectionKind.Cue, session.ActiveTimeline.Cues[0].Id);
        session.UngroupSelected();
        Assert.Equal(2, session.ActiveTimeline.Cues.Count);
        Assert.DoesNotContain(session.Show.Assets, x => x.Kind == AssetKind.Composition);
    }

    [Fact]
    public void WakeOnLanMagicPacketRepeatsMac()
    {
        Assert.True(WakeOnLan.TryParseMac("AA:BB:CC:DD:EE:FF", out var mac));
        var packet = WakeOnLan.MagicPacket(mac);
        Assert.Equal(102, packet.Length);
        Assert.True(packet.Take(6).All(b => b == 0xFF));
        for (var i = 0; i < 16; i++)
            Assert.Equal(mac, packet.Skip(6 + i * 6).Take(6));
        Assert.False(WakeOnLan.TryParseMac("not-a-mac", out _));
    }

    [Fact]
    public void Watchout6ImporterMapsLooseJson()
    {
        var json = """
            {
              "name": "Tour 2019",
              "displays": [
                { "name": "Left", "width": 1920, "height": 1080, "x": 0, "role": "fill" },
                { "name": "Key", "width": 1920, "height": 1080, "x": 1920, "role": "key", "keyChannel": 2 }
              ],
              "assets": [
                { "name": "opener", "path": "D:/media/opener.mp4", "width": 1920, "height": 1080, "duration": 8 }
              ],
              "cues": [
                { "name": "Opener", "start": 1, "duration": 8, "asset": "opener", "x": 0, "y": 0 }
              ]
            }
            """;
        var report = Watchout6Importer.ImportJson("tour.json", json);
        Assert.True(report.Ok);
        Assert.False(report.NativeWatchMe);
        var show = report.Show!;
        Assert.Equal("Tour 2019", show.Name);
        Assert.Equal(2, show.Displays.Count);
        Assert.Equal(DisplayRole.Key, show.Displays[1].Role);
        Assert.Equal(2, show.Displays[1].KeyChannel);
        Assert.Single(show.Assets);
        var cue = Assert.Single(show.Timelines[0].Cues);
        Assert.Equal(1000, cue.Start);
        Assert.Equal(8000, cue.Duration);
        Assert.Equal(show.Assets[0].Id, cue.AssetId);
    }

    [Fact]
    public void Watchout6ImporterOpensNativeWatchMe()
    {
        var session = new ProducerSession();
        session.OpenDemo();
        var json = ShowSerializer.Save(session.Show!);
        var report = Watchout6Importer.ImportJson("demo.watchme.json", json);
        Assert.True(report.Ok);
        Assert.True(report.NativeWatchMe);
        Assert.Equal(session.Show!.Name, report.Show!.Name);
        Assert.Equal(session.Show.Displays.Count, report.Show.Displays.Count);
    }

    [Fact]
    public void Watchout6ImporterRejectsBinaryWatch()
    {
        var path = Path.Combine(Path.GetTempPath(), "watchme-binary.watch");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0x04]);
        var report = Watchout6Importer.ImportFile(path);
        Assert.False(report.Ok);
        Assert.Contains("binary", report.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KeyFillAndColorSpaceRoundTrip()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.UpdateDisplay(session.Show!.Displays[0].Id, d =>
        {
            d.Role = DisplayRole.Key;
            d.KeyChannel = 3;
            d.ColorSpace = ColorSpaceTag.Rec2020;
        });
        session.UpdateNode(session.Show.Nodes[1].Id, n => n.MacAddress = "00-11-22-33-44-55");
        session.SetColorSpace(ColorSpaceTag.Hlg);
        var round = ShowSerializer.Load(ShowSerializer.Save(session.Show));
        Assert.Equal(DisplayRole.Key, round.Displays[0].Role);
        Assert.Equal(3, round.Displays[0].KeyChannel);
        Assert.Equal(ColorSpaceTag.Rec2020, round.Displays[0].ColorSpace);
        Assert.Equal(ColorSpaceTag.Hlg, round.Prefs.ColorSpace);
        Assert.Equal("00-11-22-33-44-55", round.Nodes[1].MacAddress);
    }

    [Fact]
    public void AssetRevisionSwapKeepsCueSlot()
    {
        var session = new ProducerSession();
        session.NewShow();
        var probe = new MediaProbe { Width = 1920, Height = 1080, DurationMs = 4000, Codec = "h264" };
        var media = MediaImport.FromProbe("/wall.mp4", "/library/wall.mp4", probe, 1000, true);
        session.ApplyImported(media);
        var cue = session.AddCueFromAsset(media.Id)!;
        session.PushAssetRevision(media.Id, "file:///library/wall.v2.mp4", "/library/wall.v2.mp4", "H.264 version");
        var asset = session.Show!.Assets.Single();
        Assert.True(asset.Dynamic);
        Assert.Equal(2, asset.Revisions.Count);
        Assert.Equal("file:///library/wall.v2.mp4", asset.Url);
        Assert.Equal(media.Id, cue.AssetId);
        session.ActivateRevision(asset.Id, asset.Revisions[0].Id);
        Assert.Contains("wall.mp4", asset.Url);
    }

    [Fact]
    public void SessionImportWatchout6LoadsShow()
    {
        var json = """{ "name": "Imported", "displays": [ { "name": "Wall", "width": 3840, "height": 1080 } ] }""";
        var path = Path.Combine(Path.GetTempPath(), "watchme-import.json");
        File.WriteAllText(path, json);
        var session = new ProducerSession();
        var report = session.ImportWatchout6(path);
        Assert.True(report.Ok);
        Assert.Equal("Imported", session.Show!.Name);
        Assert.Equal(3840, session.Show.Displays[0].Width);
    }
}
