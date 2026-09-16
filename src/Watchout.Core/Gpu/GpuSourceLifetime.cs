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
}
