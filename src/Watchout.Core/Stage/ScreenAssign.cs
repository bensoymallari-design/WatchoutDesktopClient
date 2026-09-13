using Watchout.Core.Models;

namespace Watchout.Core.Stage;

public static class ScreenAssign
{
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

    public static List<Display> LayoutDisplaysOnScreens(IReadOnlyList<Display> displays, IReadOnlyList<OutputScreen> screens, bool includePrimary = false)
    {
        var pool = OutputPool(screens, includePrimary);
        if (pool.Count == 0) return displays.ToList();
        double x = 0;
        var mapped = new List<Display>();
        for (var i = 0; i < pool.Count; i++)
        {
            var screen = pool[i];
            var prev = i < displays.Count ? displays[i] : ShowFactory.EmptyDisplay(new Display { Name = screen.Label, Channel = i + 1 });
            var width = screen.PhysicalWidth > 0 ? screen.PhysicalWidth : screen.Width;
            var height = screen.PhysicalHeight > 0 ? screen.PhysicalHeight : screen.Height;
            mapped.Add(new Display
            {
                Id = prev.Id,
                Name = string.IsNullOrEmpty(prev.Name) ? (screen.Label ?? $"Display {i + 1}") : prev.Name,
                X = x,
                Y = prev.Y,
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
            x += width;
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
}
