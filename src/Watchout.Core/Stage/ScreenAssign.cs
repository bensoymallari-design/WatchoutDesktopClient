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

    public static int ScreenWidth(OutputScreen screen) =>
        screen.PhysicalWidth > 0 ? screen.PhysicalWidth : screen.Width;

    public static int ScreenHeight(OutputScreen screen) =>
        screen.PhysicalHeight > 0 ? screen.PhysicalHeight : screen.Height;

    /// <summary>
    /// True when Windows is driving the HDMI port at a different mode than the controller EDID.
    /// Stage/Use size still copy the controller size; Output placement uses Width×Height.
    /// </summary>
    public static bool WindowsModeDiffersFromController(OutputScreen screen) =>
        screen.Width > 0 && screen.Height > 0
        && (screen.Width != ScreenWidth(screen) || screen.Height != ScreenHeight(screen));

    public static string ScreenSizeText(OutputScreen screen)
    {
        var w = ScreenWidth(screen);
        var h = ScreenHeight(screen);
        return WindowsModeDiffersFromController(screen)
            ? $"{w}×{h} (Windows {screen.Width}×{screen.Height})"
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
