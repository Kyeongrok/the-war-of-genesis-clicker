namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 소환 헬카이트(345) · 어비스 네레이드(346) · 머스펠 헤임(347) — 진짜 핸들러 <c>0x10064330</c> · <c>0x10065380</c> · <c>0x10065e70</c>
/// (공격 번호 0x150 위는 표 <c>0x10043684</c> 로 간다 — 처음에 잘못 읽어 폭풍검 · 비연참이 나갔다, menu-92). 볼트 분석-파트1-소환-c.
/// </summary>
/// <remarks>
/// 셋 다 화면에 붙는 소환수 그림(640×480 화면 픽셀 — 여기서는 가운데 기준으로 옮긴다) 아래에서 판 위 이펙트가 돈다. 카메라 · 화면 멈춤은 없다.
/// 원본과 다르게 한 것: 헬카이트의 불 카펫은 보이는 칸 전부인데 넓은 화면에서는 칸이 많아 2칸에 하나만 불을 놓는다. 1/20 로 붙는 연기 사슬은 뺐다.
/// </remarks>
internal sealed partial class Part1Skill
{
    /// <summary>어비스 네레이드는 원본대로 대상마다 얼음 셋이 한 번씩 친다(세 번) — 다른 파트1 기술은 대상마다 한 번.</summary>
    internal static bool HitsEachTime(int work) => work == NereidWork || BigHitsEachTime(work);

    /// <summary>원본 화면 픽셀(640×480) → 화면 가운데 기준.</summary>
    private static (double X, double Y) Scr(double sx, double sy) => (sx - 320, sy - 240);

    /// <summary>화면 그림 한 다리 — 곧은 이동기로 가며 그 길을 그린다. 걸린 틱을 돌려준다.</summary>
    private int Leg(int obs, int motion, int tick, (double X, double Y) from, (double X, double Y) to, double speed, double factor, double max = 40, double min = 1,
                    bool mirror = false, int ghostEvery = 0)
    {
        var path = Move(from, to, speed, factor, max, min);
        path.Insert(0, from);
        Along(obs, motion, tick, AsPath(path), life: path.Count, screen: true, mirror: mirror);
        if (ghostEvery > 0)
            for (int t = 0; t < path.Count; t += ghostEvery)
                Along(obs, motion, tick + t, [(0, path[t].X, path[t].Y)], life: 14, screen: true, mirror: mirror, fadeFrom: 0.6, fadeTo: 0.05);
        return path.Count;
    }

    /// <summary>보이는 칸 — 발 자리가 보이는 판 안의 칸들.</summary>
    private List<(int Col, int Row, double X, double Y)> VisibleCells()
    {
        var list = new List<(int, int, double, double)>();
        for (int r = 0; r < host.Rows; r++)
            for (int c = 0; c < host.Cols; c++)
            {
                var (x, y) = CellFoot(c, r);
                if (Visible((x, y))) list.Add((c, r, x, y));
            }
        return list;
    }

