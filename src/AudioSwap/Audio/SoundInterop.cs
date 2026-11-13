using System.Runtime.InteropServices;

namespace AudioSwap.Audio;

// WASAPI (playing to a chosen endpoint) and the Media Foundation source reader (decoding
// files). As in CoreAudioInterop, unused members are vtable placeholders and must stay in order.

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatEx
{
    public ushort FormatTag, Channels;
    public uint SamplesPerSec, AvgBytesPerSec;
    public ushort BlockAlign, BitsPerSample, ExtraSize;

    public static WaveFormatEx StereoFloat(int sampleRate) => new()
    {
        FormatTag = 3, // WAVE_FORMAT_IEEE_FLOAT
        Channels = 2,
        SamplesPerSec = (uint)sampleRate,
        AvgBytesPerSec = (uint)sampleRate * 8,
        BlockAlign = 8,
        BitsPerSample = 32,
    };
}

[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, ref WaveFormatEx format, IntPtr sessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    void GetStreamLatency();
    [PreserveSig] int GetCurrentPadding(out uint frames);
    void IsFormatSupported();
    void GetMixFormat();
    void GetDevicePeriod();
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    void Reset();
    void SetEventHandle();
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioRenderClient
{
    [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
    [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
}

[ComImport, Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSourceReader
{
    void GetStreamSelection();
    [PreserveSig] int SetStreamSelection(uint streamIndex, [MarshalAs(UnmanagedType.Bool)] bool selected);
    void GetNativeMediaType();
    [PreserveSig] int GetCurrentMediaType(uint streamIndex, out IMFMediaType type);
    [PreserveSig] int SetCurrentMediaType(uint streamIndex, IntPtr reserved, IMFMediaType type);
    void SetCurrentPosition();
    [PreserveSig] int ReadSample(uint streamIndex, uint controlFlags, out uint actualStreamIndex, out uint streamFlags, out long timestamp, out IMFSample? sample);
}

[ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaType
{
    // IMFAttributes, up to the members used.
    void GetItem(); void GetItemType(); void CompareItem(); void Compare();
    [PreserveSig] int GetUINT32(ref Guid key, out uint value);
    void GetUINT64(); void GetDouble(); void GetGUID(); void GetStringLength(); void GetString();
    void GetAllocatedString(); void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown();
    void SetItem(); void DeleteItem(); void DeleteAllItems(); void SetUINT32(); void SetUINT64(); void SetDouble();
    [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
}

[ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSample
{
    // IMFAttributes (30 members).
    void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32();
    void GetUINT64(); void GetDouble(); void GetGUID(); void GetStringLength(); void GetString();
    void GetAllocatedString(); void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown();
    void SetItem(); void DeleteItem(); void DeleteAllItems(); void SetUINT32(); void SetUINT64();
    void SetDouble(); void SetGUID(); void SetString(); void SetBlob(); void SetUnknown();
    void LockStore(); void UnlockStore(); void GetCount(); void GetItemByIndex(); void CopyAllItems();
    // IMFSample
    void GetSampleFlags(); void SetSampleFlags(); void GetSampleTime(); void SetSampleTime();
    void GetSampleDuration(); void SetSampleDuration(); void GetBufferCount(); void GetBufferByIndex();
    [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
}

[ComImport, Guid("045FA593-8799-42B8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaBuffer
{
    [PreserveSig] int Lock(out IntPtr data, out int maxLength, out int currentLength);
    [PreserveSig] int Unlock();
}

internal static class MediaFoundation
{
    public const uint ApiVersion = 0x00020070, StartupLite = 1;

    [DllImport("mfplat.dll")] public static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] public static extern int MFShutdown();
    [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType type);

    [DllImport("mfreadwrite.dll")]
    public static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr attributes, out IMFSourceReader reader);
}
