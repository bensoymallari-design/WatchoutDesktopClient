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
        if (clientW >= 256 && clientH >= 256)
        {
            if (screenW >= 256 && screenH >= 256)
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
    /// Map a show Display onto the Output wall in destination pixels.
    /// Uniform contain so a cue that fills the Stage display shows the whole
    /// picture on the wall. Stretching 3840 stage pixels into a 1920 HWND
    /// was cropping (zoomed). Snap to 1:1 when sizes already match.
    /// </summary>
    public static (double OriginX, double OriginY, double ScaleX, double ScaleY) OutputViewport(
        double displayX, double displayY, double displayW, double displayH,
        int destW, int destH)
    {
        var dw = Math.Max(1, displayW);
        var dh = Math.Max(1, displayH);
        var sx = destW / dw;
        var sy = destH / dh;
        var s = Math.Min(sx, sy);
        if (s <= 0) s = 1;
        // Snap to 1:1 only when the wall is at least as big as the display.
        // dest 3730 / display 3840 used to snap up to 1 and crop (looked zoomed).
        if (Math.Abs(s - 1) < 0.03 && destW >= dw - 1 && destH >= dh - 1) s = 1;
        var originX = displayX - (destW / s - dw) / 2;
        var originY = displayY - (destH / s - dh) / 2;
        return (originX, originY, s, s);
    }

    /// <summary>
    /// Keep Output presenting while the wall is open even if the timeline is
    /// stopped, so the first decoded frame can land before Play.
    /// </summary>
    public static bool PulseClock(bool timelinePlaying, bool outputLive) =>
        timelinePlaying || outputLive;

    /// <summary>
    /// Vortice <c>D3D11CreateDevice(params FeatureLevel[])</c> with no levels
    /// sends count 0 and a non-null pointer. Intel UHD returns E_INVALIDARG,
    /// the compositor stays off, Play never Presents, and the black/stuck Log
    /// never runs.
    /// </summary>
    public static bool D3D11CreateNeedsFeatureLevels(int featureLevelCount) =>
        featureLevelCount > 0;

    /// <summary>
    /// D3D11 constant buffers must be a multiple of 16 bytes. A 112-byte
    /// layer cbuffer is fine; round up anyway so Intel does not E_INVALIDARG.
    /// </summary>
    public static int ConstantBufferBytes(int size)
    {
        if (size <= 0) return 16;
        return (size + 15) / 16 * 16;
    }

    /// <summary>
    /// Screenshot / snip copies the DXGI wall. When GPU budget is already
    /// gone that throw must not close WatchMe — Recover and keep Producer.
    /// Any DXGI facility code (0x887A…) plus OOM / E_INVALIDARG / E_FAIL.
    /// </summary>
    public static bool SurviveUnhandled(int hresult)
    {
        if (hresult == 0) return false;
        if (((uint)hresult & 0xFFFF0000) == 0x887A0000) return true;
        return hresult == unchecked((int)0x8007000E)  // E_OUTOFMEMORY
            || hresult == unchecked((int)0x80070057)  // E_INVALIDARG
            || hresult == unchecked((int)0x80004005); // E_FAIL (Media Foundation / D3D)
    }

    /// <summary>
    /// Dispatcher / background-task filter so a GPU glitch does not close
    /// WatchMe on a client PC.
    /// </summary>
    public static bool SurviveException(int hresult, string? typeName, string? message)
    {
        if (SurviveUnhandled(hresult)) return true;
        if (typeName is not null
            && typeName.Contains("OutOfMemory", StringComparison.OrdinalIgnoreCase))
            return true;
        var blob = $"{typeName} {message}";
        return blob.Contains("DXGI", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("D3D11", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("Direct3D", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("Media Foundation", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// While Play is running, do not retry D3D11CreateDevice. Intel UHD
    /// E_INVALIDARG every 2 s covers the WPF picture and can crash the show.
    /// Retry after Stop.
    /// </summary>
    public static bool HoldSoftwareOutput(bool playing, bool compositorFailed) =>
        playing && compositorFailed;

    public static bool RetryCompositor(bool playing, bool compositorFailed) =>
        !HoldSoftwareOutput(playing, compositorFailed);
}
