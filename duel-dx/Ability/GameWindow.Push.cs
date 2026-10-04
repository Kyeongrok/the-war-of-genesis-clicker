using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 인물을 옮기는 기술들(fg-21 ⑧·⑨)과 여러 번 치는 기술(⑩) — 원본 핸들러가 이동기(<c>0x100ca7f0</c>·<c>0x100cb4a0</c>)로 유닛을 미끄러뜨리거나
/// 피해 메시지를 여러 번 보내는 것을 옮겼다(분석-원본차이 스킬핸들러A 1·8·9, B 2·7).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>다이나믹 크래쉬</b>(<c>0x1009fd40</c>) — 비와 같은 밀어내기(시전자가 보는 쪽으로 사거리 끝까지) 뒤 대상 SOUL −10.</item>
/// <item><b>리인카네이션</b>(<c>0x1008d940</c>) — 범위 안 유닛마다 시전자 반대쪽(8방향)으로 범위 밖까지 민다. 피해는 이동기 슬롯뿐 — 보통 0타, 막힘 뒤 1타, 제자리 막힘 2타(audit3 R1).</item>
/// <item><b>워핑</b>(<c>0x1009d100</c>) — 대상을 시전자→대상 쪽으로 13±2 칸 밖의 설 수 있는 칸으로 날려 보낸다. 못 찾으면 10칸부터 당겨 온다.</item>
/// <item><b>혼·비연참·오메가 스윙</b>(<c>0x1007f220</c>·<c>0x10080430</c>·<c>0x100891a0</c>) — 시전자가 겨눈 빈 칸까지 돌진하며 지나는 칸의 적을 때린다.
/// 원본은 이동기에 타격 개체를 붙인다 — 지나는 길 한 줄만 맞는 것으로 했다(판정 폭은 가설).</item>
/// <item><b>무신멸뢰옥</b> 3타(틱 150·190·230), <b>선 블래스트</b> 4타(틱 15·30·15·25 간격), <b>카운터 미사일</b> Lv11 이상은 60틱 뒤 두 번째 일제 사격.</item>
/// </list>
/// </remarks>
internal sealed unsafe partial class GameWindow
{
    /// <summary>다이나믹 크래쉬(어빌리티 96) Lv1~20.</summary>
    internal static readonly HashSet<int> DynamicCrashWorks = [470, .. Enumerable.Range(745, 9), .. Enumerable.Range(784, 10)];

    /// <summary>리인카네이션(어빌리티 59) Lv1~10.</summary>
    internal static readonly HashSet<int> ReincarnationWorks = [419, .. Enumerable.Range(1264, 9)];

    /// <summary>워핑(어빌리티 117) Lv1~10.</summary>
    internal static readonly HashSet<int> WarpingWorks = [422, .. Enumerable.Range(1301, 9)];

    /// <summary>돌진기 — 혼(어빌리티 5) Lv1~20 · 비연참(19) Lv1~10 · 오메가 스윙(122).</summary>
    internal static readonly HashSet<int> DashWorks = [8, .. Enumerable.Range(259, 19), .. Enumerable.Range(341, 10), 517];

    /// <summary>카운터 미사일(어빌리티 78) Lv1~20 — Lv11 이상은 두 번 쏜다(<c>0x100a9ed6</c>).</summary>
    internal static readonly HashSet<int> CounterMissileWorks = [439, .. Enumerable.Range(1035, 19)];

    internal const int MusinWork = 1530, SunBlastWork = 1585;

    /// <summary>사이킥 크로스(어빌리티 84) Lv1~10 — 「\」 획 뒤 5틱마다 「/」 획이 겹쳐 <b>가운데 칸은 두 번</b> 맞는다(0x100ab290, ba-14 H4).</summary>
    internal static readonly HashSet<int> PsychicCrossWorks = [1111, .. Enumerable.Range(1112, 9)];

