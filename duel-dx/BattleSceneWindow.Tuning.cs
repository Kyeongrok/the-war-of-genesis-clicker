using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 모드 &gt; 조정 창 — 원본과 달라지는 전투 수치를 게임 안에서 고른다(사용자 요청 menu-7). 지금은 「일반 공격 소울 기여도」 하나.
/// </summary>
/// <remarks>
/// ATK = 바탕 × (SOUL + 10) ÷ 10 이라 SOUL 150 이면 일반 공격이 시작(SOUL 40, ×5.0)의 3.2배(×16.0)가 된다. 그래서 SOUL 을 아끼고
/// 일반 공격만 하는 쪽이 이득이었다. 기여도 p% 면 일반 공격을 셀 때 SOUL 을 <c>40 + (SOUL − 40) × p ÷ 100</c> 으로 본다 —
/// 시작 세기는 그대로, 거기서 벌어지는 몫만 줄인다. 어빌리티(어빌리티에 딸린 work)는 원본대로 둬서 SOUL 을 모아 쓰는 쪽이 나아진다.
/// 적의 일반 공격에도 똑같이 걸린다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int MenuTuning = 1150;

    /// <summary>고를 수 있는 기여도(%) — 첫째가 원본.</summary>
    private static readonly int[] SoulWeightChoices = [100, 75, 50, 25, 0];

    private int _soulWeight = SoulWeightChoices.Contains(UserSettings.Current.SoulWeight) ? UserSettings.Current.SoulWeight : 100;

    private bool _tuningOpen;

    /// <summary>선택 상자가 펼쳐져 있나.</summary>
    private bool _tuningListOpen;

    private const int TuningW = 600, TuningH = 270, TuningBoxX = 300, TuningBoxY = 48, TuningBoxW = 180, TuningRowH = 24;

    private static string SoulWeightLabel(int p) => p switch { 100 => "100% (원본)", 0 => "0% (소울 무관)", _ => $"{p}%" };

    /// <summary>일반 공격을 셀 때 쓰는 SOUL — 시작값에서 벌어진 몫만 기여도만큼.</summary>
    private int BasicAttackSoul(int soul) =>
        _db is not { } db || _soulWeight == 100 ? soul : Math.Max(0, db.SoulStart + (soul - db.SoulStart) * _soulWeight / 100);

    /// <summary>그 work 로 칠 때 쓰는 SOUL — 어빌리티에 안 딸린 work(일반 공격·몬스터 기본기)만 기여도를 건다.</summary>
    private int AttackSoul(WorkData w, int soul) => w.AbilityId == 0 ? BasicAttackSoul(soul) : soul;

    private (int X, int Y) TuningOrigin() => (_camX + (ViewWidth - TuningW) / 2, _camY + (ViewHeight - TuningH) / 2);

    /// <summary>조정 창이 열려 있으면 클릭을 먹는다.</summary>
    private bool OnTuningClick(int bx, int by)
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
        if (bx >= x + TuningW - 116 && bx < x + TuningW - 16 && by >= y + TuningH - 40 && by < y + TuningH - 12) _tuningOpen = false;
        return true;
    }

    private void OnTuningKey(int key)
    {
        if (key != Win32.VK_ESCAPE) return;
        if (_tuningListOpen) _tuningListOpen = false; else _tuningOpen = false;
    }

    private void DrawTuning()
    {
        if (!_tuningOpen || _db is not { } db) return;
        var (x, y) = TuningOrigin();
        FillRect(x - 4, y - 4, TuningW + 8, TuningH + 8, 0x80000000);
        FillRect(x, y, TuningW, TuningH, PanelBg);
        StrokeRect(x, y, TuningW, TuningH, BoxLine);
        FillRect(x, y, TuningW, 28, HeadBg);
        DrawText("조정 — 원본과 달라지는 값", x + 10, y + 6, White);

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
        for (int i = 0; i < lines.Length; i++) DrawText(lines[i], x + 16, y + 92 + i * 22, i is 1 or 2 ? 0xFFFFE070 : DimGray, 12);

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

    private bool MouseInBoard(int x, int y, int w, int h) => _mouse.X >= x && _mouse.X < x + w && _mouse.Y >= y && _mouse.Y < y + h;
}
