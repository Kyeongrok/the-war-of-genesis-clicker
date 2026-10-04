using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

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
internal sealed unsafe partial class BattleSceneWindow
{
    internal const int MenuTuning = 1150;

    /// <summary>고를 수 있는 기여도(%) — 첫째가 원본.</summary>
    internal static readonly int[] SoulWeightChoices = [100, 75, 50, 25, 0];

    internal int _soulWeight = SoulWeightChoices.Contains(UserSettings.Current.SoulWeight) ? UserSettings.Current.SoulWeight : 100;

    internal bool _tuningOpen;

    /// <summary>선택 상자가 펼쳐져 있나.</summary>
    internal bool _tuningListOpen;

    internal const int TuningW = 600, TuningH = 398, TuningBoxX = 300, TuningBoxY = 176, TuningBoxW = 180, TuningRowH = 24;

    /// <summary>체크 줄 넷 — 창 위에서부터 y 44 · 76 · 108 · 140. 누르면 그 메뉴 명령을 그대로 돌린다(알림·저장까지).</summary>
    internal const int TuningCheckY = 44, TuningCheckH = 32;

    internal (string Label, string Note, int Command, bool On)[] TuningChecks() =>
    [
        ("동맹을 AI 가 움직임", "끄면 동맹(편 3)도 내가 움직인다", MenuAllyAi, _allyAi),
        ("상자 내용물 보기", "전투 화면 왼쪽 위에 상자에 든 것을 보인다", MenuChestContents, _showChestContents),
        ("전투 시작 시 소울 가득", "다음 전투부터 내 편 소울을 가득 채워 시작한다", MenuFullSoul, _fullSoulAtStart),
        ("적 행동 중 클릭으로 건너뛰기", "모션·이펙트를 건너뛰고 결과만 보인다", MenuSkipEnemy, _skipEnemyAction),
    ];

    internal static string SoulWeightLabel(int p) => p switch { 100 => "100% (원본)", 0 => "0% (소울 무관)", _ => $"{p}%" };

    /// <summary>일반 공격을 셀 때 쓰는 SOUL — 시작값에서 벌어진 몫만 기여도만큼.</summary>
    internal int BasicAttackSoul(int soul) =>
        _db is not { } db || _soulWeight == 100 ? soul : Math.Max(0, db.SoulStart + (soul - db.SoulStart) * _soulWeight / 100);

    /// <summary>그 work 로 칠 때 쓰는 SOUL — 어빌리티에 안 딸린 work(일반 공격·몬스터 기본기)만 기여도를 건다.</summary>
    internal int AttackSoul(WorkData w, int soul) => w.AbilityId == 0 ? BasicAttackSoul(soul) : soul;

    internal (int X, int Y) TuningOrigin() => (_camX + (ViewWidth - TuningW) / 2, _camY + (ViewHeight - TuningH) / 2);

    /// <summary>조정 창이 열려 있으면 클릭을 먹는다.</summary>
    internal bool OnTuningClick(int bx, int by)
    {
        if (!_tuningOpen) return false;
        var (x, y) = TuningOrigin();
        int boxX = x + TuningBoxX, boxY = y + TuningBoxY;
        if (_tuningListOpen)
        {
            int row = (by - boxY - TuningRowH) / TuningRowH;
            if (bx >= boxX && bx < boxX + TuningBoxW && by >= boxY + TuningRowH && row >= 0 && row < SoulWeightChoices.Length)
            {
                _soulWeight = SoulWeightChoices[row];
                SaveSettings();
                Toast($"일반 공격 소울 기여도: {SoulWeightLabel(_soulWeight)}");
            }
            _tuningListOpen = false;
            return true;
        }
        if (bx >= boxX && bx < boxX + TuningBoxW && by >= boxY && by < boxY + TuningRowH) { _tuningListOpen = true; return true; }
        var checks = TuningChecks();
        int line = by >= y + TuningCheckY ? (by - y - TuningCheckY) / TuningCheckH : -1;
        if (line >= 0 && line < checks.Length && bx >= x + 12 && bx < x + TuningW - 12) { OnMenuCommand(checks[line].Command); return true; }
        if (bx >= x + TuningW - 116 && bx < x + TuningW - 16 && by >= y + TuningH - 40 && by < y + TuningH - 12) _tuningOpen = false;
        return true;
    }

