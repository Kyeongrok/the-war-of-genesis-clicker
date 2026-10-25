using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 소환 — 헬카이트(345) · 어비스 네레이드(346) · 머스펠 헤임(347) · 천수관음(348) · 타이타니아(349) · 묵호자(350)(사용자 요청 menu-89).
/// 원작에서는 「소환 능력」(어빌리티 119)이 있는 직업이 그 소환수의 옷(드래곤 하트 · 펄 슈츠 …)을 입으면 쓴다. 여기서는 캐릭터 에디터로 붙인다.
/// </summary>
/// <remarks>
/// <para>앞머리는 모두 <see cref="SummonPrep"/>(54틱). 핸들러: 345 <c>0x10060780</c> · 346 <c>0x1005c040</c> · 347 <c>0x10046c70</c> ·
/// 348 <c>0x10066570</c> · 349 <c>0x10066f40</c> · 350 <c>0x10067ab0</c>(볼트 분석-파트1-소환).</para>
/// <para>원본과 다르게 한 것: 헬카이트는 원본에서 시전자가 대상 칸까지 돌진해 그 칸에 남는데, 여기서는 돌진 뒤 제자리로 돌아온다(칸이 겹친다).
/// 타이타니아의 번개 백 개는 원본에서 한 사람이 여러 번 맞을 수 있지만 여기서는 사람마다 처음 맞는 한 번만 든다(다른 기술과 같이).
/// 화면을 늘여 그리는 그림(타이타니아의 빛 틈)은 빛 막대로, 화면을 멈추고 하얗게 하는 것은 멈추지 않는 하얀 덮개로 갈음했다.</para>
/// </remarks>
internal sealed partial class Part1Skill
{
    internal const int HellKiteWork = 20345, NereidWork = 20346, MuspelWork = 20347, KannonWork = 20348, TitaniaWork = 20349, BlackFoxWork = 20350;

    /// <summary>
    /// 폭풍검(공격 287, 스탐블링거) · 비연참(294) — 처음에 소환 핸들러로 잘못 읽은 것(0x1005c040 · 0x10060780)이 실제로는 이 둘의 핸들러였다(menu-92 —
    /// 공격 번호 0x150 위는 다른 표 0x10043684 로 간다). 시각표는 그대로 두고 제 이름의 기술로 붙인다. 앞머리는 검 기술 것(종류 1).
    /// </summary>
    internal const int StormSwordWork = 20287, FlyingSlashWork = 20294;

    private (double X, double Y) CellFoot(int col, int row) => (col * TileW + TileW / 2, host.CellCenterY(col, row));

    private IEnumerable<int> FoesOf(UnitState user) =>
        Enumerable.Range(0, host._units.Length).Where(i => host._units[i] is { Alive: true, OnField: true } u && u != user && BattleScene.SeesAsFoe(user, u));

