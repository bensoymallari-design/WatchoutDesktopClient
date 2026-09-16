using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Watchout.Core.Machine;
using Watchout.Desktop.Engine;

namespace Watchout.Desktop.Views;

/// <summary>
/// Live CPU / RAM / GPU meters above Devices, Layers, and Log. Warns when
/// another 4K clip or Stage output would overload this PC.
/// </summary>
public sealed class MachineStatusBar : UserControl
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Meter _cpu = new();
    readonly Meter _ram = new();
    readonly Meter _gpu = new();
    readonly TextBlock _headline = new()
    {
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
        Text = "Sampling CPU / GPU / memory…",
    };
    readonly TextBlock _caption = new()
    {
        FontSize = 10,
        Foreground = new SolidColorBrush(Color.FromRgb(150, 146, 140)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 0),
    };
    readonly Border _root;
    LoadLevel _logged = LoadLevel.Ok;
    bool _warned;

    public MachineStatusBar()
    {
        _root = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(17, 17, 17)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 8, 10, 8),
        };
        var stack = new StackPanel();
        stack.Children.Add(_cpu.Row("CPU"));
        stack.Children.Add(_ram.Row("RAM"));
        stack.Children.Add(_gpu.Row("GPU"));
        stack.Children.Add(_headline);
        stack.Children.Add(_caption);
        _root.Child = stack;
        Content = _root;
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) =>
        {
            Tick();
            _timer.Start();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    void Tick()
    {
        MachineSample sample;
        try { sample = MachineProbe.Read(); }
        catch
        {
            _headline.Text = "Machine status unavailable on this PC";
            return;
        }
        _cpu.Paint(MachineLoad.CpuValue(sample), sample.CpuPercent, MachineLoad.CpuLevel(sample));
        _ram.Paint(MachineLoad.RamValue(sample), MachineLoad.Percent(sample.RamUsedBytes, sample.RamTotalBytes), MachineLoad.RamLevel(sample));
        _gpu.Paint(MachineLoad.GpuValue(sample), MachineLoad.Percent(sample.GpuUsedBytes, sample.GpuTotalBytes), MachineLoad.GpuLevel(sample));
        var grade = MachineLoad.Grade(sample);
        _headline.Text = MachineLoad.Headline(sample);
        _headline.Foreground = new SolidColorBrush(Ink(grade));
        _caption.Text = MachineLoad.CaptionLine(sample);
        _root.Background = new SolidColorBrush(grade switch
        {
            LoadLevel.Full => Color.FromRgb(42, 22, 22),
            LoadLevel.Tight => Color.FromRgb(42, 36, 22),
            _ => Color.FromRgb(28, 28, 28),
        });
        _root.ToolTip = MachineLoad.Advice(sample) + "\n" + MachineLoad.DetailLine(sample);
        if (grade == _logged) return;
        _logged = grade;
        if (grade is LoadLevel.Tight or LoadLevel.Full)
        {
            _warned = true;
            App.Session.Log(MachineLoad.WarnLine(sample), "warn");
        }
        else if (_warned)
        {
            App.Session.Log("CPU / GPU / memory recovered — room to load more video");
        }
    }

    static Color Ink(LoadLevel level) => level switch
    {
        LoadLevel.Full => Color.FromRgb(248, 113, 113),
        LoadLevel.Tight => Color.FromRgb(245, 166, 35),
        _ => Color.FromRgb(74, 222, 128),
    };

    sealed class Meter
    {
        readonly TextBlock _value = new()
        {
            FontSize = 11,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
            Foreground = new SolidColorBrush(Color.FromRgb(210, 206, 200)),
        };
        readonly Border _fill = new()
        {
            Height = 8,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            Width = 0,
            Background = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
        };
        readonly Border _track = new()
        {
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(48, 48, 48)),
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
        };
        double _percent;

        public UIElement Row(string tag)
        {
            _track.Child = _fill;
            _track.SizeChanged += (_, _) => LayoutFill();
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            var name = new TextBlock
            {
                Text = tag,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 146, 140)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(_track, 1);
            Grid.SetColumn(_value, 2);
            grid.Children.Add(name);
            grid.Children.Add(_track);
            grid.Children.Add(_value);
            return grid;
        }

        public void Paint(string text, double percent, LoadLevel level)
        {
            _value.Text = text;
            var color = Ink(level);
            _value.Foreground = new SolidColorBrush(color);
            _fill.Background = new SolidColorBrush(color);
            _percent = Math.Clamp(percent, 0, 100);
            LayoutFill();
        }

        void LayoutFill()
        {
            var track = Math.Max(0, _track.ActualWidth);
            var width = track * (_percent / 100.0);
            _fill.Width = width < 1 ? 0 : width;
            _fill.Visibility = width < 1 ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
