using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 필드 화면(장면 3) — 챕터가 「저절로 들어가는 장소」로 쓰는 연출 장면.
/// </summary>
/// <remarks>
/// 필드는 <b>걸어다니는 화면이 아니다</b>. 누를 수 있는 것이 없고 처음부터 끝까지 스크립트가 돌며,
/// 사람이 개입하는 곳은 <b>고르기(행동 604·605)</b> 뿐이다. 나가는 길도 스크립트뿐 —
/// <c>6</c> 다른 필드 · <c>7</c> 끝내고 모세스 · <c>10</c> 전투 · <c>11</c> 끝 · <c>12</c> 타이틀.
/// <para>
/// 이게 있어야 <b>진행 깃발이 서기 시작한다</b>. <c>Chp 0010</c> 은 장소 5(프롤로그, 자동 발생)로 <c>Fld 0019</c> 를 열고,
/// 그것이 <c>Fld 0012</c> 로 넘어가 고르기 끝에 <b>깃발 13 = 1</b> 을 세운다 — 그래야 「코어헌터 훈련장(<c>Btl 0045</c>)」이 열린다.
/// </para>
/// 데모는 <b>연출을 뺀 최소판</b>이다: 배경 한 장과 대사·고르기만 그리고, 인물·물체·카메라·화면 전환(202·208·302·400번대·900)은 넘긴다.
/// 진행에 필요한 조건 <c>0·100·101</c> 과 행동 <c>0·1·2·3·6·7·10·11·12·100·101·102·103·600·601·604·605</c> 는 모두 돈다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private FieldFile? _field;
    private TalkTable? _fieldTalk;

    /// <summary>필드 안에서만 쓰는 바이트 변수 — 고르기 답이 여기 들어간다(<c>0x101bfeac</c>).</summary>
    private readonly byte[] _fieldVars = new byte[256];

    private int _fieldEvent = -1, _fieldPc;

    /// <summary>행동 0 이 다른 사건을 부를 때 돌아올 자리 — (사건, 다음 줄).</summary>
    private readonly Stack<(int Event, int Pc)> _fieldReturn = new();
    private double _fieldWaitUntil;

    /// <summary>이벤트마다 지금까지 돈 횟수.</summary>
    private int[] _fieldFired = [];

    /// <summary>고르는 중인 항목들 — (글, 몇 번째). 고른 차례가 <see cref="_fieldChoiceVar"/> 에 들어간다.</summary>
    private List<string>? _fieldChoices;
    private int _fieldChoiceVar, _fieldChoicePick;

    /// <summary>
    /// 스크립트가 놓은 그림들(행동 302) — 필드 640×480 틀 안 자리.
    /// </summary>
    /// <remarks>
    /// 인자 0 이 10000 보다 작으면 그 <c>Obs</c> 를 새로 놓고, 10000 이상이면 파일에 있는 <b>물체 열쇠</b> 를 가리킨다(<c>0x100f1d90</c>).
    /// 자리는 인자 3·4 다(<c>Fld 0019</c> 의 <c>302 [1457,0,0,318,255]</c> 처럼 — 자료로 미룬 것이라 가설).
    /// </remarks>
    private readonly List<(int Obs, int Motion, int X, int Y, double Start)> _fieldPictures = [];

    /// <summary>
    /// 화면 전환(행동 900) — 덮었다 걷는 연출.
    /// </summary>
    /// <remarks>
    /// <c>900 [a0, a1, a2, a3, a4]</c> — a0 0 이면 찍어 둔 화면을 다시 쓰고 1 이면 새로 찍는다 · a1 무늬(1 → 물들이기 방식 2 = 검정) ·
    /// <b>a2 덮는 틱</b>(0 이면 처음부터 덮인 채) · <b>a3 걷어내는 틱</b>(0 이면 안 걷음) · a4 화면에 넣을 층 수.
    /// 정도 눈금은 0~31. 데모는 무늬를 <b>검정·흰색 두 가지</b>로만 흉내 낸다.
