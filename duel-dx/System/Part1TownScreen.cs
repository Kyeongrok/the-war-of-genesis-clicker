using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 마을 — 챕터 지도의 마을 장소(종류 1)가 가리키는 필드(<c>Flds\NNNN.fld</c>, 0138 시반 포트리스 · 0109 레오카디오 …)를 마을로 연다.
/// </summary>
/// <remarks>
/// 마을 필드는 사건 0 이 「시설 고르기」 사건들을 늘어놓고, 사건마다 조건 200 [시설 차례]·행동 묶음 하나다(분석-파트1-챕터 8절):
/// 1000~1003 상점(1001 [얼굴, 인사 줄] · 1002 [아이템, 0] 파는 물건) · 1100~1103 용병 길드(1101 인사 · 1102 [Chr, 값] 용병) ·
/// 1200~1203 주점(1201 인사 · 1202 [얼굴, 줄] 소문). 배경은 그 필드의 <c>BGRs</c>.
/// 옮긴 것: 시설 목록 · 상점(값을 eld 로 치르고 가방에 — <see cref="Part1PartyScreen"/> 에서 낀다) · 용병 고용(값을 eld 로 치르고 일행에 든다 — 값 뜻은 가설) · 주점 소문 대사.
/// </remarks>
internal sealed class Part1TownScreen(GameWindow host, Part1TitleScreen shell)
{
    internal enum Kind { Shop, Guild, Pub }

    /// <summary>시설 하나 — 이름(인사 줄의 말하는 이에서 「주인」을 뗀 것), 갈래, 인사 줄, 목록(아이템 · 용병 Chr · 소문 줄과 값).</summary>
    internal sealed record Facility(string Name, Kind Kind, int Greeting, List<(int Id, int Value)> Entries);

    internal bool _open;
    private Part1Field? _field;
    private List<Facility> _facilities = [];
    private int _selected = -1;
    private int _hover = -1;
    private Action? _onLeave;
    private uint[]? _bg;
    private int _bgW, _bgH;
    private readonly Queue<(string Speaker, string Text)> _talks = [];
    private (string Speaker, string Text)? _talk;

    internal const int ListX = 24, ListY = 70, RowW = 180, RowH = 26, DetailX = 230, DetailW = 386, ButtonHit = 1000, EntryHit = 2000;

    internal void Open(int fieldId, Action onLeave)
    {
        if (Part1Field.Load(fieldId) is not { } field)
        {
            host.Toast($"파트1 마을 필드 {fieldId:D4} 를 못 읽었습니다");
            onLeave();
            return;
        }
        _field = field;
        _onLeave = onLeave;
        (_bg, _bgW, _bgH) = shell.Field.ReadImage(field.Background);
        _facilities = [];
        foreach (var e in field.Events)
        {
            if (e.Conditions.FirstOrDefault()?.Code != 200 || e.Actions.Count == 0) continue;
            var head = e.Actions.FirstOrDefault(a => a.Code is 1001 or 1101 or 1201);
            if (head == null) continue;
            var kind = head.Code switch { 1001 => Kind.Shop, 1101 => Kind.Guild, _ => Kind.Pub };
            int listCode = head.Code + 1;
            var entries = e.Actions.Where(a => a.Code == listCode).Select(a => ((int)a.Args[0], (int)a.Args[1])).ToList();
            string speaker = Part1Game.Speaker(Line(head.Args[1])).Trim();
            string name = speaker.EndsWith("주인") ? speaker[..^2] : speaker.Length > 0 ? speaker : kind switch { Kind.Shop => "상점", Kind.Guild => "용병 길드", _ => "주점" };
            _facilities.Add(new Facility(name, kind, head.Args[1], entries));
        }
        _selected = -1;
        _hover = -1;
        _talks.Clear();
        _talk = null;
        _open = true;
    }

    private string Line(int i) => _field is { } f && i >= 0 && i < f.Lines.Count ? f.Lines[i] : "";

    private string TownName => _field is { Lines.Count: > 0 } f ? Part1Game.PlainText(f.Lines[0]) : "마을";

