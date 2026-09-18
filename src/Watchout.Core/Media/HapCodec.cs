using Snappier;
using Watchout.Core.Models;

namespace Watchout.Core.Media;

public enum HapTextureKind
{
    Dxt1,
    Dxt5,
    YCoCgDxt5,
    Rgtc1
}

public readonly record struct HapFrame(HapTextureKind Kind, byte[] Blocks, int Width, int Height)
{
    public bool YCoCg => Kind == HapTextureKind.YCoCgDxt5;

    public int BlockBytes => Kind is HapTextureKind.Dxt1 or HapTextureKind.Rgtc1 ? 8 : 16;
}

/// <summary>
/// Vidvox HAP (Resolume Alley DXV-style GPU texture). Decode Snappy to DXT
/// blocks that stay on the GPU — not RGB32 system RAM, not DXVA.
/// </summary>
public static class HapCodec
{
    public const byte FmtDxt1 = 0x0B;
    public const byte FmtDxt5 = 0x0E;
    public const byte FmtYCoCg = 0x0F;
    public const byte FmtRgtc1 = 0x01;
    public const byte CompNone = 0xA0;
    public const byte CompSnappy = 0xB0;
    public const byte CompComplex = 0xC0;
    public const byte StDecodeInstructions = 0x01;
    public const byte StCompressorTable = 0x02;
    public const byte StSizeTable = 0x03;
    public const byte StOffsetTable = 0x04;

