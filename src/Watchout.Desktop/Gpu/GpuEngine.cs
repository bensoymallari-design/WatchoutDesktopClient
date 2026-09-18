using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Watchout.Core.Gpu;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Desktop.Interop;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Gpu;

/// <summary>
/// One D3D11 compositor for Stage and every Output. File decode (Media Foundation
/// + DXVA DXGI surfaces) stays on the GPU like Resolume; NDI/capture still upload
/// into the same scene. Resize is a shader quad, not an EVR HWND rebuild.
/// </summary>
public static class GpuEngine
{
    static readonly object Gate = new();
    static GpuDevice? _gpu;
    static GpuCompositor? _comp;
    static readonly Dictionary<string, IGpuFileDecoder> Files = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, StillCache> Stills = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<nint, OutputSwap> Swaps = [];
    static readonly Dictionary<string, int> Idle = new(StringComparer.OrdinalIgnoreCase);
    static bool _started;
    static bool _failed;
    static string? _error;
    static int _mfUsers;
    static DateTime _retryUtc;
    static bool _loggedStartFail;

    static readonly Dictionary<string, Action> LiveHolds = new(StringComparer.OrdinalIgnoreCase);

    public static bool Available { get { lock (Gate) return _started && !_failed && _gpu is not null; } }
    public static string? LastError { get { lock (Gate) return _error; } }

    public static string Describe()
    {
        lock (Gate)
        {
            if (_gpu is not null)
                return $"D3D11 {_gpu.Level} compositor · DXVA GPU";
            return _error ?? "off";
        }
    }

    public static (string Name, long Used, long Budget) VideoMemory()
    {
        lock (Gate)
        {
            if (_gpu is null) return ("", 0, 0);
            try { return _gpu.VideoMemory(); }
            catch { return ("", 0, 0); }
        }
    }

    public static bool TryStart()
    {
        lock (Gate)
        {
            if (_gpu is not null && !_failed) return true;
            if (_failed)
            {
                if ((DateTime.UtcNow - _retryUtc).TotalSeconds < 2) return false;
                _started = false;
                _failed = false;
            }
            if (_started) return !_failed && _gpu is not null;
            _started = true;
            var startedMf = false;
            try
            {
                MfNative.Check(MfNative.MFStartup(MfNative.MfVersion, 0), "MFStartup");
                startedMf = true;
                _mfUsers++;
                _gpu = GpuDevice.Create();
                _comp = new GpuCompositor(_gpu);
                _failed = false;
                _error = null;
                return true;
            }
            catch (Exception ex)
            {
                _failed = true;
                _error = ex.Message;
                _retryUtc = DateTime.UtcNow;
                _started = false;
                if (startedMf)
                {
                    try { MfNative.MFShutdown(); } catch { /* paired */ }
                    _mfUsers = Math.Max(0, _mfUsers - 1);
                }
                if (!_loggedStartFail)
                {
                    _loggedStartFail = true;
                    App.Session.Log(
                        "Output black — D3D11 compositor failed at boot"
                        + (string.IsNullOrWhiteSpace(ex.Message) ? "" : $" ({ex.Message})")
                        + ". Play never presented. Not RAM. WatchMe will retry and show the WPF window instead of an empty wall HWND.",
                        "error");
                }
                return false;
            }
        }
    }

    public static void Shutdown()
    {
        lock (Gate) TearDown(failed: false);
    }

