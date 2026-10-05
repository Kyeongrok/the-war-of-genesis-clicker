using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 블레이드 미사일(어빌리티 31) — 칼들이 시전자 앞에 나타나 잠깐 떠 있다가 앞으로 날아가 꽂힌다. 원본 핸들러가 자리와 도착을 코드로 셈한다.
/// 뽑은 표(AbilityScripts.g.cs · WorkFxExtra.g.cs)는 출발·도착을 못 읽어(From/To 3) 칼이 시전자 자리에 한 틱 찍히고 말았다(사용자 보고: 이펙트가 안 나온다).
/// </summary>
/// <remarks>
/// 핸들러 <c>0x10085460</c> 단계 2: 소리 386:0(시전자). 칼 줄 수 n = 효과 범위 모양(work <c>+0x14</c>)이 6 이면 2, 아니면 3(<c>0x10085667</c>) —
/// 줄 i(0 ≤ i &lt; n)마다 가운데에서 옆으로 ±40·i 떨어진 자리에 하나씩(i = 0 은 하나, 곧 셋 또는 다섯 자루).
/// 자루마다 세 이펙트를 사슬로 잇는다(<c>0x100c2640</c> — 앞의 것이 끝나면 다음):
/// <list type="number">
/// <item>나타남 171:9(위) · 13(옆) · 17(아래) — 모션 한 번(<c>0x100c2530(0)</c>), 줄 i 는 i 틱 늦게(<c>0x100c24d0(i)</c>)</item>
/// <item>떠 있음 171:0 · 4 · 8 — 20틱(<c>0x100c2530(0x14)</c>)</item>
/// <item>날아감 같은 모션 — 직선탄(<c>0x100c38a0</c> · <c>0x100c3490(도착, 80, 0, 0, 0)</c>), 틱당 80</item>
/// </list>
/// 자리(월드 — 화면은 x 그대로 · y × 0.8 · 높이 × 0.6): 출발 = 시전자에서 보는 쪽으로 90(<c>0x5a</c>), 높이 +50(<c>0x32</c>);
/// 도착 = 보는 쪽으로 D = 40 × 범위 크기 + 40(<c>0x10085694</c> — 크기 7 이면 320), 높이 +100(<c>0x64</c>). 판정(1001)은 날아간 칼이 사라질 때다.
/// 위(<c>+0x5c</c> = 0)와 아래(2) · 왼쪽(1) 가지를 읽었고 오른쪽은 왼쪽을 뒤집은 것으로 본다(표의 뒤집기 2).
/// </remarks>
internal sealed unsafe class BladeMissileSkill(GameWindow host)
{
    internal const int BladeObs = 171, BladeSoundObs = 386;

    /// <summary>블레이드 미사일의 work — Lv1~20(스킬 파일 0031).</summary>
    internal static readonly HashSet<int> Works = [467, 726, 725, 724, 723, 722, 721, 720, 719, 718, 763, 762, 761, 760, 759, 758, 757, 756, 755, 754];

    /// <summary>시전 뒤 칼이 나타나기까지(틱) — 핸들러 단계 1 이 40틱을 센 뒤 단계 2 가 깐다(<c>0x1008559a cmp [+0x96], 0x28</c>).</summary>
    internal const int AppearDelay = 41;

    /// <summary>칼들을 깐다 — 마지막 칼이 닿는 때를 판정 시각(<c>_fxArriveAt</c>)으로 남긴다.</summary>
    internal void Spawn(WorkData w, UnitState user)
    {
        var btl = host.Btl;
        var (ux, uy) = btl.UnitFoot(user);
        // 보는 쪽(월드 x, y) · 나타남 모션 · 칼 모션 · 좌우 뒤집기
        var (dx, dy, appear, blade, mirror) = user.Facing switch
        {
            Facing.Up => (0, -1, 9, 0, false),
            Facing.Down => (0, 1, 17, 8, false),
            Facing.Left => (-1, 0, 13, 4, false),
            _ => (1, 0, 13, 4, true),
        };
        int rows = w.AreaShape == 6 ? 2 : 3, far = 40 * w.AreaArg + 40;
        double appearTicks = Math.Max(1, host.UiFor(BladeObs)?.MotionLength(appear) ?? 1);
        double last = 0;
        for (int i = 0; i < rows; i++)
            foreach (int side in i == 0 ? [0] : new[] { i, -i })
            {
                // 옆으로는 보는 쪽에 수직으로 벌린다.
                double wx = -dy * 40.0 * side, wy = dx * 40.0 * side;
                (double X, double Y) from = (ux + wx + dx * 90, uy + (wy + dy * 90) * 0.8 - 50 * 0.6);
                (double X, double Y) to = (ux + wx + dx * far, uy + (wy + dy * far) * 0.8 - 100 * 0.6);
                double at = host._lastTime + (AppearDelay + i) / TicksPerSecond, hover = at + appearTicks / TicksPerSecond, fly = hover + 20 / TicksPerSecond;
                (mirror ? btl._effectMirrors : btl._effects).Add((BladeObs, appear, at, (int)from.X, (int)from.Y));
                host.HeavenEarthAb.AddTimedFx(BladeObs, blade, hover, from, 20, mirror);
                btl._shots.Add((BladeObs, blade, fly, from.X, from.Y, to.X, to.Y, 80, 1, 1, 0, 80, mirror));
                double reach = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
                last = Math.Max(last, fly + Math.Ceiling(reach / 80) / TicksPerSecond);
            }
        btl._fxArriveAt = Math.Max(btl._fxArriveAt, last);
        btl._fxLatestStart = Math.Max(btl._fxLatestStart, last);
    }
}
