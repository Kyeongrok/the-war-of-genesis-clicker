using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 캐릭터 에디터 장비 칸의 아이템 고르기 창 — 칸의 아이템 이름을 누르면 뜬다(사용자 요청 menu-95: 「무기는 누르면 창이 떠서 카테고리별로 탭 있고 검색할 수 있게」).
/// 맨 위에 갈래 탭(「전체」 + 갈래들), 그 아래 찾기 칸(에디터의 찾기 칸과 같은 글자 · 한글 조합), 그 아래 줄 목록 — 줄을 누르면 그 칸에 끼고 닫힌다.
/// </summary>
/// <remarks>
/// 파트2 갈래는 아이템 종류(Itm 파일 8, <see cref="TypeName"/>)다 — 이름은 아이템 표 머리줄(「=== 요요 ===」 · 「====&lt;아머&gt;======」)의 말을 따랐다.
/// 파트1 갈래는 아이템 갈래(+0x44, <see cref="Part1ItemTip.TypeName"/>)이고, 그 칸에 낄 수 있는 갈래만 넣는다(<see cref="Part1CharEdit.FitsSlot"/>).
/// </remarks>
internal sealed class CharEditItemPick(GameWindow host, CharEditScreen ed)
{
    internal sealed record Row(int Id, string Name, string Category, string Info);

    private (int Chr, int Slot, string SlotName, List<Row> Rows, List<string> Categories)? _at;
    private int _category, _top;

    internal bool Open => _at != null;

    /// <summary>고르는 칸 — 닫혀 있으면 null.</summary>
    internal int? Slot => _at?.Slot;

    internal const int RowH = 22, TabH = 22;

    /// <summary>창 자리 — 에디터 창 안쪽, 머리줄 아래를 거의 다 덮는다.</summary>
    private static (int X, int Y, int W, int H) Box(int ex, int ey) => (ex + 12, ey + 34, CharEditScreen.W - 24, CharEditScreen.H - 84);

    /// <summary>그 인물 그 칸의 고르기 창을 연다.</summary>
    internal void Show(int chr, int slot, string slotName)
    {
        var rows = ed.Part1Mode ? Part1Rows(slot) : Part2Rows();
        List<string> categories = ["전체", .. rows.Where(r => r.Id != 0).Select(r => r.Category).Distinct()];
        _at = (chr, slot, slotName, rows, categories);
        (_category, _top) = (0, 0);
        (ed._search, ed._imeComp) = ("", "");
    }

    internal void Close() => _at = null;

    private List<Row> Part2Rows()
    {
        List<Row> rows = [new(0, "(비움)", "", "")];
        if (host._db is not { } db) return rows;
        // 갈래는 아이템 종류(Itm 파일 8)로 가른다 — 표의 머리줄 차례로 가르면 원본 표에 머리줄이 빠진 곳에서 섞인다
        // (씨가 32~37 다음 포이즌 포트 39~43 앞에 머리줄이 없어 리엔의 술병이 씨가 탭에 들어갔다, fg-92). 장비 칸이 보는 것도 종류다.
        foreach (var (id, item) in db.Items.OrderBy(kv => kv.Value.Type).ThenBy(kv => kv.Key))
        {
            if (id == 0 || item.Type == 255 || db.T(item.NameId) is not { Length: > 0 } name || name.StartsWith('=')) continue;
            string info = string.Join(" · ", new[] { item.Attack > 0 ? $"공격 {item.Attack}" : "", item.Defense > 0 ? $"방어 {item.Defense}" : "", item.Price > 0 ? $"{item.Price}GP" : "" }.Where(s => s.Length > 0));
            rows.Add(new(id, name, TypeName(item.Type), info));
        }
        return rows;
    }

    /// <summary>파트2 아이템 종류의 탭 이름 — 원본 아이템 표 머리줄(「=== 씨가 ===」 …)의 말을 따랐다.</summary>
    internal static string TypeName(byte type) => type switch
    {
        0 => "VES", 1 => "부스터", 2 => "아머", 3 => "신발", 4 => "벨트", 5 => "반지", 6 => "목걸이", 7 => "사용아이템",
        8 => "리본류", 9 => "요요", 10 => "사진기", 11 => "크로", 12 => "쌍권총", 13 => "씨가", 14 => "일반검류", 15 => "대검류",
        16 => "아수라검", 17 => "포이즌 포트", 19 => "적들용무기", _ => $"종류 {type}",
    };

    private static List<Row> Part1Rows(int slot)
    {
        List<Row> rows = [new(0, "(비움)", "", "")];
        foreach (var item in Part1Tables.Items.Where(i => i.Name.Trim().Length > 0 && Part1CharEdit.FitsSlot(slot, i.Type)).OrderBy(i => i.Type).ThenBy(i => i.Id))
        {
            string info = string.Join(" · ", new[] { item.Attack > 0 ? $"공격 {item.Attack}" : "", item.Defense > 0 ? $"방어 {item.Defense}" : "", item.Price > 0 ? $"{item.Price} eld" : "" }.Where(s => s.Length > 0));
            rows.Add(new(item.Id, item.Name, Part1ItemTip.TypeName(item.Type) is { Length: > 0 } t ? t : $"갈래 {item.Type}", info));
        }
        return rows;
    }

    /// <summary>지금 탭 · 찾는 말에 맞는 줄 — 「(비움)」은 찾는 말이 없을 때 「전체」 탭 맨 위에만.</summary>
    private List<Row> View()
    {
        if (_at is not { } at) return [];
        string q = ed.Query, cat = at.Categories[Math.Clamp(_category, 0, at.Categories.Count - 1)];
        return [.. at.Rows.Where(r => r.Id == 0 ? q.Length == 0 && _category == 0
                                     : (_category == 0 || r.Category == cat) && CharEditScreen.Matches(q, [r.Name, r.Category], [r.Id]))];
    }

