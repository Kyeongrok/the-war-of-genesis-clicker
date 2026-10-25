namespace DuelDx;

using WarOfGenesis.Assets;

/// <summary>
/// 파트1 글 꾸밈 하나의 꼴 상태 — 원본 글 그리기 <c>0x10018c30</c> 의 지역 칸들(꼴 표 차례 <c>bl</c> · <c>/f</c> · <c>/s</c> · <c>/w</c> · 빛깔 차례).
/// </summary>
/// <param name="Table">꼴 표 <c>0x1011bda8</c> 차례(0~6). <see cref="Part1Text.Custom"/>(0xff) 이면 <c>/f</c> · <c>/s</c> · <c>/w</c> 로 지은 꼴.</param>
/// <param name="Face">지은 꼴의 이름 차례(<c>/f</c>, 0 굴림 · 1 궁서 · 2 가을체 · 3 바탕).</param>
/// <param name="Size">지은 꼴의 크기 차례(<c>/s</c>, 0~9 → 9 · 10 · 11 · 12 · 16 · 20 · 24 · 28 · 36 · 46 pt).</param>
/// <param name="Weight">지은 꼴의 굵기(<c>/w</c>, 0 이 아니면 굵게 700).</param>
/// <param name="Color">빛깔 차례(<c>/c</c>, 0~9).</param>
internal readonly record struct Part1TextStyle(int Table, int Face, int Size, int Weight, int Color)
{
    internal bool IsCustom => Table == Part1Text.Custom;

    internal string FaceName => IsCustom ? Part1Text.Faces[Math.Clamp(Face, 0, 3)] : Part1Text.TableFonts[Math.Clamp(Table, 0, 6)].Face;

    internal int Points => IsCustom ? Part1Text.Sizes[Math.Clamp(Size, 0, 9)] : Part1Text.TableFonts[Math.Clamp(Table, 0, 6)].Points;

    /// <summary>글자 높이(픽셀) — 포인트 꼴(<c>CreatePointFont</c>, 96 dpi)의 높이 = pt × 96 / 72.</summary>
    internal int Pixels => (Points * 96 + 36) / 72;

    internal bool Bold => IsCustom ? Weight != 0 : Part1Text.TableFonts[Math.Clamp(Table, 0, 6)].Bold;

    /// <summary>
    /// 그리는 빛깔 — 꼴 표 길(<c>0x100194b0</c>)은 빛깔 표 <c>0x10018730</c>, 지은 꼴 길(<c>0x10018210</c>)은 <c>0x10018680</c> 이 둔 낱말 <c>0x1014d380</c>.
    /// 두 표는 9 만 다르다(255,180,0 · 180,0,255).
    /// </summary>
    internal uint Argb => (IsCustom ? Part1Text.CustomColors : Part1Text.Colors)[Math.Clamp(Color, 0, 9)];
}

