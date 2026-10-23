using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 의 썬더스탐(공격 161, 지리학자 · 프로페서의 마법) · 헬 스트라이크(288, 유성검) · 아수라파천무(277, 킹 = 철가면)(사용자 요청 menu-89).
/// </summary>
/// <remarks>
/// <para>핸들러: 161 <c>0x1004dc80</c>(앞머리 <see cref="SummonPrep"/>) · 288 <c>0x1005cc30</c> · 277 <c>0x10053aa0</c>(앞머리 <see cref="SwordPrep"/>)
/// (볼트 분석-파트1-검기술 b).</para>
/// <para>원본과 다르게 한 것: 썬더스탐은 원본에서 판 위의 <b>모든 적</b>을 치는데 여기서는 범위 안의 적만 친다. 아수라파천무는 시전자 자신의 그림
/// (철가면 Obs 605 의 모션 132~138)으로 분신을 만든다 — 그 모션이 없는 모습이면 분신 없이 시전자가 그 자리에서 자세만 잡는다.
/// 분신이 뿌리는 불티(틱마다 140)는 20 으로 줄였다.</para>
/// </remarks>
internal sealed partial class Part1Skill
{
    internal const int ThunderWork = 20161, HellStrikeWork = 20288, AsuraWork = 20277;

    private bool Visible((double X, double Y) p)
    {
        var (l, t, r, b) = View;
        return p.X >= l && p.X < r && p.Y >= t && p.Y < b;
    }

    /// <summary>파트1 동작을 한 번 하는 틱 수(걸지는 않는다) — 그 동작이 없으면 <paramref name="fallback"/> 의 것.</summary>
    private int ActTicks(UnitState u, int part1Action, int fallback)
    {
        if (!host._sprites.TryGetValue(u.ChrCode, out var sprite)) return 20;
        int action = Part2Action(part1Action), use = sprite.RawClip(action * 3) != null ? action : fallback;
        return Math.Max(3, (int)Math.Round(sprite.ActionSeconds(use, u.Facing) * TicksPerSecond));
    }

    /// <summary>
    /// 썬더스탐 — 시전자 둘레의 세 원(반지름 R/3+200 · R/2+200 · R+200)을 번개 기둥 둘이 넓어지며 돌고, 대상마다 50~249틱 사이 아무 때 번개(75 · 379 · 71:1 · 277)가 떨어져 한 번 친다.
    /// 보이는 판의 아무 칸에도 번개 쉰 개가 떨어지고 거기 선 적도 맞는다. 판 밖의 대상은 바로 맞는다.
    /// </summary>
    private int Thunder(List<int> targets, Action<int> hit)
    {
        var (hx, hy) = _home;
        var (left, top, right, bottom) = View;
        Sound(194, 0);
        double r = Math.Max(hx - left, right - hx);
        int last = 0;
        foreach (var (dx, dy, start) in new[] { (50.0, 50.0, -Math.PI / 2), (-50.0, -50.0, Math.PI / 2) })
        {
            // 원마다 8r 점 가운데 200번째마다 마디 — 세 원을 잇달아 시계 반대로(0x10009e10 · 0x10080ba0, 빠르기 1 × 1.1, 최대 40).
            var nodes = new List<(double X, double Y)>();
            foreach (double radius in new[] { r / 3 + 200, r / 2 + 200, r + 200 })
            {
                int count = Math.Max(1, (int)(8 * radius / 200));
                for (int k = 0; k < count; k++)
                {
                    double a = start - k * 2 * Math.PI / count;
                    nodes.Add((hx + dx + radius * Math.Cos(a), hy + dy * 0.8 + radius * Math.Sin(a) * 0.8));
                }
            }
            var path = new List<(double X, double Y)>();
            (double X, double Y) at = (hx, hy);
            double speed = 1;
            foreach (var node in nodes)
            {
                var seg = Move(at, node, speed, 1.1);
                path.AddRange(seg);
                speed = Math.Min(40, speed * Math.Pow(1.1, seg.Count));
                at = node;
            }
            for (int k = 0; k < 4; k++)
                Along(75, 0, 0, [.. AsPath(path, 2).Select(p => (p.Tick, p.X, p.Y - 255 * k))], life: path.Count, flicker: true);
            last = Math.Max(last, path.Count);
        }
        void Strike(int d, double x, double y)
        {
            int life = Rand(10) + 5;
            Bolt(d, x, y, life);
            Put(379, 0, d, x, y, life: life);
            Put(71, 1, d, x, y);
            Put(277, 0, d, x, y);
            for (int n = 0; n < 3; n++) Debris(77, 0, d + 5, x, y, 10, 0.9, 12);
            last = Math.Max(last, d + Math.Max(Length(277, 0), Length(71, 1)));
        }
        foreach (int ti in targets)
        {
            var p = host.Btl.UnitFoot(host._units[ti]);
            if (!Visible(p)) { HitAt(1, ti, hit); continue; }
            int d = Rand(200) + 50;
            Strike(d, p.X, p.Y);
            HitAt(d + 1, ti, hit);
        }
        for (int n = 0; n < 50; n++)
        {
            int bx = left + Rand(right - left), by = top + Rand(bottom - top), col = bx / TileW, row = host.RowAt(bx, by);
            if ((uint)col >= host.Cols || (uint)row >= host.Rows) continue;
            var (cx, cy) = CellFoot(col, row);
            int d = Rand(200) + 50;
            Strike(d, cx, cy);
            for (int i = 0; i < host._units.Length; i++)
                if (host._units[i] is { Alive: true, OnField: true } u && u.Col == col && u.Row == row && u != _user && BattleScene.SeesAsFoe(_user, u))
                    HitAt(d + 1, i, hit);
        }
        return last + 2;
    }

