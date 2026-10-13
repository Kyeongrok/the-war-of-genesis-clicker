using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 타키리온 어빌리티 가운데 원본 핸들러가 이펙트 자리·개수·때를 <b>코드로 셈하는</b> 것들(gm-skil-22). 단위는 월드(화면 x 그대로 · y × 0.8 · 높이 × 0.6),
/// 틱은 핸들러 단계 0 부터. 이동기의 붙듦(+0x24)은 「생긴 때부터 세는 틱」으로 읽었다(격려 0x10091670 과 같은 풀이 — 지연 중에도 주는지는 확인 못 함).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>익스퍼트 웨이브 <c>0x1008a880</c> — 회오리 기둥 셋(조각 252:0 열 개씩, 높이 60 간격)이 0 · 15 · 30틱에 시전자 자리에 서고, 80틱부터 180틱 동안
/// 나선(반지름 80 → 40R − 40, 처음 각 1.5π · 0.8333π · 0.1667π, 틱당 0.04π + 0.00005π·t)으로 퍼진다. 위 조각은 아래 조각의 한 틱 전 자리를 따라가 기둥이 휜다.
/// 새 칸에 들어설 때마다 바닥에 252:2. 판정은 290틱. 전에는 푸른 파문 띠 셋이었다(값 1.5 · 0.833 · 0.167 을 속도로 잘못 읽음).</item>
/// <item>소울 스트라이크 <c>0x100ad240</c> — 겨눈 칸에서 네 갈래로 700 이 10px 간격으로 화면 끝까지 깔린다(가로는 모션 6·5·4, 세로는 2·1·0 을 오가며, 두 장마다 한 틱 늦게).</item>
/// <item>일루션 히트 <c>0x10091bc0</c> — 대상 윤곽 점마다 5:1 이 떠서(10~59틱) 50틱 뒤 30틱에 시전자 위 150 으로 모이고, 열 점마다 하나씩 472:3 이 180틱에 대상으로 날아간다(틱당 25). 닿을 때 판정.</item>
/// <item>마인드 체인지 <c>0x1008fb10</c> — 시전자 윤곽 점마다 265:1 이 N/4 틱 뒤 40틱에 대상 위 200 의 같은 윤곽 자리로 옮겨 가 80틱 머문 뒤 틱당 4 로 내려앉는다. 다 내려앉으면 판정.</item>
/// <item>오버 테이크 <c>0x10090060</c> — 시전자 윤곽 점마다 7:3 이 대상 자리의 시전자 윤곽으로 옮겨 갔다가(수명 180) 15틱에 대상 윤곽으로 바뀐다(수명 150). 그 끝에 판정.</item>
/// <item>익스퍼트 블레스트 <c>0x100ae5b0</c> — 고리(20틱) 뒤 20틱에 카메라가 시전자 앞쪽으로(가로 240 · 세로 180), 멈춘 뒤 빔 184 가 높이 100 에서 40R + 300 을 틱당 40 으로 가고
/// 472:3 넷이 옆 ±30 · ±60 에서 나란히 간다. 70틱 뒤 판정. 빔 클래스(0x100c4280)의 그리는 꼴은 못 읽어 곧게 가는 한 장으로 둔다.</item>
/// <item>리미트 캐스트 <c>0x1009b510</c> · 리미트 크래쉬 <c>0x1009b1c0</c> — 나선 조각은 10틱에 생기고, 캐스트는 내려온다(<see cref="BattleScene.SpawnAbilityEffects"/> 의 가지).</item>
/// <item>아다지오 · 프레스토 — 뿌리개 가운데가 대상 위 100. 파워드 배리어 <c>0x10096960</c> — 대상 i 마다 444:7 이 끝나면(98 + 15i) 몸이 20틱 늘어났다 돌아오고 그 끝에 판정.</item>
/// </list>
/// </remarks>
internal sealed unsafe class TachyrionSkill(GameWindow host)
{
    internal const int IllusionHit = 34, SoulStrike = 35, Adagio = 42, Presto = 43, LimitCrash = 45, LimitCast = 46, MindChange = 48, PoweredBarrier = 49,
                       OverTake = 52, ExpertWave = 54, ExpertBlast = 55;

