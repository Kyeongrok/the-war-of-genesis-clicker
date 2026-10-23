using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>파트1 검 기술 — 블리자드 스탐(파트1 공격 289, 엑스칼리버)과 설화난영참(290, 바리사다 II)(사용자 요청 menu-85).</summary>
/// <remarks>
/// 원본 핸들러 <c>0x1005d4e0</c>(블리자드 스탐) · <c>0x1005e360</c>(설화난영참). 공통 앞머리는 <see cref="SwordPrep"/>.
/// 블리자드 스탐은 회오리 원의 점 수를 8r 로 바로잡았다(전에는 4√2·r 로 어림해 원이 한 바퀴를 더 빨리 돌았다).
/// </remarks>
internal sealed partial class Part1Skill
{
    internal const int BlizzardWork = 20289, SeolhwaWork = 20290;

    /// <summary>블리자드 스탐 — 핸들러 <c>0x1005d4e0</c>(분석 노트 1.2). 끝나는 틱을 돌려준다.</summary>
    private int Blizzard(List<int> targets, Action<int> hit)
    {
        var (hx, hy) = _home;
        var user = _user;
        int last = 0;
        Sound(2026, 0);
        Sound(2027, 50);

        // 눈보라 — 보이는 화면의 왼끝 · 오른끝에서 스무 송이씩 맞은편으로 휘어 날아간다(빠르기 1 × 1.02~1.04, 최대 70). 3틱마다 잔상.
        int left = host._camX, right = host._camX + host.ViewWidth, top = host._camY, bottom = host._camY + host.ViewHeight;
        for (int n = 0; n < 40; n++)
        {
            bool fromLeft = n < 20;
            double y0 = top + Rand(Math.Max(1, bottom - top)), y1 = Math.Clamp(y0 + (Rand(11) - 5) * TileH, top, bottom);
            (double X, double Y) a = (fromLeft ? left : right, y0), b = (fromLeft ? right : left, y1);
            var controls = new List<(double X, double Y)> { a };
            for (int k = 1; k <= 3; k++)
                controls.Add((a.X + (b.X - a.X) * k / 4 + Rand(81) - 40, a.Y + (b.Y - a.Y) * k / 4 + Rand(121) - 60));
            controls.Add(b);
            var points = CurveWalk(controls, 1, 1.02 + Rand(3) / 100.0, 70);
            int motion = Rand(2);
            Along(37, motion, 0, AsPath(points, 2), life: points.Count);
            for (int t = 3; t < points.Count; t += 3) Put(37, motion, t, points[t].X, points[t].Y);
            last = Math.Max(last, points.Count + Length(37, motion));
        }

        // 회오리 — 시전자 둘레 반지름 50 · 134 · 218 · 302 · 386 의 원을 차례로 한 바퀴씩(빠르기 15 × 1.1, 최대 40) 돌며 틱마다 눈 연기 39:0 을 떨군다.
        // 연기는 30틱 동안 빠르기 1 × 1.1 로 솟는다.
        int tick = 0;
        for (int r = 50; r < 420; r += 84)
        {
            int points = 8 * r;
            double walked = 0, speed = 15, a0 = Rand(360) * Math.PI / 180;
            for (; walked < points; tick++)
            {
                double ang = a0 + walked / points * 2 * Math.PI;
                double px = hx + r * Math.Cos(ang), py = hy + r * Math.Sin(ang) * 0.8;
                var rise = new List<(int, double, double)>();
                double z = 0, s = 1;
                for (int t = 0; t <= 30; t += 5) { rise.Add((t, px, py - z * 0.6)); for (int k = 0; k < 5; k++) { z += s; s *= 1.1; } }
                Along(39, 0, tick, [.. rise], life: 30);
                walked += speed;
                speed = Math.Min(40, speed * 1.1);
            }
            tick++;   // 다음 원은 앞 원이 끝난 다음 틱에
        }
        last = Math.Max(last, tick + 30);

        // 시전자 빛 897 — 0(한 번) → 1(192틱까지 되풀이) → 2(한 번).
        int aura0 = Length(897, 0);
        Put(897, 0, 10, hx, hy - 20 * 0.6);
        Put(897, 1, 10 + aura0, hx, hy - 20 * 0.6, life: 192);
        Put(897, 2, 10 + aura0 + 192, hx, hy - 20 * 0.6);
        last = Math.Max(last, 10 + aura0 + 192 + Length(897, 2));

        int e1 = 131, e1End = e1 + Length(101, 0);
        if (targets.Count > 0) Sound(1934, e1 - 1);
        for (int i = 0; i < targets.Count; i++)
        {
            int ti = targets[i];
            var target = host._units[ti];
            var (tx, ty) = host.Btl.UnitFoot(target);
            // 눈 연기 다섯이 대상 둘레를 돈다(반지름 40, 빠르기 10, 100틱).
            for (int k = 0; k < 5; k++)
                Along(39, 0, 10 * k, Circle(tx + Rand(41) - 20, ty + Rand(41) - 20, 40, 100, 10, 1, Rand(2) == 0, Rand(360) * Math.PI / 180), life: 100);
            // 얼음 기둥이 대상에 서고(101:0 → 101:1 붙듦), 시전자가 대상마다 10틱 간격으로 얼음(768)을 던진다(빠르기 10 × 1.6).
            Put(101, 0, e1, tx, ty);
            int throwAt = e1End + 10 * i + 2;
            var flight = Move((hx, hy - 60 * 0.6), (tx, ty - 60 * 0.6), 10, 1.6);
            int arrive = throwAt + flight.Count;
            Put(101, 1, e1End, tx, ty, life: arrive - e1End);
            Along(768, 0, throwAt, AsPath(flight.Prepend((hx, hy - 60 * 0.6)).ToList()), life: flight.Count);
            Later(throwAt, () => { user.Facing = FacingOf(tx - hx, ty - hy); Act(user, 17, 8); });
            Sound(2029, arrive);
            Put(101, 6, arrive + 1, tx, ty);
            HitAt(arrive + 2, ti, hit);
            Shards(arrive + 2, tx, ty);
            last = Math.Max(last, arrive + 2 + 5 + Math.Max(Length(101, 6), Length(787, 0)));
        }
        if (targets.Count > 0) Sound(2028, e1End);
        return last;
    }

