using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 도구 &gt; 아이템 보기 — 갈래(무기 · 갑옷 · 아뮬렛 · 반지 · 벨트 · 신발 · 캡슐)를 고르면 아이템 목록이, 아이템을 고르면 그림 · 값 · 수치 · 설명이 나온다
/// (사용자 요청 menu-87, 원본에 없다). <see cref="SkillBookScreen"/> 과 같은 틀이다.
/// </summary>
/// <remarks>
/// 설명은 상점 · Status 와 같은 글(<see cref="StatusScreen.ItemTipText"/>)이라 공격 · 방어 · 장비 보정 · 치명타 효과 · 캡슐의 대상과 효과가 같이 나온다.
/// 무기는 종류(Sword · Gun …)를 꼬리표로 달고, 그 무기를 들 수 있는 동료를 적는다(<see cref="GameDatabase.WeaponTypeOf"/>). 가진 수는 파티 가방 것이다.
/// 아이템 표의 갈래 머리줄(종류 255 「=== 리본류 ===」)과 「없음」은 뺀다.
/// </remarks>
internal sealed class ItemBookScreen(GameWindow host)
{
    internal const int MenuItemBook = 1179;
    internal const int W = 624, H = 456, RowH = 20, LineH = 16, TabY = 36, ListY = 72, ListW = 236;
    internal const int ListRows = (H - 50 - ListY) / RowH;
    internal const int DetailX = 12 + ListW + 12, DetailW = W - DetailX - 12, DetailY = ListY + 64, DetailRows = (H - 50 - DetailY) / LineH;
    private const uint Selected = 0xFF2A4A8A, Gold = 0xFFFFE070, Blue = 0xFFB8D8FF;

    /// <summary>갈래 — 이름과 그 갈래의 아이템 종류들(장비 칸 차례).</summary>
    private static readonly (string Name, Func<byte, bool> Has)[] Groups =
    [
        ("무기", t => t is 0 or 1 or (>= 8 and <= 19)), ("갑옷", t => t == 2), ("아뮬렛", t => t == 6), ("반지", t => t == 5),
        ("벨트", t => t == 4), ("신발", t => t == 3), ("캡슐", t => t == 7),
    ];

    internal bool _open = Environment.GetEnvironmentVariable("DUELDX_ITEMBOOK") != null;
    private int _group, _item, _listTop, _detailTop;
    private List<ItemData>? _items;
    private List<(string Text, uint Color)>? _lines;
    private readonly List<(int X, int Y, int W, int H, Action Act)> _hits = [];

    internal void Toggle() => (_open, _items, _lines) = (!_open, null, null);

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    /// <summary>무기 종류 이름 — 0 VES · 1 써클렛(이름 표에 없다) · 8~19 는 <see cref="GameDatabase.WeaponTypeName"/>.</summary>
    private static string KindName(byte type) => type == 1 ? "Circlet" : GameDatabase.WeaponTypeName(type);

    private List<ItemData> Items()
    {
        if (_items != null) return _items;
        if (host._db is not { } db) return [];
        var has = Groups[_group].Has;
        _items = [.. db.Items.Values.Where(i => has(i.Type) && db.T(i.NameId) is { Length: > 0 } name && name != "없음" && !name.StartsWith('='))
                                    .OrderBy(i => _group == 0 ? i.Type : 0).ThenBy(i => i.Id)];
        _item = Math.Clamp(_item, 0, Math.Max(0, _items.Count - 1));
        return _items;
    }

    private void PickGroup(int index)
    {
        (_group, _items, _item, _listTop) = (Math.Clamp(index, 0, Groups.Length - 1), null, 0, 0);
        PickItem(0);
    }

    private void PickItem(int index)
    {
        int count = Items().Count;
        _item = Math.Clamp(index, 0, Math.Max(0, count - 1));
        _listTop = Math.Clamp(_listTop, Math.Max(0, _item - ListRows + 1), _item);
        (_detailTop, _lines) = (0, null);
    }

