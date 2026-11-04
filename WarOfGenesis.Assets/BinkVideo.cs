using System.Buffers.Binary;

namespace WarOfGenesis.Assets;

/// <summary>
/// Bink 1 영상 디코더 — 원본 <c>Mov\NNNN.mov</c>(<c>BIKi</c>, 640×480 · 15fps)의 영상 묶음 하나를 풀어 YUV 4:2:0 판 셋에 쌓는다.
/// 게임은 binkw32.dll 로 틀지만 우리는 네이티브 없이 C# 으로 푼다(소리는 <see cref="BinkAudio"/>).
/// </summary>
/// <remarks>
/// <para>
/// 풀이는 FFmpeg <c>libavcodec/bink.c</c> · <c>binkdsp.c</c> 의 판 풀기(버전 c~k 갈래, 옛 <c>b</c> 갈래는 뺐다)를 우리 꼴로 다시 쓴 것이다 —
/// 표(훑는 차례 · 허프만 나무 · 런 무늬 · 양자화)는 <see cref="BinkVideoTables"/>. PyAV(FFmpeg)가 내는 YUV 와 컷마다 비트까지 같다
/// (tools 쪽 비교는 커밋 설명 참고).
/// </para>
/// <para>
/// 틀 하나: (BIKi 이상) 32비트 건너뜀 → 판마다 Y · V · U 차례(h 이상은 둘째·셋째 판을 맞바꿔 담는다) — 판마다 아홉 묶음(블록 종류 ·
/// 16×16 하위 종류 · 색 · 무늬 · 움직임 x · y · 안 DC · 사이 DC · 런)의 나무를 읽고, 8×8 블록 줄마다 묶음을 채운 뒤 블록을 푼다. 판 끝은 32비트에 맞춘다.
/// 블록 종류: 0 건너뜀(앞 컷 그대로) · 1 16×16(하위 종류를 두 배로) · 2 움직임 · 3 런 · 4 움직임+나머지 · 5 안 DCT · 6 채움 · 7 움직임+DCT · 8 두 색 무늬 · 9 날것.
/// </para>
/// <para>
/// 판 줄 너비(stride)는 FFmpeg 이 잡는 것과 같게(너비를 16 에 맞추고, Y·UV 줄이 다 64 의 배수가 될 때까지 늘림) 잡는다 —
/// 움직임 벡터가 오른쪽 끝을 넘으면 다음 줄을 읽는데, 그 자리까지 같아야 컷이 비트까지 같다.
/// </para>
/// </remarks>
public sealed class BinkVideoDecoder
{
    private const int SrcBlockTypes = 0, SrcSubBlockTypes = 1, SrcColors = 2, SrcPattern = 3, SrcXOff = 4, SrcYOff = 5,
                      SrcIntraDc = 6, SrcInterDc = 7, SrcRun = 8, SourceCount = 9;

    private const int SkipBlock = 0, ScaledBlock = 1, MotionBlock = 2, RunBlock = 3, ResidueBlock = 4, IntraBlock = 5,
                      FillBlock = 6, InterBlock = 7, PatternBlock = 8, RawBlock = 9;

    private const int DcStartBits = 11;
    private static readonly byte[] BlockRunLengths = [4, 8, 12, 32];

    /// <summary>허프만 나무 16개를 미리 푼 찾기 표 — 칸 = (코드 번호 &lt;&lt; 4) | 코드 길이, 비트는 작은 쪽부터 읽는다.</summary>
    private static readonly (byte[] Table, int Bits)[] Vlcs = BuildVlcs();

    public int Width { get; }
    public int Height { get; }
    public char Revision { get; }

    /// <summary>판 Y · U · V 의 줄 너비.</summary>
    public int LumaStride { get; }
    public int ChromaStride { get; }

    /// <summary>방금 푼 컷의 판(0 = Y, 1 = U, 2 = V) — 다음 <see cref="Decode"/> 전까지 그대로다.</summary>
    public byte[][] Planes => _cur;

    private byte[][] _cur, _prev;
    private bool _havePrev;
    private readonly int[] _stride;

    private sealed class Tree
    {
        public int Vlc;
        public readonly byte[] Syms = new byte[16];
    }

    private sealed class Bundle(int size)
    {
        public int Len;
        public readonly Tree Tree = new();
        public readonly byte[] Data = new byte[size];
        /// <summary>아직 안 채운 자리 — -1 이면 이 판에서는 더 안 채운다(FFmpeg 의 NULL).</summary>
        public int Dec;
        public int Ptr;
    }

    private readonly Bundle[] _bundles = new Bundle[SourceCount];
    private readonly short[][] _dcs = new short[2][];   // 안 · 사이 DC(16비트) — 묶음 Dec/Ptr 는 이 배열 칸 단위
    private readonly Tree[] _colHigh = new Tree[16];
    private int _colLast;

    // 비트 읽개 — 작은 쪽 비트부터(FFmpeg BITSTREAM_READER_LE). 끝 뒤로 8바이트를 0 으로 채워 둔다.
    private byte[] _buf = new byte[0];
    private long _bit, _bitEnd;

