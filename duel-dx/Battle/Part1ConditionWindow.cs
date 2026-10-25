namespace DuelDx;

using WarOfGenesis.Assets;

/// <summary>
/// 파트1 전투 「승리 조건」 · 「패배 조건」 창 — 행동 706(<c>0x10021790</c>)이 띄우는 창 <c>0x10096e70</c>(vtable <c>0x101096e0</c>).
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 13.1절.
/// <list type="bullet">
/// <item>706 칸 단계 0: 0x50 개체 <c>0x10096e70(판, 판)</c> 을 만들고 <c>0x10031ac0</c>(커서 [H]) · 단계 1. 단계 1: 제목 띠(<c>+0x4c</c>)가
/// 다 사라지면(<c>+0x40 == 0 · +0x69 == 0xff</c>) 창 지우기 · <c>0x10031b90</c> · 칸 끝(<c>0x100220a0</c>). 인자는 안 쓴다 — 글은 판 머리 낱말 3·4·5
/// (<c>+0xb968 · +0xb96a · +0xb96c</c>, .dlg 줄)이다. 자료의 706 은 56곳 모두 들머리 사건 끝에 있고 바로 뒤가 행동 1 이라, 줄을 붙드는 것은 그 1 이다.</item>
/// <item>뿌리 <c>0x1009f390(화면 폭/2, 화면 높이/2, 판, 0xb6, 판)</c> = (320, 240). 그 밑에(자리는 부모에서 잰다):
/// 제목 띠 <c>0x100a1600(…, −250, −110, 500×40, Obs −1 → 111 모션 1)</c> = (70, 130) 파란 돌 · 제목 글(250, 20) 꼴 6(굴림 20 굵게) 가운데 맞춤 빛깔 (150, 200, 255);
/// 승리 띠(띠 + (0, 48), 500×60, Obs 111 모션 9 푸른 돌) · 그림 Obs 677 모션 0(15, 10) · 「승리 조건」(40, 20) 꼴 2(굴림 10 굵게) (255, 200, 50) · 조건 글(40, 40) 꼴 2 흰색;
/// 패배 띠(띠 + (0, 112), 500×60, 모션 8 붉은 돌) · Obs 677 모션 1(10, 12) · 「패배 조건」(40, 20) · 글(40, 40). 글은 그 y 를 가운데로(<c>0x100a27f0</c>), 검은 1픽셀 테두리(<c>0x10019330</c>).</item>
/// <item>틀(<c>0x100a2190</c> → <c>0x100a18b0</c>)은 해설 상자와 같다 — 돌 무늬를 섞기 13 단계 6 으로 깔고 흰 테두리 · 모서리 넷.
/// 나옴(<c>0x1009fb70</c> · 틱 <c>0x1009fbf0</c>): k = 0~7 틱 동안 무늬 단계 k/2 · 테두리 단계 k + 1, 그림은 단계 k + 1, 글은 곧바로.
/// 사라짐(<c>0x1009fba0</c>): k = 0~3 틱 동안 무늬 4 − k · 테두리 7 − k · 그림 7 − 2k(첫 틱 6), 글은 곧바로 지운다.</item>
/// <item>닫기: 틱으로는 안 닫힌다. 뿌리의 누름 판정이 늘 참(<c>0x100c4df0</c>)이고 왼 · 오른 단추를 뗄 때(vt +0xc · +0x14, 둘 다 <c>0x10097260</c> → vt +0x78)
/// 창 전체를 닫는다 — 화면 어디를 눌러도 된다. 리메이크는 누름 · Enter · Space 로 닫고, 건너뛰기(Esc · TESTSKIP)면 안 띄운다/곧바로 닫는다.</item>
/// </list>
/// </remarks>
internal sealed class Part1ConditionWindow(GameWindow host)
{
    private const int FrameObs = 111, IconObs = 0x2a5;

    /// <summary>뿌리 (320, 240) + 제목 띠 (−250, −110).</summary>
    private const int X = 70, Y = 130, W = 500, TitleH = 40, BandH = 60, WinTop = 48, LoseTop = 112;

    /// <summary>나옴 8틱 · 사라짐 4틱(<c>0x1009fbf0</c>).</summary>
    private const int GrowTicks = 8, ShrinkTicks = 4;

    /// <summary>꼴 표 <c>0x1011bda8</c> — 6 굴림 20pt 굵게 · 2 굴림 10pt 굵게, 픽셀로(× 96/72).</summary>
    private const float TitleSize = 27, LabelSize = 13;

    /// <summary>COLORREF 0xffc896 · 0x32c8ff → RGB.</summary>
    private const uint TitleColor = 0xFF96C8FF, LabelColor = 0xFFFFC832;

    /// <summary>화면 밖 자동 진행(AUTOPLAY, 건너뛰기 없이)이 창을 저절로 닫기까지 — 시험용 갈음.</summary>
    private const int AutoCloseTicks = 60;

    private Part1MapArt? _art;
    private Part1MapArt Art => _art ??= new(host);

    private bool _open;
    private double _openAt, _closeAt;
    private string _title = "", _win = "", _lose = "";

    /// <summary>창이 떠 있나(사라지는 중 포함) — 행동 1 이 이것을 기다린다.</summary>
    internal bool IsOpen => _open;

    /// <summary>706 — 이 판의 제목 · 승리 · 패배 줄로 연다. 건너뛰는 중이면 안 띄운다.</summary>
    internal void Show()
    {
        if (host.Tlk._talkSkip) return;
        if (Part1Battle.Load(host._scene.Id - Part1Game.BattleBase) is not { } b) return;
        string Plain(int line) => Part1Game.PlainText(b.Line(line)).Replace('\n', ' ');
        (_title, _win, _lose) = (Plain(b.TitleLine), Plain(b.WinLine), Plain(b.LoseLine));
        _open = true;
        _openAt = host._lastTime;
        _closeAt = 0;
    }