    /// <summary>고른 아이템의 설명 칸 — 글자 너비를 재는 일이라 고를 때 한 번만 만든다.</summary>
    private List<(string Text, uint Color)> Lines()
    {
        if (_lines != null) return _lines;
        var lines = new List<(string, uint)>();
        if (host._db is not { } db || Items() is not { Count: > 0 } items) return lines;
        var item = items[_item];
        void Wrap(string text, uint color) { foreach (string l in host.Mos.WrapText(text, DetailW - 8, 12f)) lines.Add((l, color)); }
        string[] parts = host.StatusScr.ItemTipText(item).Split("$n");
        for (int i = 0; i < parts.Length; i++) Wrap(parts[i], i == 0 ? White : Blue);
        if (item.Type is 0 or 1 or (>= 8 and <= 19))
        {
            // 무기는 무기 종류가 맞는 인물만 든다(장비 1번 칸) — 지금 파티 동료 가운데 들 수 있는 사람.
            var who = host.Mos.StyleParty().Select(c => host._party.GetValueOrDefault(c)).OfType<CharacterData>()
                          .Where(c => db.WeaponTypeOf(c) == item.Type).Select(c => db.T(c.NameId)).Where(n => n.Length > 0).Distinct().ToList();
            lines.Add(("", White));
            Wrap(who.Count > 0 ? $"들 수 있는 동료: {string.Join(", ", who)}" : "지금 파티에 이 무기를 들 수 있는 동료가 없습니다.", DimGray);
        }
        return _lines = lines;
    }

