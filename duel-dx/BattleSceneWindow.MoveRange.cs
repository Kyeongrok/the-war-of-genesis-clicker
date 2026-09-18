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
/// 차례인 인물은 <b>차례를 시작한 자리</b>에서 셈한다 — 걷는 동안에는 TP 를 안 쓰고 그 안에서 마음대로 오가며,
/// 공격·어빌리티·휴식을 할 때 시작 자리에서 지금 자리까지의 걸음 비용을 한 번에 뺀다(<see cref="CommitMove"/>).
/// Btl 오브젝트(Obj)·날기·큰 유닛·높이 보정이 붙는 공격 모양은 빠져 있다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>
    /// 칸에 <b>더하는</b> 색 — 원본은 층 색에 칸 밝기(평지 74)를 곱해 <b>가산</b>으로 얹는다(섞기 방식 17, `0x1000e150`).
    /// 알파 섞기가 아니라 <b>밝히기만</b> 한다.
    /// </summary>
    private const uint MoveTint = 0x1C1C49, RangeTint = 0x491C0B;
    private const double RangeWaveCellsPerSecond = 30;

    /// <summary>한 인물의 이동 영역 — 칸마다 드는 TP(못 가면 <see cref="int.MaxValue"/>), 되짚을 앞 칸, 빨간 칸.</summary>
    private sealed class MoveRange(int[] cost, int[] prev, bool[] red)
    {
        public int[] Cost { get; } = cost;
        public int[] Prev { get; } = prev;
        public bool[] Red { get; } = red;

        public bool CanReach(int index) => (uint)index < Cost.Length && Cost[index] != int.MaxValue;

        /// <summary>판 너비 — 칸 번호를 (열, 줄)로 풀 때 쓴다(전투마다 판 크기가 다르다).</summary>
        public int Width { get; init; } = 1;

        /// <summary>출발 칸을 뺀, 도착 칸까지 밟을 칸들.</summary>
        public List<(int Col, int Row)> PathTo(int index)
        {
            var path = new List<(int Col, int Row)>();
            for (int i = index; i >= 0 && Prev[i] >= 0; i = Prev[i]) path.Add((i % Width, i / Width));
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
        var (oc, or) = RangeOrigin(unit);
        if (_selected == _rangeUnit && oc == _rangeCol && or == _rangeRow && unit.Tp == _rangeTp) return;

        _range = ComputeRange(unit);
        if (_range == null) { _rangeUnit = -1; return; }
        if (_selected != _rangeUnit || oc != _rangeCol || or != _rangeRow) _rangeStart = _lastTime;
        _rangeUnit = _selected;
        _rangeCol = oc;
        _rangeRow = or;
        _rangeTp = unit.Tp;
    }

    /// <summary>이동 영역을 셀 출발 칸 — 차례인 인물은 차례 시작 자리, 나머지는 지금 자리.</summary>
    private (int Col, int Row) RangeOrigin(UnitState unit) =>
        _turn >= 0 && _units[_turn] == unit ? (unit.OriginCol, unit.OriginRow) : (unit.Col, unit.Row);

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

        int dex = Math.Max(1, db.Dex(EffectiveData(unit) ?? c));
        int num5 = db.N(5);
        // 25(이동 불가)면 갈 수 있는 칸이 자기 칸뿐이다(0x10074510).
        int budget = unit.HasStatus(25) ? 0 : db.MoveBudget(c, unit.Tp);

        int H(int col, int row) => map.HeightAt(col, row);
        bool InBounds(int col, int row) => (uint)col < Cols && (uint)row < Rows && col < map.Cols && row < map.Rows;

        var (originCol, originRow) = RangeOrigin(unit);

        int unitIndex = Array.IndexOf(_units, unit);
        // 제 군단 부하는 대장을 막지 않는다 — 대장이 움직이면 부하도 진형대로 따라오기 때문이다(분석-군단).
        bool Blocks(UnitState other) => other != unit && other.LeaderIndex != unitIndex;

        bool Enterable(int col, int row)
        {
            if (col == originCol && row == originRow) return true;
            if (!InBounds(col, row) || (map.FlagsAt(col, row) & 0x9) != 0) return false;
            if (LiveUnitAt(col, row) is { } other && Blocks(other)) return false;
            foreach (var (px, py) in new[] { (col, row), (col - 1, row), (col + 1, row), (col, row - 1), (col, row + 1) })
            {
                if (!InBounds(px, py) || LiveUnitAt(px, py) is not { } e || !Blocks(e)) continue;
                if (e.IsAlly != unit.IsAlly && Math.Abs(H(px, py) - H(col, row)) < 1) return false;
            }
            return true;
        }

        var queue = new PriorityQueue<(int Col, int Row), int>();
        if (!InBounds(originCol, originRow)) return null;   // 판 밖에 선 인물(맵이 더 큰 전투)은 이동 영역이 없다
        int start = originRow * Cols + originCol;
        costs[start] = 0;
        queue.Enqueue((originCol, originRow), 0);
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
            if (costs[i] != int.MaxValue && i != start && LiveUnitAt(i % Cols, i / Cols) is { } other && other != unit) costs[i] = int.MaxValue;

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
        return new MoveRange(costs, prev, red) { Width = Cols };
    }

    /// <summary>
    /// 이동 영역(파랑) 안에서만 밟아 (fromCol, fromRow) 에서 목표 칸까지 가는 가장 짧은 길(출발 칸 뺌). 못 가면 null.
    /// </summary>
    private List<(int Col, int Row)>? PathWithin(MoveRange range, int fromCol, int fromRow, int target)
    {
        int n = Cols * Rows, from = fromRow * Cols + fromCol;
        if (from == target) return [];
        var prev = new int[n];
        Array.Fill(prev, -2);
        prev[from] = -1;
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.TryDequeue(out int i))
        {
            int col = i % Cols, row = i / Cols;
            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
            {
                int nx = col + dx, ny = row + dy, j = ny * Cols + nx;
                if ((uint)nx >= Cols || (uint)ny >= Rows || prev[j] != -2 || !range.CanReach(j)) continue;
                prev[j] = i;
                if (j == target)
                {
                    var path = new List<(int Col, int Row)>();
                    for (int k = j; k != from; k = prev[k]) path.Add((k % Cols, k / Cols));
                    path.Reverse();
                    return path;
                }
                queue.Enqueue(j);
            }
        }
        return null;
    }

    /// <summary>
    /// 깔리는 물결의 반경 — 원본은 매 틀 <c>r += max(1, r×2/3)</c> 이라 0·1·2·3·5·8·13·21·35… 로 불어난다.
    /// 여덟 틀(0.27초)이면 웬만한 판은 다 덮는다.
    /// </summary>
    private static int WaveRadius(int frames)
    {
        int r = 0;
        for (int i = 0; i < frames && r < 64; i++) r += Math.Max(1, r * 2 / 3);
        return r;
    }

    private void DrawMoveRange()
    {
        if (_rangeUnit < 0 || _range is not { } range) return;
        int radius = WaveRadius((int)((_lastTime - _rangeStart) * TicksPerSecond));

        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                if (Math.Abs(col - _rangeCol) + Math.Abs(row - _rangeRow) > radius) continue;
                int i = row * Cols + col;
                uint tint = range.Cost[i] != int.MaxValue ? MoveTint : range.Red[i] ? RangeTint : 0;
                if (tint == 0) continue;
                int x = col * TileW, y = GridTop + row * TileH;
                AddRect(x, y, TileW, TileH, tint);
                // 테두리는 <b>같은 색을 불투명으로</b>, 칸보다 1픽셀 크게 — 이웃 칸과 선을 나눠 쓴다.
                StrokeRect(x, y, TileW + 1, TileH + 1, 0xFF000000 | tint);
            }
    }

    /// <summary>네모 안을 색을 <b>더해서</b> 밝힌다(255 에서 멈춘다).</summary>
    private void AddRect(int x, int y, int w, int h, uint tint)
    {
        uint tr = tint >> 16 & 0xFF, tg = tint >> 8 & 0xFF, tb = tint & 0xFF;
        for (int yy = y; yy < y + h; yy++)
        {
            if ((uint)yy >= BoardHeight) continue;
            for (int xx = x; xx < x + w; xx++)
            {
                if ((uint)xx >= BoardWidth) continue;
                int i = yy * BoardWidth + xx;
                uint c = _fb[i];
                _fb[i] = c & 0xFF000000
                       | Math.Min(255, (c >> 16 & 0xFF) + tr) << 16
                       | Math.Min(255, (c >> 8 & 0xFF) + tg) << 8
                       | Math.Min(255, (c & 0xFF) + tb);
            }
        }
    }
}
