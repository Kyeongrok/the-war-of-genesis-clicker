using System.IO;
using System.Text;
using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 도구 &gt; 군단 에디터 — 파트2 군단(원본 For.dat + 새 군단 900+)의 리더 · 부하 여섯 · 진형을 게임 안에서 고친다(사용자 요청 menu-116).
/// 편집기의 군단 편집(<c>WarOfGenesis.Editor/LegionEditWindow</c>)과 같은 파일 <c>assets/data/legions/NNNN.json</c>(<see cref="LegionBook"/>)에 적는다.
/// </summary>
/// <remarks>
/// 왼쪽은 군단 목록(찾기 칸 · 「새 군단」 · 「새 군단 지우기」), 오른쪽은 고른 군단 — 이름(새 군단만) · 「처음부터 가진 군단」(새 군단만, 편집기와 같다) ·
/// 진형 여섯 · 모세스 용병관리처럼 진형 칸에 세운 그림 · 리더와 부하 여섯 칸. 칸(또는 진형 그림)을 누르면 인물 고르기 창(파트2 인물, 찾기 포함)이 뜬다.
/// 부하 보정(LP · PSY · DEP)과 군단기는 이 화면에서 안 다룬다 — 읽은 값을 그대로 다시 적는다(편집기에서 고친다).
/// 「저장」을 누르면 고친 군단만 파일에 적고(원본과 같아졌으면 파일을 지운다) 게임의 군단 표를 다시 읽는다(<see cref="MosesScene.ReloadLegions"/>) — 모세스 · 전투가 곧바로 새 값을 쓴다.
/// 부하는 For.dat 처럼 앞에서부터 채운 목록이라(빈 칸을 사이에 둘 수 없다) 빼면 뒤 부하가 한 칸씩 당겨진다.
/// </remarks>
internal sealed class LegionEditScreen(GameWindow host)
{
    internal const int MenuLegionEdit = 1102;   // 1101 · 1103 사이 빈 번호 — 1168 은 파트1 차례 테두리가 쓴다
    internal const int W = 632, H = 456, RowH = 20, ListX = 12, ListW = 200, ListY = 62, ListRows = 15;
    internal const int RightX = ListX + ListW + 12, RightW = W - RightX - 12;
    internal const int FormY = 112, FormW = 200, FormH = 212, SlotRowH = 30;
    private const uint Selected = 0xFF2A4A8A, Hover = 0xFF1C2E52, Gold = 0xFFFFE070, Cyan = 0xFF00FFFF;

    /// <summary>「새 군단」이 처음 갖는 이름 — 이름 칸을 누르면 비운 채 시작한다(뒤에 이어 쳐지지 않게).</summary>
    private const string NewName = "새 군단";

    private static readonly string[] FormationNames = ["학익진", "일자형", "십자형", "역학익진", "젓가락형", "이자형"];

    /// <summary>DUELDX_LEGIONEDIT 가 있으면 열어 둔 채 시작한다(화면 밖 시험용).</summary>
    internal bool _open = Environment.GetEnvironmentVariable("DUELDX_LEGIONEDIT") != null;

    private sealed class Row
    {
        public required LegionData Legion;
        public LegionData? Original;
        public bool Dirty;
        public bool IsCustom => Original == null;
        public bool Changed => Original == null || !LegionBook.SameContent(Legion, Original);
    }

    private List<Row>? _rows;
    private readonly HashSet<int> _deleted = [];
    private int _chosen = -1, _listTop;
    private bool _confirmClose;

    // ── 글자 칸 ── 군단 찾기 · 이름(새 군단) · 인물 찾기. 받는 칸은 하나 — 고르기 창이 떠 있으면 인물 찾기, 이름 칸을 눌렀으면 이름, 아니면 군단 찾기.
    private string _listQuery = "", _pickQuery = "", _nameText = "", _imeComp = "";
    private bool _nameFocus;

    // ── 인물 고르기 창 ── 고르는 칸: -1 리더 · 0~5 부하. null 이면 닫혀 있다.
    private int? _pickSlot;
    private int _pickTop;
    private List<(int Chr, string Name, string Title, string Info)>? _people;

    private readonly List<(int X, int Y, int W, int H, Action Act)> _hits = [], _pickHits = [];
    private (int X, int Y, int W, int H) _nameBox;

