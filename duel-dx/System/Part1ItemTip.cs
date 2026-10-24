using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 아이템 설명 글 — 이름 · Item.dat 설명(100바이트) · 효과 목록을 <c>$n</c> 줄로 엮는다(<see cref="StatusScreen.DrawDescriptionTip"/> 에 넣는다).
/// </summary>
/// <remarks>
/// 원본 설명 글은 괄호 안에 효과를 적어 두기도 하지만 빠진 것이 많다(공격 · 명중 · 치명 · 속성 값은 없다) — 그래서 레코드 값과 보정 짝(<see cref="Part1Bonus"/>)에서 효과 줄을 따로 엮는다.
/// 약 · 치료약의 효과는 전투 중 쓰기(<see cref="BattleScene.UsePart1Item"/>)와 같은 뜻: 보정 100 = HP 회복(9999 는 전부), 102~107 = 상태 치료.
/// </remarks>
internal static class Part1ItemTip
{
    private static IReadOnlyList<Part1Attack>? _attacks;

    private static readonly string[] ElementNames = ["", "화염", "빙한", "전격", "회복", "암흑"];

    /// <summary>아이템 갈래(+0x44) 이름 — <see cref="Part1Item.CanEquip"/> 의 갈래 표.</summary>
    internal static string TypeName(int type) => type switch
    {
        1 => "단검", 2 => "세검", 3 => "장검", 4 => "곡도", 5 => "대검", 6 => "빔소드", 7 => "활", 8 => "권총", 9 => "소총", 10 => "빔라이플", 11 => "창",
        12 => "써크렛", 13 => "방패", 14 => "갑옷", 15 => "장신구", 16 => "스코프", 17 => "코팅", 18 => "필름", 20 => "회복약", 21 => "귀중품",
        22 => "필름 장검", 23 => "코팅", 24 => "치료약", 25 => "약", 26 => "옷", _ => "",
    };

    private static readonly string[] OnHit = ["메테오", "라이트닝 볼츠", "썬더스탐", "버써크", "중독", "최면", "상태이상", "혼란", "마비", "암살"];

    private static string Signed(int v) => v >= 0 ? $"+{v}" : $"−{-v}";

    /// <summary>설명 글 — 이름(갈래) · 설명 · 효과 줄. 아이템이 없으면 null.</summary>
    internal static string? Text(Part1Item? item)
    {
        if (item == null) return null;
        var lines = new List<string>();
        string type = TypeName(item.Type);
        lines.Add(type.Length > 0 ? $"{item.Name}  ({type})" : item.Name);
        string desc = item.Description.Replace("\r", "").Replace("\n", "$n").Trim();
        if (desc.Length > 0 && !desc.StartsWith('-')) lines.Add(desc);

        var stats = new List<string>();
        bool weapon = item.Type is >= 1 and <= 11 or 22 || item.Attack > 0;
        if (item.Attack > 0) stats.Add($"공격 {item.Attack}");
        if (item.Defense > 0) stats.Add($"방어 {item.Defense}");
        if (weapon && item.Accuracy > 0) stats.Add($"명중 {item.Accuracy}");
        if (weapon && item.Critical > 0) stats.Add($"치명 {item.Critical}%");
        if (weapon && item.Weight > 0) stats.Add($"무게 {item.Weight}");
        if (weapon && item.Element is > 0 and < 6) stats.Add($"{ElementNames[item.Element]} 속성");
        if (weapon && item.ElementPower > 0) stats.Add($"속성 세기 {item.ElementPower}");

        var bonus = new List<string>();
        var effects = new List<string>();
        foreach (var (code, value) in item.Bonuses)
        {
            switch (code)
            {
                case Part1Bonus.Dex: bonus.Add($"DEX {Signed(value)}"); break;
                case Part1Bonus.Str: bonus.Add($"STR {Signed(value)}"); break;
                case Part1Bonus.Ac: bonus.Add($"AC {Signed(value)}"); break;
                case Part1Bonus.Int: bonus.Add($"INT {Signed(value)}"); break;
                case Part1Bonus.Mr: bonus.Add($"MR {Signed(value)}"); break;
                case Part1Bonus.Spd: bonus.Add($"SPD {Signed(value)}"); break;
                case Part1Bonus.Range: bonus.Add($"사거리 {Signed(value / 4)}"); break;   // 원본 설명 글이 4 를 한 칸으로 적는다(스코프 4 = RANGE+1)
                case Part1Bonus.Accuracy: bonus.Add($"명중 {Signed(value)}"); break;
                case >= Part1Bonus.FireResist and <= Part1Bonus.DarkResist:
                    // 저항 칸(10 이 보통)에 더한다 — 음수일수록 덜 받는다.
                    bonus.Add($"{ElementNames[code - Part1Bonus.FireResist + 1]} 피해 {Signed(value * 10)}%"); break;
                case >= 213 and <= 215: effects.Add($"무기에 {ElementNames[code - 212]} 속성"); break;
                case Part1Bonus.GunGuard: effects.Add("총 피해 약화"); break;
                case Part1Bonus.NeverMissed: effects.Add("맞는 적이 못 피한다"); break;
                case Part1Bonus.StatusGuard: effects.Add("상태이상 막기"); break;
                case >= Part1Bonus.OnHitFirst and <= Part1Bonus.OnHitLast: effects.Add($"맞히면 때때로 {OnHit[code - Part1Bonus.OnHitFirst]}"); break;
                case Part1Bonus.HpRegen: effects.Add($"차례마다 HP +{value}"); break;
                case Part1Bonus.SoulRegen: effects.Add($"차례마다 소울 +{value}"); break;
                case 100: effects.Add(value >= 9999 ? "HP 전부 회복" : $"HP +{value} 회복"); break;
                case 102: effects.Add("버써크 치료"); break;
                case 103: effects.Add("혼란 치료"); break;
                case 104: effects.Add("중독 치료"); break;
                case 105: effects.Add("최면 치료"); break;
                case 106: effects.Add("컨디션 이상 치료"); break;
                case 107: effects.Add("모든 상태이상 치료"); break;
                default:
                    if (Part1Bonus.UnlockedAbility(code) is > 0 and var ability && Part1Ability.ById(Part1Ability.LoadAll(), ability) is { } ab)
                        effects.Add($"「{ab.Name}」 사용 가능");
                    break;
            }
        }
        if (item.Type is 20 or 24 or 25) effects.Add("전투 중 ITEM 으로 쓴다(차례가 끝난다)");
        else if (item.Type == 18) effects.Add("1회용");

        if (stats.Count + bonus.Count + effects.Count > 0) lines.Add("");
        if (stats.Count > 0) lines.Add(string.Join(" · ", stats));
        if (bonus.Count > 0) lines.Add(string.Join(" · ", bonus));
        if (effects.Count > 0) lines.Add(string.Join(" · ", effects));
        if (item.Price > 0) lines.Add($"값 {item.Price} eld");
        return string.Join("$n", lines);
    }
}
