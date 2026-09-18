using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.DXGI;
using Watchout.Core.Machine;

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

    public static double LastCpuPercent { get; private set; }

    public static MachineSample Read()
    {
        var ram = ReadRam();
        var gpu = ReadGpu();
        long process = 0;
        try { process = Process.GetCurrentProcess().WorkingSet64; }
        catch { /* process query optional */ }
        var show = App.Session.Show;
        var load = MachineLoad.CountShow(show, App.Session.LiveOutputs.Count);
        var cpu = ReadCpuPercent();
        LastCpuPercent = cpu;
        return new MachineSample(
            cpu,
            ram.Used,
            ram.Total,
            gpu.Used,
            gpu.Budget,
            process,
            gpu.Name,
            load);
    }

    static TimeSpan _procCpu;
    static DateTime _procAt;
    static bool _procPrimed;

    static double ReadCpuPercent()
    {
        var system = ReadSystemCpu();
        var process = ReadProcessCpu();
        if (system > 0.5) return system;
        return Math.Max(system, process);
    }

    static double ReadSystemCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return 0;
        if (!_cpuPrimed)
        {
            _idle = unchecked((ulong)idle);
            _kernel = unchecked((ulong)kernel);
            _user = unchecked((ulong)user);
            _cpuPrimed = true;
            return 0;
        }
        var idleNow = unchecked((ulong)idle);
        var kernelNow = unchecked((ulong)kernel);
        var userNow = unchecked((ulong)user);
        var idleDelta = idleNow - _idle;
        var kernelDelta = kernelNow - _kernel;
        var userDelta = userNow - _user;
        _idle = idleNow;
        _kernel = kernelNow;
        _user = userNow;
        var sys = kernelDelta + userDelta;
        if (sys == 0) return 0;
        var busy = sys > idleDelta ? sys - idleDelta : 0;
        return Math.Clamp(100.0 * busy / sys, 0, 100);
    }

    static double ReadProcessCpu()
    {
        try
        {
            var proc = Process.GetCurrentProcess();
            var cpu = proc.TotalProcessorTime;
            var now = DateTime.UtcNow;
            if (!_procPrimed)
            {
                _procCpu = cpu;
                _procAt = now;
                _procPrimed = true;
                return 0;
            }
            var elapsed = (now - _procAt).TotalMilliseconds;
            var used = (cpu - _procCpu).TotalMilliseconds;
            _procCpu = cpu;
            _procAt = now;
            if (elapsed < 1) return 0;
            var cores = Math.Max(1, Environment.ProcessorCount);
            return Math.Clamp(100.0 * used / (elapsed * cores), 0, 100);
        }
        catch
        {
            return 0;
        }
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

    static long ToLong(ulong n) => n > long.MaxValue ? long.MaxValue : (long)n;

    static long ToLong(nint n) => n < 0 ? 0 : n;

    static long ToLong(nuint n) => n > (ulong)long.MaxValue ? long.MaxValue : (long)n;

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
    static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
