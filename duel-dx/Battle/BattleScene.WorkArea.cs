using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

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
internal sealed unsafe partial class BattleScene
{
    /// <summary>두 칸 사이 거리(4분의 1칸) — 평지 기준.</summary>
    internal static int CellDistance(int fromCol, int fromRow, int col, int row) =>
        4 * (Math.Abs(col - fromCol) + Math.Abs(row - fromRow));

    /// <summary>
    /// 거리 자·같은 높이·모드 7·화면 창이 쓰는 칸 높이 — 원본 <c>+0x80</c>(지형 + 종류 0·3·4·5 물체 도장, <c>0x100daca0</c>·<c>0x100db787</c>, 감사3 R9).
    /// 걷기·ZOC·시야는 모든 물체를 찍은 <see cref="WalkHeightAt"/>(<c>+0x78</c>)를 쓴다.
    /// </summary>
    internal int HeightAt(int col, int row) => AimHeightAt(col, row);

    /// <summary>높이까지 넣은 거리 — 차등을 넣은 값과 안 넣은 값 둘 다 돌려준다(최대는 앞, 최소는 뒤로 잰다).</summary>
    internal (int Graded, int Plain) WorkDistance(int fromCol, int fromRow, int col, int row, bool useHeight, bool graded)
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
    internal bool HasSight(int fromCol, int fromRow, int col, int row)
    {
        if (host._map is null) return true;
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
    internal int RangeMaxOf(WorkData w, UnitState? user)
    {
        int weapon = 0;
        // 파트1 기술(work 20000 + Attack 번호)은 Attack.dat 의 사거리 갈래 · 값을 그대로 4분의 1칸으로 쓴다(Part1Rules.RangeQuarters — 0x10038ae0, RANGE 능력치 · 무기 +0x68 포함).
        // 자기 자리 기술(+0x31 = 4 — 메테오 · 힐 윈드 · 기도 …)은 빌린 틀의 것을 둔다. 기본 공격(빌린 파트2 베기 · 쏘기)은 인물의 기본 공격 칸으로.
        if (user != null && Part1.Fighter(user) is not null && Part1.AttackOf(w) is { AimFilter: not 4 } p1) return Part1.RangeQuarters(user, p1);
        if (user != null && host._scene.IsPart1 && user.Data?.BasicWorkId == w.Id && Part1.Fighter(user) is not null) return Part1.BasicRangeQuarters(user);
        // 파트1 인물은 파트2 장비가 없다 — 쏘는 기본 공격(파트2 work 6 을 빌린다)은 파트1 기본 공격 칸의 사거리로(이미 4분의 1칸).
        if (w.RangeKind is 2 or 4 && user != null && Part1.Fighter(user) is not null) weapon = Part1.BasicRangeQuarters(user);
        else if (w.RangeKind is 2 or 4 && user?.Data is { } c && c.Items.Length > 0
            && host._db?.Items.GetValueOrDefault(c.Items[0]) is { } item) weapon = item.Range * 4;
        return w.RangeKind switch
        {
            2 => weapon,
            4 => weapon + w.RangeMaxQuarters,
            _ => w.RangeMaxQuarters,
        };
    }

    /// <summary>
    /// 파트1 판에서 파트1 인물이 쓰는 이 work 의 겨눔 칸(Attack.dat) — 파트1 기술(work 20000 + Attack, 자기 자리 +0x31 = 4 는 빼고)은 그 칸,
    /// 기본 공격(빌린 파트2 work)은 인물의 기본 공격 칸. 그 밖(파트2 판 · 파트2 인물 · 기술 파일을 가진 파트2 틀 기술)은 null — 파트2 식으로 잰다.
    /// </summary>
    /// <summary>파트1 사거리에 높이 차를 넣나 — Attack +0x2d, 다만 집중(상태 22, <see cref="Part1Rules.FocusStatus"/>)이 걸려 있으면 0 으로 본다(<c>0x1003909d</c>).</summary>
    internal bool Part1RangeHeight(Part1Attack p1, UnitState? user) => p1.RangeHeight && !(user != null && Part1.Has(user, Part1Rules.FocusStatus));

    internal Part1Attack? Part1AimOf(WorkData w, UnitState? user)
    {
        if (user == null || !host._scene.IsPart1 || Part1.Fighter(user) is null) return null;
        if (Part1.AttackOf(w) is { AimFilter: not 4 } skill) return skill;
        return user.Data?.BasicWorkId == w.Id ? Part1.BasicAttack(user) : null;
    }

    /// <summary>모양 <paramref name="shape"/> 가 그 칸을 덮나(거리는 이미 최소·최대로 걸렀다고 보고 모양만 본다).</summary>
    internal static bool ShapeCovers(int shape, int dx, int dy, Facing facing) => WorkShape.Covers(shape, dx, dy, (int)facing);   // 표는 편집기 미리보기와 같이 쓴다(WarOfGenesis.Assets.WorkShape)

    /// <summary>방향을 쓰는 모양인가 — 사거리를 그릴 때는 네 방향을 합쳐야 한다.</summary>
    internal static bool ShapeUsesFacing(int shape) => shape is 3 or 5 or 6 or 7 or 9 or 10;

    /// <summary>
    /// 그 모양이 <b>앞으로 간 거리만</b> 재는가 — 3·5·6·7·9 가 그렇다(<c>0x100dafe0</c>·<c>0x100db310</c>).
    /// 옆으로 벌어진 만큼은 거리에 안 들어가므로, 맨해튼으로 자르면 바깥 줄이 통째로 잘려 나간다.
    /// </summary>
    internal static bool ShapeUsesAxisDistance(int shape) => shape is 3 or 5 or 6 or 7 or 9;

    /// <summary>바라보는 쪽으로 몇 칸 갔나(뒤쪽은 음수).</summary>
    internal static int AxisOf(int dx, int dy, Facing facing) => facing switch
    {
        Facing.Up => -dy,
        Facing.Down => dy,
        Facing.Left => -dx,
        _ => dx,
    };

    /// <summary>모양과 거리를 함께 본다 — 모양마다 <b>거리 자가 다르기</b> 때문에 따로 볼 수 없다.</summary>
    internal static bool ShapeReaches(int shape, int dx, int dy, Facing facing,
                                     int minQuarters, int maxQuarters, int graded, int plain) =>
        WorkShape.Reaches(shape, dx, dy, (int)facing, minQuarters, maxQuarters, graded, plain);

    /// <summary>
    /// (fromCol, fromRow) 에서 work 로 (col, row) 칸을 겨눌 수 있나 — 사거리 모양·최소·최대와 지형 &amp; 0x8 만 본다.
    /// </summary>
    internal bool InWorkRange(WorkData w, int fromCol, int fromRow, int col, int row, UnitState? user = null)
    {
        if ((uint)col >= host.Cols || (uint)row >= host.Rows) return false;
        // 부술 수 있는 물체(애시드 풀·럭키가이·바리케이트 따위)가 선 칸은 피해 기술로 겨눌 수 있다(사용자 요청) — 물체가 찍은 벽 깃발 때문에
        // 전에는 사거리 밖으로 쳐졌다. 기본공격은 따로 길이 있어 됐다.
        bool objectTarget = w.IsDamage && ObjectAt(col, row) is { Data.Breakable: true, Alive: true } aimed && !_opened.Contains(aimed);
        if (!objectTarget && host._map is not null && (CellFlagsAt(col, row) & 0x8) != 0) return false;

        int dx = col - fromCol, dy = row - fromRow;
        if (w.SelfCentred || w.RangeShape == 0) return dx == 0 && dy == 0;

        // 파트1 — 기술 · 기본 공격 모두 Attack.dat 사거리 칸 인자 그대로(모양 +0x20 · 크기 · 최소 +0x28 · 높이 +0x2d/+0x30 · 시야 +0x2e · 같은 높이 +0x2f).
        // 칸이 켜지는 것은 크기 ≥ d1(차등 높이) 이고 최소 ≤ d2(2 × |높이 차|)일 때(0x1008ad5d · 0x1008ad64, 분석-파트1-전투 29 · 30절).
        if (Part1AimOf(w, user) is { } p1)
        {
            if (p1.RangeSameHeight && HeightAt(col, row) != HeightAt(fromCol, fromRow)) return false;
            if (!p1.RangeShapeCovers(dx, dy)) return false;
            var (d1, d2) = WorkDistance(fromCol, fromRow, col, row, Part1RangeHeight(p1, user), p1.RangeGraded);
            if (d1 > RangeMaxOf(w, user) || d2 < p1.RangeMin) return false;
            return !p1.RangeSight || HasSight(fromCol, fromRow, col, row);
        }

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
    internal List<(int Col, int Row)> AreaCells(WorkData w, UnitState user, int col, int row)
    {
        if (w.SelfCentred) (col, row) = (user.Col, user.Row);
        if (Part1DashOf(w) is { } dash) return Part1DashCells(dash, user, col, row);
        if (Part1BeamOf(w, user) is { } beam) return Part1BeamCells(beam, user, col, row);
        if (Part1AreaOf(w, user) is { } p1) return Part1AreaCells(p1, user, col, row);
        var cells = new List<(int, int)>();
        if (w.AreaShape == 0) { cells.Add((col, row)); return cells; }

        var facing = col == user.Col && row == user.Row ? user.Facing : FacingToward(user.Col, user.Row, col, row);
        // 옆으로 벌어지는 폭(모양 6·7 의 ±1·±2)은 크기와 상관없다 — 창을 그만큼 넓게 잡는다.
        // 모양 4(화면 전체)는 최대를 아예 안 보므로 판 전체를 훑는다(0x100de010).
        int reach = w.AreaShape == 4 ? Math.Max(host.Cols, host.Rows) : Math.Max(1, w.AreaMaxQuarters / 4) + 2;
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int cx = col + dx, cy = row + dy;
                if ((uint)cx >= host.Cols || (uint)cy >= host.Rows) continue;
                if (host._map is not null && (CellFlagsAt(cx, cy) & 0x8) != 0
                    && !(w.IsDamage && ObjectAt(cx, cy) is { Data.Breakable: true, Alive: true })) continue;      // 부술 수 있는 물체 칸은 범위에 든다
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

    /// <summary>
    /// 파트1 돌진 기술(혼 26~30 · 혈랑마혼 292 · 비연참 294) — 겨누기 <c>+0x31</c> = 7(시전자가 설 수 있는 빈 칸, <c>0x1008a950</c> → <c>0x10089bf0</c>)이고
    /// 효과 거르기 <c>+0x40</c> = 1(적)인 Attack. 핸들러가 시전자를 이동기 <c>0x100816d0</c> 에 실어 겨눈 칸까지 나르며 「칸이 바뀔 때」 맞음 사건
    /// (<c>0x100769b0</c> → <c>0x1007676e</c>)으로 들어선 칸의 적을 친다. 같은 +0x31 = 7 이라도 댓쉬 366(+0x40 = 2) · 워프 367 · 내리기 399 는 치지 않는다.
    /// 파트1 판이든 파트2 판(편집기로 붙인 비연참 20100)이든 Attack.dat 으로 정한다.
    /// </summary>
    internal Part1Attack? Part1DashOf(WorkData w) => w.Id > Part1Game.ChrBase && Part1.AttackOf(w) is { AimFilter: 7, AreaFilter: 1 } a ? a : null;

    /// <summary>
    /// 파트1 돌진 기술의 맞는 칸 — 이동기가 지나며 <b>들어서는</b> 칸들(출발 칸 빼고, 겨눈 칸까지). 이동기를 틱마다 그대로 밟아(빠르기 20 × 1.8, 최대 40 —
    /// 혼 <c>0x100460d5</c> · 비연참 <c>0x1006097f</c> · 혈랑마혼 <c>0x1005fdae</c>) 그 틱 자리의 칸(월드 ÷ 40)을 모은다 — 비스듬한 비연참도 원본처럼.
    /// 혈랑마혼은 시전자 옆 ±40 에 나란히 달리는 분신 둘(<c>0x1007cf00</c>, 10 × 1.1)도 칸이 바뀔 때 친다 — 그 두 길의 칸도 넣는다.
    /// </summary>
    internal List<(int Col, int Row)> Part1DashCells(Part1Attack a, UnitState user, int col, int row)
    {
        const int W = 40;   // 월드 한 칸(0x66666667 >> 4 = ÷ 40)
        var cells = new List<(int Col, int Row)>();
        void Lane(double fx, double fy, double tx, double ty, double speed, double factor)
        {
            int last = (int)Math.Floor(fx / W) * 1000 + (int)Math.Floor(fy / W);
            double x = fx, y = fy;
            for (int guard = 0; guard < 600; guard++)
            {
                double dx = tx - x, dy = ty - y, far = Math.Sqrt(dx * dx + dy * dy);
                if (far <= speed) (x, y) = (tx, ty);
                else (x, y) = (x + dx / far * speed, y + dy / far * speed);
                int c = (int)Math.Floor(x / W), r = (int)Math.Floor(y / W);
                if (c * 1000 + r != last)
                {
                    last = c * 1000 + r;
                    if ((uint)c < host.Cols && (uint)r < host.Rows && !cells.Contains((c, r))) cells.Add((c, r));
                }
                if (far <= speed) break;
                speed = Math.Clamp(speed * factor, 1, 40);
            }
        }
        double ux = user.Col * W + W / 2.0, uy = user.Row * W + W / 2.0, gx = col * W + W / 2.0, gy = row * W + W / 2.0;
        Lane(ux, uy, gx, gy, 20, 1.8);
        if (a.Id == 292)
        {
            int dir = Part1Attack.AreaDirection(user.Col, user.Row, col, row);
            foreach (int o in new[] { -W, W })
            {
                var (ox, oy) = dir is 0 or 2 ? (o, 0) : (0, o);
                Lane(ux + ox, uy + oy, gx + ox, gy + oy, 10, 1.1);
            }
        }
        cells.Remove((user.Col, user.Row));
        return cells;
    }

    /// <summary>
    /// 파트1 빛살 · 관통탄 기술 — 핸들러가 Attack.dat 효과 범위(<c>+0x33</c>~<c>+0x3c</c>)와 상관없이 시전자 자리에서 <b>전장 범위 끝</b>(맵 <c>+0x1f4</c>~<c>+0x1fa</c>)까지
    /// 보는 쪽(<c>0x10019de0(시전자, 겨눈 유닛 +0x206 또는 칸)</c>) 축으로 이동기를 빠르기 40 × 1.0(<c>+0x10c</c>)으로 보내고, 그 이동기의 맞음 사건
    /// <c>0x100769b0(1, desc, 3, 3D, 3, 0)</c> 이 틱마다 칸이 바뀌면(<c>0x1007676e</c>) <c>0x10039690</c> 으로 그 칸의 적(<c>+0x40</c> = 1)을 모은다 — 출발 칸은 안 친다.
    /// 빔라이플 10 <c>0x100457f0</c> · 용병 빔라이플 315~317 <c>0x10062530</c> · 버스터 캐논 393 <c>0x1006f5a0</c> · 레이저 캐논 394 <c>0x10070440</c> · 버스터 런쳐 397 <c>0x10071690</c> ·
    /// 롱 레이져 398 <c>0x10072e60</c> · 사이드 슬레이더 400~402 <c>0x10073440</c>, 줄 셋(시전자 줄과 옆 ±40 — <c>0x10057e93</c>)은 버스터 포 282 <c>0x10056140</c> · 다크 라이트 406 <c>0x10073e40</c>.
    /// 자료 범위가 한 칸(315 · 393 · 394 · 398)이거나 13칸(397)인 것도 판 끝까지 맞는다. 관통사격 9(<c>0x10045590</c>, 같은 꼴)는 기본 공격 그림이 없어 파트2 길이라 뺀다.
    /// </summary>
    internal static bool IsPart1Beam(int attackId) => attackId is 10 or 282 or (>= 315 and <= 317) or 393 or 394 or 397 or 398 or (>= 400 and <= 402) or 406;

    /// <summary>파트1 판의 파트1 인물이 쓰는 빛살 기술(기본 공격 빔라이플 포함)의 Attack — <see cref="IsPart1Beam"/>.</summary>
    internal Part1Attack? Part1BeamOf(WorkData w, UnitState? user) => Part1AimOf(w, user) is { } a && IsPart1Beam(a.Id) ? a : null;

    /// <summary>
    /// 빛살 기술의 맞는 칸 — 이동기를 틱마다 그대로 밟는다(월드 x · y ÷ 40, z ÷ 20). 끝점은 그 줄의 전장 끝 칸 가운데(위 <c>+0x1f6</c> × 40 + 20 · 아래 <c>+0x1fa</c> × 40 − 20 …).
    /// 높이: 맞음 사건의 3D 깃발(둘째 바이트, <c>0x10076b78</c> → <c>0x10039690</c> 인자 5)이 서면 칸마다 유닛 몸 높이 k = 0 .. (그림 높이 × 2/3)/12 − 1 에서
    /// (|탄 z/20 − 유닛 z/20 − k| + 2 × 맨해튼) × 2 ≤ 3 인 유닛만 맞는다(<c>0x100399ac</c>~<c>0x10039a81</c>) — 곧 <b>탄보다 두 층 이상 높이 선 유닛은 안 맞는다</b>.
    /// (탄보다 훨씬 낮은 유닛이 몸 높이 밖이라 빠지는 아래쪽 끝은 그림 높이를 몰라 안 옮겼다.)
    /// 탄 높이: 10 시전자 z + 60 · 315~317 · 394 z + 80 · 400~402 z + 40(곧게) · 393 시전자 z → 끝 칸 땅 높이 × 20 · 398 둘레 3×3 칸 가장 높은 땅 × 20 + 20(시전자를 들어 올린 높이).
    /// 282 · 397 · 406 은 3D 깃발이 0(<c>0x10057e5f</c> · <c>0x10072ca7</c>~ · <c>0x10075c84</c>) — 높이와 상관없이 맞는다.
    /// </summary>
    internal List<(int Col, int Row)> Part1BeamCells(Part1Attack a, UnitState user, int col, int row)
    {
        const int W = 40;
        var cells = new List<(int Col, int Row)>();
        int dir = Part1Attack.AreaDirection(user.Col, user.Row, col, row);
        var r = Part1Range.Active ? Part1Range : null;
        int left = r?.Left ?? 0, top = r?.Top ?? 0, right = r?.Right ?? host.Cols, bottom = r?.Bottom ?? host.Rows;
        bool solid = a.Id is not (282 or 397 or 406);
        int z0 = HeightAt(user.Col, user.Row) * 20;
        int lift = a.Id switch { 10 => 60, (>= 315 and <= 317) or 394 => 80, >= 400 and <= 402 => 40, _ => 0 };
        if (a.Id == 398)
        {
            // 0x10072fc0~ — 시전자 둘레 3×3 칸 땅 높이 × 20 의 가장 큰 값, 빛살은 그 + 20 에서 곧게.
            int top3 = z0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((uint)(user.Col + dx) < host.Cols && (uint)(user.Row + dy) < host.Rows) top3 = Math.Max(top3, HeightAt(user.Col + dx, user.Row + dy) * 20);
            z0 = top3 + 20;
        }
        else z0 += lift;
        int[] lanes = a.Id is 282 or 406 ? [-1, 0, 1] : [0];
        foreach (int lane in lanes)
        {
            // 옆 줄은 시전자 칸에서 옆으로 한 칸(±40) — 그 줄이 전장 밖이면 안 쏜다(0x10057d30~0x10057d85).
            int sc = user.Col + (dir is 0 or 2 ? lane : 0), sr = user.Row + (dir is 1 or 3 ? lane : 0);
            if (sc < left || sc >= right || sr < top || sr >= bottom) continue;
            var (ec, er) = dir switch { 0 => (sc, top), 2 => (sc, bottom - 1), 1 => (left, sr), _ => (right - 1, sr) };
            double x = sc * W + W / 2.0, y = sr * W + W / 2.0, z = z0;
            double tx = ec * W + W / 2.0, ty = er * W + W / 2.0;
            // 393 은 끝 칸 땅 높이로 내려가거나 올라간다(0x1007021d~0x10070319) — 나머지는 출발 높이 그대로.
            double tz = a.Id == 393 ? HeightAt(Math.Clamp(ec, 0, host.Cols - 1), Math.Clamp(er, 0, host.Rows - 1)) * 20 : z0;
            int last = (int)Math.Floor(x / W) * 1000 + (int)Math.Floor(y / W);
            for (int guard = 0; guard < 1000; guard++)
            {
                double dx = tx - x, dy = ty - y, dz = tz - z, far = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (far <= W) (x, y, z) = (tx, ty, tz);
                else (x, y, z) = (x + dx / far * W, y + dy / far * W, z + dz / far * W);
                int c = (int)Math.Floor(x / W), rr = (int)Math.Floor(y / W);
                if (c * 1000 + rr != last)
                {
                    last = c * 1000 + rr;
                    bool reach = !solid || (uint)c >= host.Cols || (uint)rr >= host.Rows || HeightAt(c, rr) <= (int)(z / 20) + 1;
                    if ((uint)c < host.Cols && (uint)rr < host.Rows && reach && !cells.Contains((c, rr))) cells.Add((c, rr));
                }
                if (far <= W) break;
            }
        }
        cells.Remove((user.Col, user.Row));
        return cells;
    }

    /// <summary>
    /// 파트1 판에서 파트1 인물이 쓰는 파트1 기술(<see cref="Part1SkillBook"/> 이 만든 work)의 Attack — 효과 범위를 Attack.dat 로 잰다.
    /// 기본 공격(빌린 파트2 work)과 손으로 쓴 스킬 파일(썬더스탐 · 소환 …)은 null — 빌린 틀 · 그 파일의 범위를 둔다.
    /// </summary>
    internal Part1Attack? Part1AreaOf(WorkData w, UnitState? user) =>
        user != null && host._scene.IsPart1 && Part1SkillBook.IsGenerated(w.Id) && Part1.Fighter(user) is not null && Part1.AttackOf(w) is { } a ? a : null;

    /// <summary>
    /// 파트1 효과 범위 칸 — 원본 <c>0x100393b0(atk, 겨눈 x, y, 방향, 0, 0)</c> 그대로(분석-파트1-전투 31절).
    /// 가운데는 <b>겨눈 칸</b>(자기 자리 기술 +0x31 = 4 는 제 칸), 방향은 시전자 → 겨눈 칸(<see cref="Part1Attack.AreaDirection"/>).
    /// 모양 <c>+0x33</c>(<see cref="Part1Attack.AreaShapeCovers"/>) · 크기 <c>+0x38</c> ≥ d · 최소 <c>+0x3c</c> ≤ d, d = 4 × 맨해튼 + (<c>+0x35</c> 면 2 × |높이 차|) — 겨눈 칸의 높이에서 잰다.
    /// <c>+0x36</c> 이면 겨눈 칸과 높이가 같은 칸만. 거르기 <c>+0x40</c> 는 칸이 아니라 WorkTargets(효과 대상 방식)가 맡는다(칠하기는 원본도 거르기 2 — 아무 칸).
    /// 유닛을 누르는 기술(+0x31 = 1 · 3 · 5)은 누른 유닛을 따로 쥔다(<c>+0x206</c>, <c>0x10039f1c</c>) — 최소가 겨눈 칸을 빼도 그 칸은 넣는다.
    /// </summary>
    internal List<(int Col, int Row)> Part1AreaCells(Part1Attack a, UnitState user, int col, int row)
    {
        var cells = new List<(int, int)>();
        if (a.AreaShape == 0) { cells.Add((col, row)); return cells; }
        if (a.Id is >= 31 and <= 35) return Part1SpinCells(a, user);
        int dir = Part1Attack.AreaDirection(user.Col, user.Row, col, row);
        int size = a.AreaSize, min = a.AreaMinSize;
        // 높이 보정은 거리를 늘리기만 하므로(차등 없음) 크기 ÷ 4 칸 네모면 넉넉하다 — 9999(판 전체)는 판 크기로.
        int reach = Math.Min(Math.Max(host.Cols, host.Rows), Math.Max(0, size) / 4);
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int cx = col + dx, cy = row + dy;
                if ((uint)cx >= host.Cols || (uint)cy >= host.Rows) continue;
                if (!a.AreaShapeCovers(dx, dy, dir)) continue;
                if (host._map is not null && (CellFlagsAt(cx, cy) & 0x8) != 0
                    && !(ObjectAt(cx, cy) is { Data.Breakable: true, Alive: true })) continue;
                if (a.AreaSameHeight && HeightAt(cx, cy) != HeightAt(col, row)) continue;
                var (d, _) = WorkDistance(col, row, cx, cy, a.AreaHeight, graded: false);
                if (d > size || d < min) continue;
                cells.Add((cx, cy));
            }
        if (a.AimFilter is 1 or 3 or 5 && !cells.Contains((col, row))) cells.Insert(0, (col, row));
        return cells;
    }

    /// <summary>
    /// 파 31~35(<c>0x100463d0</c>) — 맞는 칸은 범위 칠하기(<c>0x100393b0</c>, 최소 <c>+0x3c</c> = 5 라 제 칸 둘레 2칸 고리)가 아니다. 다섯 바퀴 뒤 시전자 자리(x, y, z)에 선
    /// Obs 245 가 사라질 때 맞음 사건 <c>0x10076950(1, desc, +0x33, +0x35, +0x38, 0)</c>(<c>0x10046a85</c>~<c>0x10046ace</c>)이 <c>0x10039690</c> 으로 그 자리 둘레를 모은다 —
    /// 이 함수는 모양 함수에 <b>최소 0</b>을 넘기므로(<c>0x100396c6</c>~) <b>이웃 칸도 맞는다</b>(마름모 8 = 맨해튼 2칸 안 모두). 높이(<c>+0x35</c> = 1, 3D):
    /// 유닛 몸 높이 k 마다 (|시전자 z/20 − 유닛 z/20 − k| + 2 × 맨해튼) × 2 ≤ 8(<c>0x100399ac</c>~) — 시전자보다 높이 선 유닛은 한 층마다 2 를 더 잰다(낮은 유닛은 몸 높이 안이라 0).
    /// </summary>
    internal List<(int Col, int Row)> Part1SpinCells(Part1Attack a, UnitState user)
    {
        var cells = new List<(int, int)>();
        int size = a.AreaSize, reach = Math.Max(0, size) / 4, level = HeightAt(user.Col, user.Row);
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int cx = user.Col + dx, cy = user.Row + dy;
                if ((uint)cx >= host.Cols || (uint)cy >= host.Rows) continue;
                int d = 4 * (Math.Abs(dx) + Math.Abs(dy));
                if (d > size) continue;
                if (a.AreaHeight && d + 2 * Math.Max(0, HeightAt(cx, cy) - level) > size) continue;
                cells.Add((cx, cy));
            }
        return cells;
    }

    /// <summary>그 칸의 인물이 이 대상 방식에 맞나 — 1 적, 4 아군, 5 아무 유닛, 3·6 아무 칸(유닛이면 맞음).</summary>
    internal static bool ModeAccepts(int mode, UnitState user, UnitState target) => mode switch
    {
        1 => SeesAsFoe(user, target),
        4 => !user.HasStatus(4) && !Hostile(user, target),
        5 or 3 or 6 => true,
        0 or 2 => target == user,
        _ => false,
    };

    internal const int CureAbility = 11, SoulBlastAbility = 28, SacrificeAbility = 13, ExplosionAbility = 22, BlackHoleAbility = 115;

    /// <summary>겨눈 칸에서 실제로 맞는 인물들 — 효과 범위 칸 안에서 효과 대상 방식(<c>+0x1e</c>)으로 거른다.</summary>
    internal List<int> WorkTargets(WorkData w, UnitState user, int col, int row)
    {
        var cells = EffectCells(w, user, col, row);
        int mode = w.AreaMode != 0 ? w.AreaMode : w.TargetMode;
        // 폭(6)·인페르노(159)·템페스트(186) — 효과 대상은 3(아무 칸)이지만 핸들러가 0x1006fde0 으로 적만 거른다
        // (0x10083760 · 0x100b220d · 0x100bddd6, ba-20 D3). 전에는 아군과 시전자 자신도 맞았다. 소울 블레스트의 관통탄도 적만(슬롯 2 의 +0x1e = 1).
        if (w.AbilityId is 6 or 159 or 186 or SoulBlastAbility) mode = 1;
        if (w.AbilityId == SpecialHitsSkill.HellLaserAbility) mode = 1;
        // 파트1 빛살(Part1BeamCells) — 이동기의 맞음 사건이 효과 거르기 +0x40 = 1(적)로 모은다. 빌린 기본 공격 틀(빔라이플)의 대상 방식과 상관없이.
        if (Part1BeamOf(w, user) is not null) mode = 1;
        var hit = Enumerable.Range(0, host._units.Length)
            .Where(i => host._units[i].Alive && host._units[i].OnField && cells.Contains((host._units[i].Col, host._units[i].Row)) && ModeAccepts(mode, user, host._units[i]));
        // 대상 상한 — 모으는 함수(0x100df5c0 · 0x10070bb0)는 위 행부터, 행 안에서는 왼쪽부터 훑다가 상한에서 멈춘다(ba-20 E8).
        // 블레이드 샤워 16명(0x100844aa), 카운터 스피어·진 풍아열공참·빅 뱅 20명.
        int cap = w.AbilityId switch { 112 => 16, 77 or 127 or 166 => 20, _ => int.MaxValue };
        if (cap != int.MaxValue) hit = hit.OrderBy(i => host._units[i].Row).ThenBy(i => host._units[i].Col).Take(cap);
        // 회복 다섯(와이드 힐 93 · 리커버리 141 · 리제너레이션 142 · 엘레맨탈 힐 143 · 힐 윈드 144) — 모은 대상이 여섯을 넘으면 무작위 자리부터
        // 연달아(끝에서 처음으로 돌아) 여섯만 고른다(0x100a55d4 · 0x100a3fb4 · 0x100a4584 · 0x100a4ad9 · 0x100a5019, gm-skil-21).
        // 한 번 시전하는 동안은 같은 여섯이게 시전마다 _healPick 을 새로 뽑는다(Turn).
        if (w.AbilityId is 93 or 141 or 142 or 143 or 144)
        {
            var all = hit.OrderBy(i => host._units[i].Row).ThenBy(i => host._units[i].Col).ToList();
            if (all.Count > 6) return [.. Enumerable.Range(0, 6).Select(j => all[(_healPick + j) % all.Count])];
        }
        return [.. hit];
    }

    /// <summary>회복 다섯의 「여섯만」 고르기가 시작하는 자리 — 시전마다 새로 뽑는다.</summary>
    internal int _healPick;

    /// <summary>실제로 판정이 나는 칸 — 자료 범위(<see cref="AreaCells"/>)에 핸들러가 박아 둔 예외를 얹는다. 겨눔 미리보기도 이것을 쓴다.</summary>
    internal HashSet<(int Col, int Row)> EffectCells(WorkData w, UnitState user, int col, int row)
    {
        // 큐어(11) — 자료 범위는 레벨 따라 0→3 이지만 핸들러 0x10081830 은 겨눈 유닛 하나에게만 보낸다(0x10081b3c, ba-20 D4).
        if (w.AbilityId == CureAbility) return [(col, row)];
        var cells = AreaCells(w, user, col, row).ToHashSet();
        // 소울 블레스트(28) — 탄(Obs 0x319)이 시전자 자리에서 겨눈 칸까지 날며 지나는 칸마다 적을 한 번씩 친다
        // (슬롯 2 0x100c28f0, 0x1009e9e5, ba-20 D5). 지나는 칸은 직선을 칸으로 자른 것(가설).
        // 자료에 효과 범위가 있으면(모드 > 타키리온 개편 — 겨눈 칸 둘레를 치는 범위기) 관통하지 않고 그 범위만 친다.
        if (w.AbilityId == SoulBlastAbility && w.AreaArg == 0)
        {
            // 탄은 칸 가운데에서 칸 가운데로 10px/틱 직선 — 틱마다 표본 자리의 칸이 바뀌면 그 칸을 친다(0x100c2e90, 트윈 0x10037b50, ba-20 E6).
            double x0 = user.Col * 40 + 20, y0 = user.Row * 40 + 20, x1 = col * 40 + 20, y1 = row * 40 + 20;
            double length = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            for (double d = 10; d < length; d += 10)
            {
                var cell = ((int)((x0 + (x1 - x0) * d / length) / 40), (int)((y0 + (y1 - y0) * d / length) / 40));
                if (cell != (user.Col, user.Row)) cells.Add(cell);
            }
        }
        // 헬 레이져(172) — 타격 이펙트 Obs 1050 다섯이 시전자 <b>앞 5칸</b> 줄의 옆 −2..+2 칸에 놓이고 그 칸 하나씩만 친다
        // (0x100b39a0 · 0x100b3c6d~, ba-20 E3). 자료 범위(2칸 앞 줄)와 다르다 — 화면 확인은 못 했다(코드로만 확정).
        if (w.AbilityId == SpecialHitsSkill.HellLaserAbility)
        {
            var facing = (col, row) == (user.Col, user.Row) ? user.Facing : FacingToward(user.Col, user.Row, col, row);
            var (fx, fy) = facing switch { Facing.Up => (0, -1), Facing.Down => (0, 1), Facing.Left => (-1, 0), _ => (1, 0) };
            cells = [.. Enumerable.Range(-2, 5).Select(k => (user.Col + 5 * fx + k * fy, user.Row + 5 * fy + k * fx))];
        }
        return cells;
    }

    /// <summary>
    /// 그 칸에 내려설 수 있나(<c>0x100d99a0</c>) — 판 안, 플래그 &amp;9 없음, 다른 유닛 없음, <b>적 옆 칸(ZOC)이 아님</b>, 물체 없음.
    /// 돌진·이스케이프의 겨눔과 하이 텔레포트의 착지가 쓴다.
    /// </summary>
    internal bool CanLandOn(int col, int row, UnitState user)
    {
        if ((uint)col >= host.Cols || (uint)row >= host.Rows) return false;
        if (host._map is not { } map || col >= map.Cols || row >= map.Rows || (CellFlagsAt(col, row) & 0x9) != 0) return false;
        if (LiveUnitAt(col, row) is { } other && other != user) return false;
        if (ObjectAt(col, row) is { Alive: true }) return false;
        // ZOC 높이는 걷기 높이 +0x78(0x100d9b47 → 0x10073de0, 감사3 R9).
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            if (LiveUnitAt(col + dx, row + dy) is { } e && e != user && SeesAsFoe(user, e) && WalkHeightAt(col + dx, row + dy) == WalkHeightAt(col, row)) return false;
        return true;
    }

    /// <summary>겨눌 수 있는 칸인가 — 대상 방식이 유닛을 고르는 것이면 그 칸에 맞는 유닛이 있어야 한다.</summary>
    internal bool CanAimAt(WorkData w, UnitState user, int col, int row)
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
