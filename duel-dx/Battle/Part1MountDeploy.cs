using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 배치 창의 「탑승」 — 무리의 탈것(<see cref="Part1Progress.Mounts"/>)에 출전 인물을 태운다(사용자 보고: Btl 20014 는 아지다하카를 탈 수 있는데 배치 창에서 안 된다).
/// </summary>
/// <remarks>
/// 원본(alpha.dll): 배치 객체가 무리 탈것 표(전투 <c>+0xb8b0</c> 수, <c>+0xb8b4</c> 8 B 칸: Chr · 수)를 탈것 목록(<c>+0x78</c>, 16 B: Chr · 남은 수, 수 <c>+0x6c</c>)으로 옮기고
/// 인물 칸(0x6c B)마다 고른 탈것 차례 <c>+0x68</c> 을 0xffff(안 탐)로 연다(<c>0x10093413</c> · <c>0x1009362b~0x100936e5</c>) — 탈것은 판마다 새로 고른다.
/// 인물 메뉴(<c>0x10094630</c>) 다섯째 단추(차례 4) 「탑승」은 머리 낱말 9(<c>+0xb974</c>)가 0 이고, 탈것 목록이 있고, 그 인물이 아직 안 섰고(<c>+0x24</c> = 0),
/// 용병을 안 거느렸을 때(<c>+0x2e</c> = 0, <c>0x100948e1</c>) 켜진다. 목록의 어느 탈것도 탈 수 없으면(<c>0x1009e750</c> — 탑승 어빌리티 121~126, 아론다이트는 열쇠) 흐리다.
/// 낱말 9 ≠ 0 이면 「탑승 금지」(0x1011c2bc). 여섯째(차례 5) 「탑승 취소」는 탈것을 골랐을 때(<c>+0x68</c> ≠ 0xffff) 켜진다.
/// 「탑승」을 누르면 탈것 창(<c>0x10095450</c>)이 「탑승물을 선택하세요.」 / 「현재 %s 탑승중.」 아래에 탈것 이름 · 남은 수를 늘어놓고, 그 인물이 탈 수 없는 줄은 흐리게 한다.
/// 줄을 누르면(<c>0x100941a8~</c>) 남은 수가 있을 때만 앞서 고른 탈것을 목록에 돌려주고(+1) 새 탈것을 고르고 남은 수 −1. 「탑승 취소」(<c>0x10093c1c</c>)는 되돌린다.
/// 출전 확정(<c>0x1002f279~0x1002f2c5</c>)이 고른 탈것이 있으면 <c>0x100364b0(탈것 Chr)</c> — 그 자리에 탈것 유닛을 새로 세우고 탄 이는 판 밖으로(<c>+0x1bc</c> = 1,
/// <c>+0x1cc</c> / <c>+0x1d0</c> 로 서로 잇고, 방향 · 편을 옮기고, 탈것 SKILL <c>+0xc9</c> = 탄 이 SKILL), 탈것에 <c>+0x1c0</c> = 1(무리 탈것 — 쓰러지면 무리가 잃는다).
/// 그 판의 용병 배치는 건너뛴다(탄 인물은 용병을 못 거느린다).
/// <para>리메이크는 일행을 처음부터 칸에 세워 두므로 「안 섰을 때」 대신 「고정이 아닐 때」로 보고, 고르는 즉시 판의 유닛을 탈것으로 바꿔 보인다.
/// 고른 것은 일행 창의 탑승 지정(<see cref="Part1Progress.Riding"/>, 리메이크가 더한 것)에도 적어 RESTART · 다음 판이 이어받는다 — 판을 세울 때(BuildUnits) 그 지정대로 태운다.</para>
/// </remarks>
internal sealed class Part1MountDeploy(GameWindow host)
{
    /// <summary>창을 연 유닛(탄 인물이면 탈것 유닛) — null 이면 닫힘.</summary>
    internal UnitState? Who;

    internal int ListFocus;

    internal bool Open => Who != null;

    /// <summary>이 판이 「탑승 금지」인가(머리 낱말 9).</summary>
    internal bool Forbidden { get; private set; }

    internal const int PanelW = BattleScene.DeployW, RowH = 22, ListTop = 52;

    internal void Reset(int sceneId)
    {
        Forbidden = IsForbidden(sceneId);
        Who = null;
    }

    /// <summary>머리 낱말 9(<c>+0xb974</c>) ≠ 0 — 「탑승 금지」 판.</summary>
    internal static bool IsForbidden(int sceneId) =>
        sceneId >= Part1Game.BattleBase && Part1Battle.Load(sceneId - Part1Game.BattleBase) is { MountsForbidden: true };

