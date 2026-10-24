using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 마을 시설 창 — 상점(1000~1003, 원본 <c>0x100c8730</c>) · 용병 길드(1100~1103, <c>0x100c9090</c>) · 주점(1200~1203, <c>0x100ca810</c>).
/// </summary>
/// <remarks>
/// <para>옵시디안 「분석-파트1-챕터」 8.1. 세 창이 같은 틀이다: 왼위 제목 꼬리표 (7, 7) 220×33(머리 인자 0 의 줄), 오른쪽 주인 초상(머리 인자의 Obs, 장 0 을 (320, 240) 기준),
/// 밑 대사 창 (0, 360) 640×120(인사 줄) 과 그 안 EXIT 단추 (577, 457) 60×15.</para>
/// <para>상점: 목록 (20, 52) — 「구 입」·「판 매」 꼬리 단추, 줄마다 아이콘 · 이름 · 「값 Eld (가진 수)」 · ▲ 수 ▼(Obs 678 벌 3), 7줄. 금액 상자 (100, 275) 233×80 —
/// 소지금액 · 물품총액 · 잔액과 「결 정」. 사는 값 = 아이템 값 × 머리 인자 5(%) / 100, 파는 값 = 아이템 값 / 2. 살 수 있는 수 = 99 − 가진 수, 팔 수 = 가진 수.
/// ▲ 는 총액이 소지금을 넘으면 막는다(사기). 결정 = 가방에 넣고(빼고) 돈을 치르고(받고) 목록을 다시 세우고 감사 줄(인자 2). EXIT = 작별 줄(인자 3) 뒤 닫는다.</para>
/// <para>용병 길드: 1102 [Chr, 값, Chr, 값] 의 두 용병을 카드(Obs 678 벌 4 · 5)로 — 전투 그림 · 이름 · Hp/Str · Dex/Spd · Skill/Int · Ac/Mr, ▲ 수 ▼. 상한 99 − 가진 수.
/// 주점: 1202 [얼굴, 줄, 줄, 줄] 의 얼굴을 4×3 칸(61×70)에 — 누르면 그 소문 줄들.</para>
/// </remarks>
internal sealed class Part1TownWindow(GameWindow host, Part1TitleScreen shell, Func<int, string> line)
{
    internal bool _open;
    private Part1TownScreen.Facility _f = null!;
    private Action? _onClose;
    private bool _sell, _leaving;
    private int _scroll, _hover = -1;
    private string _speaker = "", _text = "";
    private readonly Queue<string> _rumors = [];
    private readonly List<Row> _rows = [];
    private IReadOnlyList<Part1Item>? _items;
    private string? _tip;

    /// <summary>목록 한 줄 — 아이템 번호(용병이면 Chr 코드) · 한 개 값 · 가진 수 · 상한 · 고른 수.</summary>
    private sealed class Row
    {
        public int Id, Unit, Owned, Max, Qty;
    }

    private Part1MapArt Art => shell.Chapter.Art;

    // 자리(640×480 기준) — 원본 창 생성 인자 그대로, 줄 높이 26 은 원본 화면에서 잰 것.
    internal const int ListX = 20, ListY = 52, ListW = 300, ListH = 216, RowX = 28, RowY = 80, RowH = 26, Rows = 7;
    internal const int SumX = 100, SumY = 275, SumW = 233, SumH = 80;
    internal const int TalkY = 360, ExitX = 577, ExitY = 457, SmallW = 60, SmallH = 15;
    internal const int ConfirmX = SumX + 164, ConfirmY = SumY + 57, TabBuyX = ListX + 10, TabSellX = ListX + 67, TabY = ListY + 8, TabW = 58;
    internal const int ScrollX = 298, ScrollH = Rows * RowH;
    internal const int CardX = ListX + 11, CardY = ListY + 37, CardStep = 90, PubX = 21, PubY = 52, PubW = 61, PubH = 70;

