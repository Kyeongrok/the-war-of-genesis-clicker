using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 판정 앞 연출 — 핸들러가 틱을 세며 대상을 돌리거나 복제를 띄우는 기술(ba-20 P6·P7·P8).
/// 밸런싱(대상이 돈다)·웹폰 크래쉬(대상 복제가 가로로 부푼다)·블랙홀(대상 복제가 소용돌이로 빨려 든다).
/// </summary>
internal sealed unsafe class StagingSkill(GameWindow host)
{
    /// <summary>하드 밸런싱 0x100b7d20(426 · 1376~1394) / 소프트 밸런싱 0x100b8110(427 · 1357~1375) — 59~78 은 격려였다(ba-21 fx 표 재생성 기록 11).</summary>
    internal static bool IsBalancingWork(int id) => id is 426 or 427 || id is >= 1376 and <= 1394 || id is >= 1357 and <= 1375;

    /// <summary>웹폰 크래쉬 0x100a20a0(442 · 1503~1506).</summary>
    internal static bool IsWeaponCrashWork(int id) => id == 442 || id is >= 1503 and <= 1506;

    /// <summary>블랙홀 0x1008d200(489 · 978~986).</summary>
    internal static bool IsBlackHoleWork(int id) => id == 489 || id is >= 978 and <= 986;

    /// <summary>더블 브레이크 0x100825e0(work 393).</summary>
    internal const int DoubleBreakWork = 393;

    /// <summary>
    /// 핸들러가 거는 화면 흔들림(생성자 0x100c6f70 가로 · 0x100c7120 세로, 세기 0x100cda20, 길이 0x100c2530 — 직접 호출 23곳 전수, ba-20 O P8).
    /// 세기·길이는 원본 값이고, 시작은 이펙트가 뜨는 때부터 차례로 잇는다(단계별 시작 틱은 못 뽑았다 — 가설).
    /// </summary>
    internal void SpawnWorkShakes(WorkData w)
    {
        (int Strength, int Ticks, bool Vertical, int Delay)[] shakes = w.Id switch
        {
            1523 => [(2, 30, false, 0)],                                          // 타이타니아 슈발츠
            1528 => [(2, 250, false, 0), (3, 180, false, 0)],                     // 코메트
            1589 => [(2, 15, false, 0), (2, 15, false, 30)],                      // 아수라 파천무
            1590 => [(2, 145, true, 0)],                                          // 진무 천지파열
            1665 => [(3, 210, true, 40)],                                         // 레드 크로스
            _ => w.AbilityId switch
            {
                6 => [(2, 4, false, 0)],                                          // 폭 0x10083660(세기 2, 4틱 — ba-21 fx F17)
                74 => [(1, 50, false, 0), (3, 25, false, 0), (5, 60, false, 0), (3, 45, false, 0), (1, 30, false, 0)],   // 어스퀘이크 0x100ad9a0
                67 => [(1, 170, false, 0)],                                       // 엘레맨탈 베이스 0x100a37d0
                111 => [(2, 100, false, 0)],                                      // 그라비티 밸런스 0x100a2b00(20n+80)
                _ => [],
            },
        };
        double at = host._lastTime;
        foreach (var (strength, ticks, vertical, delay) in shakes)
        {
            at += delay / TicksPerSecond;
            host.HeavenEarthAb._shakes.Add((at, at + ticks / TicksPerSecond, strength, vertical));
            at += ticks / TicksPerSecond;
        }
    }

    /// <summary>그리는 동안 틀마다 불리는 연출 — false 를 돌려주면 끝난 것이다.</summary>
    internal readonly List<Func<bool>> _stageDraws = [];

    internal void DrawStageFx()
    {
        for (int i = _stageDraws.Count - 1; i >= 0; i--)
            if (!_stageDraws[i]()) _stageDraws.RemoveAt(i);
    }

