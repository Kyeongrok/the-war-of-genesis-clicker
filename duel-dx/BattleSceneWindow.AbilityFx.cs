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
    /// <param name="Delay">띄우기까지 기다리는 틱(원본 <c>0x100c2530</c>) — 메테오 착탄 따위.</param>
    /// <param name="Count">뿌리개가 흩뿌리는 개수(설정 인자 3) — 대상 둘레 ±20·±10 픽셀에.</param>
    /// <param name="Fly">시전자에서 대상으로 날아가는 이펙트(생성자 <c>0x100c3340</c>·<c>0x100c5940</c>) — 모션 길이 동안 옮긴다.</param>
    private readonly record struct AbilityEffect(int Obs, int Motion, bool OnTarget, int Lift, int Delay = 0, int Count = 1, bool Fly = false);

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
        // 나인 크루세이더 — 공통 앞머리(시전 소리 1338:1 · 487:0/1/2 · 343:0 30틱 뒤 · 금빛 날개 344:18/19)만 두고,
        // 칼은 BattleSceneWindow.NineCrusader.cs 가 날린다. 도구 표의 344 모션 열한 개를 대상 한 자리에 겹쳐 띄우던 것은 뺀다(HandOnlyWorks).
        [NineCrusaderWork] = ([6, 15], [new(1338, 1, false, 0), new(487, 0, false, 0), new(487, 1, false, 0), new(487, 2, false, 0),
                                        new(343, 0, false, 0, 30), new(344, 18, false, 0), new(344, 19, false, 0)]),
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


    /// <summary>손 표만 쓰는 work — 도구 표의 이펙트가 틀려 합치면 안 되는 것.</summary>
    private static readonly HashSet<int> HandOnlyWorks = [NineCrusaderWork];

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
        if (!hasMade || HandOnlyWorks.Contains(work)) return hand;
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

    /// <summary>
    /// 필살기 초상 컷인 — 준비 동작 7 인 기술은 시전 때 그 인물의 초상(<c>.chr</c> 10 face Obs, 유닛 <c>+0x11e</c>)을 모션 2·1 로 네 번 띄워
    /// 화면을 가로지르게 한다(<c>0x1007eac4</c>·<c>0x1007eb60</c>·<c>0x1007ecdd</c>·<c>0x1007ed76</c>: 출발 x = 화면 스크롤 + 540, 날기 <c>0x100c3490</c>
    /// 속도 0.9, 지연 30틱, 수명 100틱). 도착 자리는 못 풀어(스택 인자) 화면 왼쪽 끝으로 둔다 — 가설. 분석-스킬 fx-189.
    /// </summary>
    private void SpawnFinisherCutIns(WorkData w, UnitState user)
    {
        if (w.Prepare != 7 || user.Data is not { FaceId: > 0 } c || UiFor(c.FaceId) == null) return;
        var sprite = UiFor(c.FaceId)!;
        for (int i = 0; i < 4; i++)
        {
            int motion = i % 2 == 0 ? 2 : 1;
            if (sprite.FrameAt(motion, 0, loop: true) is not { } f) continue;
            // 그림 가운데가 화면을 넷으로 나눈 줄에 오고, 오른쪽 밖에서 들어와 왼쪽 밖으로 나간다.
            int center = _camY + ViewHeight * (i + 1) / 5;
            int y = center - (f.Y + f.H / 2);
            double start = _lastTime + (30 + i * 8) / TicksPerSecond;
            _flyingEffects.Add((c.FaceId, motion, start, _camX + ViewWidth - f.X, y, _camX - f.X - f.W, y, 40));
        }
    }

    /// <summary>초능력공격(work 1479) 의 번개 구슬 — 체질이 없는(0) 인물만 이것을 쏜다.</summary>
    private const int PsychicBolt = 209;

    /// <summary>
    /// 초능력공격 핸들러 <c>0x10094590</c>: 유닛 <c>+0x122</c>(= 체질) 가 0 이면 번개 구슬 Obs 209 를 날리고(<c>0x100c3340</c>),
    /// 아니면 체질별 빛 구슬(큰 것 <c>0x100c3d60</c> 모션 2, 꼬리 작은 것 <c>0x100c3dc0</c>, 수명 20틱)을 둘 날린다 — 표 <c>0x1009496c</c>:
    /// 1 파랑(483·482) · 2 주황(479·478) · 3 빨강(475·472) · 4 보라(477·476) · 5 초록(481·480) · 그 밖 빨강(475·475).
    /// btl 0132 블랙스피어스(chr 318, 체질 4)가 번개 대신 붉은 빛을 쏘던 까닭(사용자 제보).
    /// </summary>
    private static (int Big, int Small)? PsychicOrbs(UnitState user) => user.Data?.Body switch
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
    private void SpawnAbilityEffects(WorkData w, UnitState user, int col, int row)
    {
        SpawnWorkMovies(w, user, col, row, prelude: false);   // 치는 순간의 영상(리 바이블·어스퀘이크·강림의 밤)
        SpawnRipples(w, col, row);                              // 익스퍼트 웨이브 파문(코드 이펙트)
        SpawnBodyClones(w, user, _units.FirstOrDefault(u => u.Alive && u.Col == col && u.Row == row), col, row);   // 분신·잔상
        if (ScriptFor(w.Id) is not { } m) return;
        // 손으로 적어 둔 소리표가 있는 어빌리티는 그것이 소리를 낸다 — 여기서 또 내면 겹친다.
        bool ownSounds = !_abilitySounds.ContainsKey(w.AbilityId);
        var (userX, userY) = UnitFoot(user);
        int targetX = col * TileW + TileW / 2, targetY = CellCenterY(col, row);
        foreach (var e in m.Effects)
        {
            var (x, y) = e.OnTarget ? (targetX, targetY) : (userX, userY);
            double start = _lastTime + e.Delay / TicksPerSecond;
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
            else
                for (int k = 0; k < Math.Max(1, e.Count); k++)
                {
                    // 뿌리개는 대상 둘레에 흩뿌리고 한 틱씩 어긋나게 띄운다(원본은 코드가 난수로 셈한다 — 가설).
                    int jx = k == 0 ? 0 : _rng.Next(-20, 21), jy = k == 0 ? 0 : _rng.Next(-10, 11);
                    _effects.Add((e.Obs, e.Motion, start + k / TicksPerSecond, x + jx, y - e.Lift + jy));
                }
            // 이펙트 모션에 박힌 소리 키를 그 틱에 맞춰 예약한다 — 동작 소리(ScheduleActionSounds)와 같은 꼴이다.
            // 이것이 없으면 새로 붙인 기술 이펙트가 그림만 나오고 소리가 안 났다.
            if (!ownSounds || _effectTables.GetValueOrDefault(e.Obs)?.Clips.GetValueOrDefault(e.Motion) is not { } clip) continue;
            foreach (var (tick, sound) in clip.Sounds) _pendingSounds.Add((start + tick / TicksPerSecond, sound));
        }
    }
}
