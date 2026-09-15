using System.Runtime.InteropServices;
using Watchout.Core.Media;

namespace Watchout.Desktop.Media;

public static class NdiRuntime
{
    public static string? LibraryPath { get; private set; }
    public static string? Version { get; private set; }
    public static string? LastError { get; private set; }

    static readonly object Gate = new();
    static nint _lib;
    static InitializeFn? _initialize;
    static VersionFn? _version;
    static FindCreateFn? _findCreate;
    static FindWaitFn? _findWait;
    static FindGetFn? _findGet;
    static FindDestroyFn? _findDestroy;
    static RecvCreateFn? _recvCreate;
    static RecvCaptureFn? _recvCapture;
    static RecvFreeVideoFn? _recvFreeVideo;
    static RecvDestroyFn? _recvDestroy;
    static bool _ready;

    public static IReadOnlyList<NdiAdvert> FindSources(TimeSpan wait)
    {
        lock (Gate)
        {
            LastError = null;
            if (!EnsureLoaded()) return [];
            var settings = new FindCreate { show_local_sources = true };
            var find = _findCreate!(ref settings);
            if (find == 0)
            {
                LastError = "NDI Runtime loaded but find did not start.";
                return [];
            }
            try
            {
                var ms = (uint)Math.Clamp(wait.TotalMilliseconds, 200, 8_000);
                _findWait!(find, ms);
                var list = ReadSources(find);
                if (list.Count == 0)
                {
                    _findWait!(find, Math.Min(2_000, ms));
                    list = ReadSources(find);
                }
                return list;
            }
            finally
            {
                _findDestroy!(find);
            }
        }
    }

    static List<NdiAdvert> ReadSources(nint find)
    {
        var array = _findGet!(find, out var count);
        var list = new List<NdiAdvert>();
        if (array == 0 || count == 0) return list;
        var size = Marshal.SizeOf<Source>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (uint i = 0; i < count; i++)
        {
            var src = Marshal.PtrToStructure<Source>(array + (nint)(i * (uint)size));
            var name = Marshal.PtrToStringUTF8(src.p_ndi_name);
            if (string.IsNullOrWhiteSpace(name) || !NdiNames.IsAdvertisedSource(name) || !seen.Add(name)) continue;
            var url = Marshal.PtrToStringUTF8(src.p_url_address);
            list.Add(NdiNames.FromRuntime(name, url));
        }
        return list.OrderBy(a => a.Name).ToList();
    }

