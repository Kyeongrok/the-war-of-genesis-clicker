using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 배치 창의 「용병 배치」 — 길드에서 산 용병(<see cref="Part1Progress.Mercenaries"/>)을 출전한 인물 둘레 3×3 에 부하로 붙인다(분석-파트1-전투 「용병 부대」).
/// </summary>
/// <remarks>
/// 원본(alpha.dll): 배치 객체 인물 칸(0x6c B)마다 <b>용병 칸 9개</b>(<c>+0x30</c>, u16 용병 목록 차례, 처음 0xffff — <c>0x100933eb</c>) ·
/// 채운 수 <c>+0x2e</c>. 칸 단추 9개 가운데 <b>4(가운데)는 인물 자신</b>이라 건너뛴다(<c>0x10093d16</c> · <c>0x1009410c</c>) — 많아야 8.
/// 용병 목록(<c>+0x74</c>, 16 B: Chr · 남은 수)은 무리 용병 표(<c>+0xb7ac</c> 수, <c>+0xb7b0</c> 8 B 칸)에서 온다. 목록을 누르면(명령 0x3e8)
/// 고른 칸에 넣고(있던 것은 목록으로 돌려줌) 남은 수 −1, 고른 칸을 다음 빈 칸(4 건너뜀)으로 옮긴다(<c>0x10093ee0~0x1009418a</c>).
/// 찬 칸을 다시 누르면 비운다(<c>0x10093d5d~</c>). 출전 확정(<c>0x1002f2d1~0x1002f6fb</c>)이 칸 i(a = i%3, b = i/3)마다
/// 대장 방향(<c>+0x4e</c>)으로 돌린 자리에 새 유닛(<c>0x10042ee0</c>)을 세우고 <c>0x10036790(대장, 용병, 1)</c>로 잇는다 —
/// b = 0 줄이 대장 <b>뒤</b>, b = 2 줄이 앞. 막힌 칸 검사는 없다(리메이크는 설 수 없으면 가까운 빈 칸).
/// 리메이크는 용병을 편 3(파트2 AI, 사용자 결정)으로 두고 대장의 부하로 잇는다 — 제 차례가 없고 대장 차례에 따라 걷고 함께 친다.
/// 아무에게도 안 붙였으면 첫 출전 인물에게(8 이 넘으면 다음 인물에게) 차례로 붙인다.
/// </remarks>
internal sealed class Part1MercDeploy(GameWindow host)
{
    internal const int SlotCount = 9, Center = 4;

    /// <summary>대장 Chr → 칸 9개의 용병 Chr(0 = 빈 칸). RESTART 가 유닛을 새로 만들어도 이어지게 Chr 로 적는다.</summary>
    private readonly Dictionary<int, int[]> _slots = [];

    private int _sceneId = -1;

    /// <summary>창을 연 대장 — null 이면 닫힘.</summary>
    internal UnitState? Leader;

    /// <summary>고른 칸(0~8, 4 제외)과 패드가 짚은 용병 목록 줄.</summary>
    internal int Slot, ListFocus;

    internal bool Open => Leader != null;

    internal const int PanelW = BattleScene.DeployW, CellW = 48, CellH = 34, RowH = 22;

    /// <summary>판을 새로 세울 때 — 다른 전투면 고른 것을 지운다(RESTART 는 같은 전투라 남긴다).</summary>
    internal void Reset(int sceneId)
    {
        if (sceneId != _sceneId) _slots.Clear();
        _sceneId = sceneId;
        Leader = null;
    }

    /// <summary>칸 i 의 대장 기준 자리 — 원본 출전 확정의 방향 점프표(<c>0x1002f72c</c>: 0 위 · 1 왼 · 2 아래 · 3 오른).</summary>
    internal static (int Dx, int Dy) Offset(int i, Facing f)
    {
        int a = i % 3, b = i / 3;
        return f switch
        {
            Facing.Up => (1 - a, 1 - b),
            Facing.Left => (1 - b, a - 1),
            Facing.Down => (a - 1, b - 1),
            _ => (b - 1, 1 - a),
        };
    }

    private int[] SlotsOf(int chr) => _slots.TryGetValue(chr, out var s) ? s : _slots[chr] = new int[SlotCount];

    /// <summary>용병 목록 — 이 판에 세울 수 있는 용병(Chr, 모두, 남은 수). 남은 수 = 모두 − 누군가의 칸에 넣은 수.</summary>
    internal List<(int Chr, int Total, int Left)> Pool()
    {
        var used = _slots.Values.SelectMany(s => s).Where(c => c > 0).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        return [.. host.Btl._part1Mercs.GroupBy(u => u.ChrCode).OrderBy(g => g.Key)
                   .Select(g => (g.Key, g.Count(), g.Count() - used.GetValueOrDefault(g.Key)))];
    }

