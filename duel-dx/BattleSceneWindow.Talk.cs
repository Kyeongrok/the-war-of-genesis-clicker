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
    /// <summary>대사창 그림 — 말풍선 틀 Obs 0221 · 아래 상자 틀 Obs 0224 · 「다음」 ▼ Obs 0071(분석-UI 「대사창 모양 (talk-ui)」).</summary>
    private const int TalkBalloonObs = 221, TalkBoxObs = 224, TalkNextObs = 71, TalkCloseSound = 95;
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
    /// 필드 스크립트의 <b>행동 1000</b> 도 이것을 끈다 — 원본의 건너뛰기 깃발 <c>[0x101bffb0]</c> 을 0 으로 돌리는 줄이라,
    /// 프롤로그를 건너뛰어도 1000 을 만나면 거기서부터 다시 제 속도로 흐른다(분석-필드 「행동 전수 대조」).
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

    /// <summary>
    /// 스크립트가 <b>지금 기다리는 것 하나만</b> 끝낸다 — 대사가 없을 때 클릭·Enter·Space.
    /// 한 그림(컷씬)을 몇 초씩 띄워 두는 틱 기다리기(행동 2), 음성이 끝나기를 기다리는 504, 걷기·페이드·카메라를 기다리는 행동 1 을
    /// 그 자리에서 끝내고 다음 줄로 간다. 장면 나머지는 그대로 돈다(통째로 넘기기는 Esc). 원본에는 없다 — 사용자 요청.
    /// </summary>
    private bool SkipCurrentWait()
    {
        if (_talk != null) return false;
        bool skipped = false;
        bool fieldScene = (_field != null || (_mosesOpen && _mosesChp != null)) && _fieldEvent >= 0 && _fieldChoices == null;
        if (fieldScene)
        {
            if (_fieldWaitUntil > _lastTime) { _fieldWaitUntil = 0; skipped = true; }
            if (_fieldWaitChannel >= 0) { StopChannelSound(_fieldWaitChannel); _fieldWaitChannel = -1; skipped = true; }
            if (_field != null && FieldBusy()) { FinishFieldAnimations(); skipped = true; }
        }
        if (_runningEvent >= 0 && _eventWaitUntil > _lastTime) { _eventWaitUntil = 0; skipped = true; }
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                               $"click-skip {skipped} t {_lastTime:F2} ev {_fieldEvent} pc {_fieldPc} wait {_fieldWaitUntil:F2} talk {_talk != null}" + Environment.NewLine);
        return skipped;
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
        int tick = (int)((_lastTime - t.Start) * TicksPerSecond);
        var lines = TalkLines(TalkShownText(t));
        // 원본 대사창은 640×480 화면 기준이다 — 필드는 그 틀, 전투는 보이는 판의 왼위를 (0,0) 으로 본다.
        var (sx, sy) = FieldOpen ? MosesOrigin() : (_camX, _camY);
        int screenW = FieldOpen ? MosesW : ViewWidth, screenH = FieldOpen ? MosesH : ViewHeight;
        int faceCode = t.Speaker >= 0 && _units[t.Speaker].Data is { } sc ? sc.Code : _talkFace;
        _faces.TryGetValue(faceCode, out var face);

        if (t.Box)
        {
            // 600 아래 상자(0x1003c0d0, 그리기 0x1003c630) — 창 (10,370) 620×100. 틀은 통짜 그림 Obs 0224 를 창 (0,−25) 에:
            // 바탕 조각(모션 3·4·5)을 효과 5((11·바탕+20·그림)/31)로 먼저, 테두리(모션 0·1·2)를 불투명으로 위에 얹는다.
            int x = sx + (FieldOpen ? 10 : (screenW - 620) / 2), y = sy + screenH - 110;
            // 큰 반신 초상화 — 말하는 이 Chr 의 얼굴 Obs(.chr 10, CChr+0x0e), 모션 2×표정+11 을 화면 (320,480) 기준으로 상자 <b>뒤</b>에
            // 세운다(0x1003c0d0 → 0x100f4a70). 표정은 대사 명령 인자(필드 3 · 전투 4). 원본은 틱마다 1/45 확률로 모션+1(눈)을 겹쳐
            // 깜빡이는데, 데모는 3초마다 한 번 겹친다(가설). 그 모션이 없는 얼굴은 초상화 없이 작은 얼굴로 대신한다.
            int portraitObs = t.Speaker >= 0 ? _units[t.Speaker].Data?.FaceId ?? 0 : _db?.Character(_talkFace)?.FaceId ?? 0;
            int pose = 2 * Math.Max(0, t.Face) + 11;
            bool portrait = portraitObs > 0 && UiFor(portraitObs)?.MotionLength(pose) > 0
                            && DrawUi(portraitObs, pose, tick, x - 10 + 320, y - 370 + 480, UiBlend.Alpha);
            if (portrait && UiFor(portraitObs)?.MotionLength(pose + 1) is > 0 and var blink && tick % 90 < blink)
                DrawUi(portraitObs, pose + 1, tick % 90, x - 10 + 320, y - 370 + 480, UiBlend.Alpha, loop: false);
            for (int m = 3; m <= 5; m++) DrawUi(TalkBoxObs, m, 0, x, y - 25, UiBlend.Alpha, fade: 20 / 31.0);
            for (int m = 0; m <= 2; m++) DrawUi(TalkBoxObs, m, 0, x, y - 25, UiBlend.Alpha);
            // 이름 — 탭(틀 x 0~124) 가운데 x = 창x+61, 윗변 y = 창y−16.
            var (_, nw, _) = GetText(t.Name, White, 12);
            DrawText(t.Name, x + 61 - nw / 2, y - 16, White, 12);
            // 원본 상자에는 작은 얼굴이 없다(큰 반신 초상화를 상자 뒤에 세운다 — 아직 없음). 얼굴이 있으면 데모는 글 왼쪽에 둔다.
            int textLeft = x + 12;
            if (!portrait && face != null) { BlitScaled(face, x + 12, y + 8, 84, 84); textLeft = x + 104; }
            var boxLines = WrapTalk(lines, x + 606 - textLeft, 12);
            for (int i = 0; i < boxLines.Count && i < 4; i++)
                DrawText(boxLines[i], textLeft, y + 10 + i * 16, White, 12);
            if (_talkFilled) DrawUi(TalkNextObs, 0, tick, x + 605, y + 92, UiBlend.Alpha);
            return;
        }

        // 601 말풍선(0x1003b130, 틀 0x1003bdc0) — 174×60 고정. 자리는 말하는 이 발밑 (x, y) 에서
        // 전투 (x+30, max(y−200, 50)−20) · 필드 (x+30, max(y−180, 50)−20). 말하는 이가 없으면 전투 (120,120) · 필드 (350,100).
        int bx, by;
        if (FieldTalkHead() is { } head)
            (bx, by) = (head.X - sx + 30, Math.Max(head.Y - sy - 180, 50) - 20);
        else if (t.Speaker >= 0 && _units[t.Speaker].Alive)
        {
            var (fx, fy) = UnitFoot(_units[t.Speaker]);
            (bx, by) = (fx - sx + 30, Math.Max(fy - sy - 200, 50) - 20);
        }
        else (bx, by) = FieldOpen ? (350, 100) : (120, 120);
        // 화면 안으로(0x1003b6ac~) — 오른쪽 끝, 얼굴 자리, 아래 끝, 왼쪽·위.
        if (bx + 200 >= screenW) bx = screenW - 200;
        if (face != null && bx - 60 < 5) bx = 65;
        if (by + 60 >= screenH) by = screenH - 60;
        bx = Math.Max(bx, 0);
        by = Math.Max(by, 30);
        bx += sx;
        by += sy;

        // 틀 Obs 0221 을 창 (−60, −20) 에 — 바탕(모션 1)은 효과 5, 테두리(모션 0)는 불투명. 이름은 탭 가운데 (창x−11, 창y−15).
        DrawUi(TalkBalloonObs, 1, 0, bx - 60, by - 20, UiBlend.Alpha, fade: 20 / 31.0);
        DrawUi(TalkBalloonObs, 0, 0, bx - 60, by - 20, UiBlend.Alpha);
        var (_, bnw, _) = GetText(t.Name, White, 12);
        DrawText(t.Name, bx - 11 - bnw / 2, by - 15, White, 12);
        if (face != null) BlitScaled(face, bx - 57, by, 60, 60);
        // 글은 (창x+12, 창y+10) 부터, 폭 153 · 세 줄 · 줄 내림 16(굴림 9pt).
        var balloonLines = WrapTalk(lines, 153, 12);
        for (int i = 0; i < balloonLines.Count && i < 3; i++)
            DrawText(balloonLines[i], bx + 12, by + 10 + i * 16, White, 12);
        if (_talkFilled) DrawUi(TalkNextObs, 0, tick, bx + 174, by + 60, UiBlend.Alpha);
    }
}
