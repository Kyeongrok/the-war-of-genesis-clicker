using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 어빌리티마다 다른 동작·이펙트(ba-10) — 지금까지는 무엇을 쓰든 기본공격 동작을 빌려 썼다.
/// </summary>
/// <remarks>
/// 옵시디안 분석-모션 "전투에서 캐릭터가 쓰는 어빌리티와 그 모션"(도구 <c>tools/re/work_script.py</c>)의 표를 옮겼다.
/// 동작 번호는 캐릭터 Obs 모션(동작 × 3 + 방향)이고, 이펙트는 시전자(<c>Self</c>)나 겨눈 자리에 붙는다.
/// 소리는 동작·이펙트 모션의 소리 키에서 나오므로 따로 적지 않는다(분석-사운드).
/// 원본은 어빌리티 레벨·방향에 따라 가지가 갈리는데(연의 연속 베기 횟수, 블레이드 미사일의 방향별 이펙트),
/// 여기서는 한 가지만 쓴다 — 그만큼 단순하다.
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    /// <summary>이펙트 하나 — 어느 Obs 의 어느 모션을, 대상 자리(또는 내 자리)에서 몇 픽셀 위에 띄우나.</summary>
    /// <param name="Delay">띄우기까지 기다리는 틱(원본 <c>0x100c2530</c>) — 메테오 착탄 따위.</param>
    /// <param name="Count">뿌리개가 흩뿌리는 개수(설정 인자 3) — 대상 둘레 ±20·±10 픽셀에.</param>
    /// <param name="Fly">시전자에서 대상으로 날아가는 이펙트(생성자 <c>0x100c3340</c>·<c>0x100c5940</c>) — 모션 길이 동안 옮긴다.</param>
    /// <param name="Life">수명(틱, <c>0x100c2530</c>) — 0 이면 모션 한 번, 아니면 그동안 모션을 되풀이한다(힐 297:1 120틱 따위, ba-15 R2).</param>
    /// <param name="Facing">그 방향을 볼 때만 뜬다 — 원본 방향 번호 0 위 · 1 왼 · 2 아래 · 3 오른(−1 = 늘). 핸들러의 방향 가지(ba-20 X).</param>
    internal readonly record struct AbilityEffect(int Obs, int Motion, bool OnTarget, int Lift, int Delay = 0, int Count = 1, bool Fly = false, int Life = 0, int Facing = -1);

    /// <summary>
    /// 카운터 블레이드의 이펙트 — 둘 다 시전자 자리(원본은 895 를 네 번 겹쳐 띄운다).
    /// </summary>
    /// <remarks>표(<see cref="AbilityMotions"/>)가 이것을 쓰므로 <b>표보다 먼저</b> 선언해야 한다 — 정적 초기화는 적은 차례대로 돈다.</remarks>
    internal static readonly AbilityEffect[] CounterBlade = [new(1365, 0, false, 0)];   // 칼날 895 는 SpawnCounterBlades 가 다섯 칸에 띄운다

    /// <summary>카운터 블레이드 work(390 · 997~1015).</summary>
    internal static readonly HashSet<int> CounterBladeWorks = [390, .. Enumerable.Range(997, 19)];

    /// <summary>
    /// 카운터 블레이드 0x100a8df0 — 칼날 895:0 다섯을 시전자 <b>앞 두 칸 줄</b>의 가로 −2~+2 칸에 0·4·8·12·16틱 늦춰 띄운다(fg-22).
    /// 전에는 시전자 자리에 하나만 띄웠다.
    /// </summary>
    internal void SpawnCounterBlades(UnitState user)
    {
        var (fx, fy) = user.Facing switch { Facing.Up => (0, -1), Facing.Down => (0, 1), Facing.Left => (-1, 0), _ => (1, 0) };
        // 옆 방향 — 왼쪽을 볼 때도 위 → 아래 차례로 뜬다(0x100a9027~0x100a90ec, ba-20 P10). 전에는 왼쪽만 아래 → 위였다.
        var (sx, sy) = user.Facing == Facing.Left ? (0, 1) : (fy, fx);
        for (int k = -2; k <= 2; k++)
        {
            int col = user.Col + fx * 2 + sx * k, row = user.Row + fy * 2 + sy * k;
            if ((uint)col >= host.Cols || (uint)row >= host.Rows) continue;
            _effects.Add((895, 0, host._lastTime + (k + 2) * 4 / TicksPerSecond, col * TileW + TileW / 2, host.CellCenterY(col, row)));
        }
    }

    /// <summary>work 번호 → (동작 차례, 때리는 순간에 띄울 이펙트).</summary>
    internal static readonly Dictionary<int, (int[] Actions, AbilityEffect[] Effects)> AbilityMotions = new()
    {
        // 죠안
        // 힐·큐어 — 이펙트(높이·지연·수명)는 뽑은 표가 원본대로 채운다(ba-15: 297 이 z+70 = 42px, 312 는 소리 껍데기).
        [58] = ([6, 15], []),                                                   // 힐
        [87] = ([6, 15], []),                                                   // 큐어
        // 연 — 레벨 띠마다 사슬이 다르다(분석-모션 ba-10 「연 레벨별 동작 사슬과 타수」).
        // Lv1~4 = 2타, 5~8 = 3타, 9~12 = 4타, 13~16 = 5타, 17~20 = 6타.
        // 표는 work 번호로 찾는다 — 스킬 파일(0001.json)은 원본 짝수 레벨 work 만 남겨 Lv1~10 으로 줄였으므로(새 Lv N = 원본 Lv 2N)
        // 새 레벨로는 1·2 = 2타, 3·4 = 3타, 5·6 = 4타, 7·8 = 5타, 9·10 = 6타다. 홀수 레벨 work 줄은 이제 안 쓰인다.
        [12] = ([5, 7, 13, 24], []), [191] = ([5, 7, 13, 24], []), [190] = ([5, 7, 13, 24], []), [189] = ([5, 7, 13, 24], []),
        [188] = ([5, 7, 14, 24], []), [187] = ([5, 7, 14, 24], []), [186] = ([5, 7, 14, 24], []), [185] = ([5, 7, 14, 24], []),
        [184] = ([5, 7, 14, 8, 24], []), [183] = ([5, 7, 14, 8, 24], []), [200] = ([5, 7, 14, 8, 24], []), [199] = ([5, 7, 14, 8, 24], []),
        [198] = ([5, 7, 13, 14, 24], []), [197] = ([5, 7, 13, 14, 24], []), [196] = ([5, 7, 13, 14, 24], []), [195] = ([5, 7, 13, 14, 24], []),
        [194] = ([5, 7, 13, 14, 8, 24], []), [193] = ([5, 7, 13, 14, 8, 24], []), [201] = ([5, 7, 13, 14, 8, 24], []), [192] = ([5, 7, 13, 14, 8, 24], []),
        [1583] = ([], [new(1320, 0, false, 0)]),                                // 이스케이프(순간이동)
        // 기본공격 1479(47명) — 때리는 동작 하나에 이펙트 1401 이 붙는다(분석-모션, 0x10094590).
        [1479] = ([8], [new(1401, 0, true, 0)]),
        [1641] = ([5, 7], []),                                                  // 발키리의혼
        // 살라딘
        // 비 — BiScript(GameWindow.Knockback.cs). 레벨마다 work 가 따로라(10 · 221~239) 거기서 모두 잡는다.
        [59] = ([6, 15], [new(1332, 0, false, 0), new(1324, 1, false, 0)]),     // 격려
        // 제이슨
        [467] = ([6, 15], []),       // 블레이드 미사일 — 이펙트는 도구 표의 것(41틱 뒤 386:0 …)을 쓴다. 손으로 적은 줄이 지연 0 으로 그것을 덮었다
        [735] = ([6, 15], [new(1380, 0, false, 0)]),                            // 크래쉬 봄
        // 나인 크루세이더 — 앞머리는 FinisherPrelude, 칼은 GameWindow.NineCrusader.cs 가 한다.
        // 도구 표의 344 모션 열한 개를 대상 한 자리에 겹쳐 띄우던 것은 뺀다(HandOnlyWorks).
        [NineCrusaderSkill.NineCrusaderWork] = ([6, 15], []),
        [HeavenEarthSkill.HeavenEarthWork] = ([6, 15], []),                                      // 천지 파열무 — 핸들러는 GameWindow.HeavenEarth.cs
        // 카운터 블레이드 — 준비(동작 5 → 7) 뒤 핸들러 0x100a8df0 이 동작 12 를 쓰고 이펙트 둘을 시전자에게 띄운다
        // (tools/re/work_script.py --work 390). 레벨마다 work 가 따로라(390 · 997~1015) 모두 같은 대본을 쓴다.
        [390] = ([5, 7, 12], CounterBlade),
        [997] = ([5, 7, 12], CounterBlade),
        [998] = ([5, 7, 12], CounterBlade),
        [999] = ([5, 7, 12], CounterBlade),
        [1000] = ([5, 7, 12], CounterBlade),
        [1001] = ([5, 7, 12], CounterBlade),
        [1002] = ([5, 7, 12], CounterBlade),
        [1003] = ([5, 7, 12], CounterBlade),
        [1004] = ([5, 7, 12], CounterBlade),
        [1005] = ([5, 7, 12], CounterBlade),
        [1006] = ([5, 7, 12], CounterBlade),
        [1007] = ([5, 7, 12], CounterBlade),
        [1008] = ([5, 7, 12], CounterBlade),
        [1009] = ([5, 7, 12], CounterBlade),
        [1010] = ([5, 7, 12], CounterBlade),
        [1011] = ([5, 7, 12], CounterBlade),
        [1012] = ([5, 7, 12], CounterBlade),
        [1013] = ([5, 7, 12], CounterBlade),
        [1014] = ([5, 7, 12], CounterBlade),
        [1015] = ([5, 7, 12], CounterBlade),
    };


    /// <summary>하이 텔레포트 레벨 1~20 의 work(어빌리티 37).</summary>
    internal static readonly HashSet<int> TeleportWorks = [397, 584, 583, 582, 581, 580, 579, 578, 577, 576, 575, 593, 592, 591, 590, 589, 588, 587, 586, 585];

    /// <summary>그라비티 필드 레벨 1~10 의 work(어빌리티 110).</summary>
    /// <summary>아스트럴 파이어의 솟는 불꽃 Obs.</summary>
    internal const int AstralFireObs = 839;

    internal static readonly HashSet<int> GravityFieldWorks = [484, 950, 949, 948, 947, 946, 945, 944, 943, 942];

    /// <summary>손 표만 쓰는 work — 도구 표의 이펙트가 틀려 합치면 안 되는 것.</summary>
    internal static readonly HashSet<int> HandOnlyWorks = [NineCrusaderSkill.NineCrusaderWork, HeavenEarthSkill.HeavenEarthWork];

    /// <summary>순간이동하는 work(이스케이프) — 쓰고 나면 겨눈 빈 칸으로 옮긴다.</summary>
    internal const int EscapeWork = 1583;

    /// <summary>
    /// 그 work 의 대본 — <b>손으로 맞춘 표</b>가 먼저고, 없으면 도구가 뽑은 <see cref="WorkScripts"/> 다.
    /// </summary>
    /// <remarks>
    /// 손 표는 자리·높이까지 맞춰 둔 서른 남짓이고, 뽑은 표는 1540개다. 둘 다 없으면 예전처럼 기본공격 동작을 빌린다.
    /// </remarks>
    /// <summary>그림 컷 없이 소리 키만 든 Obs — 시전 소리(1338)·기술별 소리 껍데기(분석-스킬 fx-189).</summary>
    internal static readonly HashSet<int> SoundShellObs = [1338, 1332, 1324, 311, 312, 379, 487, 1320, 1479, 1483];

    /// <summary>
    /// work 의 동작 차례와 이펙트 — 동작 차례·이펙트 자리는 손 표가 이기고, 도구 표(원본 핸들러에서 뽑은 것)의 이펙트는 <b>늘 뒤에 합친다</b>.
    /// 예전 도구가 파생 생성자로 만드는 이펙트를 놓쳐(분석-스킬 fx-189) 손 표에는 그림 하나(크래쉬 봄 1380)나 소리 껍데기만 적힌 줄이 많았다 —
    /// 손 표가 있다고 도구 표를 무시하면 폭탄·착탄 같은 그림이 영영 안 나온다. 같은 (Obs, 모션)은 손 표 것 하나만 둔다.
    /// </summary>
    internal static (int[] Actions, AbilityEffect[] Effects)? ScriptFor(int work)
    {
        // 하이 텔레포트 — 떠나는 자리의 빛만. 나타나는 빛(381:1)은 새 칸에서 TeleportRoutine 이 띄운다.
        // 엘레맨탈 파이어·서몬 몬스터 — 효과는 GameWindow.Acrost.cs 가 대상마다 깐다(준비의 시전 영상만 남긴다).
        if (AcrostSkill.AcrostWorks.Contains(work)) return ([6, 15], []);
        // 그라비티 필드 — 중력장은 GameWindow.GravityField.cs 가 대상마다 깐다.
        if (GravityFieldWorks.Contains(work)) return ([6, 15], []);
        // 블레이드 미사일 — 칼은 BladeMissileSkill 이 자리·시각대로 깐다. 표에서는 소리 껍데기(시전 1338 · 386:0)만 남긴다.
        if (BladeMissileSkill.Works.Contains(work)) return ([6, 15], [new(1338, 1, false, 0), new(BladeMissileSkill.BladeSoundObs, 0, false, 0, BladeMissileSkill.AppearDelay)]);
        // 아스트럴 애로우 — 활·화살·폭발은 GameWindow.AstralArrow.cs 가 시각대로 깐다.
        if (AstralArrowSkill.AstralArrowWorks.Contains(work)) return ([6], []);
        // 메테오 — 운석·착탄·폭발은 GameWindow.Meteor.cs 가 시각대로 깐다.
        if (MeteorSkill.MeteorWorks.Contains(work)) return ([6, 15], []);
        // 비 — 도구 표의 동작 2·0 은 맞은 인물의 것이라 시전자 사슬에서 뺀다. 밀어내기는 KnockbackRoutine.
        if (KnockbackSkill.BiWorks.Contains(work)) return KnockbackSkill.BiScript;
        // 더블 브레이크 — 도구가 동작을 코드 주소 순으로 뽑아 16 이 맨 뒤로 갔다. 원본은 16 → 17(붙듦) → 18(0x100825e0, ba-20 P9).
        if (work == StagingSkill.DoubleBreakWork && WorkScripts.TryGetValue(work, out var doubleBreak)) return ([6, 15, 16, 3017, 18], doubleBreak.Effects);
        if (TeleportWorks.Contains(work)) return ([6, 15], [new(1338, 1, false, 0), new(381, 0, false, 0), new(210, 3, false, 0)]);
        bool hasHand = AbilityMotions.TryGetValue(work, out var hand);
        bool hasMade = WorkScripts.TryGetValue(work, out var made);
        if (!hasHand) return hasMade ? made : null;
        if (!hasMade || HandOnlyWorks.Contains(work)) return hand;
        return (hand.Actions, [.. hand.Effects, .. made.Effects.Where(e => !hand.Effects.Any(h => h.Obs == e.Obs && h.Motion == e.Motion && h.Facing == e.Facing))]);
    }

    /// <summary>
    /// 대본이 있으면 그 동작 차례 — <b>비어 있어도</b> 그대로다. 원본이 시전자에게 아무 동작도 안 거는 기술(이스케이프·회피·아이템 1599~1622)이
    /// 칼을 휘두르면 안 된다(fg-20). 대본이 아예 없을 때만 기본공격 동작을 빌린다.
    /// </summary>
    internal static int[] ActionsFor(WorkData w) =>
        ScriptFor(w.Id) is { } m ? m.Actions
        : BasicWorkActions.GetValueOrDefault(w.Id) ?? StrikeActions;

    /// <summary>
    /// 동작 칸에 적힌 <b>모션 번호</b> — 1000+m 은 모션 m 을 한 번, 2000+m 은 붙든다(원본 PlayMotion <c>0x100e53c0</c> + 되풀이 1000).
    /// 폭·메테오스트라이크 30→31→32, 다크 스크림 48→49→50, 헬 카이트 90 따위(tools/re/work_fx_table.py).
    /// </summary>
    internal const int RawOnce = 1000, RawHold = 2000;

    /// <summary>3000+동작 — 원본 SetAction 되풀이 1000 이상으로 <b>붙드는 동작</b>(혼·비연참·오메가 스윙의 돌진 자세 따위).</summary>
    internal const int ActHold = 3000;

    /// <summary>붙드는 모션의 최대 길이(초) — 치는 칸에서는 이펙트가 끝나면(길어도 3초) 다음 칸이 덮는다.</summary>
    internal const double HoldSeconds = 4;

    /// <summary>필살기 앞머리가 이미 한 준비 동작(사슬 앞의 6·15) 수.</summary>
    internal static int PreludeSteps(int[] actions) =>
        actions is [6, 15, ..] ? 2 : actions is [6, ..] ? 1 : 0;

    /// <summary>사슬 한 칸을 튼다 — 동작이면 그 동작, 모션 번호면 그 모션. 붙드는 모션은 <paramref name="holdSeconds"/> 동안 붙든다.</summary>
    internal void PlayChainStep(UnitState a, int code, double holdSeconds)
    {
        if (code >= ActHold)
        {
            // 붙드는 동작 — 그 방향의 모션을 되풀이로 튼다(없는 인물이면 그냥 동작).
            int action = DrawnAction(a, code - ActHold);
            if (host._sprites.TryGetValue(a.ChrCode, out var sprite) && sprite.Clip(action, a.Facing) is { } clip)
                host.HeavenEarthAb.PlayRawMotion(a, clip.Id, loop: true, holdSeconds);
            else PlayAction(a, action);
        }
        else if (code >= RawOnce) host.HeavenEarthAb.PlayRawMotion(a, code % RawOnce, loop: code >= RawHold, holdSeconds);
        else PlayAction(a, DrawnAction(a, code));
    }

    /// <summary>
    /// 판정이 나는 동작 — 동작 사슬 안에서 <b>타격 동작</b>(8·9·13·14·26·27)이 있으면 그 첫 자리,
    /// 없으면 기본공격은 가운데(베기), 어빌리티는 마지막 동작이다(분석-모션 ba-10).
    /// </summary>
    /// <summary>타격 판정이 드는 동작 — 8·9(베기·찌르기), 13·14(연속), 26·27.</summary>
    internal static bool IsStrikeAction(int action) => action is 8 or 9 or 13 or 14 or 26 or 27;

    internal static int HitStepFor(WorkData w, int steps)
    {
        int[] actions = ActionsFor(w);
        // 붙드는 모션(칼 꽂은 채·손 든 채)이 있으면 그동안 이펙트가 터진다 — 거기서 친다.
        int hold = Array.FindIndex(actions, a => a >= RawHold);
        if (hold >= 0) return hold;
        int strike = Array.FindIndex(actions, a => a is 8 or 9 or 13 or 14 or 26 or 27);
        if (strike >= 0) return strike;
        return ScriptFor(w.Id) != null ? Math.Max(0, steps - 1) : Math.Min(StrikeHitStep, steps - 1);
    }

    /// <summary>초능력공격(work 1479) 의 번개 구슬 — 체질이 없는(0) 인물만 이것을 쏜다.</summary>
    internal const int PsychicBolt = 209;

    /// <summary>
    /// 초능력공격 핸들러 <c>0x10094590</c>: 유닛 <c>+0x122</c>(= 체질) 가 0 이면 번개 구슬 Obs 209 를 날리고(<c>0x100c3340</c>),
    /// 아니면 체질별 빛 구슬(큰 것 <c>0x100c3d60</c> 모션 2, 꼬리 작은 것 <c>0x100c3dc0</c>, 수명 20틱)을 둘 날린다 — 표 <c>0x1009496c</c>:
    /// 1 파랑(483·482) · 2 주황(479·478) · 3 빨강(475·472) · 4 보라(477·476) · 5 초록(481·480) · 그 밖 빨강(475·475).
    /// btl 0132 블랙스피어스(chr 318, 체질 4)가 번개 대신 붉은 빛을 쏘던 까닭(사용자 제보).
    /// </summary>
    internal static (int Big, int Small)? PsychicOrbs(UnitState user) => user.Data?.Body switch
    {
        null or 0 => null,
        1 => (483, 482),
        2 => (479, 478),
        3 => (475, 472),
        4 => (477, 476),
        5 => (481, 480),
        _ => (475, 475),
    };

    /// <summary>때리는 순간에 그 어빌리티의 이펙트를 띄운다.</summary>
    /// <summary>지금 행동이 띄운 이펙트 가운데 가장 늦은 것의 시작 때(게임 초).</summary>
    internal double _fxLatestStart;

    /// <summary>지금 행동의 판정 대상(유닛 하나를 겨눈 AI·사건이면 그 하나) — 대상별 이펙트가 판정과 같은 목록을 쓴다. null 이면 범위 안 전원.</summary>
    internal List<int>? _fxTargets;

    /// <summary>방금 띄운 직선탄 가운데 가장 늦게 닿는 때(게임 초) — 0 이면 탄 없음.</summary>
    internal double _fxArriveAt;

    /// <summary>직선탄이 그 거리를 가는 데 걸리는 틱 — <see cref="DrawShots"/> 와 같은 셈(상한 600).</summary>
    internal static int ShotTicks(double distance, double speed, double scale, int mode, double min, double max)
    {
        speed = Math.Max(1, speed);
        double gone = 0;
        int ticks = 0;
        for (; ticks < 600 && gone < distance; ticks++)
        {
            gone += speed;
            speed = mode == 1 ? speed * scale : speed + scale;
            if (min > 0) speed = Math.Max(min, speed);
            if (max > 0) speed = Math.Min(max, speed);
            speed = Math.Max(0.5, speed);
        }
        return ticks;
    }

    /// <summary>방금 띄운 대상별 이펙트의 엇갈림 틱(확실한 것) — 판정도 이 간격으로 든다. 0 이면 한꺼번에.</summary>
    internal int _fxStagger;

    /// <summary>
    /// 라이트닝 샤벨의 칼(0x100ce030) — 한 다리마다: 다음 점 쪽으로 16방향 모션(4m+8)을 틱마다 한 칸 돌리고(그 틱마다 잔상 211:모션+2, 첫 틱에 384:2),
    /// 짝수 점(대상)으로는 10틱 뜸 들인 뒤 80 × 0.9(바닥 40)로 날고 뒤따르는 탄 넷(70·60·50·40, 13~16틱 늦게), 닿으면 384:3 을 남기고
    /// 30 × 0.65(바닥 5)로 120 을 지나친다. 마지막에는 칼이 사라지고 211:10 이 29틱에 시전자 위(높이 200)로 돌아간다 + 384:4.
    /// 자리는 월드 단위로 세고 화면에는 y × 0.8 − z × 0.6. 칼에 붙는 384 는 생긴 자리에 세운다(원본은 칼을 따라간다). 판정 때는 안 바꿨다.
    /// </summary>
    /// <summary>
    /// 뿌리개(ba-21 fx F3, 표 <see cref="WorkFxSpray"/>) — 안 보이는 개체가 조각을 만든다. 전에는 16개까지를 ±20·±10 px 에 한 틱씩 어긋나게 세웠다(가설).
    /// 2: 가운데에서 상자(±RangeX·Y·Z) 안 아무 데로 · 5: 그 반대(상자에서 가운데로) · 3: 조각 i 가 각 2i/개수 π 로 반지름 10 → RangeX ·
    /// 6: 아무 각·반지름에서 가운데로(조각마다 0~29틱 늦게) · 8: 36개가 방향 모션으로 퍼짐 · 4: 기둥(P0+1 틱마다 높이 60 씩 위에 하나) ·
    /// 1: 떠오르는 나선(반지름 RangeX, 틱당 P0/1000 점 · 높이 P1/1000)을 타며 틱마다 그림을 남김(따라 뜨는 알갱이 셋은 안 넣음, 시작 각은 가설).
    /// 7 은 길을 못 읽어 전처럼 둔다. 조각 탄의 P0 = 빠르기, P1 = 틱마다 바뀜(양수 × P1/1000, 음수 + |P1|/1000). 수명 = 모션 길이 − 1.
    /// </summary>
    /// <summary>
    /// 원 윤곽의 점 목록(0x100090e0 → 0x100091b0) — 점은 8r 개, 0번이 맨 위(0, −r+1)이고 화면에서 시계 방향으로 돈다(위 → 오른쪽 → 아래 → 왼쪽).
    /// 한 점이 x 나 y 로 한 칸이라 각은 번호에 고르지 않다. 가운데 기준 월드 단위.
    /// </summary>
    internal static List<(int X, int Y)> CirclePoints(int r)
    {
        var quarter = new List<(int X, int Y)> { (0, -r + 1) };
        for (int s = r - 1, e = 1; s >= 0 && quarter.Count < 2 * r;)
        {
            quarter.Add((e, -s));
            if ((int)Math.Sqrt(s * s + e * e) == r) s--; else e++;
        }
        var all = new List<(int X, int Y)>(8 * r);
        for (int turn = 0; turn < 4; turn++)
            foreach (var (px, py) in quarter)
                all.Add(turn switch { 0 => (px, py), 1 => (-py, px), 2 => (-px, -py), _ => (py, -px) });
        return all;
    }

    internal bool SpawnSpray(WorkData w, AbilityEffect e, double start, int x, int y)
    {
        if (!WorkFxSpray.Table.TryGetValue(w.Id, out var rows)) return false;
        bool any = false;
        foreach (var r in rows)
        {
            if (r.Obs != e.Obs || r.Delay != e.Delay || (r.Kind != 8 && r.Motion != e.Motion)) continue;
            double scale = r.P1 > 0 ? r.P1 / 1000.0 : -r.P1 / 1000.0;
            int mode = r.P1 > 0 ? 1 : 0;
            double at = start + 1 / TicksPerSecond;
            void Shot(int motion, double fromX, double fromY, double toX, double toY, double when, bool mirror) =>
                _pieces.Add((e.Obs, motion, when, fromX, fromY, toX, toY, Math.Max(1, r.P0), scale, mode, Math.Max(1, host.EffectTicks(e.Obs, motion) - 1), mirror));
            int Spread(int range) => range > 0 ? _fxRandom.Next(2 * range) - range : 0;
            switch (r.Kind)
            {
                case 2 or 5:
                    for (int n = 0; n < r.Count; n++)
                    {
                        double px = x + Spread(r.RangeX), py = y + Spread(r.RangeY) * 0.8 - Spread(r.RangeZ) * 0.6;
                        if (r.Kind == 2) Shot(e.Motion, x, y, px, py, at, false); else Shot(e.Motion, px, py, x, y, at, false);
                    }
                    break;
                case 3:
                    for (int n = 0; n < r.Count; n++)
                    {
                        double a = n * 2.0 / r.Count * Math.PI;
                        Shot(e.Motion, x + 10 * Math.Cos(a), y + 10 * Math.Sin(a) * 0.8, x + r.RangeX * Math.Cos(a), y + r.RangeX * Math.Sin(a) * 0.8, at, false);
                    }
                    break;
                case 6:
                    for (int n = 0; n < r.Count; n++)
                    {
                        double a = _fxRandom.Next(400) * 0.005 * Math.PI, far = _fxRandom.Next(Math.Max(1, r.RangeX));
                        Shot(e.Motion, x + far * Math.Cos(a), y + far * Math.Sin(a) * 0.8, x, y, start + _fxRandom.Next(30) / TicksPerSecond, false);
                    }
                    break;
                case 8:
                    for (int n = 0; n < 36; n++)
                    {
                        double a = (n - 9) / 18.0 * Math.PI;
                        Shot(n <= 17 ? n : 36 - n, x + 10 * Math.Cos(a), y + 10 * Math.Sin(a) * 0.8, x + r.RangeX * Math.Cos(a), y + r.RangeX * Math.Sin(a) * 0.8, at, n < 18);
                    }
                    break;
                case 4:
                    for (int n = 0; n < r.Count; n++)
                        _effects.Add((e.Obs, e.Motion, start + n * (r.P0 + 1) / TicksPerSecond, x, y - (int)(r.RangeZ * n * 0.6)));
                    break;
                case 1:
                {
                    // 힐(0x10081410): 뿌리개 둘 — 하나는 원 맨 위(점 0)에서 시계 방향, 하나는 점 100 에서 반대 방향. 반지름 30 원의 점 240개를 틱당 6점,
                    // 높이 틱당 1.15, 수명 96. 틱마다 그 자리에 그림 하나 + 알갱이 6:1 셋이 대상 발 높이까지 틱당 3 으로 내려온다(좌우 흔들림은 안 넣음).
                    // 같은 틀의 다른 줄(work 1599 의 7:2)은 뿌리개 하나, 알갱이 없이.
                    int ticks = Math.Clamp(e.Life > 0 ? e.Life : r.Life, 1, 150);
                    var ring = CirclePoints(Math.Max(1, r.RangeX));
                    bool heal = e.Obs == 478;
                    foreach (var (from, reverse) in heal ? new[] { (0.0, false), (100.0, true) } : [(0.0, false)])
                    {
                        var points = new (int X, int Y)[ticks];
                        double pos = from;
                        for (int t = 0; t < ticks; t++)
                        {
                            int i = (int)pos % ring.Count;
                            var (dx, dy) = ring[reverse ? ring.Count - i - 1 : i];
                            double z = Math.Truncate(r.P1 / 1000.0 * t);
                            points[t] = (x + dx, (int)(y + dy * 0.8 - z * 0.6));
                            if (heal && z >= 3)
                                for (int n = 0; n < 3; n++)
                                    _pieces.Add((6, 1, start + t / TicksPerSecond, points[t].X, points[t].Y, points[t].X, points[t].Y + z * 0.6, 1.8, 0, 0, (int)(z / 3) + 1, false));
                            pos += r.P0 / 1000.0;
                        }
                        _ringTrails.Add((e.Obs, e.Motion, start, points, false));
                    }
                    break;
                }
                case 7 when e.Obs == 480:
                {
                    // 큐어(0x10081830): 대상 위(높이 105 — 표의 Lift)에서 반지름 10 · 12 · … · 28 의 원 열 개를 차례로 반시계로 돈다(0x10038650 — 틱당 7점, × 1.00015).
                    // 틱마다 그림 하나 + 알갱이 8:1 셋이 땅까지(105) 틱당 3 으로 내려온다. 약 217틱.
                    var points = new List<(int X, int Y)>();
                    double speed = 7;
                    for (int k = 0; k < 10; k++)
                    {
                        var ring = CirclePoints(10 + 2 * k);
                        for (double pos = 0; pos < ring.Count && points.Count < 400; pos += speed, speed = Math.Min(40, speed * 1.00015))
                        {
                            var (dx, dy) = ring[ring.Count - (int)pos - 1];
                            points.Add((x + dx, (int)(y + dy * 0.8)));
                        }
                    }
                    for (int t = 0; t < points.Count; t++)
                        for (int n = 0; n < 3; n++)
                            _pieces.Add((8, 1, start + t / TicksPerSecond, points[t].X, points[t].Y, points[t].X, points[t].Y + 63, 1.8, 0, 0, 36, false));
                    _ringTrails.Add((e.Obs, e.Motion, start, [.. points], false));
                    break;
                }
                default: continue;
            }
            any = true;
        }
        return any;
    }

    /// <summary>이펙트가 대상에 닿는 때(게임 초) — 있으면 그 대상의 판정은 그때 든다(라이트닝 샤벨: 칼이 그 칸에 닿을 때, 0x100704f0 → 0x3e9).</summary>
    internal readonly Dictionary<UnitState, double> _fxHitAt = [];

    internal void SpawnSabreBlade(double start, int userX, int userY, List<(int X, int Y)> targets, List<UnitState> units)
    {
        double Wy(int screenY) => screenY / 0.8;
        var points = new List<(double X, double Y, double Z)>();
        (double X, double Y) origin = (userX, Wy(userY) - 500);
        foreach (var (tx, ty) in targets)
        {
            double px = tx, py = Wy(ty), dx = px - origin.X, dy = py - origin.Y, far = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            points.Add((px, py, 0));
            origin = (px + dx / far * 120, py + dy / far * 120);
            points.Add((origin.X, origin.Y, 0));
        }
        points[^1] = points[^1];
        points.Add((userX, Wy(userY), 200));
        var frames = new List<(int X, int Y, int Motion, bool Mirror)>();
        double x = userX, y = Wy(userY) - 600, z = 0;
        int cur = 0, tick = 0;
        bool first = true;
        (int X, int Y) Screen() => ((int)x, (int)(y * 0.8 - z * 0.6));
        int Pose() => 4 * Math.Max(1, cur > 9 ? 18 - cur : cur) + 8;
        double At(int t) => start + t / TicksPerSecond;
        void Hold(bool ghost)
        {
            var (sx, sy) = Screen();
            frames.Add((sx, sy, Pose(), cur > 9));
            if (ghost) _movers.Add((211, Pose() + 2, At(tick + 1), 7, sx, sy, 0, 0, 5, 0, 0, 0, 0, cur > 9));
            tick++;
        }
        for (int idx = 0; idx < points.Count && tick < 600; idx++)
        {
            var (px, py, pz) = points[idx];
            bool last = idx == points.Count - 1;
            double bearing = Math.Atan2(px - x, (y * 0.8 - z * 0.6) - (py * 0.8 - pz * 0.6));
            int want = Math.Min(9, 1 + (int)((Math.Abs(bearing) + Math.PI / 16) / (Math.PI / 8)));
            if (bearing > 0 && want > 1) want = 18 - want;
            if (last) want = 0;
            while (cur != want)
            {
                cur += Math.Sign(want - cur);
                if (first) { var (fx, fy) = Screen(); _effects.Add((384, 2, At(tick), fx, fy)); first = false; }
                Hold(ghost: true);
            }
            var (bx, by) = Screen();
            if (idx % 2 == 0)
            {
                _effects.Add((384, 1, At(tick + 10), bx, by));
                first = true;
                if (last)
                {
                    _movers.Add((211, 10, At(tick), 5, bx, by, (int)px, (int)(py * 0.8 - pz * 0.6), 29, 0, 0, 0, 0, false));
                    _effects.Add((384, 4, At(tick), bx, by));
                    tick += 29;
                    break;
                }
                for (int k = 0; k < 4; k++)
                    _shots.Add((211, Pose() + Math.Min(3, k + 1), At(tick + 13 + k), bx, by, px, py * 0.8, 70 - 10 * k, 0, 0, 30, 100, cur > 9));
                Hold(ghost: true);
                for (int wait = 1; wait < 10; wait++) Hold(ghost: false);
            }
            else
            {
                _effects.Add((384, 3, At(tick), bx, by));
                _fxHitAt[units[idx / 2]] = At(tick);        // 대상 칸에 닿아 방향을 잡은 틱에 그 칸의 유닛이 맞는다
            }
            double speed = idx % 2 == 0 ? 80 : 30, scale = idx % 2 == 0 ? 0.9 : 0.65, floor = idx % 2 == 0 ? 40 : 5;
            for (int guard = 0; guard < 200; guard++)
            {
                double dx = px - x, dy = py - y, far = Math.Sqrt(dx * dx + dy * dy);
                if (far <= speed) { (x, y) = (px, py); Hold(ghost: false); break; }
                (x, y) = (x + dx / far * speed, y + dy / far * speed);
                speed = Math.Max(floor, speed * scale);
                Hold(ghost: false);
            }
        }
        _scripted.Add((211, start, [.. frames]));
        _fxLatestStart = Math.Max(_fxLatestStart, At(Math.Min(tick, 190)));
    }

    internal void SpawnAbilityEffects(WorkData w, UnitState user, int col, int row)
    {
        host.Mov.SpawnWorkMovies(w, user, col, row, prelude: false);   // 치는 순간의 영상(리 바이블·어스퀘이크·강림의 밤)
        host.Cut.Start(w);                                               // 전체 화면 컷신(헬 카이트·진무 천지파열 …) — 도는 동안 전투가 선다
        host.Mov.SpawnRipples(w, col, row);                              // 익스퍼트 웨이브 파문(코드 이펙트)
        SpawnBodyClones(w, user, host._units.FirstOrDefault(u => u.Alive && u.Col == col && u.Row == row), col, row);   // 분신·잔상
        if (CounterBladeWorks.Contains(w.Id)) SpawnCounterBlades(user);
        host.UnitFxAb.StartUnitFx(w, user, col, row);                         // 유닛 숨김·밝기(희생·블라인드·브레인 브레이크 …)
        if (BladeMissileSkill.Works.Contains(w.Id)) host.BladeMissileAb.Spawn(w, user);   // 블레이드 미사일의 칼들
        var script = ScriptFor(w.Id);
        // 가장 늦게 뜨는 이펙트의 시작 때 — 행동 루틴이 그때까지는 끝나지 않는다(늦은 이펙트가 다음 행동 위에 겹치지 않게).
        _fxLatestStart = Math.Max(_fxLatestStart, host._lastTime + (script is { Effects.Length: > 0 } timed ? timed.Effects.Max(e => e.Delay) : 0) / TicksPerSecond);
        if (DuckWorks.Contains(w.Id))
        {
            // 줄여 둘 동안 — 가장 늦게 끝나는 이펙트까지(지연 + 수명 또는 모션 길이). 대본이 없으면 40틱.
            double ticks = (script?.Effects ?? []).Select(e => e.Delay + (e.Life > 0 ? e.Life : host.EffectTicks(e.Obs, e.Motion))).DefaultIfEmpty(0).Max();
            host.DuckMusicForWork(ticks / TicksPerSecond);
        }
        if (script is not { } m) return;
        // 손으로 적어 둔 소리표가 있는 어빌리티는 그것이 소리를 낸다 — 여기서 또 내면 겹친다.
        bool ownSounds = !host._abilitySounds.ContainsKey(w.AbilityId);
        var (userX, userY) = host.Btl.UnitFoot(user);
        int targetX = col * TileW + TileW / 2, targetY = host.CellCenterY(col, row);
        int cheerDelay = 0;      // 격려 — 윤곽 알갱이가 다 뜬 뒤에야 떠오르는 알갱이가 나온다
        foreach (var e in m.Effects)
        {
            // 필살기 공통 앞머리의 효과(시전 소리 1338 · 487 · 빛 알갱이 343 · 금빛 띠 344)는 FinisherPrelude 가 제때 띄운다 — 뽑은 표에 섞여 있어도 여기서는 뺀다.
            if (w.Prepare == 7 && e.Obs is 1338 or 487 or FinisherPreludeSkill.PreludeDotObs or FinisherPreludeSkill.PreludeBandObs) continue;
            // 준비 2·3·5·6 의 시전 소리 1338:1 은 시전 시작 +2틱에 이미 냈다(UseWorkRoutine, ba-15 R6).
            if (w.Prepare is 2 or 3 or 5 or 6 && e.Obs == 1338) continue;
            // 카운터 미사일 — 네 방향 모두 Obs 637 모션 6 이다(0x100a9260, ba-14 H3). 도구 표에 섞인 2·3·4·5 는 방향 가지의 겉모습이 아니라 뺀다.
            if (PushSkill.CounterMissileWorks.Contains(w.Id) && e.Obs == 637 && e.Motion != 6) continue;
            if (e.Facing >= 0 && e.Facing != user.Facing switch { Facing.Up => 0, Facing.Left => 1, Facing.Down => 2, _ => 3 }) continue;
            var (x, y) = e.OnTarget ? (targetX, targetY) : (userX, userY);
            double start = host._lastTime + e.Delay / TicksPerSecond;
            // 줄별 덧정보(WorkFxExtra.g.cs, ba-21 fx 「덧정보 표 생성 기록」) — 좌우 뒤집기 · 기준 자리에서의 치우침 · 대상마다 하나씩(엇갈림) · 직선탄의 빠르기.
            int userDir = user.Facing switch { Facing.Up => 0, Facing.Left => 1, Facing.Down => 2, _ => 3 };
            // 같은 열쇠의 Row 가 여럿이면 원본은 그 수만큼 따로(자리·뒤집기가 다르게) 띄운다 — Row 마다 하나씩. 방향마다 값이 다른 줄은 시전자 방향 것만.
            var rowsFor = new List<WorkFxExtra.Row?>();
            if (WorkFxExtra.Table.TryGetValue(w.Id, out var extras))
            {
                var same = extras.Where(r => r.Obs == e.Obs && r.Motion == e.Motion && r.Delay == e.Delay).ToList();
                var facing = same.Where(r => r.Facing == userDir && r.Facing != e.Facing).ToList();
                foreach (var r in facing.Count > 0 ? facing : same.Where(r => r.Facing == e.Facing)) rowsFor.Add(r);
            }
            if (rowsFor.Count == 0) rowsFor.Add(null);
            var (baseX, baseY) = (x, y);
            foreach (var extra in rowsFor)
            {
                (x, y) = (baseX, baseY);
                // 라이트닝 샤벨 — 같은 열쇠(211:1, 지연 61)의 줄이 둘이다: 솟는 앞머리(직선탄, 높이 200 줄)와 칼(그 밖, 땅 줄). 제 짝만 쓴다.
                if (e.Obs == 211 && e.Motion == 1 && rowsFor.Any(r => r is { Move: 9 }) && (extra is { Move: 9 }) != (e.Lift == 0)) continue;
                bool mirrored = extra is { Mirror: 2 } || (extra is { Mirror: 1 } && user.Facing == Facing.Right);
                bool vectorShot = extra is { Move: 1 } v && (v.To == 2 || (v.From == v.To && (v.Dx != 0 || v.Dy != 0)));
                if (extra is { } ex && !vectorShot) (x, y) = (x + ex.Dx, y + ex.Dy);
                // 아스트럴 파이어 839:12(핸들러 0x100aab90 단계 1 — 60틱 뒤): 불꽃 50개가 <b>대상의 발밑</b>(x ± 15)에서 곧장 위로 260(화면 156) 솟는다 —
                // 빠르기 20 에 틱마다 × 1.1, 최대 100(0x100c3490(도착, 20, 1.1, 1) · 0x100c25e0(100)), 하나마다 0~99틱 늦게(0x100c24d0(rand % 100)).
                // 표는 출발·도착을 못 읽어(From/To 3) 시전자에서 대상으로 한 개가 날아갔다(사용자 보고: 효과가 안 나온다).
                if (e.Obs == AstralFireObs && e.Motion == 12 && extra is { Move: 1 })
                {
                    // 원본은 대상 유닛 하나(+0x8c)에 건다 — 여기서는 판정 대상마다 하나씩, 대상이 없으면 겨눈 칸에.
                    var feet = (_fxTargets ?? WorkTargets(w, user, col, row)).Select(i => UnitFoot(host._units[i])).DefaultIfEmpty((targetX, targetY)).ToList();
                    foreach (var (footX, footY) in feet)
                        for (int n = 0; n < 50; n++)
                        {
                            double fx = footX + _fxRandom.Next(30) - 15;
                            _shots.Add((e.Obs, e.Motion, host._lastTime + (60 + _fxRandom.Next(100)) / TicksPerSecond, fx, footY, fx, footY - 260 * 0.6, 20, 1.1, 1, 0, 100, false));
                        }
                    _fxLatestStart = Math.Max(_fxLatestStart, host._lastTime + 60 / TicksPerSecond);
                    continue;
                }
                // 직선탄(0x100c3490) — 틱당 빠르기와 가속·감속으로 난다(전에는 모션 길이 동안 등속). 도착을 아는 것만: 벡터(출발 + (Dx,Dy)) ·
                // 시전자 ↔ 대상 · 표가 「날기」로 적은 줄(시전자 → 대상). 도착을 못 읽은 제자리 줄은 전처럼 제자리에 띄운다.
                if (extra is { Move: 1, Speed: > 0 } shot && !(e.Obs == PsychicBolt && PsychicOrbs(user) != null)
                    && (vectorShot || (shot.From != shot.To && shot.To is 0 or 1 && shot.From is 0 or 1) || e.Fly))
                {
                    (double X, double Y) from = shot.From == 1 ? (targetX, targetY - e.Lift) : (userX, userY - e.Lift);
                    (double X, double Y) to = vectorShot ? (from.X + shot.Dx, from.Y + shot.Dy)
                                            : shot.From == 1 ? (userX, userY - e.Lift) : (targetX, targetY - e.Lift);
                    if (Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y) >= 1)
                    {
                        _shots.Add((e.Obs, e.Motion, start, from.X, from.Y, to.X, to.Y, shot.Speed, shot.ScalePermille / 1000.0,
                                    shot.Mode, shot.MinSpeed, shot.MaxSpeed, mirrored));
                        // 닿는 때 — 「탄이 사라질 때 판정」 work(WorkFxExtra.ArriveHitWorks)의 판정 시각이고, 행동도 그때까지는 안 끝난다.
                        double arrive = start + ShotTicks(Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y)),
                                                          shot.Speed, shot.ScalePermille / 1000.0, shot.Mode, shot.MinSpeed, shot.MaxSpeed) / TicksPerSecond;
                        _fxArriveAt = Math.Max(_fxArriveAt, arrive);
                        _fxLatestStart = Math.Max(_fxLatestStart, arrive);
                        continue;
                    }
                }
                // 그 밖 이동기(ba-21 fx F2, 원본 식 확인) — 전에는 제자리에 한 장으로 섰다. 그 밖(9)은 아직 제자리.
                // 떠오름(0x100c5c10 → 틱 0x10038da0): 높이 z 만 바뀐다 — T(= MaxSpeed, 음수면 내려옴, MinSpeed 가 있으면 그 너비로 흔들린 값)만큼
                // 틱당 빠르기(ScalePermille/1000)로. 화면에서는 높이 × 12/20(0x100ea910). 좌우 1~3px 흔들림(타원 윤곽 길)은 안 넣었다.
                // 조각 떼(WorkFxSwarm) — 원본 핸들러는 이 이펙트를 고리 안에서 수십 개 만든다: 자리는 기준 ± 너비/2, 조각마다 늦게.
                var swarm = extra is { } keyed && WorkFxSwarm.Table.TryGetValue(w.Id, out var swarmRows)
                    ? swarmRows.FirstOrDefault(r => r.Obs == e.Obs && r.Motion == e.Motion && r.Delay == keyed.Delay) : default;
                int pieces = Math.Max(1, swarm.Count);
                if (extra is { Move: 2, ScalePermille: > 0 } rise && rise.MaxSpeed != 0 && !e.Fly)
                {
                    for (int n = 0; n < pieces; n++)
                    {
                        int height = rise.MaxSpeed + (rise.MinSpeed > 0 ? _fxRandom.Next(rise.MinSpeed) - rise.MinSpeed / 2 : 0);
                        int px = x + (swarm.XWidth > 0 ? _fxRandom.Next(swarm.XWidth) - swarm.XWidth / 2 : 0);
                        int py = y - e.Lift + (swarm.YWidth > 0 ? (int)((_fxRandom.Next(swarm.YWidth) - swarm.YWidth / 2) * 0.8) : 0);
                        double at = start + ((swarm.DelayRandom > 0 ? _fxRandom.Next(swarm.DelayRandom) : 0) + n * swarm.DelayStep + cheerDelay) / TicksPerSecond;
                        _shots.Add((e.Obs, e.Motion, at, px, py, px, py - (int)(height * 0.6), rise.ScalePermille / 1000.0 * 0.6, 1, 1, 0, 0, mirrored));
                    }
                    continue;
                }
                if (extra is { Move: 4, Mode: > 0 } ring && !e.Fly)
                {
                    // 점은 틱마다 하나를 미리 셈한다(0x10039410) — 각·각속도는 π 단위, 각속도와 높이 빠르기는 틱마다 바뀔 수 있다(오버플로우·리미트플로우).
                    for (int n = 0; n < pieces; n++)
                    {
                        double a = ring.MinSpeed / 1000.0 + n * swarm.AngleStepPermille / 1000.0, turn = ring.ScalePermille / 1000.0;
                        double dz = swarm.Dz0Permille / 1000.0, z = 0;
                        var points = new (int X, int Y)[ring.Mode];
                        for (int t = 0; t < ring.Mode; t++)
                        {
                            double r = Math.Truncate(ring.Speed + t * (double)(ring.MaxSpeed - ring.Speed) / ring.Mode);
                            dz = swarm.DzMultiply ? dz * swarm.DzKPermille / 1000.0 : dz + swarm.DzKPermille / 1000.0;
                            z += dz;
                            points[t] = ((int)(x + r * Math.Cos(a * Math.PI)), (int)(y - e.Lift + r * Math.Sin(a * Math.PI) * TileH / TileW - z * 0.6));
                            turn = swarm.W2Multiply ? turn * swarm.W2Permille / 1000.0 : turn + swarm.W2Permille / 1000.0;
                            a += turn;
                        }
                        _ringTrails.Add((e.Obs, e.Motion, start + ((swarm.DelayRandom > 0 ? _fxRandom.Next(swarm.DelayRandom) : 0) + n * swarm.DelayStep) / TicksPerSecond, points, mirrored));
                    }
                    _fxLatestStart = Math.Max(_fxLatestStart, start);
                    continue;
                }
                // 그 밖(9) 가운데 식을 읽은 둘 — 표에 인자 자리가 없어 여기 적는다(ba-21 fx F2 뒤 분석).
                // 리미트 크래쉬 · 리미트 캐스트 476:3 · 8:1(0x100c57d0 → 틱 0x100389a0): 조각 50개가 대상 둘레 반지름 30 원을 틱당 8점씩 돌며
                // 틱당 2.0~2.9 씩 떠오른다(모두 약 100 높이, 조각마다 0~79틱 늦게). 원 한 바퀴의 점 수(≈ 둘레 188)는 가설.
                if (extra is { Move: 9 } && !e.Fly && ((e.Obs == 476 && e.Motion == 3) || (e.Obs == 8 && e.Motion == 1)))
                {
                    for (int n = 0; n < 50; n++)
                    {
                        double dz = 2.0 + 0.1 * _fxRandom.Next(10);
                        _movers.Add((e.Obs, e.Motion, start + _fxRandom.Next(80) / TicksPerSecond, 6, x, y - e.Lift, 0, 0, (int)(100 / dz), 30, dz,
                                     _fxRandom.NextDouble() * 2 * Math.PI, 8.0 / 30, mirrored));
                    }
                    continue;
                }
                // 라이트닝 샤벨 211:1(클래스 0x100cdf40, 틱 0x100ce030) — 칼이 위에서 와 대상마다 꿰뚫고 120 지나쳤다가 다음 대상으로 돈다.
                if (extra is { Move: 9 } && e.Obs == 211 && e.Motion == 1)
                {
                    var piercedUnits = (_fxTargets ?? WorkTargets(w, user, col, row)).Select(i => host._units[i]).Take(10).ToList();
                    var pierced = piercedUnits.Select(UnitFoot).ToList();
                    // 칼은 앞머리(위로 솟는 211:1 — 빠르기 100 뒤 40, 약 14틱)가 끝난 뒤에 선다.
                    if (pierced.Count > 0) { SpawnSabreBlade(start + 14 / TicksPerSecond, userX, userY, pierced, piercedUnits); continue; }
                }
                // 격려 670:1(0x10091670): 시전자 지금 컷의 <b>윤곽</b> 점마다 알갱이 하나(0x1000cbb0 — 가로·세로 줄의 가장자리만, 보통 200~300개).
                // 알갱이 i 는 i/4 틱에 윤곽 위에 뜨고(위에서 아래로, 틱당 넷), (개수/4 + i/4) 틱까지 제자리에 있다가 80틱 동안 대상 쪽으로 나선을 그리며
                // 모인다(반지름 = 거리 → 0, 각속도 0.03π — 0x100c59d0). 효과는 마지막 알갱이가 닿을 때 든다. 떠오르는 478:3 은 개수/4 + 70 틱 늦게.
                if (extra is { Move: 9 } && e.Obs == 670 && e.Motion == 1 && host._sprites.TryGetValue(user.ChrCode, out var cheerSprite)
                    && cheerSprite.FrameFor(user) is { } cheerFrame)
                {
                    var outline = new List<(int X, int Y)>();
                    bool Solid(int px, int py) => (uint)px < cheerFrame.W && (uint)py < cheerFrame.H && (cheerFrame.Px[py * cheerFrame.W + px] & 0xFF000000) != 0;
                    for (int py = 0; py < cheerFrame.H; py++)
                        for (int px = 0; px < cheerFrame.W; px++)
                            if (Solid(px, py) && (!Solid(px - 1, py) || !Solid(px + 1, py) || !Solid(px, py - 1) || !Solid(px, py + 1)))
                                outline.Add((cheerFrame.X + px, cheerFrame.Y + py));
                    int count = outline.Count, hold = count >> 2;
                    for (int i = 0; i < count; i++)
                        _cheer.Add((e.Obs, e.Motion, start + (i >> 2) / TicksPerSecond, hold, userX + outline[i].X, userY + outline[i].Y,
                                    targetX, targetY + outline[count - 1 - i].Y));
                    cheerDelay = hold + 70;
                    double last = start + ((count >> 2) + hold + 80) / TicksPerSecond;
                    _fxLatestStart = Math.Max(_fxLatestStart, start + hold / TicksPerSecond);
                    foreach (int ti in _fxTargets ?? WorkTargets(w, user, col, row)) _fxHitAt[host._units[ti]] = last;
                    continue;
                }
                // 리인카네이션 279:4(0x100c4810 → 틱 0x100380f0): 안 보이는 앞잡이가 시전자 둘레 아홉 점을 틱당 3 으로 돌고, 이 그림이 틱당 2 로 그 뒤를 쫓는다.
                if (extra is { Move: 9 } && !e.Fly && e.Obs == 279 && e.Motion == 4)
                {
                    _chasers.Add((e.Obs, e.Motion, start, userX, userY, mirrored));
                    _fxLatestStart = Math.Max(_fxLatestStart, start + 97 / TicksPerSecond);   // 아홉 점을 도는 데 약 97틱 — 그동안 행동이 안 끝난다(가설)
                    continue;
                }
                // 엘레맨탈 라이트 211:0(클래스 틱 0x100d1090): 빛덩이가 시전자 위(높이 100)에 30틱 떠 있다가 겨눈 칸 쪽으로 16방향 모션을 한 틱에 한 칸씩 돌리고,
                // 반대로 100 물러났다가(빠르기 10 × 0.9) 겨눈 쪽으로 600~700 을 내달린다(빠르기 40 × 1.1, 최대 100). 잔상 셋이 2틱씩 늦게 따라간다.
                if (extra is { Move: 9 } && !e.Fly && e.Obs == 211 && e.Motion == 0)
                {
                    int ox = userX, oy = userY - 60, dc = col - user.Col, dr = row - user.Row;
                    double bearing = Math.Atan2(dc, -dr);
                    int want = Math.Min(9, 1 + (int)((Math.Abs(bearing) + Math.PI / 16) / (Math.PI / 8)));
                    if (bearing > 0 && want > 1) want = 18 - want;
                    _movers.Add((211, 0, start, 7, ox, oy, 0, 0, 31, 0, 0, 0, 0, false));
                    int turned = 0, cur = 1;
                    for (; cur != want; turned++)
                    {
                        cur += Math.Sign(want - cur);
                        _movers.Add((211, 4 * (cur > 9 ? 18 - cur : cur) + 8, start + (31 + turned) / TicksPerSecond, 7, ox, oy, 0, 0, 1, 0, 0, 0, 0, cur > 9));
                    }
                    int pose = cur > 9 ? 18 - cur : cur;
                    bool flip = cur > 9;
                    var (backX, backY, endX, endY) = Math.Abs(dr) > Math.Abs(dc) && dr < 0 ? (ox, oy + 80, ox, oy - 480)
                        : Math.Abs(dc) >= Math.Abs(dr) && dc < 0 ? (ox + 100, oy, ox - 700, oy)
                        : Math.Abs(dc) >= Math.Abs(dr) && dc > 0 ? (ox - 100, oy, ox + 700, oy)
                        : (ox, oy - 80, ox, oy + 480);
                    double dashAt = start + (31 + turned) / TicksPerSecond, recoil = 0, speed = 10;
                    int recoilTicks = 0;
                    for (; recoil < 100 && recoilTicks < 200; recoilTicks++) { recoil += speed; speed = Math.Max(1, speed * 0.9); }
                    _shots.Add((211, 4 * pose + 8, dashAt, ox, oy, backX, backY, 10, 0.9, 1, 1, 0, flip));
                    for (int k = 0; k < 4; k++)
                        _shots.Add((211, 4 * pose + 8 + k, dashAt + (recoilTicks + 2 * k) / TicksPerSecond, backX, backY, endX, endY, 40, 1.1, 1, 0, 100, flip));
                    _fxLatestStart = Math.Max(_fxLatestStart, dashAt + recoilTicks / TicksPerSecond);
                    // 판정은 빛덩이가 쏜 뒤 39틱(핸들러 0x1009fa00 단계 1 — 빛덩이가 죽으면 +0x96 = 1, 40 이 되면 범위 안 모두에게 0x3e9). 내달림이 어디 있든 같다.
                    foreach (int ti in _fxTargets ?? WorkTargets(w, user, col, row)) _fxHitAt[host._units[ti]] = dashAt + 39 / TicksPerSecond;
                    continue;
                }
                // 소울 블레스트 793:4(0x100d1970 → 틱 0x100d19e0): 시전자 → 겨눈 칸을 틱당 10 으로 곧게 가는 머리가 틱마다 그림을 남긴다 —
                // 남기는 자리는 가는 길에서 옆으로 15 × sin(n × π/6) 만큼 물결친다. 머리 자체는 안 보인다(가설).
                if (extra is { Move: 9 } && !e.Fly && e.Obs == 793 && e.Motion == 4 && (userX != targetX || userY != targetY))
                {
                    double dx = targetX - userX, dy = targetY - userY, far = Math.Sqrt(dx * dx + dy * dy);
                    var points = new (int X, int Y)[Math.Max(1, (int)(far / 10))];
                    for (int n = 0; n < points.Length; n++)
                    {
                        double side = 15 * Math.Sin(n * Math.PI / 6);
                        points[n] = ((int)(userX + dx / far * 10 * n - dy / far * side), (int)(userY - e.Lift + dy / far * 10 * n + dx / far * side * TileH / TileW));
                    }
                    _ringTrails.Add((e.Obs, e.Motion, start, points, mirrored));
                    _fxLatestStart = Math.Max(_fxLatestStart, start);   // 판정은 「닿을 때」 자리가 아니다(0x100c28f0)
                    continue;
                }
                // 떨굼(생성자 0x100c5c50 + 0x100c5d40(vx, vy, vz, 튕김)) — 조각 50개가 높이 MaxSpeed 에서 제 빠르기로 튀어 나가 중력 5.0 으로 떨어지고 땅에서 튄다.
                // 오버 드라이브 8:4 · 카운터 미사일 7:2 · 크래쉬 봄 251:0. 전에는 한 장이 제자리에 섰다. 표의 Lift 는 높이 × 0.6 과 같은 값이라 여기서는 안 쓴다.
                if (extra is { Move: 3, ScalePermille: > 0 } fall && !e.Fly)
                {
                    int len = host.EffectTicks(e.Obs, e.Motion);
                    bool bomb = fall.Speed < 0;      // 크래쉬 봄 — 수명 = 모션 × 3, 조각마다 0~49틱 늦게(0x100c24d0)
                    for (int n = 0; n < 50; n++)
                        _fallers.Add((e.Obs, e.Motion, start + (bomb ? _fxRandom.Next(50) : 0) / TicksPerSecond,
                                      x + (fall.From > 0 ? _fxRandom.Next(fall.From) - fall.From / 2 : 0),
                                      (fall.MaxSpeed > 0 ? y : y - e.Lift) + (fall.To > 0 ? (_fxRandom.Next(fall.To) - fall.To / 2) * 0.8 : 0),
                                      fall.MaxSpeed,
                                      fall.Mode > 0 ? _fxRandom.Next(fall.Mode) - fall.Mode / 2 : 0,
                                      fall.Mode > 0 ? _fxRandom.Next(fall.Mode) - fall.Mode / 2 : 0,
                                      fall.Speed / 1000.0 + (fall.MinSpeed > 0 ? _fxRandom.Next(fall.MinSpeed) : 0),
                                      fall.ScalePermille / 1000.0, bomb ? len * 3 : Math.Max(1, len - 1), mirrored));
                    continue;
                }
                if (extra is { Move: 5, Speed: > 0 } arc && (userX != targetX || userY != targetY))
                {
                    _movers.Add((e.Obs, e.Motion, start, 5, userX, userY - e.Lift, targetX, targetY - e.Lift, arc.Speed, 0, 0, 0, 0, mirrored));
                    _fxArriveAt = Math.Max(_fxArriveAt, start + arc.Speed / TicksPerSecond);
                    continue;
                }
                // 대상마다 하나씩(0x1009f9c5 헤비프레셔 15틱 · 엘레맨탈 썬더 8틱 …) — 겨눈 칸 한 곳이 아니라 판정 대상마다, 엇갈림 틱만큼 늦게.
                if (extra is { PerTarget: true } each && !e.Fly && (_fxTargets ?? WorkTargets(w, user, col, row)) is { Count: > 0 } eachTargets)
                {
                    for (int i = 0; i < eachTargets.Count; i++)
                    {
                        var (tx, ty) = host.Btl.UnitFoot(host._units[eachTargets[i]]);
                        double at = start + i * each.Stagger / TicksPerSecond;
                        if (e.Life > 0) host.HeavenEarthAb.AddTimedFx(e.Obs, e.Motion, at, (tx + each.Dx, ty + each.Dy - e.Lift), e.Life, false);
                        else (mirrored ? _effectMirrors : _effects).Add((e.Obs, e.Motion, at, tx + each.Dx, ty + each.Dy - e.Lift));
                    }
                    if (each.StaggerSure) _fxStagger = Math.Max(_fxStagger, each.Stagger);
                    _fxLatestStart = Math.Max(_fxLatestStart, start + (eachTargets.Count - 1) * each.Stagger / TicksPerSecond);
                    continue;
                }
                if (e.Fly && e.Obs == PsychicBolt && PsychicOrbs(user) is var (big, small))
                    for (int k = 0; k < 2; k++)
                    {
                        // 체질이 있으면 빛 구슬 둘이 꼬리(작은 구슬)를 달고 날아간다 — 둘째는 조금 늦게(가설: 원본은 출발점을 15 앞으로 둔다).
                        double at = start + k * 3 / TicksPerSecond;
                        _flyingEffects.Add((big, 2, at, userX, userY - e.Lift, targetX, targetY - e.Lift, 0));
                        _flyingEffects.Add((small, 2, at + 2 / TicksPerSecond, userX, userY - e.Lift, targetX, targetY - e.Lift, 0));
                    }
                else if (e.Fly)
                    _flyingEffects.Add((e.Obs, e.Motion, start, userX, userY - e.Lift, targetX, targetY - e.Lift, 0));
                else if (SpawnSpray(w, e, start, x, y - e.Lift)) { }
                else
                    for (int k = 0; k < Math.Max(1, e.Count); k++)
                    {
                        if (e.Life > 0)
                        {
                            // 수명이 있으면 그동안 되풀이해 그린다(시각표 효과 — 끝나는 때가 정해진다).
                            host.HeavenEarthAb.AddTimedFx(e.Obs, e.Motion, start + k / TicksPerSecond, (x, y - e.Lift), e.Life, false);
                            continue;
                        }
                        // 뿌리개는 대상 둘레에 흩뿌리고 한 틱씩 어긋나게 띄운다(원본은 코드가 난수로 셈한다 — 가설).
                        int jx = k == 0 ? 0 : host._rng.Next(-20, 21), jy = k == 0 ? 0 : host._rng.Next(-10, 11);
                        (mirrored ? _effectMirrors : _effects).Add((e.Obs, e.Motion, start + k / TicksPerSecond, x + jx, y - e.Lift + jy));
                    }
            }
            (x, y) = (baseX, baseY);

            // 이펙트 모션에 박힌 소리 키를 그 틱에 맞춰 예약한다 — 동작 소리(ScheduleActionSounds)와 같은 꼴이다.
            // 이것이 없으면 새로 붙인 기술 이펙트가 그림만 나오고 소리가 안 났다.
            // 썬더 스톰의 217:0 은 시작(A) 목록 소리 114 를 이펙트가 도는 동안 되풀이한다(0x100d2aa0, 감사4 S3).
            if (!e.Fly) host.QueueEffectLoopSound(e.Obs, e.Motion, start, e.Life > 0 ? e.Life : host.EffectTicks(e.Obs, e.Motion), x);
            // 이펙트 Obs 가 assets/effects 밖(moses/obs · characters)에 있어도 소리 키를 읽는다 — 락킹 필드 584:5 → 639 따위가 빠졌다(ba-21 sound D4).
            if (!ownSounds || (host._effectTables.GetValueOrDefault(e.Obs)?.Clips.GetValueOrDefault(e.Motion) ?? host.UiFor(e.Obs)?.Clip(e.Motion)) is not { } clip) continue;
            // 좌우 소리(감사4 S1) — 이펙트가 뜨는 자리 x. 날아가는 것은 떠나는 자리.
            float sx = e.Fly ? userX : x;
            foreach (var (tick, sound) in clip.Sounds) host._pendingSounds.Add((start + tick / TicksPerSecond, sound, sx));
            // 자식 이펙트의 소리도 따라간다 — 포스 필드 444:6 > 443:0 → 634 @t15(ba-21 sound D5).
            foreach (var (childStart, obs, motion, _, _, _, _) in clip.Children)
                if (host._effectTables.GetValueOrDefault(obs)?.Clips.GetValueOrDefault(motion) is { } child)
                    foreach (var (tick, sound) in child.Sounds) host._pendingSounds.Add((start + (childStart + tick) / TicksPerSecond, sound, sx));
        }
    }

    /// <summary>
    /// 핸들러가 음악을 40 % 로 줄였다가 되돌리는 work — 1467(헬 카이트 계열)·1521~1524·1528·1588~1590
    /// (<c>0x100c7710(40, 20)</c> 호출 22곳을 핸들러에 맞춘 것, 감사4 B4).
    /// </summary>
    internal static readonly HashSet<int> DuckWorks = [1467, 1521, 1522, 1523, 1524, 1528, 1588, 1589, 1590];
}
