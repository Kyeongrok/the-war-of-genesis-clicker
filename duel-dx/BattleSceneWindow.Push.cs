using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 인물을 옮기는 기술들(fg-21 ⑧·⑨)과 여러 번 치는 기술(⑩) — 원본 핸들러가 이동기(<c>0x100ca7f0</c>·<c>0x100cb4a0</c>)로 유닛을 미끄러뜨리거나
/// 피해 메시지를 여러 번 보내는 것을 옮겼다(분석-원본차이 스킬핸들러A 1·8·9, B 2·7).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>다이나믹 크래쉬</b>(<c>0x1009fd40</c>) — 비와 같은 밀어내기(시전자가 보는 쪽으로 사거리 끝까지) 뒤 대상 SOUL −10.</item>
/// <item><b>리인카네이션</b>(<c>0x1008d940</c>) — 범위 안 적마다 시전자 반대쪽(8방향)으로 범위 밖까지 밀고, 막히면 한 번 더 때린다(뜻은 가설).</item>
/// <item><b>워핑</b>(<c>0x1009d100</c>) — 대상을 시전자→대상 쪽으로 13±2 칸 밖의 설 수 있는 칸으로 날려 보낸다. 못 찾으면 10칸부터 당겨 온다.</item>
/// <item><b>혼·비연참·오메가 스윙</b>(<c>0x1007f220</c>·<c>0x10080430</c>·<c>0x100891a0</c>) — 시전자가 겨눈 빈 칸까지 돌진하며 지나는 칸의 적을 때린다.
/// 원본은 이동기에 타격 개체를 붙인다 — 지나는 길 한 줄만 맞는 것으로 했다(판정 폭은 가설).</item>
/// <item><b>무신멸뢰옥</b> 3타(틱 150·190·230), <b>선 블래스트</b> 4타(틱 15·30·15·25 간격), <b>카운터 미사일</b> Lv11 이상은 60틱 뒤 두 번째 일제 사격.</item>
/// </list>
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>다이나믹 크래쉬(어빌리티 96) Lv1~20.</summary>
    private static readonly HashSet<int> DynamicCrashWorks = [470, .. Enumerable.Range(745, 9), .. Enumerable.Range(784, 10)];

    /// <summary>리인카네이션(어빌리티 59) Lv1~10.</summary>
    private static readonly HashSet<int> ReincarnationWorks = [419, .. Enumerable.Range(1264, 9)];

    /// <summary>워핑(어빌리티 117) Lv1~10.</summary>
    private static readonly HashSet<int> WarpingWorks = [422, .. Enumerable.Range(1301, 9)];

    /// <summary>돌진기 — 혼(어빌리티 5) Lv1~20 · 비연참(19) Lv1~10 · 오메가 스윙(122).</summary>
    private static readonly HashSet<int> DashWorks = [8, .. Enumerable.Range(259, 19), .. Enumerable.Range(341, 10), 517];

    /// <summary>카운터 미사일(어빌리티 78) Lv1~20 — Lv11 이상은 두 번 쏜다(<c>0x100a9ed6</c>).</summary>
    private static readonly HashSet<int> CounterMissileWorks = [439, .. Enumerable.Range(1035, 19)];

    private const int MusinWork = 1530, SunBlastWork = 1585;

    /// <summary>첫 타 뒤에 더 치는 간격(틱) — 없으면 빈 목록.</summary>
    private int[] ExtraHitGaps(WorkData w) => w.Id switch
    {
        MusinWork => [40, 40],
        SunBlastWork => [30, 15, 25],
        _ when CounterMissileWorks.Contains(w.Id) && w.Level >= 11 => [60],
        _ => [],
    };

    /// <summary>한 칸 = 월드 40. 이동기 빠르기 30, 틱마다 ×0.9, 5 아래로는 안 준다(<c>0x100c25c0(5)</c>).</summary>
    private static List<double> SlideSteps(int cells, double speed = 30, double factor = 0.9, double floor = 5)
    {
        var steps = new List<double>();
        for (double pos = 0, total = Math.Max(1, cells) * WorldPerCell; pos < total;)
        {
            pos = Math.Min(total, pos + speed);
            speed = Math.Max(floor, speed * factor);
            steps.Add(pos / total);
        }
        return steps;
    }

    /// <summary>여러 인물을 한꺼번에 미끄러뜨린다 — 각자 제 칸까지, 틱마다 한 걸음.</summary>
    private IEnumerable<bool> SlideAll(List<(UnitState Unit, int Col, int Row, List<double> Steps)> moves)
    {
        foreach (var (u, col, row, _) in moves) u.BeginSlide(col, row);
        double start = _lastTime;
        int longest = moves.Max(m => m.Steps.Count);
        for (int k; (k = (int)((_lastTime - start) * TicksPerSecond)) < longest;)
        {
            foreach (var (u, _, _, steps) in moves) u.SetSlide(steps[Math.Min(k, steps.Count - 1)]);
            yield return true;
        }
        foreach (var (u, _, _, _) in moves) u.SetSlide(1);
    }

    /// <summary>리인카네이션 — 범위 안 대상들을 시전자 반대쪽으로 범위 밖까지 밀어낸다. 막힌 인물은 한 번 더 맞는다.</summary>
    private IEnumerable<bool> RadialPushRoutine(UnitState user, WorkData w, int col, int row, List<int> targets, List<UnitState> dying)
    {
        var area = AreaCells(w, user, col, row).ToHashSet();
        var moves = new List<(UnitState, int, int, List<double>)>();
        var blocked = new List<UnitState>();
        foreach (int ti in targets)
        {
            var t = _units[ti];
            if (!t.Alive || dying.Contains(t) || t == user) continue;
            int dc = Math.Sign(t.Col - user.Col), dr = Math.Sign(t.Row - user.Row);
            if (dc == 0 && dr == 0) continue;
            int c = t.Col, r = t.Row, moved = 0;
            bool stuck = false;
            // 범위 안에 있는 동안 나아가고, 범위를 벗어난 첫 칸에 선다. 갈 수 없으면 거기서 멈춘다(원본은 되돌아오며 깃발을 세운다).
            while (area.Contains((c, r)) || moved == 0)
            {
                if (!CanStand(c + dc, r + dr, t) || moves.Any(m => m.Item2 == c + dc && m.Item3 == r + dr)) { stuck = true; break; }
                c += dc; r += dr; moved++;
                if (moved > 12) break;
            }
            if (stuck) blocked.Add(t);
            if (moved == 0) continue;
            t.Facing = FacingToward(c, r, user.Col, user.Row);
            t.PlayAction(HitAction, 1000);
            var (fx, fy) = UnitFoot(t);
            _effects.Add((BiTrailObs, 0, _lastTime, fx, fy));
            moves.Add((t, c, r, SlideSteps(moved)));
        }
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"radial push work {w.Id}: {string.Join(", ", moves.Select(m => $"{m.Item1.ChrCode}→({m.Item2},{m.Item3})"))} 막힘 {blocked.Count}" + Environment.NewLine);
        if (moves.Count > 0) foreach (bool _ in SlideAll(moves)) yield return true;
        foreach (var (t, _, _, _) in moves) t.PlayAction(ObsMotionTable.ActionStand, 0);
        foreach (var t in blocked) if (t.Alive && !dying.Contains(t)) ApplyWork(user, w, t, dying);
    }

    /// <summary>워핑 — 대상을 시전자→대상 쪽으로 13±2 칸 밖의 설 수 있는 칸에 떨어뜨린다. 없으면 10칸부터 한 칸씩 당겨 찾는다.</summary>
    private IEnumerable<bool> ThrowRoutine(UnitState user, UnitState t)
    {
        var (dc, dr) = FacingToward(user.Col, user.Row, t.Col, t.Row) switch
        {
            Facing.Up => (0, -1), Facing.Down => (0, 1), Facing.Left => (-1, 0), _ => (1, 0),
        };
        (int Col, int Row)? landing = null;
        for (int n = 0; n < 100 && landing == null; n++)
        {
            int c = t.Col + dc * 13 + _rng.Next(-2, 3), r = t.Row + dr * 13 + _rng.Next(-2, 3);
            if (dc == 0) c = t.Col + _rng.Next(-2, 3); else r = t.Row + _rng.Next(-2, 3);
            if (CanStand(c, r, t)) landing = (c, r);
        }
        for (int d = 10; d >= 1 && landing == null; d--)
            if (CanStand(t.Col + dc * d, t.Row + dr * d, t)) landing = (t.Col + dc * d, t.Row + dr * d);
        if (landing is not var (lc, lr)) yield break;

        const double Tick = 1 / TicksPerSecond;
        var (fx, fy) = UnitFoot(t);
        _effects.Add((587, 4, _lastTime, fx, fy));
        for (double start = _lastTime, end = start + 12 * Tick; _lastTime < end;)
        {
            t.Fade = Math.Max(0, 1 - (_lastTime - start) / (12 * Tick));
            yield return true;
        }
        t.Fade = 0;
        // 날아가는 동안 — 거리에 맞춰 잠깐(칸당 1틱).
        for (double end = _lastTime + (Math.Abs(lc - t.Col) + Math.Abs(lr - t.Row)) * Tick; _lastTime < end;) yield return true;
        t.WarpTo(lc, lr);
        t.OriginCol = lc;
        t.OriginRow = lr;
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"warping: {user.ChrCode} → {t.ChrCode} 를 ({lc},{lr}) 로" + Environment.NewLine);
        for (double start = _lastTime, end = start + 12 * Tick; _lastTime < end;)
        {
            t.Fade = Math.Min(1, (_lastTime - start) / (12 * Tick));
            yield return true;
        }
        t.Fade = 1;
        t.PlayAction(HitAction, 15 * Tick);
    }

    /// <summary>
    /// 돌진 — 시전자가 겨눈 빈 칸까지 한 줄로 미끄러지며, 지나는 칸에 선 적을 지날 때마다 때린다. 도착한 칸이 새 자리다.
    /// </summary>
    private IEnumerable<bool> DashRoutine(UnitState a, WorkData w, int col, int row, List<UnitState> dying)
    {
        // 지나는 칸들 — 브레젠험 한 줄.
        var cells = new List<(int Col, int Row)>();
        int x0 = a.Col, y0 = a.Row, dx = Math.Abs(col - x0), dy = Math.Abs(row - y0), sx = Math.Sign(col - x0), sy = Math.Sign(row - y0), err = dx - dy;
        for (int guard = 0; (x0 != col || y0 != row) && guard < 64; guard++)
        {
            int e2 = err * 2;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
            cells.Add((x0, y0));
        }
        if (cells.Count == 0 || LiveUnitAt(col, row) != null) yield break;

        var steps = SlideSteps(cells.Count, speed: 24, factor: 1, floor: 24);
        a.BeginSlide(col, row);
        a.OriginCol = col;
        a.OriginRow = row;
        var struck = new HashSet<UnitState>();
        double start = _lastTime;
        for (int k; (k = (int)((_lastTime - start) * TicksPerSecond)) < steps.Count;)
        {
            double p = steps[k];
            a.SetSlide(p);
            // 지난 칸까지의 적을 친다.
            int passed = Math.Min(cells.Count, (int)Math.Floor(p * cells.Count + 0.5));
            for (int i = 0; i < passed; i++)
                foreach (var t in _units.Where(t => t.Alive && t.OnField && t != a && t.Col == cells[i].Col && t.Row == cells[i].Row && SeesAsFoe(a, t)))
                    if (struck.Add(t)) ApplyWork(a, w, t, dying);
            yield return true;
        }
        a.SetSlide(1);
        foreach (var c in cells)
            foreach (var t in _units.Where(t => t.Alive && t.OnField && t != a && t.Col == c.Col && t.Row == c.Row && SeesAsFoe(a, t)))
                if (struck.Add(t)) ApplyWork(a, w, t, dying);
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"dash work {w.Id}: {a.ChrCode} → ({col},{row}) {cells.Count}칸, 맞은 {struck.Count}명" + Environment.NewLine);
    }
}
