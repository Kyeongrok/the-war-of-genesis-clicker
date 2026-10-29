using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

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
internal sealed unsafe partial class BattleScene
{
    /// <summary>대장 세력 기본값 — 원본은 <c>CChr+0x148[군단]</c> 이 모두 1000 으로 시작한다.</summary>
    internal const int LegionPower = 1000;

    /// <summary>전투에 세울 인물들 — 군단이 붙은 대장 뒤에 부하를 끼워 넣는다.</summary>
    internal UnitState[] BuildUnits(DemoScene scene)
    {
        var legions = host.Mos.Legions();
        var list = new List<UnitState>();
        _deployMovable.Clear();
        _deployBench.Clear();
        _part1Riders.Clear();
        Part1.Enchant.Clear();
        Part1.Buffs.Clear();
        Part1.Ailments.Clear();
        Part1.Stances.Clear();
        _part1Counters.Clear();
        _part1LostMounts.Clear();
        _part1TeamMounts.Clear();
        _part1Mercs.Clear();
        _part1ProcRunning = false;
        var followers = new List<UnitState>();

        foreach (var rosterRecord in scene.Roster)
        {
            // 인물 레코드의 군단 칸(파일 15) — 머리 워드 7 이 켜진 전투에서 <b>값이 1 일 때만</b> 그 인물에게 배속된 군단(CChr+0x1c)으로 바꾼다
            // (LoadBtl 0x100634f9~0x10063503). 값 0·2 이상은 Btl 값 그대로(0x1006350c), 워드 7 이 꺼져 있으면 1 도 그대로(For[1] 「default」 = 부하 없음).
            // 전에는 편 4 면 워드 7·값과 상관없이 배속 군단으로 덮어써, 군단 금지 전투에도 부하가 나오고 이야기 전투의 편대(0292 마리아 → 5)가 바뀌었다(감사3 L4).
            var record = rosterRecord.Legion == 1 && scene.LegionsAllowed
                ? rosterRecord with { Legion = host.Mos._unitLegion.GetValueOrDefault(rosterRecord.ChrCode) } : rosterRecord;
            // 군단 편집기가 리더를 바꿔 두었으면(legions/*.json 의 leader) 전투 자료의 적·동맹 대장을 그 인물로 세운다(ed-1).
            if (record.Side != 4 && record.Legion > 1 && legions.GetValueOrDefault(record.Legion) is { Leader: > 0 } withLeader)
                record = record with { ChrCode = withLeader.Leader };
            int leaderIndex = list.Count;
            list.Add(new UnitState(record));
            followers.AddRange(SpawnFollowers(record, leaderIndex, legions, list.Concat(followers)));
        }

        list.AddRange(followers);
        if (scene.IsPart1)
        {
            LinkPart1Squads(list);
            // 전투 파일이 태워 둔 사람 — 판 밖에서 기다리다 탈것이 쓰러지면 그 칸에 내려선다(일행 탑승과 같은 틀).
            // 원본은 그 레코드가 <b>사람</b>이고 탈것은 거기 붙은 것이라(+0x1cc, 0x100364b0) 사건의 10000+N 이 내린 뒤에도 그 사람을 가리킨다 —
            // 탄 이에게도 같은 레코드 번호를 준다. 전에는 탈것만 그 번호라, 탈것이 쓰러지면 Btl 20072 탈출 칸(조건 302)에 살라딘이 서도 안 끝났다(카페 제보).
            foreach (var mount in list.Where(u => u.Part1Rider > 0).ToList())
            {
                var rider = new UnitState(new DemoUnit(mount.Part1Rider, 0, 0, mount.Side, 0, mount.Facing) { Record = mount.Record }) { OnField = false, Awake = true };
                list.Add(rider);
                _part1Riders[mount] = rider;
            }
        }

        // 파트1 판에 편 4 로 나온 사람은 일행에 들인다(배치 칸이 없는 판도) — 다음 판의 배치 칸에 선다.
        if (scene.IsPart1) host.Part1.Join(list.Where(u => u.Side == 4 && u.ChrCode >= Part1Game.ChrBase).Select(u => u.ChrCode));

        // 배치 칸이 있는 전투는 파티를 거기 세운다 — 원본은 배치 칸이 있으면 싱글에서 늘 「캐릭터 배치」(상태 2, 0x10066d00)로 시작한다.
        // 전에는 고정 아군이 하나라도 있으면 건너뛰어, Btl 0140 처럼 진(고정)만 서고 나머지 파티가 안 나왔다(사용자 보고, 26개 전투).
        // 이미 Btl 에 선 사람은 빼고, 빈 칸에만 차례로 세운다(자동 배치 0x10066480 과 같은 차례). 고르는 화면은 아직 없다.
        if (scene.Placement is { Count: > 0 } spots)
        {
            // 세우는 것은 <b>동료</b>(801 로 들어온 인물)다 — _party 에는 앞 전투가 편 3 으로 끼워 준 제이슨·용병까지 남아 있어
            // 그대로 쓰면 훈련용 던젼에 제이슨이 따라 들어왔다(사용자 지적). 동료 목록이 없으면 예전대로.
            // 파트1 용병(908 · 용병 길드)은 배치 칸에 따로 서지 않는다 — 아래에서 판 밖에 만들어 두고, 배치종료 때 「용병 배치」로 고른 인물의 3×3 부하로 붙인다(Part1MercDeploy).
            var party = scene.IsPart1 ? [.. host.Part1.ActiveMembers()]
                      : host.Mos._members.Count > 0 ? host.Mos._members.Where(host._party.ContainsKey).ToList()
                      : host._party.Keys.Count > 0 ? host._party.Keys.ToList()
                                              : [.. DemoScene.Fallback.Roster.Where(u => u.IsAlly).Select(u => u.ChrCode)];
            party = [.. party.Where(chr => !list.Any(u => u.ChrCode == chr))];
            var free = spots.Where(sp => !list.Any(u => u.Col == sp.Col && u.Row == sp.Row)).ToList();
            // 배치 단계로 세우는 파티원은 「군단사용」(명단 +0x30, 기본 = 워드 7 켜짐 && 배속 있음)이면 배속 군단(+0x12c)으로 부하를 만든다
            // (0x100673dd~0x10067717, 분석-전투 ba-6). 전에는 늘 군단 0 이라 모세스에서 배속해도 대부분의 전투에서 부하가 안 나왔다(감사3 L4).
            // 여기서는 기본값(켜짐)으로 만들고, 배치 창의 「군단사용」 단추가 끄면 배치종료 때 부하를 지운다(Deploy.cs). 부하는 파티원들 뒤에 붙인다 — 맵 밖 여분을 지울 때 DropUnits 가 번호를 다시 맞춘다.
            var placedFollowers = new List<UnitState>();
            int LegionOf(int chr) => scene.LegionsAllowed ? host.Mos._unitLegion.GetValueOrDefault(chr) : 0;
            // 파트1 — 일행 창에서 탈것을 정한 사람은 탈것으로 선다(0x100364b0: 탈것 유닛을 그 칸에, 탄 이는 판 밖). 탈것 수만큼만.
            var mountsLeft = scene.IsPart1 ? new Dictionary<int, int>(host.Part1.Mounts) : [];
            UnitState? RiderFor(int chr)
            {
                if (!scene.IsPart1 || !host.Part1.Riding.TryGetValue(chr, out int mount) || mountsLeft.GetValueOrDefault(mount) <= 0) return null;
                mountsLeft[mount]--;
                var rider = new UnitState(new DemoUnit(chr, 0, 0, 4, 0, Facing.Up)) { OnField = false };
                list.Add(rider);
                return rider;
            }
            // 파트1 용병(일행이 아닌 사람)은 편 3 — 파트2 AI 가 알아서 싸운다(사용자 결정: 파트1 AI 보다 파트2 AI 가 낫다). 「동맹을 AI 가 움직임」을 끄면 직접 조종.
            int SideOf(int chr) => scene.IsPart1 && !host.Part1.IsOurs(chr) ? 3 : 4;
            // 탈 수 있는지는 배치할 때 다시 본다 — 열쇠(보정 248)를 빼거나 어빌리티가 없어졌으면 걸어서 선다.
            var items = scene.IsPart1 ? Part1Item.LoadAll() : [];
            bool CanRide(int chr, int mount) =>
                Part1Chr.Load(chr - Part1Game.ChrBase) is { } raw && host.Part1.Apply(raw) is var c
                && c.CanRide(mount - Part1Game.ChrBase, c.Equipment.Select(id => Part1Item.ById(items, id)));
            // 「탑승 금지」 판(머리 낱말 9)은 태우지 않는다 — 원본은 배치 창의 「탑승」으로만 태우고 그 단추가 잠긴다(0x100948b5).
            bool ridingForbidden = scene.IsPart1 && Part1MountDeploy.IsForbidden(scene.Id);
            int Mounted(int chr) => scene.IsPart1 && !ridingForbidden && host.Part1.Riding.TryGetValue(chr, out int mount) && mountsLeft.GetValueOrDefault(mount) > 0
                                    && CanRide(chr, mount) ? mount : chr;
            for (int i = 0; i < free.Count && i < party.Count; i++)
            {
                int stand = Mounted(party[i]);
                var rider = stand != party[i] ? RiderFor(party[i]) : null;
                var rec = new DemoUnit(stand, free[i].Col, free[i].Row, SideOf(party[i]), LegionOf(party[i]), free[i].Facing);
                var placed = new UnitState(rec);
                if (rider != null) { _part1Riders[placed] = rider; _part1TeamMounts.Add(placed); }
                if (SideOf(party[i]) == 3) _part1Mercs.Add(placed);
                list.Add(placed);
                _deployMovable.Add(placed);
                placedFollowers.AddRange(SpawnFollowers(rec, list.Count - 1, legions, list.Concat(placedFollowers)));
            }
            // 칸이 모자라 못 선 파티원은 맵 밖(0,0)에 만들어 둔다 — 배치 단계에서 바꿔 세울 수 있게. 배치 단계를 안 열면 지운다(Deploy.cs).
            for (int i = free.Count; i < party.Count; i++)
            {
                int stand = Mounted(party[i]);
                var rider = stand != party[i] ? RiderFor(party[i]) : null;
                var rec = new DemoUnit(stand, 0, 0, SideOf(party[i]), LegionOf(party[i]), Facing.Up);
                var bench = new UnitState(rec);
                if (rider != null) { _part1Riders[bench] = rider; _part1TeamMounts.Add(bench); }
                if (SideOf(party[i]) == 3) _part1Mercs.Add(bench);
                list.Add(bench);
                _deployMovable.Add(bench);
                _deployBench.Add(bench);
                placedFollowers.AddRange(SpawnFollowers(rec, list.Count - 1, legions, list.Concat(placedFollowers)));
            }
            list.AddRange(placedFollowers);
            // 파트1 용병 — 판 밖(0,0)에 수만큼. 옮길 수 있는 사람(_deployMovable)으로 두어 배치종료 · 불러오기가 안 쓴 것을 지운다.
            if (scene.IsPart1)
                foreach (var (chr, count) in host.Part1.Mercenaries.OrderBy(m => m.Key))
                    for (int n = 0; n < count; n++)
                    {
                        var merc = new UnitState(new DemoUnit(chr, 0, 0, 3, 0, Facing.Up)) { OnField = false, Awake = true };
                        list.Add(merc);
                        _part1Mercs.Add(merc);
                        _deployMovable.Add(merc);
                    }
        }
        host.MercDeploy.Reset(scene.Id);   // 파트1 「용병 배치」 — 다른 전투면 고른 칸을 지운다(창도 닫는다)
        host.MountDeploy.Reset(scene.Id);  // 파트1 「탑승」 — 창을 닫고 탑승 금지(낱말 9)를 읽는다
        return [.. list];
    }

