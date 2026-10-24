using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 챕터 지도의 교환 창(「교  환」 단추, 원본 <c>0x100b6860</c> · 내용 <c>0x100b6c00</c>) — 왼쪽 · 오른쪽 무리 사이로 인물(· 아이템 · 용병 · 탑승물)을 ▶ ◀ 로 옮긴다.
/// </summary>
/// <remarks>
/// <para>옵시디안 「분석-파트1-챕터」 13.6. 틀 상자 (65, 65) 510×390. 갈래: 고른 팀이 본부(머리 낱말 9 장소)에 있으면 「본  부」 ↔ 팀,
/// 아니면 같은 장소의 팀끼리. 단계 0 인물 · 1 아이템 · 2 용병 · 3 탑승물 — 「다음단계」가 단계 3 에서 오른쪽을 다음 팀으로 넘긴다.</para>
/// <para>리메이크의 팀: 팀 0 은 다른 팀에 안 든 무리 사람(앞 10명), 팀 1 부터는 옮긴 사람만 적는다(<see cref="Part1Team.Members"/>).
/// 그래서 「본  부」 = 팀 1 부터에 안 든 사람 모두(<see cref="Part1Progress.PoolMembers"/>)이고, 본부에서는 오른쪽 끝에 빈 새 팀을 두어 팀을 만든다(원본 갈래 0 팀 편성 자리).
/// 아이템 · 용병 · 탑승물은 리메이크가 무리 하나로 들고 있어(팀마다 나누지 않는다) 보이기만 하고 옮기지 않는다.</para>
/// </remarks>
internal sealed class Part1ExchangeWindow(GameWindow host, Part1ChapterScreen map)
{
    internal const int X = 65, Y = 65, W = 510, H = 390;

    internal const int ListW = 175, ListH = 300, LeftX = 15, RightX = 320, ListY = 40;

    internal const int RightArrow = 0x1f7, LeftArrow = 0x1f6, PrevStep = 0x1f4, NextStep = 0x1f5, LeftCell = 2000, RightCell = 2100;

    internal static readonly string[] StepNames = ["인  물", "아이템", "용  병", "탑승물"];

    /// <summary>한쪽 — 팀 번호, 또는 <see cref="Pool"/>(본부) · <see cref="NewTeam"/>(본부에서 만들 빈 팀).</summary>
    internal const int Pool = -1, NewTeam = -2;

    internal bool _open;
    private int _step;
    private int _left;
    private List<int> _rights = [];
    private int _right;
    private int _place;
    private bool _hq;
    private int _hover = -1;
    private readonly HashSet<int> _selLeft = [], _selRight = [];

    private int RightSide => _right >= 0 && _right < _rights.Count ? _rights[_right] : NewTeam;

    internal void Open()
    {
        if (map._chapter is not { } chapter || map.SelectedTeam is not { } team) return;
        int t = map.SelectedTeamIndex;
        _place = team.Place;
        int hq = chapter.Header.Length > 9 ? chapter.Header[9] : -1;
        _hq = hq >= 0 && _place == hq;
        _left = _hq ? Pool : t;
        _rights = Candidates(t);
        if (_rights.Count == 0) { host.Toast("같은 장소에 교환할 다른 팀이 없습니다"); return; }
        _right = 0;
        _step = 0;
        _selLeft.Clear();
        _selRight.Clear();
        _hover = -1;
        _open = true;
    }

    /// <summary>오른쪽 차례 — 본부: 이 장소의 팀 1 부터(고른 팀 먼저) + 새 팀(8개 못 채웠으면), 그 밖: 같은 장소의 다른 팀.</summary>
    private List<int> Candidates(int selected)
    {
        var teams = map.Teams;
        var here = Enumerable.Range(0, teams.Count).Where(i => teams[i].Place == _place).ToList();
        if (!_hq) return [.. here.Where(i => i != selected)];
        var list = here.Where(i => i >= 1).OrderBy(i => i == selected ? -1 : i).ToList();
        if (teams.Count < 8) list.Add(NewTeam);
        return list;
    }

