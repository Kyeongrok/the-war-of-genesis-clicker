using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>
/// 전투 맵 그림 한 장 — BGRA 픽셀과 전투 칸 수. 칸 하나는 <see cref="ObtMap.CellWidth"/>×<see cref="ObtMap.CellHeight"/> 픽셀이다.
/// </summary>
/// <param name="OriginY">그림의 맨 윗줄이 칸 판의 몇 픽셀 자리에 오는지(음수면 판 위로 삐져나간다).</param>
/// <param name="Heights">칸 높이(칸 수 = Cols×Rows, 행 우선) = (칸 레코드 첫 u16 + 19) / 20. 지형 비트 0x10 칸은 0.</param>
/// <param name="Flags">칸 지형 플래그(칸 레코드 뒤 u16 격자). <c>&amp; 0x9</c> 면 못 들어가고, <c>&amp; 0x8</c> 이면 공격 범위에서도 빠진다.</param>
/// <param name="Corners">칸 네 모서리 높이(원 단위, 20 = 한 층) — 칸마다 레코드 앞 네 워드 차례: <c>+0</c> 남동 · <c>+2</c> 북서 · <c>+4</c> 북동 · <c>+6</c> 남서.
/// 이웃 칸끼리 맞닿은 모서리 값이 이어지는 것으로 확인했다(Obt 0153). 밝기 계산 <c>0x10030cc0</c> 은 북서(+2)에서 북동(+4)·남서(+6)로의 기울기를 쓴다.</param>
/// <param name="Slopes">칸 비탈 모양(레코드 <c>+0xa</c>, 0 평지 · 1~3 비탈) — 원본은 이 값으로 칸 칠하기 모양을 고른다.</param>
public sealed record ObtMapImage(int Width, int Height, int OriginY, int Cols, int Rows, byte[] Bgra, int[] Heights, ushort[] Flags,
                                 short[]? Corners = null, byte[]? Slopes = null)
{
    /// <summary>맵 밖은 높이 0 · 막힌 칸(플래그 8)으로 본다 — 판이 맵보다 클 수 있다(전투마다 맵 크기가 다르다).</summary>
    public int HeightAt(int col, int row) => Inside(col, row) ? Heights[row * Cols + col] : 0;

    /// <summary>모서리 높이(원 단위) — k: 0 남동 · 1 북서 · 2 북동 · 3 남서. 자료가 없으면 칸 높이 × 20.</summary>
    public int CornerAt(int col, int row, int k) =>
        Inside(col, row) && Corners != null ? Corners[(row * Cols + col) * 4 + k] : HeightAt(col, row) * 20;

    /// <summary>비탈 모양(0 평지 · 1~3 비탈). 맵 밖·자료 없음은 0.</summary>
    public int SlopeAt(int col, int row) => Inside(col, row) && Slopes != null ? Slopes[row * Cols + col] : 0;
    public ushort FlagsAt(int col, int row) => Inside(col, row) ? Flags[row * Cols + col] : (ushort)0x8;

    private bool Inside(int col, int row) => (uint)col < Cols && (uint)row < Rows;
}

/// <summary>
/// 물체(Obj) 가 판에 찍는 발자국 — 물체의 <c>.obj +12</c> 가 가리키는 Obt 의 칸 레코드만 읽은 것.
/// </summary>
/// <param name="Raw">칸 레코드 첫 u16(원높이, 판 1 파일은 ×20). 도장 높이 = (원높이 + 19) / 20 + 바닥층, 0 이면 높이를 안 쓴다.</param>
/// <param name="Flags">칸 뒤 u16 격자 그대로 — 비트 <c>0x10</c> 인 칸은 발자국이 아니다. 나머지는 판 플래그를 <b>덮어쓴다</b>(<c>0x10028620</c>).</param>
public sealed record ObtFootprint(int Cols, int Rows, ushort[] Raw, ushort[] Flags)
{
    /// <summary>그 칸이 발자국인가(격자 비트 0x10 이 아닌 칸).</summary>
    public bool Covers(int col, int row) => (uint)col < Cols && (uint)row < Rows && (Flags[row * Cols + col] & 0x10) == 0;
}