    /// <summary>그 유닛이 무리 탈것이면 탄 인물, 아니면 그 유닛.</summary>
    internal UnitState Person(UnitState u) =>
        host.Btl._part1TeamMounts.Contains(u) && host.Btl._part1Riders.TryGetValue(u, out var rider) ? rider : u;

    /// <summary>그 유닛이 탄 무리 탈것(Chr) — 안 탔으면 0.</summary>
    internal int MountOf(UnitState u) => host.Btl._part1TeamMounts.Contains(u) && host.Btl._part1Riders.ContainsKey(u) ? u.ChrCode : 0;

    /// <summary>탈것 목록 — 무리 탈것(Chr, 모두, 남은 수). 남은 수 = 모두 − 판에 탈것으로 선 수.</summary>
    internal List<(int Chr, int Total, int Left)> Pool()
    {
        var btl = host.Btl;
        var used = host._units.Where(u => btl._part1TeamMounts.Contains(u) && btl._part1Riders.ContainsKey(u))
                              .GroupBy(u => u.ChrCode).ToDictionary(g => g.Key, g => g.Count());
        return [.. host.Part1.Mounts.Where(m => m.Value > 0).OrderBy(m => m.Key)
                   .Select(m => (m.Key, m.Value, Math.Max(0, m.Value - used.GetValueOrDefault(m.Key))))];
    }

    /// <summary>그 인물이 그 탈것을 탈 수 있나 — 탑승 어빌리티(아론다이트는 열쇠도, <c>0x1009e750</c>). 판을 세울 때와 같은 셈(BuildUnits).</summary>
    internal bool CanRide(UnitState person, int mount)
    {
        if (Part1Chr.Load(person.ChrCode - Part1Game.ChrBase) is not { } raw) return false;
        var chr = host.Part1.Apply(raw);
        var items = Part1Item.LoadAll();
        return chr.CanRide(mount - Part1Game.ChrBase, chr.Equipment.Select(id => Part1Item.ById(items, id)));
    }

    /// <summary>그 인물이 탈 수 없는 까닭 — 탈 수 있으면(이미 탔으면 바꾸거나 내릴 수 있으면) null(인물 메뉴 차례 4 의 켜짐 조건, 0x100948af~0x10094946).</summary>
    internal string? CannotRide(UnitState? who)
    {
        var btl = host.Btl;
        if (Forbidden) return "이 전투는 탑승 금지입니다";
        if (who == null) return "탈 인물을 명단에서 고르세요";
        if (MountOf(who) > 0) return null;
        var pool = Pool();
        if (pool.Count == 0) return "무리에 탈것이 없습니다";
        if (btl._part1Mercs.Contains(who)) return "용병은 탈 수 없습니다";
        if (!btl.DeployMovable(who)) return "이 전투에 고정된 인물은 탈 수 없습니다";
        if (host.MercDeploy.CountOf(who) > 0) return "용병을 거느린 인물은 탈 수 없습니다 — 용병 배치를 비우세요";
        if (!pool.Any(p => CanRide(who, p.Chr))) return "탈 수 있는 탈것이 없습니다 (탑승 어빌리티)";
        return null;
    }

    /// <summary>명단에서 탈 수 있는 첫 인물 — 없으면 명단 첫 인물(까닭을 알리게).</summary>
    internal UnitState? FirstRider(List<UnitState> roster) => roster.FirstOrDefault(u => CannotRide(u) == null) ?? roster.FirstOrDefault();

    /// <summary>「탑승」 단추 — 그 인물의 탈것 창을 연다(같은 인물이면 닫는다).</summary>
    internal void Toggle(UnitState? who)
    {
        if (who != null && Who == who) { Who = null; return; }
        if (CannotRide(who) is { } why) { host.Toast(why); return; }
        OpenFor(who!);
    }

    internal void OpenFor(UnitState who)
    {
        if (CannotRide(who) is { } why) { host.Toast(why); return; }
        host.MercDeploy.Leader = null;     // 용병 칸 창과 한 자리 — 하나만
        Who = who;
        var pool = Pool();
        int now = pool.FindIndex(p => p.Chr == MountOf(who));
        ListFocus = now >= 0 ? now : Math.Max(0, pool.FindIndex(p => p.Left > 0 && CanRide(who, p.Chr)));
    }

    /// <summary>목록 줄을 누름 — 그 탈것에 태운다(원본 0x100941a8: 남은 수가 있을 때만, 앞서 탄 것은 돌려준다).</summary>
    internal void Ride(int mount)
    {
        if (Who is not { } u) return;
        var person = Person(u);
        if (MountOf(u) == mount) return;
        if (Pool().FirstOrDefault(p => p.Chr == mount) is not { Left: > 0 }) { host.Toast("남은 탈것이 없습니다"); return; }
        if (!CanRide(person, mount)) { host.Toast("이 인물은 그 탈것을 탈 수 없습니다 (탑승 어빌리티)"); return; }
        if (MountOf(u) > 0) u = Dismount(u);
        Who = Mount(u, mount);
    }