    /// <summary>글자를 받나 — 열려 있으면 늘(찾기 칸이 있다). 시험 단축키(W · T)가 안 돌게 키도 먼저 받는다.</summary>
    internal bool TextActive => _open;

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    internal void Toggle()
    {
        if (_open) { TryClose(); return; }
        _open = true;
        if (_rows != null && !_rows.Any(r => r.Dirty) && _deleted.Count == 0) _rows = null;   // 고친 것이 없으면 열 때마다 파일에서 다시(편집기에서 바꿨을 수 있다)
        (_pickSlot, _nameFocus, _imeComp, _confirmClose) = (null, false, "", false);
    }

    /// <summary>닫는다 — 저장 안 한 고침이 있으면 한 번은 알리고, 한 번 더 닫으면 버린다.</summary>
    private void TryClose()
    {
        CommitName();
        if (_rows != null && (_rows.Any(r => r.Dirty) || _deleted.Count > 0) && !_confirmClose)
        {
            _confirmClose = true;
            host.Toast("저장하지 않은 군단이 있습니다 — 「저장」을 누르거나, 한 번 더 닫으면 버립니다");
            return;
        }
        (_open, _pickSlot, _nameFocus, _confirmClose) = (false, null, false, false);
        if (_rows != null && (_rows.Any(r => r.Dirty) || _deleted.Count > 0)) { _rows = null; _deleted.Clear(); }
    }

    private List<Row> Rows()
    {
        if (_rows != null) return _rows;
        if (host._db is not { } db) return [];
        var original = LegionBook.LoadOriginal(db.Files);
        _rows = [.. LegionBook.LoadAll(db.Files).Values.OrderBy(l => l.Id).Select(l => new Row { Legion = l, Original = original.GetValueOrDefault(l.Id) })];
        _deleted.Clear();
        if (_rows.All(r => r.Legion.Id != _chosen)) _chosen = _rows.FirstOrDefault()?.Legion.Id ?? -1;
        return _rows;
    }

    private Row? Current => Rows().FirstOrDefault(r => r.Legion.Id == _chosen);

    private List<Row> ListView()
    {
        var db = host._db!;
        string q = (_nameFocus || _pickSlot != null ? _listQuery : _listQuery + _imeComp).Trim();
        return [.. Rows().Where(r => CharEditScreen.Matches(q, [db.LegionName(r.Legion)], [r.Legion.Id]))];
    }

    private void Choose(int id)
    {
        CommitName();
        _chosen = id;
        var view = ListView();
        int at = view.FindIndex(r => r.Legion.Id == id);
        if (at >= 0) _listTop = Math.Clamp(_listTop, Math.Max(0, at - ListRows + 1), at);
    }

    private void Set(Row row, LegionData next)
    {
        if (LegionBook.SameContent(next, row.Legion)) return;
        row.Legion = next;
        row.Dirty = true;
        _confirmClose = false;
    }

    /// <summary>이름 칸에 친 것을 군단에 넣는다 — 비우면 「군단 N」.</summary>
    private void CommitName()
    {
        if (!_nameFocus) return;
        _nameFocus = false;
        _imeComp = "";
        if (Current is { IsCustom: true } row)
            Set(row, row.Legion with { CustomName = _nameText.Trim() is { Length: > 0 } name ? name : $"군단 {row.Legion.Id}" });
    }

    /// <summary>칸에 인물을 넣는다(0 = 빼기). -1 은 리더(0 = 전투 자료대로), 0~5 는 부하 — 부하 목록은 앞에서부터라 빈 칸 뒤를 누르면 맨 뒤에 붙는다.</summary>
    private void SetSlot(int slot, int chr)
    {
        if (Current is not { } row) return;
        var l = row.Legion;
        if (slot < 0) { Set(row, l with { Leader = (ushort)chr }); return; }
        var members = l.Members.ToList();
        if (chr == 0) { if (slot < members.Count) members.RemoveAt(slot); }
        else if (slot < members.Count) members[slot] = (ushort)chr;
        else if (members.Count < 6) members.Add((ushort)chr);
        Set(row, l with { Members = [.. members] });
    }