    /// <summary>비연참(파트1, 핸들러 0x10060780) — 시전자 등에 불 날개(503, 높이 100)가 돋고, 동작 9 를 23번 한 뒤 불 소용돌이(230)와 함께 대상까지 돌진한다(빠르기 20 × 1.8).
    /// 돌진이 지나는 칸의 적마다 한 번 친다(칸이 바뀔 때마다 — 0x100769b0). 겨누는 것은 빈 칸(+0x31 = 7)이고 시전자는 그 칸에 남는다.</summary>
    private int FlyingSlash(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var (hx, hy) = _home;
        var user = _user;
        Put(503, 0, 0, hx, hy - 100 * 0.6, sounds: true);
        var dir = FacingOf(aim.X - hx, aim.Y - hy);
        user.Facing = Facing.Down;
        int c1 = 1 + Act(user, 9, 5, 23), go = c1 + 1;
        // 불 소용돌이 — 보는 쪽마다 모션 · 자리가 다르다(0x10060cc0 표).
        var (sm, sx, sy, mirror) = dir switch
        {
            Facing.Up => (0, hx, hy, false), Facing.Left => (1, hx, hy, false),
            Facing.Down => (0, hx, hy + 20 * 0.8, false), _ => (1, hx + 20, hy, true),
        };
        Put(230, sm, go, sx, sy, mirror: mirror);
        var charge = Move(_home, aim, 20, 1.8);
        Later(go, () => { user.Facing = dir; Act(user, 18, 1, repeat: 0); host.Btl.CenterOn(aim.X, aim.Y); });
        Carry(go, charge);
        // 지나는 칸 — 대상(돌진 길에 들어서는 칸의 적, BattleScene.Part1DashCells)마다 길에서 가장 가까운 틱에 친다.
        foreach (int ti in targets)
        {
            var (fx, fy) = host.Btl.UnitFoot(host._units[ti]);
            int best = 0;
            for (int t = 1; t < charge.Count; t++)
                if (Dist(charge[t], (fx, fy)) < Dist(charge[best], (fx, fy))) best = t;
            HitAt(go + best + 1, ti, hit);
        }
        int arrive = go + charge.Count;
        // 닿은 뒤 돌진 자세 두 번 → 동작 21. 원본은 이동기(0x100816d0)가 시전자를 그 칸에 내려놓는다 — 끝날 때까지 그 자리에 붙들고, 판 자리는 전투가 옮긴다.
        Later(arrive, () => Act(user, 18, 1, 2));
        int swing = arrive + 15;
        Later(swing, () => Act(user, 21, 8));
        int last = swing + ActionTicks(user, 21, 8);
        Carry(arrive, Enumerable.Repeat(aim, last - arrive + 1).ToList());
        return last + 1;
    }

    /// <summary>
    /// 폭풍검(핸들러 0x1005c040) — 800틱 비 · 처음 100틱의 번쩍임 · 화면 구름 서른 · 200틱부터 시전자의 번개 기둥, 300틱부터 대상마다 땅을 타고 가는 빛(379)과
    /// 비스듬히 떨어지는 번개(959:0), 396+2i 틱에 대상마다 터짐(959:2)과 함께 한 번 친다. 399틱에 화면이 하얗게 바랜다.
    /// </summary>
    private int StormSword(List<int> targets, Action<int> hit)
    {
        var (hx, hy) = _home;
        var user = _user;
        var (left, top, right, bottom) = View;
        Sound(2079, 0);
        Act(user, 30, ObsMotionTable.ActionStand);
        Rain(800, 7);
        // 번쩍임 — 틱마다 번쩍이고 있지 않으면 1/20 로 5~8틱(0x10087e00). 397틱에 한 번 더.
        for (int t = 0; t < 100; t++)
            if (Rand(20) == 0) { int n = Rand(4) + 5; _flashes.Add((At(t), At(t + n))); t += n; }
        _flashes.Add((At(397), At(397 + Rand(4) + 5)));
        for (int n = 0; n < 30; n++)
            Screen(959, 1, Rand(300) + 100, Rand(host.ViewWidth) - host.ViewWidth / 2, Rand(host.ViewHeight) - host.ViewHeight / 2, mirror: Rand(2) == 0);
        Sound(2080, 200);
        Bolt(200, hx, hy, 200);
        for (int t = 230; t < 400; t += 30)
            for (int k = 0; k < 4; k++) Along(72, 0, t, AsPath(Move((hx, hy - 255 * (k + 1)), (hx, hy - 255 * k), 12, 1)));
        Put(379, 0, 200, hx, hy, life: 200);
        Put(76, 0, 200, hx, hy, life: 100);
        Sound(2082, 399);
        _whiteouts.Add((At(399), 8, 0xFFFFFF));
        int last = 440;
        for (int i = 0; i < targets.Count; i++)
        {
            int ti = targets[i];
            var target = host._units[ti];
            var (tx, ty) = host.Btl.UnitFoot(target);
            var walk = Move(_home, (tx, ty), 14 * 0.9, 1, 14);
            Along(379, 0, 300, AsPath(walk, 2), life: walk.Count);
            int w = 300 + walk.Count;
            if (i == 0) Sound(2081, w);
            Later(w + 1, () => Act(target, 5, 2, 2));
            var fall = Move((tx - 120, top - 40), (tx, ty), 10, 1.6, 70);
            Along(959, 0, w + 1, AsPath(fall), life: fall.Count);
            int b = w + 1 + fall.Count;
            Bolt(b + 1, tx, ty, 15);
            Put(379, 0, b + 1, tx, ty, life: 15);
            Put(959, 2, b + 1, tx, ty);
            Bolt(400, tx, ty, 10);
            Put(379, 0, 400, tx, ty, life: 10);
            Put(959, 2, 396 + 2 * i, tx, ty);
            HitAt(397 + 2 * i, ti, hit);
            last = Math.Max(last, b + 1 + Length(959, 2));
        }
        return last;
    }

