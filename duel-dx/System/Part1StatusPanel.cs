using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 전투의 상태 창 — 파트2 의 화면 가득한 Status 와 달리 인물 곁에 뜨는 패널 하나다(<c>0x10091650</c> + <c>0x10090f70</c>, 분석-파트1-전투 24절 C).
/// </summary>
/// <remarks>
/// <para>170×300 패널을 인물 화면 자리 + (−220, −80) 에(판 밖으로 안 나가게 민다): 얼굴(인물 칸 5) · 이름 · 소속(칸 6 → 이름표 21개) · 직업(칸 7) ·
/// HP · SOUL(150) · EXP(누적 % 100) 막대 · 능력치 3×3(Str Dex Int / Skill Luck Spd / Ac Mr Wtp — 바탕 + 어빌리티 + 장비, 소울로 안 깎은 값) ·
/// 장비 다섯. 오른쪽에 그 인물의 어빌리티와 레벨. 레벨 · RANGE · 저항은 원본도 안 보인다.</para>
/// <para>원본처럼 장비 줄을 누르면 바꾼다(행동을 안 쓴다) — 가방에서 그 칸에 맞는 것을 차례로 끼우고 마지막은 빼기. 직업 바꾸기는 아직 없다.
/// 패널 밖을 누르면 닫힌다. 장비 줄 · 장비 목록 줄을 짚으면 아이템 설명(<see cref="Part1ItemTip"/>).</para>
/// <para>전투 밖에서도 쓴다 — 챕터 지도 상태 창의 얼굴(<see cref="Part1HqStatusWindow"/>, <see cref="OpenOff"/>): 640×480 가운데에 팀 얼굴 띠와 함께, HP 는 최대 · SOUL 은 40.</para>
/// </remarks>
internal sealed class Part1StatusPanel(GameWindow host)
{
    /// <summary>소속 이름표(<c>0x1011c700</c>) — 인물 칸 6.</summary>
    internal static readonly string[] Affiliations =
    [
        "무소속", "시반슈미터", "왕국기사단", "십자군단", "왕국병단", "마법사단", "과학마법사단", "파티마", "천사군", "제국기사단", "제국공군",
        "제국병단", "발키리", "제국마법사단", "흑태자교단", "예니체리", "투르병단", "앙그라교단", "한제국", "커티스공화국", "그라나다상단",
    ];

    internal const int W = 170, H = 345, ListW = 150, EquipRowH = 26;

    private IReadOnlyList<Part1Job>? _jobs;
    private IReadOnlyList<Part1Ability>? _abilities;
    private IReadOnlyList<Part1Item>? _items;

    /// <summary>지난 틀에 그린 패널 · 장비 줄 자리(누르기 판정).</summary>
    private (int X, int Y, int W, int H) _panel;
    private readonly (int X, int Y, int W, int H)[] _equipRows = new (int, int, int, int)[5];

    private (int X, int Y, int W, int H) _jobRow;

    /// <summary>패널을 눌렀다 — 장비 줄이면 바꾸고 true, 패널 안이면 true, 밖이면 false(닫기).</summary>
    internal bool OnClick(UnitState u, int bx, int by) => OnClick(u.ChrCode, bx, by);

    private bool OnClick(int code, int bx, int by)
    {
        static bool In((int X, int Y, int W, int H) r, int px, int py) => px >= r.X && px < r.X + r.W && py >= r.Y && py < r.Y + r.H;
        if (_jobChoices is { } choices)
        {
            // 직업 목록이 떠 있으면 — 줄을 누르면 그 직업으로, 밖을 누르면 그냥 닫는다.
            for (int i = 0; i < choices.Count; i++)
                if (In(JobChoiceRow(i), bx, by)) { ChangeJob(code, choices[i]); break; }
            _jobChoices = null;
            return true;
        }
        if (_equipChoices is { } items)
        {
            // 장비 목록 — 줄을 누르면 그것을 낀다(0 은 「벗기」), 밖을 누르면 닫는다.
            for (int i = 0; i < items.Count; i++)
                if (In(EquipChoiceRow(i), bx, by)) { Equip(code, _equipSlot, items[i]); break; }
            _equipChoices = null;
            return true;
        }
        if (In(_jobRow, bx, by)) { OpenJobChoices(code); return true; }
        for (int s = 0; s < 5; s++)
            if (In(_equipRows[s], bx, by)) { OpenEquipChoices(code, s); return true; }
        return In(_panel, bx, by);
    }

    /// <summary>떠 있는 직업 목록(원본처럼 직업 줄을 누르면 고를 수 있는 직업이 창으로 뜬다 — 사용자 화면) — 없으면 null.</summary>
    private List<int>? _jobChoices;

    internal const int JobRowH = 24, JobBoxW = 150;

