using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 도구 &gt; 체질별 어빌리티 — 체질(계열 · 형)을 고르면 거기서 배우는 어빌리티가 나오고, 어빌리티를 고르면 레벨별 효과 수치를 본다(사용자 요청, 원본에 없다).
/// </summary>
/// <remarks>
/// 계열 · 형은 편집기의 「체질」 창과 같은 묶음이다 — <c>Dep.dat</c> 계열 f 의 레코드 3f+1 · 3f+2 · 3f+3 이 1 · 2 · 3단계(<see cref="JobBook"/>).
/// 효과 글은 Status 설명 창과 같은 것(<see cref="StatusScreen.WorkEffect"/> · <see cref="StatusScreen.TargetText"/>)이라 스킬 자료를 고치면 저절로 따라온다 —
/// 피해(위력) · 회복(최대 HP %) · 상태이상 · 능력치 보정 · 상태이상 회복이 한 칸에 같이 적힌다.
/// 비용(TP · SOUL · HP)은 체질 비용이 체질마다 달라, 아래 체질 단추로 고른 체질 기준으로 셈한다(<see cref="GameDatabase.WorkCostsFor"/>).
/// </remarks>
internal sealed unsafe class SkillBookScreen(GameWindow host)
{
    internal const int MenuSkillBook = 1176;
    internal const int W = 624, H = 456, RowH = 20, LineH = 16, TabY = 36, FormY = 64, InfoY = 91, ListY = 124, ListW = 196;
    internal const int ListRows = (H - 50 - ListY) / RowH;
    internal const int DetailX = 12 + ListW + 12, DetailW = W - DetailX - 12, DetailY = ListY + 40, DetailRows = (H - 50 - DetailY) / LineH;
    private const uint Selected = 0xFF2A4A8A, Gold = 0xFFFFE070, Blue = 0xFFB8D8FF;

    /// <summary>표의 숫자 칸 — TP · SOUL · HP · EXP. 모든 레벨이 0 인 칸은 빼고 그 자리를 효과 칸에 준다.</summary>
    private static readonly (string Label, int Width)[] CostColumns = [("TP", 38), ("SOUL", 56), ("HP", 36), ("EXP", 40)];
    private const int LevelW = 26, DemoW = 104;

    /// <summary>어빌리티 한 줄 — 꼬리표는 「2단계만」(다른 체질이 이 계열로 갈아탔을 때만) · 「1단계만」.</summary>
    private sealed record Entry(AbilityData Ab, string Tag);

    /// <summary>형 하나(1 · 2단계가 같이 쓴다) 또는 3단계 직업 하나.</summary>
    private sealed record Column(string Label, string Line1, string Line2, List<Entry> Abilities);

    /// <summary>계열 — <paramref name="Body"/> 는 그 계열이 제 것인 체질(1 에텔 … 5 메텔).</summary>
    private sealed record Family(string Name, int Body, List<Column> Columns);

    /// <summary>설명 칸의 한 줄 — <paramref name="Cells"/> 가 있으면 표의 줄(Lv · TP · SOUL · HP · EXP)이고 글은 효과 칸이다.</summary>
    private sealed record Line(string Text, uint Color, string[]? Cells = null, int Level = 0);

    internal bool _open = Environment.GetEnvironmentVariable("DUELDX_SKILLBOOK") != null;
    private int _family, _column, _ability, _body = 1, _listTop, _detailTop;

    /// <summary>표에서 고른 레벨 — 「시연」이 이 레벨로 쓴다.</summary>
    private int _level = 1;
    private List<Family>? _families;
    private List<Line>? _lines;
    private bool[] _shown = new bool[CostColumns.Length];
    private readonly List<(int X, int Y, int W, int H, Action Act)> _hits = [];

    /// <summary>DUELDX_SKILLBOOK=계열,형,어빌리티(0부터) 면 그 칸을 고른 채 열어 둔다(화면 밖 시험용).</summary>
    private string? _startAt = Environment.GetEnvironmentVariable("DUELDX_SKILLBOOK");

    internal void Toggle()
    {
        _open = !_open;
        // 개발 창에서 스킬 · 체질 자료를 다시 읽었을 수 있다 — 열 때마다 새로 만든다.
        (_families, _lines) = (null, null);
    }

    /// <summary>시연이 끝나 돌아왔을 때 — 고르던 자리 그대로 다시 연다.</summary>
    internal void Reopen() => (_open, _lines) = (true, null);

