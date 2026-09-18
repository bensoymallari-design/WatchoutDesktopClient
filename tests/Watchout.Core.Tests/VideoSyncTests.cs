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

    [Fact]
    public void PlayingSeeksForwardWhenTheDecoderIsStillOnTheFirstFrame()
    {
        Assert.False(VideoSync.SeekCatchUp(22_000, 22_100));
        Assert.True(VideoSync.SeekCatchUp(0, 22_203));
        Assert.False(VideoSync.SeekCatchUp(22_203, 0));
        Assert.True(VideoSync.SnapOutputAfterDisplayWake(true, 400));
        Assert.False(VideoSync.SnapOutputAfterDisplayWake(false, 400));
        Assert.False(VideoSync.SnapOutputAfterDisplayWake(true, 50));
        Assert.Equal(2000, VideoSync.DisplayWakeResnapMs);
    }

    [Fact]
    public void DecoderStalledAfterTheClockStopsAdvancing()
    {
        Assert.False(VideoSync.DecoderStalled(false, 10_000, 10_000));
        Assert.False(VideoSync.DecoderStalled(true, 500, 10_000));
        Assert.False(VideoSync.DecoderStalled(true, 10_000, 500));
        Assert.True(VideoSync.DecoderStalled(true, 5_000, 5_000));
    }

    [Fact]
    public void OutputVideoLayoutHoldsWhileStageIsBusy()
    {
        Assert.False(VideoSync.HoldOutputVideoLayout(false, true));
        Assert.False(VideoSync.HoldOutputVideoLayout(true, false));
        Assert.True(VideoSync.HoldOutputVideoLayout(true, true));
        Assert.False(VideoSync.VideoLayoutChanged(100, 101));
        Assert.True(VideoSync.VideoLayoutChanged(100, 104));
    }

    [Fact]
    public void PauseSeekReadsANewFrameEvenIfTheLastOneWasReady()
    {
        Assert.True(VideoSync.ReadFrameAfterIdleSeek(seeked: true, hadFrame: true));
        Assert.True(VideoSync.ReadFrameAfterIdleSeek(seeked: true, hadFrame: false));
        Assert.True(VideoSync.ReadFrameAfterIdleSeek(seeked: false, hadFrame: false));
        Assert.False(VideoSync.ReadFrameAfterIdleSeek(seeked: false, hadFrame: true));
    }
}
