using System.IO;
using System.Text.RegularExpressions;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 대사창 — 전투 이벤트 행동 <c>600</c>(아래 대사 상자)·<c>601</c>(말풍선), 필드·챕터 스크립트 <c>600</c>·<c>601</c>·<c>602</c>(통신 말풍선)·<c>603</c>·<c>609</c>.
/// </summary>
/// <remarks>
/// 전투 글은 <b><c>Tlk\&lt;전투번호&gt;.tlb</c></b> 에서 온다(<c>0x1004a420</c> 갈래 1) — 챕터가 쓰는 <c>.tlc</c> 도 <c>TXR</c> 도 아니다.
/// 서식은 <see cref="TalkTable"/> 이 그대로 읽는다.
/// <para>전투 인자(18바이트 명령의 낱말 여덟 중 네 개만 쓴다):</para>
/// <list type="bullet">
/// <item><b>0 말하는 이</b> — <c>10000+N</c> 이면 Btl 배치표 N번, 1~9999 면 인물 번호, 0 이면 없음(화면 가운데).</item>
/// <item><b>2 글 번호</b> — 그 전투 <c>.tlb</c> 의 번호.</item>
/// <item><b>3 음성</b>(600 만 — <c>Bgm\NNNN.bgm</c>, 0 이면 없음) · <b>4 얼굴</b> — 초상화 모션 = <c>2×값 + 11</c>.</item>
/// </list>
/// <b>600</b> 은 화면 아래 <c>(10, 370) 620×100</c> 상자(<c>0x1003c0d0</c>)에 초상화와 이름을 얹고,
/// <b>601</b> 은 말하는 이 머리 위 <c>(x+30, y−200) 174×60</c> 말풍선(<c>0x1003b130</c>)이다.
/// <para>원본 대사창의 한살이(감사 3 「대사창」 T0~T9):</para>
/// <list type="bullet">
/// <item>펴짐·접힘 — 601/602 는 10틱, 600 은 8틱(<c>0x1003b980</c> <c>+0x13c/+0x138</c>). 펴지는 동안 글·이름·▼ 는 없고 음성도 다 펴진 뒤에 튼다.
/// 닫을 때 효과음 <b>95</b> 를 틀고 같은 틱 수로 거꾸로 접은 뒤에야(<c>+0x108 = 4</c>) 뒤따르는 행동 1 이 풀린다.</item>
/// <item>글 흘리기 — <b>3틱에 한 글자</b>(<c>0x10029070</c>, 속도 6 → <c>0x1002958a</c> 한 걸음 1글자, <c>$n</c> 도 한 걸음).
/// 음성이 있으면 음성 진행에 맞춘다: 보일 글자 = 전체 × 재생 위치 / 길이(<c>0x10029088</c>~<c>0x100290a6</c>).</item>
/// <item>긴 글 — 칸(상자 4줄·말풍선 3줄)을 넘으면 16걸음 동안 1픽셀씩 밀어 올리고 한 줄을 버린 뒤 이어 흘린다(<c>0x100290f0</c> 상태 3·5).</item>
/// <item>저절로 넘김 — 글이 다 나오고 음성도 끝난 <b>뒤부터</b> 세어 120번째 틱(<c>0x1003b99e</c>~<c>0x1003b9cf</c>). ▼ 도 같은 조건.</item>
/// <item>601 은 칸이 <b>둘</b>(<c>[0x101bfe20]/[0x101bfe24]</c>) — 행동 1 없이 601 → 601 이면 두 말풍선이 함께 뜬다(<c>0x100ef2a5</c>~<c>0x100ef2c9</c>).
/// 600 은 <c>[0x101bfe30]</c>, 602 는 <c>[0x101bfe28]</c> 로 저마다 제 칸의 옛 창만 지운다.</item>
/// </list>
/// 넘기기는 <b>클릭·아무 키</b>(키는 사용자 요청 — 원본은 왼쪽 클릭만), 우클릭·Esc 는 장면 통째 건너뛰기(사용자 요청).
/// 대사가 도는 동안은 <b>전투가 통째로 멈춘다</b>(<c>0x10066197</c>) — 틱도 차례도 안 흐른다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>대사창 그림 — 말풍선 틀 Obs 0221 · 아래 상자 틀 Obs 0224 · 「다음」 ▼ Obs 0071(분석-UI 「대사창 모양 (talk-ui)」).</summary>
    private const int TalkBalloonObs = 221, TalkBoxObs = 224, TalkNextObs = 71, TalkCloseSound = 95;

    /// <summary>
    /// 602 틀 — Obs <c>222 + 인자3</c>(<c>0x100ef7eb</c>·<c>0x100ef80f</c>): 0222 파랑 넓은 탭 · 0223 초록. 바탕(모션 1)은 효과 6 = 알파 24/31.
    /// 얼굴 Obs 가 없으면 0xe5 = <c>Obs 0229</c>(<c>0x100ef85e</c>).
    /// </summary>
    private const int TalkRadioObs = 222, TalkRadioFaceFallbackObs = 229;

    /// <summary>저절로 넘김 — 조건이 선 뒤 카운터가 118 을 넘는 틱(<c>0x1003b9be</c>).</summary>
    private const int TalkAutoTicks = 118;

    /// <summary>글 한 걸음(한 글자)의 틱 수 — <c>[+0x21c] % 3</c>(<c>0x10029070</c>).</summary>
    private const int TalkStepTicks = 3;

    /// <summary>한 줄 올리기 — 줄 높이 + 4 = 16걸음 동안 1픽셀씩(<c>0x10029197</c>~<c>0x100291c8</c>).</summary>
    private const int TalkScrollSteps = 16, TalkLinePx = 16;

    /// <summary>대사 상자에 그릴 초상화의 Chr 번호 — 필드처럼 말하는 이가 전투 유닛이 아닐 때 쓴다.</summary>
    private int _talkFace;

    /// <summary>떠 있는 대사창 하나(원본 창 객체 <c>0x1003b130</c>/<c>0x1003c0d0</c> 한 개).</summary>
    private sealed class TalkWindow
    {
        /// <summary>600 상자 · 601 말풍선 · 602 통신 말풍선 · 609 제 칸의 상자(<c>[0x101bfe34]</c>).</summary>
        public int Kind;
        /// <summary>눈 깜빡임 — 시작한 틱(−1 이면 안 깜빡이는 중)과 확률을 굴린 마지막 틱.</summary>
        public int BlinkAt = -1, BlinkTick = int.MaxValue;
        /// <summary>아래 상자 꼴인가 — 600 과 609(609 틀 (164,120)~(313,239) 은 아직 안 옮겨 600 상자로 그린다).</summary>
        public bool IsBox => Kind is 600 or 609 or 603;
        /// <summary>603 — 화면 맨 위 한 줄 띠(자막·알림, 0x100ef9d0): 창 (10,0) 620×34, 틀 Obs 0225, 이름·초상 없음, 펴짐 없이 곧바로 뜬다(ba-20 N2).</summary>
        public bool IsBand => Kind == 603;
        /// <summary>600 인자 6 ≠ 0 — 반신 초상 없이(0x100ef169, ba-20 N3).</summary>
        public bool NoPortrait;
        /// <summary>601 칸 0/1(<c>[0x101bfe20]</c>/<c>[0x101bfe24]</c>).</summary>
        public int Slot;
        /// <summary>전투 유닛 자리(없으면 −1) · 필드 말하는 이(<c>10000+열쇠</c>, 머리 위 자리를 찾는다).</summary>
        public int Speaker = -1, FieldSpeaker;
        public string Name = "", Text = "";
        /// <summary>609 카드의 둘째 줄(발신지 TXR — 「페르소 영자 연구소」「T&amp;T 속보」).</summary>
        public string Location = "";
        /// <summary>609 — 모세스 메일 뷰어와 같은 카드 창(0x1003c870).</summary>
        public bool IsCard => Kind == 609;
        /// <summary>초상화 표정(모션 2×값+11) — 600 만 쓴다.</summary>
        public int Pose;
        /// <summary>초상화·얼굴의 Chr 번호(말하는 이가 전투 유닛이 아닐 때).</summary>
        public int FaceCode;
        public int FrameObs = TalkBalloonObs;
        /// <summary>틀 바탕 비침 — 0221 효과 5 = 20/31, 0222/0223 효과 6 = 24/31.</summary>
        public double FrameFade = 20 / 31.0;
        /// <summary>602 — 얼굴 물들이기 방식 10·세기 20 · 얼굴 지직거림(<c>+0x148</c>).</summary>
        public bool Tint, Glitch;

        public double OpenedAt, ClosingAt = -1, DoneAt = -1;
        public int OpenTicks => IsBand ? 1 : IsBox ? 8 : 10;

        // 음성 — 다 펴진 뒤에 튼다(0x1003ba38).
        public int Voice, VoiceTag, ExternalVoiceTag;
        public bool VoiceStarted;
        /// <summary>0 = 푸는 중 · 1 = 틀었음 · −1 = 없음(배경 실이 쓴다).</summary>
        public int VoiceLoaded;
        public float VoiceSeconds;
        public double VoiceStartAt = -1;

        // 글 — 표시 코드를 푼 글, 접은 줄, 글자마다 줄 번호.
        public string Clean = "";
        public List<(int Start, int Len)>? Lines;
        public int[] CharLine = [];
        public int MaxLines, TextLeft;
        public bool Portrait;
        public int Revealed, Steps, TopLine, ScrollPx;
        public bool AllRevealed => Revealed >= Clean.Length;
    }

    /// <summary>떠 있는 대사창 — 나중에 연 것이 뒤. 접히는 중인 창도 다 접힐 때까지 남는다.</summary>
    private readonly List<TalkWindow> _talks = [];

    /// <summary>
    /// 맨 나중에 연 대사창 한 줄(없으면 null) — 필드·카메라·이벤트가 「대사가 떠 있나」를 본다.
    /// 넣으면 그 대사로 창을 연다(행동 603·609 의 <see cref="ShowFieldTalk"/>), null 이면 창을 모두 곧바로 지운다.
    /// </summary>
    private (bool Box, int Speaker, string Name, string Text, int Face, double Start)? _talk
    {
        get => _talks.Count == 0 ? null : (_talks[^1].IsBox, _talks[^1].Speaker, _talks[^1].Name, _talks[^1].Text, _talks[^1].Pose, _talks[^1].OpenedAt);
        set
        {
            if (value is not { } v)
            {
                _talks.Clear();
                StopTalkVoice();
                return;
            }
            // ShowFieldTalk 는 음성을 먼저 틀어 두었다 — 그 표지를 들고 「음성 끝남」을 본다.
            // 609 는 제 칸([0x101bfe34])이라 600 과 서로 안 지운다 — Fld 0360 은 609 창 위에 600 이 겹쳐 뜬다(감사 3 T8).
            OpenTalkWindow(new TalkWindow
            {
                Kind = v.Box ? (RunningFieldCode(609) ? 609 : RunningFieldCode(603) ? 603 : 600) : 601, Speaker = v.Speaker, Name = v.Name, Text = v.Text, Pose = v.Face,
                FaceCode = _talkFace, FieldSpeaker = Fld._fieldTalkOf, ExternalVoiceTag = _talkVoiceTag,
                Location = v.Box && RunningFieldCode(609) ? Fld._talkLocation : "",
            });
        }
    }

    /// <summary>
    /// 지금 필드·챕터 주 사건이 막 읽은 줄이 609 인가 — 609 는 <see cref="ShowFieldTalk"/> 를 거쳐 <see cref="_talk"/> 로 오므로 여기서 칸을 가린다.
    /// 609 는 곁 사건에선 안 돈다(<c>MainOnly</c>).
    /// </summary>
    private const int TalkBandObs = 225;

    private bool RunningField609() => RunningFieldCode(609);

    private bool RunningFieldCode(int code)
    {
        var events = Fld._field?.Events ?? (Mos._mosesOpen ? Mos._mosesChp?.Events : null);
        return events != null && (uint)Fld._fieldEvent < (uint)events.Count && Fld._fieldPc > 0 && Fld._fieldPc <= events[Fld._fieldEvent].Actions.Count
               && events[Fld._fieldEvent].Actions[Fld._fieldPc - 1].Code == code;
    }

    /// <summary>맨 나중 창의 글이 다 나왔나 — 넣는 쪽(필드의 false)은 무시한다.</summary>
    private bool _talkFilled
    {
        get => _talks.Count > 0 && _talks[^1].AllRevealed;
        set { if (value && _talks.Count > 0) FillTalk(_talks[^1]); }
    }

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
        // 20001~20009 는 그 편의 첫 유닛이 말한다(원본 — 전에는 −1 로 가운데에 띄웠다).
        if (value >= 20000 && value < 20010)
            return EventTargets(value, out _).FirstOrDefault(u => u.Alive && u.OnField) is { } first ? Array.IndexOf(_units, first) : -1;
        if (value >= 20000) return -1;
        // 10000+N 은 배열 자리가 아니라 <b>Btl 레코드 번호</b>다 — 빈 칸을 걸러 낸 뒤의 자리와 다르다.
        if (value >= 10000) return Array.FindIndex(_units, u => u.LeaderIndex < 0 && u.Record == value - 10000);
        if (value <= 0) return -1;
        return Array.FindIndex(_units, u => u.ChrCode == value);
    }

    /// <summary>전투 대사 한 줄을 띄운다(행동 600·601).</summary>
    private void ShowTalk(bool box, ScriptCommand a)
    {
        if (_talkSkip) return;

        short A(int i) => i < a.Args.Length ? a.Args[i] : (short)0;
        string text = TalkTableFor(_scene.Id)?[A(2)] ?? "";
        int speaker = TalkSpeaker(A(0));
        string name = speaker >= 0 && _units[speaker].Data is { } c ? _db?.T(c.NameId) ?? "" : "";
        int faceCode = 0;
        // 말하는 이가 판에 없으면(0x1004ebd0, ba-20 V6): Chr 번호(1~9999)는 명부에서 이름·얼굴을 달아 가운데 창으로, 말하는 이 0 은 그 줄을 건너뛴다.
        if (speaker < 0 && A(0) == 0) return;
        if (speaker < 0 && A(0) > 0 && A(0) < 10000 && _db?.Character(A(0)) is { } absent)
        {
            name = _db.T(absent.NameId);
            Fld.LoadFieldFace(absent);
            faceCode = absent.Code;
        }
        // 대사 상자(600)만 음성 칸이 있다 — 전투 말풍선(601)은 a17 = −1(0x10053c46).
        OpenTalkWindow(new TalkWindow { Kind = box ? 600 : 601, Speaker = speaker, Name = name, Text = text, Pose = A(4), Voice = box ? A(3) : 0, FaceCode = faceCode });
    }

    /// <summary>
    /// 필드·챕터 스크립트의 600·601·602 — 말하는 이 <c>10000+열쇠</c>(챕터는 Chr 번호) · 글 · 음성 · 인자3 · 인자4.
    /// </summary>
    /// <remarks>
    /// 600 의 인자3 은 초상화 표정. 602(<c>0x100ef540</c>)는 601 과 같은 말풍선인데 틀이 <c>Obs 222+인자3</c>,
    /// 얼굴을 물들이기 방식 10·세기 20 으로 새로 만들고(<c>0x100ef85e</c>~<c>0x100ef963</c>), 인자4 가 0 이 아니면 얼굴 지직거림(<c>0x100ef984</c>).
    /// 창은 저마다 슬롯이다 — 다 접힐 때(<c>+0x108 == 4</c>)까지 살고 뒤따르는 행동 1 이 기다린다(<c>0x100ef4f0</c>·<c>0x100ef991</c>).
    /// </remarks>
    private void FieldTalkCommand(ScriptCommand a)
    {
        short A(int i) => i < a.Args.Length ? a.Args[i] : (short)0;
        if (_talkSkip)
        {
            // 건너뛰는 중이면 띄우지 않는다 — 602 는 옛 602 만 지우고 지나간다(0x100ef5bf~0x100ef5d6).
            if (a.Code == 602) _talks.RemoveAll(t => t.Kind == 602);
            return;
        }
        int speaker = A(0);
        var w = new TalkWindow { Kind = a.Code, FieldSpeaker = speaker, Text = Fld.FieldText(A(1)), Voice = A(2) };
        if (a.Code == 600) { w.Pose = A(3); w.NoPortrait = A(6) != 0; }
        if (a.Code == 602)
        {
            w.FrameObs = TalkRadioObs + Math.Clamp((int)A(3), 0, 1);
            w.FrameFade = 24 / 31.0;
            w.Tint = true;
            w.Glitch = A(4) != 0;                    // 값 크기는 안 쓴다 — 0 이냐 아니냐만(0x1003bcae)
        }
        Fld._fieldTalkOf = speaker;
        if (Fld._field is { } field && speaker >= 10000
            && field.People.FirstOrDefault(p => p.Key == speaker - 10000) is { } person
            && _db?.Character(person.ChrCode) is { } c)
        {
            w.Name = _db.T(c.NameId);
            Fld.LoadFieldFace(c);
            w.FaceCode = c.Code;
        }
        // 필드의 600·602 도 말하는 이가 Chr 번호(10000 미만)일 수 있다 — 임시 CChr 를 .chr 에서 읽어 이름·얼굴을 쓴다(0x100eefb3·0x100ef63b, ba-20 N1).
        else if (speaker > 0 && speaker < 10000 && _db?.Character(speaker) is { } cc)
        {
            // 챕터 스크립트의 600 [Chr, 글] — 말하는 이가 Chr 번호다(필드는 10000+열쇠).
            w.Name = _db.T(cc.NameId);
            Fld.LoadFieldFace(cc);
            w.FaceCode = cc.Code;
        }
        _talkFace = w.FaceCode;
        OpenTalkWindow(w);
        Fld.HoldSlot(() => _talks.Contains(w), talk: true);
    }

    /// <summary>창을 제 칸에 연다 — 칸에 있던 옛 창은 곧바로 지운다(접힘 없이).</summary>
    private void OpenTalkWindow(TalkWindow w)
    {
        w.OpenedAt = _lastTime;
        if (w.Kind == 601)
        {
            // 0x100ef2a5: 칸0 이 있고 칸1 이 비었으면 칸1, 아니면 칸0 을 지우고 칸0.
            var s0 = _talks.Find(t => t.Kind == 601 && t.Slot == 0);
            var s1 = _talks.Find(t => t.Kind == 601 && t.Slot == 1);
            if (s0 != null && s1 == null) w.Slot = 1;
            else
            {
                if (s0 != null) RemoveTalkWindow(s0);
                w.Slot = 0;
            }
        }
        else
            foreach (var old in _talks.Where(t => t.Kind == w.Kind).ToArray()) RemoveTalkWindow(old);
        _talks.Add(w);
    }

    private void RemoveTalkWindow(TalkWindow w)
    {
        _talks.Remove(w);
        if (w.VoiceTag != 0 && w.VoiceTag == _talkVoiceTag) StopTalkVoice();
    }

    /// <summary>닫기 시작 — 효과음 95 를 (320,240)에 틀고 접는다(0x1003bc10 → 0x1003bc45). 다 접히면 <see cref="UpdateTalk"/> 가 지운다.</summary>
    private void BeginCloseTalk(TalkWindow w)
    {
        if (w.ClosingAt >= 0) return;
        FillTalk(w);
        w.ClosingAt = _lastTime;
        if (w.VoiceTag != 0 && w.VoiceTag == _talkVoiceTag) StopTalkVoice();
        if (w.ExternalVoiceTag != 0 && w.ExternalVoiceTag == _talkVoiceTag) StopTalkVoice();
        Play(TalkCloseSound);
    }

    /// <summary>대사를 모두 곧바로 닫는다 — 장면 건너뛰기.</summary>
    private void CloseTalk()
    {
        if (_talks.Count == 0) return;
        _talks.Clear();
        StopTalkVoice();
        Play(TalkCloseSound);
    }

    /// <summary>
    /// 클릭·키로 넘기기 — 맨 나중에 연 창부터, <b>누르면 곧바로 닫는다</b>(글이 덜 나왔어도 채우는 단계 없이 접힘으로).
    /// </summary>
    /// <remarks>
    /// 원본 넘김 <c>0x1003bfe0</c> 은 <c>[0x10173534]</c>(DVD 판 설정값 0x15e, 쓰는 곳 없음 — 늘 참) 가지로 가서 글 상태를 4 로 적고
    /// <b>같은 함수에서 바로</b> 닫기(vt+0xdc)를 부른다(<c>0x1003c029</c>) — 음성이 도는 중이어도 닫힌다(감사 3 T1). 전에는 첫 클릭이 글을 채웠다.
    /// 설정 &gt; 「대사 첫 클릭은 글 채우기」(<see cref="UserSettings.TalkFillFirst"/>)를 켜면 예전처럼 첫 번째는 채우고 두 번째에 닫는다.
    /// 키 넘김(원본은 왼쪽 클릭만)·우클릭/Esc 장면 건너뛰기는 사용자 요청으로 남긴다 — 키도 클릭과 같은 규칙을 따른다.
    /// </remarks>
    private bool OnTalkInput(bool skipAll = false)
    {
        if (_talks.Count == 0) return false;
        if (skipAll) { SkipTalk(); return true; }
        if (_talks.LastOrDefault(t => t.ClosingAt < 0) is not { } w) return true;   // 접히는 중이면 입력만 먹는다
        if (_talkClickFills && !w.AllRevealed) { FillTalk(w); return true; }
        BeginCloseTalk(w);
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
        bool battleScene = _runningEvent >= 0, fieldScene = FieldOpen && Fld._fieldEvent >= 0;
        if (!battleScene && !fieldScene) return false;
        SkipTalk();
        if (battleScene)
        {
            _eventWaitUntil = 0;
            StepEvent();
        }
        if (fieldScene)
        {
            Fld._fieldWaitUntil = 0;
            Fld.FinishFieldAnimations();
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
        bool fieldScene = (Fld._field != null || (Mos._mosesOpen && Mos._mosesChp != null)) && Fld._fieldEvent >= 0 && Fld._fieldChoices == null;
        if (fieldScene)
        {
            if (Fld._fieldWaitUntil > _lastTime) { Fld._fieldWaitUntil = 0; skipped = true; }
            if (Fld._fieldWaitChannel >= 0) { StopChannelSound(Fld._fieldWaitChannel); Fld._fieldWaitChannel = -1; skipped = true; }
            if (Fld._field != null && Fld.FieldBusy()) { Fld.FinishFieldAnimations(); skipped = true; }
        }
        if (_runningEvent >= 0 && _eventWaitUntil > _lastTime) { _eventWaitUntil = 0; skipped = true; }
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                               $"click-skip {skipped} t {_lastTime:F2} ev {Fld._fieldEvent} pc {Fld._fieldPc} wait {Fld._fieldWaitUntil:F2} talk {_talk != null}" + Environment.NewLine);
        return skipped;
    }

    /// <summary>
    /// 창마다 한 틀 — 펴짐·접힘, 다 펴지면 음성 시작, 글 흘리기·줄 올리기, 글·음성이 다 끝난 뒤 120번째 틱에 저절로 닫기(0x1003b980).
    /// </summary>
    private void UpdateTalk()
    {
        for (int i = _talks.Count - 1; i >= 0; i--)
        {
            if (i >= _talks.Count) continue;
            var w = _talks[i];
            if (w.ClosingAt >= 0)
            {
                if ((_lastTime - w.ClosingAt) * TicksPerSecond >= w.OpenTicks) RemoveTalkWindow(w);
                continue;
            }
            double t = (_lastTime - w.OpenedAt) * TicksPerSecond - w.OpenTicks;
            if (t < 0) continue;                                  // 펴지는 중 — 글도 음성도 아직
            EnsureTalkLayout(w);
            if (w.Voice > 0 && !w.VoiceStarted) { w.VoiceStarted = true; StartTalkWindowVoice(w); }
            AdvanceTalk(w, (int)t);
            if (w.AllRevealed && !TalkVoicePlaying(w))
            {
                if (w.DoneAt < 0) w.DoneAt = _lastTime;
                if ((_lastTime - w.DoneAt) * TicksPerSecond > TalkAutoTicks) BeginCloseTalk(w);
            }
        }
    }

    /// <summary>창의 음성을 튼다 — 원본 창마다 음성 객체(<c>+0x144</c>)가 있지만 데모는 대사 음성 한 줄만 울린다.</summary>
    private void StartTalkWindowVoice(TalkWindow w)
    {
        StopTalkVoice();
        if (Muted) { w.VoiceLoaded = -1; return; }
        int tag = _talkVoiceTag = ++_soundTag;
        // 말하는 이의 화면 x 로 좌우를 가른다(≤192 왼쪽 · ≥448 오른쪽, 감사4 S2). 판 좌표 → 640 틀 x.
        float speakerX = w.Speaker >= 0 && w.Speaker < _units.Length
            ? (float)((UnitFoot(_units[w.Speaker]).X - _camX) * 640.0 / Math.Max(1, ViewWidth)) : float.NaN;
        var (panL, panR) = VoicePan(speakerX);
        w.VoiceTag = tag;
        int id = w.Voice;
        LoadClip(id, pcm =>
        {
            bool play = pcm != null && tag == Volatile.Read(ref _talkVoiceTag);
            if (play)
            {
                w.VoiceSeconds = ClipSeconds(pcm!);
                _mixer.PlayEffect(pcm!, _effectGain, tag, left: panL, right: panR);
            }
            Volatile.Write(ref w.VoiceLoaded, play ? 1 : -1);
            // 배경 실에서 올 수 있다 — 기록은 주 실이 옮겨 적는다(PlayTalkVoice 와 같다).
            if (Trace) _backgroundTrace.Enqueue($"talk voice {id}: {(pcm == null ? "파일 없음" : $"{ClipSeconds(pcm):0.0}초")}");
        });
    }

    /// <summary>음성이 아직 도나(푸는 중 포함) — 저절로 넘김과 ▼ 는 음성이 끝나야 선다(0x1003b99e·0x1003bb82).</summary>
    private bool TalkVoicePlaying(TalkWindow w)
    {
        if (w.ExternalVoiceTag != 0)
            return w.ExternalVoiceTag == _talkVoiceTag && _mixer.IsPlaying(w.ExternalVoiceTag);
        if (w.Voice <= 0 || !w.VoiceStarted) return false;
        return Volatile.Read(ref w.VoiceLoaded) switch
        {
            0 => true,
            1 => _mixer.IsPlaying(w.VoiceTag),
            _ => false,
        };
    }

    /// <summary>글을 흘린다 — 음성이 있으면 재생 위치에, 없으면 3틱 한 걸음에 맞춘다.</summary>
    private void AdvanceTalk(TalkWindow w, int textTick)
    {
        if (w.AllRevealed) return;
        int loaded = w.Voice > 0 && w.VoiceStarted ? Volatile.Read(ref w.VoiceLoaded) : -1;
        if (loaded == 0) return;                                  // 음성을 푸는 동안은 글도 기다린다
        if (loaded == 1 && w.VoiceSeconds > 0)
        {
            if (w.VoiceStartAt < 0) w.VoiceStartAt = _lastTime;
            double frac = _mixer.IsPlaying(w.VoiceTag) ? (_lastTime - w.VoiceStartAt) / w.VoiceSeconds : 1;
            RevealTalkTo(w, (int)(w.Clean.Length * Math.Clamp(frac, 0, 1)));
            return;
        }
        int target = (textTick + TalkStepTicks - 1) / TalkStepTicks;
        while (w.Steps < target && !w.AllRevealed) TalkStep(w);
    }

    /// <summary>다음 글자가 칸 아래로 넘치나 — 넘치면 한 줄 올리기부터(0x10029782 상태 3).</summary>
    private static bool TalkNeedsScroll(TalkWindow w) =>
        w.Revealed < w.Clean.Length && w.CharLine[w.Revealed] >= w.TopLine + w.MaxLines;

    /// <summary>글 한 걸음 — 올리는 중이면 1픽셀, 아니면 한 글자.</summary>
    private static void TalkStep(TalkWindow w)
    {
        w.Steps++;
        if (w.ScrollPx > 0 || TalkNeedsScroll(w))
        {
            if (++w.ScrollPx >= TalkScrollSteps) { w.TopLine++; w.ScrollPx = 0; }
            return;
        }
        w.Revealed++;
    }

    /// <summary>음성에 맞춰 한꺼번에 — 줄 올리기는 그 자리에서 끝낸다.</summary>
    private static void RevealTalkTo(TalkWindow w, int count)
    {
        count = Math.Min(count, w.Clean.Length);
        while (w.Revealed < count)
        {
            if (TalkNeedsScroll(w)) { w.TopLine++; w.ScrollPx = 0; }
            else w.Revealed++;
        }
    }

    /// <summary>글을 다 채운다 — 마지막 칸만큼의 줄을 보인다.</summary>
    private void FillTalk(TalkWindow w)
    {
        EnsureTalkLayout(w);
        w.Revealed = w.Clean.Length;
        w.ScrollPx = 0;
        int last = w.Clean.Length > 0 ? w.CharLine[^1] : 0;
        w.TopLine = Math.Max(w.TopLine, last - w.MaxLines + 1);
    }

    /// <summary>
    /// 글 표시 코드 — <c>$c $f $s $t $w $m $v $d</c> + 한 글자는 3바이트 글꼴 전환(<c>0x100216bf</c> 점프표)이라 지운다
    /// (<c>$m1</c> 이 글자로 찍혔다 — Fld 0006). 강제 줄바꿈 <c>$n $N $p $P</c> 는 한 걸음 먹는 줄바꿈으로.
    /// <c>$p</c> 는 원본에선 쪽 넘김(상태 3)인데 데모는 줄바꿈으로 둔다.
    /// </summary>
    private static readonly Regex TalkFontCode = new(@"\$[cCfFsStTwWmMvVdD].", RegexOptions.Compiled);

    private static string CleanTalkText(string text) =>
        TalkFontCode.Replace(text, "").Replace("$n", "\n").Replace("$N", "\n").Replace("$p", "\n").Replace("$P", "\n");

    /// <summary>글을 강제 줄바꿈으로 나눈 줄들(모세스 편지가 쓴다).</summary>
    private static string[] TalkLines(string text) => CleanTalkText(text).Split('\n');

    /// <summary>글을 칸 폭에 맞춰 글자 단위로 접어 둔다 — 창마다 한 번.</summary>
    private void EnsureTalkLayout(TalkWindow w)
    {
        if (w.Lines != null) return;
        w.Clean = CleanTalkText(w.Text);
        int width;
        if (w.IsBand)
        {
            w.Portrait = false;
            w.TextLeft = 12;
            width = 594;
            w.MaxLines = 1;
        }
        else if (w.IsCard)
        {
            // 609 카드 — 본문 칸 (w−15)×(h−70): 폭 298, 열 줄.
            w.Portrait = false;
            w.TextLeft = 15;
            width = 292;
            w.MaxLines = 10;
        }
        else if (w.IsBox)
        {
            // 상자 글은 (창x+12) 부터 창x+606 까지. 반신 초상이 없고 작은 얼굴이 있으면 얼굴 오른쪽(창x+104)부터.
            int portraitObs = TalkPortraitObs(w);
            int pose = 2 * Math.Max(0, w.Pose) + 11;
            w.Portrait = !w.NoPortrait && portraitObs > 0 && UiFor(portraitObs)?.Clip(pose) is { Keys.Count: > 0 };
            w.TextLeft = !w.NoPortrait && !w.Portrait && _faces.ContainsKey(TalkFaceCode(w)) ? 104 : 12;
            width = 606 - w.TextLeft;
            w.MaxLines = 4;
        }
        else
        {
            width = 153;
            w.MaxLines = 3;
        }
        var lines = new List<(int Start, int Len)>();
        var charLine = new int[w.Clean.Length];
        int pos = 0;
        foreach (string para in w.Clean.Split('\n'))
        {
            if (para.Length == 0) lines.Add((pos, 0));
            int start = 0;
            while (start < para.Length)
            {
                int take = 1;
                while (start + take < para.Length && GetText(para.Substring(start, take + 1), White, 12).W <= width) take++;
                lines.Add((pos + start, take));
                for (int j = 0; j < take; j++) charLine[pos + start + j] = lines.Count - 1;
                start += take;
            }
            int end = pos + para.Length;
            if (end < charLine.Length) charLine[end] = lines.Count - 1;   // 줄바꿈 글자는 그 줄 끝 걸음
            pos = end + 1;
        }
        w.Lines = lines;
        w.CharLine = charLine;
    }

    private int TalkFaceCode(TalkWindow w) => w.Speaker >= 0 && _units[w.Speaker].Data is { } sc ? sc.Code : w.FaceCode;

    private int TalkPortraitObs(TalkWindow w) =>
        w.Speaker >= 0 ? _units[w.Speaker].Data?.FaceId ?? 0 : _db?.Character(w.FaceCode)?.FaceId ?? 0;

    /// <summary>한 줄 내려가는 만큼 — 글자 높이 + 4픽셀(<c>0x1002993c</c>).</summary>
    private int TalkLineStep(float size) => GetText("가", White, size).H + 4;

    private void DrawTalk()
    {
        foreach (var w in _talks.ToArray()) DrawTalkWindow(w);
    }

    /// <summary>보이는 줄을 (left, top) 부터 16픽셀 간격으로 — 올리는 중이면 그만큼 위로.</summary>
    private void DrawTalkLines(TalkWindow w, int left, int top)
    {
        if (w.Lines is not { } lines) return;
        for (int i = 0; i < w.MaxLines; i++)
        {
            int li = w.TopLine + i;
            if (li >= lines.Count) break;
            // 밀려 올라가는 맨 윗줄은 반을 넘기면 지운다 — 원본은 글 칸으로 잘라 낸다(가설).
            if (i == 0 && w.ScrollPx >= TalkScrollSteps / 2) continue;
            var (start, len) = lines[li];
            int shown = Math.Clamp(w.Revealed - start, 0, len);
            if (shown == 0) continue;
            DrawText(w.Clean.Substring(start, shown), left, top + i * TalkLinePx - w.ScrollPx, White, 12);
        }
    }

    private void DrawTalkWindow(TalkWindow w)
    {
        EnsureTalkLayout(w);
        int tick = (int)((_lastTime - w.OpenedAt) * TicksPerSecond);
        double open = w.ClosingAt >= 0
            ? 1 - (_lastTime - w.ClosingAt) * TicksPerSecond / w.OpenTicks
            : (_lastTime - w.OpenedAt) * TicksPerSecond / w.OpenTicks;
        open = Math.Clamp(open, 0, 1);
        bool full = w.ClosingAt < 0 && open >= 1;
        bool ready = w.AllRevealed && !TalkVoicePlaying(w);
        // 원본 대사창은 640×480 화면 기준이다 — 필드·모세스는 그 틀, 전투는 보이는 판의 왼위를 (0,0) 으로 본다.
        // 모세스 대화도 창 아래 끝이 아니라 모세스 틀 아래 끝에 맞춘다 — 창이 틀보다 길면 상자 아래가 틀 밖으로 잘렸다(사용자 보고: Chp 0049).
        bool framed = FieldOpen || Mos._mosesOpen;
        var (sx, sy) = framed ? Mos.MosesOrigin() : (_camX, _camY);
        int screenW = framed ? MosesScene.MosesW : ViewWidth, screenH = framed ? MosesScene.MosesH : ViewHeight;
        _faces.TryGetValue(TalkFaceCode(w), out var face);

        if (w.IsBand)
        {
            // 603 띠 — 틀 Obs 0225(바탕 모션 3·4·5 비침 24/31, 테두리 0·1·2)를 창 (10,0) 에, 글 한 줄(자리 (12,10)은 가설).
            int bandX = sx + 10, bandY = sy;
            for (int m = 3; m <= 5; m++) DrawUi(TalkBandObs, m, 0, bandX, bandY, UiBlend.Alpha, fade: 24 / 31.0);
            for (int m = 0; m <= 2; m++) DrawUi(TalkBandObs, m, 0, bandX, bandY, UiBlend.Alpha);
            DrawTalkLines(w, bandX + w.TextLeft, bandY + 10);
            if (ready) DrawUi(TalkNextObs, 0, tick, bandX + 605, bandY + 26, UiBlend.Alpha);
            return;
        }
        if (w.IsCard)
        {
            // 609(0x100efb30 → 0x1003c870(164,120,313,239, …)) — 모세스 메일 뷰어와 같은 창: 틀 Obs 0226(바탕 모션 2·3 비침 24/31, 테두리 0·1),
            // 얼굴 60×60 @ (+4,+8), 「이름」(+73,+12) / 인물 이름 오른끝 +303, 발신지 (+73,+54) / 「LOCATION」 오른끝 +303, 본문 (+15,+76) 열 줄.
            // 전에는 600 상자에 발신지를 말하는 이 이름 자리에 넣어 그렸다(ba-21 field Y3).
            int cx = sx + (framed ? 164 : (screenW - 313) / 2), cy = sy + (framed ? 120 : (screenH - 239) / 2);
            const uint tag = 0xFFF2DB6F, value = 0xFFDAE6FC;
            if (UiFor(226) != null)
            {
                DrawUi(226, 2, 0, cx, cy, UiBlend.Alpha, fade: 24 / 31.0 * open);
                DrawUi(226, 3, 0, cx, cy, UiBlend.Alpha, fade: 24 / 31.0 * open);
                DrawUi(226, 0, 0, cx, cy, UiBlend.Alpha, fade: open);
                DrawUi(226, 1, 0, cx, cy, UiBlend.Alpha, fade: open);
            }
            else
            {
                DarkenRect(cx - 1, cy - 1, 315, 241);
                StrokeRect(cx, cy, 313, 239, White);
            }
            if (!full) return;
            if (face != null) BlitScaled(face, cx + 4, cy + 8, 60, 60);
            DrawText("이름", cx + 73, cy + 12, tag, 12);
            RightText(w.Name, cx + 303, cy + 12, value, 12);
            DrawText(w.Location, cx + 73, cy + 54, value, 12);
            RightText("LOCATION", cx + 303, cy + 54, tag, 12);
            DrawTalkLines(w, cx + w.TextLeft, cy + 76);
            if (ready) DrawUi(TalkNextObs, 0, tick, cx + 300, cy + 232, UiBlend.Alpha);
            return;
        }
        if (w.IsBox)
        {
            // 600 아래 상자(0x1003c0d0, 그리기 0x1003c630) — 창 (10,370) 620×100. 틀은 통짜 그림 Obs 0224 를 창 (0,−25) 에:
            // 바탕 조각(모션 3·4·5)을 효과 5((11·바탕+20·그림)/31)로 먼저, 테두리(모션 0·1·2)를 불투명으로 위에 얹는다.
            int x = sx + (framed ? 10 : (screenW - 620) / 2), y = sy + screenH - 110;
            if (!full)
            {
                // 8틱 동안 상자 가운데(640×480 의 (320,420))에서 펴지고 같은 틱 수로 접힌다(0x1003b980 +0x138 = 8). 초상·글은 다 펴진 뒤.
                int cx = x + 310, cy = y + 50;
                for (int m = 3; m <= 5; m++) DrawUiScaled(TalkBoxObs, m, x, y - 25, open, cx, cy, cx, cy, 20 / 31.0);
                for (int m = 0; m <= 2; m++) DrawUiScaled(TalkBoxObs, m, x, y - 25, open, cx, cy, cx, cy, 1);
                return;
            }
            // 큰 반신 초상화 — 말하는 이 Chr 의 얼굴 Obs(.chr 10, CChr+0x0e), 모션 2×표정+11 을 화면 (320,480) 기준으로 상자 <b>뒤</b>에
            // 세운다(0x1003c0d0 → 0x100f4a70). 표정은 대사 명령 인자(필드 3 · 전투 4). 원본은 틱마다 1/45 확률로 모션+1(눈)을 겹쳐
            // 깜빡이는데, 데모는 3초마다 한 번 겹친다(가설). 그 모션이 없는 얼굴은 초상화 없이 작은 얼굴로 대신한다.
            int portraitObs = TalkPortraitObs(w);
            int pose = 2 * Math.Max(0, w.Pose) + 11;
            // 초상 모션은 키가 있으면 된다 — 한 장짜리 정지 초상(퉁 파오 Obs 0786 모션 11: 길이 0, 키 1)도 있다.
            // 예전에는 길이 > 0 만 봐서 정지 초상인 인물은 작은 얼굴로 떨어졌다(사용자 보고: Fld 0094).
            bool portrait = w.Portrait && DrawUi(portraitObs, pose, tick, x - 10 + 320, y - 370 + 480, UiBlend.Alpha);
            // 눈 깜빡임(0x1003c55b): 깜빡이는 중이 아니면 틱마다 1/45 확률로 모션 (표정+1)을 한 번 겹친다 — 전에는 3초마다 규칙적으로 깜빡였다(ba-21 field Y5).
            if (portrait && UiFor(portraitObs)?.MotionLength(pose + 1) is > 0 and var blink)
            {
                int now = (int)(_lastTime * TicksPerSecond);
                if (w.BlinkTick == int.MaxValue || now - w.BlinkTick > 60) w.BlinkTick = now;   // 창이 처음 그려질 때부터 센다
                if (w.BlinkAt >= 0 && now - w.BlinkAt >= blink) w.BlinkAt = -1;
                for (; w.BlinkTick < now; w.BlinkTick++)
                    if (w.BlinkAt < 0 && _drawRng.Next(45) == 0) w.BlinkAt = w.BlinkTick + 1;
                if (w.BlinkAt >= 0 && now >= w.BlinkAt)
                    DrawUi(portraitObs, pose + 1, now - w.BlinkAt, x - 10 + 320, y - 370 + 480, UiBlend.Alpha, loop: false);
            }
            for (int m = 3; m <= 5; m++) DrawUi(TalkBoxObs, m, 0, x, y - 25, UiBlend.Alpha, fade: 20 / 31.0);
            for (int m = 0; m <= 2; m++) DrawUi(TalkBoxObs, m, 0, x, y - 25, UiBlend.Alpha);
            // 이름 — 탭(틀 x 0~124, y 창y−25~창y) 가운데. 윗변을 창y−16 에 두었더니 글자가 탭 아래 선에 걸쳤다(사용자 보고).
            var (_, nw, nh) = GetText(w.Name, White, 12);
            DrawText(w.Name, x + 61 - nw / 2, y - 25 + (25 - nh) / 2, White, 12);
            // 원본 상자에는 작은 얼굴이 없다(큰 반신 초상화를 상자 뒤에 세운다). 초상 모션이 없는 얼굴이면 데모는 글 왼쪽에 둔다.
            if (!w.NoPortrait && !w.Portrait && face != null && w.TextLeft > 12) BlitScaled(face, x + 12, y + 8, 84, 84);
            // 글은 4줄 — 넘치면 한 줄씩 올린다(0x100290f0).
            DrawTalkLines(w, x + w.TextLeft, y + 10);
            if (ready) DrawUi(TalkNextObs, 0, tick, x + 605, y + 92, UiBlend.Alpha);
            return;
        }

        // 601·602 말풍선(0x1003b130, 틀 0x1003bdc0) — 174×60 고정. 자리는 말하는 이 발밑 (x, y) 에서
        // 전투 (x+30, max(y−200, 50)−20) · 필드 (x+30, max(y−180, 50)−20). 말하는 이가 없으면 전투 (120,120) · 필드 (350,100).
        // 꼬리점(펴짐이 시작하는 자리)은 전투 (x, y−100) · 필드 (x, y−80), 말하는 이가 없으면 필드 (320,220)(0x100ef6fe·0x100ef7a7).
        int bx, by, tailX, tailY;
        if (TalkHead(w) is { } head)
        {
            (bx, by) = (head.X - sx + 30, Math.Max(head.Y - sy - 180, 50) - 20);
            (tailX, tailY) = (head.X - sx, head.Y - sy - 80);
        }
        else if (w.Speaker >= 0 && _units[w.Speaker].Alive)
        {
            var (fx, fy) = UnitFoot(_units[w.Speaker]);
            (bx, by) = (fx - sx + 30, Math.Max(fy - sy - 200, 50) - 20);
            (tailX, tailY) = (fx - sx, fy - sy - 100);
        }
        else
        {
            (bx, by) = FieldOpen ? (350, 100) : (120, 120);
            (tailX, tailY) = (bx - 30, by + 120);
        }
        // 화면 안으로(0x1003b6ac~) — 오른쪽 끝, 얼굴 자리, 아래 끝, 왼쪽·위.
        SpriteFrame? faceFrame = face ?? (w.Kind == 602 ? UiFor(TalkRadioFaceFallbackObs)?.FrameAt(0, 0) : null);
        if (bx + 200 >= screenW) bx = screenW - 200;
        if (faceFrame != null && bx - 60 < 5) bx = 65;
        if (by + 60 >= screenH) by = screenH - 60;
        bx = Math.Max(bx, 0);
        by = Math.Max(by, 30);
        bx += sx;
        by += sy;
        tailX += sx;
        tailY += sy;

        int ax = bx - 60, ay = by - 20;
        if (!full)
        {
            // 10틱 동안 꼬리점에서 창 자리로 틀 243×88·얼굴 60×60 이 커지며 날아오고, 닫힐 땐 거꾸로(0x1003bdc0).
            int tx = tailX + (int)((ax - tailX) * open), ty = tailY + (int)((ay - tailY) * open);
            DrawUiScaled(w.FrameObs, 1, ax, ay, open, ax, ay, tx, ty, w.FrameFade);
            DrawUiScaled(w.FrameObs, 0, ax, ay, open, ax, ay, tx, ty, 1);
            int size = (int)(60 * open);
            if (faceFrame != null && size > 0) DrawTalkFace(faceFrame, tx + (int)(3 * open), ty + (int)(20 * open), size, w.Tint);
            return;
        }

        // 틀 Obs 0221(602 는 0222/0223)을 창 (−60, −20) 에 — 바탕(모션 1)은 효과 5(602 는 6), 테두리(모션 0)는 불투명. 이름은 탭 가운데 (창x−11, 창y−15).
        DrawUi(w.FrameObs, 1, 0, ax, ay, UiBlend.Alpha, fade: w.FrameFade);
        DrawUi(w.FrameObs, 0, 0, ax, ay, UiBlend.Alpha);
        var (_, bnw, _) = GetText(w.Name, White, 12);
        DrawText(w.Name, bx - 11 - bnw / 2, by - 15, White, 12);
        if (faceFrame != null)
        {
            DrawTalkFace(faceFrame, bx - 57, by, 60, w.Tint);
            if (w.Glitch)
            {
                // 지직거림(0x1003bcfb~0x1003bda4) — 매 틱 줄 r = rand%59 부터 아래를 가로로 rand%59+1 어긋나게 한 번 더.
                // 원본 효과 0x44 의 합성은 모른다(가설) — 반쯤 비치게 겹친다.
                var rng = new Random(HashCode.Combine(tick, w.OpenedAt));
                int row = rng.Next(59), shift = rng.Next(59) + 1;
                DrawTalkFace(faceFrame, bx - 57 + shift, by, 60, w.Tint, row, 0.5);
            }
        }
        // 글은 (창x+12, 창y+10) 부터, 폭 153 · 세 줄 · 줄 내림 16(굴림 9pt). 넘치면 한 줄씩 올린다.
        DrawTalkLines(w, bx + 12, by + 10);
        if (ready) DrawUi(TalkNextObs, 0, tick, bx + 174, by + 60, UiBlend.Alpha);
    }

    /// <summary>필드 말하는 이의 발 자리(판 낱칸) — 창마다 제 말하는 이.</summary>
    private (int X, int Y)? TalkHead(TalkWindow w)
    {
        if (!FieldOpen || w.FieldSpeaker == 0 || Fld.FieldActorOf(w.FieldSpeaker) is not { Visible: true } who) return null;
        var (ox, oy) = Mos.MosesOrigin();
        return (ox + (int)who.X - Fld._fieldCam.X, oy + (int)who.Y - Fld._fieldCam.Y);
    }

    /// <summary>
    /// 얼굴을 size×size 칸에(비율 유지) — <paramref name="tint"/> 면 602 물들이기 방식 10·세기 20:
    /// 채널마다 <c>min(31, c·32/(32−20))</c>(<c>0x1000b7c0</c> 색표 <c>0x1019a018</c>) — 거의 하얗게 밝힌다.
    /// <paramref name="fromRow"/> 는 60줄 얼굴 기준 그 줄부터 아래만(지직거림).
    /// </summary>
    private void DrawTalkFace(SpriteFrame f, int x, int y, int size, bool tint, int fromRow = 0, double fade = 1)
    {
        if (f.W == 0 || f.H == 0 || size <= 0) return;
        double scale = Math.Min((double)size / f.W, (double)size / f.H);
        int dw = Math.Max(1, (int)(f.W * scale)), dh = Math.Max(1, (int)(f.H * scale));
        int left = x + (size - dw) / 2, top = y + (size - dh) / 2;
        int k = Math.Clamp((int)(fade * 256), 0, 256);
        var clip = _uiClip;
        for (int yy = fromRow * size / 60; yy < dh; yy++)
        {
            int py = top + yy;
            if ((uint)py >= BoardHeight || clip is { } c1 && (py < c1.Top || py >= c1.Top + c1.Height)) continue;
            int srcY = yy * f.H / dh;
            for (int xx = 0; xx < dw; xx++)
            {
                int px = left + xx;
                if ((uint)px >= BoardWidth || clip is { } c2 && (px < c2.Left || px >= c2.Left + c2.Width)) continue;
                uint c = f.Px[srcY * f.W + xx * f.W / dw];
                if ((c & 0xFF000000) == 0) continue;
                if (tint)
                {
                    uint Lit(int shift) => (uint)Math.Min(255, (int)(c >> shift & 0xFF) * 32 / 12);
                    c = Lit(16) << 16 | Lit(8) << 8 | Lit(0);
                }
                int i = py * BoardWidth + px;
                if (k < 256)
                {
                    uint d = _fb[i];
                    uint Mix(int shift) => (uint)(((int)(c >> shift & 0xFF) * k + (int)(d >> shift & 0xFF) * (256 - k)) / 256);
                    c = Mix(16) << 16 | Mix(8) << 8 | Mix(0);
                }
                _fb[i] = c | 0xFF000000;
            }
        }
    }

    /// <summary>
    /// UI Obs 한 장을 배율 <paramref name="s"/> 로 — 원래 자리 P 는 <c>(tx,ty) + (P − (ax,ay))·s</c> 로 옮겨 그린다(대사창 펴짐·접힘).
    /// </summary>
    private void DrawUiScaled(int obs, int motion, int x, int y, double s, int ax, int ay, int tx, int ty, double fade)
    {
        if (s <= 0.01 || UiFor(obs) is not { } sprite || sprite.FrameAt(motion, 0) is not { } f) return;
        int left = tx + (int)Math.Floor((x + f.X - ax) * s), top = ty + (int)Math.Floor((y + f.Y - ay) * s);
        int dw = Math.Max(1, (int)(f.W * s)), dh = Math.Max(1, (int)(f.H * s));
        int k = Math.Clamp((int)(fade * 256), 0, 256);
        var clip = _uiClip;
        for (int yy = 0; yy < dh; yy++)
        {
            int py = top + yy;
            if ((uint)py >= BoardHeight || clip is { } c1 && (py < c1.Top || py >= c1.Top + c1.Height)) continue;
            int srcY = Math.Min(f.H - 1, (int)(yy / s));
            for (int xx = 0; xx < dw; xx++)
            {
                int px = left + xx;
                if ((uint)px >= BoardWidth || clip is { } c2 && (px < c2.Left || px >= c2.Left + c2.Width)) continue;
                uint c = f.Px[srcY * f.W + Math.Min(f.W - 1, (int)(xx / s))];
                if ((c & 0xFF000000) == 0) continue;
                int i = py * BoardWidth + px;
                if (k < 256)
                {
                    uint d = _fb[i];
                    uint Mix(int shift) => (uint)(((int)(c >> shift & 0xFF) * k + (int)(d >> shift & 0xFF) * (256 - k)) / 256);
                    c = Mix(16) << 16 | Mix(8) << 8 | Mix(0);
                }
                _fb[i] = c | 0xFF000000;
            }
        }
    }
}
