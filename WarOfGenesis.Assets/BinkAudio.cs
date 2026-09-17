using System.IO;
using System.Numerics;

namespace WarOfGenesis.Assets;

/// <summary>풀어 낸 소리 — 16비트 PCM(여러 채널이면 채널끼리 번갈아 든 꼴).</summary>
public sealed record PcmSound(short[] Samples, int SampleRate, int Channels)
{
    public int FrameCount => Samples.Length / Channels;
    public TimeSpan Duration => TimeSpan.FromSeconds((double)FrameCount / SampleRate);

    /// <summary>44바이트 머리를 붙인 WAV(PCM 16비트) 바이트.</summary>
    public byte[] ToWav()
    {
        int dataBytes = Samples.Length * 2;
        using var ms = new MemoryStream(44 + dataBytes);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)Channels);
        w.Write(SampleRate); w.Write(SampleRate * Channels * 2); w.Write((short)(Channels * 2)); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        w.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(Samples.AsSpan()));
        w.Flush();
        return ms.ToArray();
    }
}

/// <summary>Bink 파일 머리에서 읽은 것 — 목록을 채울 때는 이것만 읽고 소리는 안 푼다.</summary>
public sealed record BinkInfo(char Revision, int FrameCount, int FpsNumerator, int FpsDenominator,
                              int SampleRate, int Channels, bool UseDct)
{
    public TimeSpan Duration => FpsNumerator == 0 ? TimeSpan.Zero
        : TimeSpan.FromSeconds((double)FrameCount * FpsDenominator / FpsNumerator);
}

/// <summary>
/// <c>BGM/*.bgm</c> — RAD Game Tools 의 Bink 1 파일(<c>BIKi</c>)에서 소리 갈래를 풀어 16비트 PCM 으로 낸다.
/// 게임은 binkw32.dll 로 틀지만, 편집기는 단일 exe 라 네이티브 없이 C# 으로 푼다.
/// </summary>
/// <remarks>
/// <para>
/// 틀 읽기는 FFmpeg <c>libavformat/bink.c</c>, 소리 풀기는 <c>libavcodec/binkaudio.c</c>(RDCT 갈래)를 옮겼다.
/// 변환(RDFT)은 FFmpeg 이 내는 값과 블록마다 맞대어 식을 확정했다 —
/// <c>out[n] = x0/2 + (x1/2)(-1)^n + Σ_{m≥1} x[2m]·cos(2πmn/N) + x[2m+1]·sin(2πmn/N)</c>.
/// 분석은 옵시디안 분석-사운드 노트.
/// </para>
/// <para>
/// 파일 틀: 머리 <c>BIKi</c> · u32 (파일 크기-8) · u32 프레임 수 · u32 가장 큰 프레임 · u32 (프레임 수) ·
/// u32 너비 · u32 높이 · u32 fps 분자 · u32 fps 분모 · u32 영상 플래그 · u32 소리 갈래 수(k) ·
/// k×u32 최대 풀린 크기 · k×(u16 표본률, u16 플래그) · k×u32 갈래 번호 · (프레임 수+1)×u32 프레임 위치(최하 비트 = 키프레임).
/// 프레임마다 갈래 차례로 u32 소리 길이 + 소리 묶음(앞 u32 = 풀린 바이트 수)이 오고 나머지가 영상이다.
/// </para>
/// </remarks>
public sealed class BinkAudio
{
    private const ushort FlagStereo = 0x2000;
    private const ushort FlagUseDct = 0x1000;

    /// <summary>WMA 의 임계 대역 경계(Hz) — Bink 소리가 대역(양자화 단위)을 나눌 때 그대로 쓴다.</summary>
    private static readonly int[] CriticalFreqs =
    [
        100, 200, 300, 400, 510, 630, 770, 920, 1080, 1270, 1480, 1720, 2000, 2320, 2700, 3150, 3700, 4400,
        5300, 6400, 7700, 9500, 12000, 15500, 24500,
    ];

    private static readonly int[] RleLengths = [2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 32, 64];

    public BinkInfo Info { get; }

    private readonly byte[] _data;
    private readonly int _audioTracks;
    private readonly int[] _framePos;   // 프레임 수+1 개 — 마지막은 파일 끝

