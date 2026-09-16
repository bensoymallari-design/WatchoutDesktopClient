using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.DXGI;
using Watchout.Core.Machine;
using Watchout.Desktop.Gpu;

namespace Watchout.Desktop.Engine;

/// <summary>
/// Samples Windows CPU, RAM, and DXGI GPU memory for the Producer load strip.
/// </summary>
public static class MachineProbe
{
    static ulong _idle;
    static ulong _kernel;
    static ulong _user;
    static bool _cpuPrimed;

    public static MachineSample Read()
    {
        var ram = ReadRam();
        var gpu = ReadGpu();
        long process = 0;
        try { process = Process.GetCurrentProcess().WorkingSet64; }
        catch { /* process query optional */ }
        var show = App.Session.Show;
        var load = MachineLoad.CountShow(show, App.Session.LiveOutputs.Count);
        return new MachineSample(
            ReadCpuPercent(),
            ram.Used,
            ram.Total,
            gpu.Used,
            gpu.Budget,
            process,
            gpu.Name,
            load);
    }

    static double ReadCpuPercent()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return 0;
        var idle = ToUlong(idleTime);
        var kernel = ToUlong(kernelTime);
        var user = ToUlong(userTime);
        if (!_cpuPrimed)
        {
            _idle = idle;
            _kernel = kernel;
            _user = user;
            _cpuPrimed = true;
            return 0;
        }
        var idleDelta = idle - _idle;
        var kernelDelta = kernel - _kernel;
        var userDelta = user - _user;
        _idle = idle;
        _kernel = kernel;
        _user = user;
        var sys = kernelDelta + userDelta;
        if (sys == 0) return 0;
        var busy = sys > idleDelta ? sys - idleDelta : 0;
        return Math.Clamp(100.0 * busy / sys, 0, 100);
    }

    static (long Used, long Total) ReadRam()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
            return (0, 0);
        var total = ToLong(status.TotalPhys);
        var avail = ToLong(status.AvailPhys);
        return (Math.Max(0, total - avail), total);
    }

    static (string Name, long Used, long Budget) ReadGpu()
    {
        if (GpuEngine.Available)
        {
            var live = GpuEngine.VideoMemory();
            if (live.Budget > 0 || live.Name.Length > 0) return live;
        }
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint i = 0; i < 8; i++)
            {
                IDXGIAdapter adapter;
                try { factory.EnumAdapters(i, out adapter); }
                catch { break; }
                using (adapter)
                {
                    var desc = adapter.Description;
                    var name = desc.Description ?? "";
                    if (IsSoftware(name)) continue;
                    try
                    {
                        using var adapter3 = adapter.QueryInterface<IDXGIAdapter3>();
                        var info = adapter3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
                        var dedicated = ToLong(desc.DedicatedVideoMemory);
                        var budget = info.Budget > 0 ? ToLong(info.Budget) : dedicated;
                        return (name, ToLong(info.CurrentUsage), budget);
                    }
                    catch
                    {
                        return (name, 0, ToLong(desc.DedicatedVideoMemory));
                    }
                }
            }
        }
        catch { /* DXGI optional on this PC */ }
        return ("", 0, 0);
    }

    static bool IsSoftware(string name) =>
        name.Contains("Microsoft Basic Render", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Software Adapter", StringComparison.OrdinalIgnoreCase);

    static ulong ToUlong(FileTime ft) => ((ulong)ft.High << 32) | ft.Low;

    static long ToLong(ulong n) => n > long.MaxValue ? long.MaxValue : (long)n;

    static long ToLong(nint n) => n < 0 ? 0 : n;

    static long ToLong(nuint n) => n > (ulong)long.MaxValue ? long.MaxValue : (long)n;

    [StructLayout(LayoutKind.Sequential)]
    struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
