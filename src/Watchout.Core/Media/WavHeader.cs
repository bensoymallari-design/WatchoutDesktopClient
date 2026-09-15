using System.Buffers.Binary;
using System.Text;

namespace Watchout.Core.Media;

public sealed class WavInfo
{
    public int Channels { get; init; }
    public int SampleRate { get; init; }
    public int BitsPerSample { get; init; }
    public int BlockAlign { get; init; }
}

public static class WavHeader
{
    public const int MaxChannels = 65535;

    public static bool TryRead(ReadOnlySpan<byte> data, out WavInfo info)
    {
        info = new WavInfo();
        if (data.Length < 44) return false;
        if (data[0] != (byte)'R' || data[1] != (byte)'I' || data[2] != (byte)'F' || data[3] != (byte)'F') return false;
        if (data[8] != (byte)'W' || data[9] != (byte)'A' || data[10] != (byte)'V' || data[11] != (byte)'E') return false;
        var pos = 12;
        while (pos + 8 <= data.Length)
        {
            var id = Encoding.ASCII.GetString(data.Slice(pos, 4));
            var size = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos + 4, 4));
            if (size < 0) return false;
            var body = pos + 8;
            if (id.StartsWith("fmt", StringComparison.OrdinalIgnoreCase) && body + 16 <= data.Length)
            {
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(body + 2, 2));
                var rate = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(body + 4, 4));
                var align = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(body + 12, 2));
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(body + 14, 2));
                info = new WavInfo
                {
                    Channels = channels,
                    SampleRate = rate,
                    BitsPerSample = bits,
                    BlockAlign = align,
                };
                return channels > 0 && rate > 0;
            }
            pos = body + size + (size & 1);
        }
        return false;
    }

    public static bool TryReadFile(string path, out WavInfo info)
    {
        info = new WavInfo();
        try
        {
            using var fs = File.OpenRead(path);
            var buf = new byte[Math.Min(4096, fs.Length)];
            var n = fs.Read(buf, 0, buf.Length);
            return TryRead(buf.AsSpan(0, n), out info);
        }
        catch
        {
            return false;
        }
    }

    public static bool NeedsStereoDownmix(int channels) => channels > 8;

    public static byte[] WritePcm16Mono(short[] samples, int sampleRate)
    {
        var dataBytes = samples.Length * 2;
        var bytes = new byte[44 + dataBytes];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 36 + dataBytes);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(bytes, 12);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), dataBytes);
        Buffer.BlockCopy(samples, 0, bytes, 44, dataBytes);
        return bytes;
    }
}
