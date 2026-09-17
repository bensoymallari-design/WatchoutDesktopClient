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
                    "Startup splash — Resolume-style boot: Initializing framework, application controller, audio engine, video engine, display, NDI, capture, codec. Those engines actually start before Welcome opens (WASAPI, D3D11 compositor, screens, NDI Runtime, capture cards, ffmpeg).\n" +
                    "Black screen while PLAY — Stage and Output no longer drop the shared H.264 decoder when one Present is empty. Loop wrap snaps back onto the clip, the last frame is held, and a stalled DXVA decoder is rebuilt instead of leaving a black wall.\n" +
                    "Output stuck after another clip — the wall no longer clears to black while DXVA opens the new file. Play rebuilds a dead decoder, Output keeps the last frame until the new picture is ready, and a failed DXGI flip retries so the stack can play again.\n" +
                    "Output still black — the wall no longer presents into a 64×64 nested HWND (DXGI FlipDiscard cannot target a child window, which tore the decoder down). Output fills the real screen pixels, falls back to a blt swap on the child HWND, and retries RGB32 without hardware transforms when Intel UHD rejects DXVA. Log shows the HWND size and any decoder fallback. Prefer H.264 MP4; some HEVC / Dolby Vision files still will not decode.\n" +
                    "Output Play was still black — WPF’s Output window sat on top of the DXGI child. Play now draws on a top-level wall HWND (FlipDiscard, above WPF), maps the cue into those screen pixels (not the 64×64 host), and keeps presenting while Output is open so the first frame can land. Log says “Output wall HWND …” then “Output has picture on the wall.”\n" +
                    "Stage / Output size — Output no longer shrinks after a DPI layout pass (that left a 2560 window on a 4K TV). The wall is the OS screen pixels; the Stage display stretch-fills that wall so 1920×1080 on a 1920×1080 screen is 1:1 again. Log: Output wall 3840×2160 · Stage display 3840×2160.\n" +
                    "Stage / cue resize — Fit-to-display clips no longer snap back to the wall (the drop magnet was 200+ px at overview zoom). Drag and resize use a ~12 px snap. While Output is live, Stage keeps an amber preview box so you can still grab and resize the cue without a second 4K decode.\n" +
                    "Colorlight X20 / LED maps — 6720×1344 was leftover NVIDIA from another controller, not this X20 (516×430). WatchMe no longer guesses that list. Set X20 HDMI to 516×430. Stage stays 1920×1080 (or any canvas you built); Output contain-fits it onto that wall, a TV, or any processor.\n" +
                    "Devices tab — Producer opens on Devices (screens, capture, audio) in the lower-right, not Layers. Layers and Log are still there.\n" +
                    "CPU / GPU / memory — left-to-right CPU · RAM · GPU bars. Green = room for more. Amber RAM means close browsers — it does not mean this laptop cannot run 4K (Resolume on the same PC is the proof). Amber GPU/CPU means another 4K may hitch. Red = full. WatchMe copies 4K frames in RAM so the RAM bar fills faster than Resolume.\n" +
                    "Stage canvas — while Output is live, Stage does not run a second 4K decode (that froze laptops). Each display shows its name in the center (Display 1) and “Output live — picture is on the wall.” Close Output to preview video on Stage again.\n" +
                    "GPU compositor — D3D11 + Media Foundation DXVA, one decode shared by Stage and Output (Resolume / WATCHOUT style). Resize, crop, wipe, chroma, and blend (Normal / Add / Multiply / Screen) run on the GPU so the wall does not hitch.\n" +
                    "Blind edit — Output holds a snapshot while you change Producer. Ctrl+T Take to Output.\n" +
                    "WATCHOUT 6 import — File → Import. JSON, XML, or a zip with JSON inside. Binary Dataton .watch is reported, not decoded.\n" +
                    "Key / Fill — each Display can be Fill or Key 1–4 (label + routing). Video is never OpacityMasked (that blacks DXVA).\n" +
                    "Group / Ungroup — selected cues become a composition asset; Ungroup explodes children.\n" +
                    "Wake on LAN — MAC on the Runner node, Output → Wake Display Node.\n" +
                    "GPU preference — saved in Preferences. Bind WatchMe.exe in Windows Graphics settings; WPF cannot switch GPUs by itself.\n" +
                    "Create H.264 version / re-optimize — ffmpeg transcode, stored as an asset revision you can switch.\n" +
                    "Color space tag — Rec.709 / Rec.2020 / HLG / PQ metadata. WPF output is still 8-bit.\n" +
                    "Watch folder — drop files into a folder and WatchMe imports them.\n" +
                    "ST 2110 — import an SDP (Live → Import ST 2110 SDP) or pull NMOS senders. Stage shows the live slot; a 2110 NIC + ffmpeg/registry still feed the essence.\n" +
                    "NMOS IS-04 — Preferences registry URL, then Live → Refresh NMOS.\n" +
                    "10-bit HDR — Preferences enables 10-bit / PQ encodes (high10). The WPF window is still 8-bit; files and tags are 10-bit.\n" +
                    "HAP encode — Assets → Encode HAP (ffmpeg hap).\n" +
                    "Notch LC — imports transcode to H.264 MP4 like HAP/DXV.\n" +
                    "Playback node — Runner / WATCHPAX-style node, ping HTTP /watchme/health, Wake-on-LAN.\n" +
                    "Access control — PIN + Producer/Operator/Viewer in Preferences.\n" +
                    "Optimize presets — Fast / Quality / Broadcast for H.264 versions.\n" +
                    "65535-ch WAV — header is read; >8 channels downmix to stereo for Media Foundation.\n" +
                    "LTC — generate a WAV from the playhead (Output → Export LTC) and chase SMPTE time.\n\n" +
                    "WatchMe is this app — those pipelines live here. A Dataton appliance is not required.",
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