/// <summary>
/// <c>Obt/NNNN.obt</c> — 전투 맵. 칸별 지형 정보와, 맵 그림을 이루는 40×8 픽셀 띠 타일이 들어 있다.
/// </summary>
/// <remarks>
/// <c>G3PartII.dll</c> 의 <c>LoadObtFile</c>(VA <c>0x10030840</c>, 문자열 <c>Obt\%04d.obt</c>)이 읽는 순서를
/// 그대로 따랐다. 0153.obt 를 이 순서로 읽으면 파일 끝과 딱 맞아떨어진다.
/// <code>
/// u16 version, s16 cols, s16 rows
/// cols*rows × 칸 레코드 (10+1+16+2+2+2 바이트, version ≥ 4 면 +6+6)
/// cols*rows × u16            칸별 지형 값
/// s16 layerW, layerH, layerX, layerY
/// layerW*layerH × (s8, s8, u16 타일번호)   — 그림 배치표. 한 칸이 40×8 픽셀 띠 하나다
/// u32 n, n × (s16 x, s16 y, s16 w, s16 h, w*h × (u8 칸 줄, u8 z, u16 타일))  — 덧그림(유닛 앞에 서는 층)
/// u32 tileCount, tileCount × 640 바이트(40×8 RGB555), tileCount × 1 바이트
/// version ≥ 3: u32 n, n × (s16 w, s16 h, u16×3, w*h 바이트)
/// </code>
/// 전투 칸은 40×32 픽셀이다 — 원본은 640×480 화면에서 커서 칸이 40×32 이고, 32 칸 × 40 = 1280 이
/// 배치표 폭과 맞는다. <c>layerY</c>(0153 은 −2)는 띠 두 줄(16픽셀)만큼 그림을 위로 올린다.
/// 배치표 레코드의 앞 두 바이트(s8 두 개)와 마지막 절의 뜻은 아직 모른다 — 그림에는 안 쓴다. 덧그림은 <see cref="ParseDepth"/> 참고.
/// </remarks>
public static class ObtMap
{
    public const int CellWidth = 40, CellHeight = 32;
    internal const int StripWidth = 40, StripHeight = 8;

    public static ObtMapImage Load(string path) => Parse(File.ReadAllBytes(path), Path.GetFileName(path));

