using System.Text.Json;
using Watchout.Core.Models;

namespace Watchout.Core.Media;

public sealed class SdpStream
{
    public string Kind { get; init; } = "video";
    public string Encoding { get; init; } = "";
    public string Destination { get; init; } = "";
    public int Port { get; init; }
    public int ClockRate { get; init; }
    public int Payload { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double Fps { get; init; } = 60;
    public int Channels { get; init; }
}

public static class Sdp
{
    public static IReadOnlyList<SdpStream> Parse(string text)
    {
        var streams = new List<SdpStream>();
        if (string.IsNullOrWhiteSpace(text)) return streams;
        string dest = "";
        string? kind = null, encoding = null;
        int port = 0, clock = 0, payload = 0, width = 0, height = 0, channels = 0;
        double fps = 60;

        void Flush()
        {
            if (kind is null) return;
            streams.Add(new SdpStream
            {
                Kind = kind,
                Encoding = encoding ?? "",
                Destination = dest,
                Port = port,
                ClockRate = clock,
                Payload = payload,
                Width = width,
                Height = height,
                Fps = fps,
                Channels = channels,
            });
            kind = null;
            encoding = null;
            port = clock = payload = width = height = channels = 0;
            fps = 60;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("c=", StringComparison.OrdinalIgnoreCase) && line.Contains(' '))
            {
                dest = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
                continue;
            }
            if (line.StartsWith("m=", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                var parts = line[2..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    kind = parts[0];
                    int.TryParse(parts[1], out port);
                    if (parts.Length >= 4) int.TryParse(parts[3], out payload);
                }
                continue;
            }
            if (line.StartsWith("a=rtpmap:", StringComparison.OrdinalIgnoreCase))
            {
                var body = line["a=rtpmap:".Length..];
                var bits = body.Split([' ', '/'], StringSplitOptions.RemoveEmptyEntries);
                if (bits.Length >= 2) encoding = bits[1];
                if (bits.Length >= 3) int.TryParse(bits[2], out clock);
                if (bits.Length >= 4) int.TryParse(bits[3], out channels);
                continue;
            }
            if (line.StartsWith("a=fmtp:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var token in line.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = token.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (kv.Length != 2) continue;
                    if (kv[0].Contains("width", StringComparison.OrdinalIgnoreCase)) int.TryParse(kv[1], out width);
                    if (kv[0].Contains("height", StringComparison.OrdinalIgnoreCase)) int.TryParse(kv[1], out height);
                    if (kv[0].Contains("rate", StringComparison.OrdinalIgnoreCase) || kv[0].Contains("fps", StringComparison.OrdinalIgnoreCase))
                        double.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fps);
                }
            }
        }
        Flush();
        return streams;
    }

    public static (int Width, int Height, double Fps) VideoSize(IEnumerable<SdpStream> streams)
    {
        var video = streams.FirstOrDefault(s => s.Kind.Equals("video", StringComparison.OrdinalIgnoreCase));
        return (video?.Width > 0 ? video.Width : 1920, video?.Height > 0 ? video.Height : 1080, video?.Fps > 0 ? video.Fps : 60);
    }
}

public sealed class NmosSender
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Transport { get; init; } = "";
    public string ManifestHref { get; init; } = "";
    public string Description { get; init; } = "";
}

public static class Nmos
{
    public static string QuerySendersUrl(string registry)
    {
        var root = registry.Trim().TrimEnd('/');
        if (root.Contains("/x-nmos/", StringComparison.OrdinalIgnoreCase))
            return root.Contains("/senders", StringComparison.OrdinalIgnoreCase) ? root : root + "/senders";
        return root + "/x-nmos/query/v1.3/senders";
    }

    public static IReadOnlyList<NmosSender> ParseSenders(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        var root = doc.RootElement;
        var arr = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("data", out var data) ? data
            : root.TryGetProperty("senders", out var senders) ? senders
            : default;
        if (arr.ValueKind != JsonValueKind.Array) return [];
        var list = new List<NmosSender>();
        foreach (var el in arr.EnumerateArray())
        {
            list.Add(new NmosSender
            {
                Id = Str(el, "id") ?? Ids.New("nmos"),
                Label = Str(el, "label", "description") ?? "ST 2110 sender",
                Transport = Str(el, "transport") ?? "",
                ManifestHref = Str(el, "manifest_href", "manifestHref", "href") ?? "",
                Description = Str(el, "description") ?? "",
            });
        }
        return list;
    }

    static string? Str(System.Text.Json.JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                return v.GetString();
        }
        return null;
    }
}
