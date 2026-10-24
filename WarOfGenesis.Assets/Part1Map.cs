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
///   s16 layerW, layerH, layerX, layerY — 화면 x = 세계 x − layerX·40, y = 세계 y·32/40 − z·12/20 − layerY·8
///   layerW*layerH × (s8, s8, u16 타일) 바탕 배치표 — 윗층이 덮는 자리는 검은 타일로 비어 있다
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

            // 그림의 왼쪽 끝을 세계 x = 0 에 맞춘다 — 띠 열 layerX 가 그림의 0 열이다(파트2 맵은 layerX 가 0 이라 안 쓴다).
            int width = Math.Max(0, layerW - layerX) * ObtMap.StripWidth, imageHeight = layerH * ObtMap.StripHeight;
            var bgra = new byte[width * imageHeight * 4];
            void Put(int col, int row, int tile, bool keyed)
            {
                if (tile >= tileCount) return;
                int sx = (col - layerX) * ObtMap.StripWidth, sy = row * ObtMap.StripHeight;
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

    private static int[] ReadEntries(BinaryReader reader, int count)
    {
        var tiles = new int[count];
        for (int i = 0; i < count; i++)
        {
            reader.ReadInt16();                       // s8 두 개 — 윗층에서는 그리는 차례로 보인다(아직 모름)
            tiles[i] = reader.ReadUInt16();
        }
        return tiles;
    }
}
