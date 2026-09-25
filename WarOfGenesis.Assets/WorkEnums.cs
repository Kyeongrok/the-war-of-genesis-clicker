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

/// <summary>
/// 효과 번호 — work <c>+0x1c·+0x1f·+0x22</c>(bonus1~3Stat). 맞힌 대상에게 거는 상태이상(Sta.dat 번호)이고,
/// 30·31·32·33·37·48 은 상태이상 칸이 아니라 전투 끝까지 쌓이는 능력치 보정이다(<c>0x1007c210</c>, 분석-전투 6절).
/// 값(bonusNValue)이 세기·변화량이다 — 예: 31 에 −10 이면 PSY −10.
/// 28·34·35·36·39 는 거는 함수가 그냥 칸에 넣지만 DLL 어디서도 그 번호를 검사하지 않아(검사 함수 <c>0x1007bf20</c>·<c>0x1007bb10</c>·
/// <c>0x1007bec0</c> 를 부르는 곳 전부) 아무 효과가 없다. Sta.dat 에도 28·34·35·36 은 레코드가 없고 39 는 「없음」이다.
/// </summary>
public enum StatusEffect : byte
{
    [Description("0 없음")] S0 = 0,
    [Description("1 DEX 저하")] S1 = 1,
    [Description("2 화염")] S2 = 2,
    [Description("3 중독")] S3 = 3,
    [Description("4 버서커")] S4 = 4,
    [Description("5 마비")] S5 = 5,
    [Description("6 빙결")] S6 = 6,
    [Description("7 피해 감소")] S7 = 7,
    [Description("8 자동 회복")] S8 = 8,
    [Description("9 소울 습득")] S9 = 9,
    [Description("10 피격 가속")] S10 = 10,
    [Description("11 경험치 증가")] S11 = 11,
    [Description("12 어빌 봉인")] S12 = 12,
    [Description("13 공격력 변화")] S13 = 13,
    [Description("14 방어력 변화")] S14 = 14,
    [Description("15 소울 소모")] S15 = 15,
    [Description("16 TP 소모")] S16 = 16,
    [Description("17 체력 소모")] S17 = 17,
    [Description("18 소울 비용")] S18 = 18,
    [Description("19 소울 정지")] S19 = 19,
    [Description("20 TP 비용")] S20 = 20,
    [Description("21 소울 습득 변화")] S21 = 21,
    [Description("22 소울 사망")] S22 = 22,
    [Description("23 TP 사망")] S23 = 23,
    [Description("24 소울 만사망")] S24 = 24,
    [Description("25 이동 불가")] S25 = 25,
    [Description("26 휴식 불가")] S26 = 26,
    [Description("27 악세사리 무시")] S27 = 27,
    [Description("28 효과 없음(원본도 안 읽음)")] S28 = 28,
    [Description("29 무기 공격력")] S29 = 29,
    [Description("30 DEX 변화(보정)")] S30 = 30,
    [Description("31 PSY 변화(보정)")] S31 = 31,
    [Description("32 DEP 변화(보정)")] S32 = 32,
    [Description("33 최대 TP 변화(보정)")] S33 = 33,
    [Description("34 효과 없음(원본도 안 읽음)")] S34 = 34,
    [Description("35 효과 없음(원본도 안 읽음)")] S35 = 35,
    [Description("36 효과 없음(원본도 안 읽음)")] S36 = 36,
    [Description("37 최대 SOUL 변화(보정)")] S37 = 37,
    [Description("38 턴 속도")] S38 = 38,
    [Description("39 효과 없음(원본도 안 읽음)")] S39 = 39,
    [Description("40 DEP 저하")] S40 = 40,
    [Description("41 반사")] S41 = 41,
    [Description("42 EXP 증가")] S42 = 42,
    [Description("43 소울 증가")] S43 = 43,
    [Description("44 칸 비우기")] S44 = 44,
    [Description("45 칸 비우기")] S45 = 45,
    [Description("46 칸 비우기")] S46 = 46,
    [Description("47 전투불능 방지")] S47 = 47,
    [Description("48 최대 HP 변화(보정)")] S48 = 48,
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