    /// <summary>익스퍼트 블레스트 — 고리 20 + 1 + 20틱에 카메라를 보내고, 멈춘 뒤 빔이 나간다. 카메라가 가는 틱은 15 로 잡았다(가설 — 원본은 멈출 때까지 기다린다).</summary>
    internal const int BlastCamera = 41, BlastBeam = BlastCamera + 15;

    internal static bool Owns(WorkData w, BattleScene.AbilityEffect e) => w.AbilityId switch
    {
        SoulStrike => e.Obs == 700,
        IllusionHit => (e.Obs == 5 && e.Motion == 1) || (e.Obs == 472 && e.Motion == 3),
        MindChange => e.Obs == 265,
        OverTake => e.Obs == 7,
        ExpertBlast => e.Obs is 184 or 1352 || (e.Obs == 472 && e.Motion == 3),
        PoweredBarrier => e.Obs == 1324 && e.Motion == 0,
        _ => false,
    };

    /// <summary>표의 줄을 손보는 것 — 아다지오 478:2 · 프레스토 476:0 의 뿌리개 가운데는 대상 위 100(0x1009adef · 0x1009b02c).</summary>
    internal static BattleScene.AbilityEffect Adjust(WorkData w, BattleScene.AbilityEffect e) => w.AbilityId switch
    {
        Adagio when e.Obs == 478 && e.Motion == 2 => e with { Lift = 60 },
        Presto when e.Obs == 476 && e.Motion == 0 => e with { Lift = 60 },
        _ => e,
    };

    /// <summary>표의 지연에 더할 틱 — 리미트 크래쉬 · 캐스트의 나선 조각은 단계 1 의 10틱에 생긴다(0x1009b291 · 0x1009b5e1).</summary>
    internal static int Late(WorkData w, BattleScene.AbilityEffect e) =>
        w.AbilityId is LimitCrash or LimitCast && ((e.Obs == 476 && e.Motion == 3) || (e.Obs == 8 && e.Motion == 1)) ? 10 : 0;

    private FxTimeline Line => host.FxLine;
    private double At(int tick) => Line.At(tick);
    private int Rand(int n) => Line.Rand(n);
    private static (int X, int Y) Ahead(Facing f) => f switch { Facing.Up => (0, -1), Facing.Down => (0, 1), Facing.Left => (-1, 0), _ => (1, 0) };

    internal void Spawn(WorkData w, UnitState user, int col, int row)
    {
        switch (w.AbilityId)
        {
            case ExpertWave: SpawnWave(w, user, col, row); break;
            case SoulStrike: SpawnSoulStrike(col, row); break;
            case IllusionHit: SpawnIllusionHit(w, user, col, row); break;
            case MindChange: SpawnMindChange(w, user, col, row); break;
            case OverTake: SpawnOverTake(w, user, col, row); break;
            case ExpertBlast: SpawnBlast(w, user, col, row); break;
            case PoweredBarrier: SpawnBarrier(w, user, col, row); break;
        }
    }

    private void HitAt(UnitState t, int tick)
    {
        host.Btl._fxHitAt[t] = At(tick);
        host.Btl._fxLatestStart = Math.Max(host.Btl._fxLatestStart, At(tick));
    }

