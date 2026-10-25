using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 마법 스물둘 — 화이어 애로우 · 화이어 볼(퓨리 오브 화이어 · 묵호파) · 화이어 필드 · 아이스 미사일 · 아이스 실드 · 프로즌 아이스 · 문 라이트 ·
/// 쇼크 웨이브 · 에너지 필드 · 힐 · 블래스 · 큐어 · 힐 윈드 · 썬 라이트 · 스피디 · 배리어 · 커즈 · 쉐도우 실드 · 블러드 러스트 · 킬링 필드 · 아마게돈 ·
/// 화이어/아이스/썬더 블레이드(볼트 분석-파트1-기술-b 9절). Attack 번호로 나눈다(<see cref="IsMagicSkill"/>).
/// </summary>
/// <remarks>
/// <para>원본 핸들러는 공격 번호 표(<c>0x1004354c</c> → <c>0x1004342c</c>)로 나뉜다 — 블레이드 셋은 한 핸들러 <c>0x10047440</c>(인자 0 화염 · 1 빙한 · 2 전격),
/// 나머지는 기술마다 하나. 레벨(같은 기술의 다음 공격 번호)이 핸들러 안에서 바꾸는 것은 적고 대부분 Attack.dat 수치만 다르다.</para>
/// <para>앞머리(<c>Attack +0x69</c>)는 레벨 1~4 가 갈래 3(<see cref="MagicPrep"/> — <c>0x10043a70</c>), 레벨 5 와 문 라이트 · 썬 라이트 · 아마게돈 · 묵호파 ·
/// 퓨리 오브 화이어가 갈래 4(<see cref="SummonPrep"/>), 레벨 6~9(적 전용으로 보인다)는 갈래 0(앞머리 없음), 다하카 애로우(65)는 갈래 1(<see cref="SwordPrep"/>).</para>
/// <para>피해 · 회복 · 상태는 전투 쪽(Part1Rules)이 Attack.dat 로 셈하고, 여기서는 그림을 깔고 원본이 맞히는 틱에 대상마다 한 번 <c>hit</c> 를 부른다.</para>
/// </remarks>
internal sealed partial class Part1Skill
{
    /// <summary>이 파일이 그리는 파트1 마법의 공격 번호인가.</summary>
    internal static bool IsMagicSkill(int attackId) => attackId is (>= 51 and <= 86) or (>= 88 and <= 133) or (>= 143 and <= 160)
        or (>= 162 and <= 207) or (>= 226 and <= 270) or 276;

    /// <summary>마법 하나 — 공격 번호로 나눠 시각표를 깔고 끝나는 틱을 돌려준다. <paramref name="aim"/> 은 겨눈 칸의 발 자리(판 픽셀).</summary>
    private int Magic(int attackId, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit) => attackId switch
    {
        (>= 51 and <= 59) or (>= 88 and <= 96) or (>= 125 and <= 133) => Blade(attackId, targets, cells, aim, hit),
        >= 60 and <= 68 => FireArrow(attackId, targets, cells, aim, hit),
        >= 69 and <= 77 => FireBall(attackId, targets, cells, aim, hit),
        >= 78 and <= 86 => FireField(attackId, targets, cells, aim, hit),
        >= 97 and <= 105 => IceMissile(attackId, targets, cells, aim, hit),
        >= 106 and <= 114 => IceShield(attackId, targets, cells, aim, hit),
        >= 115 and <= 123 => FrozenIce(attackId, targets, cells, aim, hit),
        124 => MoonLight(attackId, targets, cells, aim, hit),
        >= 143 and <= 151 => ShockWave(attackId, targets, cells, aim, hit),
        >= 152 and <= 160 => EnergyField(attackId, targets, cells, aim, hit),
        >= 162 and <= 170 => Heal(attackId, targets, cells, aim, hit),
        >= 171 and <= 179 => Bless(attackId, targets, cells, aim, hit),
        >= 180 and <= 188 => Cure(attackId, targets, cells, aim, hit),
        >= 189 and <= 197 => HealWind(attackId, targets, cells, aim, hit),
        198 => SunLight(attackId, targets, cells, aim, hit),
        >= 199 and <= 207 => Speedy(attackId, targets, cells, aim, hit),
        >= 226 and <= 234 => Barrier(attackId, targets, cells, aim, hit),
        >= 235 and <= 243 => Curse(attackId, targets, cells, aim, hit),
        >= 244 and <= 252 => ShadowShield(attackId, targets, cells, aim, hit),
        >= 253 and <= 261 => BloodLust(attackId, targets, cells, aim, hit),
        >= 262 and <= 270 => KillingField(attackId, targets, cells, aim, hit),
        276 => Armageddon(attackId, targets, cells, aim, hit),
        _ => 0,
    };

    /// <summary>그 마법이 쓰는 파트1 그림(앞머리 갈래 3 · 4 의 것까지) — 파트1 이 없는 PC 에서 쓰기 전에 받아 둔다.</summary>
    private static int[] MagicNeeds(int attackId)
    {
        int[] prep = [55, 49, 50, 91, 897, 395];
        int[] own = attackId switch
        {
            (>= 51 and <= 59) or (>= 88 and <= 96) or (>= 125 and <= 133) => BladeNeeds,
            >= 60 and <= 68 => FireArrowNeeds,
            >= 69 and <= 77 => FireBallNeeds,
            >= 78 and <= 86 => FireFieldNeeds,
            >= 97 and <= 105 => IceMissileNeeds,
            >= 106 and <= 114 => IceShieldNeeds,
            >= 115 and <= 123 => FrozenIceNeeds,
            124 => MoonLightNeeds,
            >= 143 and <= 151 => ShockWaveNeeds,
            >= 152 and <= 160 => EnergyFieldNeeds,
            >= 162 and <= 170 => HealNeeds,
            >= 171 and <= 179 => BlessNeeds,
            >= 180 and <= 188 => CureNeeds,
            >= 189 and <= 197 => HealWindNeeds,
            198 => SunLightNeeds,
            >= 199 and <= 207 => SpeedyNeeds,
            >= 226 and <= 234 => BarrierNeeds,
            >= 235 and <= 243 => CurseNeeds,
            >= 244 and <= 252 => ShadowShieldNeeds,
            >= 253 and <= 261 => BloodLustNeeds,
            >= 262 and <= 270 => KillingFieldNeeds,
            276 => ArmageddonNeeds,
            _ => [],
        };
        return [.. prep.Concat(own).Distinct()];
    }

    private static IReadOnlyList<Part1Attack>? _magicAttacks;

    /// <summary>
    /// 마법 앞머리 — <c>Attack +0x69</c> 갈래대로. 0 은 없음(다음 틱에 핸들러), 1 은 <see cref="SwordPrep"/>, 4 는 <see cref="SummonPrep"/>,
    /// 3 은 <c>0x10043a70</c>(갈래 4 에서 Obs 91 짝과 897 사슬을 뺀 것): 검은 빛 55:0(z+40, 89틱), 마법진 49:0(소리 73) · 49:1(54틱),
    /// 20 · 25 · … · 40틱에 솟는 불티 50:0 다섯씩(빠르기 10~29 × 0.95, 10틱), 카메라를 시전자에, 대상 쪽을 보고 파트1 동작 4 를 54틱 붙든 뒤 동작 24 를 한 번.
    /// </summary>
    private int MagicPrep(int attackId, UnitState user, (double X, double Y) aim)
    {
        int kind = Part1Attack.ById(_magicAttacks ??= Part1Attack.LoadAll(), attackId)?.Prep ?? 3;
        switch (kind)
        {
            case 0: return 1;
            case 1: return SwordPrep(user);
            case 4: return SummonPrep(user, aim);
        }
        var (hx, hy) = _home;
        Put(55, 0, 0, hx, hy - 40 * 0.6, life: 89);
        Put(49, 0, 0, hx, hy, life: 54, sounds: true);
        Put(49, 1, 0, hx, hy, life: 54);
        for (int k = 0; k < 5; k++)
            for (int n = 0; n < 5; n++) Rise(50, 0, 20 + 5 * k, hx + Rand(41) - 20, hy + Rand(41) - 20, Rand(20) + 10, 0.95, 10);
        host.Btl.CenterOnUnit(user);
        if (aim != _home) user.Facing = FacingOf(aim.X - hx, aim.Y - hy);
        user.PlayAction(Part2Action(4), 54 / TicksPerSecond);
        _afterPrep = (24, Part2Action(4), true);   // 54틱 — 동작 24 를 걸고 잠근다(0x10043d85..0x10043d9c)
        return 55;
    }

    // ---- A ----

    // ---- 화이어/아이스/썬더 블레이드 · 화이어 애로우 · 화이어 볼 · 화이어 필드 (볼트 분석-파트1-기술-b 9.1~9.4) ----

    /// <summary>블레이드가 쓰는 파트1 그림 — 속성 셋(화염 · 빙한 · 전격)을 다 받아 둔다.</summary>
    private static readonly int[] BladeNeeds = [55, 56, 52, 28, 44, 43, 45, 47, 46, 38, 39, 40, 41];

