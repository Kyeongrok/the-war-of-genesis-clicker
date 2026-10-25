using System.Runtime.CompilerServices;

namespace DuelDx;

/// <summary>
/// 파트1 차례 유닛의 반짝이는 테두리. 원본(alpha.dll)은 유닛 그리기 <c>0x10034900</c>~ 에서 깃발 <c>+0x25e</c> 가 서 있으면
/// 같은 컷을 <b>실루엣</b>(섞기 0x1f — <c>0x1001ae70</c> → <c>0x10011250</c>: 투명색이 아닌 픽셀을 한 색으로 칠함)으로
/// 화면 (x, y−1) · (x, y+1) · (x−1, y) · (x+1, y) 네 번, 정렬 키 −1(몸 뒤)로 찍는다(<c>0x10034c54</c>~<c>0x10034f3d</c>)
/// — 곧 십자 이웃 1픽셀 테두리다.
/// <para>색(<c>0x10034be7</c>~): t = 유닛 틱 <c>+0x3c</c>, t % 20 &lt; 10 이면 v = 30 + 20·(t % 10), 아니면 v = 230 − 20·(t % 10)
/// (30 → 210, 230 → 50 의 20틱 세모꼴). 565 는 <c>((v &amp; 0xfff8) | 0xfc00) &lt;&lt; 3</c>, 555 는 <c>((v &amp; 0xfff8) | 0x1c00) &lt;&lt; 2</c>
/// = 빨강 28/31 · 초록 v · 파랑 0 — 붉은 주황(230, 30, 0)과 금빛(230, 230, 0) 사이를 오간다.</para>
/// <para>리메이크는 사용자 요청으로 빨강 쪽을 빼고 <b>주황(230, 130, 0) ↔ 노랑(230, 230, 0)</b> 만, 빠르기도 늦춰 <b>48틱(1.6초)</b> 세모꼴로 오간다.
/// 모드 &gt; 일반 「파트1 차례 테두리」로 켠다(기본 끔).</para>
/// <para>깃발은 틱마다(<c>0x100350c7</c>~) 유닛 번호 <c>+0x1ec</c> 가 판의 지금 차례 유닛 <c>+0xb9ac</c> 와 같으면 1, 아니면 0 —
/// 편을 가리지 않고 차례인 유닛 하나에만, 움직이고 치는 동안에도 선다.</para>
/// 리메이크는 컷마다 테두리 픽셀 자리를 한 번만 셈해 둔다(뒤집은 컷은 따로 있는 <see cref="SpriteFrame"/> 라 따로 담긴다).
/// </summary>
internal static class Part1TurnOutline
{
    /// <summary>컷 → 테두리 픽셀 자리(컷 왼쪽 위 기준, (y + 1)·(W + 2) + (x + 1) 로 묶음).</summary>
    static readonly ConditionalWeakTable<SpriteFrame, int[]> Cache = [];

    /// <summary>한 번 오가는 틱 수 — 원본은 20틱, 사용자 요청으로 느리게.</summary>
    internal const int Period = 48;

    /// <summary>틱 t 의 테두리 색(BGRA, 불투명) — 초록만 130 → 230 → 130 으로 오간다(빨강 230 · 파랑 0 은 원본 그대로).</summary>
    internal static uint ColorAt(int tick)
    {
        int t = (int)((uint)tick % Period), half = Period / 2, phase = t < half ? t : Period - t;
        int g = 130 + 100 * phase / half;
        const uint R = 28 * 255 / 31;
        return 0xFF000000 | R << 16 | (uint)g << 8;
    }

    /// <summary>컷 <paramref name="f"/> 를 (x, y)(컷 왼쪽 위) 에 그릴 때의 테두리를 먼저 찍는다 — 몸은 그 위에 그린다.</summary>
    internal static void Draw(GameWindow host, SpriteFrame f, int x, int y, int tick)
    {
        var edge = Cache.GetValue(f, Build);
        uint color = ColorAt(tick);
        int stride = f.W + 2;
        foreach (int p in edge)
            host.SetPixel(x + p % stride - 1, y + p / stride - 1, color);
    }

    static int[] Build(SpriteFrame f)
    {
        bool Solid(int xx, int yy) => (uint)xx < f.W && (uint)yy < f.H && (f.Px[yy * f.W + xx] & 0xFF000000) != 0;
        var list = new List<int>();
        for (int yy = -1; yy <= f.H; yy++)
            for (int xx = -1; xx <= f.W; xx++)
                if (!Solid(xx, yy) && (Solid(xx - 1, yy) || Solid(xx + 1, yy) || Solid(xx, yy - 1) || Solid(xx, yy + 1)))
                    list.Add((yy + 1) * (f.W + 2) + xx + 1);
        return [.. list];
    }
}
