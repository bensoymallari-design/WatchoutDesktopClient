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
}
