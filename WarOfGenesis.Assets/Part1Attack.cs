using System.Text;

namespace WarOfGenesis.Assets;

/// <summary>
/// 파트1 기술 <c>Dats\Attack.dat</c> 한 칸 — 로더 <c>alpha.dll 0x10023260</c>(파일 94바이트 → 메모리 0x6c바이트). <b>번호 = 레코드 + 1</b>(메모리 0 번은 비어 있다).
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-검기술」 0.1, 「분석-파트1-전투」 9·15절. <see cref="Raw"/> 는 메모리 자리 → 값이다.
/// </remarks>
public sealed record Part1Attack(int Id, string Name, IReadOnlyDictionary<int, int> Raw)
{
    /// <summary>파일 칸 차례 — (메모리 자리, 바이트 수, 부호).</summary>
    private static readonly (int At, int Size)[] Layout =
    [
        (0x20, 1), (0x21, 1), (0x24, 4), (0x28, 4),
        (0x2c, 1), (0x2d, 1), (0x2e, 1), (0x2f, 1), (0x30, 1), (0x31, 1), (0x32, 1), (0x33, 1), (0x34, 1), (0x35, 1), (0x36, 1),
        (0x38, 4), (0x3c, 4), (0x40, 1), (0x41, 1), (0x42, 1), (0x44, 4), (0x48, 1),
        (0x4c, 4), (0x50, 4), (0x54, 4), (0x58, 4), (0x5c, 1), (0x60, 4),
        (0x64, 1), (0x65, 1), (0x66, 1), (0x67, 1), (0x68, 1), (0x69, 1),
    ];

    public const int FileSize = 94;

    /// <summary>
    /// <c>+0x21</c> 사거리 갈래 — 1 RANGE 능력치 + <see cref="RangeValue"/> · 2 무기 사거리 + RANGE 보정 · 3 <see cref="RangeValue"/> 그대로 · 4 무기 사거리 + <see cref="RangeValue"/>(<c>0x10038ae0</c>).
    /// 사거리는 효과 범위 크기와 같은 <b>4분의 1칸</b>이다(평지 칸마다 4, <c>0x1008a950</c>) — 칸 수 = 값 ÷ 4. 최소 <c>+0x28</c> 도 같은 단위.
    /// </summary>
    public int RangeKind => Raw[0x21];
    public int RangeValue => Raw[0x24];

    /// <summary>
    /// <c>+0x20</c> 사거리 모양(<c>0x10038ce8</c> 점프표 <c>0x10039054</c>) — 1 십자(<c>0x1008b360</c>, 같은 줄 · 같은 열만) · 2 네 방향 외줄(<c>0x1008af30</c> ×4) ·
    /// 3 마름모(<c>0x1008ab80</c>) · 4 네 방향 부채꼴(<c>0x1008b710</c> ×4 — 앞으로 f 칸 가면 옆으로 f/3 칸까지) · 5 네 방향 폭 3 줄(<c>0x1008c440</c> ×4).
    /// 거리는 모양과 상관없이 늘 4 × 맨해튼 + 높이 보정(<c>0x1008a950</c>)이다.
    /// </summary>
    public int RangeShape => Raw[0x20];

    /// <summary><c>+0x28</c> 사거리 최소(4분의 1칸) — 높이 보정은 늘 2 × |높이 차|로 잰 거리와 견준다(<c>0x1008ad64</c>). 칼 5 · 창 4 · 활 9 · 총 13.</summary>
    public int RangeMin => Raw[0x28];

    /// <summary><c>+0x2d</c> 사거리 거리에 높이 차를 넣는다 · <c>+0x30</c> 차등(오르막 ×3 · 내리막 −×2, 끄면 2 × |차|) · <c>+0x2e</c> 시야(<c>0x1008a7a0</c>) · <c>+0x2f</c> 같은 높이 칸만.</summary>
    public bool RangeHeight => Raw[0x2d] != 0;
    public bool RangeGraded => Raw[0x30] != 0;
    public bool RangeSight => Raw[0x2e] != 0;
    public bool RangeSameHeight => Raw[0x2f] != 0;

