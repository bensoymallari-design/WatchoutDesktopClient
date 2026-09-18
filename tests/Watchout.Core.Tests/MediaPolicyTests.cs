using Watchout.Core.Media;
using Xunit;

namespace Watchout.Core.Tests;

public class MediaPolicyTests
{
    [Fact]
    public void ImportNeverCopiesMasters()
    {
        Assert.False(MediaPolicy.ShouldCopyOnImport(8L * 1024 * 1024));
        Assert.False(MediaPolicy.ShouldCopyOnImport(500L * 1024 * 1024));
        Assert.False(MediaPolicy.ShouldCopyOnImport(100L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void Huge4kFilesSkipAFullResProxy()
    {
        Assert.False(MediaPolicy.ShouldBuildFullProxy(100L * 1024 * 1024 * 1024, 3840, 2160));
        Assert.True(MediaPolicy.ShouldBuildHap(200L * 1024 * 1024, 3840, 2160));
        Assert.False(MediaPolicy.ShouldBuildFullProxy(200L * 1024 * 1024, 3840, 2160));
        Assert.True(MediaPolicy.ShouldBuildFullProxy(200L * 1024 * 1024, 1920, 1080));
    }

    [Fact]
    public void FormatsByteCounts()
    {
        Assert.Equal("100.00 GB", MediaPolicy.FormatBytes(100L * 1024 * 1024 * 1024));
    }
}
