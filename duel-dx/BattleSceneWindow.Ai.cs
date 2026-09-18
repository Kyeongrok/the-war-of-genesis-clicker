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
                _ => (int)(Danger(user, t.Col, t.Row) * 1000),
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
    private (int Stand, int Col, int Row, int Score)? BestUse(int unitIndex, WorkData w, MoveRange range)
    {
        var user = _units[unitIndex];
        int num74 = _db?.N(74) ?? 4;
        (int Stand, int Col, int Row, int Score)? best = null;

        for (int stand = 0; stand < range.Cost.Length; stand++)
        {
            if (!range.CanReach(stand)) continue;
            int sc = stand % Cols, sr = stand / Cols;
            for (int row = 0; row < Rows; row++)
                for (int col = 0; col < Cols; col++)
                {
                    if (!InWorkRange(w, sc, sr, col, row, user)) continue;
                    var targets = WorkTargetsFrom(w, user, sc, sr, col, row);
                    if (targets.Count == 0 || !WorthUsing(user, w, targets)) continue;
                    int score = CDiv(TargetValue(user, w, targets) * num74 * 10, 4 + Math.Abs(col - sc) + Math.Abs(row - sr));
                    if (best == null || score > best.Value.Score) best = (stand, col, row, score);
                }
        }
        return best;
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

        int hpPercent = u.MaxHp == 0 ? 100 : u.Hp * 100 / u.MaxHp;
        var enemies = _units.Where(t => t.Alive && t.IsAlly != u.IsAlly).ToList();
        int nearest = enemies.Count == 0 ? 99 : enemies.Min(t => Math.Abs(t.Col - u.Col) + Math.Abs(t.Row - u.Row));

        // 2단계 도망 — 피가 적고 적이 가까우면 가장 안전한 칸으로 물러난다.
        if (hpPercent < db.N(66) && nearest < db.N(90))
        {
            int safest = -1;
            double bestDanger = double.MaxValue;
            for (int i = 0; i < range.Cost.Length; i++)
            {
                if (!range.CanReach(i)) continue;
                double danger = Danger(u, i % Cols, i / Cols);
                if (danger < bestDanger) { bestDanger = danger; safest = i; }
            }
            if (safest >= 0) foreach (var r in WalkTo(u, range, safest)) yield return r;
            Rest(index);
            yield break;
        }

        // 3·4단계 — 회복이 먼저, 그다음 공격. 쓸 수 있는 work 를 차례로 보고 첫 번째를 쓴다.
        foreach (var w in AiWorks(u))
        {
            if (hpPercent < db.N(70) && !w.IsHeal) continue;
            if (BestUse(index, w, range) is not { } use) continue;

            var path = PathWithin(range, u.Col, u.Row, use.Stand) ?? [];
            var aimed = LiveUnitAt(use.Col, use.Row);
            int targetIndex = w.TargetMode is 1 or 4 or 5 && aimed != null ? Array.IndexOf(_units, aimed) : -1;
            var routine = UseWorkRoutine(index, w, targetIndex, use.Col, use.Row, path);
            while (routine.MoveNext()) yield return true;
            if (_turn == index && _outcome.Length == 0 && u.Alive) Rest(index);
            yield break;
        }

        // 4(B)·6단계 — 칠 수 없으면 목표 쪽으로 다가가 쉰다. 이동 방식 0 = 가장 센 적부터.
        var goal = enemies.OrderByDescending(t => CDiv(Power(t) * db.N(74), 4 + Math.Abs(t.Col - u.Col) + Math.Abs(t.Row - u.Row))).FirstOrDefault();
        if (goal != null)
        {
            int best = -1, bestDist = int.MaxValue, bestCost = int.MaxValue;
            for (int i = 0; i < range.Cost.Length; i++)
            {
                if (!range.CanReach(i)) continue;
                int d = Math.Abs(goal.Col - i % Cols) + Math.Abs(goal.Row - i / Cols);
                if (d < bestDist || d == bestDist && range.Cost[i] < bestCost) { bestDist = d; best = i; bestCost = range.Cost[i]; }
            }
            if (best >= 0 && range.Cost[best] > 0) foreach (var r in WalkTo(u, range, best)) yield return r;
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
