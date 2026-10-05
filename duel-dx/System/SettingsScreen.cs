using DuelDx.Native;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 설정 창 — 메뉴 막대의 「설정」을 누르면 모드 창처럼 뜬다(사용자 요청 menu-19). 왼쪽에 갈래(화면 · 게임 · 키보드 · 패드), 오른쪽에 그 갈래의 항목.
/// </summary>
/// <remarks>
/// 전에 펼침 메뉴에 있던 것(해상도 · 배율 · 격자 · 상단 상태 줄 · 장면 번호 · 난이도 · 게임 속도 · 단축키 설정 · 끝내기)을 옮겼다 —
/// 값을 바꾸는 일은 예전 메뉴 명령(<see cref="GameWindow.OnMenuCommand"/>)을 그대로 돌린다. 패드 갈래는 단추 묶음(<see cref="GamePad"/>)을 바꾼다:
/// 줄을 누르고 패드 단추를 누르면 그 동작에 묶인다. 원본에 없는 창이다.
/// </remarks>
internal sealed unsafe class SettingsScreen(GameWindow host)
{
    internal const int MenuSettings = 1166;
    internal const int W = 620, H = 440, SideW = 130, SideRowH = 34, RowH = 34, PadRowH = 28;

    /// <summary>DUELDX_SETTINGS=&lt;갈래&gt; 면 그 갈래로 열어 둔 채 시작한다(화면 밖 시험용).</summary>
    internal bool _open = Environment.GetEnvironmentVariable("DUELDX_SETTINGS") != null;
    internal int _page = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_SETTINGS"), out int start) ? Math.Clamp(start, 0, 3) : 0;

    internal static readonly string[] Pages = ["화면", "게임", "키보드", "패드"];

    internal void Open()
    {
        _open = true;
        host.Pad._capture = null;
        host.Btl._heldMoveKeys.Clear();
    }

    internal void Close()
    {
        _open = false;
        host.Pad._capture = null;
    }

    /// <summary>줄 하나 — 고르기(◀ 값 ▶)면 <see cref="Step"/>, 켜고 끄기면 <see cref="Toggle"/>, 단추면 <see cref="Press"/>.</summary>
    internal sealed record Row(string Label, string Value, Action<int>? Step = null, Action? Toggle = null, bool On = false, Action? Press = null, string Note = "");

    private static int Cycle(int index, int delta, int count) => ((index + delta) % count + count) % count;

    internal Row[] Rows()
    {
        switch (_page)
        {
            case 0:
            {
                int res = Math.Max(0, Array.FindIndex(ResChoices, r => r.W == host._viewW && r.H == host._viewH));
                int zoom = Math.Max(0, Array.IndexOf(ZoomChoices, host._zoomPercent));
                return
                [
                    new("해상도", res == 0 ? $"{host._viewW}×{host._viewH} (원본)" : $"{host._viewW}×{host._viewH}",
                        Step: d => host.OnMenuCommand(MenuResBase + Cycle(res, d, ResChoices.Length))),
                    new("배율", host._zoomPercent == 0 ? "자동(화면에 맞춤)" : $"{host._zoomPercent}%",
                        Step: d => host.OnMenuCommand(MenuZoomAuto + Cycle(zoom, d, ZoomChoices.Length))),
                    new("격자 보이기", "", Toggle: () => host.OnMenuCommand(MenuGrid), On: host._showGrid),
                    new("상단 상태 줄 보이기", "", Toggle: () => host.OnMenuCommand(MenuStatusBar), On: host._showStatusBar),
                    new("장면 번호 보이기", "", Toggle: () => host.OnMenuCommand(MenuSceneTag), On: host._showSceneTag, Note: "왼쪽 아래 Fld · Btl · Chp 번호"),
                ];
            }
            case 1:
            {
                int speed = Math.Max(0, Array.IndexOf(SpeedChoices, host._gameSpeed));
                int hard = host.Btl._difficulty;
                var (name, hp, damage) = BattleScene.DifficultyChoices[hard];
                return
                [
                    new("난이도", name, Step: d => host.OnMenuCommand(BattleScene.MenuDifficultyBase + Cycle(hard, d, BattleScene.DifficultyChoices.Length)),
                        Note: hard == 0 ? "" : $"적 HP {hp}% · 적 피해 {damage}% · AI 강화"),
                    new("게임 속도", host._gameSpeed == 100 ? "보통(원본)" : $"{host._gameSpeed / 100.0:0.#}배",
                        Step: d => host.OnMenuCommand(MenuSpeedBase + Cycle(speed, d, SpeedChoices.Length))),
                    new("게임 끝내기", "", Press: () => host.OnMenuCommand(MenuExit)),
                ];
            }
            case 2:
                return [new("단축키 설정 창 열기…", "", Press: () => { Close(); host.OnMenuCommand(MenuKeys); }, Note: "동작마다 키를 바꾼다")];
            default:
                return [];
        }
    }

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    private bool In(int bx, int by, int x, int y, int w, int h) => bx >= x && bx < x + w && by >= y && by < y + h;

