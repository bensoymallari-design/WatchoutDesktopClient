using System.Text.Json;
using System.Text.Json.Serialization;
using Watchout.Core.Models;

namespace Watchout.Core.Persistence;

public static class ShowSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new OutputTypeConverter() },
    };

    public static string Save(Models.Show show) => JsonSerializer.Serialize(show, Options);

    public static Models.Show Load(string json)
    {
        var show = JsonSerializer.Deserialize<Models.Show>(json, Options)
                   ?? throw new InvalidDataException("Show JSON was empty");
        Normalize(show);
        return show;
    }

    public static List<RecentShow> LoadRecents(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<RecentShow>>(json, Options) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string SaveRecents(IEnumerable<RecentShow> recents) =>
        JsonSerializer.Serialize(recents.ToList(), Options);

    public static string SaveCue(Cue cue) => JsonSerializer.Serialize(cue, Options);

    public static Cue LoadCue(string json) =>
        JsonSerializer.Deserialize<Cue>(json, Options) ?? new Cue();

    public static Models.Show Clone(Models.Show show) => Load(Save(show));

    public static Cue CloneCue(Cue cue)
    {
        var copy = LoadCue(SaveCue(cue));
        copy.Id = Ids.New("cue");
        return copy;
    }

    static void Normalize(Models.Show show)
    {
        show.Prefs ??= new ShowPrefs();
        show.Assets ??= [];
        show.Displays ??= [];
        show.Timelines ??= [];
        show.Nodes ??= [];
        show.AudioDevices ??= [];
        show.CaptureDevices ??= [];
        show.Variables ??= [];
        show.CueSets ??= [];
        foreach (var asset in show.Assets)
        {
            asset.Revisions ??= [];
            asset.Children ??= [];
        }
        foreach (var display in show.Displays)
        {
            if (display.KeyChannel <= 0) display.KeyChannel = 1;
        }
        foreach (var node in show.Nodes)
            node.MacAddress ??= "";
        foreach (var tl in show.Timelines)
        {
            tl.Layers ??= [];
            tl.Cues ??= [];
            if (tl.Rate <= 0) tl.Rate = 1;
            foreach (var cue in tl.Cues)
            {
                cue.Position ??= new Vec3();
                cue.Scale ??= new Vec2 { X = 100, Y = 100 };
                cue.Rotation ??= new Vec3();
                cue.Crop ??= new Crop();
                cue.Anchor ??= new Vec2 { X = 0.5, Y = 0.5 };
                cue.Tweens ??= [];
                if (cue.Speed <= 0) cue.Speed = 100;
                if (string.IsNullOrEmpty(cue.ChromaKeyColor)) cue.ChromaKeyColor = "#00FF00";
                if (cue.ChromaKeyTolerance <= 0) cue.ChromaKeyTolerance = 28;
            }
        }
    }
}

sealed class OutputTypeConverter : JsonConverter<OutputType>
{
    public override OutputType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString() ?? "GPU";
        return raw.ToUpperInvariant() switch
        {
            "SDI" => OutputType.SDI,
            "NDI" => OutputType.NDI,
            "VIRTUAL" => OutputType.Virtual,
            _ => OutputType.GPU,
        };
    }

    public override void Write(Utf8JsonWriter writer, OutputType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            OutputType.SDI => "SDI",
            OutputType.NDI => "NDI",
            OutputType.Virtual => "Virtual",
            _ => "GPU",
        });
}
