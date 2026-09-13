using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Watchout.Core;
using Watchout.Core.Models;
using Watchout.Core.Persistence;
using Watchout.Desktop.Interop;
using Watchout.Desktop.Media;
using Watchout.Desktop.Views;

namespace Watchout.Desktop;

public partial class MainWindow : Window
{
    readonly WelcomeView _welcome = new();
    readonly ProducerView _producer = new();

    public MainWindow()
    {
        InitializeComponent();
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.png"));
        App.Session.Changed += OnSessionChanged;
        PreviewKeyDown += OnPreviewKey;
        OnSessionChanged();
        _ = FfmpegTools.DetectAsync().ContinueWith(t =>
        {
            Dispatcher.Invoke(() =>
            {
                if (t.Result)
                    App.Session.Log($"ffmpeg {FfmpegTools.H264Encoder} ready — HAP/DXV/ProRes can transcode to H.264 MP4 for DXVA");
                else
                    App.Session.Log("ffmpeg not on PATH — H.264/MP4 still play natively. Install ffmpeg only if you import HAP, DXV, or ProRes.", "warn");
            });
        });
    }

    void OnSessionChanged()
    {
        var s = App.Session;
        Root.Content = s.View == "producer" ? _producer : _welcome;
        Title = s.Show is { } show ? $"{show.Name} — {Brand.Name}" : Brand.Name;
        if (s.ActiveTimeline is { } tl)
            StatusClock.Text = TimeFormat.FormatPlayTime(tl.Playhead);
        StatusLog.Text = s.Logs.FirstOrDefault()?.Message ?? "Ready";
        SnapItem.IsChecked = s.Snap;
        ClickJumpItem.IsChecked = s.ClickJumpsToTime;
        LoopItem.IsChecked = s.ActiveTimeline?.Loop == true;
    }