/// <summary>
/// 파트1 글 꾸밈 — 말풍선 · 해설 상자 · 전투 대사 · 장면 대사가 함께 쓰는 원본 글 읽기.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 35절. 원본 글 그리기 <c>0x10018c30</c>(점프표 <c>0x10018ff4</c> · <c>0x10019048</c>), 재기 <c>0x100187d0</c>,
/// 창 <c>0x100a7ef0</c> 의 줄 나누기 <c>0x100a8440</c> · 틱 <c>0x100a8670</c>.
/// <list type="bullet">
/// <item><c>/cN</c> 빛깔(그 뒤 글자부터 — 줄 가운데서도 바뀐다) · <c>/fN</c> 꼴 이름 · <c>/sN</c> 크기 · <c>/wN</c> 굵기 — 셋은 꼴 표를 떠나 새 꼴을 짓는다 ·
/// <c>/tN</c> 꼴 표 차례 · <c>/m0</c> 꼴 3 · 빛깔 7 · <c>/m1</c> 꼴 4 · 빛깔 0. 대소문자는 c f s t w 만 같고 m 은 작은 글자만.</item>
/// <item><c>/n</c> 줄 · <c>/p</c> 쪽 · <c>/dN</c> N×10 틱 쉼 · <c>/vN</c> 찍는 빠르기 — 그리기는 이 넷을 건너뛰기만 하고 창 틱이 맡는다.
/// 그 밖의 「/」 두 글자(<c>/d</c>, 모르는 글자 …)도 그리기는 세 글자를 건너뛴다.</item>
/// <item>줄은 따로 그린다 — 줄 첫 자리의 꼴은 글 처음부터 그 자리까지 꾸밈만 훑어 정한다(<see cref="Prescan"/>). 아무 꾸밈도 없으면 꼴 표 0(굴림 9) · 흰색.</item>
/// <item>크기가 다른 조각은 줄 밑에 맞춘다 — 조각 y = 줄 y + 줄 높이 − 조각 높이(<c>0x10018e3b</c>).</item>
/// </list>
/// </remarks>
internal static class Part1Text
{
    /// <summary>지은 꼴 표지(<c>bl = 0xff</c>).</summary>
    internal const int Custom = 0xff;

    /// <summary>빛깔 표 <c>0x10018730</c>(COLORREF → ARGB) — 0 흰 · 1 빨 · 2 초록 · 3 파랑 · 4 노랑 · 5 자홍 · 6 (249,217,161) · 7 (0,255,150) · 8 (255,220,0) · 9 (255,180,0).</summary>
    internal static readonly uint[] Colors =
        [0xFFFFFFFF, 0xFFFF0000, 0xFF00FF00, 0xFF0000FF, 0xFFFFFF00, 0xFFFF00FF, 0xFFF9D9A1, 0xFF00FF96, 0xFFFFDC00, 0xFFFFB400];

    /// <summary>지은 꼴 길의 빛깔 <c>0x10018680</c> — 9 만 (180,0,255).</summary>
    internal static readonly uint[] CustomColors =
        [0xFFFFFFFF, 0xFFFF0000, 0xFF00FF00, 0xFF0000FF, 0xFFFFFF00, 0xFFFF00FF, 0xFFF9D9A1, 0xFF00FF96, 0xFFFFDC00, 0xFFB400FF];

    /// <summary>꼴 표 <c>0x1011bda8</c>(7개, 0x1c 바이트 — 이름 · pt · 굵기).</summary>
    internal static readonly (string Face, int Points, bool Bold)[] TableFonts =
        [("굴림", 9, false), ("굴림", 10, false), ("굴림", 10, true), ("굴림", 12, true), ("가을체", 12, true), ("Premi", 12, true), ("굴림", 20, true)];

    /// <summary><c>/f</c> 꼴 이름(<c>0x100184d0</c> → <c>0x1011be84</c> · <c>be7c</c> · <c>be74</c> · <c>be6c</c>). 3 보다 크면 그대로 둔다.</summary>
    internal static readonly string[] Faces = ["굴림", "궁서", "가을체", "바탕"];

    /// <summary><c>/s</c> 크기(<c>0x10018590</c>, pt). 9 보다 크면 그대로 둔다.</summary>
    internal static readonly int[] Sizes = [9, 10, 11, 12, 16, 20, 24, 28, 36, 46];

    /// <summary><c>/m0</c> · <c>/m1</c> 의 꼴 · 빛깔(<c>0x10018018</c>: <c>0x10146dd8</c> = 3 · <c>dd c</c> = 7 · <c>dd0</c> = 4 · <c>dd4</c> = 0).</summary>
    internal const int NameTable = 3, NameColor = 7, BodyTable = 4, BodyColor = 0;

    /// <summary>꾸밈 없는 첫 꼴 — 꼴 표 0 · 지은 꼴 칸 0 · 흰색.</summary>
    internal static readonly Part1TextStyle Start = new(0, 0, 0, 0, 0);

