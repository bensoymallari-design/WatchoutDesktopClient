namespace Watchout.Core.Playback;

/// <summary>
/// DXVA MediaElement stalls if we seek it every clock tick or Pause+seek on Stop.
/// NDI is a live blit so it stays smooth while the file decoder waits.
/// </summary>
public static class VideoSync
{
    public const double StartSeekMs = 250;
    public const double PlayReseekMs = 800;
    public const double SeekCooldownMs = 1000;
    public const double ScrubSeekMs = 120;

    public static bool SeekOnPlayStart(double driftMs) => driftMs > StartSeekMs;

    public static bool ReseekWhilePlaying(double driftMs, double msSinceSeek) =>
        msSinceSeek >= SeekCooldownMs && driftMs > PlayReseekMs;

    public static bool SeekWhileIdle(double driftMs) => driftMs > ScrubSeekMs;
}
