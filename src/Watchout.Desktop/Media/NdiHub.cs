using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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

    static readonly object FeedGate = new();
    static readonly Dictionary<string, RecvSession> Feeds = new(StringComparer.OrdinalIgnoreCase);

    public static WriteableBitmap? Retain(string sourceName, Action onFrame)
    {
        lock (FeedGate)
        {
            if (!Feeds.TryGetValue(sourceName, out var session))
            {
                session = new RecvSession(sourceName);
                Feeds[sourceName] = session;
                session.Start();
            }
            session.Add(onFrame);
            return session.Bitmap;
        }
    }

    public static WriteableBitmap? Peek(string sourceName)
    {
        lock (FeedGate) return Feeds.TryGetValue(sourceName, out var session) ? session.Bitmap : null;
    }

    public static void Release(string sourceName, Action onFrame)
    {
        RecvSession? dead = null;
        lock (FeedGate)
        {
            if (!Feeds.TryGetValue(sourceName, out var session)) return;
            session.Remove(onFrame);
            if (session.Listeners == 0)
            {
                Feeds.Remove(sourceName);
                dead = session;
            }
        }
        dead?.Dispose();
    }

    public static void Shutdown()
    {
        RecvSession[] all;
        lock (FeedGate)
        {
            all = Feeds.Values.ToArray();
            Feeds.Clear();
        }
        foreach (var s in all) s.Dispose();
    }

    sealed class RecvSession : IDisposable
    {
        const int VideoFrame = 1;
        const int Bgra = 'B' | ('G' << 8) | ('R' << 16) | ('A' << 24);
        const int Bgrx = 'B' | ('G' << 8) | ('R' << 16) | ('X' << 24);
        const int Rgba = 'R' | ('G' << 8) | ('B' << 16) | ('A' << 24);
        const int Rgbx = 'R' | ('G' << 8) | ('B' << 16) | ('X' << 24);
        const int Uyvy = 'U' | ('Y' << 8) | ('V' << 16) | ('Y' << 24);

        readonly string _name;
        readonly List<Action> _listeners = [];
        readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
        readonly CancellationTokenSource _stop = new();
        Thread? _thread;
        nint _recv;
        nint _namePtr;
        byte[]? _scratch;
        int _width;
        int _height;
        bool _logged;
        bool _fourccLogged;
        int _uiBusy;

        public WriteableBitmap? Bitmap { get; private set; }
        public int Listeners { get { lock (_listeners) return _listeners.Count; } }

        public RecvSession(string name) => _name = name;

        public void Add(Action onFrame)
        {
            lock (_listeners) _listeners.Add(onFrame);
        }

        public void Remove(Action onFrame)
        {
            lock (_listeners) _listeners.Remove(onFrame);
        }

        public void Start()
        {
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "WatchMe NDI " + _name,
            };
            _thread.Start();
        }

        void Loop()
        {
            var token = _stop.Token;
            _recv = NdiRuntime.CreateReceiver(_name, out _namePtr);
            if (_recv == 0)
            {
                if (!token.IsCancellationRequested)
                    _ui.BeginInvoke(() => App.Session.Log(NdiRuntime.LastError ?? $"NDI receive failed: {_name}", "error"));
                return;
            }
            if (token.IsCancellationRequested) return;
            _ui.BeginInvoke(() => App.Session.Log($"NDI receive: {_name}"));
            while (!token.IsCancellationRequested)
            {
                var frame = new NdiRuntime.VideoFrame();
                var kind = NdiRuntime.CaptureVideo(_recv, ref frame, 80);
                if (kind != VideoFrame)
                    continue;
                try
                {
                    Pump(frame);
                }
                finally
                {
                    NdiRuntime.FreeVideo(_recv, ref frame);
                }
            }
        }

        void Pump(NdiRuntime.VideoFrame frame)
        {
            if (Interlocked.CompareExchange(ref _uiBusy, 1, 0) != 0) return;
            var w = frame.xres;
            var h = frame.yres;
            if (frame.p_data == 0 || w <= 0 || h <= 0)
            {
                Interlocked.Exchange(ref _uiBusy, 0);
                return;
            }
            if (frame.FourCC is not (Bgra or Bgrx or Rgba or Rgbx or Uyvy))
            {
                if (!_fourccLogged)
                {
                    _fourccLogged = true;
                    _ui.BeginInvoke(() => App.Session.Log($"NDI {_name} sent FourCC {frame.FourCC:X8} — expecting BGRA", "warn"));
                }
                Interlocked.Exchange(ref _uiBusy, 0);
                return;
            }
            var row = w * 4;
            var stride = frame.line_stride_in_bytes > 0 ? frame.line_stride_in_bytes : (frame.FourCC == Uyvy ? w * 2 : row);
            var needed = row * h;
            if (_scratch is null || _scratch.Length != needed) _scratch = new byte[needed];
            if (frame.FourCC == Uyvy)
                UyvyToBgra(frame.p_data, stride, _scratch, w, h);
            else if (stride == row && frame.FourCC is Bgra or Bgrx)
                Marshal.Copy(frame.p_data, _scratch, 0, needed);
            else
            {
                for (var y = 0; y < h; y++)
                {
                    var src = nint.Add(frame.p_data, y * stride);
                    var dest = y * row;
                    if (frame.FourCC is Rgba or Rgbx)
                    {
                        for (var x = 0; x < w; x++)
                        {
                            var r = Marshal.ReadByte(src, x * 4);
                            var g = Marshal.ReadByte(src, x * 4 + 1);
                            var b = Marshal.ReadByte(src, x * 4 + 2);
                            var a = Marshal.ReadByte(src, x * 4 + 3);
                            var o = dest + x * 4;
                            _scratch[o] = b;
                            _scratch[o + 1] = g;
                            _scratch[o + 2] = r;
                            _scratch[o + 3] = a;
                        }
                    }
                    else
                        Marshal.Copy(src, _scratch, dest, row);
                }
            }
            var pixels = _scratch;
            _ui.BeginInvoke(() =>
            {
                try
                {
                    if (Bitmap is null || _width != w || _height != h)
                    {
                        Bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                        _width = w;
                        _height = h;
                        if (!_logged)
                        {
                            _logged = true;
                            App.Session.Log($"NDI {_name} {w}×{h} — live on Stage");
                        }
                    }
                    Bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, row, 0);
                    Action[] listeners;
                    lock (_listeners) listeners = _listeners.ToArray();
                    foreach (var fn in listeners) fn();
                }
                finally
                {
                    Interlocked.Exchange(ref _uiBusy, 0);
                }
            }, DispatcherPriority.Render);
        }

        static void UyvyToBgra(nint src, int stride, byte[] dst, int w, int h)
        {
            for (var y = 0; y < h; y++)
            {
                var row = nint.Add(src, y * stride);
                for (var x = 0; x + 1 < w; x += 2)
                {
                    var i = x * 2;
                    var u = Marshal.ReadByte(row, i) - 128;
                    var y0 = Marshal.ReadByte(row, i + 1) - 16;
                    var v = Marshal.ReadByte(row, i + 2) - 128;
                    var y1 = Marshal.ReadByte(row, i + 3) - 16;
                    WriteYuv(dst, (y * w + x) * 4, y0, u, v);
                    WriteYuv(dst, (y * w + x + 1) * 4, y1, u, v);
                }
            }
        }

        static void WriteYuv(byte[] dst, int o, int y, int u, int v)
        {
            var r = Math.Clamp((298 * y + 409 * v + 128) >> 8, 0, 255);
            var g = Math.Clamp((298 * y - 100 * u - 208 * v + 128) >> 8, 0, 255);
            var b = Math.Clamp((298 * y + 516 * u + 128) >> 8, 0, 255);
            dst[o] = (byte)b;
            dst[o + 1] = (byte)g;
            dst[o + 2] = (byte)r;
            dst[o + 3] = 255;
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _thread?.Join(2500); } catch { /* ignore */ }
            NdiRuntime.DestroyReceiver(_recv, _namePtr);
            _recv = 0;
            _namePtr = 0;
        }
    }
}
