using System.Collections.Concurrent;
using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>파트1 맵 물체의 한 장 — 칸 격자(높이 · 지형)와 그림. 그림 자리는 물체 기준점(칸 왼쪽 위, 물체 높이)에서 픽셀로.</summary>
/// <param name="Heights">칸 높이 A — 0 이 아니면 판 높이를 「물체 높이 + A」로 덮는다(<c>0x100997a3</c>). 0 이면 판 높이를 건드리지 않는다.</param>
/// <param name="Terrain">칸 지형 B — 판 지형 바이트를 <b>그대로 덮는다</b>(<c>0x1009977e</c>). 비트 1 걷기 금지 · 2 나는 유닛 설 수 없음 · 0x10 「그림만 걸친 칸」(물체 높이를 잴 때 뺀다, <c>0x100979c2</c>).</param>
/// <param name="Pixels">ARGB — 알파 0 이 비침. 띠 번호 0 은 안 그리고(<c>0x100975c9</c> · <c>0x10097881</c>), 색 0 도 비친다 [H: 맵 윗층과 같은 그리기 0x1001bc60].</param>
public sealed record Part1MapObjectFrame(int Cols, int Rows, byte[] Heights, byte[] Terrain, int X, int Y, int Width, int Height, uint[] Pixels);

/// <summary>맵 물체 동작의 키 — (틱, 길이, 종류, 인자 6). 꼴은 인물 Obs 의 키와 같고, 처리기는 바탕 틀의 <c>0x10098c70</c>(가상 +0xb4) 이다.</summary>
public sealed record Part1MapObjectKey(int Start, int Length, int Kind, short[] Args);

/// <summary>맵 물체 동작 하나 — 길이(틱)와 키. 끝나면 처음으로 돌아간다(<c>0x10098be0</c> → 같은 동작 다시, <c>0x10098af0</c>).</summary>
public sealed record Part1MapObjectMotion(int Length, IReadOnlyList<Part1MapObjectKey> Keys);

/// <summary>
/// 맵 물체가 받는 알림의 반응 — 성질 뒤 <c>u16 수</c> · 16바이트 × 수(<c>u16 알림, u16 반응, i16 × 6</c>). 물체의 알림 처리기 <c>0x10099d60</c>(가상 +0x48)가
/// 같은 알림의 항목마다 반응 0~13 을 가상 +0x74~+0xa8 로 부른다. 반응 6(<c>0x10099e1e</c>)은 보낸 이가 판의 유닛이고 조종 <c>+0x1f8</c> = 4(사람)일 때만
/// <c>0x1009a710</c> — 동작 19(여는 동작)를 걸고 다음 상태 6(동작이 끝나면 동작 5 = 열린 장). 상자 147 은 알림 2(곁에서 대기), 쇠 상자 324 는 알림 5(곁에서 대기 + 어빌리티 63).
/// </summary>
public sealed record Part1MapObjectReaction(int Message, int Action, short[] Args);

/// <summary>
/// 파트1 맵 물체 — 전투 파일 절 A 의 첫 낱말이 가리키는 <c>Obts\NNNN.obt</c>. 전투 맵과 <b>같은 Obt 꼴</b>이고 같은 로더(<c>0x10022bc0</c>)가 읽는다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 36절. 맵은 1장이지만 물체는 여러 장(통 245: 멀쩡 · 타는 중 · 타 버림 6장, 상자 324: 닫힘 · 열림, 문 120: 닫힘 · 빈 장 · 반쯤)이고
/// 동작표(장 넘기기 차례)가 장을 고른다. 칸 격자는 판에 찍는 발자국이다 — 그림이 없는 Obt 0260(1×1, 지형 1)·0270(10×1)·0262(5×3, 높이 20) 은
/// <b>보이지 않는 벽</b>이다. 물체마다 그림자(Obs 0355 둥근 · 0356 비스듬한)를 동작 키 종류 2 로 자식처럼 붙인다.
/// </remarks>
/// <param name="Properties">동작표 뒤 32바이트(낱말 8, 가상 +0x60 → Obt <c>+0x18</c>). 칸 6(<c>+0x18</c>) ≠ 0 이면 타는 동작(20~22 · 26~34) 동안 곁의 유닛을 다치게 한다
/// (<c>0x10097b04</c>) — 나무 233 만 1. 칸 0~2 는 [H: 불붙는 온도 · 열 받는 정도] (통 80·128·80, 상자 150·10·10, 문 120 30000·100·100). 리메이크는 아직 안 쓴다.</param>
public sealed record Part1MapObject(int Id, IReadOnlyList<Part1MapObjectFrame> Frames, IReadOnlyList<Part1MapObjectMotion?> Motions, int[] Properties)
{
    /// <summary>알림 반응표(<see cref="Part1MapObjectReaction"/>) — 성질 뒤에 없으면 빈 목록.</summary>
    public IReadOnlyList<Part1MapObjectReaction> Reactions { get; init; } = [];

