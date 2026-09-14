using Watchout.Core.Models;

namespace Watchout.Core.Media;

public static class AudioAssign
{
    public const string DefaultId = "default";

    public static AudioDevice DefaultSpeaker() => new()
    {
        Id = DefaultId,
        Name = "Windows default speaker",
        NodeId = "local-runner",
        Channels = 2,
        Driver = "WASAPI",
    };

    public static string StatusLine(AudioDevice device) =>
        $"{(string.IsNullOrEmpty(device.Driver) ? "WASAPI" : device.Driver)} · {Math.Max(1, device.Channels)} ch";

    public static List<AudioDevice> MenuDevices(
        IEnumerable<AudioDevice> endpoints,
        string? consoleId,
        string? consoleName,
        string? commsId,
        string? commsName)
    {
        var list = new List<AudioDevice> { DefaultSpeaker() };
        if (!string.IsNullOrWhiteSpace(consoleId) && !string.IsNullOrWhiteSpace(consoleName))
            list.Add(Copy($"role:console:{consoleId}", $"Default - {consoleName}"));
        if (!string.IsNullOrWhiteSpace(commsId) && !string.IsNullOrWhiteSpace(commsName))
            list.Add(Copy($"role:comms:{commsId}", $"Communications - {commsName}"));
        foreach (var ep in endpoints)
        {
            if (string.IsNullOrWhiteSpace(ep.Id) || list.Any(d => d.Id == ep.Id)) continue;
            list.Add(new AudioDevice
            {
                Id = ep.Id,
                Name = string.IsNullOrWhiteSpace(ep.Name) ? ep.Id : ep.Name,
                NodeId = "local-runner",
                Channels = ep.Channels > 0 ? ep.Channels : 2,
                Driver = string.IsNullOrEmpty(ep.Driver) ? "WASAPI" : ep.Driver,
            });
        }
        return list;
    }

    public static AudioDevice Resolve(IReadOnlyList<AudioDevice> menu, string? id) =>
        menu.FirstOrDefault(d => d.Id == id) ?? menu.FirstOrDefault() ?? DefaultSpeaker();

    static AudioDevice Copy(string id, string name) => new()
    {
        Id = id,
        Name = name,
        NodeId = "local-runner",
        Channels = 2,
        Driver = "WASAPI",
    };
}
