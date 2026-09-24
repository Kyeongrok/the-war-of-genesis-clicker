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
    /// <summary>전투가 흐른 틱 — 원본은 전투를 만들 때 <b>1</b> 로 놓는다(<c>0x100644d4</c>).</summary>
    private int _tick = 1;
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

    /// <summary>
    /// 기본공격 work 마다 도는 동작이 다르다(분석-모션 「인물별 동작 구간표는 없다」).
    /// 288명은 work 1 로 베고, 48명은 387(때리는 동작이 없다), 47명은 1479, 48명은 6·1584 로 <b>쏜다</b>(동작 9).
    /// </summary>
    private static readonly Dictionary<int, int[]> BasicWorkActions = new()
    {
        [1] = [5, 8, 24],
        [387] = [5, 7],
        [1479] = [8],
        [6] = [9],
        [1584] = [9],
    };
    private const int StrikeHitStep = 1;

    /// <summary>자세를 세우는 work — 516 방어(맞을 때 한 번 더 깎임), 515 회피(상대 명중 −DEX/5).</summary>
    private const int StanceDefendWork = 516, StanceEvadeWork = 515;
    private const double TickDelaySeconds = 0.05;

    /// <summary>그 인물을 내가 직접 움직이나 — 편 4 는 늘, 편 3(동맹)은 「모드 &gt; 동맹을 AI 가 움직임」을 껐을 때.</summary>
    private bool IsMine(UnitState u) => !AutoPlay && (u.PlayerControlled || (u.IsAlly && !_allyAi));

    /// <summary>DUELDX_AUTOPLAY=1 이면 내 부대도 AI 가 움직인다 — 화면 밖 시험에서 전투를 저절로 돌리려고.</summary>
    private static readonly bool AutoPlay = Environment.GetEnvironmentVariable("DUELDX_AUTOPLAY") == "1";

    /// <summary>DUELDX_TRACE=1 이면 동작 재생을 <c>%TEMP%\dueldx_trace.log</c> 에 적는다(화면 밖 시험용).</summary>
    private static readonly bool Trace = Environment.GetEnvironmentVariable("DUELDX_TRACE") == "1";

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
        // 적 레벨의 기준이 되는 파티 레벨은 <b>유닛을 채우기 전에</b> 한 번 셈한다 —
        // 채우는 도중에 세면 아직 안 채워진 아군 때문에 순서에 따라 값이 흔들린다.
        int partyLevel = PartyLevel();
        foreach (var unit in _units)
        {
            if (_db.Character(unit.ChrCode) is not { } c) continue;
            // 데모라 쌓인 경험치는 지금 레벨에 맞춰 시작한다(저장 파일이 없다).
            // DUELDX_CUMEXP 로 아군 시작값을 바꿀 수 있다 — 레벨업 창을 시험할 때 쓴다(예: 190 이면 한 번만 쓰러뜨려도 오름).
            int startCum = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_CUMEXP"), out int v) ? v : c.Level * 100;
            // 앞 전투에서 얻은 레벨·경험치·장비는 다음 전투로 이어진다(_party 가 들고 있다).
            // DUELDX_CUMEXP 를 주면 레벨도 그 값에 맞춘다 — 쌓인 경험치와 레벨은 늘 짝이 맞아야 한다(레벨 = 쌓인 경험치 ÷ 100).
            unit.Data = _party.TryGetValue(unit.ChrCode, out var carried) ? carried
                      : unit.IsAlly ? c with { Exp = DemoExp, CumExp = startCum, Level = (ushort)Math.Max(c.Level, startCum / 100),
                                               // DUELDX_JOB=<직업> 이면 아군 직업을 바꾼다 — 전직 화면(2단계·3단계 단추)을 시험할 때 쓴다.
                                               JobId = ushort.TryParse(Environment.GetEnvironmentVariable("DUELDX_JOB"), out ushort job) ? job : c.JobId }
                      : c with { CumExp = c.Level * 100 };
            // 파티 레벨에 맞춰 자란다 — 면제 명단(0002.nch)에 없는 인물만(0x1007a8e0).
            if (!unit.IsAlly) unit.Data = GrowToPartyLevel(unit.Data ?? c, unit.LevelOffset, partyLevel);

            // 최대치는 <b>이어받은 인물</b>로 셈한다 — 앞 전투에서 레벨이 올랐으면 그 값이 따라와야 한다.
            var data = unit.Data ?? c;
            unit.MaxHp = unit.Hp = Math.Max(1, _db.MaxHp(data));
            unit.MaxTp = _db.MaxTp(data);
            // 원본은 유닛을 만들 때 <b>TP 를 0 으로 민다</b>(0x10071941) — 아무도 다시 안 채운다.
            // 그래서 첫 차례는 「최대TP ÷ STP」가 가장 작은 인물이 가져간다.
            unit.Tp = 0;
            unit.Stp = Math.Max(1, _db.Stp(data));
            unit.MaxSoul = _db.MaxSoul(data);
            // DUELDX_SOUL 로 시작 SOUL 을 올릴 수 있다 — 어빌리티·상태이상을 시험할 때 쓴다.
            unit.Soul = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_SOUL"), out int soul) ? Math.Min(unit.MaxSoul, soul) : _db.SoulStart;
            // DUELDX_AILMENT=<번호>[:<값>][,<번호>[:<값>]…] 이면 그 상태이상들을 칸 순서대로 걸고 시작한다(화면 밖 시험용).
            if (Environment.GetEnvironmentVariable("DUELDX_AILMENT") is { Length: > 0 } spec)
            {
                var wanted = spec.Split(',', StringSplitOptions.RemoveEmptyEntries);
                for (int s = 0; s < wanted.Length && s < 3; s++)
                {
                    string[] parts = wanted[s].Split(':');
                    if (!byte.TryParse(parts[0], out byte id)) continue;
                    unit.StatusId[s] = id;
                    unit.StatusValue[s] = parts.Length > 1 && short.TryParse(parts[1], out short v2) ? v2 : (short)10;
                }
            }
            // 차례 깃발도 0 으로 시작한다(0x1007196c) — 틱이 흘러 TP 가 가득 차야 차례가 온다.
            unit.HasTurn = false;
        }
        // 챕터 스크립트가 가방을 채웠으면 데모용 아이템은 안 넣는다 — 자료가 준 것이 옳다.
        if (_chapterFired.Count == 0) FillDemoInventory();
    }

    private void UpdateTurn()
    {
        if (_loading || _db == null || _outcome.Length > 0) return;
        // 이벤트(대사)가 도는 동안은 틱도 차례도 안 흐른다(0x10066197).
        if (EventsBusy) return;
        RunEvents();                 // 틱이 안 흐르는 사이에도 조건(턴 수 따위)은 본다
        if (EventsBusy) return;
        // DUELDX_WIN=1 이면 시작하자마자 이긴 것으로 친다 — 전투 이어짐·진행 깃발·모세스 전환을 화면 밖에서 시험할 때 쓴다.
        if (Environment.GetEnvironmentVariable("DUELDX_WIN") == "1")
        {
            foreach (var u in _units.Where(u => !u.IsAlly)) u.Alive = false;
            CheckOutcome();
            return;
        }
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

        // 불러온 판은 저장했던 인물의 차례로 곧장 돌아간다 — 원본도 판을 읽은 뒤 Active 유닛(+0x4cf4)의 상태 22 로 선다
        // (0x100619c0, 분석-시스템메뉴 2.4b). 틱을 흘려 다시 고르면 같은 틱에 TP 가 찬 앞 번호 적이 먼저 움직였다.
        if (_resumeTurn >= 0)
        {
            int r = _resumeTurn;
            _resumeTurn = -1;
            if (r < _units.Length && _units[r] is { Alive: true, OnField: true, HasTurn: true } ru && ru.LeaderIndex < 0 && CanTakeTurn(ru))
            {
                StartTurn(r, resume: true);
                return;
            }
        }

        if (_lastTime < _nextTickAt) return;
        for (int guard = 0; guard < 10000; guard++)
        {
            int next = Array.FindIndex(_units, u => u.Alive && u.OnField && u.HasTurn && u.LeaderIndex < 0 && CanTakeTurn(u));
            if (next >= 0) { StartTurn(next); return; }
            AdvanceTick();
        }
    }

    /// <summary>
    /// 아군 레벨 상위 셋의 평균 — 원본이 적 레벨을 맞추는 기준(분석-전투 「Lev.dat 성장」).
    /// </summary>
    private int PartyLevel()
    {
        var levels = _units.Where(u => u.IsAlly)
                           .Select(u => _party.TryGetValue(u.ChrCode, out var carried) ? carried.Level
                                      : _db?.Character(u.ChrCode)?.Level ?? 0)
                           .Select(v => (int)v)
                           .Where(v => v > 0)
                           .OrderByDescending(v => v).Take(3).ToList();
        return levels.Count == 0 ? 1 : levels.Sum() / levels.Count;
    }

    /// <summary>
    /// 그 인물을 <paramref name="offset"/> + 파티 레벨로 키운다 — <b>늘 <c>.chr</c> 원본에서</b> 다시 셈하므로 쌓이지 않고,
    /// <b>TP 제수와 CTP 는 그대로</b> 둔다. 면제 명단에 있으면 그대로 돌려준다.
    /// </summary>
    private CharacterData GrowToPartyLevel(CharacterData c, int offset, int partyLevel)
    {
        if (_db is null || _db.LevelExempt.Contains(c.Code)) return c;
        var rows = _db.LevelGrowth;
        if (rows.Count == 0) return c;

        int level = Math.Max(1, offset + partyLevel);
        var g = rows[Math.Min(level, rows.Count) - 1];
        int Grow(int v, int percent) => v + v * percent / 100;

        return c with
        {
            Level = (ushort)level,
            CumExp = level * 100,
            Lp = (uint)Grow((int)c.Lp, g.Lp),
            Tp = (ushort)Grow(c.Tp, g.Tp),
            Psy = (ushort)Grow(c.Psy, g.Psy),
            Dex = (ushort)Grow(c.Dex, g.Dex),
            Dep = (ushort)Grow(c.Dep, g.Dep),
        };
    }

    private void AdvanceTick()
    {
        _tick++;
        foreach (var u in _units.Where(u => u.Alive))
        {
            // 원본(0x10071db0)은 STP 와 턴 속도(38)를 <b>먼저 다 더하고</b> 한 번만 자른 뒤 차례 깃발을 세운다 —
            // 나중에 더하면 38 이 양수일 때 차례가 한 틱 늦는다. 아래쪽 0 자르기는 원본에 없다.
            u.Tp += u.Stp;
            if (u.HasStatus(38)) u.Tp += u.Status(38);
            if (u.Tp > u.MaxTp) u.Tp = u.MaxTp;
            if (u.Tp >= u.MaxTp) u.HasTurn = true;
        }
        TickAilments();
        StepObjects();
        // 22·23·24 는 HP 가 남아 있어도 SOUL·TP 가 조건에 닿으면 쓰러뜨린다(0x1007c689~).
        foreach (var u in _units.Where(u => u.Alive && (u.Hp <= 0 || DiesByStatus(u))))
            if (!SurvivesFatal(u)) KillUnit(u);
        CheckOutcome();
    }

    /// <summary>차례 시작 — 그 인물을 고른다(fg-8). 적이면 AI 를 돌린다(fg-7).</summary>
    /// <summary>불러온 뒤 이어 받을 차례 — 저장할 때 차례였던 인물. 없으면 −1.</summary>
    private int _resumeTurn = -1;

    /// <param name="resume">
    /// 불러온 판의 차례를 이어 받는 것 — 턴 수만 맞추고(저장 때 하나 빼 둔 것), 이벤트 타이머·자동 회복은 이미 그 차례에 돌았으니 다시 안 돌린다.
    /// 원본도 판을 읽으면 차례 시작 처리 없이 상태 22 로 선다(0x100619c0).
    /// </param>
    private void StartTurn(int index, bool resume = false)
    {
        _turn = index;
        _selected = index;
        _turnNo++;                  // 이벤트 조건 1·3 이 보는 턴 수(0x10067d36)
        if (!resume)
        {
            // 켜 둔 이벤트 타이머는 턴마다 하나씩 센다(0x10067d3c 가 턴을 올린 바로 다음 줄에서 부른다).
            for (int i = 0; i < _eventTimer.Length; i++) if (_eventTimerRun[i]) _eventTimer[i]++;
            _units[index].Stance = 0;   // 자세는 다음 차례가 오면 풀린다(0x10072d90)
            AutoHeal(_units[index]);    // 8(자동 회복)은 차례를 받는 순간 채운다
        }
        _units[index].OriginCol = _units[index].Col;
        _units[index].OriginRow = _units[index].Row;
        CancelTargeting();
        _heldMoveKeys.Clear();
        // 편 4 만 내가 움직인다. 편 3(동맹 AI)과 적은 같은 AI 로 스스로 움직인다(ba-6·ba-11).
        if (IsMine(_units[index]))
        {
            // 「누구 차례」 알림은 안 띄운다(사용자 요청) — 머리줄에 이미 나오고, 무엇보다 <b>같은 알림 칸</b>이라
            // 상자에서 얻은 것 같은 결과 알림을 곧바로 덮어써 못 읽게 했다. 차례는 부르는 목소리로 알린다.
            PlayTurnVoice(_units[index]);
            // 자동 저장(슬롯 20) — 원본은 전투 시작·새 차례마다 깃발(+0x4cd8)을 세우고, 플레이어가 유닛을 고르는
            // 상태 22 에 처음 들어설 때 SaveGame(20) 한 뒤 지운다(0x1006acc0). AI 차례는 상태 10~12 가 카메라만 옮기고
            // 깃발을 저장 없이 지우므로 저장이 없다. 곧 「내 차례가 시작될 때마다 한 번」이다(분석-시스템메뉴 2.4).
            AutoSave();
        }
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
        _aimCell = null;
        _targetHotkey = -1;
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
    /// <summary>TP 와 SOUL 이 되나 — 필요 SOUL 은 체질 덧붙임까지 넣은 값이다(분석-전투 ba-4).</summary>
    private bool CanAfford(UnitState u, WorkData w) =>
        u.Data != null && _db != null && u.Tp + u.Ctp >= TpCostFor(u, u.Data, w.Id) && u.Soul >= SoulNeedFor(u, u.Data, w.Id);

    /// <summary>
    /// 기본공격 자리 찾기 — 이동 영역 칸(시작 자리 포함) 중 목표가 사거리에 드는, 시작 자리에서 가장 싼 칸.
    /// 길은 지금 자리에서 그 칸까지다.
    /// </summary>
    private (List<(int Col, int Row)> Path, int Cost)? FindAttackPath(int attackerIndex, int targetIndex)
    {
        var a = _units[attackerIndex];
        var t = _units[targetIndex];
        if (!a.Alive || !t.Alive || !SeesAsFoe(a, t) || a.Data == null || Work(a.Data.BasicWorkId) is not { } w || !CanAfford(a, w)) return null;
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
            Hint("공격할 수 없습니다 — 빨간 칸 안의 적을 고르세요");
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
        Hint($"{UnitName(_attackCursor)} 을(를) 노립니다 — 클릭·Enter·{confirm}: 공격, Tab: 다른 적, 우클릭·Esc: 취소");
    }

    /// <summary>
    /// 차례인 인물이 지금 칠 수 있는 적 — <b>걸어갈 비용이 적은(가까운) 순</b>, 같으면 HP 낮은 순.
    /// 약한 적부터 노리면 멀리 걸어가 TP 를 헛되이 쓰기 일쑤라 가까운 적을 먼저 세운다.
    /// </summary>
    private List<int> AttackableEnemies() =>
        [.. Enumerable.Range(0, _units.Length)
            .Where(i => _units[i].Alive && SeesAsFoe(_units[_turn], _units[i]))
            .Select(i => (Index: i, Plan: FindAttackPath(_turn, i)))
            .Where(t => t.Plan != null)
            .OrderBy(t => t.Plan!.Value.Cost).ThenBy(t => t.Plan!.Value.Path.Count)
            .ThenBy(t => _units[t.Index].Hp).ThenBy(t => t.Index)
            .Select(t => t.Index)];

    /// <summary>공격 커서를 다음(HP 순) 적으로 옮긴다 — 어빌리티를 겨누는 중이면 그 어빌리티 사거리로 센다.</summary>
    private void CycleAttackCursor()
    {
        var targets = !_targetIsBasicAttack && _targetWork >= 0 && Work(_targetWork) is { } aw
            ? AbilityTargets(aw) : AttackableEnemies();
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
                Hint("공격할 수 없습니다 — 빨간 칸 안의 적을 고르세요 (우클릭·Esc 취소)");
                return true;
            }
            CancelTargeting();
            _routine = UseWorkRoutine(_turn, w, target, _units[target].Col, _units[target].Row, plan.Path);
            return true;
        }

        if (!InWorkRange(w, user.Col, user.Row, col, row, user))
        {
            Hint("사거리 밖입니다 — 노란 칸을 고르세요 (우클릭·Esc 취소)");
            return true;
        }
        // 대상 방식 3·6(아무 칸)·7(빈 칸)은 메테오처럼 <b>칸을 고르는</b> 기술이라 그 칸에 아무도 없어도 된다.
        bool needsUnit = w.TargetMode is 1 or 4 or 5;
        if (needsUnit && WorkTargets(w, user, col, row).Count == 0)
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

        ReformFollowers(userIndex);        // 군단이면 부하가 대장 진형으로 따라온다
        var dying = new List<UnitState>();
        int[] actions = ActionsFor(w);
        int hitStep = HitStepFor(w, actions.Length);
        bool chained = ScriptFor(w.Id) is { Actions.Length: > 0 };
        bool effectsDone = false, followersDone = false;
        _followerStrikes.Clear();
        SpawnWorkMovies(w, a, col, row, prelude: true);     // 준비 동작의 시전 영상(불기둥 Mov 0041·0042) — 시전이 시작할 때
        // 필살기(준비 7)는 공통 앞머리(빛 알갱이·초상 컷인·금빛 띠, 0x1007e330)를 다 돈 뒤에 핸들러로 간다.
        bool finisher = w.Prepare == 7;
        if (finisher) foreach (bool _ in FinisherPrelude(w, a)) yield return true;
        for (int step = 0; step < Math.Max(actions.Length, 1); step++)
        {
            // 어빌리티 사슬은 <b>타격 동작마다</b> 친다 — 「연」은 레벨이 오르면 13 → 14 → 8 처럼 타격 동작이 늘어나
            // 2·3·4·5·6타가 된다(분석-모션 ba-10). 전에는 사슬의 첫 타격 동작에서만 판정을 내 3타에서 멈췄다.
            bool strikes = step == hitStep || (chained && step < actions.Length && IsStrikeAction(actions[step]));
            if (!strikes)
            {
                if (step >= actions.Length || finisher) continue;   // 필살기의 준비 동작(6·15)은 앞머리가 이미 했다
                PlayAction(a, actions[step]);
                while (a.IsBusy) yield return true;
                continue;
            }

            // 치는 동작은 끝까지 기다리지 않는다 — 동작이 뜨고 0.05초 뒤부터,
            // 그 모션에 든 타격 키 수(동작 13 = 2타, 14 = 3타)만큼 그 간격대로 판정을 낸다(분석-모션 ba-10).
            var hitTimes = new List<double> { 0 };
            if (step < actions.Length)
            {
                PlayAction(a, actions[step]);
                hitTimes = HitTimesFor(a, actions[step], ranged: w.RangeMax > 4);
                for (double end = _lastTime + hitTimes[0]; _lastTime < end;) yield return true;
            }

            // 「연」 사슬 끝의 동작 8 한 대는 연 위력이 아니라 기본공격 위력이다(키 인자 25 = work 1, 분석-모션 ba-10).
            var hitWork = chained && step < actions.Length && actions[step] == 8 && a.Data is { } ad
                          && Work(ad.BasicWorkId) is { } basic ? basic : w;
            if (!effectsDone)
            {
                ScheduleAbilitySounds(w);
                SpawnAbilityEffects(w, a, col, row);
                effectsDone = true;
            }
            // 하이 텔레포트 — 피해 없이 고른 칸 둘레로 순간이동한다(범위는 빗나가는 정도).
            if (IsTeleportWork(w))
            {
                foreach (bool _ in TeleportRoutine(w, a, col, row)) yield return true;
                while (a.IsBusy) yield return true;
                continue;
            }
            // 천지 파열무 — X 자로 땅이 터진 뒤 대상마다 폭발한다. 피해는 그 폭발 때 대상마다 한 번.
            if (w.Id == HeavenEarthWork && targetIndex < 0)
            {
                var targets = WorkTargets(w, a, col, row);
                var struck = new HashSet<int>();
                foreach (bool _ in HeavenEarthRoutine(a, targets, ti => { if (struck.Add(ti)) ApplyWork(a, hitWork, _units[ti], dying); }))
                    yield return true;
                if (!followersDone && targets.Count > 0)
                {
                    FollowersAttack(userIndex, _units[targets[0]], dying, allyPass: false);
                    followersDone = true;
                }
                while (a.IsBusy) yield return true;
                continue;
            }
            // 나인 크루세이더 — 칼이 날아다니며 차례로 꿰뚫는다. 피해는 대상마다 처음 꿰뚫릴 때 준다.
            if (w.Id == NineCrusaderWork && targetIndex < 0)
            {
                var targets = WorkTargets(w, a, col, row);
                var flight = StartNineCrusader(a, targets);
                var struck = new HashSet<int>();
                while (!flight.Done)
                {
                    StepSword(flight);
                    while (flight.Pierced.TryDequeue(out int ti))
                        if (struck.Add(ti)) ApplyWork(a, hitWork, _units[ti], dying);
                    yield return true;
                }
                foreach (int ti in targets.Where(struck.Add)) ApplyWork(a, hitWork, _units[ti], dying);   // 칼이 못 닿은 대상(없어야 한다)
                if (!followersDone && targets.Count > 0)
                {
                    FollowersAttack(userIndex, _units[targets[0]], dying, allyPass: false);
                    followersDone = true;
                }
                while (a.IsBusy) yield return true;
                continue;
            }
            for (int hit = 0; hit < hitTimes.Count; hit++)
            {
                if (hit > 0)
                    for (double end = _lastTime + (hitTimes[hit] - hitTimes[hit - 1]); _lastTime < end;) yield return true;
                var targets = targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row);
                foreach (int ti in targets) ApplyWork(a, hitWork, _units[ti], dying);
                // 군단 행동(상태 15) — 대장이 기술을 쓰면 부하들도 <b>한 번</b> 같은 패스로 제 기술을 쓴다(여러 타를 쳐도 부하는 한 번).
                // 아군 하나를 겨누는 기술(방식 4)이면 아군 패스(회복·보조), 피해 기술이면 적 패스.
                if (!followersDone && targets.Count > 0 && (w.IsDamage || w.TargetMode == 4))
                {
                    FollowersAttack(userIndex, _units[targets[0]], dying, allyPass: w.TargetMode == 4);
                    followersDone = true;
                }
                if (targets.Count == 0 || !_units[targets[0]].Alive) break;
            }
            while (a.IsBusy) yield return true;   // 남은 동작을 마저 재생한다
        }

        // 사거리 밖이던 부하는 먼저 걸어간 뒤 친다(원본 0x1005f1c0: 명령1 이동 → 명령2 기술).
        if (_followerStrikes.Count > 0)
        {
            while (_followerStrikes.Any(s => s.Follower.IsBusy)) yield return true;
            foreach (var (follower, work, target) in _followerStrikes)
            {
                if (!follower.Alive || !target.Alive) continue;
                follower.Facing = FacingToward(follower.Col, follower.Row, target.Col, target.Row);
                PlayAction(follower, 8);
                ApplyWork(follower, work, target, dying);
            }
            _followerStrikes.Clear();
            for (double end = _lastTime + 0.2; _lastTime < end;) yield return true;
        }

        // 이스케이프는 겨눈 빈 칸으로 순간이동한다(분석-모션 ba-10) — 마리아·유블레인이 쓰는,
        // 사라졌다 나타나는 그 기술이다. 그냥 자리만 덮어쓰면 뚝 끊겨 보이니 짧게 사라졌다 나타나게 한다.
        if (w.Id == EscapeWork && LiveUnitAt(col, row) == null)
        {
            const double fadeSeconds = 0.15;
            for (double start = _lastTime, end = start + fadeSeconds; _lastTime < end;)
            {
                a.Fade = Math.Max(0, 1 - (_lastTime - start) / fadeSeconds);
                yield return true;
            }
            a.Fade = 0;
            a.WarpTo(col, row);
            a.OriginCol = col;
            a.OriginRow = row;
            for (double start = _lastTime, end = start + fadeSeconds; _lastTime < end;)
            {
                a.Fade = Math.Min(1, (_lastTime - start) / fadeSeconds);
                yield return true;
            }
            a.Fade = 1;
        }

        if (w.Id is StanceDefendWork or StanceEvadeWork) a.Stance = w.Id == StanceDefendWork ? 1 : 2;
        // 비용은 행동이 끝난 뒤 TP → SOUL → HP 차례로 뺀다(0x10076380). 체질마다 SOUL·TP·HP 로 나뉘는 비율이 다르다.
        if (a.Data is { } cost && _db is { } db2)
        {
            a.Tp -= TpCostFor(a, cost, w.Id);
            a.Soul = Math.Max(0, a.Soul - SoulCostFor(a, cost, w.Id));
            AddSoul(a, w.Kind switch { 0 => 10, 1 => 6, _ => 4 });
            int hp = db2.WorkHpCost(cost, w.Id);
            if (hp > 0) a.Hp = Math.Max(1, a.Hp - hp);
        }

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
        // 판정에는 상태이상까지 얹은 능력치를 쓴다(1 DEX −1 · 40 DEP −1 · 30~32 보정).
        var (amount, result, crit) = _db.Resolve(_rng, EffectiveData(a)!, a.Tp, a.Soul, EffectiveData(t)!, t.Tp, t.Hp, t.MaxHp, w, t.Stance, a.Status(29));

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
        // 상태이상 보정(7·13·14)은 <b>판정 함수 안에서</b> 끝나고, 「Miss」는 그 뒤에 남은 양으로 가른다(0x10078e60).
        amount = AilmentDamage(a, t, amount);
        if (result == 3 || amount <= 0) { ShowNumber(t, _db.T(42) is { Length: > 0 } m ? m : "Miss", MissColor); return; }

        t.LastHitBy = a;                        // 맞았을 때만 적는다(빗나가면 그대로) — 원본 0x10079990
        t.Hp = Math.Max(0, t.Hp - amount);
        // 10(피격 가속) — 맞으면 TP 가 값% 만큼 앞당겨진다(0x1007952c).
        if (t.Status(10) is var rush and > 0) t.Tp = Math.Min(t.MaxTp, t.Tp + rush * t.MaxTp / Math.Max(1, t.Stp) / 100);
        ShowNumber(t, $"{_db.T(159)} {amount}", DamageColor);
        PlayHitReaction(t, damaged: true);
        if (crit) PlayCritFlash();
        AddSoul(t, amount / Math.Max(1, _db.N(43)));
        PlayHurtVoice(t);
        ApplyAilments(a, t, w);
        Counterattack(a, t, amount);
        if (t.Hp > 0) return;
        if (SurvivesFatal(t)) return;
        AddSoul(a, 10);   // 처치(메시지 1016)
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
        // 먼저 이벤트 스크립트 — 「몇 턴 버티기」·「누구를 지키기」처럼 전멸 말고 다른 조건으로 끝나는 전투가 있다.
        RunEvents();
        if (_outcome.Length > 0) return;
        // 아직 안 나온 사람은 세지 않는다 — 안 그러면 증원이 있는 전투가 영영 안 끝난다.
        // 다만 머리 워드 9 가 0 인 전투(엔진이 전멸을 안 봄)에서 적이 아직 들어오기 전이면 전멸이 아니다 —
        // Btl 0137 은 보스·적 전부가 턴 2 이벤트로 들어와, 시작하자마자 「승리」가 떠 버렸다(사용자 제보).
        bool enemiesPending = !_scene.EngineJudgesWipe && _units.Any(u => u.Alive && !u.OnField && !u.IsAlly);
        if (!enemiesPending && !_units.Any(u => u.Alive && u.OnField && !u.IsAlly))
        {
            ScriptedDestinationOnWipe();
            _outcome = "승리 — 적을 모두 쓰러뜨렸습니다"; _outcomeAt = _lastTime; PlayOutcomeMusic(win: true);
        }
        else if (!_units.Any(u => u.Alive && u.OnField && u.IsAlly)) { _outcome = "패배 — 아군이 모두 쓰러졌습니다"; _outcomeAt = _lastTime; PlayOutcomeMusic(win: false); }
    }

    /// <summary>이벤트 행동 11·10·6 이 적는 결과.</summary>
    private void SetEventOutcome(bool win)
    {
        if (_outcome.Length > 0) return;
        _outcome = win ? "승리" : "패배";
        _outcomeAt = _lastTime;
        PlayOutcomeMusic(win);
    }

    // ── 표시 ─────────────────────────────────────────────────────────────────

    private const uint HealColor = 0xFF60E060;

    /// <summary>
    /// 그 동작에서 판정이 나는 시각들(초, 동작 시작 기준) — 모션의 타격 키(종류 6) 간격을 그대로 쓰되
    /// 첫 타는 사용자가 원하는 대로 <b>0.05초</b>에 낸다. 타격 키가 없으면 한 번만.
    /// 다만 <b>원거리</b>(사거리 1칸 넘음)는 모션의 타격 키 그 틱에 낸다 — 총은 쏘는 컷에 타격 키가 있어서, 0.05초로 당기면
    /// 크리스티앙처럼 피해 숫자가 총을 쏘기 전에 떴다(사용자 보고).
    /// </summary>
    private List<double> HitTimesFor(UnitState u, int action, bool ranged = false)
    {
        const double first = 0.05;
        if (!_sprites.TryGetValue(u.ChrCode, out var sprite) || sprite.Clip(action, u.Facing) is not { Hits.Count: > 0 } clip)
            return [first];
        var starts = clip.Hits.Select(h => h.Start).OrderBy(t => t).ToList();
        double at = ranged ? Math.Max(first, starts[0] / TicksPerSecond) : first;
        return [.. starts.Select(t => at + (t - starts[0]) / TicksPerSecond)];
    }

    private void PlayAction(UnitState u, int action)
    {
        double seconds = _sprites.TryGetValue(u.ChrCode, out var sprite) ? sprite.ActionSeconds(action, u.Facing) : 0;
        if (Trace)
        {
            var clip = sprite?.Clip(action, u.Facing);
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"{_lastTime:F2} chr {u.ChrCode} action {action} facing {u.Facing} clip {(clip?.Id.ToString() ?? "none")} len {clip?.Length ?? 0} keys {clip?.Keys.Count ?? 0} seconds {seconds:F2}" + Environment.NewLine);
        }
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
            int cx = t.Col * TileW, cy = CellTop(t.Col, t.Row);
            for (int k = 0; k < 3; k++) StrokeRect(cx + k, cy + k, TileW - 2 * k, TileH - 2 * k, color);
            return;
        }
        if (_targetWork < 0 || _targetIsBasicAttack || _turn < 0 || Work(_targetWork) is not { } w) return;
        var u = _units[_turn];
        // 원본 상태 11(어빌리티 대상 고르기, 분석-UI 「칸 깃발」): 사거리는 층 12 붉은색 (255,100,40)×74/256 = (73,28,11) 을
        // <b>가산</b>으로 칠하고 테두리는 같은 색 불투명 41×33. 겨눈 칸의 효과 범위는 층 0 주황 (255,170,40) → (73,49,11) 로 덧칠한다
        // (그리는 차례는 층 0 이 먼저라 주황이 이긴다).
        var aim = _attackCursor >= 0 ? (_units[_attackCursor].Col, _units[_attackCursor].Row) : _aimCell;
        var splash = aim is { } ac ? AreaCells(w, u, ac.Item1, ac.Item2).ToHashSet() : [];
        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                bool inRange = InWorkRange(w, u.Col, u.Row, col, row, u), inSplash = splash.Contains((col, row));
                if (!inRange && !inSplash) continue;
                PaintCell(col, row, inSplash ? SplashLayer : RangeLayer);
            }
        // 저절로 겨눈 대상(적 커서나 칸)에도 깜빡이는 빨간 테두리
        if (aim is { } a)
        {
            uint alpha = (uint)(160 + 95 * (0.5 + 0.5 * Math.Sin(_lastTime * Math.PI * 4)));
            uint color = alpha << 24 | 0xFF3030;
            int cx = a.Item1 * TileW, cy = CellTop(a.Item1, a.Item2);
            for (int k = 0; k < 3; k++) StrokeRect(cx + k, cy + k, TileW - 2 * k, TileH - 2 * k, color);
        }
    }

    private string TurnLine()
    {
        if (_turn < 0) return $"틱 {_tick} — 차례를 기다리는 중";
        var u = _units[_turn];
        string help = !u.IsAlly ? "적군이 움직입니다"
            : _targetWork >= 0 ? (_targetIsBasicAttack ? "클릭·Enter 공격  Tab 다른 적  Esc 취소" : "노란 칸 클릭  Esc 취소")
            : $"{KeyBindings.KeyName(_keys[KeyAction.MoveUp])}{KeyBindings.KeyName(_keys[KeyAction.MoveLeft])}{KeyBindings.KeyName(_keys[KeyAction.MoveDown])}{KeyBindings.KeyName(_keys[KeyAction.MoveRight])} 걷기  {KeyBindings.KeyName(_keys[KeyAction.Ring])} 링  {KeyBindings.KeyName(_keys[KeyAction.Attack])} 공격  {KeyBindings.KeyName(_keys[KeyAction.Rest])} 휴식  1~4 어빌리티  Esc 취소";
        return $"틱 {_tick}  {(u.IsAlly ? "아군" : "적군")} {UnitName(_turn)}  HP {u.Hp}/{u.MaxHp}  TP {u.Tp}/{u.MaxTp}  SOUL {u.Soul}   {help}";
    }

}