    /// <summary>
    /// 화이어 · 아이스 · 썬더 블레이드(공격 51~59 · 88~96 · 125~133) — 셋이 한 핸들러 <c>0x10047440</c>(인자 = 속성 0 · 1 · 2)를 쓰고 그림 번호만 다르다.
    /// 소리 84/86/88, 시전자 발치에 고리 28/44/43(모션 0 · 1, 120틱)이 0틱 · 15틱에 두 번, 31틱에 기운 55/56/52:0(z+40, 89틱),
    /// 불티 38/39/40:0 쉰다섯이 2틱 간격으로 시전자 둘레(±20)에서 솟는다(빠르기 5, 31틱). 100틱에 소리 85/87/89, 대상마다 고리 45/47/46(모션 0 · 1, 120틱)이 서고
    /// 모션 1 이 한 틱 지나 친다(<c>0x10047aa5</c>). 131틱에 대상 z+40 에 기운 55/56/52:0. 화염 · 빙한은 대상 위(z 0~171)에서 불티 38/39 쉰다섯이 내려앉고(100+2i 틱),
    /// 전격은 대신 번개 41:0(z+40, 61틱, 소리 68)이 100 · 130 · 160틱에 친다.
    /// </summary>
    private int Blade(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        int e = id switch { >= 125 => 2, >= 88 => 1, _ => 0 };
        int aura = new[] { 55, 56, 52 }[e], ring = new[] { 28, 44, 43 }[e], mark = new[] { 45, 47, 46 }[e], spark = new[] { 38, 39, 40 }[e];
        var (hx, hy) = _home;
        Sound(new[] { 84, 86, 88 }[e], 0);
        foreach (int t in new[] { 0, 15 })
        {
            Put(ring, 0, t, hx, hy, life: 120);
            Put(ring, 1, t, hx, hy, life: 120);
        }
        Put(aura, 0, 31, hx, hy - 40 * 0.6, life: 89);
        for (int i = 0; i < 55; i++)
            Rise(spark, 0, 2 * i, hx + Rand(20) * (Rand(2) == 0 ? -1 : 1), hy + Rand(20) * (Rand(2) == 0 ? -1 : 1) * 0.8, 5, 1.0, life: 31);
        Later(70, () => host.Btl.CenterOn(hx, hy - 60));
        Sound(new[] { 85, 87, 89 }[e], 100);
        int last = 2 * 54 + 31;
        bool first = true;
        foreach (var (ti, (tx, ty)) in Points(targets, aim))
        {
            if (first) { Later(100, () => host.Btl.CenterOn(tx, ty)); first = false; }
            Put(mark, 0, 100, tx, ty, life: 120);
            Put(mark, 1, 100, tx, ty, life: 120);
            if (ti >= 0) HitAt(101, ti, hit);
            Put(aura, 0, 131, tx, ty - 40 * 0.6, life: 89);
            last = Math.Max(last, 220);
            if (e == 2)
            {
                foreach (int t in new[] { 100, 130, 160 }) Put(41, 0, t, tx, ty - 40 * 0.6, life: 61, sounds: true);
                last = Math.Max(last, 221);
                continue;
            }
            // 내려앉는 불티(0x10079330) — z 를 빠르기 5 씩 대상 높이까지 내리고 31틱에 끝난다.
            for (int i = 0; i < 55; i++)
            {
                double x = tx + Rand(20) * (Rand(2) == 0 ? -1 : 1), y = ty + Rand(20) * (Rand(2) == 0 ? -1 : 1) * 0.8, z = 9 * Rand(20);
                int fall = (int)Math.Ceiling(z / 5);
                Along(spark, 0, 100 + 2 * i, [(0, x, y - z * 0.6), (Math.Max(1, fall), x, y)], life: 31);
            }
            last = Math.Max(last, 100 + 2 * 54 + 31);
        }
        return last + 1;
    }

    /// <summary>화이어 애로우가 쓰는 파트1 그림.</summary>
    private static readonly int[] FireArrowNeeds = [34, 64, 55, 33, 83, 18, 48, 10, 38];

    /// <summary>
    /// 화이어 애로우(공격 60~68, 핸들러 <c>0x10047df0</c>) — 화살 하나가 Attack +0x66 번(1 · 1 · 2 · 2 · 3 · 다하카 9 · 1 · 1 · 1) 난다. k 번째 화살은
    /// 표 <c>0, 5, 20, 25, 30, 35, 40, 45, 50, 55</c> 의 d 틱에 소리 3(+0~9), 시전자 위 z+120(둘째부터 ±40 · z+120~139) S 로 불티 34/64:1 서른이 모여들고,
    /// d+15 에 기운 55:1, d+10 에 불꽃 33:0(39틱)이 핀다(첫 화살은 그 뒤 7틱에 83:0 도). 33 이 지면 화살 18(16방향 모션, 9~15 는 뒤집어)이 대상으로
    /// 빠르기 1 × 1.3 으로 날며 불티 꼬리를 끈다. 닿으면 터짐 48:0(소리 58)이 서고 한 틱 지나 그 칸을 친다(<c>0x1004862a</c>, 칸 하나),
    /// 불기둥 10:0(소리 8), 솟는 불티 34 열, 앞으로 튀는 불티 34 스물 · 38 열.
    /// </summary>
    private int FireArrow(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        int n = id switch { 62 or 63 => 2, 64 => 3, 65 => 9, _ => 1 };
        int[] delays = [0, 5, 20, 25, 30, 35, 40, 45, 50, 55];
        var (hx, hy) = _home;
        var T = TargetPoint(targets, aim);
        int victim = targets.Count > 0 ? targets[0] : -1, last = 0;
        for (int k = 0; k < n; k++)
        {
            int d = delays[Math.Min(k, delays.Length - 1)];
            (double X, double Y) s = k == 0 ? (hx, hy - 120 * 0.6)
                : (hx + Sign() * Rand(40), hy + Sign() * Rand(40) * 0.8 - (120 + Rand(20)) * 0.6);
            (double X, double Y) t = victim >= 0 ? T : (T.X + Sign() * Rand(40), T.Y + Sign() * Rand(40) * 0.8);
            Sound(3, d + Rand(10));
            FireGather(s, 30, 90, 1, d);
            Put(55, 1, d + 15, s.X, s.Y, life: 56);
            Put(33, 0, d + 10, s.X, s.Y);
            int go = d + 10 + Length(33, 0) + 1;
            int dir = Dir16(s, t);
            if (k == 0)
            {
                bool flip = dir is 1 or 2 or 3 or 9 or 10 or 11 || (dir % 4 == 0 && Rand(2) == 1);
                Put(83, 0, go + 7, s.X, s.Y, life: 31, mirror: flip);
            }
            int impact = FireMissile(18, dir <= 8 ? dir : 16 - dir, dir > 8, go, s, t, 1.3);
            if (k == 0) Later(go, () => host.Btl.CenterOn(t.X, t.Y));
            Put(48, 0, impact + 2, t.X, t.Y, life: 24, sounds: true);
            if (k == 0 && victim >= 0) HitAt(impact + 3, victim, hit);
            last = Math.Max(last, FireBurst(impact + 2, t, s));
        }
        return last + 1;

        int Sign() => Rand(2) == 0 ? -1 : 1;
    }

    /// <summary>화이어 볼이 쓰는 파트1 그림(땅불 23 · 96 과 그 자식 93 까지).</summary>
    private static readonly int[] FireBallNeeds = [64, 55, 33, 17, 48, 10, 34, 38, 23, 96, 93];

    /// <summary>
    /// 화이어 볼(공격 69~77, 핸들러 <c>0x10048970</c> — 퓨리 오브 화이어 77 · 묵호파 76 도) — 시전자 둘레 반지름 50(76 · 77 은 80)의 원을 공 수(Attack +0x66: 3~7 · 1 · 1 · 30 · 20)로
    /// 나눈 자리 S_k(z+120)마다 20k 틱에 솟아 도는 불티 64:2 서른, 20k+20 에 기운 55:1, 20k+23 에 불꽃 33:0. 33 이 지면 불덩이 17:0(소리 57)이
    /// 대상 하나(목록에서 아무나)로 빠르기 1 × 1.2 로 난다. 닿으면 터짐 48:1 이 한 틱 지나 그 칸을 치고(<c>0x10049115</c>), 불기둥 10:0 과 불티가 튄다.
    /// 대상이 없어 빈 땅에 떨어지면 땅불(23 → 96)이 탄다. 소리 59 는 (20−k)·k 틱.
    /// </summary>
    /// <remarks>원본은 공마다 대상을 아무렇게나 고른다 — 여기서는 대상 목록을 차례로 돌려 모든 대상이 첫 공에 맞게 했다. 공이 여덟 넘으면 도는 불티를 열로 줄였다.</remarks>
    private int FireBall(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        int n = id switch { 69 => 3, 70 => 4, 71 => 5, 72 => 6, 73 => 7, 76 => 30, 77 => 20, _ => 1 };
        double r = id is 76 or 77 ? 80 : 50;
        var (hx, hy) = _home;
        var hitDone = new HashSet<int>();
        int last = 0;
        for (int k = 0; k < n; k++)
        {
            // 원 래스터는 위에서 시작해 시계 방향(0x10009a70) — 그 (점 수 / n)·k 번째 점.
            double a = -Math.PI / 2 + 2 * Math.PI * k / n;
            (double X, double Y) ground = (hx + r * Math.Cos(a), hy + r * Math.Sin(a) * 0.8), s = (ground.X, ground.Y - 120 * 0.6);
            int victim = targets.Count > 0 ? targets[k % targets.Count] : -1;
            (double X, double Y) t = victim >= 0 ? ((double, double))host.Btl.UnitFoot(host._units[victim])
                : (aim.X + (Rand(3) - 1) * TileW, aim.Y + (Rand(3) - 1) * 32);
            Sound(59, Math.Max(0, (20 - k) * k));
            // 솟아 도는 불티(0x100808d0) — 반지름 20~49 를 빠르기 15 로 돌며 틱마다 z 가 15 씩 오른다(50틱).
            int puffs = n > 8 ? 10 : 30;
            for (int i = 0; i < puffs; i++)
            {
                var ring = Circle(ground.X, ground.Y, Rand(30) + 20, 50, 15, 1, true, Rand(360) * Math.PI / 180);
                Along(64, 2, 20 * k, [.. ring.Select(p => (p.Tick, p.X, p.Y - p.Tick * 15 * 0.6))], life: 50);
            }
            Put(55, 1, 20 * k + 20, s.X, s.Y, life: 56);
            Put(33, 0, 20 * k + 23, s.X, s.Y);
            int go = 20 * k + 23 + Length(33, 0) + 1;
            int impact = FireMissile(17, 0, false, go, s, t, 1.2);
            if (k == 1 && id != 77) Later(go, () => host.Btl.CenterOn(t.X, t.Y));
            if (k == 0) Later(go, () => host.Btl.CenterOn(t.X, t.Y));
            Put(48, 1, impact + 2, t.X, t.Y);
            if (victim >= 0 && hitDone.Add(victim)) HitAt(impact + 3, victim, hit);
            last = Math.Max(last, FireBurst(impact + 2, t, s));
            // 빈 땅 — 땅불이 붙었다가 사그라진다(23 r → 23 r+3 → 96 r → 96 r+3 → 96 r+6, 메테오와 같은 사슬).
            if (victim < 0)
            {
                int m = Rand(3), f1 = impact + 2, hold = Rand(10) + 20;
                Put(23, m, f1, t.X, t.Y, life: hold);
                int f2 = f1 + hold + 2;
                Put(23, m + 3, f2, t.X, t.Y);
                int f3 = f2 + Length(23, m + 3) + 2;
                Put(96, m, f3, t.X, t.Y);
                int f4 = f3 + Length(96, m) + 2, hold4 = Rand(10) + 10;
                Put(96, m + 3, f4, t.X, t.Y, life: hold4);
                int f5 = f4 + hold4 + 2;
                Put(96, m + 6, f5, t.X, t.Y);
                last = Math.Max(last, f5 + Length(96, m + 6));
            }
        }
        return last + 1;
    }

