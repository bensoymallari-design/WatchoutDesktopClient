using System.Runtime.InteropServices;

namespace Watchout.Desktop.Gpu;

static class MfNative
{
    public const int MfVersion = (2 << 16) | 0x70;
    public const int VideoStream = unchecked((int)0xFFFFFFFC);
    public const int AudioStream = unchecked((int)0xFFFFFFFD);
    public const int AllStreams = unchecked((int)0xFFFFFFFE);
    public const int EndOfStream = 0x2;
    public const int CurrentTypeChanged = 0x10;
    public const int VtI8 = 20;
    public const int VtEmpty = 0;

    public static readonly Guid MfMediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid MfMediaTypeAudio = new("73647561-0000-0010-8000-00AA00389B71");
    public static readonly Guid MfVideoFormatRgb32 = new("00000016-0000-0010-8000-00AA00389B71");
    public static readonly Guid MfVideoFormatNv12 = new("3231564E-0000-0010-8000-00AA00389B71");
    public static readonly Guid MfAudioFormatPcm = new("00000001-0000-0010-8000-00AA00389B71");
    public static readonly Guid Id3d11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    public static readonly Guid MfMtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MfMtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MfMtFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MfMtAudioNumChannels = new("37e48bf5-645e-4c5b-8900-e154cf917e46");
    public static readonly Guid MfMtAudioSamplesPerSecond = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    public static readonly Guid MfMtAudioBitsPerSample = new("f2deb57b-0dd6-4c74-b611-601d91c8f6a7");
    public static readonly Guid MfMtAudioBlockAlignment = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    public static readonly Guid MfMtAudioAvgBytesPerSecond = new("1aab75c8-cf7d-4395-bd0d-195e6ca148a1");
    public static readonly Guid MfPdDuration = new("6d7226c9-0a18-4bd4-b4c7-0add5609750a");
    public static readonly Guid MfSourceReaderEnableVideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d8d854");
    public static readonly Guid MfReadwriteEnableHardwareTransforms = new("a634a91c-822b-41b2-bb68-12f5625a73ae");
    public static readonly Guid MfSourceReaderD3DManager = new("ec822da2-e1e9-4b29-ae0b-84663d18ae42");

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateAttributes(out IMFAttributes attrs, uint size);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMediaType(out IMFMediaType type);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int MFCreateSourceReaderFromURL(string url, IMFAttributes? attrs, out IMFSourceReader reader);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateDXGIDeviceManager(out uint resetToken, out IMFDXGIDeviceManager manager);

    public static void Check(int hr, string what)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr, new IntPtr(-1));
        _ = what;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public long hVal;
    }
}

