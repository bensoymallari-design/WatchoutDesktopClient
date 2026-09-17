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
                    "Startup\n" +
                    "Launch shows a splash that names each engine and starts it: folders/settings, the 60 Hz show controller, WASAPI audio, D3D11/DXVA video, attached screens, NDI Runtime, HDMI/SDI capture cards, ffmpeg. Welcome opens after that boot, so Play is not the first time those drivers wake up. Help → About lists which steps came up. Producer’s lower-right tab opens on Devices (screens, capture, audio), not Layers. The strip above Devices / Layers / Log is live CPU, RAM, and GPU. Green is fine. Amber RAM means close browsers — it is not a sign this laptop cannot run 4K (Resolume on the same PC is the proof). WatchMe copies 4K frames in system RAM so that bar fills faster than Resolume. Red is actually full. Output going black is a WatchMe decoder bug, not Intel UHD being too weak.\n\n" +
                    "Stage and Output\n" +
                    "Cues bind to displays by position on the canvas, not a DisplayId field. Output is any extra OS screen (Win+P Extend). Pick the wall/TV, not the Producer laptop. Esc closes Output. A D3D11 compositor decodes each H.264 file once (Media Foundation / DXVA) and draws the wall from that texture. While Output is live, Stage stays a labeled canvas (Display 1 in the center) so the laptop does not run a second 4K decode — that is why Stage looks black. An amber preview box still follows the cue so you can move and resize it. Select the cue and press Fit cue (or Properties → Fit cue to display) so it fills Display 1 — a 1920 clip on a 3840×2160 Stage stays postage-stamp until you fit or type Width/Height. Close Output to preview video on Stage again. Output fills the HDMI/DP screen in real pixels. The Stage display stretch-fills that wall (1920×1080 Stage on a 1920×1080 TV is 1:1). If Log shows Output wall 1920×1080 · Stage display 3840×2160, Windows is not in 4K — Use size after setting the extra screen to 3840×2160 in Display settings. Snap on Stage uses a small magnet for drag/resize; dropping a clip onto a display still uses the larger magnet.\n\n" +
                    "Wall still black after closing or uninstalling WatchMe\n" +
                    "Output is a topmost window on that HDMI/DP screen. If DXVA hangs, Windows uninstall can remove files while WatchMe.exe is still covering the wall. Press Esc, or Task Manager → End task WatchMe. The next installer force-kills WatchMe.exe and re-applies Windows Extend. If Win+P Duplicate/Extend still shows a black HDMI wall, the GPU link is stuck — reboot once, then unplug HDMI for 10 seconds. Help → Reset HDMI / wall displays does the same restore.\n\n" +
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
                    "Color / GPU / HDR\n" +
                    "Show color space and 10-bit HDR are encode tags (Rec.709 / Rec.2020 / HLG / PQ). WatchMe’s WPF window is 8-bit. Pin WatchMe.exe to the discrete GPU in Windows Graphics settings.\n\n" +
                    "ST 2110 and NMOS\n" +
                    "Live → Import ST 2110 SDP, or set an IS-04 query registry and Refresh NMOS. Senders become ST 2110 assets. Essence still needs a 2110-capable NIC or ffmpeg RTP path on the machine.\n\n" +
                    "HAP, Notch LC, WAV, LTC\n" +
                    "Encode HAP from Assets. Notch LC transcodes to H.264 for DXVA. WAV files can declare up to 65535 channels; WatchMe downmixes wide files to stereo for playback. Export LTC WAV from the timeline playhead; ChaseLtc follows SMPTE time.\n\n" +
                    "Access and playback nodes\n" +
                    "Preferences PIN locks launch. Ping the Runner/WATCHPAX-style node at /watchme/health. Wake-on-LAN uses the MAC on that node.",
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
