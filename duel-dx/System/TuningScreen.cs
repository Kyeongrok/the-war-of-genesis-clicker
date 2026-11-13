using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 모드 창 — 메뉴 막대의 「모드」를 누르면 뜬다. 원본과 달라지는 것 네 가지를 게임 안에서 고른다(사용자 요청 menu-7):
/// 동맹을 AI 가 움직임 · 상자 내용물 보기 · 전투 시작 시 소울 가득(체크 셋) · 일반 공격 소울 기여도(선택 상자).
/// </summary>
/// <remarks>
/// ATK = 바탕 × (SOUL + 10) ÷ 10 이라 SOUL 150 이면 일반 공격이 시작(SOUL 40, ×5.0)의 3.2배(×16.0)가 된다. 그래서 SOUL 을 아끼고
/// 일반 공격만 하는 쪽이 이득이었다. 기여도 p% 면 일반 공격을 셀 때 SOUL 을 <c>40 + (SOUL − 40) × p ÷ 100</c> 으로 본다 —
/// 시작 세기는 그대로, 거기서 벌어지는 몫만 줄인다. 어빌리티(어빌리티에 딸린 work)는 원본대로 둬서 SOUL 을 모아 쓰는 쪽이 나아진다.
/// 적의 일반 공격에도 똑같이 걸린다.
/// </remarks>
internal sealed unsafe class TuningScreen(GameWindow host)
{
    internal const int MenuTuning = 1150;

    /// <summary>고를 수 있는 기여도(%) — 첫째가 원본.</summary>
    internal static readonly int[] SoulWeightChoices = [100, 75, 50, 25, 0];

    internal int _soulWeight = SoulWeightChoices.Contains(UserSettings.Current.SoulWeight) ? UserSettings.Current.SoulWeight : 100;

    /// <summary>DUELDX_TUNING=&lt;탭&gt; 이면 모드 창을 그 탭으로 열어 둔 채 시작한다(화면 밖 시험용).</summary>
    internal bool _tuningOpen = Environment.GetEnvironmentVariable("DUELDX_TUNING") != null;

    /// <summary>선택 상자가 펼쳐져 있나.</summary>
    internal bool _tuningListOpen;

    // 높이 496 — 일반 탭에 「가려진 캐릭터 반투명」 줄(fa-84)이 늘어 464 에서는 아래 설명이 「Esc: 닫기」와 겹쳤다.
    internal const int TuningW = 600, TuningH = 496, TuningBoxX = 300, TuningBoxW = 180, TuningRowH = 24;

    /// <summary>소울 기여도 상자의 y(창 기준) — 일반 탭 체크 줄 바로 아래(줄이 늘면 따라 내려간다 — 고정 270 이던 때 일곱째 줄과 겹쳤다).</summary>
    internal int TuningBoxY => TuningCheckY + TuningChecks(TabGeneral).Length * TuningCheckH + 8;

    /// <summary>
    /// 파트1 에서 연 창인가 — 소울 기여도는 파트2 일반 공격(SOUL 배율)에만 걸려 파트1 에서는 안 보인다(사용자 요청 menu-120).
    /// </summary>
    private bool Part1Here => SlotsScreen.Part1Slots || (host._battleLoaded && host._scene.IsPart1);

    /// <summary>일반 탭에 소울 기여도 상자를 두나.</summary>
    internal bool ShowsSoulWeight => _tuningTab == TabGeneral && !Part1Here;

    /// <summary>파트1 에서 연 일반 탭이면 소울 기여도 자리에 「파트1 차례 목소리 크기」 단추 줄을 둔다(사용자 요청 menu-121 — 음량 창이 아니라 모드에서).</summary>
    internal bool ShowsVoiceVolume => _tuningTab == TabGeneral && Part1Here;

    /// <summary>파트1 차례 목소리 크기 %(기본 80) — S.E 크기에 곱한다(<see cref="BattleScene.PlayTurnCall"/>).</summary>
    internal int _voiceVolume = Math.Clamp(UserSettings.Current.Part1VoiceVolume, 0, 100);

    /// <summary>
    /// 파트1 대사 목소리 크기 %(기본 100) — 필드 대사(206~208)의 성우 목소리. 원본처럼 S.E 와 따로다(VoiceVol, <see cref="GameWindow.Part1TalkVoiceGain"/>).
    /// 버그 리포트 #60 — 전에는 S.E 막대에 묶여 S.E 를 낮추면 목소리가 배경음 아래로 묻혔다.
    /// </summary>
    internal int _talkVoiceVolume = Math.Clamp(UserSettings.Current.Part1TalkVoiceVolume, 0, 100);

    /// <summary>「파트1 대사 목소리 크기」 줄 — 차례 목소리 줄에서 이만큼 아래.</summary>
    internal const int TalkVoiceRowDy = 56;

    internal static readonly int[] VoiceChoices = [0, 20, 40, 60, 80, 100];
    internal const int VoiceW = 44, VoiceGap = 4;

    /// <summary>목소리 크기 단추 i 의 자리(판 좌표).</summary>
    internal (int X, int Y, int W, int H) VoiceButton(int i)
    {
        var (x, y) = TuningOrigin();
        return (x + TuningBoxX + i * (VoiceW + VoiceGap), y + TuningBoxY - _scroll, VoiceW, TuningRowH);   // 굴린 자리(menu-131)
    }

    /// <summary>대사 목소리 크기 단추 i 의 자리(판 좌표) — 차례 목소리 단추 줄 바로 아래.</summary>
    internal (int X, int Y, int W, int H) TalkVoiceButton(int i)
    {
        var (bx, by, bw, bh) = VoiceButton(i);
        return (bx, by + TalkVoiceRowDy, bw, bh);
    }

