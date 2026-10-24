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
            lines.Add(kind);
            var where = new List<string>();
            if (reach && RangeText(a) is { Length: > 0 } range) where.Add(range);
            int area = a.Raw.GetValueOrDefault(0x31) - 1;
            if (reach && area > 0) where.Add($"범위 {area}칸");
            if (a.Hits > 1) where.Add($"타수 {a.Hits}");
            if (where.Count > 0) lines.Add(string.Join(" · ", where));
            var power = new List<string>();
            if (a.PowerAdd > 0) power.Add($"세기 +{a.PowerAdd}²");
            if (a.Accuracy > 0 && a.Accuracy < 100) power.Add($"명중 {a.Accuracy}%");
            if (reach && a.Cost > 0) power.Add($"소울 {a.Cost}");
            if (power.Count > 0) lines.Add(string.Join(" · ", power));
            if (reach && level > 0 && level < max && Part1Attack.ById(_attacks, ab.AttackId + level) is { } next && next.Cost != a.Cost)
                lines.Add($"다음 Lv: 소울 {next.Cost}");
        }
        else if (PassiveText(ability, shown) is { Length: > 0 } passive) lines.Add(passive);

        // 직업 바꾸기 조건(Job.dat 머리 짝 다섯 — 그 어빌리티를 그 레벨 이상, 0x1009e6f0)에 쓰이면 그 직업들.
        _jobs ??= Part1Job.LoadAll();
        var needs = _jobs.Where(j => j.Firsts.Any(f => f.A == ability)).Select(j => $"{j.Name}(Lv {Math.Max(1, j.Firsts.First(f => f.A == ability).B)})").Distinct().ToList();
        if (needs.Count > 0) lines.Add("전직 조건: " + string.Join(" · ", needs.Take(4)) + (needs.Count > 4 ? $" 외 {needs.Count - 4}" : ""));

        if (ab.Kind is 2 or 4 && ab.ItemId > 0)
        {
            _items ??= Part1Item.LoadAll();
            string item = Part1Item.ById(_items, ab.ItemId)?.Name ?? $"아이템 {ab.ItemId}";
            lines.Add(ab.Kind == 4 ? $"{item} 을(를) 지녀야 부를 수 있다" : $"{item} 을(를) 장비해야 쓴다");
        }
        return string.Join("$n", lines);
    }

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
            13 => "언데드에게만 듣는다 — INT²",
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

    /// <summary>패시브 효과 — 능력치(<see cref="Part1Chr.Stat"/> · <see cref="Part1Chr.MaxHp"/>), 장비 · 탈것 · 자세(분석-파트1-전투).</summary>
    private static string PassiveText(int ability, int level) => ability switch
    {
        1 => $"최대 HP +100 × 레벨 (Lv {level}: +{100 * level})",
        >= 2 and <= 9 => $"{new[] { "", "", "STR", "SKILL", "DEX", "INT", "RANGE", "SPD", "AC", "MR" }[ability]} +5 × 레벨 (Lv {level}: +{5 * level})",
        >= 10 and <= 21 => "이 갈래의 무기를 장비할 수 있다",
        22 => "갑옷을 입을 수 있다",
        76 => "자세 「방어」 — 방패 + DEX 로 막는다(막으면 장비 방어를 더한다)",
        77 => "자세 「회피」 — DEX 로 물리 공격을 피한다",
        78 => "자세 「반격」 — 맞은 뒤 되받아친다",
        79 => "자세 「역습」 — 피한 뒤 되받아친다",
        80 => "자세 「완전방어」 — 늘 막고 방어 ×1.5",
        >= 86 and <= 91 => "그 지형에서 싸우는 데 익숙하다",
        >= 121 and <= 126 => "그 탈것을 탈 수 있다",
        128 => "둘째 손에 칼을 쥘 수 있다",
        _ => "",
    };
}
