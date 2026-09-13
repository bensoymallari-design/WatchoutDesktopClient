namespace Watchout.Core;

public static class Ids
{
    public static string New(string prefix = "id")
    {
        var rand = Random.Shared.NextInt64(0, 1L << 40);
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x")[^4..];
        return $"{prefix}_{ToBase36(rand)}{stamp}";
    }

    static string ToBase36(long value)
    {
        const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (value <= 0) return "0";
        Span<char> buf = stackalloc char[16];
        var i = buf.Length;
        while (value > 0 && i > 0)
        {
            buf[--i] = alphabet[(int)(value % 36)];
            value /= 36;
        }
        return new string(buf[i..]);
    }
}
