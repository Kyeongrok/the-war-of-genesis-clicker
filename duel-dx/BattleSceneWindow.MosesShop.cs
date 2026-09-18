using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 모세스 상점 페이지(mo-1, 페이지 3·4) — 아이템 상점과 VT(무기) 상점.
/// </summary>
/// <remarks>
/// 옵시디안 분석-모세스 12절 그대로. <c>Shp\%04d.shp</c>(44바이트, 워드): 1 상점 이름 TXR · 2 종류(2 장비 상점) ·
/// 3 점주 Obs · 4 점주 이름 TXR · 5 가격률 % · 6~20 파는 아이템 15칸. 챕터 머리 워드 3·4 가 그 챕터의 기본 아이템·VT 상점이다
/// (Chp 0010 = 4·3). 장소 값 20000+n 은 상점 n 을 연다.
/// 배경은 Bgr 0040(「Shop / Buy / Buy List / Account / Sell / Sell List / Reset / Set / Ok」 글자가 그림에 인쇄돼 있다).
/// 창 배치: 점주 그림 (52,101) · 상점 재고 (151,71) 217×21 네 줄 · 매입 목록 (422,71) 158×21 · 내 소지품 (151,191) · 매각 목록 (422,191) ·
/// 취소 (418,294) 68×27 · 결제 (522,294) · 나가기 (455,430) 163×27.
/// 줄 하나 = 아이콘 Obs 0326(모션 = 아이템 그림 번호) + 줄 틀 Obs 1291 모션 1(재고·소지품)·2(매입·매각) + 이름 + <c>"%d X %02d"</c>.
/// 값 = <c>아이템 가격 × 가격률 / 100</c>, <b>매각가는 그 절반</b>. 결산 = GP − 매입 + 매각, 음수면 TXR 1318 알림 + Snd 574, 되면 Snd 575.
/// 데모에는 파티 소지금이 없어 <b>5000GP 로 시작</b>한다(데모 나름, 저장 파일에 함께 담는다).
/// 목록 스크롤은 원본 스크롤 막대 대신 목록 오른쪽의 작은 화살표(Obs 0071 모션 2·4)로 대신했다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int ShopRowH = 21, ShopRows = 4, ShopIconObs = 326, ShopRowObs = 1291;
    private const int ShopButtonObs = 283, ShopExitObs = 287, ShopArrowObs = 71;
    private const int SoundShopFail = 574, SoundShopDone = 575, SoundShopLeave = 576;

    /// <summary>목록 넷 — 자리와 너비(분석-모세스 12절).</summary>
    private static readonly (int X, int Y, int W)[] ShopLists =
        [(151, 71, 217), (422, 71, 158), (151, 191, 217), (422, 191, 158)];

    private const int ListStock = 0, ListBuy = 1, ListBag = 2, ListSell = 3;

    private MosesShopFile? _shop;
    private readonly List<int> _shopBuy = [], _shopSell = [];
    private readonly int[] _shopTop = new int[4];
    private int _shopMoney = 5000;

    /// <summary>상점 페이지를 연다 — 번호가 없으면 챕터의 기본 상점(아이템 0 · VT 1).</summary>
    private void OpenMosesShop(int kind, int shopNo = -1)
    {
        int no = shopNo >= 0 ? shopNo : kind == 1 ? _mosesChp?.VtShop ?? 3 : _mosesChp?.ItemShop ?? 4;
        string path = Path.Combine(AssetsFolder.Find("moses"), "shp", $"{no:D4}.shp");
        if (!File.Exists(path) || MosesShopFile.Parse(File.ReadAllBytes(path)) is not { } shop)
        {
            Toast($"상점 {no} 자료가 없습니다");
            return;
        }
        _shop = shop;
        _shopBuy.Clear();
        _shopSell.Clear();
        Array.Clear(_shopTop);
        _mosesPage = kind == 1 ? 4 : 3;
        _mosesPageAt = _lastTime;
        StartFade();
        _mosesHover = -1;
        Play(572);
        ShowMosesBackground(40);
    }

    private int ShopPrice(int itemId) => _db?.Items.GetValueOrDefault(itemId) is { } item && _shop != null
        ? (int)(item.Price * _shop.Rate / 100) : 0;

    private int ShopSellPrice(int itemId) => ShopPrice(itemId) / 2;

    /// <summary>결산 = 지금 돈 − 매입 + 매각.</summary>
    private int ShopBalance() => _shopMoney - _shopBuy.Sum(ShopPrice) + _shopSell.Sum(ShopSellPrice);

    /// <summary>목록 넷의 지금 내용 — (아이템 번호, 개수).</summary>
    private List<(int Item, int Count)> ShopListItems(int list) => list switch
    {
        ListStock => [.. (_shop?.Items ?? []).Select(i => (i, 1))],
        ListBuy => [.. _shopBuy.GroupBy(i => i).Select(g => (g.Key, g.Count()))],
        ListBag => [.. _inventory.Where(p => p.Value > 0).Select(p => (p.Key, p.Value))],
        _ => [.. _shopSell.GroupBy(i => i).Select(g => (g.Key, g.Count()))],
    };

    private (int List, int Row) ShopRowAt(int bx, int by)
    {
        var (ox, oy) = MosesOrigin();
        for (int i = 0; i < ShopLists.Length; i++)
        {
            var (x, y, w) = ShopLists[i];
            if (bx < ox + x || bx >= ox + x + w || by < oy + y || by >= oy + y + ShopRows * ShopRowH) continue;
            return (i, _shopTop[i] + (by - oy - y) / ShopRowH);
        }
        return (-1, -1);
    }

    /// <summary>상점 페이지가 열려 있으면 클릭을 처리하고 true.</summary>
    private bool OnMosesShopClick(int bx, int by)
    {
        if (_mosesPage is not (3 or 4) || _shop == null) return false;
        var (ox, oy) = MosesOrigin();
        int x = bx - ox, y = by - oy;

        if (Hit(455, 430, 163, 27)) { Play(SoundShopLeave); MosesGoBack(); return true; }          // 나가기
        if (Hit(418, 294, 68, 27)) { _shopBuy.Clear(); _shopSell.Clear(); return true; }           // 취소(Reset)
        if (Hit(522, 294, 68, 27)) { ShopSettle(); return true; }                                  // 결제(Set)

        // 목록 오른쪽 화살표 — 위·아래로 한 줄씩
        for (int i = 0; i < ShopLists.Length; i++)
        {
            var (lx, ly, lw) = ShopLists[i];
            if (x < lx + lw || x >= lx + lw + 16 || y < ly || y >= ly + ShopRows * ShopRowH) continue;
            int max = Math.Max(0, ShopListItems(i).Count - ShopRows);
            _shopTop[i] = y < ly + ShopRows * ShopRowH / 2 ? Math.Max(0, _shopTop[i] - 1) : Math.Min(max, _shopTop[i] + 1);
            return true;
        }

        var (list, row) = ShopRowAt(bx, by);
        if (list < 0) return true;
        var items = ShopListItems(list);
        if (row < 0 || row >= items.Count) return true;
        int itemId = items[row].Item;
        switch (list)
        {
            case ListStock: _shopBuy.Add(itemId); break;                        // 사려고 담기
            case ListBuy: _shopBuy.Remove(itemId); break;                       // 담은 것 빼기
            case ListBag when _shopSell.Count(i => i == itemId) < items[row].Count: _shopSell.Add(itemId); break;
            case ListSell: _shopSell.Remove(itemId); break;
        }
        return true;

        bool Hit(int rx, int ry, int rw, int rh) => x >= rx && x < rx + rw && y >= ry && y < ry + rh;
    }

    /// <summary>결제 — 결산이 음수면 안 되고, 되면 가방과 소지금을 고친다.</summary>
    private void ShopSettle()
    {
        if (_shopBuy.Count == 0 && _shopSell.Count == 0) return;
        if (ShopBalance() < 0)
        {
            Play(SoundShopFail);
            _notice = (_db?.T(1318) is { Length: > 0 } t ? t : "돈이 모자랍니다.", _lastTime + 150 / TicksPerSecond);
            _shopBuy.Clear();
            _shopSell.Clear();
            return;
        }
        _shopMoney = ShopBalance();
        foreach (int id in _shopBuy) _inventory[id] = _inventory.GetValueOrDefault(id) + 1;
        foreach (int id in _shopSell)
            if (_inventory.TryGetValue(id, out int n)) _inventory[id] = Math.Max(0, n - 1);
        _shopBuy.Clear();
        _shopSell.Clear();
        Play(SoundShopDone);
    }

    private void DrawMosesShop(int ox, int oy, int tick)
    {
        if (_shop is not { } shop) return;

        DrawUi(shop.OwnerObs, 0, tick, ox + 52, oy + 101, UiBlend.Alpha);   // 점주

        for (int i = 0; i < ShopLists.Length; i++)
        {
            var (lx, ly, lw) = ShopLists[i];
            var items = ShopListItems(i);
            for (int r = 0; r < ShopRows; r++)
            {
                int index = _shopTop[i] + r;
                if (index >= items.Count) break;
                var (itemId, count) = items[index];
                int rx = ox + lx, ry = oy + ly + r * ShopRowH;
                DrawUi(ShopRowObs, i is ListStock or ListBag ? 1 : 2, tick, rx - 8, ry - 5, UiBlend.Alpha);
                if (_db?.Items.GetValueOrDefault(itemId) is not { } item) continue;
                DrawUi(ShopIconObs, item.PictureMotion, tick, rx - 1, ry, UiBlend.Alpha);
                DrawText(_db.T(item.NameId), rx + 20, ry + 4, White, 11);
                string value = $"{(i is ListBag or ListSell ? ShopSellPrice(itemId) : ShopPrice(itemId))} X {count:D2}";
                var (_, vw, _) = GetText(value, White, 11);
                DrawText(value, rx + lw - 2 - vw, ry + 4, White, 11);
            }
            // 줄이 넘치면 오른쪽에 위·아래 화살표
            if (items.Count > ShopRows)
            {
                DrawUi(ShopArrowObs, 2, tick, ox + lx + lw, oy + ly, UiBlend.Alpha);
                DrawUi(ShopArrowObs, 4, tick, ox + lx + lw, oy + ly + ShopRows * ShopRowH - 16, UiBlend.Alpha);
            }
        }

        // 금액 넉 줄 — TXR 666 현재금액 · 667 매입금액 · 668 매각금액 · 669 결산
        var lines = new (ushort Text, int Value)[]
        {
            (666, _shopMoney), (667, _shopBuy.Sum(ShopPrice)), (668, _shopSell.Sum(ShopSellPrice)), (669, ShopBalance()),
        };
        for (int i = 0; i < lines.Length; i++)
        {
            string label = _db?.T(lines[i].Text) is { Length: > 0 } t ? t : "";
            // 배경 그림의 「Account」 칸 안(왼쪽 아래)에 넣는다 — 이름은 왼쪽, 값은 오른쪽 맞춤
            int ly = oy + 200 + i * 20;
            DrawText(label, ox + 30, ly, White, 11);
            string value = $"{lines[i].Value}GP";
            var (_, vw, _) = GetText(value, White, 11);
            DrawText(value, ox + 132 - vw, ly, lines[i].Value < 0 ? Red : White, 11);
        }

        DrawUi(ShopButtonObs, 0, tick, ox + 418, oy + 294, UiBlend.Alpha);
        DrawUi(ShopButtonObs, 0, tick, ox + 522, oy + 294, UiBlend.Alpha);
        DrawUi(ShopExitObs, 0, tick, ox + 455, oy + 430, UiBlend.Alpha);
    }
}

/// <summary><c>Shp\NNNN.shp</c>(44바이트) — 상점 하나.</summary>
internal sealed record MosesShopFile(int NameText, int Kind, int OwnerObs, int OwnerNameText, int Rate, int[] Items)
{
    public static MosesShopFile? Parse(byte[] b)
    {
        if (b.Length < 42) return null;
        short H(int o) => BitConverter.ToInt16(b, o);
        int rate = H(10) is > 0 and < 1000 ? H(10) : 100;
        return new MosesShopFile(H(2), H(4), H(6), H(8), rate,
                                 [.. Enumerable.Range(0, 15).Select(i => (int)H(12 + 2 * i)).Where(v => v > 0)]);
    }
}
