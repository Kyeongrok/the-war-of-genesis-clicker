using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 전직 트리(T, 사용자 요청 — 원본에 없다). 가운데에 한 직업, 왼쪽에 그 직업으로 오는 직업, 오른쪽에 그 직업에서 갈 다음 직업,
/// 아래에 짚은(없으면 가운데) 직업의 전직 조건 · 가르치는 어빌리티와 지금 바꿀 수 있는 직업.
/// </summary>
/// <remarks>
/// <para>전투에서 차례인 내 인물(노란 역삼각형)이 있으면 그 인물 · 그 직업을 가운데에 둔다. 없거나 챕터 지도 · 일행 창이면 일행 첫 사람 —
/// ←/→ 나 왼쪽 이름 목록으로 사람을 바꾼다. 직업 칸을 누르면 그 직업을 가운데로, 가운데를 누르면 그 인물의 지금 직업으로 돌아온다.</para>
/// <para>지금 바꿀 수 있는가는 원본 규칙(<c>0x1009e6f0</c>, <see cref="Part1StatusPanel"/> 의 직업 목록과 같다) — Job.dat 조건 짝 다섯이
/// 모두 「그 어빌리티를 그 레벨(최소 1) 이상」. 직업은 레벨을 오르며 그 직업이 가르치는 어빌리티를 최대치까지 올리므로, 트리의 잇기는
/// 「C 에서 B 로 간다」= B 의 조건 무게(레벨 합)가 C 보다 크고, B 의 조건 가운데 능력치(어빌리티 1~9 — 어느 직업에서나 오른다)가 아닌 것을
/// C 가 가르치는 최대치 · C 의 조건 · 그 인물의 지금 레벨 가운데 큰 것으로 채울 수 있을 때다. 이렇게 하면 슈미터 → 에스코트 · 레인져 · 해적 →
/// 투사 → 어절트 슈미터 · 수크라 · 카빌라카 → 티아마타 같은 줄이 나온다. Job.dat 머리 (A, B) 의 A 는 소속(상태 창 이름표 21개)이다.</para>
/// </remarks>
internal sealed class Part1JobTree(GameWindow host)
{
    internal bool _open;

    private IReadOnlyList<Part1Job>? _jobs;
    private IReadOnlyList<Part1Ability>? _abilities;

    /// <summary>보는 사람들(Chr 번호)과 고른 사람, 가운데 직업.</summary>
    private List<int> _members = [];
    private int _member;
    private int _center;
    private readonly Dictionary<int, Part1Chr?> _chrs = [];

    private readonly List<(int X, int Y, int W, int H, Action Act)> _hits = [];

    private const int StatAbilityMax = 9, NodeGap = 6, ListW = 100, DetailH = 150, ChipsH = 40, MaxSide = 7;
    private const uint NodeBg = 0xFF242C52, NodeHot = 0xFF34407A, Green = 0xFF78E078, Yellow = 0xFFFFE070, Gold = 0xFFFFC840,
                       Dim = 0xFF8890B0, Line = 0xFF6070A8, Blue = 0xFFB8D0FF;

    // ── 열기 · 닫기 ──

    /// <summary>파트1 전투 판 위인가(모세스 · 필드 · 파트1 화면이 덮지 않았다).</summary>
    private bool InBattle => host._battleLoaded && host._scene.IsPart1 && !host.Mos._mosesOpen && !host.FieldOpen && !host.Part1TitleScr._open;

    /// <summary>파트1 챕터 지도(대사가 없을 때) · 일행 창.</summary>
    private bool OnMap => host.Part1TitleScr._open
                          && (host.Part1TitleScr.Chapter._open && host.Part1TitleScr.Chapter.Events.Talk == null || host.Part1TitleScr.Party._open);

    /// <summary>T — 파트1 전투 · 챕터 지도면 열고(열려 있으면 닫고) true. 그 밖(파트2 …)에서는 false 라 키가 원래 하던 일을 한다.</summary>
    internal bool Toggle()
    {
        if (_open) { Close(); return true; }
        if (!InBattle && !OnMap) return false;
        _jobs ??= Part1Job.LoadAll();
        _abilities ??= Part1Ability.LoadAll();
        _chrs.Clear();
        _members = Members(out int focus);
        if (_members.Count == 0 || _jobs.Count == 0) { host.Toast("전직 트리를 볼 인물이 없습니다"); return true; }
        _open = true;
        Select(focus);
        return true;
    }

