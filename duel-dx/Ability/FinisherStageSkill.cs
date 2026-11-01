using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 핸들러를 통째로 옮긴 필살기 넷 — 빅 뱅 · 인페르노 · 무신멸뢰옥 · 코메트(버그 리포트 #25, fg-111). 뽑은 표(AbilityScripts.g.cs)는 대상 고리 · 이동 클래스 · 지연을 못 읽고,
/// 준비 7 은 핸들러 틱을 안 따르므로 타격 키가 없는 모션에서 피해가 0.05초 만에 먼저 떴다.
/// </summary>
/// <remarks>
/// <para>빅 뱅(work 1593, <c>0x100b5990</c>): 모션 48+방향. t=45 에 1434:0 · 582:51 포물선(시전자 z+50 → 겨눈 칸, 30틱, 높이 거리/2), 카메라를 겨눈 칸으로.
/// t=75 에 1211:0 다섯(겨눈 칸 z+80i) · 1434:1, t=80 에 111:0 넷(칸 ±80). t=85 에 범위 안 모두 함께 맞는다.</para>
/// <para>인페르노(work 1581, <c>0x100b1fd0</c>): 모션 48, T0+80 에 215:3 → 215:4(200틱) → 215:5(시전자 z+100). 48 이 끝나면(T1) 모션 49, 적마다 화염탄 하나 + 나머지 25 − N 은 범위의 아무 칸.
/// 탄마다 D = 0~149틱에 시전자 (x+23, z+80)에서 틱당 15 로 날며 틱마다 475:2 · 불똥 472:0, 닿으면 1211:0, 그 효과가 끝날 때(F+27) 그 적이 맞는다. T1+140 에 모션 50.</para>
/// <para>무신멸뢰옥(work 1530, <c>0x100b1150</c>): 시전자는 숨고 복제가 선다 — 모션 48(100틱 뒤 20틱 동안 1.03배씩), 이어 모션 49(1.806배, 90틱 뒤 줄어듦, 105틱) · 902:9.
/// T138 에 1432:0, T140 에 324:1 셋. T112~228 의 4틱마다(30번) 시전자 둘레 2칸(~T148) · 3칸(~T188) · 4칸의 적을 다시 모아 친다 — 3타가 아니다(<c>0x100b15cb</c>).</para>
/// <para>코메트(work 1528, <c>0x100b0000</c>): 모션 90 뒤 1433:0, t=20 에 혜성 200:0(z+150, 위아래로 흔들리며 248틱) · 215:3 → 215:4, 흔들림 2(250틱), 파편 251 쉰 개가 솟았다가 혜성으로 빨려 든다.
/// 혜성이 z+600 으로 솟으면(t≈277) 영상 0033 · 0046, 그 뒤 대상마다 한 번 맞는다. 영상 0046 위의 대상 복제는 영상이 전투를 멈춰 옮기지 않았다.</para>
/// 몸 복제의 그리기 칸 17(더하기)은 보통 그리기로 갈음했다.
/// </remarks>
internal sealed class FinisherStageSkill(GameWindow host)
{
    internal const int BigBangWork = 1593, InfernoWork = 1581, MushinWork = 1530, CometWork = 1528;

    internal static bool Handles(int work) => work is BigBangWork or InfernoWork or MushinWork or CometWork;

    /// <summary>핸들러 안에서 같은 대상을 여러 번 치는가.</summary>
    internal static bool HitsRepeatedly(int work) => work == MushinWork;

    private const double LiftY = 0.6, WorldY = 0.8;

    internal IEnumerable<bool> Routine(WorkData w, UnitState user, int col, int row, List<int> targets, Action<int> hit) => w.Id switch
    {
        BigBangWork => BigBang(user, col, row, targets, hit),
        InfernoWork => Inferno(user, targets, hit),
        MushinWork => Mushin(user, hit),
        _ => Comet(w, user, targets, hit),
    };

    private static double Tick(double n) => n / TicksPerSecond;

    private IEnumerable<bool> Wait(double until)
    {
        while (host._lastTime < until) yield return true;
    }

    private (int X, int Y) CellFoot(int col, int row) => (col * TileW + TileW / 2, host.CellCenterY(col, row));

    private void Fx(int obs, int motion, double start, double x, double y, double? life = null) => host.HeavenEarthAb.AddTimedFx(obs, motion, start, (x, y), life, false);

    private void Sound(int obs, int motion, double at) => host.AcrostAb.FxSound(obs, motion, at);

    private IEnumerable<bool> BigBang(UnitState user, int col, int row, List<int> targets, Action<int> hit)
    {
        var (ux, uy) = host.Btl.UnitFoot(user);
        var (tx, ty) = CellFoot(col, row);
        double t0 = host._lastTime;
        host.HeavenEarthAb.PlayRawMotion(user, user.Facing switch { Facing.Up => 48, Facing.Down => 50, _ => 49 }, loop: false);
        foreach (bool _ in Wait(t0 + Tick(45))) yield return true;

        double launch = host._lastTime, peak = Math.Sqrt(Math.Pow(tx - ux, 2) + Math.Pow((ty - uy) / WorldY, 2)) * 0.5 * LiftY;
        Sound(1434, 0, launch);
        host.Btl.CenterOnCell(col, row);
        host.StagingAb._stageDraws.Add(() =>
        {
            double n = (host._lastTime - launch) * TicksPerSecond;
            if (n >= 30) return false;
            double f = n / 30;
            // 582:51 은 몸 그림의 모션이라 제 Obs 로 그린다(되풀이) — 효과 그림 칸으로 읽으면 안 나온다.
            if (host._sprites.TryGetValue(user.ChrCode, out var sprite) && sprite.FrameOfMotion(51, (int)n % Math.Max(1, sprite.MotionTicks(51)), false) is { } ball)
                host.BlitMasked(ball.Px, ball.W, ball.H, (int)(ux + (tx - ux) * f) + ball.X, (int)(uy - 30 * (1 - f) + (ty - uy) * f - Math.Sin(Math.PI * f) * peak) + ball.Y);
            return true;
        });
        double burst = launch + Tick(30), ring = launch + Tick(35);
        for (int i = 0; i < 5; i++) Fx(1211, 0, burst, tx, ty - 80 * i * LiftY);
        Sound(1434, 1, burst);
        foreach (var (dx, dy) in new[] { (80, 0), (-80, 0), (0, 80), (0, -80) }) Fx(111, 0, ring, tx + dx, ty + dy * WorldY);

        foreach (bool _ in Wait(launch + Tick(40))) yield return true;
        foreach (int t in targets) hit(t);
        foreach (bool _ in Wait(burst + Tick(27))) yield return true;
    }

    private IEnumerable<bool> Inferno(UnitState user, List<int> targets, Action<int> hit)
    {
        var he = host.HeavenEarthAb;
        var random = host.Btl._fxRandom;
        var (ux, uy) = host.Btl.UnitFoot(user);
        double t0 = host._lastTime, aura = uy - 100 * LiftY;
        double first = t0 + Tick(80), second = first + he.OnceSeconds(215, 3), third = second + Tick(200);
        Fx(215, 3, first, ux, aura);
        Fx(215, 4, second, ux, aura, 200);
        Fx(215, 5, third, ux, aura);
        foreach (bool _ in Wait(t0 + he.PlayRawMotion(user, 48, loop: false))) yield return true;

        double t1 = host._lastTime;
        he.PlayRawMotion(user, 49, loop: true, holdSeconds: 30);
        double fromX = ux + 23, fromY = uy - 80 * LiftY;
        var strikes = new List<(double At, int Target)>();
        var sparks = new List<(double Start, double X, double Y, double Vx, double Vy)>();
        void Ball(double toX, double toY, int target)
        {
            int delay = random.Next(150);
            double dist = Math.Sqrt(Math.Pow(toX - fromX, 2) + Math.Pow((toY - fromY) / WorldY, 2));
            int flight = (int)Math.Ceiling(dist / 15);
            double start = t1 + Tick(delay), land = start + Tick(flight + 2);
            for (int k = 0; k <= flight; k++)
            {
                double f = flight == 0 ? 1 : (double)k / flight, x = fromX + (toX - fromX) * f, y = fromY + (toY - fromY) * f, at = start + Tick(k);
                Fx(475, 2, at, x, y);
                double way = dist > 0 ? 6 * (toX - fromX) / dist : 0, wayY = dist > 0 ? 6 * (toY - fromY) / dist : 0;
                sparks.Add((at, x, y, way + (random.Next(2) * 2 - 1) * (random.Next(8) + 4), wayY + (random.Next(2) * 2 - 1) * (random.Next(8) + 4) * WorldY));
            }
            Sound(1435, 0, start);
            Fx(1211, 0, land, toX, toY);
            Sound(1435, 1, land);
            if (target >= 0) strikes.Add((land + Tick(27), target));
        }
        foreach (int t in targets)
        {
            var (tx, ty) = host.Btl.UnitFoot(host._units[t]);
            Ball(tx, ty, t);
        }
        for (int k = targets.Count; k < 25; k++)
        {
            int dc = random.Next(15) - 7, dr = random.Next(15) - 7;
            if (Math.Abs(dc) + Math.Abs(dr) > 7) { k--; continue; }
            int c = user.Col + dc, r = user.Row + dr;
            if ((uint)c >= host.Cols || (uint)r >= host.Rows) { k--; continue; }
            var (tx, ty) = CellFoot(c, r);
            Ball(tx, ty, -1);
        }
        host.StagingAb._stageDraws.Add(() =>
        {
            bool alive = false;
            foreach (var s in sparks)
            {
                double age = (host._lastTime - s.Start) * TicksPerSecond;
                if (age >= 18) continue;
                alive = true;
                if (age < 0) continue;
                double travel = (1 - Math.Pow(0.85, age)) / 0.15;
                host.DrawUi(472, 0, (int)age, (int)(s.X + s.Vx * travel), (int)(s.Y + s.Vy * travel), UiBlend.Add, loop: false);
            }
            return alive;
        });

        bool ended = false;
        double closeAt = t1 + Tick(140);
        foreach (var (at, target) in strikes.OrderBy(s => s.At))
        {
            if (!ended && at >= closeAt)
            {
                foreach (bool _ in Wait(closeAt)) yield return true;
                he.PlayRawMotion(user, 50, loop: false);
                ended = true;
            }
            foreach (bool _ in Wait(at)) yield return true;
            hit(target);
        }
        if (!ended)
        {
            foreach (bool _ in Wait(closeAt)) yield return true;
            he.PlayRawMotion(user, 50, loop: false);
        }
        user.PlayAction(ObsMotionTable.ActionStand, 0);
    }

    private IEnumerable<bool> Mushin(UnitState user, Action<int> hit)
    {
        host._sprites.TryGetValue(user.ChrCode, out var sprite);
        int first = Math.Max(1, (sprite?.MotionTicks(48) ?? 139) - 1), loop = Math.Max(1, sprite?.MotionTicks(49) ?? 7);
        double t0 = host._lastTime, second = t0 + Tick(first), end = Math.Max(t0 + Tick(230), second + Tick(105));
        var (ux, uy) = host.Btl.UnitFoot(user);
        user.Fade = 0;
        foreach (var (tick, sound) in sprite?.RawClip(48)?.Sounds ?? []) host._pendingSounds.Add((t0 + Tick(tick), sound));

        void Clone(int motion, int tick, double scale)
        {
            if (sprite?.FrameOfMotion(motion, tick, false) is not { } f) return;
            int w = Math.Max(1, (int)(f.W * scale)), h = Math.Max(1, (int)(f.H * scale));
            var px = new uint[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f.Px[Math.Min(f.H - 1, y * f.H / h) * f.W + Math.Min(f.W - 1, x * f.W / w)];
            host.BlitMasked(px, w, h, ux + (int)(f.X * scale), uy + (int)(f.Y * scale));
        }
        host.StagingAb._stageDraws.Add(() =>
        {
            if (host._lastTime >= end) return false;
            int t = (int)((host._lastTime - t0) * TicksPerSecond);
            if (t < first) Clone(48, t, Math.Pow(1.03, Math.Clamp(t - 100 + 1, 0, 20)));
            else if (t - first < 105) Clone(49, (t - first) % loop, 1.806 * Math.Pow(0.97087, Math.Clamp(t - first - 90, 0, 15)));
            return true;
        });
        Fx(902, 9, second, ux, uy, 105);
        Sound(1432, 0, second);
        double stone = t0 + Tick(140);
        foreach (var (dx, dy) in new[] { (-80, 0), (80, 0), (0, 80) }) Fx(324, 1, stone, ux + dx, uy + dy * WorldY - 50 * LiftY, 88);

        for (int t = 112; t <= 228; t += 4)
        {
            foreach (bool _ in Wait(t0 + Tick(t))) yield return true;
            int reach = t < 150 ? 2 : t < 190 ? 3 : 4;
            for (int i = 0; i < host._units.Length; i++)
                if (host._units[i] is { Alive: true, OnField: true } foe && BattleScene.SeesAsFoe(user, foe)
                    && Math.Abs(foe.Col - user.Col) + Math.Abs(foe.Row - user.Row) <= reach)
                    hit(i);
        }
        foreach (bool _ in Wait(end)) yield return true;
        user.Fade = 1;
        user.PlayAction(ObsMotionTable.ActionStand, 0);
    }

    private IEnumerable<bool> Comet(WorkData w, UnitState user, List<int> targets, Action<int> hit)
    {
        var he = host.HeavenEarthAb;
        var random = host.Btl._fxRandom;
        foreach (bool _ in Wait(host._lastTime + he.PlayRawMotion(user, 90, loop: false))) yield return true;
        user.PlayAction(ObsMotionTable.ActionStand, 0);

        double t0 = host._lastTime, rise = t0 + Tick(268);
        var (ux, uy) = host.Btl.UnitFoot(user);
        Fx(1433, 0, t0, ux, uy);
        Sound(1433, 0, t0);
        he._shakes.Add((t0, t0 + Tick(250), 2, false));
        double Height(double t) => t < 268 ? 150 + (Math.Abs((t - 8) % 30 - 15) / 15 * 16 - 8) : Math.Min(600, 142 + (t - 268) * 60);

        var rocks = new List<(int Motion, double Start, double X, double Y, List<double> Rise, List<(double X, double Y)> Fall)>();
        for (int i = 0; i < 50; i++)
        {
            int m = random.Next(6);
            double sx = ux - 280 + random.Next(560), sy = uy + (random.Next(440) - 200) * WorldY, start = t0 + Tick(random.Next(180));
            var up = new List<double>();
            for (double z = 0, speed = 14; z < 120; speed = Math.Max(1, speed * 0.85)) up.Add(z = Math.Min(120, z + speed));
            var down = new List<(double, double)>();
            double fromY = sy - 120 * LiftY, toY = uy - 150 * LiftY, dist = Math.Sqrt(Math.Pow(ux - sx, 2) + Math.Pow((toY - fromY) / WorldY, 2));
            for (double run = 0, speed = 2; run < dist; speed = Math.Min(40, speed * 1.3))
            {
                run = Math.Min(dist, run + speed);
                double f = dist > 0 ? run / dist : 1;
                down.Add((sx + (ux - sx) * f, fromY + (toY - fromY) * f));
            }
            rocks.Add((m, start, sx, sy, up, down));
            if (m >= 4)
            {
                double landed = start + Tick(up.Count + down.Count);
                Fx(705, 1, landed, ux, uy - 120 * LiftY);
                Sound(1433, 1, landed);
            }
        }
        double end = t0 + Tick(277);
        Sound(1433, 2, rise);
        host.StagingAb._stageDraws.Add(() =>
        {
            if (host._lastTime >= end) return false;
            double t = (host._lastTime - t0) * TicksPerSecond;
            foreach (var r in rocks)
            {
                int age = (int)((host._lastTime - r.Start) * TicksPerSecond);
                if (age < 0 || age >= r.Rise.Count + r.Fall.Count) continue;
                var (x, y) = age < r.Rise.Count ? (r.X, r.Y - r.Rise[age] * LiftY) : r.Fall[age - r.Rise.Count];
                host.DrawUi(251, r.Motion, age, (int)x, (int)y, UiBlend.Alpha, loop: true);
            }
            if (t >= 20)
            {
                int age = (int)t - 20, cy = uy - (int)(Height(t) * LiftY);
                host.DrawUi(200, 0, age, ux, cy, UiBlend.Add, loop: true);
                if (age < 13) host.DrawUi(215, 3, age, ux, cy, UiBlend.Add, loop: false);
                else host.DrawUi(215, 4, age - 13, ux, cy, UiBlend.Add, loop: true);
            }
            return true;
        });
        foreach (bool _ in Wait(end)) yield return true;

        host.Cut.Start(w);
        while (host.Cut.Playing) yield return true;
        foreach (int t in targets) hit(t);
    }
}
