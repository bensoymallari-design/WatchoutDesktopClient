using Watchout.Core.Models;

namespace Watchout.Core.Stage;

public static class CueLooks
{
    public static double MediaTime(double localMs, double speedPercent) =>
        localMs * (speedPercent <= 0 ? 1 : speedPercent / 100.0);

    public static double SpeedRatio(double speedPercent) =>
        speedPercent <= 0 ? 1 : speedPercent / 100.0;

    public static (double X, double Y, double W, double H) CropBox(double width, double height, Crop crop)
    {
        var w = Math.Max(1, width);
        var h = Math.Max(1, height);
        var left = Math.Clamp(crop.Left, 0, 100) / 100.0 * w;
        var top = Math.Clamp(crop.Top, 0, 100) / 100.0 * h;
        var right = Math.Clamp(crop.Right, 0, 100) / 100.0 * w;
        var bottom = Math.Clamp(crop.Bottom, 0, 100) / 100.0 * h;
        var boxW = Math.Max(1, w - left - right);
        var boxH = Math.Max(1, h - top - bottom);
        return (left, top, boxW, boxH);
    }

    /// <summary>
    /// Linear wipe along <paramref name="angleDeg"/> (0° = left→right). Completion 0 is hidden, 100 is fully visible.
    /// </summary>
    public static (double X1, double Y1, double X2, double Y2, double Soft0, double Soft1) WipeGradient(
        double completion, double angleDeg, double feather)
    {
        var rad = angleDeg * Math.PI / 180.0;
        var dx = Math.Cos(rad);
        var dy = Math.Sin(rad);
        var t = Math.Clamp(completion / 100.0, 0, 1);
        var f = Math.Clamp(feather / 200.0, 0, 0.45);
        return (
            0.5 - dx * 0.5,
            0.5 - dy * 0.5,
            0.5 + dx * 0.5,
            0.5 + dy * 0.5,
            Math.Clamp(t - f, 0, 1),
            Math.Clamp(t + f, 0, 1));
    }

    public static (double R, double G, double B, double A) TemperatureOverlay(double temperature)
    {
        var t = Math.Clamp(temperature, -100, 100) / 100.0;
        if (Math.Abs(t) < 0.01) return (0, 0, 0, 0);
        return t >= 0 ? (1, 0.55, 0.16, t * 0.38) : (0.22, 0.48, 1, -t * 0.38);
    }

    public static (double R, double G, double B, double A) ExposureOverlay(double ev)
    {
        var e = Math.Clamp(ev, -4, 4);
        if (Math.Abs(e) < 0.04) return (0, 0, 0, 0);
        return e >= 0 ? (1, 1, 1, Math.Min(0.72, e * 0.14)) : (0, 0, 0, Math.Min(0.72, -e * 0.14));
    }

    public static (Vec3 Position, Vec2 Scale) ReplaceMedia(
        MediaReplaceMode mode, Asset? previous, Asset next, Vec3 oldPos, Vec2 oldScale)
    {
        var prevW = Math.Max(1, previous?.Width > 0 ? previous.Width : 1920);
        var prevH = Math.Max(1, previous?.Height > 0 ? previous.Height : 1080);
        var boxW = prevW * (oldScale.X <= 0 ? 1 : oldScale.X / 100.0);
        var boxH = prevH * (oldScale.Y <= 0 ? 1 : oldScale.Y / 100.0);
        var nw = Math.Max(1, next.Width > 0 ? next.Width : 1920);
        var nh = Math.Max(1, next.Height > 0 ? next.Height : 1080);
        if (mode == MediaReplaceMode.NewSize)
            return (oldPos, new Vec2 { X = 100, Y = 100 });
        if (mode == MediaReplaceMode.KeepOldSize)
            return (oldPos, new Vec2 { X = boxW / nw * 100, Y = boxH / nh * 100 });
        var factor = Math.Min(boxW / nw, boxH / nh);
        var w = nw * factor;
        var h = nh * factor;
        return (
            new Vec3
            {
                X = Math.Round(oldPos.X + (boxW - w) / 2),
                Y = Math.Round(oldPos.Y + (boxH - h) / 2),
                Z = oldPos.Z,
            },
            new Vec2 { X = Math.Round(factor * 10000) / 100, Y = Math.Round(factor * 10000) / 100 });
    }

    public static string ColorToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    public static bool TryParseHex(string? hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length == 3)
        {
            r = Convert.ToByte(new string(s[0], 2), 16);
            g = Convert.ToByte(new string(s[1], 2), 16);
            b = Convert.ToByte(new string(s[2], 2), 16);
            return true;
        }
        if (s.Length < 6) return false;
        r = Convert.ToByte(s[..2], 16);
        g = Convert.ToByte(s[2..4], 16);
        b = Convert.ToByte(s[4..6], 16);
        return true;
    }
}
