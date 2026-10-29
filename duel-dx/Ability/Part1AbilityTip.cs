using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 어빌리티 설명 글 — 오른쪽 단추를 누르고 있는 동안 뜨는 설명 창(<see cref="StatusScreen.DrawDescriptionTip"/>)에 넣는다.
/// </summary>
/// <remarks>
/// 원본 파트1 에는 어빌리티 설명 글이 <b>없다</b> — <c>Abi.dat</c> 한 칸(39바이트)은 이름 30 · 최대 레벨 · Attack 번호 · 갈래 · 아이템 · 분류뿐이고,
/// alpha.dll · 실행 파일에도 그런 글이 없다(설명 글은 <c>Item.dat</c> 의 아이템에만 있다). 원본 상태 창 · 레벨업 고르기 창도 이름 · LV · 소울만 보인다.
/// 그래서 자료(Abi.dat · Attack.dat · 능력치 규칙 <see cref="Part1Chr"/>)에서 글을 엮는다 — 종류 · 속성 · 사거리 · 범위 · 횟수 · 세기 · 명중 · 소울.
/// </remarks>
internal static class Part1AbilityTip
{
    private static IReadOnlyList<Part1Ability>? _abilities;
    private static IReadOnlyList<Part1Attack>? _attacks;
    private static IReadOnlyList<Part1Item>? _items;
    private static IReadOnlyList<Part1Job>? _jobs;

    private static readonly string[] ElementNames = ["", "화염", "빙한", "전격", "회복", "암흑"];

    /// <summary>
    /// 설명 글(<c>$n</c> 줄바꿈). <paramref name="ability"/> 는 Abi 번호(1~) 또는 20000 + Abi 번호, <paramref name="level"/> 은 보일 레벨(0 = 아직 안 배움 — Lv1 값을 보인다).
    /// 파트1 어빌리티가 아니거나 자료가 없으면 null. <paramref name="reach"/> 가 false 면 사거리 · 범위 · 소울을 뺀다 — 전투 목록은 그 값을
    /// 실제로 쓰는 work 로 따로 붙인다(<see cref="StatusScreen.AbilityEffectText"/>).
    /// </summary>
    internal static string? Text(int ability, int level, int maxLevel = 0, bool reach = true)
    {
        if (ability >= Part1Game.ChrBase) ability -= Part1Game.ChrBase;
        _abilities ??= Part1Ability.LoadAll();
        if (Part1Ability.ById(_abilities, ability) is not { } ab) return null;
        _attacks ??= Part1Attack.LoadAll();
        int max = maxLevel > 0 ? maxLevel : Math.Max(1, ab.Flag);   // 직업이 정한 최대, 없으면 Abi +0x1e(HP · STR … 9, 연 · 마법 5, 나머지 1)
        var lines = new List<string> { level > 0 ? $"{ab.Name}  Lv {level}/{max}" : $"{ab.Name}  (아직 안 배움 · 최대 Lv {max})" };
        int shown = Math.Max(1, level);

        if (ab.AttackId > 0 && Part1Attack.ById(_attacks, ab.AttackId + shown - 1) is { } a)
        {
            string element = a.Element is > 0 and < 6 ? ElementNames[a.Element] : "";
            string kind = KindText(a.Formula, element);
            lines.Add(EffectText(a.Id, a.Formula) ?? kind);
            var where = new List<string>();
            if (reach && RangeText(a) is { Length: > 0 } range) where.Add(range);
            int area = a.AreaTiles();   // +0x38 ÷ 4 칸(+0x31 은 겨누기 거르기다)
            if (reach && area > 0) where.Add($"범위 {area}칸");
            if (a.Hits > 1) where.Add($"타수 {a.Hits}");
            if (where.Count > 0) lines.Add(string.Join(" · ", where));
            var power = new List<string>();
            if (a.PowerAdd > 0) power.Add($"세기 +{a.PowerAdd}²");
            if (a.Accuracy > 0 && a.Accuracy < 100) power.Add($"명중 {a.Accuracy}%");
            if (reach && a.Cost > 0) power.Add($"소울 {a.Cost}");
            if (power.Count > 0) lines.Add(string.Join(" · ", power));
            if (a.DamagePercent != 100) power.Add($"피해 {a.DamagePercent}%");
            // 다음 레벨에서 달라지는 것(사용자 요청 gm-skil-27) — 레벨 칸은 Attack.dat 의 다음 칸이다(Part1SkillBook.LevelAttackIds).
            if (level > 0 && level < max && Part1SkillBook.LevelAttackIds(ab, _attacks) is { } ids && level < ids.Count
                && Part1Attack.ById(_attacks, ids[level]) is { } next && NextText(a, next, reach) is { Length: > 0 } better)
                lines.Add($"다음 Lv {level + 1}: {better}");
            else if (level > 0 && level >= max) lines.Add("최대 레벨이다");
        }
        else if (PassiveText(ability, shown) is { Length: > 0 } passive)
        {
            lines.Add(passive);
            if (StatUseText(ability) is { Length: > 0 } use) lines.Add(use);
            if (level > 0 && level < max && PassiveNext(ability, level + 1) is { Length: > 0 } better) lines.Add($"다음 Lv {level + 1}: {better}");
        }

        // 전직 조건은 뺐다 — 전직 트리(T) · 레벨업 창 옆에서 본다(사용자 요청).

        if (ab.Kind is 2 or 4 && ab.ItemId > 0)
        {
            _items ??= Part1Item.LoadAll();
            string item = Part1Item.ById(_items, ab.ItemId)?.Name ?? $"아이템 {ab.ItemId}";
            lines.Add(ab.Kind == 4 ? $"{item} 을(를) 지녀야 부를 수 있다" : $"{item} 을(를) 장비해야 쓴다");
        }
        return string.Join("$n", lines);
    }

