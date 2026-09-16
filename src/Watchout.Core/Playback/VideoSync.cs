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

    public const double StallMs = 4000;

    public static bool SeekOnPlayStart(double driftMs) => driftMs > StartSeekMs;

    public static bool ReseekWhilePlaying(double driftMs, double msSinceSeek) =>
        msSinceSeek >= SeekCooldownMs && driftMs > PlayReseekMs;

    public static bool SeekWhileIdle(double driftMs) => driftMs > ScrubSeekMs;

    /// <summary>
    /// DXVA can freeze after a long 4K run (around an hour of dual Stage+Output
    /// decode). Position stops advancing and new Play() on the same MediaElement
    /// stays black — rebuild the decoder.
    /// </summary>
    public static bool DecoderStalled(bool playing, double msSinceAdvance, double msSinceSeek) =>
        playing && msSinceSeek >= StallMs && msSinceAdvance >= StallMs;

    /// <summary>
    /// Timeline loop jumped the clock backward. The file decoder is still at EOF
    /// (or far ahead) and WPF will not draw again unless we Play() from the new time.
    /// </summary>
    public static bool RestartAfterWrap(double decoderMs, double targetMs) =>
        decoderMs - targetMs > PlayReseekMs;
}