    /// <summary>
    /// 상자의 든 것 — 반응 6(열기)이 알림 2 · 5 에 걸려 있고 여는 동작 19 에 키 6(곁 유닛 알림)이 알림 12(아이템) · 13(eld)을 보내는 Obt.
    /// 상자가 아니면 null. <c>Item</c> 0 = 무작위(<c>0x100330f0</c> 의 표), <c>Gold</c> 는 eld. <c>Locked</c> = 알림 5 에만 반응(곁에서 대기 + 어빌리티 63 「잠긴상자열기」).
    /// </summary>
    /// <remarks>
    /// 키 6 의 인자: a0 알림(12 · 13) · a1 3 · a2 0 · a3 1 · a4 1 [H: 범위] · a5 값(아이템 번호 · eld). 원본 키 처리기 <c>0x10098d53</c> 가 범위의 유닛마다
    /// <c>0x1009d440(유닛, a0, 0, a5)</c>, 유닛은 그것을 전투로 넘기고(<c>0x100355dd</c> · <c>0x100355fd</c>) 전투가 준다(<c>0x100330f0</c> · <c>0x10033450</c>).
    /// </remarks>
    public (int Item, int Gold, bool Locked)? Chest
    {
        get
        {
            var open = Reactions.FirstOrDefault(r => r.Action == 6 && r.Message is 2 or 5);
            if (open is null || Motion(OpenMotion)?.Keys.FirstOrDefault(k => k.Kind == 6 && k.Args[0] is 12 or 13) is not { } loot) return null;
            return loot.Args[0] == 12 ? (loot.Args[5], 0, open.Message == 5) : (0, loot.Args[5], open.Message == 5);
        }
    }

    /// <summary>
    /// 든 것이 0(무작위)인 상자의 아이템 — <c>0x100330f0</c>: <c>rand() % 139</c> 로 다섯 묶음 가운데 하나, 묶음 안은 고르게
    /// (0~34 → 5 마다 · 35~62 → 4 마다 · 63~95 → 3 마다 · 96~119 → 2 마다 · 120~138 → 하나씩). 앞 묶음일수록 한 아이템이 흔하다.
    /// </summary>
    public static int RandomChestItem(Random rng)
    {
        int r = rng.Next(139);
        return r switch
        {
            < 35 => RandomTiers[0][r / 5],
            < 63 => RandomTiers[1][(r - 35) / 4],
            < 96 => RandomTiers[2][(r - 63) / 3],
            < 120 => RandomTiers[3][(r - 96) / 2],
            _ => RandomTiers[4][r - 120],
        };
    }

    private static readonly int[][] RandomTiers =
    [
        [0x81, 0x83, 0x84, 0x82, 0x0d, 0x79, 0x7e],
        [0x01, 0x4c, 0x7a, 0x7b, 0x7c, 0x7d, 0x7f],
        [0x0e, 0x23, 0x06, 0x02, 0x0f, 0x2d, 0x4d, 0x71, 0x52, 0x42, 0x32],
        [0x07, 0x1b, 0x58, 0x59, 0x5a, 0x5b, 0x5c, 0x75, 0x76, 0x77, 0x33, 0x78],
        [0x11, 0x1c, 0x26, 0x2e, 0x34, 0x35, 0x44, 0x46, 0x47, 0x48, 0x4f, 0x55, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x72],
    ];

    /// <summary>상자를 여는 동작(<c>0x1009a710</c> 이 거는 19) · 다 연 뒤 도는 동작(상태 6 → <c>0x1009a8ec</c> 의 5 = 열린 장).</summary>
    public const int OpenMotion = 19, OpenedMotion = 5;

    /// <summary>그 동작 — 없으면(길이 −1) null.</summary>
    public Part1MapObjectMotion? Motion(int motion) => (uint)motion < (uint)Motions.Count ? Motions[motion] : null;

    /// <summary>
    /// 그 동작을 걸 때의 장 — 키 가운데 처음 나오는 종류 0 의 인자 0(<c>0x10098af0</c>). 종류 0 키가 없거나 동작이 없으면 −1(장을 안 바꾼다).
    /// </summary>
    public int FirstFrame(int motion) => Motion(motion)?.Keys.FirstOrDefault(k => k.Kind == 0) is { } k ? k.Args[0] : -1;

    private static readonly ConcurrentDictionary<int, Part1MapObject?> Cache = new();

    /// <summary>파트1 폴더에서 그 물체를 읽는다(한 번 읽으면 기억) — 없거나 틀이 안 맞으면 null.</summary>
    public static Part1MapObject? Load(int id) =>
        Cache.GetOrAdd(id, n => Part1Game.Read("Obts", $"{n:D4}.obt") is { } bytes ? Parse(n, bytes) : null);