    private readonly int[] _coefList = new int[128], _modeList = new int[128];
    private readonly int[] _dct = new int[64], _temp = new int[64], _coefIdx = new int[64];
    private readonly short[] _residue = new short[64];
    private readonly int[] _nz = new int[64];
    private readonly byte[] _ublock = new byte[64];

    public BinkVideoDecoder(int width, int height, char revision)
    {
        if (width is <= 0 or > 4096 || height is <= 0 or > 4096) throw new InvalidDataException("Bink 영상 크기가 이상합니다.");
        if (revision < 'c' || revision > 'k') throw new NotSupportedException($"Bink 버전 {revision} 은 못 풉니다.");
        Width = width; Height = height; Revision = revision;

        // FFmpeg 의 판 잡기: 너비를 16 에 맞추고, Y 줄과 UV 줄(반올림 반)이 다 64 의 배수가 될 때까지 가장 낮은 비트만큼 늘린다.
        int w = (width + 15) & ~15;
        while (w % 64 != 0 || ((w + 1) >> 1) % 64 != 0) w += w & -w;
        LumaStride = w;
        ChromaStride = (w + 1) >> 1;
        _stride = [LumaStride, ChromaStride, ChromaStride];

        int bw = (width + 7) >> 3, bh = (height + 7) >> 3;
        int blocks = bw * bh;
        for (int i = 0; i < SourceCount; i++) _bundles[i] = new Bundle(i is SrcIntraDc or SrcInterDc ? 0 : blocks * 64);
        _dcs[0] = new short[blocks * 32];
        _dcs[1] = new short[blocks * 32];
        for (int i = 0; i < 16; i++) _colHigh[i] = new Tree();

        // 줄 수는 16 의 배수 + 여유(16×16 블록이 판 아래 · 오른쪽 끝을 조금 넘겨 쓴다).
        int lumaRows = ((height + 31) & ~31) + 16, chromaRows = lumaRows / 2 + 16;
        _cur = [new byte[LumaStride * lumaRows], new byte[ChromaStride * chromaRows], new byte[ChromaStride * chromaRows]];
        _prev = [new byte[_cur[0].Length], new byte[_cur[1].Length], new byte[_cur[2].Length]];
    }

    /// <summary>
    /// 영상 묶음 하나(소리 묶음을 뺀 나머지)를 푼다. 깨진 묶음이면 false — 그때 <see cref="Planes"/> 는 앞 컷 그대로다(FFmpeg 은 그 컷을 버린다).
    /// </summary>
    public bool Decode(ReadOnlySpan<byte> packet)
    {
        if (_buf.Length < packet.Length + 8) _buf = new byte[packet.Length + 64];
        packet.CopyTo(_buf);
        Array.Clear(_buf, packet.Length, 8);
        _bit = 0;
        _bitEnd = (long)packet.Length * 8;

        // 새 컷은 다른 판에 쌓는다 — 앞 컷(_prev)을 건너뜀 · 움직임 블록이 읽는다.
        (_cur, _prev) = (_prev, _cur);
        try
        {
            if (Revision >= 'i') _bit += 32;
            for (int plane = 0; plane < 3; plane++)
            {
                int idx = plane == 0 || Revision < 'h' ? plane : plane ^ 3;
                if (!DecodePlane(idx, plane != 0)) { Restore(); return false; }
                if (_bitEnd - _bit <= 0) break;
            }
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidDataException)
        {
            Restore();
            return false;
        }
        _havePrev = true;
        return true;

        void Restore()
        {
            (_cur, _prev) = (_prev, _cur);
        }
    }

    // ---------------- 비트 읽기 ----------------

    private uint Bits(int n)
    {
        if (n == 0) return 0;
        long p = _bit;
        int byteIndex = (int)(p >> 3);
        ulong v = byteIndex + 8 <= _buf.Length ? BinaryPrimitives.ReadUInt64LittleEndian(_buf.AsSpan(byteIndex)) : SlowLoad(byteIndex);
        _bit = p + n;
        return (uint)(v >> (int)(p & 7)) & (uint)((1UL << n) - 1);
    }

    private ulong SlowLoad(int byteIndex)
    {
        ulong v = 0;
        for (int i = 0; i < 8; i++)
            if (byteIndex + i < _buf.Length) v |= (ulong)_buf[byteIndex + i] << (8 * i);
        return v;
    }

    private bool Bit() => Bits(1) != 0;

    private long BitsLeft => _bitEnd - _bit;

    private int Huff(Tree t)
    {
        var (table, bits) = Vlcs[t.Vlc];
        int peek = (int)Bits(bits);
        int e = table[peek];
        _bit -= bits - (e & 15);
        return t.Syms[e >> 4];
    }