    /// <summary>
    /// 모양 <see cref="RangeShape"/> 이 (dx, dy) 칸을 덮나 — 네 방향 모양(2 · 4 · 5)은 사거리 칠하기가 네 번 불러 합친다(<c>0x10038d39</c>~). 거리는 따로 본다.
    /// </summary>
    public bool RangeShapeCovers(int dx, int dy)
    {
        int ax = Math.Abs(dx), ay = Math.Abs(dy);
        return RangeShape switch
        {
            1 or 2 => dx == 0 || dy == 0,
            4 => Math.Min(ax, ay) <= Math.Max(ax, ay) / 3,
            5 => ax <= 1 || ay <= 1,
            _ => true,
        };
    }

    /// <summary>
    /// <c>+0x31</c> 사거리 칸 거르기 — 누를 수 있는 칸(<c>0x10032229</c> · 칠하기 <c>0x10038c31</c> → 칸 거르개 <c>0x1008a950</c>):
    /// 1 적이 선 칸 · 3 같은 편(자신 포함)이 선 칸 · 5 누가 선 칸 · 2 아무 칸 · 7 따로(돌진·워프 길) · 4 자기 자리(사거리 0 인 것들).
    /// 사거리 칸은 <c>+0x20</c> 모양 · <c>+0x24</c> 크기 · <c>+0x28</c> 최소 · <c>+0x2c</c>~<c>+0x30</c> · <c>+0x32</c> 인자다.
    /// </summary>
    public int AimFilter => Raw[0x31];

    /// <summary>
    /// <c>+0x40</c> 효과 범위 칸 거르기 — <see cref="AimFilter"/> 와 같은 값(1 적만 · 3 같은 편·자신만 · 5 유닛 누구나 · 2 아무 칸 — 범위 안 모두).
    /// 범위 칠하기 <c>0x100393b0</c>(인자 6 = 0)와 맞는 순간 유닛 모으기 <c>0x10039690</c> 가 둘 다 이 값을 칸 거르개 <c>0x1008a950</c> 에 넘긴다 —
    /// 1 이면 적이 아닌 유닛의 칸은 범위에서 빠져 맞지 않는다(<c>0x10038550</c> 적인가).
    /// </summary>
    public int AreaFilter => Raw[0x40];

    /// <summary><c>+0x33</c> 효과 범위 모양(1~5, 0 이면 범위 없음) · <c>+0x38</c> 크기 · <c>+0x3c</c> 최소 — 크기는 4분의 1칸(칸마다 4, 높이 차 보정, <c>0x1008a950</c>).</summary>
    public int AreaShape => Raw[0x33];
    public int AreaSize => Raw[0x38];
    public int AreaMinSize => Raw[0x3c];

    /// <summary><c>+0x34</c> 맵 +0x6c &amp;1 칸 빼기 · <c>+0x35</c> 거리에 높이 차(늘 2 × |차|, 차등 없음) · <c>+0x36</c> 겨눈 칸과 높이가 같은 칸만 — 효과 범위 쪽 짝(<c>0x10039408</c>~).</summary>
    public bool AreaHeight => Raw[0x35] != 0;
    public bool AreaSameHeight => Raw[0x36] != 0;

    /// <summary>
    /// 효과 범위 방향(<c>0x10019de0(시전자 x, y, 겨눈 x, y)</c>) — 0 위 · 1 왼쪽 · 2 아래 · 3 오른쪽. |dx| ≥ |dy| 면 가로(같으면 가로), 같은 칸이면 2(아래).
    /// 명령 시작 <c>0x1003a092</c> 이 이 값을 <c>0x100393b0</c> 의 방향 인자로 넘긴다.
    /// </summary>
    public static int AreaDirection(int fromCol, int fromRow, int toCol, int toRow)
    {
        int adx = Math.Abs(toCol - fromCol), ady = Math.Abs(toRow - fromRow);
        if (adx >= ady) return fromCol > toCol ? 1 : fromCol < toCol ? 3 : 2;
        return fromRow > toRow ? 0 : 2;
    }

