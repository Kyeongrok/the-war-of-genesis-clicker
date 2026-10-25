namespace DuelDx;

using WarOfGenesis.Assets;
using static DuelDx.GameWindow;

/// <summary>
/// 파트1 맵 물체 — 전투 파일 절 A(<c>u16 Obt, u16 x, u16 y</c>)가 놓는 <c>Obts\NNNN.obt</c> 그림·발자국(<see cref="Part1MapObject"/>).
/// 파트2 물체(<see cref="DemoObject"/> — .obj 종류 · HP · 상자 아이템)와 틀이 달라 따로 든다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 36절. 원본 흐름:
/// <list type="bullet">
/// <item>만들기 — 로더 <c>0x1002d27b</c> 가 차례대로 <c>0x10097270(x, y, 칸 높이, Obt, 0, 맵)</c>: 바탕 <c>0x100988b0</c> 이 세계 (40x, 40y, 20·높이)에 놓고
/// Obt 를 읽어(<c>0x10098a80</c> → <c>0x10023220</c> → <c>0x10022bc0</c>) 동작 0 을 건다. 물체 목록 <c>[전투 +0xb9a8]</c> 의 차례 = 절 A 차례 = 행동 707 의 a0.</item>
/// <item>판에 찍기 <c>0x10097910</c> — 물체 높이 = 발자국 칸(지형 비트 0x10 아닌 칸) 밑의 판 높이 가운데 가장 높은 것, 그다음 <c>0x100996e0</c>:
/// 칸마다 판 지형 = 물체 지형 B(그대로 덮음), 물체 높이 A ≠ 0 이면 판 높이 = 물체 높이 + A. 앞 물체가 찍은 판 위에 다음 물체를 잰다.</item>
/// <item>동작 키(<c>0x10098c70</c>): 0 장 바꾸기(판은 안 다시 찍는다) · 1 소리 · 2 자식 그림(그림자 Obs 0355 · 0356, 불꽃 Obs 0023 …) · 9 맵에 알림 9 —
/// 맵이 높이·지형을 비우고 제 장을 찍은 뒤 물체마다 <b>기준 칸(키 10)의 높이</b>로 다시 잰다(<c>0x10089560</c> → <c>0x1009ab00</c> → <c>0x10097a80</c>) ·
/// 10 기준 칸 (a0, a1) · 6 · 11 은 안 옮겼다(아래).</item>
/// <item>행동 707 <c>0x10021880</c> — 물체 a0 에 동작 a1(<c>0x10098af0</c>: 같은 동작이 아직 도는 중이면 그대로, 아니면 처음부터 · 장은 첫 종류 0 키).
/// 자료 33번 — 보이지 않는 벽 262 를 동작 16(빈 장 + 키 9)·1 로 열고(0042 · 0043 · 0044 · 0046 · 0071 · 0083 · 0397 · 0410, 지키던 이가 죽으면),
/// 0114 문 120 을 19·5 로, 0226 나무 233 을 20(타는 나무)으로.</item>
/// </list>
/// 안 옮긴 것: 온도(머리 낱말 6 쪽으로 틱마다 1씩, <c>0x10099f60</c>)와 불·얼음 공격이 물체에 열을 더해(<c>0x1009a3b0</c>) 160 을 넘으면 타는 동작(26·28·30 · 32~34)으로
/// 바뀌는 길, 타는 물체가 곁 유닛을 다치게 하는 것(<c>0x10097af0</c> · 키 6 <c>0x10098d53</c>), 키 11(<c>0x10098f66</c> 물체 뿌리기 [H]).
/// 그리기 깊이: 원본은 띠마다 (칸 줄, 높이) 바이트로 유닛과 앞뒤를 가리지만(<c>0x1001bc60</c>) 리메이크는 파트2 물체처럼 유닛 <b>뒤</b>에 그린다 [H].
/// </remarks>
internal sealed class Part1MapObjects(GameWindow host)
{
    private sealed class Obj(Part1MapObject def, int col, int row)
    {
        public readonly Part1MapObject Def = def;
        public readonly (int Item, int Gold, bool Locked)? Chest = def.Chest;
        public readonly int Col = col, Row = row;
        public int Motion, Frame, Z, AnchorX, AnchorY;
        public int StampFrame;           // 판에 찍힌 장 — 원본은 만들 때와 키 9 때만 찍어서, 그 뒤 장이 바뀌어도 판은 그대로다
        public double MotionAt;
        public int DoneTick = -1;        // 처리한 마지막 틱(동작 시작부터) — −1 이면 틱 0 키도 아직
    }