    private bool Over(int x, int y, int w, int h) => In(host._mouse.X, host._mouse.Y, x, y, w, h);

    /// <summary>열려 있으면 클릭을 먹는다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        var (x, y) = Origin();
        if (In(bx, by, x + W - 116, y + H - 40, 100, 28)) { Close(); return true; }
        for (int i = 0; i < Pages.Length; i++)
            if (In(bx, by, x + 12, y + 40 + i * SideRowH, SideW, SideRowH - 4)) { _page = i; host.Pad._capture = null; return true; }

        int px = x + SideW + 30, top = y + 44;
        if (_page == 3)
        {
            for (int i = 0; i < GamePad.All.Length; i++)
                if (In(bx, by, px, top + 26 + i * PadRowH, W - SideW - 46, PadRowH - 4)) { host.Pad._capture = GamePad.All[i].Action; return true; }
            if (In(bx, by, px, y + H - 40, 120, 28)) { host.Pad.ResetDefaults(); host.Pad._capture = null; }
            return true;
        }
        var rows = Rows();
        for (int i = 0; i < rows.Length; i++)
        {
            int ry = top + i * RowH;
            var row = rows[i];
            if (row.Step != null)
            {
                if (In(bx, by, px + 170, ry, 24, 24)) { row.Step(-1); return true; }
                if (In(bx, by, px + 170 + 24 + 184, ry, 24, 24)) { row.Step(1); return true; }
            }
            else if (row.Toggle != null && In(bx, by, px, ry, 200, 24)) { row.Toggle(); return true; }
            else if (row.Press != null && In(bx, by, px, ry, 200, 26)) { row.Press(); return true; }
        }
        return true;
    }

    internal void OnKey(int key)
    {
        if (key != Win32.VK_ESCAPE) return;
        if (host.Pad._capture != null) host.Pad._capture = null;   // 단추를 기다리는 중이면 그것만 그만둔다
        else Close();
    }

    private void Button(int x, int y, int w, int h, string text)
    {
        host.FillRect(x, y, w, h, Over(x, y, w, h) ? 0xFF2A4A8A : StatusScreen.BoxBg);
        host.StrokeRect(x, y, w, h, StatusScreen.BoxLine);
        var (_, tw, th) = host.GetText(text, White, 12);
        host.DrawText(text, x + (w - tw) / 2, y + (h - th) / 2, White, 12);
    }

    internal void Draw()
    {
        if (!_open) return;
        var (x, y) = Origin();
        host.FillRect(x - 4, y - 4, W + 8, H + 8, 0x80000000);
        host.FillRect(x, y, W, H, StatusScreen.PanelBg);
        host.StrokeRect(x, y, W, H, StatusScreen.BoxLine);
        host.FillRect(x, y, W, 28, StatusScreen.HeadBg);
        host.DrawText("설정", x + 10, y + 6, White);

        for (int i = 0; i < Pages.Length; i++)
        {
            int ry = y + 40 + i * SideRowH;
            bool on = i == _page;
            host.FillRect(x + 12, ry, SideW, SideRowH - 4, on ? 0xFF2A4A8A : Over(x + 12, ry, SideW, SideRowH - 4) ? 0xFF1C2F5A : StatusScreen.BoxBg);
            host.StrokeRect(x + 12, ry, SideW, SideRowH - 4, StatusScreen.BoxLine);
            host.DrawText(Pages[i], x + 24, ry + 7, on ? 0xFF00FFFF : White);
        }
        host.FillRect(x + SideW + 20, y + 40, 1, H - 90, StatusScreen.BoxLine);

        int px = x + SideW + 30, top = y + 44;
        if (_page == 3) DrawPad(px, top, y);
        else
        {
            var rows = Rows();
            for (int i = 0; i < rows.Length; i++)
            {
                int ry = top + i * RowH;
                var row = rows[i];
                if (row.Step != null)
                {
                    host.DrawText(row.Label, px, ry + 4, White);
                    Button(px + 170, ry, 24, 24, "◀");
                    host.FillRect(px + 194, ry, 184, 24, StatusScreen.BoxBg);
                    host.StrokeRect(px + 194, ry, 184, 24, StatusScreen.BoxLine);
                    host.DrawText(row.Value, px + 194 + (184 - host.GetText(row.Value, White, 12).W) / 2, ry + 5, White, 12);
                    Button(px + 378, ry, 24, 24, "▶");
                    if (row.Note.Length > 0) host.DrawText(row.Note, px + 170, ry + 25, DimGray, 10);
                }
                else if (row.Toggle != null)
                {
                    host.StrokeRect(px, ry + 3, 16, 16, StatusScreen.BoxLine);
                    if (row.On) host.FillRect(px + 3, ry + 6, 10, 10, 0xFF00D8FF);
                    host.DrawText(row.Label, px + 26, ry + 3, White);
                    if (row.Note.Length > 0) host.DrawText(row.Note, px + 200, ry + 5, DimGray, 12);
                }
                else
                {
                    Button(px, ry, 200, 26, row.Label);
                    if (row.Note.Length > 0) host.DrawText(row.Note, px + 214, ry + 6, DimGray, 12);
                }
            }
        }

        int fy = y + H - 40;
        host.FillRect(x + W - 116, fy, 100, 28, StatusScreen.HeadBg);
        host.DrawText("닫기", x + W - 80, fy + 6, White);
        if (_page != 3) host.DrawText("Esc: 닫기", x + 16, fy + 7, DimGray);
    }

    /// <summary>패드 갈래 — 동작마다 묶인 단추. 줄을 누르면 다음에 누르는 패드 단추가 그 동작에 묶인다.</summary>
    private void DrawPad(int px, int top, int y)
    {
        var pad = host.Pad;
        host.DrawText(pad.Connected ? "패드 연결됨 — 줄을 누르고 패드 단추를 누르세요" : "패드가 연결되어 있지 않습니다 (연결하면 바로 잡힙니다)",
                      px, top, pad.Connected ? 0xFF90E0A0 : 0xFFFFB070, 12);
        int rowW = W - SideW - 46;
        for (int i = 0; i < GamePad.All.Length; i++)
        {
            var (action, label, _) = GamePad.All[i];
            int ry = top + 26 + i * PadRowH;
            bool capturing = pad._capture == action;
            if (capturing) host.FillRect(px - 4, ry - 2, rowW + 8, PadRowH - 2, 0xFF2A4A8A);
            else if (Over(px, ry, rowW, PadRowH - 4)) host.FillRect(px - 4, ry - 2, rowW + 8, PadRowH - 2, 0xFF1C2F5A);
            host.DrawText(label, px, ry + 3, White);
            host.FillRect(px + 220, ry, rowW - 220, PadRowH - 6, StatusScreen.BoxBg);
            host.StrokeRect(px + 220, ry, rowW - 220, PadRowH - 6, StatusScreen.BoxLine);
            host.DrawText(capturing ? "패드 단추를 누르세요… (Esc: 그만)" : GamePad.ButtonName(pad.ButtonOf(action)), px + 228, ry + 3, capturing ? 0xFFFFE070 : White, 12);
        }
        host.DrawText("이동은 십자키 · 왼쪽 스틱(고정). 이미 쓰는 단추를 고르면 두 동작의 단추가 맞바뀝니다.", px, top + 26 + GamePad.All.Length * PadRowH + 4, DimGray, 11);
        Button(px, y + H - 40, 120, 28, "기본값으로");
    }
}
