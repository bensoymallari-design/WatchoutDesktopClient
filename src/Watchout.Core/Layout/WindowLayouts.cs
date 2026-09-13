using Watchout.Core.Models;

namespace Watchout.Core.Layout;

public static class WindowLayouts
{
    public static readonly Dictionary<WindowId, (string Title, string? Shortcut)> Meta = new()
    {
        [WindowId.Stage] = ("Stage", "Ctrl+Alt+S"),
        [WindowId.Properties] = ("Properties", null),
        [WindowId.Assets] = ("Assets", "Ctrl+Alt+A"),
        [WindowId.Timelines] = ("Timelines", "Ctrl+Alt+T"),
        [WindowId.Timeline] = ("Timeline", null),
        [WindowId.Devices] = ("Devices", "Ctrl+Alt+D"),
        [WindowId.Nodes] = ("Nodes", null),
        [WindowId.Variables] = ("Variables", "Ctrl+Alt+V"),
        [WindowId.Cues] = ("Cues", "Ctrl+Alt+C"),
        [WindowId.CueSets] = ("Cue Sets", null),
        [WindowId.Log] = ("Log", null),
    };

    public static List<WindowLayout> DefaultLayout() =>
    [
        new() { Id = WindowId.Stage, X = 0.4, Y = 0.4, W = 41.6, H = 51.2, Open = true, Z = 1 },
        new() { Id = WindowId.Properties, X = 42.4, Y = 0.4, W = 21.6, H = 51.2, Open = true, Z = 2 },
        new() { Id = WindowId.Assets, X = 64.4, Y = 0.4, W = 35.2, H = 31.4, Open = true, Z = 3 },
        new() { Id = WindowId.Timelines, X = 64.4, Y = 32.2, W = 35.2, H = 19.4, Open = true, Z = 4 },
        new() { Id = WindowId.Timeline, X = 0.4, Y = 52.0, W = 63.6, H = 47.5, Open = true, Z = 5 },
        new() { Id = WindowId.Devices, X = 64.4, Y = 52.0, W = 35.2, H = 47.5, Open = true, Z = 6 },
        new() { Id = WindowId.Nodes, X = 18, Y = 12, W = 48, H = 52, Open = false, Z = 20 },
        new() { Id = WindowId.Variables, X = 22, Y = 16, W = 36, H = 46, Open = false, Z = 21 },
        new() { Id = WindowId.Cues, X = 16, Y = 18, W = 62, H = 48, Open = false, Z = 22 },
        new() { Id = WindowId.CueSets, X = 28, Y = 20, W = 32, H = 40, Open = false, Z = 23 },
        new() { Id = WindowId.Log, X = 20, Y = 50, W = 50, H = 32, Open = false, Z = 24 },
    ];
}