    /// <summary>화이어 필드가 쓰는 파트1 그림.</summary>
    private static readonly int[] FireFieldNeeds = [34, 55, 20, 83, 48, 10, 64, 23, 96, 93];

    /// <summary>
    /// 화이어 필드(공격 78~86, 핸들러 <c>0x10049760</c>) — 겨눈 칸 A 위 높이 500(화면 300px)의 점 P 로 불티 34 예순이 모여들고(소리 3), P 에 기운 55:0(89틱),
    /// 30틱에 불꽃 20:0(28틱), 44 · 47틱에 83:0 둘(하나는 뒤집어). 20:0 이 지면 불덩이 20:1(소리 4)이 P 에서 A 로 빠르기 1 × 1.2 로 떨어지고,
    /// 닿으면 터짐 48:1 · 불기둥 10:1 · 소리 1. 그때 범위의 칸마다 땅을 기는 불티 64:2 가 A 에서 그 칸으로 가고(빠르기 1 × 1.1), 닿은 칸에 땅불(23 → 96)이 탄다.
    /// </summary>
    /// <remarks>원본 핸들러에는 맞히는 사건이 없다 — 땅불(<c>0x1007b970</c>)이 붙는 틱(나이 1)에 그 칸의 유닛에게 메시지 10 을 보낸다(<c>0x1007bd90</c>).
    /// 여기서는 그 땅불이 붙고 한 틱 뒤 그 칸에 선 대상을 친다. 원본의 땅불은 판에 남아 차례 수(+0x144 = 30~49)만큼 탄다 — 여기서는 메테오처럼 틱으로 짧게 태운다.</remarks>
    private int FireField(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        var a = aim;
        (double X, double Y) p = (a.X, a.Y - 500 * 0.6);
        Sound(3, 0);
        FireGather(p, 60, 100, 1, 0, motion: -1);
        Put(55, 0, 0, p.X, p.Y, life: 89);
        Put(20, 0, 30, p.X, p.Y, life: 28);
        Put(83, 0, 44, p.X, p.Y, life: 31);
        Put(83, 0, 47, p.X, p.Y, life: 31, mirror: true);
        int go = 30 + 28 + 2;
        int impact = FireMissile(20, 1, false, go, p, a, 1.2, trail: 34);
        Later(go, () => host.Btl.CenterOn(a.X, a.Y));
        Put(48, 1, impact + 2, a.X, a.Y, life: 24);
        Put(10, 1, impact + 2, a.X, a.Y, life: 46);
        Sound(1, impact + 2);
        int last = impact + 2 + 46;
        List<(int Col, int Row)> area = cells.Count > 0 ? cells : [((int)(a.X / TileW), host.RowAt((int)a.X, (int)a.Y))];
        var hitDone = new HashSet<int>();
        foreach (var (col, row) in area)
        {
            var c = CellFoot(col, row);
            var walk = Move(a, c, 1, 1.1);
            Along(64, 2, impact + 2, AsPath(walk.Prepend(a).ToList(), 2), life: walk.Count);
            int f1 = impact + 2 + walk.Count + 2, m = Rand(3), hold = Rand(20) + 30;
            Put(23, m, f1, c.X, c.Y, life: hold);
            if (Rand(2) == 0) Rise(Rand(2) == 0 ? 54 : 64, 0, f1 + Rand(hold), c.X + Rand(41) - 20, c.Y + (Rand(41) - 20) * 0.8, Rand(7) + 7, 0.9, life: 24);
            int f2 = f1 + hold + 2;
            Put(23, m + 3, f2, c.X, c.Y);
            int f3 = f2 + Length(23, m + 3) + 2;
            Put(96, m, f3, c.X, c.Y);
            int f4 = f3 + Length(96, m) + 2, hold4 = Rand(10) + 10;
            Put(96, m + 3, f4, c.X, c.Y, life: hold4);
            int f5 = f4 + hold4 + 2;
            Put(96, m + 6, f5, c.X, c.Y);
            last = Math.Max(last, f5 + Length(96, m + 6));
            for (int i = 0; i < host._units.Length; i++)
                if (targets.Contains(i) && host._units[i] is { } u && u.Col == col && u.Row == row && hitDone.Add(i)) HitAt(f1 + 1, i, hit);
        }
        foreach (int ti in targets)
            if (hitDone.Add(ti)) HitAt(impact + 3, ti, hit);
        return last + 1;
    }

    // ---- 불 마법 셋이 함께 쓰는 조각(다른 파일과 이름이 겹치지 않게 Fire 를 붙였다) ----

    /// <summary>
    /// 불티가 점 <paramref name="to"/> 로 모여든다(<c>0x1007cb40</c>) — ±<paramref name="spread"/> 에서 빠르기 1 × 1.1, 34/64 모션 1(<paramref name="motion"/> −1 이면 34 의 모션 0/1).
    /// </summary>
    private void FireGather((double X, double Y) to, int count, int spread, double speed, int tick, int motion = 1)
    {
        for (int i = 0; i < count; i++)
        {
            (double X, double Y) from = (to.X + (Rand(2) == 0 ? -1 : 1) * Rand(spread), to.Y + (Rand(2) == 0 ? -1 : 1) * Rand(spread) * 0.8);
            var path = Move(from, to, speed, 1.1);
            int obs = motion < 0 ? 34 : Rand(2) == 1 ? 64 : 34, m = motion < 0 ? Rand(2) : motion;
            Along(obs, m, tick + Rand(10), AsPath(path.Prepend(from).ToList(), 2), life: path.Count);
        }
    }

    /// <summary>
    /// 날아가는 불 — 곧은 이동기(빠르기 1 × <paramref name="factor"/>, 최대 40)로 <paramref name="from"/> → <paramref name="to"/>. 2틱마다 뒤로 흩어지는 불티 꼬리
    /// (<c>0x10078a90</c> 꼬리 정보 — 64 또는 <paramref name="trail"/>, 24틱)를 남긴다. 닿는 틱을 돌려준다.
    /// </summary>
    private int FireMissile(int obs, int motion, bool mirror, int tick, (double X, double Y) from, (double X, double Y) to, double factor, int trail = 64)
    {
        var path = Move(from, to, 1, factor);
        Along(obs, motion, tick, AsPath(path.Prepend(from).ToList()), life: path.Count, mirror: mirror);
        double len = Math.Max(1, Dist(from, to)), bx = (from.X - to.X) / len, by = (from.Y - to.Y) / len;
        for (int t = 0; t < path.Count; t += 2)
        {
            var (x, y) = path[t];
            var drift = Move((x, y), (x + bx * (Rand(30) + 20) + Rand(21) - 10, y + by * (Rand(30) + 20) + Rand(21) - 10), 1, 1.2);
            Along(trail, 0, tick + t, AsPath(drift.Prepend((x, y)).ToList(), 2), life: 24);
        }
        return tick + path.Count;
    }

    /// <summary>
    /// 불이 닿은 자리 — 불기둥 10:0(소리 8), 솟는 불티 34:0 열(z+20~58, 빠르기 3 × 1.1, 5~14틱 늦게), 날아온 쪽으로 튀는 불티 34:0 스물(빠르기 25 × 0.8)과
    /// 38:0 열(빠르기 10, 틱마다 −0.1)(<c>0x1007a2e0</c> — 방향 16칸의 앞쪽 20~169px, 옆으로 ±30). 끝나는 틱.
    /// </summary>
    private int FireBurst(int tick, (double X, double Y) at, (double X, double Y) from)
    {
        Put(10, 0, tick, at.X, at.Y, life: 46, sounds: true);
        for (int i = 0; i < 10; i++)
            Rise(34, 0, tick + 5 + Rand(10), at.X + Rand(41) - 20, at.Y + (Rand(41) - 20) * 0.8 - (20 + 2 * Rand(20)) * 0.6, 3, 1.1, life: 24);
        double len = Math.Max(1, Dist(from, at)), fx = (at.X - from.X) / len, fy = (at.Y - from.Y) / len;
        void Fly(int obs, double speed, double factor, bool add, int life)
        {
            double reach = Rand(150) + 20, side = (Rand(2) == 0 ? -1 : 1) * Rand(30);
            (double X, double Y) to = (at.X + fx * reach - fy * side, at.Y + fy * reach + fx * side * 0.8);
            var path = Move(at, to, speed, factor, 40, 0.5, add);
            Along(obs, 0, tick + Rand(10), AsPath(path.Take(life).Prepend(at).ToList(), 2), life: life);
        }
        for (int i = 0; i < 20; i++) Fly(34, 25, 0.8, false, 24);
        for (int i = 0; i < 10; i++) Fly(38, 10, -0.1, true, 31);
        return tick + 10 + 46;
    }

