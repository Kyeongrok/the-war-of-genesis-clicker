using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1(alpha.dll) 섞기 — 원본 그리개 <c>0x1000c4f0</c>(방식 → 표 <c>0x1000c7d4</c>)가 고르는 5비트 채널 색표를 같은 식으로 미리 만들어 둔다.
/// 색표는 생성기 <c>0x1000b8d0</c> 를 에뮬레이터로 돌려 읽은 값과 1024 칸 모두 같다(볼트 분석-파트1-전투 「섞기 색표」).
/// <list type="bullet">
/// <item>방식 1~12 · 30(<c>0x10012190</c>) — 표[바탕 &lt;&lt; 5 | 그림](블리터 <c>0x1000f220</c> · <c>0x10010260</c>: 윗자리가 바탕). 세기는 안 쓴다.</item>
/// <item>방식 13(<c>0x10012660</c>) — 세기 1~8 만: <c>(바탕·(31−k) + 그림·k)/31</c>, k = min(31, 4·세기). 세기 0 · 9 이상은 <b>안 그린다</b>.</item>
/// <item>방식 14~29(<c>0x10012960</c> → <c>0x1000fd70</c>) — 바탕은 안 보고 그림만 표[그림 &lt;&lt; 5 | 세기]로 물들여 덮는다. 채널마다 표 갈래(그대로 · 밝게 · 어둡게 · 무르게 어둡게)가 다르다.</item>
/// <item>방식 0 은 그대로, 31 이상은 안 그린다.</item>
/// </list>
/// 판은 8비트 채널이라 5비트로 줄여(<c>&gt;&gt; 3</c>) 표를 찾고 되늘린다(<c>v &lt;&lt; 3 | v &gt;&gt; 2</c>). 결과가 바탕(또는 그림)의 5비트 값과 같으면 그 8비트 값을
/// 그대로 둔다 — 검은 그림을 더할 때 그림 네모 안의 바탕이 5비트로 깎여 네모가 비치지 않게.
/// </summary>
internal static class Part1Blend
{
    /// <summary>한 키를 그리는 법 — 둘 섞기 표(<see cref="Bin"/>) 또는 채널마다 물들이기 표(<see cref="R"/>·<see cref="G"/>·<see cref="B"/>, 세기는 <see cref="Level"/>).</summary>
    internal sealed class Plan
    {
        internal byte[]? Bin, R, G, B;
        internal int Level;
        /// <summary>안 그린다(방식 13 의 세기 0 · 9 이상, 방식 31).</summary>
        internal bool None;
        /// <summary>그대로 덮는다(방식 0).</summary>
        internal bool Copy;

        internal uint Mix(uint d, uint c)
        {
            if (Copy) return c | 0xFF000000;
            if (Bin is { } t) return 0xFF000000 | Two(t, d, c, 16) << 16 | Two(t, d, c, 8) << 8 | Two(t, d, c, 0);
            return 0xFF000000 | One(R!, c, 16, Level) << 16 | One(G!, c, 8, Level) << 8 | One(B!, c, 0, Level);
        }

        static uint Two(byte[] t, uint d, uint c, int shift)
        {
            int d8 = (int)(d >> shift & 0xFF), s8 = (int)(c >> shift & 0xFF), d5 = d8 >> 3, s5 = s8 >> 3, r = t[d5 << 5 | s5];
            return (uint)(r == d5 ? d8 : r == s5 ? s8 : Expand[r]);
        }

        static uint One(byte[] t, uint c, int shift, int level)
        {
            int s8 = (int)(c >> shift & 0xFF), s5 = s8 >> 3, r = t[s5 << 5 | level];
            return (uint)(r == s5 ? s8 : Expand[r]);
        }
    }

    static readonly byte[] Expand = [.. Enumerable.Range(0, 32).Select(v => (byte)(v << 3 | v >> 2))];

    static byte[] Table(Func<int, int, int> f)
    {
        var t = new byte[1024];
        for (int h = 0; h < 32; h++)
            for (int l = 0; l < 32; l++) t[h << 5 | l] = (byte)Math.Clamp(f(h, l), 0, 31);
        return t;
    }

    static int Dodge(int d, int s) => Math.Min(31, 32 * d / (32 - s));
    static int Screen(int d, int s) => Math.Min(31, d + s * (31 - d) / 31);

