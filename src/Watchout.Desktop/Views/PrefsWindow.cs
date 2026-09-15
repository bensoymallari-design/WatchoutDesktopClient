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
    readonly CheckBox _hdr = new() { Content = "10-bit HDR pipeline (PQ / Rec.2020 on encodes)", Margin = new Thickness(0, 12, 0, 0) };
    readonly ComboBox _opt = new();
    readonly TextBox _nmos = new();
    readonly CheckBox _ltc = new() { Content = "LTC chase / generate at 30 fps", Margin = new Thickness(0, 8, 0, 0) };
    readonly CheckBox _access = new() { Content = "Require PIN to open WatchMe", Margin = new Thickness(0, 12, 0, 0) };
    readonly TextBox _pin = new() { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
    readonly ComboBox _role = new();

    public PrefsWindow()
    {
        Title = "Preferences — WatchMe";
        Width = 520;
        Height = 720;
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

        root.Children.Add(new TextBlock { Text = "Optimize preset (H.264 / 10-bit encodes)", Foreground = new SolidColorBrush(Color.FromRgb(154, 149, 141)), Margin = new Thickness(0, 16, 0, 4) });
        _opt.Items.Add(new ComboBoxItem { Content = "Fast", Tag = OptimizePreset.Fast });
        _opt.Items.Add(new ComboBoxItem { Content = "Quality", Tag = OptimizePreset.Quality });
        _opt.Items.Add(new ComboBoxItem { Content = "Broadcast (slow, high bit-rate)", Tag = OptimizePreset.Broadcast });
        Select(_opt, App.Session.Show?.Prefs.OptimizePreset ?? OptimizePreset.Quality);
        _opt.IsEnabled = App.Session.Show is not null;
        root.Children.Add(_opt);
        _hdr.IsChecked = App.Session.Show?.Prefs.HdrPipeline == true;
        _hdr.IsEnabled = App.Session.Show is not null;
        root.Children.Add(_hdr);

        root.Children.Add(new TextBlock { Text = "NMOS query registry", Foreground = new SolidColorBrush(Color.FromRgb(154, 149, 141)), Margin = new Thickness(0, 16, 0, 4) });
        _nmos.Text = string.IsNullOrEmpty(App.Session.Show?.Prefs.NmosRegistry) ? App.Settings.NmosRegistry : App.Session.Show!.Prefs.NmosRegistry;
        root.Children.Add(_nmos);
        _ltc.IsChecked = App.Session.Show?.Prefs.LtcEnabled == true;
        _ltc.IsEnabled = App.Session.Show is not null;
        root.Children.Add(_ltc);

        root.Children.Add(new TextBlock { Text = "Access control", Foreground = new SolidColorBrush(Color.FromRgb(154, 149, 141)), Margin = new Thickness(0, 16, 0, 4) });
        _access.IsChecked = App.Settings.AccessEnabled;
        root.Children.Add(_access);
        root.Children.Add(new TextBlock { Text = "PIN", Foreground = new SolidColorBrush(Color.FromRgb(154, 149, 141)), Margin = new Thickness(0, 8, 0, 4) });
        _pin.Text = App.Settings.AccessPin;
        root.Children.Add(_pin);
        _role.Items.Add(new ComboBoxItem { Content = "Producer", Tag = AccessRole.Producer });
        _role.Items.Add(new ComboBoxItem { Content = "Operator", Tag = AccessRole.Operator });
        _role.Items.Add(new ComboBoxItem { Content = "Viewer", Tag = AccessRole.Viewer });
        Select(_role, App.Settings.AccessRole);
        root.Children.Add(_role);

        var save = new Button { Content = "Save", Margin = new Thickness(0, 24, 0, 0), Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += (_, _) =>
        {
            if (_gpu.SelectedItem is ComboBoxItem gpu && gpu.Tag is GpuPreference pref)
                App.Settings.GpuPreference = pref;
            App.Settings.WatchFolder = _folder.Text.Trim();
            App.Settings.WatchFolderEnabled = _watch.IsChecked == true;
            App.Settings.AutoStartLastShow = _auto.IsChecked == true;
            App.Settings.NmosRegistry = _nmos.Text.Trim();
            App.Settings.AccessEnabled = _access.IsChecked == true;
            App.Settings.AccessPin = _pin.Text.Trim();
            if (_role.SelectedItem is ComboBoxItem roleItem && roleItem.Tag is AccessRole role)
                App.Settings.AccessRole = role;
            App.PersistSettings();
            if (App.Session.Show is not null)
            {
                if (_color.SelectedItem is ComboBoxItem cs && cs.Tag is ColorSpaceTag tag)
                    App.Session.SetColorSpace(tag);
                if (_opt.SelectedItem is ComboBoxItem op && op.Tag is OptimizePreset preset)
                    App.Session.SetOptimizePreset(preset);
                App.Session.SetHdrPipeline(_hdr.IsChecked == true);
                App.Session.SetNmosRegistry(_nmos.Text.Trim());
                App.Session.SetLtc(_ltc.IsChecked == true);
            }
            App.Session.Log(App.Settings.GpuPreference switch
            {
                GpuPreference.HighPerformance => "GPU preference: High performance — pin WatchMe.exe in Windows Graphics settings",
                GpuPreference.PowerSaving => "GPU preference: Power saving",
                _ => "GPU preference: Auto",
            });
            Close();
        };
        root.Children.Add(save);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
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
