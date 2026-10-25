namespace DuelDx;

/// <summary>
/// 파트1 필드 말풍선(209) · 해설 띠(210) — 원본 창 <c>0x100a7ef0</c>(vtable <c>0x1010a6e4</c>, 그리기 <c>0x100a8ba0</c>, 틱 <c>0x100a8670</c>)를 옮긴 것.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-챕터」 11.5절. 틀은 창 바탕 <c>0x100a18b0</c> — <b>Obs 111 벌 0</b>(파란 돌 무늬 100×50)을 창에 깔아 섞기 13 · 단계 6(24/31)으로 비치게,
/// 흰 1픽셀 테두리(모서리에서 5 픽셀씩 비움) · 벌 2 모서리 5×5 넷. <b>꼬리는 없다.</b> 창 왼위 = (인물 x, 인물 y − 높이 − 50) 를 화면 안으로 밀고,
/// 얼굴(Chr 칸 4 반신 초상)은 (창 x − 얼굴 폭 + 20, 창 밑 − 얼굴 높이) 에 창보다 먼저(밑에) 찍는다. 8틱 동안 인물 자리에서 커지며 나오고, 4틱 동안 줄며 사라진다.
/// 글은 틱마다 한 글자, 쪽을 다 찍으면 ▼(Obs 111 모션 0) 이 뜨고 정한 틱이 지나면 다음 쪽 · 닫기.
/// </remarks>
internal sealed class Part1Bubble(GameWindow host, Part1MapArt art)
{
    private const int FrameObs = 111;

    /// <summary>바탕 섞기 13 단계 6 — <c>(7·바탕 + 24·그림)/31</c>.</summary>
    private const int FrameAlpha = 24 * 256 / 31;

    /// <summary>글꼴 — 이름 /m0 은 꼴 3(굴림 12pt 굵게) · 빛깔 7, 몸 /m1 은 꼴 4(가을체 12pt 굵게) · 흰색(<c>0x10018018</c>). 12pt = 16 픽셀.</summary>
    private const float FontSize = 16;

    /// <summary>한 줄 높이 — GDI 12pt 글의 높이 [H]. 줄 내림은 높이 + 4(<c>0x100a85d4</c>).</summary>
    private const int LineH = 16;

    /// <summary>/m0 이름 빛깔 7 = COLORREF 0x96FF00 → RGB(0, 255, 150).</summary>
    private const uint NameColor = 0xFF00FF96;

    private const int ScreenW = 640, ScreenH = 480, GrowTicks = 8, ShrinkTicks = 4;

    private int _serial = -1;
    private bool _narration;
    private int _ax, _ay, _x, _y, _w, _h, _ticks, _face;
    private List<(string Text, uint Color)> _lines = [];
    private readonly List<int> _pageStarts = [];
    private int _page;

    /// <summary>지금 쪽을 찍기 시작한 때 · 다 찍은 때(쪽 끝 기다림, 0 이면 아직 찍는 중) · 닫기 시작한 때(0 이면 안 닫음).</summary>
    private double _typeAt, _doneAt, _closeAt, _openAt;

    internal bool IsOpen(int serial) => _serial == serial;

