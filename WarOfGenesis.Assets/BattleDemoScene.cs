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
/// <c>분석-첫전투.md</c>). 아군은 죠안·살라딘·제이슨으로, 게임 화면에 선 세 명과 같다.
///
/// 배경 맵은 <c>.btl</c> 헤더 두 번째 워드(61) → <c>Map/0061.map</c> 의 두 번째 워드(153) →
/// <c>Obt/0153.obt</c> 로 이어 찾았다. 그림이 게임 화면의 훈련장(갈색 유기체 바닥, 돌기둥, 붉은 꽃)과
/// 같은 곳이고 칸 수(32×34)도 배치 좌표 범위와 맞지만, 이 이어짐을 DLL 에서 확인하지는 않았다.
/// </remarks>
public static class BattleDemoScene
{
    public const int BtlId = 45;
    public const string Title = "코어헌터 훈련장";
    public const int Cols = 32, Rows = 34;

    /// <summary>배경 맵 — <c>assets/maps/0153.obt</c>.</summary>
    public const string MapFile = "0153.obt";

    /// <summary><c>Btl/0045.btl</c> 의 29바이트 유닛 레코드 6개. 아군 3 + 적군 3 = 6명.</summary>
    public static readonly BattleUnit[] Roster =
    [
        // 아군 — 죠안(221), 살라딘(219), 제이슨(62)
        new(221, 2, 20, true), new(219, 3, 18, true), new(62, 5, 17, true),
        // 적군 — 가이아리더(193)×3
        new(193, 22, 27, false), new(193, 22, 16, false), new(193, 16, 26, false),
    ];
}
