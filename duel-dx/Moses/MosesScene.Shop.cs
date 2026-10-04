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
    internal sealed unsafe partial class MosesScene
    {
    internal const int ShopRowH = 21, ShopRows = 4, ShopIconObs = 326, ShopRowObs = 1291;
    internal const int ShopButtonObs = 283, ShopExitObs = 287, ShopArrowObs = 71;
    internal const int SoundShopFail = 574, SoundShopDone = 575, SoundShopLeave = 576;

    /// <summary>목록 넷 — 자리와 너비(분석-모세스 12절).</summary>
    internal static readonly (int X, int Y, int W)[] ShopLists =
        [(151, 71, 217), (422, 71, 158), (151, 191, 217), (422, 191, 158)];

    internal const int ListStock = 0, ListBuy = 1, ListBag = 2, ListSell = 3;

    internal MosesShopFile? _shop;
    internal readonly List<int> _shopBuy = [], _shopSell = [];
    internal readonly int[] _shopTop = new int[4];
    internal int _shopMoney = 5000;

    /// <summary>상점 페이지를 연다 — 번호가 없으면 챕터의 기본 상점(아이템 0 · VT 1).</summary>
    /// <param name="quiet">장소(값 ≥ 20000)로 열 때 — 원본은 페이지 3 + <c>0x100fb1f0(0, n)</c> 뿐, 소리·효과가 없다(<c>0x100feff3</c>, 감사5 P2).</param>
    internal void OpenMosesShop(int kind, int shopNo = -1, bool quiet = false)
    {
        int no = shopNo >= 0 ? shopNo : kind == 1 ? _mosesChp?.VtShop ?? 3 : _mosesChp?.ItemShop ?? 4;
        string path = Path.Combine(AssetsFolder.Find("moses"), "shp", $"{no:D4}.shp");
        if (!File.Exists(path) || MosesShopFile.Parse(File.ReadAllBytes(path)) is not { } shop)
        {
            host.Toast($"상점 {no} 자료가 없습니다");
            return;
        }
        _shop = shop;
        _shopBuy.Clear();
        _shopSell.Clear();
        Array.Clear(_shopTop);
        _shopCompareItem = 0;
        _shopCompareTop = 0;
        _mosesPage = kind == 1 ? 4 : 3;
        _mosesPageAt = host._lastTime;
        _mosesHover = -1;
        if (quiet) { _mosesFade = 0; ResetMosesSlide(0); }
        else
        {
            StartFade();
            host.Play(572);
        }
        ShowMosesBackground(40);
    }

    internal int ShopPrice(int itemId) => host._db?.Items.GetValueOrDefault(itemId) is { } item && _shop != null
        ? (int)(item.Price * _shop.Rate / 100) : 0;

    internal int ShopSellPrice(int itemId) => ShopPrice(itemId) / 2;

    /// <summary>결산 = 지금 돈 − 매입 + 매각.</summary>
    internal int ShopBalance() => _shopMoney - _shopBuy.Sum(ShopPrice) + _shopSell.Sum(ShopSellPrice);

    /// <summary>목록 넷의 지금 내용 — (아이템 번호, 개수).</summary>
    internal List<(int Item, int Count)> ShopListItems(int list) => list switch
    {
        ListStock => [.. (_shop?.Items ?? []).Select(i => (i, 1))],
        ListBuy => [.. _shopBuy.GroupBy(i => i).Select(g => (g.Key, g.Count()))],
        ListBag => [.. host._inventory.Where(p => p.Value > 0).Select(p => (p.Key, p.Value))],
        _ => [.. _shopSell.GroupBy(i => i).Select(g => (g.Key, g.Count()))],
    };

    /// <summary>상점 페이지에서 마우스가 놓인 목록을 휠로 굴린다(편의 — 원본은 화살표만, ba-20 G22). 받았으면 true.</summary>
    internal bool OnMosesShopWheel(int notches)
    {
        if (!_mosesOpen || _mosesPage != 3 || host.SystemOpen) return false;
        int list = ShopRowAt(host._mouse.X, host._mouse.Y).List;
        if (list < 0) return false;
        _shopTop[list] = Math.Clamp(_shopTop[list] - notches, 0, Math.Max(0, ShopListItems(list).Count - ShopRows));
        return true;
    }

    internal (int List, int Row) ShopRowAt(int bx, int by)
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
    /// <summary>
    /// 장비 상점(Shp+4 == 2)의 캐릭터 비교창 0x10103080(ba-20 G7) — 화면 (48,330) 에 인물 칸 다섯(Obs 302 모션 i+4, 올리면 i+9, 얼굴 +(32,50)).
    /// 재고 줄을 눌러 <b>담을 때만</b> 비교 글이 바뀐다(0x100ff660 → 0x10103640): 무기는 무기 종류가 같은 인물만 지금 무기와, 갑옷(종류 2)은 지금 갑옷과.
    /// 여섯 명이 넘으면 ←/→ (Obs 302 모션 0/1 @ (20,370)/(400,370), 20×50)로 한 명씩 넘긴다.
    /// </summary>
    internal int _shopCompareItem, _shopCompareTop;

    internal bool ShopCompares => _shop is { Kind: 2 };

    internal void DrawShopCompare(int ox, int oy, int tick)
    {
        if (!ShopCompares || host._db is not { } db) return;
        var party = StyleParty();
        _shopCompareTop = Math.Clamp(_shopCompareTop, 0, Math.Max(0, party.Count - 5));
        int mx = host._mouse.X - ox, my = host._mouse.Y - oy;
        var item = _shopCompareItem != 0 ? db.Items.GetValueOrDefault(_shopCompareItem) : null;
        for (int i = 0; i < 5 && _shopCompareTop + i < party.Count; i++)
        {
            int cx = ox + 70 * i + 48, cy = oy + 330;
            bool over = mx >= 70 * i + 48 && mx < 70 * i + 48 + 64 && my >= 330 && my < 460;
            host.DrawUi(302, over ? i + 9 : i + 4, tick, cx, cy, UiBlend.Alpha);
            if (PartyData(party[_shopCompareTop + i]) is not { } pc) continue;
            host.Fld.LoadFieldFace(pc);
            if (host._faces.TryGetValue(pc.Code, out var face)) host.BlitScaled(face, cx + 2, cy + 20, 60, 60);
            if (item == null) continue;
            // 종류 0·1·8~17 = 무기(인물의 무기 종류와 같을 때만), 2 = 갑옷, 그 밖은 글 없음(표 0x10103758).
            int slot = item.Type is 0 or 1 or (>= 8 and <= 17) ? (db.WeaponTypeOf(pc) == item.Type ? 0 : -1) : item.Type == 2 ? 1 : -1;
            if (slot < 0) continue;
            var worn = pc.Items.Length > slot && pc.Items[slot] != 0 ? db.Items.GetValueOrDefault(pc.Items[slot]) : null;
            string[] lines = [$"Atk = {item.Attack - (worn?.Attack ?? 0),3:+0;-0;+0}", $"Dep = {item.Defense - (worn?.Defense ?? 0),3:+0;-0;+0}"];
            for (int l = 0; l < lines.Length; l++)
            {
                var (_, tw, _) = host.GetText(lines[l], 0xFFFFFF00, 11);
                host.DrawText(lines[l], cx + 32 - tw / 2, cy + 65 + 35 - 12 + l * 13, 0xFFFFFF00, 11);
            }
        }
        if (party.Count >= 6)
        {
            host.DrawUi(302, 0, 0, ox + 20, oy + 370, UiBlend.Alpha);
            host.DrawUi(302, 1, 0, ox + 400, oy + 370, UiBlend.Alpha);
        }
    }

    internal bool OnShopCompareClick(int x, int y)
    {
        if (!ShopCompares) return false;
        int count = StyleParty().Count;
        if (count >= 6 && y >= 370 && y < 420)
        {
            if (x >= 20 && x < 40) { if (_shopCompareTop > 0) { _shopCompareTop--; host.Play(66); } return true; }
            if (x >= 400 && x < 420) { if (_shopCompareTop < count - 5) { _shopCompareTop++; host.Play(66); } return true; }
        }
        return false;
    }

    internal bool OnMosesShopClick(int bx, int by)
    {
        if (_mosesPage is not (3 or 4) || _shop == null) return false;
        var (ox, oy) = MosesOrigin();
        int x = bx - ox, y = by - oy;

        if (Hit(455, 430, 163, 27)) { MosesGoBack(); return true; }          // 나가기 — Snd 576 은 뒤로 처리기의 상점 갈래가 낸다(0x10101d26)
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
        if (list < 0) { OnShopCompareClick(x, y); return true; }
        var items = ShopListItems(list);
        if (row < 0 || row >= items.Count) return true;
        int itemId = items[row].Item;
        switch (list)
        {
            // 사려고 담기 — 한 칸 99개(0x100ff63e), 합계 5천만 GP(0x100ff677)까지.
            // 99 는 <b>담은 수</b>만 센다(보유 수는 안 더한다, ba-20 G8).
            case ListStock when _shopBuy.Count(i => i == itemId) < 99
                                && _shopBuy.Sum(ShopPrice) + ShopPrice(itemId) <= 50_000_000:
                _shopBuy.Add(itemId);
                _shopCompareItem = itemId;   // 비교 글은 담을 때만 바뀐다(0x100ff660)
                break;
            case ListBuy: _shopBuy.Remove(itemId); break;                       // 담은 것 빼기
            // 가격 0 인 아이템은 팔 수 없다(줄이 꺼진다, 0x100f89e0).
            // 매각 합계도 5천만 GP 까지다(0x100ff66d~0x100ff691).
            case ListBag when ShopSellPrice(itemId) > 0 && _shopSell.Count(i => i == itemId) < items[row].Count
                              && _shopSell.Sum(ShopSellPrice) + ShopSellPrice(itemId) <= 50_000_000: _shopSell.Add(itemId); break;
            case ListSell: _shopSell.Remove(itemId); break;
        }
        return true;

        bool Hit(int rx, int ry, int rw, int rh) => x >= rx && x < rx + rw && y >= ry && y < ry + rh;
    }

    /// <summary>결제 — 결산이 음수면 안 되고, 되면 가방과 소지금을 고친다.</summary>
    internal void ShopSettle()
    {
        if (_shopBuy.Count == 0 && _shopSell.Count == 0) return;
        if (ShopBalance() < 0)
        {
            host.Play(SoundShopFail);
            host._notice = (host._db?.T(1318) is { Length: > 0 } t ? t : "돈이 모자랍니다.", host._lastTime + 150 / TicksPerSecond);
            return;                              // 목록은 그대로 둔다(0x101001f6 — 알림과 소리만, ba-15)
        }
        _shopMoney = ShopBalance();
        foreach (int id in _shopBuy) host._inventory[id] = host._inventory.GetValueOrDefault(id) + 1;
        foreach (int id in _shopSell)
            if (host._inventory.TryGetValue(id, out int n)) host._inventory[id] = Math.Max(0, n - 1);
        _shopBuy.Clear();
        _shopSell.Clear();
        host.Play(SoundShopDone);
    }

    internal void DrawMosesShop(int ox, int oy, int tick)
    {
        if (_shop is not { } shop) return;

        host.DrawUi(shop.OwnerObs, 0, tick, ox + 52, oy + 101, UiBlend.Alpha);   // 점주
        int mx = host._mouse.X - ox, my = host._mouse.Y - oy;
        bool Over(int x, int y, int w, int h) => mx >= x && mx < x + w && my >= y && my < y + h;

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
                // 줄 틀 Obs 1291 모션 1(230×28)은 <b>마우스가 올라간 줄에만</b> 덧그리는 강조다(0x10043810 으로 달아 둔 덧그림,
                // 세이브 슬롯의 Obs 0471 모션 20 과 같은 짜임). 줄마다 그리면 28픽셀짜리 틀이 21픽셀 줄을 넘어 겹쳐 어지럽다.
                if (Over(lx, ly + r * ShopRowH, lw, ShopRowH)) host.DrawUi(ShopRowObs, 1, tick, rx - 8, ry - 5, UiBlend.Alpha);
                if (host._db?.Items.GetValueOrDefault(itemId) is not { } item) continue;
                host.DrawUi(ShopIconObs, item.PictureMotion, tick, rx - 1, ry, UiBlend.Alpha);
                // 줄 글(0x100f8900~ · 0x100f8f10~, ba-20 S 5): 재고 줄의 수 = 담은 수(처음 X 00), 소지품 줄의 수 = 보유 − 팔려고 담은 수,
                // 매입·매각 목록은 「이름(X nn)」이고 값이 없다. 값 글은 노랑. 가격 0 인 소지품은 「판매 불가」로 꺼진다(0x1003fdc0).
                bool unsellable = i == ListBag && ShopSellPrice(itemId) <= 0;
                int shown = i == ListStock ? _shopBuy.Count(b => b == itemId) : i == ListBag ? count - _shopSell.Count(b => b == itemId) : count;
                string label = i is ListBuy or ListSell ? $"{host._db.T(item.NameId)}(X {shown:D2})" : host._db.T(item.NameId);
                host.DrawText(label, rx + 20, ry + 4, unsellable ? DimGray : White, 11);
                if (i is ListStock or ListBag)
                {
                    string value = unsellable ? "판매 불가" : $"{(i == ListBag ? ShopSellPrice(itemId) : ShopPrice(itemId))} X {shown:D2}";
                    var (_, vw, _) = host.GetText(value, White, 11);
                    host.DrawText(value, rx + lw - 5 - vw, ry + 4, unsellable ? DimGray : 0xFFFFFF00, 11);
                }
            }
            // 줄이 넘치면 오른쪽에 위·아래 화살표
            if (items.Count > ShopRows)
            {
                host.DrawUi(ShopArrowObs, 2, tick, ox + lx + lw, oy + ly, UiBlend.Alpha);
                host.DrawUi(ShopArrowObs, 4, tick, ox + lx + lw, oy + ly + ShopRows * ShopRowH - 16, UiBlend.Alpha);
            }
        }

        // 금액 넉 줄 — TXR 666 현재금액 · 667 매입금액 · 668 매각금액 · 669 결산
        var lines = new (ushort Text, int Value)[]
        {
            (666, _shopMoney), (667, _shopBuy.Sum(ShopPrice)), (668, _shopSell.Sum(ShopSellPrice)), (669, ShopBalance()),
        };
        for (int i = 0; i < lines.Length; i++)
        {
            string label = host._db?.T(lines[i].Text) is { Length: > 0 } t ? t : "";
            // 배경 그림의 「Account」 칸 안(왼쪽 아래)에 넣는다 — 이름은 왼쪽, 값은 오른쪽 맞춤
            int ly = oy + 200 + i * 20;
            host.DrawText(label, ox + 30, ly, White, 11);
            string value = $"{lines[i].Value}GP";
            var (_, vw, _) = host.GetText(value, White, 11);
            host.DrawText(value, ox + 132 - vw, ly, lines[i].Value < 0 ? Red : White, 11);
        }

        DrawShopCompare(ox, oy, tick);

        // Reset·Set·Ok 글자는 배경 그림(Bgr 0040)에 있다 — 알약 Obs 283·287 은 마우스가 올라갔을 때만 덧그리는 보조 그림.
        if (Over(418, 294, 68, 27)) host.DrawUi(ShopButtonObs, 0, tick, ox + 418, oy + 294, UiBlend.Alpha);
        if (Over(522, 294, 68, 27)) host.DrawUi(ShopButtonObs, 0, tick, ox + 522, oy + 294, UiBlend.Alpha);
        if (Over(455, 430, 163, 27)) host.DrawUi(ShopExitObs, 0, tick, ox + 455, oy + 430, UiBlend.Alpha);
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
}