    /// <summary>첫 타 뒤에 더 치는 간격(틱) — 없으면 빈 목록.</summary>
    internal int[] ExtraHitGaps(WorkData w) => w.Id switch
    {
        MusinWork => [40, 40],
        SunBlastWork => [30, 15, 25],
        _ when CounterMissileWorks.Contains(w.Id) && w.Level >= 11 => [60],
        _ => [],
    };

    /// <summary>한 칸 = 월드 40. 이동기 빠르기 30, 틱마다 ×0.9, 5 아래로는 안 준다(<c>0x100c25c0(5)</c>).</summary>
    internal static List<double> SlideSteps(int cells, double speed = 30, double factor = 0.9, double floor = 5)
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
    /// <param name="begun">이미 <see cref="UnitState.BeginSlide"/> 로 칸을 옮겨 둔 경우(리인카네이션 — 처리 차례대로 등록표를 바꾼다).</param>
    internal IEnumerable<bool> SlideAll(List<(UnitState Unit, int Col, int Row, List<double> Steps)> moves, bool begun = false)
    {
        if (!begun) foreach (var (u, col, row, _) in moves) u.BeginSlide(col, row);
        double start = _lastTime;
        int longest = moves.Max(m => m.Steps.Count);
        for (int k; (k = (int)((_lastTime - start) * TicksPerSecond)) < longest;)
        {
            foreach (var (u, _, _, steps) in moves) u.SetSlide(steps[Math.Min(k, steps.Count - 1)]);
            yield return true;
        }
        foreach (var (u, _, _, _) in moves) u.SetSlide(1);
    }

