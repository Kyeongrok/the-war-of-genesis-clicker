using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 어빌리티 시연(도구 &gt; 체질별 어빌리티의 「시연」 단추, 사용자 요청) — 인물 하나가 적이 선 칸에 범위를 잡아 그 기술을 써 보인다.
/// </summary>
/// <remarks>
/// 하던 판을 건드리지 않으려고 <b>따로 선 실험판</b>에서 한다 — 편집기의 「게임에서 실험」과 같은 전투(<see cref="DemoBattle"/>)에
/// 아군 하나만 세우고(<see cref="GameWindow.ArenaScene"/>) 사건은 돌리지 않는다. 차례는 흐르지 않는다: 판이 서면 곧바로
/// 사거리(붉은 칸)와 효과 범위(주황 칸)를 잠깐 보인 뒤 보통의 기술 루틴(<see cref="UseWorkRoutine"/>)을 돌리고, 끝나면 시연 전으로 돌아간다.
/// 돌아가는 길은 게임의 저장·불러오기 그대로다 — 누르는 순간 <c>skill-demo.sav</c> 에 적어 두고 끝나면 그것을 부른다(타이틀에서는 타이틀로).
/// 시연 중에는 아무도 쓰러지지 않게 HP 를 1 에서 막는다 — 쓰러지면 승패 판정·레벨업 창·통계가 따라 돌기 때문이다.
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    /// <summary>시연을 벌이는 전투 — 편집기의 어빌리티 실험이 여는 것과 같다.</summary>
    internal const int DemoBattle = 106;

    /// <summary>파티가 없을 때(타이틀) 시연하는 인물 — 실험판 기본값과 같은 죠안.</summary>
    internal const int DemoDefaultChr = 221;

    internal sealed class SkillDemo(WorkData work, string name, int chr, string? savePath)
    {
        public WorkData Work { get; } = work;
        public string Name { get; } = name;
        public int Chr { get; } = chr;
        /// <summary>시연 전 상태를 적어 둔 파일 — 타이틀에서 눌렀으면 null(끝나면 타이틀로).</summary>
        public string? SavePath { get; } = savePath;
        /// <summary>0 판이 서기를 기다림 · 1 시연 루틴이 도는 중 · 2 끝 — 되돌린다.</summary>
        public int Stage { get; set; }
        public Dictionary<BattleStats.Key, BattleStats.Row> Stats { get; init; } = [];
    }

    /// <summary>지금 도는 시연 — 없으면 null.</summary>
    internal SkillDemo? _demo;

    /// <summary>시연을 시작한다. 못 하면 까닭을 돌려준다(시작했으면 null).</summary>
    internal string? StartSkillDemo(AbilityData ab, int level)
    {
        const string notNow = "지금은 시연할 수 없습니다 — 전투의 내 차례나 모세스 · 타이틀 화면에서 눌러 주세요";
        if (_demo != null) return "이미 시연하는 중입니다";
        if (host._db is not { } db || !ab.WorkByLevel.TryGetValue(level, out int wid) || Work(wid) is not { } w) return "그 레벨의 자료가 없습니다";
        if (ab.Category != 1 || w.Kind == 4) return "전투에서 직접 쓰는 어빌리티만 시연할 수 있습니다";
        if (host._loading || host.SceneFading || host._afterFadeOut != null || host.RecordsScr._recordsOpen) return notNow;

        string? save = null;
        if (!host.TitleScr._titleOpen)
        {
            // 저장할 수 없는 때(필드 · 사건 도중 · 적 차례)는 되돌릴 길이 없다.
            if (host.Sys.SaveBlockedReason != null || (!host.Mos._mosesOpen && !host.EpisodesScr._episodesOpen && !host._battleLoaded)) return notNow;
            save = UserDataFolder.File("skill-demo.sav");
            if (!host.Sys.SaveBattleTo(save)) return "시연 전 상태를 적어 두지 못해 시연하지 않습니다";
        }
        int chr = host.Mos._members.Where(host._party.ContainsKey).DefaultIfEmpty(DemoDefaultChr).First();
        _demo = new SkillDemo(w, $"{db.T(ab.NameId)} Lv{level}", chr, save) { Stats = new(host.Stats._battle) };
        host.Sys.CloseSystemWindow();
        if (!host.Sys.StartBattle(DemoBattle, rememberParty: false))
        {
            _demo = null;
            return $"시연용 전투(Btl {DemoBattle:D4}) 자료가 없어 시연할 수 없습니다";
        }
        // 실험판은 그 전투의 사건(대사 · 증원 · 승패 조건)을 안 돌린다.
        _events = [];
        _eventFired = [];
        return null;
    }

    /// <summary>
    /// 시연이 도는 동안 차례 갱신 맨 앞에서 부른다 — true 면 이번 틀의 차례 갱신을 여기서 끝낸다(틱이 흘러 적이 움직이지 않게).
    /// </summary>
    internal bool StepSkillDemo()
    {
        if (_demo is not { } demo) return false;
        switch (demo.Stage)
        {
            case 0:
                if (host.SceneFading || host._battleIntroPending) return true;
                int caster = Array.FindIndex(host._units, u => u.Side == 4 && u.Alive && u.OnField && u.Data != null);
                if (caster < 0) { host.Toast("시연할 인물을 세우지 못했습니다"); demo.Stage = 2; return true; }
                var a = host._units[caster];
                (a.Soul, a.Tp) = (a.MaxSoul, a.MaxTp);          // 실험판처럼 SOUL · TP 를 가득 채우고 쓴다
                if (PlaceForDemo(a, demo.Work) is not { } aim) { host.Toast($"{demo.Name} — 이 전투에서는 겨눌 대상을 찾지 못했습니다"); demo.Stage = 2; return true; }
                (_turn, host._selected) = (caster, caster);    // 사거리 칸은 차례인 인물 자리에서 그린다(DrawWorkRange)
                _routine = SkillDemoRoutine(caster, demo, aim.Col, aim.Row);
                demo.Stage = 1;
                return false;
            case 1:
                if (_routine != null) return false;
                demo.Stage = 2;
                return true;
            default:
                FinishSkillDemo(demo);
                return true;
        }
    }

    /// <summary>범위를 먼저 보이고(원본의 대상 고르기 화면 그대로) 기술을 쓴다.</summary>
    private IEnumerator<bool> SkillDemoRoutine(int caster, SkillDemo demo, int col, int row)
    {
        var a = host._units[caster];
        foreach (bool _ in CenterUnitAndWait(a)) yield return true;
        (_targetWork, _targetIsBasicAttack, _attackCursor, _aimCell) = (demo.Work.Id, false, -1, (col, row));
        host.Toast($"{demo.Name} 시연 — 붉은 칸이 사거리, 주황 칸이 효과 범위입니다");
        for (double end = host._lastTime + 1.6; host._lastTime < end;) yield return true;
        CancelTargeting();
        var cast = UseWorkRoutine(caster, demo.Work, -1, col, row, []);
        while (cast.MoveNext()) yield return true;
        for (double end = host._lastTime + 1.2; host._lastTime < end;) yield return true;   // 뜬 숫자를 읽을 틈
    }

    /// <summary>시연 전으로 돌아간다 — 적어 둔 판을 부르거나(전투 · 모세스 · 연대표) 타이틀로.</summary>
    private void FinishSkillDemo(SkillDemo demo)
    {
        _demo = null;
        _routine = null;
        CancelTargeting();
        _turn = -1;
        _levelUpQueue.Clear();
        _levelUpUnit = -1;
        _numbers.Clear();
        _effects.Clear();
        host._mapTintTarget = -1;
        if (demo.SavePath is { } path)
        {
            host._battleLoaded = false;                      // 하던 전투가 시연 전투와 같은 번호여도 판을 새로 세운다
            if (!host.Sys.LoadBattleFrom(path)) host.Sys.BackToTitle();
            try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        else host.Sys.BackToTitle();
        // 「이번 전투」 통계는 판을 새로 세우며 비워진다 — 시연 전 것을 돌려놓는다.
        host.Stats._battle.Clear();
        foreach (var (key, row) in demo.Stats) host.Stats._battle[key] = row;
        host.Toast($"{demo.Name} 시연이 끝나 시연 전 화면으로 돌아왔습니다");
        host.SkillBookScr.Reopen();
    }

    /// <summary>
    /// 시연할 자리를 잡는다 — 지금 자리에서 겨눌 적이 없으면 인물을 적 가까이로 옮겨 세운다. 겨눌 칸을 돌려준다(못 찾으면 null).
    /// </summary>
    private (int Col, int Row)? PlaceForDemo(UnitState a, WorkData w)
    {
        if (DemoAimFrom(a, w) is { } here) return here;
        var (homeCol, homeRow) = (a.Col, a.Row);
        int reach = Math.Clamp(Math.Max((int)w.RangeMax, (int)w.AreaArg) + 2, 3, 30);
        var foes = host._units.Where(u => u.Alive && u.OnField && SeesAsFoe(a, u))
                              .OrderBy(u => Math.Abs(u.Col - homeCol) + Math.Abs(u.Row - homeRow)).Take(6).ToList();
        foreach (var foe in foes)
        {
            var cells = from row in Enumerable.Range(foe.Row - reach, 2 * reach + 1)
                        from col in Enumerable.Range(foe.Col - reach, 2 * reach + 1)
                        where (uint)col < host.Cols && (uint)row < host.Rows && host._map is { } map && col < map.Cols && row < map.Rows
                              && (CellFlagsAt(col, row) & 0x9) == 0 && LiveUnitAt(col, row) == null && ObjectAt(col, row) == null
                        orderby Math.Abs(col - homeCol) + Math.Abs(row - homeRow)
                        select (col, row);
            foreach (var (col, row) in cells)
            {
                a.WarpTo(col, row);
                (a.OriginCol, a.OriginRow) = (col, row);
                if (DemoAimFrom(a, w) is { } aim) return aim;
            }
        }
        a.WarpTo(homeCol, homeRow);
        (a.OriginCol, a.OriginRow) = (homeCol, homeRow);
        return null;
    }

    /// <summary>지금 선 자리에서 겨눌 칸 — 적이 선 칸(가까운 적부터), 아군 대상 기술이면 제 칸. 없으면 null.</summary>
    private (int Col, int Row)? DemoAimFrom(UnitState a, WorkData w)
    {
        if (w.SelfCentred)
        {
            // 제자리 범위기는 범위 안에 맞을 다른 유닛이 있어야 볼 것이 있다. 범위가 없는 것(제게 거는 기술)은 어디서든 된다.
            bool hasOthers = WorkTargets(w, a, a.Col, a.Row).Any(i => host._units[i] != a);
            return w.AreaShape == 0 || hasOthers ? (a.Col, a.Row) : null;
        }
        if (w.TargetMode == 4) return CanAimAt(w, a, a.Col, a.Row) ? (a.Col, a.Row) : null;
        foreach (var foe in host._units.Where(u => u.Alive && u.OnField && SeesAsFoe(a, u))
                                       .OrderBy(u => Math.Abs(u.Col - a.Col) + Math.Abs(u.Row - a.Row)))
        {
            if (w.TargetMode != 7) { if (CanAimAt(w, a, foe.Col, foe.Row)) return (foe.Col, foe.Row); continue; }
            // 빈 칸을 겨누는 기술(돌진 따위)은 그 적 곁의 빈 칸으로.
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (2, 0), (-2, 0), (0, 2), (0, -2) })
                if (CanAimAt(w, a, foe.Col + dx, foe.Row + dy)) return (foe.Col + dx, foe.Row + dy);
        }
        return null;
    }
}
