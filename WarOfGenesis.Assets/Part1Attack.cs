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

    /// <summary><c>+0x21</c> 사거리 갈래 — 1 RANGE 능력치 + <see cref="RangeValue"/> · 2 무기 사거리 + RANGE 보정 · 3 <see cref="RangeValue"/> 그대로 · 4 무기 사거리(<c>0x10038ae0</c>).</summary>
    public int RangeKind => Raw[0x21];
    public int RangeValue => Raw[0x24];

    /// <summary><c>+0x40</c> 맞는 칸 둘레(유닛 모으기 <c>0x10039690</c> 에 넘긴다).</summary>
    public int Area => Raw[0x40];

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

    /// <summary>번호(1 부터)로 찾는다.</summary>
    public static Part1Attack? ById(IReadOnlyList<Part1Attack> list, int id) => id >= 1 && id <= list.Count ? list[id - 1] : null;

    public static IReadOnlyList<Part1Attack> LoadAll() => ParseAll(Part1Game.Read("Dats", "Attack.dat"));

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

    public static IReadOnlyList<Part1Job> LoadAll() => ParseAll(Part1Game.Read("Dats", "Job.dat"));

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
