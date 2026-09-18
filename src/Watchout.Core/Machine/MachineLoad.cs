using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Playback;

namespace Watchout.Core.Machine;

public enum LoadLevel
{
    Ok,
    Tight,
    Full
}

public readonly record struct ShowLoad(int Videos, int Ndi, int Capture, int Outputs, long Pixels)
{
    public const double UhdPixels = 3840.0 * 2160.0;

    public double FourK => UhdPixels <= 0 ? 0 : Pixels / UhdPixels;

    public string Line()
    {
        var parts = new List<string>();
        if (Videos > 0) parts.Add(Videos == 1 ? "1 video" : $"{Videos} videos");
        if (Ndi > 0) parts.Add(Ndi == 1 ? "1 NDI" : $"{Ndi} NDI");
        if (Capture > 0) parts.Add(Capture == 1 ? "1 capture" : $"{Capture} capture");
        if (Outputs > 0) parts.Add(Outputs == 1 ? "1 output" : $"{Outputs} outputs");
        if (FourK >= 0.9) parts.Add($"{FourK:0.0}×4K");
        return parts.Count == 0 ? "no live video" : string.Join(" · ", parts);
    }
}

public readonly record struct MachineSample(
    double CpuPercent,
    long RamUsedBytes,
    long RamTotalBytes,
    long GpuUsedBytes,
    long GpuTotalBytes,
    long ProcessBytes,
    string GpuName,
    ShowLoad Show);

/// <summary>
/// Grades CPU / RAM / GPU so Producer can warn before another 4K clip or
/// Stage output freezes the PC. Thresholds are conservative on purpose.
/// </summary>
public static class MachineLoad
{
    public const double CpuTight = 75;
    public const double CpuFull = 90;
    public const double RamTight = 88;
    public const double RamFull = 95;
    /// <summary>
    /// Once RAM is Tight, stay Tight until usage falls here. Sitting on 88%
    /// (6.8–7.0 / 7.7 GB) used to flip Tight/Ok every second and flood the Log
    /// so it looked like the laptop could not play 4K.
    /// </summary>
    public const double RamRelease = 80;
    public const double GpuTight = 75;
    public const double GpuFull = 90;
    public const long FourKHeadroomBytes = 512L * 1024 * 1024;

    public static ShowLoad CountShow(Models.Show? show, int liveOutputs)
    {
        if (show is null) return new ShowLoad(0, 0, 0, Math.Max(0, liveOutputs), 0);
        var videos = 0;
        var ndi = 0;
        var capture = 0;
        var pixels = 0L;
        foreach (var ev in PlaybackClock.VisibleMedia(show))
        {
            var asset = show.Assets.FirstOrDefault(a => a.Id == ev.Cue.AssetId);
            if (asset is null) continue;
            if (LiveSources.IsCapture(asset))
            {
                capture++;
                pixels += PixelCount(asset);
            }
            else if (LiveSources.NdiSourceName(asset) is { Length: > 0 } || LiveSources.IsNdi(asset))
            {
                ndi++;
                pixels += PixelCount(asset);
            }
            else if (asset.Kind == AssetKind.Video)
            {
                videos++;
                pixels += PixelCount(asset);
            }
        }
        return new ShowLoad(videos, ndi, capture, Math.Max(0, liveOutputs), pixels);
    }

    public static double Percent(long used, long total)
    {
        if (total <= 0) return 0;
        return Math.Clamp(100.0 * used / total, 0, 100);
    }

    public static LoadLevel FromPercent(double percent, double tight, double full)
    {
        if (percent >= full) return LoadLevel.Full;
        if (percent >= tight) return LoadLevel.Tight;
        return LoadLevel.Ok;
    }

    public static LoadLevel CpuLevel(MachineSample s) =>
        FromPercent(s.CpuPercent, CpuTight, CpuFull);

    public static LoadLevel RamLevel(MachineSample s) => RamLevel(s, LoadLevel.Ok);

