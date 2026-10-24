using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 챕터 지도 — 원본 장면 4(<c>0x100af3d0</c>). 지역 지도를 1:1 로 깔고(화면 가장자리 · 화살표 키로 민다), 왼위 지역 띠(Obs 219),
/// 오른쪽 단추 다섯(이  동 · 진  입 · 상  태 · 교  환 · 턴종료), 장소마다 팀 말(Obs 226 깃대 + 얼굴 + 「N 팀」 꼬리표)을 둔다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-챕터」 9 · 10 · 13절. 팀은 꼬리표를 눌러 고르고, 「이  동」 → 잇닿은 장소(낱말 9~13) 고르기, 「진  입」 → 지금 장소의 전투 · 마을.
/// 팀은 턴마다 한 번 움직인다(<see cref="Part1Team.Moved"/>). 상태 · 교환 창은 <see cref="Part1HqStatusWindow"/> · <see cref="Part1ExchangeWindow"/>.
/// 챕터 사건(대사 · 502 전투로 · 503 필드로 · 깃발)은 <see cref="Part1SceneScript"/> 가 돌린다. 연대표 · 일행(장비) · 타이틀은 원본 지도에 없다 — Esc · 오른 단추 메뉴로.
/// </remarks>
internal sealed class Part1ChapterScreen(GameWindow host, Part1TitleScreen shell)
{
    /// <summary>오른쪽 단추 다섯 — 가운데 x 570, 가운데 y 와 글(원본 0x100b1c30, id 0x64~0x68).</summary>
    internal static readonly (int Y, string Name)[] Buttons = [(30, "이  동"), (65, "진  입"), (100, "상  태"), (135, "교  환"), (185, "턴종료")];

    internal const int ButtonX = 570;
    internal const int ButtonMove = 0, ButtonEnter = 1, ButtonStatus = 2, ButtonExchange = 3, ButtonEndTurn = 4, ButtonHit = 1000;

    /// <summary>「N 팀」 꼬리표(id 0x12d + 팀) · 이동 고르기 장소 · Esc 메뉴 줄.</summary>
    internal const int TeamHit = 3000, TargetHit = 4000, MenuHit = 5000;

    /// <summary>원본 그림 — 지역 띠 · 팀 말(깃대 · 꼬리표) · 이동 표지(퍼지는 고리).</summary>
    internal const int BannerObs = 219, TeamObs = 226, RingObs = 182;

    /// <summary>Esc 메뉴 — 원본 지도에는 없다(시스템 메뉴 쪽). 리메이크에 있던 연대표 · 일행 창 · 타이틀을 여기에 둔다.</summary>
    internal static readonly string[] MenuItems = ["연대표", "일행 · 장비", "저장", "불러오기", "파트1 타이틀", "돌아가기"];

    internal const int MenuX = 250, MenuY = 170, MenuW = 140, MenuRowH = 28;

    internal bool _open;
    internal Part1Chapter? _chapter;
    internal int _hover = -1;

    /// <summary>지금 하는 일 — 보통 · 이동 고르기(Obs 182 표지) · Esc 메뉴.</summary>
    internal enum Mode { Map, Move, Menu }

    internal Mode _mode;

    internal Part1MapArt Art => _art ??= new(host);
    private Part1MapArt? _art;

    internal Part1HqStatusWindow Status => _status ??= new(host, this);
    private Part1HqStatusWindow? _status;

    internal Part1ExchangeWindow Exchange => _exchange ??= new(host, this);
    private Part1ExchangeWindow? _exchange;

    /// <summary>상태 · 교환 창이 떠 있나 — 그동안 지도 단추 · 지도 글은 숨긴다(원본 0x100b2070).</summary>
    internal bool WindowOpen => _status is { _open: true } || _exchange is { _open: true };

    /// <summary>
    /// 지역 지도 — 챕터 머리 낱말 1 → <c>BGRs</c> 번호: 1 → 1(시반 슈미터 · 카디스 섬) · 2 → 2(팬드래건 쪽) · 3 → 6(안타나나리보 · 로우엔 쪽) · 4 → 3.
    /// 원본은 <c>obts\%04d.obt</c>(머리 낱말 0) 타일 지도를 그린다(<see cref="BuildTileMap"/>, 13.8) — 이 BGR 그림은 obt 를 못 읽을 때만 쓴다(장소 이름이 박혀 있다). 그 밖(0 · 13)은 지도 없음.
    /// </summary>
    internal static int MapOf(Part1Chapter chapter) => chapter.Header.Length > 1 ? chapter.Header[1] switch { 1 => 1, 2 => 2, 3 => 6, 4 => 3, _ => 0 } : 0;

    private static readonly Dictionary<int, (uint[]? Px, int W, int H)> Maps = [];

    /// <summary>깐 지도(1:1, 열쇠 빛깔은 바다 무늬로) — 지도 그림이 없으면 장소를 다 담는 바다.</summary>
    private uint[] _mapPx = [];
    private int _mapW, _mapH;
    private bool _hasMap;

    /// <summary>화면 왼위의 지도 자리.</summary>
    internal (int X, int Y) _cam;

    private double _lastScroll;

    /// <summary>턴 끝 알림 「%d일 남았습니다.」(0x100b4720) — 이 시각까지.</summary>
    private double _noticeUntil;
    private string _notice = "";

    /// <summary>고른 팀이 선 장소(차례) — 처음엔 첫 장소. 팀이 아직 없을 때(챕터를 열기 전)는 따로 둔다.</summary>
    internal int _party
    {
        get => _teams is { Count: > 0 } teams ? teams[Math.Clamp(_team, 0, teams.Count - 1)].Place : _loosePlace;
        set { if (_teams is { Count: > 0 } teams) teams[Math.Clamp(_team, 0, teams.Count - 1)].Place = value; else _loosePlace = value; }
    }

    private int _loosePlace;

    /// <summary>이 챕터의 팀들(진행의 <see cref="Part1Progress.ChapterTeams"/>)과 고른 팀.</summary>
    private List<Part1Team>? _teams;
    private int _team;

    internal IReadOnlyList<Part1Team> Teams => _teams ?? [];
    internal int SelectedTeamIndex => _team;
    internal Part1Team? SelectedTeam => _teams is { Count: > 0 } t ? t[Math.Clamp(_team, 0, t.Count - 1)] : null;

    /// <summary>교환 창이 팀을 만들고 지운 뒤 — 팀 목록과 고른 팀을 다시 맞춘다.</summary>
    internal List<Part1Team> MutableTeams => _teams ??= [];

    /// <summary>904 — 그 사람을 팀으로 옮긴다. 없는 팀이면 만들고(8개까지) 장소 0 에 세운다.</summary>
    private void JoinTeam(int team, int chr)
    {
        if (_teams is not { } teams || team < 0 || team >= 8) return;
        while (teams.Count <= team) teams.Add(new Part1Team { Place = 0 });
        foreach (var t in teams) t.Members.Remove(chr);
        if (team > 0) teams[team].Members.Add(chr);
    }

    internal void SelectTeam(int team)
    {
        if (_teams is not { } teams || _chapter is not { } chapter || team < 0 || team >= teams.Count) return;
        _team = team;
        host.Part1.SelectedTeam[chapter.Id] = team;
        SaveProgress();
    }

    /// <summary>챕터 사건 — 502 전투 · 503 필드로 가면 돌아와서 남은 행동을 잇는다.</summary>
    internal Part1SceneScript Events => _events ??= new(host)
    {
        SceneChange = OnSceneChange, PlaceBattle = SetPlaceBattle, MarkerMoved = OnMarkerMoved,
        MapTurn = () => _chapter is { } c ? host.Part1.MapTurns.GetValueOrDefault(c.Id) : 0,
        TeamPlace = team => _teams is { } teams && team >= 0 && team < teams.Count ? teams[team].Place : -1,
        TeamMoved = (team, place) => { if (_teams is { } teams && team >= 0 && team < teams.Count && _chapter is { } c && place >= 0 && place < c.Places.Count) teams[team].Place = place; },
        TeamJoin = JoinTeam,
        // 챕터 100 · 102 · 103(챕터 29 한 곳: 그림 342 를 바래 띄우고 30틱 뒤 바래 걷는다) — 덮개가 떠 있는 동안은 기다림(2 · 1)을 지킨다.
        TimedNow = () => _cover != null || _transition != null,
        PictureShow = (id, x, y, _) => { var (px, w, h) = shell.Field.ReadImage(id); _cover = px != null ? (px, w, h, x, y) : null; },
        PictureClear = () => _cover = null,
        Transition = (mode, _, fadeOut, fadeIn) =>
        {
            _transition = (_shownCover, fadeOut, fadeIn, host._lastTime);
            if (mode == 1) _cover = null;
        },
        UiReset = () => { CloseWindows(); _hover = -1; },
        Ending = OnEnding,
    };

    /// <summary>챕터 100 의 그림 덮개(BGRs, 그림 안 자리) — 덮인 동안 지도 대신 그린다. 102 · 103 [1, …] 이 걷는다.</summary>
    private (uint[] Px, int W, int H, int X, int Y)? _cover;

    /// <summary>지난 틀에 보인 덮개 — 103 의 「옛 화면」.</summary>
    private (uint[] Px, int W, int H, int X, int Y)? _shownCover;

