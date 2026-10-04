using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 전투 이벤트 스크립트 — 「언제 이기고 지는지」를 정하는 것은 적을 다 잡는 것만이 아니다.
/// </summary>
/// <remarks>
/// 승패는 <b>두 갈래</b>로 정해진다([[분석-전투]]):
/// ① <c>Btl</c> 머리 워드 9 가 1 이면 엔진이 「한쪽 전멸」을 스스로 본다(파일 126개),
/// ② <b>모든</b> 전투에서 이벤트 스크립트가 조건을 보고 행동 <c>11</c>(승패)·<c>10</c>(다음 전투)·<c>6</c>(필드로)을 적는다.
/// 데모는 그동안 ① 만 했다 — 그래서 「몇 턴 버티기」나 「누구를 지키기」 같은 전투가 끝나지 않았다.
/// <para>
/// 이벤트 하나는 조건 여러 개를 <b>모두</b> 만족해야 터지고(AND, <c>0x10056574</c>), 최대 발동 수(<c>+0xc</c>)가 0 이 아니면 그만큼만 터진다.
/// 대상 지정(<c>0x1004eaa0</c>)은 <b>1~9999 = Chr 번호 · 10000+k = Btl 배치표 k번 · 20000+2f/2f+1 = 편 f 의 전부/누군가</b>
/// (편 차례는 4·3·0·1·2). 비교 연산자는 0 <c>==</c> · 1 <c>!=</c> · 2 <c>&lt;</c> · 3 <c>&lt;=</c> · 4 <c>&gt;</c> · 5 <c>&gt;=</c>.
/// </para>
/// 아직 안 만든 조건·행동은 <b>거짓/무시</b>로 둔다 — 잘못 터뜨리는 것보다 안 터뜨리는 쪽이 낫다.
/// 조건 <b>301</b>(한쪽이 다른 쪽을 지목)은 그 칸(<c>유닛+0xfc</c>)이 무엇을 담는지 아직 몰라 <b>300(맞닿음)</b> 으로 대신한다 —
/// 쓰임새가 「아군과 적이 처음 붙는 순간의 대사」라 결과가 거의 같다.
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    internal IReadOnlyList<BattleEvent> _events = [];
    internal int[] _eventFired = [];

    /// <summary>턴 수 — 이벤트 조건 1·3 이 보는 값. 새 차례가 올 때마다 오른다(<c>0x10067d36</c>).</summary>
    internal int _turnNo;

    /// <summary>전투 국소 변수 — 행동 100·101 이 고치고 조건 100 이 읽는다(<c>0x101b69a0</c>).</summary>
    internal readonly byte[] _battleVars = new byte[256];

    /// <summary>
    /// 이벤트 타이머 열 칸 — 행동 <c>900</c> 이 켜고 <b>턴이 하나 지날 때마다</b> 센다(<c>0x1004e9e0</c>).
    /// 조건 <c>2</c> 가 그 세기를 견준다. <c>Btl 0170</c> 이 「적을 다 잡은 뒤 20턴·40턴」에 쓴다.
    /// </summary>
    internal readonly int[] _eventTimer = new int[10];

    internal readonly bool[] _eventTimerRun = new bool[10];

    /// <summary>이벤트가 정한 다음 전투 — 0 이면 <c>Btl</c> 자료의 값(또는 모세스)으로 간다.</summary>
    internal int _eventNextBattle;

    /// <summary>이벤트 행동 6 이 정한 다음 필드 — 0 이면 모세스로 간다.</summary>
    internal int _eventNextField;

    /// <summary>그 전투의 이벤트를 읽어 둔다.</summary>
    internal void LoadEvents(int battleId)
    {
        _events = [];
        _eventFired = [];
        // 돌던 사건 자리를 반드시 비운다 — 안 비우면 앞 전투의 번호가 남아, 사건이 더 적은 전투를 읽었을 때
        // StepEvent 의 _events[_runningEvent] 가 목록 밖을 짚어 죽는다(사용자 보고 crash).
        _runningEvent = -1;
        _eventPc = 0;
        _eventWaitUntil = 0;
        _talkNoWait = false;
        _eventMoveUntil = 0;
        _pendingExits.Clear();
        _eventRoutine = null;
        _eventRoutineFree = _eventCamPending = false;
        host._talkSkip = false;
        _turnNo = 0;
        _eventFoundA = _eventFoundB = null;
        _eventNextBattle = 0;
        _eventNextField = 0;
        _eventSoundSeconds = 0;                  // 앞 전투에서 늦게 풀린 소리가 이 전투의 이벤트를 멈추지 않게
        Array.Clear(_battleVars);
        Array.Clear(_eventTimer);
        Array.Clear(_eventTimerRun);
        try
        {
            var files = GameFiles.FromFolder(AssetsFolder.Find("data"));
            if (files.Read("Btl", $"{battleId:D4}.btl") is not { } bytes) return;
            if (BattleEvents.Parse(bytes, out _) is not { } events) return;
            _events = events;
            _eventFired = new int[events.Count];
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { }
    }

    /// <summary>지금 돌고 있는 이벤트 — 없으면 −1. 도는 동안 전투가 통째로 멈춘다(<c>0x10066197</c>).</summary>
    internal int _runningEvent = -1;
    internal int _eventPc;
    internal double _eventWaitUntil;
    /// <summary>방금 띄운 말풍선(601)을 안 기다리고 다음 줄로 가는 중 — 뒤따르는 줄이 2(틱 기다리기)일 때.</summary>
    internal bool _talkNoWait;
    /// <summary>사건이 건 걸음들이 다 끝나는 때 — 행동 1 이 이때까지 기다린다.</summary>
    internal double _eventMoveUntil;

    /// <summary>
    /// 사건 줄이 건 기술(207 보스 필살기 · 909 폭주) — 끝날 때까지 사건이 <b>그 줄에 머문다</b>. 원본 207(<c>0x10052400</c>)은 단계 0 기술 →
    /// 1 모두 설 때까지(<c>0x1006e320</c>) → 2 쓰러짐(<c>0x1004e6d0</c>) → 3 물체(<c>0x1004e850</c>)를 다 거쳐야 <c>0x10056380</c> 이 줄을 넘긴다.
    /// 전에는 <c>_routine</c> 뒤에 붙였는데 사건 중엔 <c>_routine</c> 이 안 돌아(UpdateTurn 의 EventsBusy) 필살기가 사건이 <b>끝난 뒤</b>에 나갔고,
    /// 뒤에 결과(11)·필드(6) 줄이 있으면 아예 안 나갔다(감사5 B1·B4). <see cref="StepEvent"/> 가 틀마다 한 번 돌린다.
    /// </summary>
    /// <summary>사건이 건 걸음이 끝나는 데 드는 시간 — 높이 차 칸은 6·8·10틱, 순간이동꾼(이동 종류 1)은 길이와 상관없이 64틱.</summary>
    internal double WalkSeconds(UnitState u, int fromCol, int fromRow, IReadOnlyList<(int Col, int Row)> walk)
    {
        if (u.Data is { MoveKind: 1 }) return (BlinkFadeTicks + 2 * BlinkHiddenTicks + 18 + 1) / TicksPerSecond;
        int ticks = 0;
        foreach (var (c, r) in walk) { ticks += host.StepTicksBetween(fromCol, fromRow, c, r); (fromCol, fromRow) = (c, r); }
        return ticks / TicksPerSecond;
    }

    /// <summary>그 칸이 다른 유닛으로 차 있으면 가장 가까운(맨해튼, 반경 6까지) 설 수 있는 칸. 못 찾으면 그 칸 그대로.</summary>
    internal (int Col, int Row) FreeCellNear(int col, int row, UnitState who)
    {
        if (!host._units.Any(x => x != who && x.Alive && x.OnField && x.Col == col && x.Row == row)) return (col, row);
        for (int radius = 1; radius <= 6; radius++)
            for (int dy = -radius; dy <= radius; dy++)
                foreach (int dx in new[] { -(radius - Math.Abs(dy)), radius - Math.Abs(dy) }.Distinct())
                {
                    int c = col + dx, r = row + dy;
                    if ((uint)c < host.Cols && (uint)r < host.Rows && !host._units.Any(x => x != who && x.Alive && x.OnField && x.Col == c && x.Row == r) && CanStand(c, r, who)) return (c, r);
                }
        return (col, row);
    }

    /// <summary>행동 201 로 걸어 나가는 중인 유닛 — 다 걸으면 판에서 뺀다.</summary>
    internal readonly List<(UnitState Unit, double At, int Col, int Row, bool WithFollowers)> _pendingExits = [];

    internal void LeaveField(UnitState u, int col, int row, bool withFollowers)
    {
        u.ResetTo(col, row);
        if (u.Hp <= 0) u.Hp = 1;   // HP 0 으로 물러난 보스가 판 밖에서 죽은 것으로 처리되지 않게
        u.OnField = false;
        if (!withFollowers) return;
        int leader = Array.IndexOf(host._units, u);
        foreach (var follower in host._units.Where(f => f.LeaderIndex == leader)) follower.OnField = false;
    }

    /// <summary>걸어 나가던 유닛이 다 걸었으면(또는 전투 결과가 났으면) 판에서 뺀다 — 매 틀 부른다.</summary>
    internal void StepPendingExits()
    {
        // 승패 판정만 행동(쓰러지는 동작)·레벨업 창 뒤로 미룬다 — 그 도중에 승패가 서면 루틴이 멈춰 쓰러짐·레벨업이 건너뛰어진다.
        // 판에서 빼는 것은 미루지 않는다(다 걸어 나간 유닛이 같은 AI 차례에 또 치거나 맞지 않게).
        bool hold = _outcome.Length == 0 && (_routine != null || LevelUpOpen);
        if (_exitOutcomeDue && !hold) { _exitOutcomeDue = false; CheckOutcome(); }
        if (_pendingExits.Count == 0) return;
        var done = _pendingExits.Where(x => !(host._lastTime < x.At && _outcome.Length == 0 && x.Unit.Alive && x.Unit.IsBusy)).ToList();
        if (done.Count == 0) return;
        _pendingExits.RemoveAll(done.Contains);   // 먼저 지운다 — CheckOutcome 이 사건을 돌려 이 목록을 바꿀 수 있다
        bool left = false;
        foreach (var (u, _, col, row, withFollowers) in done)
        {
            if (!u.Alive || Array.IndexOf(host._units, u) < 0) continue;
            LeaveField(u, col, row, withFollowers);
            left = true;
        }
        if (!left) return;
        _eventCheckDue |= (1 << 2) | (1 << 1);   // 「그 유닛이 없다」 조건·전멸 판정을 다시 본다
        if (hold) _exitOutcomeDue = true;
        else CheckOutcome();
    }

    /// <summary>걸어 나간 유닛 때문에 승패를 다시 봐야 한다 — 루틴·레벨업 창이 끝나면 본다.</summary>
    internal bool _exitOutcomeDue;

    internal IEnumerator<bool>? _eventRoutine;
    internal double _eventRoutineStepAt = -1;

    /// <summary>행동 500 이 튼 소리를 푸는 중인가 — 다 풀릴 때까지 이벤트를 멈춘다(원본은 소리 개체의 +0x58 이 0 이 될 때까지 기다린다).</summary>
    internal volatile bool _eventSoundLoading;

    /// <summary>다 푼 소리의 길이(초) — 배경 실이 채우고 <see cref="StepEvent"/> 가 기다림으로 바꾼다. 0 이면 없음.</summary>
    internal volatile float _eventSoundSeconds;

    /// <summary>이벤트가 돌거나 대사가 떠 있으면 전투를 멈춘다.</summary>
    internal bool EventsBusy => _runningEvent >= 0 || host._talk != null;

    /// <summary>조건이 다 맞는 이벤트를 하나 켠다. 결과가 정해지면 더 보지 않는다.</summary>
    /// <summary>
    /// 적을 다 쓰러뜨려 이기는 순간 — 아직 안 돈 「다음 장소로 보내는」 사건(행동 6 필드 · 10 전투)이 있고 <b>턴·타이머 말고</b> 나머지 조건이
    /// 맞으면 그 행선지를 쓴다. 그 사건이 턴 수를 기다리는 동안 전멸 승리가 먼저 전투를 끝내 이야기 사슬이 끊겼다 —
    /// 샤이닝 스타 <c>Btl 0137</c> 사건 5(턴 &gt; 5 · 열쇠 10006 없음 → <c>Fld 0055</c> → 행동 11 챕터 끝)를 건너뛰어 챕터가 안 끝났다(사용자 보고).
    /// </summary>
    internal void ScriptedDestinationOnWipe()
    {
        for (int i = 0; i < _events.Count; i++)
        {
            var e = _events[i];
            if (e.Conditions.Count == 0 || (e.MaxFire > 0 && _eventFired[i] >= e.MaxFire)) continue;
            if (e.Actions.FirstOrDefault(a => a.Code is 6 or 10) is not { } go) continue;
            if (!e.Conditions.Where(c => c.Code is not (1 or 2 or 3)).All(EventCondition)) continue;
            _eventFired[i]++;
            int to = go.Args.Length > 0 ? go.Args[0] : 0;
            if (go.Code == 6) { _eventNextBattle = 0; _eventNextField = to; }
            else _eventNextBattle = to;
            return;
        }
        // 엔진이 전멸을 안 보는 전투(머리 워드 9 = 0)는 원본에서 적을 다 쓰러뜨려도 끝나지 않고 스크립트 조건(특정 칸 도달 따위)을 채워야 넘어간다.
        // 우리는 전멸이면 승리로 끝내므로, 그때 스크립트의 나가는 길(행동 6 필드·10 전투)을 대신 탄다 — 안 그러면 뒤 필드를 건너뛴다.
        // Btl 0084 「벨로스」는 아군이 (6~11, 0~2) 에 들어가야 Fld 0222(챕터 끝)로 가는데, 적을 다 잡으면 그냥 모세스로 돌아가 챕터 22 가 안 끝났다(사용자 보고).
        // 아군 전멸(401 [4])이 조건인 나가는 길은 패배 쪽이라 뺀다.
        if (host._scene.EngineJudgesWipe) return;
        for (int i = 0; i < _events.Count; i++)
        {
            var e = _events[i];
            if (e.Conditions.Count == 0 || (e.MaxFire > 0 && _eventFired[i] >= e.MaxFire)) continue;
            if (e.Actions.FirstOrDefault(a => a.Code is 6 or 10) is not { } go) continue;
            if (e.Conditions.Any(c => c.Code == 401 && c.Args.Length > 0 && c.Args[0] == 4)) continue;
            _eventFired[i]++;
            int to = go.Args.Length > 0 ? go.Args[0] : 0;
            if (go.Code == 6) { _eventNextBattle = 0; _eventNextField = to; }
            else _eventNextBattle = to;
            if (Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"wipe exit: 사건 {i} 의 {(go.Code == 6 ? "필드" : "전투")} {to} 로" + Environment.NewLine);
            return;
        }
    }

    /// <summary>
    /// 이벤트를 볼 때(비트) — 원본 <c>CheckEvents(갈래)</c> 는 세 자리에서만 돈다: 1 레벨업 끝(0x10068270) · 2 행동 끝(0x100680a3) · 3 새 틱(0x10067d5c).
    /// 사건의 갈래(레코드 +0xe, <see cref="BattleEvent.Word2"/>)가 0 이면 셋 다, 아니면 그때만. 전에는 프레임마다 봤다(fg-22).
    /// </summary>
    internal int _eventCheckDue = 0xF;

    internal void RunEvents()
    {
        if (_events.Count == 0 || _outcome.Length > 0 || EventsBusy || _eventCheckDue == 0) return;
        for (int i = 0; i < _events.Count; i++)
        {
            var e = _events[i];
            if (e.Conditions.Count == 0) continue;                      // 조건이 없으면 안 터진다(0x10056574)
            if (e.MaxFire > 0 && _eventFired[i] >= e.MaxFire) continue;
            int branch = e.Word2 & 3;
            if (branch != 0 && (_eventCheckDue & (1 << branch)) == 0) continue;
            if (!e.Conditions.All(EventCondition)) continue;
            _eventFired[i]++;
            _runningEvent = i;
            _eventPc = 0;
            _eventWaitUntil = 0;
            _eventCamLine = (-1, -1);
            _eventCamPending = false;
            StepEvent();
            return;                                                     // 나머지는 이 사건이 끝난 뒤 같은 시점 표시로 다시 본다
        }
        _eventCheckDue = 0;
    }

    /// <summary>
    /// 돌고 있는 이벤트를 한 걸음 나아가게 한다 — 대사가 <b>한 줄씩</b> 나오는 것이 여기서 생긴다.
    /// </summary>
    /// <remarks>
    /// 진행기 <c>0x10056fb0</c>: 행동 <b>0</b> 은 다른 이벤트 부르기, <b>1</b> 은 <b>앞서 띄운 것이 끝날 때까지 기다리기</b>,
    /// <b>2</b> 는 틱 기다리기, <b>3</b> 은 중단이다. 나머지는 넣고 바로 다음 줄로 간다.
    /// 대사 줄 사이에 낀 <c>1</c> 이 「눌러서 넘길 때까지 멈춤」을 만든다.
    /// </remarks>
    internal void StepEvent()
    {
        while (_runningEvent >= 0)
        {
            if (_eventRoutine != null)
            {
                // 사건이 건 기술(207·909) — 한 틀에 한 번만 돌린다(StepEvent 는 한 틀에 여러 번 불릴 수 있다).
                if (_eventRoutineStepAt != host._lastTime)
                {
                    _eventRoutineStepAt = host._lastTime;
                    if (!_eventRoutine.MoveNext())
                    {
                        _eventRoutine = null;
                        _eventRoutineFree = false;
                        _eventCheckDue |= (1 << 2) | (1 << 1);          // 행동 끝 갈래 — 사건이 끝난 뒤 다시 본다
                        if (_outcome.Length > 0) { _runningEvent = -1; host._talkSkip = false; return; }
                    }
                }
                // 뒤에 행동 1 이 없으면 줄을 안 붙든다(슬롯만 잡는다, 0x10056fb0) — Btl 0145 사건 4 는 필살기 도중 말풍선이 뜬다(ba-20 V2 b).
                if (_eventRoutine != null && !_eventRoutineFree) return;
            }
            if (host._talk == null) _talkNoWait = false;
            if (host._talk != null && !_talkNoWait) return;                  // 대사가 떠 있으면 기다린다
            if (_eventSoundSeconds > 0) { _eventWaitUntil = host._lastTime + _eventSoundSeconds; _eventSoundSeconds = 0; }
            if (_eventSoundLoading && !host._talkSkip) return;               // 행동 500 의 소리를 아직 푸는 중
            if (_eventWaitUntil > host._lastTime && !host._talkSkip) return;      // 건너뛰는 중이면 기다림은 없는 셈
            if (_runningEvent >= _events.Count) { _runningEvent = -1; host._talkSkip = false; return; }   // 판이 바뀌었다
            var e = _events[_runningEvent];
            // 그 이벤트가 끝나면 건너뛰기도 끝난다 — 다음 장면 대사는 다시 보인다.
            if (_eventPc >= e.Actions.Count)
            {
                if (_eventRoutine != null) { _eventRoutineFree = false; return; }   // 돌던 기술이 끝나야 사건도 끝난다
                _runningEvent = -1; host._talkSkip = false; return;
            }

            var a = e.Actions[_eventPc++];
            // 원본 진행기는 0·1·2·3 만 직접 다루고 나머지는 슬롯에 넣은 채 다음 줄로 간다 — 줄을 붙드는 것은 뒤따르는 행동 1 뿐이다.
            // 그래서 「바로 뒤가 1 인가」로 가른다: 1 이면 전처럼 끝날 때까지 머물고, 아니면 걸어 두고 다음 줄로 간다(ba-20 V2).
            bool waitNext = _eventPc < e.Actions.Count && e.Actions[_eventPc].Code == 1;
            switch (a.Code)
            {
                case 0: break;                                          // 다른 이벤트 부르기 — 그 이벤트가 제 조건으로 돈다
                case 1:                                                 // 기다리기 — 앞서 띄운 것(걷기 200·202·214)이 끝날 때까지
                    // 원본 진행기(0x10056fb0)는 0·1·2·3 만 직접 다루고 나머지는 슬롯에 넣은 채 다음 줄로 간다 — 행동 1 없이 이어진
                    // 200/202 묶음(45개/31전투)은 함께 들어온다. 전에는 한 명씩 차례로 들어왔다(ba-20 V2).
                    _talkNoWait = false;                                // 행동 1 은 떠 있는 대사도 기다린다
                    if (host._talk != null) { _eventPc--; return; }
                    if (_eventRoutine != null) { _eventRoutineFree = false; _eventPc--; return; }   // 돌던 기술도
                    if (_eventCamPending && CameraBusy && !host._talkSkip) { _eventPc--; return; }      // 보내 둔 카메라도
                    _eventCamPending = false;
                    if (_eventMoveUntil > host._lastTime) _eventWaitUntil = _eventMoveUntil;            // 걷기·동작도
                    break;
                case 2:
                    _eventWaitUntil = host._lastTime + ((a.Args.Length > 0 ? a.Args[0] : 0)
                                                 | ((a.Args.Length > 1 ? a.Args[1] : 0) << 16)) / TicksPerSecond;
                    break;
                case 3:                                                 // 중단
                    if (_eventRoutine != null) { _eventRoutineFree = false; _eventPc--; return; }   // 돌던 기술은 끝까지
                    _runningEvent = -1; host._talkSkip = false; return;
                case 400 or 402 or 906 when !host._talkSkip && !waitNext:
                    // 뒤에 1 이 없는 카메라 줄(400 → 2 27곳 · 400 → 200 3곳 · 906 → 2 4곳)은 스크롤을 걸어만 두고 다음 줄과 같이 간다.
                    EventCameraWaits(a);
                    _eventCamLine = (-1, -1);
                    _eventCamPending = true;
                    RunEventAction(a);
                    break;
                case 400 or 402 or 600 or 601 or 906 when !host._talkSkip && EventCameraWaits(a):
                    _eventPc--;                                         // 카메라가 설 때까지 이 줄에 머문다(0x1006e850 · 0x100ead10)
                    return;
                case 600:
                    host.ShowTalk(box: true, a);
                    if (host._talk == null) break;                           // 건너뛰는 중이면 안 뜬다
                    if (TalkRidesOn(e)) { _talkNoWait = true; break; }
                    _talkNoWait = false;
                    return;
                case 601:
                    host.ShowTalk(box: false, a);
                    if (host._talk == null) break;
                    // 「601 → 2[틱]」 꼴(행동 1 없이)은 클릭을 안 기다린다 — 말풍선이 뜬 채 틱만 세고 다음 줄로 간다(진행기 0x10056fb0 은 1 만 기다린다,
                    // ba-20 V2: 11곳/7전투, 대표 Btl 0145 사건 4 — 필살기 도중 말풍선). 말풍선은 다음 601 이 덮거나 120틱 뒤 저절로 닫힌다.
                    // 1 없이 다른 행동이 이어지는 22곳(601 → 200 0335 · 601 → 214 0161 · 601 → 202 0139·0288 · 600 → 212 0278·0296 …)도 같다(V2 c).
                    if (TalkRidesOn(e)) { _talkNoWait = true; break; }
                    _talkNoWait = false;                                // 앞 말풍선의 「안 기다림」이 이 대사로 새지 않게
                    return;
                case 207 or 909 when _eventRoutine != null:
                    _eventRoutineFree = false;                          // 앞 기술이 아직 돈다 — 끝난 뒤 이 줄을 다시 본다
                    _eventPc--;
                    return;
                default:
                    bool hadRoutine = _eventRoutine != null;
                    RunEventAction(a);
                    if (_outcome.Length > 0) { _eventRoutine = null; _eventRoutineFree = false; _runningEvent = -1; host._talkSkip = false; return; }
                    if (!hadRoutine && _eventRoutine != null) _eventRoutineFree = !waitNext && !host._talkSkip;
                    break;
            }
        }
    }

    /// <summary>
    /// 방금 띄운 대사가 클릭을 안 기다리고 다음 줄과 같이 가나 — 바로 뒤가 행동 1 이 아닐 때. 다만 뒤가 또 대사(창이 하나라 겹쳐 못 띄운다)거나
    /// 전투 결과(6·10·11 — 원본은 말풍선이 뜨자마자 전투가 끝난다, 0262 한 곳)거나 사건의 끝이면 읽을 수 있게 기다린다(일부러 둔 차이).
    /// </summary>
    internal bool TalkRidesOn(BattleEvent e)
    {
        // 곧바로 끝나는 줄(변수·깃발 100~103 따위)은 건너뛰며 본다 — 「601 → 102 → 11」(Btl 0285 사건 8)도 결과 앞이다.
        for (int pc = _eventPc; pc < e.Actions.Count; pc++)
        {
            int code = e.Actions[pc].Code;
            if (code is >= 100 and <= 103) continue;
            return code is not (1 or 3 or 6 or 10 or 11 or 600 or 601);
        }
        return false;
    }

    /// <summary>사건이 건 기술(207·909)이 줄을 안 붙들고 도는 중인가 — 뒤에 행동 1 이 없을 때.</summary>
    internal bool _eventRoutineFree;

    /// <summary>줄을 안 붙들고 보내 둔 카메라가 있나 — 다음 행동 1 이 설 때까지 기다린다.</summary>
    internal bool _eventCamPending;

    /// <summary>카메라 명령을 건 이벤트 줄 — (사건, 줄 번호). 같은 줄에 다시 오면 명령은 안 걸고 멈췄는지만 본다.</summary>
    internal (int Event, int Pc) _eventCamLine = (-1, -1);

    /// <summary>
    /// 이벤트 줄의 카메라 — 처음 오면 명령을 걸고, 카메라가 서기 전까지 true(그 줄에 머문다). 설 일이 없으면 false.
    /// 400(0x100531f0): 큐를 비우고 칸 가운데 · 402(0x10053350): 살아 판 위에 있으면 그 유닛(부하면 대장)을 <b>한 번</b> 가운데(따라가기 아님),
    /// 죽었거나 없으면 곧바로 다음 줄 · 600·601(0x100537d0·0x10053b62): 말하는 이(부하면 대장) 가운데 뒤 창 ·
    /// 906(0x10055810): 단계 0 카메라 → 스크롤이 끝난 뒤 단계 1 강조(감사4 C15·C16).
    /// </summary>
    internal bool EventCameraWaits(ScriptCommand a)
    {
        short A(int i) => i < a.Args.Length ? a.Args[i] : (short)0;
        var line = (_runningEvent, _eventPc);   // _eventPc 는 이미 다음 줄을 가리킨다 — 이 줄마다 하나
        if (_eventCamLine == line)
        {
            if (CameraBusy) return true;
            _eventCamLine = (-1, -1);
            return false;
        }
        UnitState? LeaderOf(UnitState u) => u.LeaderIndex >= 0 && u.LeaderIndex < host._units.Length ? host._units[u.LeaderIndex] : u;
        switch (a.Code)
        {
            case 400:
                _camGoal = null;                                        // 큐 비우기(0x1006e830)
                CenterOnCell(A(0), A(1));
                break;
            case 402:
            {
                if (EventTargets(A(0), out _).FirstOrDefault() is not { Alive: true, OnField: true } u) return false;
                CenterOnUnit(LeaderOf(u)!);
                break;
            }
            case 600 or 601:
            {
                int speaker = host.TalkSpeaker(A(0));
                if ((uint)speaker >= host._units.Length || host._units[speaker] is not { Alive: true, OnField: true } s) return false;
                CenterOnUnit(LeaderOf(s)!);
                break;
            }
            case 906:
            {
                int cx = (A(0) + A(2) + 1) / 2, cy = (A(1) + A(3) + 1) / 2;
                CenterOnCell(cx, cy);
                break;
            }
            default: return false;
        }
        if (!CameraBusy) return false;                                  // 이미 그 자리 — 곧바로
        _eventCamLine = line;
        return true;
    }

    internal static bool Compare(int a, int op, int b) => op switch
    {
        0 => a == b,
        1 => a != b,
        2 => a < b,
        3 => a <= b,
        4 => a > b,
        _ => a >= b,
    };

    /// <summary>원본 방향 번호(0 위·1 왼·2 아래·3 오른) 그대로 바라보는 쪽.</summary>
    internal static Facing DirectionFacing(int direction) => (direction & 3) switch
    {
        0 => Facing.Up,
        1 => Facing.Left,
        2 => Facing.Down,
        _ => Facing.Right,
    };

    /// <summary>들어오는 쪽(0 위·1 왼·2 아래·3 오른)을 <b>바라보는 쪽</b>으로 — 들어온 쪽의 반대를 본다.</summary>
    internal static Facing EdgeFacing(int edge) => (edge & 3) switch
    {
        0 => Facing.Down,
        1 => Facing.Right,
        2 => Facing.Up,
        _ => Facing.Left,
    };

    /// <summary>편 번호를 이벤트가 쓰는 차례(4·3·0·1·2)에서 꺼낸다.</summary>
    internal static readonly int[] EventSideOrder = [4, 3, 0, 1, 2];

    /// <summary>조건 300·301 이 찾아 낸 두 사람 — 대사가 <c>20010</c>·<c>20011</c> 로 이들을 가리킨다.</summary>
    internal UnitState? _eventFoundA, _eventFoundB;

    /// <summary>조건 200·203·204·402 가 찾은 사람도 20010 이 된다(0x101b699c 에 적는 조건들) — 20011 은 그대로.</summary>
    internal void RememberFound(UnitState? found) { if (found != null) _eventFoundA = found; }

    /// <summary>행동 906 이 초록으로 칠하는 사각형과 그 끝나는 때.</summary>
    internal (int X1, int Y1, int X2, int Y2)? _highlightRect;
    internal double _highlightUntil;

    /// <summary>행동 200 의 들어오는 변(0 위·1 왼·2 아래·3 오른) 가장자리 칸 — 목표 칸과 같은 열/줄.</summary>
    /// <summary>그 유닛을 변(0 위·1 왼·2 아래·3 오른) 밖 <paramref name="px"/> 픽셀에서 걸어 들어오게 한다.</summary>
    internal static void BeginEdgeEntry(UnitState u, int edge, double px)
    {
        switch (edge & 3)
        {
            case 0: u.BeginEntry(0, -px); break;
            case 1: u.BeginEntry(-px, 0); break;
            case 2: u.BeginEntry(0, px); break;
            default: u.BeginEntry(px, 0); break;
        }
    }

    internal (int Col, int Row) EdgeCell(int edge, int col, int row) => (edge & 3) switch
    {
        0 => (Math.Clamp(col, 0, host.Cols - 1), 0),
        1 => (0, Math.Clamp(row, 0, host.Rows - 1)),
        2 => (Math.Clamp(col, 0, host.Cols - 1), host.Rows - 1),
        _ => (host.Cols - 1, Math.Clamp(row, 0, host.Rows - 1)),
    };

    /// <summary>
    /// 조건 300·301 이 쓰는 대상 고르기 — <b>죽은 사람도 든다</b>(걸러내기 갈래가 0 이라 NULL 검사뿐)이고,
    /// 편 코드는 <b>홀수만</b> 받는다(짝수는 늘 거짓, <c>0x1005022c</c>).
    /// </summary>
    internal List<UnitState> EventFighters(int value)
    {
        if (value is >= 20000 and <= 20009)
            return (value - 20000) % 2 == 0
                ? []
                : [.. host._units.Where(x => x.Side == EventSideOrder[(value - 20000) / 2])];
        if (value == 20010) return _eventFoundA is null ? [] : [_eventFoundA];
        if (value == 20011) return _eventFoundB is null ? [] : [_eventFoundB];
        if (value >= 10000) return [.. host._units.Where(x => x.Record == value - 10000)];
        return value > 0 ? [.. host._units.Where(x => x.ChrCode == value)] : [];
    }

    /// <summary>대상 지정 값이 가리키는 인물들. 편을 가리키면 그 편 전부.</summary>
    internal List<UnitState> EventTargets(int value, out bool wholeSide)
    {
        wholeSide = false;
        if (value >= 20000)
        {
            int k = (value - 20000) / 2;
            wholeSide = (value - 20000) % 2 == 0;      // 짝수 = 「그 편 전부/아무도」, 홀수 = 「누군가」
            if (k >= EventSideOrder.Length) return [];
            int side = EventSideOrder[k];
            return [.. host._units.Where(u => u.Side == side)];
        }
        // 10000+N 은 Btl 레코드 번호다(빈 칸을 걸러 낸 뒤의 배열 자리가 아니다).
        if (value >= 10000) return [.. host._units.Where(u => u.LeaderIndex < 0 && !u.Detached && u.Record == value - 10000)];
        return value > 0 ? [.. host._units.Where(u => u.ChrCode == value)] : [];
    }

    internal bool EventCondition(ScriptCommand c)
    {
        short A(int i) => i < c.Args.Length ? c.Args[i] : (short)0;
        switch (c.Code)
        {
            case 0: return true;                                                // 엔진 기본 갈래 — 늘 참(전투 첫 대사 방아쇠, 37번 쓰인다)
            case 1: return _tick == 2;                                          // 시작 인트로 방아쇠 — 시간 틱 2(아무도 움직이기 전, 0x1004f218)
            case 2:                                                             // 타이머 세기 비교(0x1004f230) — 인자1 이 값, 인자2 가 연산자
            {
                int slot = A(0);
                return (uint)slot < 10 && Compare(_eventTimer[slot], A(2), A(1));
            }
            case 3: return Compare(_tick, A(1), A(0));                          // 시간 틱 비교(0x1004f272 — [+0x4cf0] 는 빈 틱마다 오른다)
            case 100: return Compare(_battleVars[A(0) & 0xFF], A(1), A(2));     // 전투 국소 변수
            case 101: return Compare(A(0) >= 0 && A(0) < host._flags.Length ? host._flags[A(0)] : 0, A(1), A(2));
            case 102:                                                           // <b>장비</b>를 가졌나(0x1004f2f0) — 상태이상이 아니다. 자료 사용 0회.
            {
                var list = EventTargets(A(0), out _);
                bool has = list.Any(u => u.Alive && u.HasStatus(A(1)));
                return A(2) != 0 ? !has : has;
            }
            case 200:                                                           // 전장에 있나(0x1004f379)
            {
                // <b>인자2 가 0 이면 「없어야」 참</b>이다 — 예전에는 거꾸로 읽어 「지켜야 할 사람이 죽으면 패배」가
                // 첫 틱에 터졌다. 편을 가리키는 20000+ 는 극성이 또 반대다(0x1004f5da).
                // 전장 밖((0,0) 대기·201 로 나간) 인물은 「없다」(0x1006e940 는 맵 밖 자리를 없음으로 친다).
                var list = EventTargets(A(0), out bool whole);
                // 20010 은 <b>편 코드 홀수 · 인자2≠0</b> 갈래에서 찾은 유닛만 남긴다(0x1004f465) — 홑 유닛 갈래는 안 적는다(ba-20 B2).
                if (A(0) >= 20000 && !whole && A(2) != 0) RememberFound(list.FirstOrDefault(u => u.Alive && u.OnField));
                if (A(0) >= 20000)
                    return whole ? A(2) == 0 && !list.Any(u => u.Alive && u.OnField)
                                 : A(2) != 0 && list.Any(u => u.Alive && u.OnField);
                bool there = list.Any(u => u.Alive && u.OnField);
                return A(2) != 0 ? there : !there;
            }
            case 201:                                                           // 죽었나·없나(0x1004f550)
            {
                // 원본은 <b>인자1 을 안 읽는다</b>. 예전에는 그걸 「뒤집기」로 읽어, 살아 있는 사람 하나만으로도
                // 「죽었다」 조건이 참이 되어 전투가 시작하자마자 끝났다.
                var list = EventTargets(A(0), out bool whole);
                if (A(0) >= 20000) return whole && list.Count > 0 && list.All(u => u.Alive && u.OnField);
                return list.Count == 0 || list.All(u => !u.Alive || !u.OnField || u.Hp <= 0);   // 전장 밖도 「없다」
            }
            case 203:                                                           // HP 퍼센트 비교
            {
                var list = EventTargets(A(0), out _);
                var low = list.FirstOrDefault(u => u.Alive && Compare(u.Hp, A(2), u.MaxHp * A(3) / 100));
                if (low != null && A(0) >= 20000) RememberFound(low);           // 편 갈래만 20010 을 남긴다(0x1004f7f5, ba-20 B2)
                return low != null;
            }
            case 401:                                                           // 그 편 전멸
            {
                // 전장에 선 사람만 센다 — 아직 안 나온 증원((0,0) 대기)까지 세면, 「전멸하면 증원을 부르는」 사건이 영영 안 터진다.
                // Btl 0151 은 사건 2(편 0 전멸 → 강화아델룬 둘 증원)가 안 터져 적을 다 쓰러뜨려도 전투가 안 끝났다(사용자 보고).
                int side = A(0) >= 0 && A(0) < EventSideOrder.Length ? A(0) : -1;
                return side >= 0 && !host._units.Any(u => u.Alive && u.OnField && u.Side == side);
            }
            case 402:                                                           // 사각형 안에 있나
            {
                var list = EventTargets(A(0), out _);
                int x1 = Math.Min(A(2), A(4)), x2 = Math.Max(A(2), A(4));
                int y1 = Math.Min(A(3), A(5)), y2 = Math.Max(A(3), A(5));
                // 원본 0x1006e940(u,0) 은 NULL 검사뿐이라 <b>쓰러진 인물도</b> 그 칸에 남아 있으면 센다(ba-14 E2).
                var inside = list.FirstOrDefault(u => u.OnField && u.Col >= x1 && u.Col <= x2 && u.Row >= y1 && u.Row <= y2);
                if (inside != null) RememberFound(inside);                      // 조건 402 도 20010 을 남긴다(0x10050a32)
                return inside != null;
            }
            case 400:                                                           // 사각형 안에 있는 수 비교
            {
                int side = A(0) >= 0 && A(0) < EventSideOrder.Length ? A(0) : -1;
                int x1 = Math.Min(A(3), A(5)), x2 = Math.Max(A(3), A(5));
                int y1 = Math.Min(A(4), A(6)), y2 = Math.Max(A(4), A(6));
                int n = side < 0 ? 0 : host._units.Count(u => u.OnField && u.Side == side              // 쓰러진 인물도 센다(ba-14 E2)
                                                          && u.Col >= x1 && u.Col <= x2 && u.Row >= y1 && u.Row <= y2);
                return Compare(n, A(1), A(2));
            }
            case 202:                                                           // 편 번호 비교
            {
                var list = EventTargets(A(0), out _);
                return list.Any(u => Compare(u.Side, A(3), A(2)));
            }
            case 204:                                                           // 이미 증원으로 들어왔나 — 데모는 처음부터 다 서 있다
            {
                var list = EventTargets(A(0), out _);
                var first = list.FirstOrDefault(u => u.Alive && u.OnField);
                RememberFound(first);                                          // 조건 204 도 20010 을 남긴다(ba-14 E8)
                bool arrived = first != null;
                return A(1) != 0 ? !arrived : arrived;
            }
            case 300:                                                           // 붙었다 — 같은 줄이고 두 칸 안(0x1004fac0)
            {
                foreach (var x in EventFighters(A(0)))
                    foreach (var y in EventFighters(A(2)))
                        if (!ReferenceEquals(x, y) && (x.Col == y.Col || x.Row == y.Row)
                            && Math.Abs(x.Col - y.Col) + Math.Abs(x.Row - y.Row) <= 2)
                        {
                            (_eventFoundA, _eventFoundB) = (x, y);
                            return true;
                        }
                return false;
            }
            case 301:                                                           // <b>인자0 이 인자2 를 때렸다</b>(0x100502d0)
            {
                // 거리도 편도 안 본다. 죽은 뒤에도 참이라야 「누가 죽였나」로 대사를 가르는 전투가 돈다.
                var hitters = EventFighters(A(0));
                foreach (var y in EventFighters(A(2)))
                    if (y.LastHitBy is { } hit && !ReferenceEquals(hit, y) && hitters.Contains(hit))
                    {
                        (_eventFoundA, _eventFoundB) = (hit, y);
                        return true;
                    }
                return false;
            }
            default: return false;   // 아직 안 만든 조건(2 = 전역 배열)은 안 터뜨린다
        }
    }

    internal void RunEventAction(ScriptCommand a)
    {
        short A(int i) => i < a.Args.Length ? a.Args[i] : (short)0;
        if (Trace && a.Code is 6 or 11 or 207 or 706 or 707 or 708 or 909)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"event action: 사건 {_runningEvent} 줄 {_eventPc - 1} 행동 {a.Code} [{string.Join(",", a.Args)}]" + Environment.NewLine);
        switch (a.Code)
        {
            case 11:                                     // 승패 — 결과 = 인자0 + 1: 0 승리(배너), 3 패배(Game Over 배너), 1 은 배너·음악 없이 조용히 끝(호위 실패)
                SetEventOutcome(A(0) == 0, quiet: A(0) == 1);
                break;
            case 10:                                     // 이어지는 전투(결과 5) — 배너 없이 120틱 뒤 넘어간다(0x1006afa0)
                _eventNextBattle = A(0);
                SetEventOutcome(win: true, quiet: true);
                break;
            case 6:                                      // 끝내고 <b>그 필드로</b>(결과 6) — 배너 없이 120틱 뒤
                _eventNextBattle = 0;
                _eventNextField = A(0);
                SetEventOutcome(win: true, quiet: true);
                break;
            case 200:                                    // 증원 — 그 사람들을 전장에 세운다(0x10050eb0). 인자1 은 원본도 안 읽는다.
            case 214:                                    // 워프 등장 — 자리는 200 과 같고 하이 텔레포트(work 585) 연출로 나타난다(0x10052bf0).
            {
                // 200 은 인자4 의 변(0 위·1 왼·2 아래·3 오른) 바깥 100px 에서 8px/틱으로 곧장 들어와 맵 안에 들어서면 걷는다(ba-14 E7).
                // 부하는 대장 옆으로 엇갈려 선 뒤 진형으로 따라온다(0x10050f16~). 여기서는 변의 가장자리 칸에서 걸어 들어오게 한다.
                double longest = 0.4, entryTicks = 0;
                int entered = 0;
                foreach (var u in EventTargets(A(0), out _))
                {
                    if (!u.Alive) continue;              // 자리 옮기기 명령이라 죽은 인물을 되살리지 않는다(0x1006e940(u,1))
                    _pendingExits.RemoveAll(x => x.Unit == u);   // 걸어 나가던 중이면 그 예약을 버린다
                    u.OnField = true;
                    u.ResetTo(A(2), A(3));
                    // 그 칸에 누가 서 있으면 가장 가까운 빈 칸에 세운다(200 0x1004ef60 · 214 0x1004f0f0, ba-20 V5). 전에는 겹쳐 섰다.
                    var (landCol, landRow) = FreeCellNear(A(2), A(3), u);
                    if ((landCol, landRow) != (A(2), A(3))) u.ResetTo(landCol, landRow);
                    u.Facing = EdgeFacing(A(4));
                    if (a.Code == 200 && EdgeCell(A(4), A(2), A(3)) is var (ec, er) && (ec, er) != (landCol, landRow))
                    {
                        u.WarpTo(ec, er);
                        var walk = ComputeRange(u, tp: 1 << 20) is { } wr && wr.CanReach(landRow * host.Cols + landCol) ? PathWithin(wr, ec, er, landRow * host.Cols + landCol) : null;
                        if (walk is { Count: > 0 }) { foreach (var step in walk) u.Path.Enqueue(step); longest = Math.Max(longest, WalkSeconds(u, ec, er, walk)); }
                        else u.WarpTo(landCol, landRow);
                        u.OriginCol = landCol; u.OriginRow = landRow;
                    }
                    // 변 밖에서 곧게 걸어 들어온다 — 혼자면 140px, 군단 대장이면 100px 밖에서 틱당 8px(0x10051659 · 0x10050fb2, ba-21 B1).
                    // 전에는 맵 안 가장자리 칸에 곧바로 나타났다. 건너뛰는 중이면 곧바로 선다.
                    bool hasFollowers = FollowersOf(Array.IndexOf(host._units, u)).Count > 0;
                    if (a.Code == 200 && !host._talkSkip)
                    {
                        BeginEdgeEntry(u, A(4), hasFollowers ? 100 : 140);
                        entryTicks = Math.Max(entryTicks, hasFollowers ? 100 / 8.0 : 140 / 8.0);
                    }
                    if (a.Code == 214)
                    {
                        // 하이 텔레포트(work 585)로 나타난다(0x10052bf0 · 0x1008c8a0, ba-21 B7·B8): 맵 밖에서 준비(소리 694, 56틱) → 사라짐(소리 90, 80틱)
                        // → 30틱 뒤 카메라가 새 자리로 → 나타남 381:1 + 210:3(소리 91) → 50틱에 걸쳐 또렷해진다. 전에는 곧바로 서 있었다.
                        // 건너뛰는 중이면 곧바로 선다.
                        if (host._talkSkip) { u.Fade = 1; }
                        else
                        {
                            var arriving = u;
                            double t0 = host._lastTime;
                            u.Fade = 0;
                            host._pendingSounds.Add((host._lastTime, 694, host.UnitFoot(u).X));
                            host.LegionStageAb._legionLater.Add((t0 + 56 / TicksPerSecond, () => host.Play(90)));
                            host.LegionStageAb._legionLater.Add((t0 + 166 / TicksPerSecond, () =>
                            {
                                if (!arriving.Alive || !arriving.OnField) return;
                                CenterOnUnit(arriving);
                                var (wx, wy) = host.UnitFoot(arriving);
                                _effects.Add((381, 1, host._lastTime, wx, wy));
                                _effects.Add((210, 3, host._lastTime, wx, wy));
                                host._pendingSounds.Add((host._lastTime, 91, wx));
                            }));
                            for (int k = 1; k <= 10; k++)
                            {
                                double fade = k / 10.0;
                                host.LegionStageAb._legionFades.Add((t0 + (166 + 5 * k) / TicksPerSecond, u, fade));
                            }
                            longest = Math.Max(longest, 216 / TicksPerSecond);
                        }
                    }
                    int leader = Array.IndexOf(host._units, u);
                    foreach (var (follower, col, row) in FormationPlan(u, A(2), A(3)))
                    {
                        follower.OnField = true;
                        follower.ResetTo(col, row);
                        follower.Facing = u.Facing;
                        _followerTarget[follower] = (col, row);
                        // 부하는 둘씩 한 칸씩 더 밖(140·180·220px)에서 같이 들어온다(0x100510dc~0x10051240, ba-21 B2).
                        if (a.Code == 200 && !host._talkSkip)
                        {
                            int px = 140 + 40 * (entered++ / 2);
                            BeginEdgeEntry(follower, A(4), px);
                            entryTicks = Math.Max(entryTicks, px / 8.0 + StepTicks * 3);   // 들어온 뒤 진형 자리로 몇 걸음
                        }
                    }
                    if (host._units.Any(f => f.LeaderIndex == leader)) AssignFormationTargets(leader);
                }
                longest += entryTicks / TicksPerSecond;   // 변 밖에서 들어오는 시간(가장 긴 유닛)
                _eventMoveUntil = Math.Max(_eventMoveUntil, host._lastTime + longest);   // 줄은 안 붙든다 — 뒤따르는 행동 1 이 기다린다(ba-20 V2)
                break;
            }
            case 201:                                    // 퇴장 — 그 칸까지 갔다가 화면 밖으로. 인자1 이 1 이면 부대째(원본 — 전에는 늘 부하까지 데려갔다)
                foreach (var u in EventTargets(A(0), out _))
                {
                    if (!u.Alive) continue;
                    // 원본(0x100519c0)은 그 칸까지 걸어간 뒤 맵 밖으로 나가 사라진다(ba-20 V4). 걸을 길이 있고 멀쩡히 서 있는 유닛만 걸려 보낸다 —
                    // HP 0 으로 물러나는 보스·건너뛰는 중·판 밖 유닛은 전처럼 곧바로 뺀다.
                    if (_pendingExits.RemoveAll(x => x.Unit == u) > 0) u.Path.Clear();   // 같은 유닛에 201 이 또 오면 앞 길을 버린다
                    var exitRange = u.OnField && u.Hp > 0 && !host._talkSkip ? ComputeRange(u, tp: 1 << 20) : null;
                    int exitGoal = exitRange != null ? NearestReachableTo(u, exitRange, A(2), A(3)) : -1;
                    var exitWalk = exitRange != null && exitGoal >= 0 ? PathWithin(exitRange, u.Col, u.Row, exitGoal) : null;
                    if (exitWalk is { Count: > 0 })
                    {
                        foreach (var step in exitWalk) u.Path.Enqueue(step);
                        double seconds = WalkSeconds(u, u.Col, u.Row, exitWalk);
                        _pendingExits.Add((u, host._lastTime + seconds, A(2), A(3), A(1) == 1));
                        _eventMoveUntil = Math.Max(_eventMoveUntil, host._lastTime + seconds);
                        continue;
                    }
                    LeaveField(u, A(2), A(3), A(1) == 1);
                }
                break;
            case 202:                                    // 지정 칸으로 <b>걸어서</b>(0x10075ff0, ba-14 E5) — 전장에 있는 사람만. 막힌 칸이면 가장 가까운 갈 수 있는 칸.
            {
                double longest = 0;
                foreach (var u in EventTargets(A(0), out _))
                {
                    if (!u.OnField || !u.Alive) continue;
                    var wr = ComputeRange(u, tp: 1 << 20);
                    int goal = wr != null ? NearestReachableTo(u, wr, A(2), A(3)) : -1;
                    var walk = wr != null && goal >= 0 ? PathWithin(wr, u.Col, u.Row, goal) : null;
                    if (walk is { Count: > 0 })
                    {
                        foreach (var step in walk) u.Path.Enqueue(step);
                        longest = Math.Max(longest, WalkSeconds(u, u.Col, u.Row, walk));
                        u.OriginCol = goal % host.Cols; u.OriginRow = goal / host.Cols;
                    }
                    // 길이 없으면 제자리에 둔다 — 전에는 못 가는 칸에도 순간이동시키고 상태까지 초기화했다. 이미 그 칸이면 할 일이 없다.
                    else if (wr == null) u.ResetTo(A(2), A(3), keepFacing: true);
                    // 인자1 이 1 이면 부대째 — 부하는 진형으로 따라온다.
                    if (A(1) == 1 && host._units.Any(f => f.LeaderIndex == Array.IndexOf(host._units, u))) AssignFormationTargets(Array.IndexOf(host._units, u));
                }
                _eventMoveUntil = Math.Max(_eventMoveUntil, host._lastTime + longest);
                break;
            }
            case 208:                                    // 동작 재생 — 인자2 는 <b>모션 번호</b>라 3 으로 나눠야 동작이 된다(0x100530a1)
            {
                // 원본은 그 모션이 <b>끝날 때까지</b> 기다린다(인자4 반복) — 모션 길이만큼.
                double longest = 0.2;
                foreach (var u in EventTargets(A(0), out _))
                {
                    if (!u.OnField) continue;
                    double seconds = host._sprites.TryGetValue(u.ChrCode, out var sp) ? sp.MotionTicks(A(2)) / TicksPerSecond : 0;
                    if (seconds <= 0) seconds = 0.6;
                    int repeat = Math.Clamp((int)A(4), 1, 4);
                    u.PlayAction(A(2) / 3, seconds * repeat);
                    longest = Math.Max(longest, seconds * repeat);
                }
                _eventMoveUntil = Math.Max(_eventMoveUntil, host._lastTime + longest);   // 슬롯만 — 「208 → 2」 23곳에서 모션만큼 더 길었다(ba-20 V2 b)
                break;
            }
            case 212:                                    // 바라보는 쪽 — 인자1 이 곧 방향(0 위·1 왼·2 아래·3 오른, 0x10053170 SetAction(0, 인자1, 10000))
                foreach (var u in EventTargets(A(0), out _)) u.Facing = DirectionFacing(A(1));   // 전에는 들어온 쪽의 반대로 읽어 거꾸로 봤다
                break;
            case 706:                                    // 최대 HP 의 인자2 % 피해
                foreach (var u in EventTargets(A(0), out _))
                {
                    if (!u.Alive || u.Hp <= 0) continue;
                    // 피해 숫자를 띄우고(0x10054eb0), 지금 차례 유닛이면 HP 1 로 남긴다(0x1004e765, ba-20 V9).
                    int cut = Math.Min(u.Hp, u.MaxHp * A(2) / 100);
                    u.Hp -= cut;
                    if (cut > 0 && u.OnField) ShowNumber(u, cut.ToString(), DamageColor);
                    if (u.Hp > 0) continue;
                    if ((uint)_turn < host._units.Length && host._units[_turn] == u) u.Hp = 1;
                    else KillUnit(u);
                }
                break;
            case 707:                                    // 잃은 HP 의 인자2 % 회복 — 자료는 전부 100(풀피)
                foreach (var u in EventTargets(A(0), out _))
                {
                    if (!u.Alive) continue;
                    int before = u.Hp;
                    u.Hp = Math.Min(u.MaxHp, u.Hp + (u.MaxHp - u.Hp) * A(2) / 100);
                    // 회복 숫자(0x100551e0, ba-20 V9) — 옛 HP 에서 새 HP 로 세어 올라간다.
                    if (u.Hp > before && u.OnField && host._db != null) ShowNumber(u, host._db.T(159), HealColor2, rise: false, count: (before, u.Hp));
                }
                break;
            case 207:                                    // 보스 필살기 — 인자2 가 `.att` work 번호(0x10052400)
            {
                var caster = EventTargets(A(0), out _).FirstOrDefault(u => u.Alive && u.OnField);
                if (caster is null || Work(A(2)) is not { } boss)
                {
                    if (Trace)
                        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                            $"207: 시전자 {A(0)} 없음(살아 판 위) 또는 work {A(2)} 없음 — 안 쏜다" + Environment.NewLine);
                    break;
                }
                int casterIndex = Array.IndexOf(host._units, caster);
                // 겨눌 곳은 <b>제자리 사거리 안</b>에서 AI 칸 점수(+0x3e 기준, 거리 가중)로 고른다(0x1005d860). 못 찾으면 안 쏜다 — fg-21 ⑮.
                (int Col, int Row, int Score)? aim = null;
                int reach = Math.Max(1, RangeMaxOf(boss, caster) / 4) + 1, num74 = host._db?.N(74) ?? 4;
                for (int ay = Math.Max(0, caster.Row - reach); ay <= Math.Min(host.Rows - 1, caster.Row + reach); ay++)
                    for (int ax = Math.Max(0, caster.Col - reach); ax <= Math.Min(host.Cols - 1, caster.Col + reach); ax++)
                    {
                        if (!InWorkRange(boss, caster.Col, caster.Row, ax, ay, caster)) continue;
                        var targets = WorkTargets(boss, caster, ax, ay);
                        if (targets.Count == 0) continue;
                        int score = CDiv(TargetValue(caster, boss, targets, plain: true) * num74 * 10, 4 + Math.Abs(ax - caster.Col) + Math.Abs(ay - caster.Row));
                        if (aim == null || score > aim.Value.Score) aim = (ax, ay, score);
                    }
                if (aim is not { } mark)
                {
                    if (Trace)
                        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                            $"207: 시전 {caster.ChrCode}(편 {caster.Side}, {caster.Col},{caster.Row}) work {boss.Id} 사거리 {reach} 안에 대상 없음 — 안 쏜다" + Environment.NewLine);
                    break;
                }
                var aimedUnit = LiveUnitAt(mark.Col, mark.Row);
                int markIndex = boss.TargetMode is 1 or 4 or 5 && aimedUnit != null ? Array.IndexOf(host._units, aimedUnit) : -1;
                // 사건 안에서 끝까지 돌린다(B1) — 공짜 필살기(0x10052651 → 0x10075ff0 인자5 = 1 → +0xa4, B2), 지금 차례 유닛은 HP 1 로 버틴다(0x1004e757, B3).
                _eventRoutine = EventWorkRoutine(caster, UseWorkRoutine(casterIndex, boss, markIndex, mark.Col, mark.Row, [], eventFinisher: true));
                break;
            }
            case 708:                                    // 소속 편 바꾸기 — 적이 아군이 되거나 그 반대(0x100553a0)
                // 인자1 이 1 이면 <b>부하까지</b> 같은 편으로 — Btl 0049 의 아지다하카는 군단째로 넘어온다.
                // 부하를 안 바꾸면 군단원이 적으로 남아 계속 덤비고 「적 부대 전원의 패배」도 영영 안 난다.
                foreach (var u in EventTargets(A(0), out _))
                {
                    u.Side = A(2);
                    if (A(1) != 1) continue;
                    int leader = Array.IndexOf(host._units, u);
                    foreach (var follower in host._units.Where(f => f.LeaderIndex == leader)) follower.Side = A(2);
                }
                break;
            case 909:                                    // 베라모드 폭주(0x10055b10) — 파티에 따라 Chr 223(베라모드)·37 을 찾아
            {                                            // work 1582(어빌리티 160 「폭주」, 모션 48)를 쓰게 하고 화면을 물들인다.
                var caster = host._units.FirstOrDefault(u => u.Alive && u.OnField && u.ChrCode is 223 or 37);
                if (caster is null || Work(1582) is not { } burst) break;
                // 교대가 끝나야 다음 줄 — 0231 의 207 [Chr 37]·706 [37] 이 새로 선 37 을 찾는다(B4).
                _eventRoutine = BerserkSwapRoutine(caster, burst);
                break;
            }
            case 907:                                    // 물체 여닫기(0x10055a50) — 인자0 배치 번호, 인자1 0 닫기 · 그 밖 열기
                ToggleObject(A(0), A(1) != 0);
                _eventMoveUntil = Math.Max(_eventMoveUntil, host._lastTime + 0.5);       // 여닫는 사이만큼 — 원본은 물체가 다 움직일 때까지 기다린다
                break;
            case 906:                                    // 카메라를 사각형 가운데로(0x10055810) — 인자는 바이트 넷 (x1, y1, x2, y2)
            {
                // 카메라는 StepEvent 가 먼저 옮기고 멈출 때까지 이 줄을 붙든다(EventCameraWaits) — 강조는 스크롤이 끝난 틀에 켠다(단계 1).
                // 건너뛰는 중이면 카메라 없이 바로 온다.
                if (host._talkSkip) CenterOnCell((A(0) + A(2) + 1) / 2, (A(1) + A(3) + 1) / 2);
                // 사각형 안 칸을 층 13 초록(배치 칸과 같은 그림)으로 200틱 동안 칠한다(ba-14 E6) — 「여기로 가라」 표시.
                _highlightRect = (Math.Min(A(0), A(2)), Math.Min(A(1), A(3)), Math.Max(A(0), A(2)), Math.Max(A(1), A(3)));
                _highlightUntil = host._lastTime + 200 / TicksPerSecond;
                break;
            }
            case 900:                                    // 타이머 켜기·끄기 — 켤 때 세기를 0 으로(0x10055700)
                if ((uint)A(0) < 10) { _eventTimerRun[A(0)] = A(1) != 0; _eventTimer[A(0)] = 0; }
                break;
            case 713:                                    // 군단 얻기 [군단] — 파티 군단 목록에 넣는다(0x10055480 → 0x1004df50, 필드 713 과 같은 함수)
                if (A(0) > 0) { host.Mos._ownedLegions.Add(A(0)); host.Mos._legionsKnown = true; }   // 전에는 아이템으로 잘못 넣었다
                break;
            case 500:                                    // 소리 한 번 내고 <b>끝날 때까지 기다린다</b>(0x10053ec0)
                if (host._talkSkip) break;                    // 건너뛰는 중에는 안 튼다 — 전에는 건너뛴 대사의 목소리가 뒤늦게 겹쳐 나왔다(ba-20 V13)
                host.PlayEventVoice(A(0));                    // 인자1 은 말하는 이 — 원본은 그 인물에 소리를 매단다(좌우 소리는 안 넣었다)
                break;
            case 400:                                    // 카메라 칸 가운데로 · 402 유닛 가운데로 — StepEvent 가 EventCameraWaits 로 옮기고 기다린다.
                if (host._talkSkip) { _camGoal = null; CenterOnCell(A(0), A(1)); }   // 건너뛰는 중에도 카메라는 보내 둔다(기다리지만 않는다) —
                break;                                   // 전에는 건너뛴 뒤 화면이 증원·보스를 안 비췄다(ba-20 V13)
            case 402:
                if (host._talkSkip && EventTargets(A(0), out _).FirstOrDefault() is { Alive: true, OnField: true } seen)
                    CenterOnUnit(seen.LeaderIndex >= 0 && seen.LeaderIndex < host._units.Length ? host._units[seen.LeaderIndex] : seen);
                break;
            case 512:                                    // BGM 바꾸기
                host.StopMusic();
                if (A(0) > 1 && A(0) != 0xffff) host.PlayMusicFile(A(0), loop: true);
                break;
            case 100: _battleVars[A(0) & 0xFF] = (byte)Math.Clamp((int)A(1), 0, 255); break;
            case 101: _battleVars[A(0) & 0xFF] = (byte)Math.Clamp(_battleVars[A(0) & 0xFF] + A(1), 0, 255); break;
            case 102:
                if (A(0) > 0 && A(0) < host._flags.Length) host._flags[A(0)] = (byte)Math.Clamp((int)A(1), 0, 255);
                break;
            case 103:
                // [깃발, 연산자, 값] — 0 더하기 · 1 빼기 · 2 곱하기 · 3 나누기(0x10050de0: 연산자 = 워드 +2, 값 = 바이트 +4).
                // 전에는 인자 1(연산자)을 값으로 더해서, 프레야 평원·던젼(Btl 0240·0239)의 「깃발 110 += 1」이 0 을 더해
                // 북 평원(Btl 0238, 깃발 110 == 2)이 영영 안 열렸다(사용자 보고, Chp 0064). 필드판과 달리 두 번 더하는 흠은 없다.
                if (A(0) > 0 && A(0) < host._flags.Length)
                    host._flags[A(0)] = FieldScene.FieldArith(host._flags[A(0)], A(1), A(2));
                break;
        }
    }

    /// <summary>
    /// 행동 909 — 폭주(work 1582) 뒤 베라모드(Chr 223)를 지우고 <b>같은 칸에 Chr 37 을 편 3(동맹 AI)으로</b> 새로 세운다
    /// (0x10055cb1~0x10055d37). 전에는 기술만 썼다(fg-21 ⑫). 이미 37 이면 기술만.
    /// </summary>
    internal IEnumerator<bool> BerserkSwapRoutine(UnitState caster, WorkData burst)
    {
        // 909 도 공짜다 — 0x10055c3a 가 0x10075ff0(0x62e, x, y, 1, 1, 1, …) 로 +0xa0 = +0xa4 = 1(감사5 B2 보충).
        var use = UseWorkRoutine(Array.IndexOf(host._units, caster), burst, -1, caster.Col, caster.Row, [], eventFinisher: true);
        while (use.MoveNext()) yield return true;
        if (caster.ChrCode != 223 || host._db?.Character(37) is not { } c) yield break;
        int col = caster.Col, row = caster.Row;
        MarkDead(caster);                        // 교대도 승계를 부른다(0x10055cb1 → 0x100716c0)
        caster.OnField = false;
        var born = new UnitState(new DemoUnit(37, col, row, 3, 0, caster.Facing)) { Data = c with { CumExp = c.Level * 100 } };
        born.MaxHp = born.Hp = ScaleMaxHp(born, Math.Max(1, host._db.MaxHp(born.Data!)));
        born.MaxTp = host._db.MaxTp(born.Data!);
        born.Tp = 0;
        born.Stp = Math.Max(0, host._db.Stp(born.Data!));
        born.MaxSoul = host._db.MaxSoul(born.Data!);
        born.Soul = Math.Min(born.MaxSoul, caster.Soul);
        born.Awake = true;
        host._units = [.. host._units, born];
        host.LoadRosterSprites();
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"909: 223 → 37 편 3 at ({col},{row}), 그림 {(host._sprites.ContainsKey(37) ? "있음" : "없음")}" + Environment.NewLine);
    }

    /// <summary>
    /// 사건이 건 기술 하나 — 시전자가 이미 쓰러졌으면 그만(B4), 아니면 끝까지 돌리고 판의 모든 유닛이 설 때까지(<c>0x1006e320</c>, 단계 1) 기다린다.
    /// 돌던 <c>_routine</c> 은 건드리지 않는다 — 사건 조건은 루틴 도중에도 보므로(301 「가 나를 때렸다」는 적의 공격 루틴 한가운데서 참이 된다)
    /// 예전처럼 덮어쓰면 AI 루틴 끝의 <c>Rest</c> 가 안 불려 차례가 영영 안 넘어갔다(Btl 0143 사건 6). 사건이 끝나면 그대로 이어 돈다.
    /// </summary>
    internal IEnumerator<bool> EventWorkRoutine(UnitState caster, IEnumerator<bool> use)
    {
        if (!caster.Alive || !caster.OnField) yield break;
        void Log(string what)
        {
            if (Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"event finisher {what}: 사건 {_runningEvent} 줄 {_eventPc - 1} 시전 {caster.ChrCode} TP {caster.Tp} SOUL {caster.Soul} 턴 {_turnNo}" + Environment.NewLine);
        }
        Log("시작");
        while (use.MoveNext()) yield return true;
        for (double end = host._lastTime + 5; host._lastTime < end && host._units.Any(u => u.Alive && u.OnField && u.IsBusy);) yield return true;
        Log("끝");
    }
}
