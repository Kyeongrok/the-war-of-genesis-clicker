using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 턴 진행 — TP 게이지로 차례를 정하고, 아군 차례엔 플레이어가, 적군 차례엔 간단한 AI 가 움직인다.
/// 공격·어빌리티는 모두 work 하나를 쓰는 <see cref="UseWorkRoutine"/> 로 처리한다.
/// </summary>
/// <remarks>
/// 차례 규칙은 <c>G3PartII.dll</c> 그대로(옵시디안 분석-전투 ba-2·ba-3·ba-4):
/// <list type="bullet">
/// <item>시간 한 칸(틱)마다 살아 있는 모든 인물 TP += STP(최대 TP / TP 나눗수), 최대에서 자른다.</item>
/// <item>TP 가 최대에 닿은 인물이 차례 표시를 받고, 표시가 있는 인물 중 <b>배열 번호가 작은 쪽</b>이 먼저 움직인다(아군·적 구분 없음).
///   전투 시작 때는 모두 TP 가 가득하다. 이 데모는 아군을 배열 앞에 두어 아군이 먼저 움직인다(Btl 파일은 적이 앞).</item>
/// <item>걷는 동안에는 TP 를 안 쓰고, 공격·어빌리티·휴식 직전에 시작 자리→지금 자리 걸음 비용을 한 번에 뺀다.
///   work 가 TP 를 쓰고, TP 가 0 이하가 되면 자동으로 휴식 — (최대HP − HP) × 남은 TP 비율 × Num[35]% 를 채우고 남은 TP 를 버린다.</item>
/// <item>SOUL: work 끝에 종류별 +10/6/4/4, 맞으면 피해/Num[43], 쓰러뜨리면 +10. work 의 SOUL 비용은 뺀다.</item>
/// </list>
/// 판정(명중·피해·흔들기·치명·회복)은 <see cref="GameDatabase.Resolve"/>(<c>0x1007b6f0</c>) — 분석-전투 "공격·어빌리티 판정".
/// 상태이상·자세·효과 범위 모양 2~9 는 빠져 있다. 적 AI 는 원본을 옮긴 것이 아니라
/// "칠 수 있으면 가장 적게 걸어 치고, 아니면 가장 가까운 아군 쪽으로 걷는다" 는 단순 규칙이다.
/// </remarks>
internal sealed unsafe partial class BattleScene(GameWindow host)
{
    /// <summary>지금 차례인 인물 번호. 차례를 기다리는 중이면 −1.</summary>
    internal int _turn = -1;
    /// <summary>전투가 흐른 틱 — 원본은 전투를 만들 때 <b>1</b> 로 놓는다(<c>0x100644d4</c>).</summary>
    internal int _tick = 1;
    internal IEnumerator<bool>? _routine;
    internal string _outcome = "";
    internal double _outcomeAt;
    internal double _nextTickAt;

    /// <summary>지금 도는 루틴이 물체 차례(ObjectTurns)인가 — 끝날 때 자동 회복을 돌리지 않는다.</summary>
    internal bool _objectRoutine;

    /// <summary>그리기에서만 쓰는 난수(깜빡임·줄 찢김) — 판정 난수 <see cref="_rng"/> 를 그리기 틀 수가 흔들지 않게 따로 둔다.</summary>
    internal readonly Random _drawRng = new();
    internal readonly List<(string Text, float Size, int X, int Y, double Start, uint Color)> _popups = [];

    /// <summary>대상을 고르는 중인 work 번호(기본공격 포함). 고르는 중이 아니면 −1.</summary>
    internal int _targetWork = -1;
    internal bool _targetIsBasicAttack;

    /// <summary>공격 대상 커서 — 공격을 열면 칠 수 있는 적 중 HP 가 가장 낮은 적. 없으면 −1.</summary>
    internal int _attackCursor = -1;

    /// <summary>
    /// 기본공격 커서가 유닛 대신 가리키는 부술 수 있는 적 물체(기총포탑 따위) — 이것이 서면 <see cref="_attackCursor"/> 는 −1.
    /// 전에는 커서가 유닛만 돌아 E·Enter·패드로는 포탑을 못 쳤다(사용자 보고 fa-65: Btl 0161).
    /// </summary>
    internal DemoObject? _attackObject;

    /// <summary>겨누는 중에 커서가 무엇이든(적 · 물체) 서 있나.</summary>
    internal bool HasAttackCursor => _attackCursor >= 0 || _attackObject != null;

    /// <summary>기본공격 work 1 의 동작 — 준비 5 → 베기 8 → 복귀 24(분석-모션 <c>0x1007f0a0</c>). 판정은 베기 끝에 들어간다.</summary>
    internal static readonly int[] StrikeActions = [5, 8, 24];

    /// <summary>
    /// 기본공격 work 마다 도는 동작이 다르다(분석-모션 「인물별 동작 구간표는 없다」).
    /// 288명은 work 1 로 베고, 48명은 387(때리는 동작이 없다), 47명은 1479, 48명은 6·1584 로 <b>쏜다</b>(동작 9).
    /// </summary>
    internal static readonly Dictionary<int, int[]> BasicWorkActions = new()
    {
        [1] = [5, 8, 24],
        [387] = [5, 7],
        [1479] = [8],
        [6] = [9],
        [1584] = [9],
    };
    internal const int StrikeHitStep = 1;

    /// <summary>자세를 세우는 work — 516 방어(맞을 때 한 번 더 깎임), 515 회피(상대 명중 −DEX/5).</summary>
    internal const int StanceDefendWork = 516, StanceEvadeWork = 515;
    internal const double TickDelaySeconds = 0.2;   // 행동 끝 → 다음 GETNEXT 까지 상태 20·18·19·21·4 를 지나 약 6걸음(ba-20 W)

    /// <summary>
    /// 그 인물을 내가 직접 움직이나 — 편 4 는 늘, 편 3(동맹)은 「모드 &gt; 동맹을 AI 가 움직임」을 껐을 때.
    /// 파트1 혼란(상태 16)이 걸린 동안은 AI 가 움직인다 — 원본은 편 4 를 3(AI 동맹)으로 바꾸고 AI 칸 +0x262 를 1 로 둔다(<c>0x10042674</c>).
    /// </summary>
    internal bool IsMine(UnitState u) => !AutoPlay && (u.PlayerControlled || (u.IsAlly && !host._allyAi))
                                         && !(host._scene.IsPart1 && Part1.HasAilment(u, Part1Rules.Confusion));

    /// <summary>DUELDX_AUTOPLAY=1 이면 내 부대도 AI 가 움직인다 — 화면 밖 시험에서 전투를 저절로 돌리려고.</summary>
    internal static readonly bool AutoPlay = Environment.GetEnvironmentVariable("DUELDX_AUTOPLAY") == "1";

    /// <summary>DUELDX_TRACE=1 이면 동작 재생을 <c>%TEMP%\dueldx_trace.log</c> 에 적는다(화면 밖 시험용).</summary>
    /// <summary>파트1 판의 수치 식(<see cref="Part1Rules"/>).</summary>
    internal Part1Rules Part1 => _part1 ??= new(code => Part1Chr.Load(code) is { } chr ? host.Part1.Apply(chr) : null, current: true)
    {
        RiderOf = u => _part1Riders.GetValueOrDefault(u),
    };
    private Part1Rules? _part1;

    /// <summary>
    /// 파트1 인물이 기본 공격(빌린 파트2 기본 work)을 쓰면 그 Attack.dat 번호(인물 칸 9, 없으면 무기 갈래) — <see cref="Part1Skill"/> 가 그 연출을 그릴 수 있을 때만.
    /// 아니면 0(파트2 그대로).
    /// </summary>
    private int Part1BasicAttack(UnitState a, WorkData w) =>
        a.ChrCode >= Part1Game.ChrBase && a.Data?.BasicWorkId == w.Id && Part1.Fighter(a) is { } f
        && Part1Rules.BasicAttackOf(f) is var id && Part1Skill.DrawsBasic(id) ? id : 0;

    internal static readonly bool Trace = Environment.GetEnvironmentVariable("DUELDX_TRACE") == "1";

    internal bool IsPlayerTurn => _turn >= 0 && IsMine(host._units[_turn]) && _routine == null && _outcome.Length == 0;


    /// <summary>지금 판의 아군 상태를 파티에 담아 둔다(다음 전투로 이어진다).</summary>
    internal void RememberParty()
    {
        foreach (var unit in host._units)
            if (unit.IsAlly && unit.LeaderIndex < 0 && !unit.Detached && !unit.WasFollower && unit.Data is { } c) host._party[unit.ChrCode] = c;   // 군단 부하는 파티원이 아니다
    }

