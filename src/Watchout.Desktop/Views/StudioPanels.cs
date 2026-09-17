using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Stage;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

public class AssetsPanel : UserControl
{
    readonly StackPanel _rows = new();
    readonly Dictionary<string, AssetRow> _byId = [];
    string _fingerprint = "";
    public string? HighlightedId { get; private set; }

    public AssetsPanel()
    {
        var scroll = new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
        };
        Content = scroll;
        Focusable = true;
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDropFiles;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Delete or Key.Back)) return;
            DeleteHighlighted();
            e.Handled = true;
        };
        var menu = new ContextMenu();
        menu.Items.Add(Menu("Place on layer", PlaceHighlighted));
        menu.Items.Add(Menu("Place at end of timeline", PlaceHighlightedAtEnd));
        menu.Items.Add(Menu("Delete asset", DeleteHighlighted));
        ContextMenu = menu;
        App.Session.Changed += () => Dispatcher.BeginInvoke(Reload);
        Loaded += (_, _) => Reload();
    }

    static MenuItem Menu(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = StudioDrag.IsMediaDrag(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    static async void OnDropFiles(object sender, DragEventArgs e)
    {
        var files = StudioDrag.Files(e.Data);
        if (files.Length == 0) return;
        e.Handled = true;
        await MediaLibrary.ImportFilesAsync(files, App.Session);
    }

    string? CurrentAssetId() =>
        HighlightedId
        ?? (App.Session.Selection.Kind == SelectionKind.Asset ? App.Session.Selection.Ids.FirstOrDefault() : null);

    public void PlaceHighlighted()
    {
        var id = CurrentAssetId();
        if (id is null)
        {
            App.Session.Log("Click the clip in Assets, then Place on layer — or drag it onto the timeline", "warn");
            return;
        }
        App.Session.AddCueFromAsset(id);
    }

    public void PlaceHighlightedAtEnd()
    {
        var id = CurrentAssetId();
        if (id is null)
        {
            App.Session.Log("Click the clip in Assets, then Place at end of timeline", "warn");
            return;
        }
        App.Session.AddCueAtEnd(id);
    }

    public void DeleteHighlighted()
    {
        var id = CurrentAssetId();
        if (id is null)
        {
            App.Session.Log("Click the clip in Assets, then Delete", "warn");
            return;
        }
        App.Session.DeleteAsset(id);
        HighlightedId = null;
    }

    public void Reload()
    {
        var show = App.Session.Show;
        var fp = show is null ? "" : string.Join("|", show.Assets.Select(a => a.Id + a.Name + a.Notes + a.Optimized + a.Kind + a.Url + a.Codec));
        if (fp == _fingerprint)
        {
            foreach (var asset in show?.Assets ?? [])
                if (_byId.TryGetValue(asset.Id, out var row)) row.Sync(asset, HighlightedId);
            return;
        }
        _fingerprint = fp;
        _rows.Children.Clear();
        _byId.Clear();
        if (HighlightedId is not null && show?.Assets.All(a => a.Id != HighlightedId) == true)
            HighlightedId = null;
        foreach (var asset in show?.Assets ?? [])
        {
            var row = new AssetRow(asset, OnPick, OnDelete, OnDrag);
            _byId[asset.Id] = row;
            _rows.Children.Add(row);
            row.Sync(asset, HighlightedId);
        }
    }

    void OnPick(string id)
    {
        HighlightedId = id;
        App.Session.Select(SelectionKind.Asset, id);
        Focus();
        foreach (var row in _byId.Values) row.SyncHighlight(id);
    }

    void OnDelete(string id)
    {
        HighlightedId = id;
        App.Session.DeleteAsset(id);
    }

    static void OnDrag(Asset asset, FrameworkElement source)
    {
        source.Dispatcher.BeginInvoke(() =>
        {
            try { DragDrop.DoDragDrop(source, StudioDrag.ForAsset(asset.Id), DragDropEffects.Copy); }
            finally { StudioDrag.AssetId = null; }
        }, DispatcherPriority.Input);
    }

    sealed class AssetRow : Border
    {
        readonly TextBlock _name = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _meta = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        readonly string _id;
        readonly Asset _asset;
        Point _press;
        bool _dragging;

        public AssetRow(Asset asset, Action<string> pick, Action<string> delete, Action<Asset, FrameworkElement> drag)
        {
            _id = asset.Id;
            _asset = asset;
            Margin = new Thickness(6, 4, 6, 0);
            Padding = new Thickness(8, 6, 8, 6);
            CornerRadius = new CornerRadius(3);
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            var root = new DockPanel();
            var del = new Button
            {
                Content = "Delete",
                Style = (Style)Application.Current.FindResource("Wo.Button"),
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            del.Click += (_, e) =>
            {
                e.Handled = true;
                delete(_id);
            };
            DockPanel.SetDock(del, Dock.Right);
            var text = new StackPanel();
            text.Children.Add(_name);
            text.Children.Add(_meta);
            root.Children.Add(del);
            root.Children.Add(text);
            Child = root;
            MouseLeftButtonDown += (_, e) =>
            {
                _press = e.GetPosition(this);
                _dragging = false;
                pick(_id);
                if (e.ClickCount >= 2)
                {
                    App.Session.AddCueFromAsset(_id);
                    e.Handled = true;
                }
            };
            MouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _dragging) return;
                var p = e.GetPosition(this);
                if (Math.Abs(p.X - _press.X) < 6 && Math.Abs(p.Y - _press.Y) < 6) return;
                _dragging = true;
                drag(_asset, this);
            };
            MouseLeftButtonUp += (_, _) => _dragging = false;
            MouseRightButtonDown += (_, e) =>
            {
                pick(_id);
                e.Handled = true;
            };
        }

        public void Sync(Asset asset, string? highlightedId)
        {
            _name.Text = asset.Name;
            _meta.Text = LiveSources.IsNdi(asset)
                ? (LiveSources.IsCaptureUrl(asset.Url) ? "NDI · live — drag onto a layer" : "NDI — drag onto a layer")
                : string.IsNullOrWhiteSpace(asset.Codec) ? asset.Kind.ToString() : asset.Codec;
            SyncHighlight(highlightedId);
        }

        public void SyncHighlight(string? highlightedId)
        {
            var on = highlightedId == _id || (App.Session.Selection.Kind == SelectionKind.Asset && App.Session.Selection.Ids.Contains(_id));
            Background = new SolidColorBrush(on ? Color.FromRgb(42, 36, 24) : Color.FromRgb(22, 22, 22));
            BorderBrush = new SolidColorBrush(on ? Color.FromRgb(245, 166, 35) : Color.FromRgb(48, 48, 48));
            _name.Foreground = (Brush)Application.Current.FindResource("Wo.Text");
            _meta.Foreground = (Brush)Application.Current.FindResource("Wo.Muted");
        }
    }
}

public class TimelinesPanel : UserControl
{
    readonly StackPanel _rows = new();
    readonly Dictionary<string, TimelineRow> _byId = [];
    string _ids = "";