    /// <summary>103 화면 바꾸기 — (옛 덮개, 나감 틱, 들어옴 틱, 시작). 나감 동안 옛 화면을 검게 바래고, 들어옴 동안 새 화면을 드러낸다(0x100ab380).</summary>
    private ((uint[] Px, int W, int H, int X, int Y)? From, int Out, int In, double Start)? _transition;

    /// <summary>
    /// 챕터 705 [n] — 원본은 영상(0 = G3.pak 엔딩 · 1 = Setup\_sys2.cab)을 틀고 장면 7(불러오기 · 타이틀 · 새로)로 간다. 리메이크는 영상을 넣지 않아(사용자 결정)
    /// 챕터를 끝낸 것으로 적고 파트1 타이틀로 돌아간다.
    /// </summary>
    private void OnEnding(int movie)
    {
        GameWindow.TestRunTrace($"part1 chapter {_chapter?.Id} ending {movie}");   // 시험 전용 줄
        int done = _chapter?.Id ?? host.Part1.ChapterId;
        if (done >= 0) host.Part1.FinishedChapters.Add(done);
        host.Part1.PendingActions = [];
        host.Part1.Chain = null;
        _resume = false;
        host.Part1.Save(host.FlagSt._flags);
        _gameOver = true;
        host.Toast("파트1 끝");
        host.LeaveScene(() => { _open = false; shell.ReturnFromBattle(false); });
    }

    /// <summary>
    /// 게임 오버 — 챕터 행동 500(0x100c04e0 → 0x100b44c0: 그림 789 를 띄우고 장면 7 = 불러오기 · 타이틀 · 새로)과 턴 한도.
    /// 전투에 진 것과 같은 끝이라 파트1 타이틀로 돌아간다(이어 하기는 저장된 자리에서). 분석-파트1-챕터 10.2.
    /// </summary>
    private void GameOver()
    {
        // 게임 오버 뒤에는 적지 않는다 — 500 을 터뜨린 사건의 발동 수 · 남은 행동을 적으면 이어 하기가 게임 오버를 건너뛴다.
        _gameOver = true;
        host.Toast("게임 오버");
        host.LeaveScene(() => { _open = false; shell.ReturnFromBattle(false); });
    }

    /// <summary>
    /// 턴 종료(원본 0x100b4010) — 지도 턴 +1, 팀 움직임 표를 다시 놓고 「%d일 남았습니다.」(머리 낱말 10 − 턴)를 띄운 뒤 사건을 다시 훑는다(조건 1 의 턴 사건).
    /// 턴이 머리 낱말 10(챕터 0 은 10 · 1 은 15)에 닿으면 행동 500 = 게임 오버(<see cref="GameOver"/>).
    /// 원본은 끝에 팀 인물마다 HP 를 최대의 30% 채운다 — 리메이크는 전투 사이에 HP 를 들고 다니지 않아(전투마다 가득 차서 시작) 할 일이 없다.
    /// </summary>
    internal void EndMapTurn()
    {
        if (_chapter is not { } chapter) return;
        int turn = host.Part1.MapTurns[chapter.Id] = host.Part1.MapTurns.GetValueOrDefault(chapter.Id) + 1;
        // 턴 한도(머리 낱말 10) — 닿으면 원본은 행동 500 을 넣고 바로 나간다(사건을 다시 훑지 않는다, 0x100b4010).
        int limit = chapter.Header.Length > 10 ? chapter.Header[10] : 0;
        if (limit > 0 && turn >= limit) { GameOver(); return; }
        foreach (var t in Teams) t.Moved = false;
        if (limit > 0) { _notice = $"{limit - turn}일 남았습니다."; _noticeUntil = host._lastTime + 1.4; }
        Events.Arrive(-1);
        SaveProgress();
    }

    /// <summary>
    /// 진행을 적는다 — 일행 장소와 지금 남은 사건 행동(가 있던 전투·필드부터)을 함께. 진행을 적는 곳은 모두 이것을 쓴다:
    /// 따로 적으면 지난번 지도를 열 때의 남은 행동이 남아, 이어 하기가 이미 돈 행동(아이템 · eld · 용병)을 또 돌렸다.
    /// </summary>
    private bool _gameOver;

    internal void SaveProgress()
    {
        if (_gameOver) return;
        if (_chapter != null)
        {
            host.Part1.PartyPlace = _party;
            host.Part1.PendingActions = [.. Events.SavePending];
        }
        host.Part1.Save(host.FlagSt._flags);
    }

    /// <summary>새 게임 · 진 전투 뒤 — 앞 판의 이어 돌기 · 일행 장소를 버린다(남아 있으면 새 게임 첫 챕터가 앞 판의 남은 행동을 돌렸다).</summary>
    internal void Forget()
    {
        _resume = false;
        _chapter = null;
        _teams = null;
        _team = 0;
        _party = 0;
        _open = false;
        CloseWindows();
    }

    private void CloseWindows()
    {
        if (_status != null) _status._open = false;
        if (_exchange != null) _exchange._open = false;
        _mode = Mode.Map;
    }

    /// <summary>
    /// 900 이 말을 일행이 선 장소로 옮겼고 그 장소가 전투 장소면 그 전투를 연다(0x100c10a0 — 장면 3, 장소 낱말 7). 남은 행동은 돌아와서 잇는다.
    /// </summary>
    private bool OnMarkerMoved(int marker, int place)
    {
        if (_chapter is not { } chapter || place < 0 || place >= chapter.Places.Count || !chapter.Places[place].IsBattle) return false;
        // 그 장소에 선 팀이 싸운다(원본 +0x5578 = 그 장소 첫 칸의 팀) — 고른 팀이 아니면 그 팀으로 바꾼다.
        int team = _teams?.FindIndex(t => t.Place == place) ?? (place == _party ? 0 : -1);
        if (team < 0) return false;
        if (_teams != null && team != _team) { _team = team; host.Part1.SelectedTeam[chapter.Id] = team; }
        int battle = chapter.Places[place].Target;
        _resume = true;
        host.LeaveScene(() => { SaveProgress(); shell.StartBattle(battle); });   // 남은 행동을 적고 간다 — 전투 세이브가 이 진행을 싣는다
        return true;
    }

    /// <summary>1201 — 장소를 전투 장소로 바꾸고 진행에 적는다(다시 열어도 그대로).</summary>
    private void SetPlaceBattle(int place, int battle)
    {
        if (_chapter is not { } chapter || place < 0 || place >= chapter.Places.Count) return;
        host.Part1.PlaceBattles[$"{chapter.Id}:{place}"] = battle;
        _chapter = WithPlaceBattles(chapter);
    }

    /// <summary>진행에 적힌 1201 바꿈을 장소에 입힌다.</summary>
    private Part1Chapter WithPlaceBattles(Part1Chapter chapter) =>
        chapter with
        {
            Places = [.. chapter.Places.Select((p, i) => host.Part1.PlaceBattles.TryGetValue($"{chapter.Id}:{i}", out int battle) ? p with { Kind = 0, Target = battle } : p)],
        };
    private Part1SceneScript? _events;

    /// <summary>사건이 전투·필드로 보냈다 — 같은 챕터로 돌아오면 남은 행동을 잇는다.</summary>
    private bool _resume;

    private bool OnSceneChange(ScriptCommand c)
    {
        int target = c.Args[0];
        switch (c.Code)
        {
            case 502:
                _resume = true;
                // 떠나기 전에 남은 행동(502 를 앞에 붙여)을 적는다 — 닿기 사건(Arrive)에서 온 502 는 Open 이 적지 않아 전투 세이브가 옛 남은 행동을 실었다.
                host.LeaveScene(() => { SaveProgress(); shell.StartBattle(target); });
                return true;
            case 501:
                host.LeaveScene(EndChapter);
                return true;
            case 500:
                GameOver();
                return true;
            case 503:
                int chapterId = _chapter?.Id ?? -1;
                _resume = true;
                // 떠나기 전에 남은 행동(503 을 머리에)을 적는다 — 안 적으면 필드 안 전투의 세이브가 지난번에 적힌 남은 행동을 실었다.
                host.LeaveScene(() => { SaveProgress(); _open = false; shell.Field.Open(target, () => Open(chapterId)); });
                return true;
        }
        return false;
    }

    /// <summary>
    /// 챕터 끝 — 장면 10(0x100c04b0 → 생성 0x100d2470 이 Eps\episode.elg 를 연다) = 연대표로. 끝낸 챕터는 연대표에 ✓.
    /// 챕터 행동 501 · 필드 행동 506(0x100cfe60) · 전투 행동 105(결과 7)가 부른다.
    /// </summary>
    internal void EndChapter()
    {
        int done = _chapter?.Id ?? host.Part1.ChapterId;
        if (done >= 0) host.Part1.FinishedChapters.Add(done);
        host.Part1.PendingActions = [];
        host.Part1.Chain = null;
        _resume = false;
        host.Part1.Save(host.FlagSt._flags);   // 챕터가 끝났다 — 남은 행동 없이
        _open = false;
        shell.Timeline.Open();
    }

