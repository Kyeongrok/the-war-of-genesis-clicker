using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 검 기술 여덟 — 연 · 살 · 비 · 혼 · 파 · 참 · 격 · 광(어빌리티 23~31, 공격 11~50, 레벨마다 다음 공격 번호). 볼트 분석-파트1-검기술 5절.
/// </summary>
/// <remarks>
/// <para>원본 핸들러 <c>0x10045ad0</c>(연 11~15 · 다하카 두번 물기) · <c>0x10045c20</c>(살 16~20) · <c>0x10045df0</c>(비 21~25) · <c>0x10045f50</c>(혼 26~30) ·
/// <c>0x100463d0</c>(파 31~35) · <c>0x10046b20</c>(참 36~40) · <c>0x10046c70</c>(격 41~45) · <c>0x10047220</c>(광 46~50). 앞머리는 모두 종류 2(<see cref="WeaponPrep"/>).
/// 핸들러 안에서 레벨이 다른 것은 연 · 참의 시전자 동작(파트1 동작 11~15 = 2타~6타)뿐이고 나머지는 Attack.dat 의 수치만 다르다.</para>
/// <para>연 · 참은 이펙트 없이 시전자 동작만 걸고, 피해는 그 모션의 타격 키(종류 5)마다 난다(<c>0x10036130</c>). 여기서는 키 틱마다 <c>hit</c> 를 부른다 —
/// 전투가 대상마다 한 번만 받고 Attack +0x66(타 수)를 세기에 곱하므로(Part1Rules) 첫 키에 다 맞는다.</para>
/// <para>원본과 다르게 한 것: 비가 맞은 이를 4칸(160px) 밀어내는 것(<c>0x100816d0</c>)과 혼이 시전자를 겨눈 칸에 옮겨 두는 것은 판 상태를 바꾸는 일이라
/// 여기서는 하지 않는다 — 비는 그 자리에서 먼지만 일고, 혼은 끝날 때까지 겨눈 칸에 서 있다가 제자리로 돌아간다(자리를 옮기는 것은 전투 쪽 일).
/// 격의 범위 칸(<c>0x100393b0</c>, 모양 +0x33 = 2)은 칸 목록이 없어서 시전자에서 대상 쪽으로 곧게 R = +0x38 ÷ 4 칸(4~8)과 대상들의 칸으로 어림했다.</para>
/// </remarks>
internal sealed partial class Part1Skill
{
    /// <summary>파트1 검 기술 여덟의 공격 번호(11~50)인가.</summary>
    internal static bool IsWeaponSkill(int attackId) => attackId is >= 11 and <= 50;

    /// <summary>
    /// 검 기술 하나 — 공격 번호로 나눠 시각표를 깔고 끝나는 틱을 돌려준다. <paramref name="aim"/> 은 겨눈 칸의 발 자리(판 픽셀).
    /// 원본 T = 겨눈 칸에 선 이(+0x206)의 자리, 없으면 칸 가운데.
    /// </summary>
    internal int Weapon(int attackId, List<int> targets, (double X, double Y) aim, Action<int> hit) => attackId switch
    {
        >= 11 and <= 15 => Strike(attackId, targets, aim, hit),
        >= 16 and <= 20 => Sal(targets, aim, hit),
        >= 21 and <= 25 => Bi(targets, aim, hit),
        >= 26 and <= 30 => Hon(targets, aim, hit),
        >= 31 and <= 35 => Pa(targets, hit),
        >= 36 and <= 40 => Strike(11 + attackId - 36, targets, aim, hit),
        >= 41 and <= 45 => Gyeok(attackId, targets, aim, hit),
        >= 46 and <= 50 => Gwang(targets, aim, hit),
        _ => 0,
    };

    /// <summary>그 검 기술이 쓰는 파트1 그림(앞머리 184 · 시전자 모션의 자식 291 · 227 까지) — 파트1 이 없는 PC 에서 쓰기 전에 받아 둔다.</summary>
    internal static int[] WeaponNeeds(int attackId)
    {
        int[] common = [184, 291, 227];
        int[] own = attackId switch
        {
            >= 16 and <= 20 => [183],
            >= 21 and <= 25 => [285, 174],
            >= 26 and <= 30 => [230],
            >= 31 and <= 35 => [107, 615, 672, 619, 359, 246, 245],
            >= 41 and <= 45 => [10, 36],
            >= 46 and <= 50 => [526, 123],
            _ => [],
        };
        return [.. common.Concat(own).Distinct()];
    }

