using Watchout.Core.Models;

namespace Watchout.Core.Media;

/// <summary>
/// Desktop codec policy: play H.264/H.265 (and other Media Foundation codecs) natively
/// with DXVA/D3D11 hardware decode. Never build VP9/WebM proxies.
/// HAP / DXV / ProRes / DNx can be transcoded to H.264 MP4 when ffmpeg is present.
/// </summary>
public static class Codecs
{
    public const int ProxyVersion = 4;
    public const string NativeDecoder = "Media Foundation + DXVA";

    static readonly HashSet<string> ImageExt = ["png", "jpg", "jpeg", "gif", "webp", "bmp", "svg", "tif", "tiff"];
    static readonly HashSet<string> AudioExt = ["wav", "flac", "ogg", "opus", "mp3", "m4a", "aac", "aif", "aiff", "wma"];
    static readonly HashSet<string> VideoExt = ["mp4", "mov", "mkv", "avi", "webm", "ogv", "m4v", "mxf", "wmv", "mpg", "mpeg", "ts", "mts", "dxv", "hap", "notchlc"];

    static readonly System.Text.RegularExpressions.Regex MfVideo = new(
        @"h\.?264|avc|hev[c1]?|h\.?265|mpeg-?2|mpeg-?4|wmv|vc-?1|mjpeg|jpeg|dvvideo",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly System.Text.RegularExpressions.Regex MfAudio = new(
        @"aac|mp3|pcm|wma|flac|alac|mpeg audio",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly System.Text.RegularExpressions.Regex GpuGpuCodec = new(
        @"prores|hap|dxv|dnx|hevc|h\.?265|apcn|apch|apco|dnxhd|cfhd|cineform|vp8|vp9|av1|av01|theora|notchlc|notch",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly System.Text.RegularExpressions.Regex NeedsH264 = new(
        @"prores|hap|dxv|dnx|apcn|apch|apco|dnxhd|cfhd|cineform|notchlc|notch",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string ExtOf(string filePath)
    {
        var baseName = Path.GetFileName(filePath) ?? "";
        var dot = baseName.LastIndexOf('.');
        return dot >= 0 ? baseName[(dot + 1)..].ToLowerInvariant() : "";
    }

    public static AssetKind MediaKind(string filePath, string mime = "")
    {
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return AssetKind.Image;
        if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return AssetKind.Video;
        if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return AssetKind.Audio;
        var ext = ExtOf(filePath);
        if (ImageExt.Contains(ext)) return AssetKind.Image;
        if (AudioExt.Contains(ext)) return AssetKind.Audio;
        if (VideoExt.Contains(ext)) return AssetKind.Video;
        return AssetKind.Video;
    }

    /// <summary>True when Windows Media Foundation can decode this file with DXVA (H.264/HEVC/MPEG-2/WMV/WAV/AAC/MP3).</summary>
    public static bool PlaysNatively(string codec, string filePath, string mime = "")
    {
        var kind = MediaKind(filePath, mime);
        if (kind == AssetKind.Image) return true;
        var ext = ExtOf(filePath);
        var blob = $"{codec} {mime} {ext}";
        if (kind == AssetKind.Audio)
        {
            if (NeedsStereoDownmix(codec, filePath, 0)) return false;
            if (MfAudio.IsMatch(blob) || ext is "wav" or "mp3" or "m4a" or "aac" or "wma" or "flac") return true;
            return false;
        }

        if (ext is "mp4" or "m4v" or "mov" or "mkv" or "avi" or "wmv" or "mpg" or "mpeg" or "ts" or "mts")
        {
            if (NeedsH264.IsMatch(blob)) return false;
            if (string.IsNullOrWhiteSpace(codec) || MfVideo.IsMatch(blob) || codec.Contains("h264", StringComparison.OrdinalIgnoreCase) || codec.Contains("avc", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return MfVideo.IsMatch(blob);
    }

    public static bool NeedsStereoDownmix(string codec, string filePath, int channels = 0)
    {
        if (channels > 0) return WavHeader.NeedsStereoDownmix(channels);
        if (ExtOf(filePath) == "wav" && File.Exists(filePath) && WavHeader.TryReadFile(filePath, out var wav))
            return WavHeader.NeedsStereoDownmix(wav.Channels);
        return false;
    }

    /// <summary>HAP, Resolume DXV, ProRes, DNx, Notch LC — GPU-show codecs that MF will not play. Transcode to H.264 MP4, never WebM.</summary>
    public static bool NeedsH264Transcode(string codec, string filePath, string mime = "")
    {
        if (HapCodec.IsHap(codec, filePath)) return false;
        var kind = MediaKind(filePath, mime);
        if (kind == AssetKind.Image) return false;
        if (PlaysNatively(codec, filePath, mime)) return false;
        var blob = $"{codec} {mime} {ExtOf(filePath)}";
        if (kind == AssetKind.Audio)
            return NeedsStereoDownmix(codec, filePath) || !MfAudio.IsMatch(blob);
        return true;
    }

    public static string PlaybackNote(string codec, bool usedH264Proxy, int width = 0, int height = 0)
    {
        var size = width > 0 && height > 0 ? $" · {width}×{height}" : "";
        if (!usedH264Proxy)
            return $"Native {NativeDecoder} · {codec}{size} · no WebM";
        return $"H.264 MP4 for DXVA{size} · source {codec}";
    }

    public static string ColorFor(AssetKind kind) => kind switch
    {
        AssetKind.Video => "#38bdf8",
        AssetKind.Audio => "#a78bfa",
        AssetKind.St2110 => "#22d3ee",
        _ => "#f59e0b",
    };

    /// <summary>
    /// HEVC / Dolby Vision / HDR often opens on Intel UHD with no DXGI frame.
    /// Prefer a prepared 8-bit H.264 sidecar when one exists.
    /// </summary>
    public static bool PrefersPreparedH264(string codec, string? filePath)
    {
        var blob = $"{codec} {filePath}";
        return GpuGpuCodec.IsMatch(blob);
    }

    public static string SiblingH264Path(string filePath)
    {
        var ext = ExtOf(filePath);
        if (ext is "mp4" or "m4v") return filePath;
        var dir = Path.GetDirectoryName(filePath) ?? "";
        var name = Path.GetFileNameWithoutExtension(filePath);
        return Path.Combine(dir, name + ".mp4");
    }

    public static IReadOnlyList<string> PreparedSidecarCandidates(string src, string? dest = null)
    {
        dest ??= src;
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { src };
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in new[] { SiblingH264Path(src), SiblingH264Path(dest) })
        {
            if (string.IsNullOrWhiteSpace(raw) || skip.Contains(raw)) continue;
            set.Add(raw);
        }
        return set.ToList();
    }

    /// <summary>
    /// A sidecar is a different file next to the master (clip.mov → clip.mp4).
    /// The master .mp4 itself is not a prepared H.264 — Dolby Vision HEVC
    /// files are .mp4 and were logged as “Using prepared H.264”.
    /// </summary>
    public static bool IsPreparedH264Sidecar(string sourcePath, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (string.Equals(sourcePath, candidate, StringComparison.OrdinalIgnoreCase)) return false;
        var ext = ExtOf(candidate);
        return ext is "mp4" or "m4v";
    }

    public static List<string> CollapseImportPaths(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        return list.Where(p =>
        {
            if (ExtOf(p) != "mp4") return true;
            return !list.Any(other => other != p && SiblingH264Path(other) == p && NeedsH264Transcode("", other));
        }).ToList();
    }

    public static (int Width, int Height) ScaledProxySize(int width, int height, int? maxWidth = null)
    {
        var w = Math.Max(0, width);
        var h = Math.Max(0, height);
        if (maxWidth is > 0 && w > maxWidth.Value)
        {
            h = (int)Math.Round(h * (double)maxWidth.Value / w);
            if (h < 2) h = 2;
            w = maxWidth.Value;
        }
        return (w > 0 ? Math.Max(2, w / 2 * 2) : 0, h > 0 ? Math.Max(2, h / 2 * 2) : 0);
    }

    public static string ProxyScaleFilter(int width, int height, int? maxWidth = null)
    {
        var (w, h) = ScaledProxySize(width, height, maxWidth);
        if (w > 0 && h > 0) return $"scale={w}:{h}";
        return "scale=trunc(iw/2)*2:trunc(ih/2)*2";
    }

    public static string PickH264Encoder(IEnumerable<string> encoderNames)
    {
        var set = encoderNames.Select(e => e.ToLowerInvariant()).ToHashSet();
        if (set.Contains("h264_nvenc")) return "h264_nvenc";
        if (set.Contains("h264_amf")) return "h264_amf";
        if (set.Contains("h264_qsv")) return "h264_qsv";
        return "libx264";
    }

    public static string[] H264TranscodeArgs(string src, string dest, string encoder, int width = 0, int height = 0, int? maxWidth = null, bool video = true) =>
        H264TranscodeArgs(src, dest, encoder, width, height, maxWidth, video, OptimizePreset.Quality, 8, 2);

    public static string[] H264TranscodeArgs(string src, string dest, string encoder, int width, int height, int? maxWidth, bool video, OptimizePreset preset, int bitDepth, int audioChannels)
    {
        if (!video)
        {
            var mix = WavHeader.NeedsStereoDownmix(audioChannels) ? 2 : Math.Max(1, audioChannels);
            return ["-y", "-i", src, "-vn", "-c:a", "aac", "-b:a", "192k", "-ac", mix.ToString(), "-ar", "48000", dest];
        }

        var vf = ProxyScaleFilter(width, height, maxWidth);
        var tenBit = bitDepth >= 10;
        var pix = tenBit ? "yuv420p10le" : "yuv420p";
        var profile = tenBit ? "high10" : "high";
        var crf = preset switch
        {
            OptimizePreset.Fast => "23",
            OptimizePreset.Broadcast => "16",
            _ => "18",
        };
        var x264Preset = preset switch
        {
            OptimizePreset.Fast => "veryfast",
            OptimizePreset.Broadcast => "slow",
            _ => "medium",
        };
        var ac = WavHeader.NeedsStereoDownmix(audioChannels) || audioChannels <= 0 ? 2 : Math.Min(audioChannels, 8);

        var videoCodec = encoder switch
        {
            "h264_nvenc" => new[] { "-c:v", "h264_nvenc", "-preset", preset == OptimizePreset.Fast ? "p1" : "p4", "-rc", "vbr", "-cq", crf, "-b:v", "0", "-profile:v", tenBit ? "main10" : "high" },
            "h264_amf" => new[] { "-c:v", "h264_amf", "-quality", preset == OptimizePreset.Broadcast ? "quality" : "balanced", "-rc", "cqp", "-qp_i", crf, "-qp_p", crf, "-profile:v", "high" },
            "h264_qsv" => new[] { "-c:v", "h264_qsv", "-preset", x264Preset, "-global_quality", crf, "-profile:v", "high" },
            _ => new[] { "-c:v", "libx264", "-preset", x264Preset, "-crf", crf, "-profile:v", profile, "-level", tenBit ? "5.2" : "5.1" },
        };

        return
        [
            "-y", "-i", src,
            "-map", "0:v:0", "-map", "0:a:0?",
            ..videoCodec,
            "-pix_fmt", pix,
            "-vf", vf,
            "-c:a", "aac", "-b:a", "192k", "-ac", ac.ToString(), "-ar", "48000",
            "-movflags", "+faststart",
            dest,
        ];
    }

    public static string[] HapEncodeArgs(string src, string dest, bool alpha = false)
    {
        return
        [
            "-y", "-hide_banner", "-i", src,
            "-map", "0:v:0", "-map", "0:a:0?",
            "-c:v", "hap",
            "-format:v", alpha ? "hap_alpha" : "hap_q",
            "-threads", "2",
            "-c:a", "aac", "-b:a", "192k", "-ac", "2", "-ar", "48000",
            dest,
        ];
    }

    /// <summary>
    /// Older ffmpeg builds reject <c>-chunks</c> as a global option and abort
    /// before HAP Q is written. Plain hap still makes a GPU DXT MOV.
    /// </summary>
    public static string[] HapEncodePlainArgs(string src, string dest)
    {
        return
        [
            "-y", "-hide_banner", "-i", src,
            "-map", "0:v:0", "-map", "0:a:0?",
            "-c:v", "hap",
            "-threads", "2",
            "-c:a", "aac", "-b:a", "192k", "-ac", "2", "-ar", "48000",
            dest,
        ];
    }

    /// <summary>
    /// ffmpeg dumps its configure banner on stderr. The operator needs the
    /// last real error (Unrecognized option, Unknown encoder), not librubberband.
    /// </summary>
    public static string FfmpegUsefulError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return "ffmpeg failed";
        var lines = stderr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();
        string[] keys =
        [
            "Unrecognized option",
            "Unknown encoder",
            "Error splitting",
            "Error opening",
            "No such file",
            "Invalid argument",
            "Unknown format",
        ];
        foreach (var key in keys)
        {
            var hit = lines.LastOrDefault(l => l.Contains(key, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        var last = lines.LastOrDefault(l =>
            !l.StartsWith("ffmpeg version", StringComparison.OrdinalIgnoreCase)
            && !l.StartsWith("libav", StringComparison.OrdinalIgnoreCase)
            && !l.StartsWith("libsw", StringComparison.OrdinalIgnoreCase)
            && !l.Contains("configuration:", StringComparison.OrdinalIgnoreCase)
            && !l.Contains("--enable-", StringComparison.OrdinalIgnoreCase));
        return last ?? "ffmpeg failed";
    }

    public static string[] AudioDownmixArgs(string src, string dest, int channels)
    {
        var ac = WavHeader.NeedsStereoDownmix(channels) ? 2 : Math.Max(1, Math.Min(channels, 8));
        return ["-y", "-i", src, "-vn", "-c:a", "pcm_s24le", "-ac", ac.ToString(), "-ar", "48000", dest];
    }

    public static string H264Cli(string src, string dest, string encoder = "libx264", int width = 0, int height = 0, int? maxWidth = null)
    {
        return string.Join(" ", H264TranscodeArgs(src, dest, encoder, width, height, maxWidth).Select(Quote));
    }

    static string Quote(string value)
    {
        if (!value.Any(ch => ch is ' ' or '\t' or '"')) return value;
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    public static bool NeedsHqRebuild(Asset asset)
    {
        if (asset.Kind is not (AssetKind.Video or AssetKind.Audio)) return false;
        if (string.IsNullOrEmpty(asset.OriginalPath)) return false;
        if (asset.Bytes is > 0 && !MediaPolicy.ShouldBuildFullProxy(asset.Bytes.Value, asset.Width, asset.Height))
            return false;
        if (PlaysNatively(asset.Codec, asset.OriginalPath)) return false;
        if (asset.ProxyVersion == ProxyVersion && !string.IsNullOrEmpty(asset.ProxyPath)) return false;
        return NeedsH264Transcode(asset.Codec, asset.OriginalPath);
    }

    /// <summary>
    /// Prefer the original H.264/MOV/MP4 for DXVA. Fall back to an H.264 sidecar, then a leftover Electron WebM.
    /// </summary>
    public static string PlaybackPath(Asset asset)
    {
        if (!string.IsNullOrWhiteSpace(asset.ProxyPath) && File.Exists(asset.ProxyPath)
            && HapCodec.IsHap(asset.Codec, asset.ProxyPath))
            return asset.ProxyPath;
        if (!string.IsNullOrWhiteSpace(asset.OriginalPath) && File.Exists(asset.OriginalPath)
            && HapCodec.IsHap(asset.Codec, asset.OriginalPath))
            return asset.OriginalPath;
        if (!string.IsNullOrWhiteSpace(asset.ProxyPath) && File.Exists(asset.ProxyPath)
            && IsPreparedH264Sidecar(asset.OriginalPath ?? "", asset.ProxyPath)
            && (asset.Optimized || PrefersPreparedH264(asset.Codec, asset.OriginalPath)))
            return asset.ProxyPath;
        if (!string.IsNullOrWhiteSpace(asset.OriginalPath) && File.Exists(asset.OriginalPath) && PlaysNatively(asset.Codec, asset.OriginalPath))
            return asset.OriginalPath;
        if (!string.IsNullOrWhiteSpace(asset.ProxyPath) && File.Exists(asset.ProxyPath))
            return asset.ProxyPath;
        if (!string.IsNullOrWhiteSpace(asset.OriginalPath) && File.Exists(asset.OriginalPath))
            return asset.OriginalPath;
        if (TryFileUrl(asset.Url) is { } fromUrl && File.Exists(fromUrl))
            return fromUrl;
        return asset.Url;
    }

    public static string? TryFileUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return Uri.UnescapeDataString(uri.LocalPath);
        if (url.StartsWith("watchout:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("watchme:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("procedural:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("capture:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("st2110:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("nmos:", StringComparison.OrdinalIgnoreCase))
            return null;
        if (File.Exists(url)) return url;
        return null;
    }
}
