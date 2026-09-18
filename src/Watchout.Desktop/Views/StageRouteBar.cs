using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

/// <summary>
/// Switches tab: circular radios. Columns are Stage 1 / Stage 2; rows are
/// Card 1, Card 2, NDI 1. Clicking a circle assigns that input and auto-fits
/// the cue to that Stage canvas.
/// </summary>
public sealed class StageRouteBar : UserControl
{
    readonly StackPanel _root = new() { Margin = new Thickness(16, 12, 16, 12) };
    string _fp = "";
    bool _building;

    public StageRouteBar()
    {
        Content = new ScrollViewer
        {
            Content = _root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
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
            var columns = show is null ? [] : StageRoute.Columns(show);
            if (columns.Count == 0)
            {
                _root.Children.Add(Hint("Add a display on Stage first. Each column is Stage 1, Stage 2, …"));
                return;
            }

            var rows = StageRoute.Rows(show, incoming);
            _root.Children.Add(Matrix(columns, rows));
            if (rows.Count == 0)
                _root.Children.Add(Hint("No live inputs yet. Plug in a capture card or start Resolume NDI, then Refresh in Devices. Each source becomes a row: Card 1, Card 2, NDI 1."));
        }
        finally
        {
            _building = false;
        }
    }

    UIElement Matrix(IReadOnlyList<Display> columns, IReadOnlyList<StageRoute.Row> rows)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        foreach (var _ in columns)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < rows.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });

        for (var c = 0; c < columns.Count; c++)
        {
            var display = columns[c];
            var displayId = display.Id;
            var header = new Button
            {
                Content = StageRoute.ColumnLabel(display, c),
                Style = (Style)FindResource("Wo.StageHeader"),
                Margin = new Thickness(8, 0, 8, 8),
                ToolTip = $"{display.Name}  {StageRoute.ColumnHint(display)}",
            };
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
            grid.Children.Add(Cell(0, r + 1, new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(242, 139, 130)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 12, 0),
                FontSize = 13,
                ToolTip = row.Label,
            }));
            var group = $"stage-route:{row.Kind}:{row.Key}";
            for (var c = 0; c < columns.Count; c++)
            {
                var display = columns[c];
                var on = row.DisplayId == display.Id;
                var radio = new RadioButton
                {
                    GroupName = group,
                    IsChecked = on,
                    Style = (Style)FindResource("Wo.StageSwitch"),
                    ToolTip = $"Assign {title} ({row.Label}) to {StageRoute.ColumnLabel(display, c)} and auto-fit {display.Width:0}×{display.Height:0}",
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
        FontSize = 12,
        Margin = new Thickness(0, 12, 0, 0),
    };
}
