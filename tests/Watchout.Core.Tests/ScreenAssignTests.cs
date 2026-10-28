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
            Width = 1920,
            Height = 1080,
            PhysicalWidth = 3840,
            PhysicalHeight = 1080,
        };
        Assert.True(ScreenAssign.WindowsModeDiffersFromController(scaled));
        Assert.Equal("MCTRL4K · wall/TV 1920×1080 (EDID 3840×1080)", ScreenAssign.ScreenChoiceLabel(scaled));
        var d = new Display { Width = 100, Height = 100 };
        ScreenAssign.CopyScreenSize(d, scaled);
        Assert.Equal(1920, d.Width);
        Assert.Equal(1080, d.Height);
    }

    [Fact]
    public void CopyScreenSizeUsesWindowsModeNotControllerEdid()
    {
        var windows = new OutputScreen
        {
            Id = "hdmi",
            Label = "HDMI",
            Width = 1920,
            Height = 1080,
            PhysicalWidth = 3840,
            PhysicalHeight = 2160,
        };
        Assert.Equal(1920, ScreenAssign.ScreenWidth(windows));
        Assert.Equal(1080, ScreenAssign.ScreenHeight(windows));
        var d = new Display { Width = 100, Height = 100 };
        ScreenAssign.CopyScreenSize(d, windows);
        Assert.Equal(1920, d.Width);
        Assert.Equal(1080, d.Height);
        var mapped = ScreenAssign.LayoutDisplaysOnScreens(
            [new Display { Id = "d1", Name = "Display 1", Width = 100, Height = 100, Enabled = true }],
            [
                new() { Id = "1", Label = "Laptop", IsPrimary = true, Width = 1920, Height = 1080 },
                windows,
            ]);
        Assert.Equal(1920, mapped[0].Width);
        Assert.Equal(1080, mapped[0].Height);
    }

    [Fact]
    public void DpiScaledFourKStillCopiesPhysicalPixels()
    {
        var dpi = new OutputScreen
        {
            Id = "tv",
            Label = "SyncMaster",
            Width = 2560,
            Height = 1440,
            PhysicalWidth = 3840,
            PhysicalHeight = 2160,
            ScaleFactor = 1.5,
        };
        Assert.True(ScreenAssign.LooksLikeDpiScaledMode(dpi));
        Assert.False(ScreenAssign.WindowsModeDiffersFromController(dpi));
        Assert.Equal(3840, ScreenAssign.ScreenWidth(dpi));
        Assert.Equal(2160, ScreenAssign.ScreenHeight(dpi));
        var d = new Display { Width = 100, Height = 100 };
        ScreenAssign.CopyScreenSize(d, dpi);
        Assert.Equal(3840, d.Width);
        Assert.Equal(2160, d.Height);
        Assert.Equal("SyncMaster · wall/TV 3840×2160", ScreenAssign.ScreenChoiceLabel(dpi));
    }

    [Fact]
    public void NvidiaCustomWindowsModeCopiesOntoStage()
    {
        Assert.Equal((3160, 2160), ScreenAssign.PickWindowsMode(3160, 2160, 1920, 1080));
        Assert.Equal((3160, 2160), ScreenAssign.PickWindowsMode(0, 0, 1920, 1080, 3160, 2160));
        Assert.Equal((3160, 2160), ScreenAssign.PickWindowsMode(3160, 2160, 3840, 2160));
        var custom = new OutputScreen
        {
            Id = "hdmi",
            Label = "Colorlight",
            Width = 3160,
            Height = 2160,
            PhysicalWidth = 3840,
            PhysicalHeight = 2160,
        };
        Assert.Equal(3160, ScreenAssign.ScreenWidth(custom));
        Assert.Equal(2160, ScreenAssign.ScreenHeight(custom));
        var d = new Display { Width = 100, Height = 100 };
        ScreenAssign.CopyScreenSize(d, custom);
        Assert.Equal(3160, d.Width);
        Assert.Equal(2160, d.Height);
        Assert.Equal("Colorlight · wall/TV 3160×2160 (EDID 3840×2160)", ScreenAssign.ScreenChoiceLabel(custom));
        var mapped = ScreenAssign.LayoutDisplaysOnScreens(
            [new Display { Id = "d1", Name = "Display 1", Width = 100, Height = 100, Enabled = true }],
            [
                new() { Id = "1", Label = "Laptop", IsPrimary = true, Width = 1920, Height = 1080 },
                custom,
            ]);
        Assert.Equal(3160, mapped[0].Width);
        Assert.Equal(2160, mapped[0].Height);
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

    [Fact]
    public void VirtualModeIndexUsesTheHighWord()
    {
        var packed = (7u << 16) | 1u;
        Assert.Equal(7, DisplayConnectors.ModeIndex(packed, 12, virtualMode: true));
        Assert.Equal(-1, DisplayConnectors.ModeIndex(packed, 12, virtualMode: false));
        Assert.Equal(3, DisplayConnectors.ModeIndex(3, 12, virtualMode: false));
        Assert.Equal(0, DisplayConnectors.ModeIndex(0, 12, virtualMode: false));
        Assert.Equal(-1, DisplayConnectors.ModeIndex(DisplayConnectors.InvalidModeIndex, 12, false));
        Assert.Equal(-1, DisplayConnectors.ModeIndex(DisplayConnectors.InvalidVirtualIndex << 16, 12, true));
    }

    [Fact]
    public void ConnectorNamesCoverLaptopAndDesktopPorts()
    {
        Assert.Equal("HDMI", DisplayConnectors.Name(DisplayConnectors.Hdmi));
        Assert.Equal("DisplayPort", DisplayConnectors.Name(DisplayConnectors.DisplayPort));
        Assert.Equal("USB-C", DisplayConnectors.Name(DisplayConnectors.UsbTunnel));
        Assert.Equal("USB-C", DisplayConnectors.Name(DisplayConnectors.DisplayPort, @"\\?\USB#VID_1234", "Thunderbolt Dock"));
        Assert.Equal("DVI", DisplayConnectors.Name(DisplayConnectors.Dvi));
        Assert.Equal("VGA", DisplayConnectors.Name(DisplayConnectors.Vga));
        Assert.Equal("SDI", DisplayConnectors.Name(DisplayConnectors.Sdi));
        Assert.Equal("USB", DisplayConnectors.Name(DisplayConnectors.IndirectWired));
        Assert.Equal("Internal", DisplayConnectors.Name(DisplayConnectors.Internal));
        Assert.Equal("Internal", DisplayConnectors.Name(DisplayConnectors.Lvds));
        Assert.Equal("Internal", DisplayConnectors.Name(DisplayConnectors.DisplayPortEmbedded));
        Assert.True(DisplayConnectors.IsInternalTechnology(DisplayConnectors.Internal));
        Assert.False(DisplayConnectors.IsInternalTechnology(DisplayConnectors.Hdmi));
        Assert.False(DisplayConnectors.IsInternalTechnology(DisplayConnectors.DisplayPort));
        Assert.False(DisplayConnectors.IsInternalTechnology(DisplayConnectors.UsbTunnel));
        Assert.True(DisplayConnectors.IsExternalName("HDMI"));
        Assert.True(DisplayConnectors.IsExternalName("USB-C"));
        Assert.False(DisplayConnectors.IsExternalName("Internal"));
    }

    [Fact]
    public void ExternalPortsStayWallsWhenWindowsMakesThemPrimary()
    {
        var panel = new OutputScreen
        {
            Id = "panel",
            Label = "Built-in",
            IsPrimary = false,
            IsInternal = true,
            Connector = "Internal",
            Width = 1920,
            Height = 1200,
            Left = 0,
        };
        var usbc = new OutputScreen
        {
            Id = "usbc",
            Label = "LG · USB-C",
            IsPrimary = true,
            Connector = "USB-C",
            Width = 3840,
            Height = 2160,
            Left = 1920,
        };
        var dp = new OutputScreen
        {
            Id = "dp",
            Label = "Colorlight · DisplayPort",
            Connector = "DisplayPort",
            Width = 1920,
            Height = 1080,
            Left = 5760,
        };
        var hdmi = new OutputScreen
        {
            Id = "hdmi",
            Label = "TV · HDMI",
            Connector = "HDMI",
            Width = 1920,
            Height = 1080,
            Left = 7680,
        };
        var screens = new List<OutputScreen> { panel, usbc, dp, hdmi };
        Assert.Equal(["usbc", "dp", "hdmi"], ScreenAssign.OutputPool(screens).Select(s => s.Id).ToArray());
        Assert.True(ScreenAssign.IsProducerScreen(panel, screens));
        Assert.False(ScreenAssign.IsProducerScreen(usbc, screens));
        Assert.Contains("Producer", ScreenAssign.ScreenChoiceLabel(panel, screens));
        Assert.Contains("wall/TV", ScreenAssign.ScreenChoiceLabel(usbc, screens));

        var display = new Display { Id = "d1", Name = "Display 1", Channel = 1, ScreenId = panel.Id };
        var resolved = ScreenAssign.ResolveOutputScreen(display, screens, null, out var skipped);
        Assert.True(skipped);
        Assert.Equal("usbc", resolved!.Id);
    }

    [Fact]
    public void DesktopPrimaryStaysTheProducer()
    {
        var gui = new OutputScreen
        {
            Id = "gui",
            Label = "Dell · HDMI",
            IsPrimary = true,
            Connector = "HDMI",
            Width = 2560,
            Height = 1440,
            Left = 0,
        };
        var wall = new OutputScreen
        {
            Id = "wall",
            Label = "Novastar · DisplayPort",
            Connector = "DisplayPort",
            Width = 1920,
            Height = 1080,
            Left = 2560,
        };
        var screens = new List<OutputScreen> { gui, wall };
        Assert.Equal("wall", Assert.Single(ScreenAssign.OutputPool(screens)).Id);
        Assert.Contains("Producer", ScreenAssign.ScreenChoiceLabel(gui, screens));
        Assert.Contains("wall/TV", ScreenAssign.ScreenChoiceLabel(wall, screens));
    }

    [Fact]
    public void SecondScreenOnlyIsTheOutput()
    {
        var tv = new OutputScreen
        {
            Id = "hdmi",
            Label = "TV · HDMI",
            IsPrimary = true,
            Connector = "HDMI",
            Width = 3840,
            Height = 2160,
        };
        var screens = new List<OutputScreen> { tv };
        Assert.Equal("hdmi", Assert.Single(ScreenAssign.OutputPool(screens)).Id);
        Assert.False(ScreenAssign.IsProducerScreen(tv, screens));
        Assert.Contains("wall/TV", ScreenAssign.ScreenChoiceLabel(tv, screens));
    }

    [Fact]
    public void ConfirmedPortModeIsNotReplacedByEdid()
    {
        var port = new OutputScreen
        {
            Id = "usbc",
            Label = "LG · USB-C",
            Connector = "USB-C",
            Width = 1920,
            Height = 1080,
            PhysicalWidth = 3840,
            PhysicalHeight = 2160,
            ScaleFactor = 2,
            ModeConfirmed = true,
        };
        Assert.True(ScreenAssign.LooksLikeDpiScaledMode(port));
        Assert.Equal(1920, ScreenAssign.ScreenWidth(port));
        Assert.Equal(1080, ScreenAssign.ScreenHeight(port));
        Assert.True(ScreenAssign.WindowsModeDiffersFromController(port));
        Assert.Equal("LG · USB-C · wall/TV 1920×1080 (EDID 3840×2160)", ScreenAssign.ScreenChoiceLabel(port));
    }
}
