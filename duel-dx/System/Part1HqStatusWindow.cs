using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 챕터 지도의 상태 창(「상  태」 단추, 원본 <c>0x100b4fe0</c>) — 고른 팀의 인물 10칸 · 소지금액 · 아이템 격자 · 용병/탑승물 정보.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-챕터」 13.5. 틀 상자 (65, 80) 510×370. 얼굴을 누르면 원본은 인물 상세 창(0x100bb580)을 여는데, 리메이크는 그 자리에 전투와 같은 상태 패널(<see cref="Part1StatusPanel"/>)을 가운데에 띄운다(사용자 요청).
/// 아이템 칸을 짚으면 이름 · 설명 · 효과(<see cref="Part1ItemTip"/>).
/// 원본은 아이템 · 용병 · 탑승물을 팀마다 들지만 리메이크는 무리 하나가 같이 쓴다(<see cref="Part1Progress.Bag"/> · <see cref="Part1Progress.Mercenaries"/> · <see cref="Part1Progress.Mounts"/>).
/// 돈은 원본도 이야기 무리 돈(0x101a07e0[머리 낱말 1])이다 — 리메이크의 eld.
/// </remarks>
internal sealed class Part1HqStatusWindow(GameWindow host, Part1ChapterScreen map)
{
    internal const int X = 65, Y = 80, W = 510, H = 370;

    internal const int FaceHit = 5000, ItemHit = 1000, PeopleHit = 0x190, MountHit = 0x191, ItemUp = 900, ItemDown = 901, MercUp = 910, MercDown = 911, TableHit = 920;

    /// <summary>「일행 한눈에」 단추(리메이크 — 일행 표 <see cref="Part1PartyTable"/>) — 팀 이름표 오른쪽.</summary>
    private const int TableX = 196, TableY = 10, TableW = 88, TableH = 20;

    /// <summary>아이템 목록 (14, 181) — 8열 × 3줄, 칸 25×43, 여백 10 · 틈 5.</summary>
    internal const int ItemX = 14, ItemY = 181, ItemW = 270, ItemH = 160, ItemCols = 8, ItemRows = 3, CellW = 25, CellH = 43;

    /// <summary>용병 정보 (294, 40) — 1열 × 3줄, 칸 160×80.</summary>
    internal const int MercX = 294, MercY = 40, MercW = 198, MercH = 298, MercRowTop = 30, MercRowH = 86;

    internal bool _open;
    private int _team;
    private int _hover = -1;
    private int _itemTop, _mercTop;

    /// <summary>용병(0x190) · 탑승물(0x191) 목록 중 무엇을 보나 — 처음은 용병.</summary>
    private bool _mounts;

    internal void Open()
    {
        _team = map.SelectedTeamIndex;
        _open = true;
        _hover = -1;
        _itemTop = _mercTop = 0;
        _mounts = false;
    }

    internal void Close() { _panel?.CloseOff(); _open = false; }

    /// <summary>얼굴을 누르면 뜨는 상태 패널(전투의 것과 같은 그림, 전투 밖 꼴).</summary>
    private Part1StatusPanel Panel => _panel ??= new(host);
    private Part1StatusPanel? _panel;

    internal bool PanelOpen => _panel is { OffOpen: true };

    /// <summary>Esc — 패널이 떠 있으면 패널만 닫는다. 닫았으면 true.</summary>
    internal bool ClosePanel()
    {
        if (!PanelOpen) return false;
        _panel!.CloseOff();
        return true;
    }

    /// <summary>←/→ — 패널이 떠 있으면 팀의 앞 · 뒤 사람으로.</summary>
    internal bool OnKey(int key)
    {
        if (!PanelOpen || key is not (Native.Win32.VK_LEFT or Native.Win32.VK_RIGHT)) return false;
        _panel!.SwitchOff(key == Native.Win32.VK_LEFT ? -1 : 1);
        return true;
    }

    /// <summary>오른 단추 — 패널이 떠 있으면 패널이 받는다(어빌리티 설명 · 목록 닫기 · 밖이면 패널 닫기). 받았으면 true.</summary>
    internal bool OnRightDown(int bx, int by)
    {
        if (!PanelOpen) return false;
        _panel!.OffRightDown(bx, by);
        return true;
    }

    /// <summary>가방을 (아이템, 수) 로 — 처음 들어온 차례.</summary>
    private List<(int Id, int Count)> Items() => [.. host.Part1.Bag.GroupBy(id => id).Select(g => (g.Key, g.Count()))];

    private List<(int Chr, int Count)> Mercs() =>
        [.. (_mounts ? host.Part1.Mounts : host.Part1.Mercenaries).Where(m => m.Value > 0).OrderBy(m => m.Key).Select(m => (m.Key, m.Value))];