    private void NewLegion()
    {
        CommitName();
        var rows = Rows();
        int id = Math.Max(LegionBook.FirstCustomId, rows.Select(r => r.Legion.Id).Concat(_deleted).DefaultIfEmpty(0).Max() + 1);
        var legion = new LegionData(id, 0, [], 0, 0, 0, 0, [], 0) { CustomName = NewName, Owned = true };
        rows.Add(new Row { Legion = legion, Original = null, Dirty = true });
        _deleted.Remove(id);
        _listQuery = "";
        _confirmClose = false;
        Choose(id);
        host.Toast($"새 군단 {id} 을(를) 만들었습니다 — 이름 · 부하를 고치고 「저장」을 누르세요");
    }

    private void DeleteLegion()
    {
        if (Current is not { IsCustom: true } row) return;
        var rows = Rows();
        int at = rows.IndexOf(row);
        rows.Remove(row);
        _deleted.Add(row.Legion.Id);
        _confirmClose = false;
        _nameFocus = false;
        _chosen = rows.Count > 0 ? rows[Math.Clamp(at, 0, rows.Count - 1)].Legion.Id : -1;
        host.Toast($"새 군단 {row.Legion.Id} 을(를) 지웠습니다 — 「저장」을 누르면 파일도 지웁니다");
    }

    private void Revert()
    {
        if (Current is not { Original: { } orig } row) return;
        Set(row, orig);
    }

    /// <summary>고친 군단을 파일에 적고(원본과 같으면 지우고) 게임의 군단 표를 다시 읽는다.</summary>
    private void Save()
    {
        CommitName();
        if (host._db is not { } db || _rows is not { } rows) return;
        var dirty = rows.Where(r => r.Dirty).ToList();
        if (dirty.Count == 0 && _deleted.Count == 0) { host.Toast("저장할 것이 없습니다"); return; }
        string folder = Path.Combine(AssetsFolder.Find("data"), LegionBook.Folder);
        try
        {
            Directory.CreateDirectory(folder);
            foreach (int id in _deleted)
                if (Path.Combine(folder, LegionBook.FileName(id)) is var gone && File.Exists(gone)) File.Delete(gone);
            foreach (var r in dirty)
            {
                string path = Path.Combine(folder, LegionBook.FileName(r.Legion.Id));
                if (!r.Changed) { if (File.Exists(path)) File.Delete(path); }
                else File.WriteAllText(path, LegionBook.ToJson(r.Legion, db.LegionName(r.Legion)), new UTF8Encoding(false));
                r.Dirty = false;
            }
            int deleted = _deleted.Count;
            _deleted.Clear();
            _confirmClose = false;
            host.Mos.ReloadLegions();
            host.Toast($"군단 {dirty.Count}개를 저장했습니다{(deleted > 0 ? $" · 새 군단 {deleted}개를 지웠습니다" : "")} — 모세스 · 전투가 곧바로 씁니다");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            host.Toast($"저장하지 못했습니다: {ex.Message}");
        }
    }

    // ── 인물 고르기 ──

    private List<(int Chr, string Name, string Title, string Info)> People()
    {
        if (_people != null) return _people;
        if (host._db is not { } db) return [];
        var list = new List<(int, string, string, string)>();
        for (int code = 1; code < 1000; code++)
            if (db.Character(code) is { } c && db.T(c.NameId) is { Length: > 0 } name && !name.StartsWith('=') && name != "없음")
            {
                string title = db.T(c.TitleId) is { Length: > 0 } t && t != "없음" ? t : "";
                string job = db.JobName(c) is { Length: > 0 } j && j != "없음" ? j : "";
                list.Add((code, name, title, string.Join("  ", new[] { job, c.Level is > 0 and < 100 ? $"Lv{c.Level}" : "" }.Where(t => t.Length > 0))));
            }
        return _people = list;
    }

    private string PickQuery => (_pickQuery + _imeComp).Trim();

    private List<(int Chr, string Name, string Title, string Info)> PickView()
    {
        string q = PickQuery;
        var view = People().Where(p => CharEditScreen.Matches(q, [p.Name, p.Title], [p.Chr])).ToList();
        if (q.Length == 0) view.Insert(0, (0, _pickSlot < 0 ? "(비우기 — 전투 자료대로)" : "(비우기)", "", ""));
        return view;
    }

    private void OpenPick(int slot)
    {
        CommitName();
        (_pickSlot, _pickTop, _pickQuery, _imeComp) = (slot, 0, "", "");
    }

    private static (int X, int Y, int W, int H) PickBox(int ex, int ey) => (ex + 12, ey + 34, W - 24, H - 84);
    private const int PickPreviewW = 150;
    private int PickRows => (PickBox(0, 0).H - 70) / RowH;

