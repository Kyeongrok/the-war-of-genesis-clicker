using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 아크로스트 어빌리티 가운데 원본 핸들러가 이펙트 자리·개수·때를 <b>코드로 셈하는</b> 것들(gm-skil-22). 단위는 월드(화면 x 그대로 · y × 0.8 · 높이 × 0.6),
/// 틱은 핸들러 단계 0 부터. 엘레맨탈 파이어 · 서몬 몬스터는 <see cref="AcrostSkill"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>블리자드 <c>0x1008b630</c> — 뿌리개가 끝난 뒤(87틱) 유닛 i 가 34i 틱마다 차례로 언다: 발의 258:11 과 높이 270(· 450 · 630)의 258:5 · 8 · 1 이 나타나(8틱) 붙들렸다가(모션 +1)
/// 34N + 40 + 3(i + 1) 틱에 깨진다(모션 +2). 판정은 41N + 10 틱 뒤 범위 안 모두.</item>
/// <item>엘레맨탈 아이스 <c>0x1009efb0</c> — 시전자 발에서 14:0 · 14:1 마흔 개씩이 솟고, 127틱부터 10틱마다 얼음 218:3 이 범위 안 무작위 칸에 떨어진다
/// (높이 600 에서 40 × 1.15, 꼬리 262:1). 닿으면 179:0 과 떨굼 조각 14 열 개, 179:0 이 끝날 때 <b>그 칸만</b> 판정.</item>
/// <item>화이어 웨이브 <c>0x10084630</c> — 184:0 이 끝나면(약 78틱) 불길 줄 2R − 1 개가 V 꼴로 앞으로 내달리며(틱당 80) 지나는 칸에 불기둥 둘씩(180~182)을 남긴다.
/// 12틱 뒤 카메라가 앞쪽으로(가로 280 · 세로 160), 그 110틱 뒤 판정.</item>
/// <item>포이즌 <c>0x1009e1e0</c> — 60틱 뒤 마름모 칸마다 511:0 이 가운데에서 바깥으로(칸 거리 × 25 + 0~29틱) 번진다. 판정은 가장 늦은 것이 끝날 때.</item>
/// <item>엘레맨탈 베이스 <c>0x100a37d0</c> — 바위 251 서른 개가 둘레에서 높이 120 까지 떠올라 부서지고, 대상 i 마다 70 + 15i 틱에 110:0, 17틱 뒤 972, 그것이 끝날 때 판정.</item>
/// <item>하드 · 소프트 밸런싱 <c>0x100b7d20</c> · <c>0x100b8110</c> — 대상 둘레 반지름 25 의 나선(리미트 크래쉬와 같은 클래스 0x100c56e0): 하드는 발에서 위로, 소프트는 높이 120 에서 아래로.</item>
/// <item>엘레맨탈 다크 <c>0x100a3340</c> — 대상마다 793:0 스무 개가 반지름 25 원 윤곽 위에 고르게.</item>
/// <item>기가실드 <c>0x10097280</c> — 대상 i 마다 444:14 가 끝나면(97 + 15i) 몸이 20틱 늘어났다 돌아오고 그 끝에 판정.</item>
/// <item>리인카네이션 <c>0x1008d940</c> — 쫓는 그림이 사라진 뒤에야 279:1(높이 50) → 279:2 가 내려온다.</item>
/// </list>
/// 썬더 스톰의 번개 줄기 개체(0x100c89c0 · 0x100c9c40)와 워핑의 끈 개체(0x100ca210)는 그리는 코드를 못 읽어 안 넣었다.
/// </remarks>
internal sealed unsafe class ElementalSkill(GameWindow host)
{
    internal const int Ice = 56, Reincarnation = 59, Dark = 64, HardBalance = 65, SoftBalance = 66, Base = 67, Poison = 70, GigaShield = 71,
                       FireWave = 72, ThunderStorm = 75, Blizzard = 76;