    private int HitAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        int x = bx - ox, y = by - oy;
        if (x >= 250 && x < 250 + Part1TitleScreen.ButtonW && y >= 446 && y < 446 + Part1TitleScreen.ButtonH) return ButtonHit;
        for (int i = 0; i < _facilities.Count; i++)
            if (x >= ListX && x < ListX + RowW && y >= ListY + i * (RowH + 4) && y < ListY + i * (RowH + 4) + RowH) return i;
        if (_selected >= 0 && _facilities[_selected].Kind != Kind.Pub)
            for (int i = 0; i < _facilities[_selected].Entries.Count; i++)
                if (x >= DetailX && x < DetailX + DetailW && y >= ListY + 60 + i * RowH && y < ListY + 60 + (i + 1) * RowH - 2) return EntryHit + i;
        return -1;
    }

    internal void UpdateHover(int bx, int by) => _hover = HitAt(bx, by);

    internal void OnClick(int bx, int by)
    {
        if (_talk != null) { NextTalk(); return; }
        int hit = HitAt(bx, by);
        if (hit == ButtonHit) { Leave(); return; }
        if (hit >= EntryHit) { if (_facilities[_selected].Kind == Kind.Shop) Buy(hit - EntryHit); else Hire(hit - EntryHit); return; }
        if (hit < 0 || hit >= _facilities.Count) return;
        _selected = hit;
        var facility = _facilities[hit];
        // 들어서면 주인의 인사 — 주점은 소문을 차례로 이어 말한다.
        Say(Line(facility.Greeting));
        if (facility.Kind == Kind.Pub)
            foreach (var (_, line) in facility.Entries) Say(Line(line));
        NextTalk();
    }

    internal void OnKey(int key)
    {
        if (_talk != null) { if (key is Win32.VK_RETURN or Win32.VK_SPACE or Win32.VK_ESCAPE) NextTalk(); return; }
        if (key == Win32.VK_ESCAPE) { if (_selected >= 0) _selected = -1; else Leave(); }
    }

    private void Say(string raw)
    {
        if (raw.Length > 0) _talks.Enqueue((Part1Game.Speaker(raw).Trim(), Part1Game.PlainText(raw)));
    }

    private void NextTalk() => _talk = _talks.Count > 0 ? _talks.Dequeue() : null;

    private void Leave()
    {
        var onLeave = _onLeave;
        _onLeave = null;
        host.LeaveScene(() => { _open = false; _bg = null; onLeave?.Invoke(); });
    }

    /// <summary>물건 사기 — 값(Item.dat +0x40)을 eld 로 치르고 가방에 넣는다. 일행 화면에서 낀다.</summary>
    private void Buy(int i)
    {
        if (_selected < 0 || _facilities[_selected] is not { Kind: Kind.Shop } shop || i >= shop.Entries.Count) return;
        if (Part1Item.ById(Part1Item.LoadAll(), shop.Entries[i].Id) is not { } item) return;
        if (host.Part1.Eld < item.Price) { host.Toast($"{item.Name} — eld 가 모자랍니다({host.Part1.Eld} / {item.Price})"); return; }
        host.Part1.Eld -= item.Price;
        host.Part1.Bag.Add(item.Id);
        host.Part1.Save(host.FlagSt._flags);
        host.Toast($"{item.Name} 을(를) 샀습니다 — 남은 eld {host.Part1.Eld}");
    }

    /// <summary>용병 고용 — 값(1102 둘째 인자)을 eld 로 치르고 일행에 넣는다. 이미 일행이면 알린다.</summary>
    private void Hire(int i)
    {
        if (_selected < 0 || _facilities[_selected] is not { Kind: Kind.Guild } guild || i >= guild.Entries.Count) return;
        var (chr, price) = guild.Entries[i];
        int code = Part1Game.ChrBase + chr;
        string name = host._db?.Character(code) is { } c ? host._db.T(c.NameId) : $"Chr {chr}";
        if (host.Part1.Eld < price) { host.Toast($"{name} — eld 가 모자랍니다({host.Part1.Eld} / {price})"); return; }
        host.Part1.Eld -= price;
        host.Part1.Mercenaries[code] = host.Part1.Mercenaries.GetValueOrDefault(code) + 1;   // 용병 — 일행이 아니라 용병 칸(배치 창에서 세운다)
        host.Part1.Save(host.FlagSt._flags);
        host.Toast($"{name} 을(를) 고용했습니다(용병 {host.Part1.Mercenaries[code]}) — 남은 eld {host.Part1.Eld}");
    }

    internal void Draw()
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        if (_bg is { } bg)
            for (int y = 0; y < Math.Min(MosesScene.MosesH, _bgH); y++)
                for (int x = 0; x < Math.Min(MosesScene.MosesW, _bgW); x++)
                    host.SetPixel(ox + x, oy + y, bg[y * _bgW + x] | 0xFF000000);
        host.UiFill(ox, oy, MosesScene.MosesW, 52, 0xC0101828);
        host.UiText(TownName, ox + 24, oy + 14, GameWindow.White, 18);
        string eld = $"eld {host.Part1.Eld}";
        host.UiText(eld, ox + 616 - host.UiGetText(eld, 0xFFFFE080, 13).W, oy + 18, 0xFFFFE080, 13);

        for (int i = 0; i < _facilities.Count; i++)
        {
            var f = _facilities[i];
            int y = oy + ListY + i * (RowH + 4);
            host.UiFill(ox + ListX, y, RowW, RowH, _hover == i || _selected == i ? 0xF0305080u : 0xD0182030u);
            host.UiStroke(ox + ListX, y, RowW, RowH, 0xFF6078A0);
            string tag = f.Kind switch { Kind.Shop => "상점", Kind.Guild => "용병", _ => "주점" };
            host.UiText($"[{tag}] {f.Name}", ox + ListX + 8, y + 5, GameWindow.White, 12);
        }

        if (_selected >= 0 && _facilities[_selected] is { } sel && sel.Kind != Kind.Pub)
        {
            int h = 70 + sel.Entries.Count * RowH;
            host.UiFill(ox + DetailX - 8, oy + ListY - 8, DetailW + 16, h, 0xE0101828);
            host.UiStroke(ox + DetailX - 8, oy + ListY - 8, DetailW + 16, h, 0xFF6078A0);
            host.UiText(sel.Kind == Kind.Shop ? $"{sel.Name} — 누르면 사서 가방에(일행 화면에서 낀다)" : $"{sel.Name} — 누르면 고용", ox + DetailX, oy + ListY + 4, 0xFFFFE080, 12);
            var items = sel.Kind == Kind.Shop ? Part1Item.LoadAll() : null;
            for (int i = 0; i < sel.Entries.Count; i++)
            {
                var (id, value) = sel.Entries[i];
                int y = oy + ListY + 60 + i * RowH;
                string label;
                if (items != null)
                {
                    label = Part1Item.ById(items, id) is { } item ? $"{item.Name}   {item.Price} eld" : $"아이템 {id}";
                    host.UiFill(ox + DetailX, y, DetailW, RowH - 2, _hover == EntryHit + i ? 0xF0305080u : 0xC0202838u);
                }
                else
                {
                    int code = Part1Game.ChrBase + id;
                    var c = host._db?.Character(code);
                    string name = c != null ? host._db!.T(c.NameId) : $"Chr {id}";
                    label = $"{name}  Lv {c?.Level ?? 0}   {value} eld{(host.Part1.Mercenaries.GetValueOrDefault(code) is > 0 and var have ? $"  (용병 {have})" : "")}";
                    host.UiFill(ox + DetailX, y, DetailW, RowH - 2, _hover == EntryHit + i ? 0xF0305080u : 0xC0202838u);
                }
                host.UiText(label, ox + DetailX + 8, y + 4, GameWindow.White, 12);
            }
        }

        shell.DrawButton(ox + 250, oy + 446, Part1TitleScreen.ButtonW, Part1TitleScreen.ButtonH, "나가기", _hover == ButtonHit);
        if (_talk is { } talk) shell.DrawTalk(ox, oy, talk.Speaker, talk.Text);
        host.DrawToast();
    }
}
