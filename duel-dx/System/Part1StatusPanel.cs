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

    internal const int W = 170, H = 300, ListW = 150;

    private IReadOnlyList<Part1Job>? _jobs;
    private IReadOnlyList<Part1Ability>? _abilities;
    private IReadOnlyList<Part1Item>? _items;

    /// <summary>지난 틀에 그린 패널 · 장비 줄 자리(누르기 판정).</summary>
    private (int X, int Y, int W, int H) _panel;
    private readonly (int X, int Y, int W, int H)[] _equipRows = new (int, int, int, int)[5];

    /// <summary>패널을 눌렀다 — 장비 줄이면 바꾸고 true, 패널 안이면 true, 밖이면 false(닫기).</summary>
    internal bool OnClick(UnitState u, int bx, int by)
    {
        static bool In((int X, int Y, int W, int H) r, int px, int py) => px >= r.X && px < r.X + r.W && py >= r.Y && py < r.Y + r.H;
        for (int s = 0; s < 5; s++)
            if (In(_equipRows[s], bx, by)) { CycleEquipment(u, s); return true; }
        return In(_panel, bx, by);
    }

    /// <summary>그 칸에 가방의 다음 맞는 것을 끼운다(끼던 것은 가방으로) — 일행 창(Part1PartyScreen)과 같은 규칙. 일행이 아니면(용병 · 적) 못 바꾼다.</summary>
    private void CycleEquipment(UnitState u, int slot)
    {
        if (!host.Part1.IsOurs(u.ChrCode) || Part1Chr.Load(u.ChrCode - Part1Game.ChrBase) is not { } raw) { host.Toast("일행만 장비를 바꿀 수 있습니다"); return; }
        var chr = host.Part1.Apply(raw);
        _items ??= Part1Item.LoadAll();
        var options = host.Part1.Bag.Distinct().Where(id => Part1Item.ById(_items, id) is { } item && item.CanEquip(chr, slot, chr.Equipment, _items)).ToList();
        var slots = (int[])chr.Equipment.Clone();
        int old = slots[slot];
        int at = options.IndexOf(old);
        int next = options.Count == 0 ? 0 : old == 0 ? options[0] : at + 1 < options.Count ? options[at + 1] : 0;
        if (next == old) { host.Toast("가방에 이 칸에 맞는 것이 없다"); return; }
        if (next != 0) host.Part1.Bag.Remove(next);
        if (old != 0) host.Part1.Bag.Add(old);
        slots[slot] = next;
        host.Part1.Equipped[u.ChrCode] = slots;
        host._db?.ForgetPart1Characters();
        host.Btl.Part1.Forget(u.ChrCode);
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

        for (int s = 0; s < 5; s++)
        {
            string name = Part1Item.ById(_items, chr.Equipment[s])?.Name ?? "—";
            _equipRows[s] = (x + 6, row - 2, W - 12, 17);
            bool hot = host._mouse.X >= x + 6 && host._mouse.X < x + W - 6 && host._mouse.Y >= row - 2 && host._mouse.Y < row + 15;
            if (hot) host.UiFill(x + 6, row - 2, W - 12, 17, 0xC0304870);
            host.UiText($"{Part1PartyScreen.SlotNames[s]}: {name}", x + 10, row, s == 0 ? GameWindow.White : 0xFFD0D8E8, 11);
            row += 17;
        }

        // 어빌리티 — 오른쪽 목록(원본도 보기만 한다), 이름 · LV.
        int lx = x + W + 4, ly = y;
        var learned = chr.Abilities.Where(a => a.Ability > 0 && a.Level > 0).ToList();
        int lh = 10 + Math.Max(1, learned.Count) * 16;
        host.UiFill(lx, ly, ListW, lh, 0xF0101828);
        host.UiStroke(lx, ly, ListW, lh, 0xFF8098C8);
        if (learned.Count == 0) host.UiText("없음", lx + 8, ly + 6, 0xFF808890, 11);
        for (int i = 0; i < learned.Count; i++)
        {
            string name = Part1Ability.ById(_abilities, learned[i].Ability)?.Name ?? $"#{learned[i].Ability}";
            host.UiText(name, lx + 8, ly + 6 + i * 16, GameWindow.White, 11);
            host.UiText($"LV{learned[i].Level}", lx + ListW - 34, ly + 6 + i * 16, 0xFFFFE080, 11);
        }
    }
}
