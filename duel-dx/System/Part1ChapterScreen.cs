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
    internal static readonly string[] MenuItems = ["연대표", "일행 · 장비", "파트1 타이틀", "돌아가기"];

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
    /// 원본은 <c>obts\%04d.obt</c>(머리 낱말 0) 타일 지도를 그린다 — BGRs 는 같은 지도를 미리 그린 그림으로 보인다(13.1, 가설). 그 밖(0 · 13)은 지도 없음.
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
    };

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
        host.LeaveScene(() => shell.StartBattle(battle));
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
                host.LeaveScene(() => shell.StartBattle(target));
                return true;
            case 501:
                // 챕터 끝 — 장면 10(0x100c04b0 → 생성 0x100d2470 이 Eps\episode.elg 를 연다) = 연대표로. 끝낸 챕터는 연대표에 ✓.
                if (_chapter is { } done) host.Part1.FinishedChapters.Add(done.Id);
                host.Part1.PendingActions = [];
                host.Part1.Save(host.FlagSt._flags);   // 챕터가 끝났다 — 남은 행동 없이
                host.LeaveScene(() => { _open = false; shell.Timeline.Open(); });
                return true;
            case 500:
                GameOver();
                return true;
            case 503:
                int chapterId = _chapter?.Id ?? -1;
                _resume = true;
                host.LeaveScene(() => { _open = false; shell.Field.Open(target, () => Open(chapterId)); });
                return true;
        }
        return false;
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
        var (map, mw, mh) = MapOf(chapter) is > 0 and var id ? Image(id) : (null, 0, 0);
        var (sea, sw, sh) = Image(0);
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
        PrepareContinue();
        return Open(host.Part1.ChapterId);
    }

    /// <summary>진행에서 이어 갈 준비 — 남은 행동을 되살리고 그 챕터 · 일행 자리를 세운다(지도는 아직 안 연다). 전투 세이브를 불러올 때도 쓴다.</summary>
    internal void PrepareContinue()
    {
        _resume = host.Part1.PendingActions.Count > 0;
        Events.Restore(host.Part1.PendingActions);
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
        host.UiText($"{who} LV {pick.Level}", ox + PickX, oy + PickY - 24, 0xFFFFE080, 12);
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
    }

    private IReadOnlyList<Part1Ability>? _abilityList;
    private IReadOnlyList<Part1Attack>? _attackList;

    // ── 입력 ──

    internal void OnClick(int bx, int by)
    {
        if (Events.Talk != null) { if (!shell.NextTalkPage()) Events.Advance(); return; }   // 대사가 떠 있으면 누르면 다음 장 · 다음 대사
        if (Picking) { PressPick(PickAt(bx, by)); return; }
        if (_status is { _open: true }) { _status.OnClick(bx, by); return; }
        if (_exchange is { _open: true }) { _exchange.OnClick(bx, by); return; }
        int hit = HitAt(bx, by);
        if (_mode == Mode.Move && hit < 0) { _mode = Mode.Map; return; }   // 표지 밖을 누르면 취소(단추가 다시 보인다)
        if (_mode == Mode.Menu && hit < 0) { _mode = Mode.Map; return; }
        Press(hit);
    }

    /// <summary>오른 단추 — 떠 있는 창 · 이동 고르기를 닫는다(원본 메시지 0x3ec). 아무것도 없으면 Esc 메뉴.</summary>
    internal void OnRightClick()
    {
        if (Events.Talk != null || Picking) return;
        Back();
    }

    private void Back()
    {
        if (_status is { _open: true }) _status.Close();
        else if (_exchange is { _open: true }) _exchange.Close();
        else if (_mode != Mode.Map) _mode = Mode.Map;
        else _mode = Mode.Menu;
    }

    /// <summary>Esc · 오른 단추는 창을 닫고(없으면 메뉴), 화살표는 지도를 민다, Enter · Space 는 짚은 것을 누른다.</summary>
    internal void OnKey(int key)
    {
        if (Events.Talk != null) { if (key is Win32.VK_RETURN or Win32.VK_SPACE or Win32.VK_ESCAPE && !shell.NextTalkPage()) Events.Advance(); return; }
        if (Picking) { if (key is Win32.VK_RETURN or Win32.VK_SPACE) PressPick(Math.Max(0, _hover - PickHit)); return; }   // 고르기 창은 닫을 수 없다(짚은 것 없으면 첫 후보)
        if (key == Win32.VK_ESCAPE) { Back(); return; }
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
            case MenuHit + 2: _mode = Mode.Map; host.LeaveScene(() => _open = false); break;
            case MenuHit + 3: _mode = Mode.Map; break;
            case >= 0 when hit < chapter.Places.Count: GoTo(hit); break;
        }
    }

    /// <summary>일행 창(장비 · 탑승) — 상태 창의 얼굴을 누르면 그 사람으로(원본 인물 상세 창 0x100bb580 자리).</summary>
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
        if (Events.Arrive(_party)) return;
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
        var (ox, oy) = host.Mos.MosesOrigin();
        const int W = MosesScene.MosesW, H = MosesScene.MosesH;
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        EdgeScroll(ox, oy);
        Art.Clip = (ox, oy, W, H);

        // 지도 — 판에 줄 단위로 옮긴다(그림 층).
        var fb = host._fb;
        int bw = host.BoardWidth, bh = host.BoardHeight;
        int x0 = Math.Max(0, ox), x1 = Math.Min(bw, ox + W);
        if (_mapPx.Length > 0 && x1 > x0)
            for (int y = 0; y < H; y++)
            {
                int py = oy + y, my = _cam.Y + y;
                if ((uint)py >= bh || my >= _mapH) continue;
                Array.Copy(_mapPx, my * _mapW + _cam.X + (x0 - ox), fb, py * bw + x0, Math.Min(x1 - x0, _mapW - _cam.X - (x0 - ox)));
            }

        // 지도 글(층)은 창 · 메뉴 · 턴 끝 알림 밑으로 비치므로 그동안은 안 그린다.
        bool window = WindowOpen || _mode == Mode.Menu || host._lastTime < _noticeUntil;
        bool calm = Events.Talk == null && !Picking;
        int tick = (int)(host._lastTime * GameWindow.TicksPerSecond);

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

        // 이동 고르기 — 갈 수 있는 이웃마다 퍼지는 고리(Obs 182, 0x100c4cf0).
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
                    if (!window) Art.TextCentered($"{at[k] + 1} 팀", tx + 20, ty + 9, on ? GameWindow.White : 0xFFD0D8F0, 11);
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

        if (Events.Talk is { } talk) shell.DrawTalk(ox, oy, talk.Speaker, talk.Text, Events.TalkSlot, Events.Faces);
        else if (Picking) DrawPick(ox, oy);
        host.DrawToast();
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