    /// <summary>
    /// 파트1 탄 이를 판에 내려놓는다(<c>0x100365e0</c>) — 그 칸에 서고, 방향은 탈것을 따르고, 소울(+0xe8)은 40 으로 돌아간다(<c>0x100366e3</c>).
    /// 내리기(166 → Attack 399)와 탈것이 쓰러질 때(<c>0x10034684</c>) 둘 다 이 길이다. 원본은 그 칸에 못 서면 탄 이가 HP 0 이 되지만(<c>0x100366fb</c>)
    /// 리메이크는 부르는 쪽이 가장 가까운 빈 칸(<see cref="FreeCellNear"/>)을 넘긴다.
    /// </summary>
    internal void PlaceRider(UnitState mount, UnitState rider, int col, int row)
    {
        rider.OnField = true;
        rider.ResetTo(col, row);
        rider.StartCol = col;
        rider.StartRow = row;
        rider.Facing = mount.Facing;
        if (host._scene.IsPart1) rider.Soul = Math.Min(Math.Max(rider.MaxSoul, 0), 40);
    }

    /// <summary>파트1 부대 대장의 군단 표시 — 군단 자료(For.dat)는 없지만 0 이 아니어야 파트2 의 따라 걷기(<see cref="SyncFollowers"/>)가 돈다.</summary>
    internal const int Part1SquadLegion = -1;

