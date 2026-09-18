using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

/// <summary>
/// Switches tab, H9 / V-Can style: SCREEN tiles, HDMI/NDI input cards,
/// orange LED crosspoints. Click a lamp to assign and auto-fit that Stage.
/// </summary>
public sealed class StageRouteBar : UserControl
{
    static readonly SolidColorBrush Ink = Brush(232, 230, 227);
    static readonly SolidColorBrush Muted = Brush(154, 149, 141);
    static readonly SolidColorBrush Nova = Brush(255, 106, 0);
    static readonly SolidColorBrush Live = Brush(33, 196, 90);
    static readonly SolidColorBrush Panel = Brush(18, 18, 18);
    static readonly SolidColorBrush Tile = Brush(30, 30, 30);
    static readonly SolidColorBrush Line = Brush(48, 48, 48);
    static readonly SolidColorBrush RowAlt = Brush(22, 22, 22);

    static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    readonly StackPanel _root = new();
    string _fp = "";
    bool _building;

    public StageRouteBar()
    {
        Content = new ScrollViewer
        {
            Content = _root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Panel,
            Padding = new Thickness(12),
        };
        App.Session.Changed += () => Dispatcher.BeginInvoke(Reload);
        CaptureHub.Changed += () => Dispatcher.BeginInvoke(Reload);
        NdiHub.Changed += () => Dispatcher.BeginInvoke(Reload);
        Loaded += (_, _) => Reload();
    }

    public void Reload()
    {
        var show = App.Session.Show;
        var incoming = Incoming().ToList();
        var fp = string.Join("|", show?.Displays.Select(d => d.Id + d.Name + d.Width + d.Height + d.Enabled + d.X) ?? [])
                 + "|" + string.Join("|", incoming.Select(i => i.Kind + i.Key + i.Label))
                 + "|" + string.Join("|", show?.CaptureDevices.Select(d => d.Signal + d.Kind + d.DisplayId + d.Name) ?? [])
                 + "|" + string.Join("|", show?.Assets.Where(a => LiveSources.IsLive(a)).Select(a => a.Id + a.Url) ?? [])
                 + "|" + CaptureHub.Generation
                 + "|" + NdiHub.Generation;
        if (fp == _fp && _root.Children.Count > 0) return;
        _fp = fp;
        _building = true;
        _root.Children.Clear();
        try
        {
            _root.Children.Add(TitleBar());
            var columns = show is null ? [] : StageRoute.Columns(show);
            if (columns.Count == 0)
            {
                _root.Children.Add(Hint("Add a display on Stage first. Each column is a SCREEN like Nova H9."));
                return;
            }

            var rows = StageRoute.Rows(show, incoming);
            _root.Children.Add(Chassis(Matrix(columns, rows)));
            if (rows.Count == 0)
                _root.Children.Add(Hint("No live inputs yet. Plug in a capture card or start Resolume NDI, then Refresh in Devices. Rows appear as HDMI Card 1 / Card 2 and NDI 1."));
        }
        finally
        {
            _building = false;
        }
    }

