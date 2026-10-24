using System.Text;

namespace WarOfGenesis.Assets;

/// <summary>
/// 파트1 아이템 <c>Dats\Item.dat</c> 한 칸(파일 204바이트) — 로더 <c>alpha.dll 0x100a7400</c>.
/// </summary>
/// <param name="Bonuses">보정 짝 셋(코드, 값) — 코드 0xc8~0xd4 는 능력치·저항(<see cref="Part1Chr.StatSource"/>), 300 대는 특수 효과로 보인다.</param>
/// <param name="Attack">메모리 <c>+0x54</c> — 피해 세기 식의 ATK.</param>
/// <param name="Defense">메모리 <c>+0x58</c> — 막기에 성공하면 방어에 더한다.</param>
/// <param name="Accuracy">메모리 <c>+0x60</c> — 명중값(회피·막기 식의 분모, 보정 0xcf 를 더한다).</param>
/// <param name="Critical">메모리 <c>+0x65</c> — 치명률(%) — 물리 기술이 rand()%100 &lt; 이 값이면 피해 × 2(어쌔신 대거 25).</param>
/// <param name="Element">메모리 <c>+0x6c</c> — 무기 속성(0 없음).</param>
/// <param name="ElementPower">메모리 <c>+0x6e</c> — 세기 식의 E(제곱해 더한다).</param>
/// <param name="Weight">메모리 <c>+0x6f</c> — 세기 식의 W(DEX·SKILL 과 곱한다).</param>
public sealed record Part1Item(int Id, string Name, string EnglishName, int Price, int Type, (int Code, int Value)[] Bonuses,
                               int Attack, int Defense, int Accuracy, int Critical, int Element, int ElementPower, int Weight, string Description)
{
    public const int Size = 204;

    /// <summary>그림 바이트(메모리 <c>+0x45</c>) — 상태 · 교환 창의 아이템 칸이 Obs 12 의 이 모션을 그린다(분석-파트1-챕터 13.5).</summary>
    public int Icon { get; init; }

    /// <summary>
    /// 사거리 값(메모리 <c>+0x68</c>, 파일 +36) — 4분의 1칸 단위(칸 거르개 <c>0x1008a950</c> 가 4 × 맨해튼 + 높이 보정과 견준다).
    /// 칼 · 빔소드 · 맨손 갈래 8 · 창 12 · 권총 28~40 · 소총 16~64 · 활 52 · 72 · 빔라이플 · 천궁 9999. 기본 공격 사거리 갈래 2 · 4 가 쓴다(<c>0x10038ae0</c>).
    /// </summary>
    public int Reach { get; init; }

    public int Bonus(int code) => Bonuses.Where(b => b.Code == code).Sum(b => b.Value);

    /// <summary>
    /// 무기 덧기술 — 보정 300~309 가 덧붙이는 <c>Attack.dat</c> 번호(<c>alpha.dll 0x10042990</c>, 값 v 는 <c>max(v−1, 0)</c> 만큼 더한다).
    /// 300 메테오 87 · 301 라이트닝 볼츠 134+ · 302 썬더스탐 161 · 303 버써크 208+ · 304 중독 383+ · 305 최면 386+ · 306 상태이상 385+ · 307 혼란 384+ · 308 마비 388 · 309 암살 389.
    /// 없으면 (0, 0).
    /// </summary>
    /// <summary>
    /// 파트1 상태 → 파트2 상태 번호(가까운 것, 가설): 중독 → 3 · 버써크 → 4 · 마비 → 5.
    /// 키는 무기 보정(300대)과 치료 보정(100대) 둘 다 받는다. 최면(305/105) · 컨디션이상(306/106) · 혼란(307/103)은 파트2 상태로 바꾸지 않는다 —
    /// 전투가 파트1 상태로 따로 든다(duel-dx Part1Rules.Ailments, 분석-파트1-전투 25절). 그래서 0.
    /// </summary>
    public static int Part2Status(int code) => code switch
    {
        304 or 104 => 3,
        303 or 102 => 4,
        308 => 5,
        _ => 0,
    };

    /// <summary>
    /// 이 무기로 치는 기본 공격(<c>Attack.dat</c> 번호) — 1 칼공격 · 2 창공격 · 3 빔소드공격 · 4 활공격 · 5 단발사격 · 10 빔라이플공격.
    /// 무기 갈래(+0x44)로 고른다: 6 빔소드 → 3 · 7 활 → 4 · 8 권총 · 9 소총 → 5 · 10 빔라이플 → 10 · 11 창 → 2 · 나머지(칼 · 맨손) → 1.
    /// 가설 — 원본이 이 짝을 고르는 자리는 못 찾았다(Attack.dat 1~10 의 이름과 사거리 갈래가 갈래별 무기와 맞다).
    /// </summary>
    public static int BasicAttack(Part1Item? weapon) => weapon?.Type switch
    {
        6 => 3, 7 => 4, 8 or 9 => 5, 10 => 10, 11 => 2, _ => 1,
    };

    /// <summary>쏘는 기본 공격인가(활 · 총 · 빔라이플) — 파트2 의 원거리 기본 공격 work 를 빌린다.</summary>
    public static bool ShootsBasic(Part1Item? weapon) => BasicAttack(weapon) is 4 or 5 or 10;

    public (int Code, int Attack) ProcAttack()
    {
        foreach (var (code, value) in Bonuses)
        {
            int up = Math.Max(value - 1, 0);
            int attack = code switch
            {
                300 => 87, 301 => 134 + up, 302 => 161, 303 => 208 + up, 304 => 383 + up,
                305 => 386 + up, 306 => 385 + up, 307 => 384 + up, 308 => 388, 309 => 389, _ => 0,
            };
            if (attack > 0) return (code, attack);
        }
        return (0, 0);
    }

    /// <summary>
    /// 이 아이템을 그 인물의 장비 칸에 낄 수 있나 — <c>alpha.dll 0x1009e260(칸, 아이템)</c>.
    /// 갈래(+0x44)마다 그 무기를 다루는 어빌리티가 하나라도 있어야 하고(1 단검→14 · 2 세검→10 · 3 장검→11 · 4 곡도→12 · 5 대검→13 · 6 빔소드→15 ·
    /// 7 활→19 · 8 권총→16 · 9 소총→17 · 10 빔라이플→18 · 11 창→20 · 12 써크렛→21 · 13 방패→14·10·11 · 14 갑옷→22 · 16 스코프→16·17 · 18 필름→11 ·
    /// 22 → 11 · 23 → 10·14·11·13·12·20), 칸마다 받는 갈래가 있다: 0 무기(두손 5·7·11 은 둘째 손이 비어야) · 1 둘째 손(첫 손이 두손·총이면 안 됨,
    /// 칼은 어빌리티 128 쌍검술이 있어야, 방패는 첫 손이 칼일 때, 스코프는 첫 손이 총·활일 때, 필름은 첫 손이 22 일 때) · 2 몸(14 · 15 · 17 · 23 · 26) · 3·4 장신구(15 · 17 · 23).
    /// 19 · 20 · 21 · 24 · 25(약 · 소모품 · 열쇠)는 못 낀다.
    /// </summary>
    /// <param name="equipment">그 인물의 지금 장비 다섯 칸(아이템 번호).</param>
    public bool CanEquip(Part1Chr chr, int slot, int[] equipment, IReadOnlyList<Part1Item> items)
    {
        bool Has(params int[] abilities) => abilities.Any(a => chr.AbilityLevel(a) >= 0);
        int TypeOf(int id) => ById(items, id)?.Type ?? 0;
        int t = Type;
        bool learned = t switch
        {
            19 or 20 or 21 or 24 or 25 => false,
            1 => Has(14), 2 => Has(10), 3 => Has(11), 4 => Has(12), 5 => Has(13), 6 => Has(15), 7 => Has(19), 8 => Has(16), 9 => Has(17),
            10 => Has(18), 11 => Has(20), 12 => Has(21), 13 => Has(14, 10, 11), 14 => Has(22), 16 => Has(17, 16), 18 => Has(11), 22 => Has(11),
            23 => Has(10, 14, 11, 13, 12, 20),
            _ => true,
        };
        if (!learned) return false;
        int first = TypeOf(equipment[0]);
        switch (slot)
        {
            case 0:
                if (t is not (>= 1 and <= 12 or 22)) return false;
                return t is not (5 or 7 or 11) || equipment[1] == 0;
            case 1:
                if (first is 5 or 7 or 9 or 10 or 11) return false;
                if (t is 0 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 14 or 15 or 17 or 22 or 23 or 26) return false;
                if (t is >= 1 and <= 4 && !Has(128)) return false;
                if (t == 13) return first is >= 1 and <= 4 or 22;
                if (t == 18) return first == 22;
                if (t == 16) return first is 7 or 8 or 9;
                return true;
            case 2: return t is 14 or 15 or 17 or 23 or 26;
            default: return t is 15 or 17 or 23;
        }
    }

    /// <summary>번호로 찾는다(<see cref="LoadAll"/> 의 목록은 번호 1 부터) — 0·범위 밖은 null.</summary>
    public static Part1Item? ById(IReadOnlyList<Part1Item> items, int id) => id >= 1 && id <= items.Count ? items[id - 1] : null;

    public static IReadOnlyList<Part1Item> LoadAll() => ParseAll(Part1Game.Read("Dats", "Item.dat"));

    /// <summary>파일: u16 수, 아이템마다 이름 30 · 영문 30(비트 반전 cp949) · 값 44바이트 · 설명 100. <b>아이템 번호 = 레코드 + 1</b> — 메모리 0 번은 「없음」 빈 칸이다(<c>0x100a7480</c>).</summary>

    public static IReadOnlyList<Part1Item> ParseAll(byte[]? b)
    {
        if (b is not { Length: >= 2 }) return [];
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        string Text(int o, int n)
        {
            var plain = new byte[n];
            for (int i = 0; i < n; i++) plain[i] = (byte)~b[o + i];
            int end = Array.IndexOf(plain, (byte)0);
            return cp949.GetString(plain, 0, end < 0 ? n : end);
        }
        int count = BitConverter.ToUInt16(b, 0);
        var items = new List<Part1Item>(count);
        for (int i = 0, o = 2; i < count && o + Size <= b.Length; i++, o += Size)
        {
            int p = o + 60;
            int U16(int at) => BitConverter.ToUInt16(b, p + at);
            int S16(int at) => BitConverter.ToInt16(b, p + at);
            int I32(int at) => BitConverter.ToInt32(b, p + at);
            // 파일 차례: u32 값 · u8 종류 · u8 · (코드, 값, 코드, 값, 코드, 값) 이 아니라 코드 +0x46 · 값 +0x4c · 코드 +0x48 · 값 +0x4e · 코드 +0x4a · 값 +0x50
            (int, int)[] bonuses = [.. new[] { (U16(6), S16(8)), (U16(10), S16(12)), (U16(14), S16(16)) }.Where(x => x.Item1 != 0)];
            items.Add(new Part1Item(i + 1, Text(o, 30), Text(o + 30, 30), I32(0), b[p + 4], bonuses,
                                    Attack: I32(18), Defense: I32(22), Accuracy: I32(30), Critical: b[p + 35], Element: b[p + 40],
                                    ElementPower: b[p + 42], Weight: b[p + 43], Description: Text(p + 44, 100)) { Icon = b[p + 5], Reach = I32(36) });
        }
        return items;
    }
}

