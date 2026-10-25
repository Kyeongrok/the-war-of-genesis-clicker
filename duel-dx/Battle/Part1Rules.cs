using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 판의 수치 식 — 사용자 결정 「중간안」대로 판·맵·사건은 파트2 틀을 쓰고 피해만 파트1 식(<see cref="Part1Combat"/>)으로 돌린다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 9·10절. 피해 = 세기 ÷ 방어(AC, 마법이면 MR) · ±1/6 흔들기 · 무기 치명 × 2.
/// 회피는 대상이 어빌리티 77 을 가졌을 때만 굴린다. 능력치는 소울 게이지로 깎는다(<see cref="Part1Combat.Scaled"/> — 파트2 SOUL 을 그 게이지로 쓴다).
/// 기본 공격(파트2 의 기본 work 를 빌린다)은 무기 갈래의 기본 공격 칸(<see cref="Part1Item.BasicAttack"/> — 칼공격 1 · 활공격 4 · 단발사격 5 …)으로,
/// 파트1 기술(work 20000 + Attack.dat 번호)은 그 칸의 식으로 친다.
/// </remarks>
internal sealed class Part1Rules(Func<int, Part1Chr?> load)
{
    private IReadOnlyList<Part1Item>? _items;
    private IReadOnlyList<Part1Attack>? _attacks;

    /// <summary>파트1 기술(work 20000 + <c>Attack.dat</c> 번호 — 헬 스트라이크 20288 따위)의 Attack.dat 칸. 기본 공격이면 null.</summary>
    internal Part1Attack? AttackOf(WorkData w) =>
        w.Id > Part1Game.ChrBase && Part1Attack.ById(_attacks ??= Part1Attack.LoadAll(), Part1SkillBook.AttackIdOf(w.Id)) is { } attack ? attack : null;
    private readonly Dictionary<int, Part1Fighter?> _fighters = [];

    /// <summary>탈것 → 탄 이(전투가 채운다). 탈것의 SKILL 은 탄 이의 SKILL 이다(<c>0x100364b0</c> → <c>0x1009eb20</c>, 255 에서 자름).</summary>
    internal Func<UnitState, UnitState?>? RiderOf;

    internal Part1Fighter? Fighter(UnitState u)
    {
        if (u.ChrCode < Part1Game.ChrBase) return null;
        if (RiderOf?.Invoke(u) is { } rider && Fighter(rider) is { } pilot && MountFighter(u) is { } mount)
        {
            var fields = (int[])mount.Chr.Fields.Clone();
            fields[Part1Chr.Skill] = Math.Min(255, pilot.Stat(Part1Chr.Skill));
            return mount with { Chr = mount.Chr with { Fields = fields, Abilities = [.. mount.Chr.Abilities.Where(a => a.Ability != 3)] } };
        }
        return MountFighter(u);
    }

    private Part1Fighter? MountFighter(UnitState u)
    {
        if (!_fighters.TryGetValue(u.ChrCode, out var f))
        {
            _items ??= Part1Item.LoadAll();
            _fighters[u.ChrCode] = f = load(u.ChrCode - Part1Game.ChrBase) is { } chr ? Part1Fighter.From(chr, _items) : null;
        }
        return f;
    }

    /// <summary>파트1 전투 행동 500 — 그 인물 칸(레코드 차례)을 값으로(원본은 유닛마다 CChr 를 들어 그 유닛만, 리메이크는 인물 번호마다 한 벌).</summary>
    internal void SetField(UnitState u, int slot, int value)
    {
        if (MountFighter(u) is not { } f || slot < 0 || slot >= f.Chr.Fields.Length) return;
        var fields = (int[])f.Chr.Fields.Clone();
        fields[slot] = value;
        _fighters[u.ChrCode] = f with { Chr = f.Chr with { Fields = fields } };
    }

    /// <summary>자란 모습이 바뀐 인물(없으면 모두)을 다시 읽게 한다.</summary>
    internal void Forget(int? chrCode = null)
    {
        if (chrCode is { } code) _fighters.Remove(code);
        else _fighters.Clear();
    }

