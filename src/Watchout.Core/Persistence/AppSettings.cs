using Watchout.Core.Models;

namespace Watchout.Core.Persistence;

public sealed class AppSettings
{
    public bool AutoStartLastShow { get; set; }
    public GpuPreference GpuPreference { get; set; } = GpuPreference.Auto;
    public string WatchFolder { get; set; } = "";
    public bool WatchFolderEnabled { get; set; }
    public bool AccessEnabled { get; set; }
    public string AccessPin { get; set; } = "";
    public AccessRole AccessRole { get; set; } = AccessRole.Producer;
    public string NmosRegistry { get; set; } = "";
}
