using System.ComponentModel;
using System.Reflection;

namespace WarOfGenesis.Assets;

/// <summary>
/// 대상 방식 — work <c>+0x13</c>(targetMode)·효과 대상 <c>+0x1e</c>(areaMode)가 같은 값을 쓴다(분석-전투 「대상 방식」, 분석-스킬 8.2).
/// </summary>
public enum TargetMode : byte
{
    [Description("0 자기 자리(범위 없음)")] SelfNoArea = 0,
    [Description("1 적")] Foe = 1,
    [Description("2 자기 자리(자기 중심 광역)")] Self = 2,
    [Description("3 아무 칸")] AnyCell = 3,
    [Description("4 아군")] Ally = 4,
    [Description("5 아무 유닛")] AnyUnit = 5,
    [Description("6 아무 칸(높이 6 고정)")] AnyCellFlat = 6,
    [Description("7 빈 칸")] EmptyCell = 7,
    [Description("8 오브젝트")] Object = 8,
}

/// <summary>사거리·효과 범위 모양 — work <c>+0x7</c>(rangeShape)·<c>+0x14</c>(areaShape), 모양 함수 <c>0x100db640</c> 등.</summary>
public enum AreaShape : byte
{
    [Description("0 없음(자기 자리)")] None = 0,
    [Description("1 마름모")] Diamond = 1,
    [Description("2 십자")] Cross = 2,
    [Description("3 부채꼴")] Fan = 3,
    [Description("4 화면 전체")] Screen = 4,
    [Description("5 직선")] Line = 5,
    [Description("6 폭 3 줄")] Line3 = 6,
    [Description("7 폭 5 줄")] Line5 = 7,
    [Description("8 대각선 X")] DiagonalX = 8,
    [Description("9 45° 삼각형")] Triangle = 9,
}

/// <summary>사거리 종류 — work <c>+0x8</c>(rangeKind).</summary>
public enum RangeKind : byte
{
    [Description("0 없음")] None = 0,
    [Description("1 자기 값")] Own = 1,
    [Description("2 무기 사거리")] Weapon = 2,
    [Description("3 자기 값")] Own3 = 3,
    [Description("4 무기 + 자기 값")] WeaponPlusOwn = 4,
}

/// <summary>종류 — work <c>+0x1f</c>(kind).</summary>
public enum WorkKind : byte
{
    [Description("0 피해")] Damage = 0,
    [Description("1 회복")] Heal = 1,
    [Description("2 보조")] Support = 2,
    [Description("3 보조")] Support3 = 3,
    [Description("4 오브젝트")] Object = 4,
    [Description("5 회복")] Heal5 = 5,
}

/// <summary>enum 값과 보기 이름(<see cref="DescriptionAttribute"/>) — 편집기 드롭다운에 쓴다.</summary>
public static class EnumChoices
{
    public sealed record Choice(int Value, string Label);

    public static IReadOnlyList<Choice> Of(Type enumType) =>
    [
        .. Enum.GetValues(enumType).Cast<object>().Select(v => new Choice(Convert.ToInt32(v),
            enumType.GetField(v.ToString()!)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? v.ToString()!)),
    ];
}
