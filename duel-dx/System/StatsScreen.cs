using DuelDx.Native;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 도구 &gt; 전투 통계 — 어느 어빌리티를 가장 많이 썼고 어느 것이 피해를 가장 많이 넣었는지 본다(사용자 요청 menu-14, 원본에 없다).
/// </summary>
/// <remarks>
/// 셈은 <see cref="BattleStats"/> 가 한다. 위 탭으로 「이번 전투」와 「누적」을, 표 머리(횟수 · 피해 · 처치 · 회복)를 눌러 줄 세우는 기준을 바꾼다.
/// 한 줄은 어빌리티 하나다 — 인물과 레벨은 합친다.
/// </remarks>
internal sealed unsafe class StatsScreen(GameWindow host)
{
    internal const int MenuStats = 1163;
    internal const int W = 600, H = 440, RowH = 20, ListY = 96, Rows = (H - ListY - 50) / RowH;

    /// <summary>DUELDX_STATS 가 있으면 열어 둔 채 시작한다(화면 밖 시험용) — 값이 total 이면 누적 탭.</summary>
    internal bool _open = Environment.GetEnvironmentVariable("DUELDX_STATS") != null;
    internal bool _total = Environment.GetEnvironmentVariable("DUELDX_STATS") == "total";
    internal int _sort;             // 0 횟수 · 1 피해 · 2 처치 · 3 회복
    internal int _top;

    internal static readonly (string Label, int X)[] Columns = [("횟수", 300), ("명중", 360), ("피해", 420), ("처치", 490), ("회복", 540)];
    /// <summary>표 머리 칸 → 줄 세우는 기준(명중은 기준이 아니다).</summary>
    internal static readonly int[] SortOfColumn = [0, -1, 1, 2, 3];

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    internal string NameOf(int ability, int work) =>
        ability > 0 ? host._db != null && host._db.Abilities.TryGetValue(ability, out var ab) && host._db.T(ab.NameId) is { Length: > 0 } name ? name : $"어빌리티 {ability}"
        : work == 0 ? "일반 공격" : $"아이템·기타 (work {work})";

    /// <summary>보이는 줄 — 어빌리티마다 합쳐서 고른 기준의 큰 것부터.</summary>
    internal List<(string Name, BattleStats.Row Sum)> Lines()
    {
        var source = _total ? host.Stats.Total().Skills : host.Stats._battle;
        var lines = source.GroupBy(p => (p.Key.Ability, p.Key.Work)).Select(g =>
        {
            var sum = new BattleStats.Row();
            foreach (var p in g) sum.Add(p.Value);
            return (Name: NameOf(g.Key.Ability, g.Key.Work), Sum: sum);
        });
        return [.. lines.OrderByDescending(l => _sort switch { 1 => l.Sum.Damage, 2 => l.Sum.Kills, 3 => l.Sum.Heal, _ => l.Sum.Uses }).ThenByDescending(l => l.Sum.Damage)];
    }

    internal bool In(int bx, int by, int x, int y, int w, int h) => bx >= x && bx < x + w && by >= y && by < y + h;

    /// <summary>열려 있으면 클릭을 먹는다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        var (x, y) = Origin();
        if (In(bx, by, x + W - 116, y + H - 40, 100, 28)) { _open = false; return true; }
        for (int tab = 0; tab < 2; tab++)
            if (In(bx, by, x + 16 + tab * 104, y + 38, 100, 24)) { (_total, _top) = (tab == 1, 0); return true; }
        for (int i = 0; i < Columns.Length; i++)
            if (SortOfColumn[i] >= 0 && In(bx, by, x + Columns[i].X - 8, y + 72, 56, 20)) { (_sort, _top) = (SortOfColumn[i], 0); return true; }
        return true;
    }

    internal void OnWheel(int delta) => _top = Math.Clamp(_top - delta, 0, Math.Max(0, Lines().Count - Rows));

    internal void OnKey(int key)
    {
        if (key == Win32.VK_ESCAPE) _open = false;
    }

    internal void Draw()
    {
        if (!_open) return;
        var (x, y) = Origin();
        host.FillRect(x - 4, y - 4, W + 8, H + 8, 0x80000000);
        host.FillRect(x, y, W, H, StatusScreen.PanelBg);
        host.StrokeRect(x, y, W, H, StatusScreen.BoxLine);
        host.FillRect(x, y, W, 28, StatusScreen.HeadBg);
        host.DrawText("전투 통계 — 내 인물이 쓴 어빌리티", x + 10, y + 6, White);

        string[] tabs = ["이번 전투", "누적"];
        for (int tab = 0; tab < 2; tab++)
        {
            bool on = (tab == 1) == _total;
            host.FillRect(x + 16 + tab * 104, y + 38, 100, 24, on ? 0xFF2A4A8A : StatusScreen.BoxBg);
            host.StrokeRect(x + 16 + tab * 104, y + 38, 100, 24, StatusScreen.BoxLine);
            host.DrawText(tabs[tab], x + 16 + tab * 104 + (100 - host.GetText(tabs[tab], White, 12).W) / 2, y + 42, White, 12);
        }
        if (_total)
        {
            var (battles, wins, _) = host.Stats.Total();
            host.DrawText($"끝낸 전투 {battles}판 · 승리 {wins}판", x + 236, y + 43, DimGray, 12);
        }

        host.DrawText("어빌리티", x + 20, y + 74, DimGray, 12);
        for (int i = 0; i < Columns.Length; i++)
            host.DrawText(Columns[i].Label + (SortOfColumn[i] == _sort ? " ▼" : ""), x + Columns[i].X, y + 74, SortOfColumn[i] == _sort ? 0xFFFFE070 : DimGray, 12);
        host.FillRect(x + 12, y + ListY - 4, W - 24, 1, StatusScreen.BoxLine);

        var lines = Lines();
        _top = Math.Clamp(_top, 0, Math.Max(0, lines.Count - Rows));
        if (lines.Count == 0)
            host.DrawText(_total ? "아직 끝낸 전투가 없습니다." : "이번 전투에서 아직 쓴 것이 없습니다.", x + 20, y + ListY + 4, DimGray, 12);
        for (int i = 0; i < Rows && _top + i < lines.Count; i++)
        {
            var (name, sum) = lines[_top + i];
            int ry = y + ListY + i * RowH;
            host.DrawText($"{_top + i + 1}", x + 20, ry, DimGray, 12);
            host.DrawText(name, x + 46, ry, White, 12);
            string[] cells = [$"{sum.Uses}", $"{sum.Hits}", $"{sum.Damage}", $"{sum.Kills}", $"{sum.Heal}"];
            for (int c = 0; c < cells.Length; c++) host.DrawText(cells[c], x + Columns[c].X, ry, SortOfColumn[c] == _sort ? 0xFFFFE070 : White, 12);
        }

        int fy = y + H - 40;
        host.FillRect(x + W - 116, fy, 100, 28, StatusScreen.HeadBg);
        host.DrawText("닫기", x + W - 80, fy + 6, White);
        host.DrawText("Esc: 닫기 · 표 머리: 줄 세우기 · 휠: 목록", x + 16, fy + 7, DimGray);
    }
}