    private enum ZMode { Keep, Initial, Anchor }

    private readonly List<Obj?> _objects = [];
    private DemoScene? _for;
    private ZMode _zMode = ZMode.Keep;

    /// <summary>판의 물체를 처음 상태로(동작 0) — 판을 세울 때 · RESTART(<see cref="BattleScene.LoadEvents"/>).</summary>
    internal void Reset()
    {
        _for = host._scene;
        _objects.Clear();
        foreach (var (obt, x, y) in host._scene.Part1Objects ?? [])
        {
            // 읽지 못한 Obt 도 자리를 둔다 — 행동 707 의 a0 는 절 A 차례다.
            if (Part1MapObject.Load(obt) is not { } def) { _objects.Add(null); continue; }
            var o = new Obj(def, x, y);
            Start(o, 0);
            o.StampFrame = o.Frame;              // 만들자마자 동작 0 의 장으로 찍는다(0x10097910) — 시작 사건의 707 보다 먼저다
            _objects.Add(o);
        }
        _zMode = ZMode.Initial;
        host.Btl.RestampObjects();
    }

    private void Ensure()
    {
        if (!ReferenceEquals(_for, host._scene)) Reset();
    }

    // ── 상자 ─────────────────────────────────────────────────────────────────────────────────────────────
    // 원본(분석-파트1-전투 37절): 차례 끝에 이동도 행동도 안 한 유닛(0x10030164 → 0x10036350)이 곁의 물체(0x1008f390)에 알림 2 를,
    // 그 유닛이 어빌리티 63 「잠긴상자열기」를 쓰면(0x1009e020 ≠ 0xff) 알림 5 를 더 보낸다. 물체는 Obt 반응표(0x10099d60)대로 —
    // 나무 상자 · 든 것이 정해진 상자는 알림 2, 쇠 상자(324 · 325~359)는 알림 5 에 반응 6(0x10099e1e: 사람 조종 유닛만) → 동작 19(0x1009a710),
    // 동작 19 의 키 6 이 곁 유닛에 알림 12(아이템) · 13(eld)을 보내 전투가 가방 · 돈에 넣고 창을 띄운다(0x100330f0 · 0x10033450), 19 가 끝나면 동작 5(열린 장).
    // 리메이크는 사용자 결정대로 파트2 처럼 — 차례인 인물이 닿는 상자 위에 주먹 커서, 누르면(필요하면 옆까지 걸어가) 연다(TP 80).

    /// <summary>열린 상자 수 — 닿는 칸 칠하기(<see cref="BattleScene"/> 이동 영역)가 다시 셀 때를 안다.</summary>
    internal int OpenedChests { get { Ensure(); return _objects.Count(o => o is { Chest: not null } && Opened(o)); } }

    /// <summary>상자 목록(모드 &gt; 상자 내용물 보기) — 상자마다 몸 칸 · 든 것 · 잠김 · 열었나.</summary>
    internal IEnumerable<(int Col, int Row, int Item, int Gold, bool Locked, bool Opened)> Chests()
    {
        Ensure();
        foreach (var o in _objects)
            if (o is { Chest: { } c })
            {
                var (col, row) = BodyCells(o)[0];
                yield return (col, row, c.Item, c.Gold, c.Locked, Opened(o));
            }
    }

    private static bool Opened(Obj o) => o.Motion is Part1MapObject.OpenMotion or Part1MapObject.OpenedMotion;