    private static (byte[] Table, int Bits)[] BuildVlcs()
    {
        var result = new (byte[], int)[16];
        for (int t = 0; t < 16; t++)
        {
            int maxBits = BinkVideoTables.TreeLens[t * 16 + 15];
            var table = new byte[1 << maxBits];
            for (int s = 0; s < 16; s++)
            {
                int len = BinkVideoTables.TreeLens[t * 16 + s], code = BinkVideoTables.TreeBits[t * 16 + s];
                // 작은 쪽 비트부터 읽으니 코드는 아래 비트에 놓이고 위 비트는 아무거나.
                for (int high = 0; high < 1 << (maxBits - len); high++)
                    table[code | (high << len)] = (byte)((s << 4) | len);
            }
            result[t] = (table, maxBits);
        }
        return result;
    }

    // ---------------- 묶음 읽기 ----------------

    private static void Merge(BinkVideoDecoder d, byte[] dst, int dstOff, byte[] src, int srcOff, int size)
    {
        int src2 = srcOff + size, size1 = size, size2 = size;
        do
        {
            if (!d.Bit()) { dst[dstOff++] = src[srcOff++]; size1--; }
            else { dst[dstOff++] = src[src2++]; size2--; }
        } while (size1 != 0 && size2 != 0);
        while (size1-- > 0) dst[dstOff++] = src[srcOff++];
        while (size2-- > 0) dst[dstOff++] = src[src2++];
    }

    private void ReadTree(Tree tree)
    {
        if (BitsLeft < 4) throw new InvalidDataException();
        tree.Vlc = (int)Bits(4);
        if (tree.Vlc == 0)
        {
            for (int i = 0; i < 16; i++) tree.Syms[i] = (byte)i;
            return;
        }
        if (Bit())
        {
            Span<bool> used = stackalloc bool[16];
            int len = (int)Bits(3);
            for (int i = 0; i <= len; i++)
            {
                tree.Syms[i] = (byte)Bits(4);
                used[tree.Syms[i]] = true;
            }
            for (int i = 0; i < 16 && len < 15; i++)
                if (!used[i]) tree.Syms[++len] = (byte)i;
        }
        else
        {
            int len = (int)Bits(2);
            byte[] a = new byte[16], b = new byte[16];
            for (int i = 0; i < 16; i++) a[i] = (byte)i;
            for (int i = 0; i <= len; i++)
            {
                int size = 1 << i;
                for (int t = 0; t < 16; t += size << 1) Merge(this, b, t, a, t, size);
                (a, b) = (b, a);
            }
            Array.Copy(a, tree.Syms, 16);
        }
    }

    private void ReadBundle(int n)
    {
        if (n == SrcColors)
        {
            for (int i = 0; i < 16; i++) ReadTree(_colHigh[i]);
            _colLast = 0;
        }
        if (n != SrcIntraDc && n != SrcInterDc) ReadTree(_bundles[n].Tree);
        _bundles[n].Dec = _bundles[n].Ptr = 0;
    }

    private static int Log2(int v) => 31 - System.Numerics.BitOperations.LeadingZeroCount((uint)v);

    private void InitLengths(int width, int bw)
    {
        width = (width + 7) & ~7;
        _bundles[SrcBlockTypes].Len = Log2((width >> 3) + 511) + 1;
        _bundles[SrcSubBlockTypes].Len = Log2((width >> 4) + 511) + 1;
        _bundles[SrcColors].Len = Log2(bw * 64 + 511) + 1;
        _bundles[SrcIntraDc].Len = _bundles[SrcInterDc].Len = _bundles[SrcXOff].Len = _bundles[SrcYOff].Len = Log2((width >> 3) + 511) + 1;
        _bundles[SrcPattern].Len = Log2((bw << 3) + 511) + 1;
        _bundles[SrcRun].Len = Log2(bw * 48 + 511) + 1;
    }

    /// <summary>FFmpeg CHECK_READ_VAL — 이번 줄에 채울 수를 읽는다. 0 이면 채우지 않는다.</summary>
    private int CountToRead(Bundle b)
    {
        if (b.Dec < 0 || b.Dec > b.Ptr) return 0;
        int t = (int)Bits(b.Len);
        if (t == 0) b.Dec = -1;
        return t;
    }

    private void ReadRuns(Bundle b)
    {
        int t = CountToRead(b);
        if (t == 0) return;
        int end = b.Dec + t;
        if (end > b.Data.Length || BitsLeft < 1) throw new InvalidDataException();
        if (Bit()) { b.Data.AsSpan(b.Dec, t).Fill((byte)Bits(4)); b.Dec = end; }
        else while (b.Dec < end) b.Data[b.Dec++] = (byte)Huff(b.Tree);
    }

    private void ReadMotionValues(Bundle b)
    {
        int t = CountToRead(b);
        if (t == 0) return;
        int end = b.Dec + t;
        if (end > b.Data.Length || BitsLeft < 1) throw new InvalidDataException();
        if (Bit())
        {
            int v = (int)Bits(4);
            if (v != 0 && Bit()) v = -v;
            b.Data.AsSpan(b.Dec, t).Fill((byte)v);
            b.Dec = end;
        }
        else
            while (b.Dec < end)
            {
                int v = Huff(b.Tree);
                if (v != 0 && Bit()) v = -v;
                b.Data[b.Dec++] = (byte)v;
            }
    }

