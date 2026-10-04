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
        // 원본 복제 클래스 셋(ba-21 fx F12 뒤 분석) — 「그 순간 모습」(−1) 줄의 꼴로 가른다.
        if (list.All(b => b.Motion < 0))
        {
            var body = list[0].OnTarget && target != null ? target : user;
            if (list.Length == 1 && list[0].OnTarget)
            {
                // 늘어나는 복제(0x100c6a10 + 0x100c6b20) — 포스 필드·배리어·실드류: 20틱 동안 가로 1 + 0.055k · 세로 1 + 0.015k (k = min(t, 19 − t)),
                // 가로는 가운데 · 세로는 발 기준으로 부풀었다 돌아온다. 표가 다른 자리(카운터 필드 120틱 …)도 같은 꼴로 본다(가설).
                _bodyShapes.Add((body, 1, host._lastTime, 0, null));
                return;
            }
            if (list.Length == 2 && list.All(b => b.OnTarget))
            {
                // 떨리는 복제(0x100c6d70 + 0x100c6e20) — 마인드 어택: 복제 둘이 ±5 로, 나이 % 4 가 0·2 면 제자리 · 1 이면 −치우침 · 3 이면 +치우침, 40틱.
                // 쇼크(0x100ae41c — 조각 떼 표에 482:3 × 80 이 있는 work)는 ±6 · 50틱.
                int sway = WorkFxSwarm.Table.TryGetValue(w.Id, out var swarm) && swarm.Any(r => r.Obs == 482 && r.Count == 80) ? 6 : 5;
                _bodyShapes.Add((body, 2, host._lastTime, sway, null));
                _bodyShapes.Add((body, 2, host._lastTime, -sway, null));
                return;
            }
            if (list.Length >= 3 && list.All(b => !b.OnTarget))
            {
                // 꼬리 복제(0x100c61c0 + 0x100c5fe0) — 혼: 복제 일곱이 시전자의 2·4·…·14틱 전 자리에 선다(돌진을 따라 늘어섰다가 멈추면 겹쳐 사라진다).
                // 비연참(21개 — 세 줄)은 옆 줄의 치우침을 못 읽어 한 줄로 겹쳐 둔다.
                var history = new List<(int X, int Y)>();
                for (int i = 0; i < Math.Min(7, list.Length); i++) _bodyShapes.Add((body, 3, host._lastTime, 2 * (i + 1), history));
                return;
            }
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
    /// 꼴이 있는 복제 — 1 늘어남(20틱) · 2 떨림(Param = 치우침, 40틱) · 3 꼬리(Param = 몇 틱 전 자리, History = 주인이 지나온 자리).
    /// 그리는 밝기는 다른 분신과 같은 값으로 둔다(원본의 그리기 방식 3·4 가 무엇인지는 못 읽음).
    /// </summary>
    internal readonly List<(UnitState Owner, int Kind, double Start, int Param, List<(int X, int Y)>? History)> _bodyShapes = [];

    internal void DrawBodyShapes()
    {
        for (int i = _bodyShapes.Count - 1; i >= 0; i--)
        {
            var c = _bodyShapes[i];
            int tick = (int)((host._lastTime - c.Start) * TicksPerSecond);
            if (!c.Owner.OnField || !host._sprites.TryGetValue(c.Owner.ChrCode, out var sprite) || sprite.FrameFor(c.Owner) is not { } frame || tick > 300)
            { _bodyShapes.RemoveAt(i); continue; }
            var (fx, fy) = host.Btl.UnitFoot(c.Owner);
            switch (c.Kind)
            {
                case 1:
                {
                    if (tick >= 20) { _bodyShapes.RemoveAt(i); continue; }
                    int k = Math.Min(tick, 19 - tick);
                    int w = Math.Max(1, (int)(frame.W * (1 + 0.055 * k))), h = Math.Max(1, (int)(frame.H * (1 + 0.015 * k)));
                    var px = new uint[w * h];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            px[y * w + x] = frame.Px[Math.Min(frame.H - 1, y * frame.H / h) * frame.W + Math.Min(frame.W - 1, x * frame.W / w)];
                    host.BlitMasked(px, w, h, fx + frame.X - (w - frame.W) / 2, fy + frame.Y - (h - frame.H), fade: CloneFade);
                    break;
                }
                case 2:
                {
                    if (tick >= (Math.Abs(c.Param) == 6 ? 50 : 40)) { _bodyShapes.RemoveAt(i); continue; }
                    int shift = (tick % 4) switch { 1 => -c.Param, 3 => c.Param, _ => 0 };
                    host.BlitMasked(frame.Px, frame.W, frame.H, fx + frame.X + shift, fy + frame.Y, fade: CloneFade);
                    break;
                }
                default:
                {
                    var history = c.History!;
                    while (history.Count <= tick) history.Add((fx, fy));
                    var (hx, hy) = history[Math.Max(0, tick - c.Param)];
                    if (tick > c.Param && (hx, hy) == (fx, fy)) { _bodyShapes.RemoveAt(i); continue; }
                    host.BlitMasked(frame.Px, frame.W, frame.H, hx + frame.X, hy + frame.Y, fade: CloneFade);
                    break;
                }
            }
        }
    }

    /// <summary>분신을 반투명으로 그린다 — 인물 위에(인물 다음에) 그린다. 끝난 것은 지운다.</summary>
    internal void DrawBodyClones()
    {
        DrawBodyShapes();
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