    /// <summary>탭 단추 자리 — 글 너비만큼, 넘치면 다음 줄로.</summary>
    private List<(int X, int Y, int W)> TabCells(int x, int y, int w)
    {
        var cells = new List<(int, int, int)>();
        if (_at is not { } at) return cells;
        int cx = x, cy = y;
        foreach (string c in at.Categories)
        {
            int cw = host.UiGetText(c, White, 12).W + 16;
            if (cx + cw > x + w) (cx, cy) = (x, cy + TabH + 4);
            cells.Add((cx, cy, cw));
            cx += cw + 4;
        }
        return cells;
    }

    private (int Tabs, int Search, int List, int Rows) Layout(int x, int y, int w, int h)
    {
        var cells = TabCells(x + 10, y + 34, w - 20);
        int tabsEnd = cells.Count > 0 ? cells.Max(c => c.Y) + TabH : y + 34;
        int search = tabsEnd + 8, list = search + 28;
        return (y + 34, search, list, Math.Max(1, (y + h - 8 - list) / RowH));
    }

    /// <summary>창이 떠 있으면 클릭을 먹는다 — 줄을 누르면 끼우고 닫고, 창 밖을 누르면 닫기만.</summary>
    internal bool OnClick(int bx, int by, int ex, int ey)
    {
        if (_at is not { } at) return false;
        var (x, y, w, h) = Box(ex, ey);
        if (!ed.In(bx, by, x, y, w, h) || ed.In(bx, by, x + w - 70, y + 5, 60, 22)) { Close(); return true; }
        var cells = TabCells(x + 10, y + 34, w - 20);
        for (int i = 0; i < cells.Count; i++)
            if (ed.In(bx, by, cells[i].X, cells[i].Y, cells[i].W, TabH)) { (_category, _top) = (i, 0); return true; }
        var (_, _, list, rows) = Layout(x, y, w, h);
        var view = View();
        int row = (by - list) / RowH;
        if (by >= list && row < rows && _top + row < view.Count)
        {
            Pick(at.Chr, at.Slot, view[_top + row].Id);
            Close();
        }
        return true;
    }

    private void Pick(int chr, int slot, int id)
    {
        if (ed.Part1Mode) ed.P1.SetItem(chr, slot, id);
        else ed.SetItem(chr, slot, id);
    }

    internal void OnWheel(int delta) => _top -= delta * 3;

    /// <summary>Esc — 찾는 말이 있으면 비우고, 없으면 창을 닫는다.</summary>
    internal void OnEscape()
    {
        if (ed._search.Length > 0 || ed._imeComp.Length > 0) (ed._search, ed._imeComp, _top) = ("", "", 0);
        else Close();
    }

    internal void ResetTop() => _top = 0;

    /// <summary>에디터 창 위에 그린다 — <paramref name="current"/> 는 그 칸에 지금 낀 아이템.</summary>
    internal void Draw(int ex, int ey, int current)
    {
        if (_at is not { } at) return;
        var (x, y, w, h) = Box(ex, ey);
        host.UiFill(x - 3, y - 3, w + 6, h + 6, 0xA0000000);
        host.UiFill(x, y, w, h, 0xFF0A1226);                // 밑의 에디터 글이 비치지 않게 먼저 불투명하게
        host.UiFill(x, y, w, h, StatusScreen.PanelBg);
        host.UiStroke(x, y, w, h, StatusScreen.BoxLine);
        host.UiText($"{at.SlotName} 고르기 — 줄을 누르면 낀다", x + 10, y + 8, White, 12);
        ed.Button(x + w - 70, y + 5, 60, "닫기");
        var cells = TabCells(x + 10, y + 34, w - 20);
        for (int i = 0; i < cells.Count; i++) ed.Button(cells[i].X, cells[i].Y, cells[i].W, at.Categories[i], on: i == _category);
        var (_, search, list, rows) = Layout(x, y, w, h);
        var view = View();
        _top = Math.Clamp(_top, 0, Math.Max(0, view.Count - rows));
        // 찾기 칸 — 에디터의 것을 그대로(그 칸은 y + SearchDy 에 그린다).
        ed.DrawSearch(x + 10, search - CharEditScreen.SearchDy, view.Count == 0 ? "없음" : $"{_top + 1}–{Math.Min(view.Count, _top + rows)} / {view.Count}", w - 20);
        for (int i = 0; i < rows && _top + i < view.Count; i++)
        {
            var r = view[_top + i];
            int ry = list + i * RowH;
            bool here = ed.In(host._mouse.X, host._mouse.Y, x + 6, ry, w - 12, RowH), on = r.Id == current;
            if (here || on) host.UiFill(x + 6, ry, w - 12, RowH - 1, here ? 0xFF2A4A8A : 0xFF1C2E52);
            if (r.Id != 0)
            {
                string no = $"#{r.Id}";
                host.UiText(no, x + 52 - host.UiGetText(no, DimGray, 12).W, ry + 3, DimGray, 12);
            }
            host.UiText(r.Name, x + 60, ry + 3, on ? 0xFF00FFFF : White, 12);
            host.UiText(r.Category, x + 250, ry + 3, DimGray, 12);
            host.UiText(r.Info, x + w - 12 - host.UiGetText(r.Info, DimGray, 12).W, ry + 3, DimGray, 12);
        }
    }
}
