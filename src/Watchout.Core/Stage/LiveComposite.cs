namespace Watchout.Core.Stage;

/// <summary>
/// When a live NDI/capture feed sits in front of H.264, DXVA is an overlay HWND
/// and a WPF bitmap (or child window) under that plane never reaches the wall.
/// Output then has to blit the live picture in a top-level layered window.
/// </summary>
public static class LiveComposite
{
    public static bool ScreenOverlayOnOutput(int liveZ, IEnumerable<int> videoZs)
    {
        foreach (var z in videoZs)
            if (z >= liveZ) return false;
        return true;
    }

    /// <summary>
    /// Draw live overlays back-to-front (low Z first) so later timeline layers
    /// sit above earlier NDI/capture feeds on the wall.
    /// </summary>
    public static int[] OverlayStack(IReadOnlyList<int> liveZs, IReadOnlyList<int> videoZs) =>
        liveZs.Where(z => ScreenOverlayOnOutput(z, videoZs)).OrderBy(z => z).ToArray();

    /// <summary>
    /// Raise order for named live feeds after a layer swap. Last id is the front-most
    /// HWND (later timeline layer). Stable when two feeds share a z.
    /// </summary>
    public static string[] StackFeeds(IReadOnlyList<(string Id, int Z)> feeds, IReadOnlyList<int> videoZs) =>
        feeds.Where(f => ScreenOverlayOnOutput(f.Z, videoZs)).OrderBy(f => f.Z).Select(f => f.Id).ToArray();

    /// <summary>
    /// Ignore 1-pixel jitter from PointToScreen / layout rounding so the overlay
    /// does not recreate its DIB and flash every frame.
    /// </summary>
    public static int Stick(int next, int prev, int slop = 1)
    {
        if (prev > 0 && Math.Abs(next - prev) <= slop) return prev;
        return next;
    }

    /// <summary>
    /// Capture/NDI on Output is a top-level layered HWND. EVR exclusive overlay
    /// (MediaElement with ScrubbingEnabled=false) then freezes the MP4 while
    /// the card keeps blitting. Scrubbing forces a non-overlay file renderer.
    /// </summary>
    public static bool FileRendererScrubs(bool outputSurface, bool liveHwndOnOutput) =>
        outputSurface && liveHwndOnOutput;
}
