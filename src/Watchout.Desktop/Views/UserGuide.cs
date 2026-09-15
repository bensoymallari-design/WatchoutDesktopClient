using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Watchout.Core;

namespace Watchout.Desktop.Views;

public sealed class UserGuideWindow : Window
{
    public UserGuideWindow()
    {
        Title = $"User Guide — {Brand.Name} {Brand.Version}";
        Width = 720;
        Height = 760;
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
                    "WatchMe maps the WATCHOUT 7.8 Producer workflow onto a native Windows app. Pull origin/main (or this branch) to get it — missing a menu usually means the laptop is still on an older build.\n\n" +
                    "Stage and Output\n" +
                    "Cues bind to displays by position on the canvas, not a DisplayId field. Output is any extra OS screen (Win+P Extend). Pick the wall/TV, not the Producer laptop. Esc closes Output. H.264 stays a direct Canvas child so DXVA does not go black.\n\n" +
                    "Blind edit\n" +
                    "Edit → Blind Edit freezes Output on a snapshot. Producer Stage, timeline, and playhead stay live. Ctrl+T Take to Output reclones the show (including playheads) onto the wall. Uncheck Blind Edit to follow Producer again.\n\n" +
                    "Key / Fill\n" +
                    "Select a Display → Properties → Role Fill or Key, channel 1–4. Stage labels KEY n. Use a second Windows screen for the key output. This does not build a 10-bit SDI keyer.\n\n" +
                    "Group\n" +
                    "Select two or more cues → Assets → Group. Ungroup restores children. Cue Sets remain a lighter enable/disable grouping.\n\n" +
                    "Import WATCHOUT 6\n" +
                    "File → Import WATCHOUT 6. Native .watchme.json opens as-is. Other JSON/XML/zip is mapped best-effort (displays, cues, media names). Binary .watch from Dataton is not decoded — export JSON or rebuild media in WatchMe.\n\n" +
                    "Wake on LAN\n" +
                    "Select a Display, set the Runner MAC in Properties, then Output → Wake Display Node. Sends a UDP magic packet. The PC must allow WOL in firmware.\n\n" +
                    "Media versions\n" +
                    "Select a clip → Assets → Create H.264 version. ffmpeg writes a new MP4 and stores it as a revision. Switch revisions in Properties without rebuilding cues. Watch folder (Preferences) auto-imports new files.\n\n" +
                    "Color / GPU\n" +
                    "Show color space is a tag for the crew (Rec.709 default). WatchMe’s WPF window is 8-bit. GPU preference is a reminder to pin WatchMe.exe to the discrete GPU in Windows Settings → System → Display → Graphics.\n\n" +
                    "Dataton-only (not in WatchMe)\n" +
                    "ST 2110, NMOS, HAP encode, Notch LC, WATCHPAX, Access Control, MainConcept, 65535-ch WAV, LTC. Those need Dataton’s server stack.",
            },
        };
    }

    public static void ShowDialog(Window? owner)
    {
        var win = new UserGuideWindow();
        if (owner is not null) win.Owner = owner;
        win.ShowDialog();
    }
}
