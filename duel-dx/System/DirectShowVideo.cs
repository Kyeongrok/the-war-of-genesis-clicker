using System.Runtime.InteropServices;

namespace DuelDx;

/// <summary>
/// 사용자 폴더의 영상(파트1 엔딩 — MPEG-1 · AVI)을 DirectShow 로 트는 것. 원본(0x100089b0)도 DirectShow 로 틀었다.
/// Media Foundation 은 MPEG-1 영상 풀개가 없어(Windows 10 에서 <c>MFVideoFormat_MPG1</c> → RGB 가 안 된다) 쓰지 않는다.
/// </summary>
/// <remarks>
/// 그래프: 파일 → (분리 · 풀개는 DirectShow 가 고른다) → Sample Grabber(RGB32, 버퍼에 담아 둠) → Null Renderer. 창을 띄우는 영상 렌더러는 안 붙인다.
/// 소리 줄기는 기본 소리 렌더러로 이어 DirectShow 가 직접 낸다 — 그래프 시계가 소리 시계라 영상과 소리가 맞는다.
/// 게임은 틀마다 그래버의 마지막 컷을 베껴 그린다(<see cref="CopyFrame"/>). 주 스레드(STA)에서 만들고 쓴다.
/// 실패하면 <see cref="Open"/> 이 <see cref="COMException"/> 따위를 낸다 — 부르는 쪽이 삼킨다.
/// </remarks>
internal sealed class DirectShowVideo : IDisposable
{
    private static readonly Guid CLSID_FilterGraph = new("e436ebb3-524f-11ce-9f53-0020af0ba770");
    private static readonly Guid CLSID_SampleGrabber = new("c1f400a0-3f08-11d3-9f0b-006008039e37");
    private static readonly Guid CLSID_NullRenderer = new("c1f400a4-3f08-11d3-9f0b-006008039e37");
    private static readonly Guid CLSID_CaptureGraphBuilder2 = new("bf87b6e1-8c27-11d0-b3f0-00aa003761c5");
    private static readonly Guid MEDIATYPE_Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MEDIATYPE_Audio = new("73647561-0000-0010-8000-00aa00389b71");
    private static readonly Guid MEDIASUBTYPE_RGB32 = new("e436eb7e-524f-11ce-9f53-0020af0ba770");
    private static readonly Guid FORMAT_VideoInfo = new("05589f80-c356-11ce-bf01-00aa0055595a");

    [StructLayout(LayoutKind.Sequential)]
    private struct AmMediaType
    {
        public Guid MajorType, SubType;
        public int FixedSizeSamples, TemporalCompression;
        public uint SampleSize;
        public Guid FormatType;
        public IntPtr Unk;
        public uint FormatSize;
        public IntPtr Format;
    }

