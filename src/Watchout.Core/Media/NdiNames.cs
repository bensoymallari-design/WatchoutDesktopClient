using System.Net;
using System.Text;
using Watchout.Core.Models;

namespace Watchout.Core.Media;

public sealed record NdiAdvert(string Name, string? Host = null, string? Address = null, int Port = 0);

public static class NdiNames
{
    public const string Service = "_ndi._tcp.local";
    public const string UrlPrefix = "ndi:";
    public const string ToolsUrl = "https://www.ndi.tv/tools/";

    public static bool IsNdiUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith(UrlPrefix, StringComparison.OrdinalIgnoreCase);

    public static string NdiUrl(string sourceName) => UrlPrefix + sourceName;

    public static string? SourceNameFromUrl(string? url) =>
        IsNdiUrl(url) ? url![UrlPrefix.Length..] : null;

    public static bool IsAdvertisedSource(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = FriendlyName(name);
        if (n.Length < 2) return false;
        if (n[0] == '_') return false;
        if (n.Contains("_ndi._tcp", StringComparison.OrdinalIgnoreCase)) return false;
        if (n.Equals("local", StringComparison.OrdinalIgnoreCase)) return false;
        if (n.StartsWith("KeepAliveServer", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    public static bool LooksLikeNdi(string? label) =>
        !string.IsNullOrEmpty(label) && label.Contains("ndi", StringComparison.OrdinalIgnoreCase);

    public static bool IsNdiWebcamLabel(string? label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        var n = label.ToLowerInvariant();
        if (!n.Contains("ndi")) return false;
        return n.Contains("webcam") || n.Contains("newtek") || n.Contains("video") || n.Contains("hx")
               || n.Contains("virtual");
    }

    public static string FriendlyName(string advertised)
    {
        var name = advertised.Trim().Trim('"');
        foreach (var suffix in new[] { "._ndi._tcp.local.", "._ndi._tcp.local", "._ndi._tcp", ".local." })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                name = name[..^suffix.Length];
        }
        return UnescapeDns(name).Trim().TrimEnd('.');
    }

    public static string UnescapeDns(string name)
    {
        var sb = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '\\' && i + 3 < name.Length && char.IsDigit(name[i + 1]) && char.IsDigit(name[i + 2]) && char.IsDigit(name[i + 3]))
            {
                sb.Append((char)int.Parse(name.AsSpan(i + 1, 3)));
                i += 3;
            }
            else if (name[i] == '\\' && i + 1 < name.Length)
            {
                sb.Append(name[i + 1]);
                i++;
            }
            else sb.Append(name[i]);
        }
        return sb.ToString();
    }

    public static (string Id, string Name)? PreferredWebcam(IEnumerable<(string Id, string Name)> devices)
    {
        var ndi = devices.Where(d => IsNdiWebcamLabel(d.Name)).ToList();
        return ndi.Count == 0 ? null : ndi[0];
    }

    public static (string Id, string Name)? MatchWebcam(string sourceName, IEnumerable<(string Id, string Name)> devices)
    {
        var list = devices.Where(d => IsNdiWebcamLabel(d.Name) || LooksLikeNdi(d.Name)).ToList();
        var friendly = FriendlyName(sourceName);
        foreach (var d in list)
        {
            if (d.Name.Contains(friendly, StringComparison.OrdinalIgnoreCase)
                || friendly.Contains(d.Name, StringComparison.OrdinalIgnoreCase))
                return d;
        }
        return PreferredWebcam(devices);
    }

    public static NdiAdvert FromRuntime(string ndiName, string? url)
    {
        var name = FriendlyName(ndiName);
        string? host = null;
        string? address = null;
        var port = 0;
        if (!string.IsNullOrWhiteSpace(url))
        {
            var trimmed = url.Trim();
            var colon = trimmed.LastIndexOf(':');
            var left = colon > 0 ? trimmed[..colon] : trimmed;
            if (colon > 0 && int.TryParse(trimmed[(colon + 1)..], out var parsed))
                port = parsed;
            if (IPAddress.TryParse(left.Trim('[', ']'), out _))
                address = left.Trim('[', ']');
            else
                host = left;
        }

        var open = name.IndexOf(" (", StringComparison.Ordinal);
        if (host is null && open > 0 && name.EndsWith(')'))
            host = name[..open];

        return new NdiAdvert(name, host, address, port);
    }

    public static byte[] QueryPacket()
    {
        var buf = new List<byte>(64);
        void U16(int v) { buf.Add((byte)(v >> 8)); buf.Add((byte)(v & 0xff)); }
        U16(0);
        U16(0);
        U16(1);
        U16(0);
        U16(0);
        U16(0);
        WriteName(buf, Service);
        U16(12);
        U16(1);
        return buf.ToArray();
    }

