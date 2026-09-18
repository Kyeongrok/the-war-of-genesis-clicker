namespace DuelDx;

/// <summary>
/// 게임 안 모든 창이 쓰는 원본 창 틀(fa-11 분석) — 바탕·테두리·제목줄·귀퉁이 장식.
/// </summary>
/// <remarks>
/// 옵시디안 분석-시스템메뉴 「메시지 창 틀 (fa-11)」: 창 하나는 <c>0x10041700</c>(틀) + <c>0x1003ff90</c>(바탕 갈래)
/// + <c>0x10040a60(2)</c>(귀퉁이 갈래) 세 줄로 만든다. 바탕 갈래는 아무도 안 고쳐 <b>늘 0 = Obs 0970 모션 7</b>
/// (2×184 그라데이션을 창 크기로 늘려 섞기 2·세기 16 으로 반투명하게 덮는다).
/// 테두리는 흰 1픽셀 사각형 <c>(x−1, y−31)~(x+w, y+h)</c>(제목이 없으면 <c>y−1</c>), 제목줄 높이는 30 이고 그 밑에 가로선이 있다.
/// 귀퉁이는 Obs 0970 모션 0 왼위·1 오른위·2 왼아래·3 오른아래, 모션 4 는 제목줄 왼쪽 「—●」 막대(제목이 있을 때만).
/// 제목 글은 제목줄 안 가운데·흰색이다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int FrameObs = 970, FrameTitleH = 30;
    private const int FrameBlend = 16, FrameBlendMax = 31;

    /// <summary>원본 창 틀을 그린다. (x, y) 는 <b>본문</b> 왼위 — 제목줄은 그 위 30픽셀을 더 쓴다.</summary>
    private void DrawGameFrame(int x, int y, int w, int h, string? title = null)
    {
        bool titled = title != null;
        int top = titled ? y - FrameTitleH : y, full = titled ? h + FrameTitleH : h;

        DrawFrameBackground(x, top, w, full);
        StrokeRect(x - 1, top - 1, w + 2, full + 2, White);

        if (titled)
        {
            for (int xx = x; xx < x + w; xx++) SetPixel(xx, y - 1, White);      // 제목줄 밑 가로선
            DrawUi(FrameObs, 4, 0, x, y - 18, UiBlend.Alpha);                   // 제목줄 왼쪽 막대
            var (_, tw, th) = GetText(title!, White, 15);
            DrawText(title!, x + (w - tw) / 2, top + (FrameTitleH - th) / 2, White, 15);
        }

        DrawUi(FrameObs, 0, 0, x, top, UiBlend.Alpha);
        DrawUi(FrameObs, 1, 0, x + w - 1, top, UiBlend.Alpha);
        DrawUi(FrameObs, 2, 0, x, y + h - 1, UiBlend.Alpha);
        DrawUi(FrameObs, 3, 0, x + w - 1, y + h - 1, UiBlend.Alpha);
    }

    /// <summary>바탕 — Obs 0970 모션 7 의 2×184 그라데이션을 창 크기로 늘려 반투명하게 덮는다.</summary>
    private void DrawFrameBackground(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        if (UiFor(FrameObs)?.FrameAt(7, 0) is not { } f || f.W == 0 || f.H == 0)
        {
            FillRect(x, y, w, h, PanelBg);   // 그림이 없으면 예전 바탕색으로
            return;
        }
        for (int yy = 0; yy < h; yy++)
        {
            int py = y + yy;
            if ((uint)py >= BoardHeight) continue;
            int sy = Math.Min(f.H - 1, yy * f.H / h);
            for (int xx = 0; xx < w; xx++)
            {
                int px = x + xx;
                if ((uint)px >= BoardWidth) continue;
                uint c = f.Px[sy * f.W + Math.Min(f.W - 1, xx * f.W / w)];
                int i = py * BoardWidth + px;
                _fb[i] = MixColor(_fb[i], c, FrameBlend, FrameBlendMax);
            }
        }
    }

    /// <summary>그림 한 장을 칸 크기에 맞춰 줄여 그린다(가장 가까운 픽셀).</summary>
    private void BlitScaled(SpriteFrame frame, int x, int y, int w, int h)
    {
        if (frame.W == 0 || frame.H == 0 || w <= 0 || h <= 0) return;
        double scale = Math.Min((double)w / frame.W, (double)h / frame.H);
        int dw = Math.Max(1, (int)(frame.W * scale)), dh = Math.Max(1, (int)(frame.H * scale));
        int left = x + (w - dw) / 2, top = y + (h - dh) / 2;
        for (int yy = 0; yy < dh; yy++)
        {
            int py = top + yy;
            if ((uint)py >= BoardHeight) continue;
            int sy = yy * frame.H / dh;
            for (int xx = 0; xx < dw; xx++)
            {
                int px = left + xx;
                if ((uint)px >= BoardWidth) continue;
                uint c = frame.Px[sy * frame.W + xx * frame.W / dw];
                if ((c & 0xFF000000) != 0) _fb[py * BoardWidth + px] = c | 0xFF000000;
            }
        }
    }

    /// <summary>글을 읽는 창 밑은 먼저 어둡게 깐다 — 반투명 바탕만으로는 배경 그림이 비쳐 글이 안 읽힌다.</summary>
    private void DarkenRect(int x, int y, int w, int h, int num = 8, int den = 31)
    {
        for (int yy = y; yy < y + h; yy++)
        {
            if ((uint)yy >= BoardHeight) continue;
            for (int xx = x; xx < x + w; xx++)
            {
                if ((uint)xx >= BoardWidth) continue;
                int i = yy * BoardWidth + xx;
                _fb[i] = ScaleColor(_fb[i], num, den);
            }
        }
    }

    /// <summary>바탕과 그림을 세기만큼 섞는다(원본 섞기 방식 2).</summary>
    private static uint MixColor(uint dst, uint src, int strength, int max)
    {
        uint Ch(int shift) => (uint)(((int)(dst >> shift & 0xFF) * (max - strength) + (int)(src >> shift & 0xFF) * strength) / max);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }
}
