using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Watchout.Core;
using Watchout.Core.Models;
using Watchout.Core.Persistence;

namespace Watchout.Desktop.Views;

public sealed class PrefsWindow : Window
{
    readonly ComboBox _gpu = new();
    readonly ComboBox _color = new();
    readonly TextBox _folder = new();
    readonly CheckBox _watch = new() { Content = "Watch that folder for new media", Margin = new Thickness(0, 8, 0, 0) };
    readonly CheckBox _auto = new() { Content = "Open last show when WatchMe starts", Margin = new Thickness(0, 12, 0, 0) };

    public PrefsWindow()
    {
        Title = "Preferences — WatchMe";
        Width = 520;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(17, 17, 17));
        Foreground = new SolidColorBrush(Color.FromRgb(232, 230, 227));
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "GPU", Foreground = new SolidColorBrush(Color.FromRgb(154, 149, 141)), Margin = new Thickness(0, 0, 0, 4) });
        _gpu.Items.Add(new ComboBoxItem { Content = "Auto (Windows default)", Tag = GpuPreference.Auto });
        _gpu.Items.Add(new ComboBoxItem { Content = "High performance (discrete GPU)", Tag = GpuPreference.HighPerformance });
        _gpu.Items.Add(new ComboBoxItem { Content = "Power saving (integrated GPU)", Tag = GpuPreference.PowerSaving });
        Select(_gpu, App.Settings.GpuPreference);
        root.Children.Add(_gpu);
        root.Children.Add(new TextBlock
        {
            Text = "WatchMe cannot switch GPUs by itself. After you pick High performance, open Windows Settings → System → Display → Graphics, add WatchMe.exe, and set High performance.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(154, 149, 141)),
            Margin = new Thickness(0, 8, 0, 16),
            FontSize = 12,
        });

        root.Children.Add(new TextBlock { Text = "Show color space (tag only — output is 8-bit WPF)", Foreground = new SolidColorBrush(Color.FromRgb(154, 149, 141)), Margin = new Thickness(0, 0, 0, 4) });
        _color.Items.Add(new ComboBoxItem { Content = "Rec.709", Tag = ColorSpaceTag.Rec709 });
        _color.Items.Add(new ComboBoxItem { Content = "Rec.2020", Tag = ColorSpaceTag.Rec2020 });
        _color.Items.Add(new ComboBoxItem { Content = "HLG", Tag = ColorSpaceTag.Hlg });
        _color.Items.Add(new ComboBoxItem { Content = "PQ / HDR10", Tag = ColorSpaceTag.Pq });
        Select(_color, App.Session.Show?.Prefs.ColorSpace ?? ColorSpaceTag.Rec709);
        _color.IsEnabled = App.Session.Show is not null;
        root.Children.Add(_color);

        root.Children.Add(new TextBlock { Text = "Watch folder", Foreground = new SolidColorBrush(Color.FromRgb(154, 149, 141)), Margin = new Thickness(0, 16, 0, 4) });
        var row = new DockPanel();
        var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        DockPanel.SetDock(browse, Dock.Right);
        _folder.Text = App.Settings.WatchFolder;
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Folder to watch for new media" };
            if (dlg.ShowDialog() == true) _folder.Text = dlg.FolderName;
        };
        row.Children.Add(browse);
        row.Children.Add(_folder);
        root.Children.Add(row);
        _watch.IsChecked = App.Settings.WatchFolderEnabled;
        root.Children.Add(_watch);
        _auto.IsChecked = App.Settings.AutoStartLastShow;
        root.Children.Add(_auto);

        var save = new Button { Content = "Save", Margin = new Thickness(0, 24, 0, 0), Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += (_, _) =>
        {
            if (_gpu.SelectedItem is ComboBoxItem gpu && gpu.Tag is GpuPreference pref)
                App.Settings.GpuPreference = pref;
            App.Settings.WatchFolder = _folder.Text.Trim();
            App.Settings.WatchFolderEnabled = _watch.IsChecked == true;
            App.Settings.AutoStartLastShow = _auto.IsChecked == true;
            App.PersistSettings();
            if (App.Session.Show is not null && _color.SelectedItem is ComboBoxItem cs && cs.Tag is ColorSpaceTag tag)
                App.Session.SetColorSpace(tag);
            App.Session.Log(App.Settings.GpuPreference switch
            {
                GpuPreference.HighPerformance => "GPU preference: High performance — pin WatchMe.exe in Windows Graphics settings",
                GpuPreference.PowerSaving => "GPU preference: Power saving",
                _ => "GPU preference: Auto",
            });
            Close();
        };
        root.Children.Add(save);
        Content = root;
    }

    public TextBox FolderBox => _folder;

    static void Select(ComboBox box, object tag)
    {
        foreach (ComboBoxItem item in box.Items)
            if (Equals(item.Tag, tag)) box.SelectedItem = item;
        if (box.SelectedItem is null) box.SelectedIndex = 0;
    }

    public static void ShowDialog(Window? owner, bool focusWatchFolder = false)
    {
        var win = new PrefsWindow();
        if (owner is not null) win.Owner = owner;
        if (focusWatchFolder) win.Loaded += (_, _) => win.FolderBox.Focus();
        win.ShowDialog();
    }
}
