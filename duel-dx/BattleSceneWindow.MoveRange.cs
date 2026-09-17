using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 고른 인물의 <b>이동 가능 영역</b>(파랑)과 그 자리들에서 기본공격이 닿는 칸(빨강)을 바닥에 깐다.
/// </summary>
/// <remarks>
/// <c>G3PartII.dll</c> 식 그대로(옵시디안 분석-전투 "이동 가능 영역"):
/// <list type="bullet">
/// <item>예산 = 현재 TP + min(0, CTP − 기본공격 TP 비용) — 이동한 뒤에도 기본공격 TP 를 남긴다(상태 12 <c>0x10069cc9</c>).</item>
/// <item>상하좌우 4방향, 한 걸음 = Num[5]·|dy|/DEX + Num[5]·|dx|/DEX + (Num[5]·|높이차|/DEX)/2 (정수 나눗셈).</item>
/// <item>못 들어가는 칸: 판 밖, 지형 플래그 &amp; 0x9, 다른 인물이 선 칸(아군도), 그 칸이나 상하좌우에 같은 높이의 적이
///   있는 칸(적 옆칸 ZOC), 지금 칸과 높이차가 2 를 넘는 칸.</item>
/// <item>빨강: 파란 칸마다 기본공격(모양 2 십자, 사거리 5~8 = 정확히 2칸)이 닿는 칸 중 지형 &amp; 0x8 이 아닌 칸. 파랑이 먼저 칠해진다.</item>
/// <item>색: 파랑 (100,100,255), 빨강 (255,100,40). 원점에서 맨해튼 거리로 물결처럼 퍼지며 깔린다(<c>+0x98</c> 반경).</item>
/// </list>
/// 이 데모는 TP 를 쓰지 않으므로 늘 가득 찬 TP 로 계산하고, 인물이 걸음을 멈추면 그 자리에서 다시 셈한다.
/// Btl 오브젝트(Obj)·날기·큰 유닛·높이 보정이 붙는 공격 모양은 빠져 있다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const uint MoveBlue = 0x786464FF, AttackRed = 0x78FF6428;
    private const double RangeWaveCellsPerSecond = 30;

    private int[] _rangeCost = [];
    private bool[] _rangeRed = [];
    private int _rangeUnit = -1, _rangeCol = -1, _rangeRow = -1;
    private double _rangeStart;

    /// <summary>고른 인물이 서 있으면 영역을 (필요할 때만) 다시 셈한다. 고른 인물이 없거나 걷는 중이면 지운다.</summary>
    private void RefreshMoveRange()
    {
        if ((uint)_selected >= _units.Length || _units[_selected].IsMoving || _map == null || _db == null)
        {
            _rangeUnit = -1;
            return;
        }
        var unit = _units[_selected];
        if (_selected == _rangeUnit && unit.Col == _rangeCol && unit.Row == _rangeRow) return;

        if (_db.Character(unit.ChrCode) is not { } c) { _rangeUnit = -1; return; }
        ComputeMoveRange(unit, c, _map, _db);
        _rangeUnit = _selected;
        _rangeCol = unit.Col;
        _rangeRow = unit.Row;
        _rangeStart = _lastTime;
    }

    private void ComputeMoveRange(UnitState unit, CharacterData c, ObtMapImage map, GameDatabase db)
    {
        int n = Cols * Rows;
        _rangeCost = new int[n];
        _rangeRed = new bool[n];
        Array.Fill(_rangeCost, int.MaxValue);

        int dex = Math.Max(1, db.Dex(c));
        int num5 = db.N(5);
        int budget = db.MoveBudget(c, db.MaxTp(c));

        int H(int col, int row) => map.HeightAt(col, row);
        bool InBounds(int col, int row) => (uint)col < Cols && (uint)row < Rows && col < map.Cols && row < map.Rows;
        UnitState? UnitAt(int col, int row) => _units.FirstOrDefault(u => u.Col == col && u.Row == row);

        bool Enterable(int col, int row)
        {
            if (col == unit.Col && row == unit.Row) return true;
            if (!InBounds(col, row) || (map.FlagsAt(col, row) & 0x9) != 0) return false;
            if (UnitAt(col, row) is { } other && other != unit) return false;
            foreach (var (px, py) in new[] { (col, row), (col - 1, row), (col + 1, row), (col, row - 1), (col, row + 1) })
            {
                if (!InBounds(px, py) || UnitAt(px, py) is not { } e || e == unit) continue;
                if (e.IsAlly != unit.IsAlly && Math.Abs(H(px, py) - H(col, row)) < 1) return false;
            }
            return true;
        }

        var queue = new PriorityQueue<(int Col, int Row), int>();
        int start = unit.Row * Cols + unit.Col;
        _rangeCost[start] = 0;
        queue.Enqueue((unit.Col, unit.Row), 0);
        (int Dx, int Dy)[] dirs = [(0, -1), (1, 0), (0, 1), (-1, 0)];

        while (queue.TryDequeue(out var cell, out int cost))
        {
            if (cost > _rangeCost[cell.Row * Cols + cell.Col]) continue;
            foreach (var (dx, dy) in dirs)
            {
                int nx = cell.Col + dx, ny = cell.Row + dy;
                if (!Enterable(nx, ny) || Math.Abs(H(cell.Col, cell.Row) - H(nx, ny)) > 2) continue;
                int step = num5 * Math.Abs(dy) / dex + num5 * Math.Abs(dx) / dex + (num5 * Math.Abs(H(nx, ny) - H(cell.Col, cell.Row)) / dex) / 2;
                int next = cost + step;
                if (next > budget || next >= _rangeCost[ny * Cols + nx]) continue;
                _rangeCost[ny * Cols + nx] = next;
                queue.Enqueue((nx, ny), next);
            }
        }

        // 다른 인물이 선 칸은 파랑에서 뺀다.
        for (int i = 0; i < n; i++)
            if (_rangeCost[i] != int.MaxValue && i != start && UnitAt(i % Cols, i / Cols) != null) _rangeCost[i] = int.MaxValue;

        // 기본공격 모양 2(십자), 사거리 min 5 · max 8 (한 칸 = 4) → 정확히 2칸 떨어진 상하좌우.
        for (int i = 0; i < n; i++)
        {
            if (_rangeCost[i] == int.MaxValue) continue;
            int col = i % Cols, row = i / Cols;
            foreach (var (dx, dy) in dirs)
            {
                int ax = col + 2 * dx, ay = row + 2 * dy;
                if (!InBounds(ax, ay) || (map.FlagsAt(ax, ay) & 0x8) != 0) continue;
                int j = ay * Cols + ax;
                if (_rangeCost[j] == int.MaxValue) _rangeRed[j] = true;
            }
        }
    }

    private void DrawMoveRange()
    {
        if (_rangeUnit < 0 || _rangeCost.Length != Cols * Rows) return;
        int radius = (int)((_lastTime - _rangeStart) * RangeWaveCellsPerSecond);

        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                if (Math.Abs(col - _rangeCol) + Math.Abs(row - _rangeRow) > radius) continue;
                int i = row * Cols + col;
                uint color = _rangeCost[i] != int.MaxValue ? MoveBlue : _rangeRed[i] ? AttackRed : 0;
                if (color == 0) continue;
                int x = col * TileW, y = GridTop + row * TileH;
                FillRect(x + 1, y + 1, TileW - 2, TileH - 2, color);
                StrokeRect(x + 1, y + 1, TileW - 2, TileH - 2, color | 0xFF000000);
            }
    }
}
