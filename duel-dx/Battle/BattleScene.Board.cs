using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>전투판 — 끝난 전투에서 나가기, 키로 걷기, 시험 훅, 유닛·격자 그리기.</summary>
internal sealed unsafe partial class BattleScene
{
    /// <summary>
    /// 전투가 끝나고 배너가 떠 있을 때 — 이기고 이어지는 전투가 있으면 그 전투로(이벤트 행동 10),
    /// 없거나 졌으면 모세스 화면으로 간다(mo-1). 키든 클릭이든 한 번이면 넘어간다(원본도 배너를 눌러 건너뛴다, 분석-전투).
    /// </summary>
    internal void LeaveFinishedBattle()
    {
        // 원본은 상태 24 가 끝나면 루프를 빠져나와 16틀 동안 화면을 검게, 음악을 100→10% 로 줄인 뒤 장면을 지운다
        // (0x10061d91~0x10061ea9, 감사4 C7·사운드 B3). 이미 페이드 중이면 그대로 둔다 — 끝나면 StepSceneFade 가 넘긴다.
        if (host._fadeOutStart >= 0) return;
        host._fadeInStart = -1;
        host._fadeOutStart = host._lastTime;
        host._afterFadeOut = null;
        host._fadeOutKeepMusic = false;
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"{host._lastTime:F2} battle fade-out ({(host._lastTime - _outcomeAt) * TicksPerSecond:F0} ticks after outcome '{_outcome}')" + Environment.NewLine);
    }

    /// <summary>페이드아웃이 끝난 뒤 — 결과대로 다음 장면을 연다.</summary>
    internal void LeaveFinishedBattleNow()
    {
        // 결과와 행선지는 <b>한 번만</b> 쓴다 — 전에는 남아 있어서, 연대표(모세스가 아님)에서 에피소드를 누르면
        // 그 클릭이 다시 「배너 넘기기」가 되어 Btl 0137 의 끝 필드 55 가 또 열렸다(사용자 보고).
        bool won = _outcome.StartsWith('승');
        int nextField = _eventNextField;
        // 시험 전용: 결과와 행선지를 남긴다(DUELDX_TESTRUN 일 때만).
        TestRunTrace($"leave btl {host._scene.Id} won {won} outcome '{_outcome}' nextBattle {_eventNextBattle} nextField {nextField} sceneNext {host._scene.NextBattle} episodes {host.EpisodesScr.Episodes().Count} turnNo {_turnNo} t {host._lastTime:F1}");
        _outcome = "";
        _outcomeQuiet = false;
        _eventNextField = 0;
        // 상태이상은 그 전투에서만 간다 — 판에 남은 유닛에 붙어 있으면 모세스 스테이터스 창에 그대로 보였다(사용자 보고).
        foreach (var u in host._units) u.ClearStatus();
        // 진행 깃발은 <b>실제로 돈</b> 행동 102 만 세운다(RunEventAction) — 이긴 뒤 파일의 102 를 모두 적용하던 것은 안 터진 갈래의 깃발까지 세웠다(fg-21 ⑬).
        // 이어지는 전투는 이벤트 행동 10 이 정한 것뿐이다. Btl 자료의 첫 행동 10 은 챕터 자료가 없는 데모 흐름에서만 쓴다 —
        // 원본은 전멸·행동 11[0] 승리를 챕터(모세스)로 돌린다.
        int next = _eventNextBattle > 0 ? _eventNextBattle : host.EpisodesScr.Episodes().Count == 0 ? host._scene.NextBattle : 0;
        _eventNextBattle = 0;
        if (won && next > 0 && host.Sys.StartBattle(next)) { TestRunTrace($"dest battle {next}"); return; }   // 시험 전용 줄
        // 행동 6 은 전투를 끝내고 그 필드로 보낸다.
        if (won && nextField > 0 && host.Fld.OpenField(nextField)) { TestRunTrace($"dest field {nextField}"); return; }   // 시험 전용 줄
        // 패배(결과 4·2)는 타이틀로 간다(0x10061d04) — 이어 하려면 세이브를 불러온다. 챕터 자료가 없는 데모 흐름만 모세스로.
        if (!won && host.EpisodesScr.Episodes().Count > 0) { TestRunTrace("dest title"); host.TitleScr.OpenTitle(); return; }   // 시험 전용 줄
        TestRunTrace($"dest moses (next {next} field {nextField})");   // 시험 전용 줄
        var navBefore = (Chapter: host.Mos._mosesChp?.Id ?? -1, Step: host.Mos._mosesStep, Planet: host.Mos._mosesPlanet, System: host.Mos._mosesSystem, Visited: host.Mos._mosesNavVisited, Start: host.Mos._navStart);
        host.Mos.OpenMoses();
        // 이기고 돌아오면 원본은 주 화면이 아니라 <b>항행 페이지</b>로 바로 간다(fg-21 ⑰). 챕터가 끝나 연대표로 갔으면 그대로.
        if (won && host.Mos._mosesOpen && host.Mos._mosesChp != null)
        {
            host.Mos.MosesGoPage(0);
            // 떠날 때의 단계·행성·성계 그대로 돌아온다(0x100fcf00(저장 단계), ba-20 G6) — 전에는 늘 챕터 시작 행성·단계로 돌아갔다.
            if (navBefore.Visited && navBefore.Chapter == host.Mos._mosesChp.Id && Equals(navBefore.Start, host.Mos._navStart))   // 스크립트 911 이 자리를 바꿨으면 그쪽이 이긴다
            {
                (host.Mos._mosesStep, host.Mos._mosesPlanet, host.Mos._mosesSystem) = (navBefore.Step, navBefore.Planet, navBefore.System);
                host.Mos.ShowMosesBackground(host.Mos.MosesSystem()?.Background ?? 70);
            }
        }
    }

    /// <summary>
    /// 차례인 아군을 그쪽으로 돌려세우고, 이동 영역(파랑) 안이면 한 칸 걷게 한다(TP 는 행동할 때 한 번에 뺀다). 움직이는 중이면 무시한다.
    /// </summary>
    internal void TryStep(Facing facing, int dx, int dy)
    {
        if (!IsPlayerTurn) return;
        host._selected = _turn;
        var unit = host._units[_turn];
        if (unit.IsBusy) return;

        unit.Facing = facing;
        int col = unit.Col + dx, row = unit.Row + dy;
        if ((uint)col >= host.Cols || (uint)row >= host.Rows || ComputeRange(unit) is not { } range) return;
        int index = row * host.Cols + col;
        if (!range.CanReach(index)) return;   // 이동 영역(차례 시작 자리 기준) 밖

        unit.BeginStep(col, row, host.StepTicksBetween(unit.Col, unit.Row, col, row));
        FollowUnit(_turn);                    // 걷는 동안 따라가기(0x100eac40(8), 감사4 C2)
    }

    /// <summary>누르고 있는 이동 키(누른 순서). 키보드 반복 대신 이걸로 한 칸이 끝나는 즉시 다음 칸을 잇는다.</summary>
    internal readonly List<int> _heldMoveKeys = [];

    internal void StepByKey(int key)
    {
        switch (host.MoveActionFor(key))
        {
            case KeyAction.MoveUp: TryStep(Facing.Up, 0, -1); break;
            case KeyAction.MoveDown: TryStep(Facing.Down, 0, 1); break;
            case KeyAction.MoveLeft: TryStep(Facing.Left, -1, 0); break;
            case KeyAction.MoveRight: TryStep(Facing.Right, 1, 0); break;
        }
    }

    /// <summary>DUELDX_POSE=&lt;Chr&gt;:&lt;동작&gt;:&lt;L|R|U|D&gt; 면 그 인물이 그 동작을 그 방향으로 되풀이한다(화면 밖 그림 시험용 — 무기 층·이펙트 자리를 본다).</summary>
    internal static readonly string? PoseHook = Environment.GetEnvironmentVariable("DUELDX_POSE");

    /// <summary>DUELDX_WORK=&lt;work 번호&gt;[:near] 면 첫 아군이 그 기술을 한 번 쓴다(화면 밖 이펙트 시험용 — 모션·이펙트 자리를 본다). near 면 적 셋을 곁으로 옮긴다.</summary>
    internal static readonly string? WorkHook = Environment.GetEnvironmentVariable("DUELDX_WORK");

    internal bool _workHookDone;

    /// <summary>DUELDX_SAVE=&lt;칸&gt; 이 걸어 둔 저장 — 한 번만 한다(화면 밖 시험용).</summary>
    internal int? _saveSlotPending;

    /// <summary>DUELDX_SAVEAT=&lt;초&gt; 면 그때 저장한다(기본 3초) — 전투가 이어진 뒤를 저장해 보려고.</summary>
    internal static readonly double SaveHookAt =
        double.TryParse(Environment.GetEnvironmentVariable("DUELDX_SAVEAT"), out double at) && at > 0 ? at : 3;

    /// <summary>DUELDX_AIM=&lt;work&gt; 면 플레이어 차례의 인물이 그 work 을 <b>제 칸에</b> 겨눠 누른 것처럼 한다(화면 밖 시험용 — 자기에게 쓰기).
    /// <c>DUELDX_AIM=&lt;work&gt;:&lt;열&gt;,&lt;줄&gt;[:&lt;열&gt;,&lt;줄&gt;]</c> 면 그 칸을 겨눈다(둘째 칸이 있으면 먼저 그 칸에 세운다) — 맵 물체 겨누기 시험.</summary>
    internal bool _aimHookDone;
    internal bool _itemHookDone;
    internal bool _tipHookDone;
    internal bool _aimCheckDone;

    internal void ApplyAimHook()
    {
        string[] aim = Environment.GetEnvironmentVariable("DUELDX_AIM")?.Split(':') ?? [];
        // DUELDX_AIM=basic:ally — 링 「공격」으로 가장 가까운 아군을 겨눈다(버서커 시험).
        if (!_aimHookDone && aim is ["basic", "ally"] && IsPlayerTurn && _routine == null && host.Tlk._talk == null && _runningEvent < 0
            && host._units[_turn] is { Data: { } bd } bu && Work(bd.BasicWorkId) is { } bw)
        {
            _aimHookDone = true;
            var mate = host._units.Where(x => x != bu && x.Alive && x.OnField && x.IsAlly).OrderBy(x => Math.Abs(x.Col - bu.Col) + Math.Abs(x.Row - bu.Row)).FirstOrDefault();
            (_targetWork, _targetIsBasicAttack) = (bw.Id, true);
            int hpBefore = mate?.Hp ?? -1;
            bool tookBasic = mate != null && OnTargetClick(mate.Col, mate.Row);
            if (BattleScene.Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"aim hook basic: {bu.ChrCode}({bu.Col},{bu.Row}) berserk {bu.HasStatus(4)} → ally {mate?.ChrCode}({mate?.Col},{mate?.Row}) hp {hpBefore} took {tookBasic} routine {_routine != null} hint '{host._toast}'" + Environment.NewLine);
            return;
        }
        if (_aimHookDone || aim.Length == 0 || !int.TryParse(aim[0], out int id)) return;
        if (!IsPlayerTurn || _routine != null || host.Tlk._talk != null || _runningEvent >= 0 || Work(id) is not { } w) return;
        _aimHookDone = true;
        var u = host._units[_turn];
        // DUELDX_AIMSHOW=1 이면 누르지 않고 어빌리티 목록에서 고른 것처럼만 한다 — 범위를 먼저 보이는 기술을 시험할 때.
        if (Environment.GetEnvironmentVariable("DUELDX_AIMSHOW") == "1") { SelectAbilityRow((host._db?.Abilities.GetValueOrDefault(w.AbilityId) is { } ab ? host._db.T(ab.NameId) : "", w, true, "")); return; }
        (_targetWork, _targetIsBasicAttack) = (w.Id, false);
        int before = u.Hp;
        bool took;
        if (aim.Length >= 2 && aim[1].Split(',') is [var ac, var ar] && int.TryParse(ac, out int aimCol) && int.TryParse(ar, out int aimRow))
        {
            if (aim.Length >= 3 && aim[2].Split(',') is [var sc, var sr] && int.TryParse(sc, out int standCol) && int.TryParse(sr, out int standRow))
                u.ResetTo(standCol, standRow, keepFacing: true);
            took = OnTargetClick(aimCol, aimRow);
        }
        else
        {
            u.Hp = Math.Max(1, u.Hp / 2);
            before = u.Hp;
            took = OnTargetClick(u.Col, u.Row);
        }
        if (BattleScene.Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"aim hook: {u.ChrCode}({u.Col},{u.Row}) work {w.Id} tm {w.TargetMode} am {w.AreaMode} took {took} routine {_routine != null} toast '{host._toast}' hp {before}" + Environment.NewLine);
    }

    /// <summary>DUELDX_CLICKCELL=&lt;열&gt;,&lt;줄&gt; 면 플레이어 차례에 그 칸 한가운데를 한 번 누른다(화면 밖 시험용 — 클릭 이동).</summary>
    internal bool _clickHookDone;

    internal void ApplyClickHook()
    {
        if (_clickHookDone || Environment.GetEnvironmentVariable("DUELDX_CLICKCELL")?.Split(',') is not [var cs, var rs]
            || !int.TryParse(cs, out int col) || !int.TryParse(rs, out int row)) return;
        if (!IsPlayerTurn || _routine != null || host.Tlk._talk != null || host._units[_turn].IsBusy) return;
        _clickHookDone = true;
        var u = host._units[_turn];
        // DUELDX_CLICKFROM=<열>,<줄> 이면 누르기 전에 차례인 인물을 그 칸에 세운다 — 걸어가서 치는 거리를 시험할 때.
        if (Environment.GetEnvironmentVariable("DUELDX_CLICKFROM")?.Split(',') is [var fc, var fr] && int.TryParse(fc, out int fromCol) && int.TryParse(fr, out int fromRow))
            u.ResetTo(fromCol, fromRow, keepFacing: true);
        int bx = col * TileW + TileW / 2, by = host.CellTop(col, row) + TileH / 2;
        var range = ComputeRange(u);
        string why = $"click cell ({col},{row}) board ({bx},{by}) → RowAt {host.RowAt(bx, by)} unitAt {host.UnitAtBoard(bx, by)} object {ObjectAt(col, host.RowAt(bx, by))?.Data.Id} "
                   + $"turn {u.ChrCode}({u.Col},{u.Row}) reach {range?.CanReach(row * host.Cols + col)} path {(range is { } r ? PathWithin(r, u.Col, u.Row, row * host.Cols + col)?.Count : null)}";
        // 그 칸과 살아 있는 적들 칸의 커서(45 = 칼) · 걸어가 칠 길이 있나 — 커서와 클릭 판정이 어긋나는지 본다.
        why += $" cursor {host.CursorFor(bx, by)} foes [{string.Join(' ', host._units.Where(f => f.Alive && f.OnField && !f.IsAlly).Select(f => $"{f.Col},{f.Row}:{host.CursorFor(f.Col * TileW + TileW / 2, host.CellTop(f.Col, f.Row) + TileH / 2)}{(FindAttackPath(_turn, Array.IndexOf(host._units, f)) != null ? "+" : "-")}{(range?.Red[f.Row * host.Cols + f.Col] == true ? "R" : "")}"))}]";
        host.OnClick((int)((bx - host._camX) * host._zoom + host.ViewOffsetX), (int)((by - host._camY) * host._zoom + host.ViewOffsetY));
        why += $" → queued {u.Path.Count} toast '{host._toast}'";
        if (BattleScene.Trace) System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"), why + Environment.NewLine);
    }

    internal void ApplyWorkHook()
    {
        ApplyAimHook();
        ApplyClickHook();
        // DUELDX_AIMCHECK=<work>:<Chr> 면 그 인물이 그 work 을 제 칸에 겨눌 수 있는지만 추적에 적는다(쓰지는 않는다 — 차례가 아닌 인물도 본다).
        if (!_aimCheckDone && _turnNo >= 1 && Environment.GetEnvironmentVariable("DUELDX_AIMCHECK")?.Split(':') is [var cw, var cc]
            && int.TryParse(cw, out int checkWork) && int.TryParse(cc, out int checkChr) && Work(checkWork) is { } cwk
            && host._units.FirstOrDefault(x => x.ChrCode == checkChr && x.Alive && x.OnField) is { } cu)
        {
            _aimCheckDone = true;
            if (BattleScene.Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"aim check: {cu.ChrCode}({cu.Col},{cu.Row}) work {cwk.Id} tm {cwk.TargetMode} canAim {CanAimAt(cwk, cu, cu.Col, cu.Row)} inRange {InWorkRange(cwk, cu.Col, cu.Row, cu.Col, cu.Row, cu)} "
                    + $"flags {CellFlagsAt(cu.Col, cu.Row):x} afford {CanAfford(cu, cwk)} tp {cu.Tp} ctp {cu.Ctp} cost {(cu.Data is { } cd ? TpCostFor(cu, cd, cwk.Id) : -1)} soul {cu.Soul} need {(cu.Data is { } cd2 ? SoulNeedFor(cu, cd2, cwk.Id) : -1)} "
                    + $"h {HeightAt(cu.Col, cu.Row)} statuses {string.Join(',', cu.StatusId)}" + Environment.NewLine);
        }
        // DUELDX_ABILITYTIP=<줄> 이면 플레이어 차례에 어빌리티 목록을 열고 그 줄의 설명 창을 띄운다(화면 밖 시험용 — 설명 문구).
        if (!_tipHookDone && int.TryParse(Environment.GetEnvironmentVariable("DUELDX_ABILITYTIP"), out int tipRow) && IsPlayerTurn && _routine == null && host.Tlk._talk == null)
        {
            _tipHookDone = true;
            _abilityMenu = true;
            _abilityTop = 0;
            _abilityPressed = tipRow;
            var (tx, ty) = UnitFoot(host._units[_turn]);
            host._mouse = (tx + 20, ty - 120);
        }
        // DUELDX_ITEMMENU=<줄> 이면 플레이어 차례에 아이템 창을 열고 마우스를 그 줄 위에 둔다(화면 밖 시험용 — 줄 강조).
        if (!_itemHookDone && int.TryParse(Environment.GetEnvironmentVariable("DUELDX_ITEMMENU"), out int itemRow) && IsPlayerTurn && _routine == null && host.Tlk._talk == null)
        {
            _itemHookDone = true;
            _itemMenu = true;
            var (ix, iy, _) = ItemMenuRect();
            host._mouse = (ix + 60, iy + 12 + itemRow * ItemRowH + 8);
        }
        if (_turnNo >= 1) OpenUnitInfoIfAsked();
        if (WorkHook == null || _workHookDone || _routine != null || host._units.Length == 0) return;
        if (host.Tlk._talk != null || _outcome.Length > 0) return;   // 대사가 끝나기를 기다린다
        if (_turnNo < 1) return;                            // 시작 사건(적이 나타나기 전)이 끝나기를 기다린다
        var parts = WorkHook.Split(':');
        if (!int.TryParse(parts[0], out int id) || Work(id) is not { } w) return;
        // DUELDX_WORKBY=<Chr> 면 그 인물이 쓴다(없으면 첫 아군).
        int by = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_WORKBY"), out int chrBy) ? chrBy : 0;
        int caster = Array.FindIndex(host._units, u => u.Alive && u.OnField && u.IsAlly && (by == 0 || u.ChrCode == by));
        if (caster < 0) return;
        _workHookDone = true;
        var a = host._units[caster];
        // 「<work>:near」 면 적 셋을 시전자 곁 칸으로 옮겨 둔다 — 자기 중심 범위기(나인 크루세이더)를 시험할 때 대상이 있게.
        if (parts.Length > 1 && parts[1] == "near")
        {
            (int C, int R)[] spots = [(a.Col + 2, a.Row), (a.Col, a.Row + 2), (a.Col + 1, a.Row - 2)];
            int k = 0;
            foreach (var enemy in host._units.Where(u => u.Alive && u.OnField && !u.IsAlly))
            {
                if (k >= spots.Length) break;
                var (c, r) = spots[k++];
                if (c < 0 || r < 0 || c >= host.Cols || r >= host.Rows) continue;
                enemy.WarpTo(c, r);
            }
        }
        // 「<work>:ally」 면 시전자 말고 가장 먼 아군을 대상으로 — 리콜처럼 아군에게 쓰는 기술을 시험할 때.
        int target = parts.Length > 1 && parts[1] == "ally"
            ? Array.IndexOf(host._units, host._units.Where(u => u.Alive && u.OnField && u.IsAlly && u != a).OrderByDescending(u => Math.Abs(u.Col - a.Col) + Math.Abs(u.Row - a.Row)).FirstOrDefault())
            : Array.FindIndex(host._units, u => u.Alive && u.OnField && !u.IsAlly);
        // 자기 중심 기술은 게임처럼 대상 없이(−1) 제 칸에 쓴다(UseSelfCentredWork).
        if (w.SelfCentred) { _routine = UseWorkRoutine(caster, w, -1, a.Col, a.Row, []); return; }
        // 빈 칸을 겨누는 기술(방식 7 — 혼·오메가 스윙)은 그 적 너머의 빈 칸을 겨눈다(돌진 시험).
        if (w.TargetMode == 7 && target >= 0)
        {
            var t = host._units[target];
            int tc = t.Col + Math.Sign(t.Col - a.Col), tr = t.Row + Math.Sign(t.Row - a.Row);
            _routine = UseWorkRoutine(caster, w, -1, tc, tr, []);
            return;
        }
        _routine = UseWorkRoutine(caster, w, target,
                                  target >= 0 ? host._units[target].Col : a.Col,
                                  target >= 0 ? host._units[target].Row : a.Row, []);
    }

    internal void ApplyPoseHook()
    {
        if (PoseHook == null || host._units.Length == 0) return;
        var parts = PoseHook.Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[0], out int chr) || !int.TryParse(parts[1], out int action)) return;
        foreach (var u in host._units)
        {
            if (u.ChrCode != chr || !u.Alive) continue;
            if (parts.Length > 2) u.Facing = parts[2] switch { "R" => Facing.Right, "U" => Facing.Up, "D" => Facing.Down, _ => Facing.Left };
            if (u.Action < 0) PlayAction(u, action);
        }
    }

    internal void DrawGridLines()
    {
        // 칸마다 제 높이 자리에 네모를 친다 — 층이 다른 칸은 격자도 어긋나 절벽이 보인다.
        for (int row = 0; row < host.Rows; row++)
            for (int col = 0; col < host.Cols; col++)
                host.StrokeRect(col * TileW, host.CellTop(col, row), TileW + 1, TileH + 1, GridLine);
    }

    internal void DrawUnits()
    {
        var sprites = host._sprites;
        // 그림을 읽는 스레드가 <b>인물 배열을 통째로 갈아 끼운다</b>(그림 없는 인물을 빼면서) —
        // 그리는 동안 길이가 줄면 칸을 벗어난다. 한 벌을 붙잡아 놓고 그린다.
        var units = host._units;

        // 아래 줄 인물이 위 줄 인물을 가리도록 발 위치(y) 순서로 그린다.
        foreach (int i in Enumerable.Range(0, units.Length).OrderBy(i => units[i].Y))
        {
            var unit = units[i];
            if (!unit.Alive || !unit.OnField) continue;
            var (footX, footY) = UnitFoot(unit);
            int headY = footY - TileH;

            if (sprites.TryGetValue(unit.ChrCode, out var sprite))
            {
                var frame = sprite.FrameFor(unit);
                // 맞을 때의 흰 번쩍임(물들이기 키)과 1픽셀 떨림(자리 키)은 모션 자료에 들어 있다(분석-전투 fg-10).
                var (clip, tick) = sprite.CurrentClip(unit);
                var tint = clip?.TintAt(tick);
                var (ox, oy) = clip?.OffsetAt(tick) ?? (0, 0);
                // 몸 그림도 모션표의 섞기 키(종류 3)를 따른다 — 17 이면 더하기. 장교(Obs 0165·0863)는 걸을 때 몸이 빛 공으로 바뀌는데,
                // 불투명으로 찍어 공 둘레의 검은 부분이 원판처럼 남았다(사용자 보고, Btl 0256). 딸린 층·필드 소품은 이미 이렇게 그린다.
                bool additive = clip?.BlendAt(tick) == 17;
                host.BlitMasked(frame.Px, frame.W, frame.H, footX + frame.X + ox, footY + frame.Y + oy, tint, BattleScene.StatusTintOf(unit), unit.Fade, additive);
                headY = footY + frame.Y;
                DrawUnitLayers(clip, tick, footX + ox, footY + oy, unit.Facing == Facing.Right, unit.Fade, loop: unit.Loops);
            }
            // 차례 표시는 TP 가 찬(차례 깃발이 선) <b>모든</b> 유닛에게 — 지금 움직이는 유닛은 행동 동안 숨긴다(0x100d1f70: +0xff && 상태≠22).
            bool acting = i == _turn && !(IsPlayerTurn && !unit.IsBusy);
            if (unit.HasTurn && !acting && _outcome.Length == 0) DrawTurnMarker(unit, footX, headY);
        }
    }

    /// <summary>차례 표시 그림 — Obs 0163(0x100d1de0 → 0x100e5310(Obs 0xa3, 모션)).</summary>
    internal const int TurnMarkerObs = 163;

    /// <summary>
    /// TP 가 찬 유닛 머리 위의 역삼각형(0x1006da40 → 0x10074320 이 붙이는 표시 객체). 모션 = (AI 편 ? 2 : 0) + (군단 부하 ? 1 : 0):
    /// 0 사람 편 연보라 · 1 사람 편 부하(작은 것) · 2 AI 편 주황 · 3 AI 편 부하. 모션마다 15틱에 4픽셀 까딱인다.
    /// 그림이 없으면 예전처럼 코드로 연보라 삼각형을 그린다.
    /// </summary>
    internal void DrawTurnMarker(UnitState unit, int x, int headY)
    {
        // 편은 0x10074250(그 부대가 AI 인가)으로 — 자동 진행(AutoPlay)이어도 사람 편은 연보라.
        bool human = unit.PlayerControlled || (unit.IsAlly && !host._allyAi);
        int motion = (human ? 0 : 2) + (unit.LeaderIndex >= 0 ? 1 : 0);
        if (host.DrawUi(TurnMarkerObs, motion, (int)(host._lastTime * TicksPerSecond), x, headY, GameWindow.UiBlend.Alpha)) return;
        const int W = 13, H = 9, Gap = 4;
        uint alpha = (uint)(150 + 105 * (0.5 + 0.5 * Math.Sin(host._lastTime * Math.PI * 2 * 1.5)));
        uint fill = alpha << 24 | 0xE0D8FF, edge = alpha << 24 | 0x6050A0;
        int top = headY - Gap - H;
        for (int row = 0; row < H; row++)
        {
            int half = (W / 2) * (H - 1 - row) / (H - 1);
            for (int dx = -half; dx <= half; dx++)
                host.SetPixel(x + dx, top + row, dx == -half || dx == half || row == 0 ? edge : fill);
        }
    }

    /// <summary>인물 발 자리(판 픽셀) — 걷는 중이면 두 칸 사이.</summary>
    internal (int X, int Y) UnitFoot(UnitState unit) =>
        ((int)(unit.X * TileW) + TileW / 2 + (int)unit.EntryX,
         GridTop + host.BoardPad + (int)(unit.Y * TileH) + TileH / 2 - (int)Math.Round(host.HeightPxAt(unit.X, unit.Y)) + (int)unit.EntryY);
}
