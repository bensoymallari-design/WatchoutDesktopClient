using Watchout.Core.Playback;
using Xunit;

namespace Watchout.Core.Tests;

public class VideoSyncTests
{
    [Fact]
    public void PlayStartSeeksOnlyWhenTheDecoderIsFarFromTheClock()
    {
        Assert.False(VideoSync.SeekOnPlayStart(0));
        Assert.False(VideoSync.SeekOnPlayStart(200));
        Assert.True(VideoSync.SeekOnPlayStart(400));
    }

    [Fact]
    public void PlayingDoesNotSeekAgainUntilCooldown()
    {
        Assert.False(VideoSync.ReseekWhilePlaying(2000, 200));
        Assert.False(VideoSync.ReseekWhilePlaying(500, 2000));
        Assert.True(VideoSync.ReseekWhilePlaying(2000, 2000));
    }

    [Fact]
    public void IdleScrubSeeksSmallJumpsButNotNoise()
    {
        Assert.False(VideoSync.SeekWhileIdle(40));
        Assert.True(VideoSync.SeekWhileIdle(200));
    }

    [Fact]
    public void RestartAfterWrapWhenDecoderIsStillAtTheEnd()
    {
        Assert.False(VideoSync.RestartAfterWrap(100, 80));
        Assert.True(VideoSync.RestartAfterWrap(21_000, 16));
        Assert.False(VideoSync.RestartAfterWrap(16, 21_000));
    }
}