/// <summary>
/// 파트1 전투 식 — 피해 세기(<c>0x1003f3c0</c>)와 피해 적용(<c>0x100413b0</c>). 옵시디안 「분석-파트1-전투」 9·10절.
/// </summary>
/// <remarks>
/// 파트2 와 꼴이 다르다 — 파트1 은 <b>피해 = 세기 / 방어</b>(AC·MR 로 나눈다)이고, 회피·막기는 대상이 어빌리티 77 회피 · 76 방어 · 80 완전방어를 가졌을 때만 굴린다.
/// 여기는 회피·막기를 뺀 셈만 둔다(그 둘은 유닛 상태가 필요하다).
/// </remarks>
public static class Part1Combat
{
    /// <summary>세기 식에 드는 때린 이 값 — 능력치는 어빌리티·장비 보정이 다 들어간 값이다.</summary>
    public readonly record struct Attacker(int Str, int Skill, int Dex, int Int, Part1Item? Weapon1, Part1Item? Weapon2, int Brightness = 0, bool Enchanted = false);

    /// <summary>피해 세기 — 선택자는 <c>Attack.dat +0x42</c>. 32 밖은 0.</summary>
    public static int Power(int selector, Attacker a)
    {
        int atk1 = a.Weapon1?.Attack ?? 0, w1 = a.Weapon1?.Weight ?? 0, e1 = Element(a.Weapon1, a.Enchanted);
        int atk2 = a.Weapon2?.Attack ?? 0, w2 = a.Weapon2?.Weight ?? 0, e2 = a.Weapon2?.ElementPower ?? 0;
        return selector switch
        {
            0 or 7 => (atk1 + 10) * a.Str + e1 * e1,
            1 => (a.Dex + a.Skill) * w1 / 2 + e1 * e1,
            2 => (w1 + atk1 + 20) * a.Skill / 2 + e1 * e1,
            3 => w1 * a.Skill / 2 + atk1 * a.Str / 2 + e1 * e1,
            4 => (a.Dex + a.Skill) * w1 / 3 + atk1 * a.Str / 3 + e1 * e1,
            5 or 6 or 8 or (>= 10 and <= 24) => a.Int * a.Int,
            9 => (a.Brightness - 160) * 20 / 40 + 1,                        // 유닛 +0x4a — 틱마다 머리 낱말 6(밝기) 쪽으로 간다
            29 => (atk2 + 10) * a.Str + e2 * e2,
            30 => (a.Dex + a.Skill) * w2 / 2 + e2 * e2,
            31 => (atk2 + w2) * a.Skill / 2,
            _ => 0,
        };
    }