    /// <summary>게임 표를 다 읽은 뒤 인물마다 전투 수치를 채운다.</summary>
    internal void InitBattle()
    {
        if (host._db == null) return;
        // 적 레벨의 기준이 되는 파티 레벨은 <b>유닛을 채우기 전에</b> 한 번 셈한다 —
        // 채우는 도중에 세면 아직 안 채워진 아군 때문에 순서에 따라 값이 흔들린다.
        int partyLevel = PartyLevel();
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"party level {partyLevel} — members [{string.Join(",", host.Mos._members.Select(m => $"{m}:{host._party.GetValueOrDefault(m)?.Level}"))}] followers {host._units.Count(u => u.LeaderIndex >= 0)}" + Environment.NewLine);
        foreach (var unit in host._units)
        {
            if (host._db.Character(unit.ChrCode) is not { } c) continue;
            // 데모라 쌓인 경험치는 지금 레벨에 맞춰 시작한다(저장 파일이 없다).
            // DUELDX_CUMEXP 로 아군 시작값을 바꿀 수 있다 — 레벨업 창을 시험할 때 쓴다(예: 190 이면 한 번만 쓰러뜨려도 오름).
            int startCum = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_CUMEXP"), out int v) ? v : c.Level * 100;
            // 앞 전투에서 얻은 레벨·경험치·장비는 다음 전투로 이어진다(_party 가 들고 있다).
            // DUELDX_CUMEXP 를 주면 레벨도 그 값에 맞춘다 — 쌓인 경험치와 레벨은 늘 짝이 맞아야 한다(레벨 = 쌓인 경험치 ÷ 100).
            // 부하는 명부(_party)에 같은 Chr 가 있어도(옛 세이브가 넣어 둔 절반 레벨 기록) 늘 제 레코드에서 새로 키운다.
            bool follower = unit.LeaderIndex >= 0;
            // 명부의 자료를 이어받는 것은 <b>내 편과 주역(면제 명단)</b>뿐이다 — 원본의 명부(0x101b6884)에는 주역 25명만 있고, 거기 없는 유닛은
            // 늘 .chr 처음 값에서 만들어 파티 레벨로 키운다(0x10072770 ③ → 0x1007a8e0). 이 게임의 명부(_party)에는 스테이터스를 열어 본 인물 따위도
            // 끼어드는데, 적이 그 기록(이미 키운 능력치)을 이어받고 또 자라 두 번 불어났다 — Btl 0145 데미안이 LP 4500 → 11520 → 26726,
            // PSY 260 → 644 → 1384 로 서서 천지 파열무 두 방에 끝났다(사용자 보고 fg-29). 겉모습(701 이 바꾸는 칸)만 이어받는다.
            host._party.TryGetValue(unit.ChrCode, out var carried);
            // 내 편이라도 <b>부대원이 아닌 손님</b>(편 3 동맹 따위)은 이어받지 않는다 — 전투가 끝날 때 RememberParty 가 판의 아군을 모두 적어 두는데,
            // 손님이 그 기록을 이어받으면 처음 나온 전투의 레벨에 머문다. 원본은 명부 밖 유닛을 전투마다 지금 파티 레벨로 새로 키운다(ba-22).
            bool mine = unit.IsAlly && (host.Mos._members.Contains(unit.ChrCode) || host.Mos._members.Count == 0);
            // 파트1 인물은 명부를 이어받지 않는다 — 자란 것(경험치 · 배운 어빌리티 · 직업 · 장비)은 파트1 진행(Part1Progress)에 있고
            // db.Character 가 그것을 입혀 늘 새로 만든다. 명부에는 파트1 전투 세이브를 불러올 때(RestorePartyBeforeBoard) 그때의 사본이 들어가는데,
            // 파트1 판은 끝날 때 명부를 안 고치므로(rememberParty: false) 그 뒤 판마다 불러온 때의 어빌리티 목록으로 섰다
            // (사용자 보고: 다음 전투에 앞 판에서 올린 어빌리티가 안 보이다가 몇 턴 뒤 그 인물이 또 고르면 뜬다 · 마르자나는 늘 기본만).
            bool part1Chr = unit.ChrCode >= Part1Game.ChrBase;
            if (carried != null && (follower || part1Chr || !(mine || host._db.LevelExempt.Contains(unit.ChrCode))))
            {
                // 파트1 인물의 겉모습도 진행(Part1Progress.Looks — 캐릭터 에디터 「외형」)에서 온다 — 불러온 때의 사본을 입히면 그 뒤에 바꾼 모습이 다음 판에 안 들었다(menu-97).
                if (!follower && !part1Chr) c = c with { SpriteId = carried.SpriteId, FaceId = carried.FaceId, NameId = carried.NameId, Name2Id = carried.Name2Id, TitleId = carried.TitleId };
                carried = null;
            }
            unit.Data = carried != null ? carried
                      : unit.IsAlly ? c with { Exp = StatusScreen.DemoExp, CumExp = startCum, Level = (ushort)Math.Max(c.Level, startCum / 100),
                                               // DUELDX_JOB=<직업> 이면 아군 직업을 바꾼다 — 전직 화면(2단계·3단계 단추)을 시험할 때 쓴다.
                                               JobId = ushort.TryParse(Environment.GetEnvironmentVariable("DUELDX_JOB"), out ushort job) ? job : c.JobId }
                      : c with { CumExp = c.Level * 100 };
            // 파티 레벨에 맞춰 자란다 — 면제 명단(0002.nch)에 없는 인물은 <b>편과 상관없이</b>(0x100633ff — 편 3 손님 제이슨도 자란다).
            // 파티 객체에서 온 인물(_party)은 제 레벨을 그대로 쓴다.
            // 파트1 판은 키우지 않는다 — 파트1 에는 레벨이 없고 능력치는 인물 레코드와 어빌리티(HP · AC · SKILL …)로만 오른다(사용자 지적).
            if (!host._scene.IsPart1 && (!unit.IsAlly || follower || carried == null)) unit.Data = GrowToPartyLevel(unit.Data ?? c, unit.LevelOffset, partyLevel);
            // 군단 부하는 능력치는 제 레벨 줄로 키우되 <b>적히는 레벨은 절반</b>이다(0x1007a916~0x1007a948 — max(1, L/2), EXP 는 그 ×100).
            // 처치 EXP(레벨 차)·상태이상 레벨 조건·정보 창이 이 값을 읽는다(ba-20 M1). 전에는 부하를 잡으면 EXP 가 과했다.
            if (unit.LeaderIndex >= 0 && unit.Data is { } fd)
            {
                int half = Math.Max(1, fd.Level / 2);
                unit.Data = fd with { Level = (ushort)half, CumExp = half * 100 };
                unit.WasFollower = true;
            }

            // 최대치는 <b>이어받은 인물</b>로 셈한다 — 앞 전투에서 레벨이 올랐으면 그 값이 따라와야 한다.
            var data = unit.Data ?? c;
            unit.MaxHp = unit.Hp = ScaleMaxHp(unit, Math.Max(1, host._db.MaxHp(data)));
            unit.MaxTp = host._db.MaxTp(data);
            // 원본은 유닛을 만들 때 <b>TP 를 0 으로 민다</b>(0x10071941) — 아무도 다시 안 채운다.
            // 그래서 첫 차례는 「최대TP ÷ STP」가 가장 작은 인물이 가져간다.
            unit.Tp = 0;
            unit.Stp = Math.Max(0, host._db.Stp(data));   // 제수 0 이면 TP 가 영영 안 찬다(0x10071db0) — 호위 대상(필그림·피맨·인질·리온)은 차례를 안 받는다(ba-20 K2)
            unit.MaxSoul = host._db.MaxSoul(data);
            // DUELDX_SOUL 로 시작 SOUL 을 올릴 수 있다 — 어빌리티·상태이상을 시험할 때 쓴다.
            unit.Soul = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_SOUL"), out int soul) ? Math.Min(unit.MaxSoul, soul) : host._db.SoulStart;
            // 모드 > 전투 시작 시 소울 가득 — 내 편(편 4)만 SOUL 을 최대로 채우고 시작한다(원본에 없는 편의 기능, 사용자 요청).
            if (host._fullSoulAtStart && unit.PlayerControlled) unit.Soul = unit.MaxSoul;
            // 파트1 소울 게이지는 0~150, 처음 40(분석-파트1-전투 11.2, 0x10034f80).
            if (host._scene.IsPart1) { unit.MaxSoul = 150; unit.Soul = host._fullSoulAtStart && unit.PlayerControlled ? 150 : Math.Min(150, soul > 0 ? soul : 40); }
            // DUELDX_AILMENT=<번호>[:<값>][,<번호>[:<값>]…] 이면 그 상태이상들을 칸 순서대로 걸고 시작한다(화면 밖 시험용).
            if (Environment.GetEnvironmentVariable("DUELDX_AILMENT") is { Length: > 0 } spec)
            {
                var wanted = spec.Split(',', StringSplitOptions.RemoveEmptyEntries);
                for (int s = 0; s < wanted.Length && s < 3; s++)
                {
                    string[] parts = wanted[s].Split(':');
                    if (!byte.TryParse(parts[0], out byte id)) continue;
                    unit.StatusId[s] = id;
                    unit.StatusValue[s] = parts.Length > 1 && short.TryParse(parts[1], out short v2) ? v2 : (short)10;
                }
            }
            // 차례 깃발도 0 으로 시작한다(0x1007196c) — 틱이 흘러 TP 가 가득 차야 차례가 온다.
            unit.HasTurn = false;
            if (Trace && host._scene.IsPart1 && unit.IsAlly)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"part1 abilities btl {host._scene.Id} chr {unit.ChrCode} {host._db.T(c.NameId)} side {unit.Side} carried {carried != null}: [{string.Join(",", (unit.Data ?? c).Abilities.Select(a => $"{a.Ability}:{a.Level}"))}]" + Environment.NewLine);
        }
        // 군단 부하는 LP 게터가 늘 대장 세력 × For.dat 보정을 더한다(0x1007ac70) — 위에서는 보정 없이 셌으므로 다시 세고 가득 채운다.
        // 전에는 레벨업·대장 교체 때만 붙어 가이아 버그즈 부하(LP +20)가 보정 없이 시작했다.
        foreach (var unit in host._units)
            if (unit.LeaderIndex >= 0 && unit.Data != null) { host.StatusScr.RefreshUnitStats(unit); unit.Hp = unit.MaxHp; }
        // 챕터 스크립트가 가방을 채웠으면 데모용 아이템은 안 넣는다 — 자료가 준 것이 옳다.
        if (host.Fld._chapterFired.Count == 0) host.StatusScr.FillDemoInventory();
        GrantArenaAbility();
    }

    /// <summary>실험판(DUELDX_ARENA) — 내 인물에게 실험할 어빌리티를 그 레벨로 쥐여 주고 SOUL·TP 를 채운다.</summary>
    internal void GrantArenaAbility()
    {
        if (GameWindow.Arena is not { } arena) return;
        foreach (var u in host._units.Where(u => u.Side == 4 && u.LeaderIndex < 0 && u.Data != null))
        {
            // 실험할 어빌리티 하나만 — 다른 것은 뺀다(사용자 요청).
            u.Data = u.Data! with { Abilities = [((ushort)arena.Ability, (ushort)arena.Level)] };
            u.Soul = u.MaxSoul;
            u.Tp = u.MaxTp;
        }
    }

    internal void UpdateTurn()
    {
        if (host._loading || host._db == null) return;
        if (StepSkillDemo()) return;             // 어빌리티 시연 — 판이 서기를 기다리거나 끝나서 되돌리는 틀(SkillDemo.cs)
        StepDelayedHits();                       // 포탑 사격처럼 나중에 맞는 타격
        if (_outcome.Length > 0)
        {
            // 조용한 결과(행동 10·6·11[1])는 클릭을 기다리지 않고 제때 넘어간다(원본 120틱).
            if (_outcomeQuiet && host._lastTime >= _outcomeLeaveAt && !host.Mos._mosesOpen && !host.FieldOpen && !host.EpisodesScr._episodesOpen) host.Btl.LeaveFinishedBattle();
            return;
        }
        if (_deployOpen) { StepDeploy(); return; }     // 캐릭터 배치 중에는 틱·차례·이벤트가 멈춘다(원본 상태 2)
        // 이벤트(대사)가 도는 동안은 틱도 차례도 안 흐른다(0x10066197).
        if (EventsBusy) return;
        RunEvents();                 // 틱이 안 흐르는 사이에도 조건(턴 수 따위)은 본다
        if (EventsBusy) return;
        // 자동 저장(슬롯 20) — 그 틱 이벤트 검사가 끝나고 이벤트가 안 돌 때, 아직 그 내 차례의 조종 상태면(상태 22 첫 진입, 감사5 S6).
        if (_autoSaveFor >= 0)
        {
            if (_autoSaveFor != _turn) _autoSaveFor = -1;          // 그새 차례가 넘어갔다 — 원본도 깃발을 저장 없이 지운다
            else if (IsPlayerTurn && !host._units[_turn].IsBusy) { _autoSaveFor = -1; host.SlotsScr.AutoSave(); }
        }
        // DUELDX_WIN=1 이면 시작하자마자 이긴 것으로 친다 — 전투 이어짐·진행 깃발·모세스 전환을 화면 밖에서 시험할 때 쓴다.
        if (Environment.GetEnvironmentVariable("DUELDX_WIN") == "1")
        {
            foreach (var u in host._units.Where(u => !u.IsAlly)) u.Alive = false;
            CheckOutcome();
            return;
        }
        // DUELDX_WIN=2 면 승리 조건을 안 보고 곧장 이긴 것으로 나간다 — 이벤트로만 이기는 판(전멸이 승리가 아닌 판)에서 모세스 복귀를 시험할 때.
        // DUELDX_WIN=3 이면 곧장 진 것으로 친다(배너 있는 패배 — 파트1 「Game Over」 띠를 화면 밖에서 볼 때).
        if (Environment.GetEnvironmentVariable("DUELDX_WIN") == "3" && host._battleLoaded && !host.Mos._mosesOpen)
        {
            SetEventOutcome(win: false);
            return;
        }
        if (Environment.GetEnvironmentVariable("DUELDX_WIN") == "2" && host._battleLoaded && !host.Mos._mosesOpen)
        {
            _outcome = "승리";
            LeaveFinishedBattleNow();
            return;
        }
        if (UpdateLevelUp()) return;   // 레벨업 창이 떠 있는 동안은 차례가 멈춘다

        if (_routine != null)
        {
            if (!_routine.MoveNext())
            {
                // 행동이 끝났다 — 갈래 2 검사. 휴식·물체 만지기처럼 UseWorkRoutine 을 안 거치는 행동도 상태 21 을 지난다(0x100681a0 → 갈래 1).
                _routine = null;
                _eventCheckDue |= (1 << 2) | (1 << 1);
                // 물체 차례(ObjectTurns)는 유닛의 행동 끝이 아니다 — 쏜 틱에만 자동 회복이 한 번 더 돌지 않게.
                if (_objectRoutine) _objectRoutine = false;
                else AutoHealAll();   // 8(자동 회복)도 행동 끝마다 전원에게(ba-20 C1)
                QueueLevelUps();
            }
            return;
        }

        if (_turn >= 0)
        {
            var u = host._units[_turn];
            if (!u.Alive || !u.OnField) EndTurn();   // 사건이 지금 차례 유닛을 판에서 빼면 차례를 버린다(0x100661b2, ba-20 V10)
            else if (IsMine(u) && !u.IsBusy && u.Tp <= 0 && !_abilityMenu && !_itemMenu && _targetWork < 0) Rest(_turn);
            return;
        }
        if (StepAilmentTicks()) return;          // 매 턴 피해 — 유닛마다 카메라가 선 뒤(감사4 C17)

        // 불러온 판은 저장했던 인물의 차례로 곧장 돌아간다 — 원본도 판을 읽은 뒤 Active 유닛(+0x4cf4)의 상태 22 로 선다
        // (0x100619c0, 분석-시스템메뉴 2.4b). 틱을 흘려 다시 고르면 같은 틱에 TP 가 찬 앞 번호 적이 먼저 움직였다.
        if (_resumeTurn >= 0)
        {
            int r = _resumeTurn;
            _resumeTurn = -1;
            if (r < host._units.Length && host._units[r] is { Alive: true, OnField: true, HasTurn: true } ru && ru.LeaderIndex < 0 && CanTakeTurn(ru))
            {
                StartTurn(r, resume: true);
                return;
            }
        }

        if (host._lastTime < _nextTickAt) return;
        for (int guard = 0; guard < 10000; guard++)
        {
            int next = Array.FindIndex(host._units, u => u.Alive && u.OnField && u.HasTurn && u.LeaderIndex < 0 && CanTakeTurn(u));
            // 파트1 은 같은 틱 안에서 편 차례로 돈다(편 4 → 3 → 0 → 1 → 2, 0x10031d20) — 그 편 안에서는 배열 차례.
            if (next >= 0 && host._scene.IsPart1)
                next = Enumerable.Range(0, host._units.Length)
                    .Where(i => host._units[i] is { Alive: true, OnField: true, HasTurn: true, LeaderIndex: < 0 } u && CanTakeTurn(u))
                    .OrderBy(i => host._units[i].Side switch { 4 => 0, 3 => 1, 0 => 2, 1 => 3, 2 => 4, _ => 5 }).ThenBy(i => i).First();
            if (next >= 0) { StartTurn(next); return; }
            // 물체는 그 틱의 유닛 차례가 <b>모두 끝난 뒤</b>에야 움직인다 — 유닛 고르기(0x1006db20)가 −1 일 때만 물체 고르기(0x1006dbf0)로 간다
            // (GETNEXT 0x10067d0c, ba-15 Q6). 전에는 틱을 올리자마자 유닛보다 먼저 쐈다.
            if (_objectsDue)
            {
                _objectsDue = false;
                // 쏠 상대가 있는 물체가 있으면 하나씩 카메라·모션을 돌며 쏜다(ObjectTurns). 없으면 전처럼 그 틀에 끝낸다(충전·빈 차례).
                if (Objects.Any(obj => obj.Alive && !_opened.Contains(obj) && obj.Data.Acts && obj.Data.Kind != 10 && !(obj.Data.Kind == 9 && obj.Team < 0)
                                       && ObjectInBounds(obj) && _tick % Math.Max(1, obj.Data.TurnEvery) == 0 && ObjectHasTarget(obj)))
                {
                    _routine = ObjectTurns();
                    _objectRoutine = true;
                    return;
                }
                StepObjects();
                SweepTickDeaths();
                if (_outcome.Length > 0 || EventsBusy) return;
                continue;
            }
            // 파트1 — 불붙는 · 부서지는 동작이 도는 맵 물체가 있으면 시간 틱을 올리지 않는다(+0xc9b8, 0x10030075).
            if (host._scene.IsPart1 && _part1Objects?.Busy == true) return;
            AdvanceTick();
            for (int g = 0; g < 8 && _events.Count > 0 && _eventCheckDue != 0 && !EventsBusy && _outcome.Length == 0; g++) RunEvents();
            if (_outcome.Length > 0 || EventsBusy) return;   // 틱 사건이 다 돈 뒤에 유닛을 고른다(GETNEXT 0x10067d36~0x10067d64, ba-20 V3)
            if (_ailmentTickQueue.Count > 0) return;   // 매 턴 피해를 먼저 — 다음 틀부터 StepAilmentTicks 가 한 명씩
            // 원본은 논리 한 걸음(33ms)에 전투 틱을 많아야 하나 올린다 — 전투 걸음 0x10066070 은 상태 핸들러를 한 번만 부르고,
            // GETNEXT(0x10067cdc)는 유닛이 없으면 틱++ 뒤 그냥 돌아간다(ba-20 W). 빈 틱이 초당 30개로 눈에 보이게 흐른다.
            // 전에는 한 틀 안에서 차례가 나올 때까지 몰아 올려 차례 사이 쉼((최대TP−TP)/STP 틱)이 없었다.
            _nextTickAt = Math.Max(_nextTickAt, host._lastTime - 1.0 / TicksPerSecond) + 1.0 / TicksPerSecond;   // 누적 — 틀 시간에 물려 처지지 않게
            return;
        }
    }

    /// <summary>
    /// 아군 레벨 상위 셋의 평균 — 원본이 적 레벨을 맞추는 기준(분석-전투 「Lev.dat 성장」).
    /// </summary>
    internal int PartyLevel()
    {
        // 원본은 플레이어 <b>부대원 명부</b>의 상위 셋이다(0x1004e070) — 명부가 있으면 그것으로(편 3 손님은 안 센다).
        if (host.Mos._members.Count > 0)
        {
            var roster = host.Mos._members.Select(chr => (int)(host._party.GetValueOrDefault(chr)?.Level ?? host._db?.Character(chr)?.Level ?? 0))
                                 .Where(v => v > 0).OrderByDescending(v => v).Take(3).ToList();
            if (roster.Count > 0) return roster.Sum() / roster.Count;
        }
        var levels = host._units.Where(u => u.IsAlly)
                           .Select(u => host._party.TryGetValue(u.ChrCode, out var carried) ? carried.Level
                                      : host._db?.Character(u.ChrCode)?.Level ?? 0)
                           .Select(v => (int)v)
                           .Where(v => v > 0)
                           .OrderByDescending(v => v).Take(3).ToList();
        return levels.Count == 0 ? 1 : levels.Sum() / levels.Count;
    }

    /// <summary>
    /// 지금 파티 부대원 레벨 상위 셋의 평균(<c>0x1004e070</c>) — 전투 밖(챕터·필드 스크립트)에서 쓴다.
    /// <see cref="PartyLevel"/> 은 전장의 아군을 세므로, 모세스에서 돌면 지난 전투의 판이나 빈 판을 센다.
    /// </summary>
    internal int RosterLevel()
    {
        var levels = host.Mos._members.Select(chr => (int)((host._units.FirstOrDefault(u => u.ChrCode == chr)?.Data ?? host._party.GetValueOrDefault(chr))?.Level ?? 0))
                             .Where(v => v > 0).OrderByDescending(v => v).Take(3).ToList();
        return levels.Count == 0 ? PartyLevel() : levels.Sum() / levels.Count;
    }

    /// <summary>
    /// 그 인물을 <paramref name="offset"/> + 파티 레벨로 키운다 — <b>늘 <c>.chr</c> 원본에서</b> 다시 셈하므로 쌓이지 않고,
    /// <b>TP 제수와 CTP 는 그대로</b> 둔다. 면제 명단에 있으면 그대로 돌려준다.
    /// </summary>
    /// <param name="ignoreExempt">
    /// 스크립트 805(레벨 맞추기, <c>0x10031a50</c>)는 면제 명단을 안 본다 — 면제 명단은 전투 유닛을 만들 때(<c>0x1007a8e0</c>)만 쓴다.
    /// 805 가 겨누는 인물은 <b>모두</b> 면제 명단에 있는 합류 인물이라, 명단을 보면 805 가 한 번도 안 먹어 리엔·유진이 Lv1 로 합류했다(사용자 보고).
    /// </param>
    internal CharacterData GrowToPartyLevel(CharacterData c, int offset, int partyLevel, bool ignoreExempt = false)
    {
        if (host._db is null || (!ignoreExempt && host._db.LevelExempt.Contains(c.Code))) return c;
        var rows = host._db.LevelGrowth;
        if (rows.Count == 0) return c;

        int level = Math.Max(1, offset + partyLevel);
        var g = rows[Math.Min(level, rows.Count) - 1];
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"grow chr {c.Code}: Lv {level} (파티 {partyLevel} + 보정 {offset}) LP {c.Lp} +{g.Lp}% PSY {c.Psy} +{g.Psy}%" + Environment.NewLine);
        int Grow(int v, int percent) => v + v * percent / 100;

        return c with
        {
            Level = (ushort)level,
            CumExp = level * 100,
            Lp = (uint)Grow((int)c.Lp, g.Lp),
            Tp = (ushort)Grow(c.Tp, g.Tp),
            Psy = (ushort)Grow(c.Psy, g.Psy),
            Dex = (ushort)Grow(c.Dex, g.Dex),
            Dep = (ushort)Grow(c.Dep, g.Dep),
        };
    }

    /// <summary>이번 틱에 물체 깃발이 섰나(0x1006dab0) — 유닛 차례가 다 끝나면 <see cref="StepObjects"/> 가 돈다(Q6).</summary>
    internal bool _objectsDue;

    internal void AdvanceTick()
    {
        _tick++;
        var turnStarts = new HashSet<UnitState>();
        // 틱 처리 차례(0x10067d36~): 틱++ → 스크립트 타이머 → 전 유닛 vt+0xb0(0x10071f10: TP 회복 → 6·5 풀림 굴림) → 유닛 깃발(0x1006da40)
        // → 물체 깃발(0x1006dab0) → 갈래 3 검사 → 상태 16(매 턴 피해). 풀림 굴림은 깃발 <b>앞</b>이다(ba-15 Q4·Q6).
        // 켜 둔 이벤트 타이머는 틱마다 하나씩 센다(0x10067d3c — 틱을 올린 바로 다음 줄). 갈래 3 이벤트 검사도 이때다.
        for (int i = 0; i < _eventTimer.Length; i++) if (_eventTimerRun[i]) _eventTimer[i]++;
        // 파트1 판은 차례가 고정 간격이다(분석-파트1-전투 11.1, 0x10030060) — 시간 틱이 그 인물의 행동 간격(WTP, 인물 칸 28)으로 나누어떨어질 때
        // 차례가 온다. TP 게이지는 안 차고, 차례가 오는 틱에 가득 채운다(이동·행동 예산은 공용 틀 그대로).
        bool part1 = host._scene.IsPart1;
        foreach (var u in host._units.Where(u => u.Alive))
        {
            if (part1)
            {
                ReleaseFreeze(u);
                RollPart1Recovery(u, (int)(u.Data?.TpDivisor ?? 1));
                TickPart1Buffs(u);
                if (!u.HasTurn && CanTakeTurn(u) && _tick % Math.Max(1, (int)(u.Data?.TpDivisor ?? 1)) == 0) u.Tp = u.MaxTp;
                continue;
            }
            // 원본(0x10071db0)은 STP 와 턴 속도(38)를 <b>먼저 다 더하고</b> 한 번만 자른 뒤 차례 깃발을 세운다 —
            // 나중에 더하면 38 이 양수일 때 차례가 한 틱 늦는다. 아래쪽 0 자르기는 원본에 없다. 마비·빙결 중에도 TP 는 찬다.
            u.Tp += u.Stp;
            if (u.HasStatus(38)) u.Tp += u.Status(38);
            if (u.Tp > u.MaxTp) u.Tp = u.MaxTp;
            ReleaseFreeze(u);
        }
        // 파트1 — 유닛 틱(0x1001c400) 다음에 판의 물체에 vt+0x54 를 돌린다(0x10030097): 유닛 · 맵 물체 온도가 기온 쪽으로 가고 타는 물체 · 칸의 불이
        // 곁을 덥히며(Part1MapObjects.TimeStep), 에너지 필드의 기운이 제 칸의 기계를 굴린다.
        if (part1)
        {
            _part1Objects?.TimeStep();
            _part1Fields?.Step();
        }
        foreach (var u in host._units.Where(u => u.Alive))
        {
            // 깃발은 <b>차례를 받을 수 있을 때만</b> 선다(0x1006e940(u,4) = 0x1007c480: 마비·빙결이면 거짓) — TP 가 정확히 최대일 때(0x100743f2 sete).
            // 전에는 마비 중에도 깃발을 세워(고르기에서만 뺌) 매 턴 피해를 맞고, 자세가 영영 안 풀리기도 했다. 이미 선 깃발은 그대로.
            if (u.HasTurn || u.Tp < u.MaxTp || !CanTakeTurn(u)) continue;
            u.HasTurn = true;
            turnStarts.Add(u);
            // 방어·회피 자세는 깃발이 서는 <b>그 틱</b>에 풀린다(0x100743b0 → 0x10072d90, +0x4d4 = 0).
            u.Stance = 0;
            // 파트1 자세(+0x257~+0x25b)도 차례가 올 때 지운다(0x10036350) — 자세 몸짓(동작 21)도 서기로.
            if (part1) ClearPart1Stance(u);
        }
        _objectsDue = Objects.Count > 0;
        _eventCheckDue |= 1 << 3;
        // 매 턴 피해(상태 16 → 17 PRETURN 0x1006a791)는 걸린 유닛마다 카메라를 가운데로 보내고 멈춘 뒤에 든다(감사4 C17) —
        // 줄을 세워 두고 UpdateTurn 이 한 명씩 처리한다. 걸린 사람이 없으면 곧바로 쓰러짐을 본다.
        foreach (var u in host._units)
            if (u.Alive && turnStarts.Contains(u) && (u.Status(2) + u.Status(3) + u.Status(17) > 0 || u.Status(16) > 0 || u.Status(15) > 0))
                _ailmentTickQueue.Enqueue(u);
        if (_ailmentTickQueue.Count == 0) SweepTickDeaths();
        else _ailmentSweepDue = true;
    }

    /// <summary>매 턴 피해를 받을 차례인 유닛 — 카메라가 그 유닛에게 선 뒤 하나씩 든다.</summary>
    internal readonly Queue<UnitState> _ailmentTickQueue = new();

    /// <summary>줄 머리 유닛에게 카메라 명령을 걸었나 · 줄이 다 빠지면 쓰러짐을 봐야 하나.</summary>
    internal bool _ailmentCamSent, _ailmentSweepDue;

    /// <summary>매 턴 피해 줄을 한 걸음 — 처리 중이면 true(그동안 틱·차례는 멈춘다).</summary>
    internal bool StepAilmentTicks()
    {
        while (_ailmentTickQueue.Count > 0)
        {
            var u = _ailmentTickQueue.Peek();
            // 판이 바뀌었거나(RESTART·다른 전투) 그새 쓰러진 사람은 건너뛴다.
            if (!u.Alive || !u.OnField || Array.IndexOf(host._units, u) < 0) { _ailmentTickQueue.Dequeue(); _ailmentCamSent = false; continue; }
            if (!_ailmentCamSent) { _ailmentCamSent = true; CenterOnUnit(u); return true; }
            if (CameraBusy) return true;
            _ailmentTickQueue.Dequeue();
            _ailmentCamSent = false;
            TickAilments(new HashSet<UnitState> { u });   // 멈추면 0x2719(0x1007a360)
            return true;
        }
        if (!_ailmentSweepDue) return false;
        _ailmentSweepDue = false;
        SweepTickDeaths();
        return true;
    }

    /// <summary>
    /// 틱 처리(<c>vt+0xb0</c> = <c>0x10071f10</c>) 끝의 풀림 굴림 — <b>상태 6 먼저, 그다음 5</b>를 각각 <c>rand()%100 &lt; 3</c> 이면 지운다
    /// (<c>0x10071f19~0x10071f70</c>, <c>0x1007c470</c>). TP 회복 뒤, 차례 깃발 앞이라 풀린 그 틱에 TP 가 가득이면 곧바로 깃발이 선다.
    /// </summary>
    internal void ReleaseFreeze(UnitState u)
    {
        foreach (int id in (ReadOnlySpan<int>)[6, 5])
        {
            if (!u.HasStatus(id) || _ailmentRandom.Next(100) >= 3) continue;
            for (int i = 0; i < 3; i++)
                if (u.StatusId[i] == id) (u.StatusId[i], u.StatusValue[i], u.StatusSource[i]) = (0, 0, null);
        }
    }

    /// <summary>틱·물체 차례 뒤 — HP 0 이나 22·23·24(SOUL·TP 가 조건에 닿으면, 0x1007c689~)로 쓰러진 인물을 치우고 승패를 본다.</summary>
    internal void SweepTickDeaths()
    {
        foreach (var u in host._units.Where(u => u.Alive && u.OnField && (u.Hp <= 0 || DiesByStatus(u))))   // 판 밖(201 로 물러난 HP 0 보스)은 안 죽는다
            if (!SurvivesFatal(u))
            {
                CenterOnUnit(u);                  // 쓰러지는 유닛마다 가운데 — 마지막이 이긴다(0x1006a8b1, 감사4 C17)
                GainAilmentKillExp(u);
                KillUnit(u);
            }
        CheckOutcome();
    }

    /// <summary>차례 시작 — 그 인물을 고른다(fg-8). 적이면 AI 를 돌린다(fg-7).</summary>
    /// <summary>불러온 뒤 이어 받을 차례 — 저장할 때 차례였던 인물. 없으면 −1.</summary>
    internal int _resumeTurn = -1;

    /// <summary>자동 저장을 걸어 둔 내 차례(원본 깃발 +0x4cd8) — 이벤트 검사 뒤 UpdateTurn 이 적는다. 없으면 −1(감사5 S6).</summary>
    internal int _autoSaveFor = -1;

    /// <param name="resume">
    /// 불러온 판의 차례를 이어 받는 것 — 턴 수만 맞추고(저장 때 하나 빼 둔 것), 이벤트 타이머·자동 회복은 이미 그 차례에 돌았으니 다시 안 돌린다.
    /// 원본도 판을 읽으면 차례 시작 처리 없이 상태 22 로 선다(0x100619c0).
    /// </param>
    /// <summary>차례를 기다리는(머리 위 ▼) 내 인물들 — 자리 번호 차례.</summary>
    internal List<int> ReadyUnits() =>
        [.. Enumerable.Range(0, host._units.Length).Where(i => host._units[i] is { Alive: true, OnField: true, HasTurn: true, LeaderIndex: < 0 } u && IsMine(u) && CanTakeTurn(u))];

    /// <summary>
    /// 패드 LB · RB — 차례를 기다리는 다른 내 인물(▼)에게 <b>지금 차례를 넘긴다</b>(사용자 요청 fa-46, 원본에 없다). 지금 인물이 아직 아무것도
    /// 안 했을 때(원래 칸 · 바쁘지 않음)만 — 지금 인물의 차례는 남아 있어 다시 돌아온다. 넘길 인물이 없으면 알리고 false.
    /// </summary>
    internal bool CycleReadyUnit(int step)
    {
        var ready = ReadyUnits();
        if (!IsPlayerTurn) { host.Toast("내 차례가 아닙니다"); return false; }
        int at = ready.IndexOf(_turn);
        var others = ready.Where(i => i != _turn).ToList();
        if (others.Count == 0) { host.Toast("차례를 기다리는 다른 인물이 없습니다"); return false; }
        int next = at < 0 ? others[0] : ready[((at + step) % ready.Count + ready.Count) % ready.Count];
        if (next == _turn) next = others[0];
        return SwitchTurnTo(next);
    }

    /// <summary>
    /// 차례를 기다리는 다른 내 인물(▼)을 왼쪽 클릭 — 그 인물에게 지금 차례를 넘긴다. 원본 상태 22(<c>0x1006aeba</c>)는 클릭한 칸의 유닛이
    /// 내가 고를 수 있고(<c>0x100742e0</c>) 타고 있지 않고 차례 깃발(<c>+0xfe</c>, <c>0x100e71e0</c>)이 서 있으면 그 유닛을 지금 유닛(<c>+0x4ce8</c>)으로 바꾼다
    /// (사용자 지적 fa-68, 전에는 클릭이 고르기만 하고 차례는 그대로였다). 넘길 수 없으면 false — 그때는 평소처럼 고르기만 한다.
    /// </summary>
    internal bool TrySwitchTurnTo(int index)
    {
        if (!IsPlayerTurn || index == _turn || !ReadyUnits().Contains(index)) return false;
        return SwitchTurnTo(index);
    }

    /// <summary>지금 인물이 아직 아무것도 안 했으면(원래 칸 · 바쁘지 않음) 링 · 겨눔을 풀고 <paramref name="next"/> 의 차례로. 지금 인물의 차례는 남아 다시 돌아온다.</summary>
    bool SwitchTurnTo(int next)
    {
        var cur = host._units[_turn];
        if (cur.IsBusy || cur.Col != cur.OriginCol || cur.Row != cur.OriginRow) { host.Toast("움직인 뒤에는 차례를 넘길 수 없습니다"); return false; }
        CancelStep(undoMove: true);   // 링 · 겨눔을 풀고
        StartTurn(next);              // 그 인물의 차례로 — 부르는 목소리도 여기서
        return true;
    }

    internal void StartTurn(int index, bool resume = false)
    {
        _part1ProcBy = null;
        if (!resume) _part1ActedBy = null;
        // 시험용 DUELDX_P1MOUNTHP=<HP> 면 파트1 탈것의 HP 를 그 아래로 깎아 둔다(쓰러져 탄 이가 내려서는 것을 화면 밖에서 보려고).
        if (int.TryParse(Environment.GetEnvironmentVariable("DUELDX_P1MOUNTHP"), out int mountHp) && mountHp > 0)
            foreach (var mount in _part1Riders.Keys) mount.Hp = Math.Min(mount.Hp, mountHp);
        // 시험용 DUELDX_P1SQUADHP=<HP> 면 부하를 거느린 대장의 HP 를 그 아래로 깎아 둔다(대장이 쓰러져 새 대장을 뽑는 것을 화면 밖에서 보려고).
        if (int.TryParse(Environment.GetEnvironmentVariable("DUELDX_P1SQUADHP"), out int squadHp) && squadHp > 0)
            for (int i = 0; i < host._units.Length; i++)
                if (host._units[i] is { Alive: true, LeaderIndex: < 0 } lead && FollowersOf(i).Count > 0) lead.Hp = Math.Min(lead.Hp, squadHp);
        // 파트1 — 차례가 올 때 소울 게이지 +20(150 에서 자름, 0x10034f80).
        if (host._scene.IsPart1 && !resume && index >= 0 && index < host._units.Length)
        {
            var p1u = host._units[index];
            p1u.Soul = Part1Combat.GaugeOnTurn(p1u.Soul, cap: p1u.MaxSoul);
            if (Part1.Has(p1u, 15)) p1u.Soul = Part1Combat.GaugeOnTurn(p1u.Soul, cap: p1u.MaxSoul);   // 블래스 — 오를 때마다 +20 더
            CapPart1Soul(p1u);
            // 장비 보정 400 「매 턴 HP 회복」(천사의 목걸이 +100)은 HP 에, 401 「소울치 추가 회복」(사이킥 써크렛)은 소울에 더한다 —
            // HP 는 최대에서, 소울은 150 에서 자른다(0x10036433 · 0x10036479). 전에는 둘 다 안 들었다(카페 제보).
            if (Part1.Fighter(p1u) is { } gear)
            {
                int hpUp = gear.Equipment.Sum(i => i?.Bonus(400) ?? 0), soulUp = gear.Equipment.Sum(i => i?.Bonus(401) ?? 0);
                if (hpUp != 0 && p1u.Hp > 0) p1u.Hp = Math.Min(p1u.MaxHp, p1u.Hp + hpUp);
                if (soulUp != 0) p1u.Soul = Math.Min(p1u.MaxSoul, p1u.Soul + soulUp);
                if (Trace && hpUp + soulUp != 0)
                    System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                        $"part1 turn regen chr {p1u.ChrCode} hp +{hpUp} → {p1u.Hp}/{p1u.MaxHp} soul +{soulUp} → {p1u.Soul}" + Environment.NewLine);
            }
            // 파트1 부하는 대장 차례마다 이동 1 · 행동 1 을 받는다(0x10036825: +0x1c6 = +0x1c8 = 1) — 부하 TP 를 채우고 시작 칸을 적는다
            // (휴식의 1/5 · 1/10 가름). 부하는 제 차례가 없어 틱의 TP 채우기(차례 깃발이 안 선 사람만)를 못 받는다.
            foreach (var f in FollowersOf(index))
            {
                f.Tp = f.MaxTp;
                f.OriginCol = f.Col;
                f.OriginRow = f.Row;
            }
        }
        _turn = index;
        host._selected = index;
        _turnNo++;
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"turn tick {_tick} unit {index} chr {host._units[index].ChrCode} side {host._units[index].Side} TP {host._units[index].Tp}/{host._units[index].MaxTp}" + Environment.NewLine);
        if (!resume)
        {
            // 이벤트 타이머·자세 풀기는 틱에서 한다(AdvanceTick) — 원본 [+0x4cf0] 은 빈 틱마다 오르는 시간 틱이다(0x10067d36, fg-22).
            // 기준 칸은 새 차례에만 — 이어 받는 차례는 세이브의 기준 칸(+0x4b8/+0x4ba)을 그대로 둔다. 전에는 여기서 지금 자리로 덮어
            // 걸은 뒤 저장·불러오기면 걸음 비용이 사라졌다(감사5 S2).
            host._units[index].OriginCol = host._units[index].Col;
            host._units[index].OriginRow = host._units[index].Row;
        }
        // 불러온 판의 카메라(감사5 S7) — 시작 카메라·페이드인 뒤, 되살린 차례가 서는 이 자리에서 놓는다. 카메라가 없는 옛 세이브는
        // 되살린 내 유닛을 한 번 가운데로(AI 차례는 아래 CameraThen 이 옮긴다).
        if (host.Sys._loadCameraPending)
        {
            host.Sys._loadCameraPending = false;
            if (host.Sys._loadCamera is { } cam) host.Sys.ApplySavedCamera(cam);
            else if (resume && IsMine(host._units[index])) CenterOnUnit(host._units[index]);
        }
        CancelTargeting();
        host.Btl._heldMoveKeys.Clear();
        // 편 4 만 내가 움직인다. 편 3(동맹 AI)과 적은 같은 AI 로 스스로 움직인다(ba-6·ba-11).
        // 버서커(상태 4)라도 조종권은 그대로다 — 원본은 편 판정(0x1006fde0)에서 「모두가 적」이 될 뿐, +0x78·+0x4e9·+0x4ec 를 안 건드리고
        // WAITNEXT 도 +0x78==4 만 본다(ba-14 A4 확정). 전에는 사용자 보고를 근거로 AI 에게 넘겼다.
        // 「누구 차례」 알림은 안 띄운다(사용자 요청) — 머리줄에 이미 나오고, 무엇보다 <b>같은 알림 칸</b>이라
        // 상자에서 얻은 것 같은 결과 알림을 곧바로 덮어써 못 읽게 했다. 차례는 부르는 목소리로 알린다 — AI·동맹 차례도(상태 8).
        PlayTurnCall(host._units[index]);
        // 실험판 — 내 차례마다 SOUL 을 다시 채운다(되풀이해 써 볼 수 있게).
        if (GameWindow.Arena != null && IsMine(host._units[index])) host._units[index].Soul = host._units[index].MaxSoul;
        if (IsMine(host._units[index]))
        {
            // 자동 저장(슬롯 20) — 원본은 전투 시작·새 차례마다 깃발(+0x4cd8)을 세우고, 플레이어가 유닛을 고르는
            // 상태 22 에 처음 들어설 때 SaveGame(20) 한 뒤 지운다(0x1006acc0). AI 차례는 상태 10~12 가 카메라만 옮기고
            // 깃발을 저장 없이 지우므로 저장이 없다. 곧 「내 차례가 시작될 때마다 한 번」이다(분석-시스템메뉴 2.4).
            // 원본 순서는 틱++ → … → 갈래 3 이벤트 검사 → 상태 16 → 상태 22 첫 진입에서 SaveGame(20) 이라, 그 틱 이벤트를 다 본 뒤에 적힌다.
            // 여기서 곧장 적으면 이벤트 검사(다음 틀 RunEvents)보다 앞서므로 깃발만 세우고 UpdateTurn 이 이벤트 뒤에 적는다(감사5 S6).
            _autoSaveFor = index;
            // 내 차례가 오면 카메라가 그 인물을 가운데로 보낸다(사용자 요청 — 원본 상태 22 는 안 옮긴다). 판 끝이면 갈 수 있는 데까지만 간다.
            // 불러온 직후 세이브의 카메라를 되살린 차례는 그대로 둔다.
            if (!resume) CenterOnUnit(host._units[index]);
            _showTurnUnit = true;   // 명령이 덮이거나 못 닿아 화면 밖에 남으면 다시 보낸다(fa-53)
        }
        else
        {
            if (host._units[index].IsAlly)
                host.Toast(host._units[index].HasStatus(4) || Part1Rules.SeesAllAsFoes(host._units[index]) ? $"{host.UnitName(index)} 이(가) {(host._units[index].HasStatus(4) ? "버서커" : "혼란")} 상태라 스스로 움직입니다" : $"{host.UnitName(index)} 차례 — 동맹이 스스로 움직입니다");
            // AI 는 행동 전에 카메라가 그 인물에게 가서 선다(상태 14 CHRWORK 0x10069df0, 감사4 C4). 내 차례(상태 22)는 안 옮긴다(C3).
            _routine = CameraThen(host._units[index], AiRoutine(index));
        }
    }

    /// <summary>맞는 목소리의 재생 표지 바탕 — 표지 = 바탕 + 소리 번호. 「그 소리가 어디서든 울리는 중인가」를 번호로 본다.</summary>
    internal const int HurtVoiceTag = 30000;

    /// <summary>
    /// 맞았을 때 나는 목소리(<c>0x10079c0f</c>, 맞음 종류 2 · 피해 &gt; 0) — Dmg.dat 묶음의 맞는 소리 둘 가운데 <b>어느 것도</b> 울리는 중이 아닐 때만
    /// (<c>0x10028810</c> = 번호로 32칸 전역 슬롯 검사, 누가 낸 것이든) 유닛 <c>+0x4c &amp; 1</c> 로 고른 하나를 낸다.
    /// <c>+0x4c</c> 의 뜻은 못 가렸다(가설) — 유닛마다 늘 같은 쪽이 나게 자리 번호의 홀짝으로 대신한다. 전에는 유닛별 표지로만 막고 난수로 골랐다.
    /// </summary>
    internal void PlayHurtCry(UnitState u)
    {
        if (u.Data == null || host._voices.GetValueOrDefault(u.Data.VoiceSet).Hurt is not { Length: > 0 } hurt) return;
        if (hurt.Any(id => host._mixer.IsPlaying(HurtVoiceTag + id))) return;
        int id = hurt[((int)(host._lastTime * TicksPerSecond) & 1) % hurt.Length];   // 전투 프레임 카운터로 고른다(0x10071e83 → 0x100eaac0, ba-21 sound D8) — 전에는 인물마다 늘 같은 쪽이었다
        host.Play(id, HurtVoiceTag + id, host.SoundScreenX(host.Btl.UnitFoot(u).X));   // 비명은 그 유닛 화면 자리에서(0x10079c68~0x10079c9a, ba-20 Q S-3)
    }

    /// <summary>
    /// 차례가 왔을 때 부르는 목소리 — 원본은 AI 차례(상태 8, <c>0x10068600~0x10068698</c>)와 플레이어 고르기 상태(<c>0x100693a7~0x10069461</c>) 두 곳에서,
    /// <b>TP 가 최대일 때만</b>(<c>0x1007aef0 == 0x1007aeb0</c>) <c>call[frame &amp; 3]</c> 을 낸다. 플레이어 쪽은 <b>원래 칸에 있을 때만</b>
    /// (<c>+0x4b8/+0x4ba == +0x44/+0x46</c>) — 차례를 막 받은 지금은 늘 원래 칸이다. 전에는 내 편만, 조건 없이 냈다(ba-15 목소리).
    /// 파트1 판의 파트1 인물은 <see cref="Part1TurnCall"/> 표의 목소리를 낸다(원본에 없음 — 모드 &gt; 일반에서 끈다).
    /// </summary>
    internal void PlayTurnCall(UnitState u)
    {
        if (u.Data == null || u.Tp != u.MaxTp || (IsMine(u) && (u.Col != u.OriginCol || u.Row != u.OriginRow))) return;
        // 파트1 인물의 차례 목소리(원본에 없음, Part1TurnCall) — 내가 움직이는 인물만, 모드에서 켰을 때.
        // 캐릭터 에디터 「외형」으로 파트2 목소리 묶음을 넣었으면(VoiceSet > 0, menu-97) 그것을 파트2 처럼 낸다.
        if (host._scene.IsPart1 && u.Data.VoiceSet == 0 && Part1TurnCall.For(u) is { Length: > 0 } p1call)
        {
            if (IsMine(u) && host._part1TurnVoice) host.Play(p1call[(_tick & 3) % p1call.Length], 1000 + Array.IndexOf(host._units, u));
            return;
        }
        if (host._voices.GetValueOrDefault(u.Data.VoiceSet).Call is { Length: > 0 } call)
        {
            // 차례를 부르는 목소리는 <b>가운데에서 제 크기로</b> 낸다(사용자 보고 — 「작게 나온다」). 원본은 유닛 자리에서 내는데(0x1006866b)
            // 그 식(SetPan((x − 320) × 7))으로는 화면 가장자리의 인물이면 반대쪽이 −20 dB 로 줄어 한쪽에서만 작게 들리고,
            // 부르는 순간에 카메라가 아직 그 인물에게 안 가 있으면 화면 밖 소리로 쳐져 −15 dB 까지 더 줄었다.
            host.Play(call[(_tick & 3) % call.Length], 1000 + Array.IndexOf(host._units, u));
        }
    }

    /// <summary>패드 LB · RB 로 인물을 골랐을 때 부르는 목소리 — <see cref="PlayTurnCall"/> 과 같은 소리지만 TP · 자리 조건 없이 늘 낸다(fa-39).</summary>
    internal void PlaySelectCall(UnitState u)
    {
        if (u.Data == null || host._voices.GetValueOrDefault(u.Data.VoiceSet).Call is not { Length: > 0 } call) return;
        host.Play(call[(_tick & 3) % call.Length], 1000 + Array.IndexOf(host._units, u));
    }

    internal void EndTurn()
    {
        _eventCheckDue |= 1 << 2;                // 갈래 2 = 행동 끝(0x100680a3)
        if (_turn >= 0) { host._units[_turn].HasTurn = false; NoteActed(host._units[_turn]); }
        _turn = -1;
        _ringUnit = -1;
        _skillLeader = -1;
        CancelTargeting();
        _nextTickAt = host._lastTime + TickDelaySeconds;
    }

    /// <summary>공격·어빌리티를 열 때 걸음 비용을 빼기 전 값 — 취소하면 되돌린다.</summary>
    internal (int Tp, int OriginCol, int OriginRow)? _commitUndo;

    /// <summary>공격 대상 고르기·어빌리티 목록을 닫는다. <paramref name="refund"/> 면 열 때 뺀 걸음 비용을 돌려준다.</summary>
    internal void CancelTargeting(bool refund = false)
    {
        _part1TargetItem = 0;
        if (refund && _commitUndo is { } undo && _turn >= 0)
        {
            var u = host._units[_turn];
            (u.Tp, u.OriginCol, u.OriginRow) = undo;
        }
        _commitUndo = null;
        _attackCursor = -1;
        _attackObject = null;
        _aimCell = null;
        _targetHotkey = -1;
        _targetWork = -1;
        _targetItem = 0;
        _abilityMenu = false;
        _itemMenu = false;
    }

    /// <summary>공격·어빌리티를 열 때: 지금까지 걸은 비용을 한 번에 뺀다(취소하면 되돌림).</summary>
    internal void CommitMoveForAction()
    {
        var u = host._units[_turn];
        _commitUndo = (u.Tp, u.OriginCol, u.OriginRow);
        CommitMove(u);
    }

    /// <summary>Esc 로 걸은 것을 물린다 — 차례 시작 자리로 되돌린다. 물렸으면 true.</summary>
    internal bool UndoMove()
    {
        if (!IsPlayerTurn) return false;
        var u = host._units[_turn];
        if (u.IsBusy || (u.Col == u.OriginCol && u.Row == u.OriginRow)) return false;
        u.WarpTo(u.OriginCol, u.OriginRow);
        return true;
    }

    /// <summary>
    /// 취소 — 목록·대상 고르기면 비용을 돌려주고 링을 다시 연다. <paramref name="undoMove"/> 면(Esc) 그다음 걸음도 물린다.
    /// 우클릭은 걸음을 물리지 않는다. 취소한 것이 있으면 true.
    /// </summary>
    internal bool CancelStep(bool undoMove)
    {
        // 아이템 목록도 우클릭·Esc 로 닫힌다(목록 창 0x100d3830 — 우클릭 = 취소, ba-20 G1). 전에는 Esc 가 시스템 메뉴를 열었다.
        if (_itemMenu) { _itemMenu = false; CancelTargeting(refund: true); if (IsPlayerTurn) OpenRing(_turn, reopen: true); return true; }
        if (_abilityMenu || _targetWork >= 0) { CancelTargeting(refund: true); OpenRing(_turn, reopen: true); return true; }
        return undoMove && UndoMove();
    }

    /// <summary>차례 시작 자리에서 지금 자리까지 걸은 비용을 TP 에서 한 번에 빼고, 시작 자리를 지금 자리로 옮긴다.</summary>
    internal void CommitMove(UnitState u)
    {
        if (ComputeRange(u) is { } range && range.CanReach(u.Row * host.Cols + u.Col)) u.Tp -= range.Cost[u.Row * host.Cols + u.Col];
        u.OriginCol = u.Col;
        u.OriginRow = u.Row;
    }

    /// <summary>휴식 <c>0x1007a790</c>: (최대 HP − 현재 HP) × 남은 TP / 최대 TP × Num[35]% 를 채우고 남은 TP 를 버린다.</summary>
    internal void Rest(int index)
    {
        var u = host._units[index];
        // 파트1 — 차례 시작 칸 그대로이고 아직 아무것도 안 했나(0x1003e5c0~: 이동 · 행동이 다 남고 칸이 같다). CommitMove 가 시작 칸을 옮기기 전에 본다.
        bool part1Stayed = u.Col == u.OriginCol && u.Row == u.OriginRow && _part1ActedBy != u;
        CommitMove(u);
        // TP 를 다 쓰고(≤ 0) 끝나는 차례는 휴식이 아니다 — 원본은 깃발만 내리고(0x10071e20 → 0x10074400) 자동 휴식(WAITNEXT 0x10067a7a)에
        // 오지 않아 <b>음수 TP(빚)가 그대로 남는다</b>. 다음 차례가 그만큼 늦다(ba-20 K1). 전에는 늘 0 으로 올려 줘 연속 행동이 공짜였다.
        if (u.Tp <= 0 && !FollowersOf(index).Any(f => f.Tp > 0))
        {
            AutoHealAll();
            EndTurn();
            return;
        }
        RestFollowers(index);        // 대장이 쉬면 부하도 먼저 쉰다(0x1005f62a, 감사3 L3)
        // 파트1 휴식(0x1003e580, 분석-파트1-전투 24절) — 안 움직였으면 잃은 HP 의 1/5, 움직였거나 이미 했으면 1/10. 소울은 그대로.
        // 안 움직였으면 그다음 자세 하나(76 → 80 가운데 가진 첫째)를 켠다 — 대장 · 탄 이(탈것)는 빼고(0x1003e6ca). AI 도 쉬면 같다.
        if (host._scene.IsPart1)
        {
            // 차례 끝 대기(0x10030164 → 0x10036350) — 안 움직이고 안 했으면 선 칸의 맵 물체에 알림 2(· 어빌리티 63 이면 5): 문 곁에서 쉬면 문이 열린다.
            if (part1Stayed) Part1Objects.WaitTurnEnd(u);
            int gain = (u.MaxHp - u.Hp) / (part1Stayed ? 5 : 10);
            if (gain > 0 && !u.HasStatus(26))
            {
                int before = u.Hp;
                u.Hp += gain;
                ShowNumber(u, host._db?.T(159) ?? "HP", HealColor2, rise: false, count: (before, u.Hp));
            }
            if (part1Stayed && FollowersOf(index).Count == 0 && !_part1Riders.ContainsKey(u) && !_part1Riders.ContainsValue(u)
                && Part1.StanceAbility(u) is > 0 and var stance)
                EnterPart1Stance(u, stance);
        }
        else if (u.Tp > 0 && u.MaxTp > 0 && host._db != null && !u.HasStatus(26))
        {
            int heal = (int)((long)(u.MaxHp - u.Hp) * u.Tp / u.MaxTp * host._db.N(35) / 100);
            if (heal > 0)
            {
                int before = u.Hp;
                u.Hp += heal;
                ShowNumber(u, host._db.T(159), HealColor2, rise: false, count: (before, u.Hp));
            }
        }
        u.Tp = 0;   // 부하 TP 가 남아 대장이 자동 휴식에 오면 대장의 빚도 지워진다(0x1007a790 → 0x10071e20, ba-20 M4)
        AutoHealAll();   // 휴식도 행동 끝(상태 20)을 지난다 — 8(자동 회복)
        EndTurn();
    }

    // ── 걷기 ─────────────────────────────────────────────────────────────────

    /// <summary>차례인 아군을 클릭한 파란 칸까지 걷게 한다. TP 는 행동할 때 한 번에 뺀다.</summary>
    internal bool TryWalkTo(int col, int row)
    {
        var u = host._units[_turn >= 0 ? _turn : 0];
        if (!IsPlayerTurn || u.IsBusy || ComputeRange(u) is not { } range) return false;
        if (PathWithin(range, u.Col, u.Row, row * host.Cols + col) is not { Count: > 0 } path) return false;
        foreach (var cell in path) u.Path.Enqueue(cell);
        return true;
    }

    // ── work 사거리·효과 범위 ────────────────────────────────────────────────

    /// <summary>버프·약화 경험치 — 보조·회복 스킬이 든 대상 한 명마다 시전자가 받는 EXP(원본에 없는 규칙, 사용자 요청).</summary>
    internal const int BuffExpPerAlly = 5;

    /// <summary>이번 기술로 보조·회복이 든 대상(시전자 자신 포함) — 기술이 끝날 때 <see cref="GainBuffExp"/> 가 센다.</summary>
    internal readonly HashSet<UnitState> _buffedAllies = [];

    /// <summary>
    /// 보조(종류 2·3)가 든 대상은 편을 가리지 않고(아군 버프·적 약화 모두), 회복(1·5)은 같은 편에게 든 것만 센다. 아이템(어빌리티 0)은 뺀다.
    /// 피해 기술(종류 0)이 적에게 상태이상을 함께 건 경우는 <see cref="ApplyWork"/> 가 따로 넣는다.
    /// </summary>
    internal void MarkBuffed(UnitState a, UnitState t, WorkData w)
    {
        if (w.AbilityId == 0 || w.IsDamage) return;
        if (w.IsHeal && t.IsAlly != a.IsAlly) return;
        _buffedAllies.Add(t);
    }

    /// <summary>든 대상 한 명마다 <see cref="BuffExpPerAlly"/> 씩 — 내 편(경험치를 쌓는 쪽)만 받는다.</summary>
    internal void GainBuffExp(UnitState a)
    {
        int n = _buffedAllies.Count;
        _buffedAllies.Clear();
        if (n == 0 || !a.IsAlly || a.Data is not { } c || host._db is not { } db) return;
        int exp = db.WithEquipExp(c, BuffExpPerAlly * n);
        a.Data = c with { Exp = c.Exp + exp, CumExp = c.CumExp + exp };
        Popup(a, $"EXP +{exp}", 0xFF90D0FF, 15);
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"buff exp: {a.ChrCode} +{exp} ({n}명) → EXP {a.Data.Exp}" + Environment.NewLine);
        QueueLevelUps();
    }

    /// <summary>격려 — 아군 하나의 SOUL 을 위력만큼 올린다.</summary>
    internal const int EncourageAbility = 10;

    /// <summary>쇼크 — 대상 SOUL 을 위력만큼 깎는다(0x100ae140).</summary>
    internal const int ShockAbility = 39;

    internal WorkData? Work(int id) => host._db != null && host._db.Works.TryGetValue(id, out var w) ? w : null;

    /// <summary>work 를 쓸 수 있나 — TP + CTP 가 TP 비용 이상, SOUL 이 비용 이상.</summary>
    /// <summary>TP 와 SOUL 이 되나 — 필요 SOUL 은 체질 덧붙임까지 넣은 값이다(분석-전투 ba-4).</summary>
    /// <summary>핸들러 첫 단계에서 대상 칸으로 카메라를 보내는 어빌리티(0x100eab80 을 부르는 28개 핸들러 — ba17-camera C14 · ba-20 O P7).</summary>
    internal static readonly HashSet<int> TargetCameraAbilities =
        [31, 37, 39, 40, 42, 43, 50, 56, 79, 81, 88, 93, 97, 107, 108, 116, 126, 141, 142, 166, 169, 171, 186, 191, 192];
    // 55 익스퍼트 블레스트 · 72 화이어 웨이브는 겨눈 칸이 아니라 시전자 앞쪽으로, 그것도 핸들러 중간에 간다(0x100ae8ce · 0x1008535d) — TachyrionSkill · ElementalSkill 이 보낸다.

    /// <summary>
    /// 카메라를 겨눈 칸이 아니라 <b>시전자 앞쪽 지점</b>으로 보내는 어빌리티(gm-skil-20) — 가로로 볼 때 Side, 세로로 볼 때 Deep 만큼(화면 픽셀).
    /// 아스트럴 블레이드 0x100ab844(300 · 200, 카메라가 멈춘 뒤 시작) · 크레이지 샷 0x10093d42(120) · 이데아 캐논 0x100b679a(240 · 160) — 뒤 둘은 안 기다린다.
    /// </summary>
    internal static (int Side, int Deep, bool Wait)? ForwardCamera(int ability) => ability switch
    {
        88 => (300, 200, true),
        126 => (120, 120, false),
        171 => (240, 160, false),
        _ => null,
    };

    internal (double X, double Y) AheadPoint(UnitState a, int side, int deep)
    {
        var (x, y) = UnitFoot(a);
        return a.Facing switch { Facing.Up => (x, y - deep), Facing.Down => (x, y + deep), Facing.Left => (x - side, y), _ => (x + side, y) };
    }

    internal bool CanAfford(UnitState u, WorkData w) =>
        u.Data != null && host._db != null && u.Tp + u.Ctp >= TpCostFor(u, u.Data, w.Id) && u.Soul >= SoulNeedFor(u, u.Data, w.Id)
        && (u.Data.JobId == 37 || u.Hp >= host._db.WorkHpCost(u.Data, w.Id));   // HP 비용도 본다(0x100726e0) — 직업 37 은 면제

    /// <summary>
    /// 기본공격 자리 찾기 — 이동 영역 칸(시작 자리 포함) 중 목표가 사거리에 드는, 시작 자리에서 가장 싼 칸.
    /// 길은 지금 자리에서 그 칸까지다.
    /// </summary>
    internal (List<(int Col, int Row)> Path, int Cost)? FindAttackPath(int attackerIndex, int targetIndex)
    {
        var a = host._units[attackerIndex];
        var t = host._units[targetIndex];
        if (!a.Alive || !t.Alive || !SeesAsFoe(a, t) || a.Data == null || Work(a.Data.BasicWorkId) is not { } w || !CanAfford(a, w)) return null;
        // 이동 범위는 같은 틀 · 같은 자리 · 같은 TP 면 한 번만 센다 — 공격 대상 고르기(E)는 적마다 이것을 부르고, 커서는 틀마다 부른다.
        // 전에는 적 하나에 범위를 두 번(좁은 · 넓은 예산), 붉은 칸까지 칠해 셌다 — 사거리 긴 인물 · 적 많은 판(20089 록슬리)에서 크게 렉(사용자 보고).
        var key = (a, host._lastTime, a.Col, a.Row, a.Tp, a.Ctp);
        if (_attackRanges is not { } cache || cache.Key != key) _attackRanges = cache = new AttackRanges(key, ComputeRange(a, withRed: false));
        if (cache.Narrow is not { } range) return null;

        // 원본 0x100608e0 — 기준 칸에서 칠한 파랑(좁은 예산) 가운데 대상이 사거리에 드는 칸 중 <b>지금 선 칸에서 가장 가까운</b> 칸
        // (맨해튼, 같으면 배열 번호 순). 비용은 기준 칸 → 그 칸 한 번이다(ba-20 M2). 전에는 기준 칸에서 가장 싼 칸을 골라,
        // 미리 걸어가 자리를 잡고 적을 눌러도 싼 칸으로 되걸어갔다.
        int narrow = a.HasStatus(25) ? 0 : a.Tp + Math.Min(0, a.Ctp - TpCostFor(a, a.Data, w.Id));
        int best = -1, bestDist = int.MaxValue;
        for (int i = 0; i < range.Cost.Length; i++)
        {
            if (!range.CanReach(i) || range.Cost[i] > narrow) continue;
            int d = Math.Abs(i % host.Cols - a.Col) + Math.Abs(i / host.Cols - a.Row);
            if (d >= bestDist || !InWorkRange(w, i % host.Cols, i / host.Cols, t.Col, t.Row, a)) continue;
            bestDist = d;
            best = i;
        }
        // 길은 넓은 예산(지금 TP 전부)으로 찾는다 — 대상 고르기 중의 range 는 좁은 예산이라, 멀리 걸어가 있으면 지금 칸이 그 밖이라 길이 안 나왔다.
        if (best < 0) return null;
        var wide = cache.Wide ??= ComputeRange(a, tp: Math.Max(a.Tp, 0), withRed: false) ?? range;
        return best < 0 || (PathWithin(wide, a.Col, a.Row, best) ?? PathWithin(range, a.Col, a.Row, best)) is not { } path ? null : (path, range.Cost[best]);
    }

    /// <summary><see cref="FindAttackPath"/> 가 센 이동 범위 — (인물, 틀, 자리, TP, CTP) 가 같으면 다시 쓴다.</summary>
    private AttackRanges? _attackRanges;

    private sealed class AttackRanges((UnitState, double, int, int, int, int) key, MoveRange? narrow)
    {
        public readonly (UnitState, double, int, int, int, int) Key = key;
        public readonly MoveRange? Narrow = narrow;
        public MoveRange? Wide;
    }

    // ── 플레이어 대상 고르기 ─────────────────────────────────────────────────

    /// <summary>적을 클릭하면 링을 안 거치고 바로 친다(fa-12) — 걸음 비용도 그때 함께 뺀다.</summary>
    internal void QuickAttack(int targetIndex)
    {
        if (host._units[_turn].Data is not { } c || Work(c.BasicWorkId) is not { } w) return;
        if (FindAttackPath(_turn, targetIndex) is not { } plan)
        {
            host.Hint("공격할 수 없습니다 — 빨간 칸 안의 적을 고르세요");
            return;
        }
        // 걸음 비용은 여기서 미리 빼지 않는다 — UseWorkRoutine 의 CommitMove 가 「기준 칸 → 친 칸」을 한 번만 뺀다(ba-20 M2).
        _commitUndo = null;   // 바로 치므로 되돌릴 일이 없다
        _routine = UseWorkRoutine(_turn, w, targetIndex, host._units[targetIndex].Col, host._units[targetIndex].Row, plan.Path);
    }

    /// <summary>링 공격: 기본공격 대상 고르기(걸어가서 친다).</summary>
    internal void BeginAttackTargeting()
    {
        if (host._units[_turn].Data is not { } c) return;
        _commitUndo = null;   // 기본공격은 걸음을 미리 굳히지 않는다(ba-20 M2)
        _targetWork = c.BasicWorkId;
        _targetIsBasicAttack = true;

        // 처음 고를 대상만 센다(사용자 요청) — 지금 자리에서 칠 수 있는 적이 1순위, 없으면 가까운 적부터 걸어가 칠 수 있는지 보고 처음 걸린 적에서 멈춘다.
        // 전에는 판의 모든 적 · 물체를 걸음 비용으로 다 세어 줄을 세웠다(적 많은 판에서 E 가 렉). 다음 대상 돌리기(CycleAttackCursor)는 그때 다 센다.
        var user = host._units[_turn];
        var foes = Enumerable.Range(0, host._units.Length)
            .Where(i => host._units[i].Alive && host._units[i].OnField && SeesAsFoe(user, host._units[i]))
            .OrderBy(i => Math.Abs(host._units[i].Col - user.Col) + Math.Abs(host._units[i].Row - user.Row)).ThenBy(i => host._units[i].Hp).ThenBy(i => i)
            .ToList();
        int first = Work(c.BasicWorkId) is { } basic && CanAfford(user, basic)
            ? foes.FirstOrDefault(i => InWorkRange(basic, user.Col, user.Row, host._units[i].Col, host._units[i].Row, user), -1) : -1;
        if (first < 0) first = foes.FirstOrDefault(i => FindAttackPath(_turn, i) != null, -1);
        if (first >= 0) (_attackCursor, _attackObject) = (first, null);
        else if (AttackableObjects() is [var obj, ..]) (_attackCursor, _attackObject) = (-1, obj);
        else
        {
            CancelTargeting(refund: true);
            host.Toast("공격할 수 있는 적이 없습니다");
            return;
        }
        AttackCursorHint();
    }

    internal void AttackCursorHint()
    {
        string confirm = KeyBindings.KeyName(host._keys[KeyAction.Attack]);
        string name = _attackObject is { } o ? host._db?.T((ushort)o.Data.NameId) ?? "물체" : host.UnitName(_attackCursor);
        host.Hint($"{name} 을(를) 노립니다 — 클릭·Enter·{confirm}: 공격, Tab: 다른 적, 우클릭·Esc: 취소");
    }

    /// <summary>기본공격 커서가 도는 차례 — 칠 수 있는 적과 물체를 한데 모아 걸음 비용이 적은 순(같으면 적 먼저).</summary>
    internal List<(int Unit, DemoObject? Obj)> AttackCursorTargets()
    {
        var user = host._units[_turn];
        return [.. AttackableEnemies().Select((i, k) => (Unit: i, Obj: (DemoObject?)null, Cost: FindAttackPath(_turn, i)?.Cost ?? 0, Order: k))
                    .Concat(AttackableObjects().Select((o, k) => (Unit: -1, Obj: (DemoObject?)o, Cost: ObjectAttackCost(user, o), Order: 10000 + k)))
                    .OrderBy(t => t.Cost).ThenBy(t => t.Order)
                    .Select(t => (t.Unit, t.Obj))];
    }

    /// <summary>차례인 인물이 걸어가서 칠 수 있는 부술 수 있는 적 물체 — 걸음 비용이 적은 순.</summary>
    internal List<DemoObject> AttackableObjects()
    {
        var user = host._units[_turn];
        return [.. Objects.Where(o => o.Alive && o.Data.Breakable && ObjectInBounds(o) && ObjectHostile(o, user))
                          .Select(o => (Obj: o, Cost: ObjectAttackCost(user, o)))
                          .Where(t => t.Cost >= 0)
                          .OrderBy(t => t.Cost).Select(t => t.Obj)];
    }

    /// <summary>
    /// 차례인 인물이 지금 칠 수 있는 적 — <b>걸어갈 비용이 적은(가까운) 순</b>, 같으면 HP 낮은 순.
    /// 약한 적부터 노리면 멀리 걸어가 TP 를 헛되이 쓰기 일쑤라 가까운 적을 먼저 세운다.
    /// </summary>
    internal List<int> AttackableEnemies() =>
        [.. Enumerable.Range(0, host._units.Length)
            .Where(i => host._units[i].Alive && SeesAsFoe(host._units[_turn], host._units[i]))
            .Select(i => (Index: i, Plan: FindAttackPath(_turn, i)))
            .Where(t => t.Plan != null)
            .OrderBy(t => t.Plan!.Value.Cost).ThenBy(t => t.Plan!.Value.Path.Count)
            .ThenBy(t => host._units[t.Index].Hp).ThenBy(t => t.Index)
            .Select(t => t.Index)];

    /// <summary>공격 커서를 다음(HP 순) 적으로 옮긴다 — 어빌리티를 겨누는 중이면 그 어빌리티 사거리로 센다.</summary>
    /// <param name="step">+1 다음 적, −1 이전 적(패드 LB).</param>
    internal void CycleAttackCursor(int step = 1)
    {
        if (_targetIsBasicAttack)
        {
            // 기본공격은 적 다음에 칠 수 있는 물체까지 한 줄로 돈다.
            var all = AttackCursorTargets();
            if (all.Count == 0) return;
            int at = all.FindIndex(t => _attackObject != null ? t.Obj == _attackObject : t.Unit == _attackCursor);
            (_attackCursor, _attackObject) = all[((at + step) % all.Count + all.Count) % all.Count];
            AttackCursorHint();
            return;
        }
        var targets = _targetWork >= 0 && Work(_targetWork) is { } aw ? AbilityTargets(aw) : AttackableEnemies();
        if (targets.Count == 0) return;
        _attackCursor = targets[((targets.IndexOf(_attackCursor) + step) % targets.Count + targets.Count) % targets.Count];
    }

    /// <summary>커서의 적을 친다.</summary>
    internal void AttackCursorTarget()
    {
        if (!HasAttackCursor || !IsPlayerTurn) return;
        var (col, row) = _attackObject is { } o ? (o.Col, o.Row) : (host._units[_attackCursor].Col, host._units[_attackCursor].Row);
        OnTargetClick(col, row);
    }

    /// <summary>대상 고르는 중의 클릭. 처리했으면 true.</summary>
    internal bool OnTargetClick(int col, int row)
    {
        if (_targetWork < 0) return false;
        if (!IsPlayerTurn || Work(_targetWork) is not { } w) { CancelTargeting(); return true; }
        if (_part1TargetItem != 0) return OnPart1ItemTarget(col, row);   // 파트1 약 — 누른 아군에게 쓴다
        var user = host._units[_turn];

        if (_targetIsBasicAttack)
        {
            int target = LiveUnitAt(col, row) is { } t ? Array.IndexOf(host._units, t) : -1;
            // 유닛이 없는 칸이면 부술 수 있는 적 물체(기총포탑 따위)를 친다.
            if (target < 0 && ObjectAt(col, row) is { Data.Breakable: true })
            {
                CancelTargeting();
                if (!TryAttackObject(col, row)) host.Hint("칠 수 없는 물체입니다");
                return true;
            }
            // 칠 수 있는 상대는 편 판정으로 가른다 — 버서커(4)면 아군도 친다(0x1006fde0). 전에는 편(IsAlly)만 봐서 버서커여도 아군을 못 쳤다.
            if (target < 0 || !SeesAsFoe(user, host._units[target]) || FindAttackPath(_turn, target) is not { } plan)
            {
                host.Hint("공격할 수 없습니다 — 빨간 칸 안의 적을 고르세요 (우클릭·Esc 취소)");
                return true;
            }
            CancelTargeting();
            _routine = UseWorkRoutine(_turn, w, target, host._units[target].Col, host._units[target].Row, plan.Path);
            return true;
        }

        // 자기 자리에 쓰는 범위 기술 — 보여 준 범위(또는 제 칸)를 한 번 더 누르면 쓴다.
        if (w.SelfCentred)
        {
            if ((col, row) != (user.Col, user.Row) && !AreaCells(w, user, col, row).Contains((col, row)))
            {
                host.Hint("범위 밖입니다 — 주황 칸을 누르세요 (우클릭·Esc 취소)");
                return true;
            }
            UseSelfCentredWork(w);
            return true;
        }
        if (!InWorkRange(w, user.Col, user.Row, col, row, user))
        {
            host.Hint("사거리 밖입니다 — 노란 칸을 고르세요 (우클릭·Esc 취소)");
            return true;
        }
        // 대상 방식 3·6(아무 칸)·7(빈 칸)은 메테오처럼 <b>칸을 고르는</b> 기술이라 그 칸에 아무도 없어도 된다.
        // 7 은 시전자가 설 수 있는 빈 칸이어야 한다(파트2 0x100daca0 모드 7 · 파트1 0x1008a950 거르기 7) — 그 밖의 칸은 누르지 못한다.
        if (w.TargetMode == 7 && !CanAimAt(w, user, col, row))
        {
            host.Hint("설 수 있는 빈 칸을 고르세요 — 적 옆 칸 · 높이가 다른 칸은 안 됩니다 (우클릭·Esc 취소)");
            return true;
        }
        bool needsUnit = w.TargetMode is 1 or 4 or 5;
        // 적 물체(바리케이트·포탑 따위 부술 수 있는 것)도 대상이다 — 피해 기술은 범위 안의 적 물체를 친다(아래 판정, 0x100d9510 은 물체를 먼저 돌려준다).
        // 전에는 유닛만 세어, 기본공격으로는 칠 수 있는 물체에 기술을 쓰면 「대상이 없습니다」가 떴다(사용자 보고: Btl 0133 (1,15)).
        bool hitsObject = w.IsDamage && w.TargetMode is 1 or 5 && w.AbilityId != BlackHoleAbility
            && EffectCells(w, user, col, row).Any(c => ObjectAt(c.Item1, c.Item2) is { Data.Breakable: true, Alive: true } obj && ObjectHostile(obj, user) && !_opened.Contains(obj));
        if (needsUnit && !hitsObject && WorkTargets(w, user, col, row).Count == 0)
        {
            // 버서커(4)가 걸리면 편 판정(0x1006fde0)이 자기 자신까지 적으로 돌려줘 아군 대상 기술(힐 따위)은 자신에게도 못 쓴다 — 이유를 알려 준다
            // (사용자 질문: Btl 0141 죠안이 자신에게 힐을 못 씀). 큐어(대상 방식 5)는 된다.
            host.Toast(w.TargetMode == 4 && user.HasStatus(4) ? "버서커 상태에서는 아군 대상 기술을 쓸 수 없습니다(자신 포함) — 큐어로 풀 수 있습니다" : "그 칸에는 대상이 없습니다");
            return true;
        }
        ConsumeTargetItem();          // 아이템이면 이때 개수가 하나 준다
        CancelTargeting();
        _routine = UseWorkRoutine(_turn, w, -1, col, row, []);
        return true;
    }

    // ── work 쓰기 (fg-5 공격 · fg-6 어빌리티) ───────────────────────────────

    /// <summary>핸들러가 판정까지 대상을 붙드는 work — 브레인 스톰 · 블라인드 · 안티 밸런싱 · 미라클 · 아이템 1609·1610·1612~1617(ba-21 T3).</summary>
    internal static readonly HashSet<int> HeldTargetWorks =
        [400, 603, 604, 605, 606, 607, 608, 609, 610, 611, 481, 935, 936, 937, 938, 482, 934, 939, 940, 941, 490, 1609, 1610, 1612, 1613, 1614, 1615, 1616, 1617];

    /// <summary>
    /// (필요하면 걸어가서) work 하나를 쓴다: 걸은 비용을 한 번에 빼고, 겨눈 쪽으로 돌고, 동작 5 → 8 → 24 를 재생하며 <b>치는 순간</b>에 대상마다 판정,
    /// 쓰러진 인물은 동작 6 뒤 판에서 뺀다. TP·SOUL 비용과 SOUL 증가를 적용한다.
    /// </summary>
    /// <summary>탄이 닿을 때 맞는 work 인가 — 도구가 뽑은 목록에, 손으로 연출을 깐 블레이드 미사일을 더한다.</summary>
    internal static bool ArriveHit(WorkData w) => WorkFxExtra.ArriveHitWorks.Contains(w.Id) || BladeMissileSkill.Works.Contains(w.Id);

    internal IEnumerator<bool> UseWorkRoutine(int userIndex, WorkData w, int targetIndex, int col, int row,
                                             List<(int Col, int Row)> path, bool eventFinisher = false, bool part1Counter = false)
    {
        // 부하의 타격은 부하 제 동작의 타격 키 때 든다(PumpFollowerHits) — 매 틱 확인한다.
        var core = UseWorkRoutineCore(userIndex, w, targetIndex, col, row, path, eventFinisher, part1Counter);
        while (true)
        {
            PumpFollowerHits();
            if (!core.MoveNext()) break;
            yield return core.Current;
        }
        PumpFollowerHits(flush: true);
    }

    private IEnumerator<bool> UseWorkRoutineCore(int userIndex, WorkData w, int targetIndex, int col, int row,
                                                 List<(int Col, int Row)> path, bool eventFinisher, bool part1Counter)
    {
        var a = host._units[userIndex];
        if (!eventFinisher && userIndex == _turn) _part1ActedBy = a;   // 파트1 휴식이 「이미 했다」를 본다
        host.Stats.BeginCast(a, w);
        // 이동 + 기술이면 진형을 다시 세우지 않는다 — 원본은 계산만 하고 결과를 안 읽는다(0x1005fa90·0x1005fd00, 분석-군단).
        // 진형은 「이동만」·「이동+휴식」(0x1005f670·0x1005f870)에서만 다시 선다. 전에는 기술 앞에서 늘 다시 세웠다.
        _skillLeader = userIndex;
        // 행동 실행(상태 14) — 행동 인물을 가운데로 보내고 멈춘 뒤에 걷기·기술을 시작한다(0x10069df0, 감사4 C1·C4).
        foreach (bool _ in CenterUnitAndWait(a)) yield return true;
        foreach (var cell in path) a.Path.Enqueue(cell);
        while (a.IsBusy) yield return true;
        foreach (bool _ in WaitFollowersStopped(userIndex)) yield return true;   // 걷던 부하가 다 선 뒤(감사5 L-A)
        CommitMove(a);
        _buffedAllies.Clear();
        _soulGain = default;
        _timedCritBy = null;
        _timedAimBy = null;

        if (targetIndex >= 0) (col, row) = (host._units[targetIndex].Col, host._units[targetIndex].Row);
        if (col != a.Col || row != a.Row) a.Facing = FacingToward(a.Col, a.Row, col, row);
        if (w.Id == StanceDefendWork) a.Facing = Facing.Down;   // 방어 0x100b2d50 은 방향 2 를 박아 넣는다 — 늘 아래를 본다
        if (!w.IsDamage) Popup(a, AbilityName(w), 0xFFB0E0FF, 15);
        if (w.TintMode == 1) host._mapTintTarget = w.TintLevel;   // 기술을 쓰는 동안 맵이 물든다(0x10069efa, ba-20 P2)

        // 타이밍 크리티컬(모드 > 밸런스) — 치기 바로 앞에 단추를 띄우고, 맞춰 누르면 이 공격의 내 타격이 반드시 맞고 크리티컬이다.
        if (TimedHit.ForcedHits > 0 && !eventFinisher && a.IsAlly && w.IsDamage) _timedCritBy = a;   // 시험용
        else if (TimedHit.ForcedAimHits > 0 && !eventFinisher && a.IsAlly && host.TimedHitFx.WantedAim(a, w)) (_timedAimBy, _timedAimBonus) = (a, TimedHit.AimBonusFor(TimedHit.ForcedAimHits));   // 시험용
        else if (!eventFinisher && host.TimedHitFx.Wanted(a, w))
        {
            var aimed = targetIndex >= 0 ? host._units[targetIndex] : null;
            foreach (bool _ in host.TimedHitFx.Prompt(a, target: aimed, guide: aimed != null ? ExpectedDamageText(a, aimed, w) : null)) yield return true;
            if (host.TimedHitFx.Hits > 0) _timedCritBy = a;
        }
        // 타이밍 명중(모드 > 밸런스, fg-34) — 명중률이 있는 어빌리티 앞에 같은 띠를 띄우고, 맞춘 박자마다 이 행동의 내 명중률을 올린다.
        else if (!eventFinisher && host.TimedHitFx.WantedAim(a, w))
        {
            foreach (bool _ in host.TimedHitFx.Prompt(a, aim: true, guide: targetIndex >= 0 ? ExpectedDamageText(a, host._units[targetIndex], w, aim: true) : null)) yield return true;
            if (host.TimedHitFx.Hits > 0) (_timedAimBy, _timedAimBonus) = (a, TimedHit.AimBonusFor(host.TimedHitFx.Hits));
        }

        var dying = new List<UnitState>();
        int[] actions = ActionsFor(w);
        int hitStep = HitStepFor(w, actions.Length);
        // 파트1 인물의 기본 공격 — 빌린 파트2 work 의 동작 · 이펙트 대신 원본 기본 공격 핸들러의 연출(Part1Skill.Basic)로 친다. 피해는 그대로 Part1Rules.
        int part1Basic = Part1BasicAttack(a, w);
        if (part1Basic > 0) (actions, hitStep) = ([], 0);
        bool timedChain = false;  // 타이밍 어택이 타격 동작을 여럿 이어 붙였나 — 그러면 「연」처럼 타격 동작마다 친다
        if (_timedCritBy == a)
        {
            // 박자 둘을 다 맞췄으면 치는 동작을 「연」의 여러 번 치는 동작(13 — 타격 키 둘 · 14 — 셋)으로 바꿔 보통 피해로 여러 번 친다(사용자 요청).
            // 횟수는 2 에서 시작해 연속 성공마다 하나씩 는다 — 13 · 14 를 이어 붙여 맞춘다(「연」이 레벨마다 그러듯), 하나 남으면 원래 동작 한 번.
            // 그 동작이 없는 인물(총 따위)은 한 번 치고 피해를 횟수만큼 곱한다. 하나만 맞췄으면 한 번 치되 크리티컬이다(배율 0 = 크리티컬로 표시).
            int want = host.TimedHitFx.AttackCount;   // 맞춘 박자 수 — 하나면 한 번을 크리티컬로
            bool both = want >= 2;
            bool twice = both && hitStep < actions.Length && actions[hitStep] is 8 or 9 && HitTimesFor(a, 13).Count >= 2;
            if (twice)
            {
                bool triple = HitTimesFor(a, 14).Count >= 3;
                var chain = new List<int>();
                for (int left = want; left > 0;)
                {
                    if (triple && (left == 3 || left >= 5)) { chain.Add(14); left -= 3; }
                    else if (left >= 2) { chain.Add(13); left -= 2; }
                    else { chain.Add(actions[hitStep]); left--; }
                }
                actions = [.. actions[..hitStep], .. chain, .. actions[(hitStep + 1)..]];
                timedChain = chain.Count > 1;
            }
            _timedPercent = !both ? 0 : twice ? 100 : Math.Max(100, host.TimedHitFx.Cfg.DamagePercent) * want / 2;
        }
        bool chained = timedChain || ScriptFor(w.Id) is { Actions.Length: > 0 };
        bool effectsDone = false, followersDone = false, objectsDone = false;
        // 범위 안의 적 물체(포탑·바리케이트)도 맞는다(0x100d9510 은 물체를 먼저 돌려준다) — 피해량은 기본공격과 같은 식(가설).
        // 유닛과 같이 <b>타마다</b> 맞는다 — 전에는 행동마다 한 번이라 「연」처럼 여러 번 치는 기술도 물체에는 한 타만 들었다(사용자 보고: Btl 0301 해골).
        // <paramref name="onlyIfNone"/> 은 보통 타격을 안 지난 갈래(전용 연출)가 끝에서 한 번 부를 때 — 이미 맞았으면 또 치지 않는다.
        void HitObjects(WorkData by, bool onlyIfNone = false)
        {
            if ((onlyIfNone && objectsDone) || !w.IsDamage || w.AbilityId == BlackHoleAbility || a.Data is not { } od) return;
            objectsDone = true;
            foreach (var (oc, or) in EffectCells(w, a, col, row))
                if (ObjectAt(oc, or) is { Data.Breakable: true, Alive: true } obj && ObjectHostile(obj, a) && !_opened.Contains(obj))
                    DamageObject(a, obj, host._db!.Atk(od, a.Soul, by.Power));
        }
        double effectsAt = host._lastTime;               // 이펙트를 띄운 때 — 핸들러 판정 틱의 기준(단계 0)
        double fxSpan = 0;                          // 그 이펙트들이 다 끝나기까지의 틱
        _followerStrikes.Clear();
        host.Mov.SpawnWorkMovies(w, a, col, row, prelude: true);     // 준비 동작의 시전 영상(불기둥 Mov 0041·0042) — 시전이 시작할 때
        // 필살기(준비 7)는 공통 앞머리(빛 알갱이·초상 컷인·금빛 띠, 0x1007e330)를 다 돈 뒤에 핸들러로 간다.
        bool finisher = w.Prepare == 7;
        // 준비 2·3·5·6 — 시전 소리 1338:1(694)은 시전 시작 +2틱(0x1007def0 단계 1). 핸들러는 동작 15 가 끝난 뒤에 돈다(ba-15 R6).
        if (w.Prepare is 2 or 3 or 5 or 6) host._pendingSounds.AddRange((host._effectTables.GetValueOrDefault(1338)?.Clips.GetValueOrDefault(1)?.Sounds ?? []).Select(s => (host._lastTime + (2 + s.Item1) / TicksPerSecond, s.Item2)));
        if (finisher) foreach (bool _ in host.FinisherPreludeAb.FinisherPrelude(w, a)) yield return true;
        // 필살기는 앞머리가 준비 동작(사슬 앞의 6·15)을 이미 했다 — <b>그 둘만</b> 건너뛴다. 전에는 타격 아닌 동작을 모두 건너뛰어
        // 오메가 스윙·더블 브레이크·이데아 캐논 따위가 마지막 동작 하나만 했다(fg-20).
        int preludeSteps = finisher ? PreludeSteps(actions) : 0;
        bool holding = false;                               // 붙드는 모션(2000+m)을 치는 칸에서 틀었나
        bool targetCentred = false;                         // 대상 칸 가운데 맞추기를 했나(치는 단계 첫 한 번)
        if (ForwardCamera(w.AbilityId) is { Wait: false } glide && a.Data?.BasicWorkId != w.Id)
        {
            // 핸들러 단계 0 이 동작을 틀면서 카메라를 앞쪽으로 보내고 기다리지 않는다(이데아 캐논은 동작 17 과 함께).
            var (gx, gy) = AheadPoint(a, glide.Side, glide.Deep);
            CenterOn(gx, gy);
            targetCentred = true;
        }
        _healPick = host._rng.Next(1 << 16);
        for (int step = 0; step < Math.Max(actions.Length, 1); step++)
        {
            // 어빌리티 사슬은 <b>타격 동작마다</b> 친다 — 「연」은 레벨이 오르면 13 → 14 → 8 처럼 타격 동작이 늘어나
            // 2·3·4·5·6타가 된다(분석-모션 ba-10). 전에는 사슬의 첫 타격 동작에서만 판정을 내 3타에서 멈췄다.
            bool strikes = step == hitStep || (chained && step < actions.Length && IsStrikeAction(actions[step]));
            if (!strikes)
            {
                if (step >= actions.Length || step < preludeSteps) continue;
                // 소닉 블레이드 · 비연참의 모으기(동작 5) — 몸 복제 둘이 옆으로 벌어지고, 비연참은 모으는 동안 아래를 본다
                // (0x1008abde · 0x10080480 face(2), gm-skil-21). 돌진할 때 겨눈 쪽으로 다시 돈다(0x10080806).
                bool charging = w.AbilityId is 19 or 20 && actions[step] == 5;
                var chargeFacing = a.Facing;
                if (charging && w.AbilityId == 19) a.Facing = Facing.Down;
                PlayChainStep(a, actions[step], HoldSeconds);
                if (charging) SpawnSideClones(a, chargeFacing, drift: true);
                if (actions[step] >= RawHold) for (double end = host._lastTime + 0.5; host._lastTime < end;) yield return true;   // 치는 칸이 아닌 붙듦(드묾)은 잠깐만
                else while (a.IsBusy) yield return true;
                if (charging) a.Facing = chargeFacing;
                continue;
            }

            // 기술은 치는 단계에 들어서면 대상 칸을 가운데로 보내고 멈출 때까지 기다린다 — 스킬 핸들러 27곳의 첫 단계
            // 0x100eab80(대상칸, 0, 0) → 0x1006e850 대기(감사4 C14). 기본공격(명령 0x2712)은 카메라 명령이 없다.
            if (!targetCentred)
            {
                targetCentred = true;
                // 대상 칸으로 카메라를 보내는 핸들러(0x100eab80 호출)는 28개뿐이다 — 나머지 기술은 카메라가 시전자에 머문다(ba-20 O P7).
                if (ForwardCamera(w.AbilityId) is { Wait: true } ahead && a.Data?.BasicWorkId != w.Id)
                {
                    var (hx, hy) = AheadPoint(a, ahead.Side, ahead.Deep);
                    foreach (bool _ in CenterAndWait(hx, hy)) yield return true;
                }
                else if (TargetCameraAbilities.Contains(w.AbilityId) && a.Data?.BasicWorkId != w.Id && (uint)col < host.Cols && (uint)row < host.Rows)
                    foreach (bool _ in CenterAndWait(col * TileW + TileW / 2, host.CellCenterY(col, row))) yield return true;
            }
            // 치는 동작은 끝까지 기다리지 않는다 — 동작이 뜨고 0.05초 뒤부터,
            // 그 모션에 든 타격 키 수(동작 13 = 2타, 14 = 3타)만큼 그 간격대로 판정을 낸다(분석-모션 ba-10).
            var hitTimes = new List<double> { 0 };
            List<int> hitAbilities = [];                   // 타격 키마다 적힌 어빌리티(hitTimes 와 같은 차례) — 비어 있으면 모두 이 work
            if (step < actions.Length && step >= preludeSteps && actions[step] >= RawOnce)
            {
                // 모션 번호 — 붙드는 모션이면 그 자세로 이펙트가 터지고, 한 번 트는 모션이면 뜨자마자 친다.
                PlayChainStep(a, actions[step], HoldSeconds);
                holding = actions[step] >= RawHold;
                for (double end = host._lastTime + 0.05; host._lastTime < end;) yield return true;
            }
            else if (step < actions.Length && step >= preludeSteps)
            {
                PlayAction(a, DrawnAction(a, actions[step]));
                if (w.AbilityId == 20 && actions[step] == 12) SpawnSideClones(a, a.Facing, drift: false);   // 소닉 블레이드 — 베는 동안 분신 둘이 ±40 에 선다(0x1008ae60)
                hitTimes = HitTimesFor(a, DrawnAction(a, actions[step]), ranged: w.RangeMax > 4);
                hitAbilities = HitAbilitiesFor(a, DrawnAction(a, actions[step]));
                // 준비 2·3·5·6 의 발동 동작 15 는 끝까지 튼 뒤에 핸들러가 돈다 — 효과·판정을 그때로(ba-15 R6).
                if (w.Prepare is 2 or 3 or 5 or 6 && actions[step] == 15 && host._sprites.TryGetValue(a.ChrCode, out var sp15))
                    (hitTimes, hitAbilities) = ([Math.Max(0.05, sp15.ActionSeconds(DrawnAction(a, 15), a.Facing))], []);
                for (double end = host._lastTime + hitTimes[0]; host._lastTime < end;) yield return true;
            }
            int effectMark = _effects.Count;

            // 「연」 사슬 끝의 동작 8 한 대는 연 위력이 아니라 기본공격 위력이다(키 인자 25 = work 1, 분석-모션 ba-10).
            var hitWork = chained && step < actions.Length && actions[step] == 8 && a.Data is { } ad
                          && Work(ad.BasicWorkId) is { } basic ? basic : w;
            if (!effectsDone && part1Basic == 0)
            {
                host.ScheduleAbilitySounds(w);
                _fxTargets = targetIndex >= 0 ? [targetIndex] : null;
                _fxStagger = 0;
                _fxHitAt.Clear();
                _fxUntouched.Clear();
                _fxArriveAt = 0;
                SpawnAbilityEffects(w, a, col, row);
                _fxTargets = null;
                // 판정을 기다릴 상한 — 이 work 의 이펙트가 다 끝나는 때(지연 + 수명 또는 모션 길이). 길이를 모르는 단계가 낀 표 값이 이펙트보다 길어 빈 대기가 되지 않게.
                fxSpan = (ScriptFor(w.Id)?.Effects ?? []).Select(fx => fx.Delay + (fx.Life > 0 ? fx.Life : host.EffectTicks(fx.Obs, fx.Motion))).DefaultIfEmpty(0).Max();
                host.StagingAb.SpawnWorkShakes(w);
                effectsAt = host._lastTime;
                // 카메라 따라가기(0x100eac00, ba-21 fx F7) — 힐·큐어·배리어류 189개는 겨눈 대상을, 34개는 시전자를 따라간다. 탄을 따라가는 60개는 아직.
                if (WorkCameraFollow.Target.Contains(w.Id) && !WorkCameraFollow.Shot.Contains(w.Id)
                    && (targetIndex >= 0 ? host._units[targetIndex] : LiveUnitAt(col, row)) is { } followed && followed != a)
                    CenterOnUnit(followed);
                else if (WorkCameraFollow.Caster.Contains(w.Id) && !WorkCameraFollow.Target.Contains(w.Id)) CenterOnUnit(a);
                // 탄을 따라가는 60개 — 탄이 가는 쪽(겨눈 칸)으로 카메라를 보낸다(탄 자체를 따라가지는 않는다 — 근사).
                else if (WorkCameraFollow.Shot.Contains(w.Id) && _fxArriveAt > 0 && (uint)col < host.Cols && (uint)row < host.Rows) CenterOnCell(col, row);
                // 군단기면 부하 잔상(ba-21 C) — 피해는 그 연출의 끝 무렵에 들어간다.
                for (double end = host._lastTime + host.LegionStageAb.StartLegionStage(a, w, col, row, WorkTargets(w, a, col, row)); host._lastTime < end;) yield return true;
                effectsDone = true;
            }
            // 혼·비연참·오메가 스윙 — 겨눈 빈 칸까지 돌진하며 지나는 칸의 적을 때린다(fg-21 ⑨). 돌진 자세(붙듦)는 이미 틀었다.
            if (PushSkill.DashWorks.Contains(w.Id))
            {
                foreach (bool _ in host.PushAb.DashRoutine(a, w, col, row, dying)) yield return true;
                holding = false;
                continue;
            }
            // 하이 텔레포트 — 피해 없이 고른 칸 둘레로 순간이동한다(범위는 빗나가는 정도).
            if (TeleportSkill.IsTeleportWork(w))
            {
                foreach (bool _ in host.TeleportAb.TeleportRoutine(w, a, col, row)) yield return true;
                while (a.IsBusy) yield return true;
                continue;
            }
            // 파트1 내리기(어빌리티 166 → Attack 399, 0x100733f0) — 탄 이를 고른 빈 칸(+0x200/+0x202)에 내려놓고(0x100365e0), 탈것의 탄 이 칸(+0x1bd · +0x1d0)을
            // 지운 뒤 탈것에 메시지 0x3f4 를 보낸다. 0x3f4(0x1003e480)는 쓰러짐 그것이다 — 칸 표에서 빼고 동작 15 · 소리 2032 · 21틱 뒤 지운다(0x1003a350).
            // 곧 빈 탈것은 판에 남지 않는다. 탄 이를 먼저 지웠으니 소멸자(0x10034560)의 「무리 탈것이고 탄 이가 있고 HP ≤ 0」에 안 걸려 무리 탈것은 줄지 않는다.
            // 판 위에서 다시 타는 길도 없다 — 태우기 0x100364b0 은 전투를 열 때(0x1002d574)와 배치(0x1002f2a0 · 0x1002faf6)에서만 부른다.
            if (w.Id == Part1DismountWork)
            {
                if (_part1Riders.Remove(a, out var rider) && rider.Alive)
                {
                    var (dc, dr) = FreeCellNear(col, row, rider);
                    PlaceRider(a, rider, dc, dr);
                    if (Trace)
                        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                            $"part1 dismount {a.ChrCode} → rider {rider.ChrCode} at ({dc},{dr}) soul {rider.Soul}; empty mount falls (0x3f4), team stock kept" + Environment.NewLine);
                    // 빈 탈것은 쓰러지는 동작 · 소리로 사라진다 — 쓰러짐(아래 dying)과 같은 21틱 옅어짐.
                    PlayActionFor(a, HitAction, DeathActionTicks);
                    host.Play(DeathSound);
                    for (double fallAt = host._lastTime; a.IsBusy;)
                    {
                        int n = 1 + (int)((host._lastTime - fallAt) * TicksPerSecond);
                        a.Fade = Math.Clamp((8 - n / 3) * 4 / 31.0, 0, 1);
                        yield return true;
                    }
                    a.Fade = 1;
                    a.OnField = false;
                    a.ResetTo(0, 0);
                    _part1TeamMounts.Remove(a);   // 잃은 것이 아니다 — 무리 탈것 수는 그대로(소멸자 0x10034595 의 +0x1bd = 0 갈래)
                    MarkDead(a);                  // 판 밖 「산 아군」으로 세지 않는다 · 대장이면 승계
                }
                continue;
            }
            // 파트1 기술(검 기술 · 썬더스탐 · 소환) — 시각표를 깔고 대상마다 그 기술이 닿을 때 한 번씩 친다(menu-85 · menu-89).
            if (Part1Skill.Handles(w.Id) || part1Basic > 0)
            {
                // 파트1 빛살(빔라이플 기본 공격 · 레이저 캐논 …)은 누른 유닛 하나가 아니라 판 끝까지 지나는 칸의 적 모두다(Part1BeamCells, 0x100769b0 → 0x10039690).
                var targets = targetIndex >= 0 && Part1BeamOf(w, a) is null ? [targetIndex] : WorkTargets(w, a, col, row);
                var cells = EffectCells(w, a, col, row).ToList();
                if (Trace)
                    System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                        $"part1 targets {w.Id} by {a.ChrCode}({a.Col},{a.Row})h{HeightAt(a.Col, a.Row)} aim ({col},{row}): {cells.Count} cells [{string.Join(' ', cells.Select(c => $"{c.Col},{c.Row}"))}], units [{string.Join(' ', targets.Select(ti => $"{host._units[ti].ChrCode}({host._units[ti].Col},{host._units[ti].Row})"))}]"
                        + $" · on board [{string.Join(' ', host._units.Where(u => u.Alive && u.OnField && u != a).Select(u => $"{u.ChrCode}({u.Col},{u.Row})h{HeightAt(u.Col, u.Row)}"))}]" + Environment.NewLine);
                var struck = new HashSet<int>();
                // 파트1 기본 공격의 타이밍 어택 — 박자 수만큼 「연」 동작으로 여러 번(Part1Skill.BasicMelee). 그러면 타마다 보통 피해(배율 100).
                host.Part1Ab.ChainHits = part1Basic > 0 && _timedCritBy == a ? host.TimedHitFx.AttackCount : 0;
                host.Part1Ab.ChainStruck = 0;
                // 타수가 둘 이상인 파트1 기본 공격(쌍검 더블 어택 · 쌍권총)은 타격마다 따로 친다 — 타수를 세기에 곱하지 않는다.
                host.Part1Ab.SplitStruck = 0;
                // 연 · 참도 모션의 타격 키마다 따로 친다(원본 0x10036130 — 키마다 0x1009d440, Part1Skill.StrikesEachKey).
                bool split = (part1Basic > 0 && host.Part1Ab.ChainHits < 2
                              && Part1Attack.ById(Part1Attack.LoadAll(), part1Basic) is { Hits: >= 2 })
                             || (part1Basic == 0 && (Part1Skill.StrikesEachKey(w.Id) || Part1Skill.HitsPerBall(w.Id)));   // 화이어 볼은 공마다(fg-62)
                // 연사 기술(권총연사 · 3연사 · 6연사 · 9연사 · 기관총)은 발마다 따로 친다 — 첫 발이 빗나가도 나머지가 맞을 수 있다(카페 제보 fg-51).
                int volley = part1Basic == 0 ? Part1Skill.VolleyShots(w.Id) : 0;
                if (volley >= 2) split = true;
                Part1.SplitHits = split;
                Part1.SplitShots = volley >= 2 ? volley : 0;
                bool each = (part1Basic == 0 && Part1Skill.HitsEachTime(w.Id)) || host.Part1Ab.ChainHits >= 2 || split;
                void Part1Hit(int ti)
                {
                    if (Trace)
                        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                            $"part1 strike {w.Id} → unit {ti} chr {host._units[ti].ChrCode} hp {host._units[ti].Hp} work {hitWork.Id} dmg {hitWork.IsDamage} at {host._lastTime:0.00}" + Environment.NewLine);
                    if (host.Part1Ab.ChainStruck >= 2) _timedPercent = 100;   // 연속 공격으로 여러 번 치면 타마다 보통 피해
                    if (struck.Add(ti) || each) ApplyWork(a, hitWork, host._units[ti], dying);
                }
                foreach (bool _ in part1Basic > 0 ? host.Part1Ab.BasicRoutine(part1Basic, a, targets, cells, (col, row), Part1Hit)
                                                  : host.Part1Ab.Routine(w, a, targets, cells, (col, row), Part1Hit, proc: _part1ProcRunning))
                    yield return true;
                (Part1.SplitHits, Part1.SplitShots) = (false, 0);
                // 맵 칸도 치는 기술(Attack +0x41 = 1) — 범위의 맵 물체에 알림 1 · 공격 서술(0x10076c46)(Part1MapObjects.HitByAttack). 땅불 · 얼음 덩이는 기술이 닿는 틱에 놓는다.
                // 통이 부서지며 터지는 키 6(틱 4) · 불붙는 동작이 끝날 때까지 기다렸다가 그 사이 쓰러진 유닛을 함께 치운다.
                if (host._scene.IsPart1 && Part1.AttackOf(w) is { HitsTiles: true } tileAttack)
                {
                    Part1Objects.HitByAttack(a, tileAttack, [.. cells], targets);
                    for (double until = host._lastTime + 3; Part1Objects.Busy && host._lastTime < until;) yield return true;
                    foreach (var d in host._units)
                        if (d.Alive && d.OnField && d.Hp <= 0 && !dying.Contains(d) && !SurvivesFatal(d)) dying.Add(d);
                }
                // 겨누기 +0x31 = 7 인 파트1 기술(혼 · 혈랑마혼 · 비연참 · 댓쉬 · 워프) — 시전자가 겨눈 칸으로 옮겨 선다. 이동기 0x100816d0 이 끝날 때
                // (소멸자 0x10081750) 시전자를 그 칸 가운데에 맞추고(×40 + 20) 판 자리에 다시 올린다(0x10037080). 워프는 유닛 맞춤 0x10081350.
                // 칸이 차 있으면 제자리. 걸은 비용을 다시 세지 않게 시작 자리도 옮긴다(파트2 돌진 DashRoutine 과 같이).
                if (Part1.AttackOf(w) is { AimFilter: 7 } && (a.Col, a.Row) != (col, row)
                    && !host._units.Any(u => u != a && u.Alive && u.OnField && u.Col == col && u.Row == row))
                {
                    a.WarpTo(col, row);
                    a.OriginCol = col;
                    a.OriginRow = row;
                }
                if (!followersDone && targets.Count > 0)
                {
                    FollowersAttack(userIndex, host._units[targets[0]], dying, allyPass: false, leaderWork: w);
                    followersDone = true;
                }
                while (a.IsBusy) yield return true;
                continue;
            }
            // 아크로스트의 엘레맨탈 파이어(불덩이가 돌다 대상마다 날아가 터짐)·서몬 몬스터(대상마다 몬스터가 나타나 침) — 피해는 대상마다 그때 한 번.
            // 편집기에서 대상 방식을 「아무 칸」(메테오처럼)으로 바꿔도 같은 연출 — 불덩이는 시전자 위에서 돌고 고른 범위의 대상에게 날아간다.
            if (w.AbilityId is AcrostSkill.ElementalFireAbility or AcrostSkill.SummonMonsterAbility)
            {
                var targets = WorkTargets(w, a, col, row);
                var struck = new HashSet<int>();
                void Hit(int ti) { if (struck.Add(ti)) ApplyWork(a, hitWork, host._units[ti], dying); }
                if (Trace)
                    System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                        $"acrost work {w.Id}: caster {a.ChrCode}({a.Col},{a.Row}) targets " +
                        string.Join(", ", targets.Select(t => $"{host._units[t].ChrCode}({host._units[t].Col},{host._units[t].Row}){(host._units[t].IsAlly ? " 아군" : "")}")) + Environment.NewLine);
                var routine = w.AbilityId == AcrostSkill.ElementalFireAbility ? host.AcrostAb.ElementalFireRoutine(a, targets, Hit) : host.AcrostAb.SummonMonsterRoutine(a, targets, Hit);
                foreach (bool _ in routine) yield return true;
                if (!followersDone && targets.Count > 0)
                {
                    FollowersAttack(userIndex, host._units[targets[0]], dying, allyPass: false, leaderWork: w);
                    followersDone = true;
                }
                while (a.IsBusy) yield return true;
                continue;
            }
            // 그라비티 필드 — 대상마다 중력장(219:0)이 차례로 깔리고, 끝날 때 효과가 든다.
            // 아스트럴 애로우 — 활을 당겨 화살을 쏘아 올리고, 대상마다 화살이 쏟아진다. 피해는 화살이 닿을 때 대상마다 한 번.
            if (w.AbilityId == AstralArrowSkill.AstralArrowAbility)
            {
                var targets = WorkTargets(w, a, col, row);
                var struck = new HashSet<int>();
                foreach (bool _ in host.AstralArrowAb.AstralArrowRoutine(a, targets, ti => { if (struck.Add(ti)) ApplyWork(a, hitWork, host._units[ti], dying); }))
                    yield return true;
                while (a.IsBusy) yield return true;
                continue;
            }
            // 리콜 — 고른 아군을 시전자 옆으로 불러온다(피해·회복 없음).
            if (w.AbilityId == TeleportSkill.RecallAbility)
            {
                var targets = WorkTargets(w, a, col, row);
                var who = targets.Select(i => host._units[i]).FirstOrDefault(u => u != a && u.IsAlly == a.IsAlly);
                if (who != null)
                {
                    foreach (bool _ in host.TeleportAb.RecallRoutine(a, who)) yield return true;
                    MarkBuffed(a, who, w);
                }
                while (a.IsBusy) yield return true;
                continue;
            }
            // 메테오 — 운석 한 발이 범위 안 한 칸에 떨어지고, 닿으면 범위 안 대상마다 피해가 한 번.
            if (w.AbilityId == MeteorSkill.MeteorAbility)
            {
                var targets = WorkTargets(w, a, col, row);
                var struck = new HashSet<int>();
                foreach (bool _ in host.MeteorAb.MeteorRoutine(w, a, col, row, targets, ti => { if (struck.Add(ti)) ApplyWork(a, hitWork, host._units[ti], dying); }))
                    yield return true;
                while (a.IsBusy) yield return true;
                continue;
            }
            if (w.AbilityId == GravityFieldSkill.GravityFieldAbility)
            {
                var targets = WorkTargets(w, a, col, row);
                var struck = new HashSet<int>();
                foreach (bool _ in host.GravityFieldAb.GravityFieldRoutine(a, targets, ti => { if (struck.Add(ti)) ApplyWork(a, hitWork, host._units[ti], dying); }))
                    yield return true;
                while (a.IsBusy) yield return true;
                continue;
            }
            // 천지 파열무 — X 자로 땅이 터진 뒤 대상마다 폭발한다. 피해는 그 폭발 때 대상마다 한 번.
            if (w.Id == HeavenEarthSkill.HeavenEarthWork && targetIndex < 0)
            {
                var targets = WorkTargets(w, a, col, row);
                var struck = new HashSet<int>();
                foreach (bool _ in host.HeavenEarthAb.HeavenEarthRoutine(a, targets, ti => { if (struck.Add(ti)) ApplyWork(a, hitWork, host._units[ti], dying); }))
                    yield return true;
                if (!followersDone && targets.Count > 0)
                {
                    FollowersAttack(userIndex, host._units[targets[0]], dying, allyPass: false, leaderWork: w);
                    followersDone = true;
                }
                while (a.IsBusy) yield return true;
                continue;
            }
            // 나인 크루세이더 — 칼이 날아다니며 차례로 꿰뚫는다. 피해는 대상마다 처음 꿰뚫릴 때 준다.
            if (w.Id == NineCrusaderSkill.NineCrusaderWork && targetIndex < 0)
            {
                var targets = WorkTargets(w, a, col, row);
                // 원본 0x1009cb50 — 모션 48(칼을 들어 올림) 뒤 칼이 나는 동안 49 를 붙들고, 다 날면 51(fg-20).
                for (double end = host._lastTime + host.HeavenEarthAb.PlayRawMotion(a, 48, loop: false); host._lastTime < end;) yield return true;
                host.HeavenEarthAb.PlayRawMotion(a, 49, loop: true, holdSeconds: 30);
                var flight = host.NineCrusaderAb.StartNineCrusader(a, targets);
                var struck = new HashSet<int>();
                while (!flight.Done)
                {
                    host.NineCrusaderAb.StepSword(flight);
                    while (flight.Pierced.TryDequeue(out int ti))
                        if (struck.Add(ti)) ApplyWork(a, hitWork, host._units[ti], dying);
                    yield return true;
                }
                foreach (int ti in targets.Where(struck.Add)) ApplyWork(a, hitWork, host._units[ti], dying);   // 칼이 못 닿은 대상(없어야 한다)
                double release = host.HeavenEarthAb.PlayRawMotion(a, 51, loop: false);
                if (release <= 0) a.PlayAction(ObsMotionTable.ActionStand, 0);
                if (!followersDone && targets.Count > 0)
                {
                    FollowersAttack(userIndex, host._units[targets[0]], dying, allyPass: false, leaderWork: w);
                    followersDone = true;
                }
                while (a.IsBusy) yield return true;
                continue;
            }
            double stagedFrom = host._lastTime;
            if (step == hitStep) foreach (bool _ in host.StagingAb.StageBeforeHit(w, a, targetIndex, col, row)) yield return true;   // 밸런싱·웹폰 크래쉬·블랙홀(ba-20 P6~P8)
            // 다이나믹 크래쉬 — 탄이 닿고(거리 × 2틱) 대상이 다 밀린 뒤에 판정과 SOUL −10 이 든다(0x1009fd40 단계 1·2, gm-skil-21). 전에는 맞고 나서 밀렸다.
            bool crashed = false;
            if (step == hitStep && PushSkill.DynamicCrashWorks.Contains(w.Id)
                && (targetIndex >= 0 ? host._units[targetIndex] : LiveUnitAt(col, row)) is { Alive: true } crashTarget && crashTarget != a)
            {
                crashed = true;
                foreach (bool _ in host.KnockbackAb.DynamicCrashRoutine(a, w, crashTarget)) yield return true;
                ApplyWork(a, hitWork, crashTarget, dying);
                if (crashTarget.Alive && crashTarget.Hp > 0) host.KnockbackAb.DrainSoul(crashTarget, 10);
            }
            // 핸들러가 판정(1001)을 보내는 틱까지 기다린다(ba-21 T1) — 원본은 이펙트가 다 나온 뒤에 숫자·맞음 동작이 뜬다(힐 114틱, 프레셔 49틱 …).
            // 전에는 이펙트를 띄우는 틀에 판정해 숫자가 먼저 떴다. 길이를 다 아는 work(Sure)과 카메라 대기만 모르는 work 만 따른다.
            // 몸짓 타격 키로 치는 칸(여러 타)·필살기(준비 7 — 사슬이 핸들러 모션을 이미 튼다)·군단기·전용 연출이 이미 기다린 work 은 뺀다.
            if (step == hitStep && !crashed && host._lastTime == stagedFrom && hitTimes.Count == 1 && w.Prepare != 7 && !LegionStageSkill.IsLegionSkill(w.Id) && !host.SpecialHitsAb.HasSpecialHit(w)
                && (WorkHitTicks.Table.TryGetValue(w.Id, out var handlerHit) || (ArriveHit(w) && _fxArriveAt > 0)))   // 길이를 모르는 단계가 낀 work 도 따른다 — 이펙트 표의 지연과 같은 시계라 숫자가 이펙트보다 먼저 뜨지 않는다
            {
                bool sureHit = handlerHit.Sure || WorkHitTicks.CameraOnly.Contains(w.Id);
                double hitAt = effectsAt + Math.Min(Math.Min(handlerHit.Ticks, 240), sureHit ? 240 : Math.Max(10, fxSpan)) / TicksPerSecond;
                // 핸들러가 대상을 붙드는 기술(브레인 스톰·블라인드·안티 밸런싱·미라클·아이템 1609~1617) — 판정까지 맞음 자세(미라클은 시전 자세로 아래를 봄)로
                // 붙들었다가 판정 바로 앞에 서기로 되돌린다(0x10095ab0 · 0x100a19ed · 0x100a1dbf · 0x1009b958 · 0x100b76f5, ba-21 T3).
                var held = new List<(UnitState Unit, Facing Was)>();
                if (HeldTargetWorks.Contains(w.Id) && hitAt > host._lastTime)
                    foreach (int ti in targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row))
                    {
                        var t = host._units[ti];
                        if (!t.Alive || t.Hp <= 0 || t == a) continue;
                        held.Add((t, t.Facing));
                        if (w.Id == 490) t.Facing = Facing.Down;
                        t.PlayAction(w.Id == 490 ? 6 : HitAction, hitAt - host._lastTime);
                    }
                // 「탄이 사라질 때 판정」 work(카운터 미사일 · 블레이드 샤워 …, 41개)은 탄이 닿는 때에 맞는다(ba-21 fx 덧정보 표 생성 기록).
                if (ArriveHit(w) && _fxArriveAt > 0) hitAt = Math.Min(_fxArriveAt, effectsAt + 300 / TicksPerSecond);
                while (host._lastTime < hitAt) yield return true;
                foreach (var (t, was) in held)
                {
                    t.Facing = was;                                   // 미라클이 돌려 둔 방향은 되돌린다
                    if (t.Alive && t.Hp > 0) t.PlayAction(ObsMotionTable.ActionStand, 0);
                }
            }
            // 소닉 블레이드·크레이지 샷 — 핸들러가 자료 범위와 다르게 친다(ba-20 E1·E2).
            if (step == hitStep && host.SpecialHitsAb.HasSpecialHit(w))
            {
                foreach (bool _ in host.SpecialHitsAb.SpecialHitRoutine(a, w, hitWork, col, row, dying)) yield return true;
                if (!followersDone && w.FollowersAct)
                {
                    FollowersAttack(userIndex, LiveUnitAt(col, row) ?? a, dying, allyPass: false, leaderWork: w, walk: false);
                    followersDone = true;
                }
                if (holding) a.PlayAction(ObsMotionTable.ActionStand, 0);
                while (a.IsBusy) yield return true;
                holding = false;
                continue;
            }
            // 리인카네이션은 보통 타격이 없다 — 피해는 밀어내기 슬롯만 준다(RadialPushRoutine, 0x1008d940 · ba-16 R1).
            for (int hit = 0; hit < (PushSkill.ReincarnationWorks.Contains(w.Id) || crashed ? 0 : hitTimes.Count); hit++)
            {
                if (hit > 0)
                    for (double end = host._lastTime + (hitTimes[hit] - hitTimes[hit - 1]); host._lastTime < end;) yield return true;
                // 타격 키의 어빌리티가 work 을 고른다(0x100322e0) — 난타 필살(동작 27)의 앞 타격 키는 Hunter(125 → work 1468, 위력 −175)라
                // 작게 여러 대 들고, 마지막 키(풍아열공참 21)만 그 기술 위력이다(gm-skil-25). 전에는 키 열한 개가 모두 기술 위력으로 들었다.
                bool hunterHit = hit < hitAbilities.Count && hitAbilities[hit] == HunterAbility;
                var strikeWork = hunterHit && Work(HunterWork) is { } hunter ? hunter : hitWork;
                // 그 마지막 한 대는 대상 위의 찌르기 이펙트(풍아열공참 Obs 301, 단계 0 부터 68틱)가 터질 때 든다 — 몸짓의 키(57틱)보다 늦다.
                if (!hunterHit && hit > 0 && hitAbilities.Take(hit).Contains(HunterAbility)
                    && ScriptFor(w.Id)?.Effects.FirstOrDefault(e => e.OnTarget).Delay is > 0 and var burst)
                    for (double end = effectsAt + burst / TicksPerSecond; host._lastTime < end;) yield return true;
                var targets = targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row);
                // 대상마다 이펙트가 엇갈려 뜨는 기술(헤비프레셔 15틱 · 엘레맨탈 썬더 8틱 …)은 판정도 그 간격으로 하나씩 든다(ba-21 fx F6).
                // 간격은 이펙트가 실제로 엇갈려 뜬 값(_fxStagger)이고, 다 합쳐 150틱을 안 넘는다. 반사로 시전자가 쓰러지면 거기서 멈춘다.
                int stagger = hitTimes.Count == 1 && targets.Count > 1 ? Math.Min(_fxStagger, 150 / (targets.Count - 1)) : 0;
                for (int k = 0; k < targets.Count; k++)
                {
                    if (k > 0 && !a.Alive) break;
                    if (_fxUntouched.Contains(host._units[targets[k]])) continue;      // 이펙트가 안 닿은 대상(엘레맨탈 아이스의 빈 칸)
                    if (k > 0 && stagger > 0)
                        for (double end = host._lastTime + stagger / TicksPerSecond; host._lastTime < end;) yield return true;
                    // 이펙트가 그 대상에 닿는 때가 정해진 기술(라이트닝 샤벨)은 그때까지 기다린다 — 시작에서 450틱을 안 넘게
                    // (오버 테이크 340~389틱 · 블리자드 97 + 41N 틱이 가장 길다).
                    if (_fxHitAt.TryGetValue(host._units[targets[k]], out double reach))
                        for (double end = Math.Min(reach, effectsAt + 450 / TicksPerSecond); host._lastTime < end;) yield return true;
                    ApplyWork(a, strikeWork, host._units[targets[k]], dying);
                }
                HitObjects(strikeWork);
                // 군단 행동(상태 15) — 대장이 기술을 쓰면 부하들도 <b>한 번</b> 같은 패스로 제 기술을 쓴다(여러 타를 쳐도 부하는 한 번).
                // 합류는 대장 work +0x41 만 본다(0x1006a110 → 0x1005fa90, FollowersAttack 첫머리). 패스는 대장 work 의 <b>효과 대상 +0x1e</b> 로 가른다
                // (0x1005fb6d~0x1005fbca): 4 → 아군 패스, 5 → 겨눈 유닛이 적이 아니면 아군 패스, 그 밖 → 적 패스. 종류(피해·보조)는 안 본다.
                // 전에는 「피해 기술이거나 방식 4」일 때만 따라가 약화 캡슐·저주·버프에 부하가 가만히 있었다(감사3 L7).
                // 겨눈 대상이 없는 제자리 기술도 패스가 돈다 — 기준 칸은 대장 자리, 걸어가 치기는 없다.
                if (!followersDone && w.FollowersAct)
                {
                    var aimed = targetIndex >= 0 ? host._units[targetIndex] : LiveUnitAt(col, row);
                    bool allyPass = w.AreaMode == 4 || (w.AreaMode == 5 && aimed != null && !SeesAsFoe(a, aimed));
                    // 군단 행동은 두 패스가 늘 다 돈다 — 아군 패스(0x1005fa90) → 적 패스(0x1005fd00). 부하는 두 패스 모두에서 고르고
                    // 대장만 제 work 에 맞는 패스에서 움직인다(ba-20 Q1). 아군 패스는 제자리만(0x1005f1c0 → 0x1005e820, Q3).
                    // 아군 패스에서 기술을 쓴 부하는 동작 중이라 적 패스에서 빠진다.
                    var anchor = targets.Count > 0 ? host._units[targets[0]] : a;
                    FollowersAttack(userIndex, anchor, dying, allyPass: true, leaderWork: w, walk: false);
                    FollowersAttack(userIndex, anchor, dying, allyPass: false, leaderWork: w, walk: targets.Count > 0 && !allyPass);
                    followersDone = true;
                }
                if (targets.Count == 0 || !host._units[targets[0]].Alive) break;
            }
            // 사이킥 크로스 — 두 획이 겹치는 가운데 칸의 대상은 5틱 뒤 한 번 더 맞는다(ba-14 H4).
            if (PushSkill.PsychicCrossWorks.Contains(w.Id))
            {
                for (double end = host._lastTime + 5 / TicksPerSecond; host._lastTime < end;) yield return true;
                if (LiveUnitAt(col, row) is { } centre && centre != a && !dying.Contains(centre) && SeesAsFoe(a, centre)) ApplyWork(a, hitWork, centre, dying);
            }
            // 여러 번 치는 기술 — 무신멸뢰옥 3타·선 블래스트 4타·카운터 미사일 Lv11↑ 2회(fg-21 ⑩). 간격은 원본 핸들러 틱.
            foreach (int gap in host.PushAb.ExtraHitGaps(w))
            {
                for (double end = host._lastTime + gap / TicksPerSecond; host._lastTime < end;) yield return true;
                if (w.Id == PushSkill.SunBlastWork) host.ForcetrulAb.SunBlastBurst(w, a, first: false);   // 타마다 빛 고리 + 화면 물들이기(흔들림은 원본에 없다)
                if (Trace) System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"), $"{host._lastTime:F2} extra hit work {w.Id} after {gap} ticks" + Environment.NewLine);
                foreach (int ti in (targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row)))
                    if (host._units[ti].Alive && !dying.Contains(host._units[ti])) ApplyWork(a, hitWork, host._units[ti], dying);
            }
            // 비·다이나믹 크래쉬 — 맞은 인물을 시전자가 보는 쪽으로 밀어낸다(fg-18·fg-21 ⑧). 그 타로 쓰러질 인물도 밀린 칸에서 쓰러진다
            // (비 단계 2 0x10080111~0x1008035c 는 겨눈 유닛을 조건 없이 민다 · 다이나믹 크래쉬는 밀고 나서 판정, ba-21 T5). 전에는 쓰러질 인물은 안 밀었다.
            if (KnockbackSkill.BiWorks.Contains(w.Id) || (PushSkill.DynamicCrashWorks.Contains(w.Id) && !crashed))
            {
                var knocked = (targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row))
                              .Select(i => host._units[i]).FirstOrDefault(u => u.Alive && u != a);
                if (knocked != null)
                    foreach (bool _ in host.KnockbackAb.KnockbackRoutine(a, w, knocked, soulDrain: PushSkill.DynamicCrashWorks.Contains(w.Id) ? 10 : 0)) yield return true;
                else if (KnockbackSkill.BiWorks.Contains(w.Id)) PlayAction(a, DrawnAction(a, 24));
            }
            // 리인카네이션 — 범위 안 적을 시전자 반대쪽으로 범위 밖까지 밀어낸다(fg-21 ⑧).
            if (PushSkill.ReincarnationWorks.Contains(w.Id))
                for (double end = effectsAt + ElementalSkill.ReincarnationPush / TicksPerSecond; host._lastTime < end;) yield return true;   // 쫓는 그림이 사라지고 279:1 이 뜬 6틱 뒤(0x1008d940 단계 2)
            if (PushSkill.ReincarnationWorks.Contains(w.Id))
                foreach (bool _ in host.PushAb.RadialPushRoutine(a, w, col, row, WorkTargets(w, a, col, row), dying)) yield return true;
            // 워핑 — 대상을 멀리 날려 보낸다(fg-21 ⑧).
            if (PushSkill.WarpingWorks.Contains(w.Id))
            {
                var thrown = (targetIndex >= 0 ? [targetIndex] : WorkTargets(w, a, col, row))
                             .Select(i => host._units[i]).FirstOrDefault(u => u.Alive && u != a && !dying.Contains(u));
                if (thrown != null) foreach (bool _ in host.PushAb.ThrowRoutine(a, thrown)) yield return true;
            }
            if (holding)
            {
                // 붙든 자세는 이 기술의 이펙트가 다 터질 때까지(길어도 3초) — 그 뒤 다음 칸(놓는 모션)으로, 없으면 선다.
                var spawned = _effects.Skip(effectMark).ToList();
                for (double start = host._lastTime; host._lastTime - start < 3 && (host._lastTime - start < 0.5 || spawned.Any(_effects.Contains));)
                    yield return true;
                holding = false;
                if (step + 1 >= actions.Length) a.PlayAction(ObsMotionTable.ActionStand, 0);
                continue;
            }
            while (a.IsBusy) yield return true;   // 남은 동작을 마저 재생한다
        }

        // 전용 연출 갈래(혼·메테오·소닉 블레이드 …)는 위의 보통 타격을 안 지난다 — 그 기술로 겨눈 물체가 안 맞았다(사용자 보고: Btl 0133 해골 방어물).
        HitObjects(w, onlyIfNone: true);
        // 늦게 뜨는 이펙트(단계·사슬 지연)가 시작할 때까지는 행동이 안 끝난다 — 상한 200틱.
        for (double cap = host._lastTime + 200 / TicksPerSecond; host._lastTime < _fxLatestStart && host._lastTime < cap;) yield return true;
        _fxLatestStart = 0;
        // 군단기 연출이 끝나 모두 다시 보일 때까지 기다린다 — 부하의 걸어가 치기도 그 뒤에.
        while (host._lastTime < host.LegionStageAb._legionStageEnd) yield return true;
        // 사거리 밖이던 부하는 먼저 걸어간 뒤 친다(원본 0x1005f1c0: 명령1 이동 → 명령2 기술).
        if (_followerStrikes.Count > 0)
        {
            while (_followerStrikes.Any(s => s.Follower.IsBusy)) yield return true;
            foreach (var (follower, work, aimCol, aimRow) in _followerStrikes)
            {
                // 걸어가는 사이 TP 가 모자라게 됐으면(걸음 값을 이미 냈다) 안 친다 — 원본도 명령2 기술을 끝처리에서 제 TP 로 정산한다(감사3 L1).
                if (!follower.Alive || !follower.OnField || follower.Hp <= 0 || !CanAfford(follower, work)) continue;
                FollowerStrike(follower, work, aimCol, aimRow, dying);
            }
            _followerStrikes.Clear();
            for (double end = host._lastTime + 0.2; host._lastTime < end;) yield return true;
        }

        // 이스케이프는 겨눈 빈 칸으로 순간이동한다(분석-모션 ba-10) — 마리아·유블레인이 쓰는,
        // 사라졌다 나타나는 그 기술이다. 그냥 자리만 덮어쓰면 뚝 끊겨 보이니 짧게 사라졌다 나타나게 한다.
        if (w.Id == EscapeWork && LiveUnitAt(col, row) == null)
        {
            const double fadeSeconds = 0.15;
            for (double start = host._lastTime, end = start + fadeSeconds; host._lastTime < end;)
            {
                a.Fade = Math.Max(0, 1 - (host._lastTime - start) / fadeSeconds);
                yield return true;
            }
            a.Fade = 0;
            a.WarpTo(col, row);
            a.OriginCol = col;
            a.OriginRow = row;
            for (double start = host._lastTime, end = start + fadeSeconds; host._lastTime < end;)
            {
                a.Fade = Math.Min(1, (host._lastTime - start) / fadeSeconds);
                yield return true;
            }
            a.Fade = 1;
        }

        // 부하의 남은 타격이 다 들 때까지 — 쓰러짐 처리(dying)가 그 뒤에 돈다.
        while (_followerHits.Count > 0) yield return true;
        host._mapTintTarget = -1;   // 행동이 끝나면 맵 물들이기를 푼다(0x1002e920)
        // 희생(13)·익스플로젼(22) — 아군을 회복시킨 뒤 시전자 HP 를 0 으로 쓴다(0x10090d62 · 0x100915f9, ba-20 D1).
        // 처치 보상은 아무도 안 받는다. 47(전투불능 방지)은 되살린다 — 되살림은 쓰러짐 처리 상태기(0x10067fe2~0x10068152)가 HP ≤ 0 인 유닛 모두에게 까닭을 가리지 않고 한다(ba-22 확인).
        if (w.AbilityId is SacrificeAbility or ExplosionAbility && a.Alive && !dying.Contains(a) && _demo == null)
        {
            a.Hp = 0;
            if (!SurvivesFatal(a)) dying.Add(a);
        }
        if (w.Id is StanceDefendWork or StanceEvadeWork) a.Stance = w.Id == StanceDefendWork ? 1 : 2;
        GainBuffExp(a);
        if (!eventFinisher) host.Stats.Cast(a, w);   // 통계 — 사건이 시킨 기술은 내가 쓴 것이 아니다
        PayWorkCost(a, w, free: eventFinisher);   // 사건 207·909 는 +0xa4 = 1 — 비용 없이 SOUL 증가만(0x1007638c, 감사5 B2)
        // 기술 뒤 대장 자리를 「진형을 짠 자리」로 적어 둔다 — SyncFollowers 가 이 자리로 진형을 다시 세우지 않게.
        _formationAt[userIndex] = (a.Col, a.Row);
        _skillLeader = -1;
        // 사건 207 의 쓰러짐 처리(0x1004e6d0)는 지금 차례 유닛([ctrl+0x4ce8])이면 HP 1 로 남긴다(0x1004e757~0x1004e765, 감사5 B3).
        if (eventFinisher && !part1Counter && (uint)_turn < host._units.Length && host._units[_turn] is var now && dying.Remove(now)) now.Hp = 1;

        // 파트1 무기 덧기술 — 치명타가 걸어 둔 기술을 맞은 자리에 공짜로 쏜다(사건 207 과 같은 길, 비용 없음).
        if (_part1Proc is { } part1Proc && (!eventFinisher || part1Counter))   // 되받아치기의 치명도 덧기술을 쏜다
        {
            _part1Proc = null;
            _part1ProcRunning = true;
            try
            {
                var procRoutine = UseWorkRoutine(part1Proc.User, part1Proc.Work, -1, part1Proc.Col, part1Proc.Row, [], eventFinisher: true);
                while (procRoutine.MoveNext()) yield return procRoutine.Current;
            }
            finally { _part1ProcRunning = false; }   // 불러오기 · RESTART 가 이 루틴을 버려도 남지 않게
            dying.RemoveAll(d => d.Hp > 0);
            foreach (var d in host._units)
                if (d.Alive && d.OnField && d.Hp <= 0 && !dying.Contains(d) && !SurvivesFatal(d)) dying.Add(d);
        }

        // 파트1 반격 · 역습 자세 — 맞은(피한) 이가 때린 이에게 기본 공격을 한 번 되돌린다. 원본은 맞은 이의 다음 명령 칸(+0x220)에
        // 기본 공격 +0xb6 · 때린 이 칸을 넣고(0x1003a2f0) 그 사람의 행동 끝(0x1003a350)에 돌린다 — 한 행동에 맞은 이마다 한 번(같은 칸을 덮어쓴다).
        // 되받아치기는 보통 공격 그대로 굴린다(피하기 · 막기 · 치명). 되받아치는 중에는 다시 걸지 않는다.
        if (_part1Counters.Count > 0 && !part1Counter)
        {
            var counters = _part1Counters.ToList();
            _part1Counters.Clear();
            foreach (var (by, target, basic) in counters)
            {
                if (!by.Alive || !by.OnField || by.Hp <= 0 || !target.Alive || !target.OnField || target.Hp <= 0 || dying.Contains(by)) continue;
                if (!InWorkRange(basic, by.Col, by.Row, target.Col, target.Row, by)) continue;
                if (Trace)
                    System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                        $"{host._lastTime:F2} part1 counter {by.ChrCode} → {target.ChrCode} ({Part1Rules.StanceName(Part1.StanceOf(by))}) work {basic.Id}" + Environment.NewLine);
                var counterRoutine = UseWorkRoutine(Array.IndexOf(host._units, by), basic, Array.IndexOf(host._units, target), target.Col, target.Row, [],
                                                    eventFinisher: true, part1Counter: true);
                while (counterRoutine.MoveNext()) yield return counterRoutine.Current;
            }
            _part1Counters.Clear();   // 되받아치는 동안 걸린 것은 버린다(되받아치기의 되받아치기는 없다)
            dying.RemoveAll(d => d.Hp > 0);
            foreach (var d in host._units)
                if (d.Alive && d.OnField && d.Hp <= 0 && !dying.Contains(d) && !SurvivesFatal(d)) dying.Add(d);
        }

        // 22·23·24(SOUL·TP 사망 조건)는 행동 끝마다 본다 — 상태 20 → 18 CHRDIE 가 전 유닛에 0x1007c670 을 돌린다(ba-20 K3).
        foreach (var d in host._units)
            if (_demo == null && d.Alive && d.OnField && !dying.Contains(d) && DiesByStatus(d) && !SurvivesFatal(d)) { d.Hp = 0; GainAilmentKillExp(d); dying.Add(d); }
        // 행동 끝 사건 검사(갈래 2, 0x100680a3)는 쓰러짐 처리(상태 18)보다 <b>앞</b>이다 — HP 0 유닛이 아직 서 있을 때 본다(ba-20 V1).
        // 보스가 죽지 않고 물러나는 사건(201)·풀피 회복(707)·HP 조건(203) 사건이 이때 터진다. 전에는 쓰러뜨린 뒤에 봐서
        // Btl 0092 사건 3·0145 사건 4(그 전투의 유일한 끝 사건)가 통째로 빠졌다.
        bool hadDying = dying.Count > 0;
        if (dying.Count > 0 && !eventFinisher)
        {
            _eventCheckDue |= 1 << 2;
            // 참인 사건이 여럿이면 다 돌 때까지 — 한 틀에 끝나는 사건 뒤의 사건(707 회복·201 퇴장)이 죽는 동작 도중에 돌지 않게.
            for (int guard = 0; guard < 16; guard++)
            {
                RunEvents();
                if (!EventsBusy && (_eventCheckDue == 0 || _events.Count == 0)) break;
                if (EventsBusy) yield return true;  // 사건이 도는 동안 이 루틴은 멈춘다(UpdateTurn)
                if (_outcome.Length > 0) break;
            }
            hadDying = true;
            dying.RemoveAll(d => d.Hp > 0 || !d.OnField || !d.Alive);
        }
        if (dying.Count > 0)
        {
            // 18 CHRDIE(0x1006a8b1) — 쓰러질 유닛마다 가운데 명령을 같은 틀에 걸어 목록 순서 <b>마지막</b>이 이긴다. 기다리지 않고 죽는 동작을 같이 한다(감사4 C17).
            CenterOnUnit(dying.OrderBy(d => Array.IndexOf(host._units, d)).Last());
            foreach (var d in dying) { PlayActionFor(d, HitAction, DeathActionTicks); host.Play(DeathSound); }
            // 쓰러지는 21틱 동안 옅어진다 — 섞기 단계 = 8 − 카운터/3, 가중 4k/31(그리기 0x10071af5~0x10071bdd, ba-20 P3).
            for (double fallAt = host._lastTime; dying.Any(d => d.IsBusy);)
            {
                int n = 1 + (int)((host._lastTime - fallAt) * TicksPerSecond);
                foreach (var d in dying) d.Fade = Math.Clamp((8 - n / 3) * 4 / 31.0, 0, 1);
                yield return true;
            }
            foreach (var d in dying) d.Fade = 1;
            foreach (var d in dying)
            {
                if (d.Hp > 0 || !d.OnField) continue;    // 그 사이 사건이 되살렸거나 판에서 뺐다
                MarkDead(d);                             // 대장이 죽으면 첫 부하가 대장이 된다(승계는 MarkDead 한 곳에서, 감사5 L-B)
            }
        }
        // 행동 끝 상태 21(0x100681a0) — 처치가 없어도 <b>매 행동마다</b> 레벨업을 보고, 그다음 갈래 1 검사(0x10068270)를 한다.
        // 전멸 판정(상태 4, 0x1006ed10)은 그 뒤라 마지막 적을 쓰러뜨린 사람의 레벨업 창도 전투가 끝나기 전에 뜬다(ba-15 Q7).
        // 전에는 CheckOutcome 이 먼저 결과를 세워 UpdateTurn 이 레벨업 창 앞에서 돌아가 버렸다.
        _timedCritBy = null;
        _timedAimBy = null;
        AutoHealAll();   // 상태 20 갈래 0(0x10067d70) — 행동이 끝날 때마다 8(자동 회복)을 전원에게(ba-20 C1)
        QueueLevelUps();
        _eventCheckDue |= 1 << 1;
        if (dying.Count > 0 || hadDying)            // 사건이 쓰러질 유닛을 빼거나(201) 죽였어도(706) 전멸 판정은 한다
        {
            if (_levelUpQueue.Count == 0) CheckOutcome();
            else _outcomeAfterLevelUp = true;       // 레벨업 줄이 다 빠진 뒤 UpdateLevelUp 이 본다
        }
        for (double end = host._lastTime + 0.1; host._lastTime < end;) yield return true;
    }

    /// <summary>
    /// work 끝처리 <c>0x10076380</c> — TP → SOUL → HP 차례로 비용을 빼고 행동 뒤 SOUL 을 올린다. 체질마다 SOUL·TP·HP 로 나뉘는 비율이 다르다.
    /// 군단 부하도 같은 끝처리를 지난다(<c>0x10071e20</c> 은 부하면 차례 끝 검사만 건너뜀, 감사3 L1).
    /// </summary>
    internal void PayWorkCost(UnitState a, WorkData w, bool free = false)
    {
        if (a.Data is not { } cost || host._db is not { } db2) return;
        // +0xa4 ≠ 0(사건 207·909)이면 TP·SOUL·HP 비용을 통째로 건너뛰고(0x10076384 jne 0x10076586) SOUL 증가만 한다(감사5 B2).
        if (free) { AddSoul(a, w.Kind switch { 0 => db2.N(26), 1 => db2.N(27), 2 => db2.N(28), 3 => db2.N(29), _ => 0 }); PaySoulGain(a); return; }
        a.Tp -= TpCostFor(a, cost, w.Id);
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"work {w.Id} by {a.ChrCode}{(a.LeaderIndex >= 0 ? " (부하)" : "")}: TP {db2.WorkTpCost(cost, w.Id)} × (100{a.Status(20):+#;-#;+0})% = {TpCostFor(a, cost, w.Id)}, 남은 TP {a.Tp}" + Environment.NewLine);
        a.Soul = Math.Max(0, a.Soul - SoulCostFor(a, cost, w.Id));
        // 행동 뒤 SOUL 증가(0x10076586 점프표 0x100765ec): 종류 0 → Num26 · 1 → Num27 · 2 → Num28 · 3 → Num29 · 4·5·7 → 0(ba-15 재확인).
        AddSoul(a, w.Kind switch { 0 => db2.N(26), 1 => db2.N(27), 2 => db2.N(28), 3 => db2.N(29), _ => 0 });
        int hp = db2.WorkHpCost(cost, w.Id);
        if (hp > 0 && cost.JobId != 37 && a.Hp > 0) a.Hp = Math.Max(1, a.Hp - hp);   // 직업 37 은 SOUL·HP 소비 면제(0x100764d3), TP 는 뺀다
        PaySoulGain(a);
    }

    /// <summary>50(맞힌 대상마다 SOUL)으로 모인 몫을 비용을 치른 뒤에 준다 — 소울 블레스트의 SOUL 흡수(Ailment.cs).</summary>
    internal void PaySoulGain(UnitState a)
    {
        if (_soulGain.Who != a) return;
        int before = a.Soul;
        AddSoul(a, _soulGain.Amount);
        _soulGain = default;
        if (a.Soul > before) Popup(a, $"{host._db?.T(41)} +{a.Soul - before}", HealColor2, 13);
    }

    /// <summary>타이밍 크리티컬을 맞춘 인물 — 그 행동이 끝날 때까지 그 인물의 타격은 반드시 맞고 크리티컬이다(<see cref="TimedHit"/>).</summary>
    internal UnitState? _timedCritBy;

    /// <summary>그 타격의 피해 배율(%) — 두 번 치는 동작으로 바꿨으면 100(두 번 맞아 2배), 한 번 치면 200. 0 은 「크리티컬로」(박자 하나만 맞춤).</summary>
    internal int _timedPercent = 200;

    /// <summary>
    /// 타이밍 어택 띠의 안내 글 — 그 대상에게 이 공격이 맞으면 들어갈 피해와 크리티컬이면 들어갈 피해(사용자 요청),
    /// 그 아래 줄에 명중률과 셈의 바탕(파트1: 세기 ÷ 대상 AC · 마법은 MR × 속성 저항/10, 파트2: 공격력 × (Num3 − 대상 방어율) / Num3)(사용자 요청 fg-48).
    /// 실제 판정(ApplyWork 첫 줄과 같은 식)을 따로 둔 난수로 여러 번 굴려, 맞은 것 가운데 크리티컬이 아닌 피해의 가운데 값을 「예상」으로,
    /// 거기에 크리티컬 배율(Num 23 — 타이밍 어택의 한 대 크리티컬과 같다)을 건 값을 「크리티컬」로, 맞은 비율을 「명중」으로 보인다(빗나감 · 피함은 안 맞음, 막음은 맞음).
    /// 한 번도 안 맞으면 명중 0% 만. <paramref name="aim"/> 이면 타이밍 명중 띠의 것 — 크리티컬 대신 박자 안내를 첫 줄에 둔다.
    /// </summary>
    internal string? ExpectedDamageText(UnitState a, UnitState t, WorkData w, bool aim = false)
    {
        if (host._db is not { } db || a.Data == null || t.Data == null || !w.IsDamage) return null;
        const int Rolls = 200;
        var rng = new Random(12345);
        var hits = new List<int>();
        int landed = 0;
        (int Power, int Defense, bool Magic, int? Resist, bool Blocked)? why = null;
        for (int i = 0; i < Rolls; i++)
        {
            Part1.LastHit = null;
            var (amount, result, crit) = (host._scene.IsPart1 ? Part1.Resolve(rng, a, t, w, AimBonus(a)) : null)
                ?? db.Resolve(rng, CombatData(a)!, a.Tp, host.TuningScr.AttackSoul(w, a.Soul), CombatData(t)!, t.Tp, t.Hp, t.MaxHp, w, t.Stance, a.Status(29), AimBonus(a));
            if (result is not (2 or Part1Rules.Blocked or Part1Rules.CounterHit)) continue;
            landed++;
            if (!crit) hits.Add(amount);
            if (Part1.LastHit is { } h && (why == null || why.Value.Blocked && !h.Blocked)) why = h;
        }
        string hitRate = $"명중 {landed * 100 / Rolls}%";
        if (hits.Count == 0) return aim ? $"{TimedHit.AimGuide}\n{hitRate}" : null;
        hits.Sort();
        int normal = hits[hits.Count / 2], critical = normal * db.N(23) / 100;
        string basis;
        if (why is { } y)
            basis = $"세기 {y.Power} ÷ 대상 {(y.Magic ? "MR" : "AC")} {y.Defense}{(y.Resist is { } r && r != 10 ? $" × 속성 {r}/10" : "")}";
        else if (CombatData(a) is { } ca && CombatData(t) is { } ct)
            basis = $"공격력 {db.Atk(ca, host.TuningScr.AttackSoul(w, a.Soul), w.Power, a.Status(29))} × 대상 방어율 ({db.N(3)} − {db.Rdp(ct, t.Hp, t.MaxHp)})/{db.N(3)}";
        else basis = "";
        string first = aim ? $"{TimedHit.AimGuide}" : $"예상 피해 {normal} · 크리티컬 {critical}";
        string second = aim ? $"예상 피해 {normal} · {hitRate}" : hitRate;
        return basis.Length > 0 ? $"{first}\n{second} · {basis}" : $"{first}\n{second}";
    }

    /// <summary>타이밍 명중을 맞춘 인물과 그 몫(%) — 그 행동이 끝날 때까지 그 인물의 명중 굴림(피해 · 상태이상)에 더한다(<see cref="TimedHit.WantedAim"/>).</summary>
    internal UnitState? _timedAimBy;
    internal int _timedAimBonus;

    internal int AimBonus(UnitState a) => a == _timedAimBy ? _timedAimBonus : 0;

    /// <summary>파트1 무기 덧기술 — 행동 끝에 쏠 것(시전자, work, 칸). 한 행동에 한 번(<see cref="_part1ProcBy"/>).</summary>
    internal (int User, WorkData Work, int Col, int Row)? _part1Proc;

    /// <summary>이번 차례에 덧기술이 이미 터진 인물(원본 +0x265 — 차례가 바뀌면 지운다).</summary>
    internal UnitState? _part1ProcBy;

    /// <summary>이번 차례에 기술 · 공격을 한 인물 — 파트1 휴식이 자세를 켤지 본다(원본 남은 행동 +0x1c4).</summary>
    internal UnitState? _part1ActedBy;

    /// <summary>행동 끝에 되받아칠 것(파트1 반격 · 역습 자세) — 맞은 이, 때린 이, 맞은 이의 기본 공격 work. 맞은 이마다 하나.</summary>
    internal readonly List<(UnitState By, UnitState Target, WorkData Basic)> _part1Counters = [];

    /// <summary>되받아치기를 걸어 둔다 — 맞은 이의 기본 공격이 있고 때린 이가 다른 사람일 때. 같은 맞은 이는 마지막 것만(원본 +0x220 덮어쓰기).</summary>
    internal void QueuePart1Counter(UnitState by, UnitState target, WorkData w)
    {
        if (by == target || by.Data is not { } d || Work(d.BasicWorkId) is not { } basic) return;
        _part1Counters.RemoveAll(c => c.By == by);
        _part1Counters.Add((by, target, basic));
    }

    /// <summary>파트1 자세를 켠다 — 자세 몸짓(동작 21 = 모션 63~65, <c>0x10035e00(0x15, 방향, 10000)</c>)을 한 번 하고 선다.</summary>
    internal void EnterPart1Stance(UnitState u, int ability)
    {
        Part1.Stances[u] = ability;
        // 몸짓은 한 번만(사용자 보고: 반격 몸짓이 계속 되풀이된다) — 전에는 서기 대신 되풀이할 동작(IdleAction)으로 걸었다.
        PlayAction(u, Part1StanceAction);
        Popup(u, Part1Rules.StanceName(ability), 0xFFB0E0FF, 15);
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"{host._lastTime:F2} part1 stance {u.ChrCode}: {ability} {Part1Rules.StanceName(ability)}" + Environment.NewLine);
    }

    internal void ClearPart1Stance(UnitState u)
    {
        Part1.Stances.Remove(u);
        if (u.IdleAction == Part1StanceAction) u.IdleAction = -1;
    }

    /// <summary>파트1 자세 몸짓 — 파트1 동작 21(0x15). 16 이상은 40 뒤 자리(<see cref="Part1Game.ActionMap"/>).</summary>
    internal const int Part1StanceAction = Part1Game.ExtraBase + 21;

    /// <summary>파트1 탈것의 「내리기」 work(어빌리티 20166 · Attack 399).</summary>
    internal const int Part1DismountWork = 20399;

    /// <summary>덧기술을 쏘는 중 — 파트1 기술이 앞머리 없이 바로 돈다(<see cref="Part1Skill.Routine"/>).</summary>
    internal bool _part1ProcRunning;

    internal void ApplyWork(UnitState a, WorkData w, UnitState t, List<UnitState> dying)
    {
        if (host._db == null || a.Data == null || t.Data == null || t.Hp <= 0) return;
        // 판정에는 상태이상까지 얹은 능력치를 쓴다(1 DEX −1 · 40 DEP −1 · 30~32 보정).
        // 군단 부하의 DEX 는 대장 것이다(0x1007ae50, 감사3 L2) — CombatData 가 바꿔 준다.
        // 파트1 판은 피해만 파트1 식(세기 ÷ 방어)으로 굴린다 — Part1Rules(사용자 결정 「중간안」).
        var (amount, result, crit) = (host._scene.IsPart1 ? Part1.Resolve(host._rng, a, t, w, AimBonus(a)) : null)
            ?? host._db.Resolve(host._rng, CombatData(a)!, a.Tp, host.TuningScr.AttackSoul(w, a.Soul), CombatData(t)!, t.Tp, t.Hp, t.MaxHp, w, t.Stance, a.Status(29), AimBonus(a));
        bool timed = a == _timedCritBy && w.IsDamage;
        if (timed)
        {
            // 빗나갔으면 맞을 때까지 다시 굴리고, 그래도 안 맞으면(명중률 0) 굴림 없이 바탕 피해로 맞힌다. 한 번만 치는 경우에는 피해를 2배로 —
            // 굴림이 크리티컬이었으면 그 배율(Num 23)을 걷어 내고 건다(겹치지 않는다). 두 번 치는 동작이면 타격마다 굴린 그대로다.
            for (int tries = 0; result == 3 && tries < 200; tries++)
                (amount, result, crit) = host._db.Resolve(host._rng, CombatData(a)!, a.Tp, host.TuningScr.AttackSoul(w, a.Soul), CombatData(t)!, t.Tp, t.Hp, t.MaxHp, w, t.Stance, a.Status(29));
            if (result == 3)
                (amount, result, crit) = ((host._db.N(3) - host._db.Rdp(CombatData(t)!, t.Hp, t.MaxHp)) * host._db.Atk(CombatData(a)!, host.TuningScr.AttackSoul(w, a.Soul), w.Power) / Math.Max(1, host._db.N(3)), 2, false);
            if (result == 2 && _timedPercent == 0) { if (!crit) (amount, crit) = (amount * host._db.N(23) / 100, true); }
            else if (result == 2 && _timedPercent != 100) (amount, crit) = ((crit ? amount * 100 / Math.Max(1, host._db.N(23)) : amount) * _timedPercent / 100, true);
        }

        // 파트1 대상 거르기(0x10041880, 표 0x1011c084 — Part1Rules.TargetFilter) — 선택자 10~28 은 상태 길(0x10042290)이 맨 먼저 걸러
        // 떨어지면 결과 3 「Miss」(0x1003e3dd). 편은 안 본다: 같은 편 기계에게 블래스 · 스피디 · 블레이드를 걸어도 Miss.
        if (host._scene.IsPart1 && Part1.AttackOf(w) is { Formula: >= 10 and <= 28 } filtered && !Part1.TargetFilter(filtered.Formula, t))
        {
            if (Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"part1 filter {a.ChrCode} → unit {Array.IndexOf(host._units, t)} chr {t.ChrCode} {filtered.Name}(식 {filtered.Formula}): 대상 거르기에서 떨어짐 → Miss" + Environment.NewLine);
            ShowNumber(t, host._db.T(42) is { Length: > 0 } fm ? fm : "Miss", MissColor);
            return;
        }
        // 파트1 힐 · 힐 윈드(세기 식 6) — 최대 HP × 세기 ÷ 20000(원본). 보여 주는 꼴은 파트2 회복 그대로.
        // 기계(칸 10 = 2)는 회복량 0 — 원본은 0 을 「Miss」로 띄운다(0x10041430 → 0x1003dc30).
        if (result == 1 && host._scene.IsPart1 && Part1.HealAmount(a, t, w) is { } part1Heal)
        {
            if (part1Heal == 0 && Part1.IsMachine(t))
            {
                if (Trace)
                    System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                        $"part1 heal {a.ChrCode} → unit {Array.IndexOf(host._units, t)} chr {t.ChrCode} work {w.Id}: 기계라 0 → Miss" + Environment.NewLine);
                ShowNumber(t, host._db.T(42) is { Length: > 0 } hm ? hm : "Miss", MissColor);
                return;
            }
            amount = part1Heal;
        }
        if (result == 1)
        {
            int before = t.Hp;
            t.Hp = Math.Min(t.MaxHp, t.Hp + amount);
            host.Stats.Heal(a, w, t.Hp - before);
            // 회복은 떠오르지 않고 옛 HP 에서 새 HP 로 세어 올라간다(노랑).
            ShowNumber(t, host._db.T(159), HealColor2, rise: false, count: (before, t.Hp));
            ApplyAilments(a, t, w);
            MarkBuffed(a, t, w);
            return;
        }
        // 격려(어빌리티 10) — 대상 SOUL 을 위력만큼 올린다(0x10091ae0). 공통 함수를 안 거쳐 19(소울 정지)도 무시하고, 최대치에서 자른다.
        if (w.AbilityId == EncourageAbility)
        {
            int before = t.Soul;
            t.Soul = Math.Min(t.MaxSoul, t.Soul + Math.Max(0, (int)w.Power));
            ShowNumber(t, $"{host._db.T(41)} +{t.Soul - before}", HealColor2);
            if (Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"encourage work {w.Id}: {a.ChrCode} → {t.ChrCode} SOUL {before} → {t.Soul} (위력 {w.Power})" + Environment.NewLine);
            MarkBuffed(a, t, w);
            return;
        }
        // 쇼크(어빌리티 39) — 피해 판정 없이 대상 SOUL 을 위력만큼 깎는다(0x100ae140, 격려의 반대). 전에는 아무 효과가 없었다(fg-21 ⑥).
        if (w.AbilityId == ShockAbility)
        {
            int drain = Math.Min(Math.Max(0, (int)w.Power), t.Soul);
            t.Soul -= drain;
            ShowNumber(t, $"{host._db.T(41)} -{drain}", MissColor);
            PlayHitReaction(t, damaged: false);
            // 적의 SOUL 을 깎은 것도 약화라 보조 경험치를 센다 — 피해 종류(0)라 MarkBuffed 가 걸러 빠져 있었다(사용자 요청 menu-88).
            if (drain > 0 && w.AbilityId != 0 && t.IsAlly != a.IsAlly) _buffedAllies.Add(t);
            return;
        }
        // 블랙홀(115) — 피해 판정이 없다. 화면 HP 가 위력(500~1000)보다 적은 유닛만 HP 0(0x1008d85f~0x1008d885, ba-20 D2).
        // 명중 굴림·상태·맞음 SOUL·처치 보상이 없고(1016 을 안 보냄 — 가설), 시전자·아군도 범위에 든다(범위 대상 5).
        if (w.AbilityId == BlackHoleAbility)
        {
            if (t.Hp < w.Power && _demo == null)
            {
                t.Hp = 0;
                if (!SurvivesFatal(t)) dying.Add(t);
            }
            return;
        }
        // 파트1 속성 부여(화이어 · 아이스 · 썬더 블레이드) — 대상 무기에 그 속성을 건다(Part1Rules.Enchant).
        if (host._scene.IsPart1 && Part1.EnchantOf(w) is > 0 and var element)
        {
            Part1.Enchant[t] = element;
            Popup(t, element switch { 1 => "화염 부여", 2 => "빙한 부여", _ => "전격 부여" }, 0xFFFFC060, 13);
            return;
        }
        if (ApplyPart1Support(a, t, w, result)) return;   // 파트1 보조 마법(힐 · 큐어 · 스피디 · 배리어 …) — 파트2 보조 틀 대신
        if (!w.IsDamage)
        {
            // 보조기도 빗나가면 「Miss」, 들어가면 맞음 동작(원본 반응표 — 전에는 아무 표시가 없었다).
            // 명중 굴림은 거는 함수(0x1007bcc0) 안에 있다 — 그 결과가 3(명중·레벨 조건 실패)이면 Miss 만 뜨고 맞음 동작·EXP 가 없다(ba-20 C5).
            if (result == 3 || (!ApplyAilments(a, t, w) && t != a))   // 종류 2·3(큐어 따위)은 상태이상만 건다. 제게 거는 것(방어·회피 자세)은 Miss 를 안 띄운다
            {
                ShowNumber(t, host._db.T(42) is { Length: > 0 } miss ? miss : "Miss", MissColor);
                return;
            }
            MarkBuffed(a, t, w);
            // 맞음 동작은 없다 — 거는 함수는 0(성공)·3(실패)만 돌려줘 「2 → 맞음 동작」 가지(0x1007a2ba)가 죽은 코드다(ba-20 K6).
            return;
        }
        if (host._scene.IsPart1 && result is 2 or Part1Rules.Blocked or Part1Rules.CounterHit) FlashPart1Shield(t, Part1.SelectorOf(a, w));
        // 상태이상 보정(7·13·14)은 <b>판정 함수 안에서</b> 끝나고, 「Miss」는 그 뒤에 남은 양으로 가른다(0x10078e60).
        amount = ScaleDamage(a, t, AilmentDamage(a, t, amount));
        if (timed && amount <= 0) amount = 1;   // 타이밍 크리티컬은 「Miss」로 떨어지지 않는다
        bool part1Guard = host._scene.IsPart1 && result is Part1Rules.Blocked or Part1Rules.Dodged or Part1Rules.CounterDodge;
        bool part1Dodge = part1Guard && result is Part1Rules.Dodged or Part1Rules.CounterDodge;
        // 파트1 반격(결과 6) · 역습(결과 7) 자세 — 맞은 이의 기본 공격 사거리에 때린 이가 있으면 행동 끝에 기본 공격으로 되받아친다(0x1003e01e · 0x1003e115).
        if (host._scene.IsPart1 && result is Part1Rules.CounterHit or Part1Rules.CounterDodge) QueuePart1Counter(t, a, w);
        if (result == 3 || amount <= 0 || part1Dodge)
        {
            if (Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"{host._lastTime:F2} {(IsFoeSide(a) && !IsFoeSide(t) ? "foe" : "ally")} miss {a.ChrCode} → {t.ChrCode}: {(result == 3 ? "빗나감" : $"피해 {amount}")} work {w.Id} (TP {a.Tp} vs {t.Tp}, HP {t.Hp}/{t.MaxHp})" + Environment.NewLine);
            ShowNumber(t, host._db.T(42) is { Length: > 0 } m ? m : "Miss", MissColor);
            // 파트1 피함(결과 5 · 7) — 「빗나감」 글에 더해 때린 이 쪽을 보고 동작 54 를 한다(0x1003dfda). 막았는데 피해가 0 이어도 동작 6(0x1003df9a).
            if (part1Guard) PlayGuardReaction(a, t, result);
            // 빗나가도 원본은 상태이상을 따로 굴리고(0x1007a285 → 0x1007bcc0) 피격 가속(10)도 건다(0x10079f59, 결과 2·3 모두) — fg-21 ⑪.
            RushTp(t);
            ApplyAilments(a, t, w);
            return;
        }

        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"{host._lastTime:F2} {(IsFoeSide(a) && !IsFoeSide(t) ? "foe" : "ally")} hit {a.ChrCode} → {t.ChrCode}: {amount} work {w.Id} (HP {t.Hp}/{t.MaxHp}, 난이도 {_difficulty})" + Environment.NewLine);
        t.LastHitBy = a;                        // 맞았을 때만 적는다(빗나가면 그대로) — 원본 0x10079990
        host.Stats.Hit(a, w, Math.Min(amount, t.Hp));   // 통계에는 실제로 깎인 만큼만
        t.Hp = Math.Max(_demo != null ? 1 : 0, t.Hp - amount);   // 시연 중에는 아무도 안 쓰러진다(SkillDemo.cs)
        RushTp(t);
        ShowNumber(t, $"{host._db.T(159)} {amount}", DamageColor);
        // 파트1 막음(결과 4) — 피해 숫자는 뜨지만 맞기 몸짓 · 불꽃 대신 때린 이 쪽을 보고 동작 6(팔짱)을 한다(0x1003df9a).
        if (part1Guard) PlayGuardReaction(a, t, result);
        else PlayHitReaction(t, damaged: true, spark: !host._scene.IsPart1 || Part1.SparksOnHit(a, w));
        if (crit) PlayCritFlash();
        AddSoul(t, amount / Math.Max(1, host._db.N(43)));
        PlayHurtCry(t);
        ApplyAilments(a, t, w, crit);
        // 파트1 무기 덧효과(분석-파트1-전투 23절) — 무기로 친 치명타면 무기 보정 300~309 의 기술이 한 번 덧붙는다(0x1003dc9f → 메시지 105,
        // 처리기 0x10035661 이 맞은 자리에 쏜다, 차례당 한 번 +0x265). 그 기술(work 20000 + Attack 번호)이 이 게임에 있으면(썬더스탐 20161 …)
        // 행동 끝에 공짜로 쏘고(UseWorkRoutine 의 _part1Proc), 없으면 상태만 건다: 304 중독(파트2 상태 3, 차례마다 최대 HP 5%) · 303 버써크(4).
        // 맞은 이가 쓰러져도 쏜다 — 원본은 피해를 적용한 바로 뒤 메시지를 보낸다(0x1003dce5). 상태만 거는 갈래는 산 대상에게만.
        if (crit && host._scene.IsPart1 && Part1.AttackOf(w) == null && Part1.Fighter(a)?.Weapon1 is { } weapon
            && weapon.ProcAttack() is { Attack: > 0 } proc && _part1ProcBy != a)
        {
            _part1ProcBy = a;
            if (Work(Part1Game.ChrBase + proc.Attack) is { } procWork) _part1Proc = (Array.IndexOf(host._units, a), procWork, t.Col, t.Row);
            var used = new List<int>();
            // 309 암살(Attack 389, 0x1006e860) — 원본은 남은 HP 가 500 이하면 굴림 없이 쓰러뜨린다. 리메이크는 HP 1 을 남긴다
            // (사용자 요청: 터지면 죽어서 너무 억울하다). 1 이하면 아무 일도 없다 · 500 넘어도 아무 일도 없다.
            if (proc.Code == 309)
            {
                if (t.Hp is > 1 and <= 500)
                {
                    t.Hp = 1;
                    used.Add(389);
                    Popup(t, "암살", AilmentPopupColor, 12, rise: 88);
                }
            }
            else if (_part1Proc == null && t.Hp > 0)
            {
                // 상태만 거는 갈래 — 파트1 상태를 가까운 파트2 상태로(Part1Item.Part2Status, 가설). 중독은 차례마다 최대 HP 5%, 나머지는 값 1.
                // 걸리는지는 원본처럼 굴린다 — 같은 편 · INT²/MR · 100 − Luck(선택자마다, 분석-파트1-전투 18절), 보정 247 이면 안 걸린다.
                // 305 최면 · 306 컨디션이상 · 307 혼란은 파트1 상태(Part1Rules.Ailments)로 — 대개는 그 기술(20386 · 20385 · 20384)이 있어 위에서 쏜다.
                if (proc.Code is 305 or 306 or 307)
                {
                    if (Part1.StatusLands(host._rng, a, t, proc.Attack))
                    {
                        LandPart1Ailment(t, proc.Code switch { 305 => Part1Rules.Hypnosis, 306 => Part1Rules.Condition, _ => Part1Rules.Confusion });
                        used.Add(proc.Attack);
                    }
                }
                else if (Part1Item.Part2Status(proc.Code) is > 0 and var status && Part1.StatusLands(host._rng, a, t, proc.Attack))
                {
                    PutAilment(t, (byte)status, (short)(status == 3 ? 5 : 1), used, a);
                    Popup(t, BattleScene.AilmentNames.GetValueOrDefault(status, "상태이상"), AilmentPopupColor, 12, rise: 88);
                }
            }
            if (Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"part1 weapon proc {a.ChrCode} → {t.ChrCode}: {weapon.Name} (보정 {proc.Code} → Attack {proc.Attack}{(_part1Proc != null ? " 쏨" : used.Contains(389) ? " 암살" : used.Count > 0 ? " 상태만" : " 없음")})" + Environment.NewLine);
        }
        // 피해 기술이 적에게 상태이상·약화를 함께 걸었으면(엘레맨탈 파이어의 점화 따위) 그 적도 보조 경험치 대상으로 센다(사용자 요청 — 원본에 없다).
        if (_ailmentLanded && w.AbilityId != 0 && t.IsAlly != a.IsAlly) _buffedAllies.Add(t);
        Counterattack(a, t, amount);
        if (t.Hp > 0) return;
        host.Stats.Kill(a, w);
        // 쓰러뜨린 타에는 불꽃(Obs 73)이 한 장 더 뜬다 — 모션 0(0x10079b84) + 무작위 한 장(0x10079c0a), ba-21 T6.
        if (w.IsDamage) { var (kx, ky) = host.Btl.UnitFoot(t); _effects.Add((HitEffectObs, 0, host._lastTime, kx, ky - HitEffectLift)); }
        // 처치(메시지 1016)는 HP 가 0 이 된 순간 공격자에게 간다 — 47(전투불능 방지)로 살아나도 보상은 받는다(0x10079ab8).
        // SOUL 은 때린 사람이, 경험치는 군단 부하가 쓰러뜨렸으면 <b>대장</b>이 받는다(0x10079b14).
        // 처치 보상(1016)은 받는 유닛에게 SOUL +Num30 과 EXP 를 같이 준다(0x1007214f) — 부하가 쓰러뜨리면 둘 다 대장 몫(ba-20 K4).
        AddSoul(a.LeaderIndex >= 0 && a.LeaderIndex < host._units.Length ? host._units[a.LeaderIndex] : a, host._db.N(30));
        GainKillExp(a.LeaderIndex >= 0 && a.LeaderIndex < host._units.Length ? host._units[a.LeaderIndex] : a, t);
        if (SurvivesFatal(t)) return;
        dying.Add(t);
    }

    /// <summary>10(피격 가속) — 맞으면(빗나가도) TP 가 값% × STP 만큼 앞당겨진다(0x10079f59: 값 × 최대TP ÷ 제수 ÷ 100). 전에는 값% × 제수였다.</summary>
    internal void RushTp(UnitState t)
    {
        if (t.Status(10) is var rush and > 0) t.Tp = Math.Min(t.MaxTp, t.Tp + rush * t.Stp / 100);
    }

    /// <summary>자기 자리에 쓰는 work(모드 0·2)면 겨냥 없이 바로 쓴다.</summary>
    internal bool UseSelfCentredWork(WorkData w)
    {
        if (!w.SelfCentred) return false;
        // 적만 맞는 제자리 범위기(피드백 — 범위 방식 +0x1e = 1)는 범위 안에 적이 없으면 안 쓴다(사용자 요청). 원본이 막는지는 확인하지 않았다 —
        // 헛쓰면 SOUL·TP 만 잃어서 막아 둔다. 내가 고를 때만 — AI 는 대상이 있을 때만 고른다.
        if (w.AreaMode == 1 && WorkTargets(w, host._units[_turn], host._units[_turn].Col, host._units[_turn].Row).Count == 0)
        {
            host.Toast("범위 안에 적이 없습니다");
            return true;
        }
        ConsumeTargetItem();          // 겨누지 않는 아이템(라이징스톰·블리자드캡슐)도 쓰면 하나 준다(0x100698af) — CancelTargeting 앞이어야 한다
        CancelTargeting();
        _routine = UseWorkRoutine(_turn, w, -1, host._units[_turn].Col, host._units[_turn].Row, []);
        return true;
    }

    internal static Facing FacingToward(int fromCol, int fromRow, int toCol, int toRow)
    {
        int dx = toCol - fromCol, dy = toRow - fromRow;
        if (Math.Abs(dx) >= Math.Abs(dy)) return dx >= 0 ? Facing.Right : Facing.Left;
        return dy >= 0 ? Facing.Down : Facing.Up;
    }

    internal void CheckOutcome()
    {
        if (_outcome.Length > 0) return;
        // 파트1 — 고를 레벨업 어빌리티가 남았으면 승패를 그 뒤로 미룬다. 마지막 적을 잡은 한 방에 레벨이 오르면 고르기 창과 전투 끝이 같이 떠서,
        // 못 고른 창이 다음 챕터 전투의 배치 화면까지 따라왔다(사용자 보고). 다 고르면 UpdateLevelUp 이 다시 본다.
        if (Part1PickPending) { _outcomeAfterLevelUp = true; return; }
        // 먼저 이벤트 스크립트 — 「몇 턴 버티기」·「누구를 지키기」처럼 전멸 말고 다른 조건으로 끝나는 전투가 있다.
        _eventCheckDue |= 1 << 2;               // 쓰러짐은 행동 끝에서 본다
        RunEvents();
        // 방금 켜진 사건이 아직 도는 중이면 그것이 끝나기를 기다린다 — 전멸 사건(대사 → 행동 6 필드)이 도는 사이에 기본 「승리」를 내면
        // 사건이 정한 행선지를 잃고 Btl 자료의 다음 전투로 샜다. Btl 0147 사건 3(→ Fld 0073 → 0074 깃발 99=6)을 건너뛰고 Btl 0312 로 가
        // 델라리움 연구소로(0148)가 안 열렸다(사용자 보고). 틱마다 다시 보니 사건이 끝난 뒤에 판정한다.
        if (_outcome.Length > 0 || EventsBusy) return;
        // 머리 워드 9 가 0 인 전투는 엔진이 전멸을 아예 안 본다(0x1006ed10 첫머리) — 승패는 스크립트(행동 11·10·6)만 낸다.
        // 적을 다 잡아도 조건(특정 칸 도달 따위)을 채워야 넘어간다. 전에는 전멸이면 끝내고 스크립트의 나가는 길을 대신 골랐다(fg-21 ⑬).
        // 파트1 판은 탈출 · 도달처럼 전멸을 안 보는 판이라도, 맵 밖에서 기다리는 증원까지 적이 하나도 안 남으면 이긴다
        // (사용자 요청: 「다 죽였으면 클리어 — 도망갈 이유가 없으면」, 원본은 조건 칸에 닿아야 끝난다). 나가는 길은 스크립트의 승리 갈래를 따른다.
        if (!host._scene.EngineJudgesWipe && host._scene.IsPart1
            && host._units.Any(u => !u.IsAlly) && !host._units.Any(u => u.Alive && !u.IsAlly)
            && host._units.Any(u => u.Alive && u.OnField && u.Side == 4))
        {
            ScriptedDestinationOnWipe();
            Part1WinFlagsOnWipe();
            _outcome = "승리 — 적을 모두 쓰러뜨렸습니다"; _outcomeAt = host._lastTime;
            TestRunTrace($"outcome part1 wipe-win btl {host._scene.Id} nextBattle {_eventNextBattle} nextField {_eventNextField} turnNo {_turnNo} t {host._lastTime:F1}");   // 시험 전용
            return;
        }
        if (!host._scene.EngineJudgesWipe)
        {
            // 원본은 여기서 AI 차례만 영영 돈다(0x1006ed6c — 시스템 메뉴도 못 여는 막다른 길, Btl 0079·0102·0252·0221).
            // 끝날 길이 없을 때만 패배로 닫는다(ba-20 F, 원본에 없는 편의): 편 4 가 맵에 섰던 전투에서 맵 위·대기 중인 편 3·4 가 하나도 없고
            // 틱·타이머로 끝나는 사건(0076·0083·0090)도 안 남았을 때.
            // 스크립트의 「아군 전멸」·「그 인물 사망」 사건(401[4]·200·201)이 먼저 터질 틈을 준다 — 그 조건이 5초(게임 시간) 이어질 때만 닫는다.
            bool dead = host._units.Any(u => u.Side == 4 && !u.Alive)
                        && !host._units.Any(u => u.Alive && u.Side is 3 or 4)
                        && !PendingTimedEnd();
            if (!dead) _softlockSince = -1;
            else if (_softlockSince < 0) _softlockSince = host._lastTime;
            else if (host._lastTime - _softlockSince >= 5)
            {
                _softlockSince = -1;
                _outcome = "패배 — 아군이 모두 쓰러졌습니다"; _outcomeAt = host._lastTime;
                TestRunTrace($"outcome softlock-lose btl {host._scene.Id} turnNo {_turnNo} t {host._lastTime:F1}");   // 시험 전용
            }
            return;
        }
        // 전장에 선 사람만 센다(원본도 맵 밖은 안 센다).
        // 패배는 <b>사람이 모는 편(편 4)</b>이 다 쓰러졌을 때다(0x1006ec00 — 세력 +8 조종 주체 0 인 편만) — 편 3 동맹이 남아도 진다.
        // 마지막 적과 마지막 아군이 같은 행동에서 쓰러지면 패배다 — 0x1006edb0 은 플레이어 0 을 먼저 본다(ba-20 F 덤).
        if (!host._units.Any(u => u.Alive && u.OnField && u.Side == 4))
        {
            _outcome = "패배 — 아군이 모두 쓰러졌습니다"; _outcomeAt = host._lastTime;
            TestRunTrace($"outcome wipe-lose btl {host._scene.Id} turnNo {_turnNo} t {host._lastTime:F1}");   // 시험 전용
        }
        else if (!host._units.Any(u => u.Alive && u.OnField && !u.IsAlly))
        {
            ScriptedDestinationOnWipe();
            _outcome = "승리 — 적을 모두 쓰러뜨렸습니다"; _outcomeAt = host._lastTime;   // 음악은 배너와 함께 16틀 뒤(UpdateOutcomeBanner, 사운드 B1)
            TestRunTrace($"outcome wipe-win btl {host._scene.Id} nextBattle {_eventNextBattle} nextField {_eventNextField} turnNo {_turnNo} t {host._lastTime:F1}");   // 시험 전용
        }
    }

    internal double _softlockSince = -1;

    /// <summary>아직 덜 터진 사건 가운데 틱·타이머 조건(2·3)으로 전투를 끝내는(행동 6·10·11) 것이 남았나.</summary>
    internal bool PendingTimedEnd()
    {
        var events = _events;
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.MaxFire > 0 && i < _eventFired.Length && _eventFired[i] >= e.MaxFire) continue;
            if (e.Conditions.Any(c => c.Code is 2 or 3) && e.Actions.Any(x => x.Code is 6 or 10 or 11)) return true;
        }
        return false;
    }

    /// <summary>배너·음악 없이 끝나는 결과(행동 10·6 의 결과 5·6, 행동 11[1] 의 결과 2) — 원본 상태 24 는 결과 1·4 만 그린다(0x1006afa0).</summary>
    internal bool _outcomeQuiet;

    /// <summary>조용한 결과가 저절로 넘어가는 때(초).</summary>
    internal double _outcomeLeaveAt;

    /// <summary>
    /// 이벤트 행동 11·10·6 이 적는 결과. <paramref name="quiet"/> 면 배너 없이 넘어간다 — 상태 24 는 결과와 상관없이 어둡게(하위 0) →
    /// 16틱 → 하위 2 에서 입력 또는 틱 &gt; 120 이라 <b>16 + 121 = 137틱</b>(감사4 C9). 배너 결과의 음악은 배너와 함께 건다(사운드 B1).
    /// </summary>
    internal void SetEventOutcome(bool win, bool quiet = false)
    {
        if (_outcome.Length > 0) return;
        _outcome = win ? "승리" : "패배";
        _outcomeAt = host._lastTime;
        _outcomeQuiet = quiet || host._scene.IsPart1 && _part1NoBanner;   // 파트1 106 — 배너 없이
        // 시험 전용: 사건이 낸 결과를 남긴다(DUELDX_TESTRUN 일 때만).
        TestRunTrace($"outcome event btl {host._scene.Id} win {win} quiet {quiet} 사건 {_runningEvent} 줄 {_eventPc - 1} nextBattle {_eventNextBattle} nextField {_eventNextField} turnNo {_turnNo} t {host._lastTime:F1}");
        _outcomeLeaveAt = host._lastTime + (OutcomeBannerDelayTicks + OutcomeAutoTicks + 1) / TicksPerSecond;
        // 파트1 의 배너 없는 끝은 어둡게도 기다리지도 않고 곧 넘어간다(0x1002eaf4 · 0x1002ed7f — 하위 0 에서 바로 +0x28 = 1).
        if (host._scene.IsPart1 && _outcomeQuiet) _outcomeLeaveAt = host._lastTime;
    }

    // ── 표시 ─────────────────────────────────────────────────────────────────

    internal const uint HealColor = 0xFF60E060;

    /// <summary>
    /// 그 동작에서 판정이 나는 시각들(초, 동작 시작 기준) — 모션의 타격 키(종류 6) 간격을 그대로 쓰되
    /// 첫 타는 사용자가 원하는 대로 <b>0.05초</b>에 낸다. 타격 키가 없으면 한 번만.
    /// 다만 <b>원거리</b>(사거리 1칸 넘음)는 모션의 타격 키 그 틱에 낸다 — 총은 쏘는 컷에 타격 키가 있어서, 0.05초로 당기면
    /// 크리스티앙처럼 피해 숫자가 총을 쏘기 전에 떴다(사용자 보고).
    /// </summary>
    internal List<double> HitTimesFor(UnitState u, int action, bool ranged = false)
    {
        const double first = 0.05;
        if (!host._sprites.TryGetValue(u.ChrCode, out var sprite) || sprite.Clip(action, u.Facing) is not { Hits.Count: > 0 } clip)
            return [first];
        var starts = clip.Hits.Select(h => h.Start).OrderBy(t => t).ToList();
        double at = ranged ? Math.Max(first, starts[0] / TicksPerSecond) : first;
        return [.. starts.Select(t => at + (t - starts[0]) / TicksPerSecond)];
    }

    /// <summary>타격 키 어빌리티 125(Hunter)가 고르는 work — 원본 <c>0x100322e0</c> 에 박혀 있다.</summary>
    internal const int HunterAbility = 125, HunterWork = 1468;

    /// <summary>그 동작의 타격 키마다 적힌 어빌리티 번호 — <see cref="HitTimesFor"/> 와 같은 차례(시각 순). 키가 없으면 빈 목록.</summary>
    internal List<int> HitAbilitiesFor(UnitState u, int action) =>
        host._sprites.TryGetValue(u.ChrCode, out var sprite) && sprite.Clip(action, u.Facing) is { Hits.Count: > 0 } clip
            ? [.. clip.Hits.OrderBy(h => h.Start).Select(h => h.Ability)] : [];

    /// <summary>
    /// 그 인물 그림에 그 동작이 비어 있으면(키 없음) 대신 그릴 동작 — 7(쏘기·휘두르기), 그다음 5. 판정 자리는 원래 동작 목록대로 둔다.
    /// 원거리 기본공격(work 6)은 동작 9 를 부르는데, 팡테온가드(그림 1111)는 9 가 비어 있고 사격(소리 8·총구 불꽃 108)이 7 에 있어
    /// 공격 이펙트가 안 나왔다(사용자 보고: Btl 0154).
    /// </summary>
    internal int DrawnAction(UnitState u, int action)
    {
        if (!host._sprites.TryGetValue(u.ChrCode, out var sprite)) return action;
        bool Empty(int a) => sprite.Clip(a, u.Facing) is not { Keys.Count: > 0 };   // 키가 하나도 없는 동작(정지 한 장짜리는 둔다)
        if (!Empty(action)) return action;
        foreach (int alt in new[] { 7, 5 })
            if (alt != action && !Empty(alt)) return alt;
        return action;
    }

    internal void PlayAction(UnitState u, int action)
    {
        double seconds = host._sprites.TryGetValue(u.ChrCode, out var sprite) ? sprite.ActionSeconds(action, u.Facing) : 0;
        if (Trace)
        {
            var clip = sprite?.Clip(action, u.Facing);
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"{host._lastTime:F2} chr {u.ChrCode} action {action} facing {u.Facing} clip {(clip?.Id.ToString() ?? "none")} len {clip?.Length ?? 0} keys {clip?.Keys.Count ?? 0} seconds {seconds:F2}" + Environment.NewLine);
        }
        u.PlayAction(action, seconds > 0 ? seconds : 0.3);
        host.ScheduleActionSounds(u, action);
    }

    internal void Popup(UnitState u, string text, uint color, float size = 18, int rise = 70)
    {
        var (x, y) = host.Btl.UnitFoot(u);
        _popups.Add((text, size, x, y - rise, host._lastTime, color));
    }

    internal void DrawPopups()
    {
        _popups.RemoveAll(p => host._lastTime - p.Start > 1.2);
        foreach (var (text, size, x, y, start, color) in _popups)
        {
            int rise = (int)((host._lastTime - start) * 30);
            int w = host.BattleTextSize(text, color, size).W;
            host.BattleText(text, x - w / 2 + 1, y - rise + 1, 0xFF000000, size);
            host.BattleText(text, x - w / 2, y - rise, color, size);
        }
    }

    /// <summary>인물 발밑의 HP(초록·빨강)·TP(노랑) 막대.</summary>
    internal void DrawGauges()
    {
        foreach (var u in host._units)
        {
            if (!u.Alive || !u.OnField || u.MaxHp <= 0) continue;   // 판 밖(증원 전·퇴장) 유닛의 막대가 빈 칸에 뜨지 않게(ba-20 G22)
            var (fx, fy) = host.Btl.UnitFoot(u);
            int w = TileW - 8, x = fx - w / 2, y = fy + TileH / 2 - 7;
            host.FillRect(x - 1, y - 1, w + 2, 7, 0xC0000000);
            host.FillRect(x, y, w * u.Hp / u.MaxHp, 3, u.IsAlly ? 0xFF50D060 : 0xFFE05050);
            if (u.MaxTp > 0) host.FillRect(x, y + 3, w * Math.Clamp(u.Tp, 0, u.MaxTp) / u.MaxTp, 2, 0xFFE8C040);
            // 아군이면 막대 왼쪽에 작은 원 안의 레벨(사용자 요청) — 적은 안 보인다. 파트1 은 누적 경험치 ÷ 100(Part1Chr.Level — EXP 100 마다
            // 어빌리티를 하나 올린 횟수)을 파트2 처럼 보인다(사용자 요청). 전투 중 오른 것까지 보이게 파트1 인물 자료에서 읽는다.
            int? level = !u.IsAlly ? null : host._scene.IsPart1 ? Part1.Fighter(u)?.Chr.Level : u.Data?.Level;
            if (level is { } shown)
            {
                string lv = shown.ToString();
                var (tw, th) = host.BattleTextSize(lv, 0xFFFFFFFF, GaugeLevelFont);
                int r = Math.Max(5, (tw + 3) / 2), cx = x - 2 - r, cy = y + 2;
                host.FillCircle(cx, cy, r + 1, 0xE0000000);
                host.FillCircle(cx, cy, r, 0xE0305070);
                host.BattleText(lv, cx - tw / 2, cy - th / 2, 0xFFFFFFFF, GaugeLevelFont);
            }
            DrawAilmentChips(u, fx, y + 7);
        }
    }

    internal const float GaugeLevelFont = 7f;

    /// <summary>
    /// 체력바 아래 상태이상 딱지 — 칸 셋 가운데 걸린 것만 두 글자로, 해로운 것은 빨강 · 이로운 것은 파랑 바탕(사용자 요청 fa-64).
    /// 원본에는 없다 — 원본은 Status · 정보 창의 칸에서만 보인다.
    /// </summary>
    internal void DrawAilmentChips(UnitState u, int cx, int top)
    {
        var chips = new List<(string Text, uint Back)>();
        for (int k = 0; k < 3; k++)
        {
            int id = u.StatusId[k];
            if (id is 0 or 44 or 45 or 46) continue;
            int score = id is 1 or 40 ? -1 : AilmentScore(id, u.StatusValue[k]);   // DEX·DEP 저하는 원본 점수표엔 0 이지만 해롭다
            chips.Add((AilmentChipText(id, u.StatusValue[k]), score < 0 ? 0xE0A02828u : score > 0 ? 0xE02858B0u : 0xE0505860u));
        }
        if (host._scene.IsPart1 && Part1.Ailments.TryGetValue(u, out var part1))   // 파트1 최면 · 컨디션이상 · 혼란(Part1Rules.Ailments)
            foreach (int s in part1.Order()) chips.Add((Part1AilmentChip(s), 0xE0A02828u));
        // 파트1 자세(Part1Rules.Stances) — 원본은 자세 몸짓만 보인다. 몸짓이 빈 인물이 많아(모션 63~65) 파란 딱지를 덧붙인다(리메이크).
        if (host._scene.IsPart1 && Part1.StanceOf(u) is > 0 and var stance)
            chips.Add((stance == 80 ? "완방" : Part1Rules.StanceName(stance), 0xE02858B0u));
        if (chips.Count == 0) return;
        const int pad = 2, gap = 1;
        var sizes = chips.Select(c => host.BattleTextSize(c.Text, 0xFFFFFFFF, AilmentChipFont)).ToList();
        int h = sizes.Max(s => s.H) + 1, total = sizes.Sum(s => s.W + 2 * pad) + gap * (chips.Count - 1), x = cx - total / 2;
        for (int k = 0; k < chips.Count; k++)
        {
            int w = sizes[k].W + 2 * pad;
            host.FillRect(x - 1, top - 1, w + 2, h + 2, 0xC0000000);
            host.FillRect(x, top, w, h, chips[k].Back);
            host.BattleText(chips[k].Text, x + pad, top + (h - sizes[k].H) / 2, 0xFFFFFFFF, AilmentChipFont);
            x += w + gap;
        }
    }

    internal const float AilmentChipFont = 6f;

    /// <summary>딱지에 쓸 두 글자 — 값의 부호로 뜻이 갈리는 것(13·14·38)은 화살표를 붙인다. 표에 없으면 이름 앞 두 글자.</summary>
    internal static string AilmentChipText(int id, int value) => id switch
    {
        1 => "둔화", 4 => "광폭", 7 => "보호", 8 => "재생", 10 => "가속", 11 or 42 => "경험", 12 => "봉인",
        13 => value < 0 ? "공↓" : "공↑", 14 => value < 0 ? "방↓" : "방↑", 15 => "SL↓", 16 => "TP↓", 17 => "HP↓",
        18 => "SL비", 19 => "정지", 20 => "TP비", 22 or 23 or 24 => "사망", 25 => "속박", 26 => "불면", 27 => "무장",
        29 => "무기", 38 => value < 0 ? "느림" : "빠름", 40 => "DEP↓", 47 => "불사",
        _ => AilmentNames.TryGetValue(id, out var n) ? n[..Math.Min(2, n.Length)] : id.ToString(),
    };

    /// <summary>어빌리티 대상 고르는 중이면 사거리 칸(노랑)을 깐다.</summary>
    internal void DrawWorkRange()
    {
        if (_targetWork >= 0 && _targetIsBasicAttack && HasAttackCursor)
        {
            // 노리는 적(물체) 칸에 깜빡이는 빨간 테두리
            var (tc, tr) = _attackObject is { } o ? (o.Col, o.Row) : (host._units[_attackCursor].Col, host._units[_attackCursor].Row);
            uint alpha = (uint)(160 + 95 * (0.5 + 0.5 * Math.Sin(host._lastTime * Math.PI * 4)));
            uint color = alpha << 24 | 0xFF3030;
            int cx = tc * TileW, cy = host.CellTop(tc, tr);
            for (int k = 0; k < 3; k++) host.StrokeRect(cx + k, cy + k, TileW - 2 * k, TileH - 2 * k, color);
            return;
        }
        if (_targetWork < 0 || _targetIsBasicAttack || _turn < 0 || Work(_targetWork) is not { } w) return;
        var u = host._units[_turn];
        // 원본 상태 11(어빌리티 대상 고르기, 분석-UI 「칸 깃발」): 사거리는 층 12 붉은색 (255,100,40)×74/256 = (73,28,11) 을
        // <b>가산</b>으로 칠하고 테두리는 같은 색 불투명 41×33. 겨눈 칸의 효과 범위는 층 0 주황 (255,170,40) → (73,49,11) 로 덧칠한다
        // (그리는 차례는 층 0 이 먼저라 주황이 이긴다).
        var aim = _attackCursor >= 0 ? (host._units[_attackCursor].Col, host._units[_attackCursor].Row) : _aimCell;
        var splash = aim is { } ac ? EffectCells(w, u, ac.Item1, ac.Item2) : [];
        // 파트1 겨누기 +0x31 = 7(혼 · 혈랑마혼 · 비연참 · 댓쉬 · 워프)은 칠하기도 칸 거르개 7 을 쓴다 — 칠하기 0x10038bb0 은 다른 기술이면 거르기 2(아무 칸)로
        // 칠하지만 +0x31 = 7 이면 7 을 넘겨(0x10038c13 · 0x10038c31) 설 수 있는 빈 칸(0x1008a950 → 0x10089bf0)만 칠해진다.
        // 누르기도 칠해진 칸만 받는다(0x100321e5 칸 깃발 &0xffc). 전에는 사거리 칸을 다 칠해 못 누르는 칸(적 옆 · 유닛 칸)도 붉었다.
        bool landOnly = Part1.AttackOf(w) is { AimFilter: 7 };
        for (int row = 0; row < host.Rows; row++)
            for (int col = 0; col < host.Cols; col++)
            {
                bool inRange = landOnly ? CanAimAt(w, u, col, row) : InWorkRange(w, u.Col, u.Row, col, row, u), inSplash = splash.Contains((col, row));
                if (!inRange && !inSplash) continue;
                PaintCell(col, row, inSplash ? SplashLayer : RangeLayer);
            }
        // 저절로 겨눈 대상(적 커서나 칸)에도 깜빡이는 빨간 테두리
        if (aim is { } a)
        {
            uint alpha = (uint)(160 + 95 * (0.5 + 0.5 * Math.Sin(host._lastTime * Math.PI * 4)));
            uint color = alpha << 24 | 0xFF3030;
            int cx = a.Item1 * TileW, cy = host.CellTop(a.Item1, a.Item2);
            for (int k = 0; k < 3; k++) host.StrokeRect(cx + k, cy + k, TileW - 2 * k, TileH - 2 * k, color);
        }
    }

    internal string TurnLine()
    {
        if (_turn < 0) return $"틱 {_tick} — 차례를 기다리는 중";
        var u = host._units[_turn];
        string help = !u.IsAlly ? "적군이 움직입니다"
            : _targetWork >= 0 ? (_targetIsBasicAttack ? "클릭·Enter 공격  Tab 다른 적  Esc 취소" : "노란 칸 클릭  Esc 취소")
            : $"{KeyBindings.KeyName(host._keys[KeyAction.MoveUp])}{KeyBindings.KeyName(host._keys[KeyAction.MoveLeft])}{KeyBindings.KeyName(host._keys[KeyAction.MoveDown])}{KeyBindings.KeyName(host._keys[KeyAction.MoveRight])} 걷기  {KeyBindings.KeyName(host._keys[KeyAction.Ring])} 링  {KeyBindings.KeyName(host._keys[KeyAction.Attack])} 공격  {KeyBindings.KeyName(host._keys[KeyAction.Rest])} 휴식  1~4 어빌리티  Esc 취소";
        return $"틱 {_tick}  {(u.IsAlly ? "아군" : "적군")} {host.UnitName(_turn)}  HP {u.Hp}/{u.MaxHp}  TP {u.Tp}/{u.MaxTp}  SOUL {u.Soul}   {help}";
    }

}
