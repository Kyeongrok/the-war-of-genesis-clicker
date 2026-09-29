using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 「캐릭터 배치」 단계(원본 전투 상태 2, <c>0x10066d00</c>) — 배치칸이 있는 전투를 새로 시작하면 전투를 멈추고 파티원을 초록 칸에 세운다(fg-17).
/// </summary>
/// <remarks>
/// 분석-전투 ba-6: 배치칸(Btl C 절)마다 층 13 초록 (20,200,60). 창 명단 = 플레이어 부대 전원, Btl 에 이미 선 부대원은 「Entry」로 고정.
/// 한 명 놓기 = 초록이고 빈 칸일 때만, 방향 = 그 칸 레코드의 방향. 자동배치(상태 1 <c>0x10066480</c>) = 안 놓인 사람 중 명단 첫 사람 →
/// 칸 순서 첫 빈 칸을 되풀이. 배치종료 = 안 놓인 부대원 유닛을 지우고 상태 5 로.
/// 데모는 처음부터 자동배치한 채로 연다(그대로 배치종료만 눌러도 된다). 원본의 「군단사용」 단추는 아직 없다 — 모세스에서 붙인 군단을 따른다.
/// 불러온 판·RESTART·화면 밖 자동 진행(DUELDX_AUTOPLAY)에는 이 단계가 없다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>배치 단계가 열려 있나 — 열려 있는 동안 전투(틱·차례·이벤트)는 멈춘다.</summary>
    private bool _deployOpen;

    /// <summary>배치칸(칸, 방향) — 전투 자료 순서.</summary>
    private IReadOnlyList<(int Col, int Row, Facing Facing)> _deploySpots = [];

    /// <summary>BuildUnits 가 배치칸이 모자라 맵 밖에 만들어 둔 파티원 — 배치 단계를 안 열면 지운다.</summary>
    private readonly HashSet<UnitState> _deployBench = [];

    /// <summary>명단에서 고른 사람(다음에 누르는 초록 칸에 선다).</summary>
    private UnitState? _deployPick;

    private const uint DeployLayer = 0x14C83C;          // 층 13 초록 (20,200,60)
    private const int DeployW = 220, DeployRowH = 24, DeployTop = 34;

    /// <summary>명단 — 내가 움직이는 사람(편 4) 가운데 군단 부하가 아닌 이. Btl 에 박힌 사람(고정)이 앞.</summary>
    private List<UnitState> DeployRoster() =>
        [.. _units.Where(u => u.PlayerControlled && u.LeaderIndex < 0 && u.Alive).OrderBy(u => DeployMovable(u) ? 1 : 0)];

    /// <summary>옮길 수 있는 사람 — BuildUnits 가 배치칸이나 맵 밖에 세운 파티원. Btl 레코드로 선 사람은 「Entry」 고정.</summary>
    private bool DeployMovable(UnitState u) => _deployMovable.Contains(u);

    private readonly HashSet<UnitState> _deployMovable = [];

    /// <summary>새 전투를 열 때 — 배치칸이 있으면 배치 단계를 연다, 아니면 맵 밖에 둔 여분을 지운다.</summary>
    private void BeginDeployOrDrop(DemoScene scene, bool fresh)
    {
        _deployOpen = false;
        _deployPick = null;
        bool open = fresh && scene.Placement is { Count: > 0 } && !AutoPlay
                    && Environment.GetEnvironmentVariable("DUELDX_NODEPLOY") != "1" && _deployMovable.Count > 0;
        if (!open)
        {
            if (_deployBench.Count > 0) DropUnits(_deployBench.Contains);   // 맵 밖 여분의 부하도 함께, 대장 번호는 다시 맞춘다
            _deployBench.Clear();
            return;
        }
        _deploySpots = scene.Placement!;
        _deployOpen = true;
        _deployCamPending = true;   // 판을 연 뒤의 처음 자리 잡기가 덮어쓰므로 첫 프레임에 옮긴다(StepDeploy)
        Hint("캐릭터 배치 — 명단에서 고르고 초록 칸을 누르세요 (자동배치 · 배치종료, Enter: 배치종료)");
    }

    private bool _deployCamPending;

    /// <summary>배치 단계의 한 틀 — 처음 한 번 카메라를 배치칸 가운데로 옮긴다.</summary>
    private void StepDeploy()
    {
        if (!_deployCamPending || _deploySpots.Count == 0) return;
        _deployCamPending = false;
        int col = (int)_deploySpots.Average(s => s.Col), row = (int)_deploySpots.Average(s => s.Row);
        _camTargetX = Math.Clamp(col * TileW + TileW / 2 - ViewWidth / 2 + DeployW / 2, 0, CamMaxX);
        _camTarget = Math.Clamp(CellTop(col, row) - ViewHeight / 2, 0, CamMax);
    }

    private UnitState? UnitOnCell(int col, int row) => _units.FirstOrDefault(u => u.Alive && u.OnField && u.Col == col && u.Row == row);

    private int SpotIndex(int col, int row)
    {
        for (int i = 0; i < _deploySpots.Count; i++) if (_deploySpots[i].Col == col && _deploySpots[i].Row == row) return i;
        return -1;
    }

    /// <summary>그 사람을 배치칸 i 에 세운다 — 방향은 칸 레코드 방향. RESTART 도 이 자리로 돌아오게 시작 자리도 옮긴다.</summary>
    private void PlaceOnSpot(UnitState u, int i)
    {
        var (col, row, facing) = _deploySpots[i];
        u.ResetTo(col, row);
        u.StartCol = col;
        u.StartRow = row;
        u.Facing = u.StartFacing = facing;
        u.OnField = true;
        PlaceLegionAround(u);    // 군단 대장이면 부하도 새 자리 둘레에 진형대로
    }

    private void Unplace(UnitState u)
    {
        u.OnField = false;
        u.ResetTo(0, 0);
        u.StartCol = u.StartRow = 0;
        PlaceLegionAround(u);
    }

    /// <summary>자동배치 — 안 놓인 사람 중 명단 첫 사람을 칸 순서 첫 빈 칸에(원본 상태 1).</summary>
    private void AutoDeploy()
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
    private void FinishDeploy()
    {
        if (!DeployRoster().Any(u => u.OnField)) { Toast("한 명 이상 세워야 합니다"); return; }
        DropUnits(u => DeployMovable(u) && !u.OnField);
        _deployBench.Clear();
        _deployMovable.Clear();
        _deployPick = null;
        _deployOpen = false;
        _selected = -1;
        Hint("");
    }

    private (int X, int Y) DeployPanel() => (_camX + ViewWidth - DeployW - 12, _camY + GridTop + 12);

    /// <summary>배치 단계의 클릭 — 명단·단추·초록 칸. 배치 중에는 다른 클릭을 받지 않는다.</summary>
    private bool OnDeployClick(int bx, int by)
    {
        if (!_deployOpen) return false;
        var roster = DeployRoster();
        var (px, py) = DeployPanel();
        int listBottom = py + DeployTop + roster.Count * DeployRowH;
        if (bx >= px && bx < px + DeployW && by >= py && by < listBottom + 110)
        {
            int row = (by - py - DeployTop) / DeployRowH;
            if (by >= py + DeployTop && row >= 0 && row < roster.Count)
            {
                var u = roster[row];
                if (!DeployMovable(u)) Toast($"{UnitName(Array.IndexOf(_units, u))} — 이 전투에 고정된 인물(Entry)입니다");
                else _deployPick = _deployPick == u ? null : u;
                return true;
            }
            int by0 = listBottom + 8;
            if (by >= by0 && by < by0 + 26)
            {
                if (bx < px + DeployW / 2) { if (_deployPick is { OnField: true } p) { Unplace(p); } else Toast("뺄 사람을 명단에서 고르세요"); }
                else AutoDeploy();
                return true;
            }
            if (by >= by0 + 32 && by < by0 + 60) { FinishDeploy(); return true; }
            return true;
        }

        if (by < GridTop) return true;
        int col = bx / TileW, cellRow = RowAt(bx, by);
        int spot = cellRow < 0 ? -1 : SpotIndex(col, cellRow);
        var occupant = UnitOrFoeAt(bx, by) is var hit and >= 0 ? _units[hit] : cellRow >= 0 ? UnitOnCell(col, cellRow) : null;
        if (occupant != null && DeployMovable(occupant)) { _deployPick = _deployPick == occupant ? null : occupant; return true; }
        if (spot >= 0 && occupant == null && _deployPick is { } pick) { PlaceOnSpot(pick, spot); _deployPick = null; return true; }
        if (spot < 0 && _deployPick != null) Toast("초록 칸에만 세울 수 있습니다");
        return true;
    }

    private bool OnDeployKey(int key)
    {
        if (!_deployOpen) return false;
        if (key == Win32.VK_RETURN) FinishDeploy();
        else if (key == Win32.VK_ESCAPE) _deployPick = null;
        return true;
    }

    private void DrawDeployCells()
    {
        // 행동 906 의 초록 사각형 — 같은 층 13 그림(200틱).
        if (_highlightRect is var (x1, y1, x2, y2) && _lastTime < _highlightUntil)
            for (int r = Math.Max(0, y1); r <= Math.Min(Rows - 1, y2); r++)
                for (int c = Math.Max(0, x1); c <= Math.Min(Cols - 1, x2); c++) PaintCell(c, r, DeployLayer);
        if (!_deployOpen) return;
        foreach (var (col, row, _) in _deploySpots) PaintCell(col, row, DeployLayer);
    }

    private void DrawDeployPanel()
    {
        if (!_deployOpen || _db is not { } db) return;
        var roster = DeployRoster();
        var (x, y) = DeployPanel();
        int h = DeployTop + roster.Count * DeployRowH + 76;
        FillRect(x, y, DeployW, h, PanelBg);
        StrokeRect(x, y, DeployW, h, BoxLine);
        FillRect(x, y, DeployW, 26, HeadBg);
        int placed = roster.Count(u => u.OnField);
        DrawText($"캐릭터 배치  {placed}/{_deploySpots.Count + roster.Count(u => !DeployMovable(u))}", x + 10, y + 4, White);
        for (int i = 0; i < roster.Count; i++)
        {
            var u = roster[i];
            int ry = y + DeployTop + i * DeployRowH;
            if (u == _deployPick) FillRect(x + 4, ry - 2, DeployW - 8, DeployRowH - 2, 0xFF2A4A8A);
            string name = u.Data is { } c ? db.T(c.NameId) : $"Chr {u.ChrCode}";
            uint colour = !DeployMovable(u) ? 0xFFB4B4B4 : u.OnField ? White : 0xFF909090;
            DrawText(name, x + 12, ry + 2, colour);
            string tag = !DeployMovable(u) ? "Entry" : u.OnField ? "배치" : "대기";
            var (_, tw, _) = GetText(tag, colour);
            DrawText(tag, x + DeployW - 12 - tw, ry + 2, !DeployMovable(u) ? 0xFFFFE070 : colour);
        }
        int by = y + DeployTop + roster.Count * DeployRowH + 8;
        FillRect(x + 8, by, DeployW / 2 - 12, 26, HeadBg);
        DrawText("배치취소", x + 28, by + 4, White);
        FillRect(x + DeployW / 2 + 4, by, DeployW / 2 - 12, 26, HeadBg);
        DrawText("자동배치", x + DeployW / 2 + 24, by + 4, White);
        FillRect(x + 8, by + 32, DeployW - 16, 28, 0xFF2A6A3A);
        StrokeRect(x + 8, by + 32, DeployW - 16, 28, BoxLine);
        var (_, fw, _) = GetText("배치종료 (Enter)", White);
        DrawText("배치종료 (Enter)", x + (DeployW - fw) / 2, by + 37, White);

        // 고른 사람은 발밑에 표시
        if (_deployPick is { OnField: true } pick)
        {
            var (fx, fy) = UnitFoot(pick);
            StrokeRect(fx - TileW / 2, fy - TileH / 2, TileW, TileH, 0xFFFFE070);
        }
    }
}
