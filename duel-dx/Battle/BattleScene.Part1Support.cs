namespace DuelDx;

using WarOfGenesis.Assets;

/// <summary>
/// 파트1 보조 마법의 실제 효과 — 세기 식(Attack +0x42)으로 가른다(볼트 분석-파트1-기술-b 9절 「Game effects」).
/// (6 힐 · 힐 윈드는 파트2 회복 갈래에서 양만 바꾼다 — <see cref="Part1Rules.HealAmount"/>.) 16 큐어: 상태 · 강화 · 속성 부여를 모두 지운다.
/// 15 블래스(소울 +20 더) · 17 스피디(SPD +8) · 20 배리어(마법 피해 0, 그 밖의 피해 ×2) · 22 쉐도우 실드(AC ×1.5 · MR ×0.5)
/// · 23 블러드 러스트(무기 공격 ×1.5)는 <see cref="Part1Rules.Buffs"/> 에 남은 차례로 건다. 21 커즈: 소울 0.
/// </summary>
/// <remarks>
/// 길이 = 적에게 세기 ÷ MR ÷ 10 + (+0x58), 같은 편에게 √세기 ÷ 10 + (+0x58)(원본, 255 에서 자름). 원본이 이 값을 무엇마다 줄이는지는 못 찾아서
/// 리메이크는 그 사람 차례마다 10씩 줄인다(가설 — 스피디 40~60 = 4~6 차례).
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    private static readonly Dictionary<int, string> Part1BuffNames = new()
    {
        [15] = "블래스", [17] = "스피디", [20] = "배리어", [22] = "쉐도우 실드", [23] = "블러드 러스트",
    };

    /// <summary>파트1 보조 마법이면 그 효과를 걸고 true — 파트2 보조 틀(ApplyAilments)은 거치지 않는다.</summary>
    private bool ApplyPart1Support(UnitState a, UnitState t, WorkData w, int result)
    {
        if (!host._scene.IsPart1 || Part1.AttackOf(w) is not { } attack) return false;
        // 희생 · 해체대법 · 도발 — 핸들러가 t0 에 직접 고친다(0x1006c0a0 · 0x1006c8f0 · 0x1006d360, 분석-파트1-기술-b 10절). 소울 칸 +0xe8(최대 150).
        switch (attack.Id)
        {
            case 376 when t != a:                     // 희생 — 같은 편 모두 HP 가득 · 소울 150(시전자는 그대로)
                t.Hp = t.MaxHp;
                t.Soul = Math.Min(t.MaxSoul, 150);
                Popup(t, "희생", HealColor2, 12, rise: 88);
                MarkBuffed(a, t, w);
                return true;
            case 377:                                 // 해체대법 — 대상 HP 1 · 소울 150
                t.Hp = Math.Min(t.Hp, 1);
                t.Soul = Math.Min(t.MaxSoul, 150);
                Popup(t, "해체대법", AilmentPopupColor, 12, rise: 88);
                return true;
            case 382:                                 // 도발 — 소울 −20(0 아래로는 안)
                t.Soul = Math.Max(0, t.Soul - 20);
                Popup(t, "도발", AilmentPopupColor, 12, rise: 88);
                return true;
            case 376:
                return true;
        }
        int sel = attack.Formula;
        if (sel is not (15 or 16 or 17 or 20 or 21 or 22 or 23)) return false;
        if (result == 3)
        {
            ShowNumber(t, host._db?.T(42) is { Length: > 0 } miss ? miss : "Miss", MissColor);
            return true;
        }
        int power = Part1.Fighter(a) is { } fa ? Part1Combat.Power(sel, fa.AttackerAt(a.Soul)) + attack.PowerAdd * attack.PowerAdd : 0;
        switch (sel)
        {
            case 16:
                t.ClearStatus();
                Part1.Buffs.Remove(t);
                Part1.Enchant.Remove(t);
                Popup(t, "큐어", HealColor2, 12, rise: 88);
                break;
            case 21:
                t.Soul = 0;
                Popup(t, "커즈", AilmentPopupColor, 12, rise: 88);
                break;
            default:
                bool ally = !Hostile(a, t);
                int mr = Part1.Fighter(t)?.Stat(Part1Chr.Mr, t.Soul) ?? 1;
                int length = Math.Min(255, (ally ? (int)Math.Sqrt(power) / 10 : power / Math.Max(1, mr) / 10) + (attack.Raw.GetValueOrDefault(0x58) & 0xff));
                if (!Part1.Buffs.TryGetValue(t, out var buffs)) Part1.Buffs[t] = buffs = [];
                buffs[sel] = Math.Max(buffs.GetValueOrDefault(sel), Math.Max(10, length));
                Popup(t, Part1BuffNames[sel], ally ? HealColor2 : AilmentPopupColor, 12, rise: 88);
                break;
        }
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 support {a.ChrCode} → {t.ChrCode} {attack.Name}(식 {sel}): 세기 {power}{(Part1.Buffs.GetValueOrDefault(t)?.GetValueOrDefault(sel) is > 0 and var left ? $" · 길이 {left}" : "")} HP {t.Hp}/{t.MaxHp}" + Environment.NewLine);
        MarkBuffed(a, t, w);
        host.StatusScr.RefreshUnitStats(t);
        return true;
    }

    /// <summary>그 사람 차례가 오면 파트1 강화의 남은 길이를 10 줄인다(<see cref="ApplyPart1Support"/> 의 가설).</summary>
    private void TickPart1Buffs(UnitState u)
    {
        if (!Part1.Buffs.TryGetValue(u, out var buffs)) return;
        foreach (int sel in buffs.Keys.ToList())
            if ((buffs[sel] -= 10) <= 0) buffs.Remove(sel);
        if (buffs.Count == 0) Part1.Buffs.Remove(u);
    }
}