    /// <summary>이름 칸 꼴(창 <c>+0x80</c>: 꼴 표 3 · 빛깔은 창이 준다) — 꾸밈을 안 풀고 이것으로 시작한다.</summary>
    internal static Part1TextStyle Fixed(int table, int color) => new(table, 0, 2, 1, color);

    private static int Digit(string s, int i) => i < s.Length ? s[i] - '0' : -'0';

    /// <summary>
    /// 줄 첫 자리 <paramref name="start"/> 의 꼴 — <c>0x10018c44</c>~ 의 훑기를 그대로: 「/」 마다 세 글자를 건너뛰고(<c>/n</c> 도 셋 —
    /// 「/n/c4…」 처럼 /n 바로 뒤의 꾸밈은 훑기에서 빠진다, 원본 그대로) c · f · s · t · w · m 만 상태를 바꾼다.
    /// </summary>
    internal static Part1TextStyle Prescan(string s, int start)
    {
        var st = Start;
        int i = 0;
        while (i < start && i < s.Length)
        {
            if (s[i] != '/' || i + 1 >= s.Length) { i++; continue; }
            int n = Digit(s, i + 2);
            st = s[i + 1] switch
            {
                'c' or 'C' => st with { Color = n },
                'f' or 'F' => st with { Table = Custom, Face = n },
                's' or 'S' => st with { Table = Custom, Size = n },
                'w' or 'W' => st with { Table = Custom, Weight = n },
                't' or 'T' => st with { Table = n },
                'm' => n == 0 ? st with { Table = NameTable, Color = NameColor } : st with { Table = BodyTable, Color = BodyColor },
                _ => st,
            };
            i += 3;
        }
        return st;
    }

    /// <summary>
    /// 그리기 · 재기의 본 고리(<c>0x10018dd8</c>~) — 「/」 를 만날 때까지를 한 조각으로, 꾸밈은 꼴을 바꾼다. <c>/n</c> 은 두 글자 · 나머지는 세 글자를 건너뛴다.
    /// 조각마다 (글, 꼴, 그 뒤가 /n 인가).
    /// </summary>
    internal static IEnumerable<(string Text, Part1TextStyle Style, bool LineBreak)> Chunks(string s, int start, int end, Part1TextStyle st)
    {
        end = Math.Min(end, s.Length);
        int i = start;
        if (i >= end) yield break;
        while (i < end)
        {
            int j = i;
            while (j < end && s[j] != '/') j++;
            bool brk = j + 1 < s.Length && j < end && s[j + 1] is 'n' or 'N';
            yield return (s[i..j], st, brk);
            if (j >= end) yield break;
            if (j + 1 < s.Length)
            {
                int n = Digit(s, j + 2);
                // 크기 · 꼴 이름 차례는 넘치면 그대로 둔다(0x100184d0 cmp al, 3 · 0x10018590 cmp eax, 9).
                st = s[j + 1] switch
                {
                    'c' or 'C' => st with { Color = n },
                    'f' or 'F' => st with { Table = Custom, Face = n is >= 0 and <= 3 ? n : st.Face },
                    's' or 'S' => st with { Table = Custom, Size = n is >= 0 and <= 9 ? n : st.Size },
                    'w' or 'W' => st with { Table = Custom, Weight = n },
                    't' or 'T' => st with { Table = n },
                    'm' => n == 0 ? st with { Table = NameTable, Color = NameColor } : st with { Table = BodyTable, Color = BodyColor },
                    _ => st,
                };
            }
            i = j + 1 < s.Length && s[j + 1] is 'n' or 'N' ? j + 2 : j + 3;
        }
    }

