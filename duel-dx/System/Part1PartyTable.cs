using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 일행 한눈에(P, 사용자 요청 — 원본에 없다) — 일행 모두를 한 표에: 줄마다 왼쪽에 얼굴, 이름 · 직업 · 소속 · 다음 직업 귀띔,
/// HP · SOUL · EXP, 능력치 아홉(상태 창과 같은 값 — 바탕 + 어빌리티 + 장비), 장비 다섯 그림, 어빌리티(능력치 어빌리티를 뺀 높은 것 몇 개와 수).
/// </summary>
/// <remarks>
/// <para>전투는 내 편(편 4) 파트1 인물 — HP 지금/최대 · SOUL 지금 값. 챕터 지도는 나갈 팀(없으면 무리 · 일행) — HP 는 최대, SOUL 은 「—」.</para>
/// <para>줄을 누르면 그 사람의 상태 패널(<see cref="Part1StatusPanel"/> 의 전투 밖 꼴 — 전투면 그 판의 HP · SOUL)이 표 위에 뜨고 장비 · 직업을
/// 바꿀 수 있다. 머리 칸을 누르면 그 값으로 줄을 세운다(한 번 더 누르면 거꾸로). 휠 · ↑/↓ 로 굴린다. Esc · P · 오른쪽 단추 · 창 밖 누르기로 닫는다.
/// 장비 그림 · 어빌리티를 짚으면 설명(<see cref="Part1ItemTip"/> · <see cref="Part1AbilityTip"/>).</para>
/// </remarks>
internal sealed class Part1PartyTable(GameWindow host)
{
    internal bool _open;

    private IReadOnlyList<Part1Ability>? _abilities;
    private IReadOnlyList<Part1Item>? _items;
    private IReadOnlyList<Part1Job>? _jobs;

    private sealed record Row(int Code, int Order, UnitState? Unit, Part1Chr Chr, Part1Fighter Fighter, string Next, uint NextColor);

    private List<Row> _rows = [];
    private bool _dirty;
    private int _top;

    /// <summary>줄 세우기 — 0 은 처음 차례, 그 밖은 <see cref="Columns"/> 의 칸 번호. 음수면 거꾸로.</summary>
    private int _sort;

    private readonly List<(int X, int Y, int W, int H, Action Act)> _hits = [];

    private Part1StatusPanel Panel => _panel ??= new(host);
    private Part1StatusPanel? _panel;
    private bool PanelOpen => _panel is { OffOpen: true };

    private const int HeadH = 20, FootH = 18, FaceW = 40, IconW = 22, CellW = 92, WideMin = 874;

    /// <summary>좁은 화면(640 폭 …) — 장비 · 어빌리티를 능력치 아래 둘째 줄에 둔다. <see cref="Window"/> 가 정한다.</summary>
    private bool _narrow;
    private int RowH => _narrow ? 54 : 44;
    private int NameW => _narrow ? 132 : 150;
    private int HpW => _narrow ? 62 : 64;
    private int SoulW => _narrow ? 36 : 38;
    private int ExpW => _narrow ? 40 : 46;
    private int StatW => _narrow ? 26 : 28;
    private const uint Gold = 0xFFFFC840, Dim = 0xFF8890B0, Line = 0xFF6070A8, Green = 0xFF78E078, Yellow = 0xFFFFE070, Bg = 0xFF1C2246, RowBg = 0xFF222A50,
                       RowAlt = 0xFF1E2548, RowHot = 0xFF34407A;

    private static readonly (string Label, int Field)[] Stats =
    [
        ("STR", Part1Chr.Str), ("DEX", Part1Chr.Dex), ("INT", Part1Chr.Int), ("SKL", Part1Chr.Skill), ("LUK", Part1Chr.Luck),
        ("SPD", Part1Chr.Spd), ("AC", Part1Chr.Ac), ("MR", Part1Chr.Mr), ("WTP", Part1Chr.Interval),
    ];

    // ── 열기 · 닫기 ──

    private bool InBattle => host._battleLoaded && host._scene.IsPart1 && !host.Mos._mosesOpen && !host.FieldOpen && !host.Part1TitleScr._open;