    /// <summary>탭 — 0 일반(전투 규칙) · 1 밸런스(어빌리티 개편, 사용자 요청 menu-64) · 2 스토리(대사 진행) · 3 편의성 · 4 실험 · 5 엔딩 영상 · 6 가독성 향상. 제목줄 바로 아래 한 줄(사용자 요청).</summary>
    internal int _tuningTab = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_TUNING"), out int startTab) ? Math.Clamp(startTab, 0, TabNames.Length - 1) : 0;
    // 탭 너비 80 — 일곱째 탭(가독성 향상)이 늘어 92 로는 창(600) 오른쪽을 넘었다(끝 탭 오른쪽 12 + 6 × 84 + 80 = 596).
    internal const int TabY = 30, TabH = 24, TabW = 80;
    internal static readonly string[] TabNames = ["일반", "밸런스", "스토리", "편의성", "실험", "엔딩 영상", "가독성 향상"];
    internal const int TabGeneral = 0, TabBalance = 1, TabStory = 2, TabComfort = 3, TabLab = 4, TabMovie = 5, TabReadable = 6;

    /// <summary>스토리 탭 — 「대사 사이 멈춤」 고르기 단추들의 자리(창 기준).</summary>
    internal const int PauseY = 100, PauseW = 90, PauseH = 26, PauseGap = 4, StoryCheckY = 150;

    /// <summary>체크 줄 넷 — 창 위에서부터 y 70 · 102 · 134 · 166(탭 줄 아래). 누르면 그 메뉴 명령을 그대로 돌린다(알림·저장까지).</summary>
    internal const int TuningCheckY = 70, TuningCheckH = 32;

    // ── 탭 내용 스크롤(사용자 요청 menu-131) ──
    // 편의성 탭이 13줄이 되어 마지막 줄이 「닫기」 · 「Esc: 닫기」와 겹쳤다. 창을 더 키우면 640×480 판을 넘으므로 내용만 굴린다.
    // 내용 칸은 탭 줄 아래(ViewTop) ~ 닫기 단추 위(ViewBottom). 그리개에 자르기가 없어 칸을 조금이라도 벗어난 줄 · 단추는 통째로 안 그리고 안 누른다.
    // 굴림은 한 줄(TuningCheckH) 단위라 체크 줄이 늘 같은 자리에 선다. 다 들어가는 탭은 굴림 0 · 막대 없음 — 전과 똑같다.

    /// <summary>내용 칸의 위 · 아래(창 기준) — 탭 줄 밑 선(54) 바로 아래부터 닫기 단추(TuningH − 40) 6 위까지.</summary>
    internal const int ViewTop = TabY + TabH + 2, ViewBottom = TuningH - 46;

    /// <summary>스크롤 막대 — 창 오른쪽 안(창 기준 x)과 굵기, 누름 판정은 양옆으로 넓힌다.</summary>
    internal const int BarX = TuningW - 10, BarW = 6, BarHitPad = 4;

    /// <summary>지금 굴린 만큼(px, 한 줄 단위) — 탭이 바뀌면 0.</summary>
    internal int _scroll;
    private int _scrollTab = -1;

    /// <summary>막대 손잡이를 끄는 중이면 손잡이 위에서 잡은 자리(px).</summary>
    private int? _barGrab;

    /// <summary>방향키로 고른 체크 줄(−1 없음) — Enter · Space 가 누른다.</summary>
    internal int _keyRow = -1;

    /// <summary>지금 탭의 체크 줄이 시작하는 y(창 기준).</summary>
    private int ChecksTop => _tuningTab == TabStory ? StoryCheckY : TuningCheckY;

    /// <summary>지금 탭 내용의 맨 아래(창 기준, 굴리기 전) — 탭마다 그리는 것(DrawTuning)과 같은 셈.</summary>
    internal int ContentBottom()
    {
        int rows = TuningChecks().Length;
        int checksEnd = ChecksTop + rows * TuningCheckH - 4;   // 마지막 줄의 강조 칸 바닥
        return _tuningTab switch
        {
            TabBalance => checksEnd + 4 + 8 + 3 * 22 + 16,                                // 어빌리티 개편 설명 넉 줄
            TabMovie => checksEnd + 4 + 8 + Math.Max(0, _movieLines.Length - 1) * 22 + 18,   // 파트1 엔딩 영상 글
            TabGeneral when ShowsSoulWeight => TuningBoxY + 32 + 3 * 21 + 16,             // 소울 기여도 설명 넉 줄
            TabGeneral when ShowsVoiceVolume => TuningBoxY + TalkVoiceRowDy + 32 + 16,   // 대사 목소리 설명
            _ => checksEnd,
        };
    }

    /// <summary>굴릴 수 있는 끝(px) — 한 줄 단위로 올려 맞춘다.</summary>
    internal int MaxScroll => (Math.Max(0, ContentBottom() - ViewBottom) + TuningCheckH - 1) / TuningCheckH * TuningCheckH;

    /// <summary>탭이 바뀌었으면 굴림 · 고른 줄을 처음으로, 내용이 줄었으면 굴림을 끝 안으로.</summary>
    private void SyncScroll()
    {
        if (_scrollTab != _tuningTab) { _scrollTab = _tuningTab; _scroll = 0; _keyRow = -1; _barGrab = null; }
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }

    /// <summary>내용을 <paramref name="rows"/> 줄 굴린다(+ 아래로). 굴렸으면 true.</summary>
    internal bool ScrollRows(int rows)
    {
        SyncScroll();
        int was = _scroll;
        _scroll = Math.Clamp(_scroll + rows * TuningCheckH, 0, MaxScroll);
        return _scroll != was;
    }

    /// <summary>한 쪽(위 −1 · 아래 +1)으로 더 굴릴 수 있나.</summary>
    internal bool CanScroll(int dy) { SyncScroll(); return dy < 0 ? _scroll > 0 : _scroll < MaxScroll; }

    /// <summary>휠 — 한 눈금에 한 줄. 펼친 선택 상자 위에서는 안 굴린다.</summary>
    internal void OnTuningWheel(int notches) { if (!_tuningListOpen) ScrollRows(-notches); }

