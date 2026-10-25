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
/// 600 [소리] 효과음(<c>Snd</c>) · 700 [곡, 되풀이] 배경음악(<c>BGMs</c>) · 802 [깃발, 값] 진행 깃발. 장면을 넘기는 행동(500 필드 끝 · 501 챕터 끝 → 연대표 · 502 전투 · 503 필드)은 <see cref="SceneChange"/> 가 맡는다.
/// 필드만의 것(<see cref="IsField"/>, 분석-파트1-챕터 14절): 100 그림 덮개 · 101 빛깔 화면 · 102 덮개 걷기 · 103~107 화면 바꾸기 · 306 뒤집기 · 307 그림자 ·
/// 400 화면 자리 · 600~605 효과음 · 701~704 배경음악 기다림·멈춤·작게·크게 · 801 장면 변수 += · 900·901 고르기 · 902~904 글 쪽 창 · 501 다음 필드 · 506 챕터 끝.
/// 같은 번호라도 챕터에서는 뜻이 다르다(900~904 는 지도 말·팀, 101 은 안 쓴다).
/// 진행 깃발은 전투 사건(702·703 → 파트2 102·103)과 같은 <see cref="FlagStore"/> 다.
/// </remarks>
internal sealed class Part1SceneScript(GameWindow host)
{
    private readonly List<ScriptCommand> _pending = [];
    private IReadOnlyList<Part1BattleEvent> _events = [];
    private IReadOnlyList<string> _lines = [];
    private Dictionary<int, int> _fired = [];
    /// <summary>장면 변수(800 · 101 · 조건 100) — 챕터는 진행에 남는 것을, 필드는 새것을 받는다(<see cref="Start"/>).</summary>
    private Dictionary<int, int> _vars = [];

    /// <summary>장면을 넘기는 행동(500 · 502 · 503) — 넘기면 true(실행을 멈춘다), 모르는 것이면 false.</summary>
    internal Func<ScriptCommand, bool>? SceneChange;

    /// <summary>행동 303 [인물 차례, x, y] — 필드 인물을 그 자리로 옮긴다(걷기 302 는 아직 없어 도착 자리만 맞춘다).</summary>
    internal Action<int, int, int>? SpritePlace;

    /// <summary>행동 302 [인물 차례, dx, dy, 틱] — 틱마다 (dx, dy) 씩 걷는다(필드 0002: [0, 0, -4, 90] 로 화면 아래에서 걸어 들어온다).</summary>
    internal Action<int, int, int, int>? SpriteShift;

    /// <summary>
    /// 필드 300 [10000 + 인물 차례, 모션, 되풀이] — 모션 번호를 바로 건다(0x100cdca0: +0x54 = 0, vt+0x80(모션), +0x46 = 0 틱 켬). 되풀이 = 1 이면 칸을 곧 비우고
    /// 모션은 돈다. 그 밖이면 한 바퀴 돌 때까지 칸에 남고(행동 1 이 기다린다) 끝 그림에서 멈춘다(+0x46 = −1). 뒤집기는 건드리지 않는다(+0x68 은 306 만).
    /// 돌려주는 값 = 칸에 남을 초(되풀이면 0). 자료: 걷기 모션 3~5 는 거의 1, 한 번 하는 동작(174 · 175 …)은 0.
    /// </summary>
    internal Func<int, int, bool, double>? SpriteMotion;

    /// <summary>
    /// 필드 300 [Obs, 모션, −, 층, x, y](Obs &lt; 10000) — 효과 그림 하나(0x6c 개체 0x1009f4d0(Obs, 모션, 화면 자리 + (x, y)))를 층(0~2, +0x78 + 층 × 0x1c)에 넣는다.
    /// 그 모션이 한 바퀴 돌 때까지 칸에 남고(+0x54 ≥ 모션 길이) 끝나면 지운다. 필드 0019 · 0025 · 0090 · 0093 의 Obs 91(빛 기둥 0 → 1) · 0072 · 0078 의 388 · 279 …
    /// 돌려주는 값 = 칸에 남을 초.
    /// </summary>
    internal Func<int, int, int, int, int, double>? EffectStart;

    /// <summary>필드 301 [인물] — 모션 틱을 멈춘다(0x100cdbe0: +0x46 = −1 — 지금 그림에서 선다).</summary>
    internal Action<int>? SpriteFreeze;

    /// <summary>행동 304 [인물 차례] 숨기기 · 305 [인물 차례] 보이기(필드 0007 은 들머리에서 여럿을 숨겼다가 차례로 보인다).</summary>
    internal Action<int, bool>? SpriteVisible;

    /// <summary>행동 401 [dx, dy, 틱] — 화면을 틱마다 (dx, dy) 씩, 틱 수만큼 민다(0x100cd950, 그림 끝에서 자른다).</summary>
    internal Action<int, int, int>? CameraPan;

    /// <summary>필드 실행기 — 필드와 챕터는 같은 번호를 다른 뜻으로 쓴다(필드 처리기 분기 0x100d1590, 챕터 0x100c2010).</summary>
    internal bool IsField;