    private const int HitExit = 1, HitConfirm = 2, HitBuy = 3, HitSell = 4, HitUp = 100, HitDown = 200, HitRow = 300, HitPub = 400, HitScrollUp = 5, HitScrollDown = 6;

    private int HeadArg(int i) => i < _f.Head.Length ? _f.Head[i] : 0;
    private int Portrait => _f.Kind == Part1TownScreen.Kind.Pub ? HeadArg(3) : HeadArg(4);
    private int Rate => HeadArg(5) > 0 ? HeadArg(5) : 100;
    private int Total => _rows.Sum(r => r.Qty * r.Unit);
    private bool Buying => _f.Kind == Part1TownScreen.Kind.Guild || !_sell;

    internal void Open(Part1TownScreen.Facility facility, Action onClose)
    {
        _f = facility;
        _onClose = onClose;
        _sell = false;
        _leaving = false;
        _scroll = 0;
        _hover = -1;
        _rumors.Clear();
        _items = Part1Item.LoadAll();
        Rebuild();
        Say(HeadArg(1));
        _open = true;
    }

    private void Say(int lineNo) => SayRaw(line(lineNo));

    private void SayRaw(string raw)
    {
        string who = Part1Game.Speaker(raw).Trim();
        _speaker = who.Length > 0 ? $"[{who}]" : "";
        _text = Part1Game.PlainText(raw);
    }

    /// <summary>목록을 다시 세운다(고른 수는 0 으로) — 상점 구입(cab30) · 판매(caf90), 길드는 1102 의 둘.</summary>
    private void Rebuild()
    {
        _rows.Clear();
        _scroll = 0;
        var bag = host.Part1.Bag;
        switch (_f.Kind)
        {
            case Part1TownScreen.Kind.Shop when !_sell:
                foreach (var e in _f.Entries)
                {
                    if (Part1Item.ById(_items!, e[0]) is not { } item) continue;
                    int owned = bag.Count(id => id == item.Id);
                    _rows.Add(new Row { Id = item.Id, Unit = item.Price * Rate / 100, Owned = owned, Max = Math.Max(0, 99 - owned) });
                }
                break;
            case Part1TownScreen.Kind.Shop:
                foreach (int id in bag.Distinct())
                {
                    if (Part1Item.ById(_items!, id) is not { } item) continue;
                    int owned = bag.Count(x => x == id);
                    // 원본 줄 글은 값 × 비율 / 200 으로 찍지만 총액에 더하는 것은 값 / 2(0x100c64f0) — 받는 돈 쪽을 따른다.
                    _rows.Add(new Row { Id = id, Unit = item.Price / 2, Owned = owned, Max = owned });
                }
                break;
            case Part1TownScreen.Kind.Guild:
                foreach (var e in _f.Entries)
                    for (int k = 0; k + 1 < e.Length; k += 2)
                    {
                        if (e[k] <= 0) continue;
                        int code = Part1Game.ChrBase + e[k];
                        int owned = host.Part1.Mercenaries.GetValueOrDefault(code);
                        _rows.Add(new Row { Id = code, Unit = e[k + 1], Owned = owned, Max = Math.Max(0, 99 - owned) });
                    }
                break;
        }
    }

    private static bool In(int x, int y, int rx, int ry, int w, int h) => x >= rx && x < rx + w && y >= ry && y < ry + h;

