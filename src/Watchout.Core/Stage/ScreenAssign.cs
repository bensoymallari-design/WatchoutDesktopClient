using Watchout.Core.Models;

namespace Watchout.Core.Stage;

public static class ScreenAssign
{
    public const string AutoPrefix = "auto:";

    public static IReadOnlyList<OutputScreen> OutputPool(IReadOnlyList<OutputScreen> screens, bool includePrimary = false)
    {
        if (includePrimary) return screens.ToList();
        var walls = screens.Where(s => !IsProducerScreen(s, screens)).ToList();
        if (walls.Count > 0) return walls;
        return screens.ToList();
    }

    public static bool IsInternalPanel(OutputScreen screen) =>
        screen.IsInternal
        || string.Equals(screen.Connector, "Internal", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The producer GUI is the built-in panel when the machine has one
    /// (laptop, tablet). HDMI, DisplayPort, USB-C, HDMI-to-C, DVI, and the
    /// other extended outputs stay walls even if Windows made the TV the
    /// primary display. A desktop has no built-in panel, so the primary
    /// monitor stays the GUI and the other ports are walls. Second-screen-only
    /// (one external port) is the wall.
    /// </summary>
    public static bool IsProducerScreen(OutputScreen screen, IReadOnlyList<OutputScreen>? topology = null)
    {
        if (IsInternalPanel(screen)) return true;
        if (topology is not null && topology.Any(IsInternalPanel))
            return false;
        if (topology is { Count: <= 1 })
            return false;
        return screen.IsPrimary;
    }

    public static OutputScreen? ScreenForDisplay(Display display, IReadOnlyList<OutputScreen> screens)
    {
        if (!string.IsNullOrEmpty(display.ScreenId))
        {
            var pinned = screens.FirstOrDefault(s => s.Id == display.ScreenId);
            if (pinned is not null) return pinned;
        }
        var pool = OutputPool(screens);
        var idx = Math.Max(0, (display.Channel == 0 ? 1 : display.Channel) - 1);
        if (pool.Count == 0) return screens.FirstOrDefault();
        return pool[Math.Min(idx, pool.Count - 1)];
    }

    public static string AutoKey(int channel) => $"{AutoPrefix}{Math.Max(1, channel)}";

    public static bool IsAutoKey(string? key) =>
        string.IsNullOrEmpty(key) || key.StartsWith(AutoPrefix, StringComparison.Ordinal);

    public static string AssignmentKey(Display display) =>
        string.IsNullOrEmpty(display.ScreenId) ? AutoKey(display.Channel) : display.ScreenId;

    public static string AutoChoiceLabel(int channel) => $"Auto (channel {Math.Max(1, channel)})";

    /// <summary>
    /// Extra Windows screens are the show outputs: LED processors (Colorlight, NovaStar, MCTRL, Linsn, Brompton…),
    /// TVs, and projectors. After Win+P Extend they are HDMI, DisplayPort, USB-C, HDMI-to-C, DVI, or USB
    /// adapters — WatchMe does not talk NovaStar protocol; it fills whichever OS screen that port presents.
    /// </summary>
    public static string ScreenChoiceLabel(OutputScreen screen, IReadOnlyList<OutputScreen>? topology = null)
    {
        var role = IsProducerScreen(screen, topology) ? "Producer" : "wall/TV";
        return $"{screen.Label} · {role} {ScreenSizeText(screen)}";
    }

    public const int MinModePx = 64;

    /// <summary>
    /// The mode Windows is actually driving on that extra screen — NVIDIA custom
    /// (3160×2160), Display settings, or the current GDI/CCD size. Never shrink
    /// with Min(rcMonitor, current): that dropped custom modes larger than the
    /// leftover monitor rectangle. Do not scan every NVIDIA leftover mode.
    /// </summary>
    public static (int W, int H) PickWindowsMode(int currentW, int currentH, int monitorW, int monitorH, int ccdW = 0, int ccdH = 0)
    {
        if (currentW >= MinModePx && currentH >= MinModePx)
            return (currentW, currentH);
        if (ccdW >= MinModePx && ccdH >= MinModePx)
            return (ccdW, ccdH);
        if (monitorW >= MinModePx && monitorH >= MinModePx)
            return (monitorW, monitorH);
        if (currentW > 0 && currentH > 0) return (currentW, currentH);
        if (ccdW > 0 && ccdH > 0) return (ccdW, ccdH);
        if (monitorW > 0 && monitorH > 0) return (monitorW, monitorH);
        return (1920, 1080);
    }

    /// <summary>
    /// Stage copies the mode Windows is scanning (Width×Height) on HDMI,
    /// DisplayPort, USB-C, and HDMI-to-C. A 1920×1080 port stays 1920×1080
    /// even when EDID prefers 3840. NVIDIA custom 3160×2160 is copied as
    /// 3160×2160. A 150% reading that was never confirmed by Windows (no
    /// DEVMODE) still expands to the 3840 panel so the wall is not 2560.
    /// </summary>
    public static int ScreenWidth(OutputScreen screen)
    {
        if (!screen.ModeConfirmed && LooksLikeDpiScaledMode(screen) && screen.PhysicalWidth > 0)
            return screen.PhysicalWidth;
        if (screen.Width > 0) return screen.Width;
        return screen.PhysicalWidth > 0 ? screen.PhysicalWidth : 1920;
    }

    public static int ScreenHeight(OutputScreen screen)
    {
        if (!screen.ModeConfirmed && LooksLikeDpiScaledMode(screen) && screen.PhysicalHeight > 0)
            return screen.PhysicalHeight;
        if (screen.Height > 0) return screen.Height;
        return screen.PhysicalHeight > 0 ? screen.PhysicalHeight : 1080;
    }

    public static bool LooksLikeDpiScaledMode(OutputScreen screen)
    {
        var scale = screen.ScaleFactor;
        if (scale < 1.05) return false;
        if (screen.Width <= 0 || screen.Height <= 0) return false;
        if (screen.PhysicalWidth <= 0 || screen.PhysicalHeight <= 0) return false;
        return Approx(screen.Width * scale, screen.PhysicalWidth)
            && Approx(screen.Height * scale, screen.PhysicalHeight);
    }

    static bool Approx(double value, int target) =>
        Math.Abs(value - target) <= Math.Max(2, target * 0.02);

    /// <summary>
    /// True when Windows is driving the port at a different mode than the
    /// controller EDID (not an unconfirmed 150% DPI reading). Stage still
    /// copies the Windows mode.
    /// </summary>
    public static bool WindowsModeDiffersFromController(OutputScreen screen)
    {
        if (screen.Width <= 0 || screen.Height <= 0) return false;
        if (screen.PhysicalWidth <= 0 || screen.PhysicalHeight <= 0) return false;
        if (screen.Width == screen.PhysicalWidth && screen.Height == screen.PhysicalHeight) return false;
        if (!screen.ModeConfirmed && LooksLikeDpiScaledMode(screen)) return false;
        return true;
    }

    public static string ScreenSizeText(OutputScreen screen)
    {
        var w = ScreenWidth(screen);
        var h = ScreenHeight(screen);
        return WindowsModeDiffersFromController(screen)
            ? $"{w}×{h} (EDID {screen.PhysicalWidth}×{screen.PhysicalHeight})"
            : $"{w}×{h}";
    }

    public static void ApplyAssignment(Display display, string? key)
    {
        if (IsAutoKey(key))
        {
            display.ScreenId = null;
            if (key is { Length: > 0 } && key.StartsWith(AutoPrefix, StringComparison.Ordinal)
                && int.TryParse(key[AutoPrefix.Length..], out var ch) && ch > 0)
                display.Channel = ch;
            return;
        }
        display.ScreenId = key;
    }

    public static void CopyScreenSize(Display display, OutputScreen screen)
    {
        display.Width = ScreenWidth(screen);
        display.Height = ScreenHeight(screen);
    }

    public static (int Left, int Top) LayoutOrigin(IReadOnlyList<OutputScreen> screens)
    {
        if (screens.Count == 0) return (0, 0);
        return (screens.Min(s => s.Left), screens.Min(s => s.Top));
    }

    public static bool ScreensSpread(IReadOnlyList<OutputScreen> screens) =>
        screens.Count > 1 && (screens.Max(s => s.Left) != screens.Min(s => s.Left) || screens.Max(s => s.Top) != screens.Min(s => s.Top));

    public static List<Display> LayoutDisplaysOnScreens(IReadOnlyList<Display> displays, IReadOnlyList<OutputScreen> screens, bool includePrimary = false)
    {
        var pool = OutputPool(screens, includePrimary);
        if (pool.Count == 0) return displays.ToList();
        var spread = ScreensSpread(pool);
        var origin = LayoutOrigin(pool);
        double packedX = 0;
        var mapped = new List<Display>();
        for (var i = 0; i < pool.Count; i++)
        {
            var screen = pool[i];
            var prev = i < displays.Count ? displays[i] : ShowFactory.EmptyDisplay(new Display { Name = screen.Label, Channel = i + 1 });
            var width = ScreenWidth(screen);
            var height = ScreenHeight(screen);
            mapped.Add(new Display
            {
                Id = prev.Id,
                Name = string.IsNullOrEmpty(prev.Name) ? (screen.Label ?? $"Display {i + 1}") : prev.Name,
                X = spread ? screen.Left - origin.Left : packedX,
                Y = spread ? screen.Top - origin.Top : prev.Y,
                Z = prev.Z,
                Width = width,
                Height = height,
                Rotation = prev.Rotation,
                OutputType = prev.OutputType,
                Channel = i + 1,
                NodeId = string.IsNullOrEmpty(prev.NodeId) ? "local-runner" : prev.NodeId,
                Enabled = prev.Enabled,
                Blend = prev.Blend,
                BlendWidth = prev.BlendWidth,
                Virtual = false,
                ScreenId = screen.Id,
            });
            if (!spread) packedX += width;
        }
        mapped.AddRange(displays.Skip(pool.Count));
        return mapped;
    }

    public static OutputScreen? PreferredOutputScreen(IReadOnlyList<OutputScreen> screens, int channel = 1)
    {
        var pool = OutputPool(screens);
        if (pool.Count == 0) return null;
        return pool[Math.Max(0, Math.Min(pool.Count - 1, channel - 1))];
    }

    public static OutputScreen? ResolveOutputScreen(Display display, IReadOnlyList<OutputScreen> screens, OutputScreen? requested = null)
        => ResolveOutputScreen(display, screens, requested, out _);

    public static OutputScreen? ResolveOutputScreen(Display display, IReadOnlyList<OutputScreen> screens, OutputScreen? requested, out bool skippedProducer)
    {
        skippedProducer = false;
        if (requested is not null)
            return screens.FirstOrDefault(s => s.Id == requested.Id) ?? requested;
        var chosen = ScreenForDisplay(display, screens) ?? screens.FirstOrDefault();
        if (chosen is null) return null;
        if (!IsProducerScreen(chosen, screens) || !screens.Any(s => !IsProducerScreen(s, screens))) return chosen;
        var extra = PreferredOutputScreen(screens, display.Channel);
        if (extra is null || extra.Id == chosen.Id) return chosen;
        skippedProducer = true;
        return extra;
    }

}
