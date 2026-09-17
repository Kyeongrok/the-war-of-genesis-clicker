namespace WarOfGenesis.Assets;

/// <summary>인물이 바라보는 쪽. 오른쪽은 왼쪽 그림을 좌우로 뒤집어 쓴다.</summary>
public enum Facing { Up, Down, Left, Right }

/// <summary>몸짓 파일 안에서 한 방향의 걷기 컷 구간 — 풀어 둔 컷 목록(몸짓벌을 이어 붙인 순서)의 번호.</summary>
public readonly record struct FrameRange(int Start, int Count);

/// <summary>한 몸짓(sprite) 파일의 위·아래·왼쪽 걷기 컷 구간.</summary>
public sealed record WalkCycle(FrameRange Up, FrameRange Down, FrameRange Left)
{
    public FrameRange For(Facing facing) => facing switch
    {
        Facing.Up => Up,
        Facing.Down => Down,
        _ => Left,
    };
}

/// <summary>
/// sprite 번호별 걷기 컷 표.
/// </summary>
/// <remarks>
/// 몸짓 파일 안에 "이 컷 구간 = 이 방향 걷기"라고 적힌 자리는 아직 못 찾았다. 컷 배치도 인물마다
/// 달라서(제이슨은 32컷씩 뒷모습·옆모습·앞모습이고, 다른 인물은 제각각이다) 컷 목록을 눈으로
/// 넘겨 보며 손으로 골랐다. 표에 없는 sprite 는 첫 컷 하나로 서 있기만 한다.
/// </remarks>
public static class WalkCycles
{
    private static readonly Dictionary<int, WalkCycle> BySprite = new()
    {
        [338] = new(Up: new(0, 9), Down: new(64, 7), Left: new(32, 8)),   // 제이슨
        [347] = new(Up: new(5, 7), Down: new(56, 8), Left: new(32, 6)),   // 샤크바리
        [368] = new(Up: new(0, 6), Down: new(46, 6), Left: new(29, 6)),   // 이반
        [367] = new(Up: new(0, 10), Down: new(38, 10), Left: new(19, 11)), // 가이아버그
    };

    public static WalkCycle? Find(int spriteCode) => BySprite.GetValueOrDefault(spriteCode);
}