    // ---- B ----

    // ---- 아이스 미사일 · 아이스 실드 · 프로즌 아이스 · 문 라이트 · 쇼크 웨이브 · 에너지 필드 (볼트 분석-파트1-기술-b 9.5~9.10) ----

    /// <summary>
    /// 아이스 미사일(공격 97~105, 핸들러 <c>0x1004b140</c>) — 대상마다 10틱 간격으로 시전자 머리 위(z+120, 둘째부터 ±40 흩어)에 빛 56:1 이 서고,
    /// 얼음살이 맺혀(19:방향 0~8, 21틱, 소리 69) 다 맺히면 소리 56 과 함께 날아가는 얼음살(19:방향 9~17)이 대상으로 곧게 난다(빠르기 1 × 1.4, 잔상).
    /// 닿고 2틱 뒤 대상에 얼음 터짐 51:0(소리 71)이 서며 한 틱 뒤 친다. 김 35 · 39 가 날아온 쪽으로 흩어지고 얼음 조각 37:1 셋이 튀어 37:5 로 사라진다.
    /// </summary>
    private int IceMissile(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        var (hx, hy) = _home;
        int n = Math.Max(1, targets.Count), last = 0;
        int Sign() => Rand(2) == 0 ? -1 : 1;
        for (int i = 0; i < n; i++)
        {
            int ti = i < targets.Count ? targets[i] : -1;
            (double X, double Y) s = i == 0 ? (hx, hy - 120 * 0.6) : (hx + Sign() * Rand(40), hy + Sign() * Rand(40) * 0.8 - (Rand(20) + 120) * 0.6);
            (double X, double Y) t = ti >= 0 ? host.Btl.UnitFoot(host._units[ti]) : aim;
            int d = Dir16(s, t);
            Put(56, 1, 10 * i + Rand(5), s.X, s.Y, life: 56);
            // 맺힘 — 오른쪽 반(0~8)은 그대로, 왼쪽(9~15)은 16−d 를 뒤집어.
            int a1 = 10 * i + 5 + Rand(5);
            Put(19, d < 9 ? d : 16 - d, a1, s.X, s.Y, life: 21, mirror: d >= 9, sounds: true);
            Sound(56, a1 + 21);
            int start = a1 + 21 + 2, fm = d < 9 ? d + 9 : 25 - d;
            var flight = Move(s, t, 1, 1.4);
            var path = flight.Prepend(s).ToList();
            Along(19, fm, start, AsPath(path), life: path.Count, mirror: d >= 9);
            // 잔상 — 2틱마다 지나간 자리에 날아가는 얼음살이 옅게 남는다(원본의 뿌리개 0x10078a90).
            for (int k = 0; k < path.Count; k += 2)
                Along(19, fm, start + k, [(0, path[k].X, path[k].Y)], life: 6, mirror: d >= 9, fadeFrom: 0.6, fadeTo: 0.1);
            if (i == 0) Later(start, () => host.Btl.CenterOn(t.X, t.Y));
            int arrive = start + flight.Count, impact = arrive + 2;
            Put(51, 0, impact, t.X, t.Y, life: 71, sounds: true);
            if (ti >= 0) HitAt(impact + 1, ti, hit);
            // 김 — 흔들리며 솟는 35:0 열(빠르기 3 × 1.1, 24틱).
            for (int k = 0; k < 10; k++)
                Rise(35, 0, impact + 5 + Rand(10), t.X + Sign() * Rand(20), t.Y + Sign() * Rand(20) * 0.8 - (20 + 2 * Rand(20)) * 0.6, 3, 1.1, 24);
            // 날아온 쪽으로 흩어지는 김 — 35:0 스물(빠르기 25 × 0.8, 24틱) · 39:0 열(빠르기 10, 31틱).
            double len = Math.Max(1, Dist(s, t)), ux = (t.X - s.X) / len, uy = (t.Y - s.Y) / len;
            void Drift(int obs, int tick, double speed, double factor, int life)
            {
                double far = Rand(100) + 50;
                var to = (t.X + ux * far + Sign() * Rand(30), t.Y + uy * far + Sign() * Rand(30) * 0.8);
                var p = Move(t, to, speed, factor).Take(life).Prepend(t).ToList();
                Along(obs, 0, tick, AsPath(p, 2), life: life);
            }
            for (int k = 0; k < 20; k++) Drift(35, impact + Rand(10), 25, 0.8, 24);
            for (int k = 0; k < 10; k++) Drift(39, impact + Rand(10), 10, 0.99, 31);
            // 얼음 조각 — 높이 40~79 로 솟았다 두 번 튀어 내려앉고 그 자리에서 37:5 로 바랜다.
            for (int k = 0; k < 3; k++)
            {
                int at = impact + Rand(5);
                double h = (Rand(40) + 40) * 0.6, dx = Sign() * (Rand(20) + 10), dy = Sign() * (Rand(20) + 10) * 0.8;
                (int, double, double)[] hop =
                [
                    (0, t.X, t.Y - h), (8, t.X + dx, t.Y + dy), (12, t.X + dx * 5 / 3, t.Y + dy * 5 / 3 - h / 2),
                    (16, t.X + dx * 2, t.Y + dy * 2), (19, t.X + dx * 7 / 3, t.Y + dy * 7 / 3 - h / 4), (22, t.X + dx * 8 / 3, t.Y + dy * 8 / 3),
                ];
                Along(37, 1, at, hop, life: 22);
                Put(37, 5, at + 24, t.X + dx * 8 / 3, t.Y + dy * 8 / 3);
                last = Math.Max(last, at + 24 + Length(37, 5));
            }
            last = Math.Max(last, impact + Math.Max(71, 40));
        }
        return last + 1;
    }

    private static readonly int[] IceMissileNeeds = [56, 19, 51, 35, 39, 37];

    /// <summary>
    /// 아이스 실드(공격 106~114, 핸들러 <c>0x1004bbc0</c>) — 소리 1880. 대상마다 그 위(z+60)에 얼음 껍질 27:0(66틱)이 한 번 돌고,
    /// 둘레(±20)에서 반짝이 35:0 스물다섯이 흔들리며 솟는다(빠르기 5~14 × 0.95, 0~29틱 늦게). 껍질이 사라질 때 친다(<c>0x10076950</c>).
    /// </summary>
    private int IceShield(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        Sound(1880, 0);
        int last = 0, shell = Length(27, 0);
        foreach (var (ti, (tx, ty)) in Points(targets, aim))
        {
            Put(27, 0, 0, tx, ty - 60 * 0.6);
            if (ti >= 0) HitAt(shell, ti, hit);
            for (int k = 0; k < 25; k++)
            {
                int at = Rand(30);
                Rise(35, 0, at, tx + (Rand(2) == 0 ? -1 : 1) * Rand(20), ty + (Rand(2) == 0 ? -1 : 1) * Rand(20) * 0.8, Rand(10) + 5, 0.95);
                last = Math.Max(last, at + Length(35, 0));
            }
            last = Math.Max(last, shell);
        }
        return last + 1;
    }

    private static readonly int[] IceShieldNeeds = [27, 35];

    /// <summary>
    /// 프로즌 아이스(공격 115~123, 핸들러 <c>0x1004bee0</c>) — 범위 칸마다(소리 70, 20틱) 보이지 않는 타이머가 칸 가운데로 걸어 들어온 뒤(빠르기 1 × 1.03),
    /// 눈보라 190:0 다섯이 반지름 30 으로 돌고(5틱 간격, 51틱), 40틱 뒤 얼음 118:r 이 솟아 얼음 덩이 118:r+3 으로 남는다.
    /// 원본 얼음 덩이는 판에 남아 차례가 돌 때마다 그 칸의 인물을 식히고(메시지 11) 칸 가운데에서 멀수록 일찍 녹는다(118:r+6).
    /// 원본에는 피해 묶음이 없다 — 여기서는 덩이가 서는 틱에 그 칸의 대상을 한 번 친다.
    /// </summary>
    private int FrozenIce(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        Sound(70, 20);
        var area = cells.Count > 0 ? cells : [((int)aim.X / TileW, host.RowAt((int)aim.X, (int)aim.Y))];
        int last = 0;
        var struck = new HashSet<int>();
        int Sign() => Rand(2) == 0 ? -1 : 1;
        foreach (var (col, row) in area)
        {
            var (px, py) = CellFoot(col, row);
            int r = Rand(3);
            var walk = Move((px + Sign() * Rand(20), py + Sign() * Rand(20) * 0.8), (px, py), 1, 1.03);
            int a = walk.Count;
            for (int k = 0; k < 5; k++)
                Along(190, 0, a + 2 + 5 * k, Circle(px + Sign() * Rand(20), py + Sign() * Rand(20) * 0.8, 30, 51, 15, 0.9, Rand(2) == 1, 0), life: 51);
            int rise = a + 2 + 40, hold = rise + Length(118, r) + 2, melt = hold + 60;
            Put(118, r, rise, px, py);
            Put(118, r + 3, hold, px, py, life: 60);
            Put(118, r + 6, melt, px, py);
            for (int i = 0; i < host._units.Length; i++)
                if (targets.Contains(i) && host._units[i] is { } u && u.Col == col && u.Row == row && struck.Add(i)) HitAt(hold + 1, i, hit);
            last = Math.Max(last, Math.Max(a + 2 + 20 + 51, melt + Length(118, r + 6)));
        }
        foreach (int ti in targets) if (struck.Add(ti)) HitAt(45, ti, hit);
        Later(0, () => host.Btl.CenterOn(aim.X, aim.Y));
        return last + 1;
    }

