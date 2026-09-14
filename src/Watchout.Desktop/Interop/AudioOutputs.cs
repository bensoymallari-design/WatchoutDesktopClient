using System.Runtime.InteropServices;
using Watchout.Core.Media;
using Watchout.Core.Models;

namespace Watchout.Desktop.Interop;

public static class AudioOutputs
{
    const int DeviceStateActive = 0x1;
    const int Render = 0;
    const int RoleConsole = 0;
    const int RoleCommunications = 2;
    const int StgmRead = 0;

    static readonly PROPERTYKEY FriendlyName = new()
    {
        Fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        Pid = 14,
    };

    public static List<AudioDevice> List()
    {
        var endpoints = new List<AudioDevice>();
        string? consoleId = null, consoleName = null, commsId = null, commsName = null;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(Render, RoleConsole, out var console) == 0 && console is not null)
                {
                    consoleId = IdOf(console);
                    consoleName = NameOf(console);
                    Release(console);
                }
                if (enumerator.GetDefaultAudioEndpoint(Render, RoleCommunications, out var comms) == 0 && comms is not null)
                {
                    commsId = IdOf(comms);
                    commsName = NameOf(comms);
                    Release(comms);
                }
                if (enumerator.EnumAudioEndpoints(Render, DeviceStateActive, out var collection) == 0 && collection is not null)
                {
                    collection.GetCount(out var n);
                    for (uint i = 0; i < n; i++)
                    {
                        if (collection.Item(i, out var device) != 0 || device is null) continue;
                        var id = IdOf(device);
                        var name = NameOf(device);
                        if (!string.IsNullOrEmpty(id))
                            endpoints.Add(new AudioDevice
                            {
                                Id = id,
                                Name = string.IsNullOrEmpty(name) ? id : name,
                                NodeId = "local-runner",
                                Channels = 2,
                                Driver = "WASAPI",
                            });
                        Release(device);
                    }
                    Release(collection);
                }
            }
            finally
            {
                Release(enumerator);
            }
        }
        catch
        {
            // WASAPI is Windows-only; Linux tests never call this.
        }
        return AudioAssign.MenuDevices(endpoints, consoleId, consoleName, commsId, commsName);
    }

    static string IdOf(IMMDevice device)
    {
        device.GetId(out var id);
        return id ?? "";
    }

    static string NameOf(IMMDevice device)
    {
        if (device.OpenPropertyStore(StgmRead, out var store) != 0 || store is null) return "";
        try
        {
            var key = FriendlyName;
            if (store.GetValue(ref key, out var value) != 0) return "";
            try
            {
                return value.Vt == 31 && value.Data != IntPtr.Zero
                    ? Marshal.PtrToStringUni(value.Data) ?? ""
                    : "";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Release(store);
        }
    }

    static void Release(object com)
    {
        if (Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387C5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    struct PROPERTYKEY
    {
        public Guid Fmtid;
        public uint Pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort Vt;
        [FieldOffset(8)] public IntPtr Data;
    }

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PROPVARIANT pvar);
}
