using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 도구 > 진행 상태 보기 — 지금 챕터의 장소가 어떤 깃발 조건으로 열리는지, 그 깃발이 지금 몇인지 한눈에 본다(사용자 요청).
/// </summary>
/// <remarks>
/// 장소 조건은 (깃발, 값, 연산자) 세 워드이고 연산자는 0 <c>==</c> · 1 <c>!=</c> · 2 <c>&lt;</c> · 3 <c>&lt;=</c> · 4 <c>&gt;</c> · 5 <c>&gt;=</c>
/// (<see cref="FlagAllows"/>). 깃발 칸 옆 [−]·[+] 로 값을 바꿀 수 있다 — 스크립트 버그로 진행이 막혔을 때 스스로 푸는 용도(원본에 없음).
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private bool _progressOpen;
    private int _progressScroll;

    /// <summary>이번 틀에 그린 [−]·[+] 단추 — (네모, 깃발, 더할 값).</summary>
    private readonly List<(int X, int Y, int W, int H, int Flag, int Delta)> _progressButtons = [];

    private const int ProgressRowH = 16;

    private static string OpText(int op) => op switch { 0 => "==", 1 => "!=", 2 => "<", 3 => "<=", 4 => ">", _ => ">=" };

    /// <summary>그 챕터가 보거나 바꾸는 깃발 — 장소 조건, 챕터 사건 조건(101), 챕터 사건이 세우는 깃발(102·103).</summary>
    private static SortedSet<int> ChapterFlags(ChapterFile chp)
    {
        var set = new SortedSet<int>();
        foreach (var place in chp.Places)
            foreach (var c in place.Conditions)
                if (c.Variable > 0) set.Add(c.Variable);
        foreach (var e in chp.Events)
        {
            foreach (var c in e.Conditions) if (c.Code == 101 && c.Args.Length > 0 && c.Args[0] > 0) set.Add(c.Args[0]);
            foreach (var a in e.Actions) if (a.Code is 102 or 103 && a.Args.Length > 0 && a.Args[0] > 0) set.Add(a.Args[0]);
        }
        set.RemoveWhere(f => f >= 2000);
        return set;
    }

    private void DrawProgress()
    {
        _progressButtons.Clear();
        if (!_progressOpen) return;
        int w = Math.Min(760, ViewWidth - 40), h = ViewHeight - 40;
        int x0 = _camX + (ViewWidth - w) / 2, y0 = _camY + 20;
        FillRect(x0, y0, w, h, 0xF0101428);
        StrokeRect(x0, y0, w, h, 0xFF6070A0);

        var chp = _mosesChp;
        string title = chp is null ? "진행 상태 — 챕터 밖" : $"진행 상태 — Chp {chp.Id:D4} {_db?.T((ushort)chp.TitleText)}";
        DrawText(title, x0 + 12, y0 + 8, 0xFFFFE070, 13);
        DrawText("휠: 굴리기 · [−][+]: 깃발 바꾸기 · Esc: 닫기", x0 + w - 300, y0 + 10, 0xFF9098B0, 11);
        if (chp is null) return;

        // 줄 목록을 만든 뒤 굴린 만큼 잘라 그린다.
        var rows = new List<Action<int>>();
        rows.Add(y => DrawText("장소 (행성별)", x0 + 12, y, 0xFF9FC0FF, 12));
        // 행성마다 머리 줄(항성계 › 행성, 행성·항성계 조건) 뒤에 그 행성의 장소를 늘어놓는다 — 행성이 잠기면 그 장소도 항행에 안 나온다.
        var grouped = chp.Planets.Select(pl => (Planet: pl, Places: chp.Places.Where(pp => pl.Places.Contains(pp.No)).ToList()))
                                 .Where(g => g.Places.Count > 0).ToList();
        var loose = chp.Places.Where(pp => !chp.Planets.Any(pl => pl.Places.Contains(pp.No))).ToList();
        foreach (var (planet, places) in grouped)
        {
            var pl = planet;
            var system = chp.Systems.FirstOrDefault(sy => sy.Planets.Contains(pl.No));
            rows.Add(y =>
            {
                bool open = FlagsAllow(pl.Conditions) && (system is null || FlagsAllow(system.Conditions));
                var conds = pl.Conditions.Concat(system?.Conditions ?? []).Where(c => c.Variable > 0 && c.Variable < _flags.Length)
                    .Select(c => $"깃발 {c.Variable} {OpText(c.Operator)} {c.Value} (지금 {_flags[c.Variable]})").ToList();
                string where = $"{(system is null ? "" : _db?.T((ushort)system.NameText) + " › ")}{_db?.T((ushort)pl.NameText)}";
                DrawText("■ " + where, x0 + 16, y, open ? 0xFFB0C8E0 : 0xFFE07070, 12);
                if (!open || conds.Count > 0)
                    DrawText((open ? "" : "행성 잠김 · ") + string.Join(", ", conds), x0 + 330, y, open ? 0xFF9098B0 : 0xFFE07070, 12);
            });
            foreach (var place in places) rows.Add(PlaceRow(place));
        }
        if (loose.Count > 0)
        {
            rows.Add(y => DrawText("■ 행성 밖(자동 발생 따위)", x0 + 16, y, 0xFFB0C8E0, 12));
            foreach (var place in loose) rows.Add(PlaceRow(place));
        }

        Action<int> PlaceRow(ChapterFile.Place place)
        {
            var p = place;
            return y =>
            {
                bool used = _placesUsed.Contains((chp.Id, p.No)) || _autoPlacesDone.Contains((chp.Id, p.No));
                bool open = PlaceOpen(p);
                string kind = p.Kind switch
                {
                    ChapterFile.PlaceKind.Shop => "상점",
                    ChapterFile.PlaceKind.Field => $"필드 {p.Value - 10000:D4}",
                    _ => $"전투 {p.Value:D4}",
                };
                var (state, color) = (used ? "다녀옴" : open ? "열림" : "잠김", used ? 0xFF8088A0u : open ? 0xFF70E070u : 0xFFE07070u);
                var conds = p.Conditions.Where(c => c.Variable > 0 && c.Variable < _flags.Length)
                    .Select(c => $"깃발 {c.Variable} {OpText(c.Operator)} {c.Value} (지금 {_flags[c.Variable]})").ToList();
                string name = _db?.T((ushort)p.NameText) is { Length: > 0 } n ? n : $"장소 {p.No}";
                DrawText($"{p.No,3}", x0 + 24, y, 0xFF9098B0, 12);
                DrawText(name, x0 + 56, y, White, 12);
                DrawText(kind + (p.IsAuto ? " · 자동" : ""), x0 + 200, y, 0xFFB0B8C8, 12);
                DrawText(state, x0 + 300, y, color, 12);
                DrawText(conds.Count == 0 ? "조건 없음" : string.Join(", ", conds), x0 + 350, y, color, 12);
            };
        }
        rows.Add(_ => { });
        rows.Add(y => DrawText("깃발 (이 챕터가 보거나 바꾸는 것)", x0 + 12, y, 0xFF9FC0FF, 12));
        var flags = ChapterFlags(chp).ToList();
        int perRow = Math.Max(2, (w - 24) / 170);             // 한 칸 = 「깃발 NNN = NNN」 + [−][+]
        int colW = (w - 24) / perRow;
        for (int i = 0; i < flags.Count; i += perRow)
        {
            var chunk = flags.Skip(i).Take(perRow).ToList();
            rows.Add(y =>
            {
                for (int k = 0; k < chunk.Count; k++)
                {
                    int f = chunk[k], cx = x0 + 16 + k * colW;
                    DrawText($"깃발 {f} = {_flags[f]}", cx, y, _flags[f] != 0 ? White : 0xFF9098B0, 12);
                    int bx = cx + 100;
                    DrawText("[−]", bx, y, 0xFFFFC080, 12);
                    DrawText("[+]", bx + 26, y, 0xFFFFC080, 12);
                    _progressButtons.Add((bx, y, 22, ProgressRowH, f, -1));
                    _progressButtons.Add((bx + 26, y, 22, ProgressRowH, f, +1));
                }
            });
        }

        int visible = (h - 40) / ProgressRowH;
        _progressScroll = Math.Clamp(_progressScroll, 0, Math.Max(0, rows.Count - visible));
        for (int i = 0; i < visible && i + _progressScroll < rows.Count; i++)
            rows[i + _progressScroll](y0 + 32 + i * ProgressRowH);
    }

    /// <summary>진행 상태 창이 떠 있으면 클릭을 먹는다 — [−]·[+] 는 그 깃발을 바꾼다.</summary>
    private bool OnProgressClick(int bx, int by)
    {
        if (!_progressOpen) return false;
        foreach (var (x, y, w, h, flag, delta) in _progressButtons)
            if (bx >= x && bx < x + w && by >= y && by < y + h)
            {
                _flags[flag] = (byte)Math.Clamp(_flags[flag] + delta, 0, 255);
                Toast($"깃발 {flag} = {_flags[flag]}");
                return true;
            }
        return true;
    }

    private void ToggleProgress()
    {
        _progressOpen = !_progressOpen;
        _progressScroll = 0;
    }
}
