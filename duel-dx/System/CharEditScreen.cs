using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 도구 &gt; 캐릭터 에디터 — 인물마다 레벨(누적 경험치)과 장비 칸의 아이템, 파티의 소지금을 게임 안에서 바꾼다(사용자 요청, 원본에 없는 시험용 도구).
/// </summary>
/// <remarks>
/// 대상은 지금 파티의 동료(전직 화면에 서는 인물)다 — 자료는 판에 서 있으면 그 유닛의 것, 아니면 명부(<c>_party</c>)의 것. 레벨은 <see cref="GameDatabase.SetLevel"/>(스크립트 805 와 같은 식)로
/// 능력치까지 다시 세고 누적 경험치를 레벨 × 100 으로 맞춘다. 장비는 칸마다 아이템 번호를 앞뒤로 넘긴다(0 = 비움). 바꾸면 명부와 판 위의 유닛에 바로 든다.
/// 고칠 인물이 없으면(타이틀처럼 아직 아무것도 안 불러온 때) 세이브 파일 목록부터 보인다 — 고르면 그 세이브를 불러온 뒤 고친다(사용자 요청 menu-8).
/// 맨 위 줄은 파티 것: 소지금 올리고 내리기와 「나야트레이 등장」(Chr 563 을 801 처럼 지금 파티에 넣는다).
/// </remarks>
internal sealed unsafe class CharEditScreen(GameWindow host)
{
    internal const int MenuCharEdit = 1160;
    internal const int W = 600, H = 440, ListW = 170, RowH = 22, PartyH = 36, SaveRowH = 24, MoneyMax = 99_999_999;
    internal const int NayatreiChr = 563;

    /// <summary>DUELDX_CHAREDIT 가 있으면 열어 둔 채 시작한다(화면 밖 시험용).</summary>
    internal bool _open = Environment.GetEnvironmentVariable("DUELDX_CHAREDIT") != null;
    internal int _chosen;           // 고른 인물(목록 순번)
    internal int _top;              // 목록 스크롤
    internal bool _pickSave;        // 인물이 있어도 세이브 목록을 보인다(「세이브 고르기」)
    internal int _saveTop;          // 세이브 목록 스크롤
    private List<(int Slot, SystemMenu.SaveState Head)>? _saves;

    /// <summary>파일이 있는 세이브 칸들 — 파일을 통째로 읽는 일이라 창을 열 때 한 번만 훑는다.</summary>
    internal List<(int Slot, SystemMenu.SaveState Head)> Saves() =>
        _saves ??= [.. Enumerable.Range(0, SlotsScreen.SaveSlots + SlotsScreen.AutoSlots)
                       .Select(slot => (Slot: slot, Head: host.SlotsScr.SlotHead(slot))).Where(s => s.Head != null).Select(s => (s.Slot, s.Head!))];

    /// <summary>고칠 게임이 아직 없나 — 타이틀·기록 고르기 화면 뒤에는 시연용 기본 판이 서 있어서 인물 수만으로는 모른다.</summary>
    internal bool NoGame(List<int> people) => people.Count == 0 || host.TitleScr._titleOpen || host.RecordsScr._recordsOpen;

    internal void Toggle()
    {
        _open = !_open;
        _pickSave = false;
        _saves = null;
    }

    /// <summary>고른 세이브를 불러온다 — 그 뒤로는 불러온 게임의 인물을 고친다.</summary>
    internal void LoadSave(int slot)
    {
        if (!host.Sys.LoadBattleFrom(SlotsScreen.SlotPath(slot))) return;
        (_pickSave, _chosen, _top) = (false, 0, 0);
    }

    internal void ChangeMoney(int delta) => host.Mos._shopMoney = Math.Clamp(host.Mos._shopMoney + delta, 0, MoneyMax);

    /// <summary>나야트레이(Chr 563)를 지금 파티에 넣는다 — 스크립트 801 과 같은 길이라 판에는 다음 전투부터 선다.</summary>
    internal void AddNayatrei()
    {
        if (host.Mos._members.Contains(NayatreiChr)) { host.Toast("나야트레이는 이미 파티에 있습니다"); return; }
        host.PartySt.AddMember(host.EpisodesScr._partyNo, NayatreiChr);
        if (!host._party.ContainsKey(NayatreiChr)) { host.Toast("나야트레이 자료(Chr 0563)를 못 읽었습니다"); return; }
        _chosen = Math.Max(0, People().IndexOf(NayatreiChr));
        host.Toast("나야트레이가 파티에 들어왔습니다 — 전투에는 다음 전투부터 나옵니다");
    }