    private bool OnMap => host.Part1TitleScr._open
                          && (host.Part1TitleScr.Chapter._open && host.Part1TitleScr.Chapter.Events.Talk == null || host.Part1TitleScr.Party._open);

    /// <summary>P — 파트1 전투 · 챕터 지도면 열고(열려 있으면 닫고) true. 그 밖(파트2 …)이면 false.</summary>
    internal bool Toggle()
    {
        if (_open) { Close(); return true; }
        return Open();
    }

    /// <summary>연다(챕터 지도 메뉴 「일행 한눈에」 · 본부 상태 창 단추도). 열 수 없는 화면이면 false.</summary>
    internal bool Open()
    {
        if (!InBattle && !OnMap) return false;
        _abilities ??= Part1Ability.LoadAll();
        _items ??= Part1Item.LoadAll();
        _jobs ??= Part1Job.LoadAll();
        Refresh();
        if (_rows.Count == 0) { host.Toast("볼 인물이 없습니다"); return true; }
        _open = true;
        _top = 0;
        if (host.JobTree._open) host.JobTree.Close();
        return true;
    }

    internal void Close()
    {
        _panel?.CloseOff();
        _open = false;
        host.StatusScr._statusTip = null;
    }

    private void Refresh()
    {
        _dirty = false;
        var rows = new List<Row>();
        if (InBattle)
        {
            var seen = new HashSet<int>();
            var units = host._units.Select((u, i) => (u, i)).Where(t => t.u.PlayerControlled && t.u.ChrCode >= Part1Game.ChrBase)
                                   .OrderBy(t => t.u.Alive ? 0 : 1).ThenBy(t => t.i);
            foreach (var (u, i) in units)
            {
                if (!seen.Add(u.ChrCode) || Chr(u.ChrCode) is not { } raw) continue;
                var fighter = host.Btl.Part1.Fighter(u) ?? Part1Fighter.From(raw, _items!);
                rows.Add(MakeRow(u.ChrCode, i, u, fighter));
            }
        }
        else
        {
            var members = host.Part1.ActiveMembers();
            if (members.Count == 0) members = host.Part1.GroupMembers();
            if (members.Count == 0) members = host.Part1.Party;
            int order = 0;
            foreach (int code in members.Distinct())
                if (Chr(code) is { } chr) rows.Add(MakeRow(code, order++, null, Part1Fighter.From(chr, _items!)));
        }
        _rows = rows;
        ApplySort();
    }

    private Part1Chr? Chr(int code) => Part1Chr.Load(code - Part1Game.ChrBase) is { } raw ? host.Part1.Apply(raw) : null;

    private Row MakeRow(int code, int order, UnitState? unit, Part1Fighter fighter)
    {
        var chr = fighter.Chr;
        string next = "";
        uint color = Dim;
        if (host.Part1.IsOurs(code) || unit == null)
        {
            var jobs = Part1JobTree.NextJobsFor(chr);
            var ready = jobs.Where(j => j.Missing.Count == 0).Select(j => j.Job.Name).ToList();
            if (ready.Count > 0) { next = "→ 전직 가능: " + string.Join(", ", ready); color = Green; }
            else if (jobs.Count > 0)
            {
                var (job, missing) = jobs[0];
                next = $"→ {job.Name} ({missing.Count}개 부족)";
                color = Yellow;
            }
        }
        return new Row(code, order, unit, chr, fighter, next, color);
    }

    // ── 줄 세우기 ──

    /// <summary>머리 칸 — (이름, 값). 값이 null 이면 그 칸은 누를 수 없다.</summary>
    private static readonly (string Label, Func<Row, int>? Key)[] Columns = BuildColumns();

    private static (string, Func<Row, int>?)[] BuildColumns()
    {
        var list = new List<(string, Func<Row, int>?)>
        {
            ("", null),
            ("인물 · 직업", r => r.Chr.Fields[7]),
            ("HP", r => r.Unit?.Hp ?? r.Chr.MaxHp),
            ("SOUL", r => r.Unit?.Soul ?? 0),
            ("EXP", r => r.Chr.Experience),
        };
        foreach (var (label, field) in Stats)
            list.Add((label, r => field == Part1Chr.Interval ? r.Chr.ActionInterval : r.Fighter.Stat(field)));
        list.Add(("장비", null));
        list.Add(("어빌리티", r => r.Chr.Abilities.Count(a => a.Ability > 0 && a.Level > 0)));
        return [.. list];
    }

