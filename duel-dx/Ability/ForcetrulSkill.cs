using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 포스트럴 어빌리티 가운데 원본 핸들러가 이펙트 자리·개수를 <b>코드로 셈하는</b> 것들(gm-skil-17) — 뽑은 표는 그 자리를 못 읽어
/// 한 곳에 한 장씩 겹쳐 띄웠다. 핸들러를 손으로 읽어 그대로 깐다. 단위는 월드(화면 x 그대로 · y × 0.8 · 높이 × 0.6), 틱은 핸들러 단계 0 부터.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>카운터 스피어 <c>0x10094990</c> — 10틱 뒤 창 348 이 보는 쪽으로 틱당 5 씩 나아가다 모션 길이(9~10틱)에 사라진다(겨눈 칸까지 가지 않는다).</item>
/// <item>카운터 봄 <c>0x10094d30</c> — 시전자 둘레 아홉 점(±120 십자 · ±60 대각 · 가운데)에 8틱 간격으로 번개 기둥(204:1·5 발치, 204:6 높이 425, 204:7 높이 850, 수명 20),
/// 기둥이 끝나면 그 자리에 422:1(수명 50). 판정은 마지막 422:1 이 끝날 때(약 136틱) 범위 안 모두.</item>
/// <item>사이킥 크로스 <c>0x100ab290</c> — 55틱 뒤 겨눈 칸을 지나는 두 대각선의 칸마다(−R~R, R = 범위/2) 5틱 간격으로 917:2~4(난수)와 가운데에 1389:1.
/// 판정은 그 칸의 917 이 뜬 다음 틱에 그 칸만.</item>
/// <item>아스트럴 파이어 <c>0x100aab90</c> — 대상이 사라지고 맞는 자세의 몸이 높이 300 까지 솟아(30 × 0.96) 200틱까지 떠 있다가 틱당 20 으로 내려온다(587:4 가 몸에 붙음).
/// 내려온 뒤(약 218틱)에 대상이 다시 보이고 판정이 든다. 불꽃은 <see cref="BattleScene.SpawnAbilityEffects"/> 가 깐다.</item>
/// <item>엘레맨탈 버스터 <c>0x1008eb90</c> · 아스트럴 블레이드 <c>0x100ab7f0</c> · 파이널 버스터 <c>0x100a0220</c> — 검기 264 는 머리 한 장 + 몸통 타일이 화면 끝까지 깔린다
/// (머리는 앞 200(세로 250) · 높이 50, 몸통 걸음 30(세로 37)). 나타남 3틱 → 유지 55틱 → 사라짐 12틱. 블레이드는 세 줄, 파이널은 늘 네 방향.
/// 65틱 뒤 폭발(179:0 · 블레이드는 206:0)이 앞으로 줄지어 터진다.</item>
/// </list>
/// </remarks>
internal sealed unsafe class ForcetrulSkill(GameWindow host)
{
    internal const int Spear = 77, AstralFire = 79, Buster = 80, Cross = 84, Blade = 88, Bomb = 89, Final = 90, Field = 91, CrazyShot = 126, SunBlast = 168;

    /// <summary>아스트럴 파이어 — 몸이 내려와 닿은 뒤 대상이 다시 보이고 판정이 드는 틱(200 + 300/20 + 알림 3).</summary>
    internal const int AstralFireLand = 218;

    /// <summary>뽑은 표의 줄 가운데 여기서 대신 까는 것 — <see cref="BattleScene.SpawnAbilityEffects"/> 가 건너뛴다.</summary>
    internal static bool Owns(WorkData w, BattleScene.AbilityEffect e) => w.AbilityId switch
    {
        Spear => e.Obs == 348,
        Bomb => e.Obs is 204 or 422 or 1379,
        Cross => (e.Obs == 917 && e.Motion != 0) || (e.Obs == 1389 && e.Motion == 1),
        AstralFire => e.Obs is 587 or 705 || (e.Obs == 1368 && e.Motion == 3),
        Buster => e.Obs is 264 or 179,
        Blade => e.Obs is 264 or 206,
        Final => e.Obs is 264 or 179 or 309,
        CrazyShot => e.Obs == 330,
        SunBlast => e.Obs == 292 || (e.Obs == 1440 && e.Motion == 1),
        _ => false,
    };

    private double At(int tick) => host._lastTime + tick / TicksPerSecond;

