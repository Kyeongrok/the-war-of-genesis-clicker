namespace WarOfGenesis.Assets;

/// <summary>전투 하나가 어디서 불리는지 — 부르는 곳 설명들과, 도달 관계로 닿는 챕터(제목 txr 순).</summary>
public sealed record BattleOrigin(IReadOnlyList<string> Callers, IReadOnlyList<(int Chapter, string Title)> Chapters);

/// <summary>
/// 전투(Btl) → 챕터·장소 찾기. <c>tools/re/battle_list.py</c> 에서 표를 만드는 데 필요한 만큼만 옮겼다.
/// </summary>
/// <remarks>
/// 전투를 거는 세 곳(옵시디안 분석-전투목록 ba-7): ① <c>Chp</c> 장소 레코드(20B) 셋째 워드 v, 1~9999 = Btl v ·
/// 10000~19999 = Fld v−10000, ② <c>Chp</c>·<c>Fld</c> 스크립트 행동 10(Btl)·6(Fld), ③ <c>Btl</c> 이벤트 행동 10(Btl)·6(Fld).
/// 챕터에서 이 간선을 따라 닿는 전투를 그 챕터 것으로 친다. 파일 끝까지 맞는 Chp·Fld·Btl 만 쓴다.
/// </remarks>
public static class BattleChapters
{
    public static Dictionary<int, BattleOrigin> Build(GameFiles files, Func<ushort, string> text)
    {
        var edges = new Dictionary<(char Kind, int Id), HashSet<(char, int)>>();
        var callers = new Dictionary<int, List<string>>();
        var titles = new Dictionary<int, int>();

        void Edge((char, int) from, (char, int) to)
        {
            if (!edges.TryGetValue(from, out var set)) edges[from] = set = [];
            set.Add(to);
        }
        void Caller(int btl, string s)
        {
            if (!callers.TryGetValue(btl, out var list)) callers[btl] = list = [];
            if (!list.Contains(s)) list.Add(s);
        }
        string T(int id) => id >= 0 ? text((ushort)id) : "";

        foreach (int ci in Ids(files, "Chp", ".chp"))
        {
            if (ParseChp(files.Read("Chp", $"{ci:D4}.chp")) is not { } c) continue;
            titles[ci] = c.Title;
            string title = T(c.Title);
            var planetOf = new Dictionary<int, string>();
            foreach (var (name, places) in c.Planets)
                foreach (int pid in places)
                    if (pid >= 0) planetOf.TryAdd(pid, T(name));
            foreach (var (no, name, v) in c.Places)
            {
                if (v is > 0 and < 10000)
                {
                    Caller(v, $"Chp {ci:D4}「{title}」 {planetOf.GetValueOrDefault(no, "?")} / {T(name)}");
                    Edge(('c', ci), ('b', v));
                }
                else if (v is >= 10000 and < 20000) Edge(('c', ci), ('f', v - 10000));
            }
            foreach (var (code, arg) in c.Script)
            {
                if (code == 10) { Caller(arg, $"Chp {ci:D4}「{title}」 스크립트"); Edge(('c', ci), ('b', arg)); }
                else if (code == 6) Edge(('c', ci), ('f', arg));
            }
        }

        foreach (int fi in Ids(files, "Fld", ".fld"))
        {
            if (ParseFld(files.Read("Fld", $"{fi:D4}.fld")) is not { } script) continue;
            foreach (var (code, arg) in script)
            {
                if (code == 10) { Caller(arg, $"Fld {fi:D4} 스크립트"); Edge(('f', fi), ('b', arg)); }
                else if (code == 6) Edge(('f', fi), ('f', arg));
            }
        }

        foreach (int bi in Ids(files, "Btl", ".btl"))
        {
            if (BattleEvents.Parse(files.Read("Btl", $"{bi:D4}.btl"), out bool exact) is not { } events || !exact) continue;
            foreach (var act in events.SelectMany(e => e.Actions))
            {
                if (act.Code == 10) { Caller(act.Args[0], $"Btl {bi:D4} 이벤트(이어서)"); Edge(('b', bi), ('b', act.Args[0])); }
                else if (act.Code == 6) Edge(('b', bi), ('f', act.Args[0]));
            }
        }

        var reach = new Dictionary<int, HashSet<int>>();
        foreach (int ci in titles.Keys)
        {
            var seen = new HashSet<(char, int)>();
            var stack = new Stack<(char, int)>([('c', ci)]);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (!seen.Add(n)) continue;
                if (n.Item1 == 'b')
                {
                    if (!reach.TryGetValue(n.Item2, out var set)) reach[n.Item2] = set = [];
                    set.Add(ci);
                }
                if (edges.TryGetValue(n, out var next)) foreach (var m in next) stack.Push(m);
            }
        }