    private BinkAudio(byte[] data, BinkInfo info, int audioTracks, int[] framePos)
    {
        _data = data; Info = info; _audioTracks = audioTracks; _framePos = framePos;
    }

    /// <summary>파일 앞부분(0x40 바이트면 넉넉하다)만으로 길이·표본률·채널을 읽는다. Bink 가 아니거나 소리가 없으면 null.</summary>
    public static BinkInfo? ReadInfo(ReadOnlySpan<byte> head) => ParseHeader(head, out _, out _);

    public static BinkInfo? ReadInfo(string path)
    {
        using var f = File.OpenRead(path);
        Span<byte> head = stackalloc byte[0x40];
        int n = f.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        return ReadInfo(head[..n]);
    }

    public static BinkAudio Open(string path) => Open(File.ReadAllBytes(path));

    public static BinkAudio Open(byte[] data)
    {
        var info = ParseHeader(data, out int tracks, out int tableOffset)
                   ?? throw new InvalidDataException("Bink 소리 파일이 아닙니다.");
        long fileSize = BitConverter.ToUInt32(data, 4) + 8L;
        var pos = new int[info.FrameCount + 1];
        if (tableOffset + 4L * info.FrameCount > data.Length) throw new InvalidDataException("Bink 프레임 표가 잘렸습니다.");
        for (int i = 0; i < info.FrameCount; i++) pos[i] = (int)(BitConverter.ToUInt32(data, tableOffset + 4 * i) & ~1u);
        pos[info.FrameCount] = (int)Math.Min(fileSize, data.Length);
        return new BinkAudio(data, info, tracks, pos);
    }

    private static BinkInfo? ParseHeader(ReadOnlySpan<byte> d, out int tracks, out int tableOffset)
    {
        tracks = 0; tableOffset = 0;
        if (d.Length < 0x2c || d[0] != 'B' || d[1] != 'I' || d[2] != 'K') return null;
        char revision = (char)d[3];
        uint U32(ReadOnlySpan<byte> s, int o) => BitConverter.ToUInt32(s.Slice(o, 4));
        int frames = (int)U32(d, 8);
        int fpsNum = (int)U32(d, 0x1c), fpsDen = (int)U32(d, 0x20);
        tracks = (int)U32(d, 0x28);
        if (tracks < 1 || tracks > 256 || frames < 0 || frames > 1_000_000) return null;
        int o = 0x2c + (revision == 'k' ? 4 : 0);   // FFmpeg: BIKk 는 칸 하나가 더 있다
        o += 4 * tracks;                            // 갈래마다 최대 풀린 크기
        if (d.Length < o + 4) return null;
        int rate = BitConverter.ToUInt16(d.Slice(o, 2));
        ushort flags = BitConverter.ToUInt16(d.Slice(o + 2, 2));
        tableOffset = o + 4 * tracks + 4 * tracks;  // (표본률·플래그) 뒤 갈래 번호
        return new BinkInfo(revision, frames, fpsNum, fpsDen, rate, (flags & FlagStereo) != 0 ? 2 : 1, (flags & FlagUseDct) != 0);
    }

    /// <summary>첫 소리 갈래를 통째로 풀어 16비트 PCM 으로 낸다. 길이는 묶음마다 적힌 풀린 크기 합으로 잘라 맞춘다.</summary>
    public PcmSound Decode()
    {
        if (Info.UseDct) throw new NotSupportedException("Bink DCT 소리는 아직 못 풉니다(이 게임 파일은 모두 RDCT).");
        var dec = new RdctDecoder(Info.SampleRate, Info.Channels);
        var output = new List<short>(Math.Max(0, (int)(Info.Duration.TotalSeconds * Info.SampleRate * Info.Channels) + 8192));
        long reportedSamples = 0;

        for (int f = 0; f < Info.FrameCount; f++)
        {
            int p = _framePos[f], end = _framePos[f + 1];
            // 첫 갈래만 쓴다 — 이 게임 파일은 모두 갈래 하나다.
            if (p + 4 > end || _audioTracks < 1) continue;
            int size = (int)BitConverter.ToUInt32(_data, p);
            if (size < 4 || p + 4 + size > end) continue;
            var packet = new ReadOnlySpan<byte>(_data, p + 4, size);
            reportedSamples += BitConverter.ToUInt32(packet[..4]) / 2;
            dec.DecodePacket(packet, output);
        }

        int keep = (int)Math.Min(output.Count, reportedSamples - reportedSamples % Info.Channels);
        return new PcmSound(output.GetRange(0, keep).ToArray(), Info.SampleRate, Info.Channels);
    }