    /// <summary>이펙트 한 장(수명이 있으면 그동안 되풀이) + 그 모션의 소리.</summary>
    internal void Fx(WorkData w, int obs, int motion, int tick, double x, double y, int life = 0, bool mirror = false)
    {
        var btl = host.Btl;
        if (life > 0) host.HeavenEarthAb.AddTimedFx(obs, motion, At(tick), (x, y), life, mirror);
        else (mirror ? btl._effectMirrors : btl._effects).Add((obs, motion, At(tick), (int)x, (int)y));
        if (host._abilitySounds.ContainsKey(w.AbilityId)) return;
        if ((host._effectTables.GetValueOrDefault(obs)?.Clips.GetValueOrDefault(motion) ?? host.UiFor(obs)?.Clip(motion)) is not { } clip) return;
        foreach (var (t, sound) in clip.Sounds) host._pendingSounds.Add((At(tick) + t / TicksPerSecond, sound, (float)x));
    }

    private static (int X, int Y) Ahead(Facing f) => f switch { Facing.Up => (0, -1), Facing.Down => (0, 1), Facing.Left => (-1, 0), _ => (1, 0) };

    internal void Spawn(WorkData w, UnitState user, int col, int row)
    {
        switch (w.AbilityId)
        {
            case Spear: SpawnSpear(w, user); break;
            case Bomb: SpawnBomb(w, user, col, row); break;
            case Cross: SpawnCross(w, user, col, row); break;
            case AstralFire: SpawnAstralFire(w, user, col, row); break;
            case Buster: SpawnBuster(w, user, col, row); break;
            case Blade: SpawnBlade(w, user, col, row); break;
            case Final: SpawnFinal(w, user, col, row); break;
            case Field: SpawnField(w, user, col, row); break;
            case CrazyShot: SpawnBulletMarks(w, user); break;
            case SunBlast: SunBlastBurst(w, user, first: true); break;
        }
    }

    private List<UnitState> Targets(WorkData w, UnitState user, int col, int row) =>
        [.. (host.Btl._fxTargets ?? host.Btl.WorkTargets(w, user, col, row)).Select(i => host._units[i])];

    internal void HitAllAt(WorkData w, UnitState user, int col, int row, int tick)
    {
        foreach (var t in Targets(w, user, col, row)) host.Btl._fxHitAt[t] = At(tick);
        host.Btl._fxLatestStart = Math.Max(host.Btl._fxLatestStart, At(tick));
    }

    private void SpawnSpear(WorkData w, UnitState user)
    {
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        var (dx, dy) = Ahead(user.Facing);
        int motion = user.Facing switch { Facing.Up => 0, Facing.Down => 2, _ => 1 };
        // 수명 = 모션 길이 − 1 이라 도착점(두 칸 앞)에 닿기 전에 사라진다 — 틱당 5 로 그 틱만큼만.
        int life = Math.Max(2, (host.UiFor(348)?.MotionLength(motion) ?? 10) - 1), far = 5 * life;
        btl._shots.Add((348, motion, At(10), ux, uy, ux + dx * far, uy + dy * far * 0.8, 5, 1, 1, 0, 0, user.Facing == Facing.Right));
    }

    private void SpawnBomb(WorkData w, UnitState user, int col, int row)
    {
        var (ux, uy) = host.Btl.UnitFoot(user);
        Fx(w, 422, 1, 0, ux, uy);
        (int X, int Y)[] points = [(120, 0), (0, 120), (-120, 0), (0, -120), (60, 60), (60, -60), (-60, 60), (-60, -60), (0, 0)];
        for (int i = 0; i < points.Length; i++)
        {
            double px = ux + points[i].X, py = uy + points[i].Y * 0.8;
            int d = 8 * i;
            Fx(w, 204, 1, d, px, py, 20);
            Fx(w, 204, 5, d, px, py, 20);
            Fx(w, 204, 6, d, px, py - 425 * 0.6, 20);
            Fx(w, 204, 7, d, px, py - 850 * 0.6, 20);
            Fx(w, 422, 1, d + 20, px, py, 50);      // 그 줄기의 204:5 가 끝나면
            Fx(w, 1379, 0, d, ux, uy);              // 소리
        }
        HitAllAt(w, user, col, row, 64 + 20 + 50 + 2);
    }

    private void SpawnCross(WorkData w, UnitState user, int col, int row)
    {
        var btl = host.Btl;
        int cx = col * TileW + TileW / 2, cy = host.CellCenterY(col, row), reach = Math.Max(1, w.AreaArg / 2), n = 0;
        var targets = Targets(w, user, col, row);
        foreach (int slope in new[] { 1, -1 })
            for (int k = -reach; k <= reach; k++, n++)
            {
                int c = col + k, r = row + slope * k, tick = 55 + 5 * n;
                double x = cx + 40 * k, y = (uint)c < host.Cols && (uint)r < host.Rows ? host.CellCenterY(c, r) : cy + slope * k * TileH;
                Fx(w, 917, 2 + btl._fxRandom.Next(3), tick, x, y);
                Fx(w, 1389, 1, tick, cx, cy);
                // 판정은 그 칸의 917 이 뜬 다음 틱 — 가운데 칸은 두 줄에 다 걸리지만 먼저 뜬 쪽에서 한 번만 친다.
                foreach (var t in targets)
                    if (t.Col == c && t.Row == r && !btl._fxHitAt.ContainsKey(t)) btl._fxHitAt[t] = At(tick + 1);
            }
        btl._fxLatestStart = Math.Max(btl._fxLatestStart, At(55 + 5 * (n - 1)));
    }

