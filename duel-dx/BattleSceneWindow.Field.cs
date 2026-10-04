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

    /// <summary>
    /// 사건이 띄워 둔 「슬롯」 — (띄운 사건, 아직 살아 있나, 대사창인가).
    /// </summary>
    /// <remarks>
    /// 원본 진행기(<c>0x100f47f0</c>)에서 사건 줄을 붙드는 것은 <b>행동 0·1·2·504 뿐</b>이다. 나머지 행동은 <c>0x100f3400</c> 으로
    /// 슬롯을 잡고(사건 <c>+0x20</c> 셈 ++) 핸들러를 한 번 부른 뒤 <b>곧장 다음 줄</b>로 간다 — 걷기·모션·대사·전환·음악 페이드는
    /// 슬롯 실행기(<c>0x100f35f0</c>)가 매 틀 밀고, 끝나면 핸들러가 <c>0x100f3490</c> 으로 풀어 셈을 내린다.
    /// 뒤따르는 행동 1 은 <b>그 사건이 띄운</b> 슬롯이 다 풀리기를 기다린다(<c>0x100f488a</c>) — 화면의 다른 움직임은 안 본다.
    /// 그래서 <c>208 [10005,87,0] 208 [10006,87,0] 1</c>(Fld 0012 사건 11)은 두 훈련병이 <b>함께</b> 움직인다.
    /// </remarks>
    private readonly List<(int Owner, Func<bool> Alive, bool Talk)> _fieldSlots = [];

    /// <summary>지금 행동을 읽고 있는 사건 — 행동이 띄우는 슬롯의 주인(<c>0x100f3400</c> 의 사건 인자).</summary>
    private int _fieldOwner = -1;

    /// <summary>이 사건 몫의 슬롯을 하나 띄운다 — 처음부터 끝나 있으면(0틱) 안 띄운다.</summary>
    private void HoldSlot(Func<bool> alive, bool talk = false)
    {
        if (alive()) _fieldSlots.Add((_fieldOwner, alive, talk));
    }

    /// <summary>틱 수만큼 사는 슬롯(208 모션 한 바퀴 · 517 음악 페이드 · 900 덮기·걷기 · 전환).</summary>
    private void HoldSlotTicks(double ticks)
    {
        double end = _lastTime + ticks / TicksPerSecond;
        HoldSlot(() => _lastTime < end);
    }

    /// <summary>인물이 이 걷기(202·203·205·206)를 다 걸을 때까지 사는 슬롯 — 새 걷기가 덮으면 풀린다.</summary>
    private void HoldWalkSlot(FieldActor who)
    {
        if (who.Walk is not { } walk) return;
        HoldSlot(() => who.Walk is { } now && now.Start == walk.Start && _fieldActors.Contains(who));
    }

    /// <summary>카메라가 이번 옮기기(401·402)를 마칠 때까지 사는 슬롯 — 새 옮기기·400 이 덮으면 풀린다.</summary>
    private void HoldCameraSlot()
    {
        if (_fieldCamMove is not { } cam) return;
        HoldSlot(() => _fieldCamMove is { } now && now.Start == cam.Start);
    }

    /// <summary>그 사건이 띄운 슬롯이 아직 남았나 — 행동 1 이 본다. 대사창 슬롯이 남았으면 <paramref name="talk"/> 가 참.</summary>
    private bool SlotsBusy(int owner, out bool talk)
    {
        _fieldSlots.RemoveAll(s => !s.Alive());
        talk = false;
        bool busy = false;
        foreach (var s in _fieldSlots)
            if (s.Owner == owner) { busy = true; talk |= s.Talk; }
        return busy;
    }

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
    /// <remarks>
    /// 감사5 D10: 원본 페이드 물체(<c>0x10029f80</c>)의 눈금은 0→31(a2 틱) 뒤 31→63(a3 틱)으로 간다(<c>0x1002a030</c>).
    /// 눈금 &lt; 32 이면 그 세기로 덮고, 이상이면 <b>a0 ≠ 0 일 때만</b> 63−눈금으로 걷는다 — a0 = 0 이면 두 번째 그림도 단색이라 덮인 채 남는다.
    /// <c>Color</c> 는 무늬 a1 의 단색(<c>0x100f1fc0</c>: 0 흰 · 1 검정 · 2 빨강, 감사5 D13), <c>Back</c> 은 a0 ≠ 0.
    /// </remarks>
    private (double Start, int CoverTicks, int UncoverTicks, uint Color, bool Back)? _fieldFade;

    /// <summary>덮기가 가리는 층 수(900 의 a4) — 8 이면 인물·물체까지 다 가린다.</summary>
    private int _fieldFadeCover = 8;

    /// <summary>
    /// 전환(901·903·904·909)·404·900 이 남긴 그림이 가리는 층 수 — 이 층 <b>아래</b>의 물체·인물은 그림 밑에 묻혀 안 보인다.
    /// 자리는 전환마다 다르다(901·904 인자 4, 903·909 인자 3, 404 인자 1, 900 인자 4 — 대부분 8 = 다). 필드 화면으로 돌아오면 0.
    /// 전에는 배경만 그림으로 바꾸고 방 조각(창문·복도 물체)을 그 위에 그대로 그려 Fld 0081 의 컷씬과 섞였다(사용자 보고).
    /// </summary>
    private int _fieldPictureCover;

    /// <summary>
    /// 남은 그림 <c>[0x101bfe18]</c>(640×480) — 404·900 끝·전환 a0 = 0 이 남긴다. 있으면 배경 대신 화면 (0,0)에 그리고
    /// 가림 이상 층만 위에 덧그린다(<c>0x100ebeb9~0x100ec020</c>). <b>배경·카메라는 그대로</b> 둔다(감사5 D8) —
    /// 전에는 그림으로 갈 때 <c>_fieldCam = (0,0)</c> 으로 바꿔 돌아온 필드가 다른 자리를 비췄다(Fld 0176·0195).
    /// </summary>
    private uint[]? _fieldPicture;

    /// <summary>필드 배경 한 장 전체(자르지 않은 것) — 카메라 한계(감사5 D2)와 노란 창 뚫기(D1)에 쓴다.</summary>
    private (uint[] Px, int W, int H)? _fieldBg;

    /// <summary>
    /// 배경 뒤 조각 그림(파일의 B 덩이, 감사5 D1) — 세계 자리 (X1,Y1) 에 W×H 네모로, 내용은 그림 한 장(<c>Px</c>, PicW×PicH)을 감아 두른다.
    /// </summary>
    private readonly List<(int X1, int Y1, int W, int H, uint[] Px, int PicW, int PicH)> _fieldPieces = [];

    /// <summary>407·408 로 숨긴 층 창(0~8) — 층 창 <c>+0x5c</c> 하나의 보이기다(<c>0x100ee690</c>/<c>0x100ee6d0</c>, 감사5 D16).</summary>
    private readonly bool[] _fieldLayerHidden = new bool[9];

    /// <summary>213·307·302 가 층 안 차례를 매길 때 쓰는 셈(감사5 D3).</summary>
    private long _fieldOrderSeq;

    /// <summary>
    /// 걷어내는 전환(903 빗살 지우기 · 904 줄 늘여 쓸기).
    /// </summary>
    /// <remarks>
    /// 901·903~909 는 원본에서 <b>한 함수</b>(<c>0x100f2770</c>)가 돌리고 인자 자리도 같다 —
    /// <b>a0 방향</b>(0 이면 지금 화면 → 그림이라 끝나도 그림이 남고, ≠0 이면 그림 → 지금 화면이라 끝에 그림을 걷는다) ·
    /// <b>a1 <c>Bgr</c> 번호</b> · <b>마지막 인자는 전환이 가리는 층 수</b>(0~8, 그보다 위 층은 전환 위에 덧그린다).
    /// 전환이 도는 동안 슬롯이 살아 있어 뒤따르는 행동 1 이 기다린다(<c>0x100f2b13</c>) — 줄 자체는 안 막는다.
    /// <para>
    /// <c>Base</c> 는 바탕, <c>Over</c> 는 걷히면서 드러나는 그림이다. 904 는 그림 <b>한 장</b>만 쓰고
    /// 바탕은 그 그림의 <b>경계 줄을 늘여</b> 채우므로 <c>Base</c> 가 없다.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <c>Cover</c> 는 전환이 가리는 층 수 — 전환 물체 위에 층 (가림)~7 을 산 채로 덧그린다(<c>0x100ebe66~0x100ebea1</c>, 감사5 D12).
    /// 909 겹쳐 디졸브도 여기로 온다(감사5 D5).
    /// </remarks>
    private sealed record FieldWipe(int Kind, int Way, int Ticks, double Start, uint[]? Base, uint[] Over, int Cover = 8);

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
        /// <summary>행동 209 가 애니메이션을 세운 때(<c>0x100f1680</c> → <c>vt+0xc0(-1)</c>) — 다음 208·걷기까지 그 장에 멈춰 있다(ba-20 B1).</summary>
        public double? FrozenAt { get; set; }
        public bool Mirror { get; set; }
        public bool Visible { get; set; } = true;

        /// <summary>
        /// 모션을 한 바퀴 돌고 <b>마지막 장에 멈추나</b> — 208 인자 2 가 1 이 아니면 원본은 끝날 때까지 기다린 뒤 물체를 멈춘다
        /// (<c>0x100f163f</c> → 틱 셈을 멈추는 <c>0x100f4c30</c>). 걷기가 새 모션을 걸면 풀린다.
        /// 전에는 늘 되풀이해 Fld 0083 엠블라의 안경 만지기(모션 21)가 끝없이 돌았다(사용자 보고).
        /// </summary>
        public bool Hold { get; set; }

        /// <summary>같은 층 안 그리는 차례 — 작을수록 먼저(아래). 로더는 인물을 물체보다 아래에, 앞 번호를 위에 둔다(감사5 D3).</summary>
        public long Order { get; set; }

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

        /// <summary>같은 층 안 그리는 차례 — 작을수록 먼저(아래)(감사5 D3).</summary>
        public long Order { get; set; }

        /// <summary>
        /// 모션을 한 바퀴 돌고 마지막 장에 멈추나 — 302 인자 2 ≠ 1 이면 모션 끝(<c>+0x68</c>)에서 <c>vt+0xc0(−1)</c> 로
        /// 애니메이터를 멈춘다(<c>0x100f1f78</c> → <c>0x100274e0</c>, 감사5 D15). 파일 처음 모션·302 a2 = 1 은 되풀이.
        /// </summary>
        public bool Hold { get; set; }

        /// <summary>
        /// 스크립트(302)가 건 모션이 한 바퀴 끝나는 때 — 그때까지 행동 1 이 기다린다. 원본 물체는 틱마다 셈을 올려
        /// 모션 끝에 닿으면 끝 표시(<c>+0x68</c>)를 세운다(<c>0x100f4c80</c>). 자료에서 302 바로 뒤가 행동 1 인 곳이 869번이다.
        /// </summary>
        public double PlayUntil { get; set; }

        /// <summary>
        /// 302 가 새로 놓은 물체 중 인자 2 가 0 인 것 — 모션을 한 번 돌고 나면 <b>스스로 지워진다</b>(<c>0x100f4c80</c>: 열쇠 −1 이고
        /// <c>+0x60</c>(= 인자 2) 이 0 이면 셈이 모션 길이를 넘을 때 소멸자). 그 때. 인자 2 가 0 이 아니면 null(남는다).
        /// 전에는 다 남겨 Fld 0074 디에네의 「!」(Obs 1180)가 계속 떠 있었다(사용자 보고).
        /// </summary>
        public double? RemoveAt { get; init; }

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
        _fieldPictureCover = 0;
        _fieldPicture = null;
        Array.Clear(_fieldLayerHidden);
        _fieldOrderSeq = 0;
        try
        {
            var files = GameFiles.FromFolder(AssetsFolder.Find("data"));
            var bytes = files.Read("Fld", $"{id:D4}.fld");
            if (FieldFile.Parse(id, bytes) is not { } field || bytes == null) return false;
            _field = field;
            _fieldGray = false;
            _screenWaveOwner = null;   // 필드 개체 생성자 0x100ec180 이 +0x294 = 0
            _fieldTalk = TalkTable.Parse(files.Read("Tlk", $"{id:D4}.tlf"));
            _fieldFired = new int[field.Events.Count];
            _sideEvents.Clear();
            _fieldSlots.Clear();
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
            // 층 안 차례(감사5 D3): 로더는 물체 전부(0x100ec8f5) → 인물 전부(0x100eca0d) 순으로 만들고, 생성자가 부모 창 <b>머리에 끼우므로</b>
            // (0x10001560 → 0x10001650(…,1)) 나중에 만든 것이 먼저(아래) 그려진다 — 한 층 = [마지막 인물 … 첫 인물, 마지막 물체 … 첫 물체].
            _fieldActors = [.. field.People.Select((p, i) => new FieldActor(p) { Order = -1_000_000 - i })];
            // 파일의 물체 — 갈래 칸이 곧 처음 모션이다. 층은 파일 값 그대로(감사5 D17): −1 은 뿌리 창(배경 아래, 안 보임),
            // 8 은 층 7 안의 창 +0x170 — FieldFile 은 0~7 로 잘라 두므로 원래 워드를 다시 읽는다(물체 16바이트의 +10).
            _fieldProps = [.. field.Objects.Select((o, i) => new FieldProp
            {
                Key = o.Key, Obs = o.Picture, Motion = o.Kind, X = o.X, Y = o.Y,
                Layer = 16 + 16 * i + 12 <= bytes.Length ? BitConverter.ToInt16(bytes, 16 + 16 * i + 10) : o.Layer,
                Order = -i,
            })];
            LoadFieldBackdrop(field, bytes);
            _fieldCam = ClampFieldCam(field.CameraX, field.CameraY);   // 필드 시작도 [0, 폭−640]×[0, 높이−480] 로(0x100ec6c3~, 감사5 D2)
            _fieldCamMove = null;
            _mosesOpen = false;
            _talk = null;
            // 필드 배경은 640×480 보다 넓다 — 머리가 정한 첫 화면 자리부터 보여 준다.
            ShowMosesBackground(field.Background, _fieldCam.X, _fieldCam.Y);
            StopMusic();
            // 머리 곡은 논리 크기 0 으로 건다(0x100ec3fb~0x100ec449: 0x10025320(0) → 곧 재개) — 들리기는 100 %(×B.G.M)지만
            // 첫 517 은 0 에서 올린다(뚝 끊겼다 커짐), 첫 512 는 0 을 물려받는다(감사4 M3).
            if (field.Bgm > 0) PlayMusicFile(field.Bgm, loop: true, gain: 0, audible: MusicGain);
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
        _fieldGray = false;
        _screenWaveOwner = null;   // 돌아갈 챕터는 원본에서 다시 만들어진다(0x100f755e → 0x100ec0a0)
        _fieldTalk = null;
        _sideEvents.Clear();
        _fieldSlots.Clear();
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
        _fieldPicture = null;
        _fieldPictureCover = 0;
        _fieldBg = null;
        _fieldPieces.Clear();
    }

    /// <summary>
    /// 필드 배경 전체와 B 덩이 조각 그림을 읽는다(감사5 D1·D2).
    /// </summary>
    /// <remarks>
    /// B 덩이는 물체 뒤 <c>[수, 예비] + 16바이트 × n</c> — 워드 1 <c>Bgr</c> · 4·5 (x1,y1) · 6·7 (x2,y2). 조각 그림은 <c>Bgr 0148</c>·<c>0100</c>(우주)이고,
    /// 이 덩이를 가진 22필드는 모두 노란 창이 뚫린 배경(<c>Bgr 0120</c>·<c>0149</c>·<c>0179</c>)을 쓴다.
    /// 조각 생성자 <c>0x100eaff0</c> 뒤의 <c>0x100eb7d0</c> 이 자리를 0 으로 되돌리는 것처럼 보여 <b>화면 자리는 가설</b>이다 —
    /// 여기서는 (x1,y1) 을 세계 자리로 쓴다(그래야 네모가 노란 창들을 덮는다: Fld 0050 네모 94~687×34~460 ⊃ 노랑 114~681×45~447).
    /// </remarks>
    private void LoadFieldBackdrop(FieldFile field, byte[] bytes)
    {
        _fieldBg = LoadFieldBgr(field.Background);
        _fieldPieces.Clear();
        int at = 16 + 16 * field.Objects.Count;
        if (at + 4 > bytes.Length) return;
        int count = BitConverter.ToInt16(bytes, at);
        for (int i = 0; i < count && at + 4 + 16 * (i + 1) <= bytes.Length; i++)
        {
            short W(int k) => BitConverter.ToInt16(bytes, at + 4 + 16 * i + 2 * k);
            int x1 = W(4), y1 = W(5), w = W(6) - x1, h = W(7) - y1;
            if (w <= 0 || h <= 0 || LoadFieldBgr(W(1)) is not { } pic) continue;   // Fld 0198·0207 의 조각 10 은 0×0
            _fieldPieces.Add((x1, y1, w, h, pic.Px, pic.W, pic.H));
        }
    }

    /// <summary>배경 그림 한 장을 자르지 않고 통째로 읽는다(.bgr = JPEG·GIF).</summary>
    private (uint[] Px, int W, int H)? LoadFieldBgr(int id)
    {
        try
        {
            string path = Path.Combine(AssetsFolder.Find("moses"), "bgr", $"{id:D4}.bgr");
            if (!File.Exists(path)) return null;
            using var bitmap = new System.Drawing.Bitmap(path);
            int w = bitmap.Width, h = bitmap.Height;
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                       System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var px = new uint[w * h];
                for (int y = 0; y < h; y++)
                    new Span<uint>((void*)(data.Scan0 + y * data.Stride), w).CopyTo(px.AsSpan(y * w, w));
                return (px, w, h);
            }
            finally { bitmap.UnlockBits(data); }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// 카메라를 배경 안으로 자른다 — 400·401·402·필드 시작 모두 뿌리 창을 <c>[640−폭, 0]×[480−높이, 0]</c> 로 자른다
    /// (<c>0x100edd80</c>·<c>0x100ee020</c>·<c>0x100ee431~</c>·<c>0x100ec6c3~</c>, 감사5 D2). 전에는 아래(0)만 잘라, 배경은
    /// 멈추는데 인물·물체만 계속 밀려 Fld 0376·0405 의 사람들이 화면 밖으로 나갔다. 배경을 못 읽었으면 위는 안 자른다.
    /// </summary>
    private (int X, int Y) ClampFieldCam(double x, double y)
    {
        int maxX = _fieldBg is { } bg ? Math.Max(0, bg.W - MosesW) : int.MaxValue;
        int maxY = _fieldBg is { } bg2 ? Math.Max(0, bg2.H - MosesH) : int.MaxValue;
        return ((int)Math.Clamp(x, 0, maxX), (int)Math.Clamp(y, 0, maxY));
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
        // 고르기가 떠 있으면 기다린다. 대사창(600·601)은 <b>줄을 안 막는다</b> — 원본은 창을 슬롯으로 띄우고 다음 줄로 가며,
        // 창이 닫히기를 기다리는 것은 뒤따르는 행동 1 이다(0x100f488a, 자료의 600→1 이 2980/3073). 600 에 이어 208·900·517 이
        // 대사와 함께 시작해야 한다(Fld 600→(1 아님) 93곳).
        if (_fieldChoices != null) return;
        if (_talkSkip) { _fieldWaitUntil = 0; if (_field != null) FinishFieldAnimations(); }   // 건너뛰는 중 — 기다림 없이 끝난 자리로
        if (_fieldWaitUntil > _lastTime) return;
        // 걷기(202·203)·자리 옮기기(205·206)도 줄을 안 막는다 — 핸들러(0x100f0be0)는 슬롯만 붙들고 끝나면 0x100f3490 으로 풀 뿐,
        // 사건 pc 는 진행기가 곧장 올린다(0x100f0dfd 는 슬롯을 풀지 말지의 갈래다). 여럿이 <b>함께 걷고</b>, 뒤따르는 1 이 다 걷기를 기다린다.
        // 걷는 도중의 208 은 원본에서도 보이며, 202 인자 5 ≠ 0 이면 끝날 때 방향표의 서기 모션이 다시 덮는다(StepFieldActors).
        // 행동 504 가 걸어 둔 「소리가 끝날 때까지」 — 건너뛰는 중이면 그 소리를 끊고 지나간다.
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
                // 마지막 대사가 행동 1 없이 끝났으면 그 창이 닫힐 때까지는 남는다(원본은 아예 안 나간다 — 데모 안전장치).
                if (_field != null && _sideEvents.Count == 0 && _talk == null) LeaveField();
                return;
            }
        }

        while (_fieldEvent >= 0 && _fieldWaitUntil <= _lastTime)
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
            _fieldOwner = _fieldEvent;                  // 이 행동이 띄우는 슬롯은 이 사건 것이다
            if (!RunFieldAction(action)) return;  // false = 필드를 떠났거나 줄에 머문다(0·1·2·504)
        }
    }

    /// <summary>나란히 도는 곁 사건 — (사건 번호, 다음 줄, 기다림이 끝나는 때).</summary>
    private readonly List<(int Event, int Pc, double WaitUntil)> _sideEvents = [];

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
                _sideEvents.Add((index, 0, 0));
            }
        for (int i = _sideEvents.Count - 1; i >= 0; i--)
        {
            var (ev, pc, waitUntil) = _sideEvents[i];
            var acts = events[ev].Actions;
            bool done = false;
            while (!done)
            {
                if (_talkSkip) waitUntil = 0;
                if (waitUntil > _lastTime) break;
                if (pc >= acts.Count) { done = true; break; }
                var a = acts[pc];
                short A0 = a.Args.Length > 0 ? a.Args[0] : (short)0;
                switch (a.Code)
                {
                    case 1:                                    // <b>이 곁 사건이 띄운</b> 슬롯(걷기·모션·물체 모션)이 끝나기를(0x100f488a)
                        if (_talkSkip) _fieldSlots.RemoveAll(s => s.Owner == ev);
                        if (SlotsBusy(ev, out _)) goto hold;
                        pc++;
                        break;
                    case 2: waitUntil = _lastTime + A0 / TicksPerSecond; pc++; break;
                    case 3: _fieldSlots.RemoveAll(s => s.Owner == ev); done = true; break;   // 제 슬롯을 지우고 접는다
                    case 504: pc++; break;                     // 소리 끝 기다림 — 곁 사건은 안 기다린다
                    default:
                    {
                        // 행동이 띄우는 슬롯(208 모션 한 바퀴 · 걷기 따위)은 <b>이 곁 사건</b>의 것이다 — 주 사건의 행동 1 은 안 기다린다.
                        // 줄을 붙드는 기다림(_fieldWaitUntil·채널)은 주 사건 것이라 건드리지 않게 되돌린다.
                        var (mainWait, mainChannel, mainOwner) = (_fieldWaitUntil, _fieldWaitChannel, _fieldOwner);
                        _fieldOwner = ev;
                        RunFieldAction(a);
                        if (_fieldWaitUntil != mainWait) waitUntil = Math.Max(waitUntil, _fieldWaitUntil);
                        (_fieldWaitUntil, _fieldWaitChannel, _fieldOwner) = (mainWait, mainChannel, mainOwner);
                        pc++;
                        break;
                    }
                }
                continue;
            hold:
                break;
            }
            if (done) _sideEvents.RemoveAt(i); else _sideEvents[i] = (ev, pc, waitUntil);
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
            // [파티, 아이템] 가졌나(0x100edb40) — 가방이나 <b>지금 파티원</b>의 장비. 명부가 하나로 합쳐졌으니 파티 밖 인물은 빼야 한다.
            102 => _inventory.ContainsKey(A(1)) || _party.Where(p => _members.Count == 0 || _members.Contains(p.Key)).Any(p => p.Value.Items.Contains((ushort)A(1))),
            503 => MailTriggerRead(A(0)),                                        // [메일 방아쇠] 그 편지를 읽었나(0x100edc40)
            505 => _mosesChp is { } chp505 && _planetVisits.Remove((chp505.Id, A(0))),   // [행성] 방문 표시 — 한 번 참, 지운다(0x100edcd0)
            // 평가기(0x100f34f0)가 모르는 조건 번호는 <b>참</b>으로 흘린다(갈래 없음 → eax = 사건 포인터 ≠ 0). 샤이닝 스타 사건 5 의 504 가 그렇다.
            _ => true,
        };
    }

    /// <summary>
    /// 화면 어디에든 아직 굴러가는 연출이 있나 — 클릭 한 번으로 기다림을 끝내는 <c>SkipCurrentWait</c> 가 본다.
    /// </summary>
    /// <remarks>
    /// 행동 1 은 이것이 아니라 <b>제 사건의 슬롯</b>만 본다(<see cref="SlotsBusy"/>). 대사창 슬롯은 넣지 않는다(클릭이 창을 닫는다).
    /// </remarks>
    private bool FieldBusy() =>
        _fieldWipe != null || _fieldCamMove != null
        || _fieldActors.Any(w => w.Walk != null || w.Fade != null)
        || _fieldProps.Any(p => p.Move != null || p.PlayUntil > _lastTime)
        || _fieldSlots.Any(s => !s.Talk && s.Alive());

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
                (actor.X, actor.Y, actor.Walk) = (walk.ToX, walk.ToY, null);
                if (walk.EndMotion >= 0) (actor.Motion, actor.Mirror, actor.Hold) = (walk.EndMotion, walk.EndMirror, false);
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
        _fieldProps.RemoveAll(p => p.RemoveAt != null);  // 건너뛰면 한 번 돌고 사라질 물체(「!」 따위)도 치운다
        if (_fieldCamMove is { } cam) { MoveFieldCamera((int)cam.ToX, (int)cam.ToY); _fieldCamMove = null; }
        _fieldWipe = null;                                   // 걷어내기 전환은 끝(원본도 끝나면 그림만 남긴다)
        if (_fieldFade is { } fade2)
        {
            // 끝 눈금(63)으로 — a0 = 0 이면 단색 그림이 남고, a0 ≠ 0 이면 걷힌다(감사5 D10).
            _fieldFade = (_lastTime - 100000, fade2.CoverTicks, fade2.UncoverTicks, fade2.Color, fade2.Back);
            StepFieldFade();
        }
        // 틱으로 사는 슬롯(208 모션 · 517 음악 페이드 · 900 · 전환)도 다 풀었다 — 대사창 슬롯만 남긴다(창은 클릭·건너뛰기가 닫는다).
        _fieldSlots.RemoveAll(s => !s.Talk);
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
        double.TryParse(Environment.GetEnvironmentVariable("DUELDX_HOLD"), out double hold) && hold > 0 ? hold : 30;   // 13초짜리 연출(Fld 0165 사건 6)이 잘리지 않게 넉넉히(ba-20 N9)

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

    /// <summary>추적 기록에 마지막으로 적은 줄 — (사건, 다음 줄).</summary>
    private (int Owner, int Pc) _traceLastLine = (-2, -1);

    private bool RunFieldAction(ScriptCommand a)
    {
        short A(int i) => i < a.Args.Length ? a.Args[i] : (short)0;
        // DUELDX_TRACE=1 이면 어느 줄을 읽었는지 남긴다 — 화면 밖 시험에서 스크립트가 어디서 멈췄는지 보려고.
        // 행동 1 이 같은 줄에서 틀마다 다시 읽히는 것은 처음 한 번만 적는다(대사창을 기다리는 동안 줄이 쌓이지 않게).
        bool traceRepeat = a.Code == 1 && _traceLastLine == (_fieldOwner, _fieldPc);
        _traceLastLine = (_fieldOwner, _fieldPc);
        if (Trace && !traceRepeat)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                               $"fld {(_field?.Id ?? _mosesChp?.Id ?? 0)} "
                               + (_fieldOwner == _fieldEvent ? $"ev {_fieldEvent} pc {_fieldPc - 1}" : $"side ev {_fieldOwner}")
                               + $" t {_lastTime:F2}: {a.Code} [{string.Join(", ", a.Args)}]" + Environment.NewLine);
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
                // <b>이 사건이 띄운</b> 슬롯(대사창·걷기·모션·카메라·전환·900·517…)이 다 끝나기를 기다린다 — 원본은 [사건+0x20] == 0 까지(0x100f488a).
                // 전에는 화면 전체의 움직임(FieldBusy)을 기다려, Fld 0065 사건 7 의 대사마다 곁 사건 4 의 보초가 한 구간 걷기를 마칠 때까지 멈췄다.
                if (_talkSkip) { FinishFieldAnimations(); _fieldSlots.RemoveAll(s => s.Owner == _fieldOwner); }   // 건너뛰는 중이면 끝자리로 보내고 지나간다
                if (!SlotsBusy(_fieldOwner, out bool talkOpen)) { _fieldHoldSince = 0; break; }
                if (talkOpen)
                {
                    // 대사창은 사람이 닫을 때까지(또는 저절로 넘길 때까지) — 참을성 셈에 넣지 않는다.
                    _fieldHoldSince = 0;
                    _fieldPc--;
                    return false;
                }
                // 안 끝나는 연출에 갇히지 않게, 한 줄에서 오래 머물면 그냥 다음 줄로 간다.
                if (_fieldHoldSince <= 0) _fieldHoldSince = _lastTime;
                else if (_lastTime - _fieldHoldSince > FieldHoldSeconds)
                {
                    // 여기 걸렸다는 것은 데모가 못 끝내는 연출이 있다는 뜻이다 — 화면 밖 감사에서 찾으려고 남긴다.
                    if (Trace)
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                                           $"HOLD fld {(_field?.Id ?? _mosesChp?.Id ?? 0)} ev {_fieldEvent} pc {_fieldPc - 1}"
                                           + $" (slots {_fieldSlots.Count(s => s.Owner == _fieldOwner)}, wipe {_fieldWipe != null}, cam {_fieldCamMove != null},"
                                           + $" walk {_fieldActors.Count(w => w.Walk != null)}, fade {_fieldActors.Count(w => w.Fade != null)},"
                                           + $" prop {_fieldProps.Count(pr => pr.Move != null)})" + Environment.NewLine);
                    _fieldSlots.RemoveAll(s => s.Owner == _fieldOwner);
                    _fieldHoldSince = 0;
                    break;
                }
                _fieldPc--;                                  // 다음 틀에 이 줄을 다시 본다
                return false;
            }
            case 2:
                if (_talkSkip) break;   // 건너뛰는 중이면 기다림(행동 2)은 그냥 지나간다(0x100f4844, ba-20 N6)
                // 대사 사이의 멈춤은 설정한 초만큼만(음수면 스크립트 값 그대로 — 원본).
                _fieldWaitUntil = _lastTime + (_talkPauseSeconds >= 0 && IsPauseBetweenLines()
                                                   ? Math.Min(_talkPauseSeconds, A(0) / TicksPerSecond)
                                                   : A(0) / TicksPerSecond);
                break;
            case 504:                                        // [채널] 그 채널의 소리가 끝날 때까지(진행기 0x100f489b 가 직접 본다)
                if (_talkSkip) { StopChannelSound(A(0)); break; }
                _fieldWaitChannel = A(0);
                return false;
            case 3:
                // 이 사건 접기 — 원본은 제 슬롯을 다 지우고(0x100f3490) 사건을 되돌린 뒤 1 을 돌려준다. 행동 0 으로 불린 하위 사건이면
                // 부모가 그 1 을 받고 다음 줄로 간다(§ 진행기 0x100f47f0). 전에는 _fieldReturn 을 안 비워, 하위 사건에서 쓰면
                // 다음 사건이 끝날 때 엉뚱한 부모 자리로 돌아갔다.
                _fieldSlots.RemoveAll(s => s.Owner == _fieldOwner);
                if (_fieldReturn.Count > 0) (_fieldEvent, _fieldPc) = _fieldReturn.Pop();
                else { _fieldEvent = -1; _talkSkip = false; }
                break;

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
            case 101:                                        // [변수, 연산, 값] — 원본은 바이트에 그대로 써서 <b>넘치면 돈다</b>(0x100f2fa0, 자르지 않는다)
                ScriptVars[A(0) & 0xFF] = unchecked((byte)FieldArithRaw(ScriptVars[A(0) & 0xFF], A(1), A(2)));
                break;
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
            case 602: FieldTalkCommand(a); break;            // 인자 2 = 음성, 3 = 600 표정 / 602 틀(Obs 222+값), 4 = 602 지직거림 — 창 칸은 Talk.cs(감사 3 T6·T7)
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
                double moveStart = _lastTime;
                // 핸들러는 틱 0…a3 의 a3+1 번 더한다(감사5 D14 — 첫 틀에 실행기 + 슬롯 실행기 0x100f49df 로 두 걸음).
                int steps = Math.Max(0, (int)A(3)) + 1;
                prop.Move = (prop.X, prop.Y, prop.X + A(1) * steps, prop.Y + A(2) * steps, ticks, moveStart);
                HoldSlot(() => prop.Move is { } now && now.Start == moveStart && _fieldProps.Contains(prop));   // 다 옮길 때까지 슬롯(행동 1 이 기다린다)
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
                        prop.Hold = A(2) != 1;               // a2 ≠ 1 은 모션 끝에서 멈춤(0x100f1f78, 감사5 D15)
                        prop.PlayUntil = _lastTime + (UiFor(prop.Obs)?.MotionLength(prop.Motion) ?? 0) / TicksPerSecond;
                        // 인자 2 가 1 이면 슬롯이 곧장 풀리고, 아니면 모션 끝(+0x68)까지 산다(0x100f1d90) — 뒤따르는 행동 1 이 이것을 기다린다.
                        if (A(2) != 1) HoldSlot(() => prop.PlayUntil > _lastTime && _fieldProps.Contains(prop));
                        SchedulePropSounds(prop);   // 문 여닫는 소리(Obs 529 모션 1·2 → 157·158) 같은 모션 소리
                    }
                }
                else if (A(0) > 0)
                    _fieldProps.Add(new FieldProp
                    {
                        Obs = A(0), Motion = A(1), X = A(3), Y = A(4), Layer = 7,
                        Mirror = A(5) != 0, Start = _lastTime, Hold = A(2) != 1,
                        // 층 7 창을 부모로 생성(0x100f1e9b) — 머리에 끼워져 층 7 맨 아래(감사5 D3).
                        Order = -3_000_000 - ++_fieldOrderSeq,
                        // a2 ≠ 1 이면 모션 끝에 핸들러가 지운다(0x100f1f97), a2 = 0 은 물체 틱이 지운다(감사5 D18) — a2 = 1 만 되풀이하며 남는다.
                        RemoveAt = A(2) != 1 ? _lastTime + (UiFor(A(0))?.MotionLength(A(1)) ?? 0) / TicksPerSecond : null,
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
                prop.Order = ++_fieldOrderSeq;               // 새 층 창 꼬리에 붙인다(0x100f1d50 → 0x10001650(…,0)) — 그 층 맨 위(감사5 D3)
                break;
            }
            case 202:                                        // 걷기(목적지) — 걷는 동안 걷기 모션, 멈추면 서기 모션
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                var (walk, stand, mirror) = FieldFacing(A(4));
                SetActorMotion(who, walk);
                who.Hold = false;
                who.Mirror = mirror;
                // 인자 5 가 0 이면 끝날 때 모션을 안 건드린다(원본은 a5 ≠ 0 일 때만 방향표 0x100f0fd8 로 서기 모션을 건다) —
                // 걷는 도중 걸린 208 모션이 그대로 남는다. −1 = 그대로.
                who.Walk = (who.X, who.Y, A(1), A(2), Math.Max(1, (int)A(3)), _lastTime,
                            A(5) != 0 ? stand : -1, mirror);
                HoldWalkSlot(who);                           // 줄은 안 막고 슬롯만 — 뒤따르는 1 이 다 걷기를 기다린다
                break;
            }
            case 203:                                        // 걷기(매 틀 증분) — 방향에 맞는 모션까지 202 와 같다
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                var (walk, stand, mirror) = FieldFacing(A(4));
                int ticks = Math.Max(1, (int)A(3));
                int steps = Math.Max(0, (int)A(3)) + 1;      // 틱 0…a3 의 a3+1 번(0x100f116c·0x100f11a6, 감사5 D14 — Fld 0354 돌문 155px)
                SetActorMotion(who, walk);
                who.Hold = false;
                who.Mirror = mirror;
                who.Walk = (who.X, who.Y, who.X + A(1) * steps, who.Y + A(2) * steps, ticks, _lastTime,
                            A(5) != 0 ? stand : -1, mirror);
                HoldWalkSlot(who);
                break;
            }
            case 206:                                        // 자리 옮기기(매 틀 증분) — 모션은 안 건드린다
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                int ticks = Math.Max(1, (int)A(3));
                int steps = Math.Max(0, (int)A(3)) + 1;      // a3+1 번 더한다(감사5 D14)
                who.Walk = (who.X, who.Y, who.X + A(1) * steps, who.Y + A(2) * steps, ticks, _lastTime,
                            -1, who.Mirror);
                HoldWalkSlot(who);
                break;
            }
            case 205:                                        // 자리 옮기기 — 모션은 안 건드린다
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                who.Walk = (who.X, who.Y, A(1), A(2), Math.Max(1, (int)A(3)), _lastTime, -1, who.Mirror);
                HoldWalkSlot(who);
                break;
            }
            case 208:                                        // 모션 지정
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                // 같은 모션이 아직 도는 중이면 처음으로 안 돌린다(0x10027090, 감사5 D19) — 되풀이 중이거나 한 바퀴를 아직 안 돈 때.
                // 마지막 장에 멈춰 선(Hold) 모션이면 애니메이터가 멈춘 것이라 처음부터 다시 돈다.
                bool running = who.Motion == A(1)
                               && !(who.Hold && _db?.Character(who.ChrCode) is { SpriteId: > 0 } spc
                                    && UiFor(spc.SpriteId)?.MotionLength(A(1)) is > 0 and var runLength
                                    && (_lastTime - who.MotionStart) * TicksPerSecond >= runLength - 1);
                who.Motion = A(1);
                who.Mirror = A(3) != 0;
                if (who.FrozenAt != null) { running = false; who.FrozenAt = null; }   // 209 로 선 모션은 다시 돈다(vt+0xc0(0))
                if (!running) who.MotionStart = _lastTime;
                who.Hold = A(2) != 1;
                // 인자2 가 1 이면 되풀이라 슬롯이 곧장 풀리고, 아니면 <b>한 바퀴 다 돌 때까지</b> 슬롯이 산다(0x100f1510).
                // 줄은 안 막는다 — 원본은 여럿의 208 이 한 틀에 함께 시작하고 뒤따르는 행동 1 이 다 돌기를 기다린다
                // (Fld 0012 사건 11 의 두 훈련병, 208→208 752곳). 전에는 208 마다 줄을 막아 한 명씩 차례로 움직였다.
                if (A(2) != 1 && _db?.Character(who.ChrCode) is { SpriteId: > 0 } pc
                    && UiFor(pc.SpriteId)?.MotionLength(A(1)) is > 0 and var length)
                    HoldSlotTicks(length);
                break;
            }
            case 209:                                        // 모션 멈추기 — 애니메이션을 지금 장에서 세운다(0x100f1680, ba-20 B1)
                if (FieldActorOf(A(0)) is { } still) still.FrozenAt ??= _lastTime;
                break;
            case 210:                                        // 서서히 사라지기
            case 211:                                        // 서서히 나타나기
            {
                if (FieldActorOf(A(0)) is not { } who) break;
                double to = a.Code == 211 ? 1 : 0;
                if (a.Code == 211) who.Visible = true;        // 나타날 때는 먼저 보이게 해 두고 밝기를 올린다
                if (A(1) <= 0) { who.Alpha = to; who.Visible = to > 0; who.Fade = null; break; }
                double fadeStart = _lastTime;
                who.Fade = (who.Alpha, to, A(1), fadeStart);
                HoldSlot(() => who.Fade is { } now && now.Start == fadeStart && _fieldActors.Contains(who));
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
                who.Order = ++_fieldOrderSeq;                // 새 층 창 꼬리에(0x100f1966 → 0x10001650(…,0)) — 그 층 맨 위(감사5 D3)
                break;
            }
            case 400:                                        // 화면을 그 자리로(즉시)
                _fieldCamMove = null;
                MoveFieldCamera(A(0), A(1));
                break;
            case 401:                                        // <b>매 틀</b> (a0,a1)씩 a2 틀 동안 민다 — 한 번이 아니다
            {
                // 원본은 틀마다 (a0,a1) 을 더하고 배경 안으로 자른다(0x100ee045) — 벽에 닿으면 거기서 선다. 틱 0…a2 의 a2+1 번(감사5 D2·D14).
                // 한 방향으로만 더하므로 「틀마다 자르기」 = 「끝자리를 보간해 틀마다 자르기」(MoveFieldCamera 가 자른다).
                int camTicks = Math.Max(1, (int)A(2));
                int camSteps = Math.Max(0, (int)A(2)) + 1;
                _fieldCamMove = (_fieldCam.X, _fieldCam.Y,
                                 _fieldCam.X + A(0) * camSteps, _fieldCam.Y + A(1) * camSteps, camTicks, _lastTime);
                HoldCameraSlot();
                break;
            }
            case 402:                                        // 그 인물이 화면 한가운데 오도록 a1 틀에 걸쳐
            {
                if (FieldActorOf(A(0)) is not { } target) break;
                int camTicks = Math.Max(1, (int)A(1));
                // 목표를 먼저 배경 안으로 자르고 보간한다(0x100ee431~0x100ee493, 감사5 D2).
                var (goalX, goalY) = ClampFieldCam(target.X - MosesW / 2.0, target.Y - MosesH / 2.0);
                _fieldCamMove = (_fieldCam.X, _fieldCam.Y, goalX, goalY, camTicks, _lastTime);
                HoldCameraSlot();
                break;
            }
            case 901:                                        // 밀어내기 — a2 는 화면 너비에 더하는 여분 거리다
                BeginFieldWipe(901, A(2), Math.Max(1, (int)A(3)), A(0) == 0, A(1), A(4));
                break;
            case 903:                                        // 빗살 지우기 — a2 는 틀 수가 아니라 <b>세로 띠 수</b>다
            {
                int bands = Math.Max(1, (int)A(2));
                BeginFieldWipe(903, bands, MosesW / bands + bands, A(0) == 0, A(1), A(3));
                break;
            }
            case 904:                                        // 줄 늘여 쓸기 — a2 방향(0 아래→위, 1 위→아래, 2 오른→왼, 3 왼→오른)
                BeginFieldWipe(904, A(2), Math.Max(1, (int)A(3)), A(0) == 0, A(1), A(4));
                break;
            case 404:                                        // [Bgr, 가림] 그림 띄우기 — 남은 그림 = Bgr a0, 가림 = a1(0x100f2180, 감사5 D6)
                // 즉시 그 그림 + 층 a1~7. 카메라는 그대로(감사5 D8). 전에는 무동작이라 Fld 0038 사건 18 의 컷(206 → 97)이 안 바뀌었다.
                if (ReadBackground(A(0)) is { } picture404)
                {
                    _fieldPicture = picture404;
                    _fieldPictureCover = Math.Clamp((int)A(1), 0, 8);
                }
                break;
            case 405:                                        // 그림 걷기 — 남은 그림을 지워 필드로(0x100f21c0, 감사5 D6)
                _fieldPicture = null;
                _fieldPictureCover = 0;
                break;
            case 902: break;                                 // 동영상(<c>Mov\%04d.mov</c>) — 게임 쪽 영상만 405MB 라 데모는 안 묶는다
            case 905:                                        // 뜻이 아직 가설인 전환들 — 자료에 한 번씩뿐이다.
            case 907:                                        // 인자 자리(a0 방향 · a1 그림)는 다 같으니 <b>겹쳐 디졸브로 갈음</b>한다.
            case 908:                                        // 걸리는 틀은 909 자리(a2)로 읽는다 — 제 자리는 저마다 다르다.
            case 909:                                        // 겹쳐 디졸브 — 전환의 대부분이 이것이다
                // a0 이 방향이다. 0 이면 그 그림으로 갈아타고 <b>그림이 그대로 남는다</b>.
                // 0 이 아니면 그림에서 <b>이 필드 화면으로 돌아오며 그림을 걷는다</b> — 909 27번·903 5번·904 1번·905 1번이 이쪽이다.
                // 원본 0x1002cc60 은 a2 틱 동안 8단계로 겹친다(감사5 D5) — 전에는 그림을 즉시 바꾸고 기다리기만 했다.
                // 전환 물체 +0x58 이 설 때까지 슬롯 — 줄은 안 막는다(909→205 7곳은 원본에서 디졸브와 함께). 카메라는 안 건드린다(D8).
                // 908(0x1002c980)은 60틱 고정에 가림이 a2, 907(0x1002c6b0)은 a4 틱에 가림 a5 다(가림 표 0x100f2b6c, ba-20 N7) — 909 자리로 읽으면
                // Fld 0355 의 908[0,209,4] 가 4틱 디졸브 + 가림 0 이 됐다.
                if (a.Code == 908) { BeginFieldWipe(909, 0, 60, A(0) == 0, A(1), A(2)); break; }
                if (a.Code == 907 && A(4) > 0) { BeginFieldWipe(909, 0, A(4), A(0) == 0, A(1), A(5)); break; }
                if (A(0) == 0 && A(1) <= 0) { if (A(2) > 0) HoldSlotTicks(A(2)); break; }
                if (A(2) > 0) BeginFieldWipe(909, 0, A(2), A(0) == 0, A(1), A(3));
                else if (A(0) == 0)
                {
                    if (ReadBackground(A(1)) is { } picture909)
                    {
                        _fieldPicture = picture909;
                        _fieldPictureCover = Math.Clamp((int)A(3), 0, 8);
                    }
                }
                else
                {
                    _fieldPicture = null;                    // 필드 화면으로(감사5 D9)
                    _fieldPictureCover = 0;
                }
                break;
            case 407:
            case 408:                                        // 층 창 하나 감추기·보이기 — 층 창 +0x5c = 0/1(0x100ee690/0x100ee6d0, 감사5 D16)
                // 자식의 제 보이기(304·305·210·211)는 그대로 둔다 — 전에는 그 층 것 하나하나의 Visible 을 바꿔 304 로 숨긴 물체까지 살아났고
                // (Fld 0186 사건 6~8), 407 로 숨긴 층에서 307 로 옮겨 나간 물체가 계속 안 보였다(Fld 0376 사건 2·6).
                if ((uint)A(0) < (uint)_fieldLayerHidden.Length) _fieldLayerHidden[A(0)] = a.Code == 407;
                break;
            case 412:                                        // 화면 흑백 [0/1] — 회상 장면. 그리기가 (7R+2G+B)/10 회색 팔레트로 바꿔 그린다(0x100ee750 → 0x10026280, ba-14 E4)
                _fieldGray = A(0) != 0;
                break;
            case 409:                                        // 화면 물결 [0/1] — [[0x101bfe1c]+0x294] = a0(0x100ee710). 필드·챕터 둘 다(audit3 R3)
                _screenWaveOwner = A(0) != 0 ? (object?)_field ?? _mosesChp : null;
                break;
            case 900:
            {
                int cover900 = Math.Clamp((int)A(4), 0, 8);   // 가리는 층 수 — 8 이면 다 가린다
                // a0 ≠ 0(원래 화면으로 돌아오기)이면 남은 그림(앞 900 의 단색·404 그림)을 치운다(끝 0x100f26ee 의 0x100f20d0 — 여기서는
                // 밝아지는 동안 필드가 보이게 처음에 치운다). a5 > 0 이면 Bgr a5 를 새 남은 그림(가림 a4)으로 두고 그 그림으로 밝아진다
                // (0x100f2243~0x100f225d · 0x100f2735~0x100f2746, 감사5 D7 — Fld 0233 회상 컷 92·91·90, 0411 엔딩 251).
                if (A(0) != 0)
                {
                    _fieldPicture = null;
                    _fieldPictureCover = 0;
                    if (A(5) > 0 && ReadBackground(A(5)) is { } picture900)
                    {
                        _fieldPicture = picture900;
                        _fieldPictureCover = cover900;
                    }
                }
                _fieldWipe = null;                            // 전환 물체는 하나뿐이다([0x101c0014])
                // 무늬(0x100f1fc0): 0 흰(0x7fff/0xffff) · 1 검정 · 2 빨강(0x7c00/0xf800) 단색(감사5 D13).
                uint color900 = A(1) switch { 0 => 0xFFFFFFFF, 2 => 0xFFFF0000, _ => 0xFF000000 };
                _fieldFade = (_lastTime, Math.Max(0, (int)A(2)), Math.Max(0, (int)A(3)), color900, A(0) != 0);
                _fieldFadeCover = cover900;
                // 눈금이 63 에 닿을 때(a2 + a3 틱)까지 슬롯이 산다(전환 물체 +0x58, 0x100f26ee) — 줄은 안 막고 뒤따르는 1 이 기다린다.
                // Fld 0012 사건 4 의 900[1,1,0,45,8] → 517[100,60] → 1 → 2[30] → 600 은 밝아지기와 음악 페이드가 <b>함께</b> 돌고,
                // 둘 다 끝난 뒤 1초 쉬고 첫 대사가 뜬다(전에는 걷어내기를 안 기다려 밝아지는 도중에 대사가 떴다).
                HoldSlotTicks(Math.Max(0, (int)A(2)) + Math.Max(0, (int)A(3)));
                break;
            }
            case 512:                                        // BGM 바꾸기 — 새 곡은 <b>옛 곡의 지금 크기</b>로 시작한다(0x100eed8a, 옛 곡이 없으면 0)
                // 512 358곳 중 346곳이 바로 뒤 517 로 키운다 — 0(또는 줄여 둔 크기)에서 서서히 커지는 연출이다.
                // 대사 건너뛰기 중([0x101bffb0] ≠ 0)이면 원본은 통째로 무시한다(0x100eed6d~0x100eed74, 감사4 M4).
                if (_talkSkip) break;
                PlayMusicInherit(A(0));
                // 챕터 사건이 건 곡은 챕터 창이 80 % 까지 틱마다 1 % 올린다(0x100f80c9, 감사4 M1).
                if (_field is null && _mosesOpen) MarkChapterEventMusic();
                break;
            case 515:                                        // 멈춘 배경음악 잇기(0x100eee50 → 0x10025480, 감사4 M5) — Fld 360 사건 1
                ResumeMusic();
                break;
            case 517:                                        // 음량을 인자0(0~100)까지 인자1 틱에 걸쳐 — 그동안 슬롯이 산다(0x100eee70, +4 ≥ a1 에서 풀림)
                FadeMusic(A(0), A(1));
                HoldSlotTicks(A(1));
                break;
            case 910:                                        // [v] 챕터 주 화면 가운데 구체를 Obs 562 ↔ 587 로(Chp 0045·0050) —
                break;                                       // 데모 모세스 주 화면은 그 구체를 안 그려 할 일이 없다
            case 911:                                        // [단계, 번호] 항행 시작 단계·번호(챕터 +0x2e40/+0x2e42, 0x100f6c60) — a ≥ 1 일 때만
                if (A(0) >= 1 && _mosesChp is { } navChp) _navStart = (navChp.Id, A(0), A(1));
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
                // 되감아 멈출 뿐 개체와 크기(%)는 남는다 — 뒤따르는 512 가 그 크기로 바로 튼다(감사4 M2).
                RewindMusic();
                break;
            case 500:                                        // 소리 한 번 내고 끝날 때까지 슬롯(0x100ee7b0) — 인자1 은 매달 인물. 뒤따르는 1 이 기다린다
                if (_talkSkip) break;                        // 건너뛰는 중이면 원본도 소리를 안 낸다([0x101bffb0] 검사)
                PlayChannelSound(FieldVoiceChannel, A(0), loop: false, FieldSoundX(A(1)));
                HoldSlot(() => ChannelBusy(FieldVoiceChannel));
                break;
            case 501:                                        // [소리, 채널, 인물, 되풀이] 채널에 걸고 기다리지 않는다(0x100ee960)
                if (_talkSkip) break;
                PlayChannelSound(A(1), A(0), loop: A(3) != 0, FieldSoundX(A(2)));
                break;
            case 506:                                        // [채널, 음량, 틱] 채널 음량을 서서히 바꾼다(0x100eeb80) — 517 의 채널판
                if (_talkSkip) break;                        // 건너뛰는 중이면 원본도 건너뛴다([0x101bffb0] 검사)
                FadeChannelSound(A(0), A(1), A(2));
                HoldSlotTicks(Math.Max(1, (int)A(2)));       // 그 틱만큼 슬롯이 산다 — 줄은 안 막고 뒤따르는 1 이 기다린다(자료 둘 다 뒤가 1)
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
    /// 스크립트 꼴이 필드와 같아 같은 실행기(<see cref="RunFieldAction"/>)를 쓴다. 기다림 없는 사건만 여기서 한 번에 훑고,
    /// 대사·고르기·기다림이 든 사건은 모세스 위의 실행기(<see cref="UpdateField"/>)에 맡긴다.
    /// <c>Chp 0010</c> 이라면 깃발 107·113·116·117·118·14 를 세우고 동료 둘(219·221)과 3000GP,
    /// 아이템 122×10 · 124×3 · 125×10 · 84×2 · 126×1 을 준다.
    /// </remarks>
    /// <returns>스크립트가 장면을 떠났으면(6 필드 · 7 · 10 전투 · 11 · 12) true — 부른 쪽은 모세스 화면을 더 꾸미지 않는다.</returns>
    private bool RunChapterScript(ChapterFile chapter)
    {
        // 사건별 횟수가 없던 옛 세이브에서 온 챕터는 「다 돌았다」로 본다(사건 −1 표시).
        if (_chapterFired.ContainsKey((chapter.Id, -1))) return false;
        foreach (var wanted in chapter.Events.Count > 0 ? chapter.Events[0].Actions : [])
        {
            int index = wanted.Args.Length > 0 ? wanted.Args[0] : -1;
            if ((uint)index >= chapter.Events.Count || index == 0) continue;
            var e = chapter.Events[index];
            if (e.MaxFire > 0 && _chapterFired.GetValueOrDefault((chapter.Id, index)) >= e.MaxFire) continue;
            // 대사·고르기·기다림(줄을 붙드는 0·1·2·504)이 든 사건은 여기서 안 돌고 실행기(UpdateField)가 모세스 위에서 돈다.
            // 조건보다 <b>먼저</b> 거른다 — 조건 505(행성 방문)는 보는 순간 표시를 지우므로, 여기서 보고 건너뛰면
            // 실행기가 볼 때는 이미 지워져 그 사건이 영영 안 돈다(0011 사건 14 등 11개, 분석-모세스 mo-mail).
            if (e.Actions.Any(a => a.Code is 0 or 1 or 2 or 504 or 600 or 601 or 602 or 603 or 604 or 605 or 609)) continue;
            if (!e.Conditions.All(FieldCondition)) continue;
            _chapterFired[(chapter.Id, index)] = _chapterFired.GetValueOrDefault((chapter.Id, index)) + 1;
            // 챕터 사건도 필드와 <b>같은 실행기</b>다 — 모든 행동을 돈다. 전에는 70x·80x·102·103 만 돌리고 6·10·100·101·910·911 을
            // 버린 채 횟수만 올려, Chp 0057 사건 1(102[38,1] → 10[110])의 자동 전투가 사라지고 Chp 0050 사건 1 의 100[87,1]·100[32,1],
            // Chp 0013 사건 4 의 100[77,10] 이 안 섰다.
            _fieldOwner = index;
            foreach (var a in e.Actions)
                if (!RunFieldAction(a)) return true;         // 필드·전투·타이틀로 떠났다
        }
        return false;
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
                // 레벨이 1~254 일 때만 「이미 배움」이다 — 0xff 는 안 배운 칸이라 레벨 1 로 덮는다(0x100f074e, ba-21 outer-rules).
                UpdateCharacter(A(0), c => c.Abilities.Any(ab => ab.Ability == A(1) && ab.Level is > 0 and < 0xff) ? c
                    : c with { Abilities = [.. c.Abilities.Where(ab => ab.Ability != A(1)), ((ushort)A(1), (ushort)1)] });
                break;
            case 805:                                            // 레벨 맞추기 [Chr, Δ] — 파티 레벨(상위 셋 평균, 0x1004e070) + Δ 로(0x100f0b40 → 0x10031a50)
                if (_db is { } db805)
                    UpdateCharacter(A(0), c =>
                    {
                        // 성장은 직업 성장률 합으로 기본값에서 다시 센다(0x10031a50, ba-21 outer-rules 1) — 전에는 적·손님용 Lev.dat 식을 써서
                        // 합류 인물의 PSY 가 30~45%, DEX 가 20~50% 낮았다(Chr 237 Lv40: PSY 460 ↔ 297). 장비·어빌리티·직업·체질은 c 그대로다.
                        var grown = db805.SetLevel(c, RosterLevel() + A(1));
                        // 원본은 남은 EXP(어빌리티를 배우는 데 쓰는 것)를 안 건드린다. 그러면 Lv60 으로 합류한 유진이 누적 6000 을 번 셈인데
                        // 배울 EXP 는 0 이라 스킬이 다 낮다(사용자 보고) — 레벨 맞추기로 늘어난 누적 EXP 만큼 남은 EXP 도 준다(원본과 다르다).
                        int gained = Math.Max(0, grown.CumExp - Math.Max(c.CumExp, c.Level * 100));
                        return grown with { Exp = c.Exp + gained };
                    });
                if (Trace)
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                        $"805: Chr {A(0)} → Lv {_party.GetValueOrDefault(A(0))?.Level} EXP {_party.GetValueOrDefault(A(0))?.Exp} (파티 {RosterLevel()} + {A(1)})" + Environment.NewLine);
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
        _fieldCam = ClampFieldCam(x, y);                     // 배경 안으로(감사5 D2)
        if (_field is { } field && _fieldBg is null) ShowMosesBackground(field.Background, _fieldCam.X, _fieldCam.Y);
    }

    /// <summary>대상 지정 값(<c>10000+열쇠</c>)이 가리키는 물체.</summary>
    private FieldProp? FieldPropOf(int value) =>
        value >= 10000 ? _fieldProps.FirstOrDefault(o => o.Key == value - 10000) : null;

    /// <summary>대상 지정 값(<c>10000+열쇠</c>)이 가리키는 인물.</summary>
    private FieldActor? FieldActorOf(int value) =>
        value >= 10000 ? _fieldActors.FirstOrDefault(a => a.Key == value - 10000) : null;

    /// <summary>
    /// 걷기·서기 모션을 건다 — 다른 모션이면 틱 0 부터, 같은 모션이 도는 중이면 그대로(0x10027090, 감사5 D19).
    /// 전에는 MotionStart 를 안 고쳐 걷기 첫 걸음이 아무 장에서나 시작했다.
    /// </summary>
    private void SetActorMotion(FieldActor who, int motion)
    {
        if (who.Motion == motion && who.FrozenAt == null) return;
        who.Motion = motion;
        who.MotionStart = _lastTime;
        who.FrozenAt = null;
    }

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
                if (walk.EndMotion >= 0)                      // −1 = 끝날 때 모션을 안 건드린다(202 인자 5 = 0 · 205·206)
                {
                    SetActorMotion(actor, walk.EndMotion);
                    actor.Hold = false;
                    actor.Mirror = walk.EndMirror;
                }
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

        _fieldProps.RemoveAll(p => p.RemoveAt is { } gone && _lastTime > gone);   // 한 번 돌고 사라지는 물체(302 인자 2 = 0)
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

    private static byte FieldArith(byte now, int op, int value) => (byte)Math.Clamp(FieldArithRaw(now, op, value), 0, 255);

    private static int FieldArithRaw(byte now, int op, int value) => op switch
    {
        0 => now + value,
        1 => now - value,
        2 => now * value,
        _ => value != 0 ? now / value : now,
    };

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
        // 말하는 이 자리로 좌우를 가른다(0x1003ba76 꼬리점 → 0x100f5350, 감사4 S2). 필드 대사 상자는 꼬리를 말하는 이에게 댄다 — 그 x.
        PlayTalkVoice(voice, FieldSoundX(speaker));
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
        // 대사창도 슬롯이다 — 창이 닫힐 때(+0x108 == 4)까지 살고, 뒤따르는 행동 1 이 그것을 기다린다(0x100eeef0).
        // 새 창이 이 창을 바꿔 치면(원본도 새 600 은 있던 창을 지운다) 이 슬롯은 풀린다.
        int serial = ++_fieldTalkSerial;
        HoldSlot(() => _talk != null && _fieldTalkSerial == serial, talk: true);
    }

    /// <summary>필드 대사창을 띄운 차례 — 대사창 슬롯이 「내 창이 아직 떠 있나」를 가린다.</summary>
    private int _fieldTalkSerial;

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
        int row = FieldChoiceAt(bx, by, choices.Count);
        if (row < 0) return true;
        ScriptVars[_fieldChoiceVar] = (byte)(row + 1);      // 고른 차례는 1부터
        _fieldChoices = null;
        // 원본 필드 고르기(604)는 소리를 안 낸다 — Snd 66 은 모세스 주 화면 단추뿐(0x10104950, 감사4 S5).
        return true;
    }

    /// <summary>고르기 창 — 640×480 틀 안, 대사 상자 바로 위에 놓는다.</summary>
    /// <summary>고르기 창에서 마우스가 얹힌 줄을 표시한다.</summary>
    private void UpdateFieldHover(int bx, int by)
    {
        if (_fieldChoices is not { Count: > 0 } choices) return;
        _fieldChoicePick = FieldChoiceAt(bx, by, choices.Count);
    }

    /// <summary>
    /// 고르기(604·605) 항목 k 의 띠 — 원본은 항목마다 620×32 띠(틀 Obs 0225)를 x 10, y 230 에서 시작해 항목이 늘 때마다 25px 위로,
    /// 50px 간격으로 놓는다(0x100f3120 · 0x1003d1d0 · 0x1003d440, ba-20 N4). 띠의 정확한 y 는 가설.
    /// </summary>
    private (int X, int Y) FieldChoiceBand(int k, int rows)
    {
        var (fx, fy) = MosesOrigin();
        return (fx + 10, fy + 230 - 25 * (rows - 1) + 50 * k);
    }

    /// <summary>그 자리의 항목 번호(없으면 −1).</summary>
    private int FieldChoiceAt(int bx, int by, int rows)
    {
        for (int k = 0; k < rows; k++)
        {
            var (x, y) = FieldChoiceBand(k, rows);
            if (bx >= x && bx < x + 620 && by >= y && by < y + 32) return k;
        }
        return -1;
    }

    /// <summary>고르기 띠 묶음 — 필드와 모세스(챕터 스크립트) 둘 다 쓴다.</summary>
    private void DrawFieldChoices()
    {
        if (_fieldChoices is not { Count: > 0 } choices) return;
        for (int i = 0; i < choices.Count; i++)
        {
            var (x, y) = FieldChoiceBand(i, choices.Count);
            for (int m = 3; m <= 5; m++) DrawUi(TalkBandObs, m, 0, x, y, UiBlend.Alpha, fade: 24 / 31.0);
            for (int m = 0; m <= 2; m++) DrawUi(TalkBandObs, m, 0, x, y, UiBlend.Alpha);
            DrawText(choices[i], x + 12, y + 10, i == _fieldChoicePick ? 0xFF00FFFF : White, 13);
        }
    }

    /// <summary>필드 행동 412 — 화면을 회색조로(회상 장면).</summary>
    private bool _fieldGray;

    /// <summary>회색조 — 원본 팔레트 변환 <c>(7R + 2G + B) / 10</c>(0x100262e0). 필드를 다 그린 뒤 화면 네모 안을 바꾼다.</summary>
    private void ApplyFieldGray()
    {
        if (!_fieldGray) return;
        for (int y = _camY; y < _camY + ViewHeight; y++)
            for (int x = _camX; x < _camX + ViewWidth; x++)
            {
                int i = y * BoardWidth + x;
                uint c = _fb[i];
                uint g = (7 * (c >> 16 & 0xFF) + 2 * (c >> 8 & 0xFF) + (c & 0xFF)) / 10;
                _fb[i] = 0xFF000000 | g << 16 | g << 8 | g;
            }
    }

    /// <summary>
    /// 필드 행동 409 로 물결을 켠 필드/챕터 개체 — null 이면 꺼짐. 원본 깃발 <c>+0x294</c> 는 필드 개체 생성자 <c>0x100ec0a0</c>(<c>0x100ec180</c>)만 0 으로 되돌리고,
    /// 그 생성자는 필드 장면(<c>0x100ebbc0</c>)과 챕터 Chp 읽기(<c>0x100f755e</c>) 둘 다에서 불린다 — 곧 새 필드·챕터를 열면 꺼진다.
    /// 켠 주인이 지금 화면의 필드/챕터가 아니면 꺼진 것으로 본다(새 챕터를 열면 저절로 꺼짐).
    /// </summary>
    private object? _screenWaveOwner;

    /// <summary>물결 사인표 <c>0x101bf848</c>(필드, <c>0x100eba28~0x100eba60</c>) = 챕터 <c>0x101e8220</c> — <c>ftol(sin(i·3.141592/180)·1024 + 0.5)</c>, 0 쪽 자름.</summary>
    private static readonly int[] WaveSin = [.. Enumerable.Range(0, 360).Select(i => (int)(Math.Sin(i * 3.141592 * (1 / 180.0)) * 1024.0 + 0.5))];

    /// <summary>
    /// 화면 물결 <c>0x100eb8f0(P, T)</c> — 640×480 틀의 줄 y 를 <c>T[(4·tick + 479 − y) mod 360] &gt;&gt; 6</c> 픽셀(−16~16, 양수 = 오른쪽) 민다.
    /// 드러난 가장자리는 밀기 전 그 줄의 픽셀이 남는다(제자리 memmove <c>0x10129140</c>). 필드는 <c>0x100ec03d</c>(창 나무 다음, 커서 앞),
    /// 챕터는 <c>0x100f667b</c>(위상 = 챕터 장면 <c>+0x17c</c> × 4). 전투에는 없다.
    /// </summary>
    private void ApplyScreenWave(int ox, int oy, int tick)
    {
        if (_screenWaveOwner is null || !ReferenceEquals(_screenWaveOwner, _field is not null ? _field : _mosesChp)) return;
        var line = new uint[MosesW];
        int idx = (int)((long)tick * 4 % 360);
        if (idx < 0) idx += 360;
        for (int y = MosesH - 1; y >= 0; y--, idx = (idx + 1) % 360)   // 0x101bf844(줄 479) 부터 줄 0 까지
        {
            int s = WaveSin[idx] >> 6;                   // 산술 시프트 — 음수는 −∞ 쪽
            int fy = oy + y;
            if (s == 0 || ox < 0 || ox + MosesW > BoardWidth || fy < 0 || (fy + 1) * BoardWidth > _fb.Length) continue;
            int row = fy * BoardWidth + ox;
            Array.Copy(_fb, row, line, 0, MosesW);
            if (s > 0) Array.Copy(line, 0, _fb, row + s, MosesW - s);    // 오른쪽으로 — 왼쪽 s 픽셀은 그대로
            else Array.Copy(line, -s, _fb, row, MosesW + s);             // 왼쪽으로 — 오른쪽 |s| 픽셀은 그대로
        }
    }

    private void DrawField()
    {
        if (_field is null) return;
        DrawFieldBody();
        ApplyFieldGray();
    }

    private void DrawFieldBody()
    {
        var (ox, oy) = MosesOrigin();

        FillRect(_camX, _camY, ViewWidth, ViewHeight, 0xFF000000);
        // 필드 화면은 640×480 틀 안이 전부다 — 물체·인물이 그 밖으로 새지 않게 자른다.
        _uiClip = (ox, oy, MosesW, MosesH);

        // 원본 매 틀 그리기 0x100ebe20 의 차례(감사5 D4·D12):
        //  전환 물체가 있으면 그것 → 층 (가림)~7 을 산 채로(0x100ebe66~0x100ebea1),
        //  아니면 남은 그림 → 층 (가림)~7(0x100ebeb9~), 아니면 필드 나무 전체(0x100ec022),
        //  그다음 <b>창 나무(대사·고르기)</b>(0x100ec02a) — 대사창은 늘 전환·덮개 <b>위</b>다. 전에는 덮개를 대사창 뒤에 칠해
        //  검정 31단계면 대사가 새까맣게 사라졌다(Fld 0012 사건 3 끝 900[0,1,30,0,8] → 600 따위 약 150번).
        StepFieldFade();
        if (_fieldWipe is { } wipe && DrawFieldWipe(ox, oy))
            DrawFieldLayers(ox, oy, Math.Max(wipe.Cover, _fieldPicture != null ? _fieldPictureCover : 0), 9);
        else if (_fieldFade is not null)
        {
            // 덮기(900)가 가리는 층은 a4 까지다 — 그 위 층은 덮개보다 나중에 그려 안 가려진다.
            // 원본은 시작 때 찍은 사진을 섞지만(감사5 D11) 여기서는 산 화면 위에 덮개를 얹는다.
            int start = DrawFieldScene(ox, oy, _fieldFadeCover);
            DrawFieldFade(ox, oy);
            DrawFieldLayers(ox, oy, Math.Max(_fieldFadeCover, start), 9);
        }
        else DrawFieldScene(ox, oy, 9);

        DrawTalk();
        DrawFieldChoices();
        _uiClip = null;
        ApplyScreenWave(ox, oy, (int)(_lastTime * TicksPerSecond));   // 409 물결 — 대화창까지 일렁인다(0x100ec03d)
        DrawToast();
    }

    /// <summary>
    /// 전환·덮개 없는 필드 장면을 층 <paramref name="to"/> 아래까지 그린다 — 남은 그림이 있으면 그 그림과 층 (가림)~,
    /// 없으면 조각·배경과 층 0~. 그린 첫 층을 돌려준다.
    /// </summary>
    private int DrawFieldScene(int ox, int oy, int to)
    {
        int start = 0;
        if (_fieldPicture is { } picture)
        {
            // 남은 그림은 화면 (0,0)에 따로 그린다 — 필드 카메라는 안 건드린다(감사5 D8).
            for (int y = 0; y < MosesH; y++)
                for (int x = 0; x < MosesW; x++)
                    SetPixel(ox + x, oy + y, picture[y * MosesW + x] | 0xFF000000);
            start = _fieldPictureCover;
        }
        else DrawFieldBackdrop(ox, oy);
        DrawFieldLayers(ox, oy, start, to);
        return start;
    }

    /// <summary>색 키 노랑인가 — 원본 표면 키 0x7fe0(555)/0xffe0(565)(<c>0x10002f9c</c>·<c>0x10004154</c>). 565 로 줄였을 때 키와 같으면 뚫는다.</summary>
    private static bool IsFieldKey(uint c) => (c & 0x00F8FCF8) == 0x00F8FC00;

    /// <summary>
    /// 필드 배경 — B 덩이 조각을 <b>먼저</b> 그리고 배경은 노란 색 키를 뚫어 얹는다(감사5 D1).
    /// </summary>
    /// <remarks>
    /// 원본 배경 창은 <c>Blt(…, DDBLT_KEYSRC=0x8000)</c>(<c>0x100eb785</c>)로 찍혀 노랑(0x7fe0/0xffe0)이 뚫리고, 조각은 뿌리 창 머리에
    /// 끼워져(<c>0x10001650(…,1)</c>) 배경보다 먼저 — 곧 노란 창 너머로만 — 보인다. 조각의 내용은 카메라에 따라 시차로 밀린다:
    /// <c>조각+0x54 = (조각그림폭 − 조각네모폭) × 뿌리x / (배경폭 − 640)</c>(세로도 같은 꼴, 배경폭이 640 이면 0 — <c>0x100ee152~0x100ee28a</c>),
    /// 그림은 2×2 조각으로 감아 두른다(<c>0x100eb324~0x100eb65f</c>). <b>밀리는 방향(부호)과 네모의 화면 자리는 가설</b>이다 —
    /// 뿌리x = −카메라x 이므로 그림을 왼쪽으로 민다(그림 x = 네모 x + 시차)로 읽었다.
    /// 전에는 조각을 건너뛰고 배경을 불투명으로 칠해 22필드(Fld 0050·0108·0132·0207…)의 창이 샛노랬다.
    /// </remarks>
    private void DrawFieldBackdrop(int ox, int oy)
    {
        var (cx, cy) = _fieldCam;
        if (_fieldBg is not { } bg)
        {
            if (_mosesBg is { } crop)                        // 배경을 통째로 못 읽었으면 예전처럼 잘라 둔 것을
                for (int y = 0; y < MosesH; y++)
                    for (int x = 0; x < MosesW; x++)
                        SetPixel(ox + x, oy + y, crop[y * MosesW + x] | 0xFF000000);
            return;
        }
        // 조각 — 나중 것이 먼저(앞에 끼움).
        for (int i = _fieldPieces.Count - 1; i >= 0; i--)
        {
            var p = _fieldPieces[i];
            int shiftX = bg.W > MosesW ? (p.PicW - p.W) * cx / (bg.W - MosesW) : 0;
            int shiftY = bg.H > MosesH ? (p.PicH - p.H) * cy / (bg.H - MosesH) : 0;
            for (int y = 0; y < p.H; y++)
            {
                int sy = p.Y1 + y - cy;
                if ((uint)sy >= MosesH) continue;
                int py = ((y + shiftY) % p.PicH + p.PicH) % p.PicH;
                for (int x = 0; x < p.W; x++)
                {
                    int sx = p.X1 + x - cx;
                    if ((uint)sx >= MosesW) continue;
                    int px = ((x + shiftX) % p.PicW + p.PicW) % p.PicW;
                    SetPixel(ox + sx, oy + sy, p.Px[py * p.PicW + px] | 0xFF000000);
                }
            }
        }
        for (int y = 0; y < MosesH && y + cy < bg.H; y++)
        {
            int row = (y + cy) * bg.W + cx;
            for (int x = 0; x < MosesW && x + cx < bg.W; x++)
            {
                uint c = bg.Px[row + x];
                if (!IsFieldKey(c)) SetPixel(ox + x, oy + y, c | 0xFF000000);
            }
        }
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
        float x = FieldScreenAt(prop.Layer, prop.X, prop.Y).X;   // 좌우 소리(감사4 S1) — 640 틀 안 x
        foreach (var (start, sound) in clip.Sounds) _pendingSounds.Add((_lastTime + start / TicksPerSecond, sound, x));
        foreach (var (start, obs, motion, _, _, _, _) in clip.Children)
            if (UiFor(obs)?.Clip(motion) is { } child)
                foreach (var (s, sound) in child.Sounds) _pendingSounds.Add((_lastTime + (start + s) / TicksPerSecond, sound, x));
    }

    /// <summary>
    /// 소리를 매단 인물·물체(<c>10000+열쇠</c>)의 640 틀 화면 x — 없으면 NaN(가운데). 원본 <c>0x100f5300</c> 은 매단 물체의
    /// <c>+0x54</c> 를, 매단 것이 없으면 (320, 240) 을 쓴다.
    /// </summary>
    private float FieldSoundX(int target)
    {
        // 층 −1·숨은 인물은 화면에 없다(말하는 이로만 쓰인다, 자리도 (−1,−1)이 많다) — 가운데로 둔다(가설).
        if (FieldActorOf(target) is { } actor) return actor.Layer >= 0 && actor.Visible ? FieldScreenAt(actor.Layer, actor.X, actor.Y).X : float.NaN;
        if (FieldPropOf(target) is { } prop) return FieldScreenAt(prop.Layer, prop.X, prop.Y).X;
        return float.NaN;
    }

    /// <summary>그 층(−1~8)이 층 창으로 보이나 — 층 8 은 층 7 창 안(+0x170)이라 7 이 숨으면 함께 숨는다. −1 은 배경 아래라 안 보인다(감사5 D16·D17).</summary>
    private bool FieldLayerShown(int layer) =>
        layer is >= 0 and <= 8 && !_fieldLayerHidden[layer] && !(layer == 8 && _fieldLayerHidden[7]);

    /// <remarks>
    /// 층 창 0~7 을 차례로, <b>한 층 안에 물체·인물을 함께</b> 그린다(감사5 D3) — 차례 값(<c>Order</c>)이 작은 것부터:
    /// 파일 인물(뒤 번호부터) → 파일 물체(뒤 번호부터) → 213·307 로 옮겨 온 것(옮긴 차례). 302 새 물체는 층 7 맨 아래.
    /// 층 8 은 층 7 과 함께, 그 층의 맨 아래로 그린다(감사5 D17). 전에는 물체를 전 층 먼저, 인물을 그 뒤 전 층 그려
    /// 높은 층 물체(앞 기둥·등불)가 낮은 층 인물 뒤로 숨었다(178쌍/46필드).
    /// </remarks>
    private void DrawFieldLayers(int ox, int oy, int from, int to)
    {
        from = Math.Max(from, 0);
        var items = new List<(int Layer, int Sub, long Order, FieldProp? Prop, FieldActor? Actor)>();
        foreach (var prop in _fieldProps)
        {
            int eff = Math.Min(prop.Layer, 7);
            if (prop.Visible && FieldLayerShown(prop.Layer) && eff >= from && eff < to)
                items.Add((eff, prop.Layer == 8 ? 0 : 1, prop.Order, prop, null));
        }
        foreach (var actor in _fieldActors)
        {
            int eff = Math.Min(actor.Layer, 7);
            if (actor.Visible && FieldLayerShown(actor.Layer) && eff >= from && eff < to)
                items.Add((eff, actor.Layer == 8 ? 0 : 1, actor.Order, null, actor));
        }
        items.Sort((a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : a.Sub != b.Sub ? a.Sub.CompareTo(b.Sub) : a.Order.CompareTo(b.Order));
        foreach (var item in items)
        {
            if (item.Prop is { } prop) DrawFieldProp(ox, oy, prop);
            else if (item.Actor is { } actor) DrawFieldActor(ox, oy, actor);
        }
    }

    private void DrawFieldProp(int ox, int oy, FieldProp prop)
    {
        var (px, py) = FieldScreenAt(prop.Layer, prop.X, prop.Y);
        int tick = (int)((_lastTime - prop.Start) * TicksPerSecond);
        // 302 a2 ≠ 1 은 모션 끝 장에 선다 — 섞기 키(BlendAt)도 같은 틱으로(감사5 D15). 돌문(Obs 1233)이 옅어졌다 다시 불투명해지지 않게.
        if (prop.Hold && UiFor(prop.Obs)?.MotionLength(prop.Motion) is > 0 and var propLength)
            tick = Math.Min(tick, propLength - 1);
        // 모션의 섞기 키(종류 3) — 17 은 더하기 합성이다(분석-UI 「섞기 방식 17」). 등불 빛(Obs 528 따위)이 이것이라
        // 보통으로 그리면 검은 원판이 된다(마에라드 프롤로그 Fld 0036).
        int key = UiFor(prop.Obs)?.BlendAt(prop.Motion, tick) ?? 0;
        // 조명(Obs 0876)은 10 닷지·12 스크린(Fld 0058), 1~7 은 반투명 — 돌문(Obs 1233, Fld 0354)이 8→1 로 옅어지며 열린다.
        DrawUi(prop.Obs, prop.Motion, tick, ox + px, oy + py, BlendOf(key), loop: !prop.Hold, fade: BlendFade(key));
        // 모션에 붙은 자식 그림(키 종류 2) — 문이 열릴 때 번지는 빛(Obs 529 모션 3 → Obs 535, Fld 0037) 같은 것이 여기 있다.
        // 전에는 물체는 자식을 안 그려서 문이 소리 없이 열린 그림으로만 바뀌었다(사용자 보고). 자식은 모션을 건 때부터 한 번 돈다.
        DrawUnitLayers(UiFor(prop.Obs)?.Clip(prop.Motion), tick, ox + px, oy + py, prop.Mirror, loop: false);
    }

    private void DrawFieldActor(int ox, int oy, FieldActor actor)
    {
        if (_db?.Character(actor.ChrCode) is not { SpriteId: > 0 } pc) return;
        var (px, py) = FieldScreenAt(actor.Layer, actor.X, actor.Y);
        int actorTick = (int)(((actor.FrozenAt ?? _lastTime) - actor.MotionStart) * TicksPerSecond);
        if (actor.Hold && UiFor(pc.SpriteId)?.MotionLength(actor.Motion) is > 0 and var holdLength)
            actorTick = Math.Min(actorTick, holdLength - 1);   // 한 바퀴 돈 뒤 마지막 장에 멈춘다
        // 몸 모션이 더하기(17)면 그대로 — 전장의 몸 그림과 같은 규칙.
        var actorBlend = UiFor(pc.SpriteId)?.BlendAt(actor.Motion, actorTick) == 17 ? UiBlend.Add : UiBlend.Alpha;
        // 좌우반전(걷기 방향 3 · 208 인자 3 · 212)을 넘겨야 한다 — 빠져 있어 오른쪽으로 걷는 인물이 왼쪽을 보고 뒷걸음질 쳤다(사용자 보고: Fld 0076).
        DrawUi(pc.SpriteId, actor.Motion, actorTick, ox + px, oy + py, actorBlend, loop: true, fade: actor.Alpha, mirror: actor.Mirror);
    }

    private (int X, int Y) FieldScreenAt(int layer, double x, double y) =>
        layer < 0 ? ((int)x, (int)y) : ((int)x - _fieldCam.X, (int)y - _fieldCam.Y);

    /// <summary>
    /// 걷어내는 전환을 건다 — <paramref name="toPicture"/> 면 지금 화면에서 그림으로 가고(그림이 남는다),
    /// 아니면 그림에서 지금 화면으로 돌아온다.
    /// </summary>
    /// <remarks>
    /// <paramref name="cover"/> 는 전환이 가리는 층 수 — 사진에는 배경과 층 0~(가림−1)만 담고 윗층은 전환 위에 산 채로 그린다(감사5 D12).
    /// 카메라는 안 건드린다(감사5 D8). 돌아오는 쪽(a0 ≠ 0)은 먼저 남은 그림을 걷어 필드 배경을 되살리고, 그 필드 장면을 새로 그려
    /// 드러날 그림으로 쓴다(감사5 D9 — 전에는 바탕·덮개가 둘 다 그 그림이라 전환이 안 보이고 그림이 남았다: Fld 0111 사건 5·0376 사건 3).
    /// </remarks>
    private void BeginFieldWipe(int kind, int way, int ticks, bool toPicture, int background, int cover = 8)
    {
        cover = Math.Clamp(cover, 0, 8);
        _fieldFade = null;                                   // 전환 물체는 하나뿐이다([0x101c0014])
        uint[] under, over;
        if (toPicture)
        {
            under = RenderFieldShot(cover);                  // 지금 장면(남은 그림이 있으면 그 그림)
            var picture = ReadBackground(background);
            over = picture ?? under;
            // 그림으로 갔으면 전환이 끝난 뒤에도 그 그림이 남는다 — 남은 그림 + 가림.
            if (picture != null)
            {
                _fieldPicture = picture;
                _fieldPictureCover = cover;
            }
        }
        else
        {
            var picture = ReadBackground(background) ?? _fieldPicture;
            _fieldPicture = null;
            _fieldPictureCover = 0;
            over = RenderFieldShot(cover);                   // 되살린 필드 화면
            under = picture ?? over;
        }
        _fieldWipe = new FieldWipe(kind, way, ticks, _lastTime, under, over, cover);
        HoldSlotTicks(ticks);   // 전환이 끝날 때까지 슬롯 — 줄은 안 막는다(903→603 은 전환 중에 글이 뜬다). 뒤따르는 1 이 기다린다
    }

    /// <summary>지금 필드 화면(640×480)을 그대로 찍는다.</summary>
    private uint[] CaptureFieldScreen()
    {
        var (ox, oy) = MosesOrigin();
        var shot = new uint[MosesW * MosesH];
        for (int y = 0; y < MosesH; y++)
            Array.Copy(_fb, (oy + y) * BoardWidth + ox, shot, y * MosesW, MosesW);
        return shot;
    }

    /// <summary>
    /// 전환 사진 — 필드 장면(남은 그림 또는 조각·배경)과 층 <paramref name="cover"/> 아래까지만 뒷면에 그려 찍는다(<c>0x10022490</c>).
    /// 대사창·고르기·토스트는 안 들어간다(감사5 D12 — 전에는 지난 틀 화면 전부를 찍어 사진에 대사창이 박혔다).
    /// </summary>
    private uint[] RenderFieldShot(int cover)
    {
        var (ox, oy) = MosesOrigin();
        var saved = CaptureFieldScreen();
        var clip = _uiClip;
        FillRect(ox, oy, MosesW, MosesH, 0xFF000000);
        _uiClip = (ox, oy, MosesW, MosesH);
        DrawFieldScene(ox, oy, cover);
        _uiClip = clip;
        var shot = CaptureFieldScreen();
        for (int y = 0; y < MosesH; y++)
            Array.Copy(saved, y * MosesW, _fb, (oy + y) * BoardWidth + ox, MosesW);
        return shot;
    }

    /// <summary>전환이 도는 동안은 가림 아래가 전환 것이다 — 바탕을 깔고 걷힌 만큼 덮는 그림을 올린다. 끝났으면 false.</summary>
    private bool DrawFieldWipe(int ox, int oy)
    {
        if (_fieldWipe is not { } wipe) return false;
        int tick = (int)((_lastTime - wipe.Start) * TicksPerSecond);
        if (tick >= wipe.Ticks) { _fieldWipe = null; return false; }

        switch (wipe.Kind)
        {
            case 901: DrawSlideWipe(ox, oy, wipe, tick); break;
            case 903: DrawCombWipe(ox, oy, wipe, tick); break;
            case 909: DrawDissolveWipe(ox, oy, wipe, tick); break;
            default: DrawStreakWipe(ox, oy, wipe, tick); break;
        }
        return true;
    }

    /// <summary>
    /// 909 겹쳐 디졸브 — 바닥 그림 위에 다른 그림을 <c>k = t·8/a2 + 1</c> 단계(1~8)로 겹친다(<c>0x1002cc60</c>/<c>0x1002ccd0</c>, 감사5 D5).
    /// 방식 1~7 은 알파 4k/31, 8 은 불투명.
    /// </summary>
    private void DrawDissolveWipe(int ox, int oy, FieldWipe wipe, int tick)
    {
        int k = Math.Min(8, tick * 8 / Math.Max(1, wipe.Ticks) + 1);
        uint a = k >= 8 ? 256u : (uint)(4 * k * 256 / 31);
        var under = wipe.Base ?? wipe.Over;
        for (int y = 0; y < MosesH; y++)
        {
            int row = (oy + y) * BoardWidth + ox;
            if (oy + y < 0 || row + MosesW > _fb.Length || ox < 0) continue;
            for (int x = 0; x < MosesW; x++)
            {
                uint b = under[y * MosesW + x], o = wipe.Over[y * MosesW + x];
                uint Mix(int s) => ((b >> s & 0xFF) * (256 - a) + (o >> s & 0xFF) * a) >> 8;
                _fb[row + x] = 0xFF000000 | Mix(16) << 16 | Mix(8) << 8 | Mix(0);
            }
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

    /// <summary>
    /// 900 의 눈금(0~63) — 0 에서 <c>31/a2</c> 씩 31 까지(a2 = 0 이면 처음부터 31), 그다음 <c>31/a3</c> 씩 63 까지(a3 = 0 이면 곧장 63)
    /// (<c>0x1002a030</c>, 감사5 D10).
    /// </summary>
    private static int FieldFadeLevel(int tick, int coverTicks, int uncoverTicks)
    {
        if (tick < coverTicks) return Math.Min(31, 31 * tick / coverTicks);
        return uncoverTicks > 0 ? Math.Min(63, 31 + 31 * (tick - coverTicks) / uncoverTicks) : 63;
    }

    /// <summary>
    /// 눈금이 63 에 닿으면 페이드 물체가 끝난다(<c>+0x58 = 1</c>, <c>0x1002a0cb</c> → <c>0x100f26ee</c>): a0 ≠ 0 이면 그냥 걷히고,
    /// a0 = 0 이면 <b>단색 그림이 남은 그림으로 남는다</b>(가림 a4) — 그 뒤 405·900[1,…]·전환이 걷는다.
    /// 전에는 a0 를 안 보고 a2 > 0 이면 덮고 끝, a2 = 0 이면 a3 틱에 걸쳐 걷어, Fld 0132 의 흰 번쩍 [1,0,5,5] 이 흰 화면으로 굳고
    /// Fld 0015·0081 의 [0,…,0,a3] 은 덮여 있어야 할 화면이 다시 밝아졌다.
    /// </summary>
    private void StepFieldFade()
    {
        if (_fieldFade is not { } fade) return;
        int tick = (int)((_lastTime - fade.Start) * TicksPerSecond);
        if (FieldFadeLevel(tick, fade.CoverTicks, fade.UncoverTicks) < 63) return;
        _fieldFade = null;
        if (fade.Back) return;
        var solid = new uint[MosesW * MosesH];
        Array.Fill(solid, fade.Color);
        _fieldPicture = solid;
        _fieldPictureCover = _fieldFadeCover;
    }

    /// <summary>덮기·걷기 — 세기 = 눈금 &lt; 32 이면 눈금, 이상이면 a0 ≠ 0 일 때 63 − 눈금(걷기), a0 = 0 이면 31(덮인 채)(<c>0x1002a0e0</c>).</summary>
    private void DrawFieldFade(int ox, int oy)
    {
        if (_fieldFade is not { } fade) return;
        int tick = (int)((_lastTime - fade.Start) * TicksPerSecond);
        int level = FieldFadeLevel(tick, fade.CoverTicks, fade.UncoverTicks);
        int power = level < 32 ? level : fade.Back ? 63 - level : 31;
        if (power <= 0) return;

        uint color = fade.Color;
        for (int y = oy; y < oy + MosesH; y++)
            for (int x = ox; x < ox + MosesW; x++)
            {
                uint c = _fb[y * BoardWidth + x];
                uint Ch(int shift)
                {
                    int v = (int)(c >> shift & 0xFF), to = (int)(color >> shift & 0xFF);
                    return (uint)(v + (to - v) * power / 31);
                }
                _fb[y * BoardWidth + x] = c & 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
            }
    }
}