    private int HitAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        int x = bx - ox, y = by - oy;
        if (In(x, y, ExitX, ExitY, SmallW, SmallH)) return HitExit;
        if (_leaving) return -1;
        switch (_f.Kind)
        {
            case Part1TownScreen.Kind.Pub:
                for (int i = 0; i < Math.Min(12, _f.Entries.Count); i++)
                    if (In(x, y, PubX + i % 4 * PubW + 5, PubY + i / 4 * PubH + 10, 50, 50)) return HitPub + i;
                return -1;
            case Part1TownScreen.Kind.Shop:
                if (In(x, y, TabBuyX, TabY, TabW, SmallH)) return HitBuy;
                if (In(x, y, TabSellX, TabY, TabW, SmallH)) return HitSell;
                if (Part1MapArt.ScrollHit(ScrollX, RowY, ScrollH, x, y) is not 0 and var s) return s < 0 ? HitScrollUp : HitScrollDown;
                for (int v = 0; v < Rows && _scroll + v < _rows.Count; v++)
                {
                    int ry = RowY + v * RowH;
                    if (In(x, y, RowX + 226, ry + 5, 12, 16)) return HitUp + _scroll + v;
                    if (In(x, y, RowX + 255, ry + 5, 12, 16)) return HitDown + _scroll + v;
                    if (In(x, y, RowX, ry, 222, RowH - 1)) return HitRow + _scroll + v;
                }
                break;
            case Part1TownScreen.Kind.Guild:
                for (int i = 0; i < Math.Min(2, _rows.Count); i++)
                {
                    int cy = CardY + i * CardStep;
                    if (In(x, y, CardX + 238, cy + 4, 12, 16)) return HitUp + i;
                    if (In(x, y, CardX + 268, cy + 4, 12, 16)) return HitDown + i;
                }
                break;
        }
        if (In(x, y, ConfirmX, ConfirmY, SmallW, SmallH)) return HitConfirm;
        return -1;
    }

    internal void UpdateHover(int bx, int by) => _hover = HitAt(bx, by);

    internal void OnClick(int bx, int by)
    {
        if (_leaving) { Close(); return; }
        if (_rumors.Count > 0) { SayRaw(_rumors.Dequeue()); return; }
        int hit = HitAt(bx, by);
        switch (hit)
        {
            case HitExit: Leave(); return;
            case HitConfirm: Confirm(); return;
            case HitBuy or HitSell:
                if ((hit == HitSell) != _sell) { _sell = hit == HitSell; Rebuild(); }
                return;
            case HitScrollUp: _scroll = Math.Max(0, _scroll - 1); return;
            case HitScrollDown: _scroll = Math.Clamp(_scroll + 1, 0, Math.Max(0, _rows.Count - Rows)); return;
        }
        if (hit >= HitPub) { Rumor(hit - HitPub); return; }
        if (hit >= HitRow) return;
        if (hit >= HitDown) { Step(hit - HitDown, -1); return; }
        if (hit >= HitUp) Step(hit - HitUp, +1);
    }

    internal void OnKey(int key)
    {
        if (_leaving) { if (key is Win32.VK_RETURN or Win32.VK_SPACE or Win32.VK_ESCAPE) Close(); return; }
        if (_rumors.Count > 0) { if (key is Win32.VK_RETURN or Win32.VK_SPACE) SayRaw(_rumors.Dequeue()); else if (key == Win32.VK_ESCAPE) _rumors.Clear(); return; }
        if (key == Win32.VK_ESCAPE) Leave();
    }

    /// <summary>▲(+1) · ▼(−1) — 사기·고용은 총액이 소지금을 넘지 않을 때만, 팔기는 가진 수까지. 소리 0x34.</summary>
    private void Step(int i, int d)
    {
        if (i < 0 || i >= _rows.Count) return;
        var r = _rows[i];
        if (d > 0)
        {
            if (r.Qty >= r.Max) return;
            if (Buying && Total + r.Unit > host.Part1.Eld) return;
            r.Qty++;
        }
        else
        {
            if (r.Qty <= 0) return;
            r.Qty--;
        }
        host.Play(Part1Game.ObsBase + 0x34);
    }

    /// <summary>「결 정」 — 총액이 0 이면 아무 일 없음. 사기(cb540) · 팔기(cb5d0) · 고용(cb7b0) 뒤 목록을 다시 세우고 감사 줄(인자 2).</summary>
    private void Confirm()
    {
        int total = Total;
        if (total == 0) return;
        var p = host.Part1;
        foreach (var r in _rows.Where(r => r.Qty > 0))
        {
            if (_f.Kind == Part1TownScreen.Kind.Guild) p.Mercenaries[r.Id] = p.Mercenaries.GetValueOrDefault(r.Id) + r.Qty;
            else if (_sell) for (int k = 0; k < r.Qty; k++) p.Bag.Remove(r.Id);
            else for (int k = 0; k < r.Qty; k++) p.Bag.Add(r.Id);
        }
        p.Eld += Buying ? -total : total;
        shell.Chapter.SaveProgress();
        Rebuild();
        Say(HeadArg(2));
    }

    private void Rumor(int i)
    {
        if (i >= _f.Entries.Count) return;
        var e = _f.Entries[i];
        _rumors.Clear();
        string? last = null;
        for (int k = 1; k < e.Length && k <= 3; k++)
        {
            if (e[k] <= 0 && k > 1) continue;
            string raw = line(e[k]);
            if (raw.Length == 0 || raw == last) continue;
            _rumors.Enqueue(last = raw);
        }
        if (_rumors.Count > 0) SayRaw(_rumors.Dequeue());
    }

    /// <summary>EXIT — 작별 줄(상점 · 길드 인자 3, 주점 인자 2)을 띄우고, 누르면 닫는다(1003 이 창 상태 4 를 기다리는 것).</summary>
    private void Leave()
    {
        _leaving = true;
        _rumors.Clear();
        Say(_f.Kind == Part1TownScreen.Kind.Pub ? HeadArg(2) : HeadArg(3));
    }

    private void Close()
    {
        _open = false;
        _onClose?.Invoke();
    }

    // ── 그리기 ──

    internal void Draw(int ox, int oy)
    {
        _tip = null;
        var art = Art;
        art.Clip = (ox, oy, 640, 480);
        DrawPortrait(ox, oy);

        art.Box(ox + 7, oy + 7, 220, 33);
        art.TextCentered(Part1Game.PlainText(line(HeadArg(0))), ox + 117, oy + 24, GameWindow.White, 14);

        switch (_f.Kind)
        {
            case Part1TownScreen.Kind.Shop: DrawShop(art, ox, oy); DrawSums(art, ox, oy); break;
            case Part1TownScreen.Kind.Guild: DrawGuild(art, ox, oy); DrawSums(art, ox, oy); break;
            default: DrawPub(art, ox, oy); break;
        }

        shell.DrawTalkBox(ox, oy + TalkY, 640, 120, _speaker, _text, arrow: false);
        SmallButton(ox + ExitX, oy + ExitY, "EXIT", _hover == HitExit);
        if (_tip is { Length: > 0 } tip) host.StatusScr.DrawDescriptionTip(tip, host._mouse.X, host._mouse.Y, ox, oy, 640, 480);
    }

    /// <summary>주인 초상 — Obs 장 0(150×120 · 150×240 네 쪽)을 (320, 240) 기준에. 원본 대사 창이 얼굴로 그린다(0x100a9084).</summary>
    private void DrawPortrait(int ox, int oy)
    {
        if (Portrait <= 0 || host.UiFor(Part1Game.ObsBase + Portrait) is not { } sprite) return;
        foreach (var ((sub, _), frame) in sprite._frames)
            if (sub == 0) Art.Blit(frame, ox + 320, oy + 240);
    }

    private void SmallButton(int x, int y, string label, bool hot, bool on = false)
    {
        host.UiFill(x, y, SmallW, SmallH, on ? 0xF0485890u : hot ? 0xF0384878u : 0xE0181C40u);
        host.UiStroke(x, y, SmallW, SmallH, on || hot ? 0xFFE8ECF8u : 0xFF8890B8u);
        Art.TextCentered(label, x + SmallW / 2, y + SmallH / 2, on ? 0xFFFFE080u : GameWindow.White, 11);
    }

    private void Arrows(Part1MapArt art, int x, int y, int upHit, int downHit, int qty)
    {
        art.Blit(art.Frame(678, 3, _hover == upHit ? 1 : 0), x, y);
        art.Blit(art.Frame(678, 3, 6), x + 12, y);
        art.Blit(art.Frame(678, 3, _hover == downHit ? 4 : 3), x + 29, y);
        art.TextRight($"{qty}", x + 27, y + 1, 0xFFFFFF00, 11);
    }

    private void DrawShop(Part1MapArt art, int ox, int oy)
    {
        art.Box(ox + ListX, oy + ListY, ListW, ListH);
        int tabTab = _sell ? HitSell : HitBuy;
        SmallTab(ox + TabBuyX, oy + TabY, "구  입", tabTab == HitBuy, _hover == HitBuy);
        SmallTab(ox + TabSellX, oy + TabY, "판  매", tabTab == HitSell, _hover == HitSell);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _rows.Count - Rows));
        for (int v = 0; v < Rows && _scroll + v < _rows.Count; v++)
        {
            int i = _scroll + v;
            var r = _rows[i];
            int rx = ox + RowX, ry = oy + RowY + v * RowH;
            var item = Part1Item.ById(_items!, r.Id);
            if (_hover == HitRow + i) art.Fill(rx - 2, ry, 226, RowH - 1, 0x60304890);
            art.Fill(rx, ry, 25, 25, 0xC0080A20);
            art.Stroke(rx, ry, 25, 25, 0xFF8890B8);
            if (item != null && art.Motion(12, item.Icon) is { } icon) art.Blit(icon, rx + (25 - icon.W) / 2 - icon.X, ry + (25 - icon.H) / 2 - icon.Y);
            art.Text(item?.Name ?? $"아이템 {r.Id}", rx + 30, ry + 6, GameWindow.White, 11);
            art.TextRight($"{r.Unit} Eld", rx + 190, ry + 6, GameWindow.White, 11);
            art.TextRight($"({r.Owned,2})", rx + 222, ry + 6, 0xFFC8D0E8, 11);
            Arrows(art, rx + 226, ry + 5, HitUp + i, HitDown + i, r.Qty);
            if (_hover == HitRow + i) _tip = Part1ItemTip.Text(item);
        }
        if (_rows.Count == 0) art.TextCentered(_sell ? "팔 물건이 없습니다" : "파는 물건이 없습니다", ox + ListX + 140, oy + RowY + 80, 0xFFA0A8C8, 12);
        art.ScrollBar(ox + ScrollX, oy + RowY, ScrollH);
        if (_rows.Count > Rows)
        {
            int track = ScrollH - 32, knob = Math.Max(10, track * Rows / _rows.Count);
            int top = oy + RowY + 16 + (track - knob) * _scroll / Math.Max(1, _rows.Count - Rows);
            art.Fill(ox + ScrollX + 3, top, 10, knob, 0xFF8890B8);
        }
    }

    private void SmallTab(int x, int y, string label, bool on, bool hot)
    {
        host.UiFill(x, y, TabW, SmallH, on ? 0xF0485890u : hot ? 0xF0384878u : 0xC0181C40u);
        host.UiStroke(x, y, TabW, SmallH, on ? 0xFFE8ECF8u : 0xFF8890B8u);
        Art.TextCentered(label, x + TabW / 2, y + SmallH / 2, on ? 0xFFFFE080u : 0xFFC8D0E8u, 11);
    }

    private void DrawSums(Part1MapArt art, int ox, int oy)
    {
        int x = ox + SumX, y = oy + SumY, eld = host.Part1.Eld, total = Total;
        art.Box(x, y, SumW, SumH);
        string[] names = ["소지금액", "물품총액", "잔      액"];
        int[] values = [eld, total, Buying ? eld - total : eld + total];
        for (int k = 0; k < 3; k++)
        {
            int ly = y + 10 + k * 24;
            art.Text(names[k], x + 10, ly, GameWindow.White, 11);
            art.TextRight($"{values[k]}", x + 128, ly, k == 2 && values[k] < 0 ? 0xFFFF8080 : GameWindow.White, 11);
            art.Text("Eld", x + 133, ly, GameWindow.White, 11);
        }
        SmallButton(ox + ConfirmX, oy + ConfirmY, "결   정", _hover == HitConfirm);
    }

    /// <summary>길드 카드 — 판 (20, 52) 313×219 안에 90 간격 둘: 바탕(벌 4) · 이름 띠(벌 5) · 전투 그림(모션 2) · 능력치 여덟 · ▲ 수 ▼.</summary>
    private void DrawGuild(Part1MapArt art, int ox, int oy)
    {
        art.Box(ox + ListX, oy + ListY, 313, 219);
        int tick = (int)(host._lastTime * GameWindow.TicksPerSecond);
        for (int i = 0; i < Math.Min(2, _rows.Count); i++)
        {
            var r = _rows[i];
            int cx = ox + CardX, cy = oy + CardY + i * CardStep;
            art.Blit(art.Frame(678, 4, 0), cx, cy);
            art.Blit(art.Frame(678, 4, 1), cx, cy);
            art.Blit(art.Frame(678, 5, 0), cx + 50, cy + 4);
            if (art.Chr(r.Id) is not { } chr) continue;
            if (chr.SpriteId > 0) art.Blit(art.Motion(chr.SpriteId, 2, tick), cx + 23, cy + 66);
            art.Text(chr.Name, cx + 59, cy + 5, 0xFFC8FF96, 11);
            art.TextRight($"{r.Unit} Eld ({r.Owned,2})", cx + 226, cy + 5, GameWindow.White, 11);
            Arrows(art, cx + 238, cy + 4, HitUp + i, HitDown + i, r.Qty);
            (string, int)[] stats =
            [
                ("Hp", chr.MaxHp), ("Str", chr.Stat(Part1Chr.Str)), ("Dex", chr.Stat(Part1Chr.Dex)), ("Spd", chr.Stat(Part1Chr.Spd)),
                ("Skill", chr.Stat(Part1Chr.Skill)), ("Int", chr.Stat(Part1Chr.Int)), ("Ac", chr.Stat(Part1Chr.Ac)), ("Mr", chr.Stat(Part1Chr.Mr)),
            ];
            for (int k = 0; k < stats.Length; k++)
            {
                int sx = cx + 50 + k / 2 * 62, sy = cy + 26 + k % 2 * 15;
                art.Text(stats[k].Item1, sx, sy, 0xFFA0C8FF, 10);
                art.TextRight($"{stats[k].Item2}", sx + 42, sy, GameWindow.White, 10);
            }
        }
    }

    /// <summary>주점 — 1202 의 얼굴들(50×50)을 4×3 칸(61×70, 처음 (21, 52))에. 누르면 그 소문.</summary>
    private void DrawPub(Part1MapArt art, int ox, int oy)
    {
        art.Box(ox + PubX - 6, oy + PubY - 6, 4 * PubW + 12, 3 * PubH + 12);
        for (int i = 0; i < Math.Min(12, _f.Entries.Count); i++)
        {
            int x = ox + PubX + i % 4 * PubW + 5, y = oy + PubY + i / 4 * PubH + 10;
            art.Fill(x, y, 50, 50, 0xC0080A20);
            if (art.Motion(_f.Entries[i][0], 0) is { } face) art.Blit(face, x + 25, y + 25);
            art.Stroke(x - 1, y - 1, 52, 52, _hover == HitPub + i ? 0xFFFFE080 : 0xFF8890B8);
        }
    }
}
