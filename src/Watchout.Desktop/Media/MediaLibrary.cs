using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Core.Stage;

namespace Watchout.Desktop.Media;

public static class MediaLibrary
{
    public static async Task<List<string>> ImportFilesAsync(IEnumerable<string> paths, ProducerSession session)
    {
        await FfmpegTools.DetectAsync();
        var root = Path.Combine(App.DataDir(), "media");
        var ids = new List<string>();
        foreach (var src in Codecs.CollapseImportPaths(paths))
        {
            if (!File.Exists(src)) continue;
            var bytes = new FileInfo(src).Length;
            var dest = src;
            session.Log($"Linking {Path.GetFileName(src)} ({MediaPolicy.FormatBytes(bytes)}) — playing from the original disk file, not copying into AppData");

            var probe = await FfmpegTools.ProbeAsync(dest);
            if (Codecs.ExtOf(src) == "wav" && WavHeader.TryReadFile(dest, out var wav))
            {
                probe.Channels = wav.Channels;
                probe.BitDepth = wav.BitsPerSample;
                probe.HasAudio = true;
                if (string.IsNullOrEmpty(probe.Codec)) probe.Codec = $"pcm_s{wav.BitsPerSample}le";
            }
            if (probe.Width == 0 && Codecs.MediaKind(src) == AssetKind.Image)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = LocalUri(dest);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    probe.Width = bmp.PixelWidth;
                    probe.Height = bmp.PixelHeight;
                    probe.Codec = Codecs.ExtOf(src).ToUpperInvariant();
                }
                catch { /* still import */ }
            }

            var prepared = FindPreparedH264(src, dest);
            if (prepared is not null)
            {
                probe = await FfmpegTools.ProbeAsync(prepared);
                dest = prepared;
                session.Log($"Using prepared H.264 next to {Path.GetFileName(src)} — DXVA playback starts immediately");
            }

            var media = MediaImport.FromProbe(src, dest, probe, bytes, linked: true);
            if (prepared is not null)
            {
                media.ProxyPath = prepared;
                media.ProxyVersion = Codecs.ProxyVersion;
                media.Optimized = true;
                media.Url = MediaImport.ToFileUrl(prepared);
            }

            if (Codecs.MediaKind(src) == AssetKind.Video)
                media.PosterUrl = await ExtractPosterAsync(media.Id, dest);

            session.ApplyImported(media);
            ids.Add(media.Id);