    /// <summary>비(<c>0x1009aec0</c> 갈래 1) — 틱마다 <paramref name="perTick"/> 줄기가 보이는 칸 위에서 비스듬히 떨어진다(빠르기 1~5, 틱마다 +0.5). 번쩍이는 동안은 하얗다.</summary>
    private void Rain(int ticks, int perTick)
    {
        var (left, top, right, bottom) = View;
        for (int t = 0; t < ticks; t++)
            for (int k = 0; k < perTick; k++)
            {
                double lx = left + Rand(right - left), ly = top + Rand(bottom - top), z0 = (ly - top) / 0.6 + 60 + Rand(200);
                double speed = Rand(5) + 1, streak = Rand(15) + 25, slant = streak / (3 + Rand(2));
                var heights = new List<double>();
                for (double z = z0; z > 0; z -= speed, speed += 0.5) heights.Add(z);
                uint grey = (uint)(Rand(150) + 100);
                _lines.Add(new Line(At(t), heights.Count, false, tick =>
                {
                    double z = heights[Math.Min(tick, heights.Count - 1)], x = lx + z * 0.6 / 3.5, y = ly - z * 0.6;
                    return (x - slant, y - streak, x, y, 0.8);
                }, 0xFF000000 | grey << 16 | grey << 8 | grey));
                if (k % 3 == 0) Put(67, 1, t + heights.Count, lx, ly);
            }
    }

    /// <summary>
    /// 천수관음 — 화면 가운데에 금빛(497)과 천수관음(212)이 떠올라 240틱 동안 흔들리며 떠 있고, 가운데에서 빛살 3000 줄기가 뻗는다.
    /// 대상마다 빛기둥(194)이 서고 그 빛기둥이 사라지기 시작할 때(50~149 + 114 틱) 회복한다(파트1 식 6 = 힐과 같은 식).
    /// </summary>
    private int Kannon(List<int> targets, Action<int> hit)
    {
        // 회복이라 범위 안의 우리 편만 — 범위 대상 목록에는 적도 들어 있었다.
        targets = [.. targets.Where(i => !BattleScene.SeesAsFoe(_user, host._units[i]))];
        Sound(1935, 0);
        int g0 = Length(497, 0);
        Screen(497, 0, 0, 0, 0);
        Screen(497, 1, g0 + 1, 0, 0, life: 270);
        Screen(497, 2, g0 + 272, 0, 0);
        int fy = -24;
        Screen(212, 0, g0 + 1, 0, fy, life: 16);
        var float1 = Circle(0, fy + 20, 20, 240, 3, 1, false, -Math.PI / 2, flat: true);
        int r0 = g0 + 18;
        Along(212, 2, r0, float1, life: 240, screen: true);
        for (int t = 0; t < 240; t += 2)
        {
            var p = float1[Math.Min(float1.Length - 1, t / 2)];
            Along(212, 2, r0 + t, [(0, p.X, p.Y)], life: 5, screen: true, fadeFrom: 0.5, fadeTo: 0.1);
        }
        var end = float1[^1];
        Screen(212, 4, r0 + 241, end.X, end.Y, life: 16);
        // 빛살 — 가운데에서 아무 점으로 빠르기 1 × 1.14~1.18(최대 40). 3~5틱마다 꼬리가 머리를 따라잡아 짧은 대시로 보인다(0x10087390).
        for (int n = 0; n < 3000; n++)
        {
            double tx = Rand(host.ViewWidth) - host.ViewWidth / 2 + Rand(401) - 200, ty = Rand(host.ViewHeight) - host.ViewHeight / 2 + Rand(401) - 200;
            var path = Move((0, 0), (tx, ty), 1, 1.14 + Rand(5) / 100.0);
            int hop = Rand(3) + 3;
            uint grey = (uint)(Rand(100) + 150);
            _lines.Add(new Line(At(Rand(270)), path.Count, true, tick =>
            {
                var head = path[tick];
                var tail = path[Math.Max(0, tick / hop * hop - 1)];
                return (tail.X, tail.Y, head.X, head.Y, (Rand(8) + 1) / 8.0);
            }, 0xFF000000 | grey << 16 | grey << 8 | grey, 1));
        }
        int last = r0 + 260;
        var (left, top, _, _) = View;
        foreach (int ti in targets)
        {
            var (tx, ty) = host.Btl.UnitFoot(host._units[ti]);
            int d = Rand(100) + 50, a1 = d + 12, a2 = a1 + 101;
            // 빛기둥 — 바닥 조각(홀수 모션)과 그 위로 화면 위까지 쌓은 기둥 조각(짝수 모션). 나타남(11) → 빛남(100) → 사라짐(19).
            var pieces = new List<(double Y, int Odd)> { (ty, 1) };
            for (double py = ty - 234 * 0.6; py > top - 40; py -= 50 * 0.6) pieces.Add((py, 0));
            foreach (var (py, odd) in pieces)
            {
                Put(194, 0 + odd, d, tx, py, life: 11);
                Put(194, 2 + odd, a1, tx, py, life: 100);
                Put(194, 4 + odd, a2, tx, py, life: 19);
            }
            HitAt(a2 + 1, ti, hit);
            last = Math.Max(last, a2 + 20);
        }
        return last;
    }

