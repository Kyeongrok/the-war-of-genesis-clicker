using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 총 · 기계 · 그 밖의 기술 — 권총연사 · 3연사 · 9연사 · 6연사 · 저격 · 댓쉬 · 워프 · 도발 · 집중 · 기도 · 희생 · 해체대법 · 강탈 ·
/// 최면 · 혼란 · 컨디션이상, 기계(미사일 · 포격 · 자주포 · 폭격 · 공폭탄 · 레이저 캐논 · 사이드 슬레이더), 용병 총 기술. 볼트 분석-파트1-기술-b 10절.
/// </summary>
/// <remarks>
/// <para>원본 핸들러는 Attack 번호마다 하나(<see cref="IsGunSkill"/> 의 표). 앞머리는 Attack +0x69 — 2(검 기술과 같은 <see cref="WeaponPrep"/>) ·
/// 5(<c>0x10044380</c>, 35틱 — <see cref="GunPrep"/>) · 0(앞머리 없음, 다음 틱에 핸들러).</para>
/// <para>여기서는 그림을 깔고 시전자 동작을 걸고 <c>hit</c> 를 대상마다 한 번 부른다(피해 · 상태는 전투 쪽이 Attack.dat 로 낸다).
/// 총 기술은 원본이 탄 자국(Obs 183)마다 맞히지만(<c>0x100768f0</c>) 타 수는 Attack +0x66 이 세므로 첫 자국에서 부른다.</para>
/// <para>판 상태를 바꾸는 것(댓쉬 · 워프의 자리 옮김, 도발 SP −20, 집중 +0x25c = 40, 희생 · 해체대법의 HP · SP, 기도의 덧기술, 최면 · 혼란 · 컨디션이상의 상태)은
/// 전투 쪽 일이다 — 여기서는 시전자를 그 칸에 붙들어 두기만 한다(혼 · 연과 같다). 원본 수치는 볼트 노트.</para>
/// </remarks>
internal sealed partial class Part1Skill
{
    /// <summary>이 파일이 그리는 파트1 Attack 번호인가.</summary>
    internal static bool IsGunSkill(int attackId) => attackId is 6 or 7 or 8 or 408 or 365 or 379 or 366 or 367 or 382 or 378 or 375 or 376 or 377
        or 374 or 384 or 385 or 386 or 281 or 362 or 387 or 394 or 395 or 396 or (>= 400 and <= 402)
        or (>= 309 and <= 317) or (>= 339 and <= 344) or (>= 355 and <= 357);

    /// <summary>
    /// 기술 하나 — Attack 번호로 나눠 시각표를 깔고 끝나는 틱을 돌려준다. <paramref name="aim"/> 은 겨눈 칸의 발 자리(판 픽셀),
    /// <paramref name="cells"/> 는 범위 칸(기계 포격이 칸마다 터진다 — 비면 대상들의 칸).
    /// </summary>
    private int Gun(int attackId, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit) => attackId switch
    {
        6 or 7 or 8 or 408 or 365 or (>= 309 and <= 314) or (>= 339 and <= 341) or (>= 355 and <= 357) or 379 => GunVolley(attackId, targets, aim, hit),
        >= 342 and <= 344 => GunBazooka(targets, aim, hit),
        366 => GunDash(targets, aim),
        367 => GunWarp(targets, aim),
        382 => GunTaunt(targets, hit),
        378 => GunFocus(targets, aim),
        375 => GunPray(targets, aim),
        376 => GunSacrifice(targets, aim, hit),
        377 => GunDismantle(targets, hit),
        374 => GunSteal(targets, aim, hit),
        384 or 385 or 386 => GunStatus(attackId, targets, aim, hit),
        281 or 396 => GunMissile(attackId, targets, cells, aim, hit),
        362 or 395 => GunShell(attackId, targets, cells, aim, hit),
        387 => GunAirBomb(targets, cells, aim, hit),
        394 or (>= 315 and <= 317) or (>= 400 and <= 402) => GunBeam(attackId, targets, aim, hit),
        _ => 0,
    };

    /// <summary>그 기술이 쓰는 파트1 그림(앞머리 · 자식 그림까지) — 파트1 이 없는 PC 에서 쓰기 전에 받아 둔다.</summary>
    private static int[] GunNeeds(int attackId)
    {
        int[] prep = Attack69(attackId) switch { 2 => [184], 5 => [95], _ => [] };
        int[] blast = [123, 10, 48, 34, 38, 366];
        int[] own = attackId switch
        {
            >= 342 and <= 344 => [10],
            366 => [230, 174],
            367 => [],
            382 => [782],
            378 => [56, 54, 57, 58, 63],
            375 => [194],
            376 => [55, 194, 52, 65],
            377 => [52, 20],
            374 => [],
            384 => [783],
            385 => [795],
            386 => [780],
            281 or 396 => [281, 284, .. blast],
            362 or 395 => blast,
            387 => [283, .. blast],
            394 or (>= 315 and <= 317) => [505],
            >= 400 and <= 402 => [250],
            _ => [183],
        };
        return [.. prep.Concat(own).Distinct()];
    }