    public static bool PresentStage(WriteableBitmap bmp, IReadOnlyList<GpuDraw> draws, bool playAudio, bool keepLastFrame = false)
    {
        if (!TryStart() || _comp is null) return false;
        try
        {
            lock (Gate)
            {
                var playing = keepLastFrame || draws.Any(d => d.Playing);
                if (GpuResidentPath.SkipStageCpuReadback(draws.Count == 0, keepLastFrame)) return true;
                SyncSources(draws, playAudio, playing);
                var ready = draws.Count(d => _comp.Has(d.SourceKey));
                if (!GpuSourceLifetime.ClearToBlack(ready) && keepLastFrame) return true;
                var frame = _comp.RenderStage(bmp.PixelWidth, bmp.PixelHeight, draws);
                bmp.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Width * 4, 0);
                return true;
            }
        }
        catch (Exception ex)
        {
            Recover(ex);
            return false;
        }
    }

    public static bool PresentOutput(nint hwnd, int width, int height, IReadOnlyList<GpuDraw> draws, Display? display, bool playAudio, bool keepLastFrame = false)
    {
        if (!TryStart() || _comp is null || _gpu is null || hwnd == 0) return false;
        width = Math.Max(2, width);
        height = Math.Max(2, height);
        var client = NativeWindow.ClientSize(hwnd);
        if (client.W >= OutputViewMath.MinHostPx && client.H >= OutputViewMath.MinHostPx)
        {
            width = Math.Min(width, client.W);
            height = Math.Min(height, client.H);
        }
        try
        {
            lock (Gate)
            {
                var playing = keepLastFrame || draws.Any(d => d.Playing);
                var child = NativeWindow.IsChild(hwnd);
                if (!Swaps.TryGetValue(hwnd, out var swap)
                    || swap.RequestedWidth != width || swap.RequestedHeight != height)
                {
                    swap?.Dispose();
                    swap = OutputSwap.Create(_gpu, hwnd, width, height, child);
                    Swaps[hwnd] = swap;
                    NoteSwap(hwnd, swap.Width, swap.Height, swap.Flip, child);
                }
                width = swap.Width;
                height = swap.Height;
                if (draws.Count > 0 || !GpuSourceLifetime.FreezeIdleWhilePlaying(playing, draws.Count))
                    SyncSources(draws, playAudio, playing);
                var ready = draws.Count(d => _comp.Has(d.SourceKey));
                NoteWall(draws, ready, playing, keepLastFrame);
                if (GpuSourceLifetime.ClearToBlack(ready))
                {
                    _comp.Render(swap.Rtv, width, height, draws, display);
                    NotePicture();
                }
                PresentSwap(swap);
                return true;
            }
        }
        catch (SharpGenException ex) when (OutputViewMath.TearGpuOnPresentError(ex.HResult))
        {
            Recover(ex);
            return false;
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                if (Swaps.Remove(hwnd, out var swap)) swap.Dispose();
            }
            NoteSwapRetry(ex.Message);
            return false;
        }
    }

    public static void KickForPlay(bool playing)
    {
        if (!playing) return;
        lock (Gate)
        {
            foreach (var key in Files.Keys.ToList())
            {
                var decoder = Files[key];
                if (!decoder.Dead && !decoder.Stalled) continue;
                if (decoder.Dead && decoder.Error is { Length: > 0 } err)
                    NoteDecode(key, err);
                DropKey(key);
                Rebuilt.Remove(key);
            }
        }
    }

    /// <summary>
    /// Play + Output can stay black with no decoder log if Present never runs
    /// (HWND 0 or compositor off). Call from the 60 Hz clock.
    /// </summary>
    public static void WatchPlay(bool playing, int liveOutputs)
    {
        lock (Gate)
        {
            if (_pictureLogged) return;
            var gpuOn = _gpu is not null && !_failed;
            var hwndOk = Swaps.Count > 0;
            var kind = OutputPictureCause.WhenPresentSkipped(playing, liveOutputs, gpuOn, hwndOk);
            if (kind == OutputPictureKind.Idle) return;
            NoteKind(kind, "", LastError);
        }
    }

    public static void DropOutput(nint hwnd)
    {
        lock (Gate)
        {
            if (Swaps.Remove(hwnd, out var swap)) swap.Dispose();
        }
    }

    static void SyncSources(IReadOnlyList<GpuDraw> draws, bool playAudio, bool playing)
    {
        var live = draws.Select(d => d.SourceKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AgeIdle(live, playing);

        foreach (var draw in draws)
        {
            switch (draw.Kind)
            {
                case GpuSourceKind.File:
                    var decoder = FileDecoder(draw, playAudio);
                    if (decoder is null) break;
                    if (decoder.UsedSoftwareFallback)
                        NoteSoftware(draw.SourceKey);
                    if (decoder.FellBackFromNv12)
                        NoteNv12Fallback(draw.SourceKey);
                    decoder.Sync(draw.MediaTimeMs, draw.Playing, draw.Loop, draw.Volume, playAudio);
                    var gpuReady = decoder.TryBindGpu(out var gpuTex, out var gpuDirty);
                    var cpuReady = decoder.TryCopyFrame(out var pixels, out var w, out var h, out var stride, out var cpuDirty);
                    switch (GpuResidentPath.Choose(gpuReady && gpuTex is not null, cpuReady))
                    {
                        case GpuFrameSource.DxgiTexture:
                            if (gpuDirty || _comp is null || !_comp.Has(draw.SourceKey))
                                _comp!.BindGpu(draw.SourceKey, gpuTex!, decoder.HapYCoCg);
                            if (decoder.UsedGpuSurfaces)
                                NoteGpuSurface(draw.SourceKey, decoder.HapYCoCg);
                            break;
                        case GpuFrameSource.CpuPixels:
                            if (cpuDirty || _comp is null || !_comp.Has(draw.SourceKey))
                                _comp!.UploadBgra(draw.SourceKey, pixels, w, h, stride);
                            break;
                    }
                    break;
                case GpuSourceKind.Still:
                    UploadStill(draw.SourceKey);
                    break;
                case GpuSourceKind.Ndi:
                    HoldLive(draw.SourceKey, true);
                    UploadLive(draw.SourceKey, NdiHub.Peek(draw.SourceKey["ndi:".Length..]));
                    break;
                case GpuSourceKind.Capture:
                    HoldLive(draw.SourceKey, false);
                    UploadLive(draw.SourceKey, CaptureHub.Peek(draw.SourceKey["capture:".Length..]));
                    break;
            }
        }
    }

    static readonly Dictionary<string, DateTime> Rebuilt = new(StringComparer.OrdinalIgnoreCase);

    static IGpuFileDecoder? FileDecoder(GpuDraw draw, bool playAudio)
    {
        if (Files.TryGetValue(draw.SourceKey, out var decoder)
            && (decoder.Dead || (draw.Playing && decoder.Stalled)))
        {
            if (decoder.Dead && decoder.Error is { Length: > 0 } err)
                NoteDecode(draw.SourceKey, err);
            if (Rebuilt.TryGetValue(draw.SourceKey, out var at)
                && (DateTime.UtcNow - at).TotalSeconds < 2)
                return decoder.Dead ? null : decoder;
            DropKey(draw.SourceKey);
            Rebuilt[draw.SourceKey] = DateTime.UtcNow;
            decoder = null;
        }
        if (decoder is not null) return decoder;
        var path = draw.SourceKey.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? draw.SourceKey["file:".Length..]
            : draw.SourceKey;
        if (!File.Exists(path))
        {
            NoteMissing(path);
            return null;
        }
        IGpuFileDecoder created = HapCodec.IsHap("", path)
            ? new HapGpuDecoder(path, playAudio, _gpu)
            : new MfGpuDecoder(new Uri(Path.GetFullPath(path)).AbsoluteUri, playAudio, _gpu);
        Files[draw.SourceKey] = created;
        Idle[draw.SourceKey] = 0;
        return created;
    }

    static OutputPictureKind _wallKind;
    static DateTime _wallKindUtc = DateTime.UtcNow;
    static bool _wallKindLogged;

    static void NoteWall(IReadOnlyList<GpuDraw> draws, int ready, bool playing, bool keepLastFrame)
    {
        var draw = draws.Count > 0 ? draws[0] : default;
        var key = draws.Count > 0 ? draw.SourceKey : "";
        var kind = draws.Count > 0 ? draw.Kind : GpuSourceKind.File;
        Files.TryGetValue(key, out var decoder);
        var missing = kind == GpuSourceKind.File
            && key.Length > 0
            && decoder is null
            && !File.Exists(FilePath(key));
        var liveHas = kind is GpuSourceKind.Ndi or GpuSourceKind.Capture && ready > 0;
        var hint = new OutputPictureHint(
            playing,
            draws.Count,
            ready,
            keepLastFrame,
            kind,
            decoder?.Opening == true,
            decoder?.Ready == true,
            decoder?.Dead == true,
            decoder?.Stalled == true,
            missing,
            liveHas,
            decoder?.Error);
        var cause = OutputPictureCause.Classify(hint);
        NoteKind(cause, key, decoder?.Error);
    }

    static void NoteKind(OutputPictureKind cause, string key, string? error)
    {
        if (cause != _wallKind)
        {
            _wallKind = cause;
            _wallKindUtc = DateTime.UtcNow;
            _wallKindLogged = false;
            if (cause != OutputPictureKind.Picture)
                _pictureLogged = false;
        }
        if (cause == OutputPictureKind.Picture)
        {
            NotePicture();
            _wallKindLogged = true;
            return;
        }
        var ms = (DateTime.UtcNow - _wallKindUtc).TotalMilliseconds;
        if (!OutputPictureCause.ShouldLog(cause, _wallKindLogged, ms)) return;
        var text = OutputPictureCause.Message(cause, key, error);
        if (text.Length == 0) return;
        _wallKindLogged = true;
        App.Session.Log(text, OutputPictureCause.Level(cause));
    }

    static string FilePath(string key) =>
        key.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? key["file:".Length..] : key;

    static readonly HashSet<string> MissingLogged = new(StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> DecodeLogged = new(StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> SoftwareLogged = new(StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> SwapLogged = new(StringComparer.OrdinalIgnoreCase);

    static bool _pictureLogged;

    static void NoteMissing(string path)
    {
        if (!MissingLogged.Add(path)) return;
        App.Session.Log($"Output has no file to play — {path}", "error");
    }

    static void NoteDecode(string key, string error)
    {
        if (!DecodeLogged.Add(key + error)) return;
        App.Session.Log($"DXVA could not play this MP4 ({key}): {error} — use 8-bit H.264, or Stop then Play", "error");
    }

    static readonly HashSet<string> GpuSurfaceLogged = new(StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> Nv12FallbackLogged = new(StringComparer.OrdinalIgnoreCase);

    static void NoteSoftware(string key)
    {
        if (!SoftwareLogged.Add(key)) return;
        App.Session.Log($"Playing {key} without DXVA hardware transforms — RGB32 still runs on this GPU");
    }

    static void NoteNv12Fallback(string key)
    {
        if (!Nv12FallbackLogged.Add(key)) return;
        App.Session.Log($"DXVA NV12 GPU path failed for this MP4 — using RGB32 so Output is not stuck black after delete-and-reload.");
    }

    static void NoteGpuSurface(string key, bool hapQ = false)
    {
        if (!GpuSurfaceLogged.Add(key)) return;
        App.Session.Log(hapQ || HapCodec.IsHap("", key)
            ? $"HAP GPU texture — {key} stays on the GPU (Resolume Alley / HAP Q, DXT, no RGB32 RAM copy)"
            : $"DXVA GPU texture — {key} stays on the GPU (Resolume path, no RGB32 RAM copy)");
    }

    static void NoteSwap(nint hwnd, int w, int h, bool flip, bool child)
    {
        var key = $"{hwnd}:{w}x{h}:{flip}:{child}";
        if (!SwapLogged.Add(key)) return;
        App.Session.Log($"Output swap {w}×{h} {(flip ? "flip" : "blt")} {(child ? "child HWND" : "window")}");
    }

    static DateTime _swapRetryUtc;

    static void NoteSwapRetry(string message)
    {
        if ((DateTime.UtcNow - _swapRetryUtc).TotalSeconds < 2) return;
        _swapRetryUtc = DateTime.UtcNow;
        App.Session.Log($"Output swap retry after {message}", "warn");
    }

    static void NotePicture()
    {
        if (_pictureLogged) return;
        _pictureLogged = true;
        App.Session.Log("Output has picture on the wall");
    }

    static void AgeIdle(HashSet<string> live, bool playing)
    {
        foreach (var key in live) Idle[key] = 0;
        if (GpuSourceLifetime.FreezeIdleWhilePlaying(playing, live.Count)) return;
        foreach (var key in Idle.Keys.ToList())
        {
            if (live.Contains(key)) continue;
            Idle[key]++;
            if (!GpuSourceLifetime.ReleaseAfterIdle(Idle[key])) continue;
            DropKey(key);
            Idle.Remove(key);
        }
    }

    static void DropKey(string key)
    {
        GpuSurfaceLogged.Remove(key);
        Nv12FallbackLogged.Remove(key);
        SoftwareLogged.Remove(key);
        if (Files.Remove(key, out var decoder)) decoder.Dispose();
        Stills.Remove(key);
        if (LiveHolds.Remove(key, out var fn))
        {
            if (key.StartsWith("ndi:", StringComparison.OrdinalIgnoreCase))
                NdiHub.Release(key["ndi:".Length..], fn);
            else if (key.StartsWith("capture:", StringComparison.OrdinalIgnoreCase))
                CaptureHub.Release(key["capture:".Length..], fn);
        }
        _comp?.Drop(key);
    }

    static void HoldLive(string key, bool ndi)
    {
        if (LiveHolds.ContainsKey(key)) return;
        Action tick = static () => { };
        LiveHolds[key] = tick;
        if (ndi) NdiHub.Retain(key["ndi:".Length..], tick);
        else CaptureHub.Retain(key["capture:".Length..], tick);
    }

    static void UploadStill(string key)
    {
        if (Stills.ContainsKey(key)) return;
        var path = key.StartsWith("still:", StringComparison.OrdinalIgnoreCase) ? key["still:".Length..] : key;
        if (!File.Exists(path)) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = MediaLibrary.LocalUri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            var w = conv.PixelWidth;
            var h = conv.PixelHeight;
            var stride = w * 4;
            var pixels = new byte[stride * h];
            conv.CopyPixels(pixels, stride, 0);
            _comp!.UploadBgra(key, pixels, w, h, stride);
            Stills[key] = new StillCache(w, h);
        }
        catch { /* still missing */ }
    }

    static void UploadLive(string key, WriteableBitmap? bmp)
    {
        if (bmp is null) return;
        bmp.Lock();
        try
        {
            _comp!.UploadBgra(key, bmp.BackBuffer, bmp.PixelWidth, bmp.PixelHeight, bmp.BackBufferStride);
        }
        finally
        {
            bmp.Unlock();
        }
    }

    static void PresentSwap(OutputSwap swap)
    {
        try
        {
            swap.Chain.Present(1, PresentFlags.None);
        }
        catch (SharpGenException ex) when (ex.HResult == unchecked((int)0x887A000A))
        {
            try { swap.Chain.Present(0, PresentFlags.None); }
            catch { /* keep last flipped frame */ }
        }
    }

    static void Recover(Exception ex)
    {
        App.Session.Log($"Output GPU recovered after {ex.Message} — Play again if the wall stays black", "warn");
        lock (Gate)
        {
            _error = ex.Message;
            TearDown(failed: false);
        }
        TryStart();
    }

    static void TearDown(bool failed)
    {
        foreach (var d in Files.Values) d.Dispose();
        Files.Clear();
        Stills.Clear();
        Idle.Clear();
        Rebuilt.Clear();
        SoftwareLogged.Clear();
        Nv12FallbackLogged.Clear();
        GpuSurfaceLogged.Clear();
        SwapLogged.Clear();
        _pictureLogged = false;
        _wallKind = OutputPictureKind.Idle;
        _wallKindLogged = false;
        foreach (var hold in LiveHolds)
        {
            if (hold.Key.StartsWith("ndi:", StringComparison.OrdinalIgnoreCase))
                NdiHub.Release(hold.Key["ndi:".Length..], hold.Value);
            else if (hold.Key.StartsWith("capture:", StringComparison.OrdinalIgnoreCase))
                CaptureHub.Release(hold.Key["capture:".Length..], hold.Value);
        }
        LiveHolds.Clear();
        foreach (var s in Swaps.Values) s.Dispose();
        Swaps.Clear();
        _comp?.Dispose();
        _gpu?.Dispose();
        _comp = null;
        _gpu = null;
        if (_mfUsers > 0)
        {
            MfNative.MFShutdown();
            _mfUsers = 0;
        }
        _started = false;
        _failed = failed;
    }

    readonly record struct StillCache(int W, int H);
}

sealed class OutputSwap : IDisposable
{
    public IDXGISwapChain1 Chain { get; }
    public ID3D11RenderTargetView Rtv { get; }
    public int Width { get; }
    public int Height { get; }
    public int RequestedWidth { get; }
    public int RequestedHeight { get; }
    public bool Flip { get; }

    OutputSwap(IDXGISwapChain1 chain, ID3D11RenderTargetView rtv, int w, int h, int requestedW, int requestedH, bool flip)
    {
        Chain = chain;
        Rtv = rtv;
        Width = w;
        Height = h;
        RequestedWidth = requestedW;
        RequestedHeight = requestedH;
        Flip = flip;
    }

    public static OutputSwap Create(GpuDevice gpu, nint hwnd, int width, int height, bool child)
    {
        var wantFlip = OutputViewMath.FlipModelAllowed(child);
        try
        {
            return CreateCore(gpu, hwnd, width, height, wantFlip);
        }
        catch when (wantFlip)
        {
            return CreateCore(gpu, hwnd, width, height, flip: false);
        }
    }

    static OutputSwap CreateCore(GpuDevice gpu, nint hwnd, int width, int height, bool flip)
    {
        width = Math.Max(2, width);
        height = Math.Max(2, height);
        var desc = new SwapChainDescription1
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = flip ? 2u : 1u,
            Scaling = Scaling.Stretch,
            SwapEffect = flip ? SwapEffect.FlipDiscard : SwapEffect.Discard,
            AlphaMode = AlphaMode.Ignore,
        };
        var chain = gpu.Factory.CreateSwapChainForHwnd(gpu.Device, hwnd, desc);
        try
        {
            gpu.Factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter | WindowAssociationFlags.IgnoreAll);
        }
        catch { /* factory optional */ }
        using var back = chain.GetBuffer<ID3D11Texture2D>(0);
        var actual = OutputViewMath.SwapPixels(width, height, (int)back.Description.Width, (int)back.Description.Height);
        var rtv = gpu.Device.CreateRenderTargetView(back);
        gpu.Context.ClearRenderTargetView(rtv, new Vortice.Mathematics.Color4(0, 0, 0, 1));
        return new OutputSwap(chain, rtv, actual.W, actual.H, width, height, flip);
    }

    public void Dispose()
    {
        try { Chain.SetFullscreenState(false); } catch { /* already windowed */ }
        Rtv.Dispose();
        Chain.Dispose();
    }
}