    /// <summary>
    /// 헬 스트라이크 — 보이는 대상마다 40틱 간격으로 시전자가 대상 쪽으로 땅을 치고(파트1 동작 26), 대상 자리에 충격(124:0·18)과 화면 위까지 솟는 불기둥(10:1),
    /// 불꽃(832:2→3)과 운석 셋(228 — 떨어져 한 번 튀고 터진다)이 인다. 충격 때 한 번 친다. 판 밖의 대상은 바로 맞는다.
    /// </summary>
    private int HellStrike(List<int> targets, Action<int> hit)
    {
        var (hx, hy) = _home;
        var user = _user;
        var (_, top, _, _) = View;
        int last = 0, i = 0;
        foreach (int ti in targets)
        {
            var target = host._units[ti];
            var (tx, ty) = host.Btl.UnitFoot(target);
            if (!Visible((tx, ty))) { HitAt(1, ti, hit); continue; }
            int at = 40 * i++;
            Sound(2025, at);
            var face = FacingOf(tx - hx, ty - hy);
            int len;
            {
                var keep = user.Facing;
                user.Facing = face;
                len = ActTicks(user, 26, 8);
                user.Facing = keep;
            }
            Later(at, () => { user.Facing = face; Act(user, 26, 8); });
            int c = at + len;
            Sound(2020, c);
            Put(124, 0, c + 2, tx, ty);
            Put(124, 18, c + 2, tx, ty);
            HitAt(c + 3, ti, hit);
            for (int j = 0; ty - 60 * j * 0.6 > top - 40; j++) Put(10, 1, c + 2 + 2 * j, tx, ty - 60 * j * 0.6, mirror: Rand(2) == 0);
            Put(832, 2, c + 2, tx, ty, life: 60);
            Put(832, 3, c + 63, tx, ty);
            for (int m = 0; m < 3; m++)
            {
                int r = Rand(3), start = c + 2 + Rand(10);
                double sx = tx + Rand(81) - 40, sy = ty + (Rand(81) - 40) * 0.8, h = (Rand(100) + 140) * 0.6;
                double bx = (Rand(41) + 20) * 2 * (Rand(2) == 0 ? -1 : 1), by = (Rand(41) + 20) * 2 * (Rand(2) == 0 ? -1 : 1) * 0.8;
                // 떨어짐(빠르기 5~9, 틱마다 +2) → 한 번 튐(같은 높이로 솟았다 옆으로 내려앉는다, 따라가는 빠르기는 반).
                int fall = 0;
                for (double z = h, v = Rand(5) + 5; z > 0; z -= v * 0.6, v += 2) fall++;
                int hop = fall * 2;
                (int, double, double)[] path = [(0, sx, sy - h), (fall, sx, sy), (fall + hop / 2, sx + bx / 2, sy + by / 2 - h * 0.6), (fall + hop, sx + bx, sy + by)];
                Along(228, r, start, path, life: fall + hop);
                for (int t = 0; t < fall + hop; t += 2)
                {
                    int k = 0;
                    while (k + 1 < path.Length && t >= path[k + 1].Item1) k++;
                    var (t0, x0, y0) = path[k];
                    var (t1, x1, y1) = path[Math.Min(k + 1, path.Length - 1)];
                    double f = t1 > t0 ? (t - t0) / (double)(t1 - t0) : 0;
                    Put(38, 0, start + t, x0 + (x1 - x0) * f, y0 + (y1 - y0) * f);
                }
                Put(228, r + 3, start + fall + hop + 2, sx + bx, sy + by);
            }
            for (int n = 0; n < 10; n++) Debris(38, 0, c + 2 + Rand(5), tx + Rand(41) - 20, ty + Rand(41) - 20, Rand(10) + 10, 0.9);
            last = Math.Max(last, c + 63 + Length(832, 3));
        }
        return Math.Max(last, 10);
    }