[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFAttributes
{
    void GetItem(in Guid guidKey, IntPtr pValue);
    void GetItemType(in Guid guidKey, out int pType);
    void CompareItem(in Guid guidKey, IntPtr value, out int pbResult);
    void Compare(IMFAttributes other, int match, out int pbResult);
    void GetUINT32(in Guid guidKey, out uint punValue);
    void GetUINT64(in Guid guidKey, out ulong punValue);
    void GetDouble(in Guid guidKey, out double pfValue);
    void GetGUID(in Guid guidKey, out Guid pguidValue);
    void GetStringLength(in Guid guidKey, out uint pcchLength);
    void GetString(in Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
    void GetAllocatedString(in Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
    void GetBlobSize(in Guid guidKey, out uint pcbBlobSize);
    void GetBlob(in Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
    void GetAllocatedBlob(in Guid guidKey, out IntPtr ip, out uint pcbSize);
    void GetUnknown(in Guid guidKey, in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    void SetItem(in Guid guidKey, IntPtr value);
    void DeleteItem(in Guid guidKey);
    void DeleteAllItems();
    void SetUINT32(in Guid guidKey, uint unValue);
    void SetUINT64(in Guid guidKey, ulong unValue);
    void SetDouble(in Guid guidKey, double fValue);
    void SetGUID(in Guid guidKey, in Guid guidValue);
    void SetString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
    void SetBlob(in Guid guidKey, IntPtr pBuf, uint cbBufSize);
    void SetUnknown(in Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object? pUnknown);
    void LockStore();
    void UnlockStore();
    void GetCount(out uint pcItems);
    void GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
    void CopyAllItems(IMFAttributes pDest);
}

[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaType : IMFAttributes
{
    void GetMajorType(out Guid pguidMajorType);
    void IsCompressedFormat(out int pfCompressed);
    void IsEqual(IMFMediaType pIMediaType, out int pdwFlags);
    void GetRepresentation(Guid guidRepresentation, out IntPtr ppvRepresentation);
    void FreeRepresentation(Guid guidRepresentation, IntPtr pvRepresentation);
}

[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSample : IMFAttributes
{
    void GetSampleFlags(out uint pdwSampleFlags);
    void SetSampleFlags(uint dwSampleFlags);
    void GetSampleTime(out long phnsSampleTime);
    void SetSampleTime(long hnsSampleTime);
    void GetSampleDuration(out long phnsSampleDuration);
    void SetSampleDuration(long hnsSampleDuration);
    void GetBufferCount(out uint pdwBufferCount);
    void GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
    void ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
    void AddBuffer(IMFMediaBuffer pBuffer);
    void RemoveBufferByIndex(uint dwIndex);
    void RemoveAllBuffers();
    void GetTotalLength(out uint pcbTotalLength);
    void CopyToBuffer(IMFMediaBuffer pBuffer);
}

[ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFMediaBuffer
{
    void Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
    void Unlock();
    void GetCurrentLength(out int pcbCurrentLength);
    void SetCurrentLength(int cbCurrentLength);
    void GetMaxLength(out int pcbMaxLength);
}

[ComImport, Guid("70ae66f2-c809-4e4f-8915-277a51b5d99a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSourceReader
{
    void GetStreamSelection(int dwStreamIndex, out int pfSelected);
    void SetStreamSelection(int dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] bool fSelected);
    void GetNativeMediaType(int dwStreamIndex, int dwMediaTypeIndex, out IMFMediaType ppMediaType);
    void GetCurrentMediaType(int dwStreamIndex, out IMFMediaType ppMediaType);
    void SetCurrentMediaType(int dwStreamIndex, IntPtr pdwReserved, IMFMediaType pMediaType);
    void SetCurrentPosition(in Guid guidTimeFormat, in MfNative.PropVariant varPosition);
    void ReadSample(int dwStreamIndex, int dwControlFlags, out int pdwActualStreamIndex, out int pdwStreamFlags, out long pllTimestamp, out IMFSample? ppSample);
    void Flush(int dwStreamIndex);
    void GetServiceForStream(int dwStreamIndex, in Guid guidService, in Guid riid, out IntPtr ppvObject);
    void GetPresentationAttribute(int dwStreamIndex, in Guid guidAttribute, out MfNative.PropVariant pvarAttribute);
}

[ComImport, Guid("eb533e1c-f10b-4332-b56a-c163649af9ac"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFDXGIDeviceManager
{
    void ResetDevice(IntPtr pUnkDevice, uint resetToken);
    void OpenDeviceHandle(out IntPtr phDevice);
    void CloseDeviceHandle(IntPtr hDevice);
    void TestDevice(IntPtr hDevice);
    void LockDevice(IntPtr hDevice, in Guid riid, out IntPtr ppUnk, [MarshalAs(UnmanagedType.Bool)] bool fBlock);
    void UnlockDevice(IntPtr hDevice, [MarshalAs(UnmanagedType.Bool)] bool fSaveState);
    void GetVideoService(IntPtr hDevice, in Guid riid, out IntPtr ppService);
}

[ComImport, Guid("e7174cfa-1c9e-48b1-8866-626226bfc258"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFDXGIBuffer
{
    void GetResource(in Guid riid, out IntPtr ppvObject);
    void GetSubresourceIndex(out uint puSubresourceIndex);
    void SetUnknown(in Guid guid, [MarshalAs(UnmanagedType.IUnknown)] object? pUnkData);
    void GetUnknown(in Guid guid, in Guid riid, out IntPtr ppvObject);
}