    /// <summary>
    /// 글 크기(<c>0x100187d0</c>) — 줄마다 조각 폭을 더하고 높이는 가장 큰 것, <c>/n</c> 이면 다음 줄(높이 + 4 아래). 폭은 가장 긴 줄.
    /// </summary>
    internal static (int W, int H) Measure(string s, int start, int end, Func<string, Part1TextStyle, int> width)
    {
        int maxW = 0, y = 0, lineW = 0, lineH = 0;
        bool any = false;
        foreach (var (text, st, brk) in Chunks(s, start, end, Prescan(s, start)))
        {
            any = true;
            lineW += text.Length == 0 ? 0 : width(text, st);
            lineH = Math.Max(lineH, st.Pixels);
            if (!brk) continue;
            maxW = Math.Max(maxW, lineW);
            y += lineH + 4;
            lineW = lineH = 0;
        }
        maxW = Math.Max(maxW, lineW);
        if (!any || lineH == 0) y -= 4; else y += lineH;
        return (maxW, Math.Max(0, y));
    }

    /// <summary>창 줄 하나 — 글 자리 [Start, End) · 끝 꾸밈(/n · /p)까지 센 길이 · 창 안 y · 높이 · /p 로 끝났나.</summary>
    internal readonly record struct Line(int Start, int End, int Length, int Y, int H, bool PageBreak);

    /// <summary>
    /// 쪽 하나를 줄로 나눈다(<c>0x100a8440</c>) — <paramref name="from"/> 부터, 줄 앞 빈칸은 건너뛰고, <c>/n</c> · <c>/p</c> 에서, 또는 폭 − 16 을 넘는 글자에서 끊는다
    /// (낱말이 아니라 글자에서). 첫 줄 y = <paramref name="top"/>, 다음 줄은 높이 + 4 아래. 둘째 줄부터는 밑이 <paramref name="bottom"/> 을 넘으면 다음 쪽. 다섯 줄까지.
    /// </summary>
    internal static List<Line> Page(string s, int from, int limit, int top, int bottom, Func<string, Part1TextStyle, int> width)
    {
        var lines = new List<Line>();
        int pos = from, y = top;
        while (lines.Count < 5 && pos < s.Length)
        {
            while (pos < s.Length && s[pos] == ' ') pos++;
            int start = pos, len = 0, end;
            bool pageBreak = false;
            while (true)
            {
                // 앞의 꾸밈은(/n · /p 말고) 세 글자씩 먹는다.
                int p = start + len;
                while (p + 1 < s.Length && s[p] == '/' && s[p + 1] is not ('n' or 'N' or 'p' or 'P')) p += 3;
                if (p >= s.Length) { len = s.Length - start; end = s.Length; break; }
                if (s[p] == '/' && p + 1 < s.Length)
                {
                    pageBreak = s[p + 1] is 'p' or 'P';
                    end = p;
                    len = p + 2 - start;
                    break;
                }
                int before = p - start;   // 끊으면 앞의 꾸밈까지는 이 줄(0x100a8538 mov ebp, ebx)
                len = p + 1 - start;
                if (before > 0 && Measure(s, start, start + len, width).W > limit && HasText(s, start, before)) { len = before; end = start + before; break; }
            }
            end = Math.Min(end, s.Length);
            int h = Math.Max(1, MeasureLine(s, start, start + len, width));
            if (lines.Count > 0 && y + h > bottom) break;
            lines.Add(new Line(start, end, len, y, h, pageBreak));
            y += h + 4;
            pos = start + len;
            if (pageBreak) break;
        }
        return lines;
    }

    /// <summary>[start, start + len) 에 꾸밈 말고 글자가 하나라도 있나 — 첫 글자부터 넘쳐도 한 글자는 싣는다(원본은 막지 않는다 [H]).</summary>
    private static bool HasText(string s, int start, int len)
    {
        for (int i = start; i < start + len && i < s.Length; i++)
        {
            if (s[i] == '/' && i + 1 < s.Length) { i += 2; continue; }
            return true;
        }
        return false;
    }

