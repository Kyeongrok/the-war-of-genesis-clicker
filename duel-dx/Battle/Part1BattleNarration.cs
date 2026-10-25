namespace DuelDx;

using System.Text.RegularExpressions;
using WarOfGenesis.Assets;

/// <summary>
/// 파트1 전투 해설 상자 — 행동 202 [x, y, 줄](<c>0x1001e9b0</c>) · 203 [폭, 높이](<c>0x1001eb10</c>).
/// 필드 209 · 210 과 같은 창 <c>0x100a7ef0</c>(vtable <c>0x1010a6e4</c>)를 정한 자리 · 크기로 띄운다(<see cref="Part1Bubble.OpenFixed"/>).
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 35절 · 「분석-파트1-챕터」 11.5 · 17.4절.
/// <list type="bullet">
/// <item>203: 크기 낱말 <c>0x1018ac6a</c> = a0(폭) · <c>0x1018ac68</c> = a1(높이). 판을 열 때(<c>0x10021ae0</c>) 200 × 100 으로 둔다.</item>
/// <item>202: 칸 단계 0 이면 <c>0x10031ac0</c>(판 커서를 그림으로 바꿈 [H]) 뒤 <c>0x100a7ef0(1, 150, x + 폭/2, y + 높이/2, x, y, 폭, 높이, 0, 글, 0, 판, 0x6f, 9)</c> —
/// 비례 1(640×480 이면 그대로) · 틱 150 · 닻 = 창 가운데 · 이름 0 · 얼굴 0 · 틀 Obs 111 모션 9(벌 5 푸른 돌). 단계 1 이면 창이 사라졌을 때
/// (<c>+0x40 == 0 · +0x69 == 0xff</c>) <c>0x10031b90</c>(커서 되돌림) · 창 지우기 · 칸 끝(<c>0x100220a0</c>).</item>
/// <item>기다림: 창은 쪽을 다 찍은 뒤 150틱이 지나면 다음 쪽 · 닫기(창 <c>0x100a8685</c>). 202 는 칸에 걸어 둘 뿐이라 줄을 붙드는 것은 뒤따르는 행동 1 이다(실행기 <c>0x100229b0</c>) —
/// 0053 · 0169 · 0394 는 202 뒤 503 · 702 가 상자가 뜬 채 돌고, 0138 은 증원이 들어오는 동안 상자가 떠 있다.</item>
/// <item>누름: 창의 누름 처리는 안 봤다 [H: 필드 말풍선처럼 찍는 중이면 쪽을 다 보이고, 다 찍었으면 넘김]. 건너뛰기(Esc)면 곧바로 닫는다(리메이크 갈음).</item>
/// <item>글 꾸밈(<c>0x10018c94</c> 표): /cN 빛깔 · /fN 꼴 이름 · /sN 크기 · /wN 굵기 · /tN 꼴 표 차례 · /m0 · /m1. 리메이크는 /c 만 줄 단위로 따르고
/// (줄 첫 글자 때의 빛깔 [H]) 나머지 꼴 바꾸기는 지운다 — 0415 · 0420 의 「/s3」 · 0195 의 「/t3」 은 크기 · 꼴이 원본과 다르다 [H].</item>
/// </list>
/// </remarks>
internal sealed class Part1BattleNarration(GameWindow host)
{
    /// <summary>창이 쪽을 다 찍은 뒤 넘기기까지의 틱(<c>0x1001ea7a push 0x96</c>).</summary>
    private const int Ticks = 150;

    /// <summary>틀 Obs 111 모션 9(<c>0x1001ea22 push 9</c>) = 벌 5 장 0(푸른 돌 100×50).</summary>
    private const int FrameSet = 5;

    /// <summary>판을 열 때의 크기(<c>0x10021b0e</c> · <c>0x10021b17</c>).</summary>
    private const int DefaultW = 200, DefaultH = 100;

    /// <summary>빛깔 표 <c>0x10018730</c>(COLORREF) — 0 흰 · 1 빨 · 2 초록 · 3 파랑 · 4 노랑 · 5 자홍 · 6 · 7 · 8 · 9.</summary>
    internal static readonly uint[] Colors =
        [0xFFFFFFFF, 0xFFFF0000, 0xFF00FF00, 0xFF0000FF, 0xFFFFFF00, 0xFFFF00FF, 0xFFF9D9A1, 0xFF00FF96, 0xFFFFDC00, 0xFFB400FF];

