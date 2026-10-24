namespace DuelDx;

using static DuelDx.GameWindow;
using WarOfGenesis.Assets;

/// <summary>
/// 파트1 라이트닝볼츠(공격 134~137) — 라이트닝 무기(보정 301)가 치명타 때 맞은 자리에 덧붙여 떨어뜨리는 벼락. 핸들러 <c>0x1004ca20</c>(볼트 분석-파트1-기술-b 7절).
/// </summary>
/// <remarks>
/// <para>맞은 자리 T 위 높이 420(화면 252px)의 점 O 로 불꽃 N 개가 모여들고(56:1 · 35/65:1), 40틱에 구슬(76:0)이 번쩍 한 뒤 불꽃이 다시 터져 흩어진다.
/// 구슬이 끝나면 4틱마다 벼락 기둥(75 — 한 장이 255px 라 T 에서 O 까지 닿는다)이 K 번 번쩍이고, 그 사이 T 에 379:0 · 71:0(소리 2) · 277:0 이 서며
/// 277 이 한 틱 지나 그 칸의 적 하나를 친다. 277 이 끝나고 22틱 뒤 41:0(소리 68)과 파편 77 셋. 레벨은 N = 10 × (번호 − 129) · K = 번호 − 131 만 다르다.</para>
/// <para>원본과 다르게 한 것: 마지막 벼락 뒤의 땅 걷는 77 셋은 원본의 칸 · 픽셀 비교 버그로 거의 안 나와서 뺐다. 모이는 불꽃은 2틱마다 자리를 찍는다.</para>
/// </remarks>
internal sealed partial class Part1Skill
{
    internal const int LightningWork = 20134;

    /// <summary>라이트닝볼츠 1~9(Attack 134~142) — 덧기술 · 어빌리티 43 · 겹침으로 비켜 준 work(+1000)까지.</summary>
    internal static bool IsLightning(int work) => work > Part1GameBase && Part1SkillBook.AttackIdOf(work) is >= 134 and <= 142;

    private int Lightning(int work, List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        int id = Part1SkillBook.AttackIdOf(work), n = 10 * (id - 129), k = id - 131;
        // T — 맞은 이가 그 칸에 있으면 그 발 자리, 없으면 칸 가운데(0x1004ca44).
        var (tx, ty) = targets.Count > 0 && host._units[targets[0]] is { } struck ? host.Btl.UnitFoot(struck) : aim;
        double ox = tx, oy = ty - 252;

        Sound(67, 0);
        Put(56, 1, 0, ox, oy);
        for (int i = 0; i < n; i++)
        {
            var start = (ox + (Rand(2) == 0 ? -1 : 1) * Rand(100), oy + (Rand(2) == 0 ? -1 : 1) * Rand(100) * 0.8);
            var flight = Move(start, (ox, oy), 1, 1.1);
            Along(Rand(2) == 1 ? 65 : 35, 1, Rand(15), AsPath(flight.Prepend(start).ToList(), 2), life: flight.Count);
        }
        Put(76, 0, 40, ox, oy);
        int beta = 40 + Length(76, 0);
        for (int i = 0; i < n; i++) Debris(Rand(2) == 1 ? 65 : 35, 0, beta + 2 + Rand(5), ox, oy, 5, 1.1);
        for (int i = 0; i < k; i++) Put(75, Rand(3), beta + 2 + 4 * i, tx, ty, life: 4);

        int impact = beta + 4;
        double iy = ty - 12;                                   // z + 20
        Put(379, 0, impact, tx, iy);
        Put(71, 0, impact, tx, iy, sounds: true);
        Put(277, 0, impact, tx, iy);
        foreach (int ti in targets) HitAt(impact + 1, ti, hit);

        int after = impact + Length(277, 0) + 22;
        Put(41, 0, after, tx, iy, sounds: true);
        for (int j = 0; j < 3; j++) Debris(77, j, after, tx, iy, 10, 0.9, 12);
        return Math.Max(after + Length(41, 0), beta + 2 + 4 * k) + 2;
    }

    private const int Part1GameBase = WarOfGenesis.Assets.Part1Game.ChrBase;
}
