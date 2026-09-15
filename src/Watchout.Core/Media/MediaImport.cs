using Watchout.Core.Media;
using Watchout.Core.Models;

namespace Watchout.Core.Media;

public static class MediaImport
{
    public static ImportedMedia FromProbe(string sourcePath, string destPath, MediaProbe probe, long bytes, bool linked)
    {
        var id = Ids.New("asset");
        var kind = Codecs.MediaKind(sourcePath);
        var native = kind == AssetKind.Image || Codecs.PlaysNatively(probe.Codec, destPath);
        var needsH264 = !native && Codecs.NeedsH264Transcode(probe.Codec, destPath);
        var canProxy = needsH264 && MediaPolicy.ShouldBuildFullProxy(bytes, probe.Width, probe.Height);
        var silent = kind == AssetKind.Video && !probe.HasAudio;
        var downmix = kind == AssetKind.Audio && WavHeader.NeedsStereoDownmix(probe.Channels);
        var ten = probe.BitDepth >= 10 ? " · 10-bit" : "";
        var ch = probe.Channels > 0 ? $" · {probe.Channels} ch" : "";
        var notes = native && !downmix
            ? $"{Path.GetFileName(sourcePath)} · {MediaPolicy.LargeMediaNote(bytes, linked)} · {Codecs.PlaybackNote(probe.Codec, false, probe.Width, probe.Height)}{ch}{ten}{(silent ? " · no audio track" : "")}"
            : downmix
                ? $"{Path.GetFileName(sourcePath)} · {probe.Channels} channels (WAV allows up to {WavHeader.MaxChannels}) — downmix to stereo for Media Foundation"
            : canProxy
                ? $"{Path.GetFileName(sourcePath)} · {MediaPolicy.LargeMediaNote(bytes, linked)} · GPU codec {probe.Codec} — transcoding to H.264 MP4 for DXVA (not WebM){ten}"
                : $"{Path.GetFileName(sourcePath)} · {MediaPolicy.LargeMediaNote(bytes, linked)} · {Codecs.PlaybackNote(probe.Codec, false, probe.Width, probe.Height)}{ch}{(silent ? " · no audio track" : "")}";

        if (downmix) native = false;

        return new ImportedMedia
        {
            Id = id,
            Name = Path.GetFileNameWithoutExtension(sourcePath),
            Kind = kind,
            Width = probe.Width,
            Height = probe.Height,
            Duration = probe.DurationMs,
            Fps = probe.Fps,
            Url = ToFileUrl(destPath),
            Codec = string.IsNullOrEmpty(probe.Codec) ? Codecs.ExtOf(sourcePath).ToUpperInvariant() : probe.Codec,
            Color = Codecs.ColorFor(kind),
            Optimized = !downmix && !(needsH264 && canProxy),
            Notes = notes,
            OriginalPath = destPath,
            ProxyVersion = native ? Codecs.ProxyVersion : null,
            Bytes = bytes,
            Linked = linked,
            Channels = probe.Channels,
            BitDepth = probe.BitDepth,
            ColorSpace = probe.BitDepth >= 10 ? ColorSpaceTag.Rec2020 : ColorSpaceTag.Rec709,
        };
    }

    public static ImportedMedia WithH264Proxy(ImportedMedia media, string proxyPath, MediaProbe source)
    {
        var silent = media.Kind == AssetKind.Video && !source.HasAudio;
        return new ImportedMedia
        {
            Id = media.Id,
            Name = media.Name,
            Kind = media.Kind,
            Width = media.Width,
            Height = media.Height,
            Duration = media.Duration,
            Fps = media.Fps,
            Url = ToFileUrl(proxyPath),
            Codec = source.Codec,
            Color = media.Color,
            Optimized = true,
            Notes = $"{Path.GetFileName(media.OriginalPath)} · {MediaPolicy.LargeMediaNote(media.Bytes, media.Linked)} · {Codecs.PlaybackNote(source.Codec, true, (int)media.Width, (int)media.Height)}{(silent ? " · no audio track" : "")}",
            OriginalPath = media.OriginalPath,
            ProxyPath = proxyPath,
            ProxyVersion = Codecs.ProxyVersion,
            Bytes = media.Bytes,
            Linked = media.Linked,
            PosterUrl = media.PosterUrl,
            Channels = media.Channels,
            BitDepth = media.BitDepth,
            ColorSpace = media.ColorSpace,
        };
    }

    public static string ToFileUrl(string path)
    {
        var full = Path.GetFullPath(path);
        return new Uri(full).AbsoluteUri;
    }
}
