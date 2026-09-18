using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Playback;
using Watchout.Core.Stage;

namespace Watchout.Core.Gpu;

public enum GpuSourceKind
{
    File,
    Still,
    Ndi,
    Capture
}

/// <summary>
/// One layer in the GPU scene: a textured quad in destination pixels, with
/// crop UVs, blend, color, wipe, and chroma. Stage and Output draw the same
/// list from one decoder.
/// </summary>
public readonly record struct GpuDraw(
    string CueId,
    GpuSourceKind Kind,
    string SourceKey,
    double MediaTimeMs,
    double DurationMs,
    bool Playing,
    bool Loop,
    float X,
    float Y,
    float W,
    float H,
    float U0,
    float V0,
    float U1,
    float V1,
    float Opacity,
    BlendMode Blend,
    float Brightness,
    float Contrast,
    float Saturation,
    float Hue,
    float Temperature,
    float Exposure,
    float Wipe,
    float WipeAngle,
    float WipeFeather,
    bool Chroma,
    float ChromaR,
    float ChromaG,
    float ChromaB,
    float ChromaTolerance,
    float Volume);

public static class GpuLayerMath
{
    public static bool UsesGpu(Asset? asset)
    {
        if (asset is null) return false;
        if (LiveSources.IsCapture(asset)) return true;
        if (LiveSources.NdiSourceName(asset) is { Length: > 0 }) return true;
        if (LiveSources.IsNdi(asset) || LiveSources.IsSt2110(asset)) return false;
        if (asset.Url.StartsWith("procedural:", StringComparison.Ordinal)) return false;
        return asset.Kind is AssetKind.Image or AssetKind.Video;
    }

    /// <summary>
    /// GPU compositor is up: Producer Stage skips MediaElement and draws the
    /// shared DXVA texture (one decode with Output). If the compositor is off,
    /// this is false so Stage keeps MediaElement — a 0.15s poster is often a
    /// black frame and looks like the cue has no content.
    /// </summary>
    public static bool StageYieldsFilePreview(bool editing, bool outputLive, Asset? asset) =>
        StageYieldsFilePreview(editing, outputLive, gpuOn: false, asset);

    public static bool StageYieldsFilePreview(bool editing, bool outputLive, bool gpuOn, Asset? asset) =>
        gpuOn && editing && outputLive && asset is not null && SourceKind(asset) == GpuSourceKind.File;

    /// <summary>
    /// Never. A first-frame JPEG at 0.15s stays black on this clip while the
    /// playhead is at 7 s. Dual MediaElement is the GPU-off fallback.
    /// </summary>
    public static bool StageShowsPoster(bool editing, bool outputLive, bool gpuOn, Asset? asset)
    {
        _ = (editing, outputLive, gpuOn, asset);
        return false;
    }

    /// <summary>
    /// Shared D3D11 textures. Stage and Output both draw this asset. Skip the
    /// WPF MediaElement / labeled canvas so the Output stack is visible on Stage.
    /// </summary>
    public static bool StageDrawsSharedGpu(bool gpuOn, Asset? asset) =>
        gpuOn && UsesGpu(asset);

    /// <summary>
    /// Producer Stage name plate. Output walls stay unlabeled. Cue name wins;
    /// fall back to the asset so a clip is still identifiable.
    /// </summary>
    public static bool ShowCueStageLabel(bool editing) => editing;

    public static string CueStageLabel(string? cueName, string? assetName)
    {
        var cue = cueName?.Trim() ?? "";
        var asset = assetName?.Trim() ?? "";
        if (cue.Length > 0 && !cue.Equals("Cue", StringComparison.OrdinalIgnoreCase))
            return cue;
        if (asset.Length > 0) return asset;
        return cue;
    }

    /// <summary>
    /// Opaque title bar on Producer Stage so a black clip still shows its name.
    /// Output walls stay unlabeled.
    /// </summary>
    public static double CueLabelBarHeight(double mappedHeight)
    {
        if (mappedHeight < 20) return 0;
        return Math.Clamp(mappedHeight * 0.12, 28, 36);
    }

    /// <summary>
    /// MediaElement / capture HWNDs cover WPF drawn on top of them. Inset the
    /// video so the title bar sits in a WPF strip the decoder cannot hide.
    /// </summary>
    public static (double X, double Y, double W, double H) CueVideoInset(
        double x, double y, double w, double h, bool editing, bool hwndLayer)
    {
        if (!editing || !hwndLayer) return (x, y, w, h);
        var barH = CueLabelBarHeight(h);
        if (barH < 18 || h - barH < 16) return (x, y, w, h);
        return (x, y + barH, w, h - barH);
    }

