using System.Net;
using System.Net.Sockets;
using Watchout.Core.Media;

namespace Watchout.Desktop.Media;

public static class NdiHub
{
    public static IReadOnlyList<NdiAdvert> Sources { get; private set; } = [];
    public static int Generation { get; private set; }
    public static string? LastError { get; private set; }
    public static event Action? Changed;

    public static async Task RefreshAsync()
    {
        IReadOnlyList<NdiAdvert> list = [];
        string? error = null;
        try
        {
            list = await ScanAsync(TimeSpan.FromMilliseconds(900));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        void Apply()
        {
            Sources = list;
            LastError = error;
            Generation++;
            if (error is not null) App.Session.Log($"NDI scan failed: {error}", "warn");
            Changed?.Invoke();
        }

        var dispatcher = App.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.Invoke(Apply);
        else Apply();
    }

    public static async Task<IReadOnlyList<NdiAdvert>> ScanAsync(TimeSpan timeout)
    {
        var found = new Dictionary<string, NdiAdvert>(StringComparer.OrdinalIgnoreCase);
        using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        try { sock.Bind(new IPEndPoint(IPAddress.Any, 5353)); }
        catch { sock.Bind(new IPEndPoint(IPAddress.Any, 0)); }
        using var udp = new UdpClient { Client = sock };
        try { udp.JoinMulticastGroup(IPAddress.Parse("224.0.0.251")); } catch { /* already a member */ }
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
