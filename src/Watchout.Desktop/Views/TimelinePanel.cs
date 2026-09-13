using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Watchout.Core;
using Watchout.Core.Models;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

public sealed class TimelinePanel : FrameworkElement
{
    const double LaneH = 28;
    const double HeadW = 120;
    string? _dragId;
    double _dragStartMs;
    Point _mouseDown;

    public TimelinePanel()
    {
        ClipToBounds = true;
        Focusable = true;
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            e.Effects = StudioDrag.IsMediaDrag(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += OnDrop;
        App.Session.Changed += () => Dispatcher.BeginInvoke(InvalidateVisual);
        MouseLeftButtonDown += OnDown;
        MouseLeftButtonUp += (_, _) => { _dragId = null; ReleaseMouseCapture(); };
        MouseMove += OnMove;
        MouseWheel += (_, e) =>
        {
            App.Session.TimelineZoom = Math.Clamp(App.Session.TimelineZoom * (e.Delta > 0 ? 1.15 : 0.87), 0.002, 0.2);
            InvalidateVisual();
        };
    }

    public void Reload() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 22, 22)), null, new Rect(0, 0, w, h));
        var tl = App.Session.ActiveTimeline;
        if (tl is null) return;
        var zoom = App.Session.TimelineZoom;
        var scroll = App.Session.TimelineScroll;
        var xOf = (double ms) => HeadW + (ms - scroll) * zoom;
        var msOf = (double x) => (x - HeadW) / zoom + scroll;

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(32, 32, 32)), null, new Rect(0, 0, HeadW, h));
        var rulerH = 22.0;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 28, 28)), null, new Rect(HeadW, 0, w - HeadW, rulerH));
        var step = NiceStep(80 / zoom);
        for (double t = 0; t <= tl.Duration; t += step)
        {
            var x = xOf(t);
            if (x < HeadW || x > w) continue;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(70, 70, 70)), 1), new Point(x, 0), new Point(x, h));
            var label = new FormattedText(TimeFormat.FormatMs(t)[3..], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, new SolidColorBrush(Color.FromRgb(160, 160, 160)), 1.25);
            dc.DrawText(label, new Point(x + 4, 4));
        }

        var y = rulerH;
        foreach (var layer in tl.Layers)
        {
            var name = new FormattedText(layer.Name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, Brushes.White, 1.25);
            dc.DrawText(name, new Point(8, y + 6));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(40, 40, 40)), 1), new Point(0, y + LaneH), new Point(w, y + LaneH));
            foreach (var cue in tl.Cues.Where(c => c.LayerId == layer.Id))
            {
                var x = xOf(cue.Start);
                var cw = Math.Max(4, cue.Duration * zoom);
                var selected = App.Session.Selection.Kind == SelectionKind.Cue && App.Session.Selection.Ids.Contains(cue.Id);
                var fill = BrushFrom(cue.Color);
                dc.DrawRectangle(fill, new Pen(selected ? new SolidColorBrush(Color.FromRgb(245, 166, 35)) : Brushes.Transparent, 2),
                    new Rect(x, y + 3, cw, LaneH - 6));
                var title = new FormattedText(cue.Name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 11, Brushes.White, 1.25);
                title.MaxTextWidth = Math.Max(10, cw - 8);
                title.MaxTextHeight = 16;
                dc.DrawText(title, new Point(x + 4, y + 6));
            }
            y += LaneH;
        }

        var px = xOf(tl.Playhead);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(245, 166, 35)), 1.5), new Point(px, 0), new Point(px, h));
    }

    static double NiceStep(double raw)
    {
        var mag = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1, raw))));
        var n = raw / mag;
        var step = n < 2 ? 1 : n < 5 ? 2 : 5;
        return step * mag;
    }

    static Brush BrushFrom(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!); }
        catch { return new SolidColorBrush(Color.FromRgb(59, 130, 196)); }
    }

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        var tl = App.Session.ActiveTimeline;
        if (tl is null) return;
        var p = e.GetPosition(this);
        var zoom = App.Session.TimelineZoom;
        var scroll = App.Session.TimelineScroll;
        if (p.Y < 22)
        {
            var ms = Math.Max(0, (p.X - HeadW) / zoom + scroll);
            App.Session.SetPlayhead(tl.Id, ms);
            return;
        }
        var layerIndex = (int)Math.Floor((p.Y - 22) / LaneH);
        if (layerIndex < 0 || layerIndex >= tl.Layers.Count) return;
        var layer = tl.Layers[layerIndex];
        var msAt = (p.X - HeadW) / zoom + scroll;
        var cue = tl.Cues.LastOrDefault(c => c.LayerId == layer.Id && msAt >= c.Start && msAt <= c.Start + Math.Max(40 / zoom, c.Duration));
        if (cue is not null)
        {
            App.Session.Select(SelectionKind.Cue, cue.Id);
            _dragId = cue.Id;
            _dragStartMs = cue.Start;
            _mouseDown = p;
            CaptureMouse();
        }
        else if (App.Session.ClickJumpsToTime)
            App.Session.SetPlayhead(tl.Id, Math.Max(0, msAt));
    }

    async void OnDrop(object sender, DragEventArgs e)
    {
        var tl = App.Session.ActiveTimeline;
        if (tl is null) return;
        e.Handled = true;
        var p = e.GetPosition(this);
        var zoom = App.Session.TimelineZoom;
        var scroll = App.Session.TimelineScroll;
        var start = Math.Max(0, (p.X - HeadW) / zoom + scroll);
        var layerIndex = Math.Clamp((int)Math.Floor((p.Y - 22) / LaneH), 0, Math.Max(0, tl.Layers.Count - 1));
        var layerId = tl.Layers[layerIndex].Id;
        if (StudioDrag.TryAssetId(e.Data, out var assetId))
        {
            App.Session.AddCueFromAsset(assetId, layerId, start);
            StudioDrag.AssetId = null;
            return;
        }
        var files = StudioDrag.Files(e.Data);
        if (files.Length == 0) return;
        var ids = await MediaLibrary.ImportFilesAsync(files, App.Session);
        foreach (var id in ids)
            App.Session.AddCueFromAsset(id, layerId, start);
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (_dragId is null || e.LeftButton != MouseButtonState.Pressed) return;
        var zoom = App.Session.TimelineZoom;
        var dx = e.GetPosition(this).X - _mouseDown.X;
        var start = Math.Max(0, _dragStartMs + dx / zoom);
        App.Session.UpdateCue(_dragId, c => c.Start = Math.Round(start), record: false);
    }
}
