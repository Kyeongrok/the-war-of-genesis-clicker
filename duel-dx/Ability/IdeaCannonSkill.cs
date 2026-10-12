using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 이데아 캐논(어빌리티 171, work 1592) — 살라딘의 빔. 원본 핸들러 <c>0x100b6720</c> 은 다섯 열 × (끝머리 + 몸통 타일)을 화면 끝까지 깔고
/// 열마다 「켜짐 4틱 → 유지 → 꺼짐 13틱」을 사슬로 잇는다. 뽑은 표는 조각을 종류마다 한 장씩 한 점에 겹쳐 띄웠다(gm-skil-18).
/// </summary>
/// <remarks>
/// 단계 1(동작 17 이 끝난 뒤): 열 k = −2…2. 끝머리 자리(시전자 기준 월드) — 위·아래 (20k, ∓(200 + 42|k|)), 좌·우 (∓(160 + 35|k|·s), 25k·t) —
/// s 는 위쪽 열(k ≤ 0) 0.8 · 아래쪽 열 1.2, t 는 위쪽 열만 0.8(가운데 열이 가장 가깝고 가장자리 열일수록 멀리서 시작하는 화살촉 꼴).
/// 몸통 j 는 끝머리에서 j × 30(세로 37)씩 더 나아간 자리, 높이는 모두 80. 열은 2|k| 틱 늦게 켜지고 유지는 110 − 4|k| 틱이라 가운데 열이 먼저 켜지고 늦게 꺼진다.
/// 판정은 마지막 열이 꺼진 뒤(약 123틱) 범위 안 모두에게 한 번(<c>0x100b7149</c>).
/// 아래를 볼 때 몸통의 켜짐 모션은 원본이 10(위쪽 끝머리 유지 그림)을 쓴다 — 그대로 둔다.
/// </remarks>
internal sealed unsafe class IdeaCannonSkill(GameWindow host)
{
    internal const int Ability = 171, BeamObs = 987;

    /// <summary>빔이 다 꺼지고 판정이 드는 틱(단계 1 부터).</summary>
    internal const int HitTick = 123;

    internal void Spawn(WorkData w, UnitState user, int col, int row)
    {
        var fx = host.ForcetrulAb;
        var (ux, uy) = host.Btl.UnitFoot(user);
        var dir = user.Facing;
        bool vertical = dir is Facing.Up or Facing.Down, mirror = dir == Facing.Right;
        int sign = dir is Facing.Up or Facing.Left ? -1 : 1;
        var (hA, hB, hC, bA, bB, bC) = dir switch
        {
            Facing.Up => (5, 10, 0, 8, 13, 3),
            Facing.Down => (7, 12, 2, 10, 13, 3),
            _ => (6, 11, 1, 9, 14, 4),
        };
        int tiles = fx.Tiles(user, dir);
        for (int k = -2; k <= 2; k++)
        {
            int far = Math.Abs(k);
            double wx, wy;
            if (vertical) (wx, wy) = (20 * k, sign * (200 + 42 * far));
            else (wx, wy) = (sign * (160 + (int)(35 * far * (k <= 0 ? 0.8 : 1.2))), k <= 0 ? (int)(25 * k * 0.8) : 25 * k);
            double headX = ux + wx, headY = uy + wy * 0.8 - 80 * 0.6;
            double stepX = vertical ? 0 : sign * 30, stepY = vertical ? sign * 37 * 0.8 : 0;
            int on = 2 * far, hold = 110 - 4 * far;
            void Row(int head, int body, int at, int life)
            {
                fx.Fx(w, BeamObs, head, at, headX, headY, life, mirror);
                for (int j = 0; j < tiles - 1; j++) fx.Fx(w, BeamObs, body, at, headX + stepX * j, headY + stepY * j, life, mirror);
            }
            Row(hA, bA, on, 0);
            Row(hB, bB, on + 4, hold);
            Row(hC, bC, on + 4 + hold, 0);
        }
        fx.HitAllAt(w, user, col, row, HitTick);
    }
}
