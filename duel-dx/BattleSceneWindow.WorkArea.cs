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
/// 「시야」 플래그가 서면 대상에서 시전자로 직선을 훑어 칸 높이가 <c>높이+3</c> 선 이상이면 막힌 것으로 본다(<see cref="HasSight"/>).
/// 플래그 &amp;8 은 물체까지 찍은 판(<see cref="CellFlagsAt"/>)으로 본다.
/// 사거리 종류 2·4 는 <b>무기 사거리</b>(Itm 파일 16 ×4)를 최대로 쓴다. 오브젝트(대상 8)는 아직 없다.
/// </para>
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>두 칸 사이 거리(4분의 1칸) — 평지 기준.</summary>
    private static int CellDistance(int fromCol, int fromRow, int col, int row) =>
        4 * (Math.Abs(col - fromCol) + Math.Abs(row - fromRow));

    /// <summary>
    /// 거리 자·같은 높이·모드 7·화면 창이 쓰는 칸 높이 — 원본 <c>+0x80</c>(지형 + 종류 0·3·4·5 물체 도장, <c>0x100daca0</c>·<c>0x100db787</c>, 감사3 R9).
    /// 걷기·ZOC·시야는 모든 물체를 찍은 <see cref="WalkHeightAt"/>(<c>+0x78</c>)를 쓴다.
    /// </summary>
    private int HeightAt(int col, int row) => AimHeightAt(col, row);

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

    /// <summary>
    /// 시야 — 원본 <c>0x100daaf0(x0,y0,h0, x1,y1,h1)</c> 그대로(감사3 R2). 모양 함수가 (대상 칸, 대상 높이+3, 시전자 칸, 시전자 높이+3)으로
    /// 부르므로(<c>0x100db95f~0x100db987</c>) 선은 <b>대상 → 시전자</b>로 긋는다. 큰 축으로 한 칸씩 가며 작은 축은 오차를 쌓아 넘기고,
    /// 선 높이는 16.16 고정소수로 더해 가다 산술 시프트(내림)로 읽는다. <b>칸 높이 ≥ 선 높이</b>면 막힌다 — 끝 칸(시전자 칸)까지 본다.
    /// 높이는 모든 물체를 찍은 <c>+0x78</c>(<see cref="WalkHeightAt"/>) — 석상·크리스탈·닫힌 문이 시야를 막는다.
    /// 전에는 시전자 → 대상, 0 쪽 자름 보간, <c>&gt;</c> 부등호, 지형 높이만 써서 무작위 판의 31% 가 원본과 달랐다.
    /// </summary>
    private bool HasSight(int fromCol, int fromRow, int col, int row)
    {
        if (_map is null) return true;
        int x0 = col, y0 = row, x1 = fromCol, y1 = fromRow;          // 대상 → 시전자
        int adx = Math.Abs(x1 - x0), ady = Math.Abs(y1 - y0);
        if (adx == 0 && ady == 0) return true;
        int h = (WalkHeightAt(x0, y0) + 3) << 16;
        int num = ((WalkHeightAt(x1, y1) + 3) << 16) - h;
        int sx = x0 >= x1 ? -1 : 1, sy = y0 >= y1 ? -1 : 1, err = 0, x = x0, y = y0;
        if (adx > ady)
        {
            int slope = num / adx;                                   // idiv — 0 쪽으로 자른다
            do
            {
                x += sx; err += ady; h += slope;
                if (err >= adx) { err -= adx; y += sy; }
                if (WalkHeightAt(x, y) >= h >> 16) return false;
            } while (x != x1);
        }
        else
        {
            int slope = num / ady;
            do
            {
                y += sy; err += adx; h += slope;
                if (err >= ady) { err -= ady; x += sx; }
                if (WalkHeightAt(x, y) >= h >> 16) return false;
            } while (y != y1);
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
            3 => axis >= 0 && a <= axis / 3,            // 부채꼴 — 원점 줄도 덮는다
            4 => true,                                  // 화면 전체
            5 => axis >= 0 && side == 0,                // 직선
            6 => axis >= 0 && a <= 1,                   // 폭 3 줄(시전자 줄도 덮는다 — work_area.py 그림)
            7 => axis >= 0 && a <= 2,                   // 폭 5 줄
            8 => Math.Abs(dx) == Math.Abs(dy),          // 대각선 X
            9 => axis >= 1 && a <= axis - 1,            // 45° 삼각형 — 앞으로 한 칸 간 뒤부터 벌어진다
            // 10 = ^ 호(데모 추가, 원본 표는 9 까지) — 겨눈 칸 기준 한 칸 더 앞과 양옆. 파(gm-skil-14)가 옆 칸을 겨누면 시전자 기준
            // 12시(두 칸 앞)·11시·1시(대각선)가 된다. 겨눈 칸 자체는 안 든다.
            10 => axis == 1 && side == 0 || axis == 0 && a == 1,
            _ => dx == 0 && dy == 0,
        };
    }

    /// <summary>방향을 쓰는 모양인가 — 사거리를 그릴 때는 네 방향을 합쳐야 한다.</summary>
    private static bool ShapeUsesFacing(int shape) => shape is 3 or 5 or 6 or 7 or 9 or 10;

    /// <summary>
    /// 그 모양이 <b>앞으로 간 거리만</b> 재는가 — 3·5·6·7·9 가 그렇다(<c>0x100dafe0</c>·<c>0x100db310</c>).
    /// 옆으로 벌어진 만큼은 거리에 안 들어가므로, 맨해튼으로 자르면 바깥 줄이 통째로 잘려 나간다.
    /// </summary>
    private static bool ShapeUsesAxisDistance(int shape) => shape is 3 or 5 or 6 or 7 or 9;

    /// <summary>바라보는 쪽으로 몇 칸 갔나(뒤쪽은 음수).</summary>
    private static int AxisOf(int dx, int dy, Facing facing) => facing switch
    {
        Facing.Up => -dy,
        Facing.Down => dy,
        Facing.Left => -dx,
        _ => dx,
    };

    /// <summary>모양과 거리를 함께 본다 — 모양마다 <b>거리 자가 다르기</b> 때문에 따로 볼 수 없다.</summary>
    private static bool ShapeReaches(int shape, int dx, int dy, Facing facing,
                                     int minQuarters, int maxQuarters, int graded, int plain)
    {
        if (!ShapeCovers(shape, dx, dy, facing)) return false;
        if (!ShapeUsesAxisDistance(shape)) return plain >= minQuarters && graded <= maxQuarters;
        // 축 거리에도 <b>높이항은 그대로 붙는다</b>(0x100db2bf~) — 맨해튼 자와 같은 식이다.
        // 맨해튼 몫만 축 거리로 바꾸고, 높이 몫은 이미 잰 값에서 가져온다.
        int axis = 4 * AxisOf(dx, dy, facing);
        int flat = 4 * (Math.Abs(dx) + Math.Abs(dy));
        return axis + (plain - flat) >= minQuarters && axis + (graded - flat) <= maxQuarters;
    }

    /// <summary>
    /// (fromCol, fromRow) 에서 work 로 (col, row) 칸을 겨눌 수 있나 — 사거리 모양·최소·최대와 지형 &amp; 0x8 만 본다.
    /// </summary>
    private bool InWorkRange(WorkData w, int fromCol, int fromRow, int col, int row, UnitState? user = null)
    {
        if ((uint)col >= Cols || (uint)row >= Rows) return false;
        if (_map is not null && (CellFlagsAt(col, row) & 0x8) != 0) return false;

        int dx = col - fromCol, dy = row - fromRow;
        if (w.SelfCentred || w.RangeShape == 0) return dx == 0 && dy == 0;

        if (w.SameHeightRange != 0 && HeightAt(col, row) != HeightAt(fromCol, fromRow)) return false;
        // 모양 1 의 보조(종류 2) work 은 물체가 선 칸을 못 겨눈다(0x100db640).
        if (w.RangeShape == 1 && w.Kind == 2 && ObjectAt(col, row) != null) return false;
        if (w.Sight != 0 && !HasSight(fromCol, fromRow, col, row)) return false;

        var (graded, plain) = WorkDistance(fromCol, fromRow, col, row, w.HeightRange != 0, w.HeightGraded != 0);
        int min = w.RangeMinQuarters, max = RangeMaxOf(w, user);
        if (!ShapeUsesFacing(w.RangeShape))
            return ShapeReaches(w.RangeShape, dx, dy, Facing.Right, min, max, graded, plain);

        // 사거리 그림은 네 방향을 다 그려 합친다.
        foreach (var way in new[] { Facing.Up, Facing.Down, Facing.Left, Facing.Right })
            if (ShapeReaches(w.RangeShape, dx, dy, way, min, max, graded, plain)) return true;
        return false;
    }

    /// <summary>효과 범위 칸들 — 겨눈 칸(자기 자리에 쓰는 work 면 시전자 칸)을 가운데로, 시전자 → 겨눈 칸 방향으로.</summary>
    private List<(int Col, int Row)> AreaCells(WorkData w, UnitState user, int col, int row)
    {
        if (w.SelfCentred) (col, row) = (user.Col, user.Row);
        var cells = new List<(int, int)>();
        if (w.AreaShape == 0) { cells.Add((col, row)); return cells; }

        var facing = col == user.Col && row == user.Row ? user.Facing : FacingToward(user.Col, user.Row, col, row);
        // 옆으로 벌어지는 폭(모양 6·7 의 ±1·±2)은 크기와 상관없다 — 창을 그만큼 넓게 잡는다.
        // 모양 4(화면 전체)는 최대를 아예 안 보므로 판 전체를 훑는다(0x100de010).
        int reach = w.AreaShape == 4 ? Math.Max(Cols, Rows) : Math.Max(1, w.AreaMaxQuarters / 4) + 2;
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int cx = col + dx, cy = row + dy;
                if ((uint)cx >= Cols || (uint)cy >= Rows) continue;
                if (_map is not null && (CellFlagsAt(cx, cy) & 0x8) != 0) continue;
                if (w.SameHeightArea != 0 && HeightAt(cx, cy) != HeightAt(col, row)) continue;
                var (graded, plain) = WorkDistance(col, row, cx, cy, w.HeightArea != 0, graded: false);
                if (w.AreaShape == 4)
                {
                    // 화면 전체 — 화면에 보이는 만큼만이다(가로 8칸, 세로는 층까지 넣어 240점).
                    if (plain < w.AreaMinQuarters) continue;
                    if (Math.Abs(dx) > 8) continue;
                    if (Math.Abs(32 * dy - 12 * (HeightAt(cx, cy) - HeightAt(col, row))) > 240) continue;
                }
                else if (dx != 0 || dy != 0)
                {
                    if (!ShapeReaches(w.AreaShape, dx, dy, facing, w.AreaMinQuarters, w.AreaMaxQuarters, graded, plain)) continue;
                }
                // 겨눈 칸 자체도 모양이 덮어야 한다 — 모양 9(삼각형)는 축 ≥ 1 부터라 겨눈 칸이 절대 안 켜진다
                // (0x100dd2d0 방향 0 이 원점y−1 줄부터 훑는다 0x100dd380, 감사3 R5). 전에는 모양을 안 보고 넣었다.
                else if (!ShapeCovers(w.AreaShape, 0, 0, facing) || plain < w.AreaMinQuarters) continue;
                cells.Add((cx, cy));
            }
        return cells;
    }

    /// <summary>그 칸의 인물이 이 대상 방식에 맞나 — 1 적, 4 아군, 5 아무 유닛, 3·6 아무 칸(유닛이면 맞음).</summary>
    private static bool ModeAccepts(int mode, UnitState user, UnitState target) => mode switch
    {
        1 => SeesAsFoe(user, target),
        4 => !user.HasStatus(4) && !Hostile(user, target),
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
            .Where(i => _units[i].Alive && _units[i].OnField && cells.Contains((_units[i].Col, _units[i].Row)) && ModeAccepts(mode, user, _units[i]))];
    }

    /// <summary>
    /// 그 칸에 내려설 수 있나(<c>0x100d99a0</c>) — 판 안, 플래그 &amp;9 없음, 다른 유닛 없음, <b>적 옆 칸(ZOC)이 아님</b>, 물체 없음.
    /// 돌진·이스케이프의 겨눔과 하이 텔레포트의 착지가 쓴다.
    /// </summary>
    private bool CanLandOn(int col, int row, UnitState user)
    {
        if ((uint)col >= Cols || (uint)row >= Rows) return false;
        if (_map is not { } map || col >= map.Cols || row >= map.Rows || (CellFlagsAt(col, row) & 0x9) != 0) return false;
        if (LiveUnitAt(col, row) is { } other && other != user) return false;
        if (ObjectAt(col, row) is { Alive: true }) return false;
        // ZOC 높이는 걷기 높이 +0x78(0x100d9b47 → 0x10073de0, 감사3 R9).
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            if (LiveUnitAt(col + dx, row + dy) is { } e && e != user && SeesAsFoe(user, e) && WalkHeightAt(col + dx, row + dy) == WalkHeightAt(col, row)) return false;
        return true;
    }

    /// <summary>겨눌 수 있는 칸인가 — 대상 방식이 유닛을 고르는 것이면 그 칸에 맞는 유닛이 있어야 한다.</summary>
    private bool CanAimAt(WorkData w, UnitState user, int col, int row)
    {
        if (!InWorkRange(w, user.Col, user.Row, col, row, user)) return false;
        if (w.TargetMode is 3 or 6 or 0 or 2) return true;
        // 모드 7(빈 칸) — 들어갈 수 있고(플래그·유닛·적 옆 칸 ZOC), 물체가 없고, 시전자와 높이가 같은 칸만(0x100daca0 모드 7 → 0x100d9a20).
        if (w.TargetMode == 7) return CanLandOn(col, row, user) && HeightAt(col, row) == HeightAt(user.Col, user.Row);
        // 모드 1 은 적 유닛뿐 아니라 <b>적 물체</b>(포탑·바리케이트)도 겨눈다(0x100daca0).
        if (w.TargetMode == 1 && ObjectAt(col, row) is { Data.Breakable: true, Alive: true } obj && ObjectHostile(obj, user)) return true;
        return LiveUnitAt(col, row) is { } t && ModeAccepts(w.TargetMode, user, t);
    }
}
