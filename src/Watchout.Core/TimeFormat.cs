namespace Watchout.Core;

public static class TimeFormat
{
    public static double Clamp(double n, double min, double max) => Math.Min(max, Math.Max(min, n));

    public static string FormatTimecode(double ms, double fps = 60)
    {
        var sign = ms < 0 ? "-" : "";
        var abs = Math.Max(0, Math.Abs(ms));
        var h = (int)Math.Floor(abs / 3_600_000);
        var m = (int)Math.Floor(abs % 3_600_000 / 60_000);
        var s = (int)Math.Floor(abs % 60_000 / 1000);
        var frames = (int)Math.Floor(abs % 1000 / 1000 * fps);
        return $"{sign}{h:00}:{m:00}:{s:00}.{frames:00}";
    }

    public static string FormatMs(double ms)
    {
        var abs = Math.Max(0, ms);
        var h = (int)Math.Floor(abs / 3_600_000);
        var m = (int)Math.Floor(abs % 3_600_000 / 60_000);
        var s = (int)Math.Floor(abs % 60_000 / 1000);
        var milli = (int)Math.Floor(abs % 1000);
        return $"{h:00}:{m:00}:{s:00}.{milli:000}";
    }

    public static string FormatPlayTime(double ms)
    {
        var abs = Math.Max(0, Math.Round(ms));
        return $"{FormatMs(abs)}  ·  {abs / 1000:0.000} s  ·  {abs:0} ms";
    }

    public static double ParseTimecode(string value)
    {
        var parts = value.Trim().Split(':');
        if (parts.Length == 1)
            return double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n * 1000 : 0;

        var h = ParsePart(parts[0]);
        var m = parts.Length > 1 ? ParsePart(parts[1]) : 0;
        var rest = parts.Length > 2 ? parts[2] : "0";
        var split = rest.Split('.');
        var sec = ParsePart(split[0]);
        var frac = split.Length > 1 ? split[1].PadRight(3, '0')[..3] : "000";
        var milli = ParsePart(frac);
        return ((h * 60 + m) * 60 + sec) * 1000 + milli;
    }

    static double ParsePart(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
}