    private void SpawnWave(WorkData w, UnitState user, int col, int row)
    {
        var fx = host.ForcetrulAb;
        var btl = host.Btl;
        if (host.UiFor(252) == null) return;
        var (ux, uy) = btl.UnitFoot(user);
        const int Move = 80, Turn = 180, Pieces = 10;
        int reach = Math.Max(2, (int)w.AreaArg), wide = 40 * reach - 40;
        fx.Fx(w, 436, 0, 0, ux, uy);
        fx.Fx(w, 436, 1, Move, ux, uy);
        double[] angles = [1.5, 0.8333, 0.1667];
        for (int j = 0; j < 3; j++)
        {
            // 개체의 틱마다의 자리(월드, 시전자 기준) — 80틱까지는 제자리, 그 뒤 고리 이동기.
            var path = new (double X, double Y)[Move + Turn + 1];
            double a = angles[j] * Math.PI, turn = 0.04 * Math.PI;
            for (int t = Move; t < path.Length; t++)
            {
                double r = Math.Truncate(80 + (t - Move) * (double)(wide - 80) / Turn);
                path[t] = (r * Math.Cos(a), r * Math.Sin(a));
                turn += 0.00005 * Math.PI;
                a += turn;
            }
            int born = 15 * j, end = Move + Turn;
            for (int k = 0; k < Pieces; k++)
            {
                // 조각 k 는 4k 틱 뒤에 뜨고, 아래 조각의 한 틱 전 자리(= 개체의 k 틱 전 자리) 위 60k 에 선다. 끝나면 위 조각부터 4틱 간격으로 사라진다.
                int from = born + 4 * k, until = end + 4 * (Pieces - k);
                var frames = new (int X, int Y, int Motion, bool Mirror)[until - from];
                for (int n = 0; n < frames.Length; n++)
                {
                    var (px, py) = path[Math.Clamp(from + n - k, 0, end)];
                    frames[n] = ((int)(ux + px), (int)(uy + py * 0.8 - 60 * k * 0.6), 0, false);
                }
                btl._scripted.Add((252, At(from), frames));
            }
            // 새 칸에 들어설 때마다 바닥에 252:2 한 장(0x100cff4d).
            (int C, int R) cell = (int.MinValue, 0);
            for (int t = Move; t <= end; t++)
            {
                var now = ((int)Math.Floor(path[t].X / 40), (int)Math.Floor(path[t].Y / 40));
                if (now == cell) continue;
                cell = now;
                Line.Put(252, 2, t, ux + path[t].X, uy + path[t].Y * 0.8);
            }
        }
        // 단계 1 — 첫째 기둥이 끝난 뒤 30틱에 범위 안 모두(0x1008ab5a).
        fx.HitAllAt(w, user, col, row, Move + Turn + 30);
    }

    private void SpawnSoulStrike(int col, int row)
    {
        if ((uint)col >= host.Cols || (uint)row >= host.Rows) return;
        int cx = col * TileW + TileW / 2, cy = host.CellCenterY(col, row);
        // 갈래 하나 — 20px 한 바퀴에 두 장(지연 d · d + 1), 바퀴마다 d 가 1 는다. 모션은 high 에서 시작해 high − 2 까지 오간다.
        void Arm(int laps, int dx, int dy, int high)
        {
            int motion = high, way = -1;
            for (int n = 0; n < 2 * Math.Clamp(laps, 0, 80); n++)
            {
                Line.Put(700, motion, n / 2 + n % 2, cx + dx * (40 + 10 * n), cy + dy * (40 + 10 * n) * 0.8);
                if (motion == high) way = -1; else if (motion == high - 2) way = 1;
                motion += way;
            }
        }
        Arm((cx - host._camX) / 20, -1, 0, 6);
        Arm((host._camX + host.ViewWidth - cx) / 20, 1, 0, 6);
        Arm((cy - host._camY) / 20, 0, -1, 2);
        Arm((host._camY + host.ViewHeight - cy) / 20, 0, 1, 2);
    }

