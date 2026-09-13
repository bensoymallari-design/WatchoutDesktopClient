using Watchout.Core.Models;

namespace Watchout.Core.Media;

public static class LiveSources
{
    public const string CapturePrefix = "capture:";
    public const double LiveCueDurationMs = 24 * 60 * 60 * 1000;

    public static bool IsCapture(Asset? asset) =>
        asset is not null && (asset.Kind == AssetKind.Capture || IsCaptureUrl(asset.Url));

    public static bool IsCaptureUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith(CapturePrefix, StringComparison.OrdinalIgnoreCase);

    public static string CaptureUrl(string deviceId) => CapturePrefix + deviceId;

    public static string? CaptureDeviceId(Asset? asset) => CaptureDeviceId(asset?.Url);

    public static string? CaptureDeviceId(string? url)
    {
        if (!IsCaptureUrl(url)) return null;
        return url![CapturePrefix.Length..];
    }

    public static ImportedMedia CaptureAsset(string deviceId, string name, int width = 1920, int height = 1080)
    {
        return new ImportedMedia
        {
            Id = Ids.New("asset"),
            Name = string.IsNullOrWhiteSpace(name) ? "Capture card" : name,
            Kind = AssetKind.Capture,
            Width = width > 0 ? width : 1920,
            Height = height > 0 ? height : 1080,
            Duration = LiveCueDurationMs,
            Fps = 60,
            Url = CaptureUrl(deviceId),
            Codec = "Capture card · live",
            Color = "#4ade80",
            Optimized = true,
            Notes = $"{name} · live HDMI/SDI capture · Resolume or any program output · Media Foundation",
            OriginalPath = CaptureUrl(deviceId),
        };
    }
}
