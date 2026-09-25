using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

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
internal sealed unsafe partial class BattleSceneWindow
{
    private IReadOnlyList<BattleEvent> _events = [];
    private int[] _eventFired = [];

    /// <summary>턴 수 — 이벤트 조건 1·3 이 보는 값. 새 차례가 올 때마다 오른다(<c>0x10067d36</c>).</summary>
    private int _turnNo;

    /// <summary>전투 국소 변수 — 행동 100·101 이 고치고 조건 100 이 읽는다(<c>0x101b69a0</c>).</summary>
    private readonly byte[] _battleVars = new byte[256];

    /// <summary>
    /// 이벤트 타이머 열 칸 — 행동 <c>900</c> 이 켜고 <b>턴이 하나 지날 때마다</b> 센다(<c>0x1004e9e0</c>).
    /// 조건 <c>2</c> 가 그 세기를 견준다. <c>Btl 0170</c> 이 「적을 다 잡은 뒤 20턴·40턴」에 쓴다.
    /// </summary>
    private readonly int[] _eventTimer = new int[10];

    private readonly bool[] _eventTimerRun = new bool[10];

    /// <summary>이벤트가 정한 다음 전투 — 0 이면 <c>Btl</c> 자료의 값(또는 모세스)으로 간다.</summary>
    private int _eventNextBattle;

    /// <summary>이벤트 행동 6 이 정한 다음 필드 — 0 이면 모세스로 간다.</summary>
    private int _eventNextField;

