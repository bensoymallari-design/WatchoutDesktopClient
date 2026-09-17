using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Watchout.Desktop.Gpu;

sealed class GpuDevice : IDisposable
{
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public IDXGIFactory2 Factory { get; }
    public FeatureLevel Level { get; }
    public IMFDXGIDeviceManager? DxgiManager { get; }
    public bool HasVideoProcessor { get; }

    readonly ID3D11Multithread? _mt;

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

    GpuDevice(
        ID3D11Device device,
        ID3D11DeviceContext context,
        IDXGIFactory2 factory,
        FeatureLevel level,
        IMFDXGIDeviceManager? dxgiManager,
        ID3D11Multithread? mt,
        bool hasVideoProcessor)
    {
        Device = device;
        Context = context;
        Factory = factory;
        Level = level;
        DxgiManager = dxgiManager;
        _mt = mt;
        HasVideoProcessor = hasVideoProcessor;
    }

    public void Enter()
    {
        try { _mt?.Enter(); } catch { /* optional */ }
    }

    public void Leave()
    {
        try { _mt?.Leave(); } catch { /* optional */ }
    }

    public static GpuDevice Create()
    {
        var flags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;
        ID3D11Device device;
        try
        {
            device = D3D11.D3D11CreateDevice(DriverType.Hardware, flags);
        }
        catch
        {
            try
            {
                device = D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
            }
            catch
            {
                device = D3D11.D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport);
            }
        }
        var context = device.ImmediateContext;
        var level = device.FeatureLevel;
        ID3D11Multithread? mt = null;
        try
        {
            mt = device.QueryInterface<ID3D11Multithread>();
            mt.SetMultithreadProtected(true);
        }
        catch { mt = null; }

        var hasVpe = false;
        try
        {
            using var video = device.QueryInterface<ID3D11VideoDevice>();
            hasVpe = video is not null;
        }
        catch { hasVpe = false; }

        IMFDXGIDeviceManager? manager = null;
        try
        {
            MfNative.Check(MfNative.MFCreateDXGIDeviceManager(out var token, out manager), "MFCreateDXGIDeviceManager");
            manager.ResetDevice(device.NativePointer, token);
        }
        catch
        {
            if (manager is not null)
            {
                try { MarshalRelease(manager); } catch { /* ignore */ }
                manager = null;
            }
        }

        using var dxgi = device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgi.GetAdapter();
        using var fac = adapter.GetParent<IDXGIFactory2>();
        var factory = fac.QueryInterface<IDXGIFactory2>();
        return new GpuDevice(device, context, factory, level, manager, mt, hasVpe);
    }

    static void MarshalRelease(object com) =>
        System.Runtime.InteropServices.Marshal.ReleaseComObject(com);

    public void Dispose()
    {
        if (DxgiManager is not null)
        {
            try { MarshalRelease(DxgiManager); } catch { /* ignore */ }
        }
        _mt?.Dispose();
        Context.Dispose();
        Factory.Dispose();
        Device.Dispose();
    }
}
