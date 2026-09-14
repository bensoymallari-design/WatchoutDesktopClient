using System.Net;
using System.Net.Sockets;
using Watchout.Core.Media;

namespace Watchout.Desktop.Media;

public static class NdiHub
{
    public static IReadOnlyList<NdiAdvert> Sources { get; private set; } = [];
    public static int Generation { get; private set; }
    public static string? LastError { get; private set; }
    public static string? Engine { get; private set; }
    public static event Action? Changed;

    public static async Task RefreshAsync()
    {
        IReadOnlyList<NdiAdvert> list = [];
        string? error = null;
        string? engine = null;
        try
        {
            list = await ScanAsync();
            engine = Describe(list);
            if (list.Count == 0)
                error = EmptyHint();
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        void Apply()
        {
            Sources = list;
            LastError = error;
            Engine = engine;
            Generation++;
            Changed?.Invoke();
        }

        var dispatcher = App.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.Invoke(Apply);
        else Apply();
    }

    public static async Task<IReadOnlyList<NdiAdvert>> ScanAsync()
    {
        var found = new Dictionary<string, NdiAdvert>(StringComparer.OrdinalIgnoreCase);
        var runtimeTask = Task.Run(() => NdiRuntime.FindSources(TimeSpan.FromMilliseconds(2_200)));
        var mdnsTask = ScanMdnsAsync(TimeSpan.FromMilliseconds(1_400));
        await Task.WhenAll(runtimeTask, mdnsTask);
        foreach (var advert in runtimeTask.Result)
            found[advert.Name] = advert;
        foreach (var advert in mdnsTask.Result)
            if (found.TryAdd(advert.Name, advert) == false && string.IsNullOrEmpty(found[advert.Name].Address))
                found[advert.Name] = advert;
        return found.Values.OrderBy(a => a.Name).ToList();
    }

    static string Describe(IReadOnlyList<NdiAdvert> list)
    {
        var runtime = NdiRuntime.LibraryPath is not null;
        if (list.Count == 0) return runtime ? "runtime" : "mdns";
        return runtime ? "NDI Runtime" : "mDNS";
    }

    static string EmptyHint()
    {
        if (NdiRuntime.LastError is { Length: > 0 } load)
            return load + " WatchMe also listened for mDNS (_ndi._tcp) and heard nothing — NDI Runtime often already owns port 5353, which is why this picker must use the DLL.";
        var ver = string.IsNullOrEmpty(NdiRuntime.Version) ? "NDI Runtime" : NdiRuntime.Version;
        return $"{ver} loaded but no senders answered yet. Start Resolume NDI, OBS DistroAV, or NDI Camera Pro on this LAN (or this PC), then Scan again.";
    }

    public static async Task<IReadOnlyList<NdiAdvert>> ScanMdnsAsync(TimeSpan timeout)
    {
        var found = new Dictionary<string, NdiAdvert>(StringComparer.OrdinalIgnoreCase);
        using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        try { sock.ExclusiveAddressUse = false; } catch { /* platform */ }
        try { sock.Bind(new IPEndPoint(IPAddress.Any, 5353)); }
        catch { sock.Bind(new IPEndPoint(IPAddress.Any, 0)); }
        using var udp = new UdpClient { Client = sock };
        try { udp.JoinMulticastGroup(IPAddress.Parse("224.0.0.251")); } catch { /* already a member */ }
        try { sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true); } catch { /* ignore */ }
        udp.Client.ReceiveTimeout = 200;
        var query = NdiNames.QueryPacket();
        var mcast = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);
        await udp.SendAsync(query, query.Length, mcast);
        await Task.Delay(40);
        await udp.SendAsync(query, query.Length, mcast);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var remain = deadline - DateTime.UtcNow;
            if (remain <= TimeSpan.Zero) break;
            var receive = udp.ReceiveAsync();
            var winner = await Task.WhenAny(receive, Task.Delay(remain));
            if (winner != receive) break;
            UdpReceiveResult datagram;
            try { datagram = await receive; }
            catch { continue; }
            foreach (var advert in NdiNames.Parse(datagram.Buffer))
                found[advert.Name] = advert;
        }
        return found.Values.OrderBy(a => a.Name).ToList();
    }
}
