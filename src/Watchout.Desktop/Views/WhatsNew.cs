using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Watchout.Core;

namespace Watchout.Desktop.Views;

public sealed class WhatsNewWindow : Window
{
    public WhatsNewWindow()
    {
        Title = $"What's New — {Brand.Name} {Brand.Version}";
        Width = 640;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(17, 17, 17));
        Foreground = new SolidColorBrush(Color.FromRgb(232, 230, 227));
        Content = new ScrollViewer
        {
            Padding = new Thickness(28),
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 22,
                Text =
                    "Producer tools for LED-wall shows (the WATCHOUT 7.8 creative set that runs locally in WatchMe).\n\n" +
                    "Linear wipe — angle, feather, and completion on each cue. 100 is fully on, 0 is hidden.\n" +
                    "Temperature and Exposure — warm/cool and EV overlays on Stage and Output.\n" +
                    "Chroma key — enable on a still, then Pick key color on Stage (eyedropper).\n" +
                    "Speed — play the clip faster or slower without re-encoding.\n" +
                    "Placeholder cues — park a slot on the timeline, assign media later.\n" +
                    "Replace media — keep old size, use new size, or fit proportionally.\n" +
                    "Display image mask — PNG/JPG on a display, used on Output for LED processor shapes.\n" +
                    "Pixel-perfect placement — cues snap to whole pixels so LED walls stay sharp.\n" +
                    "Auto-start — play a show when it opens, and optionally reopen the last show when WatchMe launches.\n\n" +
                    "Dataton WATCHOUT 7.8 also has ST 2110, HAP encode, NMOS, WATCHPAX, Access Control, and a .watch v6 importer. Those are their media-server stack, not copied here. WatchMe remains native Media Foundation / DXVA H.264 on extra Windows screens (Colorlight, NovaStar, TVs, projectors).",
            },
        };
    }

    public static void ShowDialog(Window? owner)
    {
        var win = new WhatsNewWindow();
        if (owner is not null) win.Owner = owner;
        win.ShowDialog();
    }
}
