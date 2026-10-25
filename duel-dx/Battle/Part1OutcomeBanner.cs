using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 전투 끝 연출 — 「Scenario Clear」 · 「Game Over」 띠. 원본(alpha.dll) 전투 끝 상태 <c>0x1002eac0</c>(하위 상태 <c>+0xb998</c>):
/// <list type="number">
/// <item>하위 0 — 배너 깃발 <c>+0x2c</c> 가 0(행동 106)이면 <b>어둡게도 안 하고 곧</b> 끝(<c>+0x28 = 1</c>). 결과 <c>+0x24</c> 가 1 · 5 · 6 · 7(이김 ·
/// 101 이어지는 전투 · 104 필드로 · 105 챕터 끝)이면 BGM 24, 4(짐)이면 BGM 25 를 <b>되풀이</b>로 건다(<c>0x100094e0(곡, 1)</c> — 되풀이 깃발
/// <c>0x1011fd68</c>). 그 밖의 결과(100 의 인자 1 · 2 → 결과 2 · 3, 메뉴의 8~10)도 곧 끝이다. 그리고 판 물들이기를 방식 15 · 목표 세기 16 으로
/// (<c>0x1001c3a0(0xf, 0x10)</c>) — 세기 <c>0x1014d3f2</c> 는 틱마다 1 씩 목표로 간다(<c>0x1002dab0</c>). 방식 15 의 색표(<c>0x10137dcc</c>,
/// 만드는 곳 <c>0x1000b8d5</c>)는 채널 c(5비트) · 세기 L 에서 <c>c·(31−L)/31</c> — 세기 16 이면 15/31 ≈ 48% 밝기다. 맵 칸과 인물만 물든다.</item>
/// <item>하위 1 — 카운터 <c>+0xb978</c> 가 16 이 될 때까지(어두워지는 16틱).</item>
/// <item>하위 2 — Obs 789(0x315) 를 화면 가운데(<c>0x1011b100 / 2</c>, <c>0x1011b104 / 2</c>)에 둘 세운다. 이기면 모션 8(띠) · 4(글),
/// 지면 10 · 5. 띠 물체에만 섞기 10 을 준다(<c>0x1009fb50(10, 0)</c> → <c>+0x48</c>). 8틱 앞당겨 돌린 뒤(<c>vtable +0x30</c> × 8) 띄운다 — 모션은 길이 3 의 한 장이라
/// 나타나고 사라지는 연출은 없다.</item>
/// <item>하위 3 — 입력(<c>+0xc9fc · 0xc9fd · 0xc9ff · 0xca01</c>)이 올 때까지 <b>기다리기만</b> 한다. 시간이 지나도 저절로 넘어가지 않는다.</item>
/// </list>
/// Obs 789: 띠 = 장 8(142×90, 이김) · 장 9(143×90, 짐) 을 가운데와 좌우 ±142·284·426·568(짐은 143 간격) 아홉 장으로 깐다(모션 9 · 11 자식, 섞기 10).
/// 글 = 장 6 · 7(이김) · 2 · 3(짐)의 번진 그림을 섞기 13 세기 4(<c>(15·바탕 + 16·그림)/31</c>)로, 그 위에 장 4 · 5 · 0 · 1 의 글씨를 섞기 9(더하기)로.
/// 섞기 10 은 색표 <c>0x10122dcc</c>: <c>min(31, 바탕 + 그림²/31)</c>(바탕이 색인 윗자리 — 블리터 <c>0x1000e2f0</c>).
/// </summary>
internal static class Part1OutcomeBanner
{
    /// <summary>파트1 Obs 789 — 게임 안 번호.</summary>
    internal const int Obs = Part1Game.ObsBase + 789;

    /// <summary>이김 · 짐 곡(<c>BGMs\0024.bgm</c> · <c>0025.bgm</c>).</summary>
    internal const int WinBgm = 24, LoseBgm = 25;

    /// <summary>물들이기 목표 세기 — 채널이 (31 − 16)/31 로 어두워진다.</summary>
    internal const int DimLevel = 16;

    /// <summary>
    /// 시험(화면 밖 자동 진행)에서만 배너를 저절로 넘긴다 — 원본은 입력을 기다리기만 해서, 그대로 두면 자동 전투가 배너에서 멈춘다.
    /// </summary>
    static bool AutoLeave => TestRun || TestSkip || TestClick || BattleScene.AutoPlay || Part1Auto.On;

