using System.Text;

namespace WarOfGenesis.Assets;

/// <summary>파트1 <c>.btl</c> 인물 레코드(17바이트) — Chr, 처음에 숨김, 칸, 방향, 대장의 인물 차례, 편, 레벨, ?.</summary>
/// <param name="Hidden">≠0 이면 처음엔 맵에 없다 — 로더가 만든 뒤 바로 지운다(<c>0x1002d50f</c>, 증원으로 보임).</param>
/// <param name="Leader">대장의 인물 차례 번호 — 자기 차례면 대장, 아니면 그 대장에게 붙는다(<c>0x10036790</c>).</param>
/// <param name="Level">레벨 그대로(파트2 처럼 파티 레벨에 더하는 보정이 아니다 — 가설).</param>
/// <param name="Extra">숨김이 아닐 때 ≠0 이면 <c>0x100364b0(값)</c> 에 넘긴다 — 뜻은 아직 모른다(AI 방식으로 보임).</param>
public sealed record Part1BattleUnit(int ChrCode, int Hidden, int X, int Y, int Direction, int Leader, int Side, int Level, int Extra);

/// <summary>파트1 이벤트 하나 — 최대 발동 횟수, 조건들(모두 참이어야), 행동들. 명령은 코드 + 인자 6칸(14바이트)이다.</summary>
public sealed record Part1BattleEvent(int Index, int MaxFire, IReadOnlyList<ScriptCommand> Conditions, IReadOnlyList<ScriptCommand> Actions);

