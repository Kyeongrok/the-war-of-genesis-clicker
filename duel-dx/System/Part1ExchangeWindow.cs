using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 챕터 지도의 교환 창(「교  환」 단추, 원본 <c>0x100b6860</c> · 내용 <c>0x100b6c00</c>) — 왼쪽 · 오른쪽 무리 사이로 인물(· 아이템 · 용병 · 탑승물)을 ▶ ◀ 로 옮긴다.
/// </summary>
/// <remarks>
/// <para>옵시디안 「분석-파트1-챕터」 13.6. 틀 상자 (65, 65) 510×390. 갈래: 고른 팀이 본부(머리 낱말 9 장소)에 있으면 「본  부」 ↔ 팀,
/// 아니면 같은 장소의 팀끼리. 단계 0 인물 · 1 아이템 · 2 용병 · 3 탑승물 — 「다음단계」가 단계 3 에서 오른쪽을 다음 팀으로 넘긴다.</para>
/// <para>리메이크의 팀: 팀 0 은 다른 팀에 안 든 무리 사람(앞 10명), 팀 1 부터는 옮긴 사람만 적는다(<see cref="Part1Team.Members"/>).
/// 그래서 「본  부」 = 팀 1 부터에 안 든 사람 모두(<see cref="Part1Progress.PoolMembers"/>)이고, 본부에서는 오른쪽 끝에 빈 새 팀을 두어 팀을 만든다(원본 갈래 0 팀 편성 자리).</para>
/// <para>아이템 · 용병 · 탑승물은 원본처럼 팀마다 따로 든다(<see cref="Part1Team.Stock"/>) — 「본  부」 쪽은 이야기 무리 짐(<see cref="Part1Progress.CurrentStock"/>, 원본 0x101a0828[머리 낱말 1]).
/// 칸 하나를 Ctrl 없이 고르고 ▶ ◀ 를 누르면 하나씩(0x100b97a0 · 0x100b9ac0 · 0x100b9df0), 여럿이거나 Ctrl 이면 고른 칸을 통째로(0x100b8b20 · 0x100b8f40 · 0x100b9370) 옮기고,
/// 받는 쪽이 팀이면 칸마다 99 까지(0x100ba3d0 · 본부는 한도 없음). 사람을 다 내보낸 팀은 짐도 받는 쪽으로 따라간다(0x100b87d0, 팀 해산).</para>
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
    private bool _formation;
    private int _step;
    private int _left;
    private List<int> _rights = [];
    private int _right;
    private int _place;
    private bool _hq;
    private int _hover = -1;
    private readonly HashSet<int> _selLeft = [], _selRight = [];

    private int RightSide => _right >= 0 && _right < _rights.Count ? _rights[_right] : NewTeam;

    /// <summary>
    /// 연다 — <paramref name="formation"/> 이면 팀 편성(원본 갈래 0: 팀이 하나도 없이 지도가 손에 들어올 때 0x100b3f10 이 연다) — 장소와 상관없이 본부 ↔ 팀.
    /// </summary>
    internal void Open(bool formation = false)
    {
        if (map._chapter is not { } chapter || map.SelectedTeam is not { } team) return;
        int t = map.SelectedTeamIndex;
        _place = team.Place;
        int hq = chapter.Header.Length > 9 ? chapter.Header[9] : -1;
        _formation = formation;
        _hq = formation || hq >= 0 && _place == hq;
        _left = _hq ? Pool : t;
        _rights = Candidates(t);
        if (_rights.Count == 0) { host.Toast("같은 장소에 교환할 다른 팀이 없습니다"); return; }
        // 본부에서는 팀 0 도 옮긴 사람만 드는 팀으로 — 안 그러면 본부와 1팀에 같은 사람이 보이고 옮겨도 그대로였다(fg-58). 비운 채 닫으면 되돌린다(Close).
        if (_hq && map.MutableTeams is { Count: > 0 } all && !all[0].Fixed) (all[0].Fixed, all[0].Members) = (true, []);
        _right = 0;
        _step = 0;
        _selLeft.Clear();
        _selRight.Clear();
        _hover = -1;
        _open = true;
    }

    /// <summary>
    /// 오른쪽 차례 — 본부: 이 장소의 팀(고른 팀 먼저, 팀 0 도 — 짐은 본부와 따로다) + 새 팀(8개 못 채웠으면), 그 밖: 같은 장소의 다른 팀.
    /// 팀 편성이면 장소와 상관없이 모든 팀.
    /// </summary>
    private List<int> Candidates(int selected)
    {
        var teams = map.Teams;
        var here = Enumerable.Range(0, teams.Count).Where(i => _formation || teams[i].Place == _place).ToList();
        if (!_hq) return [.. here.Where(i => i != selected)];
        var list = here.OrderBy(i => i == selected ? -1 : i).ToList();
        if (teams.Count < 8) list.Add(NewTeam);
        return list;
    }

    /// <summary>
    /// 닫기(0x100ba120) — 빈 팀(1 부터)을 지우고 당긴다. 고른 팀은 지워진 만큼 당기고, 지워졌으면 팀 0.
    /// </summary>
    internal void Close()
    {
        _open = false;
        if (_formation && map._chapter is { } chapter) host.Part1.Formed.Add(chapter.Id);
        _formation = false;
        var teams = map.MutableTeams;
        int selected = map.SelectedTeamIndex;
        if (teams.Count > 0 && teams[0].Fixed && teams[0].Members.Count == 0) teams[0].Fixed = false;   // 팀 0 을 비웠으면 「남은 사람」 팀으로 되돌린다
        for (int i = teams.Count - 1; i >= 1; i--)
        {
            if (teams[i].Members.Count > 0) continue;
            host.Part1.CurrentStock.TakeAll(teams[i].Stock);   // 사람 없이 짐만 남은 팀(행동이 적어 둔 칸) — 지우기 전에 본부로
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

    /// <summary>한쪽의 짐 — 본부는 이야기 무리 짐, 팀은 그 팀 짐, 새 팀은 없음.</summary>
    private Part1Progress.Stock? StockOf(int side) => side switch
    {
        Pool => host.Part1.CurrentStock,
        NewTeam => null,
        _ => side >= 0 && side < map.Teams.Count ? map.Teams[side].Stock : null,
    };

    /// <summary>한쪽 목록의 칸(단계 1 아이템 · 2 용병 · 3 탑승물) — (번호, 수). 아이템은 가방 차례, 용병 · 탑승물은 Chr 차례.</summary>
    private List<(int Id, int Count)> Entries(int side) => StockOf(side) is not { } stock ? [] : _step switch
    {
        1 => [.. stock.Bag.GroupBy(id => id).Select(x => (x.Key, x.Count()))],
        2 => [.. stock.Mercenaries.Where(m => m.Value > 0).OrderBy(m => m.Key).Select(m => (m.Key, m.Value))],
        3 => [.. stock.Mounts.Where(m => m.Value > 0).OrderBy(m => m.Key).Select(m => (m.Key, m.Value))],
        _ => [],
    };

    /// <summary>한쪽 목록의 칸 수(지금 단계).</summary>
    private int Count(int side) => _step == 0 ? Members(side).Count : Entries(side).Count;

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
    /// 옮기기(0x100b87d0) — 고른 인물을 다른 쪽으로. 받는 쪽이 팀이면 10명까지(본부는 한도 없음). 보낸 팀이 비면 그 짐도 받는 쪽으로 따라간다.
    /// 단계 1~3 은 <see cref="TransferStock"/>.
    /// </summary>
    private void Transfer(HashSet<int> selected, int from, int to)
    {
        if (selected.Count == 0) return;
        if (_step > 0) { TransferStock(selected, from, to); return; }
        var teams = map.MutableTeams;
        var members = Members(from);
        var moving = selected.Where(i => i < members.Count).Select(i => members[i]).ToList();
        selected.Clear();   // 사람은 옮기면 목록에서 빠져 칸이 당겨진다 — 고름을 남기면 다음 사람이 딸려 간다
        bool explicitTeam(int side) => side >= 1 || side == 0 && map.Teams.Count > 0 && map.Teams[0].Fixed;
        bool pooled(int side) => side == Pool || side == 0 && !explicitTeam(0);
        if (pooled(from) && pooled(to)) return;   // 본부 ↔ (남은 사람) 팀 0 은 같은 사람들이다
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
            if (explicitTeam(to) && teams[to].Members.Count >= Part1Progress.TeamLimit) { host.Toast("한 팀은 10명까지입니다"); break; }
            if (explicitTeam(from)) teams[from].Members.Remove(chr);
            if (explicitTeam(to) && !teams[to].Members.Contains(chr)) teams[to].Members.Add(chr);
        }
        // 팀 해산 — 사람을 다 내보낸 팀(1 부터)의 아이템 · 용병 · 탑승물은 받는 쪽으로(0x100b87d0).
        if (from >= 1 && teams[from].Members.Count == 0 && StockOf(to) is { } into) into.TakeAll(teams[from].Stock);
        if (map._chapter is { } chapter) host.Part1.Formed.Add(chapter.Id);
        map.SaveProgress();
    }

    /// <summary>
    /// 짐 옮기기 — 칸 하나를 Ctrl 없이 골랐으면 하나씩(0x100b97a0 · 0x100b9ac0 · 0x100b9df0), 여럿이거나 Ctrl 이면 고른 칸 통째로(0x100b8b20 …).
    /// 받는 쪽이 팀이면 칸마다 99 까지(0x100ba3d0 — 꽉 찼으면 못 옮김), 본부는 한도 없음.
    /// </summary>
    private void TransferStock(HashSet<int> selected, int from, int to)
    {
        if (StockOf(from) is not { } src || StockOf(to) is not { } dst) return;
        var entries = Entries(from);
        var picked = selected.Where(i => i < entries.Count).Select(i => entries[i]).ToList();
        bool one = picked.Count == 1 && !MosesScene.CtrlHeld;
        int cap = to == Pool ? int.MaxValue : 99;
        bool full = false;
        foreach (var (id, count) in picked)
        {
            int have = _step switch { 1 => dst.Bag.Count(b => b == id), 2 => dst.Mercenaries.GetValueOrDefault(id), _ => dst.Mounts.GetValueOrDefault(id) };
            int n = Math.Min(one ? 1 : count, cap - have);
            if (n <= 0) { full = true; continue; }
            if (_step == 1) for (int k = 0; k < n; k++) { src.Bag.Remove(id); dst.Bag.Add(id); }
            else
            {
                var (a, b) = _step == 2 ? (src.Mercenaries, dst.Mercenaries) : (src.Mounts, dst.Mounts);
                if (count - n > 0) a[id] = count - n; else a.Remove(id);
                b[id] = have + n;
            }
        }
        if (full) host.Toast("한 칸에 99 까지입니다");
        // 고름은 그대로 — 그 아이템이 다 빠져 칸이 사라졌으면 남은 칸 안으로만(사용자 요청 fg-58: 하나씩 여러 번 넘기기).
        var left = Entries(from);
        selected.RemoveWhere(i => i >= left.Count || !picked.Any(p => p.Id == left[i].Id));
        map.SaveProgress();
    }

    /// <summary>이번 틀에 짚은 아이템 칸의 설명(이름 · 설명 · 효과, <see cref="Part1ItemTip"/>).</summary>
    private string? _tip;

    internal void Draw(int ox, int oy)
    {
        _tip = null;
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
        if (_tip is { Length: > 0 } tip) host.StatusScr.DrawDescriptionTip(tip, host._mouse.X, host._mouse.Y, ox, oy, 640, 480);
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
        if (_step == 1)
        {
            var table = Part1Item.LoadAll();
            var items = Entries(side);
            for (int i = 0; i < Math.Min(items.Count, g.Cols * g.Rows); i++)
            {
                var (cx, cy) = Cell(lx - wx, i);
                int sx = wx + cx, sy = wy + cy;
                if (Part1Item.ById(table, items[i].Id) is { } item && art.Motion(12, item.Icon) is { } icon) art.Blit(icon, sx - icon.X, sy - icon.Y);
                art.TextCentered($"[{items[i].Count}]", sx + 13, sy + 33, GameWindow.White, 10);
                Mark(art, sx - 1, sy - 1, 28, 28, selected.Contains(i), _hover == hitBase + i);
                if (_hover == hitBase + i) _tip = Part1ItemTip.Text(Part1Item.ById(table, items[i].Id));
            }
            return;
        }
        var list = Entries(side);
        for (int i = 0; i < Math.Min(list.Count, g.Rows); i++)
        {
            var (cx, cy) = Cell(lx - wx, i);
            int sx = wx + cx, sy = wy + cy;
            if (art.Chr(list[i].Id) is not { } chr) continue;
            if (chr.SpriteId > 0) DrawRowSprite(art, chr.SpriteId, tick, sx, sy, g.CellH);
            // 글은 그림 칸 오른쪽(55~124) — 이름이 길면 글씨를 줄이고, 수는 이름과 겹치지 않게 셋째 줄에.
            float size = 12;
            while (size > 9 && art.TextW(chr.Name, size) > TextW) size--;
            art.Text(chr.Name, sx + 55, sy + 12, 0xFFC8FF96, size);
            art.Text($"Hp {chr.MaxHp}", sx + 55, sy + 34, 0xFFE0E4F0, 11);
            art.Text($"× {list[i].Count}", sx + 55, sy + 56, 0xFFFF96C8, 12);
            Mark(art, sx, sy, g.CellW, g.CellH, selected.Contains(i), _hover == hitBase + i);
        }
    }

    /// <summary>용병 · 탈것 줄의 글 칸 폭(55 ~ 124).</summary>
    private const int TextW = 69;

    /// <summary>
    /// 용병 · 탈것 줄의 그림 — 줄 왼쪽 칸(52 × 줄 높이) 안에. 큰 탈것(아지다하카 따위)은 모션 2 첫 장의 크기로 줄여(장마다 다시 재면 출렁인다)
    /// 칸 가운데 · 바닥에 맞추고, 도는 장이 그보다 커도 칸 밖으로는 안 그린다.
    /// </summary>
    private static void DrawRowSprite(Part1MapArt art, int sprite, int tick, int sx, int sy, int cellH)
    {
        const int BoxW = 52, Pad = 2;
        if (art.Motion(sprite, 2, 0) is not { W: > 0, H: > 0 } first) return;
        int boxH = cellH - 2 * Pad;
        double scale = Math.Min(1.0, Math.Min((BoxW - 2.0 * Pad) / first.W, (double)boxH / first.H));
        // 첫 장의 네모를 칸 가운데 · 바닥(줄 아래 Pad)에 둘 기준점. 줄지 않는 그림은 예전 자리(발 = sx + 25, sy + 76)를 넘지 않게 둔다.
        int ax = scale < 1 ? (int)Math.Round(sx + BoxW / 2.0 - scale * (first.X + first.W / 2.0)) : sx + 25;
        int ay = scale < 1 ? (int)Math.Round(sy + Pad + boxH - scale * (first.Y + first.H)) : Math.Min(sy + 76, sy + Pad + boxH - (first.Y + first.H));
        var clip = art.Clip;
        art.Clip = (sx, sy, BoxW, cellH);
        art.BlitScaled(art.Motion(sprite, 2, tick), ax, ay, scale);
        art.Clip = clip;
    }

    /// <summary>고른 칸 · 짚은 칸 표시(원본 효과 9 · 0x14 의 모양은 모른다 — 노란 테).</summary>
    private static void Mark(Part1MapArt art, int x, int y, int w, int h, bool selected, bool hover)
    {
        if (selected) { art.Stroke(x, y, w, h, 0xFFFFE080); art.Stroke(x + 1, y + 1, w - 2, h - 2, 0xFFFFE080); }
        else if (hover) art.Stroke(x, y, w, h, 0xFFB0C8FF);
    }
}
