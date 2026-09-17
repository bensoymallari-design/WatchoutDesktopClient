namespace Watchout.Core.Gpu;

/// <summary>
/// Output used to Present into a nested 64×64 Static HWND while the wall
/// window was 4K, and DXGI FlipDiscard cannot target a WS_CHILD HWND.
/// Size the host to the OS screen and use a blt swap on child windows.
/// </summary>
public static class OutputViewMath
{
    public const int MinHostPx = 64;

    /// <summary>
    /// Physical pixels for the Output swap chain. A 64×64 HwndHost must not
    /// drive a 4K wall — prefer the OS screen, then the show Display.
    /// </summary>
    public static (int W, int H) PresentSize(
        int widgetW, int widgetH,
        int displayW, int displayH,
        int screenW, int screenH)
    {
        if (screenW >= MinHostPx && screenH >= MinHostPx)
            return (screenW, screenH);
        if (displayW >= MinHostPx && displayH >= MinHostPx)
            return (displayW, displayH);
        if (widgetW >= MinHostPx && widgetH >= MinHostPx)
            return (widgetW, widgetH);
        return (Math.Max(2, widgetW), Math.Max(2, widgetH));
    }

    public static (int W, int H) DipToPixels(double dipW, double dipH, double scale)
    {
        var s = scale > 0.1 ? scale : 1;
        return (
            Math.Max(2, (int)Math.Round(dipW * s)),
            Math.Max(2, (int)Math.Round(dipH * s)));
    }

    /// <summary>
    /// WPF Width/Height are DIP. Convert OS screen pixels with the *target*
    /// monitor scale — never the laptop PresentationSource DPI. Using 1.5 on a
    /// 100% HDMI wall turns 3840 into 2560.
    /// </summary>
    public static (double DipW, double DipH) ScreenToDip(int pixelW, int pixelH, double monitorScale)
    {
        var s = monitorScale > 0.1 ? monitorScale : 1;
        return (Math.Max(1, pixelW / s), Math.Max(1, pixelH / s));
    }

    /// <summary>
    /// Output HWND size is the Windows mode, not a previous client rect and
    /// not the Stage display (EDID 4K on a 1080 mode must not create a 4K HWND).
    /// </summary>
    public static (int W, int H) WallPixels(int screenW, int screenH) =>
        (Math.Max(MinHostPx, screenW), Math.Max(MinHostPx, screenH));

    /// <summary>
    /// Swap-chain / mapping dest. A 64×64 stub must not drive 4K, but drawing
    /// 3840 pixels into a smaller HWND crops the picture (looks zoomed).
    /// Use the real client when it is a real window, and never present larger
    /// than the Windows mode (a 3840 buffer on a 2560 client is a zoomed crop).
    /// </summary>
    public static (int W, int H) PresentDest(int clientW, int clientH, int screenW, int screenH)
    {
        // 64×64 stub HWND must not drive 4K. A Colorlight 516×430 (or smaller
        // cabinet map) is a real window — never fall back to a 1920 client.
        if (clientW >= 256 && clientH >= 256)
        {
            if (screenW >= MinHostPx && screenH >= MinHostPx)
                return (Math.Min(clientW, screenW), Math.Min(clientH, screenH));
            return (clientW, clientH);
        }
        return WallPixels(screenW, screenH);
    }

    /// <summary>
    /// DXGI FlipDiscard ignores Stretch (behaves like NONE). A swap larger than
    /// the HWND crops top-left — the 3840 Fit-cue zoom. Present at the real
    /// back-buffer size, not the size we asked for.
    /// </summary>
    public static (int W, int H) SwapPixels(int requestedW, int requestedH, int actualW, int actualH)
    {
        var w = actualW >= MinHostPx ? actualW : requestedW;
        var h = actualH >= MinHostPx ? actualH : requestedH;
        return (Math.Max(2, w), Math.Max(2, h));
    }

    public static bool HostNeedsResize(int hostW, int hostH, int presentW, int presentH) =>
        Math.Abs(hostW - presentW) > 1 || Math.Abs(hostH - presentH) > 1;

    /// <summary>
    /// DXGI flip-model (FLIP_DISCARD / FLIP_SEQUENTIAL) is invalid on a
    /// WS_CHILD HWND. That HRESULT used to TearDown the shared decoder and
    /// leave the wall black.
    /// </summary>
    public static bool FlipModelAllowed(bool hwndIsChild) => !hwndIsChild;

    /// <summary>
    /// Use the top-level Output window when the nested host is still the
    /// 64×64 CreateWindowEx stub.
    /// </summary>
    public static bool PreferTopLevelHwnd(int hostW, int hostH, int presentW, int presentH) =>
        hostW < MinHostPx || hostH < MinHostPx || HostNeedsResize(hostW, hostH, presentW, presentH);

    /// <summary>
    /// RGB32 + MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS can fail on Intel UHD
    /// (HEVC / Dolby Vision). Retry the same URL without that flag.
    /// </summary>
    public static bool RetryOpenWithoutHardwareTransforms(bool hardwareOpenFailed) =>
        hardwareOpenFailed;

    /// <summary>
    /// Only nuke the D3D device on DEVICE_REMOVED / DEVICE_RESET. A bad
    /// child swap chain must not dispose the H.264 decoder.
    /// </summary>
    public static bool TearGpuOnPresentError(int hresult) =>
        hresult == unchecked((int)0x887A0005) || hresult == unchecked((int)0x887A0007);

    /// <summary>
    /// Resolume-style: Output is the live controller/TV pixels. Stretch the
    /// Stage display to fill that dest — every cabinet pixel has picture, no
    /// letterbox gap. When Stage already matches the wall (516×430 on an X20),
    /// scale is 1:1.
    /// </summary>
    public static (double OriginX, double OriginY, double ScaleX, double ScaleY) OutputViewport(
        double displayX, double displayY, double displayW, double displayH,
        int destW, int destH)
    {
        var dw = Math.Max(1, displayW);
        var dh = Math.Max(1, displayH);
        var sx = destW / dw;
        var sy = destH / dh;
        if (sx <= 0) sx = 1;
        if (sy <= 0) sy = 1;
        if (Math.Abs(destW - dw) <= 1 && Math.Abs(destH - dh) <= 1)
        {
            sx = 1;
            sy = 1;
        }
        return (displayX, displayY, sx, sy);
    }

    /// <summary>
    /// Keep Output presenting while the wall is open even if the timeline is
    /// stopped, so the first decoded frame can land before Play.
    /// </summary>
    public static bool PulseClock(bool timelinePlaying, bool outputLive) =>
        timelinePlaying || outputLive;
}