    internal void Close() => _open = false;

    /// <summary>보는 사람들 — 전투는 내 편 파트1 인물(차례인 사람이 있으면 그 사람을 고른다), 지도는 나갈 팀 · 무리 · 일행.</summary>
    private List<int> Members(out int focus)
    {
        focus = 0;
        if (InBattle)
        {
            var list = host._units.Where(u => u.Alive && u.PlayerControlled && u.ChrCode >= Part1Game.ChrBase).Select(u => u.ChrCode).Distinct().ToList();
            int turn = host.Btl._turn;
            if (host.Btl.IsPlayerTurn && (uint)turn < host._units.Length && host._units[turn].ChrCode >= Part1Game.ChrBase)
            {
                int code = host._units[turn].ChrCode;
                if (!list.Contains(code)) list.Insert(0, code);
                focus = list.IndexOf(code);
            }
            return list;
        }
        var members = host.Part1.ActiveMembers();
        if (members.Count == 0) members = host.Part1.GroupMembers();
        if (members.Count == 0) members = host.Part1.Party;
        return [.. members.Where(c => Chr(c) != null).Distinct()];
    }

    private Part1Chr? Chr(int code)
    {
        if (_chrs.TryGetValue(code, out var chr)) return chr;
        return _chrs[code] = Part1Chr.Load(code - Part1Game.ChrBase) is { } raw ? host.Part1.Apply(raw) : null;
    }

    private Part1Chr? Focused => (uint)_member < _members.Count ? Chr(_members[_member]) : null;

    private void Select(int index)
    {
        if (_members.Count == 0) return;
        _member = (index % _members.Count + _members.Count) % _members.Count;
        _center = Focused is { } chr && Part1Job.ById(_jobs!, chr.Fields[7]) != null ? chr.Fields[7] : 1;
    }

    internal void OnKey(int key)
    {
        if (key == Win32.VK_ESCAPE || host._keys.ActionFor(key) == KeyAction.JobTree) Close();
        else if (key is Win32.VK_LEFT or Win32.VK_UP) Select(_member - 1);
        else if (key is Win32.VK_RIGHT or Win32.VK_DOWN or Win32.VK_TAB) Select(_member + 1);
        else if (key is Win32.VK_BACK or Win32.VK_RETURN) Select(_member);   // 지금 직업으로
    }

