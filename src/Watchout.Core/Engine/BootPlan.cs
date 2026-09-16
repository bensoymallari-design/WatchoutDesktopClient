namespace Watchout.Core.Engine;

/// <summary>
/// Resolume/WATCHOUT-style startup: name the engines, then the desktop host
/// actually starts them before Producer opens.
/// </summary>
public readonly record struct BootStep(string Id, string Phrase, string Detail);

public static class BootPlan
{
    public static IReadOnlyList<BootStep> Steps { get; } =
    [
        new("framework", "Initializing framework", "Folders, settings, and the desktop host"),
        new("controller", "Initializing application controller", "Show session and the 60 Hz clock"),
        new("audio", "Initializing audio engine", "WASAPI outputs"),
        new("video", "Initializing video engine", "D3D11 compositor and DXVA"),
        new("display", "Initializing display engine", "Screens and EDID"),
        new("ndi", "Initializing NDI", "NDI Runtime"),
        new("capture", "Initializing capture engine", "HDMI / SDI cards"),
        new("codec", "Initializing codec toolkit", "ffmpeg for HAP / DXV / ProRes"),
    ];

    public static string Line(BootStep step) => step.Phrase + "…";

    public static string DoneLine(BootStep step, string note)
    {
        var name = step.Phrase.StartsWith("Initializing ", StringComparison.Ordinal)
            ? step.Phrase["Initializing ".Length..]
            : step.Phrase;
        return string.IsNullOrEmpty(note) ? name : name + "  —  " + note;
    }

    public static double Progress(int completed, int total) =>
        total <= 0 ? 1 : Math.Clamp(completed / (double)total, 0, 1);
}