    public static GpuSourceKind SourceKind(Asset asset)
    {
        if (LiveSources.IsCapture(asset)) return GpuSourceKind.Capture;
        if (LiveSources.NdiSourceName(asset) is { Length: > 0 }) return GpuSourceKind.Ndi;
        if (asset.Kind == AssetKind.Image) return GpuSourceKind.Still;
        return GpuSourceKind.File;
    }

    public static string SourceKey(Asset asset)
    {
        if (LiveSources.IsCapture(asset))
            return "capture:" + (LiveSources.CaptureDeviceId(asset) ?? asset.Id);
        if (LiveSources.NdiSourceName(asset) is { Length: > 0 } ndi)
            return "ndi:" + ndi;
        if (asset.Kind == AssetKind.Image)
            return "still:" + (Codecs.TryFileUrl(asset.Url) ?? asset.PosterUrl ?? asset.Url);
        return "file:" + Codecs.PlaybackPath(asset);
    }

    public static GpuDraw FromCue(
        EvaluatedCue ev,
        Asset asset,
        double originX,
        double originY,
        double scale,
        double destW,
        double destH,
        PlaybackState playback,
        bool loop,
        double scaleY = 0)
    {
        var sy = scaleY == 0 ? scale : scaleY;
        var rect = StageGeometry.CueRect(ev, asset);
        var x = (float)((rect.X - originX) * scale);
        var y = (float)((rect.Y - originY) * sy);
        var w = (float)(rect.W * scale);
        var h = (float)(rect.H * sy);
        if (destW > 1 && destH > 1)
            (x, y, w, h) = FitDrawToDest(x, y, w, h, destW, destH);
        var crop = ev.Crop;
        var u0 = (float)Math.Clamp(crop.Left / 100.0, 0, 0.99);
        var v0 = (float)Math.Clamp(crop.Top / 100.0, 0, 0.99);
        var u1 = (float)Math.Clamp(1 - crop.Right / 100.0, u0 + 0.01, 1);
        var v1 = (float)Math.Clamp(1 - crop.Bottom / 100.0, v0 + 0.01, 1);
        CueLooks.TryParseHex(ev.Cue.ChromaKeyColor, out var kr, out var kg, out var kb);
        var playing = playback == PlaybackState.Play;
        var local = loop && asset.Duration > 1
            ? PlaybackClock.LoopFileTime(ev.LocalTime, asset.Duration)
            : ev.LocalTime;
        return new GpuDraw(
            ev.Cue.Id,
            SourceKind(asset),
            SourceKey(asset),
            local,
            asset.Duration,
            playing,
            loop,
            x, y, Math.Max(1, w), Math.Max(1, h),
            u0, v0, u1, v1,
            (float)Math.Clamp(ev.Opacity / 100.0, 0, 1),
            ev.Cue.Blend,
            (float)ev.Brightness,
            (float)ev.Contrast,
            (float)ev.Saturation,
            (float)ev.Hue,
            (float)ev.Temperature,
            (float)ev.Exposure,
            (float)ev.Wipe,
            (float)ev.WipeAngle,
            (float)ev.WipeFeather,
            ev.Cue.ChromaKeyEnabled,
            kr / 255f, kg / 255f, kb / 255f,
            (float)Math.Clamp(ev.Cue.ChromaKeyTolerance / 100.0, 0.01, 1),
            (float)Math.Clamp(ev.Volume / 100.0, 0, 1));
    }

    /// <summary>
    /// Map a stage-space cue rectangle into a destination pixel view
    /// (Stage widget or one Output display).
    /// </summary>
    public static (float X, float Y, float W, float H) MapRect(
        StageRect rect, double originX, double originY, double scaleX, double scaleY = 0)
    {
        var sy = scaleY == 0 ? scaleX : scaleY;
        return ((float)((rect.X - originX) * scaleX),
         (float)((rect.Y - originY) * sy),
         (float)(rect.W * scaleX),
         (float)(rect.H * sy));
    }

