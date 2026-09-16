using NAudio.CoreAudioApi;
using NAudio.Wave;
using Watchout.Core.Media;
using Watchout.Core.Models;
using Watchout.Desktop.Interop;

namespace Watchout.Desktop.Engine;

/// <summary>
/// WASAPI output engine. Resolume opens an audio device at launch; WatchMe
/// enumerates endpoints and opens a shared 48 kHz client so Play is not the
/// first time the driver wakes up. File playback uses the same player type.
/// </summary>
public static class AudioEngine
{
    public static IReadOnlyList<AudioDevice> Devices { get; private set; } = [AudioAssign.DefaultSpeaker()];
    public static AudioDevice Default { get; private set; } = AudioAssign.DefaultSpeaker();
    public static string Status { get; private set; } = "not started";

    public static IWavePlayer CreatePlayer()
    {
        try
        {
            return new WasapiOut(AudioClientShareMode.Shared, 80);
        }
        catch
        {
            return new WaveOutEvent { DesiredLatency = 80 };
        }
    }

    public static string Warm()
    {
        try
        {
            Devices = AudioOutputs.List();
            Default = Devices.FirstOrDefault() ?? AudioAssign.DefaultSpeaker();
            using var output = CreatePlayer();
            output.Init(new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)));
            Status = $"{Devices.Count} WASAPI output(s) · {Default.Name} · 48 kHz";
        }
        catch (Exception ex)
        {
            Devices = Devices.Count > 0 ? Devices : [AudioAssign.DefaultSpeaker()];
            Default = Devices[0];
            Status = $"WASAPI listed {Devices.Count} device(s) — {ex.Message}";
        }
        return Status;
    }
}
