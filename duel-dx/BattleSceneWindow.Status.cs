using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// Status 화면 — 원본처럼 화면 전체(640×480)에 배경 Bgr 0058 을 깔고 값·아이콘·스크롤 막대를 원본 자리에 얹으며, 아군이면 장비 바꾸기(st-1)·장착 어빌리티 바꾸기(st-2)·
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

    /// <summary>Status 는 화면 전체 창이다 — 보이는 판 가운데에 640×480.</summary>
    private (int X, int Y) StatusOrigin() => (_camX + (ViewWidth - StatusW) / 2, _camY + (ViewHeight - StatusH) / 2);

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

    /// <summary>DUELDX_STATUS=&lt;Chr 번호&gt; 면 그 인물의 스테이터스 창을 바로 연다(화면 밖 시험용). 0 이면 첫 아군.</summary>
    private void OpenStatusIfAsked()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("DUELDX_STATUS"), out int chr)) return;
        if (chr == 0 && Array.FindIndex(_units, u => u.IsAlly) is >= 0 and var ally) { _statusUnit = ally; return; }
        OpenStatusFor(chr);
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
        if (_statusTip != null) return true;   // 설명이 떠 있는 동안은 다른 입력을 받지 않는다(0x10042c00)
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

        bool inside = bx >= ox && by >= oy && bx < ox + StatusW && by < oy + StatusH;
        // 닫기는 오른쪽 위 CLOSE 단추(0x100e05a2 — (565,1) 76×23)
        bool close = bx >= ox + CloseX && bx < ox + CloseX + CloseW && by >= oy + CloseY && by < oy + CloseY + CloseH;
        if (close || !inside) { _statusUnit = -1; _statusTip = null; return true; }

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

    /// <summary>
    /// 스테이터스 창 안 오른쪽 단추 누름 — 어빌리티·장비·상태이상 줄이면 설명을 띄운다(떼면 사라진다). 처리했으면 true.
    /// 레벨 내리기는 Shift+클릭으로 옮겼다(원본은 오른쪽 단추가 설명이다).
    /// </summary>
    private bool OnStatusRightClick(int bx, int by)
    {
        if (_statusUnit < 0) return false;
        if (_popup != null) { _popup = null; return true; }
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
    // 자리는 모두 640×480 화면 좌표다(원본 창 왼위 (1,21) 를 더한 값) — 옵시디안 분석-캐릭터 「Status 창 그림과 설명 표시 (st-5)」.
    // 칸 틀·칸 제목·「STATUS」·LEVEL 같은 라벨·HP/SOUL/TP 밑 붉은 줄은 전부 배경 Bgr 0058 한 장에 그려져 있어 코드는 값만 얹는다.

    private const int StatusBgr = 58;
    private const int CloseX = 565, CloseY = 1, CloseW = 76, CloseH = 23;          // 0x100e05a2 — Obs 0471 모션 28(평소)·29(눌림), 기준점 = 단추 가운데
    private const int RowObs = 471, ScrollObs = 71;
    private const int StatRight = 181;                                             // 능력치 값 오른끝(칸 폭 − 10)
    private const int SideX = 219, SideW = 162, RowH = 22;                         // 가운데 열 줄(장착 어빌리티·장비)
    private const int PassiveY = 149, PassiveStep = 28, EquipY = 304, EquipStep = 26;
    private const int AbilityX = 407, AbilityW = 190;                              // 오른쪽 열 줄
    private const int LearnedY = 69, LearnedStep = 27, LearnedRows = 6;
    private const int LearnableY = 279, LearnableStep = 46, LearnableRows = 4, LearnableRowH = 37;
    private const int ScrollX = 603, LearnedScrollY = 63, LearnedScrollH = 169, LearnableScrollY = 273, LearnableScrollH = 187;
    private const uint StatusWhite = 0xFFF8FCF8, StatusDim = 0xFF787C78, CostRed = 0xFFFF0000, CostYellow = 0xFFFFFF00, ScrollTrack = 0xFF636563;
    private const float StatusFont = 12f;                                          // 굴림 9pt

    /// <summary>두 어빌리티 목록의 맨윗줄 — 스크롤 화살표 한 번에 한 줄(0x10044e10).</summary>
    private int _learnedTop, _learnableTop;

    /// <summary>
    /// 스테이터스 창의 마우스 휠 — 커서가 놓인 어빌리티 목록을 한 칸에 한 줄씩 굴린다(원본은 스크롤 막대만 있다).
    /// 목록 밖이면 false(휠이 다른 데로 간다). 끝 자르기는 다음 그리기가 한다. 자리는 클릭처럼 지금의 <see cref="StatusOrigin"/> 으로 잰다.
    /// </summary>
    private bool ScrollStatusLists(int lines)
    {
        var (ox, oy) = StatusOrigin();
        int x = ox + AbilityX, w = ScrollX + 16 - AbilityX;
        if (MouseIn(x, oy + LearnedScrollY, w, LearnedScrollH)) { _learnedTop = Math.Max(0, _learnedTop + lines); return true; }
        if (MouseIn(x, oy + LearnableScrollY, w, LearnableScrollH)) { _learnableTop = Math.Max(0, _learnableTop + lines); return true; }
        return false;
    }

    /// <summary>
    /// 오른쪽 단추를 누르고 있는 동안 보이는 설명(원본 <c>0x10042c00</c>) — 글과 그때 마우스 자리. 떼면 사라지고,
    /// 떠 있는 동안에는 다른 클릭을 받지 않는다.
    /// </summary>
    private (string Text, int X, int Y)? _statusTip;

    private uint[]? _statusBg;
    private bool _statusBgTried;

    private uint[]? StatusBackground()
    {
        if (!_statusBgTried) { _statusBgTried = true; _statusBg = ReadBackground(StatusBgr); }
        return _statusBg;
    }

    private bool MouseIn(int x, int y, int w, int h) => _mouse.X >= x && _mouse.X < x + w && _mouse.Y >= y && _mouse.Y < y + h;

    /// <summary>줄 상자 안에 글을 세로 가운데로 찍는다 — 가로는 왼쪽 여백(<paramref name="left"/>) 또는 오른끝(<paramref name="right"/>).</summary>
    private void RowText(string text, int x, int y, int h, uint color, int left = -1, int right = -1)
    {
        if (text.Length == 0) return;
        var (_, tw, th) = GetText(text, color, StatusFont);
        int tx = right >= 0 ? x + right - tw : x + Math.Max(0, left);
        DrawText(text, tx, y + (h - th) / 2, color, StatusFont);
    }

    private void DrawStatusScreen()
    {
        _statusHits.Clear();
        _statusRightHits.Clear();
        if (_statusUnit < 0) return;
        var (ox, oy) = StatusOrigin();

        // 원본 Status 는 화면 전체를 쓰는 창이다 — 둘레는 검게, 가운데 640×480 에 배경 한 장(0x100e09f0).
        FillRect(_camX, _camY, ViewWidth, ViewHeight, 0xFF000000);
        if (StatusBackground() is { } bg)
            for (int y = 0; y < StatusH; y++)
                for (int x = 0; x < StatusW; x++)
                    SetPixel(ox + x, oy + y, bg[y * StatusW + x] | 0xFF000000);
        else
        {
            DrawGameFrame(ox, oy + FrameTitleH, StatusW, StatusH - FrameTitleH, "STATUS");
            DrawText("assets/moses/bgr/0058.bgr 이 없어 틀만 그렸습니다", ox + 20, oy + 40, DimGray);
        }
        DrawUi(RowObs, 28, 0, ox + CloseX + CloseW / 2, oy + CloseY + CloseH / 2, UiBlend.Alpha);

        var unit = StatusUnit();
        if (_db is not { } db || unit.Data is not { } c)
        {
            DrawText("이 인물의 게임 자료(assets/data)를 못 읽었습니다.", ox + 20, oy + 60, Red);
            return;
        }
        // 전투가 끝난 뒤 모세스에서 열면 결과 글이 남아 있어도 고칠 수 있어야 한다.
        bool editable = unit.IsAlly && (_outcome.Length == 0 || _mosesOpen);

        // ── 능력치 칸(0x100d4740) — 줄 k 의 세로 가운데 81 + 15k, 값은 오른끝 181. 초상은 .chr 10 의 Obs 모션 0 을 (62,103) 에 ──
        if (!DrawUi(c.FaceId, 0, 0, ox + 62, oy + 103, UiBlend.Alpha, loop: false) && _faces.TryGetValue(unit.ChrCode, out var face))
            BlitClipped(face, ox + 30, oy + 72, 64, 64);
        void StatLine(int k, string value) => RowText(value, ox, oy + 81 + 15 * k - 8, 16, StatusWhite, right: StatRight);
        string[] names = [db.T(c.NameId), db.T(c.TitleId), db.FamilyName(c), db.JobName(c)];
        for (int k = 0; k < names.Length; k++) StatLine(k, names[k]);
        StatLine(5, c.Level.ToString());
        StatLine(6, c.Exp.ToString());
        StatLine(8, $"{unit.Hp} / {unit.MaxHp}");
        StatLine(10, $"{unit.Soul} / {unit.MaxSoul}");
        StatLine(12, $"{unit.Tp} / {unit.MaxTp}");
        StatLine(15, db.Atk(c, unit.Soul).ToString());
        StatLine(16, db.Acr(c, unit.Tp).ToString());
        StatLine(17, db.Rdp(c, unit.Hp, unit.MaxHp).ToString());
        int[] basics = [(int)c.Lp + db.EquipBonus(c, 0x30), c.Ctp, db.Stp(c), db.Psy(c), db.Dep(c), db.Dex(c)];
        for (int k = 0; k < basics.Length; k++) StatLine(19 + k, basics[k].ToString());

        // ── 상태이상 셋(0x100d5448) — Obs 0489, 모션 = Sta 레코드 +4 칸(0 = EMPTY), 가운데 (242 + 53i, 81) ──
        for (int i = 0; i < 3; i++)
        {
            int cx = ox + 242 + 53 * i, cy = oy + 81;
            DrawUi(AilmentIconObs, AilmentIconMotion(unit, i), 0, cx, cy, UiBlend.Alpha, loop: false);
            int id = unit.StatusId[i];
            if (id != 0 && db.Statuses.GetValueOrDefault(id) is { } sta && db.T(sta.DescriptionId) is { Length: > 0 } fmt)
            {
                string tip = FormatPrintf(fmt, unit.StatusValue[i]);
                _statusRightHits.Add((cx - 22, cy - 11, 44, 22, () => ShowStatusTip(tip)));
            }
        }

        // ── 장착 어빌리티 세 줄(0x10035890) — 칸 수 = Dep +6, 넘는 줄은 꺼진 「없음」 ──
        int slots = db.PassiveSlotCount(c);
        for (int i = 0; i < 3; i++)
        {
            int rx = ox + SideX, ry = oy + PassiveY + PassiveStep * i;
            bool open = i < slots;
            if (editable && open && _popup == null && MouseIn(rx, ry, SideW, RowH)) DrawUi(RowObs, 1, 0, rx - 4, ry, UiBlend.Alpha);
            ushort id = c.Passives[i];
            if (id != 0 && open && db.Abilities.TryGetValue(id, out var pab))
            {
                DrawAbilityRow(pab, AbilityLabel(pab, c.AbilityLevel(id)), 0, false, rx, ry, SideW, RowH, 11);
                _statusRightHits.Add((rx, ry, SideW, RowH, () => ShowAbilityTip(pab, c.AbilityLevel(id))));
            }
            else RowText(db.T(0), rx, ry, RowH, open ? StatusWhite : StatusDim, left: 46);
            int slot = i;
            if (editable && open) AddHit(rx, ry, SideW, RowH, () => ChoosePassive(unit, slot));
        }

        // ── 장비 여섯 줄(0x100d3330) — WEAPON 띠 가운데 (296,288), 줄 그림 (8,2), 이름 오른쪽 맞춤 −16 ──
        DrawUi(ItemPictureObs, c.WeaponBand == 0 ? 62 : c.WeaponBand, 0, ox + 296, oy + 288, UiBlend.Alpha, loop: false);
        for (int i = 0; i < 6; i++)
        {
            int rx = ox + SideX, ry = oy + EquipY + EquipStep * i;
            if (editable && _popup == null && MouseIn(rx, ry, SideW, RowH)) DrawUi(RowObs, 1, 0, rx - 4, ry, UiBlend.Alpha);
            var it = c.Items[i] != 0 && db.Items.TryGetValue(c.Items[i], out var found) ? found : null;
            if (it != null) DrawUi(ItemPictureObs, it.PictureMotion, 0, rx + 8, ry + 2, UiBlend.Alpha, loop: false);
            RowText(it != null ? db.T(it.NameId) : db.T(0), rx, ry, RowH, StatusWhite, right: SideW - 16);
            if (it != null && db.T(it.DescriptionId) is { Length: > 0 } desc)
                _statusRightHits.Add((rx, ry, SideW, RowH, () => ShowStatusTip(desc)));
            int slot = i;
            if (editable) AddHit(rx, ry, SideW, RowH, () => ChooseEquipment(unit, slot));
        }

        // ── 획득한 어빌리티(0x10035290) — 번호 차례, 여섯 줄씩 ──
        var learned = c.Abilities.OrderBy(a => a.Ability)
            .Select(a => (Ab: db.Abilities.GetValueOrDefault(a.Ability), Level: (int)a.Level)).Where(a => a.Ab != null).ToList();
        _learnedTop = Math.Clamp(_learnedTop, 0, Math.Max(0, learned.Count - LearnedRows));
        for (int k = 0; k < LearnedRows && _learnedTop + k < learned.Count; k++)
        {
            var (found, level) = learned[_learnedTop + k];
            var ab = found!;
            int rx = ox + AbilityX, ry = oy + LearnedY + LearnedStep * k;
            int cost = db.AbilityExpCost(ab, level);             // 다음 레벨로 올리는 EXP — 최대 레벨이면 0(숫자 없음)
            if (editable && _popup == null && MouseIn(rx, ry, AbilityW, RowH)) DrawUi(RowObs, 4, 0, rx, ry, UiBlend.Alpha);
            DrawAbilityRow(ab, AbilityLabel(ab, level), cost, cost == 0 || cost > c.Exp, rx, ry, AbilityW, RowH, 11);
            _statusRightHits.Add((rx, ry, AbilityW, RowH, () => ShowAbilityTip(ab, level)));
            // 레벨 내리기(원본에 없는 데모 기능) — 마우스를 올린 줄의 비용 왼쪽에 작은 ▼ 단추(스크롤 막대 아래 화살표 Obs 0071 모션 4, 누름 5).
            // 원본 그림이라 창에 어울리고, 올린 줄에만 떠 목록이 어지럽지 않다. 오른쪽 단추는 원본대로 설명에 쓴다. Shift+클릭도 된다.
            if (editable && level > 1 && _popup == null && MouseIn(rx, ry, AbilityW, RowH))
            {
                int costW = cost > 0 ? GetText(cost.ToString(), CostRed, StatusFont).W : 0;
                int ax = rx + AbilityW - 10 - costW - 20, ay = ry + (RowH - 16) / 2;
                DrawUi(ScrollObs, MouseIn(ax, ay, 16, 16) ? 5 : 4, 0, ax, ay, UiBlend.Alpha, loop: false);
                AddHit(ax, ay, 16, 16, () => LowerAbility(unit, ab));      // 줄 클릭보다 먼저 받는다(먼저 넣은 것이 이긴다)
            }
            // 누르면 올리기. Shift 를 누른 채 누르면 한 레벨 내리기.
            if (editable) AddHit(rx, ry, AbilityW, RowH, () => { if (ShiftHeld) LowerAbility(unit, ab); else ConfirmAbility(unit, ab, learn: false); });
        }
        DrawScrollBar(ox + ScrollX, oy + LearnedScrollY, LearnedScrollH, _learnedTop, learned.Count, LearnedRows, top => _learnedTop = top);

        // ── 획득할 수 있는 어빌리티(0x100361e0) — 네 줄씩, 줄 높이 37 ──
        var learnable = db.Learnable(c).ToList();
        _learnableTop = Math.Clamp(_learnableTop, 0, Math.Max(0, learnable.Count - LearnableRows));
        for (int k = 0; k < LearnableRows && _learnableTop + k < learnable.Count; k++)
        {
            var ab = learnable[_learnableTop + k];
            int rx = ox + AbilityX, ry = oy + LearnableY + LearnableStep * k;
            int cost = db.AbilityExpCost(ab, 1);
            if (editable && _popup == null && MouseIn(rx, ry, AbilityW, LearnableRowH)) DrawUiStretched(RowObs, 7, rx, ry, AbilityW, LearnableRowH);
            DrawAbilityRow(ab, AbilityLabel(ab, 1), cost, cost == 0 || cost > c.Exp, rx, ry, AbilityW, LearnableRowH, 18);
            _statusRightHits.Add((rx, ry, AbilityW, LearnableRowH, () => ShowAbilityTip(ab, 0)));
            if (editable) AddHit(rx, ry, AbilityW, LearnableRowH, () => ConfirmAbility(unit, ab, learn: true));
        }
        DrawScrollBar(ox + ScrollX, oy + LearnableScrollY, LearnableScrollH, _learnableTop, learnable.Count, LearnableRows, top => _learnableTop = top);

        DrawPopup();
        if (_statusTip is { } shown) DrawDescriptionTip(shown.Text, shown.X, shown.Y, ox, oy, StatusW, StatusH);
    }

    /// <summary>
    /// 어빌리티 한 줄 — 글 (46, 가운데) · 종류 아이콘 (14, iconY) · 대상 아이콘 (34, iconY) · 비용 오른끝 폭−10.
    /// 꺼진 줄은 글·아이콘을 15/31 로 어둡게 하지만 비용 숫자는 꺼진 뒤에 만들어 안 흐리다(0x1003fdc0). 비용 &gt; EXP 면 빨강, 아니면 노랑.
    /// </summary>
    private void DrawAbilityRow(AbilityData ab, string label, int cost, bool off, int x, int y, int w, int h, int iconY)
    {
        RowText(label, x, y, h, off ? StatusDim : StatusWhite, left: 46);
        var blend = off ? UiBlend.Dim : UiBlend.Alpha;
        if (ab.IconKindMotion >= 0) DrawUi(AbilityIconObs, ab.IconKindMotion, 0, x + 14, y + iconY, blend, loop: false);
        if (ab.IconTargetMotion >= 0) DrawUi(AbilityIconObs, ab.IconTargetMotion, 0, x + 34, y + iconY, blend, loop: false);
        if (cost > 0) RowText(cost.ToString(), x, y, h, cost > (StatusUnit().Data?.Exp ?? 0) ? CostRed : CostYellow, right: w - 10);
    }

    /// <summary>
    /// 목록 스크롤 막대(0x10044e10) — 바탕 단색, 위 화살표 Obs 0071 모션 2 · 아래 4 · 손잡이 6(16×16).
    /// 화살표 한 번 = 한 줄. 막대 빈 곳을 누르면 한 화면씩 옮긴다(손잡이 끌기는 아직 없다).
    /// </summary>
    private void DrawScrollBar(int x, int y, int h, int top, int count, int rows, Action<int> setTop)
    {
        FillRect(x, y, 16, h, ScrollTrack);
        DrawUi(ScrollObs, 2, 0, x, y, UiBlend.Alpha, loop: false);
        DrawUi(ScrollObs, 4, 0, x, y + h - 16, UiBlend.Alpha, loop: false);
        int max = Math.Max(0, count - rows);
        int thumbY = y + 16 + (max == 0 ? 0 : (h - 48) * top / max);
        DrawUi(ScrollObs, 6, 0, x, thumbY, UiBlend.Alpha, loop: false);
        AddHit(x, y, 16, 16, () => setTop(Math.Max(0, top - 1)));
        AddHit(x, y + h - 16, 16, 16, () => setTop(Math.Min(max, top + 1)));
        AddHit(x, y + 16, 16, thumbY - y - 16, () => setTop(Math.Max(0, top - rows)));
        AddHit(x, thumbY + 16, 16, y + h - 16 - thumbY - 16, () => setTop(Math.Min(max, top + rows)));
    }

    /// <summary>UI 그림 한 컷을 네모에 맞춰 늘려 찍는다 — 「획득할 수 있는 어빌리티」 줄의 마우스 올림(Obs 0471 모션 7).</summary>
    private void DrawUiStretched(int obs, int motion, int x, int y, int w, int h)
    {
        if (UiFor(obs)?.FrameAt(motion, 0, loop: false) is not { W: > 0, H: > 0 } f) return;
        for (int yy = 0; yy < h; yy++)
        {
            int sy = yy * f.H / h;
            for (int xx = 0; xx < w; xx++)
            {
                uint c = f.Px[sy * f.W + xx * f.W / w];
                if ((c & 0xFF000000) != 0) SetPixel(x + xx, y + yy, c | 0xFF000000);
            }
        }
    }

    private void ShowAbilityTip(AbilityData ab, int level)
    {
        if (_db?.T(ab.DescriptionId) is { Length: > 0 } desc) ShowStatusTip(desc + AbilityEffectText(ab, level));
    }

    /// <summary>능력치 보정 번호(패시브·버프) — 슬롯이 아니라 능력치에 바로 더해지는 것(0x10032af0).</summary>
    private static readonly Dictionary<int, string> StatBonusNames = new()
    {
        [30] = "DEX", [31] = "PSY", [32] = "DEP", [33] = "최대 TP", [37] = "최대 SOUL", [48] = "LP",
    };

    /// <summary>
    /// 설명 창 아래에 붙일 <b>레벨별 효과 수치</b> — 지금 레벨과 다음 레벨(배우기 전이면 Lv1).
    /// 원본 설명문은 레벨과 상관없는 한 줄뿐이라 레벨을 올려 얼마나 달라지는지 안 보였다(사용자 요청: 리미트플로우).
    /// </summary>
    private string AbilityEffectText(AbilityData ab, int level)
    {
        string Line(int lv) =>
            ab.WorkByLevel.TryGetValue(lv, out int wid) && Work(wid) is { } w ? WorkEffect(w) : "";
        var parts = new List<string>();
        if (level > 0 && Line(level) is { Length: > 0 } now) parts.Add($"Lv{level}: {now}");
        int next = level + 1;
        if (next <= ab.MaxLevel && Line(next) is { Length: > 0 } then) parts.Add($"{(level == 0 ? "배우면 " : "다음 ")}Lv{next}: {then}");
        else if (level >= ab.MaxLevel && level > 0) parts.Add("(최대 레벨)");
        return parts.Count == 0 ? "" : "$n$n" + string.Join("$n", parts);
    }

    /// <summary>work 하나의 효과 — 위력(피해·회복)과 보정 셋을 한 줄로.</summary>
    private string WorkEffect(WorkData w)
    {
        var bits = new List<string>();
        if (w.AbilityId == EncourageAbility) bits.Add($"아군 SOUL +{w.Power}");
        else if (w.IsHeal && w.Power > 0) bits.Add($"최대 HP의 {w.Power}% 회복");
        else if (w.IsDamage && w.Power > 0) bits.Add($"위력 {w.Power}");
        foreach (var (stat, value) in w.Bonuses)
        {
            if (stat is 0 or 44 or 45 or 46) continue;          // 44~46 은 원본의 「없음」 칸
            if (StatBonusNames.TryGetValue(stat, out var statName)) { bits.Add($"{statName} {value:+#;-#;0}"); continue; }
            string desc = _db?.Statuses.GetValueOrDefault(stat) is { } st ? _db.T(st.DescriptionId) : "";
            if (desc.Contains("%d")) bits.Add(FormatPrintf(desc, value).TrimEnd('.', ' '));
            else bits.Add(value != 0 ? $"{AilmentNames.GetValueOrDefault(stat, $"효과 {stat}")} {value}" : AilmentNames.GetValueOrDefault(stat, $"효과 {stat}"));
        }
        return string.Join(" · ", bits);
    }

    private void ShowStatusTip(string text) => _statusTip = (text, _mouse.X, _mouse.Y);

    /// <summary>
    /// 설명 창(원본 <c>0x10042c00</c> → <c>0x100429a0</c>) — 게임 공통 틀(Obs 0970)이고 제목줄이 없다.
    /// 크기 = 글 + 56, 글은 흰색 가운데, <c>$n</c> 에서 줄을 바꾼다. 자리는 마우스 + (16,16) 을 화면(<paramref name="areaX"/>… 네모) 안으로 밀어 넣은 곳.
    /// </summary>
    private void DrawDescriptionTip(string text, int mx, int my, int areaX, int areaY, int areaW, int areaH)
    {
        var lines = text.Replace("$N", "$n").Replace("$p", "$n").Replace("$P", "$n").Split("$n");
        int tw = 0;
        const int lh = 16;
        foreach (string line in lines) tw = Math.Max(tw, GetText(line, StatusWhite, StatusFont).W);
        int th = lh * lines.Length;
        int w = tw + 56, h = th + 56;
        int x = Math.Clamp(mx + 16, areaX + 1, Math.Max(areaX + 1, areaX + areaW - 1 - w));
        int y = Math.Clamp(my + 16, areaY + 1, Math.Max(areaY + 1, areaY + areaH - 1 - h));
        DrawGameFrame(x, y, w, h);
        for (int i = 0; i < lines.Length; i++)
        {
            var (_, lw, _) = GetText(lines[i], StatusWhite, StatusFont);
            DrawText(lines[i], x + (w - lw) / 2, y + (h - th) / 2 + i * lh, StatusWhite, StatusFont);
        }
    }

    /// <summary>원본 설명 서식(<c>sprintf</c>)의 첫 <c>%d</c> 에 값을 넣는다 — 상태이상 설명이 남은 값을 이렇게 받는다.</summary>
    private static string FormatPrintf(string format, int value)
    {
        int at = format.IndexOf("%d", StringComparison.Ordinal);
        return at < 0 ? format : format[..at] + value + format[(at + 2)..];
    }

    private static bool ShiftHeld => (Native.Win32.GetKeyState(0x10) & 0x8000) != 0;

    private void AddHit(int x, int y, int w, int h, Action click) => _statusHits.Add((x, y, w, h, click));

    /// <summary>장비·장착 어빌리티 고르기 목록 — 게임 공통 틀(제목줄 있음)에 줄마다 글.</summary>
    private void DrawPopup()
    {
        if (_popup == null) return;
        var (px, py, h) = PopupRect();
        DarkenRect(px - 1, py - 1, PopupW + 2, h + 2, 8);
        DrawGameFrame(px, py + FrameTitleH, PopupW, h - FrameTitleH, _popupTitle);
        for (int i = 0; i < _popup.Count; i++)
        {
            var (label, right, enabled, _) = _popup[i];
            int ry = py + 30 + i * PopupRowH;
            if (enabled && MouseIn(px, ry, PopupW, PopupRowH)) DrawUi(RowObs, 1, 0, px + 6, ry + 1, UiBlend.Alpha);
            RowText(label, px, ry, PopupRowH, enabled ? StatusWhite : StatusDim, left: 16);
            if (right.Length > 0) RowText(right, px, ry, PopupRowH, StatusDim, right: PopupW - 16);
        }
    }
}