    /// <summary>블리자드 단계 1 이 시작하는 틱 — 30 + 셋째 뿌리개(지연 6)의 낱알 수명(262:1 = 51틱). 낱알 수명을 뿌리개의 끝으로 본 것은 가설.</summary>
    internal const int BlizzardFreeze = 87;

    /// <summary>엘레맨탈 아이스 — 얼음이 떨어진 칸만 판정한다(0x1009f581, ba-20 D6). false 면 연출만 따르고 범위 안 모두를 마지막 얼음 뒤에 판정한다.</summary>
    internal const bool IceHitsStruckCellsOnly = true;

    /// <summary>리인카네이션 — 쫓는 그림 279:4 가 사라지는 틱(61 + 아홉 점 약 97틱)과 그 뒤 밀어내기까지(279:1 뒤 6틱).</summary>
    internal const int ReincarnationSettle = 159, ReincarnationPush = ReincarnationSettle + 6;

    internal static bool Owns(WorkData w, BattleScene.AbilityEffect e) => w.AbilityId switch
    {
        Blizzard => e.Obs == 258 || (e.Obs == 1361 && e.Motion != 0),
        Ice => e.Obs is 14 or 218 or 179 || (e.Obs == 1353 && e.Motion == 1),
        FireWave => e.Obs == 184 || (e.Obs == 183 && e.Motion == 1) || (e.Obs == 383 && e.Motion != 0),
        Poison => e.Obs == 511 && e.Motion == 0,
        Base => e.Obs is 251 or 972 or 110 or 390,
        HardBalance => (e.Obs == 472 && e.Motion == 3) || (e.Obs == 5 && e.Motion == 1),
        SoftBalance => (e.Obs == 478 && e.Motion == 3) || (e.Obs == 670 && e.Motion == 1),
        ThunderStorm => e.Obs == 218 && e.Motion == 2,
        Dark => e.Obs == 793,
        GigaShield => e.Obs == 1324 && e.Motion == 0,
        Reincarnation => e.Obs == 279 && e.Motion == 2,
        _ => false,
    };

    /// <summary>표의 줄을 손보는 것 — 리인카네이션 279:1 은 시전자 위 50 에 뜬다(0x1008d940 단계 1).</summary>
    internal static BattleScene.AbilityEffect Adjust(WorkData w, BattleScene.AbilityEffect e) =>
        w.AbilityId == Reincarnation && e.Obs == 279 && e.Motion == 1 ? e with { Lift = 40 } : e;

    /// <summary>표의 지연에 더할 틱 — 리인카네이션 279:1 은 쫓는 그림이 사라진 다음 틱.</summary>
    internal static int Late(WorkData w, BattleScene.AbilityEffect e) =>
        w.AbilityId == Reincarnation && e.Obs == 279 && e.Motion == 1 ? ReincarnationSettle - e.Delay : 0;

    private FxTimeline Line => host.FxLine;
    private double At(int tick) => Line.At(tick);
    private int Rand(int n) => Line.Rand(n);
    private static (int X, int Y) Ahead(Facing f) => f switch { Facing.Up => (0, -1), Facing.Down => (0, 1), Facing.Left => (-1, 0), _ => (1, 0) };

    /// <summary>범위의 가운데(화면 자리) — 자기 자리에 쓰는 work 면 시전자 발, 아니면 겨눈 칸.</summary>
    private (int X, int Y) Centre(WorkData w, UnitState user, int col, int row) =>
        w.SelfCentred || (uint)col >= host.Cols || (uint)row >= host.Rows ? host.Btl.UnitFoot(user) : (col * TileW + TileW / 2, host.CellCenterY(col, row));

