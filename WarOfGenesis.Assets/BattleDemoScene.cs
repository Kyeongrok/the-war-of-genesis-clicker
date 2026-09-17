namespace WarOfGenesis.Assets;

/// <summary>전투판 위 인물 하나 — Chr 코드와 칸 자리(Col·Row), 아군인지.</summary>
public readonly record struct BattleUnit(int ChrCode, int Col, int Row, bool IsAlly);

/// <summary>
/// 정적 분석으로 찾아낸 <c>Btl/0173.btl</c> 자료 — "영혼의 검" 챕터(<c>0019.chp</c>)의 전투
/// 하나. duel-dx 데모와 <c>WarOfGenesis.Editor</c>의 전투맵 보기 창이 이 자료를 함께 쓴다.
/// </summary>
/// <remarks>
/// 이 전투가 그 챕터의 <b>"첫" 전투</b>라는 표시는 사용자가 아니라고 정정했다 — 진짜 순서는
/// 아직 다시 확인 못 했다. 배경도 <b>확인 못 한 자리표시자</b>다(어느 <c>Map</c>/<c>Bgr</c>
/// id를 쓰는지는 <c>CBattle+0xa0</c> 을 채우는 함수가 실행 시점 상태를 거쳐야 풀리는 값이라
/// 정적 분석만으론 못 찾았다).
/// </remarks>
public static class BattleDemoScene
{
    public const int BtlId = 173;
    public const int Cols = 32, Rows = 32;
    public const string PlaceholderBackgroundFile = "0200_placeholder.jpg";

    /// <summary><c>Btl/0173.btl</c> 을 파싱해서 얻은 실제 배치. 아군 7 + 적군 15 = 22명.</summary>
    public static readonly BattleUnit[] Roster =
    [
        // 아군 — 천사(266)×2, 아델룬장교(302)×4, 세큘리티볼(19)×1
        new(266, 17, 11, true), new(266, 13, 14, true),
        new(302, 7, 30, true), new(302, 8, 18, true), new(302, 10, 24, true), new(302, 3, 27, true),
        new(19, 5, 22, true),
        // 적군 — 유블레인(33)×3, 엠블라(60)×6, 시녀(54)×6
        new(33, 1, 20, false), new(33, 30, 30, false), new(33, 26, 8, false),
        new(60, 15, 8, false), new(60, 19, 10, false), new(60, 13, 11, false),
        new(60, 6, 16, false), new(60, 10, 19, false), new(60, 11, 20, false),
        new(54, 9, 21, false), new(54, 5, 17, false), new(54, 11, 22, false),
        new(54, 16, 14, false), new(54, 15, 10, false), new(54, 11, 13, false),
    ];
}
