using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

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
internal sealed unsafe partial class BattleScene(GameWindow host)
{
    /// <summary>지금 차례인 인물 번호. 차례를 기다리는 중이면 −1.</summary>
    internal int _turn = -1;
    /// <summary>전투가 흐른 틱 — 원본은 전투를 만들 때 <b>1</b> 로 놓는다(<c>0x100644d4</c>).</summary>
    internal int _tick = 1;
    internal IEnumerator<bool>? _routine;
    internal string _outcome = "";
    internal double _outcomeAt;
    internal double _nextTickAt;

    /// <summary>지금 도는 루틴이 물체 차례(ObjectTurns)인가 — 끝날 때 자동 회복을 돌리지 않는다.</summary>
    internal bool _objectRoutine;

    /// <summary>그리기에서만 쓰는 난수(깜빡임·줄 찢김) — 판정 난수 <see cref="_rng"/> 를 그리기 틀 수가 흔들지 않게 따로 둔다.</summary>
    internal readonly Random _drawRng = new();
    internal readonly List<(string Text, float Size, int X, int Y, double Start, uint Color)> _popups = [];

    /// <summary>대상을 고르는 중인 work 번호(기본공격 포함). 고르는 중이 아니면 −1.</summary>
    internal int _targetWork = -1;
    internal bool _targetIsBasicAttack;

    /// <summary>공격 대상 커서 — 공격을 열면 칠 수 있는 적 중 HP 가 가장 낮은 적. 없으면 −1.</summary>
    internal int _attackCursor = -1;

    /// <summary>기본공격 work 1 의 동작 — 준비 5 → 베기 8 → 복귀 24(분석-모션 <c>0x1007f0a0</c>). 판정은 베기 끝에 들어간다.</summary>
    internal static readonly int[] StrikeActions = [5, 8, 24];

    /// <summary>
    /// 기본공격 work 마다 도는 동작이 다르다(분석-모션 「인물별 동작 구간표는 없다」).
    /// 288명은 work 1 로 베고, 48명은 387(때리는 동작이 없다), 47명은 1479, 48명은 6·1584 로 <b>쏜다</b>(동작 9).
    /// </summary>
    internal static readonly Dictionary<int, int[]> BasicWorkActions = new()
    {
        [1] = [5, 8, 24],
        [387] = [5, 7],
        [1479] = [8],
        [6] = [9],
        [1584] = [9],
    };
    internal const int StrikeHitStep = 1;

    /// <summary>자세를 세우는 work — 516 방어(맞을 때 한 번 더 깎임), 515 회피(상대 명중 −DEX/5).</summary>
    internal const int StanceDefendWork = 516, StanceEvadeWork = 515;
    internal const double TickDelaySeconds = 0.2;   // 행동 끝 → 다음 GETNEXT 까지 상태 20·18·19·21·4 를 지나 약 6걸음(ba-20 W)

    /// <summary>그 인물을 내가 직접 움직이나 — 편 4 는 늘, 편 3(동맹)은 「모드 &gt; 동맹을 AI 가 움직임」을 껐을 때.</summary>
    internal bool IsMine(UnitState u) => !AutoPlay && (u.PlayerControlled || (u.IsAlly && !host._allyAi));

    /// <summary>DUELDX_AUTOPLAY=1 이면 내 부대도 AI 가 움직인다 — 화면 밖 시험에서 전투를 저절로 돌리려고.</summary>
    internal static readonly bool AutoPlay = Environment.GetEnvironmentVariable("DUELDX_AUTOPLAY") == "1";

    /// <summary>DUELDX_TRACE=1 이면 동작 재생을 <c>%TEMP%\dueldx_trace.log</c> 에 적는다(화면 밖 시험용).</summary>
    internal static readonly bool Trace = Environment.GetEnvironmentVariable("DUELDX_TRACE") == "1";

    internal bool IsPlayerTurn => _turn >= 0 && IsMine(host._units[_turn]) && _routine == null && _outcome.Length == 0;


    /// <summary>지금 판의 아군 상태를 파티에 담아 둔다(다음 전투로 이어진다).</summary>
    internal void RememberParty()
    {
        foreach (var unit in host._units)
            if (unit.IsAlly && unit.LeaderIndex < 0 && !unit.Detached && !unit.WasFollower && unit.Data is { } c) host._party[unit.ChrCode] = c;   // 군단 부하는 파티원이 아니다
    }

    /// <summary>게임 표를 다 읽은 뒤 인물마다 전투 수치를 채운다.</summary>
    internal void InitBattle()
    {
        if (host._db == null) return;
        // 적 레벨의 기준이 되는 파티 레벨은 <b>유닛을 채우기 전에</b> 한 번 셈한다 —
        // 채우는 도중에 세면 아직 안 채워진 아군 때문에 순서에 따라 값이 흔들린다.
        int partyLevel = PartyLevel();
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"party level {partyLevel} — members [{string.Join(",", host.Mos._members.Select(m => $"{m}:{host._party.GetValueOrDefault(m)?.Level}"))}] followers {host._units.Count(u => u.LeaderIndex >= 0)}" + Environment.NewLine);
        foreach (var unit in host._units)
        {
            if (host._db.Character(unit.ChrCode) is not { } c) continue;
            // 데모라 쌓인 경험치는 지금 레벨에 맞춰 시작한다(저장 파일이 없다).
            // DUELDX_CUMEXP 로 아군 시작값을 바꿀 수 있다 — 레벨업 창을 시험할 때 쓴다(예: 190 이면 한 번만 쓰러뜨려도 오름).
            int startCum = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_CUMEXP"), out int v) ? v : c.Level * 100;
            // 앞 전투에서 얻은 레벨·경험치·장비는 다음 전투로 이어진다(_party 가 들고 있다).
            // DUELDX_CUMEXP 를 주면 레벨도 그 값에 맞춘다 — 쌓인 경험치와 레벨은 늘 짝이 맞아야 한다(레벨 = 쌓인 경험치 ÷ 100).
            // 부하는 명부(_party)에 같은 Chr 가 있어도(옛 세이브가 넣어 둔 절반 레벨 기록) 늘 제 레코드에서 새로 키운다.
            bool follower = unit.LeaderIndex >= 0;
            unit.Data = !follower && host._party.TryGetValue(unit.ChrCode, out var carried) ? carried
                      : unit.IsAlly ? c with { Exp = StatusScreen.DemoExp, CumExp = startCum, Level = (ushort)Math.Max(c.Level, startCum / 100),
                                               // DUELDX_JOB=<직업> 이면 아군 직업을 바꾼다 — 전직 화면(2단계·3단계 단추)을 시험할 때 쓴다.
                                               JobId = ushort.TryParse(Environment.GetEnvironmentVariable("DUELDX_JOB"), out ushort job) ? job : c.JobId }
                      : c with { CumExp = c.Level * 100 };
            // 파티 레벨에 맞춰 자란다 — 면제 명단(0002.nch)에 없는 인물은 <b>편과 상관없이</b>(0x100633ff — 편 3 손님 제이슨도 자란다).
            // 파티 객체에서 온 인물(_party)은 제 레벨을 그대로 쓴다.
            if (!unit.IsAlly || follower || !host._party.ContainsKey(unit.ChrCode)) unit.Data = GrowToPartyLevel(unit.Data ?? c, unit.LevelOffset, partyLevel);
            // 군단 부하는 능력치는 제 레벨 줄로 키우되 <b>적히는 레벨은 절반</b>이다(0x1007a916~0x1007a948 — max(1, L/2), EXP 는 그 ×100).
            // 처치 EXP(레벨 차)·상태이상 레벨 조건·정보 창이 이 값을 읽는다(ba-20 M1). 전에는 부하를 잡으면 EXP 가 과했다.
            if (unit.LeaderIndex >= 0 && unit.Data is { } fd)
            {
                int half = Math.Max(1, fd.Level / 2);
                unit.Data = fd with { Level = (ushort)half, CumExp = half * 100 };
                unit.WasFollower = true;
            }

            // 최대치는 <b>이어받은 인물</b>로 셈한다 — 앞 전투에서 레벨이 올랐으면 그 값이 따라와야 한다.
            var data = unit.Data ?? c;
            unit.MaxHp = unit.Hp = ScaleMaxHp(unit, Math.Max(1, host._db.MaxHp(data)));
            unit.MaxTp = host._db.MaxTp(data);
            // 원본은 유닛을 만들 때 <b>TP 를 0 으로 민다</b>(0x10071941) — 아무도 다시 안 채운다.
            // 그래서 첫 차례는 「최대TP ÷ STP」가 가장 작은 인물이 가져간다.
            unit.Tp = 0;
            unit.Stp = Math.Max(0, host._db.Stp(data));   // 제수 0 이면 TP 가 영영 안 찬다(0x10071db0) — 호위 대상(필그림·피맨·인질·리온)은 차례를 안 받는다(ba-20 K2)
            unit.MaxSoul = host._db.MaxSoul(data);
            // DUELDX_SOUL 로 시작 SOUL 을 올릴 수 있다 — 어빌리티·상태이상을 시험할 때 쓴다.
            unit.Soul = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_SOUL"), out int soul) ? Math.Min(unit.MaxSoul, soul) : host._db.SoulStart;
            // 모드 > 전투 시작 시 소울 가득 — 내 편(편 4)만 SOUL 을 최대로 채우고 시작한다(원본에 없는 편의 기능, 사용자 요청).
            if (host._fullSoulAtStart && unit.PlayerControlled) unit.Soul = unit.MaxSoul;
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
        // 군단 부하는 LP 게터가 늘 대장 세력 × For.dat 보정을 더한다(0x1007ac70) — 위에서는 보정 없이 셌으므로 다시 세고 가득 채운다.
        // 전에는 레벨업·대장 교체 때만 붙어 가이아 버그즈 부하(LP +20)가 보정 없이 시작했다.
        foreach (var unit in host._units)
            if (unit.LeaderIndex >= 0 && unit.Data != null) { host.StatusScr.RefreshUnitStats(unit); unit.Hp = unit.MaxHp; }
        // 챕터 스크립트가 가방을 채웠으면 데모용 아이템은 안 넣는다 — 자료가 준 것이 옳다.
        if (host.Fld._chapterFired.Count == 0) host.StatusScr.FillDemoInventory();
    }

    internal void UpdateTurn()
    {
        if (host._loading || host._db == null) return;
        StepDelayedHits();                       // 포탑 사격처럼 나중에 맞는 타격
        if (_outcome.Length > 0)
        {
            // 조용한 결과(행동 10·6·11[1])는 클릭을 기다리지 않고 제때 넘어간다(원본 120틱).
            if (_outcomeQuiet && host._lastTime >= _outcomeLeaveAt && !host.Mos._mosesOpen && !host.FieldOpen && !host.EpisodesScr._episodesOpen) host.Btl.LeaveFinishedBattle();
            return;
        }
        if (_deployOpen) { StepDeploy(); return; }     // 캐릭터 배치 중에는 틱·차례·이벤트가 멈춘다(원본 상태 2)
        // 이벤트(대사)가 도는 동안은 틱도 차례도 안 흐른다(0x10066197).
        if (EventsBusy) return;
        RunEvents();                 // 틱이 안 흐르는 사이에도 조건(턴 수 따위)은 본다
        if (EventsBusy) return;
        // 자동 저장(슬롯 20) — 그 틱 이벤트 검사가 끝나고 이벤트가 안 돌 때, 아직 그 내 차례의 조종 상태면(상태 22 첫 진입, 감사5 S6).
        if (_autoSaveFor >= 0)
        {
            if (_autoSaveFor != _turn) _autoSaveFor = -1;          // 그새 차례가 넘어갔다 — 원본도 깃발을 저장 없이 지운다
            else if (IsPlayerTurn && !host._units[_turn].IsBusy) { _autoSaveFor = -1; host.SlotsScr.AutoSave(); }
        }
        // DUELDX_WIN=1 이면 시작하자마자 이긴 것으로 친다 — 전투 이어짐·진행 깃발·모세스 전환을 화면 밖에서 시험할 때 쓴다.
        if (Environment.GetEnvironmentVariable("DUELDX_WIN") == "1")
        {
            foreach (var u in host._units.Where(u => !u.IsAlly)) u.Alive = false;
            CheckOutcome();
            return;
        }
        if (UpdateLevelUp()) return;   // 레벨업 창이 떠 있는 동안은 차례가 멈춘다

        if (_routine != null)
        {
            if (!_routine.MoveNext())
            {
                // 행동이 끝났다 — 갈래 2 검사. 휴식·물체 만지기처럼 UseWorkRoutine 을 안 거치는 행동도 상태 21 을 지난다(0x100681a0 → 갈래 1).
                _routine = null;
                _eventCheckDue |= (1 << 2) | (1 << 1);
                // 물체 차례(ObjectTurns)는 유닛의 행동 끝이 아니다 — 쏜 틱에만 자동 회복이 한 번 더 돌지 않게.
                if (_objectRoutine) _objectRoutine = false;
                else AutoHealAll();   // 8(자동 회복)도 행동 끝마다 전원에게(ba-20 C1)
                QueueLevelUps();
            }
            return;
        }

        if (_turn >= 0)
        {
            var u = host._units[_turn];
            if (!u.Alive || !u.OnField) EndTurn();   // 사건이 지금 차례 유닛을 판에서 빼면 차례를 버린다(0x100661b2, ba-20 V10)
            else if (IsMine(u) && !u.IsBusy && u.Tp <= 0 && !_abilityMenu && !_itemMenu && _targetWork < 0) Rest(_turn);
            return;
        }
        if (StepAilmentTicks()) return;          // 매 턴 피해 — 유닛마다 카메라가 선 뒤(감사4 C17)

        // 불러온 판은 저장했던 인물의 차례로 곧장 돌아간다 — 원본도 판을 읽은 뒤 Active 유닛(+0x4cf4)의 상태 22 로 선다
        // (0x100619c0, 분석-시스템메뉴 2.4b). 틱을 흘려 다시 고르면 같은 틱에 TP 가 찬 앞 번호 적이 먼저 움직였다.
        if (_resumeTurn >= 0)
        {
            int r = _resumeTurn;
            _resumeTurn = -1;
            if (r < host._units.Length && host._units[r] is { Alive: true, OnField: true, HasTurn: true } ru && ru.LeaderIndex < 0 && CanTakeTurn(ru))
            {
                StartTurn(r, resume: true);
                return;
            }
        }

        if (host._lastTime < _nextTickAt) return;
        for (int guard = 0; guard < 10000; guard++)
        {
            int next = Array.FindIndex(host._units, u => u.Alive && u.OnField && u.HasTurn && u.LeaderIndex < 0 && CanTakeTurn(u));
            if (next >= 0) { StartTurn(next); return; }
            // 물체는 그 틱의 유닛 차례가 <b>모두 끝난 뒤</b>에야 움직인다 — 유닛 고르기(0x1006db20)가 −1 일 때만 물체 고르기(0x1006dbf0)로 간다
            // (GETNEXT 0x10067d0c, ba-15 Q6). 전에는 틱을 올리자마자 유닛보다 먼저 쐈다.
            if (_objectsDue)
            {
                _objectsDue = false;
                // 쏠 상대가 있는 물체가 있으면 하나씩 카메라·모션을 돌며 쏜다(ObjectTurns). 없으면 전처럼 그 틀에 끝낸다(충전·빈 차례).
                if (Objects.Any(obj => obj.Alive && !_opened.Contains(obj) && obj.Data.Acts && obj.Data.Kind != 10 && !(obj.Data.Kind == 9 && obj.Team < 0)
                                       && ObjectInBounds(obj) && _tick % Math.Max(1, obj.Data.TurnEvery) == 0 && ObjectHasTarget(obj)))
                {
                    _routine = ObjectTurns();
                    _objectRoutine = true;
                    return;
                }
                StepObjects();
                SweepTickDeaths();
                if (_outcome.Length > 0 || EventsBusy) return;
                continue;
            }
            AdvanceTick();
            for (int g = 0; g < 8 && _events.Count > 0 && _eventCheckDue != 0 && !EventsBusy && _outcome.Length == 0; g++) RunEvents();
            if (_outcome.Length > 0 || EventsBusy) return;   // 틱 사건이 다 돈 뒤에 유닛을 고른다(GETNEXT 0x10067d36~0x10067d64, ba-20 V3)
            if (_ailmentTickQueue.Count > 0) return;   // 매 턴 피해를 먼저 — 다음 틀부터 StepAilmentTicks 가 한 명씩
            // 원본은 논리 한 걸음(33ms)에 전투 틱을 많아야 하나 올린다 — 전투 걸음 0x10066070 은 상태 핸들러를 한 번만 부르고,
            // GETNEXT(0x10067cdc)는 유닛이 없으면 틱++ 뒤 그냥 돌아간다(ba-20 W). 빈 틱이 초당 30개로 눈에 보이게 흐른다.
            // 전에는 한 틀 안에서 차례가 나올 때까지 몰아 올려 차례 사이 쉼((최대TP−TP)/STP 틱)이 없었다.
            _nextTickAt = Math.Max(_nextTickAt, host._lastTime - 1.0 / TicksPerSecond) + 1.0 / TicksPerSecond;   // 누적 — 틀 시간에 물려 처지지 않게
            return;
        }
    }

