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

    [Fact]
    public void AssignmentKeyPinsAControllerAndCopySize()
    {
        var d = new Display { Name = "Display 1", Channel = 2, Width = 100, Height = 100 };
        Assert.Equal("auto:2", ScreenAssign.AssignmentKey(d));
        Assert.Equal("Auto (channel 1)", ScreenAssign.AutoChoiceLabel(1));
        ScreenAssign.ApplyAssignment(d, "mctrl4k");
        Assert.Equal("mctrl4k", d.ScreenId);
        ScreenAssign.ApplyAssignment(d, "auto:1");
        Assert.Null(d.ScreenId);
        Assert.Equal(1, d.Channel);
        ScreenAssign.CopyScreenSize(d, new OutputScreen
        {
            Id = "mctrl4k",
            Label = "MCTRL4K",
            Width = 3840,
            Height = 1080,
            PhysicalWidth = 3840,
            PhysicalHeight = 1080,
        });
        Assert.Equal(3840, d.Width);
        Assert.Equal(1080, d.Height);
    }

    [Fact]
    public void AssignScreensCopiesDesktopLayoutFromControllers()
    {
        var screens = new List<OutputScreen>
        {
            new() { Id = "1", Label = "Laptop", IsPrimary = true, Width = 1920, Height = 1080, PhysicalWidth = 1920, PhysicalHeight = 1080 },
            new() { Id = "mctrl", Label = "MCTRL4K", Left = 1920, Top = 0, Width = 1920, Height = 1080, PhysicalWidth = 1920, PhysicalHeight = 1080 },
            new() { Id = "nova", Label = "NovaStar", Left = 3840, Top = 0, Width = 2560, Height = 1080, PhysicalWidth = 2560, PhysicalHeight = 1080 },
        };
        var displays = new List<Display>
        {
            new() { Id = "d1", Name = "Display 1", Width = 100, Height = 100, Enabled = true },
            new() { Id = "d2", Name = "Display 2", Width = 100, Height = 100, Enabled = true },
        };
        var mapped = ScreenAssign.LayoutDisplaysOnScreens(displays, screens);
        Assert.Equal("mctrl", mapped[0].ScreenId);
        Assert.Equal("nova", mapped[1].ScreenId);
        Assert.Equal(0, mapped[0].X);
        Assert.Equal(1920, mapped[1].X);
        Assert.Equal(1920, mapped[0].Width);
        Assert.Equal(2560, mapped[1].Width);
    }
}
