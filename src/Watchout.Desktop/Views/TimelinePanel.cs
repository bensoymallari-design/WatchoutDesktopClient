using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Watchout.Core;
using Watchout.Core.Models;
using Watchout.Core.Scheduling;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

public sealed class TimelinePanel : FrameworkElement
{
    const double LaneH = 28;
    const double HeadW = 120;
    string? _dragId;
    double _dragStartMs;
    double _dragDuration;
    string _dragEdge = "m";
    int _dragLayerIndex;
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
            var nameBrush = !layer.Enabled
                ? new SolidColorBrush(Color.FromRgb(90, 90, 90))
                : layer.Locked
                    ? new SolidColorBrush(Color.FromRgb(160, 140, 90))
                    : Brushes.White;
            var selectedLayer = App.Session.Selection.Kind == SelectionKind.Layer && App.Session.Selection.Ids.Contains(layer.Id);
            if (selectedLayer)
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(42, 36, 24)), null, new Rect(0, y, HeadW, LaneH));
            var name = new FormattedText((layer.Locked ? "* " : "") + layer.Name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, nameBrush, 1.25);
            name.MaxTextWidth = HeadW - 12;
            name.MaxTextHeight = LaneH - 4;
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
        Focus();
        var tl = App.Session.ActiveTimeline;
        if (tl is null) return;
        var p = e.GetPosition(this);
        var zoom = App.Session.TimelineZoom;
        var scroll = App.Session.TimelineScroll;
        if (e.RightButton == MouseButtonState.Pressed || e.ChangedButton == MouseButton.Right)
            return;
        if (p.Y < 22)
        {
            var ms = Math.Max(0, (p.X - HeadW) / zoom + scroll);
            App.Session.SetPlayhead(tl.Id, ms);
            return;
        }
        var layerIndex = (int)Math.Floor((p.Y - 22) / LaneH);
        if (layerIndex < 0 || layerIndex >= tl.Layers.Count) return;
        var layer = tl.Layers[layerIndex];
        if (p.X < HeadW)
        {
            App.Session.Select(SelectionKind.Layer, layer.Id);
            return;
        }
        var msAt = (p.X - HeadW) / zoom + scroll;
        var cue = tl.Cues.LastOrDefault(c => c.LayerId == layer.Id && msAt >= c.Start && msAt <= c.Start + Math.Max(40 / zoom, c.Duration));
        if (cue is not null)
        {
            App.Session.Select(SelectionKind.Cue, cue.Id);
            var x = HeadW + (cue.Start - scroll) * zoom;
            var w = Math.Max(4, cue.Duration * zoom);
            var local = p.X - x;
            _dragEdge = cue.Type == CueType.Marker ? "m" : local < 8 ? "l" : local > w - 8 ? "r" : "m";
            _dragId = cue.Id;
            _dragStartMs = cue.Start;
            _dragDuration = cue.Duration;
            _dragLayerIndex = layerIndex;
            _mouseDown = p;
            CaptureMouse();
        }
        else
        {
            App.Session.Select(SelectionKind.Layer, layer.Id);
            if (App.Session.ClickJumpsToTime)
                App.Session.SetPlayhead(tl.Id, Math.Max(0, msAt));
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        var tl = App.Session.ActiveTimeline;
        if (tl is null) return;
        var p = e.GetPosition(this);
        var zoom = App.Session.TimelineZoom;
        var scroll = App.Session.TimelineScroll;
        var layerIndex = (int)Math.Floor((p.Y - 22) / LaneH);
        Layer? layer = layerIndex >= 0 && layerIndex < tl.Layers.Count ? tl.Layers[layerIndex] : null;
        var msAt = (p.X - HeadW) / zoom + scroll;
        Cue? cue = layer is null ? null : tl.Cues.LastOrDefault(c => c.LayerId == layer.Id && msAt >= c.Start && msAt <= c.Start + Math.Max(40 / zoom, c.Duration));
        var menu = new ContextMenu();
        if (cue is not null)
        {
            App.Session.Select(SelectionKind.Cue, cue.Id);
            menu.Items.Add(Menu("Fade in", () => App.Session.ToggleFade("in")));
            menu.Items.Add(Menu("Fade out", () => App.Session.ToggleFade("out")));
            menu.Items.Add(Menu("Crossfade", () => App.Session.ApplyCrossfade()));
            menu.Items.Add(new Separator());
            menu.Items.Add(Menu("Duplicate", () => App.Session.DuplicateSelected()));
            menu.Items.Add(Menu("Delete", () => App.Session.DeleteSelected()));
        }
        else if (layer is not null)
        {
            App.Session.Select(SelectionKind.Layer, layer.Id);
            menu.Items.Add(Menu("Insert layer below", () => App.Session.InsertLayer(layer.Id)));
            menu.Items.Add(Menu("Add layer", () => App.Session.AddLayer()));
            menu.Items.Add(new Separator());
            menu.Items.Add(Menu(layer.Enabled ? "Disable layer" : "Enable layer", () => App.Session.UpdateLayer(layer.Id, l => l.Enabled = !l.Enabled)));
            menu.Items.Add(Menu(layer.Locked ? "Unlock layer" : "Lock layer", () => App.Session.UpdateLayer(layer.Id, l => l.Locked = !l.Locked)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Menu("Delete layer", () => App.Session.DeleteLayer(layer.Id)));
        }
        else
        {
            menu.Items.Add(Menu("Add layer", () => App.Session.AddLayer()));
            menu.Items.Add(Menu("Loop", () => App.Session.ToggleLoop()));
        }
        menu.IsOpen = true;
        e.Handled = true;
        base.OnMouseRightButtonUp(e);
    }

    static MenuItem Menu(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
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
        var layer = tl.Layers[layerIndex];
        if (layer.Locked)
        {
            App.Session.Log($"Layer \"{layer.Name}\" is locked", "warn");
            return;
        }
        var layerId = layer.Id;
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
        var tl = App.Session.ActiveTimeline;
        if (tl is null) return;
        var zoom = App.Session.TimelineZoom;
        var dx = e.GetPosition(this).X - _mouseDown.X;
        var dms = dx / zoom;
        var anchors = App.Session.Snap ? TimelineMath.TimelineAnchors(tl.Cues, [_dragId], tl.Playhead) : [];
        var threshold = App.Session.Snap ? 18 / zoom : 0;
        if (_dragEdge == "m")
        {
            var start = Math.Max(0, _dragStartMs + dms);
            if (threshold > 0) start = TimelineMath.SnapTime(start, anchors, threshold);
            var dy = e.GetPosition(this).Y - _mouseDown.Y;
            var li = Math.Clamp(_dragLayerIndex + (int)Math.Round(dy / LaneH), 0, tl.Layers.Count - 1);
            var layer = tl.Layers[li];
            App.Session.UpdateCue(_dragId, c =>
            {
                c.Start = Math.Round(start);
                if (!layer.Locked) c.LayerId = layer.Id;
            }, record: false);
        }
        else if (_dragEdge == "r")
        {
            var end = _dragStartMs + _dragDuration + dms;
            if (threshold > 0) end = TimelineMath.SnapTime(end, anchors, threshold);
            App.Session.UpdateCue(_dragId, c =>
            {
                c.Start = Math.Round(_dragStartMs);
                c.Duration = Math.Max(40, end - _dragStartMs);
            }, record: false);
        }
        else
        {
            var start = _dragStartMs + dms;
            if (threshold > 0) start = TimelineMath.SnapTime(start, anchors, threshold);
            start = Math.Max(0, start);
            App.Session.UpdateCue(_dragId, c =>
            {
                c.Start = Math.Round(start);
                c.Duration = Math.Max(40, _dragDuration - (start - _dragStartMs));
            }, record: false);
        }
    }
}
