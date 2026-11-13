using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

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
/// 물체는 판에 찍은 플래그·높이로 막는다(<see cref="CellFlagsAt"/>·<see cref="WalkHeightAt"/>, 감사3 R1). 날기는 파트2 자료에 쓰는 인물이 없어 빠져 있고,
/// 파트1 나는 유닛(인물 칸 12)만 <see cref="Part1Flyer"/> 규칙으로 걷는다.
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    /// <summary>
    /// 칸에 <b>더하는</b> 색 — 원본은 층 색에 칸 밝기(평지 74)를 곱해 <b>가산</b>으로 얹는다(섞기 방식 17, `0x1000e150`).
    /// 알파 섞기가 아니라 <b>밝히기만</b> 한다.
    /// </summary>
    internal const uint MoveTint = 0x1C1C49, RangeTint = 0x491C0B;

    /// <summary>층 색 원값 — 이동 층 2 (100,100,255) · 사거리 층 12 (255,100,40) · 효과 범위 층 0 (255,170,40). 화면에는 × 칸밝기/256 을 더한다.</summary>
    internal const uint MoveLayer = 0x6464FF, RangeLayer = 0xFF6428, SplashLayer = 0xFFAA28;
    /// <summary>닿을 수 있는 오브젝트 칸(층 1) — 노랑 (255,255,20). 겹치면 이동 칸보다 먼저 칠한다(분석-UI 「칸 깃발」).</summary>
    internal const uint ObjectLayer = 0xFFFF14;
    internal const double RangeWaveCellsPerSecond = 30;

    /// <summary>한 인물의 이동 영역 — 칸마다 드는 TP(못 가면 <see cref="int.MaxValue"/>), 되짚을 앞 칸, 빨간 칸.</summary>
    internal sealed class MoveRange(int[] cost, int[] prev, bool[] red)
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

    internal MoveRange? _range;
    internal int _rangeUnit = -1, _rangeCol = -1, _rangeRow = -1, _rangeTp = -1;
    internal double _rangeStart;

    /// <summary>고른 인물이 서 있으면 영역을 (필요할 때만) 다시 셈한다. 고른 인물이 없거나 움직이는 중이면 지운다.</summary>
    internal void RefreshMoveRange()
    {
        if ((uint)host._selected >= host._units.Length || !host._units[host._selected].Alive || host._units[host._selected].IsBusy || _routine != null)
        {
            _rangeUnit = -1;
            return;
        }
        var unit = host._units[host._selected];
        var (oc, or) = RangeOrigin(unit);
        if (host._selected == _rangeUnit && oc == _rangeCol && or == _rangeRow && unit.Tp == _rangeTp
            && _range is { } cached && cached.Cost.Length == host.Cols * host.Rows) return;   // 판 크기가 바뀌었으면 다시 셈한다

        _range = ComputeRange(unit, withRed: true);
        if (_range == null) { _rangeUnit = -1; return; }
        if (host._selected != _rangeUnit || oc != _rangeCol || or != _rangeRow) _rangeStart = host._lastTime;
        _rangeUnit = host._selected;
        _rangeCol = oc;
        _rangeRow = or;
        _rangeTp = unit.Tp;
    }

    /// <summary>이동 영역을 셀 출발 칸 — 차례인 인물은 차례 시작 자리, 나머지는 지금 자리.</summary>
    internal (int Col, int Row) RangeOrigin(UnitState unit) =>
        _turn >= 0 && host._units[_turn] == unit ? (unit.OriginCol, unit.OriginRow) : (unit.Col, unit.Row);

    // 전장에 있는 사람만 — 퇴장(사건 201)한 사람은 살아 있는 채 좌표가 남아, 그 칸을 누르면 보이지 않는 사람을 골라 걷지 못했다
    // (사용자 보고: Btl 0150 (21,22) 는 WASD 로는 가는데 클릭으로는 안 감).
    // 파트1 큰 유닛(인물 칸 11 = 유닛 +0xb9)은 둘레 3×3 칸 어디에서든 잡힌다 — 원본 칸의 유닛 찾기 0x10089610 이 그 칸(맵 +0x64 표, 가운데 칸만 적힌다 0x10037080)에
    // 유닛이 없으면 둘레 3×3 에서 +0xb9 인 유닛을 준다(0x1008969c~0x100897c9). 겨누기 · 범위 모으기(0x1008f1a0) · 설 칸(0x10089bf0)이 모두 이것을 쓴다(버그 리포트 #55).
    internal UnitState? LiveUnitAt(int col, int row) =>
        UnitStandingAt(col, row) ?? host._units.FirstOrDefault(u => u.Alive && u.OnField && Covers(u, col, row));

    /// <summary>그 칸에 <b>가운데</b>를 둔 유닛만(원본 맵 +0x64 표 그대로) — ZOC 의 네 이웃(0x10089830 은 이웃을 표에서 바로 읽는다)이 쓴다.</summary>
    internal UnitState? UnitStandingAt(int col, int row) => host._units.FirstOrDefault(u => u.Alive && u.OnField && u.Col == col && u.Row == row);

    /// <summary>파트1 판의 큰 유닛(<see cref="Part1Rules.IsBig"/>) — 제 칸 둘레 3×3 을 차지한다.</summary>
    internal bool Part1Big(UnitState u) => host._scene.IsPart1 && Part1.IsBig(u);

    /// <summary>차지 칸의 반지름 — 큰 유닛 1(3×3), 나머지 0.</summary>
    internal int Footprint(UnitState u) => Part1Big(u) ? 1 : 0;

    /// <summary>그 유닛이 (col, row) 를 차지하나 — 제 칸, 큰 유닛이면 둘레 3×3.</summary>
    internal bool Covers(UnitState u, int col, int row)
    {
        int dx = Math.Abs(u.Col - col), dy = Math.Abs(u.Row - row);
        return dx == 0 && dy == 0 || dx <= 1 && dy <= 1 && Part1Big(u);
    }

    /// <summary>
    /// <paramref name="who"/> 가 (col, row) 에 서면 차지 칸이 겹치는 다른 살아 있는 유닛(없으면 null) — 큰 유닛은 3×3 모든 칸에서 <c>0x10089610</c> 이
    /// 자신 말고 아무도 안 주어야 선다(<c>0x10089ed9</c>~<c>0x10089efe</c> · 땅 <c>0x10089fa8</c>), 작은 유닛은 그 칸만(<c>0x10089d99</c>).
    /// </summary>
    internal UnitState? Overlapping(UnitState who, int col, int row, Func<UnitState, bool>? blocks = null)
    {
        int reach = Footprint(who);
        foreach (var o in host._units)
        {
            if (o == who || !o.Alive || !o.OnField || blocks != null && !blocks(o)) continue;
            int far = Math.Max(Math.Abs(o.Col - col), Math.Abs(o.Row - row));
            if (far <= reach || far == reach + 1 && Footprint(o) == 1) return o;
        }
        return null;
    }

    /// <summary>인물의 지금 자리·TP 로 이동 영역을 셈한다. 지도·게임 표가 없으면 null.</summary>
    /// <param name="workId">예산에서 뺄 기술(0 이면 플레이어가 고른 기술·기본공격) — AI 는 기술마다 예산이 다르다(0x1005d070).</param>
    /// <param name="tp">지금 TP 대신 쓸 값(접근 단계는 최대 TP 로 잰다) — null 이면 지금 TP.</param>
    /// <param name="origin">출발 칸을 바꿔 잰다(목표 칸에서의 경로 비용 지도) — 예산은 <paramref name="tp"/> 로 넉넉히.</param>
    /// <param name="flee">파트1 AI 표 「움직이지 않음」(+0 = 3) 인물도 도망칠 때는 걷는다(0x10026470 은 이동 방식을 안 본다).</param>
    internal MoveRange? ComputeRange(UnitState unit, int workId = 0, int? tp = null, (int Col, int Row)? origin = null, bool flee = false, bool withRed = false)
    {
        if (host._map is not { } map || host._db is not { } db || unit.Data is not { } c) return null;

        int n = host.Cols * host.Rows;
        var costs = new int[n];
        var prev = new int[n];
        var red = new bool[n];
        Array.Fill(costs, int.MaxValue);
        Array.Fill(prev, -1);

        int dex = Math.Max(1, db.Dex(CombatData(unit) ?? c));   // 군단 부하는 대장 DEX(0x1007ae50, 감사3 L2)
        int num5 = db.N(5);
        // 25(이동 불가)면 갈 수 있는 칸이 자기 칸뿐이다(0x10074510).
        // 예산은 <b>지금 고른 work</b> 의 TP 를 남긴다(상태 12 는 어빌리티, 상태 10 은 기본공격) —
        // 늘 기본공격으로 셈하면 비싼 어빌리티를 고른 채 너무 멀리 걸을 수 있다. TP 비용에는 상태 20(소모량 %)도 먹는다.
        bool playerBrowse = workId <= 0 && tp == null && origin == null && _targetWork <= 0 && IsMine(unit);
        if (workId <= 0) workId = _targetWork > 0 ? _targetWork : c.BasicWorkId;
        int narrow = unit.HasStatus(25) ? 0 : (tp ?? unit.Tp) + Math.Min(0, c.Ctp - TpCostFor(unit, c, workId));
        // 플레이어가 인물을 고른 상태 10 은 파랑을 <b>현재 TP 전부</b>로 한 번 더 칠한다(0x10069659) — 멀리 걸어가 쉴 수 있다.
        // 빨강(기본공격 자리)은 좁은 예산 기준이고, TP+CTP 가 기본공격 비용에 못 미치면 아예 없다(0x10069524). fg-22.
        int budget = playerBrowse && !unit.HasStatus(25) ? Math.Max(narrow, unit.Tp) : narrow;
        // 붉은 칸(기본공격 사거리)은 withRed 일 때만 칠한다 — 갈 칸마다 사거리 창을 훑어(시야선까지) 범위 계산의 9할을 먹는다(사거리 긴 인물 70~80ms).
        // 쓰는 곳은 판에 그리는 이동 영역(_range)과 링의 「쉬기만 남음」뿐이다 — 전에는 AI · 길찾기의 모든 계산이 같이 칠해 적 차례가 느렸다(사용자 보고).
        bool redAllowed = withRed && (!playerBrowse || unit.Tp + c.Ctp >= TpCostFor(unit, c, c.BasicWorkId));

        // 파트1 판 — 이동력은 SPD × 2(0x1003a9ec → A* 0x100239d0 의 한도, 0x10023d93), 한 걸음 = (|높이 차| + 2 · 맨해튼) × 2(0x1008a4f0) —
        // 평지 한 칸 4, 높이 한 단마다 2 더. 높이 차 2 넘는 칸은 못 간다(0x1008a130). 전에는 (높이 차 + 2) × 4 = 평지 8 로 원본의 반만 갔다.
        // 파트2 틀은 걸음을 TP 로 빼므로, 걸음 값은 파트1 이동력 단위로 길을 찾은 뒤 최대 TP 가 그 이동력과 같아지게 TP 로 바꾼다(분석-파트1-전투 11.3 · 30절).
        // 공격 값은 CTP 100 이 빚으로 받아 준다(Part1Chr.ToCharacterData). 스피디(파트1 강화 17)는 SPD +8(0x10051640 의 상태 17, 분석-파트1-기술-b 9절), 지형 적응도 +8, 0 아래 온도는 그만큼 깎는다(Part1Spd).
        int part1Points = host._scene.IsPart1 && Part1.Fighter(unit) != null ? Math.Max(1, Part1Spd(unit) * 2) : 0;
        int part1Scale = Math.Max(1, unit.MaxTp);
        // 파트1 길찾기 한도(이동력 단위) — TP 예산을 이동력으로 바꾼 값(내림). 칸 값은 끝에서 TP 로 올림해 바꾸므로 예산을 넘지 않는다.
        int walkBudget = part1Points > 0 ? (int)Math.Min(int.MaxValue, Math.Max(0L, budget) * part1Points / part1Scale) : budget;
        // 파트1 AI 표 이동 방식 3(움직이지 않음 — 엔진 0 번 칸 · AI 24)은 이동 고르기(0x10026520)를 통째로 건너뛰고 제자리에서만 친다(0x10026b80).
        if (!flee && origin == null && Part1AiOf(unit) is { MoveMode: 3 }) walkBudget = 0;
        // 파트1 나는 유닛(인물 칸 12 = 유닛 +0xba ≠ 0 — 비룡 아지다하카 · 티아마트 · 썬더볼트 · 라이트닝 · 아지): 지나는 칸은 판 안이기만 하면 되고
        // (0x1008a130 의 +0xba 갈래 — 벽 비트 1 · 높이 차 · 유닛 · ZOC 를 안 본다, 큰 유닛 +0xb9 면 3×3 이 판 안), 설 칸은 지형 비트 2 가 없어야 한다
        // (0x10089bf0 의 +0xba 갈래 0x10089cc9 · 큰 유닛 0x10089ed9). 걸음 비용은 같은 꼴에 높이만 나는 높이(0x10038090)로 잰다.
        var flyer = part1Points > 0 && Part1Flyer(unit) is { } fc ? fc : null;
        bool flyBig = flyer != null && flyer.Fields[Part1Chr.BigField] != 0;

        // 높이·플래그는 물체까지 찍은 판(+0x78 걷기 높이 · +0x88 플래그)으로 본다 — 상자·포탑·닫힌 문 칸은 &9 벽이다(감사3 R1).
        int H(int col, int row) => WalkHeightAt(col, row);
        ushort F(int col, int row) => CellFlagsAt(col, row);
        // 파트1 전장 범위(행동 103) 밖은 판 밖과 같다 — 길 찾기 · 설 칸 · 나는 유닛이 지나는 칸 모두(0x100267a8 · 0x10029132 · 0x10089bf0 둘레).
        bool InBounds(int col, int row) => (uint)col < host.Cols && (uint)row < host.Rows && col < map.Cols && row < map.Rows && Part1Range.Contains(col, row);

        var (originCol, originRow) = origin ?? RangeOrigin(unit);

        int unitIndex = Array.IndexOf(host._units, unit);
        // 제 군단 부하는 대장을 막지 않는다 — 대장이 움직이면 부하도 진형대로 따라오기 때문이다(분석-군단).
        // 원본 0x100d9a20(0x100d9b17~0x100d9b2e)은 자기 말고 모든 유닛이 막고, 부하·대장이 서로 지나가는 것은 진형 다시 세우기
        // (0x100da220·0x100da390, 이동 명령 0x2711 중일 때)뿐이다(감사3 R3). 그래도 사용자 요청(fg-15)으로 리메이크는 대장이 제 부하를
        // 지나가게 둔다 — 원본대로 막으면 부하에 둘러싸인 대장이 못 움직인다. 일부러 남긴 차이다.
        // 내가 움직이는 인물은 <b>같은 편(아군·동맹)</b>도 지나간다 — 서지는 못한다(아래에서 선 칸을 파랑에서 뺀다). 사용자 보고: 다른 대장의
        // 용병들에 둘러싸이면 한 칸도 못 움직였다(원본에서는 칸을 찍으면 아군이 비켜 주거나 지나갔다고 한다 — 분석(감사3 R3)과 다르지만 사용자 기억을 따른다).
        // AI 가 움직이는 인물은 전처럼 막힌다(전투 결과가 달라지지 않게).
        // 원본을 다시 읽은 결과(0x10074510 → 군단 유닛은 0x100da390): 지나가는 것은 <b>제 군단 안에서만</b>이다 — 대장은 제 부하를,
        // 부하는 같은 대장의 부하와 제 대장을. 다른 대장의 부하·군단 아닌 아군·동맹은 원본에서도 막는다. 그래도 「같은 편은 지나간다」는
        // 사용자 요청으로 남긴다(일부러 남긴 차이). 부하 쪽 규칙은 전에 빠져 있었다.
        bool passAllies = IsMine(unit);
        bool SameLegion(UnitState other) => other.LeaderIndex == unitIndex
            || (unit.LeaderIndex >= 0 && (other.LeaderIndex == unit.LeaderIndex || Array.IndexOf(host._units, other) == unit.LeaderIndex));
        bool Blocks(UnitState other) => other != unit && !SameLegion(other) && !(passAllies && other.IsAlly);

        // 파트1 큰 유닛(인물 칸 11)도 같은 3×3 규칙이다 — 0x10089bf0 의 +0xb9 땅 갈래(0x10089f31~): 3×3 이 판 안, 칸마다 |높이 − 가운데 높이| ≤ 2 ·
        // 막힘 비트 1 없음 · 0x10089610 이 자신 말고 아무도 안 줌, ZOC 는 안 본다(버그 리포트 #55 — 전에는 한 칸으로 세워 서로 겹쳤다).
        bool big = c.Big || Part1Big(unit);
        // 그 칸을 차지한(큰 유닛이면 둘레 3×3 에서) 막는 유닛이 있나.
        bool Occupied(int col, int row) => host._units.Any(o => o.Alive && o.OnField && Covers(o, col, row) && Blocks(o));
        bool Enterable(int col, int row)
        {
            if (col == originCol && row == originRow) return true;
            if (flyer != null) return flyBig ? InBounds(col - 1, row - 1) && InBounds(col + 1, row + 1) : InBounds(col, row);
            if (!InBounds(col, row) || (F(col, row) & 0x9) != 0) return false;
            if (Occupied(col, row)) return false;
            // 큰 유닛(.chr 18 = 1)은 3×3 이 모두 판 안이고, 플래그 &9 가 없고, 가운데와 높이차 ≤ 2 이고, 다른 유닛이 없어야 들어간다 —
            // 이것뿐이고 <b>ZOC 는 안 본다</b>(0x100d9c7d~0x100d9e22, 땅 유닛 갈래 0x100d9d70~, 감사3 R6). 전에는 높이차를 안 보고 ZOC 를 봤다.
            if (big)
            {
                for (int by = row - 1; by <= row + 1; by++)
                    for (int bx = col - 1; bx <= col + 1; bx++)
                    {
                        if (!InBounds(bx, by) || (F(bx, by) & 0x9) != 0) return false;
                        if (Math.Abs(H(bx, by) - H(col, row)) > 2) return false;
                        if (Occupied(bx, by)) return false;
                    }
                return true;
            }
            foreach (var (px, py) in new[] { (col, row), (col - 1, row), (col + 1, row), (col, row - 1), (col, row + 1) })
            {
                // 이웃은 가운데 칸만 본다(0x10089830 은 제 칸만 0x10089610, 이웃 넷은 맵 +0x64 표를 바로 읽는다) — 큰 유닛의 가장자리 칸 옆은 ZOC 가 아니다.
                if (!InBounds(px, py) || UnitStandingAt(px, py) is not { } e || !Blocks(e)) continue;
                // 적인지는 <b>버서커(4)까지 보는</b> 편 판정으로 가른다(0x1006fde0) — 그게 걸리면 모두가 적이다.
                if (SeesAsFoe(unit, e) && Math.Abs(H(px, py) - H(col, row)) < 1) return false;
            }
            return true;
        }

        var queue = new PriorityQueue<(int Col, int Row), int>();
        if (!InBounds(originCol, originRow)) return null;   // 판 밖에 선 인물(맵이 더 큰 전투)은 이동 영역이 없다
        int start = originRow * host.Cols + originCol;
        costs[start] = 0;
        queue.Enqueue((originCol, originRow), 0);
        (int Dx, int Dy)[] dirs = [(0, -1), (1, 0), (0, 1), (-1, 0)];

        while (queue.TryDequeue(out var cell, out int cost))
        {
            if (cost > costs[cell.Row * host.Cols + cell.Col]) continue;
            foreach (var (dx, dy) in dirs)
            {
                int nx = cell.Col + dx, ny = cell.Row + dy;
                if (!Enterable(nx, ny) || (flyer == null && Math.Abs(H(cell.Col, cell.Row) - H(nx, ny)) > 2)) continue;
                int step = flyer != null
                    ? (Math.Abs(Part1FlyHeight(flyer, nx, ny) - Part1FlyHeight(flyer, cell.Col, cell.Row)) + 2) * 2
                    : part1Points > 0
                    ? (Math.Abs(H(nx, ny) - H(cell.Col, cell.Row)) + 2) * 2
                    : num5 * Math.Abs(dy) / dex + num5 * Math.Abs(dx) / dex + (num5 * Math.Abs(H(nx, ny) - H(cell.Col, cell.Row)) / dex) / 2;
                int next = cost + step;
                if (next > walkBudget || next >= costs[ny * host.Cols + nx]) continue;
                costs[ny * host.Cols + nx] = next;
                prev[ny * host.Cols + nx] = cell.Row * host.Cols + cell.Col;
                queue.Enqueue((nx, ny), next);
            }
        }
        // 파트1 — 이동력 단위 칸 값을 TP 로(올림). 걸은 만큼 TP 를 빼는 틀(CommitMove)과 좁은 예산 비교가 TP 로 본다.
        if (part1Points > 0)
            for (int i = 0; i < n; i++)
                if (costs[i] != int.MaxValue) costs[i] = (int)Math.Min(Math.Max(0, budget), ((long)costs[i] * part1Scale + part1Points - 1) / part1Points);

        // 나는 유닛이 설 수 없는 칸 — 지형 비트 2(큰 유닛은 3×3 어디든), 물체가 찍힌 칸(&8). 지나가기만 한 칸은 Prev 가 남아 길에는 든다(PathWithin).
        // 3×3 에 다른 유닛이 없어야 하는 것은 아래 「다른 인물이 선 칸」이 차지 칸으로 본다.
        if (flyer != null)
            for (int i = 0; i < n; i++)
            {
                if (costs[i] == int.MaxValue || i == start) continue;
                int col = i % host.Cols, row = i / host.Cols;
                bool bad = false;
                for (int by = flyBig ? row - 1 : row; by <= (flyBig ? row + 1 : row) && !bad; by++)
                    for (int bx = flyBig ? col - 1 : col; bx <= (flyBig ? col + 1 : col) && !bad; bx++)
                        // 지형 비트 2 는 맵 물체까지 찍은 판에서 본다 — 원본도 물체 지형이 판 지형을 덮는다(0x1009977e, Part1MapObjects).
                        bad = InBounds(bx, by) && ((F(bx, by) >> Part1Map.RawShift & 2) != 0 || (F(bx, by) & 0x8) != 0);
                if (bad) costs[i] = int.MaxValue;
            }

        // 문·스위치문 칸에는 <b>설 수</b> 없다(0x100746b0) — 닫힌 문은 도장 플래그로 이미 벽이고, 열린 문은 지나가기만 한다.
        for (int i = 0; i < n; i++)
            if (costs[i] != int.MaxValue && ObjectBlocks(i % host.Cols, i / host.Cols)) costs[i] = int.MaxValue;

        // 다른 인물이 선 칸은 파랑에서 뺀다 — 차지 칸이 겹치는 칸 모두(파트1 큰 유닛은 둘레 3×3, 0x10089bf0 → 0x10089610, 버그 리포트 #55).
        int self = Footprint(unit);
        void Shut(int col, int row, int reach)
        {
            for (int y = row - reach; y <= row + reach; y++)
                for (int x = col - reach; x <= col + reach; x++)
                {
                    int at = y * host.Cols + x;
                    if ((uint)x < host.Cols && (uint)y < host.Rows && at != start) costs[at] = int.MaxValue;
                }
        }
        foreach (var other in host._units)
            if (other != unit && other.Alive && other.OnField) Shut(other.Col, other.Row, self + Footprint(other));

        // 걸어오는 중인 다른 인물의 <b>도착 칸</b>도 뺀다 — 아직 출발 쪽 칸에 서 있어 위에서 안 잡힌다. 대장을 따라 걷는 부하가
        // 다 서기 전에 다음 유닛이 그 칸을 골라 둘이 한 칸에 겹쳐 섰다(사용자 보고: 적이 한 칸에 겹친다, ba-22).
        foreach (var walker in host._units)
        {
            if (walker == unit || !walker.Alive || !walker.OnField || walker.Path.Count == 0) continue;
            var (lc, lr) = walker.Path.Last();
            Shut(lc, lr, self + Footprint(walker));
        }
        // 순간이동꾼(이동 종류 1)은 길을 비우고 도착 칸을 따로 들고 있다 — 칸을 옮기기 전(단계 0·1) 31틱 동안 같은 구멍이 있었다.
        foreach (var (blinker, b) in _blinks)
            if (blinker != unit && b.Stage < 2) Shut(b.Dest.Col, b.Dest.Row, self + Footprint(blinker));

        // 붉은 칸 — 갈 수 있는 칸마다 <b>그 인물의 기본공격 모양</b>을 칠한다(0x100749b0).
        // 예전에는 「정확히 두 칸 상하좌우」로 박아 두어 높이·시야가 빠졌다 — 한 층만 달라도 사거리가 달라진다.
        if (redAllowed && Work(c.BasicWorkId) is { } basic)
        {
            // 훑는 창은 최대/2 칸 — 원본 모양 함수가 그만큼 네모를 훑는다(0x100db6d4~0x100db6df, 0x100dc63c~). 내리막 차등(−2/층)으로
            // 사거리 칸 수보다 먼 칸도 켜질 수 있어서다. 전에는 최대/4 라 그런 칸이 빨강에서 빠지고 겨눔(CanAimAt)과 어긋났다(감사3 R7).
            int max = RangeMaxOf(basic, unit), reach = Math.Max(1, max / 2);
            // 파트1 — 사거리 칸 인자(모양 · 최소 · 최대 · 높이 · 시야)는 이 인물 · 이 기본 공격에서 늘 같으므로 한 번만 꺼내고, 모양이 덮는 어긋남(dx, dy)도
            // 한 번만 추려 둔다(사용자 요청: 사거리 모양을 미리 계산). 칸마다 하던 InWorkRange 는 매번 인물 · 기술 표를 다시 찾고 무기 사거리를 다시 셈해
            // 사거리 긴 인물(록슬리)은 이 고리가 70ms 넘게 걸렸다. 판정 자체는 InWorkRange 의 파트1 갈래와 같다.
            if (!basic.SelfCentred && basic.RangeShape != 0 && Part1AimOf(basic, unit) is { } aim1)   // InWorkRange 와 같은 차례 — 자기 자리 · 모양 0 은 아래 일반 고리로
            {
                var offsets = new List<(int Dx, int Dy)>();
                for (int dy = -reach; dy <= reach; dy++)
                    for (int dx = -reach; dx <= reach; dx++)
                        if ((dx != 0 || dy != 0) && aim1.RangeShapeCovers(dx, dy)) offsets.Add((dx, dy));
                for (int i = 0; i < n; i++)
                {
                    if (costs[i] == int.MaxValue || costs[i] > narrow) continue;
                    int col = i % host.Cols, row = i / host.Cols, fromH = HeightAt(col, row);
                    foreach (var (dx, dy) in offsets)
                    {
                        int ax = col + dx, ay = row + dy;
                        if (!InBounds(ax, ay)) continue;
                        int j = ay * host.Cols + ax;
                        if (costs[j] != int.MaxValue || red[j]) continue;
                        if (host._map is not null && (CellFlagsAt(ax, ay) & 0x8) != 0
                            && !(ObjectAt(ax, ay) is { Data.Breakable: true, Alive: true } aimed && !_opened.Contains(aimed))) continue;
                        if (aim1.RangeSameHeight && HeightAt(ax, ay) != fromH) continue;
                        var (d1, d2) = WorkDistance(col, row, ax, ay, Part1RangeHeight(aim1, unit), aim1.RangeGraded,
                                                    Part1AimHeight(unit, col, row), Part1TargetHeight(aim1, ax, ay));
                        if (d1 > max || d2 < aim1.RangeMin) continue;
                        if (!aim1.RangeSight || HasSight(col, row, ax, ay)) red[j] = true;
                    }
                }
            }
            else
            for (int i = 0; i < n; i++)
            {
                if (costs[i] == int.MaxValue || costs[i] > narrow) continue;   // 빨강은 좁은 예산 안에서만
                int col = i % host.Cols, row = i / host.Cols;
                for (int ay = row - reach; ay <= row + reach; ay++)
                    for (int ax = col - reach; ax <= col + reach; ax++)
                    {
                        if (!InBounds(ax, ay)) continue;
                        int j = ay * host.Cols + ax;
                        if (costs[j] != int.MaxValue || red[j]) continue;
                        if (InWorkRange(basic, col, row, ax, ay, unit)) red[j] = true;
                    }
            }
        }
        return new MoveRange(costs, prev, red) { Width = host.Cols };
    }

    /// <summary>파트1 판에서 나는 유닛이면 그 인물 레코드(탈것이면 탈것의 것) — 칸 12(유닛 <c>+0xba</c>) ≠ 0. 아니면 null.</summary>
    internal Part1Chr? Part1Flyer(UnitState u) =>
        host._scene.IsPart1 && Part1.Fighter(u)?.Chr is { } c && c.Fields[Part1Chr.FlyField] != 0 ? c : null;

    /// <summary>
    /// 나는 유닛의 높이(<c>0x10038090</c> 의 +0xba 갈래) — 칸 높이 A(큰 유닛 +0xb9 면 3×3 가운데 가장 높은 것)에
    /// max(칸 13 − A/2, 칸 14) 를 더한 값(바이트). 자료는 모두 칸 13 &lt; 칸 14(4·8, 아지 12·20)라 늘 A + 칸 14 — 걸음 비용의 높이 차는 땅 유닛과 같다.
    /// </summary>
    internal int Part1FlyHeight(Part1Chr c, int col, int row)
    {
        int a = WalkHeightAt(col, row) & 0xFF;
        if (c.Fields[Part1Chr.BigField] != 0)
            for (int y = row - 1; y <= row + 1; y++)
                for (int x = col - 1; x <= col + 1; x++)
                    if ((uint)x < host.Cols && (uint)y < host.Rows) a = Math.Max(a, WalkHeightAt(x, y) & 0xFF);
        return (a + Math.Max(c.Fields[Part1Chr.FlyBaseField] - (a >> 1), c.Fields[Part1Chr.FlyMinField])) & 0xFF;
    }

    /// <summary>파트1 유닛의 높이 z(유닛 <c>+0x3a</c> / 20) — 나는 유닛은 나는 높이, 땅 유닛은 칸 높이. AI 거리(0x10028e00)가 쓴다.</summary>
    internal int Part1Z(UnitState u) => Part1Flyer(u) is { } c ? Part1FlyHeight(c, u.Col, u.Row) : WalkHeightAt(u.Col, u.Row);

    /// <summary>
    /// 파트1 SPD(<c>0x1003ece0</c>) — 바탕(칸 25 + 어빌리티 7 × 5 + 장비) + 스피디(상태 7, 강화 17) 8 + 지형 적응(<see cref="Part1Adapted"/>) 8,
    /// 온도(유닛 +0x4a)가 0 아래면 그만큼 뺀다(⌊60·t/60⌋ = t). 0 아래는 0. 이동력(× 2) · 동결 검사가 쓴다.
    /// </summary>
    /// <param name="heat">거짓이면 온도를 빼지 않은 값(기록용).</param>
    internal int Part1Spd(UnitState u, bool heat = true)
    {
        if (Part1.Fighter(u) is not { } f) return 0;
        int spd = f.Stat(Part1Chr.Spd) + (Part1.Has(u, 17) ? 8 : 0) + (Part1Adapted(u) ? 8 : 0);
        int t = heat ? Part1Objects.UnitHeat(u) : 0;
        if (t < 0) spd += t;
        return Math.Max(0, spd);
    }

    /// <summary>전장 지형(머리 낱말 7 = 1~6)에 맞는 적응 어빌리티(86 사막 · 87 정글 · 88 동굴 · 89 산지 · 90 극지 · 91 바다 적응)를 쓰는가(<c>0x10042930</c>).</summary>
    internal bool Part1Adapted(UnitState u) =>
        host._scene.Part1Terrain is >= 1 and <= 6 && Part1.Fighter(u)?.Chr.EffectiveLevel(85 + host._scene.Part1Terrain) >= 0;

    /// <summary>
    /// 이동 영역(파랑) 안에서만 밟아 (fromCol, fromRow) 에서 목표 칸까지 가는 가장 짧은 길(출발 칸 뺌). 못 가면 null.
    /// </summary>
    internal List<(int Col, int Row)>? PathWithin(MoveRange range, int fromCol, int fromRow, int target)
    {
        int n = host.Cols * host.Rows, from = fromRow * host.Cols + fromCol;
        if (from == target) return [];
        var prev = new int[n];
        Array.Fill(prev, -2);
        prev[from] = -1;
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.TryDequeue(out int i))
        {
            int col = i % host.Cols, row = i / host.Cols;
            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
            {
                int nx = col + dx, ny = row + dy, j = ny * host.Cols + nx;
                if ((uint)nx >= host.Cols || (uint)ny >= host.Rows || prev[j] != -2) continue;
                // 설 수는 없어도 지나갈 수 있는 칸(지나가도 되는 아군이 선 칸·열린 문 — 영역을 셀 때 밟고 지나간 칸이라 Prev 가 있다)은 길에 넣는다.
                // 전에는 그런 칸을 길에서 빼서, 아군 너머의 파란 칸으로 못 걷고 그 칸에서만 닿는 적은 빨간 칸인데도 못 쳤다(사용자 보고: Btl 0298 크리스티앙).
                if (!range.CanReach(j) && !(j != target && range.Prev[j] >= 0)) continue;
                prev[j] = i;
                if (j == target)
                {
                    var path = new List<(int Col, int Row)>();
                    for (int k = j; k != from; k = prev[k]) path.Add((k % host.Cols, k / host.Cols));
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
    internal static int WaveRadius(int frames)
    {
        int r = 0;
        for (int i = 0; i < frames && r < 64; i++) r += Math.Max(1, r * 2 / 3);
        return r;
    }

    /// <summary>걸어가 손댈 수 있는 물체 칸(이동 영역과 함께 칠함) — 무엇으로 셈했는지와 함께 들고 있는다.</summary>
    internal readonly HashSet<int> _touchable = [];
    internal (object? Range, int Turn, bool Mine, int Objects) _touchableFor;

    /// <summary>시험용 DUELDX_HEIGHTS=1 — 칸마다 걷기 높이 숫자를 그 칸 그린 자리(높이만큼 올린 곳)에 띄운다. 지형 그림과 높이 자료가 맞는지 볼 때.</summary>
    private static readonly bool ShowHeights = Environment.GetEnvironmentVariable("DUELDX_HEIGHTS") == "1";

    internal void DrawMoveRange()
    {
        if (ShowHeights && host.BoardIsMap)
            for (int r = 0; r < host.Rows; r++)
                for (int c = 0; c < host.Cols; c++)
                {
                    int top = host.CellTop(c, r);
                    host.UiStroke(c * TileW, top, TileW, TileH, 0x80FFFFFF);
                    host.UiText($"{WalkHeightAt(c, r)}", c * TileW + 14, top + 8, (CellFlagsAt(c, r) & 1) != 0 ? 0xFFFF4040 : 0xFFFFFF00, 11);   // 빨강 = 물체까지 찍은 판에서 못 들어가는 칸
                }
        if (_rangeUnit < 0 || _range is not { } range) return;
        // 판 크기가 바뀐 뒤(다음 전투·필드로 넘어간 틀) 옛 판으로 셈한 영역이 남아 있으면 칸 수가 안 맞아 배열 밖을 읽었다(사용자 보고, 튕김) — 버린다.
        if (range.Cost.Length != host.Cols * host.Rows || range.Red.Length != host.Cols * host.Rows) { _range = null; _rangeUnit = -1; return; }
        // 어빌리티 대상을 고르는 중(원본 상태 11)에는 이동 영역(파랑)·공격 사거리(빨강)를 걷고 그 기술의 사거리·효과 범위만 깐다 —
        // 겹쳐 칠하면 사거리 칸이 묻혀 안 보였다(사용자 보고). 기본공격 겨냥은 예전처럼 둔다.
        if (_targetWork >= 0 && !_targetIsBasicAttack) return;
        int radius = WaveRadius((int)((host._lastTime - _rangeStart) * TicksPerSecond));
        // 차례인 인물의 영역이면 걸어가 손댈 수 있는 물체 칸도 칠한다(원본 상태 10 이 층 1 을 함께 만든다, 0x1006961c).
        // 물체마다 길찾기를 하므로 틀마다 다시 세지 않는다 — 같은 영역·같은 차례·같은 물체 수인 동안은 앞 틀의 것을 쓴다
        // (전에는 내 차례에 이동 영역이 떠 있는 내내 한 틀에 40ms 를 먹었다).
        bool mine = _rangeUnit == _turn && IsPlayerTurn;
        int liveObjects = Objects.Count(o => o.Alive) + (host._scene.IsPart1 ? 1000 * Part1Objects.OpenedChests : 0);
        if (!ReferenceEquals(range, _touchableFor.Range) || _touchableFor.Turn != _turn || _touchableFor.Mine != mine || _touchableFor.Objects != liveObjects)
        {
            _touchable.Clear();
            if (mine)
                foreach (var obj in Objects)
                    if (FindTouchPath(host._units[_turn], obj, range) != null)
                        foreach (var (fc, fr, _, _) in FootprintCells(obj))   // 발자국 칸 모두(여러 칸짜리 문)
                            if ((uint)fc < host.Cols && (uint)fr < host.Rows && ObjectAt(fc, fr) == obj) _touchable.Add(fr * host.Cols + fc);
            if (mine && host._scene.IsPart1)
                foreach (var cell in Part1Objects.TouchableCells(host._units[_turn], range)) _touchable.Add(cell);
            _touchableFor = (range, _turn, mine, liveObjects);
        }
        var touchable = _touchable;
        // 파트1 — 앞 칸에 통째로 덮여 고를 수 없는 칸은 안 칠한다(덮은 앞 칸 그림 위에 뒤 칸 파랑이 깔려 「벽 위로 간다」처럼 보였다, fg-82).
        bool occlude = host._scene.IsPart1 && host.BoardIsMap;
        var edges = EdgeCells();

        for (int row = 0; row < host.Rows; row++)
            for (int col = 0; col < host.Cols; col++)
            {
                if (Math.Abs(col - _rangeCol) + Math.Abs(row - _rangeRow) > radius) continue;
                int i = row * host.Cols + col;
                // 대상을 딱히 고르는 중이 아니어도 사거리 빨강은 늘 파랑과 함께 뜬다 — 사용자가 원본에서
                // 직접 본 그대로다("aiming일 때만 빨강"으로 좁혔던 이전 판단은 상태 번호를 오독한 것으로 보인다:
                // 같은 파일 안에서도 상태 12를 "어빌리티"(97줄 언저리)와 "그냥 걷기"(여기)로 서로 다르게 적어 놨었다).
                uint layer = touchable.Contains(i) ? ObjectLayer : range.Cost[i] != int.MaxValue ? MoveLayer : range.Red[i] ? RangeLayer : 0;
                if (layer == 0 || occlude && Part1Occlusion.Hidden(col, row)) continue;
                PaintCell(col, row, layer);
                // 테두리 집합 — 이동(손댈 물체 칸도 걸어가 닿으니 이동 쪽) 1 · 사거리 2.
                if (edges != null) edges[i] = (byte)(layer == RangeLayer ? 2 : 1);
            }
        if (edges != null)
        {
            DrawRangeEdges(edges, 1, 0, MoveEdge);
            DrawRangeEdges(edges, 2, 1, RangeEdge);   // 사거리는 파랑과 닿은 쪽에는 안 긋는다 — 바깥 가장자리만
        }
    }

    /// <summary>범위 테두리 색 — 이동 파랑 · 사거리 주황(모드 &gt; 가독성 향상 &gt; 범위 테두리, 원본에 없음).</summary>
    internal const uint MoveEdge = 0xFF3C8CFF, RangeEdge = 0xFFFF8C1E, EdgeShade = 0xC0000000;

    /// <summary>테두리 집합을 담는 칸 표 — 틀마다 새로 만들지 않는다.</summary>
    private byte[] _edgeCells = [];

    /// <summary>범위 테두리를 켰으면 판 크기의 빈 칸 표를, 껐으면 null.</summary>
    internal byte[]? EdgeCells()
    {
        if (!host._rangeEdges) return null;
        int n = host.Cols * host.Rows;
        if (_edgeCells.Length != n) _edgeCells = new byte[n];
        else Array.Clear(_edgeCells);
        return _edgeCells;
    }

    /// <summary>
    /// 범위 집합의 경계 변에만 2px 선(안쪽)과 1px 어두운 외곽선(바깥)을 긋는다 — 칸 표에서 값이 <paramref name="kind"/> 인 칸의 네 변 가운데
    /// 이웃이 같은 값도 <paramref name="alsoInside"/> 도 아닌 변(판 밖 포함). 비탈 칸은 네 모서리 높이로 기운 변을 긋는다(PaintCell 과 같은 꼭짓점).
    /// 어두운 외곽선을 먼저 다 긋고 색 선을 얹어, 이웃 변의 외곽선이 색 선을 덮지 않게 한다. 모드 &gt; 가독성 향상 &gt; 범위 테두리(원본에 없음).
    /// </summary>
    internal void DrawRangeEdges(byte[] cells, byte kind, byte alsoInside, uint color)
    {
        int cols = host.Cols, rows = host.Rows;
        bool Out(int c, int r) => (uint)c >= cols || (uint)r >= rows || cells[r * cols + c] is var k && k != kind && (alsoInside == 0 || k != alsoInside);
        for (int pass = 0; pass < 2; pass++)
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                {
                    if (cells[row * cols + col] != kind) continue;
                    bool n = Out(col, row - 1), s = Out(col + 0, row + 1), w = Out(col - 1, row), e = Out(col + 1, row);
                    if (n || s || w || e) StrokeCellSides(col, row, n, s, w, e, pass == 0 ? EdgeShade : color, shade: pass == 0);
                }
    }

    /// <summary>
    /// 칸의 고른 변에 선을 긋는다 — <paramref name="shade"/> 면 변 바로 바깥 1px, 아니면 변에서 안쪽으로 2px.
    /// </summary>
    internal void StrokeCellSides(int col, int row, bool n, bool s, bool w, bool e, uint color, bool shade)
    {
        if (col * TileW > host._camX + host.ViewWidth + 2 || (col + 1) * TileW < host._camX - 2) return;
        var (x0, x1, yNw, yNe, ySw, ySe) = CellCorners(col, row);
        if (Math.Max(ySw, ySe) < host._camY - 2 || Math.Min(yNw, yNe) > host._camY + host.ViewHeight + 2) return;
        // 선 자리 — 바깥 1px(그림자)은 −1, 안쪽 2px 는 0 · +1(변에서 칸 안쪽으로).
        int[] offs = shade ? [-1] : [0, 1];
        foreach (int o in offs)
        {
            if (n) Segment(x0, yNw + o, x1, yNe + o, color);
            if (s) Segment(x0, ySw - o, x1, ySe - o, color);
            if (w) Segment(x0 + o, yNw, x0 + o, ySw, color);
            if (e) Segment(x1 - o, yNe, x1 - o, ySe, color);
        }
    }

    /// <summary>칸의 네 꼭짓점 — 평지는 40×32 네모(오른쪽 · 아래 변은 이웃과 나눠 쓰는 줄), 비탈은 모서리 높이로 올린 자리(<see cref="PaintCell"/> 과 같다).</summary>
    internal (int X0, int X1, int YNw, int YNe, int YSw, int YSe) CellCorners(int col, int row)
    {
        int x0 = col * TileW, x1 = x0 + TileW;
        if (!host.BoardIsMap || host._map!.SlopeAt(col, row) == 0)
        {
            int y = host.CellTop(col, row);
            return (x0, x1, y, y, y + TileH, y + TileH);
        }
        int baseTop = GridTop + host.BoardPad + row * TileH, baseBottom = baseTop + TileH;
        return (x0, x1, baseTop - CornerPx(host._map.CornerAt(col, row, 1)), baseTop - CornerPx(host._map.CornerAt(col, row, 2)),
                baseBottom - CornerPx(host._map.CornerAt(col, row, 3)), baseBottom - CornerPx(host._map.CornerAt(col, row, 0)));
    }

    /// <summary>브레젠험 선 — 판 밖은 SetPixel 이 거른다.</summary>
    private void Segment(int x0, int y0, int x1, int y1, uint color)
    {
        if (y0 == y1) { host.FillRect(Math.Min(x0, x1), y0, Math.Abs(x1 - x0) + 1, 1, color); return; }
        if (x0 == x1) { host.FillRect(x0, Math.Min(y0, y1), 1, Math.Abs(y1 - y0) + 1, color); return; }
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, err = dx + dy;
        for (int guard = 0; guard < 4096; guard++)
        {
            host.SetPixel(x0, y0, color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    // ── 밝은 칸 범위 진하게(모드 > 가독성 향상, 원본에 없음) ──
    // 원본은 범위 칸을 가산(섞기 방식 17)으로만 밝혀, 바탕이 거의 흰 눈밭(파트1 Btl 20031 빙원 따위)에서는 채널이 255 에 붙어 칸이 안 보였다.
    // 가산이 잘리는(포화하는) 만큼만 곱하기(바탕 × 층색/255)로 넘어간다 — 잘림이 없는 점은 원본 가산 그대로(비트 단위로 같다).
    // 곱하기는 바탕의 결 · 그림자를 그대로 두고 색만 입히니, 흰 눈은 층색 그대로 물들고 반투명 판을 덮은 느낌이 안 난다.

    /// <summary>
    /// 한 점의 곱하기 쪽 가중치(0~256) — 가산 tint 가 그 점에서 잘려 나가는 몫의 채널 최대값: max_c( max(0, 바탕_c + tint_c − 255) / tint_c ).
    /// 잘림이 없으면 0(원본 가산 그대로), tint 를 다 잃으면 256(곱하기만).
    /// </summary>
    private static int ClipWeight(uint c, uint tr, uint tg, uint tb)
    {
        int w = 0;
        int r = (int)(c >> 16 & 0xFF) + (int)tr - 255, g = (int)(c >> 8 & 0xFF) + (int)tg - 255, b = (int)(c & 0xFF) + (int)tb - 255;
        if (r > 0 && tr > 0) w = Math.Max(w, r * 256 / (int)tr);
        if (g > 0 && tg > 0) w = Math.Max(w, g * 256 / (int)tg);
        if (b > 0 && tb > 0) w = Math.Max(w, b * 256 / (int)tb);
        return Math.Min(256, w);
    }

    /// <summary>칸마다 바탕(맵 그림)의 평균 곱하기 가중치(−1 = 아직 안 셈) — tint 마다 따로, 맵이 바뀌면 다시 만든다.</summary>
    private readonly Dictionary<uint, short[]> _cellBlend = [];
    private object? _cellBlendMap;

    /// <summary>
    /// 칸의 곱하기 가중치(0~256) — 맵 그림에서 그 칸(높이만큼 올린 40×32) 점들의 <see cref="ClipWeight"/> 평균 p 를
    /// smoothstep(0, <see cref="BlendFullAt"/>, p) × <see cref="BlendMax"/> 로 편 값. 칸마다 tint 마다 한 번 센다.
    /// 칸 안에서는 한 값으로 섞어 눈의 결 · 그림자의 밝고 어두움이 뒤집히지 않게 한다 — 점마다 가중치를 달리해 봤더니 밝은 점일수록 더 곱해져
    /// 어두워져서 눈결이 얼룩처럼 뒤집혔다. 잘리는 점이 없는 칸은 0 이라 원본 가산과 비트 단위로 같다. 칸 테두리선이 칸 사이 경계를 가린다.
    /// smoothstep 은 잘림이 조금뿐인 칸(풀밭의 밝은 점 몇 개)은 거의 그대로 두고, 눈밭(이동 파랑 p ≈ 0.85, 사거리 주황 p ≈ 0.5 — 눈이 푸른 기가 돌아
    /// 빨강 채널이 덜 잘린다)은 곱하기 쪽으로 다 넘긴다.
    /// </summary>
    internal int CellBlendWeight(int col, int row, uint tint)
    {
        if (!host.BoardIsMap || host._map is not { } map) return 0;
        int n = host.Cols * host.Rows;
        if (!ReferenceEquals(_cellBlendMap, map)) { _cellBlend.Clear(); _cellBlendMap = map; }
        if (!_cellBlend.TryGetValue(tint, out var table) || table.Length != n)
        {
            _cellBlend[tint] = table = new short[n];
            Array.Fill(table, (short)-1);
        }
        int i = row * host.Cols + col;
        if (table[i] >= 0) return table[i];
        uint tr = tint >> 16 & 0xFF, tg = tint >> 8 & 0xFF, tb = tint & 0xFF;
        int top = host.CellTop(col, row) - GridTop - map.OriginY - host.BoardPad;
        long sum = 0;
        int count = 0;
        for (int y = Math.Max(0, top); y < Math.Min(map.Height, top + TileH); y += 2)
            for (int x = Math.Max(0, col * TileW); x < Math.Min(map.Width, col * TileW + TileW); x += 2)
            {
                int o = (y * map.Width + x) * 4;
                sum += ClipWeight((uint)map.Bgra[o + 2] << 16 | (uint)map.Bgra[o + 1] << 8 | map.Bgra[o], tr, tg, tb);
                count++;
            }
        double p = count == 0 ? 0 : Math.Min(1, sum / (double)count / 256 / BlendFullAt);
        int w = (int)Math.Round(p * p * (3 - 2 * p) * BlendMax * 256);
        return table[i] = (short)(w < BlendMin ? 0 : w);   // 잘림이 아주 조금인 칸(밝은 흙길 따위)은 원본 가산 그대로
    }

    /// <summary>
    /// 칸 평균 잘림 몫이 <see cref="BlendFullAt"/> 이상이면 가중치가 <see cref="BlendMax"/> 에 닿는다(<see cref="CellBlendWeight"/>).
    /// 곱하기만 쓰면 흰 눈이 층색 그대로(파랑 (88,93,245))라 칠한 판처럼 납작했다 — 가산 몫을 30% 남겨 눈의 밝은 기를 살린다.
    /// </summary>
    internal const double BlendFullAt = 0.6, BlendMax = 0.7;

    /// <summary>가중치가 이보다 작으면(256 분의) 0 으로 — 차이가 1 단계도 안 나는 섞기로 원본과 어긋나지 않게.</summary>
    internal const int BlendMin = 8;

    /// <summary>
    /// 섞기 표 — 채널마다 바탕 0~255 → 결과(가산 × (256 − w) + 곱하기 × w) / 256, R · G · B 를 이어 768 칸. (tint, 층색, w) 마다 한 번 만든다.
    /// 점마다는 표 세 번 찾기라 원본 가산과 값이 거의 같다.
    /// </summary>
    private readonly Dictionary<(uint Tint, uint Layer, int W), byte[]> _blendLut = [];

    private byte[] BlendLut(uint tint, uint layer, int w)
    {
        if (_blendLut.TryGetValue((tint, layer, w), out var lut)) return lut;
        if (_blendLut.Count > 4096) _blendLut.Clear();
        lut = new byte[768];
        for (int ch = 0; ch < 3; ch++)
        {
            int shift = 16 - 8 * ch;
            uint t = tint >> shift & 0xFF, l = layer >> shift & 0xFF;
            uint l256 = l + (l >> 7);   // 0~255 → 0~256 — 흰 층(255)은 바탕을 그대로 둔다
            for (uint c = 0; c < 256; c++)
            {
                uint a = Math.Min(255, c + t), m = c * l256 >> 8;
                lut[ch * 256 + c] = (byte)((a * (uint)(256 - w) + m * (uint)w + 128) >> 8);
            }
        }
        return _blendLut[(tint, layer, w)] = lut;
    }

    /// <summary>
    /// 칸 밝기 10~90 — 원본 <c>0x10030cc0</c> 이 Obt 를 읽은 뒤 칸마다 넣는 값. 평지는 74.
    /// 비탈은 북서 모서리에서 북동·남서로의 기울기 벡터 (−40·Δ동, −40·Δ남, 1600) 로 빛을 셈한다:
    /// <c>((1920·b + 4096000) / 40) / |(a, b, 1600)|</c> 를 80 에서 자르고 10 을 더한다(a = −40·(북동−북서), b = −40·(남서−북서), 16비트로 접힘).
    /// </summary>
    internal int CellBrightness(int col, int row)
    {
        if (!host.BoardIsMap || host._map!.SlopeAt(col, row) is 0 or > 3) return 74;
        int nw = host._map.CornerAt(col, row, 1), ne = host._map.CornerAt(col, row, 2), sw = host._map.CornerAt(col, row, 3);
        int a = (short)(-40 * (ne - nw)), b = (short)(-40 * (sw - nw));
        int len = (int)Math.Sqrt((double)a * a + (double)b * b + 2560000.0);
        int v = (15 * b * 128 + 4096000) / 40 / Math.Max(1, len);
        return Math.Max(10, Math.Min(80, v) + 10);
    }

    /// <summary>모서리 높이(원 단위) → 화면 픽셀: 원본은 <c>높이 × 12 / 20</c>(0x100d7bf3, 0 쪽으로 자름).</summary>
    internal static int CornerPx(int raw) => raw * 12 / 20;

    /// <summary>
    /// 칸 하나를 층 색으로 칠한다 — 원본처럼 ① 층색 × 칸밝기/256 을 <b>더해서</b> 채우고 ② 같은 색 불투명으로 테두리(41×33, 이웃과 선을 나눠 쓴다).
    /// 평지는 40×32 네모. 비탈 칸은 네 모서리 높이(북서·북동·남서·남동)로 꼭짓점을 올린 사각형이다 —
    /// 원본은 비탈 모양 3 을 「윗변 북서·아랫변 남서 높이의 네모」로, 1·2 는 선·삼각형 조각으로 그리는데(0x100d7ba3·0x100d7dcf·0x100d820e)
    /// 조각 표까지는 못 옮겨 네 꼭짓점 사각형으로 근사한다(가설).
    /// </summary>
    internal void PaintCell(int col, int row, uint layer) => PaintCell(col, row, layer, mark: false);

    /// <summary>
    /// 모드 &gt; 가독성 향상 &gt; 밝은 칸 범위 진하게면 가산이 포화하는 칸은 곱하기 쪽으로 섞는다(<see cref="CellBlendWeight"/>).
    /// <paramref name="mark"/> 면 테두리 없이 칸 안만 물들이고 그 옵션과 상관없이 늘 섞는다(얼음 · 땅불 칸 표시, <see cref="Part1MapObjects"/>).
    /// </summary>
    internal void PaintCell(int col, int row, uint layer, bool mark)
    {
        // 화면에 안 보이는 칸은 안 칠한다 — 판 전체가 한 장의 그림이라 전에는 카메라 밖 칸까지 틀마다 다 칠했다. 넓은 맵에서 TP 가 많은
        // 인물의 차례면 이동 · 사거리 칸이 수천이라 한 틀에 12ms 를 먹어 내 차례만 되면 느려졌다(사용자 보고 fa-60). 비탈로 솟는 몫만큼 위아래로 넉넉히 본다.
        if (col * TileW > host._camX + host.ViewWidth || (col + 1) * TileW < host._camX) return;
        int cellTop = host.CellTop(col, row);
        if (cellTop > host._camY + host.ViewHeight + 2 * TileH || cellTop + TileH < host._camY - 2 * TileH) return;
        int bright = CellBrightness(col, row);
        uint tint = (layer >> 16 & 0xFF) * (uint)bright / 256 << 16 | (layer >> 8 & 0xFF) * (uint)bright / 256 << 8 | (layer & 0xFF) * (uint)bright / 256;
        // 곱하기 가중치 — 끔이거나 잘림이 없는 칸이면 0(원본 가산 그대로), 아니면 섞기 표.
        int blend = !host._brightCellFill && !mark ? 0 : CellBlendWeight(col, row, tint);
        byte[]? lut = blend > 0 ? BlendLut(tint, layer, blend) : null;
        int x0 = col * TileW, x1 = x0 + TileW;
        int baseTop = GridTop + host.BoardPad + row * TileH, baseBottom = baseTop + TileH;
        if (!host.BoardIsMap || host._map!.SlopeAt(col, row) == 0)
        {
            int y = host.CellTop(col, row);
            AddRect(x0, y, TileW, TileH, tint, lut);
            if (!mark) host.StrokeRect(x0, y, TileW + 1, TileH + 1, 0xFF000000 | tint);
            return;
        }
        int yNw = baseTop - CornerPx(host._map.CornerAt(col, row, 1)), yNe = baseTop - CornerPx(host._map.CornerAt(col, row, 2));
        int ySw = baseBottom - CornerPx(host._map.CornerAt(col, row, 3)), ySe = baseBottom - CornerPx(host._map.CornerAt(col, row, 0));
        AddQuad(x0, x1, yNw, yNe, ySw, ySe, tint, lut);
        if (mark) return;
        uint line = 0xFF000000 | tint;
        host.Mos.DrawSegment(x0, yNw, x1, yNe, line);
        host.Mos.DrawSegment(x0, ySw, x1, ySe, line);
        host.Mos.DrawSegment(x0, yNw, x0, ySw, line);
        host.Mos.DrawSegment(x1, yNe, x1, ySe, line);
    }

    /// <summary>윗변(yNw→yNe)과 아랫변(ySw→ySe)이 기운 사각형 안을 색을 더해 밝힌다 — 세로줄마다 두 변 사이를 채운다(섞기는 <see cref="AddRect"/> 와 같다).</summary>
    internal void AddQuad(int x0, int x1, int yNw, int yNe, int ySw, int ySe, uint tint, byte[]? lut = null)
    {
        int w = Math.Max(1, x1 - x0);
        for (int x = x0; x < x1; x++)
        {
            double t = (x - x0) / (double)w;
            int top = (int)Math.Round(yNw + (yNe - yNw) * t), bottom = (int)Math.Round(ySw + (ySe - ySw) * t);
            if (bottom > top) AddRect(x, top, 1, bottom - top, tint, lut);
        }
    }

    /// <summary>
    /// 네모 안을 색을 <b>더해서</b> 밝힌다(255 에서 멈춘다) — 원본 섞기 방식 17.
    /// <paramref name="lut"/>(<see cref="BlendLut"/>)이 있으면 가산과 곱하기를 섞은 표로 바꾼다(모드 &gt; 가독성 향상).
    /// </summary>
    internal void AddRect(int x, int y, int w, int h, uint tint, byte[]? lut = null)
    {
        uint tr = tint >> 16 & 0xFF, tg = tint >> 8 & 0xFF, tb = tint & 0xFF;
        // 네모를 판 안으로 먼저 자르고 판 크기 · 버퍼를 한 번만 읽는다 — 전에는 픽셀마다 경계와 판 너비를 다시 셈했다(이동 영역이 떠 있는 내내 드는 값이다, fa-60).
        int bw = host.BoardWidth, bh = host.BoardHeight;
        int x0 = Math.Max(0, x), x1 = Math.Min(bw, x + w), y0 = Math.Max(0, y), y1 = Math.Min(bh, y + h);
        if (x0 >= x1 || y0 >= y1) return;
        var fb = host._fb;
        for (int yy = y0; yy < y1; yy++)
        {
            int i = yy * bw + x0;
            if (lut != null)
            {
                for (int n = x1 - x0; n > 0; n--, i++)
                {
                    uint c = fb[i];
                    fb[i] = c & 0xFF000000 | (uint)lut[c >> 16 & 0xFF] << 16 | (uint)lut[256 + (c >> 8 & 0xFF)] << 8 | lut[512 + (c & 0xFF)];
                }
                continue;
            }
            for (int n = x1 - x0; n > 0; n--, i++)
            {
                uint c = fb[i];
                fb[i] = c & 0xFF000000
                      | Math.Min(255, (c >> 16 & 0xFF) + tr) << 16
                      | Math.Min(255, (c >> 8 & 0xFF) + tg) << 8
                      | Math.Min(255, (c & 0xFF) + tb);
            }
        }
    }
}
