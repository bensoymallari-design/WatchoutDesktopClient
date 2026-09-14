namespace Watchout.Core.Media;

public sealed record NdiChoice(string Name, string? Detail, string? WebcamId);

public static class NdiCatalog
{
    public static IReadOnlyList<NdiChoice> Choices(
        IEnumerable<NdiAdvert> lan,
        IEnumerable<(string Id, string Name)> captureDevices)
    {
        var devices = captureDevices.ToList();
        var result = new List<NdiChoice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var advert in lan)
        {
            var name = NdiNames.FriendlyName(advert.Name);
            if (!NdiNames.IsAdvertisedSource(name) || !seen.Add(name)) continue;
            var cam = NdiNames.MatchWebcam(name, devices);
            result.Add(new NdiChoice(name, Detail(advert), cam?.Id));
        }
        foreach (var device in devices)
        {
            if (!NdiNames.LooksLikeNdi(device.Name) || !seen.Add(device.Name)) continue;
            result.Add(new NdiChoice(device.Name, "NDI Webcam Input", device.Id));
        }
        return result;
    }

    static string? Detail(NdiAdvert advert)
    {
        var host = string.IsNullOrWhiteSpace(advert.Host) ? null : NdiNames.FriendlyName(advert.Host);
        if (!string.IsNullOrEmpty(advert.Address) && host is not null)
            return $"{host} · {advert.Address}";
        return string.IsNullOrEmpty(advert.Address) ? host : advert.Address;
    }
}