    /// <summary>
    /// 상태이상 기술(<c>Attack.dat</c> 번호, 선택자 10~28)이 걸리나 — <see cref="Part1Combat.StatusLands"/>(<c>0x10041960</c>, 분석-파트1-전투 18절).
    /// 세기 = 그 선택자의 세기 식 + (+0x44)². 대상이 보정 247(상태이상 막기)을 가졌으면 실패. 파트1 인물이 아니면 늘 걸린다.
    /// </summary>
    internal bool StatusLands(Random rng, UnitState a, UnitState t, int attackId)
    {
        if (Fighter(a) is not { } fa || Fighter(t) is not { } ft || Part1Attack.ById(_attacks ??= Part1Attack.LoadAll(), attackId) is not { } attack)
            return true;
        // 대상 거르기(0x10041880, 표 0x1011c084) — 선택자 25 · 27 · 28 은 인물 칸 10(+0xb8, 종족으로 보임)이 0 인 사람에게만 듣는다.
        if (attack.Formula is 25 or 27 or 28 && ft.Chr.Fields[10] != 0)
        {
            if (BattleScene.Trace)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"part1 status {a.ChrCode} → {t.ChrCode} {attack.Name}: 칸 10 = {ft.Chr.Fields[10]} 이라 안 듣는다" + Environment.NewLine);
            return false;
        }
        int power = Part1Combat.Power(attack.Formula, fa.AttackerAt(a.Soul)) + attack.PowerAdd * attack.PowerAdd;
        bool guard = ft.Equipment.Any(i => i?.Bonus(247) is not (null or 0));
        // 25(컨디션이상)는 최면 · 혼란이, 28(혼란)은 최면이 걸려 있으면 안 걸린다(0x10041a7f · 0x10041b15).
        bool blocked = attack.Formula switch
        {
            25 or 26 => HasAilment(t, Hypnosis) || HasAilment(t, Confusion),
            28 => HasAilment(t, Hypnosis),
            _ => false,
        };
        bool lands = Part1Combat.StatusLands(attack.Formula, power, ft.Stat(Part1Chr.Mr, t.Soul), ft.Stat(Part1Chr.Luck, t.Soul),
                                             a.IsAlly == t.IsAlly, guard, blocked, rng.Next(32768));
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 status {a.ChrCode} → {t.ChrCode} {attack.Name}(식 {attack.Formula}): 세기 {power} · MR {ft.Stat(Part1Chr.Mr, t.Soul)} · Luck {ft.Stat(Part1Chr.Luck, t.Soul)}{(guard ? " · 막기 247" : "")} → {(lands ? "걸림" : "안 걸림")}" + Environment.NewLine);
        return lands;
    }

    /// <summary>
    /// 속성 부여(유닛 +0x249 화염 · +0x24a 빙한 · +0x24b 전격) — 화이어 · 아이스 · 썬더 블레이드(세기 식 10 · 11 · 12)가 대상에게 건다.
    /// 걸린 동안 그 사람의 무기 공격은 그 속성이 되고 무기 E 가 ×1.2(<c>0x1003f000</c> · <c>0x1003f070</c>, 분석-파트1-전투 9절). 유닛 → 속성(1~3).
    /// </summary>
    internal readonly Dictionary<UnitState, int> Enchant = [];

    /// <summary>파트1 강화(블래스 15 · 스피디 17 · 배리어 20 · 쉐도우 실드 22 · 블러드 러스트 23) — 유닛 → 세기 식 → 남은 길이(BattleScene.Part1Support).</summary>
    internal readonly Dictionary<UnitState, Dictionary<int, int>> Buffs = [];

    /// <summary>파트1 회복 기술(세기 식 6)의 회복량 — 최대 HP × 세기 ÷ 20000(분석-파트1-기술-b 9절). 아니면 null.</summary>
    internal int? HealAmount(UnitState a, UnitState t, WorkData w) =>
        AttackOf(w) is { Formula: 6 } attack && Fighter(a) is { } fa
            ? (int)((long)t.MaxHp * (Part1Combat.Power(6, fa.AttackerAt(a.Soul)) + attack.PowerAdd * attack.PowerAdd) / 20000)
            : null;

    internal bool Has(UnitState u, int sel) => Buffs.TryGetValue(u, out var b) && b.ContainsKey(sel);

    /// <summary>파트1 상태 번호 — 유닛 바이트 +0x246 + 번호: 14 최면(+0x254) · 15 컨디션이상(+0x255) · 16 혼란(+0x256).</summary>
    internal const int Hypnosis = 14, Condition = 15, Confusion = 16;

    /// <summary>
    /// 파트1 상태 14 · 15 · 16 이 걸린 사람(분석-파트1-전투 25절). 원본은 값 1 을 넣고 줄이지 않는다 — 시간 틱마다 따로 굴려 풀린다(<see cref="RecoveryRoll"/>).
    /// </summary>
    internal readonly Dictionary<UnitState, HashSet<int>> Ailments = [];

    private static Part1Rules? s_current;
    internal Part1Rules(Func<int, Part1Chr?> load, bool current) : this(load) { if (current) s_current = this; }

    internal bool HasAilment(UnitState u, int s) => Ailments.TryGetValue(u, out var set) && set.Contains(s);

    /// <summary>혼란이면 자기 말고 모두가 적이다 — 원본은 편 판정 칸 +0x1f8 을 2(모두 적)로 바꾼다(<c>0x10042674</c> · <c>0x10038550</c>).</summary>
    internal static bool SeesAllAsFoes(UnitState u) => s_current?.HasAilment(u, Confusion) == true;

    /// <summary>
    /// 상태를 건다(<c>0x10042570</c> — 이미 걸려 있으면 그대로). 최면은 컨디션이상 · 혼란을, 혼란은 컨디션이상을 지운다(<c>0x10042648</c> · <c>0x10042674</c>).
    /// 새로 걸렸으면 true.
    /// </summary>
    internal bool AddAilment(UnitState u, int s)
    {
        if (HasAilment(u, s)) return false;
        if (!Ailments.TryGetValue(u, out var set)) Ailments[u] = set = [];
        if (s == Hypnosis) { set.Remove(Condition); set.Remove(Confusion); }
        if (s == Confusion) set.Remove(Condition);
        set.Add(s);
        return true;
    }

    internal bool ClearAilment(UnitState u, int s)
    {
        if (!Ailments.TryGetValue(u, out var set) || !set.Remove(s)) return false;
        if (set.Count == 0) Ailments.Remove(u);
        return true;
    }

    internal static int AilmentOf(int selector) => selector switch { 27 => Hypnosis, 28 => Confusion, _ => Condition };

    /// <summary>AI 가 그 대상에게 선택자 25 · 27 · 28 을 쓸 만한가 — 듣는 종족(칸 10 = 0)이고 그 상태가 없고 막는 상태도 없을 때.</summary>
    internal bool AilmentWorth(UnitState t, int selector) =>
        (Fighter(t) is not { } f || f.Chr.Fields[10] == 0) && !HasAilment(t, AilmentOf(selector))
        && !(selector == 25 && (HasAilment(t, Hypnosis) || HasAilment(t, Confusion))) && !(selector == 28 && HasAilment(t, Hypnosis));

    /// <summary>
    /// 시간 틱마다의 풀림 굴림(<c>0x10035150</c> → <c>0x10041c00</c>, 유닛 vt+0x54 — 아무도 움직일 수 없어 틱이 오를 때 모두에게).
    /// 컨디션이상: rand() % (최대 HP × 4) ≤ HP ÷ 10 · 최면 · 혼란(과 중독): rand() % 600 ≤ 소울 ÷ 10. 풀리면 true.
    /// </summary>
    internal static bool RecoveryRoll(int status, int hp, int maxHp, int soul, int roll) =>
        status == Condition ? roll % Math.Max(1, maxHp * 4) <= hp / 10 : roll % 600 <= soul / 10;

    /// <summary>그 기술이 속성 부여면 그 속성(1 · 2 · 3), 아니면 0.</summary>
    internal int EnchantOf(WorkData w) => AttackOf(w)?.Formula is >= 10 and <= 12 and var sel ? sel - 9 : 0;

    /// <summary>
    /// 그 공격이 맞은 자리에 타격 불꽃(Obs 796)을 띄우나 — 피해 적용 <c>0x1003df30</c> 은 세기 식(선택자) 0 · 1 · 3 · 29 · 30 일 때만 띄운다
    /// (무기로 치는 것 — 마법 · 소환은 몸짓만). 기본 공격은 무기 갈래의 기본 공격 칸으로 본다. 파트1 인물이 아니면 늘 띄운다.
    /// </summary>
    internal bool SparksOnHit(UnitState a, WorkData w)
    {
        if (Fighter(a) is not { } fa) return true;
        var attack = AttackOf(w) ?? Part1Attack.ById(_attacks ??= Part1Attack.LoadAll(), BasicAttackOf(fa));
        return (attack?.Formula ?? 0) is 0 or 1 or 3 or 29 or 30;
    }

    /// <summary>기본 공격의 Attack.dat 번호 — 인물 칸 9, 없으면 무기 갈래의 것.</summary>
    internal static int BasicAttackOf(Part1Fighter f) => f.Chr.BasicAttackId > 0 ? f.Chr.BasicAttackId : Part1Item.BasicAttack(f.Weapon1);

    /// <summary>
    /// 사거리(4분의 1칸) — <c>0x10038ae0</c>: Attack <c>+0x21</c> 갈래 1 RANGE 능력치 + 값 <c>+0x24</c> · 2 무기 <c>+0x68</c> + 보정 206 · 3 값 그대로 · 4 무기 <c>+0x68</c> + 값.
    /// 범위 칠하기(<c>0x10038bb0</c> · <c>0x10039070</c>)가 이 값을 <b>그대로</b> 모양 함수(<c>0x1008ab80</c> 등)의 크기로 넘기고, 칸 거르개 <c>0x1008a950</c> 이
    /// 4 × 맨해튼 + 높이 보정과 견준다 — 곧 칸 수 = 값 ÷ 4(분석-파트1-전투 29절). 전에는 ÷ 2 로 읽어 두 배 멀리 닿았다.
    /// </summary>
    internal int RangeQuarters(UnitState u, Part1Attack a)
    {
        if (Fighter(u) is not { } f) return a.RangeValue;
        return a.RangeKind switch
        {
            1 => f.RangeStat + a.RangeValue,
            2 => f.WeaponReach + f.RangeBonus,
            4 => f.WeaponReach + a.RangeValue,
            _ => a.RangeValue,
        };
    }

    /// <summary>
    /// 사람 손에 든 총의 사거리(4분의 1칸) — 권총 피스톨 · 킬러(<c>+0x68</c> 32, 8칸). 갈래 3 의 쏘는 기본 공격을 여기서 자른다:
    /// 사용자 요청 「록슬리의 기본 공격은 총 정도 사거리」 — 록슬리 330(용병 라이트닝볼츠) 값 32 = 8칸이라 지금은 그대로 같다.
    /// </summary>
    private const int GunReach = 32;

    /// <summary>그 인물의 기본 공격 칸(Attack.dat) — 인물 칸 9, 없으면 무기 갈래의 것(<see cref="BasicAttackOf"/>). 파트1 인물이 아니면 null.</summary>
    internal Part1Attack? BasicAttack(UnitState u) =>
        Fighter(u) is { } f ? Part1Attack.ById(_attacks ??= Part1Attack.LoadAll(), BasicAttackOf(f)) : null;

    /// <summary>기본 공격 사거리(4분의 1칸) — 인물의 기본 공격 칸(<see cref="BasicAttackOf"/>)으로 <see cref="RangeQuarters"/>. 갈래 3 은 총만큼으로 자른다.</summary>
    internal int BasicRangeQuarters(UnitState u)
    {
        if (BasicAttack(u) is not { } a) return 4;
        int q = RangeQuarters(u, a);
        return a.RangeKind == 3 ? Math.Min(q, GunReach) : q;
    }

    /// <summary>
    /// 파트1 피해 결과(<c>0x100413b0</c>) — 4 막음(피해는 들어간다, 맞는 이는 동작 6) · 5 피함(피해 0, 동작 54) ·
    /// 6 반격 자세로 맞음(맞기 몸짓 + 되받아치기) · 7 역습 자세로 피함(동작 54 + 되받아치기). 분석-파트1-전투 32절.
    /// </summary>
    internal const int Blocked = 4, Dodged = 5, CounterHit = 6, CounterDodge = 7;

    /// <summary>
    /// 파트1 자세(유닛 바이트 +0x257 방어 · +0x258 회피 · +0x259 반격 · +0x25a 역습 · +0x25b 완전방어) — 유닛 → 어빌리티 76~80.
    /// 안 움직이고 아무것도 안 한 채 휴식(명령 0x3f3 → <c>0x1003e580</c>)하면 가진 것 가운데 첫째(76 → 80 차례) 하나가 켜지고,
    /// 다음 자기 차례가 올 때 모두 지운다(<c>0x10036350</c>). 대장 · 탄 이는 안 켠다.
    /// </summary>
    internal readonly Dictionary<UnitState, int> Stances = [];

    internal int StanceOf(UnitState u) => Stances.GetValueOrDefault(u);

    /// <summary>쉬면 켜질 자세 — 어빌리티 76 · 77 · 78 · 79 · 80 가운데 가진 첫째(<c>0x1003e6e6</c>~). 없거나 파트1 인물이 아니면 0.</summary>
    internal int StanceAbility(UnitState u) =>
        Fighter(u) is { } f ? new[] { 76, 77, 78, 79, 80 }.FirstOrDefault(id => f.Chr.AbilityLevel(id) >= 0) : 0;

    /// <summary>자세 이름(어빌리티 이름 — 분석-파트1-전투 24절 A).</summary>
    internal static string StanceName(int ability) => ability switch { 76 => "방어", 77 => "회피", 78 => "반격", 79 => "역습", 80 => "완전방어", _ => "" };

    /// <summary>
    /// 피해 굴림 — 파트2 <c>GameData.Resolve</c> 와 같은 꼴(결과 2 맞음 · 3 빗나감)에 파트1 의 4 막음 · 5 피함을 더해 돌려준다. 파트1 인물이 아니면 null(파트2 식을 쓴다).
    /// </summary>
    internal (int Amount, int Result, bool Critical)? Resolve(Random rng, UnitState a, UnitState t, WorkData w)
    {
        if (!w.IsDamage || Fighter(a) is not { } fa || Fighter(t) is not { } ft) return null;
        // 기술이면 Attack.dat 의 세기 식(+0x42) · 명중률(+0x48) · 속성(+0x64) · 더하는 세기(+0x44 제곱) · 곱(+0x66)을 쓴다.
        // 상태이상 기술(선택자 10~28)은 피해가 아니어서 파트2 식으로 넘긴다. 기본 공격은 무기 갈래의 기본 공격 칸(칼 1 — 식 0 · 명중 100, 활 4 — 식 3 · 명중 90 …).
        var attack = AttackOf(w) ?? Part1Attack.ById(_attacks ??= Part1Attack.LoadAll(), BasicAttackOf(fa));
        int selector = attack?.Formula ?? 0;
        if (selector is >= 10 and <= 28) return null;
        int accuracy = attack is { Accuracy: > 0 } ? attack.Accuracy : 100;
        // 원본 차례(0x100413b0): 무기로 치는 물리(선택자 0 · 1 · 29 · 30)만 ① 회피(0x10040fa0 → 결과 5, 역습 자세 +0x25a 면 7) ② 명중 굴림(결과 3)
        // ③ 막기(0x10040eb0 → 결과 4 — 피해는 들어가고 방어에 두 손 아이템 +0x58 을 더한다). 때린 이 장비에 보정 246(회피 불능)이 있으면 ①③ 을 안 굴린다.
        // 자세(+0x257 ~ +0x25b, Stances): 회피는 77 이 있으면 DEX(회피 자세가 아니면 ½), 77 이 없어도 역습 자세면 DEX 그대로 굴린다(0x10040fa0).
        // 역습 자세로 피하면 결과 7(0x10041080). 막기는 76 이면 방어 자세일 때 ½ 없이, 80 이면 완전방어 자세일 때 굴림 없이 막는다(0x10040eb0).
        // 시험용 DUELDX_P1GUARD=block|dodge 면 굴림 없이 그렇게 된다. DUELDX_P1STANCE=76~80 이면 자세가 없는 파트1 대상을 그 자세로 본다.
        int stance = StanceOf(t);
        if (stance == 0 && int.TryParse(Environment.GetEnvironmentVariable("DUELDX_P1STANCE"), out int forcedStance) && forcedStance is >= 76 and <= 80) stance = forcedStance;
        bool guardable = selector is 0 or 1 or 29 or 30 && !fa.HasBonus(Part1Bonus.NeverMissed);
        string? forcedGuard = Environment.GetEnvironmentVariable("DUELDX_P1GUARD");
        bool rollsEvade = ft.CanEvade || stance == 79;
        if (guardable && (forcedGuard == "dodge" || (rollsEvade
            && rng.Next(100) < Part1Combat.EvadeChance(ft.Stat(Part1Chr.Dex, t.Soul), !ft.CanEvade || stance == 77, fa.HitValue, fa.Stat(Part1Chr.Dex, a.Soul)))))
            return Missed("회피", stance == 79 ? CounterDodge : Dodged);
        if (!Part1Combat.Hits(accuracy, rng.Next(32768))) return Missed("명중 굴림");
        bool blocked = guardable && (forcedGuard == "block" || Part1Combat.Blocks(ft.BlockAbility, stance != 0 && stance == ft.BlockAbility,
            ft.GuardValue + ft.Stat(Part1Chr.Dex, t.Soul), fa.HitValue, fa.Stat(Part1Chr.Dex, a.Soul), rng.Next(100)));
        // 버써크(파트1 상태 +0x24e, 분석-파트1-기술-b 8절 — 이 게임은 상태 4 「버서커」) — 걸린 동안 STR ×1.5 · AC ×0.5.
        var attacker = fa.AttackerAt(a.Soul);
        int enchant = attack == null || selector is 0 or 1 or 2 or 3 or 4 or 29 or 30 or 31 ? Enchant.GetValueOrDefault(a) : 0;
        if (enchant > 0) attacker = attacker with { Enchanted = true };
        if (a.Status(4) > 0) attacker = attacker with { Str = attacker.Str * 3 / 2 };
        int power = Part1Combat.Power(selector, attacker);
        bool weaponHit = attack == null || selector is 0 or 1 or 2 or 3 or 4 or 29 or 30 or 31;
        if (weaponHit && Has(a, 23)) power = power * 3 / 2;          // 블러드 러스트 — 무기 E ×1.5
        if (attack != null) power = (power + attack.PowerAdd * attack.PowerAdd) * Math.Max(1, attack.Hits);
        int element = attack is { Element: > 0 } ? attack.Element : enchant > 0 ? enchant : fa.Weapon1?.Element ?? 0;
        bool magic = selector is 5 or 6 or 8 or (>= 10 and <= 24);
        int defense = ft.Stat(magic ? Part1Chr.Mr : Part1Chr.Ac, t.Soul);
        if (!magic && t.Status(4) > 0) defense /= 2;
        if (Has(t, 22)) defense = magic ? defense / 2 : defense * 3 / 2;   // 쉐도우 실드 — AC ×1.5 · MR ×0.5
        if (blocked) defense += ft.BlockDefense;
        if (stance == 80 && selector != 5) defense += defense / 2;           // 완전방어 자세 — 마법(식 5) 말고는 방어 ×1.5(0x100415e1)
        int damage = Part1Combat.Damage(power, defense, ft.Resist(element), null, rng.Next(32768));
        if (Has(t, 20)) damage = selector == 5 ? 0 : damage * 2;      // 배리어 — 마법(식 5) 피해 0, 그 밖은 ×2
        // 시험용 DUELDX_P1CRIT=<%> 면 치명률을 그 값으로 덮는다(무기 덧기술을 화면 밖에서 보려고).
        int critRate = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_P1CRIT"), out int forced) ? forced : fa.Weapon1?.Critical ?? 0;
        // 반격 자세면 결과는 막았어도 6 이다(0x100417c5 가 4 를 덮는다). 치명은 결과 2 · 6 에서만 — 막은 것(4)은 굴리지 않는다(0x100417e4).
        int result = stance == 78 ? CounterHit : blocked ? Blocked : 2;
        bool crit = false;
        if (result != Blocked) damage = Part1Combat.Critical(damage, critRate, rng.Next(32768), out crit);
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 hit {a.ChrCode} → {t.ChrCode}{(attack != null ? $" {attack.Name}(식 {selector})" : "")}: 세기 {power} ÷ 방어 {defense} = {damage}{(crit ? " 치명" : "")}{(blocked ? " 막음" : "")}{(stance != 0 ? $" 자세 {stance}" : "")} → 결과 {result}" + Environment.NewLine);
        return (Math.Max(0, damage), result, crit);

        (int, int, bool) Missed(string why, int result = 3)
        {
            if (BattleScene.Trace)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                    $"part1 miss {a.ChrCode} → {t.ChrCode}{(attack != null ? $" {attack.Name}" : "")}: {why} (명중 {accuracy}, 결과 {result})" + Environment.NewLine);
            return (0, result, false);
        }
    }
}