    private void SpawnAstralFire(WorkData w, UnitState user, int col, int row)
    {
        var btl = host.Btl;
        foreach (var t in Targets(w, user, col, row))
        {
            btl._bodyShapes.Add((t, 9, host._lastTime, 0, null, 0, 0, 1, null));
            btl._fxHitAt[t] = At(AstralFireLand);
        }
        btl._fxLatestStart = Math.Max(btl._fxLatestStart, At(AstralFireLand));
    }

    /// <summary>
    /// 아스트럴 필드 <c>0x10097ba0</c> — 유닛 i 마다 15i 틱 늦게: 떠오름 476:3 50개 · 444:11(70틱 뒤)은 표가 깔고,
    /// 444:11 이 끝나면(130틱 뒤) 그 유닛의 몸이 20틱 늘어났다 돌아오며 그 끝(150틱 뒤)에 효과가 든다.
    /// </summary>
    private void SpawnField(WorkData w, UnitState user, int col, int row)
    {
        var btl = host.Btl;
        var targets = Targets(w, user, col, row);
        for (int i = 0; i < targets.Count; i++)
        {
            btl._bodyShapes.Add((targets[i], 1, At(15 * i + 130), 20, null, 0, 0, 4 / 9.0, null));
            btl._fxHitAt[targets[i]] = At(15 * i + 150);
        }
        if (targets.Count > 0) btl._fxLatestStart = Math.Max(btl._fxLatestStart, At(15 * (targets.Count - 1) + 150));
    }

    /// <summary>
    /// 크레이지 샷 <c>0x10093ce0</c> — 총알 자국 330:0~2 예순 장이 시전자 앞 삼각형(깊이 1~범위 칸, 옆으로는 그 깊이 미만)에 54~143틱 사이 흩어져 박힌다.
    /// 판정 자리와는 따로 뽑는다. 전에는 겨눈 칸과 시전자 발에 한 장씩 떴다.
    /// </summary>
    private void SpawnBulletMarks(WorkData w, UnitState user)
    {
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        var (dx, dy) = Ahead(user.Facing);
        int reach = Math.Max(1, (int)w.AreaArg);
        for (int n = 0; n < 60; n++)
        {
            int depth = btl._fxRandom.Next(reach) + 1, side = btl._fxRandom.Next(depth) * (btl._fxRandom.Next(2) == 0 ? -1 : 1);
            double wx = dx * 40 * depth + (dx == 0 ? 40 * side : 0), wy = dy * 40 * depth + (dx == 0 ? 0 : 40 * side);
            Fx(w, 330, btl._fxRandom.Next(3), btl._fxRandom.Next(90) + 54, ux + wx, uy + wy * 0.8);
        }
    }

    /// <summary>
    /// 선 블래스트 <c>0x100b5ec0</c> 의 한 타 — 빛 조각 292 서른여섯이 시전자 위(높이 200)에서 반지름 600 의 고리로 퍼지고(빠르기 100, 틱마다 × 0.94),
    /// 1440:1 이 시전자에게 뜨고, 화면이 물든다(1타 방식 6 · 한 틱, 그 뒤 방식 3 · 두 틱 — 세기 16). 흔들림은 없다.
    /// </summary>
    internal void SunBlastBurst(WorkData w, UnitState user, bool first)
    {
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        double cy = uy - 200 * 0.6;
        for (int i = 0; i < 36; i++)
        {
            double a = i / 18.0 * Math.PI, cos = Math.Cos(a), sin = Math.Sin(a) * 0.8;
            btl._shots.Add((292, i <= 18 ? i : 36 - i, host._lastTime, ux + 10 * cos, cy + 10 * sin, ux + 600 * cos, cy + 600 * sin, 100, 0.94, 1, 1, 0, i <= 17));
        }
        Fx(w, 1440, 1, 0, ux, uy);
        if (first) btl.PlayTintFlash(6, 16, 1); else btl.PlayTintFlash(3, 16, 2);
    }

