using System.Text;

namespace WarOfGenesis.Assets;

/// <summary>
/// 파트1(전작) 인물 레코드 <c>Chars\NNNN.chr</c>(175바이트) 전체 — 로더 <c>alpha.dll 0x1009d518</c> 가 읽는 차례 그대로.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 8절. <see cref="Fields"/> 의 번호가 곧 전투 행동 500(인물 칸 넣기)의 칸 번호다 —
/// 칸 0 이름 · 1 영문 이름 · 2 짧은 이름 · 3 전투 그림 · 4 반신 초상 · 5 얼굴 · 6~17 바이트·낱말 · 18·19 u32 · 20~29 바이트 · 30~38 낱말 · 39~43 장비.
/// 글 칸(0·1·2)은 바이트마다 비트 반전, cp949. 뒤에 어빌리티 10칸(u32 번호, u8 레벨)이 오고 2바이트가 남는다(엔진이 안 읽는다).
/// 메모리 CChr 는 유닛 <c>+0x68</c> 에 있고 칸은 CChr <c>+6</c> 부터 차례로 놓인다(이름 +6 · 영문 +0x1f · 짧은 이름 +0x38 · 그림 +0x44 …).
/// 최대 HP = 칸 19 + 어빌리티 1 레벨 × 100(<c>0x1009ea50</c>).
/// </remarks>
/// <param name="Fields">칸 3~43 의 값(칸 번호로 찾는다 — 0~2 는 글이라 0).</param>
public sealed record Part1Chr(int Code, string Name, string EnglishName, string ShortName, int[] Fields, (int Ability, int Level)[] Abilities)
{
    public const int Size = 175;

    public int SpriteId => Fields[3];
    public int BustId => Fields[4];
    public int FaceId => Fields[5];

    /// <summary>칸 19 — 기본 최대 HP(어빌리티 1 이 레벨마다 100 을 더한다).</summary>
    public int BaseMaxHp => Fields[19];

    /// <summary>능력치 칸 번호 — 쓸 때 값은 바탕 + 같은 번호 어빌리티 레벨 × 5 + 장비 보정이다(<see cref="Stat"/>).</summary>
    /// <remarks>상태 화면 이름표(<c>0x10091b31</c>~): SOUL · EXP · Str · Dex · Int · Skill · Luck · Spd · Ac · Mr · Wtp.</remarks>
    public const int Str = 20, Skill = 21, Dex = 22, Int = 23, Luck = 24, Spd = 25, Ac = 26, Mr = 27, Range = 29;

    /// <summary>칸 18 — 누적 경험치. 상태 화면 EXP 는 이 값 % 100, 레벨은 ÷ 100(<c>0x10091c5e</c>).</summary>
    public int Experience => Fields[18];
    public int Level => Fields[18] / 100;

    /// <summary>칸 7 — 직업(<see cref="Part1Job"/> 번호).</summary>
    public int JobId => Fields[7];

    /// <summary>칸 28 — <b>행동 간격</b>(상태 화면 이름 「Wtp」): 시간 틱이 이 값으로 나누어떨어질 때 차례가 온다(<c>0x10031d20</c>). 작을수록 자주 움직인다.</summary>
    public const int Interval = 28;

    public int ActionInterval => Math.Max(1, Fields[Interval]);

    /// <summary>능력치 칸 → 그것을 올리는 어빌리티(<c>Abi.dat</c> 2 STR … 9 MR)와 장비 보정 코드(<c>0x1009eaa0</c>~<c>0x1009ec70</c>).</summary>
    public static (int Ability, int Bonus) StatSource(int field) => field switch
    {
        Str => (2, 0xc9), Skill => (3, 0), Dex => (4, 0xc8), Int => (5, 0xcb),
        Range => (6, 0xce), Spd => (7, 0xcd), Ac => (8, 0xca), Mr => (9, 0xcc), _ => (0, 0),
    };

    /// <summary>장비 보정을 뺀 능력치 — 바탕 + 어빌리티 레벨 × 5.</summary>
    public int Stat(int field) => Fields[field] + (StatSource(field).Ability is > 0 and var a ? Math.Max(0, AbilityLevel(a)) * 5 : 0);

    /// <summary>칸 39~43 — 장비 아이템 번호 다섯(0 은 빈 칸).</summary>
    public int[] Equipment => Fields[39..44];

    /// <summary>그 어빌리티의 레벨 — 없으면 -1(원본은 0xff).</summary>
    public int AbilityLevel(int ability) => Abilities.FirstOrDefault(a => a.Ability == ability) is { Ability: > 0 } a ? a.Level : -1;

    /// <summary>최대 HP — 칸 19 + 어빌리티 1 레벨 × 100(<c>0x1009ea50</c>).</summary>
    public int MaxHp => BaseMaxHp + Math.Max(0, AbilityLevel(1)) * 100;

    /// <summary>칸마다 파일에서 차지하는 바이트 수(칸 3 부터) — 1 바이트, 2 낱말, 4 u32.</summary>
    internal static readonly int[] Widths =
    [
        2, 2, 2,                                   // 3 그림 · 4 반신 · 5 얼굴
        1, 1, 1, 2, 1, 1, 1, 1, 1, 1, 1, 1,        // 6~17
        4, 4,                                      // 18 · 19 최대 HP
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1,              // 20~29
        2, 2, 2, 2, 2, 2, 2, 2, 2,                 // 30~38
        2, 2, 2, 2, 2,                             // 39~43 장비
    ];

    public static Part1Chr? Load(int code) => Parse(code, Part1Game.Read("Chars", $"{code:D4}.chr"));

    public static Part1Chr? Parse(int code, byte[]? b)
    {
        if (b is not { Length: >= Size - 2 }) return null;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        string Text(int offset, int length)
        {
            var plain = new byte[length];
            for (int i = 0; i < length; i++) plain[i] = (byte)~b[offset + i];
            int end = Array.IndexOf(plain, (byte)0);
            return cp949.GetString(plain, 0, end < 0 ? length : end);
        }

        var fields = new int[44];
        int o = 0x3a;
        for (int i = 0; i < Widths.Length; i++)
        {
            int w = Widths[i];
            fields[3 + i] = w switch { 1 => b[o], 2 => BitConverter.ToUInt16(b, o), _ => BitConverter.ToInt32(b, o) };
            o += w;
        }
        var abilities = new List<(int, int)>();
        for (int k = 0; k < 10; k++, o += 5)
        {
            int ability = BitConverter.ToInt32(b, o);
            if (ability > 0 && ability < 200 && b[o + 4] != 0) abilities.Add((ability, b[o + 4]));   // 원본도 0 번·200 이상·레벨 0 은 버린다
        }
        return new Part1Chr(code, Text(0, 0x18), Text(0x18, 0x18), Text(0x30, 10), fields, [.. abilities]);
    }
}