    internal void Spawn(WorkData w, UnitState user, int col, int row)
    {
        switch (w.AbilityId)
        {
            case Blizzard: SpawnBlizzard(w, user, col, row); break;
            case Ice: SpawnIce(w, user, col, row); break;
            case FireWave: SpawnFireWave(w, user, col, row); break;
            case Poison: SpawnPoison(w, user, col, row); break;
            case Base: SpawnBase(w, user, col, row); break;
            case HardBalance: Spirals(w, user, col, row, rise: true, 472, 3, 40, 5, 1, 80); break;
            case SoftBalance: Spirals(w, user, col, row, rise: false, 478, 3, 70, 670, 1, 70); break;
            case ThunderStorm:
            {
                var (ux, uy) = host.Btl.UnitFoot(user);
                Line.Rings(218, 2, ux, uy - 150 * 0.6, 400, 0);
                break;
            }
            case Dark: SpawnDark(w, user, col, row); break;
            case GigaShield: SpawnGigaShield(w, user, col, row); break;
            case Reincarnation:
            {
                // 279:2 — 279:1(26틱)이 끝나면 시전자 위 50 에서 발까지 내려온다(직선 1 × 1.08).
                var (ux, uy) = host.Btl.UnitFoot(user);
                int at = ReincarnationSettle + Math.Max(1, host.EffectTicks(279, 1));
                host.Btl._shots.Add((279, 2, At(at), ux, uy - 40, ux, uy, 0.8, 1.08, 1, 0, 0, false));
                host.Btl._fxLatestStart = Math.Max(host.Btl._fxLatestStart, At(at));
                break;
            }
        }
    }

    private void SpawnBlizzard(WorkData w, UnitState user, int col, int row)
    {
        var fx = host.ForcetrulAb;
        var btl = host.Btl;
        var targets = Line.Targets(w, user, col, row);
        var shakes = host.HeavenEarthAb._shakes;
        int n = targets.Count;
        for (int i = 0; i < n; i++)
        {
            var t = targets[i];
            var (tx, ty) = btl.UnitFoot(t);
            int freeze = BlizzardFreeze + 34 * i, burst = BlizzardFreeze + 34 * n + 40 + 3 * (i + 1);
            fx.Fx(w, 1361, 1, freeze, tx, ty);
            fx.Fx(w, 1361, 3, burst, tx, ty);
            // 얼음 한 조각 — 나타남(모션) → 붙듦(+1) → 깨짐(+2, 자식 261:0 파편).
            void Piece(int motion, int at, int lift)
            {
                int shown = Math.Max(1, host.EffectTicks(258, motion) - 1), held = Math.Max(at + shown, burst);
                Line.Put(258, motion, at, tx, ty - lift);
                if (held > at + shown) Line.Put(258, motion + 1, at + shown, tx, ty - lift, held - at - shown);
                Line.Put(258, motion + 2, held, tx, ty - lift);
            }
            Piece(11, freeze, 0);
            // 위쪽 단(높이 450 · 630)은 유닛이 화면 위쪽 끝에서 160 · 270 넘게 내려와 있을 때만(0x1008bbd1 · 0x1008bd6b).
            int room = ty - host._camY;
            for (int level = 0; level < 3 && (level == 0 || room >= (level == 1 ? 160 : 270)); level++)
                for (int k = 0; k < 3; k++)
                    Piece(k switch { 0 => 5, 1 => 8, _ => 1 }, freeze + 3 + 9 * level + 3 * k, (int)((270 + 180 * level) * 0.6));
            shakes.Add((At(freeze), At(freeze + 8), 3, false));
            // 다 얼면 깨질 때까지 동작 6 으로 굳는다(0x100d01d0 — SetAction(6, 방향, 100)).
            Line.Later(freeze + 8, () => { if (t.Alive && t.Hp > 0) t.PlayAction(6, (burst - freeze - 8) / TicksPerSecond); });
        }
        if (n == 0) return;
        int quake = BlizzardFreeze + 37 * n - 10;
        fx.Fx(w, 1361, 2, quake, btl.UnitFoot(user).X, btl.UnitFoot(user).Y);
        shakes.Add((At(quake), At(quake + 10), 1, false));
        shakes.Add((At(quake + 10), At(quake + 10 + 2 * n), 5, false));
        fx.HitAllAt(w, user, col, row, quake + 4 * n + 20);
    }

