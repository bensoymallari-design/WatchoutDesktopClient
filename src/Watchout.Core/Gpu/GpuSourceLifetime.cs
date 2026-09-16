namespace Watchout.Core.Gpu;

/// <summary>
/// Stage and Output share one decoder. An empty Present used to dispose that
/// decoder and clear both surfaces to black while the timeline still said PLAY.
/// Hold textures across a short idle and keep the last frame while playing.
/// </summary>
public static class GpuSourceLifetime
{
    public const int HoldFrames = 180;

    public static bool ReleaseAfterIdle(int idleFrames) => idleFrames >= HoldFrames;

    public static bool KeepLastFrame(bool playing, int drawCount) =>
        playing && drawCount <= 0;

    /// <summary>
    /// Do not dispose the shared H.264 decoder while PLAY just because this
    /// Present's layer list is empty (decoder still opening, Stage skipped the
    /// file preview, or a new clip is replacing the last one).
    /// </summary>
    public static bool FreezeIdleWhilePlaying(bool playing, int liveCount) =>
        playing && liveCount <= 0;

    /// <summary>
    /// Output must keep a swap chain flipping while PLAY. Skipping Present left
    /// a black wall after loading another video (decoder not ready yet).
    /// </summary>
    public static bool OutputMustFlip(bool outputSurface, bool playing) =>
        outputSurface && playing;

    /// <summary>
    /// Only clear the wall when a texture is ready to draw. Clearing first then
    /// skipping the quad (decoder still opening / Present DoNotWait failed)
    /// is what stuck Output on black.
    /// </summary>
    public static bool ClearToBlack(int readyTextures) => readyTextures > 0;
}
