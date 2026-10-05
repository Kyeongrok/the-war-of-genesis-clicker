using DuelDx.Native;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 전투 통계(<see cref="BattleStats"/>)를 익명으로 모은다는 것을 알리는 창, 그리고 꺼 둔 사람에게 켜기를 권하는 창(사용자 요청 menu-14).
/// </summary>
/// <remarks>
/// 통계 보내기는 기본 켬이다. 보낼 수 있는 판(받는 곳 주소가 있는 릴리즈 판)에서 게임을 켤 때:
/// <list type="bullet">
/// <item>켜져 있고 아직 안 알렸으면 — <b>알림</b>: 무엇을 모으는지와 끄는 곳(모드 &gt; 편의성 「익명 전투 통계 보내기」)을 보이고 「확인」으로 닫는다. 한 번만.</item>
/// <item>꺼져 있고 마지막으로 알리거나 권한 지 <see cref="AskAgainDays"/> 일이 지났으면 — <b>권유</b>: 「켜기」 / 「그대로 두기」.</item>
/// </list>
/// 마지막으로 띄운 때를 설정(<c>StatsAskedAt</c>)에 적는다. 모드 창에서 끄면 그때부터 다시 센다. 릴리즈 노트 창이 떠 있으면 그것이 닫힌 뒤에 보인다.
/// DUELDX_CONSENT=1 이면 판 조건 없이 같은 규칙으로 띄운다(화면 밖 시험용).
/// </remarks>
internal sealed unsafe class StatsConsentScreen(GameWindow host)
{
    internal const int W = 500, H = 250, ButtonW = 130, ButtonH = 30;

    /// <summary>꺼 둔 사람에게 다시 권하는 간격(일).</summary>
    internal const int AskAgainDays = 7;

    /// <summary>0 닫힘 · 1 알림(확인 하나) · 2 권유(켜기 / 그대로 두기).</summary>
    internal int _mode;

    internal bool Open => _mode != 0 && !host.NotesScr.Open;

    /// <summary>지금 띄울 창 — 켜져 있으면 아직 안 알렸을 때 알림, 꺼져 있으면 마지막으로 띄운 지 이레가 지났을 때 권유.</summary>
    internal static int ModeFor(bool sending, string askedAt, DateTime now)
    {
        bool known = DateTime.TryParse(askedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var asked);
        if (sending) return known ? 0 : 1;
        return !known || now - asked >= TimeSpan.FromDays(AskAgainDays) ? 2 : 0;
    }

    internal void OpenIfNeeded()
    {
        bool forced = Environment.GetEnvironmentVariable("DUELDX_CONSENT") == "1";
        _mode = forced || host.Stats.CanUploadBuild ? ModeFor(host._sendStats, host._statsAskedAt, DateTime.UtcNow) : 0;
    }

    /// <summary>창을 닫는다 — 띄운 때를 적고, <paramref name="turnOn"/> 이면 통계 보내기를 켠다(알림의 「확인」은 설정을 안 바꾼다).</summary>
    internal void Close(bool turnOn)
    {
        bool proposal = _mode == 2;
        _mode = 0;
        host._statsAskedAt = DateTime.UtcNow.ToString("o");
        if (turnOn) host._sendStats = true;
        host.SaveSettings();
        if (proposal) host.Toast(turnOn ? "고맙습니다 — 전투가 끝나면 통계를 익명으로 보냅니다" : "통계를 보내지 않습니다 — 모드 > 편의성에서 켤 수 있습니다");
    }

    internal string[] Lines() => _mode == 2 ?
    [
        "익명 전투 통계 보내기가 꺼져 있습니다.",
        "",
        "보내는 것: 전투에서 쓴 어빌리티의 횟수와 피해, 전투 번호, 승패,",
        "              게임 판 번호, 설치마다 하나인 무작위 번호",
        "보내지 않는 것: 이름, 계정, 세이브 내용",
        "",
        "게임을 다듬는 데 도움이 됩니다. 켜 주시겠어요?",
    ] :
    [
        "게임을 다듬는 데 쓰려고 전투 통계를 익명으로 모읍니다.",
        "",
        "보내는 것: 전투에서 쓴 어빌리티의 횟수와 피해, 전투 번호, 승패,",
        "              게임 판 번호, 설치마다 하나인 무작위 번호",
        "보내지 않는 것: 이름, 계정, 세이브 내용",
        "",
        "원하지 않으시면 모드 > 편의성 「익명 전투 통계 보내기」에서 끌 수 있습니다.",
    ];

    /// <summary>단추들 — 글과 「켜기」인지.</summary>
    internal (string Label, bool TurnOn)[] Buttons() => _mode == 2 ? [("켜기 (Enter)", true), ("그대로 두기 (Esc)", false)] : [("확인 (Enter)", false)];

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    private (int X, int Y) ButtonAt(int index, int count)
    {
        var (x, y) = Origin();
        int total = count * ButtonW + (count - 1) * 20;
        return (x + (W - total) / 2 + index * (ButtonW + 20), y + H - ButtonH - 16);
    }

    private static bool In(int bx, int by, int x, int y, int w, int h) => bx >= x && bx < x + w && by >= y && by < y + h;

    /// <summary>열려 있으면 클릭을 먹는다 — 단추를 눌러야 닫힌다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!Open) return false;
        var buttons = Buttons();
        for (int i = 0; i < buttons.Length; i++)
        {
            var (x, y) = ButtonAt(i, buttons.Length);
            if (In(bx, by, x, y, ButtonW, ButtonH)) { Close(buttons[i].TurnOn); break; }
        }
        return true;
    }

    /// <summary>Enter = 첫 단추(확인 · 켜기) · Esc = 닫기(권유면 그대로 두기).</summary>
    internal void OnKey(int key)
    {
        if (key == Win32.VK_RETURN) Close(Buttons()[0].TurnOn);
        else if (key == Win32.VK_ESCAPE) Close(false);
    }

    internal void Draw()
    {
        if (!Open) return;
        var (x, y) = Origin();
        host.FillRect(x - 4, y - 4, W + 8, H + 8, 0x80000000);
        host.FillRect(x, y, W, H, StatusScreen.PanelBg);
        host.StrokeRect(x, y, W, H, StatusScreen.BoxLine);
        host.FillRect(x, y, W, 28, StatusScreen.HeadBg);
        host.DrawText(_mode == 2 ? "전투 통계 보내기" : "전투 통계 알림", x + 10, y + 6, White);

        var lines = Lines();
        for (int i = 0; i < lines.Length; i++)
            host.DrawText(lines[i], x + 20, y + 42 + i * 18, i == lines.Length - 1 ? 0xFFFFE070 : White, 12);

        var buttons = Buttons();
        for (int i = 0; i < buttons.Length; i++)
        {
            var (bx, by) = ButtonAt(i, buttons.Length);
            bool here = In(host._mouse.X, host._mouse.Y, bx, by, ButtonW, ButtonH);
            host.FillRect(bx, by, ButtonW, ButtonH, here ? 0xFF2A4A8A : StatusScreen.HeadBg);
            host.StrokeRect(bx, by, ButtonW, ButtonH, StatusScreen.BoxLine);
            host.DrawText(buttons[i].Label, bx + (ButtonW - host.GetText(buttons[i].Label, White, 12).W) / 2, by + 7, White, 12);
        }
    }
}
