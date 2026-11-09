namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 메테오(공격 87) — 유성검(보정 300)이 치명타 때 덧붙여 쏘는 운석 비. 핸들러 <c>0x1004a190</c>(볼트 분석-파트1-기술-b 6절), 앞머리는 썬더스탐과 같은 갈래 4.
/// </summary>
/// <remarks>
/// <para>보이는 판의 아무 칸 35곳에 15틱 간격(+0~9)으로 운석(115:0 큰 것 1/3 · 115:3 작은 것 2/3)이 왼위에서 날아와 떨어진다 — 발사 소리 60/61,
/// 떨어지면 소리 62(작은 것 63·64), 폭발 123:5, 파편 228 둘, 충격 124:0·18(큰 것) 또는 116:0(작은 것), 반쯤은 땅불(23 r — 판의 기운, 시간 틱 동안 남는다)이 탄다.
/// 폭발 1틱 뒤 그 칸에서 맨해튼 3칸(큰 것) · 4칸(작은 것) 안의 적을 한 번씩 친다(<c>0x10039690(…, 3, 1, 12|16, Attack +0x40 = 1)</c> — 적만, 한 운석 안에서는
/// <c>+0x27a</c> 로 한 번, 큰 유닛은 차지한 칸 하나라도 들면). 운석마다 따로라 한 적이 여러 번 맞을 수 있다(fg-160 — menu-89 때는 사람마다 한 번이었다).
/// 판 밖은 (판 칸 수 ÷ 20 − 35) 번, 보이는 칸 밖 아무 칸에 보이지 않는 맞음(c = 16 이 3/4, 12 가 1/4)이 t0+1 에 서고 반쯤 땅불(100~299틱 뒤)이 붙는다(6.3절).
/// 대상은 범위 목록이 아니라 판 위의 모든 적이다(크기 9999).</para>
/// <para>원본과 다르게 한 것: 꼬리 입자(틱마다 34/64 둘)는 3틱마다 하나로 줄였다. 운석이 날아오는 길은 곧은 이동기(빠르기 1, ×(5~8), 최대 40)로 셈한다.
/// 판 밖 맞음의 「걸을 수 없는 칸이면 건너뜀」은 안 옮겼다 [H].</para>
/// </remarks>
internal sealed partial class Part1Skill
{
    internal const int MeteorWork = 20087;

    /// <summary>
    /// 메테오(Attack 87)인가 — 유성검 덧기술의 고정 work 20087 말고도, 어빌리티 36 「메테오」를 배워 쓰는 work 도(Part1SkillBook 이 20087 이 이미 쓰여
    /// 1000 비켜 준 21087). 전에는 고정 번호만 봐서 21087 이 이 틀을 못 타고 파트2 길로 빠져 운석 없이 피해 · 죽음 · 경험치가 곧바로 났다(카페 제보 fg-99).
    /// </summary>
    internal static bool IsMeteor(int work) => work == MeteorWork || (work > Part1GameBase && WarOfGenesis.Assets.Part1SkillBook.AttackIdOf(work) == 87);