    /// <summary>지금 칸과 다음 레벨 칸의 다른 값 — 「세기 +140² → +150² · 타수 2 → 3」. 같으면 빈 글.</summary>
    private static string NextText(Part1Attack now, Part1Attack next, bool reach)
    {
        var bits = new List<string>();
        void Diff(string label, int a, int b, string unit = "") { if (a != b) bits.Add($"{label} {a}{unit} → {b}{unit}"); }
        if (now.PowerAdd != next.PowerAdd) bits.Add($"세기 +{now.PowerAdd}² → +{next.PowerAdd}²");
        Diff("타수", Math.Max(1, now.Hits), Math.Max(1, next.Hits));
        Diff("명중", now.Accuracy, next.Accuracy, "%");
        Diff("피해", now.DamagePercent, next.DamagePercent, "%");
        if (now.Formula != next.Formula) bits.Add($"종류 → {KindText(next.Formula, next.Element is > 0 and < 6 ? ElementNames[next.Element] : "")}");
        if (reach)
        {
            if (RangeText(now) != RangeText(next) && RangeText(next) is { Length: > 0 } r) bits.Add(r);
            Diff("범위", now.AreaTiles(), next.AreaTiles(), "칸");
            Diff("소울", now.Cost, next.Cost);
        }
        return string.Join(" · ", bits);
    }

    /// <summary>패시브의 다음 레벨 값 — HP · 능력치 보정만 레벨에 따라 오른다.</summary>
    private static string PassiveNext(int ability, int level) => ability switch
    {
        1 => $"최대 HP +{100 * level}",
        >= 2 and <= 9 => $"{new[] { "", "", "STR", "SKILL", "DEX", "INT", "RANGE", "SPD", "AC", "MR" }[ability]} +{5 * level}",
        _ => "",
    };

