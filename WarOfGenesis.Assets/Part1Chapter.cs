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

    /// <summary>
    /// 지도 말(적 무리로 보인다)의 처음 장소 — 장소 뒤 첫 목록(u16 M, M 낱말). 챕터 10 「시지아의 결전」: 16 · 14 · 15.
    /// 사건 900 [말(1부터), 장소] 이 옮기고, 조건 301 · 302 가 본다(분석-파트1-챕터 4절). −1 은 안 쓰는 칸.
    /// </summary>
    public IReadOnlyList<int> Markers { get; init; } = [];

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
        var markers = Enumerable.Range(0, m).Select(_ => (int)H()).ToList();
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
        return new Part1Chapter(id, header, places, events) { Markers = markers };
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

/// <summary>
/// 파트1 에피소드(연대표 칸) — <c>Eps\episode.eps</c>, 로더 <c>alpha.dll 0x100a7c40</c>. 이야기 줄기 3개, 줄기마다 u16 수 + 34바이트 칸.
/// </summary>
/// <param name="Line">이야기 줄기 0~2(살라딘 쪽 · 바이올라 쪽 · 인페르노 쪽).</param>
/// <param name="Row">바이트 0 — 연대표의 줄(1~3).</param>
/// <param name="X">u16 +2 — 연대표 위 가로 자리(20 단위).</param>
/// <param name="ChapterId">낱말 3(+0x1e) — 이 칸이 여는 <see cref="Part1Chapter"/> 번호.</param>
/// <param name="Words">+0x18 의 낱말 다섯 그대로(0 앞 칸? · 1 540~ · 2 줄기 첫 칸 표시 · 3 챕터 · 4 다른 챕터?).</param>
public sealed record Part1Episode(int Line, int Index, int Row, int X, string Name, int ChapterId, short[] Words)
{
    public const int Size = 34;

    public static IReadOnlyList<Part1Episode> LoadAll() => Parse(Part1Game.Read("Eps", "episode.eps"));

    public static IReadOnlyList<Part1Episode> Parse(byte[]? b)
    {
        var list = new List<Part1Episode>();
        if (b is null) return list;
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var cp949 = System.Text.Encoding.GetEncoding(949);
        int o = 0;
        for (int line = 0; line < 3 && o + 2 <= b.Length; line++)
        {
            int count = BitConverter.ToUInt16(b, o);
            o += 2;
            for (int i = 0; i < count && o + Size <= b.Length; i++, o += Size)
            {
                var plain = new byte[20];
                for (int k = 0; k < 20; k++) plain[k] = (byte)~b[o + 4 + k];
                int end = Array.IndexOf(plain, (byte)0);
                var words = Enumerable.Range(0, 5).Select(k => BitConverter.ToInt16(b, o + 24 + 2 * k)).ToArray();
                list.Add(new Part1Episode(line, i, b[o], BitConverter.ToUInt16(b, o + 2), cp949.GetString(plain, 0, end < 0 ? 20 : end), words[3], words));
            }
        }
        return list;
    }
}
