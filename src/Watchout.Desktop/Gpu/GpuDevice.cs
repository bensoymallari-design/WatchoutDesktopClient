using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Watchout.Core.Gpu;

namespace Watchout.Desktop.Gpu;

sealed class GpuDevice : IDisposable
{
    /// <summary>
    /// Intel UHD has returned E_INVALIDARG when 11.1 is first. Try 11.0 first,
    /// then 11.1, then 10.x.
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
                var ptr = adapter.NativePointer;
                foreach (var flags in new[]
                {
                    DeviceCreationFlags.BgraSupport,
                    DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                    DeviceCreationFlags.None,
                })
                {
                    try { return NativeCreate(ptr, DriverType.Unknown, flags, Levels); }
                    catch (Exception ex) { last = ex; }
                    try { return NativeCreate(ptr, DriverType.Unknown, flags, Levels11Only); }
                    catch (Exception ex) { last = ex; }
                }
            }
        }

        foreach (var flags in new[]
        {
            DeviceCreationFlags.BgraSupport,
            DeviceCreationFlags.None,
            DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
        })
        {
            try { return NativeCreate(IntPtr.Zero, DriverType.Hardware, flags, Levels); }
            catch (Exception ex) { last = ex; }
            try { return NativeCreate(IntPtr.Zero, DriverType.Hardware, flags, Levels11Only); }
            catch (Exception ex) { last = ex; }
        }

        try { return NativeCreate(IntPtr.Zero, DriverType.Warp, DeviceCreationFlags.BgraSupport, Levels); }
        catch (Exception ex)
        {
            throw last ?? ex;
        }
    }

    /// <summary>
    /// Call d3d11.dll D3D11CreateDevice with an explicit feature-level count.
    /// Vortice's params FeatureLevel[] overloads bind the wrong signature and
    /// Intel UHD returns E_INVALIDARG (compositor off, gold Stage, black wall).
    /// </summary>
    static ID3D11Device NativeCreate(IntPtr adapter, DriverType type, DeviceCreationFlags flags, FeatureLevel[] levels)
    {
        var hr = D3D11CreateDeviceNative(
            adapter,
            (int)type,
            IntPtr.Zero,
            (uint)flags,
            levels,
            (uint)levels.Length,
            D3D11.SdkVersion,
            out var devicePtr,
            out _,
            out var contextPtr);
        if (hr < 0 || devicePtr == IntPtr.Zero)
            throw new InvalidOperationException($"D3D11CreateDevice 0x{hr:X8}");
        if (contextPtr != IntPtr.Zero)
            Marshal.Release(contextPtr);
        return new ID3D11Device(devicePtr);
    }

    [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", ExactSpelling = true)]
    static extern int D3D11CreateDeviceNative(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        [In] FeatureLevel[] featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out IntPtr device,
        out FeatureLevel featureLevel,
        out IntPtr context);

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
