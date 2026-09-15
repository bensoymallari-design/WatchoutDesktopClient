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
        Width = 680;
        Height = 720;
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
                    "WATCHOUT 7.8 Producer tools that run locally in WatchMe.\n\n" +
                    "Already in the first 7.8 slice:\n" +
                    "Linear wipe — angle, feather, completion. Temperature and Exposure overlays. Chroma key on stills with Stage eyedropper. Playback speed. Placeholder cues. Replace media (keep old / new size / fit). Display image mask. Pixel-perfect placement. Auto-start.\n\n" +
                    "Added in this update:\n" +
                    "Blind edit — Output holds a snapshot while you change Producer. Ctrl+T Take to Output.\n" +
                    "WATCHOUT 6 import — File → Import. JSON, XML, or a zip with JSON inside. Binary Dataton .watch is reported, not decoded.\n" +
                    "Key / Fill — each Display can be Fill or Key 1–4 (label + routing). Video is never OpacityMasked (that blacks DXVA).\n" +
                    "Group / Ungroup — selected cues become a composition asset; Ungroup explodes children.\n" +
                    "Wake on LAN — MAC on the Runner node, Output → Wake Display Node.\n" +
                    "GPU preference — saved in Preferences. Bind WatchMe.exe in Windows Graphics settings; WPF cannot switch GPUs by itself.\n" +
                    "Create H.264 version / re-optimize — ffmpeg transcode, stored as an asset revision you can switch.\n" +
                    "Color space tag — Rec.709 / Rec.2020 / HLG / PQ metadata. WPF output is still 8-bit.\n" +
                    "Watch folder — drop files into a folder and WatchMe imports them.\n" +
                    "Help → User Guide — honest map of WatchMe vs Dataton-only hardware.\n\n" +
                    "Not copied (Dataton media-server stack): ST 2110, NMOS, 10-bit SDI/HDR GPU pipeline, HAP encode suite, Notch LC, WATCHPAX, Access Control, MainConcept optimizer, 65535-ch WAV, LTC hardware. WatchMe stays native Media Foundation / DXVA H.264 on extra Windows screens (Colorlight, NovaStar, TVs, projectors).",
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