    /// <summary>그 챕터를 연다 — 못 읽으면 알리고 false.</summary>
    internal bool Open(int chapterId)
    {
        bool resume = _resume && _chapter?.Id == chapterId;
        bool same = _chapter?.Id == chapterId && _mapPx.Length > 0;
        _resume = false;
        _gameOver = false;
        if (_chapter?.Id != chapterId) { _teams = null; _party = 0; }
        if (Part1Chapter.Load(chapterId) is not { Places.Count: > 0 } chapter)
        {
            host.Toast($"파트1 챕터 {chapterId} 를 못 읽었습니다");
            return false;
        }
        _chapter = chapter = WithPlaceBattles(chapter);
        // 지도 말 — 챕터마다 진행에 남긴 자리(900 이 옮긴 것)를 쓴다. 처음 여는 챕터면 처음 장소로.
        if (!host.Part1.ChapterMarkers.TryGetValue(chapterId, out var markers) || markers.Count != chapter.Markers.Count)
            host.Part1.ChapterMarkers[chapterId] = markers = [.. chapter.Markers];
        Events.Markers = markers;
        // 앞 장소 — 진행에 남긴 것(900 이 적은 옛 장소). 처음이면 지금 장소(아직 안 움직임).
        if (!host.Part1.ChapterMarkerPrev.TryGetValue(chapterId, out var prev) || prev.Count != markers.Count)
            host.Part1.ChapterMarkerPrev[chapterId] = prev = [.. markers];
        Events.MarkerPrev = prev;
        // 팀 — 진행에 남긴 것을 쓴다. 처음 여는 챕터면 팀 0 하나(일행 모두)를 지금 자리에.
        if (!host.Part1.ChapterTeams.TryGetValue(chapterId, out var teams) || teams.Count == 0)
            host.Part1.ChapterTeams[chapterId] = teams = [new Part1Team { Place = _party }];
        _teams = teams;
        _team = Math.Clamp(host.Part1.SelectedTeam.GetValueOrDefault(chapterId), 0, teams.Count - 1);
        host.Part1.ChapterId = chapterId;
        host.Part1.Chain = null;   // 챕터로 돌아왔다 — 사슬은 끝났다(아래 SaveProgress 가 지운 것을 적는다)
        PlayMapMusic(chapter);
        BuildMap(chapter);
        // 처음 화면 자리 = (min(낱말 3, 폭 − 640), min(낱말 4, 높이 − 480))(13.1). 전투 · 필드에서 돌아오면 보던 자리 그대로.
        if (!same) _cam = (chapter.Header.Length > 4 ? chapter.Header[3] : 0, chapter.Header.Length > 4 ? chapter.Header[4] : 0);
        ClampCamera();
        _open = true;
        _hover = -1;
        CloseWindows();
        _lastScroll = host._lastTime;
        Events.Start(chapter.Events, chapter.Lines, host.Part1.ChapterFired(chapterId), resume, host.Part1.VarsOf(chapterId));
        // 열 때마다 적어 둔다 — 사건이 전투·필드로 보냈으면 남은 행동까지(CONTINUE 가 그 자리에서 잇는다).
        SaveProgress();
        return true;
    }

    /// <summary>
    /// 지도 배경음악 — 챕터 머리 낱말 8(무리 표 +0x16, 머리 로더 0x100ade70)의 <c>BGMs</c> 곡을 되풀이로(장면 4 0x100af69a: 0xffff 가 아니면 0x100094e0(곡, 1)).
    /// 챕터 0 은 16, 20~29 는 17. 전투 · 필드가 제 곡으로 바꾸고, 돌아와 지도를 열면 다시 건다(같은 곡이 돌고 있으면 그대로).
    /// </summary>
    private void PlayMapMusic(Part1Chapter chapter)
    {
        if (chapter.Header.Length <= 8 || chapter.Header[8] < 0) return;
        int bgm = Part1Game.BgmBase + chapter.Header[8];
        if (host._musicId != bgm) host.PlayMusicFile(bgm, loop: true);
    }

    /// <summary>지도 그림을 1:1 로 — 열쇠 빛깔(노랑 · 파랑)과 그림 밖은 바다 무늬(BGR 0). 그림이 없으면 장소를 다 담는 크기의 바다.</summary>
    private void BuildMap(Part1Chapter chapter)
    {
        (uint[]? Px, int W, int H) Image(int bgr)
        {
            lock (Maps) if (Maps.TryGetValue(bgr, out var hit)) return hit;
            var read = shell.Field.ReadImage(bgr);
            lock (Maps) Maps[bgr] = read;
            return read;
        }
        var (sea, sw, sh) = Image(0);
        (_sea, _seaW, _seaH) = (sea, sw, sh);
        if (BuildTileMap(chapter)) return;
        var (map, mw, mh) = MapOf(chapter) is > 0 and var id ? Image(id) : (null, 0, 0);
        _hasMap = map != null;
        int needW = chapter.Places.Max(p => p.X + p.Words[3]) + 120, needH = chapter.Places.Max(p => p.Y + p.Words[4]) + 120;
        _mapW = Math.Max(MosesScene.MosesW, _hasMap ? mw : needW);
        _mapH = Math.Max(MosesScene.MosesH, _hasMap ? mh : needH);
        static bool Key(uint c) => (c & 0xFFFFFF) is var rgb && ((rgb >> 16 & 0xFF) > 240 && (rgb >> 8 & 0xFF) > 240 && (rgb & 0xFF) < 16
                                                             || (rgb >> 16 & 0xFF) < 16 && (rgb >> 8 & 0xFF) < 16 && (rgb & 0xFF) > 240);
        var px = new uint[_mapW * _mapH];
        for (int y = 0; y < _mapH; y++)
            for (int x = 0; x < _mapW; x++)
            {
                uint c = map != null && x < mw && y < mh ? map[y * mw + x] : 0xFFFFFF00;
                if (Key(c)) c = sea != null && sw > 0 && sh > 0 ? sea[y % sh * sw + x % sw] : 0xFF102040;
                px[y * _mapW + x] = c | 0xFF000000;
            }
        _mapPx = px;
    }

    /// <summary>타일 지도(머리 낱말 0 의 obt) — 한 번 읽어 둔다. 못 읽으면 null(BGR 그림으로 물러선다).</summary>
    private static readonly Dictionary<int, Part1ChapterMap?> TileMaps = [];

    /// <summary>바다 그림(BGR 0, 128×128)과 이번 틱 물결 무늬.</summary>
    private uint[]? _sea;
    private int _seaW, _seaH, _rippleFrame = -1;
    private readonly uint[] _ripple = new uint[128 * 128];

    /// <summary>obt 타일 지도로 깔았나 — 알파 0 픽셀이 바다(그릴 때 물결로 채운다).</summary>
    private bool _tiled;

    /// <summary>구름 스무 장(0x1011f1f0 — 3D 가속이 있을 때) — 세계 자리 (X, Y), 무늬 번호.</summary>
    private readonly List<(int X, int Y, int Look)> _clouds = [];
    private static byte[][]? _cloudLooks;
    private int _cloudTick;
    private Random _cloudRng = new(1);

    /// <summary>
    /// 원본 지도 객체 0x100c3960 그대로 — <c>obts\%04d.obt</c>(머리 낱말 0)를 40×8 띠로 깔고 빈 자리는 바다(분석-파트1-챕터 13.1 · 13.x).
    /// BGR 지도와 달리 장소 이름이 그림에 없다 — 원본도 장소 종류 1 만 글로 쓴다.
    /// </summary>
    private bool BuildTileMap(Part1Chapter chapter)
    {
        int id = chapter.Header.Length > 0 ? chapter.Header[0] : 0;
        Part1ChapterMap? map;
        lock (TileMaps)
            if (!TileMaps.TryGetValue(id, out map)) TileMaps[id] = map = Part1ChapterMap.Load(id);
        _tiled = map != null;
        if (map == null) return false;
        _hasMap = true;
        _mapW = Math.Max(MosesScene.MosesW, map.Width);
        _mapH = Math.Max(MosesScene.MosesH, map.Height);
        var px = new uint[_mapW * _mapH];
        for (int y = 0; y < map.Height; y++) Array.Copy(map.Px, y * map.Width, px, y * _mapW, map.Width);
        _mapPx = px;
        _rippleFrame = -1;
        // 구름 — 처음 자리 (난수 % W · 40, 난수 % H · 8)(띠 수 W × H).
        _cloudLooks ??= [.. Enumerable.Range(0, 20).Select(i => Part1ChapterMap.Cloud(new Random(100 + i)))];
        _cloudRng = new Random(chapter.Id);
        _clouds.Clear();
        int sw = map.Width / 40, sh = map.Height / 8;
        for (int i = 0; i < 20; i++) _clouds.Add((_cloudRng.Next(sw) * 40, _cloudRng.Next(sh) * 8, i));
        _cloudTick = (int)(host._lastTime * GameWindow.TicksPerSecond);
        return true;
    }

    /// <summary>구름을 틱만큼 민다(0x100c4c80): x +1, 세 틱마다 y −1. 지도 오른끝을 넘거나 y &lt; −240 이면 x = −320, y = 난수 % H · 16.</summary>
    private void StepClouds(int tick)
    {
        int steps = Math.Clamp(tick - _cloudTick, 0, 300);
        if (tick - _cloudTick < 0 || tick - _cloudTick > 300) _cloudTick = tick;
        int sh = Math.Max(1, _mapH / 8);
        for (; steps > 0; steps--)
        {
            _cloudTick++;
            for (int i = 0; i < _clouds.Count; i++)
            {
                var (x, y, look) = _clouds[i];
                x++;
                if (_cloudTick % 3 == 0) y--;
                if (x >= _mapW || y < -240) { x = -320; y = _cloudRng.Next(sh) * 16; }
                _clouds[i] = (x, y, look);
            }
        }
    }