    /// <summary>「탑승 취소」(원본 차례 5, 0x10093c1c) — 탈것을 무리로 돌려보내고 그 자리에 인물을 세운다.</summary>
    internal void Cancel()
    {
        if (Who is { } u && MountOf(u) > 0) Who = Dismount(u);
    }

    /// <summary>인물을 탈것 유닛으로 바꾼다 — 탈것이 그 자리 · 방향 · 배열 자리를 이어받고, 인물은 판 밖에서 탄 이로 기다린다(<c>0x100364b0</c>).</summary>
    private UnitState Mount(UnitState person, int mount)
    {
        var btl = host.Btl;
        int pi = Array.IndexOf(host._units, person);
        if (pi < 0) return person;
        var m = new UnitState(new DemoUnit(mount, person.Col, person.Row, person.Side, 0, person.StartFacing));
        m.ResetTo(person.Col, person.Row);
        m.StartCol = person.StartCol;
        m.StartRow = person.StartRow;
        m.Facing = person.Facing;
        m.OnField = person.OnField;
        m.Awake = true;
        InitMount(m);
        var units = host._units.ToList();
        units[pi] = m;
        units.Add(person);
        host._units = [.. units];
        bool hadTurn = person.HasTurn;
        person.OnField = false;
        person.ResetTo(0, 0, keepFacing: true);
        person.StartCol = person.StartRow = 0;
        person.HasTurn = hadTurn;
        btl._part1Riders[m] = person;
        btl._part1TeamMounts.Add(m);
        Swap(person, m);
        host.Part1.Riding[person.ChrCode] = mount;
        host.LoadRosterSprites();
        Trace($"part1 deploy mount {person.ChrCode} → {mount} at ({m.Col},{m.Row})");
        return m;
    }

    /// <summary>탈것 유닛을 판에서 빼고 탄 인물을 그 자리에 되세운다.</summary>
    private UnitState Dismount(UnitState m)
    {
        var btl = host.Btl;
        if (!btl._part1Riders.Remove(m, out var person)) return m;
        btl._part1TeamMounts.Remove(m);
        bool hadTurn = person.HasTurn;
        person.ResetTo(m.Col, m.Row, keepFacing: true);
        person.Facing = m.Facing;
        person.StartCol = m.StartCol;
        person.StartRow = m.StartRow;
        person.OnField = m.OnField;
        person.HasTurn = hadTurn;
        // 탈것이 쓰던 배열 자리를 인물에게 주고 탈것을 뺀다 — DropUnits 가 뒤쪽 부하의 대장 번호를 다시 맞춘다.
        var units = host._units;
        int mi = Array.IndexOf(units, m), ri = Array.IndexOf(units, person);
        if (mi >= 0 && ri >= 0) (units[mi], units[ri]) = (person, m);
        Swap(m, person);
        btl.DropUnits(u => u == m);
        host.Part1.Riding.Remove(person.ChrCode);
        Trace($"part1 deploy dismount {person.ChrCode} (was {m.ChrCode}) at ({person.Col},{person.Row})");
        return person;
    }

    /// <summary>배치 창의 표(옮길 수 있음 · 맵 밖 여분 · 고른 사람)에서 from 을 to 로 바꾼다.</summary>
    private void Swap(UnitState from, UnitState to)
    {
        var btl = host.Btl;
        if (btl._deployMovable.Remove(from)) btl._deployMovable.Add(to);
        if (btl._deployBench.Remove(from)) btl._deployBench.Add(to);
        if (btl._deployPick == from) btl._deployPick = to;
    }

    /// <summary>탈것 유닛의 처음 값 — 전투를 열 때(InitBattle)의 파트1 내 편과 같다: 인물 레코드 그대로, TP 0, 소울 40(0~150).</summary>
    private void InitMount(UnitState m)
    {
        if (host._db is not { } db || db.Character(m.ChrCode) is not { } c) return;
        m.Data = c with { Exp = StatusScreen.DemoExp, CumExp = c.Level * 100 };
        m.MaxHp = m.Hp = host.Btl.ScaleMaxHp(m, Math.Max(1, db.MaxHp(m.Data)));
        m.MaxTp = db.MaxTp(m.Data);
        m.Tp = 0;
        m.Stp = Math.Max(0, db.Stp(m.Data));
        m.MaxSoul = 150;
        m.Soul = host._fullSoulAtStart ? 150 : 40;
        m.HasTurn = false;
    }

