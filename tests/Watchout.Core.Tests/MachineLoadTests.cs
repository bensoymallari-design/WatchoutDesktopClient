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

        var fullRam = Sample(cpu: 10, ramUsed: 15.5, ramTotal: 16, gpuUsed: 1, gpuTotal: 8);
        Assert.Equal(LoadLevel.Full, MachineLoad.Grade(fullRam));
        Assert.False(MachineLoad.CanLoadMore(fullRam));
        Assert.False(MachineLoad.CanLoadAnother4K(fullRam));
        Assert.Contains("RAM is gone", MachineLoad.Headline(fullRam), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("System memory is full", MachineLoad.Advice(fullRam));
    }

    [Fact]
    public void EightGigLaptopPlaying4KIsNotA1080pWarning()
    {
        var laptop = Sample(cpu: 2, ramUsed: 6.5, ramTotal: 7.7, gpuUsed: 0.3, gpuTotal: 3.5);
        Assert.Equal(LoadLevel.Ok, MachineLoad.CpuLevel(laptop));
        Assert.Equal(LoadLevel.Ok, MachineLoad.GpuLevel(laptop));
        Assert.Equal(LoadLevel.Ok, MachineLoad.Grade(laptop));
        Assert.True(MachineLoad.CanLoadAnother4K(laptop));
        Assert.Contains("room to load", MachineLoad.Headline(laptop), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1080p", MachineLoad.Headline(laptop));

        var ramTight = Sample(cpu: 2, ramUsed: 7.0, ramTotal: 7.7, gpuUsed: 0.3, gpuTotal: 3.5);
        Assert.Equal(LoadLevel.Tight, MachineLoad.RamLevel(ramTight));
        Assert.Equal(LoadLevel.Tight, MachineLoad.Grade(ramTight));
        Assert.True(MachineLoad.CanLoadAnother4K(ramTight));
        Assert.Contains("RAM is high", MachineLoad.Headline(ramTight), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1080p", MachineLoad.Headline(ramTight));
        Assert.False(MachineLoad.LogAsMachineWarn(ramTight));
        Assert.Equal(LoadLevel.Tight, MachineLoad.RamLevel(ramTight, LoadLevel.Tight));
        var dip = Sample(cpu: 2, ramUsed: 6.5, ramTotal: 7.7, gpuUsed: 0.3, gpuTotal: 3.5);
        Assert.Equal(LoadLevel.Tight, MachineLoad.RamLevel(dip, LoadLevel.Tight));
        var recovered = Sample(cpu: 2, ramUsed: 6.0, ramTotal: 7.7, gpuUsed: 0.3, gpuTotal: 3.5);
        Assert.Equal(LoadLevel.Ok, MachineLoad.RamLevel(recovered, LoadLevel.Tight));
        Assert.Equal(80, MachineLoad.RamRelease);
        var gpuTight = Sample(cpu: 20, ramUsed: 4, ramTotal: 16, gpuUsed: 6.2, gpuTotal: 8);
        Assert.True(MachineLoad.LogAsMachineWarn(gpuTight));
        var fullRam = Sample(cpu: 10, ramUsed: 15.5, ramTotal: 16, gpuUsed: 1, gpuTotal: 8);
        Assert.False(MachineLoad.LogAsMachineWarn(fullRam));
        var laptopRamFull = Sample(cpu: 18, ramUsed: 7.5, ramTotal: 7.7, gpuUsed: 0.9, gpuTotal: 3.5);
        Assert.Equal(LoadLevel.Full, MachineLoad.RamLevel(laptopRamFull));
        Assert.False(MachineLoad.LogAsMachineWarn(laptopRamFull));
        var gpuFull = Sample(cpu: 18, ramUsed: 7.5, ramTotal: 7.7, gpuUsed: 3.4, gpuTotal: 3.5);
        Assert.True(MachineLoad.LogAsMachineWarn(gpuFull));
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
        Assert.Equal("5.7 / 7.7 GB", MachineLoad.PairBytes(
            (long)(5.7 * 1024 * 1024 * 1024), (long)(7.7 * 1024 * 1024 * 1024)));
        Assert.Equal("0.2 / 3.5 GB", MachineLoad.PairBytes(
            159L * 1024 * 1024, (long)(3.5 * 1024 * 1024 * 1024)));
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
        var s = Sample(cpu: 94, ramUsed: 15.5, ramTotal: 16, gpuUsed: 7.5, gpuTotal: 8);
        var line = MachineLoad.WarnLine(s);
        Assert.Contains("CPU  94%", line);
        Assert.Contains("RAM", line);
        Assert.Contains("GPU", line);
        Assert.Contains("Full", line);
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
