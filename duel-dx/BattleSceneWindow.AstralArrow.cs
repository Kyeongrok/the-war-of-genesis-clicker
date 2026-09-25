namespace DuelDx;

/// <summary>
/// 아스트럴 애로우(어빌리티 87) — 핸들러 <c>0x100a5ac0</c> 가 세 단계로 도는 연출을 옮겼다. 뽑은 표(AbilityScripts.g.cs)는 활·화살·폭발을
/// 시각 없이 한꺼번에 한 자리에 띄워, 무엇이 지나갔는지 안 보였다(사용자 보고).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>활</b> — 소리 1375:0, 시전자 머리 위(높이 +150)에 활 183:0 한 번 → 183:1 70틱 → 183:2 한 번(<c>0x100c2640</c> 로 줄줄이).</item>
/// <item><b>쏘아 올리기</b>(상태 1) — 10틱 뒤 소리 1375:0, 화살 839:1 <b>50발</b>을 시전자(높이 +170)에서 500 위로(가로 ±10),
/// 빠르기 20·틱마다 ×1.2·최대 100(<c>0x100c3490</c>·<c>0x100c25e0</c>), 발마다 0~59틱 늦게.</item>
/// <item><b>쏟아지기</b>(상태 2, 80틱 뒤) — 대상마다 화살 839:44 가 화면 위 100 밖(<c>0x100dfdc0</c>)에서 대상 머리(+60)로
/// 빠르기 40·×1.1·최대 100, 0~99틱 늦게. 닿으면 피해(<c>0x100c29b0</c>)와 폭발 705:0, 소리 1375:2(출발 때).
/// 그다음 장식 화살 50발이 시전자 둘레(가로 ±300, 세로 −200~+240)에 똑같이 떨어져 터진다(피해 없음).</item>
/// </list>
/// 월드 → 화면은 다른 연출과 같다: 세로 ×0.8, 높이 ×0.6 위로.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int AstralArrowAbility = 87;
    private const int BowObs = 183, ArrowObs = 839, ArrowBlastObs = 705, ArrowSoundObs = 1375;

    /// <summary>아스트럴 애로우 레벨 1~10 의 work — 뽑은 표 대신 손으로 그린다.</summary>
    private static readonly HashSet<int> AstralArrowWorks = [448, 1147, 1146, 1145, 1144, 1143, 1142, 1141, 1140, 1139];

    /// <summary>날아가는 화살 하나 — 곧게 목표로, 틱마다 빨라진다. 닿으면 <see cref="OnArrive"/>.</summary>
    private sealed class Arrow
    {
        public int Motion;
        public double Start, X, Y, ToX, ToY, Speed, Factor, Max;
        public int Tick;
        public bool Done;
        public Action? OnArrive;
    }

    private readonly List<Arrow> _arrows = [];
    private double _arrowLastStep;

    private IEnumerable<bool> AstralArrowRoutine(UnitState user, List<int> targets, Action<int> hit)
    {
        double t0 = _lastTime;
        var (ux, uy) = UnitFoot(user);
        int bowY = (int)(uy - 150 * 0.6);

        // 1. 활
        FxSound(ArrowSoundObs, 0, t0);
        double t1 = t0 + OnceSeconds(BowObs, 0), t2 = t1 + Ticks(70);
        _timedFx.Add(new TimedFx(BowObs, 0, t0, ux, bowY, null, false));
        _timedFx.Add(new TimedFx(BowObs, 1, t1, ux, bowY, t2, false));
        _timedFx.Add(new TimedFx(BowObs, 2, t2, ux, bowY, null, false));
        double t3 = t2 + OnceSeconds(BowObs, 2);

        // 2. 쏘아 올리기 — 50발
        FxSound(ArrowSoundObs, 0, t3 + Ticks(10));
        for (int k = 0; k < 50; k++)
            _arrows.Add(new Arrow
            {
                Motion = 1, Start = t3 + Ticks(_rng.Next(60)), X = ux, Y = uy - 170 * 0.6,
                ToX = ux + _rng.Next(20) - 10, ToY = uy - 500 * 0.6, Speed = 20, Factor = 1.2, Max = 100,
            });

        // 3. 쏟아지기 — 80틱 뒤. 대상마다 한 발(닿으면 피해), 그다음 장식 50발.
        double t4 = t3 + Ticks(80);
        var pending = new HashSet<int>(targets);
        void Fall(double tx, double ty, int? target)
        {
            double start = t4 + Ticks(_rng.Next(100));
            FxSound(ArrowSoundObs, 2, start);
            _arrows.Add(new Arrow
            {
                Motion = 44, Start = start, X = tx, Y = _camY - 100, ToX = tx, ToY = ty - 60 * 0.6, Speed = 40, Factor = 1.1, Max = 100,
                OnArrive = () =>
                {
                    _timedFx.Add(new TimedFx(ArrowBlastObs, 0, _arrowLastStep, (int)tx, (int)ty, null, false));
                    if (target is { } t) { hit(t); pending.Remove(t); }
                },
            });
        }
        foreach (int t in targets) { var (tx, ty) = UnitFoot(_units[t]); Fall(tx, ty, t); }
        for (int k = 0; k < 50; k++) Fall(ux + _rng.Next(600) - 300, uy + (_rng.Next(440) - 200) * 0.8, null);

        _arrowLastStep = _lastTime;
        while (_arrows.Count > 0 || pending.Count > 0)
        {
            StepArrows();
            yield return true;
        }
        for (double end = _lastTime + OnceSeconds(ArrowBlastObs, 0); _lastTime < end;) yield return true;
    }

    /// <summary>화살을 틱 단위로 민다 — 출발 전이면 기다리고, 닿으면 없앤다.</summary>
    private void StepArrows()
    {
        int ticks = (int)((_lastTime - _arrowLastStep) * TicksPerSecond);
        if (ticks <= 0) return;
        for (int k = 0; k < ticks; k++)
        {
            _arrowLastStep += 1 / TicksPerSecond;
            foreach (var a in _arrows.Where(a => !a.Done && _arrowLastStep >= a.Start))
            {
                a.Tick++;
                double dx = a.ToX - a.X, dy = a.ToY - a.Y, dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= a.Speed) { (a.X, a.Y, a.Done) = (a.ToX, a.ToY, true); a.OnArrive?.Invoke(); continue; }
                a.X += dx / dist * a.Speed;
                a.Y += dy / dist * a.Speed;
                a.Speed = Math.Min(a.Max, a.Speed * a.Factor);
            }
        }
        _arrows.RemoveAll(a => a.Done);
    }

    private void DrawArrows()
    {
        foreach (var a in _arrows.Where(a => _lastTime >= a.Start && !a.Done))
            DrawUi(ArrowObs, a.Motion, a.Tick, (int)a.X, (int)a.Y, BlendOf(UiFor(ArrowObs)?.BlendAt(a.Motion, a.Tick) ?? 0));
    }
}
