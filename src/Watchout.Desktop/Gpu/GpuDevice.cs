using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Watchout.Core.Gpu;

namespace Watchout.Desktop.Gpu;

sealed class GpuDevice : IDisposable
{
    /// <summary>
    /// Intel UHD has returned E_INVALIDARG when 11.1 is first. Try 11.0 first
    /// (Resolume's D3D11 path), then 11.1, then 10.x.
    /// </summary>
    static readonly FeatureLevel[] Levels =
    [
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0,
    ];

    static readonly FeatureLevel[] Levels11Only = [FeatureLevel.Level_11_0];

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
        if (!OutputViewMath.D3D11CreateNeedsFeatureLevels(Levels.Length))
            throw new InvalidOperationException("D3D11CreateDevice needs an explicit feature-level list");

        var factory = CreateFactory();
        var device = CreateDevice(factory);
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

        return new GpuDevice(device, context, factory, level, manager, mt, hasVpe);
    }

    static IDXGIFactory2 CreateFactory()
    {
        try { return DXGI.CreateDXGIFactory2<IDXGIFactory2>(false); }
        catch { return DXGI.CreateDXGIFactory1<IDXGIFactory2>(); }
    }

    static ID3D11Device CreateDevice(IDXGIFactory2 factory)
    {
        Exception? last = null;
        foreach (var adapter in HardwareAdapters(factory))
        {
            using (adapter)
            {
                foreach (var flags in new[]
                {
                    DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                    DeviceCreationFlags.BgraSupport,
                })
                {
                    try { return CreateOn(adapter, DriverType.Unknown, flags, Levels); }
                    catch (Exception ex) { last = ex; }
                    try { return CreateOn(adapter, DriverType.Unknown, flags, Levels11Only); }
                    catch (Exception ex) { last = ex; }
                }
            }
        }

        foreach (var flags in new[]
        {
            DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            DeviceCreationFlags.BgraSupport,
        })
        {
            try { return D3D11.D3D11CreateDevice(DriverType.Hardware, flags, Levels); }
            catch (Exception ex) { last = ex; }
            try { return D3D11.D3D11CreateDevice(DriverType.Hardware, flags, Levels11Only); }
            catch (Exception ex) { last = ex; }
        }

        try { return D3D11.D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport, Levels); }
        catch (Exception ex)
        {
            throw last ?? ex;
        }
    }

    static ID3D11Device CreateOn(IDXGIAdapter adapter, DriverType type, DeviceCreationFlags flags, FeatureLevel[] levels)
    {
        var hr = D3D11.D3D11CreateDevice(adapter, type, flags, levels, out ID3D11Device? device);
        if (hr.Failure || device is null)
            throw new InvalidOperationException(hr.ToString());
        return device;
    }

    static IEnumerable<IDXGIAdapter> HardwareAdapters(IDXGIFactory2 factory)
    {
        for (uint i = 0; i < 8; i++)
        {
            IDXGIAdapter adapter;
            try { factory.EnumAdapters(i, out adapter); }
            catch { yield break; }
            var name = adapter.Description.Description ?? "";
            if (name.Contains("Microsoft Basic Render", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Software Adapter", StringComparison.OrdinalIgnoreCase))
            {
                adapter.Dispose();
                continue;
            }
            yield return adapter;
        }
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
