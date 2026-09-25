using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 아크로스트의 마법 둘 — 엘레맨탈 파이어(어빌리티 29)·서몬 몬스터(어빌리티 60). 둘 다 자기 중심 범위기로, 원본 핸들러가 <b>대상마다</b> 연출을 따로 깐다.
/// 뽑은 표(AbilityScripts.g.cs)는 그 효과들을 대상 한 자리에 한 번씩만 띄워 불덩이·몬스터가 제대로 안 보였다(사용자 보고).
/// </summary>
/// <remarks>
/// <para><b>엘레맨탈 파이어</b> — 핸들러 <c>0x1009ea80</c>, 대상 j/n 마다:
/// 불덩이 637:0 이 시전자 머리 위(z+120)에서 반지름 80 으로 돌고(<c>0x100c5460</c>·<c>0x100c5550</c>, 고른 간격 j/n 에서 출발, 150+10j 틱),
/// 5틱마다 꼬리 321:0 을 남긴다(<c>0x100c55a0(321, 5, 4)</c>). 다 돌면 그 불덩이가 대상에게 날아가(빠르기 5, ×1.5, 위 80) 터진다 —
/// 252:0 → 252:1, 피해는 터질 때(<c>0x100c2950</c>). 소리 1331:0(돌기 시작) · 1331:1(날기 시작) · 1331:0(터짐).
/// 원 궤도의 빠르기는 곡선 이동기(<c>0x100384c0</c>) 안쪽이라 못 풀었다 — 인자의 10.0 을 틱당 10° 로 읽었다(가설).</para>
/// <para><b>서몬 몬스터</b> — 핸들러 <c>0x100af1e0</c>, 대상 j 마다 난수 넷 중 하나로 몬스터가 대상의 한쪽에 나타나 친다(j+10 틱 뒤):
/// 0 위(y−80) 919:2 · 2 아래(y+80) 919:0 · 1 왼쪽(x−80, 뒤집음) 919:1 · 3 오른쪽(x+80) 919:1. 몬스터 모션이 끝나면 피해(<c>0x100c29b0</c>)와 함께
/// 시전자에게 919:5/3/4(자식 920:2/0/1). 소리 1356:0 은 j 틱 뒤. 몬스터의 잔상(<c>0x100c64b0</c> + <c>0x100c6600</c>)은 아직 안 그린다.</para>
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int ElementalFireAbility = 29, SummonMonsterAbility = 60;
    private const int FireBallObs = 637, FireTrailObs = 321, FireBlastObs = 252, FireSoundObs = 1331;
    private const int MonsterObs = 919, MonsterSoundObs = 1356;

    /// <summary>엘레맨탈 파이어 레벨 1~20 · 서몬 몬스터 레벨 1~20 의 work — 뽑은 표 대신 손으로 그린다.</summary>
    private static readonly HashSet<int> AcrostWorks =
    [
        389, 1206, 1205, 1204, 1203, 1202, 1201, 1200, 1199, 1198, 1197, 1196, 1195, 1194, 1193, 1192, 1191, 1190, 1189, 1188,
        420, 1291, 1290, 1289, 1288, 1287, 1286, 1285, 1284, 1283, 1282, 1281, 1280, 1279, 1278, 1277, 1276, 1275, 1274, 1273,
    ];

    /// <summary>도는 불덩이 하나.</summary>
    private sealed class FireBall
    {
        public double Start, Angle, X, Y, Speed = 5;
        public int OrbitTicks, Tick, Target;
        public bool Diving, Done;
    }

    private readonly List<FireBall> _fireBalls = [];
    private double _fireLastStep;

    /// <summary>효과 모션에 박힌 소리를 그때 낸다 — 효과 폴더 밖(moses/obs)의 소리 껍데기도 찾는다.</summary>
    private void FxSound(int obs, int motion, double at)
    {
        if (UiFor(obs)?.Clip(motion) is not { } clip) return;
        foreach (var (tick, sound) in clip.Sounds) _pendingSounds.Add((at + tick / TicksPerSecond, sound));
    }

    private IEnumerable<bool> ElementalFireRoutine(UnitState user, List<int> targets, Action<int> hit)
    {
        int n = targets.Count;
        if (n == 0) yield break;
        _fireLastStep = _lastTime;
        var balls = new List<FireBall>();
        for (int j = 0; j < n; j++)
        {
            var ball = new FireBall { Start = _lastTime, Angle = 2 * Math.PI * j / n, OrbitTicks = 150 + 10 * j, Target = targets[j] };
            balls.Add(ball);
            FxSound(FireSoundObs, 0, _lastTime + 150.0 * j / n / TicksPerSecond);
        }
        _fireBalls.AddRange(balls);
        var (ux, uy) = UnitFoot(user);
        var struck = new HashSet<FireBall>();
        while (balls.Any(b => !b.Done))
        {
            StepFireBalls(ux, uy);
            foreach (var b in balls.Where(b => b.Done && struck.Add(b))) hit(b.Target);
            yield return true;
        }
        // 마지막 폭발(252:1)이 끝날 때까지
        for (double end = _lastTime + 1.0; _lastTime < end;) yield return true;
    }

    /// <summary>불덩이를 틱 단위로 민다 — 돌기(틱당 10°) → 대상으로 날기 → 터짐.</summary>
    private void StepFireBalls(int cx, int cy)
    {
        int ticks = (int)((_lastTime - _fireLastStep) * TicksPerSecond);
        if (ticks <= 0) return;
        _fireLastStep += ticks / TicksPerSecond;
        for (int k = 0; k < ticks; k++)
            foreach (var b in _fireBalls.Where(b => !b.Done))
            {
                b.Tick++;
                if (!b.Diving)
                {
                    b.Angle += Math.PI / 18;                                     // 10°
                    b.X = cx + Math.Cos(b.Angle) * 80;
                    b.Y = cy + Math.Sin(b.Angle) * 80 * 0.8 - 120 * 0.6;         // 반지름 80(월드), 머리 위 120
                    if (b.Tick % 5 == 0) _timedFx.Add(new TimedFx(FireTrailObs, 0, _fireLastStep, (int)b.X, (int)b.Y, null, false));
                    if (b.Tick >= b.OrbitTicks) { b.Diving = true; FxSound(FireSoundObs, 1, _fireLastStep); }
                    continue;
                }
                var (tx, ty) = UnitFoot(_units[b.Target]);
                double dx = tx - b.X, dy = ty - b.Y, dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= b.Speed)
                {
                    b.Done = true;
                    _timedFx.Add(new TimedFx(FireBlastObs, 0, _fireLastStep, tx, ty, null, false));
                    _timedFx.Add(new TimedFx(FireBlastObs, 1, _fireLastStep + OnceSeconds(FireBlastObs, 0), tx, ty + 36, null, false));
                    FxSound(FireSoundObs, 0, _fireLastStep);
                    continue;
                }
                b.X += dx / dist * b.Speed;
                b.Y += dy / dist * b.Speed;
                b.Speed = Math.Min(80, b.Speed * 1.5);
            }
        _fireBalls.RemoveAll(b => b.Done);
    }

    private void DrawFireBalls()
    {
        foreach (var b in _fireBalls)
            DrawUi(FireBallObs, 0, b.Tick, (int)b.X, (int)b.Y, UiBlend.Add);
    }

    private IEnumerable<bool> SummonMonsterRoutine(UnitState user, List<int> targets, Action<int> hit)
    {
        double t0 = _lastTime;
        var (ux, uy) = UnitFoot(user);
        var hits = new List<(double At, int Target)>();
        double finish = t0;
        for (int j = 0; j < targets.Count; j++)
        {
            var (tx, ty) = UnitFoot(_units[targets[j]]);
            // 원본 갈래(rand%4) — (몬스터 모션, 시전자 쪽 모션, 치우침, 뒤집기)
            var (motion, casterMotion, ox, oy, mirror) = _rng.Next(4) switch
            {
                0 => (2, 5, 0, -80 * 0.8, false),
                2 => (0, 3, 0, 80 * 0.8, false),
                1 => (1, 4, -80, 0.0, true),
                _ => (1, 4, 80, 0.0, false),
            };
            double start = t0 + (j + 10) / TicksPerSecond, end = start + OnceSeconds(MonsterObs, motion);
            _timedFx.Add(new TimedFx(MonsterObs, motion, start, (int)(tx + ox), (int)(ty + oy), null, mirror));
            _timedFx.Add(new TimedFx(MonsterObs, casterMotion, end, ux, uy, null, false));
            FxSound(MonsterSoundObs, 0, t0 + j / TicksPerSecond);
            hits.Add((end, targets[j]));
            finish = Math.Max(finish, end + OnceSeconds(MonsterObs, casterMotion));
        }
        foreach (var (at, target) in hits.OrderBy(h => h.At))
        {
            while (_lastTime < at) yield return true;
            hit(target);
        }
        while (_lastTime < finish) yield return true;
    }
}