    /// <summary>치는 단계에서 판정 바로 앞에 도는 연출. 연출이 없는 기술은 곧장 끝난다.</summary>
    internal IEnumerable<bool> StageBeforeHit(WorkData w, UnitState a, int targetIndex, int col, int row)
    {
        if (w.Id == DoubleBreakWork)
        {
            // 분신 A(시전자 Obs 모션 75)가 앞 한 칸 자리에서 「범위 최대」칸을 40px/틱(틱마다 ×0.92, 최소 6)으로 날아가고,
            // 이어 분신 B 가 그 자리에서 10px/틱(×1.11, 최대 40)으로 돌아온다(0x10082756~0x10082894). 높이 +30/+20.
            // 판정은 A 가 다 날아간 뒤 한 번에 낸다(원본은 칸을 넘을 때마다 3칸 띠 — 범위는 같다).
            if (!host._sprites.TryGetValue(a.ChrCode, out var sprite) || sprite.MotionTicks(75) <= 0) yield break;
            int n = Math.Max(1, w.AreaMaxQuarters / 4);
            var (dx, dy) = a.Facing switch { Facing.Up => (0, -1), Facing.Down => (0, 1), Facing.Left => (-1, 0), _ => (1, 0) };
            var (ox, oy) = host.UnitFoot(a);
            double lead = dx != 0 ? 50 : 40, total = 40.0 * n;
            bool mirror = a.Facing == Facing.Right;
            var outward = new List<double>();
            for (double pos = 0, speed = 40; pos < total; speed = Math.Max(6, speed * 0.92)) { pos = Math.Min(total, pos + speed); outward.Add(pos); }
            var back = new List<double>();
            for (double pos = total, speed = 10; pos > 0; speed = Math.Min(40, speed * 1.11)) { pos = Math.Max(0, pos - speed); back.Add(pos); }
            double flyAt = host._lastTime;
            _stageDraws.Add(() =>
            {
                int k = (int)((host._lastTime - flyAt) * TicksPerSecond);
                if (k < 0 || k >= outward.Count + back.Count) return false;
                double pos = lead + (k < outward.Count ? outward[k] : back[k - outward.Count]);
                int lift = k < outward.Count ? 18 : 12;
                if (sprite.FrameOfMotion(75, Math.Min(k, sprite.MotionTicks(75) - 1), mirror) is not { } frame) return false;
                host.BlitMasked(frame.Px, frame.W, frame.H, ox + (int)(dx * pos) + frame.X, oy + (int)(dy * pos * 0.8) - lift + frame.Y, fade: 24 / 31.0);
                return true;
            });
            while ((host._lastTime - flyAt) * TicksPerSecond < outward.Count) yield return true;
            yield break;
        }
        if (!IsBalancingWork(w.Id) && !IsWeaponCrashWork(w.Id) && !IsBlackHoleWork(w.Id)) yield break;
        var targets = (targetIndex >= 0 ? [targetIndex] : host.WorkTargets(w, a, col, row)).Select(i => host._units[i]).ToList();
        double start = host._lastTime;
        int Tick() => (int)((host._lastTime - start) * TicksPerSecond);

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
            foreach (var u in targets.Where(u => u.Alive && host._sprites.ContainsKey(u.ChrCode)))
            {
                var frame = host._sprites[u.ChrCode].FrameFor(u);
                var (fx, fy) = host.UnitFoot(u);
                if (frame == null) continue;
                host.PlayActionFor(u, HitAction, 60);
                _stageDraws.Add(() =>
                {
                    int n = (int)((host._lastTime - start) * TicksPerSecond);
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
        var (cx, cy) = host.UnitFoot(a);
        double swirlAt = host._lastTime;
        var pulled = targets.Where(u => u != a && u.Alive && host._sprites.ContainsKey(u.ChrCode)).ToList();
        foreach (var u in pulled)
        {
            var frame = host._sprites[u.ChrCode].FrameFor(u);
            var (ux, uy) = host.UnitFoot(u);
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
                int n = (int)((host._lastTime - swirlAt) * TicksPerSecond);
                if (u.Alive) u.Fade = n < 150 ? Math.Max(8 / 31.0, 1 - n / 12.0 * (1 - 8 / 31.0))
                                              : Math.Min(1, 8 / 31.0 + (n - 150) / 35.0 * (1 - 8 / 31.0));
                if (n >= 185) { u.Fade = 1; return false; }
                foreach (var (late, fade) in new[] { (20, 4 / 31.0), (15, 8 / 31.0), (10, 8 / 31.0), (5, 12 / 31.0), (0, 24 / 31.0) })
                {
                    int i = n - 20 - late;
                    if (i < 0 || i >= 100) continue;
                    host.BlitMasked(frame.Px, frame.W, frame.H, path[i].X + frame.X, path[i].Y + frame.Y, fade: fade);
                }
                return true;
            });
        }
        while (Tick() < 200) yield return true;
    }

    /// <summary>컷을 가로로만 늘여 그린다 — 가로는 컷 가운데 기준, 세로는 그대로.</summary>
    internal void BlitStretched(SpriteFrame frame, int footX, int footY, double scale, double fade)
    {
        int w = Math.Max(1, (int)(frame.W * scale));
        var px = new uint[w * frame.H];
        for (int y = 0; y < frame.H; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = frame.Px[y * frame.W + Math.Min(frame.W - 1, x * frame.W / w)];
        host.BlitMasked(px, w, frame.H, footX + frame.X - (w - frame.W) / 2, footY + frame.Y, fade: fade);
    }
}
