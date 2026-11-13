namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 「캐릭터 배치」 창 명단 줄을 오른쪽 단추로 누르면 뜨는 작은 메뉴 — 「배치 취소」(미배치면 「배치」) · 「탑승」(탔으면 「내리기」).
/// 원본에는 없다(사용자 요청). 동작은 배치 창 단추와 같은 것을 부른다 — 배치 취소는 <see cref="BattleScene.Unplace"/>,
/// 배치는 첫 빈 초록 칸에 <see cref="BattleScene.PlaceOnSpot"/>(자동배치와 같은 칸 순서), 탑승 · 내리기는 <see cref="Part1MountDeploy"/> 의
/// Ride · Cancel(원본 인물 메뉴 차례 4 · 5 의 조건 <see cref="Part1MountDeploy.CannotRide"/> 그대로). 탈 수 있는 탈것이 여럿이면 옆에 하위 목록을 연다.
/// 파트2 판에는 탈것이 없어 「탑승」 줄을 안 보인다.
/// </summary>
internal sealed class DeployContextMenu(GameWindow host)
{
    /// <summary>메뉴를 연 명단 유닛(탄 인물이면 탈것 유닛) — null 이면 닫힘.</summary>
    internal UnitState? Who;

    /// <summary>메뉴 왼쪽 위(판 픽셀) — 누른 자리.</summary>
    private int _x, _y;

    /// <summary>탈것 하위 목록이 열렸나.</summary>
    private bool _subOpen;

    internal bool Open => Who != null;

    private const int W = 132, SubW = 168, RowH = 24, Pad = 4;
    private const uint HoverBg = 0xFF2A4A8A, OffInk = 0xFF707070;

    private sealed record Item(string Text, bool On, string? Why, Action? Do, bool Sub = false);

    internal void Close()
    {
        Who = null;
        _subOpen = false;
    }

    /// <summary>그 명단 유닛의 메뉴를 누른 자리(판 픽셀)에 연다 — 보이는 영역 안으로 당긴다.</summary>
    internal void OpenAt(UnitState who, int bx, int by)
    {
        Who = who;
        _subOpen = false;
        _x = bx;
        _y = by;
        int h = Pad * 2 + Items().Count * RowH;
        _x = Math.Clamp(_x, host._camX, Math.Max(host._camX, host._camX + host.ViewWidth - W));
        _y = Math.Clamp(_y, host._camY, Math.Max(host._camY, host._camY + host.ViewHeight - h));
    }

    private List<Item> Items()
    {
        var btl = host.Btl;
        var items = new List<Item>();
        if (Who is not { } u) return items;
        if (!btl.DeployMovable(u)) items.Add(new("배치 취소", false, "이 전투에 고정된 인물(Entry)입니다", null));
        else if (u.OnField) items.Add(new("배치 취소", true, null, () => { btl.Unplace(u); if (btl._deployPick == u) btl._deployPick = null; }));
        else
        {
            int spot = FirstFreeSpot();
            items.Add(new("배치", spot >= 0, "빈 초록 칸이 없습니다", () =>
            {
                int at = FirstFreeSpot();
                if (at >= 0) { btl.PlaceOnSpot(u, at); if (btl._deployPick == u) btl._deployPick = null; }
            }));
        }
        if (host._scene.IsPart1)
        {
            var mount = host.MountDeploy;
            if (mount.MountOf(u) > 0) items.Add(new("내리기", true, null, () => WithMount(u, mount.Cancel)));
            else
            {
                string? why = mount.CannotRide(u);
                var rides = why == null ? Rideable(u) : [];
                if (why == null && rides.Count == 0) why = "남은 탈것이 없습니다";
                string label = mount.Forbidden ? "탑승 금지" : rides.Count == 1 ? $"탑승: {Name(rides[0])}" : rides.Count > 1 ? "탑승  ▸" : "탑승";
                items.Add(new(label, why == null, why, rides.Count == 1 ? () => WithMount(u, () => mount.Ride(rides[0])) : null, Sub: rides.Count > 1));
            }
        }
        return items;
    }

    /// <summary>그 인물이 지금 탈 수 있는 무리 탈것(Chr) — 남은 수가 있고 탑승 어빌리티가 맞는 것.</summary>
    private List<int> Rideable(UnitState u)
    {
        var mount = host.MountDeploy;
        var person = mount.Person(u);
        return [.. mount.Pool().Where(p => p.Left > 0 && mount.CanRide(person, p.Chr)).Select(p => p.Chr)];
    }

    /// <summary>탈것 창의 동작을 그 인물에게 — 탈것 창(<see cref="Part1MountDeploy.Who"/>)을 잠깐 그 인물로 열었다가 닫는다.</summary>
    private void WithMount(UnitState u, Action act)
    {
        var mount = host.MountDeploy;
        host.MercDeploy.Leader = null;
        mount.Who = u;
        act();
        mount.Who = null;
    }

