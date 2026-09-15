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
