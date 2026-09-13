using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Watchout.Desktop.Media;

public static class DemoArt
{
    public static ImageSource? ForUrl(string url, int width = 1920, int height = 1080)
    {
        return url switch
        {
            "watchout:demo/bars" or "watchme:demo/bars" => Bars(width, height),
            "watchout:demo/title" or "watchme:demo/title" => Title(width, height),
            "watchout:demo/grid" or "watchme:demo/grid" => Grid(width, height),
            _ => null,
        };
    }

    public static ImageSource Bars(int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            Color[] colors = [Color.FromRgb(0xC0, 0xC0, 0xC0), Color.FromRgb(0xC0, 0xC0, 0), Color.FromRgb(0, 0xC0, 0xC0), Color.FromRgb(0, 0xC0, 0), Color.FromRgb(0xC0, 0, 0xC0), Color.FromRgb(0xC0, 0, 0), Color.FromRgb(0, 0, 0xC0)];
            var col = w / 7.0;
            for (var i = 0; i < 7; i++)
                dc.DrawRectangle(new SolidColorBrush(colors[i]), null, new System.Windows.Rect(i * col, 0, col + 1, h));
            DrawCentered(dc, "WatchMe", w, h, 72, Colors.White);
        }
        return Render(dv, w, h);
    }

    public static ImageSource Title(int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(11, 18, 32), Color.FromRgb(28, 25, 23), 45), null, new System.Windows.Rect(0, 0, w, h));
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(245, 166, 35)), 4), new System.Windows.Rect(80, 80, w - 160, h - 160));
            DrawCentered(dc, "WatchMe", w, h - 80, 92, Color.FromRgb(245, 166, 35));
            DrawCentered(dc, "MULTI-DISPLAY SHOW COMPOSER", w, h + 80, 28, Color.FromRgb(231, 229, 228));
        }
        return Render(dv, w, h);
    }

    public static ImageSource Grid(int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(9, 9, 11)), null, new System.Windows.Rect(0, 0, w, h));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(39, 39, 42)), 1);
            for (var x = 0; x < w; x += 60) dc.DrawLine(pen, new System.Windows.Point(x, 0), new System.Windows.Point(x, h));
            for (var y = 0; y < h; y += 60) dc.DrawLine(pen, new System.Windows.Point(0, y), new System.Windows.Point(w, y));
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(245, 166, 35)), 3), new System.Windows.Point(w / 2.0, h / 2.0), 80, 80);
        }
        return Render(dv, w, h);
    }

    static void DrawCentered(DrawingContext dc, string text, int w, int h, double size, Color color)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, new SolidColorBrush(color), 1.25);
        dc.DrawText(ft, new System.Windows.Point((w - ft.Width) / 2, (h - ft.Height) / 2));
    }

    static ImageSource Render(DrawingVisual dv, int w, int h)
    {
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }
}
