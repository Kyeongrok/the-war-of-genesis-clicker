namespace WarOfGenesis.Assets;

/// <summary>스크립트 명령 한 줄(18바이트) — u16 코드 + i16 인자 8칸. 조건과 행동이 같은 꼴이다.</summary>
public sealed record ScriptCommand(int Code, short[] Args)
{
    /// <summary>뒤쪽 0 을 뗀 인자 목록(보기용). 다 0 이면 빈 문자열.</summary>
    public string ArgsText()
    {
        int n = Args.Length;
        while (n > 0 && Args[n - 1] == 0) n--;
        return n == 0 ? "" : "(" + string.Join(", ", Args.Take(n)) + ")";
    }
}

/// <summary>Btl 이벤트 하나 — 최대 발동 횟수(0·음수 = 제한 없음으로 보임), 둘째 워드, 조건들(모두 참이어야), 행동들.</summary>
public sealed record BattleEvent(int Index, int MaxFire, int Word2, IReadOnlyList<ScriptCommand> Conditions, IReadOnlyList<ScriptCommand> Actions);

/// <summary>
/// <c>Btl/NNNN.btl</c> 끝의 이벤트 절(ba-1 D 절) 읽기와, 뜻이 밝혀진 행동 코드 이름.
/// </summary>
/// <remarks>
/// 배치(옵시디안 분석-전투 ba-1, <c>tools/re/btl_dump.py</c>): 머리 u16×10 · (nA, u16) + nA×29 · (nB, u16) + nB×22 ·
/// (nC, u16) + nC×12 · u16 이벤트 수 · 이벤트마다 u16 최대 발동, u16 ?, u16 조건 수, (u16 코드 + 16B)×, u16 행동 수, (u16 코드 + 16B)×.
/// 조건 검사 <c>0x10056400</c>, 행동 해석기 점프표 <c>0x100567f0</c>.
/// 뜻을 아는 행동: 10 이어서 다음 전투(인자0 = Btl), 6 끝나고 필드로(인자0 = Fld) — 분석-전투목록;
/// 600 대사 상자(말하는 이 0, Tlk 줄 2, 음성 3, 얼굴 4), 601 말풍선(줄 2), 500·501 유닛 자리 소리(인자0), 512 배경음악(인자0) — 분석-사운드.
/// 조건 코드(1·3·301·401 …)의 뜻은 아직 안 풀렸다.
/// </remarks>
public static class BattleEvents
{
    /// <summary>이벤트 절을 읽는다. 틀이 안 맞으면(옛 형식·빈 파일) null. 파일 끝까지 딱 맞는지는 <paramref name="exact"/> 로 알린다.</summary>
    public static IReadOnlyList<BattleEvent>? Parse(byte[]? b) => Parse(b, out _);

    public static IReadOnlyList<BattleEvent>? Parse(byte[]? b, out bool exact)
    {
        exact = false;
        if (b == null || b.Length < 24) return null;
        int o = 20;
        bool Need(int n) => o + n <= b.Length;
        int H() { short v = BitConverter.ToInt16(b, o); o += 2; return v; }

        if (!Need(4)) return null;
        int na = H() & 0xFFFF; H();
        if (!Need(29 * na)) return null;
        o += 29 * na;
        if (!Need(4)) return null;
        int nb = H() & 0xFFFF; H();
        if (!Need(22 * nb)) return null;
        o += 22 * nb;
        if (!Need(4)) return null;
        int nc = H() & 0xFFFF; H();
        if (!Need(12 * nc)) return null;
        o += 12 * nc;
        if (!Need(2)) return null;
        int nd = H();

        var events = new List<BattleEvent>();
        for (int i = 0; i < nd; i++)
        {
            if (!Need(6)) return null;
            int max = H(), w2 = H(), n1 = H();
            if (n1 < 0 || !Need(18 * n1 + 2)) return null;
            var conds = ReadCommands(b, ref o, n1);
            int n2 = H();
            if (n2 < 0 || !Need(18 * n2)) return null;
            var acts = ReadCommands(b, ref o, n2);
            events.Add(new BattleEvent(i, max, w2, conds, acts));
        }
        exact = o == b.Length;
        return events;
    }

    private static List<ScriptCommand> ReadCommands(byte[] b, ref int o, int count)
    {
        var list = new List<ScriptCommand>(count);
        for (int k = 0; k < count; k++, o += 18)
        {
            var args = new short[8];
            for (int j = 0; j < 8; j++) args[j] = BitConverter.ToInt16(b, o + 2 + 2 * j);
            list.Add(new ScriptCommand(BitConverter.ToUInt16(b, o), args));
        }
        return list;
    }

    /// <summary>뜻을 아는 행동 코드의 이름. 모르면 빈 문자열.</summary>
    public static string ActionName(int code) => code switch
    {
        6 => "끝나고 필드로",
        10 => "이어서 다음 전투",
        500 or 501 => "유닛 자리 소리",
        512 => "배경음악",
        600 => "대사 상자",
        601 => "말풍선",
        _ => "",
    };
}
