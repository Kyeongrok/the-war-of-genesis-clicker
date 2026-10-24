namespace WarOfGenesis.Assets;

/// <summary>파트1 챕터 지도의 장소 하나(낱말 14).</summary>
/// <param name="NameLine">낱말 0 — <c>.clg</c> 의 이 줄이 장소 이름이다.</param>
/// <param name="Kind">낱말 6 — 0 전투 · 1 마을(성·거점).</param>
/// <param name="Target">낱말 7 — 전투면 <c>Btls</c> 번호, 마을이면 마을 번호, -1 이면 길목.</param>
/// <param name="Words">낱말 14 그대로(1·2 지도 자리, 5 먼저 거칠 장소, 9~13 잇닿은 장소 — 가설).</param>
public sealed record Part1ChapterPlace(int NameLine, int X, int Y, int Kind, int Target, short[] Words)
{
    public bool IsBattle => Kind == 0 && Target >= 0;
    public bool IsTown => Kind == 1 && Target >= 0;
}

/// <summary>
/// 파트1 챕터 <c>Chps\NNNN.cpr</c>(지도·장소·이벤트)와 <c>.clg</c>(글) — 로더 <c>alpha.dll 0x100b31bb</c>.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-챕터」. 머리 13낱말(마지막이 장소 수) · 장소 28바이트 × N · u16 M · M 낱말 · M × (u16 k, k × 짝) · u16 P · 12바이트 × P ·
/// 이벤트(전투와 같은 꼴, 명령 14바이트 — 다만 코드 묶음은 챕터용). 시판 89개 중 88개를 읽는다.
/// <c>.clg</c> 줄 0 은 챕터 제목, 1 은 지역 이름이다.
/// </remarks>
public sealed record Part1Chapter(int Id, short[] Header, IReadOnlyList<Part1ChapterPlace> Places, IReadOnlyList<Part1BattleEvent> Events)
{
    public IReadOnlyList<string> Lines { get; init; } = [];

    public string Title => Lines.Count > 0 ? Lines[0] : "";
    public string Region => Lines.Count > 1 ? Lines[1] : "";
    public string NameOf(Part1ChapterPlace place) => place.NameLine >= 0 && place.NameLine < Lines.Count ? Lines[place.NameLine] : "";

    /// <summary>이 챕터의 전투 번호들(장소 차례).</summary>
    public IEnumerable<int> Battles => Places.Where(p => p.IsBattle).Select(p => p.Target);

    public static Part1Chapter? Load(int id)
    {
        var chapter = Parse(id, Part1Game.Read("Chps", $"{id:D4}.cpr"));
        return chapter is null ? null : chapter with { Lines = Part1Battle.ParseDialogue(Part1Game.Read("Chps", $"{id:D4}.clg")) };
    }

    public static Part1Chapter? Parse(int id, byte[]? b)
    {
        if (b is not { Length: >= 26 }) return null;
        int o = 0;
        bool Need(int n) => o + n <= b.Length;
        short H() { short v = BitConverter.ToInt16(b, o); o += 2; return v; }

        var header = Enumerable.Range(0, 13).Select(_ => H()).ToArray();
        int count = header[12];
        if (count < 0 || !Need(28 * count)) return null;
        var places = new List<Part1ChapterPlace>(count);
        for (int i = 0; i < count; i++)
        {
            var w = Enumerable.Range(0, 14).Select(_ => H()).ToArray();
            places.Add(new Part1ChapterPlace(w[0], w[1], w[2], w[6], w[7], w));
        }

        if (!Need(2)) return null;
        int m = H();
        if (m < 0 || !Need(2 * m)) return null;
        o += 2 * m;
        for (int i = 0; i < m; i++)
        {
            if (!Need(2)) return null;
            int k = H();
            if (k < 0 || !Need(4 * k)) return null;
            o += 4 * k;
        }
        if (!Need(2)) return null;
        int p = H();
        if (p < 0 || !Need(12 * p)) return null;
        o += 12 * p;

        var events = new List<Part1BattleEvent>();
        int n = Need(2) ? H() : 0;
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
        return new Part1Chapter(id, header, places, events);
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