    /// <summary>무기 E — 속성 부여 상태(유닛 +0x249~+0x24b)면 ×120/100(<c>0x1003f000</c>).</summary>
    private static int Element(Part1Item? weapon, bool enchanted) => weapon is null ? 0 : enchanted ? weapon.ElementPower * 120 / 100 : weapon.ElementPower;

    /// <summary>
    /// 회피·막기를 지난 뒤의 피해 — 방어 <paramref name="defense"/>(AC·MR, 방어 자세면 ×1.5 한 값)로 나누고 속성 저항(−10~20)을 ×/10, ±1/6 흔든다.
    /// <paramref name="roll"/> 은 rand() 한 번(0~32767).
    /// </summary>
    /// <param name="resist">서술 +3 속성(1~5)의 저항 칸 30~34 — 속성이 없으면 null.</param>
    /// <param name="resist2">서술 +4 둘째 속성(1~4)의 칸 35~38 — 없으면 null.</param>
    public static int Damage(int power, int defense, int? resist, int? resist2, int roll)
    {
        if (resist is { } r1) power = power * r1 / 10;
        if (resist2 is { } r2) power = power * r2 / 10;
        int damage = defense != 0 ? power / defense : power;
        int r = damage / 3;
        if (r != 0) damage += roll % r - damage / 6;
        return damage;
    }

