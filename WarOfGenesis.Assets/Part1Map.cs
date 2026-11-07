using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>
/// 파트1(전작) 전투 맵 <c>Obts\NNNN.obt</c> 를 파트2 꼴 <see cref="ObtMapImage"/> 로 읽는다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 7절, 로더 <c>alpha.dll 0x10022bc0</c>(맵 생성 <c>0x10088590</c> → <c>0x10098a80</c> → <c>0x10023220</c>):
/// <code>
/// u32 장 수 n — 장마다(맵이 움직이는 그림일 때 여러 장, 전투 맵은 1)
///   s16 cols, s16 rows
///   cols*rows 바이트  칸 높이(z = 값 × 20 — 파트2 「층」과 같은 단위, 맵 +0x68)
///   cols*rows 바이트  칸 지형(비트 1 걷는 유닛 금지 · 2 다른 이동 금지 · 4·8 계단·비탈 걸음, 맵 +0x6c)
///   s16 layerW, layerH, layerX, layerY — 띠 (i, j) 는 화면 ((layerX + i)·40, (layerY + j)·8), 칸 (c, r) 윗면은 (40c, 32r − 12A)
///   layerW*layerH × (u8 칸 줄, u8 높이, u16 타일) 바탕 배치표 — 윗층이 덮는 자리는 검은 타일로 비어 있다
///   u32 m, m × (s16 x, s16 y, s16 w, s16 h, w*h × (s8, s8, u16 타일))  윗층(띠 단위 자리) — 색 0 은 투명
///   u32 t, t × 640 바이트(40×8 RGB555), t × 1 바이트
/// u32 k, k × (u32, u32 c, c × (u16, u16, u16, 12바이트))  — 장 넘기기 차례(움직이는 그림)
/// 32 바이트, u16 j, j × 16 바이트
/// </code>
/// 파트2(<see cref="ObtMap"/>)의 33바이트 칸 레코드·u16 지형 격자 대신 바이트 두 장이고, 윗층이 그림의 일부다.
/// 시판 자료 343개 중 337개가 파일 끝까지 맞는다(작은 6개는 전투 맵이 아니다).
/// </remarks>
public static class Part1Map
{
    /// <summary>지형 값을 파트2 플래그에 담는 자리 — 낮은 비트는 파트2 뜻(0x1 못 들어감)으로, 원래 값은 이만큼 올려 함께 둔다.</summary>
    public const int RawShift = 8;

    /// <summary>파트1 폴더에서 그 맵을 읽는다 — 없거나 틀이 안 맞으면 null.</summary>
    public static ObtMapImage? Load(int id) =>
        Part1Game.Read("Obts", $"{id:D4}.obt") is { } bytes ? Parse(bytes) : null;

    public static ObtMapImage? Parse(byte[] bytes)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            if (reader.ReadInt32() < 1) return null;
            int cols = reader.ReadInt16(), rows = reader.ReadInt16();
            if (cols <= 0 || rows <= 0) return null;
            byte[] height = reader.ReadBytes(cols * rows), terrain = reader.ReadBytes(cols * rows);

            int layerW = reader.ReadInt16(), layerH = reader.ReadInt16(), layerX = reader.ReadInt16(), layerY = reader.ReadInt16();
            var baseTiles = ReadEntries(reader, layerW * layerH);
            int upperCount = reader.ReadInt32();
            var uppers = new List<(int X, int Y, int W, int H, int[] Tiles)>();
            for (int i = 0; i < upperCount; i++)
            {
                int x = reader.ReadInt16(), y = reader.ReadInt16(), w = reader.ReadInt16(), h = reader.ReadInt16();
                uppers.Add((x, y, w, h, ReadEntries(reader, w * h)));
            }
            int tileCount = reader.ReadInt32();
            byte[] tiles = reader.ReadBytes(tileCount * ObtMap.StripWidth * ObtMap.StripHeight * 2);
            if (tiles.Length != tileCount * ObtMap.StripWidth * ObtMap.StripHeight * 2) return null;