    static readonly System.Text.RegularExpressions.Regex HapName = new(
        @"\bhapq?\b|hap_q|hap1|hap5|hapy|hapa|hapm",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool IsHap(string? codec, string? path = null)
    {
        var blob = $"{codec} {path}";
        if (path is { Length: > 0 })
        {
            if (Codecs.ExtOf(path) == "hap") return true;
            if (path.Contains(".hap.", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return HapName.IsMatch(blob);
    }

    public static bool NeedsHapEncode(string codec, string path)
    {
        if (Codecs.MediaKind(path) != AssetKind.Video) return false;
        if (IsHap(codec, path)) return false;
        if (path.StartsWith("procedural:", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    public static HapTextureKind KindFromFourCc(string fourCc)
    {
        var t = fourCc.Replace(" ", "", StringComparison.Ordinal);
        if (t.Equals("Hap1", StringComparison.OrdinalIgnoreCase) || t.Equals("hap1", StringComparison.OrdinalIgnoreCase))
            return HapTextureKind.Dxt1;
        if (t.Equals("Hap5", StringComparison.OrdinalIgnoreCase) || t.Equals("hapa", StringComparison.OrdinalIgnoreCase))
            return HapTextureKind.Dxt5;
        if (t.Equals("HapA", StringComparison.OrdinalIgnoreCase))
            return HapTextureKind.Rgtc1;
        return HapTextureKind.YCoCgDxt5;
    }

    public static HapTextureKind KindFromFormat(byte format) => format switch
    {
        FmtDxt1 => HapTextureKind.Dxt1,
        FmtDxt5 => HapTextureKind.Dxt5,
        FmtRgtc1 => HapTextureKind.Rgtc1,
        _ => HapTextureKind.YCoCgDxt5,
    };

    public static int DxtSize(int width, int height, HapTextureKind kind)
    {
        var bw = Math.Max(1, (Math.Max(1, width) + 3) / 4);
        var bh = Math.Max(1, (Math.Max(1, height) + 3) / 4);
        var block = kind is HapTextureKind.Dxt1 or HapTextureKind.Rgtc1 ? 8 : 16;
        return bw * bh * block;
    }

    public static HapFrame Decode(ReadOnlySpan<byte> packet, int width, int height)
    {
        if (packet.Length < 4 || width < 2 || height < 2)
            throw new InvalidOperationException("HAP packet is empty");
        var off = 0;
        if (!TryReadSection(packet, ref off, out var sectionSize, out var sectionType))
            throw new InvalidOperationException("HAP header is truncated");
        var format = (byte)(sectionType & 0x0F);
        var compressor = (byte)(sectionType & 0xF0);
        var payload = packet.Slice(off, Math.Min(sectionSize, packet.Length - off));
        var kind = KindFromFormat(format);
        byte[] dxt;
        if (compressor == CompNone)
            dxt = payload.ToArray();
        else if (compressor == CompSnappy)
            dxt = Snappy.DecompressToArray(payload);
        else if (compressor == CompComplex)
            dxt = DecodeComplex(payload);
        else
            throw new InvalidOperationException($"HAP compressor 0x{compressor:X2} is not supported");
        return new HapFrame(kind, dxt, width, height);
    }

    static byte[] DecodeComplex(ReadOnlySpan<byte> payload)
    {
        var off = 0;
        if (!TryReadSection(payload, ref off, out var instrSize, out var instrType)
            || instrType != StDecodeInstructions)
            throw new InvalidOperationException("HAP complex frame has no decode instructions");
        var instrEnd = off + instrSize;
        var compressors = new List<byte>();
        var sizes = new List<int>();
        var offsets = new List<int>();
        while (off < instrEnd)
        {
            if (!TryReadSection(payload, ref off, out var innerSize, out var innerType)) break;
            var body = payload.Slice(off, Math.Min(innerSize, payload.Length - off));
            off += innerSize;
            switch (innerType)
            {
                case StCompressorTable:
                    for (var i = 0; i < body.Length; i++)
                        compressors.Add((byte)(body[i] << 4));
                    break;
                case StSizeTable:
                    for (var i = 0; i + 4 <= body.Length; i += 4)
                        sizes.Add(BitConverter.ToInt32(body.Slice(i, 4)));
                    break;
                case StOffsetTable:
                    for (var i = 0; i + 4 <= body.Length; i += 4)
                        offsets.Add(BitConverter.ToInt32(body.Slice(i, 4)));
                    break;
            }
        }
        if (compressors.Count == 0 || sizes.Count == 0 || compressors.Count != sizes.Count)
            throw new InvalidOperationException("HAP complex frame is missing chunk tables");
        if (offsets.Count != sizes.Count)
        {
            offsets.Clear();
            var run = 0;
            foreach (var size in sizes)
            {
                offsets.Add(run);
                run += size;
            }
        }
        var data = payload[off..];
        using var buf = new MemoryStream();
        for (var i = 0; i < sizes.Count; i++)
        {
            var start = offsets[i];
            var size = sizes[i];
            if (start < 0 || size < 0 || start + size > data.Length)
                throw new InvalidOperationException("HAP chunk is outside the packet");
            var chunk = data.Slice(start, size);
            if (compressors[i] == CompSnappy)
                buf.Write(Snappy.DecompressToArray(chunk));
            else
                buf.Write(chunk);
        }
        return buf.ToArray();
    }

    public static bool TryReadSection(ReadOnlySpan<byte> data, ref int offset, out int size, out byte type)
    {
        size = 0;
        type = 0;
        if (offset + 4 > data.Length) return false;
        size = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
        type = data[offset + 3];
        offset += 4;
        if (size == 0)
        {
            if (offset + 4 > data.Length) return false;
            size = BitConverter.ToInt32(data.Slice(offset, 4));
            offset += 4;
        }
        if (size < 0 || offset + size > data.Length) return false;
        return true;
    }

    public static byte[] WrapUncompressed(HapTextureKind kind, ReadOnlySpan<byte> dxt)
    {
        var format = kind switch
        {
            HapTextureKind.Dxt1 => FmtDxt1,
            HapTextureKind.Dxt5 => FmtDxt5,
            HapTextureKind.Rgtc1 => FmtRgtc1,
            _ => FmtYCoCg,
        };
        var packet = new byte[4 + dxt.Length];
        var size = dxt.Length;
        packet[0] = (byte)(size & 0xFF);
        packet[1] = (byte)((size >> 8) & 0xFF);
        packet[2] = (byte)((size >> 16) & 0xFF);
        packet[3] = (byte)(CompNone | format);
        dxt.CopyTo(packet.AsSpan(4));
        return packet;
    }
}