    static bool EnsureLoaded()
    {
        if (_ready) return true;
        if (_lib != 0) return false;

        var extra = new List<string>();
        try
        {
            var app = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(app)) extra.Add(app);
        }
        catch { /* ignore */ }

        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in NdiRuntimePaths.EnvKeys)
            env[key] = Environment.GetEnvironmentVariable(key);

        foreach (var path in NdiRuntimePaths.Candidates(
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     env,
                     extra))
        {
            if (!File.Exists(path)) continue;
            if (NativeLibrary.TryLoad(path, out _lib))
            {
                LibraryPath = path;
                break;
            }
        }

        if (_lib == 0 && NativeLibrary.TryLoad(NdiRuntimePaths.DllName, out _lib))
            LibraryPath = NdiRuntimePaths.DllName;

        if (_lib == 0)
        {
            LastError = "NDI Runtime was not found (Processing.NDI.Lib.x64.dll). Install NDI Runtime / NDI Tools — the same DLL WatchJhon uses at C:\\Program Files\\NDI\\NDI 6 Runtime\\v6\\.";
            return false;
        }

        try
        {
            _initialize = Get<InitializeFn>("NDIlib_initialize");
            _version = Get<VersionFn>("NDIlib_version");
            _findCreate = Get<FindCreateFn>("NDIlib_find_create_v2");
            _findWait = Get<FindWaitFn>("NDIlib_find_wait_for_sources");
            _findGet = Get<FindGetFn>("NDIlib_find_get_current_sources");
            _findDestroy = Get<FindDestroyFn>("NDIlib_find_destroy");
            _recvCreate = Get<RecvCreateFn>("NDIlib_recv_create_v3");
            _recvCapture = Get<RecvCaptureFn>("NDIlib_recv_capture_v2");
            _recvFreeVideo = Get<RecvFreeVideoFn>("NDIlib_recv_free_video_v2");
            _recvDestroy = Get<RecvDestroyFn>("NDIlib_recv_destroy");
            if (!_initialize())
            {
                LastError = "NDI Runtime loaded but this CPU is not supported.";
                return false;
            }
            if (_version is not null)
            {
                var ptr = _version();
                Version = ptr == 0 ? null : Marshal.PtrToStringUTF8(ptr);
            }
            _ready = true;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    static T Get<T>(string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_lib, name, out var fn) || fn == 0)
            throw new InvalidOperationException($"NDI Runtime is missing {name}.");
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    public static nint CreateReceiver(string sourceName, out nint namePtr)
    {
        namePtr = 0;
        lock (Gate)
        {
            if (!EnsureLoaded() || _recvCreate is null) return 0;
            namePtr = Marshal.StringToCoTaskMemUTF8(sourceName);
            var recvName = Marshal.StringToCoTaskMemUTF8("WatchMe · " + sourceName);
            var create = new RecvCreate
            {
                p_ndi_name = namePtr,
                color_format = LivePicture.NdiColorBgra,
                bandwidth = LivePicture.NdiHighestBandwidth,
                allow_video_fields = false,
                p_ndi_recv_name = recvName,
            };
            var recv = _recvCreate(ref create);
            Marshal.FreeCoTaskMem(recvName);
            if (recv == 0)
            {
                Marshal.FreeCoTaskMem(namePtr);
                namePtr = 0;
                LastError = $"NDI could not connect to {sourceName}.";
            }
            return recv;
        }
    }

    public static int CaptureVideo(nint recv, ref VideoFrame frame, uint timeoutMs) =>
        _recvCapture is null || recv == 0 ? 0 : _recvCapture(recv, ref frame, 0, 0, timeoutMs);

    public static void FreeVideo(nint recv, ref VideoFrame frame)
    {
        if (_recvFreeVideo is null || recv == 0) return;
        _recvFreeVideo(recv, ref frame);
    }

    public static void DestroyReceiver(nint recv, nint namePtr)
    {
        lock (Gate)
        {
            if (recv != 0) _recvDestroy?.Invoke(recv);
            if (namePtr != 0) Marshal.FreeCoTaskMem(namePtr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FindCreate
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool show_local_sources;
        public nint p_groups;
        public nint p_extra_ips;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Source
    {
        public nint p_ndi_name;
        public nint p_url_address;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    struct RecvCreate
    {
        public nint p_ndi_name;
        public nint p_url_address;
        public int color_format;
        public int bandwidth;
        [MarshalAs(UnmanagedType.U1)]
        public bool allow_video_fields;
        public nint p_ndi_recv_name;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct VideoFrame
    {
        public int xres;
        public int yres;
        public int FourCC;
        public int frame_rate_N;
        public int frame_rate_D;
        public float picture_aspect_ratio;
        public int frame_format_type;
        public long timecode;
        public nint p_data;
        public int line_stride_in_bytes;
        public nint p_metadata;
        public long timestamp;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    delegate bool InitializeFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate nint VersionFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate nint FindCreateFn(ref FindCreate settings);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    delegate bool FindWaitFn(nint find, uint timeoutMs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate nint FindGetFn(nint find, out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void FindDestroyFn(nint find);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate nint RecvCreateFn(ref RecvCreate settings);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int RecvCaptureFn(nint recv, ref VideoFrame video, nint audio, nint metadata, uint timeoutMs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void RecvFreeVideoFn(nint recv, ref VideoFrame video);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void RecvDestroyFn(nint recv);
}
