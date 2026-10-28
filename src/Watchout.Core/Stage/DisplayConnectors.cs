namespace Watchout.Core.Stage;

/// <summary>
/// Connector names from Windows <c>DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY</c>.
/// OBS, mpv, and Resolume treat every active OS screen as a real output.
/// The GPU reports a USB-C DP-alt / HDMI-to-C dongle as DisplayPort, and a
/// USB4 / Thunderbolt tunnel as USB-C. Both are external walls. The built-in
/// panel is LVDS, embedded DisplayPort, or INTERNAL — that one is the producer.
/// </summary>
public static class DisplayConnectors
{
    public const uint Other = 0xFFFFFFFFu;
    public const uint Vga = 0;
    public const uint SVideo = 1;
    public const uint Composite = 2;
    public const uint Component = 3;
    public const uint Dvi = 4;
    public const uint Hdmi = 5;
    public const uint Lvds = 6;
    public const uint DJpn = 8;
    public const uint Sdi = 9;
    public const uint DisplayPort = 10;
    public const uint DisplayPortEmbedded = 11;
    public const uint Udi = 12;
    public const uint UdiEmbedded = 13;
    public const uint SdtvDongle = 14;
    public const uint Miracast = 15;
    public const uint IndirectWired = 16;
    public const uint IndirectVirtual = 17;
    public const uint UsbTunnel = 18;
    public const uint Internal = 0x80000000;

    public const uint PathSupportsVirtualMode = 0x8;
    public const uint InvalidModeIndex = 0xFFFFFFFFu;
    public const uint InvalidVirtualIndex = 0xFFFFu;

    /// <summary>
    /// Mode index into the CCD mode array. With
    /// <c>DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE</c> (DisplayPort, USB-C,
    /// HDMI-to-C, hybrid laptop GPUs) the mode index is the high 16 bits.
    /// The low 16 bits are the clone group. Reading the low word returns the
    /// laptop panel's mode and the wall is sized wrong.
    /// </summary>
    public static int ModeIndex(uint packed, uint modeCount, bool virtualMode)
    {
        if (modeCount == 0) return -1;
        if (virtualMode)
        {
            var idx = (packed >> 16) & InvalidVirtualIndex;
            if (idx == InvalidVirtualIndex || idx >= modeCount) return -1;
            return (int)idx;
        }
        if (packed == InvalidModeIndex || packed >= modeCount) return -1;
        return (int)packed;
    }

    public static bool IsInternalTechnology(uint technology) =>
        technology is Lvds or DisplayPortEmbedded or UdiEmbedded or Internal;

    public static string Name(uint technology, string? devicePath = null, string? friendlyName = null)
    {
        if (technology == UsbTunnel) return "USB-C";
        if (technology == DisplayPort && LooksLikeUsbC(devicePath, friendlyName))
            return "USB-C";
        return technology switch
        {
            Hdmi or SdtvDongle => "HDMI",
            DisplayPort or Udi or DJpn => "DisplayPort",
            Dvi => "DVI",
            Vga => "VGA",
            Sdi => "SDI",
            IndirectWired => "USB",
            IndirectVirtual => "Virtual",
            Miracast => "Wireless",
            Lvds or DisplayPortEmbedded or UdiEmbedded or Internal => "Internal",
            SVideo or Composite or Component => "Analog",
            _ => "",
        };
    }

    /// <summary>
    /// HDMI, DisplayPort, USB-C (including HDMI-to-C and USB4), DVI, VGA,
    /// SDI, DisplayLink, and wireless screens Windows has extended.
    /// </summary>
    public static bool IsExternalName(string? connector) =>
        connector is "HDMI" or "DisplayPort" or "USB-C" or "DVI" or "VGA" or "SDI" or "USB" or "Analog" or "Wireless" or "Virtual";

    static bool LooksLikeUsbC(string? devicePath, string? friendlyName)
    {
        var blob = $"{devicePath} {friendlyName}";
        return blob.Contains("USB", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("Type-C", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("Thunderbolt", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("USB4", StringComparison.OrdinalIgnoreCase);
    }
}