    public TimelinesPanel()
    {
        var root = new DockPanel();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 8) };
        bar.Children.Add(Mini("Add timeline", () => App.Session.AddTimeline()));
        bar.Children.Add(Mini("Delete", () => App.Session.DeleteTimeline()));
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        Content = root;
        ContextMenu = BuildMenu();
        App.Session.Changed += () => Dispatcher.BeginInvoke(Reload);
        App.Session.Clock += () => Dispatcher.BeginInvoke(SyncClocks);
    }

    static ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Add Timeline", () => App.Session.AddTimeline()));
        menu.Items.Add(Item("Loop", () => App.Session.ToggleLoop()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Delete Timeline", () => App.Session.DeleteTimeline(), true));
        return menu;
    }

    static MenuItem Item(string header, Action click, bool danger = false)
    {
        var item = new MenuItem { Header = header };
        if (danger) item.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        item.Click += (_, _) => click();
        return item;
    }

    static Button Mini(string label, Action click)
    {
        var b = new Button { Content = label, Style = (Style)Application.Current.FindResource("Wo.Button"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    public void Reload()
    {
        var show = App.Session.Show;
        var ids = show is null ? "" : string.Join("|", show.Timelines.Select(t => t.Id));
        if (ids != _ids)
        {
            _ids = ids;
            _rows.Children.Clear();
            _byId.Clear();
            if (show is not null)
            {
                foreach (var tl in show.Timelines)
                {
                    var row = new TimelineRow(tl);
                    _byId[tl.Id] = row;
                    _rows.Children.Add(row);
                }
            }
        }
        foreach (var tl in show?.Timelines ?? [])
            if (_byId.TryGetValue(tl.Id, out var row)) row.Sync(tl);
    }

    void SyncClocks()
    {
        var show = App.Session.Show;
        foreach (var tl in show?.Timelines ?? [])
            if (_byId.TryGetValue(tl.Id, out var row)) row.SyncClock(tl);
    }

    sealed class TimelineRow : Border
    {
        readonly TextBlock _name = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _clock = new() { FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
        readonly CheckBox _loop = new() { Content = "Loop", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly string _id;
        bool _syncing;

        public TimelineRow(Timeline tl)
        {
            _id = tl.Id;
            Margin = new Thickness(6, 4, 6, 0);
            Padding = new Thickness(8, 6, 8, 6);
            CornerRadius = new CornerRadius(3);
            BorderThickness = new Thickness(1);
            var root = new DockPanel();
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            tools.Children.Add(_loop);
            tools.Children.Add(Icon("Play", () => App.Session.Play(_id)));
            tools.Children.Add(Icon("Pause", () => App.Session.Pause(_id)));
            tools.Children.Add(Icon("Stop", () => App.Session.Stop(_id)));
            tools.Children.Add(Icon("Del", () => App.Session.DeleteTimeline(_id)));
            DockPanel.SetDock(tools, Dock.Bottom);
            var text = new StackPanel();
            text.Children.Add(_name);
            text.Children.Add(_clock);
            root.Children.Add(tools);
            root.Children.Add(text);
            Child = root;
            _loop.Click += (_, _) =>
            {
                if (_syncing) return;
                App.Session.SetLoop(_id, _loop.IsChecked == true);
            };
            MouseLeftButtonDown += (_, _) =>
            {
                App.Session.SetActiveTimeline(_id);
                App.Session.Select(SelectionKind.Timeline, _id);
            };
            Sync(tl);
        }

        static Button Icon(string label, Action click)
        {
            var b = new Button
            {
                Content = label,
                Style = (Style)Application.Current.FindResource("Wo.Button"),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 4, 6, 0),
                FontSize = 11,
            };
            b.Click += (_, e) =>
            {
                e.Handled = true;
                click();
            };
            return b;
        }

        public void Sync(Timeline tl)
        {
            _syncing = true;
            _name.Text = tl.Name;
            _clock.Text = $"{TimeFormat.FormatPlayTime(tl.Playhead)}  ·  {tl.Playback.ToString().ToUpperInvariant()}";
            _loop.IsChecked = tl.Loop;
            SyncClock(tl);
            var active = App.Session.ActiveTimelineId == tl.Id;
            var selected = App.Session.Selection.Kind == SelectionKind.Timeline && App.Session.Selection.Ids.Contains(tl.Id);
            Background = new SolidColorBrush(active || selected ? Color.FromRgb(42, 36, 24) : Color.FromRgb(22, 22, 22));
            BorderBrush = new SolidColorBrush(active ? Color.FromRgb(245, 166, 35) : Color.FromRgb(48, 48, 48));
            _name.Foreground = (Brush)Application.Current.FindResource("Wo.Text");
            _clock.Foreground = (Brush)Application.Current.FindResource("Wo.Muted");
            _syncing = false;
        }

        public void SyncClock(Timeline tl)
        {
            _clock.Text = $"{TimeFormat.FormatPlayTime(tl.Playhead)}  ·  {tl.Playback.ToString().ToUpperInvariant()}";
        }
    }
}

public class LogPanel : UserControl
{
    readonly ListBox _list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontFamily = new FontFamily("Consolas"), FontSize = 11 };
    int _count = -1;

    public LogPanel()
    {
        Content = _list;
        App.Session.Changed += () => Dispatcher.BeginInvoke(Reload);
    }

    public void Reload()
    {
        if (App.Session.Logs.Count == _count) return;
        _count = App.Session.Logs.Count;
        _list.ItemsSource = App.Session.Logs.Take(80).Select(l => $"[{l.Level}] {l.Message}").ToList();
    }
}

public class LayersPanel : UserControl
{
    readonly StackPanel _rows = new() { Margin = new Thickness(6, 4, 6, 8) };
    readonly TextBlock _caption = new()
    {
        Text = "Layers — 1 in front",
        Foreground = new SolidColorBrush(Color.FromRgb(214, 211, 209)),
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    string _fp = "";

    public LayersPanel()
    {
        var root = new DockPanel();
        var bar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
            Padding = new Thickness(8, 6, 8, 6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(17, 17, 17)),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        bar.Child = _caption;
        DockPanel.SetDock(bar, Dock.Top);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 8) };
        actions.Children.Add(Mini("Add layer", () => App.Session.AddLayer()));
        actions.Children.Add(Mini("Delete", () => App.Session.DeleteLayer()));
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(actions);
        root.Children.Add(new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        Content = root;
        App.Session.Changed += () => Dispatcher.BeginInvoke(Reload);
        Loaded += (_, _) => Reload();
    }

    static Button Mini(string label, Action click)
    {
        var b = new Button
        {
            Content = label,
            Style = (Style)Application.Current.FindResource("Wo.Button"),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 8, 0),
        };
        b.Click += (_, _) => click();
        return b;
    }

    public void Reload()
    {
        var s = App.Session;
        var tl = s.ActiveTimeline;
        var sel = s.Selection.Kind == SelectionKind.Layer ? string.Join(",", s.Selection.Ids) : "";
        var fp = (tl?.Id ?? "") + "|" + sel + "|" + string.Join("|", tl?.Layers.Select(l => $"{l.Id}:{l.Name}:{l.Enabled}:{l.Locked}") ?? []);
        if (fp == _fp && _rows.Children.Count > 0) return;
        _fp = fp;
        _caption.Text = tl is null ? "Layers" : $"Layers — 1 in front";
        _rows.Children.Clear();
        if (tl is null) return;
        for (var i = 0; i < tl.Layers.Count; i++)
        {
            var layer = tl.Layers[i];
            var selected = s.Selection.Kind == SelectionKind.Layer && s.Selection.Ids.Contains(layer.Id);
            _rows.Children.Add(Row(layer, i + 1, selected));
        }
    }

    static Border Row(Layer layer, int number, bool selected)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(8, 4, 6, 4),
            Background = new SolidColorBrush(selected ? Color.FromRgb(48, 42, 30) : Color.FromRgb(36, 36, 36)),
            BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(245, 166, 35) : Color.FromRgb(48, 48, 48)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var num = new TextBlock
        {
            Text = number.ToString(),
            Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        };
        var name = new TextBlock
        {
            Text = layer.Name,
            Foreground = new SolidColorBrush(!layer.Enabled
                ? Color.FromRgb(110, 110, 110)
                : layer.Locked ? Color.FromRgb(210, 185, 120) : Color.FromRgb(232, 230, 227)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 8, 0),
        };
        var lockBtn = IconBtn(LockGlyph(layer.Locked), layer.Locked ? "Unlock layer" : "Lock layer",
            () => App.Session.ToggleLayerLocked(layer.Id));
        var eyeBtn = IconBtn(EyeGlyph(layer.Enabled), layer.Enabled ? "Hide layer" : "Show layer",
            () => App.Session.ToggleLayerVisible(layer.Id));
        Grid.SetColumn(num, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(lockBtn, 2);
        Grid.SetColumn(eyeBtn, 3);
        grid.Children.Add(num);
        grid.Children.Add(name);
        grid.Children.Add(lockBtn);
        grid.Children.Add(eyeBtn);
        row.Child = grid;
        row.MouseLeftButtonDown += (_, _) => App.Session.Select(SelectionKind.Layer, layer.Id);
        return row;
    }

    static Button IconBtn(UIElement glyph, string tip, Action click)
    {
        var b = new Button
        {
            Content = glyph,
            ToolTip = tip,
            Style = (Style)Application.Current.FindResource("Wo.Button"),
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(2, 0, 0, 0),
            Width = 28,
            Height = 24,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        b.Click += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return b;
    }

    static UIElement EyeGlyph(bool visible)
    {
        var color = new SolidColorBrush(visible ? Color.FromRgb(210, 210, 210) : Color.FromRgb(110, 110, 110));
        var canvas = new Canvas { Width = 16, Height = 16, IsHitTestVisible = false };
        var almond = new Ellipse
        {
            Width = 14,
            Height = 8,
            Stroke = color,
            StrokeThickness = 1.2,
            Fill = Brushes.Transparent,
        };
        Canvas.SetLeft(almond, 1);
        Canvas.SetTop(almond, 4);
        canvas.Children.Add(almond);
        var pupil = new Ellipse
        {
            Width = 4,
            Height = 4,
            Fill = visible ? color : Brushes.Transparent,
            Stroke = color,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(pupil, 6);
        Canvas.SetTop(pupil, 6);
        canvas.Children.Add(pupil);
        if (!visible)
        {
            canvas.Children.Add(new Line
            {
                X1 = 2,
                Y1 = 13,
                X2 = 14,
                Y2 = 3,
                Stroke = color,
                StrokeThickness = 1.2,
            });
        }
        return canvas;
    }

    static UIElement LockGlyph(bool locked)
    {
        var color = new SolidColorBrush(locked ? Color.FromRgb(245, 166, 35) : Color.FromRgb(170, 170, 170));
        var canvas = new Canvas { Width = 16, Height = 16, IsHitTestVisible = false };
        var body = new Rectangle
        {
            Width = 10,
            Height = 7,
            RadiusX = 1,
            RadiusY = 1,
            Stroke = color,
            StrokeThickness = 1.2,
            Fill = Brushes.Transparent,
        };
        Canvas.SetLeft(body, 3);
        Canvas.SetTop(body, 8);
        canvas.Children.Add(body);
        var shackle = new System.Windows.Shapes.Path
        {
            Stroke = color,
            StrokeThickness = 1.2,
            Fill = Brushes.Transparent,
            Data = locked
                ? Geometry.Parse("M 5.5,8 C 5.5,5.2 10.5,5.2 10.5,8")
                : Geometry.Parse("M 5.5,8 C 5.5,4.4 12,4.6 12,7"),
        };
        canvas.Children.Add(shackle);
        return canvas;
    }
}

public class DevicesPanel : UserControl
{
    readonly StackPanel _root = new() { Margin = new Thickness(10) };
    string _fp = "";
    bool _building;

    public DevicesPanel()
    {
        Content = new ScrollViewer { Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        App.Session.Changed += () => Dispatcher.BeginInvoke(Reload);
        CaptureHub.Changed += () => Dispatcher.BeginInvoke(Reload);
        NdiHub.Changed += () => Dispatcher.BeginInvoke(Reload);
        Loaded += async (_, _) =>
        {
            await CaptureHub.RefreshAsync();
            _ = NdiHub.RefreshAsync();
            Reload();
        };
    }

    public void Reload()
    {
        var show = App.Session.Show;
        var connected = show?.CaptureDevices.Select(d => d.Signal) ?? [];
        var ndiCams = CaptureHub.Devices.Where(d => NdiNames.LooksLikeNdi(d.Name)).ToList();
        var cards = CaptureHub.Devices.Where(d => !NdiNames.LooksLikeNdi(d.Name)).ToList();
        var audioOutputs = Interop.AudioOutputs.List();
        var fp = string.Join("|", Interop.Monitors.List().Select(s => s.Id + s.Label + s.Width + s.Height + s.PhysicalWidth + s.PhysicalHeight + s.MappedWidth + s.MappedHeight))
                 + App.Session.LiveOutputs.Count
                 + (show?.Displays.Count ?? 0)
                 + string.Join("|", show?.Displays.Select(d => d.Id + d.Name + d.ScreenId + d.Channel + d.Width + d.Height + d.Enabled + d.Role + d.KeyChannel) ?? [])
                 + string.Join("|", audioOutputs.Select(d => d.Id + d.Name))
                 + (show?.AudioDevices.FirstOrDefault()?.Id)
                 + CaptureHub.Generation
                 + CaptureHub.LiveCount
                 + NdiHub.Generation
                 + string.Join("|", CaptureHub.Devices.Select(d => d.Id))
                 + string.Join("|", NdiHub.Sources.Select(s => s.Name + s.Address))
                 + string.Join("|", connected)
                 + string.Join("|", show?.CaptureDevices.Select(d => d.Signal + d.DisplayId) ?? []);
        if (fp == _fp && _root.Children.Count > 0) return;
        _fp = fp;
        _building = true;
        _root.Children.Clear();
        try
        {
        var screens = Interop.Monitors.List();
        var extras = ScreenAssign.OutputPool(screens);
        _root.Children.Add(Header("DISPLAYS"));
        var displayBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 8) };
        displayBar.Children.Add(Btn("Assign screens", () => App.Session.MapScreens(screens), "go", compact: true));
        displayBar.Children.Add(Btn("Output all", () =>
        {
            if (App.Session.Show is { } s) App.Outputs.OpenAll(s.Displays);
        }, "go", compact: true));
        displayBar.Children.Add(Btn("Stop all", () => App.Outputs.CloseAll(), "stop", compact: true));
        _root.Children.Add(displayBar);
        foreach (var display in show?.Displays ?? [])
            _root.Children.Add(DisplayRow(display, screens));

        _root.Children.Add(Header("SHOW OUTPUTS"));
        var monitorBar = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
        var find = Btn("Find screens", () =>
        {
            _fp = "";
            var found = Interop.Monitors.List();
            var n = found.Count;
            App.Session.Log(n <= 1
                ? "Only the Producer laptop is visible. Win+P → Extend so Windows sees the LED wall, TV, or processor (Colorlight, NovaStar, any brand), then Find screens."
                : $"{n} OS screens — extra HDMI/DP outputs are LED walls, TVs, and processors. Pick one on each Display row, or Assign screens to copy the layout onto the Stage.");
            foreach (var screen in found.Where(s => !s.IsPrimary))
                App.Session.Log($"{screen.Label}: {ScreenAssign.ScreenSizeText(screen)}");
            Reload();
        }, "go", compact: true);
        find.HorizontalAlignment = HorizontalAlignment.Right;
        find.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(find, Dock.Right);
        monitorBar.Children.Add(find);
        monitorBar.Children.Add(new TextBlock
        {
            Text = $"{screens.Count} screen(s)",
            Foreground = (Brush)FindResource("Wo.Muted"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _root.Children.Add(monitorBar);
        foreach (var screen in screens)
            _root.Children.Add(MonitorRow(screen, show));
        _root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = (Brush)FindResource("Wo.Muted"),
            Text = extras.Count == 0
                ? "Only the laptop is detected. Win+P → Extend so Windows sees the LED wall, TV, or processor (Colorlight, NovaStar, MCTRL, any brand), then Find screens. Output on the laptop looks blurry and the wall stays black."
                : "Output contain-fits your Stage canvas (1920×1080 or whatever you built) onto the live wall/TV — Colorlight X20 516×430, a 1080 TV, 4K, any processor. Do not leave NVIDIA on a leftover custom from another controller (6720×1344). For this X20 set X20 HDMI to 516×430 (Customize… if it is missing), scale 100%. Assign screens pins the extra HDMI row; Stage size stays yours. Use size is only if you want 1:1 pixels.",
        });

        _root.Children.Add(Header("AUDIO"));
        _root.Children.Add(AudioRow(audioOutputs, show));
        _root.Children.Add(Btn("Include laptop in Stage", () => App.Session.MapScreens(screens, true)));

        _root.Children.Add(Header("NDI"));
        _root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 4),
            Foreground = (Brush)FindResource("Wo.Muted"),
            Text = "Click NDI in Assets (or Browse below) to pick which sources to import — same idea as Resolume. Drag the clip onto a timeline layer. Picture comes from NDI Runtime.",
        });
        _root.Children.Add(Btn("Browse NDI sources", () =>
        {
            var owner = Window.GetWindow(this);
            if (owner is not null) NdiPicker.Open(owner);
        }, "go"));
        if (NdiHub.Sources.Count == 0)
        {
            _root.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = (Brush)FindResource("Wo.Muted"),
                Text = NdiHub.LastError is { } err
                    ? $"No NDI names yet ({err}). Same network, source streaming, then Refresh NDI."
                    : "No NDI names on the LAN yet. Start Resolume NDI or NDI Camera Pro, then Refresh NDI.",
            });
        }
        foreach (var source in NdiHub.Sources)
        {
            var advert = source;
            _root.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(advert.Address) ? advert.Name : $"{advert.Name}  ·  {advert.Address}",
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = (Brush)FindResource("Wo.Text"),
            });
            _root.Children.Add(Btn($"Import {advert.Name} to Assets", () => ImportNdi(advert.Name)));
            _root.Children.Add(Btn($"Place {advert.Name} on a layer", () => PlaceNdi(advert.Name)));
        }
        foreach (var cam in ndiCams)
        {
            var live = connected.Contains(cam.Id);
            _root.Children.Add(new TextBlock
            {
                Text = $"{cam.Name}  ·  NDI Webcam{(live ? "  ·  bound" : "")}",
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = (Brush)FindResource("Wo.Text"),
            });
            var id = cam.Id;
            var name = cam.Name;
            _root.Children.Add(Btn($"Import {name} to Assets", () => App.Session.ImportNdi(name, id)));
            _root.Children.Add(Btn($"Place {name} on a layer", () => App.Session.ConnectNdi(name, id)));
        }
        if (ndiCams.Count == 0)
        {
            _root.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = (Brush)FindResource("Wo.Muted"),
                Text = "No NDI Webcam device yet. Install NDI Tools, open NDI Webcam Input, pick your source, then Refresh NDI.",
            });
        }
        _root.Children.Add(Btn("Refresh NDI", () =>
        {
            _ = CaptureHub.RefreshAsync();
            _ = NdiHub.RefreshAsync();
        }));

        _root.Children.Add(Header("CAPTURE CARDS"));
        _root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 4),
            Foreground = (Brush)FindResource("Wo.Muted"),
            Text = "Play Resolume (or any HDMI/SDI program) into one or many cards. On each card pick Display 1, Display 2, … then Connect. Or click a Stage display and choose the card in Properties. Connect all maps card 1 → Display 1, card 2 → Display 2, and so on.",
        });
        _root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (Brush)FindResource("Wo.Muted"),
            Text = CaptureHub.Devices.Count == 0
                ? "No capture devices yet. Plug in the cards and press Refresh."
                : $"{CaptureHub.LiveCount} live session(s) · {CaptureHub.Devices.Count} device(s) found. Connect all to run them together.",
        });
        foreach (var device in cards)
            _root.Children.Add(CaptureRow(device, show, connected.Contains(device.Id)));
        _root.Children.Add(Btn("Connect all capture cards", () =>
            App.Session.ConnectCaptures(cards.Select(d => (d.Id, d.Name)))));
        _root.Children.Add(Btn("Refresh capture cards", () => _ = CaptureHub.RefreshAsync()));

        _root.Children.Add(Header("CODEC"));
        _root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = (Brush)FindResource("Wo.Muted"),
            Text = "A D3D11 compositor decodes H.264 once (Media Foundation / DXVA) and shares it with Stage and Output. Import MP4/MOV/H.264 directly. Blend Add/Multiply/Screen, crop, wipe, and chroma run on the GPU. HAP, DXV, and ProRes transcode to H.264 MP4 when ffmpeg is installed — never to WebM. Live capture and NDI upload into the same scene.",
        });
        }
        finally
        {
            _building = false;
        }
    }

    UIElement DisplayRow(Display display, IReadOnlyList<OutputScreen> screens)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock
        {
            Text = $"{display.Name}  {display.Width:0}×{display.Height:0}" + (display.Role == DisplayRole.Key ? $"  ·  KEY {Math.Max(1, display.KeyChannel)}" : ""),
            Foreground = (Brush)FindResource("Wo.Text"),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        var displayId = display.Id;
        name.MouseLeftButtonDown += (_, _) => App.Session.Select(SelectionKind.Display, displayId);
        Grid.SetColumn(name, 0);
        var box = new ComboBox
        {
            MinWidth = 188,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        box.Items.Add(Choice(ScreenAssign.AutoChoiceLabel(display.Channel), ScreenAssign.AutoKey(display.Channel)));
        foreach (var screen in screens)
            box.Items.Add(Choice(ScreenAssign.ScreenChoiceLabel(screen), screen.Id));
        var want = ScreenAssign.AssignmentKey(display);
        foreach (ComboBoxItem item in box.Items)
            if (Equals(item.Tag, want)) box.SelectedItem = item;
        if (box.SelectedItem is null) box.SelectedIndex = 0;
        box.SelectionChanged += (_, _) =>
        {
            if (_building) return;
            if (box.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag as string;
                var picked = screens.FirstOrDefault(s => s.Id == tag);
                App.Session.AssignDisplayScreen(displayId, tag, picked);
            }
        };
        Grid.SetColumn(box, 1);
        var output = Btn("Output", () =>
        {
            var live = App.Session.Show?.Displays.FirstOrDefault(d => d.Id == displayId);
            if (live is not null) App.Outputs.Open(live);
        }, "amber", compact: true);
        output.Margin = new Thickness(0);
        Grid.SetColumn(output, 2);
        row.Children.Add(name);
        row.Children.Add(box);
        row.Children.Add(output);
        return row;
    }

    UIElement MonitorRow(OutputScreen screen, Show? show)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = ScreenAssign.ScreenChoiceLabel(screen),
            Foreground = (Brush)FindResource("Wo.Text"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(label, 0);
        var useSize = Btn("Use size", () => App.Session.CopyScreenSizeToDisplay(null, screen), "go", compact: true);
        useSize.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(useSize, 1);
        var here = Btn("Output here", () =>
        {
            var id = App.Session.Selection.Kind == SelectionKind.Display
                ? App.Session.Selection.Ids.FirstOrDefault()
                : show?.Displays.FirstOrDefault()?.Id;
            var display = show?.Displays.FirstOrDefault(d => d.Id == id) ?? show?.Displays.FirstOrDefault();
            if (display is null)
            {
                App.Session.Log("Add a Display on Stage first, then Output here", "warn");
                return;
            }
            App.Session.AssignDisplayScreen(display.Id, screen.Id);
            App.Outputs.Open(display, screen);
        }, "amber", compact: true);
        here.Margin = new Thickness(0);
        Grid.SetColumn(here, 2);
        row.Children.Add(label);
        row.Children.Add(useSize);
        row.Children.Add(here);
        return row;
    }

    UIElement CaptureRow(CaptureDeviceInfo device, Show? show, bool live)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock
        {
            Text = $"{device.Name}  ·  {device.Kind}{(live ? "  ·  live" : "")}",
            Foreground = (Brush)FindResource("Wo.Text"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(name, 0);
        var box = new ComboBox
        {
            MinWidth = 168,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        box.Items.Add(Choice("Auto (next display)", LiveSources.AutoDisplayKey));
        foreach (var display in show?.Displays ?? [])
            box.Items.Add(Choice(LiveSources.DisplayChoiceLabel(display), display.Id));
        var want = show is null ? LiveSources.AutoDisplayKey : LiveSources.CaptureDisplayKey(show, device.Id);
        foreach (ComboBoxItem item in box.Items)
            if (Equals(item.Tag, want)) box.SelectedItem = item;
        if (box.SelectedItem is null) box.SelectedIndex = 0;
        var deviceId = device.Id;
        var deviceName = device.Name;
        box.SelectionChanged += (_, _) =>
        {
            if (_building) return;
            if (box.SelectedItem is not ComboBoxItem item || item.Tag is not string key) return;
            App.Session.AssignCaptureToDisplay(deviceId, key, deviceName);
        };
        Grid.SetColumn(box, 1);
        var connect = Btn(live ? "Reconnect" : "Connect", () =>
        {
            var key = box.SelectedItem is ComboBoxItem item ? item.Tag as string : LiveSources.AutoDisplayKey;
            App.Session.ConnectCapture(deviceId, deviceName, displayId: LiveSources.IsAutoDisplay(key) ? null : key);
        }, live ? "" : "amber", compact: true);
        connect.Margin = new Thickness(0);
        Grid.SetColumn(connect, 2);
        row.Children.Add(name);
        row.Children.Add(box);
        row.Children.Add(connect);
        return row;
    }

    UIElement AudioRow(IReadOnlyList<AudioDevice> outputs, Show? show)
    {
        var selected = AudioAssign.Resolve(outputs, show?.AudioDevices.FirstOrDefault()?.Id);
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var row = new DockPanel();
        var beep = Btn("Test beep", Beep, "amber", compact: true);
        beep.HorizontalAlignment = HorizontalAlignment.Right;
        beep.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(beep, Dock.Right);
        row.Children.Add(beep);
        var box = new ComboBox { MinHeight = 28, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var device in outputs)
            box.Items.Add(Choice(device.Name, device.Id));
        foreach (ComboBoxItem item in box.Items)
            if (Equals(item.Tag, selected.Id)) box.SelectedItem = item;
        if (box.SelectedItem is null) box.SelectedIndex = 0;
        box.SelectionChanged += (_, _) =>
        {
            if (_building) return;
            if (box.SelectedItem is ComboBoxItem item && item.Tag is string id)
                App.Session.SetAudioOutput(AudioAssign.Resolve(outputs, id));
        };
        row.Children.Add(box);
        stack.Children.Add(row);
        stack.Children.Add(new TextBlock
        {
            Text = AudioAssign.StatusLine(selected),
            Foreground = (Brush)FindResource("Wo.Muted"),
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 11,
        });
        return stack;
    }

    ComboBoxItem Choice(string label, string key)
    {
        return new ComboBoxItem
        {
            Content = label,
            Tag = key,
            Foreground = (Brush)FindResource("Wo.Text"),
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
        };
    }

    static UIElement Header(string text) => new TextBlock { Text = text, Foreground = (Brush)Application.Current.FindResource("Wo.Amber"), Margin = new Thickness(0, 12, 0, 4), FontSize = 11 };

    static Button Btn(string label, Action click, string kind = "", bool compact = false)
    {
        var primary = kind is "amber" or "primary";
        var b = new Button
        {
            Content = label,
            Style = (Style)Application.Current.FindResource(primary ? "Wo.Primary" : "Wo.Button"),
            Margin = compact ? new Thickness(0, 0, 8, 0) : new Thickness(0, 6, 0, 0),
            Padding = compact ? new Thickness(10, 4, 10, 4) : new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        if (kind == "go")
        {
            b.Background = new SolidColorBrush(Color.FromRgb(46, 125, 50));
            b.Foreground = Brushes.White;
            b.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 142, 60));
        }
        else if (kind == "stop")
        {
            b.Background = new SolidColorBrush(Color.FromRgb(90, 40, 40));
            b.Foreground = Brushes.White;
            b.BorderBrush = new SolidColorBrush(Color.FromRgb(120, 50, 50));
        }
        b.Click += (_, _) => click();
        return b;
    }

    static string? WebcamId(string sourceName)
    {
        var devices = CaptureHub.Devices.Select(d => (d.Id, d.Name));
        return NdiNames.MatchWebcam(sourceName, devices)?.Id;
    }

    static void ImportNdi(string sourceName, bool announce = true) =>
        App.Session.ImportNdi(sourceName, WebcamId(sourceName), placeOnLayer: false, announce);

    static void PlaceNdi(string sourceName) =>
        App.Session.ConnectNdi(sourceName, WebcamId(sourceName));

    static void Beep()
    {
        var device = App.Session.ActiveAudioDevice;
        try
        {
            System.Media.SystemSounds.Beep.Play();
            App.Session.Log($"Test beep · {device.Name} · {AudioAssign.StatusLine(device)}");
        }
        catch (Exception ex) { App.Session.Log(ex.Message, "error"); }
    }
}

public class PropertiesPanel : UserControl
{
    readonly StackPanel _root = new() { Margin = new Thickness(10) };
    string _fp = "";
    bool _building;

    public PropertiesPanel()
    {
        Content = new ScrollViewer { Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        App.Session.Changed += () => Dispatcher.BeginInvoke(Reload);
    }

    public void Reload()
    {
        var s = App.Session;
        var show = s.Show;
        var extra = "";
        if (show is not null && s.Selection.Ids.Count > 0)
        {
            extra = s.Selection.Kind switch
            {
                SelectionKind.Timeline when show.Timelines.FirstOrDefault(t => s.Selection.Ids.Contains(t.Id)) is { } tl => tl.Loop + tl.Name + tl.Duration + tl.Rate + tl.Enabled,
                SelectionKind.Layer when s.ActiveTimeline?.Layers.FirstOrDefault(l => s.Selection.Ids.Contains(l.Id)) is { } layer => layer.Name + layer.Enabled + layer.Locked,
                SelectionKind.Display when show.Displays.FirstOrDefault(d => s.Selection.Ids.Contains(d.Id)) is { } d =>
                    d.Name + d.Enabled + d.Blend + d.Width + d.Height + d.MaskEnabled + d.MaskUrl
                    + d.Role + d.KeyChannel + d.ColorSpace
                    + LiveSources.CaptureOnDisplay(show, d.Id)
                    + string.Join("|", show.CaptureDevices.Select(c => c.Signal + c.DisplayId))
                    + string.Join("|", show.Nodes.Select(n => n.Id + n.MacAddress)),
                SelectionKind.Cue when show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => s.Selection.Ids.Contains(c.Id)) is { } cue =>
                    cue.Name + cue.AssetId + cue.Speed + cue.WipeCompletion + cue.WipeAngle + cue.WipeFeather
                    + cue.Temperature + cue.Exposure + cue.ChromaKeyEnabled + cue.ChromaKeyColor + s.PickingChroma
                    + cue.Blend + cue.Brightness + cue.Contrast + cue.Saturation + cue.Hue
                    + cue.Position.X + cue.Position.Y + cue.Scale.X + cue.Scale.Y + cue.Opacity
                    + (show.Assets.FirstOrDefault(a => a.Id == cue.AssetId)?.Width)
                    + (show.Assets.FirstOrDefault(a => a.Id == cue.AssetId)?.Height)
                    + show.Prefs.MediaReplaceMode + show.Prefs.AutoStart,
                SelectionKind.Asset when show.Assets.FirstOrDefault(a => s.Selection.Ids.Contains(a.Id)) is { } a =>
                    a.Name + a.Notes + a.ActiveRevisionId + a.Url + a.Revisions.Count,
                _ => "",
            };
        }
        var fp = s.Selection.Kind + string.Join(",", s.Selection.Ids) + extra + (show?.Prefs.ColorSpace) + (show?.Prefs.AutoStart) + s.BlindEdit;
        if (fp == _fp) return;
        _fp = fp;
        _building = true;
        _root.Children.Clear();
        try
        {
        if (show is null) return;
        if (s.Selection.Kind == SelectionKind.Cue)
        {
            var cue = show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => s.Selection.Ids.Contains(c.Id));
            if (cue is null) return;
            var media = show.Assets.FirstOrDefault(a => a.Id == cue.AssetId);
            var pixel = StageGeometry.CuePixelSize(media, cue.Scale);
            Field("Name", cue.Name, v => s.UpdateCue(cue.Id, c => c.Name = v));
            Field("X", cue.Position.X.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Position.X = n); });
            Field("Y", cue.Position.Y.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Position.Y = n); });
            Field("Width", pixel.W.ToString("0"), v => { if (double.TryParse(v, out var n)) s.SetCuePixelSize(cue.Id, width: n); });
            Field("Height", pixel.H.ToString("0"), v => { if (double.TryParse(v, out var n)) s.SetCuePixelSize(cue.Id, height: n); });
            ActionBtn("Fit cue to display", () => s.FitSelectedToDisplay());
            ActionBtn("Fit cue to wall", () => s.FitSelectedToWall());
            Field("Start ms", cue.Start.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Start = n); });
            Field("Duration ms", cue.Duration.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Duration = n); });
            Field("Opacity", cue.Opacity.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Opacity = n); });
            var blendBox = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
            blendBox.Items.Add(new ComboBoxItem { Content = "Blend: Normal", Tag = BlendMode.Normal });
            blendBox.Items.Add(new ComboBoxItem { Content = "Blend: Add", Tag = BlendMode.Add });
            blendBox.Items.Add(new ComboBoxItem { Content = "Blend: Multiply", Tag = BlendMode.Multiply });
            blendBox.Items.Add(new ComboBoxItem { Content = "Blend: Screen", Tag = BlendMode.Screen });
            foreach (ComboBoxItem item in blendBox.Items)
                if (Equals(item.Tag, cue.Blend)) blendBox.SelectedItem = item;
            if (blendBox.SelectedItem is null) blendBox.SelectedIndex = 0;
            var blendCue = cue.Id;
            blendBox.SelectionChanged += (_, _) =>
            {
                if (_building) return;
                if (blendBox.SelectedItem is ComboBoxItem item && item.Tag is BlendMode mode)
                    s.UpdateCue(blendCue, c => c.Blend = mode);
            };
            _root.Children.Add(blendBox);
            Field("Brightness", cue.Brightness.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Brightness = n); });
            Field("Contrast", cue.Contrast.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Contrast = n); });
            Field("Saturation", cue.Saturation.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Saturation = n); });
            Field("Hue", cue.Hue.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Hue = n); });
            Field("Volume", cue.Volume.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Volume = n); });
            Field("Scale X %", cue.Scale.X.ToString("0.##"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Scale.X = n); });
            Field("Scale Y %", cue.Scale.Y.ToString("0.##"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Scale.Y = n); });
            Field("Speed %", (cue.Speed <= 0 ? 100 : cue.Speed).ToString("0.##"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Speed = Math.Max(1, n)); });
            Field("Wipe %", cue.WipeCompletion.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.WipeCompletion = n); });
            Field("Wipe angle", cue.WipeAngle.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.WipeAngle = n); });
            Field("Wipe feather", cue.WipeFeather.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.WipeFeather = n); });
            Field("Temperature", cue.Temperature.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Temperature = n); });
            Field("Exposure EV", cue.Exposure.ToString("0.##"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Exposure = n); });
            Check("Chroma key", cue.ChromaKeyEnabled, v => s.UpdateCue(cue.Id, c => c.ChromaKeyEnabled = v));
            Field("Key color", cue.ChromaKeyColor, v => s.UpdateCue(cue.Id, c => c.ChromaKeyColor = v));
            Field("Key tolerance", cue.ChromaKeyTolerance.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.ChromaKeyTolerance = n); });
            ActionBtn(s.PickingChroma ? "Click Stage for key color…" : "Pick key color on Stage", () => s.BeginPickChroma());
            _root.Children.Add(new TextBlock
            {
                Text = "Media on this cue",
                Foreground = (Brush)FindResource("Wo.Muted"),
                Margin = new Thickness(0, 12, 0, 2),
            });
            var mediaBox = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
            mediaBox.Items.Add(new ComboBoxItem { Content = "Placeholder (empty)", Tag = "" });
            foreach (var asset in show.Assets)
                mediaBox.Items.Add(new ComboBoxItem { Content = asset.Name, Tag = asset.Id });
            foreach (ComboBoxItem item in mediaBox.Items)
                if (Equals(item.Tag, cue.AssetId ?? "")) mediaBox.SelectedItem = item;
            if (mediaBox.SelectedItem is null) mediaBox.SelectedIndex = 0;
            var cueId = cue.Id;
            mediaBox.SelectionChanged += (_, _) =>
            {
                if (_building) return;
                if (mediaBox.SelectedItem is ComboBoxItem item)
                    s.ReplaceCueMedia(cueId, item.Tag as string);
            };
            _root.Children.Add(mediaBox);
            var replaceBox = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
            replaceBox.Items.Add(new ComboBoxItem { Content = "Replace: keep old size", Tag = MediaReplaceMode.KeepOldSize });
            replaceBox.Items.Add(new ComboBoxItem { Content = "Replace: use new size", Tag = MediaReplaceMode.NewSize });
            replaceBox.Items.Add(new ComboBoxItem { Content = "Replace: fit proportionally", Tag = MediaReplaceMode.FitProportionally });
            foreach (ComboBoxItem item in replaceBox.Items)
                if (Equals(item.Tag, show.Prefs.MediaReplaceMode)) replaceBox.SelectedItem = item;
            if (replaceBox.SelectedItem is null) replaceBox.SelectedIndex = 0;
            replaceBox.SelectionChanged += (_, _) =>
            {
                if (_building) return;
                if (replaceBox.SelectedItem is ComboBoxItem item && item.Tag is MediaReplaceMode mode)
                    s.SetMediaReplaceMode(mode);
            };
            _root.Children.Add(replaceBox);
            Check("Muted", cue.Muted == true, v => s.UpdateCue(cue.Id, c => c.Muted = v));
            Check("Fade in", cue.FadeIn, v => s.UpdateCue(cue.Id, c => c.FadeIn = v));
            Check("Fade out", cue.FadeOut, v => s.UpdateCue(cue.Id, c => c.FadeOut = v));
            ActionBtn("Delete cue", () => s.DeleteSelected());
        }
        else if (s.Selection.Kind == SelectionKind.Display)
        {
            var d = show.Displays.FirstOrDefault(x => s.Selection.Ids.Contains(x.Id));
            if (d is null) return;
            _root.Children.Add(new TextBlock { Text = "Display canvas", Foreground = (Brush)FindResource("Wo.Amber"), Margin = new Thickness(0, 0, 0, 4) });
            Field("Name", d.Name, v => s.UpdateDisplay(d.Id, x => x.Name = v));
            Field("Width", d.Width.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateDisplay(d.Id, x => x.Width = n); });
            Field("Height", d.Height.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateDisplay(d.Id, x => x.Height = n); });
            Field("X", d.X.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateDisplay(d.Id, x => x.X = n); });
            Field("Y", d.Y.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateDisplay(d.Id, x => x.Y = n); });
            Field("Channel", d.Channel.ToString(), v => { if (int.TryParse(v, out var n)) s.UpdateDisplay(d.Id, x => x.Channel = n); });
            var roleBox = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
            roleBox.Items.Add(new ComboBoxItem { Content = "Fill (picture)", Tag = DisplayRole.Fill });
            roleBox.Items.Add(new ComboBoxItem { Content = "Key (alpha / luminance out)", Tag = DisplayRole.Key });
            foreach (ComboBoxItem item in roleBox.Items)
                if (Equals(item.Tag, d.Role)) roleBox.SelectedItem = item;
            if (roleBox.SelectedItem is null) roleBox.SelectedIndex = 0;
            var dispId = d.Id;
            roleBox.SelectionChanged += (_, _) =>
            {
                if (_building) return;
                if (roleBox.SelectedItem is ComboBoxItem item && item.Tag is DisplayRole role)
                    s.UpdateDisplay(dispId, x => x.Role = role);
            };
            _root.Children.Add(new TextBlock { Text = "Role", Foreground = (Brush)FindResource("Wo.Muted"), Margin = new Thickness(0, 8, 0, 2) });
            _root.Children.Add(roleBox);
            Field("Key channel", Math.Max(1, d.KeyChannel).ToString(), v =>
            {
                if (int.TryParse(v, out var n)) s.UpdateDisplay(d.Id, x => x.KeyChannel = Math.Clamp(n, 1, 4));
            });
            var colorBox = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
            colorBox.Items.Add(new ComboBoxItem { Content = "Rec.709", Tag = ColorSpaceTag.Rec709 });
            colorBox.Items.Add(new ComboBoxItem { Content = "Rec.2020", Tag = ColorSpaceTag.Rec2020 });
            colorBox.Items.Add(new ComboBoxItem { Content = "HLG", Tag = ColorSpaceTag.Hlg });
            colorBox.Items.Add(new ComboBoxItem { Content = "PQ / HDR10", Tag = ColorSpaceTag.Pq });
            foreach (ComboBoxItem item in colorBox.Items)
                if (Equals(item.Tag, d.ColorSpace)) colorBox.SelectedItem = item;
            if (colorBox.SelectedItem is null) colorBox.SelectedIndex = 0;
            colorBox.SelectionChanged += (_, _) =>
            {
                if (_building) return;
                if (colorBox.SelectedItem is ComboBoxItem item && item.Tag is ColorSpaceTag tag)
                    s.UpdateDisplay(dispId, x => x.ColorSpace = tag);
            };
            _root.Children.Add(new TextBlock { Text = "Color space (tag — 8-bit WPF)", Foreground = (Brush)FindResource("Wo.Muted"), Margin = new Thickness(0, 8, 0, 2) });
            _root.Children.Add(colorBox);
            var node = show.Nodes.FirstOrDefault(n => n.Id == d.NodeId) ?? show.Nodes.FirstOrDefault(n => n.Services.Runner) ?? show.Nodes.FirstOrDefault();
            if (node is not null)
            {
                Field("Wake MAC", node.MacAddress, v => s.UpdateNode(node.Id, n => n.MacAddress = v.Trim()));
                ActionBtn("Wake on LAN", () => s.WakeNode(node.Id));
            }
            Check("Enabled", d.Enabled, v => s.UpdateDisplay(d.Id, x => x.Enabled = v));
            Check("Blend", d.Blend, v => s.UpdateDisplay(d.Id, x => x.Blend = v));
            Check("Image mask", d.MaskEnabled, v => s.UpdateDisplay(d.Id, x => x.MaskEnabled = v));
            Field("Mask image", d.MaskUrl ?? "", v => s.UpdateDisplay(d.Id, x => x.MaskUrl = string.IsNullOrWhiteSpace(v) ? null : v));
            ActionBtn("Browse mask image…", () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files|*.*" };
                if (dlg.ShowDialog() == true)
                    s.UpdateDisplay(d.Id, x => { x.MaskUrl = dlg.FileName; x.MaskEnabled = true; });
            });
            _root.Children.Add(new TextBlock
            {
                Text = "Live capture",
                Foreground = (Brush)FindResource("Wo.Muted"),
                Margin = new Thickness(0, 12, 0, 2),
            });
            var capBox = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
            capBox.Items.Add(new ComboBoxItem { Content = "None", Tag = "" });
            var seen = new HashSet<string>();
            foreach (var card in CaptureHub.Devices.Where(c => !NdiNames.LooksLikeNdi(c.Name)))
            {
                seen.Add(card.Id);
                capBox.Items.Add(new ComboBoxItem { Content = $"{card.Name}  ·  {card.Kind}", Tag = card.Id });
            }
            foreach (var rec in show.CaptureDevices.Where(c => c.Kind != "NDI"))
            {
                if (!seen.Add(rec.Signal)) continue;
                capBox.Items.Add(new ComboBoxItem { Content = rec.Name, Tag = rec.Signal });
            }
            var current = LiveSources.CaptureOnDisplay(show, d.Id) ?? "";
            foreach (ComboBoxItem item in capBox.Items)
                if (Equals(item.Tag, current)) capBox.SelectedItem = item;
            if (capBox.SelectedItem is null) capBox.SelectedIndex = 0;
            var displayId = d.Id;
            capBox.SelectionChanged += (_, _) =>
            {
                if (_building) return;
                if (capBox.SelectedItem is not ComboBoxItem item || item.Tag is not string key || key.Length == 0)
                {
                    _fp = "";
                    Reload();
                    return;
                }
                var label = item.Content?.ToString() ?? key;
                var name = label.Split("  ·  ")[0];
                App.Session.AssignCaptureToDisplay(key, displayId, name);
            };
            _root.Children.Add(capBox);
            _root.Children.Add(new TextBlock
            {
                Text = "Pick a capture card to fill this Stage display. Many cards can each target a different display.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("Wo.Muted"),
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 11,
            });
            ActionBtn("Delete display", () => s.DeleteSelected());
        }
        else if (s.Selection.Kind == SelectionKind.Timeline)
        {
            var tl = show.Timelines.FirstOrDefault(t => s.Selection.Ids.Contains(t.Id)) ?? s.ActiveTimeline;
            if (tl is null) return;
            Field("Name", tl.Name, v => s.UpdateTimeline(tl.Id, t => t.Name = v));
            Field("Duration ms", tl.Duration.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateTimeline(tl.Id, t => t.Duration = n); });
            Field("Rate", tl.Rate.ToString("0.##"), v => { if (double.TryParse(v, out var n)) s.UpdateTimeline(tl.Id, t => t.Rate = n); });
            Check("Loop", tl.Loop, v => s.SetLoop(tl.Id, v));
            Check("Enabled", tl.Enabled, v => s.UpdateTimeline(tl.Id, t => t.Enabled = v));
            ActionBtn("Delete timeline", () => s.DeleteTimeline(tl.Id));
        }
        else if (s.Selection.Kind == SelectionKind.Layer)
        {
            var layer = show.Timelines.SelectMany(t => t.Layers).FirstOrDefault(l => s.Selection.Ids.Contains(l.Id));
            if (layer is null) return;
            Field("Name", layer.Name, v => s.UpdateLayer(layer.Id, l => l.Name = v));
            Check("Visible", layer.Enabled, v => s.UpdateLayer(layer.Id, l => l.Enabled = v));
            Check("Locked", layer.Locked, v => s.UpdateLayer(layer.Id, l => l.Locked = v));
            ActionBtn("Insert layer below", () => s.InsertLayer(layer.Id));
            ActionBtn("Delete layer", () => s.DeleteLayer(layer.Id));
        }
        else if (s.Selection.Kind == SelectionKind.Asset)
        {
            var a = show.Assets.FirstOrDefault(x => s.Selection.Ids.Contains(x.Id));
            if (a is null) return;
            _root.Children.Add(new TextBlock { Text = a.Name, Foreground = (Brush)FindResource("Wo.Text"), FontWeight = FontWeights.SemiBold });
            _root.Children.Add(new TextBlock { Text = a.Notes, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Wo.Muted"), Margin = new Thickness(0, 8, 0, 0) });
            _root.Children.Add(new TextBlock { Text = $"{a.Width:0}×{a.Height:0}  ·  {a.Codec}", Foreground = (Brush)FindResource("Wo.Amber"), Margin = new Thickness(0, 8, 0, 0) });
            if (a.Kind == AssetKind.Composition)
                _root.Children.Add(new TextBlock { Text = $"{a.Children.Count} grouped cue(s) — Assets → Ungroup to explode", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Wo.Muted"), Margin = new Thickness(0, 8, 0, 0) });
            if (a.Revisions.Count > 0)
            {
                _root.Children.Add(new TextBlock { Text = "Revision", Foreground = (Brush)FindResource("Wo.Muted"), Margin = new Thickness(0, 12, 0, 2) });
                var revBox = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
                foreach (var rev in a.Revisions)
                    revBox.Items.Add(new ComboBoxItem { Content = string.IsNullOrEmpty(rev.Notes) ? rev.Id : rev.Notes, Tag = rev.Id });
                foreach (ComboBoxItem item in revBox.Items)
                    if (Equals(item.Tag, a.ActiveRevisionId)) revBox.SelectedItem = item;
                if (revBox.SelectedItem is null && revBox.Items.Count > 0) revBox.SelectedIndex = revBox.Items.Count - 1;
                var assetId = a.Id;
                revBox.SelectionChanged += (_, _) =>
                {
                    if (_building) return;
                    if (revBox.SelectedItem is ComboBoxItem item && item.Tag is string revId)
                        s.ActivateRevision(assetId, revId);
                };
                _root.Children.Add(revBox);
            }
            ActionBtn("Create H.264 version", () => _ = MediaLibrary.CreateVersionAsync(a.Id, s));
            ActionBtn("Delete asset", () => s.DeleteAsset(a.Id));
        }
        else
        {
            _root.Children.Add(new TextBlock { Text = show.Name, Foreground = (Brush)FindResource("Wo.Text"), FontSize = 16, FontWeight = FontWeights.SemiBold });
            Check("Auto-start play when this show opens", show.Prefs.AutoStart, v => s.SetShowAutoStart(v));
            Check("Open last show when WatchMe starts", App.Settings.AutoStartLastShow, v =>
            {
                App.Settings.AutoStartLastShow = v;
                App.PersistSettings();
            });
            var spaceBox = new ComboBox { Margin = new Thickness(0, 8, 0, 4) };
            spaceBox.Items.Add(new ComboBoxItem { Content = "Rec.709", Tag = ColorSpaceTag.Rec709 });
            spaceBox.Items.Add(new ComboBoxItem { Content = "Rec.2020", Tag = ColorSpaceTag.Rec2020 });
            spaceBox.Items.Add(new ComboBoxItem { Content = "HLG", Tag = ColorSpaceTag.Hlg });
            spaceBox.Items.Add(new ComboBoxItem { Content = "PQ / HDR10", Tag = ColorSpaceTag.Pq });
            foreach (ComboBoxItem item in spaceBox.Items)
                if (Equals(item.Tag, show.Prefs.ColorSpace)) spaceBox.SelectedItem = item;
            if (spaceBox.SelectedItem is null) spaceBox.SelectedIndex = 0;
            spaceBox.SelectionChanged += (_, _) =>
            {
                if (_building) return;
                if (spaceBox.SelectedItem is ComboBoxItem item && item.Tag is ColorSpaceTag tag)
                    s.SetColorSpace(tag);
            };
            _root.Children.Add(new TextBlock { Text = "Show color space (tag)", Foreground = (Brush)FindResource("Wo.Muted"), Margin = new Thickness(0, 12, 0, 2) });
            _root.Children.Add(spaceBox);
            _root.Children.Add(new TextBlock
            {
                Text = "Click a display on Stage to edit that canvas. Loop and Delete live on each timeline. Double-click a display (or Edit displays) when media covers it.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("Wo.Muted"),
                Margin = new Thickness(0, 8, 0, 0),
            });
        }
        }
        finally
        {
            _building = false;
        }
    }

    void Field(string label, string value, Action<string> set)
    {
        _root.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("Wo.Muted"), Margin = new Thickness(0, 8, 0, 2) });
        var box = new TextBox { Text = value };
        box.LostFocus += (_, _) => set(box.Text);
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) set(box.Text); };
        _root.Children.Add(box);
    }

    void Check(string label, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 8, 0, 0) };
        box.Click += (_, _) => set(box.IsChecked == true);
        _root.Children.Add(box);
    }

    void ActionBtn(string label, Action click)
    {
        var b = new Button { Content = label, Style = (Style)Application.Current.FindResource("Wo.Button"), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) => click();
        _root.Children.Add(b);
    }
}
