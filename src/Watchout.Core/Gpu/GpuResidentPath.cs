namespace Watchout.Core.Gpu;

/// <summary>
/// Keep decoded frames on the GPU (DXVA/DXGI surfaces). WatchMe used to copy
/// every H.264 frame to RGB32 in system RAM and upload it again. Prefer the
/// GPU texture; CPU pixels are the fallback only.
/// </summary>
public enum GpuFrameSource
{
    None,
    DxgiTexture,
    CpuPixels
}

public static class GpuResidentPath
{
    /// <summary>
    /// Bind a DXGI/DXVA texture when one is ready. CPU RGB32 is only for
    /// software fallback (Intel UHD rejecting hardware surfaces).
    /// </summary>
    public static GpuFrameSource Choose(bool dxgiTextureReady, bool cpuPixelsReady)
    {
        if (dxgiTextureReady) return GpuFrameSource.DxgiTexture;
        if (cpuPixelsReady) return GpuFrameSource.CpuPixels;
        return GpuFrameSource.None;
    }

    /// <summary>
    /// Stage's WPF Image does a GPU→CPU Map. Skip that while Output owns the
    /// file decoder (empty Stage draws + hold last frame) — that Map is why
    /// RAM filled with a green bar during a long PLAY.
    /// </summary>
    public static bool SkipStageCpuReadback(bool stageHasNoDraws, bool keepLastFrame) =>
        stageHasNoDraws && keepLastFrame;

    /// <summary>
    /// Stage preview is a scaled GPU view, not a 4K BGRA staging texture.
    /// Cap Stage's CPU bitmap so Intel UHD is not filled with a second
    /// 3840×2160 Map while the wall already Presents.
    /// </summary>
    public const int StagePreviewMaxEdge = 1280;

    public static (int W, int H) StagePreviewSize(int width, int height, int maxEdge = StagePreviewMaxEdge)
    {
        width = Math.Max(2, width);
        height = Math.Max(2, height);
        var cap = maxEdge < 64 ? StagePreviewMaxEdge : maxEdge;
        var edge = Math.Max(width, height);
        if (edge <= cap) return (width, height);
        var s = cap / (double)edge;
        var w = Math.Max(2, (int)Math.Round(width * s) / 2 * 2);
        var h = Math.Max(2, (int)Math.Round(height * s) / 2 * 2);
        return (w, h);
    }

    /// <summary>
    /// When a DXGI/HAP texture is bound, do not also copy RGB32. That second
    /// path is the RAM fill a long PLAY used to hit.
    /// </summary>
    public static bool NeedCpuPixels(bool gpuTextureReady) => !gpuTextureReady;

    /// <summary>
    /// Reuse a scratch buffer instead of <c>new byte[]</c> every capture, NDI,
    /// or PCM packet. Same length (or larger) is a hit.
    /// </summary>
    public static bool ReuseBuffer(int existingLength, int needed) =>
        needed > 0 && existingLength >= needed;

    public static int GrowBuffer(int existingLength, int needed)
    {
        if (needed <= 0) return 0;
        if (existingLength >= needed) return existingLength;
        var grow = existingLength <= 0 ? needed : existingLength * 2;
        return Math.Max(needed, grow);
    }

    /// <summary>
    /// NV12 + D3D manager can "open" without ever handing a DXGI frame
    /// (Dolby Vision / HEVC, or IMFDXGIBuffer QI failing). That left Output
    /// black after delete-and-reload of the same MP4. Reject the open and
    /// try RGB32. NDI and capture are not this path.
    /// </summary>
    public const int PrerollAttempts = 80;
    public const int PrerollBudgetMs = 2500;

    public static bool OpenProducedAFrame(bool ready) => ready;

    public static bool StillOpening(bool openFinished) => !openFinished;

    public static bool StallWithoutPicture(bool playing, bool ready, double sinceOpenMs, double stallMs) =>
        playing && !ready && sinceOpenMs >= stallMs;
}