    private void SpawnIce(WorkData w, UnitState user, int col, int row)
    {
        var fx = host.ForcetrulAb;
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        for (int k = 0; k < 40; k++)
            foreach (var (motion, top) in new[] { (0, 1000), (1, 600) })
            {
                double x = ux + Rand(30) - 15, y = uy + (Rand(20) - 5) * 0.8;
                btl._shots.Add((14, motion, At(Rand(60)), x, y, x, y - top * 0.6, 5 * 0.6, 1.1, 1, 0, 80 * 0.6, false));
            }
        // 얼음 수 = (Lv − 1) / 4 + 6(Lv20 은 하나 더) — 범위 칸에서 겹치지 않게 뽑는다(판정 자리라 전투 난수를 쓴다).
        int level = Math.Max(1, user.Data?.AbilityLevel(Ice) ?? 1);
        var cells = btl.AreaCells(w, user, col, row).Distinct().ToList();
        var struck = new List<(int Col, int Row)>();
        for (int n = (level - 1) / 4 + 6 + (level >= 20 ? 1 : 0); n > 0 && cells.Count > 0; n--)
        {
            int pick = host._rng.Next(cells.Count);
            struck.Add(cells[pick]);
            cells.RemoveAt(pick);
        }
        var targets = Line.Targets(w, user, col, row);
        int last = 127;
        for (int i = 0; i < struck.Count; i++)
        {
            var (c, r) = struck[i];
            int x = c * TileW + TileW / 2, y = host.CellCenterY(c, r), at = 127 + 10 * i;
            // 떨어지는 길 — 틱마다의 자리에 꼬리 262:1 을 하나씩 남긴다(0x100c3b80).
            var tail = new List<(int X, int Y)>();
            double gone = 0, speed = 40;
            while (gone < 600 && tail.Count < 60) { tail.Add((x, (int)(y - (600 - gone) * 0.6))); gone += speed; speed = Math.Min(90, speed * 1.15); }
            int land = at + tail.Count;
            btl._shots.Add((218, 3, At(at), x, y - 600 * 0.6, x, y, 40 * 0.6, 1.15, 1, 0, 90 * 0.6, false));
            btl._ringTrails.Add((262, 1, At(at), [.. tail], false));
            fx.Fx(w, 1353, 1, at, x, y);
            fx.Fx(w, 179, 0, land, x, y);
            for (int k = 0; k < 10; k++)
            {
                int motion = Rand(2);
                btl._fallers.Add((14, motion, At(land), x, y, 0, Rand(40) - 20, Rand(40) - 20, Rand(40) + 20, 0.2, Math.Max(1, host.EffectTicks(14, motion)) * 4, false));
            }
            int hit = land + Math.Max(1, host.EffectTicks(179, 0));
            foreach (var t in targets)
                if (t.Col == c && t.Row == r) btl._fxHitAt[t] = At(hit);
            last = Math.Max(last, hit);
        }
        if (IceHitsStruckCellsOnly)
        {
            foreach (var t in targets)
                if (!btl._fxHitAt.ContainsKey(t)) btl._fxUntouched.Add(t);
            btl._fxLatestStart = Math.Max(btl._fxLatestStart, At(last));
        }
        else fx.HitAllAt(w, user, col, row, last);
    }