    /// <summary>
    /// 쓰러뜨린 보상 — 맞은 이가 HP 0 이 되면 때린 이에게 메시지 0x10(값 = 죽은 이 칸 18 경험치)이 가고(<c>0x1003de76</c>),
    /// 때린 이가 편 4 이고 Chr &lt; 106 이면 경험치를 받는다(<c>0x10035908</c>~, 탈것·탄 이는 짝에게 넘긴다).
    /// 경험치 = ⌊죽은 이 경험치 / 100⌋ × 2000 / (때린 이 레벨 × 100) — 곧 죽은 이 레벨 × 20 / 때린 이 레벨(레벨 0 이면 죽은 이 경험치 × 20).
    /// 돈(eld, 전투 <c>+0xb95c</c>) = 때린 이 Luck × 죽은 이 경험치 / 1000.
    /// </summary>
    public static (int Exp, int Eld) KillReward(int victimExp, int attackerExp, int attackerLuck)
    {
        int levelBase = attackerExp / 100 * 100;
        int exp = levelBase != 0 ? victimExp / 100 * 2000 / levelBase : victimExp * 20;
        return ((short)exp, attackerLuck * victimExp / 20 / 50);
    }

    /// <summary>명중 굴림 — rand()%100 &lt; 기술 명중률(<see cref="Part1Attack.Accuracy"/>)이면 맞는다. 못 맞으면 결과 3(<c>0x10041050</c>).</summary>
    public static bool Hits(int accuracy, int roll) => roll % 100 < accuracy;