    /// <summary>상자의 몸 — 찍힌 장에서 걷기 금지(지형 비트 1) 칸. 상자 324 · 147 은 1×3 의 가운데 칸.</summary>
    private static List<(int Col, int Row)> BodyCells(Obj o)
    {
        var f = o.Def.Frames[o.StampFrame];
        var cells = new List<(int, int)>();
        for (int r = 0; r < f.Rows; r++)
            for (int c = 0; c < f.Cols; c++)
                if ((f.Terrain[r * f.Cols + c] & 1) != 0) cells.Add((o.Col + c, o.Row + r));
        if (cells.Count == 0) cells.Add((o.Col, o.Row));
        return cells;
    }

    /// <summary>마우스가 상자로 치는 칸 — 몸 칸과 그림만 걸친 칸(지형 0x10, 뚜껑이 그려진 윗칸).</summary>
    private static bool Covers(Obj o, int col, int row)
    {
        var f = o.Def.Frames[o.StampFrame];
        int c = col - o.Col, r = row - o.Row;
        return (uint)c < (uint)f.Cols && (uint)r < (uint)f.Rows && (f.Terrain[r * f.Cols + c] & 0x11) != 0;
    }

    private Obj? ChestAt(int col, int row)
    {
        Ensure();
        // 몸 칸을 먼저 — 그림만 걸친 칸은 뒤 상자의 몸과 겹칠 수 있다.
        return _objects.FirstOrDefault(o => o is { Chest: not null } && BodyCells(o).Contains((col, row)))
            ?? _objects.FirstOrDefault(o => o is { Chest: not null } && Covers(o, col, row));
    }

    /// <summary>잠긴 상자를 여는 어빌리티 63 「잠긴상자열기」(Attack 368) — 쓰는 레벨(<c>0x1009e020</c>, 직업 · 장비)로 가졌을 때만(<c>0x10036406</c>).</summary>
    internal const int UnlockAbility = 63;

    private bool CanUnlock(UnitState u) => host.Btl.Part1.Fighter(u)?.Chr.EffectiveLevel(UnlockAbility) >= 0;

    private bool CanOpen(UnitState u, Obj o) => o.Chest is { } chest && !Opened(o) && (!chest.Locked || CanUnlock(u));

    /// <summary>주먹 커서 — 그 칸이 차례인 인물이 열 수 있고(걸어가서라도) 닿는 상자인가.</summary>
    internal bool CanTouchAt(int col, int row)
    {
        var btl = host.Btl;
        if (btl._turn < 0 || row < 0 || ChestAt(col, row) is not { } o) return false;
        var user = host._units[btl._turn];
        return CanOpen(user, o) && btl.FindTouchPathTo(user, BodyCells(o)) != null;
    }

    /// <summary>이동 영역 위에 노랗게 칠할 상자 칸(판 칸 번호) — 차례인 인물이 걸어가 열 수 있는 것.</summary>
    internal IEnumerable<int> TouchableCells(UnitState user, BattleScene.MoveRange range)
    {
        Ensure();
        foreach (var o in _objects)
        {
            if (o == null || !CanOpen(user, o) || host.Btl.FindTouchPathTo(user, BodyCells(o), range) == null) continue;
            foreach (var (c, r) in BodyCells(o))
                if ((uint)c < (uint)host.Cols && (uint)r < (uint)host.Rows) yield return r * host.Cols + c;
        }
    }

    private Obj? _pendingTouch;