    internal static readonly (int Dx, int Delta, string Text)[] MoneySteps = [(170, -10000, "−1만"), (228, 1000, "+1천"), (286, 10000, "+1만"), (344, 100000, "+10만")];

    /// <summary>
    /// 고칠 수 있는 인물 — 전직 화면에 서는 인물과 같다(<see cref="MosesScene.StyleParty"/>: 스크립트 801 로 들어온 동료, 그 차례대로).
    /// 전에는 명부 전체에 판 위의 내 편을 더해서 동맹 NPC(제이슨)나 파티를 떠난 인물까지 나왔다(사용자 요청 menu-10).
    /// </summary>
    internal List<int> People() => host.Mos.StyleParty();

    internal CharacterData? DataOf(int chr) =>
        host._units.FirstOrDefault(u => u.ChrCode == chr && u.Side == 4 && u.LeaderIndex < 0)?.Data ?? host._party.GetValueOrDefault(chr);

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    /// <summary>바꾼 기록을 명부와 판 위의 그 인물에 넣고 최대치를 다시 센다.</summary>
    internal void Apply(int chr, CharacterData changed)
    {
        host.Stats._charEdited = true;
        if (host._party.ContainsKey(chr) || host.Mos._members.Contains(chr)) host._party[chr] = changed;
        foreach (var u in host._units.Where(u => u.ChrCode == chr && u.Side == 4 && u.LeaderIndex < 0))
        {
            u.Data = changed;
            host.StatusScr.RefreshUnitStats(u);
            u.Hp = Math.Min(Math.Max(1, u.Hp), u.MaxHp);
        }
    }

    internal void ChangeLevel(int chr, int delta)
    {
        if (host._db is not { } db || DataOf(chr) is not { } c) return;
        int level = Math.Clamp(c.Level + delta, 1, 99);
        Apply(chr, db.SetLevel(c, level) with { Level = (ushort)level, CumExp = level * 100 });
    }

    internal void ChangeItem(int chr, int slot, int delta)
    {
        if (host._db is not { } db || DataOf(chr) is not { } c || (uint)slot >= c.Items.Length) return;
        // 0(비움)과 있는 아이템 번호들을 한 줄로 놓고 앞뒤로 넘긴다.
        var ids = new List<int> { 0 };
        // 아이템 표에는 갈래 머리줄(「=== 요요 ===」)과 이름 없는 빈 줄이 섞여 있다 — 건너뛴다.
        ids.AddRange(db.Items.Where(kv => kv.Key != 0 && db.T(kv.Value.NameId) is { Length: > 0 } name && !name.StartsWith('=')).Select(kv => kv.Key).OrderBy(i => i));
        int at = Math.Max(0, ids.IndexOf(c.Items[slot]));
        int next = ids[((at + delta) % ids.Count + ids.Count) % ids.Count];
        Apply(chr, c with { Items = [.. c.Items.Select((it, k) => k == slot ? (ushort)next : it)] });
    }

    internal bool In(int bx, int by, int x, int y, int w, int h) => bx >= x && bx < x + w && by >= y && by < y + h;

