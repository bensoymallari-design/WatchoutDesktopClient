using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Watchout.Core.Models;
using Watchout.Core.Playback;
using Watchout.Core.Stage;
using Watchout.Desktop.Gpu;
using Watchout.Desktop.Interop;

namespace Watchout.Desktop.Output;

public sealed class OutputManager
{
    readonly Dictionary<string, OutputWindow> _windows = [];
    readonly DispatcherTimer _wakeSnap;

    public IReadOnlyCollection<string> LiveIds => _windows.Keys;

    public OutputManager()
    {
        _wakeSnap = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(VideoSync.DisplayWakeResnapMs) };
        _wakeSnap.Tick += (_, _) =>
        {
            _wakeSnap.Stop();
            ReviveAfterDisplayChange(log: false);
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void Open(Display display, OutputScreen? screen = null, bool fullscreen = true)
    {
        var screens = Monitors.List();
        var target = ScreenAssign.ResolveOutputScreen(display, screens, screen, out var skippedProducer);
        if (target is null)
        {
            App.Session.Log("No OS screen to output on. Win+P → Extend, then Find screens.", "warn");
            return;
        }
        if (_windows.TryGetValue(display.Id, out var existing))
        {
            existing.PlaceOnScreen(refresh: true);
            existing.Activate();
            return;
        }
        var playAudio = OutputShouldPlayAudio(target, screens);
        var win = new OutputWindow(display, target, playAudio);
        win.Closed += (_, _) =>
        {
            _windows.Remove(display.Id);
            App.Session.LiveOutputs.Remove(display.Id);
            App.Session.NoteLiveOutputsChanged();
            App.Session.Log($"Closed output {display.Name}");
        };
        _windows[display.Id] = win;
        App.Session.LiveOutputs.Add(display.Id);
        win.Show();
        if (fullscreen) win.PlaceOnScreen();
        App.Session.NoteLiveOutputsChanged();
        App.Session.Log(GpuEngine.Available
            ? "Output on a top-level GPU wall — Stage shares the same H.264 decode"
            : "Output uses Windows Media Foundation (the only DXVA). Stage is a software preview so Intel UHD does not stall both.");
        if (target.IsPrimary && screens.Any(s => !s.IsPrimary))
            App.Session.Log($"{display.Name} is on the Producer laptop — the LED wall / TV stays black. On the Display row pick the extra HDMI/DP screen (Colorlight, NovaStar, processor, or TV), then Output.", "warn");
        else if (skippedProducer)
            App.Session.Log($"{display.Name} was aimed at the laptop — Output went to {target.Label} so the wall gets picture.");
        App.Session.Log($"Output {display.Name} → {target.Label} ({ScreenAssign.ScreenSizeText(target)} at {target.Left},{target.Top}) · DXVA H.264 · audio {(playAudio ? "on" : "muted")}");
    }

    public void Close(string displayId)
    {
        if (_windows.TryGetValue(displayId, out var win))
            win.Close();
    }

    public void CloseAll()
    {
        foreach (var win in _windows.Values.ToList())
        {
            try { win.Close(); } catch { /* hung DXGI */ }
        }
        _windows.Clear();
    }

    public void OpenAll(IEnumerable<Display> displays)
    {
        foreach (var display in displays.Where(d => d.Enabled))
            Open(display);
    }

    public void PushShow(Show? show)
    {
        if (show is null) return;
        foreach (var (id, win) in _windows.ToList())
        {
            var display = show.Displays.FirstOrDefault(d => d.Id == id);
            if (display is null) win.Close();
            else win.BindDisplay(display);
        }
    }

    public void PushClock(Show? _) { /* StageSurface listens to Session.Clock */ }

    void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp is null) return;
        if (disp.CheckAccess()) ScheduleWakeSnap();
        else disp.BeginInvoke(ScheduleWakeSnap);
    }

    void ScheduleWakeSnap()
    {
        ReviveAfterDisplayChange(log: true);
        _wakeSnap.Stop();
        _wakeSnap.Start();
    }

    void ReviveAfterDisplayChange(bool log)
    {
        if (_windows.Count == 0) return;
        foreach (var win in _windows.Values.ToList())
        {
            try { win.ReviveAfterSinkChange(); }
            catch { /* display still training */ }
        }
        if (!log) return;
        App.Session.Log("Output snapped to the playhead after the TV / display woke (HDMI was off; the cue kept running).");
    }

    static bool OutputShouldPlayAudio(OutputScreen target, IReadOnlyList<OutputScreen> screens)
    {
        var hasTv = screens.Any(s => !s.IsPrimary);
        return !hasTv || !target.IsPrimary;
    }
}
