using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

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
internal sealed unsafe partial class BattleScene
{
    /// <summary>맞음 동작 — 동작 2(모션 6·7·8), 15틱.</summary>
    internal const int HitAction = 2, HitActionTicks = 15;

    /// <summary>불꽃 이펙트 Obs 와 유닛 기준점에서의 높이(픽셀).</summary>
    internal const int HitEffectObs = 73, HitEffectLift = 18;

    /// <summary>쓰러질 때도 같은 동작 2 를 21틱 한다(명령 0x2717).</summary>
    internal const int DeathActionTicks = 21;

    internal readonly List<(int Obs, int Motion, double Start, int X, int Y)> _effects = [];

    /// <summary>치명타가 난 틱에 화면 전체를 한 번 물들인다(<c>0x1002e8f0(9,16,1,0)</c>).</summary>
    internal double _critFlashAt = -1;

    /// <summary>맞은 쪽 반응 — 동작 2 와 불꽃 이펙트(피해가 있을 때만).</summary>
    internal void PlayHitReaction(UnitState target, bool damaged)
    {
        // 맞음 동작은 그 유닛 모션(6·7·8)의 길이만큼 한 번 돈다 — 15틱이 아닌 모션이 119개다(ba-21 T7). 전에는 늘 15틱으로 잘랐다.
        int ticks = host._sprites.TryGetValue(target.ChrCode, out var sprite) ? sprite.Clip(HitAction, target.Facing)?.Length ?? 0 : 0;
        PlayActionFor(target, HitAction, ticks > 0 ? ticks : HitActionTicks);
        if (!damaged) return;
        var (x, y) = host.Btl.UnitFoot(target);
        _effects.Add((HitEffectObs, host._rng.Next(3), host._lastTime, x, y - HitEffectLift));
    }

    internal void PlayCritFlash() => _critFlashAt = host._lastTime;