    private void ReadBlockTypes(Bundle b)
    {
        int t = CountToRead(b);
        if (t == 0) return;
        if (Revision == 'k')
        {
            t ^= 0xBB;
            if (t == 0) { b.Dec = -1; return; }
        }
        int end = b.Dec + t;
        if (end > b.Data.Length || BitsLeft < 1) throw new InvalidDataException();
        if (Bit()) { b.Data.AsSpan(b.Dec, t).Fill((byte)Bits(4)); b.Dec = end; }
        else
        {
            int last = 0;
            while (b.Dec < end)
            {
                int v = Huff(b.Tree);
                if (v < 12) { last = v; b.Data[b.Dec++] = (byte)v; }
                else
                {
                    int run = BlockRunLengths[v - 12];
                    if (end - b.Dec < run) throw new InvalidDataException();
                    b.Data.AsSpan(b.Dec, run).Fill((byte)last);
                    b.Dec += run;
                }
            }
        }
    }

    private void ReadPatterns(Bundle b)
    {
        int t = CountToRead(b);
        if (t == 0) return;
        int end = b.Dec + t;
        if (end > b.Data.Length) throw new InvalidDataException();
        while (b.Dec < end)
        {
            if (BitsLeft < 2) throw new InvalidDataException();
            int v = Huff(b.Tree);
            v |= Huff(b.Tree) << 4;
            b.Data[b.Dec++] = (byte)v;
        }
    }

    private int ColorValue(Bundle b)
    {
        _colLast = Huff(_colHigh[_colLast]);
        int v = (_colLast << 4) | Huff(b.Tree);
        if (Revision < 'i')
        {
            int sign = (sbyte)v >> 7;
            v = (((v & 0x7F) ^ sign) - sign) + 0x80;
        }
        return v;
    }

    private void ReadColors(Bundle b)
    {
        int t = CountToRead(b);
        if (t == 0) return;
        int end = b.Dec + t;
        if (end > b.Data.Length || BitsLeft < 1) throw new InvalidDataException();
        if (Bit()) { b.Data.AsSpan(b.Dec, t).Fill((byte)ColorValue(b)); b.Dec = end; }
        else
            while (b.Dec < end)
            {
                if (BitsLeft < 2) throw new InvalidDataException();
                b.Data[b.Dec++] = (byte)ColorValue(b);
            }
    }

    private void ReadDcs(int which, bool hasSign)
    {
        var b = _bundles[which == 0 ? SrcIntraDc : SrcInterDc];
        var dst = _dcs[which];
        int len = CountToRead(b);
        if (len == 0) return;
        int startBits = DcStartBits - (hasSign ? 1 : 0);
        if (BitsLeft < startBits) throw new InvalidDataException();
        int v = (int)Bits(startBits);
        if (v != 0 && hasSign && Bit()) v = -v;
        int d = b.Dec;
        if (dst.Length - d < 1) throw new InvalidDataException();
        dst[d++] = (short)v;
        len--;
        for (int i = 0; i < len; i += 8)
        {
            int len2 = Math.Min(len - i, 8);
            if (dst.Length - d < len2) throw new InvalidDataException();
            int bsize = (int)Bits(4);
            if (bsize != 0)
                for (int j = 0; j < len2; j++)
                {
                    int v2 = (int)Bits(bsize);
                    if (v2 != 0 && Bit()) v2 = -v2;
                    v += v2;
                    dst[d++] = (short)v;
                    if (v < -32768 || v > 32767) throw new InvalidDataException();
                }
            else
                for (int j = 0; j < len2; j++) dst[d++] = (short)v;
        }
        b.Dec = d;
    }

    private int Value(int n) => n switch
    {
        SrcXOff or SrcYOff => (sbyte)_bundles[n].Data[_bundles[n].Ptr++],
        SrcIntraDc => _dcs[0][_bundles[n].Ptr++],
        SrcInterDc => _dcs[1][_bundles[n].Ptr++],
        _ => _bundles[n].Data[_bundles[n].Ptr++],
    };

    // ---------------- 계수 ----------------

