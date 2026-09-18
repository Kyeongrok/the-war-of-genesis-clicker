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
    private double _outcomeAt;
    private double _nextTickAt;
    private readonly Random _rng = new();
    private readonly List<(string Text, float Size, int X, int Y, double Start, uint Color)> _popups = [];

    /// <summary>대상을 고르는 중인 work 번호(기본공격 포함). 고르는 중이 아니면 −1.</summary>
    private int _targetWork = -1;
    private bool _targetIsBasicAttack;

    /// <summary>공격 대상 커서 — 공격을 열면 칠 수 있는 적 중 HP 가 가장 낮은 적. 없으면 −1.</summary>
    private int _attackCursor = -1;

    /// <summary>기본공격 work 1 의 동작 — 준비 5 → 베기 8 → 복귀 24(분석-모션 <c>0x1007f0a0</c>). 판정은 베기 끝에 들어간다.</summary>
    private static readonly int[] StrikeActions = [5, 8, 24];
    private const int StrikeHitStep = 1;

    /// <summary>자세를 세우는 work — 516 방어(맞을 때 한 번 더 깎임), 515 회피(상대 명중 −DEX/5).</summary>
    private const int StanceDefendWork = 516, StanceEvadeWork = 515;
    private const double TickDelaySeconds = 0.05;

    /// <summary>그 인물을 내가 직접 움직이나 — 편 4 는 늘, 편 3(동맹)은 「모드 &gt; 동맹을 AI 가 움직임」을 껐을 때.</summary>
    private bool IsMine(UnitState u) => u.PlayerControlled || (u.IsAlly && !_allyAi);

    private bool IsPlayerTurn => _turn >= 0 && IsMine(_units[_turn]) && _routine == null && _outcome.Length == 0;

    /// <summary>전투를 넘어 이어지는 파티 상태 — Chr 번호 → 그 인물의 레벨·경험치·장비·어빌리티.</summary>
    private readonly Dictionary<int, CharacterData> _party = [];

    /// <summary>지금 판의 아군 상태를 파티에 담아 둔다(다음 전투로 이어진다).</summary>
    private void RememberParty()
    {
        foreach (var unit in _units)
            if (unit.IsAlly && unit.Data is { } c) _party[unit.ChrCode] = c;
    }

    /// <summary>게임 표를 다 읽은 뒤 인물마다 전투 수치를 채운다.</summary>
    private void InitBattle()
    {
        if (_db == null) return;
        foreach (var unit in _units)
        {
            if (_db.Character(unit.ChrCode) is not { } c) continue;
            // 데모라 쌓인 경험치는 지금 레벨에 맞춰 시작한다(저장 파일이 없다).
            // DUELDX_CUMEXP 로 아군 시작값을 바꿀 수 있다 — 레벨업 창을 시험할 때 쓴다(예: 190 이면 한 번만 쓰러뜨려도 오름).
            int startCum = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_CUMEXP"), out int v) ? v : c.Level * 100;
            // 앞 전투에서 얻은 레벨·경험치·장비는 다음 전투로 이어진다(_party 가 들고 있다).
            unit.Data = _party.TryGetValue(unit.ChrCode, out var carried) ? carried
                      : unit.IsAlly ? c with { Exp = DemoExp, CumExp = startCum }
                      : c with { CumExp = c.Level * 100 };
            unit.MaxHp = unit.Hp = Math.Max(1, _db.MaxHp(c));
            unit.MaxTp = unit.Tp = _db.MaxTp(c);
            unit.Stp = Math.Max(1, _db.Stp(c));
            unit.MaxSoul = _db.MaxSoul(c);
            // DUELDX_SOUL 로 시작 SOUL 을 올릴 수 있다 — 어빌리티·상태이상을 시험할 때 쓴다.
            unit.Soul = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_SOUL"), out int soul) ? Math.Min(unit.MaxSoul, soul) : _db.SoulStart;
            // DUELDX_AILMENT=<번호>[:<값>] 이면 그 상태이상을 걸고 시작한다(화면 밖 시험용).
            if (Environment.GetEnvironmentVariable("DUELDX_AILMENT") is { Length: > 0 } spec)
            {
                string[] parts = spec.Split(':');
                if (byte.TryParse(parts[0], out byte id))
                {
                    unit.StatusId[0] = id;
                    unit.StatusValue[0] = parts.Length > 1 && short.TryParse(parts[1], out short v2) ? v2 : (short)10;
                }
            }
            unit.HasTurn = true;
        }
        FillDemoInventory();
    }

    private void UpdateTurn()
    {
        if (_loading || _db == null || _outcome.Length > 0) return;
        if (UpdateLevelUp()) return;   // 레벨업 창이 떠 있는 동안은 차례가 멈춘다

        if (_routine != null)
        {
            if (!_routine.MoveNext()) _routine = null;
            return;
        }

        if (_turn >= 0)
        {
            var u = _units[_turn];
            if (!u.Alive) EndTurn();
            else if (IsMine(u) && !u.IsBusy && u.Tp <= 0 && !_abilityMenu && _targetWork < 0) Rest(_turn);
            return;
        }

        if (_lastTime < _nextTickAt) return;
        for (int guard = 0; guard < 10000; guard++)
        {
            int next = Array.FindIndex(_units, u => u.Alive && u.HasTurn && u.LeaderIndex < 0 && CanTakeTurn(u));
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
        TickAilments();
        foreach (var u in _units.Where(u => u.Alive && u.Hp <= 0))
            if (!SurvivesFatal(u)) KillUnit(u);
    }

    /// <summary>차례 시작 — 그 인물을 고른다(fg-8). 적이면 AI 를 돌린다(fg-7).</summary>
    private void StartTurn(int index)
    {
        _turn = index;
        _selected = index;
        _units[index].Stance = 0;   // 자세는 다음 차례가 오면 풀린다(0x10072d90)
        _units[index].OriginCol = _units[index].Col;
        _units[index].OriginRow = _units[index].Row;
        CancelTargeting();
        _heldMoveKeys.Clear();
        // 편 4 만 내가 움직인다. 편 3(동맹 AI)과 적은 같은 AI 로 스스로 움직인다(ba-6·ba-11).
        if (IsMine(_units[index])) { Toast($"{UnitName(index)} 차례"); PlayTurnVoice(_units[index]); }
        else
        {
            if (_units[index].IsAlly) Toast($"{UnitName(index)} 차례 — 동맹이 스스로 움직입니다");
            _routine = AiRoutine(index);
        }
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
        _attackCursor = -1;
        _targetWork = -1;
        _targetItem = 0;
        _abilityMenu = false;
        _itemMenu = false;
    }

    /// <summary>공격·어빌리티를 열 때: 지금까지 걸은 비용을 한 번에 뺀다(취소하면 되돌림).</summary>
    private void CommitMoveForAction()
    {
        var u = _units[_turn];
        _commitUndo = (u.Tp, u.OriginCol, u.OriginRow);
        CommitMove(u);
    }

    /// <summary>Esc 로 걸은 것을 물린다 — 차례 시작 자리로 되돌린다. 물렸으면 true.</summary>
    private bool UndoMove()
    {
        if (!IsPlayerTurn) return false;
        var u = _units[_turn];
        if (u.IsBusy || (u.Col == u.OriginCol && u.Row == u.OriginRow)) return false;
        u.WarpTo(u.OriginCol, u.OriginRow);
        return true;
    }

    /// <summary>
    /// 취소 — 목록·대상 고르기면 비용을 돌려주고 링을 다시 연다. <paramref name="undoMove"/> 면(Esc) 그다음 걸음도 물린다.
    /// 우클릭은 걸음을 물리지 않는다. 취소한 것이 있으면 true.
    /// </summary>
    private bool CancelStep(bool undoMove)
    {
        if (_abilityMenu || _targetWork >= 0) { CancelTargeting(refund: true); OpenRing(_turn, reopen: true); return true; }
        return undoMove && UndoMove();
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
        if (u.Tp > 0 && u.MaxTp > 0 && _db != null && !u.HasStatus(26))
        {
            int heal = (int)((long)(u.MaxHp - u.Hp) * u.Tp / u.MaxTp * _db.N(35) / 100);
            if (heal > 0)
            {
                int before = u.Hp;
                u.Hp += heal;
                ShowNumber(u, _db.T(159), HealColor2, rise: false, count: (before, u.Hp));
            }
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
            if (range.Cost[i] >= bestCost || !InWorkRange(w, i % Cols, i / Cols, t.Col, t.Row, a)) continue;
            bestCost = range.Cost[i];
            best = i;
        }
        return best < 0 || PathWithin(range, a.Col, a.Row, best) is not { } path ? null : (path, bestCost);
    }

    // ── 플레이어 대상 고르기 ─────────────────────────────────────────────────

    /// <summary>적을 클릭하면 링을 안 거치고 바로 친다(fa-12) — 걸음 비용도 그때 함께 뺀다.</summary>
    private void QuickAttack(int targetIndex)
    {
        if (_units[_turn].Data is not { } c || Work(c.BasicWorkId) is not { } w) return;
        if (FindAttackPath(_turn, targetIndex) is not { } plan)
        {
            Toast("공격할 수 없습니다 — 빨간 칸 안의 적을 고르세요");
            return;
        }
        CommitMoveForAction();
        _commitUndo = null;   // 바로 치므로 되돌릴 일이 없다
        _routine = UseWorkRoutine(_turn, w, targetIndex, _units[targetIndex].Col, _units[targetIndex].Row, plan.Path);
    }

    /// <summary>링 공격: 기본공격 대상 고르기(걸어가서 친다).</summary>
    private void BeginAttackTargeting()
    {
        if (_units[_turn].Data is not { } c) return;
        CommitMoveForAction();
        _targetWork = c.BasicWorkId;
        _targetIsBasicAttack = true;

        var targets = AttackableEnemies();
        if (targets.Count == 0)
        {
            CancelTargeting(refund: true);
            Toast("공격할 수 있는 적이 없습니다");
            return;
        }
        _attackCursor = targets[0];
        string confirm = KeyBindings.KeyName(_keys[KeyAction.Attack]);
        Toast($"{UnitName(_attackCursor)} 을(를) 노립니다 — 클릭·Enter·{confirm}: 공격, Tab: 다른 적, 우클릭·Esc: 취소");
    }

    /// <summary>차례인 인물이 지금 칠 수 있는 적 — HP 가 낮은 순(같으면 배열 순).</summary>
    private List<int> AttackableEnemies() =>
        [.. Enumerable.Range(0, _units.Length)
            .Where(i => _units[i].Alive && !_units[i].IsAlly && FindAttackPath(_turn, i) != null)
            .OrderBy(i => _units[i].Hp).ThenBy(i => i)];

    /// <summary>공격 커서를 다음(HP 순) 적으로 옮긴다.</summary>
    private void CycleAttackCursor()
    {
        var targets = AttackableEnemies();
        if (targets.Count == 0) return;
        _attackCursor = targets[(targets.IndexOf(_attackCursor) + 1) % targets.Count];
    }

    /// <summary>커서의 적을 친다.</summary>
    private void AttackCursorTarget()
    {
        if (_attackCursor < 0 || !IsPlayerTurn) return;
        var (col, row) = (_units[_attackCursor].Col, _units[_attackCursor].Row);
        OnTargetClick(col, row);
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

        if (!InWorkRange(w, user.Col, user.Row, col, row, user))
        {
            Toast("사거리 밖입니다 — 노란 칸을 고르세요 (우클릭·Esc 취소)");
            return true;
        }
        if (WorkTargets(w, user, col, row).Count == 0)
        {
            Toast("그 칸에는 대상이 없습니다");
            return true;
        }
        ConsumeTargetItem();          // 아이템이면 이때 개수가 하나 준다
        CancelTargeting();
        _routine = UseWorkRoutine(_turn, w, -1, col, row, []);
        return true;
    }

    // ── work 쓰기 (fg-5 공격 · fg-6 어빌리티) ───────────────────────────────

    /// <summary>
    /// (필요하면 걸어가서) work 하나를 쓴다: 걸은 비용을 한 번에 빼고, 겨눈 쪽으로 돌고, 동작 5 → 8 → 24 를 재생하며 <b>치는 순간</b>에 대상마다 판정,
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

        MoveFollowers(userIndex);          // 군단이면 부하가 대장 진형으로 따라온다
        var dying = new List<UnitState>();
        int[] actions = ActionsFor(w);
        int hitStep = HitStepFor(w, actions.Length);
        for (int step = 0; step < Math.Max(actions.Length, 1); step++)
        {
            if (step != hitStep)
            {
                if (step >= actions.Length) continue;
                PlayAction(a, actions[step]);
                while (a.IsBusy) yield return true;
                continue;
            }

            // 치는 동작은 끝까지 기다리지 않는다 — 모션 안 「치는 순간」(소리 키 자리)에 바로 판정을 내서
            // 피해 숫자와 맞는 모션이 때리는 그림과 같이 나오게 한다.
            if (step < actions.Length)
            {
                PlayAction(a, actions[step]);
                double hitAt = _sprites.TryGetValue(a.ChrCode, out var sprite) ? sprite.HitMomentSeconds(actions[step], a.Facing) : 0;
                for (double end = _lastTime + hitAt; _lastTime < end;) yield return true;
            }

            var targets = targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row);
            ScheduleAbilitySounds(w);
            SpawnAbilityEffects(w, a, col, row);
            foreach (int ti in targets) ApplyWork(a, w, _units[ti], dying);
            // 군단 행동(상태 15) — 대장이 친 대상을 부하들도 함께 친다
            if (w.IsDamage && targets.Count > 0) FollowersAttack(userIndex, _units[targets[0]], dying);
            while (a.IsBusy) yield return true;   // 남은 동작을 마저 재생한다
        }

        // 이스케이프는 겨눈 빈 칸으로 순간이동한다(분석-모션 ba-10).
        if (w.Id == EscapeWork && LiveUnitAt(col, row) == null)
        {
            a.WarpTo(col, row);
            a.OriginCol = col;
            a.OriginRow = row;
        }

        if (w.Id is StanceDefendWork or StanceEvadeWork) a.Stance = w.Id == StanceDefendWork ? 1 : 2;
        if (a.Data != null && _db != null) a.Tp -= _db.WorkTpCost(a.Data, w.Id);
        a.Soul = Math.Clamp(a.Soul - w.SoulBase + (w.Kind switch { 0 => 10, 1 => 6, _ => 4 }), 0, a.MaxSoul);

        if (dying.Count > 0)
        {
            foreach (var d in dying) { PlayActionFor(d, HitAction, DeathActionTicks); Play(SoundDeath); }
            while (dying.Any(d => d.IsBusy)) yield return true;
            foreach (var d in dying)
            {
                d.Alive = false;
                PromoteFollower(Array.IndexOf(_units, d));   // 대장이 죽으면 첫 부하가 대장이 된다
            }
            CheckOutcome();
            QueueLevelUps();
        }
        for (double end = _lastTime + 0.1; _lastTime < end;) yield return true;
    }

    private void ApplyWork(UnitState a, WorkData w, UnitState t, List<UnitState> dying)
    {
        if (_db == null || a.Data == null || t.Data == null || t.Hp <= 0) return;
        var (amount, result, crit) = _db.Resolve(_rng, a.Data, a.Tp, a.Soul, t.Data, t.Tp, t.Hp, t.MaxHp, w, t.Stance);

        if (result == 1)
        {
            int before = t.Hp;
            t.Hp = Math.Min(t.MaxHp, t.Hp + amount);
            // 회복은 떠오르지 않고 옛 HP 에서 새 HP 로 세어 올라간다(노랑).
            ShowNumber(t, _db.T(159), HealColor2, rise: false, count: (before, t.Hp));
            ApplyAilments(a, t, w);
            return;
        }
        if (!w.IsDamage)
        {
            if (result != 3) ApplyAilments(a, t, w);   // 종류 2·3(큐어·격려)은 상태이상만 건다
            return;
        }
        if (result == 3 || amount <= 0) { ShowNumber(t, _db.T(42) is { Length: > 0 } m ? m : "Miss", MissColor); return; }

        amount = AilmentDamage(a, t, amount);
        t.Hp = Math.Max(0, t.Hp - amount);
        ShowNumber(t, $"{_db.T(159)} {amount}", DamageColor);
        PlayHitReaction(t, damaged: true);
        if (crit) PlayCritFlash();
        t.Soul = Math.Min(t.MaxSoul, t.Soul + amount / Math.Max(1, _db.N(43)));
        PlayHurtVoice(t);
        ApplyAilments(a, t, w);
        Counterattack(a, t, amount);
        if (t.Hp > 0) return;
        if (SurvivesFatal(t)) return;
        a.Soul = Math.Min(a.MaxSoul, a.Soul + 10);   // 처치(메시지 1016)
        GainKillExp(a, t);
        dying.Add(t);
    }

    /// <summary>자기 자리에 쓰는 work(모드 0·2)면 겨냥 없이 바로 쓴다.</summary>
    private bool UseSelfCentredWork(WorkData w)
    {
        if (!w.SelfCentred) return false;
        CancelTargeting();
        _routine = UseWorkRoutine(_turn, w, -1, _units[_turn].Col, _units[_turn].Row, []);
        return true;
    }

    private static Facing FacingToward(int fromCol, int fromRow, int toCol, int toRow)
    {
        int dx = toCol - fromCol, dy = toRow - fromRow;
        if (Math.Abs(dx) >= Math.Abs(dy)) return dx >= 0 ? Facing.Right : Facing.Left;
        return dy >= 0 ? Facing.Down : Facing.Up;
    }

    private void CheckOutcome()
    {
        if (_outcome.Length > 0) return;
        if (!_units.Any(u => u.Alive && !u.IsAlly)) { _outcome = "승리 — 적을 모두 쓰러뜨렸습니다"; _outcomeAt = _lastTime; PlayOutcomeMusic(win: true); }
        else if (!_units.Any(u => u.Alive && u.IsAlly)) { _outcome = "패배 — 아군이 모두 쓰러졌습니다"; _outcomeAt = _lastTime; PlayOutcomeMusic(win: false); }
    }

    // ── 표시 ─────────────────────────────────────────────────────────────────

    private const uint HealColor = 0xFF60E060;

    private void PlayAction(UnitState u, int action)
    {
        double seconds = _sprites.TryGetValue(u.ChrCode, out var sprite) ? sprite.ActionSeconds(action, u.Facing) : 0;
        u.PlayAction(action, seconds > 0 ? seconds : 0.3);
        ScheduleActionSounds(u, action);
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
        if (_targetWork >= 0 && _targetIsBasicAttack && _attackCursor >= 0)
        {
            // 노리는 적 칸에 깜빡이는 빨간 테두리
            var t = _units[_attackCursor];
            uint alpha = (uint)(160 + 95 * (0.5 + 0.5 * Math.Sin(_lastTime * Math.PI * 4)));
            uint color = alpha << 24 | 0xFF3030;
            int cx = t.Col * TileW, cy = GridTop + t.Row * TileH;
            for (int k = 0; k < 3; k++) StrokeRect(cx + k, cy + k, TileW - 2 * k, TileH - 2 * k, color);
            return;
        }
        if (_targetWork < 0 || _targetIsBasicAttack || _turn < 0 || Work(_targetWork) is not { } w) return;
        var u = _units[_turn];
        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                if (!InWorkRange(w, u.Col, u.Row, col, row, u)) continue;
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
            : _targetWork >= 0 ? (_targetIsBasicAttack ? "노리는 적: 클릭·Enter 공격, Tab 다른 적 (우클릭·Esc 취소)" : "노란 칸 안의 대상을 클릭 (우클릭·Esc 취소)")
            : $"파란 칸 클릭·{KeyBindings.KeyName(_keys[KeyAction.MoveUp])}{KeyBindings.KeyName(_keys[KeyAction.MoveLeft])}{KeyBindings.KeyName(_keys[KeyAction.MoveDown])}{KeyBindings.KeyName(_keys[KeyAction.MoveRight])}: 걷기   우클릭·{KeyBindings.KeyName(_keys[KeyAction.Ring])}: 링   {KeyBindings.KeyName(_keys[KeyAction.Attack])}: 공격   {KeyBindings.KeyName(_keys[KeyAction.Rest])}: 휴식   우클릭: 취소   Esc: 취소·걸음 물리기";
        return $"틱 {_tick}   {(u.IsAlly ? "아군" : "적군")} {UnitName(_turn)} 차례   HP {u.Hp}/{u.MaxHp}  TP {u.Tp}/{u.MaxTp}  SOUL {u.Soul}   {help}";
    }

}
