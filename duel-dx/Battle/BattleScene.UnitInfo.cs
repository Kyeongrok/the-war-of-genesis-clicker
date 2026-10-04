using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 인물 위에서 오른쪽 단추를 누르고 있는 동안 뜨는 정보 창(fa-8) — 적·아군 모두 같은 창이다.
/// </summary>
/// <remarks>
/// 옵시디안 분석-전투 "적·아군 유닛 정보 창": 창 140×268 을 마우스 자리 + (16,16) 에 띄우고,
/// 제목은 이름, 줄은 칭호 · 계열 · 선 · LEVEL · EXP · 선 · HP · SOUL · TP · 선 · ATK · ACR · RDP · 상태이상 칸.
/// 값은 오른쪽 맞춤, 앞 두 줄만 가운데. 그림·막대·초상은 없다. <b>오른쪽 단추를 떼면 닫힌다.</b>
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    internal const int InfoW = 140, InfoH = 268;

    internal int _infoUnit = -1;
    internal (int X, int Y) _infoAt;

    internal bool InfoOpen => _infoUnit >= 0;

    /// <summary>인물 위에서 오른쪽 단추를 누르면 정보 창을 띄운다. 띄웠으면 true.</summary>
    internal bool OpenUnitInfo(int bx, int by)
    {
        int index = host.UnitAtPoint(bx, by);
        if (index < 0) return false;
        _infoUnit = index;
        _infoAt = (Math.Clamp(bx + 16, host._camX, host._camX + host.ViewWidth - InfoW), Math.Clamp(by + 16, host._camY, host._camY + host.ViewHeight - InfoH));
        return true;
    }

    internal void CloseUnitInfo() => _infoUnit = -1;

    /// <summary>DUELDX_INFO=1 이면 첫 아군의 정보 창을 띄워 둔다(화면 밖 시험용 — 오른쪽 단추를 누르고 있는 것처럼).</summary>
    internal void OpenUnitInfoIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_INFO") != "1" || InfoOpen || host._units.Length == 0) return;
        int ally = Array.FindIndex(host._units, u => u.Alive && u.OnField && u.IsAlly);
        if (ally < 0) return;
        _infoUnit = ally;
        _infoAt = (host._camX + 300, host._camY + 80);
    }

    internal void DrawUnitInfo()
    {
        if (!InfoOpen || host._db is not { } db || host._units[_infoUnit] is not { Data: { } c } unit) return;
        var (x, y) = _infoAt;

        // 걸린 상태이상·버프를 아이콘 아래에 글로 — 누르고 있는 동안 무엇이 걸렸는지 바로 보이게(원본에 없는 덧붙임, 사용자 요청).
        var effects = AilmentLabels(unit);
        foreach (var (label, value) in new[] { ("DEX", unit.BonusDex), ("PSY", unit.BonusPsy), ("DEP", unit.BonusDep),
                                               ("최대 TP", unit.BonusMaxTp), ("최대 SOUL", unit.BonusMaxSoul), ("최대 HP", unit.BonusMaxHp) })
            if (value != 0) effects.Add($"{label} {value:+#;-#}");
        const int EffectLineH = 16;
        int h = InfoH + (effects.Count > 0 ? effects.Count * EffectLineH + 6 : 0);
        y = Math.Min(y, host._camY + host.ViewHeight - h);

        // 원본 창 틀 — 제목줄에 이름(분석-시스템메뉴 「메시지 창 틀」)
        host.DarkenRect(x - 1, y - FrameTitleH - 1, InfoW + 2, h + FrameTitleH + 2, 8);
        host.DrawGameFrame(x, y, InfoW, h, db.T(c.NameId));

        // 글자는 모두 흰색이다(0x10041450 색 −1, 분석-전투 「정보 창」) — 전에는 칭호·계열이 회색이었다(ba-20 G17).
        Centre(db.T(c.TitleId), x, y + 20, White);
        Centre(db.FamilyName(c), x, y + 36, White);
        Rule(x, y + 58);
        Row(db.T(160), c.Level.ToString(), y + 68);
        Row(db.T(161), c.Exp.ToString(), y + 84);
        Rule(x, y + 106);
        // 보정(장비 + 장착 어빌리티 + 유닛 가감)이 0 보다 크면 "%d/%d(+%d)"(0x1016f200), 아니면 "%d/%d"(0x1016f1ec) — Status 창과 같은 값.
        static string WithBonus(int now, int max, int bonus) => bonus > 0 ? $"{now}/{max}(+{bonus})" : $"{now}/{max}";
        int hpBonus = db.EquipBonus(c, 0x30) + unit.BonusMaxHp, armor = db.ArmorRate(c);
        if (hpBonus > 0 && armor != 0 && db.N(6) != 0) hpBonus = hpBonus * (armor + db.N(6)) / db.N(6);
        Row(db.T(159), WithBonus(unit.Hp, unit.MaxHp, hpBonus), y + 116);
        Row(db.T(41), WithBonus(unit.Soul, unit.MaxSoul, db.EquipBonus(c, 0x25) + unit.BonusMaxSoul), y + 132);
        Row(db.T(38), WithBonus(unit.Tp, unit.MaxTp, db.EquipBonus(c, 0x21) + unit.BonusMaxTp), y + 148);
        Rule(x, y + 170);
        // ATK·ACR·RDP 는 원본 게터(0x1007ab20·ab90·abf0)처럼 상태이상·군단 보정·부하=대장 DEX/최대 TP 를 얹은 값 — Status 와 같은 헬퍼(감사4 S1).
        var (shown, acr, rdp, _) = host.StatusScr.ShownStats(db, unit, c);
        Row(db.T(156), StatusScreen.AtkWithSoul(db, shown, host.TuningScr.BasicAttackSoul(unit.Soul), compact: true, weaponPercent: unit.Status(29)), y + 180);
        Row(db.T(157), acr.ToString(), y + 196);
        Row(db.T(158), rdp.ToString(), y + 212);

        // 상태이상 칸 셋 — 원본처럼 Obs 0489 아이콘 한 장씩, 빈 칸은 모션 0(「EMPTY」 판)이다(분석-전투 창 228).
        host.FillRect(x + 6, y + 228, InfoW - 12, 34, StatusScreen.BoxBg);
        host.StrokeRect(x + 6, y + 228, InfoW - 12, 34, StatusScreen.BoxLine);
        // 아이콘 기준점은 그림 <b>가운데</b>다(스테이터스 창도 가운데 자리로 찍는다) — 왼쪽 위 자리로 찍어 칸 밖으로 반쯤 삐져나왔다(사용자 보고).
        // 원본 상태이상 칸(0x100d53c0, 정보 창에선 (0,228,140,40)): s = (폭 − 40)/3, 가운데 x = 10 + s/2 + (s + 10)·i, y = (높이 − 20)/2 + 10
        // → 본문 (26 + 43i, 248)(0x100d5437~0x100d54dd, 감사4 I4).
        for (int i = 0; i < 3; i++)
            host.DrawUi(AilmentIconObs, AilmentIconMotion(unit, i), 0, x + 26 + 43 * i, y + 248, UiBlend.Alpha, loop: false);
        for (int i = 0; i < effects.Count; i++)
            host.DrawText(effects[i], x + 10, y + 268 + i * EffectLineH, 0xFFFFE070, 11f);

        void Centre(string text, int left, int top, uint color)
        {
            var (_, w, _) = host.GetText(text, color);
            host.DrawText(text, left + (InfoW - w) / 2, top, color);
        }

        void Row(string label, string value, int top)
        {
            host.DrawText(label, x + 10, top, White);
            host.RightText(value, x + 130, top, White);
        }

        void Rule(int left, int top) => host.FillRect(left + 10, top, InfoW - 20, 1, StatusScreen.BoxLine);
    }
}
