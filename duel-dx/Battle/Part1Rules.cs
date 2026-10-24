using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 판의 수치 식 — 사용자 결정 「중간안」대로 판·맵·사건은 파트2 틀을 쓰고 피해만 파트1 식(<see cref="Part1Combat"/>)으로 돌린다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 9·10절. 피해 = 세기 ÷ 방어(AC, 마법이면 MR) · ±1/6 흔들기 · 무기 치명 × 2.
/// 회피는 대상이 어빌리티 77 을 가졌을 때만 굴린다. 능력치는 소울 게이지로 깎는다(<see cref="Part1Combat.Scaled"/> — 파트2 SOUL 을 그 게이지로 쓴다).
/// 파트1 유닛은 지금 기본 공격(파트2 살라딘의 기본 work)만 써서, 그것을 세기 식 0(무기1 · STR) · 명중 100 으로 친다.
/// </remarks>
internal sealed class Part1Rules
{
    private IReadOnlyList<Part1Item>? _items;
    private readonly Dictionary<int, Part1Fighter?> _fighters = [];

    internal Part1Fighter? Fighter(UnitState u)
    {
        if (u.ChrCode < Part1Game.ChrBase) return null;
        if (!_fighters.TryGetValue(u.ChrCode, out var f))
        {
            _items ??= Part1Item.LoadAll();
            _fighters[u.ChrCode] = f = Part1Chr.Load(u.ChrCode - Part1Game.ChrBase) is { } chr ? Part1Fighter.From(chr, _items) : null;
        }
        return f;
    }

    /// <summary>
    /// 피해 굴림 — 파트2 <c>GameData.Resolve</c> 와 같은 꼴(결과 2 맞음 · 3 빗나감)로 돌려준다. 파트1 인물이 아니면 null(파트2 식을 쓴다).
    /// </summary>
    internal (int Amount, int Result, bool Critical)? Resolve(Random rng, UnitState a, UnitState t, WorkData w)
    {
        if (!w.IsDamage || Fighter(a) is not { } fa || Fighter(t) is not { } ft) return null;
        const int selector = 0, accuracy = 100;
        if (!Part1Combat.Hits(accuracy, rng.Next(32768))) return (0, 3, false);
        if (ft.CanEvade && rng.Next(100) < Part1Combat.EvadeChance(ft.Stat(Part1Chr.Dex, t.Soul), false, fa.HitValue, fa.Stat(Part1Chr.Dex, a.Soul)))
            return (0, 3, false);
        int power = Part1Combat.Power(selector, fa.AttackerAt(a.Soul));
        int element = fa.Weapon1?.Element ?? 0;
        bool magic = selector is 5 or 6 or 8 or (>= 10 and <= 24);
        int defense = ft.Stat(magic ? Part1Chr.Mr : Part1Chr.Ac, t.Soul);
        int damage = Part1Combat.Damage(power, defense, ft.Resist(element), null, rng.Next(32768));
        damage = Part1Combat.Critical(damage, fa.Weapon1?.Critical ?? 0, rng.Next(32768), out bool crit);
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 hit {a.ChrCode} → {t.ChrCode}: 세기 {power} ÷ 방어 {defense} = {damage}{(crit ? " 치명" : "")}" + Environment.NewLine);
        return (Math.Max(0, damage), 2, crit);
    }
}
