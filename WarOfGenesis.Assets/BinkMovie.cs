using Microsoft.Win32.SafeHandles;

namespace WarOfGenesis.Assets;

/// <summary>
/// Bink 1 파일 하나(<c>Mov\NNNN.mov</c>) — 머리와 프레임 표만 읽어 두고, 프레임은 필요할 때 파일에서 읽는다.
/// 원본 묶음(<c>Mov00.pak</c>) 안의 한 토막도 그대로 연다(<paramref name="offset"/>·길이). 원본 폴더에는 쓰지 않는다.
/// </summary>
/// <remarks>
/// 틀은 <see cref="BinkAudio"/> 설명과 같다(FFmpeg <c>libavformat/bink.c</c>). 프레임마다 소리 갈래 차례로 u32 길이 + 소리 묶음이 오고
/// 나머지가 영상 묶음이다(<see cref="BinkVideoDecoder"/>). 읽기는 자리 지정 읽기(<see cref="RandomAccess"/>)라 여러 실에서 같이 읽어도 된다.
/// </remarks>
public sealed class BinkMovie : IDisposable
{
    private const ushort FlagStereo = 0x2000;
    private const ushort FlagUseDct = 0x1000;

    public BinkInfo Info { get; }
    public int Width { get; }
    public int Height { get; }
    public int AudioTracks { get; }
    public double Fps => Info.FpsDenominator == 0 ? 15 : (double)Info.FpsNumerator / Info.FpsDenominator;
    public bool HasAudio => AudioTracks > 0 && Info.SampleRate > 0;

    private readonly SafeFileHandle _file;
    private readonly long _base;
    private readonly long[] _pos;   // 프레임 수+1 — 파일(토막) 안 자리, 마지막은 끝

    private BinkMovie(SafeFileHandle file, long @base, BinkInfo info, int width, int height, int tracks, long[] pos)
    {
        _file = file; _base = @base; Info = info; Width = width; Height = height; AudioTracks = tracks; _pos = pos;
    }

    /// <summary>파일(또는 묶음 안 토막)을 연다. Bink 가 아니면 <see cref="InvalidDataException"/>.</summary>
    public static BinkMovie Open(string path, long offset = 0, long length = -1)
    {
        var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (length < 0) length = RandomAccess.GetLength(file) - offset;
            var head = new byte[0x40];
            if (RandomAccess.Read(file, head, offset) < 0x30 || head[0] != 'B' || head[1] != 'I' || head[2] != 'K')
                throw new InvalidDataException("Bink 파일이 아닙니다.");
            char revision = (char)head[3];
            int U32(byte[] d, int o) => (int)BitConverter.ToUInt32(d, o);
            long fileSize = Math.Min(BitConverter.ToUInt32(head, 4) + 8L, length);
            int frames = U32(head, 8), width = U32(head, 0x14), height = U32(head, 0x18);
            int fpsNum = U32(head, 0x1c), fpsDen = U32(head, 0x20), tracks = U32(head, 0x28);
            if (frames is < 1 or > 1_000_000 || width is < 1 or > 4096 || height is < 1 or > 4096 || tracks is < 0 or > 256)
                throw new InvalidDataException("Bink 머리가 이상합니다.");
            int o = 0x2c + (revision == 'k' ? 4 : 0);
            var meta = new byte[12 * tracks];
            if (tracks > 0 && RandomAccess.Read(file, meta, offset + o) < meta.Length) throw new InvalidDataException("Bink 머리가 잘렸습니다.");
            int rate = 0, channels = 0;
            bool dct = false;
            if (tracks > 0)
            {
                rate = BitConverter.ToUInt16(meta, 4 * tracks);
                ushort flags = BitConverter.ToUInt16(meta, 4 * tracks + 2);
                channels = (flags & FlagStereo) != 0 ? 2 : 1;
                dct = (flags & FlagUseDct) != 0;
            }
            long table = o + 12L * tracks;
            var raw = new byte[4 * frames];
            if (RandomAccess.Read(file, raw, offset + table) < raw.Length) throw new InvalidDataException("Bink 프레임 표가 잘렸습니다.");
            var pos = new long[frames + 1];
            for (int i = 0; i < frames; i++) pos[i] = BitConverter.ToUInt32(raw, 4 * i) & ~1u;
            pos[frames] = fileSize;
            for (int i = 0; i < frames; i++)
                if (pos[i] > pos[i + 1] || pos[i] < table) throw new InvalidDataException("Bink 프레임 표가 이상합니다.");
            var info = new BinkInfo(revision, frames, fpsNum, fpsDen, rate, channels, dct);
            return new BinkMovie(file, offset, info, width, height, tracks, pos);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public int FrameCount => Info.FrameCount;

    /// <summary>프레임 하나를 통째로 읽는다(소리 + 영상). <paramref name="buffer"/> 는 모자라면 새로 잡는다. 읽은 바이트 수를 낸다.</summary>
    public int ReadFrame(int index, ref byte[] buffer)
    {
        int size = (int)(_pos[index + 1] - _pos[index]);
        if (buffer.Length < size) buffer = new byte[Math.Max(size, buffer.Length * 2)];
        return RandomAccess.Read(_file, buffer.AsSpan(0, size), _base + _pos[index]);
    }

    /// <summary>읽은 프레임에서 첫 소리 묶음과 영상 묶음 자리를 가른다. 소리가 없으면 소리 길이 0.</summary>
    public void Split(ReadOnlySpan<byte> frame, out Range audio, out Range video)
    {
        int p = 0;
        audio = 0..0;
        for (int t = 0; t < AudioTracks; t++)
        {
            if (p + 4 > frame.Length) { video = frame.Length..frame.Length; return; }
            int size = (int)BitConverter.ToUInt32(frame.Slice(p, 4));
            if (size < 0 || p + 4 + size > frame.Length) { video = frame.Length..frame.Length; return; }
            if (t == 0 && size >= 4) audio = (p + 4)..(p + 4 + size);
            p += 4 + size;
        }
        video = p..frame.Length;
    }

    /// <summary>프레임의 첫 소리 묶음만 읽는다(영상은 안 읽는다). 소리 묶음 길이를 낸다 — 없으면 0.</summary>
    public int ReadAudio(int index, ref byte[] buffer)
    {
        long start = _base + _pos[index], room = _pos[index + 1] - _pos[index];
        Span<byte> head = stackalloc byte[4];
        if (AudioTracks == 0 || room < 8 || RandomAccess.Read(_file, head, start) < 4) return 0;
        uint size = BitConverter.ToUInt32(head);
        if (size < 4 || size + 4 > room) return 0;
        if (buffer.Length < size) buffer = new byte[Math.Max((int)size, buffer.Length * 2)];
        return RandomAccess.Read(_file, buffer.AsSpan(0, (int)size), start + 4);
    }

    /// <summary>소리 묶음마다 적힌 풀린 크기(바이트)의 합 ÷ 2 — 소리 표본 수(채널 합).</summary>
    public long CountAudioSamples()
    {
        if (!HasAudio) return 0;
        long total = 0;
        Span<byte> head = stackalloc byte[8];
        for (int i = 0; i < FrameCount; i++)
        {
            if (_pos[i + 1] - _pos[i] < 8 || RandomAccess.Read(_file, head, _base + _pos[i]) < 8) continue;
            uint size = BitConverter.ToUInt32(head);
            if (size >= 4 && size + 4 <= _pos[i + 1] - _pos[i]) total += BitConverter.ToUInt32(head[4..]) / 2;
        }
        return total - total % Math.Max(1, Info.Channels);
    }

    public void Dispose() => _file.Dispose();
}