    /// <summary>
    /// 화면 밖 시험(T 키, <c>DUELDX_TOUCH</c>) — 그 인물을 가장 가까운 닫힌 상자(「열,줄」이면 그 칸의 상자) 몸 아래 칸에 세우고 연다.
    /// 잠긴 상자를 못 여는 인물이면 알림만 띄운다. TP 는 안 본다. 「place」 는 두 칸 떨어져 세우기만, 「…key」 는 어빌리티 63 을 가진 아군으로.
    /// </summary>
    internal bool TouchNearestForTest(UnitState user, string spec)
    {
        Ensure();
        bool place = spec == "place";
        // 「…,key」 — 잠긴 상자를 여는 인물(어빌리티 63)로 바꿔 시험한다.
        if (spec.EndsWith("key"))
        {
            spec = spec[..^3].TrimEnd(',');
            if (host._units.FirstOrDefault(u => u.Alive && u.OnField && u.PlayerControlled && CanUnlock(u)) is { } keyHolder) user = keyHolder;
        }
        var cell = spec.Split(',');
        Obj? o = cell.Length == 2 && int.TryParse(cell[0], out int tc) && int.TryParse(cell[1], out int tr) ? ChestAt(tc, tr)
            : _objects.Where(x => x is { Chest: not null } && !Opened(x) && x.Col < host.Cols && x.Row < host.Rows)
                      .OrderBy(x => Math.Abs(x!.Col - user.Col) + Math.Abs(x.Row - user.Row)).FirstOrDefault();
        if (o == null || Opened(o)) return false;
        var (bc, br) = BodyCells(o)[^1];
        user.ResetTo(bc, Math.Clamp(br + 1 + (place ? 2 : 0), 0, host.Rows - 1), keepFacing: true);
        host.Btl.CenterOnCell(bc, br);
        if (place) return true;                          // 「place」 — 세우기만(두 칸 떨어져서), 여는 것은 클릭으로 시험한다
        if (!CanOpen(user, o)) { host.Toast("잠긴 상자입니다 — 「잠긴상자열기」 어빌리티가 있어야 열 수 있습니다."); return true; }
        Open(o, user);
        return true;
    }

    /// <summary>상자 칸을 눌렀을 때 — 옆이면 바로, 아니면 옆까지 걸어가서 연다. 처리했으면 true(연 상자 · 못 닿는 상자 · 상자 아닌 칸은 false).</summary>
    internal bool TryTouch(int col, int row)
    {
        var btl = host.Btl;
        if (ChestAt(col, row) is not { } o || Opened(o)) return false;
        var user = host._units[btl._turn];
        var body = BodyCells(o);
        if (btl.FindTouchPathTo(user, body) == null) return false;
        if (!CanOpen(user, o))
        {
            host.Toast("잠긴 상자입니다 — 「잠긴상자열기」 어빌리티가 있어야 열 수 있습니다.");
            return true;
        }
        if (btl.FindTouchPathTo(user, body, needTp: true) is not { } path)
        {
            host.Hint($"TP 가 모자랍니다 — 상자를 열려면 옆 칸까지 걷고도 TP+CTP 로 {BattleScene.ObjectTouchTp} 을 낼 수 있어야 합니다");
            return true;
        }
        if (path.Count == 0) { Open(o, user); return true; }
        foreach (var cell in path) user.Path.Enqueue(cell);
        _pendingTouch = o;
        return true;
    }

    /// <summary>걸어간 인물이 멈췄으면 기다리던 상자를 연다 — 매 틀(<see cref="BattleScene.ResolvePendingTouch"/>).</summary>
    internal void ResolvePendingTouch()
    {
        if (_pendingTouch is not { } o) return;
        var btl = host.Btl;
        if (!btl.IsPlayerTurn || !ReferenceEquals(_for, host._scene)) { _pendingTouch = null; return; }
        var user = host._units[btl._turn];
        if (user.IsBusy) return;
        _pendingTouch = null;
        if (CanOpen(user, o) && btl.FindTouchPathTo(user, BodyCells(o), needTp: true) is { Count: 0 }) Open(o, user);
    }

