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
}