    /// <summary>방식 1~12 · 30 — (바탕 d, 그림 s). 표 주소는 B 채널 표(G · R 은 +0x800 · +0x1000 에 같은 값을 밀어 둔 것).</summary>
    static readonly Dictionary<int, Func<int, int, int>> Binary = new()
    {
        [1] = (d, s) => d >> 1,                                       // 0x1013adcc 바탕 반(그림자)
        [2] = Dodge,                                                  // 0x101455cc 닷지 32d/(32−s)
        [3] = Math.Max,                                               // 0x10143dcc 밝은 쪽
        [4] = Screen,                                                 // 0x10140dcc 스크린 d + s(31−d)/31
        [5] = (d, s) => Screen(Dodge(d, s), s),                       // 0x1013f5cc 닷지 뒤 스크린
        [6] = (d, s) => d * (32 - s) * (32 - s) * (32 - s) >> 15,     // 0x101425cc 번(어둡게)
        [7] = (d, s) => Math.Abs(d - s),                              // 0x1013ddcc 차이
        [8] = Math.Min,                                               // 0x1013c5cc 어두운 쪽
        [9] = (d, s) => Math.Min(31, d + s),                          // 0x101245cc 더하기
        [10] = (d, s) => Math.Min(31, d + s * s / 31),                // 0x10122dcc 빛(제곱 더하기)
        [11] = Screen,                                                // 0x101215cc 스크린(4 와 같은 값)
        [12] = (d, s) => Math.Min(31, d * (31 - s) / 31 + s * (31 - d) / 31), // 0x1011fdcc 배제(뒤집기)
        [30] = (d, s) => d * s / 31,                                  // 0x101395cc 곱하기
    };

    /// <summary>물들이기 표 갈래 — (그림 p, 세기 L).</summary>
    static readonly byte[] Same = Table((p, _) => p),                                        // 0x10131dcc
        Up = Table((p, l) => p + l * (31 - p) / 31),                                       // 0x101365cc 밝게(회색 스크린)
        Down = Table((p, l) => p * (31 - l) / 31),                                         // 0x10137dcc 어둡게
        Soft = Table((p, l) => p * (31 - l * ((31 - p) / 4 + 3) / 10) / 31);               // 0x10134dcc 어두운 색일수록 더 어둡게

    /// <summary>방식 14~29 의 (R, G, B) 표 갈래 — <c>0x10012960</c> 의 점프표 <c>0x10012df4</c> 가 미는 표.</summary>
    static readonly (byte[] R, byte[] G, byte[] B)[] Tints =
    [
        (Up, Up, Up),          // 14 하얗게(맞을 때)
        (Down, Down, Down),    // 15 어둡게
        (Up, Same, Same),      // 16 빨강 밝게
        (Same, Up, Same),      // 17 초록 밝게
        (Same, Same, Up),      // 18 파랑 밝게
        (Up, Up, Same),        // 19 노랑 밝게
        (Up, Same, Up),        // 20 자주 밝게
        (Same, Up, Up),        // 21 하늘 밝게
        (Same, Down, Down),    // 22 붉게(치명)
        (Down, Same, Down),    // 23 초록빛
        (Down, Down, Same),    // 24 파랗게
        (Same, Same, Down),    // 25 노랗게
        (Same, Down, Same),    // 26 자줏빛
        (Down, Same, Same),    // 27 하늘빛
        (Same, Soft, Down),    // 28 주황빛
        (Soft, Soft, Same),    // 29 푸른빛(빨강 · 초록을 무르게)
    ];

    static readonly Plan?[] Plans = new Plan?[1024];
    static readonly Dictionary<int, byte[]> BinaryTables = Binary.ToDictionary(p => p.Key, p => Table(p.Value));
    static readonly byte[]?[] Trans = [null, .. Enumerable.Range(1, 8).Select(n => Table((d, s) => (d * (31 - Math.Min(31, 4 * n)) + s * Math.Min(31, 4 * n)) / 31))];

    /// <summary>파트1 섞기 키(<see cref="Part1Game.BlendKey"/>)인가.</summary>
    internal static bool Is(int key) => key >= Part1Game.BlendKeyBase;

    /// <summary>그 키를 그리는 법 — 파트1 키가 아니면 null.</summary>
    internal static Plan? PlanOf(int key)
    {
        if (!Is(key)) return null;
        int i = key & 1023;
        if (Plans[i] is { } done) return done;
        int mode = i >> 5, level = i & 31;
        var plan = new Plan();
        if (mode == 0) plan.Copy = true;
        else if (BinaryTables.TryGetValue(mode, out var bin)) plan.Bin = bin;
        else if (mode == 13 && level is >= 1 and <= 8) plan.Bin = Trans[level];
        else if (mode is >= 14 and <= 29) (plan.R, plan.G, plan.B, plan.Level) = (Tints[mode - 14].R, Tints[mode - 14].G, Tints[mode - 14].B, level);
        else plan.None = true;
        return Plans[i] = plan;
    }

    /// <summary>파트1 키를 판 섞기(<see cref="GameWindow.UiBlend"/>)로 — 값을 그대로 담는다.</summary>
    internal static GameWindow.UiBlend AsUiBlend(int key) => (GameWindow.UiBlend)key;
}
