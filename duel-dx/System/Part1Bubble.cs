namespace DuelDx;

/// <summary>
/// 파트1 필드 말풍선(209) · 해설 띠(210) · 전투 해설 상자(202, <see cref="OpenFixed"/>) · 전투 대사(200, <see cref="OpenBattle"/>) — 원본 창 <c>0x100a7ef0</c>(vtable <c>0x1010a6e4</c>, 그리기 <c>0x100a8ba0</c>, 틱 <c>0x100a8670</c>)를 옮긴 것.
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

    /// <summary>이름 칸 꼴 — 꼴 표 3(굴림 12pt 굵게). 몸글은 <see cref="Part1Text"/> 꾸밈대로(꾸밈 없으면 꼴 표 0 굴림 9pt).</summary>
    private static readonly Part1TextStyle NameStyle = Part1Text.Fixed(Part1Text.NameTable, Part1Text.NameColor);

    private const int ScreenW = 640, ScreenH = 480, GrowTicks = 8, ShrinkTicks = 4;

    private readonly Part1TextPainter _paint = new(host);

    private int Width(string s, Part1TextStyle st) => _paint.Width(s, st);

    private int _serial = -1;
    private bool _narration;
    private int _ax, _ay, _x, _y, _w, _h, _ticks, _face;

    /// <summary>틀 무늬의 Obs 111 벌 — 0(모션 1, 209 · 210 · 전투 200 편 4·3) · 4(모션 8, 전투 200 적) · 5(모션 9, 전투 202 · 판에 없는 이의 200).</summary>
    private int _frameSet;

    /// <summary>창의 이름 칸(<c>+0x80</c>) — 전투 200 만 쓴다(필드 209 는 글 안의 /m0[이름]). 빛깔은 칸 <c>+0x8a</c>(빛깔 표 차례를 COLORREF 로).</summary>
    private string _name = "";
    private uint _nameColor = Part1Text.Colors[Part1Text.NameColor];

    /// <summary>몸글 첫 줄의 창 안 y — 이름이 없으면 8, 있으면 8 + 이름 높이 + 8(<c>0x100a846a</c>~<c>0x100a848c</c>).</summary>
    private int _textTop = 8;

    /// <summary>꾸밈째 글(<c>+0x110</c>) · 지금 쪽 첫 자리(<c>+0x114</c>) · 쪽의 줄 · 찍는 걸음(줄, 자리, 틱) · 빠르기(<c>/v</c>, 쪽이 바뀌어도 남는다).</summary>
    private string _text = "";
    private int _pos, _speed = 6;
    private List<Part1Text.Line> _lines = [];
    private List<(int Line, int Pos, int Tick)> _steps = [];

    /// <summary>지금 쪽을 찍기 시작한 때 · 다 찍은 때(쪽 끝 기다림, 0 이면 아직 찍는 중) · 닫기 시작한 때(0 이면 안 닫음).</summary>
    private double _typeAt, _doneAt, _closeAt, _openAt;

    internal bool IsOpen(int serial) => _serial == serial;

    /// <summary>
    /// 새 말풍선 — 209 는 인물 발밑 (ax, ay)(화면 640×480 자리)에서, 210 은 (320, 15) 에서 (10, 5) 619×35 띠로.
    /// <paramref name="text"/> 는 꾸밈째 글(「/m0[이름]/m1/n…」 — 이름도 글 안에 있다).
    /// 크기는 글로 정한다(<c>0x100a7feb</c>~, 재기 <c>0x100187d0</c>): 폭 = 가장 긴 줄 + 16(≤ 200), 높이 = ⌈긴 줄 / (폭 − 16)⌉ × (글 높이 + 4) + 16(≤ 100).
    /// </summary>
    internal void Open(int serial, bool narration, string text, int face, int ticks, int ax, int ay)
    {
        _serial = serial;
        _narration = narration;
        _ticks = ticks;
        _face = narration ? 0 : face;
        _frameSet = 0;
        (_name, _textTop) = ("", 8);
        if (narration) (_ax, _ay, _x, _y, _w, _h) = (320, 15, 10, 5, 619, 35);
        else
        {
            (_ax, _ay) = (ax, ay);
            var (maxW, totalH) = Part1Text.Measure(text, 0, text.Length, Width);
            _w = Math.Min(200, maxW + 16);
            _h = Math.Min(100, Wraps(maxW) * (totalH + 4) + 16);
            // 화면 안으로(0x100a822d~) — 오른쪽 끝, 얼굴이 왼쪽 밖으로 안 나가게, 아래 끝, 위.
            _x = ax;
            _y = ay - _h - 50;
            KeepOnScreen();
        }
        Start(text);
    }

    /// <summary>⌈긴 줄 / (폭 − 16)⌉ — <c>0x100a8062</c>: (폭 + 긴 줄 − 17) / (폭 − 16).</summary>
    private int Wraps(int maxW) => _w > 16 ? (_w + maxW - 17) / (_w - 16) : 1;

    /// <summary>화면 안으로(<c>0x100a822d</c>~) — 오른쪽 끝, 얼굴이 왼쪽 밖으로 안 나가게, 아래 끝, 위.</summary>
    private void KeepOnScreen()
    {
        var f = FaceFrame();
        if (_x + _w >= ScreenW) _x = ScreenW - _w;
        if (f != null) { if (_x - f.W + 20 < 0) _x = f.W - 20; }
        else if (_x < 0) _x = 0;
        if (_y + _h >= ScreenH) _y = ScreenH - _h;
        if (f != null && _h <= f.H) { if (_y + _h - f.H < 0) _y = f.H - _h; }
        else if (_y < 0) _y = 0;
    }

    /// <summary>
    /// 파트1 전투 대사 200(<c>0x1001e200</c>) — <c>0x100a7ef0(0, 150, 닻 x, 닻 y, 창 x, 창 y, −1, −1, 짧은 이름, 글, 반신 초상, 판, 0x6f, 틀 모션)</c>.
    /// 크기는 글로(<c>0x100a7ffa</c>~): 폭 = max(긴 줄 + 16, 이름 폭 + 16) ≤ 200, 높이 = ⌈긴 줄 / (폭 − 16)⌉ × (글 높이 + 4) + 16 + (이름 높이 + 8) ≤ 100.
    /// 창 x 가 −1 이면 창 왼위 = (닻 x, 닻 y − 높이 − 50). 이름은 (창 + 8, 창 + 8)(<c>0x100a82e6</c>~), 몸글은 그 아래 이름 높이 + 8 부터.
    /// 이름은 창이 다 펴진 뒤(<c>+0x69 == 0xff</c>) 통째로 찍는다(<c>0x100a8e96</c> → <c>0x10018c30</c>) — 한 글자씩이 아니다.
    /// 이름 칸은 꾸밈을 안 푸는 꼴 표 3(굴림 12pt 굵게)이다(<c>+0x8c</c> = 3).
    /// </summary>
    internal void OpenBattle(int serial, string name, uint nameColor, string text, int face, int ticks,
                             int ax, int ay, int x, int y, int frameSet)
    {
        _serial = serial;
        _narration = false;
        _ticks = ticks;
        _face = face;
        _frameSet = frameSet;
        (_name, _nameColor) = (name, nameColor);
        (_ax, _ay) = (ax, ay);
        var (maxW, totalH) = Part1Text.Measure(text, 0, text.Length, Width);
        int nameH = NameStyle.Pixels;
        _w = maxW + 16;
        if (name.Length > 0) _w = Math.Max(_w, Width(name, NameStyle) + 16);
        _w = Math.Min(200, _w);
        _h = Wraps(maxW) * (totalH + 4) + 16;
        if (name.Length > 0) _h += nameH + 8;
        _h = Math.Min(100, _h);
        _textTop = name.Length > 0 ? 8 + nameH + 8 : 8;
        if (x == -1) (_x, _y) = (ax, ay - _h - 50);
        else (_x, _y) = (x, y);
        KeepOnScreen();
        Start(text);
    }

    /// <summary>
    /// 자리 · 크기를 정해 연다 — 파트1 전투 해설 202(<c>0x1001e9b0</c>): <c>0x100a7ef0(1, 150, x + 폭/2, y + 높이/2, x, y, 폭, 높이, 0, 글, 0, 판, 0x6f, 9)</c>.
    /// 이름 · 얼굴 없음, 닻은 창 가운데라 가운데에서 커지며 나온다. 틀은 Obs 111 모션 9 = 벌 5(푸른 돌) — <paramref name="frameSet"/> 에 벌 번호.
    /// 화면 안으로 미는 것(<c>0x100a822d</c>~)은 정한 자리에도 걸린다 — 자료의 자리는 모두 화면 안이라 아무 일 없다.
    /// </summary>
    internal void OpenFixed(int serial, string text, int ticks, int x, int y, int w, int h, int frameSet)
    {
        _serial = serial;
        _narration = true;
        _ticks = ticks;
        _face = 0;
        _frameSet = frameSet;
        (_name, _textTop) = ("", 8);
        (_x, _y, _w, _h) = (x, y, w, h);
        (_ax, _ay) = (x + w / 2, y + h / 2);
        if (_x + _w >= ScreenW) _x = ScreenW - _w;
        if (_x < 0) _x = 0;
        if (_y + _h >= ScreenH) _y = ScreenH - _h;
        if (_y < 0) _y = 0;
        Start(text);
    }

    private void Start(string text)
    {
        _text = text;
        _pos = 0;
        _speed = 6;   // 창을 지을 때 /v6(0x100a8382)
        _openAt = host._lastTime;
        Layout(GrowTicks / GameWindow.TicksPerSecond);
    }

    /// <summary>
    /// 지금 자리(<see cref="_pos"/>)부터 한 쪽 — 줄 나누기 <c>0x100a8440</c>(폭 − 16 · 창 밑 − 8 · 다섯 줄 · /n · /p)와 찍는 걸음(틱 <c>0x100a873e</c>).
    /// </summary>
    private void Layout(double delay)
    {
        _lines = Part1Text.Page(_text, _pos, _w - 16, _textTop, _h - 8, Width);
        _steps = Part1Text.Steps(_text, _lines, ref _speed);
        _typeAt = host._lastTime + delay;
        _doneAt = _closeAt = 0;
    }

    private SpriteFrame? FaceFrame() => _face > 0 ? art.Frame(_face, 0, 0) : null;

    /// <summary>쪽을 다 찍는 틱 — 마지막 걸음의 틱.</summary>
    private int PageTicks() => _steps.Count == 0 ? 0 : _steps[^1].Tick;

    private double Ticks(double since) => (host._lastTime - since) * GameWindow.TicksPerSecond;

    /// <summary>틀마다 — 쪽을 다 찍었는지 보고, 정한 틱이 지나면 다음 쪽 · 닫기(0x100a8685). 다 닫혔으면 true(다음 행동으로).</summary>
    internal bool Update()
    {
        if (_serial < 0) return false;
        if (_closeAt > 0) return Ticks(_closeAt) >= ShrinkTicks;
        if (_doneAt == 0 && host._lastTime >= _typeAt && Ticks(_typeAt) >= PageTicks()) _doneAt = host._lastTime;
        if (_doneAt > 0 && _ticks > 0 && Ticks(_doneAt) > _ticks) Next();
        return false;
    }

    /// <summary>누름 — 찍는 중이면 쪽을 다 보이고, 다 찍었으면 다음 쪽 · 닫기 [H: 원본 키 처리는 안 봤다].</summary>
    internal void Click()
    {
        if (_serial < 0 || _closeAt > 0) return;
        if (_doneAt == 0) { _typeAt = host._lastTime - (PageTicks() + 1) / GameWindow.TicksPerSecond; _doneAt = host._lastTime; return; }
        Next();
    }

    /// <summary>다음 쪽은 마지막 걸음 뒤 자리부터(<c>+0x114</c>), 글이 끝났으면 닫기(<c>0x100a86a2</c>).</summary>
    private void Next()
    {
        int next = _steps.Count > 0 ? _steps[^1].Pos : Part1Text.After(_lines);
        if (_lines.Count > 0 && next > _pos && next < _text.Length)
        {
            _pos = next;
            Layout(0);
            if (_lines.Count > 0) return;
        }
        _closeAt = host._lastTime;
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
        // 글 — (창 + 8, 줄 y) 부터. 줄마다 [첫 자리, 찍은 자리) 를 꾸밈대로(0x100a8ec9 → 0x10018c30), 찍는 줄까지만.
        if (_name.Length > 0) _paint.Outlined(_name, ox + _x + 8, oy + _y + 8, NameStyle, _nameColor);
        int tick = (int)Math.Floor(Ticks(_typeAt));
        int line = -1, typed = 0;
        foreach (var st in _steps)
        {
            if (_doneAt == 0 && st.Tick > tick) break;
            (line, typed) = (st.Line, st.Pos);
        }
        for (int i = 0; i <= line && i < _lines.Count; i++)
        {
            var l = _lines[i];
            int end = i < line ? l.End : Math.Min(l.End, typed);
            if (end > l.Start) _paint.DrawLine(_text, l, end, ox + _x + 8, oy + _y);
        }
        // ▼ — 창 (w − 10, h − 2) 의 Obs 111 모션 0(0x100a81f4), 쪽을 다 찍으면 뜬다(상태 3, 0x100a8a54).
        if (_doneAt > 0) art.Blit(art.Motion(FrameObs, 0, (int)Ticks(_doneAt)), ox + _x + _w - 10, oy + _y + _h - 2);
        art.Clip = default;
    }

    /// <summary>창 바탕(0x100a18b0) — 돌 무늬를 깔고, 흰 테두리(모서리 5 픽셀 비움)와 모서리 넷(Obs 111 벌 2)을 불투명으로.</summary>
    private void DrawFrame(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        art.Tile(art.Frame(FrameObs, _frameSet, 0) ?? art.Frame(FrameObs, 0, 0), x, y, w, h, FrameAlpha);
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
}
