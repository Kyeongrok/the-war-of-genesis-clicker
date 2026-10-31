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

    internal const int TuningW = 600, TuningH = 464, TuningBoxX = 300, TuningBoxW = 180, TuningRowH = 24;

    /// <summary>소울 기여도 상자의 y(창 기준) — 일반 탭 체크 줄 바로 아래(줄이 늘면 따라 내려간다 — 고정 270 이던 때 일곱째 줄과 겹쳤다).</summary>
    internal int TuningBoxY => TuningCheckY + TuningChecks(TabGeneral).Length * TuningCheckH + 8;

    /// <summary>
    /// 파트1 에서 연 창인가 — 소울 기여도는 파트2 일반 공격(SOUL 배율)에만 걸려 파트1 에서는 안 보인다(사용자 요청 menu-120).
    /// </summary>
    private bool Part1Here => SlotsScreen.Part1Slots || (host._battleLoaded && host._scene.IsPart1);

    /// <summary>일반 탭에 소울 기여도 상자를 두나.</summary>
    internal bool ShowsSoulWeight => _tuningTab == TabGeneral && !Part1Here;

    /// <summary>탭 — 0 일반(전투 규칙) · 1 밸런스(어빌리티 개편, 사용자 요청 menu-64) · 2 스토리(대사 진행) · 3 편의성 · 4 실험. 제목줄 바로 아래 한 줄(사용자 요청).</summary>
    internal int _tuningTab = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_TUNING"), out int startTab) ? Math.Clamp(startTab, 0, TabNames.Length - 1) : 0;
    internal const int TabY = 30, TabH = 24, TabW = 96;
    internal static readonly string[] TabNames = ["일반", "밸런스", "스토리", "편의성", "실험"];
    internal const int TabGeneral = 0, TabBalance = 1, TabStory = 2, TabComfort = 3, TabLab = 4;

    /// <summary>스토리 탭 — 「대사 사이 멈춤」 고르기 단추들의 자리(창 기준).</summary>
    internal const int PauseY = 100, PauseW = 90, PauseH = 26, PauseGap = 4, StoryCheckY = 150;

    /// <summary>체크 줄 넷 — 창 위에서부터 y 70 · 102 · 134 · 166(탭 줄 아래). 누르면 그 메뉴 명령을 그대로 돌린다(알림·저장까지).</summary>
    internal const int TuningCheckY = 70, TuningCheckH = 32;

    /// <summary>지금 탭의 체크 줄 — 일반(전투 규칙 둘)과 편의성(보기·진행 편의 넷, 사용자 요청).</summary>
    internal (string Label, string Note, int Command, bool On)[] TuningChecks() => TuningChecks(_tuningTab);

    /// <summary>
    /// 켜 둔 모드 — 모든 탭의 체크 줄 가운데 켠 것의 이름과, 원본이 아닌 고르기(소울 기여도 · 대사 사이 멈춤). 전투 통계가 싣는다(사용자 요청 menu-112).
    /// 「익명 전투 통계 보내기」는 보내는 판이면 늘 켜져 있어 뺀다.
    /// </summary>
    internal List<string> EnabledMods()
    {
        var on = new[] { TabGeneral, TabBalance, TabStory, TabComfort, TabLab }.SelectMany(TuningChecks)
            .Where(c => c.On && c.Command != MenuSendStats).Select(c => c.Label).ToList();
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
    ] : tab == TabLab ?
    [
        // 실험 탭 — 아직 다듬는 중인 것. 기본은 모두 끔.
        ("새 UI (또렷한 글자)", "설정 창을 화면 해상도로 그린다 · 끄면 이전 UI", MenuHiResUi, host._hiResUi),
        ("고해상도 모세스", host.HiBack.StatusLine, MenuHiResMoses, host._hiResMoses),
    ] : tab == TabComfort ?
    [
        ("조작 안내 글 보이기", "화면 아래에 지금 할 수 있는 조작을 알려 준다", MenuHints, host._showHints),
        ("체력바 보이기", "유닛 머리 위의 HP·TP 막대", MenuGauges, host._showGauges),
        ("레벨업 창 보이기", "레벨이 오를 때 능력치 창을 띄운다", MenuLevelUpWindow, host.Btl._showLevelUp),
        ("상자 내용물 보기", "전투 화면 왼쪽 위에 상자에 든 것을 보인다", MenuChestContents, host._showChestContents),
        ("상자 강조", "아직 안 연 상자에 깜박이는 노란 테두리를 두른다", MenuChestGlow, host._highlightChests),
        ("행성에 할 일 표시", "전투·이야기 장소가 있는 행성에 노란 점", MenuPlanetMarks, host._planetMarks),
        ("적 행동 중 클릭으로 건너뛰기", "모션·이펙트를 건너뛰고 결과만 보인다", MenuSkipEnemy, host._skipEnemyAction),
        ("기술 영상 보기", "헬 카이트·진무 천지파열 같은 기술의 영상(처음 한 번 받는다)", MenuSkillMovies, host._skillMovies),
        ("전직할 때 EXP 없애기", "원본처럼 전직하면 쌓인 EXP 를 지운다 · 끄면 남긴다", MenuKeepJobExp, host._dropJobExp),
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
        var (x, y) = TuningOrigin();
        int boxX = x + TuningBoxX, boxY = y + TuningBoxY;
        // 닫기 단추와 탭은 어느 탭에서나.
        if (!_tuningListOpen)
        {
            if (bx >= x + TuningW - 116 && bx < x + TuningW - 16 && by >= y + TuningH - 40 && by < y + TuningH - 12) { _tuningOpen = false; return true; }
            for (int t = 0; t < TabNames.Length; t++)
                if (bx >= x + 12 + t * (TabW + 4) && bx < x + 12 + t * (TabW + 4) + TabW && by >= y + TabY && by < y + TabY + TabH) { _tuningTab = t; return true; }
        }
        if (_tuningTab == TabStory)
        {
            _tuningListOpen = false;
            for (int i = 0; i < TalkPauseChoices.Length; i++)
            {
                int px = x + 16 + i * (PauseW + PauseGap);
                if (bx >= px && bx < px + PauseW && by >= y + PauseY && by < y + PauseY + PauseH) { host.OnMenuCommand(MenuTalkPauseBase + i); return true; }
            }
            var story = TuningChecks();
            int pick = by >= y + StoryCheckY ? (by - y - StoryCheckY) / TuningCheckH : -1;
            if (pick >= 0 && pick < story.Length && bx >= x + 12 && bx < x + TuningW - 12) host.OnMenuCommand(story[pick].Command);
            return true;
        }
        if (_tuningTab != TabGeneral)
        {
            // 밸런스 · 편의성 · 실험 탭 — 체크 줄뿐이다.
            _tuningListOpen = false;
            var rows = TuningChecks();
            int at = by >= y + TuningCheckY ? (by - y - TuningCheckY) / TuningCheckH : -1;
            if (at >= 0 && at < rows.Length && bx >= x + 12 && bx < x + TuningW - 12) host.OnMenuCommand(rows[at].Command);
            return true;
        }
        if (_tuningListOpen && ShowsSoulWeight)
        {
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
        if (ShowsSoulWeight && bx >= boxX && bx < boxX + TuningBoxW && by >= boxY && by < boxY + TuningRowH) { _tuningListOpen = true; return true; }
        var checks = TuningChecks();
        int line = by >= y + TuningCheckY ? (by - y - TuningCheckY) / TuningCheckH : -1;
        if (line >= 0 && line < checks.Length && bx >= x + 12 && bx < x + TuningW - 12) { host.OnMenuCommand(checks[line].Command); return true; }
        return true;
    }

    internal void OnTuningKey(int key)
    {
        if (key != Win32.VK_ESCAPE) return;
        if (_tuningListOpen) _tuningListOpen = false; else _tuningOpen = false;
    }

    internal void DrawTuning()
    {
        if (!_tuningOpen || host._db is not { } db) return;
        var (x, y) = TuningOrigin();
        host.UiFill(x - 4, y - 4, TuningW + 8, TuningH + 8, 0x80000000);
        host.UiFill(x, y, TuningW, TuningH, StatusScreen.PanelBg);
        host.UiStroke(x, y, TuningW, TuningH, StatusScreen.BoxLine);
        host.UiFill(x, y, TuningW, 28, StatusScreen.HeadBg);
        host.UiText("모드 — 원본과 달라지는 것", x + 10, y + 6, White);
        for (int t = 0; t < TabNames.Length; t++)
        {
            int tx = x + 12 + t * (TabW + 4);
            bool on = t == _tuningTab;
            host.UiFill(tx, y + TabY, TabW, TabH, on ? StatusScreen.HeadBg : StatusScreen.BoxBg);
            host.UiStroke(tx, y + TabY, TabW, TabH, StatusScreen.BoxLine);
            host.UiText(TabNames[t], tx + (TabW - host.UiGetText(TabNames[t], White).W) / 2, y + TabY + 4, on ? 0xFF00FFFF : White);
        }
        host.UiFill(x + 12, y + TabY + TabH, TuningW - 24, 1, StatusScreen.BoxLine);
        if (_tuningTab == TabStory) { DrawStoryTab(x, y); DrawChecks(x, y + StoryCheckY, TuningChecks()); DrawTuningFoot(x, y); return; }

        var checks = TuningChecks();
        DrawChecks(x, y + TuningCheckY, checks);

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
            for (int i = 0; i < rework.Length; i++) host.UiText(rework[i], x + 44, y + TuningCheckY + checks.Length * TuningCheckH + 8 + i * 22, DimGray, 12);
        }
        if (!ShowsSoulWeight) { DrawTuningFoot(x, y); return; }
        int boxX = x + TuningBoxX, boxY = y + TuningBoxY;
        host.UiText("일반 공격 소울 기여도", x + 16, boxY + 4, White);
        host.UiFill(boxX, boxY, TuningBoxW, TuningRowH - 2, StatusScreen.BoxBg);
        host.UiStroke(boxX, boxY, TuningBoxW, TuningRowH - 2, StatusScreen.BoxLine);
        host.UiText(SoulWeightLabel(_soulWeight), boxX + 8, boxY + 4, White);
        host.UiText("▼", boxX + TuningBoxW - 18, boxY + 4, DimGray);

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
        for (int i = 0; i < lines.Length; i++) host.UiText(lines[i], x + 16, boxY + 32 + i * 21, i is 1 or 2 ? 0xFFFFE070 : DimGray, 12);

        DrawTuningFoot(x, y);

        // 펼친 목록은 맨 위에
        if (_tuningListOpen)
            for (int i = 0; i < SoulWeightChoices.Length; i++)
            {
                int ry = boxY + TuningRowH * (i + 1);
                bool here = MouseInBoard(boxX, ry, TuningBoxW, TuningRowH);
                host.UiFill(boxX, ry, TuningBoxW, TuningRowH, here ? 0xFF2A4A8A : StatusScreen.PanelBg);
                host.UiStroke(boxX, ry, TuningBoxW, TuningRowH, StatusScreen.BoxLine);
                host.UiText(SoulWeightLabel(SoulWeightChoices[i]), boxX + 8, ry + 4, SoulWeightChoices[i] == _soulWeight ? 0xFF00FFFF : White);
            }
    }

    internal void DrawTuningFoot(int x, int y)
    {
        int by = y + TuningH - 40;
        host.UiFill(x + TuningW - 116, by, 100, 28, StatusScreen.HeadBg);
        host.UiText("닫기", x + TuningW - 80, by + 6, White);
        host.UiText("Esc: 닫기", x + 16, by + 7, DimGray);
    }

    /// <summary>스토리 탭 — 대사 사이 멈춤(설정 메뉴와 같은 값)과 대사 첫 클릭 방식.</summary>
    /// <summary>체크 줄들을 <paramref name="top"/> 부터 한 줄씩 그린다.</summary>
    private void DrawChecks(int x, int top, (string Label, string Note, int Command, bool On)[] checks)
    {
        for (int i = 0; i < checks.Length; i++)
        {
            int cy = top + i * TuningCheckH;
            if (!_tuningListOpen && MouseInBoard(x + 12, cy, TuningW - 24, TuningCheckH)) host.UiFill(x + 12, cy, TuningW - 24, TuningCheckH - 4, 0x402A4A8A);
            host.UiFill(x + 18, cy + 6, 16, 16, StatusScreen.BoxBg);
            host.UiStroke(x + 18, cy + 6, 16, 16, StatusScreen.BoxLine);
            if (checks[i].On) host.UiText("✔", x + 20, cy + 5, 0xFF00FFFF, 12);
            host.UiText(checks[i].Label, x + 44, cy + 6, White);
            host.UiText(checks[i].Note, x + TuningBoxX, cy + 7, DimGray, 12);
        }
    }

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
