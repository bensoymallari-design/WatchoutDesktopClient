using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            if (_started) return !_failed;
            _started = true;
            try
            {
                MfNative.Check(MfNative.MFStartup(MfNative.MfVersion, 0), "MFStartup");
                _mfUsers++;
                _gpu = GpuDevice.Create();
                _comp = new GpuCompositor(_gpu);
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
        lock (Gate)
        {
            foreach (var d in Files.Values) d.Dispose();
            Files.Clear();
            Stills.Clear();
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
        }
    }

    public static bool PresentStage(WriteableBitmap bmp, IReadOnlyList<GpuDraw> draws, bool playAudio)
    {
        if (!TryStart() || _comp is null) return false;
        lock (Gate)
        {
            SyncSources(draws, playAudio);
            var frame = _comp.RenderStage(bmp.PixelWidth, bmp.PixelHeight, draws);
            bmp.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Width * 4, 0);
            return true;
        }
    }

    public static bool PresentOutput(nint hwnd, int width, int height, IReadOnlyList<GpuDraw> draws, Display? display, bool playAudio)
    {
        if (!TryStart() || _comp is null || _gpu is null || hwnd == 0) return false;
        width = Math.Max(2, width);
        height = Math.Max(2, height);
        lock (Gate)
        {
            SyncSources(draws, playAudio);
            if (!Swaps.TryGetValue(hwnd, out var swap) || swap.Width != width || swap.Height != height)
            {
                swap?.Dispose();
                swap = OutputSwap.Create(_gpu, hwnd, width, height);
                Swaps[hwnd] = swap;
            }
            _comp.Render(swap.Rtv, width, height, draws, display);
            swap.Chain.Present(1, PresentFlags.None);
            return true;
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
        foreach (var dead in Files.Keys.Where(k => !live.Contains(k)).ToList())
        {
            Files[dead].Dispose();
            Files.Remove(dead);
            _comp?.Drop(dead);
        }
        foreach (var dead in Stills.Keys.Where(k => !live.Contains(k)).ToList())
        {
            Stills.Remove(dead);
            _comp?.Drop(dead);
        }
        foreach (var dead in LiveHolds.Keys.Where(k => !live.Contains(k)).ToList())
        {
            var fn = LiveHolds[dead];
            LiveHolds.Remove(dead);
            if (dead.StartsWith("ndi:", StringComparison.OrdinalIgnoreCase))
                NdiHub.Release(dead["ndi:".Length..], fn);
            else if (dead.StartsWith("capture:", StringComparison.OrdinalIgnoreCase))
                CaptureHub.Release(dead["capture:".Length..], fn);
            _comp?.Drop(dead);
        }

        foreach (var draw in draws)
        {
            switch (draw.Kind)
            {
                case GpuSourceKind.File:
                    if (!Files.TryGetValue(draw.SourceKey, out var decoder))
                    {
                        var path = draw.SourceKey.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                            ? draw.SourceKey["file:".Length..]
                            : draw.SourceKey;
                        if (!File.Exists(path)) continue;
                        decoder = new MfGpuDecoder(new Uri(Path.GetFullPath(path)).AbsoluteUri, playAudio);
                        Files[draw.SourceKey] = decoder;
                    }
                    decoder.Sync(draw.MediaTimeMs, draw.Playing, draw.Loop, draw.Volume, playAudio);
                    if (decoder.TryCopyFrame(out var pixels, out var w, out var h, out var stride, out var dirty) && dirty)
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
        using var back = chain.GetBuffer<ID3D11Texture2D>(0);
        var rtv = gpu.Device.CreateRenderTargetView(back);
        return new OutputSwap(chain, rtv, width, height);
    }

    public void Dispose()
    {
        Rtv.Dispose();
        Chain.Dispose();
    }
}