    /// <summary>
    /// 효과 범위 모양 <see cref="AreaShape"/> 이 겨눈 칸에서 (dx, dy) 칸을 덮나 — 점프표 <c>0x10039554</c>: 1 십자 <c>0x1008b360</c> · 2 외줄 <c>0x1008af30</c> ·
    /// 3 마름모 <c>0x1008ab80</c> · 4 부채꼴 <c>0x1008b710</c> · 5 폭 3 줄 <c>0x1008c440</c>. 2 · 4 · 5 는 <b>한 방향만</b>(<paramref name="dir"/>, <see cref="AreaDirection"/>) —
    /// 겨눈 칸(앞 0)부터 앞으로, 부채꼴은 앞 f 칸 줄에서 옆 f/3 칸까지, 폭 3 줄은 옆 ±1(겨눈 칸 줄 포함). 거리는 따로 본다(4 × 맨해튼 + 높이).
    /// </summary>
    public bool AreaShapeCovers(int dx, int dy, int dir)
    {
        var (f, side) = dir switch { 0 => (-dy, dx), 1 => (-dx, dy), 2 => (dy, dx), _ => (dx, dy) };
        return AreaShape switch
        {
            1 => dx == 0 || dy == 0,
            2 => f >= 0 && side == 0,
            3 => true,
            4 => f >= 0 && Math.Abs(side) <= f / 3,
            5 => f >= 0 && Math.Abs(side) <= 1,
            _ => dx == 0 && dy == 0,
        };
    }

    /// <summary>효과 범위 반경(칸) — <see cref="AreaSize"/> ÷ 4(평지 맨해튼). 9999(판 전체)는 <paramref name="cap"/> 칸으로 자른다.</summary>
    public int AreaTiles(int cap = 16) => AreaShape == 0 ? 0 : Math.Min(cap, Math.Max(0, AreaSize) / 4);

    /// <summary><c>+0x41</c> 1 이면 맵 칸도 맞는다(<c>0x10076c46</c>).</summary>
    public bool HitsTiles => Raw[0x41] == 1;

    /// <summary><c>+0x42</c> 피해 세기 식 선택자(<see cref="Part1Combat.Power"/>).</summary>
    public int Formula => Raw[0x42];

    /// <summary><c>+0x44</c> 세기에 이 값의 제곱을 더한다(<c>0x10040694</c>).</summary>
    public int PowerAdd => Raw[0x44];

    /// <summary><c>+0x4c</c> 소울 비용으로 보인다 — 쓸 수 있나가 소울 − 50 과 견준다(<c>0x10026df2</c>).</summary>
    public int Cost => Raw[0x4c];

    /// <summary><c>+0x48</c> 명중률(%) — 맞기 전에 rand()%100 ≥ 이 값이면 빗나간다(결과 3, <c>0x10041050</c>).</summary>
    public int Accuracy => Raw[0x48];

    /// <summary><c>+0x64</c> 속성(1 화염 · 2 빙한 · 3 전격 · 4 회복 · 5 암흑) — 0 이면 무기 속성을 쓴다. <c>+0x65</c> 둘째 속성(1~4, 인물 칸 35~38).</summary>
    public int Element => Raw[0x64];
    public int Element2 => Raw[0x65];

    /// <summary><c>+0x66</c> 세기에 곱하는 값(타수로 보인다).</summary>
    public int Hits => Raw[0x66];

    /// <summary><c>+0x69</c> 기술 전 준비 동작 갈래(<c>0x10042fc4</c>).</summary>
    public int Prep => Raw[0x69];