    public static Part1MapObject? Parse(int id, byte[] bytes)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            int frameCount = reader.ReadInt32();
            if (frameCount is < 1 or > 256) return null;
            var frames = new List<Part1MapObjectFrame>();
            for (int f = 0; f < frameCount; f++)
            {
                int cols = reader.ReadInt16(), rows = reader.ReadInt16();
                if (cols <= 0 || rows <= 0) return null;
                byte[] heights = reader.ReadBytes(cols * rows), terrain = reader.ReadBytes(cols * rows);
                int layerW = reader.ReadInt16(), layerH = reader.ReadInt16(), layerX = reader.ReadInt16(), layerY = reader.ReadInt16();
                // 띠 (열, 줄, 타일) — 바탕은 (layerX + i, layerY + j), 윗층은 (layerX + 윗층 x + i, layerY + 윗층 y + j) 에 그린다(0x100973ef · 0x10097681).
                var strips = new List<(int Col, int Row, int Tile)>();
                void ReadStrips(int x0, int y0, int w, int h)
                {
                    for (int i = 0; i < w * h; i++)
                    {
                        reader.ReadInt16();                   // u8 두 개 — 그 띠의 칸 줄·높이. 원본은 유닛과 앞뒤를 가리는 깊이로 쓴다(0x100975e6)
                        int tile = reader.ReadUInt16();
                        if (tile != 0) strips.Add((x0 + i % w, y0 + i / w, tile));
                    }
                }
                ReadStrips(layerX, layerY, layerW, layerH);
                int upperCount = reader.ReadInt32();
                for (int u = 0; u < upperCount; u++)
                {
                    int x = reader.ReadInt16(), y = reader.ReadInt16(), w = reader.ReadInt16(), h = reader.ReadInt16();
                    ReadStrips(layerX + x, layerY + y, w, h);
                }
                int tileCount = reader.ReadInt32();
                int stripBytes = ObtMap.StripWidth * ObtMap.StripHeight * 2;
                byte[] tiles = reader.ReadBytes(tileCount * stripBytes);
                reader.ReadBytes(tileCount);
                if (tiles.Length != tileCount * stripBytes) return null;
                strips.RemoveAll(s => s.Tile >= tileCount);

                int minC = strips.Count > 0 ? strips.Min(s => s.Col) : 0, maxC = strips.Count > 0 ? strips.Max(s => s.Col) + 1 : 0;
                int minR = strips.Count > 0 ? strips.Min(s => s.Row) : 0, maxR = strips.Count > 0 ? strips.Max(s => s.Row) + 1 : 0;
                int width = (maxC - minC) * ObtMap.StripWidth, height = (maxR - minR) * ObtMap.StripHeight;
                var px = new uint[width * height];
                foreach (var (col, row, tile) in strips)
                {
                    int src = tile * stripBytes, sx = (col - minC) * ObtMap.StripWidth, sy = (row - minR) * ObtMap.StripHeight;
                    for (int y = 0; y < ObtMap.StripHeight; y++)
                        for (int x = 0; x < ObtMap.StripWidth; x++, src += 2)
                        {
                            int c = tiles[src] | tiles[src + 1] << 8;
                            if (c == 0) continue;
                            px[(sy + y) * width + sx + x] = 0xFF000000u | (uint)((c >> 10 & 0x1F) * 255 / 31) << 16
                                                            | (uint)((c >> 5 & 0x1F) * 255 / 31) << 8 | (uint)((c & 0x1F) * 255 / 31);
                        }
                }
                frames.Add(new Part1MapObjectFrame(cols, rows, heights, terrain, minC * ObtMap.StripWidth, minR * ObtMap.StripHeight, width, height, px));
            }

            int motionCount = reader.ReadInt32();
            if (motionCount is < 0 or > 1024) return null;
            var motions = new List<Part1MapObjectMotion?>();
            for (int m = 0; m < motionCount; m++)
            {
                int length = reader.ReadInt32(), keyCount = reader.ReadInt32();
                var keys = new List<Part1MapObjectKey>();
                for (int k = 0; k < keyCount; k++)
                {
                    int start = reader.ReadUInt16(), len = reader.ReadUInt16(), kind = reader.ReadUInt16();
                    var args = new short[6];
                    for (int a = 0; a < 6; a++) args[a] = reader.ReadInt16();
                    keys.Add(new Part1MapObjectKey(start, len, kind, args));
                }
                motions.Add(length < 0 ? null : new Part1MapObjectMotion(length, keys));
            }
            var props = new int[8];
            for (int i = 0; i < 8; i++) props[i] = reader.ReadInt32();
            var reactions = new List<Part1MapObjectReaction>();
            if (reader.BaseStream.Length - reader.BaseStream.Position >= 2)
            {
                int count = reader.ReadUInt16();
                for (int r = 0; r < count && reader.BaseStream.Length - reader.BaseStream.Position >= 16; r++)
                {
                    int message = reader.ReadUInt16(), action = reader.ReadUInt16();
                    var args = new short[6];
                    for (int a = 0; a < 6; a++) args[a] = reader.ReadInt16();
                    reactions.Add(new Part1MapObjectReaction(message, action, args));
                }
            }
            return new Part1MapObject(id, frames, motions, props) { Reactions = reactions };
        }
        catch (EndOfStreamException) { return null; }
    }
}
