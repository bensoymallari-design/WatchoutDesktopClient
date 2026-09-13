using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

static class CapturePicker
{
    public static CaptureDeviceInfo? Choose(Window owner, IReadOnlyList<CaptureDeviceInfo> devices)
    {
        if (devices.Count == 0)
        {
            MessageBox.Show(owner,
                "No capture cards were found.\n\n" +
                "1. Plug an HDMI/SDI capture card into this PC (Elgato Cam Link, Blackmagic, Magewell, USB capture).\n" +
                "2. In Resolume, send Program Out to that HDMI/SDI output.\n" +
                "3. Or enable NDI Webcam Input and send Resolume over NDI.\n" +
                "4. Then Live → Refresh Capture Cards.",
                "WatchMe capture");
            return null;
        }
        if (devices.Count == 1) return devices[0];

        var list = new ListBox
        {
            ItemsSource = devices.Select(d => $"{d.Name}  ·  {d.Kind}").ToList(),
            SelectedIndex = 0,
            Height = 160,
            Margin = new Thickness(0, 12, 0, 16),
        };
        var ok = new Button { Content = "Connect", Style = (Style)Application.Current.FindResource("Wo.Primary"), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Style = (Style)Application.Current.FindResource("Wo.Button"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "Choose the card that receives Resolume (or any HDMI/SDI program).",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("Wo.Muted"),
        });
        root.Children.Add(list);
        root.Children.Add(buttons);
        var win = new Window
        {
            Title = "Connect capture card",
            Owner = owner,
            Width = 480,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = root,
            ResizeMode = ResizeMode.NoResize,
        };
        CaptureDeviceInfo? picked = null;
        ok.Click += (_, _) =>
        {
            if (list.SelectedIndex >= 0) picked = devices[list.SelectedIndex];
            win.DialogResult = picked is not null;
        };
        return win.ShowDialog() == true ? picked : null;
    }
}