    /// <summary>치명 — 피해가 양수인 보통 맞음에서 rand()%100 &lt; 치명률이면 × 2(<c>0x100417ec</c>). 치명률은 선택자 0~3 이 무기1, 29~31 이 무기2 것이다.</summary>
    public static int Critical(int damage, int criticalRate, int roll, out bool critical)
    {
        critical = damage > 0 && roll % 100 < criticalRate;
        return critical ? damage * 2 : damage;
    }

    /// <summary>회복(선택자 6) — 최대 HP × 세기 / 20000, 잃은 HP 까지. 결과는 양수(HP 에 더할 값).</summary>
    public static int Heal(int power, int maxHp, int hp) => Math.Min(maxHp * (power / 50) / 400, maxHp - hp);

    /// <summary>소울 게이지(유닛 +0xe8, 0~150 — 보정 401 「소울치 추가 회복」이 더해지는 값)에 따른 능력치 — 값 − 값·⅔·(150 − 게이지)/150(<c>0x1003ec10</c>). 0 이면 ⅓.</summary>
    public static int Scaled(int stat, int gauge) => Math.Max(0, stat + (-2 * stat / 3) * (150 - gauge) / 150);

    /// <summary>차례가 올 때 게이지 — +20(상태 +0x24c 면 +40), 150(상태에 따라 120·50)에서 자른다(<c>0x10034f80</c>). 처음 값은 40.</summary>
    public static int GaugeOnTurn(int gauge, bool doubled = false, int cap = 150) => Math.Min(cap, gauge + (doubled ? 40 : 20));

    /// <summary>
    /// 상태이상 기술(선택자 10~28)이 걸리나 — <c>0x10041960</c>. 대상이 보정 247 을 가졌으면 실패(같은 편 상대 갈래 빼고).
    /// <paramref name="roll"/> 은 rand() 한 번, <paramref name="blockedByStatus"/> 는 선택자 25·26(상태 +0x254·+0x256)·28(+0x254)이 막히는 대상 상태가 걸려 있는가.
    /// </summary>
    public static bool StatusLands(int selector, int power, int targetMr, int targetLuck, bool sameSide, bool statusGuard, bool blockedByStatus, int roll)
    {
        int mr = Math.Max(1, targetMr);
        switch (selector)
        {
            case >= 10 and <= 14 or 18 or (>= 22 and <= 24):
                return sameSide || (!statusGuard && roll % 400 < power / mr);
            case >= 15 and <= 17 or 20:
                return true;
            case 19 or 21:
                return !statusGuard && roll % 200 < (100 - targetLuck) * power / mr / 100;
            case 25 or 26 or 28:
                return !statusGuard && !blockedByStatus && roll % 100 < 100 - targetLuck;
            case 27:
                return !statusGuard && roll % 100 < 100 - targetLuck;
            default:
                return false;
        }
    }

    /// <summary>회피 확률(%) — 대상이 어빌리티 77 을 가졌을 때. 상태 +0x258 이 없으면 대상 DEX 는 반만 친다.</summary>
    public static int EvadeChance(int targetDex, bool fullDex, int attackerHit, int attackerDex) =>
        attackerHit + attackerDex > 0 ? (fullDex ? targetDex : targetDex / 2) * 100 / (attackerHit + attackerDex) : 0;

