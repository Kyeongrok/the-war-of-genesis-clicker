namespace WarOfGenesis.Assets;

/// <summary>
/// 파트1 AI 표 <c>Dats\AI.dat</c> 한 칸 — 로더 <c>alpha.dll 0x10023640</c>. 유닛은 <see cref="Part1BattleUnit.AiType"/>(유닛 <c>+0x262</c>)로 칸을 고른다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 12절. 0 번은 엔진이 만든 「제자리」 칸이고 파일 칸은 1~48 번이다(인물 레코드의 AI 번호 0 이 1451개로 가장 흔하다). 파일 33바이트를 메모리 38바이트 칸 자리(<see cref="Raw"/> 의 번호)로 옮긴다.
/// 뜻을 안 칸만 이름을 붙였다 — 나머지는 대상·기술 고르기에 쓰인다.
/// </remarks>
/// <param name="Raw">메모리 자리 → 값(0x00 · 0x01 · 0x02 · 0x03 · 0x04 · 0x06 · 0x08 · 0x0a · 0x0c~0x10 · 0x12 · 0x14 · 0x16 · 0x18(3바이트) · 0x1b~0x1e · 0x20 · 0x22 · 0x24).</param>
public sealed record Part1Ai(int Index, IReadOnlyDictionary<int, int> Raw)
{
    /// <summary>파일 칸 차례 — (메모리 자리, 바이트 수).</summary>
    private static readonly (int At, int Size)[] Layout =
    [
        (0x00, 1), (0x01, 1), (0x02, 1), (0x03, 1), (0x04, 1), (0x06, 2), (0x08, 2), (0x0a, 2),
        (0x0c, 1), (0x0d, 1), (0x0e, 1), (0x0f, 1), (0x10, 1), (0x12, 2), (0x14, 1), (0x16, 2),
        (0x18, 3), (0x1b, 1), (0x1c, 1), (0x1d, 1), (0x1e, 1), (0x20, 2), (0x22, 2), (0x24, 1),
    ];

    public const int FileSize = 33;

    /// <summary><c>+0</c> 이동 방식 — 0 정한 칸(<see cref="GoalX"/>, <see cref="GoalY"/>)으로 · 1 Chr <see cref="FollowChr"/> 를 따라감 · 2 보통 · 3 움직이지 않음(<c>0x10026570</c>).</summary>
    public int MoveMode => Raw[0x00];
    public int GoalX => Raw[0x06];
    public int GoalY => Raw[0x08];
    public int FollowChr => Raw[0x0a];

    /// <summary><c>+0xc</c> 이동 한도 — 이동력을 이 값으로 자른다(100 이 대부분, 사실상 한도 없음).</summary>
    public int MoveCap => Raw[0x0c];

    /// <summary><c>+0x16</c> 먼저 쓸 기술(Attack.dat 번호), 0 이면 없음.</summary>
    public int PreferredAttack => Raw[0x16];

    /// <summary><c>+0x1d</c> 깨어나는 조건 — 0 처음부터 · 1 가까운 깨어난 유닛까지 거리 ≤ <see cref="WakeDistance"/> · 2 다른 거리 ≤ 그 값 · 3 시간 틱 ≥ <see cref="WakeTick"/> (<c>0x10026230</c>).</summary>
    public int Wake => Raw[0x1d];
    public int WakeDistance => Raw[0x1e];
    public int WakeTick => Raw[0x20];

    /// <summary><c>+0x1c</c> 도망 문턱 — 1 HP 80% · 2 50% · 3 20% 이하면 「위험」 갈래, 4(또는 그 밖)는 안 한다(<c>0x100263a0</c>).</summary>
    public int FleeThreshold => Raw[0x1c];

    /// <summary>도망 문턱 HP — 최대 HP 에서 셈한 값, 안 하면 -1.</summary>
    public int FleeHp(int maxHp) => FleeThreshold switch { 1 => maxHp * 8 / 10, 2 => maxHp * 5 / 10, 3 => maxHp * 2 / 10, _ => -1 };

    /// <summary>AI 번호로 고른다 — 목록의 차례가 곧 번호다(0 은 엔진이 만든 칸).</summary>
    public static IReadOnlyList<Part1Ai> LoadAll() => ParseAll(Part1Game.Read("Dats", "AI.dat"));

    public static IReadOnlyList<Part1Ai> ParseAll(byte[]? b)
    {
        if (b is not { Length: >= 2 }) return [];
        int count = BitConverter.ToUInt16(b, 0);
        // 0 번은 파일에 없고 엔진이 채운다(0x100236af) — 제자리(이동 방식 3) · +0xd 2 · 도망 안 함(4). 파일 칸은 1 번부터다.
        var builtin = Layout.ToDictionary(f => f.At, _ => 0);
        builtin[0x00] = 3; builtin[0x0d] = 2; builtin[0x1c] = 4;
        var list = new List<Part1Ai>(count + 1) { new(0, builtin) };
        for (int i = 1, o = 2; i <= count && o + FileSize <= b.Length; i++)
        {
            var raw = new Dictionary<int, int>();
            foreach (var (at, size) in Layout)
            {
                raw[at] = size switch { 1 => b[o], 2 => BitConverter.ToUInt16(b, o), _ => b[o] | b[o + 1] << 8 | b[o + 2] << 16 };
                o += size;
            }
            list.Add(new Part1Ai(i, raw));
        }
        return list;
    }
}
