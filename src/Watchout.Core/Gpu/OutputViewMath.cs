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
    /// Map a show Display onto the Output wall in destination pixels.
    /// Stretch-fills so Stage display 1920×1080 on a 1920×1080 wall is 1:1,
    /// and WPF DIP (2560 at 150%) must not letterbox a 4K wall.
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
        if (Math.Abs(sx - 1) < 0.03) sx = 1;
        if (Math.Abs(sy - 1) < 0.03) sy = 1;
        return (displayX, displayY, sx, sy);
    }

    /// <summary>
    /// Keep Output presenting while the wall is open even if the timeline is
    /// stopped, so the first decoded frame can land before Play.
    /// </summary>
    public static bool PulseClock(bool timelinePlaying, bool outputLive) =>
        timelinePlaying || outputLive;
}
