using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 연대표 — <c>Eps\episode.eps</c>(<see cref="Part1Episode"/>)의 이야기 줄기 셋을 줄마다 늘어놓고, 칸을 누르면 그 칸이 여는 챕터로 간다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-챕터」 3절. 원본 연대표는 칸의 가로 자리(u16 +2, 20 단위)대로 긴 띠에 그리지만, 한 화면에 다 들어가게 줄기마다 일곱 칸씩 접어 놓는다.
/// 칸을 여는 조건(앞 칸을 끝냈나)은 아직 모른다 — 다 열어 둔다.
/// </remarks>
internal sealed class Part1TimelineScreen(GameWindow host, Part1TitleScreen shell)
{
    /// <summary>줄기 이름 — 줄기 0 살라딘 쪽 · 1 바이올라 쪽 · 2 인페르노 쪽(첫 칸 이름으로 붙인 것).</summary>
    internal static readonly string[] LineNames = ["살라딘", "바이올라", "인페르노"];

    internal const int Columns = 7, CellW = 84, CellH = 22, Left = 26, Top = 56;

    internal static readonly (int X, int Y, string Name)[] Back = [(250, 446, "타이틀")];

    internal const int ButtonHit = 1000;

    internal bool _open;
    internal IReadOnlyList<Part1Episode> _episodes = [];
    internal (int X, int Y)[] _cells = [];
    internal (string Name, int Y)[] _headers = [];
    internal int _hover = -1;

    internal void Open()
    {
        _episodes = Part1Episode.LoadAll();
        var cells = new List<(int, int)>();
        var headers = new List<(string, int)>();
        int y = Top;
        foreach (var line in _episodes.GroupBy(e => e.Line).OrderBy(g => g.Key))
        {
            headers.Add((line.Key < LineNames.Length ? LineNames[line.Key] : $"줄기 {line.Key}", y));
            y += 18;
            int k = 0;
            foreach (var _ in line)
            {
                cells.Add((Left + k % Columns * (CellW + 2), y + k / Columns * (CellH + 3)));
                k++;
            }
            y += (k + Columns - 1) / Columns * (CellH + 3) + 8;
        }
        _cells = [.. cells];
        _headers = [.. headers];
        _open = true;
        _hover = -1;
    }

    internal int HitAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        var (backX, backY, _) = Back[0];
        if (bx >= ox + backX && bx < ox + backX + Part1TitleScreen.ButtonW && by >= oy + backY && by < oy + backY + Part1TitleScreen.ButtonH) return ButtonHit;
        for (int i = 0; i < _cells.Length; i++)
        {
            var (x, y) = _cells[i];
            if (bx >= ox + x && bx < ox + x + CellW && by >= oy + y && by < oy + y + CellH) return i;
        }
        return -1;
    }

    internal void UpdateHover(int bx, int by) => _hover = HitAt(bx, by);

    internal void OnClick(int bx, int by) => Press(HitAt(bx, by));

    internal void OnKey(int key)
    {
        if (key == Win32.VK_ESCAPE) Press(ButtonHit);
        else if (key is Win32.VK_RETURN or Win32.VK_SPACE && _hover >= 0) Press(_hover);
    }

    private void Press(int hit)
    {
        if (hit == ButtonHit) host.LeaveScene(() => _open = false);
        else if (hit >= 0 && hit < _episodes.Count)
        {
            int chapter = _episodes[hit].ChapterId;
            host.LeaveScene(() => { if (shell.Chapter.Open(chapter)) _open = false; });
        }
    }

    internal void Draw()
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        host.UiFill(ox, oy, MosesScene.MosesW, MosesScene.MosesH, 0xFF141A24);
        const string title = "연대표";
        var (_, tw, _) = host.UiGetText(title, GameWindow.White, 18);
        host.UiText(title, ox + (MosesScene.MosesW - tw) / 2, oy + 18, GameWindow.White, 18);
        foreach (var (name, y) in _headers) host.UiText(name, ox + Left, oy + y, 0xFFFFE080, 12);
        for (int i = 0; i < _cells.Length && i < _episodes.Count; i++)
        {
            var (x, y) = _cells[i];
            bool current = _episodes[i].ChapterId == host.Part1.ChapterId;
            host.UiFill(ox + x, oy + y, CellW, CellH, _hover == i ? 0xF0305080u : current ? 0xD0405828u : 0xC0202838u);
            host.UiStroke(ox + x, oy + y, CellW, CellH, _hover == i ? 0xFFB0D0FFu : 0xFF506080u);
            string name = _episodes[i].Name;
            // 칸보다 긴 이름은 끝을 줄여 「…」 — 이웃 칸을 덮었다.
            while (name.Length > 1 && host.UiGetText(name, GameWindow.White, 11).W > CellW - 4) name = name[..^2] + "…";
            var (_, nw, nh) = host.UiGetText(name, GameWindow.White, 11);
            host.UiText(name, ox + x + Math.Max(2, (CellW - nw) / 2), oy + y + (CellH - nh) / 2, GameWindow.White, 11);
        }
        var (bx, by, bn) = Back[0];
        shell.DrawButton(ox + bx, oy + by, Part1TitleScreen.ButtonW, Part1TitleScreen.ButtonH, bn, _hover == ButtonHit);
        host.DrawToast();
    }
}