    /// <summary>보는 쪽으로 화면 끝까지 깔리는 타일 수(원본 <c>(화면 자리 − 60) / 30</c> — 여기서는 지금 보이는 판의 끝까지).</summary>
    internal int Tiles(UnitState user, Facing dir)
    {
        var (ux, uy) = host.Btl.UnitFoot(user);
        int room = dir switch
        {
            Facing.Up => uy - host._camY,
            Facing.Down => host._camY + host.ViewHeight - uy,
            Facing.Left => ux - host._camX,
            _ => host._camX + host.ViewWidth - ux,
        };
        return Math.Clamp((room - 60) / 30, 1, 40);
    }

    /// <summary>검기 한 줄 — 머리 + 몸통 타일. <paramref name="side"/> 는 줄을 옆으로 민 값(월드), <paramref name="downBody"/> 는 아래쪽 몸통의 나타남 모션.</summary>
    private void Beam(WorkData w, UnitState user, Facing dir, int tick, int side = 0, int downBody = 10)
    {
        var (ux, uy) = host.Btl.UnitFoot(user);
        var (dx, dy) = Ahead(dir);
        bool vertical = dx == 0, mirror = dir == Facing.Right;
        // (머리 나타남 · 유지 · 사라짐, 몸통 나타남 · 유지 · 사라짐)
        var (hA, hB, hC, bA, bB, bC) = dir switch
        {
            Facing.Up => (5, 10, 0, 8, 13, 3),
            Facing.Down => (7, 12, 2, downBody, 13, 3),
            _ => (6, 11, 1, 9, 14, 4),
        };
        double headX = ux + dx * 200 + (vertical ? side : 0), headY = uy + (dy * 250 + (vertical ? 0 : side)) * 0.8 - 50 * 0.6;
        double stepX = dx * 30, stepY = dy * 37.5 * 0.8;
        int tiles = Tiles(user, dir);
        void Row(int head, int body, int at, int life)
        {
            Fx(w, 264, head, at, headX, headY, life, mirror);
            for (int i = 0; i < tiles - 1; i++) Fx(w, 264, body, at, headX + stepX * i, headY + stepY * i, life, mirror);
        }
        Row(hA, bA, tick, 0);
        Row(hB, bB, tick + 3, 55);
        Row(hC, bC, tick + 58, 0);
    }

    /// <summary>검기가 지나간 자리의 폭발 줄 — k 번째는 앞으로 40(세로 50)·k. 마지막 것이 뜨는 틱을 돌려준다.</summary>
    private int Blasts(WorkData w, UnitState user, Facing dir, int tick, int obs, Func<int, int> delay, bool scatter)
    {
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        var (dx, dy) = Ahead(dir);
        int last = tick;
        for (int k = 1; k <= Tiles(user, dir); k++)
        {
            int side = scatter ? 40 * btl._fxRandom.Next(3) - 40 : 0, at = tick + delay(k);
            Fx(w, obs, 0, at, ux + dx * 40 * k + (dx == 0 ? side : 0), uy + (dy * 50 * k + (dx == 0 ? 0 : side)) * 0.8);
            last = Math.Max(last, at);
        }
        return last;
    }

    private void SpawnBuster(WorkData w, UnitState user, int col, int row)
    {
        Beam(w, user, user.Facing, 55);
        int last = Blasts(w, user, user.Facing, 120, 179, _ => host.Btl._fxRandom.Next(10), scatter: false);
        HitAllAt(w, user, col, row, last + 23);
    }

    private void SpawnBlade(WorkData w, UnitState user, int col, int row)
    {
        // 세 줄 — 옆 간격은 30, 오른쪽을 볼 때만 40(0x100ac931, 원본이 그렇다).
        for (int line = -1; line <= 1; line++) Beam(w, user, user.Facing, 56, line * (user.Facing == Facing.Right ? 40 : 30), downBody: 8);
        int last = Blasts(w, user, user.Facing, 121, 206, _ => host.Btl._fxRandom.Next(10), scatter: true);
        HitAllAt(w, user, col, row, last + 11);
    }

    private void SpawnFinal(WorkData w, UnitState user, int col, int row)
    {
        var (ux, uy) = host.Btl.UnitFoot(user);
        int first = int.MaxValue;
        foreach (var dir in new[] { Facing.Up, Facing.Left, Facing.Down, Facing.Right })
        {
            var (dx, dy) = Ahead(dir);
            double px = ux + dx * 40, py = uy + dy * 40 * 0.8 - 50 * 0.6;
            Fx(w, 309, 0, 25, px, py);
            Fx(w, 309, 1, 73, px, py, 40);
            Fx(w, 309, 2, 113, px, py);
            Beam(w, user, dir, 55);
            first = Math.Min(first, Blasts(w, user, dir, 120, 179, k => 3 * k, scatter: false));
        }
        HitAllAt(w, user, col, row, first + 23);      // 가장 먼저 끝나는 줄에서 모두 판정
    }
}
