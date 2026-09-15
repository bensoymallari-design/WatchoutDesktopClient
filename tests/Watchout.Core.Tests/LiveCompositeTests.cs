using Watchout.Core.Stage;
using Xunit;

namespace Watchout.Core.Tests;

public class LiveCompositeTests
{
    [Fact]
    public void ScreenOverlayOnOutputWhenLiveFeedIsInFrontOfVideo()
    {
        Assert.True(LiveComposite.ScreenOverlayOnOutput(101, [100]));
        Assert.True(LiveComposite.ScreenOverlayOnOutput(100, []));
        Assert.False(LiveComposite.ScreenOverlayOnOutput(100, [101]));
        Assert.False(LiveComposite.ScreenOverlayOnOutput(100, [100]));
        Assert.True(LiveComposite.ScreenOverlayOnOutput(102, [100, 101]));
        Assert.False(LiveComposite.ScreenOverlayOnOutput(101, [100, 102]));
        Assert.Equal(new[] { 101, 102 }, LiveComposite.OverlayStack([102, 100, 101], [100]));
        Assert.Equal(new[] { 102 }, LiveComposite.OverlayStack([101, 102], [101]));
        Assert.Empty(LiveComposite.OverlayStack([100], [101]));
    }

    [Fact]
    public void StackFeedsFollowsLayerSwapSoLaterLayerStaysInFront()
    {
        Assert.Equal(new[] { "ndi1", "ndi2" }, LiveComposite.StackFeeds([("ndi1", 101), ("ndi2", 102)], [100]));
        Assert.Equal(new[] { "ndi2", "ndi1" }, LiveComposite.StackFeeds([("ndi1", 102), ("ndi2", 101)], [100]));
        Assert.Equal("ndi1", LiveComposite.StackFeeds([("ndi2", 101), ("ndi1", 102)], [100])[^1]);
        Assert.Equal(new[] { "ndi2" }, LiveComposite.StackFeeds([("ndi1", 101), ("ndi2", 102)], [101]));
    }

    [Fact]
    public void StickIgnoresOnePixelJitter()
    {
        Assert.Equal(1920, LiveComposite.Stick(1921, 1920));
        Assert.Equal(1920, LiveComposite.Stick(1919, 1920));
        Assert.Equal(1280, LiveComposite.Stick(1280, 1920));
        Assert.Equal(100, LiveComposite.Stick(100, 0));
    }
}
