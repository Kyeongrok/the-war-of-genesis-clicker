using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 창세기전3 파트1 의 검 기술 — 블리자드 스탐(파트1 공격 289, 엑스칼리버)과 설화난영참(290, 바리사다 II)(사용자 요청 menu-85).
/// 이 게임에서는 스킬 파일 <c>assets/data/skills/20097.json</c>·<c>20099.json</c> 의 어빌리티로 캐릭터 에디터에서 붙여 쓴다.
/// </summary>
/// <remarks>
/// <para>원본 <c>alpha.dll</c> 의 핸들러 <c>0x1005d4e0</c>(블리자드 스탐)·<c>0x1005e360</c>(설화난영참)를 옮겼다. 파트2 와 달리 핸들러가
/// <b>한 번만</b> 돌며 이펙트를 지연·사슬·수명으로 다 깔아 두므로, 여기서도 시작할 때 시각표를 다 짜 두고 그 때마다 그리고 친다.
/// 공통 앞머리(<c>0x10043950</c>, 50틱): 시전자 둘레에 Obs 395:0(소리 1878), 정면을 보고 동작 9(파트2 5 — 공격 준비)를 붙든다.</para>
/// <para>단위는 판 픽셀 — 월드 → 화면은 x 그대로 · y × 0.8 · 높이 z × 0.6(<c>0x1008fbb0</c>). 이동기(<c>0x100780f0</c>)는 틱마다 빠르기만큼
/// 다가가고 빠르기에 곱수를 곱한다(최대 40 · 최소 1). 그림·소리 번호는 파트1 번호에 <see cref="Part1Game.ObsBase"/> 를 더해 부른다.</para>
/// <para>검은 바탕의 빛(섞기 키 9 → 가산)만 가산으로 그리고 나머지는 보통으로 그린다. 원을 도는 이동기의 한 바퀴 점 수는 원을 픽셀로 그린 점 수라
/// 4√2·r 로 어림했다(가설).</para>
/// </remarks>
internal sealed class Part1SwordSkill(GameWindow host)
{
    internal const int BlizzardWork = 20289, SeolhwaWork = 20290;
    internal static readonly HashSet<int> Works = [BlizzardWork, SeolhwaWork];

    private static int P(int part1) => Part1Game.ObsBase + part1;

    /// <summary>그릴 그림 하나 — 시작(초), 길(틱마다가 아니어도 된다 — 마디 사이는 곧게), 수명(null 이면 모션 한 번).</summary>
    private sealed record Fx(int Obs, int Motion, double Start, (int Tick, double X, double Y)[] Path, int? Life, bool Mirror);

    /// <summary>설화난영참의 눈송이 — 점으로 떨어진다(<c>0x10082b00</c>, 색 13).</summary>
    private sealed record Flake(double Start, double X, double Y, double Z, double Speed, int Sway);

    private readonly List<Fx> _fx = [];
    private readonly List<Flake> _flakes = [];

    private double _t0;
    private double At(int tick) => _t0 + tick / TicksPerSecond;
    private int Rand(int n) => host.Btl._fxRandom.Next(Math.Max(1, n));
    private int Length(int obs, int motion) => Math.Max(1, host.UiFor(P(obs))?.MotionLength(motion) ?? 1);

    private void Put(int obs, int motion, int tick, double x, double y, int? life = null, bool mirror = false, bool sounds = false) =>
        Along(obs, motion, tick, [(0, x, y)], life, mirror, sounds);

    private void Along(int obs, int motion, int tick, (int Tick, double X, double Y)[] path, int? life = null, bool mirror = false, bool sounds = false)
    {
        _fx.Add(new Fx(P(obs), motion, At(tick), path, life, mirror));
        if (sounds && host.UiFor(P(obs))?.Clip(motion) is { } clip)
            foreach (var (t, sound) in clip.Sounds) host._pendingSounds.Add((At(tick + t), sound));
    }

    private void Sound(int part1, int tick) => host._pendingSounds.Add((At(tick), P(part1)));