    internal void OnTuningKey(int key)
    {
        if (key != Win32.VK_ESCAPE) return;
        if (_tuningListOpen) _tuningListOpen = false; else _tuningOpen = false;
    }

    internal void DrawTuning()
    {
        if (!_tuningOpen || _db is not { } db) return;
        var (x, y) = TuningOrigin();
        FillRect(x - 4, y - 4, TuningW + 8, TuningH + 8, 0x80000000);
        FillRect(x, y, TuningW, TuningH, PanelBg);
        StrokeRect(x, y, TuningW, TuningH, BoxLine);
        FillRect(x, y, TuningW, 28, HeadBg);
        DrawText("모드 — 원본과 달라지는 것", x + 10, y + 6, White);

        var checks = TuningChecks();
        for (int i = 0; i < checks.Length; i++)
        {
            int cy = y + TuningCheckY + i * TuningCheckH;
            if (!_tuningListOpen && MouseInBoard(x + 12, cy, TuningW - 24, TuningCheckH)) FillRect(x + 12, cy, TuningW - 24, TuningCheckH - 4, 0x402A4A8A);
            FillRect(x + 18, cy + 6, 16, 16, BoxBg);
            StrokeRect(x + 18, cy + 6, 16, 16, BoxLine);
            if (checks[i].On) DrawText("✔", x + 20, cy + 5, 0xFF00FFFF, 12);
            DrawText(checks[i].Label, x + 44, cy + 6, White);
            DrawText(checks[i].Note, x + TuningBoxX, cy + 7, DimGray, 12);
        }

        int boxX = x + TuningBoxX, boxY = y + TuningBoxY;
        DrawText("일반 공격 소울 기여도", x + 16, boxY + 4, White);
        FillRect(boxX, boxY, TuningBoxW, TuningRowH - 2, BoxBg);
        StrokeRect(boxX, boxY, TuningBoxW, TuningRowH - 2, BoxLine);
        DrawText(SoulWeightLabel(_soulWeight), boxX + 8, boxY + 4, White);
        DrawText("▼", boxX + TuningBoxW - 18, boxY + 4, DimGray);

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
        for (int i = 0; i < lines.Length; i++) DrawText(lines[i], x + 16, boxY + 44 + i * 22, i is 1 or 2 ? 0xFFFFE070 : DimGray, 12);

        int by = y + TuningH - 40;
        FillRect(x + TuningW - 116, by, 100, 28, HeadBg);
        DrawText("닫기", x + TuningW - 80, by + 6, White);
        DrawText("Esc: 닫기", x + 16, by + 7, DimGray);

        // 펼친 목록은 맨 위에
        if (_tuningListOpen)
            for (int i = 0; i < SoulWeightChoices.Length; i++)
            {
                int ry = boxY + TuningRowH * (i + 1);
                bool here = MouseInBoard(boxX, ry, TuningBoxW, TuningRowH);
                FillRect(boxX, ry, TuningBoxW, TuningRowH, here ? 0xFF2A4A8A : PanelBg);
                StrokeRect(boxX, ry, TuningBoxW, TuningRowH, BoxLine);
                DrawText(SoulWeightLabel(SoulWeightChoices[i]), boxX + 8, ry + 4, SoulWeightChoices[i] == _soulWeight ? 0xFF00FFFF : White);
            }
    }

    internal bool MouseInBoard(int x, int y, int w, int h) => _mouse.X >= x && _mouse.X < x + w && _mouse.Y >= y && _mouse.Y < y + h;
}
