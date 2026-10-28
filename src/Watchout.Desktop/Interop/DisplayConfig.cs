using System.Runtime.InteropServices;
using Watchout.Core.Stage;

namespace Watchout.Desktop.Interop;

/// <summary>
/// Reads the monitor EDID preferred mode and the GDI device name Windows uses
/// for that port. QueryDisplayConfig is called the way PowerToys and Chromium
/// do it: <c>QDC_VIRTUAL_MODE_AWARE</c>, then the mode index in the high 16
/// bits. Without that, DisplayPort, USB-C, and HDMI-to-C on a laptop return
/// the built-in panel's mode (or the whole query fails) while a plain desktop
/// HDMI still works.
/// </summary>
static class DisplayConfig
{
    internal readonly record struct EdidInfo(
        string DeviceKey,
        string FriendlyName,
        int PreferredWidth,
        int PreferredHeight,
        int CurrentWidth,
        int CurrentHeight,
        int Left,
        int Top,
        string Connector,
        bool IsInternal);

    internal static List<EdidInfo> List()
    {
        var list = new List<EdidInfo>();
        try
        {
            var virtualAware = true;
            if (!TryQuery(QdcOnlyActivePaths | QdcVirtualModeAware, out var paths, out var modes, out var pathCount, out var modeCount))
            {
                virtualAware = false;
                if (!TryQuery(QdcOnlyActivePaths, out paths, out modes, out pathCount, out modeCount))
                    return list;
            }
            for (var i = 0; i < pathCount; i++)
            {
                var path = paths[i];
                var source = new SourceName();
                source.Header.Type = GetSourceName;
                source.Header.Size = (uint)Marshal.SizeOf<SourceName>();
                source.Header.AdapterId = path.Source.AdapterId;
                source.Header.Id = path.Source.Id;
                var sourceOk = GetInfo(ref source) == 0;

                var preferred = new PreferredMode();
                preferred.Header.Type = GetTargetPreferredMode;
                preferred.Header.Size = (uint)Marshal.SizeOf<PreferredMode>();
                preferred.Header.AdapterId = path.Target.AdapterId;
                preferred.Header.Id = path.Target.Id;
                GetInfo(ref preferred);

                var target = new TargetName();
                target.Header.Type = GetTargetName;
                target.Header.Size = (uint)Marshal.SizeOf<TargetName>();
                target.Header.AdapterId = path.Target.AdapterId;
                target.Header.Id = path.Target.Id;
                GetInfo(ref target);

                var key = sourceOk ? DeviceKey(source.ViewGdiDeviceName) : "";
                var friendly = string.IsNullOrWhiteSpace(target.MonitorFriendlyDeviceName)
                    ? ""
                    : target.MonitorFriendlyDeviceName.Trim();
                var tech = path.Target.OutputTechnology;
                if (tech == 0 && target.OutputTechnology != 0)
                    tech = target.OutputTechnology;
                var connector = DisplayConnectors.Name(tech, target.MonitorDevicePath, friendly);
                var current = CurrentDesktop(path, modes, modeCount, virtualAware);
                if (string.IsNullOrEmpty(key) && current.W < 64 && string.IsNullOrEmpty(friendly))
                    continue;
                list.Add(new EdidInfo(
                    key,
                    friendly,
                    (int)preferred.Width,
                    (int)preferred.Height,
                    current.W,
                    current.H,
                    current.X,
                    current.Y,
                    connector,
                    DisplayConnectors.IsInternalTechnology(tech)));
            }
        }
        catch
        {
            // CCD is best-effort — EnumDisplayMonitors still fills a list.
        }
        return list;
    }