    /// <summary>
    /// 새 말풍선 — 209 는 인물 발밑 (ax, ay)(화면 640×480 자리)에서, 210 은 (320, 15) 에서 (10, 5) 619×35 띠로.
    /// 크기는 글로 정한다(<c>0x100a7feb</c>~): 폭 = 가장 긴 줄 + 16(≤ 200), 높이 = ⌈긴 줄 / (폭 − 16)⌉ × (글 높이 + 4) + 16(≤ 100).
    /// </summary>
    internal void Open(int serial, bool narration, string speaker, string text, int face, int ticks, int ax, int ay)
    {
        _serial = serial;
        _narration = narration;
        _ticks = ticks;
        _face = narration ? 0 : face;
        string full = speaker.Length > 0 ? $"[{speaker}]\n{text}" : text;
        var rows = full.Split('\n');
        int nameRows = speaker.Length > 0 ? 1 : 0;
        if (narration) (_ax, _ay, _x, _y, _w, _h) = (320, 15, 10, 5, 619, 35);
        else
        {
            (_ax, _ay) = (ax, ay);
            int maxW = rows.Select(r => Width(r)).DefaultIfEmpty(0).Max();
            int totalH = rows.Length * LineH + 4 * (rows.Length - 1);
            _w = Math.Min(200, maxW + 16);
            int wraps = _w > 16 ? (_w + maxW - 17) / (_w - 16) : 1;
            _h = Math.Min(100, wraps * (totalH + 4) + 16);
            // 화면 안으로(0x100a822d~) — 오른쪽 끝, 얼굴이 왼쪽 밖으로 안 나가게, 아래 끝, 위.
            _x = ax;
            _y = ay - _h - 50;
            var f = FaceFrame();
            if (_x + _w >= ScreenW) _x = ScreenW - _w;
            if (f != null) { if (_x - f.W + 20 < 0) _x = f.W - 20; }
            else if (_x < 0) _x = 0;
            if (_y + _h >= ScreenH) _y = ScreenH - _h;
            if (f != null && _h <= f.H) { if (_y + _h - f.H < 0) _y = f.H - _h; }
            else if (_y < 0) _y = 0;
        }
        // 줄 나누기(0x100a8440) — 앞 빈칸은 건너뛰고, 「/n」 이나 폭 − 16 을 넘는 글자에서 끊는다(낱말이 아니라 글자에서).
        _lines = [];
        for (int r = 0; r < rows.Length; r++)
        {
            uint color = r < nameRows ? NameColor : GameWindow.White;
            string rest = rows[r];
            do
            {
                rest = rest.TrimStart(' ');
                int cut = rest.Length;
                if (Width(rest) > _w - 16)
                {
                    int lo = 1, hi = rest.Length - 1;   // 들어가는 가장 긴 앞자리 — 반씩 좁힌다(글 재기가 비싸다)
                    while (lo < hi) { int mid = (lo + hi + 1) / 2; if (Width(rest[..mid]) <= _w - 16) lo = mid; else hi = mid - 1; }
                    cut = lo;
                }
                _lines.Add((rest[..cut], color));
                rest = rest[cut..];
            } while (rest.Length > 0);
        }
        // 쪽 — 첫 줄 (8, 8), 줄마다 높이 + 4 아래로, 줄 밑이 창 밑 − 8 을 넘으면 다음 쪽(다섯 줄까지).
        _pageStarts.Clear();
        int perPage = Math.Clamp((_h - 8 - 8 - LineH) / (LineH + 4) + 1, 1, 5);
        for (int i = 0; i < _lines.Count; i += perPage) _pageStarts.Add(i);
        if (_pageStarts.Count == 0) _pageStarts.Add(0);
        _page = 0;
        _openAt = host._lastTime;
        _typeAt = _openAt + GrowTicks / GameWindow.TicksPerSecond;
        _doneAt = _closeAt = 0;
    }

    private int Width(string s) => s.Length == 0 ? 0 : host.UiGetText(s, GameWindow.White, FontSize).W;

    private SpriteFrame? FaceFrame() => _face > 0 ? art.Frame(_face, 0, 0) : null;

    private IEnumerable<(string Text, uint Color)> PageLines()
    {
        int start = _pageStarts[_page], end = _page + 1 < _pageStarts.Count ? _pageStarts[_page + 1] : _lines.Count;
        return _lines.Skip(start).Take(end - start);
    }

    private int PageChars() => PageLines().Sum(l => l.Text.Length);

    private double Ticks(double since) => (host._lastTime - since) * GameWindow.TicksPerSecond;

    /// <summary>틀마다 — 쪽을 다 찍었는지 보고, 정한 틱이 지나면 다음 쪽 · 닫기(0x100a8685). 다 닫혔으면 true(다음 행동으로).</summary>
    internal bool Update()
    {
        if (_serial < 0) return false;
        if (_closeAt > 0) return Ticks(_closeAt) >= ShrinkTicks;
        if (_doneAt == 0 && host._lastTime >= _typeAt && Ticks(_typeAt) >= PageChars()) _doneAt = host._lastTime;
        if (_doneAt > 0 && _ticks > 0 && Ticks(_doneAt) > _ticks) Next();
        return false;
    }

    /// <summary>누름 — 찍는 중이면 쪽을 다 보이고, 다 찍었으면 다음 쪽 · 닫기 [H: 원본 키 처리는 안 봤다].</summary>
    internal void Click()
    {
        if (_serial < 0 || _closeAt > 0) return;
        if (_doneAt == 0) { _typeAt = host._lastTime - (PageChars() + 1) / GameWindow.TicksPerSecond; _doneAt = host._lastTime; return; }
        Next();
    }