    private double Ticks(double since) => (host._lastTime - since) * GameWindow.TicksPerSecond;

    /// <summary>틀마다 — 다 사라지면 닫는다. 건너뛰는 중이면 곧바로.</summary>
    internal void Update()
    {
        if (!_open) return;
        if (host.Tlk._talkSkip) { Close(); return; }
        if (_closeAt == 0 && BattleScene.AutoPlay && Ticks(_openAt) >= AutoCloseTicks) _closeAt = host._lastTime;
        if (_closeAt > 0 && Ticks(_closeAt) >= ShrinkTicks) Close();
    }

    /// <summary>누름(왼 · 오른 단추) — 떠 있으면 닫기 시작하고 누름을 먹는다.</summary>
    internal bool Click()
    {
        if (!_open) return false;
        if (_closeAt == 0) _closeAt = host._lastTime;
        return true;
    }

    internal void Close() => _open = false;

    /// <summary>섞기 13 의 단계(0~8) → 256 비침 — <c>4·단계/31</c>(단계 6 = 24/31).</summary>
    private static int Alpha(int stage) => Math.Clamp(stage * 4 * 256 / 31, 0, 256);

    internal void Draw()
    {
        if (!_open) return;
        var (ox, oy) = host.Mos.MosesOrigin();
        int k;
        int texture, border, icon;
        bool text;
        if (_closeAt > 0)
        {
            k = (int)Ticks(_closeAt);
            if (k >= ShrinkTicks) return;
            (texture, border, icon, text) = (4 - k, 7 - k, k == 0 ? 6 : 7 - 2 * k, false);
        }
        else
        {
            k = (int)Ticks(_openAt);
            (texture, border, icon, text) = k < GrowTicks ? (k / 2, k + 1, k + 1, true) : (6, 8, 8, true);
        }
        Art.Clip = (ox, oy, 640, 480);
        int x = ox + X, y = oy + Y;
        Band(x, y, TitleH, 0, texture, border);
        Band(x, y + WinTop, BandH, 5, texture, border);
        Band(x, y + LoseTop, BandH, 4, texture, border);
        Art.Blit(Art.Motion(IconObs, 0), x + 15, y + WinTop + 10, Alpha(icon));
        Art.Blit(Art.Motion(IconObs, 1), x + 10, y + LoseTop + 12, Alpha(icon));
        Art.Clip = default;
        if (!text) return;
        Outlined(_title, x + W / 2, y + 20, TitleColor, TitleSize, center: true);
        Outlined("승리 조건", x + 40, y + WinTop + 20, LabelColor, LabelSize);
        Outlined(_win, x + 40, y + WinTop + 40, GameWindow.White, LabelSize);
        Outlined("패배 조건", x + 40, y + LoseTop + 20, LabelColor, LabelSize);
        Outlined(_lose, x + 40, y + LoseTop + 40, GameWindow.White, LabelSize);
    }

    /// <summary>띠 하나(<c>0x100a18b0</c>) — 돌 무늬(Obs 111 벌 <paramref name="set"/>)를 깔고 흰 1픽셀 테두리(모서리 5픽셀 비움) · 모서리 넷(벌 2).</summary>
    private void Band(int x, int y, int h, int set, int textureStage, int borderStage)
    {
        Art.Tile(Art.Frame(FrameObs, set, 0) ?? Art.Frame(FrameObs, 0, 0), x, y, W, h, Alpha(textureStage));
        int a = Alpha(borderStage);
        Line(x + 5, y, W - 10, 1, a);
        Line(x + 5, y + h - 1, W - 10, 1, a);
        Line(x, y + 5, 1, h - 10, a);
        Line(x + W - 1, y + 5, 1, h - 10, a);
        Art.Blit(Art.Frame(FrameObs, 2, 0), x, y, a);
        Art.Blit(Art.Frame(FrameObs, 2, 1), x + W - 1, y, a);
        Art.Blit(Art.Frame(FrameObs, 2, 2), x, y + h - 1, a);
        Art.Blit(Art.Frame(FrameObs, 2, 3), x + W - 1, y + h - 1, a);
    }

    private static readonly SpriteFrame WhitePixel = new([0xFFFFFFFF], 1, 1, 0, 0);

    private void Line(int x, int y, int w, int h, int alpha)
    {
        if (alpha >= 256) Art.Fill(x, y, w, h, 0xFFFFFFFF);
        else Art.Tile(WhitePixel, x, y, w, h, alpha);
    }

    /// <summary>글 — y 를 가운데로, 가운데 맞춤이면 x 도(<c>0x100a27f0</c>). 검은 글을 위아래 왼오른 1픽셀씩 찍고 그 위에 빛깔(<c>0x10019330</c>).</summary>
    private void Outlined(string s, int x, int cy, uint color, float size, bool center = false)
    {
        if (s.Length == 0) return;
        var (_, w, h) = host.UiGetText(s, color, size);
        int left = center ? x - w / 2 : x, top = cy - h / 2;
        const uint Black = 0xFF000000;
        host.UiText(s, left + 1, top, Black, size);
        host.UiText(s, left - 1, top, Black, size);
        host.UiText(s, left, top + 1, Black, size);
        host.UiText(s, left, top - 1, Black, size);
        host.UiText(s, left, top, color, size);
    }
}
