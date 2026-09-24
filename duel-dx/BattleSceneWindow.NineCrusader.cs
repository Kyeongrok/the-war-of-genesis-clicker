using System.IO;

namespace DuelDx;

/// <summary>
/// 나인 크루세이더(어빌리티 139, work 1491) — 칼 한 자루가 날아다니며 대상들을 차례로 꿰뚫는 필살기.
/// </summary>
/// <remarks>
/// <para>원본 핸들러 <c>0x1009cb50</c>(분석-스킬 「필살기 — 나인 크루세이더」)를 옮겼다.</para>
/// <list type="bullet">
/// <item><b>찌르는 수</b> — 범위 안 대상이 1명이면 3번, 2명이면 5번, 3명 이상이면 9번. 차례는 난수로 뽑는다(같은 대상이 거듭 나올 수 있다).</item>
/// <item><b>떠나는 칼</b> — 시전자 곁(x+11)에서 칼(585:44)이 가속하며 화면 아래로 빠진다(<c>0x100c3340</c> → <c>0x100c3490</c>: 빠르기 10, ×1.1).</item>
/// <item><b>나는 칼</b> — 시전자 아래 600(화면 480px)에서 들어와, 대상 → 그 대상을 120 지나친 점 → 다음 대상 … 을 잇는 길(점 2N+1개)을 난다
/// (<c>0x100ce9a0</c>·<c>0x100c4670</c>). 이동기(<c>0x10037e50</c>)는 매 틀 빠르기만큼 다가가고(닿을 만하면 붙는다) 빠르기에 곱수를 곱한다.</item>
/// <item><b>갱신</b>(<c>0x100cea90</c>) — 다음 점이 바뀌면 칼끝을 <b>한 틀에 한 칸씩</b> 돌린다(돌기 시작할 때 휙 소리 1428:1). 다 돌면
/// 짝수 점(대상)으로 가는 길은 10틱 겨누고 빠르기 110·×0.9 로 찌르러 가고(1428:2), 홀수 점(지나친 점)으로 가는 길은 방금 꿰뚫은 것이라
/// 빠르기 40·×0.65 로 미끄러지며 불꽃·피해를 낸다(1428:3). 40×0.65ⁿ 을 다 더하면 114 — 120 지나친 점 앞에서 멈춰 선다.</item>
/// <item><b>칼끝 방향</b> — 다음 점까지 화면 (dx, dy) 의 <c>atan2(dx, dy)</c> 를 22.5° 칸 아홉으로 나눈 b(1 아래 … 9 위), 모션 <c>b×4+8</c>.
/// 왼쪽으로 갈 때는 그 그림을 뒤집는다(<c>0x100e56c0(1)</c>, 오른쪽은 3).</item>
/// <item><b>끝</b> — 마지막 지나친 점에서 칼이 시전자 곁으로 29틱에 걸쳐 돌아온다(<c>0x100cb550</c>, 1428:4).</item>
/// </list>
/// <para>칼 그림은 Obs 585(칼끝 빛)와 그 자식 키 586(칼날)이 모션마다 한 컷이다. 원본의 불꽃(<c>0x100cc600</c>)은 그림 자료가 없는 입자라
/// 여기서는 치는 이펙트 1401 로 갈음한다(가설). 피해는 대상마다 처음 꿰뚫릴 때 한 번 준다.</para>
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int NineCrusaderWork = 1491;
    private const int SwordObs = 585, SwordSoundObs = 1428, SwordSparkObs = 1401;

    /// <summary>원본 이동기(<c>0x10037e50</c>) — 점들을 차례로 따라가며 빠르기에 곱수를 곱한다.</summary>
    private sealed class SwordMover((double X, double Y) start, IReadOnlyList<(double X, double Y)> points, double speed, double factor)
    {
        public double X = start.X, Y = start.Y;
        public int Index;
        public int Pause;
        public double Speed = speed, Factor = factor;
        public readonly IReadOnlyList<(double X, double Y)> Points = points;
        public bool Done => Index >= Points.Count;

        public void Step()
        {
            if (Done) return;
            if (Pause > 0) { Pause--; return; }
            var (tx, ty) = Points[Index];
            double dx = tx - X, dy = ty - Y, dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist <= Speed) (X, Y) = (tx, ty);
            else { X += dx / dist * Speed; Y += dy / dist * Speed; }
            Speed = Math.Clamp(Speed * Factor, 1, 400);   // 곱셈 방식(0x10037fef), 아래는 멈춰 서지 않게
            if (Math.Round(X) == Math.Round(tx) && Math.Round(Y) == Math.Round(ty)) Index++;
        }
    }

    /// <summary>날고 있는 나인 크루세이더 칼.</summary>
    private sealed class SwordFlight
    {
        public required UnitState User;
        /// <summary>떠나는 칼 — 다 빠지면 나는 칼이 시작한다.</summary>
        public required SwordMover Rise;
        public required SwordMover Main;
        /// <summary>점마다 꿰뚫는 대상(짝수 점만, 그 밖은 −1).</summary>
        public required int[] PointTarget;
        /// <summary>돌아오는 칼 — (출발, 도착, 시작 틀).</summary>
        public ((double X, double Y) From, (double X, double Y) To, int StartTick)? Return;
        /// <summary>칼끝 방향 칸(1~17, 원본 <c>+0x12c</c>)과 다 돌고 맞춘 점(<c>+0x130</c>).</summary>
        public int Dir, Settled = -1;
        public bool TurnSound = true;
        public int Tick;
        public double LastStep;
        public readonly Queue<int> Pierced = new();
        public bool Done;
    }

    private readonly List<SwordFlight> _swords = [];

    /// <summary>원본처럼 찌를 차례를 뽑는다 — 대상 1명 3번, 2명 5번, 3명 이상 9번.</summary>
    private List<int> NineCrusaderOrder(List<int> targets)
    {
        if (targets.Count == 0) return [];
        if (targets.Count == 1) return [targets[0], targets[0], targets[0]];
        int n = targets.Count == 2 ? 5 : 9;
        return [.. Enumerable.Range(0, n).Select(_ => targets[_rng.Next(targets.Count)])];
    }

    private SwordFlight StartNineCrusader(UnitState user, List<int> targets)
    {
        var (ux, uy) = UnitFoot(user);
        var order = NineCrusaderOrder(targets);
        // 길 — 대상, 그 대상을 (앞 점에서 본 방향으로) 120 지나친 점, … 그리고 시전자 머리 위(z+200 → 화면 120px). 월드 y 는 화면 0.8배.
        var points = new List<(double X, double Y)>();
        var pointTarget = new List<int>();
        (double X, double Y) from = (ux + 11, uy + 400);          // 앞 점 — 처음은 떠난 칼이 닿은 자리(y+500)
        foreach (int ti in order)
        {
            var (tx, ty) = UnitFoot(_units[ti]);
            double dx = tx - from.X, dy = ty - from.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            (double X, double Y) past = (tx + dx / d * 120, ty + dy / d * 120 * 0.8);
            points.Add((tx, ty)); pointTarget.Add(ti);
            points.Add(past); pointTarget.Add(-1);
            from = past;
        }
        points.Add((ux, uy - 120)); pointTarget.Add(-1);
        var flight = new SwordFlight
        {
            User = user,
            Rise = new SwordMover((ux + 11, uy), [(ux + 11, uy + 400)], 10, 1.1),
            Main = new SwordMover((ux, uy + 480), points, 100, 0.9),
            PointTarget = [.. pointTarget],
            LastStep = _lastTime,
            // 처음부터 첫 대상을 본다 — 한 칸씩 돌리며 들어오면 빠르기 100 으로 첫 대상과 그 지나친 점을 돌기도 전에 지나쳐 첫 찌르기가 빠졌다(가설).
            Dir = SwordDirTo(points[0].X - ux, points[0].Y - (uy + 480)),
        };
        _swords.Add(flight);
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                               $"nine crusader: targets {targets.Count}, order [{string.Join(",", order)}], points {points.Count}" + Environment.NewLine);
        return flight;
    }

    /// <summary>원본 방향 칸 — <c>atan2(dx, dy)</c> 의 크기를 22.5° 아홉 칸으로(1 아래 … 9 위), 오른쪽으로 가면 18 − 칸.</summary>
    private static int SwordDirTo(double dx, double dy)
    {
        double angle = Math.Atan2(dx, dy), a = Math.Abs(angle) * 180 / Math.PI;
        int band = a < 11.25 ? 1 : a < 33.75 ? 2 : a < 56.25 ? 3 : a < 78.75 ? 4 : a < 101.25 ? 5 : a < 123.75 ? 6 : a < 146.25 ? 7 : a < 168.75 ? 8 : 9;
        return angle > 0 && band > 1 ? 18 - band : band;
    }

    /// <summary>방향 칸 → (모션, 뒤집기). 9 넘는 칸(오른쪽)은 그대로, 나머지(왼쪽)는 뒤집는다.</summary>
    private static (int Motion, bool Mirror) SwordMotion(int dir) =>
        dir > 9 ? ((18 - dir) * 4 + 8, false) : (Math.Max(dir, 1) * 4 + 8, true);

    /// <summary>1428 소리 껍데기의 그 모션 소리를 지금 낸다.</summary>
    private void SwordSound(int motion)
    {
        if (_effectTables.GetValueOrDefault(SwordSoundObs)?.Clips.GetValueOrDefault(motion) is not { } clip) return;
        foreach (var (tick, sound) in clip.Sounds) _pendingSounds.Add((_lastTime + tick / TicksPerSecond, sound));
    }

    /// <summary>지난 틀 이후 흐른 틱만큼 칼을 민다 — 기술 코루틴이 매 틀 부른다.</summary>
    private void StepSword(SwordFlight f)
    {
        int ticks = (int)((_lastTime - f.LastStep) * TicksPerSecond);
        if (ticks <= 0) return;
        f.LastStep += ticks / TicksPerSecond;
        for (int k = 0; k < ticks && !f.Done; k++) StepSwordTick(f);
    }

    private void StepSwordTick(SwordFlight f)
    {
        f.Tick++;
        if (f.Return is { } back)
        {
            if (f.Tick - back.StartTick >= 29) f.Done = true;
            return;
        }
        if (!f.Rise.Done) { f.Rise.Step(); return; }

        var m = f.Main;
        m.Step();
        if (m.Index == f.Settled || m.Done && f.Settled == m.Points.Count - 1) return;
        int next = Math.Min(m.Index, m.Points.Count - 1);
        // 다음 점으로 칼끝을 한 칸씩 돌린다 — 마지막 점(시전자 머리 위)으로 갈 때는 아래(0)를 본다.
        int want = next == m.Points.Count - 1 ? 0 : SwordDirTo(m.Points[next].X - m.X, m.Points[next].Y - m.Y);
        if (f.Dir != want)
        {
            f.Dir += Math.Sign(want - f.Dir);
            if (f.TurnSound) { SwordSound(1); f.TurnSound = false; }
            return;
        }
        f.Settled = next;
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                               $"sword tick {f.Tick}: point {next}/{m.Points.Count} at ({m.X:0},{m.Y:0}) dir {f.Dir}" + Environment.NewLine);
        if (next % 2 == 0)
        {
            // 대상(또는 끝)으로 — 10틱 겨누고 빠르게 찌르러 간다.
            m.Pause = 10; m.Speed = 110; m.Factor = 0.9;
            SwordSound(2);
            f.TurnSound = true;
        }
        else
        {
            // 방금 대상을 꿰뚫었다 — 불꽃·피해, 그리고 지나친 점까지 미끄러진다.
            m.Speed = 40; m.Factor = 0.65;
            int pierced = f.PointTarget[next - 1];
            if (pierced >= 0) f.Pierced.Enqueue(pierced);
            _effects.Add((SwordSparkObs, 0, _lastTime, (int)m.X, (int)m.Y - 30));
            SwordSound(3);
        }
        if (next == m.Points.Count - 1)
        {
            // 마지막 — 칼이 시전자 곁으로 돌아온다.
            var (ux, uy) = UnitFoot(f.User);
            f.Return = ((m.X, m.Y), (ux + 11, uy), f.Tick);
            SwordSound(4);
        }
    }

    /// <summary>칼을 그린다 — 585(칼끝 빛)와 자식 586(칼날)을 같은 모션으로.</summary>
    private void DrawSwords()
    {
        _swords.RemoveAll(f => f.Done);
        foreach (var f in _swords)
        {
            double x, y;
            int dir = f.Dir;
            if (!f.Rise.Done) { (x, y) = (f.Rise.X, f.Rise.Y); dir = 9; }           // 585:44 — 떠나는 칼
            else if (f.Return is { } back)
            {
                double t = Math.Clamp((f.Tick - back.StartTick) / 29.0, 0, 1);
                (x, y) = (back.From.X + (back.To.X - back.From.X) * t, back.From.Y + (back.To.Y - back.From.Y) * t);
            }
            else (x, y) = (f.Main.X, f.Main.Y);
            var (motion, mirror) = SwordMotion(dir);
            DrawSwordLayer(SwordObs, motion, (int)x, (int)y, mirror);
            foreach (var (_, obs, childMotion, dx, dy, _, flag) in UiFor(SwordObs)?.Clip(motion)?.Children ?? [])
                DrawSwordLayer(obs, childMotion, (int)x + (mirror && flag == 0 ? -dx : dx), (int)y + dy, mirror && flag == 0);
        }
    }

    private void DrawSwordLayer(int obs, int motion, int x, int y, bool mirror) =>
        DrawUi(obs, motion, 0, x, y, BlendOf(UiFor(obs)?.BlendAt(motion, 0) ?? 0), mirror: mirror);
}