    /// <summary>필드 100 [BGRs, x, y, 대사 보임] — 화면 가득 그림을 덮는다(0x100cf1f0: 덮개 0x101b454c, 자리 0x101b4548/4544, 대사 0x101b4540).</summary>
    internal Action<int, int, int, bool>? PictureShow;

    /// <summary>필드 101 [빛깔] — 화면을 한 빛깔로 덮는다(0x100cf0b0: 0 흰 · 1 검 · 2 빨).</summary>
    internal Action<int>? ColorShow;

    /// <summary>필드 102 — 덮개를 걷어 필드(배경 + 인물)로 돌아간다(0x100cf1c0).</summary>
    internal Action? PictureClear;

    /// <summary>
    /// 필드 103~107 [갈래, 빛깔, 나감 틱, 들어옴 틱] — 화면 바꾸기(0x100ab310: 단계값 0→31 을 31/나감 씩 올려 옛 화면을 빛깔로 바래고, 31→63 을 31/들어옴 씩 올려 새 화면을 드러낸다).
    /// 갈래 0 = 덮개(100·101)로, 1 = 필드 그대로로(끝에 덮개를 지운다). 빛깔 0 = 0xe(흰 — 가설) · 1 = 0xf(검 — 전투 끝 바램 0x1002c8da 와 같다) · 2 = 0x20(가설).
    /// </summary>
    internal Action<int, int, int, int>? Transition;

    /// <summary>필드 306 [인물] — 좌우 뒤집기를 바꾼다(0x100cdc20: +0x68 = !+0x68).</summary>
    internal Action<int>? SpriteFlip;

    /// <summary>필드 307 [인물] — 발밑 그림자(Obs 0074)를 켠다(0x100cdc60: +0x4e = 1, 그리기 0x100c81a4). 304 가 끈다.</summary>
    internal Action<int, bool>? SpriteShadow;

    /// <summary>필드 400 [x, y] — 화면 자리를 바로 옮긴다(0x100cd880, 그림 끝에서 자른다).</summary>
    internal Action<int, int>? CameraSet;

    /// <summary>필드 고르기(900 [그림, 물음 줄, 변수] + 901 [고를 줄] ×) — 고르면 장면 변수 = 1부터 센 차례(0x100d02b0 → 0x100aa690, 0x101bb14c + 변수).</summary>
    internal sealed class Choosing(string speaker, string text, int variable)
    {
        public readonly string Speaker = speaker, Text = text;
        public readonly int Variable = variable;
        public readonly List<string> Options = [];
    }

    /// <summary>떠 있는 고르기 — 행동 1 이 고를 때까지 기다린다.</summary>
    internal Choosing? Choice { get; private set; }

    /// <summary>고를 줄이 다 떴고(901 다음의 행동 1 에 닿았다) 고르기를 기다리는 중.</summary>
    internal bool WaitingOnChoice => Choice != null && _waitBusy;

    /// <summary>고른다(0부터) — 장면 변수에 1부터 센 차례를 넣고 잇는다.</summary>
    internal void Choose(int index)
    {
        if (Choice is not { } c || index < 0 || index >= c.Options.Count) return;
        _vars[c.Variable] = index + 1;
        Choice = null;
        Run();
    }

    /// <summary>필드 903 이 쌓은 글 쪽(줄 넷) — 904 가 차례로 띄운다(0x100d0900 · 0x100d0440).</summary>
    private readonly List<short[]> _pageTable = [];

    /// <summary>904 가 띄울 남은 쪽.</summary>
    private readonly Queue<string> _pages = [];

    /// <summary>필드 행동이 건 「칸」(화면 바꾸기 · 음악 크기)이 끝나는 때 — 행동 1 이 기다린다(원본 실행기는 칸이 다 빌 때까지 1 에 머문다).</summary>
    private double _slotsUntil;

    /// <summary>602 — 600 의 소리가 끝날 때까지.</summary>
    private bool _waitSound;

    /// <summary>필드 600 의 소리 칸(0x101b452c) · 604 의 되풀이 칸(0x101b4528).</summary>
    private const int SoundTag = 0x5012, LoopTag = 0x5013;

    /// <summary>필드 소리를 끊는다 — 필드를 떠날 때.</summary>
    internal void StopSounds()
    {
        host._mixer.StopEffect(SoundTag);
        host._mixer.StopEffect(LoopTag);
    }

    /// <summary>
    /// 지도 말 — 차례(0부터) → 장소. 챕터를 열 때 처음 장소로 채우고, 행동 900 [말(1부터), 장소] 이 옮긴다(<see cref="MarkerMoved"/> 가 그 뒤를 맡는다).
    /// 조건 301 [말, v] 은 「말이 살아 있나」(원본 +0xe — 리메이크는 늘 산 것으로 본다, 가설), 302 [장소, 말, v] 는 「말이 그 장소에 있나」.
    /// </summary>
    internal List<int> Markers = [];

    /// <summary>지도 말의 앞 장소(원본 +0xc — 900 이 옮길 때 옛 장소를 적는다). 조건 201(막 옮겨 닿음) · 303(머무름)이 본다. 열 때는 지금 장소로.</summary>
    internal List<int> MarkerPrev = [];

