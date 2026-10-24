using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 연대표 — 원본 화면(배경 <c>BGRs\0096</c>: 제목 · 세 줄기 머리글 「Shivan Scimitar · Crimson Crusade · Apocalypse」이 그림에 있다)대로
/// 줄기마다 한 세로 줄에 칸을 때 차례로 늘어놓고, 칸을 누르면 그 칸이 여는 챕터로 간다(사용자 화면 · 분석-파트1-챕터 3절).
/// </summary>
/// <remarks>
/// 칸 이름은 그림이다 — 낱말 1 의 Obs(540~ : 「카디스 요새 / A maiden fortress, Cardis」). 세로 자리 = 때((줄 − 1) × 240 + 가로 자리, 20 마다 48px),
/// 왼쪽에 「1281. 00 · 20 · 40 …」 눈금. 때가 같은 칸은 아래로 비켜 놓는다. 휠 · 위아래 키 · 오른쪽 화살표로 굴린다.
/// 칸은 낱말 0 의 진행 깃발이 서면 열린다(<see cref="Unlocked"/>) — 닫힌 칸은 어둡게 보이고 안 눌린다.
/// </remarks>
internal sealed class Part1TimelineScreen(GameWindow host, Part1TitleScreen shell)
{
    /// <summary>줄기 이름 — 줄기 0 살라딘 쪽 · 1 바이올라 쪽 · 2 인페르노 쪽(첫 칸 이름으로 붙인 것).</summary>
    internal static readonly string[] LineNames = ["살라딘", "바이올라", "인페르노"];

    /// <summary>줄기 가운데(배경 머리글 자리) · 칸 크기 · 보이는 띠(위 · 아래).</summary>
    internal static readonly int[] ColumnX = [155, 320, 505];

    internal const int CellW = 170, CellH = 34, BandTop = 112, BandBottom = 470, Background = 96, Step = 48;

    internal static readonly (int X, int Y, string Name)[] Back = [(10, 452, "타이틀")];

    internal const int ButtonHit = 1000, ScrollUpHit = 1001, ScrollDownHit = 1002, MenuHit = 5000;

    /// <summary>Esc 메뉴 — 저장 · 불러오기(파트1 칸 창, 장면 갈래 7 「연대표」) · 타이틀 · 돌아가기(사용자 요청: 연대표에서도 저장).</summary>
    internal static readonly string[] MenuItems = ["저장", "불러오기", "파트1 타이틀", "돌아가기"];

    internal const int MenuX = 250, MenuY = 170, MenuW = 140, MenuRowH = 28;

    /// <summary>Esc 메뉴가 떠 있나.</summary>
    internal bool _menu;

    internal bool _open;
    internal IReadOnlyList<Part1Episode> _episodes = [];

    /// <summary>칸 가운데 — x 와 띠 맨 위에서의 y.</summary>
    internal (int X, int Y)[] _cells = [];
    internal int _hover = -1;
    private int _scroll, _contentH;
    private uint[]? _bg;

    /// <summary>칸의 때 — 줄(1~3)마다 240, 가로 자리를 더한다.</summary>
    private static int TimeOf(Part1Episode e) => (Math.Max(1, e.Row) - 1) * 240 + e.X;

    internal void Open()
    {
        GameWindow.TestRunTrace("part1 timeline open");   // 시험 전용 줄
        _episodes = Part1Episode.LoadAll();
        var cells = new (int, int)[_episodes.Count];
        var taken = new HashSet<(int, int)>();
        for (int i = 0; i < _episodes.Count; i++)
        {
            var e = _episodes[i];
            int col = Math.Clamp(e.Line, 0, ColumnX.Length - 1), y = 30 + TimeOf(e) * Step / 20;
            while (!taken.Add((col, y))) y += CellH + 4;   // 때가 같은 칸(사략해적 · 호위선단)은 아래로
            cells[i] = (ColumnX[col], y);
        }
        _cells = cells;
        _contentH = cells.Length > 0 ? cells.Max(c => c.Item2) + CellH : 0;
        _bg ??= shell.Field.ReadImage(Background).Px;
        _scroll = 0;
        _menu = false;
        _open = true;
        _hover = -1;
    }

