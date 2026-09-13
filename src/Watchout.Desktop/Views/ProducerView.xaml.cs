using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Watchout.Core;
using Watchout.Core.Models;

namespace Watchout.Desktop.Views;

public partial class ProducerView : UserControl
{
    bool _syncing;

    public ProducerView()
    {
        InitializeComponent();
        Stage.Editing = true;
        Stage.PlayAudio = true;
        App.Session.Changed += () => Dispatcher.BeginInvoke(SyncChrome);
        Loaded += (_, _) => SyncChrome();
    }

    public void Reload()
    {
        SyncChrome();
        Properties.Reload();
        Assets.Reload();
        Timelines.Reload();
        Timeline.Reload();
        Devices.Reload();
        Log.Reload();
        Stage.Refresh();
    }

    void SyncChrome()
    {
        _syncing = true;
        var tl = App.Session.ActiveTimeline;
        var loop = tl?.Loop == true;
        TimelineTitle.Text = tl is null
            ? "Timeline"
            : $"{tl.Name}  ·  {TimeFormat.FormatPlayTime(tl.Playhead)}  ·  {(loop ? "LOOP" : "once")}  ·  {tl.Playback.ToString().ToUpperInvariant()}";
        LoopBox.IsChecked = loop;
        var displays = App.Session.StageEditMode == StageEditMode.Displays;
        StageHint.Text = displays
            ? "Stage  ·  click a display to edit the canvas — drag to move, handles to resize"
            : "Stage  ·  click a display name, or double-click, to edit the canvas";
        PaintMode(EditCuesBtn, !displays);
        PaintMode(EditDisplaysBtn, displays);
        _syncing = false;
    }

    static void PaintMode(Button button, bool on)
    {
        button.Background = new SolidColorBrush(on ? Color.FromRgb(245, 166, 35) : Color.FromRgb(31, 31, 31));
        button.Foreground = new SolidColorBrush(on ? Color.FromRgb(17, 17, 17) : Color.FromRgb(232, 230, 227));
        button.BorderBrush = new SolidColorBrush(on ? Color.FromRgb(245, 166, 35) : Color.FromRgb(58, 58, 58));
    }

    async void Import_Click(object sender, RoutedEventArgs e) => await MainWindow.ImportMediaAsync();
    void DeleteAsset_Click(object sender, RoutedEventArgs e) => DeleteHighlightedAsset();
    public void DeleteHighlightedAsset() => Assets.DeleteHighlighted();
    void Play_Click(object sender, RoutedEventArgs e) => App.Session.Play();
    void Pause_Click(object sender, RoutedEventArgs e) => App.Session.Pause();
    void Stop_Click(object sender, RoutedEventArgs e) => App.Session.Stop();
    void Loop_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        App.Session.SetLoop(null, LoopBox.IsChecked == true);
    }
    void AddLayer_Click(object sender, RoutedEventArgs e) => App.Session.AddLayer();
    void DeleteLayer_Click(object sender, RoutedEventArgs e) => App.Session.DeleteLayer();
    void EditCues_Click(object sender, RoutedEventArgs e) => App.Session.SetStageEditMode(StageEditMode.Cues);
    void EditDisplays_Click(object sender, RoutedEventArgs e) => App.Session.SetStageEditMode(StageEditMode.Displays);
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
