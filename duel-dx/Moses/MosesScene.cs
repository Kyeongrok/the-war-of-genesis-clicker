using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 전투가 끝나면 나오는 모세스 단말 화면(mo-1) — 주 화면·항행·파티 페이지와 ESC 시스템 메뉴.
/// </summary>
/// <remarks>
/// 옵시디안 분석-모세스: 모세스는 챕터 장면(장면 4)이고 <b>배경은 영상이 아니라 <c>Bgr\NNNN.bgr</c>(640×480 JPEG) 한 장</b>이다.
/// <list type="bullet">
/// <item>주 화면 — 배경은 Chp 머리 둘째 워드(0010 → 52), 로고 Obs 0291 모션 1 @ (10,10),
/// 아이콘 여섯 개 Obs 0291 모션 4·6·8·10·12·23 을 칸 (50/105/160,400)·(25/80/135,435) 안 (+14,+19) 에. 칸 틀은 Obs 0246 세 조각.
/// 설명(TXR 485~490)은 마우스 +(16,16) 툴팁, 누르면 Snd 66.</item>
/// <item>항행 — 단계는 0 성계 · 1 행성 · 2 장소인데 챕터가 최저 단계를 정한다(Chp 머리 22번째 워드, 0010 = 2 → <b>장소 고르기부터</b>).
/// 배경은 성계 레코드 끝 워드(70), 행성 구체는 행성 레코드의 Obs·모션(0010 리치 = 0630 모션 1) @ (320,220),
/// 장소 칸은 여덟 자리 고정표에 Obs 0643 모션 0 단추 + Obs 0498 모션 2 아이콘 @ +(20,14) + 오른쪽 정렬 이름,
/// 칸은 10프레임 세로 와이프로 나타난다. 장소 값 v 는 &lt;10000 전투 · 10000+ 필드 · 20000+ 상점.</item>
/// <item>파티 — 배경은 주 화면과 같고 같은 칸 표의 두 칸(전직 TXR 1326 · 용병관리 1327, 아이콘 Obs 0498 모션 0·1).</item>
/// <item>ESC — 모세스 전용 시스템 메뉴 네 줄(LOAD·SAVE·VOLUME·EXIT GAME, 전투와 달리 MISSION·RESTART 가 없다), Snd 578.</item>
/// </list>
/// 페이지를 바꿀 때 나는 소리: 항행 564 · 파티 580 · 상점 572 · 전직 581 · 용병관리 583 · 뒤로는 항행 장소→행성 569 와 상점 나가기 576 뿐.
/// 원본은 페이지 사이에 Mov 영상과 UI 전환 효과를 거는데, 데모는 페이드로만 흉내 낸다 —
/// 메일·상점·파티·전직·용병관리·뒤로 대부분은 <b>효과 4 = 15틱 알파 크로스페이드</b>(<c>0x1002cc60</c>, 검정을 안 거친다),
/// 항행 → 주 화면과 행성 고르기는 검은 페이드(효과 2·3), <b>항행 진입(효과 1)만 30틱에 걸쳐 파랑 채널을 씻어 낸다</b>.
/// 아이콘 여섯은 주 화면·항행·메일·통신·파티 페이지에 늘 떠 있는 「도크」다(상점·전직·용병관리만 숨김, <c>0x100f968f</c>·<c>0x100fad23</c>·<c>0x100fb216</c>).
/// 640×480 화면을 우리 판 가운데에 1배로 놓는다. 페이지마다 그리는 코드는 GameWindow.Moses*.cs 로 나눠 두었다.
/// </remarks>
internal sealed unsafe partial class MosesScene(GameWindow host)
{
    internal const int MosesW = 640, MosesH = 480;
    internal const int MosesObs = 291, MosesFrameObs = 246, MosesBackObs = 248;
    internal const int MosesCellObs = 643, MosesCellIconObs = 498, MosesMarkObs = 163;
    internal const int MosesExitObs = 287, MosesMailBackground = 94;
    internal const int MosesClickSound = 66, MosesFadeTicks = 15;

    /// <summary>항행 진입(효과 1)만 <b>30틱</b>에 걸쳐 파랑을 씻어 낸다 — 나머지는 15틱 검정([[분석-모세스]] 2절).</summary>
    internal const int MosesBlueFadeTicks = 30;
    internal const int MosesChapter = 10;

    /// <summary>주 화면 아이콘 — 칸 왼위 자리와 Obs 0291 모션, 설명 TXR, 누르면 가는 페이지.</summary>
    internal static readonly (int X, int Y, int Motion, ushort Text, string Name, int Page)[] MosesIcons =
    [
        (50, 400, 4, 485, "NAVIGATION", 0),
        (105, 400, 6, 486, "MAIL", 1),
        (160, 400, 8, 487, "MESSAGE", 2),
        (25, 435, 10, 488, "ITEM SHOP", 3),
        (80, 435, 12, 489, "VT SHOP", 4),
        (135, 435, 23, 490, "PARTY", 5),
    ];

    /// <summary>항행·파티가 함께 쓰는 여덟 칸 자리(코드 안 상수) — 위아래로 퍼지는 활 모양.</summary>
    internal static readonly (int X, int Y)[] MosesCells =
        [(438, 180), (442, 220), (421, 140), (434, 260), (383, 100), (412, 300), (335, 60), (360, 340)];

    /// <summary>파티 페이지의 두 칸 — Obs 0498 아이콘 모션, 이름 TXR, 들어갈 때 소리.</summary>
    internal static readonly (int Icon, ushort Text, string Name, int Sound)[] MosesPartyItems =
        [(0, 1326, "전직", 581), (1, 1327, "용병관리", 583)];

    /// <summary>파티 칸을 누르면 하는 일 — 0 전직 페이지, 1 용병관리 페이지.</summary>
    internal void RunPartyItem(int index, string label)
    {
        if (index == 0) { OpenMosesStyle(); return; }
        if (index == 1) { OpenMosesLegion(); return; }
        host.Play(MosesPartyItems[index].Sound);
        host.Toast($"{label} — 아직 만들지 않았습니다");
    }

    internal bool _mosesOpen;
    internal uint[]? _mosesBg;
    internal int _mosesBgId = -1;
    internal (int X, int Y) _mosesBgAt;
    internal int _mosesHover = -1;
    internal int _mosesPage = -1;          // -1 주 화면 · 0 항행 · 5 파티
    internal int _mosesStep = 2;           // 항행 단계 — 1 행성 고르기 · 2 장소 고르기
    internal int _mosesPlanet;             // 고른 행성 번호

    /// <summary>
    /// 지금 보고 있는 항성계 번호 — 항행 단계 1 의 성도는 <b>그 항성계의 행성만</b> 보인다.
    /// </summary>
    /// <remarks>
    /// 챕터에 항성계가 여럿이면(11·12·13·16·40·44·45·46) 성도 좌우 단추로 옮긴다.
    /// 전에는 모든 항성계의 행성을 한 성도에 같이 그려, 서로 다른 항성계의 행성이 뒤섞였다(사용자 보고).
    /// </remarks>
    internal int _mosesSystem;

    /// <summary>지금 항성계 — 번호가 안 맞으면 첫 항성계.</summary>
    internal ChapterFile.StarSystem? MosesSystem() =>
        _mosesChp?.SystemOf(_mosesSystem) ?? _mosesChp?.Systems.FirstOrDefault();

    /// <summary>지금 항성계에 딸린 행성만 — 항성계가 없는 챕터는 행성 전부. 그리기·마우스가 매 틀 물어 와서 한 번 골라 두고 쓴다.</summary>
    internal IReadOnlyList<ChapterFile.Planet> MosesSystemPlanets()
    {
        if (_mosesChp is not { } chp) return [];
        if (_systemPlanetsOf == (chp.Id, _mosesSystem)) return _systemPlanets;
        _systemPlanetsOf = (chp.Id, _mosesSystem);
        return _systemPlanets = MosesSystem() is not { } sys || sys.Planets.Count == 0
            ? chp.Planets
            : [.. chp.Planets.Where(p => sys.Planets.Contains(p.No))];
    }

    /// <summary>그 행성에 지금 고를 수 있는 전투·필드 장소가 있나 — 상점뿐이면 아니다. 장소 목록(<see cref="MosesCellList"/>)과 같은 조건.</summary>
    internal bool PlanetHasMission(ChapterFile.Planet planet) =>
        _mosesChp is { } chp && planet.Places.Select(chp.PlaceOf).OfType<ChapterFile.Place>()
                                      .Any(p => p.Auto == 0 && !PlaceUsed(p) && host.FlagSt.PlaceOpen(p) && p.Kind != ChapterFile.PlaceKind.Shop);

    internal (int Chapter, int System) _systemPlanetsOf = (-1, -1);
    internal IReadOnlyList<ChapterFile.Planet> _systemPlanets = [];

    /// <summary>그 행성이 딸린 항성계 번호 — 못 찾으면 첫 항성계.</summary>
    internal int MosesSystemOfPlanet(int planet) =>
        _mosesChp?.Systems.FirstOrDefault(sy => sy.Planets.Contains(planet))?.No
        ?? _mosesChp?.Systems.FirstOrDefault()?.No ?? 0;

    /// <summary>다른 항성계 단추 그림 — Obs 0680 모션 0(왼쪽)·1(오른쪽), 20×50 반쪽 화살(0x100fd4f2~0x100fd670).</summary>
    internal const int MosesSystemObs = 680;

    /// <summary>다른 항성계 단추 두 자리 — 첫째 (30,40) id 127 · 둘째 (630,40) id 128.</summary>
    internal static readonly (int X, int Y)[] MosesSystemButtons = [(30, 40), (630, 40)];