    void OnPreviewKey(object sender, KeyEventArgs e)
    {
        var typing = e.OriginalSource is TextBox or PasswordBox or ComboBox or ComboBoxItem;
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Save_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Open_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (typing) return;
        if (e.Key == Key.Space && App.Session.View == "producer")
        {
            App.Session.TogglePlay();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (App.Outputs.LiveIds.Count > 0) App.Outputs.CloseAll();
            else App.Session.Stop();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete) App.Session.DeleteSelected();
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) App.Session.Undo();
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control) App.Session.Redo();
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control) App.Session.DuplicateSelected();
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            var dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            var dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            App.Session.NudgeSelected(dx, dy);
            e.Handled = true;
        }
    }

    void NewShow_Click(object sender, RoutedEventArgs e) => App.Session.NewShow();
    void Demo_Click(object sender, RoutedEventArgs e) => App.Session.OpenDemo();

    void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "WatchMe show|*.watchme.json;*.watch.json;*.json|All files|*.*",
            Title = "Open WatchMe show",
        };
        if (dlg.ShowDialog() == true) OpenPath(dlg.FileName);
    }

    public static void OpenPath(string path)
    {
        var json = File.ReadAllText(path);
        App.Session.LoadShow(ShowSerializer.Load(json), path);
        App.PersistRecents();
        App.Session.Log($"Opened {path} — original H.264 files play through DXVA (Electron WebM proxies are ignored when the master is H.264).");
    }

    void Save_Click(object sender, RoutedEventArgs e) => Save(false);
    void SaveAs_Click(object sender, RoutedEventArgs e) => Save(true);

    void Save(bool forceAs)
    {
        if (App.Session.Show is null) return;
        var path = App.Session.ShowPath;
        if (forceAs || string.IsNullOrEmpty(path))
        {
            var dlg = new SaveFileDialog
            {
                Filter = "WatchMe show|*.watchme.json|WATCHOUT show|*.watch.json|JSON|*.json",
                FileName = App.Session.Show.Name.Replace(' ', '_') + ".watchme.json",
                Title = "Save WatchMe show",
            };
            if (dlg.ShowDialog() != true) return;
            path = dlg.FileName;
        }
        File.WriteAllText(path, App.Session.SaveJson());
        App.Session.DidSave(path);
        App.PersistRecents();
    }

    void Exit_Click(object sender, RoutedEventArgs e) => Close();
    void QuitWelcome_Click(object sender, RoutedEventArgs e) => App.Session.QuitToWelcome();
    void Undo_Click(object sender, RoutedEventArgs e) => App.Session.Undo();
    void Redo_Click(object sender, RoutedEventArgs e) => App.Session.Redo();
    void Delete_Click(object sender, RoutedEventArgs e) => App.Session.DeleteSelected();
    void Duplicate_Click(object sender, RoutedEventArgs e) => App.Session.DuplicateSelected();
    void Snap_Click(object sender, RoutedEventArgs e) => App.Session.SetSnap(SnapItem.IsChecked == true);
    void ClickJump_Click(object sender, RoutedEventArgs e) => App.Session.SetClickJumpsToTime(ClickJumpItem.IsChecked == true);
    void FitDisplay_Click(object sender, RoutedEventArgs e) => App.Session.FitSelectedToDisplay();
    void FitWall_Click(object sender, RoutedEventArgs e) => App.Session.FitSelectedToWall();
    void Frame_Click(object sender, RoutedEventArgs e) => App.Session.FrameDisplays();
    void AddDisplay_Click(object sender, RoutedEventArgs e) => App.Session.AddDisplay();
    void Grid31_Click(object sender, RoutedEventArgs e) => App.Session.AddDisplayGrid(3, 1, 1920, 1080);
    void Grid22_Click(object sender, RoutedEventArgs e) => App.Session.AddDisplayGrid(2, 2, 1920, 1080);
    void Grid41_Click(object sender, RoutedEventArgs e) => App.Session.AddDisplayGrid(4, 1, 1920, 1080);
    void Play_Click(object sender, RoutedEventArgs e) => App.Session.TogglePlay();
    void Pause_Click(object sender, RoutedEventArgs e) => App.Session.Pause();
    void Stop_Click(object sender, RoutedEventArgs e) => App.Session.Stop();
    void Loop_Click(object sender, RoutedEventArgs e) => App.Session.SetLoop(null, LoopItem.IsChecked == true);
    void AddTimeline_Click(object sender, RoutedEventArgs e) => App.Session.AddTimeline();
    void DeleteTimeline_Click(object sender, RoutedEventArgs e) => App.Session.DeleteTimeline();
    void AddLayer_Click(object sender, RoutedEventArgs e) => App.Session.AddLayer();
    void InsertLayer_Click(object sender, RoutedEventArgs e) => App.Session.InsertLayer();
    void DeleteLayer_Click(object sender, RoutedEventArgs e) => App.Session.DeleteLayer();
    void EditCues_Click(object sender, RoutedEventArgs e) => App.Session.SetStageEditMode(StageEditMode.Cues);
    void EditDisplays_Click(object sender, RoutedEventArgs e) => App.Session.SetStageEditMode(StageEditMode.Displays);
    void Crossfade_Click(object sender, RoutedEventArgs e) => App.Session.ApplyCrossfade();
    void FadeIn_Click(object sender, RoutedEventArgs e) => App.Session.ToggleFade("in");
    void FadeOut_Click(object sender, RoutedEventArgs e) => App.Session.ToggleFade("out");

    void OutputSelected_Click(object sender, RoutedEventArgs e)
    {
        var show = App.Session.Show;
        if (show is null) return;
        var id = App.Session.Selection.Kind == SelectionKind.Display ? App.Session.Selection.Ids.FirstOrDefault() : show.Displays.FirstOrDefault()?.Id;
        var display = show.Displays.FirstOrDefault(d => d.Id == id) ?? show.Displays.FirstOrDefault();
        if (display is null) return;
        App.Outputs.Open(display);
    }

    void OutputAll_Click(object sender, RoutedEventArgs e)
    {
        if (App.Session.Show is { } show) App.Outputs.OpenAll(show.Displays);
    }

    void CloseOutputs_Click(object sender, RoutedEventArgs e) => App.Outputs.CloseAll();

    async void RefreshCapture_Click(object sender, RoutedEventArgs e)
    {
        await CaptureHub.RefreshAsync();
        App.Session.Log(CaptureHub.Devices.Count == 0
            ? "No capture cards found. Plug in an HDMI/SDI card (Elgato, Blackmagic, Magewell) or enable NDI Webcam Input."
            : $"Found {CaptureHub.Devices.Count} capture device(s). Connect all of them (Live → Connect All) — each card gets its own Stage display.");
    }

    async void ConnectCapture_Click(object sender, RoutedEventArgs e)
    {
        await CaptureHub.RefreshAsync();
        var picks = CapturePicker.ChooseMany(this, CaptureHub.Devices);
        if (picks.Count == 0) return;
        App.Session.ConnectCaptures(picks.Select(p => (p.Id, p.Name)));
    }

    async void ConnectAllCapture_Click(object sender, RoutedEventArgs e)
    {
        await CaptureHub.RefreshAsync();
        if (CaptureHub.Devices.Count == 0)
        {
            CapturePicker.ChooseMany(this, CaptureHub.Devices);
            return;
        }
        App.Session.ConnectCaptures(CaptureHub.Devices.Select(d => (d.Id, d.Name)));
    }

    void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            $"{Brand.Name} {Brand.Version} — native .NET / WPF desktop.\n\n" +
            "Video: Windows Media Foundation with DXVA/D3D11 hardware decode.\n" +
            "Play H.264, H.265, MPEG-2, WMV, AAC, WAV, MP3 as-is. No WebM/VP9 proxy.\n\n" +
            "Live: many HDMI/SDI capture cards at once (Elgato, Blackmagic, Magewell, USB).\n" +
            "Each card is a separate live layer on its own Stage display.\n" +
            "Send Resolume (or any program) into each card — or Resolume NDI through\n" +
            "NDI Webcam Input — then Live → Connect All Capture Cards.\n\n" +
            "HAP, Resolume DXV, ProRes, DNx: optional ffmpeg transcode to H.264 MP4\n" +
            "(NVENC / AMF / QSV when present) so the GPU still decodes DXVA H.264.",
            $"About {Brand.Name}");
    }

    public static async Task ImportMediaAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import media",
            Multiselect = true,
            Filter = "Media|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.mp4;*.mov;*.mkv;*.avi;*.mxf;*.wmv;*.wav;*.mp3;*.aac;*.flac;*.dxv;*.hap|All files|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        await MediaLibrary.ImportFilesAsync(dlg.FileNames, App.Session);
    }
}
