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
    /// 정도 눈금은 0~31. 데모는 무늬를 <b>검정·흰색 두 가지</b>로만 흉내 내고, 층 수는 안 쓴다.
    /// <c>Fld 0019</c> 는 시작에 <c>900 [1,1,0,40,8]</c>(40틱에 걸쳐 걷기), 끝에 <c>900 [0,1,40,0,8]</c>(40틱에 걸쳐 덮기)를 쓴다.
    /// </remarks>
    private (double Start, int CoverTicks, int UncoverTicks, bool White)? _fieldFade;

    /// <summary>전환 직전에 찍어 둔 640×480 화면 — 빗살 지우기·밀어내기가 이 위에서 걷힌다.</summary>
    private uint[]? _fieldShot;

    /// <summary>
    /// 걷어내는 전환(903 빗살 지우기 · 904 밀어내기) — 찍어 둔 옛 화면을 새 화면 위에 덮고 조금씩 걷는다.
    /// </summary>
    private (int Kind, int Way, int Ticks, double Start)? _fieldWipe;

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
            Array.Clear(_fieldVars);
            _fieldEvent = -1;
            _fieldPc = 0;
            _fieldWaitUntil = 0;
            _fieldChoices = null;
            _fieldPictures.Clear();
            _fieldFade = null;
            _fieldShot = null;
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
            return false;
        }
    }

    private void CloseField()
    {
        _field = null;
        _fieldTalk = null;
        _talk = null;
        _fieldChoices = null;
    }

    /// <summary>
    /// 필드 스크립트를 한 걸음 나아가게 한다.
    /// </summary>
    /// <remarks>
    /// 이벤트 <b>0</b> 의 행동 인자 0 이 「살려 둘 이벤트」 목록이다(<c>0x100f49a0</c>) — 그 차례로 조건을 보고 하나를 돌린다.
    /// </remarks>
    private void UpdateField()
    {
        if (_field is not { } field) return;
        StepFieldActors();
        if (_talk != null || _fieldChoices != null) return;          // 대사·고르기가 떠 있으면 기다린다
        if (_fieldWaitUntil > _lastTime) return;

        if (_fieldEvent < 0)
        {
            foreach (var wanted in field.Events.Count > 0 ? field.Events[0].Actions : [])
            {
                int index = wanted.Args.Length > 0 ? wanted.Args[0] : -1;
                if ((uint)index >= field.Events.Count || index == 0) continue;
                var e = field.Events[index];
                if (e.MaxFire > 0 && _fieldFired[index] >= e.MaxFire) continue;
                if (!e.Conditions.All(FieldCondition)) continue;
                _fieldFired[index]++;
                _fieldEvent = index;
                _fieldPc = 0;
                break;
            }
            if (_fieldEvent < 0) { LeaveField(); return; }           // 더 돌 이벤트가 없으면 나간다
        }

        var running = field.Events[_fieldEvent];
        while (_fieldEvent >= 0 && _talk == null && _fieldWaitUntil <= _lastTime)
        {
            if (_fieldPc >= running.Actions.Count) { _fieldEvent = -1; return; }
            // 고르기(604)를 낸 뒤에는 뒤따르는 605 들을 <b>먼저 다 읽어</b> 항목을 채우고, 그다음에 사람을 기다린다.
            if (_fieldChoices != null && running.Actions[_fieldPc].Code != 605) return;
            if (!RunFieldAction(running.Actions[_fieldPc++])) return;  // false = 필드를 떠났다
        }
    }

    /// <summary>나갈 길이 없는 필드(찌꺼기)는 그냥 모세스로 돌아간다.</summary>
    private void LeaveField()
    {
        CloseField();
        OpenMoses();
    }

    private bool FieldCondition(ScriptCommand c)
    {
        short A(int i) => i < c.Args.Length ? c.Args[i] : (short)0;
        return c.Code switch
        {
            0 => true,                                                          // 언제나
            100 => Compare(_fieldVars[A(0) & 0xFF], A(1), A(2)),                // 필드 변수
            101 => Compare(A(0) >= 0 && A(0) < _flags.Length ? _flags[A(0)] : 0, A(1), A(2)),
            _ => false,                                                         // 안 만든 조건은 안 터뜨린다
        };
    }

    /// <summary>행동 하나. 필드를 떠났으면 false.</summary>
    private bool RunFieldAction(ScriptCommand a)
    {
        short A(int i) => i < a.Args.Length ? a.Args[i] : (short)0;
        switch (a.Code)
        {
            case 0:                                          // 다른 이벤트 부르기 — 이벤트 0 목록이 이미 돌리므로 넘긴다
            case 1: break;                                   // 띄운 것이 끝나기를 기다림 — 데모는 대사마다 이미 멈춘다
            case 2: _fieldWaitUntil = _lastTime + A(0) / TicksPerSecond; break;
            case 3: _fieldEvent = -1; break;                 // 이 이벤트 접기

            case 6:                                          // 다른 필드로
                if (OpenField(A(0))) return false;
                LeaveField();
                return false;
            case 7:
            case 11: LeaveField(); return false;             // 필드 끝 — 챕터가 있으니 모세스로
            case 10:                                         // 전투
                CloseField();
                if (!StartBattle(A(0))) OpenMoses();
                return false;
            case 12: CloseField(); OpenTitle(); return false;

            case 100: _fieldVars[A(0) & 0xFF] = (byte)Math.Clamp((int)A(1), 0, 255); break;
            case 101: _fieldVars[A(0) & 0xFF] = FieldArith(_fieldVars[A(0) & 0xFF], A(1), A(2)); break;
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
            case 602: ShowFieldTalk(a.Code == 600, A(0), A(1)); break;
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
            case 404:
            case 405: break;                                 // 전환용 그림 미리 얹기·버리기 — 909 가 제 그림을 직접 읽으므로 안 쓴다
            case 909:                                        // 겹쳐 디졸브 — 전환의 대부분이 이것이다
                // a0 이 0 이 아니면 앞뒤를 바꾼다. 데모는 <b>새 배경으로 갈아타는 것</b>만 흉내 낸다.
                if (A(1) > 0)
                {
                    ShowMosesBackground(A(1));
                    _fieldCam = (0, 0);
                    _fieldCamMove = null;
                }
                if (A(2) > 0) _fieldWaitUntil = _lastTime + A(2) / TicksPerSecond;
                break;
            case 407:
            case 408:                                        // 층 감추기·보이기
                foreach (var layerProp in _fieldProps.Where(o => o.Layer == A(0))) layerProp.Visible = a.Code == 408;
                foreach (var layerWho in _fieldActors.Where(w => w.Layer == A(0))) layerWho.Visible = a.Code == 408;
                break;
            case 900:
                _fieldFade = (_lastTime, A(2), A(3), A(1) == 0);
                // 덮는 데 걸리는 틱만큼은 스크립트도 기다린다 — 안 그러면 화면이 덮이기 전에 다음 장면으로 넘어간다.
                if (A(2) > 0) _fieldWaitUntil = _lastTime + A(2) / TicksPerSecond;
                break;
            case 512:                                        // BGM 바꾸기
                _mixer.StopMusic();
                if (A(0) > 0) PlayMusicFile(A(0), loop: true);
                break;
            case 517:                                        // 음량을 인자0 까지 인자1 틱에 걸쳐 — 데모는 0 이면 끄기만 한다
                if (A(0) <= 0) _mixer.StopMusic();
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
        if (!_chapterScriptDone.Add(chapter.Id)) return;
        foreach (var wanted in chapter.Events.Count > 0 ? chapter.Events[0].Actions : [])
        {
            int index = wanted.Args.Length > 0 ? wanted.Args[0] : -1;
            if ((uint)index >= chapter.Events.Count || index == 0) continue;
            var e = chapter.Events[index];
            if (!e.Conditions.All(FieldCondition)) continue;
            foreach (var a in e.Actions) RunChapterAction(a);
        }
    }

    /// <summary>스크립트를 이미 돌린 챕터.</summary>
    private readonly HashSet<int> _chapterScriptDone = [];

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
            case 703:                                            // 아이템 인자1 을 인자2 개
                if (A(1) > 0) _inventory[A(1)] = _inventory.GetValueOrDefault(A(1)) + Math.Max(1, (int)A(2));
                break;
            case 705: _shopMoney += A(1); break;                 // 돈
            case 801:                                            // 동료 넣기 — 다음 전투부터 파티에 든다
                if (A(1) > 0 && _db?.Character(A(1)) is { } c) _party[A(1)] = c;
                break;
            case 802:
                if (A(1) > 0) _party.Remove(A(1));               // 동료 빼기
                break;
        }
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

    private string FieldText(int id) => _fieldTalk?[id] ?? "";

    /// <summary>필드에 나오는 인물의 초상화 — 전투 인물과 달리 뽑아 둔 폴더가 없어 <c>.chr</c> 의 얼굴 Obs 를 바로 읽는다.</summary>
    private void LoadFieldFace(CharacterData c)
    {
        if (_faces.ContainsKey(c.Code) || c.FaceId == 0) return;
        try
        {
            string path = Path.Combine(AssetsFolder.Find("moses"), "obs", $"{c.FaceId:D4}.obs");
            if (File.Exists(path) && ObsSprite.DecodeFirstFrame(path) is { } face) _faces[c.Code] = SpriteFrame.From(face);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { }
    }

    /// <summary>
    /// 필드 대사 — 말하는 이는 <c>10000+인물 열쇠</c> 다. 이름과 초상화는 그 인물의 <c>.chr</c> 에서 온다.
    /// </summary>
    private void ShowFieldTalk(bool box, int speaker, int textId)
    {
        string name = "";
        if (_field is { } field && speaker >= 10000
            && field.People.FirstOrDefault(p => p.Key == speaker - 10000) is { } person
            && _db?.Character(person.ChrCode) is { } c)
        {
            name = _db.T(c.NameId);
            LoadFieldFace(c);
            _talkFace = c.Code;
        }
        else _talkFace = 0;
        _talk = (box, -1, name, FieldText(textId), 0, _lastTime);
        _talkFilled = false;
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
        _fieldVars[_fieldChoiceVar] = (byte)(row + 1);      // 고른 차례는 1부터
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

    private void DrawField()
    {
        if (_field is null) return;
        var (ox, oy) = MosesOrigin();

        FillRect(0, _camY, BoardWidth, ViewHeight, 0xFF000000);
        if (_mosesBg is { } bg)
            for (int y = 0; y < MosesH; y++)
                for (int x = 0; x < MosesW; x++)
                    SetPixel(ox + x, oy + y, bg[y * MosesW + x] | 0xFF000000);

        // 필드 화면은 640×480 틀 안이 전부다 — 물체·인물이 그 밖으로 새지 않게 자른다.
        _uiClip = (ox, oy, MosesW, MosesH);

        // 물체 — 층 번호가 앞뒤 순서라 작은 층부터 그린다.
        foreach (var prop in _fieldProps.Where(o => o.Visible).OrderBy(o => o.Layer))
            DrawUi(prop.Obs, prop.Motion, (int)((_lastTime - prop.Start) * TicksPerSecond),
                   ox + (int)prop.X - _fieldCam.X, oy + (int)prop.Y - _fieldCam.Y, UiBlend.Alpha);

        // 인물 — 층 순서로, 저마다의 모션으로 그린다.
        foreach (var actor in _fieldActors.Where(a => a.Visible).OrderBy(a => a.Layer))
            if (_db?.Character(actor.ChrCode) is { SpriteId: > 0 } pc)
                DrawUi(pc.SpriteId, actor.Motion, (int)((_lastTime - actor.MotionStart) * TicksPerSecond),
                       ox + (int)actor.X - _fieldCam.X, oy + (int)actor.Y - _fieldCam.Y, UiBlend.Alpha,
                       loop: true, fade: actor.Alpha);

        DrawTalk();

        if (_fieldChoices is { Count: > 0 } choices)
        {
            var (x, y, w, h) = FieldChoiceRect(choices.Count);
            DarkenRect(x - 1, y - FrameTitleH - 1, w + 2, h + FrameTitleH + 2, 8);
            DrawGameFrame(x, y, w, h, "");
            for (int i = 0; i < choices.Count; i++)
                DrawText(choices[i], x + 16, y + 14 + i * 22, i == _fieldChoicePick ? 0xFF00FFFF : White, 13);
        }
        _uiClip = null;
        DrawFieldWipe(ox, oy);
        DrawFieldFade(ox, oy);
        DrawToast();
    }

    /// <summary>지금 필드 화면(640×480)을 그대로 찍어 둔다 — 걷어내는 전환이 그 위에서 시작한다.</summary>
    private void CaptureFieldScreen()
    {
        var (ox, oy) = MosesOrigin();
        _fieldShot = new uint[MosesW * MosesH];
        for (int y = 0; y < MosesH; y++)
            Array.Copy(_fb, (oy + y) * BoardWidth + ox, _fieldShot, y * MosesW, MosesW);
    }

    /// <summary>찍어 둔 옛 화면을 새 화면 위에 덮고, 전환 종류대로 걷어낸다.</summary>
    private void DrawFieldWipe(int ox, int oy)
    {
        if (_fieldWipe is not { } wipe || _fieldShot is not { } shot) return;
        int tick = (int)((_lastTime - wipe.Start) * TicksPerSecond);
        if (tick >= wipe.Ticks) { _fieldWipe = null; _fieldShot = null; return; }
        double done = (double)tick / wipe.Ticks;          // 0(옛 화면 그대로) ~ 1(다 걷힘)

        for (int y = 0; y < MosesH; y++)
            for (int x = 0; x < MosesW; x++)
            {
                if (!WipeKeeps(wipe, done, x, y, out int sx, out int sy)) continue;
                SetPixel(ox + x, oy + y, shot[sy * MosesW + sx] | 0xFF000000);
            }
    }

    /// <summary>그 점에 옛 화면이 아직 남아 있는지 — 남아 있으면 옛 화면의 어느 점을 가져올지도 알려 준다.</summary>
    private static bool WipeKeeps((int Kind, int Way, int Ticks, double Start) wipe, double done, int x, int y,
                                  out int sx, out int sy)
    {
        sx = x;
        sy = y;
        if (wipe.Kind == 903)
        {
            // 빗살 — 화면을 세로(또는 가로) 띠로 나누고, 띠마다 <b>번갈아 반대 쪽</b>에서 줄어든다.
            const int CombWidth = 16;
            int band = (wipe.Way is 0 or 1 ? x : y) / CombWidth;
            int within = (wipe.Way is 0 or 1 ? x : y) % CombWidth;
            double left = CombWidth * (1 - done);
            return band % 2 == 0 ? within < left : within >= CombWidth - left;
        }

        // 밀어내기 — 옛 화면이 통째로 한 쪽으로 빠져 나간다.
        int dx = wipe.Way switch { 0 => -(int)(MosesW * done), 1 => (int)(MosesW * done), _ => 0 };
        int dy = wipe.Way switch { 2 => -(int)(MosesH * done), 3 => (int)(MosesH * done), _ => 0 };
        sx = x - dx;
        sy = y - dy;
        return (uint)sx < MosesW && (uint)sy < MosesH;
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