            // 원본은 띠 (열, 줄)을 세계 (layerX + 열)·40, (layerY + 줄)·8 에 그린다(0x1009765b·윗층 0x100973ef) — 그림이 layerX 띠만큼 <b>오른쪽</b>으로 간다.
            // 띠마다 붙은 (칸 줄, 높이) 바이트로 맞춰 보면 띠 열 i 가 칸 열 i + layerX 다(0050: 94% 맞음, −layerX 로 두면 59%). layerX 가 0 이 아닌 맵이 40개다
            // (−2~6). 전에는 거꾸로 왼쪽으로 당겨 그림이 칸과 2·layerX 칸 어긋났다(사용자 보고: btl 20087 = obt 0050, layerX 2).
            // 세계 x 0 왼쪽(음수 layerX)은 잘라 내고, 오른쪽으로 민 자리는 비워 둔다.
            int width = Math.Max(0, layerW + layerX) * ObtMap.StripWidth, imageHeight = layerH * ObtMap.StripHeight;
            var bgra = new byte[width * imageHeight * 4];
            void Put(int col, int row, int tile, bool keyed)
            {
                if (tile >= tileCount) return;
                int sx = (col + layerX) * ObtMap.StripWidth, sy = row * ObtMap.StripHeight;
                int src = tile * ObtMap.StripWidth * ObtMap.StripHeight * 2;
                for (int py = 0; py < ObtMap.StripHeight; py++)
                    for (int px = 0; px < ObtMap.StripWidth; px++, src += 2)
                    {
                        int c = tiles[src] | tiles[src + 1] << 8, x = sx + px, y = sy + py;
                        if ((keyed && c == 0) || (uint)x >= width || (uint)y >= imageHeight) continue;
                        int dst = (y * width + x) * 4;
                        bgra[dst] = (byte)((c & 0x1F) * 255 / 31);
                        bgra[dst + 1] = (byte)(((c >> 5) & 0x1F) * 255 / 31);
                        bgra[dst + 2] = (byte)(((c >> 10) & 0x1F) * 255 / 31);
                        bgra[dst + 3] = 255;
                    }
            }
            for (int i = 0; i < baseTiles.Length; i++) Put(i % layerW, i / layerW, baseTiles[i], keyed: false);
            foreach (var (x, y, w, _, list) in uppers)
                for (int i = 0; i < list.Length; i++) Put(x + i % w, y + i / w, list[i], keyed: true);

