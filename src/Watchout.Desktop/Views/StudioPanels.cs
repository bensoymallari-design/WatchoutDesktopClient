using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Watchout.Core.Models;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

public class AssetsPanel : UserControl
{
    readonly ListBox _list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    string _fingerprint = "";

    public AssetsPanel()
    {
        Content = _list;
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is Asset a)
                App.Session.Select(SelectionKind.Asset, a.Id);
        };
        _list.MouseDoubleClick += async (_, _) =>
        {
            if (_list.SelectedItem is Asset a)
                App.Session.AddCueFromAsset(a.Id);
            else
                await MainWindow.ImportMediaAsync();
        };
        AllowDrop = true;
        Drop += async (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
                await MediaLibrary.ImportFilesAsync(files, App.Session);
        };
        App.Session.Changed += () => Dispatcher.BeginInvoke(Reload);
        Loaded += (_, _) => Reload();
    }

    public void Reload()
    {
        var show = App.Session.Show;
        var fp = show is null ? "" : string.Join("|", show.Assets.Select(a => a.Id + a.Notes + a.Optimized));
        if (fp == _fingerprint) return;
        _fingerprint = fp;
        _list.ItemsSource = show?.Assets.ToList();
        _list.DisplayMemberPath = "Name";
    }
}

public class TimelinesPanel : UserControl
{
    readonly ListBox _list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    string _fp = "";

    public TimelinesPanel()
    {
        var root = new DockPanel();
        var add = new Button { Content = "Add timeline", Style = (Style)Application.Current.FindResource("Wo.Button"), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => App.Session.AddTimeline();
        DockPanel.SetDock(add, Dock.Bottom);
        root.Children.Add(add);
        root.Children.Add(_list);
        Content = root;
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is Timeline tl)
                App.Session.SetActiveTimeline(tl.Id);
        };
        App.Session.Changed += () => Dispatcher.BeginInvoke(Reload);
    }

    public void Reload()
    {
        var show = App.Session.Show;
        var fp = show is null ? "" : string.Join("|", show.Timelines.Select(t => t.Id + t.Name + t.Loop));
        if (fp == _fp && _list.Items.Count > 0) return;
        _fp = fp;
        _list.ItemsSource = show?.Timelines.ToList();
        _list.DisplayMemberPath = "Name";
        if (App.Session.ActiveTimeline is { } cur)
            _list.SelectedItem = show?.Timelines.FirstOrDefault(t => t.Id == cur.Id);
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

    public DevicesPanel()
    {
        Content = new ScrollViewer { Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        App.Session.Changed += () => Dispatcher.BeginInvoke(Reload);
        CaptureHub.Changed += () => Dispatcher.BeginInvoke(Reload);
        Loaded += async (_, _) =>
        {
            await CaptureHub.RefreshAsync();
            Reload();
        };
    }

    public void Reload()
    {
        var show = App.Session.Show;
        var connected = show?.CaptureDevices.Select(d => d.Signal) ?? [];
        var fp = string.Join("|", Interop.Monitors.List().Select(s => s.Id))
                 + App.Session.LiveOutputs.Count
                 + (show?.Displays.Count ?? 0)
                 + CaptureHub.Generation
                 + string.Join("|", CaptureHub.Devices.Select(d => d.Id))
                 + string.Join("|", connected);
        if (fp == _fp && _root.Children.Count > 0) return;
        _fp = fp;
        _root.Children.Clear();
        _root.Children.Add(Header("SCREENS"));
        foreach (var screen in Interop.Monitors.List())
        {
            _root.Children.Add(new TextBlock
            {
                Text = $"{screen.Label}  {screen.Width}×{screen.Height}{(screen.IsPrimary ? "  ·  Producer" : "  ·  HDMI")}",
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = (Brush)FindResource("Wo.Text"),
            });
        }
        _root.Children.Add(Btn("Map extra monitors to Stage", () => App.Session.MapScreens(Interop.Monitors.List())));
        _root.Children.Add(Btn("Include laptop in Stage", () => App.Session.MapScreens(Interop.Monitors.List(), true)));
        _root.Children.Add(Btn("Output all displays", () =>
        {
            if (App.Session.Show is { } s) App.Outputs.OpenAll(s.Displays);
        }));
        _root.Children.Add(Btn("Close outputs", () => App.Outputs.CloseAll()));
        _root.Children.Add(Btn("Test beep", Beep));

        _root.Children.Add(Header("CAPTURE CARDS"));
        _root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 4),
            Foreground = (Brush)FindResource("Wo.Muted"),
            Text = "Play Resolume (or any HDMI/SDI program) into a capture card on this PC, then Connect. NDI Webcam Input also appears here.",
        });
        if (CaptureHub.Devices.Count == 0)
        {
            _root.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = (Brush)FindResource("Wo.Muted"),
                Text = "No capture devices yet. Plug in the card and press Refresh.",
            });
        }
        foreach (var device in CaptureHub.Devices)
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

    static UIElement Header(string text) => new TextBlock { Text = text, Foreground = (Brush)Application.Current.FindResource("Wo.Amber"), Margin = new Thickness(0, 12, 0, 4), FontSize = 11 };

    static Button Btn(string label, Action click)
    {
        var b = new Button { Content = label, Style = (Style)Application.Current.FindResource("Wo.Button"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) => click();
        return b;
    }

    static void Beep()
    {
        try { System.Media.SystemSounds.Beep.Play(); App.Session.Log("WASAPI test beep"); }
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
        var fp = s.Selection.Kind + string.Join(",", s.Selection.Ids);
        if (fp == _fp) return;
        _fp = fp;
        _root.Children.Clear();
        var show = s.Show;
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
        }
        else if (s.Selection.Kind == SelectionKind.Display)
        {
            var d = show.Displays.FirstOrDefault(x => s.Selection.Ids.Contains(x.Id));
            if (d is null) return;
            Field("Name", d.Name, v => s.UpdateDisplay(d.Id, x => x.Name = v));
            Field("Width", d.Width.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateDisplay(d.Id, x => x.Width = n); });
            Field("Height", d.Height.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateDisplay(d.Id, x => x.Height = n); });
            Field("X", d.X.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateDisplay(d.Id, x => x.X = n); });
            Field("Y", d.Y.ToString("0"), v => { if (double.TryParse(v, out var n)) s.UpdateDisplay(d.Id, x => x.Y = n); });
            Field("Channel", d.Channel.ToString(), v => { if (int.TryParse(v, out var n)) s.UpdateDisplay(d.Id, x => x.Channel = n); });
        }
        else if (s.Selection.Kind == SelectionKind.Asset)
        {
            var a = show.Assets.FirstOrDefault(x => s.Selection.Ids.Contains(x.Id));
            if (a is null) return;
            _root.Children.Add(new TextBlock { Text = a.Name, Foreground = (Brush)FindResource("Wo.Text"), FontWeight = FontWeights.SemiBold });
            _root.Children.Add(new TextBlock { Text = a.Notes, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Wo.Muted"), Margin = new Thickness(0, 8, 0, 0) });
            _root.Children.Add(new TextBlock { Text = $"{a.Width:0}×{a.Height:0}  ·  {a.Codec}", Foreground = (Brush)FindResource("Wo.Amber"), Margin = new Thickness(0, 8, 0, 0) });
        }
        else
        {
            _root.Children.Add(new TextBlock { Text = show.Name, Foreground = (Brush)FindResource("Wo.Text"), FontSize = 16, FontWeight = FontWeights.SemiBold });
            _root.Children.Add(new TextBlock
            {
                Text = "Native DXVA H.264 Producer. Select a cue, display, or asset.",
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
}