    private static readonly int[] FrozenIceNeeds = [190, 192, 118];

    /// <summary>
    /// 문 라이트(공격 124, 핸들러 <c>0x1004c480</c>) — 화면 가운데에 달 517:1 → 517:2(120틱까지) → 517:3 이 뜨고, 판 위의 적마다 0~49틱 사이 아무 때
    /// 달빛 517:0(94틱, 소리 1905, 자식 666 · 667 · 668)이 그 발치에 내려 한 틱 뒤 친다. 둘레(±40)에서 빛 65:0 이 솟는다(원본 백 개 — 여기서는 서른).
    /// 판 밖의 대상은 바로 맞는다.
    /// </summary>
    private int MoonLight(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        int m1 = 10, m2 = m1 + Length(517, 1) + 2, m3 = m2 + 120 + 2;
        Screen(517, 1, m1, 0, 0);
        Screen(517, 2, m2, 0, 0, life: 120);
        Screen(517, 3, m3, 0, 0);
        int last = m3 + Length(517, 3);
        int Sign() => Rand(2) == 0 ? -1 : 1;
        foreach (int ti in targets)
        {
            var (tx, ty) = host.Btl.UnitFoot(host._units[ti]);
            if (!Visible((tx, ty))) { HitAt(1, ti, hit); continue; }
            int d = Rand(50);
            Put(517, 0, d, tx, ty, sounds: true);
            HitAt(d + 1, ti, hit);
            for (int k = 0; k < 30; k++)
                Rise(65, 0, d + Rand(40), tx + Sign() * Rand(40), ty + Sign() * Rand(40) * 0.8 - 4 * Rand(20) * 0.6, Rand(10) + 10, 0.9);
            last = Math.Max(last, d + Math.Max(Length(517, 0), 40 + Length(65, 0)));
        }
        return last + 1;
    }

    private static readonly int[] MoonLightNeeds = [517, 666, 667, 668, 65];

    /// <summary>
    /// 쇼크 웨이브(공격 143~151, 핸들러 <c>0x1004d3f0</c>) — 소리 1896. 시전자의 화면 자리에서 충격파 400 이 화면 끝으로 퍼진다 —
    /// 40px 마다 위(400:0) · 아래(400:2)로, 32px 마다 왼쪽 · 오른쪽(400:1, 오른쪽은 뒤집어)으로, 빠르기 25 × 0.9(최소 1)로 느려지며.
    /// 대상마다 발치에 터짐 363:0 이 서며 다음 틱(t0+1)에 친다.
    /// </summary>
    private int ShockWave(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        Sound(1896, 0);
        var (left, top, _, _) = View;
        double hw = host.ViewWidth / 2.0, hh = host.ViewHeight / 2.0;
        var (hx, hy) = _home;
        double sx = hx - left - hw, sy = hy - top - hh;
        // 원본은 끝까지 1px 씩 기어가 수백 틱이 걸린다 — 여기서는 90틱에서 끊는다.
        const int cap = 90;
        void Wave(int motion, (double X, double Y) from, (double X, double Y) to, bool mirror = false)
        {
            var p = Move(from, to, 25, 0.9).Take(cap).Prepend(from).ToList();
            Along(400, motion, 0, AsPath(p, 2), life: p.Count, mirror: mirror, screen: true, fadeFrom: 1, fadeTo: 0.4);
        }
        for (double x = -hw; x < hw + 40; x += 40)
        {
            Wave(0, (x, sy), (x, -hh));
            Wave(2, (x, sy), (x, hh));
        }
        for (double y = -hh; y < hh + 40; y += 32)
        {
            Wave(1, (sx, y), (-hw, y));
            Wave(1, (sx, y), (hw, y), mirror: true);
        }
        int last = cap + 1;
        foreach (var (ti, (tx, ty)) in Points(targets, aim))
        {
            Put(363, 0, 0, tx, ty);
            if (ti >= 0) HitAt(1, ti, hit);
            last = Math.Max(last, Length(363, 0));
        }
        return last + 1;
    }

    private static readonly int[] ShockWaveNeeds = [400, 363];

    /// <summary>
    /// 에너지 필드(공격 152~160, 핸들러 <c>0x1004d850</c>) — 소리 1906. 겨눈 칸 가운데에서 범위 칸마다 보이지 않는 타이머가 걸어 나가(빠르기 1 × 1.1)
    /// 닿으면 그 칸에 기운 81:6 이 솟고, 이어 기운 81:r 이 칸에 남았다가 81:r+3 으로 흩어진다.
    /// 원본의 남는 기운(40 차례)은 차례마다 그 칸의 기계(종족 2 — 마장기 · 탱크)를 1/50 로 상태 2 에 건다(메시지 14). 원본에는 피해 묶음이 없다 — 여기서는 기운이 남는 틱에 그 칸의 대상을 한 번 친다.
    /// </summary>
    private int EnergyField(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        Sound(1906, 0);
        var area = cells.Count > 0 ? cells : [((int)aim.X / TileW, host.RowAt((int)aim.X, (int)aim.Y))];
        int last = 0;
        var struck = new HashSet<int>();
        foreach (var (col, row) in area)
        {
            var (px, py) = CellFoot(col, row);
            int r = Rand(3);
            int a = Move(aim, (px, py), 1, 1.1).Count;
            int burst = a + 2, stay = burst + Length(81, 6) + 2, hold = 60 + Rand(30), fade = stay + hold + 2;
            Put(81, 6, burst, px, py, sounds: true);
            Put(81, r, stay, px, py, life: hold);
            Put(81, r + 3, fade, px, py);
            for (int i = 0; i < host._units.Length; i++)
                if (targets.Contains(i) && host._units[i] is { } u && u.Col == col && u.Row == row && struck.Add(i)) HitAt(stay + 1, i, hit);
            last = Math.Max(last, fade + Length(81, r + 3));
        }
        foreach (int ti in targets) if (struck.Add(ti)) HitAt(20, ti, hit);
        Later(0, () => host.Btl.CenterOn(aim.X, aim.Y));
        return last + 1;
    }

    private static readonly int[] EnergyFieldNeeds = [81];

    // ---- C ----

    // ---- 회복 · 축복 마법(힐 · 블래스 · 큐어 · 힐 윈드 · 썬 라이트 · 스피디) — 볼트 분석-파트1-기술-b 9.11~9.16 ----

    /// <summary>힐(공격 162~170)이 쓰는 파트1 그림.</summary>
    private static readonly int[] HealNeeds = [52, 66, 65];

    /// <summary>블래스(171~179)가 쓰는 파트1 그림.</summary>
    private static readonly int[] BlessNeeds = [29, 55, 65];

    /// <summary>큐어(180~188)가 쓰는 파트1 그림.</summary>
    private static readonly int[] CureNeeds = [56, 60, 61, 62, 35];

    /// <summary>힐 윈드(189~197)가 쓰는 파트1 그림(59 는 자식 59:1 을 스스로 단다).</summary>
    private static readonly int[] HealWindNeeds = [59, 65, 52];

    /// <summary>썬 라이트(198)가 쓰는 파트1 그림.</summary>
    private static readonly int[] SunLightNeeds = [194, 35, 29, 34, 63, 197, 54];

    /// <summary>스피디(199~207)가 쓰는 파트1 그림(85:1 이 자식 86 을 단다).</summary>
    private static readonly int[] SpeedyNeeds = [85, 86];

    /// <summary>
    /// 힐 — 핸들러 <c>0x1004ecd0</c>. 소리 7, 대상마다 머리 위(z+40)에 빛 52:0(89틱)이 20틱 뒤 서며 그 한 틱 뒤 친다(시작 때 맞기).
    /// 대상 발치에 반지름 40 · 33 · 26 · 19 의 원 넷을 바깥에서 안으로 돌며 10점마다 한 틱씩 빛알 66:0 을 찍고(모두 96곳),
    /// 그 자리에서 66:0 둘이 흔들리며 솟는다(0틱 · 15틱 뒤). 20틱부터 한 틱에 하나씩 불티 65:0 백 개가 솟는다(빠르기 20 × 0.9).
    /// </summary>
    /// <remarks>원본과 다르게 한 것: 솟는 불티 백 개는 쉰으로 줄였다(두 틱에 하나).</remarks>
    private int Heal(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        if (targets.Count > 0) Sound(7, 0);
        int last = 0;
        foreach (var (ti, (tx, ty)) in Points(targets, aim))
        {
            Put(52, 0, 20, tx, ty - 40 * 0.6, life: 89);
            if (ti >= 0) HitAt(21, ti, hit);
            // 원 넷 — 래스터 원(8r 점)을 위에서 시계 방향으로, 10점마다 한 자리(k 는 원을 건너가도 이어진다).
            int k = 0;
            for (int r = 40; r > 12; r -= 7)
            {
                int n = 8 * r;
                for (int i = 0; i < n; i += 10, k++)
                {
                    double a = -Math.PI / 2 + 2 * Math.PI * i / n;
                    double px = tx + r * Math.Cos(a), py = ty + r * Math.Sin(a) * 0.8;
                    Put(66, 0, k, px, py, life: 24);
                    for (int j = 0; j < 2; j++)
                        Along(66, 0, k + 15 * j, HealSparkPath(px, py, 0, 10, 0.9, 24, up: true), life: 24);
                }
            }
            for (int i = 0; i < 100; i += 2)
                Rise(65, 0, 20 + i, tx + (Rand(2) == 0 ? -1 : 1) * Rand(20), ty + (Rand(2) == 0 ? -1 : 1) * Rand(20) * 0.8, 20, 0.9, 24);
            last = Math.Max(last, Math.Max(20 + 89, Math.Max(k + 15 + 24, 20 + 100 + 24)));
        }
        return last + 1;
    }

