namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 판에서 앞(아래) 칸 윗면이 뒤 칸을 덮을 때 무엇을 고르나 — 원본 고르기(<see cref="GameWindow"/> 의 Part1RowAt, 뒤 줄이 이김)에
/// 「걸어서 닿는 앞 칸은 앞이 이김」을 얹는다(사용자 결정 fg-82).
/// </summary>
/// <remarks>
/// <para>전투 0025(Obt 36): 아래쪽 성벽 윗길(줄 32~35, 높이 14)은 계단(31~33, 24~26)으로 올라가는 길이고 적 궁수도 서 있는데, 그 윗면이
/// 뒤 바닥(줄 29~31, 높이 6)과 화면에서 정확히 겹친다(32·32 = 29·32 + 8·12). 뒤 줄이 이기면 윗길 그림을 눌러도 가려진 바닥이 잡혀
/// 그 바닥의 파란 칸이 윗길 위에 그려지고(「벽 위로 간다」), 윗길 위 상자(4,32)는 누를 자리가 없었다(사용자 제보).</para>
/// <para>btl 20229(Obt 115)의 높이 10 벽 윗면은 바닥과 이어지지 않은 섬이라 걸어서 닿는 칸이 아니다 — 거기서는 여전히 뒤 복도가 이긴다.</para>
/// <para>걸어서 닿는 칸 = 유닛이 선 칸 · 배치 칸에서 걷기 금지(&amp;9) 없이 높이차 2 이하로 이어진 칸. 그 칸 옆의 물체 칸(상자 · 문)도 앞에서 이긴다.
/// 앞 칸에 통째로 덮여 어느 점도 그 칸을 고르지 않는 뒤 칸은 이동 칸 칠하기에서 뺀다.</para>
/// </remarks>
internal sealed class Part1Occlusion(GameWindow host)
{
    private (object? Map, int Key, int Cols, int Rows) _for;
    private bool[] _reach = [], _hidden = [];

    /// <summary>원본 고르기가 준 줄 <paramref name="back"/> 앞에, 그 점을 덮는 걸어서 닿는 칸이 있으면 그 줄(가장 앞).</summary>
    internal int Pick(int col, int by, int back)
    {
        Ensure();
        for (int row = host.Rows - 1; row > back; row--)
        {
            int top = host.CellTop(col, row);
            if (by >= top && by < top + TileH && Wins(col, row)) return row;
        }
        return back;
    }

    /// <summary>앞 칸에 통째로 덮여 고를 수 없는 칸인가 — 이동 칸을 칠하지 않는다.</summary>
    internal bool Hidden(int col, int row)
    {
        Ensure();
        int i = row * _for.Cols + col;
        return (uint)col < (uint)_for.Cols && (uint)row < (uint)_for.Rows && _hidden[i];
    }

    private bool Wins(int col, int row)
    {
        if ((uint)col >= (uint)_for.Cols || (uint)row >= (uint)_for.Rows) return false;
        if (_reach[row * _for.Cols + col]) return true;
        if (host.Btl.ObjectAt(col, row) is null) return false;
        foreach (var (dx, dy) in Steps)
        {
            int c = col + dx, r = row + dy;
            if ((uint)c < (uint)_for.Cols && (uint)r < (uint)_for.Rows && _reach[r * _for.Cols + c]) return true;
        }
        return false;
    }

    private static readonly (int Dx, int Dy)[] Steps = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    private void Ensure()
    {
        var btl = host.Btl;
        btl.EnsureStamp();
        var key = ((object?)btl._stampMap, btl._stampKey, host.Cols, host.Rows);
        if (key == _for && _reach.Length == host.Cols * host.Rows) return;
        _for = key;
        int cols = host.Cols, rows = host.Rows, n = cols * rows;
        _reach = new bool[n];
        _hidden = new bool[n];
        var queue = new Queue<int>();
        void Seed(int c, int r)
        {
            if ((uint)c < (uint)cols && (uint)r < (uint)rows && !_reach[r * cols + c] && (btl.CellFlagsAt(c, r) & 0x9) == 0) { _reach[r * cols + c] = true; queue.Enqueue(r * cols + c); }
        }
        foreach (var u in host._units) if (u.Alive && u.OnField) Seed(u.Col, u.Row);
        foreach (var p in host._scene.Placement ?? []) Seed(p.Col, p.Row);
        while (queue.TryDequeue(out int i))
        {
            int c0 = i % cols, r0 = i / cols, h0 = btl.WalkHeightAt(c0, r0);
            foreach (var (dx, dy) in Steps)
            {
                int c = c0 + dx, r = r0 + dy;
                if ((uint)c >= (uint)cols || (uint)r >= (uint)rows || _reach[r * cols + c]) continue;
                if ((btl.CellFlagsAt(c, r) & 0x9) != 0 || Math.Abs(btl.WalkHeightAt(c, r) - h0) > 2) continue;
                _reach[r * cols + c] = true;
                queue.Enqueue(r * cols + c);
            }
        }
        // 덮인 칸 — 열마다 판의 모든 y 를 골라 보고, 한 번도 안 잡힌 칸.
        int hiddenCount = 0;
        for (int c = 0; c < cols; c++)
        {
            var seen = new bool[rows];
            int yTop = host.CellTop(c, 0), yBottom = host.CellTop(c, rows - 1) + TileH;
            for (int r = 0; r < rows; r++) { yTop = Math.Min(yTop, host.CellTop(c, r)); yBottom = Math.Max(yBottom, host.CellTop(c, r) + TileH); }
            for (int y = yTop; y < yBottom; y++)
                if (host.RowAt(c * TileW, y) is >= 0 and var r && r < rows) seen[r] = true;
            for (int r = 0; r < rows; r++)
                if (!seen[r]) { _hidden[r * cols + c] = true; hiddenCount++; }
        }
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 occlusion reach {_reach.Count(b => b)} hidden {hiddenCount} (닿는 칸 {Enumerable.Range(0, n).Count(k => _hidden[k] && _reach[k])})" + Environment.NewLine);
    }
}
