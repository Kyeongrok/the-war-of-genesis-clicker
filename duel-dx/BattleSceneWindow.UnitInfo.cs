using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 인물 위에서 오른쪽 단추를 누르고 있는 동안 뜨는 정보 창(fa-8) — 적·아군 모두 같은 창이다.
/// </summary>
/// <remarks>
/// 옵시디안 분석-전투 "적·아군 유닛 정보 창": 창 140×268 을 마우스 자리 + (16,16) 에 띄우고,
/// 제목은 이름, 줄은 칭호 · 계열 · 선 · LEVEL · EXP · 선 · HP · SOUL · TP · 선 · ATK · ACR · RDP · 상태이상 칸.
/// 값은 오른쪽 맞춤, 앞 두 줄만 가운데. 그림·막대·초상은 없다. <b>오른쪽 단추를 떼면 닫힌다.</b>
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int InfoW = 140, InfoH = 268;

    private int _infoUnit = -1;
    private (int X, int Y) _infoAt;

    private bool InfoOpen => _infoUnit >= 0;

    /// <summary>인물 위에서 오른쪽 단추를 누르면 정보 창을 띄운다. 띄웠으면 true.</summary>
    private bool OpenUnitInfo(int bx, int by)
    {
        int index = UnitAtBoard(bx, by);
        if (index < 0) return false;
        _infoUnit = index;
        _infoAt = (Math.Clamp(bx + 16, 0, BoardWidth - InfoW), Math.Clamp(by + 16, _camY, _camY + ViewHeight - InfoH));
        return true;
    }

    private void CloseUnitInfo() => _infoUnit = -1;

    private void DrawUnitInfo()
    {
        if (!InfoOpen || _db is not { } db || _units[_infoUnit] is not { Data: { } c } unit) return;
        var (x, y) = _infoAt;

        // 원본 창 틀 — 제목줄에 이름(분석-시스템메뉴 「메시지 창 틀」)
        DarkenRect(x - 1, y - FrameTitleH - 1, InfoW + 2, InfoH + FrameTitleH + 2, 8);
        DrawGameFrame(x, y, InfoW, InfoH, db.T(c.NameId));

        Centre(db.T(c.TitleId), x, y + 20, DimGray);
        Centre(db.FamilyName(c), x, y + 36, DimGray);
        Rule(x, y + 58);
        Row(db.T(160), c.Level.ToString(), y + 68);
        Row(db.T(161), c.Exp.ToString(), y + 84);
        Rule(x, y + 106);
        Row(db.T(159), $"{unit.Hp}/{unit.MaxHp}", y + 116);
        Row(db.T(41), $"{unit.Soul}/{unit.MaxSoul}", y + 132);
        Row(db.T(38), $"{unit.Tp}/{unit.MaxTp}", y + 148);
        Rule(x, y + 170);
        Row(db.T(156), db.Atk(c, unit.Soul).ToString(), y + 180);
        Row(db.T(157), db.Acr(c, unit.Tp).ToString(), y + 196);
        Row(db.T(158), db.Rdp(c, unit.Hp, unit.MaxHp).ToString(), y + 212);

        // 상태이상 칸 셋 — 원본처럼 Obs 0489 아이콘 한 장씩, 빈 칸은 모션 0(「EMPTY」 판)이다(분석-전투 창 228).
        FillRect(x + 6, y + 228, InfoW - 12, 34, BoxBg);
        StrokeRect(x + 6, y + 228, InfoW - 12, 34, BoxLine);
        for (int i = 0; i < 3; i++)
            DrawUi(AilmentIconObs, AilmentIconMotion(unit, i), 0, x + 10 + i * 40, y + 236, UiBlend.Alpha, loop: false);

        void Centre(string text, int left, int top, uint color)
        {
            var (_, w, _) = GetText(text, color);
            DrawText(text, left + (InfoW - w) / 2, top, color);
        }

        void Row(string label, string value, int top)
        {
            DrawText(label, x + 10, top, White);
            RightText(value, x + 130, top, White);
        }

        void Rule(int left, int top) => FillRect(left + 10, top, InfoW - 20, 1, BoxLine);
    }
}
