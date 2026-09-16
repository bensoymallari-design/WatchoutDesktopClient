using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Watchout.Core;
using Watchout.Core.Models;
using Watchout.Core.Persistence;
using Watchout.Desktop.Media;
using Watchout.Desktop.Output;

namespace Watchout.Desktop;

public partial class App : Application
{
    public static ProducerSession Session { get; } = new();
    public static OutputManager Outputs { get; } = new();
    public static AppSettings Settings { get; set; } = new();

    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / 60) };
    DateTime _lastTick = DateTime.UtcNow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
        LoadRecents();
        LoadSettings();
        Session.Log($"{Brand.Name} {Brand.Version} — native Windows desktop. H.264 plays through Media Foundation / DXVA. HDMI/SDI capture cards can take Resolume (or any program) live. No Electron, no WebM proxy.");
        Session.Log($"Media folder {Path.Combine(DataDir(), "media")} — 4K files stay on the original disk. Delete leftover copies here if the drive filled up.");
        if (Settings.GpuPreference != GpuPreference.Auto)
            Session.Log($"GPU preference {Settings.GpuPreference} — pin WatchMe.exe in Windows Graphics settings. WPF cannot switch adapters itself.");
        _clock.Tick += OnClock;
        _clock.Start();
        Session.Changed += () => Outputs.PushShow(Session.PlaybackShow);
        Session.PlaybackChanged += () => Outputs.PushShow(Session.PlaybackShow);
        _ = CaptureHub.RefreshAsync();
    }

    void OnClock(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = (now - _lastTick).TotalMilliseconds;
        _lastTick = now;
        if (dt is <= 0 or > 250) dt = 1000.0 / 60;
        Session.Tick(dt);
        Outputs.PushClock(Session.PlaybackShow);
    }

    public static string DataDir()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Brand.Name);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "media"));
        Directory.CreateDirectory(Path.Combine(dir, "media", "proxies"));
        Directory.CreateDirectory(Path.Combine(dir, "autosave"));
        return dir;
    }

    static void LoadRecents()
    {
        var path = Path.Combine(DataDir(), "recents.json");
        if (!File.Exists(path)) return;
        Session.SetRecents(ShowSerializer.LoadRecents(File.ReadAllText(path)));
    }

    static void LoadSettings()
    {
        var path = Path.Combine(DataDir(), "settings.json");
        if (!File.Exists(path)) return;
        try
        {
            Settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), ShowSerializer.Options) ?? new AppSettings();
        }
        catch
        {
            Settings = new AppSettings();
        }
    }

    public static void PersistSettings()
    {
        File.WriteAllText(Path.Combine(DataDir(), "settings.json"), System.Text.Json.JsonSerializer.Serialize(Settings, ShowSerializer.Options));
    }

    public static void PersistRecents()
    {
        File.WriteAllText(Path.Combine(DataDir(), "recents.json"), ShowSerializer.SaveRecents(Session.Recents));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Outputs.CloseAll();
        CaptureHub.Shutdown();
        NdiHub.Shutdown();
        PersistRecents();
        base.OnExit(e);
    }
}