    /// <summary>창 기준 y 의 칸(높이 h)이 굴린 뒤에 내용 칸 안에 통째로 드나 — <paramref name="top"/> 은 굴리기 전 창 기준.</summary>
    internal bool Shown(int top, int h) => top - _scroll >= ViewTop && top - _scroll + h <= ViewBottom;

    /// <summary>한 쪽(창 기준) 페이지 — 내용 칸에 드는 줄 수에서 한 줄 덜.</summary>
    private int PageRows => Math.Max(1, (ViewBottom - ViewTop) / TuningCheckH - 1);

    /// <summary>체크 줄 i 가 보이게 굴린다(방향키 · 패드).</summary>
    private void ShowRow(int i)
    {
        int top = ChecksTop + i * TuningCheckH;
        if (top - _scroll < ViewTop) _scroll = Math.Max(0, (top - ViewTop) / TuningCheckH * TuningCheckH);
        int over = top + TuningCheckH - 4 - ViewBottom;
        if (over - _scroll > 0) _scroll = (over + TuningCheckH - 1) / TuningCheckH * TuningCheckH;
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }

    /// <summary>스크롤 막대 칸(판 좌표)과 손잡이 — 굴릴 것이 없으면 null.</summary>
    private (int X, int TrackY, int TrackH, int ThumbY, int ThumbH)? Bar()
    {
        int max = MaxScroll;
        if (max <= 0) return null;
        var (x, y) = TuningOrigin();
        int trackY = y + ViewTop, trackH = ViewBottom - ViewTop;
        int thumbH = Math.Max(24, trackH * trackH / (trackH + max));
        return (x + BarX, trackY, trackH, trackY + (trackH - thumbH) * _scroll / max, thumbH);
    }

    /// <summary>막대를 누른 것이면 손잡이는 끌기 시작, 빈 칸은 그 쪽으로 한 쪽 굴리기.</summary>
    private bool OnBarClick(int bx, int by)
    {
        if (Bar() is not { } bar) return false;
        var (barX, trackY, trackH, thumbY, thumbH) = bar;
        if (bx < barX - BarHitPad || bx >= barX + BarW + BarHitPad || by < trackY || by >= trackY + trackH) return false;
        if (by >= thumbY && by < thumbY + thumbH) _barGrab = by - thumbY;
        else ScrollRows(by < thumbY ? -PageRows : PageRows);
        return true;
    }

    /// <summary>마우스가 움직였다 — 손잡이를 끄는 중이면 따라 굴린다(왼쪽 단추를 뗐으면 끝).</summary>
    internal void OnTuningMouseMove(int by, bool leftHeld)
    {
        if (_barGrab is not { } grab) return;
        if (!leftHeld || !_tuningOpen || Bar() is not { } bar) { _barGrab = null; return; }
        var (_, trackY, trackH, _, thumbH) = bar;
        int max = MaxScroll, room = Math.Max(1, trackH - thumbH);
        int px = Math.Clamp((by - grab - trackY) * max / room, 0, max);
        _scroll = (px + TuningCheckH / 2) / TuningCheckH * TuningCheckH;
        _scroll = Math.Clamp(_scroll, 0, max);
    }

    /// <summary>왼쪽 단추를 뗐다 — 끌기 끝.</summary>
    internal void OnTuningMouseUp() => _barGrab = null;

    /// <summary>지금 탭의 체크 줄 — 일반(전투 규칙 둘)과 편의성(보기·진행 편의 넷, 사용자 요청).</summary>
    internal (string Label, string Note, int Command, bool On)[] TuningChecks() => TuningChecks(_tuningTab);

    /// <summary>
    /// 켜 둔 모드 — 모든 탭의 체크 줄 가운데 켠 것의 이름과, 원본이 아닌 고르기(소울 기여도 · 대사 사이 멈춤). 전투 통계가 싣는다(사용자 요청 menu-112).
    /// 「익명 전투 통계 보내기」는 보내는 판이면 늘 켜져 있어 뺀다.
    /// </summary>
    internal List<string> EnabledMods()
    {
        var on = new[] { TabGeneral, TabBalance, TabStory, TabComfort, TabLab, TabMovie, TabReadable }.SelectMany(TuningChecks)
            .Where(c => c.On && c.Command is not (MenuSendStats or MenuMosesWarpMovie)).Select(c => c.Label).ToList();
        if (!host._mosesWarpMovie) on.Add("모세스 워프 영상 끔");   // 기본 켬(원본대로) — 끈 것이 원본과 다르다
        if (_soulWeight != 100) on.Add($"일반 공격 소울 기여도 {SoulWeightLabel(_soulWeight)}");
        if (host._talkPauseSeconds >= 0) on.Add($"대사 사이 멈춤 {GameWindow.TalkPauseLabel(host._talkPauseSeconds)}");
        return on;
    }

