using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

/// <summary>
/// Radio switches above Devices / Layers / Log. Each row is a capture card
/// or NDI source; each column is a Stage display. Clicking a column assigns
/// that input and auto-fits the cue to the canvas.
/// </summary>
public sealed class StageRouteBar : UserControl
{
    readonly StackPanel _root = new();
    string _fp = "";
    bool _building;

    public StageRouteBar()
    {
        var chrome = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(17, 17, 17)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 8, 10, 8),
            Child = new ScrollViewer
            {
                Content = _root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 220,
            },
        };
        Content = chrome;
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
            _root.Children.Add(new TextBlock
            {
                Text = "STAGE COLUMNS",
                Foreground = (Brush)FindResource("Wo.Amber"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
            });
            var columns = show is null ? [] : StageRoute.Columns(show);
            if (columns.Count == 0)
            {
                _root.Children.Add(Hint("Add a display on Stage first. Each column is one canvas; capture and NDI rows switch which column they fill."));
                return;
            }

            var rows = StageRoute.Rows(show, incoming);
            _root.Children.Add(Hint("Radio switches — one Stage column per incoming row. Click Display 1 / 2 / … to assign that capture card or NDI and auto-fit it to that canvas."));
            _root.Children.Add(Matrix(columns, rows));
            if (rows.Count == 0)
                _root.Children.Add(Hint("No live inputs yet. Plug in a capture card or start Resolume NDI, then Refresh in Devices. Each source becomes a row."));
        }
        finally
        {
            _building = false;
        }
    }

    UIElement Matrix(IReadOnlyList<Display> columns, IReadOnlyList<StageRoute.Row> rows)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 88 });
        foreach (var _ in columns)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < rows.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        grid.Children.Add(Cell(0, 0, new TextBlock
        {
            Text = "Incoming",
            Foreground = (Brush)FindResource("Wo.Muted"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        }));
        for (var c = 0; c < columns.Count; c++)
        {
            var display = columns[c];
            var displayId = display.Id;
            var header = new Button
            {
                Content = $"{StageRoute.ColumnLabel(display, c)}\n{StageRoute.ColumnHint(display)}",
                Style = (Style)FindResource("Wo.Button"),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 4),
                MinWidth = 88,
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                ToolTip = $"Select {display.Name} on Stage",
            };
            header.Click += (_, _) => App.Session.Select(SelectionKind.Display, displayId);
            grid.Children.Add(Cell(c + 1, 0, header));
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var kindTag = row.Kind == StageRoute.Ndi ? "NDI" : "Capture";
            grid.Children.Add(Cell(0, r + 1, new TextBlock
            {
                Text = $"{kindTag}  {row.Label}",
                Foreground = (Brush)FindResource("Wo.Text"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 8, 4),
                FontSize = 11,
            }));
            var group = $"stage-route:{row.Kind}:{row.Key}";
            for (var c = 0; c < columns.Count; c++)
            {
                var display = columns[c];
                var on = row.DisplayId == display.Id;
                var radio = new RadioButton
                {
                    Content = StageRoute.ColumnLabel(display, c),
                    GroupName = group,
                    IsChecked = on,
                    Style = (Style)FindResource("Wo.StageSwitch"),
                    ToolTip = $"Assign {row.Label} to {display.Name} and auto-fit {display.Width:0}×{display.Height:0}",
                };
                var kind = row.Kind;
                var key = row.Key;
                var label = row.Label;
                var displayId = display.Id;
                radio.Checked += (_, _) =>
                {
                    if (_building) return;
                    Assign(kind, key, label, displayId);
                };
                grid.Children.Add(Cell(c + 1, r + 1, radio));
            }
        }

        return grid;
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
        Foreground = (Brush)Application.Current.FindResource("Wo.Muted"),
        FontSize = 11,
        Margin = new Thickness(0, 0, 0, 6),
    };
}
