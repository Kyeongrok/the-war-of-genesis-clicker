namespace DuelDx;

/// <summary>
/// 파트1 링 「행군」 아이콘 — 원본에 없는 명령이라 원본 그림이 없다(파트1 차례 메뉴는 글 단추 넷, 분석-파트1-전투 24절 · 파트2 링 Obs 0087~0092 에도 걷는 그림이 없다).
/// 다른 링 아이콘(Obs 0087~0092 벌 1)과 같은 꼴로 손으로 그린 24×20 점 그림: 깃발(제비꼬리 군기)을 든 사람이 오른쪽으로 걷는다.
/// 빛깔은 게임 화면에 찍힌 링 아이콘(상태 0088 · 아이템 0089)에서 떠 온 짙은 파랑(본색 0x0030B0 · 그늘 0x001A90 · 밝은 테 0x2060D8, 더하기 섞기). 7틱마다 발을 바꾸고, 원본 아이콘의 숨쉬기(모션 1, 27틱)처럼 밝기가 오르내린다.
/// </summary>
internal static class MarchIcon
{
    private static readonly string[][] Poses =
    [
        [
            ".......S................",
            "..HHHHHS................",
            "..HMMMMS................",
            "...HMMMS................",
            "....HMMS.......HH.......",
            "...HMMMS......HMMS......",
            "..HMMMMS......MMMS......",
            "..HS...S.......MS.......",
            ".......S.....HMMMS......",
            ".......S....HMMMMMS.....",
            ".......SMMMMMMMMMMSM....",
            ".......S....MMMMMS..M...",
            "..LLLL.S....MMMMMS......",
            ".......S....MMMMMM......",
            "............MMS.MMS.....",
            ".LLLL......MMS...MS.....",
            "...........MS.....MS....",
            "..........MS......MS....",
            ".........MS........MS...",
            ".........M.........MMS..",
        ],
        [
            ".......S................",
            "..HHHHHS................",
            "..HMMMMS................",
            "...HMMMS................",
            "....HMMS.......HH.......",
            "...HMMMS......HMMS......",
            "..HMMMMS......MMMS......",
            "..HS...S.......MS.......",
            ".......S.....HMMMS......",
            ".......S....HMMMMMS.....",
            ".......SMMMMMMMMMMSM....",
            ".......S....MMMMMS..M...",
            "...LLLLS....MMMMMS......",
            ".......S....MMMMMM......",
            "............MMS.MS......",
            "..LLLL......MMS.MS......",
            "............MS..MS......",
            "............MS...MS.....",
            "...........MS....MS.....",
            "...........MMS...MMS....",
        ],
    ];

    private const int Steps = 8, PoseTicks = 7, BreathTicks = 27;
    private static readonly SpriteFrame[] Frames = Build();

    /// <summary>링이 열린 뒤 <paramref name="tick"/> 틱째 그림 — 기준점이 아이콘 가운데(다른 링 아이콘 컷처럼 (−12, −14)).</summary>
    internal static SpriteFrame Frame(int tick)
    {
        int pose = (tick / PoseTicks) & 1;
        // 0 → 1 → 0 으로 오르내리는 숨 — 원본 모션 1 이 장 0~7 을 오가는 것과 같은 결.
        int phase = tick % BreathTicks, step = (int)Math.Round((Steps - 1) * (1 - Math.Abs(2.0 * phase / BreathTicks - 1)));
        return Frames[pose * Steps + step];
    }

    private static SpriteFrame[] Build()
    {
        var frames = new SpriteFrame[Poses.Length * Steps];
        for (int p = 0; p < Poses.Length; p++)
            for (int s = 0; s < Steps; s++)
            {
                string[] rows = Poses[p];
                int w = rows[0].Length, h = rows.Length;
                double k = 0.82 + 0.3 * s / (Steps - 1);   // 숨 밝기 0.82 ~ 1.12
                var px = new uint[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (rows[y][x] switch { 'M' => 0x0030B0u, 'S' => 0x001A90u, 'H' => 0x2060D8u, 'L' => 0x00209Cu, _ => 0u } is var c and not 0)
                            px[y * w + x] = 0xFF000000 | Scale(c, 16, k) << 16 | Scale(c, 8, k) << 8 | Scale(c, 0, k);
                frames[p * Steps + s] = new SpriteFrame(px, w, h, -12, -14);
            }
        return frames;
    }

    private static uint Scale(uint c, int shift, double k) => (uint)Math.Min(255, (int)((c >> shift & 0xFF) * k));
}
