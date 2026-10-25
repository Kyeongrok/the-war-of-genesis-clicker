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
/// <para>여러 단계 경로(사용자 요청): 조건을 한 직업이 다 가르치지 않는 직업(티아마타 · 시반 블레이드)은 그 인물의 지금 직업 · 레벨에서 가장 적게 바꾸는 길을
/// 찾아(<see cref="Search"/>) 아래 「경로」 줄에 칸마다 올릴 것과 함께 보이고, 바로 오는 직업이 없으면 그 길의 마지막 직업을 왼쪽에, 그런 직업을 오른쪽에 흐리게 둔다.</para>
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

    private const int StatAbilityMax = 9, NodeGap = 6, ListW = 100, DetailH = 150, ChipsH = 40, RouteH = 38, MaxSide = 7;
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
        _routes.Clear();
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

    /// <summary>
    /// from 에서 to 로 이어지나 — to 의 조건을 모두(HP · SKILL 같은 능력치 어빌리티도) from 이 가르치는 최대 · from 의 조건 · 인물의 지금 레벨로 채울 수 있어야 한다.
    /// 전에는 능력치 조건을 빼고 봐서 파일럿(HP 최대 3)에서 스페셜 오피서(HP 5)로 바로 이었다(사용자 지적 — 오피서를 거쳐야 한다).
    /// </summary>
    private static bool Leads(Part1Job from, Part1Job to, Part1Chr? chr)
    {
        if (from.Id == to.Id || !to.Firsts.Any(f => f.A > 0)) return false;
        // 소속이 다른 직업(마하즈리다 → 무슬림)은 무게를 안 보고, from 이 가르치는 능력치 아닌 조건 어빌리티가 있을 때만 잇는다(사용자 요청).
        // 같은 소속 안에서는 예전처럼 조건이 더 무거운 쪽으로만 잇는다.
        bool cross = from.Head.A != to.Head.A;
        if (cross ? !Needs(to).Any(n => n.Ability > StatAbilityMax && Teaches(from, n.Ability) > 0) : Weight(to) <= Weight(from)) return false;
        var own = Needs(from).ToDictionary(n => n.Ability, n => n.Need);
        foreach (var (ability, need) in Needs(to))
        {
            if (Math.Max(Math.Max(Teaches(from, ability), own.GetValueOrDefault(ability)), Level(chr, ability)) < need) return false;
        }
        return true;
    }

    /// <summary>
    /// 그 인물이 지금 직업에서 갈 수 있는 다음 직업과, 직업마다 아직 모자란 조건(어빌리티, 지금, 필요) — 레벨업 고르기 창 옆에 보인다.
    /// 지금 바로 바꿀 수 있는 직업은 모자란 것이 빈 목록이다.
    /// </summary>
    internal static List<(Part1Job Job, List<(int Ability, int Now, int Need)> Missing)> NextJobsFor(Part1Chr chr)
    {
        var jobs = Part1Job.LoadAll();
        if (Part1Job.ById(jobs, chr.Fields[7]) is not { } now) return [];
        return [.. jobs.Where(b => Leads(now, b, chr) || (b.Id != now.Id && CanChange(chr, b)))
                       .OrderBy(b => Needs(b).Count(n => chr.AbilityLevel(n.Ability) < n.Need)).ThenBy(Weight)
                       .Select(b => (b, Needs(b).Where(n => chr.AbilityLevel(n.Ability) < n.Need)
                                               .Select(n => (n.Ability, Math.Max(0, chr.AbilityLevel(n.Ability)), n.Need)).ToList()))
                       .Take(MaxSide)];
    }

    private List<Part1Job> NextOf(Part1Job job, Part1Chr? chr) =>
        [.. _jobs!.Where(b => Leads(job, b, chr)).OrderBy(Weight).ThenBy(b => b.Id).Take(MaxSide)];

    private List<Part1Job> PrevOf(Part1Job job) =>
        [.. _jobs!.Where(p => Leads(p, job, null)).OrderByDescending(Weight).ThenBy(p => p.Id).Take(MaxSide)];

    // ── 여러 단계 경로 ──

    private sealed class RouteSet
    {
        internal Dictionary<int, List<Part1Job>> All = [];                                       // 한 번에 찾은 길(목표 → 거칠 직업)
        internal readonly Dictionary<int, List<(Part1Job Job, List<(int Ability, int Level)> Raise)>?> Built = [];
    }

    /// <summary>사람마다 경로 셈(창을 열 때 비운다).</summary>
    private readonly Dictionary<int, RouteSet> _routes = [];

    private RouteSet RoutesOf(Part1Chr chr)
    {
        if (!_routes.TryGetValue(chr.Code, out var set))
        {
            _routes[chr.Code] = set = new RouteSet();
            if (Part1Job.ById(_jobs!, chr.Fields[7]) is { } now) set.All = Search(_jobs!, chr, now, null);
        }
        return set;
    }

    /// <summary>지금 직업에서 target 까지 — [지금 직업, 거칠 직업…] 과 칸마다 「거기서 올릴 것」. target 은 넣지 않는다. null = 경로 없음.</summary>
    private List<(Part1Job Job, List<(int Ability, int Level)> Raise)>? RouteTo(Part1Chr chr, Part1Job target)
    {
        var set = RoutesOf(chr);
        if (set.Built.TryGetValue(target.Id, out var built)) return built;
        List<(Part1Job, List<(int, int)>)>? result = null;
        if (Part1Job.ById(_jobs!, chr.Fields[7]) is { } now && target.Id != now.Id && target.Firsts.Any(f => f.A > 0))
        {
            // 모두 찾기에서 넓이를 잘라 놓쳤으면 그 목표 하나만 다시 찾는다.
            var path = set.All.GetValueOrDefault(target.Id) ?? Search(_jobs!, chr, now, target).GetValueOrDefault(target.Id);
            if (path != null) result = Steps(chr, [now, .. path], target);
        }
        return set.Built[target.Id] = result;
    }

    /// <summary>
    /// 지금 직업에서 가장 적게 바꾸는 길(목표 → 거칠 직업). 어빌리티 레벨은 직업을 바꿔도 남으므로 상태 = 지금까지 오른 레벨 벡터(조건에 나오는 어빌리티만,
    /// 조건 최대치로 자름)다. 한 직업에 가면 그 직업이 가르치는 최대치까지 올린다고 보고(레벨은 내려가지 않으니 손해가 없다), 다음 직업은 원본 규칙
    /// (<see cref="CanChange"/>) 그대로 조건을 모두 채운 직업. 너비 우선으로 단계를 늘리며 같은 벡터는 한 번만 보고, 한 단계가 너무 넓으면 target
    /// (없으면 레벨 합)에 가까운 상태만 남긴다. 같은 단계 길이 여럿이면 조건이 가벼운 쪽. target 이 있으면 그것만, 없으면 모든 직업의 길을 찾는다.
    /// </summary>
    private static Dictionary<int, List<Part1Job>> Search(IReadOnlyList<Part1Job> jobs, Part1Chr chr, Part1Job now, Part1Job? target)
    {
        var cap = new Dictionary<int, int>();
        foreach (var j in jobs) foreach (var (a, n) in Needs(j)) cap[a] = Math.Max(cap.GetValueOrDefault(a), n);
        var ids = cap.Keys.OrderBy(a => a).ToArray();
        var index = ids.Select((a, i) => (a, i)).ToDictionary(p => p.a, p => p.i);
        var teach = jobs.ToDictionary(j => j.Id, j => ids.Select(a => (char)Math.Min(cap[a], Teaches(j, a))).ToArray());
        var needs = jobs.ToDictionary(j => j.Id, j => Needs(j).Select(n => (I: index[n.Ability], n.Need)).ToArray());
        bool Ok(char[] l, Part1Job j) => needs[j.Id].All(n => l[n.I] >= n.Need);
        char[] Max(char[] a, char[] b) { var r = new char[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] > b[i] ? a[i] : b[i]; return r; }
        int Score(char[] l) => target != null ? needs[target.Id].Sum(n => Math.Min(l[n.I], n.Need)) : l.Sum(c => c);

        var start = Max([.. ids.Select(a => (char)Math.Min(cap[a], Math.Max(0, chr.AbilityLevel(a))))], teach[now.Id]);
        var goals = jobs.Where(j => j.Id != now.Id && j.Firsts.Any(f => f.A > 0) && (target == null || j.Id == target.Id)).ToList();
        var candidates = jobs.Where(j => j.Id != now.Id && j.Firsts.Any(f => f.A > 0)).OrderBy(Weight).ThenBy(j => j.Id).ToList();
        var found = new Dictionary<int, List<Part1Job>>();
        foreach (var g in goals) if (Ok(start, g)) found[g.Id] = [];
        var seen = new HashSet<string> { new(start) };
        var frontier = new List<(char[] L, List<Part1Job> Path, int Cost)> { (start, [], 0) };
        for (int depth = 1; depth <= 6 && frontier.Count > 0 && found.Count < goals.Count; depth++)
        {
            var next = new List<(char[] L, List<Part1Job> Path, int Cost)>();
            var best = new Dictionary<int, (List<Part1Job> Path, int Cost)>();
            foreach (var (l, path, cost) in frontier)
                foreach (var j in candidates)
                {
                    if (!Ok(l, j)) continue;
                    var l2 = Max(l, teach[j.Id]);
                    if (!seen.Add(new string(l2))) continue;
                    List<Part1Job> p2 = [.. path, j];
                    int c2 = cost + Weight(j);
                    foreach (var g in goals)
                        if (!found.ContainsKey(g.Id) && g.Id != j.Id && Ok(l2, g) && (!best.TryGetValue(g.Id, out var b) || c2 < b.Cost)) best[g.Id] = (p2, c2);
                    next.Add((l2, p2, c2));
                }
            foreach (var (id, b) in best) found[id] = b.Path;
            frontier = next.Count > 3000 ? [.. next.OrderByDescending(s => Score(s.L)).ThenBy(s => s.Cost).Take(3000)] : next;
        }
        return found;
    }

    /// <summary>칸마다 올릴 것 — 뒤 칸(목표 포함)의 조건 가운데 아직 모자라고 이 직업이 더 올려 주는 것을, 뒤에서 필요한 만큼(이 직업의 최대까지).</summary>
    private static List<(Part1Job Job, List<(int Ability, int Level)> Raise)> Steps(Part1Chr chr, List<Part1Job> seq, Part1Job target)
    {
        var level = new Dictionary<int, int>();
        int Have(int a) => level.TryGetValue(a, out int v) ? v : Math.Max(0, chr.AbilityLevel(a));
        var result = new List<(Part1Job Job, List<(int Ability, int Level)> Raise)>();
        for (int i = 0; i < seq.Count; i++)
        {
            var later = seq.Skip(i + 1).Append(target).SelectMany(Needs).GroupBy(n => n.Ability).ToDictionary(g => g.Key, g => g.Max(n => n.Need));
            var raise = new List<(int Ability, int Level)>();
            foreach (var (a, need) in later.OrderBy(p => p.Key))
            {
                int have = Have(a), t = Teaches(seq[i], a);
                if (have < need && t > have) raise.Add((a, Math.Min(t, need)));
            }
            foreach (var a in later.Keys) level[a] = Math.Max(Have(a), Teaches(seq[i], a));
            result.Add((seq[i], raise));
        }
        return result;
    }

    /// <summary>한 직업에서 바로 오는 길이 없는 직업 가운데, 그 인물의 가장 짧은 길이 center 를 마지막으로 거치는 것 — 오른쪽에 흐리게.</summary>
    private List<Part1Job> MultiNextOf(Part1Job center, Part1Chr chr) =>
        [.. RoutesOf(chr).All.Where(r => r.Value.Count > 0 && r.Value[^1].Id == center.Id).Select(r => Job(r.Key)!)
                         .Where(j => j.Id != center.Id && PrevOf(j).Count == 0).OrderBy(Weight).ThenBy(j => j.Id)];

    // ── 그리기 ──

    private (int X, int Y, int W, int H) Window()
    {
        int w = Math.Min(720, host.ViewWidth - 16), h = Math.Min(540, host.ViewHeight - GameWindow.FrameTitleH - 16);
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
        int areaX = x + ListW + 16, areaR = x + W - 10, areaY = y + 10, areaB = y + H - DetailH - ChipsH - RouteH - 32;
        const int Gap = 28;
        int prevW = 128, centerW = 170, nextW = areaR - areaX - prevW - centerW - 2 * Gap;
        int prevX = areaX, centerX = prevX + prevW + Gap, nextX = centerX + centerW + Gap;
        host.UiText("이 직업으로 오는 직업", prevX, areaY, Dim, 10);
        host.UiText("다음 직업", nextX, areaY, Dim, 10);
        int top = areaY + 16, space = areaB - top;

        var prev = PrevOf(center);
        var next = NextOf(center, chr);
        // 바로 오는 길이 없는 직업 가운데 그 인물의 가장 짧은 길이 이 직업을 마지막으로 거치는 것도 흐리게(오른쪽 끝).
        int multiFrom = next.Count;
        if (chr != null) next.AddRange(MultiNextOf(center, chr).Take(Math.Max(0, MaxSide - next.Count)));
        var needs = Needs(center).ToList();
        int centerH = 64 + needs.Count * 15;
        int centerY = top + Math.Max(0, (space - centerH) / 2);

        // 칸 자리를 먼저 잡아 짚은 칸을 안다.
        var nodes = new List<(Part1Job Job, int X, int Y, int W, int H, int Side)>();
        const int PrevH = 34;
        int prevTotal = prev.Count * (PrevH + NodeGap) - NodeGap;
        int py = top + Math.Max(0, (space - prevTotal) / 2);
        foreach (var p in prev) { nodes.Add((p, prevX, py, prevW, PrevH, -1)); py += PrevH + NodeGap; }
        // 한 직업에서 바로 오는 길이 없으면 — 그 인물의 지금 직업에서 가는 가장 짧은 여러 단계 길의 마지막 직업을 흐리게.
        var route = chr != null && center.Id != current ? RouteTo(chr, center) : null;
        if (prev.Count == 0 && route is { Count: > 0 } && route[^1].Job.Id != center.Id)
            nodes.Add((route[^1].Job, prevX, top + Math.Max(0, (space - PrevH) / 2), prevW, PrevH, -2));
        var nextHeights = next.Select((n, i) => i >= multiFrom ? PrevH : NextHeight(n, chr, nextW)).ToList();
        int gap = NodeGap, nextTotal = nextHeights.Sum() + gap * Math.Max(0, next.Count - 1);
        if (nextTotal > space && next.Count > 1) { gap = Math.Max(1, (space - nextHeights.Sum()) / (next.Count - 1)); nextTotal = nextHeights.Sum() + gap * (next.Count - 1); }
        int ny = top + Math.Max(0, (space - nextTotal) / 2);
        for (int i = 0; i < next.Count; i++) { nodes.Add((next[i], nextX, ny, nextW, nextHeights[i], i >= multiFrom ? 2 : 1)); ny += nextHeights[i] + gap; }

        Part1Job? hover = null;
        foreach (var n in nodes) if (Hot(n.X, n.Y, n.W, n.H)) hover = n.Job;
        bool centerHot = Hot(centerX, centerY, centerW, centerH);
        if (centerHot) hover = center;

        // 잇는 줄 — 옆 칸 가운데에서 가운데 칸으로.
        foreach (var n in nodes)
        {
            uint color = n.Job == hover ? Blue : Line;
            if (n.Side == -2) Elbow(n.X + n.W, n.Y + n.H / 2, centerX, centerY + 18, n.Job == hover ? Blue : Dim);
            else if (n.Side < 0) Elbow(n.X + n.W, n.Y + n.H / 2, centerX, centerY + 18, color);
            else Elbow(centerX + centerW, centerY + 18, n.X, n.Y + Math.Min(n.H / 2, 10), n.Job == hover ? Blue : n.Side == 2 ? Dim : StateColor(chr, n.Job) == Green ? Green : Line);
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
            if (side is -2 or 2)
            {
                int changes = (side < 0 ? route : RouteTo(chr!, job))?.Count ?? 0;
                host.UiFill(nx, nyy, nw, nh, hot ? NodeHot : 0xFF1A2040);
                host.UiStroke(nx, nyy, nw, nh, Dim);
                host.UiText(job.Name, nx + 6, nyy + 4, job.Id == current ? Gold : 0xFFC0C8E0, 12);
                host.UiText($"여러 단계 · {changes}번 바꿈", nx + 6, nyy + 19, Dim, 9);
                int mid = job.Id;
                _hits.Add((nx, nyy, nw, nh, () => _center = mid));
                continue;
            }
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
        if (prev.Count == 0 && !nodes.Any(n => n.Side == -2)) host.UiText("(첫 직업 · 따로 오는 길)", prevX, top + space / 2 - 6, Dim, 10);
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

        DrawRoute(center, chr, current, route, areaX, y + H - DetailH - 20 - RouteH, areaR);
        DrawDetail(hover ?? center, chr, areaX, y + H - DetailH - 22, areaR - areaX, DetailH);
        host.UiText("직업을 누르면 그 직업을 가운데로 · 가운데를 누르면 지금 직업 · Esc/T/오른쪽 단추: 닫기", x + 10, y + H - 17, Dim, 9);
    }

    /// <summary>
    /// 「경로」 줄 — 가운데 직업까지 「지금 직업 → 거칠 직업 (거기서 올릴 것) → … → 목표」. 칸을 누르면 그 직업을 가운데로.
    /// 지금 조건을 다 채웠으면 「바로 갈 수 있음」, 길이 없으면 「경로 없음」.
    /// </summary>
    private void DrawRoute(Part1Job center, Part1Chr? chr, int current, List<(Part1Job Job, List<(int Ability, int Level)> Raise)>? route, int x0, int y0, int right)
    {
        host.UiFill(x0, y0 - 3, right - x0, 1, Line);
        host.UiText("경로:", x0, y0 + 2, Dim, 10);
        int x = x0 + TextW("경로:", 10) + 8, y = y0;
        string? note = chr == null ? "인물 없음" : center.Id == current ? "지금 직업" : CanChange(chr, center) ? "바로 갈 수 있음"
                       : route == null ? "경로 없음" : null;
        if (note != null) { host.UiText(note, x, y + 2, note == "경로 없음" ? 0xFFE08080 : note == "바로 갈 수 있음" ? Green : Gold, 10); return; }
        // 거기서 올릴 것까지 두 줄에 안 들어가면 직업 이름만.
        string Label(int i, bool full) => (i == 0 ? "지금 " : "") + route![i].Job.Name
            + (full && route[i].Raise.Count > 0 ? " (" + string.Join(" · ", route[i].Raise.Select(r => $"{AbilityName(r.Ability)} {r.Level}")) + ")" : "");
        var steps = new List<(Part1Job Job, string Text, bool Final, int X, int Y, int W)>();
        foreach (bool full in new[] { true, false })
        {
            steps.Clear();
            int cx = x, cy = y;
            var texts = route!.Select((s, i) => (s.Job, Text: Label(i, full), Final: false)).Append((Job: center, Text: center.Name, Final: true)).ToList();
            for (int i = 0; i < texts.Count; i++)
            {
                int cw = TextW(texts[i].Text, 10) + 12;
                if (i > 0)
                {
                    int aw = TextW("→", 10) + 6;
                    if (cx + aw + cw > right && cx > x0 + 40) { cx = x0 + 8; cy += 18; }
                    cx += aw;
                }
                steps.Add((texts[i].Job, texts[i].Text, texts[i].Final, cx, cy, cw));
                cx += cw + 4;
            }
            if (cy + 16 <= y0 + RouteH) break;
        }
        for (int i = 0; i < steps.Count; i++)
        {
            var (job, text, final, sx, sy, cw) = steps[i];
            if (sy + 16 > y0 + RouteH) break;
            if (i > 0) host.UiText("→", sx - TextW("→", 10) - 4, sy + 2, Dim, 10);
            bool hot = Hot(sx, sy, cw, 16);
            host.UiFill(sx, sy, cw, 16, hot ? NodeHot : NodeBg);
            host.UiStroke(sx, sy, cw, 16, final || i == 0 ? Gold : CanChange(chr, job) ? Green : Line);
            host.UiText(text, sx + 6, sy + 2, final ? Gold : GameWindow.White, 10);
            int id = job.Id;
            _hits.Add((sx, sy, cw, 16, () => _center = id));
        }
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