    private void SortBy(int column)
    {
        _sort = Math.Abs(_sort) == column ? -_sort : column;
        ApplySort();
    }

    private void ApplySort()
    {
        int col = Math.Abs(_sort);
        if (col == 0 || Columns[col].Key is not { } key) { _rows = [.. _rows.OrderBy(r => r.Order)]; return; }
        // 처음 누르면 큰 값부터(능력치 비교), 인물 · 직업은 직업 번호 차례.
        bool desc = col != 1;
        if (_sort < 0) desc = !desc;
        _rows = desc ? [.. _rows.OrderByDescending(key).ThenBy(r => r.Order)] : [.. _rows.OrderBy(key).ThenBy(r => r.Order)];
    }

    // ── 입력 ──

    internal void OnKey(int key)
    {
        if (PanelOpen)
        {
            if (key == Win32.VK_ESCAPE) ClosePanel();
            else if (key is Win32.VK_LEFT or Win32.VK_RIGHT) _panel!.SwitchOff(key == Win32.VK_LEFT ? -1 : 1);
            return;
        }
        if (key == Win32.VK_ESCAPE || host._keys.ActionFor(key) == KeyAction.PartyTable) Close();
        else if (host._keys.ActionFor(key) == KeyAction.JobTree) { Close(); host.JobTree.Toggle(); }
        else if (key == Win32.VK_UP) Scroll(-1);
        else if (key == Win32.VK_DOWN) Scroll(1);
    }

    internal void OnWheel(int notches) { if (!PanelOpen) Scroll(-notches); }

    private void Scroll(int delta) => _top = Math.Clamp(_top + delta, 0, Math.Max(0, _rows.Count - VisibleRows()));

    private void ClosePanel()
    {
        _panel?.CloseOff();
        Refresh();
    }