    /// <summary>곧은 이동기 — 틱마다의 자리(첫 칸 = 출발 다음 틱). 닿은 틱이 마지막 칸이다.</summary>
    private static List<(double X, double Y)> Move((double X, double Y) from, (double X, double Y) to, double speed, double factor, double max = 40, double min = 1)
    {
        var points = new List<(double X, double Y)>();
        var (x, y) = from;
        for (int guard = 0; guard < 600; guard++)
        {
            double dx = to.X - x, dy = to.Y - y, far = Math.Sqrt(dx * dx + dy * dy);
            if (far <= speed) { points.Add(to); break; }
            (x, y) = (x + dx / far * speed, y + dy / far * speed);
            points.Add((x, y));
            speed = Math.Clamp(speed * factor, min, max);
        }
        if (points.Count == 0) points.Add(to);
        return points;
    }

    private static (int Tick, double X, double Y)[] AsPath(List<(double X, double Y)> points, int every = 1)
    {
        var path = new List<(int, double, double)>();
        for (int i = 0; i < points.Count; i += every) path.Add((i, points[i].X, points[i].Y));
        if ((points.Count - 1) % every != 0) path.Add((points.Count - 1, points[^1].X, points[^1].Y));
        return [.. path];
    }

    /// <summary>원을 도는 이동기(<c>0x10080000</c>) — 반지름 r(월드) 둘레 4√2·r 점을 틱당 빠르기만큼 걷는다.</summary>
    private static (int Tick, double X, double Y)[] Circle(double cx, double cy, double r, int life, double speed, double factor, bool reverse, double start)
    {
        int points = Math.Max(8, (int)(4 * Math.Sqrt(2) * r));
        var path = new List<(int, double, double)>();
        double walked = 0;
        for (int t = 0; t <= life; t += 2)
        {
            double a = start + (reverse ? -1 : 1) * walked / points * 2 * Math.PI;
            path.Add((t, cx + r * Math.Cos(a), cy + r * Math.Sin(a) * 0.8));
            for (int k = 0; k < 2; k++) { walked += speed; speed = Math.Clamp(speed * factor, 1, 40); }
        }
        return [.. path];
    }

    /// <summary>터지는 파편(<c>0x10079780</c>) — 둘레 ±300(높이 ±100) 의 아무 점으로 날아가고 수명(0 = 모션 한 번)이 되면 끝난다.</summary>
    private void Debris(int obs, int motion, int tick, double x, double y, double speed, double factor)
    {
        double tx = x + Rand(601) - 300, ty = y + (Rand(601) - 300) * 0.8 - Math.Max(0, Rand(201) - 100) * 0.6;
        var points = Move((x, y), (tx, ty), speed, factor, 40, 0.5);
        int life = Length(obs, motion);
        Along(obs, motion, tick, AsPath(points.Take(life).Prepend((x, y)).ToList(), 2));
    }

    /// <summary>얼음 조각 열 개 — 16방향(<c>r &lt; 9</c> 면 모션 r, 아니면 16−r 을 뒤집어).</summary>
    private void Shards(int tick, double x, double y)
    {
        for (int n = 0; n < 10; n++)
        {
            int r = Rand(16);
            Put(787, r < 9 ? r : 16 - r, tick + Rand(5), x, y, mirror: r >= 9);
        }
        for (int n = 0; n < 20; n++) Debris(39, 0, tick + Rand(5), x, y, Rand(10) + 5, 0.9);
    }

    private static Facing FacingOf(double dx, double dy) =>
        Math.Abs(dx) >= Math.Abs(dy) * 1.25 ? (dx < 0 ? Facing.Left : Facing.Right) : (dy < 0 ? Facing.Up : Facing.Down);

    /// <summary>시전자에게 동작을 건다 — 그 동작이 없는 그림(파트2 모습)이면 <paramref name="fallback"/>.</summary>
    private void Act(UnitState u, int action, int fallback, bool loop)
    {
        if (!host._sprites.TryGetValue(u.ChrCode, out var sprite)) return;
        int use = sprite.RawClip(action * 3) != null ? action : fallback;
        u.PlayAction(use, loop ? 30 : Math.Max(0.1, sprite.ActionSeconds(use, u.Facing)));
    }

