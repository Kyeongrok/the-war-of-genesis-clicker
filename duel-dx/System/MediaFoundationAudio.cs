using System.Runtime.InteropServices;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 오디오 파일(MP3 · OGG …)을 Windows Media Foundation(<c>mfplat</c> · <c>mfreadwrite</c>)의 소스 리더로 16비트 PCM 으로 푼다 —
/// 저장소에 MP3 를 푸는 것이 없어서다. 파트1 시디 트랙(<see cref="Part1CdMusic"/>)과, 그것을 우리 WAV 로 바꿔 두는 <c>tools/part1-cd-pack</c> 이 쓴다.
/// </summary>
/// <remarks>
/// 유닉스 이식(user32 흉내)이나 MF 가 빠진 Windows N 에는 DLL · 진입점이 없다 — 부르는 쪽이 예외(<see cref="DllNotFoundException"/> ·
/// <see cref="EntryPointNotFoundException"/> · <see cref="COMException"/> …)를 삼킨다.
/// </remarks>
internal static unsafe class MediaFoundationAudio
{
    private const uint MF_VERSION = 0x00020070, MF_SOURCE_READER_FIRST_AUDIO_STREAM = 0xFFFFFFFD, MF_SOURCE_READER_ALL_STREAMS = 0xFFFFFFFE;
    private const uint MF_SOURCE_READERF_ERROR = 0x1, MF_SOURCE_READERF_ENDOFSTREAM = 0x2;
    private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    private static readonly Guid MFMediaType_Audio = new("73647561-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFAudioFormat_PCM = new("00000001-0000-0010-8000-00aa00389b71");

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint coInit);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out IntPtr mediaType);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr reader);

    /// <summary>COM 개체의 vtable 칸 i.</summary>
    private static IntPtr Slot(IntPtr obj, int i) => (*(IntPtr**)obj)[i];
    private static void Release(IntPtr obj) { if (obj != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(obj, 2))(obj); }
    private static void Check(int hr, string what) { if (hr < 0) throw new COMException(what, hr); }

    // IMFAttributes(IUnknown 3칸 뒤): GetUINT32 7 · SetUINT32 21 · SetGUID 24. IMFSourceReader: SetStreamSelection 4 · GetCurrentMediaType 6 ·
    // SetCurrentMediaType 7 · ReadSample 9. IMFSample(IMFAttributes 30칸 뒤): ConvertToContiguousBuffer 41. IMFMediaBuffer: Lock 3 · Unlock 4.
    private static uint GetUInt32(IntPtr attrs, Guid key)
    {
        uint v;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint*, int>)Slot(attrs, 7))(attrs, &key, &v), "GetUINT32");
        return v;
    }
    private static void SetUInt32(IntPtr attrs, Guid key, uint v) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)Slot(attrs, 21))(attrs, &key, v), "SetUINT32");
    private static void SetGuid(IntPtr attrs, Guid key, Guid v) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Slot(attrs, 24))(attrs, &key, &v), "SetGUID");

    /// <summary>그 파일을 16비트 PCM 으로 — 꼴이 안 맞으면 null, MF 가 없거나 못 풀면 예외.</summary>
    public static PcmSound? Decode(string path)
    {
        bool com = CoInitializeEx(IntPtr.Zero, 0 /* COINIT_MULTITHREADED */) >= 0;
        IntPtr reader = IntPtr.Zero, want = IntPtr.Zero, got = IntPtr.Zero;
        bool started = false;
        try
        {
            Check(MFStartup(MF_VERSION, 0), "MFStartup");
            started = true;
            Check(MFCreateSourceReaderFromURL(Path.GetFullPath(path), IntPtr.Zero, out reader), "MFCreateSourceReaderFromURL");
            var setSelection = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int>)Slot(reader, 4);
            Check(setSelection(reader, MF_SOURCE_READER_ALL_STREAMS, 0), "SetStreamSelection");
            Check(setSelection(reader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, 1), "SetStreamSelection");
            // 16비트 PCM 을 달라고 한다 — 채널 수 · 표본율은 원래 것 그대로(믹서가 표본율을 맞춘다).
            Check(MFCreateMediaType(out want), "MFCreateMediaType");
            SetGuid(want, MF_MT_MAJOR_TYPE, MFMediaType_Audio);
            SetGuid(want, MF_MT_SUBTYPE, MFAudioFormat_PCM);
            SetUInt32(want, MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Slot(reader, 7))(reader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, IntPtr.Zero, want), "SetCurrentMediaType");
            IntPtr cur;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(reader, 6))(reader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, &cur), "GetCurrentMediaType");
            got = cur;
            int channels = (int)GetUInt32(got, MF_MT_AUDIO_NUM_CHANNELS), rate = (int)GetUInt32(got, MF_MT_AUDIO_SAMPLES_PER_SECOND);
            if (GetUInt32(got, MF_MT_AUDIO_BITS_PER_SAMPLE) != 16 || channels is < 1 or > 8 || rate <= 0) return null;

            var readSample = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint*, uint*, long*, IntPtr*, int>)Slot(reader, 9);
            using var pcm = new MemoryStream();
            while (true)
            {
                uint stream, flags;
                long time;
                IntPtr sample = IntPtr.Zero;
                Check(readSample(reader, MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0, &stream, &flags, &time, &sample), "ReadSample");
                try
                {
                    if ((flags & MF_SOURCE_READERF_ERROR) != 0) return null;
                    if (sample != IntPtr.Zero)
                    {
                        IntPtr buffer;
                        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(sample, 41))(sample, &buffer), "ConvertToContiguousBuffer");
                        try
                        {
                            byte* data;
                            uint max, length;
                            Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, &data, &max, &length), "Lock");
                            pcm.Write(new ReadOnlySpan<byte>(data, (int)length));
                            ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(buffer, 4))(buffer);
                        }
                        finally { Release(buffer); }
                    }
                }
                finally { Release(sample); }
                if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
            }
            int frames = (int)(pcm.Length / (2 * channels));
            if (frames == 0) return null;
            var samples = new short[frames * channels];
            Buffer.BlockCopy(pcm.GetBuffer(), 0, samples, 0, samples.Length * 2);
            return new PcmSound(samples, rate, channels);
        }
        finally
        {
            Release(got);
            Release(want);
            Release(reader);
            if (started) MFShutdown();
            if (com) CoUninitialize();
        }
    }
}
