using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Models;

namespace Watchout.Desktop.Media;

public static class MediaLibrary
{
    public static async Task ImportFilesAsync(IEnumerable<string> paths, ProducerSession session)
    {
        await FfmpegTools.DetectAsync();
        var root = Path.Combine(App.DataDir(), "media");
        foreach (var src in Codecs.CollapseImportPaths(paths))
        {
            if (!File.Exists(src)) continue;
            var bytes = new FileInfo(src).Length;
            var linked = !MediaPolicy.ShouldCopyOnImport(bytes);
            string dest;
            if (linked)
            {
                dest = src;
                session.Log($"Linking {Path.GetFileName(src)} ({MediaPolicy.FormatBytes(bytes)}) — playing from the original disk file", "warn");
            }
            else
            {
                dest = Path.Combine(root, $"{Ids.New("file")}{Path.GetExtension(src)}");
                File.Copy(src, dest, true);
                session.Log($"Copying {Path.GetFileName(src)} ({MediaPolicy.FormatBytes(bytes)}) into the media library");
            }

            var probe = await FfmpegTools.ProbeAsync(dest);
            if (probe.Width == 0 && Codecs.MediaKind(src) == AssetKind.Image)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(dest);
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

            var media = MediaImport.FromProbe(src, dest, probe, bytes, linked);
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

            if (prepared is null && Codecs.NeedsH264Transcode(probe.Codec, dest) && FfmpegTools.Available && MediaPolicy.ShouldBuildFullProxy(bytes, probe.Width, probe.Height))
            {
                var id = media.Id;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var proxy = Path.Combine(root, "proxies", $"{id}.v{Codecs.ProxyVersion}.mp4");
                        session.Log($"Transcoding {Path.GetFileName(src)} → H.264 ({FfmpegTools.H264Encoder}) for DXVA. Not WebM.");
                        await FfmpegTools.TranscodeH264Async(dest, proxy, probe, null, new Progress<string>(m => session.Log(m)));
                        var updated = MediaImport.WithH264Proxy(media, proxy, probe);
                        App.Current.Dispatcher.Invoke(() => session.ApplyImported(updated));
                    }
                    catch (Exception ex)
                    {
                        App.Current.Dispatcher.Invoke(() => session.Log(ex.Message, "error"));
                    }
                });
            }
        }
    }

    static string? FindPreparedH264(string src, string dest)
    {
        foreach (var candidate in Codecs.PreparedSidecarCandidates(src, dest))
        {
            if (File.Exists(candidate) && Codecs.PlaysNatively("h264", candidate))
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
            bmp.UriSource = new Uri(file);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 480;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
