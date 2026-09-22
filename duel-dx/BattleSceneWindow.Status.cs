using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// Status 화면 — 게임 화면(죠안 캡처) 칸 구성을 640×480 에 옮기고, 아군이면 장비 바꾸기(st-1)·장착 어빌리티 바꾸기(st-2)·
/// 어빌리티 올리기/배우기(st-3)를 할 수 있다.
/// </summary>
/// <remarks>
/// 규칙은 옵시디안 분석-캐릭터 "Status 편집 (구현 st-1·st-2·st-3 용)":
/// <list type="bullet">
/// <item>장비: 무기 칸은 캐릭터 무기 종류와 같은 아이템만, 나머지 칸은 아이템 종류로 정해진다. 맨 위 줄 "해제".
///   뺀 아이템은 파티 가방에 +1, 낀 아이템은 −1. TP 는 안 쓴다.</item>
/// <item>장착 어빌리티: 칸 수 = Dep.dat 레코드 바이트 4. 배운 패시브(분류 3) 중 다른 칸에 없는 것을 고른다(빈 줄 없음).</item>
/// <item>어빌리티 올리기: 빨간 숫자(그 레벨 work 의 EXP)만큼 EXP 를 쓰고 레벨 +1. 배우기는 Lv1 work 의 EXP 를 쓰고,
///   선행 어빌리티 1 을 지운다. EXP 가 모자라면 못 누른다.</item>
/// </list>
/// 이 데모에는 저장 파일이 없어서 파티 가방은 <see cref="FillDemoInventory"/> 가 채우고, 아군 EXP 는 <see cref="DemoExp"/> 로 시작한다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int StatusW = 640, StatusH = 480;
    private const uint PanelBg = 0xF00A1428, BoxBg = 0xFF13203A, BoxLine = 0xFF3C5C98, HeadBg = 0xFF34549A, Red = 0xFFE04040, Bar = 0xFF7A1C1C;
    /// <summary>
    /// 어빌리티를 올리는 데 쓰는 EXP 의 시작값. 원본은 <b>0</b> 에서 시작해 처치로만 번다(분석-캐릭터 an-3) —
    /// 예전에는 데모라 5000 을 줬는데 너무 넉넉해서 원본대로 되돌렸다. 시험할 때는 <c>DUELDX_EXP</c> 로 올린다.
    /// </summary>
    private static int DemoExp =>
        int.TryParse(Environment.GetEnvironmentVariable("DUELDX_EXP"), out int exp) ? exp : 0;

    /// <summary>아이템 그림과 WEAPON 띠가 들어 있는 Obs(분석-캐릭터 "아이템 그림").</summary>
    private const int ItemPictureObs = 326;

    /// <summary>파티 가방 — 아이템 번호 → 개수.</summary>
    private readonly SortedDictionary<int, int> _inventory = [];

    private readonly List<(int X, int Y, int W, int H, Action Click)> _statusHits = [];
    private string _popupTitle = "";
    private List<(string Label, string Right, bool Enabled, Action Apply)>? _popup;
    private const int PopupW = 320, PopupRowH = 24;

    private (int X, int Y) StatusOrigin() => (_camX + (ViewWidth - StatusW) / 2, _camY + GridTop + (ViewHeight - GridTop - StatusH) / 2);

    /// <summary>데모 파티 가방 — 아군 무기 종류마다 무기 3개, 갑옷·신발·벨트·반지·목걸이 종류마다 4개씩(번호 순, 이름 있는 것).</summary>
    private void FillDemoInventory()
    {
        if (_db == null) return;
        var types = _units.Where(u => u.IsAlly && u.Data != null).Select(u => _db.WeaponTypeOf(u.Data!)).Distinct()
            .Select(t => (Type: t, Count: 3)).Concat(new[] { 2, 3, 4, 5, 6 }.Select(t => (Type: t, Count: 4)))
;
        foreach (var (type, count) in types)
            foreach (var item in _db.Items.Values.Where(i => i.Type == type && _db.T(i.NameId).Length > 0).OrderBy(i => i.Id).Take(count))
                _inventory[item.Id] = _inventory.GetValueOrDefault(item.Id) + 1;

        // 전투에서 쓰는 캡슐(종류 7)도 몇 개 — 회복 캡슐 셋과 공격 캡슐 셋(분석-전투 「전투 중 아이템 쓰기」)
        var capsules = _db.Items.Values.Where(i => i.IsConsumable && _db.T(i.NameId).Length > 0).ToList();
        foreach (var item in capsules.Where(i => Work(i.UseWork) is { IsHeal: true }).OrderBy(i => i.Id).Take(3)
                                     .Concat(capsules.Where(i => Work(i.UseWork) is { IsHeal: false }).OrderBy(i => i.Id).Take(3)))
            _inventory[item.Id] = _inventory.GetValueOrDefault(item.Id) + 2;
    }

    /// <summary>장비·어빌리티가 바뀐 뒤 최대치들을 다시 셈한다(현재 HP·TP·SOUL 은 최대를 넘지 않게만).</summary>
    private void RefreshUnitStats(UnitState u)
    {
        if (_db == null || u.Data is not { } c) return;
        // 군단 부하는 대장 세력만큼 LP·PSY·DEP 가 오른다(분석-군단 1절).
        var (lp, psy, dep) = LegionBonusFor(u);
        if (lp != 0 || psy != 0 || dep != 0) c = c with { Lp = (uint)Math.Max(0, c.Lp + lp), Psy = (ushort)Math.Max(0, c.Psy + psy), Dep = (ushort)Math.Max(0, c.Dep + dep) };
        // 상태이상 30~48 은 능력치에 바로 더한다(분석-전투 6절) — 최대치 셋만 여기서 반영한다.
        u.MaxHp = Math.Max(1, _db.MaxHp(c) + u.BonusMaxHp);
        u.MaxTp = _db.MaxTp(c) + u.BonusMaxTp;
        u.Stp = Math.Max(1, _db.Stp(c));
        u.MaxSoul = _db.MaxSoul(c) + u.BonusMaxSoul;
        u.Hp = Math.Min(u.Hp, u.MaxHp);
        u.Tp = Math.Min(u.Tp, u.MaxTp);
        u.Soul = Math.Min(u.Soul, u.MaxSoul);
    }

    private string AbilityLabel(AbilityData ab, int level) => $"{_db!.T(ab.NameId)} Lv{level}";

    // ── 클릭 ─────────────────────────────────────────────────────────────────

    /// <summary>모세스 전직 화면에서 전투에 안 선 파티원의 스테이터스를 열 때의 자리 번호 — <see cref="_statusVirtual"/> 을 보인다.</summary>
    private const int VirtualStatus = 1_000_000;

    /// <summary>전투 판에 없는 파티원을 위한 임시 유닛 — 창을 닫으면 <see cref="SyncVirtualStatus"/> 가 자료를 파티에 되돌려 적는다.</summary>
    private UnitState? _statusVirtual;

    private UnitState StatusUnit() => _statusUnit == VirtualStatus && _statusVirtual != null ? _statusVirtual : _units[_statusUnit];

    /// <summary>파티원(Chr)의 스테이터스 창을 연다 — 전투에 서 있으면 그 유닛, 아니면 파티 자료로 임시 유닛을 만든다.</summary>
    private void OpenStatusFor(int chr)
    {
        int index = Array.FindIndex(_units, u => u.ChrCode == chr);
        if (index >= 0) { _statusUnit = index; return; }
        if (_db is not { } db || _party.GetValueOrDefault(chr) is not { } c) return;
        var unit = new UnitState(new DemoUnit(chr, 0, 0, 4, 0, Facing.Left)) { Data = c };
        unit.MaxHp = unit.Hp = Math.Max(1, db.MaxHp(c));
        unit.MaxTp = db.MaxTp(c);
        unit.Stp = Math.Max(1, db.Stp(c));
        unit.MaxSoul = db.MaxSoul(c);
        unit.Soul = db.SoulStart;
        LoadFieldFace(c);
        _statusVirtual = unit;
        _statusUnit = VirtualStatus;
    }

    /// <summary>임시 유닛으로 연 창이 닫혔으면 바뀐 자료(어빌리티 레벨·장비)를 파티에 되돌려 적는다 — 매 틀 부른다.</summary>
    private void SyncVirtualStatus()
    {
        if (_statusVirtual is not { } v || _statusUnit == VirtualStatus) return;
        if (v.Data is { } c) _party[v.ChrCode] = c;
        _statusVirtual = null;
    }

    private bool OnStatusClick(int bx, int by)
    {
        if (_statusUnit < 0) return false;
        var (ox, oy) = StatusOrigin();

        if (_popup != null)
        {
            var (px, py, _) = PopupRect();
            int row = (by - py - 30) / PopupRowH;
            if (bx >= px && bx < px + PopupW && by >= py + 30 && row < _popup.Count)
            {
                var (_, _, enabled, apply) = _popup[row];
                if (!enabled) return true;
                _popup = null;
                apply();
            }
            else _popup = null;
            return true;
        }

        bool inside = bx >= ox && by >= oy - FrameTitleH && bx < ox + StatusW && by < oy + StatusH;
        // 닫기는 제목줄 오른쪽 X 단추(원본 자리 창폭−24, −24, 18×18)
        bool close = bx >= ox + StatusW - 24 && bx < ox + StatusW - 6 && by >= oy - 24 && by < oy - 6;
        if (close || !inside) { _statusUnit = -1; return true; }

        foreach (var (x, y, w, h, click) in _statusHits)
            if (bx >= x && bx < x + w && by >= y && by < y + h) { click(); break; }
        return true;
    }

    private void OpenPopup(string title, List<(string, string, bool, Action)> rows)
    {
        _popupTitle = title;
        _popup = rows;
    }

    private (int X, int Y, int H) PopupRect()
    {
        var (ox, oy) = StatusOrigin();
        int h = 30 + (_popup?.Count ?? 0) * PopupRowH + 8;
        return (ox + (StatusW - PopupW) / 2, oy + Math.Max(40, (StatusH - h) / 2), h);
    }

    /// <summary>st-1: 장비 칸을 누르면 "해제" + 그 칸에 맞는 가방 아이템 목록.</summary>
    private void ChooseEquipment(UnitState u, int slot)
    {
        var db = _db!;
        var c = u.Data!;
        var rows = new List<(string, string, bool, Action)>();
        if (c.Items[slot] != 0) rows.Add(("해제", "", true, () => SetEquipment(u, slot, 0)));
        foreach (var (id, count) in _inventory)
        {
            if (count <= 0 || !db.Items.TryGetValue(id, out var item) || !db.FitsSlot(c, item, slot)) continue;
            string stat = slot == 0 ? $"공격 {item.Attack}" : item.Defense > 0 ? $"방어 {item.Defense}" : "";
            rows.Add(($"{db.T(item.NameId)} ×{count}", stat, true, () => SetEquipment(u, slot, (ushort)id)));
        }
        if (rows.Count == 0) { Toast("가방에 이 칸에 낄 아이템이 없습니다"); return; }
        OpenPopup("장비 바꾸기", rows);
    }

    private void SetEquipment(UnitState u, int slot, ushort itemId)
    {
        var c = u.Data!;
        var items = (ushort[])c.Items.Clone();
        if (items[slot] != 0) _inventory[items[slot]] = _inventory.GetValueOrDefault(items[slot]) + 1;
        if (itemId != 0 && (_inventory[itemId] -= 1) <= 0) _inventory.Remove(itemId);
        items[slot] = itemId;
        u.Data = c with { Items = items };
        RefreshUnitStats(u);
    }

    /// <summary>st-2: 장착 어빌리티 칸 — 배운 패시브 중 다른 칸에 없는 것.</summary>
    private void ChoosePassive(UnitState u, int slot)
    {
        var db = _db!;
        var c = u.Data!;
        var rows = new List<(string, string, bool, Action)>();
        foreach (var (id, level) in c.Abilities.OrderBy(a => a.Ability))
        {
            if (!db.Abilities.TryGetValue(id, out var ab) || !ab.IsPassive || c.Passives.Contains(id)) continue;
            rows.Add((AbilityLabel(ab, level), "", true, () =>
            {
                var passives = (ushort[])u.Data!.Passives.Clone();
                passives[slot] = id;
                u.Data = u.Data with { Passives = passives };
                RefreshUnitStats(u);
            }));
        }
        if (rows.Count == 0) { Toast("장착할 패시브 어빌리티를 배우지 않았습니다"); return; }
        OpenPopup("장착 어빌리티", rows);
    }

    /// <summary>st-3: 배운 어빌리티를 누르면 <b>바로</b> 레벨을 올리고, 배울 수 있는 어빌리티를 누르면 배운다 — 묻지 않는다.</summary>
    private void ConfirmAbility(UnitState u, AbilityData ab, bool learn)
    {
        var c = u.Data!;
        int level = learn ? 0 : c.AbilityLevel(ab.Id);
        int cost = _db!.AbilityExpCost(ab, learn ? 1 : level);
        if (!learn && cost == 0) { Toast("최대 레벨입니다"); return; }
        if (cost > c.Exp) { Toast($"EXP 가 모자랍니다 (필요 {cost}, 있음 {c.Exp})"); return; }
        ApplyAbility(u, ab, learn, cost);
    }

    /// <summary>우클릭 — 어빌리티 레벨을 하나 내리고 그 레벨에 썼던 EXP 를 돌려준다. Lv1 아래로는 안 내린다.</summary>
    private void LowerAbility(UnitState u, AbilityData ab)
    {
        var c = u.Data!;
        int level = c.AbilityLevel(ab.Id);
        if (level <= 1) { Toast("Lv1 아래로는 내릴 수 없습니다"); return; }
        int refund = _db!.AbilityExpCost(ab, level - 1);       // Lv(level−1) → Lv(level) 에 썼던 값
        var list = c.Abilities.ToList();
        int i = list.FindIndex(a => a.Ability == ab.Id);
        list[i] = ((ushort)ab.Id, (ushort)(level - 1));
        u.Data = c with { Abilities = [.. list], Exp = c.Exp + refund };
        RefreshUnitStats(u);
        Toast($"{_db.T(ab.NameId)} Lv{level - 1} — EXP {refund} 돌려받음");
    }

    /// <summary>스테이터스 창 안 우클릭 — 어빌리티 줄이면 레벨을 내린다. 처리했으면 true.</summary>
    private bool OnStatusRightClick(int bx, int by)
    {
        if (_statusUnit < 0 || _popup != null) return false;
        foreach (var (x, y, w, h, click) in _statusRightHits)
            if (bx >= x && bx < x + w && by >= y && by < y + h) { click(); return true; }
        return false;
    }

    private readonly List<(int X, int Y, int W, int H, Action Click)> _statusRightHits = [];

    private void ApplyAbility(UnitState u, AbilityData ab, bool learn, int cost)
    {
        var c = u.Data!;
        var list = c.Abilities.ToList();
        var passives = (ushort[])c.Passives.Clone();
        if (learn)
        {
            list.Add(((ushort)ab.Id, 1));
            // 배우면 선행 어빌리티 1 이 캐릭터에서 지워진다(0x10032a80).
            if (ab.Prereq1 != 0)
            {
                list.RemoveAll(a => a.Ability == ab.Prereq1);
                for (int i = 0; i < passives.Length; i++) if (passives[i] == ab.Prereq1) passives[i] = 0;
            }
        }
        else
        {
            int i = list.FindIndex(a => a.Ability == ab.Id);
            list[i] = ((ushort)ab.Id, (ushort)(list[i].Level + 1));
        }
        u.Data = c with { Abilities = [.. list], Passives = passives, Exp = c.Exp - cost };
        RefreshUnitStats(u);
        Toast(learn ? $"{_db!.T(ab.NameId)} 을(를) 배웠습니다" : $"{_db!.T(ab.NameId)} Lv{u.Data.AbilityLevel(ab.Id)}");
    }

    // ── 그리기 ───────────────────────────────────────────────────────────────

    private void DrawStatusScreen()
    {
        _statusHits.Clear();
        _statusRightHits.Clear();
        if (_statusUnit < 0) return;
        var (ox, oy) = StatusOrigin();
        // 창은 게임 안 모든 창과 같은 원본 틀로(분석-시스템메뉴 「메시지 창 틀」) — 글이 읽히게 밑을 먼저 어둡게 깐다.
        DarkenRect(ox - 1, oy - FrameTitleH - 1, StatusW + 2, StatusH + FrameTitleH + 2, 6);
        DrawGameFrame(ox, oy, StatusW, StatusH, "STATUS");
        if (!DrawUi(FrameObs, 5, 0, ox + StatusW - 24, oy - 24, UiBlend.Alpha))
        {
            FillRect(ox + StatusW - 78, oy + 6, 72, 22, HeadBg);
            DrawText("CLOSE", ox + StatusW - 64, oy + 9, White);
        }

        var unit = StatusUnit();
        if (_db is not { } db || unit.Data is not { } c)
        {
            DrawText("이 인물의 게임 자료(assets/data)를 못 읽었습니다.", ox + 20, oy + 60, Red);
            return;
        }
        // 전투가 끝난 뒤 모세스에서 열면 결과 글이 남아 있어도 고칠 수 있어야 한다.
        bool editable = unit.IsAlly && (_outcome.Length == 0 || _mosesOpen);
        if (editable) DrawText("장비·장착 어빌리티·어빌리티 줄을 누르면 바꿀 수 있습니다 — 어빌리티는 클릭 올리기·우클릭 내리기", ox + 16, oy + 12, DimGray);

        // 1열 — 능력치
        int x = ox + 16, w = 184;
        Header(x, oy + 38, w, db.T(11));
        Box(x, oy + 60, w, 76);
        if (_faces.TryGetValue(unit.ChrCode, out var face)) BlitClipped(face, x + 6, oy + 66, 64, 64);
        StrokeRect(x + 6, oy + 66, 64, 64, BoxLine);
        string[] names = [db.T(c.NameId), db.T(c.TitleId), db.FamilyName(c), db.JobName(c)];
        for (int i = 0; i < names.Length; i++) RightText(names[i], x + w - 8, oy + 64 + i * 17, White);

        Box(x, oy + 140, w, 42);
        Stat(x, oy + 144, w, db.T(160), c.Level.ToString());
        Stat(x, oy + 162, w, db.T(161), c.Exp.ToString());

        Box(x, oy + 186, w, 100);
        StatBar(x, oy + 190, w, db.T(159), unit.Hp, unit.MaxHp);
        StatBar(x, oy + 222, w, db.T(41), unit.Soul, unit.MaxSoul);
        StatBar(x, oy + 254, w, db.T(38), unit.Tp, unit.MaxTp);

        Box(x, oy + 290, w, 60);
        Stat(x, oy + 294, w, db.T(156), db.Atk(c, unit.Soul).ToString());
        Stat(x, oy + 312, w, db.T(157), db.Acr(c, unit.Tp).ToString());
        Stat(x, oy + 330, w, db.T(158), db.Rdp(c, unit.Hp, unit.MaxHp).ToString());

        Box(x, oy + 354, w, 112);
        (ushort Id, int Value)[] basics = [(34, (int)c.Lp + db.EquipBonus(c, 0x30)), (39, c.Ctp), (40, db.Stp(c)), (35, db.Psy(c)), (37, db.Dep(c)), (36, db.Dex(c))];
        for (int i = 0; i < basics.Length; i++) Stat(x, oy + 358 + i * 18, w, db.T(basics[i].Id), basics[i].Value.ToString());

        // 2열 — 상태이상 · 장착 어빌리티 · 장비
        x = ox + 216; w = 184;
        Header(x, oy + 38, w, db.T(163));
        Box(x, oy + 60, w, 38);
        // 칸 셋은 원본대로 Obs 0489 아이콘 한 장씩 — 모션은 Sta.dat 의 아이콘 번호, 빈 칸은 모션 0(「EMPTY」 판).
        for (int i = 0; i < 3; i++)
            DrawUi(AilmentIconObs, AilmentIconMotion(unit, i), 0, x + 10 + i * 58, oy + 70, UiBlend.Alpha, loop: false);

        Header(x, oy + 110, w, db.T(166));
        Box(x, oy + 132, w, 84);
        int slots = db.PassiveSlotCount(c);
        for (int i = 0; i < 3; i++)
        {
            ushort id = c.Passives[i];
            string label = id != 0 && db.Abilities.TryGetValue(id, out var pab) ? AbilityLabel(pab, c.AbilityLevel(id)) : db.T(0);
            int ry = oy + 140 + i * 24;
            DrawText(label, x + 40, ry, i < slots ? White : DimGray);
            int slot = i;
            if (editable && i < slots) AddHit(x + 4, ry - 4, w - 8, 24, () => ChoosePassive(unit, slot));
        }

        Header(x, oy + 228, w, db.T(14));
        Box(x, oy + 250, w, 216);
        var weapon = c.Items[0] != 0 && db.Items.TryGetValue(c.Items[0], out var wi) ? wi : null;
        FillRect(x + 6, oy + 256, w - 12, 26, 0xFF0C1830);
        StrokeRect(x + 6, oy + 256, w - 12, 26, 0xFF8FB8F0);
        DrawText("WEAPON", x + 10, oy + 258, 0xFF8FB8F0);
        // 무기 종류 띠도 원본 그림(Obs 0326)이다 — .chr 40 이 모션 번호, 0 이면 62(분석-캐릭터 "아이템 그림").
        if (!DrawUi(ItemPictureObs, c.WeaponBand == 0 ? 62 : c.WeaponBand, 0, x + w / 2, oy + 268, UiBlend.Alpha, loop: false)
            && weapon != null)
            DrawText(GameDatabase.WeaponTypeName(weapon.Type), x + 90, oy + 262, White, 15);
        for (int i = 0; i < 6; i++)
        {
            var it = c.Items[i] != 0 && db.Items.TryGetValue(c.Items[i], out var found) ? found : null;
            string item = it != null ? db.T(it.NameId) : db.T(0);
            int ry = oy + 292 + i * 28;
            if (it != null) DrawUi(ItemPictureObs, it.PictureMotion, 0, x + 10, ry - 2, UiBlend.Alpha, loop: false);
            RightText(item, x + w - 12, ry, c.Items[i] != 0 ? White : DimGray);
            int slot = i;
            if (editable) AddHit(x + 6, ry - 6, w - 12, 28, () => ChooseEquipment(unit, slot));
        }

        // 3열 — 획득한 어빌리티 · 획득할 수 있는 어빌리티
        x = ox + 420; w = 204;
        Header(x, oy + 38, w, db.T(12));
        Box(x, oy + 60, w, 156);
        int row = 0;
        foreach (var (abilityId, level) in c.Abilities.OrderBy(a => a.Ability).Take(7))
        {
            if (!db.Abilities.TryGetValue(abilityId, out var ab)) continue;
            int ry = oy + 66 + row++ * 22;
            int cost = db.AbilityExpCost(ab, level);
            AbilityRow(x, ry, w, AbilityLabel(ab, level), cost > 0 ? cost.ToString() : "", cost <= c.Exp);
            if (editable && cost > 0) AddHit(x + 4, ry - 2, w - 8, 22, () => ConfirmAbility(unit, ab, learn: false));
            if (editable) _statusRightHits.Add((x + 4, ry - 2, w - 8, 22, () => LowerAbility(unit, ab)));
        }

        Header(x, oy + 228, w, db.T(13));
        Box(x, oy + 250, w, 216);
        row = 0;
        foreach (var ab in db.Learnable(c).Take(7))
        {
            int ry = oy + 258 + row++ * 30;
            int cost = db.AbilityExpCost(ab, 1);
            AbilityRow(x, ry, w, AbilityLabel(ab, 1), cost > 0 ? cost.ToString() : "", cost <= c.Exp);
            if (editable) AddHit(x + 4, ry - 4, w - 8, 28, () => ConfirmAbility(unit, ab, learn: true));
        }

        DrawPopup();
    }

    private void AddHit(int x, int y, int w, int h, Action click) => _statusHits.Add((x, y, w, h, click));

    private void DrawPopup()
    {
        if (_popup == null) return;
        var (px, py, h) = PopupRect();
        FillRect(px - 4, py - 4, PopupW + 8, h + 8, 0x80000000);
        FillRect(px, py, PopupW, h, 0xFF0C1830);
        StrokeRect(px, py, PopupW, h, 0xFF8FB8F0);
        FillRect(px, py, PopupW, 26, HeadBg);
        DrawText(_popupTitle, px + 10, py + 5, White);
        for (int i = 0; i < _popup.Count; i++)
        {
            var (label, right, enabled, _) = _popup[i];
            int ry = py + 30 + i * PopupRowH;
            DrawText(label, px + 12, ry + 4, enabled ? White : DimGray);
            if (right.Length > 0) RightText(right, px + PopupW - 12, ry + 4, DimGray);
        }
    }
}
