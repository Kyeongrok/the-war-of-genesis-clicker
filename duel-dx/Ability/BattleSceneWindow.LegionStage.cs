using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 군단기(392 · 1659~1666)의 부하 연출 — 옵시디안 ba21-bubble-reinforce-legion C절.
/// </summary>
/// <remarks>
/// 원본 핸들러는 부하(대부분 대장도)를 숨기고(<c>0x100eadb0(10000)</c>) <b>그 유닛의 Obs 로 만든 잔상</b>을 뛰게·날게·돌진하게 한 뒤
/// 끝에 다시 보인다(<c>0x100eadb0(0)</c>). 실제 칸은 안 옮긴다. 전에는 대장 쪽 이펙트(Obs 1483 등)만 나오고 부하는 가만히 서 있었다.
/// 뛰는 자리·틱은 노트 C-1 표대로 넣었고, <b>다시 보이는 때</b>는 표에서 단계 틱을 다 못 푼 군단기(1659·1661·1663)에서 가설이다.
/// 1660·1665 의 틱은 표의 단계 틱을 이어 붙인 것이다(다시 보이는 때는 가설).
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>움직이는 유닛 잔상 — 제 그림의 한 모션을 From → To 로 옮기며 그린다. 때는 게임 초.</summary>
    internal sealed record LegionGhost(UnitState Owner, int Motion, bool Mirror, double Start, double X0, double Y0, double X1, double Y1,
                                      int MoveTicks, bool Arc, int Life, double Fade0, double Fade1);

    internal readonly List<LegionGhost> _legionGhosts = [];

    /// <summary>정해 둔 때에 유닛을 숨기거나(0) 다시 보인다(1).</summary>
    internal readonly List<(double At, UnitState Unit, double Fade)> _legionFades = [];

    /// <summary>월드 세로 40px = 화면 세로 32px(칸 40×32).</summary>
    internal const double LegionYScale = (double)TileH / TileW;

    internal static bool IsLegionSkill(int work) => work is 392 or (>= 1659 and <= 1666);

    /// <summary>군단기를 쓰는 순간 — 부하 잔상과 숨김·보임을 시각표에 건다(줄을 붙들지 않는다).</summary>
    /// <returns>판정까지 기다릴 초 — 원본은 피해가 끝 무렵(다시 보이기 50틱쯤 앞)에 한 번 들어간다. 연출이 없으면 0.</returns>
    internal double StartLegionStage(UnitState leader, WorkData w, int col, int row, IReadOnlyList<int> targets)
    {
        if (!IsLegionSkill(w.Id)) return 0;
        int leaderIndex = Array.IndexOf(_units, leader);
        var followers = FollowersOf(leaderIndex).Where(f => f.OnField && f.Hp > 0).OrderBy(f => f.FormationSlot).ToList();
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"legion stage work {w.Id} leader {leader.ChrCode} followers {followers.Count}" + Environment.NewLine);
        if (followers.Count == 0) return 0;
        double t0 = _lastTime;
        int fadesBefore = _legionFades.Count;
        var (lx, ly) = UnitFoot(leader);
        var aim = (X: col * TileW + TileW / 2, Y: CellCenterY(col, row));
        int reach = Math.Max(1, RangeMaxOf(w, leader) / 4);

        double At(int tick) => t0 + tick / TicksPerSecond;
        (int Motion, bool Mirror) Pose(UnitState u, int action, Facing facing) =>
            (_sprites.TryGetValue(u.ChrCode, out var s) ? s.Clip(action, facing)?.Id ?? -1 : -1, facing == Facing.Right);
        void Ghost(UnitState u, int action, Facing facing, int startTick, (double X, double Y) from, (double X, double Y) to,
                   int moveTicks, int life, bool arc = false, double fade0 = 1, double fade1 = 1)
        {
            var (motion, mirror) = Pose(u, action, facing);
            if (motion < 0) (motion, mirror) = Pose(u, 0, facing);
            if (motion < 0) return;
            _legionGhosts.Add(new LegionGhost(u, motion, mirror, At(startTick), from.X, from.Y, to.X, to.Y, Math.Max(1, moveTicks), arc, life, fade0, fade1));
        }
        void Hide(UnitState u, int tick) => _legionFades.Add((At(tick), u, 0));
        void Show(UnitState u, int tick) => _legionFades.Add((At(tick), u, 1));
        (double X, double Y) Foot(UnitState u) { var (x, y) = UnitFoot(u); return (x, y); }
        Facing Toward((double X, double Y) from, (double X, double Y) to) =>
            Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y) ? (to.X >= from.X ? Facing.Right : Facing.Left) : (to.Y >= from.Y ? Facing.Down : Facing.Up);

        const int Run = 11, Cast = 6, Ready = 5;   // 동작 11 = 모션 33~35(돌진·날기), 6 = 18~20(시전), 5 = 15~17
        var all = followers.Append(leader).ToList();
        switch (w.Id)
        {
            case 392:                               // 사신의 분노(0x100bbf70) — 모두 반지름 1000px 로 흩어졌다가 대상마다 화면을 가로질러 치고 돌아온다
            {
                var spots = new (double X, double Y)[all.Count];
                for (int i = 0; i < all.Count; i++)
                {
                    double turn = (2 * i + (_rng.Next(20) - 10) / 15.0) / all.Count * Math.PI;
                    spots[i] = (lx + 1000 * Math.Cos(turn), ly + 1000 * Math.Sin(turn) * LegionYScale);
                    Hide(all[i], 0);
                    Ghost(all[i], Run, Toward(Foot(all[i]), spots[i]), 0, Foot(all[i]), spots[i], 20, 20);
                }
                int n = Math.Clamp(targets.Count, 1, 6);   // 대상이 많아도 여섯 번까지만 가로지른다(대기가 끝없이 길어지지 않게)
                for (int k = 0; k < n && k < targets.Count; k++)
                {
                    var m = all[k % all.Count];
                    var (tx, ty) = UnitFoot(_units[targets[k]]);
                    bool fromLeft = _rng.Next(2) == 0;
                    (double X, double Y) a = (tx + (fromLeft ? -700 : 700), ty - 80 + _rng.Next(-200, 200)), hit = (tx, ty - 80 * LegionYScale),
                                         b = (tx + (fromLeft ? 700 : -700), ty - 80 + _rng.Next(-200, 200));
                    Ghost(m, Run + 1, Toward(a, hit), 100 + 20 * k, a, hit, 60, 60);
                    Ghost(m, Run + 1, Toward(hit, b), 160 + 20 * k, hit, b, 20, 20);
                }
                int back = 100 + 20 * n + 90;
                for (int i = 0; i < all.Count; i++)
                {
                    Ghost(all[i], Run, Toward(spots[i], Foot(all[i])), back, spots[i], Foot(all[i]), 80, 80);
                    Show(all[i], back + 80);
                }
                break;
            }
            case 1659:                              // 템페스트(0x100bce20) — 부하 둘이 40틱 뛰어 대장 앞 삼각 꼭짓점에 120틱, 40틱 돌아옴
            {
                var (fx, fy) = leader.Facing switch { Facing.Up => (0, -1), Facing.Down => (0, 1), Facing.Left => (-1, 0), _ => (1, 0) };
                for (int i = 0; i < Math.Min(2, followers.Count); i++)
                {
                    int side = i == 0 ? -1 : 1, across = 40 * reach - 40, ahead = 40 * reach + 40;
                    (double X, double Y) p = (lx + fx * ahead + fy * side * across, ly + (fy * ahead + fx * side * across) * LegionYScale);
                    var f = followers[i];
                    Hide(f, 0);
                    Ghost(f, Run, Toward(Foot(f), p), 0, Foot(f), p, 40, 40, arc: true);
                    Ghost(f, Cast, leader.Facing, 40, p, p, 1, 120);
                    Ghost(f, Run, Toward(p, Foot(f)), 160, p, Foot(f), 40, 40, arc: true);
                    Show(f, 200);
                }
                break;
            }
            case 1660:                              // 회색의 잔영(0x100bde70) — 넷이 흐려져 사라졌다가 +35틱 대상 둘레 ±80px 에 나타나 +49틱에 친다
            {
                (int X, int Y)[] around = [(80, 0), (-80, 0), (0, 80), (0, -80)];
                Facing[] look = [Facing.Left, Facing.Right, Facing.Up, Facing.Down];
                for (int i = 0; i < Math.Min(4, all.Count); i++)
                {
                    var u = all[i];
                    (double X, double Y) p = (aim.X + around[i].X, aim.Y + around[i].Y * LegionYScale);
                    Hide(u, 0);
                    Ghost(u, 0, u.Facing, 0, Foot(u), Foot(u), 1, 50, fade0: 1, fade1: 0);
                    Ghost(u, 0, look[i], 35, p, p, 1, 49, fade0: 0.1, fade1: 1);
                    Ghost(u, 27, look[i], 84, p, p, 1, 35);                 // 모션 81~83 = 동작 27
                    Ghost(u, 0, u.Facing, 119, Foot(u), Foot(u), 1, 44, fade0: 0.1, fade1: 1);
                    Show(u, 163);
                }
                break;
            }
            case 1665:                              // 레드 크로스(0x100c0880) — 넷의 그림만 화면 네 점에 뜬다(유닛은 안 숨긴다): 모션 30 → 31(150틱) → 32 → 2(40틱 흐려짐)
            {
                (int X, int Y)[] points = [(-120, -80), (120, -80), (-120, 80), (120, 80)];
                int cx = _camX + ViewWidth / 2, cy = _camY + ViewHeight / 2;
                for (int i = 0; i < Math.Min(4, all.Count); i++)
                {
                    var u = all[i];
                    if (!_sprites.TryGetValue(u.ChrCode, out var sp) || sp.MotionTicks(31) <= 0) continue;
                    (double X, double Y) p = (cx + points[i].X, cy + points[i].Y);
                    int delay = _rng.Next(60), rise = Math.Max(1, sp.MotionTicks(30)), fall = Math.Max(1, sp.MotionTicks(32));
                    void Raw(int motion, int at, int life, double f0 = 1, double f1 = 1) =>
                        _legionGhosts.Add(new LegionGhost(u, motion, false, At(at), p.X, p.Y, p.X, p.Y, 1, false, life, f0, f1));
                    Raw(30, delay, rise);
                    Raw(31, delay + rise, 150);
                    Raw(32, delay + rise + 150, fall);
                    Raw(2, delay + rise + 150 + fall, 40, 1, 0);
                }
                _legionFades.Add((At(300), leader, 1));   // 판정 대기의 기준(피해는 끝 무렵) — 대장은 그대로 보인다
                break;
            }
            case 1661:                              // 강림의 밤(0x100be5a0) — 넷이 20틱 뛰어 대장 둘레 십자(±D)로, 시전 자세 250틱, 20틱 돌아옴
            {
                int d = 40 * reach;
                (int X, int Y)[] cross = [(d, 0), (-d, 0), (0, d), (0, -d)];
                Facing[] look = [Facing.Right, Facing.Left, Facing.Down, Facing.Up];
                for (int i = 0; i < Math.Min(4, all.Count); i++)
                {
                    var u = all[i];
                    (double X, double Y) p = (lx + cross[i].X, ly + cross[i].Y * LegionYScale);
                    Hide(u, 0);
                    Ghost(u, Run, Toward(Foot(u), p), 0, Foot(u), p, 20, 20, arc: true);
                    Ghost(u, Cast, look[i], 20, p, p, 1, 250);
                    Ghost(u, Run, Toward(p, Foot(u)), 270, p, Foot(u), 20, 20, arc: true);
                    Show(u, 290);
                }
                break;
            }
            case 1662:                              // 서풍의 광시곡(0x100bf170) — 여섯이 30틱 뛰어 세로 줄(80px 간격), +89틱 가로 돌진(틱당 50px, 30틱)
            {
                for (int i = 0; i < Math.Min(6, all.Count); i++)
                {
                    var u = all[i];
                    (double X, double Y) p = (lx, ly + (-180 + 80 * i) * LegionYScale);
                    bool right = i % 2 == 0;
                    (double X, double Y) far = (p.X + (right ? 1500 : -1500), p.Y);
                    Hide(u, 0);
                    Ghost(u, Run, Toward(Foot(u), p), 0, Foot(u), p, 30, 30, arc: true);
                    Ghost(u, Ready, right ? Facing.Right : Facing.Left, 30, p, p, 1, 59);
                    Ghost(u, Run, right ? Facing.Right : Facing.Left, 89, p, far, 30, 30);
                    for (int k = 1; k <= 3; k++)   // 잔상(원본 15장, 수명 19 — 여기서는 석 장)
                        Ghost(u, Run, right ? Facing.Right : Facing.Left, 89 + 2 * k, p, far, 30, 30, fade0: 0.5 - 0.12 * k, fade1: 0.1);
                    Show(u, 154);
                }
                break;
            }
            case 1663:                              // 블랙 이클립스(0x100bf7a0) — 부하가 10틱씩 엇갈려 24틱 뛰어 대장에게 흡수된다
            {
                for (int i = 0; i < Math.Min(5, followers.Count); i++)
                {
                    var f = followers[i];
                    Hide(f, 10 * i);
                    Ghost(f, Run, Toward(Foot(f), (lx, ly)), 10 * i, Foot(f), (lx, ly), 24, 24, arc: true, fade0: 1, fade1: 0);
                    Show(f, 250);
                }
                break;
            }
            case 1664:                              // 월광의 살육(0x100bfe90) — 부하 넷이 V 자 점에 나타나 대장이 보는 쪽으로 돌진(틱당 60px, 30틱)
            {
                var (fx, fy) = leader.Facing switch { Facing.Up => (0, -1), Facing.Down => (0, 1), Facing.Left => (-1, 0), _ => (1, 0) };
                (int Side, int Back)[] vee = [(-40, 40), (40, 40), (-80, 80), (80, 80)];
                for (int i = 0; i < Math.Min(4, followers.Count); i++)
                {
                    var f = followers[i];
                    (double X, double Y) p = (lx - fx * vee[i].Back + fy * vee[i].Side, ly + (-fy * vee[i].Back + fx * vee[i].Side) * LegionYScale);
                    (double X, double Y) far = (p.X + fx * 1800, p.Y + fy * 1800 * LegionYScale);
                    Hide(f, 0);
                    Ghost(f, 0, f.Facing, 0, Foot(f), Foot(f), 1, 37, fade0: 1, fade1: 0);
                    Ghost(f, Ready, leader.Facing, 30, p, p, 1, 63, fade0: 0.2, fade1: 1);
                    Ghost(f, Run, leader.Facing, 93, p, far, 30, 30);
                    Show(f, 165);
                }
                (double X, double Y) ahead = (lx + fx * 1800, ly + fy * 1800 * LegionYScale);
                Hide(leader, 85);
                Ghost(leader, Run, leader.Facing, 85, (lx, ly), ahead, 30, 30);
                Show(leader, 165);
                break;
            }
            case 1666:                              // 아지다하카 전술 MK-II(0x100c1490) — 넷이 네 쪽으로 돌진(틱당 40px, 30틱), +60틱 겨눈 칸 둘레에서 50틱 모여듦
            {
                int d = 40 * reach;
                (int X, int Y)[] dirs = [(-1, 0), (1, 0), (0, -1), (0, 1)];
                Facing[] look = [Facing.Left, Facing.Right, Facing.Up, Facing.Down];
                for (int i = 0; i < Math.Min(4, all.Count); i++)
                {
                    var u = all[i];
                    var from = Foot(u);
                    (double X, double Y) away = (from.X + dirs[i].X * 1200, from.Y + dirs[i].Y * 1200 * LegionYScale);
                    (double X, double Y) ring = (aim.X + dirs[i].X * d, aim.Y + dirs[i].Y * d * LegionYScale);
                    Hide(u, 0);
                    Ghost(u, Run, look[i], 0, from, away, 30, 30);
                    Ghost(u, Run, Toward(ring, (aim.X, aim.Y)), 60, ring, (aim.X, aim.Y), 50, 50);
                    Show(u, 150);
                }
                break;
            }
        }
        double last = _legionFades.Count > fadesBefore ? _legionFades.Skip(fadesBefore).Max(f => f.At) : t0;
        _legionStageEnd = Math.Max(_legionStageEnd, last);
        return Math.Max(0, last - t0 - 50 / TicksPerSecond);
    }

    /// <summary>숨김·보임 시각표 — 갱신 틀마다(그리기와 따로 돌아야 화면 밖에서도 멈추지 않는다).</summary>
    /// <summary>군단기 연출이 다 끝나는 때 — 행동 루틴이 그때까지 기다린다(숨은 유닛이 남은 채 다음 차례가 시작되지 않게).</summary>
    internal double _legionStageEnd;

    /// <summary>정해 둔 때에 할 일(소리·이펙트·카메라) — 숨김·보임 시각표와 같이 돈다.</summary>
    internal readonly List<(double At, Action Do)> _legionLater = [];

    internal void StepLegionFades()
    {
        for (int i = 0; i < _legionLater.Count; i++)
        {
            if (_lastTime < _legionLater[i].At) continue;
            var todo = _legionLater[i].Do;
            _legionLater.RemoveAt(i--);
            todo();
        }
        for (int i = _legionFades.Count - 1; i >= 0; i--)
        {
            var (at, unit, fade) = _legionFades[i];
            if (_lastTime < at) continue;
            if (unit.Alive || fade >= 1) unit.Fade = fade;   // 다시 보이기는 쓰러진 유닛에도 건다
            _legionFades.RemoveAt(i);
        }
    }

    /// <summary>군단기 잔상을 그리고, 숨김·보임 시각표를 돌린다 — 그리기 틀마다.</summary>
    internal void DrawLegionGhosts()
    {
        for (int i = _legionGhosts.Count - 1; i >= 0; i--)
        {
            var g = _legionGhosts[i];
            if (_lastTime < g.Start) continue;
            double ticks = (_lastTime - g.Start) * TicksPerSecond;
            if (ticks >= g.Life || !_sprites.TryGetValue(g.Owner.ChrCode, out var sprite)
                || sprite.FrameOfMotion(g.Motion, (int)ticks % Math.Max(1, sprite.MotionTicks(g.Motion)), g.Mirror) is not { } frame)
            {
                _legionGhosts.RemoveAt(i);
                continue;
            }
            double k = Math.Min(1, ticks / g.MoveTicks), life = Math.Min(1, ticks / Math.Max(1, g.Life));
            double x = g.X0 + (g.X1 - g.X0) * k, y = g.Y0 + (g.Y1 - g.Y0) * k;
            if (g.Arc) y -= 60 * 4 * k * (1 - k);                       // 포물선으로 뛰기(0x100cb550)
            double fade = g.Fade0 + (g.Fade1 - g.Fade0) * life;
            if (fade <= 0.02) continue;
            BlitMasked(frame.Px, frame.W, frame.H, (int)x + frame.X, (int)y + frame.Y, fade: Math.Min(1, fade));
        }
    }
}