    /// <summary>
    /// 항행 단계 1 의 다른 항성계 단추가 가리키는 항성계 — 항성계 표를 차례로 훑어 <b>조건을 통과하고 지금 성계가 아닌 것</b> 앞의 둘
    /// (원본 <c>0x100fd4f2</c>~<c>0x100fd670</c>). 항성계가 둘인 챕터는 왼쪽 단추 하나뿐이다.
    /// 조건 있는 항성계는 Chp 0016 가브리엘(깃발 184 == 1)·Chp 0044 아르케(깃발 209 == 1) — 깃발 전에는 못 간다(감사5 N5).
    /// </summary>
    internal List<ChapterFile.StarSystem> MosesOtherSystems() =>
        _mosesChp is not { } chp ? []
            : [.. chp.Systems.Where(s => s.No != _mosesSystem && s.Conditions.All(c => c.Variable < 0 || MosesFlagTest(c.Variable, c.Value, c.Operator))).Take(2)];

    /// <summary>깃발 비교 <c>0x100fda40(flags[변수], 값, 연산자)</c> — 0 == · 1 != · 2 &lt; · 3 &lt;= · 4 &gt; · 5 &gt;=, 6 이상은 거짓. 깃발 0 도 진짜 깃발이다.</summary>
    internal bool MosesFlagTest(int variable, int value, int op)
    {
        if ((uint)variable >= host.FlagSt._flags.Length) return false;
        int now = host.FlagSt._flags[variable];
        return op switch { 0 => now == value, 1 => now != value, 2 => now < value, 3 => now <= value, 4 => now > value, 5 => now >= value, _ => false };
    }

    /// <summary>다른 항성계 단추 i 의 누름 칸 — 그림 사각형(없으면 기준점 가운데 20×50). 판 좌표.</summary>
    internal (int X, int Y, int W, int H) MosesSystemButtonRect(int i, int ox, int oy)
    {
        var (x, y) = MosesSystemButtons[i];
        return host.UiFor(MosesSystemObs)?.FrameAt(i, 0) is { W: > 0 } f
            ? (ox + x + f.X, oy + y + f.Y, f.W, f.H)
            : (ox + x - 10, oy + y - 25, 20, 50);
    }

    /// <summary>←·→ 키 — 왼쪽 단추(첫째)·오른쪽 단추(둘째, 없으면 첫째) 성계로. 조건은 단추와 같게 지킨다.</summary>
    internal void MosesTurnSystem(int step)
    {
        if (_mosesSystemSwitch != null) return;
        var others = MosesOtherSystems();
        if (others.Count == 0) return;
        MosesSwitchSystem(others[step < 0 || others.Count == 1 ? 0 : 1].No);
    }

    /// <summary>
    /// 다른 항성계로 옮긴다 — 원본(<c>0x100ff49e</c>~<c>0x100ff5ed</c>): Snd 565 → 큐 10100(<b>100틱 대기</b>) → 떠나는 영상·오는 영상
    /// → 효과 4 → 큐 20566 = <b>Snd 566</b>(DLL 에서 566 을 넣는 곳은 여기 하나, <c>0x100ff5d7</c>). 영상은 생략하고(원본차이 F9)
    /// 100틱 입력을 막은 뒤 크로스페이드로 바꾼다. (챕터 +0x2ec4 가 서 있으면 565 대신 590 이지만 그 표시는 데모에 없다.)
    /// </summary>
    internal void MosesSwitchSystem(int system)
    {
        host.Play(565, WarpVoiceTag);
        _mosesHover = -1;
        _mosesSystemSwitch = (system, host._lastTime + 100 / TicksPerSecond);
    }

    /// <summary>기다리던 항성계 옮기기 — 100틱이 지나면 새 성계를 차린다. 방금 그린 화면에서 크로스페이드하려고 DrawMoses 끝에서 부른다.</summary>
    internal void StepMosesSystemSwitch()
    {
        if (_mosesSystemSwitch is not { } sw || host._lastTime < sw.At) return;
        _mosesSystemSwitch = null;
        _mosesSystem = sw.System;
        _mosesHover = -1;
        StartFade();
        ShowMosesBackground(MosesSystem()?.Background ?? 70);
        host._pendingSounds.Add((host._lastTime + MosesFadeTicks / TicksPerSecond, 566));   // 효과 4 뒤 큐 20566
    }

    /// <summary>워프 안내 음성(Snd 565)에 붙이는 꼬리표 — 누르면 끊으려고.</summary>
    internal const int WarpVoiceTag = 5650;

    /// <summary>기다리는 항성계 옮기기 — (성계, 바꿀 때).</summary>
    internal (int System, double At)? _mosesSystemSwitch;

    internal int _mosesFade;               // 남은 페이드 틱
    internal bool _mosesBlueFade;          // 항행 진입(효과 1)은 검정이 아니라 파랑 씻김이다
    internal bool _mosesBlackFade;         // 효과 2·3 — 검정에서 밝아짐(항행 → 주 화면, 행성 고르기)
    internal uint[]? _mosesFadeFrom;       // 효과 4 크로스페이드의 전 화면(판 가로 전부 × 보이는 줄)
    internal double _mosesPageAt;          // 페이지를 연 때(칸 와이프용)
    internal ChapterFile? _mosesChp;

    /// <summary>
    /// 페이지 전환 효과를 건다 — <paramref name="blue"/> 면 항행 진입용 30틱 파랑 씻김(효과 1), <paramref name="black"/> 면 15틱 검은 페이드(효과 2·3),
    /// 아니면 <b>효과 4 = 15틱 알파 크로스페이드</b>(<c>0x1002cc60(전 화면, 새 화면, 15)</c>, 감사5 P1) — 지금 판(마지막으로 그린 화면)을 떠 둔다.
    /// 단추가 미끄러져 들어오는 시계도 효과가 끝날 때로 맞춘다(모세스 장면은 큐가 비어야 그린다).
    /// </summary>
    internal void StartFade(bool blue = false, bool black = false)
    {
        _mosesBlueFade = blue;
        _mosesBlackFade = black;
        _mosesFade = blue ? MosesBlueFadeTicks : MosesFadeTicks;
        _mosesFadeFrom = null;
        if (!blue && !black && host._fb.Length >= (host._camY + host.ViewHeight) * host.BoardWidth && host._camY >= 0)
            _mosesFadeFrom = host._fb.AsSpan(host._camY * host.BoardWidth, host.ViewHeight * host.BoardWidth).ToArray();
        ResetMosesSlide(_mosesFade);
    }

    // ── 단추 클래스 0x101042e0 — 미끄러져 들어오기(B3)·올린 것만 움직임(B2) ─────────────────────

    internal const int SlideTrailObs = 6;
    internal double _mosesSlideAt, _mosesSceneAt;   // 페이지 단추 · 도크 아이콘이 들어오기 시작하는 때
    internal int _mosesSlideSeed, _mosesSceneSeed;
    internal double _mosesHoverAt, _mosesDockHoverAt;
    internal int _mosesDockHover = -1;

    /// <summary>
    /// 항행을 한 번이라도 열었나 — 원본 항행 단계 <c>+0x2e6a ≠ −1</c>. 메일·통신·상점·파티에서 뒤로 가면 이것이 서 있을 때 항행으로 돌아간다
    /// (<c>0x10101a90</c>, 감사5 D2). 항행에서 주 화면으로 나가면 내린다.
    /// </summary>
    internal bool _mosesNavVisited;

    /// <summary>페이지 단추들이 <paramref name="delayTicks"/> 뒤부터 새로 미끄러져 들어오게 한다.</summary>
    internal void ResetMosesSlide(int delayTicks)
    {
        _mosesSlideAt = host._lastTime + delayTicks / TicksPerSecond;
        _mosesSlideSeed = host.Btl._ailmentRandom.Next();
    }

    /// <summary>
    /// 단추 하나의 들어오기(<c>0x10104404</c>·<c>0x101045c0</c>): 시작 x = 640 + 24 + 80·(rand&amp;7), 틱마다 −20 px,
    /// 자리에 닿으면 10틱 세로 와이프(높이 × n/10). 와이프가 10 이 되기 전엔 안 눌린다(<c>0x10104880</c>). (지금 x, 와이프 0~10)
    /// </summary>
    internal static (int X, int Wipe) MosesSlide(int key, int targetX, double sinceTicks, int seed)
    {
        int start = MosesW + 24 + 80 * (int)((uint)HashCode.Combine(seed, key) & 7);
        if (sinceTicks < 0) return (start, 0);
        int x = start - 20 * (int)sinceTicks;
        if (x > targetX) return (x, 0);
        int arrive = (start - targetX + 19) / 20;
        return (targetX, Math.Clamp((int)sinceTicks - arrive, 0, 10));
    }

    internal (int X, int Wipe) PageSlide(int key, int targetX) =>
        MosesSlide(key, targetX, (host._lastTime - _mosesSlideAt) * TicksPerSecond, _mosesSlideSeed);

    /// <summary>도크 아이콘이 들어오는 빠르기 — 원본의 두 배(사용자 요청 mo-6: 챕터를 열 때마다 기다리는 것이 길다).</summary>
    internal const int DockSlideSpeed = 2;

    internal (int X, int Wipe) DockSlide(int key, int targetX) =>
        MosesSlide(key, targetX, (host._lastTime - _mosesSceneAt) * TicksPerSecond * DockSlideSpeed, _mosesSceneSeed);

    /// <summary>도크 아이콘이 아직 들어오는 중인가 — 하나라도 다 안 나왔으면.</summary>
    internal bool DockSliding => MosesDockShown && Enumerable.Range(0, MosesIcons.Length).Any(i => DockSlide(1000 + i, MosesIcons[i].X).Wipe < 10);