    // ── 입력 ──

    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        if (_pickSlot != null)
        {
            var (px, py, pw, ph) = PickBox(Origin().X, Origin().Y);
            if (bx < px || bx >= px + pw || by < py || by >= py + ph) { _pickSlot = null; return true; }
            foreach (var (x, y, w, h, act) in _pickHits)
                if (bx >= x && bx < x + w && by >= y && by < y + h) { act(); break; }
            return true;
        }
        var nb = _nameBox;
        bool onName = nb.W > 0 && bx >= nb.X && bx < nb.X + nb.W && by >= nb.Y && by < nb.Y + nb.H;
        if (!onName) CommitName();
        foreach (var (x, y, w, h, act) in _hits)
            if (bx >= x && bx < x + w && by >= y && by < y + h) { act(); break; }
        return true;
    }

    internal void OnWheel(int delta)
    {
        if (_pickSlot != null) _pickTop -= delta * 3;
        else _listTop -= delta * 3;   // 넘친 값은 그릴 때 자른다
    }

    /// <summary>다 된 글자 하나(WM_CHAR) — 받는 칸에 넣는다. Backspace 는 한 글자 지운다.</summary>
    internal void OnChar(char ch)
    {
        ref string text = ref (_pickSlot != null ? ref _pickQuery : ref (_nameFocus ? ref _nameText : ref _listQuery));
        int max = _nameFocus && _pickSlot == null ? 24 : 20;
        if (ch == '\b') { if (text.Length > 0) text = text[..^1]; }
        else if (ch >= ' ' && text.Length < max) text += ch;
        else return;
        if (_pickSlot != null) _pickTop = 0;
        else if (!_nameFocus) _listTop = 0;
    }

    /// <summary>한글 조합 중인 글자(WM_IME_COMPOSITION).</summary>
    internal void OnComposition(string text) => _imeComp = text;

    internal void OnKey(int key)
    {
        switch (key)
        {
            case Win32.VK_ESCAPE:
                if (_pickSlot != null) { if (_pickQuery.Length > 0 || _imeComp.Length > 0) (_pickQuery, _imeComp, _pickTop) = ("", "", 0); else _pickSlot = null; }
                else if (_nameFocus) CommitName();
                else if (_listQuery.Length > 0 || _imeComp.Length > 0) (_listQuery, _imeComp, _listTop) = ("", "", 0);
                else TryClose();
                break;
            case Win32.VK_RETURN:
                if (_nameFocus) CommitName();
                break;
            case Win32.VK_UP or Win32.VK_DOWN when _pickSlot == null && !_nameFocus && host._db != null:
                var view = ListView();
                int at = view.FindIndex(r => r.Legion.Id == _chosen);
                if (view.Count > 0) Choose(view[Math.Clamp(at + (key == Win32.VK_UP ? -1 : 1), 0, view.Count - 1)].Legion.Id);
                break;
        }
    }

    // ── 그리기 ──

    private bool Over(int x, int y, int w, int h) => host._mouse.X >= x && host._mouse.X < x + w && host._mouse.Y >= y && host._mouse.Y < y + h;

    private void Button(List<(int, int, int, int, Action)> hits, string label, int x, int y, int w, int h, bool on, Action? act, bool enabled = true)
    {
        bool here = enabled && Over(x, y, w, h);
        host.UiFill(x, y, w, h, here || on ? Selected : StatusScreen.BoxBg);
        host.UiStroke(x, y, w, h, StatusScreen.BoxLine);
        label = Fit(label, w - 6, 12);
        host.UiText(label, x + (w - host.UiGetText(label, White, 12).W) / 2, y + (h - 16) / 2, enabled ? White : DimGray, 12);
        if (enabled && act != null) hits.Add((x, y, w, h, act));
    }

    private string Fit(string text, int width, float size)
    {
        if (host.UiGetText(text, White, size).W <= width) return text;
        while (text.Length > 1 && host.UiGetText(text + "…", White, size).W > width) text = text[..^1];
        return text + "…";
    }

    /// <summary>글자 칸 — 친 글자와 조합 중인 글자(밑줄), 받는 칸이면 깜박이는 자리 표시. 비었으면 <paramref name="hint"/>.</summary>
    private void TextBox(int x, int y, int w, string text, bool focused, string hint)
    {
        host.UiFill(x, y, w, 20, StatusScreen.BoxBg);
        host.UiStroke(x, y, w, 20, focused ? Cyan : StatusScreen.BoxLine);
        int tx = x + 6;
        if (text.Length > 0) { string shown = Fit(text, w - 20, 12); host.UiText(shown, tx, y + 2, White, 12); tx += host.UiGetText(shown, White, 12).W; }
        if (focused && _imeComp.Length > 0)
        {
            int cw = host.UiGetText(_imeComp, White, 12).W;
            host.UiText(_imeComp, tx, y + 2, Cyan, 12);
            host.UiFill(tx, y + 17, cw, 1, Cyan);
            tx += cw;
        }
        if (focused && (int)(host._lastTime * 2) % 2 == 0) host.UiFill(tx + 1, y + 3, 1, 14, White);
        if (text.Length == 0 && !(focused && _imeComp.Length > 0)) host.UiText(hint, x + 10, y + 2, 0xFF5A6A8A, 12);
    }

    private string ChrName(GameDatabase db, int chr) => db.Character(chr) is { } c && db.T(c.NameId) is { Length: > 0 } n ? n : $"Chr {chr}";

    /// <summary>인물 그림(앞모습 서기, 모션 2)을 <paramref name="x"/>,<paramref name="y"/>(발 자리)에 — 모세스 용병관리와 같은 그림(<see cref="MosesScene.PreviewSprite"/>), 없으면 인물 레코드의 그림 Obs. 그렸으면 true.</summary>
    private bool DrawFigure(GameDatabase db, int chr, int x, int y)
    {
        if (chr <= 0) return false;
        if (host.Mos.PreviewSprite(chr)?.FrameOfMotion(2, 0, false) is { } f)
        {
            if (host._hiResUi) host.Hi.DrawImage(f.Px, f.W, f.W, f.H, x + f.X, y + f.Y);
            else host.BlitMasked(f.Px, f.W, f.H, x + f.X, y + f.Y);
            return true;
        }
        return db.Character(chr) is { SpriteId: > 0 and < ushort.MaxValue } c && host.UiSprite(c.SpriteId, ObsMotionTable.DirectionFront, 0, x, y, loop: false);
    }

    internal void Draw()
    {
        if (!_open) return;
        _hits.Clear();
        _pickHits.Clear();
        _nameBox = default;
        var (x, y) = Origin();
        host.UiFill(x - 4, y - 4, W + 8, H + 8, 0x80000000);
        host.UiFill(x, y, W, H, StatusScreen.PanelBg);
        host.UiStroke(x, y, W, H, StatusScreen.BoxLine);
        host.UiFill(x, y, W, 28, StatusScreen.HeadBg);
        host.UiText("군단 에디터 — 파트2 군단의 리더·부하·진형 (편집기와 같은 파일)", x + 10, y + 6, White);
        int fy = y + H - 40;
        Button(_hits, "닫기", x + W - 116, fy, 100, 28, false, TryClose);
        bool anyDirty = _rows != null && (_rows.Any(r => r.Dirty) || _deleted.Count > 0);
        Button(_hits, anyDirty ? "● 저장" : "저장", x + W - 246, fy, 120, 28, anyDirty, Save);
        host.UiText("Esc: 닫기 · ↑↓: 군단 · 휠: 목록", x + 16, fy + 7, DimGray, 12);
        if (host._db is not { } db) { host.UiText("아직 게임 자료를 읽지 못했습니다.", x + 20, y + 40, DimGray, 12); return; }

        // ── 왼쪽: 군단 목록 ──
        int lx = x + ListX;
        bool listFocus = !_nameFocus && _pickSlot == null;
        TextBox(lx, y + 36, ListW, _listQuery, listFocus, "찾기: 이름이나 번호");
        var view = ListView();
        _listTop = Math.Clamp(_listTop, 0, Math.Max(0, view.Count - ListRows));
        for (int i = 0; i < ListRows && _listTop + i < view.Count; i++)
        {
            var r = view[_listTop + i];
            int ry = y + ListY + i * RowH, id = r.Legion.Id;
            bool on = id == _chosen;
            if (on || Over(lx, ry, ListW, RowH)) host.UiFill(lx, ry, ListW, RowH - 1, on ? Selected : Hover);
            string no = $"{id}";
            host.UiText(no, lx + 30 - host.UiGetText(no, DimGray, 11).W, ry + 3, DimGray, 11);
            string tag = r.Dirty ? "●" : r.IsCustom ? "새" : r.Changed ? "고침" : "";
            int tagW = tag.Length > 0 ? host.UiGetText(tag, Gold, 10).W + 4 : 0;
            host.UiText(Fit(db.LegionName(r.Legion), ListW - 40 - tagW, 12), lx + 36, ry + 2, on ? Cyan : White, 12);
            if (tagW > 0) host.UiText(tag, lx + ListW - tagW, ry + 4, r.Dirty ? Gold : DimGray, 10);
            _hits.Add((lx, ry, ListW, RowH, () => Choose(id)));
        }
        string count = view.Count == 0 ? "맞는 군단이 없습니다" : $"{_listTop + 1}–{Math.Min(view.Count, _listTop + ListRows)} / {view.Count}";
        host.UiText(count, lx, y + ListY + ListRows * RowH + 1, DimGray, 10);
        var cur = Current;
        Button(_hits, "새 군단", lx, y + 380, 96, 24, false, NewLegion);
        Button(_hits, "새 군단 지우기", lx + 100, y + 380, ListW - 100, 24, false, DeleteLegion, enabled: cur is { IsCustom: true });
        host.UiFill(x + RightX - 7, y + 36, 1, H - 84, StatusScreen.BoxLine);

        if (cur == null) { host.UiText("군단을 고르세요.", x + RightX, y + 40, DimGray, 12); DrawPick(db, x, y); return; }
        var l = cur.Legion;
        int rx = x + RightX;

        // ── 오른쪽 머리: 번호 · 이름 · 상태 ──
        string state = cur.Dirty ? "● 저장 안 함" : cur.IsCustom ? "새 군단(원본에 없는 번호)" : cur.Changed ? "원본과 다름" : "원본 그대로";
        int stateW = host.UiGetText(state, DimGray, 11).W;
        host.UiText(Fit($"#{l.Id}  {db.LegionName(l)}", RightW - stateW - 10, 15), rx, y + 34, Gold, 15);
        host.UiText(state, rx + RightW - stateW, y + 38, cur.Dirty ? Gold : DimGray, 11);

        if (cur.IsCustom)
        {
            host.UiText("이름", rx, y + 62, DimGray, 12);
            _nameBox = (rx + 34, y + 60, 184, 20);
            TextBox(rx + 34, y + 60, 184, _nameFocus ? _nameText : l.CustomName ?? "", _nameFocus, "이름을 치세요");
            _hits.Add((rx + 34, y + 60, 184, 20, () => { if (!_nameFocus) (_nameFocus, _nameText, _imeComp) = (true, Current?.Legion.CustomName is { } n && n != NewName ? n : "", ""); }));
            Button(_hits, l.Owned ? "처음부터 가진 군단: 켬" : "처음부터 가진 군단: 끔", rx + 226, y + 59, RightW - 226, 22, l.Owned,
                   () => { if (Current is { } row) Set(row, row.Legion with { Owned = !row.Legion.Owned }); });
        }
        else
        {
            host.UiText("이름 · 설명은 원본 글 — 여기서는 못 바꾼다", rx, y + 62, DimGray, 12);
            Button(_hits, "원본으로", rx + RightW - 100, y + 59, 100, 22, false, Revert, enabled: cur.Changed);
        }

        // ── 진형 ──
        int fw = (RightW + 4) / FormationNames.Length;
        for (int f = 0; f < FormationNames.Length; f++)
        {
            byte form = (byte)f;
            Button(_hits, FormationNames[f], rx + f * fw, y + 85, fw - 4, 22, l.Formation == f,
                   () => { if (Current is { } row) Set(row, row.Legion with { Formation = form }); });
        }

        // ── 진형 그림 — 모세스 용병관리처럼 가운데가 리더, 칸 40×32, 아랫줄이 앞에 ──
        int bx = rx, by = y + FormY, cx = bx + FormW / 2, cy = by + 128;
        host.UiFill(bx, by, FormW, FormH, 0xFF0A1226);
        host.UiStroke(bx, by, FormW, FormH, StatusScreen.BoxLine);
        var cells = LegionData.FormationCells[Math.Clamp((int)l.Formation, 0, 5)];
        var spots = new List<(int Slot, int Dx, int Dy, int Chr)> { (-1, 0, 0, l.Leader) };
        for (int i = 0; i < 6; i++) spots.Add((i, cells[i].Dx, cells[i].Dy, i < l.Members.Length ? l.Members[i] : 0));
        int hoverSlot = int.MinValue;
        foreach (var (slot, dx, dy, _) in spots)
            if (Over(cx + 40 * dx - 20, cy + 32 * dy - 16, 40, 32)) hoverSlot = slot;
        for (int i = 0; i < 7; i++)
            if (Over(rx + FormW + 8, by + i * SlotRowH, RightW - FormW - 8, SlotRowH - 4)) hoverSlot = i - 1;
        foreach (var (slot, dx, dy, chr) in spots)
        {
            int sx = cx + 40 * dx, sy = cy + 32 * dy;
            host.UiFill(sx - 18, sy - 6, 36, 14, slot == hoverSlot ? Selected : 0xFF16223E);
            host.UiStroke(sx - 18, sy - 6, 36, 14, slot < 0 ? Gold : StatusScreen.BoxLine);
            int s = slot;
            _hits.Add((sx - 20, sy - 16, 40, 32, () => OpenPick(s)));
        }
        foreach (var (slot, dx, dy, chr) in spots.OrderBy(p => p.Dy))
        {
            int sx = cx + 40 * dx, sy = cy + 32 * dy;
            if (!DrawFigure(db, chr, sx, sy + 6) && (chr > 0 || slot < 0))
            {
                string label = chr > 0 ? ChrName(db, chr) : "리더";
                if (label.Length > 4) label = label[..4];
                host.UiText(label, sx - host.UiGetText(label, White, 10).W / 2, sy - 6, slot < 0 ? Gold : chr > 0 ? White : DimGray, 10);
            }
        }
        foreach (var (slot, dx, dy, _) in spots)
        {
            string mark = slot < 0 ? "★" : $"{slot + 1}";
            host.UiText(mark, cx + 40 * dx - 17, cy + 32 * dy - 6, slot < 0 ? Gold : Cyan, 9);
        }
        host.UiText($"진형: {FormationNames[Math.Clamp((int)l.Formation, 0, 5)]}", bx + 6, by + 4, DimGray, 11);

        // ── 칸 일곱 — 리더 · 부하 1~6. 이름을 누르면 고르기 창, × 는 빼기 ──
        int sxl = rx + FormW + 8, slotW = RightW - FormW - 8;
        for (int i = 0; i < 7; i++)
        {
            int slot = i - 1, ry = by + i * SlotRowH;
            int chr = slot < 0 ? l.Leader : slot < l.Members.Length ? l.Members[slot] : 0;
            bool next = slot == l.Members.Length;    // 부하를 넣을 다음 칸
            host.UiText(slot < 0 ? "리더" : $"{slot + 1}", sxl, ry + 5, slot < 0 ? Gold : DimGray, 12);
            string label = chr > 0 ? $"{ChrName(db, chr)}  #{chr}" : slot < 0 ? "(전투 자료대로)" : next ? "＋ 넣기" : "(빈 칸)";
            int nameW = slotW - 34 - (chr > 0 ? 28 : 0);
            bool here = Over(sxl + 30, ry, nameW, 24) || hoverSlot == slot;
            host.UiFill(sxl + 30, ry, nameW, 24, here ? Selected : StatusScreen.BoxBg);
            host.UiStroke(sxl + 30, ry, nameW, 24, StatusScreen.BoxLine);
            host.UiText(Fit(label, nameW - 10, 12), sxl + 36, ry + 4, chr > 0 ? White : next || slot < 0 ? DimGray : 0xFF5A6A8A, 12);
            int s = slot;
            _hits.Add((sxl + 30, ry, nameW, 24, () => OpenPick(s)));
            if (chr > 0) Button(_hits, "×", sxl + slotW - 26, ry, 26, 24, false, () => SetSlot(s, 0));
        }

        // ── 아래 도움말 ──
        int hy = by + FormH + 6;
        host.UiText("칸이나 진형 그림을 누르면 인물 고르기 · × 는 빼기", rx, hy, DimGray, 11);
        host.UiText("부하는 앞에서부터 — 빼면 뒤 부하가 한 칸씩 당겨진다", rx, hy + 15, DimGray, 11);
        host.UiText("리더를 비우면 전투 자료의 대장 그대로(아군 군단 대장은 안 바뀐다)", rx, hy + 30, DimGray, 11);
        host.UiText(Fit($"보정 LP {l.LpBonus} · PSY {l.PsyBonus} · DEP {l.DepBonus} · 군단기 {l.Skills.Length}개 — 편집기에서 고친다", RightW, 11), rx, hy + 45, DimGray, 11);
        if (cur.IsCustom && db.LegionDescription(l) is { Length: > 0 } desc)
            host.UiText(Fit($"설명: {desc.Replace("$n", " ")}", RightW, 11), rx, hy + 60, DimGray, 11);

        DrawPick(db, x, y);
    }

    /// <summary>인물 고르기 창 — 창 안쪽을 거의 다 덮는다. 찾기 칸 · 줄 목록 · 오른쪽에 마우스가 얹힌 인물(없으면 지금 칸의 인물) 그림.</summary>
    private void DrawPick(GameDatabase db, int ex, int ey)
    {
        if (_pickSlot is not int slot || Current is not { } cur) return;
        var (x, y, w, h) = PickBox(ex, ey);
        host.UiFill(x - 3, y - 3, w + 6, h + 6, 0xA0000000);
        host.UiFill(x, y, w, h, 0xFF0A1226);
        host.UiFill(x, y, w, h, StatusScreen.PanelBg);
        host.UiStroke(x, y, w, h, StatusScreen.BoxLine);
        int current = slot < 0 ? cur.Legion.Leader : slot < cur.Legion.Members.Length ? cur.Legion.Members[slot] : 0;
        host.UiText($"{(slot < 0 ? "리더" : $"부하 {slot + 1}번 칸")} 고르기 — 줄을 누르면 넣는다", x + 10, y + 8, White, 12);
        Button(_pickHits, "닫기", x + w - 70, y + 5, 60, 22, false, () => _pickSlot = null);
        var view = PickView();
        int rows = PickRows, listW = w - 20 - PickPreviewW - 8;
        _pickTop = Math.Clamp(_pickTop, 0, Math.Max(0, view.Count - rows));
        TextBox(x + 10, y + 34, 220, _pickQuery, true, "이름 · 칭호 · 번호");
        string range = view.Count == 0 ? "없음" : $"{_pickTop + 1}–{Math.Min(view.Count, _pickTop + rows)} / {view.Count}";
        host.UiText(range, x + 10 + listW - host.UiGetText(range, DimGray, 12).W, y + 37, DimGray, 12);
        int hover = current;
        for (int i = 0; i < rows && _pickTop + i < view.Count; i++)
        {
            var p = view[_pickTop + i];
            int ry = y + 62 + i * RowH;
            bool here = Over(x + 6, ry, listW + 4, RowH), on = p.Chr == current;
            if (here) hover = p.Chr;
            if (here || on) host.UiFill(x + 6, ry, listW + 4, RowH - 1, here ? Selected : Hover);
            if (p.Chr != 0)
            {
                string no = $"#{p.Chr}";
                host.UiText(no, x + 52 - host.UiGetText(no, DimGray, 12).W, ry + 2, DimGray, 12);
            }
            int infoW = host.UiGetText(p.Info, DimGray, 11).W;
            string name = p.Title.Length > 0 ? $"{p.Name} · {p.Title}" : p.Name;
            host.UiText(Fit(name, listW - 70 - infoW, 12), x + 60, ry + 2, on ? Cyan : White, 12);
            host.UiText(p.Info, x + 10 + listW - infoW, ry + 3, DimGray, 11);
            int chr = p.Chr;
            _pickHits.Add((x + 6, ry, listW + 4, RowH, () => { SetSlot(slot, chr); _pickSlot = null; }));
        }
        // 미리 보기
        int px = x + w - 10 - PickPreviewW, py = y + 34, ph = h - 44;
        host.UiFill(px, py, PickPreviewW, ph, StatusScreen.BoxBg);
        host.UiStroke(px, py, PickPreviewW, ph, StatusScreen.BoxLine);
        if (hover > 0)
        {
            if (!DrawFigure(db, hover, px + PickPreviewW / 2, py + ph / 2 + 40)) host.UiText("그림 없음", px + 44, py + ph / 2, DimGray, 12);
            string caption = Fit($"{ChrName(db, hover)}  #{hover}", PickPreviewW - 8, 12);
            host.UiText(caption, px + (PickPreviewW - host.UiGetText(caption, White, 12).W) / 2, py + ph - 22, White, 12);
        }
    }
}