    public static LoadLevel RamLevel(MachineSample s, LoadLevel previous)
    {
        if (s.RamTotalBytes <= 0) return LoadLevel.Ok;
        return StickyPercent(Percent(s.RamUsedBytes, s.RamTotalBytes), previous, RamTight, RamFull, RamRelease);
    }

    /// <summary>
    /// Enter Tight at <paramref name="tight"/>, leave only below
    /// <paramref name="release"/> so a meter sitting on the line does not flap.
    /// </summary>
    public static LoadLevel StickyPercent(
        double percent, LoadLevel previous, double tight, double full, double release)
    {
        if (previous == LoadLevel.Full)
            return percent >= full ? LoadLevel.Full : percent >= tight ? LoadLevel.Tight : LoadLevel.Ok;
        if (previous == LoadLevel.Tight)
        {
            if (percent >= full) return LoadLevel.Full;
            if (percent >= release) return LoadLevel.Tight;
            return LoadLevel.Ok;
        }
        return FromPercent(percent, tight, full);
    }

    public static LoadLevel GpuLevel(MachineSample s) =>
        s.GpuTotalBytes <= 0 ? LoadLevel.Ok : FromPercent(Percent(s.GpuUsedBytes, s.GpuTotalBytes), GpuTight, GpuFull);

    public static LoadLevel Grade(MachineSample s) => Grade(s, RamLevel(s));

    public static LoadLevel Grade(MachineSample s, LoadLevel ram)
    {
        var cpu = CpuLevel(s);
        var gpu = GpuLevel(s);
        if (cpu == LoadLevel.Full || ram == LoadLevel.Full || gpu == LoadLevel.Full) return LoadLevel.Full;
        if (cpu == LoadLevel.Tight || ram == LoadLevel.Tight || gpu == LoadLevel.Tight) return LoadLevel.Tight;
        return LoadLevel.Ok;
    }

    /// <summary>
    /// RAM-only Tight/Full is "close a browser" — one 4K H.264 is still OK.
    /// Keep it on the meter; do not spam the Log as a machine failure.
    /// GPU/CPU Tight and Full still warn (that is the frame path).
    /// </summary>
    public static bool LogAsMachineWarn(MachineSample s) => LogAsMachineWarn(s, RamLevel(s));

    public static bool LogAsMachineWarn(MachineSample s, LoadLevel ram)
    {
        _ = ram;
        var cpu = CpuLevel(s);
        var gpu = GpuLevel(s);
        if (cpu == LoadLevel.Full || gpu == LoadLevel.Full) return true;
        if (cpu == LoadLevel.Tight || gpu == LoadLevel.Tight) return true;
        return false;
    }

    public static bool CanLoadMore(MachineSample s) => Grade(s) != LoadLevel.Full;

    public static bool CanLoadAnother4K(MachineSample s)
    {
        if (!CanLoadMore(s)) return false;
        if (s.GpuTotalBytes > 0 && s.GpuTotalBytes - s.GpuUsedBytes < FourKHeadroomBytes) return false;
        if (CpuLevel(s) == LoadLevel.Tight && s.Show.FourK >= 1) return false;
        return true;
    }

    public static string Headline(MachineSample s) => Headline(s, RamLevel(s));

    public static string Headline(MachineSample s, LoadLevel ram)
    {
        var gpu = GpuLevel(s);
        var cpu = CpuLevel(s);
        if (gpu == LoadLevel.Full) return "Full — GPU memory is gone; stop Output or drop a layer";
        if (ram == LoadLevel.Full) return "Full — RAM is gone; close other apps";
        if (cpu == LoadLevel.Full) return "Full — CPU is maxed; pause or drop a live layer";
        if (gpu == LoadLevel.Tight)
            return CanLoadAnother4K(s)
                ? "Tight — GPU is busy; another 1080p is safer than another 4K"
                : "Tight — GPU is busy; another 4K may hitch";
        if (ram == LoadLevel.Tight) return "Tight — RAM is high; close browsers (this 4K is still OK)";
        if (cpu == LoadLevel.Tight) return "Tight — CPU is busy; another 4K may hitch";
        return "OK — room to load more Stage / video";
    }