    /// <summary>「시연」 — 되면 창을 내리고(끝나면 <see cref="Reopen"/>), 안 되면 까닭을 알린다.</summary>
    private void StartDemo(AbilityData ab)
    {
        if (host.Btl.StartSkillDemo(ab, _level) is { } why) host.Toast(why);
        else _open = false;
    }

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    private static bool Named(GameDatabase db, ushort id) => id != 0 && db.T(id) is { Length: > 0 } name && name != "없음";

    /// <summary>직업 이름 — 체질마다 이름 칸이 있고 못 가는 칸은 TXR 「없음」이다. 빈 칸을 빼고 「/」로 잇는다.</summary>
    private static string JobNames(GameDatabase db, JobData job)
    {
        var names = job.NamesByBody.Skip(1).Where(n => Named(db, n)).Select(db.T).Distinct().ToList();
        return names.Count > 0 ? string.Join("/", names) : $"직업 {job.Id}";
    }

    private List<Family> Families()
    {
        if (_families != null) return _families;
        var list = new List<Family>();
        if (host._db is not { } db) return list;
        List<AbilityData> Slots(JobData job) =>
            [.. job.AbilityList.Where(a => a != 0).Select(a => db.Abilities.GetValueOrDefault(a)).OfType<AbilityData>().Distinct()];
        for (int f = 0; f < 5; f++)
        {
            DepData? d1 = db.Deps.FirstOrDefault(d => d.Id == 3 * f + 1), d2 = db.Deps.FirstOrDefault(d => d.Id == 3 * f + 2),
                     d3 = db.Deps.FirstOrDefault(d => d.Id == 3 * f + 3);
            if (d1 == null) continue;
            var columns = new List<Column>();
            int body = 0;
            for (int k = 0; k < JobBook.Forms.Length && k < d1.Jobs.Length; k++)
            {
                if (!db.Jobs.TryGetValue(d1.Jobs[k], out var j1)) continue;
                var j2 = d2 != null && k < d2.Jobs.Length ? db.Jobs.GetValueOrDefault(d2.Jobs[k]) : null;
                if (body == 0) body = Math.Max(0, Array.FindIndex(j1.NamesByBody, 1, n => Named(db, n)));
                var first = Slots(j1);
                var second = j2 != null ? Slots(j2) : first;
                var entries = first.Select(a => new Entry(a, second.Contains(a) ? "" : "1단계만")).ToList();
                entries.AddRange(second.Where(a => !first.Contains(a)).Select(a => new Entry(a, "2단계만")));
                columns.Add(new Column(JobBook.Forms[k], $"1단계 「{JobNames(db, j1)}」 · Lv{j1.NeedLevel}부터",
                    j2 != null ? $"2단계 「{JobNames(db, j2)}」 · Lv{j2.NeedLevel}부터 (다른 체질이 이 계열로 전직)" : "", entries));
            }
            foreach (ushort id in d3?.Jobs ?? [])
            {
                if (id == 0 || !db.Jobs.TryGetValue(id, out var j3)) continue;
                string need = j3.NeedAbility != 0 && db.Abilities.TryGetValue(j3.NeedAbility, out var key) ? $"전직 조건: 「{db.T(key.NameId)}」 을(를) 배운 인물" : "";
                columns.Add(new Column(JobNames(db, j3), $"3단계(궁극) 「{JobNames(db, j3)}」 · Lv{j3.NeedLevel}부터", need,
                    [.. Slots(j3).Select(a => new Entry(a, ""))]));
            }
            if (columns.Count > 0) list.Add(new Family(db.T(d1.NameId), body, columns));
        }
        _families = list;
        if (list.Count > 0)
        {
            int[] at = [.. (_startAt ?? "").Split(',').Select(s => int.TryParse(s, out int n) ? n : 0)];
            if (_startAt != null) (_family, _column, _ability, _startAt) = (at.ElementAtOrDefault(0), at.ElementAtOrDefault(1), at.ElementAtOrDefault(2), null);
            _family = Math.Clamp(_family, 0, list.Count - 1);
            _column = Math.Clamp(_column, 0, list[_family].Columns.Count - 1);
            _ability = Math.Clamp(_ability, 0, Math.Max(0, list[_family].Columns[_column].Abilities.Count - 1));
            _body = Math.Max(1, list[_family].Body);
        }
        return list;
    }

    private Column? Current() => Families() is { Count: > 0 } all ? all[_family].Columns[_column] : null;