    /// <summary>열려 있으면 클릭을 먹는다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        var (x, y) = Origin();
        var people = People();
        if (In(bx, by, x + W - 116, y + H - 40, 100, 28)) { _open = false; return true; }
        if (!NoGame(people) && In(bx, by, x + W - 246, y + H - 40, 120, 28)) { _pickSave = !_pickSave; _saves = null; return true; }
        if (_pickSave || NoGame(people))
        {
            var saves = Saves();
            int at = _saveTop + (by - y - 62) / SaveRowH;
            if (In(bx, by, x + 12, y + 62, W - 24, SaveRows * SaveRowH) && at < saves.Count) LoadSave(saves[at].Slot);
            return true;
        }
        // 파티 줄 — 소지금 · 나야트레이
        foreach (var (dx, delta, _) in MoneySteps)
            if (In(bx, by, x + dx, y + 36, 54, 22)) { ChangeMoney(delta); return true; }
        if (In(bx, by, x + W - 16 - 150, y + 36, 150, 22)) { AddNayatrei(); return true; }
        y += PartyH;
        // 목록
        if (bx >= x + 12 && bx < x + 12 + ListW && by >= y + 40 && by < y + 40 + ListRows * RowH)
        {
            int row = _top + (by - y - 40) / RowH;
            if (row < people.Count) _chosen = row;
            return true;
        }
        if (_chosen >= people.Count) return true;
        int chr = people[_chosen], px = x + ListW + 30;
        // 레벨 단추 — −10 · −1 · +1 · +10
        int ly = y + 76;
        (int Dx, int Delta)[] steps = [(170, -10), (214, -1), (258, 1), (302, 10)];
        foreach (var (dx, delta) in steps)
            if (bx >= px + dx && bx < px + dx + 40 && by >= ly && by < ly + 22) { ChangeLevel(chr, delta); return true; }
        // 장비 칸 — ◀◀ ◀ ▶ ▶▶ (10 · 1 씩)
        if (DataOf(chr) is { } c)
            for (int slot = 0; slot < c.Items.Length; slot++)
            {
                int iy = y + 140 + slot * 30;
                (int Dx, int Delta)[] turns = [(250, -10), (284, -1), (318, 1), (352, 10)];
                foreach (var (dx, delta) in turns)
                    if (bx >= px + dx && bx < px + dx + 30 && by >= iy && by < iy + 22) { ChangeItem(chr, slot, delta); return true; }
            }
        return true;
    }

    internal const int ListRows = (H - PartyH - 90) / RowH, SaveRows = (H - 62 - 50) / SaveRowH;

    internal void OnWheel(int delta)
    {
        var people = People();
        if (_pickSave || NoGame(people)) _saveTop = Math.Clamp(_saveTop - delta, 0, Math.Max(0, Saves().Count - SaveRows));
        else _top = Math.Clamp(_top - delta, 0, Math.Max(0, people.Count - ListRows));
    }

    internal void OnKey(int key)
    {
        if (key == Win32.VK_ESCAPE) _open = false;
    }

    internal void Button(int bx, int by, int w, string text)
    {
        bool here = In(host._mouse.X, host._mouse.Y, bx, by, w, 22);
        host.FillRect(bx, by, w, 22, here ? 0xFF2A4A8A : StatusScreen.BoxBg);
        host.StrokeRect(bx, by, w, 22, StatusScreen.BoxLine);
        var (_, tw, _) = host.GetText(text, White, 12);
        host.DrawText(text, bx + (w - tw) / 2, by + 3, White, 12);
    }

    /// <summary>세이브 고르기 쪽 — 파일이 있는 칸만 줄로 올린다. 누르면 불러온다.</summary>
    internal void DrawSaves(int x, int y, bool hasPeople)
    {
        var saves = Saves();
        host.DrawText(saves.Count == 0 ? "세이브 파일이 없습니다 — 새 게임을 시작해 전투나 챕터에 들어간 뒤에 여세요."
                      : hasPeople ? "불러올 세이브 파일을 고르세요 — 지금 게임의 저장하지 않은 것은 없어집니다."
                      : "먼저 고칠 세이브 파일을 고르세요 — 불러온 뒤 인물과 소지금을 고칩니다.", x + 16, y + 38, DimGray, 12);
        _saveTop = Math.Clamp(_saveTop, 0, Math.Max(0, saves.Count - SaveRows));
        for (int i = 0; i < SaveRows && _saveTop + i < saves.Count; i++)
        {
            var (slot, head) = saves[_saveTop + i];
            int ry = y + 62 + i * SaveRowH;
            if (In(host._mouse.X, host._mouse.Y, x + 12, ry, W - 24, SaveRowH)) host.FillRect(x + 12, ry, W - 24, SaveRowH - 2, 0xFF2A4A8A);
            string label = slot == SlotsScreen.AutoSlot ? "[자동 저장]" : slot > SlotsScreen.AutoSlot ? $"[자동 {slot - SlotsScreen.AutoSlot}차례 전]"
                           : $"[{slot:D2}:{head.SceneKind switch { 4 => "챕  터", 7 => "연대표", _ => "전  투" }}]";
            host.DrawText(label, x + 18, ry + 4, SlotsScreen.SlotLabelColor, 12);
            host.DrawText(host._db?.T((ushort)head.SceneText) ?? "", x + 130, ry + 4, White, 12);
            host.DrawText($"{head.Money}GP", x + 330, ry + 4, DimGray, 12);
            var (_, sw, _) = host.GetText(head.SavedAt, White, 12);
            host.DrawText(head.SavedAt, x + W - 18 - sw, ry + 4, SlotsScreen.SlotTimeColor, 12);
        }
    }

    internal void Draw()
    {
        if (!_open || host._db is not { } db) return;
        var (x, y) = Origin();
        host.FillRect(x - 4, y - 4, W + 8, H + 8, 0x80000000);
        host.FillRect(x, y, W, H, StatusScreen.PanelBg);
        host.StrokeRect(x, y, W, H, StatusScreen.BoxLine);
        host.FillRect(x, y, W, 28, StatusScreen.HeadBg);
        host.DrawText("캐릭터 에디터 — 레벨·장비·소지금 바꾸기(시험용)", x + 10, y + 6, White);

        var people = People();
        bool picking = _pickSave || NoGame(people);
        int fy = y + H - 40;
        host.FillRect(x + W - 116, fy, 100, 28, StatusScreen.HeadBg);
        host.DrawText("닫기", x + W - 80, fy + 6, White);
        if (!NoGame(people))
        {
            string text = picking ? "돌아가기" : "세이브 고르기";
            host.FillRect(x + W - 246, fy, 120, 28, StatusScreen.HeadBg);
            host.DrawText(text, x + W - 246 + (120 - host.GetText(text, White).W) / 2, fy + 6, White);
        }
        host.DrawText("Esc: 닫기 · 휠: 목록", x + 16, fy + 7, DimGray);
        if (picking) { DrawSaves(x, y, !NoGame(people)); return; }

        // 파티 줄 — 소지금 · 나야트레이
        host.DrawText($"소지금 {host.Mos._shopMoney}GP", x + 16, y + 39, White);
        foreach (var (dx, _, text) in MoneySteps) Button(x + dx, y + 36, 54, text);
        Button(x + W - 16 - 150, y + 36, 150, host.Mos._members.Contains(NayatreiChr) ? "나야트레이 (파티에 있음)" : "나야트레이 등장");
        host.FillRect(x + 12, y + 36 + 28, W - 24, 1, StatusScreen.BoxLine);
        y += PartyH;

        _chosen = Math.Clamp(_chosen, 0, Math.Max(0, people.Count - 1));
        for (int i = 0; i < ListRows && _top + i < people.Count; i++)
        {
            int chr = people[_top + i], ry = y + 40 + i * RowH;
            bool on = _top + i == _chosen;
            if (on) host.FillRect(x + 12, ry, ListW, RowH - 2, 0xFF2A4A8A);
            var d = DataOf(chr);
            host.DrawText($"{(d != null ? db.T(d.NameId) : chr.ToString())}  Lv{d?.Level}", x + 18, ry + 2, on ? 0xFF00FFFF : White, 12);
        }
        host.FillRect(x + 12 + ListW + 8, y + 40, 1, ListRows * RowH, StatusScreen.BoxLine);

        if (DataOf(people[_chosen]) is { } c)
        {
            int px = x + ListW + 30;
            host.DrawText($"{db.T(c.NameId)}  (Chr {c.Code})", px, y + 44, White);
            host.DrawText($"레벨 {c.Level}  ·  누적 EXP {c.CumExp}", px, y + 78, White);
            Button(px + 170, y + 76, 40, "−10"); Button(px + 214, y + 76, 40, "−1"); Button(px + 258, y + 76, 40, "+1"); Button(px + 302, y + 76, 40, "+10");
            host.DrawText("장비 (칸마다 아이템을 앞뒤로 넘긴다 — ◀◀ ▶▶ 는 10개씩)", px, y + 116, DimGray, 12);
            for (int slot = 0; slot < c.Items.Length; slot++)
            {
                int iy = y + 140 + slot * 30;
                int id = c.Items[slot];
                string name = id == 0 ? "(비움)" : db.Items.TryGetValue(id, out var item) ? db.T(item.NameId) : $"아이템 {id}";
                host.DrawText($"{slot + 1}", px, iy + 3, DimGray, 12);
                host.DrawText($"{name}{(id != 0 ? $"  #{id}" : "")}", px + 18, iy + 3, White, 12);
                Button(px + 250, iy, 30, "◀◀"); Button(px + 284, iy, 30, "◀"); Button(px + 318, iy, 30, "▶"); Button(px + 352, iy, 30, "▶▶");
            }
            host.DrawText("바꾸면 바로 든다 — 레벨은 능력치까지 다시 센다.", px, y + H - PartyH - 66, DimGray, 12);
        }
    }
}