    internal (string Label, string Note, int Command, bool On)[] TuningChecks(int tab) => tab == TabStory ?
    [
        // 스토리 탭 — 대사 사이 멈춤 아래(StoryCheckY). 일반 탭에 두면 소울 기여도 상자와 겹쳤다.
        // 파트1 세이브 이스터에그(원본은 파트1 세이브를 보고 켠다 — MosesEasterEggs.cs). 모세스를 열 때 깃발을 세운다.
        ("카오스 큐브 · 나야트레이", "샤이닝 스타 메일 → 블랙 레이븐 다리엘에서 합류", MenuEggChaosCube, host._eggChaosCube),
        ("알 수 없는 지도", "의혹 — 파치나의 동굴 전투가 열린다", MenuEggUnknownMap, host._eggUnknownMap),
        ("알 수 없는 씨앗", "의혹 — 빅 도그마 메일 · 건슬라이서 맘모스", MenuEggUnknownSeed, host._eggUnknownSeed),
    ] : tab == TabMovie ?
    [
        // 엔딩 영상 탭 — 파트2 오프닝 켜기와, 파트1 엔딩 영상이 있는지(아래 글, DrawMovieTab).
        ("파트2 오프닝 영상", CutscenePlayer.FindOriginal(TitleScreen.OpeningMovie) != null ? "게임을 켤 때 타이틀 앞에 원본 오프닝(원본 폴더에서 바로 튼다)" : "원본 폴더에 Mov 0008 이 없어 나오지 않는다", MenuOpeningMovie, host._openingMovie),
        // 모세스 워프 영상 — 원본은 항성계를 옮길 때마다 떠나는 · 오는 영상을 튼다(버그 리포트 #70). 기본 켬, 끄면 영상 없이 넘어간다.
        ("모세스 워프 영상", CutscenePlayer.FindOriginal(MosesScene.WarpLeaveMovies[0]) != null ? "항성계를 옮길 때 원본의 워프 영상을 튼다" : "원본 폴더에 워프 영상(Mov 0016~0031)이 없다", MenuMosesWarpMovie, host._mosesWarpMovie),
    ] : tab == TabReadable ?
    [
        // 가독성 향상 탭 — 흰 눈밭 판(파트1 Btl 20031 빙원 따위)에서 범위 칸 · 얼음 덩이가 잘 안 보인다는 요청. 셋 다 원본에 없다 — 범위 테두리만 기본 끔(어색하다는 말에 물들이기로 바꿈).
        ("밝은 칸 범위 진하게", "흰 바탕에 묻히는 범위 칸을 물들인다(원본에 없음)", MenuBrightCellFill, host._brightCellFill),
        ("범위 테두리", "이동 · 공격 범위 바깥에 선을 두른다(원본에 없음)", MenuRangeEdges, host._rangeEdges),
        ("얼음 · 땅불 칸 표시", "파트1 얼음 덩이 · 땅불 칸을 물들인다(원본에 없음)", MenuPart1FieldMarks, host._part1FieldMarks),
        // 온도 표시 — 빙원 판에서 캐릭터가 얼어붙는데 기온 · 몸 온도가 안 보인다는 요청. 원본에 없고 기본 켬.
        ("온도 표시", "파트1 기온 · 몸 온도 · 깎인 SPD(원본에 없음)", MenuTemperatureDisplay, host._temperatureDisplay),
    ] : tab == TabLab ?
    [
        // 실험 탭 — 아직 다듬는 중인 것. 기본은 모두 끔.
        ("새 UI (또렷한 글자)", "설정 창을 화면 해상도로 그린다 · 끄면 이전 UI", MenuHiResUi, host._hiResUi),
        ("고해상도 모세스", host.HiBack.StatusLine, MenuHiResMoses, host._hiResMoses),
    ] : tab == TabComfort ?
    [
        ("조작 안내 글 보이기", "화면 아래에 지금 할 수 있는 조작을 알려 준다", MenuHints, host._showHints),
        ("체력바 보이기", "유닛 머리 위의 HP·TP 막대", MenuGauges, host._showGauges),
        ("차례 회복 숫자 보이기", "파트1 장비로 차례마다 찬 HP·소울(원본에 없음)", MenuTurnRegenNumbers, host._turnRegenNumbers),
        ("레벨업 창 보이기", "레벨이 오를 때 능력치 창을 띄운다", MenuLevelUpWindow, host.Btl._showLevelUp),
        ("상자 내용물 보기", "전투 화면 왼쪽 위에 상자에 든 것을 보인다", MenuChestContents, host._showChestContents),
        ("상자 강조", "아직 안 연 상자에 깜박이는 노란 테두리를 두른다", MenuChestGlow, host._highlightChests),
        ("행성에 할 일 표시", "전투·이야기 장소가 있는 행성에 노란 점", MenuPlanetMarks, host._planetMarks),
        ("파트1 지도 길목 표시", "사건이 걸린 표지 없는 길목에 이름과 느낌표(원본에 없음)", MenuPart1RouteMarks, host._part1RouteMarks),
        ("적 행동 중 클릭으로 건너뛰기", "모션·이펙트를 건너뛰고 결과만 보인다", MenuSkipEnemy, host._skipEnemyAction),
        ("기술 영상 보기", "헬 카이트·진무 천지파열 같은 기술의 영상(원본 폴더에서 튼다)", MenuSkillMovies, host._skillMovies),
        ("전직할 때 EXP 없애기", "원본처럼 전직하면 쌓인 EXP 를 지운다 · 끄면 남긴다", MenuKeepJobExp, host._dropJobExp),
        ("대사 중 우클릭으로 건너뛰기", "끄면 우클릭이 장면을 넘기지 않는다 · Esc 는 그대로", MenuRightClickSkipsTalk, host._rightClickSkipsTalk),
        ("파트1 대사는 눌러서 넘기기", "켜면 저절로 넘어가지 않고 누를 때까지 기다린다", MenuPart1TalkWaits, host._part1TalkWaits),
        ("대사 첫 클릭은 글 채우기", "끄면 원본처럼 첫 클릭에 바로 넘어간다", MenuTalkClickFills, host._talkClickFills),
        ("익명 전투 통계 보내기", "쓴 어빌리티 · 피해 · 조작 방식 — 이름·세이브는 안 보낸다", MenuSendStats, host._sendStats),
    ] : tab == TabBalance ?
    [
        // 밸런스 탭 — 전투의 세기를 바꾸는 것: 타이밍 어택 · 타이밍 명중(사용자 요청 menu-65)과 어빌리티 · 체질 자료를 고쳐 쓰는 개편(menu-64).
        // 기본은 모두 끔. 어빌리티 개편은 맨 아래 — 그 밑에 개편에 든 것을 적는다.
        ("타이밍 어택", "박자에 맞춰 Space(패드 LT → RT)", MenuTimedHit, host._timedHit),
        ("타이밍 명중", $"어빌리티 앞의 띠 — 명중률 하나 +{TimedHit.AimBonusFor(1)}% · 둘 +{TimedHit.AimBonusFor(2)}%", MenuTimedAim, host._timedAim),
        ("어빌리티 개편", "켜면 아래 것들이 원본과 달라진다", MenuTachyrionRework, host._tachyrionRework),
    ] :
    [
        ("동맹을 AI 가 움직임", "끄면 동맹(편 3)도 내가 움직인다", MenuAllyAi, host._allyAi),
        ("전투 시작 시 소울 가득", "다음 전투부터 내 편 소울을 가득 채워 시작한다", MenuFullSoul, host._fullSoulAtStart),
        ("안 움직이는 먼 적은 안 비춤", "화면 밖 적은 걷거나 기술을 쓸 때만 카메라가 간다", MenuQuietFoeCamera, host._quietFoeCamera),
        ("파트1 차례 목소리", "파트1 인물 차례에 성우 한 마디(원본에 없음)", MenuPart1TurnVoice, host._part1TurnVoice),
        ("파트1 차례 테두리", "차례인 유닛에 주황·노랑 반짝이는 테두리", MenuPart1TurnOutline, host._part1TurnOutline),
        ("가려진 캐릭터 반투명", "파트1 기둥·벽 뒤에 선 유닛을 반투명으로 비춘다", MenuCoverGhost, host._coverGhost),
        ("튜토리얼에서도 저장", "파트1 튜토리얼 전투에서도 SAVE(원본은 막는다)", MenuPart1TutorialSave, host._part1TutorialSave),
        ("무작위 상자는 시작 때", "파트1 무작위 상자를 전투 시작에 정함(끄면 열 때)", MenuPart1ChestRoll, host._part1ChestRollAtStart),
    ];