    /// <summary>Attack +0x69(앞머리 종류) — 이 파일의 기술들만(<c>p1att.py</c>).</summary>
    private static int Attack69(int attackId) => attackId switch
    {
        6 or 7 or 365 or (>= 400 and <= 402) => 2,
        379 or 366 or 367 or 382 or 378 or 375 or 376 or 377 or 374 or 384 or 385 or 386 => 5,
        _ => 0,
    };

    /// <summary>
    /// 앞머리 — 종류 2 는 검 기술 것(24틱), 종류 0 은 없음(다음 틱), 종류 5(<c>0x10044380</c>)는 시전자 z+60 에 Obs 95:0(34틱, 소리 82),
    /// 카메라를 시전자에, 대상 쪽을 보고 파트1 동작 10 을 34틱 붙든 뒤 한 번 더 걸고 다음 틱에 핸들러(35틱).
    /// </summary>
    private int GunPrep(int attackId, UnitState user, (double X, double Y) aim)
    {
        switch (Attack69(attackId))
        {
            case 2: return WeaponPrep(user, aim);
            case 5:
                var (hx, hy) = _home;
                Put(95, 0, 0, hx, hy - 60 * 0.6, sounds: true);
                host.Btl.CenterOnUnit(user);
                if (aim != _home) user.Facing = FacingOf(aim.X - hx, aim.Y - hy);
                // 파트1 동작 10 은 이 게임 동작표에 없다 — 공격 준비(5)로 붙든다.
                user.PlayAction(5, 35 / TicksPerSecond);
                _afterPrep = (9, 5, true);   // 34틱에 동작 10 을 다시 걸고 잠근다(0x100444b6..0x100444d7) — 10 이 없어 공격 준비로
                return 35;
            default: return 1;
        }
    }

    // ---- 총 ----

    /// <summary>
    /// 연사(권총연사 6 · 3연사 7 · 9연사 8 · 6연사 365 · 저격 379 · 용병 2연사 · 3연사 · 기관총 · 쌍권총) — 시전자 동작과 탄 자국의 늦춤(틱).
    /// 핸들러 <c>0x10044d50</c> · <c>0x10045010</c> · <c>0x100452e0</c> · <c>0x1006b080</c> · <c>0x1006cdf0</c> · <c>0x10061f00</c> · <c>0x10062210</c> ·
    /// <c>0x10063df0</c> · <c>0x10068fb0</c>.
    /// </summary>
    private static (int Action, int[] Delays) GunVolleyPlan(int attackId) => attackId switch
    {
        6 => (11, [20, 29]),
        7 => (12, [20, 29, 38]),
        8 or 408 => (16, [.. Enumerable.Range(0, 9).Select(i => 32 + 5 * i)]),
        365 => (15, [.. Enumerable.Range(0, 6).Select(i => 30 + 8 * i)]),
        379 => (2, [20]),
        >= 309 and <= 311 => (2, [20, 29]),
        >= 312 and <= 314 => (2, [20, 29, 38]),
        >= 339 and <= 341 => (2, [.. Enumerable.Range(0, 9).Select(i => 20 + 5 * i)]),
        _ => (2, [30, 36]),
    };

    /// <summary>
    /// 연사 — 시전자가 대상 쪽을 보고 쏘는 동작(파트1 동작 11 · 12 · 15 · 16, 용병 · 저격은 2)을 한 번 하고, 늦춤마다 탄 자국 Obs 183(모션 0~2 중 하나, 3틱)이
    /// 대상에 붙어 선다(대상이 없으면 칸 안 아무 데나). 자국마다 맞히지만(<c>0x100768f0(1, 서술, 3, 1, 3, 0)</c>) 여기서는 첫 자국 한 틱 뒤에 한 번 부른다.
    /// 카메라는 핸들러 때 시전자에, 첫 자국이 서는 틱에 대상에(<c>+0xb4 = 1</c>).
    /// </summary>
    private int GunVolley(int attackId, List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (action, delays) = GunVolleyPlan(attackId);
        var t = TargetPoint(targets, aim);
        user.Facing = FacingOf(t.X - _home.X, t.Y - _home.Y);
        host.Btl.CenterOnUnit(user);
        int last = Swing(user, action, 9);
        foreach (var (ti, (x, y)) in Points(targets, aim))
        {
            foreach (int d in delays)
            {
                // 대상이 없으면 칸 귀퉁이에서 0~39px(월드) 안의 아무 점.
                var (px, py) = ti >= 0 ? (x, y) : (x - 20 + Rand(40), y + (Rand(40) - 20) * 0.8);
                Put(183, Rand(3), d, px, py, sounds: true);
            }
            if (ti >= 0) HitAt(delays[0] + 1, ti, hit);
        }
        Later(delays[0], () => host.Btl.CenterOn(t.X, t.Y));
        return Math.Max(last, delays[^1] + Length(183, 0)) + 1;
    }

