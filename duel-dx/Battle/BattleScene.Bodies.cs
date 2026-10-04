using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 몸 복제(분신·잔상) 이펙트 — 파·혼·연·오버 드라이브·비연참·회피의 잔상, 포스 필드·마인드 어택의 대상 복제.
/// </summary>
/// <remarks>
/// 옵시디안 분석-스킬 「소리 껍데기만 남은 기술의 그림 (fx-189)」: 원본은 이펙트 생성자에 Obs 로 <b>그 유닛 자신의 sprite</b>(<c>[나+0x58]+0x8</c>)를
/// 넘겨 몸을 복제한다(파 <c>0x100c3340</c> 모션 33~37, 혼·연은 지금 모션 <c>애니+0xe</c>). 복제의 자리·수명은 코드가 셈해 자료로는 모른다 —
/// 여기서는 (가설) 정해진 모션은 시전자에서 대상 쪽으로 늘어선 반투명 분신이 한 번 재생하고,
/// 지금 모션(−1)은 그 순간 모습을 떠서 10틱 동안 흐려지는 잔상으로, 복제본마다 2틱씩 늦게 뜨게 한다.
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    /// <summary>work 에 붙는 몸 복제 하나 — 복제할 모션(−1 이면 그때 모습), 대상 몸인가.</summary>
    internal readonly record struct BodyFx(int Motion, bool OnTarget);

    /// <summary>
    /// 떠 있는 분신 — 주인, 모션(−1 잔상), 시작 시각, 자리(발), 좌우, 잔상이면 뜬 순간의 컷.
    /// </summary>
    internal readonly List<(UnitState Owner, int Motion, double Start, int X, int Y, bool Mirror, SpriteFrame? Snapshot)> _bodyClones = [];

    internal const int AfterimageTicks = 10, AfterimageStagger = 2;
    internal const double CloneFade = 0.55;

    internal void SpawnBodyClones(WorkData w, UnitState user, UnitState? target, int col, int row)
    {
        if (w.Id == StagingSkill.DoubleBreakWork) return;   // 분신 A·B 는 StageBeforeHit 가 날린다(Staging.cs)
        if (!WorkBodies.TryGetValue(w.Id, out var list)) return;
        // 원본 복제 클래스(ba-21 fx F12 뒤 분석, 17:20 재확인) — 「그 순간 모습」(−1) 줄을 기술로 가른다.
        if (list.All(b => b.Motion < 0))
        {
            var body = list[0].OnTarget && target != null ? target : user;
            double now = host._lastTime;
            if (list.Length == 1 && list[0].OnTarget && !StagingSkill.IsWeaponCrashWork(w.Id) && !StagingSkill.IsBalancingWork(w.Id))
            {
                // 늘어나는 복제(0x100c6a10 + 0x100c6b20, 그리기 칸 4) — 포스 필드·배리어·실드류 9자리: 20틱, 가로 1 + 0.055k · 세로 1 + 0.015k
                // (k = min(t, 19 − t), 캐노피만 0.05 · 0.01). 카운터 실드(98, 0x10098253 · 0x100986ca)·카운터 필드(113, 0x100a1eec · 0x100a221d)는
                // 120틱 가로만 — 표 120칸이 40틱 주기 셋: 앞 20틱 1 + 0.03k, 뒤 20틱 1.4 − 0.03k(세로 표는 없음 = 1). Param = 틱 수.
                _bodyShapes.Add((body, 1, now, w.AbilityId is 98 or 113 ? 120 : 20, null, 0, 0, 4 / 9.0, null));
                return;
            }
            if (list.Length == 2 && list.All(b => b.OnTarget) && (w.Id == 396 || w.Id is >= 556 and <= 574 || w.Id == 399 || w.Id is >= 594 and <= 602))
            {
                // 떨리는 복제(0x100c6d70 + 0x100c6e20, 그리기 칸 3 · 본체 5) — 마인드 어택 ±5 · 40틱, 쇼크 ±6 · 50틱.
                int sway = w.Id == 399 || w.Id is >= 594 and <= 602 ? 6 : 5;
                _bodyShapes.Add((body, 2, now, sway, null, 0, 0, 3 / 9.0, null));
                _bodyShapes.Add((body, 2, now, -sway, null, 0, 0, 3 / 9.0, null));
                return;
            }
            if (list.Length == 1 && !list[0].OnTarget && (w.Id == 3 || w.Id is >= 21 and <= 39))
            {
                // 크기 복제(0x100c64b0 + 0x100c6600(20, 0.97, 0.97), 그리기 칸 3) — 오버 드라이브: 10틱 뒤에 1.84배로 떠서 틱마다 × 0.97, 10틱(1.36배에서 사라짐).
                _bodyShapes.Add((user, 5, now + 10 / TicksPerSecond, 0, null, 0, 0, 3 / 9.0, null));
                return;
            }
            if (list.Length == 7 && list.All(b => !b.OnTarget))
            {
                // 꼬리 복제(0x100c61c0 + 0x100c5fe0) — 혼: 일곱 개가 시전자의 2·4·…·14틱 전 자리에 선다.
                var history = new List<(int X, int Y, SpriteFrame? Frame, bool Busy)>();
                for (int n = 1; n <= 7; n++) _bodyShapes.Add((user, 3, now, 2 * n, history, 0, 0, CloneFade, null));
                return;
            }
            if (w.Id is >= 341 and <= 350)
            {
                // 비연참(0x10080430) — 꼬리 21개: 세 줄 × (2·4·…·14틱 전). 옆 두 줄은 가는 쪽 뒤로 40 · 옆으로 ±40(방향 표 0x100813f4, 화면 y 는 × 0.8).
                var history = new List<(int X, int Y, SpriteFrame? Frame, bool Busy)>();
                var (b1, b2) = user.Facing switch
                {
                    Facing.Up => ((40, 32), (-40, 32)),
                    Facing.Left => ((40, 32), (40, -32)),
                    Facing.Down => ((40, -32), (-40, -32)),
                    _ => ((-40, 32), (-40, -32)),
                };
                foreach (var (dx, dy) in new[] { (0, 0), b1, b2 })
                    for (int n = 1; n <= 7; n++) _bodyShapes.Add((user, 3, now, 2 * n, history, dx, dy, CloneFade, null));
                return;
            }
            if (list.Length == 9 && list.All(b => !b.OnTarget))
            {
                // 연(0x1007f860) — 휘두를 때마다 복제 셋이 그 몸짓을 6 · 9 · 12틱 늦게 되풀이한다(자리 y+1·+2·+3, 그리기 칸 4 · 3 · 2).
                // 여기서는 시전자의 6 · 9 · 12틱 전 모습을 그 자리에 겹쳐 그린다 — 시전자가 설 때까지.
                var history = new List<(int X, int Y, SpriteFrame? Frame, bool Busy)>();
                _bodyShapes.Add((user, 4, now, 6, history, 0, 1, 4 / 9.0, null));
                _bodyShapes.Add((user, 4, now, 9, history, 0, 2, 3 / 9.0, null));
                _bodyShapes.Add((user, 4, now, 12, history, 0, 3, 2 / 9.0, null));
                return;
            }
        }
        // 파(어빌리티 2, 핸들러 0x10082990) — 시전자 몸의 복제 탄 여덟이 3틱 간격으로 여덟 방향으로 날아간다(빠르기 20, 틱마다 × 0.85, 바닥 10,
        // 거리 D = (범위 − 2) × 40 — 대각선은 D/2 씩). 탄마다 2·4·6틱 전 자리에 꼬리 복제 셋. 그리기 칸 4. 전에는 분신 열한 개가 대상 쪽으로 늘어섰다.
        if (w.AbilityId == 2)       // 파 = 어빌리티 2(work 11 · 202~220). 11 은 큐어다 — 처음에 번호를 잘못 넣어 큐어에 탄이 붙었었다
        {
            var (px, py) = host.Btl.UnitFoot(user);
            double reach = Math.Max(0, w.AreaMaxQuarters - 2) * 40;      // 메모리의 +0x1a(파일값 × 4) − 2 — 파일값 1 이면 80
            (double Dx, double Dy, int Shot, int Tail, bool Mirror)[] ways =
            [
                // 만드는 차례(0x10082af4~0x10083040)와 꼬리 모션 표(0x10083558): 오른쪽 34/37 · 대각 · 대각 · 왼쪽 34/38 · 대각 · 대각 · 아래 35/37 · 위 33/36.
                // 왼쪽 탄의 꼬리가 38, 아래 탄의 꼬리가 37 인 것은 원본 표 그대로다. 대각 넷의 차례는 가설.
                (reach, 0, 34, 37, true), (reach / 2, -reach / 2, 37, 37, true), (reach / 2, reach / 2, 37, 37, true), (-reach, 0, 34, 38, false),
                (-reach / 2, reach / 2, 37, 37, false), (-reach / 2, -reach / 2, 37, 37, false), (0, reach, 35, 37, false), (0, -reach, 33, 36, false),
            ];
            for (int i = 0; i < ways.Length; i++)
                _bodyShots.Add((user, ways[i].Shot, ways[i].Tail, host._lastTime + 3 * i / TicksPerSecond, px, py, px + ways[i].Dx, py + ways[i].Dy * 0.8, ways[i].Mirror));
            return;
        }
        var (ux, uy) = host.Btl.UnitFoot(user);
        var (tx, ty) = target != null ? host.Btl.UnitFoot(target) : (col * TileW + TileW / 2, host.CellCenterY(col, row));
        for (int i = 0; i < list.Length; i++)
        {
            var b = list[i];
            var owner = b.OnTarget && target != null ? target : user;
            double start = host._lastTime + i * AfterimageStagger / TicksPerSecond;
            if (b.Motion < 0)
            {
                // 잔상 — 자리와 컷은 그 차례가 올 때(뜨는 순간) 뜬다.
                _bodyClones.Add((owner, -1, start, 0, 0, owner.Facing == Facing.Right, null));
                continue;
            }
            // 분신 — 시전자에서 대상 쪽으로 고르게 늘어선다(대상 몸 복제면 대상 자리).
            double t = (i + 1.0) / (list.Length + 1);
            var (x, y) = owner == user && target != user ? ((int)(ux + (tx - ux) * t), (int)(uy + (ty - uy) * t)) : host.Btl.UnitFoot(owner);
            _bodyClones.Add((owner, b.Motion, start, x, y, owner.Facing == Facing.Right, null));
        }
    }

    /// <summary>
    /// 꼴이 있는 복제 — 1 늘어남(Param = 틱 수, 20 또는 120) · 2 떨림(Param = 치우침) · 3 꼬리(Param 틱 전 자리 + (Dx, Dy)) · 4 메아리(Param 틱 전 모습을 제자리에) ·
    /// 5 크기(1.84배에서 틱마다 × 0.97, 10틱) · 6 리콜의 사라짐/나타남 크기(Param 0/1). History 는 주인이 틱마다 지나온 자리·모습(같은 묶음이 함께 쓴다). Fade 는 그리기 칸(+0x13) ÷ 9.
    /// </summary>
    internal readonly List<(UnitState Owner, int Kind, double Start, int Param, List<(int X, int Y, SpriteFrame? Frame, bool Busy)>? History,
                           int Dx, int Dy, double Fade, SpriteFrame? Snapshot)> _bodyShapes = [];

    internal void DrawBodyShapes()
    {
        for (int i = _bodyShapes.Count - 1; i >= 0; i--)
        {
            var c = _bodyShapes[i];
            if (host._lastTime < c.Start) continue;
            int tick = (int)((host._lastTime - c.Start) * TicksPerSecond);
            if (!c.Owner.OnField || !host._sprites.TryGetValue(c.Owner.ChrCode, out var sprite) || sprite.FrameFor(c.Owner) is not { } frame || tick > 400)
            { if (c.Kind == 2) c.Owner.Fade = 1; _bodyShapes.RemoveAt(i); continue; }
            var (fx, fy) = host.Btl.UnitFoot(c.Owner);
            void Stretched(SpriteFrame f, double sx, double sy)
            {
                int w = Math.Max(1, (int)(f.W * sx)), h = Math.Max(1, (int)(f.H * sy));
                var px = new uint[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        px[y * w + x] = f.Px[Math.Min(f.H - 1, y * f.H / h) * f.W + Math.Min(f.W - 1, x * f.W / w)];
                // 가로는 가운데, 세로는 그림 아랫변을 붙든다(0x100c6bf0 · 0x100c6740).
                host.BlitMasked(px, w, h, fx + f.X - (w - f.W) / 2, fy + f.Y - (h - f.H), fade: c.Fade);
            }
            switch (c.Kind)
            {
                case 1:
                {
                    if (tick >= c.Param) { _bodyShapes.RemoveAt(i); continue; }
                    if (c.Param == 120)
                    {
                        int at = tick % 40;
                        Stretched(frame, at < 20 ? 1 + 0.03 * at : 1.4 - 0.03 * (at - 20), 1);
                        break;
                    }
                    int k = Math.Min(tick, 19 - tick);
                    Stretched(frame, 1 + 0.055 * k, 1 + 0.015 * k);
                    break;
                }
                case 2:
                {
                    int life = Math.Abs(c.Param) == 6 ? 50 : 40;
                    if (tick >= life) { c.Owner.Fade = 1; _bodyShapes.RemoveAt(i); continue; }
                    int shift = (tick % 4) switch { 1 => -c.Param, 3 => c.Param, _ => 0 };
                    c.Owner.Fade = tick >= life - 1 ? 1 : 5 / 9.0;      // 떨리는 동안 본체는 그리기 칸 5
                    host.BlitMasked(frame.Px, frame.W, frame.H, fx + frame.X + shift, fy + frame.Y, fade: c.Fade);
                    break;
                }
                case 5:
                {
                    if (tick >= 10) { _bodyShapes.RemoveAt(i); continue; }
                    if (c.Snapshot == null) { c = c with { Snapshot = frame }; _bodyShapes[i] = c; }
                    double scale = Math.Pow(0.97, tick + 1 - 20);
                    Stretched(c.Snapshot!, scale, scale);
                    break;
                }
                case 6:
                {
                    // 리콜(0x1009da20) — Param 0: 사라질 때 제 크기에서 틱마다 가로 × 0.8 · 세로 × 1.2(0x100c6590, 0x1009dc89),
                    // Param 1: 나타날 때 가로 1.1^−10 · 세로 0.9^−10 에서 틱마다 × 1.1 · × 0.9 로 제 크기까지(0x100c6600, 0x1009e08b). 둘 다 10틱 · 그리기 칸 3.
                    if (tick >= 10) { _bodyShapes.RemoveAt(i); continue; }
                    if (c.Snapshot == null) { c = c with { Snapshot = frame }; _bodyShapes[i] = c; }
                    if (c.Param == 0) Stretched(c.Snapshot!, Math.Pow(0.8, tick), Math.Pow(1.2, tick));
                    else Stretched(c.Snapshot!, Math.Pow(1.1, tick - 10), Math.Pow(0.9, tick - 10));
                    break;
                }
                default:
                {
                    var history = c.History!;
                    while (history.Count <= tick) history.Add((fx, fy, frame, c.Owner.IsBusy));
                    var past = history[Math.Max(0, tick - c.Param)];
                    if (c.Kind == 3)
                    {
                        // 꼬리 — 주인을 따라잡고 주인이 서 있으면 사라진다.
                        if (tick > c.Param && (past.X, past.Y) == (fx, fy)) { _bodyShapes.RemoveAt(i); continue; }
                        host.BlitMasked(frame.Px, frame.W, frame.H, past.X + c.Dx + frame.X, past.Y + c.Dy + frame.Y, fade: c.Fade);
                    }
                    else
                    {
                        // 메아리 — 그때의 모습이 서 있는 모습이 되면(주인이 다 휘두른 뒤 Param 틱) 사라진다.
                        if (tick > c.Param && !past.Busy) { _bodyShapes.RemoveAt(i); continue; }
                        if (tick >= c.Param && past.Frame is { } old) host.BlitMasked(old.Px, old.W, old.H, past.X + old.X, past.Y + c.Dy + old.Y, fade: c.Fade);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>파의 복제 탄 — 주인 몸의 모션 Shot 이 From 에서 To 로 날고(20 × 0.85, 바닥 10), 꼬리 셋(모션 Tail)이 2·4·6틱 전 자리를 따른다.</summary>
    internal readonly List<(UnitState Owner, int Shot, int Tail, double Start, double FromX, double FromY, double ToX, double ToY, bool Mirror)> _bodyShots = [];

    internal void DrawBodyShots()
    {
        _bodyShots.RemoveAll(s =>
        {
            if (host._lastTime < s.Start) return false;
            if (!host._sprites.TryGetValue(s.Owner.ChrCode, out var sprite)) return true;
            int tick = (int)((host._lastTime - s.Start) * TicksPerSecond);
            double dx = s.ToX - s.FromX, dy = s.ToY - s.FromY, total = Math.Sqrt(dx * dx + dy * dy);
            (double X, double Y) At(int t)
            {
                double gone = 0, speed = 20;
                for (int k = 0; k < t && gone < total; k++) { gone += speed; speed = Math.Max(10, speed * 0.85); }
                double f = total <= 0 ? 1 : Math.Min(1, gone / total);
                return (s.FromX + dx * f, s.FromY + dy * f);
            }
            int life = Math.Max(1, sprite.MotionTicks(s.Shot) - 1);
            var here = At(tick);
            if (here == (s.ToX, s.ToY) && tick >= life) return true;      // 닿았고 모션도 끝났다
            if (tick > 200) return true;
            foreach (int back in new[] { 6, 4, 2 })
                if (tick >= back && sprite.FrameOfMotion(s.Tail, tick - back, s.Mirror) is { } tail)
                {
                    var (bx, by) = At(tick - back);
                    host.BlitMasked(tail.Px, tail.W, tail.H, (int)bx + tail.X, (int)by + tail.Y, fade: 4 / 9.0);
                }
            if (sprite.FrameOfMotion(s.Shot, tick, s.Mirror) is { } frame)
                host.BlitMasked(frame.Px, frame.W, frame.H, (int)here.X + frame.X, (int)here.Y + frame.Y, fade: 4 / 9.0);
            return false;
        });
    }

    /// <summary>분신을 반투명으로 그린다 — 인물 위에(인물 다음에) 그린다. 끝난 것은 지운다.</summary>
    internal void DrawBodyClones()
    {
        DrawBodyShapes();
        DrawBodyShots();
        for (int i = _bodyClones.Count - 1; i >= 0; i--)
        {
            var c = _bodyClones[i];
            if (host._lastTime < c.Start) continue;
            if (!host._sprites.TryGetValue(c.Owner.ChrCode, out var sprite)) { _bodyClones.RemoveAt(i); continue; }
            int tick = (int)((host._lastTime - c.Start) * TicksPerSecond);
            SpriteFrame? frame;
            double fade;
            if (c.Motion < 0)
            {
                if (tick >= AfterimageTicks) { _bodyClones.RemoveAt(i); continue; }
                if (c.Snapshot == null)
                {
                    var (fx, fy) = host.Btl.UnitFoot(c.Owner);
                    c = c with { X = fx, Y = fy, Snapshot = sprite.FrameFor(c.Owner) };
                    _bodyClones[i] = c;
                }
                frame = c.Snapshot;
                fade = CloneFade * (1 - (double)tick / AfterimageTicks);
            }
            else
            {
                // 모션이 끝나면 지운다 — 모션 읽기는 끝을 넘으면 마지막 컷을 붙들고 있어서, 다크 스크림(51·52)·폭풍검(98)의 분신이
                // 필살기가 끝난 뒤에도 그 자리에 잔상으로 남았다(사용자 보고).
                if (tick >= Math.Max(1, sprite.MotionTicks(c.Motion))) { _bodyClones.RemoveAt(i); continue; }
                frame = sprite.FrameOfMotion(c.Motion, tick, c.Mirror);
                if (frame == null) { _bodyClones.RemoveAt(i); continue; }
                fade = CloneFade;
            }
            host.BlitMasked(frame!.Px, frame.W, frame.H, c.X + frame.X, c.Y + frame.Y, fade: fade);
        }
    }
}