    /// <summary>binkaudio.c 의 RDCT 갈래 — 스테레오도 채널을 한 줄로 번갈아 담아 한 번에 변환한다.</summary>
    private sealed class RdctDecoder
    {
        private readonly int _frameLen, _overlapLen, _numBands;
        private readonly int[] _bands = new int[26];
        private readonly float[] _quantTable = new float[96];
        private readonly float _root;
        private readonly float[] _previous;
        private readonly float[] _coeffs;
        private readonly float[] _quant = new float[25];
        private readonly Fft _fft;
        private bool _first = true;

        public RdctDecoder(int sampleRate, int channels)
        {
            int frameLenBits = sampleRate < 22050 ? 9 : sampleRate < 44100 ? 10 : 11;
            sampleRate *= channels;
            frameLenBits += BitOperations.Log2((uint)channels);
            _frameLen = 1 << frameLenBits;
            _overlapLen = _frameLen / 16;
            int sampleRateHalf = (sampleRate + 1) / 2;
            _root = (float)(2.0 / (Math.Sqrt(_frameLen) * 32768.0));
            for (int i = 0; i < 96; i++) _quantTable[i] = MathF.Exp(i * 0.15289164787221953823f) * _root;

            for (_numBands = 1; _numBands < 25; _numBands++)
                if (sampleRateHalf <= CriticalFreqs[_numBands - 1]) break;
            _bands[0] = 2;
            for (int i = 1; i < _numBands; i++) _bands[i] = (CriticalFreqs[i - 1] * _frameLen / sampleRateHalf) & ~1;
            _bands[_numBands] = _frameLen;

            _previous = new float[_overlapLen];
            _coeffs = new float[_frameLen];
            _fft = Fft.Get(_frameLen);
        }

        public void DecodePacket(ReadOnlySpan<byte> packet, List<short> output)
        {
            var br = new BitReader(packet);
            br.Skip(32);   // 풀린 크기
            while (br.BitsLeft >= 58 + _numBands * 8)
            {
                if (!DecodeBlock(ref br)) return;
                for (int i = 0; i < _frameLen - _overlapLen; i++)
                {
                    float v = _coeffs[i] * 32768f;
                    output.Add((short)Math.Clamp(MathF.Round(v), short.MinValue, short.MaxValue));
                }
                br.AlignTo32();
            }
        }

        private float ReadFloat(ref BitReader br)
        {
            int power = (int)br.Read(5);
            float f = MathF.ScaleB(br.Read(23), power - 23);
            return br.Read(1) != 0 ? -f : f;
        }

        private bool DecodeBlock(ref BitReader br)
        {
            var c = _coeffs;
            c[0] = ReadFloat(ref br) * _root;
            c[1] = ReadFloat(ref br) * _root;
            for (int i = 0; i < _numBands; i++) _quant[i] = _quantTable[Math.Min(br.Read(8), 95u)];

            int k = 0, idx = 2;
            float q = _quant[0];
            while (idx < _frameLen)
            {
                if (br.BitsLeft < 5) return false;
                int j = br.Read(1) != 0 ? idx + RleLengths[br.Read(4)] * 8 : idx + 8;
                j = Math.Min(j, _frameLen);
                int width = (int)br.Read(4);
                if (width == 0)
                {
                    Array.Clear(c, idx, j - idx);
                    idx = j;
                    while (_bands[k] < idx) q = _quant[k++];
                }
                else
                {
                    while (idx < j)
                    {
                        if (_bands[k] == idx) q = _quant[k++];
                        uint coeff = br.Read(width);
                        if (coeff != 0) c[idx] = br.Read(1) != 0 ? -q * coeff : q * coeff;
                        else c[idx] = 0f;
                        idx++;
                    }
                }
            }

            _fft.InverseRdft(c);

            // 앞 블록 꼬리와 겹쳐 잇는다(겹침 구간을 곧은 비율로 섞음).
            int count = _overlapLen;
            if (!_first)
                for (int i = 0; i < _overlapLen; i++)
                    c[i] = (_previous[i] * (count - i) + c[i] * i) / count;
            Array.Copy(c, _frameLen - _overlapLen, _previous, 0, _overlapLen);
            _first = false;
            return true;
        }
    }