    private (int X, int Y, int W, int H) JobChoiceRow(int i) => (_jobRow.X + 6, _jobRow.Y + _jobRow.H + 10 + i * JobRowH, JobBoxW - 12, JobRowH - 2);

    /// <summary>
    /// 직업 줄을 누르면 — 바꿀 수 있는 직업 목록을 띄운다(원본 0x1009e6f0: Job.dat 머리 짝 다섯이 조건 — 그 어빌리티를 그 레벨 이상). 행동은 안 쓴다.
    /// </summary>
    private void OpenJobChoices(int code)
    {
        if (!host.Part1.IsOurs(code) || Part1Chr.Load(code - Part1Game.ChrBase) is not { } raw) { host.Toast("일행만 직업을 바꿀 수 있습니다"); return; }
        var chr = host.Part1.Apply(raw);
        _jobs ??= Part1Job.LoadAll();
        var options = _jobs.Where(j => j.Id != chr.Fields[7] && j.Firsts.Any(f => f.A > 0)
                                       && j.Firsts.Where(f => f.A > 0).All(f => chr.AbilityLevel(f.A) >= Math.Max(1, f.B)))
                           .Select(j => j.Id).ToList();
        if (options.Count == 0) { host.Toast("바꿀 수 있는 직업이 없습니다"); return; }
        _jobChoices = options;
    }

    /// <summary>
    /// 그 직업으로 전직. 원본은 이번 레벨의 경험치를 잃는다(0x1011c20c 「직업을 바꾸면 현재 경험치가 없어지고…」 — 누적을 100 단위로 내린다) —
    /// 리메이크는 모드 &gt; 편의성 「전직할 때 EXP 없애기」를 켰을 때만 그렇게 한다(기본은 그대로 둔다, 사용자 요청).
    /// </summary>
    private void ChangeJob(int code, int job)
    {
        if (Part1Chr.Load(code - Part1Game.ChrBase) is not { } raw) return;
        var chr = host.Part1.Apply(raw);
        var growth = host.Part1.GrowthOf(code, chr.Experience);
        growth.Job = job;
        if (host._dropJobExp) growth.Experience = growth.Experience / 100 * 100;
        host._db?.ForgetPart1Characters();
        host.Btl.Part1.Forget(code);
        host.Btl.RefreshPart1Abilities(code);   // 전투 안에서 바꾸면 그 판의 기술 목록 · 정보 창도 바로 새 직업 것으로(사용자 보고: 록슬리 과학자로 돌아왔는데 화이어 애로우가 없다)
        string name = Part1Job.ById(_jobs!, job)?.Name ?? "";
        // 받침이 있으면 「으로」(ㄹ 받침은 「로」) — 「무슬림 로 전직」이 어색했다.
        bool batchim = name.Length > 0 && name[^1] is >= '가' and <= '힣' && (name[^1] - '가') % 28 is not (0 or 8);
        host.Toast($"{name}{(batchim ? "으로" : "로")} 전직{(host._dropJobExp ? " — 이번 레벨의 경험치는 없어집니다" : "")}");
        _onChange?.Invoke();
    }

    /// <summary>떠 있는 장비 목록(슬롯을 누르면 그 칸에 낄 수 있는 가방의 아이템이 뜬다 — 원본 0x100925f0, 사용자 화면) — 0 은 「벗기」.</summary>
    private List<int>? _equipChoices;
    private int _equipSlot;

    private (int X, int Y, int W, int H) EquipChoiceRow(int i) =>
        (_equipRows[_equipSlot].X + 6, _equipRows[_equipSlot].Y + EquipRowH + 10 + i * JobRowH, EquipBoxW - 12, JobRowH - 2);

    internal const int EquipBoxW = 190;

    /// <summary>슬롯을 누르면 — 그 칸에 맞는 가방의 아이템 목록(일행 창 Part1PartyScreen 과 같은 규칙). 일행이 아니면(용병 · 적) 못 바꾼다.</summary>
    private void OpenEquipChoices(int code, int slot)
    {
        if (!host.Part1.IsOurs(code) || Part1Chr.Load(code - Part1Game.ChrBase) is not { } raw) { host.Toast("일행만 장비를 바꿀 수 있습니다"); return; }
        var chr = host.Part1.Apply(raw);
        _items ??= Part1Item.LoadAll();
        var options = host.Part1.Bag.Distinct().Where(id => id != chr.Equipment[slot] && Part1Item.ById(_items, id) is { } item && item.CanEquip(chr, slot, chr.Equipment, _items)).ToList();
        if (chr.Equipment[slot] != 0) options.Add(0);
        if (options.Count == 0) { host.Toast("가방에 이 칸에 맞는 것이 없습니다"); return; }
        _equipSlot = slot;
        _equipChoices = options;
    }

