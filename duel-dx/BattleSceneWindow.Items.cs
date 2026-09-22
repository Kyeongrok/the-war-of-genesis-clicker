using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 전투 중 아이템 쓰기(링 ITEM, 하위 메뉴 모드 2) — 목록 창과 쓰기.
/// </summary>
/// <remarks>
/// 옵시디안 분석-전투 「전투 중 아이템 쓰기」: 목록은 <c>0x100d3830</c>(창 0x455, 줄 0x456) —
/// <b>1열 × 보이는 줄 8, 줄 182×22</b>, 안쪽 너비 206, 링 중심에 가운데 맞춘다. 줄 하나는
/// 바탕 <c>Obs 0471 모션 4</c> @(−4,0) · 아이콘 <c>Obs 0326</c>(모션 = 아이템 그림) @(8,2) ·
/// 이름 오른끝 <c>줄너비−46</c> · 개수 <c>"(x%d)"</c> 오른끝 <c>줄너비−6</c>. 잠기는 줄은 없고, 목록이 비면 TXR 0 「없음」 한 줄.
/// <para>
/// 쓸 수 있는 것은 <b>종류 7(캡슐)이고 쓰는 work(파일 42)이 있는 것</b>뿐이다. 고르면 어빌리티와 똑같이 대상 고르기로 가고,
/// 대상을 확정하는 순간 <b>개수가 하나 준다</b>(고르다 취소하면 안 준다). TP 는 work 값(80)을 work 이 끝날 때 물고, <b>차례는 끝나지 않는다</b>.
/// 회복 캡슐은 몸짓 없이 이펙트 <c>Obs 0297</c>·<c>Obs 0312</c> 와 <c>Snd 85</c> 로 나타난다(회복량 = 최대 HP × work 위력 / 100).
/// </para>
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int ItemRowW = 182, ItemRowH = 22, ItemRows = 8, ItemMenuW = 206;
    private const int ItemIconObs = 326, ItemRowObs = 471, ItemRowMotion = 4;

    private bool _itemMenu;
    private int _itemTop;

    /// <summary>대상을 고르는 중인 아이템 — 쓰면 개수가 하나 준다. 0 이면 아이템이 아니다.</summary>
    private int _targetItem;

    /// <summary>가방에서 전투에 쓸 수 있는 것만 — (아이템, 개수).</summary>
    private List<(ItemData Item, int Count)> ItemRowsList()
    {
        if (_db is not { } db) return [];
        var list = new List<(ItemData Item, int Count)>();
        foreach (var (id, count) in _inventory)
            if (count > 0 && db.Items.GetValueOrDefault(id) is { IsConsumable: true } item) list.Add((item, count));
        return [.. list.OrderBy(p => p.Item.Id)];
    }

    private (int X, int Y, int H) ItemMenuRect()
    {
        var rows = ItemRowsList();
        int shown = Math.Clamp(rows.Count, 1, ItemRows);
        int h = shown * ItemRowH + 24;
        var (cx, cy) = RingCenter(_units[Math.Max(_turn, 0)]);
        int x = Math.Clamp(cx - ItemMenuW / 2, _camX + 8, _camX + ViewWidth - ItemMenuW - 8);
        int y = Math.Clamp(cy - h / 2, _camY + GridTop + 28, _camY + ViewHeight - h - 8);
        return (x, y, h);
    }

    /// <summary>목록이 열려 있으면 클릭을 처리하고 true. 밖을 누르면 닫는다.</summary>
    private bool OnItemMenuClick(int bx, int by)
    {
        if (!_itemMenu) return false;
        _itemMenu = false;
        if (!IsPlayerTurn) return true;

        var rows = ItemRowsList();
        var (x, y, h) = ItemMenuRect();
        int index = _itemTop + (by - y - 12) / ItemRowH;
        if (bx < x || bx >= x + ItemMenuW || by < y + 12 || index < 0 || index >= rows.Count)
        {
            CancelTargeting(refund: true);
            return true;
        }

        var (item, _) = rows[index];
        if (Work(item.UseWork) is not { } w) { Toast($"{_db?.T(item.NameId)} — 쓸 수 없습니다"); return true; }
        _targetWork = w.Id;
        _targetIsBasicAttack = false;
        _targetItem = item.Id;
        if (UseSelfCentredWork(w)) { ConsumeTargetItem(); Toast(_db?.T(item.NameId) ?? ""); return true; }
        Hint($"{_db?.T(item.NameId)} — 노란 칸 안의 대상을 클릭하세요 (우클릭·Esc 취소)");
        return true;
    }

    /// <summary>대상을 확정했을 때 — 개수를 하나 줄인다(0 이면 목록에서 사라진다).</summary>
    private void ConsumeTargetItem()
    {
        if (_targetItem == 0) return;
        if (_inventory.TryGetValue(_targetItem, out int count))
        {
            if (count <= 1) _inventory.Remove(_targetItem);
            else _inventory[_targetItem] = count - 1;
        }
        _targetItem = 0;
    }

    private void DrawItemMenu()
    {
        if (!_itemMenu || _turn < 0 || _db is not { } db) return;
        var rows = ItemRowsList();
        var (x, y, h) = ItemMenuRect();

        DarkenRect(x - 1, y - 21, ItemMenuW + 2, h + 22);
        DrawGameFrame(x, y, ItemMenuW, h, db.T(4) is { Length: > 0 } t ? t : "ITEM");

        if (rows.Count == 0)
        {
            string none = db.T(0) is { Length: > 0 } n ? n : "없음";
            var (_, nw, _) = GetText(none, DimGray);
            DrawText(none, x + (ItemMenuW - nw) / 2, y + 16, DimGray);
            return;
        }

        for (int r = 0; r < ItemRows; r++)
        {
            int index = _itemTop + r;
            if (index >= rows.Count) break;
            var (item, count) = rows[index];
            int rx = x + 12, ry = y + 12 + r * ItemRowH;
            DrawUi(ItemRowObs, ItemRowMotion, 0, rx - 4, ry, UiBlend.Alpha, loop: false);
            DrawUi(ItemIconObs, item.PictureMotion, 0, rx + 8, ry + 2, UiBlend.Alpha, loop: false);

            string name = db.T(item.NameId);
            var (_, nw, _) = GetText(name, White, 12);
            DrawText(name, rx + ItemRowW - 46 - nw, ry + 4, White, 12);
            string amount = $"(x{count})";
            var (_, aw, _) = GetText(amount, White, 12);
            DrawText(amount, rx + ItemRowW - 6 - aw, ry + 4, White, 12);
        }
    }
}
