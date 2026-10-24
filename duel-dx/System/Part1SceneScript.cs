using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 챕터·필드 사건 실행기 — 사건 0 이 행동 0(사건 부르기)으로 조건이 맞는 사건을 돌리고, 할 일이 비면 사건 0 을 다시 훑는다
/// (깃발·변수가 바뀌어 새로 열린 사건이 이어 터지게 — 파트2 필드처럼 「매 틀 훑기」를 대사 단위로 줄인 것).
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-챕터」 4·7절. 옮긴 것: 조건 0(언제나) · 100 [변수, 값](장면 변수 같은가 — 가설) · 101 [깃발, 값, 연산](진행 깃발 비교) ·
/// 행동 0 사건 부르기 · 100 [그림] 배경 바꾸기 · 200·201·202 [얼굴, 줄] · 206·207·208 [얼굴, 줄, 목소리?] 대사(창 자리만 다르다 — 가설) · 203~205 대사 창 닫기 ·
/// 800 [변수, 값] 장면 변수 = 값 · 101 [변수] 장면 변수 + 1(필드 0001: 800 [1,1] → 사건 2 「변수 1 == 1」 → 끝의 101 [1] → 사건 3 「== 2」 — 자료로 본 가설) ·
/// 700 [곡, 되풀이] 배경음악(<c>BGMs</c>) · 802 [깃발, 값] 진행 깃발. 장면을 넘기는 행동(500 필드 끝 · 501 챕터 끝 → 연대표 · 502 전투 · 503 필드)은 <see cref="SceneChange"/> 가 맡는다.
/// 나머지(300대 인물 걷기·모습 · 400대 화면 · 700대 소리 · 900대 지도 연출 · 1000대)는 아직 건너뛴다.
/// 진행 깃발은 전투 사건(702·703 → 파트2 102·103)과 같은 <see cref="FlagStore"/> 다.
/// </remarks>
internal sealed class Part1SceneScript(GameWindow host)
{
    private readonly List<ScriptCommand> _pending = [];
    private IReadOnlyList<Part1BattleEvent> _events = [];
    private IReadOnlyList<string> _lines = [];
    private Dictionary<int, int> _fired = [];
    private readonly Dictionary<int, int> _vars = [];

    /// <summary>장면을 넘기는 행동(500 · 502 · 503) — 넘기면 true(실행을 멈춘다), 모르는 것이면 false.</summary>
    internal Func<ScriptCommand, bool>? SceneChange;

    /// <summary>행동 303 [인물 차례, x, y] — 필드 인물을 그 자리로 옮긴다(걷기 302 는 아직 없어 도착 자리만 맞춘다).</summary>
    internal Action<int, int, int>? SpritePlace;

    /// <summary>행동 302 [인물 차례, dx, dy, 틱] — 틱마다 (dx, dy) 씩 걷는다(필드 0002: [0, 0, -4, 90] 로 화면 아래에서 걸어 들어온다). 지금은 걸음 없이 도착 자리로 옮긴다.</summary>
    internal Action<int, int, int>? SpriteShift;

    /// <summary>행동 100 [그림] — 필드 배경을 그 <c>BGRs</c> 그림으로 바꾼다.</summary>
    internal Action<int>? BackgroundChange;

    /// <summary>
    /// 챕터 지도에서 일행이 막 닿은 장소(조건 200 [갈래, 장소] — <c>0x100bcfd0</c>: 무리의 지금 장소 ≠ 앞 장소이고 지금 장소 == 값). 없으면 -1.
    /// 갈래 0 은 「한 무리라도」, 1 은 「모든 무리가」인데 리메이크는 일행 하나라 같다. <see cref="Arrive"/> 가 한 번 훑는 동안만 산다.
    /// </summary>
    internal int Arrived = -1;

    private bool _sceneChanged;

    /// <summary>일행이 그 장소에 닿았다 — 조건 200 이 맞는 사건을 돌린다. 무엇이 일어났으면(사건 · 대사 · 장면 넘김) true.</summary>
    internal bool Arrive(int place)
    {
        int before = FiredEvents();
        _sceneChanged = false;
        Arrived = place;
        Run();
        Arrived = -1;
        return _sceneChanged || Talk != null || FiredEvents() != before;
    }

    /// <summary>사건 0(늘 도는 목록)을 뺀 발동 수 합.</summary>
    private int FiredEvents() => _fired.Where(f => f.Key != 0).Sum(f => f.Value);

    /// <summary>떠 있는 대사(말하는 이, 글) — 누르면 다음으로.</summary>
    internal (string Speaker, string Text)? Talk { get; private set; }