    internal static string SoulWeightLabel(int p) => p switch { 100 => "100% (원본)", 0 => "0% (소울 무관)", _ => $"{p}%" };

    /// <summary>일반 공격을 셀 때 쓰는 SOUL — 시작값에서 벌어진 몫만 기여도만큼.</summary>
    internal int BasicAttackSoul(int soul) =>
        host._db is not { } db || SoulWeightNow == 100 ? soul : Math.Max(0, db.SoulStart + (soul - db.SoulStart) * SoulWeightNow / 100);

    /// <summary>어빌리티 개편을 켜면 일반 공격의 소울 몫이 지금의 80% 가 된다(사용자 요청 fg-33) — 고른 기여도에 곱한다.</summary>
    internal const int ReworkSoulPercent = 80;

    /// <summary>지금 걸리는 기여도(%) — 고른 값 × 어빌리티 개편의 몫.</summary>
    internal int SoulWeightNow => host._tachyrionRework ? _soulWeight * ReworkSoulPercent / 100 : _soulWeight;

    /// <summary>그 work 로 칠 때 쓰는 SOUL — 어빌리티에 안 딸린 work(일반 공격·몬스터 기본기)만 기여도를 건다.</summary>
    internal int AttackSoul(WorkData w, int soul) => w.AbilityId == 0 ? BasicAttackSoul(soul) : soul;

    internal (int X, int Y) TuningOrigin() => (host._camX + (host.ViewWidth - TuningW) / 2, host._camY + (host.ViewHeight - TuningH) / 2);

    /// <summary>조정 창이 열려 있으면 클릭을 먹는다.</summary>
    internal bool OnTuningClick(int bx, int by)
    {
        if (!_tuningOpen) return false;
        SyncScroll();
        var (x, y0) = TuningOrigin();
        // 탭 내용은 굴린 자리로 판정한다(menu-131) — y 는 굴린 내용의 원점, y0 는 창 자리(탭 · 닫기 · 막대).
        int y = y0 - _scroll;
        int boxX = x + TuningBoxX, boxY = y + TuningBoxY;
        bool inView = by >= y0 + ViewTop && by < y0 + ViewBottom;
        // 닫기 단추와 탭은 어느 탭에서나.
        if (!_tuningListOpen)
        {
            if (bx >= x + TuningW - 116 && bx < x + TuningW - 16 && by >= y0 + TuningH - 40 && by < y0 + TuningH - 12) { _tuningOpen = false; return true; }
            for (int t = 0; t < TabNames.Length; t++)
                if (bx >= x + 12 + t * (TabW + 4) && bx < x + 12 + t * (TabW + 4) + TabW && by >= y0 + TabY && by < y0 + TabY + TabH) { _tuningTab = t; SyncScroll(); return true; }
            if (OnBarClick(bx, by)) return true;
        }
        if (_tuningTab == TabStory)
        {
            _tuningListOpen = false;
            if (!inView) return true;
            for (int i = 0; i < TalkPauseChoices.Length; i++)
            {
                int px = x + 16 + i * (PauseW + PauseGap);
                if (Shown(PauseY, PauseH) && bx >= px && bx < px + PauseW && by >= y + PauseY && by < y + PauseY + PauseH) { host.OnMenuCommand(MenuTalkPauseBase + i); return true; }
            }
            PressCheck(x, y, bx, by);
            return true;
        }
        if (_tuningTab != TabGeneral)
        {
            // 밸런스 · 편의성 · 실험 탭 — 체크 줄뿐이다.
            _tuningListOpen = false;
            if (inView) PressCheck(x, y, bx, by);
            return true;
        }
        if (_tuningListOpen && ShowsSoulWeight)
        {
            // 펼친 목록은 내용 칸 밖(닫기 단추 위)까지 덮어 그리므로 칸 검사 없이 받는다.
            int row = (by - boxY - TuningRowH) / TuningRowH;
            if (bx >= boxX && bx < boxX + TuningBoxW && by >= boxY + TuningRowH && row >= 0 && row < SoulWeightChoices.Length)
            {
                _soulWeight = SoulWeightChoices[row];
                host.SaveSettings();
                host.Toast($"일반 공격 소울 기여도: {SoulWeightLabel(_soulWeight)}");
            }
            _tuningListOpen = false;
            return true;
        }
        _tuningListOpen = false;
        if (!inView) return true;
        if (ShowsVoiceVolume)
            for (int i = 0; i < VoiceChoices.Length; i++)
                if (VoiceButton(i) is var (vx, vy, vw, vh) && Shown(TuningBoxY, vh) && bx >= vx && bx < vx + vw && by >= vy && by < vy + vh)
                {
                    _voiceVolume = VoiceChoices[i];
                    host.SaveSettings();
                    return true;
                }
                else if (TalkVoiceButton(i) is var (tx, ty, tw, th) && Shown(TuningBoxY + TalkVoiceRowDy, th) && bx >= tx && bx < tx + tw && by >= ty && by < ty + th)
                {
                    _talkVoiceVolume = VoiceChoices[i];
                    host.SaveSettings();
                    return true;
                }
        if (ShowsSoulWeight && Shown(TuningBoxY, TuningRowH) && bx >= boxX && bx < boxX + TuningBoxW && by >= boxY && by < boxY + TuningRowH) { _tuningListOpen = true; return true; }
        PressCheck(x, y, bx, by);
        return true;
    }

