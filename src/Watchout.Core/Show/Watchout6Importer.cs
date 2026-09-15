using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Watchout.Core.Models;

namespace Watchout.Core.Persistence;

public sealed class ImportReport
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public List<string> Notes { get; init; } = [];
    public Models.Show? Show { get; init; }
    public bool NativeWatchMe { get; init; }
}

public static class Watchout6Importer
{
    public static ImportReport ImportFile(string path)
    {
        if (!File.Exists(path))
            return Fail($"File not found: {path}");

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K')
            return ImportZip(path, bytes);

        var text = DecodeText(bytes);
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            return ImportJson(path, text);
        if (trimmed.StartsWith('<'))
            return ImportXml(path, text);

        return Fail(
            "That file is not JSON, XML, or a zip WatchMe can read. Dataton WATCHOUT 6 binary .watch shows are not opened here — export JSON from Producer, or save a WatchMe .watchme.json.");
    }

    public static ImportReport ImportJson(string path, string json)
    {
        try
        {
            var show = ShowSerializer.Load(json);
            if (LooksLikeWatchMe(show))
            {
                var notes = new List<string>();
                if (show.Displays.Count == 0) notes.Add("No displays in the file — added a 1920×1080 canvas.");
                return new ImportReport
                {
                    Ok = true,
                    NativeWatchMe = true,
                    Show = show,
                    Message = $"Opened {Path.GetFileName(path)} as a WatchMe show.",
                    Notes = notes,
                };
            }
        }
        catch
        {
            /* fall through to a looser mapping */
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return MapLooseJson(path, doc.RootElement);
        }
        catch (Exception ex)
        {
            return Fail($"JSON did not parse: {ex.Message}");
        }
    }

    static ImportReport ImportZip(string path, byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
            var json = zip.Entries
                .Where(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                            || e.Name.EndsWith(".watch.json", StringComparison.OrdinalIgnoreCase)
                            || e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName.Contains("watchme", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(e => e.Length)
                .FirstOrDefault();
            if (json is null)
                return Fail("The zip has no JSON/XML show inside. WatchMe cannot read a packed Dataton binary .watch.");
            using var reader = new StreamReader(json.Open(), Encoding.UTF8);
            var text = reader.ReadToEnd();
            var report = json.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                ? ImportXml(path, text)
                : ImportJson(path, text);
            if (report.Ok && report.Show is not null)
            {
                report.Notes.Add($"Unpacked {json.FullName} from {Path.GetFileName(path)}.");
            }
            return report;
        }
        catch (Exception ex)
        {
            return Fail($"Zip could not be read: {ex.Message}");
        }
    }

    static ImportReport ImportXml(string path, string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var show = ShowFactory.EmptyShow(NameFrom(doc.Root?.Attribute("name")?.Value ?? doc.Root?.Element("Name")?.Value, path));
            show.Displays.Clear();
            foreach (var el in Descendants(doc, "Display", "Screen", "Output"))
            {
                show.Displays.Add(ShowFactory.EmptyDisplay(new Display
                {
                    Name = Attr(el, "name", "Name") ?? $"Display {show.Displays.Count + 1}",
                    Width = Num(el, "width", "Width") ?? 1920,
                    Height = Num(el, "height", "Height") ?? 1080,
                    X = Num(el, "x", "X") ?? show.Displays.Sum(d => d.Width),
                    Y = Num(el, "y", "Y") ?? 0,
                    Channel = (int)(Num(el, "channel", "Channel") ?? show.Displays.Count + 1),
                    Role = RoleOf(Attr(el, "role", "Role")),
                    KeyChannel = (int)(Num(el, "keyChannel", "KeyChannel") ?? 1),
                }));
            }
            if (show.Displays.Count == 0)
                show.Displays.Add(ShowFactory.EmptyDisplay());

            var tl = show.Timelines[0];
            var layer = tl.Layers[0];
            foreach (var el in Descendants(doc, "Cue", "Clip", "MediaCue"))
            {
                var start = TimeMs(Num(el, "start", "Start", "begin"));
                var duration = TimeMs(Num(el, "duration", "Duration", "length"), 5000);
                tl.Cues.Add(ShowFactory.EmptyCue(new Cue
                {
                    Name = Attr(el, "name", "Name") ?? "Imported cue",
                    LayerId = layer.Id,
                    Start = start,
                    Duration = duration,
                    Position = new Vec3
                    {
                        X = Num(el, "x", "X") ?? 0,
                        Y = Num(el, "y", "Y") ?? 0,
                    },
                }));
            }

            return new ImportReport
            {
                Ok = true,
                Show = show,
                Message = $"Imported XML layout from {Path.GetFileName(path)}. Cue looks and media paths may need a pass in Properties.",
                Notes = ["WATCHOUT 6 XML is mapped best-effort. Binary Dataton .watch is not decoded."],
            };
        }
        catch (Exception ex)
        {
            return Fail($"XML did not parse: {ex.Message}");
        }
    }

