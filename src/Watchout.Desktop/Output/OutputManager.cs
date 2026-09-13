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
        if (_windows.TryGetValue(display.Id, out var existing))
        {
            existing.Activate();
            return;
        }
        var screens = Monitors.List();
        var target = screen
                     ?? (display.ScreenId is { } sid ? screens.FirstOrDefault(s => s.Id == sid) : null)
                     ?? ScreenAssign.PreferredOutputScreen(screens, display.Channel)
                     ?? screens[0];
        var playAudio = OutputShouldPlayAudio(target, screens);
        var win = new OutputWindow(display, target, playAudio);
        win.Closed += (_, _) =>
        {
            _windows.Remove(display.Id);
            App.Session.LiveOutputs.Remove(display.Id);
            App.Session.Log($"Closed output {display.Name}");
        };
        _windows[display.Id] = win;
        App.Session.LiveOutputs.Add(display.Id);
        win.Show();
        if (fullscreen)
        {
            win.Left = target.Left;
            win.Top = target.Top;
            win.Width = target.Width;
            win.Height = target.Height;
            win.WindowState = WindowState.Maximized;
        }
        App.Session.Log($"Output {display.Name} → {target.Label} ({target.Width}×{target.Height}) · DXVA H.264 · audio {(playAudio ? "on" : "muted")}");
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
        var screens = Monitors.List();
        foreach (var display in displays.Where(d => d.Enabled))
        {
            var screen = ScreenAssign.ScreenForDisplay(display, screens);
            Open(display, screen);
        }
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
