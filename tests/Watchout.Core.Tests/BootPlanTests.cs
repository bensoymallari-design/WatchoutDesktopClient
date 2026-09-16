using Watchout.Core.Engine;
using Xunit;

namespace Watchout.Core.Tests;

public class BootPlanTests
{
    [Fact]
    public void NamesTheResolumeStyleEngines()
    {
        Assert.Equal(8, BootPlan.Steps.Count);
        Assert.Equal("framework", BootPlan.Steps[0].Id);
        Assert.Equal("Initializing framework", BootPlan.Steps[0].Phrase);
        Assert.Equal("Initializing application controller", BootPlan.Steps[1].Phrase);
        Assert.Equal("Initializing audio engine", BootPlan.Steps[2].Phrase);
        Assert.Equal("Initializing video engine", BootPlan.Steps[3].Phrase);
        Assert.Equal("Initializing display engine", BootPlan.Steps[4].Phrase);
        Assert.Equal("Initializing NDI", BootPlan.Steps[5].Phrase);
        Assert.Equal("Initializing capture engine", BootPlan.Steps[6].Phrase);
        Assert.Equal("Initializing codec toolkit", BootPlan.Steps[7].Phrase);
    }

    [Fact]
    public void LineAndProgressMatchTheSplash()
    {
        var step = BootPlan.Steps[0];
        Assert.Equal("Initializing framework…", BootPlan.Line(step));
        Assert.Equal("framework  —  folders", BootPlan.DoneLine(step, "folders"));
        Assert.Equal(0, BootPlan.Progress(0, 8));
        Assert.Equal(0.5, BootPlan.Progress(4, 8));
        Assert.Equal(1, BootPlan.Progress(8, 8));
        Assert.Equal(1, BootPlan.Progress(1, 0));
    }
}