    static ImportReport MapLooseJson(string path, JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            root = WrapArray(root);

        var show = ShowFactory.EmptyShow(Str(root, "name", "title", "showName", "show") ?? NameFrom(null, path));
        show.Displays.Clear();
        foreach (var el in Arr(root, "displays", "Displays", "screens", "outputs", "Stages"))
        {
            show.Displays.Add(ShowFactory.EmptyDisplay(new Display
            {
                Name = Str(el, "name", "Name", "label", "id") ?? $"Display {show.Displays.Count + 1}",
                Width = Num(el, "width", "Width", "w") ?? 1920,
                Height = Num(el, "height", "Height", "h") ?? 1080,
                X = Num(el, "x", "X", "left") ?? show.Displays.Sum(d => d.Width),
                Y = Num(el, "y", "Y", "top") ?? 0,
                Channel = (int)(Num(el, "channel", "Channel") ?? show.Displays.Count + 1),
                Role = RoleOf(Str(el, "role", "Role")),
                KeyChannel = (int)(Num(el, "keyChannel", "KeyChannel", "key") ?? 1),
                ColorSpace = ColorOf(Str(el, "colorSpace", "ColorSpace")),
            }));
        }
        if (show.Displays.Count == 0)
            show.Displays.Add(ShowFactory.EmptyDisplay());

        foreach (var el in Arr(root, "assets", "Assets", "media", "Media", "files"))
        {
            var name = Str(el, "name", "Name", "file", "path") ?? $"Asset {show.Assets.Count + 1}";
            var url = Str(el, "url", "Url", "path", "file", "href") ?? "";
            var kind = KindOf(Str(el, "kind", "type", "Kind"), url);
            show.Assets.Add(ShowFactory.EmptyAsset(new Asset
            {
                Name = Path.GetFileNameWithoutExtension(name),
                Kind = kind,
                Url = url,
                OriginalPath = string.IsNullOrEmpty(url) ? null : url,
                Width = Num(el, "width", "Width") ?? 1920,
                Height = Num(el, "height", "Height") ?? 1080,
                Duration = TimeMs(Num(el, "duration", "Duration", "length"), 10_000),
                Codec = Str(el, "codec", "Codec") ?? "",
                Notes = "Imported from a WATCHOUT-style JSON map. Re-link the file in Assets if the path is missing.",
            }));
        }

        var tl = show.Timelines[0];
        var namedTimelines = Arr(root, "timelines", "Timelines");
        if (namedTimelines.Count > 0 && TryGet(namedTimelines[0], "name", "Name") is not null)
            tl.Name = Str(namedTimelines[0], "name", "Name") ?? tl.Name;

        var cueSource = namedTimelines.Count > 0
            ? Arr(namedTimelines[0], "cues", "Cues", "clips")
            : Arr(root, "cues", "Cues", "clips", "mediaCues");
        var layer = tl.Layers.FirstOrDefault() ?? ShowFactory.EmptyLayer("Layer 1", 1);
        if (tl.Layers.Count == 0) tl.Layers.Add(layer);

        var seconds = LooksLikeSeconds(cueSource);
        foreach (var el in cueSource)
        {
            var assetName = Str(el, "asset", "media", "file", "clip");
            var asset = assetName is null
                ? null
                : show.Assets.FirstOrDefault(a => a.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase) || a.Id == assetName);
            var start = Num(el, "start", "Start", "begin", "in") ?? 0;
            var duration = Num(el, "duration", "Duration", "length", "out") ?? (seconds ? 5 : 5000);
            tl.Cues.Add(ShowFactory.EmptyCue(new Cue
            {
                Name = Str(el, "name", "Name") ?? asset?.Name ?? "Imported cue",
                LayerId = layer.Id,
                Start = seconds ? start * 1000 : start,
                Duration = seconds ? duration * 1000 : duration,
                AssetId = asset?.Id,
                Position = new Vec3
                {
                    X = Num(el, "x", "X", "left") ?? 0,
                    Y = Num(el, "y", "Y", "top") ?? 0,
                },
                Opacity = Num(el, "opacity", "Opacity") ?? 100,
            }));
        }

