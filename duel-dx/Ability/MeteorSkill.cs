using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

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
internal sealed unsafe class MeteorSkill(GameWindow host)
{
    internal const int MeteorAbility = 95;
    internal const int MeteorObs = 200, MeteorSoundObs = 311;

    /// <summary>메테오 레벨마다의 work — Lv1 469 · Lv2~10 744→736 · Lv11~20 783→774. 핸들러는 모두 같다.</summary>
    internal static readonly HashSet<int> MeteorWorks =
        [469, .. Enumerable.Range(736, 9), .. Enumerable.Range(774, 10)];

    internal IEnumerable<bool> MeteorRoutine(WorkData w, UnitState user, int col, int row, List<int> targets, Action<int> hit)
    {
        // 운석은 세 발 — 앞의 것이 닿으면 다음 것이 떨어진다(단계 0·1·2 가 같은 묶음, 0x10086746 · 0x10086f27 · 0x1008770e). 칸은 발마다 새로 고르고,
        // 출발은 5~14 · 5~14 · 30~44틱 늦게. 판정은 셋째가 닿은 뒤 한 번(0x10087fc0). 전에는 한 발이고 그 한 발에 판정했다.
        var cells = host.Btl.AreaCells(w, user, col, row);
        bool ownSounds = !host._abilitySounds.ContainsKey(MeteorAbility);   // 손으로 적은 소리표가 있으면 그쪽이 낸다
        double landedAt = 0;
        for (int shot = 0; shot < 3; shot++)
        {
            var (cc, cr) = cells.Count > 0 ? cells[host._rng.Next(cells.Count)] : (col, row);
            int tx = cc * TileW + TileW / 2, ty = host.CellCenterY(cc, cr);
            double start = host._lastTime + host.HeavenEarthAb.Ticks(shot < 2 ? host._rng.Next(10) + 5 : host._rng.Next(15) + 30);
            if (ownSounds) host.AcrostAb.FxSound(MeteorSoundObs, 0, start);
            bool landed = false;
            host.AstralArrowAb._arrows.Add(new AstralArrowSkill.Arrow
            {
                Obs = MeteorObs, Motion = 0, Start = start, X = tx + 200, Y = ty - 400, ToX = tx, ToY = ty,      // 위 −500 은 월드 y — 화면으로는 400
                Speed = 60, Factor = 1, Max = 100,
                OnArrive = () =>
                {
                    landed = true;
                    landedAt = host.AstralArrowAb._arrowLastStep;
                    if (ownSounds) host.AcrostAb.FxSound(MeteorSoundObs, 1, landedAt);
                    host.HeavenEarthAb._timedFx.Add(new HeavenEarthSkill.TimedFx(111, 0, landedAt, tx, ty, null, false));
                    host.HeavenEarthAb._timedFx.Add(new HeavenEarthSkill.TimedFx(199, 0, landedAt, tx, ty, null, false));
                    host.HeavenEarthAb._timedFx.Add(new HeavenEarthSkill.TimedFx(170, 0, landedAt, tx, ty, landedAt + host.HeavenEarthAb.Ticks(50), false));
                    host.HeavenEarthAb._timedFx.Add(new HeavenEarthSkill.TimedFx(170, 1, landedAt + host.HeavenEarthAb.Ticks(50), tx, ty, null, false));
                    for (int k = 0; k < 15; k++)
                    {
                        host.HeavenEarthAb._timedFx.Add(new HeavenEarthSkill.TimedFx(251, 6, landedAt, tx + host._rng.Next(121) - 60, ty + host._rng.Next(81) - 40, null, false));
                        // 불꽃 109:7 은 착탄점에서 운석이 온 쪽으로 40 씩 한 줄(0x10086bd7).
                        host.HeavenEarthAb._timedFx.Add(new HeavenEarthSkill.TimedFx(109, 7, landedAt, tx + (int)(40 * (k + 1) * 0.447), ty - (int)(40 * (k + 1) * 0.894), null, false));
                    }
                    host.HeavenEarthAb._shakes.Add((landedAt, landedAt + host.HeavenEarthAb.Ticks(20), 3, false));
                },
            });
            if (BattleScene.Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"meteor work {w.Id} shot {shot}: cell ({cc},{cr}) at ({tx},{ty}), 대상 {targets.Count}" + Environment.NewLine);
            host.AstralArrowAb._arrowLastStep = host._lastTime;
            while (!landed)
            {
                host.AstralArrowAb.StepArrows();
                yield return true;
            }
        }
        foreach (int t in targets) hit(t);
        double end = landedAt + Math.Max(host.HeavenEarthAb.Ticks(50), Math.Max(host.HeavenEarthAb.OnceSeconds(170, 1), host.HeavenEarthAb.OnceSeconds(109, 7) + host.HeavenEarthAb.Ticks(10)));
        while (host._lastTime < end) yield return true;
    }
}
