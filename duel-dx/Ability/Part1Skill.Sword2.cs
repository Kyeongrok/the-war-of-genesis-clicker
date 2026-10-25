using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 검 기술(아이템 기술) 여덟 — 천지파열무(공격 286) · 풍아열공참(293) · 지하드(364) · 새틀라이트 어택(291) · 마룡광아탄(296) ·
/// 플라즈마 슬래쉬(295) · 혈랑마혼(292) · 소드브레이크(363). 볼트 분석-파트1-검기술 6절.
/// </summary>
/// <remarks>
/// <para>원본 핸들러 <c>0x10059640</c>(286) · <c>0x100602a0</c>(293) · <c>0x1006a2b0</c>(364) · <c>0x1005f350</c>(291, 단계식) · <c>0x10061230</c>(296) ·
/// <c>0x10060cd0</c>(295) · <c>0x1005fab0</c>(292) · <c>0x100698f0</c>(363). 앞머리는 모두 종류 1(<see cref="SwordPrep"/>).
/// 핸들러는 이펙트 · 시전자 동작 · 피해 사건만 깐다 — 소드브레이크가 무기를 깨뜨리거나 혈랑마혼이 빨아들이는 일은 핸들러에 없다.</para>
/// <para>사슬은 앞 것이 끝난 다음 틱에, 지연이 붙은 사슬은 끝나고 (지연 + 2) 틱에 시작한다(<c>0x10076610</c>). 피해 사건(<c>0x100768f0</c>)은 그 그림의
/// 첫 틱에 난다. 높이 z 는 판 픽셀로 −0.6 배, 월드 y 는 0.8 배(<c>0x1008fbb0</c>).</para>
/// <para>원본과 다르게 한 것: 플라즈마 슬래쉬 · 마룡광아탄의
/// 범위 칸은 전투가 넘겨준 <c>cells</c> 를 쓴다(대상의 칸이 그 안에 없으면 가장 가까운 칸이 터질 때 맞는다). 천지파열무의 큰 인물용 금 모션(21)은
/// 인물 크기(+0xb9)를 알 수 없어 늘 보통(19)을 쓴다.</para>
/// </remarks>
internal sealed partial class Part1Skill
{
    /// <summary>이 파일이 그리는 파트1 검 기술(공격 번호)인가.</summary>
    internal static bool IsSword2Skill(int attackId) => attackId is 286 or 291 or 292 or 293 or 295 or 296 or 363 or 364;

    /// <summary>검 기술 하나 — 공격 번호로 나눠 시각표를 깔고 끝나는 틱을 돌려준다. <paramref name="aim"/> 은 겨눈 칸의 발 자리(판 픽셀).</summary>
    private int Sword2(int attackId, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit) => attackId switch
    {
        286 => S2Cheonji(targets, hit),
        293 => S2Punga(targets, aim, hit),
        364 => S2Jihad(targets, hit),
        291 => S2Satellite(targets, aim, hit),
        296 => S2Maryong(targets, cells, aim, hit),
        295 => S2Plasma(targets, cells, aim, hit),
        292 => S2Hyeollang(targets, aim, hit),
        363 => S2SwordBreak(targets, hit),
        _ => 0,
    };

    /// <summary>앞머리 — 여덟 모두 Attack +0x69 = 1(검 기술 앞머리 <c>0x10043950</c>, 50틱).</summary>
    private int Sword2Prep(int attackId, UnitState user, (double X, double Y) aim) => SwordPrep(user);

    /// <summary>그 기술이 쓰는 파트1 그림(앞머리 395 · 자식 그림까지) — 파트1 이 없는 PC 에서 쓰기 전에 받아 둔다.</summary>
    private static int[] Sword2Needs(int attackId) => attackId switch
    {
        286 => [395, 123, 666, 617, 10, 48, 228, 765],
        291 => [395, 397, 398, 399, 277, 897],
        292 => [395, 536, 230],
        293 => [395, 54, 64, 499, 55],
        295 => [395, 724],
        296 => [395, 173, 666, 764, 766],
        363 => [395, 535, 367, 173, 10, 48, 365, 368, 897],
        364 => [395, 394, 192, 897],
        _ => [395],
    };

