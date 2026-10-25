using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 챕터 지도의 마을 상점 정보 창 — 마을 장소(종류 1)를 누르면 그 마을 필드의 상점들(1001 · 1002)이 파는 물건을 늘어놓는다(사용자 요청, 원본에 없다).
/// </summary>
/// <remarks>
/// 상점 목록은 마을 화면과 같은 것(<see cref="Part1TownScreen.FacilitiesOf"/>)이고, 값도 상점 창의 사는 값(아이템 값 × 머리 인자 5 / 100, <see cref="Part1TownWindow"/>)이다.
/// 상점마다 제목(머리 인자 0 의 줄) 밑에 아이콘 · 이름 · 값 줄 — 세로 칸이 차면 오른쪽 칸으로 넘긴다. 줄을 짚으면 아이템 설명(<see cref="Part1ItemTip"/>).
/// 창 밖을 누르거나 오른 단추 · Esc 로 닫는다.
/// </remarks>
internal sealed class Part1TownShopInfo(GameWindow host, Part1ChapterScreen chapter)
{
    internal bool _open;

    private string _town = "";
    private readonly List<Line> _lines = [];
    private readonly List<string> _others = [];
    private IReadOnlyList<Part1Item>? _items;
    private int _hover = -1;
    private int _cols = 1, _rows;

    /// <summary>창 안의 한 줄 — 상점 제목이면 Item 이 null.</summary>
    private sealed record Line(string Text, Part1Item? Item, int Price);

    private const int ColW = 200, RowH = 22, MaxRows = 16, HeadH = 40, Pad = 12, FootH = 24;

    private Part1MapArt Art => chapter.Art;

    /// <summary>그 마을 필드의 상점들로 창을 연다 — 필드를 못 읽으면 알리고 안 연다.</summary>
    internal void Open(string town, int fieldId)
    {
        if (Part1Field.Load(fieldId) is not { } field)
        {
            host.Toast($"파트1 마을 필드 {fieldId:D4} 를 못 읽었습니다");
            return;
        }
        _items ??= Part1Item.LoadAll();
        _town = town;
        _lines.Clear();
        _others.Clear();
        string FieldLine(int i) => i >= 0 && i < field.Lines.Count ? field.Lines[i] : "";
        foreach (var f in Part1TownScreen.FacilitiesOf(field))
        {
            if (f.Kind != Part1TownScreen.Kind.Shop)
            {
                _others.Add(f.Kind == Part1TownScreen.Kind.Guild ? "용병 길드" : "주점");
                continue;
            }
            string title = f.Head.Length > 0 ? Part1Game.PlainText(FieldLine(f.Head[0])).Replace('\n', ' ').Trim() : "";
            _lines.Add(new Line(title.Length > 0 ? title : f.Name, null, 0));
            int rate = f.Head.Length > 5 && f.Head[5] > 0 ? f.Head[5] : 100;
            foreach (var e in f.Entries)
                if (e.Length > 0 && Part1Item.ById(_items, e[0]) is { } item)
                    _lines.Add(new Line(item.Name, item, item.Price * rate / 100));
        }
        // 칸 나누기 — 한 칸 MaxRows 줄, 상점 제목이 칸 맨 밑에 홀로 남지 않게 다음 칸으로 민다.
        _cols = 1;
        _rows = 0;
        int row = 0;
        _layout.Clear();
        foreach (var line in _lines)
        {
            if (row >= MaxRows || line.Item == null && row >= MaxRows - 1 && row > 0) { _cols++; row = 0; }
            _layout.Add((_cols - 1, row));
            row++;
            _rows = Math.Max(_rows, row);
        }
        _hover = -1;
        _open = true;
    }

    /// <summary>줄마다 (칸, 줄) 자리.</summary>
    private readonly List<(int Col, int Row)> _layout = [];

    internal void Close() => _open = false;