            var heights = new int[cols * rows];
            var flags = new ushort[cols * rows];
            var corners = new short[cols * rows * 4];
            for (int i = 0; i < heights.Length; i++)
            {
                heights[i] = height[i];
                flags[i] = (ushort)((terrain[i] & 1) | terrain[i] << RawShift);
                for (int k = 0; k < 4; k++) corners[i * 4 + k] = (short)(height[i] * 20);
            }
            return new ObtMapImage(width, imageHeight, layerY * ObtMap.StripHeight, cols, rows, bgra, heights, flags, corners, new byte[cols * rows]);
        }
        catch (EndOfStreamException) { return null; }
    }

    /// <summary>
    /// 맵 그림 띠의 깊이 격자 — 띠 칸(40×8)마다 그 자리에 그리는 띠들(바탕 → 윗층 차례)의 (칸 줄, 높이)와 그림.
    /// 원본은 맵도 맵 물체와 같은 그리기(<c>0x10097390</c>)로 띠 하나하나를 깊이 (줄·40, 높이·20) 로 그리기 목록에 넣어(바탕 <c>0x1009761a</c> ·
    /// 윗층 <c>0x100978d8</c> → <c>0x1001bc60</c>) 유닛과 함께 (y, z, 넣은 차례)로 그린다(<c>0x1001b7a0</c> → <c>0x1001ad10</c> → <c>0x1001ae70</c>) —
    /// 유닛보다 앞줄이거나 같은 줄에서 높은 띠(벽 · 기둥 · 지붕)가 유닛을 <b>불투명하게</b> 가린다. 반투명 · 실루엣은 없다.
    /// 리메이크는 맵 그림을 통째로 바탕에 깔고, 이 격자로 유닛을 가리는 띠만 유닛 위에 다시 그린다.
    /// </summary>
    /// <param name="keyBaseBlack">바탕 띠의 색 0 도 비치게 — 맵 장식(하늘)을 깐 판은 바탕의 검은 점을 뚫어 그린다.</param>
    public static Part1MapDepthGrid? LoadDepth(int id, bool keyBaseBlack) =>
        Part1Game.Read("Obts", $"{id:D4}.obt") is { } bytes ? ParseDepth(bytes, keyBaseBlack) : null;

    public static Part1MapDepthGrid? ParseDepth(byte[] bytes, bool keyBaseBlack)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            if (reader.ReadInt32() < 1) return null;
            int cols = reader.ReadInt16(), rows = reader.ReadInt16();
            if (cols <= 0 || rows <= 0) return null;
            reader.ReadBytes(cols * rows * 2);
            int layerW = reader.ReadInt16(), layerH = reader.ReadInt16(), layerX = reader.ReadInt16(), layerY = reader.ReadInt16();
            if (layerW <= 0 || layerH <= 0) return null;
            var entries = new List<(int Col, int Row, int Depth, int Tile, bool Upper)>();
            void Read(int x0, int y0, int w, int h, bool upper)
            {
                for (int i = 0; i < w * h; i++)
                {
                    int row = reader.ReadByte(), level = reader.ReadByte(), tile = reader.ReadUInt16();
                    // 띠 번호 0 은 원본이 안 그린다(0x100975c9 · 0x10097881).
                    if (tile != 0) entries.Add((x0 + i % w, y0 + i / w, row << 8 | level, tile, upper));
                }
            }
            Read(0, 0, layerW, layerH, false);
            int upperCount = reader.ReadInt32();
            for (int u = 0; u < upperCount; u++)
            {
                int x = reader.ReadInt16(), y = reader.ReadInt16(), w = reader.ReadInt16(), h = reader.ReadInt16();
                Read(x, y, w, h, true);
            }
            int tileCount = reader.ReadInt32();
            int stripPixels = ObtMap.StripWidth * ObtMap.StripHeight;
            byte[] tiles = reader.ReadBytes(tileCount * stripPixels * 2);
            if (tiles.Length != tileCount * stripPixels * 2) return null;

            var pictures = new Dictionary<(int, bool), uint[]>();
            uint[] Picture(int tile, bool keyed)
            {
                if (pictures.TryGetValue((tile, keyed), out var px)) return px;
                px = new uint[stripPixels];
                for (int p = 0, src = tile * stripPixels * 2; p < stripPixels; p++, src += 2)
                {
                    int c = tiles[src] | tiles[src + 1] << 8;
                    if (c == 0 && keyed) continue;
                    px[p] = 0xFF000000u | (uint)((c >> 10 & 0x1F) * 255 / 31) << 16 | (uint)((c >> 5 & 0x1F) * 255 / 31) << 8 | (uint)((c & 0x1F) * 255 / 31);
                }
                return pictures[(tile, keyed)] = px;
            }
            var cells = new List<Part1MapStrip>?[layerW * layerH];
            foreach (var (col, row, depth, tile, upper) in entries)
            {
                if (tile >= tileCount || (uint)col >= layerW || (uint)row >= layerH) continue;
                // 바탕은 Part1Map 그림처럼 색 0 도 검게 칠하고, 윗층은 색 0 이 비친다.
                (cells[row * layerW + col] ??= []).Add(new Part1MapStrip(depth >> 8, depth & 0xFF, Picture(tile, upper || keyBaseBlack)));
            }
            return new Part1MapDepthGrid(layerX, layerY, layerW, layerH, cells);
        }
        catch (EndOfStreamException) { return null; }
    }

    private static int[] ReadEntries(BinaryReader reader, int count)
    {
        var tiles = new int[count];
        for (int i = 0; i < count; i++)
        {
            reader.ReadInt16();                       // u8 두 개 — 그 띠의 칸 줄·높이(A). 원본은 유닛과 앞뒤를 가리는 깊이로만 쓴다(0x100978a2)
            tiles[i] = reader.ReadUInt16();
        }
        return tiles;
    }
}

/// <summary>맵 띠 하나 — 깊이(칸 줄, 높이 A)와 40×8 ARGB 그림(알파 0 이 비침).</summary>
public sealed record Part1MapStrip(int Row, int Level, uint[] Pixels);

/// <summary>
/// 맵 띠의 깊이 격자(<see cref="Part1Map.LoadDepth"/>). 띠 칸 (i, j) 는 세계 화면 ((<paramref name="Col0"/> + i)·40, (<paramref name="Row0"/> + j)·8) — 맵 그림과 같은 자리.
/// 칸마다 띠는 원본이 넣는 차례(바탕 → 윗층)다.
/// </summary>
public sealed record Part1MapDepthGrid(int Col0, int Row0, int Cols, int Rows, List<Part1MapStrip>?[] Cells);