    /// <summary>
    /// 보조 · 상태 기술의 실제 효과(원본 분석 — 분석-파트1-기술-b 9 · 10절, 분석-파트1-전투 25절). 걸리는 길이는 시간이 흐르며 준다. 따로 없으면 null.
    /// </summary>
    private static string? EffectText(int attackId, int formula) => attackId switch
    {
        375 => "기도 — 5% 썬 라이트 · 25% 힐 윈드가 내린다(나머지는 빛만)",
        376 => "희생 — 다른 아군 모두 HP 가득 · SOUL 150",
        377 => "해체대법 — 대상 HP 1 · SOUL 150",
        382 => "도발 — 대상 SOUL −20",
        366 => "댓쉬 — 겨눈 칸으로 달려간다",
        367 => "워프 — 겨눈 칸으로 순간 이동",
        _ => formula switch
        {
            6 => "회복 — 최대 HP × 세기 ÷ 20000",
            15 => "블래스 — 걸린 동안 차례마다 SOUL 이 +20 더 오른다(두 배로 빨리 찬다)",
            16 => "큐어 — 상태이상 · 강화 · 속성 부여를 모두 푼다",
            17 => "스피디 — 걸린 동안 SPD +8(이동 두 칸 더)",
            20 => "배리어 — 걸린 동안 마법 피해 0, 그 밖의 피해는 두 배",
            21 => "커즈 — SOUL 0, 그리고 20% 씩 중독 · 최면 · 컨디션 이상",
            22 => "쉐도우 실드 — 걸린 동안 AC ×1.5, MR ×0.5",
            23 => "블러드 러스트 — 걸린 동안 무기 공격 ×1.5",
            25 => "컨디션 이상 — SOUL 이 50 을 못 넘는다(능력치가 떨어진다)",
            27 => "최면 — 차례가 오지 않는다",
            28 => "혼란 — 자기 말고 모두를 적으로 본다",
            _ => null,
        },
    };

    /// <summary>세기 식(<c>Attack +0x42</c>, <see cref="Part1Combat.Power"/>)으로 본 종류.</summary>
    private static string KindText(int formula, string element)
    {
        string with = element.Length > 0 ? $"{element} 속성 " : "";
        return formula switch
        {
            0 or 7 => $"{with}무기 공격 — STR × 무기 공격력",
            1 => $"{with}무기 공격 — (DEX + SKILL) × 무게",
            2 => $"{with}무기 공격 — SKILL × (무게 + 공격력)",
            3 or 4 => $"{with}무기 공격 — STR · SKILL × 무기",
            29 or 30 or 31 => $"{with}둘째 손 무기 공격",
            5 => $"{with}마법 피해 — INT² ÷ MR",
            6 => "회복 — 최대 HP 에 비례",
            8 => "적에게는 피해, 아군에게는 회복 — INT²",
            9 => "밝기에 따른 피해",
            10 or 11 or 12 => $"무기에 {ElementNames[formula - 9]} 속성을 입힌다(무기 속성 ×1.2)",
            13 => "기계(마장기 · 탱크 · 썬더볼트 …)에게만 듣는다 — INT²",   // 종족 칸 10 = 2 — 1 이 언데드, 2 가 마장기 · 탱크(사용자 지적)
            >= 14 and <= 28 => "상태 · 보조 기술 — INT² ÷ MR 로 건다",
            _ => with.Length > 0 ? $"{with}기술" : "기술",
        };
    }

    /// <summary>사거리(<c>Attack +0x21</c> 갈래, <c>0x10038ae0</c>) — 값은 반 칸 단위라 ÷ 2 칸.</summary>
    private static string RangeText(Part1Attack a) => a.RangeKind switch
    {
        1 => $"사거리 (RANGE + {a.RangeValue}) ÷ 2칸",
        2 => "사거리 무기 + RANGE 보정",
        3 when a.RangeValue <= 1 => "사거리 제자리",
        3 => $"사거리 {Math.Max(1, (a.RangeValue + 1) / 2)}칸",
        4 => "사거리 무기",
        _ => "",
    };