    static bool TryQuery(uint flags, out PathInfo[] paths, out ModeInfo[] modes, out uint pathCount, out uint modeCount)
    {
        paths = [];
        modes = [];
        pathCount = 0;
        modeCount = 0;
        if (GetDisplayConfigBufferSizes(flags, out pathCount, out modeCount) != 0)
            return false;
        if (pathCount == 0) return false;
        paths = new PathInfo[pathCount];
        modes = new ModeInfo[Math.Max(1, modeCount)];
        var pc = pathCount;
        var mc = modeCount;
        if (QueryDisplayConfig(flags, ref pc, paths, ref mc, modes, IntPtr.Zero) != 0)
            return false;
        pathCount = pc;
        modeCount = mc;
        return true;
    }

    internal static string DeviceKey(string? device) =>
        (device ?? "").Replace(@"\\.\", "", StringComparison.Ordinal).Trim();

    const uint SourceType = 1;
    const uint TargetType = 2;

    static (int W, int H, int X, int Y) CurrentDesktop(PathInfo path, ModeInfo[] modes, uint modeCount, bool virtualAware)
    {
        var virtualMode = virtualAware && (path.Flags & DisplayConnectors.PathSupportsVirtualMode) != 0;
        var source = ReadSource(path.Source.ModeInfoIdx, modes, modeCount, virtualMode);
        if (source.W >= 64 && source.H >= 64) return source;
        var target = ReadTargetActive(path.Target.ModeInfoIdx, modes, modeCount, virtualMode);
        return (target.W, target.H, source.X, source.Y);
    }

    static (int W, int H, int X, int Y) ReadSource(uint packed, ModeInfo[] modes, uint modeCount, bool virtualMode)
    {
        var i = DisplayConnectors.ModeIndex(packed, modeCount, virtualMode);
        if (i < 0 || i >= modes.Length) return (0, 0, 0, 0);
        var mode = modes[i];
        if (mode.InfoType != SourceType) return (0, 0, 0, 0);
        return ((int)mode.Width, (int)mode.Height, mode.PositionX, mode.PositionY);
    }

    static (int W, int H) ReadTargetActive(uint packed, ModeInfo[] modes, uint modeCount, bool virtualMode)
    {
        var i = DisplayConnectors.ModeIndex(packed, modeCount, virtualMode);
        if (i < 0 || i >= modes.Length) return (0, 0);
        var mode = modes[i];
        if (mode.InfoType != TargetType) return (0, 0);
        return ((int)mode.ActiveWidth, (int)mode.ActiveHeight);
    }

    static int GetInfo<T>(ref T packet) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(packet, ptr, false);
            var err = DisplayConfigGetDeviceInfo(ptr);
            packet = Marshal.PtrToStructure<T>(ptr);
            return err;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    const uint QdcOnlyActivePaths = 2;
    const uint QdcVirtualModeAware = 0x10;
    const uint GetSourceName = 1;
    const uint GetTargetName = 2;
    const uint GetTargetPreferredMode = 3;

    [DllImport("user32.dll")]
    static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] PathInfo[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] ModeInfo[] modeArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    static extern int DisplayConfigGetDeviceInfo(IntPtr requestPacket);

    [StructLayout(LayoutKind.Sequential)]
    struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Header
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public Rational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PathInfo
    {
        public PathSourceInfo Source;
        public PathTargetInfo Target;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public int PositionX;
        public int PositionY;
        public uint Pad0;
        public uint ActiveWidth;
        public uint ActiveHeight;
        public uint Pad3;
        public uint Pad4;
        public uint Pad5;
        public uint Pad6;
        public uint Pad7;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SourceName
    {
        public Header Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TargetName
    {
        public Header Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Region2d
    {
        public uint Cx;
        public uint Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct VideoSignal
    {
        public ulong PixelRate;
        public Rational HSyncFreq;
        public Rational VSyncFreq;
        public Region2d ActiveSize;
        public Region2d TotalSize;
        public uint VideoStandard;
        public uint ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PreferredMode
    {
        public Header Header;
        public uint Width;
        public uint Height;
        public VideoSignal TargetMode;
    }
}