    /// <summary>틀마다 — 결과가 서는 틀에 곡을 걸고(하위 0), 시험이면 배너가 뜬 뒤 121틱에 넘긴다.</summary>
    internal static void Update(GameWindow host)
    {
        var btl = host.Btl;
        if (btl._outcome.Length == 0 || btl._outcomeQuiet || host.Mos._mosesOpen || host.FieldOpen || host.EpisodesScr._episodesOpen) return;
        if (host._outcomeMusicPlayedFor != btl._outcomeAt)
        {
            host._outcomeMusicPlayedFor = btl._outcomeAt;
            host.StopMusic();
            host.PlayMusicFile(Part1Game.BgmBase + (btl._outcome.StartsWith('승') ? WinBgm : LoseBgm), loop: true);
        }
        if (AutoLeave && (host._lastTime - btl._outcomeAt) * TicksPerSecond >= OutcomeBannerDelayTicks + OutcomeAutoTicks + 1)
            btl.LeaveFinishedBattle();
    }

    /// <summary>판을 어둡게 하고, 16틱부터 띠와 글을 그린다. 배너 없는 결과(106 · 100 의 인자 1 · 2)는 어둡게도 안 한다.</summary>
    internal static void Draw(GameWindow host)
    {
        var btl = host.Btl;
        if (btl._outcome.Length == 0 || btl._outcomeQuiet) return;
        double since = (host._lastTime - btl._outcomeAt) * TicksPerSecond;
        int level = Math.Clamp((int)since, 0, DimLevel);
        int left = host._camX, top = host._camY, w = host.ViewWidth, h = host.ViewHeight;
        if (level > 0) Dim(host, left, top, w, h, level);
        if (since < OutcomeBannerDelayTicks) return;

        bool win = btl._outcome.StartsWith('승');
        int cx = left + w / 2, cy = top + h / 2;
        if (host.UiFor(Obs) is not { } sprite)
        {
            // 파트1 그림이 없으면 글자로
            string text = win ? "Scenario Clear" : "Game Over";
            var (_, tw, th) = host.GetText(text, 0xFFFFFFFF, 32);
            host.DrawText(text, cx - tw / 2, cy - th / 2, 0xFFFFFFFF, 32);
            return;
        }
        // 띠 — 가운데 한 장과 좌우 넷씩(모션 8 · 10 의 자식 키 자리), 섞기 10.
        int step = win ? 142 : 143;
        if (sprite.FrameAt(win ? 8 : 10, 0) is { } tile)
            for (int k = -4; k <= 4; k++)
                Blend(host, tile, cx + k * step, cy, Glow.Mix);
        // 글 — 번진 그림(모션 4 · 6 / 5 · 7, 섞기 13 세기 4) 위에 글씨(모션 0 · 2 / 1 · 3, 섞기 9 더하기).
        foreach (int m in win ? (int[])[4, 6] : [5, 7])
            host.DrawUi(Obs, m, 0, cx, cy, Part1Blend.AsUiBlend(Part1Game.BlendKey(13, 4)));
        foreach (int m in win ? (int[])[0, 2] : [1, 3])
            if (sprite.FrameAt(m, 0) is { } f) Blend(host, f, cx, cy, Add.Mix);
    }

    /// <summary>방식 15 물들이기 — 채널마다 <c>c·(31−L)/31</c>.</summary>
    static void Dim(GameWindow host, int left, int top, int w, int h, int level)
    {
        var fb = host._fb;
        int stride = host.BoardWidth, keep = 31 - level;
        int x0 = Math.Max(0, left), x1 = Math.Min(stride, left + w), y0 = Math.Max(0, top), y1 = Math.Min(fb.Length / Math.Max(1, stride), top + h);
        for (int y = y0; y < y1; y++)
            for (int i = y * stride + x0, end = y * stride + x1; i < end; i++)
            {
                uint c = fb[i];
                uint Ch(int shift) => (uint)((int)(c >> shift & 0xFF) * keep / 31);
                fb[i] = 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
            }
    }

    /// <summary>섞기 10 — <c>min(31, 바탕 + 그림²/31)</c> · 섞기 9 — <c>min(31, 바탕 + 그림)</c>(원본 5비트 색표, <see cref="Part1Blend"/>).</summary>
    static readonly Part1Blend.Plan Glow = Part1Blend.PlanOf(Part1Game.BlendKey(10, 0))!, Add = Part1Blend.PlanOf(Part1Game.BlendKey(9, 0))!;

    /// <summary>컷을 기준점 (x, y) 에 <paramref name="mix"/>(바탕, 그림) 로 찍는다 — 판 밖은 자른다.</summary>
    static void Blend(GameWindow host, SpriteFrame f, int x, int y, Func<uint, uint, uint> mix)
    {
        var fb = host._fb;
        int stride = host.BoardWidth, rows = fb.Length / Math.Max(1, stride);
        int left = x + f.X, top = y + f.Y;
        int x0 = Math.Max(0, left), x1 = Math.Min(stride, left + f.W), y0 = Math.Max(0, top), y1 = Math.Min(rows, top + f.H);
        for (int py = y0; py < y1; py++)
            for (int px = x0; px < x1; px++)
            {
                uint c = f.Px[(py - top) * f.W + px - left];
                if ((c & 0xFF000000) == 0) continue;
                int i = py * stride + px;
                fb[i] = mix(fb[i], c);
            }
    }
}