    /// <summary>설화난영참 — 핸들러 <c>0x1005e360</c>(분석 노트 2.2·2.3). 끝나는 틱을 돌려준다.</summary>
    private int Seolhwa(List<int> targets, Action<int> hit)
    {
        var (hx, hy) = _home;
        var user = _user;
        var home = _home;
        Sound(70, 0);
        // 눈 — 400틱 동안 틱마다 두 송이가 보이는 칸 위에서 떨어진다(빠르기 7~13, 좌우로 흔들림 5~9).
        for (int t = 0; t < 400; t++)
            for (int k = 0; k < 2; k++)
            {
                double x = host._camX + Rand(host.ViewWidth), y = host._camY + Rand(host.ViewHeight);
                _flakes.Add(new Flake(At(t), x, y, (y - host._camY) / 0.6 + 100, Rand(7) + 7, Rand(5) + 5));
            }
        int last = 400;

        var stand = new Dictionary<int, (int Obs, int Motion, int Start, double X, double Y)>();
        foreach (int ti in targets)
        {
            var (tx, ty) = host.Btl.UnitFoot(host._units[ti]);
            for (int n = 0; n < 20; n++) Debris(173, 6, 0, tx + Rand(41) - 20, ty + Rand(41) - 20, Rand(10) + 10, 0.8);
            // 얼음 기둥 마름모 — |dx|+|dy| ≤ 2 인 13칸. 칸마다 꽃잎 다섯(190:0, 반지름 30), 40+10d 틱에 솟고(118:r) 버티다(r+3) 부서진다(r+6).
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    int d = Math.Abs(dx) + Math.Abs(dy);
                    if (d > 2) continue;
                    double cx = tx + dx * 40, cy = ty + dy * 40 * 0.8;
                    int r = Rand(3);
                    for (int k = 0; k < 5; k++)
                        Along(190, 0, 5 * k, Circle(cx + Rand(41) - 20, cy + Rand(41) - 20, 30, 51, 15, 0.9, Rand(2) == 0, Rand(360) * Math.PI / 180), life: 51);
                    int g1 = 40 + 10 * d, g2 = g1 + Length(118, r) + 1, hold = 10 * d + 50, g3 = g2 + hold + 1;
                    Put(118, r, g1, cx, cy);
                    Put(118, r + 3, g2, cx, cy, life: hold);
                    Put(118, r + 6, g3, cx, cy);
                    last = Math.Max(last, g3 + Length(118, r + 6));
                }
            Put(101, 0, 40, tx, ty);
            stand[ti] = (101, 1, 40 + Length(101, 0), tx, ty);
        }
        if (targets.Count > 0) Sound(1934, 40);

        // 시전자 빛 767 — 0(한 번) → 1(100틱). 그것이 끝나면 돌진이 시작한다.
        Sound(2030, 0);
        int c1 = Length(767, 0);
        Put(767, 0, 0, hx, hy - 60 * 0.6);
        Put(767, 1, c1, hx, hy - 60 * 0.6, life: 100);
        int tick = c1 + 100 + 1;

