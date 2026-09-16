# WatchMe (native Windows)

WatchMe is a **.NET 8 WPF** show composer: Stage, Timeline, Assets, Runner outputs, **NDI**, and **live HDMI/SDI capture**. It replaces [WatchOutElctron](https://github.com/bensoymallari-design/WatchOutElctron). Video is **H.264 through Windows Media Foundation + DXVA**, not a VP9/WebM proxy inside Chromium.

Build and install from **`main`**. You do not need an old `cursor/…` branch to get the installer.

## Why not Electron

| WatchOutElctron | WatchMe |
| --- | --- |
| Chromium `<video>` | Windows Media Foundation |
| Forced VP9+Opus `.webm` proxy | Play the H.264 `.mp4` / `.mov` as-is |
| Software VP9 on laptops | DXVA / D3D11 hardware decode |
| HAP / ProRes / DXV → WebM | HAP / Resolume DXV / ProRes → optional **H.264 MP4** (NVENC, AMF, QSV, or libx264) |
| Frameless `BrowserWindow` outputs | Frameless WPF windows on each monitor |
| No capture-card ingest | HDMI/SDI capture (Elgato, Blackmagic, Magewell, USB) plus **NDI** |

## Codecs

**Play natively (DXVA):** H.264/AVC, H.265/HEVC, MPEG-2, WMV, JPEG/PNG stills, WAV, AAC, MP3.

**Live NDI:** Resolume (or any NDI sender) at the source size — including **3840×2160**. Put the NDI cue on a **later timeline layer** than the H.264 clip so it sits in front on Stage and Output. Later layer = front.

**Live capture:** HDMI/SDI from one or **many** cards at the same time (Elgato, Blackmagic, Magewell, USB, NDI Webcam Input). **Live → Connect All Capture Cards**. Each card gets a Stage display. The same session is shared on Runner outputs. A camera in front of video works if that cue is on a later layer than the file.

**Optional H.264 transcode (never WebM):** HAP, Resolume DXV, ProRes, DNx, CineForm. Needs [ffmpeg](https://ffmpeg.org/) on PATH. The encoder is `h264_nvenc` (NVIDIA), `h264_amf` (AMD), `h264_qsv` (Intel), or `libx264`.

Imported clips are **linked**, not copied into AppData. 4K files stream from the original disk path.

Launch shows a Resolume-style splash that **initializes the engines** before Welcome: framework (folders/settings), application controller (60 Hz clock), WASAPI audio, D3D11/DXVA video, displays, NDI Runtime, capture cards, ffmpeg. Those are real startups, not labels. Help → About lists which ones came up.

Opening an old Electron `.watch.json` prefers the original H.264 file over a leftover `.webm` sidecar.

Loop a file with **Loop** checked. The decoder restarts when the clip hits the end so Stage and the wall do not go black.

## Resolume → WatchMe

**NDI (typical)**

1. Install [NDI Runtime / NDI Tools](https://ndi.video/tools/) on the WatchMe PC.
2. In Resolume, enable NDI output at the composition size (for example 3840×2160, High / full NDI).
3. WatchMe → **Assets → NDI** (or **Live**) → pick the Resolume source. Drag it onto a timeline layer **in front of** the video.
4. **Devices** → assign the wall screen (not the laptop) → **Output**.
5. Check the log for `NDI … 3840×2160 60p`. If it says 1920×1080, Resolume is sending HD.

**HDMI / SDI capture cards**

1. In Resolume, send Advanced Output (or Preview/Program) to the HDMI/SDI that feeds each card.
2. Plug those cables into Elgato / Blackmagic / Magewell / USB capture on the WatchMe PC.
3. WatchMe → **Live → Connect All Capture Cards** (or **Connect Capture Cards…**).
4. Map extra HDMI monitors in Devices, then **Output all displays**.

USB bandwidth (not WatchMe) usually caps how many Elgato-style dongles you can run; DeckLink / Magewell PCIe cards scale further.

4K NDI is copied on the CPU onto Stage and Output. It will not feel as fluid as Resolume’s GPU output. A stronger PC drops fewer frames; it does not match Arena.

## Install WatchMe on any Windows PC (`WatchMe-Setup.exe`)

Do this on a **Windows** machine. You cannot build the desktop EXE on a Mac.

**Build PC (once)**

```powershell
winget install Git.Git
winget install Microsoft.DotNet.SDK.8
winget install JRSoftware.InnoSetup
```

Close PowerShell, open a new window, then:

```powershell
dotnet --version
```

You should see `8.0.…`.

**Get `main` and build the installer**

```powershell
git clone https://github.com/bensoymallari-design/WatchoutDesktopClient.git
cd WatchoutDesktopClient
git checkout main
git pull
powershell -ExecutionPolicy Bypass -File setup\build.ps1
```

That writes **`dist\WatchMe-Setup.exe`**. Copy that one file to a USB stick or the show PC and double-click it. No extra .NET install is required on the show PC — the setup bundles the runtime. Optionally tick **Create a desktop shortcut**.

If you already cloned the repo:

```powershell
cd path\to\WatchoutDesktopClient
git checkout main
git pull
powershell -ExecutionPolicy Bypass -File setup\build.ps1
```

**Show PC extras (not inside the installer)**

| Need | For |
| --- | --- |
| Nothing extra | H.264 videos, Stage, Output |
| [NDI Runtime](https://ndi.video/tools/) | Resolume / NDI live |
| Capture-card drivers | HDMI/SDI cards |
| ffmpeg (`winget install Gyan.FFmpeg`) | HAP / DXV / ProRes import only |

Minimum OS: **Windows 10 1809** or Windows 11, 64-bit.

GitHub Actions also uploads a **WatchMe-Setup** artifact on each Windows build of `main`.

## Wall still black after closing or uninstalling WatchMe

Output is a **topmost black window** on that HDMI/DP screen. Uninstalling used to delete files while `WatchMe.exe` was still covering the wall. Win+P Duplicate/Extend **cannot** fix a stuck GPU HDMI mode.

1. Esc (closes Output), or Task Manager → **End task WatchMe**.
2. **Reboot the PC once** if the wall is still black with no WatchMe.exe. Then unplug HDMI for 10 seconds and plug it back.
3. Run the new `WatchMe-Setup.exe` — setup kills WatchMe and re-applies Windows Extend. In the app: **Help → Reset HDMI / wall displays**.

Do not pick the Producer laptop as the Output screen.

## Portable folder (no installer)

```powershell
git checkout main
dotnet publish src\Watchout.Desktop\Watchout.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish
```

Copy the **entire** `publish` folder. Run `WatchMe.exe` inside it. Do not copy only the exe.

## Run from source

```powershell
git checkout main
dotnet restore
dotnet test tests/Watchout.Core.Tests/Watchout.Core.Tests.csproj
dotnet run --project src/Watchout.Desktop/Watchout.Desktop.csproj -c Release
```

Visual Studio: open `Watchout.sln`, set **Watchout.Desktop** as startup project, press F5.

## Workflow

1. **New Show** or **Demo Show** (3-wide LED wall).
2. **Assets → Import** an H.264 MP4, or drag files onto Assets. Then **drag the asset onto a Stage display** (or onto the Timeline). Press **Space** or **Play**.
3. Drag the clip on **Stage** to snap it; drag the amber handles to resize. **Fit wall** / **Fit display** / **Fit to media**.
4. **Devices → Find screens** / **Assign screens**. Pick the LED wall, TV, or processor (Colorlight, NovaStar, any extra HDMI) — not the Producer laptop. Then **Output**.
5. Click the Stage, press **Space**. Esc stops (or closes outputs).
6. For Resolume NDI: **Assets → NDI**. For cards: **Live → Connect All Capture Cards**.

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
- `src/Watchout.Desktop` — WPF Producer + fullscreen Runner outputs + NDI + capture hub (Windows)
- `setup/` — Inno Setup script and `build.ps1` for `WatchMe-Setup.exe`
- `tests/Watchout.Core.Tests` — xUnit

WatchMe version **1.0.0**.