    /// <summary>그 팀이 선 장소(없는 팀이면 −1) — 조건 302 [장소, 팀, v] 가 본다.</summary>
    internal Func<int, int>? TeamPlace;

    /// <summary>904 [팀, Chr] — 그 사람을 그 팀으로(0x100c12e0: 팀이 없으면 만들고 장소 0 에 새 팀 말을 놓는다).</summary>
    internal Action<int, int>? TeamJoin;

    /// <summary>903 [팀, 장소] — 팀 말을 전투 없이 그 장소로(0x100c14a0 → 0x100b2840). 리메이크는 팀 0(일행)만 있다.</summary>
    internal Action<int, int>? TeamMoved;

    private int Prev(int m) => m >= 0 && m < MarkerPrev.Count ? MarkerPrev[m] : m >= 0 && m < Markers.Count ? Markers[m] : -1;

    /// <summary>지금 챕터 지도 턴 — 조건 1 [값, 연산] 이 본다(챕터만).</summary>
    internal Func<int>? MapTurn;

    /// <summary>900 으로 말이 옮겨졌다(말 차례, 장소) — 일행이 선 전투 장소면 그 전투가 열린다(0x100c10a0). 장면을 넘겼으면 true.</summary>
    internal Func<int, int, bool>? MarkerMoved;

    /// <summary>챕터 행동 1201 [장소, 전투] — 그 장소를 그 전투 장소로 바꾼다(0x100c1a50: 장소 낱말 7 = 전투, 낱말 6 = 0).</summary>
    internal Action<int, int>? PlaceBattle;

    /// <summary>행동 100 [그림] — 필드 배경을 그 <c>BGRs</c> 그림으로 바꾼다.</summary>
    internal Action<int>? BackgroundChange;

    /// <summary>챕터 조건 202 [0, 장소] — 「진입」한 장소(0x101b4444). <see cref="Part1ChapterScreen.EnterHere"/> 가 훑는 동안만 산다.</summary>
    internal int Entered = -1;

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
    /// 대사 상자 셋(0 왼쪽 200·206 · 1 오른쪽 201·207 · 2 가운데 202·208)마다 띄운 일러스트 — Obss 번호(0 이면 없음).
    /// 원본 <c>0x100a9000</c>: 상자를 닫는 203~205 가 그 칸을 지운다(분석-파트1-챕터 11절). 여러 칸이 함께 떠 있을 수 있다.
    /// </summary>
    internal readonly int[] Faces = new int[3];

    /// <summary>지금 대사가 뜬 상자(0 왼 · 1 오른 · 2 가운데 · <see cref="BubbleSlot"/> 필드 인물 말풍선 · <see cref="NarrationSlot"/> 위쪽 해설 띠).</summary>
    internal int TalkSlot { get; private set; } = 2;

    internal const int BubbleSlot = 3, NarrationSlot = 4, PageSlot = 5;

    /// <summary>209 의 말하는 필드 인물(차례) — 말풍선을 그 머리 위에.</summary>
    internal int TalkPerson { get; private set; } = -1;

    /// <summary>209 의 얼굴(Chr 칸 4 반신 초상 Obs, 0 없음) · 209 · 210 의 저절로 넘김 틱 · 새 209 · 210 마다 오르는 번호 — 말풍선(<see cref="Part1Bubble"/>)이 쓴다.</summary>
    internal int TalkFace { get; private set; }

    internal int TalkTicks { get; private set; }

    internal int TalkSerial { get; private set; }

    /// <summary>209 · 210 은 정한 틱이 지나면 저절로 넘어간다(누르면 먼저 넘어간다). 0 이면 저절로 안 넘어감.</summary>
    private double _talkAutoAt;

    /// <summary>성우 대사의 재생 표지 — 다음 성우 대사가 앞 목소리를 끊는다.</summary>
    private const int VoiceTag = 0x5011;

    /// <summary>지금 성우 목소리가 끝나는 때 — 대사를 넘기면 그때 끊는다(원본은 끝날 때까지 다음 행동을 미룬다, 분석-파트1-챕터 11절).</summary>
    private double _voiceEnd;

    /// <summary>목소리 길이를 알았나(파일을 이미 받아 둠) — 모르면 저절로 넘기지 않는다.</summary>
    private bool _voiceKnown;

    /// <summary>목소리가 끝난 뒤 넘기기까지 쉬는 틈(초).</summary>
    private const double VoiceAfterSeconds = 0.8;