    /// <summary>도크 아이콘이 들어오는 중이면 곧바로 다 들어온 것으로 한다(클릭으로 건너뛰기, mo-6). 건너뛰었으면 true.</summary>
    internal bool SkipDockSlide()
    {
        if (!DockSliding) return false;
        _mosesSceneAt = host._lastTime - 60;
        return true;
    }

    /// <summary>들어오는 단추의 꼬리 — Obs 0006 24개를 (x+i−24) 에, 제각기 i/2+12 장(<c>0x1010444a</c>). 모세스 네모 밖으로는 안 그린다.</summary>
    internal void DrawSlideTrail(int ox, int oy, int x, int y)
    {
        var saved = host._uiClip;
        host._uiClip = (ox, oy, MosesW, MosesH);
        for (int i = 0; i < 24; i++)
            host.DrawUi(SlideTrailObs, 0, i / 2 + 12, x + i - 24, y, GameWindow.UiBlend.Add, loop: false);
        host._uiClip = saved;
    }

    /// <summary>단추 그림 — 와이프 n/10 만큼 위에서부터 보인다. 다 나왔으면 그대로 그린다.</summary>
    internal void DrawButtonUi(int obs, int motion, int tick, int x, int y, int wipe)
    {
        if (wipe >= 10) { host.DrawUi(obs, motion, tick, x, y, GameWindow.UiBlend.Alpha); return; }
        if (wipe <= 0 || host.UiFor(obs)?.FrameAt(motion, tick) is not { } f) return;
        var saved = host._uiClip;
        host._uiClip = (x + f.X, y + f.Y, f.W, f.H * wipe / 10);
        host.DrawUi(obs, motion, tick, x, y, GameWindow.UiBlend.Alpha);
        host._uiClip = saved;
    }

    /// <summary>올린 단추만 움직인다 — 평소는 첫 장에 멈춰 두고(<c>+0x28 = −1</c>, <c>0x1010448a</c>), 올리면 처음부터(<c>0x10104990</c>/<c>0x101049c0</c>).</summary>
    internal int HoverTick(bool hovered, double since) => hovered ? (int)((host._lastTime - since) * TicksPerSecond) : 0;

    internal (int X, int Y) MosesOrigin() => (host._camX + (host.ViewWidth - MosesW) / 2, host._camY + (host.ViewHeight - MosesH) / 2);

