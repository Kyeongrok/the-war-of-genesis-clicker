namespace WarOfGenesis.Assets;

/// <summary>
/// <c>Dat\Lev.dat</c> — 레벨마다 붙는 성장률(%)과, <c>Dat\0002.nch</c> — 그 성장을 <b>안 받는</b> 인물들.
/// </summary>
/// <remarks>
/// 분석-전투 「전투 시작 TP · Lev.dat 성장」. 파일은 머리 낱말 셋 뒤에 <b>14바이트 × 200</b> 이 이어지고,
/// 레코드는 <c>+4</c> 레벨 · <c>+6</c> LP% · <c>+8</c> TP% · <c>+0xa</c> PSY% · <c>+0xc</c> DEX% · <c>+0xe</c> DEP% 다.
/// 레벨 설정(<c>0x1007a8e0</c>)은 <b>늘 <c>.chr</c> 원본에서</b> 다시 셈하므로 두 번 먹여도 쌓이지 않고,
/// <b>TP 제수와 CTP 는 건드리지 않는다</b>.
/// <para>
/// <c>0002.nch</c> 는 (슬롯, Chr) 25쌍이고 그 <b>Chr</b> 들은 파티 레벨 성장을 <b>면제</b>받는다(<c>0x10031890</c>).
/// </para>
/// </remarks>
public sealed record LevelGrowth(int Lp, int Tp, int Psy, int Dex, int Dep)
{
    /// <summary>레벨 1~200 의 성장률. 레벨이 넘치면 마지막 줄을 쓴다.</summary>
    public static IReadOnlyList<LevelGrowth> Parse(byte[]? b)
    {
        var rows = new List<LevelGrowth>();
        if (b == null || b.Length < 20) return rows;
        int count = BitConverter.ToUInt16(b, 2);
        for (int i = 0; i < count; i++)
        {
            int o = 6 + 14 * i;
            if (o + 14 > b.Length) break;
            ushort U(int k) => BitConverter.ToUInt16(b, o + k);
            rows.Add(new LevelGrowth(U(2), U(4), U(6), U(8), U(10)));
        }
        return rows;
    }

    /// <summary>파티 레벨 성장을 안 받는 Chr 번호들(<c>0002.nch</c>).</summary>
    public static HashSet<int> ParseExempt(byte[]? b)
    {
        var set = new HashSet<int>();
        if (b == null || b.Length < 10) return set;
        int count = BitConverter.ToUInt16(b, 2);
        for (int i = 0; i < count && 6 + 4 * i + 4 <= b.Length; i++)
            set.Add(BitConverter.ToUInt16(b, 6 + 4 * i + 2));
        return set;
    }
}