/// <summary>
/// 파트1(전작) 전투 한 판 — <c>Btls\NNNN.btl</c> 과 대사 <c>Btls\NNNN.dlg</c>.
/// </summary>
/// <remarks>
/// 옵시디안 분석 「분석-파트1-전투」(ba-p1-1), 로더 <c>alpha.dll 0x1002cb20</c>:
/// <code>
/// 머리 낱말 11 — 맵 Obt, 첫 화면 x·y, 제목·승리·패배 대사 줄, ?(80·120…), ?(0~6), BGM, 용병 고정?, 출전 고정?
/// u16 n, n × 6바이트  맵 물체(Obs?, x, y)
/// u16 n, n × 17바이트 인물(<see cref="Part1BattleUnit"/>)
/// u16 n, n × 7바이트  아군 배치 칸(x, y, u8 방향, ?)
/// u16 n, n × 9바이트  대전 판 인물 칸 — 이야기 전투는 안 쓴다
/// u16 n, 이벤트 n 개 — u16 최대 발동, u16 조건 수, (u16 코드 + i16 × 6) ×, u16 행동 수, (…) ×
/// </code>
/// 파트2(<see cref="BattleFile"/>)와 달리 절 개수 뒤의 빈 낱말이 없고, Map 파일 없이 Obt 를 바로 적고, 글은 TXR·Tlk 가 아니라 .dlg 줄 번호다.
/// 시판 자료 455개 중 453개가 파일 끝까지 딱 맞는다. 0018 은 대전 칸·이벤트 절이 없어 0개로 읽고, 0214 는 인물 100명이라 적고 자료가 모자라 못 읽는다(null).
/// </remarks>
public sealed record Part1Battle(int Id, int ObtId, int StartX, int StartY, int TitleLine, int WinLine, int LoseLine,
                                 int Ambient, int Weather, int Bgm, bool MercenariesFixed, bool SortieFixed,
                                 IReadOnlyList<(int Obs, int X, int Y)> Objects, IReadOnlyList<Part1BattleUnit> Units,
                                 IReadOnlyList<BattlePlacementCell> Placement, IReadOnlyList<Part1BattleEvent> Events)
{
    /// <summary>대사 줄(<c>.dlg</c>) — 행동 200 과 머리의 제목·승패 조건이 이 번호를 쓴다.</summary>
    public IReadOnlyList<string> Lines { get; init; } = [];

    /// <summary>그 줄 — 없으면 빈 문자열. 글 안의 「/n」 은 줄 바꿈이다.</summary>
    public string Line(int index) => index >= 0 && index < Lines.Count ? Lines[index] : "";

    /// <summary>파트1 폴더에서 그 판을 읽는다 — 폴더가 없거나 파일이 없으면 null.</summary>
    public static Part1Battle? Load(int id)
    {
        var battle = Parse(id, Part1Game.Read("Btls", $"{id:D4}.btl"));
        return battle is null ? null : battle with { Lines = ParseDialogue(Part1Game.Read("Btls", $"{id:D4}.dlg")) };
    }

    public static Part1Battle? Parse(int id, byte[]? b)
    {
        if (b is not { Length: >= 22 }) return null;
        int o = 0;
        bool Need(int n) => o + n <= b.Length;
        int H() { int v = BitConverter.ToInt16(b, o); o += 2; return v; }
        int U() { int v = BitConverter.ToUInt16(b, o); o += 2; return v; }
        int B() => b[o++];

        var h = Enumerable.Range(0, 11).Select(_ => H()).ToArray();
        var objects = new List<(int, int, int)>();
        var units = new List<Part1BattleUnit>();
        var placement = new List<BattlePlacementCell>();
        var events = new List<Part1BattleEvent>();

        if (!Need(2)) return null;
        int n = U();
        if (!Need(6 * n)) return null;
        for (int i = 0; i < n; i++) objects.Add((H(), H(), H()));

        if (!Need(2)) return null;
        n = U();
        if (!Need(17 * n)) return null;
        for (int i = 0; i < n; i++)
        {
            int chr = H(), hidden = H(), x = H(), y = H(), dir = B();
            units.Add(new Part1BattleUnit(chr, hidden, x, y, dir, Leader: H(), Side: H(), Level: H(), Extra: H()));
        }

        // 뒤쪽 절이 통째로 빠진 파일(0018 — 배치 칸 뒤가 없다)은 그 절을 0개로 읽는다.
        n = Need(2) ? U() : 0;
        if (!Need(7 * n)) return null;
        for (int i = 0; i < n; i++)
        {
            int x = H(), y = H(), dir = B();
            H();
            placement.Add(new BattlePlacementCell(x, y, dir));
        }

        n = Need(2) ? U() : 0;
        if (!Need(9 * n)) return null;
        o += 9 * n;                                     // 대전 판 인물 칸 — 이야기 전투는 안 쓴다

        int count = Need(2) ? H() : 0;
        for (int i = 0; i < count; i++)
        {
            if (!Need(4)) return null;
            int max = H(), nc = H();
            if (nc < 0 || !Need(14 * nc + 2)) return null;
            var conds = ReadCommands(b, ref o, nc);
            int na = H();
            if (na < 0 || !Need(14 * na)) return null;
            events.Add(new Part1BattleEvent(i, max, conds, ReadCommands(b, ref o, na)));
        }

        return new Part1Battle(id, h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7], h[8], h[9] != 0, h[10] != 0,
                               objects, units, placement, events);
    }

    private static List<ScriptCommand> ReadCommands(byte[] b, ref int o, int count)
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

    /// <summary>
    /// <c>.dlg</c> — u16 줄 수 · u16 글 바이트 수 · 줄마다 u16 시작 자리 · 글(바이트마다 비트 반전, cp949, 줄은 0 으로 끝). 빈 파일은 줄 수 0 뿐이다.
    /// </summary>
    public static IReadOnlyList<string> ParseDialogue(byte[]? b)
    {
        if (b is not { Length: >= 4 }) return [];
        int count = BitConverter.ToInt16(b, 0), length = BitConverter.ToInt16(b, 2);
        int text = 4 + 2 * count;
        if (count <= 0 || length < 0 || text + length > b.Length) return [];
        var plain = new byte[length];
        for (int i = 0; i < length; i++) plain[i] = (byte)~b[text + i];
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        var lines = new string[count];
        for (int i = 0; i < count; i++)
        {
            int start = Math.Clamp(BitConverter.ToInt16(b, 4 + 2 * i), 0, length);
            int end = Array.IndexOf(plain, (byte)0, start);
            lines[i] = cp949.GetString(plain, start, (end < 0 ? length : end) - start);
        }
        return lines;
    }
}
