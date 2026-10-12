using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 오즈마 어빌리티 가운데 원본 핸들러가 이펙트 자리·개수·때를 <b>코드로 셈하는</b> 것들(gm-skil-19). 단위는 월드(화면 x 그대로 · y × 0.8 · 높이 × 0.6).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>버프·디버프 여섯(위큰드 아머 <c>0x10098c90</c> · 파워드 아머 · 파워 업/다운 · 스피드 업/다운) — 대상 자리에 낱알 기둥: 큰 낱알 100 · 작은 낱알 50 이
/// 0~79틱 사이에 떠서 빠르기 5 × 1.06 으로 솟거나(위큰드 · 파워 다운 · 스피드 업) 높이 150~199 에서 내려온다. 수명은 모션 길이라 22 · 18틱.
/// 전에는 낱알 한 장이 시전자에서 대상으로 날아갔다.</item>
/// <item>카운터 실드 <c>0x10098050</c> · 카운터 필드 <c>0x10098400</c> — 대상마다 5:0 일흔 개가 0~99틱 사이에 솟는다(빠르기 5 × 1.05, 22틱).
/// 필드는 유닛 i 마다 15i 틱 늦게, 몸이 120틱 동안 가로로 늘었다 준다.</item>
/// <item>그라비티 밸런스 <c>0x100a2b00</c> — 유닛마다 480:0/2 백 개가 발 둘레에서 높이 100 까지 틱당 10 으로 솟는다(30 + 20i 틱부터 40틱에 걸쳐). 판정은 약 64 + 20i 틱.</item>
/// <item>미라클 <c>0x1009b860</c> — 210:0~2 서른 개가 대상 위 150 에서 틱당 3 으로 내려온다(0~99틱 사이).</item>
/// <item>블랙홀 <c>0x1008d200</c> — 중심 278:1 → 278:2(모션 × 8 = 192틱) → 278:3 사슬, 빛 6:1 이백 개가 반지름 300 에서 20틱에 소용돌이치며 빨려 든다(0~129틱 사이).</item>
/// <item>블레이드 샤워 <c>0x10083f60</c> — 칼 열여섯이 3틱 간격으로 시전자 둘레(반지름 90 · 높이 100)에 나타나 떠 있다가 72틱에 2틱 간격으로 바깥으로 흩어지며 사라지고,
/// 약 119틱 뒤 대상마다(30틱 간격) 화면 위에서 빛줄기 185:0 아홉이 틱당 70 으로 내리꽂힌다 — 여덟은 대상 둘레 ±80 에 110:0, 아홉째는 대상에 111:0 과 판정.</item>
/// </list>
/// </remarks>
internal sealed unsafe class OzmaSkill(GameWindow host)
{
    internal const int CounterShield = 98, WeakenArmor = 99, PoweredArmor = 100, PowerDown = 101, PowerUp = 102, SpeedUp = 103, SpeedDown = 104,
                       GravityBalance = 111, BladeShower = 112, CounterField = 113, BlackHole = 115, Miracle = 116, CrashBomb = 94;

    internal static bool Owns(WorkData w, BattleScene.AbilityEffect e) => w.AbilityId switch
    {
        WeakenArmor or PoweredArmor => e.Obs is 6 or 476,
        PowerDown or PowerUp or SpeedUp or SpeedDown => e.Obs is 265 or 482,
        GravityBalance => e.Obs == 480,
        Miracle => e.Obs == 210,
        BlackHole => e.Obs == 278 || (e.Obs == 6 && e.Motion == 1),
        BladeShower => e.Obs == 171 || (e.Obs == 1415 && e.Motion == 2),
        _ => false,
    };

    /// <summary>표의 줄을 손보는 것 — 크래쉬 봄의 둘째 파편 고리는 높이 300 에서(0x100b1b14).</summary>
    internal static BattleScene.AbilityEffect Adjust(WorkData w, BattleScene.AbilityEffect e) =>
        w.AbilityId == CrashBomb && e.Obs == 109 && e.Motion == 6 && e.Delay == 5 ? e with { Lift = 180 } : e;

    /// <summary>표의 지연에 더할 틱 — 크래쉬 봄의 떨어지는 바위 251:0 은 단계 2(31틱)에 생긴다.</summary>
    internal static int Late(WorkData w, BattleScene.AbilityEffect e) => w.AbilityId == CrashBomb && e.Obs == 251 && e.Motion == 0 ? 31 : 0;

