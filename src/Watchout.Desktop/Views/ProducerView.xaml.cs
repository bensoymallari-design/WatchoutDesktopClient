using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Watchout.Core;
using Watchout.Core.Models;
using Watchout.Core.Scheduling;
using Watchout.Core.Stage;

namespace Watchout.Desktop.Views;

public partial class ProducerView : UserControl
{
    bool _syncing;
    bool _syncingScroll;

    public ProducerView()
    {
        InitializeComponent();
        Stage.Editing = true;
        Stage.PlayAudio = true;
        App.Session.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            SyncChrome();
            SyncTimelineScroll();
        });
        App.Session.LayoutChanged += () => Dispatcher.BeginInvoke(SyncChrome);
        App.Session.Clock += () => Dispatcher.BeginInvoke(SyncChrome);
        App.Session.TimelineViewChanged += () => Dispatcher.BeginInvoke(SyncTimelineScroll);
        Loaded += (_, _) =>
        {
            StudioBottom.SelectedIndex = 0;
            Devices.Reload();
            SyncChrome();
            SyncTimelineScroll();
        };
    }

    public void Reload()
    {
        SyncChrome();
        Properties.Reload();
        Assets.Reload();
        Timelines.Reload();
        Timeline.Reload();
        Devices.Reload();
        Layers.Reload();
        Log.Reload();
        Stage.Refresh();
        SyncTimelineScroll();
    }

    void SyncTimelineScroll()
    {
        _syncingScroll = true;
        try
        {
            var s = App.Session;
            var tl = s.ActiveTimeline;
            var visibleMs = s.VisibleDurationMs();
            var duration = tl?.Duration ?? 0;
            TimelineHScroll.ViewportSize = visibleMs;
            TimelineHScroll.Maximum = Math.Max(0, duration - visibleMs);
            TimelineHScroll.SmallChange = Math.Max(1, visibleMs * 0.05);
            TimelineHScroll.LargeChange = Math.Max(1, visibleMs * 0.8);
            TimelineHScroll.Value = s.TimelineScroll;

            var lanesH = Math.Max(1, s.TimelineViewHeight - TimelineMath.RulerHeight);
            var contentH = (tl?.Layers.Count ?? 0) * TimelineMath.LaneHeight;
            TimelineVScroll.ViewportSize = lanesH;
            TimelineVScroll.Maximum = Math.Max(0, contentH - lanesH);
            TimelineVScroll.SmallChange = TimelineMath.LaneHeight;
            TimelineVScroll.LargeChange = lanesH;
            TimelineVScroll.Value = s.TimelineLayerScroll;
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    void TimelineHScroll_Scroll(object sender, ScrollEventArgs e)
    {
        if (_syncingScroll) return;
        App.Session.SetTimelineScroll(TimelineHScroll.Value);
    }

    void TimelineVScroll_Scroll(object sender, ScrollEventArgs e)
    {
        if (_syncingScroll) return;
        App.Session.SetTimelineLayerScroll(TimelineVScroll.Value);
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
        var show = App.Session.Show;
        var zoom = App.Session.Camera.Zoom;
        var n = show?.Displays.Count(d => d.Enabled) ?? 0;
        var wall = show is null ? null : StageGeometry.WallRect(show.Displays);
        var size = wall is { } w ? $"{w.W:0}×{w.H:0}" : "";
        StageHint.Text = $"Stage  ·  zoom {zoom * 100:0}%  ·  {n} display(s) {size}  ·  drag empty Stage to pan · Fit wall to center";
        PaintMode(EditCuesBtn, !displays);
        PaintMode(EditDisplaysBtn, displays);
        PaintMode(FitMediaBtn, true);
        _syncing = false;
    }

    static void PaintMode(Button button, bool on)
    {
        button.Background = new SolidColorBrush(on ? Color.FromRgb(245, 166, 35) : Color.FromRgb(31, 31, 31));
        button.Foreground = new SolidColorBrush(on ? Color.FromRgb(17, 17, 17) : Color.FromRgb(232, 230, 227));
        button.BorderBrush = new SolidColorBrush(on ? Color.FromRgb(245, 166, 35) : Color.FromRgb(58, 58, 58));
    }

    void Ndi_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (owner is not null) NdiPicker.Open(owner);
    }

    async void Import_Click(object sender, RoutedEventArgs e) => await MainWindow.ImportMediaAsync();
    void DeleteAsset_Click(object sender, RoutedEventArgs e) => DeleteHighlightedAsset();
    public void DeleteHighlightedAsset() => Assets.DeleteHighlighted();
    public void FitTimelineToMedia() => Timeline.FitToMedia();
    void Play_Click(object sender, RoutedEventArgs e) => App.Session.Play();
    void Pause_Click(object sender, RoutedEventArgs e) => App.Session.Pause();
    void Stop_Click(object sender, RoutedEventArgs e) => App.Session.Stop();
    void Loop_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        App.Session.SetLoop(null, LoopBox.IsChecked == true);
    }
    void FitMedia_Click(object sender, RoutedEventArgs e) => Timeline.FitToMedia();
    void AddLayer_Click(object sender, RoutedEventArgs e) => App.Session.AddLayer();
    void DeleteLayer_Click(object sender, RoutedEventArgs e) => App.Session.DeleteLayer();
    void EditCues_Click(object sender, RoutedEventArgs e) => App.Session.SetStageEditMode(StageEditMode.Cues);
    void EditDisplays_Click(object sender, RoutedEventArgs e) => App.Session.SetStageEditMode(StageEditMode.Displays);
    void FrameWall_Click(object sender, RoutedEventArgs e) => Stage.FrameWall();
    void FrameDisplay_Click(object sender, RoutedEventArgs e) => Stage.FrameSelectedDisplay();
    void ZoomIn_Click(object sender, RoutedEventArgs e) => Stage.ZoomBy(1.15);
    void ZoomOut_Click(object sender, RoutedEventArgs e) => Stage.ZoomBy(0.87);
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
