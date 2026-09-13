using Watchout.Core.Models;
using Watchout.Core.Stage;
using Xunit;

namespace Watchout.Core.Tests;

public class ScreenAssignTests
{
    [Fact]
    public void OutputPoolPrefersExtras()
    {
        var screens = new List<OutputScreen>
        {
            new() { Id = "1", Label = "Laptop", IsPrimary = true, Width = 1920, Height = 1080, PhysicalWidth = 1920, PhysicalHeight = 1080 },
            new() { Id = "2", Label = "HDMI 1", Width = 1920, Height = 1080, PhysicalWidth = 1920, PhysicalHeight = 1080 },
            new() { Id = "3", Label = "HDMI 2", Width = 1920, Height = 1080, PhysicalWidth = 1920, PhysicalHeight = 1080 },
        };
        var pool = ScreenAssign.OutputPool(screens);
        Assert.Equal(2, pool.Count);
        Assert.All(pool, s => Assert.False(s.IsPrimary));

        var displays = new List<Display> { new() { Id = "d1", Name = "Display 1", Width = 100, Height = 100, Enabled = true } };
        var mapped = ScreenAssign.LayoutDisplaysOnScreens(displays, screens);
        Assert.Equal(2, mapped.Count(d => !string.IsNullOrEmpty(d.ScreenId)));
        Assert.Equal(0, mapped[0].X);
        Assert.Equal(1920, mapped[1].X);
        Assert.Equal("2", mapped[0].ScreenId);
    }
}
