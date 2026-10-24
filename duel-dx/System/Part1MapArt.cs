using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 챕터 지도 화면 · 상태 창 · 교환 창이 같이 쓰는 그리기 — 원본 그림(Obs 11 단추 · Obs 111 틀 상자 · Obs 226 꼬리표)을 판에 찍고 글은 UI 층에.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-챕터」 13절. 그림은 모두 판(<c>_fb</c>)에, 글은 <see cref="GameWindow.UiText"/>(새 UI 면 그 위 층)에 — 그래서 창이 떠 있는 동안
/// 지도 쪽 글(장소 이름 · 단추 글 · 「N 팀」)은 그리지 않는다(층이 판 위라 창을 뚫고 보인다).
/// </remarks>
internal sealed class Part1MapArt(GameWindow host)
{
    /// <summary>그림을 이 네모 안으로만(640×480 화면) — 폭이 0 이면 판 전체.</summary>
    internal (int X, int Y, int W, int H) Clip;

    internal const uint White = 0xFFFFFFFF, Gray = 0xFF7F7F7F;

    /// <summary>Obs 의 벌 · 장 하나 그대로(모션표를 거치지 않는다).</summary>
    internal SpriteFrame? Frame(int obs, int sub, int slot) =>
        host.UiFor(Part1Game.ObsBase + obs) is { } s && s._frames.TryGetValue((sub, slot), out var f) ? f : null;

    /// <summary>모션 틱의 장.</summary>
    internal SpriteFrame? Motion(int obs, int motion, int tick = 0) => host.UiFor(Part1Game.ObsBase + obs)?.FrameAt(motion, tick);

    /// <summary>장 하나를 기준점 (x, y) 에 — <paramref name="alpha"/> 256 이 불투명, <paramref name="add"/> 는 밝기 더하기(단추 출렁임).</summary>
    internal void Blit(SpriteFrame? f, int x, int y, int alpha = 256, int add = 0, bool dim = false)
    {
        if (f is null || f.W == 0 || f.H == 0) return;
        int left = x + f.X, top = y + f.Y;
        var (x0, y0, x1, y1) = Bounds(left, top, f.W, f.H);
        for (int py = y0; py < y1; py++)
            for (int px = x0; px < x1; px++)
                Put(px, py, f.Px[(py - top) * f.W + px - left], alpha, add, dim);
    }

    /// <summary>장을 네모에 깐다(되풀이) — 틀 상자의 돌 무늬.</summary>
    internal void Tile(SpriteFrame? f, int x, int y, int w, int h, int alpha = 256)
    {
        if (f is null || f.W == 0 || f.H == 0) return;
        var (x0, y0, x1, y1) = Bounds(x, y, w, h);
        for (int py = y0; py < y1; py++)
        {
            int row = (py - y) % f.H * f.W;
            for (int px = x0; px < x1; px++) Put(px, py, f.Px[row + (px - x) % f.W], alpha, 0, false);
        }
    }

    private (int X0, int Y0, int X1, int Y1) Bounds(int x, int y, int w, int h)
    {
        int bw = host.BoardWidth, bh = host.BoardHeight;
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(bw, x + w), y1 = Math.Min(bh, y + h);
        if (Clip.W > 0)
        {
            x0 = Math.Max(x0, Clip.X); y0 = Math.Max(y0, Clip.Y);
            x1 = Math.Min(x1, Clip.X + Clip.W); y1 = Math.Min(y1, Clip.Y + Clip.H);
        }
        return (x0, y0, x1, y1);
    }

    private void Put(int px, int py, uint c, int alpha, int add, bool dim)
    {
        if (c >> 24 == 0) return;
        int r = (int)(c >> 16 & 0xFF), g = (int)(c >> 8 & 0xFF), b = (int)(c & 0xFF);
        if (dim) { r = r * 15 / 31; g = g * 15 / 31; b = b * 15 / 31; }
        if (add != 0) { r = Math.Min(255, r + add); g = Math.Min(255, g + add); b = Math.Min(255, b + add); }
        int i = py * host.BoardWidth + px;
        if (alpha < 256)
        {
            uint d = host._fb[i];
            r = (r * alpha + (int)(d >> 16 & 0xFF) * (256 - alpha)) >> 8;
            g = (g * alpha + (int)(d >> 8 & 0xFF) * (256 - alpha)) >> 8;
            b = (b * alpha + (int)(d & 0xFF) * (256 - alpha)) >> 8;
        }
        host._fb[i] = 0xFF000000u | (uint)r << 16 | (uint)g << 8 | (uint)b;
    }

    internal void Fill(int x, int y, int w, int h, uint color)
    {
        var (x0, y0, x1, y1) = Bounds(x, y, w, h);
        if (x1 > x0 && y1 > y0) host.FillRect(x0, y0, x1 - x0, y1 - y0, color);
    }

    internal void Stroke(int x, int y, int w, int h, uint color)
    {
        Fill(x, y, w, 1, color);
        Fill(x, y + h - 1, w, 1, color);
        Fill(x, y, 1, h, color);
        Fill(x + w - 1, y, 1, h, color);
    }

