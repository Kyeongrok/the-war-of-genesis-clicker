using DuelDx.Native;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 「익명 통계를 보내도 될까요?」 — 전투 통계(<see cref="BattleStats"/>)를 보내도 되는지 게임을 켤 때 묻는 창(사용자 요청 menu-14).
/// </summary>
/// <remarks>
/// 보낼 수 있는 판(받는 곳 주소가 있는 릴리즈 판)에서 통계 보내기가 꺼져 있을 때 켤 때 뜬다. 동의하면 바로 켜고 다시 안 묻는다.
/// 동의하지 않으면 <see cref="AskAgainDays"/> 일 뒤에 다시 묻는다(마지막으로 물은 때를 설정에 적는다). 답은 모드 &gt; 편의성
/// 「익명 전투 통계 보내기」에서 언제든 바꾼다. 릴리즈 노트 창이 떠 있으면 그것이 닫힌 뒤에 보인다.
/// DUELDX_CONSENT=1 이면 조건 없이, DUELDX_CONSENT=due 면 판 조건만 빼고(날짜는 보고) 띄운다(화면 밖 시험용).
/// </remarks>
internal sealed unsafe class StatsConsentScreen(GameWindow host)
{
    internal const int W = 500, H = 250, ButtonW = 130, ButtonH = 30;

    /// <summary>동의하지 않은 사람에게 다시 묻는 간격(일).</summary>
    internal const int AskAgainDays = 7;

    internal bool _pending;

    /// <summary>지금 물을 때인가 — 켜져 있으면 안 묻고, 꺼져 있으면 한 번도 안 물었거나 마지막으로 물은 지 이레가 지났을 때.</summary>
    internal static bool Due(bool sending, string askedAt, DateTime now) =>
        !sending && (!DateTime.TryParse(askedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var asked) || now - asked >= TimeSpan.FromDays(AskAgainDays));

    internal bool Open => _pending && !host.NotesScr.Open;

    internal static readonly string[] Lines =
    [
        "게임을 다듬는 데 쓰려고 전투 통계를 모으고 있습니다.",
        "",
        "보내는 것: 전투에서 쓴 어빌리티의 횟수와 피해, 전투 번호, 승패,",
        "              게임 판 번호, 설치마다 하나인 무작위 번호",
        "보내지 않는 것: 이름, 계정, 세이브 내용",
        "",
        "익명 통계를 보내도 될까요?",
    ];

    internal void OpenIfNeeded()
    {
        string? hook = Environment.GetEnvironmentVariable("DUELDX_CONSENT");
        _pending = hook == "1" || ((hook == "due" || host.Stats.CanUploadBuild) && Due(host._sendStats, host._statsAskedAt, DateTime.UtcNow));
    }

    internal void Answer(bool send)
    {
        _pending = false;
        (host._statsAskedAt, host._sendStats) = (DateTime.UtcNow.ToString("o"), send);
        host.SaveSettings();
        host.Toast(send ? "고맙습니다 — 전투가 끝나면 통계를 익명으로 보냅니다" : "통계를 보내지 않습니다 — 모드 > 편의성에서 켤 수 있습니다");
    }

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    private (int X, int Y) ButtonAt(int index)
    {
        var (x, y) = Origin();
        return (x + W / 2 + (index == 0 ? -ButtonW - 10 : 10), y + H - ButtonH - 16);
    }

    private static bool In(int bx, int by, int x, int y, int w, int h) => bx >= x && bx < x + w && by >= y && by < y + h;

    /// <summary>열려 있으면 클릭을 먹는다 — 단추 둘 가운데 하나를 골라야 닫힌다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!Open) return false;
        for (int i = 0; i < 2; i++)
        {
            var (x, y) = ButtonAt(i);
            if (In(bx, by, x, y, ButtonW, ButtonH)) { Answer(i == 0); break; }
        }
        return true;
    }

    /// <summary>Enter = 보내기 · Esc = 보내지 않기.</summary>
    internal void OnKey(int key)
    {
        if (key == Win32.VK_RETURN) Answer(true);
        else if (key == Win32.VK_ESCAPE) Answer(false);
    }

    internal void Draw()
    {
        if (!Open) return;
        var (x, y) = Origin();
        host.FillRect(x - 4, y - 4, W + 8, H + 8, 0x80000000);
        host.FillRect(x, y, W, H, StatusScreen.PanelBg);
        host.StrokeRect(x, y, W, H, StatusScreen.BoxLine);
        host.FillRect(x, y, W, 28, StatusScreen.HeadBg);
        host.DrawText("전투 통계 보내기", x + 10, y + 6, White);

        for (int i = 0; i < Lines.Length; i++)
            host.DrawText(Lines[i], x + 20, y + 42 + i * 18, i == Lines.Length - 1 ? 0xFFFFE070 : White, 12);
        host.DrawText("모드 > 편의성에서 언제든 바꿀 수 있습니다.", x + 20, y + 42 + Lines.Length * 18 + 2, DimGray, 11);

        string[] labels = ["보내기 (Enter)", "보내지 않기 (Esc)"];
        for (int i = 0; i < 2; i++)
        {
            var (bx, by) = ButtonAt(i);
            bool here = In(host._mouse.X, host._mouse.Y, bx, by, ButtonW, ButtonH);
            host.FillRect(bx, by, ButtonW, ButtonH, here ? 0xFF2A4A8A : StatusScreen.HeadBg);
            host.StrokeRect(bx, by, ButtonW, ButtonH, StatusScreen.BoxLine);
            host.DrawText(labels[i], bx + (ButtonW - host.GetText(labels[i], White, 12).W) / 2, by + 7, White, 12);
        }
    }
}