    /// <summary>체크 줄을 누른다 — <paramref name="y"/> 는 굴린 내용의 원점. 다 보이는 줄만 받는다.</summary>
    private void PressCheck(int x, int y, int bx, int by)
    {
        var rows = TuningChecks();
        int top = ChecksTop;
        int at = by >= y + top ? (by - y - top) / TuningCheckH : -1;
        if (at < 0 || at >= rows.Length || bx < x + 12 || bx >= x + TuningW - 12 || !Shown(top + at * TuningCheckH, TuningCheckH - 4)) return;
        _keyRow = at;
        host.OnMenuCommand(rows[at].Command);
    }

    /// <summary>
    /// 키 — Esc 닫기, ↑↓ 체크 줄 고르기(내용이 따라 굴러간다), Enter · Space 누르기, ←→ 탭, PgUp · PgDn 한 쪽, Home · End 처음 · 끝(menu-131).
    /// </summary>
    internal void OnTuningKey(int key)
    {
        if (key == Win32.VK_ESCAPE)
        {
            if (_tuningListOpen) _tuningListOpen = false; else _tuningOpen = false;
            return;
        }
        if (_tuningListOpen) return;
        SyncScroll();
        var rows = TuningChecks();
        switch (key)
        {
            case Win32.VK_UP or Win32.VK_DOWN when rows.Length > 0:
                int d = key == Win32.VK_DOWN ? 1 : -1;
                _keyRow = _keyRow < 0 ? (d > 0 ? FirstShownRow(rows.Length) : LastShownRow(rows.Length)) : Math.Clamp(_keyRow + d, 0, rows.Length - 1);
                ShowRow(_keyRow);
                break;
            case Win32.VK_UP or Win32.VK_DOWN:
                ScrollRows(key == Win32.VK_DOWN ? 1 : -1);
                break;
            case Win32.VK_RETURN or Win32.VK_SPACE when _keyRow >= 0 && _keyRow < rows.Length:
                ShowRow(_keyRow);
                host.OnMenuCommand(rows[_keyRow].Command);
                break;
            case Win32.VK_LEFT or Win32.VK_RIGHT:
                _tuningTab = (_tuningTab + (key == Win32.VK_RIGHT ? 1 : TabNames.Length - 1)) % TabNames.Length;
                SyncScroll();
                break;
            case Win32.VK_PRIOR or Win32.VK_NEXT:
                ScrollRows(key == Win32.VK_NEXT ? PageRows : -PageRows);
                if (_keyRow >= 0) _keyRow = key == Win32.VK_NEXT ? Math.Max(_keyRow, FirstShownRow(rows.Length)) : Math.Min(_keyRow, LastShownRow(rows.Length));
                break;
            case Win32.VK_HOME or Win32.VK_END:
                _scroll = key == Win32.VK_HOME ? 0 : MaxScroll;
                break;
        }
    }

    /// <summary>지금 다 보이는 첫 · 마지막 체크 줄.</summary>
    private int FirstShownRow(int n) { for (int i = 0; i < n; i++) if (Shown(ChecksTop + i * TuningCheckH, TuningCheckH - 4)) return i; return 0; }
    private int LastShownRow(int n) { for (int i = n - 1; i >= 0; i--) if (Shown(ChecksTop + i * TuningCheckH, TuningCheckH - 4)) return i; return n - 1; }

