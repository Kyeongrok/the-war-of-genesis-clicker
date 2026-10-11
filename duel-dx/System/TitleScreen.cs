using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 타이틀 화면(an-ui-3) — 게임을 켜면 나오는 NEW GAME · CONTINUE · EXIT 화면(장면 6).
/// </summary>
/// <remarks>
/// 옵시디안 분석-UI 「타이틀 화면 (an-ui-3)」:
/// <list type="bullet">
/// <item>배경은 <c>Bgr\0046.bgr</c> 한 장(640×480 <b>GIF</b>) — 로고·부제·저작권과 <b>메뉴 글자 세 줄까지 그림에 들어 있다</b>. 글자 Obs·TXR 은 없다.</item>
/// <item>단추 셋은 그림 없는 <b>170×14 투명 칸</b> — NEW GAME (235,394) · CONTINUE (235,418) · EXIT (235,450).
/// 마우스를 올리면 <c>Obs 0370</c> 모션 0(170×18, 가산 합성)만 덧그리고, 누름 표시도 소리도 없다.</item>
/// <item>아래쪽 반짝임 <c>Obs 0374</c> 다섯 벌을 (80·200·320·440·560, 440) 에 0·10·20·30·40틱 시차로.</item>
/// <item>오른쪽 위 (560,10) 에 <c>Ver %5.3f</c> = <c>Num.dat[0]</c> × 0.001 → 「Ver 1.005」.</item>
/// <item>음악은 <c>BGM\0021</c> 한 번(반복 안 함 — <c>0x100252a0(21, 1, 0)</c> 의 셋째 인자, 감사 F8), 효과음은 하나도 없다. 커서는 화살표(Obs 0044) 그대로.</item>
/// </list>
/// NEW GAME 은 원본에서 연대표(장면 7)를 거쳐 <c>Chp 0010</c>「코어헌터」 → 첫 전투 <c>Btl 0045</c> 로 간다 —
/// 이 데모는 연대표가 없어 곧바로 첫 전투를 연다. CONTINUE 는 불러오기 슬롯(21칸), EXIT 는 정말로 창을 닫는다.
/// <para>
/// 리메이크는 NEW GAME 아래에 <b>LOAD GAME</b> 을 넣어 네 줄이다(사용자 요청 menu-30, 원본에 없다) — LOAD GAME 이 원본 CONTINUE 가 하던
/// 불러오기 목록이고, CONTINUE 는 <b>가장 나중에 적은 세이브</b>를 바로 부른다. 글자가 배경 그림에 들어 있어 그림을 고쳐 쓴다(<see cref="AddLoadGameRow"/>).
/// </para>
/// </remarks>
internal sealed unsafe class TitleScreen(GameWindow host)
{
    internal const int TitleBackground = 46, TitleGlowObs = 370, TitleSparkObs = 374;
    internal const int TitleBgm = 21, TitleFirstBattle = 45;

    /// <summary>단추 넷 — 자리(왼위)와 하는 일. 원본 셋(394 · 418 · 450)에 LOAD GAME 을 끼우느라 아래 둘을 20픽셀 간격으로 다시 놓았다.</summary>
    internal static readonly (int X, int Y, string Name)[] TitleButtons =
        [(235, 394, "NEW GAME"), (235, 414, "LOAD GAME"), (235, 434, "CONTINUE"), (235, 454, "EXIT")];

    internal const int ButtonLoad = 1, ButtonContinue = 2, ButtonExit = 3;

    internal const int TitleButtonW = 170, TitleButtonH = 14;

    internal bool _titleOpen;
    internal int _titleHover = -1;
    internal double _titleOpenedAt;
    /// <summary>타이틀 곡을 건 때 — 3340틱 뒤 다시 건다(기록 화면을 다녀와도 안 바뀐다).</summary>
    internal double _titleMusicAt;

