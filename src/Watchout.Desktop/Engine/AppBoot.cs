using System.Diagnostics;
using Watchout.Core;
using Watchout.Core.Engine;
using Watchout.Desktop.Gpu;
using Watchout.Desktop.Interop;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Engine;

public sealed record BootResult(string StepId, bool Ok, string Note);

/// <summary>
/// Starts the same engines Resolume names on its splash, then Producer opens.
/// </summary>
public static class AppBoot
{
    public static readonly TimeSpan MinStep = TimeSpan.FromMilliseconds(220);

    public static IReadOnlyList<BootResult> Results { get; private set; } = [];
    public static string Summary { get; private set; } = "";

    public static async Task RunAsync(Action<BootStep, int, string>? report, Action? startClock, Func<Task>? pump = null)
    {
        var results = new List<BootResult>();
        var steps = BootPlan.Steps;
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            report?.Invoke(step, i, "");
            if (pump is not null) await pump().ConfigureAwait(true);
            var clock = Stopwatch.StartNew();
            var (ok, note) = await Run(step, startClock).ConfigureAwait(true);
            var wait = MinStep - clock.Elapsed;
            if (wait > TimeSpan.Zero) await Task.Delay(wait).ConfigureAwait(true);
            results.Add(new BootResult(step.Id, ok, note));
            App.Session.Log($"{step.Phrase} — {note}", ok ? "info" : "warn");
            report?.Invoke(step, i + 1, note);
            if (pump is not null) await pump().ConfigureAwait(true);
        }
        Results = results;
        Summary = string.Join(" · ", results.Select(r => r.Ok ? r.StepId : r.StepId + " off"));
    }

    static async Task<(bool Ok, string Note)> Run(BootStep step, Action? startClock)
    {
        try
        {
            return step.Id switch
            {
                "framework" => Framework(),
                "controller" => Controller(startClock),
                "audio" => Audio(),
                "video" => Video(),
                "display" => Display(),
                "ndi" => Ndi(),
                "capture" => await Capture(),
                "codec" => await Codec(),
                _ => (true, step.Detail),
            };
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    static (bool, string) Framework()
    {
        var dir = App.DataDir();
        App.LoadStartupState();
        return (true, dir);
    }

    static (bool, string) Controller(Action? startClock)
    {
        startClock?.Invoke();
        App.Session.Changed += () =>
        {
            void Push() => App.Outputs.PushShow(App.Session.PlaybackShow);
            var d = Application.Current?.Dispatcher;
            if (d is null || d.CheckAccess()) Push();
            else d.BeginInvoke(Push);
        };
        App.Session.PlaybackChanged += () =>
        {
            void Push() => App.Outputs.PushShow(App.Session.PlaybackShow);
            var d = Application.Current?.Dispatcher;
            if (d is null || d.CheckAccess()) Push();
            else d.BeginInvoke(Push);
        };
        return (true, $"{Brand.Name} {Brand.Version} · 60 Hz clock");
    }

    static (bool, string) Audio() => (true, AudioEngine.Warm());

    static (bool, string) Video()
    {
        if (GpuEngine.TryStart())
            return (true, GpuEngine.Describe() + " — one decode shared by Stage and Output");
        return (false, GpuEngine.LastError ?? "D3D11 compositor off — MediaElement fallback");
    }

    static (bool, string) Display()
    {
        var screens = Monitors.List();
        var walls = screens.Where(s => !s.IsPrimary).ToList();
        var note = screens.Count == 1
            ? $"{screens[0].Label} {screens[0].Width}×{screens[0].Height} (extend a wall screen in Win+P)"
            : $"{screens.Count} screens · wall {walls.FirstOrDefault()?.Label ?? "extra"}";
        return (true, note);
    }

    static (bool, string) Ndi()
    {
        if (NdiRuntime.Warm())
            return (true, string.IsNullOrEmpty(NdiRuntime.Version) ? "NDI Runtime loaded" : NdiRuntime.Version!);
        return (false, NdiRuntime.LastError ?? "NDI Runtime not found");
    }

    static async Task<(bool, string)> Capture()
    {
        await CaptureHub.RefreshAsync();
        var n = CaptureHub.Devices.Count;
        return (true, n == 0 ? "no capture cards yet" : $"{n} capture device(s)");
    }

    static async Task<(bool, string)> Codec()
    {
        if (await FfmpegTools.DetectAsync())
            return (true, $"ffmpeg {FfmpegTools.H264Encoder}");
        return (true, "ffmpeg not on PATH — H.264 still plays natively");
    }
}