    private int HitAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        int x = bx - ox - X, y = by - oy - Y;
        for (int i = 0; i < 10; i++)
        {
            int cx = 39 + 55 * (i % 5), cy = 65 + 55 * (i / 5);
            if (Math.Abs(x - cx) < 25 && Math.Abs(y - cy) < 25) return FaceHit + i;
        }
        if (Part1MapArt.ScrollHit(ItemX + ItemW - 16, ItemY, ItemH, x, y) is not 0 and var s) return s < 0 ? ItemUp : ItemDown;
        if (Part1MapArt.ScrollHit(MercX + MercW - 16, MercY + MercRowTop, MercH - MercRowTop, x, y) is not 0 and var m) return m < 0 ? MercUp : MercDown;
        var items = Items();
        for (int k = 0; k < ItemCols * ItemRows; k++)
        {
            int i = _itemTop * ItemCols + k;
            if (i >= items.Count) break;
            int cx = ItemX + 10 + k % ItemCols * (CellW + 5), cy = ItemY + 10 + k / ItemCols * (CellH + 5);
            if (x >= cx && x < cx + CellW && y >= cy && y < cy + CellH) return ItemHit + i;
        }
        if (x >= TableX && x < TableX + TableW && y >= TableY && y < TableY + TableH) return TableHit;
        if (x >= MercX + 150 && x < MercX + 173 && y >= MercY + 5 && y < MercY + 28) return PeopleHit;
        if (x >= MercX + 173 && x < MercX + 196 && y >= MercY + 5 && y < MercY + 28) return MountHit;
        return -1;
    }

    internal void UpdateHover(int bx, int by) => _hover = PanelOpen ? -1 : HitAt(bx, by);

    internal void OnClick(int bx, int by)
    {
        if (PanelOpen) { _panel!.OffClick(bx, by); return; }
        int hit = HitAt(bx, by);
        var (ox, oy) = host.Mos.MosesOrigin();
        int x = bx - ox, y = by - oy;
        switch (hit)
        {
            case >= FaceHit and < FaceHit + 10:
                var members = host.Part1.TeamMembers(_team);
                if (hit - FaceHit < members.Count) Panel.OpenOff(members[hit - FaceHit], members, map.SaveProgress);
                break;
            case TableHit: host.PartyTable.Open(); break;
            case PeopleHit: _mounts = false; _mercTop = 0; break;
            case MountHit: _mounts = true; _mercTop = 0; break;
            case ItemUp: _itemTop = Math.Max(0, _itemTop - 1); break;
            case ItemDown: _itemTop = Math.Clamp(_itemTop + 1, 0, Math.Max(0, (Items().Count + ItemCols - 1) / ItemCols - ItemRows)); break;
            case MercUp: _mercTop = Math.Max(0, _mercTop - 1); break;
            case MercDown: _mercTop = Math.Clamp(_mercTop + 1, 0, Math.Max(0, Mercs().Count - 3)); break;
            case -1 when x < X || y < Y || x >= X + W || y >= Y + H: Close(); break;   // 창 밖을 누르면 닫는다
        }
    }

    internal void Draw(int ox, int oy)
    {
        var art = map.Art;
        int wx = ox + X, wy = oy + Y;
        art.Box(wx, wy, W, H);
        art.Tag(wx + 30, wy + 10, $"{_team + 1} 팀");
        host.UiFill(wx + TableX, wy + TableY, TableW, TableH, _hover == TableHit ? 0xFF4A66D0 : 0xFF2A3A80);
        host.UiStroke(wx + TableX, wy + TableY, TableW, TableH, 0xFFB0C0F0);
        host.UiText("동료 정보", wx + TableX + (TableW - host.UiGetText("동료 정보", GameWindow.White, 11).W) / 2, wy + TableY + 4, GameWindow.White, 11);

        // 인물 10칸 — 얼굴(Obs 단추) 또는 Obs 217 모션 2 「Empty」.
        var members = host.Part1.TeamMembers(_team);
        for (int i = 0; i < 10; i++)
        {
            int cx = wx + 39 + 55 * (i % 5), cy = wy + 65 + 55 * (i / 5);
            if (i < members.Count && art.Face(members[i], cx, cy))
            {
                if (_hover == FaceHit + i) art.Stroke(cx - 25, cy - 25, 50, 50, 0xFFFFE080);
            }
            else art.Blit(art.Frame(217, 2, 0), wx + 14 + 55 * (i % 5), wy + 40 + 55 * (i / 5));
        }

        // 소지금액 — Obs 217 모션 0(두 장 141 + 129), 글 (155, 163) · 숫자 오른끝 275.
        art.Blit(art.Frame(217, 0, 0), wx + 14, wy + 152);
        art.Blit(art.Frame(217, 0, 1), wx + 14, wy + 152);
        art.Text("소지금액", wx + 155, wy + 155, GameWindow.White, 12);
        art.TextRight($"{host.Part1.Eld}", wx + 275, wy + 155, GameWindow.White, 12);

        // 아이템 격자 — Obs 12 의 아이템 그림 모션 · 아래 「[수]」.
        art.Panel(wx + ItemX, wy + ItemY, ItemW, ItemH);
        art.ScrollBar(wx + ItemX + ItemW - 16, wy + ItemY, ItemH);
        var items = Items();
        var table = Part1Item.LoadAll();
        string? tip = null;
        for (int k = 0; k < ItemCols * ItemRows; k++)
        {
            int i = _itemTop * ItemCols + k;
            if (i >= items.Count) break;
            int cx = wx + ItemX + 10 + k % ItemCols * (CellW + 5), cy = wy + ItemY + 10 + k / ItemCols * (CellH + 5);
            var item = Part1Item.ById(table, items[i].Id);
            if (item == null || art.Motion(12, item.Icon) is not { } icon) art.Stroke(cx, cy, CellW, CellW, 0xFF8890B8);
            else art.Blit(icon, cx - icon.X, cy - icon.Y);
            art.TextCentered($"[{items[i].Count}]", cx + CellW / 2, cy + 34, GameWindow.White, 10);
            if (_hover == ItemHit + i) { art.Stroke(cx - 1, cy - 1, CellW + 2, CellW + 2, 0xFFFFE080); tip = Part1ItemTip.Text(item); }
        }

        // 용병 정보 — 제목 · 사람(0x190)/탑승물(0x191) 고르기(Obs 613) · 칸마다 전투 그림 · 이름(연두) · 머릿수(분홍) · 능력치.
        art.Panel(wx + MercX, wy + MercY, MercW, MercH);
        art.Text(_mounts ? "탑승물 정보" : "용병 정보", wx + MercX + 8, wy + MercY + 8, GameWindow.White, 13);
        art.Blit(art.Motion(613, !_mounts ? 2 : _hover == PeopleHit ? 1 : 0), wx + MercX + 150, wy + MercY + 5);
        art.Blit(art.Motion(613, _mounts ? 5 : _hover == MountHit ? 4 : 3), wx + MercX + 173, wy + MercY + 5);
        art.ScrollBar(wx + MercX + MercW - 16, wy + MercY + MercRowTop, MercH - MercRowTop);
        var mercs = Mercs();
        for (int r = 0; r < 3 && _mercTop + r < mercs.Count; r++)
        {
            var (code, count) = mercs[_mercTop + r];
            if (art.Chr(code) is not { } chr) continue;
            int rx = wx + MercX, ry = wy + MercY + MercRowTop + r * MercRowH;
            int tick = (int)(host._lastTime * GameWindow.TicksPerSecond);
            if (chr.SpriteId > 0) art.Blit(art.Motion(chr.SpriteId, 2, tick), rx + 25, ry + 70);
            art.Text(chr.Name, rx + 55, ry + 6, 0xFFC8FF96, 13);
            art.TextRight($"{count}", rx + 160, ry + 6, 0xFFFF96C8, 13);
            (string Label, int Value, int Col, int Row)[] stats =
            [
                ("Hp", chr.MaxHp, 0, 0), ("Str", chr.Stat(Part1Chr.Str), 0, 1), ("Skill", chr.Stat(Part1Chr.Skill), 0, 2), ("Int", chr.Stat(Part1Chr.Int), 0, 3),
                ("Dex", chr.Stat(Part1Chr.Dex), 1, 0), ("Spd", chr.Stat(Part1Chr.Spd), 1, 1), ("Ac", chr.Stat(Part1Chr.Ac), 1, 2), ("Mr", chr.Stat(Part1Chr.Mr), 1, 3),
            ];
            foreach (var (label, value, col, row) in stats)
            {
                int lx = rx + (col == 0 ? 55 : 115), ly = ry + 24 + row * 16;
                art.Text(label, lx, ly, 0xFFE0E4F0, 10);
                art.TextRight($"{value}", rx + (col == 0 ? 100 : 160), ly, GameWindow.White, 10);
            }
        }
        if (mercs.Count == 0) art.TextCentered(_mounts ? "탑승물이 없습니다" : "용병이 없습니다", wx + MercX + 90, wy + MercY + 80, 0xFFA0A8C8, 12);

        // 아이템 설명 — 이름 · 설명 · 효과(사용자 요청: 이름만 보였다).
        if (tip is { Length: > 0 } && !PanelOpen) host.StatusScr.DrawDescriptionTip(tip, host._mouse.X, host._mouse.Y, ox, oy, 640, 480);

        // 얼굴을 눌러 연 상태 패널 — 창 위 가운데에.
        if (PanelOpen) _panel!.DrawOff(ox, oy);
    }
}