    /// <summary>작은 쪽 비트부터 읽는 비트 읽개(FFmpeg BITSTREAM_READER_LE).</summary>
    private ref struct BitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private long _pos;

        public readonly long BitsLeft => (long)_data.Length * 8 - _pos;

        public void Skip(int n) => _pos += n;

        public void AlignTo32() => _pos += (-_pos) & 31;

        public uint Read(int n)
        {
            if (n == 0) return 0;
            ulong v = 0;
            int byteIndex = (int)(_pos >> 3), shift = (int)(_pos & 7);
            for (int i = 0; i < 5; i++)
            {
                int bi = byteIndex + i;
                if (bi < _data.Length) v |= (ulong)_data[bi] << (8 * i);
            }
            _pos += n;
            return (uint)((v >> shift) & ((1UL << n) - 1));
        }
    }

    /// <summary>2의 거듭제곱 크기 복소 FFT — 크기마다 표를 한 번만 만든다.</summary>
    private sealed class Fft
    {
        private static readonly Dictionary<int, Fft> Cache = [];

        private readonly int _n;
        private readonly int[] _rev;
        private readonly double[] _cos, _sin;
        private readonly double[] _re, _im;

        private Fft(int n)
        {
            _n = n;
            int bits = BitOperations.Log2((uint)n);
            _rev = new int[n];
            for (int i = 0; i < n; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
                _rev[i] = r;
            }
            _cos = new double[n / 2]; _sin = new double[n / 2];
            for (int i = 0; i < n / 2; i++) { _cos[i] = Math.Cos(2 * Math.PI * i / n); _sin[i] = Math.Sin(2 * Math.PI * i / n); }
            _re = new double[n]; _im = new double[n];
        }

        public static Fft Get(int n)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(n, out var fft)) Cache[n] = fft = new Fft(n);
                return new Fft(fft);   // 작업칸은 디코더마다 따로
            }
        }

        private Fft(Fft shared)
        {
            _n = shared._n; _rev = shared._rev; _cos = shared._cos; _sin = shared._sin;
            _re = new double[_n]; _im = new double[_n];
        }

        /// <summary>
        /// 계수 x(길이 N) 를 시간 표본으로 되돌린다: 짝 (x[2m], x[2m+1]) 을 복소수 z_m 으로 묶어
        /// <c>Re Σ z_m e^{-i2πmn/N}</c> 를 구하고, m=0 항(직류)과 나이퀴스트 항을 FFmpeg 과 같게 반으로 맞춘다.
        /// </summary>
        public void InverseRdft(float[] x)
        {
            int n = _n, half = n / 2;
            for (int m = 0; m < half; m++) { _re[m] = x[2 * m]; _im[m] = x[2 * m + 1]; }
            Array.Clear(_re, half, half); Array.Clear(_im, half, half);
            Transform();
            double dc = x[0] * 0.5, nyq = x[1] * 0.5;
            for (int i = 0; i < n; i++)
                x[i] = (float)(_re[i] - dc + ((i & 1) == 0 ? nyq : -nyq));
        }

        /// <summary>제자리 FFT: X[n] = Σ_k x[k]·e^{-i2πkn/N}.</summary>
        private void Transform()
        {
            int n = _n;
            for (int i = 0; i < n; i++)
            {
                int j = _rev[i];
                if (j > i) { (_re[i], _re[j]) = (_re[j], _re[i]); (_im[i], _im[j]) = (_im[j], _im[i]); }
            }
            for (int size = 2; size <= n; size <<= 1)
            {
                int halfSize = size >> 1, step = n / size;
                for (int start = 0; start < n; start += size)
                {
                    for (int k = 0; k < halfSize; k++)
                    {
                        double wr = _cos[k * step], wi = -_sin[k * step];
                        int a = start + k, b = a + halfSize;
                        double tr = _re[b] * wr - _im[b] * wi;
                        double ti = _re[b] * wi + _im[b] * wr;
                        _re[b] = _re[a] - tr; _im[b] = _im[a] - ti;
                        _re[a] += tr; _im[a] += ti;
                    }
                }
            }
        }
    }
}
