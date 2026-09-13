using System.Windows;
using System.Windows.Controls;
using Watchout.Core;
using Watchout.Core.Models;

namespace Watchout.Desktop.Views;

public partial class ProducerView : UserControl
{
    public ProducerView()
    {
        InitializeComponent();
        Stage.Editing = true;
        Stage.PlayAudio = true;
        App.Session.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            var tl = App.Session.ActiveTimeline;
            TimelineTitle.Text = tl is null ? "Timeline" : $"{tl.Name}  ·  {TimeFormat.FormatPlayTime(tl.Playhead)}";
        });
    }

    public void Reload()
    {
        var tl = App.Session.ActiveTimeline;
        TimelineTitle.Text = tl is null ? "Timeline" : $"{tl.Name}  ·  {Watchout.Core.TimeFormat.FormatPlayTime(tl.Playhead)}";
        Properties.Reload();
        Assets.Reload();
        Timelines.Reload();
        Timeline.Reload();
        Devices.Reload();
        Log.Reload();
        Stage.Refresh();
    }

    async void Import_Click(object sender, RoutedEventArgs e) => await MainWindow.ImportMediaAsync();
    void Play_Click(object sender, RoutedEventArgs e) => App.Session.TogglePlay();
    void Stop_Click(object sender, RoutedEventArgs e) => App.Session.SetPlayback(null, PlaybackState.Stop);
    void FitWall_Click(object sender, RoutedEventArgs e) => App.Session.FitSelectedToWall();
    void FitDisplay_Click(object sender, RoutedEventArgs e) => App.Session.FitSelectedToDisplay();
    void Output_Click(object sender, RoutedEventArgs e)
    {
        var show = App.Session.Show;
        if (show is null) return;
        var id = App.Session.Selection.Kind == SelectionKind.Display ? App.Session.Selection.Ids.FirstOrDefault() : show.Displays.FirstOrDefault()?.Id;
        var display = show.Displays.FirstOrDefault(d => d.Id == id) ?? show.Displays.FirstOrDefault();
        if (display is not null) App.Outputs.Open(display);
    }
    void OutputAll_Click(object sender, RoutedEventArgs e)
    {
        if (App.Session.Show is { } show) App.Outputs.OpenAll(show.Displays);
    }
}
