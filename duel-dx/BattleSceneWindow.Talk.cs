using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 전투 중 대사 — 이벤트 행동 <c>600</c>(아래 대사 상자)과 <c>601</c>(말풍선).
/// </summary>
/// <remarks>
/// 글은 <b><c>Tlk\&lt;전투번호&gt;.tlb</c></b> 에서 온다(<c>0x1004a420</c> 갈래 1) — 챕터가 쓰는 <c>.tlc</c> 도 <c>TXR</c> 도 아니다.
/// 서식은 <see cref="TalkTable"/> 이 그대로 읽는다.
/// <para>인자(18바이트 명령의 낱말 여덟 중 네 개만 쓴다):</para>
/// <list type="bullet">
/// <item><b>0 말하는 이</b> — <c>10000+N</c> 이면 Btl 배치표 N번, 1~9999 면 인물 번호, 0 이면 없음(화면 가운데).</item>
/// <item><b>2 글 번호</b> — 그 전투 <c>.tlb</c> 의 번호.</item>
/// <item><b>3 음성</b>(600 만, 데모는 아직 안 냄) · <b>4 얼굴</b> — 초상화 모션 = <c>2×값 + 11</c>.</item>
/// </list>
/// <b>600</b> 은 화면 아래 <c>(10, 370) 620×100</c> 고정 상자에 초상화와 이름을 얹고,
/// <b>601</b> 은 말하는 이 머리 위 <c>(x+30, y−200) 174×60</c> 말풍선이다(틀 <c>Obs 0111</c>, 모션 1 아군 · 8 적 · 9 화면 밖).
/// 글의 <c>$n</c> 은 줄바꿈, <c>$m0</c> 은 글꼴·색 전환이다.
/// 넘기기는 <b>클릭·아무 키</b>, 가만히 두면 <b>118틱</b> 뒤 저절로. 닫을 때 효과음 <b>95</b>.
/// 대사가 도는 동안은 <b>전투가 통째로 멈춘다</b>(<c>0x10066197</c>) — 틱도 차례도 안 흐른다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int TalkBalloonObs = 111, TalkCloseSound = 95;
    private const int TalkAutoTicks = 118;

    /// <summary>지금 떠 있는 대사 한 줄. 없으면 null.</summary>
    private (bool Box, int Speaker, string Name, string Text, int Face, double Start)? _talk;

    private TalkTable? _battleTalk;
    private int _battleTalkId = -1;

    /// <summary>그 전투의 대사 표를 읽어 둔다.</summary>
    private TalkTable? TalkTableFor(int battleId)
    {
        if (_battleTalkId == battleId) return _battleTalk;
        _battleTalkId = battleId;
        _battleTalk = null;
        try
        {
            var files = GameFiles.FromFolder(AssetsFolder.Find("data"));
            _battleTalk = TalkTable.Parse(files.Read("Tlk", $"{battleId:D4}.tlb"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { }
        return _battleTalk;
    }

    /// <summary>말하는 이를 찾는다 — 없으면 −1(화면 가운데에 띄운다).</summary>
    private int TalkSpeaker(int value)
    {
        if (value >= 20000) return -1;
        // 10000+N 은 배열 자리가 아니라 <b>Btl 레코드 번호</b>다 — 빈 칸을 걸러 낸 뒤의 자리와 다르다.
        if (value >= 10000) return Array.FindIndex(_units, u => u.LeaderIndex < 0 && u.Record == value - 10000);
        if (value <= 0) return -1;
        return Array.FindIndex(_units, u => u.ChrCode == value);
    }

    /// <summary>대사 한 줄을 띄운다(행동 600·601).</summary>
    private void ShowTalk(bool box, ScriptCommand a)
    {
        short A(int i) => i < a.Args.Length ? a.Args[i] : (short)0;
        string text = TalkTableFor(_scene.Id)?[A(2)] ?? "";
        int speaker = TalkSpeaker(A(0));
        string name = speaker >= 0 && _units[speaker].Data is { } c ? _db?.T(c.NameId) ?? "" : "";
        _talk = (box, speaker, name, text, A(4), _lastTime);
    }

    /// <summary>대사를 닫는다 — 넘기거나 저절로 넘어갈 때.</summary>
    private void CloseTalk()
    {
        if (_talk == null) return;
        _talk = null;
        Play(TalkCloseSound);
    }

    /// <summary>클릭·키로 넘기기. 대사가 떠 있었으면 true.</summary>
    private bool OnTalkInput()
    {
        if (_talk == null) return false;
        CloseTalk();
        return true;
    }

    /// <summary>가만히 두면 118틱 뒤 저절로 넘어간다.</summary>
    private void UpdateTalk()
    {
        if (_talk is { } t && (_lastTime - t.Start) * TicksPerSecond > TalkAutoTicks) CloseTalk();
    }

    /// <summary>글에 박힌 <c>$n</c>(줄바꿈)·<c>$m0</c>(색 전환)을 푼다.</summary>
    private static string[] TalkLines(string text) =>
        text.Replace("$m0", "").Split("$n", StringSplitOptions.None);

    private void DrawTalk()
    {
        if (_talk is not { } t) return;
        var (ox, oy) = (0, _camY);
        int tick = (int)((_lastTime - t.Start) * TicksPerSecond);
        var lines = TalkLines(t.Text);

        if (t.Box)
        {
            // 600 — 화면 아래 고정 상자. 원본은 640×480 기준 (10,370) 620×100 이라 판 너비에 맞춰 가운데 둔다.
            int w = Math.Min(620, BoardWidth - 20), h = 100;
            int x = ox + (BoardWidth - w) / 2, y = oy + ViewHeight - h - 10;
            DarkenRect(x - 1, y - FrameTitleH - 1, w + 2, h + FrameTitleH + 2, 8);
            DrawGameFrame(x, y, w, h, t.Name);
            // 초상화 — 인물 얼굴 그림을 왼쪽에. 원본은 Obs 모션 2×얼굴+11 이지만 데모는 뽑아 둔 얼굴 그림을 쓴다.
            int textLeft = x + 12;
            if (t.Speaker >= 0 && _units[t.Speaker].Data is { } c && _faces.TryGetValue(c.Code, out var face))
            {
                BlitScaled(face, x + 8, y + 6, 84, 84);
                textLeft = x + 100;
            }
            for (int i = 0; i < lines.Length && i < 4; i++)
                DrawText(lines[i], textLeft, y + 10 + i * 20, White, 13);
            // 오른쪽 아래 「다음」 표시 — 깜빡인다.
            if (tick % 20 < 12) DrawText("▼", x + w - 22, y + h - 22, 0xFFFFE070, 13);
            return;
        }

        // 601 — 말하는 이 머리 위 말풍선. 화면 밖이면 판 가운데.
        int bw = 174, bh = 60;
        int bx, by;
        if (t.Speaker >= 0 && _units[t.Speaker].Alive)
        {
            var (fx, fy) = UnitFoot(_units[t.Speaker]);
            bx = Math.Clamp(fx + 30, 8, BoardWidth - bw - 8);
            by = Math.Clamp(fy - 200, _camY + GridTop + 8, _camY + ViewHeight - bh - 8);
        }
        else
        {
            bx = (BoardWidth - bw) / 2;
            by = _camY + ViewHeight / 2 - bh;
        }
        // 글이 길면 창이 늘어난다 — 원본도 폭 200·높이 100 까지 늘린다.
        int widest = lines.Max(l => GetText(l, White, 12).Item2);
        bw = Math.Clamp(widest + 16, 50, 200);
        bh = Math.Clamp(lines.Length * 18 + 26, 50, 100);
        bx = Math.Clamp(bx, 8, BoardWidth - bw - 8);

        DarkenRect(bx - 1, by - FrameTitleH - 1, bw + 2, bh + FrameTitleH + 2, 8);
        DrawGameFrame(bx, by, bw, bh, t.Name);
        for (int i = 0; i < lines.Length && i * 18 + 8 < bh; i++)
            DrawText(lines[i], bx + 8, by + 6 + i * 18, White, 12);
        if (tick % 20 < 12) DrawText("▼", bx + bw - 18, by + bh - 20, 0xFFFFE070, 12);
    }
}