    public static string Advice(MachineSample s) => Advice(s, RamLevel(s));

    public static string Advice(MachineSample s, LoadLevel ram)
    {
        var gpu = GpuLevel(s);
        var cpu = CpuLevel(s);
        if (gpu == LoadLevel.Full)
            return "GPU memory is full. Stop Output or drop a layer before adding video.";
        if (ram == LoadLevel.Full)
            return "System memory is full. Close other apps before loading more video.";
        if (cpu == LoadLevel.Full)
            return "CPU is maxed. Stage preview was grabbing 4K ffmpeg stills too often — it now holds the last frame. Pause if the wall still hitchs.";
        if (gpu == LoadLevel.Tight)
            return "GPU is busy. One more 1080p is safer than another 4K.";
        if (ram == LoadLevel.Tight)
            return "RAM is high (Windows + other apps). Close browsers. One 8-bit H.264 4K still plays; a second 4K decode on Stage is what filled RAM.";
        if (cpu == LoadLevel.Tight)
            return "CPU is busy. Another 4K file may hitch Stage and the wall.";
        if (s.Show.FourK >= 1.8)
            return "4K is already on the wall — Stage shows the same GPU stack.";
        return "CPU, RAM, and GPU have headroom for more video.";
    }

    public static string WarnLine(MachineSample s) =>
        $"{CpuLine(s)} · {RamLine(s)} · {GpuLine(s)} — {Headline(s)}";

    public static string CpuLine(MachineSample s) => $"CPU  {CpuValue(s)}";

    public static string RamLine(MachineSample s) => $"RAM  {RamValue(s)}";

    public static string GpuLine(MachineSample s) => $"GPU  {GpuValue(s)}";

    public static string CpuValue(MachineSample s) => $"{s.CpuPercent:0}%";

    public static string RamValue(MachineSample s) => PairBytes(s.RamUsedBytes, s.RamTotalBytes);

    public static string GpuValue(MachineSample s) => PairBytes(s.GpuUsedBytes, s.GpuTotalBytes);

    public static string PairBytes(long used, long total)
    {
        if (total <= 0) return "—";
        const double gb = 1024.0 * 1024 * 1024;
        const double mb = 1024.0 * 1024;
        if (total >= gb)
            return $"{used / gb:0.0} / {total / gb:0.0} GB";
        return $"{used / mb:0} / {total / mb:0} MB";
    }

    public static string CaptionLine(MachineSample s)
    {
        var gpu = string.IsNullOrWhiteSpace(s.GpuName) ? "GPU" : s.GpuName.Trim();
        return $"{gpu} · WatchMe {Bytes(s.ProcessBytes)} · {s.Show.Line()}";
    }

    public static string DetailLine(MachineSample s) =>
        $"{CpuLine(s)} · {RamLine(s)} · {GpuLine(s)} · {CaptionLine(s)}";

    public static string Bytes(long n)
    {
        n = Math.Max(0, n);
        const double gb = 1024.0 * 1024 * 1024;
        const double mb = 1024.0 * 1024;
        if (n >= 10L * 1024 * 1024 * 1024) return $"{n / gb:0} GB";
        if (n >= 1024L * 1024 * 1024) return $"{n / gb:0.0} GB";
        if (n >= 1024L * 1024) return $"{n / mb:0} MB";
        return $"{n} B";
    }

    static long PixelCount(Asset asset)
    {
        var w = asset.Width > 0 ? asset.Width : 1920;
        var h = asset.Height > 0 ? asset.Height : 1080;
        return (long)(w * h);
    }
}