    /// <summary>
    /// 타이타니아 — 화면 가운데에 빛 틈이 위아래로 열리고 전격 거인(210)이 나타나 180틱 떠 있는 동안 손의 구슬에서 빛줄기 백 개가 화면 끝으로 뻗었다 줄어든다.
    /// 그동안 범위 안 아무 칸에 번개 백 개(210:9)가 떨어지고, 맞은 칸의 사람을 그 번개(71:0)로 친다.
    /// </summary>
    private int Titania(List<(int Col, int Row)> cells, Action<int> hit)
    {
        var user = _user;
        int hw = host.ViewWidth / 2, hh = host.ViewHeight / 2;
        Sound(2045, 0);
        // 빛 틈(0x10087f60 — 그림을 늘인다): 위아래로 빠르기 1 × 1.08 씩, 틱마다 1px 씩 넓어진다.
        double[] reach = new double[52];
        for (int t = 1; t < reach.Length; t++) reach[t] = reach[t - 1] + Math.Pow(1.08, t);
        for (int w = 0; w < 12; w += 3)
        {
            int off = w;
            _lines.Add(new Line(At(0), 52, true, tick => (off - 6, -reach[tick], off - 6, reach[tick], 0.7 - off / 20.0), 0xFFB8E0FF, 3));
        }
        Screen(210, 2, 52, -20, 0, life: 33);
        Sound(2046, 85);
        var float1 = Circle(-20, 10, 10, 180, 1, 1, Rand(2) == 0, -Math.PI / 2, flat: true);
        Along(210, 4, 86, float1, life: 180, screen: true);
        for (int n = 0; n < 100; n++)
        {
            (double X, double Y) to = Rand(4) switch
            {
                0 => (Rand(2 * hw) - hw, -hh), 1 => (hw, Rand(2 * hh) - hh), 2 => (Rand(2 * hw) - hw, hh), _ => (-hw, Rand(2 * hh) - hh),
            };
            var path = Move((-20, -100), to, 5, 1.6, 80);
            int len = path.Count;
            uint color = Rand(2) == 0 ? 0xFF6AE8FFu : 0xFF2040A0u;
            _lines.Add(new Line(At(87 + Rand(80)), 2 * len + 1, true, tick =>
            {
                var head = path[Math.Min(tick, len - 1)];
                var tail = tick < len ? (X: -20.0, Y: -100.0) : path[Math.Min(tick - len, len - 1)];
                double level = tick < len ? Math.Min(1, (tick / 3 + 1) * 0.25) : Math.Max(0.1, 1 - (tick - len) / 3 * 0.25);
                return (tail.X, tail.Y, head.X, head.Y, level);
            }, color, 2));
        }
        Sound(2051, 266);
        // 가로 빛 띠 — 같은 그림이 옆으로 늘어난다(167틱부터 51틱).
        for (int w = 0; w < 12; w += 3)
        {
            int off = w;
            _lines.Add(new Line(At(167), 51, true, tick => (-reach[tick] * 1.3, off - 6, reach[tick] * 1.3, off - 6, 0.6 - off / 24.0), 0xFFE0F0FF, 3));
        }
        var end = float1[^1];
        Screen(210, 6, 267, end.X, end.Y, life: 33);
        int last = 300;
        if (cells.Count == 0) return last;
        var (_, top, _, _) = View;
        for (int n = 0; n < 100; n++)
        {
            var (col, row) = cells[Rand(cells.Count)];
            var (cx, cy) = CellFoot(col, row);
            int delay = Rand(100) + 100;
            if (n % 3 == 0) Sound(2047 + Rand(4), delay);
            var fall = Move((cx + 400, top - 60), (cx, cy), Rand(5) + 5, 1.6, 100);
            Along(210, 9, delay, AsPath(fall), life: fall.Count);
            int e = delay + fall.Count + 1;
            Put(71, 0, e, cx, cy);
            Put(379, 0, e, cx, cy);
            Put(667, 1, e, cx - TileW / 2, cy - TileH / 2);
            for (int i = 0; i < host._units.Length; i++)
                if (host._units[i] is { Alive: true, OnField: true } u && u.Col == col && u.Row == row && u != user && BattleScene.SeesAsFoe(user, u))
                    HitAt(e + 1, i, hit);
            last = Math.Max(last, e + Length(71, 0));
        }
        return last;
    }

