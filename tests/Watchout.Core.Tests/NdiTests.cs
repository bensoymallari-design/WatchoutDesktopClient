using System.IO;
using System.Text;
using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Xunit;

namespace Watchout.Core.Tests;

public class NdiTests
{
    [Fact]
    public void FriendlyNameStripsServiceAndDnsEscapes()
    {
        Assert.Equal("SHOW-PC (Resolume Arena)", NdiNames.FriendlyName("SHOW-PC (Resolume Arena)._ndi._tcp.local"));
        Assert.Equal("PHONE (Cam)", NdiNames.FriendlyName("PHONE\\032(Cam)._ndi._tcp.local"));
        Assert.True(NdiNames.IsNdiWebcamLabel("NewTek NDI Video"));
        Assert.True(NdiNames.IsNdiWebcamLabel("NDI Webcam Video"));
        Assert.False(NdiNames.IsNdiWebcamLabel("Integrated Camera"));
        Assert.True(NdiNames.LooksLikeNdi("NDI Webcam Input"));
    }

    [Fact]
    public void PreferredWebcamIgnoresLaptopCamera()
    {
        var devices = new (string Id, string Name)[]
        {
            ("cam", "Integrated Camera"),
            ("ndi", "NewTek NDI Video"),
        };
        Assert.Equal("ndi", NdiNames.PreferredWebcam(devices)?.Id);
        Assert.Equal("ndi", NdiNames.MatchWebcam("SHOW-PC (Resolume)", devices)?.Id);
        Assert.Null(NdiNames.PreferredWebcam([("cam", "Integrated Camera")]));
    }

    [Fact]
    public void FromRuntimeParsesUrlAndMachineName()
    {
        var a = NdiNames.FromRuntime("LOCALHOST (Qubit Jhon NDI)", "192.168.8.110:5961");
        Assert.Equal("LOCALHOST (Qubit Jhon NDI)", a.Name);
        Assert.Equal("192.168.8.110", a.Address);
        Assert.Equal(5961, a.Port);
        Assert.Equal("LOCALHOST", a.Host);
    }

    [Fact]
    public void RuntimePathsPreferEnvThenNdi6RuntimeFolder()
    {
        var custom = Path.Combine("custom", "v6");
        var dirs = NdiRuntimePaths.Candidates(
            Path.Combine("pf"),
            new Dictionary<string, string?> { ["NDI_RUNTIME_DIR_V6"] = custom });
        Assert.Equal(Path.GetFullPath(Path.Combine(custom, "Processing.NDI.Lib.x64.dll")), dirs[0]);
        Assert.Contains(dirs, p => p.EndsWith(Path.Combine("NDI", "NDI 6 Runtime", "v6", "Processing.NDI.Lib.x64.dll"), StringComparison.Ordinal));
    }

    [Fact]
    public void QueryPacketAsksForNdiPtr()
    {
        var q = NdiNames.QueryPacket();
        Assert.True(q.Length > 12);
        var text = Encoding.ASCII.GetString(q);
        Assert.Contains("_ndi", text);
        Assert.Contains("_tcp", text);
    }

    [Fact]
    public void ParsesMdnsPtrAnswer()
    {
        var packet = PtrPacket("SHOW-PC (Arena)._ndi._tcp.local");
        var found = NdiNames.Parse(packet);
        Assert.Contains(found, a => a.Name == "SHOW-PC (Arena)");
    }

    [Fact]
    public void ConnectNdiPutsALiveCueOnALayer()
    {
        var session = new ProducerSession();
        session.NewShow();
        var displays = session.Show!.Displays.Count;
        var asset = session.ConnectNdi("SHOW-PC (Resolume)");
        Assert.Equal(AssetKind.Ndi, asset.Kind);
        Assert.True(LiveSources.IsNdi(asset));
        Assert.True(LiveSources.IsLive(asset));
        Assert.StartsWith("ndi:", asset.Url);
        var cue = session.Show!.Timelines.SelectMany(t => t.Cues).First(c => c.AssetId == asset.Id);
        Assert.True(cue.FreeRunning);
        Assert.False(string.IsNullOrEmpty(cue.LayerId));
        Assert.Equal(displays, session.Show.Displays.Count);

        var bound = session.ConnectNdi("SHOW-PC (Resolume)", "webcam-ndi");
        Assert.Equal(asset.Id, bound.Id);
        Assert.Equal("capture:webcam-ndi", bound.Url);
        Assert.True(LiveSources.IsCapture(bound));
        Assert.Contains(session.Show.CaptureDevices, d => d.Kind == "NDI" && d.Signal == "webcam-ndi");
    }