    /// <summary>
    /// 검 기술 앞머리 종류 2(<c>0x100437e0</c>, 24틱) — 시전자 머리 위(z+100)에 Obs 184:0(소리 83), 카메라를 시전자에,
    /// 대상 쪽을 보고 공격 자세(파트1 동작 9)를 붙든다. 24틱에 자세를 한 번 더 걸고 다음 틱에 핸들러가 돈다.
    /// </summary>
    private int WeaponPrep(UnitState user, (double X, double Y) aim)
    {
        var (hx, hy) = _home;
        Put(184, 0, 0, hx, hy - 100 * 0.6, sounds: true);
        host.Btl.CenterOnUnit(user);
        if (aim != _home) user.Facing = FacingOf(aim.X - hx, aim.Y - hy);
        user.PlayAction(Part2Action(9), 25 / TicksPerSecond);
        return 25;
    }

    /// <summary>연 · 참 — 대상 쪽을 보고 파트1 동작 11~15(2타~6타)를 한 번. 모션의 타격 키마다 대상들을 친다.</summary>
    private int Strike(int part1Action, List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var t = TargetPoint(targets, aim);
        user.Facing = FacingOf(t.X - _home.X, t.Y - _home.Y);
        var (length, hits) = PlayStrike(user, part1Action);
        foreach (int tick in hits)
            foreach (int ti in targets) HitAt(tick, ti, hit);
        return length + 1;
    }

    /// <summary>살 — 시전자가 베고(파트1 동작 19), 15틱 뒤 대상 위(z+40)에 칼금 183(모션 0~2 중 하나)이 서며 한 틱 뒤 친다.</summary>
    private int Sal(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var t = TargetPoint(targets, aim);
        user.Facing = FacingOf(t.X - _home.X, t.Y - _home.Y);
        int last = Swing(user, 19, 8);
        foreach (var (ti, (tx, ty)) in Points(targets, aim))
        {
            int m = Rand(3);
            Put(183, m, 15, tx, ty - 40 * 0.6, sounds: true);
            if (ti >= 0) HitAt(16, ti, hit);
            last = Math.Max(last, 15 + Length(183, m));
        }
        return last + 1;
    }

    /// <summary>
    /// 비 — 시전자가 던지는 동작(파트1 동작 17)을 하고 15틱 뒤 검기 285:0(소리 96)이 시전자 z+60 에서 대상 z+60 으로 난다(빠르기 5 × 1.3, 최대 40).
    /// 가는 길에 처음 닿은 적을 그 자리에서 치고 멈춘다(<c>0x1007db00</c>). 원본은 그 적을 4칸 밀어내며 틱마다 먼지 174:3~5 둘을 일으킨다.
    /// </summary>
    private int Bi(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = TargetPoint(targets, aim);
        user.Facing = FacingOf(t.X - hx, t.Y - hy);
        int last = Swing(user, 17, 8);
        (double X, double Y) from = (hx, hy - 60 * 0.6), to = (t.X, t.Y - 60 * 0.6);
        var flight = Move(from, to, 5, 1.3);
        // 처음 닿는 적 — 길 위에서 그 발 자리에 반 칸(20px) 안으로 들어오는 첫 틱.
        int victim = -1, at = flight.Count;
        foreach (int ti in targets)
        {
            var (ux, uy) = host.Btl.UnitFoot(host._units[ti]);
            for (int k = 0; k < Math.Min(at, flight.Count); k++)
                if (Math.Abs(flight[k].X - ux) < 20 && Math.Abs(flight[k].Y + 60 * 0.6 - uy) < 20 * 0.8) { (victim, at) = (ti, k + 1); break; }
        }
        if (victim < 0 && targets.Count > 0) victim = targets[0];
        var path = flight.Take(at).Prepend(from).ToList();
        Along(285, 0, 15, AsPath(path), life: path.Count, sounds: true);
        Later(15, () => host.Btl.CenterOn(t.X, t.Y));
        int arrive = 15 + path.Count - 1;
        last = Math.Max(last, arrive);
        if (victim >= 0)
        {
            HitAt(arrive, victim, hit);
            // 밀려나는 동안의 먼지(빠르기 25 × 0.85 로 160px — 여기서는 그 자리에서 일어난다).
            var (vx, vy) = host.Btl.UnitFoot(host._units[victim]);
            int push = Move((0, 0), (160, 0), 25, 0.85).Count;
            for (int k = 0; k < push; k++)
                for (int n = 0; n < 2; n++) Put(174, Rand(3) + 3, arrive + k, vx + (Rand(2) == 0 ? -1 : 1) * Rand(20), vy + 20 * 0.8);
            last = Math.Max(last, arrive + push + Length(174, 3));
        }
        return last + 1;
    }