    /// <summary>
    /// 새로 돌린다 — <paramref name="fired"/> 는 발동 수(챕터는 진행에 남겨 지도로 돌아와도 들머리가 다시 안 나오게, 필드는 들어올 때마다 새것).
    /// <paramref name="resume"/> 면 남은 행동을 이어 돈다(전투를 마치고 돌아왔을 때).
    /// </summary>
    internal void Start(IReadOnlyList<Part1BattleEvent> events, IReadOnlyList<string> lines, Dictionary<int, int> fired, bool resume = false)
    {
        _events = events;
        _lines = lines;
        _fired = fired;
        Talk = null;
        _away = null;
        if (!resume)
        {
            _pending.Clear();
            _vars.Clear();
        }
        Run();
    }

    /// <summary>
    /// 세이브에 적을 남은 행동 — 지금 가 있는 장면(전투·필드)을 연 행동을 앞에 붙인다. 이어 하기는 그 장면부터 다시 한다
    /// (돌아와서 잇는 <see cref="Start"/>(resume) 는 그 행동을 빼고 남은 것만 돈다).
    /// </summary>
    internal IReadOnlyList<ScriptCommand> SavePending => _away is { } away ? [away, .. _pending] : _pending;

    /// <summary>장면을 넘긴 행동(502 · 503) — 그 장면을 마치고 돌아와 다시 돌기 시작하면 지운다.</summary>
    private ScriptCommand? _away;

    /// <summary>세이브에서 읽은 남은 행동을 넣는다 — 다음 <see cref="Start"/>(resume) 가 이어 돈다.</summary>
    internal void Restore(IEnumerable<ScriptCommand> pending)
    {
        _pending.Clear();
        _pending.AddRange(pending);
    }

    /// <summary>대사를 넘긴다.</summary>
    internal void Advance()
    {
        Talk = null;
        Run();
    }

    private bool Call(int index)
    {
        if (_events.FirstOrDefault(e => e.Index == index) is not { } e) return false;
        int fired = _fired.GetValueOrDefault(index);
        if (e.MaxFire >= 0 && fired >= e.MaxFire) return false;
        if (e.MaxFire > 1000) return false;                                 // 29999 번 도는 배경 움직임(필드 0007 사건 11) — 걸음을 바로 옮겨 끝없이 돌므로 건너뛴다
        if (!e.Conditions.All(Holds)) return false;
        _fired[index] = fired + 1;
        _pending.InsertRange(0, e.Actions);
        return true;
    }

    private bool Holds(ScriptCommand c)
    {
        short[] a = c.Args;
        return c.Code switch
        {
            0 => true,
            100 => _vars.GetValueOrDefault(a[0]) == a[1],
            200 => a[1] == Arrived,
            101 => BattleScene.Compare(a[0] >= 0 && a[0] < host.FlagSt._flags.Length ? host.FlagSt._flags[a[0]] : 0, a[2], a[1]),
            _ => false,                                                     // 아직 안 푼 조건은 안 터뜨린다
        };
    }

    private void Run()
    {
        // 할 일이 비면 사건 0 을 다시 훑는다 — 사건 0 은 발동 한도가 없어 늘 「부르기」만 넣으니, 그 훑기로 새로 터진 사건이 없으면 멈춘다.
        int firedBefore = -1;
        while (Talk == null)
        {
            if (_pending.Count == 0)
            {
                int firedNow = FiredEvents();
                if (firedNow == firedBefore) return;
                firedBefore = firedNow;
                if (!Call(0)) return;
            }
            var c = _pending[0];
            _pending.RemoveAt(0);
            short[] a = c.Args;
            switch (c.Code)
            {
                case 0: Call(a[0]); break;
                case >= 200 and <= 202 or >= 206 and <= 208:
                {
                    string raw = a[1] >= 0 && a[1] < _lines.Count ? _lines[a[1]] : "";
                    if (raw.Length > 0) Talk = (Part1Game.Speaker(raw).Trim(), Part1Game.PlainText(raw));
                    break;
                }
                case >= 203 and <= 205: Talk = null; break;
                case 100: BackgroundChange?.Invoke(a[0]); break;
                case 303: SpritePlace?.Invoke(a[0], a[1], a[2]); break;
                case 700: host.PlayMusicFile(Part1Game.BgmBase + a[0], loop: a[1] != 0); break;   // [곡, 되풀이] — 필드 0002: 700 [42, 1]
                case 302: SpriteShift?.Invoke(a[0], a[1] * a[3], a[2] * a[3]); break;
                case 101: _vars[a[0]] = _vars.GetValueOrDefault(a[0]) + 1; break;
                case 800: _vars[a[0]] = a[1]; break;
                case 802:
                    if (a[0] >= 0 && a[0] < host.FlagSt._flags.Length) host.FlagSt._flags[a[0]] = (byte)Math.Clamp((int)a[1], 0, 255);
                    break;
                case >= 500 and <= 503:
                    if (SceneChange?.Invoke(c) == true) { _sceneChanged = true; _away = c.Code is 502 or 503 ? c : null; return; }
                    break;
            }
        }
    }
}
