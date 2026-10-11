using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 「캐릭터 배치」 단계(원본 전투 상태 2, <c>0x10066d00</c>) — 배치칸이 있는 전투를 새로 시작하면 전투를 멈추고 파티원을 초록 칸에 세운다(fg-17).
/// </summary>
/// <remarks>
/// 분석-전투 ba-6: 배치칸(Btl C 절)마다 층 13 초록 (20,200,60). 창 명단 = 플레이어 부대 전원, Btl 에 이미 선 부대원은 「Entry」로 고정.
/// 한 명 놓기 = 초록이고 빈 칸일 때만, 방향 = 그 칸 레코드의 방향. 자동배치(상태 1 <c>0x10066480</c>) = 안 놓인 사람 중 명단 첫 사람 →
/// 칸 순서 첫 빈 칸을 되풀이. 배치종료 = 안 놓인 부대원 유닛을 지우고 상태 5 로.
/// 데모는 처음부터 자동배치한 채로 연다(그대로 배치종료만 눌러도 된다).
/// 「군단사용」(단추 0x4b8, 명단 레코드 <c>+0x30</c>) — 고른 사람의 군단을 켜고 끈다. 기본 = 머리 워드 7 켜짐 && 배속 군단 있음,
/// 워드 7 이 꺼진 전투에선 단추가 안 먹는다(<c>0x100e257c</c>). 끈 사람은 부하 없이 서고, 배치종료 때 부하를 지운다.
/// 불러온 판·화면 밖 자동 진행(DUELDX_AUTOPLAY)에는 이 단계가 없다. RESTART 는 원본과 달리 이 단계부터 다시 한다(사용자 요청).
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    /// <summary>배치 단계가 열려 있나 — 열려 있는 동안 전투(틱·차례·이벤트)는 멈춘다.</summary>
    internal bool _deployOpen;

    /// <summary>배치칸(칸, 방향) — 전투 자료 순서.</summary>
    internal IReadOnlyList<(int Col, int Row, Facing Facing)> _deploySpots = [];

    /// <summary>BuildUnits 가 배치칸이 모자라 맵 밖에 만들어 둔 파티원 — 배치 단계를 안 열면 지운다.</summary>
    internal readonly HashSet<UnitState> _deployBench = [];

    /// <summary>세이브를 불러오느라 판을 세우는 중 — 벤치를 지우지 않고 남겨 둔다.</summary>
    internal bool _keepBenchForLoad;

    /// <summary>명단에서 고른 사람(다음에 누르는 초록 칸에 선다).</summary>
    internal UnitState? _deployPick;

    /// <summary>
    /// 패드로 짚고 있는 자리(fa-43) — 십자키 위 · 아래로 옮기고 A 로 고른다. 명단 줄 다음에 단추 넷(배치취소 · 자동배치 · 군단사용 · 배치종료)이
    /// 이어진다(fa-55) — 명단 끝에서 더 내려가면 단추로 간다.
    /// </summary>
    internal int _deployFocus;

    /// <summary>명단 아래 단추 수 — 배치취소 · 자동배치 · 군단사용 · 배치종료.</summary>
    internal const int DeployButtons = 4;

    /// <summary>마지막으로 짚은 명단 줄 — 단추를 짚고 있을 때 「배치취소」 · 「군단사용」이 누구 것인지.</summary>
    internal int _deployLastRow;

    /// <summary>패드가 가리키는 명단 줄 — 단추를 짚고 있으면 마지막으로 짚은 줄.</summary>
    internal int DeployRowFocus(int rosterCount) => Math.Clamp(_deployFocus < rosterCount ? _deployFocus : _deployLastRow, 0, Math.Max(0, rosterCount - 1));

    /// <summary>패드로 인물을 고른 뒤 <b>세울 칸을 고르는 중</b>인지 — 십자키가 초록 칸 사이를 옮기고 A 가 세운다, B 는 명단으로.</summary>
    internal bool _deployCellMode;

    /// <summary>패드 — 명단 줄을 위 · 아래로. 끝에서 반대쪽으로 돈다.</summary>
    internal void DeployFocusMove(int step)
    {
        var roster = DeployRoster();
        int count = roster.Count + DeployButtons;
        _deployFocus = ((_deployFocus + step) % count + count) % count;
        if (_deployFocus >= roster.Count) return;
        _deployLastRow = _deployFocus;
        // 짚은 인물이 판에 서 있으면 카메라가 그 인물을 잡는다(사용자 요청 fa-54) — 명단만 봐서는 어디 선 누구인지 알 수 없었다.
        if (roster[_deployFocus] is { OnField: true } shown) CenterOnUnit(shown);
    }

    /// <summary>
    /// 패드 A — 명단에서 짚은 인물을 골라 칸 고르기로 들어간다. 커서를 둘 칸(이미 서 있으면 그 칸, 아니면 첫 빈 초록 칸)을 돌려준다.
    /// 고정(Entry) 인물이면 알리고 null.
    /// </summary>
    internal (int Col, int Row)? DeployPadPick()
    {
        var roster = DeployRoster();
        // 단추를 짚고 있으면 그 단추를 누른다.
        if (_deployFocus >= roster.Count)
        {
            switch (_deployFocus - roster.Count)
            {
                case 0: DeployPadUnplace(); break;
                case 1: AutoDeploy(); break;
                case 2: DeployPadLegion(); break;
                default: FinishDeploy(); break;
            }
            return null;
        }
        if (roster.Count == 0) return null;
        var u = roster[_deployFocus];
        if (!DeployMovable(u)) { host.Toast($"{host.UnitName(Array.IndexOf(host._units, u))} — 이 전투에 고정된 인물(Entry)입니다"); return null; }
        _deployPick = u;
        _deployCellMode = true;
        (int Col, int Row)? cell = u.OnField ? (u.Col, u.Row) : null;
        if (cell == null)
            foreach (var (col, row, _) in _deploySpots) if (UnitOnCell(col, row) == null) { cell = (col, row); break; }
        cell ??= _deploySpots.Count > 0 ? (_deploySpots[0].Col, _deploySpots[0].Row) : null;
        // 커서를 둘 칸을 곧바로 화면 가운데에 — 부른 쪽이 바로 뒤에 커서를 그 칸에 놓는다(fa-54).
        if (cell is { } at) CenterNow(at.Col * TileW + TileW / 2, host.CellCenterY(at.Col, at.Row));
        return cell;
    }

    /// <summary>패드 B — 칸 고르기를 그만두고 명단으로.</summary>
    internal void DeployPadCancel() { _deployPick = null; _deployCellMode = false; }

    /// <summary>패드 X — 짚은 인물을 판에서 뺀다.</summary>
    internal void DeployPadUnplace()
    {
        var roster = DeployRoster();
        if (roster.Count == 0) return;
        var u = roster[DeployRowFocus(roster.Count)];
        if (!DeployMovable(u) || !u.OnField) { host.Toast("판에 선 인물을 짚고 누르세요"); return; }
        Unplace(u);
        if (_deployPick == u) DeployPadCancel();
    }

    /// <summary>패드 LB — 짚은 인물의 「군단사용」을 켜고 끈다. 고른 인물은 그대로 둔다.</summary>
    internal void DeployPadLegion()
    {
        var roster = DeployRoster();
        if (roster.Count == 0) return;
        var was = _deployPick;
        _deployPick = roster[DeployRowFocus(roster.Count)];
        ToggleDeployLegion();
        _deployPick = was;
    }

    /// <summary>칸에 세운 뒤(패드) — 칸 고르기를 끝내고 다음 미배치 인물을 짚는다. 다 세웠으면 「배치종료」 쪽을 알린다.</summary>
    internal void DeployPadPlaced()
    {
        if (!_deployCellMode) return;
        _deployCellMode = false;
        var roster = DeployRoster();
        int next = roster.FindIndex(u => DeployMovable(u) && !u.OnField);
        if (next >= 0) _deployFocus = _deployLastRow = next;
        else
        {
            // 다 세웠으면 「배치종료」 단추를 짚어 둔다 — 확인만 누르면 시작한다(fa-55).
            _deployFocus = roster.Count + DeployButtons - 1;
            host.Hint($"모두 세웠습니다 — {host.Pad.Name(PadAction.Confirm)}: 배치종료");
        }
    }

    internal const uint DeployLayer = 0x14C83C;          // 층 13 초록 (20,200,60)
    internal const int DeployW = 220, DeployRowH = 24, DeployTop = 34;

    /// <summary>명단 — 내가 움직이는 사람(편 4) 가운데 군단 부하가 아닌 이. Btl 에 박힌 사람(고정)이 앞.</summary>
    internal List<UnitState> DeployRoster() =>
        [.. host._units.Where(u => u.PlayerControlled && u.LeaderIndex < 0 && u.Alive).OrderBy(u => DeployMovable(u) ? 1 : 0)];

    /// <summary>옮길 수 있는 사람 — BuildUnits 가 배치칸이나 맵 밖에 세운 파티원. Btl 레코드로 선 사람은 「Entry」 고정.</summary>
    internal bool DeployMovable(UnitState u) => _deployMovable.Contains(u);

    internal readonly HashSet<UnitState> _deployMovable = [];

    /// <summary>「군단사용」을 끈 대장(명단 +0x30 = 0) — 부하를 맵 밖에 두었다가 배치종료 때 지운다.</summary>
    internal readonly HashSet<UnitState> _deployNoLegion = [];

    /// <summary>이 전투가 군단을 허용하나(머리 워드 7) — 「군단사용」 단추가 켜지는 조건(<c>0x100e257c</c>).</summary>
    internal bool _deployLegionsAllowed;

    /// <summary>「군단사용」을 켜고 끌 수 있는 사람 — 옮길 수 있고 배치 단계가 부하를 만들어 둔 대장.</summary>
    internal bool DeployHasLegion(UnitState u) =>
        DeployMovable(u) && Array.IndexOf(host._units, u) is var li and >= 0 && host._units.Any(f => f.LeaderIndex == li);

    /// <summary>
    /// 「군단사용」 단추(0x4b8) — 고른 사람의 명단 <c>+0x30</c> 을 뒤집는다. 켜면 부하를 진형대로 다시 세우고(0x100673dd~), 끄면 맵 밖으로 뺀다.
    /// </summary>
    internal void ToggleDeployLegion()
    {
        if (!_deployLegionsAllowed) { host.Toast("이 전투에서는 군단을 쓸 수 없습니다"); return; }
        if (_deployPick is not { } u) { host.Toast("군단을 켜고 끌 사람을 명단에서 고르세요"); return; }
        if (!DeployHasLegion(u)) { host.Toast($"{host.UnitName(Array.IndexOf(host._units, u))} — 배속된 군단이 없습니다"); return; }
        if (_deployNoLegion.Remove(u))
        {
            PlaceLegionAround(u);
            host.Toast($"{host.UnitName(Array.IndexOf(host._units, u))} — 군단사용 켬");
        }
        else
        {
            _deployNoLegion.Add(u);
            HideDeployFollowers(u);
            host.Toast($"{host.UnitName(Array.IndexOf(host._units, u))} — 군단사용 끔");
        }
    }

    /// <summary>군단을 끈 대장의 부하를 맵 밖(0,0)으로 — 배치 단계 동안만 남겨 둔다.</summary>
    internal void HideDeployFollowers(UnitState leader)
    {
        int li = Array.IndexOf(host._units, leader);
        if (li < 0) return;
        foreach (var f in FollowersOf(li))
        {
            _followerTarget.Remove(f);
            f.ResetTo(0, 0);
            f.StartCol = f.StartRow = 0;
            f.OnField = false;
        }
    }

    /// <summary>새 전투를 열 때 — 배치칸이 있으면 배치 단계를 연다, 아니면 맵 밖에 둔 여분을 지운다.</summary>
    internal void BeginDeployOrDrop(DemoScene scene, bool fresh)
    {
        _deployOpen = false;
        _deployPick = null;
        _deployNoLegion.Clear();
        _deployLegionsAllowed = scene.LegionsAllowed;
        bool open = fresh && scene.Placement is { Count: > 0 } && !AutoPlay
                    && Environment.GetEnvironmentVariable("DUELDX_NODEPLOY") != "1" && _deployMovable.Count > 0;
        if (!open)
        {
            if (_keepBenchForLoad) return;      // 불러오기 — 세이브에 적힌 인물을 보고 LoadBattleFrom 이 추린다
            if (_deployBench.Count > 0) DropUnits(_deployBench.Contains);   // 맵 밖 여분의 부하도 함께, 대장 번호는 다시 맞춘다
            _deployBench.Clear();
            return;
        }
        _deploySpots = scene.Placement!;
        _deployOpen = true;
        _deployFocus = _deployLastRow = 0;
        _deployCamPending = true;   // 판을 연 뒤의 처음 자리 잡기가 덮어쓰므로 첫 프레임에 옮긴다(StepDeploy)
        host.Hint("캐릭터 배치 — 명단에서 고르고 초록 칸을 누르세요 (자동배치 · 배치종료, Enter: 배치종료)");
    }

    internal bool _deployCamPending;

    /// <summary>
    /// 배치 단계의 한 틀 — 원본 상태 2 에는 카메라 명령이 없다(감사4 C5): 첫 화면은 Btl 머리 워드 2·3 자리 그대로이고
    /// 가장자리 스크롤로만 옮긴다. 예전에는 배치칸 평균으로 옮겼다.
    /// </summary>
    internal void StepDeploy() => _deployCamPending = false;

    internal UnitState? UnitOnCell(int col, int row) => host._units.FirstOrDefault(u => u.Alive && u.OnField && u.Col == col && u.Row == row);

    internal int SpotIndex(int col, int row)
    {
        for (int i = 0; i < _deploySpots.Count; i++) if (_deploySpots[i].Col == col && _deploySpots[i].Row == row) return i;
        return -1;
    }

    /// <summary>그 사람을 배치칸 i 에 세운다 — 방향은 칸 레코드 방향. RESTART 도 이 자리로 돌아오게 시작 자리도 옮긴다.</summary>
    internal void PlaceOnSpot(UnitState u, int i)
    {
        var (col, row, facing) = _deploySpots[i];
        u.ResetTo(col, row);
        u.StartCol = col;
        u.StartRow = row;
        u.Facing = u.StartFacing = facing;
        u.OnField = true;
        // 군단 대장이면 부하도 새 자리 둘레에 진형대로 — 「군단사용」을 끈 사람은 부하를 맵 밖에 둔다.
        if (_deployNoLegion.Contains(u)) HideDeployFollowers(u); else PlaceLegionAround(u);
    }

    internal void Unplace(UnitState u)
    {
        u.OnField = false;
        u.ResetTo(0, 0);
        u.StartCol = u.StartRow = 0;
        if (_deployNoLegion.Contains(u)) HideDeployFollowers(u); else PlaceLegionAround(u);
    }

    /// <summary>자동배치 — 안 놓인 사람 중 명단 첫 사람을 칸 순서 첫 빈 칸에(원본 상태 1).</summary>
    internal void AutoDeploy()
    {
        foreach (var u in DeployRoster().Where(u => DeployMovable(u) && !u.OnField))
        {
            int spot = Enumerable.Range(0, _deploySpots.Count).FirstOrDefault(i => UnitOnCell(_deploySpots[i].Col, _deploySpots[i].Row) == null, -1);
            if (spot < 0) break;
            PlaceOnSpot(u, spot);
        }
        _deployPick = null;
    }

    /// <summary>배치종료 — 안 놓인 사람은 지우고 전투를 시작한다.</summary>
    internal void FinishDeploy()
    {
        if (!DeployRoster().Any(u => u.OnField)) { host.Toast("한 명 이상 세워야 합니다"); return; }
        // 「군단사용」을 끈 대장 — 이 전투의 군단(+0x4ea)은 0 이고 부하는 없다(원본은 켜진 사람만 부하를 만든다, 0x100673dd~).
        var noLegion = _deployNoLegion.Where(u => u.OnField).ToHashSet();
        foreach (var leader in noLegion) leader.LegionId = 0;
        DropUnits(u => (DeployMovable(u) && !u.OnField)
                       || (u.LeaderIndex >= 0 && u.LeaderIndex < host._units.Length && noLegion.Contains(host._units[u.LeaderIndex])));
        _deployNoLegion.Clear();
        _deployBench.Clear();
        _deployMovable.Clear();
        _deployPick = null;
        _deployOpen = false;
        host._selected = -1;
        host.Hint("");
    }

    internal (int X, int Y) DeployPanel() => (host._camX + host.ViewWidth - DeployW - 12, host._camY + GridTop + 12);

    /// <summary>배치 단계의 클릭 — 명단·단추·초록 칸. 배치 중에는 다른 클릭을 받지 않는다.</summary>
    internal bool OnDeployClick(int bx, int by)
    {
        if (!_deployOpen) return false;
        var roster = DeployRoster();
        var (px, py) = DeployPanel();
        int listBottom = py + DeployTop + roster.Count * DeployRowH;
        if (bx >= px && bx < px + DeployW && by >= py && by < listBottom + 142)
        {
            int row = (by - py - DeployTop) / DeployRowH;
            if (by >= py + DeployTop && row >= 0 && row < roster.Count)
            {
                var u = roster[row];
                if (!DeployMovable(u)) host.Toast($"{host.UnitName(Array.IndexOf(host._units, u))} — 이 전투에 고정된 인물(Entry)입니다");
                else _deployPick = _deployPick == u ? null : u;
                return true;
            }
            int by0 = listBottom + 8;
            if (by >= by0 && by < by0 + 26)
            {
                if (bx < px + DeployW / 2) { if (_deployPick is { OnField: true } p) { Unplace(p); } else host.Toast("뺄 사람을 명단에서 고르세요"); }
                else AutoDeploy();
                return true;
            }
            if (by >= by0 + 32 && by < by0 + 58) { ToggleDeployLegion(); return true; }
            if (by >= by0 + 64 && by < by0 + 92) { FinishDeploy(); return true; }
            return true;
        }

        if (by < GridTop) return true;
        int col = bx / TileW, cellRow = host.RowAt(bx, by);
        int spot = cellRow < 0 ? -1 : SpotIndex(col, cellRow);
        var occupant = host.UnitOrFoeAt(bx, by) is var hit and >= 0 ? host._units[hit] : cellRow >= 0 ? UnitOnCell(col, cellRow) : null;
        if (occupant != null && DeployMovable(occupant)) { _deployPick = _deployPick == occupant ? null : occupant; return true; }
        if (spot >= 0 && occupant == null && _deployPick is { } pick) { PlaceOnSpot(pick, spot); _deployPick = null; DeployPadPlaced(); return true; }
        if (spot < 0 && _deployPick != null) host.Toast("초록 칸에만 세울 수 있습니다");
        return true;
    }

    internal bool OnDeployKey(int key)
    {
        if (!_deployOpen) return false;
        if (key == Win32.VK_RETURN) FinishDeploy();
        else if (key == Win32.VK_ESCAPE) _deployPick = null;
        return true;
    }

    internal void DrawDeployCells()
    {
        // 행동 906 의 초록 사각형 — 같은 층 13 그림(200틱).
        if (_highlightRect is var (x1, y1, x2, y2) && host._lastTime < _highlightUntil)
            for (int r = Math.Max(0, y1); r <= Math.Min(host.Rows - 1, y2); r++)
                for (int c = Math.Max(0, x1); c <= Math.Min(host.Cols - 1, x2); c++) PaintCell(c, r, DeployLayer);
        if (!_deployOpen) return;
        foreach (var (col, row, _) in _deploySpots) PaintCell(col, row, DeployLayer);
    }

    /// <summary>배치된 · 미배치 · 고정의 색 — 한눈에 갈리게(사용자 요청 fa-43: 「배치」·「대기」 글만으로는 안 보인다).</summary>
    private const uint PlacedColor = 0xFF70E090, UnplacedColor = 0xFFFFA060, FixedColor = 0xFFB4B4B4;

    internal void DrawDeployPanel()
    {
        if (!_deployOpen || host._db is not { } db) return;
        var roster = DeployRoster();
        var (x, y) = DeployPanel();
        bool pad = host.Pad.Active && host.Pad.Connected;
        int h = DeployTop + roster.Count * DeployRowH + 108 + (pad ? 18 : 0);
        host.FillRect(x, y, DeployW, h, StatusScreen.PanelBg);
        host.StrokeRect(x, y, DeployW, h, StatusScreen.BoxLine);
        host.FillRect(x, y, DeployW, 26, StatusScreen.HeadBg);
        int movable = roster.Count(DeployMovable), placed = roster.Count(u => DeployMovable(u) && u.OnField);
        host.DrawText("캐릭터 배치", x + 10, y + 4, White);
        string count = $"{placed}/{movable}";
        var (_, cw, _) = host.GetText(count, White);
        host.DrawText(count, x + DeployW - 12 - cw, y + 4, placed == movable ? PlacedColor : UnplacedColor);

        for (int i = 0; i < roster.Count; i++)
        {
            var u = roster[i];
            int ry = y + DeployTop + i * DeployRowH;
            bool fixedHere = !DeployMovable(u);
            if (u == _deployPick) host.FillRect(x + 4, ry - 2, DeployW - 8, DeployRowH - 2, 0xFF2A4A8A);
            // 패드가 짚은 줄 — 커서 테와 같은 색의 둥근 테(칸 고르기 중에는 고른 인물 줄에 그대로).
            if (pad && i == _deployFocus && !_deployCellMode) host.Pad.StrokeFocus(x + 4, ry - 2, DeployW - 8, DeployRowH - 2);
            string name = u.Data is { } c ? db.T(c.NameId) : $"Chr {u.ChrCode}";
            uint colour = fixedHere ? FixedColor : u.OnField ? White : UnplacedColor;
            // 줄 머리의 점 — 찬 점은 판에 섰다, 빈 점은 아직이다. 고정은 점 없이 이름만.
            if (!fixedHere)
            {
                if (u.OnField) host.FillCircle(x + 14, ry + 9, 3, PlacedColor);
                else host.StrokeCircle(x + 14, ry + 9, 3, UnplacedColor, 1);
            }
            host.DrawText(name, x + 24, ry + 2, colour);
            if (DeployHasLegion(u) && !_deployNoLegion.Contains(u))
            {
                var (_, nw, _) = host.GetText(name, colour);
                host.DrawText("군단", x + 28 + nw, ry + 3, 0xFF80D0FF, 11);
            }
            string tag = fixedHere ? "고정" : u.OnField ? "배치됨" : "미배치";
            uint tagColor = fixedHere ? 0xFFFFE070 : u.OnField ? PlacedColor : UnplacedColor;
            var (_, tw, _) = host.GetText(tag, tagColor);
            host.DrawText(tag, x + DeployW - 12 - tw, ry + 2, tagColor);
        }

        int by = y + DeployTop + roster.Count * DeployRowH + 8;
        if (pad)
        {
            // 지금 할 일 한 줄 — 명단에서는 고르기, 칸 고르기에서는 세우기.
            string step = _deployCellMode ? $"십자키로 초록 칸 → {host.Pad.Name(PadAction.Confirm)} 세우기 · {host.Pad.Name(PadAction.Cancel)} 돌아가기"
                                          : $"십자키 위 · 아래로 인물 · 단추 → {host.Pad.Name(PadAction.Confirm)}";
            host.DrawText(step, x + 10, by - 2, 0xFFD0E8D0, 11);
            by += 18;
        }
        string Key(PadAction a) => pad ? $" {host.Pad.Name(a)}" : "";
        Button(x + 8, by, DeployW / 2 - 12, 26, "배치취소" + Key(PadAction.Ring), StatusScreen.HeadBg, White);
        Button(x + DeployW / 2 + 4, by, DeployW / 2 - 12, 26, "자동배치" + Key(PadAction.Rest), StatusScreen.HeadBg, White);
        // 「군단사용」 — 짚은(또는 고른) 사람의 군단 켬/끔. 워드 7 이 꺼진 전투면 흐리게(0x100e257c).
        var who = _deployPick ?? (pad && roster.Count > 0 ? roster[DeployRowFocus(roster.Count)] : null);
        string legionLabel = who != null && DeployHasLegion(who) ? (_deployNoLegion.Contains(who) ? "군단사용: 끔" : "군단사용: 켬") : "군단사용";
        Button(x + 8, by + 32, DeployW - 16, 26, legionLabel + Key(PadAction.PrevUnit), StatusScreen.HeadBg, _deployLegionsAllowed ? White : 0xFF707070);
        Button(x + 8, by + 64, DeployW - 16, 28, pad ? "배치종료" + Key(PadAction.System) : "배치종료 (Enter)", 0xFF2A6A3A, White, border: true);
        // 패드가 단추를 짚고 있으면 그 단추에 테.
        if (pad && !_deployCellMode && _deployFocus >= roster.Count)
        {
            var (fx0, fy0, fw, fh) = (_deployFocus - roster.Count) switch
            {
                0 => (x + 8, by, DeployW / 2 - 12, 26),
                1 => (x + DeployW / 2 + 4, by, DeployW / 2 - 12, 26),
                2 => (x + 8, by + 32, DeployW - 16, 26),
                _ => (x + 8, by + 64, DeployW - 16, 28),
            };
            host.Pad.StrokeFocus(fx0, fy0, fw, fh);
        }

        // 고른 사람은 발밑에 표시
        if (_deployPick is { OnField: true } pick)
        {
            var (fx, fy) = host.Btl.UnitFoot(pick);
            host.StrokeRect(fx - TileW / 2, fy - TileH / 2, TileW, TileH, GamePad.FocusColor);
        }

        void Button(int bx, int by2, int bw, int bh, string text, uint bg, uint fg, bool border = false)
        {
            host.FillRect(bx, by2, bw, bh, bg);
            if (border) host.StrokeRect(bx, by2, bw, bh, StatusScreen.BoxLine);
            var (_, tw, th) = host.GetText(text, fg);
            host.DrawText(text, bx + (bw - tw) / 2, by2 + (bh - th) / 2, fg);
        }
    }
}
