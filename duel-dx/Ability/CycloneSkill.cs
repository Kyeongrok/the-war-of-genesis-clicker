using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 사이클론 어빌리티 가운데 원본 핸들러가 이펙트 자리·개수·때를 <b>코드로 셈하는</b> 것들(gm-skil-19). 단위는 월드(화면 x 그대로 · y × 0.8 · 높이 × 0.6).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>폭 <c>0x10083580</c> — 12틱 뒤부터 6틱마다 한 걸음씩, 대각선 두 칸에 폭발(170:0 24틱 → 170:1)과 불기둥(110:0 을 4틱마다 높이 60 씩, 걸음 수만큼 · 여섯까지)이 번진다.
/// 범위 R 의 앞 절반 걸음은 「↘ · ↖」, 나머지는 「↗ · ↙」 대각선. 걸음마다 흔들림(세기 4 · 4틱)과 380:0. 판정은 마지막 걸음 뒤(12 + 6R 틱) 범위 안 모두.</item>
/// <item>메테오스트라이크 <c>0x1008c1a0</c> — 착탄 111:0 이 대상마다 시전자와의 거리 / 10 틱 늦게 떨어지고 그 다음 틱에 그 대상이 맞는다.</item>
/// <item>포스 리젼 <c>0x10096010</c> — 유닛 i 마다 20i 틱 늦게: 444:6(높이 60, 70틱 뒤) → 끝나면 몸이 20틱 늘어났다 돌아오고 그 끝에 효과가 든다.</item>
/// <item>희생 <c>0x10090720</c> · 익스플로젼 <c>0x10090de0</c> — 시전자 몸의 윤곽 점마다 빛 알갱이 1294:1 이 빠져나간다(빠르기 5 × 1.1, 10~59틱 늦게;
/// 희생은 옆·위로 흩어지고 익스플로젼은 곧장 위로 400). 그것이 다 나간 뒤에야 대상 쪽 연출과 판정이 이어진다.</item>
/// </list>
/// </remarks>
internal sealed unsafe class CycloneSkill(GameWindow host)
{
    internal const int Pok = 6, Sacrifice = 13, Meteor = 17, Legion = 18, Sonic = 20, Explosion = 22, OverDrive = 8;

    /// <summary>희생 · 익스플로젼의 단계 1 이 시작하는 틱 — 가장 늦은 알갱이(지연 59)가 닿을 때(약 24틱 뒤). 원본의 붙드는 틱(이동기 +0x24)은 못 읽었다.</summary>
    internal const int SecondStage = 83;

    internal static bool Owns(WorkData w, BattleScene.AbilityEffect e) => w.AbilityId switch
    {
        Pok => e.Obs is 170 or 110 or 380,
        Meteor => e.Obs == 111 || (e.Obs == 378 && e.Motion == 0),
        Legion => e.Obs == 444,
        Sonic => e.Obs == 43,
        Sacrifice => e.Obs == 1294,
        Explosion => e.Obs == 1294 || (e.Obs == 588 && e.Motion == 1),      // 588:1 은 유닛 +0x129 가 선 대상에만 — 보통은 588:0
        _ => false,
    };

    /// <summary>표의 줄을 손보는 것 — 높이를 못 읽은 줄.</summary>
    internal static BattleScene.AbilityEffect Adjust(WorkData w, BattleScene.AbilityEffect e) => w.AbilityId switch
    {
        OverDrive when e.Obs == 8 && e.Motion is 0 or 3 => e with { Lift = 60 },              // 뿌리개 가운데가 높이 100(0x100820fb)
        Sacrifice or Explosion when e.Obs == 295 => e with { Lift = 36 },                    // 대상 위 60(0x10090a89)
        Explosion when e.Obs == 478 && e.Motion == 3 => e with { Lift = 210 },               // 높이 300~400 에서 내려온다(0x100914b1)
        _ => e,
    };

    /// <summary>표의 지연에 더할 틱 — 희생 · 익스플로젼의 단계 1 줄(대상 쪽 이펙트)은 알갱이가 다 나간 뒤에 뜬다.</summary>
    internal static int Late(WorkData w, BattleScene.AbilityEffect e) =>
        w.AbilityId is Sacrifice or Explosion && (e.Obs is 295 or 588 or 6 or 478 || (e.Obs is 1395 or 1344 && e.Motion == 1))
            ? SecondStage + (e.Obs == 588 ? 19 : 0) : 0;

    private double At(int tick) => host._lastTime + tick / TicksPerSecond;

    internal void Spawn(WorkData w, UnitState user, int col, int row)
    {
        switch (w.AbilityId)
        {
            case Pok: SpawnPok(w, user, col, row); break;
            case Meteor: SpawnMeteor(w, user, col, row); break;
            case Legion: SpawnLegion(w, user, col, row); break;
            case Sacrifice or Explosion: SpawnSoulLeak(w, user, col, row); break;
        }
    }

    private List<UnitState> Targets(WorkData w, UnitState user, int col, int row) =>
        [.. (host.Btl._fxTargets ?? host.Btl.WorkTargets(w, user, col, row)).Select(i => host._units[i])];