    /// <summary>
    /// 막기 — <c>0x10040eb0</c>: 어빌리티 76 이면 (대상 두 손 명중 + DEX, 자세 +0x257 이 없으면 ½) × 150 / (때린 이 명중값 + DEX) &gt; rand()%100,
    /// 80 이면 × 200(자세 +0x25b 면 늘). <paramref name="roll"/> 은 rand()%100.
    /// </summary>
    public static bool Blocks(int ability, int guard, int attackerHit, int attackerDex, int roll)
    {
        int against = attackerHit + attackerDex;
        if (ability == 0 || against <= 0) return false;
        return (ability == 76 ? guard / 2 * 150 : guard * 200) / against > roll;
    }
}

/// <summary>
/// 파트1 싸우는 이 — 인물 레코드와 장비 다섯(칸 39~43)에서 쓰는 능력치를 셈한다(바탕 + 어빌리티 레벨 × 5 + 장비 보정, <c>0x1009eaa0</c>~).
/// </summary>
/// <remarks>살라딘은 칸 39·40 에 다마스커스 둘(쌍검) — 둘째 무기가 선택자 29~31 의 W2 다.</remarks>
public sealed record Part1Fighter(Part1Chr Chr, Part1Item?[] Equipment)
{
    public static Part1Fighter From(Part1Chr chr, IReadOnlyList<Part1Item> items) => new(chr, [.. chr.Equipment.Select(id => Part1Item.ById(items, id))]);

    public Part1Item? Weapon1 => Equipment.Length > 0 ? Equipment[0] : null;
    public Part1Item? Weapon2 => Equipment.Length > 1 ? Equipment[1] : null;

    private int EquipBonus(int code) => code > 0 ? Equipment.Sum(i => i?.Bonus(code) ?? 0) : 0;

    public int Stat(int field) => Chr.Stat(field) + EquipBonus(Part1Chr.StatSource(field).Bonus);

    /// <summary>소울 게이지(0~150)로 깎은 능력치(<see cref="Part1Combat.Scaled"/>) — 싸울 때 쓰는 값.</summary>
    public int Stat(int field, int gauge) => Part1Combat.Scaled(Stat(field), Math.Clamp(gauge, 0, 150));

    public Part1Combat.Attacker AttackerAt(int gauge) =>
        new(Stat(Part1Chr.Str, gauge), Stat(Part1Chr.Skill, gauge), Stat(Part1Chr.Dex, gauge), Stat(Part1Chr.Int, gauge), Weapon1, Weapon2);

    /// <summary>명중값 — 무기1 명중 + 보정 207(회피 식의 분모).</summary>
    public int HitValue => (Weapon1?.Accuracy ?? 0) + EquipBonus(Part1Bonus.Accuracy);

    /// <summary>
    /// 무기 사거리(4분의 1칸) — 무기(장비 칸 0) 아이템 <c>+0x68</c>(<see cref="Part1Item.Reach"/>). 사거리 갈래 2 는 여기에 장비 보정 206(RANGE)을 더하고,
    /// 4 는 기술 값 <c>+0x24</c> 를 더한다(<c>0x10038ae0</c>). 칸 수는 ÷ 4 — 분석-파트1-전투 29절(전에는 「모두 8 · ÷ 2 칸」으로 읽었다).
    /// </summary>
    public int WeaponReach => Weapon1?.Reach ?? 0;

    /// <summary>RANGE 능력치(칸 29 + 장비 보정 206) — 사거리 갈래 1 이 기술 값에 더한다(<c>0x1003eea0</c>, 소울 게이지로 깎지 않는다).</summary>
    public int RangeStat => Stat(Part1Chr.Range);

    /// <summary>장비 보정 206(RANGE) 합 — 사거리 갈래 2 가 무기 사거리에 더한다(<c>0x1009e180(0xce)</c>).</summary>
    public int RangeBonus => EquipBonus(Part1Bonus.Range);

    /// <summary>속성 1~5 저항(칸 30~34, 10 이 보통) + 장비 보정 — 속성이 없으면 null.</summary>
    public int? Resist(int element) => element is >= 1 and <= 5 ? Chr.Fields[30 + element - 1] + EquipBonus(Part1Bonus.ResistOf(element)) : null;

    /// <summary>어빌리티 77 회피 — 이것이 있어야 회피를 굴린다.</summary>
    public bool CanEvade => Chr.AbilityLevel(77) >= 0;