    /// <summary>열려 있으면 클릭을 먹는다 — 패널이 떠 있으면 패널이, 아니면 머리 칸 · 줄, 창 밖이면 닫기.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        if (PanelOpen)
        {
            _panel!.OffClick(bx, by);
            if (!PanelOpen || _dirty) Refresh();
            return true;
        }
        foreach (var (x, y, w, h, act) in _hits)
            if (bx >= x && bx < x + w && by >= y && by < y + h) { act(); return true; }
        var (wx, wy, ww, wh) = Window();
        if (bx < wx || by < wy - GameWindow.FrameTitleH || bx >= wx + ww || by >= wy + wh) Close();
        return true;
    }

    /// <summary>오른쪽 단추 — 패널이 떠 있으면 패널이(목록 닫기 · 어빌리티 설명 · 밖이면 패널 닫기), 아니면 표를 닫는다.</summary>
    internal void OnRightDown(int bx, int by)
    {
        if (PanelOpen)
        {
            _panel!.OffRightDown(bx, by);
            if (!PanelOpen) Refresh();
            return;
        }
        Close();
    }

    private void OpenRow(Row row)
    {
        var codes = _rows.Select(r => r.Code).ToList();
        var units = _rows.Where(r => r.Unit != null).ToDictionary(r => r.Code, r => r.Unit!);
        Panel.OpenOff(row.Code, codes, () =>
        {
            _dirty = true;
            if (!InBattle && host.Part1TitleScr.Chapter._open) host.Part1TitleScr.Chapter.SaveProgress();
        }, units.Count > 0 ? code => units.GetValueOrDefault(code) : null);
    }

    // ── 그리기 ──

    private (int X, int Y, int W, int H) Window()
    {
        int w = Math.Min(980, host.ViewWidth - 16);
        _narrow = w < WideMin;
        int want = HeadH + 14 + Math.Max(1, _rows.Count) * RowH + FootH + 8;
        int h = Math.Min(want, host.ViewHeight - GameWindow.FrameTitleH - 16);
        return (host._camX + (host.ViewWidth - w) / 2, host._camY + (host.ViewHeight - h + GameWindow.FrameTitleH) / 2, w, h);
    }

    private int VisibleRows()
    {
        var (_, _, _, h) = Window();
        return Math.Max(1, (h - HeadH - 14 - FootH - 8) / RowH);
    }

    private bool Hot(int x, int y, int w, int h) => host._mouse.X >= x && host._mouse.X < x + w && host._mouse.Y >= y && host._mouse.Y < y + h;

    private int TextW(string text, float size) => host.UiGetText(text, GameWindow.White, size).W;

    /// <summary>칸 너비에 맞게 뒤를 「…」로 자른다.</summary>
    private string Fit(string text, int w, float size)
    {
        if (TextW(text, size) <= w) return text;
        for (int n = text.Length - 1; n > 0; n--)
            if (TextW(text[..n] + "…", size) <= w) return text[..n] + "…";
        return "";
    }

    /// <summary>그림 한 장을 네모에 맞춰 — 새 UI 면 글과 같은 층에(바탕이 층에 있어 판에 그리면 덮인다), 아니면 판에.</summary>
    private void Img(SpriteFrame f, int x, int y, int w, int h)
    {
        if (f.W == 0 || f.H == 0) return;
        if (!host._hiResUi) { host.BlitScaled(f, x, y, w, h); return; }
        double scale = Math.Min(1.0, Math.Min((double)w / f.W, (double)h / f.H));
        int dw = Math.Max(1, (int)(f.W * scale)), dh = Math.Max(1, (int)(f.H * scale));
        host.Hi.DrawImage(f.Px, f.W, f.W, f.H, x + (w - dw) / 2, y + (h - dh) / 2, dw, dh);
    }

    internal void Draw()
    {
        if (!_open) return;
        if (!InBattle && !OnMap) { Close(); return; }
        if (_dirty && !PanelOpen) Refresh();
        _hits.Clear();
        var (x, y, w, h) = Window();
        host.UiFill(x, y - GameWindow.FrameTitleH, w, h + GameWindow.FrameTitleH, Bg);   // 틀 바탕이 비치지 않게 먼저 깐다
        host.UiFrame(x, y, w, h, InBattle ? $"동료 정보 — 전투 ({_rows.Count}명)" : $"동료 정보 ({_rows.Count}명)");

        // 칸의 왼쪽 끝과 너비 — 좁으면 장비 · 어빌리티 칸은 HP 칸 아래(둘째 줄)부터.
        var cols = new List<(int X, int W)>();
        int cx = x + 6;
        void Col(int cw) { cols.Add((cx, cw)); cx += cw; }
        Col(FaceW + 4); Col(NameW); Col(HpW); Col(SoulW); Col(ExpW);
        foreach (var _ in Stats) Col(StatW);
        if (_narrow) cx = cols[2].X;
        Col(5 * IconW + 8); Col(x + w - 10 - cx);

        // 머리 줄 — 누르면 그 값으로 줄 세우기.
        int hy = y + 6;
        host.UiFill(x + 4, hy, w - 8, HeadH, 0xFF141A38);
        for (int c = 0; c < Columns.Length && c < cols.Count; c++)
        {
            var (colX, colW) = cols[c];
            string label = Columns[c].Label;
            if (label.Length == 0 || _narrow && c >= 5 + Stats.Length) continue;
            bool sortable = Columns[c].Key != null;
            bool hot = sortable && Hot(colX, hy, colW, HeadH);
            if (hot) host.UiFill(colX, hy, colW, HeadH, RowHot);
            if (Math.Abs(_sort) == c) label += _sort > 0 == (c != 1) ? " ▼" : " ▲";
            uint color = Math.Abs(_sort) == c ? Gold : sortable ? 0xFFB8C4E8 : Dim;
            bool center = c >= 2 && c < 2 + 3 + Stats.Length;
            if (center) host.UiText(label, colX + (colW - TextW(label, 10)) / 2, hy + 4, color, 10);
            else host.UiText(label, colX + 4, hy + 4, color, 10);
            if (sortable) { int col = c; _hits.Add((colX, hy, colW, HeadH, () => SortBy(col))); }
        }

        // 줄들.
        int top = hy + HeadH + 4, visible = VisibleRows();
        _top = Math.Clamp(_top, 0, Math.Max(0, _rows.Count - visible));
        string? tip = null;
        for (int k = 0; k < visible && _top + k < _rows.Count; k++)
        {
            var row = _rows[_top + k];
            int ry = top + k * RowH;
            bool hot = Hot(x + 4, ry, w - 8, RowH - 2);
            host.UiFill(x + 4, ry, w - 8, RowH - 2, hot ? RowHot : k % 2 == 0 ? RowBg : RowAlt);
            tip = DrawRow(row, cols, ry) ?? tip;
            _hits.Add((x + 4, ry, w - 8, RowH - 2, () => OpenRow(row)));
        }

        // 굴림 막대 — 다 안 보일 때만.
        if (_rows.Count > visible)
        {
            int barY = top, barH = visible * RowH - 2;
            host.UiFill(x + w - 8, barY, 4, barH, 0xFF303858);
            int thumbH = Math.Max(12, barH * visible / _rows.Count), thumbY = barY + (barH - thumbH) * _top / Math.Max(1, _rows.Count - visible);
            host.UiFill(x + w - 8, thumbY, 4, thumbH, 0xFF8098C8);
        }

        host.UiText("줄을 누르면 상태 창(장비 · 직업 바꾸기) · 머리 칸을 누르면 줄 세우기 · 휠/↑↓ 굴리기 · Esc/P/오른쪽 단추: 닫기",
                    x + 10, y + h - FootH + 2, Dim, 9);

        if (PanelOpen)
        {
            int ox = host._camX + (host.ViewWidth - 640) / 2, oy = host._camY + (host.ViewHeight - 480) / 2;
            _panel!.DrawOff(ox, oy);
            return;
        }
        if (tip != null) host.StatusScr.DrawDescriptionTip(tip, host._mouse.X, host._mouse.Y, host._camX, host._camY, host.ViewWidth, host.ViewHeight);
    }

    /// <summary>한 줄을 그린다 — 짚은 장비 · 어빌리티가 있으면 그 설명을 돌려준다.</summary>
    private string? DrawRow(Row row, List<(int X, int W)> cols, int ry)
    {
        var chr = row.Chr;
        int dy = _narrow ? -7 : 0;   // 좁으면 위 줄을 올리고 아래에 장비 · 어빌리티
        bool dead = row.Unit is { Alive: false };
        string? tip = null;

        // 얼굴.
        if (chr.FaceId > 0 && host.UiFor(Part1Game.ObsBase + chr.FaceId)?.FrameAt(0, 0) is { } face)
            Img(face, cols[0].X + 1, ry + 1, FaceW, FaceW);
        host.UiStroke(cols[0].X, ry, FaceW + 2, FaceW + 2, dead ? 0xFF803030 : Line);

        // 이름 · Lv / 직업 · 소속 / 다음 직업.
        var (nx, nw) = cols[1];
        host.UiText(Fit(chr.Name, nw - 44, 12), nx + 4, ry + 2, dead ? Dim : Gold, 12);
        host.UiRightText($"Lv {chr.Level}", nx + nw - 6, ry + 4, 0xFFD0D8F0, 10);
        string job = Part1Job.ById(_jobs!, chr.Fields[7])?.Name ?? "—";
        int affiliation = chr.Fields[6];
        string group = affiliation >= 0 && affiliation < Part1StatusPanel.Affiliations.Length ? Part1StatusPanel.Affiliations[affiliation] : "";
        host.UiText(Fit(job, nw - 70, 10), nx + 4, ry + 17, GameWindow.White, 10);
        host.UiRightText(group, nx + nw - 6, ry + 18, 0xFF98A8C8, 9);
        if (row.Next.Length > 0) host.UiText(Fit(row.Next, nw - 8, 9), nx + 4, ry + 30, row.NextColor, 9);

        // HP · SOUL · EXP.
        int maxHp = row.Unit?.MaxHp ?? Math.Max(1, chr.MaxHp), hp = row.Unit?.Hp ?? maxHp;
        Gauge(cols[2], ry + dy, row.Unit != null ? $"{hp}/{maxHp}" : $"{maxHp}", hp, maxHp, dead ? 0xFF804040 : 0xFF60D060);
        if (row.Unit is { } u) Gauge(cols[3], ry + dy, $"{u.Soul}", u.Soul, 150, 0xFF60A0F0);
        else CenterText("—", cols[3], ry + 14 + dy, Dim, 11);
        Gauge(cols[4], ry + dy, $"{chr.Experience % 100}", chr.Experience % 100, 100, 0xFFF0C060);

        // 능력치 아홉.
        for (int s = 0; s < Stats.Length; s++)
        {
            int field = Stats[s].Field;
            int value = field == Part1Chr.Interval ? chr.ActionInterval : row.Fighter.Stat(field);
            CenterText($"{value}", cols[5 + s], ry + 14 + dy, GameWindow.White, 11);
        }

        // 장비 다섯 — 그림, 짚으면 설명.
        var (ex, _) = cols[5 + Stats.Length];
        for (int slot = 0; slot < 5; slot++)
        {
            int ix = ex + 4 + slot * IconW, iy = _narrow ? ry + 28 : ry + 10;
            host.UiStroke(ix, iy, IconW - 2, IconW - 2, 0xFF404C80);
            var item = Part1Item.ById(_items!, chr.Equipment[slot]);
            if (item != null && host.UiFor(Part1Game.ObsBase + 12)?.FrameAt(item.Icon, 0) is { } icon) Img(icon, ix + 1, iy + 1, IconW - 4, IconW - 4);
            if (item != null && Hot(ix, iy, IconW - 2, IconW - 2))
            {
                host.UiStroke(ix, iy, IconW - 2, IconW - 2, 0xFFFFE080);
                tip = Part1ItemTip.Text(item) is { } t ? $"{Part1PartyScreen.SlotName(slot, item)}: {t}" : null;
            }
        }

        // 어빌리티 — 능력치 어빌리티(1~9)를 뺀 높은 차례, 두 줄. 짚으면 설명.
        var (ax, aw) = cols[6 + Stats.Length];
        var learned = chr.Abilities.Where(a => a.Ability > 0 && a.Level > 0).ToList();
        var key = learned.Where(a => a.Ability > 9).OrderByDescending(a => a.Level).ThenBy(a => a.Ability).ToList();
        int lines = _narrow ? 1 : 2, top = _narrow ? ry + 31 : ry + 4;
        host.UiRightText($"{learned.Count}개", ax + aw - 4, _narrow ? top + 1 : ry + 2, Dim, 9);
        int perLine = Math.Max(1, (aw - 30) / CellW);
        for (int i = 0; i < key.Count && i < perLine * lines; i++)
        {
            var (ability, level) = key[i];
            int px = ax + 4 + i % perLine * CellW, py = top + i / perLine * 18;
            var info = Part1Ability.ById(_abilities!, ability);
            if (info != null && host.UiFor(Part1Game.ObsBase + 11)?.FrameAt(info.Tail + 0x13, 0) is { } icon) Img(icon, px, py, 14, 14);
            string name = Fit(info?.Name ?? $"#{ability}", CellW - 40, 10);
            // 쓰는 레벨(원본 0x1009e020) — 지금 직업이 안 다루면 흐리게 「-」, 직업 최대로 막히면 그 값. 아이템 기술(폭풍검)은 초필살기 제어 + 그 무기일 때만.
            int use = chr.ListedLevel(ability);
            host.UiText(name, px + 17, py, use > 0 ? GameWindow.White : Dim, 10);
            host.UiText(use > 0 ? $"{use}" : "-", px + 19 + TextW(name, 10), py, use > 0 ? Yellow : Dim, 10);
            if (Hot(px, py - 1, CellW - 4, 16)) tip = Part1AbilityTip.Text(ability, level);
        }
        if (key.Count == 0) host.UiText(learned.Count > 0 ? "능력치 어빌리티만" : "없음", ax + 4, _narrow ? top : ry + 14, Dim, 10);
        return tip;
    }

    private void CenterText(string text, (int X, int W) col, int y, uint color, float size) =>
        host.UiText(text, col.X + (col.W - TextW(text, size)) / 2, y, color, size);

    private void Gauge((int X, int W) col, int ry, string text, int value, int max, uint color)
    {
        CenterText(text, col, ry + 10, GameWindow.White, 10);
        int bw = col.W - 10;
        host.UiFill(col.X + 5, ry + 26, bw, 3, 0xFF303848);
        if (max > 0) host.UiFill(col.X + 5, ry + 26, bw * Math.Clamp(value, 0, max) / max, 3, color);
    }
}