    private static void Trace(string line)
    {
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), line + Environment.NewLine);
    }

    internal void ListStep(int step)
    {
        int n = Pool().Count;
        if (n > 0) ListFocus = ((ListFocus + step) % n + n) % n;
    }

    internal void RideFocused()
    {
        var pool = Pool();
        if (pool.Count > 0) Ride(pool[Math.Clamp(ListFocus, 0, pool.Count - 1)].Chr);
    }

    /// <summary>탈것 창의 자리 — 용병 칸 창과 같은 곳(배치 창 왼쪽).</summary>
    internal (int X, int Y) Panel()
    {
        var (px, py) = host.Btl.DeployPanel();
        // 배치 창을 왼쪽 끝으로 끌어 놓았으면 오른쪽에 붙인다(fa-73).
        return (px - PanelW - 8 >= host._camX ? px - PanelW - 8 : px + BattleScene.DeployW + 8, py);
    }

    private int PanelH(int poolCount) => ListTop + Math.Max(1, poolCount) * RowH + 44;

    internal bool OnClick(int bx, int by)
    {
        if (Who == null) return false;
        var pool = Pool();
        var (x, y) = Panel();
        if (bx < x || bx >= x + PanelW || by < y || by >= y + PanelH(pool.Count)) return false;
        int row = (by - y - ListTop) / RowH;
        if (by >= y + ListTop && row >= 0 && row < pool.Count)
        {
            ListFocus = row;
            Ride(pool[row].Chr);
            return true;
        }
        int bty = y + ListTop + Math.Max(1, pool.Count) * RowH + 8;
        if (by >= bty && by < bty + 26)
        {
            if (bx < x + PanelW / 2) Cancel();
            else Who = null;
        }
        return true;
    }

    internal void Draw()
    {
        if (Who is not { } who || host._db is not { } db) return;
        var pool = Pool();
        var person = Person(who);
        int mounted = MountOf(who);
        var (x, y) = Panel();
        int h = PanelH(pool.Count);
        bool pad = host.Pad.Active && host.Pad.Connected;
        host.UiFill(x, y, PanelW, h, StatusScreen.PanelBg);
        host.UiStroke(x, y, PanelW, h, StatusScreen.BoxLine);
        host.UiFill(x, y, PanelW, 26, StatusScreen.HeadBg);
        string Name(int chr) => db.Character(chr) is { } c ? db.T(c.NameId) : $"Chr {chr}";
        host.UiText($"탑승 — {Name(person.ChrCode)}", x + 10, y + 4, White);
        // 원본 탈것 창의 머리 글(0x1011c370 · 0x1011c360).
        host.UiText(mounted > 0 ? $"현재 {Name(mounted)} 탑승중." : "탑승물을 선택하세요.", x + 12, y + 30, 0xFFD0E8D0, 11);

        for (int r = 0; r < pool.Count; r++)
        {
            var (chr, total, left) = pool[r];
            int ry = y + ListTop + r * RowH;
            bool current = chr == mounted;
            bool ok = current || left > 0 && CanRide(person, chr);
            if (current) host.UiFill(x + 4, ry - 1, PanelW - 8, RowH - 2, 0xFF2A4A8A);
            if (pad && r == ListFocus) host.UiStroke(x + 4, ry - 1, PanelW - 8, RowH - 2, GamePad.FocusColor);
            uint ink = ok ? White : DimGray;
            host.UiText(Name(chr), x + 12, ry + 2, ink);
            string n = $"{left}/{total}";
            host.UiText(n, x + PanelW - 12 - host.UiGetText(n, ink).W, ry + 2, ink);
        }
        if (pool.Count == 0) host.UiText("무리에 탈것이 없습니다", x + 12, y + ListTop + 2, DimGray);

        int by = y + ListTop + Math.Max(1, pool.Count) * RowH + 8;
        Button(x + 8, by, PanelW / 2 - 12, 26, pad ? $"탑승 취소 {host.Pad.Name(PadAction.Ring)}" : "탑승 취소", mounted > 0);
        Button(x + PanelW / 2 + 4, by, PanelW / 2 - 12, 26, pad ? $"닫기 {host.Pad.Name(PadAction.Cancel)}" : "닫기", true);

        void Button(int bx, int by2, int bw, int bh, string text, bool on)
        {
            host.UiFill(bx, by2, bw, bh, on ? StatusScreen.HeadBg : 0xFF182238);
            uint fg = on ? White : 0xFF707070;
            var (_, tw, th) = host.UiGetText(text, fg);
            host.UiText(text, bx + (bw - tw) / 2, by2 + (bh - th) / 2, fg);
        }
    }
}
