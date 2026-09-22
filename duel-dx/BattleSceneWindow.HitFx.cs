using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 맞는 효과(fg-10) — 맞은 쪽 몸짓·흰 번쩍임·떨림, 불꽃 이펙트, 숫자·Miss 글자, 치명타 화면 물들이기.
/// </summary>
/// <remarks>
/// 옵시디안 분석-전투 "맞는 효과": 반응표(<c>0x1007a344</c>)는 결과 2(맞음)일 때만 반응한다 —
/// 맞은 쪽이 <b>동작 2</b>(모션 6·7·8)를 15틱 하고, 피해가 있으면 <b>Obs 0073</b> 모션 0~2 중 하나를
/// 유닛 기준점 18픽셀 위에 가산 합성으로 띄운다. 흰 번쩍임(물들이기 키)과 1픽셀 떨림(자리 키)은
/// 코드가 아니라 <b>그 모션 자료</b>에 들어 있어, 모션표를 그대로 재생하면 따라온다.
/// 빗나감은 연두색 "Miss" 글자만, 회복은 노란 숫자만(반응·소리 없음), 치명타는 화면 전체를 한 틱 물들인다.
/// 맞는 타격음은 따로 없다 — 비명(Dmg.dat)과 때리는 쪽 모션 소리뿐이다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>맞음 동작 — 동작 2(모션 6·7·8), 15틱.</summary>
    private const int HitAction = 2, HitActionTicks = 15;

    /// <summary>불꽃 이펙트 Obs 와 유닛 기준점에서의 높이(픽셀).</summary>
    private const int HitEffectObs = 73, HitEffectLift = 18;

    /// <summary>쓰러질 때도 같은 동작 2 를 21틱 한다(명령 0x2717).</summary>
    private const int DeathActionTicks = 21;

    private readonly List<(int Obs, int Motion, double Start, int X, int Y)> _effects = [];

    /// <summary>치명타가 난 틱에 화면 전체를 한 번 물들인다(<c>0x1002e8f0(9,16,1,0)</c>).</summary>
    private double _critFlashAt = -1;

    /// <summary>맞은 쪽 반응 — 동작 2 와 불꽃 이펙트(피해가 있을 때만).</summary>
    private void PlayHitReaction(UnitState target, bool damaged)
    {
        PlayActionFor(target, HitAction, HitActionTicks);
        if (!damaged) return;
        var (x, y) = UnitFoot(target);
        _effects.Add((HitEffectObs, _rng.Next(3), _lastTime, x, y - HitEffectLift));
    }

    private void PlayCritFlash() => _critFlashAt = _lastTime;

    /// <summary>동작을 정해진 틱 수만큼 재생한다(모션 길이 대신).</summary>
    private void PlayActionFor(UnitState u, int action, int ticks)
    {
        u.PlayAction(action, ticks / TicksPerSecond);
        ScheduleActionSounds(u, action);
    }

    /// <summary>
    /// 인물 그림에 붙는 자식 층 — 제이슨처럼 몸과 무기가 나뉜 인물의 <b>무기 그림</b>(Obs 0426)과 검기가 여기로 붙는다
    /// (분석-모션 ba-8: 캐릭터 모션마다 키 종류 2 로 같은 번호의 모션을 같은 자리에 겹친다).
    /// </summary>
    /// <param name="mirror">
    /// 인물이 오른쪽을 볼 때 참 — 몸 그림은 옆모습을 뒤집어 그리므로(<see cref="UnitSprite.FrameFor"/>),
    /// 같이 붙는 무기·검기 층도 뒤집어야 한다. 안 그러면 왼쪽을 보고 치는 자료 그대로 나와 실제로는
    /// 오른쪽을 쳤는데 이펙트만 왼쪽에 남아 보인다(죠안의 「연」에서 나타난 증상).
    /// </param>
    /// <param name="fade">몸 그림과 같이 사라졌다 나타나게(<see cref="UnitState.Fade"/>) — 몸만 사라지고 무기가 남으면 안 된다.</param>
    private void DrawUnitLayers(ObsMotionClip? clip, int tick, int footX, int footY, bool mirror, double fade = 1)
    {
        if (clip == null || fade <= 0) return;
        foreach (var (start, obs, motion) in clip.Children)
        {
            if (start > tick) continue;
            var blend = UiFor(obs)?.BlendAt(motion, tick - start) == 17 ? UiBlend.Add : UiBlend.Alpha;
            // 무기 층은 몸 모션과 같이 돈다 — 서기처럼 되풀이하는 모션이면 자식도 되풀이한다.
            DrawUi(obs, motion, tick - start, footX, footY, blend, mirror: mirror, fade: fade);
        }
    }

    private void DrawEffects()
    {
        _effects.RemoveAll(e => !DrawUi(e.Obs, e.Motion, (int)((_lastTime - e.Start) * TicksPerSecond), e.X, e.Y, UiBlend.Add, loop: false));
    }

    /// <summary>치명타 물들이기 — 방식 9(픽셀 절반)에 가깝게 한 프레임만 화면을 어둡게 번쩍인다.</summary>
    private void DrawCritFlash()
    {
        if (_critFlashAt < 0 || _lastTime - _critFlashAt > 1.0 / TicksPerSecond) return;
        for (int i = 0; i < _fb.Length; i++)
        {
            uint c = _fb[i];
            _fb[i] = 0xFF000000 | ((c >> 16 & 0xFF) / 2 + 90) << 16 | ((c >> 8 & 0xFF) / 2 + 90) << 8 | ((c & 0xFF) / 2 + 90);
        }
    }

    // ── 숫자·글자 ────────────────────────────────────────────────────────────

    private const uint DamageColor = 0xFFFF3030, HealColor2 = 0xFFFFFF60, MissColor = 0xFF64FF64;

    /// <summary>
    /// 떠오르는 숫자·글자 — 피해는 빨강 "HP 91", 회복은 노랑(안 떠오르고 옛 HP 에서 새 HP 로 세어 올라감),
    /// Miss 는 연두. 떠오름은 틱마다 z += 40/나이, 20틱에 사라진다(화면 픽셀 = z×12/20).
    /// </summary>
    private void ShowNumber(UnitState u, string text, uint color, bool rise = true, (int From, int To)? count = null)
    {
        var (x, y) = UnitFoot(u);
        _numbers.Add((text, color, x, y, _lastTime, rise, count));
    }

    private readonly List<(string Text, uint Color, int X, int Y, double Start, bool Rise, (int From, int To)? Count)> _numbers = [];

    private void DrawNumbers()
    {
        _numbers.RemoveAll(n => (_lastTime - n.Start) * TicksPerSecond > 20);
        foreach (var (text, color, x, y, start, rise, count) in _numbers)
        {
            int tick = (int)((_lastTime - start) * TicksPerSecond);
            int lift = 24;
            if (rise)
            {
                double z = 40;
                for (int age = 2; age <= tick; age++) z += 40.0 / age;
                lift = (int)(z * 12 / 20);
            }
            string shown = text;
            if (count is { } c)
            {
                int step = Math.Max(1, Math.Abs(c.To - c.From) / 10);
                int value = c.From + Math.Sign(c.To - c.From) * Math.Min(Math.Abs(c.To - c.From), step * tick);
                shown = $"{text} {value}";
            }
            var (_, w, _) = GetText(shown, color, 16);
            // 검정 1픽셀 테두리 뒤에 제 색
            foreach (var (ox, oy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                DrawText(shown, x - w / 2 + ox, y - lift + oy, 0xFF000000, 16);
            DrawText(shown, x - w / 2, y - lift, color, 16);
        }
    }
}