    private void PickFamily(int index)
    {
        var all = Families();
        if ((uint)index >= all.Count) return;
        (_family, _column, _body) = (index, Math.Clamp(_column, 0, all[index].Columns.Count - 1), Math.Max(1, all[index].Body));
        PickAbility(0);
    }

    private void PickColumn(int index)
    {
        if (Families() is not { Count: > 0 } all) return;
        _column = Math.Clamp(index, 0, all[_family].Columns.Count - 1);
        PickAbility(0);
    }

    private void PickAbility(int index)
    {
        int count = Current()?.Abilities.Count ?? 0;
        _ability = Math.Clamp(index, 0, Math.Max(0, count - 1));
        _listTop = Math.Clamp(_listTop, Math.Max(0, _ability - ListRows + 1), _ability);
        (_detailTop, _lines, _level) = (0, null, 1);
    }

    /// <summary>고른 어빌리티의 설명 칸 — 설명글 · 대상 · 레벨별 표. 글자 너비를 재는 일이라 고를 때 한 번만 만든다.</summary>
    private List<Line> Lines()
    {
        if (_lines != null) return _lines;
        var lines = new List<Line>();
        if (host._db is not { } db || Current() is not { } column || (uint)_ability >= column.Abilities.Count) return lines;
        var ab = column.Abilities[_ability].Ab;
        void Wrap(string text, uint color) { foreach (string l in host.Mos.WrapText(text, DetailW - 8, 12f)) lines.Add(new Line(l, color)); }

        Wrap(db.AbilityDescription(ab) is { Length: > 0 } desc ? desc : "(설명 없음)", White);
        var levels = ab.WorkByLevel.Keys.Where(k => k >= 1 && (ab.MaxLevel == 0 || k <= ab.MaxLevel)).Order()
                       .Select(k => (Level: k, Work: host.Btl.Work(ab.WorkByLevel[k]))).Where(p => p.Work != null).Select(p => (p.Level, Work: p.Work!)).ToList();
        lines.Add(new Line("", White));
        if (levels.Count == 0) { lines.Add(new Line("레벨별 자료가 없습니다.", DimGray)); return _lines = lines; }

        var w1 = levels[0].Work;
        bool battle = ab.Category is 1 or 2;
        bool damage = battle && w1.IsDamage && ab.Id is not (BattleScene.BlackHoleAbility or BattleScene.EncourageAbility);
        // 사거리만 레벨마다 달라지는 것이 흔하다 — 그때는 줄마다 효과 칸에 붙여 적고, 위 한 줄은 Lv1 것이다.
        bool rangeVaries = !ab.IsPassive && levels.Any(p => p.Work.RangeMax != w1.RangeMax);
        bool reachVaries = !ab.IsPassive && levels.Any(p => StatusScreen.TargetText(p.Work) != StatusScreen.TargetText(w1));
        if (ab.IsPassive) Wrap("장착하고 있는 동안 늘 적용됩니다.", Blue);
        else Wrap(StatusScreen.TargetText(w1) + (reachVaries ? " (Lv1)" : ""), Blue);
        if (damage)
            Wrap("피해 = ATK × (200 + 위력) ÷ 200 — 상대 방어(RDP)를 넣기 전"
                 + (levels.All(p => p.Work.Accuracy == w1.Accuracy && p.Work.Critical == w1.Critical) ? $" · 명중 바탕 {w1.Accuracy} · 치명 {w1.Critical + 1}%" : ""), DimGray);

        var costs = levels.Select(p => db.WorkCostsFor(_body, p.Work)).ToList();
        _shown = [costs.Any(c => c.Tp != 0), costs.Any(c => c.SoulNeed != 0 || c.Soul != 0), costs.Any(c => c.Hp != 0), levels.Any(p => p.Work.ExpCost != 0)];
        int effectW = DetailW - 8 - LevelW - CostColumns.Where((_, i) => _shown[i]).Sum(c => c.Width);
        // 모든 레벨이 똑같은 효과(「맞힌 대상마다 내 SOUL +10」 따위)는 표 위에 한 번만 적는다 — 줄마다 되풀이하면 표가 길어진다.
        var effects = levels.Select(p => host.StatusScr.WorkEffect(p.Work).Split(" · ", StringSplitOptions.RemoveEmptyEntries)).ToList();
        var always = levels.Count > 1 ? effects[0].Where(bit => effects.All(e => e.Contains(bit))).ToList() : [];
        if (always.Count > 0) Wrap("모든 레벨: " + string.Join(" · ", always), White);
        lines.Add(new Line("", White));
        lines.Add(new Line("효과", DimGray, ["Lv", .. CostColumns.Select(c => c.Label)]));
        for (int i = 0; i < levels.Count; i++)
        {
            var (level, w) = levels[i];
            var before = levels[Math.Max(0, i - 1)].Work;
            // 사거리 말고 다른 것(대상 · 범위 모양과 크기 · 최소 사거리)이 달라지는 레벨은 그 앞에 한 줄로 알린다.
            if (!ab.IsPassive && StatusScreen.TargetText(w with { RangeMax = before.RangeMax }) != StatusScreen.TargetText(before))
                Wrap($"Lv{level}부터 {StatusScreen.TargetText(w)}", Blue);
            var bits = new List<string>();
            // 위력이 0 이하인 피해 기술은 효과 글에 위력이 안 나온다 — 표에서는 줄마다 적어 둔다.
            if (damage && w.Power <= 0) bits.Add($"위력 {w.Power}");
            bits.AddRange(effects[i].Where(bit => !always.Contains(bit)));
            if (rangeVaries) bits.Add($"사거리 {w.RangeMax}칸");
            string effect = string.Join(" · ", bits);
            var c = costs[i];
            string[] cells = [$"{level}", Number(c.Tp), c.SoulNeed == c.Soul ? Number(c.Soul) : $"{c.SoulNeed}/{c.Soul}", Number(c.Hp), Number(w.ExpCost)];
            var wrapped = WrapBits(effect.Length > 0 ? effect : "—", effectW);
            for (int n = 0; n < wrapped.Count; n++) lines.Add(new Line(wrapped[n], White, n == 0 ? cells : ["", "", "", "", ""], level));
        }
        lines.Add(new Line("", White));
        if (_shown[3]) Wrap("EXP — 그 레벨로 올리는(Lv1 은 배우는) 데 드는 EXP.", DimGray);
        if (costs.Any(c => c.SoulNeed != c.Soul)) Wrap("SOUL a/b — a 만큼 있어야 쓸 수 있고, 쓰면 b 만큼 빠집니다.", DimGray);
        if (ab.Category == 1 && levels.Count > 1) Wrap("레벨 줄을 누르면 그 레벨로 시연합니다.", DimGray);
        if (levels.Any(p => p.Work.HpFactor != 0)) Wrap($"TP · SOUL · HP 는 {db.BodyName((byte)_body)} 기준 — 체질마다 다릅니다(아래 단추).", DimGray);
        return _lines = lines;

        static string Number(int value) => value == 0 ? "·" : value.ToString();
    }