    /// <summary>
    /// 물체 발자국만 읽는다 — 칸 레코드 첫 u16(원높이)과 뒤 u16 격자(플래그). 그림은 안 푼다.
    /// 원본 LoadBtl 이 물체마다 이것을 판에 찍는다(<c>0x10062b11</c> → <c>0x10028620</c>). 모자라면 null.
    /// </summary>
    public static ObtFootprint? ReadFootprint(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 6) return null;
        int version = BitConverter.ToUInt16(bytes, 0);
        int cols = BitConverter.ToInt16(bytes, 2), rows = BitConverter.ToInt16(bytes, 4);
        if (cols <= 0 || rows <= 0) return null;
        int cellRecordSize = 10 + 1 + 16 + 2 + 2 + 2 + (version >= 4 ? 12 : 0);
        int n = cols * rows, grid = 6 + n * cellRecordSize;
        if (bytes.Length < grid + n * 2) return null;
        var raw = new ushort[n];
        var flags = new ushort[n];
        for (int i = 0; i < n; i++)
        {
            int v = BitConverter.ToUInt16(bytes, 6 + i * cellRecordSize);
            raw[i] = (ushort)(version == 1 ? v * 20 : v);   // 판 1 파일은 ×20 해서 읽는다(분석-전투)
            flags[i] = BitConverter.ToUInt16(bytes, grid + i * 2);
        }
        return new ObtFootprint(cols, rows, raw, flags);
    }

    /// <summary>덧그림 띠 하나 — 배치표 띠 자리(열, 줄), 그 띠의 깊이(칸 줄 · 높이 원 단위 z 바이트)와 타일 번호.</summary>
    private readonly record struct Overlay(int Col, int Row, int CellRow, int Z, int Tile);

    /// <summary>
    /// 덧그림 절 <c>u32 n, n × (s16 x, s16 y, s16 w, s16 h, w·h × (u8 칸 줄, u8 z, u16 타일))</c> — 파트1 윗층과 같은 꼴, 배치표와 같은 띠 좌표(layerY 를 따로 안 더한다).
    /// 타일 0 은 원본이 안 그린다.
    /// </summary>
    private static List<Overlay> ReadOverlays(BinaryReader reader)
    {
        var list = new List<Overlay>();
        uint n = reader.ReadUInt32();
        for (int i = 0; i < n; i++)
        {
            int x = reader.ReadInt16(), y = reader.ReadInt16(), w = reader.ReadInt16(), h = reader.ReadInt16();
            for (int k = 0; k < w * h; k++)
            {
                int row = reader.ReadByte(), z = reader.ReadByte(), tile = reader.ReadUInt16();
                if (tile != 0) list.Add(new Overlay(x + k % w, y + k / w, row, z, tile));
            }
        }
        return list;
    }

    /// <summary>
    /// 덧그림 띠의 깊이 격자 — 바탕 띠는 넣지 않는다(바탕은 유닛을 가리지 않는다). 덧그림이 없으면 null.
    /// 원본은 덧그림 띠마다(<c>0x100d6c00</c> → <c>0x10027c20</c>) 깊이 (칸 줄·40, z·20)으로 그리기 목록(<c>0x1002e2c0</c>, 파트1 <c>0x1001bc60</c> 과 같은 것)에 넣어
    /// 유닛과 함께 깊이 차례로 그린다 — 유닛보다 앞 줄의 덧그림(벽 · 기둥 · 덩굴)이 유닛을 가린다. 파트1 <see cref="Part1Map.ParseDepth"/> 와 같은 격자를 돌려준다.
    /// </summary>
    /// <remarks>z 바이트는 원 높이(한 층 = 20) 그대로라 층으로는 (z + 19) / 20 이다.</remarks>
    public static Part1MapDepthGrid? ParseDepth(byte[] bytes)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            int version = reader.ReadUInt16();
            int cols = reader.ReadInt16(), rows = reader.ReadInt16();
            if (cols <= 0 || rows <= 0) return null;
            int cellRecordSize = 10 + 1 + 16 + 2 + 2 + 2 + (version >= 4 ? 12 : 0);
            reader.BaseStream.Seek((long)cols * rows * (cellRecordSize + 2), SeekOrigin.Current);
            int layerW = reader.ReadInt16(), layerH = reader.ReadInt16();
            reader.ReadInt16();
            int layerY = reader.ReadInt16();
            if (layerW <= 0 || layerH <= 0) return null;
            reader.BaseStream.Seek((long)layerW * layerH * 4, SeekOrigin.Current);
            var overlays = ReadOverlays(reader);
            if (overlays.Count == 0) return null;
            int tileCount = (int)reader.ReadUInt32();
            int stripPixels = StripWidth * StripHeight;
            byte[] tiles = reader.ReadBytes(tileCount * stripPixels * 2);
            if (tiles.Length != tileCount * stripPixels * 2) return null;
            byte[] opaque = reader.ReadBytes(tileCount);
            var pictures = new Dictionary<(int, bool), uint[]>();
            var cells = new List<Part1MapStrip>?[layerW * layerH];
            foreach (var o in overlays)
            {
                if (o.Tile >= tileCount || (uint)o.Col >= layerW || (uint)o.Row >= layerH) continue;
                bool keyed = o.Tile >= opaque.Length || opaque[o.Tile] == 0;
                if (!pictures.TryGetValue((o.Tile, keyed), out var px))
                {
                    px = new uint[stripPixels];
                    for (int p = 0, src = o.Tile * stripPixels * 2; p < stripPixels; p++, src += 2)
                    {
                        int c = tiles[src] | tiles[src + 1] << 8;
                        if (c == 0 && keyed) continue;
                        px[p] = 0xFF000000u | (uint)((c >> 10 & 0x1F) * 255 / 31) << 16 | (uint)((c >> 5 & 0x1F) * 255 / 31) << 8 | (uint)((c & 0x1F) * 255 / 31);
                    }
                    pictures[(o.Tile, keyed)] = px;
                }
                (cells[o.Row * layerW + o.Col] ??= []).Add(new Part1MapStrip(o.CellRow, (o.Z + 19) / 20, px));
            }
            return new Part1MapDepthGrid(0, layerY, layerW, layerH, cells);
        }
        catch (EndOfStreamException) { return null; }
    }

    /// <summary>메모리에 읽어 둔 Obt(예: pak 안 파일)를 푼다.</summary>
    public static ObtMapImage Parse(byte[] bytes, string name = "obt")
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        string path = name;

        int version = reader.ReadUInt16();
        int cols = reader.ReadInt16(), rows = reader.ReadInt16();
        int cellRecordSize = 10 + 1 + 16 + 2 + 2 + 2 + (version >= 4 ? 12 : 0);
        // 칸 높이·지형 플래그 (0x10028680, 0x10030919 — 옵시디안 분석-전투 "이동 가능 영역")
        var heights = new int[cols * rows];
        var flags = new ushort[cols * rows];
        var corners = new short[cols * rows * 4];
        var slopes = new byte[cols * rows];
        for (int i = 0; i < heights.Length; i++)
        {
            // 앞 네 워드 = 모서리 높이(남동·북서·북동·남서), 다섯째 워드 = 대표 높이, 그 뒤 바이트 = 비탈 모양(0x10030cc0·0x100d7ba3).
            for (int k = 0; k < 4; k++)
            {
                int raw = reader.ReadUInt16();
                corners[i * 4 + k] = (short)(version == 1 ? raw * 20 : raw);   // 판 1 파일은 ×20 해서 읽는다(분석-전투)
            }
            heights[i] = (corners[i * 4] + 19) / 20;
            reader.ReadUInt16();
            slopes[i] = reader.ReadByte();
            reader.BaseStream.Seek(cellRecordSize - 11, SeekOrigin.Current);
        }
        for (int i = 0; i < flags.Length; i++)
        {
            ushort f = reader.ReadUInt16();
            if ((f & 0x10) != 0) { heights[i] = 0; f = 0; slopes[i] = 0; for (int k = 0; k < 4; k++) corners[i * 4 + k] = 0; }
            flags[i] = f;
        }

        int layerW = reader.ReadInt16(), layerH = reader.ReadInt16();
        reader.ReadInt16();
        int layerY = reader.ReadInt16();
        var tileIds = new int[layerW * layerH];
        for (int i = 0; i < tileIds.Length; i++)
        {
            reader.ReadInt16();
            tileIds[i] = reader.ReadUInt16();
        }

        var overlays = ReadOverlays(reader);

        int tileCount = (int)reader.ReadUInt32();
        byte[] tiles = reader.ReadBytes(tileCount * StripWidth * StripHeight * 2);
        if (tiles.Length != tileCount * StripWidth * StripHeight * 2)
            throw new InvalidDataException($"{Path.GetFileName(path)}: 타일 자료가 잘렸습니다.");
        byte[] opaque = reader.ReadBytes(tileCount);

        int width = layerW * StripWidth, height = layerH * StripHeight;
        var bgra = new byte[width * height * 4];
        void Put(int lx, int ly, int id, bool keyed)
        {
            if (id >= tileCount || (uint)lx >= layerW || (uint)ly >= layerH) return;
            int src = id * StripWidth * StripHeight * 2;
            for (int py = 0; py < StripHeight; py++)
            {
                int dst = ((ly * StripHeight + py) * width + lx * StripWidth) * 4;
                for (int px = 0; px < StripWidth; px++, src += 2, dst += 4)
                {
                    int c = tiles[src] | (tiles[src + 1] << 8);
                    if (c == 0 && keyed) continue;
                    bgra[dst] = (byte)(((c >> 0) & 0x1F) * 255 / 31);
                    bgra[dst + 1] = (byte)(((c >> 5) & 0x1F) * 255 / 31);
                    bgra[dst + 2] = (byte)(((c >> 10) & 0x1F) * 255 / 31);
                    bgra[dst + 3] = 255;
                }
            }
        }
        for (int ly = 0; ly < layerH; ly++)
            for (int lx = 0; lx < layerW; lx++)
                Put(lx, ly, tileIds[ly * layerW + lx], keyed: false);
        // 덧그림(유닛 앞에 서는 층)도 그림에 얹는다 — 전에는 읽고 버려서 107/144 맵에서 그림이 빠졌다(Obt 0153 앞쪽 덩굴 대신 바탕의 검은 그림자, 버그 리포트 #102).
        // 불투명 바이트가 0 인 타일은 색 0 이 비친다(파트1 윗층과 같은 규칙 — 덧그림 타일 51,094개 전수).
        foreach (var o in overlays)
            Put(o.Col, o.Row, o.Tile, keyed: o.Tile >= opaque.Length || opaque[o.Tile] == 0);

        return new ObtMapImage(width, height, layerY * StripHeight, cols, rows, bgra, heights, flags, corners, slopes);
    }
}