    private int FirstFreeSpot()
    {
        var btl = host.Btl;
        for (int i = 0; i < btl._deploySpots.Count; i++)
            if (btl.UnitOnCell(btl._deploySpots[i].Col, btl._deploySpots[i].Row) == null) return i;
        return -1;
    }

    private string Name(int chr) => host._db?.Character(chr) is { } c ? host._db.T(c.NameId) : $"Chr {chr}";

    /// <summary>하위 목록 자리 — 메뉴 오른쪽(모자라면 왼쪽), 「탑승」 줄 높이.</summary>
    private (int X, int Y) SubPanel(int row, int count)
    {
        int sx = _x + W - 2 + SubW <= host._camX + host.ViewWidth ? _x + W - 2 : _x - SubW + 2;
        int sy = _y + Pad + row * RowH - Pad;
        sy = Math.Clamp(sy, host._camY, Math.Max(host._camY, host._camY + host.ViewHeight - (Pad * 2 + count * RowH)));
        return (sx, sy);
    }

    /// <summary>메뉴가 떠 있을 때의 왼쪽 클릭 — 줄이면 그 동작, 밖이면 닫기만(뒤의 판 · 창은 안 받는다).</summary>
    internal void OnClick(int bx, int by)
    {
        if (Who is not { } u) return;
        var items = Items();
        int subRow = items.FindIndex(i => i.Sub);
        if (_subOpen && subRow >= 0)
        {
            var rides = Rideable(u);
            var (sx, sy) = SubPanel(subRow, rides.Count);
            if (bx >= sx && bx < sx + SubW && by >= sy + Pad && by < sy + Pad + rides.Count * RowH)
            {
                int chr = rides[(by - sy - Pad) / RowH];
                WithMount(u, () => host.MountDeploy.Ride(chr));
                Close();
                return;
            }
        }
        if (bx >= _x && bx < _x + W && by >= _y + Pad && by < _y + Pad + items.Count * RowH)
        {
            var item = items[(by - _y - Pad) / RowH];
            if (!item.On) { if (item.Why != null) host.Toast(item.Why); return; }
            if (item.Sub) { _subOpen = true; return; }
            item.Do?.Invoke();
            Close();
            return;
        }
        Close();
    }

    internal void Draw()
    {
        if (Who is not { } u) return;
        // 명단에서 사라졌으면(배치종료 · 판 바뀜) 닫는다.
        if (!host.Btl.DeployRoster().Contains(u)) { Close(); return; }
        var items = Items();
        var (mx, my) = host._mouse;
        int h = Pad * 2 + items.Count * RowH;
        host.UiFill(_x, _y, W, h, StatusScreen.PanelBg);
        host.UiStroke(_x, _y, W, h, StatusScreen.BoxLine);
        int hover = mx >= _x && mx < _x + W && my >= _y + Pad && my < _y + Pad + items.Count * RowH ? (my - _y - Pad) / RowH : -1;
        // 마우스를 「탑승 ▸」 줄에 올리면 하위 목록을 연다, 다른 줄로 옮기면 닫는다.
        if (hover >= 0) _subOpen = items[hover].Sub && items[hover].On || (_subOpen && items[hover].Sub);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            int ry = _y + Pad + i * RowH;
            if ((i == hover || it.Sub && _subOpen) && it.On) host.UiFill(_x + 2, ry, W - 4, RowH, HoverBg);
            host.UiText(it.Text, _x + 10, ry + 3, it.On ? White : OffInk);
        }
        int subRow = items.FindIndex(i => i.Sub);
        if (!_subOpen || subRow < 0) return;
        var rides = Rideable(u);
        var mount = host.MountDeploy;
        var pool = mount.Pool();
        var (sx, sy) = SubPanel(subRow, rides.Count);
        int sh = Pad * 2 + rides.Count * RowH;
        host.UiFill(sx, sy, SubW, sh, StatusScreen.PanelBg);
        host.UiStroke(sx, sy, SubW, sh, StatusScreen.BoxLine);
        for (int r = 0; r < rides.Count; r++)
        {
            int ry = sy + Pad + r * RowH;
            if (mx >= sx && mx < sx + SubW && my >= ry && my < ry + RowH) host.UiFill(sx + 2, ry, SubW - 4, RowH, HoverBg);
            host.UiText(Name(rides[r]), sx + 10, ry + 3, White);
            var p = pool.FirstOrDefault(q => q.Chr == rides[r]);
            string n = $"{p.Left}/{p.Total}";
            host.UiText(n, sx + SubW - 10 - host.UiGetText(n, White).W, ry + 3, 0xFFD0E8D0);
        }
    }
}