    /// <summary>게임을 켜면 타이틀부터 — <c>DUELDX_TITLE=0</c> 이면 건너뛰고 바로 전투로 간다.</summary>
    internal void OpenTitleIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_EPISODES") == "1") { host.EpisodesScr.OpenEpisodesIfAsked(); return; }
        if (Environment.GetEnvironmentVariable("DUELDX_TITLE") == "0") return;
        OpenTitle();
    }

    internal void OpenTitle()
    {
        _titleOpen = true;
        _titleHover = -1;
        _titleOpenedAt = host._lastTime;
        host.Mos._mosesOpen = false;
        // 연대표·기록 화면도 내린다 — 원본 0x10106530(EXIT GAME, 0x452)은 연대표 장면을 끝내고 장면 6 을 세운다.
        // 안 내리면 연대표가 타이틀 위에 그대로 그려지고 클릭도 먼저 먹었다(감사 F3).
        host.EpisodesScr._episodesOpen = false;
        host.RecordsScr._recordsOpen = false;
        host.EpisodesScr._episodePick = -1;
        // 게임오버(LeaveFinishedBattle)에서 오면 판이 아직 전투 맵 크기다 — 원본은 장면이 새로 선다(감사 F15, 모세스와 같은 까닭).
        if (host.Cols != TitleBoardCols || host.Rows != TitleBoardRows) { host.ResizeBoard(TitleBoardCols, TitleBoardRows); host._battleLoaded = false; }
        bool fresh = host.Mos._mosesBgId != TitleBackground;
        host.Mos.ShowMosesBackground(TitleBackground);
        if (fresh && host.Mos._mosesBgId == TitleBackground && host.Mos._mosesBg is { Length: MosesScene.MosesW * MosesScene.MosesH } loaded) AddLoadGameRow(loaded);
        host.EnterSceneFade();
        // 타이틀 곡이 이미 돌고 있으면(불러오기 화면에서 돌아옴) 다시 걸지 않는다 — 원본 [0x101a99e4] 검사.
        if (host._musicId == TitleBgm) return;
        host.StopMusic();
        _titleMusicAt = host._lastTime;
        // 반복하지 않는다 — 원본 0x1010583c: 0x100252a0(21, 1, 0), 셋째 인자 0 = 한 번(끝나면 BinkGoto(1)·BinkPause). 감사 F8.
        // 0 에서 15틀에 84% 로 올라 거기 머문다(ba-21 outer #3).
        host.PlayMusicFile(TitleBgm, loop: false, gain: 0);
        host.FadeMusic(84, 15);
    }

    /// <summary>
    /// 배경 그림의 메뉴 글자를 네 줄로 고친다 — CONTINUE 를 16, EXIT 를 4픽셀 내리고 그 사이에 LOAD GAME 을 만든다.
    /// 글자는 「둘레 바탕보다 밝은 만큼」으로 떠서(바탕은 글자 왼쪽·오른쪽 12픽셀의 줄 평균을 이은 값) 새 자리에 더한다.
    /// LOAD GAME 의 O · A · GAME 은 그림의 글자를 그대로 옮기고, 그림에 없는 L · D 만 같은 굵기의 점 그림으로 그린다.
    /// </summary>
    internal static void AddLoadGameRow(uint[] bg)
    {
        const int W = MosesScene.MosesW, Margin = 12;
        var src = (uint[])bg.Clone();

        // 그 네모의 글자(바탕보다 밝은 만큼, 0xRRGGBB)를 뜬다. erase 면 글자 자리를 바탕으로 메운다.
        uint[] Lift(int x0, int y0, int x1, int y1, bool erase)
        {
            var lifted = new uint[(x1 - x0) * (y1 - y0)];
            Span<int> left = stackalloc int[3], right = stackalloc int[3];
            for (int y = y0; y < y1; y++)
            {
                left.Clear(); right.Clear();
                for (int i = 0; i < Margin; i++)
                    for (int c = 0; c < 3; c++)
                    {
                        left[c] += (int)(src[y * W + x0 - Margin + i] >> (16 - 8 * c) & 0xFF);
                        right[c] += (int)(src[y * W + x1 + i] >> (16 - 8 * c) & 0xFF);
                    }
                for (int x = x0; x < x1; x++)
                {
                    uint p = src[y * W + x], delta = 0, flat = 0xFF000000;
                    for (int c = 0; c < 3; c++)
                    {
                        int shift = 16 - 8 * c;
                        int ground = (left[c] + (right[c] - left[c]) * (x - x0) / (x1 - x0)) / Margin;
                        delta |= (uint)Math.Max(0, (int)(p >> shift & 0xFF) - ground) << shift;
                        flat |= (uint)ground << shift;
                    }
                    lifted[(y - y0) * (x1 - x0) + (x - x0)] = delta;
                    if (erase && Math.Max(delta >> 16, Math.Max(delta >> 8 & 0xFF, delta & 0xFF)) > 10) bg[y * W + x] = flat;
                }
            }
            return lifted;
        }

        void Add(int x, int y, uint delta)
        {
            uint d = bg[y * W + x];
            uint Ch(int shift) => Math.Min(255, (d >> shift & 0xFF) + (delta >> shift & 0xFF));
            bg[y * W + x] = 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
        }

        // 뜬 글자의 열 [from, to) 를 (x, y) 에 더한다.
        void Put(uint[] lifted, int w, int x, int y, int from = 0, int to = int.MaxValue)
        {
            to = Math.Min(to, w);
            for (int row = 0; row < lifted.Length / w; row++)
                for (int col = from; col < to; col++) Add(x + col - from, y + row, lifted[row * w + col]);
        }

        // 점 그림 글자 — # 은 글자 몸, 그 오른쪽·아래로 번지는 옅은 빛(그림의 글자가 그렇다).
        void Stamp(string[] rows, int x, int y)
        {
            bool Core(int cx, int cy) => cy >= 0 && cy < rows.Length && cx >= 0 && cx < rows[cy].Length && rows[cy][cx] == '#';
            for (int cy = 0; cy < rows.Length + 2; cy++)
                for (int cx = 0; cx < rows[0].Length + 2; cx++)
                {
                    if (Core(cx, cy)) Add(x + cx, y + cy, 0x807E86);
                    else if (Core(cx - 1, cy) || Core(cx, cy - 1) || Core(cx - 1, cy - 1) || Core(cx - 2, cy - 2)) Add(x + cx, y + cy, 0x26262C);
                }
        }

        var cont = Lift(276, 416, 364, 436, erase: true);    // CONTINUE
        var exit = Lift(300, 449, 344, 469, erase: true);    // EXIT
        var game = Lift(319, 392, 368, 412, erase: false);   // NEW GAME 의 GAME — 제자리에 둔다
        Put(cont, 88, 276, 432);
        Put(exit, 44, 300, 453);

        Stamp(GlyphL, 266, 415);
        Put(cont, 88, 276, 412, 14, 28);                     // O
        Put(game, 49, 291, 412, 14, 25);                     // A
        Stamp(GlyphD, 303, 415);
        Put(game, 49, 323, 412);                             // GAME
    }

    private static readonly string[] GlyphL =
    [
        "##.......", "##.......", "##.......", "##.......", "##.......", "##.......", "##.......", "##.......", "##.......", "##.......",
        "###......", ".########", "..#######",
    ];

    private static readonly string[] GlyphD =
    [
        "#######.....", "#########...", "##.....###..", "##......###.", "##.......##.", "##.......###", "##.......###", "##.......###",
        "##.......##.", "##......###.", "##.....###..", "#########...", "#######.....",
    ];

    internal int TitleButtonAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        for (int i = 0; i < TitleButtons.Length; i++)
        {
            var (x, y, _) = TitleButtons[i];
            if (bx >= ox + x && bx < ox + x + TitleButtonW && by >= oy + y && by < oy + y + TitleButtonH) return i;
        }
        return -1;
    }

    /// <summary>타이틀이 떠 있으면 클릭을 처리하고 true. 원본처럼 <b>누르는 순간</b> 움직인다.</summary>
    internal bool OnTitleClick(int bx, int by)
    {
        if (!_titleOpen) return false;
        // CONTINUE 로 연 슬롯 창이 떠 있으면 그 창이 먼저 클릭을 받는다.
        if (host.Sys.SystemOpen) return host.Sys.OnSystemClick(bx, by);
        PressTitleButton(TitleButtonAt(bx, by));
        return true;
    }

    /// <summary>
    /// 타이틀 키 — Esc 는 CONTINUE 가 연 슬롯 창을 닫고, <b>Enter 는 초점 단추 NEW GAME 을 누른다</b>
    /// (NEW GAME 이 만들어질 때 <c>vt+0x40</c> 으로 초점을 받는다, 감사 F13). 전에는 Esc 말고는 무시했다.
    /// </summary>
    internal bool OnTitleKey(int key)
    {
        if (!_titleOpen) return false;
        if (key == Win32.VK_ESCAPE) host.Sys.CloseSystemWindow();
        else if (host.Sys.SystemOpen) { }
        // 위·아래 이동 키(기본 W · S, 방향키)로 단추를 옮기고 Enter · Space 로 누른다(사용자 요청 menu-33) — 짚은 단추가 없으면 NEW GAME.
        else if (host.MoveActionFor(key) is KeyAction.MoveUp or KeyAction.MoveDown || key is Win32.VK_UP or Win32.VK_DOWN)
        {
            bool up = key == Win32.VK_UP || host.MoveActionFor(key) == KeyAction.MoveUp;
            int n = TitleButtons.Length;
            _titleHover = _titleHover < 0 ? (up ? n - 1 : 0) : (_titleHover + (up ? n - 1 : 1)) % n;
        }
        else if (key is Win32.VK_RETURN or Win32.VK_SPACE) PressTitleButton(Math.Max(0, _titleHover));
        return true;
    }

    internal void PressTitleButton(int button)
    {
        if (button < 0 || button >= TitleButtons.Length) return;   // 단추 밖을 누른 것
        // CONTINUE — 부를 세이브가 없으면 화면을 떠나지 않는다.
        if (button == ButtonContinue && host.SlotsScr.LatestSlot() < 0) { host.Toast("저장된 기록이 없습니다"); return; }
        // 16틀 검게 + 음악 100 → 10% 뒤에 넘어간다(0x10105420). 불러오기 화면으로 갈 때는 곡이 이어진다. EXIT 도 페이드 뒤 끝난다.
        host.LeaveScene(() => RunTitleButton(button), keepMusic: button == ButtonLoad);
    }

    internal void RunTitleButton(int button)
    {
        switch (button)
        {
            case 0:                                   // NEW GAME — 원본처럼 연대표(장면 7)로 간다
                host._party.Clear();
                // 앞 판의 전투 유닛도 버린다 — 남겨 두면 첫 전투의 RememberParty 가 그 자료(장비·레벨)를 새 명부에 도로 적어,
                // 살라딘·죠안이 지난 세이브의 장비를 차고 시작했다(피드백 menu-26).
                foreach (var old in host._units) old.Data = null;
                host.Mos._members.Clear();
                host.Mos._ownedLegions.Clear();
                host.Mos._legionsKnown = true;
                host.PartySt._partyBank.Clear();
                // 파티의 가방·GP·군단 배속과 「이미 겪은」 표시(자동 장소·챕터 사건 횟수·다녀온 장소)도 비운다 — 안 비우면 같은 실행에서
                // 두 번째 새 게임이 프롤로그·동료 합류·3000GP 를 건너뛰고 지난 판의 가방을 들고 시작한다(원본 0x1004d870 은 파티를 새로 만든다).
                host.StatusScr._inventory.Clear();
                host.Mos._shopMoney = 0;
                host.Mos._unitLegion.Clear();
                host.Mos._placesUsed.Clear();
                host.Mos._autoPlacesDone.Clear();
                host.Fld._chapterFired.Clear();
                host.Mos._mailbox.Clear();
                host.Mos._mailRead.Clear();
                host.Mos._planetVisits.Clear();
                Array.Clear(host.FlagSt._flags);                  // 새 게임 — 진행 깃발을 비운다(0x1004d870). 그래야 연대표에 0·1번만 열린다.
                host.EpisodesScr._chapterDone = false;
                host.EpisodesScr._episodesPicked.Clear();              // 고른 에피소드 표시도 NEW GAME 만 지운다(0x1004d870)
                host.EpisodesScr._partyNo = 0;
                // 플레이 시간 0([0x101737a4] = 0)·챕터 상태(0x101b6898 — 지금 챕터·스크립트 변수·항행 시작) 버림(0x1004d870, 감사 F10).
                // 지금 챕터가 남으면 연대표 세이브를 부를 때 앞 판 챕터가 열렸다(F6).
                host.Sys._playBase = -host._realTime * 1000;
                host.Mos._mosesChp = null;
                host.Mos._navStart = null;
                Array.Clear(host.Fld._chapterVars);
                if (host.EpisodesScr.Episodes().Count > 0) host.EpisodesScr.OpenEpisodes();
                else { _titleOpen = false; if (!host.Sys.StartBattle(TitleFirstBattle)) OpenTitle(); }
                break;
            case ButtonLoad:                          // LOAD GAME — 「Select your record」 화면(장면 9, 원본의 CONTINUE)
                host.RecordsScr.OpenRecords();
                break;
            case ButtonContinue:                      // CONTINUE — 가장 나중에 적은 세이브(자동 저장 포함)를 바로 부른다
                if (host.SlotsScr.LatestSlot() is var latest and >= 0 && host.Sys.LoadBattleFrom(SlotsScreen.SlotPath(latest))) break;
                OpenTitle();                          // 못 불렀으면 타이틀로 돌아와 알린다(깨진 파일 문구 — SlotsScreen.LoadSlot 과 같다)
                host.SlotsScr._notice = ("세이브 파일에 오류가 생겼거나 허가없이 변경되었습니다.\n로드할 수 없습니다.", host._lastTime + 120 / TicksPerSecond);
                break;
            case ButtonExit:                          // EXIT — 원본도 여기서만 진짜로 끝난다
                host._running = false;
                break;
        }
    }

    internal void UpdateTitleHover(int bx, int by)
    {
        if (_titleOpen) _titleHover = TitleButtonAt(bx, by);
    }

    internal void DrawTitle()
    {
        if (!_titleOpen) return;
        // 원본 타이틀 틱(0x10105920)은 3340틱(약 111초)을 넘으면 오프닝 필드(Fld 0022·0413 — Mov)로 갔다가 돌아온다(ba-20 T2).
        // 오프닝 영상은 없으니 곡만 처음부터 다시 건다 — 전에는 곡이 한 번 끝나면 계속 조용했다.
        if (_titleOpen && !host.Sys.SystemOpen && (host._lastTime - _titleMusicAt) * TicksPerSecond > 3340)
        {
            _titleMusicAt = host._lastTime;
            host.StopMusic();
            host.PlayMusicFile(TitleBgm, loop: false);
        }
        var (ox, oy) = host.Mos.MosesOrigin();
        int tick = (int)(host._lastTime * TicksPerSecond);

        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        if (host.Mos._mosesBg is { } bg)
            for (int y = 0; y < MosesScene.MosesH; y++)
                for (int x = 0; x < MosesScene.MosesW; x++)
                    host.SetPixel(ox + x, oy + y, bg[y * MosesScene.MosesW + x] | 0xFF000000);

        // 아래쪽 반짝임 다섯 벌 — 시차 0·10·20·30·40틱
        for (int i = 0; i < 5; i++)
            host.DrawUi(TitleSparkObs, 0, tick + i * 10, ox + 80 + i * 120, oy + 440, GameWindow.UiBlend.Add);

        // 마우스를 올린 단추에만 빛(가산) — 단추 왼위 (0,0) 그대로(0x10044320(0, 0, 370, 0, …) → 단추 +0x140/+0x142 = (0,0), 장 자리 (0,0)).
        // 전에는 2픽셀 올려 그렸다(감사 F14, 장 자리 값은 분석-UI 기록에 기댄 가설).
        if (_titleHover >= 0)
        {
            var (x, y, _) = TitleButtons[_titleHover];
            host.DrawUi(TitleGlowObs, 0, (int)((host._lastTime - _titleOpenedAt) * TicksPerSecond), ox + x, oy + y, GameWindow.UiBlend.Add);
        }

        // 오른쪽 위 판 번호 — Num.dat[0] × 0.001
        string version = $"Ver {(host._db?.N(0) ?? 1005) * 0.001:0.000}";
        host.DrawText(version, ox + 560, oy + 10, White, 12);
        // 자료를 뒤에서 받는 동안 진행을 왼쪽 위에 보인다(AssetDownload).
        if (AssetPack.Status is { Length: > 0 } pack) host.DrawText(pack, ox + 8, oy + 10, White, 12);

        host.Sys.DrawSystem();     // CONTINUE 가 연 슬롯 창
        host.DrawToast();
    }
}