    /// <summary>묵호자의 칼 그림 — 보는 방향 16칸(0 위 · 4 오른쪽 · 8 아래)의 모션, 9~15 는 22−d 를 뒤집어.</summary>
    private static (int Motion, bool Mirror) SwordPose(int dir16) => dir16 <= 8 ? (6 + dir16, false) : (22 - dir16, true);

    private static int Dir16((double X, double Y) from, (double X, double Y) to) =>
        ((int)Math.Round(Math.Atan2(to.X - from.X, -(to.Y - from.Y)) / (Math.PI / 8)) + 16) % 16;

    /// <summary>묵호자의 칼이 대상을 지나쳐 가는 자리(월드, 보이는 방향 16칸 — <c>0x10068e88</c> 표).</summary>
    private static readonly (int X, int Y)[] SlashOffsets =
    [
        (0, -400), (80, -320), (160, -240), (240, -160), (400, 0), (320, 80), (240, 160), (160, 240),
        (0, 400), (-80, 320), (-160, 240), (-240, 160), (-400, 0), (-320, -80), (-240, -160), (-160, -240),
    ];

    /// <summary>
    /// 묵호자 — 화면에서 두 겹의 묵호자(282)가 가운데로 모여 300틱 떠 있다 갈라지고 칼 넷이 화면 밖으로 날아간다.
    /// 121틱부터 대상마다 칼 한 자루가 보이는 판 가장자리에서 나와 모든 대상을 아무 차례로 꿰뚫고(맞는 자세만), 마지막에 제 대상 위로 올라가 내리꽂으며 한 번 친다.
    /// 원본은 방향이 같으면 한 바퀴를 다 돌고 칼끼리 차례가 이어지는 버릇이 있다 — 여기서는 칼마다 따로 돈다.
    /// </summary>
    private int BlackFox(List<int> targets, Action<int> hit)
    {
        int hw = host.ViewWidth / 2, hh = host.ViewHeight / 2;
        Sound(2039, 0);
        var p1 = Move((0, -120), (0, 0), 15, 0.9);
        var p2 = Move((0, 120), (0, 0), 15, 0.9);
        Along(282, 0, 0, AsPath(p1), life: p1.Count, screen: true);
        Along(282, 0, 0, AsPath(p2), life: p2.Count, screen: true);
        int r0 = p2.Count + 1;
        var float1 = Circle(0, 10, 10, 300, 1, 1, false, -Math.PI / 2, flat: true);
        Along(282, 2, r0, float1, life: 300, screen: true);
        Sound(2052, r0 + 300);
        var end = float1[^1];
        foreach (int sign in new[] { -1, 1 })
        {
            var apart = Move((end.X, end.Y), (0, sign * 120), 15, 0.9);
            Along(282, 4, r0 + 301, AsPath(apart), life: Math.Max(apart.Count, Length(282, 4)), screen: true);
        }
        (int M, double X0, double Y0, double X1, double Y1, bool Mir)[] leave =
            [(6, -100, -100, -100, -hh - 50, false), (10, -50, -50, hw + 50, -50, false), (14, -100, 0, -100, hh + 50, false), (10, -150, -50, -hw - 50, -50, true)];
        foreach (var (m, x0, y0, x1, y1, mir) in leave)
        {
            var path = Move((x0, y0), (x1, y1), 1, 1.1);
            Along(282, m, r0, AsPath(path, 2), life: path.Count, screen: true, mirror: mir);
        }
        int last = r0 + 330;
        if (targets.Count == 0) return last;
        var (left, top, right, bottom) = View;
        foreach (int own in targets)
        {
            int tick = 121, cur = 0;
            (double X, double Y) prev = Rand(4) switch
            {
                0 => (left + Rand(right - left), top + 20), 1 => (right - 20, top + Rand(bottom - top)),
                2 => (left + Rand(right - left), bottom - 20), _ => (left + 20, top + Rand(bottom - top)),
            };
            var owner = host.Btl.UnitFoot(host._units[own]);
            var order = targets.OrderBy(_ => Rand(1 << 16)).ToList();
            var points = order.Select(t => (Target: (int?)t, At: ((double X, double Y))host.Btl.UnitFoot(host._units[t]))).ToList();
            points.Add((null, (owner.X, owner.Y - 200 * 0.6)));
            foreach (var (who, next) in points)
            {
                int dir = Dir16(prev, next);
                Sound(2040, tick);
                // 도는 칼 — 한 칸(22.5°)에 2틱, 시계 방향으로.
                while (cur != dir)
                {
                    cur = (cur + 1) % 16;
                    var (tm, tmir) = SwordPose(cur);
                    Put(282, tm, tick, prev.X, prev.Y, life: 2, mirror: tmir);
                    tick += 2;
                }
                Sound(2041, tick);
                var (m, mir) = SwordPose(dir);
                var dash = Move(prev, next, 15, 1.6);
                Along(282, m, tick, AsPath(dash), life: dash.Count, mirror: mir);
                for (int t = 0; t < dash.Count; t++) Put(56, 2, tick + t, dash[t].X, dash[t].Y);
                tick += dash.Count;
                if (who is not int target) { prev = next; break; }
                Sound(2067, tick);
                var victim = host._units[target];
                Later(tick + 1, () => Act(victim, 5, 2));
                var (ox, oy) = SlashOffsets[dir];
                (double X, double Y) past = (next.X + ox, next.Y + oy * 0.8);
                var slide = Move(next, past, 20, 0.96, 40, 3);
                Along(282, m, tick + 1, AsPath(slide, 2), life: slide.Count, mirror: mir);
                for (int t = 0; t < slide.Count; t += 2) Put(56, 2, tick + 1 + t, slide[t].X, slide[t].Y);
                tick += 1 + slide.Count;
                prev = past;
            }
            // 끝 — 아래를 보도록 돌고(9 까지) 제 대상 위에서 내리꽂는다(빠르기 1 × 1.1). 닿으면 526:0 과 불티 스물, 그때 한 번 친다.
            Sound(2042, tick);
            var dive = Move(prev, (owner.X, owner.Y - 40 * 0.6), 1, 1.1);
            Along(282, 14, tick, AsPath(dive, 2), life: dive.Count);
            int hitAt = tick + dive.Count + 1;
            Put(526, 0, hitAt, owner.X, owner.Y, sounds: true);
            HitAt(hitAt + 1, own, hit);
            for (int n = 0; n < 20; n++) Debris(192, 0, hitAt + Rand(5), owner.X, owner.Y, 10, 0.9);
            last = Math.Max(last, hitAt + Length(526, 0));
        }
        return last;
    }
}