    /// <summary>열려 있으면 클릭을 먹는다 — 칸이면 그 일, 창 밖이면 닫기.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        foreach (var (x, y, w, h, act) in _hits)
            if (bx >= x && bx < x + w && by >= y && by < y + h) { act(); return true; }
        var (wx, wy, ww, wh) = Window();
        if (bx < wx || by < wy - GameWindow.FrameTitleH || bx >= wx + ww || by >= wy + wh) Close();
        return true;
    }

    // ── 직업 셈 ──

    private Part1Job? Job(int id) => Part1Job.ById(_jobs!, id);

    private static IEnumerable<(int Ability, int Need)> Needs(Part1Job job) => job.Firsts.Where(f => f.A > 0).Select(f => (f.A, Math.Max(1, f.B)));

    private static int Weight(Part1Job job) => Needs(job).Sum(n => n.Need);

    private static int Teaches(Part1Job job, int ability) => job.Abilities.Where(a => a.Ability == ability).Select(a => a.Level).DefaultIfEmpty(0).Max();

    private static int Level(Part1Chr? chr, int ability) => chr?.AbilityLevel(ability) ?? -1;

    /// <summary>원본 규칙 — 조건 어빌리티가 모두 그 레벨 이상이면 지금 바꿀 수 있다.</summary>
    private static bool CanChange(Part1Chr? chr, Part1Job job) => chr != null && job.Firsts.Any(f => f.A > 0) && Needs(job).All(n => chr.AbilityLevel(n.Ability) >= n.Need);

    /// <summary>from 에서 to 로 이어지나 — to 의 능력치 아닌 조건을 from 이 가르치는 최대 · from 의 조건 · 인물의 지금 레벨로 채운다.</summary>
    private static bool Leads(Part1Job from, Part1Job to, Part1Chr? chr)
    {
        if (from.Id == to.Id || !to.Firsts.Any(f => f.A > 0) || Weight(to) <= Weight(from)) return false;
        var own = Needs(from).ToDictionary(n => n.Ability, n => n.Need);
        foreach (var (ability, need) in Needs(to))
        {
            if (ability <= StatAbilityMax) continue;
            if (Math.Max(Math.Max(Teaches(from, ability), own.GetValueOrDefault(ability)), Level(chr, ability)) < need) return false;
        }
        return true;
    }

    private List<Part1Job> NextOf(Part1Job job, Part1Chr? chr) =>
        [.. _jobs!.Where(b => Leads(job, b, chr)).OrderBy(Weight).ThenBy(b => b.Id).Take(MaxSide)];

    private List<Part1Job> PrevOf(Part1Job job) =>
        [.. _jobs!.Where(p => Leads(p, job, null)).OrderByDescending(Weight).ThenBy(p => p.Id).Take(MaxSide)];

    // ── 그리기 ──

    private (int X, int Y, int W, int H) Window()
    {
        int w = Math.Min(720, host.ViewWidth - 16), h = Math.Min(500, host.ViewHeight - GameWindow.FrameTitleH - 16);
        return (host._camX + (host.ViewWidth - w) / 2, host._camY + (host.ViewHeight - h + GameWindow.FrameTitleH) / 2, w, h);
    }

    private bool Hot(int x, int y, int w, int h) => host._mouse.X >= x && host._mouse.X < x + w && host._mouse.Y >= y && host._mouse.Y < y + h;

    private int TextW(string text, float size) => host.UiGetText(text, GameWindow.White, size).W;

    private string AbilityName(int id) => Part1Ability.ById(_abilities!, id)?.Name ?? $"#{id}";

    private void Icon(int ability, int x, int y)
    {
        if (Part1Ability.ById(_abilities!, ability) is not { } info) return;
        if (host.UiFor(Part1Game.ObsBase + 11)?.FrameAt(info.Tail + 0x13, 0) is { } icon)
            host.UiSprite(Part1Game.ObsBase + 11, info.Tail + 0x13, 0, x - icon.X, y - icon.Y);
    }

    /// <summary>조건 하나의 꼴 — 채웠으면 초록 「이름 n」, 아니면 노랑 「이름 지금/필요」.</summary>
    private (string Text, uint Color) NeedLabel(Part1Chr? chr, int ability, int need)
    {
        int now = Math.Max(0, Level(chr, ability));
        if (chr == null) return ($"{AbilityName(ability)} {need}", GameWindow.White);
        return now >= need ? ($"{AbilityName(ability)} {need}", Green) : ($"{AbilityName(ability)} {now}/{need}", Yellow);
    }

    /// <summary>조건을 몇 개 채웠나에 따른 테 색 — 다 = 초록, 일부 = 노랑, 하나도 = 흐림.</summary>
    private uint StateColor(Part1Chr? chr, Part1Job job)
    {
        if (chr == null) return Line;
        var needs = Needs(job).ToList();
        int met = needs.Count(n => chr.AbilityLevel(n.Ability) >= n.Need);
        return needs.Count > 0 && met == needs.Count ? Green : met > 0 ? Yellow : Dim;
    }

    private void Elbow(int x0, int y0, int x1, int y1, uint color)
    {
        int mid = (x0 + x1) / 2;
        host.UiFill(Math.Min(x0, mid), y0, Math.Abs(mid - x0) + 1, 2, color);
        host.UiFill(mid, Math.Min(y0, y1), 2, Math.Abs(y1 - y0) + 2, color);
        host.UiFill(Math.Min(mid, x1), y1, Math.Abs(x1 - mid) + 1, 2, color);
    }

    /// <summary>다음 직업 칸 높이 — 이름 줄 + 조건 글이 칸 너비로 접힌 줄 수.</summary>
    private int NextHeight(Part1Job job, Part1Chr? chr, int w)
    {
        int lines = 1, x = 0;
        foreach (var (ability, need) in Needs(job))
        {
            int cw = TextW(NeedLabel(chr, ability, need).Text, 10) + 8;
            if (x > 0 && x + cw > w - 12) { lines++; x = 0; }
            x += cw;
        }
        return 20 + lines * 13 + 4;
    }

    internal void Draw()
    {
        if (!_open) return;
        if (!InBattle && !OnMap) { _open = false; return; }
        _hits.Clear();
        var (x, y, W, H) = Window();
        var chr = Focused;
        var center = Job(_center) ?? _jobs![0];
        int current = chr?.Fields[7] ?? 0;

        host.UiFill(x, y - GameWindow.FrameTitleH, W, H + GameWindow.FrameTitleH, 0xFF1C2246);   // 틀 바탕이 비치지 않게 먼저 깐다
        host.UiFrame(x, y, W, H, chr != null ? $"전직 트리 — {chr.Name}" : "전직 트리");

        // 왼쪽 — 사람 목록.
        host.UiText("인물  ←/→", x + 10, y + 8, Dim, 10);
        for (int i = 0; i < _members.Count; i++)
        {
            int ry = y + 24 + i * 20;
            if (ry + 20 > y + H - 30) break;
            var c = Chr(_members[i]);
            bool hot = Hot(x + 6, ry, ListW, 19);
            if (i == _member) host.UiFill(x + 6, ry, ListW, 19, 0xFF2A4A8A);
            else if (hot) host.UiFill(x + 6, ry, ListW, 19, 0x60A090E0);
            host.UiText(c?.Name ?? $"#{_members[i]}", x + 12, ry + 3, i == _member ? Gold : GameWindow.White, 11);
            int index = i;
            _hits.Add((x + 6, ry, ListW, 19, () => Select(index)));
        }

        // 가운데 · 양옆 자리.
        int areaX = x + ListW + 16, areaR = x + W - 10, areaY = y + 10, areaB = y + H - DetailH - ChipsH - 22;
        const int Gap = 28;
        int prevW = 128, centerW = 170, nextW = areaR - areaX - prevW - centerW - 2 * Gap;
        int prevX = areaX, centerX = prevX + prevW + Gap, nextX = centerX + centerW + Gap;
        host.UiText("이 직업으로 오는 직업", prevX, areaY, Dim, 10);
        host.UiText("다음 직업", nextX, areaY, Dim, 10);
        int top = areaY + 16, space = areaB - top;

        var prev = PrevOf(center);
        var next = NextOf(center, chr);
        var needs = Needs(center).ToList();
        int centerH = 64 + needs.Count * 15;
        int centerY = top + Math.Max(0, (space - centerH) / 2);

        // 칸 자리를 먼저 잡아 짚은 칸을 안다.
        var nodes = new List<(Part1Job Job, int X, int Y, int W, int H, int Side)>();
        const int PrevH = 34;
        int prevTotal = prev.Count * (PrevH + NodeGap) - NodeGap;
        int py = top + Math.Max(0, (space - prevTotal) / 2);
        foreach (var p in prev) { nodes.Add((p, prevX, py, prevW, PrevH, -1)); py += PrevH + NodeGap; }
        var nextHeights = next.Select(n => NextHeight(n, chr, nextW)).ToList();
        int gap = NodeGap, nextTotal = nextHeights.Sum() + gap * Math.Max(0, next.Count - 1);
        if (nextTotal > space && next.Count > 1) { gap = Math.Max(1, (space - nextHeights.Sum()) / (next.Count - 1)); nextTotal = nextHeights.Sum() + gap * (next.Count - 1); }
        int ny = top + Math.Max(0, (space - nextTotal) / 2);
        for (int i = 0; i < next.Count; i++) { nodes.Add((next[i], nextX, ny, nextW, nextHeights[i], 1)); ny += nextHeights[i] + gap; }

        Part1Job? hover = null;
        foreach (var n in nodes) if (Hot(n.X, n.Y, n.W, n.H)) hover = n.Job;
        bool centerHot = Hot(centerX, centerY, centerW, centerH);
        if (centerHot) hover = center;

        // 잇는 줄 — 옆 칸 가운데에서 가운데 칸으로.
        foreach (var n in nodes)
        {
            uint color = n.Job == hover ? Blue : Line;
            if (n.Side < 0) Elbow(n.X + n.W, n.Y + n.H / 2, centerX, centerY + 18, color);
            else Elbow(centerX + centerW, centerY + 18, n.X, n.Y + Math.Min(n.H / 2, 10), n.Job == hover ? Blue : StateColor(chr, n.Job) == Green ? Green : Line);
        }

        // 가운데 칸 — 이름 · 영어 이름 · 소속 · 조건(지금/필요).
        host.UiFill(centerX, centerY, centerW, centerH, centerHot ? NodeHot : 0xFF2C3868);
        host.UiStroke(centerX, centerY, centerW, centerH, center.Id == current ? Gold : StateColor(chr, center));
        host.UiStroke(centerX + 1, centerY + 1, centerW - 2, centerH - 2, center.Id == current ? Gold : StateColor(chr, center));
        host.UiText(center.Name, centerX + 8, centerY + 6, Gold, 14);
        host.UiText(center.EnglishName, centerX + 8, centerY + 26, Dim, 10);
        string group = center.Head.A >= 0 && center.Head.A < Part1StatusPanel.Affiliations.Length ? Part1StatusPanel.Affiliations[center.Head.A] : "";
        string tag = center.Id == current ? "지금 직업" : CanChange(chr, center) ? "전직 가능" : "";
        host.UiText(group, centerX + 8, centerY + 40, 0xFFB0C0D8, 10);
        if (tag.Length > 0) host.UiRightText(tag, centerX + centerW - 8, centerY + 40, center.Id == current ? Gold : Green, 10);
        for (int i = 0; i < needs.Count; i++)
        {
            var (label, color) = NeedLabel(chr, needs[i].Ability, needs[i].Need);
            Icon(needs[i].Ability, centerX + 8, centerY + 58 + i * 15);
            host.UiText(label, centerX + 26, centerY + 58 + i * 15, color, 11);
        }
        if (needs.Count == 0) host.UiText("조건 없음", centerX + 8, centerY + 58, Dim, 11);
        _hits.Add((centerX, centerY, centerW, centerH, () => Select(_member)));

        // 옆 칸들.
        foreach (var (job, nx, nyy, nw, nh, side) in nodes)
        {
            bool hot = job == hover;
            host.UiFill(nx, nyy, nw, nh, hot ? NodeHot : NodeBg);
            host.UiStroke(nx, nyy, nw, nh, job.Id == current ? Gold : StateColor(chr, job));
            host.UiText(job.Name, nx + 6, nyy + 4, job.Id == current ? Gold : GameWindow.White, 12);
            if (job.Id == current) host.UiRightText("지금", nx + nw - 6, nyy + 5, Gold, 10);
            else if (CanChange(chr, job)) host.UiRightText("가능", nx + nw - 6, nyy + 5, Green, 10);
            if (side < 0)
            {
                string g = job.Head.A >= 0 && job.Head.A < Part1StatusPanel.Affiliations.Length ? Part1StatusPanel.Affiliations[job.Head.A] : "";
                host.UiText(g, nx + 6, nyy + 19, Dim, 9);
            }
            else
            {
                int cx = nx + 6, cy = nyy + 20;
                foreach (var (ability, need) in Needs(job))
                {
                    var (label, color) = NeedLabel(chr, ability, need);
                    int cw = TextW(label, 10) + 8;
                    if (cx > nx + 6 && cx + cw > nx + nw - 6) { cx = nx + 6; cy += 13; }
                    host.UiText(label, cx, cy, color, 10);
                    cx += cw;
                }
            }
            int id = job.Id;
            _hits.Add((nx, nyy, nw, nh, () => _center = id));
        }
        if (prev.Count == 0) host.UiText("(첫 직업 · 따로 오는 길)", prevX, top + space / 2 - 6, Dim, 10);
        if (next.Count == 0) host.UiText("(이어지는 직업 없음)", nextX, top + space / 2 - 6, Dim, 10);

        // 지금 바꿀 수 있는 직업 — 원본 규칙(0x1009e6f0) 그대로.
        int chipsY = areaB + 6;
        host.UiFill(areaX, chipsY - 3, areaR - areaX, 1, Line);
        host.UiText("지금 바꿀 수 있는 직업:", areaX, chipsY + 2, Dim, 10);
        int chipX = areaX + TextW("지금 바꿀 수 있는 직업:", 10) + 8, chipY = chipsY;
        var allowed = chr == null ? [] : _jobs!.Where(j => j.Id != current && CanChange(chr, j)).ToList();
        if (allowed.Count == 0) host.UiText("없음", chipX, chipY + 2, Dim, 10);
        foreach (var job in allowed)
        {
            int cw = TextW(job.Name, 10) + 12;
            if (chipX + cw > areaR) { chipX = areaX + 8; chipY += 18; }
            if (chipY + 16 > chipsY + ChipsH) break;
            bool hot = Hot(chipX, chipY, cw, 16);
            if (hot) hover = job;
            host.UiFill(chipX, chipY, cw, 16, hot ? NodeHot : job.Id == _center ? 0xFF2A4A8A : NodeBg);
            host.UiStroke(chipX, chipY, cw, 16, Green);
            host.UiText(job.Name, chipX + 6, chipY + 2, GameWindow.White, 10);
            int id = job.Id;
            _hits.Add((chipX, chipY, cw, 16, () => _center = id));
            chipX += cw + 4;
        }

        DrawDetail(hover ?? center, chr, areaX, y + H - DetailH - 22, areaR - areaX, DetailH);
        host.UiText("직업을 누르면 그 직업을 가운데로 · 가운데를 누르면 지금 직업 · Esc/T/오른쪽 단추: 닫기", x + 10, y + H - 17, Dim, 9);
    }

    /// <summary>아래 칸 — 그 직업의 전직 조건과 그 조건을 가르치는 직업, 그 직업이 가르치는 어빌리티(지금/최대).</summary>
    private void DrawDetail(Part1Job job, Part1Chr? chr, int x, int y, int w, int h)
    {
        host.UiFill(x, y, w, h, 0xFF141A38);
        host.UiStroke(x, y, w, h, Line);
        int half = Math.Min(w / 2, 280);
        host.UiText($"{job.Name} — 전직 조건", x + 8, y + 6, Gold, 11);
        int row = y + 24;
        foreach (var (ability, need) in Needs(job))
        {
            var (label, color) = NeedLabel(chr, ability, need);
            Icon(ability, x + 8, row);
            host.UiText(label, x + 26, row, color, 11);
            // 그 어빌리티를 그 레벨까지 가르치는 직업(조건이 가벼운 차례로 셋) — 능력치는 거의 모든 직업이 가르쳐 뺀다.
            if (ability > StatAbilityMax)
            {
                var teachers = _jobs!.Where(j => j.Id != job.Id && Teaches(j, ability) >= need).OrderBy(Weight).Select(j => j.Name).Distinct().ToList();
                int fx = x + 26 + Math.Max(90, TextW(label, 11) + 8), room = x + half - 6 - fx;
                string from = "← 가르치는 직업 없음";
                for (int k = Math.Min(3, teachers.Count); k >= 1; k--)
                {
                    from = "← " + string.Join(", ", teachers.Take(k)) + (teachers.Count > k ? $" 외 {teachers.Count - k}" : "");
                    if (TextW(from, 9) <= room) break;
                }
                host.UiText(from, fx, row + 1, Dim, 9);
            }
            row += 17;
        }
        if (!job.Firsts.Any(f => f.A > 0)) host.UiText("조건 없음", x + 8, row, Dim, 11);

        int tx = x + half + 8;
        host.UiFill(x + half, y + 6, 1, h - 12, Line);
        host.UiText("배울 수 있는 어빌리티  (지금/최대)", tx, y + 6, Gold, 11);
        var teach = job.Abilities.Where(a => a.Ability > 0).GroupBy(a => a.Ability).Select(g => (Ability: g.Key, Max: g.Max(a => a.Level))).ToList();
        int colW = (x + w - tx - 4) / 2, rows = (h - 30) / 16;
        for (int i = 0; i < teach.Count && i < rows * 2; i++)
        {
            var (ability, max) = teach[i];
            int cx = tx + i / rows * colW, cy = y + 24 + i % rows * 16;
            int now = Math.Max(0, Level(chr, ability));
            Icon(ability, cx, cy);
            host.UiText(AbilityName(ability), cx + 18, cy, now > 0 ? GameWindow.White : 0xFFA0A8C0, 10);
            string lv = chr == null ? $"{max}" : $"{now}/{max}";
            host.UiRightText(lv, cx + colW - 6, cy, chr == null ? GameWindow.White : now >= max ? Green : now > 0 ? Yellow : Dim, 10);
        }
        if (teach.Count == 0) host.UiText("없음", tx, y + 24, Dim, 10);
    }
}