    /// <summary>그 칸에 <paramref name="next"/> 를 끼운다(끼던 것은 가방으로, 0 이면 벗기만).</summary>
    private void Equip(int code, int slot, int next)
    {
        if (Part1Chr.Load(code - Part1Game.ChrBase) is not { } raw) return;
        var chr = host.Part1.Apply(raw);
        var slots = (int[])chr.Equipment.Clone();
        int old = slots[slot];
        if (next == old) return;
        if (next != 0) host.Part1.Bag.Remove(next);
        if (old != 0) host.Part1.Bag.Add(old);
        slots[slot] = next;
        host.Part1.Equipped[code] = slots;
        host._db?.ForgetPart1Characters();
        host.Btl.Part1.Forget(code);
        host.Btl.RefreshPart1Abilities(code);   // 장비가 여는 어빌리티(앤젤 블래스 → 블래스)를 그 판 목록에도 바로
        _onChange?.Invoke();
    }

    /// <summary>아이템 그림(Obs 0012 모션 = 아이템 +0x45)을 size 칸에 — UI 층(목록 창 위에 보이게). 없으면 그리지 않는다.</summary>
    private void DrawItemIcon(Part1Item? item, int x, int y, int size = 22)
    {
        if (item == null || host.UiFor(Part1Game.ObsBase + 12)?.FrameAt(item.Icon, 0) is not { } icon) return;
        host.UiSprite(Part1Game.ObsBase + 12, item.Icon, 0, x + (size - icon.W) / 2 - icon.X, y + (size - icon.H) / 2 - icon.Y);
    }

    /// <summary>
    /// 능력치 설명 — 원본 식에서 그 값이 쓰이는 곳(분석-파트1-전투: 피해 세기 식 · 방어 · 회피 · 막기 · 상태이상 · 이동 · 차례 · 처치 보상).
    /// 싸울 때 능력치는 SOUL 로 깎인다(150 이면 그대로, 0 이면 ⅓) — 여기 보이는 값은 깎기 전이다.
    /// </summary>
    private static readonly Dictionary<string, string> StatTips = new()
    {
        ["Str"] = "STR — 칼 · 도끼 같은 무기 공격의 세기(무기 공격력 + 10) × STR,$n창 · 활 · 둘째 손 무기 식에도 들어간다$n싸울 때는 SOUL 로 깎인다(150 이면 그대로)",
        ["Dex"] = "DEX — 피하기 확률(DEX × 100 ÷ (상대 명중값 + DEX)) · 막기 확률$n총 · 단검 공격의 세기((DEX + SKILL) × 무게)$n싸울 때는 SOUL 로 깎인다",
        ["Int"] = "INT — 마법 피해 · 회복 · 상태이상 거는 힘(INT²)$n싸울 때는 SOUL 로 깎인다",
        ["Skill"] = "SKILL — 무기 공격의 세기(창 · 활 · 총 · 둘째 손 무기 식)$n싸울 때는 SOUL 로 깎인다",
        ["Luck"] = "LUCK — 상태이상을 버티는 힘(100 − LUCK % 로 걸린다)$n처치할 때 받는 eld(LUCK × 상대 경험치 ÷ 1000)",
        ["Spd"] = "SPD — 이동력(SPD × 2 — 평지 한 칸 4, 높이 한 단 +2)",
        ["Ac"] = "AC — 무기 · 물리 공격을 받을 때의 방어(피해 = 세기 ÷ AC)$n싸울 때는 SOUL 로 깎인다",
        ["Mr"] = "MR — 마법 공격을 받을 때의 방어(피해 = 세기 ÷ MR)$n상태이상 · 보조 마법이 걸리는 길이에도 들어간다$n싸울 때는 SOUL 로 깎인다",
        ["Wtp"] = "WTP — 차례가 오는 간격(틱) — 작을수록 자주 움직인다",
    };

    /// <summary>파트1 판의 파트1 인물이면 이 창을 쓴다.</summary>
    internal bool Applies(UnitState u) => host._battleLoaded && host._scene.IsPart1 && u.ChrCode >= Part1Game.ChrBase && !host.Mos._mosesOpen;