        int Rank(int ci) => titles[ci] is >= 2284 and <= 2313 ? titles[ci] : 9999;
        var result = new Dictionary<int, BattleOrigin>();
        foreach (int btl in callers.Keys.Union(reach.Keys))
        {
            var chapters = reach.GetValueOrDefault(btl)?.OrderBy(Rank).ThenBy(c => c).Select(c => (c, T(titles[c]))).ToList() ?? [];
            result[btl] = new BattleOrigin(callers.GetValueOrDefault(btl) ?? [], chapters);
        }
        return result;
    }

    private static IEnumerable<int> Ids(GameFiles files, string folder, string ext) =>
        files.List(folder, ext).Keys
             .Select(n => int.TryParse(Path.GetFileNameWithoutExtension(n), out int id) ? id : -1)
             .Where(id => id >= 0).Distinct().OrderBy(id => id);

    private sealed record Chp(int Title, List<(int Name, int[] Places)> Planets, List<(int No, int Name, int Value)> Places,
                              List<(int Code, int Arg)> Script);

    private static short H(byte[] b, int o) => BitConverter.ToInt16(b, o);

    /// <summary><c>battle_list.parse_chp</c>. 끝이 안 맞거나 넘치면 null.</summary>
    private static Chp? ParseChp(byte[]? b)
    {
        if (b == null) return null;
        try
        {
            int o = 42;
            int title = H(b, o + 4), n1 = H(b, o + 6);
            o += 10 + 12 * n1;
            o += 4 + 30 * H(b, o);
            int n3 = H(b, o);
            o += 4 + 66 * n3;
            int n4 = H(b, o);
            o += 4;
            var planets = new List<(int, int[])>();
            for (int i = 0; i < n4; i++, o += 84)
                planets.Add((H(b, o + 4), [.. Enumerable.Range(0, 8).Select(k => (int)H(b, o + 16 + 2 * k))]));
            int n5 = H(b, o);
            o += 4;
            var places = new List<(int, int, int)>();
            for (int i = 0; i < n5; i++, o += 20) places.Add((H(b, o), H(b, o + 2), H(b, o + 4)));
            o += 4 + 4 * H(b, o);
            var script = ParseScript(b, ref o, hasMax: false);
            return o == b.Length ? new Chp(title, planets, places, script) : null;
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary><c>battle_list.parse_fld</c> — 스크립트의 (행동 코드, 인자0) 만.</summary>
    private static List<(int, int)>? ParseFld(byte[]? b)
    {
        if (b == null) return null;
        try
        {
            int o = 12;
            o += 4 + 16 * H(b, o);
            o += 4 + 16 * H(b, o);
            o += 4 + 10 * H(b, o);
            var script = ParseScript(b, ref o, hasMax: false);
            return o == b.Length ? script : null;
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>스크립트: u16 수, 이벤트마다 u16, u16 조건 수, (u16 코드+16B)×, u16 행동 수, (u16 코드+16B)×.</summary>
    private static List<(int, int)> ParseScript(byte[] b, ref int o, bool hasMax)
    {
        var acts = new List<(int, int)>();
        int n = H(b, o);
        o += 2;
        for (int i = 0; i < n; i++)
        {
            if (hasMax) o += 2;
            int nc = H(b, o + 2);
            o += 4 + 18 * nc;
            int na = H(b, o);
            o += 2;
            for (int k = 0; k < na; k++, o += 18) acts.Add((H(b, o), H(b, o + 2)));
        }
        if (o > b.Length) throw new ArgumentException("스크립트가 파일 끝을 넘습니다.");
        return acts;
    }
}
