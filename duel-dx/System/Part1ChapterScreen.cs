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
    /// <summary>오른쪽 단추 다섯 — 가운데 x 570(지도 화면 오른끝 − 70), 가운데 y 와 글(원본 0x100b1c30, id 0x64~0x68).</summary>
    internal static readonly (int Y, string Name)[] Buttons = [(30, "이  동"), (65, "진  입"), (100, "상  태"), (135, "교  환"), (185, "턴종료")];

    internal const int ButtonX = 570;

    /// <summary>단추 가운데 x — 원본 640 화면의 570 을 지도 화면 오른끝에 붙인다(넓은 창에서도 오른위).</summary>
    private static int ButtonLeft(int ox, int w) => ox + w - (MosesScene.MosesW - ButtonX);
    internal const int ButtonMove = 0, ButtonEnter = 1, ButtonStatus = 2, ButtonExchange = 3, ButtonEndTurn = 4, ButtonHit = 1000;

    /// <summary>「N 팀」 꼬리표(id 0x12d + 팀) · 이동 고르기 장소 · Esc 메뉴 줄 · 마을 장소(누르면 상점 정보).</summary>
    internal const int TeamHit = 3000, TargetHit = 4000, MenuHit = 5000, TownHit = 6000;

    /// <summary>
    /// 단추 소리 — 원본 장면 4 메시지 처리기(0x100aff10~)가 「상  태」(0x100b0e58) · 「교  환」(0x100b0e90) · 「턴종료」(0x100b0ecf)를 누를 때(0x3e8)와
    /// 「N 팀」 꼬리표를 누를 때(0x3e9, 0x100aff43) <c>0x1001a330(0x34, 0)</c> = <c>Snd 0052</c> 를 한 번 낸다. 「이  동」 · 「진  입」 · 이동 고르기 · 짚기는 소리가 없고,
    /// 못 쓰는 단추(상태 3)는 메시지가 안 와 소리도 없다.
    /// </summary>
    internal const int PressSound = 0x34;

    private void PlayPress() => host.Play(Part1Game.ObsBase + PressSound);

    /// <summary>원본 그림 — 지역 띠 · 팀 말(깃대 · 꼬리표) · 이동 표지(퍼지는 고리).</summary>
    internal const int BannerObs = 219, TeamObs = 226, RingObs = 182;

    /// <summary>Esc 메뉴 — 원본 지도에는 없다(시스템 메뉴 쪽). 리메이크에 있던 연대표 · 일행 창 · 타이틀을 여기에 둔다.</summary>
    internal static readonly string[] MenuItems = ["연대표", "일행 · 장비", "동료 정보", "저장", "불러오기", "파트1 타이틀", "돌아가기"];

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
    internal bool WindowOpen => _status is { _open: true } || _exchange is { _open: true } || _shops is { _open: true };

    /// <summary>마을 상점 정보 창 — 지도에서 마을 장소를 누르면 뜬다(<see cref="Part1TownShopInfo"/>).</summary>
    internal Part1TownShopInfo Shops => _shops ??= new(host, this);
    private Part1TownShopInfo? _shops;

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

    /// <summary>처음 화면 가운데(지도 좌표) — 지도 화면 크기는 판을 넓힌 뒤에야 알므로 첫 그리기에서 <see cref="_cam"/> 로 바꾼다.</summary>
    private (int X, int Y)? _camAim;

    /// <summary>
    /// 지도를 비추는 화면 네모 — 보이는 판 전체(사용자 요청: 넓은 창이면 지도를 넓게). 지도가 그보다 작으면 지도 크기로 가운데에 두고 둘레는 검게.
    /// 원본은 640×480 한 장 — 지역 띠는 이 네모 왼위, 단추는 오른위에 붙고, 창 · 메뉴 · 대사는 가운데 640×480 틀(<see cref="MosesScene.MosesOrigin"/>)에 그대로 뜬다.
    /// </summary>
    internal (int X, int Y, int W, int H) MapView()
    {
        int vw = host.ViewWidth, vh = host.ViewHeight;
        int w = Math.Min(vw, _mapW > 0 ? _mapW : MosesScene.MosesW), h = Math.Min(vh, _mapH > 0 ? _mapH : MosesScene.MosesH);
        return (host._camX + (vw - w) / 2, host._camY + (vh - h) / 2, w, h);
    }

    /// <summary>
    /// 판 크기 — 지도를 그리는 동안은 창(해상도 ÷ 배율)만큼 넓은 판을 쓰고, 다른 파트1 화면으로 가면 640 틀 판으로 되돌린다.
    /// 판이 640 틀이면 <see cref="GameWindow.ViewWidth"/> 가 640 에 묶여 넓은 창에서도 지도가 가운데 640×480 만 보였다(사용자 보고).
    /// 배율은 판 너비 640 이상이면 판과 상관없다(<see cref="GameWindow.FitZoom"/>) — 글 · 그림 크기는 그대로, 보이는 지도만 넓어진다.
    /// </summary>
    internal void FitBoard(bool showing)
    {
        if (showing)
        {
            double zoom = host._zoom > 0 ? host._zoom : 1;
            int w = (int)Math.Ceiling(host._resW / zoom), h = (int)Math.Ceiling(host._resH / zoom);
            int cols = Math.Max(GameWindow.TitleBoardCols, (w + GameWindow.TileW - 1) / GameWindow.TileW);
            // 보이는 높이는 판의 70% 까지(ViewHeight) — 그만큼 줄을 넉넉히.
            int rows = Math.Max(GameWindow.TitleBoardRows, (int)Math.Ceiling((h * 10 / 7.0 - GameWindow.GridTop) / GameWindow.TileH) + 1);
            if (host.Cols != cols || host.Rows != rows) host.ResizeViewBoard(cols, rows);
            _wideBoard = (cols, rows);
        }
        else if (_wideBoard != default)
        {
            // 전투가 판을 제 맵으로 갈았으면 그대로 둔다 — 이 지도가 넓힌 판일 때만 되돌린다.
            if ((host.Cols, host.Rows) == _wideBoard && !host.BoardIsMap) host.ResizeViewBoard(GameWindow.TitleBoardCols, GameWindow.TitleBoardRows);
            _wideBoard = default;
        }
    }

    private (int Cols, int Rows) _wideBoard;

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
        if (team > 0 || teams[0].Fixed) teams[team].Members.Add(chr);
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
        SceneChange = OnSceneChange, PlaceBattle = SetPlaceBattle, PlaceTown = SetPlaceTown, MarkerMoved = OnMarkerMoved,
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
        PictureHide = n => { if (_chapter is { } c) _hiddenPictures.Add((c.Id, n)); },
        Ending = OnEnding,
    };

    /// <summary>챕터 100 의 그림 덮개(BGRs, 그림 안 자리) — 덮인 동안 지도 대신 그린다. 102 · 103 [1, …] 이 걷는다.</summary>
    private (uint[] Px, int W, int H, int X, int Y)? _cover;

    /// <summary>지난 틀에 보인 덮개 — 103 의 「옛 화면」.</summary>
    private (uint[] Px, int W, int H, int X, int Y)? _shownCover;

    /// <summary>103 화면 바꾸기 — (옛 덮개, 나감 틱, 들어옴 틱, 시작). 나감 동안 옛 화면을 검게 바래고, 들어옴 동안 새 화면을 드러낸다(0x100ab380).</summary>
    private ((uint[] Px, int W, int H, int X, int Y)? From, int Out, int In, double Start)? _transition;

    /// <summary>
    /// 챕터 705(0x100c1ab0) — <b>인자를 안 읽고</b> 장면 요청 6 · 번호 1, +0x13ca0 = 1. 장면 6(0x100d5480 → 0x100d5500)은 번호(+0xc)가 0 이면 <c>G3.pak</c>(오프닝 영상 —
    /// 부팅이 거는 것), 아니면 <c>Setup\_sys2.cab</c>(엔딩 영상 — 설치 폴더에 없으면 시디에서, 0x10004ac0)을 DirectShow(0x100089b0)로 틀고 Esc 로 건너뛴 뒤 장면 7(불러오기 · 타이틀 · 새로)로 간다.
    /// 챕터 55 [0] · 56 [1] 둘 다 엔딩 영상이다. 리메이크는 그 영상이 없어(시디에만 있다) 영상 없이 챕터를 끝낸 것으로 적고 파트1 타이틀로 돌아간다.
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
        host.Part1.Fight = null;
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
        _shops?.Close();
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
        host.Part1.Fight = [chapter.Id, 3, marker, place];   // 갈래 3 — 말이 팀에게(0x100c117d)
        host.LeaveScene(() => { SaveProgress(); shell.StartBattle(battle); });   // 남은 행동을 적고 간다 — 전투 세이브가 이 진행을 싣는다
        return true;
    }

    /// <summary>
    /// 지도에서 연 전투를 이기고 돌아왔다(<see cref="Part1Progress.Fight"/>) — 원본 장면 4 를 다시 열 때(0x100b4de0, 돌아온 갈래 <c>+0x13cac</c>):
    /// 1 진입 = 그 장소 낱말 7(전투) = −1(결과 2 가 아니면) · 2 팀이 말에게 = 말 살았나 <c>+0xe</c> = 0 · 장소 칸 비움 · 팀을 그 장소로 · 낱말 7 = −1(결과 2 면 팀은 제자리) ·
    /// 3 말이 팀에게(900) = 말 <c>+0xe</c> = 0 · 낱말 7 = −1(결과를 안 본다). 챕터 조건 301 [말, v](0x100bd510)이 이 「살았나」를 본다 —
    /// 챕터 28 사건 38(말 1 이 쓰러지면 장소 23 을 마을 153 으로) · 25 · 28 · 34~36, 챕터 10 사건 12 · 15.
    /// 리메이크는 진 전투(결과 2 · 4)를 타이틀로 보내므로 이긴 때만 온다. 쓰러진 말은 지도 말 −1(902 와 같다), 지운 전투는 장소 전투 −1(길목)로 적는다.
    /// </summary>
    internal void SettleFight()
    {
        if (host.Part1.Fight is not [int chapterId, int kind, int marker, int place]) return;
        host.Part1.Fight = null;
        if (kind is 2 or 3 && marker >= 0)
        {
            if (!host.Part1.ChapterMarkers.TryGetValue(chapterId, out var markers) && Part1Chapter.Load(chapterId) is { } loaded)
                host.Part1.ChapterMarkers[chapterId] = markers = [.. loaded.Markers];
            if (markers != null && marker < markers.Count) markers[marker] = -1;
        }
        host.Part1.PlaceTowns.Remove($"{chapterId}:{place}");
        host.Part1.PlaceBattles[$"{chapterId}:{place}"] = -1;
    }

    /// <summary>1201 — 장소를 전투 장소로 바꾸고 진행에 적는다(다시 열어도 그대로).</summary>
    private void SetPlaceBattle(int place, int battle)
    {
        if (_chapter is not { } chapter || place < 0 || place >= chapter.Places.Count) return;
        host.Part1.PlaceTowns.Remove($"{chapter.Id}:{place}");
        host.Part1.PlaceBattles[$"{chapter.Id}:{place}"] = battle;
        _chapter = WithPlaceBattles(chapter);
    }

    /// <summary>챕터 1200 [장소, 마을] — 그 장소를 마을 장소로(0x100c19f0: 낱말 7 = 마을, 낱말 6 = 1). 진행에 적어 다시 열어도 남는다.</summary>
    private void SetPlaceTown(int place, int town)
    {
        if (_chapter is not { } chapter || place < 0 || place >= chapter.Places.Count) return;
        host.Part1.PlaceBattles.Remove($"{chapter.Id}:{place}");
        host.Part1.PlaceTowns[$"{chapter.Id}:{place}"] = town;
        _chapter = WithPlaceBattles(chapter);
    }

    /// <summary>진행에 적힌 1201 · 1200 바꿈을 장소에 입힌다.</summary>
    private Part1Chapter WithPlaceBattles(Part1Chapter chapter) =>
        chapter with
        {
            Places = [.. chapter.Places.Select((p, i) =>
                host.Part1.PlaceBattles.TryGetValue($"{chapter.Id}:{i}", out int battle) ? p with { Kind = 0, Target = battle }
                : host.Part1.PlaceTowns.TryGetValue($"{chapter.Id}:{i}", out int town) ? p with { Kind = 1, Target = town } : p)],
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
        host.Part1.CloseChapter();   // 용병 → eld(×100), 용병 · 탑승물 비우기(0x100ae560)
        host.Part1.Fight = null;
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
        host.Part1.Fight = null;   // 이긴 전투에서 돌아왔으면 SettleFight 가 이미 적었다 — 이어 하기 등 다른 길이면 그 전투는 다시 연다
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
        // 넓은 창이면 원본 640×480 의 가운데를 지도 화면 가운데에 둔다(첫 그리기에서, _camAim).
        if (!same)
        {
            _cam = (chapter.Header.Length > 4 ? chapter.Header[3] : 0, chapter.Header.Length > 4 ? chapter.Header[4] : 0);
            ClampCamera(MosesScene.MosesW, MosesScene.MosesH);
            _camAim = (_cam.X + MosesScene.MosesW / 2, _cam.Y + MosesScene.MosesH / 2);
        }
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
    private void DrawCloud(byte[] look, int sx, int sy, (int X, int Y, int W, int H) view)
    {
        var fb = host._fb;
        int bw = host.BoardWidth, bh = host.BoardHeight;
        int x0 = Math.Max(Math.Max(sx, view.X), 0), x1 = Math.Min(Math.Min(sx + 320, view.X + view.W), bw);
        int y0 = Math.Max(Math.Max(sy, view.Y), 0), y1 = Math.Min(Math.Min(sy + 240, view.Y + view.H), bh);
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

    private void ClampCamera()
    {
        var (_, _, w, h) = MapView();
        ClampCamera(w, h);
    }

    private void ClampCamera(int w, int h) =>
        _cam = (Math.Clamp(_cam.X, 0, Math.Max(0, _mapW - w)), Math.Clamp(_cam.Y, 0, Math.Max(0, _mapH - h)));

    /// <summary>화면을 민다(지도 좌표).</summary>
    internal void Scroll(int dx, int dy)
    {
        _cam = (_cam.X + dx, _cam.Y + dy);
        ClampCamera();
    }

    /// <summary>끌어서 밀 수 있는 누름인가(<see cref="BattleDragScroll"/>) — 가장자리 스크롤과 같은 때, 지도가 화면보다 크고 누른 자리가 지도 화면 안.</summary>
    internal bool DragScrollAllowed(int bx, int by)
    {
        if (_chapter == null || WindowOpen || _mode == Mode.Menu || Events.Talk != null || Events.Choice != null || Picking || _cover != null || _transition != null) return false;
        var (x, y, w, h) = MapView();
        return (_mapW > w || _mapH > h) && bx >= x && bx < x + w && by >= y && by < y + h;
    }

    /// <summary>끄는 동안 — 지도 왼위를 (x, y) 로 두고 지도 안으로 자른다.</summary>
    internal void DragCamera(int x, int y)
    {
        _cam = (x, y);
        _camAim = null;
        ClampCamera();
    }

    /// <summary>그 장소가 화면 가운데쯤 오게.</summary>
    internal void CenterOn(int place)
    {
        if (_chapter is not { } chapter || place < 0 || place >= chapter.Places.Count) return;
        var (cx, cy) = Center(chapter.Places[place]);
        var (_, _, w, h) = MapView();
        _cam = (cx - w / 2, cy - h / 2);
        _camAim = null;
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

    /// <summary>이동 고르기 표지가 선 이웃(장소 차례) — 처음엔 첫 이웃, 마우스를 움직이면 마우스 쪽 이웃.</summary>
    private int _moveAim = -1;

    /// <summary>
    /// 이동 고르기 표지의 이웃 — 원본 0x100c50b0(틱마다): 팀 장소 가운데 → 마우스 벡터와 팀 → 이웃 벡터의 코사인이 가장 큰 이웃(깃발 낱말 5 로 거른 것)으로
    /// 고리(Obs 182)를 옮긴다. 마우스가 팀 가운데에 있으면 그대로. 고리 · 선은 이 이웃 하나에만 그린다(사용자 보고: 갈 수 있는 모든 곳에 선이 나온다).
    /// </summary>
    private int MoveAim(int bx, int by)
    {
        if (_chapter is not { } chapter || _party < 0 || _party >= chapter.Places.Count) return -1;
        var targets = MoveTargets();
        if (targets.Count == 0) return _moveAim = -1;
        if (!targets.Contains(_moveAim)) _moveAim = targets[0];
        var (fx, fy) = Center(chapter.Places[_party]);
        var (ox, oy, _, _) = MapView();
        double mx = bx - (ox + fx - _cam.X), my = by - (oy + fy - _cam.Y), mm = Math.Sqrt(mx * mx + my * my);
        if (mm < 1) return _moveAim;
        double best = -2;
        foreach (int n in targets)
        {
            var (tx, ty) = Center(chapter.Places[n]);
            double vx = tx - fx, vy = ty - fy, vv = Math.Sqrt(vx * vx + vy * vy);
            double cos = vv < 1 ? -1 : (vx * mx + vy * my) / (vv * mm);
            if (cos > best) { best = cos; _moveAim = n; }
        }
        return _moveAim;
    }

    // ── 맞음 ──

    internal int HitAt(int bx, int by)
    {
        if (_chapter is not { } chapter) return -1;
        if (_mode == Mode.Menu)
        {
            var (mx, my) = host.Mos.MosesOrigin();   // 메뉴는 가운데 640×480 틀
            for (int i = 0; i < MenuItems.Length; i++)
                if (bx >= mx + MenuX && bx < mx + MenuX + MenuW && by >= my + MenuY + 30 + i * MenuRowH && by < my + MenuY + 30 + (i + 1) * MenuRowH - 2) return MenuHit + i;
            return -1;
        }
        var (ox, oy, ow, _) = MapView();
        if (_mode == Mode.Move)
        {
            // 원본 0x100c4e00 의 표지는 하나 — 지도 화면 어디를 눌러도 표지가 선 이웃(마우스 쪽)으로 간다. 오른 단추 · Esc 가 취소.
            var (_, _, _, oh) = MapView();
            if (bx < ox || by < oy || bx >= ox + ow || by >= oy + oh) return -1;
            return MoveAim(bx, by) is >= 0 and var aim ? TargetHit + aim : -1;
        }
        for (int i = 0; i < Buttons.Length; i++)
            if (Part1MapArt.ButtonHit(ButtonLeft(ox, ow), oy + Buttons[i].Y, bx, by)) return ButtonHit + i;
        // 「N 팀」 꼬리표 — 묶음 기준 (59, 18k − 75), 39×18.
        for (int i = 0; i < chapter.Places.Count; i++)
        {
            var at = TeamsAt(i);
            if (at.Count == 0) continue;
            var (gx, gy) = Screen(chapter.Places[i], ox, oy);
            for (int k = 0; k < at.Count; k++)
                if (bx >= gx + 59 && bx < gx + 59 + 39 && by >= gy + 18 * k - 75 && by < gy + 18 * k - 75 + 18) return TeamHit + at[k];
        }
        // 마을 장소 — 장소 네모(낱말 1~4)나 그 밑 이름 글.
        for (int i = 0; i < chapter.Places.Count; i++)
            if (TownBounds(chapter, i, ox, oy) is { } b && (In(b.Place, bx, by) || In(b.Label, bx, by))) return TownHit + i;
        return -1;
    }

    private static bool In((int X, int Y, int W, int H) r, int x, int y) => x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H;

    /// <summary>마을 장소(종류 1, 마을 번호 있음)의 화면 네모 — 장소 네모와 이름 글 네모(<see cref="Draw"/> 의 장소 이름 자리). 마을이 아니면 null.</summary>
    private ((int X, int Y, int W, int H) Place, (int X, int Y, int W, int H) Label)? TownBounds(Part1Chapter chapter, int i, int ox, int oy)
    {
        var p = chapter.Places[i];
        if (!p.IsTown) return null;
        int px = ox + p.X - _cam.X, py = oy + p.Y - _cam.Y, pw = Math.Max(20, (int)p.Words[3]), ph = Math.Max(20, (int)p.Words[4]);
        string name = Part1Game.PlainText(chapter.NameOf(p)).Replace('\n', ' ');
        int tw = name.Length > 0 ? Art.TextW(name, 12) : 0;
        int sx = px + p.Words[3] / 2, sy = py + p.Words[4] + 15;
        return ((px, py, pw, ph), (sx - tw / 2 - 3, sy - 1, tw + 6, 17));
    }

    internal void UpdateHover(int bx, int by)
    {
        if (Events.Choice != null && Events.Talk == null) { if (ChoiceAt(bx, by) is >= 0 and var c) _choiceHover = c; return; }
        if (Picking) { _hover = PickAt(bx, by) + PickHit; return; }
        if (_status is { _open: true }) { _status.UpdateHover(bx, by); return; }
        if (_exchange is { _open: true }) { _exchange.UpdateHover(bx, by); return; }
        if (_shops is { _open: true }) { _shops.UpdateHover(bx, by); _hover = -1; return; }
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
        if (BattleScene.Trace) File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"part1 pick {pick.Chr} ability {option.Ability} ({option.Name}) Lv {option.NewLevel} in battle {host._battleLoaded}" + Environment.NewLine);
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
        if (_shops is { _open: true }) { _shops.OnClick(bx, by); return; }   // 창 밖을 누르면 닫힌다
        int hit = HitAt(bx, by);
        if (_mode == Mode.Move && hit < 0) { _mode = Mode.Map; return; }   // 지도 화면 밖을 누르면 취소(단추가 다시 보인다)
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
        else if (_shops is { _open: true }) _shops.Close();
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
            case ButtonHit + ButtonEnter: if (CanEnter()) EnterHere(); break;          // 못 쓰는 단추(상태 3)는 눌러도 아무 일 없다
            case ButtonHit + ButtonStatus: PlayPress(); Status.Open(); break;
            case ButtonHit + ButtonExchange: if (CanExchange()) { PlayPress(); Exchange.Open(); } break;
            case ButtonHit + ButtonEndTurn: PlayPress(); EndMapTurn(); break;
            case >= TeamHit and < TeamHit + 8: PlayPress(); SelectTeam(hit - TeamHit); break;
            case >= TownHit and < TownHit + 1000 when hit - TownHit < chapter.Places.Count && chapter.Places[hit - TownHit] is { IsTown: true } town:
                PlayPress();
                Shops.Open(Part1Game.PlainText(chapter.NameOf(town)).Replace('\n', ' ').Trim(), town.Target);
                break;
            case >= TargetHit and < TargetHit + 1000: _mode = Mode.Map; MoveTo(hit - TargetHit); break;
            case MenuHit + 0: _mode = Mode.Map; host.LeaveScene(() => { _open = false; shell.Timeline.Open(); }); break;
            case MenuHit + 1: _mode = Mode.Map; OpenParty(0); break;
            case MenuHit + 2: _mode = Mode.Map; host.PartyTable.Open(); break;   // 일행 표(Part1PartyTable, P 와 같다)
            // 저장 · 불러오기 — 전투 SAVE 와 같은 칸 창(파트1 칸 part1-battle-save-NN.json). 지도에서 적은 칸은 장면 갈래 4(「챕  터」)로, 불러오면 이 지도로 돌아온다.
            case MenuHit + 3: _mode = Mode.Map; SaveProgress(); host.SlotsScr.OpenSlots(0); break;
            case MenuHit + 4: _mode = Mode.Map; host.SlotsScr.OpenSlots(1); break;
            case MenuHit + 5: _mode = Mode.Map; host.LeaveScene(() => _open = false); break;
            case MenuHit + 6: _mode = Mode.Map; break;
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

    /// <summary>
    /// 「진  입」을 쓸 수 있나 — 원본은 지도를 다시 그릴 때마다(0x100b20d0 끝, 0x100b265c~) 단추 상태를 정한다: 고른 팀이 이번 턴에 움직였으면(+0xaa4) 「이  동」과 함께 못 씀(상태 3, 회색),
    /// 아니면 그 팀 장소의 낱말 7(전투 · 마을 번호)이 −1 이거나 전투 장소(낱말 6 = 0)인데 낱말 8 ≠ 0(적 말이 길목에서 막는 전투 — 이동으로만 부딪친다)이면 못 씀.
    /// 들어갈 곳이 없는 장소(챕터 21 팬드래건 북부 처음 자리)에서 단추가 살아 「들어갈 곳이 없습니다」만 띄웠다(사용자 보고).
    /// </summary>
    internal bool CanEnter()
    {
        if (_chapter is not { } chapter || _party < 0 || _party >= chapter.Places.Count || SelectedTeam is { Moved: true }) return false;
        var place = chapter.Places[_party];
        return place.Target >= 0 && !(place.Kind == 0 && place.Words.Length > 8 && place.Words[8] != 0);
    }

    /// <summary>「교  환」을 쓸 수 있나(0x100b2780~) — 고른 팀 장소에 다른 팀이 있거나, 그 장소가 본부(머리 낱말 9)일 때. 움직였는지는 안 본다.</summary>
    internal bool CanExchange()
    {
        if (_chapter is not { } chapter || SelectedTeam is not { } team) return false;
        if (TeamsAt(team.Place).Any(t => t != SelectedTeamIndex)) return true;
        return chapter.Header.Length > 9 && chapter.Header[9] >= 0 && team.Place == chapter.Header[9];
    }

    /// <summary>「이  동」 — 고른 팀이 이번 턴에 안 움직였고 갈 이웃이 있으면 고르기 표지를 띄운다(없으면 아무 일 없음).</summary>
    private void StartMove()
    {
        if (SelectedTeam is { Moved: true }) { host.Toast("이 팀은 이번 턴에 이미 움직였습니다"); return; }
        if (MoveTargets().Count == 0) return;
        _mode = Mode.Move;
        _hover = -1;
        _moveAim = -1;
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
        var (_, _, w, h) = MapView();
        int left = 40, right = w - 140, top = 100, bottom = h - 40;   // 띠(152×64) · 오른쪽 단추(오른끝 − 120 ~) · 아래 안내 글을 비킨다
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
        if (EnemyAt(place) && p.IsBattle)
        {
            host.Part1.Fight = [chapter.Id, 2, Events.Markers.FindIndex(m => m == place), place];   // 갈래 2 — 팀이 말에게(0x100b131a, 그 장소 첫 칸의 말)
            host.LeaveScene(() => shell.StartBattle(p.Target));
        }
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
            host.Part1.Fight = [chapter.Id, 1, -1, _party];   // 갈래 1 — 진입(0x100b0ddb)
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
        // 팀을 안 꾸린 채(504 · 904 가 없거나 머리 낱말 2 ≠ 1) 지도가 손에 들어오면 원본처럼 팀 편성 교환 창(갈래 0)을 연다(0x100b3f10 → 0x100b6860(-1)).
        // 본부 짐(이야기 무리)을 팀으로 나눠 가지는 자리다 — 닫으면 꾸린 것으로 친다.
        if (_open && !host.Part1.Formed.Contains(chapter.Id) && Events.Idle && !WindowOpen && !Picking && _mode == Mode.Map
            && _cover == null && _transition == null && SelectedTeam != null)
            Exchange.Open(formation: true);
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        var view = MapView();
        var (ox, oy, W, H) = view;
        var (cx0, cy0) = host.Mos.MosesOrigin();   // 창 · 메뉴 · 대사 · 알림 — 가운데 640×480 틀
        if (_camAim is { } aim) { _cam = (aim.X - W / 2, aim.Y - H / 2); _camAim = null; }
        ClampCamera(W, H);   // 창 크기가 바뀌어도 지도 밖을 비추지 않게
        EdgeScroll(view);
        Art.Clip = view;

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
                DrawCloud(_cloudLooks![look], ox + cx - _cam.X, oy + cy - _cam.Y, view);
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

        // 짚은 마을 — 장소 네모에 노란 두 줄 테, 이름 글에 노란 테(이동 고르기 · 고르기 창의 짚음과 같은 빛깔). 누르면 상점 정보 창.
        if (!window && _mode == Mode.Map && calm && _hover >= TownHit && _hover - TownHit < chapter.Places.Count
            && TownBounds(chapter, _hover - TownHit, ox, oy) is { } town)
        {
            var (r, l) = (town.Place, town.Label);
            Art.Stroke(r.X - 3, r.Y - 3, r.W + 6, r.H + 6, HoverColor);
            Art.Stroke(r.X - 2, r.Y - 2, r.W + 4, r.H + 4, HoverColor);
            if (l.W > 6) { host.UiStroke(l.X, l.Y, l.W, l.H, HoverColor); host.UiStroke(l.X + 1, l.Y + 1, l.W - 2, l.H - 2, 0x80FFE080); }
        }

        // 지도 그림(장소 뒤 12바이트 목록 — 거의 Obs 820 엇갈린 두 칼, 전투 장소마다) — 원본 0x100b2b30 이 지도를 세울 때 모션 0 을 (x, y) 에 되풀이로 둔다.
        // 팀 말(0x100b20d0)보다 먼저 만든 개체라 그 밑에 그린다. 챕터 304 [n] 이 숨긴다(_hiddenPictures).
        for (int i = 0; i < chapter.Pictures.Count; i++)
        {
            var pic = chapter.Pictures[i];
            if (pic.Length < 3 || pic[0] <= 0 || _hiddenPictures.Contains((chapter.Id, i))) continue;
            int px = ox + pic[1] - _cam.X, py = oy + pic[2] - _cam.Y;
            if (px < ox - 120 || px > ox + W + 120 || py < oy - 120 || py > oy + H + 120) continue;
            Art.Blit(Art.Motion(pic[0], 0, tick), px, py);
        }

        // 이동 고르기 — 갈 수 있는 이웃마다 퍼지는 고리(Obs 182, 0x100c4cf0)와 팀 자리에서 그 이웃까지의 선.
        // 원본(0x100c4e00)은 고리 하나를 마우스 쪽 이웃에 두고(0x100c50b0, MoveAim), 팀 장소 가운데 → 고리 자리로 선을 긋는다:
        // 0x1000c8b0 선을 (0,0) · x+1 · y+1 · x−1 · y−1 로 다섯 번(굵기 3 십자꼴), 빛깔 16비트 0x6259(15비트 0x3139) = (99, 73, 206).
        // 전에는 이웃마다 고리와 선을 다 그렸다 — 원본처럼 표지가 선 이웃 하나만(사용자 보고).
        if (_mode == Mode.Move && _party >= 0 && _party < chapter.Places.Count && MoveAim(host._mouse.X, host._mouse.Y) is >= 0 and var aimed)
        {
            var (fx, fy) = Screen(chapter.Places[_party], ox, oy);
            var (tx, ty) = Screen(chapter.Places[aimed], ox, oy);
            foreach (var (dx, dy) in RouteOffsets) Line(fx + dx, fy + dy, tx + dx, ty + dy, RouteColor);
            if (!host.DrawUi(Part1Game.ObsBase + RingObs, 0, tick, tx, ty, GameWindow.UiBlend.Add, loop: true))   // 빛 고리 — 더하기로(검은 바탕이 지도를 가리지 않게)
                Art.Stroke(tx - 12, ty - 12, 24, 24, 0xFFFFE080);
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
                    if (!TagCovered(tx - cx0, ty - cy0, 39, 18)) Art.TextCentered($"{at[k] + 1} 팀", tx + 20, ty + 9, on ? GameWindow.White : 0xFFD0D8F0, 11);
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
                bool disabled = i switch
                {
                    ButtonMove => SelectedTeam is { Moved: true } || MoveTargets().Count == 0,
                    ButtonEnter => !CanEnter(),
                    ButtonExchange => !CanExchange(),
                    _ => false,
                };
                Art.Button(ButtonLeft(ox, W), oy + Buttons[i].Y, Buttons[i].Name, _hover == ButtonHit + i, disabled);
            }
        if (_mode == Mode.Move && calm) Art.TextCentered("갈 곳을 고르세요 (오른 단추 · Esc 취소)", ox + W / 2, oy + H - 16, 0xFFFFE080, 12);

        if (_status is { _open: true }) _status.Draw(cx0, cy0);
        if (_exchange is { _open: true }) _exchange.Draw(cx0, cy0);
        if (_shops is { _open: true }) _shops.Draw(cx0, cy0);
        Art.Clip = default;

        // 턴 끝 알림 — 틀 상자 (200, 200) 240×40, 가운데 글.
        if (host._lastTime < _noticeUntil)
        {
            Art.Box(cx0 + 200, cy0 + 200, 240, 40);
            Art.TextCentered(_notice, cx0 + 320, cy0 + 220, GameWindow.White, 14);
        }
        if (_mode == Mode.Menu) DrawMenu(cx0, cy0);

        DrawCover(view, cx0, cy0);
        (ox, oy) = (cx0, cy0);   // 아래 대사 · 고르기 창은 가운데 640×480 틀
        if (Events.Choice == null) shell.ClearOptions();                                       // 고르기가 닫히면 줄 찍기를 잊는다
        if (Events.Talk != null && Events.TalkSlot == Part1SceneScript.PageSlot)
            shell.DrawTableWindow(ox + 70, oy + 100, Events.PageRows);                      // 1104 표 창(0x100a9000, 70 · 100 · 500×285)
        else if (Events.Talk != null && Events.TalkSlot == Part1SceneScript.NarrationSlot)
            shell.DrawFormattedBox(ox + 10, oy + 5, 619, 35, Events.TalkRaw);                 // 210 해설 띠
        else if (Events.Talk != null) shell.DrawTalk(ox, oy, Events.TalkRaw, Events.TalkSlot, Events.Faces);
        else if (Events.Choice is { } choice)
        {
            // 1100 물음 + 1101 고를 줄(0x100aa690 · 0x100aa750) — 고르면 챕터 변수 = 1부터 센 차례.
            shell.DrawFormattedBox(ox + 64, oy + 336, 511, 111, choice.Raw, arrow: false);   // 물음 창도 꾸밈대로 찍는다(틱 0x100aa7e0)
            for (int i = 0; i < choice.Options.Count; i++)
            {
                var (x, y, w, h) = ChoiceRow(i, choice.Options.Count);
                shell.DrawOptionLine(i, ox + x, oy + y, w, h, choice.RawOptions[i], i == _choiceHover);
            }
        }
        else if (Picking) DrawPick(ox, oy);
        host.DrawToast();
    }

    /// <summary>가운데 640×480 틀 기준 네모 (x, y, w, h) 가 떠 있는 창 · 메뉴 · 턴 끝 알림에 덮이나.</summary>
    private bool TagCovered(int x, int y, int w, int h)
    {
        if (WindowOpen || host.SlotsScr.SlotsOpen || host.Sys._confirm != null) return true;   // 저장 칸 창 · 확인 창 위로 글이 비치지 않게
        bool Hit(int rx, int ry, int rw, int rh) => x < rx + rw && x + w > rx && y < ry + rh && y + h > ry;
        if (_mode == Mode.Menu && Hit(MenuX - 10, MenuY, MenuW + 20, 36 + MenuItems.Length * MenuRowH)) return true;
        return host._lastTime < _noticeUntil && Hit(200, 200, 240, 40);
    }

    /// <summary>짚음 테 빛깔 — 이동 고르기 고리 · 주점 얼굴의 짚음과 같은 노랑.</summary>
    private const uint HoverColor = 0xFFFFE080;

    /// <summary>챕터 304 [n] 이 숨긴 지도 그림(챕터, n) — 원본은 지도를 다시 세울 때까지 숨긴다(챕터 56 한 곳).</summary>
    private readonly HashSet<(int Chapter, int Index)> _hiddenPictures = [];

    /// <summary>이동 고르기 선 빛깔(0x100c4e00: 16비트 0x6259) · 다섯 번 긋는 어긋남.</summary>
    private const uint RouteColor = 0xFF6349CE;
    private static readonly (int, int)[] RouteOffsets = [(0, 0), (1, 0), (0, 1), (-1, 0), (0, -1)];

    /// <summary>한 점 굵기 선(브레젠험) — 지도 화면 안만.</summary>
    private void Line(int x0, int y0, int x1, int y1, uint color)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, err = dx + dy;
        var (ox, oy, vw, vh) = MapView();
        var fb = host._fb;
        int bw = host.BoardWidth, bh = host.BoardHeight;
        for (int guard = 0; guard < 8000; guard++)
        {
            if (x0 >= ox && x0 < ox + vw && y0 >= oy && y0 < oy + vh && (uint)x0 < (uint)bw && (uint)y0 < (uint)bh) fb[y0 * bw + x0] = color;
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>
    /// 챕터 100 덮개 · 103 바꾸기 — 바꾸는 중이면 나감 동안 옛 화면(덮개 또는 지도)을 검게, 들어옴 동안 새 화면을 드러낸다.
    /// 덮개 그림은 원본 화면 한 장이라 가운데 640×480 틀(<paramref name="ox"/>, <paramref name="oy"/>)에 두고, 지도 화면 나머지는 검게 · 페이드는 지도 화면 전체에.
    /// </summary>
    private void DrawCover((int X, int Y, int W, int H) view, int ox, int oy)
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
            host.FillRect(view.X, view.Y, view.W, view.H, 0xFF000000);
            for (int y = 0; y < Math.Min(H, c.H - sy); y++)
                for (int x = 0; x < Math.Min(W, c.W - sx); x++)
                    host.SetPixel(ox + x, oy + y, c.Px[(sy + y) * c.W + sx + x] | 0xFF000000);
        }
        if (fade > 0) host.FillRect(view.X, view.Y, view.W, view.H, (uint)(Math.Clamp(fade, 0, 1) * 255) << 24);
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
    private void EdgeScroll((int X, int Y, int W, int H) view)
    {
        double now = host._lastTime, dt = Math.Clamp(now - _lastScroll, 0, 0.1);
        _lastScroll = now;
        if (WindowOpen || _mode == Mode.Menu || Events.Talk != null || Picking) return;
        if (host._dragScroll is { Held: true }) return;   // 왼쪽 단추로 끄는 동안은 끌기만(BattleDragScroll)
        var (mx, my) = host._mouse;
        int x = mx - view.X, y = my - view.Y;
        if (x < 0 || y < 0 || x >= view.W || y >= view.H) return;
        int step = (int)Math.Round(600 * dt);
        int dx = x < 8 ? -step : x >= view.W - 8 ? step : 0, dy = y < 8 ? -step : y >= view.H - 8 ? step : 0;
        if (dx != 0 || dy != 0) Scroll(dx, dy);
    }
}
