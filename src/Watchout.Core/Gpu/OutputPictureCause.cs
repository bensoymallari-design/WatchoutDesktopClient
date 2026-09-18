namespace Watchout.Core.Gpu;

/// <summary>
/// Why Output is black or frozen on the last frame. Producer logs this so a
/// black wall is not a silent "waiting" line — the Log names the cause.
/// </summary>
public enum OutputPictureKind
{
    Idle,
    Picture,
    Opening,
    WaitingFirstFrame,
    DecodeFailed,
    Stalled,
    MissingFile,
    NoCue,
    HoldingLastFrame,
    LiveEmpty,
    GpuOff,
    NoHwnd
}

public readonly record struct OutputPictureHint(
    bool Playing,
    int DrawCount,
    int ReadyTextures,
    bool KeepLastFrame,
    GpuSourceKind Kind,
    bool DecoderOpening,
    bool DecoderReady,
    bool DecoderDead,
    bool DecoderStalled,
    bool FileMissing,
    bool LiveHasPixels,
    string? DecodeError);

public static class OutputPictureCause
{
    /// <summary>
    /// Healthy Play opens in under this. Log Opening / first-frame wait only
    /// after it, so a normal MP4 is not a false alarm.
    /// </summary>
    public const int QuietMs = 500;

    public static OutputPictureKind Classify(in OutputPictureHint h)
    {
        if (!h.Playing)
            return h.ReadyTextures > 0 ? OutputPictureKind.Picture : OutputPictureKind.Idle;
        if (h.ReadyTextures > 0 && !h.DecoderStalled)
            return OutputPictureKind.Picture;
        if (h.FileMissing)
            return OutputPictureKind.MissingFile;
        if (h.DecoderDead)
            return OutputPictureKind.DecodeFailed;
        if (h.DrawCount <= 0)
            return h.KeepLastFrame ? OutputPictureKind.HoldingLastFrame : OutputPictureKind.NoCue;
        if (h.Kind is GpuSourceKind.Ndi or GpuSourceKind.Capture)
            return h.LiveHasPixels ? OutputPictureKind.Picture : OutputPictureKind.LiveEmpty;
        if (h.DecoderStalled)
            return OutputPictureKind.Stalled;
        if (h.DecoderOpening || !h.DecoderReady)
            return OutputPictureKind.Opening;
        return OutputPictureKind.WaitingFirstFrame;
    }

    public static bool ShouldLog(OutputPictureKind kind, bool alreadyLoggedThisKind, double msInThisKind)
    {
        if (kind is OutputPictureKind.Picture or OutputPictureKind.Idle) return false;
        if (alreadyLoggedThisKind) return false;
        if (kind is OutputPictureKind.Opening or OutputPictureKind.WaitingFirstFrame
            or OutputPictureKind.HoldingLastFrame or OutputPictureKind.GpuOff
            or OutputPictureKind.NoHwnd)
            return msInThisKind >= QuietMs;
        return true;
    }

    /// <summary>
    /// Play + Output live but PresentOutput never ran (HWND 0 or GPU off).
    /// Log after QuietMs so a healthy first Present is not a false alarm.
    /// </summary>
    public static OutputPictureKind WhenPresentSkipped(bool playing, int liveOutputs, bool gpuOn, bool hwndOk)
    {
        if (!playing || liveOutputs <= 0) return OutputPictureKind.Idle;
        if (!gpuOn) return OutputPictureKind.GpuOff;
        if (!hwndOk) return OutputPictureKind.NoHwnd;
        return OutputPictureKind.Idle;
    }

    public static string Level(OutputPictureKind kind) => kind switch
    {
        OutputPictureKind.DecodeFailed or OutputPictureKind.MissingFile => "error",
        OutputPictureKind.Idle or OutputPictureKind.Picture => "info",
        _ => "warn"
    };

    public static string Message(OutputPictureKind kind, string key, string? error)
    {
        var src = string.IsNullOrWhiteSpace(key) ? "the clip" : key;
        return kind switch
        {
            OutputPictureKind.Picture => "Output has picture on the wall",
            OutputPictureKind.Opening =>
                $"Output black — DXVA is still opening this MP4 ({src}). Not NDI/capture. If it stays black, use 8-bit H.264.",
            OutputPictureKind.WaitingFirstFrame =>
                $"Output black — DXVA opened {src} but has no picture yet. Prefer 8-bit H.264 (HandBrake Rec.709). A YouTube title is not Dolby Vision.",
            OutputPictureKind.DecodeFailed =>
                $"Output black — DXVA could not decode this MP4 ({src})"
                + (string.IsNullOrWhiteSpace(error) ? "" : $": {error}")
                + ". Prefer 8-bit H.264, or Assets → Create H.264 version.",
            OutputPictureKind.Stalled =>
                $"Output stuck — this MP4 ({src}) stopped delivering frames. Rebuilding DXVA.",
            OutputPictureKind.MissingFile =>
                $"Output black — the MP4 file is missing ({src}).",
            OutputPictureKind.NoCue =>
                "Output black — Play is on but no cue is on this wall (playhead off the clip, or the cue was deleted).",
            OutputPictureKind.HoldingLastFrame =>
                $"Output stuck — holding the last frame while the next MP4 opens ({src}).",
            OutputPictureKind.LiveEmpty =>
                $"Output black — {src} has no live pixels yet (NDI/capture, not the MP4).",
            OutputPictureKind.GpuOff =>
                "Output black — D3D11 compositor is off so Play never presented"
                + (string.IsNullOrWhiteSpace(error) ? "" : $" ({error})")
                + ". Not RAM and not this MP4. WatchMe drops the empty wall HWND so the WPF window can show picture.",
            OutputPictureKind.NoHwnd =>
                "Output black — Output window has no wall HWND (CreateWindow failed or handle is 0). Play is not presenting. Not the MP4 and not RAM.",
            _ => ""
        };
    }
}
