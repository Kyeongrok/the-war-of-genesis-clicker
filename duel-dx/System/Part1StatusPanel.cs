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
/// 패널 밖을 누르면 닫힌다.</para>
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
    internal bool OnClick(UnitState u, int bx, int by)
    {
        static bool In((int X, int Y, int W, int H) r, int px, int py) => px >= r.X && px < r.X + r.W && py >= r.Y && py < r.Y + r.H;
        if (_jobChoices is { } choices)
        {
            // 직업 목록이 떠 있으면 — 줄을 누르면 그 직업으로, 밖을 누르면 그냥 닫는다.
            for (int i = 0; i < choices.Count; i++)
                if (In(JobChoiceRow(i), bx, by)) { ChangeJob(u, choices[i]); break; }
            _jobChoices = null;
            return true;
        }
        if (_equipChoices is { } items)
        {
            // 장비 목록 — 줄을 누르면 그것을 낀다(0 은 「벗기」), 밖을 누르면 닫는다.
            for (int i = 0; i < items.Count; i++)
                if (In(EquipChoiceRow(i), bx, by)) { Equip(u, _equipSlot, items[i]); break; }
            _equipChoices = null;
            return true;
        }
        if (In(_jobRow, bx, by)) { OpenJobChoices(u); return true; }
        for (int s = 0; s < 5; s++)
            if (In(_equipRows[s], bx, by)) { OpenEquipChoices(u, s); return true; }
        return In(_panel, bx, by);
    }

    /// <summary>떠 있는 직업 목록(원본처럼 직업 줄을 누르면 고를 수 있는 직업이 창으로 뜬다 — 사용자 화면) — 없으면 null.</summary>
    private List<int>? _jobChoices;

    internal const int JobRowH = 24, JobBoxW = 150;

    private (int X, int Y, int W, int H) JobChoiceRow(int i) => (_jobRow.X + 6, _jobRow.Y + _jobRow.H + 10 + i * JobRowH, JobBoxW - 12, JobRowH - 2);

    /// <summary>
    /// 직업 줄을 누르면 — 바꿀 수 있는 직업 목록을 띄운다(원본 0x1009e6f0: Job.dat 머리 짝 다섯이 조건 — 그 어빌리티를 그 레벨 이상). 행동은 안 쓴다.
    /// </summary>
    private void OpenJobChoices(UnitState u)
    {
        if (!host.Part1.IsOurs(u.ChrCode) || Part1Chr.Load(u.ChrCode - Part1Game.ChrBase) is not { } raw) { host.Toast("일행만 직업을 바꿀 수 있습니다"); return; }
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
    private void ChangeJob(UnitState u, int job)
    {
        if (Part1Chr.Load(u.ChrCode - Part1Game.ChrBase) is not { } raw) return;
        var chr = host.Part1.Apply(raw);
        var growth = host.Part1.GrowthOf(u.ChrCode, chr.Experience);
        growth.Job = job;
        if (host._dropJobExp) growth.Experience = growth.Experience / 100 * 100;
        host._db?.ForgetPart1Characters();
        host.Btl.Part1.Forget(u.ChrCode);
        string name = Part1Job.ById(_jobs!, job)?.Name ?? "";
        // 받침이 있으면 「으로」(ㄹ 받침은 「로」) — 「무슬림 로 전직」이 어색했다.
        bool batchim = name.Length > 0 && name[^1] is >= '가' and <= '힣' && (name[^1] - '가') % 28 is not (0 or 8);
        host.Toast($"{name}{(batchim ? "으로" : "로")} 전직{(host._dropJobExp ? " — 이번 레벨의 경험치는 없어집니다" : "")}");
    }

    /// <summary>떠 있는 장비 목록(슬롯을 누르면 그 칸에 낄 수 있는 가방의 아이템이 뜬다 — 원본 0x100925f0, 사용자 화면) — 0 은 「벗기」.</summary>
    private List<int>? _equipChoices;
    private int _equipSlot;

    private (int X, int Y, int W, int H) EquipChoiceRow(int i) =>
        (_equipRows[_equipSlot].X + 6, _equipRows[_equipSlot].Y + EquipRowH + 10 + i * JobRowH, EquipBoxW - 12, JobRowH - 2);

    internal const int EquipBoxW = 190;

    /// <summary>슬롯을 누르면 — 그 칸에 맞는 가방의 아이템 목록(일행 창 Part1PartyScreen 과 같은 규칙). 일행이 아니면(용병 · 적) 못 바꾼다.</summary>
    private void OpenEquipChoices(UnitState u, int slot)
    {
        if (!host.Part1.IsOurs(u.ChrCode) || Part1Chr.Load(u.ChrCode - Part1Game.ChrBase) is not { } raw) { host.Toast("일행만 장비를 바꿀 수 있습니다"); return; }
        var chr = host.Part1.Apply(raw);
        _items ??= Part1Item.LoadAll();
        var options = host.Part1.Bag.Distinct().Where(id => id != chr.Equipment[slot] && Part1Item.ById(_items, id) is { } item && item.CanEquip(chr, slot, chr.Equipment, _items)).ToList();
        if (chr.Equipment[slot] != 0) options.Add(0);
        if (options.Count == 0) { host.Toast("가방에 이 칸에 맞는 것이 없습니다"); return; }
        _equipSlot = slot;
        _equipChoices = options;
    }

    /// <summary>그 칸에 <paramref name="next"/> 를 끼운다(끼던 것은 가방으로, 0 이면 벗기만).</summary>
    private void Equip(UnitState u, int slot, int next)
    {
        if (Part1Chr.Load(u.ChrCode - Part1Game.ChrBase) is not { } raw) return;
        var chr = host.Part1.Apply(raw);
        var slots = (int[])chr.Equipment.Clone();
        int old = slots[slot];
        if (next == old) return;
        if (next != 0) host.Part1.Bag.Remove(next);
        if (old != 0) host.Part1.Bag.Add(old);
        slots[slot] = next;
        host.Part1.Equipped[u.ChrCode] = slots;
        host._db?.ForgetPart1Characters();
        host.Btl.Part1.Forget(u.ChrCode);
    }

    /// <summary>아이템 그림(Obs 0012 모션 = 아이템 +0x45)을 size 칸에 — UI 층(목록 창 위에 보이게). 없으면 그리지 않는다.</summary>
    private void DrawItemIcon(Part1Item? item, int x, int y, int size = 22)
    {
        if (item == null || host.UiFor(Part1Game.ObsBase + 12)?.FrameAt(item.Icon, 0) is not { } icon) return;
        host.UiSprite(Part1Game.ObsBase + 12, item.Icon, 0, x + (size - icon.W) / 2 - icon.X, y + (size - icon.H) / 2 - icon.Y);
    }

    /// <summary>파트1 판의 파트1 인물이면 이 창을 쓴다.</summary>
    internal bool Applies(UnitState u) => host._battleLoaded && host._scene.IsPart1 && u.ChrCode >= Part1Game.ChrBase && !host.Mos._mosesOpen;

    internal void Draw(UnitState u)
    {
        if (host.Btl.Part1.Fighter(u) is not { } fighter) return;
        var chr = fighter.Chr;
        _jobs ??= Part1Job.LoadAll();
        _abilities ??= Part1Ability.LoadAll();
        _items ??= Part1Item.LoadAll();
        var (fx, fy) = host.Btl.UnitFoot(u);
        int x = Math.Clamp(fx - 220, host._camX + 4, host._camX + host.ViewWidth - W - ListW - 8);
        int y = Math.Clamp(fy - 80 - 40, host._camY + 4, host._camY + host.ViewHeight - H - 4);
        host.FillRect(x, y, W, H, 0xFF101828);   // 바탕은 그림 층에 — UI 층(UiFill)은 얼굴 그림을 덮는다
        _panel = (x, y, W + 4 + ListW, H);
        host.UiStroke(x, y, W, H, 0xFF8098C8);

        if (chr.FaceId > 0) host.DrawUi(Part1Game.ObsBase + chr.FaceId, 0, 0, x + 30, y + 30, GameWindow.UiBlend.Alpha);
        host.UiText(chr.Name, x + 60, y + 10, 0xFFFFE080, 13);
        int affiliation = chr.Fields[6];
        host.UiText(affiliation >= 0 && affiliation < Affiliations.Length ? Affiliations[affiliation] : "", x + 60, y + 28, 0xFFB0C0D8, 11);
        _jobRow = (x + 56, y + 42, W - 62, 16);
        if (host._mouse.X >= _jobRow.X && host._mouse.X < _jobRow.X + _jobRow.W && host._mouse.Y >= _jobRow.Y && host._mouse.Y < _jobRow.Y + _jobRow.H)
            host.UiFill(_jobRow.X, _jobRow.Y, _jobRow.W, _jobRow.H, 0xC0304870);
        host.UiText(Part1Job.ById(_jobs, chr.Fields[7])?.Name ?? "—", x + 60, y + 44, GameWindow.White, 11);

        int row = y + 66;
        void Bar(string label, int value, int max, uint color)
        {
            host.UiText($"{label} {value}/{max}", x + 10, row, GameWindow.White, 11);
            host.UiFill(x + 10, row + 15, ListW, 3, 0xFF303848);
            if (max > 0) host.UiFill(x + 10, row + 15, ListW * Math.Clamp(value, 0, max) / max, 3, color);
            row += 22;
        }
        Bar("HP", u.Hp, u.MaxHp, 0xFF60D060);
        Bar("SOUL", u.Soul, 150, 0xFF60A0F0);
        Bar("EXP", chr.Experience % 100, 100, 0xFFF0C060);

        (string Label, int Field)[] stats =
        [
            ("Str", Part1Chr.Str), ("Dex", Part1Chr.Dex), ("Int", Part1Chr.Int),
            ("Skill", Part1Chr.Skill), ("Luck", Part1Chr.Luck), ("Spd", Part1Chr.Spd),
            ("Ac", Part1Chr.Ac), ("Mr", Part1Chr.Mr), ("Wtp", Part1Chr.Interval),
        ];
        for (int k = 0; k < stats.Length; k++)
        {
            int value = stats[k].Field == Part1Chr.Interval ? chr.ActionInterval : fighter.Stat(stats[k].Field);
            host.UiText($"{stats[k].Label} {value}", x + 10 + k % 3 * 52, row + k / 3 * 17, GameWindow.White, 11);
        }
        row += 3 * 17 + 8;

        // 장비 다섯 칸 — 줄마다 아이템 그림(Obs 0012 모션 = 아이템 +0x45) · 칸 이름 · 아이템 이름. 누르면 낄 것 목록.
        for (int s = 0; s < 5; s++)
        {
            var item = Part1Item.ById(_items, chr.Equipment[s]);
            _equipRows[s] = (x + 6, row - 2, W - 12, EquipRowH - 2);
            bool hot = host._mouse.X >= x + 6 && host._mouse.X < x + W - 6 && host._mouse.Y >= row - 2 && host._mouse.Y < row + EquipRowH - 4;
            if (hot) host.UiFill(x + 6, row - 2, W - 12, EquipRowH - 2, 0xC0304870);
            DrawItemIcon(item, x + 10, row);
            host.UiText($"{Part1PartyScreen.SlotNames[s]}: {item?.Name ?? "—"}", x + 36, row + 4, s == 0 ? GameWindow.White : 0xFFD0D8E8, 11);
            row += EquipRowH;
        }

        // 어빌리티 — 오른쪽 목록(원본도 보기만 한다), 이름 · LV.
        int lx = x + W + 4, ly = y;
        var learned = chr.Abilities.Where(a => a.Ability > 0 && a.Level > 0).ToList();
        int lh = 10 + Math.Max(1, learned.Count) * 16;
        host.FillRect(lx, ly, ListW, lh, 0xFF101828);   // 그림 층 — 아이콘(그림 층)이 덮이지 않게
        host.UiStroke(lx, ly, ListW, lh, 0xFF8098C8);
        if (learned.Count == 0) host.UiText("없음", lx + 8, ly + 6, 0xFF808890, 11);
        for (int i = 0; i < learned.Count; i++)
        {
            var info = Part1Ability.ById(_abilities, learned[i].Ability);
            string name = info?.Name ?? $"#{learned[i].Ability}";
            // 아이콘 — Obs 0011 의 모션 (Abi +0x28) + 0x13(0x10090970, 분석-파트1-전투 24절 B).
            if (info != null && host.UiFor(Part1Game.ObsBase + 11)?.FrameAt(info.Tail + 0x13, 0) is { } icon)
                host.BlitScaled(icon, lx + 6, ly + 6 + i * 16, 14, 14);
            host.UiText(name, lx + 24, ly + 6 + i * 16, GameWindow.White, 11);
            host.UiText($"LV{learned[i].Level}", lx + ListW - 34, ly + 6 + i * 16, 0xFFFFE080, 11);
        }

        // 장비 목록 — 누른 슬롯 아래에 틀 창, 줄마다 그림 · 이름.
        if (_equipChoices is { } equipChoices)
        {
            var (ex, ey, _, _) = _equipRows[_equipSlot];
            int eh = 12 + equipChoices.Count * JobRowH;
            host.UiFill(ex, ey + EquipRowH + 4, EquipBoxW, eh, 0xFF201838);
            host.UiFrame(ex, ey + EquipRowH + 4, EquipBoxW, eh);
            for (int i = 0; i < equipChoices.Count; i++)
            {
                var (rx, ry, rw, rh) = EquipChoiceRow(i);
                if (host._mouse.X >= rx && host._mouse.X < rx + rw && host._mouse.Y >= ry && host._mouse.Y < ry + rh)
                    host.UiFill(rx, ry, rw, rh, 0x60A090E0);
                var item = Part1Item.ById(_items, equipChoices[i]);
                if (item != null) DrawItemIcon(item, rx + 2, ry - 1, 22);
                host.UiText(item?.Name ?? "벗기", rx + 28, ry + 3, GameWindow.White, 12);
            }
        }

        // 직업 목록 — 직업 줄 아래에 틀 창, 줄마다 구슬 · 이름(원본 꼴). 짚은 줄은 밝게.
        if (_jobChoices is { } choices)
        {
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
    }
}
