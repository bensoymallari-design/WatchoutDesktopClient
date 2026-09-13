# WatchMe (native Windows)

WatchMe is a **.NET 8 WPF** show composer: Stage, Timeline, Assets, Runner outputs, and **live HDMI/SDI capture**. It replaces [WatchOutElctron](https://github.com/bensoymallari-design/WatchOutElctron). Video is **H.264 through Windows Media Foundation + DXVA**, not a VP9/WebM proxy inside Chromium.

Electron could not hardware-decode H.264 the way a show PC needs, so the old app transcoded every clip to WebM. This client does not.

## Why not Electron

| WatchOutElctron | WatchMe |
| --- | --- |
| Chromium `<video>` | Windows Media Foundation |
| Forced VP9+Opus `.webm` proxy | Play the H.264 `.mp4` / `.mov` as-is |
| Software VP9 on laptops | DXVA / D3D11 hardware decode |
| HAP / ProRes / DXV → WebM | HAP / Resolume DXV / ProRes → optional **H.264 MP4** (NVENC, AMF, QSV, or libx264) |
| Frameless `BrowserWindow` outputs | Frameless WPF windows on each monitor |
| No capture-card ingest | HDMI/SDI capture (Elgato, Blackmagic, Magewell, USB, NDI Webcam Input) |

## Codecs

**Play natively (DXVA):** H.264/AVC, H.265/HEVC, MPEG-2, WMV, JPEG/PNG stills, WAV, AAC, MP3.

**Live capture:** HDMI/SDI from a capture card. Send Resolume Program Out (or any mixer) into the card, then **Live → Connect Capture Card** (or Devices → Connect). The same session is shared on Stage and Runner outputs.

**Optional H.264 transcode (never WebM):** HAP, Resolume DXV, ProRes, DNx, CineForm. Needs [ffmpeg](https://ffmpeg.org/) on PATH. The encoder is `h264_nvenc` (NVIDIA), `h264_amf` (AMD), `h264_qsv` (Intel), or `libx264`.

Huge masters (over 2 GB) are **linked**, not copied. 100 GB 4K files stream from disk.

Opening an old Electron `.watch.json` prefers the original H.264 file over a leftover `.webm` sidecar.

## Resolume → WatchMe

1. In Resolume, set **Output → Advanced Output** (or Preview/Program) to the HDMI/SDI output that feeds your capture card.
2. Plug that cable into Elgato / Blackmagic / Magewell / a USB HDMI capture dongle on the WatchMe PC.
3. Open WatchMe → **Live → Refresh Capture Cards** (or the Devices tab).
4. **Connect** the card. A live layer appears on Stage and the Timeline. Press **Space** and output to LED as usual.

Alternatively: enable **NDI** in Resolume, run **NDI Webcam Input**, and connect that virtual camera from the same Devices list.

## Run on Windows

Install the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or the SDK if you build from source). Windows 10 1809+ is required for capture cards.

```powershell
dotnet restore
dotnet test tests/Watchout.Core.Tests/Watchout.Core.Tests.csproj
dotnet run --project src/Watchout.Desktop/Watchout.Desktop.csproj -c Release
```

Publish a folder you can copy to a show PC:

```powershell
dotnet publish src/Watchout.Desktop/Watchout.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish
```

`publish\WatchMe.exe` is the app.

Optional: `choco install ffmpeg` if you will import HAP / DXV / ProRes.

## Workflow

1. **New Show** or **Demo Show** (3-wide LED wall).
2. **Assets → Import** an H.264 MP4. It plays immediately — no amber “building WebM” wait.
3. Drag the clip on **Stage** (or double-click the asset). **Stage → Fit to Wall** to span every controller.
4. **Devices → Map extra monitors to Stage**, then **Output all displays**. Win+P → Extend.
5. Click the Stage, press **Space**. Esc stops (or closes outputs).
6. For Resolume: **Devices → Capture cards → Connect**.

Shows save as `.watchme.json`. Old `.watch.json` files still open.

Settings live in `%AppData%\WatchMe`.

## Keyboard

| Key | Action |
| --- | --- |
| Space | Play / pause |
| Esc | Stop / close outputs |
| Ctrl+O / Ctrl+S | Open / Save |
| Delete | Delete selection |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Mouse wheel on Stage | Zoom |
| Right-drag on Stage | Pan |
| Click the timeline ruler | Seek |

## Project layout

- `src/Watchout.Core` — show model, timeline, stage math, H.264/DXVA media policy (tested on any OS)
- `src/Watchout.Desktop` — WPF Producer + fullscreen Runner outputs + capture hub (Windows)
- `tests/Watchout.Core.Tests` — xUnit

WatchMe version **1.0.0**.