    internal bool HasMercs => host.Btl._part1Mercs.Count > 0;

    /// <summary>「용병 배치」 단추 — 그 인물의 용병 칸 창을 연다(같은 인물이면 닫는다).</summary>
    internal void Toggle(UnitState? who)
    {
        if (!HasMercs) { host.Toast("고용한 용병이 없습니다 — 마을 용병 길드에서 고용하세요"); return; }
        if (who == null) { host.Toast("용병을 거느릴 인물을 명단에서 고르세요"); return; }
        if (Leader == who) { Leader = null; return; }
        OpenFor(who);
    }

    internal void OpenFor(UnitState who)
    {
        Leader = who;
        var s = SlotsOf(who.ChrCode);
        Slot = Enumerable.Range(0, SlotCount).FirstOrDefault(i => i != Center && s[i] == 0, 0);
        ListFocus = Math.Clamp(ListFocus, 0, Math.Max(0, Pool().Count - 1));
    }

    /// <summary>목록의 용병을 고른 칸에 — 있던 용병은 목록으로 돌아가고, 고른 칸은 다음 빈 칸으로(원본 0x10093ee0~).</summary>
    internal void Assign(int chr)
    {
        if (Leader is not { } leader || Slot == Center) return;
        if (Pool().FirstOrDefault(p => p.Chr == chr) is not { Left: > 0 }) { host.Toast("남은 용병이 없습니다"); return; }
        var s = SlotsOf(leader.ChrCode);
        s[Slot] = chr;
        for (int step = 1; step < SlotCount; step++)
        {
            int next = (Slot + step) % SlotCount;
            if (next != Center && s[next] == 0) { Slot = next; break; }
        }
    }

    /// <summary>고른 칸을 비운다.</summary>
    internal void Clear()
    {
        if (Leader is { } leader && Slot != Center) SlotsOf(leader.ChrCode)[Slot] = 0;
    }

    /// <summary>칸을 누름 — 이미 고른 찬 칸이면 비우고(원본 0x10093d5d), 아니면 그 칸을 고른다.</summary>
    internal void PickSlot(int i)
    {
        if (i == Center || Leader is not { } leader) return;
        if (Slot == i && SlotsOf(leader.ChrCode)[i] != 0) { Clear(); return; }
        Slot = i;
    }

    /// <summary>패드 — 고른 칸을 앞뒤로(가운데 건너뜀).</summary>
    internal void SlotStep(int step)
    {
        do Slot = ((Slot + step) % SlotCount + SlotCount) % SlotCount; while (Slot == Center);
    }

    internal void ListStep(int step)
    {
        int n = Pool().Count;
        if (n > 0) ListFocus = ((ListFocus + step) % n + n) % n;
    }

    internal void AssignFocused()
    {
        var pool = Pool();
        if (pool.Count > 0) Assign(pool[Math.Clamp(ListFocus, 0, pool.Count - 1)].Chr);
    }

