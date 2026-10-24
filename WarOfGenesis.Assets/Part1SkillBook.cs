namespace WarOfGenesis.Assets;

/// <summary>
/// 파트1 어빌리티로 스킬을 만든다 — 스킬 파일(<c>assets/data/skills/200NN.json</c>)이 없는 파트1 어빌리티(화이어 애로우 · 힐 · 연 …)도
/// 전투 어빌리티 목록에 뜨고 쓸 수 있게(사용자 보고: 어윈 록슬리의 기술이 목록에 없다).
/// </summary>
/// <remarks>
/// <para>어빌리티 20000 + Abi 번호, 레벨 L 의 work = 20000 + (Abi 의 Attack + L − 1) — 연(Attack 11 「2연」)의 Lv2 가 12 「3연」인 것처럼 레벨마다 다음 칸이다.
/// 이름의 끝 숫자를 뺀 머리가 같은 동안 레벨을 잇는다(「화이어 애로우1」 「화이어 애로우2」 …).</para>
/// <para>피해 · 명중 · 소울 비용은 전투가 Attack.dat 으로 다시 셈한다(Part1Rules · SoulCostFor). 여기서는 겨냥 틀만 잡는다:
/// 세기 식(+0x42) 6 은 회복 — 파트2 힐(어빌리티 9)의 틀, 0~4 · 29~31(무기 기술)은 파트2 연(1), 그 밖은 엘레맨탈 파이어(29).
/// 사거리 = (+0x21 갈래 1 이면 RANGE 능력치 +) +0x24 ÷ 4 칸, 범위 = +0x38 ÷ 4 칸, 누가 맞나 = +0x31(겨누기) · +0x40(효과 범위) 거르기(<see cref="AimFields"/>). 10~28(상태이상)은 피해가 아닌 기술(종류 2)로 둔다 — 효과는 아직 없다.</para>
/// <para>이미 스킬 파일이 있는 어빌리티는 건드리지 않는다. 같은 work 를 쓰는 숨은 덧기술(라이트닝볼츠 20991)은 이 어빌리티가 덮어써도 덧기술 길은 work 번호로 찾아 그대로다.</para>
/// </remarks>
public static class Part1SkillBook
{
    public static void AddTo(List<SkillFile> skills)
    {
        if (Part1Game.Root is null) return;
        var abilities = Part1Ability.LoadAll();
        var attacks = Part1Attack.LoadAll();
        if (abilities.Count == 0 || attacks.Count == 0) return;
        var have = skills.Select(s => s.Ability).ToHashSet();
        // 여러 어빌리티가 같은 Attack 을 쓰면(연 · 다하카 두번 물기 = 11) work 번호가 겹친다 — 뒤의 것은 1000 씩 비켜 준다(<see cref="AttackIdOf"/>).
        var used = skills.SelectMany(s => s.Levels).Select(l => l.Work).ToHashSet();
        var heal = skills.FirstOrDefault(s => s.Ability == 9)?.Common;
        var melee = skills.FirstOrDefault(s => s.Ability == 1)?.Common;
        var magic = skills.FirstOrDefault(s => s.Ability == 29)?.Common;
        if (heal is null || melee is null || magic is null) return;
        foreach (var ab in abilities)
        {
            int id = Part1Game.ChrBase + ab.Id;
            if (ab.AttackId <= 0 || ab.Kind is not (1 or 2) || have.Contains(id) || Part1Attack.ById(attacks, ab.AttackId) is not { } first) continue;
            string head = Head(first.Name);
            var levels = new List<SkillLevel>();
            for (int lv = 1; lv <= 9; lv++)
            {
                if (Part1Attack.ById(attacks, ab.AttackId + lv - 1) is not { } a || (lv > 1 && Head(a.Name) != head)) break;
                int work = Part1Game.ChrBase + a.Id;
                while (used.Contains(work) && work < ushort.MaxValue - 1000) work += WorkStride;
                used.Add(work);
                levels.Add(new SkillLevel { Work = work, Level = lv, Fields = AimFields(a) });
            }
            int sel = first.Formula;
            bool isHeal = sel == 6, weapon = sel is (>= 0 and <= 4) or (>= 29 and <= 31), status = sel is >= 10 and <= 28;
            var common = new Dictionary<string, int>(isHeal ? heal : weapon ? melee : magic)
            {
                // 사거리는 4분의 1칸(분석-파트1-전투 29절). 싸울 때는 RangeMaxOf 가 Attack.dat 갈래로 다시 셈한다(갈래 1 은 시전자 RANGE 능력치를 더한다) —
                // 여기 값은 화면 글 · 미리보기용으로, 갈래 1 은 흔한 RANGE 10 을 더해 둔다.
                ["rangeMax"] = Math.Max(1, (first.RangeValue + (first.RangeKind == 1 ? 10 : 0)) / 4),
                ["rangeMin"] = isHeal ? 0 : 1,
                ["accuracy"] = first.Accuracy > 0 ? first.Accuracy : 100,
                ["kind"] = isHeal ? 1 : status ? 2 : 0,
                ["soul"] = first.Cost,
                // 빌린 파트2 틀의 덧효과(엘레맨탈 파이어의 화염 · DEP 저하 · 둔화 따위)는 떼어 낸다 — 파트1 기술의 효과는 Part1Rules · Part1Support 가 맡는다
                // (사용자 화면: 화이어 애로우에 「화염 · DEP↓ · 둔화」가 붙었다). AI 가 대상 둘 이상일 때만 쓰던 조건(minTargets 2)도 1 로.
                ["bonus1Stat"] = 0, ["bonus2Stat"] = 0, ["bonus3Stat"] = 0,
                ["minTargets"] = 1,
                // 엘레맨탈 파이어 틀의 효과 범위 최소 1칸(가운데 칸 빼기)도 뗀다 — 누른 칸의 적이 대상에서 빠져 록슬리 마법이 아무도 못 맞혔다(사용자 보고).
                ["areaMin"] = 0,
            };
            foreach (var (k, v) in AimFields(first)) common[k] = v;
            skills.Add(new SkillFile { Ability = id, Name = ab.Name, MaxLevel = levels.Count, Common = common, Levels = levels });
        }
    }