    private int Meteor(List<int> targets, Action<int> hit)
    {
        var (left, top, right, bottom) = View;
        int minCol = Math.Max(0, left / TileW), maxCol = Math.Min(host.Cols - 1, (right - 1) / TileW);
        int minRow = Math.Max(0, host.RowAt(left, top)), maxRow = Math.Min(host.Rows - 1, host.RowAt(left, bottom - 1));
        if (maxRow < minRow) (minRow, maxRow) = (maxRow, minRow);
        int last = 0;
        var foes = FoesOf(_user).ToList();
        // 운석 하나의 맞음 — 그 칸에서 맨해튼 reach 안에 차지한 칸이 하나라도 드는 적을 한 번씩.
        void Blast(int tick, int col, int row, int reach)
        {
            foreach (int ti in foes)
                if (host._units[ti] is { } u && ReachOf(u, col, row) <= reach) HitAt(tick, ti, hit);
        }

        // 판 밖 — 보이는 칸(끝 열 · 끝 줄 넣어) 밖 아무 칸에 보이지 않는 맞음이 t0+1 에(0x1004ac62~). 판이 작아 M ≤ 0 이면 없다.
        int offCount = host.Cols * host.Rows / 20 - 35;
        bool anyOutside = minCol > 0 || minRow > 0 || maxCol < host.Cols - 1 || maxRow < host.Rows - 1;
        for (int j = 0; j < offCount && anyOutside; j++)
        {
            int reach = Rand(4) != 0 ? 4 : 3, col, row;
            do { col = Rand(host.Cols); row = Rand(host.Rows); }
            while (col >= minCol && col <= maxCol && row >= minRow && row <= maxRow);
            Blast(1, col, row, reach);
            if (Rand(2) == 1)
            {
                int r = Rand(3), f1 = 1 + Rand(200) + 100, hold = Rand(10) + 20, fc = col, fr = row;
                Later(f1, () => host.Btl.Part1Objects.AddFire(fc, fr, r, hold));
            }
        }

        for (int i = 0; i < 35; i++)
        {
            bool small = Rand(3) != 0;
            int col = minCol + Rand(Math.Max(1, maxCol - minCol)), row = minRow + Rand(Math.Max(1, maxRow - minRow));
            int k = row - minRow, d = 15 * i + Rand(10);
            var (px, py) = CellFoot(col, row);
            // 날아오는 길 — 맞을 자리에서 (−(40k+20), −16−60k) 화면 픽셀 위에서 출발한다(Δx = −(40k+20), Δz = +100k).
            var start = (px - (40 * k + 20), py - 16 - 60 * k);
            var flight = Move(start, (px, py), 1, Rand(4) + 5);
            int impact = d + flight.Count;
            Sound(small ? 61 : 60, d);
            Along(115, small ? 3 : 0, d, AsPath(flight.Prepend(start).ToList()), life: flight.Count);
            for (int t = 0; t < flight.Count; t += 3)
            {
                var (fx, fy) = flight[t];
                Along(Rand(2) == 0 ? 34 : 64, 0, d + t, AsPath(Move((fx, fy), (fx - Rand(150) - 20, fy - Rand(30) - 40), Rand(3) + 1, 1.2), 2));
            }
            Sound(small ? 63 + Rand(2) : 62, impact);
            Put(123, 5, impact + 2, px, py);
            for (int n = 0; n < 2; n++) Debris(228, Rand(3), impact + 2, px, py, 15, 1.0, 30);
            if (small) Put(116, 0, impact + 2, px, py);
            else { Put(124, 0, impact + 2, px, py); Put(124, 18, impact + 2, px, py); }
            int end = impact + 2 + Math.Max(Length(123, 5), Length(small ? 116 : 124, 0));
            // 반쯤 땅불(0x1007b970, 공격 0x4e = 화이어 필드의 것, vt+0xfc(rand%10+20), 0x1004a9ca~) — 판의 기운으로 넘겨 시간 틱 동안 타고 23 r+3 → 96 r → 재로 사그라진다(Part1MapObjects).
            // 원본은 칸을 걸을 수 있고 운석이 땅에 닿았을 때만 붙인다 — 그 거르기는 안 옮겼다 [H].
            if (Rand(2) == 1)
            {
                int r = Rand(3), f1 = impact + 2, hold = Rand(10) + 20, fc = col, fr = row;
                Later(f1, () => host.Btl.Part1Objects.AddFire(fc, fr, r, hold));
                end = Math.Max(end, f1 + 2);
            }
            // 폭발 1틱 뒤 — 맨해튼 3(큰 것, 원본 값 12) · 4(작은 것, 16) 칸 안의 적을 한 번씩.
            Blast(impact + 3, col, row, small ? 4 : 3);
            last = Math.Max(last, end);
        }
        return last + 2;
    }

    /// <summary>칸 (<paramref name="col"/>, <paramref name="row"/>)에서 유닛이 차지한 칸까지 가장 가까운 맨해튼 거리 — 큰 유닛(둘레 3×3)은 아홉 칸 중 가장 가까운 것.</summary>
    private int ReachOf(UnitState u, int col, int row)
    {
        int best = Math.Abs(u.Col - col) + Math.Abs(u.Row - row);
        if (!host.Btl.Part1Big(u)) return best;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                best = Math.Min(best, Math.Abs(u.Col + dx - col) + Math.Abs(u.Row + dy - row));
        return best;
    }
}
