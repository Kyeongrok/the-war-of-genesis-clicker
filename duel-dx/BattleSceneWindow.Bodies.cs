using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 몸 복제(분신·잔상) 이펙트 — 파·혼·연·오버 드라이브·비연참·회피의 잔상, 포스 필드·마인드 어택의 대상 복제.
/// </summary>
/// <remarks>
/// 옵시디안 분석-스킬 「소리 껍데기만 남은 기술의 그림 (fx-189)」: 원본은 이펙트 생성자에 Obs 로 <b>그 유닛 자신의 sprite</b>(<c>[나+0x58]+0x8</c>)를
/// 넘겨 몸을 복제한다(파 <c>0x100c3340</c> 모션 33~37, 혼·연은 지금 모션 <c>애니+0xe</c>). 복제의 자리·수명은 코드가 셈해 자료로는 모른다 —
/// 여기서는 (가설) 정해진 모션은 시전자에서 대상 쪽으로 늘어선 반투명 분신이 한 번 재생하고,
/// 지금 모션(−1)은 그 순간 모습을 떠서 10틱 동안 흐려지는 잔상으로, 복제본마다 2틱씩 늦게 뜨게 한다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>work 에 붙는 몸 복제 하나 — 복제할 모션(−1 이면 그때 모습), 대상 몸인가.</summary>
    private readonly record struct BodyFx(int Motion, bool OnTarget);

    /// <summary>
    /// 떠 있는 분신 — 주인, 모션(−1 잔상), 시작 시각, 자리(발), 좌우, 잔상이면 뜬 순간의 컷.
    /// </summary>
    private readonly List<(UnitState Owner, int Motion, double Start, int X, int Y, bool Mirror, SpriteFrame? Snapshot)> _bodyClones = [];

    private const int AfterimageTicks = 10, AfterimageStagger = 2;
    private const double CloneFade = 0.55;

    private void SpawnBodyClones(WorkData w, UnitState user, UnitState? target, int col, int row)
    {
        if (!WorkBodies.TryGetValue(w.Id, out var list)) return;
        var (ux, uy) = UnitFoot(user);
        var (tx, ty) = target != null ? UnitFoot(target) : (col * TileW + TileW / 2, CellCenterY(col, row));
        for (int i = 0; i < list.Length; i++)
        {
            var b = list[i];
            var owner = b.OnTarget && target != null ? target : user;
            double start = _lastTime + i * AfterimageStagger / TicksPerSecond;
            if (b.Motion < 0)
            {
                // 잔상 — 자리와 컷은 그 차례가 올 때(뜨는 순간) 뜬다.
                _bodyClones.Add((owner, -1, start, 0, 0, owner.Facing == Facing.Right, null));
                continue;
            }
            // 분신 — 시전자에서 대상 쪽으로 고르게 늘어선다(대상 몸 복제면 대상 자리).
            double t = (i + 1.0) / (list.Length + 1);
            var (x, y) = owner == user && target != user ? ((int)(ux + (tx - ux) * t), (int)(uy + (ty - uy) * t)) : UnitFoot(owner);
            _bodyClones.Add((owner, b.Motion, start, x, y, owner.Facing == Facing.Right, null));
        }
    }

    /// <summary>분신을 반투명으로 그린다 — 인물 위에(인물 다음에) 그린다. 끝난 것은 지운다.</summary>
    private void DrawBodyClones()
    {
        for (int i = _bodyClones.Count - 1; i >= 0; i--)
        {
            var c = _bodyClones[i];
            if (_lastTime < c.Start) continue;
            if (!_sprites.TryGetValue(c.Owner.ChrCode, out var sprite)) { _bodyClones.RemoveAt(i); continue; }
            int tick = (int)((_lastTime - c.Start) * TicksPerSecond);
            SpriteFrame? frame;
            double fade;
            if (c.Motion < 0)
            {
                if (tick >= AfterimageTicks) { _bodyClones.RemoveAt(i); continue; }
                if (c.Snapshot == null)
                {
                    var (fx, fy) = UnitFoot(c.Owner);
                    c = c with { X = fx, Y = fy, Snapshot = sprite.FrameFor(c.Owner) };
                    _bodyClones[i] = c;
                }
                frame = c.Snapshot;
                fade = CloneFade * (1 - (double)tick / AfterimageTicks);
            }
            else
            {
                frame = sprite.FrameOfMotion(c.Motion, tick, c.Mirror);
                if (frame == null) { _bodyClones.RemoveAt(i); continue; }
                fade = CloneFade;
            }
            BlitMasked(frame!.Px, frame.W, frame.H, c.X + frame.X, c.Y + frame.Y, fade: fade);
        }
    }
}