    /// <summary>
    /// 블래스 — 핸들러 <c>0x1004f1c0</c>. 소리 13, 대상 머리 위(z+40)에 빛 29:0(93틱)이 서고 그것이 <b>사라질 때</b> 친다. 이어 검은 빛 55:0(89틱).
    /// 대상 둘레 반지름 45 원의 5점마다(72곳) 20틱에 불티 65:0 이 흔들리며 솟고(24틱), 그것이 끝난 뒤 22틱에 같은 자리 z+100 에서 65:0 이 내려앉는다.
    /// 그 뒤 20틱 간격으로 다섯 번, 대상 위 높이 110 · 100 · 90 · 80 · 70 에서 빛알 65:2 가 그 원의 점으로 흩어져 내린다(빠르기 6 × 0.9).
    /// </summary>
    /// <remarks>원본과 다르게 한 것: 흩어져 내리는 빛알은 원의 점 하나 걸러(360 → 180)만 낸다.</remarks>
    private int Bless(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        if (targets.Count > 0) Sound(13, 0);
        int last = 0;
        foreach (var (ti, (tx, ty)) in Points(targets, aim))
        {
            Put(29, 0, 0, tx, ty - 40 * 0.6, life: 93);
            if (ti >= 0) HitAt(94, ti, hit);
            Put(55, 0, 94, tx, ty - 40 * 0.6, life: 89);
            int n = 8 * 45, k = 0;
            for (int i = 0; i < n; i += 5, k++)
            {
                double a = -Math.PI / 2 + 2 * Math.PI * i / n;
                double px = tx + 45 * Math.Cos(a), py = ty + 45 * Math.Sin(a) * 0.8;
                Along(65, 0, 20, HealSparkPath(px, py, 0, 10, 0.9, 24, up: true), life: 24);
                // 솟는 것이 끝나고(44틱) 22틱 뒤 — z+100 에서 바닥까지 흔들리며 내려앉는다(24틱).
                Along(65, 0, 66, HealSparkPath(px, py, 100, 10, 0.9, 24, up: false), life: 24);
                if (k % 2 != 0) continue;
                for (int s = 0; s < 5; s++)
                {
                    (double X, double Y) from = (tx, ty - (110 - 10 * s) * 0.6);
                    var flight = Move(from, (px, py), 6, 0.9);
                    int at = 66 + 20 * s + Rand(10);
                    Along(65, 2, at, AsPath(flight.Prepend(from).ToList(), 2), life: flight.Count);
                    last = Math.Max(last, at + flight.Count);
                }
            }
            last = Math.Max(last, 94 + 89);
        }
        return last + 1;
    }

