using Watchout.Core.Models;

namespace Watchout.Core.Media;

/// <summary>
/// Stage-column radio matrix: each live input is a row, each Stage display
/// is a column. Clicking a column assigns that source and auto-fits the cue
/// to that canvas.
/// </summary>
public static class StageRoute
{
    public const string Capture = "capture";
    public const string Ndi = "ndi";

    public sealed record Incoming(string Kind, string Key, string Label);

    public sealed record Row(string Kind, string Key, string Label, string? DisplayId);

    public static IReadOnlyList<Display> Columns(Show show) =>
        show.Displays.Where(d => d.Enabled).OrderBy(d => d.X).ThenBy(d => d.Channel).ThenBy(d => d.Name).ToList();

    public static string ColumnLabel(Display display, int index) =>
        $"Stage {index + 1}";

    public static string CaptureRowLabel(int index) => $"Card {index}";

    public static string NdiRowLabel(int index) => $"NDI {index}";

    public static string ColumnHint(Display display) =>
        $"{display.Width:0}×{display.Height:0}";

    public static string? AssignedDisplayId(Show show, string kind, string key)
    {
        var id = kind == Ndi
            ? LiveSources.NdiDisplayKey(show, key)
            : LiveSources.CaptureDisplayKey(show, key);
        return LiveSources.IsAutoDisplay(id) ? null : id;
    }

    public static IReadOnlyList<Row> Rows(Show? show, IEnumerable<Incoming> incoming)
    {
        var list = new List<Row>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string kind, string key, string label)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!seen.Add($"{kind}:{key}")) return;
            var assigned = show is null ? null : AssignedDisplayId(show, kind, key);
            list.Add(new Row(kind, key, string.IsNullOrWhiteSpace(label) ? key : label, assigned));
        }

        foreach (var src in incoming)
            Add(src.Kind, src.Key, src.Label);

        if (show is null) return list;

        foreach (var rec in show.CaptureDevices)
        {
            if (LiveSources.IsNdiRecord(rec))
                Add(Ndi, rec.Name, rec.Name);
            else if (!LiveSources.IsPlaceholderCapture(rec))
                Add(Capture, rec.Signal, rec.Name);
        }

        foreach (var asset in show.Assets)
        {
            if (LiveSources.IsNdi(asset))
            {
                var key = LiveSources.NdiSourceName(asset) ?? asset.Name;
                Add(Ndi, key, asset.Name);
            }
            else if (LiveSources.IsCapture(asset) && LiveSources.CaptureDeviceId(asset) is { } id)
                Add(Capture, id, asset.Name);
        }

        return list;
    }
}