    /// <summary>구름 한 장 — 128×128 무늬를 320×240 으로 늘려 스크린 섞기(원본 D3D: 원본 × (1 − 판) + 판).</summary>
    private void DrawCloud(byte[] look, int sx, int sy, int ox, int oy)
    {
        var fb = host._fb;
        int bw = host.BoardWidth, bh = host.BoardHeight;
        int x0 = Math.Max(Math.Max(sx, ox), 0), x1 = Math.Min(Math.Min(sx + 320, ox + MosesScene.MosesW), bw);
        int y0 = Math.Max(Math.Max(sy, oy), 0), y1 = Math.Min(Math.Min(sy + 240, oy + MosesScene.MosesH), bh);
        for (int y = y0; y < y1; y++)
        {
            // 원본은 겹선형 거르기(D3D 확대 · 축소 거르기 2)로 늘린다 — 8비트 고정 소수점으로 같은 꼴.
            int fy = (y - sy) * 127 * 256 / 239, ty = fy >> 8, wy = fy & 0xFF, r0 = ty * 128, r1 = Math.Min(ty + 1, 127) * 128;
            for (int x = x0; x < x1; x++)
            {
                int fx = (x - sx) * 127 * 256 / 319, tx = fx >> 8, wx = fx & 0xFF, tx1 = Math.Min(tx + 1, 127);
                int top = look[r0 + tx] * (256 - wx) + look[r0 + tx1] * wx, bottom = look[r1 + tx] * (256 - wx) + look[r1 + tx1] * wx;
                int a = (top * (256 - wy) + bottom * wy) * 255 / (31 << 16);
                if (a == 0) continue;
                uint d = fb[y * bw + x];
                int r = (int)(d >> 16 & 0xFF), g = (int)(d >> 8 & 0xFF), b = (int)(d & 0xFF);
                r += a * (255 - r) / 255; g += a * (255 - g) / 255; b += a * (255 - b) / 255;
                fb[y * bw + x] = 0xFF000000u | (uint)r << 16 | (uint)g << 8 | (uint)b;
            }
        }
    }

    private void ClampCamera() =>
        _cam = (Math.Clamp(_cam.X, 0, Math.Max(0, _mapW - MosesScene.MosesW)), Math.Clamp(_cam.Y, 0, Math.Max(0, _mapH - MosesScene.MosesH)));

    /// <summary>화면을 민다(지도 좌표).</summary>
    internal void Scroll(int dx, int dy)
    {
        _cam = (_cam.X + dx, _cam.Y + dy);
        ClampCamera();
    }

    /// <summary>그 장소가 화면 가운데쯤 오게.</summary>
    internal void CenterOn(int place)
    {
        if (_chapter is not { } chapter || place < 0 || place >= chapter.Places.Count) return;
        var (cx, cy) = Center(chapter.Places[place]);
        _cam = (cx - MosesScene.MosesW / 2, cy - MosesScene.MosesH / 2);
        ClampCamera();
    }

    /// <summary>세이브에서 이어 간다 — 적어 둔 챕터를 열고 남은 행동을 잇는다.</summary>
    internal bool Continue()
    {
        var chain = host.Part1.Chain;
        PrepareContinue();
        // 사슬 안에서 적힌 진행 — 그 장면(전투 · 필드)부터 연다. 마치면 챕터로 돌아와 머리를 뺀 남은 행동을 잇는다.
        if (chain is [1, int battle] && shell.StartBattle(battle)) return true;
        if (chain is [2, int field]) { shell.FieldFromBattle(field); return true; }
        // 시험: DUELDX_PART1TOWN=필드 — 챕터를 열고 곧바로 그 마을로.
        if (int.TryParse(Environment.GetEnvironmentVariable("DUELDX_PART1TOWN"), out int town) && town > 0 && Open(host.Part1.ChapterId))
        {
            int chapterId = host.Part1.ChapterId;
            _open = false;
            shell.Town.Open(town, () => Open(chapterId));
            return true;
        }
        return Open(host.Part1.ChapterId);
    }

    /// <summary>진행에서 이어 갈 준비 — 남은 행동을 되살리고 그 챕터 · 일행 자리를 세운다(지도는 아직 안 연다). 전투 세이브를 불러올 때도 쓴다.</summary>
    /// <remarks>
    /// <paramref name="inBattle"/> = 전투 세이브를 불러올 때. 진행의 남은 행동 머리는 그 전투를 연 행동(502 · 전투 장소로 옮긴 900, <see cref="Part1SceneScript.SavePending"/>)이라
    /// 이어 하기는 그것부터 다시 하지만, 전투 세이브는 이미 그 전투 안이다 — 머리를 빼고 잇는다. 안 빼면 이기고 돌아와 같은 전투가 또 열렸다(20087 사용자 보고).
    /// 원본은 전투를 마치면 장면 4 로 돌아와 502 다음 행동부터 돈다.
    /// </remarks>
    internal void PrepareContinue(bool inBattle = false)
    {
        var pending = host.Part1.PendingActions;
        // 사슬 안(Chain)이면 머리(사슬을 연 502 · 503 · 900)는 이미 지났다 — 이어 하기 · 전투 세이브 모두 뺀다.
        if ((inBattle || host.Part1.Chain != null) && pending.Count > 0 && (pending[0].Code is 502 or 900 || host.Part1.Chain != null && pending[0].Code == 503))
            pending = [.. pending.Skip(1)];
        _resume = pending.Count > 0;
        Events.Restore(pending);
        if (Part1Chapter.Load(host.Part1.ChapterId) is { } chapter) _chapter = chapter;   // 같은 챕터로 열어 resume 이 맞게
        _mapPx = [];
        _teams = null;
        _party = host.Part1.PartyPlace;   // Open 앞에 — 남은 행동(900 의 「일행이 선 장소」)과 Open 이 적는 장소가 이 값을 본다
    }

    // ── 장소 · 팀 말 ──

    /// <summary>장소 가운데(지도 좌표) — 낱말 1·2 왼위 + 낱말 3·4 크기의 절반. 팀 말 묶음이 여기 선다(0x100b20d0).</summary>
    internal static (int X, int Y) Center(Part1ChapterPlace p) => (p.X + p.Words[3] / 2, p.Y + p.Words[4] / 2);

    /// <summary>장소 가운데의 화면 자리.</summary>
    private (int X, int Y) Screen(Part1ChapterPlace p, int ox, int oy)
    {
        var (cx, cy) = Center(p);
        return (ox + cx - _cam.X, oy + cy - _cam.Y);
    }

    /// <summary>그 장소에 선 팀들(차례대로, 원본 장소 3칸) — 3개까지.</summary>
    internal List<int> TeamsAt(int place) => [.. Enumerable.Range(0, Teams.Count).Where(t => Teams[t].Place == place).Take(3)];

    /// <summary>그 장소의 살아 있는 지도 말(적 무리).</summary>
    private bool EnemyAt(int place) => Events.Markers.Any(m => m >= 0 && m == place);

    /// <summary>
    /// 고른 팀이 갈 수 있는 이웃 — 낱말 9~13 중 −1 이 아니고, 그 장소 낱말 5 가 −1 이거나 깃발[낱말 5] ≠ 0 인 것(0x100b2840 앞 검사).
    /// </summary>
    internal List<int> MoveTargets()
    {
        if (_chapter is not { } chapter || _party < 0 || _party >= chapter.Places.Count) return [];
        var flags = host.FlagSt._flags;
        return [.. chapter.Places[_party].Words[9..14].Select(w => (int)w).Distinct()
                    .Where(n => n >= 0 && n < chapter.Places.Count && n != _party)
                    .Where(n => chapter.Places[n].Words[5] is var gate && (gate < 0 || gate < flags.Length && flags[gate] != 0))];
    }

    // ── 맞음 ──

