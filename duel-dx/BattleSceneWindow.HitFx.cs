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
    /// <param name="loop">몸 모션이 되풀이하는가(서기·걷기) — 되풀이하면 자식 키도 한 바퀴마다 다시 터진다.</param>
    private void DrawUnitLayers(ObsMotionClip? clip, int tick, int footX, int footY, bool mirror, double fade = 1, bool loop = true)
    {
        if (clip == null || fade <= 0) return;
        // 되풀이하는 몸 모션에서는 틱을 한 바퀴로 접는다 — 원본도 모션이 처음으로 돌아가면 키를 다시 읽는다.
        int cycle = loop && clip.Length > 0 ? tick % clip.Length : tick;
        foreach (var (start, obs, motion, dx, dy, _, flag) in clip.Children)
        {
            if (start > cycle) continue;
            // 자식은 <b>제 모션이 끝나면 사라진다</b> — 원본은 키마다 개체를 만들고 그 모션이 다 돌면 지운다(0x100e5410 case 2).
            // 안 지우면 앞선 키가 그대로 남아, 크리스티앙의 총 넣기(동작 24)에서 든 총과 <b>머리 위에 뜬 총</b>이 함께 보였다(사용자 보고).
            // 몸과 같이 도는 무기 층(제이슨 Obs 0426·Obs 0060)은 모션 길이가 몸과 같아 한 바퀴 내내 남는다.
            int life = UiFor(obs)?.MotionLength(motion) ?? 0;
            if (life > 0 && cycle - start >= life) continue;
            var blend = BlendOf(UiFor(obs)?.BlendAt(motion, cycle - start) ?? 0);
            // 치우침(x, y)은 키에 들어 있다(분석-모션 ba-8: 인자 2·3). 주인이 반대쪽 옆을 보면 깃발 0 인 자식은 x 를 뒤집고 자식 그림도 뒤집는다(0x100e56f0).
            bool flip = mirror && flag == 0;
            DrawUi(obs, motion, cycle - start, footX + (flip ? -dx : dx), footY + dy, blend, mirror: flip, fade: fade);
        }
    }

    /// <summary>시전자에서 대상으로 날아가는 이펙트 — (Obs, 모션, 시작, 출발 x·y, 도착 x·y).</summary>
    /// <remarks>Ticks 가 0 보다 크면 그 틱 동안 옮기며 모션을 되풀이한다(초상 컷인처럼 한 장짜리 모션). 0 이면 모션 길이 동안 한 번.</remarks>
    private readonly List<(int Obs, int Motion, double Start, int FromX, int FromY, int ToX, int ToY, int Ticks)> _flyingEffects = [];

    /// <summary>날아가는 이펙트를 모션 길이 동안 출발에서 도착으로 옮기며 그린다.</summary>
    private void DrawFlyingEffects()
    {
        _flyingEffects.RemoveAll(f =>
        {
            if (_lastTime < f.Start) return false;
            int tick = (int)((_lastTime - f.Start) * TicksPerSecond);
            if (UiFor(f.Obs) is not { } sprite) return true;
            int length = f.Ticks > 0 ? f.Ticks : Math.Max(1, sprite.MotionLength(f.Motion));
            if (tick >= length) return true;
            double t = (double)tick / length;
            DrawUi(f.Obs, f.Motion, tick, (int)(f.FromX + (f.ToX - f.FromX) * t), (int)(f.FromY + (f.ToY - f.FromY) * t),
                   f.Ticks > 0 ? UiBlend.Alpha : UiBlend.Add, loop: f.Ticks > 0);
            return false;
        });
    }

    private void DrawEffects()
    {
        DrawFlyingEffects();
        DrawSwords();                                       // 나인 크루세이더의 나는 칼
        DrawFinisherFx();                                   // 필살기 앞머리의 빛 알갱이·초상 컷인
        // 지우는 것은 모션이 <b>다 끝났을 때</b>다 — 첫 컷이 몇 틱 뒤에 시작하는 이펙트(크래쉬 봄의 폭탄, 메테오 착탄 170:1)는
        // 첫 틀에 그릴 컷이 없어 예전에는 뜨기도 전에 지워졌다. 그림이 아예 없는 Obs(소리 껍데기)는 바로 지운다.
        _effects.RemoveAll(e =>
        {
            if (_lastTime < e.Start) return false;             // 아직 기다리는 이펙트(지연)
            int tick = (int)((_lastTime - e.Start) * TicksPerSecond);
            // 자식 키만으로 된 모션(필살기 금빛 띠 344:18·19 — 제 컷 없이 자식 여섯)도 있다 — 원본 애니메이터처럼 자식을 함께 그린다(0x100e5410).
            var clip = UiFor(e.Obs)?.Clip(e.Motion);
            if (clip is { Children.Count: > 0 }) DrawUnitLayers(clip, tick, e.X, e.Y, mirror: false, loop: false);
            if (DrawUi(e.Obs, e.Motion, tick, e.X, e.Y, UiBlend.Add, loop: false)) return false;
            return UiFor(e.Obs) is not { } sprite || tick >= Math.Max(sprite.MotionLength(e.Motion), clip?.Children.Count > 0 ? clip.Length : 0);
        });
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
