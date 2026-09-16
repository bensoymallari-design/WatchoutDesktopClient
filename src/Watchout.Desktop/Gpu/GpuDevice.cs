using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Watchout.Desktop.Gpu;

sealed class GpuDevice : IDisposable
{
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public IDXGIFactory2 Factory { get; }
    public FeatureLevel Level { get; }

    public (string Name, long Used, long Budget) VideoMemory()
    {
        using var dxgi = Device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgi.GetAdapter();
        var desc = adapter.Description;
        var name = desc.Description ?? "";
        var dedicated = ToLong(desc.DedicatedVideoMemory);
        try
        {
            using var adapter3 = adapter.QueryInterface<IDXGIAdapter3>();
            var info = adapter3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
            var budget = info.Budget > 0 ? ToLong(info.Budget) : dedicated;
            return (name, ToLong(info.CurrentUsage), budget);
        }
        catch
        {
            return (name, 0, dedicated);
        }
    }

    static long ToLong(ulong n) => n > long.MaxValue ? long.MaxValue : (long)n;

    static long ToLong(nint n) => n < 0 ? 0 : n;

    static long ToLong(nuint n) => n > long.MaxValue ? long.MaxValue : (long)n;

    GpuDevice(ID3D11Device device, ID3D11DeviceContext context, IDXGIFactory2 factory, FeatureLevel level)
    {
        Device = device;
        Context = context;
        Factory = factory;
        Level = level;
    }

    public static GpuDevice Create()
    {
        var flags = DeviceCreationFlags.BgraSupport;
        ID3D11Device device;
        try
        {
            device = D3D11.D3D11CreateDevice(DriverType.Hardware, flags);
        }
        catch
        {
            device = D3D11.D3D11CreateDevice(DriverType.Warp, flags);
        }
        var context = device.ImmediateContext;
        var level = device.FeatureLevel;
        try
        {
            var mt = device.QueryInterface<ID3D11Multithread>();
            mt.SetMultithreadProtected(true);
            mt.Dispose();
        }
        catch { /* optional */ }
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgi.GetAdapter();
        using var fac = adapter.GetParent<IDXGIFactory2>();
        var factory = fac.QueryInterface<IDXGIFactory2>();
        return new GpuDevice(device, context, factory, level);
    }

    public void Dispose()
    {
        Context.Dispose();
        Factory.Dispose();
        Device.Dispose();
    }
}
