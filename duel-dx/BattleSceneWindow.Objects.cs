namespace DuelDx;

/// <summary>
/// 전투판에 놓인 물체 — 상자·문·포탑·바리케이트(분석-전투 「물체(오브젝트) 배열 <c>+0x3c74</c>」).
/// </summary>
/// <remarks>
/// 물체는 <b>파일이 놓는 것이 전부</b>다 — 전투 중에 새로 생기지 않는다. 길은 안 막고, <b>문(종류 1·4)만</b>
/// 걸어 들어갈 목표 칸이 될 수 없다(<c>0x100746b0</c>). 데모는 아직 그리기와 목표 칸 막기까지만 한다 —
/// 상자 열기(work 386)·부수기·포탑의 차례는 아직이다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private IReadOnlyList<DemoObject> Objects => _scene.Objects ?? [];

    /// <summary>그 칸에 선 물체 — 없으면 null.</summary>
    private DemoObject? ObjectAt(int col, int row) =>
        Objects.FirstOrDefault(o => o.Alive && !_opened.Contains(o) && o.Col == col && o.Row == row);

    /// <summary>그 칸이 물체 때문에 설 수 없는 칸인가 — 문과 스위치문뿐이다.</summary>
    private bool ObjectBlocks(int col, int row) => ObjectAt(col, row) is { Data.BlocksStanding: true };

    /// <summary>
    /// 물체를 만지는 work <b>386</b> — 상하좌우 한 칸, <b>TP 80</b>. 원본은 명령 0x2714 가 이 work 으로 물체에 손을 댄다.
    /// </summary>
    private const int ObjectTouchTp = 80;

    /// <summary>물체가 부서질 때의 폭발 그림.</summary>
    private const int ObjectBreakObs = 1009;

    /// <summary>차례인 인물이 그 칸의 물체에 손을 댈 수 있나 — 옆 한 칸이고 TP 가 남아 있어야 한다.</summary>
    private bool CanTouchObject(UnitState user, DemoObject obj) =>
        Math.Abs(user.Col - obj.Col) + Math.Abs(user.Row - obj.Row) == 1
        && user.Tp >= ObjectTouchTp && obj.Data.Kind is 1 or 2 or 6 or 8;

    /// <summary>
    /// 상자를 연다 — 아이템이 들었으면 가방에, 아니면 돈을 지갑에 넣고 물체를 치운다(<c>0x100e7ce0</c>).
    /// </summary>
    private bool TryTouchObject(int col, int row)
    {
        if (!IsPlayerTurn || _units[_turn].IsBusy) return false;
        if (ObjectAt(col, row) is not { } obj || !CanTouchObject(_units[_turn], obj)) return false;

        CommitMove(_units[_turn]);
        _units[_turn].Tp -= ObjectTouchTp;
        _opened.Add(obj);

        if (obj.Data.Kind == 8)
        {
            Explode(obj);
            return true;
        }

        if (obj.Data.Kind == 1)                          // 문 — 만지면 열린다(0x100e603f 의 갈래표)
        {
            Toast($"{_db?.T((ushort)obj.Data.NameId)} 이(가) 열렸습니다.");
            Play(MosesClickSound);
            return true;
        }

        if (obj.Record.ItemId > 0)
        {
            _inventory[obj.Record.ItemId] = _inventory.GetValueOrDefault(obj.Record.ItemId) + 1;
            string itemName = _db?.Items.GetValueOrDefault(obj.Record.ItemId) is { } item ? _db.T(item.NameId) : "";
            Toast($"{(itemName.Length > 0 ? itemName : $"아이템 {obj.Record.ItemId}")} 1개를 획득하였습니다.");
        }
        else if (obj.Record.Gold > 0)
        {
            _shopMoney += obj.Record.Gold;
            Toast($"{obj.Record.Gold}GP 를 획득하였습니다.");
        }
        else
            Toast("비어 있습니다.");

        Play(MosesClickSound);
        return true;
    }

    /// <summary>
    /// 화면 밖 시험용 — 차례인 인물을 가장 가까운 상자 옆으로 세우고 그 상자를 연다(T 키, <c>DUELDX_TOUCH=1</c>).
    /// 상자까지 걸어가는 데 여러 칸이 걸려 클릭으로는 확인하기 번거롭다.
    /// </summary>
    private bool TouchNearestObjectForTest()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_TOUCH") is not { Length: > 0 }) return false;
        // 시험은 차례를 안 기다린다 — 첫 아군으로 친다.
        var user = _units.FirstOrDefault(u => u.Alive && u.PlayerControlled);
        if (user is null) return false;
        // B 키면 부술 수 있는 물체를, 그 밖에는 열 수 있는 물체를 고른다.
        bool breaking = Environment.GetEnvironmentVariable("DUELDX_TOUCH") == "break";
        var target = Objects.Where(o => !_opened.Contains(o) && o.Alive
                                        && (breaking ? o.Data.Breakable && o.Record.Team != 4 : o.Data.Kind is 2 or 6 or 8))
                            .OrderBy(o => Math.Abs(o.Col - user.Col) + Math.Abs(o.Row - user.Row))
                            .FirstOrDefault();
        if (target is null) return false;
        // 열 때는 옆 한 칸, 칠 때는 기본공격 자리(정확히 두 칸)로 세운다.
        user.ResetTo(target.Col, Math.Clamp(target.Row + (breaking ? 2 : 1), 0, Rows - 1));
        if (!breaking) return TryTouchObject(target.Col, target.Row);
        _turn = Array.IndexOf(_units, user);
        return TryBreakObject(target.Col, target.Row, force: true);
    }

    /// <summary>
    /// 물체를 친다 — 물체에는 RDP 가 없어 <b>공격력이 그대로 피해</b>가 되고(<c>0x100e79ac</c>), 명중·치명·흔들기도 없다.
    /// HP 가 0 이하면 부서지면서 친 인물에게 소울 10 과 경험치를 준다(<c>0x1006ab70</c>).
    /// </summary>
    private bool TryBreakObject(int col, int row, bool force = false)
    {
        if (!force && (!IsPlayerTurn || _units[_turn].IsBusy)) return false;
        if (ObjectAt(col, row) is not { Data.Breakable: true } obj) return false;
        var user = _units[_turn];
        // 제 편 물체는 못 친다 — 종류 7(바리케이트)은 적·중립이면, 9·10 은 적이면 칠 수 있다.
        bool foe = obj.Record.Team != (user.PlayerControlled ? 4 : 0);
        if (!foe || (obj.Data.Kind is 9 or 10 && obj.Record.Team < 0)) return false;
        if (user.Data is not { } c || _db is null || Work(c.BasicWorkId) is not { } w) return false;
        // 기본공격과 같은 자리 규칙 — 옆 두 칸(모양 2 십자, 사거리 5~8) 안이어야 친다.
        if (!InWorkRange(w, user.Col, user.Row, col, row, user)) return false;

        CommitMove(user);
        user.Tp = Math.Max(0, user.Tp - w.TpBase);
        int damage = _db.Atk(c, user.Soul, w.Power);
        obj.Hp -= damage;
        ShowNumber(user, damage.ToString(), DamageColor);
        Play(MosesClickSound);

        if (obj.Hp > 0) return true;
        // 부서지면 그 자리에 폭발이 한 번 돈다(Obs 1009, 0x100e7ba0).
        _effects.Add((ObjectBreakObs, 0, _lastTime, col * TileW + TileW / 2, CellCenterY(col, row)));
        user.Soul = Math.Min(user.MaxSoul, user.Soul + 10);
        Toast($"{_db.T((ushort)obj.Data.NameId)} 이(가) 부서졌습니다.");
        return true;
    }

    /// <summary>
    /// 폭탄 상자(종류 8)가 터진다 — 제 <c>ATK</c> 로 <b>반경 안의 모두</b>를 친다(적아 안 가린다).
    /// </summary>
    private void Explode(DemoObject obj)
    {
        _effects.Add((ObjectBreakObs, 0, _lastTime,
                      obj.Col * TileW + TileW / 2, CellCenterY(obj.Col, obj.Row)));
        Play(MosesClickSound);
        if (_db is null) return;

        int reach = Math.Max(1, obj.Data.Radius);
        foreach (var u in _units.Where(u => u.Alive && u.Data is not null
                                            && Math.Abs(u.Col - obj.Col) + Math.Abs(u.Row - obj.Row) <= reach))
        {
            int damage = (_db.N(3) - _db.Rdp(u.Data!, u.Hp, u.MaxHp)) * obj.Data.Attack / Math.Max(1, _db.N(3));
            if (damage <= 0) continue;
            u.Hp = Math.Max(0, u.Hp - damage);
            ShowNumber(u, damage.ToString(), DamageColor);
        }
        Toast($"{_db.T((ushort)obj.Data.NameId)} 이(가) 터졌습니다.");
    }

    /// <summary>
    /// 물체의 차례 — 물체는 TP 게이지를 안 쓰고 <b><c>전투틱 % wtp == 0</c></b> 인 틱에만 움직인다(<c>0x100e71f0</c>).
    /// 종류 3·9·10 만 차례를 받고, 그중 9·10 은 편이 중립(−1)이면 영영 안 움직인다.
    /// </summary>
    private void StepObjects()
    {
        foreach (var obj in Objects)
        {
            if (!obj.Alive || _opened.Contains(obj) || !obj.Data.Acts) continue;
            if (obj.Data.Kind is 9 or 10 && obj.Record.Team < 0) continue;
            int every = Math.Max(1, obj.Data.TurnEvery);
            if (_tick % every != 0) continue;
            ObjectActs(obj);
        }
    }

    /// <summary>
    /// 포탑·힐 크리스탈이 한 번 움직인다 — 사거리 안의 상대를 친다.
    /// 피해는 <c>(1000 − 대상 RDP) × 물체 공격력 / 1000</c> 이고 PSY·무기·치명타가 없다(<c>0x1007b8f4</c>).
    /// </summary>
    private void ObjectActs(DemoObject obj)
    {
        if (_db is null || obj.Data.Attack <= 0) return;
        // 어디까지 닿는지는 그 물체가 쓰는 work 이 정한다(파일 +31). work 이 없으면 옆 한 칸으로 본다.
        var work = Work(obj.Data.WorkId);
        bool ally = obj.Record.Team == 4;

        bool Reaches(UnitState u) => work is { } w
            ? InWorkRange(w, obj.Col, obj.Row, u.Col, u.Row)
            : Math.Abs(u.Col - obj.Col) + Math.Abs(u.Row - obj.Row) <= 1;

        // 힐 크리스탈(종류 10)의 work 은 회복이다 — 제 편을 고쳐 준다. 나머지는 상대를 친다.
        bool heals = work is { IsHeal: true };
        var target = _units.Where(u => u.Alive && u.PlayerControlled == (heals ? ally : !ally) && Reaches(u))
                           .OrderBy(u => heals ? u.Hp * 100 / Math.Max(1, u.MaxHp) : u.Hp)
                           .FirstOrDefault();
        if (target?.Data is not { } tc) return;

        if (heals)
        {
            int heal = target.MaxHp * (work?.Power ?? 0) / 100;
            if (heal <= 0 || target.Hp >= target.MaxHp) return;
            int before = target.Hp;
            target.Hp = Math.Min(target.MaxHp, target.Hp + heal);
            ShowNumber(target, (target.Hp - before).ToString(), HealColor2);
            return;
        }

        int damage = (_db.N(3) - _db.Rdp(tc, target.Hp, target.MaxHp)) * obj.Data.Attack / Math.Max(1, _db.N(3));
        if (damage <= 0) return;
        target.Hp = Math.Max(0, target.Hp - damage);
        ShowNumber(target, damage.ToString(), DamageColor);
    }

    /// <summary>이미 연 상자 — 판에서 사라진다.</summary>
    private readonly HashSet<DemoObject> _opened = [];

    /// <summary>물체를 칸에 그린다 — 인물보다 먼저(뒤에) 그려 인물이 앞에 서게 한다.</summary>
    private void DrawObjects()
    {
        foreach (var obj in Objects)
        {
            if (!obj.Alive || _opened.Contains(obj) || obj.Data.SpriteId <= 0) continue;
            int x = obj.Col * TileW + TileW / 2, y = CellTop(obj.Col, obj.Row) + TileH;
            DrawUi(obj.Data.SpriteId, 0, (int)(_lastTime * TicksPerSecond), x, y, UiBlend.Alpha);
        }
    }
}