    private int _w = DefaultW, _h = DefaultH, _serial;
    private bool _open;
    private Part1Bubble? _bubble;
    private Part1Battle? _dlg;

    private Part1Bubble Bubble => _bubble ??= new(host, new Part1MapArt(host));

    /// <summary>203 이 정한 크기 — 판에 없는 이의 200 대사도 이것으로 자리를 잡는다(<c>0x1001e51c</c>~: 창 = (화면 폭/2 − 폭, 화면 높이/2 − 높이)).</summary>
    internal (int W, int H) Size => (_w, _h);

    /// <summary>상자가 떠 있나 — 행동 1 이 이것을 기다린다.</summary>
    internal bool IsOpen => _open;

    /// <summary>판을 열 때 — 크기를 200 × 100 으로, 떠 있던 상자는 닫는다.</summary>
    internal void Reset()
    {
        (_w, _h) = (DefaultW, DefaultH);
        Close();
    }

    /// <summary>203 [폭, 높이].</summary>
    internal void SetSize(int w, int h) => (_w, _h) = (w, h);

    /// <summary>202 [x, y, 줄] — 그 전투 .dlg 의 줄을 (x, y) 에 지금 크기로. 새 202 는 떠 있던 상자를 갈음한다 [H: 원본은 칸마다 창이라 겹칠 수 있다 — 자료엔 겹치는 곳이 없다].</summary>
    internal void Show(int x, int y, int line)
    {
        if (host.Tlk._talkSkip) return;
        int id = host._scene.Id - Part1Game.BattleBase;
        if (_dlg?.Id != id) _dlg = Part1Battle.Load(id);
        var rows = Rows(_dlg?.Line(line) ?? "");
        if (rows.Count == 0) return;
        Bubble.OpenFixed(++_serial, rows, Ticks, x, y, _w, _h, FrameSet);
        _open = true;
    }

    /// <summary>틀마다 — 창이 다 사라지면 닫는다. 건너뛰는 중이면 곧바로.</summary>
    internal void Update()
    {
        if (!_open) return;
        if (host.Tlk._talkSkip || Bubble.Update()) Close();
    }

    /// <summary>누름 — 떠 있으면 상자가 받는다.</summary>
    internal bool Click()
    {
        if (!_open) return false;
        Bubble.Click();
        return true;
    }

    internal void Close()
    {
        _open = false;
        _bubble?.Close();
    }

    /// <summary>화면 자리 640×480 — 판 가운데의 원본 화면 네모(모세스 · 레벨업 고르기와 같은 원점).</summary>
    internal void Draw()
    {
        if (!_open) return;
        var (ox, oy) = host.Mos.MosesOrigin();
        Bubble.Draw(ox, oy);
    }

    /// <summary>
    /// 파트1 글을 줄 · 빛깔로 — 「/n」 은 줄 바꿈(맨 앞의 「/n」 도 빈 줄로 남긴다 — 0138 「/n/s2…」), /cN 은 그 뒤 빛깔, 다른 꾸밈은 지운다.
    /// 끝의 빈 줄만 버린다.
    /// </summary>
    internal static List<(string Text, uint Color)> Rows(string raw)
    {
        var rows = new List<(string, uint)>();
        uint color = Colors[0];
        foreach (string part in Regex.Replace(raw, @"/m0\[[^\]]*\]/m1", "").Split("/n"))
        {
            uint rowColor = color;
            bool seenText = false;
            var text = new System.Text.StringBuilder();
            foreach (Match m in Regex.Matches(part, @"/([cC])(\d)|/[fFsStTwW]\d?|/[mM][01]|/[dDpP]\d*|[^/]+|/"))
            {
                if (m.Groups[1].Success)
                {
                    color = Colors[Math.Min(Colors.Length - 1, m.Groups[2].Value[0] - '0')];
                    if (!seenText) rowColor = color;
                    continue;
                }
                if (m.Value.StartsWith('/') && m.Value.Length > 1) continue;
                text.Append(m.Value);
                if (m.Value.Trim().Length > 0) seenText = true;
            }
            rows.Add((text.ToString(), rowColor));
        }
        while (rows.Count > 0 && rows[^1].Item1.Trim().Length == 0) rows.RemoveAt(rows.Count - 1);
        return rows;
    }
}
