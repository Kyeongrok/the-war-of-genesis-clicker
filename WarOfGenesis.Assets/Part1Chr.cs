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

    /// <summary>
    /// 칸 10~14(파일 0x45~0x49 = 유닛 <c>+0xb8</c>~<c>+0xbc</c>, CChr 는 유닛 +0x68) — 10 종류(2 면 AI 가 도망치지 않는다, <c>0x100263a6</c>) ·
    /// 11 큰 유닛(3×3, <c>+0xb9</c>) · 12 <b>난다</b>(<c>+0xba</c>) · 13·14 나는 높이 바탕 · 최소(<c>0x10038090</c>).
    /// 나는 인물은 아지다하카 · 티아마트 · 썬더볼트 · 라이트닝(4 · 8) · 아지(12 · 20) — 모두 큰 유닛이다(옵시디안 분석-파트1-전투 33절).
    /// </summary>
    public const int KindField = 10, BigField = 11, FlyField = 12, FlyBaseField = 13, FlyMinField = 14;

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

    /// <summary>장비 보정을 뺀 능력치 — 바탕 + <b>쓰는</b> 어빌리티 레벨(<see cref="EffectiveLevel"/>) × 5(<c>0x1009eaa0</c>~, 0xff 면 안 더한다).</summary>
    public int Stat(int field) => Fields[field] + (StatSource(field).Ability is > 0 and var a ? Math.Max(0, EffectiveLevel(a)) * 5 : 0);

    /// <summary>칸 39~43 — 장비 아이템 번호 다섯(0 은 빈 칸).</summary>
    public int[] Equipment => Fields[39..44];

    /// <summary>
    /// <b>배운</b> 어빌리티 레벨(CChr <c>+0x8c</c> + 번호) — 없으면 -1(원본은 0xff). 전직 조건(<c>0x1009e6f0</c>) · 레벨업 후보(<c>0x1009dac0</c>) · 직업 나무 · 저장은 이것을 쓴다.
    /// 전투에서 쓰는 값은 <see cref="EffectiveLevel"/>.
    /// </summary>
    public int AbilityLevel(int ability) => Abilities.FirstOrDefault(a => a.Ability == ability) is { Ability: > 0 } a ? a.Level : -1;

    /// <summary>직업 0x5e(94) — Job.dat(93칸) 밖의 특별 직업: 모든 어빌리티를 쓴다(안 배운 것은 레벨 1, <c>0x1009e020</c> · 목록 <c>0x1009dda0</c>).</summary>
    public const int AllAbilitiesJob = 0x5e;

    /// <summary>
    /// 지금 직업으로 쓰는 레벨(장비 빼고) — <c>0x1009e020</c> 의 앞 절반: 직업 0 이면 배운 레벨 그대로 · 94 면 배운 레벨(안 배웠으면 1) ·
    /// 갈래(Abi +0x24) 2·4(무기 덧기술 · 소환)면 1 · 그 밖은 지금 직업의 14칸(Job.dat +0x4a/+0x58)에 있어야 min(배운, 직업 최대), 없거나 안 배웠으면 -1.
    /// </summary>
    public int JobLevel(int ability)
    {
        int raw = AbilityLevel(ability), job = JobId;
        if (job == 0) return raw;
        if (job == AllAbilitiesJob) return raw >= 0 ? raw : 1;
        if (Part1Ability.ById(Part1Tables.Abilities, ability)?.Kind is 2 or 4) return 1;
        if (Part1Job.ById(Part1Tables.Jobs, job) is not { } j) return -1;
        foreach (var (a, max) in j.Abilities)
            if (a == ability) return raw < 0 ? -1 : Math.Min(raw, max);
        return -1;
    }

    /// <summary>
    /// 장비가 주는 레벨 — <c>0x1009d850</c>: 해금 보정(216~238 · 249, <see cref="Part1Bonus.UnlockCode"/>)이 있는 어빌리티면 장비 다섯의 그 보정 값 가운데 큰 것(<c>0x1009e1f0</c>, 없으면 0),
    /// 해금 보정이 없는 어빌리티면 -1(0xff).
    /// </summary>
    public int ItemLevel(int ability)
    {
        int code = Part1Bonus.UnlockCode(ability);
        if (code == 0) return -1;
        int best = 0;
        foreach (int id in Equipment)
            if (id > 0 && Part1Item.ById(Part1Tables.Items, id) is { } item)
                foreach (var b in item.Bonuses)
                    if (b.Code == code && b.Value > best) best = b.Value;
        return best;
    }

    /// <summary>
    /// 전투에서 <b>쓰는</b> 어빌리티 레벨 — <c>alpha.dll 0x1009e020</c>: <see cref="JobLevel"/> 와 <see cref="ItemLevel"/> 가운데 큰 것(한쪽이 0xff 면 다른 쪽).
    /// 능력치(× 5) · 최대 HP(어빌리티 1 × 100) · 회피 · 막기 · 자세 · 무기 장비 · 탈것이 이것을 쓴다. -1 이면 못 쓴다.
    /// 해금 보정이 있는 어빌리티를 지금 직업이 안 다루고 장비도 없으면 원본처럼 0 이 나온다(목록에는 안 오른다).
    /// </summary>
    public int EffectiveLevel(int ability)
    {
        int job = JobLevel(ability), item = ItemLevel(ability);
        if (job < 0) return item;
        return item < 0 ? job : Math.Max(job, item);
    }

    /// <summary>
    /// 아이템이 주는 기술(갈래 2 · 4) 가운데 지금 나오는 것 — 원본 전투 목록 <c>0x10090c87</c>~<c>0x10090d4c</c>: 「초필살기 제어」(120)를 쓰면 갈래 2 가운데
    /// <b>무기 칸</b>(장비 0) 아이템이 +0x26 과 같은 첫 것(스탐블링거 34 → 폭풍검 94 · 엑스칼리버 → 블리자드 스탐 …), 「소환 능력」(119)을 쓰면 갈래 4 가운데 <b>몸 칸</b>(장비 2)의 옷과 같은 첫 것.
    /// 갈래 2 · 4 를 직접 배워 둔 것(롤랑 · 아델라이데의 폭풍검)은 이 조건을 못 채우면 안 나온다.
    /// </summary>
    public IEnumerable<int> ItemSkills()
    {
        foreach (var (gate, kind, item) in new[] { (120, 2, Equipment[0]), (119, 4, Equipment[2]) })
            if (item > 0 && EffectiveLevel(gate) > 0 && Part1Tables.Abilities.FirstOrDefault(a => a.Kind == kind && a.ItemId == item) is { } skill)
                yield return skill.Id;
    }

    /// <summary>
    /// 목록(상태 · 일행 표)에 보일 쓰는 레벨 — <see cref="EffectiveLevel"/> 이되, 갈래 2 · 4(아이템 기술)는 <see cref="ItemSkills"/> 에 들 때만 1, 아니면 -1.
    /// <c>0x1009e020</c> 은 갈래 2 · 4 를 늘 1 로 주지만 원본 목록은 그것을 직접 모으지 않는다(<c>0x1009dda0</c> 은 갈래 1 · 3 · 5 만).
    /// </summary>
    public int ListedLevel(int ability) =>
        Part1Ability.ById(Part1Tables.Abilities, ability)?.Kind is 2 or 4 ? (ItemSkills().Contains(ability) ? 1 : -1) : EffectiveLevel(ability);

    /// <summary>
    /// 그 탈것(파트1 Chr 번호)을 타는 데 드는 어빌리티 — <c>0x1009e750</c>(분석-파트1-챕터 10.3): 121 아론다이트탑승 · 122 마장기탑승 · 123 아지다하카탑승 ·
    /// 125 비행기탑승 · 126 탱크탑승. 탈것이 아니면 0.
    /// </summary>
    public static int RideAbility(int mount) => mount switch
    {
        433 or 476 => 121,
        177 or 477 or 478 or 314 or 425 or 431 or 419 or 424 or 469 or 470 => 122,
        473 or 488 or 507 or 516 or 517 or 625 => 123,
        471 or 472 or 475 => 125,
        417 or 418 => 126,
        _ => 0,
    };

    /// <summary>이 인물이 그 탈것(파트1 Chr 번호)을 탈 수 있나 — 그 어빌리티가 있어야 하고, 아론다이트는 장비 보정 248(바리사다 II 「아론다이트의 열쇠」)도.</summary>
    public bool CanRide(int mount, IEnumerable<Part1Item?> equipment) =>
        RideAbility(mount) is > 0 and var ability && EffectiveLevel(ability) >= 0
        && (ability != 121 || equipment.Any(i => i?.Bonus(248) is not (null or 0)));

    /// <summary>
    /// 기본 공격(Attack.dat 번호) — 인물 칸 9(유닛 +0xb6, 분석-파트1-전투 24절 A: 이동 단계에 이 기술의 범위를 칠하고 적을 누르면 친다).
    /// 록슬리 330 「용병 라이트닝볼츠1」 · 살라딘 352 「더블 어택」 · 깁슨 5 「단발사격」 … 0 이면 무기 갈래의 것(<see cref="Part1Item.BasicAttack"/>).
    /// </summary>
    public int BasicAttackId => Fields[9];

    /// <summary>최대 HP — 칸 19 + <b>쓰는</b> 어빌리티 1 레벨(<see cref="EffectiveLevel"/> — 직업 최대로 막힌다) × 100(<c>0x1009ea50</c>).</summary>
    public int MaxHp => BaseMaxHp + Math.Max(0, EffectiveLevel(1)) * 100;

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

    /// <summary>
    /// 파트2 꼴 인물 자료로 — 번호는 <see cref="Part1Game.ChrBase"/> + 파트1 번호, 그림·얼굴은 <see cref="Part1Game.ObsBase"/> 위.
    /// 파트2 식이 읽는 칸은 <b>임시로</b> 이렇게 채운다: LP = 최대 HP · PSY = STR · DEP = AC · DEX = DEX · TP 100 · TP 제수 = WTP(행동 간격 — 작을수록 자주) · CTP 100(파트1 은 다 걷고도 한 번 친다 — 이동이 TP 를 다 써도 공격 값을 빚으로 낸다).
    /// 파트1 판의 피해·차례·이동은 파트1 식(<see cref="Part1Combat"/>)으로 돌린다(옵시디안 분석-파트1-전투 17절, 사용자 결정 「중간안」).
    /// 장비·어빌리티는 파트2 번호와 겹치므로 비운다.
    /// </summary>
    public CharacterData ToCharacterData(ushort basicWork, ushort nameId = 0) =>
        new(Part1Game.ChrBase + Code, nameId, 0, (ushort)(Part1Game.ObsBase + SpriteId), (ushort)(FaceId > 0 ? Part1Game.ObsBase + FaceId : 0), 0,
            0, 0, basicWork, (ushort)Math.Max(1, Level), (uint)Math.Max(1, MaxHp),
            (ushort)Stat(Str), 100, (ushort)ActionInterval, 100, (ushort)Stat(Ac), (ushort)Stat(Dex),
            new ushort[6], []);

    /// <summary>
    /// 자란 모습 — 칸 18 누적 경험치(레벨 = ÷100)를 <paramref name="experience"/> 로, 어빌리티 레벨을 <paramref name="abilities"/> 로 덮는다(없던 것은 더한다 — 개수 제한 없음).
    /// </summary>
    public Part1Chr WithGrowth(int experience, IReadOnlyDictionary<int, int> abilities)
    {
        var fields = (int[])Fields.Clone();
        fields[18] = experience;
        var merged = Abilities.Select(a => abilities.TryGetValue(a.Ability, out int level) ? (a.Ability, level) : a).ToList();
        foreach (var (ability, level) in abilities)
            if (!merged.Any(a => a.Ability == ability)) merged.Add((ability, level));   // 원본 메모리는 어빌리티 번호별 칸이라 개수 제한이 없다(Chr.dat 의 10칸은 시작값일 뿐)
        return this with { Fields = fields, Abilities = [.. merged] };
    }

    /// <summary>장비 다섯 칸(39~43)을 바꾼 모습.</summary>
    public Part1Chr WithEquipment(int[] slots)
    {
        var fields = (int[])Fields.Clone();
        for (int i = 0; i < 5 && i < slots.Length; i++) fields[39 + i] = slots[i];
        return this with { Fields = fields };
    }

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

/// <summary>파트1 표(Job.dat · Abi.dat · Item.dat)를 한 번 읽어 둔다 — <see cref="Part1Chr.EffectiveLevel"/> 가 자주 부른다. 비어 있으면(파트1 폴더를 아직 모름) 다음에 다시 읽는다.</summary>
public static class Part1Tables
{
    private static IReadOnlyList<Part1Job>? _jobs;
    private static IReadOnlyList<Part1Ability>? _abilities;
    private static IReadOnlyList<Part1Item>? _items;

    public static IReadOnlyList<Part1Job> Jobs => _jobs is { Count: > 0 } j ? j : _jobs = Part1Job.LoadAll();
    public static IReadOnlyList<Part1Ability> Abilities => _abilities is { Count: > 0 } a ? a : _abilities = Part1Ability.LoadAll();
    public static IReadOnlyList<Part1Item> Items => _items is { Count: > 0 } i ? i : _items = Part1Item.LoadAll();
}
