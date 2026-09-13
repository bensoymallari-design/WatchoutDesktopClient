using System.Windows;
using System.Windows.Media;

namespace Watchout.Desktop.Views;

public sealed class ProceduralLayer : FrameworkElement
{
    public string Kind { get; set; } = "procedural:aurora";
    public double LocalTime { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var t = LocalTime / 1000.0;
        if (Kind.Contains("ndi", StringComparison.OrdinalIgnoreCase))
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(8, 16, 24)), null, new Rect(0, 0, w, h));
            var cx = w / 2 + Math.Sin(t) * 40;
            var cy = h / 2 + Math.Cos(t * 0.8) * 20;
            var rg = new RadialGradientBrush(Color.FromArgb(140, 74, 222, 128), Color.FromArgb(0, 8, 16, 24))
            {
                Center = new Point(cx / w, cy / h),
                RadiusX = 0.55,
                RadiusY = 0.55,
            };
            dc.DrawRectangle(rg, null, new Rect(0, 0, w, h));
            var text = new FormattedText("NDI  ·  PROGRAM", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), Math.Max(18, w / 22), new SolidColorBrush(Color.FromRgb(74, 222, 128)), 1.25);
            dc.DrawText(text, new Point((w - text.Width) / 2, h / 2 - 10));
            return;
        }

        var a = (Math.Sin(t * 0.35) + 1) / 2;
        var g = new LinearGradientBrush(
            ColorFromHsl(190 + a * 40, 0.8, 0.45),
            ColorFromHsl(330, 0.6, 0.16),
            45);
        dc.DrawRectangle(g, null, new Rect(0, 0, w, h));
    }

    static Color ColorFromHsl(double h, double s, double l)
    {
        h = (h % 360 + 360) % 360;
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
}
