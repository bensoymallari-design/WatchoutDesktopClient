using System.Net;
using System.Net.Sockets;

namespace Watchout.Core.Network;

public static class WakeOnLan
{
    public static bool TryParseMac(string? raw, out byte[] mac)
    {
        mac = [];
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var hex = new string(raw.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12) return false;
        mac = Convert.FromHexString(hex);
        return true;
    }

    public static byte[] MagicPacket(byte[] mac)
    {
        var packet = new byte[6 + 16 * 6];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (var i = 0; i < 16; i++)
            Buffer.BlockCopy(mac, 0, packet, 6 + i * 6, 6);
        return packet;
    }

    public static bool Send(string? mac, string? broadcast = null, int port = 9)
    {
        if (!TryParseMac(mac, out var bytes)) return false;
        try
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            var packet = MagicPacket(bytes);
            var ip = string.IsNullOrWhiteSpace(broadcast) ? IPAddress.Broadcast : IPAddress.Parse(broadcast);
            udp.Send(packet, packet.Length, new IPEndPoint(ip, port));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
