using WarOfGenesis.Assets;

namespace DuelDx;

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
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>C 정수 나눗셈(0 으로 나누면 0).</summary>
    private static int CDiv(int a, int b) => b == 0 ? 0 : a / b;

    /// <summary>
    /// 이동 방식이 정하는 <b>목표 차례</b>(<c>0x1005c460</c>) — 앞에서부터 갈 칸이 나오는 첫 목표를 쓴다.
    /// </summary>
    /// <remarks>
    /// <c>0</c> 가깝고 센 적 · <c>1</c> <b>내 편이 몰아붙이고 있는</b> 적 · <c>2</c> 먼 적 ·
    /// <c>3</c> 가깝고 센 아군 곁 · <c>4</c> 멀고 약한 아군 곁 · <c>5</c> 위험한 아군 곁.
    /// 그 밖(6 이상·음수)은 목록이 비어 제자리에서 쉰다. 시판 자료는 <b>0·1·2·3 만</b> 쓴다(2154·2·9·1명).
    /// <para>동점이면 <c>Btl</c> 줄 순서를 지킨다 — 원본이 인접 맞바꿈으로 고르기 때문이다.</para>
    /// </remarks>
    private List<UnitState> MoveGoals(UnitState u, List<UnitState> enemies)
    {
        if (_db is not { } db) return [];
        int Reach(UnitState t) => 4 + Math.Abs(t.Col - u.Col) + Math.Abs(t.Row - u.Row);
        var friends = _units.Where(t => t.Alive && t != u && !SeesAsFoe(u, t)).ToList();

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
    private List<UnitState> FarSortLikeOriginal(UnitState u, List<UnitState> enemies)
    {
        var list = enemies.OrderBy(t => Array.IndexOf(_units, t)).Take(100).ToList();
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
    private bool WakesNow(UnitState u)
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
            var found = _units.Where(pick).Select(WakeDistance).ToList();
            return found.Count == 0 ? 1000 : found.Min();
        }
    }

    /// <summary>세력 점수 — AI 가 "이 인물이 얼마나 센가" 를 재는 값.</summary>
    private int Power(UnitState u)
    {
        if (_db is not { } db || u.Data is not { } c) return 0;
        int v = db.Acr(c, u.Tp) * db.Rdp(c, u.Hp, u.MaxHp) * db.Atk(c, u.Soul);
        v = CDiv(CDiv(CDiv(v, db.N(61)), db.N(63)), db.N(64));
        return CDiv(u.Hp + u.MaxHp, db.N(62)) * v;
    }

    /// <summary>
    /// 그 칸이 받는 영향력 — <paramref name="viewer"/> 눈에 적(<paramref name="foes"/>)인, 또는 한편인 살아 있는 인물들의 세력을 거리로 나눠 더한다.
    /// 편은 세력 행렬로 가른다(편 0·1·2 는 서로 적). <paramref name="viewer"/> 자신은 넣지 않는다(원본 지도 <c>0x1005b5e0</c> 자기포함=0).
    /// </summary>
    private int Influence(int col, int row, UnitState viewer, bool foes)
    {
        int num65 = _db?.N(65) ?? 6, sum = 0;
        foreach (var u in _units)
        {
            if (!u.Alive || !u.OnField || u == viewer || Hostile(viewer, u) != foes) continue;
            int d = Math.Abs(u.Col - col) + Math.Abs(u.Row - row);
            sum += CDiv(Power(u) * num65, d + num65);
        }
        return sum;
    }

    /// <summary>그 칸의 위험도(기준 16·17, 이동 방식 1·5) — 적 영향력 / 아군 영향력(자기 몫은 안 넣는다 — 전에는 두 번 넣었다).</summary>
    private double Danger(UnitState u, int col, int row)
    {
        int enemies = Influence(col, row, u, foes: true);
        // 지도에는 제 번짐이 한 번 들어 있다(0x1005b5e0(t, 0) — 기준 16·17 0x1005c976, 이동 방식 1·5 0x100612b0, ba-20 J N4).
        int friends = Influence(col, row, u, foes: false)
                      + CDiv(Power(u) * (_db?.N(65) ?? 6), Math.Abs(u.Col - col) + Math.Abs(u.Row - row) + (_db?.N(65) ?? 6));
        return friends == 0 ? enemies : (double)enemies / friends;
    }

    /// <summary>
    /// 도망이 재는 위험도(<c>0x1005b4e0</c>) — 제 번짐을 지도에서 뺀 뒤 적 영향력 / (제 세력 점수 + 남은 아군 영향력).
    /// 자기 몫이 모든 칸에서 같아야 「어디로 가면 안전한가」만 남는다.
    /// </summary>
    private double FleeDanger(UnitState u, int col, int row)
    {
        int enemies = Influence(col, row, u, foes: true);
        int friends = Power(u) + Influence(col, row, u, foes: false);
        return friends <= 0 ? enemies : (double)enemies / friends;
    }

    /// <summary>도망의 안전 문턱 — 위험도 이 값 이하인 칸이면 된다(원본 절댓값 0.70).</summary>
    private const double SafeDanger = 0.70;

    /// <summary>work <c>+0x3e</c> 기준으로 그 대상들이 얼마나 좋은지 — 짝수면 최댓값, 홀수면 1000000 − 최솟값.</summary>
    /// <param name="plain">난이도 겨냥(SmartAi)을 끄고 원본 칸 값만 — 사건 207 필살기의 겨냥은 원본 그대로(0x1005d860, 감사5 B6).</param>
    private int TargetValue(UnitState user, WorkData w, List<int> targets, bool plain = false)
    {
        if (targets.Count == 0) return 0;
        if (!plain && w.IsDamage && SmartAi(user)) return SmartValue(user, w, targets);   // 어려움 이상: 가장 많이 깎는 곳(Difficulty.cs)
        int criterion = w.AiCriterion >> 1;
        bool wantMax = (w.AiCriterion & 1) == 0;
        int best = wantMax ? int.MinValue : int.MaxValue;
        foreach (int i in targets)
        {
            var t = _units[i];
            int v = criterion switch
            {
                0 => t.Hp,
                1 => t.Soul,
                2 => _db is { } db && t.Data is { } c ? db.Atk(c, t.Soul) : 0,
                3 => t.Tp,
                4 => _db is { } db2 && t.Data is { } c2 ? db2.Rdp(c2, t.Hp, t.MaxHp) : 0,
                5 => _db is { } db3 && t.Data is { } c3 ? db3.Acr(c3, t.Tp) : 0,
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
    private bool WorthUsing(UnitState user, WorkData w, List<int> targets)
    {
        int need = w.MinTargets + 1;
        if (w.AbilityId == CureAbility) need = 1;   // 큐어는 한 명에게만 간다(WorkTargets) — 최소 대상 수를 못 채워 영영 안 쓰는 일이 없게
        // 블랙홀은 화면 HP 가 위력보다 적은 유닛만 쓰러뜨린다 — 쓰러질 적이 쓰러질 제 편(시전자 포함)보다 많을 때만 쓴다(데모 판단, 원본 AI 는 대상 수만 본다).
        if (w.AbilityId == BlackHoleAbility)
        {
            var doomed = targets.Select(i => _units[i]).Where(t => t.Hp < w.Power).ToList();
            return doomed.Count(t => Hostile(user, t)) > doomed.Count(t => !Hostile(user, t));
        }
        if (w.IsHeal)
        {
            int lost = targets.Sum(i => _units[i].MaxHp == 0 ? 0 : (_units[i].MaxHp - _units[i].Hp) * 100 / _units[i].MaxHp);
            return lost > (_db?.N(79) ?? 30) * need;
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
                var t = _units[i];
                int a = 0;
                for (int k = 0; k < 3; k++) a += AilmentScore(t.StatusId[k], t.StatusValue[k]);
                if (w.AiTargetSide == 1 ? a > b : w.AiTargetSide == 4 && a < b) count++;
            }
            return count >= need;
        }
        return targets.Count >= need;
    }

    /// <summary>상태 점수표 <c>0x1005c4e0</c> — 해로운 것 −1, 이로운 것 +1, 값의 부호로 갈리는 것(13·14·21·29 는 양수가 이득, 18·20·38 은 음수가 이득), 나머지 0.</summary>
    private static int AilmentScore(int id, int value) => id switch
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
    private (int Stand, int Col, int Row, int Score)? BestUse(int unitIndex, WorkData w, MoveRange range, bool approach = false,
                                                              (int Col, int Row)? anchor = null, HashSet<int>? taken = null)
    {
        var user = _units[unitIndex];
        int num74 = _db?.N(74) ?? 4;
        // 효과 모양이 0 인 work(이스케이프·혼 등 387개)는 칠할 칸이 없어 대상 0 → 값 0 → AI 가 절대 안 고른다(0x100704f0 → 0x1005d4d7, ba-14 A5).
        if (w.AreaShape == 0) return null;

        // 겨눌 칸 후보는 <b>갈 수 있는 칸에서 사거리에 드는 칸</b>뿐이다(층 12, 0x100749b0) — 맵 전체를 채점하면 못 닿는 1등 칸 때문에
        // 닿는 다른 적이 있어도 기술을 통째로 버렸다(fg-21 ⑮). 대상 방식 6(방향기)은 제자리 네 이웃 칸만 본다.
        var stands = new List<int>();
        var candidates = new HashSet<(int Col, int Row)>();
        int here = user.Row * Cols + user.Col;
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
            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
                if ((uint)(user.Col + dx) < Cols && (uint)(user.Row + dy) < Rows) candidates.Add((user.Col + dx, user.Row + dy));
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
                if (!range.CanReach(stand) || (stayPut && stand != here)) continue;
                stands.Add(stand);
                int sc = stand % Cols, sr = stand / Cols;
                for (int ay = Math.Max(0, sr - reach); ay <= Math.Min(Rows - 1, sr + reach); ay++)
                    for (int ax = Math.Max(0, sc - reach); ax <= Math.Min(Cols - 1, sc + reach); ax++)
                        if (!candidates.Contains((ax, ay)) && InWorkRange(w, sc, sr, ax, ay, user)) candidates.Add((ax, ay));
            }
        }

        // ① 겨눌 칸 — 점수는 지금 서 있는 자리에서 잰다.
        (int Col, int Row, int Score)? aim = null;
        bool aimIsObject = false;
        foreach (var (col, row) in candidates)
        {
            var targets = WorkTargets(w, user, col, row);
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
            int sc = stand % Cols, sr = stand / Cols;
            if (taken != null && stand != here && taken.Contains(stand)) continue;
            if (!InWorkRange(w, sc, sr, pick.Col, pick.Row, user)) continue;
            if (WorkTargetsFrom(w, user, sc, sr, pick.Col, pick.Row) is not { Count: > 0 } && AiObjectAt(w, user, pick.Col, pick.Row) == null) continue;
            int d = Math.Abs(sc - user.Col) + Math.Abs(sr - user.Row);
            if (d < bestDist) { bestDist = d; bestStand = stand; }
        }
        return bestStand < 0 ? null : (bestStand, pick.Col, pick.Row, pick.Score);
    }

    /// <summary>AI 가 그 칸에서 때릴 수 있는 물체 — 피해 기술(대상 방식 1·5, 최소 대상 1)이고 부술 수 있는 적대 물체일 때.</summary>
    private DemoObject? AiObjectAt(WorkData w, UnitState user, int col, int row) =>
        // 기본공격으로만 친다 — 전용 연출 갈래를 타는 기술은 물체 피해 고리를 안 지나 헛손질이 된다. 종류는 바리케이트(7)·포탑(9)·크리스탈(10)만
        // (상자·폭탄 상자는 표 값이 1 이라 못 때린다, 0x1006fe40).
        w.IsDamage && w.TargetMode is 1 or 5 && w.MinTargets == 0 && user.Data?.BasicWorkId == w.Id
        && ObjectAt(col, row) is { Alive: true, Data.Breakable: true, Data.Kind: 7 or 9 or 10 } obj && ObjectHostile(obj, user) && !_opened.Contains(obj) ? obj : null;

    /// <summary>물체의 칸 값(0x1005c8fc 물체 가지) — 기준 0/1 HP, 4/5 공격력, 12/13 잃은 HP, 그 밖 0. 짝수는 최댓값, 홀수는 1000000 − 최솟값.</summary>
    private int AiObjectValue(WorkData w, DemoObject obj)
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
    private int[]? CostMapFrom(UnitState u, int col, int row)
    {
        if (_map is not { } map || _db is not { } db || u.Data is not { } c) return null;
        bool InBounds(int x, int y) => (uint)x < Cols && (uint)y < Rows && x < map.Cols && y < map.Rows;
        if (!InBounds(col, row)) return null;
        int dex = Math.Max(1, db.Dex(CombatData(u) ?? c)), num5 = db.N(5);
        var costs = new int[Cols * Rows];
        Array.Fill(costs, int.MaxValue);
        costs[row * Cols + col] = 0;
        var queue = new PriorityQueue<(int Col, int Row), int>();
        queue.Enqueue((col, row), 0);
        (int Dx, int Dy)[] dirs = [(0, -1), (1, 0), (0, 1), (-1, 0)];
        while (queue.TryDequeue(out var cell, out int cost))
        {
            if (cost > costs[cell.Row * Cols + cell.Col]) continue;
            int h = WalkHeightAt(cell.Col, cell.Row);
            foreach (var (dx, dy) in dirs)
            {
                int nx = cell.Col + dx, ny = cell.Row + dy;
                if (!InBounds(nx, ny) || (CellFlagsAt(nx, ny) & 0x9) != 0) continue;
                int nh = WalkHeightAt(nx, ny);
                if (Math.Abs(nh - h) > 2) continue;
                int next = cost + num5 * Math.Abs(dy) / dex + num5 * Math.Abs(dx) / dex + (num5 * Math.Abs(nh - h) / dex) / 2;
                if (next >= costs[ny * Cols + nx]) continue;
                costs[ny * Cols + nx] = next;
                queue.Enqueue((nx, ny), next);
            }
        }
        return costs;
    }

    /// <summary>갈 수 있는 칸 가운데 목표까지 경로 비용이 가장 작은 칸(같으면 지금 자리에서 싼 칸). 없으면 −1.</summary>
    private int NearestReachableTo(UnitState u, MoveRange range, int goalCol, int goalRow)
    {
        var cost = CostMapFrom(u, goalCol, goalRow);
        int best = -1, bestCost = int.MaxValue, bestOwn = int.MaxValue;
        for (int i = 0; i < range.Cost.Length; i++)
        {
            // 길이 없는 칸은 후보가 아니다 — 원본 0x10059a20 은 0xffff 를 돌려줘 ⑥은 다음 목표로, 다 없으면 제자리에서 쉰다.
            // 전에는 맨해튼 거리로 대신해 벽 쪽으로 걸어갔다(ba-20 J N2).
            if (!range.CanReach(i) || cost == null || cost[i] == int.MaxValue) continue;
            int d = cost[i];
            if (d < bestCost || d == bestCost && range.Cost[i] < bestOwn) { bestCost = d; best = i; bestOwn = range.Cost[i]; }
        }
        return best;
    }

    /// <summary>(sc, sr) 에 선다고 쳤을 때 (col, row) 를 겨누면 맞는 인물들.</summary>
    private List<int> WorkTargetsFrom(WorkData w, UnitState user, int sc, int sr, int col, int row)
    {
        int keepCol = user.Col, keepRow = user.Row;
        user.WarpTo(sc, sr);
        var targets = WorkTargets(w, user, col, row);
        user.WarpTo(keepCol, keepRow);
        return targets;
    }

    /// <summary>그 인물이 쓸 수 있는 work 차례 — 익힌 어빌리티(분류 1·2·4) 다음에 기본공격.</summary>
    private List<WorkData> AiWorks(UnitState u, bool ignoreCost = false)
    {
        var list = new List<WorkData>();
        if (_db is not { } db || u.Data is not { } c) return list;
        foreach (var (abilityId, level) in c.Abilities)
        {
            if (!db.Abilities.TryGetValue(abilityId, out var ab) || ab.Category is not (1 or 2 or 4)) continue;
            if (ab.TryWorkAt(level, out int wid) && Work(wid) is { } w && (ignoreCost || CanAfford(u, w))) list.Add(w);
        }
        if (Work(c.BasicWorkId) is { } basic) list.Add(basic);
        return list;
    }

    /// <summary>고른 (설 칸, 겨눌 칸)으로 걸어가 work 을 쓴다.</summary>
    private IEnumerable<bool> AiUseWork(int index, WorkData w, (int Stand, int Col, int Row, int Score) use, MoveRange range)
    {
        var u = _units[index];
        var path = PathWithin(range, u.Col, u.Row, use.Stand) ?? [];
        var aimed = LiveUnitAt(use.Col, use.Row);
        int targetIndex = w.TargetMode is 1 or 4 or 5 && aimed != null ? Array.IndexOf(_units, aimed) : -1;
        var routine = UseWorkRoutine(index, w, targetIndex, use.Col, use.Row, path);
        while (routine.MoveNext()) yield return true;
    }

    /// <summary>
    /// AI 차례 — 명령 하나를 만들어 실행한 뒤 <b>TP 가 남으면 처음부터 다시 생각한다</b>(원본 0x1006db20 은 지금 유닛부터 다시 훑어
    /// TP&gt;0 이면 같은 유닛을 또 뽑는다, ba-2·ba-11). 기본공격 80 이면 TP 155 인 적이 두 번 친다. 전에는 한 번 쓰고 곧장 쉬어
    /// 적의 손 수가 원본의 절반쯤이었고 남은 TP 로 회복까지 받았다(fg-21 ①). 아무것도 못 하거나 TP 가 다하면 쉰다(WAITNEXT 자동 휴식).
    /// </summary>
    private IEnumerator<bool> AiRoutine(int index)
    {
        var u = _units[index];
        for (double end = _lastTime + 0.4; _lastTime < end;) yield return true;
        for (int think = 0; think < 8; think++)
        {
            bool acted = false;
            foreach (bool r in AiThink(index, a => acted = a)) yield return r;
            if (!acted || _turn != index || _outcome.Length > 0 || !u.Alive || !u.OnField) break;
            CommitMove(u);                                   // 걸은 비용을 빼야 남은 TP 로 다시 생각할 수 있다
            if (u.Tp <= 0) break;
            for (double end = _lastTime + 0.3; _lastTime < end;) yield return true;
            // 다시 생각하기 전에 판의 모든 유닛이 설 때까지 — 상태 15 갈래 1 이 0x1006e320(모든 유닛 +0x80 == 0)을 기다린 뒤 GETNEXT(0x1006a241).
            // 이동만 한 군단 대장의 부하가 진형 자리로 다 걸어간 뒤에 다음 공격을 해야 부하가 모두 낀다(감사5 L-A). 멈춤 대비 5초 상한.
            for (double end = _lastTime + 5; _lastTime < end && _units.Any(x => x.Alive && x.OnField && x.IsBusy);) yield return true;
        }
        if (_turn == index && _outcome.Length == 0 && u.Alive && u.OnField) Rest(index);
    }

    /// <summary>한 번 생각하기 — 분석-전투 ba-11 의 여섯 단계. 기술을 쓰거나 걸었으면 <paramref name="report"/>(true).</summary>
    private IEnumerable<bool> AiThink(int index, Action<bool> report)
    {
        var u = _units[index];
        if (_db is not { } db || ComputeRange(u) is not { } range) yield break;

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
        var enemies = _units.Where(t => t.Alive && t.OnField && SeesAsFoe(u, t)).ToList();
        int nearest = enemies.Count == 0 ? 99 : enemies.Min(t => Math.Abs(t.Col - u.Col) + Math.Abs(t.Row - u.Row));

        // 2단계 도망(0x1005b4e0) — 피가 적고 적이 가까우면 물러난다. 위험도 0.70 이하인 칸 중 <b>지금 자리에서 맨해튼으로 가장 가까운 칸</b>,
        // 없으면 위험도가 가장 작은 칸. 지금 칸이 이미 0.70 이하면 안 움직인다. 부대장이 아니고 회복기가 있으면 물러난 자리에서 회복기를 쓴다.
        if (hpPercent < db.N(66) && nearest < db.N(90))
        {
            var heal = FollowersOf(index).Count == 0 ? AiWorks(u).FirstOrDefault(x => x.IsHeal) : null;
            var fleeRange = heal != null ? ComputeRange(u, heal.Id) ?? range : range;
            int here = u.Row * Cols + u.Col, safest = here;
            if (FleeDanger(u, u.Col, u.Row) > SafeDanger)
            {
                int bestDist = int.MaxValue;
                double bestDanger = double.MaxValue;
                int leastDangerous = here;
                for (int i = 0; i < fleeRange.Cost.Length; i++)
                {
                    if (!fleeRange.CanReach(i)) continue;
                    double danger = FleeDanger(u, i % Cols, i / Cols);
                    if (danger < bestDanger) { bestDanger = danger; leastDangerous = i; }
                    if (danger > SafeDanger) continue;
                    int d = Math.Abs(i % Cols - u.Col) + Math.Abs(i / Cols - u.Row);
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

        // 3단계 자가 회복 → 4단계 공격. 원본은 <b>회복기만 한 바퀴 돌고, 못 쓰면 전부 한 바퀴</b> 돈다 —
        // 예전처럼 「피가 적으면 회복기 아닌 것을 건너뛴다」로 하면 회복기가 없는 인물이 공격까지 통째로 걸렀다.
        for (int pass = hpPercent < db.N(70) ? 0 : 1; pass < 2; pass++)
        {
            // 어려움 이상의 적은 첫 work 대신 피해 기술 가운데 가장 많이 깎는 것을 쓴다(Difficulty.cs).
            if (pass == 1 && SmartAi(u) && SmartPick(index, range) is { } smart)
            {
                report(true);
                foreach (bool r in AiUseWork(index, smart.Work, smart.Use, ComputeRange(u, smart.Work.Id) ?? range)) yield return r;
                yield break;
            }
            foreach (var w in AiWorks(u))
            {
                if (pass == 0 && !w.IsHeal) continue;
                // 이동 예산은 <b>그 기술</b>의 TP 비용을 뺀 만큼이다(0x1005d070) — 늘 기본공격 기준이면 비싼 기술도 너무 멀리 걸어가 쓴다.
                var workRange = ComputeRange(u, w.Id) ?? range;
                if (BestUse(index, w, workRange) is not { } use) continue;
                report(true);
                foreach (bool r in AiUseWork(index, w, use, workRange)) yield return r;
                yield break;
            }
        }

        // 4단계 (B) 접근(0x1005e39b) — 지금은 못 치지만 <b>최대 TP</b> 로는 닿는 적이 있으면, 목록 차례로 첫 성공 기술이 겨눌 칸을 향해
        // 지금 TP 로 갈 수 있는 칸 중 경로 비용이 가장 짧은 칸으로 가서 쉰다. 이 단계는 5단계 휴식보다 앞이다(fg-21 ⑮).
        if (u.Data is { } cd && Work(cd.BasicWorkId) is { } basicWork && ComputeRange(u, basicWork.Id, tp: u.MaxTp) is { } fullRange
            && BestUse(index, basicWork, fullRange, approach: true) != null)
        {
            foreach (var w in AiWorks(u, ignoreCost: true))
            {
                var wRange = ComputeRange(u, w.Id, tp: u.MaxTp) ?? fullRange;
                if (BestUse(index, w, wRange, approach: true) is not { } use) continue;
                int go = NearestReachableTo(u, range, use.Col, use.Row);
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
                $"ai no attack: unit {index} chr {u.ChrCode} side {u.Side} ({u.Col},{u.Row}) TP {u.Tp} works [{string.Join(",", AiWorks(u).Select(x => x.Id))}] enemies {enemies.Count} nearest {nearest} goals {goals.Count}"
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
        for (double end = _lastTime + 0.2; _lastTime < end;) yield return true;
    }

    private IEnumerable<bool> WalkTo(UnitState u, MoveRange range, int cell)
    {
        foreach (var step in range.PathTo(cell)) u.Path.Enqueue(step);
        while (u.IsBusy) yield return true;
    }
}
