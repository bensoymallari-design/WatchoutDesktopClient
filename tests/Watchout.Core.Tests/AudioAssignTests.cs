using Watchout.Core;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Xunit;

namespace Watchout.Core.Tests;

public class AudioAssignTests
{
    [Fact]
    public void MenuListsDefaultThenRoleAliasesThenEndpoints()
    {
        var endpoints = new[]
        {
            new AudioDevice { Id = "realtek", Name = "Speaker (Realtek(R) Audio)", Channels = 2, Driver = "WASAPI" },
            new AudioDevice { Id = "droid", Name = "Internal AUX Jack (DroidCam Audio)", Channels = 2, Driver = "WASAPI" },
        };
        var menu = AudioAssign.MenuDevices(endpoints, "realtek", "Speaker (Realtek(R) Audio)", "realtek", "Speaker (Realtek(R) Audio)");
        Assert.Equal("Windows default speaker", menu[0].Name);
        Assert.Equal("default", menu[0].Id);
        Assert.Equal("Default - Speaker (Realtek(R) Audio)", menu[1].Name);
        Assert.Equal("Communications - Speaker (Realtek(R) Audio)", menu[2].Name);
        Assert.Contains(menu, d => d.Name == "Speaker (Realtek(R) Audio)");
        Assert.Contains(menu, d => d.Name.Contains("DroidCam"));
        Assert.Equal("WASAPI · 2 ch", AudioAssign.StatusLine(menu[0]));
    }

    [Fact]
    public void ResolveFallsBackToDefault()
    {
        var menu = AudioAssign.MenuDevices([], null, null, null, null);
        Assert.Equal("default", AudioAssign.Resolve(menu, "missing").Id);
        Assert.Equal("default", AudioAssign.Resolve(menu, null).Id);
    }

    [Fact]
    public void SessionStoresTheSelectedOutput()
    {
        var session = new ProducerSession();
        session.NewShow();
        session.SetAudioOutput(new AudioDevice { Id = "realtek", Name = "Speaker (Realtek(R) Audio)", Channels = 2, Driver = "WASAPI" });
        Assert.Equal("realtek", session.ActiveAudioDevice.Id);
        Assert.Equal("Speaker (Realtek(R) Audio)", session.ActiveAudioDevice.Name);
        session.SetAudioOutput(AudioAssign.DefaultSpeaker());
        Assert.Equal("default", session.ActiveAudioDevice.Id);
    }
}
