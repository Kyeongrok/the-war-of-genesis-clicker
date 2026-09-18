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

    /// <summary>차례인 인물이 그 칸의 물체에 손을 댈 수 있나 — 옆 한 칸이고 TP 가 남아 있어야 한다.</summary>
    private bool CanTouchObject(UnitState user, DemoObject obj) =>
        Math.Abs(user.Col - obj.Col) + Math.Abs(user.Row - obj.Row) == 1
        && user.Tp >= ObjectTouchTp && obj.Data.Kind is 2 or 6;

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
        if (!IsPlayerTurn) return false;
        var user = _units[_turn];
        var target = Objects.Where(o => !_opened.Contains(o) && o.Data.Kind is 2 or 6)
                            .OrderBy(o => Math.Abs(o.Col - user.Col) + Math.Abs(o.Row - user.Row))
                            .FirstOrDefault();
        if (target is null) return false;
        user.ResetTo(target.Col, Math.Min(Rows - 1, target.Row + 1));
        return TryTouchObject(target.Col, target.Row);
    }

    /// <summary>이미 연 상자 — 판에서 사라진다.</summary>
    private readonly HashSet<DemoObject> _opened = [];

    /// <summary>물체를 칸에 그린다 — 인물보다 먼저(뒤에) 그려 인물이 앞에 서게 한다.</summary>
    private void DrawObjects()
    {
        foreach (var obj in Objects)
        {
            if (!obj.Alive || _opened.Contains(obj) || obj.Data.SpriteId <= 0) continue;
            int x = obj.Col * TileW + TileW / 2, y = GridTop + obj.Row * TileH + TileH;
            DrawUi(obj.Data.SpriteId, 0, (int)(_lastTime * TicksPerSecond), x, y, UiBlend.Alpha);
        }
    }
}
