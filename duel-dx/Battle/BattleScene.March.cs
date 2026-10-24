namespace DuelDx;

/// <summary>
/// 파트1 링 「행군」 — 원본에 없는 편의 명령(사용자 요청). 차례인 인물이 <b>마지막으로 행동한 다른 아군</b> 쪽으로
/// 이번 차례에 걸을 수 있는 만큼 걸어가(그 아군에 가장 가까운 빈 파란 칸) 링 「휴식」과 똑같이 쉰다.
/// 행동한 아군이 아직 없으면 가장 가까운 아군을 따라간다.
/// </summary>
internal sealed unsafe partial class BattleScene
{
    internal const string MarchHelp = "행군: 마지막으로 행동한 아군 쪽으로 걸을 수 있는 만큼 걸어간 뒤 휴식한다.";

    /// <summary>차례를 마친 아군 — 끝이 가장 최근. 판이 바뀌면 옛 인물은 <see cref="MarchTarget"/> 이 걸러 낸다.</summary>
    internal readonly List<UnitState> _actedAllies = [];

    /// <summary>행군으로 걷는 중인 인물 — 다 걸으면 쉰다.</summary>
    internal UnitState? _marchRest;

    internal int _marchHookCount;

    /// <summary>차례가 끝난 인물을 적어 둔다(<see cref="EndTurn"/>).</summary>
    internal void NoteActed(UnitState u)
    {
        if (!u.IsAlly) return;
        _actedAllies.Remove(u);
        _actedAllies.Add(u);
        if (_actedAllies.Count > 64) _actedAllies.RemoveAt(0);
    }

    /// <summary>행군이 따라갈 아군 — 마지막으로 행동한 다른 아군, 없으면 가장 가까운 아군. 없으면 null.</summary>
    internal UnitState? MarchTarget(UnitState u)
    {
        int self = Array.IndexOf(host._units, u);
        bool Ok(UnitState o) => o != u && o.Alive && o.OnField && o.IsAlly && o.LeaderIndex != self && Array.IndexOf(host._units, o) >= 0;
        for (int i = _actedAllies.Count - 1; i >= 0; i--)
            if (Ok(_actedAllies[i])) return _actedAllies[i];
        return host._units.Where(Ok).OrderBy(o => Math.Abs(o.Col - u.Col) + Math.Abs(o.Row - u.Row)).FirstOrDefault();
    }

    /// <summary>행군: 따라갈 아군에 가장 가까운 빈 칸(같으면 걸음이 싼 칸)까지 걷기를 걸고, 다 걸으면 <see cref="UpdateMarch"/> 가 쉰다.</summary>
    internal void March(int index)
    {
        var u = host._units[index];
        if (MarchTarget(u) is not { } t || ComputeRange(u) is not { } range) { host.Toast($"{host.UnitName(index)} 휴식"); Rest(index); return; }
        int cols = host.Cols, here = u.Row * cols + u.Col;
        int Dist(int i) => Math.Abs(i % cols - t.Col) + Math.Abs(i / cols - t.Row);
        int best = here, bestDist = Dist(here), bestCost = range.CanReach(here) ? range.Cost[here] : 0;
        for (int i = 0; i < range.Cost.Length; i++)
        {
            if (i == here || !range.CanReach(i) || LiveUnitAt(i % cols, i / cols) != null) continue;
            int d = Dist(i);
            if (d > bestDist || (d == bestDist && range.Cost[i] >= bestCost)) continue;
            best = i; bestDist = d; bestCost = range.Cost[i];
        }
        var path = best == here ? null : PathWithin(range, u.Col, u.Row, best);
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"march: {u.ChrCode}({u.Col},{u.Row}) → follow {t.ChrCode}({t.Col},{t.Row}) acted [{string.Join(' ', _actedAllies.Select(a => a.ChrCode))}] dest ({best % cols},{best / cols}) dist {bestDist} steps {path?.Count ?? 0}" + Environment.NewLine);
        host.Toast($"{host.UnitName(index)} 행군");
        if (path is not { Count: > 0 }) { Rest(index); return; }
        foreach (var cell in path) u.Path.Enqueue(cell);
        _marchRest = u;
    }

    /// <summary>행군으로 걷던 인물이 다 서면 쉰다. 차례가 바뀌었거나(사건 따위) 쓰러졌으면 그만둔다. DUELDX_MARCH=1 시험 고리도 여기서.</summary>
    internal void UpdateMarch()
    {
        if (_marchRest is { } m)
        {
            if (_turn < 0 || host._units[_turn] != m || !m.Alive || _outcome.Length > 0) _marchRest = null;
            else if (!m.IsBusy && _routine == null && host.Tlk._talk == null && _runningEvent < 0 && !LevelUpOpen)
            {
                _marchRest = null;
                Rest(_turn);
            }
            return;
        }
        // DUELDX_MARCH=ring — 화면 밖 시험: 첫 플레이어 차례에 링을 열고 행군 항목에 마우스를 올려 둔다(그림·이름표 보기).
        if (Environment.GetEnvironmentVariable("DUELDX_MARCH") == "ring" && _marchHookCount == 0 && IsPlayerTurn && _ringUnit < 0
            && host.Tlk._talk == null && _runningEvent < 0 && !host._units[_turn].IsBusy)
        {
            _marchHookCount++;
            OpenRing(_turn);
            _ringHover = Array.FindIndex(Items, r => r.Command == RingCommand.March);
        }
        // DUELDX_MARCH=<n> — 화면 밖 시험: 처음 n 번의 플레이어 차례에 링을 거치지 않고 행군한다.
        if (int.TryParse(Environment.GetEnvironmentVariable("DUELDX_MARCH"), out int times) && _marchHookCount < times
            && IsPlayerTurn && _ringUnit < 0 && host.Tlk._talk == null && _runningEvent < 0 && !host._units[_turn].IsBusy && !LevelUpOpen)
        {
            _marchHookCount++;
            RingShortcut(RingCommand.March);
        }
    }
}
