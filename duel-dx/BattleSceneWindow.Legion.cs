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
        var legions = Legions();
        var list = new List<UnitState>();
        var followers = new List<(int Leader, int Slot, UnitState Unit)>();

        foreach (var record in scene.Roster)
        {
            int leaderIndex = list.Count;
            var leader = new UnitState(record);
            list.Add(leader);
            if (record.Legion == 0 || legions.GetValueOrDefault(record.Legion) is not { } legion) continue;

            var cells = LegionData.FormationCells[Math.Clamp((int)legion.Formation, 0, 5)];
            var taken = new HashSet<(int, int)>(list.Select(u => (u.Col, u.Row)).Concat(followers.Select(f => (f.Unit.Col, f.Unit.Row))));
            for (int i = 0; i < legion.Members.Length && i < cells.Length; i++)
            {
                var (dx, dy) = RotateFormation(cells[i], record.Facing);
                int col = Math.Clamp(record.Col + dx, 0, Cols - 1), row = Math.Clamp(record.Row + dy, 0, Rows - 1);
                // 같은 칸에 둘이 서지 않게 — 겹치면 대장 둘레에서 빈 칸을 찾는다.
                for (int r = 1; r <= 3 && taken.Contains((col, row)); r++)
                    for (int ny = -r; ny <= r && taken.Contains((col, row)); ny++)
                        for (int nx = -r; nx <= r && taken.Contains((col, row)); nx++)
                        {
                            int cx = Math.Clamp(record.Col + nx, 0, Cols - 1), cy = Math.Clamp(record.Row + ny, 0, Rows - 1);
                            if (!taken.Contains((cx, cy))) (col, row) = (cx, cy);
                        }
                taken.Add((col, row));
                var member = new UnitState(record with { ChrCode = legion.Members[i], Col = col, Row = row, Legion = 0 })
                {
                    LeaderIndex = leaderIndex,
                    FormationSlot = i,
                };
                followers.Add((leaderIndex, i, member));
            }
        }

        foreach (var (_, _, member) in followers) list.Add(member);

        // 아군이 하나도 없는 전투(개별훈련용 던젼 0060 처럼)는 배치 칸에 파티를 세운다.
        if (!list.Any(u => u.PlayerControlled) && scene.Placement is { Count: > 0 } spots)
        {
            var party = _party.Keys.Count > 0 ? _party.Keys.ToList()
                                              : [.. DemoScene.Fallback.Roster.Where(u => u.IsAlly).Select(u => u.ChrCode)];
            for (int i = 0; i < spots.Count && i < party.Count; i++)
                list.Add(new UnitState(new DemoUnit(party[i], spots[i].Col, spots[i].Row, 4, 0, spots[i].Facing)));
        }
        return [.. list];
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
        if (_map is { } map && (col >= map.Cols || row >= map.Rows || (map.FlagsAt(col, row) & 0x9) != 0)) return false;
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
        if (ComputeRange(follower) is not { } range || !range.CanReach(target.Row * Cols + target.Col)) return;
        foreach (var step in range.PathTo(target.Row * Cols + target.Col)) follower.Path.Enqueue(step);
        PlayWalkSound(follower);
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
            if (!follower.Alive || follower.IsBusy || follower.LeaderIndex < 0) continue;
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
    private void FollowersAttack(int leaderIndex, UnitState target, List<UnitState> dying)
    {
        if (_db is null) return;
        foreach (var follower in FollowersOf(leaderIndex))
        {
            if (follower.Data is not { } c) continue;
            foreach (var work in FollowerWorks(c))
            {
                if (!CanAfford(follower, work)) continue;
                // 제 사거리 안의 적 중 대장이 겨눈 칸에 가장 가까운 쪽.
                var pick = _units
                    .Where(u => u.Alive && SeesAsFoe(follower, u)
                                && InWorkRange(work, follower.Col, follower.Row, u.Col, u.Row, follower))
                    .OrderBy(u => Math.Abs(u.Col - target.Col) + Math.Abs(u.Row - target.Row))
                    .FirstOrDefault();
                if (pick is null) continue;
                follower.Facing = FacingToward(follower.Col, follower.Row, pick.Col, pick.Row);
                PlayAction(follower, 8);   // 동작 8 = 치는 순간(분석-모션)
                ApplyWork(follower, work, pick, dying);
                break;
            }
        }
    }

    /// <summary>부하가 고를 수 있는 기술 — 익힌 어빌리티(그 차례가 우선순위) 다음에 기본공격.</summary>
    private IEnumerable<WorkData> FollowerWorks(CharacterData c)
    {
        if (_db is not { } db) yield break;
        foreach (var (abilityId, level) in c.Abilities)
            if (db.Abilities.TryGetValue(abilityId, out var ab) && ab.WorkByLevel.TryGetValue(level, out int wid)
                && Work(wid) is { IsDamage: true } w)
                yield return w;
        if (Work(c.BasicWorkId) is { } basic) yield return basic;
    }

    /// <summary>대장이 쓰러지면 — 첫 부하가 새 대장이 되고 세력이 0.6배가 된다.</summary>
    private void PromoteFollower(int leaderIndex)
    {
        var followers = FollowersOf(leaderIndex);
        if (followers.Count == 0) return;
        var newLeader = followers[0];
        int newIndex = Array.IndexOf(_units, newLeader);
        newLeader.LeaderIndex = -1;
        newLeader.FormationSlot = -1;
        newLeader.LegionId = _units[leaderIndex].LegionId;
        newLeader.LegionPowerPercent = _units[leaderIndex].LegionPowerPercent * 6 / 10;
        foreach (var follower in followers.Skip(1)) follower.LeaderIndex = newIndex;
        RefreshUnitStats(newLeader);
    }

    /// <summary>부하가 대장에게서 받는 능력치 보정 — 세력 × For 보정 / 100(분석-군단 1절).</summary>
    private (int Lp, int Psy, int Dep) LegionBonusFor(UnitState unit)
    {
        if (unit.LeaderIndex < 0 || (uint)unit.LeaderIndex >= _units.Length) return (0, 0, 0);
        var leader = _units[unit.LeaderIndex];
        if (Legions().GetValueOrDefault(leader.LegionId) is not { } legion) return (0, 0, 0);
        int power = leader.LegionPowerPercent;
        return (legion.LpBonus * power / 100, legion.PsyBonus * power / 100, legion.DepBonus * power / 100);
    }
}
