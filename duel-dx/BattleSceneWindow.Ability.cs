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
        _targetHotkey = key;                       // 같은 키를 한 번 더 누르면 겨눈 대상에게 쓴다
        bool handled = OnAbilityMenuClick(ox + MenuRowX + 10, oy + MenuHeadH + index * MenuRowH + 4);
        if (_targetWork < 0) _targetHotkey = -1;
        return handled;
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
    // 원본에 없는 「소모」(실제로 깎이는 SOUL) 칸을 SOUL 오른쪽에 더해 창을 40 넓혔다(사용자 요청).
    private const int MenuW = 344, MenuRowH = 24, MenuHeadH = 26, MenuRowW = 320, MenuRowX = 12;

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
            // 전투 목록(0x10032760)은 분류 1(전투)·4 만 보여 준다(분석-스킬) — 비전투 수련(0, 발키리의 혼 따위)·군단기(2)·
            // 장착형 패시브(3, PSY증가 따위)는 안 나온다.
            if (ab.Category is not (1 or 4)) continue;
            string reason = w.Kind == 4 ? "쓸 수 없음"
                : u.Tp + u.Ctp < TpCostFor(u, c, wid) ? "TP 부족"
                : u.Soul < SoulNeedFor(u, c, wid) ? "SOUL 부족" : "";
            rows.Add(($"{_db.T(ab.NameId)} Lv{level}", w, reason.Length == 0, reason));
        }
        // 군단기 — 배속된 군단(CChr+0x1c)의 기술 다섯 칸 가운데 필요 세력을 채우고 대장 조건(0 이거나 나)이 맞는 것(0x10032760 뒷부분, 분석-군단 4.4).
        // 부하가 <b>다 살아 있을 때만</b> 줄이 켜진다(0x100d58c2: 부하 수 == 군단 부하 칸 수). 레벨은 늘 1.
        if (_unitLegion.TryGetValue(u.ChrCode, out int legionId) && Legions().TryGetValue(legionId, out var legion))
        {
            int alive = FollowersOf(_turn).Count, slots = legion.Members.Count(m => m != 0);
            foreach (var (abilityId, power, leader) in legion.Skills)
            {
                if (abilityId == 0 || power > 1000 || (leader != 0 && leader != u.ChrCode)) continue;
                if (!_db.Abilities.TryGetValue(abilityId, out var ab) || !ab.WorkByLevel.TryGetValue(1, out int wid) || Work(wid) is not { } w) continue;
                string reason = alive < slots ? "부하가 모자람"
                    : u.Tp + u.Ctp < TpCostFor(u, c, wid) ? "TP 부족"
                    : u.Soul < SoulNeedFor(u, c, wid) ? "SOUL 부족" : "";
                rows.Add(($"{_db.T(ab.NameId)}", w, reason.Length == 0, reason));
            }
        }
        return rows;
    }

    private string AbilityName(WorkData w) =>
        _db != null && _db.Abilities.TryGetValue(w.AbilityId, out var ab) ? $"{_db.T(ab.NameId)} Lv{w.Level}" : $"work {w.Id}";

    /// <summary>그 어빌리티가 지금 자리에서 겨눌 수 있는 적 — HP 가 낮은 순.</summary>
    /// <summary>어빌리티 사거리 안의 적 — <b>가까운 순, 같으면 HP 낮은 순</b>(단축키 자동 조준의 우선순위).</summary>
    private List<int> AbilityTargets(WorkData w)
    {
        var user = _units[_turn];
        // 아군을 겨누는 기술(힐 따위, 대상 방식 4)은 <b>자기까지 포함한 아군</b> 가운데 HP 비율이 가장 낮은 쪽부터 —
        // 전에는 적만 찾아 힐을 단축키로 고르면 아무도 안 겨눠, 한 번 더 눌러도 자기에게 못 썼다(사용자 보고).
        if (w.TargetMode == 4)
            return [.. Enumerable.Range(0, _units.Length)
                .Where(i => _units[i].Alive && _units[i].OnField && ModeAccepts(4, user, _units[i])
                            && InWorkRange(w, user.Col, user.Row, _units[i].Col, _units[i].Row, user))
                .OrderBy(i => _units[i].Hp * 1000L / Math.Max(1, _units[i].MaxHp)).ThenBy(i => i != _turn).ThenBy(i => i)];
        return [.. Enumerable.Range(0, _units.Length)
            .Where(i => _units[i].Alive && _units[i].OnField && SeesAsFoe(user, _units[i])
                        && InWorkRange(w, user.Col, user.Row, _units[i].Col, _units[i].Row, user))
            .OrderBy(i => Math.Abs(_units[i].Col - user.Col) + Math.Abs(_units[i].Row - user.Row))
            .ThenBy(i => _units[i].Hp).ThenBy(i => i)];
    }

    /// <summary>어빌리티를 고른 단축키 — 같은 키를 한 번 더 누르면 겨눈 대상에게 바로 쓴다.</summary>
    private int _targetHotkey = -1;

    /// <summary>칸을 고르는 어빌리티(범위 공격 따위)가 저절로 겨눈 칸 — 사거리 안 가장 가까운(같으면 약한) 적이 선 칸.</summary>
    private (int Col, int Row)? _aimCell;

    /// <summary>
    /// 단축키로 고른 어빌리티에 대상을 저절로 붙인다. 적 하나를 겨누는 것은 공격 커서로, 칸을 고르는 것은 적이 선 칸으로.
    /// 우선순위는 가까운 적, 그다음 약한 적이다.
    /// </summary>
    private void AutoAimAbility(WorkData w, string name)
    {
        var user = _units[_turn];
        var targets = AbilityTargets(w).Where(i => CanAimAt(w, user, _units[i].Col, _units[i].Row)).ToList();
        if (targets.Count == 0) return;
        string again = _targetHotkey >= 0 ? $"{(char)_targetHotkey} 한 번 더·" : "";
        if (w.TargetMode == 1)
        {
            _attackCursor = targets[0];
            Hint($"{name} — {UnitName(_attackCursor)} 을(를) 노립니다 ({again}Enter: 쓰기, Tab: 다른 적, 우클릭·Esc 취소)");
            return;
        }
        var t = _units[targets[0]];
        _aimCell = (t.Col, t.Row);
        Hint($"{name} — {UnitName(targets[0])} 이(가) 선 칸을 겨눕니다 ({again}Enter: 쓰기, 다른 칸 클릭, 우클릭·Esc 취소)");
    }

    /// <summary>같은 단축키를 한 번 더 누르거나 Enter — 저절로 겨눈 대상에게 쓴다. 처리했으면 true.</summary>
    private bool UseAimedAbility()
    {
        if (_targetWork < 0 || _targetIsBasicAttack || !IsPlayerTurn) return false;
        if (_attackCursor >= 0) { AttackCursorTarget(); return true; }
        if (_aimCell is { } cell) { OnTargetClick(cell.Col, cell.Row); return true; }
        return false;
    }

    private (int X, int Y) MenuOrigin(int rowCount)
    {
        // 차례가 끝난 뒤에도 창이 남아 있을 수 있어 차례가 없으면 판 가운데로 잡는다.
        var (fx, fy) = _turn >= 0 && _turn < _units.Length ? UnitFoot(_units[_turn]) : (_camX + ViewWidth / 2, _camY + ViewHeight / 2);
        int h = MenuHeadH + Math.Max(1, rowCount) * MenuRowH + 8;
        int x = fx + TileW < _camX + ViewWidth - MenuW - 8 ? fx + TileW : fx - TileW - MenuW;
        return (Math.Clamp(x, _camX + 8, _camX + ViewWidth - MenuW - 8), Math.Clamp(fy - h / 2, _camY + GridTop + 8, _camY + ViewHeight - h - 8));
    }

    /// <summary>목록이 열려 있으면 클릭을 처리하고 true — 왼쪽 클릭은 바로 고르고, 목록 밖을 누르면 닫는다. 설명은 오른쪽 단추를 누르고 있는 동안 보인다.</summary>
    private bool OnAbilityMenuClick(int bx, int by)
    {
        if (!_abilityMenu) return false;
        if (!IsPlayerTurn) { _abilityMenu = false; return true; }

        var rows = MenuRows();
        var (ox, oy) = MenuOrigin(rows.Count);
        int index = (by - oy - MenuHeadH) / MenuRowH;
        if (bx < ox || bx >= ox + MenuW || by < oy + MenuHeadH || index >= rows.Count) { _abilityMenu = false; CancelTargeting(refund: true); return true; }
        SelectAbilityRow(rows[index]);
        return true;
    }

    /// <summary>오른쪽 단추를 누른 채 있는 줄(설명이 보이는 줄) — 없으면 −1. 단추를 떼면(WM_RBUTTONUP) −1 로 돌아간다.</summary>
    private int _abilityPressed = -1;

    /// <summary>목록 줄 위에서 오른쪽 단추를 누르면 — 누르고 있는 동안 그 어빌리티의 설명이 보인다(사용자 요청). 처리했으면 true.</summary>
    private bool OnAbilityMenuRightDown(int bx, int by)
    {
        if (!_abilityMenu) return false;
        var rows = MenuRows();
        var (ox, oy) = MenuOrigin(rows.Count);
        int index = (by - oy - MenuHeadH) / MenuRowH;
        if (bx < ox || bx >= ox + MenuW || by < oy + MenuHeadH || index >= rows.Count) return false;
        _abilityPressed = index;
        return true;
    }

    /// <summary>목록의 한 줄을 고른다 — 못 쓰는 줄이면 까닭만 알리고 창은 그대로 둔다.</summary>
    private void SelectAbilityRow((string Name, WorkData Work, bool Enabled, string Reason) row)
    {
        var (name, w, enabled, reason) = row;
        if (!enabled) { Toast($"{name}: {reason}"); return; }
        _abilityMenu = false;
        _targetWork = w.Id;
        _targetIsBasicAttack = false;
        if (UseSelfCentredWork(w)) { Toast(name); return; }

        // 사거리 안의 적을 저절로 겨눈다 — 가까운 적, 그다음 약한 적. 적 하나짜리는 커서로, 칸 고르기는 그 적의 칸으로.
        AutoAimAbility(w, name);
        if (_attackCursor >= 0 || _aimCell != null) return;
        Hint($"{name} — 노란 칸 안의 대상을 클릭하세요 (우클릭·Esc 취소)");
    }

    /// <summary>
    /// 칸을 고르는 어빌리티(메테오처럼 대상 방식이 한 명이 아닌 것)를 겨누는 동안 마우스 아래 칸이 사거리 안이면 그 칸을 겨눈 것으로 —
    /// 원본은 커서가 움직일 때 그 칸의 효과 범위(층 0 주황)를 다시 깐다(<c>0x1006d581</c>). 전에는 저절로 겨눈 칸만 있어 범위가 안 따라왔다.
    /// </summary>
    private void UpdateAimHover(int bx, int by)
    {
        if (_targetWork < 0 || _targetIsBasicAttack || _turn < 0 || _abilityMenu || Work(_targetWork) is not { } w || w.TargetMode == 1) return;
        if (by < GridTop) return;
        int col = bx / TileW, row = RowAt(bx, by);
        if (row < 0 || !CanAimAt(w, _units[_turn], col, row)) return;
        _aimCell = (col, row);
        _attackCursor = -1;
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
        // 머리 글자는 아래 숫자 칸의 오른끝에 하나씩 맞춘다 — 한 덩이로 창 오른끝에 붙였더니 단축키 칸까지 밀려 한 칸씩 어긋났다(사용자 보고).
        // 두 칸 오른끝이 30픽셀밖에 안 떨어져 그대로 맞추면 「TPSOUL」로 붙는다 — TP 는 조금 왼쪽, SOUL 은 조금 오른쪽으로 띄운다.
        RightText("TP", ox + MenuRowX + 204, oy + 3, White, 12);
        RightText("SOUL", ox + MenuRowX + 248, oy + 3, White, 12);
        RightText("소모", ox + MenuRowX + 286, oy + 3, White, 12);

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
            // 못 쓰는 줄은 흐린 글자로만 알린다 — 「TP 부족」 같은 까닭 글은 이름과 겹치고 어차피 안다(사용자 요청). 누르면 알림으로 까닭을 준다.
            // 18·20(소울·TP 소모량 변화)까지 얹은 값을 보여 준다 — 실제로 물리는 값과 같아야 한다.
            RightText($"{TpCostFor(_units[_turn], c, w.Id)}", rx + 210, y + 4, color, 12);
            // 체질마다 실제로 깎이는 SOUL 이 다르다(분석-전투 ba-4) — 필요한 값을 보여 준다.
            RightText($"{SoulNeedFor(_units[_turn], c, w.Id)}", rx + 240, y + 4, color, 12);
            // 쓰면 실제로 깎이는 SOUL(소모량) — 필요 SOUL 과 다를 수 있다(18 소울 소모량 % 보정까지 얹은 값).
            RightText($"{SoulCostFor(_units[_turn], c, w.Id)}", rx + 280, y + 4, color, 12);
        }

        // 오른쪽 단추를 누르고 있는 줄의 설명(abi +0x1c 설명 TXR) — 스테이터스와 같은 설명 창(0x10042c00): 마우스 + (16,16), 제목줄 없음, 글 가운데.
        if (_abilityPressed >= 0 && _abilityPressed < rows.Count
            && _db.Abilities.TryGetValue(rows[_abilityPressed].Work.AbilityId, out var pressed) && _db.T(pressed.DescriptionId) is { Length: > 0 } desc)
            DrawDescriptionTip(desc + AbilityEffectText(pressed, rows[_abilityPressed].Work.Level), _mouse.X, _mouse.Y, _camX, _camY, ViewWidth, ViewHeight);
    }
}