    /// <summary>
    /// 헬카이트 — 화면 왼쪽 위로 붉은 용(103)이 날아들어 세 번 불덩이를 화면 아래로 뱉고(뒤로 물러났다 돌아온다) 떠난 뒤, 용 그림자(671)가 화면을 비스듬히 지난다.
    /// 판에는 170틱부터 불덩이 열이 아무 칸에 떨어지고, 260틱부터 보이는 칸마다 불길(23)이 오른쪽 아래 대각선부터 번진다. 대상마다 그 열에 불이 닿을 때 한 번 친다.
    /// </summary>
    private int HellKite(List<int> targets, Action<int> hit)
    {
        int hw = host.ViewWidth / 2, hh = host.ViewHeight / 2;
        Sound(1929, 10);
        // 용 — 103:0(윗몸) · 103:1(아랫몸)을 같이 옮긴다(뒤집어서).
        int Dragon(int tick, (double X, double Y) from, (double X, double Y) to, double speed, double factor, double min = 1)
        {
            Leg(103, 0, tick, from, to, speed, factor, 40, min, mirror: true);
            return Leg(103, 1, tick, from, to, speed, factor, 40, min, mirror: true);
        }
        int t = Dragon(0, Scr(-100, -100), Scr(70, 100), 15, 0.95);
        for (int k = 1; k <= 3; k++)
        {
            Sound(2066, t);
            Leg(103, 6, t, Scr(180, 80), Scr(320 * (1 + k / 2.0), 530), 10, 1.6);
            int back = Dragon(t, Scr(70, 100), Scr(10, 30), 16, 0.8);
            int ret = Dragon(t + back, Scr(10, 30), Scr(70, 100), 1, 1.1);
            t += back + ret + 1;
        }
        Sound(2065, t);
        t += Dragon(t, Scr(70, 100), Scr(-100, -100), 2, 1.2);
        int shadow = Leg(671, 0, t, (-hw, -hh), (hw + 256, hh + 256), 5, 1.6);
        Sound(1930, t + shadow);
        int last = t + shadow;

        var cells = VisibleCells();
        if (cells.Count == 0) return last;
        int minCol = cells.Min(c => c.Col), maxCol = cells.Max(c => c.Col), minRow = cells.Min(c => c.Row), maxRow = cells.Max(c => c.Row);
        var (left, top, _, _) = View;
        for (int k = 0; k < 10; k++)
        {
            var (_, _, x, y) = cells[Rand(cells.Count)];
            var fall = Move((left - Rand(50), top - Rand(50) - 60), (x, y), 1, 1.1);
            Along(103, 6, 170 + 2 * k, AsPath(fall, 2), life: fall.Count);
            Sound(60 + Rand(2), 210 + 3 * k);
            int end = 170 + 2 * k + fall.Count;
            Sound(62, end);
            Put(103, 7, end + 1, x, y);
            last = Math.Max(last, end + 1 + Length(103, 7));
        }
        // 불 카펫 — 왼쪽 위 칸에서 땅을 타고 오는 불(10:1, 넷째 줄만 보인다)이 닿으면 그 칸에 불길 23:(r+3).
        var (sx, sy) = CellFoot(minCol, minRow);
        bool wide = cells.Count > 400;
        foreach (var (col, row, x, y) in cells)
        {
            if (wide && (col + row) % 2 == 1) continue;
            int delay = 260 + Math.Abs((maxCol - col) - (maxRow - row));
            var run = Move((sx, sy), (x, y), 10, 2, 70);
            if (row % 4 == 0) Along(10, 1, delay, AsPath(run), life: run.Count);
            int at = delay + run.Count + 1, r = Rand(3);
            Put(23, r + 3, at, x, y);
            last = Math.Max(last, at + Length(23, r + 3));
        }
        foreach (int ti in targets)
        {
            var u = host._units[ti];
            HitAt(260 + Math.Max(0, u.Col - minCol) + 2, ti, hit);
        }
        return last;
    }

