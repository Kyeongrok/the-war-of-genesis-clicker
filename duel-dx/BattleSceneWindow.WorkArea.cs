using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// work 의 사거리 칸과 효과 범위 칸 — 모양 아홉 개(분석-전투 "어빌리티 범위·자세·상태이상").
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>거리 자 = 4×(|dx|+|dy|) — 한 칸이 4 다. 사거리는 최소·최대(4분의 1칸)로 거른다(기본공격 2/2 → 5~8 = 정확히 2칸).</item>
/// <item>모양: 1 마름모, 2 십자, 3 부채꼴(옆 ≤ 앞/3), 4 화면 전체, 5 직선, 6 폭 3 줄, 7 폭 5 줄, 8 대각선 X, 9 45° 삼각형.</item>
/// <item>방향을 쓰는 모양(3·5·6·7·9)은 <b>사거리를 그릴 때 네 방향을 다 합치고</b>, 효과 범위는 시전자 → 겨눈 칸 방향 하나만 쓴다.</item>
/// <item>대상 방식(사거리 <c>+0x13</c>, 효과 범위 <c>+0x1e</c>): 0·2 자기 자리, 1 적, 3·6 아무 칸, 4 아군, 5 아무 유닛, 7 빈 칸, 8 오브젝트.</item>
/// </list>
/// <para>
/// 높이(2026-09-19): 높이 반영 플래그가 서 있으면 거리에 <b>차등이면 올려치기 +3/층·내려치기 −2/층, 아니면 +2×|층차|</b> 를 더한다.
/// 칸을 켤지는 <b>최대 ≥ 차등까지 넣은 거리</b>이고 <b>최소 ≤ 차등 없이 잰 거리</b>일 때다. 「같은 높이만」 플래그가 서면 층이 다른 칸은 빠지고,
/// 「시야」 플래그가 서면 두 칸을 잇는 직선(브레젠험)을 훑어 중간 칸이 <c>높이+3</c> 선보다 높으면 막힌 것으로 본다.
/// 사거리 종류 2·4 는 <b>무기 사거리</b>(Itm 파일 16 ×4)를 최대로 쓴다. 오브젝트(대상 8)는 아직 없다.
/// </para>
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>두 칸 사이 거리(4분의 1칸) — 평지 기준.</summary>
    private static int CellDistance(int fromCol, int fromRow, int col, int row) =>
        4 * (Math.Abs(col - fromCol) + Math.Abs(row - fromRow));

    private int HeightAt(int col, int row) => _map is { } map ? map.HeightAt(col, row) : 0;

    /// <summary>높이까지 넣은 거리 — 차등을 넣은 값과 안 넣은 값 둘 다 돌려준다(최대는 앞, 최소는 뒤로 잰다).</summary>
    private (int Graded, int Plain) WorkDistance(int fromCol, int fromRow, int col, int row, bool useHeight, bool graded)
    {
        int d = CellDistance(fromCol, fromRow, col, row);
        if (!useHeight) return (d, d);
        int diff = HeightAt(col, row) - HeightAt(fromCol, fromRow);
        int plain = Math.Max(0, d + 2 * Math.Abs(diff));
        int gradedDistance = graded ? Math.Max(0, d + (diff > 0 ? 3 * diff : 2 * diff)) : plain;
        return (gradedDistance, plain);
    }

    /// <summary>시야 — 두 칸을 잇는 직선을 훑어 중간 칸이 그 선보다 높으면 막힌 것으로 본다(<c>0x100daaf0</c>).</summary>
    private bool HasSight(int fromCol, int fromRow, int col, int row)
    {
        if (_map is not { } map) return true;
        int h0 = map.HeightAt(fromCol, fromRow) + 3, h1 = map.HeightAt(col, row) + 3;
        int dx = Math.Abs(col - fromCol), dy = Math.Abs(row - fromRow);
        int steps = Math.Max(dx, dy);
        for (int i = 1; i < steps; i++)
        {
            int cx = fromCol + (col - fromCol) * i / steps, cy = fromRow + (row - fromRow) * i / steps;
            if (map.HeightAt(cx, cy) > h0 + (h1 - h0) * i / steps) return false;
        }
        return true;
    }

    /// <summary>그 work 의 사거리 최대(4분의 1칸) — 종류 2·4 는 낀 무기의 사거리를 쓴다.</summary>
    private int RangeMaxOf(WorkData w, UnitState? user)
    {
        int weapon = 0;
        if (w.RangeKind is 2 or 4 && user?.Data is { } c && c.Items.Length > 0
            && _db?.Items.GetValueOrDefault(c.Items[0]) is { } item) weapon = item.Range * 4;
        return w.RangeKind switch
        {
            2 => weapon,
            4 => weapon + w.RangeMaxQuarters,
            _ => w.RangeMaxQuarters,
        };
    }

    /// <summary>모양 <paramref name="shape"/> 가 그 칸을 덮나(거리는 이미 최소·최대로 걸렀다고 보고 모양만 본다).</summary>
    private static bool ShapeCovers(int shape, int dx, int dy, Facing facing)
    {
        // 바라보는 쪽을 앞(axis)으로, 그 직각을 옆(side)으로 돌려 놓는다.
        (int axis, int side) = facing switch
        {
            Facing.Up => (-dy, dx),
            Facing.Down => (dy, dx),
            Facing.Left => (-dx, dy),
            _ => (dx, dy),
        };
        int a = Math.Abs(side);
        return shape switch
        {
            1 => true,                                  // 마름모(거리만)
            2 => dx == 0 || dy == 0,                    // 십자
            3 => axis > 0 && a <= axis / 3,             // 부채꼴
            4 => true,                                  // 화면 전체
            5 => axis >= 0 && side == 0,                // 직선
            6 => axis >= 0 && a <= 1,                   // 폭 3 줄(시전자 줄도 덮는다 — work_area.py 그림)
            7 => axis >= 0 && a <= 2,                   // 폭 5 줄
            8 => Math.Abs(dx) == Math.Abs(dy),          // 대각선 X
            9 => axis > 0 && a <= axis,                 // 45° 삼각형
            _ => dx == 0 && dy == 0,
        };
    }

    /// <summary>방향을 쓰는 모양인가 — 사거리를 그릴 때는 네 방향을 합쳐야 한다.</summary>
    private static bool ShapeUsesFacing(int shape) => shape is 3 or 5 or 6 or 7 or 9;

    /// <summary>
    /// (fromCol, fromRow) 에서 work 로 (col, row) 칸을 겨눌 수 있나 — 사거리 모양·최소·최대와 지형 &amp; 0x8 만 본다.
    /// </summary>
    private bool InWorkRange(WorkData w, int fromCol, int fromRow, int col, int row, UnitState? user = null)
    {
        if ((uint)col >= Cols || (uint)row >= Rows) return false;
        if (_map is { } map && (map.FlagsAt(col, row) & 0x8) != 0) return false;

        int dx = col - fromCol, dy = row - fromRow;
        if (w.SelfCentred || w.RangeShape == 0) return dx == 0 && dy == 0;

        if (w.SameHeightRange != 0 && HeightAt(col, row) != HeightAt(fromCol, fromRow)) return false;
        if (w.Sight != 0 && !HasSight(fromCol, fromRow, col, row)) return false;

        var (graded, plain) = WorkDistance(fromCol, fromRow, col, row, w.HeightRange != 0, w.HeightGraded != 0);
        if (plain < w.RangeMinQuarters || graded > RangeMaxOf(w, user)) return false;
        if (!ShapeUsesFacing(w.RangeShape)) return ShapeCovers(w.RangeShape, dx, dy, Facing.Right);

        // 사거리 그림은 네 방향을 다 그려 합친다.
        return ShapeCovers(w.RangeShape, dx, dy, Facing.Up) || ShapeCovers(w.RangeShape, dx, dy, Facing.Down)
            || ShapeCovers(w.RangeShape, dx, dy, Facing.Left) || ShapeCovers(w.RangeShape, dx, dy, Facing.Right);
    }

    /// <summary>효과 범위 칸들 — 겨눈 칸(자기 자리에 쓰는 work 면 시전자 칸)을 가운데로, 시전자 → 겨눈 칸 방향으로.</summary>
    private List<(int Col, int Row)> AreaCells(WorkData w, UnitState user, int col, int row)
    {
        if (w.SelfCentred) (col, row) = (user.Col, user.Row);
        var cells = new List<(int, int)>();
        if (w.AreaShape == 0) { cells.Add((col, row)); return cells; }

        var facing = col == user.Col && row == user.Row ? user.Facing : FacingToward(user.Col, user.Row, col, row);
        int reach = Math.Max(1, w.AreaMaxQuarters / 4);
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int cx = col + dx, cy = row + dy;
                if ((uint)cx >= Cols || (uint)cy >= Rows) continue;
                if (_map is { } map && (map.FlagsAt(cx, cy) & 0x8) != 0) continue;
                if (w.SameHeightArea != 0 && HeightAt(cx, cy) != HeightAt(col, row)) continue;
                var (graded, plain) = WorkDistance(col, row, cx, cy, w.HeightArea != 0, graded: false);
                if (plain < w.AreaMinQuarters || graded > w.AreaMaxQuarters) continue;
                if (dx != 0 || dy != 0 ? !ShapeCovers(w.AreaShape, dx, dy, facing) : false) continue;
                cells.Add((cx, cy));
            }
        return cells;
    }

    /// <summary>그 칸의 인물이 이 대상 방식에 맞나 — 1 적, 4 아군, 5 아무 유닛, 3·6 아무 칸(유닛이면 맞음).</summary>
    private static bool ModeAccepts(int mode, UnitState user, UnitState target) => mode switch
    {
        1 => target.IsAlly != user.IsAlly,
        4 => target.IsAlly == user.IsAlly,
        5 or 3 or 6 => true,
        0 or 2 => target == user,
        _ => false,
    };

    /// <summary>겨눈 칸에서 실제로 맞는 인물들 — 효과 범위 칸 안에서 효과 대상 방식(<c>+0x1e</c>)으로 거른다.</summary>
    private List<int> WorkTargets(WorkData w, UnitState user, int col, int row)
    {
        var cells = AreaCells(w, user, col, row).ToHashSet();
        int mode = w.AreaMode != 0 ? w.AreaMode : w.TargetMode;
        return [.. Enumerable.Range(0, _units.Length)
            .Where(i => _units[i].Alive && cells.Contains((_units[i].Col, _units[i].Row)) && ModeAccepts(mode, user, _units[i]))];
    }

    /// <summary>겨눌 수 있는 칸인가 — 대상 방식이 유닛을 고르는 것이면 그 칸에 맞는 유닛이 있어야 한다.</summary>
    private bool CanAimAt(WorkData w, UnitState user, int col, int row)
    {
        if (!InWorkRange(w, user.Col, user.Row, col, row, user)) return false;
        if (w.TargetMode is 3 or 6 or 0 or 2) return true;
        if (w.TargetMode == 7) return LiveUnitAt(col, row) == null && _map is { } m && (m.FlagsAt(col, row) & 0x9) == 0;
        return LiveUnitAt(col, row) is { } t && ModeAccepts(w.TargetMode, user, t);
    }
}