        // 돌진 — 대상을 아무 차례로 하나씩: 빠르기 15 × 1.6 으로 대상까지(닿으면 101:2 와 함께 한 번 친다), 대상을 80 지나 빠르기 16 × 0.86 으로 미끄러진다.
        // 다 돌면 제자리로 돌아온다. 돌진하는 동안 시전자는 돌진 자세(파트1 동작 18)를 되풀이하고 틱마다 꼬리 767:2 를 남긴다.
        var order = targets.OrderBy(_ => Rand(1 << 16)).ToList();
        (double X, double Y) at = home;
        for (int k = 0; k <= order.Count; k++)
        {
            bool back = k == order.Count;
            (double X, double Y) to = back ? home : host.Btl.UnitFoot(host._units[order[k]]);
            Sound(2031, tick);
            var dash = Move(at, to, 15, 1.6);
            var from = at;
            int start = tick;
            Later(start, () =>
            {
                user.Facing = FacingOf(to.X - from.X, to.Y - from.Y);
                Act(user, 18, 1, repeat: 0);
                host.Btl.CenterOn(to.X, to.Y);
            });
            Carry(start, dash);
            for (int t = 0; t < dash.Count; t++) Put(767, 2, start + t, dash[t].X, dash[t].Y - 60 * 0.6);
            int arrive = start + dash.Count;
            if (back) { tick = arrive; break; }
            int ti = order[k];
            var s = stand[ti];
            if (arrive > s.Start) Put(s.Obs, s.Motion, s.Start, s.X, s.Y, life: arrive - s.Start);
            Put(101, 2, arrive + 1, to.X, to.Y);
            HitAt(arrive + 2, ti, hit);
            Sound(2029, arrive);
            Shards(arrive + 1, to.X, to.Y);
            // 지나치는 점 — 보이는 방향 16칸(0 = 위 · 4 = 오른쪽 …)으로 80(대각은 표대로).
            int dir16 = ((int)Math.Round(Math.Atan2(to.X - from.X, -(to.Y - from.Y)) / (Math.PI / 8)) + 16) % 16;
            var (ox, oy) = PastOffsets[dir16];
            (double X, double Y) past = (to.X + ox, to.Y + oy * 0.8);
            var slide = Move(to, past, 16, 0.86);
            Carry(arrive + 1, slide);
            for (int t = 0; t < slide.Count; t++) Put(767, 2, arrive + 1 + t, slide[t].X, slide[t].Y - 60 * 0.6);
            at = past;
            tick = arrive + 1 + slide.Count + 1;
            last = Math.Max(last, arrive + 6 + Math.Max(Length(101, 2), Length(787, 0)));
        }
        Later(tick, () => host.Btl.CenterOnUnit(user));
        return Math.Max(last, tick + 1);
    }

    /// <summary>설화난영참이 대상을 지나쳐 멈추는 자리(월드, 보이는 방향 16칸 — <c>0x1005f30c</c> 표).</summary>
    private static readonly (int X, int Y)[] PastOffsets =
    [
        (0, -80), (40, -80), (80, -80), (80, -40), (80, 0), (80, 40), (80, 80), (40, 80),
        (0, 80), (-40, 80), (-80, 80), (-80, 40), (-80, 0), (-80, -40), (-80, -80), (-40, -80),
    ];

    /// <summary>
    /// 휘어 나는 이동기(<c>0x1007e060</c>) — 조절점들을 잇는 곡선(캣멀롬)을 빠르기 1 에서 틱마다 곱수를 곱해(최대 70) 따라간다. 틱마다의 자리.
    /// </summary>
    private static List<(double X, double Y)> CurveWalk(List<(double X, double Y)> controls, double speed, double factor, double max, double min = 0)
    {
        var dense = new List<(double X, double Y)>();
        for (int i = 0; i + 1 < controls.Count; i++)
        {
            var p0 = controls[Math.Max(0, i - 1)]; var p1 = controls[i]; var p2 = controls[i + 1]; var p3 = controls[Math.Min(controls.Count - 1, i + 2)];
            for (int s = 0; s < 40; s++)
            {
                double t = s / 40.0, t2 = t * t, t3 = t2 * t;
                dense.Add((0.5 * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3),
                           0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3)));
            }
        }
        dense.Add(controls[^1]);
        var points = new List<(double X, double Y)>();
        int k = 0;
        var (x, y) = dense[0];
        for (int guard = 0; guard < 600 && k < dense.Count - 1; guard++)
        {
            double left = speed;
            while (k < dense.Count - 1)
            {
                double dx = dense[k + 1].X - x, dy = dense[k + 1].Y - y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d > left) { x += dx / d * left; y += dy / d * left; break; }
                left -= d;
                (x, y) = dense[++k];
            }
            points.Add((x, y));
            speed = Math.Clamp(speed * factor, Math.Max(0.5, min), max);
        }
        return points;
    }
}