    private static int MeasureLine(string s, int start, int end, Func<string, Part1TextStyle, int> width)
    {
        int h = 0;
        foreach (var (_, st, brk) in Chunks(s, start, end, Prescan(s, start)))
        {
            h = Math.Max(h, st.Pixels);
            if (brk) break;
        }
        return h == 0 ? Prescan(s, start).Pixels : h;
    }

    /// <summary>
    /// 쪽을 찍는 틱(<c>0x100a873e</c>~) — 걸음마다(글자 하나 · 꾸밈 하나) 몇째 틱에 찍히나. 기본 빠르기 /v6(틱마다 한 걸음).
    /// /v0~5 는 32 · 16 · 8 · 4 · 3 · 2 틱에 한 걸음, /v7~9 는 틱마다 2 · 3 · 4 걸음. <c>/dN</c> 은 그 틱을 끝내고 N×10 틱 쉰 뒤 한 틱을 더 쉰다.
    /// 돌려주는 것: 걸음마다 (줄 차례, 그 걸음 뒤 글 자리, 틱), 빠르기는 <paramref name="speed"/> 에 이어 둔다(쪽이 바뀌어도 남는다).
    /// </summary>
    internal static List<(int Line, int Pos, int Tick)> Steps(string s, IReadOnlyList<Line> lines, ref int speed)
    {
        var steps = new List<(int, int, int)>();
        if (lines.Count == 0) return steps;
        int line = 0, pos = lines[0].Start, tick = 0, perTick = 1, vAt = 0;
        int delayUntil = -1;
        int guard = 0;
        while (guard++ < 200000)
        {
            tick++;
            if (delayUntil >= 0)
            {
                if (tick < delayUntil) continue;
                delayUntil = -1;
                continue;
            }
            if (perTick == 1)
            {
                int every = speed switch { 0 => 32, 1 => 16, 2 => 8, 3 => 4, 4 => 3, 5 => 2, _ => 1 };
                if (every > 1 && (tick - vAt) % every != 0) continue;
                perTick = speed switch { 7 => 2, 8 => 3, 9 => 4, _ => 1 };
            }
            for (int k = 0; k < perTick; k++)
            {
                bool pageEnd = false, stop = false;
                int lineEnd = lines[line].Start + lines[line].Length;
                if (pos < lineEnd && pos < s.Length)
                {
                    if (s[pos] == '/' && pos + 1 < s.Length)
                    {
                        char c = s[pos + 1];
                        if (c is 'n' or 'N') pos += 2;
                        else if (c is 'p' or 'P') { pos += 2; pageEnd = true; }
                        else if (c is 'v' or 'V') { speed = Math.Clamp(Digit(s, pos + 2), 0, 9); vAt = tick; pos += 3; }
                        else if (c is 'd' or 'D') { delayUntil = tick + Math.Max(0, Digit(s, pos + 2)) * 10; pos += 3; stop = true; }
                        else pos += 3;
                    }
                    else pos++;
                }
                else pos = Math.Max(pos, lineEnd);
                steps.Add((line, Math.Min(pos, s.Length), tick));
                if (stop) break;
                if (pos >= lineEnd)
                {
                    if (pageEnd || lines[line].PageBreak && pos >= lineEnd || line >= lines.Count - 1) return steps;
                    line++;
                    pos = lines[line].Start;
                }
            }
        }
        return steps;
    }

    /// <summary>쪽 끝 다음의 글 자리 — 다음 쪽이 여기서 시작한다(<c>+0x114</c>).</summary>
    internal static int After(IReadOnlyList<Line> lines) => lines.Count == 0 ? int.MaxValue : lines[^1].Start + lines[^1].Length;

    /// <summary>남은 글이 있나 — 빈칸 · 꾸밈만 남았으면 없다.</summary>
    internal static bool HasMore(string s, int pos) => pos < s.Length;
}