    /// <summary>창 네모(640×480 기준) — 화면 가운데.</summary>
    private (int X, int Y, int W, int H) Rect
    {
        get
        {
            int w = Math.Max(300, Pad * 2 + _cols * ColW), h = HeadH + Math.Max(1, _rows) * RowH + FootH + Pad;
            w = Math.Min(w, 620);
            return ((MosesScene.MosesW - w) / 2, Math.Max(10, (MosesScene.MosesH - h) / 2), w, h);
        }
    }

    private int LineAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        var (x, y, _, _) = Rect;
        for (int i = 0; i < _lines.Count; i++)
        {
            var (c, r) = _layout[i];
            int lx = ox + x + Pad + c * ColW, ly = oy + y + HeadH + r * RowH;
            if (bx >= lx && bx < lx + ColW - 6 && by >= ly && by < ly + RowH) return i;
        }
        return -1;
    }

    private bool Inside(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        var (x, y, w, h) = Rect;
        return bx >= ox + x && bx < ox + x + w && by >= oy + y && by < oy + y + h;
    }

    internal void UpdateHover(int bx, int by) => _hover = LineAt(bx, by);

    /// <summary>창 밖을 누르면 닫는다 — 안은 아무 일 없음.</summary>
    internal void OnClick(int bx, int by)
    {
        if (!Inside(bx, by)) Close();
    }

    internal void Draw(int ox, int oy)
    {
        var (x, y, w, h) = Rect;
        x += ox;
        y += oy;
        Art.Fill(x, y, w, h, 0xFF101428);   // 틀 상자 돌 무늬는 반투명 — 밑의 팀 말 · 얼굴이 비치지 않게 먼저 검게
        Art.Box(x, y, w, h);
        Art.Text($"{_town} — 상점", x + Pad + 2, y + 12, 0xFFFFE080, 14);
        string eld = $"소지금 {host.Part1.Eld} Eld";
        Art.TextRight(eld, x + w - Pad - 2, y + 14, GameWindow.White, 11);
        if (_lines.Count == 0) Art.Text("파는 물건이 없습니다", x + Pad + 4, y + HeadH + 3, 0xFFA0A8C8, 12);
        string? tip = null;
        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            var (c, r) = _layout[i];
            int lx = x + Pad + c * ColW, ly = y + HeadH + r * RowH;
            if (line.Item is not { } item)
            {
                // 상점 제목 — 노란 글 밑에 가는 줄.
                Art.Text(line.Text, lx + 2, ly + 4, 0xFFFFE080, 12);
                Art.Fill(lx, ly + RowH - 3, ColW - 8, 1, 0xFF8890B8);
                continue;
            }
            if (_hover == i) { Art.Fill(lx, ly, ColW - 6, RowH - 1, 0x60304890); tip = Part1ItemTip.Text(item); }
            Art.Fill(lx + 2, ly + 1, 20, 20, 0xC0080A20);
            if (Art.Motion(12, item.Icon) is { } icon) Art.Blit(icon, lx + 2 + (20 - icon.W) / 2 - icon.X, ly + 1 + (20 - icon.H) / 2 - icon.Y);
            Art.Text(line.Text, lx + 27, ly + 4, GameWindow.White, 11);
            Art.TextRight($"{line.Price}", lx + ColW - 12, ly + 4, line.Price > host.Part1.Eld ? 0xFFFF8080 : 0xFFFFE080, 11);
        }
        string foot = _others.Count > 0 ? $"그 밖: {string.Join(" · ", _others.Distinct())}" : "";
        if (foot.Length > 0) Art.Text(foot, x + Pad + 2, y + h - FootH - 2, 0xFFC8D0E8, 11);
        const string hint = "오른 단추 · Esc 로 닫기";
        if ((foot.Length > 0 ? Art.TextW(foot, 11) : 0) + Art.TextW(hint, 10) + 2 * Pad + 16 < w) Art.TextRight(hint, x + w - Pad - 2, y + h - FootH - 1, 0xFF8890B8, 10);
        if (tip is { Length: > 0 }) host.StatusScr.DrawDescriptionTip(tip, host._mouse.X, host._mouse.Y, ox, oy, MosesScene.MosesW, MosesScene.MosesH);
    }
}
