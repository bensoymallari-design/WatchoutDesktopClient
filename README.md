# WATCHOUT Producer (native Windows)

Desktop WATCHOUT 7 Producer rebuilt as a **.NET 8 WPF** app. This replaces [WatchOutElctron](https://github.com/bensoymallari-design/WatchOutElctron): same Stage / Timeline / Assets / Runner-output workflow, but video is **H.264 through Windows Media Foundation + DXVA**, not a VP9/WebM proxy inside Chromium.

Electron could not hardware-decode H.264 the way a show PC needs, so the old app transcoded every clip to WebM. This client does not.

## Why not Electron

| WatchOutElctron | This app |
| --- | --- |
| Chromium `<video>` | Windows Media Foundation |
| Forced VP9+Opus `.webm` proxy | Play the H.264 `.mp4` / `.mov` as-is |
| Software VP9 on laptops | DXVA / D3D11 hardware decode |
| HAP / ProRes / DXV → WebM | HAP / Resolume DXV / ProRes → optional **H.264 MP4** (NVENC, AMF, QSV, or libx264) |
| Frameless `BrowserWindow` outputs | Frameless WPF windows on each monitor |

## Codecs

**Play natively (DXVA):** H.264/AVC, H.265/HEVC, MPEG-2, WMV, JPEG/PNG stills, WAV, AAC, MP3.

**Optional H.264 transcode (never WebM):** HAP, Resolume DXV, ProRes, DNx, CineForm. Needs [ffmpeg](https://ffmpeg.org/) on PATH. The encoder is `h264_nvenc` (NVIDIA), `h264_amf` (AMD), `h264_qsv` (Intel), or `libx264`.

Huge masters (over 2 GB) are **linked**, not copied. 100 GB 4K files stream from disk.

Opening an old Electron `.watch.json` prefers the original H.264 file over a leftover `.webm` sidecar.

## Run on Windows

Install the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or the SDK if you build from source).

```powershell
dotnet restore
dotnet test tests/Watchout.Core.Tests/Watchout.Core.Tests.csproj
dotnet run --project src/Watchout.Desktop/Watchout.Desktop.csproj -c Release
```

Publish a folder you can copy to a show PC:

```powershell
dotnet publish src/Watchout.Desktop/Watchout.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish
```

`publish\WatchoutProducer.exe` is the app.

Optional: `choco install ffmpeg` if you will import HAP / DXV / ProRes.

## Workflow

1. **New Show** or **Demo Show** (3-wide LED wall).
2. **Assets → Import** an H.264 MP4. It plays immediately — no amber “building WebM” wait.
3. Drag the clip on **Stage** (or double-click the asset). **Stage → Fit to Wall** to span every controller.
4. **Devices → Map extra monitors to Stage**, then **Output all displays**. Win+P → Extend.
5. Click the Stage, press **Space**. Esc stops (or closes outputs).

Shows save as `.watch.json` (same document the Electron app used).

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
- `src/Watchout.Desktop` — WPF Producer + fullscreen Runner outputs (Windows)
- `tests/Watchout.Core.Tests` — xUnit

Producer version **7.8.12**.
