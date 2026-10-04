using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 필살기 공통 앞머리 — 준비 동작 <c>+0x3f</c> = 7 인 기술(나인 크루세이더·천지파열무·다크 스크림 …)이 핸들러 앞에 도는 연출(<c>0x1007e330</c>).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>단계 0 — 겨눈 쪽을 보고 동작 6(되풀이).</item>
/// <item>단계 1 — 시전 소리 1338:1, 시전자에게 Mov 0042(보라 빛덩이, 가산).</item>
/// <item>단계 2 — 동작 15, 487:0/1/2, 그리고 <b>빛 알갱이 288개</b>: 화면(원본 640×480) 세로 150~310 을 20 간격 9줄, 가로 640 부터 20 간격 32칸.
/// 칸마다 343:1 이 화면 왼쪽 끝(y 는 ±300 흔들림)에서 빠르기 90 으로 그 칸까지 날아오고(시작 지연 k/2 + 0~4틱), 343:0 이 그 자리에 30틱 머문 뒤,
/// 343:3 이 시전자 둘레 먼 점(±(2~7)×256) 쪽으로 빠르기 30 으로 6틱 흩어지며 사라진다. 343 은 밝기 단계(1~8) 키를 가진 빛 네모다.</item>
/// <item>단계 3 — 10틱 뒤 초상 컷인 둘: 모션 2 가 오른쪽 540 에서 139 로 감속해 들어와(80, ×0.9, 30틱) 왼쪽 밖 −300 으로 가속해 나가고(40, ×1.3),
/// 모션 1 이 왼쪽 끝에서 오른쪽 639 로(80, +0.9, 30틱) 들어와 940 으로 나간다(40, +1.3). 화면 (0,130)·(0,340) 에 금빛 띠 344:18·344:19(10틱 뒤).</item>
/// <item>단계 4 — 60틱 뒤 핸들러로 넘어간다.</item>
/// </list>
/// 효과 시간 규칙: <c>0x100c24d0(n)</c> 시작 지연 · <c>0x100c2530(n)</c> 수명(0 = 모션 한 번) · <c>0x100c2640(e)</c> e 가 끝나면 시작 ·
/// <c>0x100c25c0</c>/<c>0x100c25e0</c> 이동 빠르기 아래·위 한계. 원본 좌표는 640×480 화면 기준이라 지금 보기 크기로 늘린다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int PreludeDotObs = 343, PreludeBandObs = 344;

    /// <summary>빛 알갱이 하나 — 날아오기(343:1) · 머물기(343:0, 30틱) · 흩어지기(343:3, 6틱).</summary>
    private sealed record PreludeDot(double Start, double FromX, double FromY, double X, double Y, int FlyTicks, double Vx, double Vy);

    private readonly List<PreludeDot> _preludeDots = [];

    /// <summary>원본 이동기를 따르는 효과 하나(초상 컷인) — 빠르기에 곱수(또는 더하기)를 주고 아래·위 한계로 자른다.</summary>
    private sealed class FxFlight
    {
        public int Obs, Motion;
        public double X, Y, Tx, Ty, Speed, Factor, Min = 1, Max = 100;
        public bool Multiply;
        public int Delay, Life = -1, Age;
        public bool Arrived, Done;
        public FxFlight? Next;
    }

    private readonly List<FxFlight> _fxFlights = [];
    private double _fxLastStep;

    /// <summary>원본 화면(640×480) 좌표를 지금 판 좌표로 — 카메라 자리를 더하고 보기 크기로 늘린다.</summary>
    /// <remarks>
    /// 가로는 보기 폭에 맞춰 늘리고(초상이 화면 끝에서 끝으로 지나간다), 세로는 늘리지 않고 화면 가운데(원본 240)를 기준으로 둔다 —
    /// 빛 알갱이 띠(150~330)와 금빛 띠(130·340)의 간격이 원본 픽셀 그대로여야 빈틈 없는 띠가 된다.
    /// </remarks>
    private (double X, double Y) PreludeScreen(double sx, double sy) =>
        (_camX + sx * ViewWidth / 640.0, _camY + ViewHeight / 2.0 + (sy - 240));

    /// <summary>앞머리를 돌린다 — 기술 코루틴이 핸들러(동작 사슬) 앞에서 끝까지 기다린다.</summary>
    private IEnumerable<bool> FinisherPrelude(WorkData w, UnitState user)
    {
        const double Tick = 1 / TicksPerSecond;
        PlayAction(user, 6);
        PreludeSound(1338, 1);                                   // 시전 소리(0x1007e42c)
        // 단계 1 — Mov 0042 은 SpawnWorkMovies(prelude) 가 이미 띄웠다. 영상(51장, 30fps)이 끝날 만큼 기다린다.
        for (double end = _lastTime + 51 * Tick; _lastTime < end;) yield return true;

        // 단계 2
        PlayAction(user, 15);
        var (ux, uy) = UnitFoot(user);
        foreach (int m in new[] { 0, 1, 2 }) { _effects.Add((487, m, _lastTime, ux, uy)); PreludeSound(487, m); }
        SpawnPreludeDots(ux, uy);
        for (double end = _lastTime + 10 * Tick; _lastTime < end;) yield return true;

        // 단계 3
        SpawnPreludeCutIns(user);
        foreach (var (motion, sy) in new[] { (18, 130), (19, 340) })
        {
            var (bx, by) = PreludeScreen(0, sy);
            _effects.Add((PreludeBandObs, motion, _lastTime + 10 * Tick, (int)bx, (int)by));
        }
        for (double end = _lastTime + 60 * Tick; _lastTime < end;) yield return true;
    }

    private void SpawnPreludeDots(int casterX, int casterY)
    {
        // 칸은 원본처럼 20px 간격 — 보기가 640 보다 넓으면 칸 수를 늘려 폭을 채운다(원본 32칸).
        int columns = Math.Max(32, (ViewWidth + 19) / 20);
        for (int r = 0; r < 180; r += 20)
            for (int k = 0; k < columns; k++)
            {
                double x = _camX + ViewWidth - 20 * k, y = PreludeScreen(0, 150 + r).Y;
                double fx = _camX - 20;
                double fy = y + (_rng.Next(2) == 0 ? -1 : 1) * _rng.Next(300) * 0.8;
                double dist = Math.Sqrt((x - fx) * (x - fx) + (y - fy) * (y - fy));
                int fly = Math.Max(1, (int)Math.Ceiling(dist / 90));
                double start = _lastTime + (k / 2 + _rng.Next(5)) / TicksPerSecond;
                // 흩어질 쪽 — 시전자에서 x·y 따로 ±(2~7)×256(월드) 떨어진 점. 빠르기 30.
                double tx = casterX + (_rng.Next(2) == 0 ? -1 : 1) * (_rng.Next(6) + 2) * 256.0;
                double ty = casterY + (_rng.Next(2) == 0 ? -1 : 1) * (_rng.Next(6) + 2) * 256.0 * 0.8;
                double d = Math.Max(1, Math.Sqrt((tx - x) * (tx - x) + (ty - y) * (ty - y)));
                _preludeDots.Add(new PreludeDot(start, fx, fy, x, y, fly, (tx - x) / d * 30, (ty - y) / d * 30));
            }
    }

    private void SpawnPreludeCutIns(UnitState user)
    {
        if (user.Data is not { FaceId: > 0 } c || UiFor(c.FaceId) == null) return;
        double s = ViewWidth / 640.0;
        FxFlight Flight(int motion, double fromSx, double toSx, double sy, double speed, double factor, bool multiply, int life, int delay) =>
            new()
            {
                Obs = c.FaceId, Motion = motion, Speed = speed * s, Factor = factor, Multiply = multiply, Max = 100 * s, Min = 1,
                Life = life, Delay = delay,
                X = PreludeScreen(fromSx, 0).X, Y = PreludeScreen(0, sy).Y, Tx = PreludeScreen(toSx, 0).X, Ty = PreludeScreen(0, sy).Y,
            };
        // 원본의 두 대 — 1 번은 곱셈(감속·가속), 2 번은 더하기 방식이다(0x10037fe8).
        var a = Flight(2, 540, 139, 150, 80, 0.9, true, 30, 0);
        a.Next = Flight(2, 139, -300, 150, 40, 1.3, true, -1, 0);
        var b = Flight(1, 1, 639, 480, 80, 0.9 * s, false, 30, 0);
        b.Next = Flight(1, 639, 940, 480, 40, 1.3 * s, false, -1, 0);
        _fxLastStep = _lastTime;
        _fxFlights.Add(a);
        _fxFlights.Add(b);
    }

    /// <summary>앞머리 효과를 그린다(판 좌표) — DrawEffects 가 부른다.</summary>
    private void DrawFinisherFx()
    {
        // 빛 알갱이
        _preludeDots.RemoveAll(d => (_lastTime - d.Start) * TicksPerSecond >= d.FlyTicks + 36);
        foreach (var d in _preludeDots)
        {
            int t = (int)((_lastTime - d.Start) * TicksPerSecond);
            if (t < 0) continue;
            if (t < d.FlyTicks)
            {
                double f = (double)t / d.FlyTicks;
                DrawPreludeDot(1, t, d.FromX + (d.X - d.FromX) * f, d.FromY + (d.Y - d.FromY) * f);
            }
            else if (t < d.FlyTicks + 30) DrawPreludeDot(0, 0, d.X, d.Y);
            else
            {
                int u = t - d.FlyTicks - 30;
                DrawPreludeDot(3, u, d.X + d.Vx * u, d.Y + d.Vy * u);
            }
        }

        // 초상 컷인 — 흐른 틱만큼 민다.
        int ticks = (int)((_lastTime - _fxLastStep) * TicksPerSecond);
        if (ticks > 0)
        {
            _fxLastStep += ticks / TicksPerSecond;
            for (int k = 0; k < ticks; k++)
                for (int i = _fxFlights.Count - 1; i >= 0; i--)
                {
                    var f = _fxFlights[i];
                    StepFxFlight(f);
                    if (!f.Done) continue;
                    _fxFlights.RemoveAt(i);
                    if (f.Next is { } next) { (next.X, next.Y) = (f.X, f.Y); _fxFlights.Add(next); }
                }
        }
        foreach (var f in _fxFlights)
            if (f.Delay <= 0) DrawUi(f.Obs, f.Motion, f.Age, (int)f.X, (int)f.Y, UiBlend.Alpha);
    }

    private static void StepFxFlight(FxFlight f)
    {
        if (f.Delay > 0) { f.Delay--; return; }
        f.Age++;
        if (!f.Arrived)
        {
            double dx = f.Tx - f.X, dy = f.Ty - f.Y, dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist <= f.Speed) { (f.X, f.Y) = (f.Tx, f.Ty); f.Arrived = true; }
            else { f.X += dx / dist * f.Speed; f.Y += dy / dist * f.Speed; }
            f.Speed = Math.Clamp(f.Multiply ? f.Speed * f.Factor : f.Speed + f.Factor, f.Min, f.Max);
        }
        f.Done = f.Life >= 0 ? f.Age >= f.Life : f.Arrived;
    }

    /// <summary>효과 모션에 박힌 소리를 지금부터 예약한다 — 487·1338 은 소리만 든 껍데기다.</summary>
    private void PreludeSound(int obs, int motion)
    {
        if (_effectTables.GetValueOrDefault(obs)?.Clips.GetValueOrDefault(motion) is not { } clip) return;
        foreach (var (tick, sound) in clip.Sounds) _pendingSounds.Add((_lastTime + tick / TicksPerSecond, sound));
    }

    /// <summary>343 빛 네모 — 모션의 밝기 단계(1~8, 없으면 8)만큼 더한다.</summary>
    private void DrawPreludeDot(int motion, int tick, double x, double y)
    {
        int level = UiFor(PreludeDotObs)?.BlendAt(motion, tick) ?? 0;
        if (level is <= 0 or > 8) level = 8;
        DrawUi(PreludeDotObs, motion, tick, (int)x, (int)y, UiBlend.Add, loop: false, fade: level / 8.0);
    }
}
