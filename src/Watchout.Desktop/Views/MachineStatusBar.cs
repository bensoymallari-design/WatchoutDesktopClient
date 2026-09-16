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
    readonly TextBlock _cpu = MeterLabel();
    readonly TextBlock _ram = MeterLabel();
    readonly TextBlock _gpu = MeterLabel();
    readonly Border _cpuFill = FillBar();
    readonly Border _ramFill = FillBar();
    readonly Border _gpuFill = FillBar();
    readonly TextBlock _headline = new()
    {
        Text = "Sampling CPU / GPU / memory…",
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(232, 230, 227)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
    };
    readonly TextBlock _detail = new()
    {
        FontSize = 10,
        Foreground = new SolidColorBrush(Color.FromRgb(160, 155, 148)),
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
        stack.Children.Add(Row("CPU", _cpu, _cpuFill));
        stack.Children.Add(Row("RAM", _ram, _ramFill));
        stack.Children.Add(Row("GPU", _gpu, _gpuFill));
        stack.Children.Add(_headline);
        stack.Children.Add(_detail);
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
        Paint(_cpu, _cpuFill, MachineLoad.CpuValue(sample), sample.CpuPercent, MachineLoad.CpuLevel(sample));
        Paint(_ram, _ramFill, MachineLoad.RamValue(sample), MachineLoad.Percent(sample.RamUsedBytes, sample.RamTotalBytes), MachineLoad.RamLevel(sample));
        Paint(_gpu, _gpuFill, MachineLoad.GpuValue(sample), MachineLoad.Percent(sample.GpuUsedBytes, sample.GpuTotalBytes), MachineLoad.GpuLevel(sample));
        var grade = MachineLoad.Grade(sample);
        _headline.Text = MachineLoad.Headline(sample);
        _headline.Foreground = new SolidColorBrush(Ink(grade));
        _detail.Text = MachineLoad.DetailLine(sample);
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

    static void Paint(TextBlock label, Border fill, string text, double percent, LoadLevel level)
    {
        label.Text = text;
        label.Foreground = new SolidColorBrush(Ink(level));
        fill.Background = new SolidColorBrush(Ink(level));
        fill.Width = Math.Clamp(percent / 100.0, 0, 1) * TrackWidth;
    }

    const double TrackWidth = 88;

    static DockPanel Row(string tag, TextBlock value, Border fill)
    {
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
        var name = new TextBlock
        {
            Text = tag,
            Width = 28,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(150, 146, 140)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(name, Dock.Left);
        DockPanel.SetDock(value, Dock.Right);
        value.Width = 96;
        value.TextAlignment = TextAlignment.Right;
        var track = new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(48, 48, 48)),
            Margin = new Thickness(6, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Canvas { Width = TrackWidth, Height = 6, ClipToBounds = true },
        };
        ((Canvas)track.Child).Children.Add(fill);
        row.Children.Add(name);
        row.Children.Add(value);
        row.Children.Add(track);
        return row;
    }

    static TextBlock MeterLabel() => new()
    {
        FontSize = 10,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Color.FromRgb(210, 206, 200)),
    };

    static Border FillBar() => new()
    {
        Height = 6,
        Width = 8,
        CornerRadius = new CornerRadius(3),
        Background = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
    };

    static Color Ink(LoadLevel level) => level switch
    {
        LoadLevel.Full => Color.FromRgb(248, 113, 113),
        LoadLevel.Tight => Color.FromRgb(245, 166, 35),
        _ => Color.FromRgb(74, 222, 128),
    };
}