    /// <summary>두 빛깔 사이 — <paramref name="at"/> / <paramref name="span"/> 만큼 간 빛깔(불투명).</summary>
    private static uint Lerp(uint from, uint to, int at, int span)
    {
        uint Ch(int s) => (uint)(((int)(from >> s & 0xFF) * (span - at) + (int)(to >> s & 0xFF) * at) / Math.Max(1, span));
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    internal void Draw(UnitState u)
    {
        if (host.Btl.Part1.Fighter(u) is not { } fighter) return;
        var (fx, fy) = host.Btl.UnitFoot(u);
        int x = Math.Clamp(fx - 220, host._camX + 4, host._camX + host.ViewWidth - W - ListW - 8);
        int y = Math.Clamp(fy - 80 - 40, host._camY + 4, host._camY + host.ViewHeight - H - 4);
        _layer = false;
        DrawAt(fighter, u.Hp, u.MaxHp, u.Soul, x, y, host.StatusScr._statusRightHits);
        _panel = (x, y, W + 4 + ListW, H);
    }

    // ── 전투 밖(챕터 지도 상태 창의 얼굴) ─────────────────────────────────────

    /// <summary>전투 밖에서 보는 인물(20000 + 파트1 번호) — 0 이면 닫혀 있다.</summary>
    private int _offChr;
    private List<int> _offMembers = [];
    private Action? _onChange;

    /// <summary>전투 중에 연 패널이면 그 사람의 판 위 인물(일행 한눈에 <see cref="Part1PartyTable"/>) — 있으면 HP · SOUL · 능력치를 그 판 값으로.</summary>
    private Func<int, UnitState?>? _offUnitOf;

    /// <summary>전투 밖 패널에서 오른 단추를 누를 자리(어빌리티 줄 설명) — 전투는 StatusScreen 의 목록을 쓴다.</summary>
    private readonly List<(int X, int Y, int W, int H, Action Click)> _offRightHits = [];

    /// <summary>위 얼굴 띠의 칸(팀 사람 차례) — 누르면 그 사람으로.</summary>
    private readonly List<(int X, int Y, int W, int H)> _strip = [];

    /// <summary>그림 · 바탕을 새 UI 층에 그리나 — 전투 밖은 지도의 층 글(창 · 단추 이름)이 패널 밑에서 비치지 않게 바탕부터 층에 깐다.</summary>
    private bool _layer;

    internal bool OffOpen => _offChr != 0;

    internal const int StripH = 40, StripGap = 4, StripCell = 31;

    /// <summary>
    /// 전투 밖에서 그 사람의 패널을 연다 — 챕터 지도 상태 창의 얼굴(원본 인물 상세 창 0x100bb580 자리). HP 는 최대, SOUL 은 전투 처음 값 40(<see cref="Part1Combat.GaugeOnTurn"/>).
    /// <paramref name="members"/> 는 ←/→ · 위 얼굴 띠로 오갈 사람들, <paramref name="onChange"/> 는 장비 · 직업을 바꾼 뒤(진행 저장).
    /// </summary>
    internal void OpenOff(int chr, List<int> members, Action? onChange, Func<int, UnitState?>? unitOf = null)
    {
        _offUnitOf = unitOf;
        _offChr = chr;
        _offMembers = members;
        _onChange = onChange;
        _jobChoices = null;
        _equipChoices = null;
        host.StatusScr._statusTip = null;
    }

    internal void CloseOff()
    {
        _offChr = 0;
        _jobChoices = null;
        _equipChoices = null;
        host.StatusScr._statusTip = null;
    }

    /// <summary>←/→ — 팀의 앞 · 뒤 사람으로.</summary>
    internal void SwitchOff(int delta)
    {
        if (_offMembers.Count == 0) return;
        int i = Math.Max(0, _offMembers.IndexOf(_offChr));
        _offChr = _offMembers[(i + delta + _offMembers.Count) % _offMembers.Count];
        _jobChoices = null;
        _equipChoices = null;
        host.StatusScr._statusTip = null;
    }

    /// <summary>전투 밖 패널을 눌렀다 — 얼굴 띠는 사람 바꾸기, 패널은 전투와 같고, 밖이면 닫는다.</summary>
    internal void OffClick(int bx, int by)
    {
        if (_jobChoices == null && _equipChoices == null)
            for (int i = 0; i < _strip.Count && i < _offMembers.Count; i++)
                if (bx >= _strip[i].X && bx < _strip[i].X + _strip[i].W && by >= _strip[i].Y && by < _strip[i].Y + _strip[i].H)
                {
                    _offChr = _offMembers[i];
                    host.StatusScr._statusTip = null;
                    return;
                }
        if (!OnClick(_offChr, bx, by)) CloseOff();
    }

    /// <summary>오른 단추 — 떠 있는 목록은 닫고, 어빌리티 줄이면 설명(떼면 사라진다), 패널 밖이면 패널을 닫는다.</summary>
    internal void OffRightDown(int bx, int by)
    {
        if (_jobChoices != null || _equipChoices != null) { _jobChoices = null; _equipChoices = null; return; }
        foreach (var (x, y, w, h, click) in _offRightHits)
            if (bx >= x && bx < x + w && by >= y && by < y + h) { click(); return; }
        if (bx < _panel.X || by < _panel.Y || bx >= _panel.X + _panel.W || by >= _panel.Y + _panel.H) CloseOff();
    }

    /// <summary>전투 밖 패널 — 640×480 가운데에, 위에 팀 사람 얼굴 띠. 뒤 지도는 어둡게 한다.</summary>
    internal void DrawOff(int ox, int oy)
    {
        if (Part1Chr.Load(_offChr - Part1Game.ChrBase) is not { } raw) { CloseOff(); return; }
        _items ??= Part1Item.LoadAll();
        var chr = host.Part1.Apply(raw);
        var fighter = Part1Fighter.From(chr, _items);
        var unit = _offUnitOf?.Invoke(_offChr);
        if (unit != null && host.Btl.Part1.Fighter(unit) is { } inBattle) fighter = inBattle;
        _layer = host._hiResUi;
        _offRightHits.Clear();
        host.UiDarken(ox, oy, 640, 480, 14, 31);

        const int total = W + 4 + ListW;
        int x = ox + (640 - total) / 2, y0 = oy + (480 - StripH - StripGap - H) / 2;

        // 얼굴 띠 — 팀 사람마다 얼굴(줄여서), 보는 사람은 노란 테. 누르거나 ←/→ 로 바꾼다.
        Bg(x, y0, total, StripH, 0xFF101828);
        host.UiStroke(x, y0, total, StripH, 0xFF8098C8);
        _strip.Clear();
        for (int i = 0; i < _offMembers.Count && i < 10; i++)
        {
            int cx = x + 6 + i * StripCell, cy = y0 + 5;
            _strip.Add((cx, cy, StripCell - 2, StripCell - 2));
            if (Part1Chr.Load(_offMembers[i] - Part1Game.ChrBase) is { } offRaw && host.Part1.FaceObs(host.Part1.Apply(offRaw)) is > 0 and var offFace && host.UiFor(offFace)?.FrameAt(0, 0) is { } face)
                Img(face, cx, cy, StripCell - 2, StripCell - 2);
            bool hot = host._mouse.X >= cx && host._mouse.X < cx + StripCell - 2 && host._mouse.Y >= cy && host._mouse.Y < cy + StripCell - 2;
            if (_offMembers[i] == _offChr) host.UiStroke(cx - 1, cy - 1, StripCell, StripCell, 0xFFFFE080);
            else if (hot) host.UiStroke(cx - 1, cy - 1, StripCell, StripCell, 0xFFB0C8FF);
        }
        host.UiRightText("←/→", x + total - 8, y0 + 13, 0xFF8098C8, 11);

        int y = y0 + StripH + StripGap;
        int maxHp = Math.Max(1, chr.MaxHp);
        if (unit != null) DrawAt(fighter, unit.Hp, unit.MaxHp, unit.Soul, x, y, _offRightHits);
        else DrawAt(fighter, maxHp, maxHp, 40, x, y, _offRightHits);
        _panel = (x, y0, total, StripH + StripGap + Math.Max(H, _listH));
    }

    /// <summary>바탕 — 전투는 그림 층(층 바탕은 판에 그린 얼굴을 덮는다), 전투 밖은 그림도 층에 그리므로 층에.</summary>
    private void Bg(int x, int y, int w, int h, uint color)
    {
        if (_layer) host.UiFill(x, y, w, h, color); else host.FillRect(x, y, w, h, color);
    }

    /// <summary>그림 한 장을 네모에 맞춰 줄여 — 판(<see cref="GameWindow.BlitScaled"/>) 또는 층.</summary>
    private void Img(SpriteFrame f, int x, int y, int w, int h)
    {
        if (!_layer) { host.BlitScaled(f, x, y, w, h); return; }
        if (f.W == 0 || f.H == 0) return;
        double scale = Math.Min((double)w / f.W, (double)h / f.H);
        int dw = Math.Max(1, (int)(f.W * scale)), dh = Math.Max(1, (int)(f.H * scale));
        host.Hi.DrawImage(f.Px, f.W, f.W, f.H, x + (w - dw) / 2, y + (h - dh) / 2, dw, dh);
    }

    /// <summary>지난 틀의 어빌리티 목록 높이.</summary>
    private int _listH;

    private void DrawAt(Part1Fighter fighter, int hp, int maxHp, int soul, int x, int y, List<(int X, int Y, int W, int H, Action Click)> rightHits)
    {
        var chr = fighter.Chr;
        _jobs ??= Part1Job.LoadAll();
        _abilities ??= Part1Ability.LoadAll();
        _items ??= Part1Item.LoadAll();
        Bg(x, y, W, H, 0xFF101828);   // 바탕은 그림 층에 — UI 층(UiFill)은 얼굴 그림을 덮는다
        host.UiStroke(x, y, W, H, 0xFF8098C8);

        if (host.Part1.FaceObs(chr) is > 0 and var faceObs)
        {
            if (_layer) host.UiSprite(faceObs, 0, 0, x + 30, y + 30);
            else host.DrawUi(faceObs, 0, 0, x + 30, y + 30, GameWindow.UiBlend.Alpha);
        }
        host.UiText(chr.Name, x + 60, y + 10, 0xFFFFE080, 13);
        int affiliation = chr.Fields[6];
        host.UiText(affiliation >= 0 && affiliation < Affiliations.Length ? Affiliations[affiliation] : "", x + 60, y + 28, 0xFFB0C0D8, 11);
        _jobRow = (x + 56, y + 42, W - 62, 16);
        if (host._mouse.X >= _jobRow.X && host._mouse.X < _jobRow.X + _jobRow.W && host._mouse.Y >= _jobRow.Y && host._mouse.Y < _jobRow.Y + _jobRow.H)
            host.UiFill(_jobRow.X, _jobRow.Y, _jobRow.W, _jobRow.H, 0xC0304870);
        host.UiText(Part1Job.ById(_jobs, chr.Fields[7])?.Name ?? "—", x + 60, y + 44, GameWindow.White, 11);

        int row = y + 66;
        // 막대는 원본 인물 창처럼 왼쪽 → 오른쪽 그라데이션(사용자 요청) — HP 빨강 → 노랑, SOUL 보라 → 분홍, EXP 파랑 → 하늘.
        // 빛깔은 막대 전체 길이에 걸쳐 정하고 찬 만큼만 칠한다(원본 화면을 보고 고른 값).
        void Bar(string label, int value, int max, uint from, uint to)
        {
            host.UiText($"{label} {value}/{max}", x + 10, row, GameWindow.White, 11);
            host.UiFill(x + 10, row + 15, ListW, 3, 0xFF303848);
            int filled = max > 0 ? ListW * Math.Clamp(value, 0, max) / max : 0;
            for (int bx = 0; bx < filled; bx += 2)
                host.UiFill(x + 10 + bx, row + 15, Math.Min(2, filled - bx), 3, Lerp(from, to, bx, ListW));
            row += 22;
        }
        Bar("HP", hp, maxHp, 0xFFC02828, 0xFFF0D040);
        Bar("SOUL", soul, 150, 0xFF7838C0, 0xFFE8A8E8);
        Bar("EXP", chr.Experience % 100, 100, 0xFF2850C0, 0xFF68E0F0);

        string? hoverStat = null;
        (string Label, int Field)[] stats =
        [
            ("Str", Part1Chr.Str), ("Dex", Part1Chr.Dex), ("Int", Part1Chr.Int),
            ("Skill", Part1Chr.Skill), ("Luck", Part1Chr.Luck), ("Spd", Part1Chr.Spd),
            ("Ac", Part1Chr.Ac), ("Mr", Part1Chr.Mr), ("Wtp", Part1Chr.Interval),
        ];
        for (int k = 0; k < stats.Length; k++)
        {
            int value = stats[k].Field == Part1Chr.Interval ? chr.ActionInterval : fighter.Stat(stats[k].Field);
            int sx = x + 10 + k % 3 * 52, sy = row + k / 3 * 17;
            host.UiText($"{stats[k].Label} {value}", sx, sy, GameWindow.White, 11);
            // 마우스를 올리면 그 능력치가 어디에 쓰이는지(원본 식 — Part1Combat.Power · Damage · EvadeChance · StatusLands, 사용자 요청).
            if (hoverStat == null && host._mouse.X >= sx && host._mouse.X < sx + 50 && host._mouse.Y >= sy - 1 && host._mouse.Y < sy + 16)
                hoverStat = StatTips.GetValueOrDefault(stats[k].Label);
        }
        row += 3 * 17 + 8;

        // 장비 다섯 칸 — 줄마다 아이템 그림(Obs 0012 모션 = 아이템 +0x45) · 칸 이름 · 아이템 이름. 누르면 낄 것 목록, 짚으면 아이템 설명.
        Part1Item? hoverItem = null;
        for (int s = 0; s < 5; s++)
        {
            var item = Part1Item.ById(_items, chr.Equipment[s]);
            _equipRows[s] = (x + 6, row - 2, W - 12, EquipRowH - 2);
            bool hot = host._mouse.X >= x + 6 && host._mouse.X < x + W - 6 && host._mouse.Y >= row - 2 && host._mouse.Y < row + EquipRowH - 4;
            if (hot) { host.UiFill(x + 6, row - 2, W - 12, EquipRowH - 2, 0xC0304870); hoverItem = item; }
            DrawItemIcon(item, x + 10, row);
            host.UiText($"{Part1PartyScreen.SlotName(s, item)}: {item?.Name ?? "—"}", x + 36, row + 4, s == 0 ? GameWindow.White : 0xFFD0D8E8, 11);
            row += EquipRowH;
        }

        // 어빌리티 — 오른쪽 목록(원본도 보기만 한다), 이름 · LV. 원본 전투 목록(0x1009dda0)처럼 <b>지금 직업으로 쓰는 것</b>만 —
        // 지금 직업이 가르치는 것(Job.dat 14칸)은 「지금/최대」로, 아직 안 배운 것도 흐리게 함께(무엇을 몇까지 올릴 수 있는지, 사용자 요청),
        // 장비가 여는 것(해금 보정)과 아이템 기술(초필살기 제어 + 무기 · 소환 능력 + 옷 — 스탐블링거 → 폭풍검)은 「LV」로.
        // 다른 직업에서 배운 것(롤랑의 초필살기 제어 · 댓쉬 · 비 — 나이트는 안 가르친다)은 줄로 늘어놓지 않고 개수만 적는다 — 배운 레벨은 남아서
        // 그것을 가르치는 직업으로 바꾸면 바로 다시 나온다(사용자 보고: 「나이트에서 초필살기 제어 못 배우는데 왜 있지」).
        int lx = x + W + 4, ly = y;
        var job = Part1Job.ById(_jobs, chr.Fields[7]);
        var teach = job?.Abilities.Where(a => a.Ability > 0 && a.Level > 0).GroupBy(a => a.Ability).ToDictionary(g => g.Key, g => g.Max(a => a.Level)) ?? [];
        var learned = chr.Abilities.Where(a => a.Ability > 0 && a.Level > 0).ToList();
        var rows = learned.Where(a => teach.ContainsKey(a.Ability) || chr.ListedLevel(a.Ability) > 0).Select(a => (a.Ability, a.Level)).ToList();
        foreach (var (ability, _) in teach) if (!rows.Any(r => r.Ability == ability)) rows.Add((ability, 0));
        foreach (int ability in chr.ItemSkills().Concat(chr.Equipment.Where(e => e > 0).Select(e => Part1Item.ById(_items, e)).OfType<Part1Item>()
                                                   .SelectMany(i => i.Bonuses).Select(b => Part1Bonus.UnlockedAbility(b.Code)).Where(id => id > 0)))
            if (!rows.Any(r => r.Ability == ability) && chr.ListedLevel(ability) > 0) rows.Add((ability, chr.ListedLevel(ability)));
        int otherJob = learned.Count(a => !rows.Any(r => r.Ability == a.Ability) && Part1Ability.ById(_abilities, a.Ability)?.Kind is not (2 or 4));   // 직접 배운 아이템 기술(폭풍검)은 직업 것이 아니다
        // 쓰는 레벨(원본 0x1009e020) — 직업 최대보다 높으면 최대로 막힌다.
        var usable = rows.Select(r => r.Ability).Distinct().ToDictionary(a => a, chr.ListedLevel);
        bool anyLocked = otherJob > 0;
        const int RowH = 16;
        int lh = 10 + Math.Max(1, rows.Count) * RowH + (teach.Count > 0 ? 16 : 0) + (anyLocked ? 14 : 0);
        _listH = lh;
        Bg(lx, ly, ListW, lh, 0xFF101828);   // 그림 층 — 아이콘(그림 층)이 덮이지 않게
        host.UiStroke(lx, ly, ListW, lh, 0xFF8098C8);
        if (rows.Count == 0) host.UiText("없음", lx + 8, ly + 6, 0xFF808890, 11);
        for (int i = 0; i < rows.Count; i++)
        {
            var (ability, level) = rows[i];
            var info = Part1Ability.ById(_abilities, ability);
            string name = info?.Name ?? $"#{ability}";
            int ry = ly + 6 + i * RowH;
            // 아이콘 — Obs 0011 의 모션 (Abi +0x28) + 0x13(0x10090970, 분석-파트1-전투 24절 B).
            if (info != null && host.UiFor(Part1Game.ObsBase + 11)?.FrameAt(info.Tail + 0x13, 0) is { } icon)
                Img(icon, lx + 6, ry, 14, 14);
            bool taught = teach.TryGetValue(ability, out int max);
            int use = usable[ability];
            bool locked = level > 0 && use <= 0;
            uint nameColor = level == 0 || locked ? 0xFF808890 : GameWindow.White;
            host.UiText(name, lx + 24, ry, nameColor, 11);
            // 지금 직업이 가르치면 「지금/최대」(다 올렸으면 초록), 아니면 지금 단계만. 못 쓰면 「(배운 레벨)」(흐리게),
            // 직업 최대로 막혔으면 「쓰는 (배운 N)」.
            string lv = locked ? $"({level})"
                      : level > 0 && use > 0 && use != level ? $"{use} (배운 {level})"
                      : taught ? $"{level}/{max}" : $"LV{level}";
            uint lvColor = locked ? 0xFF606870 : !taught ? 0xFFFFE080 : level >= max ? 0xFF80E080 : level == 0 ? 0xFF808890 : 0xFFFFE080;
            var (_, lw, _) = host.UiGetText(lv, lvColor, 11);
            host.UiText(lv, lx + ListW - 8 - lw, ry, lvColor, 11);
            // 오른쪽 단추를 누르고 있는 동안 설명(원본엔 글이 없어 자료로 엮는다 — Part1AbilityTip). 떼면 사라진다(WM_RBUTTONUP).
            int tipMax = taught ? max : 0;
            rightHits.Add((lx, ry - 1, ListW, RowH, () =>
            {
                if (Part1AbilityTip.Text(ability, level, tipMax) is { } tip)
                    host.StatusScr.ShowStatusTip(tip + (locked ? "\n지금 직업에서는 못 씀" : level > 0 && use > 0 && use < level ? $"\n지금 직업에서는 {use} 까지만 씀" : ""));
            }));
        }
        if (teach.Count > 0)
            host.UiText($"{job!.Name} — 지금/최대", lx + 8, ly + 6 + rows.Count * RowH + 2, 0xFF8098C8, 10);
        if (anyLocked)
            host.UiText($"다른 직업에서 배운 것 {otherJob}개", lx + 8, ly + 6 + rows.Count * RowH + (teach.Count > 0 ? 16 : 0) + 2, 0xFF808890, 10);

        // 장비 목록 — 누른 슬롯 아래에 틀 창, 줄마다 그림 · 이름. 짚은 줄은 아이템 설명.
        if (_equipChoices is { } equipChoices)
        {
            hoverItem = null;
            var (ex, ey, _, _) = _equipRows[_equipSlot];
            int eh = 12 + equipChoices.Count * JobRowH;
            host.UiFill(ex, ey + EquipRowH + 4, EquipBoxW, eh, 0xFF201838);
            host.UiFrame(ex, ey + EquipRowH + 4, EquipBoxW, eh);
            for (int i = 0; i < equipChoices.Count; i++)
            {
                var (rx, ry, rw, rh) = EquipChoiceRow(i);
                var item = Part1Item.ById(_items, equipChoices[i]);
                if (host._mouse.X >= rx && host._mouse.X < rx + rw && host._mouse.Y >= ry && host._mouse.Y < ry + rh)
                {
                    host.UiFill(rx, ry, rw, rh, 0x60A090E0);
                    hoverItem = item;
                }
                if (item != null) DrawItemIcon(item, rx + 2, ry - 1, 22);
                host.UiText(item?.Name ?? "벗기", rx + 28, ry + 3, GameWindow.White, 12);
            }
        }

        // 직업 목록 — 직업 줄 아래에 틀 창, 줄마다 구슬 · 이름(원본 꼴). 짚은 줄은 밝게.
        if (_jobChoices is { } choices)
        {
            hoverItem = null;
            // 틀 바탕이 비쳐 뒤 패널 글이 보였다 — 먼저 불투명하게 깐다.
            host.UiFill(_jobRow.X, _jobRow.Y + _jobRow.H + 4, JobBoxW, 12 + choices.Count * JobRowH, 0xFF201838);
            host.UiFrame(_jobRow.X, _jobRow.Y + _jobRow.H + 4, JobBoxW, 12 + choices.Count * JobRowH);
            for (int i = 0; i < choices.Count; i++)
            {
                var (rx, ry, rw, rh) = JobChoiceRow(i);
                if (host._mouse.X >= rx && host._mouse.X < rx + rw && host._mouse.Y >= ry && host._mouse.Y < ry + rh)
                    host.UiFill(rx, ry, rw, rh, 0x60A090E0);
                host.UiText("●", rx + 4, ry + 3, 0xFFC8B8F0, 12);
                host.UiText(Part1Job.ById(_jobs, choices[i])?.Name ?? $"#{choices[i]}", rx + 24, ry + 3, GameWindow.White, 12);
            }
        }

        if (host.StatusScr._statusTip is { } shown)
            host.StatusScr.DrawDescriptionTip(shown.Text, shown.X, shown.Y, host._camX, host._camY, host.ViewWidth, host.ViewHeight);
        else if (hoverStat != null)
            host.StatusScr.DrawDescriptionTip(hoverStat, host._mouse.X, host._mouse.Y, host._camX, host._camY, host.ViewWidth, host.ViewHeight);
        else if (Part1ItemTip.Text(hoverItem) is { } itemTip)
            host.StatusScr.DrawDescriptionTip(itemTip, host._mouse.X, host._mouse.Y, host._camX, host._camY, host.ViewWidth, host.ViewHeight);
    }
}
