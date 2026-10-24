using System.Text;

namespace WarOfGenesis.Assets;

/// <summary>
/// 파트1 아이템 <c>Dats\Item.dat</c> 한 칸(파일 204바이트) — 로더 <c>alpha.dll 0x100a7400</c>.
/// </summary>
/// <param name="Bonuses">보정 짝 셋(코드, 값) — 코드 0xc8~0xd4 는 능력치·저항(<see cref="Part1Chr.StatSource"/>), 300 대는 특수 효과로 보인다.</param>
/// <param name="Attack">메모리 <c>+0x54</c> — 피해 세기 식의 ATK.</param>
/// <param name="Defense">메모리 <c>+0x58</c> — 막기에 성공하면 방어에 더한다.</param>
/// <param name="Critical">메모리 <c>+0x65</c> — 치명률(어쌔신 대거 25).</param>
/// <param name="Element">메모리 <c>+0x6c</c> — 무기 속성(0 없음).</param>
/// <param name="ElementPower">메모리 <c>+0x6e</c> — 세기 식의 E(제곱해 더한다).</param>
/// <param name="Weight">메모리 <c>+0x6f</c> — 세기 식의 W(DEX·SKILL 과 곱한다).</param>
public sealed record Part1Item(int Id, string Name, string EnglishName, int Price, int Type, (int Code, int Value)[] Bonuses,
                               int Attack, int Defense, int Critical, int Element, int ElementPower, int Weight, string Description)
{
    public const int Size = 204;

    public int Bonus(int code) => Bonuses.Where(b => b.Code == code).Sum(b => b.Value);

    public static IReadOnlyList<Part1Item> LoadAll() => ParseAll(Part1Game.Read("Dats", "Item.dat"));

    /// <summary>파일: u16 수, 아이템마다 이름 30 · 영문 30(비트 반전 cp949) · 값 44바이트 · 설명 100.</summary>

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
            items.Add(new Part1Item(i, Text(o, 30), Text(o + 30, 30), I32(0), b[p + 4], bonuses,
                                    Attack: I32(18), Defense: I32(22), Critical: b[p + 35], Element: b[p + 40],
                                    ElementPower: b[p + 42], Weight: b[p + 43], Description: Text(p + 44, 100)));
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

    /// <summary>회복(선택자 6) — 최대 HP × 세기 / 20000, 잃은 HP 까지. 결과는 양수(HP 에 더할 값).</summary>
    public static int Heal(int power, int maxHp, int hp) => Math.Min(maxHp * (power / 50) / 400, maxHp - hp);

    /// <summary>기력 게이지(유닛 +0xe8, 0~150)에 따른 능력치 — 값 − 값·⅔·(150 − 게이지)/150(<c>0x1003ec10</c>). 0 이면 ⅓.</summary>
    public static int Scaled(int stat, int gauge) => Math.Max(0, stat + (-2 * stat / 3) * (150 - gauge) / 150);

    /// <summary>차례가 올 때 게이지 — +20(상태 +0x24c 면 +40), 150(상태에 따라 120·50)에서 자른다(<c>0x10034f80</c>). 처음 값은 40.</summary>
    public static int GaugeOnTurn(int gauge, bool doubled = false, int cap = 150) => Math.Min(cap, gauge + (doubled ? 40 : 20));

    /// <summary>회피 확률(%) — 대상이 어빌리티 77 을 가졌을 때. 상태 +0x258 이 없으면 대상 DEX 는 반만 친다.</summary>
    public static int EvadeChance(int targetDex, bool fullDex, int attackerHit, int attackerDex) =>
        attackerHit + attackerDex > 0 ? (fullDex ? targetDex : targetDex / 2) * 100 / (attackerHit + attackerDex) : 0;
}
