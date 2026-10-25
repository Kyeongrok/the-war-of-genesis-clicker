namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 전장 범위 — 전투 행동 103 [x1 | y1 &lt;&lt; 8, x2 | y2 &lt;&lt; 8](<c>0x1001deb0</c>)이 정하는 맵 안의 사각형.
/// </summary>
/// <remarks>
/// <para>원본: 맵 <c>[0x10192488]</c> 의 <c>+0x1f4 · +0x1f6 · +0x1f8 · +0x1fa</c>(왼 · 위 · 오른 · 아래 칸, 오른 · 아래는 끝 칸 + 1)에
/// 바이트 넷을 그대로 적고 0 과 맵 칸 수(<c>+0x60 · +0x62</c>)로 자른다. 맵을 만들 때(<c>0x100895c0</c>)는 (0, 0)-(칸 수)라 행동 103 이 없는 판은 맵 전체다.
/// 그와 함께 픽셀 끝 <c>+0x1fc · +0x1fe · +0x200 · +0x202</c> 를 다시 잰다 — 가로 = 칸 × 40 − 띠 원점, 세로 = 칸 × 32 − 띠 원점 × 8 에서
/// 위는 첫 줄 · 아래는 끝 줄(아래 − 1)의 가장 높은 칸 × 12 만큼 올리고, 맵 그림 크기(<c>+0x5c · +0x5e</c>)로 자른다.</para>
/// <para>쓰는 곳(모두 [C]): 카메라 스크롤(<c>0x10031c86</c> — x 를 [+0x1fc, +0x200 − 화면 폭], y 를 [+0x1fe, +0x202 − 화면 높이] 로, 아래 · 오른 끝을 먼저 자른다) ·
/// 길 찾기 · 설 칸(<c>0x100267a8</c> · <c>0x10029132</c> · <c>0x10089bf0</c> 둘레 — 범위 밖 칸은 판 밖과 같다) · 칸 표(<c>0x10037100</c> — 범위 안 칸만 유닛을 적는다) ·
/// 증원 300 의 가장자리(<c>0x1001ec6f~</c> — 범위 변 밖 140px 에서 걸어 들어와 범위 안 · 설 수 있는 칸에 닿으면 걷기) · 탈것 내리기의 화살 끝(<c>0x10073440</c>).</para>
/// <para>자료: 전투 0193 [0, 13099] = (0, 0)-(43, 51) — 맵 44 × 52 의 끝 열 · 끝 줄을 뺀다, 0261 [1026, 7983] = (2, 4)-(47, 31) — 맵 49 × 36 의 둘레를 줄인다.
/// 둘 다 조건 1(전투 시작) 사건이라 판 전체를 그 범위로 쓰는 셈이다.</para>
/// </remarks>
internal sealed class Part1BattleRange(GameWindow host)
{
    /// <summary>행동 103 이 범위를 정했나 — 아니면 맵 전체(원본 기본값 0x100895c0)라 아무것도 막지 않는다.</summary>
    internal bool Active { get; private set; }

    /// <summary>칸 범위 — 오른 · 아래는 끝 칸 + 1(원본 +0x1f8 · +0x1fa 와 같이 「미만」으로 견준다).</summary>
    internal int Left, Top, Right, Bottom;

    /// <summary>판을 세울 때 · RESTART — 맵 전체로.</summary>
    internal void Reset() => Active = false;

    /// <summary>행동 103 — 바이트 사각형을 맵 칸 수로 자른다(0x1001deb0, 0 아래는 바이트라 없다).</summary>
    internal void Set(int packed1, int packed2)
    {
        Left = packed1 & 0xFF;
        Top = (packed1 >> 8) & 0xFF;
        Right = Math.Min(packed2 & 0xFF, host.Cols);
        Bottom = Math.Min((packed2 >> 8) & 0xFF, host.Rows);
        Active = true;
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 battle range ({Left},{Top})-({Right},{Bottom}) map {host.Cols}x{host.Rows} camera x [{MinX},{MaxRightX}] y [{MinY},{MaxBottomY}]" + Environment.NewLine);
    }

    /// <summary>세이브 — 정했으면 (왼, 위, 오른, 아래), 아니면 null.</summary>
    internal int[]? Save() => Active ? [Left, Top, Right, Bottom] : null;

    internal void Restore(int[]? saved)
    {
        if (saved is [var l, var t, var r, var b, ..]) { Left = l; Top = t; Right = r; Bottom = b; Active = true; }
        else Active = false;
    }

    /// <summary>그 칸이 전장 안인가 — 범위를 안 정했으면 늘 참(판 밖 검사는 부르는 쪽이 따로 한다).</summary>
    internal bool Contains(int col, int row) => !Active || (col >= Left && col < Right && row >= Top && row < Bottom);

    /// <summary>카메라 왼끝(판 픽셀) — 원본 +0x1fc.</summary>
    internal int MinX => Active ? Left * TileW : 0;

    /// <summary>카메라 오른끝(판 픽셀, 화면 오른쪽이 닿는 곳) — 원본 +0x200.</summary>
    internal int MaxRightX => Active ? Right * TileW : int.MaxValue;

    /// <summary>카메라 윗끝 — 첫 줄의 가장 높은 칸 윗줄(원본 +0x1fe: 줄 × 32 − 가장 높은 칸 × 12).</summary>
    internal int MinY => Active ? RowTop(Top) : 0;

    /// <summary>카메라 아랫끝(화면 아래가 닿는 곳) — 끝 줄의 가장 높은 칸 아랫줄(원본 +0x202).</summary>
    internal int MaxBottomY => Active ? RowTop(Bottom - 1) + TileH : int.MaxValue;

    private int RowTop(int row)
    {
        row = Math.Clamp(row, 0, Math.Max(0, host.Rows - 1));
        int top = int.MaxValue;
        for (int col = Left; col < Math.Max(Left + 1, Right); col++) top = Math.Min(top, host.CellTop(Math.Clamp(col, 0, host.Cols - 1), row));
        return top;
    }

    /// <summary>카메라 x 를 자른다 — 원본처럼 오른끝을 먼저, 그다음 왼끝(범위가 화면보다 좁으면 왼끝이 이긴다).</summary>
    internal double ClampX(double x, int camMaxX) => Math.Max(Math.Max(0, MinX), Math.Min(x, Math.Min(camMaxX, (double)MaxRightX - host.ViewWidth)));

    /// <summary>카메라 y 를 자른다 — <see cref="ClampX"/> 와 같은 차례.</summary>
    internal double ClampY(double y, int camMax) => Math.Max(Math.Max(0, MinY), Math.Min(y, Math.Min(camMax, (double)MaxBottomY - host.ViewHeight)));
}