    /// <summary>
    /// 아군 레벨 상위 셋의 평균 — 원본이 적 레벨을 맞추는 기준(분석-전투 「Lev.dat 성장」).
    /// </summary>
    internal int PartyLevel()
    {
        // 원본은 플레이어 <b>부대원 명부</b>의 상위 셋이다(0x1004e070) — 명부가 있으면 그것으로(편 3 손님은 안 센다).
        if (host.Mos._members.Count > 0)
        {
            var roster = host.Mos._members.Select(chr => (int)(host._party.GetValueOrDefault(chr)?.Level ?? host._db?.Character(chr)?.Level ?? 0))
                                 .Where(v => v > 0).OrderByDescending(v => v).Take(3).ToList();
            if (roster.Count > 0) return roster.Sum() / roster.Count;
        }
        var levels = host._units.Where(u => u.IsAlly)
                           .Select(u => host._party.TryGetValue(u.ChrCode, out var carried) ? carried.Level
                                      : host._db?.Character(u.ChrCode)?.Level ?? 0)
                           .Select(v => (int)v)
                           .Where(v => v > 0)
                           .OrderByDescending(v => v).Take(3).ToList();
        return levels.Count == 0 ? 1 : levels.Sum() / levels.Count;
    }

    /// <summary>
    /// 지금 파티 부대원 레벨 상위 셋의 평균(<c>0x1004e070</c>) — 전투 밖(챕터·필드 스크립트)에서 쓴다.
    /// <see cref="PartyLevel"/> 은 전장의 아군을 세므로, 모세스에서 돌면 지난 전투의 판이나 빈 판을 센다.
    /// </summary>
    internal int RosterLevel()
    {
        var levels = host.Mos._members.Select(chr => (int)((host._units.FirstOrDefault(u => u.ChrCode == chr)?.Data ?? host._party.GetValueOrDefault(chr))?.Level ?? 0))
                             .Where(v => v > 0).OrderByDescending(v => v).Take(3).ToList();
        return levels.Count == 0 ? PartyLevel() : levels.Sum() / levels.Count;
    }

    /// <summary>
    /// 그 인물을 <paramref name="offset"/> + 파티 레벨로 키운다 — <b>늘 <c>.chr</c> 원본에서</b> 다시 셈하므로 쌓이지 않고,
    /// <b>TP 제수와 CTP 는 그대로</b> 둔다. 면제 명단에 있으면 그대로 돌려준다.
    /// </summary>
    /// <param name="ignoreExempt">
    /// 스크립트 805(레벨 맞추기, <c>0x10031a50</c>)는 면제 명단을 안 본다 — 면제 명단은 전투 유닛을 만들 때(<c>0x1007a8e0</c>)만 쓴다.
    /// 805 가 겨누는 인물은 <b>모두</b> 면제 명단에 있는 합류 인물이라, 명단을 보면 805 가 한 번도 안 먹어 리엔·유진이 Lv1 로 합류했다(사용자 보고).
    /// </param>
    internal CharacterData GrowToPartyLevel(CharacterData c, int offset, int partyLevel, bool ignoreExempt = false)
    {
        if (host._db is null || (!ignoreExempt && host._db.LevelExempt.Contains(c.Code))) return c;
        var rows = host._db.LevelGrowth;
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

    /// <summary>이번 틱에 물체 깃발이 섰나(0x1006dab0) — 유닛 차례가 다 끝나면 <see cref="StepObjects"/> 가 돈다(Q6).</summary>
    internal bool _objectsDue;

    internal void AdvanceTick()
    {
        _tick++;
        var turnStarts = new HashSet<UnitState>();
        // 틱 처리 차례(0x10067d36~): 틱++ → 스크립트 타이머 → 전 유닛 vt+0xb0(0x10071f10: TP 회복 → 6·5 풀림 굴림) → 유닛 깃발(0x1006da40)
        // → 물체 깃발(0x1006dab0) → 갈래 3 검사 → 상태 16(매 턴 피해). 풀림 굴림은 깃발 <b>앞</b>이다(ba-15 Q4·Q6).
        // 켜 둔 이벤트 타이머는 틱마다 하나씩 센다(0x10067d3c — 틱을 올린 바로 다음 줄). 갈래 3 이벤트 검사도 이때다.
        for (int i = 0; i < _eventTimer.Length; i++) if (_eventTimerRun[i]) _eventTimer[i]++;
        foreach (var u in host._units.Where(u => u.Alive))
        {
            // 원본(0x10071db0)은 STP 와 턴 속도(38)를 <b>먼저 다 더하고</b> 한 번만 자른 뒤 차례 깃발을 세운다 —
            // 나중에 더하면 38 이 양수일 때 차례가 한 틱 늦는다. 아래쪽 0 자르기는 원본에 없다. 마비·빙결 중에도 TP 는 찬다.
            u.Tp += u.Stp;
            if (u.HasStatus(38)) u.Tp += u.Status(38);
            if (u.Tp > u.MaxTp) u.Tp = u.MaxTp;
            ReleaseFreeze(u);
        }
        foreach (var u in host._units.Where(u => u.Alive))
        {
            // 깃발은 <b>차례를 받을 수 있을 때만</b> 선다(0x1006e940(u,4) = 0x1007c480: 마비·빙결이면 거짓) — TP 가 정확히 최대일 때(0x100743f2 sete).
            // 전에는 마비 중에도 깃발을 세워(고르기에서만 뺌) 매 턴 피해를 맞고, 자세가 영영 안 풀리기도 했다. 이미 선 깃발은 그대로.
            if (u.HasTurn || u.Tp < u.MaxTp || !CanTakeTurn(u)) continue;
            u.HasTurn = true;
            turnStarts.Add(u);
            // 방어·회피 자세는 깃발이 서는 <b>그 틱</b>에 풀린다(0x100743b0 → 0x10072d90, +0x4d4 = 0).
            u.Stance = 0;
        }
        _objectsDue = Objects.Count > 0;
        _eventCheckDue |= 1 << 3;
        // 매 턴 피해(상태 16 → 17 PRETURN 0x1006a791)는 걸린 유닛마다 카메라를 가운데로 보내고 멈춘 뒤에 든다(감사4 C17) —
        // 줄을 세워 두고 UpdateTurn 이 한 명씩 처리한다. 걸린 사람이 없으면 곧바로 쓰러짐을 본다.
        foreach (var u in host._units)
            if (u.Alive && turnStarts.Contains(u) && (u.Status(2) + u.Status(3) + u.Status(17) > 0 || u.Status(16) > 0 || u.Status(15) > 0))
                _ailmentTickQueue.Enqueue(u);
        if (_ailmentTickQueue.Count == 0) SweepTickDeaths();
        else _ailmentSweepDue = true;
    }

    /// <summary>매 턴 피해를 받을 차례인 유닛 — 카메라가 그 유닛에게 선 뒤 하나씩 든다.</summary>
    internal readonly Queue<UnitState> _ailmentTickQueue = new();

    /// <summary>줄 머리 유닛에게 카메라 명령을 걸었나 · 줄이 다 빠지면 쓰러짐을 봐야 하나.</summary>
    internal bool _ailmentCamSent, _ailmentSweepDue;

    /// <summary>매 턴 피해 줄을 한 걸음 — 처리 중이면 true(그동안 틱·차례는 멈춘다).</summary>
    internal bool StepAilmentTicks()
    {
        while (_ailmentTickQueue.Count > 0)
        {
            var u = _ailmentTickQueue.Peek();
            // 판이 바뀌었거나(RESTART·다른 전투) 그새 쓰러진 사람은 건너뛴다.
            if (!u.Alive || !u.OnField || Array.IndexOf(host._units, u) < 0) { _ailmentTickQueue.Dequeue(); _ailmentCamSent = false; continue; }
            if (!_ailmentCamSent) { _ailmentCamSent = true; CenterOnUnit(u); return true; }
            if (CameraBusy) return true;
            _ailmentTickQueue.Dequeue();
            _ailmentCamSent = false;
            TickAilments(new HashSet<UnitState> { u });   // 멈추면 0x2719(0x1007a360)
            return true;
        }
        if (!_ailmentSweepDue) return false;
        _ailmentSweepDue = false;
        SweepTickDeaths();
        return true;
    }

    /// <summary>
    /// 틱 처리(<c>vt+0xb0</c> = <c>0x10071f10</c>) 끝의 풀림 굴림 — <b>상태 6 먼저, 그다음 5</b>를 각각 <c>rand()%100 &lt; 3</c> 이면 지운다
    /// (<c>0x10071f19~0x10071f70</c>, <c>0x1007c470</c>). TP 회복 뒤, 차례 깃발 앞이라 풀린 그 틱에 TP 가 가득이면 곧바로 깃발이 선다.
    /// </summary>
    internal void ReleaseFreeze(UnitState u)
    {
        foreach (int id in (ReadOnlySpan<int>)[6, 5])
        {
            if (!u.HasStatus(id) || _ailmentRandom.Next(100) >= 3) continue;
            for (int i = 0; i < 3; i++)
                if (u.StatusId[i] == id) (u.StatusId[i], u.StatusValue[i], u.StatusSource[i]) = (0, 0, null);
        }
    }

    /// <summary>틱·물체 차례 뒤 — HP 0 이나 22·23·24(SOUL·TP 가 조건에 닿으면, 0x1007c689~)로 쓰러진 인물을 치우고 승패를 본다.</summary>
    internal void SweepTickDeaths()
    {
        foreach (var u in host._units.Where(u => u.Alive && u.OnField && (u.Hp <= 0 || DiesByStatus(u))))   // 판 밖(201 로 물러난 HP 0 보스)은 안 죽는다
            if (!SurvivesFatal(u))
            {
                CenterOnUnit(u);                  // 쓰러지는 유닛마다 가운데 — 마지막이 이긴다(0x1006a8b1, 감사4 C17)
                GainAilmentKillExp(u);
                KillUnit(u);
            }
        CheckOutcome();
    }

    /// <summary>차례 시작 — 그 인물을 고른다(fg-8). 적이면 AI 를 돌린다(fg-7).</summary>
    /// <summary>불러온 뒤 이어 받을 차례 — 저장할 때 차례였던 인물. 없으면 −1.</summary>
    internal int _resumeTurn = -1;

    /// <summary>자동 저장을 걸어 둔 내 차례(원본 깃발 +0x4cd8) — 이벤트 검사 뒤 UpdateTurn 이 적는다. 없으면 −1(감사5 S6).</summary>
    internal int _autoSaveFor = -1;

    /// <param name="resume">
    /// 불러온 판의 차례를 이어 받는 것 — 턴 수만 맞추고(저장 때 하나 빼 둔 것), 이벤트 타이머·자동 회복은 이미 그 차례에 돌았으니 다시 안 돌린다.
    /// 원본도 판을 읽으면 차례 시작 처리 없이 상태 22 로 선다(0x100619c0).
    /// </param>
    internal void StartTurn(int index, bool resume = false)
    {
        _turn = index;
        host._selected = index;
        _turnNo++;
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"turn tick {_tick} unit {index} chr {host._units[index].ChrCode} side {host._units[index].Side} TP {host._units[index].Tp}/{host._units[index].MaxTp}" + Environment.NewLine);
        if (!resume)
        {
            // 이벤트 타이머·자세 풀기는 틱에서 한다(AdvanceTick) — 원본 [+0x4cf0] 은 빈 틱마다 오르는 시간 틱이다(0x10067d36, fg-22).
            // 기준 칸은 새 차례에만 — 이어 받는 차례는 세이브의 기준 칸(+0x4b8/+0x4ba)을 그대로 둔다. 전에는 여기서 지금 자리로 덮어
            // 걸은 뒤 저장·불러오기면 걸음 비용이 사라졌다(감사5 S2).
            host._units[index].OriginCol = host._units[index].Col;
            host._units[index].OriginRow = host._units[index].Row;
        }
        // 불러온 판의 카메라(감사5 S7) — 시작 카메라·페이드인 뒤, 되살린 차례가 서는 이 자리에서 놓는다. 카메라가 없는 옛 세이브는
        // 되살린 내 유닛을 한 번 가운데로(AI 차례는 아래 CameraThen 이 옮긴다).
        if (host.Sys._loadCameraPending)
        {
            host.Sys._loadCameraPending = false;
            if (host.Sys._loadCamera is { } cam) host.Sys.ApplySavedCamera(cam);
            else if (resume && IsMine(host._units[index])) CenterOnUnit(host._units[index]);
        }
        CancelTargeting();
        host.Btl._heldMoveKeys.Clear();
        // 편 4 만 내가 움직인다. 편 3(동맹 AI)과 적은 같은 AI 로 스스로 움직인다(ba-6·ba-11).
        // 버서커(상태 4)라도 조종권은 그대로다 — 원본은 편 판정(0x1006fde0)에서 「모두가 적」이 될 뿐, +0x78·+0x4e9·+0x4ec 를 안 건드리고
        // WAITNEXT 도 +0x78==4 만 본다(ba-14 A4 확정). 전에는 사용자 보고를 근거로 AI 에게 넘겼다.
        // 「누구 차례」 알림은 안 띄운다(사용자 요청) — 머리줄에 이미 나오고, 무엇보다 <b>같은 알림 칸</b>이라
        // 상자에서 얻은 것 같은 결과 알림을 곧바로 덮어써 못 읽게 했다. 차례는 부르는 목소리로 알린다 — AI·동맹 차례도(상태 8).
        PlayTurnCall(host._units[index]);
        if (IsMine(host._units[index]))
        {
            // 자동 저장(슬롯 20) — 원본은 전투 시작·새 차례마다 깃발(+0x4cd8)을 세우고, 플레이어가 유닛을 고르는
            // 상태 22 에 처음 들어설 때 SaveGame(20) 한 뒤 지운다(0x1006acc0). AI 차례는 상태 10~12 가 카메라만 옮기고
            // 깃발을 저장 없이 지우므로 저장이 없다. 곧 「내 차례가 시작될 때마다 한 번」이다(분석-시스템메뉴 2.4).
            // 원본 순서는 틱++ → … → 갈래 3 이벤트 검사 → 상태 16 → 상태 22 첫 진입에서 SaveGame(20) 이라, 그 틱 이벤트를 다 본 뒤에 적힌다.
            // 여기서 곧장 적으면 이벤트 검사(다음 틀 RunEvents)보다 앞서므로 깃발만 세우고 UpdateTurn 이 이벤트 뒤에 적는다(감사5 S6).
            _autoSaveFor = index;
            // 내 차례가 오면 카메라가 그 인물을 가운데로 보낸다(사용자 요청 — 원본 상태 22 는 안 옮긴다). 판 끝이면 갈 수 있는 데까지만 간다.
            // 불러온 직후 세이브의 카메라를 되살린 차례는 그대로 둔다.
            if (!resume) CenterOnUnit(host._units[index]);
        }
        else
        {
            if (host._units[index].IsAlly)
                host.Toast(host._units[index].HasStatus(4) ? $"{host.UnitName(index)} 이(가) 버서커 상태라 스스로 움직입니다" : $"{host.UnitName(index)} 차례 — 동맹이 스스로 움직입니다");
            // AI 는 행동 전에 카메라가 그 인물에게 가서 선다(상태 14 CHRWORK 0x10069df0, 감사4 C4). 내 차례(상태 22)는 안 옮긴다(C3).
            _routine = CameraThen(host._units[index], AiRoutine(index));
        }
    }

