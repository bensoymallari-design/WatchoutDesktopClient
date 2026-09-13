using Watchout.Core;
using Xunit;

namespace Watchout.Core.Tests;

public class TimeFormatTests
{
    [Fact]
    public void PlayTimeShowsClockSecondsAndMilliseconds()
    {
        Assert.Equal("00:02:05.040", TimeFormat.FormatMs(125040));
        Assert.Equal("00:02:05.040  ·  125.040 s  ·  125040 ms", TimeFormat.FormatPlayTime(125040));
        Assert.Equal("00:00:00.000  ·  0.000 s  ·  0 ms", TimeFormat.FormatPlayTime(0));
        Assert.Equal(125040, TimeFormat.ParseTimecode("00:02:05.040"));
        Assert.Equal(125040, TimeFormat.ParseTimecode("125.04"));
    }
}