    /// <summary>
    /// 리인카네이션(<c>0x1008d940</c>, audit3 R1) — 범위 안 유닛(편 무관, 시전자 빼고)을 시전자 반대쪽(8방향)으로 범위 밖 첫 칸까지 민다.
    /// 피해는 일반 타격이 아니라 이동기 슬롯 둘뿐이다.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>처리 차례 = 범위 사각형을 행(y) 오름차순, 행 안에서 열(x) 오름차순(<c>0x100df5c0</c>, <c>0x100df5ed~0x100df694</c>). 유닛은 차례대로 곧바로 새 칸에 등록된다(<c>0x1008df5a</c> 해제 → <c>0x1008dfa5</c> 등록).</item>
    /// <item>슬롯 0(<c>0x1008eac8</c>) — 다음 틱 <b>출발 칸</b>에서 한 번. 그때 출발 칸은 이미 비었으므로 보통은 아무도 안 맞는다.
    /// 제자리에 막힌 유닛, 또는 남이 떠난 출발 칸으로 되돌아와 선 유닛만 맞는다(<c>0x100c2d9c</c>, <c>0x100d9b17</c>).</item>
    /// <item>슬롯 1(<c>0x1008eb15</c>) — 이동이 끝날 때 <b>도착 칸</b>에서 한 번. 막힘 깃발 <c>[esp+0x30]</c> 은 고리 밖 <c>0x1008de68</c> 에서만 0 으로 두므로
    /// 한 번 막힌 유닛이 나온 뒤로 처리되는 유닛은 막히지 않았어도 모두 받는다.</item>
    /// <item>효과 범위 밖·시전자·물체는 밀리지도 맞지도 않는다.</item>
    /// </list>
    /// </remarks>
    internal IEnumerable<bool> RadialPushRoutine(UnitState user, WorkData w, int col, int row, List<int> targets, List<UnitState> dying)
    {
        _ = targets;   // 원본은 WorkTargets(적만) 가 아니라 범위 안 모든 유닛을 모은다(방식 5).
        var cells = AreaCells(w, user, col, row).Distinct().OrderBy(c => c.Row).ThenBy(c => c.Col).ToList();
        var area = cells.ToHashSet();
        // 먼저 모아 둔다(0x100df5c0 이 버퍼에 모은 뒤 고리를 돈다) — 밀려난 유닛이 다시 잡히지 않게.
        var order = cells.Select(c => LiveUnitAt(c.Col, c.Row)).OfType<UnitState>()
                         .Where(t => t != user && !dying.Contains(t)).Distinct().ToList();
        // 되돌아올 때 설 수 있는 칸 0x100d9a20 — 지형 &9 · 다른 유닛 등록 · 물체 없음(자기 칸은 늘 참).
        bool Stand(int c, int r, UnitState t) => CanStand(c, r, t) && ObjectAt(c, r) is not { Alive: true, Data.BlocksStanding: true };

        bool stuckFlag = false;   // 0x1008de68 — 고리 밖에서 한 번만 0
        var starts = new List<(int Col, int Row)>();
        var slot1 = new List<UnitState>();
        var moves = new List<(UnitState, int, int, List<double>)>();
        foreach (var t in order)
        {
            int dc = Math.Sign(t.Col - user.Col), dr = Math.Sign(t.Row - user.Row);
            if (dc == 0 && dr == 0) continue;
            int c = t.Col, r = t.Row, nc = c, nr = r;
            starts.Add((c, r));
            // 범위 안인 동안 나아가 범위 밖 첫 칸. 못 서면 깃발을 세우고 한 칸씩 되돌아오며 처음 설 수 있는 칸(0x1008df33 …).
            do { nc += dc; nr += dr; } while (area.Contains((nc, nr)));
            if (!Stand(nc, nr, t))
            {
                stuckFlag = true;
                do { nc -= dc; nr -= dr; } while (!(nc == c && nr == r) && !Stand(nc, nr, t));
            }
            if (stuckFlag) slot1.Add(t);
            int moved = Math.Max(Math.Abs(nc - c), Math.Abs(nr - r));
            if (moved == 0) continue;
            t.Facing = FacingToward(nc, nr, user.Col, user.Row);
            t.PlayAction(HitAction, 1000);
            var (fx, fy) = UnitFoot(t);
            _effects.Add((BiTrailObs, 0, _lastTime, fx, fy));
            t.BeginSlide(nc, nr);   // 곧바로 새 칸에 등록 — 뒤에 처리되는 유닛은 이 칸에 막히고, 비운 출발 칸에는 설 수 있다
            moves.Add((t, nc, nr, SlideSteps(moved)));
        }
        // 슬롯 0 — 출발 칸에 지금 선 유닛(제자리에 막힌 유닛, 또는 남의 출발 칸으로 되돌아온 유닛).
        var slot0 = starts.Select(s => LiveUnitAt(s.Col, s.Row)).OfType<UnitState>().ToList();
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"radial push work {w.Id}: {string.Join(", ", moves.Select(m => $"{m.Item1.ChrCode}→({m.Item2},{m.Item3})"))} " +
                $"슬롯0 [{string.Join(",", slot0.Select(u => u.ChrCode))}] 슬롯1 [{string.Join(",", slot1.Select(u => u.ChrCode))}]" + Environment.NewLine);
        foreach (var v in slot0) if (v.Alive && !dying.Contains(v)) ApplyWork(user, w, v, dying);
        if (moves.Count > 0) foreach (bool _ in SlideAll(moves, begun: true)) yield return true;
        foreach (var (t, _, _, _) in moves) t.PlayAction(ObsMotionTable.ActionStand, 0);
        foreach (var t in slot1) if (t.Alive && !dying.Contains(t)) ApplyWork(user, w, t, dying);
    }

    /// <summary>워핑 — 대상을 시전자→대상 쪽으로 13±2 칸 밖의 설 수 있는 칸에 떨어뜨린다. 없으면 10칸부터 한 칸씩 당겨 찾는다.</summary>
    internal IEnumerable<bool> ThrowRoutine(UnitState user, UnitState t)
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
    internal IEnumerable<bool> DashRoutine(UnitState a, WorkData w, int col, int row, List<UnitState> dying)
    {
        // 원본 이동기 0x100ca9a0(ba-14 H1): 시전자가 <b>보는 쪽</b>으로 틱마다 한 칸씩 곧장 간다. 겨눈 칸이 축에서 벗어나면 보는 축의 거리만 쓴다.
        // 지형·물체에만 막히고 유닛은 안 막는다. 혼은 지난 칸만, 비연참·오메가 스윙은 지난 칸의 3칸 폭 띠를 친다(한 유닛에 한 번).
        var (dc, dr) = a.Facing switch { Facing.Up => (0, -1), Facing.Down => (0, 1), Facing.Left => (-1, 0), _ => (1, 0) };
        int dist = dc != 0 ? (col - a.Col) * dc : (row - a.Row) * dr;
        var cells = new List<(int Col, int Row)>();
        for (int k = 1; k <= dist; k++)
        {
            int c = a.Col + dc * k, r = a.Row + dr * k;
            if ((uint)c >= Cols || (uint)r >= Rows || _map is not { } map || c >= map.Cols || r >= map.Rows || (CellFlagsAt(c, r) & 0x9) != 0) break;
            if (ObjectAt(c, r) is { Alive: true, Data.BlocksStanding: true }) break;
            cells.Add((c, r));
        }
        // 마지막 칸에 누가 서 있으면 그 앞에서 멈춘다(겹쳐 서지 않게 — 가설).
        while (cells.Count > 0 && LiveUnitAt(cells[^1].Col, cells[^1].Row) is { } blocker && blocker != a) cells.RemoveAt(cells.Count - 1);
        if (cells.Count == 0) yield break;
        var (endCol, endRow) = cells[^1];
        bool band = w.Id != 8 && !(w.Id >= 259 && w.Id <= 277);   // 혼만 한 줄, 나머지(비연참·오메가 스윙)는 3칸 폭
        bool InBand(UnitState t, (int Col, int Row) c) => dc != 0 ? t.Col == c.Col && Math.Abs(t.Row - c.Row) <= (band ? 1 : 0)
                                                                  : t.Row == c.Row && Math.Abs(t.Col - c.Col) <= (band ? 1 : 0);

        // 혼·비연참은 한 칸(40px)/틱, 오메가 스윙은 15px/틱.
        double perTick = w.Id == 517 ? 15.0 / WorldPerCell : 1;
        var steps = new List<double>();
        for (double pos = 0; pos < cells.Count;) { pos = Math.Min(cells.Count, pos + perTick); steps.Add(pos / cells.Count); }
        a.BeginSlide(endCol, endRow);
        a.OriginCol = endCol;
        a.OriginRow = endRow;
        var struck = new HashSet<UnitState>();
        double start = _lastTime;
        for (int k; (k = (int)((_lastTime - start) * TicksPerSecond)) < steps.Count;)
        {
            double p = steps[k];
            a.SetSlide(p);
            // 지난 칸까지의 적을 친다.
            int passed = Math.Min(cells.Count, (int)Math.Floor(p * cells.Count + 0.5));
            for (int i = 0; i < passed; i++)
                foreach (var t in _units.Where(t => t.Alive && t.OnField && t != a && InBand(t, cells[i]) && SeesAsFoe(a, t)))
                    if (struck.Add(t)) ApplyWork(a, w, t, dying);
            yield return true;
        }
        a.SetSlide(1);
        foreach (var c in cells)
            foreach (var t in _units.Where(t => t.Alive && t.OnField && t != a && InBand(t, c) && SeesAsFoe(a, t)))
                if (struck.Add(t)) ApplyWork(a, w, t, dying);
        col = endCol; row = endRow;
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"dash work {w.Id}: {a.ChrCode} → ({col},{row}) {cells.Count}칸, 맞은 {struck.Count}명" + Environment.NewLine);
    }
}
