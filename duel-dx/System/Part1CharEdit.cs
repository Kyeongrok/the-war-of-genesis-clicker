using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 캐릭터 에디터의 파트1 쪽 — 파트1 을 하는 동안(파트1 전투 · 타이틀 · 챕터 지도 · 마을) 에디터가 파트1 자료를 고친다(사용자 요청: 「파트1용도 있어야 하지 않나
/// 소지금도 파트1은 eld이고 챕터별로 가방이 달라」).
/// </summary>
/// <remarks>
/// 소지금은 지금 챕터 무리의 eld(<see cref="Part1Progress.Eld"/>), 인물은 지금 무리 사람(일행 창과 같은 <see cref="Part1Progress.GroupMembers"/>)에 판 위의 편 4 파트1 인물을 더한다.
/// 레벨은 누적 경험치 ÷ 100(<see cref="Part1Progress.Growth.Experience"/>), 장비는 다섯 칸(<see cref="Part1Progress.Equipped"/>)을 그 칸 갈래에 맞는 아이템으로 넘긴다
/// (시험용이라 직업이 다루는지는 따지지 않고 표시만 한다 — 가방에서 꺼내지도 않는다), 어빌리티는 배운 레벨(<see cref="Part1Progress.Growth.Abilities"/>, 0 = 지우기)을
/// 직업 최대를 넘어서도 바꾼다, 가방은 지금 무리의 것(<see cref="Part1Progress.Bag"/>)에 넣고 뺀다. 외형 · 나야트레이는 파트2 것이라 안 보인다.
/// 바꾸면 판 위의 그 인물에게도 바로 든다(어빌리티 목록 · 레벨 · 최대 HP · 능력치).
/// </remarks>
internal sealed class Part1CharEdit(GameWindow host, CharEditScreen ed)
{
    internal static readonly string[] Tabs = ["장비", "어빌리티", "가방"];
    internal const int LevelMax = 99, AbilityMax = 99;

    private (int Chr, List<int> Ids)? _abRows;
    private (int Group, List<int> Ids)? _bagRows;

    internal void Forget() => (_abRows, _bagRows) = (null, null);

    /// <summary>파트1 을 하는 중인가 — 파트1 저장 칸이 열렸거나(파트1 타이틀 · 챕터 · 마을) 파트1 판이 서 있다.</summary>
    internal bool Active => SlotsScreen.Part1Slots || (host._battleLoaded && host._scene.IsPart1);

    /// <summary>고칠 인물 — 지금 무리 사람, 그 뒤에 판에 편 4 로 선 파트1 인물(무리 밖 동료).</summary>
    internal List<int> People()
    {
        var people = host.Part1.GroupMembers().Where(c => Part1Chr.Load(c - Part1Game.ChrBase) != null).ToList();
        if (host._battleLoaded && host._scene.IsPart1 && !host.FieldOpen)
            foreach (var u in host._units)
                if (u.Side == 4 && u.LeaderIndex < 0 && u.ChrCode >= Part1Game.ChrBase && !people.Contains(u.ChrCode) && Part1Chr.Load(u.ChrCode - Part1Game.ChrBase) != null)
                    people.Add(u.ChrCode);
        return people;
    }

    /// <summary>자란 것 · 바꾼 장비를 입힌 그 인물의 레코드.</summary>
    internal Part1Chr? Chr(int code) => Part1Chr.Load(code - Part1Game.ChrBase) is { } raw ? host.Part1.Apply(raw) : null;

    private Part1Progress.Growth GrowthOf(int code) =>
        host.Part1.GrowthOf(code, Part1Chr.Load(code - Part1Game.ChrBase)?.Experience ?? 0);

    internal string Label(int code) => Chr(code) is { } c ? $"{c.Name}  Lv{c.Level}  ({code})" : $"Chr {code}";

    /// <summary>오른쪽 위에 보일 지금 챕터 — 「챕터 4 · 사략해적」(사용자 요청 menu-98: 무리 번호 · 인물 이름 말고 챕터명). 챕터 밖이면 「챕터 없음」.</summary>
    internal string ChapterName()
    {
        int id = host.Part1.ChapterId;
        return id < 0 ? "챕터 없음" : Part1Chapter.Load(id)?.Title is { Length: > 0 } title ? $"챕터 {id} · {title}" : $"챕터 {id}";
    }

    /// <summary>바꾼 것을 판 위의 그 인물에게도 — 인물 자료 · 싸움 자료를 다시 읽게 하고 어빌리티 목록 · 레벨 · 능력치 · 최대 HP 를 새로.</summary>
    internal void Changed(int code)
    {
        host.Stats._charEdited = true;
        host._db?.ForgetPart1Characters();
        host.Btl.Part1.Forget(code);
        host.Btl.RefreshPart1Abilities(code);
        if (!host._battleLoaded || !host._scene.IsPart1 || host._db?.Character(code) is not { } fresh) return;
        foreach (var u in host._units.Where(u => u.ChrCode == code && u.Data != null))
        {
            u.Data = u.Data! with { Level = fresh.Level, Lp = fresh.Lp, Psy = fresh.Psy, Dep = fresh.Dep, Dex = fresh.Dex, BasicWorkId = fresh.BasicWorkId };
            if (host.Btl.Part1.Fighter(u) is { } f) u.MaxHp = Math.Max(1, f.Chr.MaxHp);
            u.Hp = Math.Min(Math.Max(1, u.Hp), u.MaxHp);
        }
    }

    internal void ChangeMoney(int delta) => host.Part1.Eld = Math.Clamp(host.Part1.Eld + delta, 0, CharEditScreen.MoneyMax);

    internal void ChangeLevel(int code, int delta)
    {
        if (Chr(code) is not { } c) return;
        GrowthOf(code).Experience = Math.Clamp(c.Level + delta, 1, LevelMax) * 100;
        Changed(code);
    }

    /// <summary>그 칸이 받는 아이템 갈래인가 — <see cref="Part1Item.CanEquip"/> 의 칸 규칙만(어빌리티 · 다른 칸은 안 본다). 약 · 소모품 · 열쇠(19~21 · 24 · 25)는 못 낀다.</summary>
    internal static bool FitsSlot(int slot, int type) => slot switch
    {
        0 => type is >= 1 and <= 12 or 22,
        1 => type is >= 1 and <= 4 or 13 or 16 or 18,
        2 => type is 14 or 15 or 17 or 23 or 26,
        _ => type is 15 or 17 or 23,
    };

    internal void ChangeItem(int code, int slot, int delta)
    {
        if (Chr(code) is not { } c) return;
        var items = Part1Tables.Items;
        List<int> ids = [0, .. items.Where(i => i.Name.Length > 0 && FitsSlot(slot, i.Type)).Select(i => i.Id)];
        int at = Math.Max(0, ids.IndexOf(c.Equipment[slot]));
        var slots = (int[])c.Equipment.Clone();
        slots[slot] = ids[((at + delta) % ids.Count + ids.Count) % ids.Count];
        host.Part1.Equipped[code] = slots;
        Changed(code);
    }

    /// <summary>그 칸에 그 아이템을 낀다(고르기 창) — 0 은 비우기.</summary>
    internal void SetItem(int code, int slot, int id)
    {
        if (Chr(code) is not { } c) return;
        var slots = (int[])c.Equipment.Clone();
        slots[slot] = id;
        host.Part1.Equipped[code] = slots;
        Changed(code);
    }

    /// <summary>고르기 창이 고르는 칸에 지금 낀 아이템 — 창이 닫혀 있으면 −1.</summary>
    internal int CurrentItem(int code) => ed.ItemPick.Slot is int slot && Chr(code) is { } c ? c.Equipment[slot] : -1;

    /// <summary>그 직업이 그 어빌리티를 올릴 수 있는 레벨 — 직업 표에 없으면 0.</summary>
    internal static int JobMax(Part1Chr c, int ability) =>
        Part1Job.ById(Part1Tables.Jobs, c.JobId)?.Abilities.Where(a => a.Ability == ability).Select(a => a.Level).DefaultIfEmpty(0).Max() ?? 0;

    /// <summary>어빌리티 줄 — 이름이 있는 모든 어빌리티, 배운 것 · 직업이 다루는 것 · 나머지 차례. 인물을 고를 때 한 번 정한다.</summary>
    internal List<int> AbilityRows(Part1Chr c)
    {
        int code = Part1Game.ChrBase + c.Code;
        if (_abRows is { } rows && rows.Chr == code) return rows.Ids;
        List<int> ids = [.. Part1Tables.Abilities.Where(a => a.Name.Trim().Length > 0)
            .OrderBy(a => c.AbilityLevel(a.Id) >= 0 ? 0 : JobMax(c, a.Id) > 0 ? 1 : 2).ThenBy(a => a.Id).Select(a => a.Id)];
        (_abRows, ed._abTop) = ((code, ids), 0);
        return ids;
    }

    internal List<int> AbilityView(Part1Chr c) =>
        ed.Query is { Length: > 0 } q ? [.. AbilityRows(c).Where(id => CharEditScreen.Matches(q, [Part1Ability.ById(Part1Tables.Abilities, id)?.Name ?? ""], [id]))] : AbilityRows(c);

    /// <summary>배운 레벨을 올리고 내린다 — 안 배운 것 ＋ 는 Lv1, Lv1 에서 − 는 지운다(0). 직업 최대는 넘어도 된다(시험용).</summary>
    internal void ChangeAbility(int code, int ability, int delta)
    {
        if (Chr(code) is not { } c) return;
        int now = c.AbilityLevel(ability);
        if (now < 0 && delta < 0) return;
        int next = now < 0 ? 1 : now + delta;
        if (next > AbilityMax) { host.Toast("최대 레벨입니다"); return; }
        GrowthOf(code).Abilities[ability] = Math.Max(0, next);
        Changed(code);
    }

    /// <summary>가방 줄 — 이름이 있는 모든 아이템, 지금 무리 가방에 든 것을 앞에. 무리가 바뀔 때 다시 정한다.</summary>
    internal List<int> BagRows()
    {
        int g = host.Part1.CurrentGroup;
        if (_bagRows is { } rows && rows.Group == g) return rows.Ids;
        var bag = host.Part1.Bag;
        List<int> ids = [.. Part1Tables.Items.Where(i => i.Name.Trim().Length > 0).OrderBy(i => bag.Contains(i.Id) ? 0 : 1).ThenBy(i => i.Id).Select(i => i.Id)];
        (_bagRows, ed._lookTop) = ((g, ids), 0);
        return ids;
    }

    internal List<int> BagView() =>
        ed.Query is { Length: > 0 } q ? [.. BagRows().Where(id => CharEditScreen.Matches(q, [Part1Item.ById(Part1Tables.Items, id)?.Name ?? ""], [id]))] : BagRows();

    internal void ChangeBag(int item, int delta)
    {
        if (delta > 0) host.Part1.Bag.Add(item);
        else if (!host.Part1.Bag.Remove(item)) return;
        host.Stats._charEdited = true;
    }

    internal static readonly (int Dx, int Delta)[] LevelSteps = [(170, -10), (214, -1), (258, 1), (302, 10)];
    internal static readonly (int Dx, int Delta)[] ItemTurns = [(-136, -10), (-102, -1), (-68, 1), (-34, 10)];

    /// <summary>파티 줄 아래(인물 상자 · 갈래 · 레벨 · 칸들)의 클릭 — <paramref name="y"/> 는 창 위 끝.</summary>
    internal bool OnClick(int bx, int by, int x, int y, List<int> people)
    {
        foreach (var (dx, delta, _) in CharEditScreen.MoneySteps)
            if (ed.In(bx, by, x + dx, y + 36, 54, 22)) { ChangeMoney(delta); return true; }
        y += CharEditScreen.PartyH;
        int px = x + CharEditScreen.ContentDx, w = CharEditScreen.ContentW;
        if (ed.In(bx, by, px, y + 42, CharEditScreen.PickW, 22)) { ed._pickerOpen = true; return true; }
        if (ed._chosen >= people.Count) return true;
        int code = people[ed._chosen];
        for (int t = 0; t < Tabs.Length; t++)
            if (ed.In(bx, by, CharEditScreen.TabX(px, t), y + 42, CharEditScreen.TabW, 22))
            {
                if (t != ed._tab) (ed._search, ed._imeComp) = ("", "");
                ed._tab = t;
                return true;
            }
        foreach (var (dx, delta) in LevelSteps)
            if (ed.In(bx, by, px + dx, y + 76, 40, 22)) { ChangeLevel(code, delta); return true; }
        if (Chr(code) is not { } c) return true;
        if (ed._tab == 0)
        {
            for (int slot = 0; slot < 5; slot++)
                if (ed.In(bx, by, px, y + 140 + slot * 30, w - 140, 22))
                {
                    ed.ItemPick.Show(code, slot, Part1PartyScreen.SlotName(slot, Part1Item.ById(Part1Tables.Items, c.Equipment[slot])));
                    return true;
                }
            for (int slot = 0; slot < 5; slot++)
                foreach (var (dx, delta) in ItemTurns)
                    if (ed.In(bx, by, px + w + dx, y + 140 + slot * 30, 30, 22)) { ChangeItem(code, slot, delta); return true; }
            return true;
        }
        var ids = ed._tab == 1 ? AbilityView(c) : BagView();
        int top = ed._tab == 1 ? ed._abTop : ed._lookTop;
        for (int i = 0; i < CharEditScreen.AbRows && top + i < ids.Count; i++)
        {
            int ry = y + CharEditScreen.RowsDy + i * CharEditScreen.AbRowH;
            int delta = ed.In(bx, by, px + w - 64, ry, 30, 20) ? -1 : ed.In(bx, by, px + w - 30, ry, 30, 20) ? 1 : 0;
            if (delta == 0) continue;
            if (ed._tab == 1) ChangeAbility(code, ids[top + i], delta);
            else ChangeBag(ids[top + i], delta);
            return true;
        }
        return true;
    }

    /// <summary>오른쪽 단추 — 장비 칸 · 가방 줄 위면 아이템 설명(일행 창과 같은 <see cref="Part1ItemTip"/>).</summary>
    internal (string Text, int X, int Y)? TipAt(int bx, int by, int x, int y, List<int> people)
    {
        if (ed._chosen >= people.Count || Chr(people[ed._chosen]) is not { } c) return null;
        int px = x + CharEditScreen.ContentDx;
        y += CharEditScreen.PartyH;
        int id = 0;
        if (ed._tab == 0)
        {
            for (int slot = 0; slot < 5; slot++)
                if (ed.In(bx, by, px, y + 140 + slot * 30, CharEditScreen.ContentW - 140, 22)) id = c.Equipment[slot];
        }
        else if (ed._tab == 2)
        {
            var ids = BagView();
            int row = (by - (y + CharEditScreen.RowsDy)) / CharEditScreen.AbRowH;
            if (by >= y + CharEditScreen.RowsDy && ed.In(bx, by, px, y + CharEditScreen.RowsDy, CharEditScreen.ContentW - 70, CharEditScreen.AbRows * CharEditScreen.AbRowH)
                && ed._lookTop + row < ids.Count) id = ids[ed._lookTop + row];
        }
        return Part1ItemTip.Text(Part1Item.ById(Part1Tables.Items, id)) is { } tip ? (tip, bx, by) : null;
    }

    /// <summary>파티 줄(eld · 무리)과 고른 인물의 레벨 · 갈래 칸 — 인물 상자는 에디터가 맨 끝에 그린다.</summary>
    internal void Draw(int x, int y, List<int> people)
    {
        host.UiText($"소지금 {host.Part1.Eld} eld", x + 16, y + 39, 0xFFFFE080);
        foreach (var (dx, _, text) in CharEditScreen.MoneySteps) ed.Button(x + dx, y + 36, 54, text);
        string chapter = ChapterName();
        host.UiText(chapter, x + CharEditScreen.W - 16 - host.UiGetText(chapter, DimGray, 12).W, y + 40, DimGray, 12);
        host.UiFill(x + 12, y + 36 + 28, CharEditScreen.W - 24, 1, StatusScreen.BoxLine);
        y += CharEditScreen.PartyH;
        int px = x + CharEditScreen.ContentDx, w = CharEditScreen.ContentW;
        if (people.Count == 0 || Chr(people[ed._chosen]) is not { } c) return;
        host.UiText($"레벨 {c.Level}  ·  EXP {c.Experience}", px, y + 78, White);
        ed.Button(px + 170, y + 76, 40, "−10"); ed.Button(px + 214, y + 76, 40, "−1"); ed.Button(px + 258, y + 76, 40, "+1"); ed.Button(px + 302, y + 76, 40, "+10");
        string job = Part1Job.ById(Part1Tables.Jobs, c.JobId)?.Name is { Length: > 0 } jn ? $"직업 {jn}" : $"직업 {c.JobId}";
        host.UiText(job, px + w - host.UiGetText(job, DimGray, 12).W, y + 80, DimGray, 12);
        for (int t = 0; t < Tabs.Length; t++) ed.Button(CharEditScreen.TabX(px, t), y + 42, CharEditScreen.TabW, Tabs[t], on: t == ed._tab);
        if (ed._tab == 0) DrawEquipment(c, px, y);
        else if (ed._tab == 1) DrawAbilities(c, px, y);
        else DrawBag(px, y);
        string hint = ed._tab switch
        {
            0 => "바꾸면 바로 든다 — 시험용이라 직업이 못 다루는 것도 낀다 · 오른쪽 단추: 설명",
            1 => "+ 넣기·올리기 · − 내리기·지우기 — 직업 최대를 넘으면(주황) 전투는 직업 최대로 쓴다.",
            _ => "지금 챕터 무리의 가방 — 챕터마다 무리 가방이 따로다 · 오른쪽 단추: 설명",
        };
        host.UiText(hint, px, y + CharEditScreen.H - CharEditScreen.PartyH - 66, DimGray, 12);
    }

    private void DrawEquipment(Part1Chr c, int px, int y)
    {
        var items = Part1Tables.Items;
        int w = CharEditScreen.ContentW;
        host.UiText("장비 다섯 칸 (이름을 누르면 고르기 창 — ◀◀ ▶▶ 는 10개씩 넘긴다)", px, y + 116, DimGray, 12);
        for (int slot = 0; slot < 5; slot++)
        {
            int iy = y + 140 + slot * 30, id = c.Equipment[slot];
            var item = Part1Item.ById(items, id);
            host.UiText(Part1PartyScreen.SlotName(slot, item), px, iy + 3, DimGray, 12);
            host.UiText(item == null ? "(비움)" : $"{item.Name}  #{id}", px + 56, iy + 3, White, 12);
            if (item != null && !item.CanEquip(c, slot, c.Equipment, items)) host.UiText("직업이 못 다룸", px + 300, iy + 3, 0xFFE08060, 12);
            int bx = px + w;
            ed.Button(bx - 136, iy, 30, "◀◀"); ed.Button(bx - 102, iy, 30, "◀"); ed.Button(bx - 68, iy, 30, "▶"); ed.Button(bx - 34, iy, 30, "▶▶");
        }
        // 능력치 — 장비 · 어빌리티를 바꾼 결과를 바로 본다(일행 창과 같은 값).
        var fighter = Part1Fighter.From(c, items);
        int[] stats = [Part1Chr.Str, Part1Chr.Skill, Part1Chr.Dex, Part1Chr.Int, Part1Chr.Luck, Part1Chr.Spd, Part1Chr.Ac, Part1Chr.Mr];
        string[] labels = ["STR", "SKILL", "DEX", "INT", "LUCK", "SPD", "AC", "MR"];
        host.UiText($"HP {c.MaxHp}", px, y + 294, 0xFF00FFFF, 12);
        for (int k = 0; k < stats.Length; k++)
            host.UiText($"{labels[k]} {fighter.Stat(stats[k])}", px + 80 + k * 62, y + 294, White, 12);
    }

    private void DrawAbilities(Part1Chr c, int px, int y)
    {
        var ids = AbilityView(c);
        int w = CharEditScreen.ContentW, rows = CharEditScreen.AbRows;
        ed._abTop = Math.Clamp(ed._abTop, 0, Math.Max(0, ids.Count - rows));
        ed.DrawSearch(px, y, ids.Count == 0 ? "없음" : $"{ed._abTop + 1}–{Math.Min(ids.Count, ed._abTop + rows)} / {ids.Count}");
        for (int i = 0; i < rows && ed._abTop + i < ids.Count; i++)
        {
            var ab = Part1Ability.ById(Part1Tables.Abilities, ids[ed._abTop + i])!;
            int ry = y + CharEditScreen.RowsDy + i * CharEditScreen.AbRowH, level = c.AbilityLevel(ab.Id), max = JobMax(c, ab.Id);
            string no = $"#{ab.Id}";
            host.UiText(no, px + CharEditScreen.LookNameDx - 6 - host.UiGetText(no, DimGray, 12).W, ry + 2, DimGray, 12);
            host.UiText(ab.Name, px + CharEditScreen.LookNameDx, ry + 2, level >= 0 ? White : DimGray, 12);
            if (level >= 0) host.UiText($"Lv{level}", px + w - 180, ry + 2, level > max ? 0xFFE08060 : 0xFF00FFFF, 12);
            if (max > 0) host.UiText($"직업 최대 {max}", px + w - 140, ry + 2, DimGray, 12);
            ed.Button(px + w - 64, ry, 30, "−", h: 20); ed.Button(px + w - 30, ry, 30, "＋", h: 20);
        }
    }

    private void DrawBag(int px, int y)
    {
        var ids = BagView();
        var bag = host.Part1.Bag;
        int w = CharEditScreen.ContentW, rows = CharEditScreen.AbRows;
        ed._lookTop = Math.Clamp(ed._lookTop, 0, Math.Max(0, ids.Count - rows));
        ed.DrawSearch(px, y, $"가방 {bag.Count}개 · {(ids.Count == 0 ? "없음" : $"{ed._lookTop + 1}–{Math.Min(ids.Count, ed._lookTop + rows)} / {ids.Count}")}");
        for (int i = 0; i < rows && ed._lookTop + i < ids.Count; i++)
        {
            var item = Part1Item.ById(Part1Tables.Items, ids[ed._lookTop + i])!;
            int ry = y + CharEditScreen.RowsDy + i * CharEditScreen.AbRowH, count = bag.Count(b => b == item.Id);
            string no = $"#{item.Id}";
            host.UiText(no, px + CharEditScreen.LookNameDx - 6 - host.UiGetText(no, DimGray, 12).W, ry + 2, DimGray, 12);
            host.UiText(item.Name, px + CharEditScreen.LookNameDx, ry + 2, count > 0 ? White : DimGray, 12);
            if (count > 0) host.UiText($"×{count}", px + w - 200, ry + 2, 0xFF00FFFF, 12);
            string price = $"{item.Price} eld";
            host.UiText(price, px + w - 72 - host.UiGetText(price, DimGray, 12).W, ry + 2, DimGray, 12);
            ed.Button(px + w - 64, ry, 30, "−", h: 20); ed.Button(px + w - 30, ry, 30, "＋", h: 20);
        }
    }
}