    internal int HitAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        if (_chapter is not { } chapter) return -1;
        if (_mode == Mode.Menu)
        {
            for (int i = 0; i < MenuItems.Length; i++)
                if (bx >= ox + MenuX && bx < ox + MenuX + MenuW && by >= oy + MenuY + 30 + i * MenuRowH && by < oy + MenuY + 30 + (i + 1) * MenuRowH - 2) return MenuHit + i;
            return -1;
        }
        if (_mode == Mode.Move)
        {
            foreach (int n in MoveTargets())
            {
                var p = chapter.Places[n];
                var (sx, sy) = Screen(p, ox, oy);
                int hw = Math.Max(24, p.Words[3] / 2), hh = Math.Max(24, p.Words[4] / 2);
                if (Math.Abs(bx - sx) <= hw && Math.Abs(by - sy) <= hh) return TargetHit + n;
            }
            return -1;
        }
        for (int i = 0; i < Buttons.Length; i++)
            if (Part1MapArt.ButtonHit(ox + ButtonX, oy + Buttons[i].Y, bx, by)) return ButtonHit + i;
        // 「N 팀」 꼬리표 — 묶음 기준 (59, 18k − 75), 39×18.
        for (int i = 0; i < chapter.Places.Count; i++)
        {
            var at = TeamsAt(i);
            if (at.Count == 0) continue;
            var (gx, gy) = Screen(chapter.Places[i], ox, oy);
            for (int k = 0; k < at.Count; k++)
                if (bx >= gx + 59 && bx < gx + 59 + 39 && by >= gy + 18 * k - 75 && by < gy + 18 * k - 75 + 18) return TeamHit + at[k];
        }
        return -1;
    }

    internal void UpdateHover(int bx, int by)
    {
        if (Events.Choice != null && Events.Talk == null) { if (ChoiceAt(bx, by) is >= 0 and var c) _choiceHover = c; return; }
        if (Picking) { _hover = PickAt(bx, by) + PickHit; return; }
        if (_status is { _open: true }) { _status.UpdateHover(bx, by); return; }
        if (_exchange is { _open: true }) { _exchange.UpdateHover(bx, by); return; }
        _hover = HitAt(bx, by);
    }

    // ── 레벨업 고르기 — 전투에서 오른 레벨마다 직업 어빌리티 하나(원본 0x1009dac0 은 전투 안에서 고른다) ──

    internal const int PickHit = 2000, PickX = 190, PickY = 150, PickW = 260, PickRowH = 24;

    /// <summary>고를 레벨업이 있고 대사가 없다 — 지도 위에 고르기 창을 띄운다.</summary>
    internal bool Picking => host.Part1.LevelPicks.Count > 0 && Events.Talk == null;

    private (int Chr, List<(int Ability, int NewLevel, string Name)> Options)? _pickCache;

    /// <summary>지금 고를 인물과 후보 — 후보가 없는 것(다 올린 인물)은 건너뛴다.</summary>
    private (int Chr, int Level, List<(int Ability, int NewLevel, string Name)> Options)? CurrentPick()
    {
        while (host.Part1.LevelPicks.Count > 0)
        {
            var (chr, level) = host.Part1.LevelPicks[0];
            if (_pickCache is not { } cache || cache.Chr != chr) _pickCache = cache = (chr, host.Part1.PickCandidates(chr));
            if (cache.Options.Count > 0) return (chr, level, cache.Options);
            host.Part1.LevelPicks.RemoveAt(0);
            _pickCache = null;
        }
        return null;
    }

    internal int PickAt(int bx, int by)
    {
        if (CurrentPick() is not { } pick) return -1;
        var (ox, oy) = host.Mos.MosesOrigin();
        for (int i = 0; i < pick.Options.Count; i++)
            if (bx >= ox + PickX && bx < ox + PickX + PickW && by >= oy + PickY + i * PickRowH && by < oy + PickY + (i + 1) * PickRowH - 2) return i;
        return -1;
    }

    internal void PressPick(int i)
    {
        if (CurrentPick() is not { } pick || i < 0 || i >= pick.Options.Count) return;
        var option = pick.Options[i];
        host.Part1.Pick(pick.Chr, option.Ability, option.NewLevel);
        host.Part1.LevelPicks.RemoveAt(0);
        _pickCache = null;
        host._db?.ForgetPart1Characters();
        host.Btl.Part1.Forget(pick.Chr);
        host.Btl.RefreshPart1Abilities(pick.Chr);   // 전투 안에서 고르면 그 판의 어빌리티 목록에도 바로(사용자 보고: 필살 6연사를 배웠는데 목록에 없다)
        string who = host._db?.Character(pick.Chr) is { } c ? host._db.T(c.NameId) : "";
        host.Toast($"{who} — {option.Name} Lv {option.NewLevel}");
        SaveProgress();
    }

    internal void DrawPick(int ox, int oy)
    {
        // 원본 꼴(사용자 화면) — 작은 목록 창: 아이콘 · 이름 · 「LV n」(오를 레벨) · 소울 비용. 머리글 없이, 짚은 줄은 파랗게.
        if (CurrentPick() is not { } pick) return;
        _abilityList ??= Part1Ability.LoadAll();
        _attackList ??= Part1Attack.LoadAll();
        int h = 12 + pick.Options.Count * PickRowH;
        host.UiFill(ox + PickX - 6, oy + PickY - 6, PickW + 12, h, 0xFF1C2246);
        host.UiStroke(ox + PickX - 6, oy + PickY - 6, PickW + 12, h, 0xFFB0B8E0);
        string who = host._db?.Character(pick.Chr) is { } c ? host._db.T(c.NameId) : "";
        host.UiText(who, ox + PickX, oy + PickY - 24, 0xFFFFE080, 12);   // 파트1 은 레벨이 없다 — 이름만
        for (int i = 0; i < pick.Options.Count; i++)
        {
            var (ability, level, label) = pick.Options[i];
            int y = oy + PickY + i * PickRowH;
            if (_hover == PickHit + i) host.UiFill(ox + PickX, y, PickW, PickRowH - 2, 0xFF2840C8);
            var info = Part1Ability.ById(_abilityList, ability);
            if (info != null && host.UiFor(Part1Game.ObsBase + 11)?.FrameAt(info.Tail + 0x13, 0) is { } icon)
                host.UiSprite(Part1Game.ObsBase + 11, info.Tail + 0x13, 0, ox + PickX + 6 - icon.X, y + 3 - icon.Y);   // 창과 같은 층에
            host.UiText(label, ox + PickX + 28, y + 3, GameWindow.White, 12);
            host.UiText($"LV{level}", ox + PickX + PickW - 76, y + 3, 0xFFFF6060, 12);
            int cost = info != null && info.AttackId > 0 ? Part1Attack.ById(_attackList, info.AttackId + level - 1)?.Cost ?? 0 : 0;
            if (cost > 0) host.UiText($"{cost}", ox + PickX + PickW - 34, y + 3, 0xFFFFE080, 12);
        }
        DrawPickJobs(pick.Chr, ox + Math.Min(PickX + PickW + 8, MosesScene.MosesW - PickJobsW - 2), oy + PickY - 6);
    }

    /// <summary>
    /// 고르기 창 오른쪽 — 갈 수 있는 다음 직업과 그 직업에 가려면 더 올려야 할 어빌리티(지금/필요). 바로 바꿀 수 있으면 「가능」(사용자 요청).
    /// 고르는 줄을 짚으면 그 어빌리티를 올렸을 때 채워지는 조건이 초록으로 바뀐다.
    /// </summary>
    private const int PickJobsW = 182;

    private void DrawPickJobs(int chrCode, int x, int y)
    {
        if (Part1Chr.Load(chrCode - Part1Game.ChrBase) is not { } raw) return;
        var chr = host.Part1.Apply(raw);
        var next = Part1JobTree.NextJobsFor(chr);
        var current = Part1Job.ById(Part1Job.LoadAll(), chr.Fields[7]);
        int hovered = CurrentPick() is { } pick && _hover - PickHit is var hi && hi >= 0 && hi < pick.Options.Count ? pick.Options[hi].Ability : -1;
        int hoveredLevel = hovered > 0 && CurrentPick() is { } p2 ? p2.Options[_hover - PickHit].NewLevel : 0;
        const int W = PickJobsW, RowH = 17;
        int h = 32 + Math.Max(1, next.Sum(n => 1 + n.Missing.Count)) * RowH;
        host.UiFill(x, y, W, h, 0xFF1C2246);
        host.UiStroke(x, y, W, h, 0xFFB0B8E0);
        host.UiText($"{current?.Name ?? ""} → 갈 수 있는 직업", x + 8, y + 6, 0xFFFFE080, 10);
        int ry = y + 28;
        if (next.Count == 0) host.UiText("없음", x + 10, ry, 0xFF808890, 11);
        foreach (var (job, missing) in next)
        {
            bool ready = missing.Count == 0 || missing.All(m => m.Ability == hovered && hoveredLevel >= m.Need);
            host.UiText(job.Name, x + 8, ry, ready ? 0xFF80E080 : GameWindow.White, 12);
            if (missing.Count == 0) host.UiText("가능", x + W - 40, ry, 0xFF80E080, 11);
            ry += RowH;
            for (int i = 0; i < missing.Count; i++)
            {
                var (ability, now, need) = missing[i];
                bool met = ability == hovered && hoveredLevel >= need;
                string name = Part1Ability.ById(_abilityList ??= Part1Ability.LoadAll(), ability)?.Name ?? $"#{ability}";
                host.UiText($"{name}", x + 18, ry, met ? 0xFF80E080 : 0xFFE8C860, 10);
                var (_, nw, _) = host.UiGetText($"{now}/{need}", 0xFFE8C860, 10);
                host.UiText($"{now}/{need}", x + W - 10 - nw, ry, met ? 0xFF80E080 : 0xFFE8C860, 10);
                ry += RowH;
            }
        }
    }

    private IReadOnlyList<Part1Ability>? _abilityList;
    private IReadOnlyList<Part1Attack>? _attackList;

    // ── 입력 ──

    internal void OnClick(int bx, int by)
    {
        if (Events.Talk != null) { if (!shell.NextTalkPage()) Events.Advance(); return; }   // 대사가 떠 있으면 누르면 다음 장 · 다음 대사
        if (Events.Choice != null) { if (Events.WaitingOnChoice && ChoiceAt(bx, by) is >= 0 and var c) Events.Choose(c); return; }
        if (_cover != null || _transition != null) return;
        if (Picking) { PressPick(PickAt(bx, by)); return; }
        if (_status is { _open: true }) { _status.OnClick(bx, by); return; }
        if (_exchange is { _open: true }) { _exchange.OnClick(bx, by); return; }
        int hit = HitAt(bx, by);
        if (_mode == Mode.Move && hit < 0) { _mode = Mode.Map; return; }   // 표지 밖을 누르면 취소(단추가 다시 보인다)
        if (_mode == Mode.Menu && hit < 0) { _mode = Mode.Map; return; }
        Press(hit);
    }

    /// <summary>지도 저장을 받을 수 있나 — 사건 · 대사 · 고르기 · 덮개 · 레벨업 고르기가 없을 때.</summary>
    internal bool CanSave => _chapter != null && Events.Idle && !Picking && _cover == null && _transition == null;

    /// <summary>지도 세이브의 이름(칸 목록 글) — 지역 이름(머리 낱말 11).</summary>
    internal string RegionName => _chapter is { } c && c.Header.Length > 11 && c.Header[11] >= 0 && c.Header[11] < c.Lines.Count
        ? Part1Game.PlainText(c.Lines[c.Header[11]]).Replace('\n', ' ').Trim() : "";

    /// <summary>오른 단추 — 떠 있는 창 · 이동 고르기를 닫는다(원본 메시지 0x3ec). 아무것도 없으면 Esc 메뉴.</summary>
    internal void OnRightClick()
    {
        if (Events.Talk != null || Picking) return;
        if (_status is { _open: true } && _status.OnRightDown(host._mouse.X, host._mouse.Y)) return;   // 상태 패널: 어빌리티 설명 · 목록 닫기 · 밖이면 패널만 닫기
        Back();
    }

    private void Back()
    {
        if (_status is { _open: true }) { if (!_status.ClosePanel()) _status.Close(); }   // 얼굴로 연 상태 패널이 있으면 그것만
        else if (_exchange is { _open: true }) _exchange.Close();
        else if (_mode != Mode.Map) _mode = Mode.Map;
        else _mode = Mode.Menu;
    }

    /// <summary>Esc · 오른 단추는 창을 닫고(없으면 메뉴), 화살표는 지도를 민다, Enter · Space 는 짚은 것을 누른다.</summary>
    internal void OnKey(int key)
    {
        if (Events.Talk != null) { if (key is Win32.VK_RETURN or Win32.VK_SPACE or Win32.VK_ESCAPE && !shell.NextTalkPage()) Events.Advance(); return; }
        if (Events.Choice is { } choosing)
        {
            int n = choosing.Options.Count;
            if (n > 0 && key is Win32.VK_UP or Win32.VK_DOWN) _choiceHover = (Math.Clamp(_choiceHover, 0, n - 1) + (key == Win32.VK_UP ? n - 1 : 1)) % n;
            else if (n > 0 && key is Win32.VK_RETURN or Win32.VK_SPACE && Events.WaitingOnChoice) Events.Choose(Math.Clamp(_choiceHover, 0, n - 1));
            return;
        }
        if (_cover != null || _transition != null) return;
        if (Picking) { if (key is Win32.VK_RETURN or Win32.VK_SPACE) PressPick(Math.Max(0, _hover - PickHit)); return; }   // 고르기 창은 닫을 수 없다(짚은 것 없으면 첫 후보)
        if (key == Win32.VK_ESCAPE) { Back(); return; }
        if (_status is { _open: true } && _status.OnKey(key)) return;   // 상태 패널: ←/→ 로 사람 바꾸기
        if (WindowOpen) return;
        switch (key)
        {
            case Win32.VK_LEFT: Scroll(-80, 0); break;
            case Win32.VK_RIGHT: Scroll(80, 0); break;
            case Win32.VK_UP: Scroll(0, -80); break;
            case Win32.VK_DOWN: Scroll(0, 80); break;
            case Win32.VK_RETURN or Win32.VK_SPACE when _hover >= 0: Press(_hover); break;
        }
    }

    internal void Press(int hit)
    {
        if (_chapter is not { } chapter) return;
        switch (hit)
        {
            case ButtonHit + ButtonMove: StartMove(); break;
            case ButtonHit + ButtonEnter: EnterHere(); break;
            case ButtonHit + ButtonStatus: Status.Open(); break;
            case ButtonHit + ButtonExchange: Exchange.Open(); break;
            case ButtonHit + ButtonEndTurn: EndMapTurn(); break;
            case >= TeamHit and < TeamHit + 8: SelectTeam(hit - TeamHit); break;
            case >= TargetHit and < TargetHit + 1000: _mode = Mode.Map; MoveTo(hit - TargetHit); break;
            case MenuHit + 0: _mode = Mode.Map; host.LeaveScene(() => { _open = false; shell.Timeline.Open(); }); break;
            case MenuHit + 1: _mode = Mode.Map; OpenParty(0); break;
            // 저장 · 불러오기 — 전투 SAVE 와 같은 칸 창(파트1 칸 part1-battle-save-NN.json). 지도에서 적은 칸은 장면 갈래 4(「챕  터」)로, 불러오면 이 지도로 돌아온다.
            case MenuHit + 2: _mode = Mode.Map; SaveProgress(); host.SlotsScr.OpenSlots(0); break;
            case MenuHit + 3: _mode = Mode.Map; host.SlotsScr.OpenSlots(1); break;
            case MenuHit + 4: _mode = Mode.Map; host.LeaveScene(() => _open = false); break;
            case MenuHit + 5: _mode = Mode.Map; break;
            case >= 0 when hit < chapter.Places.Count: GoTo(hit); break;
        }
    }

    /// <summary>일행 창(장비 · 탑승) — Esc 메뉴 「일행 · 장비」. 상태 창의 얼굴은 전투와 같은 상태 패널을 띄운다(<see cref="Part1HqStatusWindow"/>).</summary>
    internal void OpenParty(int chr)
    {
        if (_chapter is not { } chapter) return;
        int chapterId = chapter.Id;
        int member = Math.Max(0, host.Part1.GroupMembers().IndexOf(chr));
        host.LeaveScene(() => { _open = false; shell.Party.Open(() => Open(chapterId), member); });
    }

    /// <summary>「이  동」 — 고른 팀이 이번 턴에 안 움직였고 갈 이웃이 있으면 고르기 표지를 띄운다(없으면 아무 일 없음).</summary>
    private void StartMove()
    {
        if (SelectedTeam is { Moved: true }) { host.Toast("이 팀은 이번 턴에 이미 움직였습니다"); return; }
        if (MoveTargets().Count == 0) return;
        _mode = Mode.Move;
        _hover = -1;
        RevealTargets();
    }

    /// <summary>
    /// 이동 고르기를 열 때 팀 자리와 갈 곳이 다 보이게 화면을 민다(리메이크 편의 — 원본은 마우스 쪽 이웃 하나에 고리를 둔다).
    /// 챕터 20 처음 화면(385, 397)에서는 솔즈베리다리(398, 450)가 왼위 지역 띠 밑에 가려 고리가 띠 아래 반쯤 숨었다(사용자 보고).
    /// </summary>
    private void RevealTargets()
    {
        if (_chapter is not { } chapter || _party < 0 || _party >= chapter.Places.Count) return;
        var points = MoveTargets().Append(_party).Select(n => Center(chapter.Places[n])).ToList();
        int minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        const int left = 40, right = 500, top = 100, bottom = 440;   // 띠(152×64) · 오른쪽 단추(x 520~) · 아래 안내 글을 비킨다
        int dx = minX - _cam.X < left ? minX - left - _cam.X : maxX - _cam.X > right ? maxX - right - _cam.X : 0;
        int dy = minY - _cam.Y < top ? minY - top - _cam.Y : maxY - _cam.Y > bottom ? maxY - bottom - _cam.Y : 0;
        if (dx != 0 || dy != 0) Scroll(dx, dy);
    }

    /// <summary>
    /// 이동 고르기를 눌렀다 — 팀을 옮기고(움직임 표 = 1) 닿은 사건(조건 200)을 돌린다. 사건이 없고 그 장소에 적 말이 있으며 전투 장소면 곧바로 전투(0x100afef0, id 0xc9).
    /// </summary>
    private void MoveTo(int place)
    {
        if (_chapter is not { } chapter || place < 0 || place >= chapter.Places.Count) return;
        if (SelectedTeam is { } team) team.Moved = true;
        _party = place;
        SaveProgress();
        if (Events.Arrive(place)) return;
        var p = chapter.Places[place];
        if (EnemyAt(place) && p.IsBattle) host.LeaveScene(() => shell.StartBattle(p.Target));
    }

    /// <summary>
    /// 「진  입」 — 지금 장소가 전투면 그 전투, 마을이면 마을(원본은 장면 5 필드 = 낱말 7). 먼저 그 장소의 닿은 사건을 훑는다 —
    /// 처음 선 자리(챕터 0 카디스 요새 → 필드 2 · 전투 6)의 사건이 이것으로 돈다.
    /// </summary>
    internal void EnterHere()
    {
        if (_chapter is not { } chapter || _party < 0 || _party >= chapter.Places.Count) return;
        // 조건 202 [0, 장소] — 「진입」한 팀(0x101b4440)과 그 장소(0x101b4444, 0x100b0d9d). 챕터 29 · 31 · 42 는 장소에 들어서면 마을 · 전투 대신 이야기를 튼다.
        Events.Entered = _party;
        bool fired = Events.Arrive(_party);
        Events.Entered = -1;
        if (fired) return;
        var place = chapter.Places[_party];
        if (place.IsBattle)
        {
            if (SelectedTeam is { } team) team.Moved = true;
            host.LeaveScene(() => shell.StartBattle(place.Target));
        }
        else if (place.IsTown)
        {
            int chapterId = chapter.Id;
            host.LeaveScene(() => { _open = false; shell.Town.Open(place.Target, () => Open(chapterId)); });   // 마을 = 그 번호의 필드(시설)
        }
        else host.Toast("들어갈 곳이 없습니다");
    }

    /// <summary>
    /// 시험 · 자동 진행용 — 고른 팀을 그 장소로 곧장 옮기고(이웃 · 턴 제한 없이) 닿은 사건을 돌린 뒤, 사건이 없으면 그 장소에 들어간다(예전 「장소 누르기」).
    /// </summary>
    internal void GoTo(int place)
    {
        if (_chapter is not { } chapter || place < 0 || place >= chapter.Places.Count) return;
        _party = place;
        EnterHere();
    }

    // ── 그리기 ──

    internal void Draw()
    {
        if (_chapter is not { } chapter) return;
        Events.Pump();   // 챕터 덮개 동안의 기다림(2 · 1)을 잇는다
        var (ox, oy) = host.Mos.MosesOrigin();
        const int W = MosesScene.MosesW, H = MosesScene.MosesH;
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        EdgeScroll(ox, oy);
        Art.Clip = (ox, oy, W, H);

        // 지도 — 판에 줄 단위로 옮긴다(그림 층).
        var fb = host._fb;
        int bw = host.BoardWidth, bh = host.BoardHeight;
        int x0 = Math.Max(0, ox), x1 = Math.Min(bw, ox + W);
        int tick = (int)(host._lastTime * GameWindow.TicksPerSecond);
        if (_tiled && _mapPx.Length > 0 && x1 > x0)
        {
            // 바다(알파 0) — 물결 무늬(0x100c3d40)를 세계 자리 & 127 로. 그 위 구름(0x1011f1f0 일 때 스무 장).
            if (_rippleFrame != tick && _sea is { } sea && _seaW > 0 && _seaH > 0) { Part1ChapterMap.Ripple(sea, _seaW, _seaH, tick, _ripple); _rippleFrame = tick; }
            bool hasSea = _rippleFrame >= 0;
            for (int y = 0; y < H; y++)
            {
                int py = oy + y, my = _cam.Y + y;
                if ((uint)py >= bh || my >= _mapH) continue;
                int src = my * _mapW + _cam.X + (x0 - ox), dst = py * bw + x0, n = Math.Min(x1 - x0, _mapW - _cam.X - (x0 - ox));
                int seaRow = (my & 127) * 128, mx = _cam.X + (x0 - ox);
                for (int i = 0; i < n; i++)
                {
                    uint c = _mapPx[src + i];
                    fb[dst + i] = c >> 24 != 0 ? c : hasSea ? _ripple[seaRow + ((mx + i) & 127)] | 0xFF000000u : 0xFF102040u;
                }
            }
            StepClouds(tick);
            foreach (var (cx, cy, look) in _clouds)
                DrawCloud(_cloudLooks![look], ox + cx - _cam.X, oy + cy - _cam.Y, ox, oy);
        }
        else if (_mapPx.Length > 0 && x1 > x0)
            for (int y = 0; y < H; y++)
            {
                int py = oy + y, my = _cam.Y + y;
                if ((uint)py >= bh || my >= _mapH) continue;
                Array.Copy(_mapPx, my * _mapW + _cam.X + (x0 - ox), fb, py * bw + x0, Math.Min(x1 - x0, _mapW - _cam.X - (x0 - ox)));
            }

        // 지도 글(층)은 창 · 메뉴 · 턴 끝 알림 밑으로 비치므로 그동안은 안 그린다.
        bool window = WindowOpen || _mode == Mode.Menu || host._lastTime < _noticeUntil || host.SlotsScr.SlotsOpen || host.Sys._confirm != null;
        bool calm = Events.Talk == null && !Picking;

        // 장소 이름 — 마을(종류 1)만, 장소 아래 가운데(낱말 0 의 clg 글, 0x100b2940). 전투 장소 이름은 지도 그림에 그려져 있다. 지도 그림이 없으면 다 적는다.
        if (!window)
            for (int i = 0; i < chapter.Places.Count; i++)
            {
                var p = chapter.Places[i];
                if (_hasMap && p.Kind != 1) continue;
                string name = Part1Game.PlainText(chapter.NameOf(p)).Replace('\n', ' ');
                if (name.Length == 0) continue;
                int sx = ox + p.X + p.Words[3] / 2 - _cam.X, sy = oy + p.Y + p.Words[4] + 15 - _cam.Y;
                if (sx < ox - 80 || sx > ox + W + 80 || sy < oy - 20 || sy > oy + H) continue;
                int tw = Art.TextW(name, 12);
                if (!_hasMap) Art.Fill(sx - tw / 2 - 3, sy - 1, tw + 6, 17, p.IsBattle ? 0xA0602020u : 0xA0203860u);
                host.UiText(name, sx - tw / 2 + 1, sy + 1, 0xFF000000, 12);
                host.UiText(name, sx - tw / 2, sy, GameWindow.White, 12);
            }

        // 이동 고르기 — 갈 수 있는 이웃마다 퍼지는 고리(Obs 182, 0x100c4cf0)와 팀 자리에서 그 이웃까지의 선.
        // 원본(0x100c4e00)은 고리 하나를 마우스 쪽 이웃에 두고(0x100c50b0), 팀 장소 가운데 → 고리 자리로 선을 긋는다:
        // 0x1000c8b0 선을 (0,0) · x+1 · y+1 · x−1 · y−1 로 다섯 번(굵기 3 십자꼴), 빛깔 16비트 0x6259(15비트 0x3139) = (99, 73, 206).
        // 리메이크는 이웃마다 고리와 선을 함께 그린다.
        if (_mode == Mode.Move && _party >= 0 && _party < chapter.Places.Count)
        {
            var (fx, fy) = Screen(chapter.Places[_party], ox, oy);
            foreach (int n in MoveTargets())
            {
                var (tx, ty) = Screen(chapter.Places[n], ox, oy);
                foreach (var (dx, dy) in RouteOffsets) Line(fx + dx, fy + dy, tx + dx, ty + dy, RouteColor);
            }
        }
        if (_mode == Mode.Move)
            foreach (int n in MoveTargets())
            {
                var (sx, sy) = Screen(chapter.Places[n], ox, oy);
                if (!host.DrawUi(Part1Game.ObsBase + RingObs, 0, tick, sx, sy, GameWindow.UiBlend.Add, loop: true))   // 빛 고리 — 더하기로(검은 바탕이 지도를 가리지 않게)
                    Art.Stroke(sx - 12, sy - 12, 24, 24, 0xFFFFE080);
                if (_hover == TargetHit + n) Art.Stroke(sx - 26, sy - 26, 52, 52, 0xFFFFE080);
            }

        // 팀 말 — 장소 가운데에 깃대(Obs 226 모션 0, 적 말이면 모션 3) · 얼굴 (34, −48) · 「N 팀」 꼬리표 (59, 18k − 75)(0x100b20d0).
        for (int i = 0; i < chapter.Places.Count; i++)
        {
            var at = TeamsAt(i);
            bool enemy = EnemyAt(i);
            if (at.Count == 0 && !enemy) continue;
            var (gx, gy) = Screen(chapter.Places[i], ox, oy);
            if (gx < ox - 120 || gx > ox + W + 120 || gy < oy - 20 || gy > oy + H + 100) continue;
            Art.Blit(Art.Motion(TeamObs, at.Count > 0 ? 0 : 3), gx, gy);
            if (at.Count > 0)
            {
                int lead = at.Contains(_team) ? _team : at[0];
                if (host.Part1.TeamMembers(lead) is { Count: > 0 } members) Art.Face(members[0], gx + 34, gy - 48);
                for (int k = 0; k < at.Count; k++)
                {
                    bool on = at[k] == _team;
                    int tx = gx + 59, ty = gy + 18 * k - 75;
                    Art.Blit(Art.Motion(TeamObs, on ? 1 : 2, tick), tx, ty);
                    // 꼬리표 글은 판 위 층이라 창을 뚫고 보인다 — 전에는 메뉴 · 턴 끝 알림이 뜨기만 하면 모든 꼬리표 글을 숨겨 빈 상자로 보였다(사용자 보고).
                    // 이제는 그 꼬리표가 메뉴 · 알림 상자에 실제로 덮일 때만 숨긴다(상태 · 교환 창은 화면을 거의 덮어 늘 숨긴다).
                    if (!TagCovered(tx - ox, ty - oy, 39, 18)) Art.TextCentered($"{at[k] + 1} 팀", tx + 20, ty + 9, on ? GameWindow.White : 0xFFD0D8F0, 11);
                }
            }
        }

        // 왼위 지역 띠 — Obs 219(152×64) · 지역 이름(머리 낱말 11 의 글) · 「연 년 날 일」(낱말 6 년, 낱말 7 + 턴 일).
        Art.Blit(Art.Frame(BannerObs, 0, 0), ox, oy);
        if (chapter.Header.Length > 11 && chapter.Header[11] >= 0 && chapter.Header[11] < chapter.Lines.Count
            && Part1Game.PlainText(chapter.Lines[chapter.Header[11]]).Replace('\n', ' ') is { Length: > 0 } region)
            host.UiText(region, ox + 20, oy + 22, GameWindow.White, 13);
        if (chapter.Header.Length > 7)
        {
            int day = chapter.Header[7] + host.Part1.MapTurns.GetValueOrDefault(chapter.Id);
            // 「%d년      일」 오른끝 142 · 아래끝 59, 날 숫자는 오른끝 129(가운데 53) — 사진(「1281년 80일」)에 맞춰 놓았다.
            Art.TextRight($"{chapter.Header[6]}년", ox + 112, oy + 46, GameWindow.White, 11);
            Art.TextRight($"{day}일", ox + 142, oy + 46, GameWindow.White, 11);
        }

        // 오른쪽 단추 — 창 · 이동 고르기 · 대사 동안은 숨긴다(0x100b2070).
        if (!WindowOpen && _mode == Mode.Map && calm)
            for (int i = 0; i < Buttons.Length; i++)
            {
                bool disabled = i == ButtonMove && (SelectedTeam is { Moved: true } || MoveTargets().Count == 0);
                Art.Button(ox + ButtonX, oy + Buttons[i].Y, Buttons[i].Name, _hover == ButtonHit + i, disabled);
            }
        if (_mode == Mode.Move && calm) Art.TextCentered("갈 곳을 고르세요 (오른 단추 · Esc 취소)", ox + W / 2, oy + H - 16, 0xFFFFE080, 12);

        if (_status is { _open: true }) _status.Draw(ox, oy);
        if (_exchange is { _open: true }) _exchange.Draw(ox, oy);
        Art.Clip = default;

        // 턴 끝 알림 — 틀 상자 (200, 200) 240×40, 가운데 글.
        if (host._lastTime < _noticeUntil)
        {
            Art.Box(ox + 200, oy + 200, 240, 40);
            Art.TextCentered(_notice, ox + 320, oy + 220, GameWindow.White, 14);
        }
        if (_mode == Mode.Menu) DrawMenu(ox, oy);

        DrawCover(ox, oy);
        if (Events.Talk is { } page && Events.TalkSlot == Part1SceneScript.PageSlot)
            shell.DrawTalkBox(ox + 70, oy + 100, 500, 285, "", page.Text);                 // 1104 표 창(0x100a9000, 70 · 100 · 500×285)
        else if (Events.Talk is { } narration && Events.TalkSlot == Part1SceneScript.NarrationSlot)
            shell.DrawTalkBox(ox + 10, oy + 5, 619, 35, "", narration.Text);               // 210 해설 띠
        else if (Events.Talk is { } talk) shell.DrawTalk(ox, oy, talk.Speaker, talk.Text, Events.TalkSlot, Events.Faces);
        else if (Events.Choice is { } choice)
        {
            // 1100 물음 + 1101 고를 줄(0x100aa690 · 0x100aa750) — 고르면 챕터 변수 = 1부터 센 차례.
            shell.DrawTalkBox(ox + 64, oy + 336, 511, 111, choice.Speaker, choice.Text);
            for (int i = 0; i < choice.Options.Count; i++)
            {
                var (x, y, w, h) = ChoiceRow(i, choice.Options.Count);
                shell.DrawButton(ox + x, oy + y, w, h, choice.Options[i], i == _choiceHover);
            }
        }
        else if (Picking) DrawPick(ox, oy);
        host.DrawToast();
    }

    /// <summary>화면(640×480) 네모 (x, y, w, h) 가 떠 있는 창 · 메뉴 · 턴 끝 알림에 덮이나.</summary>
    private bool TagCovered(int x, int y, int w, int h)
    {
        if (WindowOpen || host.SlotsScr.SlotsOpen || host.Sys._confirm != null) return true;   // 저장 칸 창 · 확인 창 위로 글이 비치지 않게
        bool Hit(int rx, int ry, int rw, int rh) => x < rx + rw && x + w > rx && y < ry + rh && y + h > ry;
        if (_mode == Mode.Menu && Hit(MenuX - 10, MenuY, MenuW + 20, 36 + MenuItems.Length * MenuRowH)) return true;
        return host._lastTime < _noticeUntil && Hit(200, 200, 240, 40);
    }

    /// <summary>이동 고르기 선 빛깔(0x100c4e00: 16비트 0x6259) · 다섯 번 긋는 어긋남.</summary>
    private const uint RouteColor = 0xFF6349CE;
    private static readonly (int, int)[] RouteOffsets = [(0, 0), (1, 0), (0, 1), (-1, 0), (0, -1)];

    /// <summary>한 점 굵기 선(브레젠험) — 화면(640×480) 안만.</summary>
    private void Line(int x0, int y0, int x1, int y1, uint color)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, err = dx + dy;
        var (ox, oy) = host.Mos.MosesOrigin();
        var fb = host._fb;
        int bw = host.BoardWidth, bh = host.BoardHeight;
        for (int guard = 0; guard < 8000; guard++)
        {
            if (x0 >= ox && x0 < ox + MosesScene.MosesW && y0 >= oy && y0 < oy + MosesScene.MosesH && (uint)x0 < (uint)bw && (uint)y0 < (uint)bh) fb[y0 * bw + x0] = color;
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>챕터 100 덮개 · 103 바꾸기 — 바꾸는 중이면 나감 동안 옛 화면(덮개 또는 지도)을 검게, 들어옴 동안 새 화면을 드러낸다.</summary>
    private void DrawCover(int ox, int oy)
    {
        const int W = MosesScene.MosesW, H = MosesScene.MosesH;
        var cover = _cover;
        double fade = 0;
        if (_transition is { } tr)
        {
            double t = (host._lastTime - tr.Start) * GameWindow.TicksPerSecond;
            if (t < tr.Out) { cover = tr.From; fade = t / Math.Max(1, tr.Out); }
            else if (t < tr.Out + tr.In) fade = 1 - (t - tr.Out) / Math.Max(1, tr.In);
            else _transition = null;
        }
        _shownCover = cover;
        if (cover is { } c)
        {
            int sx = Math.Clamp(c.X, 0, Math.Max(0, c.W - W)), sy = Math.Clamp(c.Y, 0, Math.Max(0, c.H - H));
            host.FillRect(ox, oy, W, H, 0xFF000000);
            for (int y = 0; y < Math.Min(H, c.H - sy); y++)
                for (int x = 0; x < Math.Min(W, c.W - sx); x++)
                    host.SetPixel(ox + x, oy + y, c.Px[(sy + y) * c.W + sx + x] | 0xFF000000);
        }
        if (fade > 0) host.FillRect(ox, oy, W, H, (uint)(Math.Clamp(fade, 0, 1) * 255) << 24);
    }

    /// <summary>고르기 줄 자리(필드와 같은 꼴) — 물음 상자 위에 쌓는다.</summary>
    private static (int X, int Y, int W, int H) ChoiceRow(int i, int count) => (120, 330 - (count - i) * 32, 400, 28);

    private int _choiceHover;

    private int ChoiceAt(int bx, int by)
    {
        if (Events.Choice is not { } choice) return -1;
        var (ox, oy) = host.Mos.MosesOrigin();
        for (int i = 0; i < choice.Options.Count; i++)
        {
            var (x, y, w, h) = ChoiceRow(i, choice.Options.Count);
            if (bx >= ox + x && bx < ox + x + w && by >= oy + y && by < oy + y + h) return i;
        }
        return -1;
    }

    private void DrawMenu(int ox, int oy)
    {
        int h = 36 + MenuItems.Length * MenuRowH;
        host.UiFill(ox + MenuX - 10, oy + MenuY, MenuW + 20, h, 0xF0182040);
        host.UiStroke(ox + MenuX - 10, oy + MenuY, MenuW + 20, h, 0xFFE8ECF8);
        host.UiText("메뉴", ox + MenuX, oy + MenuY + 8, 0xFFFFE080, 13);
        for (int i = 0; i < MenuItems.Length; i++)
        {
            int y = oy + MenuY + 30 + i * MenuRowH;
            if (_hover == MenuHit + i) host.UiFill(ox + MenuX, y, MenuW, MenuRowH - 2, 0xFF2840C8);
            host.UiText(MenuItems[i], ox + MenuX + 10, y + 5, GameWindow.White, 13);
        }
    }

    /// <summary>마우스가 화면 가장자리(8점 안)에 있으면 그쪽으로 민다 — 초에 600점. 창 · 메뉴 · 대사 중에는 안 민다.</summary>
    private void EdgeScroll(int ox, int oy)
    {
        double now = host._lastTime, dt = Math.Clamp(now - _lastScroll, 0, 0.1);
        _lastScroll = now;
        if (WindowOpen || _mode == Mode.Menu || Events.Talk != null || Picking) return;
        var (mx, my) = host._mouse;
        int x = mx - ox, y = my - oy;
        if (x < 0 || y < 0 || x >= MosesScene.MosesW || y >= MosesScene.MosesH) return;
        int step = (int)Math.Round(600 * dt);
        int dx = x < 8 ? -step : x >= MosesScene.MosesW - 8 ? step : 0, dy = y < 8 ? -step : y >= MosesScene.MosesH - 8 ? step : 0;
        if (dx != 0 || dy != 0) Scroll(dx, dy);
    }
}