    internal void DrawTuning()
    {
        if (!_tuningOpen || host._db is not { } db) return;
        SyncScroll();
        var (x, y0) = TuningOrigin();
        host.UiFill(x - 4, y0 - 4, TuningW + 8, TuningH + 8, 0x80000000);
        host.UiFill(x, y0, TuningW, TuningH, StatusScreen.PanelBg);
        host.UiStroke(x, y0, TuningW, TuningH, StatusScreen.BoxLine);
        host.UiFill(x, y0, TuningW, 28, StatusScreen.HeadBg);
        host.UiText("모드 — 원본과 달라지는 것", x + 10, y0 + 6, White);
        for (int t = 0; t < TabNames.Length; t++)
        {
            int tx = x + 12 + t * (TabW + 4);
            bool on = t == _tuningTab;
            host.UiFill(tx, y0 + TabY, TabW, TabH, on ? StatusScreen.HeadBg : StatusScreen.BoxBg);
            host.UiStroke(tx, y0 + TabY, TabW, TabH, StatusScreen.BoxLine);
            host.UiText(TabNames[t], tx + (TabW - host.UiGetText(TabNames[t], White).W) / 2, y0 + TabY + 4, on ? 0xFF00FFFF : White);
        }
        host.UiFill(x + 12, y0 + TabY + TabH, TuningW - 24, 1, StatusScreen.BoxLine);
        DrawBar();
        // 아래부터 탭 내용 — y 는 굴린 내용의 원점(menu-131). 내용 칸(ViewTop ~ ViewBottom)을 벗어난 것은 Shown 이 거른다.
        int y = y0 - _scroll;
        if (_tuningTab == TabStory) { if (Shown(PauseY - 28, PauseH + 28)) DrawStoryTab(x, y); DrawChecks(x, y, StoryCheckY, TuningChecks()); DrawTuningFoot(x, y0); return; }

        var checks = TuningChecks();
        DrawChecks(x, y, TuningCheckY, checks);
        int below = TuningCheckY + checks.Length * TuningCheckH + 8;   // 체크 줄 아래(창 기준, 굴리기 전)

        if (_tuningTab == TabBalance)
        {
            // 어빌리티 개편에 든 것 — 체크 줄 아래에 한 줄씩.
            string[] rework =
            [
                "· 타키리온 체질 — 소울 블레스트가 범위 SOUL 흡수기로, 락킹 필드 · 배리어 · 리콜이 바뀐다",
                "· 연 — SOUL 20 이 있으면 쓸 수 있고, 쓰면 그 20 이 빠진다 (원본: 70 필요 · 안 빠짐)",
                "· 엘레맨탈 파이어 — 필요 TP 가 20% 줄어든다 (레벨 1: 160 → 128 · 레벨 9 부터: 200 → 160)",
                $"· 일반 공격 — SOUL 로 늘어나는 공격력이 {ReworkSoulPercent}% 가 된다 (일반 탭의 소울 기여도에 곱한다)",
            ];
            for (int i = 0; i < rework.Length; i++)
                if (Shown(below + i * 22, 16)) host.UiText(rework[i], x + 44, y + below + i * 22, DimGray, 12);
        }
        if (_tuningTab == TabMovie) DrawMovieTab(x, y, below);
        if (ShowsVoiceVolume)
        {
            int vy = y + TuningBoxY;
            if (Shown(TuningBoxY, TuningRowH))
            {
                host.UiText("파트1 차례 목소리 크기", x + 16, vy + 4, White);
                for (int i = 0; i < VoiceChoices.Length; i++)
                {
                    var (bx, by, bw, bh) = VoiceButton(i);
                    bool on = VoiceChoices[i] == _voiceVolume;
                    host.UiFill(bx, by, bw, bh - 2, on ? 0xFF2A4A8A : MouseInBoard(bx, by, bw, bh) ? 0x402A4A8A : StatusScreen.BoxBg);
                    host.UiStroke(bx, by, bw, bh - 2, StatusScreen.BoxLine);
                    string label = $"{VoiceChoices[i]}";
                    host.UiText(label, bx + (bw - host.UiGetText(label, White).W) / 2, by + 4, on ? 0xFF00FFFF : White);
                }
            }
            if (Shown(TuningBoxY + 32, 16)) host.UiText("내 편 인물 차례에 나오는 성우 목소리 크기(%) — 효과음 크기에 곱한다", x + 16, vy + 32, DimGray, 12);
            int ty = vy + TalkVoiceRowDy;
            if (Shown(TuningBoxY + TalkVoiceRowDy, TuningRowH))
            {
                host.UiText("파트1 대사 목소리 크기", x + 16, ty + 4, White);
                for (int i = 0; i < VoiceChoices.Length; i++)
                {
                    var (bx, by, bw, bh) = TalkVoiceButton(i);
                    bool on = VoiceChoices[i] == _talkVoiceVolume;
                    host.UiFill(bx, by, bw, bh - 2, on ? 0xFF2A4A8A : MouseInBoard(bx, by, bw, bh) ? 0x402A4A8A : StatusScreen.BoxBg);
                    host.UiStroke(bx, by, bw, bh - 2, StatusScreen.BoxLine);
                    string label = $"{VoiceChoices[i]}";
                    host.UiText(label, bx + (bw - host.UiGetText(label, White).W) / 2, by + 4, on ? 0xFF00FFFF : White);
                }
            }
            if (Shown(TuningBoxY + TalkVoiceRowDy + 32, 16)) host.UiText("이야기 장면 대사의 성우 목소리 크기(%) — 원본처럼 효과음 크기와 따로 정한다", x + 16, ty + 32, DimGray, 12);
        }
        if (!ShowsSoulWeight) { DrawTuningFoot(x, y0); return; }
        int boxX = x + TuningBoxX, boxY = y + TuningBoxY;
        bool boxShown = Shown(TuningBoxY, TuningRowH);
        if (boxShown)
        {
            host.UiText("일반 공격 소울 기여도", x + 16, boxY + 4, White);
            host.UiFill(boxX, boxY, TuningBoxW, TuningRowH - 2, StatusScreen.BoxBg);
            host.UiStroke(boxX, boxY, TuningBoxW, TuningRowH - 2, StatusScreen.BoxLine);
            host.UiText(SoulWeightLabel(_soulWeight), boxX + 8, boxY + 4, White);
            host.UiText("▼", boxX + TuningBoxW - 18, boxY + 4, DimGray);
        }

        // 설명 — 지금 값으로 SOUL 0·40·150 일 때 일반 공격 배율이 어떻게 되는지.
        double Factor(int soul) => db.N(85) == 0 ? 0 : (double)(BasicAttackSoul(soul) + db.N(2)) * db.N(42) / db.N(85);
        double Original(int soul) => db.N(85) == 0 ? 0 : (double)(soul + db.N(2)) * db.N(42) / db.N(85);
        string[] lines =
        [
            $"SOUL {db.SoulStart}(전투 시작)을 기준으로, 그보다 많거나 적은 몫만 이 비율만큼 친다.",
            $"일반 공격 배율 — SOUL 0: ×{Original(0):0.0} → ×{Factor(0):0.0} · SOUL {db.SoulStart}: ×{Factor(db.SoulStart):0.0}",
            $"                         SOUL 150: ×{Original(150):0.0} → ×{Factor(150):0.0}",
            "어빌리티는 원본대로라 SOUL 을 모아 쓰는 쪽이 나아진다. 적의 일반 공격에도 걸린다.",
        ];
        for (int i = 0; i < lines.Length; i++)
            if (Shown(TuningBoxY + 32 + i * 21, 16)) host.UiText(lines[i], x + 16, boxY + 32 + i * 21, i is 1 or 2 ? 0xFFFFE070 : DimGray, 12);

        DrawTuningFoot(x, y0);

        // 펼친 목록은 맨 위에 — 내용 칸 밖(닫기 단추 위)까지 덮는 띄움 창이라 자르지 않는다.
        if (_tuningListOpen && boxShown)
            for (int i = 0; i < SoulWeightChoices.Length; i++)
            {
                int ry = boxY + TuningRowH * (i + 1);
                bool here = MouseInBoard(boxX, ry, TuningBoxW, TuningRowH);
                host.UiFill(boxX, ry, TuningBoxW, TuningRowH, here ? 0xFF2A4A8A : StatusScreen.PanelBg);
                host.UiStroke(boxX, ry, TuningBoxW, TuningRowH, StatusScreen.BoxLine);
                host.UiText(SoulWeightLabel(SoulWeightChoices[i]), boxX + 8, ry + 4, SoulWeightChoices[i] == _soulWeight ? 0xFF00FFFF : White);
            }
    }

