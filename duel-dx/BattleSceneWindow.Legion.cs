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

    /// <summary>진형 칸은 방향 0(위) 기준이다 — 대장이 보는 쪽으로 돌린다.</summary>
    private static (int Dx, int Dy) RotateFormation((int Dx, int Dy) cell, Facing facing) => facing switch
    {
        Facing.Up => cell,
        Facing.Down => (-cell.Dx, -cell.Dy),
        Facing.Left => (cell.Dy, -cell.Dx),
        _ => (-cell.Dy, cell.Dx),
    };

    /// <summary>그 인물의 부하들(살아 있는 것만).</summary>
    private List<UnitState> FollowersOf(int leaderIndex) =>
        [.. _units.Where(u => u.Alive && u.LeaderIndex == leaderIndex)];

    /// <summary>부하가 설 칸 — 대장 자리에 진형을 얹고, 막혔으면 대장 둘레에서 가까운 빈 칸.</summary>
    private (int Col, int Row) FormationCellFor(UnitState leader, UnitState follower)
    {
        if (Legions().GetValueOrDefault(leader.LegionId) is not { } legion || follower.FormationSlot < 0)
            return (leader.Col, leader.Row);

        var cells = LegionData.FormationCells[Math.Clamp((int)legion.Formation, 0, 5)];
        var (dx, dy) = RotateFormation(cells[Math.Clamp(follower.FormationSlot, 0, cells.Length - 1)], leader.Facing);
        int col = leader.Col + dx, row = leader.Row + dy;
        if (CanStand(col, row, follower)) return (col, row);

        for (int radius = 1; radius <= 3; radius++)
            for (int ny = -radius; ny <= radius; ny++)
                for (int nx = -radius; nx <= radius; nx++)
                    if (CanStand(leader.Col + nx, leader.Row + ny, follower)) return (leader.Col + nx, leader.Row + ny);
        return (follower.Col, follower.Row);
    }

    private bool CanStand(int col, int row, UnitState who)
    {
        if ((uint)col >= Cols || (uint)row >= Rows) return false;
        if (_map is { } map && (col >= map.Cols || row >= map.Rows || (map.FlagsAt(col, row) & 0x9) != 0)) return false;
        return LiveUnitAt(col, row) is not { } other || other == who;
    }

    /// <summary>대장이 움직인 뒤 — 부하들을 제 진형 칸으로 옮긴다(걸어가는 대신 바로 선다).</summary>
    private void MoveFollowers(int leaderIndex)
    {
        var leader = _units[leaderIndex];
        foreach (var follower in FollowersOf(leaderIndex))
        {
            var (col, row) = FormationCellFor(leader, follower);
            if (col == follower.Col && row == follower.Row) continue;
            follower.WarpTo(col, row);
            follower.Facing = leader.Facing;
            PlayWalkSound(follower);
        }
    }

    /// <summary>프레임마다 — 걸음을 멈춘 대장의 부하들을 제자리로 데려온다.</summary>
    private void SyncFollowers()
    {
        for (int i = 0; i < _units.Length; i++)
        {
            var leader = _units[i];
            if (!leader.Alive || leader.LeaderIndex >= 0 || leader.LegionId == 0 || leader.IsBusy) continue;
            MoveFollowers(i);
        }
    }

    /// <summary>대장이 친 대상을 부하들도 함께 친다(군단 행동, 상태 15).</summary>
    private void FollowersAttack(int leaderIndex, UnitState target, List<UnitState> dying)
    {
        if (_db is not { } db) return;
        foreach (var follower in FollowersOf(leaderIndex))
        {
            if (!target.Alive || follower.Data is not { } c || Work(c.BasicWorkId) is not { } basic) continue;
            if (!InWorkRange(basic, follower.Col, follower.Row, target.Col, target.Row, follower)) continue;
            follower.Facing = FacingToward(follower.Col, follower.Row, target.Col, target.Row);
            PlayAction(follower, 8);   // 동작 8 = 치는 순간(분석-모션)
            ApplyWork(follower, basic, target, dying);
        }
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
