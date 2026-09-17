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
/// 인물의 지금 TP 로 셈하고, 자리나 TP 가 바뀌면 다시 셈한다.
/// Btl 오브젝트(Obj)·날기·큰 유닛·높이 보정이 붙는 공격 모양은 빠져 있다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const uint MoveBlue = 0x786464FF, AttackRed = 0x78FF6428;
    private const double RangeWaveCellsPerSecond = 30;

    /// <summary>한 인물의 이동 영역 — 칸마다 드는 TP(못 가면 <see cref="int.MaxValue"/>), 되짚을 앞 칸, 빨간 칸.</summary>
    private sealed class MoveRange(int[] cost, int[] prev, bool[] red)
    {
        public int[] Cost { get; } = cost;
        public int[] Prev { get; } = prev;
        public bool[] Red { get; } = red;

        public bool CanReach(int index) => (uint)index < Cost.Length && Cost[index] != int.MaxValue;

        /// <summary>출발 칸을 뺀, 도착 칸까지 밟을 칸들.</summary>
        public List<(int Col, int Row)> PathTo(int index)
        {
            var path = new List<(int Col, int Row)>();
            for (int i = index; i >= 0 && Prev[i] >= 0; i = Prev[i]) path.Add((i % Cols, i / Cols));
            path.Reverse();
            return path;
        }
    }

    private MoveRange? _range;
    private int _rangeUnit = -1, _rangeCol = -1, _rangeRow = -1, _rangeTp = -1;
    private double _rangeStart;

    /// <summary>고른 인물이 서 있으면 영역을 (필요할 때만) 다시 셈한다. 고른 인물이 없거나 움직이는 중이면 지운다.</summary>
    private void RefreshMoveRange()
    {
        if ((uint)_selected >= _units.Length || !_units[_selected].Alive || _units[_selected].IsBusy || _routine != null)
        {
            _rangeUnit = -1;
            return;
        }
        var unit = _units[_selected];
        if (_selected == _rangeUnit && unit.Col == _rangeCol && unit.Row == _rangeRow && unit.Tp == _rangeTp) return;

        _range = ComputeRange(unit);
        if (_range == null) { _rangeUnit = -1; return; }
        if (_selected != _rangeUnit || unit.Col != _rangeCol || unit.Row != _rangeRow) _rangeStart = _lastTime;
        _rangeUnit = _selected;
        _rangeCol = unit.Col;
        _rangeRow = unit.Row;
        _rangeTp = unit.Tp;
    }

    private UnitState? LiveUnitAt(int col, int row) => _units.FirstOrDefault(u => u.Alive && u.Col == col && u.Row == row);

    /// <summary>인물의 지금 자리·TP 로 이동 영역을 셈한다. 지도·게임 표가 없으면 null.</summary>
    private MoveRange? ComputeRange(UnitState unit)
    {
        if (_map is not { } map || _db is not { } db || unit.Data is not { } c) return null;

        int n = Cols * Rows;
        var costs = new int[n];
        var prev = new int[n];
        var red = new bool[n];
        Array.Fill(costs, int.MaxValue);
        Array.Fill(prev, -1);

        int dex = Math.Max(1, db.Dex(c));
        int num5 = db.N(5);
        int budget = db.MoveBudget(c, unit.Tp);

        int H(int col, int row) => map.HeightAt(col, row);
        bool InBounds(int col, int row) => (uint)col < Cols && (uint)row < Rows && col < map.Cols && row < map.Rows;

        bool Enterable(int col, int row)
        {
            if (col == unit.Col && row == unit.Row) return true;
            if (!InBounds(col, row) || (map.FlagsAt(col, row) & 0x9) != 0) return false;
            if (LiveUnitAt(col, row) is { } other && other != unit) return false;
            foreach (var (px, py) in new[] { (col, row), (col - 1, row), (col + 1, row), (col, row - 1), (col, row + 1) })
            {
                if (!InBounds(px, py) || LiveUnitAt(px, py) is not { } e || e == unit) continue;
                if (e.IsAlly != unit.IsAlly && Math.Abs(H(px, py) - H(col, row)) < 1) return false;
            }
            return true;
        }

        var queue = new PriorityQueue<(int Col, int Row), int>();
        int start = unit.Row * Cols + unit.Col;
        costs[start] = 0;
        queue.Enqueue((unit.Col, unit.Row), 0);
        (int Dx, int Dy)[] dirs = [(0, -1), (1, 0), (0, 1), (-1, 0)];

        while (queue.TryDequeue(out var cell, out int cost))
        {
            if (cost > costs[cell.Row * Cols + cell.Col]) continue;
            foreach (var (dx, dy) in dirs)
            {
                int nx = cell.Col + dx, ny = cell.Row + dy;
                if (!Enterable(nx, ny) || Math.Abs(H(cell.Col, cell.Row) - H(nx, ny)) > 2) continue;
                int step = num5 * Math.Abs(dy) / dex + num5 * Math.Abs(dx) / dex + (num5 * Math.Abs(H(nx, ny) - H(cell.Col, cell.Row)) / dex) / 2;
                int next = cost + step;
                if (next > budget || next >= costs[ny * Cols + nx]) continue;
                costs[ny * Cols + nx] = next;
                prev[ny * Cols + nx] = cell.Row * Cols + cell.Col;
                queue.Enqueue((nx, ny), next);
            }
        }

        // 다른 인물이 선 칸은 파랑에서 뺀다.
        for (int i = 0; i < n; i++)
            if (costs[i] != int.MaxValue && i != start && LiveUnitAt(i % Cols, i / Cols) != null) costs[i] = int.MaxValue;

        // 기본공격 모양 2(십자), 사거리 min 5 · max 8 (한 칸 = 4) → 정확히 2칸 떨어진 상하좌우.
        for (int i = 0; i < n; i++)
        {
            if (costs[i] == int.MaxValue) continue;
            int col = i % Cols, row = i / Cols;
            foreach (var (dx, dy) in dirs)
            {
                int ax = col + 2 * dx, ay = row + 2 * dy;
                if (!InBounds(ax, ay) || (map.FlagsAt(ax, ay) & 0x8) != 0) continue;
                int j = ay * Cols + ax;
                if (costs[j] == int.MaxValue) red[j] = true;
            }
        }
        return new MoveRange(costs, prev, red);
    }

    private void DrawMoveRange()
    {
        if (_rangeUnit < 0 || _range is not { } range) return;
        int radius = (int)((_lastTime - _rangeStart) * RangeWaveCellsPerSecond);

        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                if (Math.Abs(col - _rangeCol) + Math.Abs(row - _rangeRow) > radius) continue;
                int i = row * Cols + col;
                uint color = range.Cost[i] != int.MaxValue ? MoveBlue : range.Red[i] ? AttackRed : 0;
                if (color == 0) continue;
                int x = col * TileW, y = GridTop + row * TileH;
                FillRect(x + 1, y + 1, TileW - 2, TileH - 2, color);
                StrokeRect(x + 1, y + 1, TileW - 2, TileH - 2, color | 0xFF000000);
            }
    }
}