/// <summary>파트1 꾸밈 글을 리메이크 글꼴 그리기로 — 재기 · 테두리 글 · 줄 하나(창 <c>0x100a7ef0</c> · <c>0x100a9000</c> 이 함께 쓴다).</summary>
internal sealed class Part1TextPainter(GameWindow host)
{
    /// <summary>조각 폭 — 꾸밈의 꼴(이름 · 크기 · 굵기)로 잰다.</summary>
    internal int Width(string s, Part1TextStyle st) =>
        s.Length == 0 ? 0 : (int)Math.Ceiling(Native.TextRaster.Width(s, host.FontSize(st.Pixels), st.FaceName, st.Bold));

    /// <summary>검은 테두리(위아래 왼오른 1픽셀) 위에 빛깔 글(<c>0x100194b0</c> · <c>0x10018210</c>).</summary>
    internal void Outlined(string text, int x, int y, Part1TextStyle st, uint color)
    {
        if (text.Length == 0) return;
        const uint Black = 0xFF000000;
        float px = st.Pixels;
        string face = st.FaceName;
        bool bold = st.Bold;
        host.UiText(text, x + 1, y, Black, px, face, bold);
        host.UiText(text, x - 1, y, Black, px, face, bold);
        host.UiText(text, x, y + 1, Black, px, face, bold);
        host.UiText(text, x, y - 1, Black, px, face, bold);
        host.UiText(text, x, y, color, px, face, bold);
    }

    /// <summary>한 줄 [첫 자리, <paramref name="end"/>) — 조각마다 꼴을 바꿔 오른쪽으로, 조각 y = 창 y + 줄 y + 줄 높이 − 조각 높이(줄 밑 맞춤, <c>0x10018e3b</c>).</summary>
    internal void DrawLine(string s, Part1Text.Line l, int end, int x, int windowY)
    {
        foreach (var (text, st, _) in Part1Text.Chunks(s, l.Start, end, Part1Text.Prescan(s, l.Start)))
        {
            if (text.Length == 0) continue;
            Outlined(text, x, windowY + l.Y + l.H - st.Pixels, st, st.Argb);
            x += Width(text, st);
        }
    }

    /// <summary>글 전체의 쪽 첫 자리들 — 쪽마다 <see cref="Part1Text.Page"/>, 다음 쪽은 그 끝(/p 뒤)부터. 꾸밈 · 빈칸만 남으면 끝.</summary>
    internal List<int> PageStarts(string s, int limit, int top, int bottom)
    {
        var starts = new List<int>();
        int pos = 0;
        while (starts.Count < 64)
        {
            var lines = Part1Text.Page(s, pos, limit, top, bottom, Width);
            if (lines.Count == 0) break;
            starts.Add(pos);
            int next = Part1Text.After(lines);
            if (next <= pos || next >= s.Length || Part1Game.PlainText(s[next..]).Trim().Length == 0) break;
            pos = next;
        }
        if (starts.Count == 0) starts.Add(0);
        return starts;
    }
}

