namespace Watchout.Core.Media;

public readonly record struct LtcStamp(int Hours, int Minutes, int Seconds, int Frames, double Fps)
{
    public double Milliseconds =>
        ((((Hours * 60) + Minutes) * 60 + Seconds) * 1000d) + Frames * (1000d / Math.Max(1, Fps));

    public static LtcStamp FromMilliseconds(double ms, double fps)
    {
        fps = fps <= 0 ? 30 : fps;
        var totalFrames = Math.Max(0, (int)Math.Round(ms / (1000d / fps)));
        var framesPerSec = Math.Max(1, (int)Math.Round(fps));
        var frames = totalFrames % framesPerSec;
        var totalSec = totalFrames / framesPerSec;
        var seconds = totalSec % 60;
        var totalMin = totalSec / 60;
        var minutes = totalMin % 60;
        var hours = Math.Min(23, totalMin / 60);
        return new LtcStamp(hours, minutes, seconds, frames, fps);
    }
}

public static class Ltc
{
    const ushort Sync = 0x3FFD; // 0011 1111 1111 1101 in the last 16 bits, stored LSB-first per SMPTE

    public static byte[] Pack(LtcStamp stamp)
    {
        var bits = new byte[80];
        WriteUnits(bits, 0, stamp.Frames);
        WriteTens(bits, 8, stamp.Frames, 3);
        WriteUnits(bits, 16, stamp.Seconds);
        WriteTens(bits, 24, stamp.Seconds, 3);
        WriteUnits(bits, 32, stamp.Minutes);
        WriteTens(bits, 40, stamp.Minutes, 3);
        WriteUnits(bits, 48, stamp.Hours);
        WriteTens(bits, 56, stamp.Hours, 2);
        for (var i = 0; i < 16; i++)
            bits[64 + i] = (byte)((Sync >> i) & 1);
        return bits;
    }

    public static LtcStamp Unpack(byte[] bits, double fps = 30)
    {
        if (bits.Length < 80) return new LtcStamp(0, 0, 0, 0, fps);
        var frames = ReadUnits(bits, 0) + 10 * ReadTens(bits, 8, 3);
        var seconds = ReadUnits(bits, 16) + 10 * ReadTens(bits, 24, 3);
        var minutes = ReadUnits(bits, 32) + 10 * ReadTens(bits, 40, 3);
        var hours = ReadUnits(bits, 48) + 10 * ReadTens(bits, 56, 2);
        return new LtcStamp(hours, minutes, seconds, frames, fps <= 0 ? 30 : fps);
    }

    public static short[] Pcm(LtcStamp start, int frameCount, int sampleRate = 48000, double fps = 30)
    {
        fps = fps <= 0 ? 30 : fps;
        var samplesPerFrame = Math.Max(80, (int)Math.Round(sampleRate / fps));
        var samplesPerBit = Math.Max(2, samplesPerFrame / 80);
        var pcm = new short[frameCount * 80 * samplesPerBit];
        var polarity = true;
        var o = 0;
        for (var f = 0; f < frameCount; f++)
        {
            var ms = start.Milliseconds + f * (1000d / fps);
            var bits = Pack(LtcStamp.FromMilliseconds(ms, fps));
            foreach (var bit in bits)
            {
                polarity = !polarity;
                WriteHalf(pcm, ref o, samplesPerBit / 2, polarity);
                if (bit != 0) polarity = !polarity;
                WriteHalf(pcm, ref o, samplesPerBit - samplesPerBit / 2, polarity);
            }
        }
        return pcm;
    }

    public static byte[] Wav(LtcStamp start, int frameCount, int sampleRate = 48000, double fps = 30) =>
        WavHeader.WritePcm16Mono(Pcm(start, frameCount, sampleRate, fps), sampleRate);

    public static LtcStamp? Decode(ReadOnlySpan<short> pcm, int sampleRate, double fps = 30)
    {
        if (pcm.Length < 160) return null;
        fps = fps <= 0 ? 30 : fps;
        var crossings = new List<int>();
        for (var i = 1; i < pcm.Length; i++)
        {
            if (pcm[i] == 0) continue;
            if ((pcm[i - 1] < 0 && pcm[i] > 0) || (pcm[i - 1] > 0 && pcm[i] < 0))
                crossings.Add(i);
        }
        if (crossings.Count < 90) return null;
        var gaps = new List<int>();
        for (var i = 1; i < crossings.Count; i++)
            gaps.Add(crossings[i] - crossings[i - 1]);
        var median = gaps.OrderBy(g => g).Skip(gaps.Count / 2).First();
        var half = Math.Max(2, median);
        var full = half * 2;
        var bits = new List<byte>();
        var last = crossings[0];
        for (var i = 1; i < crossings.Count; i++)
        {
            var gap = crossings[i] - last;
            if (gap < half * 0.6) continue;
            if (gap < (half + full) / 2.0)
            {
                bits.Add(1);
                last = crossings[i];
                if (i + 1 < crossings.Count && crossings[i + 1] - crossings[i] < half * 1.5)
                {
                    i++;
                    last = crossings[i];
                }
            }
            else
            {
                bits.Add(0);
                last = crossings[i];
            }
        }
        for (var i = 0; i + 80 <= bits.Count; i++)
        {
            var window = bits.Skip(i).Take(80).ToArray();
            if (IsSync(window))
                return Unpack(window, fps);
        }
        return bits.Count >= 80 ? Unpack(bits.Take(80).ToArray(), fps) : null;
    }

    static bool IsSync(byte[] bits)
    {
        if (bits.Length < 80) return false;
        for (var i = 0; i < 16; i++)
            if (bits[64 + i] != ((Sync >> i) & 1)) return false;
        return true;
    }

    static void WriteUnits(byte[] bits, int offset, int value)
    {
        var n = Math.Abs(value) % 10;
        for (var i = 0; i < 4; i++)
            bits[offset + i] = (byte)((n >> i) & 1);
    }

    static void WriteTens(byte[] bits, int offset, int value, int width)
    {
        var n = Math.Abs(value) / 10;
        for (var i = 0; i < width; i++)
            bits[offset + i] = (byte)((n >> i) & 1);
    }

    static int ReadUnits(byte[] bits, int offset)
    {
        var n = 0;
        for (var i = 0; i < 4; i++) n |= bits[offset + i] << i;
        return n;
    }

    static int ReadTens(byte[] bits, int offset, int width)
    {
        var n = 0;
        for (var i = 0; i < width; i++) n |= bits[offset + i] << i;
        return n;
    }

    static void WriteHalf(short[] pcm, ref int o, int count, bool high)
    {
        var sample = (short)(high ? 22000 : -22000);
        for (var i = 0; i < count && o < pcm.Length; i++)
            pcm[o++] = sample;
    }
}
