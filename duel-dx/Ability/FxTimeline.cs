using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 핸들러 이펙트를 옮길 때 같이 쓰는 틀(gm-skil-22) — 마디를 따라 움직이는 알갱이, 정해진 틱에 한 번 도는 일, 몸 윤곽 점.
/// 단위는 화면 픽셀(월드 y × 0.8 · 높이 × 0.6 은 부르는 쪽이 셈한다), 틱은 30/s.
/// </summary>
internal sealed unsafe class FxTimeline(GameWindow host)
{
    /// <summary>
    /// 마디를 따라가는 알갱이 — Start 에 떠서 마디(틱, 자리) 사이를 곧게 간다(같은 자리 마디 둘 = 붙듦). 마지막 마디의 틱에 사라진다.
    /// 원본의 「지연 → 붙듦 → 포물선 이동기(0x100cb550 — 실제로는 곧은 길) → 사슬」을 한 줄로 적는다.
    /// </summary>
    internal readonly List<(int Obs, int Motion, double Start, (int Tick, double X, double Y)[] Path)> _grains = [];

    /// <summary>정해진 때에 한 번 도는 일 — 연출용(자세 · 카메라)만 건다. 판정은 걸지 않는다.</summary>
    internal readonly List<(double At, Action Run)> _later = [];

    internal double At(int tick) => host._lastTime + tick / TicksPerSecond;
    internal int Rand(int n) => host.Btl._fxRandom.Next(Math.Max(1, n));

    internal void Clear()
    {
        _grains.Clear();
        _later.Clear();
    }

    internal void Later(int tick, Action run) => _later.Add((At(tick), run));

    internal void Grain(int obs, int motion, int tick, params (int Tick, double X, double Y)[] path) => _grains.Add((obs, motion, At(tick), path));

    /// <summary>이펙트 한 장(수명이 있으면 그동안 되풀이) — 소리는 안 낸다(수십 장을 까는 줄에 쓴다. 소리까지는 <see cref="ForcetrulSkill.Fx"/>).</summary>
    internal void Put(int obs, int motion, int tick, double x, double y, int life = 0, bool mirror = false)
    {
        if (life > 0) host.HeavenEarthAb.AddTimedFx(obs, motion, At(tick), (x, y), life, mirror);
        else (mirror ? host.Btl._effectMirrors : host.Btl._effects).Add((obs, motion, At(tick), (int)x, (int)y));
    }

    internal List<UnitState> Targets(WorkData w, UnitState user, int col, int row) =>
        [.. (host.Btl._fxTargets ?? host.Btl.WorkTargets(w, user, col, row)).Select(i => host._units[i])];

    /// <summary>유닛 지금 컷의 윤곽 점(발 기준) — 가로·세로 이웃이 하나라도 빈 픽셀(0x1000ce10, 격려 · 희생과 같은 함수).</summary>
    internal List<(int X, int Y)> Outline(UnitState u)
    {
        var points = new List<(int X, int Y)>();
        if (!host._sprites.TryGetValue(u.ChrCode, out var sprite) || sprite.FrameFor(u) is not { } f) return points;
        bool Solid(int px, int py) => (uint)px < f.W && (uint)py < f.H && (f.Px[py * f.W + px] & 0xFF000000) != 0;
        for (int py = 0; py < f.H; py++)
            for (int px = 0; px < f.W; px++)
                if (Solid(px, py) && (!Solid(px - 1, py) || !Solid(px + 1, py) || !Solid(px, py - 1) || !Solid(px, py + 1)))
                    points.Add((f.X + px, f.Y + py));
        return points;
    }

    /// <summary>
    /// 고리 넷(0x100cd310 ×4) — 가운데 둘레를 반지름 R0 → R1, 처음 각 0 · 0.5π · π · 1.5π 에서 틱당 0.02π 로 20틱 돌며 틱마다 그림을 남긴다
    /// (화이어 웨이브 183:2 · 익스퍼트 블레스트 183:11 · 썬더 스톰 218:2 가 같은 값).
    /// </summary>
    internal void Rings(int obs, int motion, double cx, double cy, int r0, int r1, int ticks = 20)
    {
        for (int q = 0; q < 4; q++)
        {
            var points = new (int X, int Y)[ticks];
            double a = q * 0.5 * Math.PI;
            for (int t = 0; t < ticks; t++, a += 0.02 * Math.PI)
            {
                double r = Math.Truncate(r0 + t * (double)(r1 - r0) / ticks);
                points[t] = ((int)(cx + r * Math.Cos(a)), (int)(cy + r * Math.Sin(a) * 0.8));
            }
            host.Btl._ringTrails.Add((obs, motion, host._lastTime, points, false));
        }
    }

    internal void Draw()
    {
        for (int i = _later.Count - 1; i >= 0; i--)
        {
            if (host._lastTime < _later[i].At) continue;
            var run = _later[i].Run;
            _later.RemoveAt(i);
            run();
        }
        _grains.RemoveAll(g =>
        {
            if (host._lastTime < g.Start) return false;
            int tick = (int)((host._lastTime - g.Start) * TicksPerSecond);
            var path = g.Path;
            if (tick >= path[^1].Tick || host.UiFor(g.Obs) is not { } sprite) return true;
            int k = 0;
            while (k + 2 < path.Length && tick >= path[k + 1].Tick) k++;
            var (from, to) = (path[k], path[Math.Min(k + 1, path.Length - 1)]);
            double f = to.Tick > from.Tick ? Math.Clamp((tick - from.Tick) / (double)(to.Tick - from.Tick), 0, 1) : 1;
            int key = sprite.BlendAt(g.Motion, tick);
            host.DrawUi(g.Obs, g.Motion, tick, (int)(from.X + (to.X - from.X) * f), (int)(from.Y + (to.Y - from.Y) * f),
                        key is (>= 1 and <= 8) or 10 or 12 ? BlendOf(key) : UiBlend.Add, loop: true, fade: BlendFade(key));
            return false;
        });
    }
}
