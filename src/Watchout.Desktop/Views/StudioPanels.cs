using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        var fp = string.Join("|", Interop.Monitors.List().Select(s => s.Id + s.Width + s.Height))
                 + App.Session.LiveOutputs.Count
                 + (show?.Displays.Count ?? 0)
                 + string.Join("|", show?.Displays.Select(d => d.Id + d.Name + d.ScreenId + d.Channel + d.Width + d.Height + d.Enabled) ?? [])
                 + string.Join("|", audioOutputs.Select(d => d.Id + d.Name))
                 + (show?.AudioDevices.FirstOrDefault()?.Id)
                 + CaptureHub.Generation
                 + CaptureHub.LiveCount
                 + NdiHub.Generation
                 + string.Join("|", CaptureHub.Devices.Select(d => d.Id))
                 + string.Join("|", NdiHub.Sources.Select(s => s.Name + s.Address))
                 + string.Join("|", connected);
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

        _root.Children.Add(Header("MONITORS"));
        var monitorBar = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
        var find = Btn("Find screens", () =>
        {
            _fp = "";
            var n = Interop.Monitors.List().Count;
            App.Session.Log(n <= 1
                ? "Only the Producer screen is visible. Win+P → Extend, then Find screens again for MCTRL / NovaStar / HDMI."
                : $"{n} OS screens — pick one on each Display row, or Assign screens to copy the layout onto the Stage.");
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
                ? "Only one OS screen detected. Extend HDMI / MCTRL4K / NovaStar (Win+P), then Find screens."
                : "Controllers and TVs show up as OS screens. Assign screens copies their size and desktop layout onto the Stage. On each Display row pick which controller it uses. Select a Display, then Use size or Output here.",
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
            Text = "Import an NDI name into Assets, then drag that clip onto a timeline layer — same as a video. Picture still needs NDI Tools → NDI Webcam Input.",
        });
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
        _root.Children.Add(Btn("Import all NDI names to Assets", () =>
        {
            if (NdiHub.Sources.Count == 0 && ndiCams.Count == 0)
            {
                App.Session.Log("No NDI source yet. Refresh NDI, or open NDI Webcam Input.", "warn");
                return;
            }
            foreach (var source in NdiHub.Sources)
                ImportNdi(source.Name, announce: source == NdiHub.Sources[^1] && ndiCams.Count == 0);
            foreach (var cam in ndiCams)
                App.Session.ImportNdi(cam.Name, cam.Id, announce: cam == ndiCams[^1]);
        }));

        _root.Children.Add(Header("CAPTURE CARDS"));
        _root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 4),
            Foreground = (Brush)FindResource("Wo.Muted"),
            Text = "Play Resolume (or any HDMI/SDI program) into one or many cards on this PC. Each connected card gets its own Stage display.",
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
        {
            var live = connected.Contains(device.Id);
            _root.Children.Add(new TextBlock
            {
                Text = $"{device.Name}  ·  {device.Kind}{(live ? "  ·  live" : "")}",
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = (Brush)FindResource("Wo.Text"),
            });
            var id = device.Id;
            var name = device.Name;
            _root.Children.Add(Btn(live ? $"Reconnect {device.Name}" : $"Connect {device.Name}", () => App.Session.ConnectCapture(id, name)));
        }
        _root.Children.Add(Btn("Connect all capture cards", () =>
            App.Session.ConnectCaptures(cards.Select(d => (d.Id, d.Name)))));
        _root.Children.Add(Btn("Refresh capture cards", () => _ = CaptureHub.RefreshAsync()));

        _root.Children.Add(Header("CODEC"));
        _root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = (Brush)FindResource("Wo.Muted"),
            Text = "Outputs decode H.264 with Windows Media Foundation / DXVA. Import MP4/MOV/H.264 directly. HAP, DXV, and ProRes transcode to H.264 MP4 when ffmpeg is installed — never to WebM. Live capture plays frames from the card, not a file.",
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
            Text = $"{display.Name}  {display.Width:0}×{display.Height:0}",
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
                App.Session.AssignDisplayScreen(displayId, item.Tag as string);
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
            Text = $"{screen.Label}{(screen.IsPrimary ? " · Producer" : "")}  {ScreenAssign.ScreenWidth(screen)}×{ScreenAssign.ScreenHeight(screen)}",
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
                SelectionKind.Display when show.Displays.FirstOrDefault(d => s.Selection.Ids.Contains(d.Id)) is { } d => d.Name + d.Enabled + d.Blend,
                SelectionKind.Asset when show.Assets.FirstOrDefault(a => s.Selection.Ids.Contains(a.Id)) is { } a => a.Name + a.Notes,
                _ => "",
            };
        }
        var fp = s.Selection.Kind + string.Join(",", s.Selection.Ids) + extra;
        if (fp == _fp) return;
        _fp = fp;
        _root.Children.Clear();
        if (show is null) return;
        if (s.Selection.Kind == SelectionKind.Cue)
        {
            var cue = show.Timelines.SelectMany(t => t.Cues).FirstOrDefault(c => s.Selection.Ids.Contains(c.Id));
            if (cue is null) return;
            Field("Name", cue.Name, v => s.UpdateCue(cue.Id, c => c.Name = v));
            Field("Start ms", cue.Start.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Start = n); });
            Field("Duration ms", cue.Duration.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Duration = n); });
            Field("Opacity", cue.Opacity.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Opacity = n); });
            Field("Volume", cue.Volume.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Volume = n); });
            Field("X", cue.Position.X.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Position.X = n); });
            Field("Y", cue.Position.Y.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Position.Y = n); });
            Field("Scale X %", cue.Scale.X.ToString("0.##"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Scale.X = n); });
            Field("Scale Y %", cue.Scale.Y.ToString("0.##"), v => { if (double.TryParse(v, out var n)) s.UpdateCue(cue.Id, c => c.Scale.Y = n); });
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
            Check("Enabled", d.Enabled, v => s.UpdateDisplay(d.Id, x => x.Enabled = v));
            Check("Blend", d.Blend, v => s.UpdateDisplay(d.Id, x => x.Blend = v));
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
            Check("Enabled", layer.Enabled, v => s.UpdateLayer(layer.Id, l => l.Enabled = v));
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
            ActionBtn("Delete asset", () => s.DeleteAsset(a.Id));
        }
        else
        {
            _root.Children.Add(new TextBlock { Text = show.Name, Foreground = (Brush)FindResource("Wo.Text"), FontSize = 16, FontWeight = FontWeights.SemiBold });
            _root.Children.Add(new TextBlock
            {
                Text = "Click a display on Stage to edit that canvas. Loop and Delete live on each timeline. Double-click a display (or Edit displays) when media covers it.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("Wo.Muted"),
                Margin = new Thickness(0, 8, 0, 0),
            });
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