    /// <summary>DCT 계수를 읽어 <see cref="_dct"/> 에 넣고 양자화 번호를 낸다.</summary>
    private int ReadDctCoeffs(out int coefCount)
    {
        var block = _dct;
        var coefList = _coefList;
        var modeList = _modeList;
        var scan = BinkVideoTables.Scan;
        int listStart = 64, listEnd = 64;
        coefCount = 0;
        if (BitsLeft < 4) throw new InvalidDataException();

        coefList[listEnd] = 4; modeList[listEnd++] = 0;
        coefList[listEnd] = 24; modeList[listEnd++] = 0;
        coefList[listEnd] = 44; modeList[listEnd++] = 0;
        coefList[listEnd] = 1; modeList[listEnd++] = 3;
        coefList[listEnd] = 2; modeList[listEnd++] = 3;
        coefList[listEnd] = 3; modeList[listEnd++] = 3;

        for (int bits = (int)Bits(4) - 1; bits >= 0; bits--)
        {
            int listPos = listStart;
            while (listPos < listEnd)
            {
                if ((modeList[listPos] | coefList[listPos]) == 0 || !Bit()) { listPos++; continue; }
                int ccoef = coefList[listPos];
                int mode = modeList[listPos];
                switch (mode)
                {
                    case 0:
                    case 2:
                        if (mode == 0) { coefList[listPos] = ccoef + 4; modeList[listPos] = 1; }
                        else { coefList[listPos] = 0; modeList[listPos++] = 0; }
                        for (int i = 0; i < 4; i++, ccoef++)
                        {
                            if (Bit()) { coefList[--listStart] = ccoef; modeList[listStart] = 3; }
                            else
                            {
                                block[scan[ccoef]] = CoefValue(bits);
                                _coefIdx[coefCount++] = ccoef;
                            }
                        }
                        break;
                    case 1:
                        modeList[listPos] = 2;
                        for (int i = 0; i < 3; i++)
                        {
                            ccoef += 4;
                            coefList[listEnd] = ccoef;
                            modeList[listEnd++] = 2;
                        }
                        break;
                    case 3:
                        block[scan[ccoef]] = CoefValue(bits);
                        _coefIdx[coefCount++] = ccoef;
                        coefList[listPos] = 0;
                        modeList[listPos++] = 0;
                        break;
                }
            }
        }
        return (int)Bits(4);
    }

    private int CoefValue(int bits)
    {
        if (bits == 0) return Bit() ? -1 : 1;
        int t = (int)Bits(bits) | 1 << bits;
        return Bit() ? -t : t;
    }

    private void Unquantize(int[] quant, int quantIdx, int coefCount)
    {
        var block = _dct;
        var scan = BinkVideoTables.Scan;
        int q = quantIdx * 64;
        block[0] = unchecked(block[0] * quant[q]) >> 11;
        for (int i = 0; i < coefCount; i++)
        {
            int idx = _coefIdx[i];
            block[scan[idx]] = unchecked(block[scan[idx]] * quant[q + idx]) >> 11;
        }
    }

    private void ReadResidue(int masksCount)
    {
        var block = _residue;
        var coefList = _coefList;
        var modeList = _modeList;
        var scan = BinkVideoTables.Scan;
        int listStart = 64, listEnd = 64, nzCount = 0;

        coefList[listEnd] = 4; modeList[listEnd++] = 0;
        coefList[listEnd] = 24; modeList[listEnd++] = 0;
        coefList[listEnd] = 44; modeList[listEnd++] = 0;
        coefList[listEnd] = 0; modeList[listEnd++] = 2;

        for (int mask = 1 << (int)Bits(3); mask != 0; mask >>= 1)
        {
            for (int i = 0; i < nzCount; i++)
            {
                if (!Bit()) continue;
                if (block[_nz[i]] < 0) block[_nz[i]] -= (short)mask;
                else block[_nz[i]] += (short)mask;
                if (--masksCount < 0) return;
            }
            int listPos = listStart;
            while (listPos < listEnd)
            {
                if ((coefList[listPos] | modeList[listPos]) == 0 || !Bit()) { listPos++; continue; }
                int ccoef = coefList[listPos];
                int mode = modeList[listPos];
                switch (mode)
                {
                    case 0:
                    case 2:
                        if (mode == 0) { coefList[listPos] = ccoef + 4; modeList[listPos] = 1; }
                        else { coefList[listPos] = 0; modeList[listPos++] = 0; }
                        for (int i = 0; i < 4; i++, ccoef++)
                        {
                            if (Bit()) { coefList[--listStart] = ccoef; modeList[listStart] = 3; }
                            else
                            {
                                _nz[nzCount++] = scan[ccoef];
                                block[scan[ccoef]] = (short)(Bit() ? -mask : mask);
                                if (--masksCount < 0) return;
                            }
                        }
                        break;
                    case 1:
                        modeList[listPos] = 2;
                        for (int i = 0; i < 3; i++)
                        {
                            ccoef += 4;
                            coefList[listEnd] = ccoef;
                            modeList[listEnd++] = 2;
                        }
                        break;
                    case 3:
                        _nz[nzCount++] = scan[ccoef];
                        block[scan[ccoef]] = (short)(Bit() ? -mask : mask);
                        coefList[listPos] = 0;
                        modeList[listPos++] = 0;
                        if (--masksCount < 0) return;
                        break;
                }
            }
        }
    }

    // ---------------- 역 DCT(binkdsp.c) ----------------

    private const int A1 = 2896, A2 = 2217, A3 = 3784, A4 = -5352;

    private static int Mul(int x, int y) => unchecked(x * y) >> 11;