    /// <summary>스크롤 막대 — 굴릴 것이 있는 탭만(menu-131). 끄는 중이거나 마우스가 올라가면 손잡이를 밝힌다.</summary>
    private void DrawBar()
    {
        if (Bar() is not { } bar) return;
        var (bx, trackY, trackH, thumbY, thumbH) = bar;
        host.UiFill(bx, trackY, BarW, trackH, StatusScreen.BoxBg);
        bool hot = _barGrab != null || MouseInBoard(bx - BarHitPad, thumbY, BarW + 2 * BarHitPad, thumbH);
        host.UiFill(bx, thumbY, BarW, thumbH, hot ? 0xFF6A8ACA : 0xFF4A6A9A);
    }

    internal void DrawTuningFoot(int x, int y)
    {
        int by = y + TuningH - 40;
        host.UiFill(x + TuningW - 116, by, 100, 28, StatusScreen.HeadBg);
        host.UiText("닫기", x + TuningW - 80, by + 6, White);
        host.UiText("Esc: 닫기", x + 16, by + 7, DimGray);
    }

    /// <summary>체크 줄들을 그린다 — <paramref name="y"/> 는 굴린 내용의 원점, <paramref name="top"/> 은 첫 줄(창 기준). 내용 칸에 다 드는 줄만.</summary>
    private void DrawChecks(int x, int y, int top, (string Label, string Note, int Command, bool On)[] checks)
    {
        for (int i = 0; i < checks.Length; i++)
        {
            if (!Shown(top + i * TuningCheckH, TuningCheckH - 4)) continue;
            int cy = y + top + i * TuningCheckH;
            bool hover = !_tuningListOpen && _barGrab == null && MouseInBoard(x + 12, cy, TuningW - 24, TuningCheckH);
            if (hover || (i == _keyRow && !host.Pad.Active)) host.UiFill(x + 12, cy, TuningW - 24, TuningCheckH - 4, 0x402A4A8A);
            host.UiFill(x + 18, cy + 6, 16, 16, StatusScreen.BoxBg);
            host.UiStroke(x + 18, cy + 6, 16, 16, StatusScreen.BoxLine);
            if (checks[i].On) host.UiText("✔", x + 20, cy + 5, 0xFF00FFFF, 12);
            host.UiText(checks[i].Label, x + 44, cy + 6, White);
            host.UiText(checks[i].Note, x + TuningBoxX, cy + 7, DimGray, 12);
        }
    }

    /// <summary>엔딩 영상 탭의 글을 다시 훑은 때(게임 초)와 그 글 — 틀마다 파일을 찾지 않게 1초에 한 번.</summary>
    private double _movieLinesAt = -10;
    private (string Text, uint Color)[] _movieLines = [];

    /// <summary>엔딩 영상 탭 — 파트1 엔딩 영상(<see cref="Part1EndingMovie"/>)이 있으면 그 자리, 없으면 찾는 곳과 둘 파일을 적는다(사용자 요청).</summary>
    private void DrawMovieTab(int x, int y, int top)
    {
        if (host._lastTime - _movieLinesAt > 1 || host._lastTime < _movieLinesAt)
        {
            _movieLinesAt = host._lastTime;
            _movieLines = Part1EndingMovie.Status();
        }
        for (int i = 0; i < _movieLines.Length; i++)
            if (Shown(top + i * 22, 16)) host.UiText(_movieLines[i].Text, x + 16, y + top + i * 22, _movieLines[i].Color == 0 ? DimGray : _movieLines[i].Color, i == 0 ? 13f : 12f);
    }

    /// <summary>스토리 탭 — 대사 사이 멈춤(설정 메뉴와 같은 값). <paramref name="y"/> 는 굴린 내용의 원점.</summary>
    internal void DrawStoryTab(int x, int y)
    {
        host.UiText("대사 사이 멈춤", x + 16, y + PauseY - 28, White);
        host.UiText("대사가 끝난 뒤 다음 대사가 뜨기까지 배경만 보이는 틈", x + 130, y + PauseY - 27, DimGray, 12);
        int chosen = TalkPauseIndex(host._talkPauseSeconds);
        for (int i = 0; i < TalkPauseChoices.Length; i++)
        {
            int px = x + 16 + i * (PauseW + PauseGap);
            bool on = i == chosen, here = MouseInBoard(px, y + PauseY, PauseW, PauseH);
            host.UiFill(px, y + PauseY, PauseW, PauseH, on ? 0xFF2A4A8A : here ? 0x402A4A8A : StatusScreen.BoxBg);
            host.UiStroke(px, y + PauseY, PauseW, PauseH, StatusScreen.BoxLine);
            host.UiText(TalkPauseChoices[i] < 0 ? "원본(1초)" : TalkPauseLabel(TalkPauseChoices[i]), px + 8, y + PauseY + 5, on ? 0xFF00FFFF : White, 12);
        }
    }

    internal bool MouseInBoard(int x, int y, int w, int h) => host._mouse.X >= x && host._mouse.X < x + w && host._mouse.Y >= y && host._mouse.Y < y + h;
}