    /// <summary>
    /// 파트1 부대를 파트2 군단 틀로 잇는다(사용자 요청: 부대는 대장을 따라 함께 움직이고, 대장이 죽으면 새 대장을 뽑아 계속 부대로) —
    /// Btl 인물 칸 9(대장 차례)가 다른 사람을 가리키면 그 사람의 부하(<see cref="UnitState.LeaderIndex"/>, 진형 순번)로 두고, 대장에게 군단 표시를 단다.
    /// 부하는 파트2 처럼 제 차례가 없고 대장 차례에 따라 걷고 함께 친다. 대장이 쓰러지면 첫 부하가 대장이 된다(<see cref="PromoteFollower"/>).
    /// </summary>
    private static void LinkPart1Squads(List<UnitState> list)
    {
        var byRecord = new Dictionary<int, int>();
        for (int i = 0; i < list.Count; i++)
            if (list[i].Record >= 0) byRecord.TryAdd(list[i].Record, i);
        var slots = new Dictionary<int, int>();
        for (int i = 0; i < list.Count; i++)
        {
            var u = list[i];
            if (u.Part1Leader < 0 || u.Part1Leader == u.Record || !byRecord.TryGetValue(u.Part1Leader, out int leader) || leader == i) continue;
            var boss = list[leader];
            // 편이 다르면 잇지 않는다 — 다만 <b>아군 대장(편 4) + 동맹 용병(편 3)</b>은 잇는다: 원본 LoadBtl(0x1002d41b~0x1002d475)은 편을 안 보고
            // 0x10036790(대장, 이, 1)로 잇는다. Btl 0138 레코드 18~21(용병 580·587, 편 3)이 레코드 1(편 4)을 대장으로 적는데, 전에는 편이 달라
            // 안 이어져 용병마다 제 차례가 돌았다(사용자 보고). 부하(+0x1bf)는 차례 고르기(0x10031dc7)에서 빠진다. 대장이 또 누구의 부하여도 안 잇는다.
            bool allies = boss.Side is 3 or 4 && u.Side is 3 or 4;
            if ((boss.Side != u.Side && !allies) || boss.Part1Leader != boss.Record) continue;
            u.LeaderIndex = leader;
            u.FormationSlot = slots[leader] = slots.GetValueOrDefault(leader, -1) + 1;
            boss.LegionId = Part1SquadLegion;
        }
    }

    /// <summary>
    /// 군단 번호가 붙은 대장 레코드의 부하들을 진형대로 만든다(<c>LoadBtl 0x100635b9~0x10063739</c>) — 판에는 아직 안 넣는다.
    /// 군단 0·1(For[1] 「default」)은 부하가 없다.
    /// </summary>
    internal List<UnitState> SpawnFollowers(DemoUnit record, int leaderIndex, Dictionary<int, LegionData> legions, IEnumerable<UnitState> occupied)
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
    internal (int Col, int Row) FormationSpot(int leaderCol, int leaderRow, Facing facing, (int Dx, int Dy) cell, HashSet<(int, int)> taken)
    {
        var (dx, dy) = RotateFormation(cell, facing);
        int col = Math.Clamp(leaderCol + dx, 0, host.Cols - 1), row = Math.Clamp(leaderRow + dy, 0, host.Rows - 1);
        for (int r = 1; r <= 3 && taken.Contains((col, row)); r++)
            for (int ny = -r; ny <= r && taken.Contains((col, row)); ny++)
                for (int nx = -r; nx <= r && taken.Contains((col, row)); nx++)
                {
                    int cx = Math.Clamp(leaderCol + nx, 0, host.Cols - 1), cy = Math.Clamp(leaderRow + ny, 0, host.Rows - 1);
                    if (!taken.Contains((cx, cy))) (col, row) = (cx, cy);
                }
        return (col, row);
    }