    private int MaxScroll => Math.Max(0, _contentH - (BandBottom - BandTop));

    internal void Scroll(int notches) => _scroll = Math.Clamp(_scroll + notches * Step, 0, MaxScroll);

    internal int HitAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        if (_menu)
        {
            for (int i = 0; i < MenuItems.Length; i++)
                if (bx >= ox + MenuX && bx < ox + MenuX + MenuW && by >= oy + MenuY + 30 + i * MenuRowH && by < oy + MenuY + 30 + (i + 1) * MenuRowH - 2) return MenuHit + i;
            return -1;
        }
        var (backX, backY, _) = Back[0];
        if (bx >= ox + backX && bx < ox + backX + Part1TitleScreen.ButtonW && by >= oy + backY && by < oy + backY + Part1TitleScreen.ButtonH) return ButtonHit;
        // 오른쪽 띠의 위 · 아래 화살표(배경 그림의 장식).
        if (bx >= ox + 596 && bx < ox + 620 && by >= oy + 140 && by < oy + 190) return ScrollUpHit;
        if (bx >= ox + 596 && bx < ox + 620 && by >= oy + 380 && by < oy + 430) return ScrollDownHit;
        if (by < oy + BandTop || by >= oy + BandBottom) return -1;
        for (int i = 0; i < _cells.Length; i++)
        {
            var (x, y) = _cells[i];
            int top = oy + BandTop + y - _scroll - CellH / 2;
            if (bx >= ox + x - CellW / 2 && bx < ox + x + CellW / 2 && by >= top && by < top + CellH) return i;
        }
        return -1;
    }

    internal void UpdateHover(int bx, int by) => _hover = HitAt(bx, by);

    internal void OnClick(int bx, int by)
    {
        int hit = HitAt(bx, by);
        if (_menu && hit < 0) { _menu = false; return; }   // 메뉴 밖을 누르면 닫는다
        Press(hit);
    }

    internal void OnKey(int key)
    {
        if (key == Win32.VK_ESCAPE) { _menu = !_menu; _hover = -1; }   // Esc — 저장 · 불러오기 메뉴(다시 누르면 닫는다)
        else if (_menu) { if (key is Win32.VK_RETURN or Win32.VK_SPACE && _hover >= MenuHit) Press(_hover); }
        else if (key == Win32.VK_UP) Scroll(-1);
        else if (key == Win32.VK_DOWN) Scroll(1);
        else if (key is Win32.VK_RETURN or Win32.VK_SPACE && _hover >= 0) Press(_hover);
    }

    /// <summary>
    /// 그 칸이 열렸나 — 낱말 0 이 여는 진행 깃발이다(챕터 0 끝의 802 [4, 1] → 다음 칸 「무타나비의 성녀」 낱말 0 = 4, 자료로 본 가설).
    /// -1 이면 처음부터 열려 있다(줄기 첫 칸). 시험용 DUELDX_P1OPEN=1 이면 다 연다.
    /// </summary>
    private bool Unlocked(Part1Episode e) =>
        e.Words[0] < 0 || Environment.GetEnvironmentVariable("DUELDX_P1OPEN") == "1"
        || e.Words[0] < host.FlagSt._flags.Length && host.FlagSt._flags[e.Words[0]] != 0;

    private void Press(int hit)
    {
        if (hit >= MenuHit)
        {
            _menu = false;
            switch (hit - MenuHit)
            {
                case 0: host.SlotsScr.OpenSlots(0); break;                       // 저장 — SystemMenu.SaveBattleTo 가 연대표(장면 갈래 7)로 적는다
                case 1: host.SlotsScr.OpenSlots(1); break;
                case 2: host.LeaveScene(() => _open = false); break;
            }
            return;
        }
        if (hit == ButtonHit) host.LeaveScene(() => _open = false);
        else if (hit == ScrollUpHit) Scroll(-2);
        else if (hit == ScrollDownHit) Scroll(2);
        else if (hit >= 0 && hit < _episodes.Count)
        {
            if (!Unlocked(_episodes[hit])) { host.Toast($"{_episodes[hit].Name} — 아직 열리지 않았습니다"); return; }
            int chapter = _episodes[hit].ChapterId;
            host.LeaveScene(() => { if (shell.Chapter.Open(chapter)) _open = false; });
        }
    }

    internal void Draw()
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        if (_bg is { } bg)
            for (int y = 0; y < MosesScene.MosesH; y++)
                for (int x = 0; x < MosesScene.MosesW; x++)
                    host.SetPixel(ox + x, oy + y, bg[y * MosesScene.MosesW + x] | 0xFF000000);
        else host.FillRect(ox, oy, MosesScene.MosesW, MosesScene.MosesH, 0xFF141A24);

        // 눈금 — 20 마다, 240(한 해)마다 「1281. 00」 꼴.
        for (int t = 0; 30 + t * Step / 20 <= _contentH; t += 20)
        {
            int y = oy + BandTop + 30 + t * Step / 20 - _scroll;
            if (y < oy + BandTop - 6 || y > oy + BandBottom - 10) continue;
            string label = t % 240 == 0 ? $"{1281 + t / 240}. 00" : $"{t % 240}";
            host.UiText(label, ox + 8, y - 7, 0xFFB09880, 10);
            if (t % 240 == 0) host.UiFill(ox + 56, y, 115, 1, 0xC0D0C0B0);
        }

        int tick = (int)(host._lastTime * GameWindow.TicksPerSecond);
        for (int i = 0; i < _cells.Length && i < _episodes.Count; i++)
        {
            var (x, cy) = _cells[i];
            int y = oy + BandTop + cy - _scroll;
            if (y < oy + BandTop || y > oy + BandBottom - 8) continue;
            var e = _episodes[i];
            bool open = Unlocked(e), done = host.Part1.FinishedChapters.Contains(e.ChapterId);
            if (_hover == i) host.UiFill(ox + x - CellW / 2, y - CellH / 2, CellW, CellH, 0x60FFE0A0);
            // 이름 그림(Obs 낱말 1) — 그림 가운데를 칸 가운데에. 못 읽으면 글로.
            if (e.Words.Length > 1 && host.UiFor(Part1Game.ObsBase + e.Words[1])?.FrameAt(0, tick) is { } f)
                host.BlitScaled(f, ox + x - f.W / 2, y - f.H / 2, f.W, f.H);
            else
            {
                var (_, nw, nh) = host.UiGetText(e.Name, GameWindow.White, 12);
                host.UiText(e.Name, ox + x - nw / 2, y - nh / 2, GameWindow.White, 12);
            }
            if (!open) host.UiFill(ox + x - CellW / 2, y - CellH / 2, CellW, CellH, 0xA0000000);   // 닫힌 칸은 어둡게
            if (done) host.UiText("✓", ox + x - CellW / 2 - 4, y - 8, 0xFFFFE080, 12);
            if (e.ChapterId == host.Part1.ChapterId) host.UiStroke(ox + x - CellW / 2, y - CellH / 2, CellW, CellH, 0xFFFFE080);
        }
        var (bx, by, bn) = Back[0];
        shell.DrawButton(ox + bx, oy + by, Part1TitleScreen.ButtonW, Part1TitleScreen.ButtonH, bn, _hover == ButtonHit);
        if (_menu)
        {
            int h = 36 + MenuItems.Length * MenuRowH;
            host.UiFill(ox + MenuX - 10, oy + MenuY, MenuW + 20, h, 0xF0182040);
            host.UiStroke(ox + MenuX - 10, oy + MenuY, MenuW + 20, h, 0xFFE8ECF8);
            host.UiText("메뉴", ox + MenuX, oy + MenuY + 8, 0xFFFFE080, 13);
            for (int i = 0; i < MenuItems.Length; i++)
            {
                int y = oy + MenuY + 30 + i * MenuRowH;
                if (_hover == MenuHit + i) host.UiFill(ox + MenuX, y, MenuW, MenuRowH - 2, 0xFF2840C8);
                host.UiText(MenuItems[i], ox + MenuX + 10, y + 5, GameWindow.White, 13);
            }
        }
        host.DrawToast();
    }
}
