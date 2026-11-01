using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 대상마다 따로 치는 필살기 셋 — 다크 스크림 · 페이온 스피리츠 · 셰틀라이트 어텍(버그 리포트 #13 · #15 · #17, fg-109).
/// 뽑은 표(AbilityScripts.g.cs)는 대상 고리를 못 읽어 효과를 겨눈 칸 한 곳에 한 번만 띄웠고, 다크 스크림의 분신은 시전자와 겨눈 칸 사이에 늘어서서
/// 시전자가 혼자 몸짓만 하고 피해가 뜨는 것으로 보였다.
/// </summary>
/// <remarks>
/// <para>다크 스크림(work 1492, <c>0x1009c520</c>): 모션 48 → 49 붙듦. 대상 j(n 명)마다 시전자 몸 복제 둘 — 첫째는 대상의 시전자 쪽 80, 시작 6j 틱,
/// 둘째는 반대쪽 80, 시작 6n + 30 + 6j 틱. 모션은 위 51 · 옆 52(오른쪽은 뒤집음) · 아래 53, 한 번. 마지막 복제가 끝나고 15틱 뒤 모션 50.</para>
/// <para>페이온 스피리츠(work 1529, <c>0x100b0f00</c>): 모션 48. 대상 j 마다 1017:49 를 대상 위쪽 80 에, 시작 20j 틱. 마지막이 끝나면 모션 50.</para>
/// <para>셰틀라이트 어텍(work 1522, <c>0x100a7070</c>): 모션 90. 대상 j 마다 307:0(대상) · 1322:0(시전자), 시작 15j 틱. 끝나면 영상 Mov 0038.
/// 그 뒤 대상 j 마다 시작 30 + 8j 틱에 987:3 22조각을 대상에서 위로 37 간격, 1316:0(대상) · 1322:1(시전자). 987 첫 조각이 끝날 때 피해.</para>
/// 자리는 월드 단위, 화면 y 는 × 0.8. 다크 스크림은 복제마다 피해가 들어(<c>0x100c2860</c>) 범위 안 적을 두 번씩 친다(사용자 확인) — 명중도 타마다 따로 판정한다.
/// </remarks>
internal sealed unsafe class FinisherStrikeSkill(GameWindow host)
{
    internal const int DarkScreamWork = 1492, PeionSpiritsWork = 1529, SatelliteWork = 1522;
    internal const int PeionObs = 1017, SatelliteObs = 307, SatelliteBeamObs = 987, SatelliteBlastObs = 1316, SatelliteCasterObs = 1322;
    private const int BesideWorld = 80, BeamPieces = 22, BeamStepWorld = 37;
    private const double ScreenY = 0.8;

    internal static bool Handles(int work) => work is DarkScreamWork or PeionSpiritsWork or SatelliteWork;

    /// <summary>앞머리 뒤의 몸짓은 전용 루틴이 다 튼다 — 사슬에는 앞머리 몫(6 · 15)만 남긴다.</summary>
    internal static readonly int[] Actions = [6, 15];

    private readonly record struct Clone(UnitState Owner, int Motion, double Start, int X, int Y, bool Mirror);

    private readonly List<Clone> _clones = [];

    internal IEnumerable<bool> Routine(WorkData w, UnitState user, List<int> targets, Action<int> hit)
    {
        if (BattleScene.Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"finisher strike {w.Id}: caster {user.ChrCode} targets {string.Join(", ", targets.Select(t => host._units[t].ChrCode))}" + Environment.NewLine);
        return w.Id switch
        {
            DarkScreamWork => DarkScream(user, targets, hit),
            PeionSpiritsWork => PeionSpirits(user, targets, hit),
            _ => Satellite(w, user, targets, hit),
        };
    }

    private IEnumerable<bool> Wait(double until)
    {
        while (host._lastTime < until) yield return true;
    }

    /// <summary>모션을 끝까지 틀고 마지막 컷을 <paramref name="holdSeconds"/> 동안 붙든다 — 끝에 이어질 효과가 도는 동안 원본도 그 컷에 머문다.</summary>
    private double PlayHeld(UnitState user, int motion, double holdSeconds)
    {
        double seconds = host.HeavenEarthAb.PlayRawMotion(user, motion, loop: false);
        if (seconds > 0) user.PlayMotion(motion, seconds + holdSeconds, loop: false);
        return seconds;
    }

    private IEnumerable<bool> DarkScream(UnitState user, List<int> targets, Action<int> hit)
    {
        var he = host.HeavenEarthAb;
        foreach (bool _ in Wait(host._lastTime + he.PlayRawMotion(user, 48, loop: false))) yield return true;
        he.PlayRawMotion(user, 49, loop: true, holdSeconds: 30);
        host._sprites.TryGetValue(user.ChrCode, out var sprite);
        double t0 = host._lastTime, finish = t0;
        int n = targets.Count;
        var hits = new List<(double At, int Target)>();
        for (int j = 0; j < n; j++)
        {
            var target = host._units[targets[j]];
            var (tx, ty) = host.Btl.UnitFoot(target);
            var way = BattleScene.FacingToward(user.Col, user.Row, target.Col, target.Row);
            for (int pass = 0; pass < 2; pass++)
            {
                // 첫 복제는 대상의 시전자 쪽에 서서 대상을 보고, 둘째는 반대쪽에서 돌아서서 친다.
                var facing = pass == 0 ? way : way switch { Facing.Up => Facing.Down, Facing.Down => Facing.Up, Facing.Left => Facing.Right, _ => Facing.Left };
                var (dx, dy, motion) = facing switch
                {
                    Facing.Up => (0, BesideWorld, 51),
                    Facing.Down => (0, -BesideWorld, 53),
                    Facing.Left => (BesideWorld, 0, 52),
                    _ => (-BesideWorld, 0, 52),
                };
                double start = t0 + he.Ticks(pass == 0 ? 6 * j : 6 * n + 30 + 6 * j);
                _clones.Add(new Clone(user, motion, start, tx + dx, ty + (int)(dy * ScreenY), facing == Facing.Right));
                int length = Math.Max(1, sprite?.MotionTicks(motion) ?? 0);
                foreach (var (tick, sound) in sprite?.RawClip(motion)?.Sounds ?? []) host._pendingSounds.Add((start + he.Ticks(tick), sound));
                finish = Math.Max(finish, start + he.Ticks(length));
                var keys = sprite?.RawClip(motion)?.Hits;
                hits.Add((start + he.Ticks(keys is { Count: > 0 } ? keys.Min(h => h.Start) : length / 2), targets[j]));
            }
        }
        foreach (var (at, target) in hits.OrderBy(h => h.At))
        {
            foreach (bool _ in Wait(at)) yield return true;
            hit(target);
        }
        foreach (bool _ in Wait(finish + he.Ticks(15))) yield return true;
        he.PlayRawMotion(user, 50, loop: false);
    }

    private IEnumerable<bool> PeionSpirits(UnitState user, List<int> targets, Action<int> hit)
    {
        var he = host.HeavenEarthAb;
        int n = targets.Count;
        double length = he.OnceSeconds(PeionObs, 49), span = he.Ticks(20 * Math.Max(0, n - 1)) + length;
        foreach (bool _ in Wait(host._lastTime + PlayHeld(user, 48, span + 1))) yield return true;
        double t0 = host._lastTime, finish = t0;
        // 피해는 그 효과의 타격 키에서 — 키가 없으면 효과의 한가운데.
        var keys = host.UiFor(PeionObs)?.Clip(49)?.Hits;
        double strike = keys is { Count: > 0 } ? he.Ticks(keys.Min(h => h.Start)) : length / 2;
        var hits = new List<(double At, int Target)>();
        for (int j = 0; j < n; j++)
        {
            var (tx, ty) = host.Btl.UnitFoot(host._units[targets[j]]);
            double start = t0 + he.Ticks(20 * j);
            he.AddTimedFx(PeionObs, 49, start, (tx, ty - BesideWorld * ScreenY), null, false);
            finish = start + length;
            hits.Add((start + strike, targets[j]));
        }
        foreach (var (at, target) in hits)
        {
            foreach (bool _ in Wait(at)) yield return true;
            hit(target);
        }
        foreach (bool _ in Wait(finish)) yield return true;
        he.PlayRawMotion(user, 50, loop: false);
    }

    private IEnumerable<bool> Satellite(WorkData w, UnitState user, List<int> targets, Action<int> hit)
    {
        var he = host.HeavenEarthAb;
        int n = targets.Count;
        var (ux, uy) = host.Btl.UnitFoot(user);
        double beamLength = he.OnceSeconds(SatelliteBeamObs, 3);
        foreach (bool _ in Wait(host._lastTime + PlayHeld(user, 90, 30))) yield return true;

        double t0 = host._lastTime, finish = t0;
        for (int j = 0; j < n; j++)
        {
            var (tx, ty) = host.Btl.UnitFoot(host._units[targets[j]]);
            double start = t0 + he.Ticks(15 * j);
            he.AddTimedFx(SatelliteObs, 0, start, (tx, ty), null, false);
            he.AddTimedFx(SatelliteCasterObs, 0, start, (ux, uy), null, false);
            host.AcrostAb.FxSound(SatelliteCasterObs, 0, start);
            finish = Math.Max(finish, start + he.OnceSeconds(SatelliteObs, 0));
        }
        foreach (bool _ in Wait(finish)) yield return true;

        host.Cut.Start(w);
        while (host.Cut.Playing) yield return true;

        t0 = host._lastTime;
        finish = t0;
        var hits = new List<(double At, int Target)>();
        for (int j = 0; j < n; j++)
        {
            var (tx, ty) = host.Btl.UnitFoot(host._units[targets[j]]);
            double start = t0 + he.Ticks(30 + 8 * j);
            for (int piece = 0; piece < BeamPieces; piece++)
                he.AddTimedFx(SatelliteBeamObs, 3, start, (tx, ty - piece * BeamStepWorld * ScreenY), null, false);
            he.AddTimedFx(SatelliteBlastObs, 0, start, (tx, ty), null, false);
            he.AddTimedFx(SatelliteCasterObs, 1, start, (ux, uy), null, false);
            host.AcrostAb.FxSound(SatelliteCasterObs, 1, start);
            hits.Add((start + beamLength, targets[j]));
            finish = Math.Max(finish, start + Math.Max(beamLength, he.OnceSeconds(SatelliteBlastObs, 0)));
        }
        foreach (var (at, target) in hits)
        {
            foreach (bool _ in Wait(at)) yield return true;
            hit(target);
        }
        foreach (bool _ in Wait(finish)) yield return true;
        user.PlayAction(ObsMotionTable.ActionStand, 0);
    }

    /// <summary>복제를 그린다 — 원본 복제는 섞기를 안 걸어 본체처럼 불투명하다.</summary>
    internal void Draw()
    {
        for (int i = _clones.Count - 1; i >= 0; i--)
        {
            var c = _clones[i];
            if (host._lastTime < c.Start) continue;
            int tick = (int)((host._lastTime - c.Start) * TicksPerSecond);
            if (!host._sprites.TryGetValue(c.Owner.ChrCode, out var sprite) || tick >= Math.Max(1, sprite.MotionTicks(c.Motion))
                || sprite.FrameOfMotion(c.Motion, tick, c.Mirror) is not { } frame)
            {
                _clones.RemoveAt(i);
                continue;
            }
            host.BlitMasked(frame.Px, frame.W, frame.H, c.X + frame.X, c.Y + frame.Y);
        }
    }
}