    /// <summary>
    /// 큐어 — 핸들러 <c>0x1004f710</c>. 소리 10, 대상 머리 위(z+40)에 빛 56:0(89틱)이 서고 그것이 <b>사라질 때</b> 친다.
    /// 둘레에 빛방울 60~62:0 여섯(x ± 20, 높이 0~59, 5 + 3i + 0~19틱 뒤). 발치 반지름 30 원을 아무 점에서 10점씩 두 바퀴 남짓(30~39걸음) 두 번 돌며,
    /// 걸음마다 불티 35:0 둘(0틱 · 30틱 뒤)이 흔들리며 솟는다. 걸음 수는 두 번째 돌기까지 이어 센다.
    /// </summary>
    private int Cure(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        if (targets.Count > 0) Sound(10, 0);
        int last = 0;
        foreach (var (ti, (tx, ty)) in Points(targets, aim))
        {
            Put(56, 0, 0, tx, ty - 40 * 0.6, life: 89);
            if (ti >= 0) HitAt(90, ti, hit);
            for (int i = 0; i < 6; i++)
                Put(60 + Rand(3), 0, 5 + 3 * i + Rand(20), tx + (Rand(2) == 0 ? -1 : 1) * Rand(20), ty - Rand(60) * 0.6, life: 28);
            int n = 8 * 30, step = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int at = Rand(n), count = Rand(10) + 30;
                for (int c = 0; c < count; c++, step++)
                {
                    double a = -Math.PI / 2 + 2 * Math.PI * at / n;
                    double px = tx + 30 * Math.Cos(a), py = ty + 30 * Math.Sin(a) * 0.8;
                    for (int j = 0; j < 2; j++)
                        Along(35, 0, step + 30 * j, HealSparkPath(px, py, 0, 10, 0.9, 24, up: true), life: 24);
                    at += 10;
                    if (at > n) at = 0;
                }
            }
            last = Math.Max(last, Math.Max(89, step + 30 + 24));
        }
        return last + 1;
    }

    /// <summary>
    /// 힐 윈드 — 핸들러 <c>0x1004fb90</c>. 소리 195, 바람 둘(59:0 · 59:2)이 보이는 판의 왼위 · 오른아래 구석에서 떠올라 높이 600(화면 360px)에서
    /// 반지름 500 · 300 의 큰 원을 두 번씩(원마다 보이는 판의 아무 자리를 가운데로, 5칸 아래로) 빠르기 40 으로 돌고 오른위 · 왼아래 구석으로 내려간다.
    /// 지나는 길에 불티 65:2 를 뿌린다. 우리 편은 처음부터 머리 위(z+40)에 빛 52:0(445틱)이 서고, 356틱에 소리 196,
    /// 356 + 0~19틱에 발치를 빛알 65:2 가 반지름 30 으로 110틱 돌며 그 한 틱 뒤 친다. 판 밖의 대상은 바로 맞는다.
    /// </summary>
    /// <remarks>
    /// 원본은 판 위의 <b>우리 편 모두</b>(시전자 포함)를 고르고 큰 인물(+0xb9 = 1)은 반지름 60 · 120틱으로 돈다 — 여기서는 받은 대상만, 모두 30 · 110틱.
    /// 바람이 뿌리는 불티(원본의 스크립트 생성기 <c>0x10078a90</c>, 65:2)는 3틱마다 하나로 어림했다.
    /// </remarks>
    private int HealWind(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        var (l, t, r, b) = View;
        Sound(195, 0);
        int last = 0;
        for (int m = 0; m < 2; m++)
        {
            // 원 넷(500 → 300 을 두 번) — 원마다 100점에 한 마디, 첫 바람은 위에서 시계 방향, 둘째는 반대로.
            var nodes = new List<(double X, double Y)>();
            for (int round = 0; round < 2; round++)
                foreach (int radius in new[] { 500, 300 })
                {
                    double cx = l + Rand(Math.Max(1, r - l)), cy = t + Rand(Math.Max(1, b - t)) + 5 * TileH;
                    int n = 8 * radius;
                    for (int i = 0; i < n; i += 100)
                    {
                        int at = m == 0 ? i : n - 1 - i;
                        double a = -Math.PI / 2 + 2 * Math.PI * at / n;
                        nodes.Add((cx + radius * Math.Cos(a), cy + radius * Math.Sin(a) * 0.8 - 600 * 0.6));
                    }
                }
            (double X, double Y) start = m == 0 ? (l, t) : (r, b);
            nodes.Add(m == 0 ? (r, t) : (l, b));
            var path = new List<(double X, double Y)> { start };
            var at0 = start;
            foreach (var node in nodes) { path.AddRange(Move(at0, node, 40, 1.0)); at0 = node; }
            Along(59, m == 0 ? 0 : 2, 0, AsPath(path, 2), life: path.Count);
            for (int k = 0; k < path.Count; k += 3) Put(65, 2, k, path[k].X + Rand(21) - 10, path[k].Y + Rand(21) - 10);
            last = Math.Max(last, path.Count + Length(65, 2));
        }
        Sound(196, 356);
        foreach (int ti in targets)
        {
            var (ux, uy) = host.Btl.UnitFoot(host._units[ti]);
            if (!Visible((ux, uy))) { HitAt(1, ti, hit); continue; }
            Put(52, 0, 0, ux, uy - 40 * 0.6, life: 445);
            int d = 356 + Rand(20);
            Along(65, 2, d, Circle(ux, uy, 30, 110, 10, 1.0, false, -Math.PI / 2), life: 110);
            HitAt(d + 1, ti, hit);
            last = Math.Max(last, Math.Max(445, d + 110));
        }
        return last + 1;
    }

    /// <summary>
    /// 썬 라이트 — 핸들러 <c>0x100503d0</c>. 소리 1920, 화면 가운데에 194:12 → 13(400틱) → 14. 보이는 인물마다 화면 위까지 닿는 빛기둥
    /// (194 — 우리 편 모션 0~5 · 적 6~11: 서기 11틱 → 머묾 372틱 → 사라짐 19틱, 몸 조각은 z+234 부터 50 씩).
    /// 우리 편: 불티 35:0 스물이 솟고, 104틱에 빛 29:0(279틱)이 서며 그 93~185틱 사이 아무 때 친다. 빛알 35:2 쉰이 기둥 꼭대기에서 z+80 까지 내려오고
    /// 마지막 것이 닿으면 63:2 열이 z+100 에서 반지름 30 으로 122틱 돈다.
    /// 적: 143틱에 197:0(240틱)이 서며 그 60~179틱 사이 아무 때 친다. 내려오는 빛알은 34:2, 도는 것은 54:2. 판 밖의 인물은 바로 맞는다.
    /// </summary>
    /// <remarks>원본과 다르게 한 것: 내려오는 빛알 쉰은 서른으로 줄였다. 원본은 대상 목록 대신 판 위의 모든 인물을 훑는다(모든 칸 0x100505ff).</remarks>
    private int SunLight(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        var (_, top, _, _) = View;
        Sound(1920, 0);
        Screen(194, 12, 0, 0, 0, life: 10);
        Screen(194, 13, 12, 0, 0, life: 400);
        Screen(194, 14, 414, 0, 0);
        int last = 414 + Length(194, 14);
        foreach (int ti in targets)
        {
            var u = host._units[ti];
            var (ux, uy) = host.Btl.UnitFoot(u);
            if (!Visible((ux, uy))) { HitAt(1, ti, hit); continue; }
            bool foe = BattleScene.SeesAsFoe(_user, u);
            int k = foe ? 6 : 0;
            // 기둥 — 몸 조각(z+234+50j, 화면 위까지)과 바닥 조각. 서기 → 머묾 → 사라짐을 이어 건다.
            void Column(int motion, int start, int life)
            {
                for (int j = 0; uy - (234 + 50 * j) * 0.6 > top - 40; j++) Put(194, k + motion, start, ux, uy - (234 + 50 * j) * 0.6, life: life);
                Put(194, k + motion + 1, start, ux, uy, life: life);
            }
            Column(0, 0, 11);
            Column(2, 13, 372);
            Column(4, 387, 19);
            // 꼭대기 — 기둥 맨 위(화면 위 끝)에서 z+80 까지 빠르기 6 으로 내려온다.
            double topY = Math.Min(uy - 234 * 0.6, top), floorY = uy - 80 * 0.6;
            int fallLen = Math.Max(1, (int)Math.Ceiling((floorY - topY) / (6 * 0.6)));
            int lastFall = 0;
            for (int n = 0; n < 30; n++)
            {
                int d = Rand(70);
                Along(foe ? 34 : 35, 2, d, [(0, ux, topY), (fallLen, ux, floorY)], life: fallLen);
                lastFall = Math.Max(lastFall, d + fallLen);
            }
            for (int n = 0; n < 10; n++)
            {
                int d = lastFall + 2 + Rand(30);
                Along(foe ? 54 : 63, 2, d, Circle(ux, uy - 100 * 0.6, 30, 122, 6, 1.0, Rand(2) == 1, -Math.PI / 2), life: 122);
                last = Math.Max(last, d + 122);
            }
            if (foe)
            {
                Put(197, 0, 143, ux, uy - 40 * 0.6, life: 240);
                HitAt(143 + 60 + Rand(120) + 1, ti, hit);
                last = Math.Max(last, 143 + 240);
            }
            else
            {
                for (int n = 0; n < 20; n++)
                    Along(35, 0, Rand(30), HealSparkPath(ux + (Rand(2) == 0 ? -1 : 1) * Rand(20), uy + (Rand(2) == 0 ? -1 : 1) * Rand(20) * 0.8, 0, Rand(10) + 5, 0.95, 24, up: true), life: 24);
                Put(29, 0, 104, ux, uy - 40 * 0.6, life: 279);
                HitAt(104 + 93 + Rand(93) + 1, ti, hit);
                last = Math.Max(last, 104 + 279);
            }
            last = Math.Max(last, 387 + 19);
        }
        return last + 1;
    }

    /// <summary>스피디 — 핸들러 <c>0x100512d0</c>. 대상 머리 위(z+40)에 85:1(31틱, 소리 74 · 25틱에 자식 86)이 한 번 서고, 그 10~29틱 사이 아무 때 친다.</summary>
    private int Speedy(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        int last = 0;
        foreach (var (ti, (tx, ty)) in Points(targets, aim))
        {
            Put(85, 1, 0, tx, ty - 40 * 0.6, sounds: true);
            if (ti >= 0) HitAt(10 + Rand(20) + 1, ti, hit);
            last = Math.Max(last, Math.Max(Length(85, 1), 25 + Length(86, 0)));
        }
        return last + 1;
    }

    /// <summary>
    /// 흔들리며 솟는(내려앉는) 불티의 길(<c>0x10079200</c> · <c>0x10079330</c>) — 틱마다 x 가 −1 · 0 · +1 로 흔들리고(0~4틱마다 바뀐다)
    /// 높이 z 가 빠르기만큼 오르거나(<paramref name="up"/>) 바닥(z 0)까지 내린다. 빠르기는 곱수를 곱하고 1~40 으로 묶는다.
    /// </summary>
    private (int Tick, double X, double Y)[] HealSparkPath(double x, double y, double z, double speed, double factor, int life, bool up)
    {
        var path = new List<(int, double, double)>();
        int dir = 0, hold = 0;
        for (int t = 0; t <= life; t++)
        {
            if (t % 2 == 0 || t == life) path.Add((t, x, y - z * 0.6));
            if (hold == 0) { dir = Rand(3) == 2 ? -1 : Rand(2); hold = Rand(5); }
            else hold--;
            x += dir;
            z = up ? z + speed : Math.Max(0, z - speed);
            speed = Math.Clamp(speed * factor, 1, 40);
        }
        return [.. path];
    }

    // ---- D ----

    // ---- 배리어 · 커즈 · 쉐도우 실드 · 블러드 러스트 · 킬링 필드 · 아마게돈 (볼트 분석-파트1-기술-b 9.17~9.22) ----

    /// <summary>
    /// 배리어(공격 226~234, 핸들러 <c>0x10051640</c>) — 소리 1916. 대상마다 그 머리 위(z+40)에 막 24:0 이 서고(한 틱 뒤 친다 — 그 칸만),
    /// 발치에서 불티 35:0 스물다섯이 솟는다(빠르기 5~14 × 0.95, 0~29틱 늦게). 레벨 차이는 핸들러 안에 없다.
    /// </summary>
    private int Barrier(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        int Sign() => Rand(2) == 0 ? -1 : 1;
        Sound(1916, 0);
        int last = Length(24, 0);
        foreach (var (ti, (tx, ty)) in Points(targets, aim))
        {
            Put(24, 0, 0, tx, ty - 40 * 0.6);
            if (ti >= 0) HitAt(1, ti, hit);
            for (int n = 0; n < 25; n++)
            {
                int d = Rand(30);
                Rise(35, 0, d, tx + Sign() * Rand(20), ty + Sign() * Rand(20) * 0.8, Rand(10) + 5, 0.95);
                last = Math.Max(last, d + Length(35, 0));
            }
        }
        return last + 1;
    }

    private static readonly int[] BarrierNeeds = [24, 56, 35];

    /// <summary>
    /// 커즈(공격 235~243, 핸들러 <c>0x10051960</c>) — 대상마다 머리 위(z+40)에 저주 109(모션 0·1 중 하나)가 30틱 간격으로 셋 서고, 셋째가 서는 틱 + 1(t0+61)에 친다.
    /// 그동안 해골 108:0 셋이 대상 둘레(±40px)에서 반지름 15~24 의 작은 원을 63틱 돈다(빠르기 2, 소리 1899) — 원본은 뒤에 짧은 잔상(14틱)이 따라붙는다.
    /// </summary>
    private int Curse(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        int Sign() => Rand(2) == 0 ? -1 : 1;
        int last = 0;
        foreach (var (ti, (tx, ty)) in Points(targets, aim))
        {
            double head = ty - 40 * 0.6;
            for (int k = 0; k < 3; k++) Put(109, Rand(2), 30 * k, tx, head);
            if (ti >= 0) HitAt(61, ti, hit);
            last = Math.Max(last, 60 + Length(109, 0));
            for (int n = 0; n < 3; n++)
            {
                double cx = tx + Sign() * Rand(40), cy = head + Sign() * Rand(40) * 0.8;
                var path = Circle(cx, cy, Rand(10) + 15, 63, 2, 1.0, Rand(2) == 1, -Math.PI / 2);
                Along(108, 0, 0, path, life: 63);
                // 잔상(0x100810d0 — 빠르기 1 × 0.5 로 뒤따르다 14틱에 사라진다) — 2틱 늦은 같은 길로 어림했다.
                Along(108, 0, 2, path, life: 14, fadeFrom: 0.6, fadeTo: 0.6);
            }
            last = Math.Max(last, 63);
        }
        return last + 1;
    }

    private static readonly int[] CurseNeeds = [109, 108];

    /// <summary>
    /// 쉐도우 실드(공격 244~252, 핸들러 <c>0x10051d60</c>) — 배리어와 같은 꼴: 소리 1912, 대상 머리 위(z+40)에 그림자 막 14:0(한 틱 뒤 친다),
    /// 발치에서 그림자 192:0 스물다섯이 솟는다(빠르기 5~14 × 0.9, 0~29틱 늦게).
    /// </summary>
    private int ShadowShield(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        int Sign() => Rand(2) == 0 ? -1 : 1;
        Sound(1912, 0);
        int last = Length(14, 0);
        foreach (var (ti, (tx, ty)) in Points(targets, aim))
        {
            Put(14, 0, 0, tx, ty - 40 * 0.6);
            if (ti >= 0) HitAt(1, ti, hit);
            for (int n = 0; n < 25; n++)
            {
                int d = Rand(30);
                Rise(192, 0, d, tx + Sign() * Rand(20), ty + Sign() * Rand(20) * 0.8, Rand(10) + 5, 0.9);
                last = Math.Max(last, d + Length(192, 0));
            }
        }
        return last + 1;
    }

    private static readonly int[] ShadowShieldNeeds = [14, 192];

    /// <summary>
    /// 블러드 러스트(공격 253~261, 핸들러 <c>0x10052080</c>) — 소리 2035. 대상마다 피 기운 794:1 열이 2틱 간격으로 발치에서 솟고(빠르기 10~19 × 0.9),
    /// 여섯째가 끝나면 머리 위(z+140)에 핏덩이 794:0(96틱)이 선다. 그것이 끝나면 피 방울 794:2 열이 2틱 간격으로 대상에게 떨어지고,
    /// 같은 때 시작하는 보이지 않는 시계(수명 0~19틱)가 사라질 때 친다(<c>0x10076950</c>).
    /// </summary>
    private int BloodLust(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        int Sign() => Rand(2) == 0 ? -1 : 1;
        // 떨어지는 그림(0x10079330) — 높이 z 에서 빠르기만큼 내려와(곱수, 1~40) 바닥(대상의 높이)에 멈춘다. 모션 한 번.
        void Fall(int obs, int motion, int tick, double x, double y, double z, double speed, double factor)
        {
            int len = Length(obs, motion);
            var path = new List<(int, double, double)>();
            for (int t = 0; t <= len; t += 3)
            {
                path.Add((t, x, y - z * 0.6));
                for (int k = 0; k < 3; k++) { z = Math.Max(0, z - speed); speed = Math.Clamp(speed * factor, 1, 40); }
            }
            Along(obs, motion, tick, [.. path]);
        }
        Sound(2035, 0);
        int last = 0;
        foreach (var (ti, (tx, ty)) in Points(targets, aim))
        {
            int p5 = 0;
            for (int k = 0; k < 10; k++)
            {
                int d = 2 * k;
                Rise(794, 1, d, tx + Sign() * Rand(20), ty + Sign() * Rand(20) * 0.8 - Rand(20) * 0.6, Rand(10) + 10, 0.9);
                if (k == 5) p5 = d + Length(794, 1) - 1;
            }
            int b = p5 + 2, bEnd = b + Length(794, 0) - 1;
            Put(794, 0, b, tx, ty - 140 * 0.6);
            for (int k = 0; k < 10; k++)
                Fall(794, 2, bEnd + 2 + 2 * k, tx + Sign() * Rand(20), ty + Sign() * Rand(20) * 0.8, 140 - Rand(20), Rand(10) + 10, 0.9);
            int at = bEnd + 2 + Rand(20) + 1;
            if (ti >= 0) HitAt(at, ti, hit);
            last = Math.Max(last, Math.Max(at, bEnd + 2 + 18 + Length(794, 2)));
        }
        return last + 1;
    }

    private static readonly int[] BloodLustNeeds = [794];

    /// <summary>
    /// 킬링 필드(공격 262~270, 핸들러 <c>0x10052570</c>) — 판 위의 모든 적 가운데 HP 가 Attack +0x44(200 · 250 · 300 · 350 · 400 · 500…) 이하인 이를 즉사시킨다.
    /// 그런 적이 없으면 시전자에서 해골 229:0 이 높이 300 까지 솟고 끝난다. 있으면 한 명씩 — 카메라가 그에게 가고, 그가 맞는 자세(파트1 동작 5)를 네 번 하며
    /// 해골 229:0(소리 72)이 그 위에 서고, 51틱쯤(해골 나이 49) 죽는다. 같은 때 그의 혼(자기 그림)이 z+40 에서 z+140 으로 솟으며 바래고 카메라가 따라간다.
    /// </summary>
    /// <remarks>원본은 피해 판정 없이 HP := 0 · 처치 보상 · 죽음 연출 1012 를 직접 건다(<c>0x10086690</c>). 여기서는 그 틱에 <c>hit</c> 를 부른다 — 조건에 안 맞는 대상은 치지 않는다.
    /// 카메라가 옮겨 가는 시간(원본 카메라 대기열)은 20틱으로 어림했다.</remarks>
    private int KillingField(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        int[] limits = [200, 250, 300, 350, 400, 500, 500, 500, 500];
        int cap = limits[Math.Clamp(id - 262, 0, limits.Length - 1)];
        var victims = targets.Where(ti => host._units[ti] is { } u && u.Hp <= cap).ToList();
        if (victims.Count == 0)
        {
            var (hx, hy) = _home;
            var up = Move(_home, (hx, hy - 300 * 0.6), 1, 1.1);
            Along(229, 0, 0, AsPath(up.Prepend(_home).ToList(), 2), life: Length(229, 0), sounds: true);
            return Length(229, 0) + 1;
        }
        int start = 0, last = 0;
        foreach (int ti in victims)
        {
            var u = host._units[ti];
            var (fx, fy) = host.Btl.UnitFoot(u);
            Later(start, () => host.Btl.CenterOn(fx, fy));
            int a = start + 20;
            Later(a, () => Act(u, 5, 2, 4));
            Put(229, 0, a + 2, fx, fy, sounds: true);
            HitAt(a + 51, ti, hit);
            // 혼 — 맞은 이의 그림(파트1 동작 5 앞)이 빠르기 10 × 0.85 로 z+140 의 점까지 솟으며 6틱마다 한 단계씩 7 → 1 로 바랜다.
            int body = u.Data?.SpriteId ?? 0;
            if (body > Part1Game.ObsBase && host.UiFor(body) != null)
            {
                (double X, double Y) low = (fx, fy - 40 * 0.6);
                var rise = Move(low, (fx, fy - 140 * 0.6), 10, 0.85);
                Along(body, BodyMotion(5, 2).Motion, a + 51, AsPath(rise.Prepend(low).ToList()), life: 36, fadeFrom: 7 / 8.0, fadeTo: 1 / 8.0, rawObs: true);
            }
            Later(a + 51, () => host.Btl.CenterOn(fx, fy - 140 * 0.6));
            last = Math.Max(last, Math.Max(a + 2 + Length(229, 0), a + 51 + 36));
            start = a + 51 + 11 + 2;
        }
        return last + 1;
    }

    private static readonly int[] KillingFieldNeeds = [229];

    /// <summary>
    /// 아마게돈(공격 276, 핸들러 <c>0x10053230</c>) — 소리 1904. 넋 38:0 이 시전자 z+60 에서 z+140 으로 휘어 오르고, 닿으면 보이는 판의 모든 인물(시전자 말고)에게
    /// 넋이 하나씩 0~99틱 늦게 휘어 날아간다(× 1.02). 닿은 자리에 터짐 123:2(소리 62)가 서며 한 틱 뒤 친다 — 둘레에 넋 38 열이 솟고 불티 35 가 흩어진다.
    /// 화면 네 끝의 아무 칸으로도 넋 넷이 날아간다(꾸밈). 보이지 않는 대상은 바로 맞는다.
    /// </summary>
    private int Armageddon(int id, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        int Sign() => Rand(2) == 0 ? -1 : 1;
        // 휘어 나는 이동기(0x1007dfc0) — 조절점은 곧은 길 둘레의 아무 점 둘로 어림했다.
        List<(double X, double Y)> Curve((double X, double Y) from, (double X, double Y) to, double factor)
        {
            (double X, double Y) Mid(double f) => (from.X + (to.X - from.X) * f + Sign() * (Rand(60) + 20), from.Y + (to.Y - from.Y) * f + Sign() * (Rand(40) + 10));
            return CurveWalk([from, Mid(1 / 3.0), Mid(2 / 3.0), to], 1, factor, 70);
        }
        var (hx, hy) = _home;
        Sound(1904, 0);
        (double X, double Y) from = (hx, hy - 60 * 0.6), orb = (hx, hy - 140 * 0.6);
        var p0 = Curve(from, orb, 1.06);
        Along(38, 0, 0, AsPath(p0.Prepend(from).ToList(), 2), life: p0.Count);
        int go = p0.Count + 2, last = go;
        foreach (int ti in targets)
        {
            (double X, double Y) p = host.Btl.UnitFoot(host._units[ti]);
            if (!Visible(p)) { HitAt(1, ti, hit); continue; }
            int d = go + Rand(100);
            var flight = Curve(orb, p, 1.02);
            Along(38, 0, d, AsPath(flight.Prepend(orb).ToList(), 2), life: flight.Count);
            int boom = d + flight.Count + 2;
            Put(123, 2, boom, p.X, p.Y, sounds: true);
            HitAt(boom + 1, ti, hit);
            for (int n = 0; n < 10; n++)
                Rise(38, 0, boom + Rand(10), p.X + Sign() * Rand(20), p.Y + Sign() * Rand(20) * 0.8 - Rand(20) * 0.6, Rand(5) + 3, 1.0);
            // 원본은 스물 — 열둘로 줄였다.
            for (int n = 0; n < 12; n++) Debris(35, 0, boom + Rand(10), p.X + Sign() * Rand(20), p.Y + Sign() * Rand(20) * 0.8, Rand(8) + 5, 0.9);
            last = Math.Max(last, boom + Math.Max(Length(123, 2), 10 + Length(38, 0)));
        }
        // 화면 네 끝(위 · 오른 · 아래 · 왼)의 아무 칸으로 — 높이 +100.
        var (l, t, r, b) = View;
        (double X, double Y)[] edges =
        [
            (l + Rand(r - l), t + 20), (r - 20, t + Rand(b - t)), (l + Rand(r - l), b - 20), (l + 20, t + Rand(b - t)),
        ];
        foreach (var e in edges)
        {
            int d = go + Rand(100);
            (double X, double Y) to = (e.X, e.Y - 100 * 0.6);
            var flight = Curve(orb, to, 1.02);
            Along(38, 0, d, AsPath(flight.Prepend(orb).ToList(), 2), life: flight.Count);
            last = Math.Max(last, d + flight.Count);
        }
        return last + 1;
    }

    private static readonly int[] ArmageddonNeeds = [38, 123, 35];
}