    [ComImport, Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBaseFilter { }

    [ComImport, Guid("56a868a9-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphBuilder
    {
        [PreserveSig] int AddFilter(IBaseFilter filter, [MarshalAs(UnmanagedType.LPWStr)] string? name);
        [PreserveSig] int RemoveFilter(IBaseFilter filter);
        [PreserveSig] int EnumFilters(out IEnumFilters filters);
        [PreserveSig] int FindFilterByName([MarshalAs(UnmanagedType.LPWStr)] string name, out IBaseFilter filter);
        [PreserveSig] int ConnectDirect(IntPtr pinOut, IntPtr pinIn, IntPtr mt);
        [PreserveSig] int Reconnect(IntPtr pin);
        [PreserveSig] int Disconnect(IntPtr pin);
        [PreserveSig] int SetDefaultSyncSource();
        [PreserveSig] int Connect(IntPtr pinOut, IntPtr pinIn);
        [PreserveSig] int Render(IntPtr pinOut);
        [PreserveSig] int RenderFile([MarshalAs(UnmanagedType.LPWStr)] string file, IntPtr playList);
        [PreserveSig] int AddSourceFilter([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.LPWStr)] string name, out IBaseFilter filter);
    }

    [ComImport, Guid("56a86893-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumFilters
    {
        [PreserveSig] int Next(int count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IBaseFilter[] filters, IntPtr fetched);
    }

    [ComImport, Guid("93e5a4e0-2d50-11d2-abfa-00a0c9c6e38d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICaptureGraphBuilder2
    {
        [PreserveSig] int SetFiltergraph(IGraphBuilder graph);
        [PreserveSig] int GetFiltergraph(out IGraphBuilder graph);
        [PreserveSig] int SetOutputFileName(IntPtr type, IntPtr file, IntPtr mux, IntPtr sink);
        [PreserveSig] int FindInterface(IntPtr category, IntPtr type, IBaseFilter filter, IntPtr iid, out IntPtr obj);
        [PreserveSig] int RenderStream(IntPtr category, IntPtr type, [MarshalAs(UnmanagedType.IUnknown)] object source, IBaseFilter? intermediate, IBaseFilter? sink);
    }

    [ComImport, Guid("6b652fff-11fe-4fce-92ad-0266b5d7c78f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISampleGrabber
    {
        [PreserveSig] int SetOneShot(int oneShot);
        [PreserveSig] int SetMediaType(ref AmMediaType type);
        [PreserveSig] int GetConnectedMediaType(out AmMediaType type);
        [PreserveSig] int SetBufferSamples(int buffer);
        [PreserveSig] int GetCurrentBuffer(ref int size, IntPtr buffer);
    }

    [ComImport, Guid("56a868b1-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IMediaControl
    {
        [PreserveSig] int Run();
        [PreserveSig] int Pause();
        [PreserveSig] int Stop();
    }

    [ComImport, Guid("56a868b6-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IMediaEvent
    {
        [PreserveSig] int GetEventHandle(out IntPtr handle);
        [PreserveSig] int GetEvent(out int code, out IntPtr p1, out IntPtr p2, int timeout);
        [PreserveSig] int WaitForCompletion(int timeout, out int code);
    }

    [ComImport, Guid("56a868b3-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IBasicAudio
    {
        [PreserveSig] int put_Volume(int volume);
    }

    private IGraphBuilder? _graph;
    private readonly ISampleGrabber _grabber;
    private readonly int _bufferSize;
    private readonly uint[] _buffer;

    public int Width { get; }
    public int Height { get; }

    private DirectShowVideo(IGraphBuilder graph, ISampleGrabber grabber, int width, int height)
    {
        _graph = graph;
        _grabber = grabber;
        Width = width;
        Height = height;
        _bufferSize = width * height * 4;
        _buffer = new uint[width * height];
    }

    private static T Create<T>(Guid clsid) => (T)Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, throwOnError: true)!)!;

    private static void Check(int hr, string what) { if (hr < 0) throw new COMException(what, hr); }

    /// <summary>
    /// 그 파일로 그래프를 세우고 틀기 시작한다. <paramref name="volume"/> 0~1(0 이면 소리 끔). 못 세우면 예외.
    /// </summary>
    public static DirectShowVideo Open(string path, float volume)
    {
        var graph = Create<IGraphBuilder>(CLSID_FilterGraph);
        try
        {
            var grabberFilter = Create<IBaseFilter>(CLSID_SampleGrabber);
            var grabber = (ISampleGrabber)grabberFilter;
            var want = new AmMediaType { MajorType = MEDIATYPE_Video, SubType = MEDIASUBTYPE_RGB32, FormatType = FORMAT_VideoInfo };
            Check(grabber.SetMediaType(ref want), "SetMediaType");
            Check(grabber.SetBufferSamples(1), "SetBufferSamples");
            Check(graph.AddFilter(grabberFilter, "Grabber"), "AddFilter grabber");
            var sink = Create<IBaseFilter>(CLSID_NullRenderer);
            Check(graph.AddFilter(sink, "Null"), "AddFilter null");
            Check(graph.AddSourceFilter(Path.GetFullPath(path), "Source", out var source), "AddSourceFilter");

            var builder = Create<ICaptureGraphBuilder2>(CLSID_CaptureGraphBuilder2);
            Check(builder.SetFiltergraph(graph), "SetFiltergraph");
            Check(builder.RenderStream(IntPtr.Zero, IntPtr.Zero, source, grabberFilter, sink), "RenderStream video");
            // 소리 — 분리기(들어온 뒤 생긴 필터)의 소리 출력을 기본 소리 렌더러로. 없거나 못 이으면 소리 없이.
            if (volume > 0)
            {
                Check(graph.EnumFilters(out var each), "EnumFilters");
                var filters = new List<IBaseFilter>();
                var one = new IBaseFilter[1];
                while (each.Next(1, one, IntPtr.Zero) == 0) filters.Add(one[0]);
                var audio = MEDIATYPE_Audio;
                unsafe
                {
                    foreach (var f in filters)
                        if (builder.RenderStream(IntPtr.Zero, (IntPtr)(&audio), f, null, null) >= 0) break;
                }
            }

            Check(grabber.GetConnectedMediaType(out var got), "GetConnectedMediaType");
            int width, height;
            try
            {
                if (got.FormatType != FORMAT_VideoInfo || got.Format == IntPtr.Zero || got.FormatSize < 88) throw new InvalidDataException(path);
                width = Marshal.ReadInt32(got.Format, 52);
                height = Math.Abs(Marshal.ReadInt32(got.Format, 56));
            }
            finally
            {
                if (got.Format != IntPtr.Zero) Marshal.FreeCoTaskMem(got.Format);
                if (got.Unk != IntPtr.Zero) Marshal.Release(got.Unk);
            }
            if (width is <= 0 or > 4096 || height is <= 0 or > 4096) throw new InvalidDataException(path);

            if (graph is IBasicAudio basic)
                basic.put_Volume(volume <= 0 ? -10000 : (int)Math.Clamp(2000 * Math.Log10(volume), -10000, 0));   // 1/100 dB
            Check(((IMediaControl)graph).Run(), "Run");
            return new DirectShowVideo(graph, grabber, width, height);
        }
        catch
        {
            try { ((IMediaControl)graph).Stop(); } catch (COMException) { }
            Marshal.ReleaseComObject(graph);
            throw;
        }
    }

    /// <summary>영상이 끝났나(그래프가 EC_COMPLETE 를 냈나).</summary>
    public bool Finished => _graph is not { } g || ((IMediaEvent)g).WaitForCompletion(0, out _) == 0;

    /// <summary>그래버의 마지막 컷을 <paramref name="frame"/>(너비 × 높이, 0x00RRGGBB, 위가 첫 줄)에 베낀다 — 아직 컷이 없으면 false.</summary>
    public unsafe bool CopyFrame(uint[] frame)
    {
        if (_graph == null || frame.Length < Width * Height) return false;
        var buffer = _buffer;
        int size = _bufferSize;
        fixed (uint* p = buffer)
            if (_grabber.GetCurrentBuffer(ref size, (IntPtr)p) < 0 || size < _bufferSize) return false;
        // DIB 는 아래 줄부터 담긴다.
        for (int y = 0; y < Height; y++)
            buffer.AsSpan((Height - 1 - y) * Width, Width).CopyTo(frame.AsSpan(y * Width, Width));
        return true;
    }

    public void Dispose()
    {
        if (_graph is not { } g) return;
        _graph = null;
        try { ((IMediaControl)g).Stop(); } catch (COMException) { }
        Marshal.ReleaseComObject(g);
    }
}
