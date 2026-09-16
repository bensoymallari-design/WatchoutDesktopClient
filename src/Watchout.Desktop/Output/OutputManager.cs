using Watchout.Core.Models;
using Watchout.Core.Stage;
using Watchout.Desktop.Interop;

namespace Watchout.Desktop.Output;

public sealed class OutputManager
{
    readonly Dictionary<string, OutputWindow> _windows = [];

    public IReadOnlyCollection<string> LiveIds => _windows.Keys;

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
            existing.PlaceOnScreen();
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
        App.Session.Log("Output on the GPU compositor — Stage shares the same H.264 decode");
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
        foreach (var win in _windows.Values.ToList()) win.Close();
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

    public void PushClock(Show? _) { /* StageSurface listens to Session.Changed */ }

    static bool OutputShouldPlayAudio(OutputScreen target, IReadOnlyList<OutputScreen> screens)
    {
        var hasTv = screens.Any(s => !s.IsPrimary);
        return !hasTv || !target.IsPrimary;
    }
}
