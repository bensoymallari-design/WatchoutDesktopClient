using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Watchout.Core.Media;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

public sealed class NdiPicker : Window
{
    readonly bool _placeOnLayer;
    readonly StackPanel _rows = new();
    readonly TextBlock _status = new();
    readonly Border _warn = new();
    readonly TextBlock _warnText = new();
    readonly Button _scan = new();
    bool _busy;

    public NdiPicker(Window owner, bool placeOnLayer)
    {
        _placeOnLayer = placeOnLayer;
        Owner = owner;
        Title = "NDI";
        Width = 560;
        Height = 520;
        MinWidth = 420;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(28, 28, 28));
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new DockPanel { Margin = new Thickness(20) };
        var close = new Button
        {
            Content = "Close",
            Style = (Style)Application.Current.FindResource("Wo.Button"),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(14, 6, 14, 6),
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var body = new DockPanel();
        var intro = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        intro.Children.Add(new TextBlock
        {
            Text = "NDI",
            Foreground = (Brush)Application.Current.FindResource("Wo.Amber"),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });
        intro.Children.Add(new TextBlock
        {
            Text = placeOnLayer
                ? "Pick which NDI program to place on a timeline layer. WatchMe uses the installed NDI Runtime (same DLL as WatchJhon) so local and LAN senders show up. For picture, open NDI Tools → NDI Webcam Input and pick the same source."
                : "Pick which NDI program to import into Assets — same idea as Resolume. WatchMe uses the installed NDI Runtime (same DLL as WatchJhon) so local and LAN senders show up. Each Import adds that source as a clip you drag onto a timeline layer.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("Wo.Muted"),
        });
        _warnText.TextWrapping = TextWrapping.Wrap;
        _warnText.Foreground = new SolidColorBrush(Color.FromRgb(255, 214, 102));
        _warn.Child = _warnText;
        _warn.Background = new SolidColorBrush(Color.FromRgb(64, 42, 8));
        _warn.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 166, 35));
        _warn.BorderThickness = new Thickness(1);
        _warn.Padding = new Thickness(10, 8, 10, 8);
        _warn.Margin = new Thickness(0, 10, 0, 0);
        _warn.Visibility = Visibility.Collapsed;
        intro.Children.Add(_warn);
        DockPanel.SetDock(intro, Dock.Top);
        body.Children.Add(intro);

        var header = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
        _scan.Content = "Scan again";
        _scan.Style = (Style)Application.Current.FindResource("Wo.Button");
        _scan.Padding = new Thickness(10, 4, 10, 4);
        _scan.Margin = new Thickness(8, 0, 0, 0);
        _scan.Click += async (_, _) => await ScanAsync();
        DockPanel.SetDock(_scan, Dock.Right);
        header.Children.Add(_scan);
        header.Children.Add(new TextBlock
        {
            Text = "SOURCES ON THIS NETWORK",
            Foreground = (Brush)Application.Current.FindResource("Wo.Muted"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);

        _status.Foreground = (Brush)Application.Current.FindResource("Wo.Muted");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(_status, Dock.Top);
        body.Children.Add(_status);

        body.Children.Add(new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        root.Children.Add(body);
        Content = root;

        Loaded += async (_, _) => await ScanAsync();
        NdiHub.Changed += OnHubChanged;
        CaptureHub.Changed += OnHubChanged;
        Closed += (_, _) =>
        {
            NdiHub.Changed -= OnHubChanged;
            CaptureHub.Changed -= OnHubChanged;
        };
    }

    public static void Open(Window owner, bool placeOnLayer = false) =>
        new NdiPicker(owner, placeOnLayer).ShowDialog();

    void OnHubChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnHubChanged);
            return;
        }
        if (!_busy) Rebuild();
    }

    async Task ScanAsync()
    {
        if (_busy) return;
        _busy = true;
        _scan.IsEnabled = false;
        _status.Text = "Scanning with NDI Runtime (same DLL WatchJhon uses)…";
        try
        {
            await CaptureHub.RefreshAsync();
            await NdiHub.RefreshAsync();
        }
        finally
        {
            _busy = false;
            _scan.IsEnabled = true;
            Rebuild();
        }
    }

    void Rebuild()
    {
        var choices = NdiCatalog.Choices(
            NdiHub.Sources,
            CaptureHub.Devices.Select(d => (d.Id, d.Name)));
        var error = NdiHub.LastError;
        _warn.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        _warnText.Text = error ?? "";
        _status.Text = choices.Count == 0
            ? "No NDI senders answered this scan."
            : _placeOnLayer
                ? $"{choices.Count} source(s) via {NdiHub.Engine} — place the ones you want on a layer."
                : $"{choices.Count} source(s) via {NdiHub.Engine} — import the ones you want.";
        _rows.Children.Clear();
        var imported = App.Session.Show?.Assets
            .Where(LiveSources.IsNdi)
            .Select(a => a.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var choice in choices)
            _rows.Children.Add(Row(choice, imported.Contains(choice.Name)));
    }

    UIElement Row(NdiChoice choice, bool inAssets)
    {
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        var action = new Button
        {
            Content = _placeOnLayer ? (inAssets ? "Place again" : "Place") : (inAssets ? "In Assets" : "Import"),
            Style = (Style)Application.Current.FindResource("Wo.Primary"),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 96,
        };
        var name = choice.Name;
        var webcam = choice.WebcamId;
        action.Click += (_, _) =>
        {
            if (_placeOnLayer) App.Session.ConnectNdi(name, webcam);
            else App.Session.ImportNdi(name, webcam);
            Rebuild();
        };
        DockPanel.SetDock(action, Dock.Right);
        row.Children.Add(action);
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = choice.Name,
            Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrEmpty(choice.Detail))
        {
            text.Children.Add(new TextBlock
            {
                Text = choice.Detail,
                Foreground = (Brush)Application.Current.FindResource("Wo.Muted"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        row.Children.Add(text);
        return row;
    }
}
