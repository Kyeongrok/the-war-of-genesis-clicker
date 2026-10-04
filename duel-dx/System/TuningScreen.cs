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

    internal const int TuningW = 600, TuningH = 400, TuningBoxX = 300, TuningBoxY = 142, TuningBoxW = 180, TuningRowH = 24;

    /// <summary>탭 — 0 일반(전투 규칙) · 1 스토리(대사 진행). 제목줄 바로 아래 한 줄(사용자 요청).</summary>
    internal int _tuningTab = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_TUNING"), out int startTab) ? Math.Clamp(startTab, 0, 2) : 0;
    internal const int TabY = 30, TabH = 24, TabW = 96;
    internal static readonly string[] TabNames = ["일반", "스토리", "편의성"];

    /// <summary>스토리 탭 — 「대사 사이 멈춤」 고르기 단추들의 자리(창 기준).</summary>
    internal const int PauseY = 100, PauseW = 90, PauseH = 26, PauseGap = 4, StoryCheckY = 150;

    /// <summary>체크 줄 넷 — 창 위에서부터 y 70 · 102 · 134 · 166(탭 줄 아래). 누르면 그 메뉴 명령을 그대로 돌린다(알림·저장까지).</summary>
    internal const int TuningCheckY = 70, TuningCheckH = 32;

    /// <summary>지금 탭의 체크 줄 — 일반(전투 규칙 둘)과 편의성(보기·진행 편의 넷, 사용자 요청).</summary>
    internal (string Label, string Note, int Command, bool On)[] TuningChecks() => _tuningTab == 2 ?
    [
        ("체력바 보이기", "유닛 머리 위의 HP·TP 막대", MenuGauges, host._showGauges),
        ("레벨업 창 보이기", "레벨이 오를 때 능력치 창을 띄운다", MenuLevelUpWindow, host.Btl._showLevelUp),
        ("상자 내용물 보기", "전투 화면 왼쪽 위에 상자에 든 것을 보인다", MenuChestContents, host._showChestContents),
        ("적 행동 중 클릭으로 건너뛰기", "모션·이펙트를 건너뛰고 결과만 보인다", MenuSkipEnemy, host._skipEnemyAction),
    ] :
    [
        ("동맹을 AI 가 움직임", "끄면 동맹(편 3)도 내가 움직인다", MenuAllyAi, host._allyAi),
        ("전투 시작 시 소울 가득", "다음 전투부터 내 편 소울을 가득 채워 시작한다", MenuFullSoul, host._fullSoulAtStart),
    ];

    internal static string SoulWeightLabel(int p) => p switch { 100 => "100% (원본)", 0 => "0% (소울 무관)", _ => $"{p}%" };

    /// <summary>일반 공격을 셀 때 쓰는 SOUL — 시작값에서 벌어진 몫만 기여도만큼.</summary>
    internal int BasicAttackSoul(int soul) =>
        host._db is not { } db || _soulWeight == 100 ? soul : Math.Max(0, db.SoulStart + (soul - db.SoulStart) * _soulWeight / 100);

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
        if (_tuningTab == 1)
        {
            _tuningListOpen = false;
            for (int i = 0; i < TalkPauseChoices.Length; i++)
            {
                int px = x + 16 + i * (PauseW + PauseGap);
                if (bx >= px && bx < px + PauseW && by >= y + PauseY && by < y + PauseY + PauseH) { host.OnMenuCommand(MenuTalkPauseBase + i); return true; }
            }
            if (bx >= x + 12 && bx < x + TuningW - 12 && by >= y + StoryCheckY && by < y + StoryCheckY + TuningCheckH) host.OnMenuCommand(MenuTalkClickFills);
            return true;
        }
        if (_tuningTab == 2)
        {
            // 편의성 탭 — 체크 줄뿐이다.
            _tuningListOpen = false;
            var rows = TuningChecks();
            int at = by >= y + TuningCheckY ? (by - y - TuningCheckY) / TuningCheckH : -1;
            if (at >= 0 && at < rows.Length && bx >= x + 12 && bx < x + TuningW - 12) host.OnMenuCommand(rows[at].Command);
            return true;
        }
        if (_tuningListOpen)
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
        if (bx >= boxX && bx < boxX + TuningBoxW && by >= boxY && by < boxY + TuningRowH) { _tuningListOpen = true; return true; }
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
        host.FillRect(x - 4, y - 4, TuningW + 8, TuningH + 8, 0x80000000);
        host.FillRect(x, y, TuningW, TuningH, StatusScreen.PanelBg);
        host.StrokeRect(x, y, TuningW, TuningH, StatusScreen.BoxLine);
        host.FillRect(x, y, TuningW, 28, StatusScreen.HeadBg);
        host.DrawText("모드 — 원본과 달라지는 것", x + 10, y + 6, White);
        for (int t = 0; t < TabNames.Length; t++)
        {
            int tx = x + 12 + t * (TabW + 4);
            bool on = t == _tuningTab;
            host.FillRect(tx, y + TabY, TabW, TabH, on ? StatusScreen.HeadBg : StatusScreen.BoxBg);
            host.StrokeRect(tx, y + TabY, TabW, TabH, StatusScreen.BoxLine);
            host.DrawText(TabNames[t], tx + (TabW - host.GetText(TabNames[t], White).W) / 2, y + TabY + 4, on ? 0xFF00FFFF : White);
        }
        host.FillRect(x + 12, y + TabY + TabH, TuningW - 24, 1, StatusScreen.BoxLine);
        if (_tuningTab == 1) { DrawStoryTab(x, y); DrawTuningFoot(x, y); return; }

        var checks = TuningChecks();
        for (int i = 0; i < checks.Length; i++)
        {
            int cy = y + TuningCheckY + i * TuningCheckH;
            if (!_tuningListOpen && MouseInBoard(x + 12, cy, TuningW - 24, TuningCheckH)) host.FillRect(x + 12, cy, TuningW - 24, TuningCheckH - 4, 0x402A4A8A);
            host.FillRect(x + 18, cy + 6, 16, 16, StatusScreen.BoxBg);
            host.StrokeRect(x + 18, cy + 6, 16, 16, StatusScreen.BoxLine);
            if (checks[i].On) host.DrawText("✔", x + 20, cy + 5, 0xFF00FFFF, 12);
            host.DrawText(checks[i].Label, x + 44, cy + 6, White);
            host.DrawText(checks[i].Note, x + TuningBoxX, cy + 7, DimGray, 12);
        }

        if (_tuningTab == 2) { DrawTuningFoot(x, y); return; }
        int boxX = x + TuningBoxX, boxY = y + TuningBoxY;
        host.DrawText("일반 공격 소울 기여도", x + 16, boxY + 4, White);
        host.FillRect(boxX, boxY, TuningBoxW, TuningRowH - 2, StatusScreen.BoxBg);
        host.StrokeRect(boxX, boxY, TuningBoxW, TuningRowH - 2, StatusScreen.BoxLine);
        host.DrawText(SoulWeightLabel(_soulWeight), boxX + 8, boxY + 4, White);
        host.DrawText("▼", boxX + TuningBoxW - 18, boxY + 4, DimGray);

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
        for (int i = 0; i < lines.Length; i++) host.DrawText(lines[i], x + 16, boxY + 44 + i * 22, i is 1 or 2 ? 0xFFFFE070 : DimGray, 12);

        DrawTuningFoot(x, y);

        // 펼친 목록은 맨 위에
        if (_tuningListOpen)
            for (int i = 0; i < SoulWeightChoices.Length; i++)
            {
                int ry = boxY + TuningRowH * (i + 1);
                bool here = MouseInBoard(boxX, ry, TuningBoxW, TuningRowH);
                host.FillRect(boxX, ry, TuningBoxW, TuningRowH, here ? 0xFF2A4A8A : StatusScreen.PanelBg);
                host.StrokeRect(boxX, ry, TuningBoxW, TuningRowH, StatusScreen.BoxLine);
                host.DrawText(SoulWeightLabel(SoulWeightChoices[i]), boxX + 8, ry + 4, SoulWeightChoices[i] == _soulWeight ? 0xFF00FFFF : White);
            }
    }

    internal void DrawTuningFoot(int x, int y)
    {
        int by = y + TuningH - 40;
        host.FillRect(x + TuningW - 116, by, 100, 28, StatusScreen.HeadBg);
        host.DrawText("닫기", x + TuningW - 80, by + 6, White);
        host.DrawText("Esc: 닫기", x + 16, by + 7, DimGray);
    }

    /// <summary>스토리 탭 — 대사 사이 멈춤(설정 메뉴와 같은 값)과 대사 첫 클릭 방식.</summary>
    internal void DrawStoryTab(int x, int y)
    {
        host.DrawText("대사 사이 멈춤", x + 16, y + PauseY - 28, White);
        host.DrawText("대사가 끝난 뒤 다음 대사가 뜨기까지 배경만 보이는 틈", x + 130, y + PauseY - 27, DimGray, 12);
        int chosen = TalkPauseIndex(host._talkPauseSeconds);
        for (int i = 0; i < TalkPauseChoices.Length; i++)
        {
            int px = x + 16 + i * (PauseW + PauseGap);
            bool on = i == chosen, here = MouseInBoard(px, y + PauseY, PauseW, PauseH);
            host.FillRect(px, y + PauseY, PauseW, PauseH, on ? 0xFF2A4A8A : here ? 0x402A4A8A : StatusScreen.BoxBg);
            host.StrokeRect(px, y + PauseY, PauseW, PauseH, StatusScreen.BoxLine);
            host.DrawText(TalkPauseChoices[i] < 0 ? "원본(1초)" : TalkPauseLabel(TalkPauseChoices[i]), px + 8, y + PauseY + 5, on ? 0xFF00FFFF : White, 12);
        }
        int cy = y + StoryCheckY;
        if (MouseInBoard(x + 12, cy, TuningW - 24, TuningCheckH)) host.FillRect(x + 12, cy, TuningW - 24, TuningCheckH - 4, 0x402A4A8A);
        host.FillRect(x + 18, cy + 6, 16, 16, StatusScreen.BoxBg);
        host.StrokeRect(x + 18, cy + 6, 16, 16, StatusScreen.BoxLine);
        if (host._talkClickFills) host.DrawText("✔", x + 20, cy + 5, 0xFF00FFFF, 12);
        host.DrawText("대사 첫 클릭은 글 채우기", x + 44, cy + 6, White);
        host.DrawText("끄면 원본처럼 첫 클릭에 바로 넘어간다", x + TuningBoxX, cy + 7, DimGray, 12);
        host.DrawText("「대사 첫 클릭은 글 채우기」는 설정 메뉴에도 있다(같은 값).", x + 16, cy + 48, DimGray, 12);
    }

    internal bool MouseInBoard(int x, int y, int w, int h) => host._mouse.X >= x && host._mouse.X < x + w && host._mouse.Y >= y && host._mouse.Y < y + h;
}