    /// <summary>그 전투의 이벤트를 읽어 둔다.</summary>
    private void LoadEvents(int battleId)
    {
        _events = [];
        _eventFired = [];
        // 돌던 사건 자리를 반드시 비운다 — 안 비우면 앞 전투의 번호가 남아, 사건이 더 적은 전투를 읽었을 때
        // StepEvent 의 _events[_runningEvent] 가 목록 밖을 짚어 죽는다(사용자 보고 crash).
        _runningEvent = -1;
        _eventPc = 0;
        _eventWaitUntil = 0;
        _talkSkip = false;
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
    private int _runningEvent = -1;
    private int _eventPc;
    private double _eventWaitUntil;

    /// <summary>행동 500 이 튼 소리를 푸는 중인가 — 다 풀릴 때까지 이벤트를 멈춘다(원본은 소리 개체의 +0x58 이 0 이 될 때까지 기다린다).</summary>
    private volatile bool _eventSoundLoading;

    /// <summary>다 푼 소리의 길이(초) — 배경 실이 채우고 <see cref="StepEvent"/> 가 기다림으로 바꾼다. 0 이면 없음.</summary>
    private volatile float _eventSoundSeconds;

    /// <summary>이벤트가 돌거나 대사가 떠 있으면 전투를 멈춘다.</summary>
    private bool EventsBusy => _runningEvent >= 0 || _talk != null;

    /// <summary>조건이 다 맞는 이벤트를 하나 켠다. 결과가 정해지면 더 보지 않는다.</summary>
    /// <summary>
    /// 적을 다 쓰러뜨려 이기는 순간 — 아직 안 돈 「다음 장소로 보내는」 사건(행동 6 필드 · 10 전투)이 있고 <b>턴·타이머 말고</b> 나머지 조건이
    /// 맞으면 그 행선지를 쓴다. 그 사건이 턴 수를 기다리는 동안 전멸 승리가 먼저 전투를 끝내 이야기 사슬이 끊겼다 —
    /// 샤이닝 스타 <c>Btl 0137</c> 사건 5(턴 &gt; 5 · 열쇠 10006 없음 → <c>Fld 0055</c> → 행동 11 챕터 끝)를 건너뛰어 챕터가 안 끝났다(사용자 보고).
    /// </summary>
    private void ScriptedDestinationOnWipe()
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
        if (_scene.EngineJudgesWipe) return;
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

    private void RunEvents()
    {
        if (_events.Count == 0 || _outcome.Length > 0 || EventsBusy) return;
        for (int i = 0; i < _events.Count; i++)
        {
            var e = _events[i];
            if (e.Conditions.Count == 0) continue;                      // 조건이 없으면 안 터진다(0x10056574)
            if (e.MaxFire > 0 && _eventFired[i] >= e.MaxFire) continue;
            if (!e.Conditions.All(EventCondition)) continue;
            _eventFired[i]++;
            _runningEvent = i;
            _eventPc = 0;
            _eventWaitUntil = 0;
            StepEvent();
            return;
        }
    }

    /// <summary>
    /// 돌고 있는 이벤트를 한 걸음 나아가게 한다 — 대사가 <b>한 줄씩</b> 나오는 것이 여기서 생긴다.
    /// </summary>
    /// <remarks>
    /// 진행기 <c>0x10056fb0</c>: 행동 <b>0</b> 은 다른 이벤트 부르기, <b>1</b> 은 <b>앞서 띄운 것이 끝날 때까지 기다리기</b>,
    /// <b>2</b> 는 틱 기다리기, <b>3</b> 은 중단이다. 나머지는 넣고 바로 다음 줄로 간다.
    /// 대사 줄 사이에 낀 <c>1</c> 이 「눌러서 넘길 때까지 멈춤」을 만든다.
    /// </remarks>
    private void StepEvent()
    {
        while (_runningEvent >= 0)
        {
            if (_talk != null) return;                                  // 대사가 떠 있으면 기다린다
            if (_eventSoundSeconds > 0) { _eventWaitUntil = _lastTime + _eventSoundSeconds; _eventSoundSeconds = 0; }
            if (_eventSoundLoading && !_talkSkip) return;               // 행동 500 의 소리를 아직 푸는 중
            if (_eventWaitUntil > _lastTime && !_talkSkip) return;      // 건너뛰는 중이면 기다림은 없는 셈
            if (_runningEvent >= _events.Count) { _runningEvent = -1; _talkSkip = false; return; }   // 판이 바뀌었다
            var e = _events[_runningEvent];
            // 그 이벤트가 끝나면 건너뛰기도 끝난다 — 다음 장면 대사는 다시 보인다.
            if (_eventPc >= e.Actions.Count) { _runningEvent = -1; _talkSkip = false; return; }

            var a = e.Actions[_eventPc++];
            switch (a.Code)
            {
                case 0: break;                                          // 다른 이벤트 부르기 — 그 이벤트가 제 조건으로 돈다
                case 1: break;                                          // 기다리기 — 위에서 이미 봤다
                case 2:
                    _eventWaitUntil = _lastTime + ((a.Args.Length > 0 ? a.Args[0] : 0)
                                                 | ((a.Args.Length > 1 ? a.Args[1] : 0) << 16)) / TicksPerSecond;
                    break;
                case 3: _runningEvent = -1; _talkSkip = false; return;  // 중단
                case 600: ShowTalk(box: true, a); if (_talk == null) break; return;    // 건너뛰는 중이면 안 뜬다
                case 601: ShowTalk(box: false, a); if (_talk == null) break; return;
                default:
                    RunEventAction(a);
                    if (_outcome.Length > 0) { _runningEvent = -1; _talkSkip = false; return; }
                    break;
            }
        }
    }

    private static bool Compare(int a, int op, int b) => op switch
    {
        0 => a == b,
        1 => a != b,
        2 => a < b,
        3 => a <= b,
        4 => a > b,
        _ => a >= b,
    };

    /// <summary>들어오는 쪽(0 위·1 왼·2 아래·3 오른)을 <b>바라보는 쪽</b>으로 — 들어온 쪽의 반대를 본다.</summary>
    private static Facing EdgeFacing(int edge) => (edge & 3) switch
    {
        0 => Facing.Down,
        1 => Facing.Right,
        2 => Facing.Up,
        _ => Facing.Left,
    };

    /// <summary>편 번호를 이벤트가 쓰는 차례(4·3·0·1·2)에서 꺼낸다.</summary>
    private static readonly int[] EventSideOrder = [4, 3, 0, 1, 2];

    /// <summary>조건 300·301 이 찾아 낸 두 사람 — 대사가 <c>20010</c>·<c>20011</c> 로 이들을 가리킨다.</summary>
    private UnitState? _eventFoundA, _eventFoundB;

    /// <summary>
    /// 조건 300·301 이 쓰는 대상 고르기 — <b>죽은 사람도 든다</b>(걸러내기 갈래가 0 이라 NULL 검사뿐)이고,
    /// 편 코드는 <b>홀수만</b> 받는다(짝수는 늘 거짓, <c>0x1005022c</c>).
    /// </summary>
    private List<UnitState> EventFighters(int value)
    {
        if (value is >= 20000 and <= 20009)
            return (value - 20000) % 2 == 0
                ? []
                : [.. _units.Where(x => x.Side == EventSideOrder[(value - 20000) / 2])];
        if (value == 20010) return _eventFoundA is null ? [] : [_eventFoundA];
        if (value == 20011) return _eventFoundB is null ? [] : [_eventFoundB];
        if (value >= 10000) return [.. _units.Where(x => x.Record == value - 10000)];
        return value > 0 ? [.. _units.Where(x => x.ChrCode == value)] : [];
    }

    /// <summary>대상 지정 값이 가리키는 인물들. 편을 가리키면 그 편 전부.</summary>
    private List<UnitState> EventTargets(int value, out bool wholeSide)
    {
        wholeSide = false;
        if (value >= 20000)
        {
            int k = (value - 20000) / 2;
            wholeSide = (value - 20000) % 2 == 0;      // 짝수 = 「그 편 전부/아무도」, 홀수 = 「누군가」
            if (k >= EventSideOrder.Length) return [];
            int side = EventSideOrder[k];
            return [.. _units.Where(u => u.Side == side)];
        }
        // 10000+N 은 Btl 레코드 번호다(빈 칸을 걸러 낸 뒤의 배열 자리가 아니다).
        if (value >= 10000) return [.. _units.Where(u => u.LeaderIndex < 0 && u.Record == value - 10000)];
        return value > 0 ? [.. _units.Where(u => u.ChrCode == value)] : [];
    }

    private bool EventCondition(ScriptCommand c)
    {
        short A(int i) => i < c.Args.Length ? c.Args[i] : (short)0;
        switch (c.Code)
        {
            case 0: return true;                                                // 엔진 기본 갈래 — 늘 참(전투 첫 대사 방아쇠, 37번 쓰인다)
            case 1: return _turnNo == 2;                                        // 시작 인트로 방아쇠
            case 2:                                                             // 타이머 세기 비교(0x1004f230) — 인자1 이 값, 인자2 가 연산자
            {
                int slot = A(0);
                return (uint)slot < 10 && Compare(_eventTimer[slot], A(2), A(1));
            }
            case 3: return Compare(_turnNo, A(1), A(0));                        // 턴 수 비교
            case 100: return Compare(_battleVars[A(0) & 0xFF], A(1), A(2));     // 전투 국소 변수
            case 101: return Compare(A(0) >= 0 && A(0) < _flags.Length ? _flags[A(0)] : 0, A(1), A(2));
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
                var list = EventTargets(A(0), out bool whole);
                if (A(0) >= 20000)
                    return whole ? A(2) == 0 && !list.Any(u => u.Alive)
                                 : A(2) != 0 && list.Any(u => u.Alive);
                bool there = list.Any(u => u.Alive);
                return A(2) != 0 ? there : !there;
            }
            case 201:                                                           // 죽었나·없나(0x1004f550)
            {
                // 원본은 <b>인자1 을 안 읽는다</b>. 예전에는 그걸 「뒤집기」로 읽어, 살아 있는 사람 하나만으로도
                // 「죽었다」 조건이 참이 되어 전투가 시작하자마자 끝났다.
                var list = EventTargets(A(0), out bool whole);
                if (A(0) >= 20000) return whole && list.Count > 0 && list.All(u => u.Alive);
                return list.Count == 0 || list.All(u => !u.Alive);
            }
            case 203:                                                           // HP 퍼센트 비교
            {
                var list = EventTargets(A(0), out _);
                return list.Any(u => u.Alive && Compare(u.Hp, A(2), u.MaxHp * A(3) / 100));
            }
            case 401:                                                           // 그 편 전멸
            {
                // 전장에 선 사람만 센다 — 아직 안 나온 증원((0,0) 대기)까지 세면, 「전멸하면 증원을 부르는」 사건이 영영 안 터진다.
                // Btl 0151 은 사건 2(편 0 전멸 → 강화아델룬 둘 증원)가 안 터져 적을 다 쓰러뜨려도 전투가 안 끝났다(사용자 보고).
                int side = A(0) >= 0 && A(0) < EventSideOrder.Length ? A(0) : -1;
                return side >= 0 && !_units.Any(u => u.Alive && u.OnField && u.Side == side);
            }
            case 402:                                                           // 사각형 안에 있나
            {
                var list = EventTargets(A(0), out _);
                int x1 = Math.Min(A(2), A(4)), x2 = Math.Max(A(2), A(4));
                int y1 = Math.Min(A(3), A(5)), y2 = Math.Max(A(3), A(5));
                return list.Any(u => u.Alive && u.Col >= x1 && u.Col <= x2 && u.Row >= y1 && u.Row <= y2);
            }
            case 400:                                                           // 사각형 안에 있는 수 비교
            {
                int side = A(0) >= 0 && A(0) < EventSideOrder.Length ? A(0) : -1;
                int x1 = Math.Min(A(3), A(5)), x2 = Math.Max(A(3), A(5));
                int y1 = Math.Min(A(4), A(6)), y2 = Math.Max(A(4), A(6));
                int n = side < 0 ? 0 : _units.Count(u => u.Alive && u.Side == side
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
                bool arrived = list.Any(u => u.Alive);
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

    private void RunEventAction(ScriptCommand a)
    {
        short A(int i) => i < a.Args.Length ? a.Args[i] : (short)0;
        switch (a.Code)
        {
            case 11:                                     // 승패 — 인자0 이 0 이면 승리, 3 이면 패배
                SetEventOutcome(A(0) == 0);
                break;
            case 10:                                     // 이어지는 전투
                _eventNextBattle = A(0);
                SetEventOutcome(win: true);
                break;
            case 6:                                      // 끝내고 <b>그 필드로</b>
                _eventNextBattle = 0;
                _eventNextField = A(0);
                SetEventOutcome(win: true);
                break;
            case 200:                                    // 증원 — 그 사람들을 전장에 세운다(0x10050eb0). 인자1 은 원본도 안 읽는다.
            case 214:                                    // 워프 등장 — 자리는 200 과 같다.
                // 원본은 대장에게 군단이 있으면 <b>부하까지 한 줄로</b> 세워(0x10050f16~0x10051296: 홀수 번째는 반 칸 앞, 짝수 번째는
                // 한 칸 반 뒤, 두 명마다 한 칸씩 더 뒤) 가장자리에서 8픽셀/틱으로 걸어 들어오게 한다. 데모는 진형 자리에 바로 세운다.
                foreach (var u in EventTargets(A(0), out _))
                {
                    u.OnField = true;
                    u.ResetTo(A(2), A(3));
                    u.Facing = EdgeFacing(A(4));
                    int leader = Array.IndexOf(_units, u);
                    foreach (var (follower, col, row) in FormationPlan(u, A(2), A(3)))
                    {
                        follower.OnField = true;
                        follower.ResetTo(col, row);
                        follower.Facing = u.Facing;
                        _followerTarget[follower] = (col, row);
                    }
                    if (_units.Any(f => f.LeaderIndex == leader)) AssignFormationTargets(leader);
                }
                _eventWaitUntil = _lastTime + 0.4;       // 걸어 들어오는 사이만큼 기다린다
                break;
            case 201:                                    // 퇴장 — 그 칸까지 갔다가 화면 밖으로(부하도 같이)
                foreach (var u in EventTargets(A(0), out _))
                {
                    u.ResetTo(A(2), A(3));
                    u.OnField = false;
                    int leader = Array.IndexOf(_units, u);
                    foreach (var follower in _units.Where(f => f.LeaderIndex == leader)) follower.OnField = false;
                }
                break;
            case 202:                                    // 지정 칸으로 — 전장에 있는 사람만(원본도 맵 안인지 본다)
                foreach (var u in EventTargets(A(0), out _))
                    if (u.OnField) u.ResetTo(A(2), A(3), keepFacing: true);
                break;
            case 208:                                    // 동작 재생 — 인자2 는 <b>모션 번호</b>라 3 으로 나눠야 동작이 된다(0x100530a1)
                foreach (var u in EventTargets(A(0), out _))
                    if (u.OnField) u.PlayAction(A(2) / 3, 0.6);
                _eventWaitUntil = _lastTime + 0.6;
                break;
            case 212:                                    // 바라보는 쪽
                foreach (var u in EventTargets(A(0), out _)) u.Facing = EdgeFacing(A(1));
                break;
            case 706:                                    // 최대 HP 의 인자2 % 피해
                foreach (var u in EventTargets(A(0), out _))
                {
                    if (!u.Alive) continue;
                    u.Hp = Math.Max(0, u.Hp - u.MaxHp * A(2) / 100);
                    if (u.Hp == 0) KillUnit(u);
                }
                break;
            case 707:                                    // 잃은 HP 의 인자2 % 회복 — 자료는 전부 100(풀피)
                foreach (var u in EventTargets(A(0), out _))
                    if (u.Alive) u.Hp = Math.Min(u.MaxHp, u.Hp + (u.MaxHp - u.Hp) * A(2) / 100);
                break;
            case 207:                                    // 보스 필살기 — 인자2 가 `.att` work 번호(0x10052400)
            {
                var caster = EventTargets(A(0), out _).FirstOrDefault(u => u.Alive && u.OnField);
                if (caster is null || Work(A(2)) is not { } boss) break;
                int casterIndex = Array.IndexOf(_units, caster);
                // 겨눌 곳은 가장 가까운 상대 — 원본은 시전자 제 표적 고르기를 쓰지만 결과가 거의 같다.
                var mark = _units.Where(t => t.Alive && t.OnField && SeesAsFoe(caster, t))
                                 .OrderBy(t => Math.Abs(t.Col - caster.Col) + Math.Abs(t.Row - caster.Row))
                                 .FirstOrDefault();
                if (mark is null) break;
                _routine = AfterRunning(_routine, UseWorkRoutine(casterIndex, boss, Array.IndexOf(_units, mark), mark.Col, mark.Row, []));
                _eventWaitUntil = _lastTime + 1.2;
                break;
            }
            case 708:                                    // 소속 편 바꾸기 — 적이 아군이 되거나 그 반대(0x100553a0)
                // 인자1 이 1 이면 <b>부하까지</b> 같은 편으로 — Btl 0049 의 아지다하카는 군단째로 넘어온다.
                // 부하를 안 바꾸면 군단원이 적으로 남아 계속 덤비고 「적 부대 전원의 패배」도 영영 안 난다.
                foreach (var u in EventTargets(A(0), out _))
                {
                    u.Side = A(2);
                    if (A(1) != 1) continue;
                    int leader = Array.IndexOf(_units, u);
                    foreach (var follower in _units.Where(f => f.LeaderIndex == leader)) follower.Side = A(2);
                }
                break;
            case 909:                                    // 베라모드 폭주(0x10055b10) — 파티에 따라 Chr 223(베라모드)·37 을 찾아
            {                                            // work 1582(어빌리티 160 「폭주」, 모션 48)를 쓰게 하고 화면을 물들인다.
                var caster = _units.FirstOrDefault(u => u.Alive && u.OnField && u.ChrCode is 223 or 37);
                if (caster is null || Work(1582) is not { } burst) break;
                _routine = AfterRunning(_routine, UseWorkRoutine(Array.IndexOf(_units, caster), burst, -1, caster.Col, caster.Row, []));
                _eventWaitUntil = _lastTime + 1.2;
                break;
            }
            case 907:                                    // 물체 여닫기(0x10055a50) — 인자0 배치 번호, 인자1 0 닫기 · 그 밖 열기
                ToggleObject(A(0), A(1) != 0);
                _eventWaitUntil = _lastTime + 0.5;       // 여닫는 사이만큼 — 원본은 물체가 다 움직일 때까지 기다린다
                break;
            case 906:                                    // 카메라를 사각형 가운데로(0x10055810) — 인자는 바이트 넷 (x1, y1, x2, y2)
            {
                int cx = (A(0) + A(2) + 1) / 2, cy = (A(1) + A(3) + 1) / 2;
                _camTargetX = Math.Clamp(cx * TileW + TileW / 2 - ViewWidth / 2, 0, CamMaxX);
                _camTarget = Math.Clamp(CellCenterY(Math.Clamp(cx, 0, Cols - 1), Math.Clamp(cy, 0, Rows - 1)) - ViewHeight / 2, 0, CamMax);
                break;
            }
            case 900:                                    // 타이머 켜기·끄기 — 켤 때 세기를 0 으로(0x10055700)
                if ((uint)A(0) < 10) { _eventTimerRun[A(0)] = A(1) != 0; _eventTimer[A(0)] = 0; }
                break;
            case 713:                                    // 아이템 하나 주기
                if (A(0) > 0) _inventory[A(0)] = _inventory.GetValueOrDefault(A(0)) + 1;
                break;
            case 500:                                    // 소리 한 번 내고 <b>끝날 때까지 기다린다</b>(0x10053ec0)
                PlayEventVoice(A(0));                    // 인자1 은 말하는 이 — 원본은 그 인물에 소리를 매단다(좌우 소리는 안 넣었다)
                break;
            case 400:
            case 402: break;                             // 카메라 옮기기 — 데모 카메라는 말하는 이·차례인 이를 저절로 따라간다
            case 512:                                    // BGM 바꾸기
                _mixer.StopMusic();
                if (A(0) > 1 && A(0) != 0xffff) PlayMusicFile(A(0), loop: true);
                break;
            case 100: _battleVars[A(0) & 0xFF] = (byte)Math.Clamp((int)A(1), 0, 255); break;
            case 101: _battleVars[A(0) & 0xFF] = (byte)Math.Clamp(_battleVars[A(0) & 0xFF] + A(1), 0, 255); break;
            case 102:
                if (A(0) > 0 && A(0) < _flags.Length) _flags[A(0)] = (byte)Math.Clamp((int)A(1), 0, 255);
                break;
            case 103:
                // [깃발, 연산자, 값] — 0 더하기 · 1 빼기 · 2 곱하기 · 3 나누기(0x10050de0: 연산자 = 워드 +2, 값 = 바이트 +4).
                // 전에는 인자 1(연산자)을 값으로 더해서, 프레야 평원·던젼(Btl 0240·0239)의 「깃발 110 += 1」이 0 을 더해
                // 북 평원(Btl 0238, 깃발 110 == 2)이 영영 안 열렸다(사용자 보고, Chp 0064). 필드판과 달리 두 번 더하는 흠은 없다.
                if (A(0) > 0 && A(0) < _flags.Length)
                    _flags[A(0)] = FieldArith(_flags[A(0)], A(1), A(2));
                break;
        }
    }

    /// <summary>
    /// 사건이 거는 기술(207 보스 필살기 · 909 폭주)은 <b>돌던 루틴을 끝까지 돌린 뒤</b> 잇는다. 사건 조건은 루틴 도중에도 보므로
    /// (301 「가 나를 때렸다」는 적의 공격 루틴 한가운데서 참이 된다) 전에는 <c>_routine</c> 을 덮어써 적의 AI 루틴이 버려졌고,
    /// 그 끝의 <c>Rest</c> 가 안 불려 차례가 영영 안 넘어갔다(Btl 0143 사건 6 — 디에네의 나인 크루세이더, 사용자 보고).
    /// </summary>
    private static IEnumerator<bool> AfterRunning(IEnumerator<bool>? running, IEnumerator<bool> next)
    {
        if (running != null)
            while (running.MoveNext()) yield return true;
        while (next.MoveNext()) yield return true;
    }
}