    private double At(int tick) => host._lastTime + tick / TicksPerSecond;
    private int Rand(int n) => host.Btl._fxRandom.Next(Math.Max(1, n));

    private List<UnitState> Targets(WorkData w, UnitState user, int col, int row) =>
        [.. (host.Btl._fxTargets ?? host.Btl.WorkTargets(w, user, col, row)).Select(i => host._units[i])];

    /// <summary>대상들의 발 — 대상이 없으면 겨눈 칸 가운데.</summary>
    private List<(int X, int Y)> Feet(WorkData w, UnitState user, int col, int row)
    {
        var feet = Targets(w, user, col, row).Select(host.Btl.UnitFoot).ToList();
        if (feet.Count == 0 && (uint)col < host.Cols && (uint)row < host.Rows) feet.Add((col * TileW + TileW / 2, host.CellCenterY(col, row)));
        return feet;
    }

    internal void Spawn(WorkData w, UnitState user, int col, int row)
    {
        switch (w.AbilityId)
        {
            case WeakenArmor: Columns(w, user, col, row, rise: true, 6, 0, 476, 2); break;
            case PoweredArmor: Columns(w, user, col, row, rise: false, 6, 2, 476, 0); break;
            case PowerDown or SpeedUp: Columns(w, user, col, row, rise: true, 265, 0, 482, 2); break;
            case PowerUp or SpeedDown: Columns(w, user, col, row, rise: false, 265, 2, 482, 0); break;
            case CounterShield or CounterField: SpawnCounter(w, user, col, row); break;
            case GravityBalance: SpawnGravityBalance(w, user, col, row); break;
            case Miracle: SpawnMiracle(w, user, col, row); break;
            case BlackHole: SpawnBlackHole(w, user); break;
            case BladeShower: SpawnBladeShower(w, user, col, row); break;
        }
    }

    private void Columns(WorkData w, UnitState user, int col, int row, bool rise, int bigObs, int bigMotion, int smallObs, int smallMotion)
    {
        var btl = host.Btl;
        foreach (var (tx, ty) in Feet(w, user, col, row))
            for (int n = 0; n < 150; n++)
            {
                bool big = n < 100;
                var (obs, motion) = big ? (bigObs, bigMotion) : (smallObs, smallMotion);
                double x = tx + Rand(30) - 15, y = big ? ty : ty + Rand(50) * 0.8;
                double from = rise ? Rand(50) : 150 + Rand(50), to = rise ? 500 : Rand(50);
                btl._pieces.Add((obs, motion, At(Rand(80)), x, y - from * 0.6, x, y - to * 0.6, 5 * 0.6, 1.06, 1, Math.Max(2, host.EffectTicks(obs, motion) - 1), false));
            }
    }

    private void SpawnCounter(WorkData w, UnitState user, int col, int row)
    {
        var btl = host.Btl;
        var targets = Targets(w, user, col, row);
        bool field = w.AbilityId == CounterField;
        int life = Math.Max(2, host.EffectTicks(5, 0) - 1);
        for (int i = 0; i < targets.Count; i++)
        {
            int late = field ? 15 * i : 0;
            var (tx, ty) = btl.UnitFoot(targets[i]);
            for (int n = 0; n < 70; n++)
            {
                double x = tx + Rand(40) - 20, y = ty + (Rand(20) - 10) * 0.8;
                btl._pieces.Add((5, 0, At(late + Rand(100)), x, y, x, y - 600 * 0.6, 5 * 0.6, 1.05, 1, life, false));
            }
            if (field) btl._bodyShapes.Add((targets[i], 1, At(late), 120, null, 0, 0, 4 / 9.0, null));
        }
    }

    private void SpawnGravityBalance(WorkData w, UnitState user, int col, int row)
    {
        var btl = host.Btl;
        var targets = Targets(w, user, col, row);
        for (int i = 0; i < targets.Count; i++)
        {
            var (tx, ty) = btl.UnitFoot(targets[i]);
            for (int n = 0; n < 100; n++)
            {
                double x = tx + Rand(50) - 25, y = ty + (Rand(40) - 20) * 0.8;
                btl._shots.Add((480, Rand(2) * 2, At(30 + 20 * i + Rand(40)), x, y, x, y - 100 * 0.6, 10 * 0.6, 1, 1, 0, 0, false));
            }
            btl._fxHitAt[targets[i]] = At(64 + 20 * i);
        }
        if (targets.Count > 0) btl._fxLatestStart = Math.Max(btl._fxLatestStart, At(64 + 20 * (targets.Count - 1)));
    }