    /// <summary>동작을 정해진 틱 수만큼 재생한다(모션 길이 대신).</summary>
    internal void PlayActionFor(UnitState u, int action, int ticks)
    {
        u.PlayAction(action, ticks / TicksPerSecond);
        host.ScheduleActionSounds(u, action);
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
    internal readonly HashSet<int> _missingLayers = [];

    internal void DrawUnitLayers(ObsMotionClip? clip, int tick, int footX, int footY, bool mirror, double fade = 1, bool loop = true)
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
            // DUELDX_TRACE 면 못 읽은 자식 그림(무기 층 따위)을 한 번씩 적는다 — 「무기를 안 들고 친다」 같은 증상을 가르려고.
            if (Trace && host.UiFor(obs) == null && _missingLayers.Add(obs))
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"), $"layer obs {obs} motion {motion}: 그림 없음" + Environment.NewLine);
            int life = host.UiFor(obs)?.MotionLength(motion) ?? 0;
            if (life > 0 && cycle - start >= life) continue;
            var blend = BlendOf(host.UiFor(obs)?.BlendAt(motion, cycle - start) ?? 0);
            // 치우침(x, y)은 키에 들어 있다(분석-모션 ba-8: 인자 2·3). 주인이 반대쪽 옆을 보면 깃발 0 인 자식은 x 를 뒤집고 자식 그림도 뒤집는다(0x100e56f0).
            bool flip = mirror && flag == 0;
            host.DrawUi(obs, motion, cycle - start, footX + (flip ? -dx : dx), footY + dy, blend, mirror: flip, fade: fade);
            // 무기 층이 다시 부르는 자식(총구의 광선 Obs 0118 · 불꽃 · 검기 따위)도 그린다 — 원본 애니메이터는 자식의 자식까지 만든다(0x100e5410).
            // 전에는 한 겹만 그려 글로리가드의 사격(동작 9)에 광선이 안 나왔다(사용자 보고: Btl 0146). 자식도 제 모션이 끝나면 사라진다.
            int age = cycle - start;
            foreach (var (nestedStart, nestedObs, nestedMotion, ndx, ndy, _, nestedFlag) in host.UiFor(obs)?.Clip(motion)?.Children ?? [])
            {
                if (nestedStart > age || host.UiFor(nestedObs) is not { } nested) continue;
                int nestedAge = age - nestedStart, nestedLife = nested.MotionLength(nestedMotion);
                if (nestedLife > 0 && nestedAge >= nestedLife) continue;
                bool nestedFlip = mirror && nestedFlag == 0;
                host.DrawUi(nestedObs, nestedMotion, nestedAge, footX + (flip ? -dx : dx) + (nestedFlip ? -ndx : ndx), footY + dy + ndy,
                            BlendOf(nested.BlendAt(nestedMotion, nestedAge)), loop: false, mirror: nestedFlip, fade: fade);
            }
        }
    }

    /// <summary>시전자에서 대상으로 날아가는 이펙트 — (Obs, 모션, 시작, 출발 x·y, 도착 x·y).</summary>
    /// <remarks>Ticks 가 0 보다 크면 그 틱 동안 옮기며 모션을 되풀이한다(초상 컷인처럼 한 장짜리 모션). 0 이면 모션 길이 동안 한 번.</remarks>
    internal readonly List<(int Obs, int Motion, double Start, int FromX, int FromY, int ToX, int ToY, int Ticks)> _flyingEffects = [];

    /// <summary>날아가는 이펙트를 모션 길이 동안 출발에서 도착으로 옮기며 그린다.</summary>
    /// <summary>
    /// 빠르기가 정해진 직선탄(이동기 0x100c3490, 틱 0x10037b50) — 틱당 <c>Speed</c> px 로 To 를 향해 가고, 틱마다 빠르기에 배율을 곱하거나(방식 1)
    /// 더한다(방식 0). 최소·최대 빠르기로 자른다. 닿으면 사라진다. 전에는 「모션 길이 동안 등속」 한 가지뿐이었다(ba-21 fx F2).
    /// </summary>
    internal readonly List<(int Obs, int Motion, double Start, double FromX, double FromY, double ToX, double ToY,
                           double Speed, double Scale, int Mode, double Min, double Max, bool Mirror)> _shots = [];

    /// <summary>좌우를 뒤집어 그릴 이펙트(_effects 의 줄) — 시전자가 오른쪽을 볼 때 따위(0x100e56c0, ba-21 fx F10).</summary>
    internal readonly List<(int Obs, int Motion, double Start, int X, int Y)> _effectMirrors = [];

    /// <summary>
    /// 고리(0x100cd310)·포물선(0x100cb550) 이동기 — 고리: 가운데 둘레를 반지름 R0 → R1, 처음 각 A0 에서 각속도 W 로 Ticks 틱 돈다
    /// (점은 틱마다 하나 — 0x10039410). 고리 이펙트 자체는 안 보이고 <b>틱마다 그 자리에 그림을 하나씩 남긴다</b>(0x100cd3d0 — 수명 = 모션 길이 − 1),
    /// 그래서 보이는 것은 도는 꼬리다. 포물선: 이름과 달리 From → To 를 Ticks 틱에 <b>곧게</b> 간다(0x10037ac0 — 솟는 항이 없다).
    /// </summary>
    internal readonly List<(int Obs, int Motion, double Start, int Kind, double X0, double Y0, double X1, double Y1,
                           int Ticks, double R0, double R1, double A0, double W, bool Mirror)> _movers = [];

    /// <summary>
    /// 떨굼 이동기(틱 0x10038fa0)의 조각 — 틱마다 x += vx/2 · y += vy/2, vz′ = vz − 5, z += (vz + vz′)/4, z ≤ 0 이면 z = −z · vz = −vz′ × 튕김.
    /// 느려지고(|vz| &lt; 5) 땅에서 5 안이면 끝난다. 화면 y = y × 32/40 − z × 12/20(0x100ea910). 땅 높이는 시작 칸 기준 0 으로 본다(원본은 절대 높이 0 에서 튄다).
    /// </summary>
    internal readonly List<(int Obs, int Motion, double Start, double X, double Y, double Z, double Vx, double Vy, double Vz,
                           double Bounce, int Life, bool Mirror)> _fallers = [];

    /// <summary>연출 전용 난수 — 전투 판정의 난수 차례를 안 건드린다.</summary>
    internal readonly Random _fxRandom = new();

    /// <summary>
    /// 리인카네이션의 쫓는 그림(0x100c4810) — 앞잡이(안 보임, 0x100c45b0)는 (0,−51)에서 아홉 점을 틱당 3 으로 돌고, 그림은 (0,−52)에서 틱당 2 로 앞잡이를 향한다.
    /// 앞잡이가 끝난 다음 틱에 사라진다. 자리는 시전자 발 기준 월드 단위(화면 y × 0.8).
    /// </summary>
    internal readonly List<(int Obs, int Motion, double Start, int X, int Y, bool Mirror)> _chasers = [];

    internal static readonly (double X, double Y)[] ChaserPath =
        [(-30, -30), (-50, 0), (-30, 30), (0, 50), (30, 30), (50, 0), (30, -30), (20, -40), (0, -50)];

    internal void DrawChasers()
    {
        _chasers.RemoveAll(c =>
        {
            if (host._lastTime < c.Start) return false;
            if (host.UiFor(c.Obs) == null) return true;
            int ticks = (int)((host._lastTime - c.Start) * TicksPerSecond), at = 0;
            double lx = 0, ly = -51, fx = 0, fy = -52;
            for (int t = 0; t < ticks; t++)
            {
                if (at >= ChaserPath.Length) return true;       // 앞잡이가 끝난 다음 틱
                double left = 3;
                while (left > 0 && at < ChaserPath.Length)
                {
                    double dx = ChaserPath[at].X - lx, dy = ChaserPath[at].Y - ly, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d <= left) { (lx, ly) = ChaserPath[at]; at++; break; }      // 점에 닿으면 그 틱은 거기서 멈춘다
                    (lx, ly, left) = (lx + dx / d * left, ly + dy / d * left, 0);
                }
                double cx = lx - fx, cy = ly - fy, far = Math.Sqrt(cx * cx + cy * cy);
                (fx, fy) = far <= 2 ? (lx, ly) : (fx + cx / far * 2, fy + cy / far * 2);
            }
            int key = host.UiFor(c.Obs)?.BlendAt(c.Motion, ticks) ?? 0;
            host.DrawUi(c.Obs, c.Motion, ticks, (int)(c.X + fx), (int)(c.Y + fy * 0.8), key is (>= 1 and <= 8) or 10 or 12 ? BlendOf(key) : UiBlend.Add,
                        loop: true, fade: BlendFade(key), mirror: c.Mirror);
            return false;
        });
    }

    /// <summary>뿌리개의 조각 탄(0x100c3340 + 0x100c3490) — 직선으로 가며 빠르기가 틱마다 바뀐다. 닿거나 수명(모션 길이 − 1)이 다하면 사라진다.</summary>
    internal readonly List<(int Obs, int Motion, double Start, double FromX, double FromY, double ToX, double ToY,
                           double Speed, double Scale, int Mode, int Life, bool Mirror)> _pieces = [];

    internal void DrawPieces()
    {
        _pieces.RemoveAll(s =>
        {
            if (host._lastTime < s.Start) return false;
            int tick = (int)((host._lastTime - s.Start) * TicksPerSecond);
            double dx = s.ToX - s.FromX, dy = s.ToY - s.FromY, total = Math.Sqrt(dx * dx + dy * dy);
            if (tick >= s.Life || host.UiFor(s.Obs) == null) return true;
            double speed = s.Speed, gone = 0;
            for (int t = 0; t < tick && gone < total; t++)
            {
                gone += speed;
                speed = Math.Clamp(s.Mode == 1 ? speed * s.Scale : speed + s.Scale, 1, 40);      // 이동기 기본 한도(1~40)
            }
            if (gone >= total && total > 0) return true;
            double k = total > 0 ? gone / total : 0;
            int key = host.UiFor(s.Obs)?.BlendAt(s.Motion, tick) ?? 0;
            host.DrawUi(s.Obs, s.Motion, tick, (int)(s.FromX + dx * k), (int)(s.FromY + dy * k),
                        key is (>= 1 and <= 8) or 10 or 12 ? BlendOf(key) : UiBlend.Add, loop: false, fade: BlendFade(key), mirror: s.Mirror);
            return false;
        });
    }

    /// <summary>
    /// 격려의 윤곽 알갱이 — Start 에 (X, Y)에 떠서 Hold 틱 뒤부터 80틱 동안 (Cx, Cy)로 나선을 그리며 모인다(반지름 곧게 0 으로, 틱당 0.03π).
    /// 자리는 화면 픽셀, 나선은 월드에서 원(화면 y × 0.8).
    /// </summary>
    internal readonly List<(int Obs, int Motion, double Start, int Hold, int X, int Y, int Cx, int Cy)> _cheer = [];

    internal void DrawCheer()
    {
        _cheer.RemoveAll(p =>
        {
            if (host._lastTime < p.Start) return false;
            int tick = (int)((host._lastTime - p.Start) * TicksPerSecond), k = tick - p.Hold;
            if (k >= 80 || host.UiFor(p.Obs) == null) return true;
            double x = p.X, y = p.Y;
            if (k > 0)
            {
                double dx = p.X - p.Cx, dy = (p.Y - p.Cy) / 0.8, far = Math.Sqrt(dx * dx + dy * dy) * (1 - k / 80.0), angle = Math.Atan2(dy, dx) + 0.03 * Math.PI * k;
                (x, y) = (p.Cx + far * Math.Cos(angle), p.Cy + far * Math.Sin(angle) * 0.8);
            }
            int key = host.UiFor(p.Obs)?.BlendAt(p.Motion, tick) ?? 0;
            host.DrawUi(p.Obs, p.Motion, tick, (int)x, (int)y, key is (>= 1 and <= 8) or 10 or 12 ? BlendOf(key) : UiBlend.Add, loop: true, fade: BlendFade(key));
            return false;
        });
    }

    /// <summary>틱마다 자리·모션이 미리 정해진 그림 하나(라이트닝 샤벨의 칼) — 틀을 다 쓰면 사라진다.</summary>
    internal readonly List<(int Obs, double Start, (int X, int Y, int Motion, bool Mirror)[] Frames)> _scripted = [];

    internal void DrawScripted()
    {
        _scripted.RemoveAll(s =>
        {
            if (host._lastTime < s.Start) return false;
            int tick = (int)((host._lastTime - s.Start) * TicksPerSecond);
            if (tick >= s.Frames.Length || host.UiFor(s.Obs) == null) return true;
            var f = s.Frames[tick];
            int key = host.UiFor(s.Obs)?.BlendAt(f.Motion, tick) ?? 0;
            host.DrawUi(s.Obs, f.Motion, tick, f.X, f.Y, key is (>= 1 and <= 8) or 10 or 12 ? BlendOf(key) : UiBlend.Add, loop: true, fade: BlendFade(key), mirror: f.Mirror);
            return false;
        });
    }

    /// <summary>고리 이동기의 꼬리 — 틱마다 한 점(미리 셈한 화면 자리)에 그림을 남긴다(0x100cd3d0, 수명 = 모션 길이 − 1).</summary>
    internal readonly List<(int Obs, int Motion, double Start, (int X, int Y)[] Points, bool Mirror)> _ringTrails = [];

    internal void DrawRingTrails()
    {
        _ringTrails.RemoveAll(m =>
        {
            if (host._lastTime < m.Start) return false;
            if (host.UiFor(m.Obs) == null) return true;
            int len = Math.Max(1, host.EffectTicks(m.Obs, m.Motion) - 1), now = (int)((host._lastTime - m.Start) * TicksPerSecond);
            if (now >= m.Points.Length + len) return true;
            for (int i = Math.Max(0, now - len + 1); i <= now && i < m.Points.Length; i++)
            {
                int age = now - i, key = host.UiFor(m.Obs)?.BlendAt(m.Motion, age) ?? 0;
                host.DrawUi(m.Obs, m.Motion, age, m.Points[i].X, m.Points[i].Y, key is (>= 1 and <= 8) or 10 or 12 ? BlendOf(key) : UiBlend.Add,
                            loop: false, fade: BlendFade(key), mirror: m.Mirror);
            }
            return false;
        });
    }

    internal void DrawFallers()
    {
        _fallers.RemoveAll(f =>
        {
            if (host._lastTime < f.Start) return false;
            int ticks = (int)((host._lastTime - f.Start) * TicksPerSecond);
            if (ticks >= f.Life || host.UiFor(f.Obs) == null) return true;
            double x = f.X, y = f.Y, z = f.Z, vz = f.Vz;
            for (int t = 0; t < ticks; t++)
            {
                x += f.Vx * 0.5;
                y += f.Vy * 0.5 * 0.8;
                double next = vz - 5.0;
                z += (vz + next) * 0.25;
                vz = next;
                if (z <= 0) { z = -z; vz = -next * f.Bounce; }
                if (vz > -5 && vz < 5 && z < 5) return true;
            }
            int key = host.UiFor(f.Obs)?.BlendAt(f.Motion, ticks) ?? 0;
            host.DrawUi(f.Obs, f.Motion, ticks, (int)x, (int)(y - z * 0.6), key is (>= 1 and <= 8) or 10 or 12 ? BlendOf(key) : UiBlend.Add,
                        loop: true, fade: BlendFade(key), mirror: f.Mirror);
            return false;
        });
    }

    internal void DrawMovers()
    {
        DrawFallers();
        DrawRingTrails();
        DrawChasers();
        DrawScripted();
        DrawPieces();
        DrawCheer();
        _movers.RemoveAll(m =>
        {
            if (host._lastTime < m.Start) return false;
            double ticks = (host._lastTime - m.Start) * TicksPerSecond;
            if (host.UiFor(m.Obs) == null) return true;
            if (ticks >= m.Ticks) return true;
            double k = ticks / Math.Max(1, m.Ticks), x = m.X0 + (m.X1 - m.X0) * k, y = m.Y0 + (m.Y1 - m.Y0) * k;
            if (m.Kind == 7) (x, y) = (m.X0, m.Y0);   // 제자리에 Ticks 틱(엘레맨탈 라이트의 빛덩이)
            if (m.Kind == 6)        // 떠오르는 나선 — 반지름 R0 원을 각속도 W 로 돌며 틱당 R1 씩 뜬다(높이 × 0.6)
                (x, y) = (m.X0 + m.R0 * Math.Cos(m.A0 + m.W * (int)ticks), m.Y0 + m.R0 * Math.Sin(m.A0 + m.W * (int)ticks) * TileH / TileW - m.R1 * (int)ticks * 0.6);
            int key = host.UiFor(m.Obs)?.BlendAt(m.Motion, (int)ticks) ?? 0;
            host.DrawUi(m.Obs, m.Motion, (int)ticks, (int)x, (int)y, key is (>= 1 and <= 8) or 10 or 12 ? BlendOf(key) : UiBlend.Add,
                   loop: true, fade: BlendFade(key), mirror: m.Mirror);
            return false;
        });
    }

    internal void DrawShots()
    {
        DrawMovers();
        _shots.RemoveAll(s =>
        {
            if (host._lastTime < s.Start) return false;
            int tick = (int)((host._lastTime - s.Start) * TicksPerSecond);
            double dx = s.ToX - s.FromX, dy = s.ToY - s.FromY, total = Math.Sqrt(dx * dx + dy * dy);
            if (total < 1 || host.UiFor(s.Obs) == null) return true;
            // 그 틱까지 간 거리를 처음부터 다시 센다(틱 수가 작아 싸다).
            double speed = Math.Max(1, s.Speed), gone = 0;
            for (int t = 0; t < tick && gone < total; t++)
            {
                gone += speed;
                speed = s.Mode == 1 ? speed * s.Scale : speed + s.Scale;
                if (s.Min > 0) speed = Math.Max(s.Min, speed);
                if (s.Max > 0) speed = Math.Min(s.Max, speed);
                speed = Math.Max(0.5, speed);
            }
            if (gone >= total || tick > 600) return true;
            double k = gone / total;
            int key = host.UiFor(s.Obs)?.BlendAt(s.Motion, tick) ?? 0;
            host.DrawUi(s.Obs, s.Motion, tick, (int)(s.FromX + dx * k), (int)(s.FromY + dy * k),
                   key is (>= 1 and <= 8) or 10 or 12 ? BlendOf(key) : UiBlend.Add, loop: true, fade: BlendFade(key), mirror: s.Mirror);
            return false;
        });
    }

    internal void DrawFlyingEffects()
    {
        DrawShots();
        _flyingEffects.RemoveAll(f =>
        {
            if (host._lastTime < f.Start) return false;
            int tick = (int)((host._lastTime - f.Start) * TicksPerSecond);
            if (host.UiFor(f.Obs) is not { } sprite) return true;
            int length = f.Ticks > 0 ? f.Ticks : Math.Max(1, sprite.MotionLength(f.Motion));
            if (tick >= length) return true;
            double t = (double)tick / length;
            host.DrawUi(f.Obs, f.Motion, tick, (int)(f.FromX + (f.ToX - f.FromX) * t), (int)(f.FromY + (f.ToY - f.FromY) * t),
                   f.Ticks > 0 ? UiBlend.Alpha : UiBlend.Add, loop: f.Ticks > 0);
            return false;
        });
    }

    internal void DrawEffects()
    {
        DrawFlyingEffects();
        host.NineCrusaderAb.DrawSwords();                                       // 나인 크루세이더의 나는 칼
        host.FinisherPreludeAb.DrawFinisherFx();                                   // 필살기 앞머리의 빛 알갱이·초상 컷인
        host.AcrostAb.DrawSummonGrow();
        host.HeavenEarthAb.DrawHeavenEarthFx();                                // 천지 파열무의 X 자 불길·파편(시각표 효과 — 서몬 몬스터도 쓴다)
        host.AcrostAb.DrawFireBalls();                                    // 엘레맨탈 파이어의 도는 불덩이
        host.AstralArrowAb.DrawArrows();                                       // 아스트럴 애로우의 오르내리는 화살
        // 지우는 것은 모션이 <b>다 끝났을 때</b>다 — 첫 컷이 몇 틱 뒤에 시작하는 이펙트(크래쉬 봄의 폭탄, 메테오 착탄 170:1)는
        // 첫 틀에 그릴 컷이 없어 예전에는 뜨기도 전에 지워졌다. 그림이 아예 없는 Obs(소리 껍데기)는 바로 지운다.
        _effects.RemoveAll(e => DrawEffectOnce(e, mirror: false));
        _effectMirrors.RemoveAll(e => DrawEffectOnce(e, mirror: true));
    }

    /// <summary>이펙트 한 줄을 그린다 — 다 끝났으면 true(지운다).</summary>
    internal bool DrawEffectOnce((int Obs, int Motion, double Start, int X, int Y) e, bool mirror)
    {
        if (host._lastTime < e.Start) return false;             // 아직 기다리는 이펙트(지연)
        int tick = (int)((host._lastTime - e.Start) * TicksPerSecond);
        // 자식 키만으로 된 모션(필살기 금빛 띠 344:18·19 — 제 컷 없이 자식 여섯)도 있다 — 원본 애니메이터처럼 자식을 함께 그린다(0x100e5410).
        var clip = host.UiFor(e.Obs)?.Clip(e.Motion);
        // 이펙트의 시간줄 자식은 <b>제 모션 길이만큼만</b> 산다(0x100d2d00 — 길이 0 이면 한 틱) — 금빛 띠는 여섯 장이 한 틱에 하나씩 번갈아 뜨는 것이다.
        // 전에는 길이 0 인 자식을 안 지워 여섯 장이 쌓여 더해져 흰 네모가 됐다(사용자 보고: 이데아 캐논). 자식의 자식(띠의 가운데·오른쪽 조각)도 그린다.
        if (clip is { Children.Count: > 0 })
            foreach (var (start, obs, motion, dx, dy, _, _) in clip.Children)
            {
                if (start > tick || host.UiFor(obs) is not { } childSprite) continue;
                int age = tick - start;
                if (age >= Math.Max(1, childSprite.MotionLength(motion))) continue;
                void Layer(int layerObs, int layerMotion, int lx, int ly)
                {
                    int layerKey = host.UiFor(layerObs)?.BlendAt(layerMotion, age) ?? 0;
                    host.DrawUi(layerObs, layerMotion, age, lx, ly, layerKey is (>= 1 and <= 8) or 10 or 12 ? BlendOf(layerKey) : layerKey == 17 ? UiBlend.Add : UiBlend.Alpha,
                                loop: false, fade: BlendFade(layerKey), mirror: mirror);
                }
                Layer(obs, motion, e.X + dx, e.Y + dy);
                foreach (var (nestedStart, nestedObs, nestedMotion, ndx, ndy, _, _) in childSprite.Clip(motion)?.Children ?? [])
                    if (nestedStart <= age) Layer(nestedObs, nestedMotion, e.X + dx + ndx, e.Y + dy + ndy);
            }
        // 섞기 방식은 모션의 종류 3 키를 따른다(ba-21 fx F14): 1~8 은 그 단계의 반투명(칼날 171 · 바위 251:6 · 422:1), 10 은 닷지(450:0),
        // 그 밖(17 가산 · 키 없음 · 19 — 식을 못 푼 것)은 전처럼 가산. 전에는 전부 가산이라 반투명 이펙트가 하얗게 탔다.
        int key = host.UiFor(e.Obs)?.BlendAt(e.Motion, tick) ?? 0;
        var blend = key is (>= 1 and <= 8) or 10 or 12 ? BlendOf(key) : UiBlend.Add;
        if (host.DrawUi(e.Obs, e.Motion, tick, e.X, e.Y, blend, loop: false, fade: BlendFade(key), mirror: mirror)) return false;
        return host.UiFor(e.Obs) is not { } sprite || tick >= Math.Max(sprite.MotionLength(e.Motion), clip?.Children.Count > 0 ? clip.Length : 0);
    }

    /// <summary>치명타 물들이기 — 방식 9(픽셀 절반)에 가깝게 한 프레임만 화면을 어둡게 번쩍인다.</summary>
    internal void DrawCritFlash()
    {
        if (_critFlashAt < 0 || host._lastTime - _critFlashAt > 1.0 / TicksPerSecond) return;
        for (int i = 0; i < host._fb.Length; i++)
        {
            uint c = host._fb[i];
            host._fb[i] = 0xFF000000 | ((c >> 16 & 0xFF) / 2 + 90) << 16 | ((c >> 8 & 0xFF) / 2 + 90) << 8 | ((c & 0xFF) / 2 + 90);
        }
    }

    // ── 숫자·글자 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 숫자 색 — 원본 COLORREF 그대로 순색: 피해 0x0000ff = (255,0,0) · 회복 0x00ffff = (255,255,0) · Miss 0x64ff64(<c>0x100d2580</c>, 감사 3 H3).
    /// 전에는 피해 (255,48,48)·회복 (255,255,96) 으로 조금 바랬다.
    /// </summary>
    internal const uint DamageColor = 0xFFFF0000, HealColor2 = 0xFFFFFF00, MissColor = 0xFF64FF64;

    /// <summary>
    /// 떠오르는 숫자·글자 — 피해는 빨강 "HP 91", 회복은 노랑(안 떠오르고 옛 HP 에서 새 HP 로 세어 올라감),
    /// Miss 는 연두. 떠오름은 틱마다 z += 40/나이, 20틱에 사라진다(화면 픽셀 = z×12/20).
    /// 세어 올라가는 회복 숫자는 <b>목표 HP 에 닿는 그 틱에</b> 사라진다(<c>0x100d2950</c> — 차이/10(최소 1)씩이라 보통 10~11틱).
    /// </summary>
    internal void ShowNumber(UnitState u, string text, uint color, bool rise = true, (int From, int To)? count = null)
    {
        var (x, y) = host.Btl.UnitFoot(u);
        _numbers.Add((text, color, x, y, host._lastTime, rise, count));
    }

    /// <summary>판의 그 자리에 숫자를 띄운다 — 물체가 맞을 때는 물체 자리에 뜬다(0x100e77e0, ba-20 O P2).</summary>
    internal void ShowNumberAt(int x, int y, string text, uint color) => _numbers.Add((text, color, x, y, host._lastTime, true, null));

    internal readonly List<(string Text, uint Color, int X, int Y, double Start, bool Rise, (int From, int To)? Count)> _numbers = [];

    internal void DrawNumbers()
    {
        _numbers.RemoveAll(n =>
        {
            double age = (host._lastTime - n.Start) * TicksPerSecond;
            if (age > 20) return true;
            // 세어 올라가는 숫자 — 목표에 닿는 틱에 지운다(전에는 닿은 뒤로도 20틱까지 남았다, 감사 3 H3).
            if (n.Count is not { } c) return false;
            int diff = Math.Abs(c.To - c.From);
            return Math.Max(1, diff / 10) * (int)age >= diff;
        });
        foreach (var (text, color, x, y, start, rise, count) in _numbers)
        {
            int tick = (int)((host._lastTime - start) * TicksPerSecond);
            int lift = 24;
            if (rise)
            {
                // 0x100d2610: 틱마다 나이++ 뒤 z += 40/나이 — <b>정수 나눗셈, 나이 1 부터</b>(z 40 → 80, 100, 113, 123 …, 20틱째 178).
                int z = 40;
                for (int age = 1; age <= tick; age++) z += 40 / age;
                lift = z * 12 / 20;
            }
            string shown = text;
            if (count is { } c)
            {
                int step = Math.Max(1, Math.Abs(c.To - c.From) / 10);
                int value = c.From + Math.Sign(c.To - c.From) * Math.Min(Math.Abs(c.To - c.From), step * tick);
                shown = $"{text} {value}";
            }
            var (_, w, _) = host.GetText(shown, color, 16);
            // 피해 숫자는 유닛 기준 x −30 에서 왼쪽 맞춤(0x100d22e0(−30, 0, 40, …)) — Miss·회복은 전처럼 가운데.
            int left = color == DamageColor ? x - 30 : x - w / 2;
            // 검정 1픽셀 테두리 뒤에 제 색
            foreach (var (ox, oy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                host.DrawText(shown, left + ox, y - lift + oy, 0xFF000000, 16);
            host.DrawText(shown, left, y - lift, color, 16);
        }
    }
}
