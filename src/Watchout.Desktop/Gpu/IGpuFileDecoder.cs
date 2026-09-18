using Vortice.Direct3D11;

namespace Watchout.Desktop.Gpu;

interface IGpuFileDecoder : IDisposable
{
    string? Error { get; }
    bool UsedSoftwareFallback { get; }
    bool UsedGpuSurfaces { get; }
    bool FellBackFromNv12 { get; }
    bool Ready { get; }
    bool Opening { get; }
    bool Dead { get; }
    bool Stalled { get; }
    bool HapYCoCg { get; }
    void Sync(double mediaMs, bool playing, bool loop, float volume, bool audio);
    bool TryBindGpu(out ID3D11Texture2D? texture, out bool dirty);
    bool TryCopyFrame(out byte[] pixels, out int width, out int height, out int stride, out bool dirty);
}