    internal int TextW(string text, float size = 12) => host.UiGetText(text, White, size).W;

    internal int TextH(string text, float size = 12) => host.UiGetText(text, White, size).H;

    internal void Text(string text, int x, int y, uint color = White, float size = 12) => host.UiText(text, x, y, color, size);

    internal void TextCentered(string text, int cx, int cy, uint color = White, float size = 12)
    {
        var (_, w, h) = host.UiGetText(text, color, size);
        host.UiText(text, cx - w / 2, cy - h / 2, color, size);
    }

    internal void TextRight(string text, int right, int y, uint color = White, float size = 12) => host.UiRightText(text, right, y, color, size);

    // ── 단추(Obs 11 벌 3, 클래스 0x10109e44) ──

    /// <summary>단추 맞음 칸 — 가운데 ±50 · ±15.</summary>
    internal static bool ButtonHit(int cx, int cy, int bx, int by) => Math.Abs(bx - cx) < 50 && Math.Abs(by - cy) < 15;

    /// <summary>
    /// 지도 단추 — 몸(벌 3 장 0, 104×30 반투명) 위에 테두리(장 1) · 가운데 흰 글(못 쓰면 회색). 짚으면 몸 밝기가 32틱 세모꼴(0→30→0)로 출렁인다.
    /// </summary>
    internal void Button(int cx, int cy, string label, bool hot, bool disabled = false)
    {
        int add = 0;
        if (hot && !disabled)
        {
            int t = (int)(host._lastTime * GameWindow.TicksPerSecond) % 32;
            add = (t < 16 ? t : 32 - t) * 8;   // 원본은 5비트 밝기 0 → 30 → 0 — 8비트로 0 → 128 → 0
        }
        Blit(Frame(11, 3, 0), cx, cy, 150, add);
        Blit(Frame(11, 3, 1), cx, cy);
        TextCentered(label, cx, cy, disabled ? Gray : White, 13);
    }

    // ── 틀 상자(0x100c52b0 — Obs 111: 벌 0 파란 돌 무늬 100×50 · 벌 2 모서리 5×5 · 흰 테두리) ──

    internal void Box(int x, int y, int w, int h)
    {
        Tile(Frame(111, 0, 0), x, y, w, h, 220);
        Stroke(x + 3, y + 3, w - 6, h - 6, 0xFFE8ECF8);
        Blit(Frame(111, 2, 0), x + 1, y + 1);
        Blit(Frame(111, 2, 1), x + w - 2, y + 1);
        Blit(Frame(111, 2, 2), x + 1, y + h - 2);
        Blit(Frame(111, 2, 3), x + w - 2, y + h - 2);
    }

    /// <summary>창 안의 목록 칸 — 어두운 바탕과 밝은 테.</summary>
    internal void Panel(int x, int y, int w, int h)
    {
        Fill(x, y, w, h, 0x900A0C28);
        Stroke(x, y, w, h, 0xFFC8D0E8);
    }

    /// <summary>꼬리표(「1 팀」 · 「본  부」) — 글폭+20 × 글높이+8, 글은 (10, 4).</summary>
    internal void Tag(int x, int y, string text)
    {
        int w = TextW(text) + 20, h = TextH(text) + 8;
        Fill(x, y, w, h, 0xF0101438);
        Stroke(x, y, w, h, 0xFFE8ECF8);
        Text(text, x + 10, y + 4);
    }

    /// <summary>세로 스크롤 띠(목록 오른쪽) — 위 · 아래 화살(Obs 111 벌 3 장 0 · 1).</summary>
    internal void ScrollBar(int x, int y, int h)
    {
        Fill(x, y, 16, h, 0xC0181C40);
        Stroke(x, y, 16, h, 0xFF8890B8);
        Blit(Frame(111, 3, 0), x, y);
        Blit(Frame(111, 3, 1), x, y + h - 16);
    }

    /// <summary>스크롤 띠의 위(−1) · 아래(+1) 화살을 눌렀나.</summary>
    internal static int ScrollHit(int x, int y, int h, int bx, int by) =>
        bx < x || bx >= x + 16 ? 0 : by >= y && by < y + 16 ? -1 : by >= y + h - 16 && by < y + h ? 1 : 0;

    // ── 인물 그림 ──

    private readonly Dictionary<int, Part1Chr?> _chrs = [];

    /// <summary>인물 레코드(20000 + 파트1 번호) — 한 번 읽어 둔다(얼굴 · 이름 · 전투 그림 번호는 바뀌지 않는다).</summary>
    internal Part1Chr? Chr(int code)
    {
        if (!_chrs.TryGetValue(code, out var chr)) _chrs[code] = chr = Part1Chr.Load(code - Part1Game.ChrBase);
        return chr;
    }

    /// <summary>얼굴(인물 +0x48 의 Obs 모션 0, 50×50 가운데 기준)을 (cx, cy) 가운데에.</summary>
    internal bool Face(int code, int cx, int cy)
    {
        if (Chr(code) is not { FaceId: > 0 } chr || Motion(chr.FaceId, 0) is not { } f) return false;
        Blit(f, cx, cy);
        return true;
    }
}
