using DuelDx.Native;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 타이밍 어택 수정 창 — 메뉴 막대 개발 &gt; 타이밍 어택 수정. 타이밍 공격(<see cref="TimedHit"/>)의 수치를 게임 안에서 고치며 맞춘다(사용자 요청 fg-32).
/// 줄마다 값과 − − / − / + / + + 단추, 아래에 「미리 보기」(띠를 이 창 안에서 한 번 돌려 본다 — Space · LT · RT 로 눌러 볼 수 있다).
/// 고치면 바로 적용되고 <c>timedhit.json</c> 에 남는다.
/// </summary>
internal sealed class TimedHitEditScreen(GameWindow host)
{
    internal const int MenuTimedEdit = 1173;
    internal const int W = 620, H = 470, RowY = 44, RowH = 30, ValueX = 170, ButtonW = 34, ButtonGap = 4, PreviewY = 300, FootY = H - 40;

    /// <summary>DUELDX_TIMEDEDIT 가 있으면 열어 둔 채 시작한다(화면 밖 시험용).</summary>
    internal bool _open = Environment.GetEnvironmentVariable("DUELDX_TIMEDEDIT") != null;

    private IEnumerator<bool>? _preview;

    private TimedHitConfig Cfg => host.TimedHitFx.Cfg;

    /// <summary>고칠 수 있는 줄 — 이름, 읽기, 쓰기, 작은 걸음 · 큰 걸음, 아래 · 위 끝, 보이는 꼴, 덧붙일 말.</summary>
    internal (string Label, Func<double> Get, Action<double> Set, double Step, double Big, double Min, double Max, string Format, string Note)[] Rows() =>
    [
        ("띠 길이", () => Cfg.Seconds, v => Cfg.Seconds = v, 0.01, 0.1, 0.3, 4, "0.00초", "표시가 띠 끝까지 가는 시간"),
        ("첫 박자", () => Cfg.Beat1, v => Cfg.Beat1 = v, 0.01, 0.05, 0.05, 4, "0.00초", "시작에서 첫 칸 가운데까지"),
        ("둘째 박자", () => Cfg.Beat2, v => Cfg.Beat2 = v, 0.01, 0.05, 0.05, 4, "0.00초", "시작에서 둘째 칸 가운데까지"),
        ("칸 반 너비", () => Cfg.Tolerance, v => Cfg.Tolerance = v, 0.01, 0.05, 0.01, 1, "±0.00초", "띠에 그리는 칸의 폭"),
        ("판정 여유", () => Cfg.Grace, v => Cfg.Grace = v, 0.01, 0.05, 0, 0.5, "+0.00초", "그린 칸 밖으로 더 받아 주는 폭"),
        ("결과 표시", () => Cfg.ShowResultSeconds, v => Cfg.ShowResultSeconds = v, 0.05, 0.2, 0.1, 3, "0.00초", "「크리티컬!」 따위가 떠 있는 시간"),
        ("한 타 배율", () => Cfg.DamagePercent, v => Cfg.DamagePercent = (int)Math.Round(v), 10, 50, 100, 500, "0'%'", "두 번 치는 동작이 없는 인물용"),
        ("띠 너비", () => Cfg.BarWidth, v => Cfg.BarWidth = (int)Math.Round(v), 10, 40, 100, 500, "0px", "화면에 그리는 띠의 너비"),
    ];

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    private bool MouseIn(int x, int y, int w, int h) => host._mouse.X >= x && host._mouse.X < x + w && host._mouse.Y >= y && host._mouse.Y < y + h;

    /// <summary>줄 i 의 단추 넷(− − · − · + · + +)의 자리와 걸음 배수.</summary>
    internal (int X, int Sign, bool Big)[] StepButtons(int x) =>
    [
        (x + ValueX, -1, true), (x + ValueX + ButtonW + ButtonGap, -1, false),
        (x + ValueX + 2 * (ButtonW + ButtonGap) + 90, 1, false), (x + ValueX + 3 * (ButtonW + ButtonGap) + 90, 1, true),
    ];