    /// <summary>
    /// 혼 — 소리 1892, 시전자 발치에 칼바람 230(위·아래는 모션 0, 옆은 1), 시전자가 돌진 자세(파트1 동작 18)로 겨눈 칸까지 빠르기 20 × 1.8 로 내달린다.
    /// 틱마다 자기 그림의 잔상을 남기고, 지나는 칸의 적을 그 칸에 들어설 때 친다(<c>0x100769b0</c>). 닿으면 돌진 자세 두 번 → 내려 베기(동작 21).
    /// </summary>
    private int Hon(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = aim != _home ? aim : TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        Sound(1892, 0);
        // 칼바람 — 방향마다 자리 · 모션이 다르다(표 0x100463c0). 오른쪽은 뒤집는다.
        switch (dir)
        {
            case 0: Put(230, 0, 0, hx, hy); break;
            case 1: Put(230, 1, 0, hx, hy); break;
            case 2: Put(230, 0, 0, hx, hy + 20 * 0.8); break;
            default: Put(230, 1, 0, hx + 20, hy, mirror: true); break;
        }

        var dash = Move(_home, t, 20, 1.8);
        Act(user, 18, 1, repeat: 0);
        Later(0, () => host.Btl.CenterOn(t.X, t.Y));
        int body = user.Data?.SpriteId ?? 0;
        bool ghosts = body > Part1Game.ObsBase && host.UiFor(body) != null;
        var (gm, gmirror) = BodyMotion(18, dir);
        for (int k = 0; k < dash.Count; k++)
            if (ghosts) Along(body, gm, k, [(0, dash[k].X, dash[k].Y)], life: 14, mirror: gmirror, fadeFrom: 0.7, fadeTo: 0.1, rawObs: true);
        // 지나는 칸 — 대상마다 길에서 가장 가까운 틱에 친다.
        foreach (int ti in targets)
        {
            var (ux, uy) = host.Btl.UnitFoot(host._units[ti]);
            int best = 0;
            for (int k = 1; k < dash.Count; k++)
                if (Dist(dash[k], (ux, uy)) < Dist(dash[best], (ux, uy))) best = k;
            HitAt(best + 1, ti, hit);
        }
        int arrive = dash.Count, c1 = arrive + 2, c1Len = 2 * ActionTicks(user, 18, 1), c2 = c1 + c1Len + 2;
        Later(c1, () => Act(user, 18, 1, repeat: 2));
        Later(c2, () => Swing(user, 21, 8));
        int last = c2 + ActionTicks(user, 21, 8);
        // 원본은 시전자가 그 칸에 남는다 — 끝날 때까지 그 자리에 붙들어 둔다.
        var held = new List<(double X, double Y)>(dash);
        while (held.Count < last + 1) held.Add(t);
        Carry(0, held);
        return last + 1;
    }

