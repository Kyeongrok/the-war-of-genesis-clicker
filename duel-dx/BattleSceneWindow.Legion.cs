using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 전투에 나오는 군단(부대) — 대장 한 명 + 부하 최대 여섯이 한 덩어리로 움직인다(분석-군단 gd-1).
/// </summary>
/// <remarks>
/// <c>Btl</c> 인물 레코드의 군단 번호(파일 15)가 0 이 아니면 <c>Dat\For.dat</c> 의 그 군단 부하들이 대장 둘레에 선다 —
/// 자리는 진형표(<see cref="LegionData.FormationCells"/>)를 대장이 보는 쪽으로 돌린 것이다.
/// 예: Btl 0045 의 가이아리더 셋은 모두 군단 <b>7</b>(부하 가이아버그 넷, 진형 5 이자형)이라 <b>넷을 거느리고</b> 나온다.
/// <list type="bullet">
/// <item><b>차례</b>는 대장만 받는다. 부하는 차례 고르기에서 빠진다(<c>0x1006af1a</c>).</item>
/// <item>대장이 움직이면 부하도 제 진형 칸으로 따라가고, 대장이 치면 <b>같은 대상</b>을 함께 친다(상태 15).</item>
/// <item>부하 능력치는 대장 세력(1000) × For 보정 / 100 만큼 LP·PSY·DEP 가 올라간다.</item>
/// <item>대장이 쓰러지면 첫 부하가 새 대장이 되고 세력이 0.6배가 된다(데모는 편은 그대로 둔다).</item>
/// </list>
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>대장 세력 기본값 — 원본은 <c>CChr+0x148[군단]</c> 이 모두 1000 으로 시작한다.</summary>
    private const int LegionPower = 1000;

    /// <summary>전투에 세울 인물들 — 군단이 붙은 대장 뒤에 부하를 끼워 넣는다.</summary>
    private UnitState[] BuildUnits(DemoScene scene)
    {
        var legions = Mos.Legions();
        var list = new List<UnitState>();
        _deployMovable.Clear();
        _deployBench.Clear();
        var followers = new List<UnitState>();

        foreach (var rosterRecord in scene.Roster)
        {
            // 인물 레코드의 군단 칸(파일 15) — 머리 워드 7 이 켜진 전투에서 <b>값이 1 일 때만</b> 그 인물에게 배속된 군단(CChr+0x1c)으로 바꾼다
            // (LoadBtl 0x100634f9~0x10063503). 값 0·2 이상은 Btl 값 그대로(0x1006350c), 워드 7 이 꺼져 있으면 1 도 그대로(For[1] 「default」 = 부하 없음).
            // 전에는 편 4 면 워드 7·값과 상관없이 배속 군단으로 덮어써, 군단 금지 전투에도 부하가 나오고 이야기 전투의 편대(0292 마리아 → 5)가 바뀌었다(감사3 L4).
            var record = rosterRecord.Legion == 1 && scene.LegionsAllowed
                ? rosterRecord with { Legion = Mos._unitLegion.GetValueOrDefault(rosterRecord.ChrCode) } : rosterRecord;
            // 군단 편집기가 리더를 바꿔 두었으면(legions/*.json 의 leader) 전투 자료의 적·동맹 대장을 그 인물로 세운다(ed-1).
            if (record.Side != 4 && record.Legion > 1 && legions.GetValueOrDefault(record.Legion) is { Leader: > 0 } withLeader)
                record = record with { ChrCode = withLeader.Leader };
            int leaderIndex = list.Count;
            list.Add(new UnitState(record));
            followers.AddRange(SpawnFollowers(record, leaderIndex, legions, list.Concat(followers)));
        }

        list.AddRange(followers);

        // 배치 칸이 있는 전투는 파티를 거기 세운다 — 원본은 배치 칸이 있으면 싱글에서 늘 「캐릭터 배치」(상태 2, 0x10066d00)로 시작한다.
        // 전에는 고정 아군이 하나라도 있으면 건너뛰어, Btl 0140 처럼 진(고정)만 서고 나머지 파티가 안 나왔다(사용자 보고, 26개 전투).
        // 이미 Btl 에 선 사람은 빼고, 빈 칸에만 차례로 세운다(자동 배치 0x10066480 과 같은 차례). 고르는 화면은 아직 없다.
        if (scene.Placement is { Count: > 0 } spots)
        {
            // 세우는 것은 <b>동료</b>(801 로 들어온 인물)다 — _party 에는 앞 전투가 편 3 으로 끼워 준 제이슨·용병까지 남아 있어
            // 그대로 쓰면 훈련용 던젼에 제이슨이 따라 들어왔다(사용자 지적). 동료 목록이 없으면 예전대로.
            var party = Mos._members.Count > 0 ? Mos._members.Where(_party.ContainsKey).ToList()
                      : _party.Keys.Count > 0 ? _party.Keys.ToList()
                                              : [.. DemoScene.Fallback.Roster.Where(u => u.IsAlly).Select(u => u.ChrCode)];
            party = [.. party.Where(chr => !list.Any(u => u.ChrCode == chr))];
            var free = spots.Where(sp => !list.Any(u => u.Col == sp.Col && u.Row == sp.Row)).ToList();
            // 배치 단계로 세우는 파티원은 「군단사용」(명단 +0x30, 기본 = 워드 7 켜짐 && 배속 있음)이면 배속 군단(+0x12c)으로 부하를 만든다
            // (0x100673dd~0x10067717, 분석-전투 ba-6). 전에는 늘 군단 0 이라 모세스에서 배속해도 대부분의 전투에서 부하가 안 나왔다(감사3 L4).
            // 여기서는 기본값(켜짐)으로 만들고, 배치 창의 「군단사용」 단추가 끄면 배치종료 때 부하를 지운다(Deploy.cs). 부하는 파티원들 뒤에 붙인다 — 맵 밖 여분을 지울 때 DropUnits 가 번호를 다시 맞춘다.
            var placedFollowers = new List<UnitState>();
            int LegionOf(int chr) => scene.LegionsAllowed ? Mos._unitLegion.GetValueOrDefault(chr) : 0;
            for (int i = 0; i < free.Count && i < party.Count; i++)
            {
                var rec = new DemoUnit(party[i], free[i].Col, free[i].Row, 4, LegionOf(party[i]), free[i].Facing);
                var placed = new UnitState(rec);
                list.Add(placed);
                _deployMovable.Add(placed);
                placedFollowers.AddRange(SpawnFollowers(rec, list.Count - 1, legions, list.Concat(placedFollowers)));
            }
            // 칸이 모자라 못 선 파티원은 맵 밖(0,0)에 만들어 둔다 — 배치 단계에서 바꿔 세울 수 있게. 배치 단계를 안 열면 지운다(Deploy.cs).
            for (int i = free.Count; i < party.Count; i++)
            {
                var rec = new DemoUnit(party[i], 0, 0, 4, LegionOf(party[i]), Facing.Up);
                var bench = new UnitState(rec);
                list.Add(bench);
                _deployMovable.Add(bench);
                _deployBench.Add(bench);
                placedFollowers.AddRange(SpawnFollowers(rec, list.Count - 1, legions, list.Concat(placedFollowers)));
            }
            list.AddRange(placedFollowers);
        }
        return [.. list];
    }

    /// <summary>
    /// 군단 번호가 붙은 대장 레코드의 부하들을 진형대로 만든다(<c>LoadBtl 0x100635b9~0x10063739</c>) — 판에는 아직 안 넣는다.
    /// 군단 0·1(For[1] 「default」)은 부하가 없다.
    /// </summary>
    private List<UnitState> SpawnFollowers(DemoUnit record, int leaderIndex, Dictionary<int, LegionData> legions, IEnumerable<UnitState> occupied)
    {
        var made = new List<UnitState>();
        if (record.Legion <= 1 || legions.GetValueOrDefault(record.Legion) is not { } legion) return made;

        var cells = LegionData.FormationCells[Math.Clamp((int)legion.Formation, 0, 5)];
        var taken = new HashSet<(int, int)>(occupied.Select(u => (u.Col, u.Row)));
        // 대장이 아직 전장 밖(0,0)이면 부하도 전장 밖에 둔다 — 증원(200)이 대장을 세울 때 진형대로 같이 데려온다.
        // 전에는 (0,0) 둘레 칸에 세워 부하만 맵 귀퉁이에 먼저 서 있었다.
        bool offField = record.Col == 0 && record.Row == 0;
        for (int i = 0; i < legion.Members.Length && i < cells.Length; i++)
        {
            var (col, row) = offField ? (0, 0) : FormationSpot(record.Col, record.Row, record.Facing, cells[i], taken);
            if (!offField) taken.Add((col, row));
            made.Add(new UnitState(record with { ChrCode = legion.Members[i], Col = col, Row = row, Legion = 0 })
            {
                LeaderIndex = leaderIndex,
                FormationSlot = i,
            });
        }
        return made;
    }

    /// <summary>진형 칸 하나를 대장 방향으로 돌려 판에 놓는다 — 겹치면 대장 둘레에서 빈 칸을 찾는다.</summary>
    private (int Col, int Row) FormationSpot(int leaderCol, int leaderRow, Facing facing, (int Dx, int Dy) cell, HashSet<(int, int)> taken)
    {
        var (dx, dy) = RotateFormation(cell, facing);
        int col = Math.Clamp(leaderCol + dx, 0, Cols - 1), row = Math.Clamp(leaderRow + dy, 0, Rows - 1);
        for (int r = 1; r <= 3 && taken.Contains((col, row)); r++)
            for (int ny = -r; ny <= r && taken.Contains((col, row)); ny++)
                for (int nx = -r; nx <= r && taken.Contains((col, row)); nx++)
                {
                    int cx = Math.Clamp(leaderCol + nx, 0, Cols - 1), cy = Math.Clamp(leaderRow + ny, 0, Rows - 1);
                    if (!taken.Contains((cx, cy))) (col, row) = (cx, cy);
                }
        return (col, row);
    }

    /// <summary>
    /// 배치 단계에서 대장을 세우거나 뺐을 때 — 부하를 새 자리 둘레에 진형대로 다시 세운다(원본도 배치 단계에서 부하를 다시 만든다, 0x100673dd).
    /// 대장이 맵 밖이면 부하도 맵 밖(0,0)으로.
    /// </summary>
    private void PlaceLegionAround(UnitState leader)
    {
        int li = Array.IndexOf(_units, leader);
        if (li < 0 || Mos.Legions().GetValueOrDefault(leader.LegionId) is not { } legion) return;
        var cells = LegionData.FormationCells[Math.Clamp((int)legion.Formation, 0, 5)];
        var taken = new HashSet<(int, int)>(_units.Where(u => u.Alive && u.OnField && u.LeaderIndex != li).Select(u => (u.Col, u.Row)));
        foreach (var f in FollowersOf(li).OrderBy(f => f.FormationSlot))
        {
            _followerTarget.Remove(f);
            var (col, row) = leader.OnField ? FormationSpot(leader.Col, leader.Row, leader.Facing, cells[Math.Clamp(f.FormationSlot, 0, cells.Length - 1)], taken) : (0, 0);
            taken.Add((col, row));
            f.StartFacing = leader.Facing;
            f.ResetTo(col, row);
            f.StartCol = col;
            f.StartRow = row;
            f.OnField = leader.OnField;
        }
    }

    /// <summary>
    /// 판에서 인물들을 뺀다 — 뺀 대장의 부하도 함께 빼고, 남은 부하의 대장 번호(<see cref="UnitState.LeaderIndex"/>, 배열 자리)를 다시 맞춘다.
    /// 전에는 배열을 걸러 내기만 해 뒤쪽 부하의 대장 번호가 엉뚱한 인물을 가리킬 수 있었다.
    /// </summary>
    private void DropUnits(Func<UnitState, bool> drop)
    {
        var old = _units;
        bool Gone(UnitState u) => drop(u) || (u.LeaderIndex >= 0 && u.LeaderIndex < old.Length && drop(old[u.LeaderIndex]));
        var leaders = old.Select(u => u.LeaderIndex >= 0 && u.LeaderIndex < old.Length ? old[u.LeaderIndex] : null).ToArray();
        var kept = old.Where((u, i) => !Gone(u)).ToArray();
        foreach (var u in kept)
            if (leaders[Array.IndexOf(old, u)] is { } leader) u.LeaderIndex = Array.IndexOf(kept, leader);
        _units = kept;
        _formationAt.Clear();
    }

    /// <summary>
    /// 진형 칸은 방향 0(위) 기준이다 — 대장이 보는 쪽으로 돌린다(<c>LoadBtl 0x100634cd</c>).
    /// </summary>
    /// <remarks>진형 여섯이 모두 좌우 대칭이라 아래(방향 2)를 <c>(dx, -dy)</c> 로 두어도 칸 집합은 같고 슬롯 짝만 바뀐다 — 원본대로 맞춘다.</remarks>
    private static (int Dx, int Dy) RotateFormation((int Dx, int Dy) cell, Facing facing) => facing switch
    {
        Facing.Up => cell,
        Facing.Down => (cell.Dx, -cell.Dy),
        Facing.Left => (cell.Dy, -cell.Dx),
        _ => (-cell.Dy, cell.Dx),
    };

    /// <summary>
    /// 대장이 움직인 뒤 부하들이 노리는 <b>고정 8칸 고리</b> — 대장 자리에서 맨해튼 거리가 딱 2 인 칸 전부.
    /// </summary>
    /// <remarks>
    /// 다시 세울 때는 <b>진형표도 대장이 보는 쪽도 안 쓴다</b>(<c>0x10073230</c>). 진형표는 <b>전투 시작 배치에만</b> 쓰인다.
    /// 부하는 많아야 여섯이고 칸은 여덟이라 <b>배정에 실패하는 일이 없다</b>.
    /// </remarks>
    private static readonly (int Dx, int Dy)[] FormationRing =
        [(0, -2), (-1, -1), (1, -1), (-2, 0), (2, 0), (-1, 1), (1, 1), (0, 2)];

    /// <summary>지금 기술을 쓰는 중인 대장(배열 번호) — 이동 + 기술이면 진형을 다시 세우지 않는다. 없으면 −1.</summary>
    private int _skillLeader = -1;

    /// <summary>진형을 마지막으로 셈한 대장 자리 — 같은 칸이면 다시 셈하지 않는다.</summary>
    private readonly Dictionary<int, (int Col, int Row)> _formationAt = [];

    /// <summary>부하가 마지막으로 받은 진형 목표 칸 — 대장이 그 뒤로 안 움직여도 이 칸을 계속 따라잡으려 한다.</summary>
    private readonly Dictionary<UnitState, (int Col, int Row)> _followerTarget = [];

    /// <summary>부하마다 다음에 따라잡기를 다시 시도할 시각 — 매 프레임 길찾기를 돌리지 않게 늦춘다.</summary>
    private readonly Dictionary<UnitState, double> _nextFollowerRetry = [];

    /// <summary>그 인물의 부하들(살아 있는 것만).</summary>
    private List<UnitState> FollowersOf(int leaderIndex) =>
        [.. _units.Where(u => u.Alive && u.LeaderIndex == leaderIndex)];

    /// <summary>
    /// 대장이 (<paramref name="destCol"/>, <paramref name="destRow"/>) 로 간 뒤 부하들이 설 칸을 <b>한꺼번에</b> 정한다.
    /// </summary>
    /// <remarks>
    /// 원본(<c>0x10073230</c> · <c>0x10073100</c> · <c>0x10073480</c>) 차례 그대로:
    /// <list type="number">
    /// <item>부하를 <b>대장 목적지까지 가까운 순</b>으로 줄 세운다 — 가까운 쪽이 먼저 칸을 고른다.</item>
    /// <item>「목적지 + (부하 지금 칸 − 대장 지금 칸)」에 가장 가까운 <b>아직 안 뽑힌 고리 칸</b>을 하나씩 가져간다.</item>
    /// <item>그 고리 칸을 목표로, 부하 둘레에서 <b>갈 수 있고 비어 있는</b> 칸 중 점수가 가장 낮은 칸에 실제로 선다 —
    /// 점수 = <c>11 × |(높이차 + |Δx| + |Δy|) − 2| + 10 × 고리칸까지 거리</c>. 하나도 없으면 고리 칸을 그대로 쓴다.</item>
    /// </list>
    /// 앞 부하가 고른 칸은 뒤 부하가 못 쓰게 잡아 둔다.
    /// </remarks>
    private List<(UnitState Follower, int Col, int Row)> FormationPlan(UnitState leader, int destCol, int destRow)
    {
        var followers = FollowersOf(Array.IndexOf(_units, leader));
        var plan = new List<(UnitState, int, int)>();
        if (followers.Count == 0) return plan;

        followers.Sort((a, b) => (Math.Abs(a.Col - destCol) + Math.Abs(a.Row - destRow))
                               - (Math.Abs(b.Col - destCol) + Math.Abs(b.Row - destRow)));
        var usedRing = new bool[FormationRing.Length];
        var taken = new HashSet<(int, int)>();

        foreach (var follower in followers)
        {
            // 바라는 칸 = 대장을 따라 그대로 평행 이동한 자리.
            int wantCol = Math.Clamp(destCol + follower.Col - leader.Col, 0, Cols - 1);
            int wantRow = Math.Clamp(destRow + follower.Row - leader.Row, 0, Rows - 1);

            int best = -1, bestScore = int.MaxValue;
            for (int i = 0; i < FormationRing.Length; i++)
            {
                if (usedRing[i]) continue;
                int rc = destCol + FormationRing[i].Dx, rr = destRow + FormationRing[i].Dy;
                int score = Math.Abs(wantCol - rc) + Math.Abs(wantRow - rr);
                if (score < bestScore) (best, bestScore) = (i, score);
            }
            if (best < 0) { plan.Add((follower, follower.Col, follower.Row)); continue; }
            usedRing[best] = true;
            int ringCol = Math.Clamp(destCol + FormationRing[best].Dx, 0, Cols - 1);
            int ringRow = Math.Clamp(destRow + FormationRing[best].Dy, 0, Rows - 1);

            var (col, row) = StandingCellFor(follower, destCol, destRow, ringCol, ringRow, taken);
            taken.Add((col, row));
            plan.Add((follower, col, row));
        }
        return plan;
    }

    /// <summary>고리 칸을 목표로 부하가 실제로 설 칸 — 못 찾으면 고리 칸 그대로(원본도 그렇다).</summary>
    private (int Col, int Row) StandingCellFor(UnitState follower, int destCol, int destRow,
                                               int ringCol, int ringRow, HashSet<(int, int)> taken)
    {
        int destHeight = _map?.HeightAt(destCol, destRow) ?? 0;
        int best = int.MaxValue;
        (int Col, int Row) pick = (ringCol, ringRow);
        for (int row = follower.Row - 20; row <= follower.Row + 20; row++)
            for (int col = follower.Col - 20; col <= follower.Col + 20; col++)
            {
                if (col == destCol && row == destRow) continue;          // 대장 자리는 비켜 준다
                if (taken.Contains((col, row)) || !CanStand(col, row, follower)) continue;
                int height = Math.Abs((_map?.HeightAt(col, row) ?? 0) - destHeight);
                int score = 11 * Math.Abs(height + Math.Abs(col - destCol) + Math.Abs(row - destRow) - 2)
                          + 10 * (Math.Abs(col - ringCol) + Math.Abs(row - ringRow));
                if (score < best) (best, pick) = (score, (col, row));
            }
        return pick;
    }

    private bool CanStand(int col, int row, UnitState who)
    {
        if ((uint)col >= Cols || (uint)row >= Rows) return false;
        if (_map is { } map && (col >= map.Cols || row >= map.Rows || (CellFlagsAt(col, row) & 0x9) != 0)) return false;   // 물체를 찍은 칸 기준(ba-16)
        return LiveUnitAt(col, row) is not { } other || other == who;
    }

    /// <summary>
    /// 대장이 움직인 뒤 — 부하들의 새 진형 목표 칸을 정한다(실제로 걷는 건 <see cref="TryAdvanceFollower"/>).
    /// </summary>
    private void AssignFormationTargets(int leaderIndex)
    {
        var leader = _units[leaderIndex];
        foreach (var (follower, col, row) in FormationPlan(leader, leader.Col, leader.Row))
            _followerTarget[follower] = (col, row);
    }

    /// <summary>
    /// 부하 하나가 제 진형 목표 칸으로 <b>한 걸음</b> 걷게 시킨다 — 이미 그 자리거나, 바쁘거나, 목표가 없으면 아무것도 안 한다.
    /// </summary>
    /// <remarks>
    /// 원본은 부하마다 경로 이동 명령(<c>0x2711</c>)을 내고 그 길을 실제로 밟는다(<c>0x1005f670</c>) — 순간이동이 아니다.
    /// 지금 예산으로 못 닿으면 이번엔 그냥 남되, <see cref="SyncFollowers"/>가 나중에 다시 시도한다 — 대장이
    /// 그 뒤로 안 움직여도, 예산이 모자라 처음에 못 따라간 부하가 영영 그 자리에 남지 않게 하기 위해서다.
    /// </remarks>
    private void TryAdvanceFollower(UnitState follower)
    {
        if (!_followerTarget.TryGetValue(follower, out var target) || (follower.Col, follower.Row) == target) return;
        if (ComputeRange(follower) is not { } range) return;
        int goal = target.Row * Cols + target.Col;
        if (!range.CanReach(goal))
        {
            // 한 번에 못 닿으면 닿을 수 있는 칸 가운데 목표에 가장 가까운 빈 칸까지라도 간다 — 전에는 아예 안 움직여,
            // 이동력이 작은 부하(Btl 0142 글로리가드 chr 274)가 대장이 떠난 첫 자리에 영영 서 있었다(사용자 보고).
            int best = -1, bestDist = Math.Abs(follower.Col - target.Col) + Math.Abs(follower.Row - target.Row);
            for (int i = 0; i < range.Cost.Length; i++)
            {
                if (!range.CanReach(i)) continue;
                int c = i % Cols, r = i / Cols;
                if (!CanStand(c, r, follower)) continue;
                int dist = Math.Abs(c - target.Col) + Math.Abs(r - target.Row);
                if (dist < bestDist) (best, bestDist) = (i, dist);
            }
            if (best < 0) return;
            goal = best;
        }
        foreach (var step in range.PathTo(goal)) follower.Path.Enqueue(step);
        PayFollowerWalk(follower, range, goal);
        PlayWalkSound(follower);
    }

    /// <summary>
    /// 부하 걸음 값 — 경로 이동(0x2710/0x2711)도 끝처리 <c>0x10076380</c> 에서 출발 칸→도착 칸 길 비용(<c>0x10075f80</c>)을
    /// 부하 자신의 TP 에서 뺀다(<c>0x100763be~0x10076430</c> → <c>0x10071e20</c>). 부하는 TP 가 0 이하가 되어도 차례 끝 처리를 안 한다(<c>0x10071e3d</c>).
    /// 전에는 부하가 걸어도 TP 가 그대로라 늘 최대 예산으로 걸었다(감사3 L1). 걷기 시작할 때 한 번에 뺀다 — 값은 같다.
    /// </summary>
    private void PayFollowerWalk(UnitState follower, MoveRange range, int goal)
    {
        if (!range.CanReach(goal)) return;
        follower.Tp -= range.Cost[goal];
        follower.OriginCol = goal % Cols;
        follower.OriginRow = goal / Cols;
    }

    /// <summary>대장이 방금 확정한 자리 기준으로 부하 목표를 다시 잡고, 바로 한 걸음씩 걷게 한다.</summary>
    private void ReformFollowers(int leaderIndex)
    {
        AssignFormationTargets(leaderIndex);
        foreach (var follower in FollowersOf(leaderIndex)) TryAdvanceFollower(follower);
    }

    /// <summary>
    /// 프레임마다 — 걸음을 멈춘 대장의 부하들이 아직 제자리가 아니면 걸어가게 한다.
    /// </summary>
    /// <remarks>
    /// 원본은 <b>대장이 「이동만·이동+휴식」 명령을 냈을 때만</b> 진형을 다시 세운다(이동 + 기술이면 아예 안 짓는다).
    /// 데모는 명령 상자가 없어 「대장도 부하도 다 멈췄을 때」로 대신한다 — 결과는 같고 한 박자 늦다.
    /// </remarks>
    private void SyncFollowers()
    {
        for (int i = 0; i < _units.Length; i++)
        {
            var leader = _units[i];
            if (!leader.Alive || leader.LeaderIndex >= 0 || leader.LegionId == 0 || leader.IsBusy) continue;
            if (i == _skillLeader) continue;          // 이동 + 기술 중인 대장 — 진형을 안 짓는다(UseWorkRoutine)
            // 아직 전장 밖(증원 전)인 대장의 부하는 전장 밖 (0,0) 에 그대로 둔다 — 진형을 짜면 (0,0) 둘레 칸으로 걸어 나와
            // 맵 귀퉁이에 서 있었다(Btl 0142 끝 장면의 글로리가드 셋).
            if (!leader.OnField) continue;
            if (FollowersOf(i).Any(f => f.IsBusy)) continue;   // 아직 걷는 중이면 새 목표를 주지 않는다
            // 대장이 같은 칸에 그대로 서 있으면 진형을 다시 셈하지 않는다 — 칸을 넓게 훑어 값이 비싸다.
            if (_formationAt.TryGetValue(i, out var last) && last == (leader.Col, leader.Row)) continue;
            _formationAt[i] = (leader.Col, leader.Row);
            ReformFollowers(i);
        }

        // 방금 목표를 받았든, 예전에 예산이 모자라 못 따라갔든 — 목표 자리에 아직 못 간 부하는 계속 다시 시도한다.
        // 이게 없으면 대장이 한 번 크게 움직여 부하가 한 걸음에 못 따라잡은 뒤 대장이 그 자리에 눌러앉을 경우,
        // 그 부하는 대장이 다시 움직일 때까지(=영영) 원래 자리에 남아 플레이어가 닿을 수 없는 낙오자가 된다.
        foreach (var follower in _units)
        {
            if (!follower.Alive || follower.IsBusy || follower.LeaderIndex < 0 || !follower.OnField) continue;
            // 기술을 쓰는 대장의 부하는 다시 걷기 시작하지 않는다 — 걷기 시작하면 FollowersAttack 이 그 부하를 못 쓴다(감사5 L-A).
            if (follower.LeaderIndex == _skillLeader) continue;
            if (!_followerTarget.TryGetValue(follower, out var target) || (follower.Col, follower.Row) == target) continue;
            if (_nextFollowerRetry.TryGetValue(follower, out var next) && _lastTime < next) continue;
            _nextFollowerRetry[follower] = _lastTime + 0.5;
            TryAdvanceFollower(follower);
        }
    }

    /// <summary>
    /// 대장이 칠 때 부하들도 함께 친다(군단 행동, 상태 15) — <b>같은 대상이 아니라 제 기술로 제 겨냥</b>을 고른다.
    /// </summary>
    /// <remarks>
    /// 원본(<c>0x1005f320</c>)은 부하가 <b>제 어빌리티 목록</b>(분류 1·2·4 + 기본공격, 그 차례가 곧 우선순위)을 훑어
    /// <b>적 대상(<c>+0x1e</c> = 1)이면서 TP·사거리가 되는 첫 기술</b>을 고르고, 겨냥은
    /// <c>칸점수 × rnd / (rnd + 대장 대상까지 거리)</c> 라 <b>대장이 겨눈 칸 가까운 쪽을 크게 선호</b>한다.
    /// 데모는 난수 없이 「제 사거리 안의 적 중 대장 대상에 가장 가까운 쪽」으로 대신한다.
    /// 사거리 밖이면 원본은 걸어가서 치는데, 데모는 아직 그 자리에서 아무것도 안 한다.
    /// </remarks>
    /// <summary>사거리 밖이라 먼저 걸어가는 부하들 — 다 걸으면 대장 루틴이 치게 한다.</summary>
    private readonly List<(UnitState Follower, WorkData Work, int Col, int Row)> _followerStrikes = [];

    /// <summary>제 칸 하나만 갈 수 있는 이동 범위 — 자리를 옮기지 않는 겨냥(아군 패스·제자리 기술 합류)에 쓴다.</summary>
    private MoveRange StandOnly(UnitState u)
    {
        int n = Cols * Rows;
        var cost = new int[n];
        var prev = new int[n];
        Array.Fill(cost, int.MaxValue);
        Array.Fill(prev, -1);
        cost[u.Row * Cols + u.Col] = 0;
        return new MoveRange(cost, prev, new bool[n]) { Width = Cols };
    }

    /// <summary>
    /// 부하가 (col, row) 를 겨눠 그 기술을 쓴다 — 정상 work 실행(0x10075ff0): 광역기면 범위 안 전원이 맞고, 제 TP·SOUL·HP 를 낸다(0x10076380).
    /// 맞을 대상이 없어졌으면(걸어오는 사이 죽었거나 옮겨 갔다) 아무것도 안 하고 false.
    /// </summary>
    private bool FollowerStrike(UnitState follower, WorkData work, int col, int row, List<UnitState> dying)
    {
        if (follower.Data is not { } c) return false;
        var struck = WorkTargets(work, follower, col, row);
        if (struck.Count == 0) return false;
        follower.Facing = FacingToward(follower.Col, follower.Row, col, row);
        PlayAction(follower, 8);   // 동작 8 = 치는 순간(분석-모션)
        // 기본공격이 아니면 이펙트·소리를 같이 띄운다(ba-20 Q5). 동작 사슬은 아직 8 하나.
        if (work.Id != c.BasicWorkId)
        {
            if (work.Prepare is 2 or 3 or 5 or 6) _pendingSounds.Add((_lastTime, 694, UnitFoot(follower).X));   // 시전 소리 — 대장은 UseWorkRoutine 이 낸다
            ScheduleAbilitySounds(work);
            SpawnAbilityEffects(work, follower, col, row);
        }
        foreach (int ti in struck) ApplyWork(follower, work, _units[ti], dying);
        PayWorkCost(follower, work);
        return true;
    }

    /// <summary>
    /// 대장이 기술을 쓰면 부하들도 같은 패스로 제 기술을 쓴다(상태 15, 분석-군단 「부하가 대장 차례에 하는 일」).
    /// <paramref name="allyPass"/> 면 아군 패스(<c>0x1005fa90</c>) — 대장이 아군 대상 기술(방식 4)을 쓸 때로, 부하는 회복·보조 기술을
    /// 대장이 겨눈 아군 가까이에 쓴다. 아니면 적 패스(<c>0x1005fd00</c>) — 피해 기술과 기본공격으로 적을 친다.
    /// </summary>
    /// <param name="walk">대장 대상 쪽으로 걸어가 치기를 해도 되나 — 겨눈 대상이 없는 제자리 기술(방식 2)이면 <paramref name="target"/> 은 대장 자신이라 걷지 않는다.</param>
    private void FollowersAttack(int leaderIndex, UnitState target, List<UnitState> dying, bool allyPass = false, WorkData? leaderWork = null, bool walk = true)
    {
        if (_db is null) return;
        // 대장이 쓴 기술의 +0x41 이 0 이면 부하는 안 따라 친다(0x1005fd00) — 피해 work 469개가 그렇다(fg-21 ⑯).
        if (leaderWork is { FollowersAct: false }) return;
        // 잡힌 칸 — 이번에 부하가 고른 설 칸과, 지금 걷고 있는 유닛(다른 군단 부하 포함)의 도착 칸. 아직 출발 칸에 서 있어 LiveUnitAt 에 안 잡힌다.
        var approached = new HashSet<int>();
        foreach (var walker in _units)
            if (walker.Alive && walker.Path.Count > 0) { var (lc, lr) = walker.Path.Last(); approached.Add(lr * Cols + lc); }
        foreach (var follower in FollowersOf(leaderIndex))
        {
            // 걷는 부하는 UseWorkRoutine 첫머리(WaitFollowersStopped)가 이미 세웠다 — 여기서 바쁜 건 동작 중인 부하뿐.
            if (follower.Data is not { } c || follower.IsBusy || !follower.OnField || follower.Hp <= 0) continue;   // 판 밖·쓰러질 부하는 뺀다
            // 부하 겨냥(0x1005e4d0, ba-20 Q2 = N11): 갈 수 있는 칸(층 2) + 그 칸들의 사거리(층 12)를 <b>한 번에</b> 채점한다 —
            // 점수 = 값 × Num74 × 10 / (Num74 + |칸 − 대장이 겨눈 칸|), 값은 0x1005c510(최소 대상·종류 3 이득·회복 문턱, WorthUsing).
            // 전에는 제자리 사거리 안의 유닛 칸만 먼저 보고, 없을 때만 대장 대상이 닿는 가장 싼 칸으로 걸어가 대장 대상을 쳤다.
            // 아군 패스(0x1005e820 → 0x10075420)와 걷지 않는 호출은 설 칸이 제 칸 하나다.
            int fi = Array.IndexOf(_units, follower), here = follower.Row * Cols + follower.Col;
            bool planned = false;
            foreach (var work in FollowerWorks(c, allyPass))
            {
                if (!CanAfford(follower, work)) continue;
                // 이동 예산은 부하 <b>자신의</b> TP 에서 그 기술 값을 남긴 만큼이다(0x1005f40f~0x1005f430, 감사3 L1).
                var range = allyPass || !walk ? StandOnly(follower) : ComputeRange(follower, work.Id);
                if (range is null) continue;
                if (BestUse(fi, work, range, anchor: (target.Col, target.Row), taken: approached) is not { } use) continue;
                planned = true;
                if (use.Stand == here) { FollowerStrike(follower, work, use.Col, use.Row, dying); break; }
                foreach (var step in range.PathTo(use.Stand)) follower.Path.Enqueue(step);
                PayFollowerWalk(follower, range, use.Stand);
                PlayWalkSound(follower);
                _followerTarget[follower] = (use.Stand % Cols, use.Stand / Cols);
                _followerStrikes.Add((follower, work, use.Col, use.Row));   // 다 걸어간 뒤 친다(명령1 이동 → 명령2 기술, 0x1005f1c0)
                approached.Add(use.Stand);
                break;
            }
            if (!walk) continue;
            if (planned || allyPass) continue;

            // 칠 수 있는 칸이 없으면 <b>다가가기만</b> 한다(적 고르개 뒷부분 0x1005f4f4~0x1005f5f4 → 이동만 0x2710, ba-20 Q4) — 대장이 겨눈 칸에
            // 가장 가까이 닿는 칸으로 걷는다. 전에는 그 자리에 섰고, 기술 뒤에는 진형 다시 세우기도 안 불려 뒤처진 부하가 영영 못 따라왔다.
            // 예산은 보통 이동 범위와 같다(ComputeRange — AI 부하는 기본공격 값을 남긴 만큼). 원본 예산은 가설.
            if (ComputeRange(follower) is not { } near) continue;
            var toAim = CostMapFrom(follower, target.Col, target.Row);
            if (toAim is null) continue;
            int go = -1, goCost = int.MaxValue, goOwn = int.MaxValue;
            for (int idx = 0; idx < Cols * Rows; idx++)
            {
                if (!near.CanReach(idx) || toAim[idx] == int.MaxValue || approached.Contains(idx)) continue;
                if (LiveUnitAt(idx % Cols, idx / Cols) is { } other && other != follower) continue;
                if (toAim[idx] < goCost || toAim[idx] == goCost && near.Cost[idx] < goOwn) (go, goCost, goOwn) = (idx, toAim[idx], near.Cost[idx]);
            }
            if (go < 0 || near.Cost[go] <= 0) continue;
            approached.Add(go);                    // 뒤 부하가 같은 칸을 고르지 않게(0x1005ff9a)
            foreach (var step in near.PathTo(go)) follower.Path.Enqueue(step);
            PayFollowerWalk(follower, near, go);
            PlayWalkSound(follower);
            _followerTarget[follower] = (go % Cols, go / Cols);
        }
    }

    /// <summary>
    /// 부하가 고를 수 있는 기술 — AI 목록 <c>0x10032370</c>(분류 1·2·4, 어빌리티 번호 ≥ 200·레벨 0 은 건너뜀, 그 차례가 우선순위) 다음에 기본공격.
    /// 기술은 <b>효과 대상 <c>+0x1e</c></b> 로 가른다 — 적 패스는 1(<c>0x1005f3b1</c>), 아군 패스는 4. 종류(피해·보조)는 안 본다.
    /// 전에는 분류 검사 없이 적 패스 = 피해 기술, 아군 패스 = 방식 4 비피해 기술로 골랐다(원본차이-AI 18). 아군 패스엔 기본공격이 없다.
    /// </summary>
    private IEnumerable<WorkData> FollowerWorks(CharacterData c, bool allyPass = false)
    {
        if (_db is not { } db) yield break;
        foreach (var (abilityId, level) in c.Abilities)
            if (abilityId < 200 && level > 0 && db.Abilities.TryGetValue(abilityId, out var ab) && ab.Category is 1 or 2 or 4
                && ab.TryWorkAt(level, out int wid) && Work(wid) is { } w && w.AreaMode == (allyPass ? 4 : 1))
                yield return w;
        if (!allyPass && Work(c.BasicWorkId) is { } basic) yield return basic;
    }

    /// <summary>
    /// 부하가 마비·빙결·이동 불가(5·6·25)에 걸리면 군단에서 떨어진다(<c>0x10072fd0(대장, 부하)</c>, ba-20 C2) — 대장 연결이 끊기고
    /// 편 4 면 편 3(동맹 AI)이 되며 HP 를 제 최대치에서 자른다. 대장 부하 수가 줄어 군단기가 꺼질 수 있다.
    /// 떨어진 뒤 혼자 움직이는지는 가설(차례 고르기가 대장 없는 유닛을 받으므로 그렇게 둔다).
    /// </summary>
    private void DetachFollower(UnitState u)
    {
        if (u.LeaderIndex < 0) return;
        u.LeaderIndex = -1;
        u.FormationSlot = -1;
        u.Detached = true;
        if (u.Side == 4) { u.Side = 3; u.Awake = true; }
        RefreshUnitStats(u);
    }

    /// <summary>대장이 쓰러지면 — 첫 부하가 새 대장이 되고 세력이 0.6배가 된다.</summary>
    private void PromoteFollower(int leaderIndex)
    {
        var followers = FollowersOf(leaderIndex);
        if (followers.Count == 0) return;
        // 플레이어 군단의 대장이 죽으면 떨어져 나온 부하들은 <b>편 3(동맹 AI)</b>이 된다(0x100716c0) — 더는 명령할 수 없다.
        if (_units[leaderIndex].Side == 4)
            foreach (var f in followers) { f.Side = 3; f.Awake = true; }
        var newLeader = followers[0];
        int newIndex = Array.IndexOf(_units, newLeader);
        newLeader.LeaderIndex = -1;
        newLeader.FormationSlot = -1;
        newLeader.LegionId = _units[leaderIndex].LegionId;
        newLeader.LegionPowerPercent = _units[leaderIndex].LegionPowerPercent * 6 / 10;
        // 다시 붙는 부하(0x10072ef0(새대장, 부하, 0))는 TP 를 새 대장 것으로 덮는다(감사5 L-C). AI 꼬리는 처음부터 대장 레코드 복사라 같다.
        foreach (var follower in followers.Skip(1)) { follower.LeaderIndex = newIndex; follower.Tp = newLeader.Tp; }
        RefreshUnitStats(newLeader);
    }

    /// <summary>
    /// 기술 앞 — 그 대장의 부하가 아직 걷고 있으면 다 설 때까지 기다린다(원본은 이동 명령 뒤 상태 15 갈래 1 이 <c>0x1006e320</c> 로 모두 설 때까지
    /// 다음 명령을 안 받는다). 전에는 <see cref="FollowersAttack"/> 가 걷는 부하를 건너뛰어 대장이 걸은 직후 친 공격에 부하가 빠졌다(감사5 L-A). 5초 상한.
    /// </summary>
    private IEnumerable<bool> WaitFollowersStopped(int leaderIndex)
    {
        for (double end = _lastTime + 5; _lastTime < end && FollowersOf(leaderIndex).Any(f => f.OnField && f.IsBusy);) yield return true;
    }

    /// <summary>
    /// 능력치 주인 — 부하면 대장, 아니면 자신. 원본 DEX(<c>0x1007ae50</c>)·최대 TP(<c>0x1007aeb0</c>)·STP 제수(<c>0x1007afa0</c>) 게터는
    /// 부하(<c>+0x4f0</c>)면 <c>+0x508</c> 사슬을 따라 <b>대장 유닛에서</b> 값을 읽는다(감사3 L2).
    /// </summary>
    private UnitState StatOwner(UnitState u) =>
        u.LeaderIndex >= 0 && u.LeaderIndex < _units.Length ? _units[u.LeaderIndex] : u;

    /// <summary>
    /// 판정·이동 비용에 쓰는 능력치 — <see cref="EffectiveData"/> 에 부하면 DEX 만 <b>대장의</b> DEX(대장 CChr DEX + 장비 + 상태 30 − 상태 1)로 바꾼다
    /// (<c>0x1007ae50</c>). 부하 자신의 장비 DEX 는 <see cref="GameDatabase.Dex"/> 가 다시 더하므로 미리 뺀다.
    /// </summary>
    private CharacterData? CombatData(UnitState u)
    {
        var own = EffectiveData(u);
        if (own is null || _db is not { } db) return own;
        // 군단 부하 보정 PSY·DEP(For +0x18·+0x1c × 세력 / 100) — 판정도 이것으로 셈한다(0x1007ade0·0x1007af20, 감사4 S1).
        var (_, lPsy, lDep) = LegionBonusFor(u);
        if (lPsy != 0 || lDep != 0)
            own = own with { Psy = (ushort)Math.Max(0, own.Psy + lPsy), Dep = (ushort)Math.Max(0, own.Dep + lDep) };
        if (StatOwner(u) is var owner && owner == u || EffectiveData(owner) is not { } lead) return own;
        int dex = db.Dex(lead) - db.EquipBonus(own, 0x1e);
        return own with { Dex = (ushort)Math.Max(0, dex) };
    }

    /// <summary>
    /// 대장이 쉬면 부하도 모두 쉰다 — 휴식 <c>0x1005f610</c>·이동+휴식 <c>0x1005f870</c> 은 대장(<c>+0x4ef</c>)이면 부하마다 명령 0x2716 을 먼저 내고
    /// (<c>0x1005f62a~0x1005f63d</c>), 휴식 <c>0x1007a790</c> 은 부하에게도 (최대HP − HP) × 남은 TP / 최대 TP(대장 값) × Num35% 를 채우고 TP 를 0 으로 한다
    /// (<c>0x1007a87b~0x1007a885</c>). 부하는 차례 끝 처리를 안 한다. 전에는 대장만 쉬어 부하 HP 가 전투 내내 안 돌아왔다(감사3 L3).
    /// </summary>
    private void RestFollowers(int leaderIndex)
    {
        if (_db is not { } db || _units[leaderIndex].LeaderIndex >= 0) return;
        foreach (var f in FollowersOf(leaderIndex))
        {
            if (f.Tp > 0 && f.MaxTp > 0 && !f.HasStatus(26))
            {
                int heal = (int)((long)(f.MaxHp - f.Hp) * f.Tp / f.MaxTp * db.N(35) / 100);
                if (heal > 0)
                {
                    int before = f.Hp;
                    f.Hp += heal;
                    ShowNumber(f, db.T(159), HealColor2, rise: false, count: (before, f.Hp));
                }
            }
            if (Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"follower rest: chr {f.ChrCode} (leader {_units[leaderIndex].ChrCode}) TP {f.Tp}/{f.MaxTp} → 0, HP {f.Hp}/{f.MaxHp}" + Environment.NewLine);
            if (f.Tp > 0) f.Tp = 0;   // 빚(음수 TP)은 그대로 남는다(ba-20 K1)
        }
    }

    /// <summary>부하가 대장에게서 받는 능력치 보정 — 세력 × For 보정 / 100(분석-군단 1절).</summary>
    private (int Lp, int Psy, int Dep) LegionBonusFor(UnitState unit)
    {
        if (unit.LeaderIndex < 0 || (uint)unit.LeaderIndex >= _units.Length) return (0, 0, 0);
        var leader = _units[unit.LeaderIndex];
        if (Mos.Legions().GetValueOrDefault(leader.LegionId) is not { } legion) return (0, 0, 0);
        int power = leader.LegionPowerPercent;
        return (legion.LpBonus * power / 100, legion.PsyBonus * power / 100, legion.DepBonus * power / 100);
    }
}