    /// <summary>
    /// 천지파열무(286, 멸살지옥검) — 시전자가 정면을 보고 파트1 동작 26 → 동작 27(번쩍임이 끝날 때까지 되풀이), 둘레에 123:0 · 666:0.
    /// 시전자에서 네 대각선으로 보이는 칸 끝까지 땅이 갈라지고(617, 왼위 · 오른아래는 뒤집힘) 칸마다 10/48:1 과 솟는 돌 228:6.
    /// 마지막 금이 끝나면 대상마다 617 이 갈라지고 765:0 이 서며 한 번씩 친다. 끝에 화면이 하얗게 번쩍인다(0x100836f0).
    /// </summary>
    private int S2Cheonji(List<int> targets, Action<int> hit)
    {
        var (hx, hy) = _home;
        var user = _user;
        Sound(2025, 15);
        user.Facing = Facing.Down;
        int a = Act(user, 26, 5);
        Later(a + 1, () => { user.Facing = Facing.Down; Act(user, 27, 5, repeat: 0); });
        Put(123, 0, a + 1, hx, hy);
        Put(666, 0, a + 1, hx, hy);
        int last = a + 1 + Math.Max(Length(123, 0), Length(666, 0));

        // 대각선 넷 — 줄마다 d = rand%30 · k* = rand%5 를 따로 뽑는다. 칸 k 의 자리 P = 시전자 + (±40k, ±40k), P′ = P 의 한 칸 위.
        var (left, top, right, bottom) = View;
        int chainEnd = -1;   // L0x1c — 마지막으로 깐 칸의 10/48 조각. 대상 쪽은 이것이 끝나면 시작한다.
        foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            bool mirror = sx == sy;
            int d = Rand(30), kStar = Rand(5);
            for (int k = 1; k < 64; k++)
            {
                double px = hx + sx * 40 * k, py = hy + sy * 40 * k * 0.8, upY = py - 40 * 0.8;
                if (px < left || px > right || py < top || py > bottom) break;
                int r = Rand(3);
                if (k == kStar || Rand(2) == 1) Put(617, 10 + r, a + 1, px, py, mirror: mirror);
                int e1 = a + d + 66, e1End = e1 + Length(617, 6 + r);
                Put(617, 6 + r, e1, px, py, mirror: mirror);
                if (k == 1) { Sound(2021, a); Sound(2022, e1End); }
                // 벌어진 금 — 250틱 붙들었다가 닫힌다.
                int s = e1End + d + 5 * k + 2;
                Put(617, 2 * r, s, px, upY, life: 250, mirror: mirror);
                Put(617, 2 * r + 1, s, px, py, life: 250, mirror: mirror);
                Put(617, 2 * r + 13, s + 251, px, upY, mirror: mirror);
                Put(617, 2 * r + 14, s + 251, px, py, mirror: mirror);
                Put(617, 6 + r, s, px, py, mirror: mirror);
                int g = e1End + d + 7 * k + 2, obs = Rand(2) == 0 ? 10 : 48;
                Put(obs, 1, g, px, py);
                chainEnd = g + Length(obs, 1);
                for (int n = 0; n < 5; n++)
                    Rise(228, 6, g + Rand(10), px + Rand(41) - 20, py + (Rand(41) - 20) * 0.8 - Rand(20) * 0.6, Rand(5) + 15, 0.9);
                last = Math.Max(last, Math.Max(s + 251 + Length(617, 2 * r + 14), chainEnd));
            }
        }