    /// <summary>
    /// 어비스 네레이드 — 인어(114)가 잔상을 남기며 오른쪽에서 들어와 250틱 떠 있는 동안 손에서 물방울 400 이 화면 아래로 흩어지고, 오른쪽으로 떠난다.
    /// 판에서는 대상마다(30틱 간격) 보이는 판의 위 · 왼 · 오른 가장자리에서 얼음 셋(114:3+방향)이 날아와 물보라(51)와 함께 한 번씩 — 세 번 — 친다.
    /// </summary>
    private int Nereid(List<int> targets, Action<int> hit)
    {
        int hw = host.ViewWidth / 2, hh = host.ViewHeight / 2;
        (double X, double Y) a = Scr(320 + 320 / 3.0 + 0.5, 240 - 80);
        Sound(1931, 0);
        int enter = Leg(114, 0, 0, (hw + 256, -hh), a, 10, 1.2, ghostEvery: 2);
        var float1 = Circle(a.X, a.Y, 10, 250, 1, 1, false, -Math.PI / 2, flat: true);
        Along(114, 1, enter + 1, float1, life: 250, screen: true);
        // 물방울 — 손(−90, −50)에서 화면 아래(+10)의 아무 x 로, 높이 솟았다 휘어 떨어진다(빠르기 10 × 0.97, 최소 5).
        (double X, double Y) hand = (a.X - 90, a.Y - 50);
        for (int n = 0; n < 400; n++)
        {
            (double X, double Y) to = (n % 2 == 0 ? hand.X - Rand(526) : hand.X + Rand(426), hh + 10);
            double d = Math.Sqrt((to.X - hand.X) * (to.X - hand.X) + (to.Y - hand.Y) * (to.Y - hand.Y)), h = d + Rand((int)(2 * d / 3) + 1);
            var path = CurveWalk([hand, ((hand.X + to.X) / 2, Math.Min(hand.Y, to.Y - h) / 2 + hand.Y / 2), to], 10, 0.97, 40, 5);
            Along(65, 2, 100 + Rand(5), AsPath(path, 2), life: path.Count, screen: true);
        }
        Sound(1932, 120);
        int leave = enter + 1 + 250;
        Leg(114, 2, leave, a, (hw + 256, a.Y), 10, 1.2, ghostEvery: 2);
        int last = leave + 40;

        var (left, top, right, bottom) = View;
        for (int k = 0; k < targets.Count; k++)
        {
            int ti = targets[k];
            var (tx, ty) = host.Btl.UnitFoot(host._units[ti]);
            (double X, double Y)[] from =
            [
                (left + 20 + Rand(Math.Max(1, right - left - 40)), top + 16),
                (left + 20, top + 16 + Rand(Math.Max(1, bottom - top - 32))),
                (right - 20, top + 16 + Rand(Math.Max(1, bottom - top - 32))),
            ];
            for (int j = 0; j < 3; j++)
            {
                var start = (from[j].X, from[j].Y - 100 * 0.6);
                Sound(1933, 140 + 30 * k + 5 * j);
                int dir = Dir16(start, (tx, ty));
                var (m, mir) = dir < 9 ? (dir + 3, false) : (19 - dir, true);
                var fly = Move(start, (tx, ty), 1, 1.5);
                int at = 130 + 30 * k;
                Along(114, m, at, AsPath(fly), life: fly.Count, mirror: mir);
                int end = at + fly.Count;
                Sound(2029, end);
                Put(51, 0, end + 1, tx, ty, sounds: true);
                HitAt(end + 2, ti, hit);
                for (int n = 0; n < 10; n++)
                {
                    int r = Rand(16);
                    Put(787, r < 9 ? r : 16 - r, end + 1 + Rand(5), tx, ty, mirror: r >= 9);
                }
                last = Math.Max(last, end + 1 + Length(51, 0));
            }
        }
        return last;
    }

    /// <summary>
    /// 머스펠 헤임 — 화면 가운데 위로 촉수 마왕(211)이 짙어지며 나타나 다섯 번 떠돌다 사라진다. 판에서는 대상마다(20틱 간격) 머리 위에 불 가시 셋(211:7)이 생겨
    /// 떨어지고(211:8) 땅에서 터진 뒤(211:9), 큰 폭발(211:6)과 함께 한 번 친다.
    /// </summary>
    private int Muspel(List<int> targets, Action<int> hit)
    {
        (double X, double Y) b = Scr(320, 170);
        Sound(2003, 0);
        Screen(211, 0, 0, b.X, b.Y, life: 23);
        int t = 24;
        var at = b;
        for (int k = 0; k < 5; k++)
        {
            (double X, double Y) to = Scr(320 + Rand(401) - 200, 170 + Rand(401) - 200);
            t += Leg(211, 2, t, at, to, 10, 0.95, 40, 2) + 1;
            at = to;
        }
        Screen(211, 4, t, at.X, at.Y, life: 24);
        int last = t + 24;
        for (int i = 0; i < targets.Count; i++)
        {
            int ti = targets[i];
            var (tx, ty) = host.Btl.UnitFoot(host._units[ti]);
            Sound(2005, 70 + 20 * i);
            int ground = 0;
            for (int j = 0; j < 3; j++)
            {
                double x = tx - 20 + 20 * j, high = (100 + Rand(100)) * 0.6;
                int e1 = 70 + 20 * i + Rand(10), e1End = e1 + Length(211, 7);
                Put(211, 7, e1, x, ty - high);
                if (j == 0) Sound(2004, e1End);
                int fallTicks = Rand(10) + 5;
                double drop = 0, v = Rand(2) + 2;
                var path = new List<(int, double, double)>();
                for (int s = 0; s <= fallTicks; s++) { path.Add((s, x, ty - Math.Max(0, high - drop))); drop += v * 0.6; v = Math.Min(40, v * 1.1); }
                Along(211, 8, e1End + 1, [.. path], life: fallTicks);
                int e3 = e1End + 1 + fallTicks + 1;
                Put(211, 9, e3, x, ty);
                ground = Math.Max(ground, e3 + Length(211, 9));
            }
            Put(211, 6, ground + 1, tx, ty, sounds: true);
            HitAt(ground + 2, ti, hit);
            last = Math.Max(last, ground + 1 + Length(211, 6));
        }
        return last;
    }
}
