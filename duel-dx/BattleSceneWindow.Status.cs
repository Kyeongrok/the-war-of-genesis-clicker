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

    /// <summary>파티 가방 — 아이템 번호 → 개수. <b>들어온 차례</b>를 지킨다(원본 파티 객체 +0x110 배열, 감사3 I4 — <see cref="ItemBag"/>).</summary>
    private readonly ItemBag _inventory = new();

    private readonly List<(int X, int Y, int W, int H, Action Click)> _statusHits = [];
    private string _popupTitle = "";

    /// <summary>고르기 목록 한 줄 — 글·오른쪽 글·켜짐·누르면 할 일, 아이콘 그리기(줄 왼위 x,y, 꺼짐)와 오른쪽 단추 설명.</summary>
    private sealed record PopupRow(string Label, string Right, bool Enabled, Action Apply, Action<int, int, bool>? Icon = null, Action? Tip = null);

    private List<PopupRow>? _popup;
    /// <summary>
    /// 고르기 창 폭·보이는 줄 수·맨윗줄 — 장비 182×10줄(0x100d3830, 폭 0xb6), 장착 어빌리티 154×8줄(0x10035c50, 폭 0x9a), 줄 높이 22(0x16), 스크롤 막대.
    /// 전에는 폭 320 에 모든 줄을 한 번에 그려 가방이 크면 창이 화면 밖으로 넘쳤다(감사4 S7·S8).
    /// </summary>
    private int _popupW = 182, _popupRows = 10, _popupTop;
    private const int PopupRowH = 22, PopupListX = 6, PopupScrollW = 16;
    private readonly List<(int X, int Y, int W, int H, Action Click)> _popupHits = [];

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
        u.MaxHp = ScaleMaxHp(u, Math.Max(1, _db.MaxHp(c, u.BonusMaxHp)));   // 48 은 갑옷 배율 앞에서 더한다(ba-15)
        u.MaxTp = Math.Max(1, _db.MaxTp(c) + u.BonusMaxTp);   // 마인드 어택(33·34)이 겹쳐도 0 밑으로 안 간다
        // STP 는 최대 TP 가감(상태 33)까지 넣은 최대 TP ÷ 제수다(0x1007acf0) — 최대 TP 를 올리면 차례 간격도 짧아진다(fg-22).
        u.Stp = Math.Max(1, c.TpDivisor == 0 ? _db.Stp(c) : u.MaxTp / c.TpDivisor);
        // 군단 부하의 최대 TP·STP 제수는 대장 것이다(0x1007aeb0·0x1007afa0 이 +0x508 사슬로 대장에서 읽음) — 부하 TP 는 대장과 같은 속도로
        // 대장 최대치까지 찬다(틱 0x10071db0). 전에는 부하 제 값으로 셌다(감사3 L2). 대장 값이 먼저 셈되어 있어야 한다(아래 되풀이).
        if (StatOwner(u) is var owner && owner != u && owner.MaxTp > 0)
        {
            u.MaxTp = owner.MaxTp;
            u.Stp = Math.Max(1, owner.Stp);
        }
        u.MaxSoul = Math.Max(0, _db.MaxSoul(c) + u.BonusMaxSoul);   // 소울 블레스트(37)가 겹쳐도 음수가 안 되게
        u.Hp = Math.Min(u.Hp, u.MaxHp);
        u.Tp = Math.Min(u.Tp, u.MaxTp);
        u.Soul = Math.Min(u.Soul, u.MaxSoul);
        // 대장 값이 바뀌면 부하도 다시 센다(상태 33 버프·레벨업·대장 교체).
        if (u.LeaderIndex < 0 && u.LegionId > 1 && Array.IndexOf(_units, u) is var li and >= 0)
            foreach (var f in FollowersOf(li)) RefreshUnitStats(f);
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
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"status open: Chr {chr} → {(index >= 0 ? $"전투 유닛 {index}" : "임시 유닛")}, 파티 자료 EXP {_party.GetValueOrDefault(chr)?.Exp}" + Environment.NewLine);
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
        // 전투 밖(모세스·필드)에서 여는 스테이터스가 <b>지난 전투에 남은 유닛</b>이면 그 유닛 자료만 바뀌고 파티 자료(_party)에는 안 적혔다 —
        // 파티 자료는 전투가 끝날 때만 유닛에서 옮겨 적기 때문이다. 그래서 유진의 LP증가를 Lv10 까지 올려도 필드에 나갔다 오거나
        // 다음 전투를 시작하면 되돌아갔다(사용자 보고). 전투 밖에서는 열려 있는 동안 파티 자료에도 곧바로 적는다.
        // 편은 안 본다 — 세이브에서 불러온 파티원 유닛은 편이 −1 이라 IsAlly 로 거르면 빠졌다. 파티에 있는 인물이면 된다.
        if ((_mosesOpen || FieldOpen) && _statusUnit >= 0 && _statusUnit != VirtualStatus && _statusUnit < _units.Length
            && _units[_statusUnit] is { Data: { } live } su && _party.ContainsKey(su.ChrCode) && !ReferenceEquals(_party[su.ChrCode], live))
        {
            _party[su.ChrCode] = live;
            if (Trace)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                    $"status sync: Chr {su.ChrCode} (전투 유닛) EXP {live.Exp}" + Environment.NewLine);
        }
        if (_statusVirtual is not { } v || _statusUnit == VirtualStatus) return;
        if (v.Data is { } c) _party[v.ChrCode] = c;
        if (Trace && v.Data is { } t)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"status sync: Chr {v.ChrCode} EXP {t.Exp} 어빌리티 {string.Join(" ", t.Abilities.Select(a => $"{a.Ability}:{a.Level}"))}" + Environment.NewLine);
        _statusVirtual = null;
    }

    private bool OnStatusClick(int bx, int by)
    {
        if (_statusUnit < 0) return false;
        if (_statusTip != null) return true;   // 설명이 떠 있는 동안은 다른 입력을 받지 않는다(0x10042c00)
        var (ox, oy) = StatusOrigin();

        if (_popup != null)
        {
            foreach (var (x, y, w, h, click) in _popupHits)                    // 스크롤 막대
                if (bx >= x && bx < x + w && by >= y && by < y + h) { click(); return true; }
            var (px, py, ph) = PopupRect();
            int row = PopupRowAt(bx, by);
            if (row >= 0)
            {
                var r = _popup[row];
                if (!r.Enabled) return true;                                    // 꺼진 줄은 클릭 없음
                _popup = null;
                r.Apply();
            }
            else if (bx < px || bx >= px + _popupW || by < py || by >= py + ph) _popup = null;   // 창 밖 = 닫기(원본 CLOSE 없음 — 편의)
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

    private void OpenPopup(string title, List<PopupRow> rows, int width, int visibleRows)
    {
        _popupTitle = title;
        _popup = rows;
        _popupW = width;
        _popupRows = visibleRows;
        _popupTop = 0;
    }

    /// <summary>고르기 창 네모 — 원본은 만든 뒤 화면 가운데로 옮긴다(0x100d37a1~0x100d37c5). 높이는 보이는 줄 수로 정해진다.</summary>
    private (int X, int Y, int H) PopupRect()
    {
        var (ox, oy) = StatusOrigin();
        int h = 30 + _popupRows * PopupRowH + 8;
        return (ox + (StatusW - _popupW) / 2, oy + (StatusH - h) / 2, h);
    }

    /// <summary>마우스 자리의 고르기 줄 번호(목록 전체 기준), 없으면 −1. 스크롤 막대 칸은 빼고 본다.</summary>
    private int PopupRowAt(int bx, int by)
    {
        if (_popup == null) return -1;
        var (px, py, _) = PopupRect();
        int lx = px + PopupListX, lw = _popupW - 2 * PopupListX - PopupScrollW, top = py + 30;
        if (bx < lx || bx >= lx + lw || by < top || by >= top + _popupRows * PopupRowH) return -1;
        int row = _popupTop + (by - top) / PopupRowH;
        return row < _popup.Count ? row : -1;
    }

    /// <summary>원본 파티 이름 — 장비 고르기 창 제목(0x100d35cd, 파티 번호 0x1007b030 마다).</summary>
    private static readonly string[] PartyTitles = ["살라딘 파티", "베라모드 파티", "크리스티앙 파티"];

    /// <summary>st-1: 장비 칸을 누르면 "해제" + 그 칸에 맞는 가방 아이템 목록.</summary>
    private void ChooseEquipment(UnitState u, int slot)
    {
        var db = _db!;
        var c = u.Data!;
        var rows = new List<PopupRow>();
        // 첫 줄은 늘 「해제」(TXR 1693, 꺼지지 않음) — 설명 TXR 1696 「선택된 아이템을 창고로 보냅니다.」(0x100d397e~0x100d399c).
        string unequip = db.T(1693) is { Length: > 0 } t1693 ? t1693 : "해제";
        string unequipTip = db.T(1696);
        rows.Add(new(unequip, "", true, () => { if (u.Data!.Items[slot] != 0) SetEquipment(u, slot, 0); },
            Tip: unequipTip.Length > 0 ? () => ShowStatusTip(unequipTip) : null));
        // 아이템 줄 = 아이콘 Obs 0326 (8,2)(0x100d3bcc) + 이름(0x100d3b78) + 오른쪽 "(x%d)"(0x1016f1e4), 설명 = Itm +0x34(0x100d3c5b).
        // 원본에 없는 「공격 N/방어 N」 열은 뺐다(감사4 S7).
        int fitting = 0;
        foreach (var (id, count) in _inventory)
        {
            if (count <= 0 || !db.Items.TryGetValue(id, out var item) || !db.FitsSlot(c, item, slot)) continue;
            fitting++;
            string desc = db.T(item.DescriptionId);
            rows.Add(new(db.T(item.NameId), $"(x{count})", true, () => SetEquipment(u, slot, (ushort)id),
                Icon: (x, y, _) => DrawUi(ItemPictureObs, item.PictureMotion, 0, x + 8, y + 2, UiBlend.Alpha, loop: false),
                Tip: desc.Length > 0 ? () => ShowStatusTip(desc) : null));
        }
        // 맞는 아이템이 하나도 없으면 꺼진 TXR 0 「없음」 줄 하나(0x100d3c8e~0x100d3cf9).
        if (fitting == 0) rows.Add(new(db.T(0), "", false, () => { }));
        OpenPopup(PartyTitles[Math.Clamp(_partyNo, 0, PartyTitles.Length - 1)], rows, 182, 10);
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
        var rows = new List<PopupRow>();
        // 줄마다 아이콘·"%s Lv%d"·오른쪽 단추 설명(목록 0x10035e50).
        foreach (var (id, level) in c.Abilities.OrderBy(a => a.Ability))
        {
            if (!db.Abilities.TryGetValue(id, out var ab) || !ab.IsPassive || c.Passives.Contains(id)) continue;
            int lv = level;
            rows.Add(new(AbilityLabel(ab, lv), "", true, () =>
            {
                var passives = (ushort[])u.Data!.Passives.Clone();
                passives[slot] = id;
                u.Data = u.Data with { Passives = passives };
                RefreshUnitStats(u);
            }, Icon: (x, y, off) => DrawAbilityIcons(ab, x, y + PopupRowH / 2, off), Tip: () => ShowAbilityTip(ab, lv)));
        }
        // 고를 것이 없어도 창은 뜬다 — 꺼진 빈 줄 하나(0x10035741~0x100357f5, 볼트 st-2, 감사4 S8). 전에는 토스트만 떴다.
        if (rows.Count == 0) rows.Add(new("", "", false, () => { }));
        OpenPopup("장착 어빌리티", rows, 154, 8);
    }

    /// <summary>st-3: 배운 어빌리티를 누르면 <b>바로</b> 레벨을 올리고, 배울 수 있는 어빌리티를 누르면 배운다 — 묻지 않는다.</summary>
    private void ConfirmAbility(UnitState u, AbilityData ab, bool learn)
    {
        var c = u.Data!;
        int level = learn ? 0 : c.AbilityLevel(ab.Id);
        int cost = _db!.AbilityExpCost(ab, level);   // 배우기는 0 → Lv1 work
        if (!learn && cost == 0) { Toast("최대 레벨입니다"); return; }
        if (cost > c.Exp) { Toast($"EXP 가 모자랍니다 (필요 {cost}, 있음 {c.Exp})"); return; }
        ApplyAbility(u, ab, learn, cost);
    }

    /// <summary>
    /// ▼ 단추·Shift+클릭 — 어빌리티 레벨을 하나 내리고 그 레벨에 썼던 EXP 를 돌려준다.
    /// Lv1 이면 확인창을 띄워 <b>어빌리티를 지운다</b> — 배울 때 쓴 EXP 는 돌려주지 않는다(사용자 요청 st-6).
    /// </summary>
    private void LowerAbility(UnitState u, AbilityData ab)
    {
        var c = u.Data!;
        int level = c.AbilityLevel(ab.Id);
        if (level <= 1)
        {
            _confirm = ($"{_db!.T(ab.NameId)} 지우기", "지우시겠습니까?\n경험치는 돌려받을 수 없습니다.", () => RemoveAbility(u, ab));
            return;
        }
        int refund = _db!.AbilityExpCost(ab, level - 1);       // Lv(level−1) → Lv(level) 에 썼던 값
        var list = c.Abilities.ToList();
        int i = list.FindIndex(a => a.Ability == ab.Id);
        list[i] = ((ushort)ab.Id, (ushort)(level - 1));
        u.Data = c with { Abilities = [.. list], Exp = c.Exp + refund };
        RefreshUnitStats(u);
        Toast($"{_db.T(ab.NameId)} Lv{level - 1} — EXP {refund} 돌려받음");
    }

    /// <summary>배운 어빌리티를 지운다 — EXP 는 돌려주지 않고, 장착 칸에 끼워 둔 패시브였으면 그 칸도 비운다.</summary>
    private void RemoveAbility(UnitState u, AbilityData ab)
    {
        if (u.Data is not { } c) return;
        var passives = c.Passives.Select(p => p == ab.Id ? (ushort)0 : p).ToArray();
        u.Data = c with { Abilities = [.. c.Abilities.Where(a => a.Ability != ab.Id)], Passives = passives };
        RefreshUnitStats(u);
        Toast($"{_db!.T(ab.NameId)} 을(를) 지웠습니다");
    }

    /// <summary>
    /// 스테이터스 창 안 오른쪽 단추 누름 — 어빌리티·장비·상태이상 줄이면 설명을 띄운다(떼면 사라진다). 처리했으면 true.
    /// 레벨 내리기는 Shift+클릭으로 옮겼다(원본은 오른쪽 단추가 설명이다).
    /// </summary>
    private bool OnStatusRightClick(int bx, int by)
    {
        if (_statusUnit < 0) return false;
        if (_popup != null)
        {
            // 고르기 창 줄 = 누르고 있는 동안 설명(0x100d3c5b·0x10035e50). 창 밖 = 창 닫기(편의 — 원본은 오른쪽 단추 닫기가 없다).
            var (px, py, ph) = PopupRect();
            if (PopupRowAt(bx, by) is var row and >= 0) { if (_popup[row].Enabled) _popup[row].Tip?.Invoke(); }
            else if (bx < px || bx >= px + _popupW || by < py || by >= py + ph) _popup = null;
            return true;
        }
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
        u.Data = c with { Abilities = [.. list], Passives = passives, Exp = c.JobId == 37 ? c.Exp : c.Exp - cost };   // 직업 37 은 EXP 면제(0x100e128c)
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
        if (_popup != null)
        {
            // 고르기 창이 떠 있으면 휠은 그 목록만 굴린다(원본은 스크롤 막대만).
            _popupTop = Math.Clamp(_popupTop + lines, 0, Math.Max(0, _popup.Count - _popupRows));
            return true;
        }
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
        // 원본은 Status 를 링(차례인 유닛)에서만 열고, 파티 밖 인물은 아이템 교환이 막힌다 — 전투에서는 <b>차례인 내 유닛</b>만,
        // 모세스에서는 파티원만 고칠 수 있다(fg-21 ⑰). 편 3 동맹은 보기만.
        bool editable = _mosesOpen ? unit.IsAlly && (_members.Count == 0 || _members.Contains(unit.ChrCode))
                                   : unit.PlayerControlled && _outcome.Length == 0 && _turn >= 0 && _units[_turn] == unit;

        // ── 능력치 칸(0x100d4740) — 줄 k 의 세로 가운데 81 + 15k, 값은 오른끝 181. 초상은 .chr 10 의 Obs 모션 0 을 (62,103) 에 ──
        // 초상 번호가 0 이면 원본은 Obs 0229(0xe5)를 찍는다(볼트 st-5 「초상」, 감사4 S11). 그것도 없으면 필드 얼굴.
        if (!DrawUi(c.FaceId != 0 ? c.FaceId : 229, 0, 0, ox + 62, oy + 103, UiBlend.Alpha, loop: false) && _faces.TryGetValue(unit.ChrCode, out var face))
            BlitClipped(face, ox + 30, oy + 72, 64, 64);
        void StatLine(int k, string value) => RowText(value, ox, oy + 81 + 15 * k - 8, 16, StatusWhite, right: StatRight);
        string[] names = [db.T(c.NameId), db.T(c.TitleId), db.FamilyName(c), db.JobName(c)];
        for (int k = 0; k < names.Length; k++) StatLine(k, names[k]);
        StatLine(5, c.Level.ToString());
        StatLine(6, c.Exp.ToString());
        // HP·SOUL·TP — 보정(장비 0x10032c60 + 장착 0x10032af0 + 유닛 가감 +0x4ce/+0x4d2/+0x4d0)이 0 보다 크면 "%d / %d(+%d)"(0x1016f230),
        // 아니면 "%d / %d"(0x1016f21c). HP 보정은 0x10032e80 으로 갑옷 배율을 곱해 화면 단위로 찍는다(0x100d4913~0x100d4dcb, 감사4 S2).
        static string WithBonus(int now, int max, int bonus) => bonus > 0 ? $"{now} / {max}(+{bonus})" : $"{now} / {max}";
        int hpBonus = db.EquipBonus(c, 0x30) + unit.BonusMaxHp, armor = db.ArmorRate(c);
        if (hpBonus > 0 && armor != 0 && db.N(6) != 0) hpBonus = hpBonus * (armor + db.N(6)) / db.N(6);
        StatLine(8, WithBonus(unit.Hp, unit.MaxHp, hpBonus));
        StatLine(10, WithBonus(unit.Soul, unit.MaxSoul, db.EquipBonus(c, 0x25) + unit.BonusMaxSoul));
        StatLine(12, WithBonus(unit.Tp, unit.MaxTp, db.EquipBonus(c, 0x21) + unit.BonusMaxTp));
        // ATK·ACR·RDP·LP·PSY·DEP·DEX 는 판정과 같은 값(상태이상·군단 보정·부하=대장 DEX/최대 TP) — 감사4 S1·S3·S4.
        var (eff, acr, rdp, lp) = ShownStats(db, unit, c);
        int weaponPct = unit.Status(29);                                   // 상태 29 — 무기 공격력 %(0x1007afd0)
        StatLine(15, AtkWithSoul(db, eff, BasicAttackSoul(unit.Soul), weaponPercent: weaponPct));   // 일반 공격 ATK — 조정의 소울 기여도가 걸린 값
        // ATK 줄에 마우스를 올리면 바탕 × 소울 배율로 풀어 보인다(사용자 요청) — 그림은 창 맨 위에 그린다.
        int atkY = oy + 81 + 15 * 15 - 8;
        string? atkHover = _statusTip == null && _popup == null && MouseIn(ox + 20, atkY, StatRight - 20, 16) ? AtkBreakdown(db, eff, BasicAttackSoul(unit.Soul), weaponPct)
              + (_soulWeight != 100 ? $"$n(모드 > 조정: 소울 기여도 {_soulWeight}% — SOUL {unit.Soul} 을 {BasicAttackSoul(unit.Soul)} 로 셈)" : "") : null;
        StatLine(16, acr.ToString());
        StatLine(17, rdp.ToString());
        // STP 0x1007acf0 = 최대 TP(상태 33 포함) / 제수, 부하는 대장 것 — RefreshUnitStats 가 셈해 둔 unit.Stp(감사4 S3).
        int[] basics = [lp, c.Ctp, unit.Stp, db.Psy(eff), db.Dep(eff), db.Dex(eff)];
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
                _statusRightHits.Add((cx - 15, cy - 10, 30, 20, () => ShowStatusTip(tip)));   // 판정 칸 30×20(0x100d54e5 → 0x10041380(−15,−10,30,20))
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
            .Select(a => (Ab: db.Abilities.GetValueOrDefault(a.Ability), Level: (int)a.Level, Legion: false)).Where(a => a.Ab != null).ToList();
        // 군단기 — 배속 군단(CChr+0x1c)의 기술 다섯 칸(For +0x1e, 6바이트씩) 가운데 필요 세력·대장 조건이 맞는 것을 목록 끝에(0x100326bd~0x1003274c).
        // 레벨 게터 0x10032a30 은 분류 2 면 늘 1, 다음 레벨(0x10032450)은 최대 레벨 1 을 넘어 −1 → 숫자 없는 꺼진 줄 「이름 Lv1」, 설명만 된다(감사4 S6).
        // 조건은 전투 어빌리티 메뉴(MenuRows)와 같다.
        if (_unitLegion.TryGetValue(unit.ChrCode, out int statusLegion) && Legions().GetValueOrDefault(statusLegion) is { } myLegion)
            foreach (var (abilityId, power, leader) in myLegion.Skills)
            {
                if (abilityId == 0 || power > 1000 || (leader != 0 && leader != unit.ChrCode)) continue;
                if (db.Abilities.GetValueOrDefault(abilityId) is { } lab && !learned.Any(l => l.Ab == lab)) learned.Add((lab, 1, true));
            }
        _learnedTop = Math.Clamp(_learnedTop, 0, Math.Max(0, learned.Count - LearnedRows));
        for (int k = 0; k < LearnedRows && _learnedTop + k < learned.Count; k++)
        {
            var (found, level, legionRow) = learned[_learnedTop + k];
            var ab = found!;
            int rx = ox + AbilityX, ry = oy + LearnedY + LearnedStep * k;
            int cost = legionRow ? 0 : db.AbilityExpCost(ab, level);             // 다음 레벨로 올리는 EXP — 최대 레벨이면 0(숫자 없음)
            // 꺼진 줄(비용 > EXP·최대 레벨·군단기)은 0x1003fdc0 으로 꺼져 강조·클릭이 없다(0x10035412, 감사4 S9).
            bool off = cost == 0 || cost > c.Exp;
            if (editable && !off && _popup == null && MouseIn(rx, ry, AbilityW, RowH)) DrawUi(RowObs, 4, 0, rx, ry, UiBlend.Alpha);
            DrawAbilityRow(ab, AbilityLabel(ab, level), cost, off, rx, ry, AbilityW, RowH, 11);
            _statusRightHits.Add((rx, ry, AbilityW, RowH, () => ShowAbilityTip(ab, level)));
            if (legionRow) continue;                                            // 군단기는 설명만
            // 레벨 내리기(원본에 없는 데모 기능) — 마우스를 올린 줄의 비용 왼쪽에 작은 ▼ 단추(스크롤 막대 아래 화살표 Obs 0071 모션 4, 누름 5).
            // 원본 그림이라 창에 어울리고, 올린 줄에만 떠 목록이 어지럽지 않다. 오른쪽 단추는 원본대로 설명에 쓴다. Shift+클릭도 된다.
            // Lv1 에서 누르면 지울지 묻는다(st-6).
            if (editable && level >= 1 && _popup == null && _confirm == null && MouseIn(rx, ry, AbilityW, RowH))
            {
                int costW = cost > 0 ? GetText(cost.ToString(), CostRed, StatusFont).W : 0;
                int ax = rx + AbilityW - 10 - costW - 20, ay = ry + (RowH - 16) / 2;
                DrawUi(ScrollObs, MouseIn(ax, ay, 16, 16) ? 5 : 4, 0, ax, ay, UiBlend.Alpha, loop: false);
                AddHit(ax, ay, 16, 16, () => LowerAbility(unit, ab));      // 줄 클릭보다 먼저 받는다(먼저 넣은 것이 이긴다)
            }
            // 누르면 올리기. Shift 를 누른 채 누르면 한 레벨 내리기(꺼진 줄도 — 사용자 요청 기능). 꺼진 줄의 그냥 클릭은 아무 일 없다(S9).
            if (editable) AddHit(rx, ry, AbilityW, RowH, () => { if (ShiftHeld) LowerAbility(unit, ab); else if (!off) ConfirmAbility(unit, ab, learn: false); });
        }
        DrawScrollBar(ox + ScrollX, oy + LearnedScrollY, LearnedScrollH, _learnedTop, learned.Count, LearnedRows, top => _learnedTop = top);

        // ── 획득할 수 있는 어빌리티(0x100361e0) — 네 줄씩, 줄 높이 37 ──
        var learnable = db.Learnable(c).ToList();
        _learnableTop = Math.Clamp(_learnableTop, 0, Math.Max(0, learnable.Count - LearnableRows));
        for (int k = 0; k < LearnableRows && _learnableTop + k < learnable.Count; k++)
        {
            var ab = learnable[_learnableTop + k];
            int rx = ox + AbilityX, ry = oy + LearnableY + LearnableStep * k;
            int cost = db.AbilityExpCost(ab, 0);
            bool off = cost == 0 || cost > c.Exp;                                // 꺼진 줄은 강조·클릭 없음(S9)
            if (editable && !off && _popup == null && MouseIn(rx, ry, AbilityW, LearnableRowH)) DrawUiStretched(RowObs, 7, rx, ry, AbilityW, LearnableRowH);
            // 선행 어빌리티 1 이 있으면 두 줄 — 위 "%s Lv%d ->"(선행 이름·필요 레벨, 아이콘 y 9) / 아래 "  %s Lv%d"(새것 Lv1, 아이콘 y 27).
            // 배우면 선행이 지워진다는 것을 화면이 알린다(0x100361e0, 볼트 st-5, 감사4 S5). 없으면 한 줄 + 아이콘 y 18.
            if (ab.Prereq1 != 0 && db.Abilities.GetValueOrDefault(ab.Prereq1) is { } pre)
            {
                DrawAbilityRow(pre, $"{db.T(pre.NameId)} Lv{ab.Prereq1Level} ->", 0, off, rx, ry, AbilityW, 18, 9);
                DrawAbilityRow(ab, $"  {db.T(ab.NameId)} Lv1", 0, off, rx, ry + 18, AbilityW, 19, 27 - 18);
                if (cost > 0) RowText(cost.ToString(), rx, ry, LearnableRowH, cost > c.Exp ? CostRed : CostYellow, right: AbilityW - 10);
            }
            else DrawAbilityRow(ab, AbilityLabel(ab, 1), cost, off, rx, ry, AbilityW, LearnableRowH, 18);
            _statusRightHits.Add((rx, ry, AbilityW, LearnableRowH, () => ShowAbilityTip(ab, 0)));
            if (editable && !off) AddHit(rx, ry, AbilityW, LearnableRowH, () => ConfirmAbility(unit, ab, learn: true));
        }
        DrawScrollBar(ox + ScrollX, oy + LearnableScrollY, LearnableScrollH, _learnableTop, learnable.Count, LearnableRows, top => _learnableTop = top);

        DrawPopup();
        if (_statusTip is { } shown) DrawDescriptionTip(shown.Text, shown.X, shown.Y, ox, oy, StatusW, StatusH);
        else if (atkHover != null) DrawDescriptionTip(atkHover, _mouse.X, _mouse.Y, ox, oy, StatusW, StatusH);
    }

    /// <summary>
    /// 어빌리티 한 줄 — 글 (46, 가운데) · 종류 아이콘 (14, iconY) · 대상 아이콘 (34, iconY) · 비용 오른끝 폭−10.
    /// 꺼진 줄은 글·아이콘을 15/31 로 어둡게 하지만 비용 숫자는 꺼진 뒤에 만들어 안 흐리다(0x1003fdc0). 비용 &gt; EXP 면 빨강, 아니면 노랑.
    /// </summary>
    private void DrawAbilityRow(AbilityData ab, string label, int cost, bool off, int x, int y, int w, int h, int iconY)
    {
        RowText(label, x, y, h, off ? StatusDim : StatusWhite, left: 46);
        DrawAbilityIcons(ab, x, y + iconY, off);
        if (cost > 0) RowText(cost.ToString(), x, y, h, cost > (StatusUnit().Data?.Exp ?? 0) ? CostRed : CostYellow, right: w - 10);
    }

    /// <summary>어빌리티 아이콘 둘 — 종류 (14, cy) · 대상 (34, cy), 꺼진 줄은 어둡게.</summary>
    private void DrawAbilityIcons(AbilityData ab, int x, int cy, bool off)
    {
        var blend = off ? UiBlend.Dim : UiBlend.Alpha;
        if (ab.IconKindMotion >= 0) DrawUi(AbilityIconObs, ab.IconKindMotion, 0, x + 14, cy, blend, loop: false);
        if (ab.IconTargetMotion >= 0) DrawUi(AbilityIconObs, ab.IconTargetMotion, 0, x + 34, cy, blend, loop: false);
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
        if (_db?.AbilityDescription(ab) is { Length: > 0 } desc) ShowStatusTip(desc + AbilityEffectText(ab, level, StatusUnit()));
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
    /// <remarks>
    /// <paramref name="user"/> 가 있으면 그 인물의 <b>SOUL 필요·소모</b>도 붙인다 — 체질 비용(hpFactor)이 체질마다 달라 같은 기술도 사람마다 다르다(사용자 요청).
    /// </remarks>
    private string AbilityEffectText(AbilityData ab, int level, UnitState? user = null)
    {
        string Line(int lv)
        {
            if (!ab.WorkByLevel.TryGetValue(lv, out int wid) || Work(wid) is not { } w) return "";
            var bits = new List<string>();
            if (WorkEffect(w) is { Length: > 0 } effect) bits.Add(effect);
            if (user?.Data is { } c)
            {
                int need = SoulNeedFor(user, c, wid), spend = SoulCostFor(user, c, wid);
                if (need > 0 || spend > 0) bits.Add(need == spend ? $"SOUL {spend} 소모" : $"SOUL {need} 필요 · {spend} 소모");
            }
            return string.Join(" · ", bits);
        }
        var parts = new List<string>();
        // 대상·범위 — 자료(대상 방식·사거리·범위 모양·크기)로 만든 줄이라 스킬을 고치면 저절로 따라온다(사용자 요청: 오버플로우 설명이 「적군 한명」 그대로).
        // 지금 레벨(배우기 전이면 Lv1) 것을 보이고, 다음 레벨에서 달라지면 그것도 보인다.
        int shown = Math.Max(1, level);
        string? reachNow = ab.WorkByLevel.TryGetValue(shown, out int nowWork) && Work(nowWork) is { } nw ? TargetText(nw) : null;
        if (reachNow != null) parts.Add(reachNow);
        if (ab.WorkByLevel.TryGetValue(shown + 1, out int nextWork) && Work(nextWork) is { } xw && TargetText(xw) is var reachNext && reachNext != reachNow)
            parts.Add($"(Lv{shown + 1}부터 {reachNext})");
        if (level > 0 && Line(level) is { Length: > 0 } now) parts.Add($"Lv{level}: {now}");
        int next = level + 1;
        if (next <= ab.MaxLevel && Line(next) is { Length: > 0 } then) parts.Add($"{(level == 0 ? "배우면 " : "다음 ")}Lv{next}: {then}");
        else if (level >= ab.MaxLevel && level > 0) parts.Add("(최대 레벨)");
        return parts.Count == 0 ? "" : "$n$n" + string.Join("$n", parts);
    }

    /// <summary>
    /// work 의 대상·사거리·범위를 한 줄로 — 「적 · 사거리 1~4칸 · 겨눈 칸 둘레 1칸(5칸)」 따위.
    /// 대상 방식(+0x13/+0x1e: 0·2 자기, 1 적, 3·6 아무 칸, 4 아군, 5 아무 유닛)과 범위 모양(분석-스킬 8절: 1 마름모 … 9 삼각형)으로 만든다.
    /// </summary>
    private static string TargetText(WorkData w)
    {
        static string Who(int mode) => mode switch
        {
            0 or 2 => "자기",
            1 => "적",
            4 => "아군",
            3 or 5 or 6 => "적·아군 모두",
            7 => "빈 칸",
            8 => "물체",
            _ => "?",
        };
        int rMin = w.RangeMin, rMax = w.RangeMaxQuarters / 4, n = w.AreaMaxQuarters / 4;
        // 크기 0 인 마름모는 그 칸 하나 — 범위가 없는 것과 같다.
        bool area = w.AreaShape != 0 && !(w.AreaShape == 1 && n == 0 && w.AreaMin == 0);
        string who = Who(area && w.AreaMode != 0 ? w.AreaMode : w.TargetMode);
        // 사거리 최소 파일값 k 는 4k−3(4분의 1칸) — 곧 k 칸부터다. 0 이면 자기 칸도 되는데, 적만 겨누는 기술에는 뜻이 없어 안 적는다.
        bool selfOnly = !w.SelfCentred && w.RangeShape != 0 && rMax == 0;
        if (selfOnly && !area) return "대상: 자기에게";
        bool canSelf = w.TargetMode is 3 or 4 or 5 or 6;
        string aim = w.SelfCentred || w.RangeShape == 0 || selfOnly ? "자기 자리에서"
                   : rMin == 0 ? $"사거리 {rMax}칸 안{(canSelf ? "(자기 포함)" : "")}"
                   : rMin == rMax ? $"사거리 {rMax}칸" : $"사거리 {rMin}~{rMax}칸";
        string centre = w.AreaMin > 0 ? ", 가운데 빼고" : "";
        string shape = w.AreaShape switch
        {
            0 => "한 명",
            1 => n == 0 ? "한 명" : $"둘레 {n}칸 마름모({2 * n * (n + 1) + 1}칸{centre})",
            2 => $"십자 {n}칸",
            3 => $"앞 부채꼴 {n}칸",
            4 => "화면 안 전체",
            5 => $"앞 일직선 {Math.Max(1, n)}칸",
            6 => n == 0 ? "앞 가로 3칸" : $"폭 3칸 × 앞 {n + 1}줄",
            7 => n == 0 ? "앞 가로 5칸" : $"폭 5칸 × 앞 {n + 1}줄",
            8 => $"대각선 X {n}칸",
            9 => $"앞 삼각형 {n}칸",
            10 => "앞 ^ 모양 3칸(두 칸 앞·양 대각선)",
            _ => $"모양 {w.AreaShape}",
        };
        return $"대상: {who} · {aim} · 범위: {shape}";
    }

    /// <summary>
    /// ATK 와 그 안에 든 <b>소울 배율</b> — ATK = 바탕 × (SOUL + Num[2]) × Num[42] / Num[85](<c>0x1007ab20</c>), 지금 자료로는 (SOUL + 10) ÷ 10 배.
    /// SOUL 이 차면 공격력이 몇 배가 되는지 보이게 한다(사용자 요청: SOUL 40 이면 ×5.0, 150 이면 ×16.0).
    /// </summary>
    /// <param name="compact">정보 창(폭 140)은 「ATK」 글자와 겹치지 않게 「100 ×5.0」 으로 줄인다.</param>
    private static string AtkWithSoul(GameDatabase db, CharacterData c, int soul, bool compact = false, int weaponPercent = 0)
    {
        double factor = db.N(85) == 0 ? 0 : (double)(soul + db.N(2)) * db.N(42) / db.N(85);
        int atk = db.Atk(c, soul, weaponPercent: weaponPercent);
        return compact ? $"{atk} ×{factor:0.0}" : $"{atk} (소울×{factor:0.0})";
    }

    /// <summary>
    /// 화면에 보일 능력치 — 판정과 같은 값. 원본 게터(<c>0x1007ade0</c> PSY·<c>0x1007af20</c> DEP·<c>0x1007ae50</c> DEX·<c>0x1007aeb0</c> 최대 TP·
    /// <c>0x1007ac70</c> LP)는 상태 1·30·31·32·40·48 과 군단 부하 보정(LP·PSY·DEP)을 얹고, 부하면 DEX·최대 TP 를 대장에서 읽는다.
    /// ATK(<c>0x1007ab20</c>)·ACR(<c>0x1007ab90</c>)·RDP(<c>0x1007abf0</c>)는 그 게터를 부른다. 전에는 날 자료(<c>unit.Data</c>)로 셈해
    /// 버프·군단이 걸린 유닛의 표시값이 판정과 달랐다(감사4 S1).
    /// </summary>
    /// <returns><c>E</c> = PSY·DEP·DEX 가 얹힌 자료(<see cref="GameDatabase.Psy"/> 따위로 읽는다), 그리고 ATK·ACR·RDP·LP.</returns>
    private (CharacterData E, int Acr, int Rdp, int Lp) ShownStats(GameDatabase db, UnitState unit, CharacterData c)
    {
        var e = CombatData(unit) ?? c;                          // 상태 1·30·31·32·40 + 군단 PSY·DEP + 부하는 대장 DEX
        var (lLp, _, _) = LegionBonusFor(unit);                  // 군단 LP(For +0x14 × 세력 / 100) — PSY·DEP 는 CombatData 가 이미 얹었다
        // ACR 0x1007ab90 = (2×CTP + 최대TP(0x1007aeb0 — 상태 33·부하=대장) + 현재TP) / N9 + DEX(0x1007ae50) / N8
        int acr = (db.N(9) == 0 ? 0 : (2 * c.Ctp + unit.MaxTp + unit.Tp) / db.N(9)) + (db.N(8) == 0 ? 0 : db.Dex(e) / db.N(8));
        int rdp = db.Rdp(e, unit.Hp, unit.MaxHp);
        // LP 0x1007ac70 = CChr LP + 장비·장착 + 상태 48(+0x4ce) + 군단 LP
        int lp = (int)c.Lp + db.EquipBonus(c, 0x30) + unit.BonusMaxHp + lLp;
        return (e, acr, rdp, lp);
    }

    /// <summary>
    /// ATK 풀이 — 바탕(무기·PSY) × 소울 배율, 그리고 그것이 피해가 되는 식. 원본 <c>0x1007ab20</c>:
    /// ATK = ((무기 + Num[1]) × PSY / Num[25]) × (SOUL + Num[2]) × Num[42] / Num[85].
    /// </summary>
    private static string AtkBreakdown(GameDatabase db, CharacterData c, int soul, int weaponPercent = 0)
    {
        int weapon = c.Items[0] != 0 && db.Items.TryGetValue(c.Items[0], out var w) ? w.Attack : 0;
        if (weaponPercent != 0) weapon = Math.Max(0, weapon + weapon * weaponPercent / 100);   // 상태 29 — 무기 공격력 %(0x1007afe5)
        int psy = db.Psy(c);
        int baseAtk = db.N(25) == 0 ? 0 : (weapon + db.N(1)) * psy / db.N(25);
        double factor = db.N(85) == 0 ? 0 : (double)(soul + db.N(2)) * db.N(42) / db.N(85);
        return $"ATK {db.Atk(c, soul, weaponPercent: weaponPercent)} = 바탕 {baseAtk} × 소울 {factor:0.0}배$n"
             + $"바탕 = (무기 {weapon} + {db.N(1)}) × PSY {psy} ÷ {db.N(25)}$n"
             + $"소울 배율 = (SOUL {soul} + {db.N(2)}) × {db.N(42)} ÷ {db.N(85)}$n"
             + $"피해 = ATK × (1000 − 상대 RDP) ÷ 1000, 어빌리티는 × (200 + 위력) ÷ 200";
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
            if (ChangeText(stat, value) is { } change) { bits.Add(change); continue; }
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

    /// <summary>
    /// 장비·장착 어빌리티 고르기 목록 — 게임 공통 틀(제목줄 있음, 0x101b5fec + 장식 2), 1열 · 보이는 줄 <see cref="_popupRows"/> · 오른쪽 스크롤 막대.
    /// 줄 = 아이콘 + 글(아이콘 있으면 46, 없으면 16) + 오른쪽 글. 꺼진 줄은 어둡고 강조·클릭이 없다.
    /// </summary>
    private void DrawPopup()
    {
        _popupHits.Clear();
        if (_popup == null) return;
        var (px, py, h) = PopupRect();
        DarkenRect(px - 1, py - 1, _popupW + 2, h + 2, 8);
        DrawGameFrame(px, py + FrameTitleH, _popupW, h - FrameTitleH, _popupTitle);
        _popupTop = Math.Clamp(_popupTop, 0, Math.Max(0, _popup.Count - _popupRows));
        int lx = px + PopupListX, lw = _popupW - 2 * PopupListX - PopupScrollW, top = py + 30;
        int hover = _statusTip == null ? PopupRowAt(_mouse.X, _mouse.Y) : -1;
        for (int k = 0; k < _popupRows && _popupTop + k < _popup.Count; k++)
        {
            var r = _popup[_popupTop + k];
            int ry = top + k * PopupRowH;
            if (r.Enabled && hover == _popupTop + k) DrawUiStretched(RowObs, 1, lx, ry, lw, PopupRowH);
            r.Icon?.Invoke(lx, ry, !r.Enabled);
            RowText(r.Label, lx, ry, PopupRowH, r.Enabled ? StatusWhite : StatusDim, left: r.Icon != null ? 46 : 10);
            if (r.Right.Length > 0) RowText(r.Right, lx, ry, PopupRowH, r.Enabled ? StatusWhite : StatusDim, right: lw - 4);
        }
        // 스크롤 막대(0x10044e10) — 원본 목록 창은 스크롤을 켜고 만든다. 클릭 칸은 고르기 창 몫으로 따로 모은다.
        int start = _statusHits.Count;
        DrawScrollBar(lx + lw, top, _popupRows * PopupRowH, _popupTop, _popup.Count, _popupRows, t => _popupTop = t);
        _popupHits.AddRange(_statusHits.Skip(start));
        _statusHits.RemoveRange(start, _statusHits.Count - start);
    }
}
