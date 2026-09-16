using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Watchout.Desktop.Interop;

/// <summary>
/// DXGI Output (and a killed exclusive Present) can leave HDMI at a mode the
/// LED processor will not show. Win+P then does nothing — re-apply the Windows
/// display database and force Extend.
/// </summary>
public static class DisplayReset
{
    const uint SdcApply = 0x00000080;
    const uint SdcTopologyExtend = 0x00000004;
    const uint AttachedToDesktop = 0x00000001;

    public static void Restore()
    {
        try { ChangeDisplaySettings(IntPtr.Zero, 0); } catch { /* best-effort */ }
        for (uint i = 0; ; i++)
        {
            var device = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            if ((device.StateFlags & AttachedToDesktop) == 0) continue;
            try { ChangeDisplaySettingsEx(device.DeviceName, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero); }
            catch { /* adapter */ }
        }
        try { SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SdcApply | SdcTopologyExtend); }
        catch { /* CCD */ }
        try
        {
            var exe = Path.Combine(Environment.SystemDirectory, "DisplaySwitch.exe");
            if (File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "/extend",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                });
            }
        }
        catch { /* DisplaySwitch optional */ }
    }

    [DllImport("user32.dll")]
    static extern int ChangeDisplaySettings(IntPtr lpDevMode, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int ChangeDisplaySettingsEx(string lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, int dwFlags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll")]
    static extern int SetDisplayConfig(uint pathCount, IntPtr pathArray, uint modeCount, IntPtr modeArray, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }
}
