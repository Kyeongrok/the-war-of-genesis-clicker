using WarOfGenesis.Assets;

namespace DuelDx;

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
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>이펙트 하나 — 어느 Obs 의 어느 모션을, 대상 자리(또는 내 자리)에서 몇 픽셀 위에 띄우나.</summary>
    private readonly record struct AbilityEffect(int Obs, int Motion, bool OnTarget, int Lift);

    /// <summary>
    /// 카운터 블레이드의 이펙트 — 둘 다 시전자 자리(원본은 895 를 네 번 겹쳐 띄운다).
    /// </summary>
    /// <remarks>표(<see cref="AbilityMotions"/>)가 이것을 쓰므로 <b>표보다 먼저</b> 선언해야 한다 — 정적 초기화는 적은 차례대로 돈다.</remarks>
    private static readonly AbilityEffect[] CounterBlade = [new(895, 0, false, 0), new(1365, 0, false, 0)];

    /// <summary>
    /// 메테오(어빌리티 95) — 레벨마다 work 이 다르지만(Lv1 469 · Lv2~10 744→736 · Lv11~20 783→774) 핸들러가 같아 효과도 같다.
    /// 뽑은 표(AbilityScripts.g.cs)는 Lv2 부터 소리만 든 Obs(1338·311)만 남겨 그림이 한 장도 안 나왔다(사용자 보고) — 모두 Lv1 효과를 쓴다.
    /// 원본의 착탄(170:1, 50틱 뒤)·폭발(109:7)·여러 발 흩뿌리기는 효과 지연이 없어 아직 못 넣었다.
    /// </summary>
    private static readonly AbilityEffect[] Meteor = [new(311, 0, true, 0), new(170, 0, true, 0), new(199, 0, true, 0), new(111, 0, true, 0)];

    /// <summary>work 번호 → (동작 차례, 때리는 순간에 띄울 이펙트).</summary>
    private static readonly Dictionary<int, (int[] Actions, AbilityEffect[] Effects)> AbilityMotions = new()
    {
        // 죠안
        [58] = ([6, 15], [new(297, 0, true, 0), new(312, 0, true, 42)]),        // 힐
        [87] = ([6, 15], [new(297, 1, true, 0), new(312, 1, true, 42)]),        // 큐어
        // 연 — 레벨 띠마다 사슬이 다르다(분석-모션 ba-10 「연 레벨별 동작 사슬과 타수」).
        // Lv1~4 = 2타, 5~8 = 3타, 9~12 = 4타, 13~16 = 5타, 17~20 = 6타.
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
        [10] = ([5, 7, 12, 24], [new(379, 0, true, 0), new(109, 0, true, 0)]),  // 비
        [59] = ([6, 15], [new(1332, 0, false, 0), new(1324, 1, false, 0)]),     // 격려
        // 제이슨
        [467] = ([6, 15], [new(386, 0, true, 0), new(171, 9, true, 30)]),       // 블레이드 미사일
        [469] = ([6, 15], Meteor),  // 메테오 Lv1 — Lv2~20 은 아래 표 끝에서 같은 효과로 채운다
        [735] = ([6, 15], [new(1380, 0, false, 0)]),                            // 크래쉬 봄
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
        // 메테오 Lv2~10(744→736)·Lv11~20(783→774) — 핸들러가 Lv1 과 같다
        [736] = ([6, 15], Meteor),
        [737] = ([6, 15], Meteor),
        [738] = ([6, 15], Meteor),
        [739] = ([6, 15], Meteor),
        [740] = ([6, 15], Meteor),
        [741] = ([6, 15], Meteor),
        [742] = ([6, 15], Meteor),
        [743] = ([6, 15], Meteor),
        [744] = ([6, 15], Meteor),
        [774] = ([6, 15], Meteor),
        [775] = ([6, 15], Meteor),
        [776] = ([6, 15], Meteor),
        [777] = ([6, 15], Meteor),
        [778] = ([6, 15], Meteor),
        [779] = ([6, 15], Meteor),
        [780] = ([6, 15], Meteor),
        [781] = ([6, 15], Meteor),
        [782] = ([6, 15], Meteor),
        [783] = ([6, 15], Meteor),
    };


    /// <summary>순간이동하는 work(이스케이프) — 쓰고 나면 겨눈 빈 칸으로 옮긴다.</summary>
    private const int EscapeWork = 1583;

    /// <summary>
    /// 그 work 의 대본 — <b>손으로 맞춘 표</b>가 먼저고, 없으면 도구가 뽑은 <see cref="WorkScripts"/> 다.
    /// </summary>
    /// <remarks>
    /// 손 표는 자리·높이까지 맞춰 둔 서른 남짓이고, 뽑은 표는 1540개다. 둘 다 없으면 예전처럼 기본공격 동작을 빌린다.
    /// </remarks>
    /// <summary>그림 컷 없이 소리 키만 든 Obs — 시전 소리(1338)·기술별 소리 껍데기(분석-스킬 fx-189).</summary>
    private static readonly HashSet<int> SoundShellObs = [1338, 1332, 1324, 311, 312, 379, 487, 1320, 1479, 1483];

    /// <summary>
    /// work 의 동작 차례와 이펙트 — 동작 차례·이펙트 자리는 손 표가 이기고, 도구 표(원본 핸들러에서 뽑은 것)의 이펙트는 <b>늘 뒤에 합친다</b>.
    /// 예전 도구가 파생 생성자로 만드는 이펙트를 놓쳐(분석-스킬 fx-189) 손 표에는 그림 하나(크래쉬 봄 1380)나 소리 껍데기만 적힌 줄이 많았다 —
    /// 손 표가 있다고 도구 표를 무시하면 폭탄·착탄 같은 그림이 영영 안 나온다. 같은 (Obs, 모션)은 손 표 것 하나만 둔다.
    /// </summary>
    private static (int[] Actions, AbilityEffect[] Effects)? ScriptFor(int work)
    {
        bool hasHand = AbilityMotions.TryGetValue(work, out var hand);
        bool hasMade = WorkScripts.TryGetValue(work, out var made);
        if (!hasHand) return hasMade ? made : null;
        if (!hasMade) return hand;
        return (hand.Actions, [.. hand.Effects, .. made.Effects.Where(e => !hand.Effects.Any(h => h.Obs == e.Obs && h.Motion == e.Motion))]);
    }

    private static int[] ActionsFor(WorkData w) =>
        ScriptFor(w.Id) is { Actions.Length: > 0 } m ? m.Actions
        : BasicWorkActions.GetValueOrDefault(w.Id) ?? StrikeActions;

    /// <summary>
    /// 판정이 나는 동작 — 동작 사슬 안에서 <b>타격 동작</b>(8·9·13·14·26·27)이 있으면 그 첫 자리,
    /// 없으면 기본공격은 가운데(베기), 어빌리티는 마지막 동작이다(분석-모션 ba-10).
    /// </summary>
    /// <summary>타격 판정이 드는 동작 — 8·9(베기·찌르기), 13·14(연속), 26·27.</summary>
    private static bool IsStrikeAction(int action) => action is 8 or 9 or 13 or 14 or 26 or 27;

    private static int HitStepFor(WorkData w, int steps)
    {
        int[] actions = ActionsFor(w);
        int strike = Array.FindIndex(actions, a => a is 8 or 9 or 13 or 14 or 26 or 27);
        if (strike >= 0) return strike;
        return ScriptFor(w.Id) != null ? Math.Max(0, steps - 1) : Math.Min(StrikeHitStep, steps - 1);
    }

    /// <summary>때리는 순간에 그 어빌리티의 이펙트를 띄운다.</summary>
    private void SpawnAbilityEffects(WorkData w, UnitState user, int col, int row)
    {
        SpawnWorkMovies(w, user, col, row, prelude: false);   // 치는 순간의 영상(리 바이블·어스퀘이크·강림의 밤)
        if (ScriptFor(w.Id) is not { } m) return;
        // 손으로 적어 둔 소리표가 있는 어빌리티는 그것이 소리를 낸다 — 여기서 또 내면 겹친다.
        bool ownSounds = !_abilitySounds.ContainsKey(w.AbilityId);
        foreach (var e in m.Effects)
        {
            var (x, y) = e.OnTarget
                ? (col * TileW + TileW / 2, CellCenterY(col, row))
                : UnitFoot(user);
            _effects.Add((e.Obs, e.Motion, _lastTime, x, y - e.Lift));
            // 이펙트 모션에 박힌 소리 키를 그 틱에 맞춰 예약한다 — 동작 소리(ScheduleActionSounds)와 같은 꼴이다.
            // 이것이 없으면 새로 붙인 기술 이펙트가 그림만 나오고 소리가 안 났다.
            if (!ownSounds || _effectTables.GetValueOrDefault(e.Obs)?.Clips.GetValueOrDefault(e.Motion) is not { } clip) continue;
            foreach (var (tick, sound) in clip.Sounds) _pendingSounds.Add((_lastTime + tick / TicksPerSecond, sound));
        }
    }
}
