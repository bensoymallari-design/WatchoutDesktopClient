using Watchout.Core.Models;

namespace Watchout.Core.Persistence;

public sealed class AppSettings
{
    public bool AutoStartLastShow { get; set; }
    public GpuPreference GpuPreference { get; set; } = GpuPreference.Auto;
    public string WatchFolder { get; set; } = "";
    public bool WatchFolderEnabled { get; set; }
}
