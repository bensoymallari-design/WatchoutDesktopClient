namespace Watchout.Core.Gpu;

/// <summary>
/// Resolume keeps decoded frames on the GPU (DXVA/DXGI surfaces, HAP, DXV).
/// WatchMe used to copy every H.264 frame to RGB32 in system RAM and upload
/// it again. Prefer the GPU texture; CPU pixels are the fallback only.
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