    public static IReadOnlyList<NdiAdvert> Parse(byte[] packet)
    {
        if (packet.Length < 12) return [];
        var qd = (packet[4] << 8) | packet[5];
        var an = (packet[6] << 8) | packet[7];
        var ns = (packet[8] << 8) | packet[9];
        var ar = (packet[10] << 8) | packet[11];
        var i = 12;
        for (var q = 0; q < qd && i < packet.Length; q++)
        {
            SkipName(packet, ref i);
            i += 4;
        }
        var names = new Dictionary<string, NdiAdvert>(StringComparer.OrdinalIgnoreCase);
        var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var total = an + ns + ar;
        for (var n = 0; n < total && i + 4 <= packet.Length; n++)
        {
            var owner = ReadName(packet, ref i);
            if (i + 10 > packet.Length) break;
            var type = (packet[i] << 8) | packet[i + 1];
            i += 4;
            var rdlen = (packet[i + 4] << 8) | packet[i + 5];
            i += 6;
            if (i + rdlen > packet.Length) break;
            var rdataAt = i;
            if (type == 12)
            {
                var ptr = rdataAt;
                var instance = ReadName(packet, ref ptr);
                var name = FriendlyName(instance);
                if (!string.IsNullOrEmpty(name) && IsAdvertisedSource(name) && IsNdiInstance(instance, owner) && !names.ContainsKey(name))
                    names[name] = new NdiAdvert(name);
            }
            else if (type == 33 && rdlen >= 6)
            {
                var port = (packet[rdataAt + 4] << 8) | packet[rdataAt + 5];
                var ptr = rdataAt + 6;
                var target = ReadName(packet, ref ptr);
                var name = FriendlyName(owner);
                names.TryGetValue(name, out var prev);
                prev ??= new NdiAdvert(name);
                names[name] = prev with { Host = string.IsNullOrWhiteSpace(target) ? prev.Host : target.TrimEnd('.'), Port = port };
            }
            else if (type == 1 && rdlen == 4)
            {
                var ip = new IPAddress(packet.AsSpan(rdataAt, 4).ToArray()).ToString();
                var host = owner.TrimEnd('.');
                hosts[host] = ip;
                hosts[FriendlyName(host)] = ip;
            }
            i += rdlen;
        }

        foreach (var kv in names.ToList())
        {
            if (kv.Value.Address is null && kv.Value.Host is { } h)
            {
                var shortHost = FriendlyName(h);
                if (hosts.TryGetValue(shortHost, out var ip) || hosts.TryGetValue(h, out ip))
                    names[kv.Key] = kv.Value with { Address = ip };
            }
        }
        return names.Values.Where(a => !string.IsNullOrWhiteSpace(a.Name)).OrderBy(a => a.Name).ToList();
    }

    static bool IsNdiInstance(string instance, string owner)
    {
        var blob = (instance + " " + owner).ToLowerInvariant();
        return blob.Contains("_ndi._tcp") && IsAdvertisedSource(FriendlyName(instance));
    }

    static void WriteName(List<byte> buf, string name)
    {
        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            buf.Add((byte)bytes.Length);
            buf.AddRange(bytes);
        }
        buf.Add(0);
    }

    static void SkipName(byte[] packet, ref int i)
    {
        ReadName(packet, ref i);
    }

    static string ReadName(byte[] packet, ref int i)
    {
        var labels = new List<string>();
        var jumped = false;
        var end = i;
        var hops = 0;
        while (i < packet.Length && hops++ < 32)
        {
            var len = packet[i];
            if (len == 0)
            {
                if (!jumped) end = i + 1;
                i = jumped ? end : i + 1;
                break;
            }
            if ((len & 0xC0) == 0xC0)
            {
                if (i + 1 >= packet.Length) break;
                var ptr = ((len & 0x3F) << 8) | packet[i + 1];
                if (!jumped) end = i + 2;
                i = ptr;
                jumped = true;
                continue;
            }
            i++;
            if (i + len > packet.Length) break;
            labels.Add(Encoding.ASCII.GetString(packet, i, len));
            i += len;
            if (!jumped) end = i;
        }
        if (!jumped) { /* i already at next */ }
        else i = end;
        return string.Join('.', labels);
    }
}

public static class NdiLive
{
    public static bool IsNdi(Asset? asset) =>
        asset is not null && (asset.Kind == AssetKind.Ndi || NdiNames.IsNdiUrl(asset.Url));
}
