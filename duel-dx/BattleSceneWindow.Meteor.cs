using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 메테오(어빌리티 95) — 핸들러 <c>0x100866f0</c> 의 단계 0 을 옮겼다. 전에는 착탄 이펙트만 겨눈 칸에 한꺼번에 떠
/// 운석이 떨어지는 모습이 안 보였다(사용자 보고).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>효과 범위(층 0) 칸 중 하나를 무작위로 고른다(<c>0x100defb0</c>). 운석은 <b>한 발</b>이다.</item>
/// <item>운석 200:0 이 그 칸 자리에서 <b>오른쪽 +200 · 위 −500</b> 에서 출발해 빠르기 60(최대 100)으로 곧게 떨어진다
/// (<c>0x100c3340</c>·<c>0x100c3490</c>·<c>0x100c25e0</c>). 출발은 5~14틱 늦게(<c>rand()%10+5</c>, <c>0x100c24d0</c>) — 소리 311:0 도 같이.</item>
/// <item>닿으면(<c>0x100c2640</c> 로 운석 뒤에 줄 세운 것들) 111:0 · 199:0 · 170:0(50틱) · 170:1 · 소리 311:1,
/// 파편 251:6 열다섯(<c>0x100cc680</c>)과 불꽃 109:7 열다섯이 둘레에 흩어진다. 단계 1 은 화면을 ±3 흔든다(<c>0x100eabb0</c>).</item>
/// </list>
/// 피해는 원본처럼 범위 안 대상마다 한 번 — 여기서는 운석이 닿을 때 넣는다(원본은 단계 2 끝 <c>0x10087fb8</c>).
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int MeteorAbility = 95;
    private const int MeteorObs = 200, MeteorSoundObs = 311;

    /// <summary>메테오 레벨마다의 work — Lv1 469 · Lv2~10 744→736 · Lv11~20 783→774. 핸들러는 모두 같다.</summary>
    private static readonly HashSet<int> MeteorWorks =
        [469, .. Enumerable.Range(736, 9), .. Enumerable.Range(774, 10)];

    private IEnumerable<bool> MeteorRoutine(WorkData w, UnitState user, int col, int row, List<int> targets, Action<int> hit)
    {
        double t0 = _lastTime;
        var cells = AreaCells(w, user, col, row);
        var (cc, cr) = cells.Count > 0 ? cells[_rng.Next(cells.Count)] : (col, row);
        int tx = cc * TileW + TileW / 2, ty = CellCenterY(cc, cr);

        double start = t0 + Ticks(_rng.Next(10) + 5);
        bool ownSounds = !_abilitySounds.ContainsKey(MeteorAbility);   // 손으로 적은 소리표가 있으면 그쪽이 낸다
        if (ownSounds) FxSound(MeteorSoundObs, 0, start);

        bool landed = false;
        double landedAt = 0;
        _arrows.Add(new Arrow
        {
            Obs = MeteorObs, Motion = 0, Start = start, X = tx + 200, Y = ty - 500, ToX = tx, ToY = ty,
            Speed = 60, Factor = 1, Max = 100,
            OnArrive = () =>
            {
                landed = true;
                landedAt = _arrowLastStep;
                if (ownSounds) FxSound(MeteorSoundObs, 1, landedAt);
                _timedFx.Add(new TimedFx(111, 0, landedAt, tx, ty, null, false));
                _timedFx.Add(new TimedFx(199, 0, landedAt, tx, ty, null, false));
                _timedFx.Add(new TimedFx(170, 0, landedAt, tx, ty, landedAt + Ticks(50), false));
                _timedFx.Add(new TimedFx(170, 1, landedAt, tx, ty, null, false));
                for (int k = 0; k < 15; k++)
                {
                    _timedFx.Add(new TimedFx(251, 6, landedAt, tx + _rng.Next(121) - 60, ty + _rng.Next(81) - 40, null, false));
                    _timedFx.Add(new TimedFx(109, 7, landedAt + Ticks(_rng.Next(10)), tx + _rng.Next(81) - 40, ty + _rng.Next(61) - 30, null, false));
                }
                _shakes.Add((landedAt, landedAt + Ticks(20), 3, false));
                foreach (int t in targets) hit(t);
            },
        });

        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"meteor work {w.Id}: cell ({cc},{cr}) at ({tx},{ty}) start +{(start - t0) * TicksPerSecond:F0}틱, 대상 {targets.Count}" + Environment.NewLine);
        _arrowLastStep = _lastTime;
        while (!landed)
        {
            StepArrows();
            yield return true;
        }
        double end = landedAt + Math.Max(Ticks(50), Math.Max(OnceSeconds(170, 1), OnceSeconds(109, 7) + Ticks(10)));
        while (_lastTime < end) yield return true;
    }
}
