namespace Watchout.Core.Models;

public enum CueType { Media, Control, Marker, Output, Variable, Artnet }

public enum AssetKind { Image, Video, Audio, Composition, Ndi, Capture, Procedural }

public enum PlaybackState { Play, Pause, Stop }

public enum OutputType { GPU, SDI, NDI, Virtual }

public enum TweenType
{
    Opacity, PositionX, PositionY, PositionZ, ScaleX, ScaleY,
    RotationX, RotationY, RotationZ, Volume, Blur, Brightness,
    Contrast, Saturation, Hue, CropTop, CropBottom, CropLeft, CropRight, WipeCompletion
}

public enum Easing
{
    Linear, QuadIn, QuadOut, QuadInOut, CubicIn, CubicOut, CubicInOut,
    SineIn, SineOut, SineInOut, ExpoIn, ExpoOut, ExpoInOut, BackOut, BounceOut, ElasticOut
}

public enum ControlState { Play, Pause, Stop }

public enum JumpMode { None, Time, Cue }

public enum ControlTarget { This, All, List }

public enum SelectionKind
{
    None, Cue, Layer, Timeline, Asset, Display, Node, Variable, Device, CueSet, TweenPoint
}

public enum StageEditMode
{
    Cues,
    Displays
}

public enum MediaReplaceMode
{
    NewSize,
    KeepOldSize,
    FitProportionally
}

public enum StageHitKind
{
    None,
    Cue,
    CueHandle,
    Display,
    DisplayHandle
}

public enum WindowId
{
    Stage, Properties, Assets, Timelines, Timeline, Devices, Nodes, Variables, Cues, CueSets, Log
}

public sealed class Vec3
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

public sealed class Vec2
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class Crop
{
    public double Top { get; set; }
    public double Bottom { get; set; }
    public double Left { get; set; }
    public double Right { get; set; }
}

public sealed class TweenPoint
{
    public string Id { get; set; } = "";
    public double Time { get; set; }
    public double Value { get; set; }
    public Easing Easing { get; set; } = Easing.Linear;
}

public sealed class Tween
{
    public string Id { get; set; } = "";
    public TweenType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public List<TweenPoint> Points { get; set; } = [];
    public string? Expression { get; set; }
}

public sealed class CueControl
{
    public ControlState State { get; set; } = ControlState.Play;
    public ControlTarget Target { get; set; } = ControlTarget.This;
    public List<string> TimelineIds { get; set; } = [];
    public JumpMode JumpMode { get; set; } = JumpMode.None;
    public double JumpTime { get; set; }
    public string? JumpCueId { get; set; }
}

public sealed class CueOutput
{
    public string Protocol { get; set; } = "tcp";
    public string Address { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class CueVariable
{
    public string VariableId { get; set; } = "";
    public double Value { get; set; }
}

public sealed class CueArtnet
{
    public int Universe { get; set; }
    public int StartChannel { get; set; }
    public List<double> Values { get; set; } = [];
}

public sealed class Cue
{
    public string Id { get; set; } = "";
    public CueType Type { get; set; } = CueType.Media;
    public string Name { get; set; } = "Cue";
    public string LayerId { get; set; } = "";
    public double Start { get; set; }
    public double Duration { get; set; }
    public string? AssetId { get; set; }
    public bool Enabled { get; set; } = true;
    public string Color { get; set; } = "#3b82c4";
    public Vec3 Position { get; set; } = new();
    public Vec2 Scale { get; set; } = new() { X = 100, Y = 100 };
    public Vec3 Rotation { get; set; } = new();
    public double Opacity { get; set; } = 100;
    public double Volume { get; set; } = 100;
    public bool? Muted { get; set; }
    public double Blur { get; set; } = 0.5;
    public double Brightness { get; set; }
    public double Contrast { get; set; }
    public double Saturation { get; set; } = 100;
    public double Hue { get; set; }
    public Crop Crop { get; set; } = new();
    public Vec2 Anchor { get; set; } = new() { X = 0.5, Y = 0.5 };
    public bool FreeRunning { get; set; }
    public bool FadeIn { get; set; }
    public bool FadeOut { get; set; }
    public double FadeInDuration { get; set; } = 500;
    public double FadeOutDuration { get; set; } = 500;
    public Easing FadeCurve { get; set; } = Easing.Linear;
    public List<Tween> Tweens { get; set; } = [];
    public double Speed { get; set; } = 100;
    public double WipeCompletion { get; set; } = 100;
    public double WipeAngle { get; set; }
    public double WipeFeather { get; set; } = 8;
    public double Temperature { get; set; }
    public double Exposure { get; set; }
    public bool ChromaKeyEnabled { get; set; }
    public string ChromaKeyColor { get; set; } = "#00FF00";
    public double ChromaKeyTolerance { get; set; } = 28;
    public CueControl? Control { get; set; }
    public CueOutput? Output { get; set; }
    public CueVariable? Variable { get; set; }
    public CueArtnet? Artnet { get; set; }
}

public sealed class Layer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Layer";
    public bool Enabled { get; set; } = true;
    public bool Locked { get; set; }
    public bool Expanded { get; set; } = true;
}

public sealed class Timeline
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Main Timeline";
    public double Duration { get; set; } = 120_000;
    public PlaybackState Playback { get; set; } = PlaybackState.Stop;
    public double Playhead { get; set; }
    public bool Loop { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public double Rate { get; set; } = 1;
    public List<Layer> Layers { get; set; } = [];
    public List<Cue> Cues { get; set; } = [];
    public string PlayExpression { get; set; } = "";
    public string PauseExpression { get; set; } = "";
    public string StopExpression { get; set; } = "";
}

public sealed class Asset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public AssetKind Kind { get; set; } = AssetKind.Video;
    public string? FolderId { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Duration { get; set; }
    public double Fps { get; set; } = 60;
    public string Url { get; set; } = "";
    public string Codec { get; set; } = "";
    public string Color { get; set; } = "#3b82c4";
    public bool Optimized { get; set; } = true;
    public string Notes { get; set; } = "";
    public string? OriginalPath { get; set; }
    public string? ProxyPath { get; set; }
    public int? ProxyVersion { get; set; }
    public long? Bytes { get; set; }
    public bool? Linked { get; set; }
    public string? PosterUrl { get; set; }
}

public sealed class Display
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Display 1";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Width { get; set; } = 1920;
    public double Height { get; set; } = 1080;
    public double Rotation { get; set; }
    public OutputType OutputType { get; set; } = OutputType.GPU;
    public int Channel { get; set; } = 1;
    public string NodeId { get; set; } = "local-runner";
    public bool Enabled { get; set; } = true;
    public bool Blend { get; set; }
    public double BlendWidth { get; set; } = 128;
    public bool Virtual { get; set; }
    public string? ScreenId { get; set; }
    public bool MaskEnabled { get; set; }
    public bool MaskInvert { get; set; }
    public string? MaskUrl { get; set; }
}

