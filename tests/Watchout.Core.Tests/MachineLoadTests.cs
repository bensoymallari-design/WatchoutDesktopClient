using Watchout.Core;
using Watchout.Core.Machine;
using Watchout.Core.Models;
using Xunit;

namespace Watchout.Core.Tests;

public class MachineLoadTests
{
    [Fact]
    public void GradesCpuRamAndGpuSeparately()
    {
        var ok = Sample(cpu: 20, ramUsed: 4, ramTotal: 16, gpuUsed: 2, gpuTotal: 8);
        Assert.Equal(LoadLevel.Ok, MachineLoad.Grade(ok));
        Assert.True(MachineLoad.CanLoadMore(ok));
        Assert.True(MachineLoad.CanLoadAnother4K(ok));
        Assert.Contains("room to load", MachineLoad.Headline(ok), StringComparison.OrdinalIgnoreCase);

        var tightGpu = Sample(cpu: 20, ramUsed: 4, ramTotal: 16, gpuUsed: 6.2, gpuTotal: 8);
        Assert.Equal(LoadLevel.Tight, MachineLoad.Grade(tightGpu));
        Assert.Contains("1080p", MachineLoad.Headline(tightGpu));

        var fullRam = Sample(cpu: 10, ramUsed: 15, ramTotal: 16, gpuUsed: 1, gpuTotal: 8);
        Assert.Equal(LoadLevel.Full, MachineLoad.Grade(fullRam));
        Assert.False(MachineLoad.CanLoadMore(fullRam));
        Assert.False(MachineLoad.CanLoadAnother4K(fullRam));
        Assert.Contains("do not add", MachineLoad.Headline(fullRam), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("System memory is full", MachineLoad.Advice(fullRam));
    }

    [Fact]
    public void BlocksAnother4KWhenGpuHeadroomIsGone()
    {
        var tight = Sample(cpu: 40, ramUsed: 8, ramTotal: 32, gpuUsed: 3.56, gpuTotal: 4);
        Assert.Equal(LoadLevel.Tight, MachineLoad.GpuLevel(tight));
        Assert.True(MachineLoad.CanLoadMore(tight));
        Assert.False(MachineLoad.CanLoadAnother4K(tight));
        Assert.Contains("may hitch", MachineLoad.Headline(tight), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingGpuNumbersDoNotFalseAlarm()
    {
        var s = Sample(cpu: 12, ramUsed: 6, ramTotal: 16, gpuUsed: 0, gpuTotal: 0);
        Assert.Equal(LoadLevel.Ok, MachineLoad.GpuLevel(s));
        Assert.Equal("GPU  —", MachineLoad.GpuLine(s));
        Assert.Equal("—", MachineLoad.GpuValue(s));
        Assert.True(MachineLoad.CanLoadAnother4K(s));
    }

    [Fact]
    public void FormatsMetersAndCountsA4KFileOnThePlayhead()
    {
        Assert.Equal("2.0 GB", MachineLoad.Bytes(2L * 1024 * 1024 * 1024));
        Assert.Equal("512 MB", MachineLoad.Bytes(512L * 1024 * 1024));
        var show = ShowFactory.EmptyShow();
        var clip = ShowFactory.EmptyAsset(new Asset
        {
            Kind = AssetKind.Video,
            Name = "Wall",
            Width = 3840,
            Height = 2160,
            Duration = 10_000,
            Url = "/tmp/wall.mp4",
            OriginalPath = "/tmp/wall.mp4",
        });
        show.Assets.Add(clip);
        show.Timelines[0].Cues.Add(ShowFactory.EmptyCue(new Cue
        {
            Type = CueType.Media,
            Name = "Wall",
            AssetId = clip.Id,
            LayerId = show.Timelines[0].Layers[0].Id,
            Start = 0,
            Duration = 10_000,
        }));
        show.Timelines[0].Playhead = 1000;
        var load = MachineLoad.CountShow(show, liveOutputs: 1);
        Assert.Equal(1, load.Videos);
        Assert.Equal(1, load.Outputs);
        Assert.InRange(load.FourK, 0.99, 1.01);
        Assert.Contains("1 video", load.Line());
        Assert.Contains("1 output", load.Line());
        Assert.Contains("4K", load.Line());
    }

    [Fact]
    public void WarnLineNamesTheFullMeters()
    {
        var s = Sample(cpu: 94, ramUsed: 15, ramTotal: 16, gpuUsed: 7.5, gpuTotal: 8);
        var line = MachineLoad.WarnLine(s);
        Assert.Contains("CPU  94%", line);
        Assert.Contains("RAM", line);
        Assert.Contains("GPU", line);
        Assert.Contains("do not add", line, StringComparison.OrdinalIgnoreCase);
    }

    static MachineSample Sample(double cpu, double ramUsed, double ramTotal, double gpuUsed, double gpuTotal) =>
        new(
            cpu,
            (long)(ramUsed * 1024 * 1024 * 1024),
            (long)(ramTotal * 1024 * 1024 * 1024),
            (long)(gpuUsed * 1024 * 1024 * 1024),
            (long)(gpuTotal * 1024 * 1024 * 1024),
            800L * 1024 * 1024,
            "Test GPU",
            new ShowLoad(1, 0, 0, 1, (long)(3840 * 2160)));
}