    /// <summary>
    /// A cue sized to the Stage display (3840) drawn into a smaller Output
    /// HWND crops the texture (looks zoomed). A 1700×720 cue fits because it
    /// stays inside that HWND. Scale the quad down so the whole picture stays
    /// visible; keep UVs 0–1. Scale X/Y with the same factor so dragging the
    /// cue still moves the picture (centering discarded the offset).
    /// </summary>
    public static (float X, float Y, float W, float H) FitDrawToDest(
        float x, float y, float w, float h, double destW, double destH)
    {
        var dw = (float)Math.Max(1, destW);
        var dh = (float)Math.Max(1, destH);
        w = Math.Max(1, w);
        h = Math.Max(1, h);
        if (w <= dw + 1 && h <= dh + 1)
            return (x, y, w, h);
        var s = Math.Min(dw / w, dh / h);
        var nw = Math.Max(1, w * s);
        var nh = Math.Max(1, h * s);
        return (x * s + (dw - nw) / 2, y * s + (dh - nh) / 2, nw, nh);
    }

    /// <summary>
    /// Column-major 4×4 acting on RGB. Brightness / contrast / saturation /
    /// hue plus Watchout temperature and exposure.
    /// </summary>
    public static float[] ColorMatrix(GpuDraw draw)
    {
        var b = draw.Brightness / 100f;
        var c = 1 + draw.Contrast / 100f;
        var s = draw.Saturation / 100f;
        var hue = draw.Hue * (MathF.PI / 180f);
        var exp = MathF.Pow(2, draw.Exposure);
        var temp = Math.Clamp(draw.Temperature, -100, 100) / 100f;

        // contrast around 0.5, then saturation, then hue rotate, then exposure, then brightness add
        var m = Identity();
        m = Multiply(ScaleRgb(c), m);
        m = Multiply(OffsetRgb((1 - c) * 0.5f), m);
        m = Multiply(SaturationMatrix(s), m);
        m = Multiply(HueMatrix(hue), m);
        m = Multiply(ScaleRgb(exp), m);
        m = Multiply(OffsetRgb(b), m);
        // temperature: warm adds red, cool adds blue
        m[12] += temp * 0.12f;
        m[14] -= temp * 0.12f;
        return m;
    }

    public static float EdgeBlend(float u, bool enabled, float blendWidthPx, float destWidthPx)
    {
        if (!enabled || blendWidthPx <= 1 || destWidthPx <= 1) return 1;
        var x = u * destWidthPx;
        var left = Math.Clamp(x / blendWidthPx, 0, 1);
        var right = Math.Clamp((destWidthPx - x) / blendWidthPx, 0, 1);
        return left * right;
    }

    public static string BlendLabel(BlendMode mode) => mode switch
    {
        BlendMode.Add => "Add",
        BlendMode.Multiply => "Multiply",
        BlendMode.Screen => "Screen",
        _ => "Normal",
    };

    static float[] Identity() =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1,
    ];

    static float[] ScaleRgb(float s)
    {
        var m = Identity();
        m[0] = m[5] = m[10] = s;
        return m;
    }

    static float[] OffsetRgb(float o)
    {
        var m = Identity();
        m[12] = m[13] = m[14] = o;
        return m;
    }

    static float[] SaturationMatrix(float s)
    {
        const float lr = 0.2126f, lg = 0.7152f, lb = 0.0722f;
        var a = (1 - s) * lr;
        var b = (1 - s) * lg;
        var c = (1 - s) * lb;
        return
        [
            a + s, b, c, 0,
            a, b + s, c, 0,
            a, b, c + s, 0,
            0, 0, 0, 1,
        ];
    }

    static float[] HueMatrix(float radians)
    {
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        return
        [
            0.213f + 0.787f * cos - 0.213f * sin,
            0.715f - 0.715f * cos - 0.715f * sin,
            0.072f - 0.072f * cos + 0.928f * sin,
            0,
            0.213f - 0.213f * cos + 0.143f * sin,
            0.715f + 0.285f * cos + 0.140f * sin,
            0.072f - 0.072f * cos - 0.283f * sin,
            0,
            0.213f - 0.213f * cos - 0.787f * sin,
            0.715f - 0.715f * cos + 0.715f * sin,
            0.072f + 0.928f * cos + 0.072f * sin,
            0,
            0, 0, 0, 1,
        ];
    }

    static float[] Multiply(float[] a, float[] b)
    {
        var r = new float[16];
        for (var col = 0; col < 4; col++)
        for (var row = 0; row < 4; row++)
        {
            r[col * 4 + row] =
                a[row] * b[col * 4] +
                a[4 + row] * b[col * 4 + 1] +
                a[8 + row] * b[col * 4 + 2] +
                a[12 + row] * b[col * 4 + 3];
        }
        return r;
    }
}
