namespace WarOfGenesis.Assets;

/// <summary>
/// 사거리·효과 범위의 모양 표(1~9, 데모 추가 10) — 게임(<c>BattleScene.WorkArea</c>)과 편집기의 범위 미리보기가 같이 쓴다.
/// 방향은 0 위 · 1 아래 · 2 왼쪽 · 3 오른쪽.
/// </summary>
public static class WorkShape
{
    /// <summary>바라보는 쪽을 앞(axis)으로, 그 직각을 옆(side)으로 돌려 놓는다.</summary>
    public static (int Axis, int Side) Turn(int dx, int dy, int facing) => facing switch
    {
        0 => (-dy, dx),
        1 => (dy, dx),
        2 => (-dx, dy),
        _ => (dx, dy),
    };

    /// <summary>모양이 그 칸을 덮나(거리는 따로 본다).</summary>
    public static bool Covers(int shape, int dx, int dy, int facing)
    {
        var (axis, side) = Turn(dx, dy, facing);
        int a = Math.Abs(side);
        return shape switch
        {
            1 => true,                                  // 마름모(거리만)
            2 => dx == 0 || dy == 0,                    // 십자
            3 => axis >= 0 && a <= axis / 3,            // 부채꼴 — 원점 줄도 덮는다
            4 => true,                                  // 화면 전체
            5 => axis >= 0 && side == 0,                // 직선
            6 => axis >= 0 && a <= 1,                   // 폭 3 줄(시전자 줄도 덮는다 — work_area.py 그림)
            7 => axis >= 0 && a <= 2,                   // 폭 5 줄
            8 => Math.Abs(dx) == Math.Abs(dy),          // 대각선 X
            9 => axis >= 1 && a <= axis - 1,            // 45° 삼각형 — 앞으로 한 칸 간 뒤부터 벌어진다
            // 10 = ^ 호(데모 추가, 원본 표는 9 까지) — 겨눈 칸 기준 한 칸 더 앞과 양옆. 파(gm-skil-14)가 옆 칸을 겨누면 시전자 기준
            // 12시(두 칸 앞)·11시·1시(대각선)가 된다. 겨눈 칸 자체는 안 든다.
            10 => axis == 1 && side == 0 || axis == 0 && a == 1,
            _ => dx == 0 && dy == 0,
        };
    }

    /// <summary>방향을 쓰는 모양인가 — 사거리를 그릴 때는 네 방향을 합쳐야 한다.</summary>
    public static bool UsesFacing(int shape) => shape is 3 or 5 or 6 or 7 or 9 or 10;

    /// <summary>
    /// 그 모양이 <b>앞으로 간 거리만</b> 재는가 — 3·5·6·7·9 가 그렇다(<c>0x100dafe0</c>·<c>0x100db310</c>).
    /// 옆으로 벌어진 만큼은 거리에 안 들어가므로, 맨해튼으로 자르면 바깥 줄이 통째로 잘려 나간다.
    /// </summary>
    public static bool UsesAxisDistance(int shape) => shape is 3 or 5 or 6 or 7 or 9;

    /// <summary>
    /// 모양과 거리를 함께 본다 — 모양마다 <b>거리 자가 다르기</b> 때문에 따로 볼 수 없다. 거리는 4분의 1칸 단위,
    /// <paramref name="graded"/> · <paramref name="plain"/> 은 높이항까지 넣어 잰 맨해튼 거리(평지면 둘 다 4 × (|dx| + |dy|)).
    /// </summary>
    public static bool Reaches(int shape, int dx, int dy, int facing, int minQuarters, int maxQuarters, int graded, int plain)
    {
        if (!Covers(shape, dx, dy, facing)) return false;
        if (!UsesAxisDistance(shape)) return plain >= minQuarters && graded <= maxQuarters;
        // 축 거리에도 <b>높이항은 그대로 붙는다</b>(0x100db2bf~) — 맨해튼 몫만 축 거리로 바꾸고, 높이 몫은 이미 잰 값에서 가져온다.
        int axis = 4 * Turn(dx, dy, facing).Axis;
        int flat = 4 * (Math.Abs(dx) + Math.Abs(dy));
        return axis + (plain - flat) >= minQuarters && axis + (graded - flat) <= maxQuarters;
    }
}
