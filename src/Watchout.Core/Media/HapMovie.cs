namespace Watchout.Core.Media;

/// <summary>
/// QuickTime/MOV sample reader for a HAP track. Samples stay compressed on
/// disk; Play only reads the current DXT packet.
/// </summary>
public sealed class HapMovie : IDisposable
{
    readonly FileStream _file;
    readonly (long Offset, int Size, double TimeMs)[] _samples;

    public int Width { get; }
    public int Height { get; }
    public string FourCc { get; }
    public HapTextureKind Kind { get; }
    public double DurationMs { get; }
    public int SampleCount => _samples.Length;

    HapMovie(FileStream file, int width, int height, string fourCc, (long Offset, int Size, double TimeMs)[] samples)
    {
        _file = file;
        Width = width;
        Height = height;
        FourCc = fourCc;
        Kind = HapCodec.KindFromFourCc(fourCc);
        _samples = samples;
        DurationMs = samples.Length == 0 ? 0 : samples[^1].TimeMs + 33.333;
    }

    public static HapMovie Open(string path)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            var boxes = ReadRoot(file);
            var moovBox = boxes.FirstOrDefault(b => b.Type == "moov");
            if (moovBox.Type != "moov")
                throw new InvalidOperationException("HAP MOV has no moov");
            var trak = FindHapTrack(file, moovBox)
                ?? throw new InvalidOperationException("HAP MOV has no video track");
            var mdia = Child(file, trak, "mdia") ?? throw new InvalidOperationException("HAP track has no mdia");
            var mdhd = Child(file, mdia, "mdhd") ?? throw new InvalidOperationException("HAP track has no mdhd");
            var minf = Child(file, mdia, "minf") ?? throw new InvalidOperationException("HAP track has no minf");
            var stbl = Child(file, minf, "stbl") ?? throw new InvalidOperationException("HAP track has no stbl");
            var timescale = ReadTimescale(file, mdhd);
            var (fourCc, width, height) = ReadSampleDesc(file, Child(file, stbl, "stsd") ?? throw new InvalidOperationException("HAP track has no stsd"));
            var sizes = ReadStsz(file, Child(file, stbl, "stsz") ?? throw new InvalidOperationException("HAP track has no stsz"));
            var offsets = ReadChunkOffsets(file, stbl);
            var stsc = ReadStsc(file, Child(file, stbl, "stsc") ?? throw new InvalidOperationException("HAP track has no stsc"));
            var stts = ReadStts(file, Child(file, stbl, "stts") ?? throw new InvalidOperationException("HAP track has no stts"));
            var sampleOffsets = ExpandOffsetsWithSizes(sizes, offsets, stsc);
            if (sampleOffsets.Length != sizes.Length)
                throw new InvalidOperationException("HAP sample table is inconsistent");
            var times = ExpandTimes(stts, timescale, sizes.Length);
            var samples = new (long Offset, int Size, double TimeMs)[sizes.Length];
            for (var i = 0; i < sizes.Length; i++)
                samples[i] = (sampleOffsets[i], sizes[i], times[i]);
            return new HapMovie(file, width, height, fourCc, samples);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public int SampleIndexAt(double mediaMs)
    {
        if (_samples.Length == 0) return 0;
        var t = Math.Max(0, mediaMs);
        var lo = 0;
        var hi = _samples.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_samples[mid].TimeMs <= t) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    public byte[] ReadSample(int index)
    {
        if ((uint)index >= (uint)_samples.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        var sample = _samples[index];
        var buf = new byte[sample.Size];
        _file.Seek(sample.Offset, SeekOrigin.Begin);
        var n = 0;
        while (n < buf.Length)
        {
            var got = _file.Read(buf, n, buf.Length - n);
            if (got <= 0) break;
            n += got;
        }
        if (n != buf.Length)
            throw new InvalidOperationException("HAP sample is truncated");
        return buf;
    }

    public void Dispose() => _file.Dispose();

    readonly record struct Box(long Start, long Size, string Type, long DataStart, long DataEnd);

    static List<Box> ReadRoot(Stream s)
    {
        var list = new List<Box>();
        s.Position = 0;
        while (s.Position + 8 <= s.Length)
        {
            var start = s.Position;
            if (!TryReadBox(s, start, s.Length, out var box)) break;
            list.Add(box);
            s.Position = box.Start + box.Size;
        }
        return list;
    }

    static List<Box> Children(Stream s, Box parent)
    {
        var list = new List<Box>();
        var pos = parent.DataStart;
        while (pos + 8 <= parent.DataEnd)
        {
            s.Position = pos;
            if (!TryReadBox(s, pos, parent.DataEnd, out var box)) break;
            list.Add(box);
            pos = box.Start + box.Size;
        }
        return list;
    }

    static Box? Child(Stream s, Box parent, string type) =>
        Children(s, parent).FirstOrDefault(b => b.Type == type);

    static Box? FindHapTrack(Stream s, Box moov)
    {
        foreach (var trak in Children(s, moov).Where(b => b.Type == "trak"))
        {
            var mdia = Child(s, trak, "mdia");
            if (mdia is null) continue;
            var hdlr = Child(s, mdia.Value, "hdlr");
            if (hdlr is null) continue;
            s.Position = hdlr.Value.DataStart + 8;
            var handler = ReadFourCc(s);
            if (handler is not ("vide" or "Vide")) continue;
            return trak;
        }
        return null;
    }

    static (string FourCc, int Width, int Height) ReadSampleDesc(Stream s, Box stsd)
    {
        s.Position = stsd.DataStart + 8; // version/flags + count
        var entrySize = ReadU32(s);
        var fourCc = ReadFourCc(s);
        s.Position += 6 + 2 + 2 + 2 + 4 + 4 + 4; // reserved / data-ref / version / rev / vendor / qualities
        var width = ReadU16(s);
        var height = ReadU16(s);
        _ = entrySize;
        if (width < 2) width = 2;
        if (height < 2) height = 2;
        return (fourCc, width, height);
    }

    static int ReadTimescale(Stream s, Box mdhd)
    {
        s.Position = mdhd.DataStart;
        var version = s.ReadByte();
        s.Position += 3;
        if (version == 1)
        {
            s.Position += 16; // creation + modification
            return (int)ReadU32(s);
        }
        s.Position += 8;
        return (int)ReadU32(s);
    }

    static int[] ReadStsz(Stream s, Box stsz)
    {
        s.Position = stsz.DataStart + 4;
        var uniform = (int)ReadU32(s);
        var count = (int)ReadU32(s);
        count = Math.Max(0, count);
        var sizes = new int[count];
        if (uniform != 0)
        {
            Array.Fill(sizes, uniform);
            return sizes;
        }
        for (var i = 0; i < count; i++)
            sizes[i] = (int)ReadU32(s);
        return sizes;
    }

    static long[] ReadChunkOffsets(Stream s, Box stbl)
    {
        var co64 = Child(s, stbl, "co64");
        if (co64 is not null)
        {
            s.Position = co64.Value.DataStart + 4;
            var count = (int)ReadU32(s);
            var offs = new long[count];
            for (var i = 0; i < count; i++)
                offs[i] = ReadI64(s);
            return offs;
        }
        var stco = Child(s, stbl, "stco") ?? throw new InvalidOperationException("HAP track has no stco");
        s.Position = stco.DataStart + 4;
        var n = (int)ReadU32(s);
        var list = new long[n];
        for (var i = 0; i < n; i++)
            list[i] = ReadU32(s);
        return list;
    }

    static (int FirstChunk, int SamplesPerChunk)[] ReadStsc(Stream s, Box stsc)
    {
        s.Position = stsc.DataStart + 4;
        var count = (int)ReadU32(s);
        var rows = new (int FirstChunk, int SamplesPerChunk)[count];
        for (var i = 0; i < count; i++)
        {
            var first = (int)ReadU32(s);
            var spc = (int)ReadU32(s);
            _ = ReadU32(s);
            rows[i] = (first, spc);
        }
        return rows;
    }

    static (int Count, int Duration)[] ReadStts(Stream s, Box stts)
    {
        s.Position = stts.DataStart + 4;
        var count = (int)ReadU32(s);
        var rows = new (int Count, int Duration)[count];
        for (var i = 0; i < count; i++)
            rows[i] = ((int)ReadU32(s), (int)ReadU32(s));
        return rows;
    }

    static long[] ExpandOffsetsWithSizes(int[] sizes, long[] chunkOffsets, (int FirstChunk, int SamplesPerChunk)[] stsc)
    {
        var offsets = new long[sizes.Length];
        var sample = 0;
        for (var chunk = 0; chunk < chunkOffsets.Length && sample < sizes.Length; chunk++)
        {
            var spc = SamplesPerChunk(stsc, chunk + 1);
            var cursor = chunkOffsets[chunk];
            for (var i = 0; i < spc && sample < sizes.Length; i++, sample++)
            {
                offsets[sample] = cursor;
                cursor += sizes[sample];
            }
        }
        return offsets;
    }

    static int SamplesPerChunk((int FirstChunk, int SamplesPerChunk)[] stsc, int chunkOneBased)
    {
        var spc = 1;
        foreach (var row in stsc)
        {
            if (row.FirstChunk <= chunkOneBased) spc = Math.Max(1, row.SamplesPerChunk);
            else break;
        }
        return spc;
    }

    static double[] ExpandTimes((int Count, int Duration)[] stts, int timescale, int sampleCount)
    {
        var times = new double[sampleCount];
        var scale = timescale <= 0 ? 1.0 : timescale;
        var dts = 0.0;
        var i = 0;
        foreach (var row in stts)
        {
            for (var n = 0; n < row.Count && i < sampleCount; n++, i++)
            {
                times[i] = dts * 1000.0 / scale;
                dts += Math.Max(1, row.Duration);
            }
        }
        for (; i < sampleCount; i++)
        {
            times[i] = dts * 1000.0 / scale;
            dts += 1;
        }
        return times;
    }

    static bool TryReadBox(Stream s, long start, long limit, out Box box)
    {
        box = default;
        if (start + 8 > limit) return false;
        s.Position = start;
        var size32 = ReadU32(s);
        var type = ReadFourCc(s);
        long size = size32;
        var dataStart = start + 8;
        if (size32 == 1)
        {
            if (start + 16 > limit) return false;
            size = ReadI64(s);
            dataStart = start + 16;
        }
        else if (size32 == 0)
            size = limit - start;
        if (size < 8) return false;
        box = new Box(start, size, type, dataStart, start + size);
        return true;
    }

    static uint ReadU32(Stream s)
    {
        Span<byte> b = stackalloc byte[4];
        if (s.Read(b) < 4) return 0;
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    static ushort ReadU16(Stream s)
    {
        Span<byte> b = stackalloc byte[2];
        if (s.Read(b) < 2) return 0;
        return (ushort)((b[0] << 8) | b[1]);
    }

    static long ReadI64(Stream s)
    {
        var hi = ReadU32(s);
        var lo = ReadU32(s);
        return (long)(((ulong)hi << 32) | lo);
    }

    static string ReadFourCc(Stream s)
    {
        Span<byte> b = stackalloc byte[4];
        if (s.Read(b) < 4) return "    ";
        return System.Text.Encoding.ASCII.GetString(b);
    }
}