    /// <summary>피해에 곱하는 %(원본에 없는 우리 칸 — 편집기 「파트1 기술 편집」, <see cref="Part1AttackBook"/>). 기본 100.</summary>
    public int DamagePercent { get; init; } = 100;

    /// <summary>번호(1 부터)로 찾는다.</summary>
    public static Part1Attack? ById(IReadOnlyList<Part1Attack> list, int id) => id >= 1 && id <= list.Count ? list[id - 1] : null;

    /// <summary>원본 <c>Attack.dat</c> 에 편집기의 덮어쓰기(<see cref="Part1AttackBook"/>)를 얹은 것.</summary>
    public static IReadOnlyList<Part1Attack> LoadAll() => Part1AttackBook.Apply(LoadOriginal());

    /// <summary>덮어쓰기 없는 원본 <c>Attack.dat</c>.</summary>
    public static IReadOnlyList<Part1Attack> LoadOriginal() => ParseAll(Part1Game.Read("Dats", "Attack.dat"));

    public static IReadOnlyList<Part1Attack> ParseAll(byte[]? b)
    {
        if (b is not { Length: >= 2 }) return [];
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        int count = BitConverter.ToUInt16(b, 0);
        var list = new List<Part1Attack>(count);
        for (int i = 0, o = 2; i < count && o + FileSize <= b.Length; i++)
        {
            var plain = new byte[30];
            for (int k = 0; k < 30; k++) plain[k] = (byte)~b[o + k];
            int end = Array.IndexOf(plain, (byte)0);
            string name = cp949.GetString(plain, 0, end < 0 ? 30 : end);
            int p = o + 30;
            var raw = new Dictionary<int, int>();
            foreach (var (at, size) in Layout)
            {
                raw[at] = size == 1 ? b[p] : BitConverter.ToInt32(b, p);
                p += size;
            }
            list.Add(new Part1Attack(i + 1, name, raw));
            o += FileSize;
        }
        return list;
    }
}

/// <summary>
/// 파트1 어빌리티 <c>Dats\Abi.dat</c> 한 칸(39바이트) — 로더 <c>alpha.dll 0x100a7980</c>. <b>번호 = 레코드 + 1</b>(1 HP · 2 STR … 9 MR · 77 회피 …).
/// </summary>
/// <param name="AttackId">메모리 <c>+0x20</c> — 쓰면 나가는 기술(Attack.dat 번호), 0 이면 패시브.</param>
/// <param name="Kind">메모리 <c>+0x24</c> — 2 면 아이템이 주는 기술(<see cref="ItemId"/> 를 장비해야 쓴다), 3·5 는 그런 것의 무리(<c>0x1003f260</c>).</param>
/// <param name="ItemId">메모리 <c>+0x26</c> — <see cref="Kind"/> 2·4 일 때 그 아이템.</param>
public sealed record Part1Ability(int Id, string Name, int Flag, int AttackId, int Kind, int ItemId, int Tail)
{
    public const int FileSize = 39;

    public static IReadOnlyList<Part1Ability> LoadAll() => ParseAll(Part1Game.Read("Dats", "Abi.dat"));

    public static Part1Ability? ById(IReadOnlyList<Part1Ability> list, int id) => id >= 1 && id <= list.Count ? list[id - 1] : null;

    public static IReadOnlyList<Part1Ability> ParseAll(byte[]? b)
    {
        if (b is not { Length: >= 2 }) return [];
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        int count = BitConverter.ToUInt16(b, 0);
        var list = new List<Part1Ability>(count);
        for (int i = 0, o = 2; i < count && o + FileSize <= b.Length; i++, o += FileSize)
        {
            var plain = new byte[30];
            for (int k = 0; k < 30; k++) plain[k] = (byte)~b[o + k];
            int end = Array.IndexOf(plain, (byte)0);
            list.Add(new Part1Ability(i + 1, cp949.GetString(plain, 0, end < 0 ? 30 : end), b[o + 30], BitConverter.ToInt32(b, o + 31),
                                      b[o + 35], BitConverter.ToUInt16(b, o + 36), b[o + 38]));
        }
        return list;
    }
}

