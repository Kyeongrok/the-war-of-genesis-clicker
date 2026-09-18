using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 링 커맨드 "어빌" 을 누르면 뜨는 어빌리티 목록 — 익힌 어빌리티마다 지금 레벨 work 의 TP·SOUL 비용을 보이고,
/// 고르면 노란 사거리 칸에서 대상을 고르게 한다(<see cref="OnTargetClick"/>).
/// </summary>
internal sealed unsafe partial class BattleSceneWindow
{
    private bool _abilityMenu;
    // 원본 목록(분석-스킬 ba-12): 창 바깥 304, 줄 280×24 여덟 줄, 이름 x=46 · TP x=210 · SOUL x=240(오른쪽 맞춤).
    private const int MenuW = 304, MenuRowH = 24, MenuHeadH = 26, MenuRowW = 280, MenuRowX = 12;

    /// <summary>어빌리티 목록 줄 아이콘 — 분석-스킬 ba-12: Obs 0488 의 17×17 그림 24장(기준점 가운데).</summary>
    private const int AbilityIconObs = 488;

    private List<(string Name, WorkData Work, bool Enabled, string Reason)> MenuRows()
    {
        var rows = new List<(string, WorkData, bool, string)>();
        if (_turn < 0 || _db == null || _units[_turn].Data is not { } c) return rows;
        var u = _units[_turn];
        foreach (var (abilityId, level) in c.Abilities.OrderBy(a => a.Ability))
        {
            if (!_db.Abilities.TryGetValue(abilityId, out var ab) || !ab.WorkByLevel.TryGetValue(level, out int wid) || Work(wid) is not { } w) continue;
            string reason = w.Kind == 4 ? "쓸 수 없음"
                : u.Tp + u.Ctp < _db.WorkTpCost(c, wid) ? "TP 부족"
                : u.Soul < w.SoulBase ? "SOUL 부족" : "";
            rows.Add(($"{_db.T(ab.NameId)} Lv{level}", w, reason.Length == 0, reason));
        }
        return rows;
    }

    private string AbilityName(WorkData w) =>
        _db != null && _db.Abilities.TryGetValue(w.AbilityId, out var ab) ? $"{_db.T(ab.NameId)} Lv{w.Level}" : $"work {w.Id}";

    private (int X, int Y) MenuOrigin(int rowCount)
    {
        var (fx, fy) = UnitFoot(_units[_turn]);
        int h = MenuHeadH + Math.Max(1, rowCount) * MenuRowH + 8;
        int x = fx + TileW < BoardWidth - MenuW - 8 ? fx + TileW : fx - TileW - MenuW;
        return (Math.Clamp(x, 8, BoardWidth - MenuW - 8), Math.Clamp(fy - h / 2, _camY + GridTop + 8, _camY + ViewHeight - h - 8));
    }

    /// <summary>목록이 열려 있으면 클릭을 처리하고 true. 목록 밖을 누르면 닫는다.</summary>
    private bool OnAbilityMenuClick(int bx, int by)
    {
        if (!_abilityMenu) return false;
        _abilityMenu = false;
        if (!IsPlayerTurn) return true;

        var rows = MenuRows();
        var (ox, oy) = MenuOrigin(rows.Count);
        int index = (by - oy - MenuHeadH) / MenuRowH;
        if (bx < ox || bx >= ox + MenuW || by < oy + MenuHeadH || index >= rows.Count) { CancelTargeting(refund: true); return true; }

        var (name, w, enabled, reason) = rows[index];
        if (!enabled) { Toast($"{name}: {reason}"); _abilityMenu = true; return true; }
        _targetWork = w.Id;
        _targetIsBasicAttack = false;
        if (UseSelfCentredWork(w)) { Toast(name); return true; }
        Toast($"{name} — 노란 칸 안의 대상을 클릭하세요 (우클릭·Esc 취소)");
        return true;
    }

    private void DrawAbilityMenu()
    {
        if (!_abilityMenu || _turn < 0 || _db == null) return;
        var rows = MenuRows();
        var (ox, oy) = MenuOrigin(rows.Count);
        int h = MenuHeadH + Math.Max(1, rows.Count) * MenuRowH + 8;

        // 창은 게임 안 모든 창과 같은 원본 틀(분석-시스템메뉴 「메시지 창 틀」)
        DarkenRect(ox - 1, oy - FrameTitleH - 1, MenuW + 2, h + FrameTitleH + 2, 8);
        DrawGameFrame(ox, oy, MenuW, h, $"{_db.T(3)} — {UnitName(_turn)}");
        RightText("TP  SOUL", ox + MenuW - 8, oy + 3, White, 12);

        if (rows.Count == 0) DrawText("익힌 어빌리티가 없습니다", ox + 10, oy + MenuHeadH + 4, DimGray);
        var c = _units[_turn].Data!;
        for (int i = 0; i < rows.Count; i++)
        {
            var (name, w, enabled, reason) = rows[i];
            int y = oy + MenuHeadH + i * MenuRowH;
            uint color = enabled ? White : DimGray;
            int rx = ox + MenuRowX;
            DrawUi(ListRowObs, 20, 0, rx, y, UiBlend.Alpha, loop: false);   // 원본 줄 바탕(280×24)
            // 줄 왼쪽에 아이콘 둘 — 종류(攻·回·異·軍·必)와 대상(한 사람·두 사람), 원본은 (14, 줄높이/2)·(34, …)
            if (_db.Abilities.TryGetValue(w.AbilityId, out var ab) && ab.IconKindMotion >= 0)
            {
                DrawUi(AbilityIconObs, ab.IconKindMotion, 0, rx + 14, y + MenuRowH / 2, UiBlend.Alpha);
                DrawUi(AbilityIconObs, ab.IconTargetMotion, 0, rx + 34, y + MenuRowH / 2, UiBlend.Alpha);
            }
            DrawText(name, rx + 46, y + 4, color, 12);
            if (!enabled) DrawText(reason, rx + 120, y + 4, Red, 11);
            RightText($"{_db.WorkTpCost(c, w.Id)}", rx + 210, y + 4, color, 12);
            RightText($"{w.SoulBase}", rx + 240, y + 4, color, 12);
        }
    }
}
