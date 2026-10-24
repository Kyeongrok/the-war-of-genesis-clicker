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
/// 사거리 = +0x24 ÷ 2 칸, 범위 = +0x31 − 1 칸(가설). 10~28(상태이상)은 피해가 아닌 기술(종류 2)로 둔다 — 효과는 아직 없다.</para>
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
                levels.Add(new SkillLevel { Work = work, Level = lv });
            }
            int sel = first.Formula;
            bool isHeal = sel == 6, weapon = sel is (>= 0 and <= 4) or (>= 29 and <= 31), status = sel is >= 10 and <= 28;
            var common = new Dictionary<string, int>(isHeal ? heal : weapon ? melee : magic)
            {
                ["rangeMax"] = Math.Max(1, (first.RangeValue + 1) / 2),
                ["rangeMin"] = isHeal ? 0 : 1,
                ["areaMax"] = Math.Max(0, first.Raw.GetValueOrDefault(0x31) - 1),
                ["accuracy"] = first.Accuracy > 0 ? first.Accuracy : 100,
                ["kind"] = isHeal ? 1 : status ? 2 : 0,
                ["soul"] = first.Cost,
                // 빌린 파트2 틀의 덧효과(엘레맨탈 파이어의 화염 · DEP 저하 · 둔화 따위)는 떼어 낸다 — 파트1 기술의 효과는 Part1Rules · Part1Support 가 맡는다
                // (사용자 화면: 화이어 애로우에 「화염 · DEP↓ · 둔화」가 붙었다). AI 가 대상 둘 이상일 때만 쓰던 조건(minTargets 2)도 1 로.
                ["bonus1Stat"] = 0, ["bonus2Stat"] = 0, ["bonus3Stat"] = 0,
                ["minTargets"] = 1,
            };
            skills.Add(new SkillFile { Ability = id, Name = ab.Name, MaxLevel = levels.Count, Common = common, Levels = levels });
        }
    }

    /// <summary>겹친 work 를 비켜 주는 폭 — Attack.dat 는 1000 칸이 안 된다.</summary>
    public const int WorkStride = 1000;

    /// <summary>파트1 work 번호의 Attack.dat 번호 — 20000 위의 1000 나머지.</summary>
    public static int AttackIdOf(int work) => (work - Part1Game.ChrBase) % WorkStride;

    private static string Head(string name) => name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Trim();
}