    private void SpawnFireWave(WorkData w, UnitState user, int col, int row)
    {
        var fx = host.ForcetrulAb;
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        // 단계 1 — 고리 넷(20틱)이 끝난 뒤 25틱: 383:1 + 184:0. 단계 2 — 184:0 이 끝나면 불길 줄.
        const int Flare = 46;
        int wave = Flare + Math.Max(1, host.EffectTicks(184, 0)) + 1, reach = Math.Max(1, (int)w.AreaArg);
        fx.Fx(w, 383, 1, Flare, ux, uy);
        fx.Fx(w, 184, 0, Flare, ux, uy);
        fx.Fx(w, 383, 2, wave, ux, uy);
        var (dx, dy) = Ahead(user.Facing);
        var (sx, sy) = (dy != 0 ? 1 : 0, dx != 0 ? 1 : 0);      // 줄이 늘어서는 옆 축
        double speed = dx != 0 ? 80 : 80 * 0.8, far = 40 * reach + 240;
        void Pillar(int obs, double x, double y, int at, int times)
        {
            int life = Math.Max(1, host.EffectTicks(obs, 0)) * times;
            Line.Put(obs, 0, at, x, y, life);
            Line.Put(obs, 1, at + life, x, y);
        }
        for (int i = 1 - reach; i < reach; i++)
        {
            int a = Math.Abs(i), marks = (reach - a) / 2;
            double x0 = ux + sx * 40 * i + dx * 40 * a, y0 = uy + (sy * 40 * i + dy * 40 * a) * 0.8;
            double x1 = ux + sx * 40 * i + dx * far, y1 = uy + (sy * 40 * i + dy * far) * 0.8;
            btl._shots.Add((184, 1, At(wave + a), x0, y0, x1, y1, speed, 1, 1, 0, 0, false));
            btl._shots.Add((183, 1, At(wave + a), x0 + dx * 40, y0 + dy * 40 * 0.8, x1, y1, speed, 1, 1, 0, 0, false));
            // 줄이 새 칸에 들어설 때마다(남길 수까지) 그 칸 땅에 불기둥 둘 — 틱당 두 칸을 지난다.
            for (int k = 1; k <= marks; k++)
            {
                double px = x0 + dx * 40 * k, py = y0 + dy * 40 * k * 0.8;
                int at = wave + a + k / 2;
                Pillar(180 + Rand(2), px - 10, py, at + Rand(8), Rand(2) + 2);
                Pillar(180 + Rand(3), px + 10, py, at + Rand(30) + 10, 3);
            }
        }
        // 단계 3(12틱 뒤) — 카메라가 시전자 앞쪽으로(안 기다린다), 단계 4(110틱 뒤) — 범위 안 모두 판정.
        Line.Later(wave + 13, () =>
        {
            var (gx, gy) = btl.AheadPoint(user, 280, 160);
            btl.CenterOn(gx, gy, 10);
        });
        fx.HitAllAt(w, user, col, row, wave + 13 + 110);
    }

    private void SpawnPoison(WorkData w, UnitState user, int col, int row)
    {
        var (cc, cr) = w.SelfCentred ? (user.Col, user.Row) : (col, row);
        int reach = Math.Max(1, (int)w.AreaArg), last = 0;
        // 단계 1 은 511:2(59틱)가 끝난 뒤 — 마름모 칸마다 하나, 가운데에서 멀수록 늦게.
        int begin = Math.Max(1, host.EffectTicks(511, 2)) + 1;
        for (int dr = -reach; dr <= reach; dr++)
            for (int dc = -reach; dc <= reach; dc++)
            {
                int far = Math.Abs(dc) + Math.Abs(dr), c = cc + dc, r = cr + dr;
                if (far > reach || (uint)c >= host.Cols || (uint)r >= host.Rows) continue;
                int at = begin + Rand(30) + 25 * far;
                Line.Put(511, 0, at, c * TileW + TileW / 2, host.CellCenterY(c, r));
                last = Math.Max(last, at);
            }
        host.ForcetrulAb.HitAllAt(w, user, col, row, last + Math.Max(1, host.EffectTicks(511, 0)));
    }

