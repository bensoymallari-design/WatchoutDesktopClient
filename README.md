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

**Live capture:** HDMI/SDI from one or **many** capture cards at the same time. Send Resolume Program Out (or any mixer) into each card, then **Live → Connect All Capture Cards**. Each card gets its own Stage display and timeline layer. The same sessions are shared on Runner outputs.

**Optional H.264 transcode (never WebM):** HAP, Resolume DXV, ProRes, DNx, CineForm. Needs [ffmpeg](https://ffmpeg.org/) on PATH. The encoder is `h264_nvenc` (NVIDIA), `h264_amf` (AMD), `h264_qsv` (Intel), or `libx264`.

Huge masters (over 2 GB) are **linked**, not copied. 100 GB 4K files stream from disk.

Opening an old Electron `.watch.json` prefers the original H.264 file over a leftover `.webm` sidecar.

## Resolume → WatchMe

1. In Resolume, set **Output → Advanced Output** (or Preview/Program) to the HDMI/SDI output that feeds each capture card. You can run several Resolume outputs into several cards.
2. Plug those cables into Elgato / Blackmagic / Magewell / USB HDMI dongles on the WatchMe PC.
3. Open WatchMe → **Live → Connect All Capture Cards** (or pick several in **Connect Capture Cards…**).
4. Each card becomes a live layer on its own Stage display. Map extra HDMI monitors in Devices, then **Output all displays**.

USB bandwidth (not WatchMe) is what usually caps how many Elgato-style dongles you can run; DeckLink / Magewell PCIe cards scale further. WatchMe opens one Media Foundation session per device and drops frames if the UI thread is busy, so 5×1080p is expected to work on a show PC.

Alternatively: enable **NDI** in Resolume, run **NDI Webcam Input**, and connect that virtual camera from the same Devices list.

## Install on your Windows laptop (to build)

WatchMe is a **Windows-only** WPF app. You cannot build the desktop EXE on a Mac.

**Required**

1. **Windows 10 (1809 / version 17763 or later)** or **Windows 11**, 64-bit.
2. **Git** — [https://git-scm.com/download/win](https://git-scm.com/download/win)
3. **.NET 8 SDK** (the SDK, not only the Runtime) — [https://dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0)  
   Pick **SDK 8.0.x / Windows x64**. This includes WPF so you can compile `WatchMe.exe`.  
   Or install **Visual Studio 2022** (17.8 or later) with the **.NET desktop development** workload — that also installs the SDK.

In PowerShell, confirm:

```powershell
dotnet --version
```

You should see `8.0.…`. If `dotnet` is not found, close and reopen the terminal after installing.

**Optional**

- **ffmpeg** on PATH — only if you import HAP, Resolume DXV, or ProRes. H.264 MP4 plays without it.  
  `winget install Gyan.FFmpeg` or [https://ffmpeg.org/download.html](https://ffmpeg.org/download.html)
- Capture-card **drivers** (Elgato, Blackmagic Desktop Video, Magewell) if you will ingest Resolume over HDMI/SDI.

**Get the code and run**

The WatchMe + capture-card work lives on branch `cursor/native-dotnet-h264-dxva-0b08` (merged PR #1 was an earlier WPF snapshot).

```powershell
git clone https://github.com/bensoymallari-design/WatchoutDesktopClient.git
cd WatchoutDesktopClient
git checkout cursor/native-dotnet-h264-dxva-0b08
dotnet restore
dotnet test tests/Watchout.Core.Tests/Watchout.Core.Tests.csproj
dotnet run --project src/Watchout.Desktop/Watchout.Desktop.csproj -c Release
```

Publish a folder you can copy to a show PC (no extra .NET install needed on that PC):

```powershell
dotnet publish src/Watchout.Desktop/Watchout.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish
```

`publish\WatchMe.exe` is the app.

Visual Studio: open `Watchout.sln`, set **Watchout.Desktop** as startup project, press F5.

## Workflow

1. **New Show** or **Demo Show** (3-wide LED wall).
2. **Assets → Import** an H.264 MP4. It plays immediately — no amber “building WebM” wait.
3. Drag the clip on **Stage** (or double-click the asset). **Stage → Fit to Wall** to span every controller.
4. **Devices → Map extra monitors to Stage**, then **Output all displays**. Win+P → Extend.
5. Click the Stage, press **Space**. Esc stops (or closes outputs).
6. For Resolume: **Live → Connect All Capture Cards**. Five cards → five live displays.

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