    /// <summary>
    /// 파 — 소리 2043. 시전자가 보는 쪽에서 시계 방향으로 네 번 돌아 다시 그 쪽까지 다섯 걸음(파트1 동작 20 을 세 번씩). 걸음마다 자기 잔상이 그 쪽으로 80px 밀려나며 바래고,
    /// 칼금 107 · 615 · 672 · 619 · 359 · 246 이 시전자에 겹친다. 끝으로 마무리 칼금 245 가 서고, 그것이 사라질 때 둘레(맨해튼 2칸)를 친다(<c>0x10076950</c>).
    /// </summary>
    private int Pa(List<int> targets, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        int d0 = Dir4Of(user.Facing);
        Sound(2043, 0);
        int step = 3 * ActionTicks(user, 20, 5);
        int body = user.Data?.SpriteId ?? 0;
        bool ghosts = body > Part1Game.ObsBase && host.UiFor(body) != null;
        int start = 0, last = 0;
        for (int k = 0; k < 5; k++)
        {
            int dir = ((d0 - k) % 4 + 4) % 4, next = ((d0 - k - 1) % 4 + 4) % 4;
            int s = start;
            Later(s, () => { user.Facing = FacingOf4(dir); Act(user, 20, 5, repeat: 3); });
            // 잔상 — 그 쪽 80px 의 점을 빠르기 2 × 2.0 으로 따라가며 3틱마다 한 단계씩 8 → 1 로 바랜다(21틱).
            if (ghosts)
            {
                var (ox, oy) = PaOffsets[dir];
                var slide = Move(_home, (hx + ox, hy + oy * 0.8), 2, 2.0);
                var (gm, gmirror) = BodyMotion(20, next);
                Along(body, gm, s, AsPath(slide.Prepend(_home).ToList()), life: 21, mirror: gmirror, fadeFrom: 0.8, fadeTo: 0.1, rawObs: true);
            }
            Put(107, Rand(3), s, hx, hy);
            Put(615, Rand(4), s, hx, hy);
            Put(672, Rand(3), s, hx, hy);
            Put(619, Rand(3), s, hx, hy);
            Put(359, Rand(3), s, hx, hy);
            Put(246, Rand(6), s, hx, hy);
            last = Math.Max(last, s + Math.Max(21, Length(107, 0)));
            start = s + step + 2;
        }
        // 마무리 칼금 — 처음 방향(위 0 · 왼 12 · 아래 8 · 오른 4)으로, 사라질 때 친다.
        int fin = d0 switch { 0 => 0, 1 => 12, 2 => 8, _ => 4 };
        Put(245, fin, start, hx, hy);
        int end = start + Length(245, fin);
        foreach (int ti in targets) HitAt(end, ti, hit);
        return Math.Max(last, end) + 1;
    }

    /// <summary>파 잔상이 밀려나는 쪽(월드 — 0 위 · 1 왼 · 2 아래 · 3 오른, 표 0x10046b00).</summary>
    private static readonly (int X, int Y)[] PaOffsets = [(0, -80), (-80, 0), (0, 80), (80, 0)];

    /// <summary>
    /// 격 — 시전자가 내려치고(파트1 동작 22) 범위 칸마다 시전자에서의 거리 d 에 따라 20 + d/10 틱 뒤 땅이 터진다(Obs 10:0, 소리 8). 터지는 그 칸을 한 틱 뒤 친다.
    /// 칸마다 돌 조각 36:0 열 개가 솟는다(빠르기 5~14 × 0.95, 0~19틱 더 늦게). 21틱쯤 카메라가 그 쪽으로 옮겨 간다.
    /// </summary>
    private int Gyeok(int attackId, List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        int last = Swing(user, 22, 8);
        // 범위 칸 — 대상 쪽으로 곧게 R 칸(+0x38 ÷ 4: Lv1 4 … Lv5 8)과 대상들의 칸.
        int reach = 4 + Math.Clamp(attackId - 41, 0, 4);
        var (ux, uy) = PaOffsets[dir];
        var cells = new List<(int Target, double X, double Y)>();
        for (int k = 1; k <= reach; k++) cells.Add((-1, hx + ux / 2.0 * k, hy + uy / 2.0 * k * 0.8));
        foreach (int ti in targets)
        {
            var (fx, fy) = host.Btl.UnitFoot(host._units[ti]);
            cells.RemoveAll(c => c.Target < 0 && Math.Abs(c.X - fx) < 20 && Math.Abs(c.Y - fy) < 16);
            cells.Add((ti, fx, fy));
        }
        var loud = new HashSet<int>();
        foreach (var (ti, x, y) in cells)
        {
            double d = Math.Sqrt((x - hx) * (x - hx) + (y - hy) / 0.8 * ((y - hy) / 0.8));
            int at = 20 + (int)(d / 10);
            Put(10, 0, at, x, y, sounds: loud.Add(at));
            if (ti >= 0) HitAt(at + 1, ti, hit);
            for (int n = 0; n < 10; n++)
                Rise(36, 0, at + Rand(20), x + (Rand(2) == 0 ? -1 : 1) * Rand(20), y + (Rand(2) == 0 ? -1 : 1) * Rand(20) * 0.8, Rand(10) + 5, 0.95);
            last = Math.Max(last, at + 20 + Math.Max(Length(10, 0), Length(36, 0)));
        }
        Later(21, () => host.Btl.CenterOn(hx + ux / 2.0 * reach / 2, hy + uy / 2.0 * reach / 2 * 0.8));
        return last + 1;
    }