    /// <summary>세로 8점 변환 — <paramref name="src"/>[o + 8k] → <paramref name="dst"/>[o + 8k]. 위 칸 말고 다 0 이면 그대로 펼친다.</summary>
    private static void IdctCol(int[] dst, int[] src, int o)
    {
        if ((src[o + 8] | src[o + 16] | src[o + 24] | src[o + 32] | src[o + 40] | src[o + 48] | src[o + 56]) == 0)
        {
            int v = src[o];
            dst[o] = dst[o + 8] = dst[o + 16] = dst[o + 24] = dst[o + 32] = dst[o + 40] = dst[o + 48] = dst[o + 56] = v;
            return;
        }
        int s0 = src[o], s1 = src[o + 8], s2 = src[o + 16], s3 = src[o + 24], s4 = src[o + 32], s5 = src[o + 40], s6 = src[o + 48], s7 = src[o + 56];
        Transform(s0, s1, s2, s3, s4, s5, s6, s7, out int d0, out int d1, out int d2, out int d3, out int d4, out int d5, out int d6, out int d7);
        dst[o] = d0; dst[o + 8] = d1; dst[o + 16] = d2; dst[o + 24] = d3; dst[o + 32] = d4; dst[o + 40] = d5; dst[o + 48] = d6; dst[o + 56] = d7;
    }

    private static void Transform(int s0, int s1, int s2, int s3, int s4, int s5, int s6, int s7,
                                  out int d0, out int d1, out int d2, out int d3, out int d4, out int d5, out int d6, out int d7)
    {
        unchecked
        {
            int a0 = s0 + s4, a1 = s0 - s4, a2 = s2 + s6, a3 = Mul(A1, s2 - s6);
            int a4 = s5 + s3, a5 = s5 - s3, a6 = s1 + s7, a7 = s1 - s7;
            int b0 = a4 + a6, b1 = Mul(A3, a5 + a7);
            int b2 = Mul(A4, a5) - b0 + b1;
            int b3 = Mul(A1, a6 - a4) - b2;
            int b4 = Mul(A2, a7) + b3 - b1;
            d0 = a0 + a2 + b0; d1 = a1 + a3 - a2 + b2; d2 = a1 - a3 + a2 + b3; d3 = a0 - a2 - b4;
            d4 = a0 - a2 + b4; d5 = a1 - a3 + a2 - b3; d6 = a1 + a3 - a2 - b2; d7 = a0 + a2 - b0;
        }
    }

    /// <summary>가로 8점 변환 후 (x + 0x7F) &gt;&gt; 8 — 한 줄 8 값을 <paramref name="row"/> 에.</summary>
    private static void IdctRow(int[] temp, int o, Span<int> row)
    {
        Transform(temp[o], temp[o + 1], temp[o + 2], temp[o + 3], temp[o + 4], temp[o + 5], temp[o + 6], temp[o + 7],
            out int d0, out int d1, out int d2, out int d3, out int d4, out int d5, out int d6, out int d7);
        row[0] = (d0 + 0x7F) >> 8; row[1] = (d1 + 0x7F) >> 8; row[2] = (d2 + 0x7F) >> 8; row[3] = (d3 + 0x7F) >> 8;
        row[4] = (d4 + 0x7F) >> 8; row[5] = (d5 + 0x7F) >> 8; row[6] = (d6 + 0x7F) >> 8; row[7] = (d7 + 0x7F) >> 8;
    }

    /// <summary>역 DCT 를 <paramref name="dst"/>[off] 에 쓴다(<paramref name="add"/> 면 더한다) — 값은 자르지 않고 8비트로 감긴다(FFmpeg C 판과 같게).</summary>
    private void IdctTo(byte[] dst, int off, int stride, bool add)
    {
        for (int i = 0; i < 8; i++) IdctCol(_temp, _dct, i);
        Span<int> row = stackalloc int[8];
        for (int i = 0; i < 8; i++)
        {
            IdctRow(_temp, 8 * i, row);
            int p = off + i * stride;
            if (add) for (int j = 0; j < 8; j++) dst[p + j] = unchecked((byte)(dst[p + j] + row[j]));
            else for (int j = 0; j < 8; j++) dst[p + j] = unchecked((byte)row[j]);
        }
    }

    // ---------------- 판 ----------------

    private static void Copy8(byte[] dst, int d, byte[] src, int s, int stride)
    {
        for (int i = 0; i < 8; i++) Buffer.BlockCopy(src, s + i * stride, dst, d + i * stride, 8);
    }

    private static void Fill(byte[] dst, int d, int stride, int size, byte v)
    {
        for (int i = 0; i < size; i++) dst.AsSpan(d + i * stride, size).Fill(v);
    }

    /// <summary>8×8 를 16×16 으로 두 배(binkdsp scale_block).</summary>
    private static void Scale(byte[] src, byte[] dst, int d, int stride)
    {
        for (int j = 0; j < 8; j++)
        {
            int r1 = d + 2 * j * stride, r2 = r1 + stride;
            for (int i = 0; i < 8; i++)
            {
                byte v = src[j * 8 + i];
                dst[r1 + 2 * i] = dst[r1 + 2 * i + 1] = dst[r2 + 2 * i] = dst[r2 + 2 * i + 1] = v;
            }
        }
    }

