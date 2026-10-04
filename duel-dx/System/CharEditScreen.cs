using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 도구 &gt; 캐릭터 에디터 — 인물마다 레벨(누적 경험치)과 장비 칸의 아이템을 게임 안에서 바꾼다(사용자 요청, 원본에 없는 시험용 도구).
/// </summary>
/// <remarks>
/// 대상은 명부(<c>_party</c> — 전투를 넘어 이어지는 인물 기록)와 지금 판에 선 내 인물이다. 레벨은 <see cref="GameDatabase.SetLevel"/>(스크립트 805 와 같은 식)로
/// 능력치까지 다시 세고 누적 경험치를 레벨 × 100 으로 맞춘다. 장비는 칸마다 아이템 번호를 앞뒤로 넘긴다(0 = 비움). 바꾸면 명부와 판 위의 유닛에 바로 든다.
/// </remarks>
internal sealed unsafe class CharEditScreen(GameWindow host)
{
    internal const int MenuCharEdit = 1160;
    internal const int W = 600, H = 400, ListW = 170, RowH = 22;

    internal bool _open;
    internal int _chosen;           // 고른 인물(목록 순번)
    internal int _top;              // 목록 스크롤

    /// <summary>고칠 수 있는 인물 — 명부와 판 위의 내 인물(대장만), Chr 번호 차례.</summary>
    internal List<int> People() =>
        [.. host._party.Keys.Concat(host._units.Where(u => u.Side == 4 && u.LeaderIndex < 0 && u.Data != null).Select(u => u.ChrCode)).Distinct().OrderBy(c => c)];

    internal CharacterData? DataOf(int chr) =>
        host._units.FirstOrDefault(u => u.ChrCode == chr && u.Side == 4 && u.LeaderIndex < 0)?.Data ?? host._party.GetValueOrDefault(chr);

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    /// <summary>바꾼 기록을 명부와 판 위의 그 인물에 넣고 최대치를 다시 센다.</summary>
    internal void Apply(int chr, CharacterData changed)
    {
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
        ids.AddRange(db.Items.Keys.OrderBy(i => i));
        int at = Math.Max(0, ids.IndexOf(c.Items[slot]));
        int next = ids[((at + delta) % ids.Count + ids.Count) % ids.Count];
        Apply(chr, c with { Items = [.. c.Items.Select((it, k) => k == slot ? (ushort)next : it)] });
    }

    /// <summary>열려 있으면 클릭을 먹는다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        var (x, y) = Origin();
        var people = People();
        if (bx >= x + W - 116 && bx < x + W - 16 && by >= y + H - 40 && by < y + H - 12) { _open = false; return true; }
        // 목록
        if (bx >= x + 12 && bx < x + 12 + ListW && by >= y + 40 && by < y + H - 50)
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

    internal void OnWheel(int delta) => _top = Math.Clamp(_top - delta, 0, Math.Max(0, People().Count - (H - 90) / RowH));

    internal void OnKey(int key)
    {
        if (key == Win32.VK_ESCAPE) _open = false;
    }

    internal void Draw()
    {
        if (!_open || host._db is not { } db) return;
        var (x, y) = Origin();
        host.FillRect(x - 4, y - 4, W + 8, H + 8, 0x80000000);
        host.FillRect(x, y, W, H, StatusScreen.PanelBg);
        host.StrokeRect(x, y, W, H, StatusScreen.BoxLine);
        host.FillRect(x, y, W, 28, StatusScreen.HeadBg);
        host.DrawText("캐릭터 에디터 — 레벨·장비 바꾸기(시험용)", x + 10, y + 6, White);

        var people = People();
        if (people.Count == 0) host.DrawText("고칠 인물이 없습니다 — 전투나 챕터에 들어간 뒤에 여세요.", x + 16, y + 50, DimGray, 12);
        _chosen = Math.Clamp(_chosen, 0, Math.Max(0, people.Count - 1));
        int rows = (H - 90) / RowH;
        for (int i = 0; i < rows && _top + i < people.Count; i++)
        {
            int chr = people[_top + i], ry = y + 40 + i * RowH;
            bool on = _top + i == _chosen;
            if (on) host.FillRect(x + 12, ry, ListW, RowH - 2, 0xFF2A4A8A);
            var d = DataOf(chr);
            host.DrawText($"{(d != null ? db.T(d.NameId) : chr.ToString())}  Lv{d?.Level}", x + 18, ry + 2, on ? 0xFF00FFFF : White, 12);
        }
        host.FillRect(x + 12 + ListW + 8, y + 40, 1, H - 90, StatusScreen.BoxLine);

        if (people.Count > 0 && DataOf(people[_chosen]) is { } c)
        {
            int px = x + ListW + 30;
            host.DrawText($"{db.T(c.NameId)}  (Chr {c.Code})", px, y + 44, White);
            host.DrawText($"레벨 {c.Level}  ·  누적 EXP {c.CumExp}", px, y + 78, White);
            void Button(int bx, int by, int w, string text)
            {
                bool here = host._mouse.X >= bx && host._mouse.X < bx + w && host._mouse.Y >= by && host._mouse.Y < by + 22;
                host.FillRect(bx, by, w, 22, here ? 0xFF2A4A8A : StatusScreen.BoxBg);
                host.StrokeRect(bx, by, w, 22, StatusScreen.BoxLine);
                var (_, tw, _) = host.GetText(text, White, 12);
                host.DrawText(text, bx + (w - tw) / 2, by + 3, White, 12);
            }
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
            host.DrawText("바꾸면 바로 든다 — 레벨은 능력치까지 다시 센다.", px, y + H - 66, DimGray, 12);
        }

        int fy = y + H - 40;
        host.FillRect(x + W - 116, fy, 100, 28, StatusScreen.HeadBg);
        host.DrawText("닫기", x + W - 80, fy + 6, White);
        host.DrawText("Esc: 닫기 · 휠: 목록", x + 16, fy + 7, DimGray);
    }
}