    /// <summary>
    /// 능력치 어빌리티(2~9)가 올리는 값을 어디에 쓰나(사용자 요청 gm-skil-31) — 세기 식 <see cref="Part1Combat.Power"/>(선택자 0 · 7 · 29 = (공격+10) × STR,
    /// 1 · 30 = (DEX+SKILL) × 무게 ÷ 2 — 2연~6연, 4 = (DEX+SKILL) × 무게 ÷ 3 + 공격 × STR ÷ 3 — 살 · 비 · 혼 · 파 · 참 · 격 · 광, 2 = 총, 3 = 활, 5 · 6 · 8 · 10~24 = INT²),
    /// 회피 <see cref="Part1Combat.EvadeChance"/> · 막기 <see cref="Part1Combat.Blocks"/>(맞는 쪽 DEX ↔ 때린 쪽 명중값 + DEX), SPD 는 이동력(× 2, <c>0x1003ece0</c>),
    /// RANGE 는 사거리 갈래 1 기술에 더한다(Part1Rules.RangeQuarters), AC · MR 은 피해를 나누고 MR 은 상태이상 확률도 나눈다(<see cref="Part1Combat.StatusLands"/>).
    /// </summary>
    private static string StatUseText(int ability) => ability switch
    {
        2 => "쓰는 곳: 칼 · 창 · 빔소드 기본 공격 세기 · 활 공격 · 칼 필살기(살 · 비 · 혼 …) 세기",
        3 => "쓰는 곳: 총 · 활 공격 세기 · 연속 공격(2연~6연) · 칼 필살기 세기",
        4 => "쓰는 곳: 연속 공격(2연~6연) · 칼 필살기(살 · 비 · 혼 · 파 · 참 · 격 · 광) 세기$n"
             + "회피 · 방어 어빌리티로 피하고 막을 확률이 오른다$n"
             + "때릴 때는 상대가 피하고 막을 확률이 내려간다",
        5 => "쓰는 곳: 마법 세기(INT × INT)",
        6 => "쓰는 곳: 사거리가 능력치를 따르는 기술의 사거리",
        7 => "쓰는 곳: 이동력(SPD × 2)",
        8 => "쓰는 곳: 받는 물리 피해를 나눈다",
        9 => "쓰는 곳: 받는 마법 피해를 나누고, 상태이상에 덜 걸린다",
        _ => "",
    };

    /// <summary>패시브 효과 — 능력치(<see cref="Part1Chr.Stat"/> · <see cref="Part1Chr.MaxHp"/>), 장비 · 탈것 · 자세(분석-파트1-전투).</summary>
    private static string PassiveText(int ability, int level) => ability switch
    {
        1 => $"최대 HP +100 × 레벨 (Lv {level}: +{100 * level})",
        >= 2 and <= 9 => $"{new[] { "", "", "STR", "SKILL", "DEX", "INT", "RANGE", "SPD", "AC", "MR" }[ability]} +5 × 레벨 (Lv {level}: +{5 * level})",
        >= 10 and <= 21 => "이 갈래의 무기를 장비할 수 있다",
        22 => "갑옷을 입을 수 있다",
        76 => "막기(DEX ½) — 막으면 장비 방어를 더한다 · 제자리 휴식하면 「방어」 자세: DEX 그대로",
        77 => "물리 회피(DEX ½) · 제자리 휴식하면 「회피」 자세: DEX 그대로",
        78 => "제자리 휴식하면 「반격」 자세 — 맞으면 사거리 안의 상대를 기본 공격으로 되받아친다",
        79 => "제자리 휴식하면 「역습」 자세 — DEX 로 피하고, 피하면 기본 공격으로 되받아친다",
        80 => "막기(DEX ½ 없이) · 제자리 휴식하면 「완전방어」 자세: 늘 막고 방어 ×1.5",
        >= 86 and <= 91 => "그 지형에서 싸우는 데 익숙하다",
        >= 121 and <= 126 => "그 탈것을 탈 수 있다",
        128 => "둘째 손에 칼을 쥘 수 있다",
        _ => "",
    };
}
