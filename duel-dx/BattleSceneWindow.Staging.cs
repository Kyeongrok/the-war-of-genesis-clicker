using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 판정 앞 연출 — 핸들러가 틱을 세며 대상을 돌리거나 복제를 띄우는 기술(ba-20 P6·P7·P8).
/// 밸런싱(대상이 돈다)·웹폰 크래쉬(대상 복제가 가로로 부푼다)·블랙홀(대상 복제가 소용돌이로 빨려 든다).
/// </summary>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>하드 밸런싱 0x100b7d20(426 · 1376~1394) / 소프트 밸런싱 0x100b8110(59~78).</summary>
    private static bool IsBalancingWork(int id) => id == 426 || id is >= 1376 and <= 1394 || id is >= 59 and <= 78;

    /// <summary>웹폰 크래쉬 0x100a20a0(442 · 1503~1506).</summary>
    private static bool IsWeaponCrashWork(int id) => id == 442 || id is >= 1503 and <= 1506;

    /// <summary>블랙홀 0x1008d200(489 · 978~986).</summary>
    private static bool IsBlackHoleWork(int id) => id == 489 || id is >= 978 and <= 986;

    /// <summary>더블 브레이크 0x100825e0(work 393).</summary>
    private const int DoubleBreakWork = 393;

    /// <summary>그리는 동안 틀마다 불리는 연출 — false 를 돌려주면 끝난 것이다.</summary>
    private readonly List<Func<bool>> _stageDraws = [];

    private void DrawStageFx()
    {
        for (int i = _stageDraws.Count - 1; i >= 0; i--)
            if (!_stageDraws[i]()) _stageDraws.RemoveAt(i);
    }

    /// <summary>치는 단계에서 판정 바로 앞에 도는 연출. 연출이 없는 기술은 곧장 끝난다.</summary>
    private IEnumerable<bool> StageBeforeHit(WorkData w, UnitState a, int targetIndex, int col, int row)
    {
        if (w.Id == DoubleBreakWork)
        {
            // 분신 A(시전자 Obs 모션 75)가 앞 한 칸 자리에서 「범위 최대」칸을 40px/틱(틱마다 ×0.92, 최소 6)으로 날아가고,
            // 이어 분신 B 가 그 자리에서 10px/틱(×1.11, 최대 40)으로 돌아온다(0x10082756~0x10082894). 높이 +30/+20.
            // 판정은 A 가 다 날아간 뒤 한 번에 낸다(원본은 칸을 넘을 때마다 3칸 띠 — 범위는 같다).
            if (!_sprites.TryGetValue(a.ChrCode, out var sprite) || sprite.MotionTicks(75) <= 0) yield break;
            int n = Math.Max(1, w.AreaMaxQuarters / 4);
            var (dx, dy) = a.Facing switch { Facing.Up => (0, -1), Facing.Down => (0, 1), Facing.Left => (-1, 0), _ => (1, 0) };
            var (ox, oy) = UnitFoot(a);
            double lead = dx != 0 ? 50 : 40, total = 40.0 * n;
            bool mirror = a.Facing == Facing.Right;
            var outward = new List<double>();
            for (double pos = 0, speed = 40; pos < total; speed = Math.Max(6, speed * 0.92)) { pos = Math.Min(total, pos + speed); outward.Add(pos); }
            var back = new List<double>();
            for (double pos = total, speed = 10; pos > 0; speed = Math.Min(40, speed * 1.11)) { pos = Math.Max(0, pos - speed); back.Add(pos); }
            double flyAt = _lastTime;
            _stageDraws.Add(() =>
            {
                int k = (int)((_lastTime - flyAt) * TicksPerSecond);
                if (k < 0 || k >= outward.Count + back.Count) return false;
                double pos = lead + (k < outward.Count ? outward[k] : back[k - outward.Count]);
                int lift = k < outward.Count ? 18 : 12;
                if (sprite.FrameOfMotion(75, Math.Min(k, sprite.MotionTicks(75) - 1), mirror) is not { } frame) return false;
                BlitMasked(frame.Px, frame.W, frame.H, ox + (int)(dx * pos) + frame.X, oy + (int)(dy * pos * 0.8) - lift + frame.Y, fade: 24 / 31.0);
                return true;
            });
            while ((_lastTime - flyAt) * TicksPerSecond < outward.Count) yield return true;
            yield break;
        }
        if (!IsBalancingWork(w.Id) && !IsWeaponCrashWork(w.Id) && !IsBlackHoleWork(w.Id)) yield break;
        var targets = (targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row)).Select(i => _units[i]).ToList();
        double start = _lastTime;
        int Tick() => (int)((_lastTime - start) * TicksPerSecond);

        if (IsBalancingWork(w.Id))
        {
            // 틱 40~98 은 짝수 틱마다(30번), 102~129 는 3틱마다(10번) 대상을 시계 방향으로 90° 돌린다 — 모두 10바퀴,
            // 틱 131 에 판정(0x100b7fd8~0x100b8073, 표 0x100b80e8: 위 → 오른 → 아래 → 왼).
            int done = 0;
            while (Tick() <= 130)
            {
                int t = Tick();
                int due = t < 40 ? 0 : t < 100 ? (t - 40) / 2 + 1 : 30 + Math.Min(10, (t - 99) / 3);
                for (; done < due; done++)
                    foreach (var u in targets.Where(u => u.Alive))
                        u.Facing = u.Facing switch { Facing.Up => Facing.Right, Facing.Right => Facing.Down, Facing.Down => Facing.Left, _ => Facing.Up };
                yield return true;
            }
            // 틀을 건너뛰어 덜 돈 것을 마저 돈다 — 40번(10바퀴)이라야 처음 방향으로 돌아온다.
            for (; done < 40; done++)
                foreach (var u in targets.Where(u => u.Alive))
                    u.Facing = u.Facing switch { Facing.Up => Facing.Right, Facing.Right => Facing.Down, Facing.Down => Facing.Left, _ => Facing.Up };
            yield break;
        }

        if (IsWeaponCrashWork(w.Id))
        {
            // 대상의 그 순간 컷을 섞기 4(52%)로 띄워 120틱 동안 가로로만 1.00 → 1.57, 1.40 → 0.83 을 40틱 주기로 세 번
            // (0x100a21b0~0x100a2234, 그리기 0x100c6bf0 — 가로는 컷 가운데, 세로는 발 기준). 대상은 맞음 자세를 붙든다.
            foreach (var u in targets.Where(u => u.Alive && _sprites.ContainsKey(u.ChrCode)))
            {
                var frame = _sprites[u.ChrCode].FrameFor(u);
                var (fx, fy) = UnitFoot(u);
                if (frame == null) continue;
                PlayActionFor(u, HitAction, 60);
                _stageDraws.Add(() =>
                {
                    int n = (int)((_lastTime - start) * TicksPerSecond);
                    if (n >= 120 || n < 0) return false;
                    int k = n % 40;
                    double scale = k < 20 ? 1.0 + 0.03 * k : 1.4 - 0.03 * (k - 20);
                    BlitStretched(frame, fx, fy, scale, 16 / 31.0);
                    return true;
                });
            }
            // 판정은 틱 60 에 붙는 645 가 끝난 뒤다 — 여기서는 60틱만 기다린다(645 는 표가 이미 띄웠다).
            while (Tick() < 60) yield return true;
            yield break;
        }

        // 블랙홀 — 틱 50 에 시전자를 뺀 대상마다: 본체가 12틱에 26% 까지 옅어지고, 그 순간 컷의 복제(섞기 6)가 20틱 뒤부터
        // 100틱 동안 시전자 둘레를 약 2.3바퀴 돌며 빨려 든다(길 표 0x10039410). 잔상 넷이 5·10·15·20틱 늦게 따라온다.
        // 틱 200 에 판정, 본체는 35틱에 걸쳐 돌아온다(0x1008d55e~0x1008d8fa).
        while (Tick() < 50) yield return true;
        var (cx, cy) = UnitFoot(a);
        double swirlAt = _lastTime;
        var pulled = targets.Where(u => u != a && u.Alive && _sprites.ContainsKey(u.ChrCode)).ToList();
        foreach (var u in pulled)
        {
            var frame = _sprites[u.ChrCode].FrameFor(u);
            var (ux, uy) = UnitFoot(u);
            if (frame == null) continue;
            // 길 100점: 반지름 r0 → 10, 각속도 0.015 에서 틱마다 ×1.02(각도 단위 π). 화면 y 는 ×0.8.
            double wx = ux - cx, wy = (uy - cy) / 0.8, r0 = Math.Sqrt(wx * wx + wy * wy), angle = Math.Atan2(wy, wx) / Math.PI, speed = 0.015, r = r0;
            if (angle < 0) angle += 2;
            var path = new (int X, int Y)[100];
            for (int i = 0; i < 100; i++)
            {
                path[i] = (cx + (int)(r * Math.Cos(angle * Math.PI)), cy + (int)(r * Math.Sin(angle * Math.PI) * 0.8));
                speed *= 1.02;
                angle += speed;
                r += (10 - r0) / 100;
            }
            _stageDraws.Add(() =>
            {
                int n = (int)((_lastTime - swirlAt) * TicksPerSecond);
                if (u.Alive) u.Fade = n < 150 ? Math.Max(8 / 31.0, 1 - n / 12.0 * (1 - 8 / 31.0))
                                              : Math.Min(1, 8 / 31.0 + (n - 150) / 35.0 * (1 - 8 / 31.0));
                if (n >= 185) { u.Fade = 1; return false; }
                foreach (var (late, fade) in new[] { (20, 4 / 31.0), (15, 8 / 31.0), (10, 8 / 31.0), (5, 12 / 31.0), (0, 24 / 31.0) })
                {
                    int i = n - 20 - late;
                    if (i < 0 || i >= 100) continue;
                    BlitMasked(frame.Px, frame.W, frame.H, path[i].X + frame.X, path[i].Y + frame.Y, fade: fade);
                }
                return true;
            });
        }
        while (Tick() < 200) yield return true;
    }

    /// <summary>컷을 가로로만 늘여 그린다 — 가로는 컷 가운데 기준, 세로는 그대로.</summary>
    private void BlitStretched(SpriteFrame frame, int footX, int footY, double scale, double fade)
    {
        int w = Math.Max(1, (int)(frame.W * scale));
        var px = new uint[w * frame.H];
        for (int y = 0; y < frame.H; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = frame.Px[y * frame.W + Math.Min(frame.W - 1, x * frame.W / w)];
        BlitMasked(px, w, frame.H, footX + frame.X - (w - frame.W) / 2, footY + frame.Y, fade: fade);
    }
}