    /// <summary>맞는 목소리의 재생 표지 바탕 — 표지 = 바탕 + 소리 번호. 「그 소리가 어디서든 울리는 중인가」를 번호로 본다.</summary>
    internal const int HurtVoiceTag = 30000;

    /// <summary>
    /// 맞았을 때 나는 목소리(<c>0x10079c0f</c>, 맞음 종류 2 · 피해 &gt; 0) — Dmg.dat 묶음의 맞는 소리 둘 가운데 <b>어느 것도</b> 울리는 중이 아닐 때만
    /// (<c>0x10028810</c> = 번호로 32칸 전역 슬롯 검사, 누가 낸 것이든) 유닛 <c>+0x4c &amp; 1</c> 로 고른 하나를 낸다.
    /// <c>+0x4c</c> 의 뜻은 못 가렸다(가설) — 유닛마다 늘 같은 쪽이 나게 자리 번호의 홀짝으로 대신한다. 전에는 유닛별 표지로만 막고 난수로 골랐다.
    /// </summary>
    internal void PlayHurtCry(UnitState u)
    {
        if (u.Data == null || host._voices.GetValueOrDefault(u.Data.VoiceSet).Hurt is not { Length: > 0 } hurt) return;
        if (hurt.Any(id => host._mixer.IsPlaying(HurtVoiceTag + id))) return;
        int id = hurt[((int)(host._lastTime * TicksPerSecond) & 1) % hurt.Length];   // 전투 프레임 카운터로 고른다(0x10071e83 → 0x100eaac0, ba-21 sound D8) — 전에는 인물마다 늘 같은 쪽이었다
        host.Play(id, HurtVoiceTag + id, host.SoundScreenX(host.Btl.UnitFoot(u).X));   // 비명은 그 유닛 화면 자리에서(0x10079c68~0x10079c9a, ba-20 Q S-3)
    }

    /// <summary>
    /// 차례가 왔을 때 부르는 목소리 — 원본은 AI 차례(상태 8, <c>0x10068600~0x10068698</c>)와 플레이어 고르기 상태(<c>0x100693a7~0x10069461</c>) 두 곳에서,
    /// <b>TP 가 최대일 때만</b>(<c>0x1007aef0 == 0x1007aeb0</c>) <c>call[frame &amp; 3]</c> 을 낸다. 플레이어 쪽은 <b>원래 칸에 있을 때만</b>
    /// (<c>+0x4b8/+0x4ba == +0x44/+0x46</c>) — 차례를 막 받은 지금은 늘 원래 칸이다. 전에는 내 편만, 조건 없이 냈다(ba-15 목소리).
    /// </summary>
    internal void PlayTurnCall(UnitState u)
    {
        if (u.Data == null || u.Tp != u.MaxTp || (IsMine(u) && (u.Col != u.OriginCol || u.Row != u.OriginRow))) return;
        if (host._voices.GetValueOrDefault(u.Data.VoiceSet).Call is { Length: > 0 } call)
        {
            // 차례를 부르는 목소리는 <b>가운데에서 제 크기로</b> 낸다(사용자 보고 — 「작게 나온다」). 원본은 유닛 자리에서 내는데(0x1006866b)
            // 그 식(SetPan((x − 320) × 7))으로는 화면 가장자리의 인물이면 반대쪽이 −20 dB 로 줄어 한쪽에서만 작게 들리고,
            // 부르는 순간에 카메라가 아직 그 인물에게 안 가 있으면 화면 밖 소리로 쳐져 −15 dB 까지 더 줄었다.
            host.Play(call[(_tick & 3) % call.Length], 1000 + Array.IndexOf(host._units, u));
        }
    }

    internal void EndTurn()
    {
        _eventCheckDue |= 1 << 2;                // 갈래 2 = 행동 끝(0x100680a3)
        if (_turn >= 0) host._units[_turn].HasTurn = false;
        _turn = -1;
        _ringUnit = -1;
        _skillLeader = -1;
        CancelTargeting();
        _nextTickAt = host._lastTime + TickDelaySeconds;
    }

    /// <summary>공격·어빌리티를 열 때 걸음 비용을 빼기 전 값 — 취소하면 되돌린다.</summary>
    internal (int Tp, int OriginCol, int OriginRow)? _commitUndo;