    private bool PutPixels(byte[] dst, byte[] prev, int d, int stride, int refEnd)
    {
        int xoff = Value(SrcXOff), yoff = Value(SrcYOff);
        int r = d + xoff + yoff * stride;
        if (r < 0 || r > refEnd) return false;
        // 같은 판이면(첫 컷) 겹칠 수 있다 — 한 번 옮겨 담는다.
        if (ReferenceEquals(dst, prev))
        {
            Span<byte> tmp = stackalloc byte[64];
            for (int i = 0; i < 8; i++) prev.AsSpan(r + i * stride, 8).CopyTo(tmp.Slice(i * 8, 8));
            for (int i = 0; i < 8; i++) tmp.Slice(i * 8, 8).CopyTo(dst.AsSpan(d + i * stride, 8));
        }
        else Copy8(dst, d, prev, r, stride);
        return true;
    }

    private bool DecodePlane(int planeIdx, bool chroma)
    {
        int stride = _stride[planeIdx];
        int bw = chroma ? (Width + 15) >> 4 : (Width + 7) >> 3;
        int bh = chroma ? (Height + 15) >> 4 : (Height + 7) >> 3;
        int width = Width >> (chroma ? 1 : 0), height = Height >> (chroma ? 1 : 0);
        byte[] dstPlane = _cur[planeIdx];
        byte[] prevPlane = _havePrev ? _prev[planeIdx] : dstPlane;

        if (Revision == 'k' && Bit())
        {
            byte fill = (byte)Bits(8);
            for (int i = 0; i < height; i++) dstPlane.AsSpan(i * stride, width).Fill(fill);
            AlignPlaneEnd();
            return true;
        }

        InitLengths(Math.Max(width, 8), bw);
        for (int i = 0; i < SourceCount; i++) ReadBundle(i);

        int refEnd = (bw - 1 + stride * (bh - 1)) * 8;
        var scan = BinkVideoTables.Patterns;
        var colors = _bundles[SrcColors];

        for (int by = 0; by < bh; by++)
        {
            ReadBlockTypes(_bundles[SrcBlockTypes]);
            ReadBlockTypes(_bundles[SrcSubBlockTypes]);
            ReadColors(colors);
            ReadPatterns(_bundles[SrcPattern]);
            ReadMotionValues(_bundles[SrcXOff]);
            ReadMotionValues(_bundles[SrcYOff]);
            ReadDcs(0, false);
            ReadDcs(1, true);
            ReadRuns(_bundles[SrcRun]);

            int d = 8 * by * stride;
            for (int bx = 0; bx < bw; bx++, d += 8)
            {
                int blk = Value(SrcBlockTypes);
                // 홀수 줄 · 칸의 16×16 은 이미 푼 블록의 한 조각 — 건너뛴다.
                if (((by & 1) != 0 || (bx & 1) != 0) && blk == ScaledBlock)
                {
                    bx++;
                    d += 8;
                    continue;
                }
                switch (blk)
                {
                    case SkipBlock:
                        if (!ReferenceEquals(dstPlane, prevPlane)) Copy8(dstPlane, d, prevPlane, d, stride);
                        break;
                    case ScaledBlock:
                    {
                        int sub = Value(SrcSubBlockTypes);
                        var ub = _ublock;
                        switch (sub)
                        {
                            case RunBlock:
                            {
                                if (BitsLeft < 4) return false;
                                int p = (int)Bits(4) * 64, i = 0;
                                do
                                {
                                    int run = Value(SrcRun) + 1;
                                    i += run;
                                    if (i > 64) return false;
                                    if (Bit())
                                    {
                                        byte v = (byte)Value(SrcColors);
                                        for (int j = 0; j < run; j++) ub[scan[p++]] = v;
                                    }
                                    else
                                        for (int j = 0; j < run; j++) ub[scan[p++]] = (byte)Value(SrcColors);
                                } while (i < 63);
                                if (i == 63) ub[scan[p++]] = (byte)Value(SrcColors);
                                break;
                            }
                            case IntraBlock:
                            {
                                Array.Clear(_dct);
                                _dct[0] = Value(SrcIntraDc);
                                int q = ReadDctCoeffs(out int count);
                                Unquantize(BinkVideoTables.IntraQuant, q, count);
                                IdctTo(ub, 0, 8, add: false);
                                break;
                            }
                            case FillBlock:
                                Fill(dstPlane, d, stride, 16, (byte)Value(SrcColors));
                                break;
                            case PatternBlock:
                            {
                                byte c0 = (byte)Value(SrcColors), c1 = (byte)Value(SrcColors);
                                for (int j = 0; j < 8; j++)
                                {
                                    int v = Value(SrcPattern);
                                    for (int i = 0; i < 8; i++, v >>= 1) ub[i + j * 8] = (v & 1) != 0 ? c1 : c0;
                                }
                                break;
                            }
                            case RawBlock:
                                for (int i = 0; i < 64; i++) ub[i] = (byte)Value(SrcColors);
                                break;
                            default:
                                return false;
                        }
                        if (sub != FillBlock) Scale(ub, dstPlane, d, stride);
                        bx++;
                        d += 8;
                        break;
                    }
                    case MotionBlock:
                        if (!PutPixels(dstPlane, prevPlane, d, stride, refEnd)) return false;
                        break;
                    case RunBlock:
                    {
                        int p = (int)Bits(4) * 64, i = 0;
                        do
                        {
                            int run = Value(SrcRun) + 1;
                            i += run;
                            if (i > 64) return false;
                            if (Bit())
                            {
                                byte v = (byte)Value(SrcColors);
                                for (int j = 0; j < run; j++, p++) dstPlane[d + (scan[p] & 7) + (scan[p] >> 3) * stride] = v;
                            }
                            else
                                for (int j = 0; j < run; j++, p++) dstPlane[d + (scan[p] & 7) + (scan[p] >> 3) * stride] = (byte)Value(SrcColors);
                        } while (i < 63);
                        if (i == 63) dstPlane[d + (scan[p] & 7) + (scan[p] >> 3) * stride] = (byte)Value(SrcColors);
                        break;
                    }
                    case ResidueBlock:
                    {
                        if (!PutPixels(dstPlane, prevPlane, d, stride, refEnd)) return false;
                        Array.Clear(_residue);
                        int v = (int)Bits(7);
                        ReadResidue(v);
                        for (int i = 0; i < 8; i++)
                        {
                            int r = d + i * stride;
                            for (int j = 0; j < 8; j++) dstPlane[r + j] = unchecked((byte)(dstPlane[r + j] + _residue[i * 8 + j]));
                        }
                        break;
                    }
                    case IntraBlock:
                    {
                        Array.Clear(_dct);
                        _dct[0] = Value(SrcIntraDc);
                        int q = ReadDctCoeffs(out int count);
                        Unquantize(BinkVideoTables.IntraQuant, q, count);
                        IdctTo(dstPlane, d, stride, add: false);
                        break;
                    }
                    case FillBlock:
                        Fill(dstPlane, d, stride, 8, (byte)Value(SrcColors));
                        break;
                    case InterBlock:
                    {
                        if (!PutPixels(dstPlane, prevPlane, d, stride, refEnd)) return false;
                        Array.Clear(_dct);
                        _dct[0] = Value(SrcInterDc);
                        int q = ReadDctCoeffs(out int count);
                        Unquantize(BinkVideoTables.InterQuant, q, count);
                        IdctTo(dstPlane, d, stride, add: true);
                        break;
                    }
                    case PatternBlock:
                    {
                        byte c0 = (byte)Value(SrcColors), c1 = (byte)Value(SrcColors);
                        for (int i = 0; i < 8; i++)
                        {
                            int v = Value(SrcPattern);
                            int r = d + i * stride;
                            for (int j = 0; j < 8; j++, v >>= 1) dstPlane[r + j] = (v & 1) != 0 ? c1 : c0;
                        }
                        break;
                    }
                    case RawBlock:
                        for (int i = 0; i < 8; i++) Buffer.BlockCopy(colors.Data, colors.Ptr + i * 8, dstPlane, d + i * stride, 8);
                        colors.Ptr += 64;
                        break;
                    default:
                        return false;
                }
            }
        }
        AlignPlaneEnd();
        return true;
    }

