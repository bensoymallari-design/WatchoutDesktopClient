using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Watchout.Core;
using Watchout.Core.Models;
using Watchout.Core.Scheduling;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

public sealed class TimelinePanel : FrameworkElement
{
    const double LaneH = TimelineMath.LaneHeight;
    const double HeadW = TimelineMath.HeaderWidth;
    const double RulerH = TimelineMath.RulerHeight;
    string? _dragId;
    double _dragStartMs;
    double _dragDuration;
    string _dragEdge = "m";
    int _dragLayerIndex;
    Point _mouseDown;
    bool _panning;
    double _panScroll;
    double _panLayer;

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
        App.Session.Clock += () => Dispatcher.BeginInvoke(InvalidateVisual, DispatcherPriority.Render);
        App.Session.TimelineViewChanged += () => Dispatcher.BeginInvoke(InvalidateVisual);
        SizeChanged += (_, _) => App.Session.ReportTimelineView(ActualWidth, ActualHeight);
        Loaded += (_, _) => App.Session.ReportTimelineView(ActualWidth, ActualHeight);
        MouseLeftButtonDown += OnDown;
        MouseLeftButtonUp += OnUp;
        MouseDown += OnAnyDown;
        MouseUp += OnAnyUp;
        MouseMove += OnMove;
        MouseWheel += OnWheel;
    }

    public void Reload() => InvalidateVisual();

    public void FitToMedia()
    {
        if (!App.Session.FitTimelineToMedia()) return;
        var tl = App.Session.ActiveTimeline;
        if (tl is null || tl.Duration <= 0) return;
        var width = ActualWidth > 80 ? ActualWidth : 800;
        App.Session.SetTimelineZoom(TimelineMath.FitZoom(tl.Duration, width, HeadW));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 22, 22)), null, new Rect(0, 0, w, h));
        var tl = App.Session.ActiveTimeline;
        if (tl is null) return;
        var zoom = App.Session.TimelineZoom;
        var scroll = App.Session.TimelineScroll;
        var layerScroll = App.Session.TimelineLayerScroll;
        var xOf = (double ms) => HeadW + (ms - scroll) * zoom;
        var timeW = Math.Max(0, w - HeadW);
        var lanesH = Math.Max(0, h - RulerH);
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(58, 58, 58)), 1);
        var lanePen = new Pen(new SolidColorBrush(Color.FromRgb(40, 40, 40)), 1);
        var step = NiceStep(80 / zoom);

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(32, 32, 32)), null, new Rect(0, 0, HeadW, h));

        dc.PushClip(new RectangleGeometry(new Rect(HeadW, RulerH, timeW, lanesH)));
        for (double t = 0; t <= tl.Duration; t += step)
        {
            var gx = xOf(t);
            if (gx < HeadW || gx > w) continue;
            dc.DrawLine(gridPen, new Point(gx, RulerH), new Point(gx, h));
        }
        var y = RulerH - layerScroll;
        foreach (var layer in tl.Layers)
        {
            if (y + LaneH >= RulerH && y <= h)
            {
                if (!layer.Enabled)
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, new Rect(HeadW, y, timeW, LaneH));
                dc.DrawLine(lanePen, new Point(HeadW, y + LaneH), new Point(w, y + LaneH));
                if (layer.Enabled)
                {
                    foreach (var cue in tl.Cues.Where(c => c.LayerId == layer.Id))
                    {
                        var x = xOf(cue.Start);
                        var cw = Math.Max(4, cue.Duration * zoom);
                        if (TimelineMath.ClipCueBar(x, cw, HeadW, w) is not { } bar) continue;
                        var selected = App.Session.Selection.Kind == SelectionKind.Cue && App.Session.Selection.Ids.Contains(cue.Id);
                        var fill = BrushFrom(cue.Color);
                        dc.DrawRectangle(fill, new Pen(selected ? new SolidColorBrush(Color.FromRgb(245, 166, 35)) : Brushes.Transparent, 2),
                            new Rect(bar.X, y + 3, bar.W, LaneH - 6));
                        DrawTransitionWedge(dc, x, cw, y, start: true, cue.FadeInDuration * zoom, CueTransitions.ResolvedIn(cue));
                        DrawTransitionWedge(dc, x, cw, y, start: false, cue.FadeOutDuration * zoom, CueTransitions.ResolvedOut(cue));
                        var titleX = bar.X + 4;
                        var titleW = bar.W - 8;
                        if (titleW > 8)
                        {
                            var title = new FormattedText(cue.Name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                new Typeface("Segoe UI"), 11, Brushes.White, 1.25);
                            title.MaxTextWidth = titleW;
                            title.MaxTextHeight = 16;
                            dc.DrawText(title, new Point(titleX, y + 6));
                        }
                    }
                }
            }
            y += LaneH;
        }
        dc.Pop();

        dc.PushClip(new RectangleGeometry(new Rect(0, RulerH, HeadW, lanesH)));
        y = RulerH - layerScroll;
        var layerIndex = 0;
        foreach (var layer in tl.Layers)
        {
            if (y + LaneH >= RulerH && y <= h)
                DrawLayerHeader(dc, layer, layerIndex, y, lanePen);
            y += LaneH;
            layerIndex++;
        }
        dc.Pop();

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 28, 28)), null, new Rect(HeadW, 0, timeW, RulerH));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(26, 26, 26)), null, new Rect(0, 0, HeadW, RulerH));
        dc.PushClip(new RectangleGeometry(new Rect(HeadW, 0, timeW, RulerH)));
        for (double t = 0; t <= tl.Duration; t += step)
        {
            var x = xOf(t);
            if (x < HeadW || x > w) continue;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(70, 70, 70)), 1), new Point(x, 0), new Point(x, RulerH));
            var label = new FormattedText(TimeFormat.FormatMs(t)[3..], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, new SolidColorBrush(Color.FromRgb(160, 160, 160)), 1.25);
            dc.DrawText(label, new Point(x + 4, 4));
        }
        dc.Pop();

        var px = xOf(tl.Playhead);
        if (px >= HeadW && px <= w)
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

    static void DrawLayerHeader(DrawingContext dc, Layer layer, int index, double y, Pen lanePen)
    {
        var nameBrush = !layer.Enabled
            ? new SolidColorBrush(Color.FromRgb(90, 90, 90))
            : layer.Locked
                ? new SolidColorBrush(Color.FromRgb(160, 140, 90))
                : Brushes.White;
        var selectedLayer = App.Session.Selection.Kind == SelectionKind.Layer && App.Session.Selection.Ids.Contains(layer.Id);
        if (selectedLayer)
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(42, 36, 24)), null, new Rect(0, y, HeadW, LaneH));
        var num = new FormattedText((index + 1).ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, new SolidColorBrush(Color.FromRgb(140, 140, 140)), 1.25);
        dc.DrawText(num, new Point(4, y + 7));
        var lockLeft = TimelineMath.LayerLockLeft();
        var name = new FormattedText(layer.Name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, nameBrush, 1.25);
        name.MaxTextWidth = Math.Max(24, lockLeft - 22);
        name.MaxTextHeight = LaneH - 4;
        dc.DrawText(name, new Point(20, y + 6));
        var iconY = y + (LaneH - TimelineMath.LayerIconSize) / 2;
        DrawLock(dc, lockLeft, iconY, layer.Locked);
        DrawEye(dc, TimelineMath.LayerEyeLeft(), iconY, layer.Enabled);
        dc.DrawLine(lanePen, new Point(0, y + LaneH), new Point(HeadW, y + LaneH));
    }

    static void DrawEye(DrawingContext dc, double x, double y, bool visible)
    {
        var cx = x + 8;
        var cy = y + 8;
        var brush = new SolidColorBrush(visible ? Color.FromRgb(210, 210, 210) : Color.FromRgb(90, 90, 90));
        var pen = new Pen(brush, 1.2);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(cx - 7, cy), false, true);
            g.QuadraticBezierTo(new Point(cx, cy - 5), new Point(cx + 7, cy), true, false);
            g.QuadraticBezierTo(new Point(cx, cy + 5), new Point(cx - 7, cy), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
        dc.DrawEllipse(visible ? brush : Brushes.Transparent, pen, new Point(cx, cy), 2.2, 2.2);
        if (!visible)
            dc.DrawLine(pen, new Point(cx - 6, cy + 5), new Point(cx + 6, cy - 5));
    }

    static void DrawLock(DrawingContext dc, double x, double y, bool locked)
    {
        var brush = new SolidColorBrush(locked ? Color.FromRgb(245, 166, 35) : Color.FromRgb(170, 170, 170));
        var pen = new Pen(brush, 1.2);
        dc.DrawRoundedRectangle(null, pen, new Rect(x + 3.5, y + 7, 9, 7), 1, 1);
        var shackle = new StreamGeometry();
        using (var g = shackle.Open())
        {
            if (locked)
            {
                g.BeginFigure(new Point(x + 5.5, y + 7.5), false, false);
                g.ArcTo(new Point(x + 10.5, y + 7.5), new Size(2.5, 3.2), 0, false, SweepDirection.Clockwise, true, false);
            }
            else
            {
                g.BeginFigure(new Point(x + 5.5, y + 7.5), false, false);
                g.ArcTo(new Point(x + 11.5, y + 4), new Size(3, 3.4), 0, false, SweepDirection.Clockwise, true, false);
            }
        }
        shackle.Freeze();
        dc.DrawGeometry(null, pen, shackle);
    }

    int LayerIndexAt(Point p)
    {
        var y = p.Y - RulerH + App.Session.TimelineLayerScroll;
        return (int)Math.Floor(y / LaneH);
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var notches = e.Delta / 120.0;
        var p = e.GetPosition(this);
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            var zoom = App.Session.TimelineZoom;
            var ms = (p.X - HeadW) / zoom + App.Session.TimelineScroll;
            App.Session.SetTimelineZoom(zoom * (e.Delta > 0 ? 1.15 : 0.87), ms, p.X);
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            App.Session.SetTimelineLayerScroll(App.Session.TimelineLayerScroll - notches * LaneH * 2);
            return;
        }
        var dms = notches * (80 / Math.Max(0.0001, App.Session.TimelineZoom));
        App.Session.SetTimelineScroll(App.Session.TimelineScroll - dms);
    }

    void OnAnyDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        Focus();
        _panning = true;
        _mouseDown = e.GetPosition(this);
        _panScroll = App.Session.TimelineScroll;
        _panLayer = App.Session.TimelineLayerScroll;
        CaptureMouse();
        e.Handled = true;
    }

    void OnAnyUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle && !_panning) return;
        _panning = false;
        if (_dragId is null) ReleaseMouseCapture();
        e.Handled = true;
    }

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        _dragId = null;
        if (!_panning) ReleaseMouseCapture();
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
        if (p.Y < RulerH)
        {
            var ms = Math.Max(0, (p.X - HeadW) / zoom + scroll);
            App.Session.SetPlayhead(tl.Id, ms);
            return;
        }
        var layerIndex = LayerIndexAt(p);
        if (layerIndex < 0 || layerIndex >= tl.Layers.Count) return;
        var layer = tl.Layers[layerIndex];
        if (p.X < HeadW)
        {
            switch (TimelineMath.HitLayerHeader(p.X))
            {
                case TimelineMath.LayerHeaderPart.Eye:
                    App.Session.ToggleLayerVisible(layer.Id);
                    break;
                case TimelineMath.LayerHeaderPart.Lock:
                    App.Session.ToggleLayerLocked(layer.Id);
                    break;
                default:
                    App.Session.Select(SelectionKind.Layer, layer.Id);
                    break;
            }
            return;
        }
        var msAt = (p.X - HeadW) / zoom + scroll;
        var cue = layer.Enabled
            ? tl.Cues.LastOrDefault(c => c.LayerId == layer.Id && msAt >= c.Start && msAt <= c.Start + Math.Max(40 / zoom, c.Duration))
            : null;
        if (cue is not null)
        {
            App.Session.Select(SelectionKind.Cue, cue.Id);
            if (layer.Locked) return;
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
        var layerIndex = LayerIndexAt(p);
        Layer? layer = layerIndex >= 0 && layerIndex < tl.Layers.Count ? tl.Layers[layerIndex] : null;
        var msAt = (p.X - HeadW) / zoom + scroll;
        Cue? cue = layer is null ? null : tl.Cues.LastOrDefault(c => c.LayerId == layer.Id && msAt >= c.Start && msAt <= c.Start + Math.Max(40 / zoom, c.Duration));
        var menu = new ContextMenu();
        if (cue is not null)
        {
            App.Session.Select(SelectionKind.Cue, cue.Id);
            var x = HeadW + (cue.Start - scroll) * zoom;
            var w = Math.Max(4, cue.Duration * zoom);
            var part = TimelineMath.HitCueBar(p.X - x, w);
            var startFilter = CueTransitions.ResolvedIn(cue);
            var endFilter = CueTransitions.ResolvedOut(cue);
            if (part is TimelineMath.CueBarPart.Start or TimelineMath.CueBarPart.Body)
                menu.Items.Add(TransitionMenu("Start effect", "in", startFilter));
            if (part is TimelineMath.CueBarPart.End or TimelineMath.CueBarPart.Body)
                menu.Items.Add(TransitionMenu("End effect", "out", endFilter));
            menu.Items.Add(Menu("Crossfade", () => App.Session.ApplyCrossfade()));
            menu.Items.Add(new Separator());
            menu.Items.Add(Menu("Duplicate", () => App.Session.DuplicateSelected()));
            menu.Items.Add(Menu("Group selected", () => App.Session.GroupSelectedCues()));
            menu.Items.Add(Menu("Ungroup", () => App.Session.UngroupSelected()));
            menu.Items.Add(Menu("Delete", () => App.Session.DeleteSelected()));
        }
        else if (layer is not null)
        {
            App.Session.Select(SelectionKind.Layer, layer.Id);
            menu.Items.Add(Menu("Add placeholder cue", () => App.Session.AddPlaceholderCue(layer.Id, msAt)));
            menu.Items.Add(Menu("Insert layer below", () => App.Session.InsertLayer(layer.Id)));
            menu.Items.Add(Menu("Add layer", () => App.Session.AddLayer()));
            menu.Items.Add(new Separator());
            menu.Items.Add(Menu(layer.Enabled ? "Hide layer" : "Show layer", () => App.Session.ToggleLayerVisible(layer.Id)));
            menu.Items.Add(Menu(layer.Locked ? "Unlock layer" : "Lock layer", () => App.Session.ToggleLayerLocked(layer.Id)));
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

    static MenuItem CheckMenu(string header, bool on, Action click)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = on };
        item.Click += (_, _) => click();
        return item;
    }

    static MenuItem TransitionMenu(string title, string which, TransitionFilter current)
    {
        var sub = new MenuItem { Header = title };
        foreach (var (filter, name) in CueTransitions.Filters)
        {
            var f = filter;
            var label = f == TransitionFilter.None
                ? "None"
                : which == "in" ? $"{name} in" : $"{name} out";
            sub.Items.Add(CheckMenu(label, current == f, () => App.Session.SetCueTransition(which, f, toggleSame: true)));
        }
        var dur = new MenuItem { Header = "Duration" };
        foreach (var ms in new[] { 250d, 500, 1000, 2000, 4000 })
        {
            var d = ms;
            dur.Items.Add(Menu($"{ms / 1000:0.##} s", () => App.Session.SetCueTransitionDuration(which, d)));
        }
        sub.Items.Add(new Separator());
        sub.Items.Add(dur);
        return sub;
    }

    static void DrawTransitionWedge(DrawingContext dc, double x, double cw, double y, bool start, double durPx, TransitionFilter filter)
    {
        if (filter == TransitionFilter.None) return;
        var w = Math.Min(cw, Math.Max(6, durPx));
        if (w < 2) return;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            if (start)
            {
                ctx.BeginFigure(new Point(x, y + 3), true, true);
                ctx.LineTo(new Point(x + w, y + 3), true, false);
                ctx.LineTo(new Point(x, y + LaneH - 3), true, false);
            }
            else
            {
                ctx.BeginFigure(new Point(x + cw, y + 3), true, true);
                ctx.LineTo(new Point(x + cw, y + LaneH - 3), true, false);
                ctx.LineTo(new Point(x + cw - w, y + LaneH - 3), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), null, geo);
        var mark = CueTransitions.Mark(filter);
        if (string.IsNullOrEmpty(mark) || cw < 18) return;
        var text = new FormattedText(mark, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 9, new SolidColorBrush(Color.FromRgb(245, 166, 35)), 1.25);
        var tx = start ? x + 3 : x + cw - text.Width - 3;
        dc.DrawText(text, new Point(tx, y + 4));
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
        var layerIndex = Math.Clamp(LayerIndexAt(p), 0, Math.Max(0, tl.Layers.Count - 1));
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
        if (_panning)
        {
            var p = e.GetPosition(this);
            var zoom = Math.Max(0.0001, App.Session.TimelineZoom);
            App.Session.SetTimelineScroll(_panScroll - (p.X - _mouseDown.X) / zoom);
            App.Session.SetTimelineLayerScroll(_panLayer - (p.Y - _mouseDown.Y));
            return;
        }
        if (_dragId is null || e.LeftButton != MouseButtonState.Pressed) return;
        var tl = App.Session.ActiveTimeline;
        if (tl is null || App.Session.CueLayerLocked(_dragId)) return;
        var zoomDrag = App.Session.TimelineZoom;
        var dx = e.GetPosition(this).X - _mouseDown.X;
        var dms = dx / zoomDrag;
        var anchors = App.Session.Snap ? TimelineMath.TimelineAnchors(tl.Cues, [_dragId], tl.Playhead) : [];
        var threshold = App.Session.Snap ? 18 / zoomDrag : 0;
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