    /// <summary>광 — 시전자가 기술 동작(파트1 동작 23)을 하고 51틱 뒤 대상에 빛기둥 526:0(z+20, 소리 8)과 터짐 123:0(소리 64)이 서며 한 틱 뒤 친다.</summary>
    private int Gwang(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var t = TargetPoint(targets, aim);
        user.Facing = FacingOf(t.X - _home.X, t.Y - _home.Y);
        int last = Swing(user, 23, 8);
        foreach (var (ti, (tx, ty)) in Points(targets, aim))
        {
            Put(526, 0, 51, tx, ty - 20 * 0.6, sounds: true);
            Put(123, 0, 51, tx, ty, sounds: true);
            if (ti >= 0) HitAt(52, ti, hit);
        }
        return Math.Max(last, 51 + Math.Max(Length(526, 0), Length(123, 0))) + 1;
    }

    // ---- 도움 ----

    /// <summary>원본 T — 첫 대상의 발 자리, 없으면 겨눈 칸.</summary>
    private (double X, double Y) TargetPoint(List<int> targets, (double X, double Y) aim) =>
        targets.Count > 0 ? ((double, double))host.Btl.UnitFoot(host._units[targets[0]]) : aim;

    /// <summary>대상마다의 발 자리 — 대상이 없으면 겨눈 칸 하나(번호 −1).</summary>
    private List<(int Target, (double X, double Y) At)> Points(List<int> targets, (double X, double Y) aim) =>
        targets.Count > 0 ? [.. targets.Select(ti => (ti, ((double, double))host.Btl.UnitFoot(host._units[ti])))] : [(-1, aim)];

    private static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>파트1 네 방향(<c>0x10019de0</c>) — |dx| ≥ |dy| 면 왼 1 · 오른 3, 아니면 위 0 · 아래 2. dy 는 화면 값이라 월드로 되돌려 잰다.</summary>
    private static int Dir4(double dx, double dy) => Math.Abs(dx) >= Math.Abs(dy / 0.8) ? (dx < 0 ? 1 : dx > 0 ? 3 : 2) : (dy < 0 ? 0 : 2);

    private static int Dir4Of(Facing f) => f switch { Facing.Up => 0, Facing.Left => 1, Facing.Right => 3, _ => 2 };

    private static Facing FacingOf4(int dir) => dir switch { 0 => Facing.Up, 1 => Facing.Left, 3 => Facing.Right, _ => Facing.Down };

    /// <summary>파트1 동작 → 이 게임 동작. 11 · 12 는 2타 · 3타(13 · 14), 13~15 는 비어 있는 40 뒤 자리(ActionMap 에 넣어야 생긴다), 16 이상은 40 뒤.</summary>
    private static int WeaponAction(int part1) => part1 switch
    {
        11 => 13,
        12 => 14,
        >= 13 and <= 15 => Part1Game.ExtraBase + part1,
        _ => Part2Action(part1),
    };

    /// <summary>시전자 그림(잔상)의 모션 번호 — 파트1 SetAction 처럼 위 0 · 옆 1 · 아래 2, 오른쪽(3)은 옆을 뒤집는다.</summary>
    private static (int Motion, bool Mirror) BodyMotion(int part1Action, int dir4) =>
        (WeaponAction(part1Action) * 3 + (dir4 is 0 ? 0 : dir4 is 2 ? 2 : 1), dir4 == 3);