    /// <summary>DUELDX_MOSES=1 이면 전투를 기다리지 않고 바로 모세스 화면을 연다(화면 밖 시험용).</summary>
    internal void OpenMosesIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_MOSES") != "1") return;
        // DUELDX_FLAGS=5=1,7=2 면 진행 깃발을 미리 세운다(화면 밖 시험용 — 깃발로 잠긴 챕터 사건·장소를 본다).
        foreach (string pair in (Environment.GetEnvironmentVariable("DUELDX_FLAGS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (pair.Split('=') is [var f, var v] && int.TryParse(f, out int flag) && int.TryParse(v, out int val) && (uint)flag < host.FlagSt._flags.Length)
                host.FlagSt._flags[flag] = (byte)val;
        // DUELDX_CHAPTER=<Chp 번호> 면 그 챕터로 연다(화면 밖 시험용) — 없으면 첫 전투의 챕터.
        OpenMoses(int.TryParse(Environment.GetEnvironmentVariable("DUELDX_CHAPTER"), out int chapterId) ? LoadChapterFile(chapterId) : null);
        // DUELDX_MOSESPAGE=<페이지> 면 프롤로그를 건너뛰고 그 페이지를 바로 연다(화면 밖 시험용) — 7 전직 · 6 용병관리 · 3 상점.
        if (!int.TryParse(Environment.GetEnvironmentVariable("DUELDX_MOSESPAGE"), out int page)) return;
        if (host.FieldOpen) host.Fld.LeaveField();
        switch (page)
        {
            case 7: OpenMosesStyle(); break;
            case 6: OpenMosesLegion(); break;
            case 3: OpenMosesShop(0); break;
            case 4: OpenMosesShop(1); break;                            // VT 상점
            case 1 or 2: MosesGoPage(page); _mosesPage = page; break;   // 메일 · 통신
            case 0: MosesGoPage(0); _mosesPageAt = host._lastTime - 30.0 / TicksPerSecond; break;   // 항행 — 칸이 다 나온 뒤 모습
            case 5: MosesGoPage(5); break;                                                     // 파티
        }
        // DUELDX_MOSESSTEP=1 이면 항행을 행성 고르기(단계 1)로 연다(화면 밖 시험용 — 행성 설명·항성계 단추).
        if (page == 0 && Environment.GetEnvironmentVariable("DUELDX_MOSESSTEP") == "1")
        {
            _mosesStep = 1;
            ShowMosesBackground(MosesSystem()?.Background ?? 70);
        }
        // 시험 훅으로 연 페이지는 단추·도크가 이미 다 들어온 모습으로(들어오기는 최대 약 55틱).
        _mosesSlideAt = _mosesSceneAt = host._lastTime - 80 / TicksPerSecond;
        // DUELDX_MOSESCLICKS=x,y@초;x,y@초… 면 그 때 모세스 화면의 그 자리를 누른다(화면 밖 시험용 — 뒤로 가는 곳·도크·스크롤).
        foreach (string item in (Environment.GetEnvironmentVariable("DUELDX_MOSESCLICKS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            if (item.Split('@') is [var at, var sec] && at.Split(',') is [var cx, var cy]
                && int.TryParse(cx, out int clickX) && int.TryParse(cy, out int clickY) && double.TryParse(sec, out double seconds))
                _mosesTestClicks.Add((host._lastTime + seconds, clickX, clickY));
        // DUELDX_MOSESHOVER=x,y 면 마우스가 모세스 화면의 그 자리에 있는 것처럼 둔다(화면 밖 시험용 — 툴팁·올림 그림).
        // 창이 화면 밖이라도 WM_MOUSEMOVE 가 한 번씩 와서 덮으므로 틀마다 다시 둔다(DrawMoses).
        if (Environment.GetEnvironmentVariable("DUELDX_MOSESHOVER")?.Split(',') is [var hx, var hy] && int.TryParse(hx, out int mx) && int.TryParse(hy, out int my))
            _mosesTestHover = (mx, my);
    }

    /// <summary>전투가 끝나고 배너를 넘기면 모세스 화면으로 간다. 챕터를 주면 그 챕터로.</summary>
    /// <summary>
    /// 행동 910 이 켜는 깃발([챕터+0x2ec4]) — 모세스 안내 음성 11개가 +25 번(562 → 587 … 576 → 601)으로 바뀌고 화면 줄이 찢긴다.
    /// 새 챕터를 열면 꺼진다(0x100fe038). 세이브에는 아직 안 싣는다(원본은 진행 세이브에 싣는다).
    /// </summary>
    internal bool _mosesAltVoice;

    /// <summary>방금 불러온 세이브의 910 깃발 — 뒤따르는 OpenMoses(챕터) 가 한 번 쓰고 지운다.</summary>
    internal bool _mosesAltVoiceLoaded;

    internal static readonly HashSet<int> MosesAltSounds = [562, 564, 565, 566, 569, 570, 571, 572, 574, 575, 576];

    internal void OpenMoses(ChapterFile? chapter = null)
    {
        // 새 챕터를 열면 꺼진다 — 다만 불러오기가 챕터를 다시 여는 길이면 세이브에 적힌 값으로.
        if (_mosesAltVoiceLoaded) _mosesAltVoice = true;
        else if (chapter != null) _mosesAltVoice = false;
        _mosesAltVoiceLoaded = false;
        _mosesOpen = true;
        _mosesHover = -1;
        _mosesPage = -1;
        // 전투는 여기서 닫힌다 — 판을 전투 맵 크기에서 640×480 틀로 되돌려 모세스만 남긴다.
        // (안 그러면 전투 맵 크기 창 한가운데에 모세스가 뜨고 둘레가 검게 남는다.)
        if (host.Cols != TitleBoardCols || host.Rows != TitleBoardRows) host.ResizeBoard(TitleBoardCols, TitleBoardRows);
        _mosesDockHover = -1;
        _mosesSystemSwitch = null;
        // 도크 아이콘은 장면을 만들 때(전투·필드에서 돌아올 때마다) 한 번 미끄러져 들어온다 — 화면이 밝아지는 15틱 뒤부터(0x100fe4e4).
        _mosesSceneAt = host._lastTime + MosesFadeTicks / TicksPerSecond;
        _mosesSceneSeed = host.Btl._ailmentRandom.Next();
        if (chapter != null) { _mosesChp = chapter; _navStart = null; _mosesNavVisited = false; host.Play(562); }   // 챕터 들어오기 안내 음성(3초, 분석-모세스 14절) · 항행 시작은 파일 값부터(0x100f6c80)
        // 챕터마다 주인 파티가 있다(Episode.dat 칸 8) — 연대표를 거치지 않고 열어도(챕터 고르기·시험 훅) 그 파티로 바꾼다.
        if (_mosesChp is { } owner && host.EpisodesScr.Episodes().FirstOrDefault(e => e.Chapter == owner.Id) is { } ep) host.PartySt.SwitchParty(ep.Party);
        // 챕터가 끝났으면(필드 행동 11) 항행 화면 대신 연대표로 — 원본 0x100f5b07: 챕터 상태 +0x10 이 서 있으면 장면 7.
        // 다음 에피소드는 진행 깃발(Episode.dat 잠금 깃발 넷)이 다 서 있어야 열린다. 표시는 에피소드를 고를 때 내린다.
        // (전에 있던 「상점 뺀 장소를 다 쓰면 챕터 끝」 데모 규칙은 뺐다 — 아벨리안(Chp 21)·계시(Chp 52)의 끝 대사와 함정(Chp 47) 본편을
        //  건너뛰었다. 옛 세이브 구제는 불러오기 쪽 규칙(System.cs, 파티 칸 없는 세이브)이 맡는다. 감사 F5.)
        if (host.EpisodesScr._chapterDone)
        {
            _mosesOpen = false;
            host.StopMusic();
            host.EpisodesScr.OpenEpisodes();
            return;
        }
        // 원본 0x100f5b77: 장소가 하나도 없거나 <b>모든</b> 장소(상점·자동·잠긴 것 포함)의 +0x14(들어가 본 표시, 0x10101f30)가 서 있으면
        // 장면 6(타이틀)로 간다. 상점은 표시가 안 서니 상점 있는 챕터는 절대 안 탄다 — 상점 없는 챕터 23·57·47·53 에서만 드러난다.
        // 원본은 챕터 끝(→ 7) 갈래를 지나서도 이 검사를 돌아 7 을 6 이 덮는다. 그러면 달(Chp 53)은 선택 장소 23 까지 다 돌고
        // 끝내면 연대표 대신 타이틀로 가 버리는데, 이것은 원본 흠으로 보여 따르지 않는다 — 챕터가 끝났으면 위에서 이미 연대표로 갔다.
        if (_mosesChp is { } spent
            && (spent.Places.Count == 0 || spent.Places.All(p => _placesUsed.Contains((spent.Id, p.No)) || _autoPlacesDone.Contains((spent.Id, p.No)))))
        {
            _mosesOpen = false;
            host.StopMusic();
            host.TitleScr.OpenTitle();
            return;
        }
        LoadMosesChapter();
        host.Fld._fieldEvent = -1;                                    // 필드에서 돌던 실행기 자리를 비운다 — 챕터 스크립트가 처음부터 고른다
        host.Fld._fieldPc = 0;
        host.Fld._fieldReturn.Clear();
        host.Fld._fieldWaitUntil = 0;
        host.Fld._fieldChoices = null;
        // 동료·돈·아이템·깃발은 챕터 스크립트가 준다(대사 든 사건은 실행기가). 스크립트가 곧장 전투·필드로 떠나면(Chp 0057 사건 1 의 10[110]) 여기서 끝.
        if (_mosesChp is { } chp && host.Fld.RunChapterScript(chp)) return;
        if (EnterAutoPlace()) return;                       // 저절로 일어나는 장소(프롤로그 따위)가 먼저다
        ShowMosesBackground(_mosesChp?.Background ?? 52);
        host.StopMusic();
        // 챕터 BGM — Chp 머리 셋째 워드. 원본 챕터 장면은 논리 0 으로 걸고(0x100fe66f~0x100fe6d2), 화면이 15틀 밝아지는 동안
        // 0 → 6 → … → 84 % 로 올린 뒤 84 % 에 머문다(0x100f5f04~0x100f5f58, 감사4 M1). 전에는 100 % 로 바로 켰다.
        host.PlayMusicFile(_mosesChp?.Bgm ?? 19, loop: true, gain: 0);
        host.FadeMusic(84, MosesFadeTicks);
        // 주 화면에는 볼 것이 없어 늘 NAVIGATION 부터 눌러야 했다 — 열 때 항행 페이지를 누른 채로 연다(사용자 요청 mo-7, 원본은 주 화면에서 선다).
        if (_mosesChp != null) MosesGoPage(0, quiet: true);
    }

    /// <summary>아직 안 겪었고 조건이 열린 「자동 발생」 장소가 있으면 거기로 들어간다(<c>0x100fdd40</c>).</summary>
    /// <remarks>
    /// <c>Chp 0010</c> 은 이렇게 프롤로그(<c>Fld 0019</c>)로 먼저 들어가고, 그것이 첫 진행 깃발을 세운다.
    /// 한 번 겪은 장소는 다시 안 일어난다 — 원본은 레코드에 표시를 남기고, 데모는 <see cref="_autoPlacesDone"/> 에 적어 둔다.
    /// </remarks>
    internal bool EnterAutoPlace()
    {
        if (_mosesChp is not { } chp) return false;
        foreach (var place in chp.Places)
        {
            // 조건을 통과한 것만 「겪음」으로 표시한다 — 원본 0x100fdd40 은 실제로 들어갈 때만 +0x14=1 을 세운다(ba15-moses N1).
            // 전에는 조건 검사 전에 표시해 조건이 뒤늦게 열리는 자동 장소가 영영 안 떴다. 위 타이틀 규칙도 이 표시를 「들어가 봄」으로 센다.
            if (!place.IsAuto || _autoPlacesDone.Contains((chp.Id, place.No))) continue;
            if (!host.FlagSt.FlagsAllow(place.Conditions)) continue;
            _autoPlacesDone.Add((chp.Id, place.No));
            if (place.Value >= 10000 && place.Value < 20000 && host.Fld.OpenField(place.Value - 10000)) return true;
            if (place.Value > 0 && place.Value < 10000 && host.Sys.StartBattle(place.Value)) return true;
        }
        return false;
    }

    /// <summary>
    /// 필드 행동 911 이 정한 항행 시작 — (챕터, 단계, 번호). 원본 챕터 <c>+0x2e40/+0x2e42</c> 로, Chp 머리 값(StartStep/StartNumber)을 덮는다.
    /// </summary>
    /// <remarks>
    /// 원본은 이 두 워드를 세이브 레코드 <c>+0x190/+0x192</c> 에 싣는다(<c>0x100f5d70</c> 저장 · <c>0x100f57c0</c> 복원) — 데모 세이브에는 아직 안 싣는다
    /// (세이브 형식이 System.cs 에 있다 — 자료의 911 은 Chp 0050·0056·0064 셋 다 <c>[1, 0]</c>). 챕터를 새로 열면 지운다.
    /// </remarks>
    internal (int Chapter, int Step, int Number)? _navStart;

    /// <summary>방문 표시가 선 행성 — (챕터, 행성). 스크립트 조건 <c>505 [행성]</c> 이 한 번 참이 되면서 지운다(<c>0x100edcd0</c>). 세이브에 실린다.</summary>
    internal readonly HashSet<(int Chapter, int Planet)> _planetVisits = [];

    /// <summary>이미 겪은 자동 발생 장소 — (챕터, 장소).</summary>
    internal readonly HashSet<(int Chapter, int Place)> _autoPlacesDone = [];

    /// <summary>그 번호의 챕터 파일을 읽는다 — 없으면 null.</summary>
    internal static ChapterFile? LoadChapterFile(int id)
    {
        string path = Path.Combine(AssetsFolder.Find("moses"), "chp", $"{id:D4}.chp");
        return File.Exists(path) ? ChapterFile.Parse(id, File.ReadAllBytes(path)) : null;
    }

    internal void LoadMosesChapter()
    {
        if (_mosesChp != null) return;
        string path = Path.Combine(AssetsFolder.Find("moses"), "chp", $"{MosesChapter:D4}.chp");
        if (File.Exists(path)) _mosesChp = ChapterFile.Parse(MosesChapter, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 배경 그림(.bgr 은 그냥 JPEG)을 읽어 둔다. 필드 배경은 640×480 보다 넓어서
    /// <paramref name="srcX"/>·<paramref name="srcY"/> 부터 잘라 온다(필드 머리의 첫 화면 자리).
    /// </summary>
    internal void ShowMosesBackground(int id, int srcX = 0, int srcY = 0)
    {
        if (_mosesBgId == id && (srcX, srcY) == _mosesBgAt) return;
        if (ReadBackground(id, srcX, srcY) is not { } pixels) return;
        _mosesBgAt = (srcX, srcY);
        _mosesBg = pixels;
        _mosesBgId = id;
    }

    /// <summary>배경 그림 한 장을 640×480 낱칸으로 읽어 온다 — 화면에 걸지는 않는다(전환이 쓴다).</summary>
    internal uint[]? ReadBackground(int id, int srcX = 0, int srcY = 0)
    {
        try
        {
            if (AssetPack.Fetch("moses/bgr", $"{id:D4}.bgr") is not { } path) return null;
            using var bitmap = new Bitmap(path);
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int left = Math.Clamp(srcX, 0, Math.Max(0, bitmap.Width - MosesW));
                int top = Math.Clamp(srcY, 0, Math.Max(0, bitmap.Height - MosesH));
                var pixels = new uint[MosesW * MosesH];
                for (int y = 0; y + top < bitmap.Height && y < MosesH; y++)
                {
                    int width = Math.Min(MosesW, bitmap.Width - left);
                    var row = new Span<uint>((void*)(data.Scan0 + (y + top) * data.Stride + 4 * left), width);
                    row.CopyTo(pixels.AsSpan(y * MosesW, row.Length));
                }
                return pixels;
            }
            finally { bitmap.UnlockBits(data); }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
        {
            host._loadError = $"모세스 배경: {ex.Message}";
            return null;
        }
    }

    /// <summary>지금 페이지에 보이는 칸들 — (자리, 이름, 아이콘 모션, 누르면 할 일).</summary>
    /// <summary>항행 단계 2 후보 장소들의 레이더 자리(경도칸, 위도칸) — <see cref="MosesCellList"/> 와 같은 차례.</summary>
    internal List<(int Lon, int Lat)> MosesPlacePatches(ChapterFile chp, ChapterFile.Planet planet) =>
        [.. planet.Places.Select(chp.PlaceOf).OfType<ChapterFile.Place>()
                         .Where(p => p.Auto == 0 && !PlaceUsed(p) && host.FlagSt.PlaceOpen(p))
                         .Take(MosesCells.Length).Select(p => (p.Lon, p.Lat))];

    internal List<(int X, int Y, string Name, int Icon, Action Click)> MosesCellList()
    {
        var list = new List<(int, int, string, int, Action)>();
        if (_mosesPage == 0 && _mosesStep == 2 && _mosesChp is { } chp && chp.PlanetOf(_mosesPlanet) is { } planet)
        {
            // 자동 발생 장소는 목록에 없고, 진행 깃발 조건이 안 맞는 장소도 아직 안 열린 것이다(0x100fdaf0).
            foreach (var place in planet.Places.Select(chp.PlaceOf).OfType<ChapterFile.Place>()
                                               .Where(p => p.Auto == 0 && !PlaceUsed(p) && host.FlagSt.PlaceOpen(p)))
            {
                int i = list.Count;
                if (i >= MosesCells.Length) break;
                var (x, y) = MosesCells[i];
                int value = place.Value, no = place.No;
                list.Add((x, y, Text(place.NameText), 2, () => MosesEnterPlace(value, no)));
            }
        }
        else if (_mosesPage == 5)
            foreach (var (icon, text, name, sound) in MosesPartyItems)
            {
                int i = list.Count;
                var (x, y) = MosesCells[i];
                string label = Text(text) is { Length: > 0 } t ? t : name;
                int index = list.Count;
                list.Add((x, y, label, icon, () => RunPartyItem(index, label)));
            }
        return list;
    }

    internal string Text(int id) => id > 0 && host._db is { } db ? db.T((ushort)id) : "";

    /// <summary>장소를 누르면 — 값 &lt;10000 전투 · 10000+ 필드 · 20000+ 상점.</summary>
    /// <remarks>
    /// 전투·필드로 들어간 장소는 <b>그 자리에서 소모</b>된다(<c>0x10101e20</c> 이 장소 <c>+0x14</c> 를 1 로 두고 세이브에 싣는다) —
    /// 그래서 다녀오면 목록에서 사라진다. 상점은 장면이 안 바뀌므로 소모하지 않는다.
    /// </remarks>
    internal void MosesEnterPlace(int value, int no = -1)
    {
        // 장소 값 ≥ 20000 은 페이지 3 + 0x100fb1f0(0, n) 뿐 — 소리·효과가 없다(0x100feff3, 감사5 P2).
        if (value >= 20000) { OpenMosesShop(0, value - 20000, quiet: true); return; }
        // 전투·필드로 들어갈 때 모세스는 16틀 검게 나간다(0x100f6208, ba-21 outer #2) — 자료가 있을 때만(없으면 그 자리에서 알린다).
        string file = value >= 10000 ? Path.Combine(AssetsFolder.Find("data"), "Fld", $"{value - 10000:D4}.fld")
                                     : Path.Combine(AssetsFolder.Find("data"), "Btl", $"{value:D4}.btl");
        if (File.Exists(file)) { host.LeaveScene(() => MosesEnterPlaceNow(value, no)); return; }
        MosesEnterPlaceNow(value, no);
    }

    internal void MosesEnterPlaceNow(int value, int no)
    {
        if (value >= 10000)
        {
            if (host.Fld.OpenField(value - 10000)) { UsePlace(no); return; }
            host.Toast($"필드 {value - 10000} 자료가 assets 에 없습니다");
            return;
        }
        if (!host.Sys.StartBattle(value)) return;                  // 자료가 없으면 모세스에 그대로 남는다
        UsePlace(no);
        host.StopMusic();
        host.StartBattleMusic();
    }

    /// <summary>한 번 다녀온 장소 — (챕터, 장소 번호). 원본의 장소 <c>+0x14</c> 소모 표시다.</summary>
    internal readonly HashSet<(int Chapter, int Place)> _placesUsed = [];

    internal bool PlaceUsed(ChapterFile.Place place) => _placesUsed.Contains((_mosesChp?.Id ?? 0, place.No));

    internal void UsePlace(int no)
    {
        if (no >= 0 && _mosesChp is { } chp) _placesUsed.Add((chp.Id, no));
    }

    /// <summary>항행 자리(단계·행성·항성계)를 챕터가 정한 시작 값으로 놓는다 — 스크립트 911 이나 세이브가 정한 값(<c>_navStart</c>)이 있으면 그것.</summary>
    internal void ResetNavToStart()
    {
        // 챕터가 정한 시작 단계에서 연다 — Chp 0010 은 2(장소 고르기)라 행성 고르기를 지나간다.
        // 원본은 챕터 +0x2e40/+0x2e42(시작 단계·번호)를 쓰고(0x100fcf00), 머리 값(0x100f6c80)을 스크립트 행동 911 이 덮는다.
        var (startStep, startNo) = _navStart is { } nav && nav.Chapter == _mosesChp?.Id
            ? (nav.Step, nav.Number)
            : (_mosesChp?.StartStep ?? 1, _mosesChp?.StartNumber ?? 0);
        _mosesStep = Math.Max(1, startStep);
        _mosesPlanet = _mosesStep == 2 ? startNo : 0;
        // 단계 1 은 번호가 <b>항성계</b> 번호다 — 항성계 표에서 그 번호의 칸을 찾는다(0x100fa830). 없으면 첫 항성계.
        _mosesSystem = _mosesStep == 2 ? MosesSystemOfPlanet(_mosesPlanet)
                                       : _mosesChp?.Systems.FirstOrDefault(sy => sy.No == startNo)?.No
                                         ?? _mosesChp?.Systems.FirstOrDefault()?.No ?? 0;
    }

    /// <param name="quiet">항행 진입 소리(564)를 안 낸다 — 모세스를 열 때 저절로 항행으로 들어가는 경우(챕터 안내 음성 562 와 겹친다).</param>
    internal void MosesGoPage(int page, bool quiet = false)
    {
        switch (page)
        {
            case 0:
                // NAVIGATION — 항행 진입(명령 0, 0x100feb58~0x100feba0)은 큐 2 → −12 → 1 + Snd 564 뿐이다.
                // 566(큐 20566)은 항성계 옮기기 끝에만 난다(0x100ff5d7, 감사5 N8) — MosesSwitchSystem.
                if (!quiet) host.Play(564);
                _mosesNavVisited = true;
                ResetNavToStart();
                break;
            case 1:                                                    // MAIL — 새 편지가 왔으면 나는 소리(0x100fc6e0)
                if (DeliverMail() > 0) host.Play(571);
                _mailTop = 0;                                          // 목록 창은 들어갈 때마다 새로 만든다(0x100fc84e)
                _mailOpen = -1;
                break;
            case 2: break;                                             // MESSAGE — 소리 없음
            case 3 or 4: OpenMosesShop(page - 3); return;
            case 5: host.Play(580); break;                                  // PARTY
        }
        _mosesPage = page;
        _mosesPageAt = host._lastTime;
        _talkPick = -1;
        // 통신(명령 2, 0x100fec2a)은 큐에 아무것도 안 넣는다 — 효과 없이 곧장(감사5 P2). 항행은 파랑 씻김, 나머지는 효과 4.
        if (page == 2) { _mosesFade = 0; ResetMosesSlide(0); }
        else StartFade(page == 0);
        _mosesHover = -1;
        // 항행·통신은 지금 항행 항성계(+0x2e98)의 배경(0x100fc2d0, 감사5 T1), 메일은 94, 파티는 주 화면과 같은 챕터 배경
        ShowMosesBackground(page switch
        {
            0 or 2 => MosesSystem()?.Background ?? 70,
            1 => MosesMailBackground,
            _ => _mosesChp?.Background ?? 52,
        });
    }

    /// <summary>항행 페이지를 <b>지금 단계 그대로</b> 다시 차린다(<c>0x100fcf00(단계)</c>) — 메일·통신·상점·파티에서 뒤로 올 때. 소리 없음, 효과 4.</summary>
    internal void ReturnToMosesNav()
    {
        _mosesPage = 0;
        _mosesPageAt = host._lastTime;
        _mosesHover = -1;
        _talkPick = -1;
        CloseMailViewer();
        StartFade();
        ShowMosesBackground(MosesSystem()?.Background ?? 70);
    }

    /// <summary>
    /// 뒤로(<c>0x10101a90</c>, 표 <c>0x10101dfc</c>, 감사5 D2·D3):
    /// <list type="bullet">
    /// <item>항행 장소 고르기 → 행성 고르기, Snd 569, 효과 4.</item>
    /// <item>항행 최저 단계 → 주 화면, <b>무음</b>(원본 소리는 Mov 0015 영상 몫 — 영상 생략), 검은 페이드(효과 3).</item>
    /// <item>메일·통신·상점·파티 → 항행을 열어 두었으면 <b>항행</b>, 아니면 주 화면. 효과 4, 무음(상점만 Snd 576, <c>0x10101d26</c>).</item>
    /// <item>전직·용병관리 → <b>파티 페이지</b>(<c>0x10101d63</c>·<c>0x10101d8a</c>), 효과 4, 무음.</item>
    /// </list>
    /// 뒤로 <b>단추</b>를 누른 소리 66 은 단추 쪽(OnMosesClick)이 낸다 — Esc·우클릭으로 올 때는 안 난다.
    /// </summary>
    internal void MosesGoBack()
    {
        if (_mosesSystemSwitch != null || _mosesFade > 0 || PlanetZooming) return;   // 항성계 옮기기·페이드·행성 줌 중에는 입력 잠금(ba-20 G4)
        // 항행 단계 2(장소 고르기)에서는 늘 행성 고르기로 한 단계만 내려간다.
        // 전에는 챕터의 시작 단계(Chp 머리)가 2 보다 낮을 때만 내려갔는데, 그 값은 「들어갈 때 어디서 시작하나」일 뿐
        // 바닥이 아니다 — 챕터 10·14·19·21·22 는 행성이 여럿인데 시작 단계가 2 라, 그 규칙으로는 첫 행성에 갇혀
        // 다른 행성에 영영 못 갔다(사용자 보고: 우클릭하면 성계로 나가 다른 행성을 고를 수 있어야 한다).
        if (_mosesPage == 0 && _mosesStep == 2)
        {
            host.Play(569);
            _mosesStep = 1;
            _mosesSystem = MosesSystemOfPlanet(_mosesPlanet);
            ShowMosesBackground(MosesSystem()?.Background ?? 70);
            _mosesPageAt = host._lastTime;
            StartFade();
            _mosesHover = -1;
            return;
        }
        if (_mosesPage is 6 or 7)
        {
            _mosesPage = 5;
            _mosesPageAt = host._lastTime;
            _mosesHover = -1;
            StartFade();
            ShowMosesBackground(_mosesChp?.Background ?? 52);
            return;
        }
        if (_mosesPage is 3 or 4) host.Play(SoundShopLeave);
        if (_mosesPage != 0 && _mosesNavVisited) { ReturnToMosesNav(); return; }
        bool fromNav = _mosesPage == 0;
        if (fromNav) _mosesNavVisited = false;                 // 단계 = −1
        _mosesPage = -1;
        CloseMailViewer();
        _talkPick = -1;
        StartFade(black: fromNav);
        _mosesHover = -1;
        ShowMosesBackground(_mosesChp?.Background ?? 52);
    }

    /// <summary>아이콘 도크가 보이는 페이지 — 주 화면·항행·메일·통신·파티(상점·전직·용병관리만 숨긴다, 감사5 D1).</summary>
    internal bool MosesDockShown => _mosesPage is -1 or 0 or 1 or 2 or 5;

    /// <summary>마우스 밑 도크 아이콘 — 다 들어온(와이프 10) 것만.</summary>
    internal int MosesDockAt(int bx, int by)
    {
        if (!MosesDockShown) return -1;
        var (ox, oy) = MosesOrigin();
        for (int i = 0; i < MosesIcons.Length; i++)
        {
            var icon = MosesIcons[i];
            int x = ox + icon.X, y = oy + icon.Y;
            if (bx >= x && bx < x + 46 && by >= y && by < y + 33 && DockSlide(1000 + i, icon.X).Wipe >= 10) return i;
        }
        return -1;
    }

    internal int MosesIconAt(int bx, int by)
    {
        var (ox, oy) = MosesOrigin();
        if (_mosesPage == -1) return -1;
        if (_mosesPage == 0 && _mosesStep == 1)
        {
            var planets = MosesSystemPlanets();
            for (int i = 0; i < planets.Count; i++)
                if (Math.Abs(bx - ox - planets[i].X) <= 20 && Math.Abs(by - oy - planets[i].Y) <= 20) return i;
            return -1;
        }
        var cells = MosesCellList();
        for (int i = 0; i < cells.Count; i++)
            if (bx >= ox + cells[i].X && bx < ox + cells[i].X + 178 && by >= oy + cells[i].Y && by < oy + cells[i].Y + 27
                && PageSlide(10 + i, cells[i].X).Wipe >= 10)
                return i;
        return -1;
    }

    internal bool MosesBackAt(int bx, int by)
    {
        var (ox, oy) = MosesOrigin();
        return _mosesPage != -1 && bx >= ox + 46 && bx < ox + 71 && by >= oy + 244 && by < oy + 270 && PageSlide(100, 46).Wipe >= 10;
    }

    /// <summary>모세스 화면이 떠 있으면 클릭을 처리하고 true.</summary>
    /// <remarks>
    /// 아이콘·행성·장소 칸·항성계 단추·뒤로 단추·통신 인물은 모두 단추 클래스 <c>0x101042e0</c> 라 누르면 늘 Snd 66 이 난다
    /// (<c>0x10104950</c> → <c>0x10028850(0x42)</c>, 감사5 B1) — 처리기 쪽 소리(565·569·581…)는 그 위에 더해진다.
    /// </remarks>
    internal bool OnMosesClick(int bx, int by)
    {
        if (!_mosesOpen) return false;
        if (host.Sys.SystemOpen) return host.Sys.OnSystemClick(bx, by);
        if (host._statusUnit >= 0) return host.StatusScr.OnStatusClick(bx, by);     // 전직 페이지의 STATUS 로 연 스테이터스 창이 먼저 받는다
        // 워프 안내(「지금부터 워프를 시작합니다」, 100틱)를 기다리는 동안 누르면 곧바로 넘어간다 — 안내 음성도 끊는다(사용자 요청, 원본은 끝까지 기다린다).
        if (_mosesSystemSwitch is { } warp && _mosesFade <= 0)
        {
            _mosesSystemSwitch = (warp.System, host._lastTime);
            host._mixer.StopEffect(WarpVoiceTag);
            return true;
        }
        if (_mosesFade > 0 || _mosesSystemSwitch != null || PlanetZooming) return true;
        if (SkipDockSlide()) return true;   // 아이콘이 들어오는 동안의 클릭은 그 연출을 건너뛴다
        if (OnMosesShopClick(bx, by)) return true;
        // 편지 뷰어·통신 말풍선은 모달이라 떠 있으면 누름은 닫기만 한다 — 도크보다 먼저.
        if (_mosesPage == 1 && _mailOpen >= 0 && OnMosesMailClick(bx, by)) return true;
        if (_mosesPage == 2 && _talkPick >= 0 && OnMosesTalkClick(bx, by)) return true;
        // 도크 — 같은 페이지면 무시, 아니면 지금 페이지를 걷고 새 페이지(0x100febcd~).
        if (MosesDockAt(bx, by) is >= 0 and var dock)
        {
            host.Play(MosesClickSound);
            if (MosesIcons[dock].Page != _mosesPage) MosesGoPage(MosesIcons[dock].Page);
            return true;
        }
        if (OnMosesMailClick(bx, by)) return true;
        if (OnMosesStyleClick(bx, by)) return true;
        if (OnMosesLegionClick(bx, by)) return true;
        if (OnMosesTalkClick(bx, by)) return true;
        if (MosesBackAt(bx, by)) { host.Play(MosesClickSound); MosesGoBack(); return true; }
        // 다른 항성계 단추(127/128) — 조건을 통과한 다른 성계 앞의 둘, 누르면 <b>그 성계로</b>(0x100ff403~).
        if (_mosesPage == 0 && _mosesStep == 1)
        {
            var (sx, sy) = MosesOrigin();
            var others = MosesOtherSystems();
            for (int i = 0; i < others.Count; i++)
            {
                var r = MosesSystemButtonRect(i, sx, sy);
                if (bx < r.X || bx >= r.X + r.W || by < r.Y || by >= r.Y + r.H || PageSlide(127 + i, MosesSystemButtons[i].X).Wipe < 10) continue;
                host.Play(MosesClickSound);
                MosesSwitchSystem(others[i].No);
                return true;
            }
        }

        int index = MosesIconAt(bx, by);
        if (index < 0) return true;
        if (_mosesPage == 0 && _mosesStep == 1)
        {
            var picked = MosesSystemPlanets();
            if (_mosesChp is { } chp && index < picked.Count)
            {
                host.Play(MosesClickSound);
                _mosesPlanet = picked[index].No;
                _planetVisits.Add((chp.Id, picked[index].No));   // 행성 +0x5c 방문 표시(가설: 고를 때 선다) — 조건 505 가 한 번 먹고 지운다
                _mosesStep = 2;
                _mosesPageAt = host._lastTime;
                // 줌 연출(Obs 0561, 73틱, 0x10102210) — 검은 화면에 막대·조준 틀·▼ 가 차례로 뜨고 43틱에 장소 화면이 밝아진다(DrawPlanetZoom).
                _planetZoomAt = host._lastTime;
                _planetZoomFaded = false;
                _mosesHover = -1;
            }
        }
        else
        {
            var cells = MosesCellList();
            if (index < cells.Count) { host.Play(MosesClickSound); cells[index].Click(); }
        }
        return true;
    }

    internal void UpdateMosesHover(int bx, int by)
    {
        if (!_mosesOpen) return;
        int dock = MosesDockAt(bx, by);
        if (dock != _mosesDockHover) { _mosesDockHover = dock; _mosesDockHoverAt = host._lastTime; }
        int hover = MosesIconAt(bx, by);
        if (hover != _mosesHover) { _mosesHover = hover; _mosesHoverAt = host._lastTime; }
    }

    /// <summary>시험 훅 DUELDX_MOSESCLICKS 가 걸어 둔 누름 — (때, 모세스 x, 모세스 y).</summary>
    internal readonly List<(double At, int X, int Y)> _mosesTestClicks = [];

    /// <summary>시험 훅 DUELDX_MOSESHOVER 의 마우스 자리(모세스 좌표).</summary>
    internal (int X, int Y)? _mosesTestHover;

    /// <summary>
    /// 행성을 고르면 도는 줌 연출(ba-20 G10, 0x10102210 · 틱 표 0x10102858) — 화면이 검어지고 Obs 561 의 좌우 막대(모션 0·1, 틱 0),
    /// 조준 틀(모션 2, 틱 7), ▼(모션 3, 틱 12)가 (320,220) 에 차례로 뜬 뒤 43틱에 장소 화면이 밝아지고 73틱에 입력이 풀린다.
    /// 행성 줌 그림 쌍(행성 +0x4e~+0x54)과 구체는 아직 안 그린다. 0~43틱의 바탕이 검정이라는 것은 가설.
    /// </summary>
    internal double _planetZoomAt = -1;
    internal bool _planetZoomFaded;

    internal bool PlanetZooming => _planetZoomAt >= 0;

    internal void DrawPlanetZoom(int ox, int oy)
    {
        if (_planetZoomAt < 0) return;
        int t = (int)((host._lastTime - _planetZoomAt) * TicksPerSecond);
        if (t >= 73 || _mosesPage != 0 || !_mosesOpen) { _planetZoomAt = -1; if (!_planetZoomFaded) StartFade(black: true); return; }
        if (t < 43) host.FillRect(ox, oy, MosesW, MosesH, 0xFF000000);
        else if (!_planetZoomFaded) { _planetZoomFaded = true; StartFade(black: true); }
        // 줌 그림 쌍은 구체 Obs 의 모션 0(떠오르는 15틱 페이드, 틱 4~19 @ (320,220))과 모션 2(행성 이름표, 틱 40~ @ (320,350))다
        // (행성 +0x4e~+0x54 = Chp 꼬리 워드 4~7, 0x10102210 · ba-20 S 2). 틱 19 부터는 도는 구체.
        if (_mosesChp?.PlanetOf(_mosesPlanet) is { } zoomPlanet)
        {
            if (t >= 4 && t < 19) host.DrawUi(zoomPlanet.GlobeObs, 0, t - 4, ox + 320, oy + 220, GameWindow.UiBlend.Alpha, loop: false);
            else if (t >= 19 && t < 43) host.DrawUi(zoomPlanet.GlobeObs, zoomPlanet.GlobeMotion, (int)(host._lastTime * TicksPerSecond), ox + 320, oy + 220, GameWindow.UiBlend.Alpha);
            if (t >= 40) host.DrawUi(zoomPlanet.GlobeObs, 2, Math.Min(t - 40, 20), ox + 320, oy + 350, GameWindow.UiBlend.Alpha, loop: false);
        }
        foreach (var (motion, from) in new[] { (0, 0), (1, 0), (2, 7), (3, 12) })
            if (t >= from) host.DrawUi(561, motion, t - from, ox + 320, oy + 220, GameWindow.UiBlend.Add, loop: false);
    }

    internal void DrawMoses()
    {
        if (!_mosesOpen) return;
        var (ox, oy) = MosesOrigin();
        if (_mosesTestHover is { } th)
        {
            host._mouse = (ox + th.X, oy + th.Y);
            UpdateMosesHover(host._mouse.X, host._mouse.Y);
        }
        if (_mosesTestClicks.Count > 0 && _mosesTestClicks[0].At <= host._lastTime)
        {
            var click = _mosesTestClicks[0];
            _mosesTestClicks.RemoveAt(0);
            host._mouse = (ox + click.X, oy + click.Y);
            UpdateMosesHover(host._mouse.X, host._mouse.Y);
            OnMosesClick(host._mouse.X, host._mouse.Y);
            if (BattleScene.Trace) File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"moses click ({click.X},{click.Y}) → page {_mosesPage} step {_mosesStep} system {_mosesSystem}" + Environment.NewLine);
        }
        int tick = (int)(host._lastTime * TicksPerSecond);

        // 화면 밖은 검게, 가운데에 640×480 단말 화면
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        if (_mosesBg is { } bg)
            for (int y = 0; y < MosesH; y++)
                for (int x = 0; x < MosesW; x++)
                    host.SetPixel(ox + x, oy + y, bg[y * MosesW + x] | 0xFF000000);

        if (_mosesPage == -1) DrawMosesDesktop(ox, oy, tick);
        else
        {
            DrawMosesPage(ox, oy, tick);
            if (MosesDockShown) DrawMosesDock(ox, oy, tick);
        }

        DrawPlanetZoom(ox, oy);
        DrawMosesTooltip();
        // 챕터 스크립트의 대사·고르기 — 모세스 화면 위에(원본 창 +0x2ee0 「대사·말풍선 묶음」)
        host._uiClip = (ox, oy, MosesW, MosesH);
        host.Tlk.DrawTalk();
        host.Fld.DrawFieldChoices();
        host._uiClip = null;
        // 910 이 켠 동안 — 매 틀 무작위 줄 하나와 그 다음 줄이 10px 왼쪽으로 밀린다(0x100f65b0: rand() % 470).
        if (_mosesAltVoice)
        {
            // 틀(1/30초)마다 한 번 굴린다 — 그리기 틀이 더 잦아도 같은 줄이다.
            int tearY = new Random((int)(host._lastTime * TicksPerSecond) * 7919).Next(MosesH - 10);
            for (int ty = tearY; ty <= tearY + 1; ty++)
                Array.Copy(host._fb, (oy + ty) * host.BoardWidth + ox + 10, host._fb, (oy + ty) * host.BoardWidth + ox, MosesW - 10);
        }
        host.Fld.ApplyScreenWave(ox, oy, tick);   // 409 물결 — 챕터 화면에서도(0x100f667b, Chp 0059, audit3 R3)
        host.Sys.DrawSystem();
        host.StatusScr.DrawStatusScreen();   // 전직 페이지의 STATUS — 스테이터스 창도 모세스 위에 그린다
        if (host._statusUnit >= 0) host.Sys.DrawConfirm();   // 스테이터스가 띄운 확인창(어빌리티 지우기)은 그 창 위에
        host.DrawToast();   // 알림은 모세스 화면 위에 — Compose 의 DrawToast 는 이 화면에 가린다

        // 페이지 전환 — 원본 색표(분석-모세스 2절)대로: 보통은 효과 4 크로스페이드, 항행 → 주 화면·행성 고르기는 검정 페이드(방식 2),
        // 항행 진입(효과 1)만 파랑 채널을 흰색 쪽으로 씻었다가 되돌린다(방식 5).
        int fadeTotal = _mosesBlueFade ? MosesBlueFadeTicks : MosesFadeTicks;
        if (_mosesFade > 0 && !_mosesBlueFade && !_mosesBlackFade
            && _mosesFadeFrom is { } from && from.Length == host.ViewHeight * host.BoardWidth && host._fb.Length >= (host._camY + host.ViewHeight) * host.BoardWidth)
        {
            // 효과 4 — 전 화면과 새 화면을 15틱에 걸쳐 섞는다(0x1002cc60). 검정을 거치지 않는다(감사5 P1).
            int k = 256 * _mosesFade / fadeTotal;
            for (int y = 0; y < host.ViewHeight; y++)
                for (int x = 0; x < host.BoardWidth; x++)
                {
                    int i = (y + host._camY) * host.BoardWidth + x;
                    uint a = from[y * host.BoardWidth + x], b = host._fb[i];
                    uint Mix(int shift) => (uint)(((int)(a >> shift & 0xFF) * k + (int)(b >> shift & 0xFF) * (256 - k)) >> 8);
                    host._fb[i] = 0xFF000000 | Mix(16) << 16 | Mix(8) << 8 | Mix(0);
                }
            if (--_mosesFade == 0) _mosesFadeFrom = null;
        }
        else if (_mosesFade > 0)
        {
            int total = fadeTotal;
            int alpha = 31 * _mosesFade / total;
            // 원본은 640×480 이 화면 전부라 씻김도 화면 전부다 — 우리 판은 더 넓으니 그 네모 안에서만 씻는다.
            var (fx, fy) = MosesOrigin();
            int x0 = _mosesBlueFade ? Math.Max(0, fx) : 0;
            int x1 = _mosesBlueFade ? Math.Min(host.BoardWidth, fx + MosesW) : host.BoardWidth;
            int y0 = _mosesBlueFade ? Math.Max(host._camY, fy) : host._camY;
            int y1 = _mosesBlueFade ? Math.Min(host._camY + host.ViewHeight, fy + MosesH) : host._camY + host.ViewHeight;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int i = y * host.BoardWidth + x;
                    uint c = host._fb[i];
                    if (_mosesBlueFade)
                    {
                        uint blue = c & 0xFF;
                        blue += (255 - blue) * (uint)alpha / 31;
                        host._fb[i] = c & 0xFFFFFF00 | blue;
                    }
                    else host._fb[i] = GameWindow.ScaleColor(c, 31 - alpha, 31);
                }
            _mosesFade--;
        }
        StepMosesSystemSwitch();
    }

    internal void DrawMosesDesktop(int ox, int oy, int tick)
    {
        host.DrawUi(MosesObs, 1, tick, ox + 10, oy + 10, GameWindow.UiBlend.Alpha);   // 로고
        DrawMosesDock(ox, oy, tick);
    }

    /// <summary>
    /// 아이콘 여섯과 창틀 — 주 화면·항행·메일·통신·파티에 늘 떠 있다(감사5 D1).
    /// 창틀 Obs 0246 세 조각은 <b>여섯 칸 모두 늘 보이고 멈춰 있다</b>(<c>0x100fe2dd</c>~<c>0x100fe3f6</c>, vt+0xc0(−1), 감사5 B4).
    /// 아이콘은 단추 클래스라 오른쪽에서 미끄러져 들어오고(B3), 마우스를 올린 것만 움직인다(B2).
    /// </summary>
    internal void DrawMosesDock(int ox, int oy, int tick)
    {
        for (int i = 0; i < MosesIcons.Length; i++)
        {
            var icon = MosesIcons[i];
            int x = ox + icon.X, y = oy + icon.Y;
            foreach (var (dx, motion) in new[] { (-8, 0), (2, 1), (26, 2) })
                host.DrawUi(MosesFrameObs, motion, 0, x + dx, y, GameWindow.UiBlend.Alpha);
            var (sx, wipe) = DockSlide(1000 + i, icon.X);
            if (sx > icon.X) { DrawSlideTrail(ox, oy, ox + sx + 14, y + 19); continue; }
            DrawButtonUi(MosesObs, icon.Motion, HoverTick(i == _mosesDockHover, _mosesDockHoverAt), x + 14, y + 19, wipe);
            // 새 편지가 있으면 MAIL 아이콘 오른쪽 위에 빨간 점 — 다 읽으면 사라진다(사용자 요청).
            if (icon.Page == 1 && wipe >= 10 && HasNewMail())
            {
                host.FillCircle(x + 27, y - 2, 5, 0xFFFFFFFF);
                host.FillCircle(x + 27, y - 2, 4, 0xFFE02020);
            }
        }
    }

    /// <summary>뒤로 단추 Obs 0248 @ (46,244) — 단추 클래스(id 100)라 들어오고 올리면 움직인다.</summary>
    /// <summary>단추에 마우스를 올린 때 — 올림 모션은 올린 순간부터 처음 장에서 돈다(전에는 전역 틱이라 모션 중간부터 돌았다, ba-20 S B2).</summary>
    internal readonly Dictionary<int, double> _hoverSince = [];

    internal int HoverTick(int key, bool hovered)
    {
        if (!hovered) { _hoverSince.Remove(key); return 0; }
        if (!_hoverSince.TryGetValue(key, out double since)) _hoverSince[key] = since = host._lastTime;
        return (int)((host._lastTime - since) * TicksPerSecond);
    }

    internal void DrawMosesBack(int ox, int oy, int tick)
    {
        var (sx, wipe) = PageSlide(100, 46);
        if (sx > 46) { DrawSlideTrail(ox, oy, ox + sx, oy + 257); return; }
        var r = (X: ox + 46, Y: oy + 244);
        bool hovered = host._mouse.X >= r.X && host._mouse.X < r.X + 25 && host._mouse.Y >= r.Y && host._mouse.Y < r.Y + 26;
        if (host.UiFor(MosesBackObs) != null) DrawButtonUi(MosesBackObs, 0, HoverTick(1000, hovered), r.X, r.Y, wipe);
        else if (wipe >= 10) host.DrawText("BACK", ox + 46, oy + 248, White);
    }

    internal void DrawMosesPage(int ox, int oy, int tick)
    {
        if (_mosesPage is 3 or 4)
        {
            DrawMosesShop(ox, oy, tick);
            return;
        }
        if (_mosesPage == 1)
        {
            DrawMosesMail(ox, oy, tick);
            return;
        }
        if (_mosesPage == 7)
        {
            DrawMosesStyle(ox, oy, tick);
            return;
        }
        if (_mosesPage == 6)
        {
            DrawMosesLegion(ox, oy, tick);
            return;
        }
        if (_mosesPage == 2)
        {
            DrawMosesTalk(ox, oy, tick);
            return;
        }

        // 항행 단계 1 — 성도 위 행성들(0x100fd69c~0x100fd891). 행성 단추는 +0x118 = 1 이라 미끄러져 들어오지 않고 바로 멈춘 그림이다.
        // 덧그림 표 Obs 0163 모션 3 은 <b>모든 행성 위에 늘</b> (0, −10 − 높이/2) 에(0x100fd767~0x100fd77e, 감사5 N6).
        // 이름 글은 없다 — 마우스를 올리면 설명 툴팁(TXR 행성+6)이 뜬다(DrawMosesTooltip).
        if (_mosesPage == 0 && _mosesStep == 1)
        {
            var planets = MosesSystemPlanets();
            for (int i = 0; i < planets.Count; i++)
            {
                var p = planets[i];
                int ptick = HoverTick(i == _mosesHover, _mosesHoverAt);
                host.DrawUi(p.MapObs, p.MapMotion, ptick, ox + p.X, oy + p.Y, GameWindow.UiBlend.Alpha);
                int h = host.UiFor(p.MapObs)?.FrameAt(p.MapMotion, 0)?.H ?? 40;
                host.DrawUi(MosesMarkObs, 3, ptick, ox + p.X, oy + p.Y - 10 - h / 2, GameWindow.UiBlend.Alpha);
                // 모드 > 편의성 「행성에 할 일 표시」 — 전투·필드 장소가 열려 있는 행성은 표시 위에 노란 점을 찍는다(사용자 요청 mo-8, 원본에 없다).
                if (host._planetMarks && PlanetHasMission(p) && host.UiFor(MosesMarkObs)?.FrameAt(3, 0) is { } mark)
                    host.FillCircle(ox + p.X + mark.X + mark.W / 2, oy + p.Y - 10 - h / 2 + mark.Y - 5, 2, 0xFFFFE040);   // 테두리는 너무 티가 나서 표시 위의 작은 점으로(사용자 요청)
            }
            DrawMosesBack(ox, oy, tick);
            // 다른 항성계 단추 — 조건을 통과한 다른 성계 앞의 둘, Obs 0680 모션 0 (30,40) · 1 (630,40),
            // 옆에 <b>그 성계 이름</b>(TXR 성계+4) — 왼쪽은 +20 왼쪽 정렬, 오른쪽은 −20 오른쪽 정렬, 흰색(세로는 단추 가운데, 가설).
            var others = MosesOtherSystems();
            for (int i = 0; i < others.Count; i++)
            {
                var (bx0, by0) = MosesSystemButtons[i];
                var (sx, wipe) = PageSlide(127 + i, bx0);
                if (sx > bx0) { DrawSlideTrail(ox, oy, ox + sx, oy + by0); continue; }
                var r = MosesSystemButtonRect(i, ox, oy);
                bool hovered = host._mouse.X >= r.X && host._mouse.X < r.X + r.W && host._mouse.Y >= r.Y && host._mouse.Y < r.Y + r.H;
                DrawButtonUi(MosesSystemObs, i, HoverTick(1100 + i, hovered), ox + bx0, oy + by0, wipe);
                if (wipe < 10 || Text(others[i].NameText) is not { Length: > 0 } sysName) continue;
                var (_, sw, sh) = host.GetText(sysName, White);
                host.DrawText(sysName, i == 0 ? ox + bx0 + 20 : ox + bx0 - 20 - sw, oy + by0 - sh / 2, White);
            }
            return;
        }

        var cells = MosesCellList();
        int since = (int)((host._lastTime - _mosesPageAt) * TicksPerSecond);

        // 행성 구체 — 항행 단계 2 에서 화면 가운데 조금 위. 그 위에 레이더 `+0x2f14`(초록 격자 구 + 장소 조각)를 돌린다.
        if (_mosesPage == 0 && _mosesStep == 2 && _mosesChp is { } chp && chp.PlanetOf(_mosesPlanet) is { } planet)
        {
            host.DrawUi(planet.GlobeObs, planet.GlobeMotion, tick, ox + 320, oy + 220, GameWindow.UiBlend.Alpha);
            DrawMosesRadar(ox, oy, since, MosesPlacePatches(chp, planet), _mosesHover);
        }
        // 장소·파티 칸도 단추 클래스 — 오른쪽 밖에서 꼬리를 끌고 들어와 10틱 세로 와이프로 나타나고, 올린 칸만 움직인다(감사5 B2·B3).
        for (int i = 0; i < cells.Count; i++)
        {
            var (cx, cy, name, icon, _) = cells[i];
            var (sx, wipe) = PageSlide(10 + i, cx);
            if (sx > cx) { DrawSlideTrail(ox, oy, ox + sx, oy + cy + 13); continue; }
            int x = ox + cx, y = oy + cy;
            int ctick = HoverTick(i == _mosesHover, _mosesHoverAt);
            DrawButtonUi(MosesCellObs, 0, ctick, x, y, wipe);
            if (wipe < 10) continue;
            if (i == _mosesHover)
                foreach (var (dx, motion) in new[] { (0, 0), (10, 1), (168, 2) })
                    host.DrawUi(MosesFrameObs, motion, ctick, x + dx, y, GameWindow.UiBlend.Alpha);
            host.DrawUi(MosesCellIconObs, icon, ctick, x + 20, y + 14, GameWindow.UiBlend.Alpha);
            if (name.Length > 0)
            {
                var (_, w, h) = host.GetText(name, White);
                host.DrawText(name, x + 178 - 15 - w, y + (27 - h) / 2 + 2, White);   // 오른쪽 정렬 x−15
            }
        }

        DrawMosesBack(ox, oy, tick);
    }

    /// <summary>
    /// 툴팁 — 도크 아이콘 설명(TXR 485~490, 도크가 보이는 페이지마다)과 항행 단계 1 의 행성 설명(TXR 행성+6, 폭 200,
    /// <c>0x10042d10(단추, 뿌리, 0, 0, 200, 0, TXR)</c>, 감사5 N6). 자리는 마우스 +(16,16)(주 화면 툴팁과 같은 규칙으로 가정).
    /// </summary>
    internal void DrawMosesTooltip()
    {
        if (_mosesFade > 0 || _mosesSystemSwitch != null) return;
        List<string> lines;
        if (MosesDockShown && _mosesDockHover >= 0)
        {
            string text = Text(MosesIcons[_mosesDockHover].Text);
            lines = [text.Length > 0 ? text : MosesIcons[_mosesDockHover].Name];
        }
        else if (_mosesPage == 0 && _mosesStep == 1 && _mosesHover >= 0 && _mosesHover < MosesSystemPlanets().Count
                 && Text(MosesSystemPlanets()[_mosesHover].DescText) is { Length: > 0 } desc)
            lines = WrapText(desc, 200, 13f);
        else return;
        int w = 0, h = 0;
        foreach (string line in lines)
        {
            var (_, lw, lh) = host.GetText(line, White);
            w = Math.Max(w, lw);
            h += Math.Max(lh, 16);
        }
        var (ox, oy) = MosesOrigin();
        int tx = Math.Min(host._mouse.X + 16, ox + MosesW - w - 6), ty = Math.Min(host._mouse.Y + 16, oy + MosesH - h - 4);
        host.FillRect(tx - 4, ty - 2, w + 8, h + 4, 0xD00A1428);
        host.StrokeRect(tx - 4, ty - 2, w + 8, h + 4, StatusScreen.BoxLine);
        foreach (string line in lines)
        {
            host.DrawText(line, tx, ty, White);
            ty += Math.Max(host.GetText(line, White).H, 16);
        }
    }
}