/// <summary>
/// 파트1 직업 <c>Dats\Job.dat</c> 한 칸(100바이트) — 로더 <c>alpha.dll 0x100a77ad</c>. <b>번호 = 레코드 + 1</b>(인물 레코드 칸 7 — 살라딘 1 슈미터 · 셰라자드 78 무슬림).
/// </summary>
/// <param name="Head">메모리 <c>+0x3e</c>·<c>+0x3f</c> 두 바이트(뜻 모름).</param>
/// <param name="Firsts">메모리 <c>+0x40+i</c>·<c>+0x45+i</c> 짝 다섯 — 어빌리티 번호와 레벨로 보인다(슈미터: 12 · 128 레벨 1).</param>
/// <param name="Abilities">메모리 <c>+0x4a+i</c>(어빌리티)·<c>+0x58+i</c>(레벨) 14칸 — 이 직업이 다루는 어빌리티와 그 레벨(슈미터: 12·1 · 128·2 · 1 HP·3 · 2 STR·2 …).</param>
public sealed record Part1Job(int Id, string Name, string EnglishName, (int A, int B) Head, (int A, int B)[] Firsts, (int Ability, int Level)[] Abilities)
{
    public const int FileSize = 100;

    /// <summary>모든 직업 — 원본 <c>Job.dat</c> 에 리메이크가 더한 어빌리티(<see cref="Part1JobAdditions"/>)를 얹는다.</summary>
    public static IReadOnlyList<Part1Job> LoadAll() => Part1JobAdditions.Apply(ParseAll(Part1Game.Read("Dats", "Job.dat")));

    /// <summary>
    /// 레벨업 후보(<c>0x1009dac0</c>) — 이 직업의 어빌리티 가운데 아직 안 배운 것은 레벨 1, 최대보다 낮은 것은 지금 + 1. 레벨이 하나 오를 때마다 이 가운데 하나를 올린다.
    /// </summary>
    public IEnumerable<(int Ability, int NewLevel)> LevelUpCandidates(Part1Chr chr)
    {
        foreach (var (ability, max) in Abilities)
        {
            int now = chr.AbilityLevel(ability);
            if (now < 0) yield return (ability, 1);
            else if (now < max) yield return (ability, now + 1);
        }
    }

    public static Part1Job? ById(IReadOnlyList<Part1Job> list, int id) => id >= 1 && id <= list.Count ? list[id - 1] : null;

    public static IReadOnlyList<Part1Job> ParseAll(byte[]? b)
    {
        if (b is not { Length: >= 2 }) return [];
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        string Text(int o)
        {
            var plain = new byte[30];
            for (int k = 0; k < 30; k++) plain[k] = (byte)~b[o + k];
            int end = Array.IndexOf(plain, (byte)0);
            return cp949.GetString(plain, 0, end < 0 ? 30 : end);
        }
        int count = BitConverter.ToUInt16(b, 0);
        var list = new List<Part1Job>(count);
        for (int i = 0, o = 2; i < count && o + FileSize <= b.Length; i++, o += FileSize)
        {
            int p = o + 62;
            var firsts = Enumerable.Range(0, 5).Select(k => ((int)b[p + 2 * k], (int)b[p + 2 * k + 1])).Where(x => x.Item1 != 0).ToArray();
            p += 10;
            var abilities = Enumerable.Range(0, 14).Select(k => ((int)b[p + 2 * k], (int)b[p + 2 * k + 1])).Where(x => x.Item1 != 0).ToArray();
            list.Add(new Part1Job(i + 1, Text(o), Text(o + 30), (b[o + 60], b[o + 61]), firsts, abilities));
        }
        return list;
    }
}
