using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 턴 진행 — TP 게이지로 차례를 정하고, 아군 차례엔 플레이어가, 적군 차례엔 간단한 AI 가 움직인다.
/// 공격·어빌리티는 모두 work 하나를 쓰는 <see cref="UseWorkRoutine"/> 로 처리한다.
/// </summary>
/// <remarks>
/// 차례 규칙은 <c>G3PartII.dll</c> 그대로(옵시디안 분석-전투 ba-2·ba-3·ba-4):
/// <list type="bullet">
/// <item>시간 한 칸(틱)마다 살아 있는 모든 인물 TP += STP(최대 TP / TP 나눗수), 최대에서 자른다.</item>
/// <item>TP 가 최대에 닿은 인물이 차례 표시를 받고, 표시가 있는 인물 중 <b>배열 번호가 작은 쪽</b>이 먼저 움직인다(아군·적 구분 없음).
///   전투 시작 때는 모두 TP 가 가득하다. 이 데모는 아군을 배열 앞에 두어 아군이 먼저 움직인다(Btl 파일은 적이 앞).</item>
/// <item>걷는 동안에는 TP 를 안 쓰고, 공격·어빌리티·휴식 직전에 시작 자리→지금 자리 걸음 비용을 한 번에 뺀다.
///   work 가 TP 를 쓰고, TP 가 0 이하가 되면 자동으로 휴식 — (최대HP − HP) × 남은 TP 비율 × Num[35]% 를 채우고 남은 TP 를 버린다.</item>
/// <item>SOUL: work 끝에 종류별 +10/6/4/4, 맞으면 피해/Num[43], 쓰러뜨리면 +10. work 의 SOUL 비용은 뺀다.</item>
/// </list>
/// 판정(명중·피해·흔들기·치명·회복)은 <see cref="GameDatabase.Resolve"/>(<c>0x1007b6f0</c>) — 분석-전투 "공격·어빌리티 판정".
/// 상태이상·자세·효과 범위 모양 2~9 는 빠져 있다. 적 AI 는 원본을 옮긴 것이 아니라
/// "칠 수 있으면 가장 적게 걸어 치고, 아니면 가장 가까운 아군 쪽으로 걷는다" 는 단순 규칙이다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>지금 차례인 인물 번호. 차례를 기다리는 중이면 −1.</summary>
    private int _turn = -1;
    private int _tick;
    private IEnumerator<bool>? _routine;
    private string _outcome = "";
    private double _nextTickAt;
    private readonly Random _rng = new();
    private readonly List<(string Text, float Size, int X, int Y, double Start, uint Color)> _popups = [];

    /// <summary>대상을 고르는 중인 work 번호(기본공격 포함). 고르는 중이 아니면 −1.</summary>
    private int _targetWork = -1;
    private bool _targetIsBasicAttack;

    /// <summary>기본공격 work 1 의 동작 — 준비 5 → 베기 8 → 복귀 24(분석-모션 <c>0x1007f0a0</c>). 판정은 베기 끝에 들어간다.</summary>
    private static readonly int[] StrikeActions = [5, 8, 24];
    private const int StrikeHitStep = 1;
    private const int DeathAction = 6;
    private const double TickDelaySeconds = 0.05;

    private bool IsPlayerTurn => _turn >= 0 && _units[_turn].IsAlly && _routine == null && _outcome.Length == 0;

    /// <summary>게임 표를 다 읽은 뒤 인물마다 전투 수치를 채운다.</summary>
    private void InitBattle()
    {
        if (_db == null) return;
        foreach (var unit in _units)
        {
            if (_db.Character(unit.ChrCode) is not { } c) continue;
            unit.Data = c;
            unit.MaxHp = unit.Hp = Math.Max(1, _db.MaxHp(c));
            unit.MaxTp = unit.Tp = _db.MaxTp(c);
            unit.Stp = Math.Max(1, _db.Stp(c));
            unit.MaxSoul = _db.MaxSoul(c);
            unit.Soul = _db.SoulStart;
            unit.HasTurn = true;
        }
    }

    private void UpdateTurn()
    {
        if (_loading || _db == null || _outcome.Length > 0) return;

        if (_routine != null)
        {
            if (!_routine.MoveNext()) _routine = null;
            return;
        }

        if (_turn >= 0)
        {
            var u = _units[_turn];
            if (!u.Alive) EndTurn();
            else if (u.IsAlly && !u.IsBusy && u.Tp <= 0 && !_abilityMenu && _targetWork < 0) Rest(_turn);
            return;
        }

        if (_lastTime < _nextTickAt) return;
        for (int guard = 0; guard < 10000; guard++)
        {
            int next = Array.FindIndex(_units, u => u.Alive && u.HasTurn);
            if (next >= 0) { StartTurn(next); return; }
            AdvanceTick();
        }
    }

    private void AdvanceTick()
    {
        _tick++;
        foreach (var u in _units.Where(u => u.Alive))
        {
            u.Tp = Math.Min(u.MaxTp, u.Tp + u.Stp);
            if (u.Tp >= u.MaxTp) u.HasTurn = true;
        }
    }

    /// <summary>차례 시작 — 그 인물을 고른다(fg-8). 적이면 AI 를 돌린다(fg-7).</summary>
    private void StartTurn(int index)
    {
        _turn = index;
        _selected = index;
        _units[index].OriginCol = _units[index].Col;
        _units[index].OriginRow = _units[index].Row;
        CancelTargeting();
        _heldMoveKeys.Clear();
        if (_units[index].IsAlly) Toast($"{UnitName(index)} 차례");
        else _routine = EnemyRoutine(index);
    }

    private void EndTurn()
    {
        if (_turn >= 0) _units[_turn].HasTurn = false;
        _turn = -1;
        _ringUnit = -1;
        CancelTargeting();
        _nextTickAt = _lastTime + TickDelaySeconds;
    }

    /// <summary>공격·어빌리티를 열 때 걸음 비용을 빼기 전 값 — 취소하면 되돌린다.</summary>
    private (int Tp, int OriginCol, int OriginRow)? _commitUndo;

    /// <summary>공격 대상 고르기·어빌리티 목록을 닫는다. <paramref name="refund"/> 면 열 때 뺀 걸음 비용을 돌려준다.</summary>
    private void CancelTargeting(bool refund = false)
    {
        if (refund && _commitUndo is { } undo && _turn >= 0)
        {
            var u = _units[_turn];
            (u.Tp, u.OriginCol, u.OriginRow) = undo;
        }
        _commitUndo = null;
        _targetWork = -1;
        _abilityMenu = false;
    }

    /// <summary>공격·어빌리티를 열 때: 지금까지 걸은 비용을 한 번에 뺀다(취소하면 되돌림).</summary>
    private void CommitMoveForAction()
    {
        var u = _units[_turn];
        _commitUndo = (u.Tp, u.OriginCol, u.OriginRow);
        CommitMove(u);
    }

    /// <summary>우클릭·Esc 로 걸은 것을 물린다 — 차례 시작 자리로 되돌린다. 물렸으면 true.</summary>
    private bool UndoMove()
    {
        if (!IsPlayerTurn) return false;
        var u = _units[_turn];
        if (u.IsBusy || (u.Col == u.OriginCol && u.Row == u.OriginRow)) return false;
        u.WarpTo(u.OriginCol, u.OriginRow);
        return true;
    }

    /// <summary>우클릭·Esc 공통 취소 — 목록·대상 고르기(비용 돌려줌) → 걸음 물리기 순. 취소한 것이 있으면 true.</summary>
    private bool CancelStep()
    {
        if (_abilityMenu || _targetWork >= 0) { CancelTargeting(refund: true); Toast("취소했습니다"); return true; }
        return UndoMove();
    }

    /// <summary>차례 시작 자리에서 지금 자리까지 걸은 비용을 TP 에서 한 번에 빼고, 시작 자리를 지금 자리로 옮긴다.</summary>
    private void CommitMove(UnitState u)
    {
        if (ComputeRange(u) is { } range && range.CanReach(u.Row * Cols + u.Col)) u.Tp -= range.Cost[u.Row * Cols + u.Col];
        u.OriginCol = u.Col;
        u.OriginRow = u.Row;
    }

    /// <summary>휴식 <c>0x1007a790</c>: (최대 HP − 현재 HP) × 남은 TP / 최대 TP × Num[35]% 를 채우고 남은 TP 를 버린다.</summary>
    private void Rest(int index)
    {
        var u = _units[index];
        CommitMove(u);
        if (u.Tp > 0 && u.MaxTp > 0 && _db != null)
        {
            int heal = (int)((long)(u.MaxHp - u.Hp) * u.Tp / u.MaxTp * _db.N(35) / 100);
            if (heal > 0) { u.Hp += heal; Popup(u, $"+{heal}", HealColor); }
        }
        u.Tp = 0;
        EndTurn();
    }

    // ── 걷기 ─────────────────────────────────────────────────────────────────

    /// <summary>차례인 아군을 클릭한 파란 칸까지 걷게 한다. TP 는 행동할 때 한 번에 뺀다.</summary>
    private bool TryWalkTo(int col, int row)
    {
        var u = _units[_turn >= 0 ? _turn : 0];
        if (!IsPlayerTurn || u.IsBusy || ComputeRange(u) is not { } range) return false;
        if (PathWithin(range, u.Col, u.Row, row * Cols + col) is not { Count: > 0 } path) return false;
        foreach (var cell in path) u.Path.Enqueue(cell);
        return true;
    }

    // ── work 사거리·효과 범위 ────────────────────────────────────────────────

    private WorkData? Work(int id) => _db != null && _db.Works.TryGetValue(id, out var w) ? w : null;

    /// <summary>
    /// (fromCol, fromRow) 에서 work 가 (col, row) 칸을 겨눌 수 있나 — 사거리 모양 +0x7(1 마름모, 2 십자, 0 제자리)과
    /// 최소·최대 칸 +0xa/+0xc, 지형 &amp; 0x8 칸 제외. 모양 번호 뜻은 기본공격(2, 2~2)·힐(1, 0~4)·크래쉬 봄(0) 값으로 짐작했다.
    /// </summary>
    private bool InWorkRange(WorkData w, int fromCol, int fromRow, int col, int row)
    {
        if ((uint)col >= Cols || (uint)row >= Rows) return false;
        if (_map is { } map && (map.FlagsAt(col, row) & 0x8) != 0) return false;
        int dx = Math.Abs(col - fromCol), dy = Math.Abs(row - fromRow), d = dx + dy;
        return w.RangeShape switch
        {
            0 => d == 0,
            2 => (dx == 0 || dy == 0) && d >= w.RangeMin && d <= w.RangeMax,
            _ => d >= w.RangeMin && d <= w.RangeMax,
        };
    }

    /// <summary>
    /// 겨눈 칸에서 맞는 인물들. +0x13 이 1·4·5 면 그 칸의 인물 하나, 아니면 효과 범위 칸 안의 모두(편 안 가림).
    /// 효과 범위 모양 1 = 겨눈 칸 둘레 마름모(반경 +0x1a / 4), 6 = 쓰는 쪽에서 겨눈 칸 방향 곧은 줄(길이 +0x1a / 4 + 1) — 둘 다 <b>가설</b>.
    /// </summary>
    private List<int> WorkTargets(WorkData w, UnitState user, int col, int row)
    {
        if (w.SingleTarget)
            return LiveUnitAt(col, row) is { } one ? [Array.IndexOf(_units, one)] : [];

        var cells = new HashSet<(int, int)> { (col, row) };
        int arg = Math.Max(0, w.AreaArg / 4);
        if (w.AreaShape == 6)
        {
            int sx = Math.Sign(col - user.Col), sy = sx != 0 ? 0 : Math.Sign(row - user.Row);
            for (int k = 1; k <= arg; k++) cells.Add((col + sx * k, row + sy * k));
        }
        else
        {
            for (int y = -arg; y <= arg; y++)
                for (int x = -arg; x <= arg; x++)
                    if (Math.Abs(x) + Math.Abs(y) <= arg) cells.Add((col + x, row + y));
        }
        return [.. Enumerable.Range(0, _units.Length).Where(i => _units[i].Alive && cells.Contains((_units[i].Col, _units[i].Row)))];
    }

    /// <summary>work 를 쓸 수 있나 — TP + CTP 가 TP 비용 이상, SOUL 이 비용 이상.</summary>
    private bool CanAfford(UnitState u, WorkData w) =>
        u.Data != null && _db != null && u.Tp + u.Ctp >= _db.WorkTpCost(u.Data, w.Id) && u.Soul >= w.SoulBase;

    /// <summary>
    /// 기본공격 자리 찾기 — 이동 영역 칸(시작 자리 포함) 중 목표가 사거리에 드는, 시작 자리에서 가장 싼 칸.
    /// 길은 지금 자리에서 그 칸까지다.
    /// </summary>
    private (List<(int Col, int Row)> Path, int Cost)? FindAttackPath(int attackerIndex, int targetIndex)
    {
        var a = _units[attackerIndex];
        var t = _units[targetIndex];
        if (!a.Alive || !t.Alive || t.IsAlly == a.IsAlly || a.Data == null || Work(a.Data.BasicWorkId) is not { } w || !CanAfford(a, w)) return null;
        if (ComputeRange(a) is not { } range) return null;

        int best = -1, bestCost = int.MaxValue;
        for (int i = 0; i < range.Cost.Length; i++)
        {
            if (range.Cost[i] >= bestCost || !InWorkRange(w, i % Cols, i / Cols, t.Col, t.Row)) continue;
            bestCost = range.Cost[i];
            best = i;
        }
        return best < 0 || PathWithin(range, a.Col, a.Row, best) is not { } path ? null : (path, bestCost);
    }

    // ── 플레이어 대상 고르기 ─────────────────────────────────────────────────

    /// <summary>링 공격: 기본공격 대상 고르기(걸어가서 친다).</summary>
    private void BeginAttackTargeting()
    {
        if (_units[_turn].Data is not { } c) return;
        CommitMoveForAction();
        _targetWork = c.BasicWorkId;
        _targetIsBasicAttack = true;
        Toast("공격할 적을 클릭하세요 — 빨간 칸 안의 적 (Esc 취소)");
    }

    /// <summary>대상 고르는 중의 클릭. 처리했으면 true.</summary>
    private bool OnTargetClick(int col, int row)
    {
        if (_targetWork < 0) return false;
        if (!IsPlayerTurn || Work(_targetWork) is not { } w) { CancelTargeting(); return true; }
        var user = _units[_turn];

        if (_targetIsBasicAttack)
        {
            int target = LiveUnitAt(col, row) is { } t ? Array.IndexOf(_units, t) : -1;
            if (target < 0 || _units[target].IsAlly || FindAttackPath(_turn, target) is not { } plan)
            {
                Toast("공격할 수 없습니다 — 빨간 칸 안의 적을 고르세요 (우클릭·Esc 취소)");
                return true;
            }
            CancelTargeting();
            _routine = UseWorkRoutine(_turn, w, target, _units[target].Col, _units[target].Row, plan.Path);
            return true;
        }

        if (!InWorkRange(w, user.Col, user.Row, col, row))
        {
            Toast("사거리 밖입니다 — 노란 칸을 고르세요 (우클릭·Esc 취소)");
            return true;
        }
        if (WorkTargets(w, user, col, row).Count == 0)
        {
            Toast("그 칸에는 대상이 없습니다");
            return true;
        }
        CancelTargeting();
        _routine = UseWorkRoutine(_turn, w, -1, col, row, []);
        return true;
    }

    // ── work 쓰기 (fg-5 공격 · fg-6 어빌리티) ───────────────────────────────

    /// <summary>
    /// (필요하면 걸어가서) work 하나를 쓴다: 걸은 비용을 한 번에 빼고, 겨눈 쪽으로 돌고, 동작 5 → 8 → 24 를 재생하며 8 끝에 대상마다 판정,
    /// 쓰러진 인물은 동작 6 뒤 판에서 뺀다. TP·SOUL 비용과 SOUL 증가를 적용한다.
    /// </summary>
    private IEnumerator<bool> UseWorkRoutine(int userIndex, WorkData w, int targetIndex, int col, int row,
                                             List<(int Col, int Row)> path)
    {
        var a = _units[userIndex];
        foreach (var cell in path) a.Path.Enqueue(cell);
        while (a.IsBusy) yield return true;
        CommitMove(a);

        if (targetIndex >= 0) (col, row) = (_units[targetIndex].Col, _units[targetIndex].Row);
        if (col != a.Col || row != a.Row) a.Facing = FacingToward(a.Col, a.Row, col, row);
        if (!w.IsDamage) Popup(a, AbilityName(w), 0xFFB0E0FF, 15);

        var dying = new List<UnitState>();
        for (int step = 0; step < StrikeActions.Length; step++)
        {
            PlayAction(a, StrikeActions[step]);
            while (a.IsBusy) yield return true;
            if (step != StrikeHitStep) continue;

            var targets = targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row);
            foreach (int ti in targets) ApplyWork(a, w, _units[ti], dying);
        }

        if (a.Data != null && _db != null) a.Tp -= _db.WorkTpCost(a.Data, w.Id);
        a.Soul = Math.Clamp(a.Soul - w.SoulBase + (w.Kind switch { 0 => 10, 1 => 6, _ => 4 }), 0, a.MaxSoul);

        if (dying.Count > 0)
        {
            foreach (var d in dying) PlayAction(d, DeathAction);
            while (dying.Any(d => d.IsBusy)) yield return true;
            foreach (var d in dying) d.Alive = false;
            CheckOutcome();
        }
        for (double end = _lastTime + 0.3; _lastTime < end;) yield return true;
    }

    private void ApplyWork(UnitState a, WorkData w, UnitState t, List<UnitState> dying)
    {
        if (_db == null || a.Data == null || t.Data == null || t.Hp <= 0) return;
        var (amount, result, crit) = _db.Resolve(_rng, a.Data, a.Tp, a.Soul, t.Data, t.Tp, t.Hp, t.MaxHp, w);

        if (result == 1)
        {
            int healed = Math.Min(amount, t.MaxHp - t.Hp);
            t.Hp += healed;
            Popup(t, $"+{healed}", HealColor);
            return;
        }
        if (!w.IsDamage) return;
        if (result == 3 || amount <= 0) { Popup(t, "Miss", 0xFFC0C0C0); return; }

        t.Hp = Math.Max(0, t.Hp - amount);
        Popup(t, crit ? $"{amount}!" : amount.ToString(), crit ? 0xFFFF9040 : 0xFFFFE070, crit ? 24 : 18);
        t.Soul = Math.Min(t.MaxSoul, t.Soul + amount / Math.Max(1, _db.N(43)));
        if (t.Hp > 0) return;
        a.Soul = Math.Min(a.MaxSoul, a.Soul + 10);   // 처치(메시지 1016)
        dying.Add(t);
    }

    private static Facing FacingToward(int fromCol, int fromRow, int toCol, int toRow)
    {
        int dx = toCol - fromCol, dy = toRow - fromRow;
        if (Math.Abs(dx) >= Math.Abs(dy)) return dx >= 0 ? Facing.Right : Facing.Left;
        return dy >= 0 ? Facing.Down : Facing.Up;
    }

    private void CheckOutcome()
    {
        if (!_units.Any(u => u.Alive && !u.IsAlly)) _outcome = "승리 — 적을 모두 쓰러뜨렸습니다";
        else if (!_units.Any(u => u.Alive && u.IsAlly)) _outcome = "패배 — 아군이 모두 쓰러졌습니다";
    }

    // ── 적 AI (fg-7) ─────────────────────────────────────────────────────────

    private IEnumerator<bool> EnemyRoutine(int index)
    {
        var u = _units[index];
        for (double end = _lastTime + 0.5; _lastTime < end;) yield return true;

        // 1) 칠 수 있는 아군 중 가장 적게 걸어도 되는 쪽을 친다.
        (int Target, List<(int Col, int Row)> Path, int Cost)? best = null;
        for (int i = 0; i < _units.Length; i++)
        {
            if (!_units[i].Alive || !_units[i].IsAlly || FindAttackPath(index, i) is not { } plan) continue;
            if (best == null || plan.Cost < best.Value.Cost) best = (i, plan.Path, plan.Cost);
        }

        if (best is { } b && u.Data != null && Work(u.Data.BasicWorkId) is { } w)
        {
            var attack = UseWorkRoutine(index, w, b.Target, 0, 0, b.Path);
            while (attack.MoveNext()) yield return true;
        }
        else if (ComputeRange(u) is { } range)
        {
            // 2) 못 치면 갈 수 있는 칸 중 가장 가까운 아군과의 거리가 제일 짧은 칸으로 걷는다.
            var allies = _units.Where(a => a.Alive && a.IsAlly).ToList();
            int bestCell = -1, bestDist = int.MaxValue, bestCost = int.MaxValue;
            for (int i = 0; i < range.Cost.Length && allies.Count > 0; i++)
            {
                if (range.Cost[i] == int.MaxValue) continue;
                int d = allies.Min(a => Math.Abs(a.Col - i % Cols) + Math.Abs(a.Row - i / Cols));
                if (d < bestDist || d == bestDist && range.Cost[i] < bestCost) { bestDist = d; bestCell = i; bestCost = range.Cost[i]; }
            }
            if (bestCell >= 0 && range.Cost[bestCell] > 0)
            {
                foreach (var cell in range.PathTo(bestCell)) u.Path.Enqueue(cell);
                while (u.IsBusy) yield return true;
            }
        }

        for (double end = _lastTime + 0.3; _lastTime < end;) yield return true;
        if (_turn == index && _outcome.Length == 0 && u.Alive) Rest(index);
    }

    // ── 표시 ─────────────────────────────────────────────────────────────────

    private const uint HealColor = 0xFF60E060;

    private void PlayAction(UnitState u, int action)
    {
        double seconds = _sprites.TryGetValue(u.ChrCode, out var sprite) ? sprite.ActionSeconds(action, u.Facing) : 0;
        u.PlayAction(action, seconds > 0 ? seconds : 0.3);
    }

    private void Popup(UnitState u, string text, uint color, float size = 18)
    {
        var (x, y) = UnitFoot(u);
        _popups.Add((text, size, x, y - 70, _lastTime, color));
    }

    private void DrawPopups()
    {
        _popups.RemoveAll(p => _lastTime - p.Start > 1.2);
        foreach (var (text, size, x, y, start, color) in _popups)
        {
            int rise = (int)((_lastTime - start) * 30);
            var (_, w, _) = GetText(text, color, size);
            DrawText(text, x - w / 2 + 1, y - rise + 1, 0xFF000000, size);
            DrawText(text, x - w / 2, y - rise, color, size);
        }
    }

    /// <summary>인물 발밑의 HP(초록·빨강)·TP(노랑) 막대.</summary>
    private void DrawGauges()
    {
        foreach (var u in _units)
        {
            if (!u.Alive || u.MaxHp <= 0) continue;
            var (fx, fy) = UnitFoot(u);
            int w = TileW - 8, x = fx - w / 2, y = fy + TileH / 2 - 7;
            FillRect(x - 1, y - 1, w + 2, 7, 0xC0000000);
            FillRect(x, y, w * u.Hp / u.MaxHp, 3, u.IsAlly ? 0xFF50D060 : 0xFFE05050);
            if (u.MaxTp > 0) FillRect(x, y + 3, w * Math.Clamp(u.Tp, 0, u.MaxTp) / u.MaxTp, 2, 0xFFE8C040);
        }
    }

    /// <summary>어빌리티 대상 고르는 중이면 사거리 칸(노랑)을 깐다.</summary>
    private void DrawWorkRange()
    {
        if (_targetWork < 0 || _targetIsBasicAttack || _turn < 0 || Work(_targetWork) is not { } w) return;
        var u = _units[_turn];
        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                if (!InWorkRange(w, u.Col, u.Row, col, row)) continue;
                int x = col * TileW, y = GridTop + row * TileH;
                FillRect(x + 1, y + 1, TileW - 2, TileH - 2, 0x70F0D040);
                StrokeRect(x + 1, y + 1, TileW - 2, TileH - 2, 0xFFF0D040);
            }
    }

    private string TurnLine()
    {
        if (_turn < 0) return $"틱 {_tick} — 차례를 기다리는 중";
        var u = _units[_turn];
        string help = !u.IsAlly ? "적군이 움직입니다"
            : _targetWork >= 0 ? (_targetIsBasicAttack ? "공격할 적을 클릭 (우클릭·Esc 취소)" : "노란 칸 안의 대상을 클릭 (우클릭·Esc 취소)")
            : "파란 칸 클릭·WASD: 걷기   우클릭·Space: 링   Q: 휴식   우클릭·Esc: 취소";
        return $"틱 {_tick}   {(u.IsAlly ? "아군" : "적군")} {UnitName(_turn)} 차례   HP {u.Hp}/{u.MaxHp}  TP {u.Tp}/{u.MaxTp}  SOUL {u.Soul}   {help}";
    }

    private void DrawOutcome()
    {
        if (_outcome.Length == 0) return;
        var (_, w, h) = GetText(_outcome, 0xFFFFE070, 32);
        int x = (BoardWidth - w) / 2, y = (BoardHeight - h) / 2;
        FillRect(x - 24, y - 14, w + 48, h + 28, 0xE0101828);
        StrokeRect(x - 24, y - 14, w + 48, h + 28, 0xFFFFE070);
        DrawText(_outcome, x, y, 0xFFFFE070, 32);
    }
}