    private void SpawnBase(WorkData w, UnitState user, int col, int row)
    {
        var fx = host.ForcetrulAb;
        var btl = host.Btl;
        var (cx, cy) = Centre(w, user, col, row);
        var (ux, uy) = btl.UnitFoot(user);
        int climb = BattleScene.ShotTicks(120 * 0.6, 14 * 0.6, 0.85, 1, 0.6, 0);
        for (int n = 0; n < 30; n++)
        {
            int motion = Rand(4), at = Rand(100);
            double x = cx + Rand(560) - 280, y = cy + (Rand(440) - 200) * 0.8;
            btl._shots.Add((251, motion, At(at), x, y, x, y - 120 * 0.6, 14 * 0.6, 0.85, 1, 0.6, 0, false));
            Line.Put(251, motion + 6, at + climb, x, y - 120 * 0.6);       // 다 오르면 부서진다
        }
        var targets = Line.Targets(w, user, col, row);
        for (int i = 0; i < targets.Count; i++)
        {
            var (tx, ty) = btl.UnitFoot(targets[i]);
            int at = 70 + 15 * i;
            fx.Fx(w, 110, 0, at, tx, ty, 0, Rand(2) == 0);
            fx.Fx(w, 390, 1, at, ux, uy);
            fx.Fx(w, 390, 1, at + 17, ux, uy);
            int burst = Rand(3);
            fx.Fx(w, 972, burst, at + 17, tx, ty);
            btl._fxHitAt[targets[i]] = At(at + 17 + Math.Max(1, host.EffectTicks(972, burst)));
        }
        btl._fxLatestStart = Math.Max(btl._fxLatestStart, At(100 + climb));
        if (targets.Count > 0) btl._fxLatestStart = Math.Max(btl._fxLatestStart, At(70 + 15 * (targets.Count - 1) + 55));
    }

    /// <summary>
    /// 밸런싱의 나선(0x100c57d0(가운데, 25, 처음 각 rand, 빠르기, dz, 틱 수)) — 원 윤곽(점 8r 개)을 틱당 8 · 9점씩 돌며 틱당 2.0~2.9 씩 뜨거나 내린다
    /// (높이 120 을 가는 틱 수, 조각마다 0~79틱 늦게).
    /// </summary>
    private void Spirals(WorkData w, UnitState user, int col, int row, bool rise, int obsA, int motionA, int countA, int obsB, int motionB, int countB)
    {
        var btl = host.Btl;
        var feet = Line.Targets(w, user, col, row).Select(btl.UnitFoot).ToList();
        if (feet.Count == 0) feet.Add(Centre(w, user, col, row));
        foreach (var (tx, ty) in feet)
            foreach (var (obs, motion, count, step) in new[] { (obsA, motionA, countA, 8), (obsB, motionB, countB, 9) })
                for (int n = 0; n < count; n++)
                {
                    double dz = 2.0 + 0.1 * Rand(10);
                    btl._movers.Add((obs, motion, At(Rand(80)), 6, tx, rise ? ty : ty - 120 * 0.6, 0, 0, (int)(120 / dz), 25, rise ? dz : -dz,
                                     btl._fxRandom.NextDouble() * 2 * Math.PI, 2 * Math.PI * step / (8 * 25), false));
                }
    }

    private void SpawnDark(WorkData w, UnitState user, int col, int row)
    {
        var ring = BattleScene.CirclePoints(25);
        var targets = Line.Targets(w, user, col, row);
        for (int i = 0; i < targets.Count; i++)
        {
            var (tx, ty) = host.Btl.UnitFoot(targets[i]);
            for (int k = 0; k < 20; k++)
            {
                var (px, py) = ring[k * ring.Count / 20];
                Line.Put(793, 0, 50 + 15 * i, tx + px, ty + py * 0.8);
            }
        }
    }

    private void SpawnGigaShield(WorkData w, UnitState user, int col, int row)
    {
        var btl = host.Btl;
        var targets = Line.Targets(w, user, col, row);
        for (int i = 0; i < targets.Count; i++)
        {
            var (tx, ty) = btl.UnitFoot(targets[i]);
            int at = 15 * i + 70 + Math.Max(1, host.EffectTicks(444, 14) - 1);
            host.ForcetrulAb.Fx(w, 1324, 0, at, tx, ty);
            btl._bodyShapes.Add((targets[i], 1, At(at), 20, null, 0, 0, 4 / 9.0, null));
            btl._fxHitAt[targets[i]] = At(at + 20);
            btl._fxLatestStart = Math.Max(btl._fxLatestStart, At(at + 20));
        }
    }
}
