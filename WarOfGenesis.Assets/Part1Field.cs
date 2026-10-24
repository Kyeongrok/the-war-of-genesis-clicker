namespace WarOfGenesis.Assets;

/// <summary>파트1 필드 인물 칸(12바이트) — 사건이 <c>10000 + 차례</c> 로 가리킨다.</summary>
/// <param name="SpriteId">낱말 0 — <c>Obss</c> 그림 번호(살라딘 196 · 마르자나 352 · 발라 674 · 무카파 682, 인물 레코드의 그림 칸과 같다).</param>
/// <param name="Direction">낱말 3 — 방향(전투 인물 칸과 같은 번호로 보인다).</param>
/// <param name="Words">낱말 여섯 그대로(4·5 는 자료에서 늘 -1).</param>
public sealed record Part1FieldSprite(int SpriteId, int X, int Y, int Direction, short[] Words);

/// <summary>
/// 파트1 필드 <c>Flds\NNNN.fld</c>(+ 글 <c>.flg</c>) — 챕터 사건 503 이 여는 연출 장면(장면 종류 5). 로더 <c>alpha.dll 0x100c77f0</c>.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-챕터」 7절. 머리 낱말 6(0 <b>배경 <c>BGRs</c> 번호</b> — 0002 는 1300×500 파노라마 · 5 인물 수) ·
/// 인물 12바이트 × n · u16 사건 수 · 사건마다 (u16 최대 발동, u16 조건 수, 조건 14바이트씩, u16 행동 수, 행동 14바이트씩) — 챕터·전투 사건과 같은 꼴.
/// 시판 231개 중 229개가 파일 끝과 맞는다. 파트2 필드(<see cref="FieldFile"/>)와 꼴이 달라 따로 읽는다.
/// </remarks>
public sealed record Part1Field(int Id, short[] Header, IReadOnlyList<Part1FieldSprite> Sprites, IReadOnlyList<Part1BattleEvent> Events)
{
    public IReadOnlyList<string> Lines { get; init; } = [];

    /// <summary>배경 <c>BGRs\NNNN.bgr</c> 번호.</summary>
    public int Background => Header[0];

    public static Part1Field? Load(int id)
    {
        var field = Parse(id, Part1Game.Read("Flds", $"{id:D4}.fld"));
        return field is null ? null : field with { Lines = Part1Battle.ParseDialogue(Part1Game.Read("Flds", $"{id:D4}.flg")) };
    }

    public static Part1Field? Parse(int id, byte[]? b)
    {
        if (b is not { Length: >= 14 }) return null;
        int o = 0;
        bool Need(int n) => o + n <= b.Length;
        short H() { short v = BitConverter.ToInt16(b, o); o += 2; return v; }

        var header = Enumerable.Range(0, 6).Select(_ => H()).ToArray();
        int count = header[5];
        if (count < 0 || !Need(12 * count + 2)) return null;
        var sprites = new List<Part1FieldSprite>(count);
        for (int i = 0; i < count; i++)
        {
            var w = Enumerable.Range(0, 6).Select(_ => H()).ToArray();
            sprites.Add(new Part1FieldSprite(w[0], w[1], w[2], w[3], w));
        }

        var events = new List<Part1BattleEvent>();
        int n = H();
        for (int i = 0; i < n; i++)
        {
            if (!Need(4)) break;
            int max = H(), nc = H();
            if (nc < 0 || !Need(14 * nc + 2)) break;
            var conds = Commands(b, ref o, nc);
            int na = H();
            if (na < 0 || !Need(14 * na)) break;
            events.Add(new Part1BattleEvent(i, max, conds, Commands(b, ref o, na)));
        }
        return new Part1Field(id, header, sprites, events);
    }

    private static List<ScriptCommand> Commands(byte[] b, ref int o, int count)
    {
        var list = new List<ScriptCommand>(count);
        for (int k = 0; k < count; k++, o += 14)
        {
            var args = new short[6];
            for (int j = 0; j < 6; j++) args[j] = BitConverter.ToInt16(b, o + 2 + 2 * j);
            list.Add(new ScriptCommand(BitConverter.ToUInt16(b, o), args));
        }
        return list;
    }
}
