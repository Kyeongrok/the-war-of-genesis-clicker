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
/// 시설을 고르면 원본 시설 창(<see cref="Part1TownWindow"/> — 상점 구입·판매 · 용병 고용 · 주점 소문)을 연다.
/// </remarks>
internal sealed class Part1TownScreen(GameWindow host, Part1TitleScreen shell)
{
    internal enum Kind { Shop, Guild, Pub }

    /// <summary>시설 하나 — 이름(인사 줄의 말하는 이에서 「주인」을 뗀 것), 갈래, 머리 인자(1001 · 1101 · 1201), 목록 인자(1002 · 1102 · 1202 마다).</summary>
    internal sealed record Facility(string Name, Kind Kind, short[] Head, List<short[]> Entries);

    private Part1TownWindow? _window;
    private Part1TownWindow Window => _window ??= new(host, shell, Line);

    internal bool _open;
    private Part1Field? _field;

    /// <summary>열린 마을 필드 번호(장면 번호 꼬리표) — 없으면 −1.</summary>
    internal int FieldId => _field?.Id ?? -1;
    private List<Facility> _facilities = [];
    private int _selected = -1;
    private int _hover = -1;
    private Action? _onLeave;
    private uint[]? _bg;
    private int _bgW, _bgH;
    private readonly Queue<(string Speaker, string Text)> _talks = [];
    private (string Speaker, string Text)? _talk;

    internal const int ButtonHit = 1000;

    /// <summary>이름 띠 · 시설 단추(건물 아이콘) · 얼굴 칸 · EXIT — 원본 마을 첫 화면.</summary>
    private Part1TownMenu Menu => _menu ??= new(host);
    private Part1TownMenu? _menu;

    /// <summary>시설마다 조건 200 의 값(= 단추 칸) — 시설 목록과 같은 차례.</summary>
    private int[] _slots = [];

    internal void Open(int fieldId, Action onLeave)
    {
        if (Part1Field.Load(fieldId) is not { } field)
        {
            host.Toast($"파트1 마을 필드 {fieldId:D4} 를 못 읽었습니다");
            onLeave();
            return;
        }
        Menu.Open(field);
        _slots = [.. field.Events.Where(e => e.Conditions.FirstOrDefault()?.Code == 200 && e.Actions.Any(a => a.Code is 1001 or 1101 or 1201))
                                 .Select(e => (int)e.Conditions[0].Args[0])];
        // 마을 배경음악 — 머리 낱말 4 의 BGMs 곡을 되풀이로(장면 0x100c5716: 마을(+0xd4)이고 0xffff 가 아니면 0x100094e0(곡, 1), +0xdc 로 한 번만).
        if (field.Header.Length > 4 && field.Header[4] >= 0) host.PlayMusicFile(Part1Game.BgmBase + field.Header[4], loop: true);
        _field = field;
        _onLeave = onLeave;
        (_bg, _bgW, _bgH) = shell.Field.ReadImage(field.Background);
        _facilities = FacilitiesOf(field);
        _selected = -1;
        _hover = -1;
        _talks.Clear();
        _talk = null;
        if (_window != null) _window._open = false;
        _open = true;
        // 시험: DUELDX_PART1SHOP=n — n 번째 시설 창을 곧바로 연다.
        if (int.TryParse(Environment.GetEnvironmentVariable("DUELDX_PART1SHOP"), out int open) && open >= 0 && open < _facilities.Count) OpenFacility(open);
    }

    /// <summary>마을 필드의 시설들 — 조건 200 사건마다 머리 행동(1001 · 1101 · 1201)과 그 목록(+1). 챕터 지도의 상점 정보 창(<see cref="Part1TownShopInfo"/>)도 이것을 쓴다.</summary>
    internal static List<Facility> FacilitiesOf(Part1Field field)
    {
        string Line(int i) => i >= 0 && i < field.Lines.Count ? field.Lines[i] : "";
        var list = new List<Facility>();
        foreach (var e in field.Events)
        {
            if (e.Conditions.FirstOrDefault()?.Code != 200 || e.Actions.Count == 0) continue;
            var head = e.Actions.FirstOrDefault(a => a.Code is 1001 or 1101 or 1201);
            if (head == null) continue;
            var kind = head.Code switch { 1001 => Kind.Shop, 1101 => Kind.Guild, _ => Kind.Pub };
            int listCode = head.Code + 1;
            var entries = e.Actions.Where(a => a.Code == listCode).Select(a => a.Args).ToList();
            string speaker = Part1Game.Speaker(Line(head.Args[1])).Trim();
            string name = speaker.EndsWith("주인") ? speaker[..^2] : speaker.Length > 0 ? speaker : kind switch { Kind.Shop => "상점", Kind.Guild => "용병 길드", _ => "주점" };
            list.Add(new Facility(name, kind, head.Args, entries));
        }
        return list;
    }

    private string Line(int i) => _field is { } f && i >= 0 && i < f.Lines.Count ? f.Lines[i] : "";

    /// <summary>EXIT → <see cref="ButtonHit"/>, 시설 단추 → 그 시설 차례(조건 200 [칸] 사건), 없으면 −1.</summary>
    private int HitAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        int hit = Menu.HitAt(bx - ox, by - oy);
        if (hit == Part1TownMenu.HitExit) return ButtonHit;
        return hit < 0 ? -1 : Array.IndexOf(_slots, hit);
    }

    internal void UpdateHover(int bx, int by)
    {
        if (_window is { _open: true } w) { w.UpdateHover(bx, by); return; }
        _hover = HitAt(bx, by);
    }

    internal void OnClick(int bx, int by)
    {
        if (_window is { _open: true } w) { w.OnClick(bx, by); return; }
        if (_talk != null) { NextTalk(); return; }
        int hit = HitAt(bx, by);
        if (hit == ButtonHit) { Leave(); return; }
        if (hit < 0 || hit >= _facilities.Count) return;
        OpenFacility(hit);
    }

    /// <summary>시설 창을 연다(1003 · 1103 · 1203 의 첫 부름 — 0x100c8730 · 0x100c9090 · 0x100ca810).</summary>
    private void OpenFacility(int i)
    {
        _selected = i;
        _hover = -1;
        Window.Open(_facilities[i], () => { _selected = -1; _hover = -1; });
    }

    internal void OnKey(int key)
    {
        if (_window is { _open: true } w) { w.OnKey(key); return; }
        if (_talk != null) { if (key is Win32.VK_RETURN or Win32.VK_SPACE or Win32.VK_ESCAPE) NextTalk(); return; }
        if (key == Win32.VK_ESCAPE) Leave();
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

    internal void Draw()
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        if (_bg is { } bg)
            for (int y = 0; y < Math.Min(MosesScene.MosesH, _bgH); y++)
                for (int x = 0; x < Math.Min(MosesScene.MosesW, _bgW); x++)
                    host.SetPixel(ox + x, oy + y, bg[y * _bgW + x] | 0xFF000000);
        if (_window is { _open: true } w) { w.Draw(ox, oy); host.DrawToast(); return; }
        int hover = _hover == ButtonHit ? Part1TownMenu.HitExit : _hover >= 0 && _hover < _slots.Length ? _slots[_hover] : -1;
        Menu.Draw(ox, oy, hover);
        if (_talk is { } talk) shell.DrawTalk(ox, oy, talk.Speaker, talk.Text);
        host.DrawToast();
    }
}
