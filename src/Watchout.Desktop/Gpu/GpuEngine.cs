using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Watchout.Core.Gpu;
using Watchout.Core.Models;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Gpu;

/// <summary>
/// One D3D11 compositor for Stage and every Output. File decode (Media Foundation
/// + DXVA) happens once; NDI/capture frames upload into the same GPU scene.
/// Resize is a shader quad, not an EVR HWND rebuild.
/// </summary>
public static class GpuEngine
{
    static readonly object Gate = new();
    static GpuDevice? _gpu;
    static GpuCompositor? _comp;
    static readonly Dictionary<string, MfGpuDecoder> Files = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, StillCache> Stills = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<nint, OutputSwap> Swaps = [];
    static readonly Dictionary<string, int> Idle = new(StringComparer.OrdinalIgnoreCase);
    static bool _started;
    static bool _failed;
    static string? _error;
    static int _mfUsers;

    static readonly Dictionary<string, Action> LiveHolds = new(StringComparer.OrdinalIgnoreCase);

    public static bool Available { get { lock (Gate) return _started && !_failed && _gpu is not null; } }
    public static string? LastError { get { lock (Gate) return _error; } }

    public static string Describe()
    {
        lock (Gate)
        {
            if (_gpu is not null) return $"D3D11 {_gpu.Level} compositor · DXVA";
            return _error ?? "off";
        }
    }

    public static bool TryStart()
    {
        lock (Gate)
        {
            if (_started) return !_failed && _gpu is not null;
            _started = true;
            try
            {
                MfNative.Check(MfNative.MFStartup(MfNative.MfVersion, 0), "MFStartup");
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
                if (keepLastFrame && draws.Count == 0) return true;
                SyncSources(draws, playAudio);
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
        try
        {
            lock (Gate)
            {
                if (keepLastFrame && draws.Count == 0) return true;
                SyncSources(draws, playAudio);
                if (!Swaps.TryGetValue(hwnd, out var swap) || swap.Width != width || swap.Height != height)
                {
                    swap?.Dispose();
                    swap = OutputSwap.Create(_gpu, hwnd, width, height);
                    Swaps[hwnd] = swap;
                }
                _comp.Render(swap.Rtv, width, height, draws, display);
                PresentSwap(swap);
                return true;
            }
        }
        catch (Exception ex)
        {
            Recover(ex);
            return false;
        }
    }

    public static void DropOutput(nint hwnd)
    {
        lock (Gate)
        {
            if (Swaps.Remove(hwnd, out var swap)) swap.Dispose();
        }
    }

    static void SyncSources(IReadOnlyList<GpuDraw> draws, bool playAudio)
    {
        var live = draws.Select(d => d.SourceKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AgeIdle(live);

        foreach (var draw in draws)
        {
            switch (draw.Kind)
            {
                case GpuSourceKind.File:
                    var decoder = FileDecoder(draw, playAudio);
                    if (decoder is null) break;
                    decoder.Sync(draw.MediaTimeMs, draw.Playing, draw.Loop, draw.Volume, playAudio);
                    if (decoder.TryCopyFrame(out var pixels, out var w, out var h, out var stride, out var dirty)
                        && (dirty || _comp is null || !_comp.Has(draw.SourceKey)))
                        _comp!.UploadBgra(draw.SourceKey, pixels, w, h, stride);
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

    static MfGpuDecoder? FileDecoder(GpuDraw draw, bool playAudio)
    {
        if (Files.TryGetValue(draw.SourceKey, out var decoder)
            && (decoder.Dead || (draw.Playing && decoder.Stalled)))
        {
            if (Rebuilt.TryGetValue(draw.SourceKey, out var at)
                && (DateTime.UtcNow - at).TotalSeconds < 2)
                return decoder.Dead ? null : decoder;
            decoder.Dispose();
            Files.Remove(draw.SourceKey);
            _comp?.Drop(draw.SourceKey);
            Rebuilt[draw.SourceKey] = DateTime.UtcNow;
            decoder = null;
        }
        if (decoder is not null) return decoder;
        var path = draw.SourceKey.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? draw.SourceKey["file:".Length..]
            : draw.SourceKey;
        if (!File.Exists(path)) return null;
        decoder = new MfGpuDecoder(new Uri(Path.GetFullPath(path)).AbsoluteUri, playAudio);
        Files[draw.SourceKey] = decoder;
        Idle[draw.SourceKey] = 0;
        return decoder;
    }

    static void AgeIdle(HashSet<string> live)
    {
        foreach (var key in live) Idle[key] = 0;
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
        var w = bmp.PixelWidth;
        var h = bmp.PixelHeight;
        var stride = w * 4;
        var pixels = new byte[stride * h];
        bmp.CopyPixels(pixels, stride, 0);
        _comp!.UploadBgra(key, pixels, w, h, stride);
    }

    static void PresentSwap(OutputSwap swap)
    {
        try
        {
            swap.Chain.Present(1, PresentFlags.DoNotWait);
        }
        catch (SharpGenException ex) when (ex.HResult == unchecked((int)0x887A000A))
        {
            // DXGI_ERROR_WAS_STILL_DRAWING — keep the last frame instead of freezing Close/uninstall.
        }
    }

    static void Recover(Exception ex)
    {
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

    OutputSwap(IDXGISwapChain1 chain, ID3D11RenderTargetView rtv, int w, int h)
    {
        Chain = chain;
        Rtv = rtv;
        Width = w;
        Height = h;
    }

    public static OutputSwap Create(GpuDevice gpu, nint hwnd, int width, int height)
    {
        var desc = new SwapChainDescription1
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
        };
        var chain = gpu.Factory.CreateSwapChainForHwnd(gpu.Device, hwnd, desc);
        try
        {
            gpu.Factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter | WindowAssociationFlags.IgnoreAll);
        }
        catch { /* factory optional */ }
        using var back = chain.GetBuffer<ID3D11Texture2D>(0);
        var rtv = gpu.Device.CreateRenderTargetView(back);
        return new OutputSwap(chain, rtv, width, height);
    }

    public void Dispose()
    {
        try { Chain.SetFullscreenState(false); } catch { /* already windowed */ }
        Rtv.Dispose();
        Chain.Dispose();
    }
}
