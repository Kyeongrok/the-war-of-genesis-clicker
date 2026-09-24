using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 천지 파열무(어빌리티 163, work 1591) — 시전자를 지나는 X 자 두 대각선으로 땅이 터지고 갈라진 뒤, 범위 안 대상마다 폭발한다.
/// </summary>
/// <remarks>
/// <para>원본 핸들러 <c>0x100b43d0</c>(단계 0~4, 필살기 공통 앞머리 <see cref="FinisherPrelude"/> 뒤). 대각선은 시전자 월드 자리에서
/// (x+d, y+d)·(x+d, y−d), d = −280 ~ 280 을 40(한 칸)씩 15곳이고, 곳마다 i%3 을 v 로 쓴다. 첫 대각선만 그림을 뒤집는다(<c>0x100e56f0(3)</c>).
/// 월드 → 화면: x 그대로, y × 0.8, 높이 z × 0.6.</para>
/// <list type="number">
/// <item>단계 1 — 세로 흔들림 3(40틱, <c>0x100c7120</c>). 곳마다 |k|×4 틱 뒤 1014:(7−v)(v=2 면 4·5 중 하나) 터짐 → 1014:(13−2v) 불길(90−|k|×4 틱),
/// 파편 251:(0~2) 두 개를 (±5, ±5, 20~79) 로 튕겨 올린다(<c>0x100c5d40</c>: 매 틀 x·y 에 속도×0.5, z 속도 −5, 땅에 닿으면 ×0.3 으로 튄다, 200틱). 소리 1438:0.</item>
/// <item>단계 2(60틱 뒤) — 소리 1438:3, 가로 흔들림 5(20틱). 대각선 곳마다 2/3 로 갈라짐 1014:v, 50 간격 11곳에 1211:0(20+|k| 틱 뒤).
/// 21틱 뒤 가로 흔들림 8(24틱), 20틱 뒤 소리 1438:1.</item>
/// <item>단계 3(30틱 뒤) — 범위 안 대상 n 명: 가로 흔들림 4(5n+30 틱). 대상 j 마다 5j 틱 뒤 폭발 111:0(여기서 피해) · 소리 1438:6 ·
/// 땅의 대상은 1014:22 → 1014:23, 떠 있는 대상은 24 → 25(5n+100 틱). 대각선 곳마다 불길 1014:(13−2v)(5n+30 틱) → 1014:(19−2v) →
/// 30틱 뒤 터짐 1014:(7−v)·반반으로 갈라짐 1014:v. 5n+68 틱 뒤 가로 흔들림 5(35틱), 5n+60 틱 뒤 소리 1438:7.</item>
/// </list>
/// <para>효과 시간 규칙은 <see cref="FinisherPrelude"/> 와 같다 — <c>0x100c2530</c> 수명(0 = 모션 한 번) · <c>0x100c24d0</c> 시작 지연 ·
/// <c>0x100c2640(e, 0, n)</c> e 가 끝나고 (n 틱 더 기다려) 시작. 떠 있는 대상(<c>+0x129</c>)은 데모에 없어 늘 땅의 것을 쓴다.</para>
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int HeavenEarthWork = 1591;
    private const int CrackObs = 1014, DebrisObs = 251, RuptureObs = 1211, BlastObs = 111, HeavenEarthSoundObs = 1438;

    /// <summary>시각을 정해 둔 효과 하나 — 끝이 null 이면 모션 한 번.</summary>
    private sealed record TimedFx(int Obs, int Motion, double Start, int X, int Y, double? End, bool Mirror);

    private readonly List<TimedFx> _timedFx = [];

    /// <summary>튕겨 오르는 파편 — 월드 좌표(x·y 화면 픽셀, z 높이)와 속도.</summary>
    private sealed class Debris
    {
        public int Obs, Motion;
        public double Start, X, Y, Z, Vx, Vy, Vz, Bounce;
        public int Age;
    }

    private readonly List<Debris> _debris = [];
    private double _debrisLastStep;

    /// <summary>화면 흔들림 — (끝나는 때, 세기, 세로인가). 원본 <c>0x100c6f70</c>(가로)·<c>0x100c7120</c>(세로) 는 틀마다 ±세기로 번갈아 민다.</summary>
    private readonly List<(double Start, double End, int Strength, bool Vertical)> _shakes = [];

    private double Ticks(double n) => n / TicksPerSecond;

    /// <summary>그 모션 한 번의 길이(초) — 자식까지는 안 본다.</summary>
    private double OnceSeconds(int obs, int motion) => Math.Max(1, UiFor(obs)?.MotionLength(motion) ?? 1) / TicksPerSecond;

    private void AddTimedFx(int obs, int motion, double start, (double X, double Y) at, double? lifeTicks, bool mirror) =>
        _timedFx.Add(new TimedFx(obs, motion, start, (int)at.X, (int)at.Y, lifeTicks is { } life ? start + Ticks(life) : null, mirror));

    private void HeavenEarthSound(int motion, double at)
    {
        if (_effectTables.GetValueOrDefault(HeavenEarthSoundObs)?.Clips.GetValueOrDefault(motion) is not { } clip) return;
        foreach (var (tick, sound) in clip.Sounds) _pendingSounds.Add((at + Ticks(tick), sound));
    }

    /// <summary>
    /// 천지 파열무 핸들러 — 연출을 시각표로 깔고, 대상마다 폭발하는 때에 피해를 준다(<paramref name="hit"/>). 다 끝날 때까지 돈다.
    /// </summary>
    private IEnumerable<bool> HeavenEarthRoutine(UnitState user, List<int> targets, Action<int> hit)
    {
        var (ux, uy) = UnitFoot(user);
        (double, double) Diagonal(int d, bool first) => (ux + d, uy + (first ? d : -d) * 0.8);
        int V(int k) => (k + 7) % 3;
        int Burst(int v) => 7 - v == 5 ? 5 - _rng.Next(2) : 7 - v;

        // ── 단계 1 ──
        double s1 = _lastTime;
        _shakes.Add((s1, s1 + Ticks(40), 3, true));
        HeavenEarthSound(0, s1);
        _debrisLastStep = s1;
        foreach (bool first in new[] { true, false })
            for (int k = -7; k <= 7; k++)
            {
                var at = Diagonal(40 * k, first);
                int v = V(k), burst = Burst(v);
                double start = s1 + Ticks(4 * Math.Abs(k));
                AddTimedFx(CrackObs, burst, start, at, null, first);
                AddTimedFx(CrackObs, 13 - 2 * v, start + OnceSeconds(CrackObs, burst), at, 90 - 4 * Math.Abs(k), first);
                for (int n = 0; n < 2; n++)
                    _debris.Add(new Debris
                    {
                        Obs = DebrisObs, Motion = _rng.Next(3), Start = start, X = at.Item1, Y = at.Item2,
                        Vx = _rng.Next(10) - 5, Vy = _rng.Next(10) - 5, Vz = _rng.Next(60) + 20, Bounce = 0.3,
                    });
            }
        for (double end = s1 + Ticks(60); _lastTime < end;) { StepDebris(); yield return true; }

        // ── 단계 2 ──
        double s2 = _lastTime;
        HeavenEarthSound(3, s2);
        _shakes.Add((s2, s2 + Ticks(20), 5, false));
        foreach (bool first in new[] { true, false })
        {
            for (int k = -7; k <= 7; k++)
                if (_rng.Next(3) != 0) AddTimedFx(CrackObs, V(k), s2, Diagonal(40 * k, first), null, first);
            for (int k = -5; k <= 5; k++)
                AddTimedFx(RuptureObs, 0, s2 + Ticks(20 + Math.Abs(k)), Diagonal(50 * k, first), null, false);
        }
        _shakes.Add((s2 + Ticks(21), s2 + Ticks(45), 8, false));
        HeavenEarthSound(1, s2 + Ticks(20));
        for (double end = s2 + Ticks(30); _lastTime < end;) { StepDebris(); yield return true; }

        // ── 단계 3 ──
        double s3 = _lastTime;
        int count = targets.Count, t = 5 * count;
        _shakes.Add((s3, s3 + Ticks(5 * count + 30), 4, false));
        var blasts = new List<(double At, int Target)>();
        for (int j = 0; j < count; j++)
        {
            var target = _units[targets[j]];
            var (tx, ty) = UnitFoot(target);
            double at = s3 + Ticks(5 * j);
            AddTimedFx(BlastObs, 0, at, (tx, ty), null, false);
            HeavenEarthSound(6, at);
            AddTimedFx(CrackObs, 22, at, (tx, ty), null, false);
            AddTimedFx(CrackObs, 23, at + OnceSeconds(CrackObs, 22), (tx, ty), 5 * count + 100, false);
            blasts.Add((at, targets[j]));
        }
        foreach (bool first in new[] { true, false })
            for (int k = -7; k <= 7; k++)
            {
                var at = Diagonal(40 * k, first);
                int v = V(k), burst = Burst(v);
                double b = s3 + Ticks(t + 30);                                   // 불길 13−2v 가 끝나는 때
                AddTimedFx(CrackObs, 13 - 2 * v, s3, at, t + 30, first);
                AddTimedFx(CrackObs, 19 - 2 * v, b, at, null, first);
                double c = b + OnceSeconds(CrackObs, 19 - 2 * v) + Ticks(30);
                AddTimedFx(CrackObs, burst, c, at, null, first);
                if (_rng.Next(2) != 0) AddTimedFx(CrackObs, v, c, at, null, first);
            }
        _shakes.Add((s3 + Ticks(t + 68), s3 + Ticks(t + 68 + 35), 5, false));
        HeavenEarthSound(7, s3 + Ticks(t + 60));

        // 폭발하는 때마다 그 대상에게 피해를 준다.
        foreach (var (at, target) in blasts)
        {
            while (_lastTime < at) { StepDebris(); yield return true; }
            hit(target);
        }
        double finish = s3 + Ticks(t + 68 + 35);
        while (_lastTime < finish || _debris.Count > 0 && _lastTime < finish + 3) { StepDebris(); yield return true; }
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                               $"heaven-earth: targets {count}, fx {_timedFx.Count}, debris {_debris.Count}, took {_lastTime - s1:0.0}s" + Environment.NewLine);
    }

    /// <summary>파편 물리 한 틱씩(<c>0x10038fa0</c>) — 수명 200틱.</summary>
    private void StepDebris()
    {
        int ticks = (int)((_lastTime - _debrisLastStep) * TicksPerSecond);
        if (ticks <= 0) return;
        _debrisLastStep += ticks / TicksPerSecond;
        for (int n = 0; n < ticks; n++)
            foreach (var d in _debris)
            {
                if (_debrisLastStep < d.Start) continue;
                d.Age++;
                d.X += d.Vx * 0.5;
                d.Y += d.Vy * 0.5 * 0.8;
                double old = d.Vz;
                d.Vz -= 5;
                d.Z += (d.Vz + old) * 0.5 * 0.5;
                if (d.Z <= 0) { d.Vz = -d.Vz * d.Bounce; d.Z = -d.Z; }
            }
        _debris.RemoveAll(d => d.Age >= 200 || d.Age > 0 && d.Z <= 0.5 && Math.Abs(d.Vz) < 5);
    }

    /// <summary>시각표 효과·파편을 그린다 — DrawEffects 가 부른다.</summary>
    private void DrawHeavenEarthFx()
    {
        StepDebris();
        _timedFx.RemoveAll(f => f.End is { } end ? _lastTime >= end : _lastTime >= f.Start + OnceSeconds(f.Obs, f.Motion));
        foreach (var f in _timedFx)
        {
            if (_lastTime < f.Start) continue;
            int tick = (int)((_lastTime - f.Start) * TicksPerSecond);
            var clip = UiFor(f.Obs)?.Clip(f.Motion);
            if (clip is { Children.Count: > 0 }) DrawUnitLayers(clip, tick, f.X, f.Y, f.Mirror, loop: f.End != null);
            DrawUi(f.Obs, f.Motion, tick, f.X, f.Y, UiBlend.Add, loop: f.End != null, mirror: f.Mirror);
        }
        foreach (var d in _debris)
            if (_lastTime >= d.Start)
                DrawUi(d.Obs, d.Motion, d.Age, (int)d.X, (int)(d.Y - d.Z * 0.6), UiBlend.Alpha);
    }

    /// <summary>지금 흔들림 — 틀마다 ±세기로 번갈아(가로·세로 따로).</summary>
    private (int X, int Y) ShakeOffset()
    {
        _shakes.RemoveAll(s => _lastTime >= s.End);
        int x = 0, y = 0, sign = (int)(_lastTime * TicksPerSecond) % 2 == 0 ? 1 : -1;
        foreach (var s in _shakes)
        {
            if (_lastTime < s.Start) continue;
            if (s.Vertical) y = Math.Max(y, s.Strength); else x = Math.Max(x, s.Strength);
        }
        return (x * sign, y * sign);
    }
}
