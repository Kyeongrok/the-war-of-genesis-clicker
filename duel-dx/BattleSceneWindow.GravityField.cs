namespace DuelDx;

/// <summary>
/// 그라비티 필드(어빌리티 110) — 자기 중심 범위기로, 원본 핸들러가 <b>대상마다</b> 중력장을 따로 깐다.
/// 뽑은 표(AbilityScripts.g.cs)는 219:0 을 겨눈 자리 한 곳에 한 번만 띄워 효과가 안 보였다(사용자 보고).
/// </summary>
/// <remarks>
/// 핸들러 <c>0x100a2810</c>: 소리 1414:0(시전자). 범위 안 대상 j(<c>0x100df5c0</c>, 최대 100)마다
/// 219:0 을 <b>그 대상에 붙여</b> 모션 한 번(<c>0x100c2530(0)</c>), 시작은 50+15j 틱 뒤(<c>0x100c24d0</c>),
/// 끝날 때 work 효과(<c>0x100c29b0</c>) — 같은 때 소리 1414:1. 그다음 가로 흔들림 세기 2(<c>0x100c6f70</c> · <c>0x100cda20(2)</c>), 15n+80 틱.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int GravityFieldAbility = 110;
    private const int GravityObs = 219, GravitySoundObs = 1414;

    private IEnumerable<bool> GravityFieldRoutine(UnitState user, List<int> targets, Action<int> hit)
    {
        double t0 = _lastTime;
        FxSound(GravitySoundObs, 0, t0);
        int n = targets.Count;
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"gravity field: caster {user.ChrCode}({user.Col},{user.Row}) targets " +
                string.Join(", ", targets.Select(t => $"{_units[t].ChrCode}({_units[t].Col},{_units[t].Row}){(_units[t].IsAlly ? " 아군" : "")}")) + Environment.NewLine);
        _shakes.Add((t0, t0 + Ticks(15 * n + 80), 2, false));
        var hits = new List<(double At, int Target)>();
        double finish = t0 + Ticks(15 * n + 80);
        for (int j = 0; j < n; j++)
        {
            double start = t0 + Ticks(50 + 15 * j), end = start + OnceSeconds(GravityObs, 0);
            var (tx, ty) = UnitFoot(_units[targets[j]]);
            _timedFx.Add(new TimedFx(GravityObs, 0, start, tx, ty, null, false));
            FxSound(GravitySoundObs, 1, start);
            hits.Add((end, targets[j]));
            finish = Math.Max(finish, end);
        }
        foreach (var (at, target) in hits)
        {
            while (_lastTime < at) yield return true;
            hit(target);
        }
        while (_lastTime < finish) yield return true;
    }
}