public sealed class NodeService
{
    public bool Producer { get; set; }
    public bool Director { get; set; }
    public bool Runner { get; set; }
    public bool AssetManager { get; set; }
}

public sealed class ShowNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "localhost";
    public string Address { get; set; } = "127.0.0.1";
    public bool Online { get; set; } = true;
    public NodeService Services { get; set; } = new();
    public string Gpu { get; set; } = "Desktop GPU";
    public double Cpu { get; set; }
    public double GpuLoad { get; set; }
    public double Ram { get; set; }
    public double Disk { get; set; }
    public string Version { get; set; } = "1.0.0";
}

public sealed class AudioDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "WASAPI Default";
    public string NodeId { get; set; } = "local-runner";
    public int Channels { get; set; } = 2;
    public string Driver { get; set; } = "WASAPI";
}

public sealed class CaptureDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "NDI Source 1";
    public string NodeId { get; set; } = "local-runner";
    public string Kind { get; set; } = "NDI";
    public string Signal { get; set; } = "WatchMe-CAPTURE";
    public string? DisplayId { get; set; }
}

public sealed class ShowVariable
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "intensity";
    public double Value { get; set; }
    public double Min { get; set; }
    public double Max { get; set; } = 1;
    public string Protocol { get; set; } = "none";
    public string Address { get; set; } = "";
}

public sealed class CueSet
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Default";
    public List<string> CueIds { get; set; } = [];
    public bool Enabled { get; set; } = true;
}

public sealed class ShowPrefs
{
    public double Fps { get; set; } = 60;
    public Vec3 EyePoint { get; set; } = new();
    public bool SdiGenlock { get; set; }
    public List<string> AudioBuses { get; set; } = ["Master", "Bus 1", "Bus 2"];
    public double ImageDuration { get; set; } = 5000;
    public bool AutoFade { get; set; }
    public double FadeIn { get; set; } = 500;
    public double FadeOut { get; set; } = 500;
    public Easing FadeCurve { get; set; } = Easing.Linear;
    public string NdiExtraIps { get; set; } = "";
    public MediaReplaceMode MediaReplaceMode { get; set; } = MediaReplaceMode.KeepOldSize;
    public bool AutoStart { get; set; }
}

public sealed class Show
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Untitled Show";
    public string CreatedAt { get; set; } = "";
    public string ModifiedAt { get; set; } = "";
    public string Director { get; set; } = "localhost";
    public string AssetManager { get; set; } = "localhost";
    public ShowPrefs Prefs { get; set; } = new();
    public List<Asset> Assets { get; set; } = [];
    public List<Display> Displays { get; set; } = [];
    public List<Timeline> Timelines { get; set; } = [];
    public List<ShowNode> Nodes { get; set; } = [];
    public List<AudioDevice> AudioDevices { get; set; } = [];
    public List<CaptureDevice> CaptureDevices { get; set; } = [];
    public List<ShowVariable> Variables { get; set; } = [];
    public List<CueSet> CueSets { get; set; } = [];
}

public sealed class Selection
{
    public SelectionKind Kind { get; set; } = SelectionKind.None;
    public List<string> Ids { get; set; } = [];
}

public sealed class WindowLayout
{
    public WindowId Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public bool Open { get; set; } = true;
    public int Z { get; set; }
}

public sealed class OutputScreen
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int PhysicalWidth { get; set; }
    public int PhysicalHeight { get; set; }
    public bool IsPrimary { get; set; }
    public double ScaleFactor { get; set; } = 1;
}

public sealed class RecentShow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string SavedAt { get; set; } = "";
}

public sealed class LogEntry
{
    public string Id { get; set; } = "";
    public long Ts { get; set; }
    public string Level { get; set; } = "info";
    public string Message { get; set; } = "";
}

public sealed class ImportedMedia
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public AssetKind Kind { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Duration { get; set; }
    public double Fps { get; set; }
    public string Url { get; set; } = "";
    public string Codec { get; set; } = "";
    public string Color { get; set; } = "";
    public bool Optimized { get; set; }
    public string Notes { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string? ProxyPath { get; set; }
    public int? ProxyVersion { get; set; }
    public long Bytes { get; set; }
    public bool Linked { get; set; }
    public string? PosterUrl { get; set; }
}

public sealed class MediaProbe
{
    public int Width { get; set; }
    public int Height { get; set; }
    public double DurationMs { get; set; } = 10_000;
    public double Fps { get; set; } = 60;
    public string Codec { get; set; } = "";
    public bool HasAudio { get; set; }
}
