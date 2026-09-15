namespace Watchout.Core.Media;

/// <summary>
/// Live NDI/capture picture size and blit timing. Resolume 4K NDI is full
/// bandwidth BGRA; WatchMe used to assume 1920×1080, blit at 30 fps, and
/// pick capture formats ≤1080, which looked soft and late on a 4K wall.
/// </summary>
public static class LivePicture
{
    public const int NdiHighestBandwidth = 100;
    public const int NdiColorBgra = 0;
    public const int OutputBlitMs = 8;
    public const int StageBlitMs = 16;
    public const int MaxWidth = 3840;
    public const int MaxHeight = 2160;

    public static bool FrameDue(long nowMs, long lastMs, int minMs) =>
        minMs <= 0 || lastMs <= 0 || nowMs - lastMs >= minMs;

    public static int BlitMinMs(bool output) => output ? OutputBlitMs : StageBlitMs;

    public static (double ScaleX, double ScaleY) KeepCueScale(
        double oldW, double oldH, double scaleX, double scaleY, int newW, int newH)
    {
        if (oldW < 1 || oldH < 1 || newW < 1 || newH < 1) return (scaleX, scaleY);
        return (scaleX * oldW / newW, scaleY * oldH / newH);
    }

    public static (int W, int H, double Fps)? PickCaptureFormat(
        IEnumerable<(int W, int H, double Fps)> formats, int maxW = MaxWidth, int maxH = MaxHeight)
    {
        var list = formats.Where(f => f.W > 0 && f.H > 0).ToList();
        if (list.Count == 0) return null;
        var hd = list.Where(f => f.W >= 1280).ToList();
        var pool = hd.Count > 0 ? hd : list;
        var within = pool.Where(f => f.W <= maxW && f.H <= maxH).ToList();
        var pickFrom = within.Count > 0 ? within : pool;
        return pickFrom
            .OrderByDescending(f => f.W * f.H)
            .ThenByDescending(f => f.Fps)
            .First();
    }
}