    private void SpawnPok(WorkData w, UnitState user, int col, int row)
    {
        var fx = host.ForcetrulAb;
        var (ux, uy) = host.Btl.UnitFoot(user);
        int reach = Math.Max(1, (int)w.AreaArg), half = reach / 2;
        for (int k = 0; k < reach; k++)
        {
            int tick = 12 + 6 * k, n = k < half ? k + 1 : k - half + 1, slope = k < half ? 1 : -1;
            bool any = false;
            foreach (int side in new[] { 1, -1 })
            {
                int c = user.Col + side * n, r = user.Row + side * slope * n;
                if ((uint)c >= host.Cols || (uint)r >= host.Rows) continue;
                any = true;
                double x = ux + side * 40 * n, y = uy + side * slope * 40 * n * 0.8;
                for (int m = 0; m < Math.Min(6, n); m++) fx.Fx(w, 110, 0, tick + 4 * m, x, y - 60 * m * 0.6);
                fx.Fx(w, 170, 0, tick, x, y, 24);
                fx.Fx(w, 170, 1, tick + 24, x, y);
            }
            if (!any) continue;
            fx.Fx(w, 380, 0, tick, ux, uy);
            host.HeavenEarthAb._shakes.Add((At(tick), At(tick + 4), 4, false));
        }
        fx.HitAllAt(w, user, col, row, 12 + 6 * reach);
    }

    private void SpawnMeteor(WorkData w, UnitState user, int col, int row)
    {
        var fx = host.ForcetrulAb;
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        int last = 0;
        foreach (var t in Targets(w, user, col, row))
        {
            var (tx, ty) = btl.UnitFoot(t);
            double dx = tx - ux, dy = (ty - uy) / 0.8;
            int late = (int)(0.1 * Math.Sqrt(dx * dx + dy * dy));
            fx.Fx(w, 111, 0, late, tx, ty);
            fx.Fx(w, 378, 0, late, ux, uy);
            btl._fxHitAt[t] = At(late + 1);
            last = Math.Max(last, late + 1);
        }
        btl._fxLatestStart = Math.Max(btl._fxLatestStart, At(last));
    }

    private void SpawnLegion(WorkData w, UnitState user, int col, int row)
    {
        var fx = host.ForcetrulAb;
        var btl = host.Btl;
        var targets = Targets(w, user, col, row);
        for (int i = 0; i < targets.Count; i++)
        {
            var (tx, ty) = btl.UnitFoot(targets[i]);
            fx.Fx(w, 444, 6, 20 * i + 70, tx, ty - 36);
            btl._bodyShapes.Add((targets[i], 1, At(20 * i + 97), 20, null, 0, 0, 4 / 9.0, null));
            btl._fxHitAt[targets[i]] = At(20 * i + 117);
        }
        if (targets.Count > 0) btl._fxLatestStart = Math.Max(btl._fxLatestStart, At(20 * (targets.Count - 1) + 117));
    }

    private void SpawnSoulLeak(WorkData w, UnitState user, int col, int row)
    {
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        bool explosion = w.AbilityId == Explosion;
        // 단계 0 — 시전자 지금 컷의 윤곽 점마다 알갱이 하나(윤곽 함수는 격려와 같다, 0x1000ce10).
        if (host._sprites.TryGetValue(user.ChrCode, out var sprite) && sprite.FrameFor(user) is { } f)
        {
            bool Solid(int px, int py) => (uint)px < f.W && (uint)py < f.H && (f.Px[py * f.W + px] & 0xFF000000) != 0;
            for (int py = 0; py < f.H; py++)
                for (int px = 0; px < f.W; px++)
                {
                    if (!Solid(px, py) || (Solid(px - 1, py) && Solid(px + 1, py) && Solid(px, py - 1) && Solid(px, py + 1))) continue;
                    double x = ux + f.X + px, y = uy + f.Y + py;
                    var (toX, toY) = explosion ? (x, y - 400 * 0.6) : (x - f.X - px - 150 + btl._fxRandom.Next(300), uy - btl._fxRandom.Next(300) * 0.6);
                    btl._shots.Add((1294, 1, At(btl._fxRandom.Next(50) + 10), x, y, toX, toY, 5, 1.1, 1, 0, 0, false));
                }
        }
        var targets = Targets(w, user, col, row).Where(t => t != user).ToList();
        foreach (var t in targets)
        {
            var (tx, ty) = btl.UnitFoot(t);
            // 희생 단계 1 — 대상마다 알갱이 여든이 발에서 130~179 높이로 떠오른다(빠르기 3, 30~129틱 늦게, 0x10090c10).
            if (!explosion)
                for (int n = 0; n < 80; n++)
                {
                    double x = tx - 15 + btl._fxRandom.Next(30);
                    btl._shots.Add((1294, 1, At(SecondStage + 30 + btl._fxRandom.Next(100)), x, ty, x, ty - (btl._fxRandom.Next(50) + 130) * 0.6, 3 * 0.6, 1, 1, 0, 0, false));
                }
            btl._fxHitAt[t] = At(SecondStage + 181);
        }
        btl._fxLatestStart = Math.Max(btl._fxLatestStart, At(SecondStage + (targets.Count > 0 ? 181 : 0)));
    }
}