        // 대상마다 — 모두 같은 때(L0x1c 끝)에 시작한다. 617 둘(두 번째는 5틱 늦게 · 뒤집혀) → 617:10~12 → 갈라진 땅 617:19(70틱) → 20,
        // 그와 함께 765:0(친다) · 123:5 · 10:1 · 솟는 돌 228 스물.
        int start = chainEnd < 0 ? a + 1 : chainEnd, flash = start;
        for (int j = 0; j < targets.Count; j++)
        {
            int ti = targets[j];
            var (tx, ty) = host.Btl.UnitFoot(host._units[ti]);
            Put(617, 6 + Rand(4), start + 1, tx, ty);
            int mx = 6 + Rand(4), x = start + 7, xEnd = x + Length(617, mx);
            Put(617, mx, x, tx, ty, mirror: true);
            if (j == 0) Sound(2023, xEnd);
            int mc = 10 + Rand(3), c = xEnd + 1, cEnd = c + Length(617, mc);
            Put(617, mc, c, tx, ty);
            if (j == 0) Sound(2024, cEnd);
            int h = cEnd + 1;
            Put(617, 19, h, tx, ty, life: 70);
            Put(617, 20, h + 71, tx, ty);
            Put(765, 0, h, tx, ty);
            HitAt(h + 1, ti, hit);
            Put(123, 5, h, tx, ty);
            Put(10, 1, h, tx, ty);
            for (int n = 0; n < 20; n++)
                Rise(228, 6 + Rand(3), h + Rand(10), tx + Rand(121) - 60, ty + (Rand(121) - 60) * 0.8 - Rand(20) * 0.6, Rand(10) + 20, 0.95);
            flash = Math.Max(flash, cEnd);
            last = Math.Max(last, h + 71 + Length(617, 20));
        }
        // 번쩍임(색 16, 0 → 31 을 10씩) — 원본은 그 자리에서 네 장을 보여 주는 막는 번쩍임. 끝나면 시전자의 동작 27 이 멎는다(0x100765e0).
        _whiteouts.Add((At(flash + 1), 8, 0xFFFFFF));
        Later(flash + 5, () => user.PlayAction(ObsMotionTable.ActionStand, 0));
        return Math.Max(last, flash + 10);
    }

    /// <summary>
    /// 풍아열공참(293) — 대상 쪽을 보고 파트1 동작 14(소리 1890 은 그 끝에). 끝나면 동작 19 를 하고, 시전자 z+60 에 기운 54/64 스물다섯이 모이며
    /// 14틱 뒤 대상 위(z+60)에 499:0 이 서서 그 셋째 틱에 친다(<c>0x10076a10</c>).
    /// </summary>
    private int S2Punga(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var (hx, hy) = _home;
        var user = _user;
        var t = TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        int a = Act(user, 14, 13);
        Sound(1890, a);
        int last = a;
        for (int n = 0; n < 25; n++)
        {
            int obs = Rand(2) == 0 ? 64 : 54;
            (double X, double Y, double Z) from = (hx + (Rand(2) == 0 ? -1 : 1) * Rand(100), hy + (Rand(2) == 0 ? -1 : 1) * Rand(100) * 0.8, 60);
            var fly = S2Fly(from, (hx, hy, 60), Rand(2) + 2, 1.3);
            int go = a + 2 + Rand(3);
            Along(obs, 0, go, AsPath(fly.Prepend(S2Board(from)).ToList()), life: fly.Count + 1);
            last = Math.Max(last, go + fly.Count + 1);
        }
        Later(a + 1, () => { user.Facing = FacingOf4(dir); Act(user, 19, 8); });
        int fx = a + 14;
        Put(499, 0, fx, t.X, t.Y - 60 * 0.6, sounds: true);
        foreach (int ti in targets) HitAt(fx + 3, ti, hit);
        return Math.Max(last, Math.Max(fx + Length(499, 0), a + 1 + ActionTicks(user, 19, 8))) + 1;
    }

    /// <summary>
    /// 지하드(364) — 시전자는 정면을 보고 파트1 동작 31, 둘레에 기운 897. 큰 칼 394 가 시전자 위(z+120 → z+200)로 솟아,
    /// 대상(최대 여덟, 아무 차례 · 겹침 없이)마다 한 칸씩 돌아 그쪽을 보고(16방향, 한 틱에 한 칸) 빠르기 15 × 1.6 으로 날아가 닿으면 친다.
    /// 대상을 80 지나 16 × 0.86 으로 미끄러진 뒤 다음 대상으로. 다 돌면 시전자 위로 돌아와 위를 보고 z+120 으로 내려앉는다.
    /// </summary>
    private int S2Jihad(List<int> targets, Action<int> hit)
    {
        var (hx, hy) = _home;
        var user = _user;
        Sound(1907, 0);
        user.Facing = Facing.Down;
        Act(user, 31, 5);
        int last = S2Aura(10, 96);
        (double X, double Y, double Z) low = (hx, hy, 120), high = (hx, hy, 200), cur = high;
        var rise = S2Fly(low, high, 1, 1.1);
        Along(394, 0, 0, AsPath(rise.Prepend(S2Board(low)).ToList()), life: rise.Count + 1);
        host.Btl.CenterOn(hx, hy - 160 * 0.6);
        int tick = rise.Count + 1, d = 0;
        var order = targets.OrderBy(_ => Rand(1 << 16)).Take(8).ToList();
        for (int k = 0; k <= order.Count + 1; k++)
        {
            bool home = k == order.Count, done = k == order.Count + 1;
            (double X, double Y, double Z) to = high;
            if (k < order.Count)
            {
                // 몸의 아무 점(0x10039d90) — 발에서 x ±10, 월드 y +19, 높이는 그림 키의 가운데쯤(가설 40).
                var (fx, fy) = host.Btl.UnitFoot(host._units[order[k]]);
                to = (fx + Rand(21) - 10, fy + 19 * 0.8, 40);
            }
            int want = done ? 0 : S2Dir16(S2Board(cur), S2Board(to));
            Sound(1908, tick);
            // 돌기 — 한 틱짜리 394 조각을 한 칸씩 이어 붙인다.
            while (d != want)
            {
                d = (d + 1) % 16;
                var b = S2Board(cur);
                Put(394, S2BladeMotion(d), tick, b.X, b.Y, life: 1, mirror: d >= 9);
                tick += 2;
            }
            if (done) break;
            Sound(1909, tick);
            var fly = S2Fly(cur, to, 15, 1.6);
            int go = tick + 1, arrive = go + fly.Count;
            Along(394, S2BladeMotion(d), go, AsPath(fly.Prepend(S2Board(cur)).ToList()), life: fly.Count + 1, mirror: d >= 9);
            var tb = S2Board(to);
            Later(go, () => host.Btl.CenterOn(tb.X, tb.Y));
            cur = to;
            tick = arrive + 1;
            if (home) continue;
            int ti = order[k];
            Sound(1910, arrive);
            HitAt(arrive + 2, ti, hit);
            for (int n = 0; n < 10; n++) Debris(192, 0, arrive + 1 + Rand(5), tb.X, tb.Y, 10, 0.9);
            // 지나치기 — 설화난영참의 표(보이는 방향 16칸)대로 80, 높이는 그대로.
            var (ox, oy) = PastOffsets[d];
            (double X, double Y, double Z) past = (to.X + ox, to.Y + oy * 0.8, to.Z);
            var slide = S2Fly(to, past, 16, 0.86);
            Along(394, S2BladeMotion(d), arrive + 1, AsPath(slide.Prepend(tb).ToList()), life: slide.Count + 1, mirror: d >= 9);
            cur = past;
            tick = arrive + 1 + slide.Count + 1;
            last = Math.Max(last, arrive + 1 + 5 + Length(192, 0));
        }
        Sound(1911, tick);
        var fall = S2Fly(high, low, 10, 0.9);
        Along(394, 0, tick + 1, AsPath(fall.Prepend(S2Board(high)).ToList()), life: fall.Count + 2);
        Later(tick + 1, () => host.Btl.CenterOnUnit(user));
        return Math.Max(last, tick + 1 + fall.Count + 2);
    }

    /// <summary>
    /// 새틀라이트 어택(291) — 원본은 먼저 카메라를 겨눈 칸으로 옮기고(단계 0 · 1) 다음 틱에 시각표를 깐다. 시전자는 정면을 보고 파트1 동작 24, 기운 897(192틱).
    /// 대상마다 20틱 간격으로 398:0(z+40) → 하늘에서 빛기둥 397(땅에서 보이는 위 끝까지 50 마다) · 399:0 · 277:0 이 내리꽂혀 한 번 친다.
    /// </summary>
    private int S2Satellite(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        const int Stage = 2;   // 단계 0 · 1 — 닻 하나가 끝날 때까지(두 틱)
        host.Btl.CenterOn(aim.X, aim.Y);
        Later(Stage + 1, () => { user.Facing = Facing.Down; Act(user, 24, 5); });
        int last = S2Aura(Stage + 10, 192);
        int top = (int)aim.Y - host.ViewHeight / 2;
        for (int i = 0; i < targets.Count; i++)
        {
            int ti = targets[i];
            var (tx, ty) = host.Btl.UnitFoot(host._units[ti]);
            int s = Stage + 20 * i;
            Sound(1923, s);
            Put(398, 0, s, tx, ty - 40 * 0.6);
            int e = s + Length(398, 0);
            Sound(1924, e);
            int height = Math.Max(0, (int)((ty - top) / TileH)) * 100;
            for (int z = 0; z < height; z += 50) Put(397, 0, e + 1, tx, ty - z * 0.6);
            Put(399, 0, e + 1, tx, ty);
            Put(277, 0, e + 1, tx, ty);
            HitAt(e + 2, ti, hit);
            last = Math.Max(last, e + 1 + Math.Max(Length(397, 0), Math.Max(Length(399, 0), Length(277, 0))));
        }
        return last + 1;
    }

    /// <summary>
    /// 마룡광아탄(296) — 대상 쪽을 보고 파트1 동작 26. 대상 둘레에 연기 173 쉰이 솟고, 25틱에 시전자 666:0 · 소리 2070, 대상 앞에 용 아가리 764 셋
    /// (가운데 25틱 · 옆 둘 45틱). 범위 칸마다 보이지 않는 이동기가 시전자에서 빠르기 4 로 가(30틱, 시전자의 줄 밖 칸은 45틱 늦게) 닿으면 766:0 이 터지며 친다.
    /// </summary>
    private int S2Maryong(List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        var (hx, hy) = _home;
        var user = _user;
        var t = TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        int last = Act(user, 26, 8);
        Sound(2025, 15);
        for (int n = 0; n < 50; n++)
            Rise(173, Rand(2) == 0 ? 0 : 6, 15 + Rand(30), t.X + Rand(41) - 20, t.Y + (Rand(41) - 20) * 0.8 - Rand(20) * 0.6, 15, 0.9);
        Put(666, 0, 25, hx, hy);
        Sound(2070, 25);
        for (int e = -1; e <= 1; e++)
        {
            int at = e == 0 ? 25 : 45;
            var (m, x, y, mirror) = dir switch
            {
                0 => (0, t.X + 40 * e, t.Y, false),
                2 => (2, t.X + 40 * e, t.Y, false),
                1 => (1, t.X, t.Y + 40 * e * 0.8, false),
                _ => (1, t.X, t.Y + 40 * e * 0.8, true),
            };
            Put(764, m, at, x, y, mirror: mirror);
            last = Math.Max(last, at + Length(764, m));
        }
        int uc = user.Col, ur = user.Row;
        bool vertical = dir is 0 or 2;
        int burst = S2CellBursts(targets, cells, c => (vertical ? c.Col == uc : c.Row == ur) ? 30 : 45, 4, 1, false, 766, hit);
        return Math.Max(last, burst) + 1;
    }

    /// <summary>
    /// 플라즈마 슬래쉬(295) — 대상 쪽을 보고 파트1 동작 17, 14틱에 소리 1917. 범위 칸마다 보이지 않는 이동기가 시전자에서 빠르기 1 · 틱마다 +0.5 로 가
    /// 닿으면 그 칸에 724:0 이 터지며 친다(먼 칸일수록 늦다). 24틱에 카메라가 겨눈 쪽으로.
    /// </summary>
    private int S2Plasma(List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        var (hx, hy) = _home;
        var user = _user;
        var t = TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        int last = Act(user, 17, 8);
        Sound(1917, 14);
        Later(24, () => host.Btl.CenterOn(t.X, t.Y));
        int burst = S2CellBursts(targets, cells, _ => 0, 1, 0.5, true, 724, hit);
        return Math.Max(last, burst) + 1;
    }

    /// <summary>
    /// 혈랑마혼(292) — 시전자 오른쪽 위(x+30, z+140)에서 칼빛 536:0 이 왼쪽 아래로 내려긋고(빠르기 10 × 0.8, 잔상 536:3 36틱), 시전자는 정면을 보고
    /// 파트1 동작 9 를 36번. 그 뒤 불 소용돌이 230 과 함께 대상까지 돌진하고(20 × 1.8), 옆 40 에 반투명한 분신 둘이 나란히 달린다(10 × 1.1).
    /// 셋 모두 지나는 칸의 적을 친다(<c>0x100769b0</c>). 닿으면 돌진 자세 두 번 → 동작 21, 시전자는 겨눈 칸에 남는다(원본 그대로).
    /// </summary>
    private int S2Hyeollang(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var (hx, hy) = _home;
        var user = _user;
        var t = aim;   // 겨눈 빈 칸(+0x31 = 7)
        int dir = Dir4(t.X - hx, t.Y - hy);
        (double X, double Y, double Z) from = (hx + 30, hy, 140);
        var stroke = S2Fly(from, (hx - 30, hy, 0), 10, 0.8);
        var strokePath = AsPath(stroke.Prepend(S2Board(from)).ToList());
        Along(536, 0, 0, strokePath, life: Length(536, 0));
        Along(536, 3, 0, strokePath, life: 36);

        user.Facing = Facing.Down;
        int go = Act(user, 9, 5, 36) + 1;
        // 불 소용돌이 — 보는 쪽마다 모션 · 자리가 다르다(비연참과 같은 표).
        var (sm, sx, sy, mirror) = dir switch
        {
            0 => (0, hx, hy, false), 1 => (1, hx, hy, false),
            2 => (0, hx, hy + 20 * 0.8, false), _ => (1, hx + 20, hy, true),
        };
        Put(230, sm, go, sx, sy, mirror: mirror);
        var charge = Move(_home, t, 20, 1.8);
        Later(go, () => { user.Facing = FacingOf4(dir); Act(user, 18, 1, repeat: 0); host.Btl.CenterOn(t.X, t.Y); });
        Carry(go, charge);
        // 분신 둘 — 시전자 그림(파트1 동작 18 의 모션), 섞기 13 · 단계 4 라 반쯤 비친다.
        int body = user.Data?.SpriteId ?? 0;
        bool clones = body > Part1Game.ObsBase && host.UiFor(body) != null;
        var (bm, bmirror) = BodyMotion(18, dir);
        var lanes = new List<List<(double X, double Y)>> { charge };
        foreach (int o in new[] { -40, 40 })
        {
            (double X, double Y) shift = dir is 0 or 2 ? (o, 0) : (0, o * 0.8);
            (double X, double Y) a = (hx + shift.X, hy + shift.Y), b = (t.X + shift.X, t.Y + shift.Y);
            var lane = Move(a, b, 10, 1.1);
            lanes.Add(lane);
            if (clones) Along(body, bm, go, AsPath(lane.Prepend(a).ToList()), life: lane.Count + 1, mirror: bmirror, fadeFrom: 0.5, fadeTo: 0.5, rawObs: true);
        }
        // 지나는 칸 — 세 길 가운데 대상 발에 반 칸(22px) 안으로 가장 먼저 드는 틱. 어느 길에도 안 들면 시전자가 닿을 때.
        foreach (int ti in targets)
        {
            var (fx, fy) = host.Btl.UnitFoot(host._units[ti]);
            int best = -1;
            foreach (var lane in lanes)
                for (int k = 0; k < lane.Count; k++)
                    if (Math.Abs(lane[k].X - fx) < 22 && Math.Abs(lane[k].Y - fy) < 22) { if (best < 0 || k < best) best = k; break; }
            HitAt(go + (best < 0 ? charge.Count - 1 : best) + 1, ti, hit);
        }
        int arrive = go + charge.Count;
        // 닿은 뒤 돌진 자세 두 번 → 동작 21. 원본은 이동기(0x100816d0)가 시전자를 그 칸에 내려놓는다 — 끝날 때까지 그 자리에 붙들고, 판 자리는 전투가 옮긴다.
        Later(arrive, () => Act(user, 18, 1, 2));
        int swing = arrive + 15;
        Later(swing, () => Act(user, 21, 8));
        int last = swing + ActionTicks(user, 21, 8);
        Carry(arrive, Enumerable.Repeat(t, last - arrive + 1).ToList());
        return Math.Max(last + 1, Length(536, 0));
    }

    /// <summary>
    /// 소드브레이크(363) — 시전자는 정면을 보고 파트1 동작 24, 기운 897(96틱). 대상마다 10틱 간격으로 머리 위(z+160)에 칼 535:0 이 나타나
    /// z+80 으로 떨어지고(2 × 1.6), z+20 에 꽂혀(535:2, 20틱) 돌 367 · 흙 173 이 튄다. 꽂힘이 끝나면 535:3 · 10:0(친다) · 48:0 과 파편 365 · 368.
    /// </summary>
    private int S2SwordBreak(List<int> targets, Action<int> hit)
    {
        var user = _user;
        user.Facing = Facing.Down;
        Act(user, 24, 5);
        int last = S2Aura(10, 96);
        for (int i = 0; i < targets.Count; i++)
        {
            int ti = targets[i];
            var (tx, ty) = host.Btl.UnitFoot(host._units[ti]);
            int d = 10 * i;
            Sound(1921, d);
            Put(535, 0, d, tx, ty - 160 * 0.6);
            int f0 = d + Length(535, 0) + 1;
            (double X, double Y, double Z) up = (tx, ty, 160);
            var fall = S2Fly(up, (tx, ty, 80), 2, 1.6);
            Along(535, 1, f0, AsPath(fall.Prepend(S2Board(up)).ToList()), life: fall.Count + 1);
            int stuck = f0 + fall.Count + 1, m2End = stuck + 20;
            Put(535, 2, stuck, tx, ty - 20 * 0.6, life: 20);
            for (int n = 0; n < 20; n++)
                Rise(367, Rand(3), stuck + 1 + Rand(30), tx + Rand(41) - 20, ty + (Rand(41) - 20) * 0.8, Rand(10) + 5, 0.95);
            for (int n = 0; n < 10; n++) Debris(173, 6, stuck + 1 + Rand(5), tx, ty, Rand(3) + 3, 0.9);
            int h = m2End + 1;
            Put(535, 3, h, tx, ty - 20 * 0.6);
            Put(10, 0, h, tx, ty);
            Put(48, 0, h, tx, ty);
            HitAt(h + 1, ti, hit);
            for (int n = 0; n < 5; n++) S2Burst(365, Rand(4), h + 1 + Rand(10), tx, ty, 7, -0.15);
            for (int n = 0; n < 10; n++) Debris(368, Rand(4), h + 1 + Rand(10), tx, ty, 25, 0.8);
            last = Math.Max(last, h + 12 + Math.Max(Length(10, 0), Math.Max(Length(365, 0), Length(368, 0))));
        }
        return last + 1;
    }

    // ---- 도움(Sword2) ----

    /// <summary>판 픽셀 발 자리 + 높이 z(월드) → 그리는 자리.</summary>
    private static (double X, double Y) S2Board((double X, double Y, double Z) p) => (p.X, p.Y - p.Z * 0.6);

    /// <summary>
    /// 곧은 이동기(<c>0x100780f0</c>)를 높이까지 — 월드(x, y ÷ 0.8, z)에서 빠르기만큼 다가가고 빠르기에 곱수를 곱하거나(<paramref name="add"/> 면 더해) 1~40 으로 묶는다.
    /// 틱마다의 그리는 자리(첫 칸 = 출발 다음 틱, 마지막 칸 = 닿은 틱).
    /// </summary>
    private static List<(double X, double Y)> S2Fly((double X, double Y, double Z) from, (double X, double Y, double Z) to, double speed, double factor, bool add = false)
    {
        var points = new List<(double X, double Y)>();
        double x = from.X, y = from.Y / 0.8, z = from.Z, tx = to.X, ty = to.Y / 0.8, tz = to.Z;
        for (int guard = 0; guard < 600; guard++)
        {
            double dx = tx - x, dy = ty - y, dz = tz - z, far = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (far <= speed) { points.Add(S2Board(to)); break; }
            (x, y, z) = (x + dx / far * speed, y + dy / far * speed, z + dz / far * speed);
            points.Add((x, y * 0.8 - z * 0.6));
            speed = Math.Clamp(add ? speed + factor : speed * factor, 1, 40);
        }
        if (points.Count == 0) points.Add(S2Board(to));
        return points;
    }

    /// <summary>보이는 방향 16칸(0 = 위 · 4 = 오른쪽 …, <c>0x10019e40</c>) — 화면 자리로 잰다.</summary>
    private static int S2Dir16((double X, double Y) a, (double X, double Y) b) =>
        ((int)Math.Round(Math.Atan2(b.X - a.X, -(b.Y - a.Y)) / (Math.PI / 8)) + 16) % 16;

    /// <summary>큰 칼 394 의 모션 — 방향 0~8 은 그대로, 9~15 는 16 − d 를 뒤집어.</summary>
    private static int S2BladeMotion(int dir16) => dir16 < 9 ? dir16 : 16 - dir16;

    /// <summary>시전자 기운 897 — <paramref name="at"/> 틱에 0(한 번) → 1(<paramref name="hold"/> 틱 되풀이) → 2(한 번). 끝나는 틱.</summary>
    private int S2Aura(int at, int hold)
    {
        var (hx, hy) = _home;
        int m0 = Length(897, 0);
        Put(897, 0, at, hx, hy - 20 * 0.6);
        Put(897, 1, at + m0 + 1, hx, hy - 20 * 0.6, life: hold);
        Put(897, 2, at + m0 + hold + 2, hx, hy - 20 * 0.6);
        return at + m0 + hold + 2 + Length(897, 2);
    }

    /// <summary>터지는 파편(<c>0x10079780</c>)에서 빠르기를 틱마다 더하는 쪽(모드 0) — 둘레 ±300 의 아무 점으로.</summary>
    private void S2Burst(int obs, int motion, int tick, double x, double y, double speed, double step)
    {
        double tx = x + Rand(601) - 300, ty = y + (Rand(601) - 300) * 0.8 - Math.Max(0, Rand(201) - 100) * 0.6;
        var points = Move((x, y), (tx, ty), speed, step, 40, 0.5, add: true);
        int len = Length(obs, motion);
        Along(obs, motion, tick, AsPath(points.Take(len).Prepend((x, y)).ToList(), 2));
    }

    /// <summary>
    /// 범위 칸마다 보이지 않는 이동기(<c>0x1007cae0</c>)를 시전자에서 그 칸으로 보내고(<paramref name="delay"/> 틱 뒤 출발), 닿으면 그 칸에
    /// <paramref name="obs"/>:0 을 세워 그 다음 틱에 그 칸의 대상을 친다. 칸이 없으면 대상들의 칸. 끝나는 틱.
    /// </summary>
    private int S2CellBursts(List<int> targets, List<(int Col, int Row)> cells, Func<(int Col, int Row), int> delay, double speed, double factor, bool add, int obs, Action<int> hit)
    {
        var (hx, hy) = _home;
        var list = cells.Count > 0 ? cells : [.. targets.Select(ti => (host._units[ti].Col, host._units[ti].Row)).Distinct()];
        var land = new List<((int Col, int Row) Cell, (double X, double Y) At, int Tick)>();
        int last = 0;
        foreach (var c in list)
        {
            var at = CellFoot(c.Col, c.Row);
            var fly = S2Fly((hx, hy, 0), (at.X, at.Y, 0), speed, factor, add);
            int tick = delay(c) + fly.Count;
            Put(obs, 0, tick + 1, at.X, at.Y);
            land.Add((c, at, tick));
            last = Math.Max(last, tick + 1 + Length(obs, 0));
        }
        foreach (int ti in targets)
        {
            var u = host._units[ti];
            var (fx, fy) = host.Btl.UnitFoot(u);
            int tick = land.Count == 0 ? 1
                : land.FindIndex(l => l.Cell.Col == u.Col && l.Cell.Row == u.Row) is >= 0 and var i ? land[i].Tick
                : land.OrderBy(l => Dist(l.At, (fx, fy))).First().Tick;
            HitAt(tick + 2, ti, hit);
        }
        return last;
    }
}