    private void Next()
    {
        if (_page + 1 < _pageStarts.Count)
        {
            _page++;
            _typeAt = host._lastTime;
            _doneAt = 0;
        }
        else _closeAt = host._lastTime;
    }

    internal void Close() => _serial = -1;

    internal void Draw(int ox, int oy)
    {
        if (_serial < 0) return;
        art.Clip = (ox, oy, ScreenW, ScreenH);
        var face = FaceFrame();
        // 펴짐 k/8(나옴) · (4 − k)/4(사라짐) — 인물 자리 (ax, ay) 에서 창 자리로 곧게(0x100a8bcd~).
        double open = _closeAt > 0 ? Math.Max(0, 1 - Ticks(_closeAt) / ShrinkTicks) : Math.Min(1, Ticks(_openAt) / GrowTicks);
        bool full = open >= 1 && _closeAt == 0;
        int Lerp(int from, int to) => from + (int)((to - from) * open);
        if (!full)
        {
            if (open <= 0) { art.Clip = default; return; }
            if (face != null)
            {
                int fw = (int)(face.W * open), fh = (int)(face.H * open);
                if (fw > 0 && fh > 0) host.BlitScaled(face, ox + Lerp(_ax, _x - face.W + 20), oy + Lerp(_ay, _y + _h - face.H), fw, fh);
            }
            DrawFrame(ox + Lerp(_ax, _x), oy + Lerp(_ay, _y), (int)(_w * open), (int)(_h * open));
            art.Clip = default;
            return;
        }
        if (face != null) art.Blit(face, ox + _x - face.W + 20 - face.X, oy + _y + _h - face.H - face.Y);
        DrawFrame(ox + _x, oy + _y, _w, _h);
        // 글 — (창 + 8, 창 + 8) 부터, 글자는 틱마다 하나(/v 기본 6). 검은 테두리(위아래 왼오른 1픽셀) 위에 빛깔(0x100194b0).
        int shown = _doneAt > 0 ? int.MaxValue : Math.Max(0, (int)Ticks(_typeAt));
        int ty = oy + _y + 8;
        foreach (var (text, color) in PageLines())
        {
            if (shown <= 0) break;
            string part = text.Length <= shown ? text : text[..shown];
            shown -= text.Length;
            Outlined(part, ox + _x + 8, ty, color);
            ty += LineH + 4;
        }
        // ▼ — 창 (w − 10, h − 2) 의 Obs 111 모션 0(0x100a81f4), 쪽을 다 찍으면 뜬다(상태 3, 0x100a8a54).
        if (_doneAt > 0) art.Blit(art.Motion(FrameObs, 0, (int)Ticks(_doneAt)), ox + _x + _w - 10, oy + _y + _h - 2);
        art.Clip = default;
    }

    /// <summary>창 바탕(0x100a18b0) — 돌 무늬를 깔고, 흰 테두리(모서리 5 픽셀 비움)와 모서리 넷(Obs 111 벌 2)을 불투명으로.</summary>
    private void DrawFrame(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        art.Tile(art.Frame(FrameObs, 0, 0), x, y, w, h, FrameAlpha);
        if (w < 10 || h < 10) return;
        const uint Line = 0xFFFFFFFF;
        art.Fill(x + 5, y, w - 10, 1, Line);
        art.Fill(x + 5, y + h - 1, w - 10, 1, Line);
        art.Fill(x, y + 5, 1, h - 10, Line);
        art.Fill(x + w - 1, y + 5, 1, h - 10, Line);
        art.Blit(art.Frame(FrameObs, 2, 0), x, y);
        art.Blit(art.Frame(FrameObs, 2, 1), x + w - 1, y);
        art.Blit(art.Frame(FrameObs, 2, 2), x, y + h - 1);
        art.Blit(art.Frame(FrameObs, 2, 3), x + w - 1, y + h - 1);
    }

    private void Outlined(string text, int x, int y, uint color)
    {
        if (text.Length == 0) return;
        const uint Black = 0xFF000000;
        host.UiText(text, x + 1, y, Black, FontSize);
        host.UiText(text, x - 1, y, Black, FontSize);
        host.UiText(text, x, y + 1, Black, FontSize);
        host.UiText(text, x, y - 1, Black, FontSize);
        host.UiText(text, x, y, color, FontSize);
    }
}
