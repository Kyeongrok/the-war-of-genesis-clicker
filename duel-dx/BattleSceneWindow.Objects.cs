namespace DuelDx;

/// <summary>
/// 전투판에 놓인 물체 — 상자·문·포탑·바리케이트(분석-전투 「물체(오브젝트) 배열 <c>+0x3c74</c>」).
/// </summary>
/// <remarks>
/// 물체는 <b>파일이 놓는 것이 전부</b>다 — 전투 중에 새로 생기지 않는다. 길은 안 막고, <b>문(종류 1·4)만</b>
/// 걸어 들어갈 목표 칸이 될 수 없다(<c>0x100746b0</c>). 데모는 아직 그리기와 목표 칸 막기까지만 한다 —
/// 상자 열기(work 386)·부수기·포탑의 차례는 아직이다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private IReadOnlyList<DemoObject> Objects => _scene.Objects ?? [];

    /// <summary>그 칸에 선 물체 — 없으면 null.</summary>
    private DemoObject? ObjectAt(int col, int row) =>
        Objects.FirstOrDefault(o => o.Alive && o.Col == col && o.Row == row);

    /// <summary>그 칸이 물체 때문에 설 수 없는 칸인가 — 문과 스위치문뿐이다.</summary>
    private bool ObjectBlocks(int col, int row) => ObjectAt(col, row) is { Data.BlocksStanding: true };

    /// <summary>물체를 칸에 그린다 — 인물보다 먼저(뒤에) 그려 인물이 앞에 서게 한다.</summary>
    private void DrawObjects()
    {
        foreach (var obj in Objects)
        {
            if (!obj.Alive || obj.Data.SpriteId <= 0) continue;
            int x = obj.Col * TileW + TileW / 2, y = GridTop + obj.Row * TileH + TileH;
            DrawUi(obj.Data.SpriteId, 0, (int)(_lastTime * TicksPerSecond), x, y, UiBlend.Alpha);
        }
    }
}
