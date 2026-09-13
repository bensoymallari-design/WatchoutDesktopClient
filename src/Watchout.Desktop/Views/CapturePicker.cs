using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

static class CapturePicker
{
    public static IReadOnlyList<CaptureDeviceInfo> ChooseMany(Window owner, IReadOnlyList<CaptureDeviceInfo> devices)
    {
        if (devices.Count == 0)
        {
            MessageBox.Show(owner,
                "No capture cards were found.\n\n" +
                "1. Plug HDMI/SDI capture cards into this PC (Elgato, Blackmagic, Magewell, USB capture).\n" +
                "2. Feed Resolume (or any program) into each card.\n" +
                "3. Or enable NDI Webcam Input for Resolume NDI.\n" +
                "4. Then Live → Refresh Capture Cards.\n\n" +
                "WatchMe can run many cards at once — each card gets its own Stage display.",
                "WatchMe capture");
            return [];
        }
        if (devices.Count == 1) return [devices[0]];

        var list = new ListBox
        {
            ItemsSource = devices.Select(d => $"{d.Name}  ·  {d.Kind}").ToList(),
            SelectionMode = SelectionMode.Extended,
            Height = Math.Min(280, 28 + devices.Count * 24),
            Margin = new Thickness(0, 12, 0, 12),
        };
        for (var i = 0; i < devices.Count; i++) list.SelectedItems.Add(list.Items[i]);
        var all = new Button { Content = "Connect all", Style = (Style)Application.Current.FindResource("Wo.Primary"), IsDefault = true };
        var selected = new Button { Content = "Connect selected", Style = (Style)Application.Current.FindResource("Wo.Button"), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Style = (Style)Application.Current.FindResource("Wo.Button"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(selected);
        buttons.Children.Add(all);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "Connect one or many cards. Each live input fills its own display on Stage (5 cards → 5 outputs).",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("Wo.Muted"),
        });
        root.Children.Add(list);
        root.Children.Add(buttons);
        var win = new Window
        {
            Title = "Connect capture cards",
            Owner = owner,
            Width = 520,
            Height = 180 + list.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = root,
            ResizeMode = ResizeMode.NoResize,
        };
        List<CaptureDeviceInfo>? picked = null;
        void Take(IEnumerable<int> indexes)
        {
            picked = indexes.Where(i => i >= 0 && i < devices.Count).Select(i => devices[i]).ToList();
            win.DialogResult = picked.Count > 0;
        }
        all.Click += (_, _) => Take(Enumerable.Range(0, devices.Count));
        selected.Click += (_, _) => Take(list.SelectedItems.Cast<object>().Select(item => list.Items.IndexOf(item)));
        return win.ShowDialog() == true ? picked ?? [] : [];
    }
}
