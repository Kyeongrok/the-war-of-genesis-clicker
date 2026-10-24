using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 판의 수치 식 — 사용자 결정 「중간안」대로 판·맵·사건은 파트2 틀을 쓰고 피해만 파트1 식(<see cref="Part1Combat"/>)으로 돌린다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 9·10절. 피해 = 세기 ÷ 방어(AC, 마법이면 MR) · ±1/6 흔들기 · 무기 치명 × 2.
/// 회피는 대상이 어빌리티 77 을 가졌을 때만 굴린다. 능력치는 소울 게이지로 깎는다(<see cref="Part1Combat.Scaled"/> — 파트2 SOUL 을 그 게이지로 쓴다).
/// 기본 공격(파트2 살라딘의 기본 work)은 세기 식 0(무기1 · STR) · 명중 100 으로, 파트1 기술(work 20000 + Attack.dat 번호)은 그 칸의 식으로 친다.
/// </remarks>
internal sealed class Part1Rules(Func<int, Part1Chr?> load)
{
    private IReadOnlyList<Part1Item>? _items;
    private IReadOnlyList<Part1Attack>? _attacks;

    /// <summary>파트1 기술(work 20000 + <c>Attack.dat</c> 번호 — 헬 스트라이크 20288 따위)의 Attack.dat 칸. 기본 공격이면 null.</summary>
    internal Part1Attack? AttackOf(WorkData w) =>
        w.Id > Part1Game.ChrBase && Part1Attack.ById(_attacks ??= Part1Attack.LoadAll(), w.Id - Part1Game.ChrBase) is { } attack ? attack : null;
    private readonly Dictionary<int, Part1Fighter?> _fighters = [];

    internal Part1Fighter? Fighter(UnitState u)
    {
        if (u.ChrCode < Part1Game.ChrBase) return null;
        if (!_fighters.TryGetValue(u.ChrCode, out var f))
        {
            _items ??= Part1Item.LoadAll();
            _fighters[u.ChrCode] = f = load(u.ChrCode - Part1Game.ChrBase) is { } chr ? Part1Fighter.From(chr, _items) : null;
        }
        return f;
    }

    /// <summary>자란 모습이 바뀐 인물(없으면 모두)을 다시 읽게 한다.</summary>
    internal void Forget(int? chrCode = null)
    {
        if (chrCode is { } code) _fighters.Remove(code);
        else _fighters.Clear();
    }

    /// <summary>
    /// 피해 굴림 — 파트2 <c>GameData.Resolve</c> 와 같은 꼴(결과 2 맞음 · 3 빗나감)로 돌려준다. 파트1 인물이 아니면 null(파트2 식을 쓴다).
    /// </summary>
    internal (int Amount, int Result, bool Critical)? Resolve(Random rng, UnitState a, UnitState t, WorkData w)
    {
        if (!w.IsDamage || Fighter(a) is not { } fa || Fighter(t) is not { } ft) return null;
        // 기술이면 Attack.dat 의 세기 식(+0x42) · 명중률(+0x48) · 속성(+0x64) · 더하는 세기(+0x44 제곱) · 곱(+0x66)을 쓴다.
        // 상태이상 기술(선택자 10~28)은 피해가 아니어서 파트2 식으로 넘긴다. 기본 공격은 세기 식 0(무기1 · STR) · 명중 100.
        var attack = AttackOf(w);
        int selector = attack?.Formula ?? 0;
        if (selector is >= 10 and <= 28) return null;
        int accuracy = attack is { Accuracy: > 0 } ? attack.Accuracy : 100;
        if (!Part1Combat.Hits(accuracy, rng.Next(32768))) return (0, 3, false);
        if (ft.CanEvade && rng.Next(100) < Part1Combat.EvadeChance(ft.Stat(Part1Chr.Dex, t.Soul), false, fa.HitValue, fa.Stat(Part1Chr.Dex, a.Soul)))
            return (0, 3, false);
        int power = Part1Combat.Power(selector, fa.AttackerAt(a.Soul));
        if (attack != null) power = (power + attack.PowerAdd * attack.PowerAdd) * Math.Max(1, attack.Hits);
        int element = attack is { Element: > 0 } ? attack.Element : fa.Weapon1?.Element ?? 0;
        bool magic = selector is 5 or 6 or 8 or (>= 10 and <= 24);
        int defense = ft.Stat(magic ? Part1Chr.Mr : Part1Chr.Ac, t.Soul);
        int damage = Part1Combat.Damage(power, defense, ft.Resist(element), null, rng.Next(32768));
        damage = Part1Combat.Critical(damage, fa.Weapon1?.Critical ?? 0, rng.Next(32768), out bool crit);
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 hit {a.ChrCode} → {t.ChrCode}{(attack != null ? $" {attack.Name}(식 {selector})" : "")}: 세기 {power} ÷ 방어 {defense} = {damage}{(crit ? " 치명" : "")}" + Environment.NewLine);
        return (Math.Max(0, damage), 2, crit);
    }
}
