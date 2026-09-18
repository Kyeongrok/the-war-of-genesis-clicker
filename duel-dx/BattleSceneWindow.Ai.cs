using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// AI 가 차례에 하는 일(ba-11) — 적군과 동맹 AI(제이슨)가 같은 규칙으로 움직인다.
/// </summary>
/// <remarks>
/// 옵시디안 분석-전투 "AI 가 차례에 하는 일": <c>AIThink 0x10060730</c> 은 여섯 단계를 차례로 보고,
/// 앞 단계가 명령을 만들면 뒤는 아예 안 본다. 난수가 없어 같은 판이면 늘 같게 움직인다.
/// <list type="number">
/// <item>깨어남 — 조건을 못 채우면 휴식만.</item>
/// <item>도망 — HP% &lt; Num[66] 이고 가장 가까운 적이 Num[90] 칸 안이면 가장 안전한 칸으로.</item>
/// <item>자가 회복 — HP% &lt; Num[70] 이면 회복 work 만.</item>
/// <item>공격 — 어빌리티 목록(마지막이 기본공격)에서 <b>조건이 되는 첫 work</b>. 닿는 칸이 없으면 대상 쪽으로 걷고 휴식.</item>
/// <item>휴식 — HP% ≤ Num[71].</item>
/// <item>이동 — 이동 방식 순서대로 목표를 정해 그쪽으로.</item>
/// </list>
/// 세력 점수 <c>0x1005b120</c> = ((HP + 최대HP) / Num[62]) × (ACR × RDP × ATK / Num[61] / Num[63] / Num[64]),
/// 영향력 지도 <c>0x1005b210</c> 은 칸마다 <c>세력 × Num[65] / (거리 + Num[65])</c> 를 더한다.
/// 칸 점수 <c>0x1005d070</c> = 값 × Num[74] × 10 / (4 + |Δx| + |Δy|), 값의 기준은 work <c>+0x3e</c>.
/// 상태이상·아이템·편대는 아직 없고, 이동 방식은 0(가장 센 적 쪽)만 넣었다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>C 정수 나눗셈(0 으로 나누면 0).</summary>
    private static int CDiv(int a, int b) => b == 0 ? 0 : a / b;

    /// <summary>
    /// 이동 방식이 정하는 <b>목표 차례</b>(<c>0x1005c460</c>) — 앞에서부터 갈 칸이 나오는 첫 목표를 쓴다.
    /// </summary>
    /// <remarks>
    /// <c>0</c> 가깝고 센 적 · <c>1</c> <b>내 편이 몰아붙이고 있는</b> 적 · <c>2</c> 먼 적 ·
    /// <c>3</c> 가깝고 센 아군 곁 · <c>4</c> 멀고 약한 아군 곁 · <c>5</c> 위험한 아군 곁.
    /// 그 밖(6 이상·음수)은 목록이 비어 제자리에서 쉰다. 시판 자료는 <b>0·1·2·3 만</b> 쓴다(2154·2·9·1명).
    /// <para>동점이면 <c>Btl</c> 줄 순서를 지킨다 — 원본이 인접 맞바꿈으로 고르기 때문이다.</para>
    /// </remarks>
    private List<UnitState> MoveGoals(UnitState u, List<UnitState> enemies)
    {
        if (_db is not { } db) return [];
        int Reach(UnitState t) => 4 + Math.Abs(t.Col - u.Col) + Math.Abs(t.Row - u.Row);
        var friends = _units.Where(t => t.Alive && t != u && !SeesAsFoe(u, t)).ToList();

        return u.AiMove switch
        {
            0 => [.. enemies.OrderByDescending(t => CDiv(Power(t) * db.N(74), Reach(t)))],
            1 => [.. enemies.OrderByDescending(t => Danger(t, t.Col, t.Row) * 4 / Reach(t))],
            // 먼 적부터 — 원본은 앞뒤 원소를 다른 자로 재는 탓에 거리가 가로 4·세로 6 으로 어긋난다.
            2 => [.. enemies.OrderByDescending(t => 4 * Math.Abs(t.Col - u.Col) + 6 * Math.Abs(t.Row - u.Row))],
            3 => [.. friends.OrderByDescending(t => CDiv(Power(t) * db.N(74), Reach(t)))],
            4 => [.. friends.OrderBy(t => CDiv(Power(t) * db.N(74), Reach(t)))],
            5 => [.. friends.OrderByDescending(t => Danger(t, t.Col, t.Row) * 4 / Reach(t))],
            _ => [],
        };
    }

    /// <summary>
    /// 깨어남 조건을 지금 채웠나 — <c>0</c> 없음(바로 깸) · <c>1</c> 깨어 있는 같은 편까지 거리 · <c>2</c> 가장 가까운 적까지 거리 ·
    /// <c>3</c> 전투 틱. <b>4 이상은 영영 안 깬다</b>(분기표 <c>0x1005baa8</c> 이 네 칸뿐).
    /// </summary>
    /// <remarks>
    /// 거리는 <c>|Δ열| + |Δ행| + |Δ높이| / 2</c> 이고 <b>같거나 작으면</b> 깬다. 찾는 상대가 없으면 1000 으로 친다.
    /// 자료 2182명 중 1798명이 0(바로), 315명이 2(적이 다가오면), 37명이 3(몇 틱 뒤)이다.
    /// </remarks>
    private bool WakesNow(UnitState u)
    {
        int WakeDistance(UnitState other) =>
            Math.Abs(other.Col - u.Col) + Math.Abs(other.Row - u.Row)
            + Math.Abs(HeightAt(other.Col, other.Row) - HeightAt(u.Col, u.Row)) / 2;

        return u.WakeCondition switch
        {
            0 => true,
            1 => Nearest(t => t.Alive && t != u && !SeesAsFoe(u, t) && t.Awake) <= u.WakeValue,
            2 => Nearest(t => t.Alive && SeesAsFoe(u, t)) <= u.WakeValue,
            3 => _tick >= u.WakeValue,
            _ => false,
        };

        int Nearest(Func<UnitState, bool> pick)
        {
            var found = _units.Where(pick).Select(WakeDistance).ToList();
            return found.Count == 0 ? 1000 : found.Min();
        }
    }

    /// <summary>세력 점수 — AI 가 "이 인물이 얼마나 센가" 를 재는 값.</summary>
    private int Power(UnitState u)
    {
        if (_db is not { } db || u.Data is not { } c) return 0;
        int v = db.Acr(c, u.Tp) * db.Rdp(c, u.Hp, u.MaxHp) * db.Atk(c, u.Soul);
        v = CDiv(CDiv(CDiv(v, db.N(61)), db.N(63)), db.N(64));
        return CDiv(u.Hp + u.MaxHp, db.N(62)) * v;
    }

    /// <summary>그 칸이 받는 영향력 — 살아 있는 인물들의 세력을 거리로 나눠 더한다.</summary>
    private int Influence(int col, int row, bool ally)
    {
        int num65 = _db?.N(65) ?? 6, sum = 0;
        foreach (var u in _units)
        {
            if (!u.Alive || u.IsAlly != ally) continue;
            int d = Math.Abs(u.Col - col) + Math.Abs(u.Row - row);
            sum += CDiv(Power(u) * num65, d + num65);
        }
        return sum;
    }

    /// <summary>그 칸의 위험도 — 적 영향력 / (내 세력 + 아군 영향력).</summary>
    private double Danger(UnitState u, int col, int row)
    {
        int enemies = Influence(col, row, !u.IsAlly);
        int friends = Power(u) + Influence(col, row, u.IsAlly);
        return friends == 0 ? enemies : (double)enemies / friends;
    }

    /// <summary>
    /// 도망이 재는 위험도 — 보통 위험도와 달리 <b>제 세력 점수를 뺀다</b>(<c>0x1005b4e0</c>).
    /// 제가 버티고 있는 몫을 빼야 「내가 여기서 빠지면 이 칸이 얼마나 위험한가」가 나온다.
    /// </summary>
    private double FleeDanger(UnitState u, int col, int row)
    {
        int enemies = Influence(col, row, !u.IsAlly);
        int friends = Influence(col, row, u.IsAlly) - Power(u);
        return friends <= 0 ? enemies : (double)enemies / friends;
    }

    /// <summary>work <c>+0x3e</c> 기준으로 그 대상들이 얼마나 좋은지 — 짝수면 최댓값, 홀수면 1000000 − 최솟값.</summary>
    private int TargetValue(UnitState user, WorkData w, List<int> targets)
    {
        if (targets.Count == 0) return 0;
        int criterion = w.AiCriterion >> 1;
        bool wantMax = (w.AiCriterion & 1) == 0;
        int best = wantMax ? int.MinValue : int.MaxValue;
        foreach (int i in targets)
        {
            var t = _units[i];
            int v = criterion switch
            {
                0 => t.Hp,
                1 => t.Soul,
                2 => _db is { } db && t.Data is { } c ? db.Atk(c, t.Soul) : 0,
                3 => t.Tp,
                4 => _db is { } db2 && t.Data is { } c2 ? db2.Rdp(c2, t.Hp, t.MaxHp) : 0,
                5 => _db is { } db3 && t.Data is { } c3 ? db3.Acr(c3, t.Tp) : 0,
                6 => t.MaxHp - t.Hp,
                7 => Power(t),
                // 위험도는 <b>대상 자신의 시점</b>으로 잰다(0x1005c976) — 쓰는 쪽 시점으로 재면
                // 아군 보조기(큐어 같은 것)에서 부호가 뒤집힌다.
                _ => (int)(Danger(t, t.Col, t.Row) * 1000),
            };
            best = wantMax ? Math.Max(best, v) : Math.Min(best, v);
        }
        return wantMax ? best : 1000000 - best;
    }

    /// <summary>그 칸을 겨눴을 때 쓸 만한가 — 피해는 대상 수, 회복은 잃은 HP 비율 합이 기준을 넘어야 한다.</summary>
    private bool WorthUsing(UnitState user, WorkData w, List<int> targets)
    {
        int need = w.MinTargets + 1;
        if (w.IsHeal)
        {
            int lost = targets.Sum(i => _units[i].MaxHp == 0 ? 0 : (_units[i].MaxHp - _units[i].Hp) * 100 / _units[i].MaxHp);
            return lost > (_db?.N(79) ?? 30) * need;
        }
        return targets.Count >= need;
    }

    /// <summary>work 하나로 가장 좋은 (설 칸, 겨눌 칸)을 찾는다. 없으면 null.</summary>
    /// <remarks>
    /// 원본은 <b>두 단계</b>다(<c>0x1005d070</c>) — 먼저 <b>지금 자리 기준</b>으로 겨눌 칸의 점수를 매겨 가장 좋은 칸을 정하고,
    /// 그다음 그 칸에 닿는 칸 가운데 <b>맨해튼으로 가장 가까운</b> 설 칸을 고른다.
    /// 설 칸 × 겨눌 칸을 한꺼번에 재면 「멀리 가서 크게 때리는」 쪽이 과하게 뽑힌다.
    /// </remarks>
    private (int Stand, int Col, int Row, int Score)? BestUse(int unitIndex, WorkData w, MoveRange range)
    {
        var user = _units[unitIndex];
        int num74 = _db?.N(74) ?? 4;
        (int Col, int Row, int Score)? aim = null;

        // ① 겨눌 칸 — 점수는 지금 서 있는 자리에서 잰다.
        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                var targets = WorkTargets(w, user, col, row);
                if (targets.Count == 0 || !WorthUsing(user, w, targets)) continue;
                int score = CDiv(TargetValue(user, w, targets) * num74 * 10,
                                 4 + Math.Abs(col - user.Col) + Math.Abs(row - user.Row));
                if (aim == null || score > aim.Value.Score) aim = (col, row, score);
            }
        if (aim is not { } pick) return null;

        // ② 설 칸 — 그 칸에 닿는 칸 중 지금 자리에서 가장 가까운 곳.
        int bestStand = -1, bestDist = int.MaxValue;
        for (int stand = 0; stand < range.Cost.Length; stand++)
        {
            if (!range.CanReach(stand)) continue;
            int sc = stand % Cols, sr = stand / Cols;
            if (!InWorkRange(w, sc, sr, pick.Col, pick.Row, user)) continue;
            if (WorkTargetsFrom(w, user, sc, sr, pick.Col, pick.Row) is not { Count: > 0 }) continue;
            int d = Math.Abs(sc - user.Col) + Math.Abs(sr - user.Row);
            if (d < bestDist) { bestDist = d; bestStand = stand; }
        }
        return bestStand < 0 ? null : (bestStand, pick.Col, pick.Row, pick.Score);
    }

    /// <summary>(sc, sr) 에 선다고 쳤을 때 (col, row) 를 겨누면 맞는 인물들.</summary>
    private List<int> WorkTargetsFrom(WorkData w, UnitState user, int sc, int sr, int col, int row)
    {
        int keepCol = user.Col, keepRow = user.Row;
        user.WarpTo(sc, sr);
        var targets = WorkTargets(w, user, col, row);
        user.WarpTo(keepCol, keepRow);
        return targets;
    }

    /// <summary>그 인물이 쓸 수 있는 work 차례 — 익힌 어빌리티(분류 1·2·4) 다음에 기본공격.</summary>
    private List<WorkData> AiWorks(UnitState u)
    {
        var list = new List<WorkData>();
        if (_db is not { } db || u.Data is not { } c) return list;
        foreach (var (abilityId, level) in c.Abilities)
        {
            if (!db.Abilities.TryGetValue(abilityId, out var ab) || ab.Category is not (1 or 2 or 4)) continue;
            if (ab.WorkByLevel.TryGetValue(level, out int wid) && Work(wid) is { } w && CanAfford(u, w)) list.Add(w);
        }
        if (Work(c.BasicWorkId) is { } basic) list.Add(basic);
        return list;
    }

    /// <summary>AI 차례 — 분석-전투 ba-11 의 여섯 단계.</summary>
    private IEnumerator<bool> AiRoutine(int index)
    {
        var u = _units[index];
        for (double end = _lastTime + 0.4; _lastTime < end;) yield return true;

        if (_db is not { } db || ComputeRange(u) is not { } range)
        {
            Rest(index);
            yield break;
        }

        // 1단계 깨어남 — 조건을 못 채우면 그 자리에서 쉰다(0x1005baa8). 깬 그 차례에 바로 움직인다.
        if (!u.Awake)
        {
            if (!WakesNow(u)) { Rest(index); yield break; }
            u.Awake = true;
        }

        int hpPercent = u.MaxHp == 0 ? 100 : u.Hp * 100 / u.MaxHp;
        // 버서커(4)가 걸린 인물에게는 자기 말고 모두가 적이다.
        var enemies = _units.Where(t => t.Alive && SeesAsFoe(u, t)).ToList();
        int nearest = enemies.Count == 0 ? 99 : enemies.Min(t => Math.Abs(t.Col - u.Col) + Math.Abs(t.Row - u.Row));

        // 2단계 도망 — 피가 적고 적이 가까우면 물러난다. 재는 값은 <b>제 점수를 뺀</b> 위험도이고,
        // 시작값이 지금 칸이라 더 안전한 칸이 없으면 <b>안 움직인다</b>(0x1005b4e0).
        if (hpPercent < db.N(66) && nearest < db.N(90))
        {
            int here = u.Row * Cols + u.Col;
            int safest = here;
            double bestDanger = FleeDanger(u, u.Col, u.Row);
            for (int i = 0; i < range.Cost.Length; i++)
            {
                if (!range.CanReach(i)) continue;
                double danger = FleeDanger(u, i % Cols, i / Cols);
                // 엇비슷하게 안전한 칸(0.70 안쪽)끼리는 <b>지금 자리에서 가까운 쪽</b>을 고른다.
                bool closer = danger <= bestDanger / 0.70 && danger >= bestDanger * 0.70
                              && range.Cost[i] < (safest == here ? int.MaxValue : range.Cost[safest]);
                if (danger < bestDanger * 0.70 || closer) { bestDanger = Math.Min(bestDanger, danger); safest = i; }
            }
            if (safest != here) foreach (var r in WalkTo(u, range, safest)) yield return r;
            Rest(index);
            yield break;
        }

        // 3단계 자가 회복 → 4단계 공격. 원본은 <b>회복기만 한 바퀴 돌고, 못 쓰면 전부 한 바퀴</b> 돈다 —
        // 예전처럼 「피가 적으면 회복기 아닌 것을 건너뛴다」로 하면 회복기가 없는 인물이 공격까지 통째로 걸렀다.
        for (int pass = hpPercent < db.N(70) ? 0 : 1; pass < 2; pass++)
            foreach (var w in AiWorks(u))
            {
                if (pass == 0 && !w.IsHeal) continue;
                if (BestUse(index, w, range) is not { } use) continue;

                var path = PathWithin(range, u.Col, u.Row, use.Stand) ?? [];
                var aimed = LiveUnitAt(use.Col, use.Row);
                int targetIndex = w.TargetMode is 1 or 4 or 5 && aimed != null ? Array.IndexOf(_units, aimed) : -1;
                var routine = UseWorkRoutine(index, w, targetIndex, use.Col, use.Row, path);
                while (routine.MoveNext()) yield return true;
                if (_turn == index && _outcome.Length == 0 && u.Alive) Rest(index);
                yield break;
            }

        // 5단계 휴식 — 피가 Num[71]% 이하면 움직이지 않고 그 자리에서 쉰다.
        if (hpPercent <= db.N(71))
        {
            Rest(index);
            yield break;
        }

        // 6단계 — 칠 수 없으면 목표 쪽으로 다가가 쉰다. 목표 차례는 <b>이동 방식</b>이 정한다(0x1005c460).
        foreach (var goal in MoveGoals(u, enemies))
        {
            int best = -1, bestDist = int.MaxValue, bestCost = int.MaxValue;
            for (int i = 0; i < range.Cost.Length; i++)
            {
                if (!range.CanReach(i)) continue;
                int d = Math.Abs(goal.Col - i % Cols) + Math.Abs(goal.Row - i / Cols);
                if (d < bestDist || d == bestDist && range.Cost[i] < bestCost) { bestDist = d; best = i; bestCost = range.Cost[i]; }
            }
            // 갈 칸이 나오는 <b>첫 목표에서 멈춘다</b> — 나머지 목표는 아예 안 본다.
            if (best < 0 || range.Cost[best] <= 0) continue;
            foreach (var r in WalkTo(u, range, best)) yield return r;
            break;
        }

        for (double end = _lastTime + 0.2; _lastTime < end;) yield return true;
        if (_turn == index && _outcome.Length == 0 && u.Alive) Rest(index);
    }

    private IEnumerable<bool> WalkTo(UnitState u, MoveRange range, int cell)
    {
        foreach (var step in range.PathTo(cell)) u.Path.Enqueue(step);
        while (u.IsBusy) yield return true;
    }
}
