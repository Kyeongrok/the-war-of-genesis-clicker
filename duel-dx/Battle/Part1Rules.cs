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

    /// <summary>마법 기술인가 — 세기 식 5 · 6 · 8 · 10~24(방어 대신 MR 을 쓰는 갈래와 같다).</summary>
    internal bool IsMagic(WorkData w) => AttackOf(w)?.Formula is 5 or 6 or 8 or (>= 10 and <= 24);

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
        // 대상 거르기(0x10041880, 표 0x1011c084 — TargetFilter). 25 · 27 · 28 은 보통 사람(칸 10 = 0)이고 큰 유닛이 아닐 때만, 13 은 기계만.
        if (!TargetFilter(attack.Formula, t))
        {
            if (BattleScene.Trace)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"part1 status {a.ChrCode} → {t.ChrCode} {attack.Name}: 대상 거르기 (칸 10 = {ft.Chr.Fields[Part1Chr.KindField]} · 11 = {ft.Chr.Fields[Part1Chr.BigField]}) 라 안 듣는다" + Environment.NewLine);
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
    /// 대상 거르기 표 <c>0x1011c084</c>(<c>0x10041880(선택자)</c> 가 읽는다, 선택자 0~31 마다 3바이트, 32 이상은 늘 떨어진다). 유닛 바이트를 본다 —
    /// 첫 바이트 → <c>+0xb8</c>(인물 칸 10 종류: 0 보통 · 1 언데드 · 괴물 · 2 기계 — 마장기 · 탱크 · 썬더볼트 · 성문 · 아지다하카):
    /// 1 = 0 일 때만 · 2 = 1 일 때만 · 3 = 2 일 때만 · 4 = 2 가 아닐 때만. 둘째 → <c>+0xb9</c>(칸 11 큰 유닛), 셋째 → <c>+0xba</c>(칸 12 난다):
    /// 1 = 0 일 때만 · 2 = 0 이 아닐 때만(그 밖의 값은 거르지 않는다 — 선택자 6 의 셋째 바이트 4 는 아무 일도 안 한다).
    /// </summary>
    /// <remarks>
    /// 값: 10 · 11 · 12 · 15 · 17~21 · 23 · 26 = (4, 0, 0) · 13 = (3, 0, 0) · 14 · 22 = (4, 1, 0) · 25 · 27 · 28 = (1, 1, 0) · 6 = (0, 0, 4) · 나머지 0.
    /// 피해 길(<c>0x100413b0</c>) · 상태 길(<c>0x10042290</c>) · AI 어림(<c>0x100410a0</c> · <c>0x1003fd55</c>) 모두 맨 먼저 거르고, 편은 안 본다 —
    /// 같은 편 기계에게 블래스 · 스피디를 걸어도 「Miss」(결과 3, <c>0x1011c0f8</c>).
    /// </remarks>
    private static ReadOnlySpan<byte> FilterTable =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0,   // 0~9
        4, 0, 0, 4, 0, 0, 4, 0, 0, 3, 0, 0, 4, 1, 0, 4, 0, 0, 0, 0, 0, 4, 0, 0, 4, 0, 0, 4, 0, 0,   // 10~19
        4, 0, 0, 4, 0, 0, 4, 1, 0, 4, 0, 0, 0, 0, 0, 1, 1, 0, 4, 0, 0, 1, 1, 0, 1, 1, 0, 0, 0, 0,   // 20~29
        0, 0, 0, 0, 0, 0,   // 30~31
    ];

    /// <summary>표 <see cref="FilterTable"/> 로 그 선택자가 이 종류 · 큰 유닛 · 나는 값의 대상에게 듣나(<c>0x10041880</c>).</summary>
    internal static bool TargetFilter(int selector, int kind, int big, int fly)
    {
        if (selector is < 0 or >= 32) return false;
        var row = FilterTable.Slice(selector * 3, 3);
        bool kindOk = row[0] switch { 1 => kind == 0, 2 => kind == 1, 3 => kind == 2, 4 => kind != 2, _ => true };
        return kindOk && Flag(row[1], big) && Flag(row[2], fly);
        static bool Flag(int rule, int value) => rule switch { 1 => value == 0, 2 => value != 0, _ => true };
    }

    /// <summary>그 대상(탈것이면 탈것의 인물 칸)에게 선택자가 듣나. 파트1 인물이 아니면 늘 듣는다.</summary>
    internal bool TargetFilter(int selector, UnitState t) =>
        Fighter(t) is not { } f || TargetFilter(selector, f.Chr.Fields[Part1Chr.KindField], f.Chr.Fields[Part1Chr.BigField], f.Chr.Fields[Part1Chr.FlyField]);

    /// <summary>기계(칸 10 = 2)인가 — 회복(선택자 6)은 표와 따로 이 대상에게 0 을 준다(<c>0x10041430</c> · AI 어림 <c>0x100410f9</c>).</summary>
    internal bool IsMachine(UnitState t) => Fighter(t)?.Chr.Fields[Part1Chr.KindField] == 2;

    /// <summary>AI 가 그 대상을 그 기술의 대상으로 셀까 — 표에서 떨어지거나 기계에게 회복이면 원본 어림이 0 이다.</summary>
    internal bool AiAffects(WorkData w, UnitState t) =>
        AttackOf(w) is not { } attack || TargetFilter(attack.Formula, t) && !(attack.Formula == 6 && IsMachine(t));

    /// <summary>
    /// 속성 부여(유닛 +0x249 화염 · +0x24a 빙한 · +0x24b 전격) — 화이어 · 아이스 · 썬더 블레이드(세기 식 10 · 11 · 12)가 대상에게 건다.
    /// 걸린 동안 그 사람의 무기 공격은 그 속성이 되고 무기 E 가 ×1.2(<c>0x1003f000</c> · <c>0x1003f070</c>, 분석-파트1-전투 9절). 유닛 → 속성(1~3).
    /// </summary>
    internal readonly Dictionary<UnitState, int> Enchant = [];

    /// <summary>파트1 강화(아이스 실드 14 · 블래스 15 · 스피디 17 · 배리어 20 · 쉐도우 실드 22 · 블러드 러스트 23) — 유닛 → 세기 식 → 남은 길이(BattleScene.Part1Support).</summary>
    internal readonly Dictionary<UnitState, Dictionary<int, int>> Buffs = [];

    /// <summary>
    /// <see cref="Buffs"/> 에서 집중(Attack 378, <c>0x1006cad0</c>)의 열쇠 — 세기 식(0)이 아니라 상태 22(+0x25c)라 1000 을 더해 겹치지 않게 둔다.
    /// 핸들러가 t0 에 +0x25c := 40 을 바로 쓰고(<c>0x1006cbd7</c>, 맞히는 사건 없음), 빈 시간 틱마다 1 씩 준다(<c>0x10035150</c> 둘째 목록 3~12 · 22).
    /// 0 이 아닌 동안 사거리 칠하기(<c>0x10039070</c>)가 Attack +0x2d(사거리에 높이 차 넣기)를 0 으로 본다 — 높이 차가 사거리를 안 깎는다.
    /// 큐어(<c>0x10042150</c>)는 22 를 안 지운다.
    /// </summary>
    internal const int FocusStatus = 1022;

    /// <summary>파트1 회복 기술(세기 식 6)의 회복량 — 최대 HP × 세기 ÷ 20000(분석-파트1-기술-b 9절). 아니면 null.
    /// 기계(칸 10 = 2)에게는 0 — 원본은 그 0 을 「Miss」로 띄운다(<c>0x10041430</c> → <c>0x1003dc30</c>).</summary>
    internal int? HealAmount(UnitState a, UnitState t, WorkData w) =>
        AttackOf(w) is { Formula: 6 } attack && Fighter(a) is { } fa
            ? IsMachine(t) ? 0 : (int)((long)t.MaxHp * (Part1Combat.Power(6, fa.AttackerAt(a.Soul)) + attack.PowerAdd * attack.PowerAdd) / 20000)
            : null;

    /// <summary>
    /// AI 가 기술을 고를 때 견주는 세기 — 피해 기술이면 (세기 식 + 더하는 세기²) × 타수 × 명중률(방어 · 속성은 대상마다라 뺀다), 아니면 0.
    /// 화이어 필드 · 프로즌 아이스(<see cref="TemperatureSkill"/>)는 세기 식 5 지만 피해가 없다 — 원본 AI 어림(<c>0x1003ff40</c> 의 <c>0x100400bd</c> · <c>0x10040296</c> 갈래)도
    /// 피해가 아니라 온도로 잰다(BattleScene.Part1TemperatureValue). 그래서 0.
    /// </summary>
    internal int AiStrength(UnitState a, WorkData w) =>
        w.IsDamage && AttackOf(w) is { } attack && attack.Formula is not (>= 10 and <= 28) && TemperatureSkill(attack) == 0 && Fighter(a) is { } fa
            ? (int)Math.Min(int.MaxValue, (long)(Part1Combat.Power(attack.Formula, fa.AttackerAt(a.Soul)) + attack.PowerAdd * attack.PowerAdd)
                                          * Math.Max(1, attack.Hits) * (attack.Accuracy > 0 ? attack.Accuracy : 100) / 100 * attack.DamagePercent / 100)
            : 0;

    /// <summary>AI 가 보는 AC — <c>0x1003edc0</c>: 바탕 AC(<c>0x1009ebf0</c>), 쉐도우 실드(+0x251) ×1.5, 버써크(+0x24e) ½, 그 뒤 소울 게이지로 깎는다.</summary>
    internal int AiAc(UnitState t)
    {
        if (Fighter(t) is not { } f) return 0;
        int ac = f.Stat(Part1Chr.Ac);
        if (Has(t, 22)) ac = ac * 3 / 2;
        if (t.Status(4) > 0) ac /= 2;
        return Part1Combat.Scaled(ac, t.Soul);
    }

    /// <summary>AI 가 보는 MR — <c>0x1003ed50</c>: 쉐도우 실드 ½, 소울 게이지로 깎는다.</summary>
    internal int AiMr(UnitState t)
    {
        if (Fighter(t) is not { } f) return 0;
        int mr = f.Stat(Part1Chr.Mr);
        if (Has(t, 22)) mr /= 2;
        return Part1Combat.Scaled(mr, t.Soul);
    }

    /// <summary>AI 가 보는 DEX — <c>0x1003ec10</c>(소울 게이지로 깎은 값).</summary>
    internal int AiDex(UnitState t) => Fighter(t) is { } f ? f.Stat(Part1Chr.Dex, t.Soul) : 0;

    /// <summary>
    /// AI 어림의 대상 하나 값 — <c>0x100410a0(대상, 서술, AI 표 +0x24)</c>. 서술은 세기 식(<c>0x1003f3c0</c>) + 더하는 세기²(<c>0x10040694</c>).
    /// 선택자 8 은 적이면 5 · 아니면 6 으로 보고(<c>0x100410c8</c>), 6 = 회복: 기계(칸 10 = 2)는 0, 아니면 min(최대 HP·(세기/50)/600, 잃은 HP) · 잃은 HP / 최대 HP · 2,
    /// 9 = 세기 그대로, 0~7 · 29~31 = 피해: <paramref name="resist"/>(AI 표 +0x24)면 속성 저항 ×/10, 방어(표 <c>0x10041368</c> — 5 는 MR, 6 은 없음, 그 밖 AC)로 나누고,
    /// 대상 HP 보다 크면 HP × 1.5. 그 밖(상태 기술 10~28)은 0. 끝에 ±1/6 을 흔든다(<c>0x10041323</c>, rand 한 번).
    /// [H] 서술 +0xc 의 ×/100 · 둘째 속성(+4) 저항은 리메이크 피해 식(<see cref="Resolve"/>)에 없어 뺐다. 타수는 부르는 쪽이 곱한다(<c>0x10040985</c>).
    /// </summary>
    internal int AiDamage(UnitState a, UnitState t, WorkData w, bool resist, bool foe, int roll)
    {
        if (Fighter(a) is not { } fa || Fighter(t) is not { } ft) return 0;
        var attack = AttackOf(w) ?? Part1Attack.ById(_attacks ??= Part1Attack.LoadAll(), BasicAttackOf(fa));
        int selector = attack?.Formula ?? 0;
        if (selector == 8) selector = foe ? 5 : 6;
        int value;
        if (selector == 6 || selector == 9 || selector is (>= 0 and <= 7) or (>= 29 and <= 31))
        {
            var attacker = fa.AttackerAt(a.Soul);
            int enchant = attack == null || selector is 0 or 1 or 2 or 3 or 4 or 29 or 30 or 31 ? Enchant.GetValueOrDefault(a) : 0;
            if (enchant > 0) attacker = attacker with { Enchanted = true };
            if (a.Status(4) > 0) attacker = attacker with { Str = attacker.Str * 3 / 2 };
            int power = Part1Combat.Power(selector, attacker);
            if ((attack == null || selector is 0 or 1 or 2 or 3 or 4 or 29 or 30 or 31) && Has(a, 23)) power = power * 3 / 2;
            if (attack != null) power += attack.PowerAdd * attack.PowerAdd;
            if (selector == 6)
            {
                int lost = t.MaxHp - t.Hp;
                value = IsMachine(t) || t.MaxHp <= 0 ? 0 : Math.Min(t.MaxHp * (power / 50) / 600, lost) * lost / t.MaxHp * 2;
            }
            else if (selector == 9) value = power;
            else
            {
                int element = attack is { Element: > 0 } ? attack.Element : enchant > 0 ? enchant : fa.Weapon1?.Element ?? 0;
                if (resist && ft.Resist(element) is { } r1) power = power * r1 / 10;
                int defense = selector == 5 ? AiMr(t) : AiAc(t);
                value = defense != 0 ? power / defense : power;
                if (value > t.Hp) value = t.Hp * 3 / 2;
            }
        }
        else value = 0;
        int jitter = value / 3;
        if (jitter != 0) value += roll % jitter - value / 6;
        return value;
    }

    /// <summary>이번 기본 공격을 타수만큼 나눠 친다 — 그동안 타수를 세기에 곱하지 않는다(BattleScene 이 켜고 끈다).</summary>
    internal bool SplitHits;

    /// <summary>연사를 발마다 나눠 칠 때의 발 수(<see cref="Part1Skill.VolleyShots"/>) — 0 이 아니면 한 발 세기 = 세기 × 타수 ÷ 발 수(다 맞으면 합이 전과 같다).</summary>
    internal int SplitShots;

    internal bool Has(UnitState u, int sel) => Buffs.TryGetValue(u, out var b) && b.ContainsKey(sel);

    /// <summary>
    /// 파트1 상태 번호 — 유닛 바이트 +0x246 + 번호: 2 쇼크(+0x248, 쇼크 웨이브가 기계에 건다) · 14 최면(+0x254) · 15 컨디션이상(+0x255) · 16 혼란(+0x256).
    /// 쇼크와 최면은 둘 다 <b>차례를 못 받게</b> 한다(차례 고르기 <c>0x10042871</c> 이 +0x247 · +0x248 · +0x254 를 본다).
    /// </summary>
    internal const int Shock = 2, Hypnosis = 14, Condition = 15, Confusion = 16;

    /// <summary>
    /// 파트1 상태 1 동결(+0x247) — 온도(유닛 +0x4a)가 0 아래이고 SPD(<c>0x1003ece0</c>)가 4 아래면 시간 틱이 건다(<c>0x100352d7</c>, Part1MapObjects).
    /// 차례를 못 받고(<c>0x10042871</c>), AI 의 상태 기술 어림이 그 대상에게 0 이다(<c>0x1003fd44</c>). 큐어(<c>0x10042150</c>)는 안 지운다.
    /// </summary>
    internal const int Frozen = 1;

    /// <summary>온도 기술 — 화이어 필드(78~86, 땅불 · 알림 10)면 +1, 프로즌 아이스(115~123, 얼음 덩이 · 알림 11)면 −1, 아니면 0.</summary>
    internal static int TemperatureSkill(Part1Attack? attack) => attack?.Id switch { >= 78 and <= 86 => 1, >= 115 and <= 123 => -1, _ => 0 };

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

    internal static int AilmentOf(int selector) => selector switch { 13 => Shock, 27 => Hypnosis, 28 => Confusion, _ => Condition };

    /// <summary>
    /// 모두 풀기(큐어 · 아이템 107, <c>0x10042150</c>) — 14 · 15 · 16 은 지우지만 쇼크(2)는 남는다: 원본은 +0x248 을 보고서 상태 5 를 지운다
    /// (<c>0x1004223f</c> push 5, 원본 실수). 원본대로 둔다. 동결(1)도 목록에 없어 남는다(온도가 푼다). 무언가 지웠으면 true.
    /// </summary>
    internal bool CureAilments(UnitState u)
    {
        if (!Ailments.TryGetValue(u, out var set)) return false;
        bool any = set.RemoveWhere(s => s is not (Shock or Frozen)) > 0;
        if (set.Count == 0) Ailments.Remove(u);
        return any;
    }

    /// <summary>
    /// 쇼크(2) 풀림 — 빈 시간 틱 <c>0x100353a1</c>: 틱 ≠ 1 이고 틱 % 행동 간격(WTP, <c>0x1003eeb0</c>) == 1 일 때(곧 차례 간격마다 한 번)
    /// rand() % 150 ≤ (소울 + 150) ÷ 5 면 풀린다 — 소울 0 이면 31/150(21%) · 40 이면 39/150(26%) · 150 이면 61/150(41%). 맞아도 줄지 않는다.
    /// </summary>
    internal static bool ShockRecovers(int soul, int roll) => roll % 150 <= (soul + 150) / 5;

    /// <summary>AI 가 그 대상에게 선택자 13 · 25 · 27 · 28 을 쓸 만한가 — 대상 거르기(<see cref="TargetFilter(int, UnitState)"/>)를 지나고 그 상태가 없고 막는 상태도 없을 때.</summary>
    internal bool AilmentWorth(UnitState t, int selector) =>
        !HasAilment(t, Frozen) && TargetFilter(selector, t) && !HasAilment(t, AilmentOf(selector))
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
    /// 마지막으로 맞은 굴림의 셈 — 세기 · 방어(AC 또는 MR, 자세 · 막기 · 쉐도우 실드를 건 값) · 마법인가 · 속성 저항(÷10, 없으면 null) · 막았나.
    /// 타이밍 띠의 예상 피해가 왜 그렇게 나왔는지 보이려고 둔다(<see cref="BattleScene.ExpectedDamageText"/>, 사용자 요청 fg-48).
    /// </summary>
    internal (int Power, int Defense, bool Magic, int? Resist, bool Blocked)? LastHit;

    /// <summary>
    /// 파트1 자세(유닛 바이트 +0x257 방어 · +0x258 회피 · +0x259 반격 · +0x25a 역습 · +0x25b 완전방어) — 유닛 → 어빌리티 76~80.
    /// 안 움직이고 아무것도 안 한 채 휴식(명령 0x3f3 → <c>0x1003e580</c>)하면 가진 것 가운데 첫째(76 → 80 차례) 하나가 켜지고,
    /// 다음 자기 차례가 올 때 모두 지운다(<c>0x10036350</c>). 대장 · 탄 이는 안 켠다.
    /// </summary>
    internal readonly Dictionary<UnitState, int> Stances = [];

    internal int StanceOf(UnitState u) => Stances.GetValueOrDefault(u);

    /// <summary>
    /// 유닛 하나의 파트1 전투 상태 — 저장 칸(<see cref="SystemMenu.SaveUnit"/> 의 P1 칸). 원본 유닛 기록(<c>0x100340b0</c> 읽기)이 상태 바이트 +0x246~+0x25c
    /// (동결 · 쇼크 · 속성 부여 +0x249~ · 강화 · 최면 · 컨디션이상 · 혼란 · 자세 +0x257~ · 집중 +0x25c — 바이트 값이 남은 길이다)를 통째로 싣는다 —
    /// 리메이크는 그것을 나눠 든 사전(<see cref="Ailments"/> · <see cref="Buffs"/> · <see cref="Enchant"/> · <see cref="Stances"/>)을 싣는다.
    /// 강화는 (세기 식 · 남은 길이) 짝을 잇대어 적는다. 비었으면 null.
    /// </summary>
    internal (int[]? Ailments, int[]? Buffs, int? Enchant, int? Stance) SaveUnit(UnitState u) =>
        (Ailments.TryGetValue(u, out var set) && set.Count > 0 ? [.. set.Order()] : null,
         Buffs.TryGetValue(u, out var b) && b.Count > 0 ? [.. b.OrderBy(p => p.Key).SelectMany(p => new[] { p.Key, p.Value })] : null,
         Enchant.TryGetValue(u, out int e) && e != 0 ? e : null,
         Stances.TryGetValue(u, out int st) && st != 0 ? st : null);

    /// <summary>불러오기 전에 모두 비운다 — 같은 판에서 불러오면 판을 새로 안 세워 지금 상태가 남는다.</summary>
    internal void ClearUnitStates()
    {
        Enchant.Clear();
        Buffs.Clear();
        Ailments.Clear();
        Stances.Clear();
    }

    /// <summary>저장한 칸을 그 유닛에 되살린다 — 칸이 없는 옛 세이브면 아무것도 안 건다(예전처럼 모두 풀린 채).</summary>
    internal void RestoreUnit(UnitState u, int[]? ailments, int[]? buffs, int? enchant, int? stance)
    {
        if (ailments is { Length: > 0 }) Ailments[u] = [.. ailments];
        if (buffs is { Length: >= 2 })
        {
            var d = new Dictionary<int, int>();
            for (int i = 0; i + 1 < buffs.Length; i += 2) d[buffs[i]] = buffs[i + 1];
            Buffs[u] = d;
        }
        if (enchant is > 0 and var en) Enchant[u] = en;
        if (stance is > 0 and var sn) Stances[u] = sn;
    }

    /// <summary>쉬면 켜질 자세 — 어빌리티 76 · 77 · 78 · 79 · 80 가운데 가진 첫째(<c>0x1003e6e6</c>~). 없거나 파트1 인물이 아니면 0.</summary>
    internal int StanceAbility(UnitState u) =>
        Fighter(u) is { } f ? new[] { 76, 77, 78, 79, 80 }.FirstOrDefault(id => f.Chr.EffectiveLevel(id) >= 0) : 0;   // 쓰는 레벨(0x1009e020) — 지금 직업이 다루는 것만

    /// <summary>자세 이름(어빌리티 이름 — 분석-파트1-전투 24절 A).</summary>
    internal static string StanceName(int ability) => ability switch { 76 => "방어", 77 => "회피", 78 => "반격", 79 => "역습", 80 => "완전방어", _ => "" };

    /// <summary>이번 타격의 세기 식(선택자) — 기술이면 Attack +0x42, 기본 공격이면 무기 갈래의 기본 공격 칸 것(<see cref="Resolve"/> 와 같다). 모르면 0.</summary>
    internal int SelectorOf(UnitState a, WorkData w) =>
        (AttackOf(w) ?? (Fighter(a) is { } fa ? Part1Attack.ById(_attacks ??= Part1Attack.LoadAll(), BasicAttackOf(fa)) : null))?.Formula ?? 0;

    /// <summary>
    /// 피해 굴림 — 파트2 <c>GameData.Resolve</c> 와 같은 꼴(결과 2 맞음 · 3 빗나감)에 파트1 의 4 막음 · 5 피함을 더해 돌려준다. 파트1 인물이 아니면 null(파트2 식을 쓴다).
    /// </summary>
    /// <param name="aimBonus">타이밍 명중(fg-34)으로 맞춘 몫(%) — 명중 굴림의 명중률에 더한다. 전에는 파트1 판에서 안 들었다(fg-67).</param>
    internal (int Amount, int Result, bool Critical)? Resolve(Random rng, UnitState a, UnitState t, WorkData w, int aimBonus = 0)
    {
        if (!w.IsDamage || Fighter(a) is not { } fa || Fighter(t) is not { } ft) return null;
        // 기술이면 Attack.dat 의 세기 식(+0x42) · 명중률(+0x48) · 속성(+0x64) · 더하는 세기(+0x44 제곱) · 곱(+0x66)을 쓴다.
        // 상태이상 기술(선택자 10~28)은 피해가 아니어서 파트2 식으로 넘긴다. 기본 공격은 무기 갈래의 기본 공격 칸(칼 1 — 식 0 · 명중 100, 활 4 — 식 3 · 명중 90 …).
        var attack = AttackOf(w) ?? Part1Attack.ById(_attacks ??= Part1Attack.LoadAll(), BasicAttackOf(fa));
        int selector = attack?.Formula ?? 0;
        if (selector is >= 10 and <= 28) return null;
        int accuracy = attack is { Accuracy: > 0 } ? attack.Accuracy : 100;
        accuracy += aimBonus;
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
            && rng.Next(100) < Part1Combat.EvadeChance(ft.Stat(Part1Chr.Dex, t.Soul), !ft.CanEvade || stance == 77, fa.HitValue, fa.Stat(Part1Chr.Dex, a.Soul))))
            && BattleScene.Part1MissStands(rng, a, t))         // 파트1 난이도 — 적의 빗나감을 단계마다 절반(BattleScene.Difficulty)
            return Missed("회피", stance == 79 ? CounterDodge : Dodged);
        if (!Part1Combat.Hits(accuracy, rng.Next(32768)) && BattleScene.Part1MissStands(rng, a, t)) return Missed("명중 굴림");
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
        // 타수(+0x66) — 세기에 곱해 한 번에 준다(원본 AI 어림 0x10040985 와 같다). 쌍검 · 쌍권총 · 연 · 참처럼 타격 키마다 따로 치는 것은 부르는 쪽이 나눠 치고(SplitHits) 곱하지 않는다.
        if (attack != null) power = (power + attack.PowerAdd * attack.PowerAdd) * (SplitHits ? 1 : Math.Max(1, attack.Hits));
        if (attack != null && SplitHits && SplitShots > 0) power = power * Math.Max(1, attack.Hits) / SplitShots;
        int element = attack is { Element: > 0 } ? attack.Element : enchant > 0 ? enchant : fa.Weapon1?.Element ?? 0;
        bool magic = selector is 5 or 6 or 8 or (>= 10 and <= 24);
        int defense = ft.Stat(magic ? Part1Chr.Mr : Part1Chr.Ac, t.Soul);
        // 쏘기(선택자 2 · 31)는 AC + 대상 장비 보정 244(총 피해 약화) — 방어 표 0x1004182c(바이트 표 0x1004183c 의 갈래 1 → 0x1004158d).
        if (selector is 2 or 31) defense += ft.Equipment.Sum(i => i?.Bonus(Part1Bonus.GunGuard) ?? 0);
        if (!magic && t.Status(4) > 0) defense /= 2;
        if (Has(t, 22)) defense = magic ? defense / 2 : defense * 3 / 2;   // 쉐도우 실드 — AC ×1.5 · MR ×0.5
        // 아이스 실드(14, 상태 10 · +0x250)는 여기 아무것도 안 한다 — 원본에서 +0x250 을 읽는 곳은 셋뿐이다: 맞을 때 막 27:0(0x1003e249),
        // AI 어림(이미 걸렸으면 0, 0x1003fdf9), 큐어(0x100421e9). AC · MR 게터(0x1003edc0 · 0x1003ed50)와 이 함수(0x100413b0)는 안 본다.
        if (blocked) defense += ft.BlockDefense;
        if (stance == 80 && selector != 5) defense += defense / 2;           // 완전방어 자세 — 마법(식 5) 말고는 방어 ×1.5(0x100415e1)
        LastHit = (power, defense, magic, ft.Resist(element), blocked);
        int damage = Part1Combat.Damage(power, defense, ft.Resist(element), null, rng.Next(32768));
        if (Has(t, 20)) damage = selector == 5 ? 0 : damage * 2;      // 배리어 — 마법(식 5) 피해 0, 그 밖은 ×2
        // 시험용 DUELDX_P1CRIT=<%> 면 치명률을 그 값으로 덮는다(무기 덧기술을 화면 밖에서 보려고).
        // 치명률은 세기 식이 서술 +0xe 에 넣는다(0x1003f62b~) — 선택자 0~3 은 무기1, 29~31 은 무기2 의 아이템 +0x65, 그 밖(마법 · 살 4 …)은 0.
        int critRate = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_P1CRIT"), out int forced) ? forced
            : selector switch { >= 0 and <= 3 => fa.Weapon1?.Critical ?? 0, >= 29 and <= 31 => fa.Weapon2?.Critical ?? 0, _ => 0 };
        // 반격 자세면 결과는 막았어도 6 이다(0x100417c5 가 4 를 덮는다). 치명은 결과 2 · 6 에서만 — 막은 것(4)은 굴리지 않는다(0x100417e4).
        int result = stance == 78 ? CounterHit : blocked ? Blocked : 2;
        bool crit = false;
        if (result != Blocked) damage = Part1Combat.Critical(damage, critRate, rng.Next(32768), out crit);
        // 피해 %(편집기 「파트1 기술 편집」 — 원본에 없는 칸, assets/data/part1-attacks). 화이어볼1~5 · 필살 20 은 80%(명중 90%, 사용자 결정 fg-67).
        if (attack is { DamagePercent: not 100 } edited) damage = damage * edited.DamagePercent / 100;
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 hit {a.ChrCode} → {t.ChrCode}{(attack != null ? $" {attack.Name}(식 {selector})" : "")}: 세기 {power} ÷ 방어 {defense} = {damage}{(crit ? " 치명" : "")}{(blocked ? " 막음" : "")}{(stance != 0 ? $" 자세 {stance}" : "")} · 명중 {accuracy} → 결과 {result}" + Environment.NewLine);
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
