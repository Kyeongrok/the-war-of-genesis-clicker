using WarOfGenesis.Assets;

namespace DuelDx;

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
/// <item>음악은 <c>BGM\0021</c> 통째 반복, 효과음은 하나도 없다. 커서는 화살표(Obs 0044) 그대로.</item>
/// </list>
/// NEW GAME 은 원본에서 연대표(장면 7)를 거쳐 <c>Chp 0010</c>「코어헌터」 → 첫 전투 <c>Btl 0045</c> 로 간다 —
/// 이 데모는 연대표가 없어 곧바로 첫 전투를 연다. CONTINUE 는 불러오기 슬롯(21칸), EXIT 는 정말로 창을 닫는다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int TitleBackground = 46, TitleGlowObs = 370, TitleSparkObs = 374;
    private const int TitleBgm = 21, TitleFirstBattle = 45;

    /// <summary>단추 셋 — 자리(왼위)와 하는 일.</summary>
    private static readonly (int X, int Y, string Name)[] TitleButtons =
        [(235, 394, "NEW GAME"), (235, 418, "CONTINUE"), (235, 450, "EXIT")];

    private const int TitleButtonW = 170, TitleButtonH = 14;

    private bool _titleOpen;
    private int _titleHover = -1;
    private double _titleOpenedAt;

    /// <summary>게임을 켜면 타이틀부터 — <c>DUELDX_TITLE=0</c> 이면 건너뛰고 바로 전투로 간다.</summary>
    private void OpenTitleIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_TITLE") == "0") return;
        OpenTitle();
    }

    private void OpenTitle()
    {
        _titleOpen = true;
        _titleHover = -1;
        _titleOpenedAt = _lastTime;
        _mosesOpen = false;
        ShowMosesBackground(TitleBackground);
        _mixer.StopMusic();
        PlayMusicFile(TitleBgm, loop: true);
    }

    private int TitleButtonAt(int bx, int by)
    {
        var (ox, oy) = MosesOrigin();
        for (int i = 0; i < TitleButtons.Length; i++)
        {
            var (x, y, _) = TitleButtons[i];
            if (bx >= ox + x && bx < ox + x + TitleButtonW && by >= oy + y && by < oy + y + TitleButtonH) return i;
        }
        return -1;
    }

    /// <summary>타이틀이 떠 있으면 클릭을 처리하고 true. 원본처럼 <b>누르는 순간</b> 움직인다.</summary>
    private bool OnTitleClick(int bx, int by)
    {
        if (!_titleOpen) return false;
        // CONTINUE 로 연 슬롯 창이 떠 있으면 그 창이 먼저 클릭을 받는다.
        if (SystemOpen) return OnSystemClick(bx, by);
        switch (TitleButtonAt(bx, by))
        {
            case 0:                                   // NEW GAME — 원본처럼 연대표(장면 7)로 간다
                _party.Clear();
                _members.Clear();
                _ownedLegions.Clear();
                _partyBank.Clear();
                Array.Clear(_flags);                  // 새 게임 — 진행 깃발을 비운다(0x1004d870). 그래야 연대표에 0·1번만 열린다.
                _chapterDone = false;
                _partyNo = 0;
                if (Episodes().Count > 0) OpenEpisodes();
                else { _titleOpen = false; if (!StartBattle(TitleFirstBattle)) OpenTitle(); }
                break;
            case 1:                                   // CONTINUE — 「Select your record」 화면(장면 9)
                OpenRecords();
                break;
            case 2:                                   // EXIT — 원본도 여기서만 진짜로 끝난다
                _running = false;
                break;
        }
        return true;
    }

    private void UpdateTitleHover(int bx, int by)
    {
        if (_titleOpen) _titleHover = TitleButtonAt(bx, by);
    }

    private void DrawTitle()
    {
        if (!_titleOpen) return;
        var (ox, oy) = MosesOrigin();
        int tick = (int)(_lastTime * TicksPerSecond);

        FillRect(_camX, _camY, ViewWidth, ViewHeight, 0xFF000000);
        if (_mosesBg is { } bg)
            for (int y = 0; y < MosesH; y++)
                for (int x = 0; x < MosesW; x++)
                    SetPixel(ox + x, oy + y, bg[y * MosesW + x] | 0xFF000000);

        // 아래쪽 반짝임 다섯 벌 — 시차 0·10·20·30·40틱
        for (int i = 0; i < 5; i++)
            DrawUi(TitleSparkObs, 0, tick + i * 10, ox + 80 + i * 120, oy + 440, UiBlend.Add);

        // 마우스를 올린 단추에만 빛(가산)
        if (_titleHover >= 0)
        {
            var (x, y, _) = TitleButtons[_titleHover];
            DrawUi(TitleGlowObs, 0, (int)((_lastTime - _titleOpenedAt) * TicksPerSecond), ox + x, oy + y - 2, UiBlend.Add);
        }

        // 오른쪽 위 판 번호 — Num.dat[0] × 0.001
        string version = $"Ver {(_db?.N(0) ?? 1005) * 0.001:0.000}";
        DrawText(version, ox + 560, oy + 10, White, 12);

        DrawSystem();     // CONTINUE 가 연 슬롯 창
        DrawToast();
    }
}
