using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 전투판 위에 겹쳐 그리는 창 두 가지 — 링 커맨드(우클릭)와 Status 화면.
/// </summary>
/// <remarks>
/// <b>링 커맨드</b>는 원본 링 메뉴(<c>0x1006b810</c> → 창 <c>0x100e0a40</c>)를 따랐다: 유닛 머리 위 40픽셀을 중심으로
/// 반지름 89 원 위에 항목 6개(Attack·Ability·Item·System·Status·Rest). Attack·Ability 는 남은 TP + CTP 가
/// Num[4](=100) 이상일 때만 켜진다(<c>0x10074280</c>). 아이콘 그림은 아직 못 찾아서 글자로 그린다.
/// 항목 자리는 게임 화면 캡처(위 = 공격, 오른쪽 아래 = STATUS)에 맞춰 시계 방향으로 놓았다.
///
/// <b>Status 화면</b>은 게임 화면(죠안 캡처)의 칸 구성을 640×480 에 옮겼다. 값은 <see cref="GameDatabase"/> 의
/// 식으로 셈한다 — 죠안은 HP 만 빼고 게임 화면과 같다(HP 는 어빌리티 패시브 보너스를 아직 안 넣음).
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private GameDatabase? _db;
    private readonly Dictionary<int, SpriteFrame> _faces = [];

    // ── 링 커맨드 ────────────────────────────────────────────────────────────

    private enum RingCommand { Attack, Ability, Status, System, Item, Rest }

    private static readonly (RingCommand Command, string Label, string Hover)[] RingItems =
    [
        (RingCommand.Attack, "공격", "ATTACK"),
        (RingCommand.Rest, "휴식", "REST"),
        (RingCommand.Status, "상태", "STATUS"),
        (RingCommand.System, "시스템", "SYSTEM"),
        (RingCommand.Item, "아이템", "ITEM"),
        (RingCommand.Ability, "어빌", "ABILITY"),
    ];

    private const int RingRadius = 89, RingItemRadius = 22, RingLift = 40;

    private int _ringUnit = -1;
    private int _ringHover = -1;
    private int _statusUnit = -1;
    private string _toast = "";
    private double _toastUntil;

    /// <summary>링 중심 — 유닛 발에서 40픽셀 위. 원본처럼 링이 판 밖으로 나가지 않게 안쪽으로 당긴다.</summary>
    private (int X, int Y) RingCenter(UnitState unit)
    {
        var (fx, fy) = UnitFoot(unit);
        int margin = RingRadius + RingItemRadius + 2;
        return (Math.Clamp(fx, margin, BoardWidth - margin), Math.Clamp(fy - RingLift, GridTop + margin, BoardHeight - margin));
    }

    private (int X, int Y) RingItemCenter(int index)
    {
        var (cx, cy) = RingCenter(_units[_ringUnit]);
        double angle = -Math.PI / 2 + index * Math.PI / 3;
        return (cx + (int)Math.Round(Math.Cos(angle) * RingRadius), cy + (int)Math.Round(Math.Sin(angle) * RingRadius));
    }

    private int RingItemAt(int bx, int by)
    {
        if (_ringUnit < 0) return -1;
        for (int i = 0; i < RingItems.Length; i++)
        {
            var (x, y) = RingItemCenter(i);
            if ((bx - x) * (bx - x) + (by - y) * (by - y) <= RingItemRadius * RingItemRadius) return i;
        }
        return -1;
    }

    private bool RingItemEnabled(RingCommand command, int ringUnit)
    {
        var unit = _units[ringUnit];
        if (command is RingCommand.Status or RingCommand.System) return true;
        if (!IsPlayerTurn || ringUnit != _turn || unit.IsBusy) return false;
        if (command is not (RingCommand.Attack or RingCommand.Ability)) return true;
        return _db == null || unit.Tp + unit.Ctp >= _db.N(4);
    }

    /// <summary>우클릭: 열린 창·링을 닫거나, 목록·대상 고르기·걸음을 취소한다. 취소할 것이 없고 인물 위면 링을 연다.</summary>
    private void OnRightClick(int bx, int by)
    {
        if (_statusUnit >= 0) { _statusUnit = -1; return; }
        if (_ringUnit >= 0) { _ringUnit = -1; return; }
        if (CancelStep()) return;
        int index = UnitAtBoard(bx, by);
        if (index < 0) return;
        _selected = index;
        _ringUnit = index;
        _ringHover = RingItemAt(bx, by);
    }

    /// <summary>Space: 고른 인물의 링을 열거나 닫는다. 고른 인물이 없으면 알려 준다.</summary>
    private void ToggleRingForSelected()
    {
        if (_ringUnit >= 0) { _ringUnit = -1; return; }
        if ((uint)_selected >= _units.Length) { Toast("먼저 인물을 고르세요 (클릭·Tab)"); return; }
        _ringUnit = _selected;
        _ringHover = -1;
    }

    /// <summary>링이 열려 있으면 항목을 실행하고 true. 링 밖을 누르면 링만 닫는다.</summary>
    private bool OnRingClick(int bx, int by)
    {
        if (_ringUnit < 0) return false;
        int item = RingItemAt(bx, by);
        int unit = _ringUnit;
        _ringUnit = -1;
        if (item < 0) return true;

        var (command, _, hover) = RingItems[item];
        if (!RingItemEnabled(command, unit))
        {
            Toast(unit != _turn || !_units[unit].IsAlly ? $"{hover}: 차례인 아군만 쓸 수 있습니다" : $"{hover}: TP 가 모자랍니다");
            return true;
        }
        switch (command)
        {
            case RingCommand.Status: _statusUnit = unit; break;
            case RingCommand.Rest: Toast($"{UnitName(unit)} 휴식"); Rest(unit); break;
            case RingCommand.Attack: BeginAttackTargeting(); break;
            case RingCommand.Ability: CommitMoveForAction(); _abilityMenu = true; break;
            default: Toast($"{hover}: 아직 구현하지 않았습니다"); break;
        }
        return true;
    }

    private void Toast(string text)
    {
        _toast = text;
        _toastUntil = _lastTime + 2.5;
    }

    private string UnitName(int index) => _names.GetValueOrDefault(_units[index].ChrCode, "");

    private int UnitAtBoard(int bx, int by)
    {
        if (by < GridTop) return -1;
        int col = bx / TileW, row = (by - GridTop) / TileH;
        return Array.FindIndex(_units, u => u.Alive && u.Col == col && u.Row == row);
    }

    private void DrawRing()
    {
        if (_ringUnit < 0) return;
        var (cx, cy) = RingCenter(_units[_ringUnit]);
        StrokeCircle(cx, cy, RingRadius, 0xC060A8FF, 2);

        for (int i = 0; i < RingItems.Length; i++)
        {
            var (command, label, _) = RingItems[i];
            var (x, y) = RingItemCenter(i);
            bool enabled = RingItemEnabled(command, _ringUnit), hover = i == _ringHover;
            FillCircle(x, y, RingItemRadius, hover ? 0xF0284C9C : 0xE0142850);
            StrokeCircle(x, y, RingItemRadius, enabled ? (hover ? 0xFFB0E0FF : 0xFF5A9CF0) : 0xFF505868, hover ? 3 : 2);
            var (_, w, h) = GetText(label, enabled ? White : DimGray);
            DrawText(label, x - w / 2, y - h / 2, enabled ? White : DimGray);
        }

        if (_ringHover >= 0)
        {
            var (x, y) = RingItemCenter(_ringHover);
            DrawText(RingItems[_ringHover].Hover, x + RingItemRadius - 6, y + RingItemRadius - 10, 0xFFFFFFFF, 15);
        }
    }

    // ── Status 화면 ──────────────────────────────────────────────────────────

    private const int StatusW = 640, StatusH = 480;
    private const uint PanelBg = 0xF00A1428, BoxBg = 0xFF13203A, BoxLine = 0xFF3C5C98, HeadBg = 0xFF34549A, Red = 0xFFE04040, Bar = 0xFF7A1C1C;

    private (int X, int Y) StatusOrigin() => ((BoardWidth - StatusW) / 2, GridTop + (BoardHeight - GridTop - StatusH) / 2);

    private bool OnStatusClick(int bx, int by)
    {
        if (_statusUnit < 0) return false;
        var (ox, oy) = StatusOrigin();
        bool inside = bx >= ox && by >= oy && bx < ox + StatusW && by < oy + StatusH;
        bool close = bx >= ox + StatusW - 78 && bx < ox + StatusW - 6 && by >= oy + 6 && by < oy + 28;
        if (close || !inside) _statusUnit = -1;
        return true;
    }

    private void DrawStatusScreen()
    {
        if (_statusUnit < 0) return;
        var (ox, oy) = StatusOrigin();
        FillRect(ox, oy, StatusW, StatusH, PanelBg);
        StrokeRect(ox, oy, StatusW, StatusH, BoxLine);
        DrawText("STATUS", ox + 14, oy + 8, 0xFF80D0FF, 18);
        FillRect(ox + StatusW - 78, oy + 6, 72, 22, HeadBg);
        DrawText("CLOSE", ox + StatusW - 64, oy + 9, White);

        var unit = _units[_statusUnit];
        if (_db?.Character(unit.ChrCode) is not { } c)
        {
            DrawText("이 인물의 게임 자료(assets/data)를 못 읽었습니다.", ox + 20, oy + 60, Red);
            return;
        }
        var db = _db;
        int tp = unit.Tp, soul = unit.Soul, hp = unit.Hp;

        // 1열 — 능력치
        int x = ox + 16, w = 184;
        Header(x, oy + 38, w, db.T(11));
        Box(x, oy + 60, w, 76);
        if (_faces.TryGetValue(unit.ChrCode, out var face)) BlitClipped(face, x + 6, oy + 66, 64, 64);
        StrokeRect(x + 6, oy + 66, 64, 64, BoxLine);
        string[] names = [db.T(c.NameId), db.T(c.TitleId), db.FamilyName(c), db.JobName(c)];
        for (int i = 0; i < names.Length; i++) RightText(names[i], x + w - 8, oy + 64 + i * 17, White);

        Box(x, oy + 140, w, 42);
        Stat(x, oy + 144, w, db.T(160), c.Level.ToString());
        Stat(x, oy + 162, w, db.T(161), "0");

        Box(x, oy + 186, w, 100);
        StatBar(x, oy + 190, w, db.T(159), hp, unit.MaxHp);
        StatBar(x, oy + 222, w, db.T(41), soul, unit.MaxSoul);
        StatBar(x, oy + 254, w, db.T(38), tp, unit.MaxTp);

        Box(x, oy + 290, w, 60);
        Stat(x, oy + 294, w, db.T(156), db.Atk(c, soul).ToString());
        Stat(x, oy + 312, w, db.T(157), db.Acr(c, tp).ToString());
        Stat(x, oy + 330, w, db.T(158), db.Rdp(c, unit.Hp, unit.MaxHp).ToString());

        Box(x, oy + 354, w, 112);
        (ushort Id, int Value)[] basics = [(34, (int)c.Lp), (39, c.Ctp), (40, db.Stp(c)), (35, db.Psy(c)), (37, c.Dep), (36, db.Dex(c))];
        for (int i = 0; i < basics.Length; i++) Stat(x, oy + 358 + i * 18, w, db.T(basics[i].Id), basics[i].Value.ToString());

        // 2열 — 상태이상 · 장착 어빌리티 · 장비
        x = ox + 216; w = 184;
        Header(x, oy + 38, w, db.T(163));
        Box(x, oy + 60, w, 38);
        for (int i = 0; i < 3; i++)
        {
            StrokeRect(x + 10 + i * 58, oy + 70, 50, 18, BoxLine);
            DrawText("EMPTY", x + 14 + i * 58, oy + 71, DimGray);
        }

        Header(x, oy + 110, w, db.T(166));
        Box(x, oy + 132, w, 84);
        for (int i = 0; i < 3; i++) DrawText(db.T(0), x + 64, oy + 140 + i * 24, i == 0 ? White : DimGray);

        Header(x, oy + 228, w, db.T(14));
        Box(x, oy + 250, w, 216);
        var weapon = c.Items[0] != 0 && db.Items.TryGetValue(c.Items[0], out var wi) ? wi : null;
        FillRect(x + 6, oy + 256, w - 12, 26, 0xFF0C1830);
        StrokeRect(x + 6, oy + 256, w - 12, 26, 0xFF8FB8F0);
        DrawText("WEAPON", x + 10, oy + 258, 0xFF8FB8F0);
        if (weapon != null) DrawText(GameDatabase.WeaponTypeName(weapon.Type), x + 90, oy + 262, White, 15);
        for (int i = 0; i < 6; i++)
        {
            string item = c.Items[i] != 0 && db.Items.TryGetValue(c.Items[i], out var it) ? db.T(it.NameId) : db.T(0);
            RightText(item, x + w - 12, oy + 292 + i * 28, c.Items[i] != 0 ? White : DimGray);
        }

        // 3열 — 획득한 어빌리티 · 획득할 수 있는 어빌리티
        x = ox + 420; w = 204;
        Header(x, oy + 38, w, db.T(12));
        Box(x, oy + 60, w, 156);
        int row = 0;
        foreach (var (abilityId, level) in c.Abilities.OrderBy(a => a.Ability).Take(8))
        {
            if (!db.Abilities.TryGetValue(abilityId, out var ab)) continue;
            AbilityRow(x, oy + 66 + row++ * 22, w, $"{db.T(ab.NameId)} Lv{level}", NextExp(db, ab, level));
        }

        Header(x, oy + 228, w, db.T(13));
        Box(x, oy + 250, w, 216);
        row = 0;
        if (db.Jobs.TryGetValue(c.JobId, out var job))
        {
            var learned = c.Abilities.Select(a => (int)a.Ability).ToHashSet();
            foreach (ushort abilityId in job.AbilityList)
            {
                if (abilityId == 0 || learned.Contains(abilityId) || !db.Abilities.TryGetValue(abilityId, out var ab)) continue;
                AbilityRow(x, oy + 258 + row++ * 30, w, $"{db.T(ab.NameId)} Lv1", NextExp(db, ab, 1));
            }
        }
    }

    /// <summary>그 레벨 work 의 EXP 비용(att +0x30) — Status 화면 빨간 숫자. 최대 레벨이면 비운다. 게임 화면 9개와 맞음.</summary>
    private static string NextExp(GameDatabase db, AbilityData ab, int level) =>
        level < ab.MaxLevel && ab.WorkByLevel.TryGetValue(level, out int wid) && db.Works.TryGetValue(wid, out var work) && work.ExpCost > 0
            ? work.ExpCost.ToString() : "";

    private void Header(int x, int y, int w, string text)
    {
        FillRect(x, y, w, 20, HeadBg);
        StrokeRect(x, y, w, 20, 0xFF7FA6E8);
        var (_, tw, _) = GetText(text, White);
        DrawText(text, x + (w - tw) / 2, y + 2, White);
    }

    private void Box(int x, int y, int w, int h)
    {
        FillRect(x, y, w, h, BoxBg);
        StrokeRect(x, y, w, h, BoxLine);
    }

    private void Stat(int x, int y, int w, string label, string value)
    {
        DrawText(label, x + 8, y, White);
        RightText(value, x + w - 8, y, White);
    }

    private void StatBar(int x, int y, int w, string label, int value, int max)
    {
        Stat(x, y, w, label, $"{value} / {max}");
        FillRect(x + 8, y + 22, w - 16, 3, Bar);
        if (max > 0) FillRect(x + 8, y + 22, (w - 16) * Math.Clamp(value, 0, max) / max, 3, Red);
    }

    private void AbilityRow(int x, int y, int w, string name, string cost)
    {
        DrawText(name, x + 10, y, White);
        if (cost.Length > 0) RightText(cost, x + w - 10, y, Red);
    }

    private void RightText(string text, int right, int y, uint color)
    {
        var (_, w, _) = GetText(text, color);
        DrawText(text, right - w, y, color);
    }

    private void BlitClipped(SpriteFrame f, int x, int y, int w, int h)
    {
        int sx = Math.Max(0, (f.W - w) / 2), sy = Math.Max(0, (f.H - h) / 2);
        for (int yy = 0; yy < Math.Min(h, f.H); yy++)
            for (int xx = 0; xx < Math.Min(w, f.W); xx++)
            {
                uint px = f.Px[(sy + yy) * f.W + sx + xx];
                if ((px & 0xFF000000) != 0) SetPixel(x + xx, y + yy, px);
            }
    }

    private void DrawToast()
    {
        if (_toast.Length == 0 || _lastTime > _toastUntil) return;
        var (_, w, h) = GetText(_toast, White);
        int x = (BoardWidth - w) / 2, y = BoardHeight - h - 24;
        FillRect(x - 10, y - 6, w + 20, h + 12, 0xD0101828);
        DrawText(_toast, x, y, White);
    }

    // ── 원 ───────────────────────────────────────────────────────────────────

    private void FillCircle(int cx, int cy, int r, uint color)
    {
        for (int y = -r; y <= r; y++)
        {
            int half = (int)Math.Sqrt(r * r - y * y);
            for (int x = -half; x <= half; x++) SetPixel(cx + x, cy + y, color);
        }
    }

    private void StrokeCircle(int cx, int cy, int r, uint color, int thickness)
    {
        int outer = r * r, inner = (r - thickness) * (r - thickness);
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                int d = x * x + y * y;
                if (d <= outer && d > inner) SetPixel(cx + x, cy + y, color);
            }
    }
}