    /// <summary>
    /// 기술 하나를 처음부터 끝까지 — 앞머리 50틱 뒤 시각표를 깔고, 대상마다 맞는 때에 <paramref name="hit"/> 를 부른다.
    /// </summary>
    internal IEnumerable<bool> Routine(WorkData w, UnitState user, List<int> targets, Action<int> hit)
    {
        host.PreloadSounds([.. Part1Game.SkillSounds.Select(P), P(1878), P(24), P(26), P(16), P(71)]);
        var (hx, hy) = host.Btl.UnitFoot(user);
        var facing = user.Facing;

        // ── 공통 앞머리(0x10043950) — 빛 395:0 · 소리 1878, 카메라를 시전자에, 정면을 보고 공격 준비를 50틱 붙든다.
        _t0 = host._lastTime;
        Put(395, 0, 0, hx, hy - 60 * 0.6, sounds: true);
        host.Btl.CenterOnUnit(user);
        user.Facing = Facing.Down;
        user.PlayAction(5, 51 / TicksPerSecond);
        for (double end = At(51); host._lastTime < end;) yield return true;

        _t0 = host._lastTime;
        var events = new List<(double At, Action Run)>();
        var shifts = new List<(double Start, List<(double X, double Y)> Points)>();
        double finish = w.Id == BlizzardWork ? Blizzard(user, targets, hit, events, (hx, hy)) : Seolhwa(user, targets, hit, events, shifts, (hx, hy));
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 sword work {w.Id}: targets {targets.Count}, fx {_fx.Count}, events {events.Count}, took {(finish - _t0) * TicksPerSecond:0} ticks" + Environment.NewLine);

        events.Sort((a, b) => a.At.CompareTo(b.At));
        int next = 0;
        while (host._lastTime < finish || next < events.Count)
        {
            while (next < events.Count && events[next].At <= host._lastTime) events[next++].Run();
            // 돌진 — 시전자 그림을 그 길대로 옮긴다(CDash 가 틱마다 유닛 자리를 덮어쓴다, 0x100821dc).
            user.Shift = (0, 0);
            foreach (var (start, points) in shifts)
            {
                int tick = (int)((host._lastTime - start) * TicksPerSecond);
                if (tick < 0 || tick >= points.Count) continue;
                user.Shift = (points[tick].X - hx, points[tick].Y - hy);
            }
            yield return true;
        }
        user.Shift = (0, 0);
        user.Facing = facing;
        user.PlayAction(ObsMotionTable.ActionStand, 0);
    }