    /// <summary>
    /// 새로 돌린다 — <paramref name="fired"/> 는 발동 수(챕터는 진행에 남겨 지도로 돌아와도 들머리가 다시 안 나오게, 필드는 들어올 때마다 새것).
    /// <paramref name="resume"/> 면 남은 행동을 이어 돈다(전투를 마치고 돌아왔을 때).
    /// </summary>
    internal void Start(IReadOnlyList<Part1BattleEvent> events, IReadOnlyList<string> lines, Dictionary<int, int> fired, bool resume = false,
                        Dictionary<int, int>? vars = null)
    {
        _events = events;
        _lines = lines;
        _fired = fired;
        _vars = vars ?? [];
        Talk = null;
        Choice = null;
        _pages.Clear();
        _pageTable.Clear();
        _waitUntil = 0;
        _waitBusy = false;
        _slotsUntil = 0;
        _waitSound = false;
        _leaving = false;
        _away = null;
        if (!resume) { _pending.Clear(); Array.Clear(Faces); Place = (-1, -1); }
        // 성우 대사(206~208 의 셋째 인자 = Snd 번호)는 처음 틀 때 받느라 조용히 지나가므로 장면을 열 때 미리 받아 둔다.
        host.PreloadSounds([.. events.SelectMany(e => e.Actions).Where(a => a.Code is >= 206 and <= 208 && a.Args.Length > 2 && a.Args[2] > 0)
                                     .Select(a => Part1Game.ObsBase + a.Args[2])]);
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

    /// <summary>
    /// 기다림을 지키나 — 필드만 켠다. 행동 2 [틱] 은 그만큼 쉬고, 행동 1 은 걷던 인물이 다 설 때까지(<see cref="Busy"/>) 쉰다.
    /// 챕터는 끄고 바로 넘긴다(지도 연출이 아직 없어 기다릴 것이 없다).
    /// </summary>
    internal bool Timed;

    /// <summary>챕터에서도 기다림을 지킬 때 — 그림 덮개 · 화면 바꾸기(챕터 100 · 103)가 떠 있는 동안(그것이 보이게).</summary>
    internal Func<bool>? TimedNow;

    private bool IsTimed => Timed || TimedNow?.Invoke() == true;

    /// <summary>챕터 505 — 지도 단추 · 떠 있는 창 · 이동 고르기를 닫는다(0x100c0d50 → 0x100b3f90: 단추 다섯 숨김 0x100b2070, 창 +0x13bf8 · +0x13bfc 지움, 고르기 +0x13c30 = 0).</summary>
    internal Action? UiReset;

    /// <summary>챕터 705 [n] — 영상 n(0 = G3.pak 안 · 1 = Setup\_sys2.cab)을 틀고 장면 7(불러오기 · 타이틀 · 새로)로(0x100c1ab0 → 장면 6 0x100d5480 DirectShow). 챕터 55 끝(엔딩) · 56.</summary>
    internal Action<int>? Ending;

    /// <summary>행동 1 이 기다릴 일 — 걷는 인물이 있나.</summary>
    internal Func<bool>? Busy;

    private double _waitUntil;
    private bool _waitBusy;

    /// <summary>다 돌았나 — 대사도 기다림도 남은 행동도 없다.</summary>
    internal bool Idle => Talk == null && Choice == null && _waitUntil == 0 && !_waitBusy && !_waitSound && _pending.Count == 0 && !_leaving;

    /// <summary>장면을 넘겼다(500~506) — 넘어가는 바램 동안 「다 돌았다」로 보지 않는다(그 사이 누름이 필드를 한 번 더 끝내지 않게).</summary>
    private bool _leaving;

    /// <summary>쉬는 중이었으면 때가 됐는지 보고 잇는다 — 필드 화면이 틀마다 부른다.</summary>
    internal void Pump()
    {
        if (Talk != null && _talkAutoAt > 0 && host._lastTime >= _talkAutoAt) { Advance(); return; }
        // 성우 대사는 목소리가 끝나고 조금 뒤 저절로 넘어간다(사용자 요청) — 장이 남았으면 다음 장부터. 받는 중이라 길이를 모르면(30초 어림) 기다리지 않는다.
        if (Talk != null && _voiceEnd > 0 && _voiceKnown && host._lastTime >= _voiceEnd + VoiceAfterSeconds)
        {
            _voiceEnd = 0;
            if (!host.Part1TitleScr.NextTalkPage()) Advance();
            return;
        }
        if (Talk == null && (_waitUntil > 0 || _waitBusy || _waitSound)) Run();
    }

    /// <summary>울리는 성우 목소리를 끊는다 — 대사를 넘길 때 · 장면을 떠날 때.</summary>
    internal void StopVoice()
    {
        if (host._lastTime < _voiceEnd) host._mixer.StopEffect(VoiceTag);
        _voiceEnd = 0;
    }

    /// <summary>대사를 넘긴다.</summary>
    internal void Advance()
    {
        Talk = null;
        _talkAutoAt = 0;
        // 904 글 쪽 창 — 남은 쪽이 있으면 다음 쪽(마지막 쪽을 넘기면 잇는다).
        if (_pages.Count > 0) { Talk = ("", _pages.Dequeue()); return; }
        // 넘기면 목소리도 끊는다(사용자 요청 — 원본은 목소리가 끝날 때까지 기다리지만 넘길 때 끝까지 나오면 답답하다).
        StopVoice();
        Run();
    }

    /// <summary>방금 돈 행동의 자리 — 사건 번호 · 그 사건 안 줄(0부터). 장면 번호 꼬리표(설정 > 장면 번호 보이기)가 「사건 n · 줄 m」으로 보인다. 없으면 −1.</summary>
    internal (int Event, int Line) Place { get; private set; } = (-1, -1);

    private void NotePlace(ScriptCommand c)
    {
        foreach (var e in _events)
            for (int i = 0; i < e.Actions.Count; i++)
                if (ReferenceEquals(e.Actions[i], c)) { Place = (e.Index, i); return; }
    }

    private static void AddCount(Dictionary<int, int> pool, int chr, int count) =>
        pool[chr] = Math.Max(0, pool.GetValueOrDefault(chr) + count);

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
            100 => BattleScene.Compare(_vars.GetValueOrDefault(a[0]), a[2], a[1]),   // [변수, 값, 연산](챕터 0x100bcf70 → 0x100bce90) — 자료의 연산은 모두 0(같다)
            202 => a[0] == 0 && Entered >= 0 && a[1] == Entered,                       // [0, 장소] 그 장소에 진입(0x100bd320)
            200 => a[1] == Arrived,
            // 조건은 말을 0부터 센다(0x100bd140 · 0x100bd510 · 0x100bd550 — 칸을 바로 쓴다), 행동 900 · 902 는 1부터(칸 a0 − 1). 분석-파트1-챕터 10.2.
            // 201 [말, 장소] 막 옮겨(지금 ≠ 앞) 그 장소에 닿음 · 301 [말, v] 살았나 · 302 [장소, 팀, v] 팀이 그 장소에(팀 0 = 일행) ·
            // 303 [장소, 말, v] 말이 살았고 지금·앞 둘 다 그 장소(머무름). v = 0 이면 뒤집는다.
            201 => a[0] >= 0 && a[0] < Markers.Count && Markers[a[0]] >= 0 && Markers[a[0]] == a[1] && Prev(a[0]) != a[1],
            301 => (a[0] >= 0 && a[0] < Markers.Count && Markers[a[0]] >= 0) == (a[1] != 0),
            302 => (TeamPlace?.Invoke(a[1]) == a[0]) == (a[2] != 0),
            303 => (a[1] >= 0 && a[1] < Markers.Count && Markers[a[1]] == a[0] && Prev(a[1]) == a[0]) == (a[2] != 0),
            1 => MapTurn != null && BattleScene.Compare(MapTurn(), a[1], a[0]),     // [값, 연산] — 지도 턴(0x100bcf40 → 0x100bce90)
            101 => BattleScene.Compare(a[0] >= 0 && a[0] < host.FlagSt._flags.Length ? host.FlagSt._flags[a[0]] : 0, a[2], a[1]),
            _ => false,                                                     // 아직 안 푼 조건은 안 터뜨린다
        };
    }

    private void Run()
    {
        // 할 일이 비면 사건 0 을 다시 훑는다 — 사건 0 은 발동 한도가 없어 늘 「부르기」만 넣으니, 그 훑기로 새로 터진 사건이 없으면 멈춘다.
        int firedBefore = -1, rescans = 0;
        while (Talk == null)
        {
            if (_waitUntil > 0)
            {
                if (host._lastTime < _waitUntil) return;
                _waitUntil = 0;
            }
            if (_waitBusy)
            {
                // 행동 1 — 걷는 인물(Busy) · 화면 바꾸기 · 음악 크기 칸 · 떠 있는 고르기가 다 끝날 때까지.
                if (Busy?.Invoke() == true || host._lastTime < _slotsUntil || Choice != null) return;
                _waitBusy = false;
            }
            if (_waitSound)
            {
                if (host._mixer.IsPlaying(SoundTag)) return;
                _waitSound = false;
            }
            if (_pending.Count == 0)
            {
                int firedNow = FiredEvents();
                // 발동 한도가 없는 사건이 조건을 늘 채우면 훑을 때마다 다시 터진다 — 한 번 돌 때 훑기를 64 번으로 자른다(멈춤 막기).
                if (firedNow == firedBefore || ++rescans > 64) return;
                firedBefore = firedNow;
                if (!Call(0)) return;
            }
            var c = _pending[0];
            _pending.RemoveAt(0);
            NotePlace(c);
            short[] a = c.Args;
            if (IsField && RunField(c)) { if (_sceneLeft) return; continue; }
            // 챕터만의 것 — 100 · 102 · 103 은 필드와 같은 덮개 틀(0x100bf890 CBgr · 0x100bf860 · 0x100bf960 0x1009b0f0 떠 두기),
            // 1100~1104 는 필드 900~904 와 같은 고르기 · 표 창(0x100bf0a0 → 0x100aa690 · 0x100bf1d0 → 0x100aa750 · 0x101b43cc 표 · 0x100a9000).
            if (!IsField && c.Code is 100 or 102 or 103 or >= 1100 and <= 1104)
            {
                RunField(c.Code >= 1100 ? new ScriptCommand(c.Code - 200, c.Args) : c);
                continue;
            }
            if (!IsField && c.Code == 505) { UiReset?.Invoke(); continue; }
            if (!IsField && c.Code == 705 && Ending != null) { _pending.Clear(); _sceneChanged = true; Ending(a.Length > 0 ? a[0] : 0); return; }
            switch (c.Code)
            {
                case 0: Call(a[0]); break;
                case 1: _waitBusy = IsTimed || Choice != null; break;   // 챕터 고르기(1100)도 고를 때까지 기다린다
                case 2: if (IsTimed && a[0] > 0) _waitUntil = host._lastTime + a[0] / GameWindow.TicksPerSecond; break;
                // 대사 [얼굴(Obss 번호), 줄] — 200·201·202 는 왼 · 오른 · 가운데 상자, 206·207·208 은 같은 자리에 성우 [얼굴, 줄, Snd 번호].
                case >= 200 and <= 202 or >= 206 and <= 208:
                {
                    int slot = c.Code >= 206 ? c.Code - 206 : c.Code - 200;
                    Faces[slot] = a[0] > 0 ? a[0] : 0;
                    TalkSlot = slot;
                    string raw = a[1] >= 0 && a[1] < _lines.Count ? _lines[a[1]] : "";
                    if (raw.Length > 0) Talk = (Part1Game.Speaker(raw).Trim(), Part1Game.PlainText(raw));
                    if (c.Code >= 206 && a.Length > 2 && a[2] > 0)
                    {
                        host._mixer.StopEffect(VoiceTag);
                        host.Play(Part1Game.ObsBase + a[2], VoiceTag);
                        PcmSound? clip;
                        lock (host._sfx) host._sfx.TryGetValue(Part1Game.ObsBase + a[2], out clip);
                        _voiceEnd = clip != null ? host._lastTime + GameWindow.ClipSeconds(clip) : host._lastTime + 30;   // 아직 안 받았으면 넉넉히 — 넘길 때 끊게
                        _voiceKnown = clip != null;
                    }
                    break;
                }
                case >= 203 and <= 205: Faces[c.Code - 203] = 0; Talk = null; break;
                // 209 [필드 인물, 얼굴, 줄, 틱] — 그 인물 머리 위 말풍선, 틱 뒤 저절로 넘어간다. 210 [줄, 틱] — 화면 위 해설 띠(10, 5, 619×35), 그림 없음.
                case 209 or 210:
                {
                    int line = c.Code == 209 ? a[2] : a[0], ticks = c.Code == 209 ? a[3] : a[1];
                    string raw = line >= 0 && line < _lines.Count ? _lines[line] : "";
                    if (raw.Length == 0) break;
                    Talk = (Part1Game.Speaker(raw).Trim(), Part1Game.PlainText(raw));
                    TalkSlot = c.Code == 209 ? BubbleSlot : NarrationSlot;
                    TalkPerson = c.Code == 209 ? a[0] : -1;
                    TalkFace = c.Code == 209 ? a[1] : 0;
                    TalkTicks = ticks;
                    TalkSerial++;
                    // 틱은 쪽을 다 찍은 뒤부터 센다(0x100a8685) — 말풍선이 제 때를 재고 넘긴다. 필드 밖(챕터)은 말풍선이 없으니 예전대로.
                    _talkAutoAt = Timed && ticks > 0 && !IsField ? host._lastTime + ticks / GameWindow.TicksPerSecond : 0;
                    break;
                }
                case 100: BackgroundChange?.Invoke(a[0]); break;
                case 303: SpritePlace?.Invoke(a[0], a[1], a[2]); break;
                // [Chr, 8, 무리] — 그 인물을 일행 무리로 옮긴다(0x100c1890: 지금 무리에서 빼 무리 a2 에 넣는다, 무리 표 0x101a0828).
                // 리메이크는 일행이 하나라 「일행에 든다」로 친다(챕터 1: 누르 에드 딘 · 일 사담 · 이븐 시나).
                case 1000 when a[1] == 8 && a[0] >= 0: host.Part1.MoveToGroup(Part1Game.ChrBase + a[0], a[2]); break;
                // [무리, Chr, 수] — 무리의 용병(908, 0x100c1610 → 0x100adb60, 칸 +0x910) · 탑승물(910, 0x100c1730 → 0x100adcc0, 칸 +0xa14)에 더한다.
                // 무리 99 는 지금 무리. 챕터 0 들머리: 용병 알가지 5 · 바시아조크 5, 탑승물 아지다하카 3.
                // 906 [무리, 아이템, 수] = 무리 가방에 아이템(0x100c14f0 → 0x100ada10, 챕터 0: 다마스커스 2 · 회복약 10 · 해독제 10 …) ·
                // 912 [eld] = 무리 돈(0x100c1850, 0x101a07e0 — 챕터 0: 3000) · 904 [무리, Chr] = 그 인물을 무리 명단에(0x100c12e0 — 일행에 든다로 친다).
                case 906 when a[1] > 0: for (int k = 0; k < Math.Clamp((int)a[2], 0, 99); k++) host.Part1.Bag.Add(a[1]); break;
                case 1201: PlaceBattle?.Invoke(a[0], a[1]); break;
                case 900 when a[0] >= 1 && a[0] - 1 < Markers.Count:
                    while (MarkerPrev.Count < Markers.Count) MarkerPrev.Add(Markers[MarkerPrev.Count]);
                    MarkerPrev[a[0] - 1] = Markers[a[0] - 1];
                    Markers[a[0] - 1] = a[1];
                    if (MarkerMoved?.Invoke(a[0] - 1, a[1]) == true) { _sceneChanged = true; _away = c; return; }   // 이어 하기가 이 전투부터 다시 하게
                    break;
                // 902 [말] — 말을 지도에서 뺀다(0x100c1270: 살았나 +0xe = 0). 챕터 10 사건 12 · 28 사건 2.
                case 902 when a[0] >= 1 && a[0] - 1 < Markers.Count: Markers[a[0] - 1] = -1; break;
                case 903: TeamMoved?.Invoke(a[0], a[1]); break;
                case 912: host.Part1.Eld = Math.Max(0, host.Part1.Eld + a[0]); break;
                case 904 when a[1] >= 0:
                    host.Part1.Join([Part1Game.ChrBase + a[1]]);
                    TeamJoin?.Invoke(a[0], Part1Game.ChrBase + a[1]);
                    break;
                case 908 when a[1] > 0: AddCount(host.Part1.Mercenaries, Part1Game.ChrBase + a[1], a[2]); break;
                case 910 when a[1] > 0: AddCount(host.Part1.Mounts, Part1Game.ChrBase + a[1], a[2]); break;
                case 600: host.Play(Part1Game.ObsBase + a[0]); break;                     // [소리] — Snd/NNNN.snd(필드 0006: 600 [1852, 0])
                case 700: host.PlayMusicFile(Part1Game.BgmBase + a[0], loop: a[1] != 0); break;   // [곡, 되풀이] — 필드 0002: 700 [42, 1]
                case 302: SpriteShift?.Invoke(a[0], a[1], a[2], a[3]); break;
                case 304 or 305: SpriteVisible?.Invoke(a[0], c.Code == 305); break;
                case 401: CameraPan?.Invoke(a[0], a[1], a[2]); break;
                case 101: _vars[a[0]] = _vars.GetValueOrDefault(a[0]) + 1; break;
                case 800: _vars[a[0]] = a[1]; break;
                case 802:
                    if (a[0] >= 0 && a[0] < host.FlagSt._flags.Length) host.FlagSt._flags[a[0]] = (byte)Math.Clamp((int)a[1], 0, 255);
                    break;
                case >= 500 and <= 503:
                    if (SceneChange?.Invoke(c) == true) { _sceneChanged = true; _leaving = IsField; _away = c.Code is 502 or 503 ? c : null; return; }
                    break;
            }
        }
    }

    /// <summary><see cref="RunField"/> 가 장면을 넘겼다 — 실행을 멈춘다.</summary>
    private bool _sceneLeft;

    private static double TicksToSeconds(int ticks) => Math.Max(0, ticks) / GameWindow.TicksPerSecond;

    /// <summary>
    /// 필드만의 행동(필드 처리기 분기 0x100d1590) — 맡았으면 true. 챕터와 번호가 겹치는 것(101 · 900~904 · 501 · 503 · 506)도 여기서 필드 뜻으로 돈다.
    /// </summary>
    private bool RunField(ScriptCommand c)
    {
        short[] a = c.Args;
        short A(int i) => i < a.Length ? a[i] : (short)0;
        _sceneLeft = false;
        switch (c.Code)
        {
            case 100: PictureShow?.Invoke(A(0), A(1), A(2), A(3) != 0); return true;
            case 101: ColorShow?.Invoke(A(0)); return true;
            case 102: PictureClear?.Invoke(); return true;
            case >= 103 and <= 107:
            {
                // 103 [갈래, 빛깔, 나감, 들어옴] · 104·105 [갈래, 띠, 틱](0x100ab550 · 0x100ab880 — 닦아 내기) · 106·107 [갈래, …](0x100abf70 · 0x100ac870).
                // 104~107 의 닦기 무늬는 아직 없어 그 틱을 반씩 검게 바래고 드러내는 것으로 갈음한다(가설).
                int kind = c.Code == 103 ? A(1) : 1;
                int fadeOut = Math.Max(1, c.Code == 103 ? A(2) : A(2) / 2), fadeIn = Math.Max(1, c.Code == 103 ? A(3) : A(2) - A(2) / 2);
                Transition?.Invoke(A(0), kind, fadeOut, fadeIn);
                if (IsTimed) _slotsUntil = Math.Max(_slotsUntil, host._lastTime + TicksToSeconds(fadeOut + fadeIn));
                return true;
            }
            case 300:
            {
                // 칸에 남는 동안 행동 1 이 기다린다 — 너무 긴 모션에 장면이 서지 않게 4 초로 자른다(원본은 끝까지 — 리메이크 갈음).
                double hold = A(0) >= 10000 ? SpriteMotion?.Invoke(A(0) - 10000, A(1), A(2) == 1) ?? 0 : EffectStart?.Invoke(A(0), A(1), A(3), A(4), A(5)) ?? 0;
                if (IsTimed && hold > 0) _slotsUntil = Math.Max(_slotsUntil, host._lastTime + Math.Min(hold, 4));
                return true;
            }
            case 301: SpriteFreeze?.Invoke(A(0)); return true;                 // [인물] 모션 틱을 멈춘다(+0x46 = −1)
            case 304: SpriteShadow?.Invoke(A(0), false); SpriteVisible?.Invoke(A(0), false); return true;   // 숨기면 그림자도 끈다(0x100cdb7f)
            case 306: SpriteFlip?.Invoke(A(0)); return true;
            case 307: SpriteShadow?.Invoke(A(0), true); return true;
            case 400: CameraSet?.Invoke(A(0), A(1)); return true;
            case 600:                                                         // [소리] 칸 소리 — 앞 소리를 끊고 튼다(0x100cd6b0)
                host._mixer.StopEffect(SoundTag);
                if (A(0) > 0) host.Play(Part1Game.ObsBase + A(0), SoundTag);
                return true;
            case 601: host._mixer.StopEffect(SoundTag); return true;           // 칸 소리 끄기(0x100cd720)
            case 602: _waitSound = Timed; return true;                       // 칸 소리가 끝날 때까지(0x100cd740)
            case 603: if (A(0) > 0) host.Play(Part1Game.ObsBase + A(0)); return true;   // 한 번 — 칸 없이(0x100cd7a0 → 0x1001a330)
            case 604:                                                         // 되풀이 소리(0x100cd7f0 → 0x1001a910(소리, 1))
                host._mixer.StopEffect(LoopTag);
                if (A(0) > 0) host.Play(Part1Game.ObsBase + A(0), LoopTag, loop: true);
                return true;
            case 605: host._mixer.StopEffect(LoopTag); return true;
            case 701: return true;                                            // CD 곡이 끝날 때까지(0x100cd510 → 0x100096d0) — 되풀이 곡뿐이라 안 기다린다(가설)
            case 702: host.StopMusic(); return true;                          // 곡 멈춤(0x100cd550)
            case 703:                                                         // [걸음] 크기 127 에서 틱마다 걸음씩 0 까지(0x100cd570, 0x1011beb0)
            {
                int ticks = A(0) > 0 ? (int)Math.Ceiling(127 * host._musicGain / A(0)) : 0;
                host.FadeMusic(0, ticks);
                if (IsTimed) _slotsUntil = Math.Max(_slotsUntil, host._lastTime + TicksToSeconds(ticks));
                return true;
            }
            case 704:                                                         // [걸음] 0 에서 127 까지(0x100cd5b0 — 첫 부름에 0 으로)
            {
                int ticks = A(0) > 0 ? (127 + A(0) - 1) / A(0) : 0;
                host.SetMusicLevel(0);
                host.FadeMusic(100, ticks);
                if (IsTimed) _slotsUntil = Math.Max(_slotsUntil, host._lastTime + TicksToSeconds(ticks));
                return true;
            }
            case 801: _vars[A(0)] = (byte)(_vars.GetValueOrDefault(A(0)) + A(1)); return true;   // 장면 변수 += (바이트, 0x100cffa0)
            case 900:
            {
                string raw = A(1) >= 0 && A(1) < _lines.Count ? _lines[A(1)] : "";
                Choice = new Choosing(Part1Game.Speaker(raw).Trim(), Part1Game.PlainText(raw), A(2));
                return true;
            }
            case 901:
                if (Choice is { } open && A(0) >= 0 && A(0) < _lines.Count) open.Options.Add(Part1Game.PlainText(_lines[A(0)]));
                return true;
            case 902: _pageTable.Clear(); return true;
            case 903: _pageTable.Add([A(0), A(1), A(2), A(3)]); return true;
            case 904:
            {
                // 표 창(70, 100, 500×285) — 903 한 칸이 한 줄: [이름, 확률, 글 1, 글 2](필드 0034 · 0101 의 참모 의견 「이븐 시나 90% …」).
                // 칸을 모두 한 창에 쌓는다(넘치면 상자가 쪽을 나눈다). 다 넘기면 잇는다.
                _pages.Clear();
                string Line(int l) => l >= 0 && l < _lines.Count ? Part1Game.PlainText(_lines[l]).Trim() : "";
                var rows = _pageTable.Select(r => $"{Line(r[0])}  {Line(r[1])}  {string.Join(" ", r.Skip(2).Select(l => Line(l)).Where(t => t.Length > 0))}".Trim())
                                     .Where(t => t.Length > 0).ToList();
                if (rows.Count > 0) { Talk = ("", string.Join("\n", rows)); TalkSlot = PageSlot; TalkPerson = -1; _talkAutoAt = 0; }
                return true;
            }
            case 503: return true;                                            // 마을 갈래 낱말(+0x56) — 마을 화면이 따로 맡는다
            case 501 or 506:                                                  // 501 [필드] 다음 필드(장면 5) · 506 챕터 끝(0x100cfe60 → 장면 10)
                if (SceneChange?.Invoke(c) == true) { _sceneChanged = true; _sceneLeft = _leaving = true; _pending.Clear(); }
                return true;
            case >= 1000 and <= 1203: return true;                            // 마을 시설(상점 · 길드 · 주점) — 마을 화면이 맡는다
        }
        return false;
    }
}
