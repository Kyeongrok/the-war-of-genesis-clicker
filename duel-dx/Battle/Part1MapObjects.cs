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
/// 온도 · 내구 · 타기 · 부서짐(아래 「알림 · 반응」, 분석-파트1-전투 36.4), 칸에 남는 땅불 · 얼음 덩이, 유닛 온도(+0x4a — SPD · 동결 · 불 피해 · 물들이기)도 옮겼다. 안 옮긴 것: 키 11(<c>0x10098f66</c> 물체 뿌리기 [H: 불똥]) · 반응 7~10.
/// 그리기 깊이: 원본처럼 띠마다 (칸 줄, 높이) 바이트로 유닛과 앞뒤를 가린다(<c>0x1001bc60</c> · 아래 「그리기 깊이」).
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
        public int Durability = def.Durability;   // 성질 칸 0 — 원본은 Obt 자료 자체를 깎는다(같은 Obt 물체가 모두 함께 닳는다 [H: 버그]). 리메이크는 물체마다
        public int Heat;                 // +0x4a 온도 — 시간 틱마다 기온 쪽으로 2 씩(0x10099f60), −60~200
        public int State = -1;           // +0x42 다음 상태 — 동작이 끝나면 할 일(0x1009a860). −1 없음
        public int StateAt;              // +0x44 불붙은 시간 틱(0x1009a2d0)
        public bool Busy;                // +0x41 — 서 있으면 전투가 시간 틱을 안 올린다(+0xc9b8, 0x10030075)
    }

    private enum ZMode { Keep, Initial, Anchor }

    private readonly List<Obj?> _objects = [];
    private DemoScene? _for;
    private ZMode _zMode = ZMode.Keep;

    /// <summary>판의 물체를 처음 상태로(동작 0) — 판을 세울 때 · RESTART(<see cref="BattleScene.LoadEvents"/>).</summary>
    internal void Reset()
    {
        _for = host._scene;
        _ambient = null;
        _objects.Clear();
        _lingering.Clear();
        _melting.Clear();
        _unitHeat.Clear();
        _iceMarks.Clear();
        foreach (var u in host._units) u.AnimFrozen = false;
        _killsPending = false;
        foreach (var (obt, x, y) in host._scene.Part1Objects ?? [])
        {
            // 읽지 못한 Obt 도 자리를 둔다 — 행동 707 의 a0 는 절 A 차례다.
            if (Part1MapObject.Load(obt) is not { } def) { _objects.Add(null); continue; }
            var o = new Obj(def, x, y) { Heat = Ambient };
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
    /// 잠긴 상자를 못 여는 인물이면 알림만 띄운다. TP 는 안 본다. 「place」 는 두 칸 떨어져 세우기만, 「…key」 는 어빌리티 63 을 가진 아군으로,
    /// 「at:열,줄」 은 그 칸에 세우기만.
    /// </summary>
    internal bool TouchNearestForTest(UnitState user, string spec)
    {
        Ensure();
        // 「at:열,줄」 — 그 칸에 세우기만(물체 뒤에 가려지는지 · 물체 위에 서는 높이를 볼 때).
        if (spec.StartsWith("at:") && spec[3..].Split(',') is [var sc, var sr] && int.TryParse(sc, out int ac) && int.TryParse(sr, out int ar))
        {
            user.ResetTo(ac, ar, keepFacing: true);
            host.Btl.CenterOnCell(ac, ar);
            return true;
        }
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
        SweepKills();
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
        if (o.Chest != null && o.Motion == Part1MapObject.OpenMotion && Ended(o))
        {
            FinishMotion(o);
            Start(o, Part1MapObject.OpenedMotion);
        }
        StepState(o);
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
                    case 6 when k.Args[0] is not (12 or 13):   // 범위 알림(0x10098d53) — 상자의 12 · 13(아이템 · eld)은 여는 순간 준다(Open)
                        Broadcast(o, k);
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

    // ── 알림 · 반응 — 온도 · 타기 · 부서짐 ───────────────────────────────────────────────────────────────────
    // 원본(분석-파트1-전투 36.4 · 37.3): 알림 0x1009d440(물체, 번호, 서술) → 물체 +0x48 0x10099d60 이 Obt 반응표에서 그 번호의 항목마다 반응 0~13(가상 +0x74 + 4·반응).
    // 알림 번호 — 1 공격(맞는 기술의 타격 사건 0x100768f0 · 0x10076950 · 0x10076a10 · 0x100769b0 이 첫 인자로 1 을 넣고, 사건 0x10076a80 이
    // 기술 +0x41 = 1 이면 범위의 물체에도 보낸다 0x10076c46) · 2 곁에서 대기 · 5 어빌리티 63 · 10 열(타는 물체 · 칸의 불) · 11 냉기 · 12 · 13 상자 아이템 · eld.
    // 공격 서술(16바이트, 0x1003f3c0): +0 유닛 번호 · +2 세기 식(Attack +0x42) · +3 속성(Attack +0x64, 0 이면 무기 · 속성 부여) · +4 둘째 속성 · +8 세기
    // (Part1Combat.Power + 핸들러가 더하는 +0x44²) · +0xc 명중(+0x48). 물체가 보내는 서술은 0x1009ab20: 유닛 0xffff · 식 7 · 세기 = 성질 칸 1² · 속성 = 칸 3.

    /// <summary>공격 서술 가운데 물체가 보는 둘 — +8 세기 · +3 속성(1 화염 · 2 빙한).</summary>
    internal readonly record struct Hit(int Power, int Element);

    private const int MinHeat = -60, MaxHeat = 200, IgniteHeat = 160;

    /// <summary>기온 — 머리 낱말 6(+0xb96e, 로더 <c>0x1002cbe5</c>). 물체 · 유닛 온도가 이쪽으로 간다. 이벤트 행동 102 가 바꾼다(<see cref="SetAmbient"/>).</summary>
    private int Ambient => _ambient ?? host._scene.Part1Ambient;

    private int? _ambient;

    /// <summary>
    /// 이벤트 행동 102 [기온] — 전투 <c>+0xb96e</c> := a0(<c>0x1001de20</c>). 그 뒤 시간 틱마다 유닛 · 물체 온도가 새 기온 쪽으로 간다.
    /// 시판 자료는 전투 0205 둘뿐: 시작 대사 뒤 −40(소리 97), 틱 55 에 170. 원본은 같은 값으로 맵 층 밝기 +0x2c = (a0 + 60)·62/126 − 2 도 바꾸는데, 리메이크는 그 밝기를 그리지 않는다.
    /// </summary>
    internal void SetAmbient(int ambient)
    {
        Log($"p1temp 기온 {Ambient} → {ambient} (행동 102, 틱 {host.Btl._tick})");
        _ambient = ambient;
    }

    /// <summary>불붙는 · 부서지는 동작이 도는 물체가 있나 — 원본은 그동안 시간 틱을 올리지 않는다(+0xc9b8, <c>0x10030075</c>).</summary>
    internal bool Busy { get { Ensure(); return _objects.Any(o => o is { Busy: true }); } }

    private static void Log(string line)
    {
        if (BattleScene.Trace) File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), line + Environment.NewLine);
    }

    private string Name(Obj o) => $"obj {_objects.IndexOf(o)} obt {o.Def.Id} ({o.Col},{o.Row})";

    /// <summary>알림 하나(<c>0x10099d60</c>) — 반응표에서 그 번호의 항목마다 반응한다.</summary>
    private void Notify(Obj o, int notice, Hit hit)
    {
        foreach (var r in o.Def.Reactions)
            if (r.Message == notice) React(o, notice, r.Action, hit);
    }

    /// <summary>
    /// 반응(가상 +0x74 + 4·번호): 0 · 1 맞음(<c>0x1009a450</c> · <c>0x1009a520</c> — 내구를 깎고 열을 더해, 0 이하면 부서짐 13~15 · 16~18) · 2 곧바로 200 · 타기(<c>0x1009a5f0</c>) ·
    /// 3 · 4 곧바로 부서짐(<c>0x1009a660</c> · <c>0x1009a6a0</c>) · 5 동작 35(<c>0x1009a6e0</c>) · 11 열 +5, 160 이상이면 타기(<c>0x1009a600</c>) ·
    /// 12 열 −5, 160 아래면 식기(<c>0x1009a630</c>) · 13 열만(<c>0x1009a3b0</c>). 6(상자 열기)은 <see cref="Open"/> 이, 7~10 은 안 옮겼다.
    /// </summary>
    private void React(Obj o, int notice, int action, Hit hit)
    {
        int heat0 = o.Heat, dur0 = o.Durability, motion0 = o.Motion;
        switch (action)
        {
            case 0 or 1:
            {
                var (damage, heat) = Impact(o, hit);
                o.Durability -= damage;
                o.Heat = Math.Clamp(o.Heat + heat, MinHeat, MaxHeat);
                if (o.Durability <= 0) Break(o, action == 0 ? 13 : 16, 0);
                else CheckBurn(o);
                break;
            }
            case 2:
                o.Heat = MaxHeat;
                Ignite(o);
                break;
            case 3 or 4:
                Break(o, action == 3 ? 13 : 16, action);
                break;
            case 5:
                Start(o, 35);
                SetState(o, 5);
                break;
            case 11:
                o.Heat = Math.Min(MaxHeat, o.Heat + 5);
                if (o.Heat >= IgniteHeat) Ignite(o);
                break;
            case 12:
                o.Heat = Math.Max(MinHeat, o.Heat - 5);
                if (o.Heat < IgniteHeat) Cool(o);
                break;
            case 13:
                o.Heat = Math.Clamp(o.Heat + Impact(o, hit).Heat, MinHeat, MaxHeat);
                CheckBurn(o);
                break;
            default:
                return;
        }
        Log($"p1heat {Name(o)} 알림 {notice} → 반응 {action}{(notice == 1 ? $" (세기 {hit.Power} 속성 {hit.Element})" : "")}: 내구 {dur0} → {o.Durability} · 온도 {heat0} → {o.Heat}"
            + (o.Motion != motion0 ? $" · 동작 {motion0} → {o.Motion}" : "") + $" (틱 {host.Btl._tick})");
    }

    /// <summary><c>0x1009ab70</c> — 피해 = 세기 ÷ 성질 칸 2(0 이면 그대로), 열 = 속성 1 이면 +피해/5 · 2 이면 −피해/5.</summary>
    private static (int Damage, int Heat) Impact(Obj o, Hit hit)
    {
        int damage = o.Def.Defense != 0 ? hit.Power / o.Def.Defense : hit.Power;
        return (damage, hit.Element switch { 1 => damage / 5, 2 => -(damage / 5), _ => 0 });
    }

    /// <summary>열을 받은 뒤(<c>0x1009a4dd</c>~) — 160 을 넘고 타는 물체면 타기, 160 이하이고 타는 물체면 식기.</summary>
    private void CheckBurn(Obj o)
    {
        if (!o.Def.Burns) return;
        if (o.Heat > IgniteHeat) Ignite(o);
        else Cool(o);
    }

    /// <summary>부서짐 — 동작 <paramref name="first"/> + rand()%3, 상태(끝나면 1~3 = 부서진 장).</summary>
    private void Break(Obj o, int first, int state)
    {
        Start(o, first + host._rng.Next(3));
        SetState(o, state);
    }

    /// <summary>
    /// 타기 <c>0x1009a2a0</c> — 동작 0 → 26 · 10 → 28 · 11 → 30 · 12 → 32~34(그 밖은 그대로 — 이미 타거나 부서진 것), 불붙은 시간 틱을 적고 상태 2.
    /// 26 · 28 · 30 은 불이 옮겨붙는 동작(끝나면 20 · 21 · 22 가 타며 돈다), 32~34 는 다 타 버리는 동작(끝나면 6~8 재).
    /// </summary>
    private void Ignite(Obj o)
    {
        int to = o.Motion switch { 0 => 26, 10 => 28, 11 => 30, 12 => 32 + host._rng.Next(3), _ => -1 };
        if (to < 0) return;
        Start(o, to);
        o.StateAt = host.Btl._tick;
        SetState(o, 2);
    }

    /// <summary>식기 <c>0x1009a350</c> — 20 · 26 → 27 · 21 · 28 → 29 · 22 · 30 → 31(꺼지는 동작, 끝나면 10 · 11 · 12 = 그을린 채 멈춤).</summary>
    private void Cool(Obj o)
    {
        int to = o.Motion switch { 20 or 26 => 27, 21 or 28 => 29, 22 or 30 => 31, _ => -1 };
        if (to >= 0) Start(o, to);
    }

    /// <summary><c>0x1009a830</c> — 상태가 비었을 때만 넣고 바쁨을 켠다(<c>0x1009a260</c>).</summary>
    private static void SetState(Obj o, int state)
    {
        if (o.State != -1) return;
        o.State = state;
        o.Busy = true;
    }

    private static void ClearState(Obj o) => (o.State, o.Busy) = (-1, false);

    /// <summary>동작이 끝났나(가상 +0x64 <c>0x10098bb0</c>: 틱 ≥ 길이) — 없는 동작(길이 −1)은 곧바로 끝난 것이다.</summary>
    private bool Ended(Obj o) => o.Def.Motion(o.Motion) is not { } m || TickOf(o) >= m.Length;

    /// <summary>끝난 동작의 남은 키를 마저 돌린다 — 다음 동작을 걸기 전에(부서짐 틱 34 의 키 9 따위를 건너뛰지 않게).</summary>
    private void FinishMotion(Obj o)
    {
        if (o.Def.Motion(o.Motion) is { Length: > 0 } m && o.DoneTick < m.Length - 1) RunKeys(o, Math.Max(0, o.DoneTick + 1), m.Length - 1);
    }

    /// <summary>
    /// 상태 틱 <c>0x1009a860</c> — 0 · 1 · 3 · 4(부서짐): 끝나면 1 + rand()%3 · 5: 끝나면 4 ·
    /// 2(탐, 가상 +0xac <c>0x1009a9c0</c>): 온도가 160 아래면 식기. 아니면 끝난 동작에 따라 26 → 20 · 28 → 21 · 30 → 22(바쁨만 끈다) · 27 → 10 · 29 → 11 · 31 → 12 ·
    /// 32~34 → 6~8(상태를 지운다), 그리고 불붙은 지 20 · 40 · 60 시간 틱이 지나면 20 → 28 · 21 → 30 · 22 → 32~34 로 더 탄다.
    /// </summary>
    private void StepState(Obj o)
    {
        switch (o.State)
        {
            case 0 or 1 or 3 or 4 when Ended(o):
                FinishMotion(o);
                Start(o, 1 + host._rng.Next(3));
                ClearState(o);
                Log($"p1heat {Name(o)} 부서짐 끝 → 동작 {o.Motion} 장 {o.Frame}");
                break;
            case 5 when Ended(o):
                FinishMotion(o);
                Start(o, 4);
                ClearState(o);
                break;
            case 2:
                if (o.Heat < IgniteHeat) { Cool(o); break; }
                if (Ended(o))
                {
                    int motion = o.Motion;
                    (int next, bool clear) = motion switch
                    {
                        26 => (20, false), 27 => (10, true), 28 => (21, false), 29 => (11, true), 30 => (22, false), 31 => (12, true),
                        32 => (6, true), 33 => (7, true), 34 => (8, true), _ => (-1, false),
                    };
                    if (next >= 0)
                    {
                        FinishMotion(o);
                        Start(o, next);
                        if (clear) ClearState(o); else o.Busy = false;
                        Log($"p1heat {Name(o)} 동작 {motion} 끝 → {next} 장 {o.Frame} 온도 {o.Heat}{(clear ? " (상태 끝)" : "")}");
                    }
                }
                int since = host.Btl._tick - o.StateAt;
                int more = (since, o.Motion) switch { ( >= 20, 20) => 28, ( >= 40, 21) => 30, ( >= 60, 22) => 32 + host._rng.Next(3), _ => -1 };
                if (more >= 0)
                {
                    Start(o, more);
                    Log($"p1heat {Name(o)} 불붙은 지 {since} 틱 → 동작 {more}");
                }
                break;
        }
    }

    /// <summary>물체가 알림을 받는 칸 — 지금 장의 발자국 가운데 지형 비트 0x10(그림만 걸친 칸)이 아닌 칸(<c>0x1008f4c6</c>). 부서진 통(장 3)은 모두 0x10 이라 더 안 맞는다.</summary>
    private static bool Touches(Obj o, Func<int, int, bool> inArea)
    {
        var f = o.Def.Frames[o.Frame];
        for (int r = 0; r < f.Rows; r++)
            for (int c = 0; c < f.Cols; c++)
                if ((f.Terrain[r * f.Cols + c] & 0x10) == 0 && inArea(o.Col + c, o.Row + r)) return true;
        return false;
    }

    /// <summary>
    /// 범위 알림 — 그 칸에서 맨해튼 <paramref name="reach"/> 안의 유닛(<c>0x1008f7c0</c>)과 물체(<c>0x1008f390</c>, 모양 3 — 값 c 는 칸마다 4 라 c/4 칸)에 알림을 보낸다.
    /// 칸에 남은 기운(땅불 · 얼음 덩이, <see cref="Lingering"/>)도 물체 목록에 있어 함께 받는다 — 남이 보낸 알림 10 · 11 · 14 면 꺼진다
    /// (땅불 vt+0x48 <c>0x1007c9d0</c> · 얼음 덩이 <c>0x1007c5b0</c>: 보낸 이가 자기면 거르고, 아니면 +0x14a = 1). 높이 거르기(인자 a3)는 안 옮겼다 [H].
    /// </summary>
    private void Send(Obj? from, int col, int row, int reach, int notice, Hit hit, bool self, Lingering? source = null)
    {
        bool Near(int c, int r) => Math.Abs(c - col) + Math.Abs(r - row) <= reach;
        foreach (var u in host._units)
            if (u is { Alive: true, OnField: true } && Near(u.Col, u.Row)) UnitNotice(u, notice, hit, from, source);
        foreach (var o in _objects)
            if (o != null && (self || o != from) && Touches(o, Near)) Notify(o, notice, hit);
        if (notice is not (10 or 11 or 14)) return;
        foreach (var l in _lingering)
            if (l != source && !l.Out && Near(l.Col, l.Row))
            {
                l.Out = true;
                Log($"p1temp {LingerName(l)} ← 알림 {notice}{(from != null ? $" ({Name(from)})" : source != null ? $" ({LingerName(source)})" : "")}: 꺼짐 (틱 {host.Btl._tick})");
            }
    }

    /// <summary>
    /// 키 6(<c>0x10098d53</c>) — 기준 칸(물체 + 키 10)에서 모양 a1(3 마름모) · 크기 a2(4분의 1칸) 안의 유닛과 다른 물체에 알림 a0 을, a0 이 1 이면
    /// 물체의 공격 서술(<c>0x1009ab20</c> — 식 7 · 세기 = 성질 칸 1² · 속성 칸 3)을 붙여 보낸다. 통 245 는 부서질 때 틱 4 에 알림 1 로 곁 2 칸을 치고(터짐)
    /// 틱 12 에 알림 10 으로 곁 3 칸을 덥히고, 타는 나무는 동작 머리마다 알림 10 으로 곁 3 칸을 덥힌다.
    /// </summary>
    private void Broadcast(Obj o, Part1MapObjectKey k)
    {
        int notice = k.Args[0];
        var hit = notice == 1 ? new Hit(o.Def.BlastPower, o.Def.BlastElement) : default;
        Log($"p1heat {Name(o)} 키 6 알림 {notice} 반경 {k.Args[2] / 4}{(notice == 1 ? $" 세기 {hit.Power}" : "")}");
        Send(o, o.Col + o.AnchorX, o.Row + o.AnchorY, k.Args[2] / 4, notice, hit, self: false);
    }

    // ── 칸에 남는 기운 — 화이어 필드 · 화이어 볼의 땅불(0x1007b970) · 프로즌 아이스의 얼음 덩이(0x1007c1b0) ─────────────────────
    // 원본(분석-파트1-기술-b 9.3 · 9.4 · 9.7): 두 기술 모두 타격 사건이 없다. 화이어 필드는 범위의 칸마다 땅불을 rand%20+30 시간 틱,
    // 화이어 볼은 대상이 없을 때 공이 떨어진 칸에 rand%10+20 남긴다. 프로즌 아이스는 칸마다 얼음 덩이를 n = 30·(R+1) − 30·⌊거리/40⌋ 시간 틱
    // (R = 범위의 가장 먼 맨해튼 칸, 거리 = 겨눈 칸 가운데에서 그 칸 가운데까지의 픽셀 — 0x1004c344~). 둘 다 바탕 0x1007b720: 나이 1 에 시간 틱을 적고
    // 그 틱 + 길이에 끝 표지(+0x14a, 0x1007b940). 첫 장과 시간 틱마다(vt+0x54 — 땅불 0x1007bd90 · 얼음 0x1007c490) 끝 검사 뒤 제 칸의 유닛 · 물체에
    // 땅불은 알림 10(열), 얼음은 알림 11(냉기)을 보낸다. 남이 보낸 10 · 11 · 14 를 받으면 꺼진다(위 Send). 땅불 그림은 기술 쪽이 그리고,
    // 얼음 덩이는 기술이 넘겨준 뒤 여기서 그린다(Obs 118 r+3 되풀이 → 끝나면 r+6 녹기). 덩이의 떨림(0x1007c3a0, ±1 px)은 안 옮겼다.

    private sealed class Lingering(int col, int row, int start, int end, int notice, int motion)
    {
        public readonly int Col = col, Row = row, Start = start, End = end, Notice = notice, Motion = motion;
        public bool Out;                 // +0x14a — 끝 표지. 다음 틱에 치운다
        public double DrawFrom, EndedAt = -1;
    }

    private readonly List<Lingering> _lingering = [];
    private readonly List<Lingering> _melting = [];

    private static string LingerName(Lingering l) => $"{(l.Notice == 10 ? "땅불" : "얼음")} ({l.Col},{l.Row})";

    /// <summary>칸에 불을 놓는다 — 놓자마자 한 번 알림 10(<c>0x1007bc39</c>).</summary>
    internal void AddFire(int col, int row, int life) => Linger(col, row, life, 10, -1);

    /// <summary>프로즌 아이스가 칸에 얼음 덩이를 남긴다(<c>0x1004c339</c>) — 놓자마자 한 번 알림 11. <paramref name="motion"/> 은 Obs 118 의 r(0~2).</summary>
    internal void AddIce(int col, int row, int motion, int life)
    {
        Ensure();
        Linger(col, row, life, 11, motion);
    }

    private void Linger(int col, int row, int life, int notice, int motion)
    {
        int tick = host.Btl._tick;
        var l = new Lingering(col, row, tick, tick + life, notice, motion) { DrawFrom = host._lastTime };
        _lingering.Add(l);
        Log($"p1temp {LingerName(l)} 놓음: 길이 {life} 시간 틱 (틱 {tick} → {tick + life})");
        Send(null, col, row, 0, notice, default, self: true, source: l);
    }

    /// <summary>그 칸에 타는 땅불이 있나(<c>0x1008fc20</c> — 칸의 기운 가운데 화이어 필드 78~86 · 화이어 볼 69~77 의 것).</summary>
    internal bool FireAt(int col, int row) => _lingering.Any(l => l is { Notice: 10, Out: false } && l.Col == col && l.Row == row);

    /// <summary>그 칸에 얼음 덩이가 있나(<c>0x1008fcc0</c> — 프로즌 아이스 115~123 의 것).</summary>
    internal bool IceAt(int col, int row) => _lingering.Any(l => l is { Notice: 11, Out: false } && l.Col == col && l.Row == row);

    /// <summary>얼음 덩이를 그린다 — 물체와 함께, 유닛보다 먼저(덩이는 칸 바닥 높이, 유닛은 그 위 +19 라 원본 깊이로도 유닛이 앞이다).</summary>
    private void DrawLingering()
    {
        int id = Part1Game.ObsBase + 118;
        if (host.UiFor(id) is not { } sprite) return;
        foreach (var l in _lingering)
        {
            if (l.Notice != 11) continue;
            int tick = (int)((host._lastTime - l.DrawFrom) * TicksPerSecond);
            int key = sprite.BlendAt(l.Motion + 3, tick % Math.Max(1, sprite.MotionLength(l.Motion + 3)));
            host.DrawUi(id, l.Motion + 3, tick, l.Col * TileW + TileW / 2, host.CellCenterY(l.Col, l.Row), BlendOf(key), loop: true, fade: BlendFade(key));
        }
        _melting.RemoveAll(l =>
        {
            int age = (int)((host._lastTime - l.EndedAt) * TicksPerSecond);
            if (age >= Math.Max(1, sprite.MotionLength(l.Motion + 6))) return true;
            int key = sprite.BlendAt(l.Motion + 6, age);
            host.DrawUi(id, l.Motion + 6, age, l.Col * TileW + TileW / 2, host.CellCenterY(l.Col, l.Row), BlendOf(key), loop: false, fade: BlendFade(key));
            return false;
        });
    }

    /// <summary>
    /// 맞는 기술이 맵 칸도 칠 때(<c>Attack +0x41</c> = 1, <c>0x10076c46</c>) — 범위의 물체에 알림 1 과 공격 서술을 보낸다. 부르는 곳은 파트1 기술이 끝난 뒤(BattleScene.UseWorkRoutine).
    /// 원본은 핸들러마다 타격 사건의 칸이 다르다: 화이어 볼(69~77)은 공마다 한 칸(대상이 있으면 대상의 칸, 없으면 범위 안 아무 칸 + 땅불), 화이어 필드(78~86)는 타격이 없고 칸마다 불,
    /// 프로즌 아이스(115~123)는 타격이 없다(얼음 덩이는 기술이 <see cref="AddIce"/> 로 넘긴다), 그 밖(검기 · 총 · 메테오 …)은 리메이크가 범위 칸 전체로 본다 [H: 핸들러별 타격 칸은 안 옮겼다].
    /// </summary>
    internal void HitByAttack(UnitState a, Part1Attack attack, IReadOnlyList<(int Col, int Row)> cells, IReadOnlyList<int> targets)
    {
        Ensure();
        if (attack.Id is >= 115 and <= 123) return;
        if (cells.Count == 0 || _objects.Count == 0 && attack.Id is not (>= 69 and <= 86)) return;
        var rules = host.Btl.Part1;
        var fa = rules.Fighter(a);
        int power = fa is null ? 0 : Part1Combat.Power(attack.Formula, fa.AttackerAt(a.Soul)) + attack.PowerAdd * attack.PowerAdd;
        int element = attack.Element > 0 ? attack.Element : rules.Enchant.GetValueOrDefault(a) is > 0 and var e ? e : fa?.Weapon1?.Element ?? 0;
        var hit = new Hit(power, element);
        Log($"p1heat 기술 {attack.Id} {attack.Name} by {a.ChrCode}: 세기 {power} 속성 {element} 범위 {cells.Count} 칸 대상 {targets.Count}");
        if (attack.Id is >= 78 and <= 86)
        {
            foreach (var (c, r) in cells) AddFire(c, r, 30 + host._rng.Next(20));
            return;
        }
        if (attack.Id is >= 69 and <= 77)
        {
            for (int k = 0; k < Math.Max(1, attack.Hits); k++)
            {
                var (c, r) = targets.Count > 0 && host._units[targets[host._rng.Next(targets.Count)]] is var t ? (t.Col, t.Row) : cells[host._rng.Next(cells.Count)];
                Send(null, c, r, 0, 1, hit, self: true);
                if (targets.Count == 0) AddFire(c, r, 20 + host._rng.Next(10));
            }
            return;
        }
        var area = new HashSet<(int, int)>(cells);
        foreach (var o in _objects)
            if (o != null && Touches(o, (c, r) => area.Contains((c, r)))) Notify(o, 1, hit);
    }

    /// <summary>
    /// 시간 틱(BattleScene.AdvanceTick — 원본 <c>0x10030086</c>: 유닛 vt+0x54 다음 물체 vt+0x54). 유닛 온도(<see cref="StepUnitHeat"/>) →
    /// 물체마다 <c>0x10097af0</c>: 온도를 기온 쪽으로 2(<c>0x10099f60</c>), 타는 물체(성질 칸 6)가 동작 20~22 · 26~34 이면 기준 칸 둘레 1 칸(모양 3 · 7)의
    /// 유닛과 물체(<b>제 몸도</b> — 그래서 혼자서도 200 까지 달아 끝까지 탄다) · 칸의 기운에 알림 10 → 칸의 기운마다 끝 검사(<c>0x1007b940</c> — 다 됐으면 끝 표지를 세우되
    /// 이번 틱은 그래도 보낸다) 뒤 제 칸에 알림 10 · 11. 끝 표지가 선 기운은 치운다(얼음은 녹는 그림 r+6).
    /// </summary>
    internal void TimeStep()
    {
        Ensure();
        StepUnitHeat();
        int ambient = Ambient;
        foreach (var o in _objects)
        {
            if (o == null) continue;
            o.Heat += Math.Sign(ambient - o.Heat) * Math.Min(2, Math.Abs(ambient - o.Heat));
            if (o.Def.Burns && o.Motion is (>= 20 and <= 22) or (>= 26 and <= 34))
                Send(o, o.Col + o.AnchorX, o.Row + o.AnchorY, 1, 10, default, self: true);
        }
        int tick = host.Btl._tick;
        foreach (var l in _lingering.ToList())
        {
            if (l.Out) continue;
            if (tick >= l.End)
            {
                l.Out = true;
                Log($"p1temp {LingerName(l)} 다 탐/녹음 (틱 {tick}, 놓은 틱 {l.Start})");
            }
            Send(null, l.Col, l.Row, 0, l.Notice, default, self: true, source: l);
        }
        foreach (var l in _lingering.Where(l => l.Out))
            if (l.Notice == 11) { l.EndedAt = host._lastTime; _melting.Add(l); }
        _lingering.RemoveAll(l => l.Out);
    }

    // ── 유닛 온도(+0x4a) ─────────────────────────────────────────────────────────────────────────────────
    // 원본 [C]: 유닛을 만들 때 80(0x10034871 — 기온과 상관없이). 유닛 시간 틱 0x10035150:
    //  ① 기계(+0xb8 = 2)가 아니면 기온(머리 낱말 6 +0xb96e) 쪽으로 2(같은 비교를 두 번). 전장 지형(낱말 7, 1 사막 ~ 6 바다)의 적응 어빌리티
    //     (86 사막 · 87 정글 · 88 동굴 · 89 산지 · 90 극지 · 91 바다 적응, 0x10042930)를 가졌으면 기온 대신 80 쪽으로.
    //  ② 160 을 넘으면 세기 식 9((온도 − 160)·20/40 + 1) · 속성 1(화염) · 명중 100 의 서술로 스스로 맞고(0x10039e60(0x3f2) — 맞음 길 그대로),
    //     10% 로 대상 거르기 25 를 지나면 컨디션이상(15, 값 20 + (온도 − 160)/2, 0x1003528d).
    //  ③ 그렇지 않고 0 아래면 SPD(0x1003ece0 — 음수 온도만큼 깎인다)가 4 아래일 때 상태 1(+0x247, 동결)을 건다: 표지 0x10041c60(1)(Obs 101 동작 3 → 4,
    //     풀리면 5 — 0x10083ae0), 서기 동작에 몸짓을 멈춘다(+0x26 = −1). 걸린 채 SPD 가 4 이상이면 푼다(0x10042700(1), +0x26 = 0). 온도가 0 이상으로
    //     뛰면 ③ 을 안 지나 풀리지 않는다(원본 그대로). 상태 1 은 차례를 못 받게 한다(0x10042871).
    // 알림 10(0x10035537): 기계가 아니면 += ⌊5·X/10⌋(X = 0x1003eee0 = 화염 저항(칸 30 + 보정 208) −10~20 + 빙한 부여(+0x24a)면 5), 200 에서 자름.
    // 알림 11(0x10035580): 기계 · 큰 유닛(+0xb9)이 아니면 −= ⌊5·Y/10⌋(Y = 0x1003ef00 = 빙한 저항(칸 31 + 보정 209) −10~20 + 화염 부여(+0x249)면 5), −60 에서 자름.
    // 온도는 저장에 안 싣는다(상태 · 강화처럼).

    /// <summary>유닛을 만들 때의 온도(<c>0x10034871</c>).</summary>
    internal const int UnitStartHeat = 80;

    /// <summary>시험 훅 DUELDX_P1HEAT=&lt;온도&gt; — 판의 유닛이 80 대신 이 온도로 시작한다(동결 · 불 피해를 화면 밖에서 빨리 보려고). 지형 적응의 목표 80 은 그대로.</summary>
    private static readonly int? HeatHook = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_P1HEAT"), out int hh) ? hh : null;

    private readonly Dictionary<UnitState, int> _unitHeat = [];

    /// <summary>유닛 온도(+0x4a) — 아직 바뀐 적 없으면 80.</summary>
    internal int UnitHeat(UnitState u) => _unitHeat.TryGetValue(u, out int h) ? h : HeatHook ?? UnitStartHeat;

    private void UnitNotice(UnitState u, int notice, Hit hit, Obj? from, Lingering? source)
    {
        var rules = host.Btl.Part1;
        if (notice == 1)
        {
            ObjectHit(u, 7, hit.Power, hit.Element, from is null ? "물체" : Name(from));
            return;
        }
        if (notice is not (10 or 11) || rules.IsMachine(u) || rules.Fighter(u) is not { } f) return;
        int before = UnitHeat(u), after;
        if (notice == 10)
        {
            int x = Math.Clamp(f.Resist(1) ?? 10, -10, 20) + (rules.Enchant.GetValueOrDefault(u) == 2 ? 5 : 0);
            after = Math.Min(MaxHeat, before + 5 * x / 10);
        }
        else
        {
            if (f.Chr.Fields[Part1Chr.BigField] != 0) return;
            int y = Math.Clamp(f.Resist(2) ?? 10, -10, 20) + (rules.Enchant.GetValueOrDefault(u) == 1 ? 5 : 0);
            after = Math.Max(MinHeat, before - 5 * y / 10);
        }
        _unitHeat[u] = after;
        string by = from != null ? $" ← {Name(from)}" : source != null ? $" ← {LingerName(source)}" : "";
        Log($"p1temp unit {Array.IndexOf(host._units, u)} chr {u.ChrCode} ({u.Col},{u.Row}) 알림 {notice}{by}: 온도 {before} → {after} (틱 {host.Btl._tick})");
    }

    private void StepUnitHeat()
    {
        var btl = host.Btl;
        var rules = btl.Part1;
        int ambient = Ambient;
        foreach (var u in host._units)
        {
            if (!u.Alive || !u.OnField) continue;
            int before = UnitHeat(u), h = before;
            if (!rules.IsMachine(u))
            {
                bool adapted = btl.Part1Adapted(u);
                int goal = adapted ? UnitStartHeat : ambient;
                h += Math.Sign(goal - h) * Math.Min(2, Math.Abs(goal - h));
                if (h != before)
                {
                    _unitHeat[u] = h;
                    Log($"p1temp unit {Array.IndexOf(host._units, u)} chr {u.ChrCode} 틱 {btl._tick}: 온도 {before} → {h} ({(adapted ? "적응 — 80" : $"기온 {ambient}")} 쪽)");
                }
            }
            if (h > IgniteHeat)
            {
                if (rules.Fighter(u) is not { } f) continue;
                int power = Part1Combat.Power(9, f.AttackerAt(u.Soul) with { Temperature = h });
                ObjectHit(u, 9, power, 1, $"온도 {h}");
                if (u.Hp > 0 && host._rng.Next(10) == 0 && rules.TargetFilter(25, u) && rules.AddAilment(u, Part1Rules.Condition))
                {
                    Log($"p1temp unit {Array.IndexOf(host._units, u)} chr {u.ChrCode}: 온도 {h} → 컨디션이상");
                    host.Btl.Popup(u, "컨디션이상", BattleScene.AilmentPopupColor, 12, rise: 88);
                    host.StatusScr.RefreshUnitStats(u);
                }
            }
            else if (h < 0) Chill(u, h);
        }
    }

    /// <summary>0 아래 온도의 동결 검사(<c>0x100352bc</c>~) — SPD &lt; 4 면 상태 1 을 걸고, 걸린 채 4 이상이면 푼다.</summary>
    private void Chill(UnitState u, int h)
    {
        var btl = host.Btl;
        var rules = btl.Part1;
        int spd = btl.Part1Spd(u);
        bool frozen = rules.HasAilment(u, Part1Rules.Frozen);
        if (frozen == spd < 4)
        {
            Log($"p1temp unit {Array.IndexOf(host._units, u)} chr {u.ChrCode}: 온도 {h} → SPD {spd} (온도 빼기 전 {btl.Part1Spd(u, heat: false)}){(frozen ? " · 동결 중" : "")}");
            return;
        }
        if (!frozen)
        {
            rules.AddAilment(u, Part1Rules.Frozen);
            u.AnimFrozen = true;
            _iceMarks[u] = (host._lastTime, -1);
        }
        else
        {
            rules.ClearAilment(u, Part1Rules.Frozen);
            u.AnimFrozen = false;
            if (_iceMarks.TryGetValue(u, out var m)) _iceMarks[u] = (m.Start, host._lastTime);
        }
        Log($"p1temp unit {Array.IndexOf(host._units, u)} chr {u.ChrCode}: 온도 {h} · SPD {spd} → {(frozen ? "동결 풀림" : "동결(상태 1)")} (틱 {btl._tick})");
        host.StatusScr.RefreshUnitStats(u);
    }

    /// <summary>동결 표지(Obs 101, 클래스 <c>0x10083aa0</c>) — 건 때 · 풀린 때(−1 이면 아직 걸림).</summary>
    private readonly Dictionary<UnitState, (double Start, double Thaw)> _iceMarks = [];

    /// <summary>
    /// 동결 표지를 그린다 — 유닛 발치에 Obs 101 동작 3(얼어붙기)을 한 번, 그다음 4 를 되풀이, 풀리면 5(녹기)를 한 번 틀고 지운다(<c>0x10083ae0</c>).
    /// 유닛 위에(머리 표지와 함께) 그린다.
    /// </summary>
    internal void DrawIceMarks()
    {
        if (_iceMarks.Count == 0) return;
        int id = Part1Game.ObsBase + 101;
        var sprite = host.UiFor(id);
        foreach (var u in _iceMarks.Keys.ToList())
        {
            var (start, thaw) = _iceMarks[u];
            if (sprite == null || !u.Alive || !u.OnField || Array.IndexOf(host._units, u) < 0) { _iceMarks.Remove(u); continue; }
            var (fx, fy) = host.Btl.UnitFoot(u);
            int motion, age;
            bool loop = false;
            if (thaw >= 0)
            {
                (motion, age) = (5, (int)((host._lastTime - thaw) * TicksPerSecond));
                if (age >= Math.Max(1, sprite.MotionLength(5))) { _iceMarks.Remove(u); continue; }
            }
            else
            {
                age = (int)((host._lastTime - start) * TicksPerSecond);
                int first = Math.Max(1, sprite.MotionLength(3));
                if (age < first) motion = 3;
                else { (motion, age, loop) = (4, age - first, true); }
            }
            int key = sprite.BlendAt(motion, loop ? age % Math.Max(1, sprite.MotionLength(motion)) : age);
            host.DrawUi(id, motion, age, fx, fy, BlendOf(key), loop: loop, fade: BlendFade(key));
        }
    }

    /// <summary>
    /// 온도 물들이기(유닛 그리기 <c>0x10034a07</c>~ — 맞을 때의 물들이기 +0x4c 가 없을 때): 0 아래면 방식 29(푸른빛) · 세기 −온도·32 ÷ SPD 바탕(+0xcd, 인물 칸 25),
    /// 160 을 넘으면 방식 28(주황빛) · 세기 (온도 − 160)·32 ÷ 40. 세기는 바이트로 담아(아래 8비트) 31 에서 자른다. 그 밖은 null.
    /// </summary>
    internal Part1Blend.Plan? TemperatureTint(UnitState u)
    {
        int h = UnitHeat(u);
        if (h >= 0 && h <= IgniteHeat) return null;
        int level;
        int mode;
        if (h < 0)
        {
            int spd = host.Btl.Part1.Fighter(u)?.Chr.Fields[Part1Chr.Spd] ?? 0;
            (mode, level) = (29, -h * 32 / Math.Max(1, spd));
        }
        else (mode, level) = (28, (h - IgniteHeat) * 32 / 40);
        level &= 0xff;
        return Part1Blend.PlanOf(Part1Game.BlendKey(mode, Math.Min(31, level)));
    }

    private bool _killsPending;

    /// <summary>
    /// 물체 · 온도가 유닛을 친다 — 서술(세기 식, 세기, 속성)을 원본 피해 길(<c>0x100413b0</c>)처럼: 식 7 · 9 는 회피 · 막기를 안 굴리고 명중 100.
    /// 식 7 은 방어 AC(버써크 ½ · 쉐도우 실드 ×1.5) · 속성 저항으로, <b>식 9(온도)는 방어 · 저항 칸을 다 건너뛰고</b> 세기 × X / 10
    /// (X = <c>0x1003eee0</c> = 화염 저항 −10~20 + 빙한 부여 5, <c>0x100414a2</c>)만 — 그 뒤 둘 다 ±1/6 · 배리어 ×2(<c>0x100416fb</c>~).
    /// 피해 숫자 · 맞기 몸짓은 기술 피해와 같은 꼴, 0 이면 아무것도 안 띄운다.
    /// </summary>
    private void ObjectHit(UnitState t, int selector, int power, int element, string why)
    {
        var btl = host.Btl;
        var rules = btl.Part1;
        if (t.Hp <= 0 || rules.Fighter(t) is not { } ft || host._db is not { } db) return;
        int defense = ft.Stat(Part1Chr.Ac, t.Soul);
        if (t.Status(4) > 0) defense /= 2;
        if (rules.Has(t, 22)) defense = defense * 3 / 2;
        int damage;
        if (selector == 9)
        {
            int x = Math.Clamp(ft.Resist(1) ?? 10, -10, 20) + (rules.Enchant.GetValueOrDefault(t) == 2 ? 5 : 0);
            (power, defense) = (x * power / 10, 0);
            damage = Part1Combat.Damage(power, 0, null, null, host._rng.Next(32768));
        }
        else damage = Part1Combat.Damage(power, defense, ft.Resist(element), null, host._rng.Next(32768));
        if (rules.Has(t, 20)) damage *= 2;
        damage = Math.Max(0, damage);
        Log($"p1heat hit unit {Array.IndexOf(host._units, t)} chr {t.ChrCode} ({t.Col},{t.Row}) ← {why}: 식 {selector} 세기 {power} 속성 {element} ÷ AC {defense} = {damage} (HP {t.Hp})");
        if (damage == 0) return;
        t.Hp = Math.Max(0, t.Hp - damage);
        btl.ShowNumber(t, $"{db.T(159)} {damage}", BattleScene.DamageColor);
        btl.PlayHitReaction(t, damaged: true, spark: false);
        btl.PlayHurtCry(t);
        if (t.Hp <= 0) _killsPending = true;
    }

    /// <summary>물체 · 온도가 쓰러뜨린 유닛 — 행동 중이 아니고 사건도 없을 때 치운다(기술 중이면 그 기술 끝이 치운다).</summary>
    private void SweepKills()
    {
        if (!_killsPending || host.Btl._routine != null || host.Btl.EventsBusy) return;
        _killsPending = false;
        host.Btl.SweepTickDeaths();
    }

    // ── 그리기 깊이 ──────────────────────────────────────────────────────────────────────────────────────
    // 원본(분석-파트1-전투 36.3): 물체 그리기 0x10097390 이 띠마다 깊이 (줄·40 + 물체 세계 y, 높이·20 + 물체 세계 z) 로 그리기 목록에 넣고(0x1001bc60),
    // 유닛은 (세계 y — 걷는 중이면 칸 사이, 세계 z + 19) 로 넣는다(0x100349b6 · vt+0x70 0x10097f70). 목록은 (y, z, 넣은 차례) 로 늘어놓아
    // 뒤에서부터 그린다(0x1001ad10 → 0x1001ae70). 리메이크: 물체 그림을 같은 깊이 층(Part1MapObjectLayer)으로 미리 나눠 두고,
    // DrawObjects 때 그림자(z < 0 자식)만 바로 그리고 나머지 층·자식은 깊이 차례로 모아 둔 뒤 DrawUnits 가 유닛 사이사이에 풀어 그린다.
    // 열쇠는 세계 단위(칸 40 · 높이 한 단 20) — y · 1,000,000 + z.

    private readonly record struct Pending(long Key, int Seq, uint[]? Pixels, int W, int H, int X, int Y, Part1MapObjectKey? Child, int Age);

    private static readonly (int Col, int Row)? CamHold =
        Environment.GetEnvironmentVariable("DUELDX_P1CAM")?.Split(',') is [var hc, var hr] && int.TryParse(hc, out int c) && int.TryParse(hr, out int r) ? (c, r) : null;

    private readonly List<Pending> _pending = [];
    private int _drawn;
    // 같은 깊이면 넣은 차례(원본 +0x34) — List.Sort 는 차례를 안 지키므로 차례 번호를 함께 본다.
    private static readonly Comparison<Pending> ByKey = (a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Seq.CompareTo(b.Seq);

    /// <summary>깊이 열쇠(세계 y, 세계 z) — y 가 먼저, 같으면 z.</summary>
    internal static long DepthKey(double worldY, int worldZ) => (long)Math.Floor(worldY) * 1_000_000L + worldZ;

    /// <summary>유닛의 깊이 열쇠 — 세계 (40·줄, 20·높이 + 19)(<c>0x100349b6</c>). 높이는 물체까지 찍은 판 높이(통 · 상자 위에 서면 그만큼 높다).</summary>
    internal static long UnitKey(double row, double height) => DepthKey(row * 40, (int)Math.Round(height * 20) + 19);

    /// <summary>
    /// 물체를 준비한다 — 키를 돌리고, 그림자(z &lt; 0 자식 — 땅에 깔린다)는 바로 그리고, 장의 깊이 층과 나머지 자식(불꽃)은 깊이 차례로 모아 둔다.
    /// 모은 것은 <see cref="DrawUntil"/>(유닛 사이) · <see cref="Flush"/>(유닛 다 그린 뒤)가 그린다.
    /// </summary>
    internal void Draw()
    {
        Ensure();
        // 시험 훅 DUELDX_P1CAM=열,줄 — 카메라를 그 칸에 묶어 둔다(화면 밖 시험에서 자동 진행 중에도 타는 물체를 계속 찍으려고).
        if (CamHold is var (holdCol, holdRow)) host.Btl.CenterNow(holdCol * TileW + TileW / 2, host.CellCenterY(holdCol, holdRow));
        _pending.Clear();
        _drawn = 0;
        DrawLingering();
        int viewX0 = host._camX, viewX1 = host._camX + host.ViewWidth, viewY0 = host._camY, viewY1 = host._camY + host.ViewHeight;
        foreach (var o in _objects)
        {
            if (o == null) continue;
            Step(o);
            var f = o.Def.Frames[o.Frame];
            int ox = o.Col * TileW, oy = GridTop + host.BoardPad + o.Row * TileH - o.Z * HeightStep;
            DrawChildren(o, ox, oy, under: true);
            // 화면 밖 물체의 층은 모으지 않는다 — 넓은 판(물체 100여 개)에서 정렬할 것을 줄인다.
            bool onScreen = f.Width > 0 && ox + f.X < viewX1 && ox + f.X + f.Width > viewX0 && oy + f.Y < viewY1 && oy + f.Y + f.Height > viewY0;
            if (onScreen)
                foreach (var l in f.Layers)
                    _pending.Add(new(DepthKey(40 * (o.Row + l.Row), 20 * (o.Z + l.Level)), _pending.Count, l.Pixels, l.Width, l.Height, ox + l.X, oy + l.Y, null, 0));
            if (o.Def.Motion(o.Motion) is not { } m) continue;
            int tick = TickOf(o), cycle = m.Length > 0 ? tick % m.Length : tick;
            foreach (var k in m.Keys)
                if (k.Kind == 2 && k.Start <= cycle && k.Args[4] >= 0)
                    // 자식은 제 세계 자리(물체 + (a2, a3, a4))의 깊이 — 그림(Obs) 개체라 z + 19(0x100850f2 와 같은 꼴).
                    _pending.Add(new(DepthKey(40 * o.Row + k.Args[3], 20 * o.Z + k.Args[4] + 19), _pending.Count, null, 0, 0, ox, oy, k, cycle - k.Start));
        }
        _pending.Sort(ByKey);
    }

    /// <summary>깊이 열쇠가 <paramref name="key"/> 보다 뒤인 층 · 자식을 그린다 — 유닛 하나를 그리기 바로 앞에.</summary>
    internal void DrawUntil(long key)
    {
        for (; _drawn < _pending.Count && _pending[_drawn].Key < key; _drawn++) DrawPending(_pending[_drawn]);
    }

    /// <summary>남은 층 · 자식을 다 그린다 — 유닛을 다 그린 뒤.</summary>
    internal void Flush()
    {
        DrawUntil(long.MaxValue);
        _pending.Clear();
        _drawn = 0;
    }

    private void DrawPending(in Pending p)
    {
        if (p.Pixels != null) { host.BlitMasked(p.Pixels, p.W, p.H, p.X, p.Y); return; }
        if (p.Child is { } k) DrawChild(k, p.Age, p.X, p.Y);
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
            if (k.Kind == 2 && k.Start <= cycle && (k.Args[4] < 0) == under) DrawChild(k, cycle - k.Start, ox, oy);
    }

    private void DrawChild(Part1MapObjectKey k, int age, int ox, int oy)
    {
        int obs = Part1Game.ObsBase + k.Args[0], motion = k.Args[1];
        if (host.UiFor(obs) is not { } sprite) return;
        int life = sprite.ShownLength(motion);
        if (life > 0 && age >= life) return;
        int key = sprite.BlendAt(motion, age);
        host.DrawUi(obs, motion, age, ox + k.Args[2], oy + k.Args[3] * 32 / 40 - k.Args[4] * 12 / 20, BlendOf(key), loop: false, fade: BlendFade(key));
    }

    private const int SaveFields = 10, OldSaveFields = 6;

    /// <summary>
    /// 저장 — 물체마다 (동작, 장, 찍힌 장, 높이, 기준 x, 기준 y, 내구, 온도, 상태, 불붙은 지 몇 시간 틱). 못 읽은 물체는 −1 열.
    /// 칸의 불(<see cref="AddFire"/>) · 유닛 온도는 싣지 않는다 — 불러오면 꺼지고 기온으로 돌아간다.
    /// </summary>
    internal int[] Save()
    {
        Ensure();
        return [.. _objects.SelectMany(o => o is null ? Enumerable.Repeat(-1, SaveFields)
            : [o.Motion, o.Frame, o.StampFrame, o.Z, o.AnchorX, o.AnchorY, o.Durability, o.Heat, o.State, host.Btl._tick - o.StateAt])];
    }

    /// <summary>되살리기 — 동작은 지금부터 다시 돈다(틱 0 키는 이미 돈 것으로). 칸 수가 안 맞으면(다른 판) 그대로 둔다. 예전 저장(물체마다 여섯 칸)도 읽는다.</summary>
    internal void Restore(int[]? saved)
    {
        Ensure();
        if (saved == null) return;
        int fields = saved.Length == _objects.Count * SaveFields ? SaveFields : saved.Length == _objects.Count * OldSaveFields ? OldSaveFields : 0;
        if (fields == 0) return;
        for (int i = 0; i < _objects.Count; i++)
        {
            int at = i * fields;
            if (_objects[i] is not { } o || saved[at] < 0) continue;
            o.Motion = saved[at];
            o.Frame = Math.Clamp(saved[at + 1], 0, o.Def.Frames.Count - 1);
            o.StampFrame = Math.Clamp(saved[at + 2], 0, o.Def.Frames.Count - 1);
            (o.Z, o.AnchorX, o.AnchorY) = (saved[at + 3], saved[at + 4], saved[at + 5]);
            if (fields == SaveFields)
            {
                (o.Durability, o.Heat, o.State, o.StateAt) = (saved[at + 6], saved[at + 7], saved[at + 8], host.Btl._tick - saved[at + 9]);
                o.Busy = o.State is 0 or 1 or 3 or 4 or 5 || (o.State == 2 && o.Motion is 26 or 28 or 30);
            }
            o.MotionAt = host._lastTime;
            o.DoneTick = 0;
        }
        _zMode = ZMode.Keep;
        host.Btl.RestampObjects();
    }
}
