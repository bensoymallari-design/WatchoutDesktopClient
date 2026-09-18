namespace Watchout.Core.Media;

public static class MediaPolicy
{
    public const long CopyLimitBytes = 64L * 1024 * 1024;
    public const long FullProxyLimitBytes = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// Never copy masters into %AppData%. Play from the original path (Resolume-style).
    /// Copying 4K on import is what filled the drive.
    /// </summary>
    public static bool ShouldCopyOnImport(long bytes)
    {
        _ = bytes;
        return false;
    }

    public static bool ShouldBuildFullProxy(long bytes, double width = 0, double height = 0)
    {
        if (bytes >= FullProxyLimitBytes) return false;
        var px = Math.Max(0, width) * Math.Max(0, height);
        if (px >= 3800 * 2100) return false;
        return true;
    }

    /// <summary>
    /// Resolume Alley converts the imported clip to HAP/DXV even at 4K so
    /// Play is a GPU texture blit. WatchMe does the same with HAP Q.
    /// </summary>
    public static bool ShouldBuildHap(long bytes, double width = 0, double height = 0)
    {
        if (bytes >= FullProxyLimitBytes) return false;
        return width >= 2 && height >= 2;
    }

    public static string VideoPreload(long? bytes) =>
        bytes is >= CopyLimitBytes ? "metadata" : "auto";

    public static string FormatBytes(long bytes)
    {
        if (!double.IsFinite(bytes) || bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double n = bytes;
        var i = 0;
        while (n >= 1024 && i < units.Length - 1)
        {
            n /= 1024;
            i++;
        }
        var digits = i >= 3 ? 2 : i >= 2 ? 1 : 0;
        return $"{n.ToString($"F{digits}")} {units[i]}";
    }

    public static string LargeMediaNote(long bytes, bool linked)
    {
        var size = FormatBytes(bytes);
        return linked
            ? $"Linked {size} on disk (not copied) · streams from the original file"
            : size;
    }
}
