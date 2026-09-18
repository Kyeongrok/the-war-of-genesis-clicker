using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 링 커맨드 "어빌" 을 누르면 뜨는 어빌리티 목록 — 익힌 어빌리티마다 지금 레벨 work 의 TP·SOUL 비용을 보이고,
/// 고르면 노란 사거리 칸에서 대상을 고르게 한다(<see cref="OnTargetClick"/>).
/// </summary>
internal sealed unsafe partial class BattleSceneWindow
{
    private bool _abilityMenu;
    private int _abilityHover = -1;

    /// <summary>줄 단축키 — 앞에서부터 1·2·3·4·Q·W·E·R, 여덟 줄이 넘으면 단축키가 없다.</summary>
    private static readonly int[] AbilityHotkeys = ['1', '2', '3', '4', 'Q', 'W', 'E', 'R'];

    private static string HotkeyLabel(int index) =>
        index < AbilityHotkeys.Length ? ((char)AbilityHotkeys[index]).ToString() : "";

    /// <summary>목록이 열려 있을 때 키를 처리한다 — 단축키면 그 줄을 고른다.</summary>
    private bool OnAbilityMenuKey(int key)
    {
        if (!_abilityMenu || _turn < 0) return false;
        int index = Array.IndexOf(AbilityHotkeys, key);
        if (index < 0) return false;
        var rows = MenuRows();
        if (index >= rows.Count) return true;
        var (ox, oy) = MenuOrigin(rows.Count);
        return OnAbilityMenuClick(ox + MenuRowX + 10, oy + MenuHeadH + index * MenuRowH + 4);
    }

    private void UpdateAbilityHover(int bx, int by)
    {
        if (!_abilityMenu || _turn < 0) { _abilityHover = -1; return; }
        var rows = MenuRows();
        var (ox, oy) = MenuOrigin(rows.Count);
        int row = (by - oy - MenuHeadH) / MenuRowH;
        _abilityHover = bx >= ox + MenuRowX && bx < ox + MenuRowX + MenuRowW && row >= 0 && row < rows.Count ? row : -1;
    }
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
                : u.Tp + u.Ctp < TpCostFor(u, c, wid) ? "TP 부족"
                : u.Soul < SoulNeedFor(u, c, wid) ? "SOUL 부족" : "";
            rows.Add(($"{_db.T(ab.NameId)} Lv{level}", w, reason.Length == 0, reason));
        }
        return rows;
    }

    private string AbilityName(WorkData w) =>
        _db != null && _db.Abilities.TryGetValue(w.AbilityId, out var ab) ? $"{_db.T(ab.NameId)} Lv{w.Level}" : $"work {w.Id}";

    /// <summary>그 어빌리티가 지금 자리에서 겨눌 수 있는 적 — HP 가 낮은 순.</summary>
    private List<int> AbilityTargets(WorkData w)
    {
        var user = _units[_turn];
        return [.. Enumerable.Range(0, _units.Length)
            .Where(i => _units[i].Alive && !_units[i].IsAlly
                        && InWorkRange(w, user.Col, user.Row, _units[i].Col, _units[i].Row, user))
            .OrderBy(i => _units[i].Hp).ThenBy(i => i)];
    }

    private (int X, int Y) MenuOrigin(int rowCount)
    {
        // 차례가 끝난 뒤에도 창이 남아 있을 수 있어 차례가 없으면 판 가운데로 잡는다.
        var (fx, fy) = _turn >= 0 && _turn < _units.Length ? UnitFoot(_units[_turn]) : (BoardWidth / 2, _camY + ViewHeight / 2);
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

        // 적 하나를 겨누는 어빌리티는 기본공격처럼 사거리 안 가장 약한 적을 먼저 노려 준다(fa-12 와 같은 규칙).
        if (w.TargetMode == 1 && AbilityTargets(w) is { Count: > 0 } aimed)
        {
            _attackCursor = aimed[0];
            Toast($"{name} — {UnitName(_attackCursor)} 을(를) 노립니다 (Enter: 쓰기, Tab: 다른 적, 우클릭·Esc 취소)");
            return true;
        }
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
            // 줄 바탕(Obs 0471 모션 20)은 마우스를 올린 줄에만 — 원본도 올린 줄 하나만 덧그린다.
            if (i == _abilityHover) DrawUi(ListRowObs, 20, 0, rx, y, UiBlend.Alpha, loop: false);
            // 단축키 글자는 아이콘과 겹치지 않게 줄 오른쪽 끝에
            string hotkey = HotkeyLabel(i);
            if (hotkey.Length > 0) DrawText(hotkey, rx + MenuRowW - 18, y + 4, i == _abilityHover ? White : DimGray, 12);
            // 줄 왼쪽에 아이콘 둘 — 종류(攻·回·異·軍·必)와 대상(한 사람·두 사람), 원본은 (14, 줄높이/2)·(34, …)
            if (_db.Abilities.TryGetValue(w.AbilityId, out var ab) && ab.IconKindMotion >= 0)
            {
                DrawUi(AbilityIconObs, ab.IconKindMotion, 0, rx + 14, y + MenuRowH / 2, UiBlend.Alpha);
                DrawUi(AbilityIconObs, ab.IconTargetMotion, 0, rx + 34, y + MenuRowH / 2, UiBlend.Alpha);
            }
            DrawText(name, rx + 46, y + 4, color, 12);
            if (!enabled) DrawText(reason, rx + 120, y + 4, Red, 11);
            // 18·20(소울·TP 소모량 변화)까지 얹은 값을 보여 준다 — 실제로 물리는 값과 같아야 한다.
            RightText($"{TpCostFor(_units[_turn], c, w.Id)}", rx + 210, y + 4, color, 12);
            // 체질마다 실제로 깎이는 SOUL 이 다르다(분석-전투 ba-4) — 필요한 값을 보여 준다.
            RightText($"{SoulNeedFor(_units[_turn], c, w.Id)}", rx + 240, y + 4, color, 12);
        }
    }
}
