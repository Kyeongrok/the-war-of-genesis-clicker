using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// AI 가 차례에 하는 일(ba-11) — 적군과 동맹 AI(제이슨)가 같은 규칙으로 움직인다.
/// </summary>
/// <remarks>
/// 옵시디안 분석-전투 "AI 가 차례에 하는 일": <c>AIThink 0x10060730</c> 은 여섯 단계를 차례로 보고,
/// 앞 단계가 명령을 만들면 뒤는 아예 안 본다. 난수가 없어 같은 판이면 늘 같게 움직인다.
/// <list type="number">
/// <item>깨어남 — 조건을 못 채우면 휴식만.</item>
/// <item>도망 — HP% &lt; Num[66] 이고 가장 가까운 적이 Num[90] 칸 안이면 가장 안전한 칸으로.</item>
/// <item>자가 회복 — HP% &lt; Num[70] 이면 회복 work 만.</item>
/// <item>공격 — 어빌리티 목록(마지막이 기본공격)에서 <b>조건이 되는 첫 work</b>. 닿는 칸이 없으면 대상 쪽으로 걷고 휴식.</item>
/// <item>휴식 — HP% ≤ Num[71].</item>
/// <item>이동 — 이동 방식 순서대로 목표를 정해 그쪽으로.</item>
/// </list>
/// 세력 점수 <c>0x1005b120</c> = ((HP + 최대HP) / Num[62]) × (ACR × RDP × ATK / Num[61] / Num[63] / Num[64]),
/// 영향력 지도 <c>0x1005b210</c> 은 칸마다 <c>세력 × Num[65] / (거리 + Num[65])</c> 를 더한다.
/// 칸 점수 <c>0x1005d070</c> = 값 × Num[74] × 10 / (4 + |Δx| + |Δy|), 값의 기준은 work <c>+0x3e</c>.
/// 상태이상·아이템·편대는 아직 없고, 이동 방식은 0(가장 센 적 쪽)만 넣었다.
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    /// <summary>C 정수 나눗셈(0 으로 나누면 0).</summary>
    internal static int CDiv(int a, int b) => b == 0 ? 0 : a / b;

    /// <summary>
    /// 이동 방식이 정하는 <b>목표 차례</b>(<c>0x1005c460</c>) — 앞에서부터 갈 칸이 나오는 첫 목표를 쓴다.
    /// </summary>
    /// <remarks>
    /// <c>0</c> 가깝고 센 적 · <c>1</c> <b>내 편이 몰아붙이고 있는</b> 적 · <c>2</c> 먼 적 ·
    /// <c>3</c> 가깝고 센 아군 곁 · <c>4</c> 멀고 약한 아군 곁 · <c>5</c> 위험한 아군 곁.
    /// 그 밖(6 이상·음수)은 목록이 비어 제자리에서 쉰다. 시판 자료는 <b>0·1·2·3 만</b> 쓴다(2154·2·9·1명).
    /// <para>동점이면 <c>Btl</c> 줄 순서를 지킨다 — 원본이 인접 맞바꿈으로 고르기 때문이다.</para>
    /// </remarks>
    internal List<UnitState> MoveGoals(UnitState u, List<UnitState> enemies)
    {
        if (host._db is not { } db) return [];
        int Reach(UnitState t) => 4 + Math.Abs(t.Col - u.Col) + Math.Abs(t.Row - u.Row);
        var friends = host._units.Where(t => t.Alive && t != u && !SeesAsFoe(u, t)).ToList();

        return u.AiMove switch
        {
            0 => [.. enemies.OrderByDescending(t => CDiv(Power(t) * db.N(74), Reach(t)))],
            1 => [.. enemies.OrderByDescending(t => Danger(t, t.Col, t.Row) * 4 / Reach(t))],
            // 먼 적부터 — 원본 0x10060d10 은 앞 원소를 4|Δx|+6|Δy|, 뒤 원소를 4|Δx|+4|Δy|+2|Δz| 로 재는 비대칭 버블 정렬이라 그대로 흉내 낸다(ba-14 A3).
            2 => FarSortLikeOriginal(u, enemies),
            3 => [.. friends.OrderByDescending(t => CDiv(Power(t) * db.N(74), Reach(t)))],
            4 => [.. friends.OrderBy(t => CDiv(Power(t) * db.N(74), Reach(t)))],
            5 => [.. friends.OrderByDescending(t => Danger(t, t.Col, t.Row) * 4 / Reach(t))],
            _ => [],
        };
    }

    /// <summary>
    /// 이동 방식 2 의 정렬(<c>0x10060d10</c>) — 유닛 배열 순서에서 시작해, 앞 원소 Da = 4|Δx|+6|Δy|, 뒤 원소 Db = 4|Δx|+4|Δy|+2|Δz| 로 재고
    /// Da &lt; Db 면 바꾼다(먼 쪽이 앞). 같으면 y 가 작은 쪽, 그다음 x 가 작은 쪽이 앞. 비추이적 비교라 초기 순서까지 같아야 결과가 같다.
    /// </summary>
    internal List<UnitState> FarSortLikeOriginal(UnitState u, List<UnitState> enemies)
    {
        var list = enemies.OrderBy(t => Array.IndexOf(host._units, t)).Take(100).ToList();
        int mz = HeightAt(u.Col, u.Row);
        for (int pass = list.Count - 1; pass >= 1; pass--)
            for (int j = 0; j < pass; j++)
            {
                var a = list[j]; var b = list[j + 1];
                int da = 4 * Math.Abs(u.Col - a.Col) + 6 * Math.Abs(u.Row - a.Row);
                int db = 4 * Math.Abs(u.Col - b.Col) + 4 * Math.Abs(u.Row - b.Row) + 2 * Math.Abs(mz - HeightAt(b.Col, b.Row));
                bool swap = da < db || da == db && (a.Row > b.Row || a.Row == b.Row && a.Col > b.Col);
                if (swap) (list[j], list[j + 1]) = (b, a);
            }
        return list;
    }

    /// <summary>
    /// 깨어남 조건을 지금 채웠나 — <c>0</c> 없음(바로 깸) · <c>1</c> 깨어 있는 같은 편까지 거리 · <c>2</c> 가장 가까운 적까지 거리 ·
    /// <c>3</c> 전투 틱. <b>4 이상은 영영 안 깬다</b>(분기표 <c>0x1005baa8</c> 이 네 칸뿐).
    /// </summary>
    /// <remarks>
    /// 거리는 <c>|Δ열| + |Δ행| + |Δ높이| / 2</c> 이고 <b>같거나 작으면</b> 깬다. 찾는 상대가 없으면 1000 으로 친다.
    /// 자료 2182명 중 1798명이 0(바로), 315명이 2(적이 다가오면), 37명이 3(몇 틱 뒤)이다.
    /// </remarks>
    internal bool WakesNow(UnitState u)
    {
        int WakeDistance(UnitState other) =>
            Math.Abs(other.Col - u.Col) + Math.Abs(other.Row - u.Row)
            + Math.Abs(HeightAt(other.Col, other.Row) - HeightAt(u.Col, u.Row)) / 2;

        return u.WakeCondition switch
        {
            0 => true,
            // 전장 밖((0,0) 대기) 인물은 안 센다(0x1006e940) — 안 나온 증원과의 거리로 깨지 않게.
            1 => Nearest(t => t.Alive && t.OnField && t != u && !SeesAsFoe(u, t) && t.Awake && !t.PlayerControlled) <= u.WakeValue,
            2 => Nearest(t => t.Alive && t.OnField && SeesAsFoe(u, t)) <= u.WakeValue,
            3 => _tick >= u.WakeValue,
            _ => false,
        };

        int Nearest(Func<UnitState, bool> pick)
        {
            var found = host._units.Where(pick).Select(WakeDistance).ToList();
            return found.Count == 0 ? 1000 : found.Min();
        }
    }

    /// <summary>세력 점수 — AI 가 "이 인물이 얼마나 센가" 를 재는 값.</summary>
    internal int Power(UnitState u)
    {
        if (host._db is not { } db || u.Data is not { } c) return 0;
        int v = db.Acr(c, u.Tp) * db.Rdp(c, u.Hp, u.MaxHp) * db.Atk(c, u.Soul);
        v = CDiv(CDiv(CDiv(v, db.N(61)), db.N(63)), db.N(64));
        return CDiv(u.Hp + u.MaxHp, db.N(62)) * v;
    }

    /// <summary>
    /// 그 칸이 받는 영향력 — <paramref name="viewer"/> 눈에 적(<paramref name="foes"/>)인, 또는 한편인 살아 있는 인물들의 세력을 거리로 나눠 더한다.
    /// 편은 세력 행렬로 가른다(편 0·1·2 는 서로 적). <paramref name="viewer"/> 자신은 넣지 않는다(원본 지도 <c>0x1005b5e0</c> 자기포함=0).
    /// </summary>
    internal int Influence(int col, int row, UnitState viewer, bool foes)
    {
        int num65 = host._db?.N(65) ?? 6, sum = 0;
        foreach (var u in host._units)
        {
            if (!u.Alive || !u.OnField || u == viewer || Hostile(viewer, u) != foes) continue;
            int d = Math.Abs(u.Col - col) + Math.Abs(u.Row - row);
            sum += CDiv(Power(u) * num65, d + num65);
        }
        return sum;
    }

    /// <summary>그 칸의 위험도(기준 16·17, 이동 방식 1·5) — 적 영향력 / 아군 영향력(자기 몫은 안 넣는다 — 전에는 두 번 넣었다).</summary>
    internal double Danger(UnitState u, int col, int row)
    {
        int enemies = Influence(col, row, u, foes: true);
        // 지도에는 제 번짐이 한 번 들어 있다(0x1005b5e0(t, 0) — 기준 16·17 0x1005c976, 이동 방식 1·5 0x100612b0, ba-20 J N4).
        int friends = Influence(col, row, u, foes: false)
                      + CDiv(Power(u) * (host._db?.N(65) ?? 6), Math.Abs(u.Col - col) + Math.Abs(u.Row - row) + (host._db?.N(65) ?? 6));
        return friends == 0 ? enemies : (double)enemies / friends;
    }

    /// <summary>
    /// 도망이 재는 위험도(<c>0x1005b4e0</c>) — 제 번짐을 지도에서 뺀 뒤 적 영향력 / (제 세력 점수 + 남은 아군 영향력).
    /// 자기 몫이 모든 칸에서 같아야 「어디로 가면 안전한가」만 남는다.
    /// </summary>
    internal double FleeDanger(UnitState u, int col, int row)
    {
        int enemies = Influence(col, row, u, foes: true);
        int friends = Power(u) + Influence(col, row, u, foes: false);
        return friends <= 0 ? enemies : (double)enemies / friends;
    }

    /// <summary>도망의 안전 문턱 — 위험도 이 값 이하인 칸이면 된다(원본 절댓값 0.70).</summary>
    internal const double SafeDanger = 0.70;

    /// <summary>work <c>+0x3e</c> 기준으로 그 대상들이 얼마나 좋은지 — 짝수면 최댓값, 홀수면 1000000 − 최솟값.</summary>
    /// <param name="plain">난이도 겨냥(SmartAi)을 끄고 원본 칸 값만 — 사건 207 필살기의 겨냥은 원본 그대로(0x1005d860, 감사5 B6).</param>
    internal int TargetValue(UnitState user, WorkData w, List<int> targets, bool plain = false)
    {
        if (targets.Count == 0) return 0;
        if (host._scene.IsPart1 && Part1Rules.TemperatureSkill(Part1.AttackOf(w)) is not 0 and var heat) return Part1TemperatureValue(user, heat, targets);
        if (!plain && w.IsDamage && SmartAi(user)) return SmartValue(user, w, targets);   // 어려움 이상: 가장 많이 깎는 곳(Difficulty.cs)
        int criterion = w.AiCriterion >> 1;
        bool wantMax = (w.AiCriterion & 1) == 0;
        int best = wantMax ? int.MinValue : int.MaxValue;
        foreach (int i in targets)
        {
            var t = host._units[i];
            int v = criterion switch
            {
                0 => t.Hp,
                1 => t.Soul,
                2 => host._db is { } db && t.Data is { } c ? db.Atk(c, t.Soul) : 0,
                3 => t.Tp,
                4 => host._db is { } db2 && t.Data is { } c2 ? db2.Rdp(c2, t.Hp, t.MaxHp) : 0,
                5 => host._db is { } db3 && t.Data is { } c3 ? db3.Acr(c3, t.Tp) : 0,
                6 => t.MaxHp - t.Hp,
                7 => Power(t),
                // 위험도는 <b>대상 자신의 시점</b>으로 잰다(0x1005c976) — 쓰는 쪽 시점으로 재면
                // 아군 보조기(큐어 같은 것)에서 부호가 뒤집힌다.
                _ => (int)(Danger(t, t.Col, t.Row) * 1000),
            };
            best = wantMax ? Math.Max(best, v) : Math.Min(best, v);
        }
        return wantMax ? best : 1000000 - best;
    }

    /// <summary>그 칸을 겨눴을 때 쓸 만한가 — 피해는 대상 수, 회복은 잃은 HP 비율 합이 기준을 넘어야 한다.</summary>
    internal bool WorthUsing(UnitState user, WorkData w, List<int> targets)
    {
        int need = w.MinTargets + 1;
        // 큐어는 한 명에게만 간다(WorkTargets) — <b>제 편이고 해로운 상태가 걸린</b> 대상일 때만 쓴다. 큐어의 대상 방식은 5(아무 유닛)라
        // 전에는 적에게도(상태가 없는 상대에게도) 썼다(사용자 보고: Btl 0136 의 이반). 원본 AI 가 어떻게 거르는지는 확인하지 않았다(상식대로).
        // 파트1 속성 부여 — 이미 그 속성이 걸린 사람에게는 다시 안 건다(사용자 보고: Btl 20087 위자드가 화이어 블레이드를 되풀이).
        if (host._scene.IsPart1 && Part1.EnchantOf(w) is > 0 and var element)
            return targets.Any(i => host._units[i] is var t && !Hostile(user, t) && Part1.Enchant.GetValueOrDefault(t) != element);
        // 파트1 최면 · 혼란 · 컨디션이상 — 적이고, 듣는 종족이고, 그 상태가 아직 없고 막는 상태도 없는 대상만 센다(원본 AI 평가 0x1003fd93 이
        // 이미 걸린 대상에게 0 을 준다).
        if (host._scene.IsPart1 && Part1.AttackOf(w)?.Formula is (13 or 25 or 27 or 28) and var p1sel)
            return targets.Count(i => host._units[i] is var t && SeesAsFoe(user, t) && Part1.AilmentWorth(t, p1sel)) >= w.MinTargets + 1;
        // 파트1 아이스 실드(선택자 14 → 상태 10) — 이미 걸린 대상은 원본 AI 어림이 0 이다(0x1003fdf9). 제 편만 세는 것은 상식대로.
        if (host._scene.IsPart1 && Part1.AttackOf(w)?.Formula == 14)
            return targets.Count(i => host._units[i] is var t && !Hostile(user, t) && !Part1.Has(t, 14)) >= need;
        if (w.AbilityId == CureAbility)
            return targets.Any(i => host._units[i] is var t && !Hostile(user, t)
                                    && Enumerable.Range(0, 3).Any(k => AilmentScore(t.StatusId[k], t.StatusValue[k]) < 0));
        // 블랙홀은 화면 HP 가 위력보다 적은 유닛만 쓰러뜨린다 — 쓰러질 적이 쓰러질 제 편(시전자 포함)보다 많을 때만 쓴다(데모 판단, 원본 AI 는 대상 수만 본다).
        if (w.AbilityId == BlackHoleAbility)
        {
            var doomed = targets.Select(i => host._units[i]).Where(t => t.Hp < w.Power).ToList();
            return doomed.Count(t => Hostile(user, t)) > doomed.Count(t => !Hostile(user, t));
        }
        if (w.IsHeal)
        {
            int lost = targets.Sum(i => host._units[i].MaxHp == 0 ? 0 : (host._units[i].MaxHp - host._units[i].Hp) * 100 / host._units[i].MaxHp);
            return lost > (host._db?.N(79) ?? 30) * need;
        }
        // 종류 3(보조·상태이상)은 <b>이득이 있는 대상만</b> 센다(0x1005c480·0x1005c6c7) — 대상이 가진 상태 3칸의 점수 합 A 와
        // work 가 거는 상태 3칸의 점수 합 B 를 견줘, 적(+0x3c=1)이면 A>B, 아군(+0x3c=4)이면 A<B 인 대상만. 그 밖의 +0x3c 는 아무도 안 센다(ba-14 A2).
        if (w.Kind == 3)
        {
            if (targets.Count < need) return false;
            int b = w.Bonuses.Sum(x => AilmentScore(x.Stat, x.Value));
            int count = 0;
            foreach (int i in targets)
            {
                var t = host._units[i];
                int a = 0;
                for (int k = 0; k < 3; k++) a += AilmentScore(t.StatusId[k], t.StatusValue[k]);
                if (w.AiTargetSide == 1 ? a > b : w.AiTargetSide == 4 && a < b) count++;
            }
            return count >= need;
        }
        // 파트1 화이어 필드 · 프로즌 아이스 — 피해가 아니라 온도로 잰 원본 어림(Part1TemperatureValue)이 0 보다 커야 쓴다(0 이면 원본도 −1 「못 씀」, 0x10040c03).
        if (host._scene.IsPart1 && Part1Rules.TemperatureSkill(Part1.AttackOf(w)) is not 0 and var heat)
            return Part1TemperatureValue(user, heat, targets) > 0;
        // 파트1 피해 기술 — 적만 센다. 범위 안 모두를 맞히는 것(Attack +0x40 = 2: 화이어 필드 · 프로즌 아이스 · 다탄두 미사일 …)은
        // 제 편이 적보다 많이 맞는 칸은 고르지 않는다(원본 AI 는 AI 표 거르기 +0xe 로 0x10039d30 에서 센다 — 상식대로 옮긴 것).
        if (host._scene.IsPart1 && w.IsDamage)
        {
            int foes = targets.Count(i => SeesAsFoe(user, host._units[i]));
            return foes >= need && foes > targets.Count - foes;
        }
        return targets.Count >= need;
    }

    /// <summary>
    /// 파트1 온도 기술의 원본 AI 어림(<c>0x1003ff40</c> — 화이어 필드 갈래 <c>0x100400bd</c>, 프로즌 아이스 갈래 <c>0x10040296</c>). 범위의 유닛마다(나는 유닛 +0xba ·
    /// 기계 +0xb8 = 2 빼고) 온도 t 로 값 v 를 매겨 v·(AC + MR)/200 을 더하고(AC = 쉐도우 실드 ×1.5 · 버써크 ½, MR = 쉐도우 실드 ½, 둘 다 소울 게이지로 깎음 —
    /// <c>0x1003edc0</c> · <c>0x1003ed50</c>), 합 × 20 을 낸다:
    /// <list type="bullet">
    /// <item>화이어 필드(<paramref name="sign"/> +1) — 땅불이 이미 있는 칸(<c>0x1008fc20</c>)은 건너뛴다. 적: t &gt; 60 이면 (t − 80)/2 + 30, 아니면 0.
    /// 제 편: 얼음 칸(<c>0x1008fcc0</c>)이거나 t ≤ 0 이면 (80 − t)·2(녹여 준다), 아니면 −50.</item>
    /// <item>프로즌 아이스(−1) — 얼음 칸은 건너뛴다. 적: t &lt; 100 이면 (80 − t)/2 + 30, 아니면 0. 제 편: 땅불 칸이거나 t ≥ 160 이면 2t − 160(식혀 준다), 아니면 −50.</item>
    /// </list>
    /// 원본은 이 합을 (150 + 비용 − (소울 − 비용 + 20)/2)로 나눠 다른 기술의 어림과 견주지만, 리메이크 AI 는 기술 차례(<see cref="AiWorks"/>)로 고르므로 합만 쓴다
    /// (겨눌 칸 점수 · 「쓸 만한가」). 피해 세기(<see cref="Part1Rules.AiStrength"/>)는 0 이라 피해 기술 뒤에 둔다.
    /// </summary>
    internal int Part1TemperatureValue(UnitState user, int sign, List<int> targets)
    {
        long sum = 0;
        foreach (int i in targets)
        {
            var t = host._units[i];
            if (Part1.Fighter(t) is not { } f || f.Chr.Fields[Part1Chr.FlyField] != 0 || Part1.IsMachine(t)) continue;
            int h = Part1Objects.UnitHeat(t);
            bool foe = SeesAsFoe(user, t), fire = Part1Objects.FireAt(t.Col, t.Row), ice = Part1Objects.IceAt(t.Col, t.Row);
            int v;
            if (sign > 0)
            {
                if (fire) continue;
                v = foe ? (h > 60 ? (h - 80) / 2 + 30 : 0) : ice || h <= 0 ? (80 - h) * 2 : -50;
            }
            else
            {
                if (ice) continue;
                v = foe ? (h < 100 ? (80 - h) / 2 + 30 : 0) : fire || h >= 160 ? 2 * h - 160 : -50;
            }
            int ac = f.Stat(Part1Chr.Ac), mr = f.Stat(Part1Chr.Mr);
            if (Part1.Has(t, 22)) { ac = ac * 3 / 2; mr /= 2; }
            if (t.Status(4) > 0) ac /= 2;
            sum += (long)v * (Part1Combat.Scaled(ac, t.Soul) + Part1Combat.Scaled(mr, t.Soul)) / 200;
        }
        long value = sum * 20;
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"p1temp ai unit {Array.IndexOf(host._units, user)} chr {user.ChrCode} {(sign > 0 ? "화이어 필드" : "프로즌 아이스")} 대상 {targets.Count}: 어림 {value}" + Environment.NewLine);
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// 파트1 AI 가 갈 칸을 고를 때 거르는 칸(<c>0x100267de</c> · <c>0x10026a74</c>) — 제 온도가 140 을 넘으면 땅불 칸(<c>0x1008fc20</c>), 20 아래면 얼음 칸(<c>0x1008fcc0</c>).
    /// </summary>
    internal bool Part1AiAvoids(UnitState u, int col, int row)
    {
        if (!host._scene.IsPart1) return false;
        int h = Part1Objects.UnitHeat(u);
        return h > 140 && Part1Objects.FireAt(col, row) || h < 20 && Part1Objects.IceAt(col, row);
    }

    /// <summary>상태 점수표 <c>0x1005c4e0</c> — 해로운 것 −1, 이로운 것 +1, 값의 부호로 갈리는 것(13·14·21·29 는 양수가 이득, 18·20·38 은 음수가 이득), 나머지 0.</summary>
    internal static int AilmentScore(int id, int value) => id switch
    {
        2 or 3 or 4 or 5 or 6 or 12 or 15 or 16 or 17 or 19 or 22 or 23 or 24 or 25 or 26 or 27 => -1,
        7 or 8 or 9 or 10 or 11 or 41 or 47 => 1,
        13 or 14 or 21 or 29 => value > 0 ? 1 : -1,
        18 or 20 or 38 => value < 0 ? 1 : -1,
        _ => 0,
    };

    /// <summary>work 하나로 가장 좋은 (설 칸, 겨눌 칸)을 찾는다. 없으면 null.</summary>
    /// <remarks>
    /// 원본은 <b>두 단계</b>다(<c>0x1005d070</c>) — 먼저 <b>지금 자리 기준</b>으로 겨눌 칸의 점수를 매겨 가장 좋은 칸을 정하고,
    /// 그다음 그 칸에 닿는 칸 가운데 <b>맨해튼으로 가장 가까운</b> 설 칸을 고른다.
    /// 설 칸 × 겨눌 칸을 한꺼번에 재면 「멀리 가서 크게 때리는」 쪽이 과하게 뽑힌다.
    /// </remarks>
    /// <param name="anchor">점수의 거리 기준 칸 — 군단 부하면 대장이 겨눈 칸(0x1005e6e0~0x1005e70a). 없으면 제 칸. 주면 물체는 후보에서 뺀다.</param>
    /// <param name="taken">앞 부하가 먼저 잡은 설 칸(0x1005ff9a) — 설 칸 후보에서 뺀다.</param>
    internal (int Stand, int Col, int Row, int Score)? BestUse(int unitIndex, WorkData w, MoveRange range, bool approach = false,
                                                              (int Col, int Row)? anchor = null, HashSet<int>? taken = null)
    {
        var user = host._units[unitIndex];
        int num74 = host._db?.N(74) ?? 4;
        // 효과 모양이 0 인 work(이스케이프·혼 등 387개)는 칠할 칸이 없어 대상 0 → 값 0 → AI 가 절대 안 고른다(0x100704f0 → 0x1005d4d7, ba-14 A5).
        if (w.AreaShape == 0) return null;

        // 겨눌 칸 후보는 <b>갈 수 있는 칸에서 사거리에 드는 칸</b>뿐이다(층 12, 0x100749b0) — 맵 전체를 채점하면 못 닿는 1등 칸 때문에
        // 닿는 다른 적이 있어도 기술을 통째로 버렸다(fg-21 ⑮). 대상 방식 6(방향기)은 제자리 네 이웃 칸만 본다.
        var stands = new List<int>();
        var candidates = new HashSet<(int Col, int Row)>();
        int here = user.Row * host.Cols + user.Col;
        // 제자리 기술(사거리 대상 0·2)은 <b>지금 칸에서만</b> 쓴다(0x1005d269 → 0x1005d725) — 걸어가서 쓰지 않는다(ba-20 J N1).
        // 접근 (B) 단계만 「최대 TP 로 닿을 칸」을 찾느라 이동을 허용한다.
        if (w.SelfCentred && !approach)
        {
            stands.Add(here);
            candidates.Add((user.Col, user.Row));
        }
        else if (w.TargetMode == 6)
        {
            stands.Add(here);
            foreach (var (dx, dy) in new[] { (0, -1), (-1, 0), (0, 1), (1, 0) })   // 위·왼·아래·오른 — 동점이면 먼저 나온 쪽(분기표 0x1005d844, ba-20 AI N10)
                if ((uint)(user.Col + dx) < host.Cols && (uint)(user.Row + dy) < host.Rows) candidates.Add((user.Col + dx, user.Row + dy));
        }
        else
        {
            // 군단 대장이 아군 대상(+0x3c = 4) 기술을 쓸 때는 자리를 옮기지 않는다 — 제자리에서 닿는 칸만 후보(0x10075420,
            // 0x1005d070 안의 토막, 분석-군단 「+0x3c == 4 분기」). 전에는 걸어가서 썼다(원본차이-AI 23).
            bool stayPut = w.AiTargetSide == 4 && user.LeaderIndex < 0 && FollowersOf(unitIndex).Count > 0;
            int reach = Math.Max(1, RangeMaxOf(w, user) / 4) + 1;
            // 원본 AI 는 사거리를 그리기 = 0 으로 칠해(0x1005cbd2·0x1005d355·0x1005e5bd·0x1005eae2 → 0x100749b0(…, 0)) 대상 방식 0·2·6 +
            // 높이반영 work 은 거리 자 0x100daca0 이 대상 높이를 칸 높이가 아니라 <b>모드 값 그 자체</b>로 잰다(0x100daf27, 버그) — 화이어 웨이브 등
            // 128개를 시전자가 6층에 서 있지 않으면 사실상 못 쓴다(감사3 R8). 리메이크는 일부러 따라가지 않고 늘 칸 높이로 잰다.
            for (int stand = 0; stand < range.Cost.Length; stand++)
            {
                if (!range.CanReach(stand) || (stayPut && stand != here) || stand != here && Part1AiAvoids(user, stand % host.Cols, stand / host.Cols)) continue;
                stands.Add(stand);
                int sc = stand % host.Cols, sr = stand / host.Cols;
                for (int ay = Math.Max(0, sr - reach); ay <= Math.Min(host.Rows - 1, sr + reach); ay++)
                    for (int ax = Math.Max(0, sc - reach); ax <= Math.Min(host.Cols - 1, sc + reach); ax++)
                        if (!candidates.Contains((ax, ay)) && InWorkRange(w, sc, sr, ax, ay, user)) candidates.Add((ax, ay));
            }
        }

        // ① 겨눌 칸 — 점수는 지금 서 있는 자리에서 잰다.
        (int Col, int Row, int Score)? aim = null;
        bool aimIsObject = false;
        // 파트1 겨누기 +0x31 = 7(혼 · 혈랑마혼 · 비연참 · 댓쉬 · 워프)은 시전자가 설 수 있는 빈 칸만 겨눈다(0x1008a950 → 0x10089bf0) — 높이는 설 칸에서 따로 본다.
        bool landOnly = Part1.AttackOf(w) is { AimFilter: 7 };
        foreach (var (col, row) in candidates)
        {
            if (landOnly && !CanLandOn(col, row, user)) continue;
            var targets = WorkTargets(w, user, col, row);
            // 파트1 AI 표 이동 한도 밖의 적은 대상으로 안 센다(AiThink 가 정한 _part1Aware).
            if (_part1Aware is ({ } owner, { } seen) && owner == user) targets.RemoveAll(i => SeesAsFoe(user, host._units[i]) && !seen.Contains(i));
            // 파트1 대상 거르기 — 표에서 떨어지는 대상(기계에게 블래스 · 스피디 …)과 기계에게 회복은 원본 AI 어림이 0 이다(0x1003fd6f · 0x100410b2 · 0x100410f9).
            if (host._scene.IsPart1) targets.RemoveAll(i => !Part1.AiAffects(w, host._units[i]));
            int value;
            bool onObject = false;
            if (targets.Count > 0 && WorthUsing(user, w, targets)) value = TargetValue(user, w, targets);
            // 때릴 수 있는 물체(중립·적 바리케이트, 적 포탑·크리스탈)도 겨눈다 — 거리 자 0x100daca0 방식 1·5 가 그 칸을 켜고
            // 0x1005c8fc 가 물체 값을 낸다(ba-20 J N3). 값이 사람과 거의 같아 닿는 것 중 가장 가까운 것을 친다. 물체 쪽으로 다가가지는 않는다.
            // 접근 (B) 단계에서는 물체를 후보로 안 넣는다(물체 쪽으로 다가가지 않는다). 어려움 AI 는 유닛 후보가 하나도 없을 때만 물체를 본다.
            else if (targets.Count == 0 && !approach && anchor == null && AiObjectAt(w, user, col, row) is { } obj)
            {
                if (SmartAi(user) && aim != null && !aimIsObject) continue;
                value = AiObjectValue(w, obj);
                onObject = true;
            }
            else continue;

            var (fromCol, fromRow) = anchor ?? (user.Col, user.Row);
            int score = CDiv(value * num74 * 10,
                             4 + Math.Abs(col - fromCol) + Math.Abs(row - fromRow));
            // 어려움 AI 는 점수가 나는 유닛 후보가 있으면 물체 후보를 버린다.
            if (score > 0 && (aim == null || (aimIsObject && !onObject && SmartAi(user)) || score > aim.Value.Score)) { aim = (col, row, score); aimIsObject = onObject; }   // 점수 > 0 인 칸만(0x1005d4d7)
        }
        if (aim is not { } pick) return null;

        // ② 설 칸 — 그 칸에 닿는 칸 중 지금 자리에서 가장 가까운 곳.
        int bestStand = -1, bestDist = int.MaxValue;
        foreach (int stand in stands)
        {
            int sc = stand % host.Cols, sr = stand / host.Cols;
            if (taken != null && stand != here && taken.Contains(stand)) continue;
            if (!InWorkRange(w, sc, sr, pick.Col, pick.Row, user)) continue;
            if (landOnly && HeightAt(sc, sr) != HeightAt(pick.Col, pick.Row)) continue;   // 거르기 7 — 겨눈 칸이 설 칸과 같은 높이(0x1008a9db)
            if (WorkTargetsFrom(w, user, sc, sr, pick.Col, pick.Row) is not { Count: > 0 } && AiObjectAt(w, user, pick.Col, pick.Row) == null) continue;
            int d = Math.Abs(sc - user.Col) + Math.Abs(sr - user.Row);
            if (d < bestDist) { bestDist = d; bestStand = stand; }
        }
        return bestStand < 0 ? null : (bestStand, pick.Col, pick.Row, pick.Score);
    }

    /// <summary>AI 가 그 칸에서 때릴 수 있는 물체 — 피해 기술(대상 방식 1·5, 최소 대상 1)이고 부술 수 있는 적대 물체일 때.</summary>
    internal DemoObject? AiObjectAt(WorkData w, UnitState user, int col, int row) =>
        // 기본공격으로만 친다 — 전용 연출 갈래를 타는 기술은 물체 피해 고리를 안 지나 헛손질이 된다. 종류는 바리케이트(7)·포탑(9)·크리스탈(10)만
        // (상자·폭탄 상자는 표 값이 1 이라 못 때린다, 0x1006fe40).
        w.IsDamage && w.TargetMode is 1 or 5 && w.MinTargets == 0 && user.Data?.BasicWorkId == w.Id
        && ObjectAt(col, row) is { Alive: true, Data.Breakable: true, Data.Kind: 7 or 9 or 10 } obj && ObjectHostile(obj, user) && !_opened.Contains(obj) ? obj : null;

    /// <summary>물체의 칸 값(0x1005c8fc 물체 가지) — 기준 0/1 HP, 4/5 공격력, 12/13 잃은 HP, 그 밖 0. 짝수는 최댓값, 홀수는 1000000 − 최솟값.</summary>
    internal int AiObjectValue(WorkData w, DemoObject obj)
    {
        int v = (w.AiCriterion >> 1) switch
        {
            0 => obj.Hp,
            2 => _objGrowth.TryGetValue(obj, out var g) ? g.Attack : 0,
            6 => (_objGrowth.TryGetValue(obj, out var g2) ? g2.MaxHp : obj.Data.MaxHp) - obj.Hp,
            _ => 0,
        };
        return (w.AiCriterion & 1) == 0 ? v : 1000000 - v;
    }

    /// <summary>
    /// 목표 칸까지의 <b>경로 비용 지도</b> — 목표에서 출발하는 이동 영역을 예산 없이 잰다. 갈 수 있는 칸 중 이 값이 가장 작은 칸이
    /// 「목표에 가장 가까운 칸」이다(원본 <c>0x10059a20</c>). 전에는 맨해튼 거리로 골라 벽·강 너머 목표에서 벽에 붙어 멈췄다.
    /// </summary>
    /// <remarks>
    /// 원본 지도는 <b>지형만</b> 본다 — 시작 칸 <c>0x100d9e40</c> 은 판 안 + 플래그 <c>&amp;9</c> 없음, 한 걸음 <c>0x100da180</c> 은 거기에
    /// <c>|Δ높이(+0x78)| ≤ 2</c> 만 더하고, 비용 <c>0x100da760</c> 은 걷기와 같은 걸음 비용식이다. 유닛·ZOC·문 목표 칸 금지는 없다(감사3 R4).
    /// 전에는 이동 영역(<see cref="ComputeRange"/>)을 그대로 써서 적을 목표로 하면 둘레 네 칸이 모두 ZOC 라 지도가 목표 칸 하나로 끝나고,
    /// 모두 맨해튼 대체값으로 떨어져 벽 너머 적 앞에서 벽에 붙어 멈췄다. 플래그·높이는 물체까지 찍은 판으로 본다(감사3 R1).
    /// 목표 칸 자체는 물체 칸(포탑 따위)이어도 출발로 친다(가설 — 원본이 시작 칸이 막혔을 때 무엇을 돌려주는지는 안 봤다).
    /// </remarks>
    internal int[]? CostMapFrom(UnitState u, int col, int row)
    {
        if (host._map is not { } map || host._db is not { } db || u.Data is not { } c) return null;
        bool InBounds(int x, int y) => (uint)x < host.Cols && (uint)y < host.Rows && x < map.Cols && y < map.Rows;
        if (!InBounds(col, row)) return null;
        int dex = Math.Max(1, db.Dex(CombatData(u) ?? c)), num5 = db.N(5);
        bool fly = Part1Flyer(u) != null;                    // 파트1 나는 유닛은 벽 · 높이 차를 넘는다(ComputeRange 와 같은 규칙)
        var costs = new int[host.Cols * host.Rows];
        Array.Fill(costs, int.MaxValue);
        costs[row * host.Cols + col] = 0;
        var queue = new PriorityQueue<(int Col, int Row), int>();
        queue.Enqueue((col, row), 0);
        (int Dx, int Dy)[] dirs = [(0, -1), (1, 0), (0, 1), (-1, 0)];
        while (queue.TryDequeue(out var cell, out int cost))
        {
            if (cost > costs[cell.Row * host.Cols + cell.Col]) continue;
            int h = WalkHeightAt(cell.Col, cell.Row);
            foreach (var (dx, dy) in dirs)
            {
                int nx = cell.Col + dx, ny = cell.Row + dy;
                if (!InBounds(nx, ny) || (!fly && (CellFlagsAt(nx, ny) & 0x9) != 0)) continue;
                int nh = WalkHeightAt(nx, ny);
                if (!fly && Math.Abs(nh - h) > 2) continue;
                int next = cost + num5 * Math.Abs(dy) / dex + num5 * Math.Abs(dx) / dex + (num5 * Math.Abs(nh - h) / dex) / 2;
                if (next >= costs[ny * host.Cols + nx]) continue;
                costs[ny * host.Cols + nx] = next;
                queue.Enqueue((nx, ny), next);
            }
        }
        return costs;
    }

    /// <summary>갈 수 있는 칸 가운데 목표까지 경로 비용이 가장 작은 칸(같으면 지금 자리에서 싼 칸). 없으면 −1.</summary>
    internal int NearestReachableTo(UnitState u, MoveRange range, int goalCol, int goalRow)
    {
        var cost = CostMapFrom(u, goalCol, goalRow);
        int best = -1, bestCost = int.MaxValue, bestOwn = int.MaxValue;
        for (int i = 0; i < range.Cost.Length; i++)
        {
            // 길이 없는 칸은 후보가 아니다 — 원본 0x10059a20 은 0xffff 를 돌려줘 ⑥은 다음 목표로, 다 없으면 제자리에서 쉰다.
            // 전에는 맨해튼 거리로 대신해 벽 쪽으로 걸어갔다(ba-20 J N2).
            if (!range.CanReach(i) || cost == null || cost[i] == int.MaxValue || Part1AiAvoids(u, i % host.Cols, i / host.Cols)) continue;
            int d = cost[i];
            if (d < bestCost || d == bestCost && range.Cost[i] < bestOwn) { bestCost = d; best = i; bestOwn = range.Cost[i]; }
        }
        return best;
    }

    /// <summary>(sc, sr) 에 선다고 쳤을 때 (col, row) 를 겨누면 맞는 인물들.</summary>
    internal List<int> WorkTargetsFrom(WorkData w, UnitState user, int sc, int sr, int col, int row)
    {
        int keepCol = user.Col, keepRow = user.Row;
        user.WarpTo(sc, sr);
        var targets = WorkTargets(w, user, col, row);
        user.WarpTo(keepCol, keepRow);
        return targets;
    }

    /// <summary>그 인물이 쓸 수 있는 work 차례 — 익힌 어빌리티(분류 1·2·4) 다음에 기본공격.</summary>
    internal List<WorkData> AiWorks(UnitState u, bool ignoreCost = false)
    {
        var list = new List<WorkData>();
        if (host._db is not { } db || u.Data is not { } c) return list;
        foreach (var (abilityId, level) in c.Abilities)
        {
            if (!db.Abilities.TryGetValue(abilityId, out var ab) || ab.Category is not (1 or 2 or 4)) continue;
            if (ab.TryWorkAt(level, out int wid) && Work(wid) is { } w && (ignoreCost || CanAfford(u, w))) list.Add(w);
        }
        // 파트1 적은 센 피해 기술부터 — 파트2 처럼 목록 차례 첫 기술을 쓰면 위자드가 화이어 볼 · 필드를 두고 가장 약한 화이어 블레이드만 썼다
        // (사용자 요청: 「공격마법 퍼붓게 해」). 피해 기술이 아닌 것(회복 · 강화 · 상태)은 그 뒤에 원래 차례대로.
        if (host._scene.IsPart1 && IsFoeSide(u))
            list = [.. list.Select((w, i) => (w, i)).OrderByDescending(t => Part1.AiStrength(u, t.w)).ThenBy(t => t.i).Select(t => t.w)];
        if (Work(c.BasicWorkId) is { } basic) list.Add(basic);
        return list;
    }

    /// <summary>
    /// 도망칠 때 쓸 회복기(<c>0x1007b470</c>, ba-20 AI N6) — 배운 어빌리티를 <b>번호 오름차순</b>으로 훑어 분류 1·4 이고 종류가 <b>1(회복)</b>이며
    /// SOUL·HP 가 되고 <b>TP 비용 ≤ 지금 TP</b>(CTP 는 안 더한다)인 첫 work. 전에는 .chr 칸 순서로 종류 1·5 를, TP + CTP 로 봤다.
    /// </summary>
    internal WorkData? FleeHeal(UnitState u)
    {
        if (host._db is not { } db || u.Data is not { } c) return null;
        foreach (var (abilityId, level) in c.Abilities.OrderBy(a => a.Item1))
        {
            if (!db.Abilities.TryGetValue(abilityId, out var ab) || ab.Category is not (1 or 4)) continue;
            if (ab.TryWorkAt(level, out int wid) && Work(wid) is { Kind: 1 } w && CanAfford(u, w) && TpCostFor(u, c, w.Id) <= u.Tp) return w;
        }
        return null;
    }

    /// <summary>파트1 AI 표가 이번 생각에서 보게 한 적(이동 방식 2 의 +0xc 한도 안) — 그 인물의 BestUse 만 거른다. null 이면 거르지 않는다.</summary>
    internal (UnitState Owner, HashSet<int> Seen)? _part1Aware;

    /// <summary>이번 차례에 목표 칸 쪽으로 이미 걸은 파트1 이동 방식 0 · 1 인물 — 다시 생각할 때는 그 자리에서만 친다(원본 남은 이동 +0x1c2 = 0 이면 상태 2).</summary>
    internal UnitState? _part1GoalWalked;

    /// <summary>지금 칸에만 설 수 있는 이동 영역 — 걸은 뒤 그 자리에서 고르는 행동(<c>0x10026b80</c>)에 쓴다.</summary>
    internal MoveRange StayRange(UnitState u)
    {
        int n = host.Cols * host.Rows;
        var cost = new int[n];
        var prev = new int[n];
        Array.Fill(cost, int.MaxValue);
        Array.Fill(prev, -1);
        if ((uint)u.Col < host.Cols && (uint)u.Row < host.Rows) cost[u.Row * host.Cols + u.Col] = 0;
        return new MoveRange(cost, prev, new bool[n]) { Width = host.Cols };
    }

    /// <summary>
    /// 파트1 AI 표 <c>+0x16</c> 먼저 쓸 기술 — <c>+0x14</c> = 0 이고 <c>+0x18</c>% 굴림에 들면 그 work 하나, 아니면 빈 목록(<c>0x1002733e</c>~).
    /// 어빌리티 번호를 그 인물이 쓰는 기술로 바꾼다(<c>0x1003f260</c>): 배워 쓰는 레벨이 있어야 하고(전투 목록 = 쓰는 레벨, <c>0x1009e020</c>),
    /// 갈래 3(초필살기 제어) · 5(소환 능력)는 무기 · 옷이 주는 갈래 2 · 4 기술로. 원본 굴림 칸은 소울 비용을 안 보지만 리메이크는 쓸 수 있는 것(AiWorks)만.
    /// </summary>
    internal IEnumerable<WorkData> Part1PreferredFirst(UnitState u, Part1Ai? ai)
    {
        if (ai is not { PickByScore: 0, PreferredAttack: > 0 } || host._db is not { } db || u.Data is not { } c) yield break;
        if (host._rng.Next(100) >= ai.PreferredChance) yield break;
        int kind = Part1Ability.ById(Part1Tables.Abilities, ai.PreferredAttack)?.Kind ?? -1;
        foreach (var (abilityId, level) in c.Abilities)
        {
            int k = Part1Ability.ById(Part1Tables.Abilities, abilityId - Part1Game.ChrBase)?.Kind ?? -1;   // 리메이크 어빌리티 번호 = 20000 + 파트1 번호
            bool match = abilityId == Part1Game.ChrBase + ai.PreferredAttack || kind == 3 && k == 2 || kind == 5 && k == 4;
            if (!match || !db.Abilities.TryGetValue(abilityId, out var ab) || !ab.TryWorkAt(level, out int wid) || Work(wid) is not { } w) continue;
            if (AiWorks(u).Contains(w)) yield return w;
            yield break;
        }
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"p1ai pref btl {host._scene.Id} chr {u.ChrCode} ability {ai.PreferredAttack} 없음(안 배움 · 지금 직업이 안 다룸)" + Environment.NewLine);
    }

    /// <summary>파트1 AI 가 고른 행동 — work · (설 칸, 겨눌 칸) · 그 이동 영역 · 원본 어림(<see cref="Part1AiEstimate"/>).</summary>
    internal sealed record Part1Pick(WorkData Work, (int Stand, int Col, int Row, int Score) Use, MoveRange Range, int Estimate);

    /// <summary>그 work 을 주는 어빌리티가 Abi 갈래 1 인가 — 파트1 어빌리티(20000 + 번호)만 본다, 파트1 어빌리티가 아니면 참.</summary>
    private bool Part1KindOne(CharacterData c, WorkData w)
    {
        if (host._db is not { } db) return true;
        int from = c.Abilities.Where(a => db.Abilities.TryGetValue(a.Item1, out var ab) && ab.TryWorkAt(a.Item2, out int wid) && wid == w.Id).Select(a => (int)a.Item1).DefaultIfEmpty(-1).First();
        return from < Part1Game.ChrBase || Part1Ability.ById(Part1Tables.Abilities, from - Part1Game.ChrBase)?.Kind == 1;
    }

    /// <summary>
    /// 원본 행동 고르기가 견주는 어빌리티 — 어빌리티 목록(<c>0x1009db90</c>)의 <b>Abi 갈래 1</b>(<c>+0x24</c> = 1 — 무기 · 옷 기술 2 · 4, 초필살기 3, 소환 5, 자세 6 은 빠짐)이고
    /// 비용 <c>Attack +0x4c</c> ≤ 소울 − 50(<c>0x10027488</c> — 소울 50 을 남긴다)인 것. 리메이크는 쓸 수 있는 것(<see cref="AiWorks"/>) 가운데서.
    /// </summary>
    internal List<WorkData> Part1Abilities(UnitState u)
    {
        if (u.Data is not { } c) return [];
        var basic = Work(c.BasicWorkId);
        return [.. AiWorks(u).Where(w => w != basic && Part1KindOne(c, w) && SoulNeedFor(u, c, w.Id) <= u.Soul - 50)];
    }

    /// <summary>원본이 볼 수 있는 것 모두 — <see cref="Part1Abilities"/> 와 기본 공격(<c>+0xb6</c>). 난이도 겨냥(<see cref="SmartPick"/>)이 이 안에서 고른다.</summary>
    internal List<WorkData> Part1ScoreWorks(UnitState u)
    {
        var list = Part1Abilities(u);
        if (u.Data is { } c && Work(c.BasicWorkId) is { } basic && AiWorks(u).Contains(basic)) list.Add(basic);
        return list;
    }

    /// <summary>소환류 — 가장 가까운 적이 10 넘게 멀면 그 자리 어림이 −1(<c>0x10040c46</c>~<c>0x10040d0d</c>, <c>+0xf</c> = 11 갈래만).</summary>
    private static bool Part1NeedsNearFoe(int attackId) =>
        attackId is (>= 0x33 and <= 0x3b) or (>= 0x58 and <= 0x60) or (>= 0x7d and <= 0x85) or (>= 0x6a and <= 0x72) or (>= 0xab and <= 0xb3)
            or (>= 0xc7 and <= 0xcf) or (>= 0xe2 and <= 0xea) or (>= 0xf4 and <= 0xfc) or (>= 0xfd and <= 0x105) or (>= 0xd0 and <= 0xd8);

    /// <summary>
    /// 원본 AI 어림 <c>0x1003ff40(공격, _, x, y, 방향, AI 표 +0x24)</c> — 그 칸을 겨눴을 때의 값, 못 쓰면 −1. 두 번째 인자(0 · 1)는 읽지 않는다.
    /// <list type="bullet">
    /// <item><c>Attack +0x68</c>(대상 편: 1 적만 · 3 적 아닌 것만) = 0 이면 −1(<c>0x1003ff74</c>).</item>
    /// <item>화이어 필드 · 프로즌 아이스는 온도 값(<see cref="Part1TemperatureValue"/>).</item>
    /// <item>겨누기(<c>+0x31</c>) 1 · 3 · 5 = 한 유닛(<c>0x10040833</c>): 그 칸 유닛 v = 값(<see cref="Part1Rules.AiDamage"/>) × 타수(<c>+0x66</c>), 식 5 면 × 명중(<c>+0x48</c>)/100,
    /// + <c>Attack +0x54</c>, 어림 = 20·v·(AC + MR)/200.</item>
    /// <item>그 밖 = 범위(<c>0x100405eb</c>): 범위 유닛마다(편 거르기) v 를 같은 식으로(타수 없이) 더해 × 20, 공격 87 이면 × 100/수, <c>+0x67</c> ≠ 0 이면 × 타수/수.</item>
    /// </list>
    /// 끝으로 150 − 소울 − 비용 &gt; 0 이면 ÷ (150 + 비용 − (소울 − 비용 + 20)/2)(<c>0x1004080b</c> — 소울이 모자랄수록 비싼 기술을 깎는다). 0 이면 −1.
    /// [H] 공격 366 · 367 · 152~160 · 180~188 의 따로 갈래와 대상 거르기 <c>0x1003fd40</c>(+0x247 · 이미 걸린 상태)는 범위 식으로 본다.
    /// </summary>
    internal int Part1AiEstimate(UnitState u, WorkData w, int aimCol, int aimRow, List<int> targets, Part1Ai ai)
    {
        if ((Part1.AttackOf(w) ?? Part1.BasicAttack(u)) is not { } attack) return -1;
        int side = attack.Raw.GetValueOrDefault(0x68);
        if (side == 0) return -1;
        bool resist = ai.ResistAware != 0;
        int bonus = attack.Raw.GetValueOrDefault(0x54);
        long total;
        if (Part1Rules.TemperatureSkill(attack) is not 0 and var heat) total = Part1TemperatureValue(u, heat, targets);
        else if (attack.AimFilter is 1 or 3 or 5)
        {
            if (LiveUnitAt(aimCol, aimRow) is not { } t) return -1;
            bool foe = SeesAsFoe(u, t);
            if (side == 1 && !foe || side == 3 && foe) return -1;
            long v = (long)Part1.AiDamage(u, t, w, resist, foe, host._rng.Next(32768)) * attack.Hits;
            if (attack.Formula == 5) v = v * attack.Accuracy / 100;
            v += bonus;
            total = 20 * v * (Part1.AiAc(t) + Part1.AiMr(t)) / 200;
        }
        else
        {
            if (targets.Count == 0) return -1;
            long sum = 0;
            foreach (int i in targets)
            {
                var t = host._units[i];
                bool foe = SeesAsFoe(u, t);
                if (side == 1 && !foe || side == 3 && foe) continue;
                long v = Part1.AiDamage(u, t, w, resist, foe, host._rng.Next(32768));
                if (attack.Formula == 5) v = v * attack.Accuracy / 100;
                v += bonus;
                sum += v * (Part1.AiAc(t) + Part1.AiMr(t)) / 200;
            }
            total = sum * 20;
            if (attack.Id == 87) total = total * 100 / targets.Count;
            else if (attack.Raw.GetValueOrDefault(0x67) != 0) total = attack.Hits * total / targets.Count;
        }
        int cost = attack.Cost;
        if (150 - u.Soul - cost > 0) total /= 150 + cost - (u.Soul - cost + 20) / 2;
        return total == 0 ? -1 : (int)Math.Clamp(total, int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// <c>+0xf</c> ≠ 11 의 대상 차례 — 사거리 안 유닛(<c>0x10039d30</c>: <c>+0xe</c> 0 적만 · 1 적 아닌 것만 · 그 밖 모두, 칸 줄 → 칸 차례)을
    /// <c>0x100284c0(x, y, z, 목록, 수, +0xf, +0x10)</c> 이 거품 정렬해 맨 앞을 친다. 쓰는 것(AI.dat): 0 거리(<c>0x1002ad40</c> · <c>0x1002ae70</c>), 1 HP(<c>0x1003eb90</c>),
    /// 5 DEX(<c>0x1003ec10</c>), 9 AC(<c>0x1003edc0</c>). <c>+0x10</c> = 1 이면 HP · DEX · AC 는 큰 것부터, 거리는 가까운 것부터 — 0 이면 거꾸로.
    /// 거리 견주기는 원본 그대로 앞 유닛 6|Δy| + 4|Δx|, 뒤 유닛 4(|Δx| + |Δy|) + 2|Δz| 다(두 쪽 식이 다르다). 그 밖의 번호는 자료에 없어 차례를 안 바꾼다.
    /// [H] 리메이크는 이 차례로 칠 수 있는 첫 유닛을 겨눈다(원본은 지금 칸 사거리 안의 맨 앞). <c>+0xe</c> 1 · 2(AI 37)여도 피해 기술은 적만 겨눈다(<see cref="WorthUsing"/>).
    /// </summary>
    internal List<int> Part1TargetOrder(UnitState u, Part1Ai ai)
    {
        var list = new List<int>();
        for (int i = 0; i < host._units.Length; i++)
        {
            var t = host._units[i];
            if (t == u || !t.Alive || !t.OnField) continue;
            bool foe = SeesAsFoe(u, t);
            if (ai.TargetSide == 0 ? foe : ai.TargetSide != 1 || !foe) list.Add(i);
        }
        list.Sort((a, b) => host._units[a].Row != host._units[b].Row ? host._units[a].Row.CompareTo(host._units[b].Row) : host._units[a].Col.CompareTo(host._units[b].Col));
        int z = Part1Z(u);
        bool high = ai.TargetHigh != 0;
        Func<UnitState, int>? key = ai.TargetPick switch
        {
            1 => t => t.Hp,
            5 => t => Part1.AiDex(t),
            9 => t => Part1.AiAc(t),
            _ => null,
        };
        if (ai.TargetPick != 0 && key == null) return list;
        for (int pass = list.Count - 1; pass > 0; pass--)
            for (int k = 0; k < pass; k++)
            {
                UnitState a = host._units[list[k]], b = host._units[list[k + 1]];
                bool swap;
                if (ai.TargetPick == 0)
                {
                    int da = 6 * Math.Abs(u.Row - a.Row) + 4 * Math.Abs(u.Col - a.Col);
                    int db = 4 * (Math.Abs(u.Col - b.Col) + Math.Abs(u.Row - b.Row)) + 2 * Math.Abs(z - Part1Z(b));
                    swap = high ? da > db : da < db;
                }
                else swap = high ? key!(a) < key!(b) : key!(a) > key!(b);
                if (swap) (list[k], list[k + 1]) = (list[k + 1], list[k]);
            }
        return list;
    }

    /// <summary>
    /// 그 work 하나를 원본처럼 잰다 — <paramref name="forced"/>(Chr 먼저)면 그 유닛, <c>+0xf</c> ≠ 11 이면 <see cref="Part1TargetOrder"/> 의 칠 수 있는 첫 유닛,
    /// 11 이면 그 자리에서 가장 좋은 칸(<c>0x10040c20</c> — [H] 리메이크는 BestUse 의 칸)을 겨눠 어림을 낸다. 칠 것이 없으면 null.
    /// </summary>
    private Part1Pick? Part1Eval(int index, UnitState u, Part1Ai ai, WorkData w, MoveRange range, MoveRange? stay, int? forced, int nearest)
    {
        var wr = stay ?? ComputeRange(u, w.Id) ?? range;
        var keep = _part1Aware;
        try
        {
            (int Stand, int Col, int Row, int Score)? Aim(int target)
            {
                if (keep is ({ } owner, { } seen) && owner == u && SeesAsFoe(u, host._units[target]) && !seen.Contains(target)) return null;
                _part1Aware = (u, [target]);
                return BestUse(index, w, wr) is { } x && WorkTargetsFrom(w, u, x.Stand % host.Cols, x.Stand / host.Cols, x.Col, x.Row).Contains(target) ? x : null;
            }
            (int Stand, int Col, int Row, int Score)? use = null;
            if (forced is { } f) use = Aim(f);
            else if (ai.TargetPick != 11)
            {
                if (BestUse(index, w, wr) == null) return null;
                foreach (int t in Part1TargetOrder(u, ai))
                    if (Aim(t) is { } x) { use = x; break; }
            }
            else
            {
                if (Part1NeedsNearFoe((Part1.AttackOf(w) ?? Part1.BasicAttack(u))?.Id ?? 0) && nearest > 10) return null;
                use = BestUse(index, w, wr);
            }
            if (use is not { } got) return null;
            var targets = WorkTargetsFrom(w, u, got.Stand % host.Cols, got.Stand / host.Cols, got.Col, got.Row);
            return new Part1Pick(w, got, wr, Part1AiEstimate(u, w, got.Col, got.Row, targets, ai));
        }
        finally { _part1Aware = keep; }
    }

    /// <summary>
    /// 파트1 행동 고르기 <c>0x10026b80</c>(Chr 먼저 <c>0x10026c4f</c>~, 보통 <c>0x10027328</c>~). 세 갈래(Chr 먼저 · <c>+0xf</c> = 11 · 그 밖) 모두 같은 꼴:
    /// <list type="number">
    /// <item><c>+0x14</c> = 0(굴림): ① <c>rand%100 &lt; +0x18</c> 이면 먼저 쓸 기술 — 어림 ≥ <c>+0x1b</c> 면 쓴다. ② 새로 <c>&lt; +0x19</c> 면 <see cref="Part1Abilities"/> 가운데
    /// 어림이 가장 큰 것(문턱 <c>+0x1b</c> 에서 시작). ③ 못 골랐으면 새로 <c>&lt; +0x1a</c> 면 기본 공격(어림 ≥ 0). 다 못 들면 행동 없음(<c>0x10028008</c>).</item>
    /// <item><c>+0x14</c> ≠ 0(<c>0x10027609</c> · <c>0x10027cc1</c>): 굴림 없이 ②, 이어 기본 공격 어림이 고른 것 이상이거나 아무것도 못 골랐고(문턱 그대로) ≥ 0 이면 기본 공격,
    /// 아니면 고른 어빌리티(<c>0x100277b4</c> · <c>0x10027fa7</c>).</item>
    /// </list>
    /// 어빌리티 견주기: <c>+0xf</c> = 11 과 Chr 먼저는 어림 ≥ 가장 큰 것 − 5 면 받되, 가장 큰 것 + 5 보다 작으면 <c>rand%10 ≤ 가장 큰 것 − 어림 + 10</c> 일 때만(<c>0x100274e6</c>~ — 비슷한 것끼리 섞인다),
    /// 그 밖은 어림 ≥ 가장 큰 것(<c>0x10027b15</c>). 받으면 그 어림이 새 가장 큰 것.
    /// </summary>
    internal Part1Pick? Part1PickAction(int index, UnitState u, Part1Ai ai, MoveRange range, MoveRange? stay, int? forced, int nearest)
    {
        if (u.Data is not { } c) return null;
        bool rolled = ai.PickByScore == 0;
        bool fuzzy = forced != null || ai.TargetPick == 11;
        var log = Trace ? new System.Text.StringBuilder() : null;
        Part1Pick? Done(Part1Pick? p, string why)
        {
            if (log != null)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"p1ai pick btl {host._scene.Id} chr {u.ChrCode} ai {u.Part1AiType} {(forced != null ? "focus" : "normal")} {(rolled ? "roll" : "score")} f {ai.TargetPick}/{ai.TargetHigh} e {ai.TargetSide} soul {u.Soul}{log}"
                    + $" -> {(p != null ? $"work {p.Work.Id} est {p.Estimate} aim ({p.Use.Col},{p.Use.Row})" : "없음")} ({why})" + Environment.NewLine);
            return p;
        }
        if (rolled)
            foreach (var w in Part1PreferredFirst(u, ai))
            {
                var e = Part1Eval(index, u, ai, w, range, stay, forced, nearest);
                log?.Append($" pref {w.Id}:{e?.Estimate.ToString() ?? "-"}");
                if (e is { } pe && pe.Estimate >= ai.MinScore) return Done(pe, "먼저 쓸 기술");
            }
        bool skill = !rolled || host._rng.Next(100) < ai.SkillChance;
        if (rolled) log?.Append($" skill {(skill ? "o" : "x")}({ai.SkillChance}%)");
        Part1Pick? chosen = null;
        int best = ai.MinScore;
        if (skill)
            foreach (var w in Part1Abilities(u))
            {
                if (Part1Eval(index, u, ai, w, range, stay, forced, nearest) is not { } e) { log?.Append($" {w.Id}:-"); continue; }
                int s = e.Estimate;
                bool take = fuzzy ? s >= best - 5 && (s >= best + 5 || host._rng.Next(10) <= best - s + 10) : s >= best;
                log?.Append($" {w.Id}:{s}{(take ? "+" : "")}");
                if (take) (chosen, best) = (e, s);
            }
        var basicWork = Work(c.BasicWorkId) is { } bw && AiWorks(u).Contains(bw) ? bw : null;
        if (rolled)
        {
            if (chosen != null) return Done(chosen, "어빌리티");
            bool basicRoll = host._rng.Next(100) < ai.BasicChance;
            log?.Append($" basic {(basicRoll ? "o" : "x")}({ai.BasicChance}%)");
            if (!basicRoll || basicWork == null) return Done(null, "기본 공격 굴림");
            var eb = Part1Eval(index, u, ai, basicWork, range, stay, forced, nearest);
            log?.Append($" {basicWork.Id}:{eb?.Estimate.ToString() ?? "-"}");
            return Done(eb is { Estimate: >= 0 } ? eb : null, "기본 공격");
        }
        var basic = basicWork != null ? Part1Eval(index, u, ai, basicWork, range, stay, forced, nearest) : null;
        log?.Append($" basic {basicWork?.Id}:{basic?.Estimate.ToString() ?? "-"} best {best}");
        if (basic != null && (basic.Estimate >= best || best == ai.MinScore && basic.Estimate >= 0)) return Done(basic, "기본 공격 ≥ 어빌리티");
        return Done(chosen, chosen != null ? "어빌리티 > 기본 공격" : "칠 것 없음");
    }

    /// <summary>
    /// 그 인물의 파트1 AI 표 칸(<c>Dats\AI.dat</c>, 유닛 <c>+0x262</c>) — 파트1 판이고 AI 가 움직이며 표 번호가 있을 때만. 사람이 움직이는 편(4)·용병 등 번호 없는 인물은 null.
    /// </summary>
    internal Part1Ai? Part1AiOf(UnitState u) =>
        host._scene.IsPart1 && !u.PlayerControlled && u.Part1AiType >= 0 && (_part1Ais ??= Part1Ai.LoadAll()) is { } ais && u.Part1AiType < ais.Count
            ? ais[u.Part1AiType] : null;

    /// <summary>파트1 AI 거리(<c>0x10028e00</c>) — (|Δz| + 2·(|Δx| + |Δy|)) × 2, z 는 유닛 높이(나는 유닛은 나는 높이). AI 표 +0xc 와 견준다.</summary>
    internal int Part1AiDistance(UnitState a, UnitState b) =>
        (Math.Abs(Part1Z(a) - Part1Z(b)) + 2 * (Math.Abs(a.Col - b.Col) + Math.Abs(a.Row - b.Row))) * 2;

    /// <summary>고른 (설 칸, 겨눌 칸)으로 걸어가 work 을 쓴다.</summary>
    internal IEnumerable<bool> AiUseWork(int index, WorkData w, (int Stand, int Col, int Row, int Score) use, MoveRange range)
    {
        var u = host._units[index];
        var path = PathWithin(range, u.Col, u.Row, use.Stand) ?? [];
        var aimed = LiveUnitAt(use.Col, use.Row);
        int targetIndex = w.TargetMode is 1 or 4 or 5 && aimed != null ? Array.IndexOf(host._units, aimed) : -1;
        var routine = UseWorkRoutine(index, w, targetIndex, use.Col, use.Row, path);
        while (routine.MoveNext()) yield return true;
    }

    /// <summary>
    /// AI 차례 — 명령 하나를 만들어 실행한 뒤 <b>TP 가 남으면 처음부터 다시 생각한다</b>(원본 0x1006db20 은 지금 유닛부터 다시 훑어
    /// TP&gt;0 이면 같은 유닛을 또 뽑는다, ba-2·ba-11). 기본공격 80 이면 TP 155 인 적이 두 번 친다. 전에는 한 번 쓰고 곧장 쉬어
    /// 적의 손 수가 원본의 절반쯤이었고 남은 TP 로 회복까지 받았다(fg-21 ①). 아무것도 못 하거나 TP 가 다하면 쉰다(WAITNEXT 자동 휴식).
    /// </summary>
    internal IEnumerator<bool> AiRoutine(int index)
    {
        var u = host._units[index];
        _part1GoalWalked = null;                             // 파트1 이동 방식 0 · 1 은 차례마다 한 번 걷는다(원본 남은 이동 +0x1c2)
        for (double end = host._lastTime + 0.4; host._lastTime < end;) yield return true;
        for (int think = 0; think < 8; think++)
        {
            bool acted = false;
            foreach (bool r in AiThink(index, a => acted = a)) yield return r;
            if (!acted || _turn != index || _outcome.Length > 0 || !u.Alive || !u.OnField) break;
            CommitMove(u);                                   // 걸은 비용을 빼야 남은 TP 로 다시 생각할 수 있다
            if (u.Tp <= 0) break;
            // 파트1 은 한 차례 = 한 번 걷고 걸은 자리에서 한 번 행동(0x10028330 · 남은 행동 +0x1c4, 분석-파트1-전투 「차례 한 번」) — TP 가 남아도 기술 · 공격을 한 번 쓰면 끝.
            // 전에는 파트2 식으로 다시 생각해 기본 공격(TP 80)을 하고 남은 TP 로 한 번 더 쳤다(사용자 보고 fg-52: Btl 20229 솔져가 3연사를 두 번).
            if (host._scene.IsPart1 && _part1ActedBy == u) break;
            for (double end = host._lastTime + 0.3; host._lastTime < end;) yield return true;
            // 다시 생각하기 전에 판의 모든 유닛이 설 때까지 — 상태 15 갈래 1 이 0x1006e320(모든 유닛 +0x80 == 0)을 기다린 뒤 GETNEXT(0x1006a241).
            // 이동만 한 군단 대장의 부하가 진형 자리로 다 걸어간 뒤에 다음 공격을 해야 부하가 모두 낀다(감사5 L-A). 멈춤 대비 5초 상한.
            for (double end = host._lastTime + 5; host._lastTime < end && host._units.Any(x => x.Alive && x.OnField && x.IsBusy);) yield return true;
        }
        if (_turn == index && _outcome.Length == 0 && u.Alive && u.OnField) Rest(index);
    }

    /// <summary>한 번 생각하기 — 분석-전투 ba-11 의 여섯 단계. 기술을 쓰거나 걸었으면 <paramref name="report"/>(true).</summary>
    internal IEnumerable<bool> AiThink(int index, Action<bool> report)
    {
        var u = host._units[index];
        if (host._db is not { } db || ComputeRange(u) is not { } range) yield break;

        // 1단계 깨어남 — 조건을 못 채우면 그 자리에서 쉰다(0x1005baa8). 깬 그 차례에 바로 움직인다.
        if (!u.Awake)
        {
            if (!WakesNow(u))
            {
                if (Trace)
                    System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                        $"ai asleep: unit {index} chr {u.ChrCode} ({u.Col},{u.Row}) wake {u.WakeCondition}:{u.WakeValue}" + Environment.NewLine);
                yield break;
            }
            u.Awake = true;
        }

        int hpPercent = u.MaxHp == 0 ? 100 : u.Hp * 100 / u.MaxHp;
        // 버서커(4)가 걸린 인물에게는 자기 말고 모두가 적이다.
        var enemies = host._units.Where(t => t.Alive && t.OnField && SeesAsFoe(u, t)).ToList();
        // 가까운 적 거리에는 높이 차의 절반도 든다(0x1007b0f0: |Δx| + |Δy| + |Δ높이|/2, ba-20 AI N7).
        int nearest = enemies.Count == 0 ? 99 : enemies.Min(t => Math.Abs(t.Col - u.Col) + Math.Abs(t.Row - u.Row) + Math.Abs(HeightAt(t.Col, t.Row) - HeightAt(u.Col, u.Row)) / 2);

        // 파트1 AI 표(Dats\AI.dat, 유닛 +0x262) — 파트2 AI 틀은 그대로 두고 표가 정한 한도만 얹는다(분석-파트1-전투 33절).
        var p1ai = Part1AiOf(u);
        _part1Aware = null;
        bool p1Blind = false;
        // 이동 방식 0 · 1(0x10026570) — 0 은 정한 칸(+6, +8), 1 은 Chr +0xa 인 유닛의 칸(유닛 배열을 끝까지 훑어 마지막으로 맞는 것, 0x100265c4~0x1002664f).
        // 1 은 그 유닛이 없으면 2(보통)와 같다. 두 방식 모두 적을 찾지 않는다(0x10026694 → 0x100269cd).
        (int Col, int Row)? p1Goal = p1ai switch
        {
            { MoveMode: 0 } => (p1ai.GoalX, p1ai.GoalY),
            { MoveMode: 1 } when host._units.LastOrDefault(t => t.Alive && t.OnField && t.ChrCode == Part1Game.ChrBase + p1ai.FollowChr) is { } lead => (lead.Col, lead.Row),
            _ => null,
        };
        if (p1ai is { MoveMode: 1 or 2 } && p1Goal == null)
        {
            // 이동 방식 2(보통)는 대상 찾기(0x100288c0)를 「AI 표 +0xc 거리」 안으로 자른다 — 거리 = (|Δz| + 2·(|Δx| + |Δy|)) × 2(0x10028e00, 걸음 비용과 같은 자).
            // 원본은 칠 대상을 min(사거리 + SPD, +0xc), 다가갈 대상을 +0xc 안에서만 찾는다. 리메이크는 칠 대상을 파트2 틀(걸어서 닿는 칸)대로 두고 +0xc 만 얹는다 —
            // 100(25칸)이 대부분이라 사실상 그대로이고, 36(AI 26) · 44(AI 40)는 9 · 11칸 밖의 적을 못 본다.
            var aware = enemies.Where(t => Part1AiDistance(u, t) <= p1ai.MoveCap).ToList();
            p1Blind = aware.Count == 0;                      // 볼 적이 없으면 (도망 판단 뒤에) 아무것도 안 하고 쉰다(0x100269de → 0x10026b57)
            enemies = aware;
            _part1Aware = (u, [.. aware.Select(t => Array.IndexOf(host._units, t))]);
        }

        // 2단계 도망(0x1005b4e0) — 피가 적고 적이 가까우면 물러난다. 위험도 0.70 이하인 칸 중 <b>지금 자리에서 맨해튼으로 가장 가까운 칸</b>,
        // 없으면 위험도가 가장 작은 칸. 지금 칸이 이미 0.70 이하면 안 움직인다. 부대장이 아니고 회복기가 있으면 물러난 자리에서 회복기를 쓴다.
        // 파트1 표 인물은 문턱을 표가 정한다(0x100263a0): +0x1c 1·2·3 = HP ≤ 최대 × 8 · 5 · 2 / 10, 4 = 안 함, 0 과 그 밖은 늘 위험. 칸 10 이 2 인 인물은 늘 아님.
        // 위험하면 가장 가까운 적이 10 안(|Δx| + |Δy| + |Δ높이|/2, 0x1003f6d0 · 0x1003fa22)일 때 물러나고, 아니면 치지 않고 쉰다 — 위험 갈래(상태 3 · 0)에는 공격이 없다.
        bool p1Danger = p1ai != null && Part1.Fighter(u)?.Chr.Fields[Part1Chr.KindField] != 2
                        && p1ai.FleeThreshold switch { 4 => false, 1 or 2 or 3 => u.Hp <= p1ai.FleeHp(u.MaxHp), _ => true };
        if (Trace && host._scene.IsPart1)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"p1ai btl {host._scene.Id} unit {index} chr {u.ChrCode} ai {u.Part1AiType} mode {p1ai?.MoveMode} cap {p1ai?.MoveCap} foes {enemies.Count} nearest {nearest} danger {p1Danger}"
                + $" fly {Part1Flyer(u) != null} reach {range.Cost.Count(x => x != int.MaxValue)} seen {_part1Aware?.Seen.Count} blind {p1Blind}"
                + (p1ai != null ? $" goal {(p1Goal is { } g0 ? $"({g0.Col},{g0.Row})" : "-")} act {p1ai.TargetMode} pref {p1ai.PreferredAttack}" : "") + Environment.NewLine);
        if (p1ai != null && p1Danger && nearest > 10) yield break;
        if (p1ai != null ? p1Danger : hpPercent < db.N(66) && nearest < db.N(90))
        {
            var heal = FollowersOf(index).Count == 0 ? FleeHeal(u) : null;
            // 「움직이지 않음」(+0 = 3)도 도망은 걷는다 — 도망 이동(0x10026470)은 이동 방식을 안 본다.
            var fleeRange = (heal != null ? ComputeRange(u, heal.Id, flee: true) : p1ai is { MoveMode: 3 } ? ComputeRange(u, flee: true) : null) ?? range;
            int here = u.Row * host.Cols + u.Col, safest = here;
            if (FleeDanger(u, u.Col, u.Row) > SafeDanger)
            {
                int bestDist = int.MaxValue;
                double bestDanger = double.MaxValue;
                int leastDangerous = here;
                for (int i = 0; i < fleeRange.Cost.Length; i++)
                {
                    if (!fleeRange.CanReach(i) || i != here && Part1AiAvoids(u, i % host.Cols, i / host.Cols)) continue;
                    double danger = FleeDanger(u, i % host.Cols, i / host.Cols);
                    if (danger < bestDanger) { bestDanger = danger; leastDangerous = i; }
                    if (danger > SafeDanger) continue;
                    int d = Math.Abs(i % host.Cols - u.Col) + Math.Abs(i / host.Cols - u.Row);
                    if (d < bestDist) { bestDist = d; safest = i; }
                }
                if (bestDist == int.MaxValue) safest = leastDangerous;
            }
            if (safest != here) foreach (var r in WalkTo(u, fleeRange, safest)) yield return r;
            if (heal != null && CanAfford(u, heal))
            {
                CommitMove(u);
                var use = UseWorkRoutine(index, heal, index, u.Col, u.Row, []);
                while (use.MoveNext()) yield return true;
                report(true);                                // 이동 + 회복기뿐이라 TP 가 남으면 다시 생각한다(0x1005bcd6, ba-20 J N5)
            }
            yield break;                                     // 도망만 했으면 쉰다(원본 0x2716)
        }
        if (p1Blind) yield break;

        // 파트1 이동 방식 0 · 1 — 차례의 이동(상태 1, 0x10026330: 남은 이동 +0x1c2)은 목표 칸으로만 걷고, 행동(상태 2 · 0x10026b80)은 걸은 뒤 그 자리에서 한다.
        // 목표 칸이 전장 밖이거나 벽(비트 1, 나는 유닛은 비트 2)이면 0x10025ce0 → 0x1008a250 이 0xffff 라 걷지 않고 제자리에서 행동한다.
        // 갈 칸은 그 칸에서 목표까지 A* 비용이 가장 작은 칸(같으면 먼저 훑은 칸, 0x10026ac5) — 리메이크는 NearestReachableTo(목표에서 거꾸로 잰 비용).
        bool p1InPlace = false;
        if (p1Goal is { } p1g)
        {
            p1InPlace = true;
            if (_part1GoalWalked != u)
            {
                _part1GoalWalked = u;
                bool goalOk = (uint)p1g.Col < host.Cols && (uint)p1g.Row < host.Rows && Part1Range.Contains(p1g.Col, p1g.Row)
                              && (CellFlagsAt(p1g.Col, p1g.Row) & (Part1Flyer(u) != null ? 0x2 : 0x1)) == 0;   // [H] 원본 지형 비트 1 · 2 ↔ 리메이크 칸 플래그
                int go = goalOk ? NearestReachableTo(u, range, p1g.Col, p1g.Row) : -1;
                if (Trace)
                    System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                        $"p1ai goal btl {host._scene.Id} unit {index} chr {u.ChrCode} mode {p1ai!.MoveMode} ({u.Col},{u.Row}) -> goal ({p1g.Col},{p1g.Row}) ok {goalOk} flags {CellFlagsAt(p1g.Col, p1g.Row)} go {(go >= 0 ? $"({go % host.Cols},{go / host.Cols})" : "-")} act {p1ai.TargetMode}" + Environment.NewLine);
                if (go >= 0 && range.Cost[go] > 0)
                {
                    foreach (var r in WalkTo(u, range, go)) yield return r;
                    CommitMove(u);
                }
            }
        }

        // 파트1 AI 표 +0xd = 2 — 행동 고르기가 곧장 끝난다(0x10026bc4 → 0x10028008): 엔진 0 번(이동 방식 3)은 서 있기만, AI 24 는 도망만, 38 · 39 · 42 는 걷기만 한다.
        if (p1ai is { TargetMode: 2 }) yield break;

        // 걸은 뒤(이동 방식 0 · 1)에는 그 자리에서만 친다 — 설 칸이 지금 칸뿐인 영역.
        MoveRange? stay = p1InPlace ? StayRange(u) : null;

        // 파트1 AI 표가 있으면 행동 고르기 0x10026b80 을 그대로 — 먼저 쓸 기술 · 어빌리티 · 기본 공격을 원본 어림(0x1003ff40)으로 고른다(Part1PickAction).
        // 못 고르면 원본은 남은 이동으로 이동 고르기를 한다(0x1002842d) — 리메이크는 아래 다가가기(4단계 B · 6단계)가 맡는다.
        if (p1ai != null)
        {
            Part1Pick? pick = null;
            // 어려움 이상(리메이크 난이도)은 쓸 수 있는 것(갈래 1 · 소울 − 50 · 기본 공격) 가운데 가장 많이 깎는 것.
            if (stay == null && SmartAi(u) && SmartPick(index, range, Part1ScoreWorks(u)) is { } smart)
                pick = new(smart.Work, smart.Use, ComputeRange(u, smart.Work.Id) ?? range, smart.Use.Score);
            // +0xd = 0 — Chr +0x12 인 유닛을 먼저 친다(0x10026bdd~: 배열 끝까지 훑어 마지막으로 맞는 것). 못 고르면 보통(+0xd = 1)으로(0x10027322).
            // [H] 원본은 그 유닛이 같은 편이어도 「칠 수 있나」(0x1003fbd0)만 본다 — 리메이크는 적일 때만 겨눈다.
            if (pick == null && p1ai is { TargetMode: 0 } && p1ai.TargetChr != 0xFFFF
                && host._units.LastOrDefault(t => t.Alive && t.OnField && t.ChrCode == Part1Game.ChrBase + p1ai.TargetChr) is { } focus && SeesAsFoe(u, focus))
                pick = Part1PickAction(index, u, p1ai, range, stay, Array.IndexOf(host._units, focus), nearest);
            pick ??= Part1PickAction(index, u, p1ai, range, stay, null, nearest);
            if (pick != null)
            {
                report(true);
                foreach (bool r in AiUseWork(index, pick.Work, pick.Use, pick.Range)) yield return r;
                yield break;
            }
        }
        else
        {
            // 3단계 자가 회복 → 4단계 공격. 원본은 <b>회복기만 한 바퀴 돌고, 못 쓰면 전부 한 바퀴</b> 돈다 —
            // 예전처럼 「피가 적으면 회복기 아닌 것을 건너뛴다」로 하면 회복기가 없는 인물이 공격까지 통째로 걸렀다.
            for (int pass = hpPercent < db.N(70) ? 0 : 1; pass < 2; pass++)
            {
                // 어려움 이상의 적은 첫 work 대신 피해 기술 가운데 가장 많이 깎는 것을 쓴다(Difficulty.cs).
                if (pass == 1 && stay == null && SmartAi(u) && SmartPick(index, range) is { } smart)
                {
                    report(true);
                    foreach (bool r in AiUseWork(index, smart.Work, smart.Use, ComputeRange(u, smart.Work.Id) ?? range)) yield return r;
                    yield break;
                }
                foreach (var w in AiWorks(u))
                {
                    if (pass == 0 && !w.IsHeal) continue;
                    // 이동 예산은 <b>그 기술</b>의 TP 비용을 뺀 만큼이다(0x1005d070) — 늘 기본공격 기준이면 비싼 기술도 너무 멀리 걸어가 쓴다.
                    var workRange = stay ?? ComputeRange(u, w.Id) ?? range;
                    if (BestUse(index, w, workRange) is not { } use) continue;
                    report(true);
                    foreach (bool r in AiUseWork(index, w, use, workRange)) yield return r;
                    yield break;
                }
            }
        }
        // 이동 방식 0 · 1 은 걸은 뒤 칠 것이 없으면 쉰다 — 다가가기(4단계 B · 6단계)는 목표 칸 걷기가 대신했다.
        if (p1InPlace) yield break;

        // 4단계 (B) 접근(0x1005e39b) — 지금은 못 치지만 <b>최대 TP</b> 로는 닿는 적이 있으면, 목록 차례로 첫 성공 기술이 겨눌 칸을 향해
        // 지금 TP 로 갈 수 있는 칸 중 경로 비용이 가장 짧은 칸으로 가서 쉰다. 이 단계는 5단계 휴식보다 앞이다(fg-21 ⑮).
        if (u.Data is { } cd && Work(cd.BasicWorkId) is { } basicWork && ComputeRange(u, basicWork.Id, tp: u.MaxTp) is { } fullRange
            && BestUse(index, basicWork, fullRange, approach: true) != null)
        {
            foreach (var w in AiWorks(u, ignoreCost: true))
            {
                var wRange = ComputeRange(u, w.Id, tp: u.MaxTp) ?? fullRange;
                if (BestUse(index, w, wRange, approach: true) is not { } use) continue;
                // 파트1 은 최소 사거리가 있어(칼 5 = 2칸 · 총 13 = 4칸, Attack +0x28) 겨눌 칸 쪽으로만 가면 너무 붙는다 — 그 칸을 칠 수 있는 설 칸 쪽으로 간다.
                int go = host._scene.IsPart1 ? NearestReachableTo(u, range, use.Stand % host.Cols, use.Stand / host.Cols) : NearestReachableTo(u, range, use.Col, use.Row);
                if (go < 0) continue;                        // 길이 없으면 다음 기술(0x1005cb40, ba-20 J N2)
                if (range.Cost[go] > 0) foreach (var r in WalkTo(u, range, go)) yield return r;
                yield break;                                 // 접근 뒤에는 쉰다(0x2716)
            }
        }

        // 5단계 휴식 — 피가 Num[71]% 이하면 움직이지 않고 그 자리에서 쉰다.
        if (hpPercent <= db.N(71)) yield break;
        if (Trace)
        {
            var goals = MoveGoals(u, enemies).ToList();
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"ai no attack: unit {index} chr {u.ChrCode} side {u.Side} ({u.Col},{u.Row}) TP {u.Tp} works [{string.Join(",", AiWorks(u).Select(x => $"{x.Id} 사거리 {x.RangeKind}:{x.RangeMinQuarters}~{RangeMaxOf(x, u)} 모양 {x.RangeShape}/{x.AreaShape}"))}] enemies {enemies.Count} nearest {nearest} goals {goals.Count}"
                + (goals.Count > 0 ? $" first ({goals[0].Col},{goals[0].Row}) reach {NearestReachableTo(u, range, goals[0].Col, goals[0].Row)}" : "") + Environment.NewLine);
        }

        // 6단계 — 칠 수 없으면 목표 쪽으로 다가가 쉰다. 목표 차례는 <b>이동 방식</b>이 정한다(0x1005c460).
        foreach (var goal in MoveGoals(u, enemies))
        {
            // 목표까지 <b>경로 비용</b>이 가장 짧은 칸(0x10059a20). 갈 칸이 나오는 <b>첫 목표에서 멈춘다</b> — 그 칸이 제자리면 쉰다.
            int best = NearestReachableTo(u, range, goal.Col, goal.Row);
            if (best < 0) continue;
            if (range.Cost[best] > 0)
            {
                // 이동은 명령 0x2710 하나뿐이다 — 걸은 뒤 TP 가 남으면 <b>처음부터 다시 생각</b>해 공격할 수 있다(0x1005c42b·0x100688ae, ba-14 A1).
                foreach (var r in WalkTo(u, range, best)) yield return r;
                report(true);
            }
            break;                                       // 제자리가 최선이면 쉰다
        }
        for (double end = host._lastTime + 0.2; host._lastTime < end;) yield return true;
    }

    internal IEnumerable<bool> WalkTo(UnitState u, MoveRange range, int cell)
    {
        var steps = range.PathTo(cell).ToList();
        // 차례 시작에 비추지 않은 먼 적(fg-86) — 정말 걸을 때 그 자리를 비춘다.
        if (_camDeferredFor == u && steps.Count > 0)
        {
            _camDeferredFor = null;
            if (Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"{host._lastTime:F2} camera deferred → walk: chr {u.ChrCode} ({u.Col},{u.Row}) {steps.Count}칸" + Environment.NewLine);
            foreach (bool b in CenterUnitAndWait(u)) yield return b;
        }
        foreach (var step in steps) u.Path.Enqueue(step);
        while (u.IsBusy) yield return true;
    }
}
