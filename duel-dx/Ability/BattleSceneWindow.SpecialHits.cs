using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 핸들러가 자료 범위와 다르게 치는 기술(ba-20 E) — 소닉 블레이드(줄마다 첫 적 하나 + 밀어냄), 크레이지 샷(무작위 M 발).
/// </summary>
internal sealed unsafe partial class BattleSceneWindow
{
    internal const int SonicBladeAbility = 20, CrazyShotAbility = 126, HellLaserAbility = 172;

    internal const int ThunderStormAbility = 75;

    internal bool HasSpecialHit(WorkData w) => w.AbilityId is SonicBladeAbility or CrazyShotAbility or ThunderStormAbility;

    internal IEnumerable<bool> SpecialHitRoutine(UnitState a, WorkData w, WorkData hitWork, int col, int row, List<UnitState> dying)
    {
        if (w.AbilityId == SonicBladeAbility)
        {
            // 소닉 블레이드 0x1008aba0 — 8틱 뒤 검기 셋(가운데·좌·우 줄)이 20px/틱으로 「범위 최대 + 2」칸을 날며
            // 줄마다 처음 만난 적 하나에서 멈춘다(검기 틱 0x100d0d00, 대상 1 = 적). 그 적에게 한 번 치고 남은 칸 수만큼 민다(0x100cab80).
            // 옆 줄은 시전자와 같은 행의 옆 칸에서 출발한다. 전에는 폭 3 줄 안 전원(아군 포함)을 치고 밀지 않았다.
            for (double end = _lastTime + 8 / TicksPerSecond; _lastTime < end;) yield return true;
            int reach = w.AreaMaxQuarters / 4 + 2;
            var (fx, fy) = a.Facing switch { Facing.Up => (0, -1), Facing.Down => (0, 1), Facing.Left => (-1, 0), _ => (1, 0) };
            var (sx, sy) = (fy, fx);
            var found = new List<(UnitState Unit, int K)>();
            foreach (int lane in new[] { 0, 1, -1 })
                for (int k = 0; k <= reach; k++)
                {
                    if (LiveUnitAt(a.Col + lane * sx + k * fx, a.Row + lane * sy + k * fy) is not { } t || t == a || !SeesAsFoe(a, t)) continue;
                    if (found.All(f => f.Unit != t)) found.Add((t, k));
                    break;
                }
            double start = _lastTime;
            foreach (var (t, k) in found.OrderBy(f => f.K))
            {
                while (_lastTime < start + k * 2 / TicksPerSecond) yield return true;   // 검기는 틱마다 반 칸
                ApplyWork(a, hitWork, t, dying);
                if (!dying.Contains(t) && t.Alive)
                    foreach (bool _ in KnockbackRoutine(a, w, t, pushCells: reach - k)) yield return true;
            }
            yield break;
        }

        if (w.AbilityId == ThunderStormAbility)
        {
            // 썬더 스톰 0x10087ff0 — 번개 줄기 넷이 위 → 오른 → 아래 → 왼 사분면 차례로 훑으며 한 명씩 25틱 간격으로 친다
            // (훑개 0x100c9d40, ba-20 E5). 사분면은 시전자 기준 화면 좌표로 가른다. 위·아래 사분면에서 빠지는 유닛 규칙은 따르지 않는다(가설이라).
            int Quadrant(UnitState t)
            {
                int dx = 40 * (t.Col - a.Col), dy = 32 * (t.Row - a.Row) - 12 * (HeightAt(t.Col, t.Row) - HeightAt(a.Col, a.Row));
                int q = -1;
                if (Math.Abs(dx) <= Math.Abs(dy)) q = dy < 0 ? 0 : 2;
                if (Math.Abs(dx) >= Math.Abs(dy)) q = dx > 0 ? 3 : dx < 0 ? 1 : q;
                return q < 0 ? 0 : q;
            }
            var storm = WorkTargets(w, a, col, row).Select(i => _units[i]).OrderBy(t => t.Row).ThenBy(t => t.Col).ToList();
            foreach (int q in new[] { 0, 3, 2, 1 })
                foreach (var t in storm.Where(t => Quadrant(t) == q))
                {
                    if (!t.Alive || dying.Contains(t)) continue;
                    var (tx, ty) = UnitFoot(t);
                    _effects.Add((204, 1, _lastTime, tx, ty));
                    ApplyWork(a, hitWork, t, dying);
                    for (double end = _lastTime + 25 / TicksPerSecond; _lastTime < end;) yield return true;
                }
            yield break;
        }

        // 크레이지 샷 0x10093ce0 — 범위 안 유닛 수 N(편 안 가림)에 따라 M 발: N<4 → 2~4, N<9 → 6~10, 그 밖 → 11~15.
        // 발마다 무작위 한 명의 자리에 rand%90+54(+2)틱 뒤 떨어져 그 칸의 적만 친다 — 적이 하나면 그 하나가 여러 번 맞고, 아군 자리는 헛발이다.
        var cells = AreaCells(w, a, col, row).ToHashSet();
        var all = _units.Where(u => u.Alive && u.OnField && cells.Contains((u.Col, u.Row))).OrderBy(u => u.Row).ThenBy(u => u.Col).ToList();
        if (all.Count == 0) yield break;
        int shots = all.Count < 4 ? _rng.Next(3) + 2 : all.Count < 9 ? _rng.Next(5) + 6 : _rng.Next(5) + 11;
        var due = new List<(double At, UnitState Unit, int Col, int Row)>();
        for (int i = 0; i < shots; i++)
        {
            var u = all[_rng.Next(all.Count)];
            due.Add((_lastTime + (_rng.Next(90) + 56) / TicksPerSecond, u, u.Col, u.Row));
        }
        foreach (var (at, u, c, r) in due.OrderBy(d => d.At))
        {
            while (_lastTime < at) yield return true;
            if (u.Alive && !dying.Contains(u) && (u.Col, u.Row) == (c, r) && SeesAsFoe(a, u)) ApplyWork(a, hitWork, u, dying);
        }
    }
}