    /// <summary>블리자드 스탐 — 핸들러 <c>0x1005d4e0</c>(분석 노트 1.2). 끝나는 때(초)를 돌려준다.</summary>
    private double Blizzard(UnitState user, List<int> targets, Action<int> hit, List<(double, Action)> events, (double X, double Y) home)
    {
        var (hx, hy) = home;
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
            int points = Math.Max(8, (int)(4 * Math.Sqrt(2) * r));
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
            events.Add((At(throwAt), () => { user.Facing = FacingOf(tx - hx, ty - hy); Act(user, Part1Game.ThrowAction, 8, loop: false); }));
            Sound(2029, arrive);
            Put(101, 6, arrive + 1, tx, ty);
            events.Add((At(arrive + 2), () => hit(ti)));
            Shards(arrive + 2, tx, ty);
            last = Math.Max(last, arrive + 2 + 5 + Math.Max(Length(101, 6), Length(787, 0)));
        }
        if (targets.Count > 0) Sound(2028, e1End);
        return At(last);
    }

    /// <summary>설화난영참 — 핸들러 <c>0x1005e360</c>(분석 노트 2.2·2.3). 끝나는 때(초)를 돌려준다.</summary>
    private double Seolhwa(UnitState user, List<int> targets, Action<int> hit, List<(double, Action)> events,
                           List<(double Start, List<(double X, double Y)> Points)> shifts, (double X, double Y) home)
    {
        var (hx, hy) = home;
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
            events.Add((At(start), () =>
            {
                user.Facing = FacingOf(to.X - from.X, to.Y - from.Y);
                Act(user, Part1Game.DashAction, 1, loop: true);
                host.Btl.CenterOn(to.X, to.Y);
            }));
            shifts.Add((At(start), dash));
            for (int t = 0; t < dash.Count; t++) Put(767, 2, start + t, dash[t].X, dash[t].Y - 60 * 0.6);
            int arrive = start + dash.Count;
            if (back) { tick = arrive; break; }
            int ti = order[k];
            var s = stand[ti];
            if (arrive > s.Start) Put(s.Obs, s.Motion, s.Start, s.X, s.Y, life: arrive - s.Start);
            Put(101, 2, arrive + 1, to.X, to.Y);
            events.Add((At(arrive + 2), () => hit(ti)));
            Sound(2029, arrive);
            Shards(arrive + 1, to.X, to.Y);
            // 지나치는 점 — 보이는 방향 16칸(0 = 위 · 4 = 오른쪽 …)으로 80(대각은 표대로).
            int dir16 = ((int)Math.Round(Math.Atan2(to.X - from.X, -(to.Y - from.Y)) / (Math.PI / 8)) + 16) % 16;
            var (ox, oy) = PastOffsets[dir16];
            (double X, double Y) past = (to.X + ox, to.Y + oy * 0.8);
            var slide = Move(to, past, 16, 0.86);
            shifts.Add((At(arrive + 1), slide));
            for (int t = 0; t < slide.Count; t++) Put(767, 2, arrive + 1 + t, slide[t].X, slide[t].Y - 60 * 0.6);
            at = past;
            tick = arrive + 1 + slide.Count + 1;
            last = Math.Max(last, arrive + 6 + Math.Max(Length(101, 2), Length(787, 0)));
        }
        events.Add((At(tick), () => host.Btl.CenterOnUnit(user)));
        return At(Math.Max(last, tick + 1));
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
    private static List<(double X, double Y)> CurveWalk(List<(double X, double Y)> controls, double speed, double factor, double max)
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
            speed = Math.Min(max, speed * factor);
        }
        return points;
    }

    /// <summary>그림 · 눈송이를 그린다 — DrawEffects 가 부른다.</summary>
    internal void Draw()
    {
        double now = host._lastTime;
        _fx.RemoveAll(f =>
        {
            if (now < f.Start) return false;
            int tick = (int)((now - f.Start) * TicksPerSecond);
            if (host.UiFor(f.Obs) is not { } sprite) return true;
            int end = f.Life ?? Math.Max(1, sprite.MotionLength(f.Motion));
            if (tick >= end) return true;
            var path = f.Path;
            int k = 0;
            while (k + 1 < path.Length && tick >= path[k + 1].Tick) k++;
            var (a, b) = (path[k], path[Math.Min(k + 1, path.Length - 1)]);
            double t = b.Tick > a.Tick ? Math.Clamp((tick - a.Tick) / (double)(b.Tick - a.Tick), 0, 1) : 0;
            int x = (int)(a.X + (b.X - a.X) * t), y = (int)(a.Y + (b.Y - a.Y) * t);
            bool loop = f.Life != null;
            if (sprite.Clip(f.Motion) is { Children.Count: > 0 } clip) host.Btl.DrawUnitLayers(clip, tick, x, y, f.Mirror, loop: loop);
            int key = sprite.BlendAt(f.Motion, tick);
            host.DrawUi(f.Obs, f.Motion, tick, x, y, BlendOf(key), loop: loop, mirror: f.Mirror, fade: BlendFade(key));
            return false;
        });
        _flakes.RemoveAll(f =>
        {
            if (now < f.Start) return false;
            double tick = (now - f.Start) * TicksPerSecond, z = f.Z - f.Speed * tick;
            if (z <= 0) return true;
            double sway = Math.Abs(tick % (2 * f.Sway) - f.Sway) - f.Sway / 2.0;
            host.FillRect((int)(f.X + sway), (int)(f.Y - z * 0.6), 2, 2, 0xFFF4F8FF);
            return false;
        });
    }
}
