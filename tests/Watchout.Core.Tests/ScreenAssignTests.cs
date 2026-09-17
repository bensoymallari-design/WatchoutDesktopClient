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

    [Fact]
    public void ScreenChoiceLabelMarksExtrasAsWallOrTv()
    {
        var laptop = new OutputScreen { Id = "1", Label = "DISPLAY1", IsPrimary = true, Width = 1920, Height = 1080 };
        var colorlight = new OutputScreen { Id = "2", Label = "Colorlight", Width = 1920, Height = 1080 };
        var tv = new OutputScreen { Id = "3", Label = "HDMI", Width = 3840, Height = 2160 };
        Assert.Contains("Producer", ScreenAssign.ScreenChoiceLabel(laptop));
        Assert.Equal("Colorlight · wall/TV 1920×1080", ScreenAssign.ScreenChoiceLabel(colorlight));
        Assert.Equal("HDMI · wall/TV 3840×2160", ScreenAssign.ScreenChoiceLabel(tv));
        var scaled = new OutputScreen
        {
            Id = "mctrl",
            Label = "MCTRL4K",
            Width = 2560,
            Height = 720,
            PhysicalWidth = 3840,
            PhysicalHeight = 1080,
            ScaleFactor = 1.5,
        };
        Assert.True(ScreenAssign.LooksLikeDpiScaledMode(scaled));
        Assert.True(ScreenAssign.WindowsModeDiffersFromController(scaled));
        Assert.Equal("MCTRL4K · wall/TV 3840×1080 (Windows 2560×720)", ScreenAssign.ScreenChoiceLabel(scaled));
        var d = new Display { Width = 100, Height = 100 };
        ScreenAssign.CopyScreenSize(d, scaled);
        Assert.Equal(3840, d.Width);
        Assert.Equal(1080, d.Height);
    }

    [Fact]
    public void ColorlightX20CustomMapIsCopiedNotEdid1920()
    {
        var x20 = new OutputScreen
        {
            Id = "x20",
            Label = "Colorlight",
            Width = 516,
            Height = 430,
            PhysicalWidth = 1920,
            PhysicalHeight = 1080,
        };
        Assert.False(ScreenAssign.LooksLikeDpiScaledMode(x20));
        Assert.Equal(516, ScreenAssign.ScreenWidth(x20));
        Assert.Equal(430, ScreenAssign.ScreenHeight(x20));
        Assert.Equal("Colorlight · wall/TV 516×430 (EDID 1920×1080)", ScreenAssign.ScreenChoiceLabel(x20));
        var display = new Display { Width = 1920, Height = 1080 };
        ScreenAssign.CopyScreenSize(display, x20);
        Assert.Equal(516, display.Width);
        Assert.Equal(430, display.Height);

        var listed = ScreenAssign.PickLedMap(1920, 1080, 1920, 1080, [(1920, 1080), (516, 430), (1280, 720), (1400, 1050), (6720, 1344)]);
        Assert.Null(listed);
        var live = ScreenAssign.PickLedMap(516, 430, 1920, 1080, [(1920, 1080), (516, 430), (6720, 1344)]);
        Assert.Equal((516, 430), live);
        Assert.False(ScreenAssign.IsStandardTiming(516, 430));
        Assert.True(ScreenAssign.LooksLikeLedMap(516, 430));
        Assert.False(ScreenAssign.LooksLikeLedMap(1400, 1050));
        Assert.True(ScreenAssign.IsStandardTiming(1920, 1080));
        Assert.Null(ScreenAssign.PickLedMap(1920, 1080, 1920, 1080, [(1920, 1080), (1400, 1050), (1280, 720)]));

        var leftoverMap = new OutputScreen
        {
            Id = "x20",
            Label = "Colorlight",
            Width = 1920,
            Height = 1080,
            PhysicalWidth = 1920,
            PhysicalHeight = 1080,
            MappedWidth = 6720,
            MappedHeight = 1344,
        };
        Assert.Equal(1920, ScreenAssign.ScreenWidth(leftoverMap));
        Assert.Equal(1080, ScreenAssign.ScreenHeight(leftoverMap));
        var mappedDisplay = new Display { Width = 1920, Height = 1080 };
        ScreenAssign.CopyScreenSize(mappedDisplay, leftoverMap);
        Assert.Equal(1920, mappedDisplay.Width);
        Assert.Equal(1080, mappedDisplay.Height);
    }

    [Fact]
    public void LeftoverNvidiaCustomFromAnotherControllerIsIgnored()
    {
        // 6720×1344 is another controller still sitting in NVIDIA's list — not this X20.
        Assert.Null(ScreenAssign.PickLedMap(1920, 1080, 1920, 1080, [(1920, 1080), (4096, 2160), (6720, 1344), (1400, 1050)]));
        var liveX20 = ScreenAssign.PickLedMap(516, 430, 1920, 1080, [(1920, 1080), (6720, 1344), (516, 430)]);
        Assert.Equal((516, 430), liveX20);
        // If that other controller is still the live NVIDIA mode, follow it — but Stage 1920 is kept.
        Assert.Equal((6720, 1344), ScreenAssign.WindowsModePixels(1920, 1080, 6720, 1344));
        var displays = new List<Display> { new() { Id = "d1", Name = "Display 1", Width = 1920, Height = 1080, Enabled = true } };
        var screens = new List<OutputScreen>
        {
            new() { Id = "1", Label = "Laptop", IsPrimary = true, Width = 1920, Height = 1080 },
            new() { Id = "x20", Label = "X20 HDMI", Width = 516, Height = 430, PhysicalWidth = 1920, PhysicalHeight = 1080 },
        };
        var mapped = ScreenAssign.LayoutDisplaysOnScreens(displays, screens);
        Assert.Equal(1920, mapped[0].Width);
        Assert.Equal(1080, mapped[0].Height);
        Assert.Equal("x20", mapped[0].ScreenId);
        Assert.True(ScreenAssign.KeepStageSize(1920, 1080));
        Assert.False(ScreenAssign.KeepStageSize(100, 100));
    }

    [Fact]
    public void ResolveOutputSkipsTheProducerLaptopWhenAControllerExists()
    {
        var laptop = new OutputScreen { Id = "1", Label = "Laptop", IsPrimary = true, Width = 1920, Height = 1080, Left = 0 };
        var wall = new OutputScreen { Id = "mctrl", Label = "MCTRL4K", Width = 1920, Height = 1080, Left = 1920 };
        var screens = new List<OutputScreen> { laptop, wall };
        var display = new Display { Id = "d1", Name = "Display 1", Channel = 1, ScreenId = laptop.Id };

        var auto = ScreenAssign.ResolveOutputScreen(display, screens, null, out var skipped);
        Assert.True(skipped);
        Assert.Equal("mctrl", auto!.Id);

        var explicitLaptop = ScreenAssign.ResolveOutputScreen(display, screens, laptop, out skipped);
        Assert.False(skipped);
        Assert.Equal("1", explicitLaptop!.Id);

        display.ScreenId = wall.Id;
        var pinned = ScreenAssign.ResolveOutputScreen(display, screens, null, out skipped);
        Assert.False(skipped);
        Assert.Equal("mctrl", pinned!.Id);
    }

    [Fact]
    public void ResolveOutputTreatsColorlightAndTvsAsTheWall()
    {
        var laptop = new OutputScreen { Id = "1", Label = "Laptop", IsPrimary = true, Width = 1920, Height = 1080, Left = 0 };
        var colorlight = new OutputScreen { Id = "color", Label = "Colorlight", Width = 1920, Height = 1080, Left = 1920 };
        var tv = new OutputScreen { Id = "tv", Label = "Samsung", Width = 3840, Height = 2160, Left = 3840 };
        var display = new Display { Id = "d1", Name = "Display 1", Channel = 1, ScreenId = laptop.Id };

        var toColorlight = ScreenAssign.ResolveOutputScreen(display, [laptop, colorlight], null, out var skipped);
        Assert.True(skipped);
        Assert.Equal("color", toColorlight!.Id);

        display.ScreenId = tv.Id;
        var toTv = ScreenAssign.ResolveOutputScreen(display, [laptop, colorlight, tv], null, out skipped);
        Assert.False(skipped);
        Assert.Equal("tv", toTv!.Id);
    }
}