    /// <summary>막기 — 어빌리티 76 방어(대상 값 ½ · ×150) 또는 80 완전방어(×200)를 가졌을 때만 굴린다(<c>0x10040eb0</c>). 0 없음.</summary>
    public int BlockAbility => Chr.AbilityLevel(76) >= 0 ? 76 : Chr.AbilityLevel(80) >= 0 ? 80 : 0;

    /// <summary>막기 식의 대상 쪽 값 — 두 손 아이템 명중(<c>+0x60</c>) + 보정 207 둘(<c>0x1003f240</c>). 여기에 DEX 를 더한다.</summary>
    public int GuardValue => (Weapon1?.Accuracy ?? 0) + (Weapon2?.Accuracy ?? 0) + 2 * EquipBonus(Part1Bonus.Accuracy);

    /// <summary>막으면 방어에 더하는 값 — 두 손 아이템 <c>+0x58</c>(<c>0x1003f220</c>).</summary>
    public int BlockDefense => (Weapon1?.Defense ?? 0) + (Weapon2?.Defense ?? 0);

    /// <summary>장비 가운데 그 보정 코드를 단 것이 있나(<c>0x1009e120</c> — 개수를 센다).</summary>
    public bool HasBonus(int code) => Equipment.Any(i => i?.Bonuses.Any(b => b.Code == code) == true);
}

/// <summary>
/// 파트1 아이템 보정 코드(<see cref="Part1Item.Bonuses"/>) — 아이템 설명 글과 짝을 맞춘 것(옵시디안 「분석-파트1-전투」 9.1).
/// </summary>
public static class Part1Bonus
{
    public const int Dex = 200, Str = 201, Ac = 202, Int = 203, Mr = 204, Spd = 205, Range = 206, Accuracy = 207;

    /// <summary>속성 저항 — 화염 · 빙한 · 전격 · 회복 · 암흑(속성 번호 1~5 차례). 값이 음수일수록 덜 받는다.</summary>
    public const int FireResist = 208, IceResist = 209, ThunderResist = 210, HolyResist = 211, DarkResist = 212;

    /// <summary>총 피해 약화 — 총 기술의 방어에 더한다.</summary>
    public const int GunGuard = 244;

    /// <summary>회피 불능 — 때린 이가 가졌으면 회피·막기를 굴리지 않는다.</summary>
    public const int NeverMissed = 246;

    public const int StatusGuard = 247;

    /// <summary>맞힌 대상에게 — 300 메테오 · 301 라이트닝 볼츠 · 302 썬더스탐 · 303 버써크 · 304 중독 · 305 최면 · 306 상태이상 · 307 혼란 · 308 마비 · 309 암살.</summary>
    public const int OnHitFirst = 300, OnHitLast = 309;

    /// <summary>차례 시작에 HP · 소울(유닛 +0xe8)에 더한다.</summary>
    public const int HpRegen = 400, SoulRegen = 401;

    /// <summary>
    /// 기술 해금 코드 216~238 → 쓸 수 있게 되는 <b>어빌리티</b>(Abi.dat) 번호(<c>0x1009dc6c</c> — 직업이 가르친 어빌리티 목록에 덧붙인다). 249 는 101.
    /// 전에는 Attack.dat 번호로 읽어 앤젤 블래스(232 → 어빌리티 48 블래스)가 「광3」으로 보였다(사용자 보고).
    /// </summary>
    public static readonly int[] UnlockAbilities = [154, 155, 156, 23, 24, 26, 27, 33, 34, 38, 40, 39, 43, 44, 47, 49, 48, 50, 55, 53, 57, 59, 56];

    /// <summary>그 보정 코드가 여는 어빌리티 번호 — 해금 코드가 아니면 0.</summary>
    public static int UnlockedAbility(int code) => code switch { >= 216 and <= 238 => UnlockAbilities[code - 216], 249 => 101, _ => 0 };

    /// <summary>속성 번호(1~5) → 저항 보정 코드.</summary>
    public static int ResistOf(int element) => element is >= 1 and <= 5 ? FireResist + element - 1 : 0;
}
