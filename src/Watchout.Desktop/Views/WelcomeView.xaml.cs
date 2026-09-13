using System.Windows.Controls;
using Watchout.Core.Models;

namespace Watchout.Desktop.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        InitializeComponent();
        Reload();
        App.Session.Changed += () => Dispatcher.BeginInvoke(Reload);
    }

    public void Reload() => Recents.ItemsSource = App.Session.Recents.ToList();

    void New_Click(object sender, System.Windows.RoutedEventArgs e) => App.Session.NewShow();
    void Demo_Click(object sender, System.Windows.RoutedEventArgs e) => App.Session.OpenDemo();
    void Open_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "WATCHOUT Show|*.watch.json;*.json" };
        if (dlg.ShowDialog() == true) MainWindow.OpenPath(dlg.FileName);
    }

    void Recents_Open(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Recents.SelectedItem is RecentShow r && System.IO.File.Exists(r.Path))
            MainWindow.OpenPath(r.Path);
    }
}