    /// <summary>용병 바즈카(342~344, <c>0x100640f0</c>) — 시전자 동작 2, 20틱 뒤 칸 안 아무 점에 폭발 Obs 10:0(46틱, 소리 8), 한 틱 뒤 친다.</summary>
    private int GunBazooka(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var t = TargetPoint(targets, aim);
        user.Facing = FacingOf(t.X - _home.X, t.Y - _home.Y);
        host.Btl.CenterOnUnit(user);
        int last = Swing(user, 2, 9);
        foreach (var (ti, (x, y)) in Points(targets, aim))
        {
            Put(10, 0, 20, x - 20 + Rand(40), y + (Rand(40) - 20) * 0.8, sounds: true);
            if (ti >= 0) HitAt(21, ti, hit);
        }
        Later(20, () => host.Btl.CenterOn(t.X, t.Y));
        return Math.Max(last, 20 + Length(10, 0)) + 1;
    }

    // ---- 움직임 ----

    /// <summary>
    /// 댓쉬(366, <c>0x1006b340</c>) — 소리 1879, 시전자 발치에 먼지 바람 230:2, 시전자가 걷는 동작(파트1 동작 1)을 이어 하며 겨눈 칸까지 빠르기 30(× 1.0)으로 미끄러진다
    /// (<c>0x100816d0</c>, 시전자를 옮기는 운반자). 닿은 뒤 떠난 자리에서 먼지 174:3~5 열 개가 0~9틱 늦게 솟는다(빠르기 5 × 0.9). 맞히는 것은 없다 — 옮기기만 하는 기술.
    /// </summary>
    private int GunDash(List<int> targets, (double X, double Y) aim)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = aim != _home ? aim : TargetPoint(targets, aim);
        user.Facing = FacingOf4(Dir4(t.X - hx, t.Y - hy));
        Sound(1879, 0);
        Put(230, 2, 0, hx, hy);
        var path = Move(_home, t, 30, 1.0);
        Act(user, 1, 1, repeat: 0);
        int arrive = path.Count;
        Later(arrive, () => user.PlayAction(ObsMotionTable.ActionStand, 0));
        int last = arrive;
        for (int n = 0; n < 10; n++)
        {
            int at = arrive + 2 + Rand(10);
            Rise(174, Rand(3) + 3, at, hx + (Rand(2) == 0 ? -1 : 1) * Rand(20), hy + (Rand(2) == 0 ? -1 : 1) * Rand(20) * 0.8, 5, 0.9);
            last = Math.Max(last, at + Length(174, 3));
        }
        Later(0, () => host.Btl.CenterOn(t.X, t.Y));
        GunHold(path, t, last + 1);
        return last + 1;
    }

    /// <summary>
    /// 워프(367, <c>0x1006b640</c>) — 소리 1849, 시전자를 감추고(<c>0x10081350</c>, 1틱) 그 자리에서 자기 그림(걷기 모션)의 잔상이 겨눈 쪽으로 80px 미끄러지며
    /// 2틱마다 한 단계씩 8 → 1 로 사라진다(14틱). 30틱에 소리 1850, 32틱에 겨눈 칸 80px 앞에서 잔상이 나타나 그 칸으로 미끄러지며 1 → 8 로 짙어지고(14틱),
    /// 그것이 끝나면(≈ 48틱) 시전자가 그 칸에 다시 보이며 선다. 카메라는 두 잔상을 따라간다. 맞히는 것은 없다.
    /// </summary>
    private int GunWarp(List<int> targets, (double X, double Y) aim)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = aim != _home ? aim : TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        var (ox, oy) = PaOffsets[dir];
        Sound(1849, 0);
        Later(1, () => user.Fade = 0);
        int body = user.Data?.SpriteId ?? 0;
        bool ghosts = body > Part1Game.ObsBase && host.UiFor(body) != null;
        var (gm, gmirror) = BodyMotion(1, dir);
        if (ghosts)
        {
            var away = Move(_home, (hx + ox, hy + oy * 0.8), 2, 2.0);
            Along(body, gm, 0, AsPath(away.Prepend(_home).ToList()), life: 14, mirror: gmirror, fadeFrom: 1, fadeTo: 0.1, rawObs: true);
            (double X, double Y) from = (t.X - ox, t.Y - oy * 0.8);
            var into = Move(from, t, 2, 2.0);
            Along(body, gm, 32, AsPath(into.Prepend(from).ToList()), life: 14, mirror: gmirror, fadeFrom: 0.1, fadeTo: 1, rawObs: true);
        }
        Sound(1850, 30);
        Later(32, () => host.Btl.CenterOn(t.X, t.Y));
        const int Back = 48;
        Later(Back, () => { user.Fade = 1; user.PlayAction(ObsMotionTable.ActionStand, 0); });
        GunHold([], t, Back + 3, from: Back);
        return Back + 3;
    }

    /// <summary>시전자를 이 길대로 옮기고 끝날 때까지 <paramref name="at"/> 에 붙들어 둔다(원본은 그 칸에 남는다 — 실제 옮기기는 전투 쪽).</summary>
    private void GunHold(List<(double X, double Y)> path, (double X, double Y) at, int until, int from = 0)
    {
        var held = new List<(double X, double Y)>(path);
        while (held.Count < until - from + 1) held.Add(at);
        Carry(from, held);
    }

    // ---- 보조 ----

    /// <summary>
    /// 도발(382, <c>0x1006d360</c>) — 대상 i 마다 10i 틱 늦게: 표지 782:0(30틱, 소리 2010)이 z+140 에서 z+60 으로 내려오고(빠르기 10 × 0.9),
    /// 이어 782:1(소리 2011)이 반지름 10 의 작은 원을 100틱 돌고(<c>0x10080550</c>), 782:2(소리 2013)가 그 끝 자리에서 터지고, 10틱 뒤 782:3 이 z+140 에 선다.
    /// 원본은 핸들러 때 대상마다 SP(+0xe8)를 20 깎는다(0 아래로는 안 간다) — 여기서는 표지가 내려앉는 틱에 부른다.
    /// </summary>
    private int GunTaunt(List<int> targets, Action<int> hit)
    {
        int last = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            int ti = targets[i], s = 10 * i;
            var (x, y) = ((double, double))host.Btl.UnitFoot(host._units[ti]);
            (double X, double Y) top = (x, y - 140 * 0.6), mid = (x, y - 60 * 0.6);
            var fall = Move(top, mid, 10, 0.9);
            Along(782, 0, s, AsPath(fall.Prepend(top).ToList()), life: Length(782, 0), sounds: true);
            HitAt(s + fall.Count, ti, hit);
            int c = s + Length(782, 0) + 2;
            var lap = Circle(mid.X, mid.Y, 10, 100, 3, 1.0, Rand(2) == 0, -Math.PI / 2);
            Along(782, 1, c, lap, life: 100, sounds: true);
            int pop = c + 100 + 2;
            Put(782, 2, pop, lap[^1].X, lap[^1].Y, sounds: true);
            int cap = pop + Length(782, 2) + 2 + 10;
            Put(782, 3, cap, top.X, top.Y);
            last = Math.Max(last, cap + Length(782, 3));
        }
        return last + 1;
    }

    /// <summary>
    /// 집중(378, <c>0x1006cad0</c>) — 소리 1938, 대상 칸 가운데 z+60 에 Obs 56:0(89틱), 그 둘레 ±150px(월드)에서 빛알 백 개(Obs 54 · 57 · 58 · 63 의 모션 2)가
    /// 그 점으로 모인다(빠르기 1~3 × 1.1). 처음 스물은 0~9틱, 나머지는 i/5 + 10 틱 늦게 떠난다. 원본은 핸들러 때 대상 +0x25c(상태 22) = 40 을 넣는다.
    /// 맞히는 사건 · 피해 서술이 없다(<c>0x1006cad0</c> — 세기 식 0 이라 <c>hit</c> 를 부르면 같은 편을 쳤다). 그래서 <c>hit</c> 대신 집중을 바로 건다
    /// (<see cref="Part1Rules.FocusStatus"/> — 사거리가 높이 차에 안 깎인다).
    /// </summary>
    private int GunFocus(List<int> targets, (double X, double Y) aim)
    {
        var t = TargetPoint(targets, aim);
        (double X, double Y) c = (t.X, t.Y - 60 * 0.6);
        Sound(1938, 0);
        Put(56, 0, 0, c.X, c.Y, sounds: true);
        int[] obs = [54, 57, 58, 63];
        int last = Length(56, 0);
        for (int i = 0; i < 100; i++)
        {
            int d = i < 20 ? Rand(10) : i / 5 + 10;
            (double X, double Y) from = (c.X + (Rand(2) == 0 ? -1 : 1) * Rand(150), c.Y + (Rand(2) == 0 ? -1 : 1) * Rand(150) * 0.8);
            var flight = Move(from, c, Rand(3) + 1, 1.1);
            Along(obs[Rand(4)], 2, d, AsPath(flight.Prepend(from).ToList(), 2), life: flight.Count);
            last = Math.Max(last, d + flight.Count);
        }
        foreach (int ti in targets)
            if (host._units[ti] is { } u) Later(0, () => host.Btl.ApplyPart1Focus(_user, u));
        return last + 1;
    }

    /// <summary>
    /// 빛기둥(기도 · 희생, Obs 194) — 땅 조각(모션 m+1)과 z+234 부터 50 씩 위로 화면 위 끝 너머까지 쌓은 기둥 조각(모션 m)이
    /// 나타나고(12틱) → <paramref name="hold"/> 틱 머물고(m+2 · m+3) → 사라진다(m+4 · m+5, 20틱). 높이 끝 = 100 × (그 칸이 화면 위 끝에서 몇 줄 아래인가).
    /// 끝나는 틱을 돌려준다.
    /// </summary>
    private int GunPillar(double x, double y, int m, int hold)
    {
        double rows = Math.Max(1, (y - View.T) / 32.0), top = 100 * rows;
        int p1 = 0, p2 = p1 + Length(194, m) + 2, p3 = p2 + hold + 2;
        for (double z = 234; z < top; z += 50)
        {
            Put(194, m, p1, x, y - z * 0.6);
            Put(194, m + 2, p2, x, y - z * 0.6, life: hold);
            Put(194, m + 4, p3, x, y - z * 0.6);
        }
        Put(194, m + 1, p1, x, y);
        Put(194, m + 3, p2, x, y, life: hold);
        Put(194, m + 5, p3, x, y);
        return p3 + Length(194, m + 4);
    }

    /// <summary>
    /// 기도(375, <c>0x1006bbe0</c>) — 대상 칸에 빛기둥 194:0~5(머묾 56틱). 원본은 rand()%100 으로 나눈다: 0~4 면 빛기둥 없이 덧기술 198 「썬라이트」,
    /// 5~29 면 덧기술 193 「힐윈드5」 + 빛기둥(소리 없음), 30 이상이면 소리 1936 + 빛기둥만(아무 일 없음).
    /// 덧기술은 다음 명령 칸(<c>0x1003a2f0(공격, 0, 0, 0, 0, 1)</c> → 행동 끝 <c>0x1003a350</c> 이 <c>0x10039e60(…, 1)</c>)으로 앞머리 없이 돈다 —
    /// 둘 다 판 전체를 훑으니 겨눈 칸(0, 0)은 상관없다. 기도 자신은 맞히는 사건 · 피해 서술이 없다(세기 식 8 이라 <c>hit</c> 를 부르면 늘 썬 라이트처럼 고쳤다).
    /// 리메이크는 덧기술을 무기 덧기술과 같은 길(<see cref="BattleScene._part1Proc"/>)로 행동 끝에 공짜로 쏜다.
    /// </summary>
    private int GunPray(List<int> targets, (double X, double Y) aim)
    {
        int r = host._rng.Next(32768) % 100;
        int follow = r < 5 ? 198 : r < 30 ? 193 : 0;
        if (follow != 0) host.Btl.QueuePart1FollowUp(_user, follow);
        if (r < 5) return 1;
        var t = TargetPoint(targets, aim);
        if (r >= 30) Sound(1936, 0);
        int end = GunPillar(t.X, t.Y, 0, 56);
        return end + 1;
    }

    /// <summary>
    /// 희생(376, <c>0x1006c0a0</c>) — 소리 1937, 시전자 칸 z+40 에 Obs 55:0(356틱 동안 되풀이), 빛기둥 194:6~11(머묾 267틱), 89틱에 소리 196.
    /// 화면 안의 아군마다 Obs 52:0(z+40, 267틱)과 89~108틱에 시작해 둘레를 도는 빛 65:2(반지름 30 · 큰 몸 60, 110 · 120틱, 빠르기 10).
    /// 원본은 핸들러 때 시전자 아닌 아군 모두의 HP 를 최대로, SP 를 150 으로 한다(화면 밖도) — 여기서는 89틱에 부른다.
    /// </summary>
    private int GunSacrifice(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var (hx, hy) = _home;
        Sound(1937, 0);
        Put(55, 0, 0, hx, hy - 40 * 0.6, life: 356);
        int last = Math.Max(356, GunPillar(aim.X, aim.Y, 6, 267));
        Sound(196, 89);
        foreach (int ti in targets)
        {
            if (host._units[ti] == _user) continue;
            var (x, y) = ((double, double))host.Btl.UnitFoot(host._units[ti]);
            Put(52, 0, 0, x, y - 40 * 0.6, life: 267);
            int d = 89 + Rand(20);
            Along(65, 2, d, Circle(x, y, 30, 110, 10, 1.0, false, -Math.PI / 2), life: 110);
            HitAt(89, ti, hit);
            last = Math.Max(last, d + 110);
        }
        return last + 1;
    }

    /// <summary>해체대법(377, <c>0x1006c8f0</c>) — 소리 2036, 대상마다 z+40 에 Obs 52:0(89틱) → 이어 Obs 20:4(82틱). 원본은 핸들러 때 대상 HP := 1 · SP := 150.</summary>
    private int GunDismantle(List<int> targets, Action<int> hit)
    {
        Sound(2036, 0);
        int next = Length(52, 0) + 2, last = next;
        foreach (int ti in targets)
        {
            var (x, y) = ((double, double))host.Btl.UnitFoot(host._units[ti]);
            Put(52, 0, 0, x, y - 40 * 0.6);
            Put(20, 4, next, x, y - 40 * 0.6, sounds: true);
            HitAt(next, ti, hit);
            last = Math.Max(last, next + Length(20, 4));
        }
        return last + 1;
    }

    /// <summary>
    /// 강탈(374, <c>0x1006bb50</c>) — 대상 쪽을 보고 시전자 동작 2(기본 공격)를 한 번 할 뿐, 이펙트도 맞히는 사건도 없다. 맞는 것은 그 모션의 타격 키(종류 5) —
    /// 여기서는 그 동작의 첫 타격 키 틱(없으면 동작 길이의 반)에 부른다.
    /// </summary>
    private int GunSteal(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var t = TargetPoint(targets, aim);
        user.Facing = FacingOf(t.X - _home.X, t.Y - _home.Y);
        int len = Swing(user, 2, 8);
        int key = host._sprites.TryGetValue(user.ChrCode, out var sprite) && sprite.Clip(WeaponAction(2), user.Facing)?.Hits is { Count: > 0 } h
            ? h.Min(x => x.Start) : Math.Max(1, len / 2);
        foreach (int ti in targets) HitAt(key, ti, hit);
        return Math.Max(len, key) + 1;
    }

    /// <summary>
    /// 최면 386 · 혼란 384 · 컨디션이상 385(<c>0x1006dc60</c> · <c>0x1006d920</c> · <c>0x1006dac0</c>) — 대상마다 Obs 780:0 · 783:0(z+40, 64틱, 소리 2015 · 2016) ·
    /// 795:0(발치, 96틱, 소리 2069). 맞히는 때는 최면 · 혼란이 그림 나이 1(t0+1), 컨디션이상이 50(<c>0x10076a10</c>).
    /// </summary>
    private int GunStatus(int attackId, List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var (obs, lift, at) = attackId switch { 386 => (780, 40, 1), 384 => (783, 40, 1), _ => (795, 0, 50) };
        foreach (var (ti, (x, y)) in Points(targets, aim))
        {
            Put(obs, 0, 0, x, y - lift * 0.6, sounds: true);
            if (ti >= 0) HitAt(at, ti, hit);
        }
        return Math.Max(at, Length(obs, 0)) + 1;
    }

    // ---- 기계 ----

    /// <summary>범위 칸 — 넘겨받은 칸, 없으면 대상들의 칸, 그것도 없으면 겨눈 칸.</summary>
    private List<(int Col, int Row)> GunCells(List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim)
    {
        if (cells.Count > 0) return cells;
        var list = targets.Select(ti => (host._units[ti].Col, host._units[ti].Row)).Distinct().ToList();
        if (list.Count == 0) list.Add(((int)(aim.X / TileW), (int)Math.Round(aim.Y / (TileW * 0.8))));
        return list;
    }

    /// <summary>
    /// 칸 하나가 터진다(미사일 · 포격 · 자주포 · 폭격 · 공폭탄 공용) — <paramref name="tick"/> + 0~4 에 폭발 Obs 10 · 48(반반) 모션 0(소리 8 · 58)과 한 틱 뒤 맞힘,
    /// 먼지 34:0 다섯(5~14틱 늦게 솟는다, 3 × 1.1), 흙 38:0 다섯(빠르기 5, 0~9틱), 파편 366:0~3 열(빠르기 20 × 0.8, 0~9틱). 맞히는 틱을 돌려준다.
    /// </summary>
    private int GunBlast(int tick, double x, double y)
    {
        int at = tick + Rand(5);
        Put(Rand(2) == 0 ? 10 : 48, 0, at, x, y);
        for (int n = 0; n < 5; n++) Rise(34, 0, tick + Rand(10) + 5, x + (Rand(2) == 0 ? -1 : 1) * Rand(20), y + (Rand(2) == 0 ? -1 : 1) * Rand(20) * 0.8, 3, 1.1);
        for (int n = 0; n < 5; n++) Debris(38, 0, tick + Rand(10), x, y, 5, 0.97);
        for (int n = 0; n < 10; n++) Debris(366, Rand(4), tick + Rand(10), x, y, 20, 0.8);
        return at + 1;
    }

    /// <summary>범위 칸마다 터뜨리고, 대상은 자기 칸이 터지는 틱에(칸이 범위에 없으면 <paramref name="fallback"/> 틱에) 한 번 맞힌다. 마지막 틱을 돌려준다.</summary>
    private int GunBlastCells(List<int> targets, List<(int Col, int Row)> cells, Func<(int Col, int Row), int> start, int fallback, Action<int> hit)
    {
        var hitAt = new Dictionary<(int, int), int>();
        int last = fallback;
        foreach (var cell in cells)
        {
            var (cx, cy) = CellFoot(cell.Col, cell.Row);
            int s = start(cell);
            hitAt[cell] = GunBlast(s, cx, cy);
            last = Math.Max(last, s + 20 + Math.Max(Length(10, 0), Length(366, 0)));
        }
        foreach (int ti in targets)
            HitAt(hitAt.TryGetValue((host._units[ti].Col, host._units[ti].Row), out int at) ? at : fallback, ti, hit);
        return last;
    }

    /// <summary>
    /// 미사일(281, <c>0x100558c0</c>) · 폭격(396, <c>0x10070e00</c>) — 시전자가 쏘는 동작(파트1 동작 38)을 하고 31틱에 미사일 281(소리 1891, 연기 자식 284)이
    /// 시전자 옆에서 떠나 대상으로 난다(빠르기 5 × 1.2, 최대 40). 미사일은 쪽마다 모션 · 떠나는 자리가 다르다(위 0 · (+40, 0) z+100, 왼 1 · (−40, 0) z+100,
    /// 아래 2 · (−40, +40) z+80, 오른 1 뒤집어 · (+40, 0) z+100). 폭격은 모션 1 하나로 시전자 자리에서. 닿으면 그 자리에 Obs 123:2(소리 62),
    /// 범위 칸마다 터진다(미사일은 0~4틱 뒤 한꺼번에, 폭격은 닿은 곳에서 멀수록 늦게 — 거리 ÷ 2 의 늦춤은 가설).
    /// </summary>
    private int GunMissile(int attackId, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        int last = Swing(user, 38, 9);
        var (dx, dy, dz, m) = attackId == 396 ? (0, 0, 0, 1) : dir switch
        {
            0 => (40, 0, 100, 0),
            1 => (-40, 0, 100, 1),
            2 => (-40, 40, 80, 2),
            _ => (40, 0, 100, 1),
        };
        (double X, double Y) from = (hx + dx, hy + dy * 0.8 - dz * 0.6);
        var flight = Move(from, t, 5, 1.2);
        Along(281, m, 31, AsPath(flight.Prepend(from).ToList()), life: flight.Count, mirror: dir == 3, sounds: true);
        Later(31, () => host.Btl.CenterOn(t.X, t.Y));
        int impact = 31 + flight.Count;
        Put(123, 2, impact + 1, t.X, t.Y, sounds: true);
        var area = GunCells(targets, cells, aim);
        last = Math.Max(last, impact + 2 + Length(123, 2));
        int end = GunBlastCells(targets, area, cell =>
        {
            if (attackId != 396) return impact + 2;
            var (cx, cy) = CellFoot(cell.Col, cell.Row);
            return impact + 2 + (int)(Dist((cx, cy), t) / 2 / 10);
        }, impact + 2, hit);
        return Math.Max(last, end) + 1;
    }

    /// <summary>
    /// 포격(362, <c>0x100692d0</c>) · 자주포(395, <c>0x10070760</c>) — 시전자가 쏘는 동작(포격 파트1 동작 3 · 자주포 2)을 하고 28 · 34틱에 겨눈 칸 안 아무 점에
    /// Obs 123:2(소리 62)가 서며 카메라가 그리로 간다. 같은 틱 + 0~4 에 범위 칸마다 터진다.
    /// </summary>
    private int GunShell(int attackId, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var t = TargetPoint(targets, aim);
        user.Facing = FacingOf(t.X - _home.X, t.Y - _home.Y);
        int last = Swing(user, attackId == 362 ? 3 : 2, 9);
        int d = attackId == 362 ? 28 : 34;
        Put(123, 2, d, aim.X - 20 + Rand(40), aim.Y + (Rand(40) - 20) * 0.8, sounds: true);
        Later(d, () => host.Btl.CenterOn(t.X, t.Y));
        int end = GunBlastCells(targets, GunCells(targets, cells, aim), _ => d, d + 1, hit);
        return Math.Max(last, Math.Max(end, d + Length(123, 2))) + 1;
    }

    /// <summary>
    /// 공폭탄(387, <c>0x1006de00</c>) — 시전자가 파트1 동작 39 를 한 번 하고 그것이 끝날 때 소리 94. 폭탄 열여덟(Attack +0x58)이 범위 칸 둘레에 떨어진다:
    /// 시전자가 위 · 아래를 보면 Obs 283:8 이 바로 위(z+1000)에서, 옆을 보면 283:7 이 옆에서 비스듬히(빠르기 1 × 1.4). i 째는 동작이 끝나고 10 + 3i + 0~17 틱 뒤 떠난다.
    /// 떨어진 칸이 터지고 그 칸의 적을 맞힌다 — 여기서는 대상들의 칸에 먼저 하나씩 떨어뜨린다.
    /// </summary>
    private int GunAirBomb(List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        int act = Swing(user, 39, 9);
        Sound(94, act);
        var area = GunCells(targets, cells, aim);
        var drops = new List<(int Col, int Row)>();
        foreach (int ti in targets) drops.Add((host._units[ti].Col, host._units[ti].Row));
        while (drops.Count < 18) drops.Add(area[Rand(area.Count)]);
        var hitAt = new Dictionary<(int, int), int>();
        int last = act;
        bool side = dir is 1 or 3;
        for (int i = 0; i < drops.Count; i++)
        {
            var cell = drops[i];
            var (cx, cy) = CellFoot(cell.Col, cell.Row);
            int s = act + 2 + 10 + 3 * i + Rand(18);
            (double X, double Y) from = side ? (cx + (dir == 1 ? 1 : -1) * 600, cy - 300) : (cx, cy - 1000 * 0.6);
            var fall = Move(from, (cx, cy), 1, 1.4);
            Along(283, side ? 7 : 8, s, AsPath(fall.Prepend(from).ToList()), life: fall.Count, mirror: dir == 1);
            int land = s + fall.Count + 2;
            int at = GunBlast(land, cx, cy);
            hitAt.TryAdd(cell, at);
            last = Math.Max(last, land + 20 + Length(366, 0));
        }
        foreach (int ti in targets)
            HitAt(hitAt.TryGetValue((host._units[ti].Col, host._units[ti].Row), out int at) ? at : last, ti, hit);
        Later(act, () => host.Btl.CenterOn(t.X, t.Y));
        return last + 1;
    }

    /// <summary>
    /// 레이저 캐논(394, <c>0x10070440</c>) · 용병 빔라이플(315~317, <c>0x10062530</c>) · 사이드 슬레이더(400~402, <c>0x10073440</c>) — 15틱에 소리(1895 · 1895 · 2043)와 함께
    /// 빛살(505 · 505 · 250)이 시전자(z+80 · z+80 · z+40)에서 보는 쪽 판 끝까지 빠르기 40 으로 곧게 난다. 들어서는 칸마다 그 칸의 적을 맞힌다(<c>0x100769b0</c>).
    /// 모션: 505 는 위 6 · 아래 7 · 왼 8 · 오른 9, 250 은 위 6 · 왼 7 · 아래 8 · 오른 7 뒤집어. 시전자는 파트1 동작 3(빔라이플은 2).
    /// </summary>
    private int GunBeam(int attackId, List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        bool slider = attackId is >= 400 and <= 402, rifle = attackId is >= 315 and <= 317;
        int last = Swing(user, rifle ? 2 : 3, 9);
        int obs = slider ? 250 : 505;
        int m = slider ? dir switch { 0 => 6, 2 => 8, _ => 7 } : dir switch { 0 => 6, 2 => 7, 1 => 8, _ => 9 };
        double lift = (slider ? 40 : 80) * 0.6;
        (double X, double Y) from = rifle ? (hx + 10, hy - 3 * 0.8 - lift) : (hx, hy - lift);
        var (ux, uy) = PaOffsets[dir];
        var (l, top, r, b) = View;
        double reach = dir switch { 0 => from.Y - top, 1 => from.X - l, 2 => b - from.Y, _ => r - from.X } + 40;
        (double X, double Y) to = (from.X + Math.Sign(ux) * Math.Max(40, reach), from.Y + Math.Sign(uy) * Math.Max(40, reach));
        var flight = Move(from, to, 40, 1.0);
        Sound(slider ? 2043 : 1895, 15);
        Along(obs, m, 15, AsPath(flight.Prepend(from).ToList()), life: flight.Count, mirror: slider && dir == 3);
        foreach (int ti in targets)
        {
            var (fx, fy) = ((double, double))host.Btl.UnitFoot(host._units[ti]);
            int best = 0;
            for (int k = 1; k < flight.Count; k++)
                if (Dist(flight[k], (fx, fy - lift)) < Dist(flight[best], (fx, fy - lift))) best = k;
            HitAt(15 + best + 1, ti, hit);
        }
        Later(15, () => host.Btl.CenterOn(t.X, t.Y));
        return Math.Max(last, 15 + flight.Count) + 1;
    }
}