    /// <summary>
    /// 닫기(0x100ba120) — 빈 팀(1 부터)을 지우고 당긴다. 고른 팀은 지워진 만큼 당기고, 지워졌으면 팀 0.
    /// </summary>
    internal void Close()
    {
        _open = false;
        var teams = map.MutableTeams;
        int selected = map.SelectedTeamIndex;
        for (int i = teams.Count - 1; i >= 1; i--)
        {
            if (teams[i].Members.Count > 0) continue;
            teams.RemoveAt(i);
            if (selected == i) selected = 0;
            else if (selected > i) selected--;
        }
        map.SelectTeam(Math.Clamp(selected, 0, teams.Count - 1));
    }

    private string Label(int side) => side switch { Pool => "본  부", NewTeam => $"{map.Teams.Count + 1} 팀", _ => $"{side + 1} 팀" };

    private List<int> Members(int side) => side switch
    {
        Pool => host.Part1.PoolMembers(),
        NewTeam => [],
        _ => host.Part1.TeamMembers(side),
    };

    /// <summary>한쪽 목록의 칸 수(지금 단계).</summary>
    private int Count(int side) => _step switch
    {
        0 => Members(side).Count,
        1 => side == _left ? host.Part1.Bag.Distinct().Count() : 0,
        2 => side == _left ? host.Part1.Mercenaries.Count(m => m.Value > 0) : 0,
        _ => side == _left ? host.Part1.Mounts.Count(m => m.Value > 0) : 0,
    };

    /// <summary>단계별 격자 — 열 · 줄 · 칸 크기(13.6 표).</summary>
    private (int Cols, int Rows, int CellW, int CellH, int GapX, int GapY) Grid => _step switch
    {
        0 => (2, 5, 60, 50, 15, 6),
        1 => (4, 6, 26, 40, 14, 6),
        _ => (1, 3, 128, 88, 0, 8),
    };

    private (int X, int Y) Cell(int listX, int i)
    {
        var g = Grid;
        int gridW = g.Cols * g.CellW + (g.Cols - 1) * g.GapX;
        return (listX + (ListW - gridW) / 2 + i % g.Cols * (g.CellW + g.GapX), ListY + 12 + i / g.Cols * (g.CellH + g.GapY));
    }

    private bool CanPrev => _step > 0 || _right > 0;

    /// <summary>다음단계 — 단계 0 에서 오른쪽이 비었으면 못 간다. 단계 3 이면 오른쪽 다음 팀(없으면 못 간다).</summary>
    private bool CanNext => !(_step == 0 && Members(RightSide).Count == 0) && (_step < 3 || _right + 1 < _rights.Count);

