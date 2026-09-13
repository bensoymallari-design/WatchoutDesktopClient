using System.Runtime.InteropServices;
using Watchout.Core.Models;

namespace Watchout.Desktop.Interop;

public static class Monitors
{
    public static List<OutputScreen> List()
    {
        var screens = new List<OutputScreen>();
        var primary = System.Windows.Forms.Screen.PrimaryScreen;
        var i = 0;
        foreach (var s in System.Windows.Forms.Screen.AllScreens)
        {
            i++;
            var bounds = s.Bounds;
            screens.Add(new OutputScreen
            {
                Id = $"{bounds.X},{bounds.Y},{bounds.Width}x{bounds.Height}",
                Label = s.DeviceName.Replace(@"\\.\", "") is { Length: > 0 } name ? name : (s.Primary ? "Primary" : $"Monitor {i}"),
                Left = bounds.X,
                Top = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
                PhysicalWidth = bounds.Width,
                PhysicalHeight = bounds.Height,
                IsPrimary = s.Primary || (primary is not null && s.DeviceName == primary.DeviceName),
                ScaleFactor = 1,
            });
        }
        if (screens.Count == 0)
        {
            screens.Add(new OutputScreen
            {
                Id = "primary",
                Label = "Primary",
                Width = 1920,
                Height = 1080,
                PhysicalWidth = 1920,
                PhysicalHeight = 1080,
                IsPrimary = true,
            });
        }
        return screens;
    }

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);
}
