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
/// One layer in the Watchout/Resolume-style GPU scene: a textured quad in
/// destination pixels, with crop UVs, blend, color, wipe, and chroma.
/// Stage and Output draw the same list from one decoder.
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
        bool loop)
    {
        var rect = StageGeometry.CueRect(ev, asset);
        var x = (float)((rect.X - originX) * scale);
        var y = (float)((rect.Y - originY) * scale);
        var w = (float)(rect.W * scale);
        var h = (float)(rect.H * scale);
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
        StageRect rect, double originX, double originY, double scale) =>
        ((float)((rect.X - originX) * scale),
         (float)((rect.Y - originY) * scale),
         (float)(rect.W * scale),
         (float)(rect.H * scale));

    /// <summary>
    /// Column-major 4×4 acting on RGB. Matches Resolume-style brightness /
    /// contrast / saturation / hue plus Watchout temperature and exposure.
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
