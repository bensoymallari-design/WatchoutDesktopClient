using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Watchout.Core;
using Watchout.Core.Engine;

namespace Watchout.Desktop.Views;

public sealed class SplashWindow : Window
{
    readonly TextBlock _phrase;
    readonly TextBlock _detail;
    readonly ProgressBar _bar;
    readonly StackPanel _done;

    public SplashWindow()
    {
        Title = Brand.Name;
        Width = 640;
        Height = 420;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(12, 12, 12));
        Foreground = new SolidColorBrush(Color.FromRgb(232, 230, 227));
        AllowsTransparency = false;
        Topmost = true;
        ShowInTaskbar = true;

        var root = new DockPanel { Margin = new Thickness(36, 32, 36, 28) };
        var brand = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        try
        {
            brand.Children.Add(new Image
            {
                Source = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.png")),
                Width = 48,
                Height = 48,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
            });
        }
        catch { /* resource optional */ }
        brand.Children.Add(new TextBlock
        {
            Text = Brand.Name,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(245, 166, 35)),
        });
        brand.Children.Add(new TextBlock
        {
            Text = Brand.Tagline,
            Foreground = new SolidColorBrush(Color.FromRgb(154, 149, 141)),
            Margin = new Thickness(0, 4, 0, 0),
        });
        DockPanel.SetDock(brand, Dock.Top);
        root.Children.Add(brand);

        _bar = new ProgressBar
        {
            Height = 4,
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(245, 166, 35)),
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            Margin = new Thickness(0, 0, 0, 16),
        };
        DockPanel.SetDock(_bar, Dock.Top);
        root.Children.Add(_bar);

        _phrase = new TextBlock
        {
            Text = BootPlan.Line(BootPlan.Steps[0]),
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 4),
        };
        DockPanel.SetDock(_phrase, Dock.Top);
        root.Children.Add(_phrase);

        _detail = new TextBlock
        {
            Text = BootPlan.Steps[0].Detail,
            Foreground = new SolidColorBrush(Color.FromRgb(154, 149, 141)),
            Margin = new Thickness(0, 0, 0, 16),
        };
        DockPanel.SetDock(_detail, Dock.Top);
        root.Children.Add(_detail);

        _done = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = _done, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden });
        Content = root;
    }

    public void Report(BootStep step, int completed, string note)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Report(step, completed, note));
            return;
        }
        _phrase.Text = BootPlan.Line(step);
        _detail.Text = string.IsNullOrEmpty(note) ? step.Detail : note;
        _bar.Value = BootPlan.Progress(completed, BootPlan.Steps.Count);
        if (!string.IsNullOrEmpty(note))
        {
            _done.Children.Add(new TextBlock
            {
                Text = "•  " + BootPlan.DoneLine(step, note),
                Foreground = new SolidColorBrush(Color.FromRgb(154, 149, 141)),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }
}