    /// <summary>
    /// 배치 단계에서 대장을 세우거나 뺐을 때 — 부하를 새 자리 둘레에 진형대로 다시 세운다(원본도 배치 단계에서 부하를 다시 만든다, 0x100673dd).
    /// 대장이 맵 밖이면 부하도 맵 밖(0,0)으로.
    /// </summary>
    internal void PlaceLegionAround(UnitState leader)
    {
        int li = Array.IndexOf(host._units, leader);
        if (li < 0 || host.Mos.Legions().GetValueOrDefault(leader.LegionId) is not { } legion) return;
        var cells = LegionData.FormationCells[Math.Clamp((int)legion.Formation, 0, 5)];
        var taken = new HashSet<(int, int)>(host._units.Where(u => u.Alive && u.OnField && u.LeaderIndex != li).Select(u => (u.Col, u.Row)));
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
    internal void DropUnits(Func<UnitState, bool> drop)
    {
        var old = host._units;
        bool Gone(UnitState u) => drop(u) || (u.LeaderIndex >= 0 && u.LeaderIndex < old.Length && drop(old[u.LeaderIndex]));
        var leaders = old.Select(u => u.LeaderIndex >= 0 && u.LeaderIndex < old.Length ? old[u.LeaderIndex] : null).ToArray();
        var kept = old.Where((u, i) => !Gone(u)).ToArray();
        foreach (var u in kept)
            if (leaders[Array.IndexOf(old, u)] is { } leader) u.LeaderIndex = Array.IndexOf(kept, leader);
        host._units = kept;
        _formationAt.Clear();
    }

    /// <summary>
    /// 진형 칸은 방향 0(위) 기준이다 — 대장이 보는 쪽으로 돌린다(<c>LoadBtl 0x100634cd</c>).
    /// </summary>
    /// <remarks>진형 여섯이 모두 좌우 대칭이라 아래(방향 2)를 <c>(dx, -dy)</c> 로 두어도 칸 집합은 같고 슬롯 짝만 바뀐다 — 원본대로 맞춘다.</remarks>
    internal static (int Dx, int Dy) RotateFormation((int Dx, int Dy) cell, Facing facing) => facing switch
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
    internal static readonly (int Dx, int Dy)[] FormationRing =
        [(0, -2), (-1, -1), (1, -1), (-2, 0), (2, 0), (-1, 1), (1, 1), (0, 2)];

    /// <summary>지금 기술을 쓰는 중인 대장(배열 번호) — 이동 + 기술이면 진형을 다시 세우지 않는다. 없으면 −1.</summary>
    internal int _skillLeader = -1;

    /// <summary>진형을 마지막으로 셈한 대장 자리 — 같은 칸이면 다시 셈하지 않는다.</summary>
    internal readonly Dictionary<int, (int Col, int Row)> _formationAt = [];

    /// <summary>부하가 마지막으로 받은 진형 목표 칸 — 대장이 그 뒤로 안 움직여도 이 칸을 계속 따라잡으려 한다.</summary>
    internal readonly Dictionary<UnitState, (int Col, int Row)> _followerTarget = [];

    /// <summary>부하마다 다음에 따라잡기를 다시 시도할 시각 — 매 프레임 길찾기를 돌리지 않게 늦춘다.</summary>
    internal readonly Dictionary<UnitState, double> _nextFollowerRetry = [];

    /// <summary>그 인물의 부하들(살아 있는 것만).</summary>
    internal List<UnitState> FollowersOf(int leaderIndex) =>
        [.. host._units.Where(u => u.Alive && u.LeaderIndex == leaderIndex)];

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
    internal List<(UnitState Follower, int Col, int Row)> FormationPlan(UnitState leader, int destCol, int destRow)
    {
        var followers = FollowersOf(Array.IndexOf(host._units, leader));
        var plan = new List<(UnitState, int, int)>();
        if (followers.Count == 0) return plan;

        followers.Sort((a, b) => (Math.Abs(a.Col - destCol) + Math.Abs(a.Row - destRow))
                               - (Math.Abs(b.Col - destCol) + Math.Abs(b.Row - destRow)));
        var usedRing = new bool[FormationRing.Length];
        var taken = new HashSet<(int, int)>();

        foreach (var follower in followers)
        {
            // 바라는 칸 = 대장을 따라 그대로 평행 이동한 자리.
            int wantCol = Math.Clamp(destCol + follower.Col - leader.Col, 0, host.Cols - 1);
            int wantRow = Math.Clamp(destRow + follower.Row - leader.Row, 0, host.Rows - 1);

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
            int ringCol = Math.Clamp(destCol + FormationRing[best].Dx, 0, host.Cols - 1);
            int ringRow = Math.Clamp(destRow + FormationRing[best].Dy, 0, host.Rows - 1);

            var (col, row) = StandingCellFor(follower, destCol, destRow, ringCol, ringRow, taken);
            taken.Add((col, row));
            plan.Add((follower, col, row));
        }
        return plan;
    }

    /// <summary>
    /// 증원(행동 200 · 214)으로 들어오는 군단의 부하 자리 — 대장이 설 칸 둘레의 8칸 고리에서 정한다(fg-75).
    /// </summary>
    /// <remarks>
    /// 원본(<c>0x100510dc~0x10051240</c>, ba-21 B-1)은 부하 i 를 대장 줄의 옆(짝수 +1 · 홀수 −1)에 두고 대장보다 <c>1 + i/2</c> 칸 뒤(변 쪽)에서
    /// 같이 곧게 걸어 들어오게 한 뒤 부대 이동(<c>0x1005ad40</c> · <c>0x1005f670</c>)으로 대장 목표 칸 둘레 진형 자리에 세운다.
    /// 여기서는 그 들어오는 대열을 「바라는 자리」로 삼아 <see cref="FormationPlan"/> 과 같은 고리 · 점수로 설 칸을 고른다 — 다만 판 밖 (0,0) 에 있는
    /// 부하의 지금 칸이 아니라 <b>대장이 설 칸</b> 둘레를 훑는다. <paramref name="claimed"/>(이 행동이 이미 준 칸)와 대장 칸은 비켜 선다.
    /// </remarks>
    internal List<(UnitState Follower, int Col, int Row)> ReinforcementPlan(UnitState leader, int landCol, int landRow, int edge, IReadOnlySet<(int, int)> claimed)
    {
        var plan = new List<(UnitState, int, int)>();
        var followers = FollowersOf(Array.IndexOf(host._units, leader)).OrderBy(f => f.FormationSlot).ToList();
        if (followers.Count == 0) return plan;
        var usedRing = new bool[FormationRing.Length];
        var taken = new HashSet<(int, int)>(claimed) { (landCol, landRow) };
        for (int i = 0; i < followers.Count; i++)
        {
            int side = i % 2 == 0 ? 1 : -1, back = 1 + i / 2;
            var (dx, dy) = (edge & 3) switch { 0 => (side, -back), 1 => (-back, side), 2 => (side, back), _ => (back, side) };
            int wantCol = landCol + dx, wantRow = landRow + dy;
            int best = -1, bestScore = int.MaxValue;
            for (int k = 0; k < FormationRing.Length; k++)
            {
                if (usedRing[k]) continue;
                int score = Math.Abs(wantCol - (landCol + FormationRing[k].Dx)) + Math.Abs(wantRow - (landRow + FormationRing[k].Dy));
                if (score < bestScore) (best, bestScore) = (k, score);
            }
            if (best < 0) best = 0;   // 부하는 많아야 여섯, 고리는 여덟 — 안 일어난다
            usedRing[best] = true;
            int ringCol = Math.Clamp(landCol + FormationRing[best].Dx, 0, host.Cols - 1);
            int ringRow = Math.Clamp(landRow + FormationRing[best].Dy, 0, host.Rows - 1);
            var (col, row) = StandingCellFor(followers[i], landCol, landRow, ringCol, ringRow, taken, around: (landCol, landRow));
            if (taken.Contains((col, row)))
                (col, row) = FreeCellNear(landCol, landRow, followers[i], taken);   // 둘레 ±20 칸에 설 데가 하나도 없을 때만
            taken.Add((col, row));
            plan.Add((followers[i], col, row));
        }
        return plan;
    }

    /// <summary>고리 칸을 목표로 부하가 실제로 설 칸 — 못 찾으면 고리 칸 그대로(원본도 그렇다).</summary>
    /// <param name="around">훑을 가운데 — 없으면 부하의 지금 칸(원본). 증원은 판 밖 부하라 대장이 설 칸을 준다.</param>
    internal (int Col, int Row) StandingCellFor(UnitState follower, int destCol, int destRow,
                                               int ringCol, int ringRow, HashSet<(int, int)> taken, (int Col, int Row)? around = null)
    {
        int destHeight = host._map?.HeightAt(destCol, destRow) ?? 0;
        int best = int.MaxValue;
        (int Col, int Row) pick = (ringCol, ringRow);
        var (centerCol, centerRow) = around ?? (follower.Col, follower.Row);
        for (int row = centerRow - 20; row <= centerRow + 20; row++)
            for (int col = centerCol - 20; col <= centerCol + 20; col++)
            {
                if (col == destCol && row == destRow) continue;          // 대장 자리는 비켜 준다
                if (taken.Contains((col, row)) || !CanStand(col, row, follower)) continue;
                int height = Math.Abs((host._map?.HeightAt(col, row) ?? 0) - destHeight);
                int score = 11 * Math.Abs(height + Math.Abs(col - destCol) + Math.Abs(row - destRow) - 2)
                          + 10 * (Math.Abs(col - ringCol) + Math.Abs(row - ringRow));
                if (score < best) (best, pick) = (score, (col, row));
            }
        return pick;
    }

    internal bool CanStand(int col, int row, UnitState who)
    {
        if ((uint)col >= host.Cols || (uint)row >= host.Rows || !Part1Range.Contains(col, row)) return false;   // 파트1 전장 범위(행동 103) 밖도 못 선다
        if (host._map is { } map && (col >= map.Cols || row >= map.Rows || (CellFlagsAt(col, row) & 0x9) != 0)) return false;   // 물체를 찍은 칸 기준(ba-16)
        return LiveUnitAt(col, row) is not { } other || other == who;
    }

    /// <summary>
    /// 대장이 움직인 뒤 — 부하들의 새 진형 목표 칸을 정한다(실제로 걷는 건 <see cref="TryAdvanceFollower"/>).
    /// </summary>
    internal void AssignFormationTargets(int leaderIndex)
    {
        var leader = host._units[leaderIndex];
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
    internal void TryAdvanceFollower(UnitState follower)
    {
        if (!_followerTarget.TryGetValue(follower, out var target) || (follower.Col, follower.Row) == target) return;
        if (ComputeRange(follower) is not { } range) return;
        int goal = target.Row * host.Cols + target.Col;
        if (!range.CanReach(goal))
        {
            // 한 번에 못 닿으면 닿을 수 있는 칸 가운데 목표에 가장 가까운 빈 칸까지라도 간다 — 전에는 아예 안 움직여,
            // 이동력이 작은 부하(Btl 0142 글로리가드 chr 274)가 대장이 떠난 첫 자리에 영영 서 있었다(사용자 보고).
            int best = -1, bestDist = Math.Abs(follower.Col - target.Col) + Math.Abs(follower.Row - target.Row);
            for (int i = 0; i < range.Cost.Length; i++)
            {
                if (!range.CanReach(i)) continue;
                int c = i % host.Cols, r = i / host.Cols;
                if (!CanStand(c, r, follower)) continue;
                int dist = Math.Abs(c - target.Col) + Math.Abs(r - target.Row);
                if (dist < bestDist) (best, bestDist) = (i, dist);
            }
            if (best < 0) return;
            goal = best;
        }
        foreach (var step in range.PathTo(goal)) follower.Path.Enqueue(step);
        PayFollowerWalk(follower, range, goal);
        host.PlayWalkSound(follower);
    }

    /// <summary>
    /// 부하 걸음 값 — 경로 이동(0x2710/0x2711)도 끝처리 <c>0x10076380</c> 에서 출발 칸→도착 칸 길 비용(<c>0x10075f80</c>)을
    /// 부하 자신의 TP 에서 뺀다(<c>0x100763be~0x10076430</c> → <c>0x10071e20</c>). 부하는 TP 가 0 이하가 되어도 차례 끝 처리를 안 한다(<c>0x10071e3d</c>).
    /// 전에는 부하가 걸어도 TP 가 그대로라 늘 최대 예산으로 걸었다(감사3 L1). 걷기 시작할 때 한 번에 뺀다 — 값은 같다.
    /// </summary>
    internal void PayFollowerWalk(UnitState follower, MoveRange range, int goal)
    {
        if (!range.CanReach(goal)) return;
        follower.Tp -= range.Cost[goal];
        follower.OriginCol = goal % host.Cols;
        follower.OriginRow = goal / host.Cols;
    }

    /// <summary>대장이 방금 확정한 자리 기준으로 부하 목표를 다시 잡고, 바로 한 걸음씩 걷게 한다.</summary>
    internal void ReformFollowers(int leaderIndex)
    {
        if (host._scene.IsPart1) { Part1FollowLeader(leaderIndex); return; }
        AssignFormationTargets(leaderIndex);
        foreach (var follower in FollowersOf(leaderIndex)) TryAdvanceFollower(follower);
    }

    /// <summary>
    /// 파트1 부대 — 대장이 걸은 뒤 부하들이 <b>대장 칸에 가장 가까이 닿는 칸</b>으로 걷는다(원본 <c>0x10036a40(대장, X, Y)</c>,
    /// 대장 이동 명령 0x3e9 끝 <c>0x1003a29f~0x1003a2ca</c> 에서 부른다). 진형표 · 파트2 의 8칸 고리는 안 쓴다.
    /// </summary>
    /// <remarks>
    /// 원본 차례: 대장을 도착 칸에 잠시 세우고, 부하마다 「지금 칸의 거리」를 최선값으로 둔다. 거리 = |높이 차| / 2 + |dx| + |dy|(대장 도착 칸 기준).
    /// 부하 사슬(<c>+0x1d8 → +0x1e4</c>) 차례로, 제 이동력(<c>0x1003ece0</c> = SPD(+8 스피디))으로 닿는 칸(<c>0x10025960</c>) 가운데
    /// 설 수 있고(<c>0x10089bf0</c>) 비었거나 제 칸인(<c>0x10089610</c>) 칸을 줄 차례(행 → 열)로 훑어, 거리가 0 이 아니고 최선값보다 <b>작은</b> 첫 칸을 고른다.
    /// 고른 칸에 미리 세워(<c>0x10037080</c>) 뒤 부하가 같은 칸을 못 고르게 하고 0x3e9 로 걷게 한다. 안 움직인 부하가 있으면 한 번 더 돈다(모두 두 바퀴까지,
    /// 최선값은 이어진다 — 앞 부하가 비운 칸을 뒤 바퀴에서 얻는다). 부하 걸음은 TP 를 안 쓴다 — 파트1 부하는 대장 차례에 이동 1 · 행동 1(<c>0x10036825</c>).
    /// </remarks>
    private void Part1FollowLeader(int leaderIndex)
    {
        var leader = host._units[leaderIndex];
        var followers = FollowersOf(leaderIndex).Where(f => f.OnField && !f.IsBusy).OrderBy(f => f.FormationSlot).ToList();
        if (followers.Count == 0) return;
        int lh = WalkHeightAt(leader.Col, leader.Row);
        int Dist(int c, int r) => Math.Abs(WalkHeightAt(c, r) - lh) / 2 + Math.Abs(c - leader.Col) + Math.Abs(r - leader.Row);
        // 칸 주인 — 고른 칸으로 미리 옮겨 둔 부하 자리(원본은 고른 칸에 세워 둔다).
        var at = followers.ToDictionary(f => f, f => f.Row * host.Cols + f.Col);
        var best = followers.ToDictionary(f => f, f => Dist(f.Col, f.Row));
        var ranges = new Dictionary<UnitState, MoveRange?>();
        var moved = new HashSet<UnitState>();
        bool Taken(int idx, UnitState who)
        {
            foreach (var (f, cell) in at) if (cell == idx) return f != who;
            return LiveUnitAt(idx % host.Cols, idx / host.Cols) is { } other && other != who && !at.ContainsKey(other);
        }
        for (int round = 0; round < 2 && moved.Count < followers.Count; round++)
            foreach (var f in followers)
            {
                // 이동력 전부 — 부하 TP 는 걸음 예산이 아니다(파트1 TP 칸은 차례 간격일 뿐).
                if (!ranges.TryGetValue(f, out var range)) ranges[f] = range = ComputeRange(f, tp: Math.Max(1, f.MaxTp));
                if (range is null) continue;
                int pick = -1;
                for (int idx = 0; idx < range.Cost.Length; idx++)
                {
                    if (!range.CanReach(idx)) continue;
                    int c = idx % host.Cols, r = idx / host.Cols;
                    if (!CanStand(c, r, f) || Taken(idx, f)) continue;
                    int d = Dist(c, r);
                    if (d != 0 && d < best[f]) { best[f] = d; pick = idx; }
                }
                if (pick < 0) continue;
                at[f] = pick;
                if (pick != f.Row * host.Cols + f.Col) moved.Add(f);
            }
        foreach (var f in followers)
        {
            int goal = at[f];
            if (goal == f.Row * host.Cols + f.Col || ranges.GetValueOrDefault(f) is not { } range) continue;
            if (Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"p1 squad follow: chr {f.ChrCode} ({f.Col},{f.Row}) → ({goal % host.Cols},{goal / host.Cols}) leader {leader.ChrCode}@({leader.Col},{leader.Row}) dist {best[f]}" + Environment.NewLine);
            foreach (var step in range.PathTo(goal)) f.Path.Enqueue(step);
            host.PlayWalkSound(f);
        }
    }

    /// <summary>
    /// 프레임마다 — 걸음을 멈춘 대장의 부하들이 아직 제자리가 아니면 걸어가게 한다.
    /// </summary>
    /// <remarks>
    /// 원본은 <b>대장이 「이동만·이동+휴식」 명령을 냈을 때만</b> 진형을 다시 세운다(이동 + 기술이면 아예 안 짓는다).
    /// 데모는 명령 상자가 없어 「대장도 부하도 다 멈췄을 때」로 대신한다 — 결과는 같고 한 박자 늦다.
    /// </remarks>
    internal void SyncFollowers()
    {
        for (int i = 0; i < host._units.Length; i++)
        {
            var leader = host._units[i];
            if (!leader.Alive || leader.LeaderIndex >= 0 || leader.LegionId == 0 || leader.IsBusy) continue;
            if (i == _skillLeader) continue;          // 이동 + 기술 중인 대장 — 진형을 안 짓는다(UseWorkRoutine)
            // 아직 전장 밖(증원 전)인 대장의 부하는 전장 밖 (0,0) 에 그대로 둔다 — 진형을 짜면 (0,0) 둘레 칸으로 걸어 나와
            // 맵 귀퉁이에 서 있었다(Btl 0142 끝 장면의 글로리가드 셋).
            if (!leader.OnField) continue;
            if (FollowersOf(i).Any(f => f.IsBusy)) continue;   // 아직 걷는 중이면 새 목표를 주지 않는다
            // 대장이 같은 칸에 그대로 서 있으면 진형을 다시 셈하지 않는다 — 칸을 넓게 훑어 값이 비싸다.
            bool seen = _formationAt.TryGetValue(i, out var last);
            if (seen && last == (leader.Col, leader.Row)) continue;
            _formationAt[i] = (leader.Col, leader.Row);
            // 처음 본 대장(전투 시작 · 불러온 직후 · 증원)은 자리만 적어 둔다 — 원본은 대장이 걸었을 때만 진형을 다시 세운다.
            // 전에는 여기서 바로 다시 세워, 시작하자마자 온 군단이 배치 대열을 깨고 한 칸씩 움직였다(피드백 fg-31 a).
            if (!seen) continue;
            ReformFollowers(i);
        }

        // 방금 목표를 받았든, 예전에 예산이 모자라 못 따라갔든 — 목표 자리에 아직 못 간 부하는 계속 다시 시도한다.
        // 이게 없으면 대장이 한 번 크게 움직여 부하가 한 걸음에 못 따라잡은 뒤 대장이 그 자리에 눌러앉을 경우,
        // 그 부하는 대장이 다시 움직일 때까지(=영영) 원래 자리에 남아 플레이어가 닿을 수 없는 낙오자가 된다.
        foreach (var follower in host._units)
        {
            if (!follower.Alive || follower.IsBusy || follower.LeaderIndex < 0 || !follower.OnField) continue;
            // 기술을 쓰는 대장의 부하는 다시 걷기 시작하지 않는다 — 걷기 시작하면 FollowersAttack 이 그 부하를 못 쓴다(감사5 L-A).
            if (follower.LeaderIndex == _skillLeader) continue;
            if (!_followerTarget.TryGetValue(follower, out var target) || (follower.Col, follower.Row) == target) continue;
            // 대장 곁(거리 4 안)에 이미 와 있으면 낙오가 아니다 — 목표를 버린다. 전에는 끝까지 그 칸을 노려서, 옆에서 누가 비켜
            // 칸이 날 때마다 부하들이 저절로 한 칸씩 움직였다(피드백 fg-31 b·c).
            if ((uint)follower.LeaderIndex < host._units.Length && host._units[follower.LeaderIndex] is var boss
                && Math.Abs(follower.Col - boss.Col) + Math.Abs(follower.Row - boss.Row) <= 4)
            {
                _followerTarget.Remove(follower);
                continue;
            }
            if (_nextFollowerRetry.TryGetValue(follower, out var next) && host._lastTime < next) continue;
            _nextFollowerRetry[follower] = host._lastTime + 0.5;
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
    internal readonly List<(UnitState Follower, WorkData Work, int Col, int Row)> _followerStrikes = [];

    /// <summary>제 칸 하나만 갈 수 있는 이동 범위 — 자리를 옮기지 않는 겨냥(아군 패스·제자리 기술 합류)에 쓴다.</summary>
    internal MoveRange StandOnly(UnitState u)
    {
        int n = host.Cols * host.Rows;
        var cost = new int[n];
        var prev = new int[n];
        Array.Fill(cost, int.MaxValue);
        Array.Fill(prev, -1);
        cost[u.Row * host.Cols + u.Col] = 0;
        return new MoveRange(cost, prev, new bool[n]) { Width = host.Cols };
    }

    /// <summary>
    /// 부하가 (col, row) 를 겨눠 그 기술을 쓴다 — 정상 work 실행(0x10075ff0): 광역기면 범위 안 전원이 맞고, 제 TP·SOUL·HP 를 낸다(0x10076380).
    /// 맞을 대상이 없어졌으면(걸어오는 사이 죽었거나 옮겨 갔다) 아무것도 안 하고 false.
    /// </summary>
    internal bool FollowerStrike(UnitState follower, WorkData work, int col, int row, List<UnitState> dying)
    {
        if (follower.Data is not { } c) return false;
        var struck = WorkTargets(work, follower, col, row);
        if (struck.Count == 0) return false;
        follower.Facing = FacingToward(follower.Col, follower.Row, col, row);
        // 치는 동작 — 보통은 8(베기), 쏘는 기본공격(work 6 · 1584)은 대장처럼 9 다. 전에는 늘 8 이라 총 든 부하(글로리가드)가 칼 휘두르는 몸짓으로 쐈다.
        int strike = BasicWorkActions.GetValueOrDefault(work.Id)?.LastOrDefault(IsStrikeAction) is > 0 and var basicStrike ? basicStrike : 8;
        PlayAction(follower, DrawnAction(follower, strike));
        // 기본공격이 아니면 이펙트·소리를 같이 띄운다(ba-20 Q5). 동작 사슬은 아직 8 하나.
        if (work.Id != c.BasicWorkId)
        {
            if (work.Prepare is 2 or 3 or 5 or 6) host._pendingSounds.Add((host._lastTime, 694, host.Btl.UnitFoot(follower).X));   // 시전 소리 — 대장은 UseWorkRoutine 이 낸다
            host.ScheduleAbilitySounds(work);
            SpawnAbilityEffects(work, follower, col, row);
        }
        // 판정은 부하 <b>제 동작의 타격 키</b> 때 든다 — 원본은 부하도 명령 2(기술)로 보통 work 실행(0x10075ff0)을 돌려 제 모션의 타격 키마다 판정한다.
        // 전에는 치는 동작을 틀자마자 맞혀, 부하 피해 숫자가 대장 첫 타와 같은 틱 같은 자리에 겹쳐 떴고 정작 부하가 쏠 때는 아무것도 안 떴다
        // (사용자 보고: Btl 0288 세큐리티볼 부하가 베라모드를 쏴도 피해가 안 들어온다 — HP 는 깎이고 있었다).
        // 파트1 부하(용병)는 대장처럼 그 모션의 첫 타격 키에서 친다(Part1Skill.BasicSwing) — 근접이라고 0.05 초에 치면 1.9 초짜리 베기 동작이
        // 휘두르기도 전에 피해 · 맞는 몸짓이 나왔다(카페 제보 fg-42: 「전투 공격 모션과 히트 모션이 안 맞고」).
        var hits = HitTimesFor(follower, DrawnAction(follower, strike), ranged: work.RangeMax > 4 || host._scene.IsPart1);
        // 파트1 쏘는 동작(용병 총잡이 동작 9)은 타격 키가 없다 — 총소리 키 뒤 0.1 초(대장 BasicShot 처럼 쏜 뒤 맞는다), 소리도 없으면 동작 절반.
        if (host._scene.IsPart1 && host._sprites.TryGetValue(follower.ChrCode, out var p1sprite)
            && p1sprite.Clip(DrawnAction(follower, strike), follower.Facing) is { Hits.Count: 0 } p1clip)
            hits = [p1clip.Sounds.Count > 0 ? p1clip.Sounds.Min(k => k.Start) / TicksPerSecond + 0.1 : Math.Max(0.05, p1sprite.ActionSeconds(DrawnAction(follower, strike), follower.Facing) / 2)];
        foreach (double at in hits) _followerHits.Add((host._lastTime + at, follower, work, struck, dying));
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"{host._lastTime:F2} follower strike {follower.ChrCode} work {work.Id} → [{string.Join(",", struck.Select(ti => host._units[ti].ChrCode))}] hits at +{string.Join("/", hits.Select(t => t.ToString("F2")))}" + Environment.NewLine);
        PayWorkCost(follower, work);
        return true;
    }

    /// <summary>들 때를 기다리는 부하 타격 — (때, 부하, work, 대상 색인, 쓰러짐 목록).</summary>
    internal readonly List<(double At, UnitState Follower, WorkData Work, List<int> Targets, List<UnitState> Dying)> _followerHits = [];

    /// <summary>때가 된 부하 타격을 든다. <paramref name="flush"/> 면 남은 것을 모두 바로 든다(행동 루틴이 끝날 때).</summary>
    internal void PumpFollowerHits(bool flush = false)
    {
        for (int i = 0; i < _followerHits.Count;)
        {
            var h = _followerHits[i];
            if (!flush && host._lastTime < h.At) { i++; continue; }
            _followerHits.RemoveAt(i);
            if (!h.Follower.Alive || h.Follower.Hp <= 0 || h.Dying.Contains(h.Follower)) continue;   // 치기 전에 쓰러진 부하
            foreach (int ti in h.Targets)
                if (host._units[ti] is var t && t.Alive && !h.Dying.Contains(t)) ApplyWork(h.Follower, h.Work, t, h.Dying);
        }
    }

    /// <summary>
    /// 대장이 기술을 쓰면 부하들도 같은 패스로 제 기술을 쓴다(상태 15, 분석-군단 「부하가 대장 차례에 하는 일」).
    /// <paramref name="allyPass"/> 면 아군 패스(<c>0x1005fa90</c>) — 대장이 아군 대상 기술(방식 4)을 쓸 때로, 부하는 회복·보조 기술을
    /// 대장이 겨눈 아군 가까이에 쓴다. 아니면 적 패스(<c>0x1005fd00</c>) — 피해 기술과 기본공격으로 적을 친다.
    /// </summary>
    /// <param name="walk">대장 대상 쪽으로 걸어가 치기를 해도 되나 — 겨눈 대상이 없는 제자리 기술(방식 2)이면 <paramref name="target"/> 은 대장 자신이라 걷지 않는다.</param>
    internal void FollowersAttack(int leaderIndex, UnitState target, List<UnitState> dying, bool allyPass = false, WorkData? leaderWork = null, bool walk = true)
    {
        if (host._db is null) return;
        // 대장이 쓴 기술의 +0x41 이 0 이면 부하는 안 따라 친다(0x1005fd00) — 피해 work 469개가 그렇다(fg-21 ⑯).
        if (leaderWork is { FollowersAct: false }) return;
        // 잡힌 칸 — 이번에 부하가 고른 설 칸과, 지금 걷고 있는 유닛(다른 군단 부하 포함)의 도착 칸. 아직 출발 칸에 서 있어 LiveUnitAt 에 안 잡힌다.
        var approached = new HashSet<int>();
        foreach (var walker in host._units)
            if (walker.Alive && walker.Path.Count > 0) { var (lc, lr) = walker.Path.Last(); approached.Add(lr * host.Cols + lc); }
        foreach (var follower in FollowersOf(leaderIndex))
        {
            // 걷는 부하는 UseWorkRoutine 첫머리(WaitFollowersStopped)가 이미 세웠다 — 여기서 바쁜 건 동작 중인 부하뿐.
            if (follower.Data is not { } c || follower.IsBusy || !follower.OnField || follower.Hp <= 0) continue;   // 판 밖·쓰러질 부하는 뺀다
            // 부하 겨냥(0x1005e4d0, ba-20 Q2 = N11): 갈 수 있는 칸(층 2) + 그 칸들의 사거리(층 12)를 <b>한 번에</b> 채점한다 —
            // 점수 = 값 × Num74 × 10 / (Num74 + |칸 − 대장이 겨눈 칸|), 값은 0x1005c510(최소 대상·종류 3 이득·회복 문턱, WorthUsing).
            // 전에는 제자리 사거리 안의 유닛 칸만 먼저 보고, 없을 때만 대장 대상이 닿는 가장 싼 칸으로 걸어가 대장 대상을 쳤다.
            // 아군 패스(0x1005e820 → 0x10075420)와 걷지 않는 호출은 설 칸이 제 칸 하나다.
            int fi = Array.IndexOf(host._units, follower), here = follower.Row * host.Cols + follower.Col;
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
                host.PlayWalkSound(follower);
                _followerTarget[follower] = (use.Stand % host.Cols, use.Stand / host.Cols);
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
            for (int idx = 0; idx < host.Cols * host.Rows; idx++)
            {
                if (!near.CanReach(idx) || toAim[idx] == int.MaxValue || approached.Contains(idx)) continue;
                if (LiveUnitAt(idx % host.Cols, idx / host.Cols) is { } other && other != follower) continue;
                if (toAim[idx] < goCost || toAim[idx] == goCost && near.Cost[idx] < goOwn) (go, goCost, goOwn) = (idx, toAim[idx], near.Cost[idx]);
            }
            if (go < 0 || near.Cost[go] <= 0) continue;
            approached.Add(go);                    // 뒤 부하가 같은 칸을 고르지 않게(0x1005ff9a)
            foreach (var step in near.PathTo(go)) follower.Path.Enqueue(step);
            PayFollowerWalk(follower, near, go);
            host.PlayWalkSound(follower);
            _followerTarget[follower] = (go % host.Cols, go / host.Cols);
        }
    }

    /// <summary>
    /// 부하가 고를 수 있는 기술 — AI 목록 <c>0x10032370</c>(분류 1·2·4, 어빌리티 번호 ≥ 200·레벨 0 은 건너뜀, 그 차례가 우선순위) 다음에 기본공격.
    /// 기술은 <b>효과 대상 <c>+0x1e</c></b> 로 가른다 — 적 패스는 1(<c>0x1005f3b1</c>), 아군 패스는 4. 종류(피해·보조)는 안 본다.
    /// 전에는 분류 검사 없이 적 패스 = 피해 기술, 아군 패스 = 방식 4 비피해 기술로 골랐다(원본차이-AI 18). 아군 패스엔 기본공격이 없다.
    /// </summary>
    internal IEnumerable<WorkData> FollowerWorks(CharacterData c, bool allyPass = false)
    {
        if (host._db is not { } db) yield break;
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
    internal void DetachFollower(UnitState u)
    {
        if (u.LeaderIndex < 0) return;
        u.LeaderIndex = -1;
        u.FormationSlot = -1;
        u.Detached = true;
        if (u.Side == 4) { u.Side = 3; u.Awake = true; }
        host.StatusScr.RefreshUnitStats(u);
    }

    /// <summary>대장이 쓰러지면 — 첫 부하가 새 대장이 되고 세력이 0.6배가 된다.</summary>
    internal void PromoteFollower(int leaderIndex)
    {
        var followers = FollowersOf(leaderIndex);
        if (followers.Count == 0) return;
        // 파트1 아군 부대(대장 편 4 — 용병을 거느린 주인공)는 대장이 쓰러지면 <b>부대가 풀린다</b>: 원본 죽음 처리(0x100345f2~0x10034646)가
        // 부하마다 대장 연결(+0x1e0 · +0x1bf)을 끊고 편 4 면 편 3 으로 바꾼다 — 새 대장을 뽑지 않고, 저마다 제 차례(동맹 AI)를 받는다.
        // 적 부대는 사용자 요청대로 새 대장을 뽑는다(아래).
        if (host._scene.IsPart1 && host._units[leaderIndex].Side == 4)
        {
            foreach (var f in followers)
            {
                f.LeaderIndex = -1;
                f.FormationSlot = -1;
                _followerTarget.Remove(f);
                if (f.Side == 4) f.Side = 3;
                f.Awake = true;
                host.StatusScr.RefreshUnitStats(f);
            }
            host._units[leaderIndex].LegionId = 0;
            return;
        }
        // 플레이어 군단의 대장이 죽으면 떨어져 나온 부하들은 <b>편 3(동맹 AI)</b>이 된다(0x100716c0) — 더는 명령할 수 없다.
        if (host._units[leaderIndex].Side == 4)
            foreach (var f in followers) { f.Side = 3; f.Awake = true; }
        var newLeader = followers[0];
        int newIndex = Array.IndexOf(host._units, newLeader);
        newLeader.LeaderIndex = -1;
        newLeader.FormationSlot = -1;
        newLeader.LegionId = host._units[leaderIndex].LegionId;
        newLeader.LegionPowerPercent = host._units[leaderIndex].LegionPowerPercent * 6 / 10;
        // 다시 붙는 부하(0x10072ef0(새대장, 부하, 0))는 TP 를 새 대장 것으로 덮는다(감사5 L-C). AI 꼬리는 처음부터 대장 레코드 복사라 같다.
        foreach (var follower in followers.Skip(1)) { follower.LeaderIndex = newIndex; follower.Tp = newLeader.Tp; }
        host.StatusScr.RefreshUnitStats(newLeader);
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"squad leader {host._units[leaderIndex].ChrCode} (unit {leaderIndex}) fell — new leader unit {newIndex} chr {newLeader.ChrCode} at ({newLeader.Col},{newLeader.Row}), followers {followers.Count - 1}, hasTurn {newLeader.HasTurn} TP {newLeader.Tp}/{newLeader.MaxTp}" + Environment.NewLine);
    }

    /// <summary>
    /// 기술 앞 — 그 대장의 부하가 아직 걷고 있으면 다 설 때까지 기다린다(원본은 이동 명령 뒤 상태 15 갈래 1 이 <c>0x1006e320</c> 로 모두 설 때까지
    /// 다음 명령을 안 받는다). 전에는 <see cref="FollowersAttack"/> 가 걷는 부하를 건너뛰어 대장이 걸은 직후 친 공격에 부하가 빠졌다(감사5 L-A). 5초 상한.
    /// </summary>
    internal IEnumerable<bool> WaitFollowersStopped(int leaderIndex)
    {
        for (double end = host._lastTime + 5; host._lastTime < end && FollowersOf(leaderIndex).Any(f => f.OnField && f.IsBusy);) yield return true;
    }

    /// <summary>
    /// 능력치 주인 — 부하면 대장, 아니면 자신. 원본 DEX(<c>0x1007ae50</c>)·최대 TP(<c>0x1007aeb0</c>)·STP 제수(<c>0x1007afa0</c>) 게터는
    /// 부하(<c>+0x4f0</c>)면 <c>+0x508</c> 사슬을 따라 <b>대장 유닛에서</b> 값을 읽는다(감사3 L2).
    /// </summary>
    internal UnitState StatOwner(UnitState u) =>
        u.LeaderIndex >= 0 && u.LeaderIndex < host._units.Length ? host._units[u.LeaderIndex] : u;

    /// <summary>
    /// 판정·이동 비용에 쓰는 능력치 — <see cref="EffectiveData"/> 에 부하면 DEX 만 <b>대장의</b> DEX(대장 CChr DEX + 장비 + 상태 30 − 상태 1)로 바꾼다
    /// (<c>0x1007ae50</c>). 부하 자신의 장비 DEX 는 <see cref="GameDatabase.Dex"/> 가 다시 더하므로 미리 뺀다.
    /// </summary>
    internal CharacterData? CombatData(UnitState u)
    {
        var own = EffectiveData(u);
        if (own is null || host._db is not { } db) return own;
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
    internal void RestFollowers(int leaderIndex)
    {
        if (host._db is not { } db || host._units[leaderIndex].LeaderIndex >= 0) return;
        // 파트1 — 대장이 쉬면 부하마다 휴식 0x3f3(0x10037040 → 0x1003e580): 대장 차례 동안 안 움직였고 안 했으면 잃은 HP 의 1/5, 아니면 1/10.
        // TP 는 안 건드린다 — 파트2 식으로 0 으로 깎으면 부하 TP 가 다시 안 차 걷기 · 함께 치기를 못 했다(Btl 20089 사용자 보고).
        if (host._scene.IsPart1)
        {
            foreach (var f in FollowersOf(leaderIndex))
            {
                bool stayed = f.Col == f.OriginCol && f.Row == f.OriginRow && f.Tp >= f.MaxTp;
                int gain = (f.MaxHp - f.Hp) / (stayed ? 5 : 10);
                if (gain <= 0 || f.HasStatus(26)) continue;
                int before = f.Hp;
                f.Hp += gain;
                ShowNumber(f, db.T(159), HealColor2, rise: false, count: (before, f.Hp));
            }
            return;
        }
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
                    $"follower rest: chr {f.ChrCode} (leader {host._units[leaderIndex].ChrCode}) TP {f.Tp}/{f.MaxTp} → 0, HP {f.Hp}/{f.MaxHp}" + Environment.NewLine);
            if (f.Tp > 0) f.Tp = 0;   // 빚(음수 TP)은 그대로 남는다(ba-20 K1)
        }
    }

    /// <summary>부하가 대장에게서 받는 능력치 보정 — 세력 × For 보정 / 100(분석-군단 1절).</summary>
    internal (int Lp, int Psy, int Dep) LegionBonusFor(UnitState unit)
    {
        if (unit.LeaderIndex < 0 || (uint)unit.LeaderIndex >= host._units.Length) return (0, 0, 0);
        var leader = host._units[unit.LeaderIndex];
        if (host.Mos.Legions().GetValueOrDefault(leader.LegionId) is not { } legion) return (0, 0, 0);
        int power = leader.LegionPowerPercent;
        return (legion.LpBonus * power / 100, legion.PsyBonus * power / 100, legion.DepBonus * power / 100);
    }
}