    private void SpawnIllusionHit(WorkData w, UnitState user, int col, int row)
    {
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        foreach (var t in Line.Targets(w, user, col, row))
        {
            var (tx, ty) = btl.UnitFoot(t);
            var outline = Line.Outline(t);
            int last = 180;
            for (int i = 0; i < outline.Count; i++)
            {
                int wait = 10 + Rand(50);
                double x = tx + outline[i].X, y = ty + outline[i].Y;
                double ax = ux + Rand(20) - 10, ay = uy + (Rand(20) - 10) * 0.8 - 150 * 0.6;
                Line.Grain(5, 1, wait, (0, x, y), (50, x, y), (80, ax, ay));
                if (i % 10 != 0) continue;
                // 열 점마다 — 모인 자리에서 472:3 이 180틱에 대상(높이 50)으로 틱당 25.
                double bx = tx + Rand(20) - 10, by = ty + (Rand(20) - 10) * 0.8 - 50 * 0.6;
                int appear = 160 - wait, flight = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) / 25));
                Line.Grain(472, 3, appear, (0, ax, ay), (180 - appear, ax, ay), (180 - appear + flight, bx, by));
                last = 180 + flight;
            }
            HitAt(t, last);
        }
    }

    private void SpawnMindChange(WorkData w, UnitState user, int col, int row)
    {
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        var outline = Line.Outline(user);
        int hold = outline.Count / 4;
        foreach (var t in Line.Targets(w, user, col, row))
        {
            var (tx, ty) = btl.UnitFoot(t);
            int last = 121;
            foreach (var (px, py) in outline)
            {
                int wait = 10 + Rand(50), drop = Rand(50) + 176, fall = drop / 4;
                double x = ux + px, y = uy + py, overX = tx + px, overY = ty + py - 200 * 0.6;
                Line.Grain(265, 1, wait, (0, x, y), (hold, x, y), (hold + 40, overX, overY), (hold + 120, overX, overY), (hold + 120 + fall, overX, overY + drop * 0.6));
                last = Math.Max(last, wait + hold + 120 + fall);
            }
            HitAt(t, last);
        }
    }

    private void SpawnOverTake(WorkData w, UnitState user, int col, int row)
    {
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        var mine = Line.Outline(user);
        foreach (var t in Line.Targets(w, user, col, row))
        {
            var (tx, ty) = btl.UnitFoot(t);
            var theirs = Line.Outline(t);
            int last = 150;
            for (int i = 0; i < mine.Count; i++)
            {
                int wait = 10 + Rand(50);
                double x = ux + mine[i].X, y = uy + mine[i].Y, midX = tx + mine[i].X, midY = ty + mine[i].Y;
                var (qx, qy) = theirs.Count > 0 ? theirs[(int)((long)i * theirs.Count / mine.Count)] : mine[i];
                Line.Grain(7, 3, wait, (0, x, y), (50, x, y), (80, midX, midY), (180, midX, midY), (195, tx + qx, ty + qy), (330, tx + qx, ty + qy));
                last = Math.Max(last, wait + 330);
            }
            HitAt(t, last);
        }
    }

    private void SpawnBlast(WorkData w, UnitState user, int col, int row)
    {
        var fx = host.ForcetrulAb;
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        var (dx, dy) = Ahead(user.Facing);
        Line.Later(BlastCamera, () =>
        {
            var (gx, gy) = btl.AheadPoint(user, 240, 180);
            btl.CenterOn(gx, gy);
        });
        fx.Fx(w, 1352, 0, BlastBeam, ux, uy);
        int motion = user.Facing switch { Facing.Up => 3, Facing.Down => 5, _ => 4 };
        bool mirror = user.Facing == Facing.Right;
        double far = 40 * Math.Max(1, (int)w.AreaArg) + 300, speed = dx != 0 ? 40 : 40 * 0.8;
        double fromX = ux, fromY = uy - 100 * 0.6, toX = ux + dx * far, toY = fromY + dy * far * 0.8;
        btl._shots.Add((184, motion, At(BlastBeam), fromX, fromY, toX, toY, speed, 1, 1, 0, 0, mirror));
        // 빔에 붙어 가는 472:3 넷 — 빔과 수직으로 ±30 · ±60(0x100aeb53~).
        foreach (int side in new[] { 30, -30, 60, -60 })
        {
            double sx = dx == 0 ? side : 0, sy = dx == 0 ? 0 : side * 0.8;
            btl._shots.Add((472, 3, At(BlastBeam), fromX + sx, fromY + sy, toX + sx, toY + sy, speed, 1, 1, 0, 0, false));
        }
        fx.HitAllAt(w, user, col, row, BlastBeam + 70);
    }

    private void SpawnBarrier(WorkData w, UnitState user, int col, int row)
    {
        var btl = host.Btl;
        var targets = Line.Targets(w, user, col, row);
        for (int i = 0; i < targets.Count; i++)
        {
            var (tx, ty) = btl.UnitFoot(targets[i]);
            int at = 15 * i + 70 + Math.Max(1, host.EffectTicks(444, 7));
            host.ForcetrulAb.Fx(w, 1324, 0, at, tx, ty);
            btl._bodyShapes.Add((targets[i], 1, At(at), 20, null, 0, 0, 4 / 9.0, null));
            HitAt(targets[i], at + 20);
        }
    }
}