    /// <summary>그 동작 한 번의 길이(틱) — 그림에 없으면 <paramref name="fallback"/>(이 게임 동작)의 길이.</summary>
    private int ActionTicks(UnitState u, int part1Action, int fallback)
    {
        if (!host._sprites.TryGetValue(u.ChrCode, out var sprite)) return 1;
        int action = WeaponAction(part1Action), use = sprite.RawClip(action * 3) != null ? action : fallback;
        return Math.Max(1, (int)Math.Round(sprite.ActionSeconds(use, u.Facing) * TicksPerSecond));
    }

    /// <summary>시전자에게 파트1 동작을 지금 한 번 건다 — 그 모션의 소리(휘두르는 소리)도 낸다. 걸린 틱.</summary>
    private int Swing(UnitState u, int part1Action, int fallback)
    {
        if (!host._sprites.TryGetValue(u.ChrCode, out var sprite)) return 0;
        int action = WeaponAction(part1Action), use = sprite.RawClip(action * 3) != null ? action : fallback;
        double once = Math.Max(1 / TicksPerSecond, sprite.ActionSeconds(use, u.Facing));
        u.PlayAction(use, once);
        host.ScheduleActionSounds(u, use);
        return (int)Math.Round(once * TicksPerSecond);
    }

    /// <summary>
    /// 연 · 참의 칼질 — 파트1 동작 11~15 를 지금 걸고 (걸린 틱, 때리는 틱들)을 돌려준다. 때리는 틱은 그 모션의 타격 키에서 읽고,
    /// 키가 없으면 살라딘(Obs 196)의 키 자리. 4타~6타 동작이 그림에 없으면 2타 · 3타를 이어 붙인다(4 = 2+2 · 5 = 2+3 · 6 = 3+3).
    /// </summary>
    private (int Length, List<int> Hits) PlayStrike(UnitState u, int part1Action)
    {
        int[] keys = StrikeKeys[Math.Clamp(part1Action - 11, 0, 4)];
        if (!host._sprites.TryGetValue(u.ChrCode, out var sprite)) return (keys[^1] + 6, [.. keys]);
        bool Has(int a) => sprite.RawClip(a * 3) != null;
        int Ticks(int a) => Math.Max(1, (int)Math.Round(sprite.ActionSeconds(a, u.Facing) * TicksPerSecond));
        List<int> HitsOf(int a, int[] fallback) =>
            sprite.Clip(a, u.Facing)?.Hits is { Count: > 0 } h ? [.. h.Select(x => x.Start).OrderBy(x => x)] : [.. fallback];
        int own = WeaponAction(part1Action);
        if (Has(own))
        {
            int len = Ticks(own);
            u.PlayAction(own, len / TicksPerSecond);
            host.ScheduleActionSounds(u, own);
            return (len, HitsOf(own, keys));
        }
        // 이어 붙이기 — 앞 동작이 끝나는 틱에 뒤 동작을 건다.
        (int A, int B) pair = part1Action switch { 13 => (13, 13), 14 => (13, 14), 15 => (14, 14), _ => (13, -1) };
        int a = Has(pair.A) ? pair.A : 8;
        int la = Ticks(a);
        u.PlayAction(a, la / TicksPerSecond);
        host.ScheduleActionSounds(u, a);
        var hits = HitsOf(a, a == 13 ? StrikeKeys[0] : [9]);
        if (pair.B < 0 || !Has(pair.B)) return (la, hits);
        int b = pair.B, lb = Ticks(b);
        Later(la, () => { u.PlayAction(b, lb / TicksPerSecond); host.ScheduleActionSounds(u, b); });
        hits.AddRange(HitsOf(b, b == 13 ? StrikeKeys[0] : StrikeKeys[1]).Select(x => la + x));
        return (la + lb, hits);
    }

    /// <summary>살라딘(Obs 196)의 동작 11~15 타격 키 틱(종류 5) — 아홉 인물이 모두 같다(Part1Game.ActionMap 주석).</summary>
    private static readonly int[][] StrikeKeys =
    [
        [9, 18], [9, 15, 21], [9, 15, 21, 27], [9, 15, 21, 27, 33], [9, 15, 18, 21, 24, 54],
    ];
}