    /// <summary>
    /// 출전 확정 — 판에 선 대장마다 칸의 용병을 대장 둘레에 세우고 부하로 잇는다(<c>0x1002f2d1~</c>). 안 쓴 용병은 판 밖에 남긴다(부른 쪽이 지운다).
    /// 판에 선 대장 누구에게도 안 붙였으면 출전 인물 차례로 8 씩 붙인다.
    /// </summary>
    internal void Apply()
    {
        var btl = host.Btl;
        Leader = null;
        if (btl._part1Mercs.Count == 0) return;
        var units = host._units;
        var leaders = units.Where(u => u.Alive && u.OnField && u.PlayerControlled && u.LeaderIndex < 0 && !btl._part1Mercs.Contains(u)).ToList();
        // RESTART — 앞서 붙인 용병을 떼고 다시 붙인다.
        foreach (var m in btl._part1Mercs.Where(m => m.LeaderIndex >= 0)) { m.LeaderIndex = -1; m.FormationSlot = -1; }
        foreach (var m in btl._part1Mercs) m.OnField = false;
        if (leaders.Count == 0) return;

        var plan = new Dictionary<UnitState, int[]>();
        foreach (var l in leaders)
            if (_slots.TryGetValue(l.ChrCode, out var s) && s.Any(c => c > 0)) plan[l] = s;
        bool fallback = plan.Count == 0;
        if (fallback)
        {
            // 아무도 안 골랐다 — 첫 출전 인물부터 칸 0~8(4 제외)을 차례로 채운다.
            var queue = new Queue<int>(btl._part1Mercs.Select(m => m.ChrCode).OrderBy(c => c));
            foreach (var l in leaders)
            {
                if (queue.Count == 0) break;
                var s = new int[SlotCount];
                for (int i = 0; i < SlotCount && queue.Count > 0; i++) if (i != Center) s[i] = queue.Dequeue();
                plan[l] = s;
            }
        }

        var free = btl._part1Mercs.ToList();
        foreach (var (leader, s) in plan)
        {
            int li = Array.IndexOf(units, leader);
            int order = btl.FollowersOf(li).Count;
            for (int i = 0; i < SlotCount; i++)
            {
                if (i == Center || s[i] == 0 || free.FirstOrDefault(m => m.ChrCode == s[i]) is not { } merc) continue;
                free.Remove(merc);
                var (dx, dy) = Offset(i, leader.Facing);
                var (col, row) = Spot(leader.Col + dx, leader.Row + dy, leader, merc);
                merc.ResetTo(col, row);
                merc.StartCol = col;
                merc.StartRow = row;
                merc.Facing = merc.StartFacing = leader.Facing;
                merc.OnField = true;
                merc.Awake = true;
                merc.LeaderIndex = li;
                merc.FormationSlot = order++;
                leader.LegionId = BattleScene.Part1SquadLegion;
                host.StatusScr.RefreshUnitStats(merc);
            }
        }
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 merc deploy{(fallback ? " (fallback)" : "")}: " + string.Join("; ", btl._part1Mercs.Where(m => m.OnField)
                    .Select(m => $"chr {m.ChrCode} at ({m.Col},{m.Row}) leader {units[m.LeaderIndex].ChrCode}@({units[m.LeaderIndex].Col},{units[m.LeaderIndex].Row}) side {m.Side}"))
                + $" — left home {btl._part1Mercs.Count(m => !m.OnField)}" + Environment.NewLine);
    }

    /// <summary>용병이 설 칸 — 그 칸에 설 수 있으면 거기, 아니면 그 칸에서 가까운(대장 둘레 3 칸 안) 빈 칸, 그것도 없으면 대장 둘레 아무 빈 칸.</summary>
    private (int Col, int Row) Spot(int col, int row, UnitState leader, UnitState merc)
    {
        var btl = host.Btl;
        bool Ok(int c, int r) => btl.CanStand(c, r, merc);
        if (Ok(col, row)) return (col, row);
        for (int radius = 1; radius <= 4; radius++)
            for (int dy = -radius; dy <= radius; dy++)
                foreach (int dx in new[] { -(radius - Math.Abs(dy)), radius - Math.Abs(dy) }.Distinct())
                    if (Ok(col + dx, row + dy) && Math.Abs(col + dx - leader.Col) <= 3 && Math.Abs(row + dy - leader.Row) <= 3) return (col + dx, row + dy);
        return btl.FreeCellNear(leader.Col, leader.Row, merc);
    }

    /// <summary>배치 창 바로 아래에 붙는 용병 칸 창의 자리 — 판의 인물을 가리지 않게.</summary>
    internal (int X, int Y) Panel()
    {
        var (px, py) = host.Btl.DeployPanel();
        return (px + BattleScene.DeployW - PanelW, py + host.Btl.DeployPanelHeight() + 6);
    }

    private int GridTop => 30;
    private int ListTop => GridTop + 3 * CellH + 10;

    /// <summary>칸 (gx, gy) 에 그릴 칸 번호 — 대장이 보는 쪽으로 돌린 판 그대로 보여 준다(위 = 판의 위).</summary>
    private static int SlotAt(int gx, int gy, Facing f)
    {
        for (int i = 0; i < SlotCount; i++) if (Offset(i, f) == (gx - 1, gy - 1)) return i;
        return Center;
    }

    private int PanelH(int poolCount) => ListTop + Math.Max(1, poolCount) * RowH + 44;

    /// <summary>창의 클릭 — 창 안이면 먹는다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (Leader is not { } leader) return false;
        var pool = Pool();
        var (x, y) = Panel();
        if (bx < x || bx >= x + PanelW || by < y || by >= y + PanelH(pool.Count)) return false;
        int gx0 = x + (PanelW - 3 * CellW) / 2;
        if (by >= y + GridTop && by < y + GridTop + 3 * CellH && bx >= gx0 && bx < gx0 + 3 * CellW)
        {
            PickSlot(SlotAt((bx - gx0) / CellW, (by - y - GridTop) / CellH, leader.Facing));
            return true;
        }
        int row = (by - y - ListTop) / RowH;
        if (by >= y + ListTop && row >= 0 && row < pool.Count) { ListFocus = row; Assign(pool[row].Chr); return true; }
        int bty = y + ListTop + Math.Max(1, pool.Count) * RowH + 8;
        if (by >= bty && by < bty + 26)
        {
            if (bx < x + PanelW / 2) Array.Clear(SlotsOf(leader.ChrCode));
            else Leader = null;
        }
        return true;
    }

    internal void Draw()
    {
        if (Leader is not { } leader || host._db is not { } db) return;
        var pool = Pool();
        var (x, y) = Panel();
        int h = PanelH(pool.Count);
        bool pad = host.Pad.Active && host.Pad.Connected;
        host.UiFill(x, y, PanelW, h, StatusScreen.PanelBg);
        host.UiStroke(x, y, PanelW, h, StatusScreen.BoxLine);
        host.UiFill(x, y, PanelW, 26, StatusScreen.HeadBg);
        string Name(int chr) => db.Character(chr) is { } c ? db.T(c.NameId) : $"Chr {chr}";
        var s = SlotsOf(leader.ChrCode);
        host.UiText($"용병 배치 — {Name(leader.ChrCode)}", x + 10, y + 4, White);
        string count = $"{s.Count(c => c > 0)}/8";
        host.UiText(count, x + PanelW - 12 - host.UiGetText(count, White).W, y + 4, White);

        int gx0 = x + (PanelW - 3 * CellW) / 2;
        for (int gy = 0; gy < 3; gy++)
            for (int gx = 0; gx < 3; gx++)
            {
                int i = SlotAt(gx, gy, leader.Facing);
                int cx = gx0 + gx * CellW, cy = y + GridTop + gy * CellH;
                host.UiFill(cx + 1, cy + 1, CellW - 2, CellH - 2, i == Center ? 0xFF3A3020 : s[i] > 0 ? 0xFF1E3E6A : StatusScreen.BoxBg);
                host.UiStroke(cx + 1, cy + 1, CellW - 2, CellH - 2, i == Slot ? GamePad.FocusColor : StatusScreen.BoxLine);
                string label = i == Center ? "대장" : s[i] > 0 ? Short(Name(s[i])) : "·";
                uint ink = i == Center ? 0xFFFFE070 : s[i] > 0 ? White : DimGray;
                var (_, tw, th) = host.UiGetText(label, ink, 11);
                host.UiText(label, cx + (CellW - tw) / 2, cy + (CellH - th) / 2, ink, 11);
            }

        for (int r = 0; r < pool.Count; r++)
        {
            var (chr, total, left) = pool[r];
            int ry = y + ListTop + r * RowH;
            if (pad && r == ListFocus) host.UiStroke(x + 4, ry - 1, PanelW - 8, RowH - 2, GamePad.FocusColor);
            uint ink = left > 0 ? White : DimGray;
            host.UiText(Name(chr), x + 12, ry + 2, ink);
            string n = $"{left}/{total}";
            host.UiText(n, x + PanelW - 12 - host.UiGetText(n, ink).W, ry + 2, ink);
        }
        if (pool.Count == 0) host.UiText("고용한 용병이 없습니다", x + 12, y + ListTop + 2, DimGray);

        int by = y + ListTop + Math.Max(1, pool.Count) * RowH + 8;
        Button(x + 8, by, PanelW / 2 - 12, 26, pad ? $"비우기 {host.Pad.Name(PadAction.Ring)}" : "모두 비우기");
        Button(x + PanelW / 2 + 4, by, PanelW / 2 - 12, 26, pad ? $"닫기 {host.Pad.Name(PadAction.Cancel)}" : "닫기");

        void Button(int bx, int by2, int bw, int bh, string text)
        {
            host.UiFill(bx, by2, bw, bh, StatusScreen.HeadBg);
            var (_, tw, th) = host.UiGetText(text, White);
            host.UiText(text, bx + (bw - tw) / 2, by2 + (bh - th) / 2, White);
        }
    }

    private static string Short(string name) => name.Length <= 4 ? name : name[..4];

    /// <summary>배치 단계에서 고른 칸이 판 어디인지 — 초록 칸 위에 파란 칸으로 미리 보여 준다.</summary>
    internal IEnumerable<(int Col, int Row)> PreviewCells()
    {
        foreach (var u in host._units.Where(u => u.Alive && u.OnField && u.PlayerControlled && u.LeaderIndex < 0))
            if (_slots.TryGetValue(u.ChrCode, out var s))
                for (int i = 0; i < SlotCount; i++)
                    if (i != Center && s[i] > 0 && Offset(i, u.Facing) is var (dx, dy)) yield return (u.Col + dx, u.Row + dy);
    }
}
