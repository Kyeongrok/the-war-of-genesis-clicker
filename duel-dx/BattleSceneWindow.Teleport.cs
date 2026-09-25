using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 하이 텔레포트(어빌리티 37, work 397·575~593) — 고른 칸 둘레 어딘가로 순간이동한다.
/// </summary>
/// <remarks>
/// 원본 핸들러 <c>0x1008c8a0</c>(단계 0~6):
/// <list type="number">
/// <item>0 — 제 자리에 사라지는 빛 381:0(267틱)·210:3 을 띄우고, 몸 복제를 80틱에 걸쳐 흐리게 한다(<c>0x100c5de0(80, 1)</c>).
/// 그리고 <b>기술 범위 안 칸 목록</b>(<c>0x100defb0</c>, 최대 400칸)에서 무작위로 한 칸을 뽑되, 설 수 있는 빈 칸(<c>0x100d99a0</c>)이
/// 나올 때까지 500번까지 다시 뽑는다. 범위(반지름)가 레벨 1 에서 10, 19·20 에서 1 이라 <b>레벨이 오를수록 고른 칸에 가깝게</b> 떨어진다
/// — 설명 「레벨이 오름에 따라 원하는 장소로 정확하게 갈수 있게된다」. 못 찾으면 제자리.</item>
/// <item>1·2 — 80틱, 30틱 기다린 뒤 새 칸에 선다(카메라가 따라간다).</item>
/// <item>3·4 — 새 자리에 나타나는 빛 381:1·210:3, 50틱.</item>
/// <item>5·6 — 몸을 80틱에 걸쳐 되살리고(<c>0x100c5de0(80, 0)</c>) 서기 동작.</item>
/// </list>
/// 자료로는 위력 0 의 「피해」 기술(범위 방식 3)이라, 예전에는 반지름 10 안의 모두에게 0 피해·Miss 를 내고 제자리에 있었다(사용자 보고).
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int HighTeleportAbility = 37;

    private static bool IsTeleportWork(WorkData w) => w.AbilityId == HighTeleportAbility;

    /// <summary>범위 안에서 설 수 있는 빈 칸을 원본처럼 무작위로 뽑는다 — 없으면 null.</summary>
    private (int Col, int Row)? TeleportLanding(WorkData w, UnitState user, int col, int row)
    {
        var cells = AreaCells(w, user, col, row);
        if (cells.Count == 0) return null;
        for (int tries = 0; tries < 500; tries++)
        {
            var (c, r) = cells[_rng.Next(cells.Count)];
            if ((c, r) == (user.Col, user.Row)) return (c, r);
            if (LiveUnitAt(c, r) != null) continue;
            if (_map is { } map && (c >= map.Cols || r >= map.Rows || (map.FlagsAt(c, r) & 0x9) != 0)) continue;
            return (c, r);
        }
        return null;
    }

    /// <summary>순간이동 연출 — 사라지고(80틱) · 30틱 · 새 칸에 나타나는 빛 · 50틱 · 되살아남(80틱).</summary>
    private IEnumerable<bool> TeleportRoutine(WorkData w, UnitState user, int col, int row)
    {
        const double Tick = 1 / TicksPerSecond;
        var landing = TeleportLanding(w, user, col, row);
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                               $"teleport work {w.Id}: from ({user.Col},{user.Row}) aim ({col},{row}) radius {w.AreaArg} → {landing?.ToString() ?? "제자리"}" + Environment.NewLine);
        for (double start = _lastTime, end = start + 80 * Tick; _lastTime < end;)
        {
            user.Fade = Math.Max(0, 1 - (_lastTime - start) / (80 * Tick));
            yield return true;
        }
        user.Fade = 0;
        for (double end = _lastTime + 30 * Tick; _lastTime < end;) yield return true;
        if (landing is var (lc, lr) && (lc, lr) != (user.Col, user.Row))
        {
            user.WarpTo(lc, lr);
            user.OriginCol = lc;
            user.OriginRow = lr;
        }
        var (x, y) = UnitFoot(user);
        _effects.Add((381, 1, _lastTime, x, y));
        _effects.Add((210, 3, _lastTime, x, y));
        if (_effectTables.GetValueOrDefault(381)?.Clips.GetValueOrDefault(1) is { } appear)
            foreach (var (t, sound) in appear.Sounds) _pendingSounds.Add((_lastTime + t / TicksPerSecond, sound));
        for (double end = _lastTime + 50 * Tick; _lastTime < end;) yield return true;
        for (double start = _lastTime, end = start + 80 * Tick; _lastTime < end;)
        {
            user.Fade = Math.Min(1, (_lastTime - start) / (80 * Tick));
            yield return true;
        }
        user.Fade = 1;
    }

    /// <summary>리콜(어빌리티 61) — 「아군 한명을 자신의 주위로 소환한다」. 전에는 종류 1(회복)로만 돌아 아무도 오지 않았다(사용자 보고).</summary>
    private const int RecallAbility = 61;

    /// <summary>시전자 둘레에서 설 수 있는 가장 가까운 빈 칸 — 한 칸 둘레부터 넓혀 간다. 없으면 null.</summary>
    private (int Col, int Row)? RecallLanding(UnitState caster, UnitState target)
    {
        for (int d = 1; d <= 4; d++)
        {
            var ring = new List<(int, int)>();
            for (int dy = -d; dy <= d; dy++)
                for (int dx = -d; dx <= d; dx++)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) != d) continue;
                    int c = caster.Col + dx, r = caster.Row + dy;
                    if (c < 0 || r < 0 || c >= Cols || r >= Rows) continue;
                    if (_map is { } map && (c >= map.Cols || r >= map.Rows || (map.FlagsAt(c, r) & 0x9) != 0)) continue;
                    if (LiveUnitAt(c, r) is { } other && other != target) continue;
                    if (ObjectAt(c, r) is { Data.BlocksStanding: true }) continue;
                    ring.Add((c, r));
                }
            if (ring.Count > 0) return ring[_rng.Next(ring.Count)];
        }
        return null;
    }

    /// <summary>리콜 연출 — 대상이 사라지고(40틱) 시전자 옆에 빛(210:3)과 함께 나타난다(40틱).</summary>
    private IEnumerable<bool> RecallRoutine(UnitState caster, UnitState target)
    {
        const double Tick = 1 / TicksPerSecond;
        if (target == caster || RecallLanding(caster, target) is not var (lc, lr)) { Toast("불러올 자리가 없습니다"); yield break; }
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                               $"recall: {target.ChrCode} ({target.Col},{target.Row}) → ({lc},{lr}) beside {caster.ChrCode} ({caster.Col},{caster.Row})" + Environment.NewLine);
        for (double start = _lastTime, end = start + 40 * Tick; _lastTime < end;)
        {
            target.Fade = Math.Max(0, 1 - (_lastTime - start) / (40 * Tick));
            yield return true;
        }
        target.WarpTo(lc, lr);
        target.OriginCol = lc;
        target.OriginRow = lr;
        target.Facing = caster.Facing;
        var (x, y) = UnitFoot(target);
        _effects.Add((210, 3, _lastTime, x, y));
        for (double start = _lastTime, end = start + 40 * Tick; _lastTime < end;)
        {
            target.Fade = Math.Min(1, (_lastTime - start) / (40 * Tick));
            yield return true;
        }
        target.Fade = 1;
    }
}