    /// <summary>
    /// 상자를 연다 — 동작 19(여는 동작, 끝나면 <see cref="Step"/> 가 동작 5 = 열린 장)를 걸고 든 것을 준다:
    /// 아이템은 전투 팀 가방(<c>0x100ada10</c>), eld 는 돈(<c>+0xb958</c>). 든 것이 0 이면 무작위(<see cref="Part1MapObject.RandomChestItem"/>).
    /// 창은 원본 글 그대로 「아이템 획득」 · 「%s 1개를 획득하였습니다.」 / 「eld 획득」 · 「%deld를 획득하였습니다.」
    /// (<c>0x1011c030</c> · <c>0x1011c03c</c> · <c>0x1011c058</c> · <c>0x1011c064</c>). 원본은 키 6 의 틱(4 · 6 · 12)에 주지만 리메이크는 여는 순간 준다 [H].
    /// </summary>
    private void Open(Obj o, UnitState user)
    {
        var btl = host.Btl;
        btl.CommitMove(user);
        user.Tp -= BattleScene.ObjectTouchTp;
        Start(o, Part1MapObject.OpenMotion);
        var (item, gold, _) = o.Chest!.Value;
        if (gold > 0)
        {
            host.Part1.Eld += gold;
            btl.ShowSpoilMessage(user, "eld 획득", $"{gold}eld를 획득하였습니다.");
        }
        else
        {
            if (item <= 0) item = Part1MapObject.RandomChestItem(host._rng);
            host.Part1.Bag.Add(item);
            string name = Part1Item.ById(Part1Tables.Items, item)?.Name ?? $"아이템 {item}";
            btl.ShowSpoilMessage(user, "아이템 획득", $"{name} 1개를 획득하였습니다.");
        }
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"p1chest obt {o.Def.Id} at ({o.Col},{o.Row}) by {user.ChrCode} item {item} gold {gold}{Environment.NewLine}");
    }

    /// <summary>동작 걸기(<c>0x10098af0</c>) — 같은 동작이 아직 끝나지 않았으면 그대로 둔다. 장은 첫 종류 0 키의 것(없으면 그대로).</summary>
    private void Start(Obj o, int motion)
    {
        int length = o.Def.Motion(o.Motion)?.Length ?? -1;
        if (o.Motion == motion && o.DoneTick >= 0 && TickOf(o) < length) return;
        o.Motion = motion;
        o.MotionAt = host._lastTime;
        o.DoneTick = -1;
        if (o.Def.FirstFrame(motion) is >= 0 and var f && f < o.Def.Frames.Count) o.Frame = f;
    }

    private int TickOf(Obj o) => Math.Max(0, (int)((host._lastTime - o.MotionAt) * TicksPerSecond));

    /// <summary>
    /// 행동 707 [물체, 동작](<c>0x10021880</c>). 원본은 707 을 칸에 걸고 바로 끝내며, 자료는 늘 행동 1(한 틀 기다림)을 사이에 둬서
    /// 앞 동작의 틱 0 키(262 동작 16 의 키 9)가 돈 뒤에 다음 동작이 걸린다. 리메이크는 같은 틀에 둘이 올 수 있어 앞 동작의 틱 0 키를 먼저 돌려 둔다.
    /// </summary>
    internal void SetMotion(int index, int motion)
    {
        Ensure();
        if ((uint)index >= (uint)_objects.Count || _objects[index] is not { } o) return;
        if (o.DoneTick < 0) RunKeys(o, 0, 0);
        Start(o, motion);
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"p1obj {index} obt {o.Def.Id} motion {motion} frame {o.Frame}{Environment.NewLine}");
    }

    /// <summary>동작 키를 지난 틱만큼 돌린다 — 동작은 끝나면 처음으로(<c>0x10098be0</c>).</summary>
    private void Step(Obj o)
    {
        // 상자 상태 6(0x1009a8ec): 여는 동작 19 가 끝나면 동작 5(열린 장)로.
        if (o.Chest != null && o.Motion == Part1MapObject.OpenMotion && TickOf(o) >= (o.Def.Motion(o.Motion)?.Length ?? 0))
        {
            RunKeys(o, Math.Max(0, o.DoneTick + 1), (o.Def.Motion(o.Motion)?.Length ?? 1) - 1);
            Start(o, Part1MapObject.OpenedMotion);
        }
        int now = TickOf(o);
        if (now <= o.DoneTick) return;
        // 오래 멈췄다 돌아오면(창 · 저장) 지난 틱을 다 돌지 않는다 — 키는 장 · 기준 칸 · 다시 찍기라 마지막 몇 바퀴면 같다.
        int length = Math.Max(1, o.Def.Motion(o.Motion)?.Length ?? 1);
        int from = Math.Max(o.DoneTick + 1, now - 2 * length);
        RunKeys(o, from, now);
    }

    private void RunKeys(Obj o, int from, int to)
    {
        if (o.Def.Motion(o.Motion) is not { } m) { o.DoneTick = Math.Max(o.DoneTick, to); return; }
        for (int t = from; t <= to; t++)
        {
            int cycle = m.Length > 0 ? t % m.Length : t;
            foreach (var k in m.Keys)
            {
                if (k.Start != cycle) continue;
                switch (k.Kind)
                {
                    case 0:                              // 장 바꾸기(0x10098cd9) — 판은 다시 안 찍는다
                        if ((uint)k.Args[0] < (uint)o.Def.Frames.Count) o.Frame = k.Args[0];
                        break;
                    case 1:                              // 소리(0x10098cec → 0x1001a330)
                        if (!host.Tlk._talkSkip && k.Args[0] > 0) host.Play(Part1Game.ObsBase + k.Args[0]);
                        break;
                    case 9:                              // 맵에 알림 9(0x10098f34) — 판을 비우고 맵 · 물체를 다시 찍는다(기준 칸 높이로)
                        // 알림은 곧바로 처리된다(0x1009d440 → 맵 가상 +0x48) — 그때 물체마다 지금 장이 찍힌다.
                        foreach (var other in _objects) if (other != null) other.StampFrame = other.Frame;
                        _zMode = ZMode.Anchor;
                        host.Btl.RestampObjects();
                        break;
                    case 10:                             // 기준 칸(0x10098f4b) — 물체 +0x58 · +0x5a
                        (o.AnchorX, o.AnchorY) = (k.Args[0], k.Args[1]);
                        break;
                }
            }
        }
        o.DoneTick = Math.Max(o.DoneTick, to);
    }

    /// <summary>
    /// 판에 찍기 — <see cref="BattleScene.RebuildStamp"/> 가 지형(맵 장)을 베낀 뒤 부른다. 물체 높이는 판을 세울 때 발자국 밑 가장 높은 칸(<c>0x10097910</c>),
    /// 키 9 뒤에는 기준 칸 높이(<c>0x10097a80</c>)로 다시 재고, 그 밖에는 잰 값 그대로 찍는다(같은 판이 나온다).
    /// </summary>
    internal void Stamp(int[] walkH, int[] aimH, ushort[] flags, int cols, int rows)
    {
        Ensure();
        foreach (var o in _objects)
        {
            if (o == null) continue;
            var f = o.Def.Frames[o.StampFrame];
            if (_zMode == ZMode.Initial)
            {
                int z = 0;
                for (int r = 0; r < f.Rows; r++)
                    for (int c = 0; c < f.Cols; c++)
                    {
                        int mc = o.Col + c, mr = o.Row + r;
                        if ((f.Terrain[r * f.Cols + c] & 0x10) == 0 && (uint)mc < cols && (uint)mr < rows) z = Math.Max(z, walkH[mr * cols + mc] & 0xFF);
                    }
                o.Z = z;
            }
            else if (_zMode == ZMode.Anchor)
            {
                int ac = o.Col + o.AnchorX, ar = o.Row + o.AnchorY;
                o.Z = (uint)ac < cols && (uint)ar < rows ? walkH[ar * cols + ac] & 0xFF : 0;
            }
            for (int r = 0; r < f.Rows; r++)
                for (int c = 0; c < f.Cols; c++)
                {
                    int mc = o.Col + c, mr = o.Row + r;
                    if ((uint)mc >= cols || (uint)mr >= rows) continue;
                    int i = mr * cols + mc, cell = r * f.Cols + c;
                    byte b = f.Terrain[cell];
                    flags[i] = (ushort)((b & 1) | b << Part1Map.RawShift);      // Part1Map 과 같은 꼴 — 파트2 뜻(0x1 못 들어감) + 원래 값
                    if (f.Heights[cell] != 0) walkH[i] = aimH[i] = (o.Z + f.Heights[cell]) & 0xFF;
                }
            if (BattleScene.Trace)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                    $"p1stamp obt {o.Def.Id} at ({o.Col},{o.Row}) {_zMode} z {o.Z} frame {o.StampFrame} {f.Cols}x{f.Rows}{Environment.NewLine}");
        }
        _zMode = ZMode.Keep;
    }

    /// <summary>물체를 그린다 — 키를 돌리고, 자식 그림 가운데 z &lt; 0(그림자 — 땅에 깔린다)을 먼저, 물체 장, 나머지 자식(불꽃) 차례로 [H: 원본은 깊이로 가린다].</summary>
    internal void Draw()
    {
        Ensure();
        foreach (var o in _objects)
        {
            if (o == null) continue;
            Step(o);
            var f = o.Def.Frames[o.Frame];
            int ox = o.Col * TileW, oy = GridTop + host.BoardPad + o.Row * TileH - o.Z * HeightStep;
            DrawChildren(o, ox, oy, under: true);
            if (f.Width > 0 && f.Height > 0) host.BlitMasked(f.Pixels, f.Width, f.Height, ox + f.X, oy + f.Y);
            DrawChildren(o, ox, oy, under: false);
        }
    }

    /// <summary>
    /// 키 종류 2 — 자식 그림(<c>0x10098d05</c> → <c>0x10085e00(Obs a0, 동작 a1, x a2, y a3, z a4, 0xff, 물체)</c>, 자리는 물체 기준 세계 값).
    /// 화면에는 (x, y·32/40 − z·12/20) — 상자 그림자 (19, −793, −1165) 가 상자 밑 (19, 65) 에 온다. 자식은 제 동작이 끝나면 사라진다(<c>0x10085e80</c>).
    /// </summary>
    private void DrawChildren(Obj o, int ox, int oy, bool under)
    {
        if (o.Def.Motion(o.Motion) is not { } m) return;
        int tick = TickOf(o), cycle = m.Length > 0 ? tick % m.Length : tick;
        foreach (var k in m.Keys)
        {
            if (k.Kind != 2 || k.Start > cycle || (k.Args[4] < 0) != under) continue;
            int obs = Part1Game.ObsBase + k.Args[0], motion = k.Args[1], age = cycle - k.Start;
            if (host.UiFor(obs) is not { } sprite) continue;
            int life = sprite.ShownLength(motion);
            if (life > 0 && age >= life) continue;
            int key = sprite.BlendAt(motion, age);
            host.DrawUi(obs, motion, age, ox + k.Args[2], oy + k.Args[3] * 32 / 40 - k.Args[4] * 12 / 20, BlendOf(key), loop: false, fade: BlendFade(key));
        }
    }

    private const int SaveFields = 6;

    /// <summary>저장 — 물체마다 (동작, 장, 찍힌 장, 높이, 기준 x, 기준 y). 못 읽은 물체는 −1 여섯.</summary>
    internal int[] Save()
    {
        Ensure();
        return [.. _objects.SelectMany(o => o is null ? Enumerable.Repeat(-1, SaveFields) : [o.Motion, o.Frame, o.StampFrame, o.Z, o.AnchorX, o.AnchorY])];
    }

    /// <summary>되살리기 — 동작은 지금부터 다시 돈다(틱 0 키는 이미 돈 것으로). 칸 수가 안 맞으면(다른 판) 그대로 둔다.</summary>
    internal void Restore(int[]? saved)
    {
        Ensure();
        if (saved == null || saved.Length != _objects.Count * SaveFields) return;
        for (int i = 0; i < _objects.Count; i++)
        {
            int at = i * SaveFields;
            if (_objects[i] is not { } o || saved[at] < 0) continue;
            o.Motion = saved[at];
            o.Frame = Math.Clamp(saved[at + 1], 0, o.Def.Frames.Count - 1);
            o.StampFrame = Math.Clamp(saved[at + 2], 0, o.Def.Frames.Count - 1);
            (o.Z, o.AnchorX, o.AnchorY) = (saved[at + 3], saved[at + 4], saved[at + 5]);
            o.MotionAt = host._lastTime;
            o.DoneTick = 0;
        }
        _zMode = ZMode.Keep;
        host.Btl.RestampObjects();
    }
}
