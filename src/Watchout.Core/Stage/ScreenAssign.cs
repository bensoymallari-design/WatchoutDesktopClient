using Watchout.Core.Models;

namespace Watchout.Core.Stage;

public static class ScreenAssign
{
    public const string AutoPrefix = "auto:";

    public static IReadOnlyList<OutputScreen> OutputPool(IReadOnlyList<OutputScreen> screens, bool includePrimary = false)
    {
        var extras = screens.Where(s => !s.IsPrimary).ToList();
        if (!includePrimary && extras.Count > 0) return extras;
        return screens;
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
    /// TVs, and projectors. They all appear as HDMI/DP monitors after Win+P Extend — WatchMe does not talk
    /// NovaStar protocol; it fills whichever extra OS screen the processor or TV presents.
    /// </summary>
    public static string ScreenChoiceLabel(OutputScreen screen) =>
        screen.IsPrimary
            ? $"{screen.Label} · Producer {ScreenSizeText(screen)}"
            : $"{screen.Label} · wall/TV {ScreenSizeText(screen)}";

    /// <summary>
    /// Size WatchMe copies onto Stage and the Output HWND. Colorlight X20-style
    /// maps (516×430) are the Windows/custom mode — EDID often still says 1920×1080.
    /// A laptop 150% DPI rectangle (2560) that matches EDID×scale stays on EDID (4K).
    /// </summary>
    public static int ScreenWidth(OutputScreen screen) => ScreenPixels(screen).W;

    public static int ScreenHeight(OutputScreen screen) => ScreenPixels(screen).H;

    public static (int W, int H) ScreenPixels(OutputScreen screen)
    {
        if (LooksLikeDpiScaledMode(screen))
        {
            var w = screen.PhysicalWidth > 0 ? screen.PhysicalWidth : screen.Width;
            var h = screen.PhysicalHeight > 0 ? screen.PhysicalHeight : screen.Height;
            return (Math.Max(1, w), Math.Max(1, h));
        }
        if (screen.MappedWidth >= 64 && screen.MappedHeight >= 64)
            return (screen.MappedWidth, screen.MappedHeight);
        if (screen.Width > 0 && screen.Height > 0)
            return (screen.Width, screen.Height);
        if (screen.PhysicalWidth > 0 && screen.PhysicalHeight > 0)
            return (screen.PhysicalWidth, screen.PhysicalHeight);
        return (1920, 1080);
    }

    public static bool LooksLikeDpiScaledMode(OutputScreen screen)
    {
        var scale = screen.ScaleFactor > 0.1 ? screen.ScaleFactor : 1;
        if (scale < 1.05) return false;
        if (screen.Width <= 0 || screen.Height <= 0) return false;
        if (screen.PhysicalWidth <= 0 || screen.PhysicalHeight <= 0) return false;
        var restoredW = screen.Width * scale;
        var restoredH = screen.Height * scale;
        return Math.Abs(restoredW - screen.PhysicalWidth) <= Math.Max(8, screen.PhysicalWidth * 0.05)
            && Math.Abs(restoredH - screen.PhysicalHeight) <= Math.Max(8, screen.PhysicalHeight * 0.05);
    }

    public static bool IsStandardTiming(int w, int h) =>
        (w, h) is
            (3840, 2160) or (3840, 1080) or (4096, 2160) or
            (2560, 1440) or (2560, 1080) or (1920, 1200) or (1920, 1080) or
            (1680, 1050) or (1600, 900) or (1366, 768) or (1360, 768) or
            (1280, 1024) or (1280, 800) or (1280, 720) or
            (1024, 768) or (800, 600) or (640, 480);

    /// <summary>
    /// GPU/TV extra modes (1400×1050, 720×480) must not beat a Colorlight
    /// cabinet map. 516×430 is ~6:5 — not 16:9 / 4:3 / 16:10.
    /// </summary>
    public static bool LooksLikeTvAspect(int w, int h)
    {
        if (w < 64 || h < 64) return false;
        var r = w / (double)h;
        return NearRatio(r, 16.0 / 9) || NearRatio(r, 16.0 / 10) || NearRatio(r, 4.0 / 3)
            || NearRatio(r, 5.0 / 4) || NearRatio(r, 3.0 / 2) || NearRatio(r, 21.0 / 9)
            || NearRatio(r, 32.0 / 9) || NearRatio(r, 5.0 / 3) || NearRatio(r, 8.0 / 5);
    }

    static bool NearRatio(double ratio, double target) => Math.Abs(ratio - target) < 0.04;

    public static bool LooksLikeLedMap(int w, int h) =>
        w >= 64 && h >= 64 && !IsStandardTiming(w, h) && !LooksLikeTvAspect(w, h);

    /// <summary>
    /// Colorlight X20 / LEDVISION custom output (cabinet map) is often 516×430
    /// or similar — not a TV 16:9 mode. Prefer that over a 1920 EDID.
    /// </summary>
    public static (int W, int H)? PickLedMap(
        int currentW, int currentH,
        int edidW, int edidH,
        IReadOnlyList<(int W, int H)> modes)
    {
        static bool Custom(int w, int h) => w >= 64 && h >= 64 && !IsStandardTiming(w, h);
        if (Custom(currentW, currentH)) return (currentW, currentH);
        var edidArea = Math.Max(1L, (long)(edidW > 0 ? edidW : currentW) * (edidH > 0 ? edidH : currentH));
        var maps = modes
            .Where(m => LooksLikeLedMap(m.W, m.H) && m.W * (long)m.H < edidArea * 85 / 100)
            .Distinct()
            .OrderByDescending(m => m.W * (long)m.H)
            .ToList();
        return maps.Count > 0 ? maps[0] : null;
    }

    /// <summary>
    /// True when Windows/EDID disagree. Colorlight maps still count even though
    /// Use size copies the Windows/custom pixels, not the EDID.
    /// </summary>
    public static bool WindowsModeDiffersFromController(OutputScreen screen) =>
        screen.Width > 0 && screen.PhysicalWidth > 0
        && (screen.Width != screen.PhysicalWidth || screen.Height != screen.PhysicalHeight);

    public static string ScreenSizeText(OutputScreen screen)
    {
        var used = ScreenPixels(screen);
        var edidW = screen.PhysicalWidth > 0 ? screen.PhysicalWidth : used.W;
        var edidH = screen.PhysicalHeight > 0 ? screen.PhysicalHeight : used.H;
        if (used.W == screen.Width && used.H == screen.Height
            && used.W == edidW && used.H == edidH)
            return $"{used.W}×{used.H}";
        if (used.W == screen.Width && used.H == screen.Height)
            return $"{used.W}×{used.H} (EDID {edidW}×{edidH})";
        if (used.W == edidW && used.H == edidH)
            return $"{used.W}×{used.H} (Windows {screen.Width}×{screen.Height})";
        return $"{used.W}×{used.H} (Windows {screen.Width}×{screen.Height}, EDID {edidW}×{edidH})";
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
        var extras = screens.Where(s => !s.IsPrimary).ToList();
        var pool = extras.Count > 0 ? extras : screens.ToList();
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
        if (!chosen.IsPrimary || screens.Count(s => !s.IsPrimary) == 0) return chosen;
        var extra = PreferredOutputScreen(screens, display.Channel);
        if (extra is null || extra.Id == chosen.Id) return chosen;
        skippedProducer = true;
        return extra;
    }

}