/// <b>a4 는 가리는 층 수</b>다 — 8 이면 다 가리지만 자료의 131번은 그보다 작다(0 이 11번·7 이 61·6 이 20·5 가 24…).
/// 그런 줄은 배경만 덮고 그 위 층의 인물·물체는 안 덮는 연출이라, 덮개보다 나중에 그린다.
    /// <c>Fld 0019</c> 는 시작에 <c>900 [1,1,0,40,8]</c>(40틱에 걸쳐 걷기), 끝에 <c>900 [0,1,40,0,8]</c>(40틱에 걸쳐 덮기)를 쓴다.
    /// </remarks>
    private (double Start, int CoverTicks, int UncoverTicks, bool White)? _fieldFade;

    /// <summary>덮기가 가리는 층 수(900 의 a4) — 8 이면 인물·물체까지 다 가린다.</summary>
    private int _fieldFadeCover = 8;

    /// <summary>
    /// 걷어내는 전환(903 빗살 지우기 · 904 줄 늘여 쓸기).
    /// </summary>
    /// <remarks>
    /// 901·903~909 는 원본에서 <b>한 함수</b>(<c>0x100f2770</c>)가 돌리고 인자 자리도 같다 —
    /// <b>a0 방향</b>(0 이면 지금 화면 → 그림이라 끝나도 그림이 남고, ≠0 이면 그림 → 지금 화면이라 끝에 그림을 걷는다) ·
    /// <b>a1 <c>Bgr</c> 번호</b> · <b>마지막 인자는 전환이 가리는 층 수</b>(0~8, 그보다 위 층은 전환 위에 덧그린다).
    /// 전환이 도는 동안 스크립트는 멈춘다(<c>0x100f2b13</c>).
    /// <para>
    /// <c>Base</c> 는 바탕, <c>Over</c> 는 걷히면서 드러나는 그림이다. 904 는 그림 <b>한 장</b>만 쓰고
    /// 바탕은 그 그림의 <b>경계 줄을 늘여</b> 채우므로 <c>Base</c> 가 없다.
    /// </para>
    /// </remarks>
    private sealed record FieldWipe(int Kind, int Way, int Ticks, double Start, uint[]? Base, uint[] Over);

    private FieldWipe? _fieldWipe;

    /// <summary>
    /// 필드 인물 하나의 지금 모습 — 자리·모션·좌우반전, 그리고 걷는 중이면 어디서 어디로.
    /// </summary>
    /// <remarks>
    /// 모션 번호는 전투와 같은 규칙이다 — <b>모션 = 동작 × 3 + 방향</b>(0 뒷모습 · 1 옆모습 · 2 앞모습),
    /// <b>방향 3 은 옆모습(1)을 좌우로 뒤집은 것</b>. 그래서 <b>서기는 동작 0 = 모션 0·1·2</b> 이고,
    /// 필드 로더가 인물에 넣는 초기 모션은 <b>2(앞모습 서기)</b> 다(<c>0x100eca4f</c>) — 0 으로 두면 등을 보이고 선다.
    /// </remarks>
    private sealed class FieldActor(FieldPerson person)
    {
        public int Key { get; } = person.Key;
        public int ChrCode { get; } = person.ChrCode;
        public int Layer { get; set; } = person.Layer;
        public double X { get; set; } = person.X;
        public double Y { get; set; } = person.Y;
        public int Motion { get; set; } = 2;          // 앞모습 서기

        /// <summary>그 모션을 건 때 — 모션마다 처음부터 돌게 한다.</summary>
        public double MotionStart { get; set; }
        public bool Mirror { get; set; }
        public bool Visible { get; set; } = true;

        /// <summary>지금 밝기 0~1 — 원본은 8단계다(행동 210·211).</summary>
        public double Alpha { get; set; } = 1;

        /// <summary>밝기가 옮겨 가는 중 — (시작 밝기, 목표 밝기, 걸리는 틱, 시작한 때).</summary>
        public (double From, double To, int Ticks, double Start)? Fade { get; set; }

        /// <summary>걷는 중 — (시작 자리, 목적지, 걸리는 틱, 시작한 때, 다 걸으면 설 모션).</summary>
        public (double FromX, double FromY, double ToX, double ToY, int Ticks, double Start, int EndMotion, bool EndMirror)? Walk { get; set; }
    }

    private List<FieldActor> _fieldActors = [];

    /// <summary>
    /// 필드에 놓인 물체 하나 — 파일에 적힌 것과 스크립트가 새로 놓은 것을 같이 담는다.
    /// </summary>
    /// <remarks>
    /// 물체 행동은 인물 행동과 <b>짝</b>이다 — 300·301·302·303·304·305·306·307 이 205·206·208·209·210·211·212·213 에 맞선다.
    /// 층 번호는 파일 값 그대로 <b>0바탕</b>이고, <b>−1 은 배경에 붙는다</b>(로더가 <c>+0x150 + 층×4</c> 에 그대로 넣는다).
    /// </remarks>
    private sealed class FieldProp
    {
        public int Key { get; init; } = -1;
        public int Obs { get; init; }
        public int Motion { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public int Layer { get; set; }
        public bool Mirror { get; set; }
        public bool Visible { get; set; } = true;
        public double Start { get; set; }

        /// <summary>
        /// 스크립트(302)가 건 모션이 한 바퀴 끝나는 때 — 그때까지 행동 1 이 기다린다. 원본 물체는 틱마다 셈을 올려
        /// 모션 끝에 닿으면 끝 표시(<c>+0x68</c>)를 세운다(<c>0x100f4c80</c>). 자료에서 302 바로 뒤가 행동 1 인 곳이 869번이다.
        /// </summary>
        public double PlayUntil { get; set; }

        /// <summary>옮기는 중 — (시작, 목표, 틱 수, 시작한 때).</summary>
        public (double FromX, double FromY, double ToX, double ToY, int Ticks, double Start)? Move { get; set; }
    }

    private List<FieldProp> _fieldProps = [];

    /// <summary>
    /// 화면이 배경의 어느 자리를 비추고 있나 — 물체·인물도 이만큼 밀어 그린다.
    /// </summary>
    /// <remarks>
    /// 배경은 640×480 보다 넓고(예: <c>Bgr 0047</c> 이 951×1000), 필드 머리가 첫 자리를 정한다.
    /// 행동 <b>400</b> 은 그 자리를 <b>바로 잡고</b>, <b>401</b> 은 <b>그만큼 민다</b>(<c>0x100ee020</c> 이 층 자리에서 인자를 뺀다).
    /// </remarks>
    private (int X, int Y) _fieldCam;

    /// <summary>카메라가 옮겨 가는 중 — (시작, 목표, 틱 수, 시작한 때).</summary>
    private (double FromX, double FromY, double ToX, double ToY, int Ticks, double Start)? _fieldCamMove;

    /// <summary>방향(0 뒤 · 1 옆 · 2 앞 · 3 옆 반대)에 맞는 걷기·서기 모션과 좌우반전.</summary>
    private static (int Walk, int Stand, bool Mirror) FieldFacing(int direction) => direction switch
    {
        0 => (3, 0, false),
        1 => (4, 1, false),
        2 => (5, 2, false),
        _ => (4, 1, true),
    };

    private bool FieldOpen => _field != null;

    /// <summary>DUELDX_FIELD=&lt;번호&gt; 면 그 필드를 바로 연다(화면 밖 시험용).</summary>
    private void OpenFieldIfAsked()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("DUELDX_FIELD"), out int id) && id > 0) OpenField(id);
    }

    /// <summary>
    /// DUELDX_WIPE=&lt;행동&gt;,&lt;a0&gt;,&lt;a1&gt;,&lt;a2&gt;,&lt;a3&gt; 면 필드를 연 뒤 그 전환을 한 번 건다(화면 밖 시험용).
    /// 전환은 스크립트 한참 뒤에나 나와서, 그것만 따로 보려면 이렇게 부른다.
    /// </summary>
    private bool RunWipeIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_WIPE") is not { Length: > 0 } text) return false;
        var n = text.Split(',').Select(t => int.TryParse(t.Trim(), out int v) ? v : 0).ToArray();
        if (n.Length < 4) return false;
        if (n[0] == 903)
        {
            int bands = Math.Max(1, n[3]);
            BeginFieldWipe(903, bands, MosesW / bands + bands, n[1] == 0, n[2]);
        }
        else if (n[0] == 901)
            BeginFieldWipe(901, n[3], Math.Max(1, n.Length > 4 ? n[4] : 60), n[1] == 0, n[2]);
        else
            BeginFieldWipe(904, n[3], Math.Max(1, n.Length > 4 ? n[4] : 60), n[1] == 0, n[2]);
        return true;
    }

    /// <summary>그 필드를 연다. 자료가 없으면 false.</summary>
    private bool OpenField(int id)
    {
        try
        {
            var files = GameFiles.FromFolder(AssetsFolder.Find("data"));
            if (FieldFile.Parse(id, files.Read("Fld", $"{id:D4}.fld")) is not { } field) return false;
            _field = field;
            _fieldTalk = TalkTable.Parse(files.Read("Tlk", $"{id:D4}.tlf"));
            _fieldFired = new int[field.Events.Count];
            _sideEvents.Clear();
            _fieldWaitWalker = null;
            Array.Clear(_fieldVars);
            _fieldEvent = -1;
            _fieldReturn.Clear();
            _fieldPc = 0;
            _fieldWaitUntil = 0;
            _fieldWaitChannel = -1;
            StopAllChannelSounds();
            _fieldHoldSince = 0;
            _fieldChoices = null;
            _fieldPictures.Clear();
            _fieldFade = null;
            _fieldWipe = null;
            _fieldActors = [.. field.People.Select(p => new FieldActor(p))];
            // 파일의 물체 — 갈래 칸이 곧 처음 모션이다.
            _fieldProps = [.. field.Objects.Select(o => new FieldProp
            {
                Key = o.Key, Obs = o.Picture, Motion = o.Kind, X = o.X, Y = o.Y, Layer = o.Layer,
            })];
            _fieldCam = (Math.Max(0, field.CameraX), Math.Max(0, field.CameraY));
            _fieldCamMove = null;
            _mosesOpen = false;
            _talk = null;
            // 필드 배경은 640×480 보다 넓다 — 머리가 정한 첫 화면 자리부터 보여 준다.
            ShowMosesBackground(field.Background, Math.Max(0, field.CameraX), Math.Max(0, field.CameraY));
            _mixer.StopMusic();
            if (field.Bgm > 0) PlayMusicFile(field.Bgm, loop: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            // 자료가 빠진 필드는 조용히 넘어가되, DUELDX_TRACE=1 이면 무엇이 빠졌는지 남긴다.
            if (Trace) File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"OpenField {id} failed: {ex.GetType().Name}: {ex.Message}" + Environment.NewLine);
            return false;
        }
    }

    private void CloseField()
    {
        _field = null;
        _fieldTalk = null;
        _sideEvents.Clear();
        _fieldWaitWalker = null;
        _talk = null;
        _fieldChoices = null;
        // 필드가 걸어 둔 소리 채널은 필드와 함께 끝난다 — 안 끄면 다음 화면까지 울리고 504 가 헛기다린다.
        _fieldWaitChannel = -1;
        StopAllChannelSounds();
        // 굴러가던 연출도 함께 끝낸다. 걷기·밝기·물체 옮기기는 <b>필드가 열려 있을 때만</b> 한 걸음씩 나아가므로
        // (StepFieldActors 는 _field 가 있을 때만 돈다), 걷는 채로 필드를 닫으면 FieldBusy() 가 영영 참이 되어
        // 그다음 챕터 스크립트가 줄마다 참을성이 다 될 때까지(10초) 멈춰 선다.
        _fieldActors = [];
        _fieldProps = [];
        _fieldWipe = null;
        _fieldCamMove = null;
    }

    /// <summary>
    /// 필드 스크립트를 한 걸음 나아가게 한다.
    /// </summary>
    /// <remarks>
    /// 이벤트 <b>0</b> 의 행동 인자 0 이 「살려 둘 이벤트」 목록이다(<c>0x100f49a0</c>) — 그 차례로 조건을 보고 하나를 돌린다.
    /// </remarks>
    private void UpdateField()
    {
        // 필드가 없으면 모세스에 떠 있는 챕터의 스크립트 — 원본 챕터 장면도 같은 실행기로 대사·고르기·동료 넣기를 돈다.
        var chapter = _field == null && _mosesOpen ? _mosesChp : null;
        var events = _field?.Events ?? chapter?.Events;
        if (events == null) return;
        if (_field != null) StepFieldActors();
        StepChannelFades();                                           // 행동 506 이 걸어 둔 채널 음량 바꾸기
        if (_field != null) StepSideEvents(events);                    // 대사 없는 곁 사건(문 여닫기 따위)은 따로 나란히 돈다
        if (_fieldChoices != null) _talkSkip = false;                 // 고르기는 사람이 해야 한다 — 건너뛰기를 여기서 멈춘다
        if (_talk != null || _fieldChoices != null) return;          // 대사·고르기가 떠 있으면 기다린다
        if (_talkSkip) { _fieldWaitUntil = 0; if (_field != null) FinishFieldAnimations(); }   // 건너뛰는 중 — 기다림 없이 끝난 자리로
        if (_fieldWaitUntil > _lastTime) return;
        // 걷기(202·203)·자리 옮기기(205·206)는 원본에서 <b>그 줄에 머문다</b> — 핸들러(0x100f0be0 등)가 매 틀 불려 보간하고
        // 셈이 틀 수에 닿아야 다음 줄로 간다(0x100f0dfd). 안 기다리면 뒤따르는 208(서기 모션·방향)이 걷는 도중에 걸려
        // 오른쪽으로 가면서 왼쪽을 보는 인물이 됐다(사용자 보고, Fld 0057·0354).
        if (_fieldWaitWalker is { Walk: not null } && !_talkSkip) return;
        _fieldWaitWalker = null;
        // 행동 500·504 가 걸어 둔 「소리가 끝날 때까지」 — 건너뛰는 중이면 그 소리를 끊고 지나간다.
        if (_fieldWaitChannel >= 0)
        {
            if (_talkSkip) StopChannelSound(_fieldWaitChannel);
            else if (ChannelBusy(_fieldWaitChannel)) return;
            _fieldWaitChannel = -1;
        }

        if (_fieldEvent < 0)
        {
            if (chapter != null && _chapterFired.ContainsKey((chapter.Id, -1))) return;   // 옛 세이브 표시 — 다 돌았다
            foreach (var wanted in events.Count > 0 ? events[0].Actions : [])
            {
                int index = wanted.Args.Length > 0 ? wanted.Args[0] : -1;
                if ((uint)index >= events.Count || index == 0) continue;
                var e = events[index];
                // 되풀이하는 곁 사건(보초 왔다 갔다 따위)은 주 사건으로 잡지 않는다 — 곁에서 돈다(StepSideEvents).
                if (_field != null && IsRepeatingSide(e)) continue;
                int fired = chapter != null ? _chapterFired.GetValueOrDefault((chapter.Id, index)) : _fieldFired[index];
                if (e.MaxFire > 0 && fired >= e.MaxFire) continue;
                if (!e.Conditions.All(FieldCondition)) continue;
                if (chapter != null) _chapterFired[(chapter.Id, index)] = fired + 1; else _fieldFired[index]++;
                _fieldEvent = index;
                _fieldPc = 0;
                _talkSkip = false;
                break;
            }
            if (_fieldEvent < 0)
            {
                // 필드는 더 돌 이벤트가 없으면 나간다 — 곁에서 도는 사건이 남아 있으면 그것이 끝나기(또는 조건이 바뀌기)를 기다린다.
                if (_field != null && _sideEvents.Count == 0) LeaveField();
                return;
            }
        }

        while (_fieldEvent >= 0 && _talk == null && _fieldWaitUntil <= _lastTime)
        {
            if (_fieldEvent >= events.Count) { _fieldEvent = -1; break; }
            var running = events[_fieldEvent];
            if (_fieldPc >= running.Actions.Count)
            {
                // 부른 데가 있으면 그 자리로 돌아가고, 없으면 이 사건이 끝난 것이다.
                if (_fieldReturn.Count > 0) (_fieldEvent, _fieldPc) = _fieldReturn.Pop();
                else { _fieldEvent = -1; _talkSkip = false; }
                continue;
            }
            // 고르기(604)를 낸 뒤에는 뒤따르는 605 들을 <b>먼저 다 읽어</b> 항목을 채우고, 그다음에 사람을 기다린다.
            if (_fieldChoices != null && running.Actions[_fieldPc].Code != 605) return;
            var action = running.Actions[_fieldPc++];
            if (!RunFieldAction(action)) return;  // false = 필드를 떠났다
            if (IsBlockingMove(action) && FieldActorOf(action.Args[0]) is { Walk: not null } walker) { _fieldWaitWalker = walker; return; }
        }
    }

    /// <summary>나란히 도는 곁 사건 — (사건 번호, 다음 줄, 기다림이 끝나는 때, 다 걷기를 기다리는 인물).</summary>
    private readonly List<(int Event, int Pc, double WaitUntil, FieldActor? Walker)> _sideEvents = [];

    /// <summary>주 사건이 다 걷기를 기다리는 인물 — 걷기·자리 옮기기 줄에 머무는 동안.</summary>
    private FieldActor? _fieldWaitWalker;

    /// <summary>원본에서 끝날 때까지 그 줄에 머무는 움직임 — 202·203 걷기, 205·206 자리 옮기기.</summary>
    private static bool IsBlockingMove(ScriptCommand a) => a.Code is 202 or 203 or 205 or 206 && a.Args.Length > 0;

    /// <summary>
    /// 곁에서만 돌리는 사건 — 대사 없이 <b>여러 번</b>(최대 발동 0 또는 2 이상) 도는 것. 원본은 사건을 다 나란히 돌리므로
    /// Fld 0065 사건 4(조건 「변수 20 = 0」, 999번: 보초가 70틱 걷고 돌아온다)가 이야기 사건과 함께 돈다. 우리 실행기가
    /// 이것을 주 사건으로 잡으면 목록에서 앞에 있는 이것만 되풀이해 뒤의 이야기 사건 5·6·7 이 영영 안 돌았다(사용자 보고).
    /// </summary>
    private static bool IsRepeatingSide(FieldEvent e) => e.MaxFire is 0 or > 1 && !e.Actions.Any(a => MainOnly(a.Code));

    /// <summary>곁 사건으로 못 돌리는 행동 — 대사·고르기(사람이 봐야 한다)와 장면을 떠나는 것.</summary>
    private static bool MainOnly(int code) => code is >= 600 and <= 605 or 609 or 6 or 7 or 10 or 11 or 0;

    /// <summary>
    /// 필드 사건을 <b>나란히</b> 돌린다 — 원본 진행기(<c>0x100f47f0</c>)는 사건마다 제 줄(<c>+0x16</c>)·기다림(<c>+0x1c</c>·<c>+0x24</c>)을 들고
    /// 매 틀 살아 있는 사건을 다 한 걸음씩 민다. 그래서 대사가 도는 사건이 변수만 바꿔 두면(<c>100 [160, 2]</c>) 문을 여닫는 작은 사건
    /// (Fld 0037 사건 13·14: 물체 10014 모션 1 열기 · 2 닫기)이 그 사이에 따로 돈다. 우리 실행기는 한 번에 한 사건이라,
    /// 주 사건이 도는 동안 조건이 맞은 <b>대사·고르기·장면 떠나기가 없는</b> 사건만 곁에서 돌린다(사용자 보고: Fld 0037 문이 안 열림).
    /// </summary>
    private void StepSideEvents(IReadOnlyList<FieldEvent> events)
    {
        if (events.Count > 0)
            foreach (var wanted in events[0].Actions)
            {
                int index = wanted.Args.Length > 0 ? wanted.Args[0] : -1;
                if ((uint)index >= events.Count || index == 0 || index == _fieldEvent || (uint)index >= _fieldFired.Length) continue;
                var e = events[index];
                // 주 사건이 쉬고 있을 때는 되풀이하는 사건만 곁에서 연다 — 한 번 도는 연출은 주 사건이 제 기다림(카메라·전환)을 지키며 돈다.
                if (_fieldEvent < 0 && !IsRepeatingSide(e)) continue;
                if (_sideEvents.Any(s => s.Event == index)) continue;
                if (e.MaxFire > 0 && _fieldFired[index] >= e.MaxFire) continue;
                if (e.Actions.Any(a => MainOnly(a.Code)) || !e.Conditions.All(FieldCondition)) continue;
                _fieldFired[index]++;
                _sideEvents.Add((index, 0, 0, null));
            }
        for (int i = _sideEvents.Count - 1; i >= 0; i--)
        {
            var (ev, pc, waitUntil, walker) = _sideEvents[i];
            var acts = events[ev].Actions;
            bool done = false;
            while (!done)
            {
                if (_talkSkip) waitUntil = 0;
                if (waitUntil > _lastTime) break;
                if (walker is { Walk: not null } && !_talkSkip) break;
                walker = null;
                if (pc >= acts.Count) { done = true; break; }
                var a = acts[pc];
                short A0 = a.Args.Length > 0 ? a.Args[0] : (short)0;
                switch (a.Code)
                {
                    case 1:                                    // 제가 건 물체 모션이 끝나기를
                        if (!_talkSkip && _fieldProps.Any(p => p.PlayUntil > _lastTime)) goto hold;
                        pc++;
                        break;
                    case 2: waitUntil = _lastTime + A0 / TicksPerSecond; pc++; break;
                    case 3: done = true; break;
                    case 504: pc++; break;                     // 소리 끝 기다림 — 곁 사건은 안 기다린다
                    default:
                    {
                        // 행동이 거는 기다림(208 모션 한 바퀴 · 900 덮기 따위)은 <b>이 곁 사건</b>의 것이다 — 주 사건의 기다림을 밀면
                        // Fld 0012 사건 11(208 되풀이)이 주 사건을 굶겨 사건 4 가 영영 안 잡혔다.
                        var (mainWait, mainChannel) = (_fieldWaitUntil, _fieldWaitChannel);
                        RunFieldAction(a);
                        if (_fieldWaitUntil != mainWait) waitUntil = Math.Max(waitUntil, _fieldWaitUntil);
                        (_fieldWaitUntil, _fieldWaitChannel) = (mainWait, mainChannel);
                        pc++;
                        if (IsBlockingMove(a) && FieldActorOf(a.Args[0]) is { Walk: not null } w) walker = w;
                        break;
                    }
                }
                continue;
            hold:
                break;
            }
            if (done) _sideEvents.RemoveAt(i); else _sideEvents[i] = (ev, pc, waitUntil, walker);
        }
    }

    /// <summary>나갈 길이 없는 필드(찌꺼기)는 그냥 모세스로 돌아간다.</summary>
    private void LeaveField()
    {
        CloseField();
        OpenMoses();
    }

    /// <summary>DUELDX_ALLEVENTS=1 이면 챕터 사건의 조건을 모두 참으로 본다(화면 밖 시험용 — 대사·고르기가 든 사건을 전부 돌려 본다).</summary>
    private static readonly bool AllChapterEvents = Environment.GetEnvironmentVariable("DUELDX_ALLEVENTS") == "1";

    private bool FieldCondition(ScriptCommand c)
    {
        if (AllChapterEvents && _field == null) return true;
        short A(int i) => i < c.Args.Length ? c.Args[i] : (short)0;
        return c.Code switch
        {
            0 => true,                                                          // 언제나
            100 => Compare(ScriptVars[A(0) & 0xFF], A(1), A(2)),                // 필드 변수(챕터 스크립트면 챕터 변수)
            101 => Compare(A(0) >= 0 && A(0) < _flags.Length ? _flags[A(0)] : 0, A(1), A(2)),
            102 => _inventory.ContainsKey(A(1)) || _party.Values.Any(pc => pc.Items.Contains((ushort)A(1))),   // [파티, 아이템] 가졌나(0x100edb40)
            503 => MailTriggerRead(A(0)),                                        // [메일 방아쇠] 그 편지를 읽었나(0x100edc40)
            505 => _mosesChp is { } chp505 && _planetVisits.Remove((chp505.Id, A(0))),   // [행성] 방문 표시 — 한 번 참, 지운다(0x100edcd0)
            // 평가기(0x100f34f0)가 모르는 조건 번호는 <b>참</b>으로 흘린다(갈래 없음 → eax = 사건 포인터 ≠ 0). 샤이닝 스타 사건 5 의 504 가 그렇다.
            _ => true,
        };
    }

    /// <summary>
    /// 아직 굴러가는 연출이 있나 — 행동 1 이 이것을 기다린다.
    /// </summary>
    /// <remarks>
    /// 덮기(900)는 넣지 않는다. 덮은 채로 두는 것이 제 일이라 영영 안 끝나고, 덮는 데 걸리는 틱은 900 이 따로 재운다.
    /// </remarks>
    private bool FieldBusy() =>
        _fieldWipe != null || _fieldCamMove != null
        || _fieldActors.Any(w => w.Walk != null || w.Fade != null)
        || _fieldProps.Any(p => p.Move != null || p.PlayUntil > _lastTime);

    /// <summary>
    /// 굴러가는 연출을 전부 끝난 자리로 보낸다 — Esc 건너뛰기. 걷기·자리 옮기기는 목적지로, 밝기는 목표값으로,
    /// 카메라는 목표 자리로, 걷어내기 전환은 끝으로, 덮기(900)는 덮은 채로 둔다(걷는 중이면 걷어 낸다).
    /// </summary>
    private void FinishFieldAnimations()
    {
        foreach (var actor in _fieldActors)
        {
            if (actor.Walk is { } walk)
            {
                (actor.X, actor.Y, actor.Motion, actor.Mirror, actor.Walk) = (walk.ToX, walk.ToY, walk.EndMotion, walk.EndMirror, null);
            }
            if (actor.Fade is { } fade)
            {
                (actor.Alpha, actor.Visible, actor.Fade) = (fade.To, fade.To > 0, null);
            }
        }
        foreach (var prop in _fieldProps)
        {
            if (prop.Move is { } move) (prop.X, prop.Y, prop.Move) = (move.ToX, move.ToY, null);
            prop.PlayUntil = 0;                           // 모션은 그대로 두고 기다림만 푼다
        }
        if (_fieldCamMove is { } cam) { MoveFieldCamera((int)cam.ToX, (int)cam.ToY); _fieldCamMove = null; }
        _fieldWipe = null;                                   // 걷어내기 전환은 끝(원본도 끝나면 그림만 남긴다)
        if (_fieldFade is { } fade2)
            _fieldFade = fade2.CoverTicks > 0 ? (_lastTime - 1000, fade2.CoverTicks, fade2.UncoverTicks, fade2.White) : null;
    }

    /// <summary>행동 500·504 가 기다리는 소리 채널 — −1 이면 안 기다리는 중.</summary>
    private int _fieldWaitChannel = -1;

    /// <summary>행동 500 이 쓰는 채널 — 자료가 501 에 쓰는 번호(1·2)와 겹치지 않게 따로 둔다.</summary>
    private const int FieldVoiceChannel = 900;

    /// <summary>행동 1 이 한 줄에서 머문 시각 — 0 이면 안 머무는 중.</summary>
    private double _fieldHoldSince;

    /// <summary>
    /// 행동 1 이 한 줄에서 참아 주는 시간(초) — <c>DUELDX_HOLD=&lt;초&gt;</c> 로 줄일 수 있다(화면 밖 시험용).
    /// </summary>
    /// <remarks>원본에는 이런 참을성이 없다 — 데모가 아직 못 끝내는 연출에 갇히지 않으려고 둔 안전장치다.</remarks>
    private static readonly double FieldHoldSeconds =
        double.TryParse(Environment.GetEnvironmentVariable("DUELDX_HOLD"), out double hold) && hold > 0 ? hold : 10;

    /// <summary>행동 하나. 이 틀에 더 읽지 말아야 하면 false(필드를 떠났거나, 연출을 기다린다).</summary>
    /// <summary>
    /// 지금 읽은 행동 2(틱 기다리기)가 <b>대사와 대사 사이</b>의 멈춤인가 — 「대사(600~603) → 1(클릭 기다림) → 2 → 대사」.
    /// 원본도 클릭 뒤 이 틱만큼(보통 30틱 = 1초) 배경만 보이다 다음 대사를 띄운다(<c>0x100f4946</c> 이 클릭 뒤에야 셈을 0 부터 시작).
    /// </summary>
    private bool IsPauseBetweenLines()
    {
        var events = _field?.Events ?? (_mosesOpen ? _mosesChp?.Events : null);
        if (events == null || (uint)_fieldEvent >= events.Count) return false;
        var acts = events[_fieldEvent].Actions;
        int at = _fieldPc - 1;                                 // 방금 읽은 행동 2
        static bool Talk(int code) => code is >= 600 and <= 603;
        if (at < 2 || at + 1 >= acts.Count || acts[at - 1].Code != 1 || !Talk(acts[at + 1].Code)) return false;
        int before = at - 2;
        while (before > 0 && acts[before].Code == 1000) before--;   // 1000(건너뛰기 깃발 내림)은 화면에 아무것도 안 한다
        return Talk(acts[before].Code);
    }

    private bool RunFieldAction(ScriptCommand a)
    {
        short A(int i) => i < a.Args.Length ? a.Args[i] : (short)0;
        // DUELDX_TRACE=1 이면 어느 줄을 읽었는지 남긴다 — 화면 밖 시험에서 스크립트가 어디서 멈췄는지 보려고.
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                               $"fld {(_field?.Id ?? _mosesChp?.Id ?? 0)} ev {_fieldEvent} pc {_fieldPc - 1}: {a.Code} [{string.Join(", ", a.Args)}]" + Environment.NewLine);
        switch (a.Code)
        {
            case 0:
            {
                // 다른 이벤트 부르기 — 목록(이벤트 0) <b>밖에 있는 사건</b>들이 이렇게만 불린다.
                // 부르는 자리를 쌓아 두고 갈아탔다가, 그 사건이 끝나면 돌아온다. 조건·최대 발동은 부를 때 본다.
                int index = A(0);
                var chapter0 = _field == null && _mosesOpen ? _mosesChp : null;
                var events0 = _field?.Events ?? chapter0?.Events;
                if (events0 == null || (uint)index >= events0.Count || index == 0) break;
                var target = events0[index];
                int fired0 = chapter0 != null ? _chapterFired.GetValueOrDefault((chapter0.Id, index)) : _fieldFired[index];
                if (target.MaxFire > 0 && fired0 >= target.MaxFire) break;
                if (!target.Conditions.All(FieldCondition)) break;
                if (_fieldReturn.Count >= 16) break;          // 서로 부르며 도는 자료를 막는다
                if (chapter0 != null) _chapterFired[(chapter0.Id, index)] = fired0 + 1; else _fieldFired[index]++;
                _fieldReturn.Push((_fieldEvent, _fieldPc));
                _fieldEvent = index;
                _fieldPc = 0;
                break;
            }
            case 1:
            {
                // 앞줄이 띄운 것이 끝나기를 기다린다 — 대사(600·601)뿐 아니라 <b>걷기·모션·카메라·전환</b>도 기다린다.
                // 자료에서 이 행동 바로 앞에 놓인 것은 600·601 다음으로 302·208·900·202·517 차례다.
                if (_talkSkip) FinishFieldAnimations();       // 건너뛰는 중이면 연출을 끝자리로 보내고 지나간다
                if (!FieldBusy()) { _fieldHoldSince = 0; break; }
                // 안 끝나는 연출에 갇히지 않게, 한 줄에서 오래 머물면 그냥 다음 줄로 간다.
                if (_fieldHoldSince <= 0) _fieldHoldSince = _lastTime;
                else if (_lastTime - _fieldHoldSince > FieldHoldSeconds)
                {
                    // 여기 걸렸다는 것은 데모가 못 끝내는 연출이 있다는 뜻이다 — 화면 밖 감사에서 찾으려고 남긴다.
                    if (Trace)
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                                           $"HOLD fld {(_field?.Id ?? _mosesChp?.Id ?? 0)} ev {_fieldEvent} pc {_fieldPc - 1}"
                                           + $" (wipe {_fieldWipe != null}, cam {_fieldCamMove != null},"
                                           + $" walk {_fieldActors.Count(w => w.Walk != null)}, fade {_fieldActors.Count(w => w.Fade != null)},"
                                           + $" prop {_fieldProps.Count(pr => pr.Move != null)})" + Environment.NewLine);
                    _fieldHoldSince = 0;
                    break;
                }
                _fieldPc--;                                  // 다음 틀에 이 줄을 다시 본다
                return false;
            }
            case 2:
                // 대사 사이의 멈춤은 설정한 초만큼만(음수면 스크립트 값 그대로 — 원본).
                _fieldWaitUntil = _lastTime + (_talkPauseSeconds >= 0 && IsPauseBetweenLines()
                                                   ? Math.Min(_talkPauseSeconds, A(0) / TicksPerSecond)
                                                   : A(0) / TicksPerSecond);
                break;
            case 504:                                        // [채널] 그 채널의 소리가 끝날 때까지(진행기 0x100f489b 가 직접 본다)
                if (_talkSkip) { StopChannelSound(A(0)); break; }
                _fieldWaitChannel = A(0);
                return false;
            case 3: _fieldEvent = -1; _talkSkip = false; break;  // 이 이벤트 접기

            case 6:                                          // 다른 필드로
                if (OpenField(A(0))) return false;
                LeaveField();
                return false;
            case 7: LeaveField(); return false;              // 필드 끝 — 챕터가 있으니 모세스로(0x100f2d90)
            case 11:                                         // 필드 끝 + <b>챕터 끝</b>(0x100f2eb0 → 0x1004e6c0 이 챕터 상태 +0x10 = 1)
                // 모세스는 이 표시를 보고 항행 대신 연대표(장면 7)로 간다 — 분석-모세스 「챕터가 끝나는 조건」.
                _chapterDone = true;
                LeaveField();
                return false;
            case 10:                                         // 전투
                CloseField();
                if (!StartBattle(A(0))) OpenMoses();
                return false;
            case 12: CloseField(); OpenTitle(); return false;
            default: RunChapterAction(a); break;              // 70x·80x(동료·돈·아이템·군단…)는 챕터와 같은 처리

            case 100: ScriptVars[A(0) & 0xFF] = (byte)Math.Clamp((int)A(1), 0, 255); break;
            case 101: ScriptVars[A(0) & 0xFF] = FieldArith(ScriptVars[A(0) & 0xFF], A(1), A(2)); break;
            case 102:
                if (A(0) > 0 && A(0) < _flags.Length) _flags[A(0)] = (byte)Math.Clamp((int)A(1), 0, 255);
                break;
            case 103:
                // 원본은 네 갈래가 모두 마지막으로 떨어져 <b>연산자 번호를 한 번 더 더한다</b>(0x100f30ef) — 그 흠까지 그대로 옮긴다.
                if (A(0) > 0 && A(0) < _flags.Length)
                    _flags[A(0)] = (byte)Math.Clamp(FieldArith(_flags[A(0)], A(1), A(2)) + A(1), 0, 255);
                break;

            case 600:
            case 601:
            case 602: ShowFieldTalk(a.Code == 600, A(0), A(1), pose: A(3), voice: A(2)); break;   // 인자 2 = 음성, 3 = 초상화 표정(모션 2×표정+11)
            case 603: ShowFieldTalk(true, 0, A(0), voice: A(1)); break;      // 말하는 이 없는 글(챕터 스크립트에 38번, 가설) — 인자 1 = 음성
            case 300:                                        // 물체를 그 자리로 즉시
            {
                if (FieldPropOf(A(0)) is not { } prop) break;
                prop.X = A(1);
                prop.Y = A(2);
                break;
            }
            case 301:                                        // 물체를 매 틀 그만큼씩
            {
                if (FieldPropOf(A(0)) is not { } prop) break;
                int ticks = Math.Max(1, (int)A(3));
                prop.Move = (prop.X, prop.Y, prop.X + A(1) * ticks, prop.Y + A(2) * ticks, ticks, _lastTime);
                break;
            }
            case 302:
                // 인자 0 이 10000 이상이면 <b>있는 물체에 모션을 지정</b>하고(전체 302 의 58%),
                // 아니면 그 <c>Obs</c> 를 새 물체로 놓는다 — 새로 놓는 것은 늘 <b>층 7(맨 위)</b> 이다.
                if (A(0) >= 10000)
                {
                    if (FieldPropOf(A(0)) is { } prop)
                    {
                        prop.Motion = A(1);
                        prop.Mirror = A(5) != 0;
                        prop.Start = _lastTime;
                        prop.PlayUntil = _lastTime + (UiFor(prop.Obs)?.MotionLength(prop.Motion) ?? 0) / TicksPerSecond;
                        SchedulePropSounds(prop);   // 문 여닫는 소리(Obs 529 모션 1·2 → 157·158) 같은 모션 소리
                    }
                }
                else if (A(0) > 0)
                    _fieldProps.Add(new FieldProp
                    {
                        Obs = A(0), Motion = A(1), X = A(3), Y = A(4), Layer = 7,
                        Mirror = A(5) != 0, Start = _lastTime,
                    });
                break;
            case 303: break;                                 // 물체 모션 멈추기 — 자료에 한 번도 안 쓴다
            case 304:
            case 305:
            {
                if (FieldPropOf(A(0)) is not { } prop) break;
                prop.Visible = a.Code == 305;
                break;
            }
            case 306:
            {
                if (FieldPropOf(A(0)) is not { } prop) break;
                prop.Mirror = A(1) != 0;
                break;
            }
            case 307:
            {
                if (FieldPropOf(A(0)) is not { } prop) break;
                prop.Layer = A(1);
                break;
            }
            case 202:                                        // 걷기(목적지) — 걷는 동안 걷기 모션, 멈추면 서기 모션
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                var (walk, stand, mirror) = FieldFacing(A(4));
                who.Motion = walk;
                who.Mirror = mirror;
                who.Walk = (who.X, who.Y, A(1), A(2), Math.Max(1, (int)A(3)), _lastTime,
                            A(5) != 0 ? stand : walk, mirror);
                break;
            }
            case 203:                                        // 걷기(매 틀 증분) — 방향에 맞는 모션까지 202 와 같다
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                var (walk, stand, mirror) = FieldFacing(A(4));
                int ticks = Math.Max(1, (int)A(3));
                who.Motion = walk;
                who.Mirror = mirror;
                who.Walk = (who.X, who.Y, who.X + A(1) * ticks, who.Y + A(2) * ticks, ticks, _lastTime,
                            A(5) != 0 ? stand : walk, mirror);
                break;
            }
            case 206:                                        // 자리 옮기기(매 틀 증분) — 모션은 안 건드린다
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                int ticks = Math.Max(1, (int)A(3));
                who.Walk = (who.X, who.Y, who.X + A(1) * ticks, who.Y + A(2) * ticks, ticks, _lastTime,
                            who.Motion, who.Mirror);
                break;
            }
            case 205:                                        // 자리 옮기기 — 모션은 안 건드린다
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                who.Walk = (who.X, who.Y, A(1), A(2), Math.Max(1, (int)A(3)), _lastTime, who.Motion, who.Mirror);
                break;
            }
            case 208:                                        // 모션 지정
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                who.Motion = A(1);
                who.Mirror = A(3) != 0;
                who.MotionStart = _lastTime;
                // 인자2 가 1 이면 되풀이라 바로 다음 줄로 가고, 0 이면 <b>한 바퀴 다 돌 때까지</b> 스크립트가 기다린다.
                if (A(2) != 1 && _db?.Character(who.ChrCode) is { SpriteId: > 0 } pc
                    && UiFor(pc.SpriteId)?.MotionLength(A(1)) is > 0 and var length)
                    _fieldWaitUntil = _lastTime + length / TicksPerSecond;
                break;
            }
            case 209: break;                                 // 모션 멈추기 — 데모는 늘 그 모션을 보이므로 할 일이 없다
            case 210:                                        // 서서히 사라지기
            case 211:                                        // 서서히 나타나기
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                double to = a.Code == 211 ? 1 : 0;
                if (a.Code == 211) who.Visible = true;        // 나타날 때는 먼저 보이게 해 두고 밝기를 올린다
                if (A(1) <= 0) { who.Alpha = to; who.Visible = to > 0; who.Fade = null; break; }
                who.Fade = (who.Alpha, to, A(1), _lastTime);
                break;
            }
            case 212:
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                who.Mirror = A(1) != 0;
                break;
            }
            case 213:
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                who.Layer = A(1);
                break;
            }
            case 400:                                        // 화면을 그 자리로(즉시)
                _fieldCamMove = null;
                MoveFieldCamera(A(0), A(1));
                break;
            case 401:                                        // <b>매 틀</b> (a0,a1)씩 a2 틀 동안 민다 — 한 번이 아니다
            {
                int camTicks = Math.Max(1, (int)A(2));
                _fieldCamMove = (_fieldCam.X, _fieldCam.Y,
                                 _fieldCam.X + A(0) * camTicks, _fieldCam.Y + A(1) * camTicks, camTicks, _lastTime);
                break;
            }
            case 402:                                        // 그 인물이 화면 한가운데 오도록 a1 틀에 걸쳐
            {
                if (FieldActorOf(A(0)) is not { } target) break;
                int camTicks = Math.Max(1, (int)A(1));
                _fieldCamMove = (_fieldCam.X, _fieldCam.Y,
                                 target.X - MosesW / 2.0, target.Y - MosesH / 2.0, camTicks, _lastTime);
                break;
            }
            case 901:                                        // 밀어내기 — a2 는 화면 너비에 더하는 여분 거리다
                BeginFieldWipe(901, A(2), Math.Max(1, (int)A(3)), A(0) == 0, A(1));
                break;
            case 903:                                        // 빗살 지우기 — a2 는 틀 수가 아니라 <b>세로 띠 수</b>다
            {
                int bands = Math.Max(1, (int)A(2));
                BeginFieldWipe(903, bands, MosesW / bands + bands, A(0) == 0, A(1));
                break;
            }
            case 904:                                        // 줄 늘여 쓸기 — a2 방향(0 아래→위, 1 위→아래, 2 오른→왼, 3 왼→오른)
                BeginFieldWipe(904, A(2), Math.Max(1, (int)A(3)), A(0) == 0, A(1));
                break;
            case 404:
            case 405: break;                                 // 전환용 그림 미리 얹기·버리기 — 전환이 제 그림을 직접 읽으므로 안 쓴다
            case 902: break;                                 // 동영상(<c>Mov\%04d.mov</c>) — 게임 쪽 영상만 405MB 라 데모는 안 묶는다
            case 905:                                        // 뜻이 아직 가설인 전환들 — 자료에 한 번씩뿐이다.
            case 907:                                        // 인자 자리(a0 방향 · a1 그림)는 다 같으니 <b>겹쳐 디졸브로 갈음</b>한다.
            case 908:                                        // 걸리는 틀은 909 자리(a2)로 읽는다 — 제 자리는 저마다 다르다.
            case 909:                                        // 겹쳐 디졸브 — 전환의 대부분이 이것이다
                // a0 이 방향이다. 0 이면 그 그림으로 갈아타고 <b>그림이 그대로 남는다</b>.
                // 0 이 아니면 그림에서 <b>이 필드 화면으로 돌아오며 그림을 걷는다</b> — 909 27번·903 5번·904 1번·905 1번이 이쪽이다.
                if (A(0) == 0)
                {
                    if (A(1) > 0)
                    {
                        ShowMosesBackground(A(1));
                        _fieldCam = (0, 0);
                        _fieldCamMove = null;
                    }
                }
                else if (_field is { } back)
                    ShowMosesBackground(back.Background, _fieldCam.X, _fieldCam.Y);
                if (A(2) > 0) _fieldWaitUntil = _lastTime + A(2) / TicksPerSecond;
                break;
            case 407:
            case 408:                                        // 층 감추기·보이기
                foreach (var layerProp in _fieldProps.Where(o => o.Layer == A(0))) layerProp.Visible = a.Code == 408;
                foreach (var layerWho in _fieldActors.Where(w => w.Layer == A(0))) layerWho.Visible = a.Code == 408;
                break;
            case 900:
                _fieldFade = (_lastTime, A(2), A(3), A(1) == 0);
                _fieldFadeCover = Math.Clamp((int)A(4), 0, 8);   // 가리는 층 수 — 8 이면 다 가린다
                // 덮는 데 걸리는 틱만큼은 스크립트도 기다린다 — 안 그러면 화면이 덮이기 전에 다음 장면으로 넘어간다.
                if (A(2) > 0) _fieldWaitUntil = _lastTime + A(2) / TicksPerSecond;
                break;
            case 512:                                        // BGM 바꾸기
                _mixer.StopMusic();
                if (A(0) > 0) PlayMusicFile(A(0), loop: true);
                break;
            case 517:                                        // 음량을 인자0(0~100)까지 인자1 틱에 걸쳐
                FadeMusic(A(0), A(1));
                break;
            case 609:                                        // [인물, 이름 TXR, 챕터 Tlc 글, ?] 이름과 글을 <b>다른 표</b>에서 꺼내는 대사(0x100efb30)
                // 원본은 0x1004a3f0(TXR)으로 이름을, 0x1004a650(Tlk\NNNN.Tlc = EvtText)으로 글을 읽어
                // 0x160바이트 창을 [0x101bfe34] 에 만든다 — 600~603 의 대사창([0x101bfe30])과 다른 자리이고 틀은 (164,120)~(313,239).
                // 데모는 창 생김새까지는 안 옮기고 <b>보통 대사창</b>으로 띄운다(가설) — 이름이 인물 이름이 아닌 것만 지킨다.
                ShowFieldTalk(box: true, A(0), 0,
                              nameOverride: _db?.T((ushort)A(1)) ?? "",
                              textOverride: TalkTableFor()?[A(2)] ?? "", voice: A(3));
                break;
            case 514:                                        // 배경음악 멈추기(0x100eee30 → 음악 개체의 0x10024fb0)
                _mixer.StopMusic();
                break;
            case 500:                                        // 소리 한 번 내고 끝날 때까지 기다린다(0x100ee7b0) — 인자1 은 매달 인물
                if (_talkSkip) break;                        // 건너뛰는 중이면 원본도 소리를 안 낸다([0x101bffb0] 검사)
                PlayChannelSound(FieldVoiceChannel, A(0), loop: false);
                _fieldWaitChannel = FieldVoiceChannel;
                return false;
            case 501:                                        // [소리, 채널, 인물, 되풀이] 채널에 걸고 기다리지 않는다(0x100ee960)
                if (_talkSkip) break;
                PlayChannelSound(A(1), A(0), loop: A(3) != 0);
                break;
            case 506:                                        // [채널, 음량, 틱] 채널 음량을 서서히 바꾼다(0x100eeb80) — 517 의 채널판
                if (_talkSkip) break;                        // 건너뛰는 중이면 원본도 건너뛴다([0x101bffb0] 검사)
                FadeChannelSound(A(0), A(1), A(2));
                _fieldWaitUntil = _lastTime + Math.Max(1, (int)A(2)) / TicksPerSecond;   // 원본은 그 틱만큼 줄에 머문다
                break;
            case 505:                                        // [채널] 그 채널의 소리를 끊는다(0x100eeb30)
                StopChannelSound(A(0));
                if (_fieldWaitChannel == A(0)) _fieldWaitChannel = -1;
                break;
            case 1000:                                       // 건너뛰기 끝(0x100f2c20 — [0x101bffb0] = 0)
                _talkSkip = false;                           // 여기서부터는 기다림을 다시 지킨다
                break;
            case 604: BeginFieldChoice(A(0), A(1), A(2)); break;
            case 605: _fieldChoices?.Add(FieldText(A(0))); break;
        }
        return true;
    }

    /// <summary>
    /// 챕터에 들어갈 때 그 챕터 스크립트를 한 번 돌린다 — <b>동료·돈·아이템·진행 깃발</b>이 여기서 들어온다.
    /// </summary>
    /// <remarks>
    /// 스크립트 꼴이 필드와 같아 같은 실행기를 쓴다. 다만 챕터 스크립트는 연출이 아니라 <b>세팅</b>이라
    /// 기다림 없이 한 번에 훑고, 대사(600·601)는 건너뛴다.
    /// <c>Chp 0010</c> 이라면 깃발 107·113·116·117·118·14 를 세우고 동료 둘(219·221)과 3000GP,
    /// 아이템 122×10 · 124×3 · 125×10 · 84×2 · 126×1 을 준다.
    /// </remarks>
    private void RunChapterScript(ChapterFile chapter)
    {
        // 사건별 횟수가 없던 옛 세이브에서 온 챕터는 「다 돌았다」로 본다(사건 −1 표시).
        if (_chapterFired.ContainsKey((chapter.Id, -1))) return;
        foreach (var wanted in chapter.Events.Count > 0 ? chapter.Events[0].Actions : [])
        {
            int index = wanted.Args.Length > 0 ? wanted.Args[0] : -1;
            if ((uint)index >= chapter.Events.Count || index == 0) continue;
            var e = chapter.Events[index];
            if (e.MaxFire > 0 && _chapterFired.GetValueOrDefault((chapter.Id, index)) >= e.MaxFire) continue;
            // 대사·고르기·기다림이 든 사건은 여기서 안 돌고 실행기(UpdateField)가 모세스 위에서 돈다.
            // 조건보다 <b>먼저</b> 거른다 — 조건 505(행성 방문)는 보는 순간 표시를 지우므로, 여기서 보고 건너뛰면
            // 실행기가 볼 때는 이미 지워져 그 사건이 영영 안 돈다(0011 사건 14 등 11개, 분석-모세스 mo-mail).
            if (e.Actions.Any(a => a.Code is 0 or 1 or 2 or 600 or 601 or 602 or 603 or 604 or 605 or 609)) continue;
            if (!e.Conditions.All(FieldCondition)) continue;
            _chapterFired[(chapter.Id, index)] = _chapterFired.GetValueOrDefault((chapter.Id, index)) + 1;
            foreach (var a in e.Actions) RunChapterAction(a);
        }
    }

    /// <summary>챕터 사건이 이미 돈 횟수 — (챕터, 사건).</summary>
    /// <remarks>
    /// 챕터 이벤트는 대부분 <b>진행 깃발로 잠겨</b> 있다 — <c>Chp 0010</c> 은 조건 없는 이벤트 2 가 첫 동료·돈·아이템을 주고,
    /// 이벤트 1·4 는 깃발 5 가 1 이어야, 이벤트 3 은 깃발 7 이 2 여야 돈다.
    /// 그러니 챕터에 처음 들어갈 때 한 번이 아니라 <b>항행 화면에 올 때마다</b> 훑어야 뒤의 동료가 들어온다.
    /// 대신 사건마다 제 「몇 번까지」를 지켜 같은 것을 두 번 주지 않는다.
    /// </remarks>
    private readonly Dictionary<(int Chapter, int Event), int> _chapterFired = [];

    private void RunChapterAction(ScriptCommand a)
    {
        short A(int i) => i < a.Args.Length ? a.Args[i] : (short)0;
        switch (a.Code)
        {
            case 102:
                if (A(0) > 0 && A(0) < _flags.Length) _flags[A(0)] = (byte)Math.Clamp((int)A(1), 0, 255);
                break;
            case 103:
                if (A(0) > 0 && A(0) < _flags.Length)
                    _flags[A(0)] = (byte)Math.Clamp(FieldArith(_flags[A(0)], A(1), A(2)) + A(1), 0, 255);
                break;
            case 703: AddItem(A(0), A(1), Math.Max(1, (int)A(2))); break;   // [파티, 아이템, 개수]
            case 705: AddMoney(A(0), A(1)); break;                           // [파티, 돈] — 파티 객체 +0x10c
            case 701:                                            // 인물 레코드 칸 고치기 [Chr, 칸, 값] (0x100efdf0)
                // 칸: 0 그림 Obs(+0xc) · 1 초상화(+0xe) · 2 이름 TXR(+6) · 3 +0xa · 4 +0x10 · 5 체질(+0x12) · 6 직업(+0x16) ·
                // 9~15 장비 칸 0~6(+0x4c~) · 16 WEAPON 띠(+0x48). Chp 0011 은 살라딘·죠안의 그림을 347·338 로, 살라딘 띠를 49 로 놓는다.
                UpdateCharacter(A(0), c => A(1) switch
                {
                    0 => c with { SpriteId = (ushort)A(2) },
                    1 => c with { FaceId = (ushort)A(2) },
                    2 => c with { NameId = (ushort)A(2) },
                    3 => c with { Name2Id = (ushort)A(2) },
                    4 => c with { TitleId = (ushort)A(2) },
                    5 => c with { Body = (byte)A(2) },
                    6 => c with { JobId = (ushort)A(2) },
                    >= 9 and <= 15 when A(1) - 9 < c.Items.Length => c with { Items = [.. c.Items.Select((it, k) => k == A(1) - 9 ? (ushort)A(2) : it)] },
                    16 => c with { WeaponBand = (byte)A(2) },
                    _ => c,
                });
                break;
            case 702:                                            // 능력치 셈 [Chr, 칸, 값, 연산] (0x100f01b0) — 칸 0 레벨(+0x2c) · 1 LP(+0x38) · 2 PSY(+0x3c) · 3 TP(+0x3e) · 5 DEP(+0x44) · 6 DEX(+0x46), 연산 0 + · 1 − · 2 × · 3 ÷
            {
                int Calc(int v) => A(3) switch { 1 => v - A(2), 2 => v * A(2), 3 => A(2) == 0 ? v : v / A(2), _ => v + A(2) };
                UpdateCharacter(A(0), c => A(1) switch
                {
                    0 => c with { Level = (ushort)Math.Clamp(Calc(c.Level), 1, 99) },
                    1 => c with { Lp = (uint)Math.Max(1, Calc((int)c.Lp)) },
                    2 => c with { Psy = (ushort)Math.Max(0, Calc(c.Psy)) },
                    3 => c with { Tp = (ushort)Math.Max(0, Calc(c.Tp)) },
                    5 => c with { Dep = (ushort)Math.Max(0, Calc(c.Dep)) },
                    6 => c with { Dex = (ushort)Math.Max(0, Calc(c.Dex)) },
                    _ => c,                                      // 4(+0x40)는 뜻을 몰라 둔다
                });
                break;
            }
            case 704:                                            // 어빌리티 배우기 [Chr, 어빌리티] — 없거나 0 이면 레벨 1 로(0x100f06d0, CChr+0x7a)
                UpdateCharacter(A(0), c => c.Abilities.Any(ab => ab.Ability == A(1) && ab.Level > 0) ? c
                    : c with { Abilities = [.. c.Abilities.Where(ab => ab.Ability != A(1)), ((ushort)A(1), (ushort)1)] });
                break;
            case 805:                                            // 레벨 맞추기 [Chr, Δ] — 파티 레벨(상위 셋 평균, 0x1004e070) + Δ 로(0x100f0b40 → 0x10031a50)
                if (_db is { } db805)
                    UpdateCharacter(A(0), c => GrowToPartyLevel(db805.Character(A(0)) ?? c, A(1), PartyLevel()) with
                    {
                        Items = c.Items, Passives = c.Passives, Abilities = c.Abilities,
                        // 원본 0x10031a50 은 레벨·누적 EXP·능력치만 쓴다 — 직업(세부 체질)·체질·남은 EXP 는 그대로 둔다.
                        // 전에는 .chr 새 자료로 덮어 전직한 형이 일반형으로 돌아갔다(사용자 보고 「불러오면 맨날 일반형」).
                        JobId = c.JobId, Body = c.Body, Exp = c.Exp,
                    });
                break;
            case 713:                                            // 군단 얻기 [군단] — 파티 군단 목록에 넣는다(0x100f0810 → 0x1004df50)
                if (A(0) > 0) { _ownedLegions.Add(A(0)); _legionsKnown = true; }
                break;
            case 801: AddMember(A(0), A(1)); break;              // 동료 넣기 [파티, Chr] — 다음 전투부터 파티에 든다
            case 802: RemoveMember(A(0), A(1)); break;           // 동료 빼기 [파티, Chr]
            case 803: MergeParties(A(0), A(1)); break;           // 파티 합치기 [A, B] (0x100f0940, 가설)
            case 804:                                            // 인물 옮기기 [Chr, 파티A → 파티B] (0x100f0ad0: 0x1004de10 빼고 0x1004ddd0 넣기)
                if (A(0) > 0) AddMember(A(2), A(0), RemoveMember(A(1), A(0)));
                break;
        }
    }

    /// <summary>스크립트가 인물 레코드를 고칠 때 — 파티 자료와 (있으면) 전투 판의 유닛 둘 다 고친다. 파티에 없으면 .chr 에서 만들어 넣는다.</summary>
    private void UpdateCharacter(int chr, Func<CharacterData, CharacterData> change)
    {
        if (chr <= 0) return;
        var data = _units.FirstOrDefault(u => u.ChrCode == chr)?.Data ?? _party.GetValueOrDefault(chr) ?? _db?.Character(chr);
        if (data == null) return;
        var changed = change(data);
        _party[chr] = changed;
        foreach (var u in _units) if (u.ChrCode == chr) u.Data = changed;
    }

    /// <summary>그림 하나를 놓는다 — 물체 열쇠를 가리키면 파일에 적힌 그림과 자리를 쓴다.</summary>
    private void PlaceFieldPicture(int what, int motion, int x, int y)
    {
        if (what >= 10000)
        {
            if (_field?.Objects.FirstOrDefault(o => o.Key == what - 10000) is not { } target) return;
            _fieldPictures.Add((target.Picture, motion, target.X, target.Y, _lastTime));
            return;
        }
        if (what > 0) _fieldPictures.Add((what, motion, x, y, _lastTime));
    }

    /// <summary>화면을 옮긴다 — 배경도 그 자리부터 다시 잘라 온다.</summary>
    private void MoveFieldCamera(int x, int y)
    {
        _fieldCam = (Math.Max(0, x), Math.Max(0, y));
        if (_field is { } field) ShowMosesBackground(field.Background, _fieldCam.X, _fieldCam.Y);
    }

    /// <summary>대상 지정 값(<c>10000+열쇠</c>)이 가리키는 물체.</summary>
    private FieldProp? FieldPropOf(int value) =>
        value >= 10000 ? _fieldProps.FirstOrDefault(o => o.Key == value - 10000) : null;

    /// <summary>대상 지정 값(<c>10000+열쇠</c>)이 가리키는 인물.</summary>
    private FieldActor? FieldActorOf(int value) =>
        value >= 10000 ? _fieldActors.FirstOrDefault(a => a.Key == value - 10000) : null;

    /// <summary>걷는 중인 인물을 한 걸음 옮긴다 — 매 틱 선형 보간이다.</summary>
    private void StepFieldActors()
    {
        foreach (var actor in _fieldActors)
        {
            if (actor.Walk is not { } walk) continue;
            int tick = (int)((_lastTime - walk.Start) * TicksPerSecond);
            if (tick >= walk.Ticks)
            {
                actor.X = walk.ToX;
                actor.Y = walk.ToY;
                actor.Motion = walk.EndMotion;
                actor.Mirror = walk.EndMirror;
                actor.Walk = null;
                continue;
            }
            actor.X = walk.FromX + (walk.ToX - walk.FromX) * tick / walk.Ticks;
            actor.Y = walk.FromY + (walk.ToY - walk.FromY) * tick / walk.Ticks;
        }

        if (_fieldCamMove is { } cam)
        {
            int camTick = (int)((_lastTime - cam.Start) * TicksPerSecond);
            if (camTick >= cam.Ticks)
            {
                MoveFieldCamera((int)cam.ToX, (int)cam.ToY);
                _fieldCamMove = null;
            }
            else
                MoveFieldCamera((int)(cam.FromX + (cam.ToX - cam.FromX) * camTick / cam.Ticks),
                                (int)(cam.FromY + (cam.ToY - cam.FromY) * camTick / cam.Ticks));
        }

        foreach (var prop in _fieldProps)
        {
            if (prop.Move is not { } move) continue;
            int tick = (int)((_lastTime - move.Start) * TicksPerSecond);
            if (tick >= move.Ticks)
            {
                prop.X = move.ToX;
                prop.Y = move.ToY;
                prop.Move = null;
                continue;
            }
            prop.X = move.FromX + (move.ToX - move.FromX) * tick / move.Ticks;
            prop.Y = move.FromY + (move.ToY - move.FromY) * tick / move.Ticks;
        }

        foreach (var actor in _fieldActors)
        {
            if (actor.Fade is not { } fade) continue;
            int tick = (int)((_lastTime - fade.Start) * TicksPerSecond);
            if (tick >= fade.Ticks)
            {
                actor.Alpha = fade.To;
                actor.Visible = fade.To > 0;
                actor.Fade = null;
                continue;
            }
            // 원본은 밝기 바이트를 8→1 로 내린다 — 8단계로 끊어 그 느낌을 맞춘다.
            double t = (double)tick / fade.Ticks;
            actor.Alpha = Math.Round((fade.From + (fade.To - fade.From) * t) * 8) / 8;
        }
    }

    private static byte FieldArith(byte now, int op, int value) => (byte)Math.Clamp(op switch
    {
        0 => now + value,
        1 => now - value,
        2 => now * value,
        _ => value != 0 ? now / value : now,
    }, 0, 255);

    private string FieldText(int id) => (_field != null ? _fieldTalk : TalkTableFor())?[id] ?? "";

    /// <summary>스크립트 변수 — 필드가 떠 있으면 그 필드의 것(필드마다 비운다), 아니면 챕터 것(원본 챕터 상태 <c>+0x88</c>, 세이브에 실린다).</summary>
    private byte[] ScriptVars => _field != null ? _fieldVars : _chapterVars;

    /// <summary>챕터 스크립트의 변수 256칸 — 고르기(604)의 답이 여기 들어가고 뒤 사건(조건 100)이 읽는다.</summary>
    private readonly byte[] _chapterVars = new byte[256];

    /// <summary>필드에 나오는 인물의 초상화 — 전투 인물과 달리 뽑아 둔 폴더가 없어 <c>.chr</c> 의 얼굴 Obs 를 바로 읽는다.</summary>
    private void LoadFieldFace(CharacterData c)
    {
        if (_faces.ContainsKey(c.Code) || c.FaceId == 0) return;
        try
        {
            string path = Path.Combine(AssetsFolder.Find("moses"), "obs", $"{c.FaceId:D4}.obs");
            if (File.Exists(path) && DecodeFaceFrame(path) is { } face) _faces[c.Code] = SpriteFrame.From(face);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { }
    }

    /// <summary>
    /// 필드 대사 — 말하는 이는 <c>10000+인물 열쇠</c> 다. 이름과 초상화는 그 인물의 <c>.chr</c> 에서 온다.
    /// </summary>
    /// <param name="nameOverride">말하는 이 자리에 넣을 이름 — 행동 609 는 인물 이름이 아니라 <b>인자1 의 TXR 이름</b>을 쓴다.</param>
    /// <param name="textOverride">글 — 행동 609 는 필드 <c>Tlf</c> 가 아니라 <b>그 챕터의 <c>Tlc</c></b> 에서 꺼낸다.</param>
    private void ShowFieldTalk(bool box, int speaker, int textId, string? nameOverride = null, string? textOverride = null, int pose = 0, int voice = 0)
    {
        if (_talkSkip) return;
        PlayTalkVoice(voice);
        string name = "";
        _fieldTalkOf = speaker;
        if (_field is { } field && speaker >= 10000
            && field.People.FirstOrDefault(p => p.Key == speaker - 10000) is { } person
            && _db?.Character(person.ChrCode) is { } c)
        {
            name = _db.T(c.NameId);
            LoadFieldFace(c);
            _talkFace = c.Code;
        }
        else if (_field == null && speaker > 0 && speaker < 10000 && _db?.Character(speaker) is { } cc)
        {
            // 챕터 스크립트의 600 [Chr, 글] — 말하는 이가 Chr 번호다(필드는 10000+열쇠).
            name = _db.T(cc.NameId);
            LoadFieldFace(cc);
            _talkFace = cc.Code;
        }
        else _talkFace = 0;
        _talk = (box, -1, nameOverride ?? name, textOverride ?? FieldText(textId), pose, _lastTime);
        _talkFilled = false;
    }

    /// <summary>필드에서 지금 말하는 이(<c>10000+열쇠</c>) — 말풍선을 그 머리 위에 띄우려고 들고 있는다.</summary>
    private int _fieldTalkOf;

    /// <summary>말하는 이가 필드 인물이면 그 머리 위 자리(판 낱칸)를 알려 준다.</summary>
    private (int X, int Y)? FieldTalkHead()
    {
        if (!FieldOpen || FieldActorOf(_fieldTalkOf) is not { Visible: true } who) return null;
        var (ox, oy) = MosesOrigin();
        return (ox + (int)who.X - _fieldCam.X, oy + (int)who.Y - _fieldCam.Y);
    }

    /// <summary>고르기 시작(행동 604) — 뒤이은 605 들이 항목을 더한다.</summary>
    private void BeginFieldChoice(int speaker, int textId, int variable)
    {
        _fieldChoices = [FieldText(textId)];
        _fieldChoiceVar = variable & 0xFF;
        _fieldChoicePick = 0;
    }

    /// <summary>고르기 창이 떠 있으면 클릭을 처리하고 true.</summary>
    private bool OnFieldClick(int bx, int by)
    {
        if (_fieldChoices is not { Count: > 0 } choices) return false;
        var (x, y, w, h) = FieldChoiceRect(choices.Count);
        int row = (by - y - 12) / 22;
        if (bx < x || bx >= x + w || row < 0 || row >= choices.Count) return true;
        ScriptVars[_fieldChoiceVar] = (byte)(row + 1);      // 고른 차례는 1부터
        _fieldChoices = null;
        Play(MosesClickSound);
        return true;
    }

    /// <summary>고르기 창 — 640×480 틀 안, 대사 상자 바로 위에 놓는다.</summary>
    /// <summary>고르기 창에서 마우스가 얹힌 줄을 표시한다.</summary>
    private void UpdateFieldHover(int bx, int by)
    {
        if (_fieldChoices is not { Count: > 0 } choices) return;
        var (x, y, w, _) = FieldChoiceRect(choices.Count);
        int row = (by - y - 12) / 22;
        _fieldChoicePick = bx >= x && bx < x + w && row >= 0 && row < choices.Count ? row : -1;
    }

    private (int X, int Y, int W, int H) FieldChoiceRect(int rows)
    {
        var (fx, fy) = MosesOrigin();
        int w = 360, h = rows * 22 + 24;
        return (fx + (MosesW - w) / 2, fy + 360 - h, w, h);
    }

    /// <summary>고르기 창 — 대사 상자 위. 필드와 모세스(챕터 스크립트) 둘 다 쓴다.</summary>
    private void DrawFieldChoices()
    {
        if (_fieldChoices is not { Count: > 0 } choices) return;
        var (x, y, w, h) = FieldChoiceRect(choices.Count);
        DarkenRect(x - 1, y - FrameTitleH - 1, w + 2, h + FrameTitleH + 2, 8);
        DrawGameFrame(x, y, w, h, "");
        for (int i = 0; i < choices.Count; i++)
            DrawText(choices[i], x + 16, y + 14 + i * 22, i == _fieldChoicePick ? 0xFF00FFFF : White, 13);
    }

    private void DrawField()
    {
        if (_field is null) return;
        var (ox, oy) = MosesOrigin();

        FillRect(_camX, _camY, ViewWidth, ViewHeight, 0xFF000000);
        if (_mosesBg is { } bg)
            for (int y = 0; y < MosesH; y++)
                for (int x = 0; x < MosesW; x++)
                    SetPixel(ox + x, oy + y, bg[y * MosesW + x] | 0xFF000000);

        // 필드 화면은 640×480 틀 안이 전부다 — 물체·인물이 그 밖으로 새지 않게 자른다.
        _uiClip = (ox, oy, MosesW, MosesH);

        // 덮기(900)가 가리는 층은 a4 까지다 — 8 이면 다 가리지만 자료의 131번은 그보다 작다.
        // 그 위의 층은 덮개보다 <b>나중에</b> 그려서 안 가려진다.
        int cover = _fieldFade is not null ? _fieldFadeCover : 8;
        DrawFieldLayers(ox, oy, int.MinValue, cover);

        DrawTalk();
        DrawFieldChoices();
        _uiClip = null;
        DrawFieldWipe(ox, oy);
        DrawFieldFade(ox, oy);
        if (cover < 8)
        {
            _uiClip = (ox, oy, MosesW, MosesH);
            DrawFieldLayers(ox, oy, cover, int.MaxValue);
            _uiClip = null;
        }
        DrawToast();
    }

    /// <summary>
    /// 층 <paramref name="from"/> 이상 <paramref name="to"/> 미만의 물체·인물을 층 차례로 그린다.
    /// </summary>
    /// <remarks>
    /// 층 <b>−1 인물은 안 보인다</b>(1174명 중 153명, 82명은 자리도 (−1,−1)) — 원본 로더(<c>0x100eca39</c>)는 층 −1 을
    /// 배경 묶음(<c>+0x128</c>)보다 <b>먼저 만든 층 <c>+0x14c</c></b> 에 넣어 배경 아래에 깔리므로, 대사의 말하는 이로만 쓰인다.
    /// 예: 첫 프롤로그 <c>Fld 0019</c> 의 두 사람(367·472)이 같은 자리에 서 있지만 화면에는 안 나온다.
    /// </remarks>
    /// <summary>물체 모션(과 그 자식 그림)에 박힌 소리를 틱에 맞춰 예약한다 — 인물 동작의 <see cref="ScheduleActionSounds"/> 와 같은 꼴.</summary>
    private void SchedulePropSounds(FieldProp prop)
    {
        if (_talkSkip || UiFor(prop.Obs)?.Clip(prop.Motion) is not { } clip) return;
        foreach (var (start, sound) in clip.Sounds) _pendingSounds.Add((_lastTime + start / TicksPerSecond, sound));
        foreach (var (start, obs, motion, _, _, _, _) in clip.Children)
            if (UiFor(obs)?.Clip(motion) is { } child)
                foreach (var (s, sound) in child.Sounds) _pendingSounds.Add((_lastTime + (start + s) / TicksPerSecond, sound));
    }

    private void DrawFieldLayers(int ox, int oy, int from, int to)
    {
        from = Math.Max(from, 0);
        foreach (var prop in _fieldProps.Where(o => o.Visible && o.Layer >= from && o.Layer < to).OrderBy(o => o.Layer))
        {
            var (px, py) = FieldScreenAt(prop.Layer, prop.X, prop.Y);
            int tick = (int)((_lastTime - prop.Start) * TicksPerSecond);
            // 모션의 섞기 키(종류 3) — 17 은 더하기 합성이다(분석-UI 「섞기 방식 17」). 등불 빛(Obs 528 따위)이 이것이라
            // 보통으로 그리면 검은 원판이 된다(마에라드 프롤로그 Fld 0036).
            int key = UiFor(prop.Obs)?.BlendAt(prop.Motion, tick) ?? 0;
            // 조명(Obs 0876)은 10 닷지·12 스크린(Fld 0058), 1~7 은 반투명 — 돌문(Obs 1233, Fld 0354)이 8→1 로 옅어지며 열린다.
            DrawUi(prop.Obs, prop.Motion, tick, ox + px, oy + py, BlendOf(key), fade: BlendFade(key));
            // 모션에 붙은 자식 그림(키 종류 2) — 문이 열릴 때 번지는 빛(Obs 529 모션 3 → Obs 535, Fld 0037) 같은 것이 여기 있다.
            // 전에는 물체는 자식을 안 그려서 문이 소리 없이 열린 그림으로만 바뀌었다(사용자 보고). 자식은 모션을 건 때부터 한 번 돈다.
            DrawUnitLayers(UiFor(prop.Obs)?.Clip(prop.Motion), tick, ox + px, oy + py, prop.Mirror, loop: false);
        }

        foreach (var actor in _fieldActors.Where(a => a.Visible && a.Layer >= from && a.Layer < to).OrderBy(a => a.Layer))
            if (_db?.Character(actor.ChrCode) is { SpriteId: > 0 } pc)
            {
                var (px, py) = FieldScreenAt(actor.Layer, actor.X, actor.Y);
                int actorTick = (int)((_lastTime - actor.MotionStart) * TicksPerSecond);
                // 몸 모션이 더하기(17)면 그대로 — 전장의 몸 그림과 같은 규칙.
                var actorBlend = UiFor(pc.SpriteId)?.BlendAt(actor.Motion, actorTick) == 17 ? UiBlend.Add : UiBlend.Alpha;
                DrawUi(pc.SpriteId, actor.Motion, actorTick, ox + px, oy + py, actorBlend, loop: true, fade: actor.Alpha);
            }
    }

    private (int X, int Y) FieldScreenAt(int layer, double x, double y) =>
        layer < 0 ? ((int)x, (int)y) : ((int)x - _fieldCam.X, (int)y - _fieldCam.Y);

    /// <summary>
    /// 걷어내는 전환을 건다 — <paramref name="toPicture"/> 면 지금 화면에서 그림으로 가고(그림이 남는다),
    /// 아니면 그림에서 지금 화면으로 돌아온다.
    /// </summary>
    private void BeginFieldWipe(int kind, int way, int ticks, bool toPicture, int background)
    {
        var shot = CaptureFieldScreen();
        var picture = ReadBackground(background) ?? shot;
        _fieldWipe = new FieldWipe(kind, way, ticks, _lastTime, toPicture ? shot : picture, toPicture ? picture : shot);
        _fieldWaitUntil = _lastTime + ticks / TicksPerSecond;
        // 그림으로 갔으면 전환이 끝난 뒤에도 그 그림이 화면에 남아 있어야 한다.
        if (toPicture)
        {
            ShowMosesBackground(background);
            _fieldCam = (0, 0);
            _fieldCamMove = null;
        }
    }

    /// <summary>지금 필드 화면(640×480)을 그대로 찍는다 — 걷어내는 전환이 이 그림에서 시작한다.</summary>
    private uint[] CaptureFieldScreen()
    {
        var (ox, oy) = MosesOrigin();
        var shot = new uint[MosesW * MosesH];
        for (int y = 0; y < MosesH; y++)
            Array.Copy(_fb, (oy + y) * BoardWidth + ox, shot, y * MosesW, MosesW);
        return shot;
    }

    /// <summary>전환이 도는 동안은 화면 전체가 전환 것이다 — 바탕을 깔고 걷힌 만큼 덮는 그림을 올린다.</summary>
    private void DrawFieldWipe(int ox, int oy)
    {
        if (_fieldWipe is not { } wipe) return;
        int tick = (int)((_lastTime - wipe.Start) * TicksPerSecond);
        if (tick >= wipe.Ticks) { _fieldWipe = null; return; }

        switch (wipe.Kind)
        {
            case 901: DrawSlideWipe(ox, oy, wipe, tick); break;
            case 903: DrawCombWipe(ox, oy, wipe, tick); break;
            default: DrawStreakWipe(ox, oy, wipe, tick); break;
        }
    }

    /// <summary>
    /// 901 밀어내기 — 경계선 하나가 <b>왼쪽에서 오른쪽으로만</b> 지나가고, 지난 쪽이 새 그림이다.
    /// 경계선은 <c>640 + a2</c> 까지 가므로 <c>a2</c>(여분 거리)만큼 다 지나고도 더 간다.
    /// </summary>
    private void DrawSlideWipe(int ox, int oy, FieldWipe wipe, int tick)
    {
        int edge = (MosesW + wipe.Way) * tick / Math.Max(1, wipe.Ticks);
        for (int y = 0; y < MosesH; y++)
            for (int x = 0; x < MosesW; x++)
            {
                uint[] from = x < edge ? wipe.Over : wipe.Base ?? wipe.Over;
                SetPixel(ox + x, oy + y, from[y * MosesW + x] | 0xFF000000);
            }
    }

    /// <summary>
    /// 903 빗살 지우기 — 세로 띠 <c>a2</c>개로 나누고, 띠 <c>i</c> 는 <c>t = i+1</c> 부터 한 틀에 1픽셀씩 왼쪽부터 드러난다.
    /// 그래서 걸리는 틀 수가 <c>640/a2 + a2</c> 다(<c>0x1002c4c0</c>).
    /// </summary>
    private void DrawCombWipe(int ox, int oy, FieldWipe wipe, int tick)
    {
        int bands = Math.Max(1, wipe.Way);
        int width = MosesW / bands;
        for (int y = 0; y < MosesH; y++)
            for (int x = 0; x < MosesW; x++)
            {
                int band = Math.Min(bands - 1, x / width);
                int shown = Math.Clamp(tick - band, 0, width);
                uint[] from = x - band * width < shown ? wipe.Over : wipe.Base ?? wipe.Over;
                SetPixel(ox + x, oy + y, from[y * MosesW + x] | 0xFF000000);
            }
    }

    /// <summary>
    /// 904 줄 늘여 쓸기 — 그림 <b>한 장</b>이 한 쪽에서 들어오는데, 아직 안 들어온 쪽은
    /// <b>경계 줄 하나를 늘여</b> 채운다(<c>0x1002a876</c>). 그래서 첫 틀부터 화면 전체가 그 그림으로 덮인다.
    /// </summary>
    private void DrawStreakWipe(int ox, int oy, FieldWipe wipe, int tick)
    {
        bool sideways = wipe.Way is 2 or 3;
        int span = sideways ? MosesW : MosesH;
        int pos = Math.Clamp(span * tick / Math.Max(1, wipe.Ticks), 0, span);
        if (pos <= 0) return;

        for (int y = 0; y < MosesH; y++)
            for (int x = 0; x < MosesW; x++)
            {
                // 드러난 띠는 제자리 그대로, 나머지는 경계 줄을 그대로 되풀이한다.
                int sx = x, sy = y;
                switch (wipe.Way)
                {
                    case 0: sy = Math.Max(y, MosesH - pos); break;   // 아래에서 위로
                    case 1: sy = Math.Min(y, pos - 1); break;        // 위에서 아래로
                    case 2: sx = Math.Max(x, MosesW - pos); break;   // 오른쪽에서 왼쪽으로
                    default: sx = Math.Min(x, pos - 1); break;       // 왼쪽에서 오른쪽으로
                }
                SetPixel(ox + x, oy + y, wipe.Over[sy * MosesW + sx] | 0xFF000000);
            }
    }

    /// <summary>덮기·걷기 — 눈금 0(안 덮임)~31(다 덮임)을 그대로 옮긴다.</summary>
    private void DrawFieldFade(int ox, int oy)
    {
        if (_fieldFade is not { } fade) return;
        int tick = (int)((_lastTime - fade.Start) * TicksPerSecond);
        int level;
        if (fade.CoverTicks > 0) level = Math.Min(31, 31 * tick / fade.CoverTicks);          // 덮는 중
        else if (fade.UncoverTicks > 0) level = Math.Max(0, 31 - 31 * tick / fade.UncoverTicks);  // 걷는 중
        else level = 31;
        if (level <= 0) { _fieldFade = null; return; }

        for (int y = oy; y < oy + MosesH; y++)
            for (int x = ox; x < ox + MosesW; x++)
            {
                uint c = _fb[y * BoardWidth + x];
                uint Ch(int shift)
                {
                    uint v = c >> shift & 0xFF;
                    return fade.White ? v + (255 - v) * (uint)level / 31 : v * (uint)(31 - level) / 31;
                }
                _fb[y * BoardWidth + x] = c & 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
            }
    }
}
