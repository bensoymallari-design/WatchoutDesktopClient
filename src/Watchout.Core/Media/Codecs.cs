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
    static readonly HashSet<string> VideoExt = ["mp4", "mov", "mkv", "avi", "webm", "ogv", "m4v", "mxf", "wmv", "mpg", "mpeg", "ts", "mts", "dxv", "hap"];

    static readonly System.Text.RegularExpressions.Regex MfVideo = new(
        @"h\.?264|avc|hev[c1]?|h\.?265|mpeg-?2|mpeg-?4|wmv|vc-?1|mjpeg|jpeg|dvvideo",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly System.Text.RegularExpressions.Regex MfAudio = new(
        @"aac|mp3|pcm|wma|flac|alac|mpeg audio",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly System.Text.RegularExpressions.Regex GpuGpuCodec = new(
        @"prores|hap|dxv|dnx|hevc|h\.?265|apcn|apch|apco|dnxhd|cfhd|cineform|vp8|vp9|av1|av01|theora",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly System.Text.RegularExpressions.Regex NeedsH264 = new(
        @"prores|hap|dxv|dnx|apcn|apch|apco|dnxhd|cfhd|cineform",
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

    /// <summary>HAP, Resolume DXV, ProRes, DNx — GPU-show codecs that MF will not play. Transcode to H.264 MP4, never WebM.</summary>
    public static bool NeedsH264Transcode(string codec, string filePath, string mime = "")
    {
        var kind = MediaKind(filePath, mime);
        if (kind == AssetKind.Image) return false;
        if (PlaysNatively(codec, filePath, mime)) return false;
        var blob = $"{codec} {mime} {ExtOf(filePath)}";
        if (kind == AssetKind.Audio) return !MfAudio.IsMatch(blob);
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
        _ => "#f59e0b",
    };

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
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SiblingH264Path(src),
            SiblingH264Path(dest),
        }.ToList();
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

    public static string[] H264TranscodeArgs(string src, string dest, string encoder, int width = 0, int height = 0, int? maxWidth = null, bool video = true)
    {
        if (!video)
        {
            return ["-y", "-i", src, "-vn", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", dest];
        }

        var vf = ProxyScaleFilter(width, height, maxWidth);
        var videoCodec = encoder switch
        {
            "h264_nvenc" => new[] { "-c:v", "h264_nvenc", "-preset", "p4", "-rc", "vbr", "-cq", "19", "-b:v", "0", "-profile:v", "high" },
            "h264_amf" => new[] { "-c:v", "h264_amf", "-quality", "quality", "-rc", "cqp", "-qp_i", "18", "-qp_p", "20", "-profile:v", "high" },
            "h264_qsv" => new[] { "-c:v", "h264_qsv", "-preset", "medium", "-global_quality", "22", "-profile:v", "high" },
            _ => new[] { "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-profile:v", "high", "-level", "5.1" },
        };

        return
        [
            "-y", "-i", src,
            "-map", "0:v:0", "-map", "0:a:0?",
            ..videoCodec,
            "-pix_fmt", "yuv420p",
            "-vf", vf,
            "-c:a", "aac", "-b:a", "192k", "-ac", "2", "-ar", "48000",
            "-movflags", "+faststart",
            dest,
        ];
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
        if (url.StartsWith("watchout:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("procedural:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;
        if (File.Exists(url)) return url;
        return null;
    }
}
