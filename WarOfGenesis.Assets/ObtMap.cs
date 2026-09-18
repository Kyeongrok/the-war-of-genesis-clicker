using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>
/// 전투 맵 그림 한 장 — BGRA 픽셀과 전투 칸 수. 칸 하나는 <see cref="ObtMap.CellWidth"/>×<see cref="ObtMap.CellHeight"/> 픽셀이다.
/// </summary>
/// <param name="OriginY">그림의 맨 윗줄이 칸 판의 몇 픽셀 자리에 오는지(음수면 판 위로 삐져나간다).</param>
/// <param name="Heights">칸 높이(칸 수 = Cols×Rows, 행 우선) = (칸 레코드 첫 u16 + 19) / 20. 지형 비트 0x10 칸은 0.</param>
/// <param name="Flags">칸 지형 플래그(칸 레코드 뒤 u16 격자). <c>&amp; 0x9</c> 면 못 들어가고, <c>&amp; 0x8</c> 이면 공격 범위에서도 빠진다.</param>
public sealed record ObtMapImage(int Width, int Height, int OriginY, int Cols, int Rows, byte[] Bgra, int[] Heights, ushort[] Flags)
{
    /// <summary>맵 밖은 높이 0 · 막힌 칸(플래그 8)으로 본다 — 판이 맵보다 클 수 있다(전투마다 맵 크기가 다르다).</summary>
    public int HeightAt(int col, int row) => Inside(col, row) ? Heights[row * Cols + col] : 0;
    public ushort FlagsAt(int col, int row) => Inside(col, row) ? Flags[row * Cols + col] : (ushort)0x8;

    private bool Inside(int col, int row) => (uint)col < Cols && (uint)row < Rows;
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
/// u32 n, n × (u16, u16, s16 w, s16 h, w*h*4 바이트)  — 덧그림
/// u32 tileCount, tileCount × 640 바이트(40×8 RGB555), tileCount × 1 바이트
/// version ≥ 3: u32 n, n × (s16 w, s16 h, u16×3, w*h 바이트)
/// </code>
/// 전투 칸은 40×32 픽셀이다 — 원본은 640×480 화면에서 커서 칸이 40×32 이고, 32 칸 × 40 = 1280 이
/// 배치표 폭과 맞는다. <c>layerY</c>(0153 은 −2)는 띠 두 줄(16픽셀)만큼 그림을 위로 올린다.
/// 배치표 레코드의 앞 두 바이트(s8 두 개)와 덧그림·마지막 절의 뜻은 아직 모른다 — 그림에는 안 쓴다.
/// </remarks>
public static class ObtMap
{
    public const int CellWidth = 40, CellHeight = 32;
    private const int StripWidth = 40, StripHeight = 8;

    public static ObtMapImage Load(string path) => Parse(File.ReadAllBytes(path), Path.GetFileName(path));

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
        for (int i = 0; i < heights.Length; i++)
        {
            heights[i] = (reader.ReadUInt16() + 19) / 20;
            reader.BaseStream.Seek(cellRecordSize - 2, SeekOrigin.Current);
        }
        for (int i = 0; i < flags.Length; i++)
        {
            ushort f = reader.ReadUInt16();
            if ((f & 0x10) != 0) { heights[i] = 0; f = 0; }
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

        uint overlays = reader.ReadUInt32();
        for (int i = 0; i < overlays; i++)
        {
            reader.ReadUInt16(); reader.ReadUInt16();
            int w = reader.ReadInt16(), h = reader.ReadInt16();
            reader.BaseStream.Seek((long)w * h * 4, SeekOrigin.Current);
        }

        int tileCount = (int)reader.ReadUInt32();
        byte[] tiles = reader.ReadBytes(tileCount * StripWidth * StripHeight * 2);
        if (tiles.Length != tileCount * StripWidth * StripHeight * 2)
            throw new InvalidDataException($"{Path.GetFileName(path)}: 타일 자료가 잘렸습니다.");

        int width = layerW * StripWidth, height = layerH * StripHeight;
        var bgra = new byte[width * height * 4];
        for (int ly = 0; ly < layerH; ly++)
        {
            for (int lx = 0; lx < layerW; lx++)
            {
                int id = tileIds[ly * layerW + lx];
                if (id >= tileCount) continue;
                int src = id * StripWidth * StripHeight * 2;
                for (int py = 0; py < StripHeight; py++)
                {
                    int dst = ((ly * StripHeight + py) * width + lx * StripWidth) * 4;
                    for (int px = 0; px < StripWidth; px++, src += 2, dst += 4)
                    {
                        int c = tiles[src] | (tiles[src + 1] << 8);
                        bgra[dst] = (byte)(((c >> 0) & 0x1F) * 255 / 31);
                        bgra[dst + 1] = (byte)(((c >> 5) & 0x1F) * 255 / 31);
                        bgra[dst + 2] = (byte)(((c >> 10) & 0x1F) * 255 / 31);
                        bgra[dst + 3] = 255;
                    }
                }
            }
        }

        return new ObtMapImage(width, height, layerY * StripHeight, cols, rows, bgra, heights, flags);
    }
}