    private void AlignPlaneEnd()
    {
        if ((_bit & 31) != 0) _bit += 32 - (_bit & 31);
    }

    /// <summary>
    /// 방금 푼 컷을 0xAARRGGBB 로 바꾼다(BT.601 · 16~235 범위 — BIKi 의 색 범위). <paramref name="dst"/> 는 너비×높이.
    /// </summary>
    public void ToArgb(Span<uint> dst)
    {
        if (dst.Length < Width * Height) throw new ArgumentException("버퍼가 작습니다.", nameof(dst));
        byte[] yp = _cur[0], up = _cur[1], vp = _cur[2];
        for (int y = 0; y < Height; y++)
        {
            int yr = y * LumaStride, cr = (y >> 1) * ChromaStride, o = y * Width;
            for (int x = 0; x < Width; x++)
            {
                int c = (yp[yr + x] - 16) * 76309;   // 1.164 << 16
                int u = up[cr + (x >> 1)] - 128, v = vp[cr + (x >> 1)] - 128;
                int r = (c + 104597 * v + 32768) >> 16;
                int g = (c - 25675 * u - 53279 * v + 32768) >> 16;
                int b = (c + 132201 * u + 32768) >> 16;
                r = r < 0 ? 0 : r > 255 ? 255 : r;
                g = g < 0 ? 0 : g > 255 ? 255 : g;
                b = b < 0 ? 0 : b > 255 ? 255 : b;
                dst[o + x] = 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;
            }
        }
    }
}