    private int HitAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        int x = bx - ox - X, y = by - oy - Y;
        if (x >= 235 && x < 280 && y >= 150 && y < 195) return RightArrow;
        if (x >= 235 && x < 280 && y >= 200 && y < 245) return LeftArrow;
        if (Part1MapArt.ButtonHit(100, 367, x, y)) return PrevStep;
        if (Part1MapArt.ButtonHit(410, 367, x, y)) return NextStep;
        var g = Grid;
        foreach (var (side, listX, hit) in new[] { (_left, LeftX, LeftCell), (RightSide, RightX, RightCell) })
            for (int i = 0, n = Math.Min(Count(side), g.Cols * g.Rows); i < n; i++)
            {
                var (cx, cy) = Cell(listX, i);
                if (x >= cx && x < cx + g.CellW && y >= cy && y < cy + g.CellH) return hit + i;
            }
        if (x < 0 || y < 0 || x >= W || y >= H) return -2;
        return -1;
    }

    internal void UpdateHover(int bx, int by) => _hover = HitAt(bx, by);

    internal void OnClick(int bx, int by)
    {
        int hit = HitAt(bx, by);
        switch (hit)
        {
            case -2: Close(); break;   // 창 밖을 누르면 닫는다
            case RightArrow: Transfer(_selLeft, _left, RightSide); break;
            case LeftArrow: Transfer(_selRight, RightSide, _left); break;
            case PrevStep when CanPrev:
                if (_step > 0) _step--;
                else { _right--; _step = 3; }
                _selLeft.Clear(); _selRight.Clear();
                break;
            case NextStep when CanNext:
                if (_step < 3) _step++;
                else { _right++; _step = 0; }
                _selLeft.Clear(); _selRight.Clear();
                break;
            case >= RightCell and < RightCell + 100: Select(_selRight, _selLeft, hit - RightCell); break;
            case >= LeftCell and < LeftCell + 100: Select(_selLeft, _selRight, hit - LeftCell); break;
        }
    }

    /// <summary>칸 누름 — Ctrl 없이면 그 칸만(다른 쪽 고름도 푼다), Ctrl 이면 여럿.</summary>
    private static void Select(HashSet<int> mine, HashSet<int> other, int i)
    {
        other.Clear();
        if (MosesScene.CtrlHeld) { if (!mine.Remove(i)) mine.Add(i); }
        else { mine.Clear(); mine.Add(i); }
    }

    /// <summary>
    /// 옮기기(0x100b87d0) — 고른 인물을 다른 쪽으로. 받는 쪽이 팀이면 10명까지(본부는 한도 없음). 단계 1~3 은 리메이크가 무리 하나로 들고 있어 못 옮긴다.
    /// </summary>
    private void Transfer(HashSet<int> selected, int from, int to)
    {
        if (selected.Count == 0) return;
        if (_step > 0) { host.Toast("아이템 · 용병 · 탑승물은 아직 무리 공용입니다 — 팀마다 나누지 않습니다"); return; }
        var teams = map.MutableTeams;
        var members = Members(from);
        var moving = selected.Where(i => i < members.Count).Select(i => members[i]).ToList();
        selected.Clear();
        bool pooled(int side) => side is Pool or 0;
        if (pooled(from) && pooled(to)) return;   // 본부 ↔ 팀 0 은 같은 사람들이다
        if (to == NewTeam)
        {
            if (teams.Count >= 8) { host.Toast("팀은 8개까지입니다"); return; }
            teams.Add(new Part1Team { Place = _place });
            to = teams.Count - 1;
            _rights[_right] = to;
            if (teams.Count < 8) _rights.Add(NewTeam);
        }
        foreach (int chr in moving)
        {
            if (to >= 1 && teams[to].Members.Count >= Part1Progress.TeamLimit) { host.Toast("한 팀은 10명까지입니다"); break; }
            if (from >= 1) teams[from].Members.Remove(chr);
            if (to >= 1 && !teams[to].Members.Contains(chr)) teams[to].Members.Add(chr);
        }
        map.SaveProgress();
    }

    internal void Draw(int ox, int oy)
    {
        var art = map.Art;
        int wx = ox + X, wy = oy + Y;
        art.Box(wx, wy, W, H);
        art.Tag(wx + 70, wy + 10, Label(_left));
        art.Tag(wx + 385, wy + 10, Label(RightSide));
        art.TextCentered(StepNames[_step], wx + 257, wy + 22, 0xFFFFE080, 13);
        DrawList(art, wx + LeftX, wy, _left, _selLeft, LeftCell);
        DrawList(art, wx + RightX, wy, RightSide, _selRight, RightCell);

        // ▶(왼 → 오) · ◀(오 → 왼) — Obs 516: ▶ 모션 1 · 짚음 3, ◀ 모션 0 · 2.
        art.Blit(art.Motion(516, _hover == RightArrow ? 3 : 1), wx + 237, wy + 152);
        art.Blit(art.Motion(516, _hover == LeftArrow ? 2 : 0), wx + 237, wy + 202);

        art.Button(wx + 100, wy + 367, "이전단계", _hover == PrevStep, !CanPrev);
        art.Button(wx + 410, wy + 367, "다음단계", _hover == NextStep, !CanNext);
    }

    private void DrawList(Part1MapArt art, int lx, int wy, int side, HashSet<int> selected, int hitBase)
    {
        art.Panel(lx, wy + ListY, ListW, ListH);
        var g = Grid;
        int wx = lx - (side == _left ? LeftX : RightX);
        int tick = (int)(host._lastTime * GameWindow.TicksPerSecond);
        if (_step == 0)
        {
            var members = Members(side);
            for (int i = 0; i < g.Cols * g.Rows; i++)
            {
                var (cx, cy) = Cell(lx - wx, i);
                int sx = wx + cx, sy = wy + cy;
                if (i < members.Count && art.Face(members[i], sx + g.CellW / 2, sy + g.CellH / 2)) { }
                else if (side != Pool && i < Part1Progress.TeamLimit) art.Blit(art.Frame(217, 2, 0), sx + 5, sy);
                Mark(art, sx + 4, sy - 1, 52, 52, selected.Contains(i), _hover == hitBase + i && i < members.Count);
            }
            if (side == Pool && members.Count > g.Cols * g.Rows) art.TextCentered($"외 {members.Count - g.Cols * g.Rows}명", lx + ListW / 2, wy + ListY + ListH - 12, 0xFFA0A8C8, 11);
            return;
        }
        if (side != _left)
        {
            art.TextCentered("무리 공용", lx + ListW / 2, wy + ListY + ListH / 2 - 10, 0xFFA0A8C8, 12);
            art.TextCentered("(팀마다 나누지 않는다)", lx + ListW / 2, wy + ListY + ListH / 2 + 10, 0xFFA0A8C8, 11);
            return;
        }
        if (_step == 1)
        {
            var table = Part1Item.LoadAll();
            var items = host.Part1.Bag.GroupBy(id => id).Select(x => (Id: x.Key, Count: x.Count())).ToList();
            for (int i = 0; i < Math.Min(items.Count, g.Cols * g.Rows); i++)
            {
                var (cx, cy) = Cell(lx - wx, i);
                int sx = wx + cx, sy = wy + cy;
                if (Part1Item.ById(table, items[i].Id) is { } item && art.Motion(12, item.Icon) is { } icon) art.Blit(icon, sx - icon.X, sy - icon.Y);
                art.TextCentered($"[{items[i].Count}]", sx + 13, sy + 33, GameWindow.White, 10);
                Mark(art, sx - 1, sy - 1, 28, 28, selected.Contains(i), _hover == hitBase + i);
            }
            return;
        }
        var list = (_step == 2 ? host.Part1.Mercenaries : host.Part1.Mounts).Where(m => m.Value > 0).OrderBy(m => m.Key).ToList();
        for (int i = 0; i < Math.Min(list.Count, g.Rows); i++)
        {
            var (cx, cy) = Cell(lx - wx, i);
            int sx = wx + cx, sy = wy + cy;
            if (art.Chr(list[i].Key) is not { } chr) continue;
            if (chr.SpriteId > 0) art.Blit(art.Motion(chr.SpriteId, 2, tick), sx + 25, sy + 76);
            art.Text(chr.Name, sx + 55, sy + 12, 0xFFC8FF96, 12);
            art.TextRight($"{list[i].Value}", sx + 124, sy + 12, 0xFFFF96C8, 12);
            art.Text($"Hp {chr.MaxHp}", sx + 55, sy + 34, 0xFFE0E4F0, 11);
            Mark(art, sx, sy, g.CellW, g.CellH, selected.Contains(i), _hover == hitBase + i);
        }
    }

    /// <summary>고른 칸 · 짚은 칸 표시(원본 효과 9 · 0x14 의 모양은 모른다 — 노란 테).</summary>
    private static void Mark(Part1MapArt art, int x, int y, int w, int h, bool selected, bool hover)
    {
        if (selected) { art.Stroke(x, y, w, h, 0xFFFFE080); art.Stroke(x + 1, y + 1, w - 2, h - 2, 0xFFFFE080); }
        else if (hover) art.Stroke(x, y, w, h, 0xFFB0C8FF);
    }
}