        var notes = new List<string>
        {
            "WATCHOUT 6 JSON is mapped best-effort (displays, cues, media names). Wipe/chroma/tweens and Dataton hardware I/O are not translated.",
        };
        if (show.Assets.Any(a => !string.IsNullOrEmpty(a.Url) && !File.Exists(a.Url) && !a.Url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)))
            notes.Add("Some media paths were kept as text — re-import those files in WatchMe so DXVA can play them.");
        if (tl.Cues.Count == 0)
            notes.Add("No cues were found in the file. Stage still has the imported displays.");

        return new ImportReport
        {
            Ok = true,
            Show = show,
            Message = $"Imported {Path.GetFileName(path)} into a new WatchMe show ({show.Displays.Count} display(s), {tl.Cues.Count} cue(s)).",
            Notes = notes,
        };
    }

    static bool LooksLikeWatchMe(Models.Show show) =>
        !string.IsNullOrEmpty(show.Id)
        && show.Id.StartsWith("show", StringComparison.OrdinalIgnoreCase)
        && show.Timelines.Any(t => t.Layers.Count > 0 && t.Cues.Count + show.Assets.Count + show.Displays.Count > 0);

    static bool LooksLikeSeconds(IReadOnlyList<JsonElement> cues)
    {
        if (cues.Count == 0) return false;
        return cues.All(el =>
        {
            var start = Num(el, "start", "Start", "begin") ?? 0;
            var duration = Num(el, "duration", "Duration", "length") ?? 0;
            return start <= 3600 && duration <= 3600;
        }) && cues.Any(el => (Num(el, "start", "Start") ?? 0) > 0 || (Num(el, "duration", "Duration") ?? 0) > 0 && (Num(el, "duration", "Duration") ?? 0) < 120);
    }

    static DisplayRole RoleOf(string? raw) =>
        raw is not null && raw.Contains("key", StringComparison.OrdinalIgnoreCase) ? DisplayRole.Key : DisplayRole.Fill;

    static ColorSpaceTag ColorOf(string? raw) => raw?.Replace(".", "").Replace("-", "").ToUpperInvariant() switch
    {
        "REC2020" or "BT2020" => ColorSpaceTag.Rec2020,
        "HLG" => ColorSpaceTag.Hlg,
        "PQ" or "HDR10" => ColorSpaceTag.Pq,
        _ => ColorSpaceTag.Rec709,
    };

    static AssetKind KindOf(string? raw, string url)
    {
        var blob = $"{raw} {url}".ToLowerInvariant();
        if (blob.Contains("audio") || blob.Contains(".wav") || blob.Contains(".mp3")) return AssetKind.Audio;
        if (blob.Contains("image") || blob.Contains(".png") || blob.Contains(".jpg")) return AssetKind.Image;
        if (blob.Contains("ndi")) return AssetKind.Ndi;
        return AssetKind.Video;
    }

    static double TimeMs(double? value, double fallback = 0) =>
        value is null ? fallback : value.Value > 0 && value.Value <= 3600 ? value.Value * 1000 : value.Value;

    static string NameFrom(string? name, string path) =>
        string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name;

    static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }

    static ImportReport Fail(string message) => new() { Ok = false, Message = message };

    static JsonElement WrapArray(JsonElement arr)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("cues");
            arr.WriteTo(writer);
            writer.WriteEndObject();
        }
        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    static IReadOnlyList<JsonElement> Arr(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return [];
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array)
                return el.EnumerateArray().ToList();
        }
        return [];
    }

    static string? Str(JsonElement obj, params string[] names)
    {
        var el = TryGet(obj, names);
        if (el is null) return null;
        return el.Value.ValueKind switch
        {
            JsonValueKind.String => el.Value.GetString(),
            JsonValueKind.Number => el.Value.ToString(),
            _ => el.Value.ToString(),
        };
    }

    static double? Num(JsonElement obj, params string[] names)
    {
        var el = TryGet(obj, names);
        if (el is null) return null;
        if (el.Value.ValueKind == JsonValueKind.Number && el.Value.TryGetDouble(out var n)) return n;
        if (el.Value.ValueKind == JsonValueKind.String && double.TryParse(el.Value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p))
            return p;
        return null;
    }

    static JsonElement? TryGet(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var el) && el.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                return el;
        }
        return null;
    }

    static IEnumerable<XElement> Descendants(XDocument doc, params string[] names)
    {
        var set = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return doc.Descendants().Where(e => set.Contains(e.Name.LocalName));
    }

    static string? Attr(XElement el, params string[] names)
    {
        foreach (var name in names)
        {
            var attr = el.Attribute(name);
            if (attr is not null && !string.IsNullOrWhiteSpace(attr.Value)) return attr.Value;
            var child = el.Element(name);
            if (child is not null && !string.IsNullOrWhiteSpace(child.Value)) return child.Value;
        }
        return null;
    }

    static double? Num(XElement el, params string[] names)
    {
        var raw = Attr(el, names);
        return raw is not null && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n
            : null;
    }
}
