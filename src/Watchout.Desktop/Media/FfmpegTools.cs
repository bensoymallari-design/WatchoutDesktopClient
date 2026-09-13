using System.Diagnostics;
using System.Text.Json;
using Watchout.Core.Media;
using Watchout.Core.Models;

namespace Watchout.Desktop.Media;

public static class FfmpegTools
{
    public static string? FfmpegPath { get; private set; }
    public static string? FfprobePath { get; private set; }
    public static string H264Encoder { get; private set; } = "libx264";

    public static bool Available => FfmpegPath is not null && FfprobePath is not null;

    public static async Task<bool> DetectAsync()
    {
        FfmpegPath = await FirstWorking(Candidates("ffmpeg"));
        FfprobePath = await FirstWorking(Candidates("ffprobe"));
        if (FfmpegPath is not null)
        {
            var encoders = await RunAsync(FfmpegPath, ["-hide_banner", "-encoders"]);
            H264Encoder = Codecs.PickH264Encoder(ParseEncoders(encoders.Stdout));
        }
        return Available;
    }

    public static async Task<MediaProbe> ProbeAsync(string filePath)
    {
        var fallback = new MediaProbe
        {
            Codec = Codecs.ExtOf(filePath).ToUpperInvariant(),
            DurationMs = 10_000,
            Fps = 60,
        };
        if (FfprobePath is null) return fallback;
        var result = await RunAsync(FfprobePath, ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", filePath]);
        if (result.Code != 0) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(result.Stdout);
            var root = doc.RootElement;
            JsonElement? video = null, audio = null;
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var s in streams.EnumerateArray())
                {
                    var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : "";
                    if (type == "video" && video is null) video = s;
                    if (type == "audio" && audio is null) audio = s;
                }
            }
            var durSec = 10.0;
            if (root.TryGetProperty("format", out var fmt) && fmt.TryGetProperty("duration", out var d) && double.TryParse(d.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ds))
                durSec = ds;
            var fps = 60.0;
            if (video is { } v && v.TryGetProperty("avg_frame_rate", out var rate) && rate.GetString() is { } r && r.Contains('/'))
            {
                var parts = r.Split('/');
                if (double.TryParse(parts[0], out var n) && double.TryParse(parts[1], out var den) && den != 0)
                    fps = n / den;
            }
            var codecV = video is { } vv && vv.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;
            var codecA = audio is { } aa && aa.TryGetProperty("codec_name", out var an) ? an.GetString() : null;
            return new MediaProbe
            {
                Width = video is { } w && w.TryGetProperty("width", out var width) ? width.GetInt32() : 0,
                Height = video is { } h && h.TryGetProperty("height", out var height) ? height.GetInt32() : 0,
                DurationMs = Math.Max(250, Math.Round(durSec * 1000)),
                Fps = fps > 1 && double.IsFinite(fps) ? fps : 60,
                Codec = string.Join(" + ", new[] { codecV, codecA }.Where(s => !string.IsNullOrEmpty(s))),
                HasAudio = audio is not null,
            };
        }
        catch
        {
            return fallback;
        }
    }

    public static async Task TranscodeH264Async(string src, string dest, MediaProbe probe, int? maxWidth, IProgress<string>? progress)
    {
        if (FfmpegPath is null) throw new InvalidOperationException("ffmpeg not found");
        var args = Codecs.H264TranscodeArgs(src, dest, H264Encoder, probe.Width, probe.Height, maxWidth, video: true);
        var result = await RunAsync(FfmpegPath, args, line =>
        {
            if (line.Contains("time=")) progress?.Report(line.Trim());
        });
        if (result.Code != 0)
            throw new InvalidOperationException(result.Stderr.Length > 400 ? result.Stderr[^400..] : result.Stderr);
    }

    static IEnumerable<string> ParseEncoders(string stdout)
    {
        foreach (var line in stdout.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 8) continue;
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) yield return parts[1];
        }
    }

    static IEnumerable<string> Candidates(string exe)
    {
        var win = OperatingSystem.IsWindows();
        var name = win ? exe + ".exe" : exe;
        yield return name;
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine("C:\\ffmpeg\\bin", name);
        yield return Path.Combine(pf, "ffmpeg", "bin", name);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", name);
        yield return Path.Combine("C:\\ProgramData\\chocolatey\\bin", name);
    }

    static async Task<string?> FirstWorking(IEnumerable<string> bins)
    {
        foreach (var bin in bins)
        {
            try
            {
                var result = await RunAsync(bin, ["-version"]);
                if (result.Code == 0) return bin;
            }
            catch
            {
                /* next */
            }
        }
        return null;
    }

    public static async Task<(string Stdout, string Stderr, int Code)> RunAsync(string file, IReadOnlyList<string> args, Action<string>? onStderr = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = new Process { StartInfo = psi };
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            onStderr?.Invoke(e.Data);
        };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();
        return (stdout.ToString(), stderr.ToString(), proc.ExitCode);
    }
}