    static UIElement TitleBar()
    {
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        bar.Children.Add(new Rectangle
        {
            Width = 4,
            Height = 22,
            Fill = Nova,
            Margin = new Thickness(0, 0, 10, 0),
            RadiusX = 1,
            RadiusY = 1,
        });
        DockPanel.SetDock(bar.Children[0], Dock.Left);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock
        {
            Text = "SWITCHING",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Ink,
        });
        titles.Children.Add(new TextBlock
        {
            Text = "INPUT  →  SCREEN",
            FontSize = 10,
            Foreground = Muted,
            Margin = new Thickness(0, 1, 0, 0),
        });
        bar.Children.Add(titles);
        return bar;
    }

    static Border Chassis(UIElement child) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(14, 14, 14)),
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12, 14, 14, 12),
        Child = child,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    UIElement Matrix(IReadOnlyList<Display> columns, IReadOnlyList<StageRoute.Row> rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        foreach (var _ in columns)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112), MinWidth = 100 });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < rows.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });

        grid.Children.Add(Cell(0, 0, new TextBlock
        {
            Text = "INPUT",
            Foreground = Muted,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(4, 0, 8, 10),
        }));

        for (var c = 0; c < columns.Count; c++)
        {
            var display = columns[c];
            var displayId = display.Id;
            var header = new Button
            {
                Style = (Style)FindResource("Wo.StageHeader"),
                ToolTip = $"{display.Name}  {StageRoute.ColumnHint(display)}",
            };
            header.Content = HeaderCopy(StageRoute.ColumnLabel(display, c).ToUpperInvariant(), StageRoute.ColumnHint(display));
            header.Click += (_, _) => App.Session.Select(SelectionKind.Display, displayId);
            grid.Children.Add(Cell(c + 1, 0, header));
        }

        var cardN = 0;
        var ndiN = 0;
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var ndi = row.Kind == StageRoute.Ndi;
            var title = ndi ? StageRoute.NdiRowLabel(++ndiN) : StageRoute.CaptureRowLabel(++cardN);
            var stripe = r % 2 == 1 ? RowAlt : Brushes.Transparent;
            var rowBg = new Border
            {
                Background = stripe,
                Margin = new Thickness(-8, 2, -8, 2),
            };
            Grid.SetColumn(rowBg, 0);
            Grid.SetColumnSpan(rowBg, columns.Count + 1);
            Grid.SetRow(rowBg, r + 1);
            grid.Children.Add(rowBg);

            grid.Children.Add(Cell(0, r + 1, SourceCard(title, row, ndi, columns)));
            for (var c = 0; c < columns.Count; c++)
            {
                var display = columns[c];
                var on = row.DisplayId == display.Id;
                var lamp = new ToggleButton
                {
                    IsChecked = on,
                    Style = (Style)FindResource("Wo.StageSwitch"),
                    ToolTip = on
                        ? $"Take {title} off {StageRoute.ColumnLabel(display, c)}"
                        : $"Switch {title} ({row.Label}) → {StageRoute.ColumnLabel(display, c)}  ·  auto-fit {display.Width:0}×{display.Height:0}",
                };
                if (on)
                {
                    lamp.Effect = new DropShadowEffect
                    {
                        Color = Color.FromRgb(255, 106, 0),
                        BlurRadius = 12,
                        ShadowDepth = 0,
                        Opacity = 0.85,
                    };
                }
                var kind = row.Kind;
                var key = row.Key;
                var label = row.Label;
                var displayId = display.Id;
                lamp.Checked += (_, _) =>
                {
                    if (_building) return;
                    Assign(kind, key, label, displayId);
                };
                lamp.Unchecked += (_, _) =>
                {
                    if (_building) return;
                    App.Session.ClearLiveFromStage(kind, key);
                };
                grid.Children.Add(Cell(c + 1, r + 1, lamp));
            }
        }

        return grid;
    }

    static UIElement HeaderCopy(string title, string hint)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Ink,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = hint,
            Foreground = Muted,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        });
        return stack;
    }

    UIElement SourceCard(string title, StageRoute.Row row, bool ndi, IReadOnlyList<Display> columns)
    {
        var routed = row.DisplayId is not null;
        var card = new Border
        {
            Background = Tile,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 8, 12, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var body = new DockPanel();
        var arm = new ToggleButton
        {
            Content = routed ? "ON" : "OFF",
            IsChecked = routed,
            Style = (Style)FindResource("Wo.StageArm"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = routed ? $"Take {title} off Stage" : $"Put {title} on Stage 1",
        };
        var kind = row.Kind;
        var key = row.Key;
        var label = row.Label;
        var first = columns.FirstOrDefault()?.Id;
        arm.Checked += (_, _) =>
        {
            if (_building) return;
            if (first is not null) Assign(kind, key, label, first);
        };
        arm.Unchecked += (_, _) =>
        {
            if (_building) return;
            App.Session.ClearLiveFromStage(kind, key);
        };
        DockPanel.SetDock(arm, Dock.Right);
        body.Children.Add(arm);
        var pip = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = routed ? Live : new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(pip, Dock.Left);
        body.Children.Add(pip);
        var text = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new Border
        {
            Background = ndi ? new SolidColorBrush(Color.FromRgb(36, 54, 40)) : new SolidColorBrush(Color.FromRgb(54, 36, 24)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock
            {
                Text = ndi ? "NDI" : "HDMI",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = ndi ? Live : Nova,
            },
        });
        top.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink,
            VerticalAlignment = VerticalAlignment.Center,
        });
        text.Children.Add(top);
        text.Children.Add(new TextBlock
        {
            Text = row.Label,
            FontSize = 10,
            Foreground = Muted,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        });
        body.Children.Add(text);
        card.Child = body;
        card.ToolTip = row.Label;
        return card;
    }

    static UIElement Cell(int column, int row, UIElement child)
    {
        Grid.SetColumn(child, column);
        Grid.SetRow(child, row);
        return child;
    }

    static void Assign(string kind, string key, string label, string displayId)
    {
        if (kind == StageRoute.Ndi)
            App.Session.AssignNdiToDisplay(key, displayId, WebcamIdForRow(key));
        else
            App.Session.AssignCaptureToDisplay(key, displayId, label);
    }

    static string? WebcamIdForRow(string sourceName)
    {
        var cam = CaptureHub.Devices.FirstOrDefault(d =>
            NdiNames.LooksLikeNdi(d.Name) &&
            string.Equals(d.Name, sourceName, StringComparison.OrdinalIgnoreCase));
        return cam?.Id;
    }

    static IEnumerable<StageRoute.Incoming> Incoming()
    {
        foreach (var card in CaptureHub.Devices.Where(d => !NdiNames.LooksLikeNdi(d.Name)))
            yield return new StageRoute.Incoming(StageRoute.Capture, card.Id, card.Name);
        foreach (var source in NdiHub.Sources)
            yield return new StageRoute.Incoming(StageRoute.Ndi, source.Name, NdiNames.FriendlyName(source.Name));
        foreach (var cam in CaptureHub.Devices.Where(d => NdiNames.LooksLikeNdi(d.Name)))
            yield return new StageRoute.Incoming(StageRoute.Ndi, cam.Name, cam.Name);
    }

    static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Muted,
        FontSize = 12,
        Margin = new Thickness(0, 12, 0, 0),
    };
}
