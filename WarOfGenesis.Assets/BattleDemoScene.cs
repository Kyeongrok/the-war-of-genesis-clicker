namespace WarOfGenesis.Assets;

/// <summary>전투판 위 인물 하나 — Chr 코드와 칸 자리(Col·Row), 아군인지.</summary>
public readonly record struct BattleUnit(int ChrCode, int Col, int Row, bool IsAlly);

/// <summary>
/// <c>Btl/0045.btl</c> 자료 — 코어헌터 훈련장의 첫 전투. duel-dx 데모와
/// <c>WarOfGenesis.Editor</c>의 전투맵 보기 창이 이 자료를 함께 쓴다.
/// </summary>
/// <remarks>
/// DOSBox(Win95)에서 실제 게임을 그 전투판까지 진행한 뒤 메모리를 훑어 찾았다 — Win95 파일
/// 캐시에 남은 <c>Btl.pak</c> 페이지 두 장에 걸친 파일이 0045 하나뿐이었다(분석 노트
/// <c>분석-첫전투.md</c>). 아군 칸은 파티 자리로 보인다 — 파일에는 제이슨·샤크바리·이반이
/// 적혀 있지만 게임 화면에는 제이슨·살라딘·죠안이 섰다. 여기서는 파일에 적힌 그대로 쓴다.
/// 배경은 여전히 <b>확인 못 한 자리표시자</b>다(헤더 두 번째 워드 61이 <c>Map/0061.map</c>
/// 일 수 있지만 미확인).
/// </remarks>
public static class BattleDemoScene
{
    public const int BtlId = 45;
    public const string Title = "코어헌터 훈련장";
    public const int Cols = 32, Rows = 32;
    public const string PlaceholderBackgroundFile = "0200_placeholder.jpg";

    /// <summary><c>Btl/0045.btl</c> 의 29바이트 유닛 레코드 6개. 아군 3 + 적군 3 = 6명.</summary>
    public static readonly BattleUnit[] Roster =
    [
        // 아군 — 제이슨(221), 샤크바리(219), 이반(62)
        new(221, 2, 20, true), new(219, 3, 18, true), new(62, 5, 17, true),
        // 적군 — 가이아버그(193)×3
        new(193, 22, 27, false), new(193, 22, 16, false), new(193, 16, 26, false),
    ];
}