            var video = Codecs.MediaKind(src) == AssetKind.Video;
            if (video && !HapCodec.IsHap(probe.Codec, dest) && FfmpegTools.Available
                && MediaPolicy.ShouldBuildHap(bytes, probe.Width, probe.Height)
                && HapCodec.NeedsHapEncode(probe.Codec, dest))
            {
                if (!FfmpegTools.HasHapEncoder)
                {
                    session.Log("ffmpeg has no hap encoder — Play stays DXVA. Install a full ffmpeg with hap (not essentials-only).", "warn");
                }
                else
                {
                var id = media.Id;
                var file = dest;
                var name = Path.GetFileName(src);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var proxy = Path.Combine(root, "proxies", $"{id}.hap.mov");
                        Directory.CreateDirectory(Path.GetDirectoryName(proxy)!);
                        UiLog(session, $"Encoding HAP Q of {name} — Resolume Alley method (GPU DXT texture). Play uses the original until HAP is ready.");
                        await FfmpegTools.HapEncodeAsync(file, proxy, alpha: false, progress: null);
                        var url = MediaImport.ToFileUrl(proxy);
                        Ui(session, () => session.PushAssetRevision(id, url, proxy, "HAP Q · Resolume Alley GPU texture"));
                    }
                    catch (Exception ex)
                    {
                        UiLog(session, $"HAP encode failed: {ex.Message}", "error");
                    }
                });
                }
            }
            else if (prepared is null && FfmpegTools.Available && MediaPolicy.ShouldBuildFullProxy(bytes, probe.Width, probe.Height)
                && (Codecs.NeedsH264Transcode(probe.Codec, dest) || WavHeader.NeedsStereoDownmix(probe.Channels)))
            {
                var id = media.Id;
                var audioOnly = Codecs.MediaKind(src) == AssetKind.Audio || WavHeader.NeedsStereoDownmix(probe.Channels) && probe.Width == 0;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (audioOnly)
                        {
                            var proxy = Path.Combine(root, "proxies", $"{id}.stereo.wav");
                            UiLog(session, $"Downmixing {Path.GetFileName(src)} ({probe.Channels} ch) → stereo WAV for Media Foundation");
                            if (FfmpegTools.FfmpegPath is null) throw new InvalidOperationException("ffmpeg not found");
                            var result = await FfmpegTools.RunAsync(FfmpegTools.FfmpegPath, Codecs.AudioDownmixArgs(dest, proxy, probe.Channels));
                            if (result.Code != 0) throw new InvalidOperationException(result.Stderr);
                            var updated = MediaImport.WithH264Proxy(media, proxy, probe);
                            updated.Codec = "pcm stereo";
                            updated.Notes = $"{Path.GetFileName(src)} · {probe.Channels} ch downmixed to stereo";
                            Ui(session, () => session.ApplyImported(updated));
                        }
                        else
                        {
                            var proxy = Path.Combine(root, "proxies", $"{id}.v{Codecs.ProxyVersion}.mp4");
                            UiLog(session, $"Transcoding {Path.GetFileName(src)} → H.264 ({FfmpegTools.H264Encoder}) for DXVA. Not WebM.");
                            await FfmpegTools.TranscodeH264Async(dest, proxy, probe, null, progress: null);
                            var updated = MediaImport.WithH264Proxy(media, proxy, probe);
                            Ui(session, () => session.ApplyImported(updated));
                        }
                    }
                    catch (Exception ex)
                    {
                        UiLog(session, ex.Message, "error");
                    }
                });
            }
        }
        return ids;
    }

    public static async Task CreateVersionAsync(string assetId, ProducerSession session)
    {
        await FfmpegTools.DetectAsync();
        var asset = session.Show?.Assets.FirstOrDefault(a => a.Id == assetId);
        if (asset is null)
        {
            session.Log("Select an imported clip in Assets first", "warn");
            return;
        }
        if (asset.Kind is not (AssetKind.Video or AssetKind.Audio))
        {
            session.Log("Create version is for video or audio files", "warn");
            return;
        }
        if (!FfmpegTools.Available)
        {
            session.Log("ffmpeg is not on PATH — install it to create an H.264 version", "warn");
            return;
        }
        var src = Codecs.PlaybackPath(asset);
        var file = Codecs.TryFileUrl(src) ?? (File.Exists(src) ? src : asset.OriginalPath);
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            session.Log($"No file on disk for {asset.Name}", "warn");
            return;
        }
        var probe = await FfmpegTools.ProbeAsync(file);
        var dest = Path.Combine(App.DataDir(), "media", "proxies", $"{asset.Id}.v{DateTime.UtcNow:yyyyMMddHHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        session.Log($"Creating H.264 version of {asset.Name} ({FfmpegTools.H264Encoder}) — not WebM");
        try
        {
            await FfmpegTools.TranscodeH264Async(file, dest, probe, null, progress: null);
            var url = MediaImport.ToFileUrl(dest);
            Ui(session, () => session.PushAssetRevision(assetId, url, dest, $"H.264 version · {FfmpegTools.H264Encoder} · DXVA"));
        }
        catch (Exception ex)
        {
            session.Log(ex.Message, "error");
        }
    }

    public static async Task EncodeHapAsync(string assetId, ProducerSession session)
    {
        await FfmpegTools.DetectAsync();
        var asset = session.Show?.Assets.FirstOrDefault(a => a.Id == assetId);
        if (asset is null || asset.Kind != AssetKind.Video)
        {
            session.Log("Select a video in Assets, then Encode HAP", "warn");
            return;
        }
        if (!FfmpegTools.Available)
        {
            session.Log("ffmpeg is not on PATH — HAP encode needs ffmpeg built with libsnappy/hap", "warn");
            return;
        }
        var src = Codecs.PlaybackPath(asset);
        var file = Codecs.TryFileUrl(src) ?? (File.Exists(src) ? src : asset.OriginalPath);
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            session.Log($"No file on disk for {asset.Name}", "warn");
            return;
        }
        var dest = Path.Combine(App.DataDir(), "media", "proxies", $"{asset.Id}.hap.mov");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        session.Log($"Encoding HAP Q of {asset.Name} — Resolume Alley GPU texture (DXT, no RGB32 RAM copy)");
        try
        {
            await FfmpegTools.HapEncodeAsync(file, dest, alpha: false, progress: null);
            var url = MediaImport.ToFileUrl(dest);
            Ui(session, () => session.PushAssetRevision(assetId, url, dest, "HAP Q · Resolume Alley GPU texture"));
        }
        catch (Exception ex)
        {
            session.Log($"HAP encode failed: {ex.Message}", "error");
        }
    }

    static void Ui(ProducerSession session, Action action)
    {
        _ = session;
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) action();
        else d.Invoke(action);
    }

    static void UiLog(ProducerSession session, string message, string level = "info") =>
        Ui(session, () => session.Log(message, level));

    static string? FindPreparedH264(string src, string dest)
    {
        foreach (var candidate in Codecs.PreparedSidecarCandidates(src, dest))
        {
            if (File.Exists(candidate) && Codecs.IsPreparedH264Sidecar(src, candidate) && Codecs.PlaysNatively("h264", candidate))
                return candidate;
        }
        return null;
    }

    static async Task<string?> ExtractPosterAsync(string id, string src)
    {
        if (!FfmpegTools.Available || FfmpegTools.FfmpegPath is null) return null;
        var poster = Path.Combine(App.DataDir(), "media", "proxies", $"{id}.poster.jpg");
        var result = await FfmpegTools.RunAsync(FfmpegTools.FfmpegPath, ["-y", "-ss", "0.15", "-i", src, "-frames:v", "1", "-q:v", "3", poster]);
        return result.Code == 0 && File.Exists(poster) ? MediaImport.ToFileUrl(poster) : null;
    }

    public static ImageSource? LoadStill(Asset asset)
    {
        if (DemoArt.ForUrl(asset.Url, Math.Max(64, (int)asset.Width), Math.Max(64, (int)asset.Height)) is { } demo)
            return demo;
        var path = asset.PosterUrl ?? Codecs.PlaybackPath(asset);
        var file = Codecs.TryFileUrl(path) ?? (File.Exists(path) ? path : null);
        if (file is null || !File.Exists(file)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = LocalUri(file);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static Uri LocalUri(string path)
    {
        if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(path, UriKind.Absolute, out var file))
            return file;
        return new Uri(Path.GetFullPath(path));
    }

    public static BitmapSource? ChromaKey(BitmapSource source, string hex, double tolerance)
    {
        if (!CueLooks.TryParseHex(hex, out var kr, out var kg, out var kb)) return null;
        try
        {
            var conv = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            var w = conv.PixelWidth;
            var h = conv.PixelHeight;
            var stride = w * 4;
            var pixels = new byte[h * stride];
            conv.CopyPixels(pixels, stride, 0);
            var t = Math.Max(8, tolerance) * 2.55;
            var limit = t * 3;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var d = Math.Abs(pixels[i + 2] - kr) + Math.Abs(pixels[i + 1] - kg) + Math.Abs(pixels[i] - kb);
                if (d < limit) pixels[i + 3] = 0;
            }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
