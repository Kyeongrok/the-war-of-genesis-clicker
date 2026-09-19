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

    /// <summary>이벤트가 정한 다음 전투 — 0 이면 <c>Btl</c> 자료의 값(또는 모세스)으로 간다.</summary>
    private int _eventNextBattle;

    /// <summary>이벤트 행동 6 이 정한 다음 필드 — 0 이면 모세스로 간다.</summary>
    private int _eventNextField;

    /// <summary>그 전투의 이벤트를 읽어 둔다.</summary>
    private void LoadEvents(int battleId)
    {
        _events = [];
        _eventFired = [];
        _turnNo = 0;
        _eventNextBattle = 0;
        _eventNextField = 0;
        Array.Clear(_battleVars);
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

    /// <summary>이벤트가 돌거나 대사가 떠 있으면 전투를 멈춘다.</summary>
    private bool EventsBusy => _runningEvent >= 0 || _talk != null;

    /// <summary>조건이 다 맞는 이벤트를 하나 켠다. 결과가 정해지면 더 보지 않는다.</summary>
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
            if (_eventWaitUntil > _lastTime) return;
            var e = _events[_runningEvent];
            if (_eventPc >= e.Actions.Count) { _runningEvent = -1; return; }

            var a = e.Actions[_eventPc++];
            switch (a.Code)
            {
                case 0: break;                                          // 다른 이벤트 부르기 — 그 이벤트가 제 조건으로 돈다
                case 1: break;                                          // 기다리기 — 위에서 이미 봤다
                case 2:
                    _eventWaitUntil = _lastTime + ((a.Args.Length > 0 ? a.Args[0] : 0)
                                                 | ((a.Args.Length > 1 ? a.Args[1] : 0) << 16)) / TicksPerSecond;
                    break;
                case 3: _runningEvent = -1; return;                     // 중단
                case 600: ShowTalk(box: true, a); return;
                case 601: ShowTalk(box: false, a); return;
                default:
                    RunEventAction(a);
                    if (_outcome.Length > 0) { _runningEvent = -1; return; }
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
                int side = A(0) >= 0 && A(0) < EventSideOrder.Length ? A(0) : -1;
                return side >= 0 && !_units.Any(u => u.Alive && u.Side == side);
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
            case 300:                                                           // 두 유닛이 같은 줄에 있고 두 칸 이내 = 맞닿음
            {
                var a = EventTargets(A(0), out _).Where(u => u.Alive).ToList();
                var b = EventTargets(A(2), out _).Where(u => u.Alive).ToList();
                return a.Any(x => b.Any(y => x != y && (x.Col == y.Col || x.Row == y.Row)
                                          && Math.Abs(x.Col - y.Col) + Math.Abs(x.Row - y.Row) <= 2));
            }
            case 301:                                                           // 한쪽이 다른 쪽을 지목하고 있다(교전) — 데모는 「맞닿음」으로 대신한다
                goto case 300;
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
                foreach (var u in EventTargets(A(0), out _))
                {
                    u.OnField = true;
                    u.ResetTo(A(2), A(3));
                    u.Facing = EdgeFacing(A(4));
                }
                _eventWaitUntil = _lastTime + 0.4;       // 걸어 들어오는 사이만큼 기다린다
                break;
            case 201:                                    // 퇴장 — 그 칸까지 갔다가 화면 밖으로
                foreach (var u in EventTargets(A(0), out _))
                {
                    u.ResetTo(A(2), A(3));
                    u.OnField = false;
                }
                break;
            case 202:                                    // 지정 칸으로 — 전장에 있는 사람만(원본도 맵 안인지 본다)
                foreach (var u in EventTargets(A(0), out _))
                    if (u.OnField) u.ResetTo(A(2), A(3));
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
            case 713:                                    // 아이템 하나 주기
                if (A(0) > 0) _inventory[A(0)] = _inventory.GetValueOrDefault(A(0)) + 1;
                break;
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
                if (A(0) > 0 && A(0) < _flags.Length)
                    _flags[A(0)] = (byte)Math.Clamp(_flags[A(0)] + A(1), 0, 255);
                break;
        }
    }
}
