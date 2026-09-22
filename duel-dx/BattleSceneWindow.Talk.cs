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

    /// <summary>글자가 흘러나오는 빠르기 — 한 틱에 몇 글자.</summary>
    private const double TalkCharsPerTick = 1.2;

    /// <summary>글을 다 채웠나(흐르는 중이면 먼저 채우고, 다 채웠으면 넘긴다).</summary>
    private bool _talkFilled;

    /// <summary>대사 상자에 그릴 초상화의 Chr 번호 — 필드처럼 말하는 이가 전투 유닛이 아닐 때 쓴다.</summary>
    private int _talkFace;

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
        // 20010·20011 은 조건 300·301 이 방금 찾아 낸 두 사람이다(0x1004eba5) — 대사 49줄이 이걸 쓴다.
        if (value == 20010) return _eventFoundA is null ? -1 : Array.IndexOf(_units, _eventFoundA);
        if (value == 20011) return _eventFoundB is null ? -1 : Array.IndexOf(_units, _eventFoundB);
        if (value >= 20000) return -1;
        // 10000+N 은 배열 자리가 아니라 <b>Btl 레코드 번호</b>다 — 빈 칸을 걸러 낸 뒤의 자리와 다르다.
        if (value >= 10000) return Array.FindIndex(_units, u => u.LeaderIndex < 0 && u.Record == value - 10000);
        if (value <= 0) return -1;
        return Array.FindIndex(_units, u => u.ChrCode == value);
    }

    /// <summary>대사 한 줄을 띄운다(행동 600·601).</summary>
    private void ShowTalk(bool box, ScriptCommand a)
    {
        if (_talkSkip) return;

        short A(int i) => i < a.Args.Length ? a.Args[i] : (short)0;
        string text = TalkTableFor(_scene.Id)?[A(2)] ?? "";
        int speaker = TalkSpeaker(A(0));
        string name = speaker >= 0 && _units[speaker].Data is { } c ? _db?.T(c.NameId) ?? "" : "";
        _talk = (box, speaker, name, text, A(4), _lastTime);
        _talkFilled = false;
    }

    /// <summary>대사를 닫는다 — 넘기거나 저절로 넘어갈 때.</summary>
    private void CloseTalk()
    {
        if (_talk == null) return;
        _talk = null;
        Play(TalkCloseSound);
    }

    /// <summary>
    /// 클릭·키로 넘기기 — <b>글이 흐르는 중이면 먼저 다 채우고</b>, 다 채운 뒤 누르면 닫는다(<c>0x1003c029</c>).
    /// </summary>
    private bool OnTalkInput(bool skipAll = false)
    {
        if (_talk == null) return false;
        if (skipAll) { SkipTalk(); return true; }
        if (!_talkFilled) { _talkFilled = true; return true; }
        CloseTalk();
        return true;
    }

    /// <summary>
    /// 이 장면에 남은 대사를 <b>통째로</b> 건너뛴다 — 대사 중 <b>Esc·우클릭</b>.
    /// </summary>
    /// <remarks>
    /// 켜 두면 <see cref="ShowTalk"/>·<see cref="ShowFieldTalk"/> 가 글을 안 띄우고 지나가므로,
    /// 이벤트·필드 스크립트가 대사 줄에서 멈추지 않고 끝까지 달린다. 그 스크립트가 끝나면 저절로 꺼진다.
    /// </remarks>
    private bool _talkSkip;

    private void SkipTalk()
    {
        _talkSkip = true;
        CloseTalk();
        Toast("장면을 건너뜁니다");
    }

    /// <summary>
    /// 돌고 있는 이벤트 장면(전투 이벤트·필드 스크립트)을 <b>통째로</b> 건너뛴다 — Esc.
    /// 대사는 안 띄우고, 기다림은 없는 셈 치고, 걷기·밝기·카메라·전환은 끝난 자리로 보낸다.
    /// 고르기(604)는 사람이 골라야 하니 거기서 멈춘다. 건너뛸 장면이 없으면 false.
    /// </summary>
    private bool SkipScene()
    {
        bool battleScene = _runningEvent >= 0, fieldScene = FieldOpen && _fieldEvent >= 0;
        if (!battleScene && !fieldScene) return false;
        SkipTalk();
        if (battleScene)
        {
            _eventWaitUntil = 0;
            StepEvent();
        }
        if (fieldScene)
        {
            _fieldWaitUntil = 0;
            FinishFieldAnimations();
        }
        return true;
    }

    /// <summary>글이 다 나온 뒤 118틱을 더 두면 저절로 넘어간다.</summary>
    private void UpdateTalk()
    {
        if (_talk is not { } t) return;
        int shown = (int)((_lastTime - t.Start) * TicksPerSecond * TalkCharsPerTick);
        if (shown >= t.Text.Length) _talkFilled = true;
        if (_talkFilled && (_lastTime - t.Start) * TicksPerSecond > TalkAutoTicks) CloseTalk();
    }

    /// <summary>지금까지 흘러나온 만큼만 잘라 낸 글.</summary>
    private string TalkShownText((bool Box, int Speaker, string Name, string Text, int Face, double Start) t)
    {
        if (_talkFilled) return t.Text;
        int shown = (int)((_lastTime - t.Start) * TicksPerSecond * TalkCharsPerTick);
        return shown >= t.Text.Length ? t.Text : t.Text[..Math.Max(0, shown)];
    }

    /// <summary>
    /// 글에 박힌 표시를 푼다 — 강제 줄바꿈은 <c>$n $N $p $P</c> 넷이고(0x10028dc0), <c>$m0</c> 는 색 전환이라 지운다.
    /// </summary>
    private static string[] TalkLines(string text) =>
        text.Replace("$m0", "").Split(["$n", "$N", "$p", "$P"], StringSplitOptions.None);

    /// <summary>한 줄 내려가는 만큼 — 글자 높이 + 4픽셀(<c>0x1002993c</c>).</summary>
    private int TalkLineStep(float size) => GetText("가", White, size).H + 4;

    /// <summary>창 너비에 맞춰 글자 단위로 줄을 접는다 — 한국어라 낱말 단위로 끊지 않는다.</summary>
    private List<string> WrapTalk(IEnumerable<string> lines, int width, float size)
    {
        var wrapped = new List<string>();
        foreach (string line in lines)
        {
            if (line.Length == 0) { wrapped.Add(""); continue; }
            int start = 0;
            while (start < line.Length)
            {
                int take = line.Length - start;
                while (take > 1 && GetText(line.Substring(start, take), White, size).Item2 > width) take--;
                wrapped.Add(line.Substring(start, take));
                start += take;
            }
        }
        return wrapped;
    }

    private void DrawTalk()
    {
        if (_talk is not { } t) return;
        var (ox, oy) = (_camX, _camY);
        int tick = (int)((_lastTime - t.Start) * TicksPerSecond);
        var lines = TalkLines(TalkShownText(t));

        if (t.Box)
        {
            // 600 — 원본은 640×480 기준 (10,370) 620×100 이다.
            // 필드·모세스처럼 640×480 틀 안에서 도는 화면이면 그 틀 안에, 전투면 보이는 판 아래에 둔다.
            int w, x, y, h = 100;
            if (FieldOpen)
            {
                var (fx, fy) = MosesOrigin();
                w = 620;
                x = fx + 10;
                y = fy + 370;
            }
            else
            {
                w = Math.Min(620, BoardWidth - 20);
                x = ox + (BoardWidth - w) / 2;
                y = oy + ViewHeight - h - 10;
            }
            DarkenRect(x - 1, y - FrameTitleH - 1, w + 2, h + FrameTitleH + 2, 8);
            DrawGameFrame(x, y, w, h, t.Name);
            // 초상화 — 인물 얼굴 그림을 왼쪽에. 원본은 Obs 모션 2×얼굴+11 이지만 데모는 뽑아 둔 얼굴 그림을 쓴다.
            int textLeft = x + 12;
            int faceCode = t.Speaker >= 0 && _units[t.Speaker].Data is { } sc ? sc.Code : _talkFace;
            if (faceCode != 0 && _faces.TryGetValue(faceCode, out var face))
            {
                BlitScaled(face, x + 8, y + 6, 84, 84);
                textLeft = x + 100;
            }
            // 줄 내림은 <b>그 줄 가장 큰 글자 높이 + 4px</b> 이다(0x1002993c) — 원본 굴림 9pt 로 16px.
            var boxLines = WrapTalk(lines, x + w - 16 - textLeft, 13);
            int step = TalkLineStep(13);
            for (int i = 0; i < boxLines.Count && 10 + (i + 1) * step <= h; i++)
                DrawText(boxLines[i], textLeft, y + 10 + i * step, White, 13);
            // 오른쪽 아래 「다음」 표시 — 깜빡인다.
            if (_talkFilled && tick % 20 < 12) DrawText("▼", x + w - 22, y + h - 22, 0xFFFFE070, 13);
            return;
        }

        // 601 — 말하는 이 머리 위 말풍선. 화면 밖이면 판 가운데.
        int bw = 174, bh = 60;
        int bx, by;
        if (FieldTalkHead() is { } head)
        {
            // 필드에서는 말하는 이가 전투 유닛이 아니라 필드 인물이다 — 규칙은 같이 (x+30, y−200).
            bx = head.X + 30;
            by = head.Y - 200;
        }
        else if (t.Speaker >= 0 && _units[t.Speaker].Alive)
        {
            var (fx, fy) = UnitFoot(_units[t.Speaker]);
            bx = Math.Clamp(fx + 30, _camX + 8, _camX + ViewWidth - bw - 8);
            by = Math.Clamp(fy - 200, _camY + GridTop + 8, _camY + ViewHeight - bh - 8);
        }
        else
        {
            bx = _camX + (ViewWidth - bw) / 2;
            by = _camY + ViewHeight / 2 - bh;
        }
        // 글이 길면 창이 늘어나지만 <b>폭 200·높이 100 까지</b>다 — 그보다 길면 줄을 접는다.
        // 크기는 <b>다 나온 글</b>로 재야 흐르는 동안 창이 들썩이지 않는다.
        const int BalloonMax = 200;
        var full = WrapTalk(TalkLines(t.Text), BalloonMax - 16, 12);
        int widest = full.Max(l => GetText(l, White, 12).Item2);
        bw = Math.Clamp(widest + 16, 50, BalloonMax);
        bh = Math.Clamp(full.Count * 18 + 26, 50, 100);

        // 필드처럼 640×480 틀 안에서 도는 화면이면 말풍선도 그 틀을 넘지 않는다.
        if (FieldOpen)
        {
            var (fx, fy) = MosesOrigin();
            bx = Math.Clamp(bx, fx + 8, fx + MosesW - bw - 8);
            by = Math.Clamp(by, fy + 8, fy + MosesH - bh - 8);
        }
        else
            bx = Math.Clamp(bx, 8, BoardWidth - bw - 8);

        DarkenRect(bx - 1, by - FrameTitleH - 1, bw + 2, bh + FrameTitleH + 2, 8);
        DrawGameFrame(bx, by, bw, bh, t.Name);
        var balloonLines = WrapTalk(lines, bw - 16, 12);
        int balloonStep = TalkLineStep(12);
        for (int i = 0; i < balloonLines.Count && i * balloonStep + 8 < bh; i++)
            DrawText(balloonLines[i], bx + 8, by + 6 + i * balloonStep, White, 12);
        if (_talkFilled && tick % 20 < 12) DrawText("▼", bx + bw - 18, by + bh - 20, 0xFFFFE070, 12);
    }
}