/// <summary>
/// 글 쪽 창의 찍기 — 장면 대사 창 <c>0x100a9000</c>(200~208 · 904, 틱 <c>0x100a9190</c>) · 마을 대사 창 <c>0x100a98a0</c>(틱 <c>0x100a9960</c>) ·
/// 고르기 물음 <c>0x100aa690</c>(틱 <c>0x100aa7e0</c>) · 고를 줄 <c>0x100a9ce0</c>(틱 <c>0x100a9d30</c>)이 같은 틱을 돈다. 틀마다 지나간 창 틱만큼 <see cref="Run"/>.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 35.1절. 창 칸: <c>+0x20</c> 창 틱 · <c>+0x110</c> 글 · <c>+0x114</c> 자리 · <c>+0x118</c> 찍는 줄 · <c>+0x94</c>~ 줄 칸(20B, <c>+6</c> 찍은 끝) ·
/// <c>+0x126</c> 줄 길이 · <c>+0x11a</c> 틱마다 걸음 · <c>+0x14c</c> 빠르기 · <c>+0x148</c> /v 틱 · <c>+0x140/+0x144</c> /d 쉼 · <c>+0x14d</c> 쉼 건너뛰기 · <c>+0x7c</c> 상태(2 찍는 중 · 3 쪽 끝).
/// <list type="bullet">
/// <item>쉼(<c>0x100a91c0</c>~): 건너뛰기가 아니고 쉼이 걸려 있으면, 끝나지 않았으면 이 틱은 쉬고 끝났으면 쉼을 풀고 그 틱도 쉰다.</item>
/// <item>빠르기(<c>0x100a920d</c>, 점프표 <c>0x100a9514</c>): 틱마다 걸음이 1 일 때만 본다 — 0~5 는 (틱 − /v 틱) 이 32 · 16 · 8 · 4 · 3 · 2 로 나뉘어야 한 걸음,
/// 6 은 1, <b>7~9 는 걸음 수를 2 · 3 · 4 로 바꿔 둔다</b> — 1 이 아니게 되면 다시 안 보므로 그 쪽이 끝날 때까지(또는 단추를 뗄 때까지) 이어진다(원본 그대로).</item>
/// <item>걸음: <c>/n</c> 둘 · <c>/p</c> 둘(쪽 끝 — 마을 창 틱 <c>0x100a9960</c> 은 /p 를 모르고 셋) · <c>/v</c> 셋(빠르기 · /v 틱) · <c>/d</c> 셋(쉼 = N×10 틱, 걸음이 10000 이 아니면 그 틱을 끝낸다) ·
/// 그 밖의 「/」 셋 · 글자 하나(한글 2 바이트 = 글자 하나). 줄을 다 찍으면 /p 였거나 끝 줄이면 쪽 끝(상태 3), 아니면 다음 줄(앞 빈칸 건너뜀).</item>
/// <item>누름(<c>0x100a8f90</c>): 찍는 중이면 틱마다 4 걸음 · 쉼 건너뛰기. 뗌(<c>0x100a8fc0</c>): 찍는 중이면 1 걸음 · 쉼 지킴. 쪽 끝에서 누르면 넘김(<c>0x100a97c0</c>: 소리 0x33, 글 끝이면 닫고 아니면 그 자리부터 다음 쪽 · 걸음 1).</item>
/// </list>
/// </remarks>
internal sealed class Part1TextTyper
{
    internal string Text { get; private set; } = "";
    internal List<Part1Text.Line> Lines { get; private set; } = [];

    /// <summary>글 자리(<c>+0x114</c>) — 쪽 끝에서 다음 쪽이 여기서 시작한다.</summary>
    internal int Pos { get; private set; }

    /// <summary>찍는 줄(<c>+0x118</c>)과 줄마다 찍은 끝(줄 칸 <c>+6</c>).</summary>
    internal int Line { get; private set; }
    private int[] _ends = [];

    /// <summary>쪽을 다 찍었나(상태 3).</summary>
    internal bool Done { get; private set; }

    private readonly bool _pageCode;
    private int _tick, _perTick = 1, _speed = 6, _vAt, _delayAt = -1, _delayLen = -1;
    private bool _skipDelay;
    private long _lastFrameTick = -1;

    /// <param name="pageCode">찍기가 <c>/p</c> 를 쪽 끝으로 아나 — 장면 대사 · 고르기 창은 안다, 마을 대사 창(<c>0x100a9960</c>)은 모른다.</param>
    internal Part1TextTyper(bool pageCode = true) => _pageCode = pageCode;

    /// <summary>새 글 — 첫 쪽부터, 빠르기 /v6(창을 지을 때 <c>0x100a8382</c> 와 같다고 봤다 [H]).</summary>
    internal void Start(string text, List<Part1Text.Line> firstPage)
    {
        Text = text;
        _speed = 6;
        _vAt = 0;
        _tick = 0;
        _delayAt = _delayLen = -1;
        _skipDelay = false;
        _lastFrameTick = -1;
        Page(firstPage);
    }