    private void SpawnMiracle(WorkData w, UnitState user, int col, int row)
    {
        foreach (var (tx, ty) in Feet(w, user, col, row))
            for (int n = 0; n < 30; n++)
            {
                double x = tx + Rand(40) - 20;
                host.Btl._shots.Add((210, Rand(3), At(1 + Rand(100)), x, ty - 150 * 0.6, x, ty, 3 * 0.6, 1, 1, 0, 0, false));
            }
    }

    private void SpawnBlackHole(WorkData w, UnitState user)
    {
        var fx = host.ForcetrulAb;
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        int open = Math.Max(1, host.EffectTicks(278, 1) - 1), hold = host.EffectTicks(278, 2) * 8;
        fx.Fx(w, 278, 1, 0, ux, uy);
        fx.Fx(w, 278, 2, open, ux, uy, hold);
        fx.Fx(w, 278, 3, open + hold, ux, uy);
        for (int i = 0; i < 200; i++)
        {
            var points = new (int X, int Y)[20];
            double a = i * 0.01 * Math.PI;
            for (int t = 0; t < 20; t++, a += 0.04 * Math.PI)
            {
                double r = 300 - 300.0 * t / 20;
                points[t] = ((int)(ux + r * Math.Cos(a)), (int)(uy + r * Math.Sin(a) * 0.8));
            }
            btl._ringTrails.Add((6, 1, At(Rand(130)), points, false));
        }
    }

    private void SpawnBladeShower(WorkData w, UnitState user, int col, int row)
    {
        var fx = host.ForcetrulAb;
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        for (int i = 0; i < 16; i++)
        {
            double a = (i / 8.0 - 0.5) * Math.PI, cos = Math.Cos(a), sin = Math.Sin(a) * 0.8;
            double x = ux + 90 * cos, y = uy + 90 * sin - 100 * 0.6;
            bool mirror = i >= 8;
            int appear = i < 8 ? 9 + i : 25 - i, still = i < 8 ? i : 16 - i, born = 3 * i;
            int shown = Math.Max(1, host.EffectTicks(171, appear) - 1);
            fx.Fx(w, 171, appear, born, x, y, 0, mirror);
            // 나타난 뒤 72틱까지 떠 있고, 거기서 2i 틱 더 있다가 바깥으로 흩어지며 사라진다(모션 +18, 8틱).
            fx.Fx(w, 171, still, born + shown, x, y, Math.Max(1, 72 + 2 * i - born - shown), mirror);
            btl._pieces.Add((171, still + 18, At(72 + 2 * i), x, y, ux + 700 * cos, uy + 700 * sin - 100 * 0.6, 4, 1.4, 1, 8, mirror));
        }
        var targets = Targets(w, user, col, row).Take(16).ToList();
        int top = host._camY, last = 119;
        for (int k = 0; k < targets.Count; k++)
        {
            var (tx, ty) = btl.UnitFoot(targets[k]);
            int begin = 119 + 30 * k + Rand(40);
            fx.Fx(w, 1415, 2, 119 + 30 * k, tx, ty);
            for (int n = 0; n < 9; n++)
            {
                double px = n < 8 ? tx + Rand(160) - 80 : tx, py = n < 8 ? ty + (Rand(160) - 80) * 0.8 : ty;
                double fromX = px - (py - top) * 0.4142;
                int flight = (int)Math.Ceiling(Math.Sqrt((px - fromX) * (px - fromX) + (py - top) * (py - top)) / 70);
                btl._shots.Add((185, 0, At(begin), fromX, top, px, py, 70, 1, 1, 0, 0, false));
                fx.Fx(w, n < 8 ? 110 : 111, 0, begin + flight, px, py);
                if (n == 8) { btl._fxHitAt[targets[k]] = At(begin + flight); last = Math.Max(last, begin + flight); }
            }
        }
        btl._fxLatestStart = Math.Max(btl._fxLatestStart, At(last));
    }
}