    /// <summary>
    /// 아수라파천무 — 시전자가 사라지고 분신(시전자 그림 132 → 137 …)이 기를 모은 뒤, 보이는 대상마다 30틱 간격으로 충격 · 빛기둥(526:1)이 서고,
    /// 분신이 모든 대상에게 날아가 넋(38)을 머리 위 구슬로 모은다. 구슬이 터지고 시전자가 화면 위로 솟으면 화면을 가로지르는 줄무늬 · 분신 · 칼금(696)이 지나고,
    /// 화면이 바랜 뒤 모든 대상이 한꺼번에 한 번 맞는다. 끝에 시전자가 내려앉으며 다시 나타난다.
    /// </summary>
    private int Asura(List<int> targets, Action<int> hit)
    {
        var (hx, hy) = _home;
        var user = _user;
        int hw = host.ViewWidth / 2, hh = host.ViewHeight / 2;
        var (left, top, right, bottom) = View;
        int body = user.Data?.SpriteId ?? 0;
        bool clones = body > Part1Game.ObsBase && host.UiFor(body)?.Clip(252) != null;
        // 분신 모션 — 파트1 모션 m 은 이 게임에서 m + 120 에 있다(16 이상의 동작을 40 뒤로 옮겼다).
        void Clone(int m, int tick, (int, double, double)[] path, int? life = null, bool mirror = false, double fadeFrom = 1, double fadeTo = 1, bool screen = false)
        {
            if (clones) Along(body, m + 120, tick, path, life, mirror, sounds: !screen, screen: screen, fadeFrom: fadeFrom, fadeTo: fadeTo, rawObs: true);
        }
        int Len(int m) => clones ? Math.Max(1, host.UiFor(body)!.MotionLength(m + 120)) : 30;
        int Motion(int part1Action, Facing f) => part1Action * 3 + (f == Facing.Up ? 0 : f == Facing.Down ? 2 : 1);

        if (clones) Later(1, () => user.Fade = 0);
        else Act(user, 9, 5, 0);
        int a1 = 0, a2 = a1 + Len(132) + 1, a3 = a2 + Len(137) + 2;
        Clone(132, a1, [(0, hx, hy)]);
        Clone(137, a2, [(0, hx, hy)]);
        var vis = targets.Where(t => Visible(host.Btl.UnitFoot(host._units[t]))).ToList();
        if (vis.Count > 0) Sound(2085, a3 - 2);
        int markEnd = a3;
        for (int k = 0; k < vis.Count; k++)
        {
            var target = host._units[vis[k]];
            var (tx, ty) = host.Btl.UnitFoot(target);
            int at = a3 + 30 * (k + 1) + 2;
            Put(124, 0, at, tx, ty);
            Put(124, 18, at, tx, ty);
            Later(at, () => Act(target, 5, 2, 5));
            int j = 0;
            for (; ty - 60 * j * 0.6 > top - 40; j++) Put(526, 1, at + 2 * j, tx, ty - 60 * j * 0.6, mirror: Rand(2) == 0);
            for (int n = 0; n < 10; n++) Debris(38, 0, at + Rand(5), tx + Rand(41) - 20, ty + Rand(41) - 20, Rand(10) + 10, 0.9);
            Put(832, 2, at, tx, ty, life: 250);
            Put(832, 3, at + 251, tx, ty);
            markEnd = at + 2 * Math.Max(0, j - 1) + 45;
        }
        int tau = markEnd;
        // 구슬 자리 Σ — 시전자 위 화면 위쪽.
        (double X, double Y) orb = (hx, Math.Max(top + 70, hy - 200));
        Sound(2086, tau);
        Put(518, 0, tau + 2, hx, hy - 40 * 0.6);
        Put(519, 0, tau + 2, hx, hy - 40 * 0.6, mirror: true);
        int pi = tau + 2;
        bool first = true;
        foreach (int ti in targets)
        {
            var target = host._units[ti];
            var (tx, ty) = host.Btl.UnitFoot(target);
            var f = FacingOf(tx - hx, ty - hy);
            var dash = Move(_home, (tx, ty), 1, 1.2);
            Clone(0, tau + 2, AsPath(dash), life: dash.Count, mirror: f == Facing.Right, fadeFrom: 0.5, fadeTo: 0.5);
            if (clones) _fx[^1] = _fx[^1] with { Motion = Motion(Part2Action(18), f) };
            int d = tau + 2 + dash.Count;
            if (first) { Sound(2087, d); first = false; }
            Put(83, 0, d + 2, tx, ty, mirror: Rand(2) == 0, sounds: true);
            Later(d + 2, () => Act(target, 5, 2, 10));
            var puff = Circle(tx, ty, 40, 100, 1, 1.06, Rand(2) == 0, 0);
            Along(38, 0, d + 2, [.. puff.Select(p => (p.Tick, p.X, p.Y - p.Tick * 0.6))], life: 100);
            var last = puff[^1];
            (double X, double Y) from = (last.X, last.Y - 100 * 0.6);
            var soul = CurveWalk([from, ((from.X + orb.X) / 2 + Rand(81) - 40, Math.Min(from.Y, orb.Y) - 60), orb], 1, 1.04, 40);
            Along(38, 0, d + 103, AsPath(soul, 2), life: soul.Count);
            pi = Math.Max(pi, d + 103 + soul.Count);
        }
        Sound(2088, pi);
        int b1 = pi + 2, b2 = b1 + Length(696, 9) + 2, b3 = b2 + 22;
        Put(696, 9, b1, orb.X, orb.Y);
        Put(696, 10, b2, orb.X, orb.Y, life: 20);
        Put(696, 11, b3, orb.X, orb.Y);
        (double X, double Y) sky = (hx, top - 200);
        var m1 = Move(orb, sky, 1, 1.2);
        Along(696, 8, b3, AsPath(m1), life: m1.Count);
        Clone(133, a3, [(0, hx, hy)], life: Math.Max(1, b2 + 20 - a3));
        Put(696, 12, a3, hx, hy, life: Math.Max(1, b1 + Length(696, 9) - a3));
        int a5 = b2 + 22;
        Clone(134, a5, [(0, hx, hy)]);
        int a6 = a5 + Len(134) + 2;
        var leap = Move(_home, sky, 1, 1.4);
        Clone(135, a6, AsPath(leap), life: Len(135));
        var m3 = Move(_home, sky, 1, 1.6);
        Along(696, 12, b1 + Length(696, 9) + 2, AsPath(m3), life: m3.Count);
        int sigma = a6 + Len(135);

        // 화면 — 비스듬한 줄무늬 쉰 · 따라가는 696:0 · 분신 136(불티를 뿌린다) · 칼금 28칸 · 화면이 바랜다.
        Sound(2089, sigma);
        for (int n = 0; n < 50; n++)
        {
            var path = Move((hw + Rand(2 * hw), -hh - Rand(2 * hh)), (-hw - Rand(2 * hw), hh + Rand(2 * hh)), Rand(2) + 1, 1.05);
            Along(696, 4, sigma + 2 + Rand(50), AsPath(path, 3), life: path.Count, screen: true);
        }
        (double X, double Y) s0 = (hw + 256, -hh - 256), s1 = (-hw - 256, hh + 256);
        var chase = Move(s0, s1, 10, 1.4, 70);
        Along(696, 0, sigma + 82, AsPath(chase), life: chase.Count, screen: true);
        var streak = Move(s0, s1, 10, 1.6, 70);
        Clone(136, sigma + 92, AsPath(streak), life: streak.Count, screen: true);
        for (int t = 0; t < streak.Count; t++)
        {
            var p = streak[t];
            for (int n = 0; n < 20; n++)
            {
                int a = (n % 8 + 1) * 50, b = (8 - n % 8) * 50;
                Along(862, Rand(17), sigma + 92 + t + Rand(5), [(0, p.X + Rand(a), p.Y - Rand(b))], screen: true);
            }
            for (int n = 0; n < 2; n++) Along(696, 13, sigma + 92 + t + Rand(10), [(0, p.X + Rand(401) - 200, p.Y + Rand(401) - 200)], life: 32, screen: true);
        }
        for (int k = 0; k < 28; k++)
        {
            double sx = hw - 24 * k * (hw / 320.0), sy = -hh + 18 * k * (hh / 240.0);
            int at = sigma + 172 + (k + 1) / 5;
            Along(696, 5, at, [(0, sx, sy)], screen: true);
            Along(696, 6, at + Length(696, 5) + 1, [(0, sx, sy)], life: 3, screen: true);
            Along(696, 7, at + Length(696, 5) + 5, [(0, sx, sy)], screen: true);
        }
        int w = sigma + 242;
        _whiteouts.Add((At(w), 6, 0xFFFFFF));
        foreach (int ti in targets) HitAt(w + 3, ti, hit);
        // 돌아옴 — 시전자 그림이 위에서 내려오며 짙어지고(1 → 8), 다 내려오면 다시 나타나 맞는 자세(파트1 동작 5)를 한 번 한다.
        var land = Move((hx, hy - 100 * 0.6), _home, 15, 0.9);
        Clone(0, w + 2, AsPath(land), life: Math.Max(21, land.Count), fadeFrom: 0.1, fadeTo: 1);
        if (clones) _fx[^1] = _fx[^1] with { Motion = 2 };
        int back = w + 2 + Math.Max(21, land.Count) + 2;
        Later(back, () => { user.Fade = 1; Act(user, 5, 2); });
        return back + 20;
    }
}
