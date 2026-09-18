using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SharpGen.Runtime;
using Watchout.Core;
using Watchout.Core.Gpu;
using Watchout.Core.Models;
using Watchout.Core.Persistence;
using Watchout.Desktop.Engine;
using Watchout.Desktop.Gpu;
using Watchout.Desktop.Interop;
using Watchout.Desktop.Media;
using Watchout.Desktop.Output;
using Watchout.Desktop.Views;

namespace Watchout.Desktop;

public partial class App : Application
{
    public static ProducerSession Session { get; } = new();
    public static OutputManager Outputs { get; } = new();
    public static AppSettings Settings { get; set; } = new();

    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / 60) };
    DateTime _lastTick = DateTime.UtcNow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            if (SurviveGpuGlitch(args.Exception))
            {
                Session.Log(
                    "WatchMe kept running after a GPU/screenshot error — "
                    + args.Exception.Message
                    + ". Not RAM. Play again if the wall went black.",
                    "warn");
                try { GpuEngine.TryStart(); } catch { /* retry next clock */ }
                args.Handled = true;
                return;
            }
            ReleaseHardware();
            args.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, _) => ReleaseHardware();
        Session.PlaybackChanged += () =>
            GpuEngine.KickForPlay(Session.Show?.Timelines.Any(t => t.Playback == PlaybackState.Play) == true);
        if (e.Args.Any(a => a.Equals("--release-displays", StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ReleaseHardware();
            Shutdown();
            return;
        }
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var splash = new SplashWindow();
        MainWindow = splash;
        splash.Show();
        await Dispatcher.Yield(DispatcherPriority.Render);
        try
        {
            await AppBoot.RunAsync(splash.Report, StartClock, async () => { await Dispatcher.Yield(DispatcherPriority.Render); });
        }
        catch (Exception ex)
        {
            Session.Log("Startup failed — " + ex.Message, "error");
        }
        var main = new MainWindow();
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
        splash.Close();
        main.Activate();
    }

    internal static void ReleaseHardware()
    {
        try { Outputs.CloseAll(); } catch { /* hung output */ }
        try { CaptureHub.Shutdown(); } catch { /* capture */ }
        try { NdiHub.Shutdown(); } catch { /* ndi */ }
        try { GpuEngine.Shutdown(); } catch { /* dxgi */ }
        try { DisplayReset.Restore(); } catch { /* display mode */ }
    }

    static bool SurviveGpuGlitch(Exception ex)
    {
        if (ex is OutOfMemoryException) return true;
        if (ex is COMException com) return OutputViewMath.SurviveUnhandled(com.HResult);
        if (ex is SharpGenException sg) return OutputViewMath.SurviveUnhandled(sg.HResult);
        return OutputViewMath.SurviveUnhandled(ex.HResult);
    }

    void StartClock()
    {
        if (_clock.IsEnabled) return;
        _clock.Tick += OnClock;
        _clock.Start();
    }

    void OnClock(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = (now - _lastTick).TotalMilliseconds;
        _lastTick = now;
        if (dt is <= 0 or > 250) dt = 1000.0 / 60;
        Session.Tick(dt);
        Outputs.PushClock(Session.PlaybackShow);
        GpuEngine.WatchPlay(
            Session.Show?.Timelines.Any(t => t.Playback == PlaybackState.Play) == true,
            Session.LiveOutputs.Count);
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

    public static void LoadStartupState()
    {
        LoadRecents();
        LoadSettings();
        Session.Log($"{Brand.Name} {Brand.Version} — native Windows desktop. H.264 plays through DXVA (one decode). HDMI/SDI capture cards take any live program. No Electron, no WebM proxy.");
        Session.Log($"Media folder {Path.Combine(DataDir(), "media")} — 4K files stay on the original disk. Delete leftover copies here if the drive filled up.");
        if (Settings.GpuPreference != GpuPreference.Auto)
            Session.Log($"GPU preference {Settings.GpuPreference} — pin WatchMe.exe in Windows Graphics settings. WPF cannot switch adapters itself.");
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
        ReleaseHardware();
        PersistRecents();
        base.OnExit(e);
    }
}