    /// <summary>공격 대상 고르기·어빌리티 목록을 닫는다. <paramref name="refund"/> 면 열 때 뺀 걸음 비용을 돌려준다.</summary>
    internal void CancelTargeting(bool refund = false)
    {
        if (refund && _commitUndo is { } undo && _turn >= 0)
        {
            var u = host._units[_turn];
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
    internal void CommitMoveForAction()
    {
        var u = host._units[_turn];
        _commitUndo = (u.Tp, u.OriginCol, u.OriginRow);
        CommitMove(u);
    }

    /// <summary>Esc 로 걸은 것을 물린다 — 차례 시작 자리로 되돌린다. 물렸으면 true.</summary>
    internal bool UndoMove()
    {
        if (!IsPlayerTurn) return false;
        var u = host._units[_turn];
        if (u.IsBusy || (u.Col == u.OriginCol && u.Row == u.OriginRow)) return false;
        u.WarpTo(u.OriginCol, u.OriginRow);
        return true;
    }

    /// <summary>
    /// 취소 — 목록·대상 고르기면 비용을 돌려주고 링을 다시 연다. <paramref name="undoMove"/> 면(Esc) 그다음 걸음도 물린다.
    /// 우클릭은 걸음을 물리지 않는다. 취소한 것이 있으면 true.
    /// </summary>
    internal bool CancelStep(bool undoMove)
    {
        // 아이템 목록도 우클릭·Esc 로 닫힌다(목록 창 0x100d3830 — 우클릭 = 취소, ba-20 G1). 전에는 Esc 가 시스템 메뉴를 열었다.
        if (_itemMenu) { _itemMenu = false; CancelTargeting(refund: true); if (IsPlayerTurn) OpenRing(_turn, reopen: true); return true; }
        if (_abilityMenu || _targetWork >= 0) { CancelTargeting(refund: true); OpenRing(_turn, reopen: true); return true; }
        return undoMove && UndoMove();
    }

    /// <summary>차례 시작 자리에서 지금 자리까지 걸은 비용을 TP 에서 한 번에 빼고, 시작 자리를 지금 자리로 옮긴다.</summary>
    internal void CommitMove(UnitState u)
    {
        if (ComputeRange(u) is { } range && range.CanReach(u.Row * host.Cols + u.Col)) u.Tp -= range.Cost[u.Row * host.Cols + u.Col];
        u.OriginCol = u.Col;
        u.OriginRow = u.Row;
    }

    /// <summary>휴식 <c>0x1007a790</c>: (최대 HP − 현재 HP) × 남은 TP / 최대 TP × Num[35]% 를 채우고 남은 TP 를 버린다.</summary>
    internal void Rest(int index)
    {
        var u = host._units[index];
        CommitMove(u);
        // TP 를 다 쓰고(≤ 0) 끝나는 차례는 휴식이 아니다 — 원본은 깃발만 내리고(0x10071e20 → 0x10074400) 자동 휴식(WAITNEXT 0x10067a7a)에
        // 오지 않아 <b>음수 TP(빚)가 그대로 남는다</b>. 다음 차례가 그만큼 늦다(ba-20 K1). 전에는 늘 0 으로 올려 줘 연속 행동이 공짜였다.
        if (u.Tp <= 0 && !FollowersOf(index).Any(f => f.Tp > 0))
        {
            AutoHealAll();
            EndTurn();
            return;
        }
        RestFollowers(index);        // 대장이 쉬면 부하도 먼저 쉰다(0x1005f62a, 감사3 L3)
        if (u.Tp > 0 && u.MaxTp > 0 && host._db != null && !u.HasStatus(26))
        {
            int heal = (int)((long)(u.MaxHp - u.Hp) * u.Tp / u.MaxTp * host._db.N(35) / 100);
            if (heal > 0)
            {
                int before = u.Hp;
                u.Hp += heal;
                ShowNumber(u, host._db.T(159), HealColor2, rise: false, count: (before, u.Hp));
            }
        }
        u.Tp = 0;   // 부하 TP 가 남아 대장이 자동 휴식에 오면 대장의 빚도 지워진다(0x1007a790 → 0x10071e20, ba-20 M4)
        AutoHealAll();   // 휴식도 행동 끝(상태 20)을 지난다 — 8(자동 회복)
        EndTurn();
    }

    // ── 걷기 ─────────────────────────────────────────────────────────────────

    /// <summary>차례인 아군을 클릭한 파란 칸까지 걷게 한다. TP 는 행동할 때 한 번에 뺀다.</summary>
    internal bool TryWalkTo(int col, int row)
    {
        var u = host._units[_turn >= 0 ? _turn : 0];
        if (!IsPlayerTurn || u.IsBusy || ComputeRange(u) is not { } range) return false;
        if (PathWithin(range, u.Col, u.Row, row * host.Cols + col) is not { Count: > 0 } path) return false;
        foreach (var cell in path) u.Path.Enqueue(cell);
        return true;
    }

    // ── work 사거리·효과 범위 ────────────────────────────────────────────────

    /// <summary>버프·약화 경험치 — 보조·회복 스킬이 든 대상 한 명마다 시전자가 받는 EXP(원본에 없는 규칙, 사용자 요청).</summary>
    internal const int BuffExpPerAlly = 5;

    /// <summary>이번 기술로 보조·회복이 든 대상(시전자 자신 포함) — 기술이 끝날 때 <see cref="GainBuffExp"/> 가 센다.</summary>
    internal readonly HashSet<UnitState> _buffedAllies = [];

    /// <summary>
    /// 보조(종류 2·3)가 든 대상은 편을 가리지 않고(아군 버프·적 약화 모두), 회복(1·5)은 같은 편에게 든 것만 센다. 아이템(어빌리티 0)은 뺀다.
    /// </summary>
    internal void MarkBuffed(UnitState a, UnitState t, WorkData w)
    {
        if (w.AbilityId == 0 || w.IsDamage) return;
        if (w.IsHeal && t.IsAlly != a.IsAlly) return;
        _buffedAllies.Add(t);
    }

    /// <summary>든 대상 한 명마다 <see cref="BuffExpPerAlly"/> 씩 — 내 편(경험치를 쌓는 쪽)만 받는다.</summary>
    internal void GainBuffExp(UnitState a)
    {
        int n = _buffedAllies.Count;
        _buffedAllies.Clear();
        if (n == 0 || !a.IsAlly || a.Data is not { } c) return;
        int exp = BuffExpPerAlly * n;
        a.Data = c with { Exp = c.Exp + exp, CumExp = c.CumExp + exp };
        Popup(a, $"EXP +{exp}", 0xFF90D0FF, 15);
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"buff exp: {a.ChrCode} +{exp} ({n}명) → EXP {a.Data.Exp}" + Environment.NewLine);
        QueueLevelUps();
    }

    /// <summary>격려 — 아군 하나의 SOUL 을 위력만큼 올린다.</summary>
    internal const int EncourageAbility = 10;

    /// <summary>쇼크 — 대상 SOUL 을 위력만큼 깎는다(0x100ae140).</summary>
    internal const int ShockAbility = 39;

    internal WorkData? Work(int id) => host._db != null && host._db.Works.TryGetValue(id, out var w) ? w : null;

    /// <summary>work 를 쓸 수 있나 — TP + CTP 가 TP 비용 이상, SOUL 이 비용 이상.</summary>
    /// <summary>TP 와 SOUL 이 되나 — 필요 SOUL 은 체질 덧붙임까지 넣은 값이다(분석-전투 ba-4).</summary>
    /// <summary>핸들러 첫 단계에서 대상 칸으로 카메라를 보내는 어빌리티(0x100eab80 을 부르는 28개 핸들러 — ba17-camera C14 · ba-20 O P7).</summary>
    internal static readonly HashSet<int> TargetCameraAbilities =
        [31, 37, 39, 40, 42, 43, 50, 55, 56, 72, 79, 81, 88, 93, 97, 107, 108, 116, 126, 141, 142, 166, 169, 171, 186, 191, 192];

    internal bool CanAfford(UnitState u, WorkData w) =>
        u.Data != null && host._db != null && u.Tp + u.Ctp >= TpCostFor(u, u.Data, w.Id) && u.Soul >= SoulNeedFor(u, u.Data, w.Id)
        && (u.Data.JobId == 37 || u.Hp >= host._db.WorkHpCost(u.Data, w.Id));   // HP 비용도 본다(0x100726e0) — 직업 37 은 면제

    /// <summary>
    /// 기본공격 자리 찾기 — 이동 영역 칸(시작 자리 포함) 중 목표가 사거리에 드는, 시작 자리에서 가장 싼 칸.
    /// 길은 지금 자리에서 그 칸까지다.
    /// </summary>
    internal (List<(int Col, int Row)> Path, int Cost)? FindAttackPath(int attackerIndex, int targetIndex)
    {
        var a = host._units[attackerIndex];
        var t = host._units[targetIndex];
        if (!a.Alive || !t.Alive || !SeesAsFoe(a, t) || a.Data == null || Work(a.Data.BasicWorkId) is not { } w || !CanAfford(a, w)) return null;
        if (ComputeRange(a) is not { } range) return null;

        // 원본 0x100608e0 — 기준 칸에서 칠한 파랑(좁은 예산) 가운데 대상이 사거리에 드는 칸 중 <b>지금 선 칸에서 가장 가까운</b> 칸
        // (맨해튼, 같으면 배열 번호 순). 비용은 기준 칸 → 그 칸 한 번이다(ba-20 M2). 전에는 기준 칸에서 가장 싼 칸을 골라,
        // 미리 걸어가 자리를 잡고 적을 눌러도 싼 칸으로 되걸어갔다.
        int narrow = a.HasStatus(25) ? 0 : a.Tp + Math.Min(0, a.Ctp - TpCostFor(a, a.Data, w.Id));
        int best = -1, bestDist = int.MaxValue;
        for (int i = 0; i < range.Cost.Length; i++)
        {
            if (!range.CanReach(i) || range.Cost[i] > narrow) continue;
            int d = Math.Abs(i % host.Cols - a.Col) + Math.Abs(i / host.Cols - a.Row);
            if (d >= bestDist || !InWorkRange(w, i % host.Cols, i / host.Cols, t.Col, t.Row, a)) continue;
            bestDist = d;
            best = i;
        }
        // 길은 넓은 예산(지금 TP 전부)으로 찾는다 — 대상 고르기 중의 range 는 좁은 예산이라, 멀리 걸어가 있으면 지금 칸이 그 밖이라 길이 안 나왔다.
        var wide = ComputeRange(a, tp: Math.Max(a.Tp, 0)) ?? range;
        return best < 0 || (PathWithin(wide, a.Col, a.Row, best) ?? PathWithin(range, a.Col, a.Row, best)) is not { } path ? null : (path, range.Cost[best]);
    }

    // ── 플레이어 대상 고르기 ─────────────────────────────────────────────────

    /// <summary>적을 클릭하면 링을 안 거치고 바로 친다(fa-12) — 걸음 비용도 그때 함께 뺀다.</summary>
    internal void QuickAttack(int targetIndex)
    {
        if (host._units[_turn].Data is not { } c || Work(c.BasicWorkId) is not { } w) return;
        if (FindAttackPath(_turn, targetIndex) is not { } plan)
        {
            host.Hint("공격할 수 없습니다 — 빨간 칸 안의 적을 고르세요");
            return;
        }
        // 걸음 비용은 여기서 미리 빼지 않는다 — UseWorkRoutine 의 CommitMove 가 「기준 칸 → 친 칸」을 한 번만 뺀다(ba-20 M2).
        _commitUndo = null;   // 바로 치므로 되돌릴 일이 없다
        _routine = UseWorkRoutine(_turn, w, targetIndex, host._units[targetIndex].Col, host._units[targetIndex].Row, plan.Path);
    }

    /// <summary>링 공격: 기본공격 대상 고르기(걸어가서 친다).</summary>
    internal void BeginAttackTargeting()
    {
        if (host._units[_turn].Data is not { } c) return;
        _commitUndo = null;   // 기본공격은 걸음을 미리 굳히지 않는다(ba-20 M2)
        _targetWork = c.BasicWorkId;
        _targetIsBasicAttack = true;

        var targets = AttackableEnemies();
        if (targets.Count == 0)
        {
            CancelTargeting(refund: true);
            host.Toast("공격할 수 있는 적이 없습니다");
            return;
        }
        _attackCursor = targets[0];
        string confirm = KeyBindings.KeyName(host._keys[KeyAction.Attack]);
        host.Hint($"{host.UnitName(_attackCursor)} 을(를) 노립니다 — 클릭·Enter·{confirm}: 공격, Tab: 다른 적, 우클릭·Esc: 취소");
    }

    /// <summary>
    /// 차례인 인물이 지금 칠 수 있는 적 — <b>걸어갈 비용이 적은(가까운) 순</b>, 같으면 HP 낮은 순.
    /// 약한 적부터 노리면 멀리 걸어가 TP 를 헛되이 쓰기 일쑤라 가까운 적을 먼저 세운다.
    /// </summary>
    internal List<int> AttackableEnemies() =>
        [.. Enumerable.Range(0, host._units.Length)
            .Where(i => host._units[i].Alive && SeesAsFoe(host._units[_turn], host._units[i]))
            .Select(i => (Index: i, Plan: FindAttackPath(_turn, i)))
            .Where(t => t.Plan != null)
            .OrderBy(t => t.Plan!.Value.Cost).ThenBy(t => t.Plan!.Value.Path.Count)
            .ThenBy(t => host._units[t.Index].Hp).ThenBy(t => t.Index)
            .Select(t => t.Index)];

    /// <summary>공격 커서를 다음(HP 순) 적으로 옮긴다 — 어빌리티를 겨누는 중이면 그 어빌리티 사거리로 센다.</summary>
    internal void CycleAttackCursor()
    {
        var targets = !_targetIsBasicAttack && _targetWork >= 0 && Work(_targetWork) is { } aw
            ? AbilityTargets(aw) : AttackableEnemies();
        if (targets.Count == 0) return;
        _attackCursor = targets[(targets.IndexOf(_attackCursor) + 1) % targets.Count];
    }

    /// <summary>커서의 적을 친다.</summary>
    internal void AttackCursorTarget()
    {
        if (_attackCursor < 0 || !IsPlayerTurn) return;
        var (col, row) = (host._units[_attackCursor].Col, host._units[_attackCursor].Row);
        OnTargetClick(col, row);
    }

    /// <summary>대상 고르는 중의 클릭. 처리했으면 true.</summary>
    internal bool OnTargetClick(int col, int row)
    {
        if (_targetWork < 0) return false;
        if (!IsPlayerTurn || Work(_targetWork) is not { } w) { CancelTargeting(); return true; }
        var user = host._units[_turn];

        if (_targetIsBasicAttack)
        {
            int target = LiveUnitAt(col, row) is { } t ? Array.IndexOf(host._units, t) : -1;
            // 유닛이 없는 칸이면 부술 수 있는 적 물체(기총포탑 따위)를 친다.
            if (target < 0 && ObjectAt(col, row) is { Data.Breakable: true })
            {
                CancelTargeting();
                if (!TryAttackObject(col, row)) host.Hint("칠 수 없는 물체입니다");
                return true;
            }
            if (target < 0 || host._units[target].IsAlly || FindAttackPath(_turn, target) is not { } plan)
            {
                host.Hint("공격할 수 없습니다 — 빨간 칸 안의 적을 고르세요 (우클릭·Esc 취소)");
                return true;
            }
            CancelTargeting();
            _routine = UseWorkRoutine(_turn, w, target, host._units[target].Col, host._units[target].Row, plan.Path);
            return true;
        }

        // 자기 자리에 쓰는 범위 기술 — 보여 준 범위(또는 제 칸)를 한 번 더 누르면 쓴다.
        if (w.SelfCentred)
        {
            if ((col, row) != (user.Col, user.Row) && !AreaCells(w, user, col, row).Contains((col, row)))
            {
                host.Hint("범위 밖입니다 — 주황 칸을 누르세요 (우클릭·Esc 취소)");
                return true;
            }
            UseSelfCentredWork(w);
            return true;
        }
        if (!InWorkRange(w, user.Col, user.Row, col, row, user))
        {
            host.Hint("사거리 밖입니다 — 노란 칸을 고르세요 (우클릭·Esc 취소)");
            return true;
        }
        // 대상 방식 3·6(아무 칸)·7(빈 칸)은 메테오처럼 <b>칸을 고르는</b> 기술이라 그 칸에 아무도 없어도 된다.
        bool needsUnit = w.TargetMode is 1 or 4 or 5;
        // 적 물체(바리케이트·포탑 따위 부술 수 있는 것)도 대상이다 — 피해 기술은 범위 안의 적 물체를 친다(아래 판정, 0x100d9510 은 물체를 먼저 돌려준다).
        // 전에는 유닛만 세어, 기본공격으로는 칠 수 있는 물체에 기술을 쓰면 「대상이 없습니다」가 떴다(사용자 보고: Btl 0133 (1,15)).
        bool hitsObject = w.IsDamage && w.TargetMode is 1 or 5 && w.AbilityId != BlackHoleAbility
            && EffectCells(w, user, col, row).Any(c => ObjectAt(c.Item1, c.Item2) is { Data.Breakable: true, Alive: true } obj && ObjectHostile(obj, user) && !_opened.Contains(obj));
        if (needsUnit && !hitsObject && WorkTargets(w, user, col, row).Count == 0)
        {
            host.Toast("그 칸에는 대상이 없습니다");
            return true;
        }
        ConsumeTargetItem();          // 아이템이면 이때 개수가 하나 준다
        CancelTargeting();
        _routine = UseWorkRoutine(_turn, w, -1, col, row, []);
        return true;
    }

    // ── work 쓰기 (fg-5 공격 · fg-6 어빌리티) ───────────────────────────────

    /// <summary>핸들러가 판정까지 대상을 붙드는 work — 브레인 스톰 · 블라인드 · 안티 밸런싱 · 미라클 · 아이템 1609·1610·1612~1617(ba-21 T3).</summary>
    internal static readonly HashSet<int> HeldTargetWorks =
        [400, 603, 604, 605, 606, 607, 608, 609, 610, 611, 481, 935, 936, 937, 938, 482, 934, 939, 940, 941, 490, 1609, 1610, 1612, 1613, 1614, 1615, 1616, 1617];

    /// <summary>
    /// (필요하면 걸어가서) work 하나를 쓴다: 걸은 비용을 한 번에 빼고, 겨눈 쪽으로 돌고, 동작 5 → 8 → 24 를 재생하며 <b>치는 순간</b>에 대상마다 판정,
    /// 쓰러진 인물은 동작 6 뒤 판에서 뺀다. TP·SOUL 비용과 SOUL 증가를 적용한다.
    /// </summary>
    internal IEnumerator<bool> UseWorkRoutine(int userIndex, WorkData w, int targetIndex, int col, int row,
                                             List<(int Col, int Row)> path, bool eventFinisher = false)
    {
        var a = host._units[userIndex];
        // 이동 + 기술이면 진형을 다시 세우지 않는다 — 원본은 계산만 하고 결과를 안 읽는다(0x1005fa90·0x1005fd00, 분석-군단).
        // 진형은 「이동만」·「이동+휴식」(0x1005f670·0x1005f870)에서만 다시 선다. 전에는 기술 앞에서 늘 다시 세웠다.
        _skillLeader = userIndex;
        // 행동 실행(상태 14) — 행동 인물을 가운데로 보내고 멈춘 뒤에 걷기·기술을 시작한다(0x10069df0, 감사4 C1·C4).
        foreach (bool _ in CenterUnitAndWait(a)) yield return true;
        foreach (var cell in path) a.Path.Enqueue(cell);
        while (a.IsBusy) yield return true;
        foreach (bool _ in WaitFollowersStopped(userIndex)) yield return true;   // 걷던 부하가 다 선 뒤(감사5 L-A)
        CommitMove(a);
        _buffedAllies.Clear();

        if (targetIndex >= 0) (col, row) = (host._units[targetIndex].Col, host._units[targetIndex].Row);
        if (col != a.Col || row != a.Row) a.Facing = FacingToward(a.Col, a.Row, col, row);
        if (w.Id == StanceDefendWork) a.Facing = Facing.Down;   // 방어 0x100b2d50 은 방향 2 를 박아 넣는다 — 늘 아래를 본다
        if (!w.IsDamage) Popup(a, AbilityName(w), 0xFFB0E0FF, 15);
        if (w.TintMode == 1) host._mapTintTarget = w.TintLevel;   // 기술을 쓰는 동안 맵이 물든다(0x10069efa, ba-20 P2)

        var dying = new List<UnitState>();
        int[] actions = ActionsFor(w);
        int hitStep = HitStepFor(w, actions.Length);
        bool chained = ScriptFor(w.Id) is { Actions.Length: > 0 };
        bool effectsDone = false, followersDone = false;
        double effectsAt = host._lastTime;               // 이펙트를 띄운 때 — 핸들러 판정 틱의 기준(단계 0)
        double fxSpan = 0;                          // 그 이펙트들이 다 끝나기까지의 틱
        _followerStrikes.Clear();
        host.Mov.SpawnWorkMovies(w, a, col, row, prelude: true);     // 준비 동작의 시전 영상(불기둥 Mov 0041·0042) — 시전이 시작할 때
        // 필살기(준비 7)는 공통 앞머리(빛 알갱이·초상 컷인·금빛 띠, 0x1007e330)를 다 돈 뒤에 핸들러로 간다.
        bool finisher = w.Prepare == 7;
        // 준비 2·3·5·6 — 시전 소리 1338:1(694)은 시전 시작 +2틱(0x1007def0 단계 1). 핸들러는 동작 15 가 끝난 뒤에 돈다(ba-15 R6).
        if (w.Prepare is 2 or 3 or 5 or 6) host._pendingSounds.AddRange((host._effectTables.GetValueOrDefault(1338)?.Clips.GetValueOrDefault(1)?.Sounds ?? []).Select(s => (host._lastTime + (2 + s.Item1) / TicksPerSecond, s.Item2)));
        if (finisher) foreach (bool _ in host.FinisherPreludeAb.FinisherPrelude(w, a)) yield return true;
        // 필살기는 앞머리가 준비 동작(사슬 앞의 6·15)을 이미 했다 — <b>그 둘만</b> 건너뛴다. 전에는 타격 아닌 동작을 모두 건너뛰어
        // 오메가 스윙·더블 브레이크·이데아 캐논 따위가 마지막 동작 하나만 했다(fg-20).
        int preludeSteps = finisher ? PreludeSteps(actions) : 0;
        bool holding = false;                               // 붙드는 모션(2000+m)을 치는 칸에서 틀었나
        bool targetCentred = false;                         // 대상 칸 가운데 맞추기를 했나(치는 단계 첫 한 번)
        for (int step = 0; step < Math.Max(actions.Length, 1); step++)
        {
            // 어빌리티 사슬은 <b>타격 동작마다</b> 친다 — 「연」은 레벨이 오르면 13 → 14 → 8 처럼 타격 동작이 늘어나
            // 2·3·4·5·6타가 된다(분석-모션 ba-10). 전에는 사슬의 첫 타격 동작에서만 판정을 내 3타에서 멈췄다.
            bool strikes = step == hitStep || (chained && step < actions.Length && IsStrikeAction(actions[step]));
            if (!strikes)
            {
                if (step >= actions.Length || step < preludeSteps) continue;
                PlayChainStep(a, actions[step], HoldSeconds);
                if (actions[step] >= RawHold) for (double end = host._lastTime + 0.5; host._lastTime < end;) yield return true;   // 치는 칸이 아닌 붙듦(드묾)은 잠깐만
                else while (a.IsBusy) yield return true;
                continue;
            }

            // 기술은 치는 단계에 들어서면 대상 칸을 가운데로 보내고 멈출 때까지 기다린다 — 스킬 핸들러 27곳의 첫 단계
            // 0x100eab80(대상칸, 0, 0) → 0x1006e850 대기(감사4 C14). 기본공격(명령 0x2712)은 카메라 명령이 없다.
            if (!targetCentred)
            {
                targetCentred = true;
                // 대상 칸으로 카메라를 보내는 핸들러(0x100eab80 호출)는 28개뿐이다 — 나머지 기술은 카메라가 시전자에 머문다(ba-20 O P7).
                if (TargetCameraAbilities.Contains(w.AbilityId) && a.Data?.BasicWorkId != w.Id && (uint)col < host.Cols && (uint)row < host.Rows)
                    foreach (bool _ in CenterAndWait(col * TileW + TileW / 2, host.CellCenterY(col, row))) yield return true;
            }
            // 치는 동작은 끝까지 기다리지 않는다 — 동작이 뜨고 0.05초 뒤부터,
            // 그 모션에 든 타격 키 수(동작 13 = 2타, 14 = 3타)만큼 그 간격대로 판정을 낸다(분석-모션 ba-10).
            var hitTimes = new List<double> { 0 };
            if (step < actions.Length && step >= preludeSteps && actions[step] >= RawOnce)
            {
                // 모션 번호 — 붙드는 모션이면 그 자세로 이펙트가 터지고, 한 번 트는 모션이면 뜨자마자 친다.
                PlayChainStep(a, actions[step], HoldSeconds);
                holding = actions[step] >= RawHold;
                for (double end = host._lastTime + 0.05; host._lastTime < end;) yield return true;
            }
            else if (step < actions.Length && step >= preludeSteps)
            {
                PlayAction(a, DrawnAction(a, actions[step]));
                hitTimes = HitTimesFor(a, DrawnAction(a, actions[step]), ranged: w.RangeMax > 4);
                // 준비 2·3·5·6 의 발동 동작 15 는 끝까지 튼 뒤에 핸들러가 돈다 — 효과·판정을 그때로(ba-15 R6).
                if (w.Prepare is 2 or 3 or 5 or 6 && actions[step] == 15 && host._sprites.TryGetValue(a.ChrCode, out var sp15))
                    hitTimes = [Math.Max(0.05, sp15.ActionSeconds(DrawnAction(a, 15), a.Facing))];
                for (double end = host._lastTime + hitTimes[0]; host._lastTime < end;) yield return true;
            }
            int effectMark = _effects.Count;

            // 「연」 사슬 끝의 동작 8 한 대는 연 위력이 아니라 기본공격 위력이다(키 인자 25 = work 1, 분석-모션 ba-10).
            var hitWork = chained && step < actions.Length && actions[step] == 8 && a.Data is { } ad
                          && Work(ad.BasicWorkId) is { } basic ? basic : w;
            if (!effectsDone)
            {
                host.ScheduleAbilitySounds(w);
                _fxTargets = targetIndex >= 0 ? [targetIndex] : null;
                _fxStagger = 0;
                _fxHitAt.Clear();
                _fxArriveAt = 0;
                SpawnAbilityEffects(w, a, col, row);
                _fxTargets = null;
                // 판정을 기다릴 상한 — 이 work 의 이펙트가 다 끝나는 때(지연 + 수명 또는 모션 길이). 길이를 모르는 단계가 낀 표 값이 이펙트보다 길어 빈 대기가 되지 않게.
                fxSpan = (ScriptFor(w.Id)?.Effects ?? []).Select(fx => fx.Delay + (fx.Life > 0 ? fx.Life : host.EffectTicks(fx.Obs, fx.Motion))).DefaultIfEmpty(0).Max();
                host.StagingAb.SpawnWorkShakes(w);
                effectsAt = host._lastTime;
                // 카메라 따라가기(0x100eac00, ba-21 fx F7) — 힐·큐어·배리어류 189개는 겨눈 대상을, 34개는 시전자를 따라간다. 탄을 따라가는 60개는 아직.
                if (WorkCameraFollow.Target.Contains(w.Id) && !WorkCameraFollow.Shot.Contains(w.Id)
                    && (targetIndex >= 0 ? host._units[targetIndex] : LiveUnitAt(col, row)) is { } followed && followed != a)
                    CenterOnUnit(followed);
                else if (WorkCameraFollow.Caster.Contains(w.Id) && !WorkCameraFollow.Target.Contains(w.Id)) CenterOnUnit(a);
                // 탄을 따라가는 60개 — 탄이 가는 쪽(겨눈 칸)으로 카메라를 보낸다(탄 자체를 따라가지는 않는다 — 근사).
                else if (WorkCameraFollow.Shot.Contains(w.Id) && _fxArriveAt > 0 && (uint)col < host.Cols && (uint)row < host.Rows) CenterOnCell(col, row);
                // 군단기면 부하 잔상(ba-21 C) — 피해는 그 연출의 끝 무렵에 들어간다.
                for (double end = host._lastTime + host.LegionStageAb.StartLegionStage(a, w, col, row, WorkTargets(w, a, col, row)); host._lastTime < end;) yield return true;
                effectsDone = true;
            }
            // 혼·비연참·오메가 스윙 — 겨눈 빈 칸까지 돌진하며 지나는 칸의 적을 때린다(fg-21 ⑨). 돌진 자세(붙듦)는 이미 틀었다.
            if (PushSkill.DashWorks.Contains(w.Id))
            {
                foreach (bool _ in host.PushAb.DashRoutine(a, w, col, row, dying)) yield return true;
                holding = false;
                continue;
            }
            // 하이 텔레포트 — 피해 없이 고른 칸 둘레로 순간이동한다(범위는 빗나가는 정도).
            if (TeleportSkill.IsTeleportWork(w))
            {
                foreach (bool _ in host.TeleportAb.TeleportRoutine(w, a, col, row)) yield return true;
                while (a.IsBusy) yield return true;
                continue;
            }
            // 아크로스트의 엘레맨탈 파이어(불덩이가 돌다 대상마다 날아가 터짐)·서몬 몬스터(대상마다 몬스터가 나타나 침) — 피해는 대상마다 그때 한 번.
            // 편집기에서 대상 방식을 「아무 칸」(메테오처럼)으로 바꿔도 같은 연출 — 불덩이는 시전자 위에서 돌고 고른 범위의 대상에게 날아간다.
            if (w.AbilityId is AcrostSkill.ElementalFireAbility or AcrostSkill.SummonMonsterAbility)
            {
                var targets = WorkTargets(w, a, col, row);
                var struck = new HashSet<int>();
                void Hit(int ti) { if (struck.Add(ti)) ApplyWork(a, hitWork, host._units[ti], dying); }
                if (Trace)
                    System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                        $"acrost work {w.Id}: caster {a.ChrCode}({a.Col},{a.Row}) targets " +
                        string.Join(", ", targets.Select(t => $"{host._units[t].ChrCode}({host._units[t].Col},{host._units[t].Row}){(host._units[t].IsAlly ? " 아군" : "")}")) + Environment.NewLine);
                var routine = w.AbilityId == AcrostSkill.ElementalFireAbility ? host.AcrostAb.ElementalFireRoutine(a, targets, Hit) : host.AcrostAb.SummonMonsterRoutine(a, targets, Hit);
                foreach (bool _ in routine) yield return true;
                if (!followersDone && targets.Count > 0)
                {
                    FollowersAttack(userIndex, host._units[targets[0]], dying, allyPass: false, leaderWork: w);
                    followersDone = true;
                }
                while (a.IsBusy) yield return true;
                continue;
            }
            // 그라비티 필드 — 대상마다 중력장(219:0)이 차례로 깔리고, 끝날 때 효과가 든다.
            // 아스트럴 애로우 — 활을 당겨 화살을 쏘아 올리고, 대상마다 화살이 쏟아진다. 피해는 화살이 닿을 때 대상마다 한 번.
            if (w.AbilityId == AstralArrowSkill.AstralArrowAbility)
            {
                var targets = WorkTargets(w, a, col, row);
                var struck = new HashSet<int>();
                foreach (bool _ in host.AstralArrowAb.AstralArrowRoutine(a, targets, ti => { if (struck.Add(ti)) ApplyWork(a, hitWork, host._units[ti], dying); }))
                    yield return true;
                while (a.IsBusy) yield return true;
                continue;
            }
            // 리콜 — 고른 아군을 시전자 옆으로 불러온다(피해·회복 없음).
            if (w.AbilityId == TeleportSkill.RecallAbility)
            {
                var targets = WorkTargets(w, a, col, row);
                var who = targets.Select(i => host._units[i]).FirstOrDefault(u => u != a && u.IsAlly == a.IsAlly);
                if (who != null)
                {
                    foreach (bool _ in host.TeleportAb.RecallRoutine(a, who)) yield return true;
                    MarkBuffed(a, who, w);
                }
                while (a.IsBusy) yield return true;
                continue;
            }
            // 메테오 — 운석 한 발이 범위 안 한 칸에 떨어지고, 닿으면 범위 안 대상마다 피해가 한 번.
            if (w.AbilityId == MeteorSkill.MeteorAbility)
            {
                var targets = WorkTargets(w, a, col, row);
                var struck = new HashSet<int>();
                foreach (bool _ in host.MeteorAb.MeteorRoutine(w, a, col, row, targets, ti => { if (struck.Add(ti)) ApplyWork(a, hitWork, host._units[ti], dying); }))
                    yield return true;
                while (a.IsBusy) yield return true;
                continue;
            }
            if (w.AbilityId == GravityFieldSkill.GravityFieldAbility)
            {
                var targets = WorkTargets(w, a, col, row);
                var struck = new HashSet<int>();
                foreach (bool _ in host.GravityFieldAb.GravityFieldRoutine(a, targets, ti => { if (struck.Add(ti)) ApplyWork(a, hitWork, host._units[ti], dying); }))
                    yield return true;
                while (a.IsBusy) yield return true;
                continue;
            }
            // 천지 파열무 — X 자로 땅이 터진 뒤 대상마다 폭발한다. 피해는 그 폭발 때 대상마다 한 번.
            if (w.Id == HeavenEarthSkill.HeavenEarthWork && targetIndex < 0)
            {
                var targets = WorkTargets(w, a, col, row);
                var struck = new HashSet<int>();
                foreach (bool _ in host.HeavenEarthAb.HeavenEarthRoutine(a, targets, ti => { if (struck.Add(ti)) ApplyWork(a, hitWork, host._units[ti], dying); }))
                    yield return true;
                if (!followersDone && targets.Count > 0)
                {
                    FollowersAttack(userIndex, host._units[targets[0]], dying, allyPass: false, leaderWork: w);
                    followersDone = true;
                }
                while (a.IsBusy) yield return true;
                continue;
            }
            // 나인 크루세이더 — 칼이 날아다니며 차례로 꿰뚫는다. 피해는 대상마다 처음 꿰뚫릴 때 준다.
            if (w.Id == NineCrusaderSkill.NineCrusaderWork && targetIndex < 0)
            {
                var targets = WorkTargets(w, a, col, row);
                // 원본 0x1009cb50 — 모션 48(칼을 들어 올림) 뒤 칼이 나는 동안 49 를 붙들고, 다 날면 51(fg-20).
                for (double end = host._lastTime + host.HeavenEarthAb.PlayRawMotion(a, 48, loop: false); host._lastTime < end;) yield return true;
                host.HeavenEarthAb.PlayRawMotion(a, 49, loop: true, holdSeconds: 30);
                var flight = host.NineCrusaderAb.StartNineCrusader(a, targets);
                var struck = new HashSet<int>();
                while (!flight.Done)
                {
                    host.NineCrusaderAb.StepSword(flight);
                    while (flight.Pierced.TryDequeue(out int ti))
                        if (struck.Add(ti)) ApplyWork(a, hitWork, host._units[ti], dying);
                    yield return true;
                }
                foreach (int ti in targets.Where(struck.Add)) ApplyWork(a, hitWork, host._units[ti], dying);   // 칼이 못 닿은 대상(없어야 한다)
                double release = host.HeavenEarthAb.PlayRawMotion(a, 51, loop: false);
                if (release <= 0) a.PlayAction(ObsMotionTable.ActionStand, 0);
                if (!followersDone && targets.Count > 0)
                {
                    FollowersAttack(userIndex, host._units[targets[0]], dying, allyPass: false, leaderWork: w);
                    followersDone = true;
                }
                while (a.IsBusy) yield return true;
                continue;
            }
            double stagedFrom = host._lastTime;
            if (step == hitStep) foreach (bool _ in host.StagingAb.StageBeforeHit(w, a, targetIndex, col, row)) yield return true;   // 밸런싱·웹폰 크래쉬·블랙홀(ba-20 P6~P8)
            // 핸들러가 판정(1001)을 보내는 틱까지 기다린다(ba-21 T1) — 원본은 이펙트가 다 나온 뒤에 숫자·맞음 동작이 뜬다(힐 114틱, 프레셔 49틱 …).
            // 전에는 이펙트를 띄우는 틀에 판정해 숫자가 먼저 떴다. 길이를 다 아는 work(Sure)과 카메라 대기만 모르는 work 만 따른다.
            // 몸짓 타격 키로 치는 칸(여러 타)·필살기(준비 7 — 사슬이 핸들러 모션을 이미 튼다)·군단기·전용 연출이 이미 기다린 work 은 뺀다.
            if (step == hitStep && host._lastTime == stagedFrom && hitTimes.Count == 1 && w.Prepare != 7 && !LegionStageSkill.IsLegionSkill(w.Id) && !host.SpecialHitsAb.HasSpecialHit(w)
                && (WorkHitTicks.Table.TryGetValue(w.Id, out var handlerHit) || (WorkFxExtra.ArriveHitWorks.Contains(w.Id) && _fxArriveAt > 0)))   // 길이를 모르는 단계가 낀 work 도 따른다 — 이펙트 표의 지연과 같은 시계라 숫자가 이펙트보다 먼저 뜨지 않는다
            {
                bool sureHit = handlerHit.Sure || WorkHitTicks.CameraOnly.Contains(w.Id);
                double hitAt = effectsAt + Math.Min(Math.Min(handlerHit.Ticks, 240), sureHit ? 240 : Math.Max(10, fxSpan)) / TicksPerSecond;
                // 핸들러가 대상을 붙드는 기술(브레인 스톰·블라인드·안티 밸런싱·미라클·아이템 1609~1617) — 판정까지 맞음 자세(미라클은 시전 자세로 아래를 봄)로
                // 붙들었다가 판정 바로 앞에 서기로 되돌린다(0x10095ab0 · 0x100a19ed · 0x100a1dbf · 0x1009b958 · 0x100b76f5, ba-21 T3).
                var held = new List<(UnitState Unit, Facing Was)>();
                if (HeldTargetWorks.Contains(w.Id) && hitAt > host._lastTime)
                    foreach (int ti in targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row))
                    {
                        var t = host._units[ti];
                        if (!t.Alive || t.Hp <= 0 || t == a) continue;
                        held.Add((t, t.Facing));
                        if (w.Id == 490) t.Facing = Facing.Down;
                        t.PlayAction(w.Id == 490 ? 6 : HitAction, hitAt - host._lastTime);
                    }
                // 「탄이 사라질 때 판정」 work(카운터 미사일 · 블레이드 샤워 …, 41개)은 탄이 닿는 때에 맞는다(ba-21 fx 덧정보 표 생성 기록).
                if (WorkFxExtra.ArriveHitWorks.Contains(w.Id) && _fxArriveAt > 0) hitAt = Math.Min(_fxArriveAt, effectsAt + 300 / TicksPerSecond);
                while (host._lastTime < hitAt) yield return true;
                foreach (var (t, was) in held)
                {
                    t.Facing = was;                                   // 미라클이 돌려 둔 방향은 되돌린다
                    if (t.Alive && t.Hp > 0) t.PlayAction(ObsMotionTable.ActionStand, 0);
                }
            }
            // 소닉 블레이드·크레이지 샷 — 핸들러가 자료 범위와 다르게 친다(ba-20 E1·E2).
            if (step == hitStep && host.SpecialHitsAb.HasSpecialHit(w))
            {
                foreach (bool _ in host.SpecialHitsAb.SpecialHitRoutine(a, w, hitWork, col, row, dying)) yield return true;
                if (!followersDone && w.FollowersAct)
                {
                    FollowersAttack(userIndex, LiveUnitAt(col, row) ?? a, dying, allyPass: false, leaderWork: w, walk: false);
                    followersDone = true;
                }
                if (holding) a.PlayAction(ObsMotionTable.ActionStand, 0);
                while (a.IsBusy) yield return true;
                holding = false;
                continue;
            }
            // 리인카네이션은 보통 타격이 없다 — 피해는 밀어내기 슬롯만 준다(RadialPushRoutine, 0x1008d940 · ba-16 R1).
            for (int hit = 0; hit < (PushSkill.ReincarnationWorks.Contains(w.Id) ? 0 : hitTimes.Count); hit++)
            {
                if (hit > 0)
                    for (double end = host._lastTime + (hitTimes[hit] - hitTimes[hit - 1]); host._lastTime < end;) yield return true;
                var targets = targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row);
                // 대상마다 이펙트가 엇갈려 뜨는 기술(헤비프레셔 15틱 · 엘레맨탈 썬더 8틱 …)은 판정도 그 간격으로 하나씩 든다(ba-21 fx F6).
                // 간격은 이펙트가 실제로 엇갈려 뜬 값(_fxStagger)이고, 다 합쳐 150틱을 안 넘는다. 반사로 시전자가 쓰러지면 거기서 멈춘다.
                int stagger = hitTimes.Count == 1 && targets.Count > 1 ? Math.Min(_fxStagger, 150 / (targets.Count - 1)) : 0;
                for (int k = 0; k < targets.Count; k++)
                {
                    if (k > 0 && !a.Alive) break;
                    if (k > 0 && stagger > 0)
                        for (double end = host._lastTime + stagger / TicksPerSecond; host._lastTime < end;) yield return true;
                    // 이펙트가 그 대상에 닿는 때가 정해진 기술(라이트닝 샤벨)은 그때까지 기다린다 — 시작에서 300틱을 안 넘게.
                    if (_fxHitAt.TryGetValue(host._units[targets[k]], out double reach))
                        for (double end = Math.Min(reach, effectsAt + 300 / TicksPerSecond); host._lastTime < end;) yield return true;
                    ApplyWork(a, hitWork, host._units[targets[k]], dying);
                }
                // 범위 안의 적 물체(포탑·바리케이트)도 맞는다(0x100d9510 은 물체를 먼저 돌려준다) — 피해량은 기본공격과 같은 식(가설).
                if (hit == 0 && w.IsDamage && w.AbilityId != BlackHoleAbility && a.Data is { } od)
                    foreach (var (oc, or) in EffectCells(w, a, col, row))
                        if (ObjectAt(oc, or) is { Data.Breakable: true, Alive: true } obj && ObjectHostile(obj, a) && !_opened.Contains(obj))
                            DamageObject(a, obj, host._db!.Atk(od, a.Soul, hitWork.Power));
                // 군단 행동(상태 15) — 대장이 기술을 쓰면 부하들도 <b>한 번</b> 같은 패스로 제 기술을 쓴다(여러 타를 쳐도 부하는 한 번).
                // 합류는 대장 work +0x41 만 본다(0x1006a110 → 0x1005fa90, FollowersAttack 첫머리). 패스는 대장 work 의 <b>효과 대상 +0x1e</b> 로 가른다
                // (0x1005fb6d~0x1005fbca): 4 → 아군 패스, 5 → 겨눈 유닛이 적이 아니면 아군 패스, 그 밖 → 적 패스. 종류(피해·보조)는 안 본다.
                // 전에는 「피해 기술이거나 방식 4」일 때만 따라가 약화 캡슐·저주·버프에 부하가 가만히 있었다(감사3 L7).
                // 겨눈 대상이 없는 제자리 기술도 패스가 돈다 — 기준 칸은 대장 자리, 걸어가 치기는 없다.
                if (!followersDone && w.FollowersAct)
                {
                    var aimed = targetIndex >= 0 ? host._units[targetIndex] : LiveUnitAt(col, row);
                    bool allyPass = w.AreaMode == 4 || (w.AreaMode == 5 && aimed != null && !SeesAsFoe(a, aimed));
                    // 군단 행동은 두 패스가 늘 다 돈다 — 아군 패스(0x1005fa90) → 적 패스(0x1005fd00). 부하는 두 패스 모두에서 고르고
                    // 대장만 제 work 에 맞는 패스에서 움직인다(ba-20 Q1). 아군 패스는 제자리만(0x1005f1c0 → 0x1005e820, Q3).
                    // 아군 패스에서 기술을 쓴 부하는 동작 중이라 적 패스에서 빠진다.
                    var anchor = targets.Count > 0 ? host._units[targets[0]] : a;
                    FollowersAttack(userIndex, anchor, dying, allyPass: true, leaderWork: w, walk: false);
                    FollowersAttack(userIndex, anchor, dying, allyPass: false, leaderWork: w, walk: targets.Count > 0 && !allyPass);
                    followersDone = true;
                }
                if (targets.Count == 0 || !host._units[targets[0]].Alive) break;
            }
            // 사이킥 크로스 — 두 획이 겹치는 가운데 칸의 대상은 5틱 뒤 한 번 더 맞는다(ba-14 H4).
            if (PushSkill.PsychicCrossWorks.Contains(w.Id))
            {
                for (double end = host._lastTime + 5 / TicksPerSecond; host._lastTime < end;) yield return true;
                if (LiveUnitAt(col, row) is { } centre && centre != a && !dying.Contains(centre) && SeesAsFoe(a, centre)) ApplyWork(a, hitWork, centre, dying);
            }
            // 여러 번 치는 기술 — 무신멸뢰옥 3타·선 블래스트 4타·카운터 미사일 Lv11↑ 2회(fg-21 ⑩). 간격은 원본 핸들러 틱.
            foreach (int gap in host.PushAb.ExtraHitGaps(w))
            {
                for (double end = host._lastTime + gap / TicksPerSecond; host._lastTime < end;) yield return true;
                if (w.Id == PushSkill.SunBlastWork) host.HeavenEarthAb._shakes.Add((host._lastTime, host._lastTime + 6 / TicksPerSecond, 6, false));
                if (Trace) System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"), $"{host._lastTime:F2} extra hit work {w.Id} after {gap} ticks" + Environment.NewLine);
                foreach (int ti in (targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row)))
                    if (host._units[ti].Alive && !dying.Contains(host._units[ti])) ApplyWork(a, hitWork, host._units[ti], dying);
            }
            // 비·다이나믹 크래쉬 — 맞은 인물을 시전자가 보는 쪽으로 밀어낸다(fg-18·fg-21 ⑧). 그 타로 쓰러질 인물도 밀린 칸에서 쓰러진다
            // (비 단계 2 0x10080111~0x1008035c 는 겨눈 유닛을 조건 없이 민다 · 다이나믹 크래쉬는 밀고 나서 판정, ba-21 T5). 전에는 쓰러질 인물은 안 밀었다.
            if (KnockbackSkill.BiWorks.Contains(w.Id) || PushSkill.DynamicCrashWorks.Contains(w.Id))
            {
                var knocked = (targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row))
                              .Select(i => host._units[i]).FirstOrDefault(u => u.Alive && u != a);
                if (knocked != null)
                    foreach (bool _ in host.KnockbackAb.KnockbackRoutine(a, w, knocked, soulDrain: PushSkill.DynamicCrashWorks.Contains(w.Id) ? 10 : 0)) yield return true;
                else if (KnockbackSkill.BiWorks.Contains(w.Id)) PlayAction(a, DrawnAction(a, 24));
            }
            // 리인카네이션 — 범위 안 적을 시전자 반대쪽으로 범위 밖까지 밀어낸다(fg-21 ⑧).
            if (PushSkill.ReincarnationWorks.Contains(w.Id))
                foreach (bool _ in host.PushAb.RadialPushRoutine(a, w, col, row, WorkTargets(w, a, col, row), dying)) yield return true;
            // 워핑 — 대상을 멀리 날려 보낸다(fg-21 ⑧).
            if (PushSkill.WarpingWorks.Contains(w.Id))
            {
                var thrown = (targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row))
                             .Select(i => host._units[i]).FirstOrDefault(u => u.Alive && u != a && !dying.Contains(u));
                if (thrown != null) foreach (bool _ in host.PushAb.ThrowRoutine(a, thrown)) yield return true;
            }
            if (holding)
            {
                // 붙든 자세는 이 기술의 이펙트가 다 터질 때까지(길어도 3초) — 그 뒤 다음 칸(놓는 모션)으로, 없으면 선다.
                var spawned = _effects.Skip(effectMark).ToList();
                for (double start = host._lastTime; host._lastTime - start < 3 && (host._lastTime - start < 0.5 || spawned.Any(_effects.Contains));)
                    yield return true;
                holding = false;
                if (step + 1 >= actions.Length) a.PlayAction(ObsMotionTable.ActionStand, 0);
                continue;
            }
            while (a.IsBusy) yield return true;   // 남은 동작을 마저 재생한다
        }

        // 늦게 뜨는 이펙트(단계·사슬 지연)가 시작할 때까지는 행동이 안 끝난다 — 상한 200틱.
        for (double cap = host._lastTime + 200 / TicksPerSecond; host._lastTime < _fxLatestStart && host._lastTime < cap;) yield return true;
        _fxLatestStart = 0;
        // 군단기 연출이 끝나 모두 다시 보일 때까지 기다린다 — 부하의 걸어가 치기도 그 뒤에.
        while (host._lastTime < host.LegionStageAb._legionStageEnd) yield return true;
        // 사거리 밖이던 부하는 먼저 걸어간 뒤 친다(원본 0x1005f1c0: 명령1 이동 → 명령2 기술).
        if (_followerStrikes.Count > 0)
        {
            while (_followerStrikes.Any(s => s.Follower.IsBusy)) yield return true;
            foreach (var (follower, work, aimCol, aimRow) in _followerStrikes)
            {
                // 걸어가는 사이 TP 가 모자라게 됐으면(걸음 값을 이미 냈다) 안 친다 — 원본도 명령2 기술을 끝처리에서 제 TP 로 정산한다(감사3 L1).
                if (!follower.Alive || !follower.OnField || follower.Hp <= 0 || !CanAfford(follower, work)) continue;
                FollowerStrike(follower, work, aimCol, aimRow, dying);
            }
            _followerStrikes.Clear();
            for (double end = host._lastTime + 0.2; host._lastTime < end;) yield return true;
        }

        // 이스케이프는 겨눈 빈 칸으로 순간이동한다(분석-모션 ba-10) — 마리아·유블레인이 쓰는,
        // 사라졌다 나타나는 그 기술이다. 그냥 자리만 덮어쓰면 뚝 끊겨 보이니 짧게 사라졌다 나타나게 한다.
        if (w.Id == EscapeWork && LiveUnitAt(col, row) == null)
        {
            const double fadeSeconds = 0.15;
            for (double start = host._lastTime, end = start + fadeSeconds; host._lastTime < end;)
            {
                a.Fade = Math.Max(0, 1 - (host._lastTime - start) / fadeSeconds);
                yield return true;
            }
            a.Fade = 0;
            a.WarpTo(col, row);
            a.OriginCol = col;
            a.OriginRow = row;
            for (double start = host._lastTime, end = start + fadeSeconds; host._lastTime < end;)
            {
                a.Fade = Math.Min(1, (host._lastTime - start) / fadeSeconds);
                yield return true;
            }
            a.Fade = 1;
        }

        host._mapTintTarget = -1;   // 행동이 끝나면 맵 물들이기를 푼다(0x1002e920)
        // 희생(13)·익스플로젼(22) — 아군을 회복시킨 뒤 시전자 HP 를 0 으로 쓴다(0x10090d62 · 0x100915f9, ba-20 D1).
        // 처치 보상은 아무도 안 받는다. 47(전투불능 방지)이 되살리는지는 가설.
        if (w.AbilityId is SacrificeAbility or ExplosionAbility && a.Alive && !dying.Contains(a))
        {
            a.Hp = 0;
            if (!SurvivesFatal(a)) dying.Add(a);
        }
        if (w.Id is StanceDefendWork or StanceEvadeWork) a.Stance = w.Id == StanceDefendWork ? 1 : 2;
        GainBuffExp(a);
        PayWorkCost(a, w, free: eventFinisher);   // 사건 207·909 는 +0xa4 = 1 — 비용 없이 SOUL 증가만(0x1007638c, 감사5 B2)
        // 기술 뒤 대장 자리를 「진형을 짠 자리」로 적어 둔다 — SyncFollowers 가 이 자리로 진형을 다시 세우지 않게.
        _formationAt[userIndex] = (a.Col, a.Row);
        _skillLeader = -1;
        // 사건 207 의 쓰러짐 처리(0x1004e6d0)는 지금 차례 유닛([ctrl+0x4ce8])이면 HP 1 로 남긴다(0x1004e757~0x1004e765, 감사5 B3).
        if (eventFinisher && (uint)_turn < host._units.Length && host._units[_turn] is var now && dying.Remove(now)) now.Hp = 1;

        // 22·23·24(SOUL·TP 사망 조건)는 행동 끝마다 본다 — 상태 20 → 18 CHRDIE 가 전 유닛에 0x1007c670 을 돌린다(ba-20 K3).
        foreach (var d in host._units)
            if (d.Alive && d.OnField && !dying.Contains(d) && DiesByStatus(d) && !SurvivesFatal(d)) { d.Hp = 0; GainAilmentKillExp(d); dying.Add(d); }
        // 행동 끝 사건 검사(갈래 2, 0x100680a3)는 쓰러짐 처리(상태 18)보다 <b>앞</b>이다 — HP 0 유닛이 아직 서 있을 때 본다(ba-20 V1).
        // 보스가 죽지 않고 물러나는 사건(201)·풀피 회복(707)·HP 조건(203) 사건이 이때 터진다. 전에는 쓰러뜨린 뒤에 봐서
        // Btl 0092 사건 3·0145 사건 4(그 전투의 유일한 끝 사건)가 통째로 빠졌다.
        bool hadDying = dying.Count > 0;
        if (dying.Count > 0 && !eventFinisher)
        {
            _eventCheckDue |= 1 << 2;
            // 참인 사건이 여럿이면 다 돌 때까지 — 한 틀에 끝나는 사건 뒤의 사건(707 회복·201 퇴장)이 죽는 동작 도중에 돌지 않게.
            for (int guard = 0; guard < 16; guard++)
            {
                RunEvents();
                if (!EventsBusy && (_eventCheckDue == 0 || _events.Count == 0)) break;
                if (EventsBusy) yield return true;  // 사건이 도는 동안 이 루틴은 멈춘다(UpdateTurn)
                if (_outcome.Length > 0) break;
            }
            hadDying = true;
            dying.RemoveAll(d => d.Hp > 0 || !d.OnField || !d.Alive);
        }
        if (dying.Count > 0)
        {
            // 18 CHRDIE(0x1006a8b1) — 쓰러질 유닛마다 가운데 명령을 같은 틀에 걸어 목록 순서 <b>마지막</b>이 이긴다. 기다리지 않고 죽는 동작을 같이 한다(감사4 C17).
            CenterOnUnit(dying.OrderBy(d => Array.IndexOf(host._units, d)).Last());
            foreach (var d in dying) { PlayActionFor(d, HitAction, DeathActionTicks); host.Play(SoundDeath); }
            // 쓰러지는 21틱 동안 옅어진다 — 섞기 단계 = 8 − 카운터/3, 가중 4k/31(그리기 0x10071af5~0x10071bdd, ba-20 P3).
            for (double fallAt = host._lastTime; dying.Any(d => d.IsBusy);)
            {
                int n = 1 + (int)((host._lastTime - fallAt) * TicksPerSecond);
                foreach (var d in dying) d.Fade = Math.Clamp((8 - n / 3) * 4 / 31.0, 0, 1);
                yield return true;
            }
            foreach (var d in dying) d.Fade = 1;
            foreach (var d in dying)
            {
                if (d.Hp > 0 || !d.OnField) continue;    // 그 사이 사건이 되살렸거나 판에서 뺐다
                MarkDead(d);                             // 대장이 죽으면 첫 부하가 대장이 된다(승계는 MarkDead 한 곳에서, 감사5 L-B)
            }
        }
        // 행동 끝 상태 21(0x100681a0) — 처치가 없어도 <b>매 행동마다</b> 레벨업을 보고, 그다음 갈래 1 검사(0x10068270)를 한다.
        // 전멸 판정(상태 4, 0x1006ed10)은 그 뒤라 마지막 적을 쓰러뜨린 사람의 레벨업 창도 전투가 끝나기 전에 뜬다(ba-15 Q7).
        // 전에는 CheckOutcome 이 먼저 결과를 세워 UpdateTurn 이 레벨업 창 앞에서 돌아가 버렸다.
        AutoHealAll();   // 상태 20 갈래 0(0x10067d70) — 행동이 끝날 때마다 8(자동 회복)을 전원에게(ba-20 C1)
        QueueLevelUps();
        _eventCheckDue |= 1 << 1;
        if (dying.Count > 0 || hadDying)            // 사건이 쓰러질 유닛을 빼거나(201) 죽였어도(706) 전멸 판정은 한다
        {
            if (_levelUpQueue.Count == 0) CheckOutcome();
            else _outcomeAfterLevelUp = true;       // 레벨업 줄이 다 빠진 뒤 UpdateLevelUp 이 본다
        }
        for (double end = host._lastTime + 0.1; host._lastTime < end;) yield return true;
    }

    /// <summary>
    /// work 끝처리 <c>0x10076380</c> — TP → SOUL → HP 차례로 비용을 빼고 행동 뒤 SOUL 을 올린다. 체질마다 SOUL·TP·HP 로 나뉘는 비율이 다르다.
    /// 군단 부하도 같은 끝처리를 지난다(<c>0x10071e20</c> 은 부하면 차례 끝 검사만 건너뜀, 감사3 L1).
    /// </summary>
    internal void PayWorkCost(UnitState a, WorkData w, bool free = false)
    {
        if (a.Data is not { } cost || host._db is not { } db2) return;
        // +0xa4 ≠ 0(사건 207·909)이면 TP·SOUL·HP 비용을 통째로 건너뛰고(0x10076384 jne 0x10076586) SOUL 증가만 한다(감사5 B2).
        if (free) { AddSoul(a, w.Kind switch { 0 => db2.N(26), 1 => db2.N(27), 2 => db2.N(28), 3 => db2.N(29), _ => 0 }); return; }
        a.Tp -= TpCostFor(a, cost, w.Id);
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"work {w.Id} by {a.ChrCode}{(a.LeaderIndex >= 0 ? " (부하)" : "")}: TP {db2.WorkTpCost(cost, w.Id)} × (100{a.Status(20):+#;-#;+0})% = {TpCostFor(a, cost, w.Id)}, 남은 TP {a.Tp}" + Environment.NewLine);
        a.Soul = Math.Max(0, a.Soul - SoulCostFor(a, cost, w.Id));
        // 행동 뒤 SOUL 증가(0x10076586 점프표 0x100765ec): 종류 0 → Num26 · 1 → Num27 · 2 → Num28 · 3 → Num29 · 4·5·7 → 0(ba-15 재확인).
        AddSoul(a, w.Kind switch { 0 => db2.N(26), 1 => db2.N(27), 2 => db2.N(28), 3 => db2.N(29), _ => 0 });
        int hp = db2.WorkHpCost(cost, w.Id);
        if (hp > 0 && cost.JobId != 37 && a.Hp > 0) a.Hp = Math.Max(1, a.Hp - hp);   // 직업 37 은 SOUL·HP 소비 면제(0x100764d3), TP 는 뺀다
    }

    internal void ApplyWork(UnitState a, WorkData w, UnitState t, List<UnitState> dying)
    {
        if (host._db == null || a.Data == null || t.Data == null || t.Hp <= 0) return;
        // 판정에는 상태이상까지 얹은 능력치를 쓴다(1 DEX −1 · 40 DEP −1 · 30~32 보정).
        // 군단 부하의 DEX 는 대장 것이다(0x1007ae50, 감사3 L2) — CombatData 가 바꿔 준다.
        var (amount, result, crit) = host._db.Resolve(host._rng, CombatData(a)!, a.Tp, host.TuningScr.AttackSoul(w, a.Soul), CombatData(t)!, t.Tp, t.Hp, t.MaxHp, w, t.Stance, a.Status(29));

        if (result == 1)
        {
            int before = t.Hp;
            t.Hp = Math.Min(t.MaxHp, t.Hp + amount);
            // 회복은 떠오르지 않고 옛 HP 에서 새 HP 로 세어 올라간다(노랑).
            ShowNumber(t, host._db.T(159), HealColor2, rise: false, count: (before, t.Hp));
            ApplyAilments(a, t, w);
            MarkBuffed(a, t, w);
            return;
        }
        // 격려(어빌리티 10) — 대상 SOUL 을 위력만큼 올린다(0x10091ae0). 공통 함수를 안 거쳐 19(소울 정지)도 무시하고, 최대치에서 자른다.
        if (w.AbilityId == EncourageAbility)
        {
            int before = t.Soul;
            t.Soul = Math.Min(t.MaxSoul, t.Soul + Math.Max(0, (int)w.Power));
            ShowNumber(t, $"{host._db.T(41)} +{t.Soul - before}", HealColor2);
            if (Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"encourage work {w.Id}: {a.ChrCode} → {t.ChrCode} SOUL {before} → {t.Soul} (위력 {w.Power})" + Environment.NewLine);
            MarkBuffed(a, t, w);
            return;
        }
        // 쇼크(어빌리티 39) — 피해 판정 없이 대상 SOUL 을 위력만큼 깎는다(0x100ae140, 격려의 반대). 전에는 아무 효과가 없었다(fg-21 ⑥).
        if (w.AbilityId == ShockAbility)
        {
            int drain = Math.Min(Math.Max(0, (int)w.Power), t.Soul);
            t.Soul -= drain;
            ShowNumber(t, $"{host._db.T(41)} -{drain}", MissColor);
            PlayHitReaction(t, damaged: false);
            return;
        }
        // 블랙홀(115) — 피해 판정이 없다. 화면 HP 가 위력(500~1000)보다 적은 유닛만 HP 0(0x1008d85f~0x1008d885, ba-20 D2).
        // 명중 굴림·상태·맞음 SOUL·처치 보상이 없고(1016 을 안 보냄 — 가설), 시전자·아군도 범위에 든다(범위 대상 5).
        if (w.AbilityId == BlackHoleAbility)
        {
            if (t.Hp < w.Power)
            {
                t.Hp = 0;
                if (!SurvivesFatal(t)) dying.Add(t);
            }
            return;
        }
        if (!w.IsDamage)
        {
            // 보조기도 빗나가면 「Miss」, 들어가면 맞음 동작(원본 반응표 — 전에는 아무 표시가 없었다).
            // 명중 굴림은 거는 함수(0x1007bcc0) 안에 있다 — 그 결과가 3(명중·레벨 조건 실패)이면 Miss 만 뜨고 맞음 동작·EXP 가 없다(ba-20 C5).
            if (result == 3 || (!ApplyAilments(a, t, w) && t != a))   // 종류 2·3(큐어 따위)은 상태이상만 건다. 제게 거는 것(방어·회피 자세)은 Miss 를 안 띄운다
            {
                ShowNumber(t, host._db.T(42) is { Length: > 0 } miss ? miss : "Miss", MissColor);
                return;
            }
            MarkBuffed(a, t, w);
            // 맞음 동작은 없다 — 거는 함수는 0(성공)·3(실패)만 돌려줘 「2 → 맞음 동작」 가지(0x1007a2ba)가 죽은 코드다(ba-20 K6).
            return;
        }
        // 상태이상 보정(7·13·14)은 <b>판정 함수 안에서</b> 끝나고, 「Miss」는 그 뒤에 남은 양으로 가른다(0x10078e60).
        amount = ScaleDamage(a, t, AilmentDamage(a, t, amount));
        if (result == 3 || amount <= 0)
        {
            ShowNumber(t, host._db.T(42) is { Length: > 0 } m ? m : "Miss", MissColor);
            // 빗나가도 원본은 상태이상을 따로 굴리고(0x1007a285 → 0x1007bcc0) 피격 가속(10)도 건다(0x10079f59, 결과 2·3 모두) — fg-21 ⑪.
            RushTp(t);
            ApplyAilments(a, t, w);
            return;
        }

        if (Trace && IsFoeSide(a) && !IsFoeSide(t))
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"foe hit {a.ChrCode} → {t.ChrCode}: {amount} (HP {t.Hp}/{t.MaxHp}, 난이도 {_difficulty})" + Environment.NewLine);
        t.LastHitBy = a;                        // 맞았을 때만 적는다(빗나가면 그대로) — 원본 0x10079990
        t.Hp = Math.Max(0, t.Hp - amount);
        RushTp(t);
        ShowNumber(t, $"{host._db.T(159)} {amount}", DamageColor);
        PlayHitReaction(t, damaged: true);
        if (crit) PlayCritFlash();
        AddSoul(t, amount / Math.Max(1, host._db.N(43)));
        PlayHurtCry(t);
        ApplyAilments(a, t, w);
        Counterattack(a, t, amount);
        if (t.Hp > 0) return;
        // 쓰러뜨린 타에는 불꽃(Obs 73)이 한 장 더 뜬다 — 모션 0(0x10079b84) + 무작위 한 장(0x10079c0a), ba-21 T6.
        if (w.IsDamage) { var (kx, ky) = host.Btl.UnitFoot(t); _effects.Add((HitEffectObs, 0, host._lastTime, kx, ky - HitEffectLift)); }
        // 처치(메시지 1016)는 HP 가 0 이 된 순간 공격자에게 간다 — 47(전투불능 방지)로 살아나도 보상은 받는다(0x10079ab8).
        // SOUL 은 때린 사람이, 경험치는 군단 부하가 쓰러뜨렸으면 <b>대장</b>이 받는다(0x10079b14).
        // 처치 보상(1016)은 받는 유닛에게 SOUL +Num30 과 EXP 를 같이 준다(0x1007214f) — 부하가 쓰러뜨리면 둘 다 대장 몫(ba-20 K4).
        AddSoul(a.LeaderIndex >= 0 && a.LeaderIndex < host._units.Length ? host._units[a.LeaderIndex] : a, host._db.N(30));
        GainKillExp(a.LeaderIndex >= 0 && a.LeaderIndex < host._units.Length ? host._units[a.LeaderIndex] : a, t);
        if (SurvivesFatal(t)) return;
        dying.Add(t);
    }

    /// <summary>10(피격 가속) — 맞으면(빗나가도) TP 가 값% × STP 만큼 앞당겨진다(0x10079f59: 값 × 최대TP ÷ 제수 ÷ 100). 전에는 값% × 제수였다.</summary>
    internal void RushTp(UnitState t)
    {
        if (t.Status(10) is var rush and > 0) t.Tp = Math.Min(t.MaxTp, t.Tp + rush * t.Stp / 100);
    }

    /// <summary>자기 자리에 쓰는 work(모드 0·2)면 겨냥 없이 바로 쓴다.</summary>
    internal bool UseSelfCentredWork(WorkData w)
    {
        if (!w.SelfCentred) return false;
        // 적만 맞는 제자리 범위기(피드백 — 범위 방식 +0x1e = 1)는 범위 안에 적이 없으면 안 쓴다(사용자 요청). 원본이 막는지는 확인하지 않았다 —
        // 헛쓰면 SOUL·TP 만 잃어서 막아 둔다. 내가 고를 때만 — AI 는 대상이 있을 때만 고른다.
        if (w.AreaMode == 1 && WorkTargets(w, host._units[_turn], host._units[_turn].Col, host._units[_turn].Row).Count == 0)
        {
            host.Toast("범위 안에 적이 없습니다");
            return true;
        }
        ConsumeTargetItem();          // 겨누지 않는 아이템(라이징스톰·블리자드캡슐)도 쓰면 하나 준다(0x100698af) — CancelTargeting 앞이어야 한다
        CancelTargeting();
        _routine = UseWorkRoutine(_turn, w, -1, host._units[_turn].Col, host._units[_turn].Row, []);
        return true;
    }

    internal static Facing FacingToward(int fromCol, int fromRow, int toCol, int toRow)
    {
        int dx = toCol - fromCol, dy = toRow - fromRow;
        if (Math.Abs(dx) >= Math.Abs(dy)) return dx >= 0 ? Facing.Right : Facing.Left;
        return dy >= 0 ? Facing.Down : Facing.Up;
    }

    internal void CheckOutcome()
    {
        if (_outcome.Length > 0) return;
        // 먼저 이벤트 스크립트 — 「몇 턴 버티기」·「누구를 지키기」처럼 전멸 말고 다른 조건으로 끝나는 전투가 있다.
        _eventCheckDue |= 1 << 2;               // 쓰러짐은 행동 끝에서 본다
        RunEvents();
        // 방금 켜진 사건이 아직 도는 중이면 그것이 끝나기를 기다린다 — 전멸 사건(대사 → 행동 6 필드)이 도는 사이에 기본 「승리」를 내면
        // 사건이 정한 행선지를 잃고 Btl 자료의 다음 전투로 샜다. Btl 0147 사건 3(→ Fld 0073 → 0074 깃발 99=6)을 건너뛰고 Btl 0312 로 가
        // 델라리움 연구소로(0148)가 안 열렸다(사용자 보고). 틱마다 다시 보니 사건이 끝난 뒤에 판정한다.
        if (_outcome.Length > 0 || EventsBusy) return;
        // 머리 워드 9 가 0 인 전투는 엔진이 전멸을 아예 안 본다(0x1006ed10 첫머리) — 승패는 스크립트(행동 11·10·6)만 낸다.
        // 적을 다 잡아도 조건(특정 칸 도달 따위)을 채워야 넘어간다. 전에는 전멸이면 끝내고 스크립트의 나가는 길을 대신 골랐다(fg-21 ⑬).
        if (!host._scene.EngineJudgesWipe)
        {
            // 원본은 여기서 AI 차례만 영영 돈다(0x1006ed6c — 시스템 메뉴도 못 여는 막다른 길, Btl 0079·0102·0252·0221).
            // 끝날 길이 없을 때만 패배로 닫는다(ba-20 F, 원본에 없는 편의): 편 4 가 맵에 섰던 전투에서 맵 위·대기 중인 편 3·4 가 하나도 없고
            // 틱·타이머로 끝나는 사건(0076·0083·0090)도 안 남았을 때.
            // 스크립트의 「아군 전멸」·「그 인물 사망」 사건(401[4]·200·201)이 먼저 터질 틈을 준다 — 그 조건이 5초(게임 시간) 이어질 때만 닫는다.
            bool dead = host._units.Any(u => u.Side == 4 && !u.Alive)
                        && !host._units.Any(u => u.Alive && u.Side is 3 or 4)
                        && !PendingTimedEnd();
            if (!dead) _softlockSince = -1;
            else if (_softlockSince < 0) _softlockSince = host._lastTime;
            else if (host._lastTime - _softlockSince >= 5)
            {
                _softlockSince = -1;
                _outcome = "패배 — 아군이 모두 쓰러졌습니다"; _outcomeAt = host._lastTime;
                TestRunTrace($"outcome softlock-lose btl {host._scene.Id} turnNo {_turnNo} t {host._lastTime:F1}");   // 시험 전용
            }
            return;
        }
        // 전장에 선 사람만 센다(원본도 맵 밖은 안 센다).
        // 패배는 <b>사람이 모는 편(편 4)</b>이 다 쓰러졌을 때다(0x1006ec00 — 세력 +8 조종 주체 0 인 편만) — 편 3 동맹이 남아도 진다.
        // 마지막 적과 마지막 아군이 같은 행동에서 쓰러지면 패배다 — 0x1006edb0 은 플레이어 0 을 먼저 본다(ba-20 F 덤).
        if (!host._units.Any(u => u.Alive && u.OnField && u.Side == 4))
        {
            _outcome = "패배 — 아군이 모두 쓰러졌습니다"; _outcomeAt = host._lastTime;
            TestRunTrace($"outcome wipe-lose btl {host._scene.Id} turnNo {_turnNo} t {host._lastTime:F1}");   // 시험 전용
        }
        else if (!host._units.Any(u => u.Alive && u.OnField && !u.IsAlly))
        {
            ScriptedDestinationOnWipe();
            _outcome = "승리 — 적을 모두 쓰러뜨렸습니다"; _outcomeAt = host._lastTime;   // 음악은 배너와 함께 16틀 뒤(UpdateOutcomeBanner, 사운드 B1)
            TestRunTrace($"outcome wipe-win btl {host._scene.Id} nextBattle {_eventNextBattle} nextField {_eventNextField} turnNo {_turnNo} t {host._lastTime:F1}");   // 시험 전용
        }
    }

    internal double _softlockSince = -1;

    /// <summary>아직 덜 터진 사건 가운데 틱·타이머 조건(2·3)으로 전투를 끝내는(행동 6·10·11) 것이 남았나.</summary>
    internal bool PendingTimedEnd()
    {
        var events = _events;
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.MaxFire > 0 && i < _eventFired.Length && _eventFired[i] >= e.MaxFire) continue;
            if (e.Conditions.Any(c => c.Code is 2 or 3) && e.Actions.Any(x => x.Code is 6 or 10 or 11)) return true;
        }
        return false;
    }

    /// <summary>배너·음악 없이 끝나는 결과(행동 10·6 의 결과 5·6, 행동 11[1] 의 결과 2) — 원본 상태 24 는 결과 1·4 만 그린다(0x1006afa0).</summary>
    internal bool _outcomeQuiet;

    /// <summary>조용한 결과가 저절로 넘어가는 때(초).</summary>
    internal double _outcomeLeaveAt;

    /// <summary>
    /// 이벤트 행동 11·10·6 이 적는 결과. <paramref name="quiet"/> 면 배너 없이 넘어간다 — 상태 24 는 결과와 상관없이 어둡게(하위 0) →
    /// 16틱 → 하위 2 에서 입력 또는 틱 &gt; 120 이라 <b>16 + 121 = 137틱</b>(감사4 C9). 배너 결과의 음악은 배너와 함께 건다(사운드 B1).
    /// </summary>
    internal void SetEventOutcome(bool win, bool quiet = false)
    {
        if (_outcome.Length > 0) return;
        _outcome = win ? "승리" : "패배";
        _outcomeAt = host._lastTime;
        _outcomeQuiet = quiet;
        // 시험 전용: 사건이 낸 결과를 남긴다(DUELDX_TESTRUN 일 때만).
        TestRunTrace($"outcome event btl {host._scene.Id} win {win} quiet {quiet} 사건 {_runningEvent} 줄 {_eventPc - 1} nextBattle {_eventNextBattle} nextField {_eventNextField} turnNo {_turnNo} t {host._lastTime:F1}");
        _outcomeLeaveAt = host._lastTime + (OutcomeBannerDelayTicks + OutcomeAutoTicks + 1) / TicksPerSecond;
    }

    // ── 표시 ─────────────────────────────────────────────────────────────────

    internal const uint HealColor = 0xFF60E060;

    /// <summary>
    /// 그 동작에서 판정이 나는 시각들(초, 동작 시작 기준) — 모션의 타격 키(종류 6) 간격을 그대로 쓰되
    /// 첫 타는 사용자가 원하는 대로 <b>0.05초</b>에 낸다. 타격 키가 없으면 한 번만.
    /// 다만 <b>원거리</b>(사거리 1칸 넘음)는 모션의 타격 키 그 틱에 낸다 — 총은 쏘는 컷에 타격 키가 있어서, 0.05초로 당기면
    /// 크리스티앙처럼 피해 숫자가 총을 쏘기 전에 떴다(사용자 보고).
    /// </summary>
    internal List<double> HitTimesFor(UnitState u, int action, bool ranged = false)
    {
        const double first = 0.05;
        if (!host._sprites.TryGetValue(u.ChrCode, out var sprite) || sprite.Clip(action, u.Facing) is not { Hits.Count: > 0 } clip)
            return [first];
        var starts = clip.Hits.Select(h => h.Start).OrderBy(t => t).ToList();
        double at = ranged ? Math.Max(first, starts[0] / TicksPerSecond) : first;
        return [.. starts.Select(t => at + (t - starts[0]) / TicksPerSecond)];
    }

    /// <summary>
    /// 그 인물 그림에 그 동작이 비어 있으면(키 없음) 대신 그릴 동작 — 7(쏘기·휘두르기), 그다음 5. 판정 자리는 원래 동작 목록대로 둔다.
    /// 원거리 기본공격(work 6)은 동작 9 를 부르는데, 팡테온가드(그림 1111)는 9 가 비어 있고 사격(소리 8·총구 불꽃 108)이 7 에 있어
    /// 공격 이펙트가 안 나왔다(사용자 보고: Btl 0154).
    /// </summary>
    internal int DrawnAction(UnitState u, int action)
    {
        if (!host._sprites.TryGetValue(u.ChrCode, out var sprite)) return action;
        bool Empty(int a) => sprite.Clip(a, u.Facing) is not { Keys.Count: > 0 };   // 키가 하나도 없는 동작(정지 한 장짜리는 둔다)
        if (!Empty(action)) return action;
        foreach (int alt in new[] { 7, 5 })
            if (alt != action && !Empty(alt)) return alt;
        return action;
    }

    internal void PlayAction(UnitState u, int action)
    {
        double seconds = host._sprites.TryGetValue(u.ChrCode, out var sprite) ? sprite.ActionSeconds(action, u.Facing) : 0;
        if (Trace)
        {
            var clip = sprite?.Clip(action, u.Facing);
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"{host._lastTime:F2} chr {u.ChrCode} action {action} facing {u.Facing} clip {(clip?.Id.ToString() ?? "none")} len {clip?.Length ?? 0} keys {clip?.Keys.Count ?? 0} seconds {seconds:F2}" + Environment.NewLine);
        }
        u.PlayAction(action, seconds > 0 ? seconds : 0.3);
        host.ScheduleActionSounds(u, action);
    }

    internal void Popup(UnitState u, string text, uint color, float size = 18)
    {
        var (x, y) = host.Btl.UnitFoot(u);
        _popups.Add((text, size, x, y - 70, host._lastTime, color));
    }

    internal void DrawPopups()
    {
        _popups.RemoveAll(p => host._lastTime - p.Start > 1.2);
        foreach (var (text, size, x, y, start, color) in _popups)
        {
            int rise = (int)((host._lastTime - start) * 30);
            var (_, w, _) = host.GetText(text, color, size);
            host.DrawText(text, x - w / 2 + 1, y - rise + 1, 0xFF000000, size);
            host.DrawText(text, x - w / 2, y - rise, color, size);
        }
    }

    /// <summary>인물 발밑의 HP(초록·빨강)·TP(노랑) 막대.</summary>
    internal void DrawGauges()
    {
        foreach (var u in host._units)
        {
            if (!u.Alive || !u.OnField || u.MaxHp <= 0) continue;   // 판 밖(증원 전·퇴장) 유닛의 막대가 빈 칸에 뜨지 않게(ba-20 G22)
            var (fx, fy) = host.Btl.UnitFoot(u);
            int w = TileW - 8, x = fx - w / 2, y = fy + TileH / 2 - 7;
            host.FillRect(x - 1, y - 1, w + 2, 7, 0xC0000000);
            host.FillRect(x, y, w * u.Hp / u.MaxHp, 3, u.IsAlly ? 0xFF50D060 : 0xFFE05050);
            if (u.MaxTp > 0) host.FillRect(x, y + 3, w * Math.Clamp(u.Tp, 0, u.MaxTp) / u.MaxTp, 2, 0xFFE8C040);
            // 아군이면 막대 왼쪽에 작은 원 안의 레벨(사용자 요청) — 적은 안 보인다.
            if (u.IsAlly && u.Data is { } d)
            {
                string lv = d.Level.ToString();
                var (_, tw, th) = host.GetText(lv, 0xFFFFFFFF, GaugeLevelFont);
                int r = Math.Max(5, (tw + 3) / 2), cx = x - 2 - r, cy = y + 2;
                host.FillCircle(cx, cy, r + 1, 0xE0000000);
                host.FillCircle(cx, cy, r, 0xE0305070);
                host.DrawText(lv, cx - tw / 2, cy - th / 2, 0xFFFFFFFF, GaugeLevelFont);
            }
        }
    }

    internal const float GaugeLevelFont = 7f;

    /// <summary>어빌리티 대상 고르는 중이면 사거리 칸(노랑)을 깐다.</summary>
    internal void DrawWorkRange()
    {
        if (_targetWork >= 0 && _targetIsBasicAttack && _attackCursor >= 0)
        {
            // 노리는 적 칸에 깜빡이는 빨간 테두리
            var t = host._units[_attackCursor];
            uint alpha = (uint)(160 + 95 * (0.5 + 0.5 * Math.Sin(host._lastTime * Math.PI * 4)));
            uint color = alpha << 24 | 0xFF3030;
            int cx = t.Col * TileW, cy = host.CellTop(t.Col, t.Row);
            for (int k = 0; k < 3; k++) host.StrokeRect(cx + k, cy + k, TileW - 2 * k, TileH - 2 * k, color);
            return;
        }
        if (_targetWork < 0 || _targetIsBasicAttack || _turn < 0 || Work(_targetWork) is not { } w) return;
        var u = host._units[_turn];
        // 원본 상태 11(어빌리티 대상 고르기, 분석-UI 「칸 깃발」): 사거리는 층 12 붉은색 (255,100,40)×74/256 = (73,28,11) 을
        // <b>가산</b>으로 칠하고 테두리는 같은 색 불투명 41×33. 겨눈 칸의 효과 범위는 층 0 주황 (255,170,40) → (73,49,11) 로 덧칠한다
        // (그리는 차례는 층 0 이 먼저라 주황이 이긴다).
        var aim = _attackCursor >= 0 ? (host._units[_attackCursor].Col, host._units[_attackCursor].Row) : _aimCell;
        var splash = aim is { } ac ? EffectCells(w, u, ac.Item1, ac.Item2) : [];
        for (int row = 0; row < host.Rows; row++)
            for (int col = 0; col < host.Cols; col++)
            {
                bool inRange = InWorkRange(w, u.Col, u.Row, col, row, u), inSplash = splash.Contains((col, row));
                if (!inRange && !inSplash) continue;
                PaintCell(col, row, inSplash ? SplashLayer : RangeLayer);
            }
        // 저절로 겨눈 대상(적 커서나 칸)에도 깜빡이는 빨간 테두리
        if (aim is { } a)
        {
            uint alpha = (uint)(160 + 95 * (0.5 + 0.5 * Math.Sin(host._lastTime * Math.PI * 4)));
            uint color = alpha << 24 | 0xFF3030;
            int cx = a.Item1 * TileW, cy = host.CellTop(a.Item1, a.Item2);
            for (int k = 0; k < 3; k++) host.StrokeRect(cx + k, cy + k, TileW - 2 * k, TileH - 2 * k, color);
        }
    }

    internal string TurnLine()
    {
        if (_turn < 0) return $"틱 {_tick} — 차례를 기다리는 중";
        var u = host._units[_turn];
        string help = !u.IsAlly ? "적군이 움직입니다"
            : _targetWork >= 0 ? (_targetIsBasicAttack ? "클릭·Enter 공격  Tab 다른 적  Esc 취소" : "노란 칸 클릭  Esc 취소")
            : $"{KeyBindings.KeyName(host._keys[KeyAction.MoveUp])}{KeyBindings.KeyName(host._keys[KeyAction.MoveLeft])}{KeyBindings.KeyName(host._keys[KeyAction.MoveDown])}{KeyBindings.KeyName(host._keys[KeyAction.MoveRight])} 걷기  {KeyBindings.KeyName(host._keys[KeyAction.Ring])} 링  {KeyBindings.KeyName(host._keys[KeyAction.Attack])} 공격  {KeyBindings.KeyName(host._keys[KeyAction.Rest])} 휴식  1~4 어빌리티  Esc 취소";
        return $"틱 {_tick}  {(u.IsAlly ? "아군" : "적군")} {host.UnitName(_turn)}  HP {u.Hp}/{u.MaxHp}  TP {u.Tp}/{u.MaxTp}  SOUL {u.Soul}   {help}";
    }

}