    /// <summary>열려 있으면 클릭을 먹는다 — 누르는 칸은 그릴 때 적어 둔 것이다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        foreach (var (x, y, w, h, act) in _hits)
            if (bx >= x && bx < x + w && by >= y && by < y + h) { act(); break; }
        return true;
    }

    /// <summary>마우스가 목록 위면 목록을, 아니면 설명 칸을 굴린다.</summary>
    internal void OnWheel(int delta)
    {
        var (x, _) = Origin();
        if (host._mouse.X < x + DetailX - 6) _listTop = Math.Clamp(_listTop - delta, 0, Math.Max(0, Items().Count - ListRows));
        else _detailTop = Math.Clamp(_detailTop - 2 * delta, 0, Math.Max(0, Lines().Count - DetailRows));
    }

    internal void OnKey(int key)
    {
        switch (key)
        {
            case Win32.VK_ESCAPE: _open = false; break;
            case Win32.VK_UP: PickItem(_item - 1); break;
            case Win32.VK_DOWN: PickItem(_item + 1); break;
            case Win32.VK_LEFT: PickGroup(_group - 1); break;
            case Win32.VK_RIGHT: PickGroup(_group + 1); break;
        }
    }

    private void Button(string label, int x, int y, int w, int h, bool on, Action act)
    {
        host.UiFill(x, y, w, h, on ? Selected : StatusScreen.BoxBg);
        host.UiStroke(x, y, w, h, StatusScreen.BoxLine);
        label = Fit(label, w - 6, 12);
        host.UiText(label, x + (w - host.UiGetText(label, White, 12).W) / 2, y + (h - 16) / 2, White, 12);
        _hits.Add((x, y, w, h, act));
    }

    private string Fit(string text, int width, float size)
    {
        if (host.UiGetText(text, White, size).W <= width) return text;
        while (text.Length > 1 && host.UiGetText(text + "…", White, size).W > width) text = text[..^1];
        return text + "…";
    }

    internal void Draw()
    {
        if (!_open) return;
        _hits.Clear();
        var (x, y) = Origin();
        host.UiFill(x - 4, y - 4, W + 8, H + 8, 0x80000000);
        host.UiFill(x, y, W, H, StatusScreen.PanelBg);
        host.UiStroke(x, y, W, H, StatusScreen.BoxLine);
        host.UiFill(x, y, W, 28, StatusScreen.HeadBg);
        host.UiText("아이템 보기", x + 10, y + 6, White);
        const string keys = "Esc: 닫기 · ↑↓: 아이템 · ←→: 갈래 · 휠: 굴리기";
        host.UiText(keys, x + W - 10 - host.UiGetText(keys, DimGray, 11).W, y + 8, DimGray, 11);
        Button("닫기", x + W - 116, y + H - 40, 100, 28, false, () => _open = false);
        if (host._db is not { } db)
        {
            host.UiText("아직 게임 자료를 읽지 못했습니다.", x + 20, y + TabY + 6, DimGray, 12);
            return;
        }

        int tabW = (W - 24) / Groups.Length;
        for (int i = 0; i < Groups.Length; i++)
        {
            int index = i;
            Button(Groups[i].Name, x + 12 + i * tabW, y + TabY, tabW - 4, 24, i == _group, () => PickGroup(index));
        }
        host.UiFill(x + 12, y + ListY - 5, W - 24, 1, StatusScreen.BoxLine);

        // 왼쪽: 아이템 — 그림 · 이름 · (무기면 종류) · 가진 수.
        var items = Items();
        _listTop = Math.Clamp(_listTop, 0, Math.Max(0, items.Count - ListRows));
        for (int i = 0; i < ListRows && _listTop + i < items.Count; i++)
        {
            int index = _listTop + i, ry = y + ListY + i * RowH;
            var item = items[index];
            if (index == _item) host.UiFill(x + 12, ry - 1, ListW, RowH, Selected);
            int owned = host.StatusScr._inventory.GetValueOrDefault(item.Id);
            string tag = string.Join(" ", new[] { _group == 0 ? KindName(item.Type) : "", owned > 0 ? $"x{owned}" : "" }.Where(t => t.Length > 0));
            int tagW = tag.Length > 0 ? host.UiGetText(tag, DimGray, 10).W + 6 : 0;
            host.UiText(Fit(db.T(item.NameId), ListW - 14 - tagW, 12), x + 18, ry + 1, White, 12);
            if (tagW > 0) host.UiText(tag, x + 12 + ListW - tagW, ry + 3, index == _item ? White : DimGray, 10);
            _hits.Add((x + 12, ry - 1, ListW, RowH, () => PickItem(index)));
        }
        if (items.Count > ListRows)
            host.UiText($"{_listTop + 1}~{Math.Min(items.Count, _listTop + ListRows)} / {items.Count}", x + 16, y + H - 50 - 2, DimGray, 10);
        host.UiFill(x + DetailX - 6, y + ListY, 1, H - 50 - ListY, StatusScreen.BoxLine);

        // 오른쪽: 고른 아이템 — 그림 · 이름 · 값, 그 아래는 굴리는 설명 칸.
        if ((uint)_item >= items.Count) return;
        var it = items[_item];
        int dx = x + DetailX;
        host.UiFill(dx, y + ListY, 44, 44, StatusScreen.BoxBg);
        host.UiStroke(dx, y + ListY, 44, 44, StatusScreen.BoxLine);
        host.UiSprite(StatusScreen.ItemPictureObs, it.PictureMotion, 0, dx + 6, y + ListY + 6, loop: false);
        host.UiText(Fit(db.T(it.NameId), DetailW - 56, 15), dx + 54, y + ListY + 1, Gold, 15);
        var meta = new List<string> { $"#{it.Id}" };
        if (_group == 0 && KindName(it.Type) is { Length: > 0 } kind) meta.Add(kind);
        meta.Add(it.Price > 0 ? $"{it.Price:N0} GP" : "팔지 않음");
        int have = host.StatusScr._inventory.GetValueOrDefault(it.Id);
        if (have > 0) meta.Add($"가방에 {have}개");
        host.UiText(Fit(string.Join(" · ", meta), DetailW - 56, 11), dx + 54, y + ListY + 24, DimGray, 11);

        var lines = Lines();
        _detailTop = Math.Clamp(_detailTop, 0, Math.Max(0, lines.Count - DetailRows));
        for (int i = 0; i < DetailRows && _detailTop + i < lines.Count; i++)
            host.UiText(lines[_detailTop + i].Text, dx, y + DetailY + i * LineH, lines[_detailTop + i].Color, 12);
        if (lines.Count > DetailRows)
        {
            int trackH = DetailRows * LineH, thumbH = Math.Max(12, trackH * DetailRows / lines.Count);
            host.UiFill(x + W - 10, y + DetailY, 3, trackH, StatusScreen.BoxBg);
            host.UiFill(x + W - 10, y + DetailY + (trackH - thumbH) * _detailTop / (lines.Count - DetailRows), 3, thumbH, DimGray);
        }
    }
}
