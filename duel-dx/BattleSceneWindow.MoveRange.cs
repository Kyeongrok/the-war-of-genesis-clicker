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

    /// <summary>층 색 원값 — 이동 층 2 (100,100,255) · 사거리 층 12 (255,100,40) · 효과 범위 층 0 (255,170,40). 화면에는 × 칸밝기/256 을 더한다.</summary>
    private const uint MoveLayer = 0x6464FF, RangeLayer = 0xFF6428, SplashLayer = 0xFFAA28;
    /// <summary>닿을 수 있는 오브젝트 칸(층 1) — 노랑 (255,255,20). 겹치면 이동 칸보다 먼저 칠한다(분석-UI 「칸 깃발」).</summary>
    private const uint ObjectLayer = 0xFFFF14;
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
        // 예산은 <b>지금 고른 work</b> 의 TP 를 남긴다(상태 12 는 어빌리티, 상태 10 은 기본공격) —
        // 늘 기본공격으로 셈하면 비싼 어빌리티를 고른 채 너무 멀리 걸을 수 있다. TP 비용에는 상태 20(소모량 %)도 먹는다.
        int workId = _targetWork > 0 ? _targetWork : c.BasicWorkId;
        int budget = unit.HasStatus(25) ? 0 : unit.Tp + Math.Min(0, c.Ctp - TpCostFor(unit, c, workId));

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
                // 적인지는 <b>버서커(4)까지 보는</b> 편 판정으로 가른다(0x1006fde0) — 그게 걸리면 모두가 적이다.
                if (SeesAsFoe(unit, e) && Math.Abs(H(px, py) - H(col, row)) < 1) return false;
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

        // 문·스위치문이 선 칸에는 <b>설 수</b> 없다 — 길은 안 막지만 목표 칸이 못 된다(0x100746b0).
        for (int i = 0; i < n; i++)
            if (costs[i] != int.MaxValue && ObjectBlocks(i % Cols, i / Cols)) costs[i] = int.MaxValue;

        // 다른 인물이 선 칸은 파랑에서 뺀다.
        for (int i = 0; i < n; i++)
            if (costs[i] != int.MaxValue && i != start && LiveUnitAt(i % Cols, i / Cols) is { } other && other != unit) costs[i] = int.MaxValue;

        // 붉은 칸 — 갈 수 있는 칸마다 <b>그 인물의 기본공격 모양</b>을 칠한다(0x100749b0).
        // 예전에는 「정확히 두 칸 상하좌우」로 박아 두어 높이·시야가 빠졌다 — 한 층만 달라도 사거리가 달라진다.
        if (Work(c.BasicWorkId) is { } basic)
        {
            int reach = Math.Max(1, RangeMaxOf(basic, unit) / 4);
            for (int i = 0; i < n; i++)
            {
                if (costs[i] == int.MaxValue) continue;
                int col = i % Cols, row = i / Cols;
                for (int ay = row - reach; ay <= row + reach; ay++)
                    for (int ax = col - reach; ax <= col + reach; ax++)
                    {
                        if (!InBounds(ax, ay)) continue;
                        int j = ay * Cols + ax;
                        if (costs[j] != int.MaxValue || red[j]) continue;
                        if (InWorkRange(basic, col, row, ax, ay, unit)) red[j] = true;
                    }
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
        // 어빌리티 대상을 고르는 중(원본 상태 11)에는 이동 영역(파랑)·공격 사거리(빨강)를 걷고 그 기술의 사거리·효과 범위만 깐다 —
        // 겹쳐 칠하면 사거리 칸이 묻혀 안 보였다(사용자 보고). 기본공격 겨냥은 예전처럼 둔다.
        if (_targetWork >= 0 && !_targetIsBasicAttack) return;
        int radius = WaveRadius((int)((_lastTime - _rangeStart) * TicksPerSecond));
        // 차례인 인물의 영역이면 걸어가 손댈 수 있는 물체 칸도 칠한다(원본 상태 10 이 층 1 을 함께 만든다, 0x1006961c).
        var touchable = new HashSet<int>();
        if (_rangeUnit == _turn && IsPlayerTurn)
            foreach (var obj in Objects)
                if (ObjectAt(obj.Col, obj.Row) == obj && FindTouchPath(_units[_turn], obj, range) != null) touchable.Add(obj.Row * Cols + obj.Col);

        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                if (Math.Abs(col - _rangeCol) + Math.Abs(row - _rangeRow) > radius) continue;
                int i = row * Cols + col;
                // 대상을 딱히 고르는 중이 아니어도 사거리 빨강은 늘 파랑과 함께 뜬다 — 사용자가 원본에서
                // 직접 본 그대로다("aiming일 때만 빨강"으로 좁혔던 이전 판단은 상태 번호를 오독한 것으로 보인다:
                // 같은 파일 안에서도 상태 12를 "어빌리티"(97줄 언저리)와 "그냥 걷기"(여기)로 서로 다르게 적어 놨었다).
                uint layer = touchable.Contains(i) ? ObjectLayer : range.Cost[i] != int.MaxValue ? MoveLayer : range.Red[i] ? RangeLayer : 0;
                if (layer == 0) continue;
                PaintCell(col, row, layer);
            }
    }

    /// <summary>
    /// 칸 밝기 10~90 — 원본 <c>0x10030cc0</c> 이 Obt 를 읽은 뒤 칸마다 넣는 값. 평지는 74.
    /// 비탈은 북서 모서리에서 북동·남서로의 기울기 벡터 (−40·Δ동, −40·Δ남, 1600) 로 빛을 셈한다:
    /// <c>((1920·b + 4096000) / 40) / |(a, b, 1600)|</c> 를 80 에서 자르고 10 을 더한다(a = −40·(북동−북서), b = −40·(남서−북서), 16비트로 접힘).
    /// </summary>
    private int CellBrightness(int col, int row)
    {
        if (!BoardIsMap || _map!.SlopeAt(col, row) is 0 or > 3) return 74;
        int nw = _map.CornerAt(col, row, 1), ne = _map.CornerAt(col, row, 2), sw = _map.CornerAt(col, row, 3);
        int a = (short)(-40 * (ne - nw)), b = (short)(-40 * (sw - nw));
        int len = (int)Math.Sqrt((double)a * a + (double)b * b + 2560000.0);
        int v = (15 * b * 128 + 4096000) / 40 / Math.Max(1, len);
        return Math.Max(10, Math.Min(80, v) + 10);
    }

    /// <summary>모서리 높이(원 단위) → 화면 픽셀: 원본은 <c>높이 × 12 / 20</c>(0x100d7bf3, 0 쪽으로 자름).</summary>
    private static int CornerPx(int raw) => raw * 12 / 20;

    /// <summary>
    /// 칸 하나를 층 색으로 칠한다 — 원본처럼 ① 층색 × 칸밝기/256 을 <b>더해서</b> 채우고 ② 같은 색 불투명으로 테두리(41×33, 이웃과 선을 나눠 쓴다).
    /// 평지는 40×32 네모. 비탈 칸은 네 모서리 높이(북서·북동·남서·남동)로 꼭짓점을 올린 사각형이다 —
    /// 원본은 비탈 모양 3 을 「윗변 북서·아랫변 남서 높이의 네모」로, 1·2 는 선·삼각형 조각으로 그리는데(0x100d7ba3·0x100d7dcf·0x100d820e)
    /// 조각 표까지는 못 옮겨 네 꼭짓점 사각형으로 근사한다(가설).
    /// </summary>
    private void PaintCell(int col, int row, uint layer)
    {
        int bright = CellBrightness(col, row);
        uint tint = (layer >> 16 & 0xFF) * (uint)bright / 256 << 16 | (layer >> 8 & 0xFF) * (uint)bright / 256 << 8 | (layer & 0xFF) * (uint)bright / 256;
        int x0 = col * TileW, x1 = x0 + TileW;
        int baseTop = GridTop + BoardPad + row * TileH, baseBottom = baseTop + TileH;
        if (!BoardIsMap || _map!.SlopeAt(col, row) == 0)
        {
            int y = CellTop(col, row);
            AddRect(x0, y, TileW, TileH, tint);
            StrokeRect(x0, y, TileW + 1, TileH + 1, 0xFF000000 | tint);
            return;
        }
        int yNw = baseTop - CornerPx(_map.CornerAt(col, row, 1)), yNe = baseTop - CornerPx(_map.CornerAt(col, row, 2));
        int ySw = baseBottom - CornerPx(_map.CornerAt(col, row, 3)), ySe = baseBottom - CornerPx(_map.CornerAt(col, row, 0));
        AddQuad(x0, x1, yNw, yNe, ySw, ySe, tint);
        uint line = 0xFF000000 | tint;
        DrawSegment(x0, yNw, x1, yNe, line);
        DrawSegment(x0, ySw, x1, ySe, line);
        DrawSegment(x0, yNw, x0, ySw, line);
        DrawSegment(x1, yNe, x1, ySe, line);
    }

    /// <summary>윗변(yNw→yNe)과 아랫변(ySw→ySe)이 기운 사각형 안을 색을 더해 밝힌다 — 세로줄마다 두 변 사이를 채운다.</summary>
    private void AddQuad(int x0, int x1, int yNw, int yNe, int ySw, int ySe, uint tint)
    {
        int w = Math.Max(1, x1 - x0);
        for (int x = x0; x < x1; x++)
        {
            double t = (x - x0) / (double)w;
            int top = (int)Math.Round(yNw + (yNe - yNw) * t), bottom = (int)Math.Round(ySw + (ySe - ySw) * t);
            if (bottom > top) AddRect(x, top, 1, bottom - top, tint);
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