    /// <summary>효과 글을 「 · 」 마디에서 접는다 — 글자 수로만 접으면 「사|거리」처럼 낱말이 갈린다. 한 마디가 칸보다 길 때만 글자로 접는다.</summary>
    private List<string> WrapBits(string text, int width)
    {
        var lines = new List<string>();
        string line = "";
        foreach (string bit in text.Split(" · "))
        {
            string joined = line.Length == 0 ? bit : line + " · " + bit;
            if (host.GetText(joined, White, 12f).W <= width) { line = joined; continue; }
            if (line.Length > 0) lines.Add(line);
            var parts = host.Mos.WrapText(bit, width, 12f);
            lines.AddRange(parts.Take(parts.Count - 1));
            line = parts.Count > 0 ? parts[^1] : "";
        }
        if (line.Length > 0 || lines.Count == 0) lines.Add(line);
        return lines;
    }

    /// <summary>열려 있으면 클릭을 먹는다 — 누르는 칸은 그릴 때 적어 둔 것이다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        foreach (var (x, y, w, h, act) in _hits)
            if (bx >= x && bx < x + w && by >= y && by < y + h) { act(); break; }
        return true;
    }

    /// <summary>마우스가 어빌리티 목록 위면 목록을, 아니면 설명 칸을 굴린다.</summary>
    internal void OnWheel(int delta)
    {
        var (x, _) = Origin();
        if (host._mouse.X < x + DetailX - 6) _listTop = Math.Clamp(_listTop - delta, 0, Math.Max(0, (Current()?.Abilities.Count ?? 0) - ListRows));
        else _detailTop = Math.Clamp(_detailTop - 2 * delta, 0, Math.Max(0, Lines().Count - DetailRows));
    }

    internal void OnKey(int key)
    {
        switch (key)
        {
            case Win32.VK_ESCAPE: _open = false; break;
            case Win32.VK_UP: PickAbility(_ability - 1); break;
            case Win32.VK_DOWN: PickAbility(_ability + 1); break;
            case Win32.VK_LEFT: PickColumn(_column - 1); break;
            case Win32.VK_RIGHT: PickColumn(_column + 1); break;
        }
    }

    /// <summary>단추 하나 — 글을 가운데에 놓고(넘치면 줄여서) 누르는 칸을 적는다.</summary>
    private void Button(string label, int x, int y, int w, int h, bool on, Action act)
    {
        host.UiFill(x, y, w, h, on ? Selected : StatusScreen.BoxBg);
        host.UiStroke(x, y, w, h, StatusScreen.BoxLine);
        label = Fit(label, w - 6, 12);
        host.UiText(label, x + (w - host.UiGetText(label, White, 12).W) / 2, y + (h - 16) / 2, White, 12);
        _hits.Add((x, y, w, h, act));
    }

    private string Fit(string text, int width, float size)
    {
        if (host.UiGetText(text, White, size).W <= width) return text;
        while (text.Length > 1 && host.UiGetText(text + "…", White, size).W > width) text = text[..^1];
        return text + "…";
    }

    internal void Draw()
    {
        if (!_open) return;
        _hits.Clear();
        var (x, y) = Origin();
        host.UiFill(x - 4, y - 4, W + 8, H + 8, 0x80000000);
        host.UiFill(x, y, W, H, StatusScreen.PanelBg);
        host.UiStroke(x, y, W, H, StatusScreen.BoxLine);
        host.UiFill(x, y, W, 28, StatusScreen.HeadBg);
        host.UiText("체질별 어빌리티 — 레벨별 효과", x + 10, y + 6, White);
        const string keys = "Esc: 닫기 · ↑↓: 어빌리티 · ←→: 형 · 휠: 굴리기";
        host.UiText(keys, x + W - 10 - host.UiGetText(keys, DimGray, 11).W, y + 8, DimGray, 11);

        int fy = y + H - 40;
        Button("닫기", x + W - 116, fy, 100, 28, false, () => _open = false);
        var families = Families();
        if (host._db is not { } db || families.Count == 0)
        {
            host.UiText("아직 게임 자료를 읽지 못했습니다.", x + 20, y + TabY + 6, DimGray, 12);
            return;
        }

        // 계열 → 형(1 · 2단계 공통)과 3단계 직업.
        int tabW = (W - 24) / families.Count;
        for (int i = 0; i < families.Count; i++)
        {
            int index = i;
            Button(families[i].Name, x + 12 + i * tabW, y + TabY, tabW - 4, 24, i == _family, () => PickFamily(index));
        }
        var columns = families[_family].Columns;
        int formW = (W - 24) / columns.Count;
        for (int i = 0; i < columns.Count; i++)
        {
            int index = i;
            Button(columns[i].Label, x + 12 + i * formW, y + FormY, formW - 4, 22, i == _column, () => PickColumn(index));
        }
        var column = columns[_column];
        host.UiText(Fit(column.Line1, W - 32, 11), x + 16, y + InfoY, DimGray, 11);
        host.UiText(Fit(column.Line2, W - 32, 11), x + 16, y + InfoY + 14, DimGray, 11);
        host.UiFill(x + 12, y + ListY - 5, W - 24, 1, StatusScreen.BoxLine);

        // 왼쪽: 그 형이 배우는 어빌리티(차례가 곧 「획득할 수 있는 어빌리티」 차례다).
        _listTop = Math.Clamp(_listTop, 0, Math.Max(0, column.Abilities.Count - ListRows));
        if (column.Abilities.Count == 0) host.UiText("배우는 어빌리티가 없습니다.", x + 16, y + ListY + 2, DimGray, 12);
        for (int i = 0; i < ListRows && _listTop + i < column.Abilities.Count; i++)
        {
            int index = _listTop + i, ry = y + ListY + i * RowH;
            var (ab, stage) = column.Abilities[index];
            if (index == _ability) host.UiFill(x + 12, ry - 1, ListW, RowH, Selected);
            string tag = string.Join(" · ", new[] { ab.Category switch { 0 => "비전투", 2 => "군단기", 3 => "패시브", _ => "" }, stage }.Where(t => t.Length > 0));
            int tagW = tag.Length > 0 ? host.UiGetText(tag, DimGray, 10).W + 6 : 0;
            host.UiText(Fit(db.T(ab.NameId) is { Length: > 0 } name ? name : $"어빌리티 {ab.Id}", ListW - 10 - tagW, 12), x + 18, ry + 1, White, 12);
            if (tagW > 0) host.UiText(tag, x + 12 + ListW - tagW, ry + 3, index == _ability ? White : DimGray, 10);
            _hits.Add((x + 12, ry - 1, ListW, RowH, () => PickAbility(index)));
        }
        if (column.Abilities.Count > ListRows)
            host.UiText($"{_listTop + 1}~{Math.Min(column.Abilities.Count, _listTop + ListRows)} / {column.Abilities.Count}", x + 16, y + H - 50 - 2, DimGray, 10);
        host.UiFill(x + DetailX - 6, y + ListY, 1, H - 50 - ListY, StatusScreen.BoxLine);

        // 오른쪽: 고른 어빌리티 — 이름 · 선행 조건, 그 아래는 굴리는 칸(설명글 · 대상 · 레벨별 표).
        int dx = x + DetailX;
        if ((uint)_ability < column.Abilities.Count)
        {
            var ab = column.Abilities[_ability].Ab;
            // 전투에서 직접 쓰는 어빌리티는 시연해 볼 수 있다 — 표에서 고른 레벨로(BattleScene.SkillDemo.cs).
            bool canDemo = ab.Category == 1;
            if (canDemo) Button($"▶ 시연 Lv{_level}", x + W - 12 - DemoW, y + ListY - 3, DemoW, 24, false, () => StartDemo(ab));
            host.UiText(Fit(db.T(ab.NameId), DetailW - (canDemo ? DemoW + 8 : 0), 15), dx, y + ListY - 1, Gold, 15);
            var meta = new List<string> { $"최대 Lv{ab.MaxLevel}" };
            foreach (var (pre, level) in new[] { (ab.Prereq1, ab.Prereq1Level), (ab.Prereq2, ab.Prereq2Level) })
                if (pre != 0 && db.Abilities.TryGetValue(pre, out var need)) meta.Add($"선행 {db.T(need.NameId)} Lv{level}");
            host.UiText(Fit(string.Join(" · ", meta), DetailW, 11), dx, y + ListY + 21, DimGray, 11);

            var lines = Lines();
            _detailTop = Math.Clamp(_detailTop, 0, Math.Max(0, lines.Count - DetailRows));
            for (int i = 0; i < DetailRows && _detailTop + i < lines.Count; i++)
            {
                var line = lines[_detailTop + i];
                int ly = y + DetailY + i * LineH;
                if (line.Cells is not { } cells) { host.UiText(line.Text, dx, ly, line.Color, 12); continue; }
                if (line.Level > 0)
                {
                    // 레벨 줄을 누르면 그 레벨을 고른다 — 「시연」이 그 레벨로 쓴다.
                    int pick = line.Level;
                    if (pick == _level) host.UiFill(dx - 3, ly, DetailW - 6, LineH, Selected);
                    _hits.Add((dx - 3, ly, DetailW - 6, LineH, () => _level = pick));
                }
                host.UiText(cells[0], dx, ly, line.Color == White ? Gold : line.Color, 12);
                host.UiText(line.Text, dx + LevelW, ly, line.Color, 12);
                int cx = dx + DetailW - 8 - CostColumns.Where((_, k) => _shown[k]).Sum(c => c.Width);
                for (int k = 0; k < CostColumns.Length; k++)
                {
                    if (!_shown[k]) continue;
                    host.UiText(cells[k + 1], cx + 4, ly, line.Color, 12);
                    cx += CostColumns[k].Width;
                }
            }
            if (lines.Count > DetailRows)
            {
                // 굴릴 것이 남았으면 오른쪽 끝에 가는 막대로 어디쯤인지 보인다.
                int trackH = DetailRows * LineH, thumbH = Math.Max(12, trackH * DetailRows / lines.Count);
                host.UiFill(x + W - 10, y + DetailY, 3, trackH, StatusScreen.BoxBg);
                host.UiFill(x + W - 10, y + DetailY + (trackH - thumbH) * _detailTop / (lines.Count - DetailRows), 3, thumbH, DimGray);
            }
        }

        // 비용을 셈할 체질 — 같은 기술도 체질마다 TP · SOUL · HP 로 나뉘는 몫이 다르다.
        host.UiText("비용 기준", x + 16, fy + 7, DimGray, 12);
        for (int b = 1; b <= 5; b++)
        {
            int body = b;
            Button(db.BodyName((byte)b), x + 78 + (b - 1) * 82, fy + 2, 78, 24, b == _body, () => (_body, _lines) = (body, null));
        }
    }
}
