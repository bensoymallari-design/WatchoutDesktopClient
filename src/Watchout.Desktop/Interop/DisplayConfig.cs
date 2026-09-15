using System.Runtime.InteropServices;

namespace Watchout.Desktop.Interop;

/// <summary>
/// Reads the monitor EDID preferred mode (what an LED processor actually advertises)
/// and the GDI device name Windows uses for that HDMI/DP port.
/// </summary>
static class DisplayConfig
{
    internal readonly record struct EdidInfo(string DeviceKey, string FriendlyName, int PreferredWidth, int PreferredHeight);

    internal static List<EdidInfo> List()
    {
        var list = new List<EdidInfo>();
        try
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0)
                return list;
            var paths = new PathInfo[pathCount];
            var modes = new ModeInfo[modeCount];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return list;
            for (var i = 0; i < pathCount; i++)
            {
                var path = paths[i];
                var source = new SourceName();
                source.Header.Type = GetSourceName;
                source.Header.Size = (uint)Marshal.SizeOf<SourceName>();
                source.Header.AdapterId = path.Source.AdapterId;
                source.Header.Id = path.Source.Id;
                if (GetInfo(ref source) != 0) continue;

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

                var key = DeviceKey(source.ViewGdiDeviceName);
                if (string.IsNullOrEmpty(key)) continue;
                var friendly = string.IsNullOrWhiteSpace(target.MonitorFriendlyDeviceName)
                    ? ""
                    : target.MonitorFriendlyDeviceName.Trim();
                list.Add(new EdidInfo(key, friendly, (int)preferred.Width, (int)preferred.Height));
            }
        }
        catch
        {
            // CCD is best-effort — EnumDisplayMonitors still fills a list.
        }
        return list;
    }

    internal static string DeviceKey(string? device) =>
        (device ?? "").Replace(@"\\.\", "", StringComparison.Ordinal).Trim();

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
        public uint Pad1;
        public uint Pad2;
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