    [Fact]
    public void CatalogListsLanSourcesAndWebcamWithoutDuplicates()
    {
        var lan = new NdiAdvert[]
        {
            new("SHOW-PC (Resolume Arena)", "SHOW-PC.local", "192.168.8.10"),
            new("Qubit Jhon NDI", "localhost", "192.168.8.110"),
        };
        var devices = new (string Id, string Name)[]
        {
            ("cam", "Integrated Camera"),
            ("ndi", "NDI Webcam Input"),
        };
        var choices = NdiCatalog.Choices(lan, devices);
        Assert.Equal(3, choices.Count);
        Assert.Contains(choices, c => c.Name == "SHOW-PC (Resolume Arena)" && c.Detail!.Contains("192.168.8.10"));
        Assert.Contains(choices, c => c.Name == "Qubit Jhon NDI" && c.Detail!.Contains("192.168.8.110"));
        Assert.Contains(choices, c => c.Name == "NDI Webcam Input" && c.WebcamId == "ndi");
        Assert.DoesNotContain(choices, c => c.Name.Contains("Integrated"));
    }

    [Fact]
    public void ImportNdiAddsAnAssetYouPlaceOnALayerLikeAVideo()
    {
        var session = new ProducerSession();
        session.NewShow();
        var displays = session.Show!.Displays.Count;
        var layer = session.Show.Timelines[0].Layers[2];
        var asset = session.ImportNdi("NDI Camera Pro");
        Assert.Equal(AssetKind.Ndi, asset.Kind);
        Assert.Equal("NDI Camera Pro", asset.Name);
        Assert.DoesNotContain(session.Show.Timelines.SelectMany(t => t.Cues), c => c.AssetId == asset.Id);
        Assert.Equal(SelectionKind.Asset, session.Selection.Kind);
        Assert.Contains(asset.Id, session.Selection.Ids);

        var cue = session.AddCueFromAsset(asset.Id, layer.Id, 12_000);
        Assert.NotNull(cue);
        Assert.Equal(layer.Id, cue!.LayerId);
        Assert.Equal(0, cue.Start);
        Assert.True(cue.FreeRunning);
        Assert.Equal(displays, session.Show.Displays.Count);
    }

    [Fact]
    public void ConnectNdiReusesDemoPlaceholder()
    {
        var session = new ProducerSession();
        session.OpenDemo();
        var placeholder = session.Show!.Assets.First(a => a.Kind == AssetKind.Ndi);
        var bound = session.ConnectNdi("Phone Cam", "ndi-cam");
        Assert.Equal(placeholder.Id, bound.Id);
        Assert.Equal("Phone Cam", bound.Name);
        Assert.Equal("capture:ndi-cam", bound.Url);
    }

    static byte[] PtrPacket(string instance)
    {
        var buf = new List<byte>();
        void U16(int v) { buf.Add((byte)(v >> 8)); buf.Add((byte)(v & 0xff)); }
        U16(0);
        U16(0x8400);
        U16(0);
        U16(1);
        U16(0);
        U16(0);
        WriteName(buf, "_ndi._tcp.local");
        U16(12);
        U16(1);
        buf.AddRange([0, 0, 0, 1]);
        var rdata = new List<byte>();
        WriteName(rdata, instance);
        U16(rdata.Count);
        buf.AddRange(rdata);
        return buf.ToArray();
    }

    static void WriteName(List<byte> buf, string name)
    {
        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            buf.Add((byte)bytes.Length);
            buf.AddRange(bytes);
        }
        buf.Add(0);
    }
}