    /// <summary>
    /// 누가 맞나 · 범위 — Attack 의 거르기 바이트를 파트2 대상 방식으로 옮긴다(분석-파트1-전투 「기술 대상 거르기」).
    /// 효과 범위 거르기 <c>+0x40</c>: 1 적만 → 1 · 3 같은 편·자신만 → 4 · 5 유닛 누구나 → 5 · 2 아무 칸(범위 안 모두) → 3.
    /// 겨누기 <c>+0x31</c>: 1 · 3 · 5 는 그 유닛을 누른다(같은 값으로) · 2 아무 칸 — 범위가 한 칸뿐이면 그 칸의 유닛 하나를 고르는 것과 같아 효과 거르기의 유닛 방식으로 둔다.
    /// 그 밖(4 자기 자리 · 6 · 7 돌진 길 · 0)은 빌린 틀의 것을 그대로 둔다. 범위 반경 = <c>+0x38</c> ÷ 4 칸(<see cref="Part1Attack.AreaTiles"/>).
    /// 전에는 엘레맨탈 파이어 틀의 효과 대상 3(아무 칸)을 그대로 써 적 마법이 제 편 몬스터까지 맞혔다(사용자 보고: btl 20007 이븐시나의 아이스 미사일).
    /// </summary>
    internal static Dictionary<string, int> AimFields(Part1Attack a)
    {
        static int? Mode(int filter) => filter switch { 1 => 1, 3 => 4, 5 => 5, 2 => 3, _ => null };
        var f = new Dictionary<string, int>();
        int area = a.AreaTiles();
        f["areaMax"] = area;
        if (Mode(a.AreaFilter) is { } am) f["areaMode"] = am;
        int? tm = a.AimFilter switch
        {
            1 or 3 or 5 => Mode(a.AimFilter),
            2 => area == 0 ? Mode(a.AreaFilter) : 3,
            _ => null,
        };
        if (tm is { } t) f["targetMode"] = t;
        return f;
    }

    /// <summary>겹친 work 를 비켜 주는 폭 — Attack.dat 는 1000 칸이 안 된다.</summary>
    public const int WorkStride = 1000;

    /// <summary>파트1 work 번호의 Attack.dat 번호 — 20000 위의 1000 나머지.</summary>
    public static int AttackIdOf(int work) => (work - Part1Game.ChrBase) % WorkStride;

    private static string Head(string name) => name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Trim();
}