    /// <summary>아래 단추 — 미리 보기 · 기본값 · 켜고 끄기 · 닫기.</summary>
    internal (int X, int W, string Label)[] FootButtons(int x) =>
    [
        (x + 16, 110, "미리 보기"), (x + 134, 90, "기본값"), (x + 232, 190, host._timedHit ? "타이밍 공격: 켜짐" : "타이밍 공격: 꺼짐"), (x + W - 116, 100, "닫기"),
    ];

    /// <summary>패드 커서가 건너뛸 자리(<see cref="GamePad.Targets"/>).</summary>
    internal void AddTargets(List<(int X, int Y, int W, int H)> list)
    {
        var (x, y) = Origin();
        int rows = Rows().Length;
        for (int i = 0; i < rows; i++)
            foreach (var (bx, _, _) in StepButtons(x)) list.Add((bx, y + RowY + i * RowH, ButtonW, RowH - 6));
        foreach (var (bx, bw, _) in FootButtons(x)) list.Add((bx, y + FootY, bw, 28));
    }

    /// <summary>열려 있으면 클릭을 먹는다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        var (x, y) = Origin();
        var rows = Rows();
        for (int i = 0; i < rows.Length; i++)
        {
            int ry = y + RowY + i * RowH;
            if (by < ry || by >= ry + RowH - 6) continue;
            foreach (var (sx, sign, big) in StepButtons(x))
            {
                if (bx < sx || bx >= sx + ButtonW) continue;
                var row = rows[i];
                row.Set(Math.Round(Math.Clamp(row.Get() + sign * (big ? row.Big : row.Step), row.Min, row.Max), 2));
                Cfg.Save();
                return true;
            }
        }
        var foot = FootButtons(x);
        for (int i = 0; i < foot.Length; i++)
        {
            if (bx < foot[i].X || bx >= foot[i].X + foot[i].W || by < y + FootY || by >= y + FootY + 28) continue;
            switch (i)
            {
                case 0: StartPreview(); break;
                case 1: host.TimedHitFx.Cfg = new TimedHitConfig(); Cfg.Save(); host.Toast("타이밍 공격 수치를 기본값으로 되돌렸습니다"); break;
                case 2: host.OnMenuCommand(MenuTimedHit); break;
                case 3: _open = false; break;
            }
            return true;
        }
        return true;
    }

    /// <summary>띠를 이 창 안에서 한 번 돌린다 — 전투의 띠가 돌고 있으면 건드리지 않는다.</summary>
    internal void StartPreview()
    {
        if (host.TimedHitFx.Running) return;
        _preview = host.TimedHitFx.Prompt(null, () =>
        {
            var (x, y) = Origin();
            return (x + (W - Math.Clamp(Cfg.BarWidth, 60, 600)) / 2, y + PreviewY + TimedHit.AboveBar);
        }).GetEnumerator();
    }

    internal void OnKey(int key)
    {
        if (key == Win32.VK_ESCAPE) _open = false;
        else if (key is Win32.VK_RETURN or Win32.VK_SPACE) StartPreview();   // 띠가 도는 동안의 Space 는 띠가 먼저 받는다(GameWindow.OnKeyDown)
    }

    internal void Draw()
    {
        // 미리 보기는 창이 닫혀도 끝까지 돌려 입력을 놓아준다.
        if (_preview != null && !_preview.MoveNext()) _preview = null;
        if (!_open) return;
        var (x, y) = Origin();
        host.FillRect(x - 4, y - 4, W + 8, H + 8, 0x80000000);
        host.FillRect(x, y, W, H, StatusScreen.PanelBg);
        host.StrokeRect(x, y, W, H, StatusScreen.BoxLine);
        host.FillRect(x, y, W, 28, StatusScreen.HeadBg);
        host.DrawText("타이밍 어택 수정 — 고치면 바로 적용 · timedhit.json 에 저장", x + 10, y + 6, White);

        var rows = Rows();
        for (int i = 0; i < rows.Length; i++)
        {
            int ry = y + RowY + i * RowH;
            host.DrawText(rows[i].Label, x + 16, ry + 4, White);
            var buttons = StepButtons(x);
            for (int b = 0; b < buttons.Length; b++)
            {
                var (bx, sign, big) = buttons[b];
                bool here = MouseIn(bx, ry, ButtonW, RowH - 6);
                host.FillRect(bx, ry, ButtonW, RowH - 6, here ? 0xFF2A4A8A : StatusScreen.BoxBg);
                host.StrokeRect(bx, ry, ButtonW, RowH - 6, StatusScreen.BoxLine);
                string mark = (sign < 0 ? "−" : "+") + (big ? (sign < 0 ? "−" : "+") : "");
                host.DrawText(mark, bx + (ButtonW - host.GetText(mark, White).W) / 2, ry + 3, White);
            }
            string value = rows[i].Get().ToString(rows[i].Format);
            int vx = x + ValueX + 2 * (ButtonW + ButtonGap);
            host.FillRect(vx, ry, 86, RowH - 6, 0xFF101828);
            host.DrawText(value, vx + (86 - host.GetText(value, White).W) / 2, ry + 4, 0xFFFFE070);
            host.DrawText(rows[i].Note, x + ValueX + 4 * (ButtonW + ButtonGap) + 98, ry + 6, DimGray, 12);
        }

        // 미리 보기 칸 — 띠가 돌지 않을 때는 지금 수치의 판정 구간을 글로.
        int py = y + PreviewY;
        host.FillRect(x + 12, py - 8, W - 24, 1, StatusScreen.BoxLine);
        host.FillRect(x + 12, py, W - 24, TimedHit.AboveBar + 12 + TimedHit.BelowBar, 0xFF0C1420);
        if (host.TimedHitFx.Running) host.TimedHitFx.Draw(preview: true);
        else
        {
            double t = Cfg.Tolerance + Cfg.Grace;
            host.DrawText($"판정 구간: {Cfg.Beat1 - t:0.00}~{Cfg.Beat1 + t:0.00}초 · {Cfg.Beat2 - t:0.00}~{Cfg.Beat2 + t:0.00}초   (띠 {Cfg.Seconds:0.00}초)", x + 24, py + 18, 0xFFFFE070, 12);
            string warn = Cfg.Beat1 + t > Cfg.Beat2 - t ? "두 구간이 겹칩니다" : Cfg.Beat2 + Cfg.Tolerance > Cfg.Seconds ? "둘째 칸이 띠 밖으로 나갑니다" : "";
            if (warn.Length > 0) host.DrawText(warn, x + 24, py + 40, 0xFFFF7060, 12);
            host.DrawText("「미리 보기」(Enter · Space)로 띠를 돌려 본다 — 도는 동안 Space · LT · RT 로 눌러 본다", x + 24, py + 66, DimGray, 12);
        }

        var foot = FootButtons(x);
        for (int i = 0; i < foot.Length; i++)
        {
            bool here = MouseIn(foot[i].X, y + FootY, foot[i].W, 28);
            host.FillRect(foot[i].X, y + FootY, foot[i].W, 28, here ? 0xFF2A4A8A : i == 3 ? StatusScreen.HeadBg : StatusScreen.BoxBg);
            host.StrokeRect(foot[i].X, y + FootY, foot[i].W, 28, StatusScreen.BoxLine);
            host.DrawText(foot[i].Label, foot[i].X + (foot[i].W - host.GetText(foot[i].Label, White).W) / 2, y + FootY + 6, i == 2 && host._timedHit ? 0xFF00FFFF : White);
        }
    }
}
