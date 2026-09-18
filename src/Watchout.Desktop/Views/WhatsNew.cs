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
                    "Stage column switches — above Devices / Layers / Log, each capture card and NDI source is a row of radio buttons. Click Display 1, Display 2, … to assign that input and auto-fit it to that Stage canvas.\n" +
                    "TV off / on delay — the cue keeps running on the laptop clock while HDMI is asleep. When the TV wakes, Output used to stay several seconds behind. WatchMe now re-places the wall and snaps the decoder to the playhead (again after 2 s for HDMI handshake). Log: “Output snapped to the playhead after the TV / display woke”.\n" +
                    "Stage preview CPU — ffmpeg was software-decoding 4K every 250 ms (99% CPU, laggy wall). Stage now grabs a 640px keyframe about every 2 s, uses one thread, and holds the last frame while CPU is Full. Output still owns DXVA.\n" +
                    "Black Stage cue — a second Media Foundation reader never showed picture (this clip’s first frame is black, and dual MF fought Output’s DXVA). Stage now grabs an ffmpeg JPEG at the playhead so PLAY at 22 s is the video, not an empty box. Output still owns the only DXVA HWND.\n" +
                    "Black Stage cue while PLAY — software preview sat on the black first frame (this clip fades in) and never jumped to the playhead at 22 s. It now seeks forward to the clock and keeps the reader if the first sample is slow. Output still owns the only DXVA HWND.\n" +
                    "Output black/stuck with Stage — two 4K MediaElements (Producer Stage + the wall) freeze Intel UHD DXVA, so both go black. Output keeps the only HWND decode. Stage grabs an ffmpeg still at the playhead. One shared decode when D3D11 is up. Log: “Stage software preview — ffmpeg frame at the playhead”.\n" +
                    "Stage picture — a live Output used to leave Producer Stage as a black 0.15s poster while the GPU compositor was off (Intel UHD). Pause at 7 s still showed that first-frame JPEG. Stage now shows a software preview of the same H.264 so the cue interior is the video, not a black box. Gold title bar still sits above the picture.\n" +
                    "Stage cue names — each clip on Producer Stage has a gold title bar with black text. DXVA MediaElement used to cover the name (HWND airspace) so a black cue looked unnamed; the video is inset so the bar stays readable. The wall itself stays unlabeled.\n" +
                    "Client Play — WatchMe stays up if DXGI/D3D11/Media Foundation throws (screenshot, GPU full, E_FAIL). While PLAY is running, a failed compositor is not retried every 2 s (that covered the wall and could crash). Output keeps one Media Foundation decode; Stage uses a software preview when the compositor is off so Producer is not a black box and the wall does not stall. HEVC/HDR files warn at import instead of going black on the client wall.\n" +
                    "Stable Play — import links the HandBrake 8-bit H.264 MP4 and plays it. No background HAP encode (that fought Play on 8 GB Intel UHD and aborted ffmpeg with Unrecognized option chunks). One DXVA decode when the D3D11 compositor is up. If it is off, Output keeps MediaElement and Stage is a software preview — not a second 4K DXVA. Log: “Play — one H.264 decode (DXVA)”. Assets → Encode HAP stays as an optional menu.\n" +
                    "HAP encode on this PC failed with Unrecognized option chunks — ffmpeg aborted before writing HAP Q. WatchMe no longer auto-encodes HAP, no longer passes -chunks, retries plain hap if you pick Encode HAP, and logs the real ffmpeg line (not the librubberband banner). D3D11CreateDevice uses the native feature-level count so Intel UHD is less likely to return E_INVALIDARG.\n" +
                    "Play with no image and no black/stuck Log — the D3D11 compositor failed at boot (Intel UHD E_INVALIDARG from an empty feature-level list). Present never ran, so the diagnostic never ran, and an empty top-level wall HWND covered the WPF picture. WatchMe now creates the device with feature levels 11.1–10.0, retries, logs “Output black — D3D11 compositor…”, drops that covering HWND, and keeps Stage video (MediaElement) plus a corner label instead of a gold canvas over the clip. Not RAM.\n" +
                    "A YouTube title like “Dolby Vision” is not HDR. yt-dlp WebM → HandBrake 8-bit H.264 MP4 is the file WatchMe plays (DXVA). Import does not convert it to HAP. Prefer Rec.709 H.264; HEVC HDR can still stay black — use Assets → Create H.264 version.\n" +
                    "RAM Tight is not a black Output — 6.8–7.0 / 7.7 GB sitting on the 88% line used to spam Tight / recovered in the Log so it looked like the laptop could not play 4K. RAM-only Tight stays on the meter (close browsers; one H.264 4K is still OK). The Log keeps GPU/CPU/full warnings, and Output black / stuck lines.\n" +
                    "Black / stuck Log — Output names the cause instead of staying silent: still opening the MP4, no DXVA picture, decoder stalled (frozen last frame), decode failed, missing file, playhead off the cue, holding last frame while the next MP4 opens, or NDI/capture with no pixels. Opening stays quiet for 0.5 s so a healthy Play is not a false alarm. Log: “Output black — …” or “Output stuck — …”.\n" +
                    "Delete-and-reload still black — that was the MP4 file, not NDI or capture. NV12 GPU open could succeed with no DXGI picture, so deleting the cue and loading the same MP4 again stayed black. WatchMe now rejects that open and falls back to RGB32 (then software). Log: “DXVA NV12 GPU path failed for this MP4 — using RGB32”. Dolby Vision / HEVC HDR may still fail — use an 8-bit H.264 MP4, or Assets → Create H.264 version.\n" +
                    "Output stack on Stage — while Output is live, Stage shows the same GPU layer stack as the wall (shared DXVA texture, no second 4K decode). Display 1 is a small corner label, not a black canvas covering the picture.\n" +
                    "GPU path — H.264 DXVA frames stay on the GPU (DXGI surfaces) and composite in D3D11. WatchMe no longer copies every file frame through RGB32 system RAM then uploads it again. That copy is why RAM filled and the wall hitch after a long PLAY while the RAM bar was still green. NDI and capture still upload from CPU (they arrive as pixels). Log: “DXVA GPU texture — … stays on the GPU”. Intel UHD can still fall back to RGB32 if hardware surfaces fail.\n" +
                    "Credit — window title is WatchMe by jhon juben mallari.\n" +
                    "Cue drag updates Properties — dragging or resizing a cue on Stage writes X/Y/Width/Height in the Properties panel (live, then again when you release). It used to keep the old numbers until you typed in the box.\n" +
                    "Stage size from Windows — picking a display copies the Windows resolution onto that Stage. NVIDIA custom (3160×2160) that you apply in Windows Display on the second monitor is caught as 3160×2160 Stage — not the controller EDID, and not a leftover NVIDIA mode. A 150% DPI 4K panel still uses the physical 3840 pixels. Output HWND matches those same pixels.\n" +
                    "Stage and cue restored — Colorlight/Resolume mapping (516×430 on a 1920 screen, leftover NVIDIA 6720) is reverted. Stage and cue work as they did when Output was already showing picture. Black Output fixes stay (top-level wall HWND, real screen pixels).\n" +
                    "Startup splash — boot names each engine: Initializing framework, application controller, audio engine, video engine, display, NDI, capture, codec. Those engines actually start before Welcome opens (WASAPI, D3D11 compositor, screens, NDI Runtime, capture cards, ffmpeg).\n" +
                    "Black screen while PLAY — Stage and Output no longer drop the shared H.264 decoder when one Present is empty. Loop wrap snaps back onto the clip, the last frame is held, and a stalled DXVA decoder is rebuilt instead of leaving a black wall.\n" +
                    "Output stuck after another clip — the wall no longer clears to black while DXVA opens the new file. Play rebuilds a dead decoder, Output keeps the last frame until the new picture is ready, and a failed DXGI flip retries so the stack can play again.\n" +
                    "Output still black — the wall no longer presents into a 64×64 nested HWND (DXGI FlipDiscard cannot target a child window, which tore the decoder down). Output fills the real screen pixels, falls back to a blt swap on the child HWND, and retries RGB32 without hardware transforms when Intel UHD rejects DXVA. Log shows the HWND size and any decoder fallback. Prefer H.264 MP4; some HEVC / Dolby Vision files still will not decode.\n" +
                    "Output Play was still black — WPF’s Output window sat on top of the DXGI child. Play now draws on a top-level wall HWND (FlipDiscard, above WPF), maps the cue into those screen pixels (not the 64×64 host), and keeps presenting while Output is open so the first frame can land. Log says “Output wall HWND …” then “Output has picture on the wall.”\n" +
                    "Stage / Output size — Output no longer shrinks after a DPI layout pass (that left a 2560 window on a 4K TV). The wall is the OS screen pixels; the Stage display stretch-fills that wall so 1920×1080 on a 1920×1080 screen is 1:1 again. Log: Output wall 3840×2160 · Stage display 3840×2160.\n" +
                    "Stage / cue resize — Fit-to-display clips no longer snap back to the wall (the drop magnet was 200+ px at overview zoom). Drag and resize use a ~12 px snap. While Output is live and the GPU compositor is up, Stage shares that decode; if the compositor is off, Output keeps DXVA and Stage is a software preview so both do not go black.\n" +
                    "Cue Width × Height — Properties shows pixel size on Stage (not only Scale %). A small cue looked “fit” on the TV because that box still fitted inside the Output window; Fit cue to 3840×2160 used to draw larger than the real HWND (DXGI cropped — zoomed). Output now presents at the actual swap/client pixels and contain-fits the Stage display into that wall. Log: Output wall 2560×1440 · Stage display 3840×2160 · cue …. Type Width/Height for a custom box. Dragging the cue moves the picture.\n" +
                    "Devices tab — Producer opens on Devices (screens, capture, audio) in the lower-right, not Layers. Layers and Log are still there. STAGE COLUMNS sits above those tabs: radio switches per capture card / NDI source, one Stage display column each, auto-fit to that canvas.\n" +
                    "CPU / GPU / memory — left-to-right CPU · RAM · GPU bars. Green = room for more. Amber RAM means close browsers — it does not mean this laptop cannot run one 4K H.264. Amber GPU/CPU means another 4K may hitch. Red = full. H.264 stays on the GPU; NDI/capture still copy pixels in RAM.\n" +
                    "Stage canvas — while Output is live, Stage draws the same GPU layer stack as the wall (one DXVA decode). Display 1 sits as a small corner label. Close Output and the MediaElement fallback is unused while the compositor is up.\n" +
                    "GPU compositor — D3D11 + Media Foundation DXVA, one decode shared by Stage and Output. Resize, crop, wipe, chroma, and blend (Normal / Add / Multiply / Screen) run on the GPU so the wall does not hitch.\n" +
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