    /// <summary>쪽을 새로(<c>0x100a980d</c>~: 줄 0 · 상태 2 · 걸음 1 · 줄 나누기). 빠르기 · /v 틱은 남는다.</summary>
    internal void Page(List<Part1Text.Line> lines)
    {
        Lines = lines;
        Line = 0;
        _ends = [.. lines.Select(l => l.Start)];
        Pos = lines.Count > 0 ? lines[0].Start : Pos;
        _perTick = 1;
        Done = lines.Count == 0;
    }

    /// <summary>누름 · 뗌(<c>0x100a8f90</c> · <c>0x100a8fc0</c>) — 찍는 중일 때만 걸음 · 쉼을 바꾼다.</summary>
    internal void Hold(bool down)
    {
        if (Done) return;
        (_perTick, _skipDelay) = down ? (4, true) : (1, false);
    }

    /// <summary>창 틱을 지금 틀 틱(<paramref name="frameTick"/>)까지 돌린다 — 처음 부른 틀은 틱 하나.</summary>
    internal void Run(long frameTick)
    {
        long n = _lastFrameTick < 0 ? 1 : Math.Clamp(frameTick - _lastFrameTick, 0, 600);
        _lastFrameTick = frameTick;
        for (long i = 0; i < n && !Done; i++) Tick();
    }

    private int Digit(int i) => i < Text.Length ? (byte)(Text[i] - '0') : 0xd0;

    private void Tick()
    {
        _tick++;
        if (!_skipDelay && _delayAt != -1)
        {
            if (_delayAt + _delayLen > _tick) return;
            _delayAt = _delayLen = -1;
            return;
        }
        if (_perTick == 1)
        {
            switch (_speed)
            {
                case 0: if ((_tick - _vAt) % 32 != 0) return; break;
                case 1: if ((_tick - _vAt) % 16 != 0) return; break;
                case 2: if ((_tick - _vAt) % 8 != 0) return; break;
                case 3: if ((_tick - _vAt) % 4 != 0) return; break;
                case 4: if ((_tick - _vAt) % 3 != 0) return; break;
                case 5: if ((_tick - _vAt) % 2 != 0) return; break;
                case 7: _perTick = 2; break;
                case 8: _perTick = 3; break;
                case 9: _perTick = 4; break;
            }
        }
        for (int k = 0; k < _perTick; k++)
        {
            bool pageEnd = false;
            int step = 1;
            if (Pos < Text.Length && Text[Pos] == '/')
            {
                char c = Pos + 1 < Text.Length ? Text[Pos + 1] : '\0';
                if (c is 'n' or 'N') step = 2;
                else if (_pageCode && c is 'p' or 'P') { step = 2; pageEnd = true; }
                else if (c is 'v' or 'V') { step = 3; _speed = Digit(Pos + 2); _vAt = _tick; }
                else if (c is 'd' or 'D')
                {
                    _delayAt = _tick;
                    _delayLen = (Pos + 2 < Text.Length ? Text[Pos + 2] - '0' : -'0') * 10;   // movsx · sub 0x30 — 숫자가 아니면 음수(쉼이 곧 끝난다)
                    step = 3;
                    if (_perTick != 10000) { Pos += step; _ends[Line] += step; return; }
                }
                else step = 3;
            }
            Pos += step;
            _ends[Line] += step;
            var l = Lines[Line];
            if (_ends[Line] - l.Start < l.Length) continue;
            if (pageEnd || Line >= Lines.Count - 1) { Done = true; return; }
            Line++;
            while (Pos < Text.Length && Text[Pos] == ' ') Pos++;
        }
    }

    /// <summary>줄 <paramref name="i"/> 의 찍은 끝 — 그리기는 줄 0 부터 찍는 줄까지 [첫 자리, 이 끝) 을 그린다(<c>0x100a9590</c>~).</summary>
    internal int End(int i) => i < _ends.Length ? Math.Min(_ends[i], Text.Length) : 0;
}
